using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0412 sole granter and wait coordinator. Supervisor reconciliation issues at most one
/// outstanding grant per execution kind; queue/start/dispatch redeem it.
/// </summary>
public sealed class CapacityRecoveryService
{
    public const string CountedTaskLockKey = "antiphon.capacity.counted-tasks";
    public const string CompatibilityCursorKey = "card-0412-compatibility";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;
    private readonly SupervisionSettings _supervision;
    private readonly ILogger<CapacityRecoveryService> _logger;

    public CapacityRecoveryService(
        IServiceScopeFactory scopeFactory,
        TimeProvider time,
        IOptions<SupervisionSettings> settings,
        ILogger<CapacityRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _time = time;
        _supervision = settings.Value;
        _logger = logger;
    }

    public CapacityRecoverySettings Settings => _supervision.CapacityRecovery;

    public bool IsEnabled =>
        Settings.Enabled && _supervision.Enabled && _supervision.ApiErrorRecovery.Enabled;

    public int MaxAttempts =>
        Settings.EffectiveMaxEpisodeAttempts(_supervision.ApiErrorRecovery.WallDeathCap);

    public async Task ReconcileAsync(CancellationToken ct)
    {
        await ConsumePendingReleasesAsync(ct);
        if (IsEnabled)
            await GrantReadyAsync(ct);
    }

    public async Task<int> ConsumePendingReleasesAsync(CancellationToken ct)
    {
        var batch = Math.Clamp(Settings.ReconciliationBatchSize, 1, 1000);
        var acknowledged = 0;
        while (true)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.ModelAvailabilityHolds
                .Where(h => h.ClearedAt != null
                    && h.ReleasePendingAt != null
                    && h.ReleaseConsumedAt == null)
                .OrderBy(h => h.Id)
                .Take(batch)
                .ToListAsync(ct);
            if (pending.Count == 0)
                break;

            var holdIds = pending.Select(h => h.Id).ToList();
            var links = await db.Set<CapacityRecoveryWaitHold>()
                .Where(l => holdIds.Contains(l.HoldId) && l.ReleaseAcknowledgedAt == null)
                .OrderBy(l => l.WaitId)
                .ThenBy(l => l.HoldId)
                .Take(batch)
                .ToListAsync(ct);

            var now = UtcNow();
            foreach (var link in links)
            {
                link.ReleaseAcknowledgedAt = now;
                var wait = await db.Set<CapacityRecoveryWait>().FirstOrDefaultAsync(w => w.Id == link.WaitId, ct);
                if (wait is null)
                    continue;
                wait.LatestClearObservedAt = now;
                var hold = pending.First(h => h.Id == link.HoldId);
                wait.ObservedClearCauses = MergeCause(wait.ObservedClearCauses, hold.ClearCause);
                wait.Version++;
                wait.UpdatedAt = now;
                if (wait.State == CapacityRecoveryWaitState.WaitingForHold)
                    wait.State = CapacityRecoveryWaitState.Ready;
            }

            foreach (var hold in pending)
            {
                var remaining = await db.Set<CapacityRecoveryWaitHold>()
                    .CountAsync(l => l.HoldId == hold.Id && l.ReleaseAcknowledgedAt == null, ct);
                if (remaining == 0)
                    hold.ReleaseConsumedAt = now;
            }

            await db.SaveChangesAsync(ct);
            acknowledged += links.Count;
            if (links.Count < batch && pending.All(h => h.ReleaseConsumedAt != null))
                break;
            if (links.Count == 0)
            {
                foreach (var hold in pending)
                    hold.ReleaseConsumedAt = now;
                await db.SaveChangesAsync(ct);
                break;
            }
        }

        return acknowledged;
    }

    public async Task<int> GrantReadyAsync(CancellationToken ct)
    {
        if (!IsEnabled)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = UtcNow();
        var granted = 0;

        foreach (var kind in Enum.GetValues<AgentKind>().Distinct())
        {
            ct.ThrowIfCancellationRequested();
            await TakeProviderLockAsync(db, kind, ct);
            var state = await db.Set<CapacityRecoveryProviderState>()
                .FirstOrDefaultAsync(s => s.Kind == kind, ct);
            if (state is null)
            {
                state = new CapacityRecoveryProviderState { Kind = kind, UpdatedAt = now };
                db.Set<CapacityRecoveryProviderState>().Add(state);
            }

            if (state.GrantedWaitId is { } outstanding)
            {
                var current = await db.Set<CapacityRecoveryWait>()
                    .FirstOrDefaultAsync(w => w.Id == outstanding, ct);
                if (current is null || !StillHoldsGrant(current, state, now))
                {
                    RecordRevokedGrant(current, state, now);
                    state.GrantedWaitId = null;
                    state.GrantedActionKey = null;
                    state.GrantedAt = null;
                }
                else
                {
                    continue;
                }
            }

            if (state.NextAdmissionAt is { } due && due > now)
                continue;

            var batch = Math.Clamp(Settings.ReconciliationBatchSize, 1, 1000);
            var candidates = await db.Set<CapacityRecoveryWait>()
                .Where(w => w.ExecutionKind == kind
                    && (w.State == CapacityRecoveryWaitState.Ready
                        || w.State == CapacityRecoveryWaitState.ActionPending
                        || (w.State == CapacityRecoveryWaitState.Admitted && w.NeedsRevalidationGrant)))
                .OrderBy(w => w.BlockedAt)
                .ThenBy(w => w.Id)
                .Take(batch)
                .ToListAsync(ct);

            CapacityRecoveryWait? winner = null;
            foreach (var wait in candidates)
            {
                if (wait.DueAt is { } waitDue && waitDue > now)
                    continue;
                if (CapacityRecoveryPolicy.AttemptWouldExhaust(wait.AdmissionCount, MaxAttempts)
                    && !wait.NeedsRevalidationGrant)
                {
                    wait.State = CapacityRecoveryWaitState.Exhausted;
                    wait.Outcome = nameof(CapacityRecoveryWaitState.Exhausted);
                    wait.OutcomeReason = "max-episode-attempts";
                    wait.UpdatedAt = now;
                    wait.Version++;
                    continue;
                }

                winner = wait;
                break;
            }

            if (winner is null)
            {
                await db.SaveChangesAsync(ct);
                continue;
            }

            state.GrantedWaitId = winner.Id;
            state.GrantedActionKey = winner.ActionKey;
            state.GrantedAt = now;
            state.GrantVersion++;
            state.ExpectedWaitVersion = winner.Version;
            state.ExpectedWaveRevision = state.WaveRevision;
            state.ExpectedOwnerId = winner.TaskId ?? winner.AgentId ?? winner.SessionId;
            state.UpdatedAt = now;
            if (winner.State == CapacityRecoveryWaitState.Ready)
                winner.State = CapacityRecoveryWaitState.ActionPending;
            winner.UpdatedAt = now;
            winner.Version++;
            await db.SaveChangesAsync(ct);
            granted++;
            _logger.LogInformation(
                "Capacity recovery grant {ActionKey} kind {Kind} wait {WaitId} blockedAt {BlockedAt:u}",
                winner.ActionKey, kind, winner.Id, winner.BlockedAt);
        }

        return granted;
    }

    public async Task<CapacityRedemption> RedeemAsync(
        Guid waitId,
        string actionKey,
        AgentKind kind,
        CapacityRedemptionPath path,
        CancellationToken ct,
        string? refusalReason = null)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TakeProviderLockAsync(db, kind, ct);
        var now = UtcNow();
        var state = await db.Set<CapacityRecoveryProviderState>()
            .FirstOrDefaultAsync(s => s.Kind == kind, ct);
        var wait = await db.Set<CapacityRecoveryWait>().FirstOrDefaultAsync(w => w.Id == waitId, ct);
        if (state is null || wait is null)
            return CapacityRedemption.Refused("missing-grant");
        if (!IsEnabled)
            return await DeferRedemptionAsync(db, wait, state, "feature-disabled", now, ct);
        if (state.GrantedWaitId != waitId || state.GrantedActionKey != actionKey)
            return CapacityRedemption.Refused("grant-mismatch");
        if (state.NextAdmissionAt is { } due && due > now)
            return await DeferRedemptionAsync(db, wait, state, "clock-not-due", now, ct);
        if (!string.IsNullOrWhiteSpace(refusalReason))
            return await DeferRedemptionAsync(db, wait, state, refusalReason, now, ct);

        var revalidation = wait.NeedsRevalidationGrant;
        if (!revalidation)
        {
            if (CapacityRecoveryPolicy.AttemptWouldExhaust(wait.AdmissionCount, MaxAttempts))
            {
                wait.State = CapacityRecoveryWaitState.Exhausted;
                wait.Outcome = nameof(CapacityRecoveryWaitState.Exhausted);
                wait.OutcomeReason = "max-episode-attempts";
                ClearGrant(state, now);
                wait.UpdatedAt = now;
                wait.Version++;
                await db.SaveChangesAsync(ct);
                return CapacityRedemption.Refused("exhausted");
            }

            wait.AdmissionCount++;
        }

        wait.NeedsRevalidationGrant = false;
        wait.State = CapacityRecoveryWaitState.Admitted;
        wait.Outcome = nameof(CapacityRecoveryWaitState.Admitted);
        wait.OutcomeReason = path.ToString();
        wait.UpdatedAt = now;
        wait.Version++;
        state.LastActionKey = actionKey;
        state.LastActionAt = now;
        state.NextAdmissionAt = now.AddSeconds(Math.Max(1, Settings.AdmissionIntervalSeconds));
        ClearGrant(state, now);
        await db.SaveChangesAsync(ct);
        return CapacityRedemption.Accepted(wait.AdmissionCount, revalidation);
    }

    public async Task<CapacityRecoveryWait> EnsureWaitAsync(CapacityWaitRegistration registration, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var wait = await EnsureWaitCoreAsync(db, registration, UtcNow(), ct);
            await db.SaveChangesAsync(ct);
            return wait;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.Set<CapacityRecoveryWait>().SingleAsync(
                w => w.ConsumerKey == registration.ConsumerKey
                    && w.State != CapacityRecoveryWaitState.Progressed
                    && w.State != CapacityRecoveryWaitState.Canceled
                    && w.State != CapacityRecoveryWaitState.Superseded
                    && w.State != CapacityRecoveryWaitState.Exhausted,
                ct);
        }
    }

    public Task<CapacityRecoveryWait> EnsureWaitOnAsync(
        AppDbContext db, CapacityWaitRegistration registration, CancellationToken ct) =>
        EnsureWaitCoreAsync(db, registration, UtcNow(), ct);

    private async Task<CapacityRecoveryWait> EnsureWaitCoreAsync(
        AppDbContext db, CapacityWaitRegistration registration, DateTime now, CancellationToken ct)
    {
        var existing = await db.Set<CapacityRecoveryWait>()
            .FirstOrDefaultAsync(
                w => w.ConsumerKey == registration.ConsumerKey
                    && w.State != CapacityRecoveryWaitState.Progressed
                    && w.State != CapacityRecoveryWaitState.Canceled
                    && w.State != CapacityRecoveryWaitState.Superseded
                    && w.State != CapacityRecoveryWaitState.Exhausted,
                ct);
        if (existing is not null)
        {
            if (registration.HoldId is { } holdId)
                await LinkHoldAsync(db, existing, holdId, registration.HoldRevision, now, ct);
            ObserveImmediateClear(existing, registration, now);
            existing.UpdatedAt = now;
            return existing;
        }

        var wait = new CapacityRecoveryWait
        {
            Id = Guid.NewGuid(),
            ConsumerKind = registration.ConsumerKind,
            ConsumerKey = registration.ConsumerKey,
            BlockedAt = registration.BlockedAt == default ? now : registration.BlockedAt,
            AgentId = registration.AgentId,
            SessionId = registration.SessionId,
            SessionStartedAt = registration.SessionStartedAt,
            TaskId = registration.TaskId,
            TaskAttempt = registration.TaskAttempt,
            CardId = registration.CardId,
            RequestedKind = registration.RequestedKind,
            RequestedAlias = registration.RequestedAlias,
            ExecutionKind = registration.ExecutionKind,
            ActionOrdinal = 0,
            State = registration.HoldAlreadyCleared
                ? CapacityRecoveryWaitState.Ready
                : CapacityRecoveryWaitState.WaitingForHold,
            AdmissionCount = 0,
            Version = 1,
            RefusalDigest = registration.RefusalDigest,
            LatestClearObservedAt = registration.HoldAlreadyCleared ? now : null,
            ObservedClearCauses = registration.HoldAlreadyCleared
                ? registration.ClearCause?.ToString()
                : null,
            UpdatedAt = now,
        };
        wait.ActionKey = CapacityRecoveryPolicy.ActionKey(wait.Id, wait.ActionOrdinal);
        wait.DueAt = CapacityRecoveryPolicy.EligibleAt(
            wait.LatestClearObservedAt ?? wait.BlockedAt,
            wait.Id,
            wait.ActionOrdinal,
            Settings.JitterSeconds);
        db.Set<CapacityRecoveryWait>().Add(wait);
        if (registration.HoldId is { } newHold)
            await LinkHoldAsync(db, wait, newHold, registration.HoldRevision, now, ct);
        _logger.LogInformation(
            "Capacity wait registered {WaitId} consumer {Consumer} kind {Kind}",
            wait.Id, wait.ConsumerKey, wait.ExecutionKind);
        return wait;
    }

    public async Task PrepareNextAttemptAsync(Guid waitId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wait = await db.Set<CapacityRecoveryWait>().FirstOrDefaultAsync(w => w.Id == waitId, ct);
        if (wait is null || wait.State == CapacityRecoveryWaitState.Exhausted)
            return;
        if (CapacityRecoveryPolicy.AttemptWouldExhaust(wait.AdmissionCount, MaxAttempts))
        {
            wait.State = CapacityRecoveryWaitState.Exhausted;
            wait.Outcome = nameof(CapacityRecoveryWaitState.Exhausted);
            wait.OutcomeReason = "max-episode-attempts";
            wait.UpdatedAt = UtcNow();
            wait.Version++;
            await db.SaveChangesAsync(ct);
            return;
        }

        wait.ActionOrdinal++;
        wait.ActionKey = CapacityRecoveryPolicy.ActionKey(wait.Id, wait.ActionOrdinal);
        wait.State = CapacityRecoveryWaitState.Ready;
        wait.NeedsRevalidationGrant = false;
        wait.UpdatedAt = UtcNow();
        wait.Version++;
        wait.DueAt = CapacityRecoveryPolicy.EligibleAt(
            wait.LatestClearObservedAt ?? wait.BlockedAt,
            wait.Id,
            wait.ActionOrdinal,
            Settings.JitterSeconds);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkDeferredAsync(Guid waitId, string reason, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wait = await db.Set<CapacityRecoveryWait>().FirstOrDefaultAsync(w => w.Id == waitId, ct);
        if (wait is null)
            return;
        var now = UtcNow();
        wait.State = CapacityRecoveryWaitState.Deferred;
        wait.Outcome = nameof(CapacityRecoveryWaitState.Deferred);
        wait.OutcomeReason = reason;
        wait.UpdatedAt = now;
        wait.Version++;
        var state = await db.Set<CapacityRecoveryProviderState>()
            .FirstOrDefaultAsync(s => s.GrantedWaitId == waitId, ct);
        if (state is not null)
            ClearGrant(state, now);
        await db.SaveChangesAsync(ct);
    }

    public async Task RequestRevalidationGrantAsync(Guid waitId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wait = await db.Set<CapacityRecoveryWait>().FirstOrDefaultAsync(w => w.Id == waitId, ct);
        if (wait is null)
            return;
        wait.NeedsRevalidationGrant = true;
        wait.State = CapacityRecoveryWaitState.Admitted;
        wait.UpdatedAt = UtcNow();
        wait.Version++;
        await db.SaveChangesAsync(ct);
    }

    public async Task BumpWaveAsync(AgentKind kind, DateTime wallObservedAt, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TakeProviderLockAsync(db, kind, ct);
        var now = UtcNow();
        var state = await db.Set<CapacityRecoveryProviderState>()
            .FirstOrDefaultAsync(s => s.Kind == kind, ct);
        if (state is null)
        {
            state = new CapacityRecoveryProviderState { Kind = kind, UpdatedAt = now };
            db.Set<CapacityRecoveryProviderState>().Add(state);
        }

        state.WaveRevision++;
        var floor = wallObservedAt.AddSeconds(Math.Max(1, Settings.AdmissionIntervalSeconds));
        if (state.NextAdmissionAt is null || state.NextAdmissionAt < floor)
            state.NextAdmissionAt = floor;
        if (state.GrantedWaitId is { } grantedId)
        {
            var wait = await db.Set<CapacityRecoveryWait>().FirstOrDefaultAsync(w => w.Id == grantedId, ct);
            if (wait is not null && wait.State is CapacityRecoveryWaitState.Ready
                or CapacityRecoveryWaitState.ActionPending)
            {
                wait.State = CapacityRecoveryWaitState.Reheld;
                wait.Outcome = nameof(CapacityRecoveryWaitState.Reheld);
                wait.OutcomeReason = "new-wall-wave";
                wait.UpdatedAt = now;
                wait.Version++;
            }

            ClearGrant(state, now);
        }

        state.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryClaimCountedSlotAsync(
        AppDbContext db,
        int maxConcurrent,
        bool retainedReturn,
        CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('{CountedTaskLockKey}'))",
            cancellationToken: ct);
        var active = await db.AgentTasks
            .Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(
                t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                    && !t.CapacityWaitRetained,
                ct);
        if (active >= maxConcurrent)
            return false;
        return true;
    }

    private static bool StillHoldsGrant(CapacityRecoveryWait wait, CapacityRecoveryProviderState state, DateTime now)
    {
        if (wait.Version != state.ExpectedWaitVersion && !CapacityRecoveryPolicy.IsGrantCandidate(wait))
            return false;
        if (wait.State is CapacityRecoveryWaitState.Canceled
            or CapacityRecoveryWaitState.Superseded
            or CapacityRecoveryWaitState.Exhausted
            or CapacityRecoveryWaitState.Reheld
            or CapacityRecoveryWaitState.Progressed)
            return false;
        if (wait.State == CapacityRecoveryWaitState.Deferred)
            return false;
        return CapacityRecoveryPolicy.IsGrantCandidate(wait) || wait.State == CapacityRecoveryWaitState.ActionPending;
    }

    private static void RecordRevokedGrant(CapacityRecoveryWait? wait, CapacityRecoveryProviderState state, DateTime now)
    {
        if (wait is null)
            return;
        if (wait.State is CapacityRecoveryWaitState.ActionPending or CapacityRecoveryWaitState.Ready
            or CapacityRecoveryWaitState.Admitted)
        {
            wait.State = CapacityRecoveryWaitState.Deferred;
            wait.Outcome = nameof(CapacityRecoveryWaitState.Deferred);
            wait.OutcomeReason ??= "grant-revoked";
            wait.UpdatedAt = now;
            wait.Version++;
        }

        ClearGrant(state, now);
    }

    private static void ClearGrant(CapacityRecoveryProviderState state, DateTime now)
    {
        state.GrantedWaitId = null;
        state.GrantedActionKey = null;
        state.GrantedAt = null;
        state.UpdatedAt = now;
    }

    private async Task<CapacityRedemption> DeferRedemptionAsync(
        AppDbContext db,
        CapacityRecoveryWait wait,
        CapacityRecoveryProviderState state,
        string reason,
        DateTime now,
        CancellationToken ct)
    {
        wait.State = CapacityRecoveryWaitState.Deferred;
        wait.Outcome = nameof(CapacityRecoveryWaitState.Deferred);
        wait.OutcomeReason = reason;
        wait.UpdatedAt = now;
        wait.Version++;
        ClearGrant(state, now);
        await db.SaveChangesAsync(ct);
        return CapacityRedemption.Defer(reason);
    }

    private static async Task LinkHoldAsync(
        AppDbContext db,
        CapacityRecoveryWait wait,
        Guid holdId,
        int revision,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await db.Set<CapacityRecoveryWaitHold>()
            .FirstOrDefaultAsync(l => l.WaitId == wait.Id && l.HoldId == holdId, ct);
        if (existing is null)
        {
            db.Set<CapacityRecoveryWaitHold>().Add(new CapacityRecoveryWaitHold
            {
                WaitId = wait.Id,
                HoldId = holdId,
                ObservedRevision = revision,
            });
        }
        else
        {
            existing.ObservedRevision = revision;
        }

        var hold = await db.ModelAvailabilityHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold is { ClearedAt: not null })
        {
            wait.LatestClearObservedAt = hold.ClearedAt;
            wait.ObservedClearCauses = MergeCause(wait.ObservedClearCauses, hold.ClearCause);
            if (wait.State == CapacityRecoveryWaitState.WaitingForHold)
                wait.State = CapacityRecoveryWaitState.Ready;
            var link = await db.Set<CapacityRecoveryWaitHold>()
                .FirstOrDefaultAsync(l => l.WaitId == wait.Id && l.HoldId == holdId, ct);
            if (link is not null && link.ReleaseAcknowledgedAt is null)
                link.ReleaseAcknowledgedAt = now;
        }
    }

    private static void ObserveImmediateClear(CapacityRecoveryWait wait, CapacityWaitRegistration registration, DateTime now)
    {
        if (!registration.HoldAlreadyCleared)
            return;
        wait.LatestClearObservedAt = now;
        wait.ObservedClearCauses = MergeCause(wait.ObservedClearCauses, registration.ClearCause);
        if (wait.State == CapacityRecoveryWaitState.WaitingForHold)
            wait.State = CapacityRecoveryWaitState.Ready;
    }

    private static string? MergeCause(string? existing, ModelAvailabilityClearCause? cause)
    {
        if (cause is null)
            return existing;
        var token = cause.Value.ToString();
        if (string.IsNullOrEmpty(existing))
            return token;
        if (existing.Split(',').Contains(token, StringComparer.Ordinal))
            return existing;
        return existing + "," + token;
    }

    private static Task TakeProviderLockAsync(AppDbContext db, AgentKind kind, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('antiphon.capacity.kind.{kind}'))",
            cancellationToken: ct);

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}

public readonly record struct CapacityRedemption(bool Ok, bool Deferred, string? Reason, int AdmissionCount, bool Revalidation)
{
    public static CapacityRedemption Accepted(int count, bool revalidation) =>
        new(true, false, null, count, revalidation);

    public static CapacityRedemption Refused(string reason) =>
        new(false, false, reason, 0, false);

    public static CapacityRedemption Defer(string reason) =>
        new(false, true, reason, 0, false);
}

public enum CapacityRedemptionPath
{
    Queue = 0,
    Start = 1,
    Dispatch = 2,
}

public sealed class CapacityWaitRegistration
{
    public required string ConsumerKey { get; init; }
    public required CapacityWaitConsumerKind ConsumerKind { get; init; }
    public required AgentKind ExecutionKind { get; init; }
    public AgentKind RequestedKind { get; init; }
    public string? RequestedAlias { get; init; }
    public DateTime BlockedAt { get; init; }
    public Guid? AgentId { get; init; }
    public Guid? SessionId { get; init; }
    public DateTime? SessionStartedAt { get; init; }
    public Guid? TaskId { get; init; }
    public int? TaskAttempt { get; init; }
    public Guid? CardId { get; init; }
    public Guid? HoldId { get; init; }
    public int HoldRevision { get; init; }
    public bool HoldAlreadyCleared { get; init; }
    public ModelAvailabilityClearCause? ClearCause { get; init; }
    public string? RefusalDigest { get; init; }
}
