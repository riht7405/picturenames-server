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

        // Помечаем игрока как offline, чтобы UI показал "offline"
        // и RoomCleanupService смог убрать мёртвые комнаты.
        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.ConnectionId == Context.ConnectionId);
        if (player is not null)
        {
            player.IsConnected = false;
            player.ConnectionId = null;
            await _db.SaveChangesAsync();
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ============================================================
    // ЛОББИ
    // ============================================================

    // Вызывается со страницы lobby.html после создания/входа.
    // С этого момента клиент получает LobbyUpdated для своей комнаты.
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

    // ============================================================
    // ПАРТИЯ
    // ============================================================

    // Вызывается со страницы game.html.
    // Сохраняем ConnectionId игрока, добавляем в группу, отправляем персональный снапшот поля.
    public async Task JoinGame(Guid roomId, Guid playerId)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player is null || player.RoomId != roomId)
            throw new HubException("Игрок не найден в этой комнате.");

        player.ConnectionId = Context.ConnectionId;
        player.IsConnected = true;
        await _db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));
        _logger.LogInformation("Connection {conn} → партия {room} как {role}",
            Context.ConnectionId, roomId, player.Role);

        // Отправляем персональный снапшот поля
        object dto = player.Role == PlayerRole.Spymaster
            ? await _game.GetForSpymasterAsync(roomId, playerId)
            : (object)await _game.GetForOperativeAsync(roomId, playerId);

        await Clients.Caller.SendAsync("GameStateUpdated", dto);
    }

    // Рассылка состояния партии — каждому игроку отдельно, с учётом его роли.
    // Вызывается сервисами при ходах, открытии карт и т.д.
    public async Task BroadcastGameStateAsync(Guid roomId)
    {
        var players = await _db.Players
            .Where(p => p.RoomId == roomId && p.IsConnected && p.ConnectionId != null)
            .ToListAsync();

        foreach (var p in players)
        {
            object dto = p.Role == PlayerRole.Spymaster
                ? await _game.GetForSpymasterAsync(roomId, p.Id)
                : (object)await _game.GetForOperativeAsync(roomId, p.Id);

            await Clients.Client(p.ConnectionId!).SendAsync("GameStateUpdated", dto);
        }
    }

    // === Ходы ===

    public async Task GiveClue(Guid roomId, Guid playerId, string word, int number)
    {
        await _game.GiveClueAsync(roomId, playerId, word, number);
        await BroadcastGameStateAsync(roomId);
    }

    public async Task RevealCard(Guid roomId, Guid playerId, Guid cardId)
    {
        var outcome = await _game.RevealCardAsync(roomId, playerId, cardId);

        await Clients.Group(RoomGroup(roomId)).SendAsync("RevealOutcome", new
        {
            cardId,
            outcome = outcome.ToString()
        });

        await BroadcastGameStateAsync(roomId);
    }

    // ============================================================
    // ТЕСТОВЫЙ ЧАТ (для отладки соединения)
    // ============================================================

    public async Task SendMessage(string nickname, string text)
    {
        await Clients.All.SendAsync("MessageReceived", nickname, text, DateTime.UtcNow);
    }
}