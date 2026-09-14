using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0514 D-4: passive rendered-screen sweep for live RC-capable sessions, independent of
/// connection-watch enablement. Global supervision shutdown still stops the hosted caller.
/// </summary>
public sealed class RemoteControlModalWatchService
{
    private static readonly SessionStatus[] LiveStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly AppDbContext _db;
    private readonly ISessionRunnerClient _runner;
    private readonly RemoteControlRecoveryService _recovery;
    private readonly SessionMessageQueueService _queue;
    private readonly SupervisionSettings _settings;
    private readonly ILogger<RemoteControlModalWatchService> _logger;

    public RemoteControlModalWatchService(
        AppDbContext db,
        ISessionRunnerClient runner,
        RemoteControlRecoveryService recovery,
        SessionMessageQueueService queue,
        IOptions<SupervisionSettings> settings,
        ILogger<RemoteControlModalWatchService> logger)
    {
        _db = db;
        _runner = runner;
        _recovery = recovery;
        _queue = queue;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        if (!_settings.Enabled || !_settings.RcModalWatch.Enabled)
            return 0;

        IReadOnlyList<SessionRunnerSessionDto> runnerSessions;
        try
        {
            runnerSessions = await _runner.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Modal watch skipped: runner unavailable");
            return 0;
        }

        var liveIds = runnerSessions.Select(s => s.SessionId).ToHashSet();
        var candidates = await _db.AgentSessions.AsNoTracking()
            .Where(s => liveIds.Contains(s.Id) && LiveStatuses.Contains(s.Status))
            .ToListAsync(ct);
        var observed = 0;
        foreach (var session in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!RemoteControlPolicy.Permits(session.AgentKind))
                continue;
            try
            {
                if (await ObserveOneAsync(session, ct))
                    observed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Modal watch failed for session {SessionId}", session.Id);
            }
        }

        return observed;
    }

    private async Task<bool> ObserveOneAsync(AgentSession session, CancellationToken ct)
    {
        var generation = SessionGeneration.Normalize(session.StartedAt);
        RemoteControlScreenObservation observation;
        try
        {
            observation = await _recovery.ObserveAsync(session.Id, generation, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Modal snapshot failed for {SessionId}; continuing sweep", session.Id);
            return false;
        }

        if (observation.Reason == "read-failure")
            return false;

        if (observation.ObservedGeneration is { } seen
            && !SessionGeneration.Equal(seen, generation))
        {
            await _recovery.CloseGenerationEndedAsync(session.Id, generation, ct);
            return true;
        }

        var episode = await _recovery.DetectAsync(session.Id, generation, observation, relatedQueueId: null, ct);
        if (episode is null)
            return observation.Menu.IsPresent || observation.Menu.HasRemnant;

        if (!observation.Menu.IsPresent)
            return true;

        var sem = _queue.GetLock(session.Id);
        var entered = await sem.WaitAsync(TimeSpan.Zero, ct);
        if (!entered)
            return true;
        RemoteControlDismissalResult? result = null;
        try
        {
            result = await _recovery.TryDismissIdleUnderLockAsync(session.Id, episode.Id, ct);
        }
        finally
        {
            sem.Release();
        }

        if (result is RemoteControlDismissalResult.DismissedVerified
            or RemoteControlDismissalResult.ObservedClear)
            await _queue.FlushSessionAsync(session.Id, ct);

        return true;
    }
}
