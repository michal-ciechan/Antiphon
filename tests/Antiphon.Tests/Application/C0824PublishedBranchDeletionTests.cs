using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S2 (test design 2026-11-02-card-0824-S2-test-design.md, CP-12). The origin branch of a
/// confirmed-published land is deleted after the delay with a lease on <c>SourceRemoteSha</c>; refused,
/// unconfirmed, moved, protected, dependent and runner-blocked branches are kept or deferred. Real lands run
/// through <see cref="LandingSafetyHarness"/>; remote reads use an independent fixture reader.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class C0824PublishedBranchDeletionTests
{
    private static readonly DateTime Start = new(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);
    private const string Branch = "feat/card-task-0824abcd";

    // ---- V-38, V-39 and the policy matrix (pure) ----

    [Test]
    [Arguments("eligible-after-delay", "Eligible", "eligible")]
    [Arguments("before-delay", "NotEligible", "before_delay")]
    [Arguments("flag-off", "NotEligible", "flag_off")]
    [Arguments("unconfirmed-pushstarted", "NotEligible", "not_published")]
    [Arguments("refused-phase", "NotEligible", "not_published")]
    [Arguments("residue-confirmed", "Eligible", "eligible")]
    [Arguments("schema-2", "Kept", "legacy_schema")]
    [Arguments("recovery-adopt", "Kept", "recovery_land")]
    [Arguments("protected-master", "Kept", "protected_branch")]
    [Arguments("protected-rc", "Kept", "protected_branch")]
    [Arguments("non-delegate-name", "Kept", "protected_branch")]
    [Arguments("source-sha-mismatch", "Kept", "source_identity_mismatch")]
    [Arguments("before-cutoff", "NotEligible", "before_cutoff")]
    [Arguments("runner-not-enabled", "Deferred", "runner_volume_not_enabled")]
    [Arguments("desktop-only-enabled", "Eligible", "eligible")]
    [Arguments("already-deleted", "NotEligible", "already_settled")]
    public void C0824_policy_rows(string name, string kind, string reason)
    {
        var confirmed = Start;
        var op = PublishedLandingRow.Build(Guid.NewGuid(), Branch, confirmed);
        var settings = Enabled(Start.AddDays(-1));
        var now = confirmed.AddMinutes(settings.SourceBranchDeleteDelayMinutes);
        IReadOnlyCollection<string> runners = [];
        switch (name)
        {
            case "before-delay": now = now.AddSeconds(-1); break;
            case "flag-off": settings.DeleteSourceBranchAfterPublication = false; break;
            case "unconfirmed-pushstarted":
                op.Phase = LandPhase.PushStarted; op.RemoteConfirmedAt = null;
                op.Publication = LandPublicationOutcome.Unconfirmed; op.ConfirmationMethod = null;
                break;
            case "refused-phase":
                op.Phase = LandPhase.Refused; op.Publication = LandPublicationOutcome.Refused;
                break;
            case "residue-confirmed":
                op.Phase = LandPhase.CleanupStarted; op.Cleanup = LandCleanupStatus.Refused;
                op.LastReason = "worktree_dirty";
                break;
            case "schema-2": op.SchemaVersion = 2; break;
            case "recovery-adopt": op.RecoveryMode = LandRecoveryMode.AdoptReviewedSource; break;
            case "protected-master":
                op.SourceFullRef = "refs/heads/master"; op.TargetFullRef = op.DestinationFullRef = "refs/heads/main";
                break;
            case "protected-rc": op.SourceFullRef = "refs/heads/rc/2026-11-01"; break;
            case "non-delegate-name": op.SourceFullRef = "refs/heads/feat/other"; break;
            case "source-sha-mismatch": op.SourceRemoteSha = Sha('e'); break;
            case "before-cutoff": settings.SourceBranchDeletionAfterUtc = confirmed; break;
            case "runner-not-enabled": runners = ["runner-a"]; break;
            case "desktop-only-enabled": settings.SourceBranchDeletionRunnerIds = ["runner-b"]; break;
            case "already-deleted": op.BranchDeletionState = LandBranchDeletionState.Deleted; break;
        }
        var decision = PublishedBranchDeletionPolicy.Decide(op, settings, runners, now);
        decision.ShouldBe(new BranchDeletionDecision(Enum.Parse<BranchDeletionDecisionKind>(kind), reason), name);
    }

    // ---- V-17 dependents (isolated schema, no git) ----

    [Test]
    [Timeout(60_000)]
    [Arguments("worktree-base-branch")]
    [Arguments("worktree-base-task")]
    [Arguments("requested-base-task-queued")]
    [Arguments("repair-source-open")]
    [Arguments("same-branch-follow-up")]
    [Arguments("pending-land-request-other-task")]
    [Arguments("terminal-dependent-does-not-block")]
    [Arguments("owner-card-not-done")]
    [Arguments("owner-reopened")]
    [Arguments("pending-land-request-owner")]
    public async Task C0824_dependent_rows(string row, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var owner = Task_(Guid.NewGuid(), Branch, AgentTaskStatus.Succeeded);
        var dependent = Task_(Guid.NewGuid(), "feat/card-task-" + Guid.NewGuid().ToString("N")[..8], AgentTaskStatus.Working);
        switch (row)
        {
            case "worktree-base-branch": dependent.WorktreeBaseBranch = Branch; break;
            case "worktree-base-task": dependent.WorktreeBaseTaskId = owner.Id; dependent.Status = AgentTaskStatus.Queued; break;
            case "requested-base-task-queued": dependent.RequestedWorktreeBaseTaskId = owner.Id; dependent.Status = AgentTaskStatus.Queued; break;
            case "repair-source-open": dependent.RepairSourceTaskId = owner.Id; dependent.Status = AgentTaskStatus.Blocked; break;
            case "same-branch-follow-up": dependent.WorktreeBranch = Branch; dependent.Status = AgentTaskStatus.Dispatched; break;
            case "pending-land-request-other-task":
                dependent.WorktreeBranch = Branch; dependent.Status = AgentTaskStatus.Succeeded;
                db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
                {
                    Id = Guid.NewGuid(), TaskId = dependent.Id, RequestedAt = Start, State = LandRequestState.Queued,
                    IsPending = true, LastEvaluatedAt = Start, LastProgressAt = Start,
                });
                break;
            case "terminal-dependent-does-not-block": dependent.WorktreeBaseBranch = Branch; dependent.Status = AgentTaskStatus.Succeeded; break;
            case "owner-card-not-done":
                // The brief's card rule: a branch bound to a card that is not Done is kept until the card is Done.
                dependent.Status = AgentTaskStatus.Succeeded;
                var projectId = Guid.NewGuid();
                var boardId = Guid.NewGuid();
                var columnId = Guid.NewGuid();
                var cardId = Guid.NewGuid();
                db.Projects.Add(new Project { Id = projectId, Name = "c0824-s2-" + projectId.ToString("N")[..8],
                    GitRepositoryUrl = "https://example.test/c0824.git", CreatedAt = Start, UpdatedAt = Start });
                db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "C0824", CreatedAt = Start, UpdatedAt = Start });
                db.BoardColumns.Add(new BoardColumn { Id = columnId, BoardId = boardId, StateKey = "inprogress", Name = "In progress",
                    ColumnOrder = 0, CardStatus = CardStatus.InProgress, CreatedAt = Start, UpdatedAt = Start });
                db.Cards.Add(new Card { Id = cardId, BoardId = boardId, BoardColumnId = columnId, Identifier = "CARD-8240",
                    Title = "C0824 S2", Description = "card rule", Status = CardStatus.InProgress, LabelsJson = "[]",
                    CreatedAt = Start, UpdatedAt = Start });
                owner.CardId = cardId;
                break;
            case "owner-reopened":
                // The owner task itself was reopened after its land: its branch is live work again.
                owner.Status = AgentTaskStatus.Working; owner.CompletedAt = null;
                dependent.Status = AgentTaskStatus.Succeeded;
                break;
            case "pending-land-request-owner":
                // A-3: the owner's own new land of the same unchanged branch (for example to another target).
                dependent.Status = AgentTaskStatus.Succeeded;
                db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
                {
                    Id = Guid.NewGuid(), TaskId = owner.Id, RequestedAt = Start, State = LandRequestState.Queued,
                    IsPending = true, LastEvaluatedAt = Start, LastProgressAt = Start,
                });
                break;
        }
        db.AgentTasks.AddRange(owner, dependent);
        var op = PublishedLandingRow.Build(owner.Id, Branch, Start);
        db.AgentTaskLandings.Add(op);
        await db.SaveChangesAsync(ct);

        var found = await PublishedBranchDeletionDependents.FindAsync(db, op, ct);
        if (row == "terminal-dependent-does-not-block")
            found.ShouldBeNull("a terminal dependent never blocks (liveness control)");
        else
        {
            found.ShouldNotBeNull(row);
            var ownerRows = new[] { "owner-card-not-done", "owner-reopened", "pending-land-request-owner" };
            found.TaskId.ShouldBe(ownerRows.Contains(row) ? owner.Id : dependent.Id, row);
            if (row == "owner-reopened") found.Term.ShouldBe("owner_open", row);
            if (row == "pending-land-request-owner") found.Term.ShouldBe("pending_land_request", row);
        }
    }

    // ---- V-14, V-18, V-45, V-46, V-48: the confirmed rebase land ----

    [Test]
    [Timeout(120_000)]
    public async Task C0824_confirmed_land_deletes_with_lease_after_delay(CancellationToken ct)
    {
        await using var w = await World.LandedAsync();
        var op = (await w.H.OperationAsync())!;
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        op.VerifiedSourceSha.ShouldNotBe(op.SourceRemoteSha, "the fixture is a real rebase land");
        var pins = await w.LandPinsAsync(op);
        pins.ShouldNotBeEmpty();
        await w.EnsureTrackingRefAsync(op.SourceRemoteSha!);
        await w.CheckOutKeeperAsync(op.SourceRemoteSha!);
        var due = op.RemoteConfirmedAt!.Value.AddMinutes(1440);

        w.Clock.SetUtcNow(due.AddSeconds(-1));
        await w.SweepAsync(ct);
        w.DeletePushes().ShouldBeEmpty("before-due-no-push");
        (await w.H.OperationAsync())!.BranchDeletionState.ShouldBe(LandBranchDeletionState.None, "before-due-no-push");

        w.Clock.SetUtcNow(due);
        await w.SweepAsync(ct);
        (await w.RemoteSourceExitAsync()).ShouldBe(2, "remote-absent");
        var pushes = w.DeletePushes();
        pushes.Count.ShouldBe(1, "lease-argv");
        pushes[0].Length.ShouldBe(4, "lease-argv");
        pushes[0][2].ShouldBe($"--force-with-lease={w.H.Fixture.SourceRef}:{op.SourceRemoteSha}", "lease-argv");
        pushes[0][3].ShouldBe(":" + w.H.Fixture.SourceRef, "lease-argv");
        var row = (await w.H.OperationAsync())!;
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted, "row-deleted");
        row.BranchDeletionReason.ShouldBe("deleted", "row-deleted");
        row.BranchDeletionAt.ShouldBe(due, "row-deleted");
        (await w.LandPinsAsync(op)).ShouldBe(pins, "pins-kept");
        (await w.LocalRefAsync("refs/remotes/origin/" + w.ShortBranch)).ShouldBeNull("tracking-gone");
        (await w.LocalRefAsync(w.H.Fixture.SourceRef)).ShouldBe(op.SourceRemoteSha, "local-branch-kept");
        (await w.EventsAsync()).Count.ShouldBe(1, "one-event");
    }

    // ---- V-15 ----

    [Test]
    [Timeout(120_000)]
    [Arguments("target-hook-reject")]
    [Arguments("divergent-remote")]
    [Arguments("unconfirmed-cut")]
    public async Task C0824_refused_or_unconfirmed_land_keeps_branch(string family, CancellationToken ct)
    {
        await using var w = await World.CreateAsync(delayMinutes: 0);
        var h = w.H;
        await h.AddSourceAsync();
        switch (family)
        {
            case "target-hook-reject":
                await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
                await Card0452LandingCases.InstallRejectingTargetHookAsync(h);
                break;
            case "divergent-remote":
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "local divergent base");
                var remoteTip = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit-tree",
                    h.Fixture.SeedSha + "^{tree}", "-p", h.Fixture.SeedSha, "-m", "remote advanced")).Trim();
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", remoteTip + ":" + h.Fixture.TargetRef);
                break;
            case "unconfirmed-cut":
                await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
                h.Fault.Phase = LandPhase.PublicationConfirmed;
                break;
        }
        try { await h.RunAsync(ct); }
        catch (LandingSafetyHarness.InjectedSaveFailure) when (family == "unconfirmed-cut") { }
        h.Fault.Phase = null;
        // divergent-remote refuses before any operation exists: the strongest "never published".
        var op = await h.OperationAsync();
        if (op is not null)
            new AgentTaskLandingState().HasPublication(op).ShouldBeFalse(family + " fixture must stay unpublished");
        var before = await w.RemoteSourceShaAsync();
        before.ShouldNotBeNull();

        w.Clock.Advance(TimeSpan.FromDays(2));
        await w.SweepAsync(ct);
        await w.SweepAsync(ct);
        (await w.RemoteSourceShaAsync()).ShouldBe(before, "remote-kept");
        w.DeletePushes().ShouldBeEmpty("no-delete-push");
        if (op is not null)
            (await h.OperationAsync())!.BranchDeletionState.ShouldBe(LandBranchDeletionState.None, "row-none");
    }

    // ---- V-16a, V-16b ----

    [Test]
    [Timeout(120_000)]
    public async Task C0824_branch_moved_before_sweep_is_kept_without_push(CancellationToken ct)
    {
        await using var w = await World.LandedAsync();
        var op = (await w.H.OperationAsync())!;
        var moved = await w.PushToSourceAsync("moved-before");
        var landCommands = w.H.Fixture.Git.Commands.Count;
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        await w.SweepAsync(ct);
        (await w.RemoteSourceShaAsync()).ShouldBe(moved, "remote-kept");
        var row = (await w.H.OperationAsync())!;
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Kept);
        row.BranchDeletionReason.ShouldBe("branch_moved");
        w.H.Fixture.Git.Commands.Skip(landCommands).Where(c => c.Arguments[0] == "push").ShouldBeEmpty("no-push");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_branch_moved_at_push_is_refused_by_lease(CancellationToken ct)
    {
        await using var w = await World.LandedAsync();
        var op = (await w.H.OperationAsync())!;
        await w.EnsureTrackingRefAsync(op.SourceRemoteSha!);
        string? moved = null;
        w.H.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (moved is null && args.Count > 0 && args[0] == "push" && args[^1] == ":" + w.H.Fixture.SourceRef)
                moved = await w.PushToSourceAsync("moved-at-push");
            return null;
        };
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        await w.SweepAsync(ct);
        moved.ShouldNotBeNull("the race fired at the delete push");
        (await w.RemoteSourceShaAsync()).ShouldBe(moved, "lease-held");
        var row = (await w.H.OperationAsync())!;
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Kept, "lease-held");
        row.BranchDeletionReason.ShouldBe("branch_moved", "lease-held");
        (await w.LocalRefAsync("refs/remotes/origin/" + w.ShortBranch)).ShouldNotBeNull("tracking-kept");
    }

    // ---- V-19 and the DryRun ----

    [Test]
    [Timeout(120_000)]
    public async Task C0824_flag_off_runs_no_git_and_writes_nothing(CancellationToken ct)
    {
        await using var w = await World.LandedAsync(flag: false);
        var op = (await w.H.OperationAsync())!;
        var before = await w.RemoteSourceShaAsync();
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddDays(30));
        var commands = w.H.Fixture.Git.Commands.Count;
        await w.SweepAsync(ct);
        w.H.Fixture.Git.Commands.Count.ShouldBe(commands, "no-git");
        var row = (await w.H.OperationAsync())!;
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.None, "no-write");
        row.BranchDeletionAt.ShouldBeNull("no-write");
        row.BranchDeletionReason.ShouldBeNull("no-write");
        row.BranchDeletionDetail.ShouldBeNull("no-write");
        row.RunnerRefsDeletedAt.ShouldBeNull("no-write");
        (await w.RemoteSourceShaAsync()).ShouldBe(before, "remote-kept");
        (await w.EventsAsync()).ShouldBeEmpty("no-write");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_dry_run_never_deletes(CancellationToken ct)
    {
        await using var w = await World.LandedAsync(dryRun: true, runnerId: "runner-a", enabledRunners: ["runner-a"]);
        var op = (await w.H.OperationAsync())!;
        var before = await w.RemoteSourceShaAsync();
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        var result = await w.SweepAsync(ct);
        result.DryRun.ShouldBeTrue();
        result.Rows.ShouldContain(r => r.OperationId == op.Id && r.Outcome == "would_delete", "dry-run-reported");
        w.DeletePushes().ShouldBeEmpty("dry-run-no-push");
        w.Runner.Calls.ShouldBe(0, "dry-run-no-runner-call");
        (await w.RemoteSourceShaAsync()).ShouldBe(before, "dry-run-remote-kept");
        (await w.H.OperationAsync())!.BranchDeletionState.ShouldBe(LandBranchDeletionState.None, "dry-run-no-write");
    }

    // ---- V-41 ----

    [Test]
    [Timeout(120_000)]
    [Arguments("checked-out", "branch_checked_out")]
    [Arguments("unpublished-local", "unpublished_local_ref")]
    [Arguments("offline", "runner_unreachable")]
    [Arguments("capability-missing", "runner_capability_missing")]
    [Arguments("runner-not-enabled", "runner_volume_not_enabled")]
    public async Task C0824_runner_refusal_or_absence_defers_before_origin(string family, string reason, CancellationToken ct)
    {
        await using var w = await World.LandedAsync(runnerId: "runner-a",
            enabledRunners: family == "runner-not-enabled" ? [] : ["runner-a"]);
        switch (family)
        {
            case "checked-out": w.Runner.Answer = new(RunnerBranchRefsOutcome.Refused, "branch_checked_out"); break;
            case "unpublished-local": w.Runner.Answer = new(RunnerBranchRefsOutcome.Refused, "unpublished_local_ref"); break;
            case "offline": w.Runner.Throw = new IOException("runner socket closed"); break;
            case "capability-missing": w.Runner.Supports = false; break;
        }
        var op = (await w.H.OperationAsync())!;
        var before = await w.RemoteSourceShaAsync();
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        await w.SweepAsync(ct);
        (await w.RemoteSourceShaAsync()).ShouldBe(before, "origin-untouched");
        w.DeletePushes().ShouldBeEmpty("origin-untouched");
        var row = (await w.H.OperationAsync())!;
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deferred, family);
        row.BranchDeletionReason.ShouldBe(reason, family);
        row.RunnerRefsDeletedAt.ShouldBeNull(family);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_runner_refs_precede_origin_delete(CancellationToken ct)
    {
        await using var w = await World.LandedAsync(runnerId: "runner-a", enabledRunners: ["runner-a"]);
        var op = (await w.H.OperationAsync())!;
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        await w.SweepAsync(ct);
        (await w.RemoteSourceExitAsync()).ShouldBe(2, "remote-absent");
        w.Runner.Calls.ShouldBe(1);
        w.Runner.Requests.ShouldBe([("runner-a", w.ShortBranch, op.SourceRemoteSha!)]);
        var pushIndex = w.H.Fixture.Git.Commands.FindIndex(c => c.Arguments[0] == "push" && c.Arguments[^1] == ":" + w.H.Fixture.SourceRef);
        pushIndex.ShouldBeGreaterThanOrEqualTo(0);
        w.Runner.CommandIndexAtCall.ShouldNotBeNull();
        w.Runner.CommandIndexAtCall!.Value.ShouldBeLessThanOrEqualTo(pushIndex, "runner-first");
        var row = (await w.H.OperationAsync())!;
        row.RunnerRefsDeletedAt.ShouldNotBeNull();
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted);
    }

    // ---- V-42 ----

    [Test]
    [Timeout(120_000)]
    public async Task C0824_crash_after_push_before_save_recovers_once(CancellationToken ct)
    {
        await using var w = await World.LandedAsync();
        var op = (await w.H.OperationAsync())!;
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        w.H.Fault.Matches = o => o.BranchDeletionState == LandBranchDeletionState.Deleted;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => w.SweepAsync(ct));
        w.H.Fault.Matches = null;
        (await w.RemoteSourceExitAsync()).ShouldBe(2, "the push ran before the cut");
        (await w.H.OperationAsync())!.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleting, "intent-saved");

        await w.H.RestartServicesAsync();
        await w.SweepAsync(ct);
        var row = (await w.H.OperationAsync())!;
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted, "recovered");
        row.BranchDeletionReason.ShouldBe("deleted", "recovered");
        w.DeletePushes().Count.ShouldBe(1, "one-delete-push");
        (await w.EventsAsync()).Count.ShouldBe(1, "one-event");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_crash_after_runner_refs_recovers_idempotently(CancellationToken ct)
    {
        await using var w = await World.LandedAsync(runnerId: "runner-a", enabledRunners: ["runner-a"]);
        var op = (await w.H.OperationAsync())!;
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        w.H.Fault.Matches = o => o.RunnerRefsDeletedAt is not null;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => w.SweepAsync(ct));
        w.H.Fault.Matches = null;
        w.DeletePushes().ShouldBeEmpty("origin untouched until the runner step is recorded");

        await w.H.RestartServicesAsync();
        w.Runner.Answer = new(RunnerBranchRefsOutcome.Deleted, null, [], ["refs/heads/" + w.ShortBranch]);
        await w.SweepAsync(ct);
        w.Runner.Calls.ShouldBe(2, "runner-op-repeated");
        w.DeletePushes().Count.ShouldBe(1, "origin-deleted-once");
        (await w.RemoteSourceExitAsync()).ShouldBe(2);
        (await w.H.OperationAsync())!.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted);
    }

    // ---- V-47 ----

    [Test]
    [Timeout(120_000)]
    public async Task C0824_backlog_cutoff_and_run_bound(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var cutoff = Start.AddDays(-10);
        var after = new List<Guid>();
        var beforeCutoff = new List<Guid>();
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            for (var i = 0; i < 32; i++)
            {
                var taskId = Guid.NewGuid();
                var branch = "feat/card-task-" + taskId.ToString("N")[..8];
                seed.AgentTasks.Add(Task_(taskId, branch, AgentTaskStatus.Succeeded));
                var confirmed = i < 2 ? cutoff.AddMinutes(-i - 1) : cutoff.AddMinutes(i);
                var op = PublishedLandingRow.Build(taskId, branch, confirmed);
                seed.AgentTaskLandings.Add(op);
                (i < 2 ? beforeCutoff : after).Add(op.Id);
            }
            await seed.SaveChangesAsync(ct);
        }
        var git = new RecordingLandingGit();
        var monitor = new MutableMonitor(Enabled(cutoff));
        async Task<PublishedBranchDeletionRunResult> Run()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            return await new PublishedBranchDeletionService(db, git, new FreeLease(), new RecordingRunnerBranchRefs(),
                monitor, new FakeTimeProvider(Start), NullLogger<PublishedBranchDeletionService>.Instance).RunAsync(ct);
        }

        monitor.Value.SourceBranchDeletionAfterUtc = null;
        await Run();
        git.Deleted.Count.ShouldBe(0, "null-cutoff-none");

        monitor.Value.SourceBranchDeletionAfterUtc = cutoff;
        await Run();
        git.Deleted.Count.ShouldBe(25, "bound-25");
        await Run();
        git.Deleted.Count.ShouldBe(30, "second run deletes the remaining 5");
        await using var read = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var rows = await read.AgentTaskLandings.AsNoTracking().ToListAsync(ct);
        rows.Where(r => beforeCutoff.Contains(r.Id)).ShouldAllBe(r => r.BranchDeletionState == LandBranchDeletionState.None, "cutoff-held");
        rows.Where(r => after.Contains(r.Id)).ShouldAllBe(r => r.BranchDeletionState == LandBranchDeletionState.Deleted);
        var preCutoffRefs = rows.Where(r => beforeCutoff.Contains(r.Id)).Select(r => r.SourceFullRef).ToHashSet();
        git.Touched.ShouldNotContain(t => preCutoffRefs.Contains(t), "cutoff-held");
    }

    // ---- V-46 ----

    [Test]
    [Timeout(120_000)]
    public async Task C0824_deferred_reason_evented_once(CancellationToken ct)
    {
        await using var w = await World.LandedAsync();
        var op = (await w.H.OperationAsync())!;
        var dependentId = Guid.NewGuid();
        await using (var db = w.H.CreateContext())
        {
            var dependent = Task_(dependentId, "feat/card-task-" + dependentId.ToString("N")[..8], AgentTaskStatus.Working);
            dependent.WorktreeBaseBranch = w.ShortBranch;
            db.AgentTasks.Add(dependent);
            await db.SaveChangesAsync(ct);
        }
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        await w.SweepAsync(ct);
        await w.SweepAsync(ct);
        await w.SweepAsync(ct);
        var row = (await w.H.OperationAsync())!;
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deferred);
        row.BranchDeletionReason.ShouldBe("dependent_open");
        (row.BranchDeletionDetail ?? "").ShouldContain(dependentId.ToString());
        (await w.EventsAsync()).Count.ShouldBe(1, "deferred-once");
        w.DeletePushes().ShouldBeEmpty();

        await using (var db = w.H.CreateContext())
        {
            var dependent = await db.AgentTasks.SingleAsync(t => t.Id == dependentId, ct);
            dependent.Status = AgentTaskStatus.Succeeded;
            await db.SaveChangesAsync(ct);
        }
        w.Clock.Advance(TimeSpan.FromMinutes(15));
        await w.SweepAsync(ct);
        (await w.H.OperationAsync())!.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted);
        var events = await w.EventsAsync();
        events.Count.ShouldBe(2, "one Deferred then one Deleted");
        (await w.RemoteSourceExitAsync()).ShouldBe(2);
    }

    // ---- V-49 (Code addition): fresh containment at deletion time ----

    [Test]
    [Timeout(120_000)]
    public async Task C0824_target_rewritten_after_land_keeps_branch(CancellationToken ct)
    {
        await using var w = await World.LandedAsync();
        var op = (await w.H.OperationAsync())!;
        var before = await w.RemoteSourceShaAsync();
        // The published commit is no longer on origin's target (a force-push rewrite after the land).
        await w.H.Fixture.RequiredAsync(w.H.Fixture.Repository, "push", "--force", "origin",
            op.TargetBeforeSha + ":" + w.H.Fixture.TargetRef);
        w.Clock.SetUtcNow(op.RemoteConfirmedAt!.Value.AddMinutes(1440));
        await w.SweepAsync(ct);
        (await w.RemoteSourceShaAsync()).ShouldBe(before, "remote-kept");
        w.DeletePushes().ShouldBeEmpty("containment-before-push");
        var row = (await w.H.OperationAsync())!;
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Kept);
        row.BranchDeletionReason.ShouldBe("publication_not_contained");
    }

    // ---- guards without a witness in round 1 (Review 370083f1 test gaps 2-4) and advisories A-1, A-2 ----

    [Test]
    [Timeout(120_000)]
    public async Task C0824_kill_switch_mid_run_stops_before_next_row(CancellationToken ct)
    {
        await using var w = await SyntheticWorld.CreateAsync(2, ct);
        // The operator switches the flag off while the first row's delete is in flight.
        w.Git.OnDelete = () => w.Monitor.Value.DeleteSourceBranchAfterPublication = false;
        var result = await w.RunAsync(ct);
        result.Stopped.ShouldBe("kill_switch", "stopped-kill-switch");
        w.Git.Deleted.Count.ShouldBe(1, "one-row-only");
        var rows = await w.RowsAsync(ct);
        rows[0].BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted);
        rows[1].BranchDeletionState.ShouldBe(LandBranchDeletionState.None, "next-row-untouched");
        w.Git.Touched.ShouldNotContain(rows[1].SourceFullRef, "next-row-no-git");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_repository_busy_defers_without_git(CancellationToken ct)
    {
        await using var w = await SyntheticWorld.CreateAsync(1, ct, runnerIds: ["runner-a"]);
        w.Lease = new BusyLease();
        await w.RunAsync(ct);
        var row = (await w.RowsAsync(ct))[0];
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deferred, "busy-deferred");
        row.BranchDeletionReason.ShouldBe("repository_busy", "busy-deferred");
        w.Git.Touched.ShouldBeEmpty("busy-no-git");
        w.Git.Deleted.ShouldBeEmpty("busy-no-git");
        w.Runner.Calls.ShouldBe(0, "busy-no-runner-call");
        row.RunnerRefsDeletedAt.ShouldBeNull();
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("short")]
    [Arguments("full")]
    [Arguments("tracking")]
    [Arguments("remote-tracking")]
    public async Task C0824_second_runner_on_branch_is_asked_and_its_refusal_defers(string spelling, CancellationToken ct)
    {
        await using var w = await SyntheticWorld.CreateAsync(1, ct, runnerIds: ["runner-a", "runner-b"]);
        var op = (await w.RowsAsync(ct))[0];
        var branch = op.SourceFullRef[11..];
        // A follow-up task on another runner volume held the same branch; its label may be stored in any spelling.
        await w.AddTaskAsync(spelling switch
        {
            "short" => branch,
            "full" => "refs/heads/" + branch,
            "tracking" => "origin/" + branch,
            _ => "refs/remotes/origin/" + branch,
        }, "runner-b", ct);
        w.Runner.AnswerFor = runner => runner == "runner-b"
            ? new(RunnerBranchRefsOutcome.Refused, "branch_checked_out") : null;
        await w.RunAsync(ct);
        w.Runner.Requests.Select(r => r.RunnerId).ShouldBe(["runner-a", "runner-b"], spelling + ": both-asked");
        w.Git.Deleted.ShouldBeEmpty(spelling + ": origin-untouched");
        var row = (await w.RowsAsync(ct))[0];
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deferred, spelling);
        row.BranchDeletionReason.ShouldBe("branch_checked_out", spelling);
        row.RunnerRefsDeletedAt.ShouldBeNull(spelling);

        // Once the second runner accepts, both are asked again and origin is deleted.
        w.Runner.AnswerFor = null;
        await w.RunAsync(ct);
        w.Runner.Requests.Select(r => r.RunnerId).ShouldBe(["runner-a", "runner-b", "runner-a", "runner-b"], spelling);
        w.Git.Deleted.Count.ShouldBe(1, spelling + ": deleted-after-accept");
        (await w.RowsAsync(ct))[0].BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted, spelling);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_dependent_opened_during_runner_step_defers_before_push(CancellationToken ct)
    {
        await using var w = await SyntheticWorld.CreateAsync(1, ct, runnerIds: ["runner-a"]);
        var op = (await w.RowsAsync(ct))[0];
        var dependentId = Guid.NewGuid();
        // A-2: a task is queued on the branch while the runner step (up to six minutes) is in flight.
        w.Runner.BeforeAnswer = async () =>
        {
            await using var db = w.Db();
            var dependent = Task_(dependentId, "feat/card-task-" + dependentId.ToString("N")[..8], AgentTaskStatus.Queued);
            dependent.WorktreeBaseBranch = op.SourceFullRef[11..];
            db.AgentTasks.Add(dependent);
            await db.SaveChangesAsync(ct);
        };
        await w.RunAsync(ct);
        w.Runner.Calls.ShouldBe(1);
        w.Git.Deleted.ShouldBeEmpty("recheck-before-push");
        var row = (await w.RowsAsync(ct))[0];
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deferred, "recheck-before-push");
        row.BranchDeletionReason.ShouldBe("dependent_open", "recheck-before-push");
        (row.BranchDeletionDetail ?? "").ShouldContain(dependentId.ToString(), Case.Sensitive, "recheck-before-push");
    }

    // ---- helpers ----

    /// <summary>
    /// Synthetic published rows (no land) in an isolated schema, driven through the real service with the recording
    /// git, lease and runner seams: the shape of V-47, for the guards whose effect is ordering, not git.
    /// </summary>
    private sealed class SyntheticWorld : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public RecordingLandingGit Git { get; } = new();
        public RecordingRunnerBranchRefs Runner { get; } = new();
        public MutableMonitor Monitor { get; } = new(Enabled(Start.AddDays(-10)));
        public IRepositoryMutationLease Lease { get; set; } = new FreeLease();

        public static async Task<SyntheticWorld> CreateAsync(int rows, CancellationToken ct, IReadOnlyList<string>? runnerIds = null)
        {
            var w = new SyntheticWorld { Schema = await TestDbFixture.CreateIsolatedSchemaAsync() };
            w.Monitor.Value.SourceBranchDeletionRunnerIds = [.. runnerIds ?? []];
            await using var db = w.Db();
            for (var i = 0; i < rows; i++)
            {
                var taskId = Guid.NewGuid();
                var branch = "feat/card-task-" + taskId.ToString("N")[..8];
                var task = Task_(taskId, branch, AgentTaskStatus.Succeeded);
                task.RunnerId = runnerIds is { Count: > 0 } ? runnerIds[0] : null;
                db.AgentTasks.Add(task);
                db.AgentTaskLandings.Add(PublishedLandingRow.Build(taskId, branch, Start.AddDays(-5).AddMinutes(i)));
            }
            await db.SaveChangesAsync(ct);
            return w;
        }

        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        public async Task AddTaskAsync(string worktreeBranch, string runnerId, CancellationToken ct)
        {
            await using var db = Db();
            var id = Guid.NewGuid();
            var task = Task_(id, worktreeBranch, AgentTaskStatus.Succeeded);
            task.RunnerId = runnerId;
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync(ct);
        }

        public async Task<PublishedBranchDeletionRunResult> RunAsync(CancellationToken ct)
        {
            await using var db = Db();
            return await new PublishedBranchDeletionService(db, Git, Lease, Runner, Monitor, new FakeTimeProvider(Start),
                NullLogger<PublishedBranchDeletionService>.Instance).RunAsync(ct);
        }

        public async Task<List<AgentTaskLanding>> RowsAsync(CancellationToken ct)
        {
            await using var db = Db();
            return await db.AgentTaskLandings.AsNoTracking().OrderBy(o => o.RemoteConfirmedAt).ToListAsync(ct);
        }

        public ValueTask DisposeAsync() => Schema.DisposeAsync();
    }

    private sealed class BusyLease : IRepositoryMutationLease
    {
        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) => Task.FromResult<RepositoryLease?>(null);
        public bool Owns(RepositoryLease lease, string repository) => false;
    }

    internal static LandingSettings Enabled(DateTime? cutoff) => new()
    {
        DeleteSourceBranchAfterPublication = true,
        SourceBranchDeleteDelayMinutes = 1440,
        SourceBranchDeletionAfterUtc = cutoff is { } c ? new DateTimeOffset(c, TimeSpan.Zero) : null,
        MaxSourceBranchDeletionsPerRun = 25,
    };

    internal static string Sha(char c) => new(c, 40);

    internal static AgentTask Task_(Guid id, string branch, AgentTaskStatus status) => new()
    {
        Id = id, RootTaskId = id, Title = "C0824 S2 fixture", Goal = "fixture",
        Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
        WorkingDirectory = "/c0824/repo", RepoPath = "/c0824/repo", WorktreePath = "/c0824/trees/" + id.ToString("N")[..8],
        WorktreeBranch = branch, MergeTargetRef = "master", Status = status, ReplyTo = AgentTaskReplyTo.None,
        CreatedAt = Start.AddDays(-30), CompletedAt = status is AgentTaskStatus.Succeeded ? Start.AddDays(-29) : null,
    };

    /// <summary>A synthetic schema-3 row that passes <see cref="AgentTaskLandingState.HasPublication"/>.</summary>
    internal static class PublishedLandingRow
    {
        public static AgentTaskLanding Build(Guid taskId, string branch, DateTime confirmed)
        {
            var id = Guid.NewGuid();
            var source = Sha('a');
            var rebased = Sha('b');
            var op = new AgentTaskLanding
            {
                Id = id, TaskId = taskId, SchemaVersion = 3, Active = true, Phase = LandPhase.Complete,
                Publication = LandPublicationOutcome.Landed, Cleanup = LandCleanupStatus.Complete,
                CreatedAt = confirmed.AddMinutes(-5), UpdatedAt = confirmed,
                RepositoryPath = "/c0824/repo", CommonDirectory = "/c0824/repo/.git", WorktreePath = "/c0824/trees/source",
                GitDirectory = "/c0824/repo/.git/worktrees/source", SourceFullRef = "refs/heads/" + branch,
                OriginalSourceSha = source, ReviewedSourceSha = source, SourceLocalSha = source,
                SourceRemoteSha = source, SourceRemoteFingerprint = new string('F', 64),
                RebasedSourceSha = rebased, VerifiedSourceSha = rebased, VerificationPassed = true, VerifiedAt = confirmed.AddMinutes(-2),
                SourcePinned = true, TargetPinned = true, PreparedPinned = true,
                TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master", TargetBeforeSha = Sha('c'),
                LocalTargetBeforeSha = Sha('c'), LandWorktreePath = "/c0824/land/" + id.ToString("N"),
                RemoteFingerprint = new string('F', 64), ObservedRemoteTargetSha = rebased, RemoteConfirmedAt = confirmed,
                ConfirmationMethod = "push-endpoint-read-fetch-ancestry",
                RecoveryRefPrefix = $"refs/antiphon/land/{taskId:N}/{id:N}", ApprovalLandRequestId = Guid.NewGuid(),
            };
            new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("synthetic row must be a confirmed publication");
            return op;
        }
    }

    internal sealed class MutableMonitor(LandingSettings value) : IOptionsMonitor<LandingSettings>
    {
        public LandingSettings Value { get; set; } = value;
        public LandingSettings CurrentValue => Value;
        public LandingSettings Get(string? name) => Value;
        public IDisposable? OnChange(Action<LandingSettings, string?> listener) => null;
    }

    internal sealed class FreeLease : IRepositoryMutationLease
    {
        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
            Task.FromResult<RepositoryLease?>(new Held());
        public bool Owns(RepositoryLease lease, string repository) => lease is Held;
        private sealed class Held : RepositoryLease
        {
            public override string CommonDirectory => "/c0824/repo/.git";
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>The V-41/V-42 runner seam: records calls (with the git command index at the call) and answers as told.</summary>
    internal sealed class RecordingRunnerBranchRefs(Func<int>? commandIndex = null) : IRunnerBranchRefs
    {
        public bool Supports { get; set; } = true;
        public RunnerBranchRefsResult Answer { get; set; } = new(RunnerBranchRefsOutcome.Deleted, null, [], []);
        /// <summary>A per-runner answer; null falls back to <see cref="Answer"/>.</summary>
        public Func<string, RunnerBranchRefsResult?>? AnswerFor { get; set; }
        /// <summary>Runs inside the runner step, before the answer (the window A-2 rechecks after).</summary>
        public Func<Task>? BeforeAnswer { get; set; }
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }
        public int? CommandIndexAtCall { get; private set; }
        public List<(string RunnerId, string Branch, string Sha)> Requests { get; } = [];

        public Task<bool> SupportsAsync(string runnerId, CancellationToken ct) => Task.FromResult(Supports);

        public async Task<RunnerBranchRefsResult> DeleteAsync(string runnerId, string branch, string expectedSha, CancellationToken ct)
        {
            Calls++;
            CommandIndexAtCall ??= commandIndex?.Invoke();
            Requests.Add((runnerId, branch, expectedSha));
            if (BeforeAnswer is { } before) await before();
            if (Throw is not null) throw Throw;
            return AnswerFor?.Invoke(runnerId) ?? Answer;
        }
    }

    /// <summary>V-47's git: every recheck answers the expected SHA until the ref is deleted, and every target contains it.</summary>
    internal sealed class RecordingLandingGit : LandingGit
    {
        public HashSet<string> Deleted { get; } = [];
        public List<string> Touched { get; } = [];
        public Action? OnDelete { get; set; }

        public override Task<LandingSourceRecheck> RecheckSourceRemoteAsync(string repository, string sourceFullRef,
            string expectedSha, string expectedFingerprint, CancellationToken ct)
        {
            Touched.Add(sourceFullRef);
            return Task.FromResult(Deleted.Contains(sourceFullRef)
                ? new LandingSourceRecheck(null, expectedFingerprint, "source_remote_missing")
                : new LandingSourceRecheck(expectedSha, expectedFingerprint, null));
        }

        public override Task<LandingRemoteObservation> ConfirmTargetContainsAsync(string repository, string targetFullRef,
            string sha, string expectedFingerprint, CancellationToken ct) =>
            Task.FromResult(new LandingRemoteObservation(Sha('d'), true, null));

        public override Task<LandingGitResult> DeleteSourceBranchAsync(string repository, string sourceFullRef,
            string expectedRemoteSha, string expectedFingerprint, CancellationToken ct)
        {
            Touched.Add(sourceFullRef);
            Deleted.Add(sourceFullRef);
            OnDelete?.Invoke();
            return Task.FromResult(new LandingGitResult(0, "", ""));
        }

        public override Task<LandingGitResult> DeleteTrackingRefAsync(string repository, string sourceFullRef,
            string expectedSha, CancellationToken ct) => Task.FromResult(new LandingGitResult(0, "", ""));
    }

    /// <summary>A real land in <see cref="LandingSafetyHarness"/> plus the deletion service under a fake clock.</summary>
    private sealed class World : IAsyncDisposable
    {
        public required LandingSafetyHarness H { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required MutableMonitor Settings { get; init; }
        public required RecordingRunnerBranchRefs Runner { get; init; }
        public string ShortBranch => H.Fixture.SourceRef[11..];

        public static async Task<World> CreateAsync(bool flag = true, bool dryRun = false, int delayMinutes = 1440,
            IReadOnlyList<string>? enabledRunners = null)
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(Start));
            // The production delegate branch shape (feat/card-task-<8hex>); the policy refuses any other name.
            var taskId = Guid.NewGuid();
            var h = new LandingSafetyHarness(taskId: taskId, sourceBranch: Antiphon.SessionRunner.Contracts.DelegateBranchName.Legacy(taskId))
                { Clock = clock };
            var settings = new MutableMonitor(Enabled(Start.AddDays(-1)));
            settings.Value.DeleteSourceBranchAfterPublication = flag;
            settings.Value.SourceBranchDeletionDryRun = dryRun;
            settings.Value.SourceBranchDeleteDelayMinutes = delayMinutes;
            settings.Value.SourceBranchDeletionRunnerIds = [.. enabledRunners ?? []];
            var runner = new RecordingRunnerBranchRefs(() => h.Fixture.Git.Commands.Count);
            h.ConfigureServices = services =>
            {
                services.AddSingleton<IOptionsMonitor<LandingSettings>>(settings);
                services.AddSingleton<IRunnerBranchRefs>(runner);
                services.AddScoped<PublishedBranchDeletionService>();
            };
            await h.InitializeAsync();
            return new World { H = h, Clock = clock, Settings = settings, Runner = runner };
        }

        /// <summary>A real rebase land: master moves first, so <c>VerifiedSourceSha != SourceRemoteSha</c>.</summary>
        public static async Task<World> LandedAsync(bool flag = true, bool dryRun = false, string? runnerId = null,
            IReadOnlyList<string>? enabledRunners = null)
        {
            var w = await CreateAsync(flag, dryRun, enabledRunners: enabledRunners);
            await w.H.AddSourceAsync();
            await w.H.Fixture.PushIndependentAsync("stranger");
            await w.H.Fixture.RequiredAsync(w.H.Fixture.Source, "push", "origin", w.H.Fixture.SourceRef);
            await w.H.RunAsync();
            var op = await w.H.OperationAsync();
            op.ShouldNotBeNull();
            new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("fixture land must be a confirmed publication");
            if (runnerId is not null)
            {
                await using var db = w.H.CreateContext();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == w.H.Fixture.TaskId);
                task.RunnerId = runnerId;
                await db.SaveChangesAsync();
            }
            w.H.Fixture.Git.Commands.Clear();
            return w;
        }

        public async Task<PublishedBranchDeletionRunResult> SweepAsync(CancellationToken ct)
        {
            await using var scope = H.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<PublishedBranchDeletionService>().RunAsync(ct);
        }

        public List<string[]> DeletePushes() => H.Fixture.Git.Commands
            .Where(c => c.Arguments.Length > 0 && c.Arguments[0] == "push" && c.Arguments[^1] == ":" + H.Fixture.SourceRef)
            .Select(c => c.Arguments).ToList();

        private LandingGitFixture.FixtureGit Reader() => new(Path.Combine(H.Fixture.Root, "home"), H.Fixture.TaskId);

        public async Task<int> RemoteSourceExitAsync() =>
            (await Reader().RunAsync(H.Fixture.Observer, ["ls-remote", "--exit-code", H.Fixture.Remote, H.Fixture.SourceRef],
                CancellationToken.None)).ExitCode;

        public async Task<string?> RemoteSourceShaAsync()
        {
            var read = await Reader().RunAsync(H.Fixture.Remote, ["rev-parse", "--verify", "-q", H.Fixture.SourceRef + "^{commit}"],
                CancellationToken.None);
            return read.Succeeded ? read.Output.Trim() : null;
        }

        public async Task<string?> LocalRefAsync(string fullRef)
        {
            var read = await Reader().RunAsync(H.Fixture.Repository, ["rev-parse", "--verify", "-q", fullRef], CancellationToken.None);
            return read.Succeeded ? read.Output.Trim() : null;
        }

        public async Task<List<string>> LandPinsAsync(AgentTaskLanding op)
        {
            var read = await Reader().RunAsync(H.Fixture.Repository,
                ["for-each-ref", "--format=%(refname) %(objectname)", op.RecoveryRefPrefix + "/"], CancellationToken.None);
            read.Succeeded.ShouldBeTrue();
            return [.. read.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];
        }

        public async Task EnsureTrackingRefAsync(string sha)
        {
            var tracking = "refs/remotes/origin/" + ShortBranch;
            if (await LocalRefAsync(tracking) != sha)
                await H.Fixture.RequiredAsync(H.Fixture.Repository, "update-ref", tracking, sha);
        }

        /// <summary>G-24: the desktop branch checked out in a worktree must survive the sweep.</summary>
        public async Task CheckOutKeeperAsync(string sha)
        {
            var keeper = Path.Combine(H.Fixture.Root, "trees", "keeper");
            if (await LocalRefAsync(H.Fixture.SourceRef) is null)
                await H.Fixture.RequiredAsync(H.Fixture.Repository, "worktree", "add", "-b", ShortBranch, keeper, sha);
            else
                await H.Fixture.RequiredAsync(H.Fixture.Repository, "worktree", "add", keeper, ShortBranch);
        }

        /// <summary>An independent clone pushes one new commit onto the task branch; returns its SHA.</summary>
        public async Task<string> PushToSourceAsync(string name)
        {
            var reader = Reader();
            async Task<string> Run(params string[] args)
            {
                var result = await reader.RunAsync(H.Fixture.Observer, args, CancellationToken.None);
                result.Succeeded.ShouldBeTrue(string.Join(' ', args) + ": " + result.Diagnostic);
                return result.Output.Trim();
            }
            await Run("fetch", "origin", H.Fixture.SourceRef);
            await Run("checkout", "-B", "c0824-mover", "FETCH_HEAD");
            await File.WriteAllTextAsync(Path.Combine(H.Fixture.Observer, name + ".txt"), name + "\n");
            await Run("add", "--", name + ".txt");
            await Run("commit", "-m", name);
            await Run("push", "origin", "HEAD:" + H.Fixture.SourceRef);
            return await Run("rev-parse", "HEAD");
        }

        public async Task<List<AgentTaskEvent>> EventsAsync()
        {
            await using var db = H.CreateContext();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == H.Fixture.TaskId && e.Type == AgentTaskEventType.LandingBranchDeletion).ToListAsync();
        }

        public ValueTask DisposeAsync() => H.DisposeAsync();
    }
}
