namespace PictureNames.Server.Entities;

public class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    // Может быть null, пока игрок не распределён по команде
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }

    public string Nickname { get; set; } = string.Empty;
    public PlayerRole Role { get; set; } = PlayerRole.Operative;

    public bool IsHost { get; set; }
    public bool IsConnected { get; set; }

    // ID SignalR-соединения. Нужен для реконнекта
    public string? ConnectionId { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}