namespace PictureNames.Server.Entities;

public class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public TeamColor Color { get; set; }

    // Сколько карт команда уже открыла
    public int Score { get; set; }

    // ID спаймастера. Храним как Guid без FK — чтобы избежать циклической зависимости
    // (Player.TeamId → Team.Id, Team.SpymasterId → Player.Id)
    public Guid? SpymasterId { get; set; }

    public List<Player> Players { get; set; } = new();
}