using System.Collections.Concurrent;

namespace PictureNames.Server.Services;

// Единый серверный таймер ходов.
// Хранит по одной отложенной задаче на комнату. При смене хода — перезапускает.
// По истечении — автоматически завершает ход через GameService и уведомляет клиентов.
public class TurnTimerService : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TurnTimerService> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _timers = new();

    public TurnTimerService(IServiceScopeFactory scopeFactory, ILogger<TurnTimerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void ScheduleTurnEnd(Guid roomId, DateTime deadlineUtc)
    {
        CancelTimer(roomId);

        var cts = new CancellationTokenSource();
        _timers[roomId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var delay = deadlineUtc - DateTime.UtcNow;
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

                await Task.Delay(delay, cts.Token);

                using var scope = _scopeFactory.CreateScope();
                var game = scope.ServiceProvider.GetRequiredService<GameService>();
                var notifier = scope.ServiceProvider.GetRequiredService<Services.Notifications.IGameNotifier>();

                var ended = await game.EndTurnAutoAsync(roomId, deadlineUtc, CancellationToken.None);
                if (ended)
                {
                    _logger.LogInformation("Авто-завершение хода в комнате {room}", roomId);
                    await notifier.StateChangedAsync(roomId);
                }
            }
            catch (OperationCanceledException) { /* перезапуск таймера или shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в TurnTimerService для {room}", roomId);
            }
            finally
            {
                // Удаляем только если там всё ещё наш cts (мог быть уже заменён)
                if (_timers.TryGetValue(roomId, out var current) && current == cts)
                    _timers.TryRemove(roomId, out _);
            }
        }, cts.Token);
    }

    public void CancelTimer(Guid roomId)
    {
        if (_timers.TryRemove(roomId, out var cts))
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        foreach (var cts in _timers.Values)
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }
        _timers.Clear();
    }
}