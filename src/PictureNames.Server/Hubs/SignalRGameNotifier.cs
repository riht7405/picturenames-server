using Microsoft.AspNetCore.SignalR;
using PictureNames.Server.Services.Notifications;

namespace PictureNames.Server.Hubs;

public class SignalRGameNotifier : IGameNotifier
{
    private readonly IHubContext<GameHub> _hub;

    public SignalRGameNotifier(IHubContext<GameHub> hub) => _hub = hub;

    public Task StateChangedAsync(Guid roomId, CancellationToken ct = default)
        => _hub.Clients
            .Group(GameHub.RoomGroup(roomId))
            .SendAsync("StateChanged", ct);
}