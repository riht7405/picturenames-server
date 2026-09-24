using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;

namespace PictureNames.Server.Services;

// Фоновый сервис: раз в 2 минуты убирает комнаты, где никто не онлайн
// и комната создана больше 10 минут назад. Игроки, которые просто F5,
// успеют вернуться до того, как комнату удалят.
public class RoomCleanupService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<RoomCleanupService> _logger;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinRoomAge = TimeSpan.FromMinutes(10);

    public RoomCleanupService(IServiceProvider sp, ILogger<RoomCleanupService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("RoomCleanupService запущен");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, ct);

                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var cutoff = DateTime.UtcNow - MinRoomAge;

                var stale = await db.Rooms
                    .Include(r => r.Players)
                    .Where(r => r.CreatedAt < cutoff)
                    .Where(r => r.Players.All(p => !p.IsConnected))
                    .ToListAsync(ct);

                if (stale.Count > 0)
                {
                    db.Rooms.RemoveRange(stale);
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("Удалено комнат: {count}", stale.Count);
                }
            }
            catch (OperationCanceledException) { /* нормальное завершение */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в RoomCleanupService");
            }
        }
    }
}