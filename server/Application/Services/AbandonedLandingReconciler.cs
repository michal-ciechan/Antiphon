using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1446 D-1. An unpublished landing that stopped in a resumable phase and was never resumed
/// is abandoned, not "missing a terminal arm": the protocol keeps <c>push_rejected</c> at
/// <c>PushStarted</c> resumable on purpose (CARD-0711). After
/// <see cref="WorktreeResidueSettings.AbandonedLandingDays"/> with a terminal task and no
/// pending request, the daily job marks it <c>Refused</c> so the next explicit <c>-Land</c>
/// starts a fresh operation (<c>CanReplaceRefused</c>). A push that returned 0 but was never
/// confirmed is <c>push_unconfirmed</c>; only a human resolves it, so it is logged and kept.
/// One transaction per operation under the task row lock, the same lock
/// <c>RequestCleanupRetryCoreAsync</c> takes.
/// </summary>
public sealed class AbandonedLandingReconciler(
    AppDbContext db,
    IOptions<WorktreeResidueSettings> settings,
    TimeProvider clock,
    ILogger<AbandonedLandingReconciler> logger)
{
    private const int ReasonLimit = 400;

    /// <summary>Every earlier phase already has a terminal arm; every later phase is published.</summary>
    private static readonly LandPhase[] ResumablePhases =
    [
        LandPhase.Verified, LandPhase.TargetAdvanceStarted, LandPhase.LocalTargetAdvanced, LandPhase.PushStarted,
    ];

    private static readonly AgentTaskStatus[] TerminalTask =
        [AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled];

    private readonly AgentTaskLandingState _state = new();

    /// <summary>Returns the number of operations marked Refused.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var days = settings.Value.AbandonedLandingDays;
        if (days <= 0) return 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now.AddDays(-days);
        var candidates = await db.AgentTaskLandings.AsNoTracking()
            .Where(o => o.Active
                && o.Publication == LandPublicationOutcome.Unconfirmed
                && o.RemoteConfirmedAt == null
                && ResumablePhases.Contains(o.Phase)
                && o.UpdatedAt < cutoff)
            .OrderBy(o => o.UpdatedAt).ThenBy(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync(ct);
        var refused = 0;
        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await RefuseAsync(id, now, cutoff, days, ct)) refused++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // One operation must not stop the next; the log keeps no path.
                logger.LogWarning("Abandoned landing {OperationId} was not reconciled ({Type})", id, ex.GetType().Name);
            }
        }

        return refused;
    }

    private async Task<bool> RefuseAsync(Guid id, DateTime now, DateTime cutoff, int days, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var snapshot = await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        if (snapshot is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {snapshot.TaskId} FOR UPDATE", ct);
        var op = await db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (op is null || !IsCandidate(op, cutoff)) return false;
        if (op.Phase == LandPhase.PushStarted && op.PushExitCode == 0)
        {
            logger.LogWarning(
                "Landing {OperationId} for task {TaskId} is push_unconfirmed (push exit 0, never confirmed) since {UpdatedAt:O}; kept for a human",
                op.Id, op.TaskId, op.UpdatedAt);
            return false;
        }

        var task = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Id == op.TaskId)
            .Select(t => new { t.Status, t.CurrentLandRequestId })
            .SingleOrDefaultAsync(ct);
        if (task is null || !TerminalTask.Contains(task.Status)) return false;
        if (await db.AgentTaskLandRequests.AnyAsync(r => r.TaskId == op.TaskId && r.IsPending, ct)) return false;
        if (task.CurrentLandRequestId is Guid current)
        {
            var state = await db.AgentTaskLandRequests.AsNoTracking()
                .Where(r => r.Id == current).Select(r => (LandRequestState?)r.State).SingleOrDefaultAsync(ct);
            if (state is not null
                && state is not (LandRequestState.Completed or LandRequestState.Superseded or LandRequestState.Canceled))
                return false;
        }

        var previousReason = op.LastReason;
        var previousPhase = op.Phase;
        var previousUpdatedAt = op.UpdatedAt;
        op.Publication = LandPublicationOutcome.Refused;
        op.LastReason = Clip("landing_abandoned:" + previousReason);
        _state.Transition(op, LandPhase.Refused, now);
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = op.TaskId,
            Type = AgentTaskEventType.Warning,
            Detail = $"landing_abandoned: operation {op.Id:N} refused {previousReason ?? "(none)"} at {previousPhase} on {previousUpdatedAt:O}; no resume within {days} days; the next -Land starts a fresh operation",
            At = now,
            LandingOperationId = op.Id,
            LandingPublication = LandPublicationOutcome.Refused,
            LandingCleanup = op.Cleanup,
            LandingMode = op.Mode,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation(
            "Landing {OperationId} for task {TaskId} abandoned at {Phase} ({Reason}) since {UpdatedAt:O}; marked Refused",
            op.Id, op.TaskId, previousPhase, previousReason, previousUpdatedAt);
        return true;
    }

    private bool IsCandidate(AgentTaskLanding op, DateTime cutoff) =>
        op.Active
        && op.Publication == LandPublicationOutcome.Unconfirmed
        && op.RemoteConfirmedAt is null
        && !_state.HasPublication(op)
        && ResumablePhases.Contains(op.Phase)
        && op.UpdatedAt < cutoff;

    private static string Clip(string value) => value.Length <= ReasonLimit ? value : value[..ReasonLimit];
}
