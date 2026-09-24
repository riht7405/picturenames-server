namespace PictureNames.Server.Entities;

public class Move
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public Guid? PlayerId { get; set; }
    public MoveType Type { get; set; }

    // JSON-строка с данными события.
    // Пример для Clue: {"word":"океан","number":3}
    // Пример для Guess: {"cardId":"...","color":"Blue","wasCorrect":true}
    public string? Payload { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;
}