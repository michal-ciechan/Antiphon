using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0091: discards machine-origin parked messages once their session has no open task. This
/// deliberately reads durable queue/task rows only; session state and transcript liveness are not
/// evidence that a message is stale.
/// </summary>
public sealed class ParkedMessageSweepService
{
    private readonly AppDbContext _db;
    private readonly SessionMessageQueueService _queue;
    private readonly ParkedMessageSweepSettings _settings;
    private readonly SupervisionSettings _supervision;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ParkedMessageSweepService> _logger;

    public ParkedMessageSweepService(
        AppDbContext db,
        SessionMessageQueueService queue,
        IOptions<ParkedMessageSweepSettings> settings,
        IOptions<SupervisionSettings> supervision,
        TimeProvider timeProvider,
        ILogger<ParkedMessageSweepService> logger)
    {
        _db = db;
        _queue = queue;
        _settings = settings.Value;
        _supervision = supervision.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs one pass and returns the count of rows actually canceled.</summary>
    public async Task<int> ScanAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        var maxAttempts = Math.Max(1, _supervision.DeliveryVerification.MaxDeliveryAttempts);
        var floor = _timeProvider.GetUtcNow().UtcDateTime
            - TimeSpan.FromMinutes(Math.Max(0, _settings.MinParkedMinutes));
        var candidates = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Status == QueuedMessageStatus.Pending
                && m.DeliveryAttempts >= maxAttempts
                && m.SourceTaskId == null
                && m.ConversationKey == null
                && (m.Origin == QueuedMessageOrigin.Delegation
                    || m.Origin == QueuedMessageOrigin.System
                    || m.Origin == QueuedMessageOrigin.Check
                    || m.Origin == QueuedMessageOrigin.Supervision)
                && (m.LastDeliveryStartedAt ?? m.CreatedAt) <= floor
                && !_db.AgentTasks.Any(t => t.AgentSessionId == m.AgentSessionId
                    && (t.Status == AgentTaskStatus.Dispatched
                        || t.Status == AgentTaskStatus.Working
                        || t.Status == AgentTaskStatus.Blocked)))
            .Select(m => new Candidate(
                m.Id,
                m.AgentSessionId,
                m.Origin,
                m.DeliveryAttempts,
                m.CreatedAt,
                m.LastDeliveryStartedAt,
                m.Body.Substring(0, 80)))
            .ToListAsync(ct);

        var discarded = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                var owner = await _db.AgentTasks.AsNoTracking()
                    .Where(t => t.AgentSessionId == candidate.SessionId
                        && t.DispatchedAt != null
                        && t.DispatchedAt <= candidate.CreatedAt.AddSeconds(5))
                    .OrderByDescending(t => t.DispatchedAt)
                    .Select(t => new { t.Id, t.Status })
                    .FirstOrDefaultAsync(ct);

                if (_settings.DryRun)
                {
                    LogDiscard("Would discard", candidate, owner?.Id, owner?.Status);
                    continue;
                }

                if (await _queue.CancelParkedIfStaleAsync(candidate.SessionId, candidate.Id, ct))
                {
                    discarded++;
                    LogDiscard("Discarded", candidate, owner?.Id, owner?.Status);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Parked-message sweep failed for message {MessageId} on session {SessionId}",
                    candidate.Id,
                    candidate.SessionId);
            }
        }

        if (discarded > 0)
            _logger.LogInformation("Parked-message sweep discarded {Count} stale message(s)", discarded);

        return discarded;
    }

    private void LogDiscard(
        string action,
        Candidate candidate,
        Guid? owningTaskId,
        AgentTaskStatus? owningTaskStatus)
    {
        _logger.LogInformation(
            "{Action} parked message {MessageId} on session {SessionId}: Origin={Origin}, "
            + "DeliveryAttempts={DeliveryAttempts}, ParkedSinceUtc={ParkedSinceUtc:o}, "
            + "OwningTaskId={OwningTaskId}, OwningTaskStatus={OwningTaskStatus}, BodyHead={BodyHead}",
            action,
            candidate.Id,
            candidate.SessionId,
            candidate.Origin,
            candidate.DeliveryAttempts,
            candidate.LastDeliveryStartedAt ?? candidate.CreatedAt,
            owningTaskId,
            owningTaskStatus,
            candidate.Head);
    }

    private sealed record Candidate(
        Guid Id,
        Guid SessionId,
        QueuedMessageOrigin Origin,
        int DeliveryAttempts,
        DateTime CreatedAt,
        DateTime? LastDeliveryStartedAt,
        string Head);
}
