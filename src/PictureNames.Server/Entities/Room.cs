namespace PictureNames.Server.Entities;

public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public RoomState State { get; set; } = RoomState.Lobby;

    public Guid? CurrentTurnTeamId { get; set; }
    public int GuessCount { get; set; }
    public int GuessesAllowed { get; set; }

    // Текущая подсказка. Обнуляется при смене хода.
    public string? ClueWord { get; set; }
    public int? ClueNumber { get; set; }
    public Guid? ClueTeamId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public GameSettings Settings { get; set; } = null!;
    public List<Team> Teams { get; set; } = new();
    public List<Player> Players { get; set; } = new();
    public List<Card> Cards { get; set; } = new();
    public List<Move> Moves { get; set; } = new();
}