using Microsoft.AspNetCore.SignalR;
using PictureNames.Server.Services;

namespace PictureNames.Server.Hubs;

public class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;
    private readonly RoomService _rooms;

    public GameHub(ILogger<GameHub> logger, RoomService rooms)
    {
        _logger = logger;
        _rooms = rooms;
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
        await base.OnDisconnectedAsync(exception);
    }

    // Клиент вызывает это после создания/входа в комнату.
    // С этого момента он получает LobbyUpdated для своей комнаты.
    public async Task JoinRoom(Guid roomId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));
        _logger.LogInformation("Connection {conn} → комната {room}", Context.ConnectionId, roomId);

        // Сразу отправляем текущий снапшот — чтобы клиент не ждал следующего события
        var lobby = await _rooms.GetLobbyAsync(roomId);
        await Clients.Caller.SendAsync("LobbyUpdated", lobby);
    }

    public async Task LeaveRoom(Guid roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(roomId));
        _logger.LogInformation("Connection {conn} ← комната {room}", Context.ConnectionId, roomId);
    }

    // Оставим для тестового чата
    public async Task SendMessage(string nickname, string text)
    {
        await Clients.All.SendAsync("MessageReceived", nickname, text, DateTime.UtcNow);
    }
}