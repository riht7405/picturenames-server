namespace PictureNames.Server.Entities;

public class Card
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public Guid ImageId { get; set; }
    public Image Image { get; set; } = null!;

    // 0..24 — позиция в сетке (слева-направо, сверху-вниз)
    public int Position { get; set; }

    // Тайна. Никогда не уходит оперативникам, пока IsRevealed = false
    public CardColor Color { get; set; }

    public bool IsRevealed { get; set; }
}