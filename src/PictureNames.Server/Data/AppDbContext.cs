using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Entities;

namespace PictureNames.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Move> Moves => Set<Move>();
    public DbSet<GameSettings> GameSettings => Set<GameSettings>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Enums храним как строки — так база читаема глазами
        mb.Entity<Room>().Property(r => r.State).HasConversion<string>();
        mb.Entity<Player>().Property(p => p.Role).HasConversion<string>();
        mb.Entity<Team>().Property(t => t.Color).HasConversion<string>();
        mb.Entity<Card>().Property(c => c.Color).HasConversion<string>();
        mb.Entity<Move>().Property(m => m.Type).HasConversion<string>();

        // Код комнаты — уникален
        mb.Entity<Room>().HasIndex(r => r.Code).IsUnique();

        // GameSettings — один-к-одному с Room
        mb.Entity<Room>()
            .HasOne(r => r.Settings)
            .WithOne(s => s.Room)
            .HasForeignKey<GameSettings>(s => s.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        // Удаление комнаты тянет за собой всё
        mb.Entity<Player>()
            .HasOne(p => p.Room).WithMany(r => r.Players)
            .HasForeignKey(p => p.RoomId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<Team>()
            .HasOne(t => t.Room).WithMany(r => r.Teams)
            .HasForeignKey(t => t.RoomId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<Card>()
            .HasOne(c => c.Room).WithMany(r => r.Cards)
            .HasForeignKey(c => c.RoomId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<Move>()
            .HasOne(m => m.Room).WithMany(r => r.Moves)
            .HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Cascade);
    }
}