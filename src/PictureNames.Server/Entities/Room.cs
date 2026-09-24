namespace PictureNames.Server.Entities;

public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Короткий код для входа (например "A7X2")
    public string Code { get; set; } = string.Empty;

    public RoomState State { get; set; } = RoomState.Lobby;

    // Чей сейчас ход. Null — партия ещё не началась
    public Guid? CurrentTurnTeamId { get; set; }

    // Сколько догадок уже сделано в текущем ходу
    public int GuessCount { get; set; }

    // Сколько разрешено в этом ходу (число из подсказки + 1)
    public int GuessesAllowed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Навигационные свойства
    public GameSettings Settings { get; set; } = null!;
    public List<Team> Teams { get; set; } = new();
    public List<Player> Players { get; set; } = new();
    public List<Card> Cards { get; set; } = new();
    public List<Move> Moves { get; set; } = new();
}