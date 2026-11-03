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

/// <summary>One examined landing row: <c>deleted</c>, <c>kept</c>, <c>deferred</c>, <c>deleting</c> or <c>would_delete</c>.</summary>
public sealed record PublishedBranchDeletionRow(Guid OperationId, Guid TaskId, string SourceFullRef, string Outcome, string Reason);

public sealed record PublishedBranchDeletionRunResult(bool Enabled, bool DryRun, int Examined, int PushAttempts,
    string? Stopped, IReadOnlyList<PublishedBranchDeletionRow> Rows);

/// <summary>
/// CARD-0824 S2 (plan §3.10, S2 test design B-3). Deletes a confirmed-published land's origin branch after the delay,
/// strictly one row at a time:
/// <list type="number">
/// <item>the pure policy (<see cref="PublishedBranchDeletionPolicy"/>) and the open-dependent query;</item>
/// <item>under the repository lease, a fresh read that origin's target still contains the published SHA, then the
/// remote source recheck: a moved branch is kept with no push, a missing one is recorded;</item>
/// <item>every runner that held the branch deletes its local refs (any refusal or failure defers; origin untouched);</item>
/// <item>the <c>Deleting</c> intent is saved, then the delete is pushed with
/// <c>--force-with-lease=&lt;ref&gt;:&lt;SourceRemoteSha&gt;</c>;</item>
/// <item>origin is read again: absent is <c>Deleted</c>, and only then is the desktop tracking ref removed.</item>
/// </list>
/// The flag is re-read before every row, so switching it off stops an in-flight run. DryRun recomputes and rechecks
/// only: no runner call, no push and no row write. The desktop <c>refs/heads/&lt;b&gt;</c> and the
/// <c>refs/antiphon/land/**</c> pins are never touched.
/// </summary>
public sealed class PublishedBranchDeletionService(AppDbContext db, ILandingGit git, IRepositoryMutationLease leases,
    IRunnerBranchRefs runners, IOptionsMonitor<LandingSettings> settings, TimeProvider clock,
    ILogger<PublishedBranchDeletionService> logger)
{
    public const string ReasonDeleted = "deleted";
    public const string ReasonAlreadyAbsent = "already_absent";

    private static readonly LandBranchDeletionState[] Pending =
        [LandBranchDeletionState.None, LandBranchDeletionState.Deferred, LandBranchDeletionState.Deleting];

    public async Task<PublishedBranchDeletionRunResult> RunAsync(CancellationToken ct)
    {
        var current = settings.CurrentValue;
        if (!current.DeleteSourceBranchAfterPublication)
            return new(false, current.SourceBranchDeletionDryRun, 0, 0, null, []);
        // Q-3: a null cutoff makes nothing eligible, so a first enable cannot sweep the historical backlog.
        if (current.SourceBranchDeletionAfterUtc is not { } cutoffOffset)
            return new(true, current.SourceBranchDeletionDryRun, 0, 0, "no_cutoff", []);
        var cutoff = cutoffOffset.UtcDateTime;
        var candidates = await db.AgentTaskLandings
            .Where(o => o.Active && o.RemoteConfirmedAt != null && o.RemoteConfirmedAt > cutoff
                && Pending.Contains(o.BranchDeletionState)
                && (o.Publication == LandPublicationOutcome.Landed || o.Publication == LandPublicationOutcome.AlreadyPresent))
            .OrderBy(o => o.RemoteConfirmedAt).ThenBy(o => o.Id)
            .ToListAsync(ct);

        var rows = new List<PublishedBranchDeletionRow>();
        var attempts = 0;
        string? stopped = null;
        var dryRun = current.SourceBranchDeletionDryRun;
        foreach (var op in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var live = settings.CurrentValue;
            if (!live.DeleteSourceBranchAfterPublication) { stopped = "kill_switch"; break; }
            if (attempts >= Math.Max(0, live.MaxSourceBranchDeletionsPerRun)) { stopped = "run_bound"; break; }
            dryRun = live.SourceBranchDeletionDryRun;
            var runnerIds = await RunnersAsync(op, ct);
            var decision = PublishedBranchDeletionPolicy.Decide(op, live, runnerIds, Now());
            if (decision.Kind == BranchDeletionDecisionKind.NotEligible)
                continue;
            if (decision.Kind != BranchDeletionDecisionKind.Eligible)
            {
                rows.Add(await SettleAsync(op, decision.Kind == BranchDeletionDecisionKind.Kept
                    ? LandBranchDeletionState.Kept : LandBranchDeletionState.Deferred, decision.Reason, null, dryRun, ct));
                continue;
            }
            var dependent = await PublishedBranchDeletionDependents.FindAsync(db, op, ct);
            if (dependent is not null)
            {
                rows.Add(await SettleAsync(op, LandBranchDeletionState.Deferred, "dependent_open",
                    $"{dependent.Term}: {dependent.TaskId}", dryRun, ct));
                continue;
            }
            attempts++;
            rows.Add(await DeleteOneAsync(op, runnerIds, dryRun, ct));
        }
        logger.LogInformation(
            "Published branch deletion examined {Examined} row(s), {Attempts} action(s), dryRun={DryRun}, stopped={Stopped}",
            candidates.Count, attempts, dryRun, stopped);
        return new(true, dryRun, candidates.Count, attempts, stopped, rows);
    }

    private async Task<PublishedBranchDeletionRow> DeleteOneAsync(AgentTaskLanding op, IReadOnlyList<string> runnerIds,
        bool dryRun, CancellationToken ct)
    {
        await using var lease = await leases.TryAcquireAsync(op.RepositoryPath, ct);
        if (lease is null)
            return await SettleAsync(op, LandBranchDeletionState.Deferred, "repository_busy", null, dryRun, ct);

        // Confirmed at deletion time, never from the cached landing receipt: origin's target must still contain it.
        var contained = await git.ConfirmTargetContainsAsync(op.RepositoryPath, op.TargetFullRef, op.VerifiedSourceSha!,
            op.RemoteFingerprint, ct);
        if (contained.Reason is not null)
            return await SettleAsync(op, LandBranchDeletionState.Deferred, "containment_unknown", contained.Reason, dryRun, ct);
        if (!contained.ContainsSource)
            return await SettleAsync(op, LandBranchDeletionState.Kept, "publication_not_contained",
                $"{op.TargetFullRef} at {contained.Sha} does not contain {op.VerifiedSourceSha}", dryRun, ct);

        var recheck = await git.RecheckSourceRemoteAsync(op.RepositoryPath, op.SourceFullRef, op.SourceRemoteSha!,
            op.SourceRemoteFingerprint!, ct);
        if (recheck.Reason == "source_remote_missing")
            return dryRun ? Row(op, "would_delete", ReasonAlreadyAbsent)
                : await DeletedAsync(op, op.BranchDeletionState == LandBranchDeletionState.Deleting ? ReasonDeleted : ReasonAlreadyAbsent, ct);
        if (!recheck.Accepted)
            return await SettleAsync(op, LandBranchDeletionState.Deferred, "source_recheck_failed", recheck.Reason, dryRun, ct);
        if (recheck.Sha != op.SourceRemoteSha)
            return await SettleAsync(op, LandBranchDeletionState.Kept, "branch_moved", $"origin at {recheck.Sha}", dryRun, ct);
        if (dryRun)
            return Row(op, "would_delete", "eligible");

        var branch = op.SourceFullRef[11..];
        if (op.RunnerRefsDeletedAt is null && runnerIds.Count > 0)
        {
            foreach (var runnerId in runnerIds)
            {
                RunnerBranchRefsResult answer;
                try
                {
                    if (!await runners.SupportsAsync(runnerId, ct))
                        return await SettleAsync(op, LandBranchDeletionState.Deferred, "runner_capability_missing", runnerId, false, ct);
                    answer = await runners.DeleteAsync(runnerId, branch, op.SourceRemoteSha!, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    return await SettleAsync(op, LandBranchDeletionState.Deferred, "runner_unreachable",
                        $"{runnerId}: {ex.GetType().Name}: {ex.Message}", false, ct);
                }
                if (answer.Outcome != RunnerBranchRefsOutcome.Deleted)
                    return await SettleAsync(op, LandBranchDeletionState.Deferred, answer.Code ?? "runner_refused", runnerId, false, ct);
            }
            op.RunnerRefsDeletedAt = Now();
            await SaveAsync(op, ct);
        }

        // Review 370083f1 A-2: the dependents and the card rule are read again under the lease, immediately before the
        // intent, because the runner step can take minutes. Deleting runner refs that are still published loses nothing.
        if (await PublishedBranchDeletionDependents.FindAsync(db, op, ct) is { } late)
            return await SettleAsync(op, LandBranchDeletionState.Deferred, "dependent_open", $"{late.Term}: {late.TaskId}", false, ct);

        // D-1: the intent is durable before the push, so a restart can tell its own delete from a stranger's.
        op.BranchDeletionState = LandBranchDeletionState.Deleting;
        op.BranchDeletionAt = Now();
        await SaveAsync(op, ct);
        var push = await git.DeleteSourceBranchAsync(op.RepositoryPath, op.SourceFullRef, op.SourceRemoteSha!,
            op.SourceRemoteFingerprint!, ct);

        var after = await git.RecheckSourceRemoteAsync(op.RepositoryPath, op.SourceFullRef, op.SourceRemoteSha!,
            op.SourceRemoteFingerprint!, ct);
        if (after.Reason == "source_remote_missing")
            return await DeletedAsync(op, ReasonDeleted, ct);
        if (after.Accepted && after.Sha != op.SourceRemoteSha)
            return await SettleAsync(op, LandBranchDeletionState.Kept, "branch_moved", $"lease refused; origin at {after.Sha}", false, ct);
        if (after.Accepted)
            return await SettleAsync(op, LandBranchDeletionState.Deferred, "delete_push_failed",
                $"exit {push.ExitCode}: {push.Diagnostic} {push.Output}".Trim(), false, ct);
        // Unknown after the push: stay Deleting; the next sweep rechecks before anything else.
        op.BranchDeletionDetail = Clip("post-push recheck: " + after.Reason);
        await SaveAsync(op, ct);
        return Row(op, "deleting", after.Reason ?? "unknown");
    }

    /// <summary>B-3 step 7: only after origin is confirmed absent; the desktop branch and the land pins stay.</summary>
    private async Task<PublishedBranchDeletionRow> DeletedAsync(AgentTaskLanding op, string reason, CancellationToken ct)
    {
        var tracking = await git.DeleteTrackingRefAsync(op.RepositoryPath, op.SourceFullRef, op.SourceRemoteSha!, ct);
        var detail = tracking.Succeeded ? null : "tracking ref kept: " + tracking.Diagnostic;
        return await SettleAsync(op, LandBranchDeletionState.Deleted, reason, detail, false, ct);
    }

    /// <summary>One event per state or reason change, never once per sweep (V-46).</summary>
    private async Task<PublishedBranchDeletionRow> SettleAsync(AgentTaskLanding op, LandBranchDeletionState state,
        string reason, string? detail, bool dryRun, CancellationToken ct)
    {
        var outcome = state.ToString().ToLowerInvariant();
        if (dryRun) return Row(op, "would_" + outcome, reason);
        var changed = op.BranchDeletionState != state || op.BranchDeletionReason != reason;
        op.BranchDeletionState = state;
        op.BranchDeletionReason = reason;
        op.BranchDeletionDetail = detail is null ? null : Clip(detail);
        if (changed)
        {
            op.BranchDeletionAt = Now();
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = op.TaskId, Type = AgentTaskEventType.LandingBranchDeletion,
                Detail = Clip($"{state} {reason}: {op.SourceFullRef}" + (detail is null ? "" : " (" + detail + ")")),
                At = Now(), LandingOperationId = op.Id,
            });
        }
        await SaveAsync(op, ct);
        if (changed)
            logger.LogInformation("Published branch {Branch} of task {TaskId} {State}: {Reason} {Detail}",
                op.SourceFullRef, op.TaskId, state, reason, detail);
        return Row(op, outcome, reason);
    }

    private async Task SaveAsync(AgentTaskLanding op, CancellationToken ct)
    {
        op.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Every runner volume that held the branch: the owner's and any other task's on the same branch, in any spelling
    /// the dependents check accepts (A-1).
    /// </summary>
    private async Task<IReadOnlyList<string>> RunnersAsync(AgentTaskLanding op, CancellationToken ct)
    {
        var names = PublishedBranchDeletionDependents.BranchNames(op.SourceFullRef);
        var ids = await db.AgentTasks.AsNoTracking()
            .Where(t => (t.Id == op.TaskId || t.WorktreeBranch != null && names.Contains(t.WorktreeBranch)) && t.RunnerId != null)
            .Select(t => t.RunnerId!).Distinct().ToListAsync(ct);
        return [.. ids.Where(id => !string.IsNullOrWhiteSpace(id) && id != PhoneHomeProtocol.LocalRunnerId)
            .Order(StringComparer.Ordinal)];
    }

    private static PublishedBranchDeletionRow Row(AgentTaskLanding op, string outcome, string reason) =>
        new(op.Id, op.TaskId, op.SourceFullRef, outcome, reason);

    private static string Clip(string value) => value.Length <= 400 ? value : value[..399] + "…";

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
