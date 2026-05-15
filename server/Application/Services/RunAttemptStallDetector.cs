using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class RunAttemptStallDetector
{
    private readonly AppDbContext _db;
    private readonly AgentSessionRuntime _runtime;
    private readonly IEventBus _eventBus;
    private readonly AgentSessionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RunAttemptStallDetector> _logger;

    public RunAttemptStallDetector(
        AppDbContext db,
        AgentSessionRuntime runtime,
        IEventBus eventBus,
        IOptions<AgentSessionSettings> settings,
        TimeProvider timeProvider,
        ILogger<RunAttemptStallDetector> logger)
    {
        _db = db;
        _runtime = runtime;
        _eventBus = eventBus;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> ScanAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - TimeSpan.FromMilliseconds(_settings.StallTimeoutMs);
        var attempts = await _db.RunAttempts
            .Include(a => a.AgentSession)
            .Where(a => a.Phase == RunPhase.StreamingTurn
                && a.CompletedAt == null
                && a.LastEventAt < cutoff)
            .OrderBy(a => a.LastEventAt)
            .ToListAsync(ct);

        var stalled = 0;
        foreach (var attempt in attempts)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                RunAttemptStateMachine.Transition(attempt, RunPhase.Stalled, now);
                attempt.ErrorDetails = "No agent output was observed before the configured stall timeout.";

                if (attempt.AgentSession is not null)
                {
                    attempt.AgentSession.Status = SessionStatus.Failed;
                    attempt.AgentSession.EndedAt = now;
                    attempt.AgentSession.LastSeenAt = now;
                    attempt.AgentSession.FailureReason = "Session stalled due to idle output.";

                    await TryKillRuntimeSessionAsync(attempt.AgentSession.Id, ct);
                    await _eventBus.PublishToGroupAsync(
                        AgentSessionGroups.Session(attempt.AgentSession.Id),
                        "RunAttemptStalled",
                        new
                        {
                            runAttemptId = attempt.Id,
                            sessionId = attempt.AgentSession.Id,
                            cardId = attempt.CardId
                        },
                        ct);
                }

                stalled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to mark run attempt {RunAttemptId} as stalled", attempt.Id);
            }
        }

        if (stalled > 0)
            await _db.SaveChangesAsync(ct);

        return stalled;
    }

    private async Task TryKillRuntimeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await _runtime.KillAsync(
                sessionId,
                TimeSpan.FromMilliseconds(Math.Max(100, _settings.KillGraceMs)),
                ct);
            await _runtime.DisposeSessionAsync(sessionId);
        }
        catch (NotFoundException)
        {
            // Session may have exited and been removed between scans.
        }
    }
}
