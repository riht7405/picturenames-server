using PictureNames.Server.Services.Dto;

namespace PictureNames.Server.Services.Notifications;

// Абстракция. Сервис не знает про SignalR — он просто шлёт "уведоми лобби".
// Кто именно уведомляет — решает DI.
public interface ILobbyNotifier
{
    Task LobbyUpdatedAsync(Guid roomId, LobbyDto lobby, CancellationToken ct = default);
}