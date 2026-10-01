using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Entities;
using PictureNames.Server.Services.Dto;
using PictureNames.Server.Services.Notifications;

namespace PictureNames.Server.Services;

public class RoomService
{
    private readonly AppDbContext _db;
    private readonly ILobbyNotifier _notifier;
    private readonly GameService _game;
    private readonly TurnTimerService _timer;
    private readonly ImagePackScanner _images;

    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 4;

    public RoomService(
        AppDbContext db,
        ILobbyNotifier notifier,
        GameService game,
        TurnTimerService timer,
        ImagePackScanner images)
    {
        _db = db;
        _notifier = notifier;
        _game = game;
        _timer = timer;
        _images = images;
    }

    public async Task<LobbyDto> CreateRoomAsync(string nickname, CancellationToken ct = default)
    {
        var code = await GenerateUniqueCodeAsync(ct);

        var room = new Room
        {
            Code = code,
            State = RoomState.Lobby,
            Settings = new GameSettings()
        };

        room.Teams.Add(new Team { Color = TeamColor.Blue });
        room.Teams.Add(new Team { Color = TeamColor.Red });

        var host = new Player
        {
            Nickname = nickname.Trim(),
            Role = PlayerRole.Operative,
            IsHost = true,
            IsConnected = true,
            VoteColor = VotePalette.PickUnused(Array.Empty<string?>())
        };
        room.Players.Add(host);

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync(ct);

        return await GetLobbyAsync(room.Id, ct);
    }

    public async Task<LobbyDto> JoinRoomAsync(string code, string nickname, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .FirstOrDefaultAsync(r => r.Code == code.ToUpperInvariant(), ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        var trimmedNick = nickname.Trim();

        var existing = await _db.Players
            .FirstOrDefaultAsync(p => p.RoomId == room.Id && p.Nickname == trimmedNick, ct);

        if (existing is not null && existing.IsConnected)
            throw new InvalidOperationException("Этот ник уже занят в комнате.");

        if (existing is not null)
        {
            existing.IsConnected = true;

            if (string.IsNullOrEmpty(existing.VoteColor))
            {
                var existingColors = await _db.Players
                    .Where(p => p.RoomId == room.Id && p.Id != existing.Id)
                    .Select(p => p.VoteColor)
                    .ToListAsync(ct);
                existing.VoteColor = VotePalette.PickUnused(existingColors);
            }

            await _db.SaveChangesAsync(ct);
            var lobbyExisting = await GetLobbyAsync(room.Id, ct);
            await _notifier.LobbyUpdatedAsync(room.Id, lobbyExisting, ct);
            return lobbyExisting;
        }

        var role = room.State == RoomState.Lobby
            ? PlayerRole.Operative
            : PlayerRole.Spectator;

        var existingColorsList = await _db.Players
            .Where(p => p.RoomId == room.Id)
            .Select(p => p.VoteColor)
            .ToListAsync(ct);

        _db.Players.Add(new Player
        {
            RoomId = room.Id,
            Nickname = trimmedNick,
            Role = role,
            IsConnected = true,
            VoteColor = VotePalette.PickUnused(existingColorsList)
        });

        await _db.SaveChangesAsync(ct);
        var lobby = await GetLobbyAsync(room.Id, ct);
        await _notifier.LobbyUpdatedAsync(room.Id, lobby, ct);
        return lobby;
    }

    public async Task<LobbyDto> GetLobbyAsync(Guid roomId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        var teamColors = room.Teams.ToDictionary(t => t.Id, t => t.Color);

        var playersList = room.Players.ToList();
        var teamsList = room.Teams.ToList();

        var players = playersList
            .OrderBy(p => p.JoinedAt)
            .Select(p => new PlayerDto(
                p.Id,
                p.Nickname,
                p.TeamId,
                p.TeamId.HasValue && teamColors.TryGetValue(p.TeamId.Value, out var c) ? c : null,
                p.Role,
                p.IsHost,
                p.IsConnected
            ))
            .ToList();

        // В лобби карт на поле нет — ставим стандартные цели 9/8 как заглушку.
        // Клиент лобби это поле не использует, оно нужно только в партии.
        var teams = teamsList
            .OrderBy(t => t.Color)
            .Select(t => new TeamDto(
                t.Id, t.Color, t.Score, t.SpymasterId,
                t.Color == TeamColor.Blue ? 9 : 8))
            .ToList();

        var host = playersList.FirstOrDefault(p => p.IsHost);

        var settings = new GameSettingsDto(
            room.Settings.GridSize,
            room.Settings.TimerEnabled,
            room.Settings.ImagePack,
            room.Settings.FirstSpymasterSeconds,
            room.Settings.SpymasterSeconds,
            room.Settings.OperativeSeconds,
            room.Settings.BonusPerCorrectSeconds
        );

        return new LobbyDto(room.Id, room.Code, room.State, host?.Id, players, teams, settings);
    }

    public async Task<LobbyDto> UpdateSettingsAsync(
        Guid roomId, UpdateSettingsRequest req, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.Lobby)
            throw new InvalidOperationException("Настройки меняются только в лобби.");

        var player = room.Players.FirstOrDefault(p => p.Id == req.PlayerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        if (!player.IsHost)
            throw new InvalidOperationException("Только хост меняет настройки.");

        var s = room.Settings;

        if (req.TimerEnabled.HasValue)
            s.TimerEnabled = req.TimerEnabled.Value;

        if (!string.IsNullOrWhiteSpace(req.ImagePack))
        {
            var packs = _images.GetPacks();
            var pack = packs.FirstOrDefault(p =>
                string.Equals(p.Name, req.ImagePack, StringComparison.OrdinalIgnoreCase));

            if (pack.Name is null)
                throw new InvalidOperationException($"Набор «{req.ImagePack}» не найден.");

            if (pack.Count < 25)
                throw new InvalidOperationException(
                    $"В наборе «{pack.Name}» только {pack.Count} картинок, нужно минимум 25.");

            s.ImagePack = pack.Name;
        }

        if (req.FirstSpymasterSeconds.HasValue)
            s.FirstSpymasterSeconds = Clamp(req.FirstSpymasterSeconds.Value, 10, 600);

        if (req.SpymasterSeconds.HasValue)
            s.SpymasterSeconds = Clamp(req.SpymasterSeconds.Value, 10, 600);

        if (req.OperativeSeconds.HasValue)
            s.OperativeSeconds = Clamp(req.OperativeSeconds.Value, 10, 600);

        if (req.BonusPerCorrectSeconds.HasValue)
            s.BonusPerCorrectSeconds = Clamp(req.BonusPerCorrectSeconds.Value, 0, 120);

        await _db.SaveChangesAsync(ct);

        var lobby = await GetLobbyAsync(roomId, ct);
        await _notifier.LobbyUpdatedAsync(roomId, lobby, ct);
        return lobby;
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    public async Task<LobbyDto> AssignTeamAsync(Guid roomId, Guid playerId, TeamColor? teamColor, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.Lobby)
            throw new InvalidOperationException("Партия уже началась.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        if (teamColor is null)
        {
            player.TeamId = null;
            player.Role = PlayerRole.Operative;

            foreach (var t in room.Teams.Where(t => t.SpymasterId == playerId))
                t.SpymasterId = null;
        }
        else
        {
            var team = room.Teams.First(t => t.Color == teamColor.Value);
            player.TeamId = team.Id;

            foreach (var t in room.Teams.Where(t => t.SpymasterId == playerId))
                t.SpymasterId = null;

            if (player.Role != PlayerRole.Operative)
                player.Role = PlayerRole.Operative;
        }

        await _db.SaveChangesAsync(ct);
        var lobby = await GetLobbyAsync(roomId, ct);
        await _notifier.LobbyUpdatedAsync(roomId, lobby, ct);
        return lobby;
    }

    public async Task<LobbyDto> AssignRoleAsync(Guid roomId, Guid playerId, PlayerRole role, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.Lobby)
            throw new InvalidOperationException("Партия уже началась.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        if (role == PlayerRole.Spymaster)
        {
            if (player.TeamId is null)
                throw new InvalidOperationException("Сначала выбери команду.");

            var team = room.Teams.First(t => t.Id == player.TeamId.Value);

            if (team.SpymasterId.HasValue && team.SpymasterId.Value != playerId)
            {
                var previous = room.Players.FirstOrDefault(p => p.Id == team.SpymasterId.Value);
                if (previous is not null) previous.Role = PlayerRole.Operative;
            }

            team.SpymasterId = playerId;
            player.Role = PlayerRole.Spymaster;
        }
        else
        {
            foreach (var t in room.Teams.Where(t => t.SpymasterId == playerId))
                t.SpymasterId = null;
            player.Role = PlayerRole.Operative;
        }

        await _db.SaveChangesAsync(ct);
        var lobby = await GetLobbyAsync(roomId, ct);
        await _notifier.LobbyUpdatedAsync(roomId, lobby, ct);
        return lobby;
    }

    public async Task StartGameAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Settings)
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.Lobby)
            throw new InvalidOperationException("Игра уже идёт или завершена.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден.");

        if (!player.IsHost)
            throw new InvalidOperationException("Только хост может начать игру.");

        foreach (var team in room.Teams)
        {
            var members = room.Players.Where(p => p.TeamId == team.Id).ToList();
            if (members.Count < 2)
                throw new InvalidOperationException($"В команде {team.Color} должно быть минимум 2 игрока.");

            if (team.SpymasterId is null)
                throw new InvalidOperationException($"В команде {team.Color} не выбран спаймастер.");
        }

        var pack = room.Settings.ImagePack ?? "default";
        var available = await _db.Images.CountAsync(i => i.Tags == pack && i.IsPublic, ct);

        if (available < 25)
            throw new InvalidOperationException(
                $"В наборе «{pack}» только {available} картинок, нужно минимум 25. " +
                $"Положи больше в wwwroot/images/{pack}/ и перезапусти сервер.");

        await _game.GenerateFieldAsync(roomId, ct);

        var lobby = await GetLobbyAsync(roomId, ct);
        await _notifier.LobbyUpdatedAsync(roomId, lobby, ct);
    }

    public async Task LeaveRoomAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        if (room is null) return;

        var player = room.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null) return;

        foreach (var t in room.Teams.Where(t => t.SpymasterId == playerId))
            t.SpymasterId = null;

        var wasHost = player.IsHost;
        room.Players.Remove(player);

        if (wasHost && room.Players.Any())
        {
            var newHost = room.Players.OrderBy(p => p.JoinedAt).First();
            newHost.IsHost = true;
        }

        if (!room.Players.Any())
        {
            _timer.CancelTimer(roomId);
            _db.Rooms.Remove(room);
            await _db.SaveChangesAsync(ct);
            return;
        }

        await _db.SaveChangesAsync(ct);

        var lobby = await GetLobbyAsync(roomId, ct);
        await _notifier.LobbyUpdatedAsync(roomId, lobby, ct);
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var code = RandomCode();
            var exists = await _db.Rooms.AnyAsync(r => r.Code == code, ct);
            if (!exists) return code;
        }
        throw new InvalidOperationException("Не удалось сгенерировать уникальный код комнаты.");
    }

    private static string RandomCode()
    {
        Span<char> buffer = stackalloc char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
            buffer[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
        return new string(buffer);
    }
}