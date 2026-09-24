using Microsoft.AspNetCore.SignalR;
using PictureNames.Server.Services.Dto;
using PictureNames.Server.Services.Notifications;

namespace PictureNames.Server.Hubs;

public class SignalRLobbyNotifier : ILobbyNotifier
{
    private readonly IHubContext<GameHub> _hub;

    public SignalRLobbyNotifier(IHubContext<GameHub> hub) => _hub = hub;

    public Task LobbyUpdatedAsync(Guid roomId, LobbyDto lobby, CancellationToken ct = default)
        => _hub.Clients
            .Group(GameHub.RoomGroup(roomId))
            .SendAsync("LobbyUpdated", lobby, ct);
}