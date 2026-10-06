using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class TerminalRunnerSeatReleaseOptions
{
    public const string SectionName = "TerminalRunnerSeatRelease";
    public bool AutomaticEnabled { get; set; } = false;
}

// Shared by the existing job and dispatcher; losing traversal position on restart is safe.
public sealed class TerminalRunnerSeatDiscoveryState
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal RunnerSeatDiscoveryCursor? Continuation { get; set; }
}

public sealed record TerminalRunnerSeatReservation(Guid? ReleaseId, TerminalRunnerSeatDecision Decision);

public sealed record TerminalRunnerSeatEvidence(
    TerminalRunnerSeatDecision? Hold,
    TerminalSeatObservationRequest? Request = null,
    TerminalSeatObservation? Observation = null);

/// <summary>
/// Only committed attempts can register debt. A durable send intent precedes runner I/O;
/// interrupted sends reconcile authoritative inventory and the bound source proof before any
/// further mutation. Production hooks use fresh scopes; automatic release ships disabled.
/// </summary>
public sealed class TerminalRunnerSeatReleaseService(
    AppDbContext db, TerminalRunnerSeatReleasePolicy policy, SessionMessageQueueService queue,
    SessionStateStore states, ISessionRunnerDirectory runners, TimeProvider clock,
    IOptions<TerminalRunnerSeatReleaseOptions> options, IEventBus events,
    ILogger<TerminalRunnerSeatReleaseService> logger, TerminalRunnerSeatDiscoveryState discovery,
    BlockedTaskParkingService? parks = null, TaskParkPublicationService? publication = null,
    IOptions<BlockedTaskParkingOptions>? parkingOptions = null)
{
    internal Func<string, CancellationToken, Task>? BoundaryAsync { get; set; }
    private bool ParkingEnabled => parkingOptions?.Value.Enabled == true;
    private TerminalRunnerSeatDecision? TerminalAttempt(AgentTask? task) => policy.TerminalAttempt(task, ParkingEnabled);

    public bool OwnsAutomaticPath(AgentTask task) => options.Value.AutomaticEnabled
        && !string.IsNullOrWhiteSpace(task.RunnerId)
        && (!RunnerRequestIntent.IsDesktopAlias(task.RunnerId) || task.Status == AgentTaskStatus.Blocked && ParkingEnabled)
        && task.SourceLandingOperationId is null;

    /// <summary>True means this path owns disposition, including a hold or unavailable peer.
    /// Callers must never fall through to the ordinary stopper after a conditional refusal.</summary>
    public async Task<bool> TryHandleTaskAsync(Guid taskId, CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled) return false;
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !OwnsAutomaticPath(task)) return false;
        try
        {
            if (task.Status == AgentTaskStatus.Blocked)
            {
                if (!ParkingEnabled || parks is null || publication is null) return true;
                var blockId = await SettlementEventAsync(task, ct);
                if (blockId is null) return true;
                var parkId = await parks.RegisterAsync(task.Id, task.Attempt, blockId.Value, task.ConcurrencyToken, ct);
                if (parkId is null || task.AgentSessionId is null) return true;
                var park = await db.AgentTaskParks.AsNoTracking().SingleAsync(p => p.Id == parkId, ct);
                if (park.State == AgentTaskParkState.Parked) return true;
                if (park.PublicationReceiptId is null && (await publication.PrepareAsync(park.Id, ct)).Evidence is null) return true;
            }
            if (task.AgentSessionId is not Guid sessionId) return true;
            var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session?.RunnerStoreId is not Guid store || session.RunnerId != task.RunnerId) return true;
            await RegisterAndReleaseAsync(taskId,
                new(store, session.StartedAt, "", -1, UseCapturedDeliveryEvidence: true), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Terminal runner seat release remains pending for task {TaskId}", taskId);
        }
        return true;
    }

    public async Task<int> DiscoverScheduledAsync(CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled || !await discovery.Gate.WaitAsync(0, ct)) return 0;
        try
        {
            var result = await DiscoverAsync(32, 4, discovery.Continuation, ct);
            discovery.Continuation = result.Continuation;
            return result.Released;
        }
        finally { discovery.Gate.Release(); }
    }

    public async Task<RunnerSlotReleaseDto> ReleaseOrphansAsync(string runnerId, string? reason, CancellationToken ct)
    {
        RunnerSlotService.RequireReason(reason);
        if (!options.Value.AutomaticEnabled) return new(0, [], Candidates: 0, Deferred: 0);
        if (await DiscoveryInventoryAsync(runnerId, ct) is not RunnerInventory.Available inventory)
            return new(0, [], Candidates: 0, Deferred: 0);
        var items = new List<RunnerSeatDiscoveryItem>();
        foreach (var seat in inventory.Sessions.OrderBy(s => s.SessionId).Take(32))
        {
            try { items.Add(await DiscoverCandidateAsync(runnerId, seat, ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            { items.Add(new(runnerId, seat.SessionId, null, "Unknown")); }
        }
        var released = items.Where(i => i.Disposition == "Confirmed").Select(i => i.SessionId).ToArray();
        return new(released.Length, released, Candidates: items.Count,
            Deferred: items.Count - released.Length, Dispositions: items);
    }

    /// <summary>Bounded candidate processing over fresh complete inventory RPCs. The caller
    /// retains the returned cursor between ticks; losing it only restarts enumeration, never
    /// the durable release identity.</summary>
    public async Task<RunnerSeatDiscoveryResult> DiscoverAsync(int candidateBudget, int pageSize,
        RunnerSeatDiscoveryCursor? continuation, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var positions = continuation?.AfterSession.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, Guid>(StringComparer.Ordinal);
        var next = continuation?.NextRunnerId;
        var results = new List<RunnerSeatDiscoveryItem>();
        var calls = 0;
        var released = 0;
        if (!options.Value.AutomaticEnabled || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            return new(results, released, calls, new(next, positions));
        var runnerIds = runners.KnownRunnerIds.Select(id => RunnerRequestIntent.IsDesktopAlias(id)
                ? RunnerPlatformWire.DesktopId : id)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // Discard positions for removed peers, keeping cursor space bounded by the catalogue.
        foreach (var removed in positions.Keys.Except(runnerIds).ToArray()) positions.Remove(removed);
        if (runnerIds.Length == 0) return new(results, released, calls, new(null, positions));
        var start = Math.Max(0, Array.IndexOf(runnerIds, next));
        var pages = new List<(string Runner, Queue<SessionRunnerSessionDto> Seats)>();
        // At most budget inventory calls, including failed/empty runners. Each RPC retains its
        // existing transport deadline. Candidate pages are in memory, not partial wire Lists.
        for (var offset = 0; offset < Math.Min(candidateBudget, runnerIds.Length); offset++)
        {
            var index = (start + offset) % runnerIds.Length;
            var runnerId = runnerIds[index];
            next = runnerIds[(index + 1) % runnerIds.Length];
            calls++;
            try
            {
                if (await DiscoveryInventoryAsync(runnerId, ct) is not RunnerInventory.Available inventory) continue;
                var seats = inventory.Sessions.OrderBy(s => s.SessionId).ToArray();
                var after = positions.GetValueOrDefault(runnerId);
                // UUID ordering is only traversal order; no age or identity authority is inferred.
                pages.Add((runnerId, new Queue<SessionRunnerSessionDto>(
                    seats.Where(s => s.SessionId.CompareTo(after) > 0)
                        .Concat(seats.Where(s => s.SessionId.CompareTo(after) <= 0)))));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            { /* One unavailable runner cannot hide another runner's seats. */ }
        }
        while (pages.Any(p => p.Seats.Count > 0))
        {
            foreach (var page in pages)
            {
                for (var count = 0; count < pageSize && page.Seats.TryDequeue(out var seat); count++)
                {
                    // Count attempted candidates too, including a poison record. Advance before
                    // processing so a recurring exception cannot pin the next pass to this seat.
                    if (results.Count >= candidateBudget)
                        return new(results, released, calls, new(next, positions));
                    positions[page.Runner] = seat.SessionId;
                    next = runnerIds[(Array.IndexOf(runnerIds, page.Runner) + 1) % runnerIds.Length];
                    try
                    {
                        if (BoundaryAsync is not null) await BoundaryAsync("Discovery:" + seat.SessionId.ToString("D"), ct);
                        var item = await DiscoverCandidateAsync(page.Runner, seat, ct);
                        results.Add(item);
                        if (item.Disposition == "Confirmed") released++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        results.Add(new(page.Runner, seat.SessionId, null, "Unknown"));
                    }
                }
            }
        }
        return new(results, released, calls, new(next, positions));
    }

    private async Task<RunnerSeatDiscoveryItem> DiscoverCandidateAsync(
        string runnerId, SessionRunnerSessionDto seat, CancellationToken ct)
    {
        if (seat.SessionId == Guid.Empty || seat.AcceptedStartedAt is not DateTime generation
            || generation == default || seat.Pending is not null || seat.VerificationBinding is not null)
            return new(runnerId, seat.SessionId, null, "IdentityUnknown");
        var descriptor = await runners.DescribeAsync(runnerId, ct);
        if (descriptor?.Capabilities?.RunnerStoreId is not Guid storeId
            || await CapturedPeerHoldAsync(runnerId, storeId, ct) is not null)
            return new(runnerId, seat.SessionId, null, "Unknown");
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seat.SessionId, ct);
        // Local nonpark conversations retain their existing owner. A Blocked park still
        // needs the same source, generation and input fences as a remote one.
        if (session is not null && (session.RunnerId != runnerId || session.RunnerStoreId != storeId
            || session.StartedAt != generation)) return new(runnerId, seat.SessionId, null, "IdentityUnknown");
        var task = await db.AgentTasks.AsNoTracking().Where(t => t.AgentSessionId == seat.SessionId)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Attempt).FirstOrDefaultAsync(ct);
        // Use the same captured-mode producer as the evidence seam. Never construct a native
        // floor from inventory, server ingestion or an already-idle transcript.
        var evidence = await ObserveCapturedCandidateAsync(runnerId, storeId, seat.SessionId, generation, ct);
        if (evidence.Request is null) return new(runnerId, seat.SessionId, null, evidence.Hold?.ToString() ?? "Unknown");
        Guid? releaseId;
        if (task is not null)
        {
            if (task.RunnerId != runnerId) return new(runnerId, seat.SessionId, null, "Owned");
            if (task.Status == AgentTaskStatus.Blocked)
            {
                await TryHandleTaskAsync(task.Id, ct);
                var parkedRelease = await FindAttemptReleaseAsync(db, task, ct);
                return new(runnerId, seat.SessionId, parkedRelease?.Id,
                    IsConfirmed(parkedRelease) ? "Confirmed" : "ParkPending");
            }
            if (RunnerRequestIntent.IsDesktopAlias(runnerId)) return new(runnerId, seat.SessionId, null, "Owned");
            releaseId = await RegisterAndReleaseAsync(task.Id, evidence.Request, ct);
        }
        else if (session is null)
        {
            releaseId = await RegisterRowlessAsync(runnerId, seat.SessionId, evidence.Request, ct);
            try { await AdvanceAsync(releaseId.Value, evidence.Request, ct); }
            finally { await PublishAttentionAsync(releaseId.Value, ct); }
        }
        else return new(runnerId, seat.SessionId, null, "Owned");
        if (releaseId is null) return new(runnerId, seat.SessionId, null, "Owned");
        var release = await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r => r.Id == releaseId, ct);
        return new(runnerId, seat.SessionId, releaseId, release.State == RunnerSeatReleaseState.Confirmed
            ? "Confirmed" : release.OutcomeCode ?? release.ReasonCode);
    }

    private async Task<Guid> RegisterRowlessAsync(string runnerId, Guid sessionId,
        TerminalSeatObservationRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RunnerSeatReleases" ("Id", "RunnerId", "RunnerStoreId", "SessionId",
                "AcceptedStartedAt", "State", "Revision", "ReasonCode", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {runnerId}, {request.ExpectedRunnerStoreId}, {sessionId},
                {request.ExpectedAcceptedStartedAt}, {0}, {0L}, {"Observing"}, {now}, {now})
            ON CONFLICT ("RunnerId", "RunnerStoreId", "SessionId", "AcceptedStartedAt") DO NOTHING
            """, ct);
        var release = await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r => r.RunnerId == runnerId
            && r.RunnerStoreId == request.ExpectedRunnerStoreId && r.SessionId == sessionId
            && r.AcceptedStartedAt == request.ExpectedAcceptedStartedAt, ct);
        if (release.State != RunnerSeatReleaseState.Observing || release.TaskId is not null) return release.Id;
        var gate = queue.GetLock(sessionId);
        if (!await gate.WaitAsync(0, ct)) return release.Id;
        try
        {
            if (await RowlessHoldAsync(release, ct) is { } ownerHold)
            {
                await HoldAsync(release, ownerHold, ct);
                return release.Id;
            }
            var evidence = await ObserveCapturedCandidateAsync(runnerId, release.RunnerStoreId,
                sessionId, release.AcceptedStartedAt, ct);
            if (evidence.Hold is { } hold)
            {
                await HoldAsync(release, hold, ct);
                return release.Id;
            }
            if (BoundaryAsync is not null) await BoundaryAsync("BeforeReservation", ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockIdentityAsync(release, ct);
            if (await RowlessHoldAsync(release, ct) is null)
            {
                var observed = evidence.Observation!;
                await db.RunnerSeatReleases.Where(r => r.Id == release.Id && r.Revision == release.Revision
                    && r.State == RunnerSeatReleaseState.Observing && r.ActionId == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Reserved)
                        .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.ActionId, Guid.NewGuid())
                        .SetProperty(r => r.ReasonCode, "Reserved:Rowless")
                        .SetProperty(r => r.ObservationToken, observed.Token)
                        .SetProperty(r => r.BindingIdentity, observed.Transcript.BindingIdentity)
                        .SetProperty(r => r.FileRevision, observed.Transcript.FileRevision)
                        .SetProperty(r => r.TranscriptRevision, observed.Transcript.TranscriptRevision)
                        .SetProperty(r => r.FirstStableObservedAt, now).SetProperty(r => r.LastObservedAt, now)
                        .SetProperty(r => r.UpdatedAt, now), ct);
            }
            await tx.CommitAsync(ct);
        }
        finally { gate.Release(); }
        return release.Id;
    }

    private async Task<TerminalRunnerSeatDecision?> RowlessHoldAsync(RunnerSeatRelease release, CancellationToken ct)
    {
        // Any newly materialized row or owner defeats rowless authority. Never manufacture a
        // server session just to pass through the ordinary stop API.
        var sessionKey = release.SessionId.ToString("D");
        if (await db.AgentSessions.AnyAsync(s => s.Id == release.SessionId, ct)
            || await db.Agents.AnyAsync(a => a.PersistentSessionId == sessionKey, ct)
            || await db.AgentTasks.AnyAsync(t => t.AgentSessionId == release.SessionId, ct))
            return TerminalRunnerSeatDecision.Owned;
        if (await HasPendingDeliveryAsync(release.SessionId, ct)) return TerminalRunnerSeatDecision.PendingDelivery;
        return null;
    }

    private async Task<RunnerInventory> DiscoveryInventoryAsync(string runnerId, CancellationToken ct) =>
        RunnerRequestIntent.IsDesktopAlias(runnerId)
            ? new RunnerInventory.Available(await runners.Local.ListAsync(ct))
            : await runners.GetInventoryAsync(runnerId, ct);

    private ISessionRunnerClient SeatClient(string runnerId) =>
        RunnerRequestIntent.IsDesktopAlias(runnerId) ? runners.Local : runners.Resolve(runnerId);

    /// <summary>Acquire runner-owned evidence for an inventory identity, including a seat with
    /// no server row. This does not reserve debt or grant ownership authority to release it.</summary>
    public async Task<TerminalRunnerSeatEvidence> ObserveCapturedCandidateAsync(
        string runnerId, Guid runnerStoreId, Guid sessionId, DateTime? acceptedStartedAt, CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled) return new(TerminalRunnerSeatDecision.Disabled);
        if (string.IsNullOrWhiteSpace(runnerId) || runnerStoreId == Guid.Empty || sessionId == Guid.Empty
            || acceptedStartedAt is null || acceptedStartedAt == default(DateTime))
            return new(TerminalRunnerSeatDecision.IdentityUnknown);
        TerminalSeatObservationRequest? request = null;
        try
        {
            if (await CapturedPeerHoldAsync(runnerId, runnerStoreId, ct) is { } hold) return new(hold);
            // No server ingestion sequence or retroactive native floor is release evidence.
            // Only the runner's pre-write capture can fill these deliberately unusable fields.
            request = new(runnerStoreId, acceptedStartedAt.Value, "", -1, UseCapturedDeliveryEvidence: true);
            var observed = await SeatClient(runnerId).ObserveTerminalSeatAsync(sessionId, request, ct);
            if (await CapturedPeerHoldAsync(runnerId, runnerStoreId, ct) is { } afterRead)
                return new(afterRead, request);
            if (observed?.Transcript is not { } transcript) return new(TerminalRunnerSeatDecision.Unknown, request);
            if (observed.Status != TerminalSeatQualificationStatus.Qualified)
                return new(observed.Status switch
                {
                    TerminalSeatQualificationStatus.Working => TerminalRunnerSeatDecision.Working,
                    TerminalSeatQualificationStatus.Waiting => TerminalRunnerSeatDecision.Waiting,
                    _ => TerminalRunnerSeatDecision.Unknown
                }, request, observed);
            // Treat a malformed "Qualified" response as unknown, never as a release receipt.
            if (transcript.Status != TerminalTranscriptReadStatus.Success
                || transcript.Verdict != TerminalTranscriptVerdict.Idle
                || string.IsNullOrWhiteSpace(transcript.BindingIdentity)
                || string.IsNullOrWhiteSpace(transcript.FileRevision)
                || transcript.LastPromptRevision is not long prompt || prompt <= 0
                || transcript.LastEndRevision is not long end || end <= prompt
                || transcript.TranscriptRevision < end
                || observed.StableFor < TimeSpan.FromSeconds(120) || string.IsNullOrWhiteSpace(observed.Token))
                return new(TerminalRunnerSeatDecision.Unknown, request);
            return new(null, request, observed);
        }
        catch (NotSupportedException) { return new(TerminalRunnerSeatDecision.Unsupported, request); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Bounded hold only: a lost/malformed reply proves neither absence nor idleness.
            return new(TerminalRunnerSeatDecision.Unknown, request);
        }
    }

    private async Task<TerminalRunnerSeatDecision?> CapturedPeerHoldAsync(
        string runnerId, Guid runnerStoreId, CancellationToken ct)
    {
        if (runners.RemoteInventoryPending(runnerId)) return TerminalRunnerSeatDecision.Unknown;
        var peer = await runners.DescribeAsync(runnerId, ct);
        if (peer is not { Available: true, DispatchEligible: true, Stale: false })
            return TerminalRunnerSeatDecision.Unknown;
        if (peer.Capabilities is null) return TerminalRunnerSeatDecision.Unsupported;
        // A local runner has no phone-home live-store entry; its fresh descriptor supplies it.
        if ((peer.RunnerId != runnerId && !(RunnerRequestIntent.IsDesktopAlias(peer.RunnerId)
                && RunnerRequestIntent.IsDesktopAlias(runnerId))) || peer.Capabilities.RunnerStoreId != runnerStoreId
            || (runners.GetLiveStoreId(runnerId) is Guid liveStore && liveStore != runnerStoreId))
            return TerminalRunnerSeatDecision.IdentityUnknown;
        var features = peer.Capabilities.Features;
        if (features?.Contains(RunnerCapabilityFeatures.TerminalSeatReleaseV1) != true
            || features.Contains(RunnerCapabilityFeatures.TerminalSeatDeliveryEvidenceV1) != true)
            return TerminalRunnerSeatDecision.Unsupported;
        return null;
    }

    // This reader is shared by answer admission and retry. A missing/stopped session alone is
    // never authority to skip StopDelegateAsync. Keep every part of the accepted identity.
    internal static async Task<RunnerSeatRelease?> FindAttemptReleaseAsync(
        AppDbContext db, AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(task.RunnerId)
            || task.AgentSessionId is not Guid sessionId) return null;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.RunnerId != task.RunnerId) return null;
        return await db.RunnerSeatReleases.AsNoTracking().SingleOrDefaultAsync(r =>
            r.TaskId == task.Id && r.Attempt == task.Attempt && r.SessionId == sessionId
            && r.AgentId == task.AgentId && r.RunnerId == task.RunnerId
            && r.RunnerStoreId == session.RunnerStoreId && r.AcceptedStartedAt == session.StartedAt
            && r.SettlementRevision == task.ConcurrencyToken && r.SettledAt == task.CompletedAt, ct);
    }

    internal static bool IsConfirmed(RunnerSeatRelease? release) =>
        release is { State: RunnerSeatReleaseState.Confirmed, ConfirmedAt: not null, ActionId: not null }
        && release.OutcomeCode is nameof(TerminalSeatReleaseOutcome.Released)
            or nameof(TerminalSeatReleaseOutcome.AlreadyExited) or nameof(TerminalSeatReleaseOutcome.AlreadyAbsent);

    public async Task<Guid?> RegisterAndReleaseAsync(
        Guid taskId, TerminalSeatObservationRequest observation, CancellationToken ct)
    {
        var reservation = await RegisterAndReserveAsync(taskId, observation, ct);
        if (reservation.ReleaseId is Guid releaseId)
        {
            try
            {
                if (reservation.Decision is TerminalRunnerSeatDecision.Reserved or TerminalRunnerSeatDecision.AlreadyReserved)
                    await AdvanceAsync(releaseId, observation, ct);
            }
            finally { await PublishAttentionAsync(releaseId, ct); }
        }
        return reservation.ReleaseId;
    }

    /// <summary>Recover committed audits and missed attention invalidations. This boundary is
    /// intentionally independent of AutomaticEnabled: it cannot discover, qualify or send a
    /// release. S4b connects it to the existing scheduled reconciliation entry point.</summary>
    public async Task ReconcileAttentionAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null) return;
        var since = clock.GetUtcNow().UtcDateTime.AddHours(-24);
        // Republish the same durable identity on every reconciliation. There is no delivery
        // stamp to lose or advance before publication, and no second alert/outbox sink.
        var ids = await db.RunnerSeatReleases.AsNoTracking()
            .Where(r => r.State != RunnerSeatReleaseState.Confirmed || r.ConfirmedAt == null || r.ConfirmedAt >= since)
            .OrderBy(r => r.Id).Select(r => r.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await ReconcileAcceptedAnswerAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Keep one bad audit from hiding other release debt; no raw error/payload text.
                logger.LogWarning("Runner seat release audit remains pending for {ReleaseId}", id);
            }
            await PublishAttentionAsync(id, ct);
        }
    }

    private async Task PublishAttentionAsync(Guid releaseId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null) return;
        var release = await db.RunnerSeatReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == releaseId, ct);
        if (release is null) return;
        try
        {
            if (BoundaryAsync is not null) await BoundaryAsync("BeforeAttentionPublish", ct);
            logger.LogInformation("Runner seat release {ReleaseId} for {RunnerId}/{SessionId}: {State} {Reason} {Outcome}",
                release.Id, release.RunnerId, release.SessionId, release.State, release.ReasonCode, release.OutcomeCode);
            await events.PublishToAllAsync("AgentChanged", new
            {
                agentId = release.AgentId, sessionId = release.SessionId, releaseId = release.Id,
                conditionKey = $"runner-seat-release:{release.Id:D}"
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Runner seat release attention invalidation remains pending for {ReleaseId}", releaseId);
        }
    }

    public async Task AdvanceAsync(Guid releaseId, TerminalSeatObservationRequest observation, CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null) return;
        var release = await db.RunnerSeatReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == releaseId, ct);
        if (release is null || release.State is RunnerSeatReleaseState.Observing or RunnerSeatReleaseState.Confirmed
            || release.ActionId is null || string.IsNullOrWhiteSpace(release.ObservationToken)
            || release.RunnerStoreId != observation.ExpectedRunnerStoreId
            || release.AcceptedStartedAt != observation.ExpectedAcceptedStartedAt) return;
        var gate = queue.GetLock(release.SessionId);
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            // No tracked caller snapshot may decide recovery. Another scope/process can have
            // advanced the same durable action while this caller waited for the recipient gate.
            release = await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r => r.Id == releaseId, ct);
            if (release.State == RunnerSeatReleaseState.Confirmed) return;
            if (release.State == RunnerSeatReleaseState.Unresolved)
            {
                await ReconcileAsync(release, observation, ct);
                return;
            }
            if (release.State != RunnerSeatReleaseState.Reserved) return;
            var source = await ReleaseSourceAsync(release, verify: true, ct);
            if (await RequiresPublicationAsync(release, ct) && source is null) return;
            if (BoundaryAsync is not null) await BoundaryAsync("BeforeDispatch", ct);
            if (!await RunnerReadyAsync(release, ct))
            {
                await PendingAsync(release, "Unknown", ct);
                return;
            }
            if (observation.UseCapturedDeliveryEvidence)
            {
                // A persisted token survives a server restart, but is never fresh authority.
                // In particular runner restart/adoption loses capture while retaining identity.
                var fresh = await ObserveCapturedCandidateAsync(release.RunnerId, release.RunnerStoreId,
                    release.SessionId, release.AcceptedStartedAt, ct);
                if (fresh.Hold is not null || fresh.Observation?.Token != release.ObservationToken)
                {
                    await PendingAsync(release, fresh.Hold?.ToString() ?? "StaleObservation", ct);
                    return;
                }
            }

            // Queue gate -> short task/release/session transaction. Commit before touching the
            // wire. Unresolved means "may have sent", including death immediately after commit.
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await LockIdentityAsync(release, ct);
                if (await RevalidateAsync(release, ct) is { } refusal)
                {
                    await PendingAsync(release, refusal.ToString(), ct);
                    await tx.CommitAsync(ct);
                    return;
                }
                var changed = await db.RunnerSeatReleases.Where(r => r.Id == release.Id
                    && r.Revision == release.Revision && r.State == RunnerSeatReleaseState.Reserved
                    && r.ActionId == release.ActionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Unresolved)
                        .SetProperty(r => r.Revision, r => r.Revision + 1)
                        .SetProperty(r => r.OutcomeCode, "Unresolved")
                        .SetProperty(r => r.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
                await tx.CommitAsync(ct);
                if (changed != 1) return;
            }
            release.Revision++;
            release.State = RunnerSeatReleaseState.Unresolved;
            TerminalSeatReleaseResult result;
            try
            {
                result = await SeatClient(release.RunnerId).ReleaseTerminalSeatAsync(release.SessionId,
                    source?.ToRunnerReleaseRequest(observation, release.ObservationToken!)
                        ?? new(release.ActionId!.Value, observation, release.ObservationToken!), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The committed send intent is already the recovery record. No new action ID,
                // force-release fallback or inferred success after a dropped response.
                return;
            }
            if (BoundaryAsync is not null) await BoundaryAsync("BeforeResponse", ct);
            if (result.SessionId != release.SessionId || result.ActionId != release.ActionId
                || (result.AcceptedStartedAt != release.AcceptedStartedAt
                    && !(result.Outcome == TerminalSeatReleaseOutcome.AlreadyAbsent && result.AcceptedStartedAt is null)))
            {
                await PendingAsync(release, "MismatchedReceipt", ct);
                return;
            }
            if (!result.ConfirmsExit)
            {
                await PendingAsync(release, result.Outcome.ToString(), ct);
                return;
            }
            if (await RunnerReadyAsync(release, ct))
                await ConfirmAsync(release, result.Outcome, ct);
        }
        finally { gate.Release(); }
    }

    /// <summary>Read-only recovery of an already-sent release for an accepted answer. The
    /// rollout switch must not strand input; this path cannot qualify or send another kill.</summary>
    internal async Task ReconcileAcceptedAnswerAsync(Guid releaseId, CancellationToken ct)
    {
        var release = await db.RunnerSeatReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == releaseId, ct);
        if (release is not { State: RunnerSeatReleaseState.Unresolved, ActionId: not null }) return;
        var gate = queue.GetLock(release.SessionId);
        await gate.WaitAsync(ct);
        try
        {
            release = await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r => r.Id == releaseId, ct);
            if (release.State == RunnerSeatReleaseState.Unresolved)
                await ReconcileAsync(release, null, ct);
        }
        finally { gate.Release(); }
    }

    private async Task ReconcileAsync(RunnerSeatRelease release,
        TerminalSeatObservationRequest? observation, CancellationToken ct)
    {
        if (await RevalidateAsync(release, ct) is not null || !await RunnerReadyAsync(release, ct)) return;
        var source = await ReleaseSourceAsync(release, verify: true, ct);
        if (await RequiresPublicationAsync(release, ct) && source is null) return;
        RunnerInventory inventory;
        try { inventory = await DiscoveryInventoryAsync(release.RunnerId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return; }
        // Available means a complete fresh List RPC, not a heartbeat or an error's empty list.
        // Recheck recovery/store after the RPC too: a disconnect or adoption can race List.
        if (inventory is not RunnerInventory.Available available || !await RunnerReadyAsync(release, ct)) return;
        var seats = available.Sessions.Where(s => s.SessionId == release.SessionId).ToArray();
        if (seats.Length == 0)
        {
            if (source is not null && !await VerifyExitedParkAsync(release, source, ct)) return;
            await ConfirmAsync(release, TerminalSeatReleaseOutcome.AlreadyAbsent, ct);
            return;
        }
        if (seats.Length != 1 || seats[0].AcceptedStartedAt != release.AcceptedStartedAt
            || seats[0].Pending is not null) return;
        if (seats[0].Status == "Exited")
        {
            if (source is not null && !await VerifyExitedParkAsync(release, source, ct)) return;
            await ConfirmAsync(release, TerminalSeatReleaseOutcome.AlreadyExited, ct);
            return;
        }

        // A remaining seat needs new runner qualification, never a replay of the ambiguous
        // action. Reusing its token would spend old authority after an unknown interval.
        if (observation is null || !options.Value.AutomaticEnabled) return;
        TerminalSeatObservation fresh;
        try
        {
            if (observation.UseCapturedDeliveryEvidence)
            {
                var evidence = await ObserveCapturedCandidateAsync(release.RunnerId, release.RunnerStoreId,
                    release.SessionId, release.AcceptedStartedAt, ct);
                if (evidence.Hold is not null || evidence.Observation is null) return;
                fresh = evidence.Observation;
            }
            else fresh = await runners.Resolve(release.RunnerId).ObserveTerminalSeatAsync(release.SessionId, observation, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return; }
        if (fresh.Status != TerminalSeatQualificationStatus.Qualified
            || fresh.StableFor < TimeSpan.FromSeconds(120) || string.IsNullOrWhiteSpace(fresh.Token)
            || fresh.Token == release.ObservationToken || fresh.Transcript.Status != TerminalTranscriptReadStatus.Success
            || fresh.Transcript.Verdict != TerminalTranscriptVerdict.Idle) return;
        if (!await RunnerReadyAsync(release, ct)) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockIdentityAsync(release, ct);
        if (await RevalidateAsync(release, ct) is null)
        {
            await db.RunnerSeatReleases.Where(r => r.Id == release.Id && r.Revision == release.Revision
                && r.State == RunnerSeatReleaseState.Unresolved && r.ActionId == release.ActionId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Reserved)
                    .SetProperty(r => r.Revision, r => r.Revision + 1)
                    .SetProperty(r => r.ActionId, source == null ? Guid.NewGuid() : source.Request.Binding.ActionId)
                    .SetProperty(r => r.ObservationToken, fresh.Token)
                    .SetProperty(r => r.BindingIdentity, fresh.Transcript.BindingIdentity)
                    .SetProperty(r => r.FileRevision, fresh.Transcript.FileRevision)
                    .SetProperty(r => r.TranscriptRevision, fresh.Transcript.TranscriptRevision)
                    .SetProperty(r => r.OutcomeCode, (string?)null)
                    .SetProperty(r => r.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        }
        await tx.CommitAsync(ct);
        // The next advance must revalidate this new reservation again before dispatch.
    }

    private async Task<bool> RunnerReadyAsync(RunnerSeatRelease release, CancellationToken ct)
    {
        try
        {
            if (runners.RemoteInventoryPending(release.RunnerId)
                || (runners.GetLiveStoreId(release.RunnerId) is Guid liveStore && liveStore != release.RunnerStoreId)) return false;
            var descriptor = await runners.DescribeAsync(release.RunnerId, ct);
            return descriptor is { Available: true, DispatchEligible: true, Stale: false }
                && (descriptor.RunnerId == release.RunnerId || (RunnerRequestIntent.IsDesktopAlias(descriptor.RunnerId)
                    && RunnerRequestIntent.IsDesktopAlias(release.RunnerId)))
                && descriptor.Capabilities?.RunnerStoreId == release.RunnerStoreId
                && descriptor.Capabilities.Features?.Contains(RunnerCapabilityFeatures.TerminalSeatReleaseV1) == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return false; }
    }

    private Task<bool> RequiresPublicationAsync(RunnerSeatRelease release, CancellationToken ct) =>
        db.AgentTasks.AnyAsync(t => t.Id == release.TaskId && t.Status == AgentTaskStatus.Blocked, ct);

    private async Task<TaskParkPublicationEvidence?> ReleaseSourceAsync(RunnerSeatRelease release, bool verify, CancellationToken ct)
    {
        if (publication is null || release.ActionId is not Guid parkId) return null;
        var park = await db.AgentTaskParks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parkId, ct);
        if (park is null || park.RunnerSeatReleaseId != release.Id || park.TaskId != release.TaskId
            || park.Attempt != release.Attempt || park.BlockEventId != release.SettlementEventId
            || park.SessionId != release.SessionId || park.RunnerStoreId != release.RunnerStoreId
            || park.AcceptedStartedAt != release.AcceptedStartedAt) return null;
        return verify ? (await publication.VerifyAsync(parkId, ct)).Evidence : await publication.ReadEvidenceAsync(parkId, ct);
    }

    private async Task<bool> VerifyExitedParkAsync(RunnerSeatRelease release, TaskParkPublicationEvidence source, CancellationToken ct)
    {
        // Inventory absence is insufficient for a park: the version-2 runner path rechecks
        // its retained checkout under the generation gate, including after a runner restart.
        try
        {
            var result = await SeatClient(release.RunnerId).ReleaseTerminalSeatAsync(release.SessionId,
                source.ToRunnerReleaseRequest(new(release.RunnerStoreId, release.AcceptedStartedAt, "", -1,
                    UseCapturedDeliveryEvidence: true), release.ObservationToken!), ct);
            return result.SessionId == release.SessionId && result.ActionId == release.ActionId && result.ConfirmsExit
                && (result.AcceptedStartedAt == release.AcceptedStartedAt
                    || result.Outcome == TerminalSeatReleaseOutcome.AlreadyAbsent && result.AcceptedStartedAt is null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return false; }
    }

    private async Task<TerminalRunnerSeatDecision?> SettlementAgeAsync(AgentTask task, CancellationToken ct)
    {
        if (task.Status != AgentTaskStatus.Blocked) return policy.SettlementAge(task, clock.GetUtcNow().UtcDateTime);
        var at = await db.AgentTaskEvents.Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Blocked)
            .MaxAsync(e => (DateTime?)e.At, ct);
        return at is null || clock.GetUtcNow().UtcDateTime - at < TimeSpan.FromSeconds(120)
            ? TerminalRunnerSeatDecision.SettlementTooYoung : null;
    }

    private async Task LockIdentityAsync(RunnerSeatRelease release, CancellationToken ct)
    {
        // Same ordering will be used by the later released-answer/claim integration. These
        // commands lock committed rows; no SaveChanges on a caller's dirty tracker is involved.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "AgentTasks" WHERE "Id" = {release.TaskId} FOR UPDATE
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "RunnerSeatReleases" WHERE "Id" = {release.Id} FOR UPDATE
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "AgentSessions" WHERE "Id" = {release.SessionId} FOR UPDATE
            """, ct);
    }

    private async Task<TerminalRunnerSeatDecision?> RevalidateAsync(RunnerSeatRelease release, CancellationToken ct)
    {
        if (release.TaskId is null) return await RowlessHoldAsync(release, ct);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == release.TaskId, ct);
        if (policy.TerminalAttempt(task, parking: task?.Status == AgentTaskStatus.Blocked) is { } terminal) return terminal;
        if (task!.Attempt != release.Attempt || task.ConcurrencyToken != release.SettlementRevision
            || task.CompletedAt != release.SettledAt || task.AgentSessionId != release.SessionId
            || task.AgentId != release.AgentId || release.ReasonCode != $"Reserved:{task.Status}")
            return TerminalRunnerSeatDecision.StaleAttempt;
        if (await SettlementEventAsync(task, ct) != release.SettlementEventId)
            return TerminalRunnerSeatDecision.StaleAttempt;
        if (task.Status == AgentTaskStatus.Blocked && await ReleaseSourceAsync(release, verify: false, ct) is null)
            return TerminalRunnerSeatDecision.PublicationRequired;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == release.SessionId, ct);
        if (session is not null && (session.RunnerId != release.RunnerId || session.RunnerStoreId != release.RunnerStoreId
            || session.StartedAt != release.AcceptedStartedAt)) return TerminalRunnerSeatDecision.IdentityUnknown;
        if (session?.StandingAgentId is not null || session?.CardId is not null)
            return TerminalRunnerSeatDecision.StandingOwner;
        var sessionKey = release.SessionId.ToString("D");
        var agents = await db.Agents.AsNoTracking().Where(a => a.Id == task.AgentId
            || a.PersistentSessionId == sessionKey).ToListAsync(ct);
        if (policy.Custody(task, null) is { } taskCustody) return taskCustody;
        foreach (var agent in agents)
            if (policy.Custody(task, agent) is { } custody) return custody;
        var agentIds = agents.Select(a => a.Id).ToArray();
        var owners = await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
            && (t.AgentSessionId == release.SessionId
                || (t.AgentId != null && (t.AgentId == task.AgentId || agentIds.Contains(t.AgentId.Value)))))
            .ToListAsync(ct);
        if (owners.Any(t => t.Status == AgentTaskStatus.Blocked || policy.TerminalAttempt(t) is not null)) return TerminalRunnerSeatDecision.Owned;
        if (await SettlementAgeAsync(task, ct) is { } age) return age;
        if (await HasPendingDeliveryAsync(release.SessionId, ct)) return TerminalRunnerSeatDecision.PendingDelivery;
        if (session is null) return null; // Fresh runner proof, not a missing server projection, supplies idle authority.
        var state = await states.ReadAsync(release.SessionId, ct);
        if (state.Readiness != SessionStateReadiness.Ready || state.AcceptedGeneration != session.StartedAt)
            return TerminalRunnerSeatDecision.Unknown;
        return state.Working ? TerminalRunnerSeatDecision.Working : null;
    }

    private async Task ConfirmAsync(RunnerSeatRelease release, TerminalSeatReleaseOutcome outcome, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockIdentityAsync(release, ct);
        if (await RevalidateAsync(release, ct) is { } refusal)
        {
            await PendingAsync(release, refusal.ToString(), ct);
            await tx.CommitAsync(ct);
            return;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.RunnerSeatReleases.Where(r => r.Id == release.Id && r.Revision == release.Revision
            && r.ActionId == release.ActionId && r.State == RunnerSeatReleaseState.Unresolved)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Confirmed)
                .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.OutcomeCode, outcome.ToString())
                .SetProperty(r => r.ConfirmedAt, now).SetProperty(r => r.UpdatedAt, now), ct);
        if (changed == 1)
        {
            await db.AgentSessions.Where(s => s.Id == release.SessionId && s.RunnerId == release.RunnerId
                && s.RunnerStoreId == release.RunnerStoreId && s.StartedAt == release.AcceptedStartedAt
                && (s.Status == SessionStatus.Created || s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running || s.Status == SessionStatus.Stopping))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped)
                    .SetProperty(s => s.TerminationSource, s => s.TerminationSource == SessionTerminationSource.Unknown
                        ? SessionTerminationSource.SystemRequest : s.TerminationSource)
                    .SetProperty(s => s.EndedAt, now).SetProperty(s => s.LastSeenAt, now), ct);
            var parked = await db.AgentTaskParks.Where(p => p.Id == release.ActionId && p.RunnerSeatReleaseId == release.Id
                    && p.State == AgentTaskParkState.ReleasePending)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.State, AgentTaskParkState.Parked)
                    .SetProperty(p => p.ParkedAt, now).SetProperty(p => p.UpdatedAt, now)
                    .SetProperty(p => p.Revision, p => p.Revision + 1), ct);
            if (parked == 1)
                await db.Agents.Where(a => a.Id == release.AgentId && a.PersistentSessionId == release.SessionId.ToString())
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AgentStatus.Stopped)
                        .SetProperty(a => a.PoolIdleSince, (DateTime?)null)
                        .SetProperty(a => a.PoolReservedForRootTaskId, (Guid?)null).SetProperty(a => a.UpdatedAt, now), ct);
        }
        await tx.CommitAsync(ct);
    }

    private async Task PendingAsync(RunnerSeatRelease release, string outcome, CancellationToken ct)
    {
        await db.RunnerSeatReleases.Where(r => r.Id == release.Id && r.Revision == release.Revision
            && r.ActionId == release.ActionId && r.State != RunnerSeatReleaseState.Confirmed)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Unresolved)
                .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.OutcomeCode, outcome)
                .SetProperty(r => r.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
    }

    private Task<Guid?> SettlementEventAsync(AgentTask task, CancellationToken ct) => task.Status == AgentTaskStatus.Blocked
        ? db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Blocked)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct)
        : db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == task.Id && e.At == task.CompletedAt
            && (e.Type == AgentTaskEventType.Completed || e.Type == AgentTaskEventType.Failed
                || e.Type == AgentTaskEventType.Canceled || e.Type == AgentTaskEventType.Blocked))
            .OrderBy(e => e.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);

    public async Task<TerminalRunnerSeatReservation> RegisterAndReserveAsync(
        Guid taskId, TerminalSeatObservationRequest observation, CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled) return new(null, TerminalRunnerSeatDecision.Disabled);
        // Never let a caller's ambient/uncommitted settlement become release authority.
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            return new(null, TerminalRunnerSeatDecision.IncompleteAttempt);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (TerminalAttempt(task) is { } refusal) return new(null, refusal);
        if (task!.Status == AgentTaskStatus.Blocked)
        {
            var park = await CurrentParkAsync(task, ct);
            if (!ParkingEnabled || park is null || publication is null
                || (await publication.VerifyAsync(park.Id, ct)).Evidence is null)
                return new(null, TerminalRunnerSeatDecision.PublicationRequired);
        }
        if (task!.AgentSessionId is not Guid sessionId) return new(null, TerminalRunnerSeatDecision.IdentityUnknown);
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        var runnerId = session?.RunnerId ?? (observation.UseCapturedDeliveryEvidence ? task.RunnerId : null);
        if (observation.ExpectedRunnerStoreId == Guid.Empty || observation.ExpectedAcceptedStartedAt == default
            || runnerId is null || (session is not null && (session.RunnerStoreId != observation.ExpectedRunnerStoreId
            || session.StartedAt != observation.ExpectedAcceptedStartedAt)))
            return new(null, TerminalRunnerSeatDecision.IdentityUnknown);
        var now = clock.GetUtcNow().UtcDateTime;
        var release = await db.RunnerSeatReleases.AsNoTracking().SingleOrDefaultAsync(r =>
            r.RunnerId == runnerId && r.RunnerStoreId == observation.ExpectedRunnerStoreId
            && r.SessionId == sessionId && r.AcceptedStartedAt == observation.ExpectedAcceptedStartedAt, ct);
        if (release is null)
        {
            // Conflict-safe insert keeps the unique generation identity even across processes.
            var id = Guid.NewGuid();
            var settlementEventId = await SettlementEventAsync(task, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "RunnerSeatReleases" ("Id", "RunnerId", "RunnerStoreId", "SessionId",
                    "AcceptedStartedAt", "TaskId", "Attempt", "AgentId", "SettlementRevision", "SettledAt",
                    "SettlementEventId", "State", "Revision", "ReasonCode", "CreatedAt", "UpdatedAt")
                VALUES ({id}, {runnerId}, {observation.ExpectedRunnerStoreId}, {sessionId},
                    {observation.ExpectedAcceptedStartedAt}, {task.Id}, {task.Attempt}, {task.AgentId},
                    {task.ConcurrencyToken}, {task.CompletedAt}, {settlementEventId}, {0}, {0L}, {"Observing"}, {now}, {now})
                ON CONFLICT ("RunnerId", "RunnerStoreId", "SessionId", "AcceptedStartedAt") DO NOTHING
                """, ct);
            release = await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r =>
                r.RunnerId == runnerId && r.RunnerStoreId == observation.ExpectedRunnerStoreId
                && r.SessionId == sessionId && r.AcceptedStartedAt == observation.ExpectedAcceptedStartedAt, ct);
        }
        if (release.TaskId != task.Id || release.Attempt != task.Attempt
            || release.SettlementRevision != task.ConcurrencyToken || release.SettledAt != task.CompletedAt)
            return new(release.Id, TerminalRunnerSeatDecision.StaleAttempt);
        if (release.State != RunnerSeatReleaseState.Observing)
            return new(release.Id, TerminalRunnerSeatDecision.AlreadyReserved);

        // The same recipient gate serializes normal queue mutation. Never enter it from a SQL
        // transaction or transcript-state lease, and never call a lock-taking queue operation here.
        var gate = queue.GetLock(sessionId);
        if (!await gate.WaitAsync(0, ct))
            return await HoldAsync(release, TerminalRunnerSeatDecision.PendingDelivery, ct);
        try
        {
            if (session?.StandingAgentId is not null || session?.CardId is not null)
                return await HoldAsync(release, TerminalRunnerSeatDecision.StandingOwner, ct);
            var sessionKey = sessionId.ToString("D");
            var agents = await db.Agents.AsNoTracking().Where(a => a.Id == task.AgentId
                || a.Id == (session == null ? null : session.StandingAgentId) || a.PersistentSessionId == sessionKey).ToListAsync(ct);
            if (policy.Custody(task, null) is { } taskCustody)
                return await HoldAsync(release, taskCustody, ct);
            foreach (var agent in agents)
                if (policy.Custody(task, agent) is { } custody)
                    return await HoldAsync(release, custody, ct);
            var agentIds = agents.Select(a => a.Id).ToArray();
            var owners = await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
                && (t.AgentSessionId == sessionId
                    || (t.AgentId != null && (t.AgentId == task.AgentId || agentIds.Contains(t.AgentId.Value)))))
                .ToListAsync(ct);
            if (owners.Any(t => t.Status == AgentTaskStatus.Blocked || policy.TerminalAttempt(t) is not null))
                return await HoldAsync(release, TerminalRunnerSeatDecision.Owned, ct);
            if (await SettlementAgeAsync(task, ct) is { } age)
                return await HoldAsync(release, age, ct);
            if (await HasPendingDeliveryAsync(sessionId, ct))
                return await HoldAsync(release, TerminalRunnerSeatDecision.PendingDelivery, ct);

            if (session is not null)
            {
                var state = await states.ReadAsync(sessionId, ct);
                if (state.Readiness != SessionStateReadiness.Ready || state.AcceptedGeneration != session.StartedAt)
                    return await HoldAsync(release, TerminalRunnerSeatDecision.Unknown, ct);
                if (state.Working)
                    return await HoldAsync(release, TerminalRunnerSeatDecision.Working, ct);
            }

            TerminalSeatObservation observed;
            try
            {
                if (observation.UseCapturedDeliveryEvidence)
                {
                    var evidence = await ObserveCapturedCandidateAsync(runnerId, release.RunnerStoreId,
                        sessionId, release.AcceptedStartedAt, ct);
                    if (evidence.Hold is { } hold) return await HoldAsync(release, hold, ct);
                    observed = evidence.Observation!;
                }
                else
                {
                    if (runners.GetLiveStoreId(runnerId) != release.RunnerStoreId)
                        return await HoldAsync(release, TerminalRunnerSeatDecision.IdentityUnknown, ct);
                    var descriptor = await runners.DescribeAsync(runnerId, ct);
                    if (descriptor is not { Available: true, DispatchEligible: true, Stale: false }
                        || runners.RemoteInventoryPending(runnerId))
                        return await HoldAsync(release, TerminalRunnerSeatDecision.Unknown, ct);
                    if (descriptor.Capabilities?.Features?.Contains(RunnerCapabilityFeatures.TerminalSeatReleaseV1) != true)
                        return await HoldAsync(release, TerminalRunnerSeatDecision.Unsupported, ct);
                    observed = await SeatClient(runnerId).ObserveTerminalSeatAsync(sessionId, observation, ct);
                }
            }
            catch (NotSupportedException)
            {
                return await HoldAsync(release, TerminalRunnerSeatDecision.Unsupported, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Unavailable is never an absence/exit certificate. Persist only a bounded code.
                return await HoldAsync(release, TerminalRunnerSeatDecision.Unknown, ct);
            }
            if (observed.Status != TerminalSeatQualificationStatus.Qualified)
                return await HoldAsync(release, observed.Status switch
                {
                    TerminalSeatQualificationStatus.Working => TerminalRunnerSeatDecision.Working,
                    TerminalSeatQualificationStatus.Waiting => TerminalRunnerSeatDecision.Waiting,
                    _ => TerminalRunnerSeatDecision.Unknown
                }, ct);
            // StableFor is measured by the runner; never subtract its timestamps from our clock.
            if (observed.StableFor < TimeSpan.FromSeconds(120)
                || observed.Transcript.Status != TerminalTranscriptReadStatus.Success
                || observed.Transcript.Verdict != TerminalTranscriptVerdict.Idle
                || string.IsNullOrWhiteSpace(observed.Token))
                return await HoldAsync(release, TerminalRunnerSeatDecision.Unknown, ct);
            if (BoundaryAsync is not null) await BoundaryAsync("BeforeReservation", ct);
            var changed = await TryReserveAsync(release, task, observed, ct);
            return new(release.Id, changed == 1 ? TerminalRunnerSeatDecision.Reserved : TerminalRunnerSeatDecision.StaleAttempt);
        }
        finally { gate.Release(); }
    }

    private async Task<bool> HasPendingDeliveryAsync(Guid sessionId, CancellationToken ct) =>
        await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == sessionId
            && (m.Status == QueuedMessageStatus.Pending
                || ((m.Status == QueuedMessageStatus.Sent || m.DeliveryAttempts > 0)
                    && m.DeliveryVerdict != DeliveryVerdict.Delivered && m.DeliveryVerdict != DeliveryVerdict.LateConfirmed)), ct)
        || await db.AgentTaskLandNotifications.AnyAsync(n => n.ParentSessionId == sessionId
            && n.ConfirmedAt == null && n.State != LandNotificationState.NotRequired
            && n.State != LandNotificationState.Canceled && n.State != LandNotificationState.Confirmed, ct)
        || await db.AgentTaskDispatchWarningIntents.AnyAsync(n => n.ParentSessionId == sessionId
            && n.MaterializedAt == null && n.InitialState != LandNotificationState.NotRequired, ct)
        || await db.AgentTasks.AnyAsync(t => t.AgentSessionId == sessionId && t.ReleasedSeatAnswerId != null
            && t.ReleasedSeatAnswerTargetAttempt == t.Attempt, ct);

    private async Task<TerminalRunnerSeatReservation> HoldAsync(
        RunnerSeatRelease expected, TerminalRunnerSeatDecision reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RunnerSeatReleases.Where(r => r.Id == expected.Id && r.Revision == expected.Revision)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.ReasonCode, reason.ToString())
                .SetProperty(r => r.Revision, r => r.Revision + 1)
                .SetProperty(r => r.UpdatedAt, now).SetProperty(r => r.LastObservedAt, now)
                .SetProperty(r => r.FirstStableObservedAt, (DateTime?)null)
                .SetProperty(r => r.ObservationToken, (string?)null), ct);
        return new(expected.Id, reason);
    }

    internal async Task<int> TryReserveAsync(RunnerSeatRelease expected, AgentTask task,
        TerminalSeatObservation observed, CancellationToken ct)
    {
        if (!options.Value.AutomaticEnabled || expected.State != RunnerSeatReleaseState.Observing
            || expected.ActionId is not null || observed.Status != TerminalSeatQualificationStatus.Qualified
            || string.IsNullOrWhiteSpace(observed.Token) || db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            return 0;
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockIdentityAsync(expected, ct);
        var current = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == task.Id, ct);
        if (current is null || await SettlementEventAsync(current, ct) != expected.SettlementEventId) return 0;
        AgentTaskPark? park = null;
        if (task.Status == AgentTaskStatus.Blocked)
        {
            park = await CurrentParkAsync(current, ct);
            if (!ParkingEnabled || park is null || publication is null || park.State != AgentTaskParkState.Published
                || await publication.ReadEvidenceAsync(park.Id, ct) is null) return 0;
        }
        var actionId = park?.Id ?? Guid.NewGuid();
        // Revision is the sole ledger compare-and-swap fence. Two readers of the same existing
        // row must report the actual affected count, not success from a tracked SaveChanges.
        var changed = await db.RunnerSeatReleases.Where(r => r.Id == expected.Id && r.Revision == expected.Revision
            && db.AgentTasks.Any(t => t.Id == expected.TaskId && t.Attempt == expected.Attempt
                && t.ConcurrencyToken == expected.SettlementRevision && t.Status == task.Status
                && t.CompletedAt == expected.SettledAt && t.AgentSessionId == expected.SessionId
                && t.AgentId == task.AgentId && t.ReportEvidence == task.ReportEvidence))
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.State, RunnerSeatReleaseState.Reserved)
                .SetProperty(r => r.Revision, r => r.Revision + 1).SetProperty(r => r.ActionId, actionId)
                .SetProperty(r => r.ReasonCode, $"Reserved:{task.Status}")
                .SetProperty(r => r.ObservationToken, observed.Token)
                .SetProperty(r => r.BindingIdentity, observed.Transcript.BindingIdentity)
                .SetProperty(r => r.FileRevision, observed.Transcript.FileRevision)
                .SetProperty(r => r.TranscriptRevision, observed.Transcript.TranscriptRevision)
                .SetProperty(r => r.FirstStableObservedAt, now)
                .SetProperty(r => r.LastObservedAt, now).SetProperty(r => r.UpdatedAt, now), ct);
        if (changed == 1 && park is not null)
        {
            var linked = await db.AgentTaskParks.Where(p => p.Id == park.Id && p.Revision == park.Revision
                    && p.State == AgentTaskParkState.Published && p.RunnerSeatReleaseId == null)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.RunnerSeatReleaseId, expected.Id)
                    .SetProperty(p => p.State, AgentTaskParkState.ReleasePending).SetProperty(p => p.ReleasePendingAt, now)
                    .SetProperty(p => p.UpdatedAt, now).SetProperty(p => p.Revision, p => p.Revision + 1), ct);
            if (linked != 1) return 0;
        }
        await tx.CommitAsync(ct);
        return changed;
    }

    private async Task<AgentTaskPark?> CurrentParkAsync(AgentTask task, CancellationToken ct)
    {
        var blockId = await SettlementEventAsync(task, ct);
        return await db.AgentTaskParks.AsNoTracking().SingleOrDefaultAsync(p => p.TaskId == task.Id
            && p.Attempt == task.Attempt && p.BlockEventId == blockId && p.TaskConcurrencyToken == task.ConcurrencyToken, ct);
    }
}
