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

    public RoomService(AppDbContext db, ILobbyNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
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
}