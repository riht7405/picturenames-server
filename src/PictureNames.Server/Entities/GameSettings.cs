namespace PictureNames.Server.Entities;

public class GameSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public int GridSize { get; set; } = 5;
    public int TurnSeconds { get; set; } = 120;
    public bool TimerEnabled { get; set; } = true;
    public string ImagePack { get; set; } = "default";
}