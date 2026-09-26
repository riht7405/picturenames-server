using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Entities;
using PictureNames.Server.Services;

namespace PictureNames.Server.Hubs;

public class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;
    private readonly RoomService _rooms;
    private readonly GameService _game;
    private readonly AppDbContext _db;

    public GameHub(
        ILogger<GameHub> logger,
        RoomService rooms,
        GameService game,
        AppDbContext db)
    {
        _logger = logger;
        _rooms = rooms;
        _game = game;
        _db = db;
    }

    public static string RoomGroup(Guid roomId) => $"room:{roomId}";

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Подключился: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Отключился: {ConnectionId}", Context.ConnectionId);

        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.ConnectionId == Context.ConnectionId);
        if (player is not null)
        {
            player.IsConnected = false;
            player.ConnectionId = null;
            await _db.SaveChangesAsync();

            var lobby = await _rooms.GetLobbyAsync(player.RoomId);
            await Clients.Group(RoomGroup(player.RoomId)).SendAsync("LobbyUpdated", lobby);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // === Лобби ===

    public async Task JoinRoom(Guid roomId, Guid playerId)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);

        if (player is null || player.RoomId != roomId)
        {
            _logger.LogWarning(
                "JoinRoom: игрок {playerId} не найден в комнате {roomId}, только подписка",
                playerId, roomId);

            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));
            var lobbyAnon = await _rooms.GetLobbyAsync(roomId);
            await Clients.Caller.SendAsync("LobbyUpdated", lobbyAnon);
            return;
        }

        player.ConnectionId = Context.ConnectionId;
        player.IsConnected = true;
        await _db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));
        _logger.LogInformation(
            "Connection {conn} → комната {room} ({nick})",
            Context.ConnectionId, roomId, player.Nickname);

        var lobby = await _rooms.GetLobbyAsync(roomId);
        await Clients.Group(RoomGroup(roomId)).SendAsync("LobbyUpdated", lobby);
    }

    public async Task LeaveRoom(Guid roomId, Guid playerId)
{
    // Сначала выходим из группы, чтобы не получать чужие события
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(roomId));

    // Очищаем ConnectionId у игрока — до удаления из БД
    var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
    if (player is not null)
    {
        player.ConnectionId = null;
        player.IsConnected = false;
        await _db.SaveChangesAsync();
    }

    // Удаляем игрока и, если надо, комнату
    await _rooms.LeaveRoomAsync(roomId, playerId);
}

    // === Партия ===

    public async Task JoinGame(Guid roomId, Guid playerId)
    {
        var roomExists = await _db.Rooms.AnyAsync(r => r.Id == roomId);
        if (!roomExists)
            throw new HubException("Комната больше не существует. Вернитесь в лобби.");

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player is null)
            throw new HubException("Игрок не найден. Вернитесь в лобби.");

        if (player.RoomId != roomId)
            throw new HubException("Игрок не в этой комнате. Вернитесь в лобби.");

        player.ConnectionId = Context.ConnectionId;
        player.IsConnected = true;
        await _db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));
        _logger.LogInformation(
            "Connection {conn} → партия {room} как {role}",
            Context.ConnectionId, roomId, player.Role);

        object dto = player.Role == PlayerRole.Spymaster
            ? await _game.GetForSpymasterAsync(roomId, playerId)
            : (object)await _game.GetForOperativeAsync(roomId, playerId);

        await Clients.Caller.SendAsync("GameStateUpdated", dto);
    }

    // Лёгкий сигнал группе: «состояние изменилось, каждый пусть подтянет своё»
    public async Task NotifyStateChanged(Guid roomId)
    {
        await Clients.Group(RoomGroup(roomId)).SendAsync("StateChanged");
    }

    public async Task GiveClue(Guid roomId, Guid playerId, string word, int number)
    {
        await _game.GiveClueAsync(roomId, playerId, word, number);
        await NotifyStateChanged(roomId);
    }

    public async Task RevealCard(Guid roomId, Guid playerId, Guid cardId)
    {
        var outcome = await _game.RevealCardAsync(roomId, playerId, cardId);

        await Clients.Group(RoomGroup(roomId)).SendAsync("RevealOutcome", new
        {
            cardId,
            outcome = outcome.ToString()
        });

        await NotifyStateChanged(roomId);
    }

    public async Task EndTurn(Guid roomId, Guid playerId)
    {
        await _game.EndTurnAsync(roomId, playerId);
        await NotifyStateChanged(roomId);
    }

    // Закрывает партию: удаляет карты, обнуляет счёт, переводит комнату в Lobby.
    // Может вызвать любой игрок. Хост потом жмёт "Начать игру" в лобби.
    public async Task ReturnToLobby(Guid roomId, Guid playerId)
    {
        await _game.ResetToLobbyAsync(roomId, playerId);

        var lobby = await _rooms.GetLobbyAsync(roomId);
        await Clients.Group(RoomGroup(roomId)).SendAsync("LobbyUpdated", lobby);
    }

    public async Task SendMessage(string nickname, string text)
    {
        await Clients.All.SendAsync("MessageReceived", nickname, text, DateTime.UtcNow);
    }
}