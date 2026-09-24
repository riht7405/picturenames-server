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

    // Алфавит без 0/O, 1/I/L — чтобы не путать при вводе с чужого экрана
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 4;
    private readonly GameService _game;

    public RoomService(AppDbContext db, ILobbyNotifier notifier, GameService game)
    {
        _db = db;
        _notifier = notifier;
        _game = game;
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

        // Две команды создаются вместе с комнатой,
        // чтобы не было состояния "команда ещё не готова".
        room.Teams.Add(new Team { Color = TeamColor.Blue });
        room.Teams.Add(new Team { Color = TeamColor.Red });

        var host = new Player
        {
            Nickname = nickname.Trim(),
            Role = PlayerRole.Operative,
            IsHost = true,
            IsConnected = true
        };
        room.Players.Add(host);

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync(ct);

        // Никого ещё нет в комнате — уведомлять некого.
        return await GetLobbyAsync(room.Id, ct);
    }

    public async Task<LobbyDto> JoinRoomAsync(string code, string nickname, CancellationToken ct = default)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();

        var room = await _db.Rooms
            .FirstOrDefaultAsync(r => r.Code == normalizedCode, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        if (room.State != RoomState.Lobby)
            throw new InvalidOperationException("Партия уже началась. Дождись следующей.");

        var trimmedNick = nickname.Trim();
        var alreadyInside = await _db.Players
            .AnyAsync(p => p.RoomId == room.Id && p.Nickname == trimmedNick, ct);

        if (alreadyInside)
            throw new InvalidOperationException("Этот ник уже занят в комнате.");

        _db.Players.Add(new Player
        {
            RoomId = room.Id,
            Nickname = trimmedNick,
            Role = PlayerRole.Operative,
            IsConnected = true
        });

        await _db.SaveChangesAsync(ct);

        var lobby = await GetLobbyAsync(room.Id, ct);
        await _notifier.LobbyUpdatedAsync(room.Id, lobby, ct);
        return lobby;
    }

    public async Task<LobbyDto> GetLobbyAsync(Guid roomId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        // Быстрый поиск цвета команды по её Id — чтобы не гонять LINQ внутри LINQ
        var teamColors = room.Teams.ToDictionary(t => t.Id, t => t.Color);

        var players = room.Players
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

        var teams = room.Teams
            .OrderBy(t => t.Color)
            .Select(t => new TeamDto(t.Id, t.Color, t.Score, t.SpymasterId))
            .ToList();

        var host = room.Players.FirstOrDefault(p => p.IsHost);

        return new LobbyDto(room.Id, room.Code, room.State, host?.Id, players, teams);
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        // 32^4 = ~1M комбинаций. Для локальной игры коллизии почти невозможны.
        // На всякий случай — до 10 попыток.
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
        }
        else
        {
            var team = room.Teams.First(t => t.Color == teamColor.Value);
            player.TeamId = team.Id;

            // Если игрок был спаймастером другой команды — снимаем
            foreach (var t in room.Teams.Where(t => t.SpymasterId == playerId))
                t.SpymasterId = null;
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

            // Если в команде уже есть спаймастер — снимаем его
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
            // Возвращаемся в оперативники — снимаем с команды, если были спаймастером
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

        await _game.GenerateFieldAsync(roomId, ct);

        // После смены состояния — уведомляем лобби, клиенты подхватят и перейдут на игровой экран
        var lobby = await GetLobbyAsync(roomId, ct);
        await _notifier.LobbyUpdatedAsync(roomId, lobby, ct);
    }
}