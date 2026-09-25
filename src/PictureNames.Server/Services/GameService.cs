using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Entities;
using PictureNames.Server.Services.Dto;

namespace PictureNames.Server.Services;

public class GameService
{
    private readonly AppDbContext _db;

    public GameService(AppDbContext db)
    {
        _db = db;
    }

    // === Генерация поля ===

    public async Task GenerateFieldAsync(Guid roomId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
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

        var allImages = await _db.Images.ToListAsync(ct);

        if (allImages.Count < 25)
            throw new InvalidOperationException($"В базе {allImages.Count} картинок, нужно минимум 25.");

        var images = allImages
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

        await _db.SaveChangesAsync(ct);
    }

    // === Подсказка ===

    public async Task GiveClueAsync(Guid roomId, Guid playerId, string word, int number, CancellationToken ct = default)
    {
        var room = await _db.Rooms
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

        room.ClueWord = word.Trim();
        room.ClueNumber = number;
        room.ClueTeamId = player.TeamId;
        room.GuessesAllowed = number; // для справки, не блокирует
        room.GuessCount = 0;

        _db.Moves.Add(new Move
        {
            RoomId = roomId,
            PlayerId = playerId,
            Type = MoveType.Clue,
            Payload = System.Text.Json.JsonSerializer.Serialize(new { word = room.ClueWord, number })
        });

        await _db.SaveChangesAsync(ct);
    }

    // === Открытие карты ===

    public async Task<RevealOutcome> RevealCardAsync(Guid roomId, Guid playerId, Guid cardId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .Include(r => r.Cards)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.InGame)
            throw new InvalidOperationException("Партия не идёт.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        if (player.Role != PlayerRole.Operative)
            throw new InvalidOperationException("Открывать карты может только оперативник.");

        if (player.TeamId != room.CurrentTurnTeamId)
            throw new InvalidOperationException("Сейчас не ваш ход.");

        if (room.ClueWord is null)
            throw new InvalidOperationException("Сначала дождись подсказки.");

        var card = room.Cards.FirstOrDefault(c => c.Id == cardId)
            ?? throw new InvalidOperationException("Карта не найдена.");

        if (card.IsRevealed)
            throw new InvalidOperationException("Карта уже открыта.");

        card.IsRevealed = true;
        room.GuessCount++;

        var currentTeam = room.Teams.First(t => t.Id == room.CurrentTurnTeamId!.Value);
        var currentTeamColor = currentTeam.Color;

        RevealOutcome outcome;

        if (card.Color == CardColor.Assassin)
        {
            outcome = RevealOutcome.Assassin;
            room.State = RoomState.Finished;
        }
        else if (CardColorMatchesTeam(card.Color, currentTeamColor))
        {
            outcome = RevealOutcome.Correct;
            currentTeam.Score++;

            var totalOwnCards = room.Cards.Count(c => CardColorMatchesTeam(c.Color, currentTeamColor));
            if (currentTeam.Score >= totalOwnCards)
                room.State = RoomState.Finished;
        }
        else if (card.Color == CardColor.Neutral)
        {
            outcome = RevealOutcome.Neutral;
            EndTurn(room);
        }
        else
        {
            outcome = RevealOutcome.WrongTeam;
            EndTurn(room);
        }

        _db.Moves.Add(new Move
        {
            RoomId = roomId,
            PlayerId = playerId,
            Type = MoveType.Guess,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                cardId,
                color = card.Color.ToString(),
                outcome = outcome.ToString()
            })
        });

        await _db.SaveChangesAsync(ct);
        return outcome;
    }

    // === Завершение хода вручную ===

    public async Task EndTurnAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
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

        EndTurn(room);
        await _db.SaveChangesAsync(ct);
    }

    // === Реванш ===

    public async Task ResetToLobbyAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        if (!player.IsHost)
            throw new InvalidOperationException("Только хост может запустить реванш.");

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

        // Spectator → Operative (новая партия, все играют)
        foreach (var p in room.Players)
        {
            if (p.Role == PlayerRole.Spectator)
                p.Role = PlayerRole.Operative;
        }

        await _db.SaveChangesAsync(ct);
    }

    // === Сборка состояния ===

    public async Task<GameStateForOperativeDto> GetForOperativeAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var state = await BuildStateAsync(roomId, playerId, ct);

        var cards = state.Cards
            .Select(c => new CardForOperativeDto(
                c.Id, c.Position, c.ImageUrl, c.AltText,
                c.IsRevealed ? c.Color : null,
                c.IsRevealed
            ))
            .ToList();

        return new GameStateForOperativeDto(
            state.RoomId, state.Code, state.State, state.CurrentTurnTeam,
            state.YourTeam, state.YourRole, state.CurrentClue,
            state.GuessesMade, state.GuessesAllowed,
            cards, state.Teams
        );
    }

    public async Task<GameStateForSpymasterDto> GetForSpymasterAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var state = await BuildStateAsync(roomId, playerId, ct);

        var cards = state.Cards
            .Select(c => new CardForSpymasterDto(
                c.Id, c.Position, c.ImageUrl, c.AltText, c.Color, c.IsRevealed
            ))
            .ToList();

        return new GameStateForSpymasterDto(
            state.RoomId, state.Code, state.State, state.CurrentTurnTeam,
            state.YourTeam, state.YourRole, state.CurrentClue,
            state.GuessesMade, state.GuessesAllowed,
            cards, state.Teams
        );
    }

    private record InternalCard(Guid Id, int Position, string ImageUrl, string? AltText, CardColor Color, bool IsRevealed);

    private record InternalState(
        Guid RoomId, string Code, RoomState State,
        TeamColor? CurrentTurnTeam, TeamColor? YourTeam, PlayerRole YourRole,
        ClueDto? CurrentClue, int GuessesMade, int GuessesAllowed,
        List<InternalCard> Cards, List<TeamDto> Teams
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

        var teams = room.Teams
            .OrderBy(t => t.Color)
            .Select(t => new TeamDto(t.Id, t.Color, t.Score, t.SpymasterId))
            .ToList();

        return new InternalState(
            room.Id, room.Code, room.State,
            currentTurnTeam, yourTeam, player.Role,
            clue, room.GuessCount, room.GuessesAllowed,
            cards, teams
        );
    }

    // === Вспомогательные ===

    private static bool CardColorMatchesTeam(CardColor card, TeamColor team) =>
        (card == CardColor.Blue && team == TeamColor.Blue) ||
        (card == CardColor.Red && team == TeamColor.Red);

    private static void EndTurn(Room room)
    {
        var currentTeamId = room.CurrentTurnTeamId!.Value;
        var other = room.Teams.First(t => t.Id != currentTeamId);
        room.CurrentTurnTeamId = other.Id;
        room.GuessCount = 0;
        room.GuessesAllowed = 0;
        room.ClueWord = null;
        room.ClueNumber = null;
        room.ClueTeamId = null;
    }
}