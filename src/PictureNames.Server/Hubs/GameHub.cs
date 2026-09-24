using Microsoft.AspNetCore.SignalR;

namespace PictureNames.Server.Hubs;

public class GameHub : Hub
{
    // Логируем подключения — чтобы видеть в консоли, кто пришёл и ушёл
    private readonly ILogger<GameHub> _logger;

    public GameHub(ILogger<GameHub> logger)
    {
        _logger = logger;
    }

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

    // Первый метод: рассылаем сообщение всем подключённым
    public async Task SendMessage(string nickname, string text)
    {
        // Clients.All — широковещательно, всем
        await Clients.All.SendAsync("MessageReceived", nickname, text, DateTime.UtcNow);
    }
}