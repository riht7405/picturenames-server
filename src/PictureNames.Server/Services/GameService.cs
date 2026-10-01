using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Entities;
using PictureNames.Server.Services.Dto;
using System.Text.Json;

namespace PictureNames.Server.Services;

public class GameService
{
    private readonly AppDbContext _db;
    private readonly TurnTimerService _timer;
    private readonly VoteService _vote;

    public GameService(AppDbContext db, TurnTimerService timer, VoteService vote)
    {
        _db = db;
        _timer = timer;
        _vote = vote;
    }

    // === Генерация поля ===

    public async Task GenerateFieldAsync(Guid roomId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Teams)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        var starting = Random.Shared.Next(2) == 0 ? TeamColor.Blue : TeamColor.Red;
        var other = starting == TeamColor.Blue ? TeamColor.Red : TeamColor.Blue;

        var colors = new List<CardColor>();
        colors.AddRange(Enumerable.Repeat(
            starting == TeamColor.Blue ? CardColor.Blue : CardColor.Red, 9));
        colors.AddRange(Enumerable.Repeat(
            other == TeamColor.Blue ? CardColor.Blue : CardColor.Red, 8));
        colors.AddRange(Enumerable.Repeat(CardColor.Neutral, 7));
        colors.Add(CardColor.Assassin);

        for (int i = colors.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (colors[i], colors[j]) = (colors[j], colors[i]);
        }

        var pack = room.Settings.ImagePack ?? "default";

        var poolImages = await _db.Images
            .Where(i => i.Tags == pack && i.IsPublic)
            .ToListAsync(ct);

        if (poolImages.Count < 25)
            throw new InvalidOperationException(
                $"В наборе «{pack}» только {poolImages.Count} картинок, нужно минимум 25. " +
                $"Положи больше в wwwroot/images/{pack}/ и перезапусти сервер.");

        var images = poolImages
            .OrderBy(_ => Random.Shared.Next())
            .Take(25)
            .ToList();

        var old = _db.Cards.Where(c => c.RoomId == roomId);
        _db.Cards.RemoveRange(old);

        for (int i = 0; i < 25; i++)
        {
            _db.Cards.Add(new Card
            {
                RoomId = roomId,
                ImageId = images[i].Id,
                Position = i,
                Color = colors[i],
                IsRevealed = false
            });
        }

        room.State = RoomState.InGame;
        var startingTeam = room.Teams.First(t => t.Color == starting);
        room.CurrentTurnTeamId = startingTeam.Id;
        room.GuessCount = 0;
        room.GuessesAllowed = 0;
        room.ClueWord = null;
        room.ClueNumber = null;
        room.ClueTeamId = null;

        room.TurnNumber = 0;
        ApplyTurnTimer(room, room.Settings);

        await _db.SaveChangesAsync(ct);
        ScheduleTimerFor(room);
    }

    // === Подсказка ===

    public async Task GiveClueAsync(Guid roomId, Guid playerId, string word, int number, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.InGame)
            throw new InvalidOperationException("Партия не идёт.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        if (player.Role != PlayerRole.Spymaster)
            throw new InvalidOperationException("Подсказку даёт только спаймастер.");

        if (player.TeamId != room.CurrentTurnTeamId)
            throw new InvalidOperationException("Сейчас не ваш ход.");

        if (room.ClueWord is not null)
            throw new InvalidOperationException("В этом ходу подсказка уже дана.");

        if (string.IsNullOrWhiteSpace(word))
            throw new InvalidOperationException("Подсказка не может быть пустой.");

        if (number < 1 || number > 9)
            throw new InvalidOperationException("Число должно быть от 1 до 9.");

        var team = room.Teams.First(t => t.Id == player.TeamId!.Value);

        room.ClueWord = word.Trim();
        room.ClueNumber = number;
        room.ClueTeamId = player.TeamId;
        room.GuessesAllowed = number;
        room.GuessCount = 0;

        if (room.Settings.TimerEnabled
            && room.TurnDeadlineUtc.HasValue
            && room.TurnStartedAtUtc.HasValue)
        {
            var bS = room.TurnNumber == 0
                ? room.Settings.FirstSpymasterSeconds
                : room.Settings.SpymasterSeconds;
            var bO = room.Settings.OperativeSeconds;

            var turnStart = DateTime.SpecifyKind(room.TurnStartedAtUtc.Value, DateTimeKind.Utc);
            var hardCap = turnStart.AddSeconds(bS + bO);
            var natural = DateTime.UtcNow.AddSeconds(bO);
            room.TurnDeadlineUtc = natural < hardCap ? natural : hardCap;
        }

        _db.Moves.Add(new Move
        {
            RoomId = roomId,
            PlayerId = playerId,
            Type = MoveType.Clue,
            Payload = JsonSerializer.Serialize(new
            {
                word = room.ClueWord,
                number,
                team = team.Color.ToString()
            })
        });

        await _db.SaveChangesAsync(ct);
        ScheduleTimerFor(room);
    }

    // === Открытие карты ===

    // playerId == null → системное открытие (по голосованию)
    public async Task<RevealResult> RevealCardAsync(
        Guid roomId, Guid? playerId, Guid cardId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .Include(r => r.Cards)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.InGame)
            throw new InvalidOperationException("Партия не идёт.");

        if (playerId.HasValue)
        {
            var player = room.Players.FirstOrDefault(p => p.Id == playerId.Value)
                ?? throw new InvalidOperationException("Игрок не найден.");

            if (player.Role != PlayerRole.Operative)
                throw new InvalidOperationException("Открывать карты может только оперативник.");

            if (player.TeamId != room.CurrentTurnTeamId)
                throw new InvalidOperationException("Сейчас не ваш ход.");

            if (room.ClueWord is null)
                throw new InvalidOperationException("Сначала дождись подсказки.");
        }
        else
        {
            if (room.ClueWord is null)
                throw new InvalidOperationException("Нельзя открывать без подсказки.");
        }

        var card = room.Cards.FirstOrDefault(c => c.Id == cardId)
            ?? throw new InvalidOperationException("Карта не найдена.");

        if (card.IsRevealed)
            throw new InvalidOperationException("Карта уже открыта.");

        _vote.ClearSession(roomId);

        card.IsRevealed = true;
        room.GuessCount++;

        var currentTeam = room.Teams.First(t => t.Id == room.CurrentTurnTeamId!.Value);
        var currentTeamColor = currentTeam.Color;

        RevealOutcome outcome;
        int bonusApplied = 0;

        if (card.Color == CardColor.Assassin)
        {
            outcome = RevealOutcome.Assassin;
            room.State = RoomState.Finished;
        }
        else if (CardColorMatchesTeam(card.Color, currentTeamColor))
        {
            outcome = RevealOutcome.Correct;
            currentTeam.Score++;

            if (room.Settings.BonusPerCorrectSeconds > 0 && room.TurnDeadlineUtc.HasValue)
            {
                bonusApplied = room.Settings.BonusPerCorrectSeconds;
                var deadline = DateTime.SpecifyKind(room.TurnDeadlineUtc.Value, DateTimeKind.Utc);
                room.TurnDeadlineUtc = deadline.AddSeconds(bonusApplied);
            }

            var totalOwnCards = room.Cards.Count(c => CardColorMatchesTeam(c.Color, currentTeamColor));
            if (currentTeam.Score >= totalOwnCards)
                room.State = RoomState.Finished;
        }
        else if (card.Color == CardColor.Neutral)
        {
            outcome = RevealOutcome.Neutral;
            EndTurn(room, room.Settings);
        }
        else
        {
            outcome = RevealOutcome.WrongTeam;

            var ownerColor = card.Color == CardColor.Blue ? TeamColor.Blue : TeamColor.Red;
            var ownerTeam = room.Teams.First(t => t.Color == ownerColor);

            ownerTeam.Score++;

            var ownerTotal = room.Cards.Count(c => CardColorMatchesTeam(c.Color, ownerColor));
            if (ownerTeam.Score >= ownerTotal)
            {
                room.State = RoomState.Finished;
            }
            else
            {
                EndTurn(room, room.Settings);
            }
        }

        _db.Moves.Add(new Move
        {
            RoomId = roomId,
            PlayerId = playerId,
            Type = MoveType.Guess,
            Payload = JsonSerializer.Serialize(new
            {
                cardId,
                color = card.Color.ToString(),
                outcome = outcome.ToString(),
                bonus = bonusApplied
            })
        });

        await _db.SaveChangesAsync(ct);
        ScheduleTimerFor(room);

        return new RevealResult(outcome, bonusApplied);
    }

    // === Завершение хода вручную ===

    public async Task EndTurnAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.InGame)
            throw new InvalidOperationException("Партия не идёт.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        if (player.Role != PlayerRole.Operative)
            throw new InvalidOperationException("Завершить ход может только оперативник.");

        if (player.TeamId != room.CurrentTurnTeamId)
            throw new InvalidOperationException("Сейчас не ваш ход.");

        if (room.ClueWord is null)
            throw new InvalidOperationException("Сначала дождись подсказки.");

        _db.Moves.Add(new Move
        {
            RoomId = roomId,
            PlayerId = playerId,
            Type = MoveType.EndTurn,
            Payload = null
        });

        EndTurn(room, room.Settings);
        await _db.SaveChangesAsync(ct);
        ScheduleTimerFor(room);
    }

    // Системное завершение (по голосованию)
    public async Task<bool> EndTurnSystemAsync(Guid roomId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Teams)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        if (room is null || room.State != RoomState.InGame) return false;
        if (room.ClueWord is null) return false;

        _db.Moves.Add(new Move
        {
            RoomId = roomId,
            PlayerId = null,
            Type = MoveType.EndTurn,
            Payload = null
        });

        EndTurn(room, room.Settings);
        await _db.SaveChangesAsync(ct);
        ScheduleTimerFor(room);
        return true;
    }

    // === Автоматическое завершение хода по таймеру ===

    public async Task<bool> EndTurnAutoAsync(Guid roomId, DateTime expectedDeadlineUtc, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Teams)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        if (room is null || room.State != RoomState.InGame) return false;
        if (room.TurnDeadlineUtc is null) return false;

        var stored = DateTime.SpecifyKind(room.TurnDeadlineUtc.Value, DateTimeKind.Utc);
        var expected = DateTime.SpecifyKind(expectedDeadlineUtc, DateTimeKind.Utc);

        var diff = Math.Abs((stored - expected).TotalSeconds);
        if (diff > 1.0) return false;

        EndTurn(room, room.Settings);
        await _db.SaveChangesAsync(ct);
        ScheduleTimerFor(room);
        return true;
    }

    // === Возврат в лобби ===

    public async Task ResetToLobbyAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        var cards = _db.Cards.Where(c => c.RoomId == roomId);
        _db.Cards.RemoveRange(cards);

        foreach (var t in room.Teams)
            t.Score = 0;

        room.State = RoomState.Lobby;
        room.CurrentTurnTeamId = null;
        room.GuessCount = 0;
        room.GuessesAllowed = 0;
        room.ClueWord = null;
        room.ClueNumber = null;
        room.ClueTeamId = null;

        room.TurnNumber = 0;
        room.TurnStartedAtUtc = null;
        room.SpymasterDeadlineUtc = null;
        room.TurnDeadlineUtc = null;

        foreach (var p in room.Players)
        {
            if (p.Role == PlayerRole.Spectator)
                p.Role = PlayerRole.Operative;
        }

        await _db.SaveChangesAsync(ct);
        _timer.CancelTimer(roomId);
        _vote.ClearSession(roomId);
    }

    // === Сборка состояния ===

    public async Task<GameStateForOperativeDto> GetForOperativeAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var state = await BuildStateAsync(roomId, playerId, ct);
        var snapshot = state.VoteSnapshot;

        bool canSeeVoterDetails = state.YourTeam.HasValue
            && state.CurrentTurnTeam.HasValue
            && state.YourTeam.Value == state.CurrentTurnTeam.Value;

        var cards = state.Cards
            .Select(c =>
            {
                List<VoteInfoDto> voters;
                int count;

                if (snapshot is not null
                    && snapshot.Kind == VoteService.VoteTargetKind.Card
                    && snapshot.CardId == c.Id)
                {
                    count = snapshot.Voters.Count;
                    voters = canSeeVoterDetails
                        ? snapshot.Voters
                            .Select(pid => new VoteInfoDto(
                                pid,
                                snapshot.VoterColors.TryGetValue(pid, out var col) ? col : "#ffffff",
                                pid == playerId))
                            .ToList()
                        : new List<VoteInfoDto>();
                }
                else
                {
                    count = 0;
                    voters = new List<VoteInfoDto>();
                }

                return new CardForOperativeDto(
                    c.Id, c.Position, c.ImageUrl, c.AltText,
                    c.IsRevealed ? c.Color : null,
                    c.IsRevealed,
                    voters,
                    count
                );
            })
            .ToList();

        var activeVote = BuildActiveVote(snapshot, state.OperativesCount, playerId, canSeeVoterDetails);

        return new GameStateForOperativeDto(
            state.RoomId, state.Code, state.State, state.CurrentTurnTeam,
            state.YourTeam, state.YourRole, state.IsHost,
            state.CurrentClue, state.GuessesMade, state.GuessesAllowed,
            cards, state.Teams,
            DateTime.UtcNow,
            EnsureUtc(state.TurnDeadlineUtc),
            EnsureUtc(state.SpymasterDeadlineUtc),
            activeVote,
            state.Players,
            state.ClueHistory
        );
    }

    public async Task<GameStateForSpymasterDto> GetForSpymasterAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var state = await BuildStateAsync(roomId, playerId, ct);
        var snapshot = state.VoteSnapshot;

        bool canSeeVoterDetails = state.YourTeam.HasValue
            && state.CurrentTurnTeam.HasValue
            && state.YourTeam.Value == state.CurrentTurnTeam.Value;

        var cards = state.Cards
            .Select(c =>
            {
                List<VoteInfoDto> voters;
                int count;

                if (snapshot is not null
                    && snapshot.Kind == VoteService.VoteTargetKind.Card
                    && snapshot.CardId == c.Id)
                {
                    count = snapshot.Voters.Count;
                    voters = canSeeVoterDetails
                        ? snapshot.Voters
                            .Select(pid => new VoteInfoDto(
                                pid,
                                snapshot.VoterColors.TryGetValue(pid, out var col) ? col : "#ffffff",
                                pid == playerId))
                            .ToList()
                        : new List<VoteInfoDto>();
                }
                else
                {
                    count = 0;
                    voters = new List<VoteInfoDto>();
                }

                return new CardForSpymasterDto(
                    c.Id, c.Position, c.ImageUrl, c.AltText, c.Color, c.IsRevealed,
                    voters,
                    count
                );
            })
            .ToList();

        var activeVote = BuildActiveVote(snapshot, state.OperativesCount, playerId, canSeeVoterDetails);

        return new GameStateForSpymasterDto(
            state.RoomId, state.Code, state.State, state.CurrentTurnTeam,
            state.YourTeam, state.YourRole, state.IsHost,
            state.CurrentClue, state.GuessesMade, state.GuessesAllowed,
            cards, state.Teams,
            DateTime.UtcNow,
            EnsureUtc(state.TurnDeadlineUtc),
            EnsureUtc(state.SpymasterDeadlineUtc),
            activeVote,
            state.Players,
            state.ClueHistory
        );
    }

    private record InternalCard(Guid Id, int Position, string ImageUrl, string? AltText, CardColor Color, bool IsRevealed);

    private record InternalState(
        Guid RoomId, string Code, RoomState State,
        TeamColor? CurrentTurnTeam, TeamColor? YourTeam, PlayerRole YourRole,
        bool IsHost,
        ClueDto? CurrentClue, int GuessesMade, int GuessesAllowed,
        List<InternalCard> Cards, List<TeamDto> Teams,
        DateTime? TurnDeadlineUtc, DateTime? SpymasterDeadlineUtc,
        VoteService.VoteSessionSnapshot? VoteSnapshot,
        int OperativesCount,
        List<PlayerInGameDto> Players,
        List<ClueHistoryItemDto> ClueHistory
    );

    private async Task<InternalState> BuildStateAsync(Guid roomId, Guid playerId, CancellationToken ct)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .Include(r => r.Cards).ThenInclude(c => c.Image)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден в комнате.");

        var currentTurnTeam = room.Teams
            .FirstOrDefault(t => t.Id == room.CurrentTurnTeamId)?.Color;

        var yourTeam = player.TeamId.HasValue
            ? room.Teams.FirstOrDefault(t => t.Id == player.TeamId.Value)?.Color
            : null;

        ClueDto? clue = null;
        if (room.ClueWord is not null && room.ClueNumber.HasValue && room.ClueTeamId.HasValue)
        {
            var clueTeam = room.Teams.FirstOrDefault(t => t.Id == room.ClueTeamId.Value);
            if (clueTeam is not null)
                clue = new ClueDto(room.ClueWord, room.ClueNumber.Value, clueTeam.Color);
        }

        var cards = room.Cards
            .OrderBy(c => c.Position)
            .Select(c => new InternalCard(
                c.Id, c.Position, c.Image.Url, c.Image.AltText, c.Color, c.IsRevealed
            ))
            .ToList();

        var teamsList = room.Teams.ToList();
        var teamColors = teamsList.ToDictionary(t => t.Id, t => t.Color);

        var blueTotal = room.Cards.Count(c => c.Color == CardColor.Blue);
        var redTotal = room.Cards.Count(c => c.Color == CardColor.Red);

        var teams = teamsList
            .OrderBy(t => t.Color)
            .Select(t => new TeamDto(
                t.Id, t.Color, t.Score, t.SpymasterId,
                t.Color == TeamColor.Blue ? blueTotal : redTotal))
            .ToList();

        // Игроки — все, кто в комнате
        var playerDtos = room.Players
            .OrderBy(p => p.JoinedAt)
            .Select(p => new PlayerInGameDto(
                p.Id,
                p.Nickname,
                p.Role,
                p.TeamId.HasValue && teamColors.TryGetValue(p.TeamId.Value, out var tc) ? tc : null,
                p.IsConnected,
                p.VoteColor,
                p.IsHost
            ))
            .ToList();

        // История подсказок (последние 50, в порядке возрастания времени)
        var clueMoves = await _db.Moves
            .Where(m => m.RoomId == roomId && m.Type == MoveType.Clue)
            .OrderByDescending(m => m.At)
            .Take(50)
            .ToListAsync(ct);
        clueMoves.Reverse();

        var spymasterIds = clueMoves
            .Where(m => m.PlayerId.HasValue)
            .Select(m => m.PlayerId!.Value)
            .Distinct()
            .ToList();

        var spymasterNames = room.Players
            .Where(p => spymasterIds.Contains(p.Id))
            .ToDictionary(p => p.Id, p => p.Nickname);

        var clueHistory = new List<ClueHistoryItemDto>();
        foreach (var m in clueMoves)
        {
            if (string.IsNullOrEmpty(m.Payload)) continue;
            try
            {
                using var doc = JsonDocument.Parse(m.Payload);
                var root = doc.RootElement;

                if (!root.TryGetProperty("word", out var wordEl)) continue;
                if (!root.TryGetProperty("number", out var numEl)) continue;
                if (!root.TryGetProperty("team", out var teamEl)) continue;

                var word = wordEl.GetString() ?? "";
                var num = numEl.GetInt32();
                var teamStr = teamEl.GetString() ?? "";

                if (string.IsNullOrEmpty(word)) continue;
                if (!Enum.TryParse<TeamColor>(teamStr, out var teamColor)) continue;

                clueHistory.Add(new ClueHistoryItemDto(
                    word,
                    num,
                    teamColor,
                    DateTime.SpecifyKind(m.At, DateTimeKind.Utc),
                    m.PlayerId.HasValue && spymasterNames.TryGetValue(m.PlayerId.Value, out var nick) ? nick : null
                ));
            }
            catch
            {
                // Повреждённый payload — просто пропускаем
            }
        }

        var operativesCount = room.Players.Count(p =>
            p.TeamId == room.CurrentTurnTeamId
            && p.Role == PlayerRole.Operative);

        return new InternalState(
            room.Id, room.Code, room.State,
            currentTurnTeam, yourTeam, player.Role, player.IsHost,
            clue, room.GuessCount, room.GuessesAllowed,
            cards, teams,
            room.TurnDeadlineUtc, room.SpymasterDeadlineUtc,
            _vote.GetSnapshot(roomId),
            operativesCount,
            playerDtos,
            clueHistory
        );
    }

    private static ActiveVoteDto? BuildActiveVote(
        VoteService.VoteSessionSnapshot? snapshot,
        int operativesCount,
        Guid meId,
        bool canSeeVoterDetails)
    {
        if (snapshot is null || snapshot.Voters.Count == 0) return null;

        var kindStr = snapshot.Kind == VoteService.VoteTargetKind.Card ? "Card" : "EndTurn";

        var voters = canSeeVoterDetails
            ? snapshot.Voters
                .Select(pid => new VoteInfoDto(
                    pid,
                    snapshot.VoterColors.TryGetValue(pid, out var col) ? col : "#ffffff",
                    pid == meId))
                .ToList()
            : new List<VoteInfoDto>();

        return new ActiveVoteDto(
            kindStr,
            snapshot.CardId,
            snapshot.Voters.Count,
            operativesCount,
            snapshot.DeadlineUtc.HasValue
                ? DateTime.SpecifyKind(snapshot.DeadlineUtc.Value, DateTimeKind.Utc)
                : null,
            voters
        );
    }

    // === Таймер ===

    private static void ApplyTurnTimer(Room room, GameSettings settings)
    {
        if (!settings.TimerEnabled)
        {
            room.TurnStartedAtUtc = null;
            room.SpymasterDeadlineUtc = null;
            room.TurnDeadlineUtc = null;
            return;
        }

        var bS = room.TurnNumber == 0 ? settings.FirstSpymasterSeconds : settings.SpymasterSeconds;
        var bO = settings.OperativeSeconds;
        var now = DateTime.UtcNow;

        room.TurnStartedAtUtc = now;
        room.SpymasterDeadlineUtc = now.AddSeconds(bS);
        room.TurnDeadlineUtc = now.AddSeconds(bS + bO);
    }

    private void ScheduleTimerFor(Room room)
    {
        if (room.State == RoomState.InGame && room.TurnDeadlineUtc.HasValue)
        {
            var deadline = DateTime.SpecifyKind(room.TurnDeadlineUtc.Value, DateTimeKind.Utc);
            _timer.ScheduleTurnEnd(room.Id, deadline);
        }
        else
        {
            _timer.CancelTimer(room.Id);
        }
    }

    // === Вспомогательные ===

    private static bool CardColorMatchesTeam(CardColor card, TeamColor team) =>
        (card == CardColor.Blue && team == TeamColor.Blue) ||
        (card == CardColor.Red && team == TeamColor.Red);

    private void EndTurn(Room room, GameSettings settings)
    {
        _vote.ClearSession(room.Id);

        var currentTeamId = room.CurrentTurnTeamId!.Value;
        var other = room.Teams.First(t => t.Id != currentTeamId);
        room.CurrentTurnTeamId = other.Id;
        room.GuessCount = 0;
        room.GuessesAllowed = 0;
        room.ClueWord = null;
        room.ClueNumber = null;
        room.ClueTeamId = null;

        room.TurnNumber++;
        ApplyTurnTimer(room, settings);
    }

    private static DateTime? EnsureUtc(DateTime? dt)
        => dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : null;
}