namespace PictureNames.Server.Services.Notifications;

public interface IGameNotifier
{
    Task StateChangedAsync(Guid roomId, CancellationToken ct = default);
}