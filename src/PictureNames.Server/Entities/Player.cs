namespace PictureNames.Server.Entities;

public class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }

    public string Nickname { get; set; } = string.Empty;
    public PlayerRole Role { get; set; } = PlayerRole.Operative;

    public bool IsHost { get; set; }
    public bool IsConnected { get; set; }

    public string? ConnectionId { get; set; }

    // Цвет точки при голосовании за карту. Hex, например "#ff6b6b".
    public string? VoteColor { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}