using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Entities;
using PictureNames.Server.Services.Notifications;
using System.Collections.Concurrent;

namespace PictureNames.Server.Services;

// Голосование оперативников: за карту или за завершение хода.
// Живёт в памяти: сессия 5 секунд после набора порога.
public class VoteService
{
    public const int VoteSeconds = 5;

    public enum VoteTargetKind { Card, EndTurn }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VoteService> _logger;

    private readonly ConcurrentDictionary<Guid, VoteSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _timers = new();

    public VoteService(IServiceScopeFactory scopeFactory, ILogger<VoteService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private class VoteSession
    {
        public VoteTargetKind Kind { get; set; }
        public Guid? CardId { get; set; }
        public HashSet<Guid> Voters { get; } = new();
        public Dictionary<Guid, string> VoterColors { get; } = new();
        public DateTime? DeadlineUtc { get; set; }
    }

    public VoteSessionSnapshot? GetSnapshot(Guid roomId)
    {
        if (!_sessions.TryGetValue(roomId, out var s)) return null;
        if (s.Voters.Count == 0) return null;
        return new VoteSessionSnapshot(
            s.Kind,
            s.CardId,
            s.Voters.ToList(),
            new Dictionary<Guid, string>(s.VoterColors),
            s.DeadlineUtc
        );
    }

    public record VoteSessionSnapshot(
        VoteTargetKind Kind,
        Guid? CardId,
        List<Guid> Voters,
        Dictionary<Guid, string> VoterColors,
        DateTime? DeadlineUtc
    );

    public Task ToggleCardVoteAsync(Guid roomId, Guid playerId, Guid cardId, CancellationToken ct = default)
        => ToggleInternalAsync(roomId, playerId, VoteTargetKind.Card, cardId, ct);

    public Task ToggleEndTurnVoteAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
        => ToggleInternalAsync(roomId, playerId, VoteTargetKind.EndTurn, null, ct);

    private async Task ToggleInternalAsync(
        Guid roomId, Guid playerId, VoteTargetKind kind, Guid? cardId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGameNotifier>();

        var room = await db.Rooms
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        if (room is null || room.State != RoomState.InGame) return;

        var player = room.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null) return;
        if (player.Role != PlayerRole.Operative) return;
        if (player.TeamId != room.CurrentTurnTeamId) return;
        if (room.ClueWord is null) return;

        if (kind == VoteTargetKind.Card)
        {
            if (!cardId.HasValue) return;
            var card = await db.Cards
                .FirstOrDefaultAsync(c => c.Id == cardId.Value && c.RoomId == roomId, ct);
            if (card is null || card.IsRevealed) return;
        }

        // На случай старых записей без VoteColor — присваиваем на лету
        if (string.IsNullOrEmpty(player.VoteColor))
        {
            var used = room.Players.Where(p => p.Id != playerId).Select(p => p.VoteColor);
            player.VoteColor = VotePalette.PickUnused(used);
            await db.SaveChangesAsync(ct);
        }

        var operatives = room.Players
            .Where(p => p.TeamId == room.CurrentTurnTeamId
                     && p.Role == PlayerRole.Operative
                     && p.IsConnected)
            .ToList();

        // Один оперативник — сразу действие
        if (operatives.Count <= 1)
        {
            if (kind == VoteTargetKind.Card && cardId.HasValue)
                await RevealViaVoteAsync(roomId, cardId.Value);
            else if (kind == VoteTargetKind.EndTurn)
                await EndTurnViaVoteAsync(roomId);
            return;
        }

        var session = _sessions.GetOrAdd(roomId, _ => new VoteSession { Kind = kind, CardId = cardId });

        // Смена цели голосования — полный сброс
        if (session.Kind != kind || session.CardId != cardId)
        {
            session = new VoteSession { Kind = kind, CardId = cardId };
            _sessions[roomId] = session;
            CancelTimer(roomId);
        }

        if (session.Voters.Contains(playerId))
        {
            session.Voters.Remove(playerId);
            session.VoterColors.Remove(playerId);
        }
        else
        {
            session.Voters.Add(playerId);
            session.VoterColors[playerId] = player.VoteColor!;
        }

        await RecalculateAsync(roomId, room, operatives, session, notifier);
    }

    public async Task OnPlayerDisconnectedAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(roomId, out var session)) return;

        session.Voters.Remove(playerId);
        session.VoterColors.Remove(playerId);

        if (session.Voters.Count == 0)
        {
            _sessions.TryRemove(roomId, out _);
            CancelTimer(roomId);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGameNotifier>();

        var room = await db.Rooms
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        if (room is null || room.State != RoomState.InGame)
        {
            ClearSession(roomId);
            return;
        }

        var operatives = room.Players
            .Where(p => p.TeamId == room.CurrentTurnTeamId
                     && p.Role == PlayerRole.Operative
                     && p.IsConnected)
            .ToList();

        await RecalculateAsync(roomId, room, operatives, session, notifier);
    }

    public void ClearSession(Guid roomId)
    {
        _sessions.TryRemove(roomId, out _);
        CancelTimer(roomId);
    }

    private async Task RecalculateAsync(
        Guid roomId,
        Room room,
        List<Player> operatives,
        VoteSession session,
        IGameNotifier notifier)
    {
        int needed = operatives.Count;

        var validIds = operatives.Select(o => o.Id).ToHashSet();
        session.Voters.RemoveWhere(id => !validIds.Contains(id));
        foreach (var key in session.VoterColors.Keys.Where(k => !validIds.Contains(k)).ToList())
            session.VoterColors.Remove(key);

        int have = session.Voters.Count;

        if (have >= needed && needed > 0)
        {
            if (session.DeadlineUtc is null)
            {
                var deadline = DateTime.UtcNow.AddSeconds(VoteSeconds);
                session.DeadlineUtc = deadline;
                ScheduleAction(roomId, session.Kind, session.CardId, deadline);
            }
        }
        else
        {
            if (session.DeadlineUtc is not null)
            {
                session.DeadlineUtc = null;
                CancelTimer(roomId);
            }
        }

        await notifier.StateChangedAsync(roomId);
    }

    private void ScheduleAction(Guid roomId, VoteTargetKind kind, Guid? cardId, DateTime deadlineUtc)
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

                if (!_sessions.TryGetValue(roomId, out var s)) return;
                if (s.Kind != kind) return;
                if (s.CardId != cardId) return;
                if (s.DeadlineUtc != deadlineUtc) return;

                if (kind == VoteTargetKind.Card && cardId.HasValue)
                    await RevealViaVoteAsync(roomId, cardId.Value);
                else if (kind == VoteTargetKind.EndTurn)
                    await EndTurnViaVoteAsync(roomId);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка авто-действия в комнате {room}", roomId);
            }
            finally
            {
                if (_timers.TryGetValue(roomId, out var c) && c == cts)
                {
                    _timers.TryRemove(roomId, out _);
                    try { cts.Dispose(); } catch { }
                }
                _sessions.TryRemove(roomId, out _);
            }
        }, cts.Token);
    }

    private void CancelTimer(Guid roomId)
    {
        if (_timers.TryRemove(roomId, out var cts))
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }
    }

    private async Task RevealViaVoteAsync(Guid roomId, Guid cardId)
    {
        using var scope = _scopeFactory.CreateScope();
        var game = scope.ServiceProvider.GetRequiredService<GameService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGameNotifier>();
        var hub = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Hubs.GameHub>>();

        try
        {
            var result = await game.RevealCardAsync(roomId, playerId: null, cardId: cardId);

            await hub.Clients
                .Group(Hubs.GameHub.RoomGroup(roomId))
                .SendAsync("RevealOutcome", new
                {
                    cardId,
                    outcome = result.Outcome.ToString(),
                    bonusSeconds = result.BonusSeconds
                });

            if (result.BonusSeconds > 0)
            {
                await hub.Clients
                    .Group(Hubs.GameHub.RoomGroup(roomId))
                    .SendAsync("TurnBonus", new { seconds = result.BonusSeconds });
            }

            await notifier.StateChangedAsync(roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть карту по голосованию");
        }
    }

    private async Task EndTurnViaVoteAsync(Guid roomId)
    {
        using var scope = _scopeFactory.CreateScope();
        var game = scope.ServiceProvider.GetRequiredService<GameService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGameNotifier>();

        try
        {
            var ok = await game.EndTurnSystemAsync(roomId);
            if (ok)
            {
                _sessions.TryRemove(roomId, out _);
                await notifier.StateChangedAsync(roomId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось завершить ход по голосованию");
        }
    }
}