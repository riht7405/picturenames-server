namespace PictureNames.Server.Entities;

public class GameSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public int GridSize { get; set; } = 5;
    public bool TimerEnabled { get; set; } = true;
    public string ImagePack { get; set; } = "default";

    // === Параметры таймера (секунды) ===
    public int FirstSpymasterSeconds { get; set; } = 90;   // первому спаймастеру партии
    public int SpymasterSeconds { get; set; } = 60;        // всем остальным
    public int OperativeSeconds { get; set; } = 60;        // оперативникам на отгадывание
    public int BonusPerCorrectSeconds { get; set; } = 15;  // +за каждый верный ответ

    // Legacy — оставил для совместимости со старой БД, не используется
    public int TurnSeconds { get; set; } = 120;
}