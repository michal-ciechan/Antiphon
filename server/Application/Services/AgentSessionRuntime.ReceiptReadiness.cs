using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public enum ReceiptReadinessStatus { Unknown, Behind, Ready }

public sealed record ReceiptReadinessObservation(
    ReceiptReadinessStatus Status, string Reason, long? BaselineSequence = null,
    string? BindingId = null, long? ParsedThrough = null, long? ObservedLength = null);

public sealed partial class AgentSessionRuntime
{
    /// <summary>
    /// Persists a fresh live-reader snapshot before qualifying absence. This is an observation,
    /// never a delivery receipt or authority to write input/stop a process. Remote and local
    /// routes use the same captured ownership coordinates. No lease spans the runner call.
    /// </summary>
    public async Task<ReceiptReadinessObservation> ObserveReceiptReadinessAsync(
        Guid sessionId, DateTime expectedGeneration, Guid standingAgentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var started = _timeProvider.GetTimestamp();
        SessionRunnerTranscriptReadiness? progress = null;
        ReceiptReadinessObservation Result(ReceiptReadinessStatus status, string reason, long? baseline = null)
        {
            _logger.LogDebug("Receipt readiness session {SessionId} generation {Generation} status {Status} reason {Reason} parsed {ParsedThrough} length {ObservedLength} elapsedMs {ElapsedMs}",
                sessionId, expectedGeneration, status, reason, progress?.ParsedThrough, progress?.ObservedLength,
                _timeProvider.GetElapsedTime(started).TotalMilliseconds);
            return new(status, reason, baseline, progress?.BindingId, progress?.ParsedThrough, progress?.ObservedLength);
        }
        if (_states is null) return Result(ReceiptReadinessStatus.Unknown, "store_absent");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        var token = budget.Token;
        try
        {
            ReceiptLaunchStamp? before;
            SessionStateSnapshot stamp;
            using (var lease = await _states.BeginWriteAsync(sessionId, token))
            {
                before = await ReadReceiptLaunchStampAsync(sessionId, standingAgentId, token);
                if (before is null || !SessionGeneration.Equal(before.Generation, expectedGeneration))
                    return Result(ReceiptReadinessStatus.Unknown, "ownership_unavailable");
                stamp = lease.Snapshot;
            }

            var snapshot = await _runnerClient.GetTranscriptAsync(sessionId, token);
            token.ThrowIfCancellationRequested();
            progress = snapshot.Readiness;
            if (snapshot.SessionId != sessionId || snapshot.Entries is null
                || snapshot.Entries.Any(e => e.SessionId != sessionId))
                return Result(ReceiptReadinessStatus.Unknown, "session_mismatch");
            if (snapshot.AcceptedStartedAt is null
                || !SessionGeneration.Equal(snapshot.AcceptedStartedAt, expectedGeneration))
                return Result(ReceiptReadinessStatus.Unknown, "generation_mismatch");
            long previous = 0;
            foreach (var entry in snapshot.Entries)
            {
                if (entry.Sequence <= previous) return Result(ReceiptReadinessStatus.Unknown, "incomplete_snapshot");
                previous = entry.Sequence;
            }
            if (snapshot.LastSequence != previous)
                return Result(ReceiptReadinessStatus.Unknown, "incomplete_snapshot");

            using (var lease = await _states.BeginWriteAsync(sessionId, token))
            {
                if (await ReadReceiptLaunchStampAsync(sessionId, standingAgentId, token) != before)
                    return Result(ReceiptReadinessStatus.Unknown, "ownership_changed");
                if (lease.Snapshot.ServerEpoch != stamp.ServerEpoch || lease.Snapshot.Revision != stamp.Revision
                    || lease.Snapshot.ResetEpoch != stamp.ResetEpoch)
                    return Result(ReceiptReadinessStatus.Unknown, "frontier_changed");
                PersistResult persisted;
                try
                {
                    persisted = await PersistTranscriptCoreAsync(sessionId, snapshot.Entries,
                        expectedReceipt: before, ct: token);
                }
                catch (OperationCanceledException)
                {
                    // A cancellation can obscure a completed commit. Reconcile before releasing
                    // the lease; the caller still gets Unknown (or its own cancellation), not Ready.
                    await lease.ReconcileAsync(false, CancellationToken.None);
                    throw;
                }
                await PublishCommittedAsync(lease, persisted);
                token.ThrowIfCancellationRequested();
                if (persisted.RecoveryFenceRejection is { } rejection)
                    return Result(ReceiptReadinessStatus.Unknown, rejection);
                if (persisted.NeedsReload || TryGetTranscriptPersistFailure(sessionId, out _))
                    return Result(ReceiptReadinessStatus.Unknown, "persist_uncertain");
                if (await ReadReceiptLaunchStampAsync(sessionId, standingAgentId, token) != before)
                    return Result(ReceiptReadinessStatus.Unknown, "ownership_changed_after_persist");
                token.ThrowIfCancellationRequested();
                // Behind/legacy snapshots still ingest their available positive receipt evidence.
                // Only a current complete binding qualifies the negative (absence) observation.
                if (progress is null || string.IsNullOrWhiteSpace(progress.BindingId)
                    || progress.ParsedThrough < 0 || progress.ObservedLength < progress.ParsedThrough)
                    return Result(ReceiptReadinessStatus.Unknown, "reader_unknown");
                if (progress.Status == "Behind") return Result(ReceiptReadinessStatus.Behind, "reader_behind");
                if (progress.Status != "Ready" || progress.ParsedThrough != progress.ObservedLength)
                    return Result(ReceiptReadinessStatus.Unknown, "reader_unknown");
                return Result(ReceiptReadinessStatus.Ready, "committed_current_snapshot", lease.Snapshot.LastSequence);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result(ReceiptReadinessStatus.Unknown, "observation_timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Receipt readiness unavailable for session {SessionId}", sessionId);
            return Result(ReceiptReadinessStatus.Unknown, "observation_failed");
        }
    }

    private sealed record ReceiptLaunchStamp(DateTime Generation, string? RunnerId,
        Guid? RunnerStoreId, string? RunnerCwd, Guid StandingAgentId);

    private async Task<ReceiptLaunchStamp?> ReadReceiptLaunchStampAsync(
        Guid sessionId, Guid standingAgentId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return await ReadReceiptLaunchStampAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            sessionId, standingAgentId, ct);
    }

    private static async Task<ReceiptLaunchStamp?> ReadReceiptLaunchStampAsync(
        AppDbContext db, Guid sessionId, Guid standingAgentId, CancellationToken ct) =>
        await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId
            && s.Status == SessionStatus.Running && s.StartedAt != default
            && s.StandingAgentId == standingAgentId
            && db.Agents.Any(a => a.Id == standingAgentId && a.PersistentSessionId == sessionId.ToString("D")))
            .Select(s => new ReceiptLaunchStamp(s.StartedAt, s.RunnerId, s.RunnerStoreId, s.RunnerCwd, standingAgentId))
            .SingleOrDefaultAsync(ct);
}
