using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0657 S2. The real reply service settling a runner-bound Worktree task: the owning prompt,
// the delegate's report and a TurnEnd go in, and the desktop checkout is synchronized to the
// task's own pushed commit BEFORE anything attributes progress, resolves files or counts commits.
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerTaskSettlementTests
{
    [Test]
    public async Task Unpushed_runner_commit_is_salvaged_before_completion()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(mirrorPublish: true);
        await world.Git.EnsureRunnerAsync();
        File.WriteAllText(Path.Combine(world.Git.Runner, "salvaged.txt"), "work");
        await world.Git.RunAsync(world.Git.Runner, "add", "salvaged.txt");
        await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "salvaged");
        var tip = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");

        await world.SettleAsync(RunnerSettlementWorld.Report("Implemented in runner mirror."));

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.RemoteSync!.MirrorSha.ShouldBe(tip);
        world.Evidence()!.RemoteSync!.MirrorPushed.ShouldBeTrue();
        (await world.Git.HeadAsync()).ShouldBe(tip);
        (await world.NoteAsync())!.Body.ShouldContain(tip);
    }

    [Test]
    public async Task Rebased_runner_mirror_blocks_with_retained_tip_and_recovery()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(mirrorPublish: true);
        await world.Git.EnsureRunnerAsync();
        await world.Git.RunAsync(world.Git.Runner, "checkout", "--orphan", "rebased-root");
        File.WriteAllText(Path.Combine(world.Git.Runner, "rebased.txt"), "rebased");
        await world.Git.RunAsync(world.Git.Runner, "add", "rebased.txt");
        await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "rebased root");
        var tip = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
        await world.Git.RunAsync(world.Git.Runner, "checkout", world.Git.Branch);
        await world.Git.RunAsync(world.Git.Runner, "reset", "--hard", tip);

        await world.SettleAsync(RunnerSettlementWorld.Report("Committed after rebase."));

        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.MirrorDiverged);
        world.Evidence()!.RemoteSync!.MirrorSha.ShouldBe(tip);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        Directory.Exists(world.Git.Runner).ShouldBeTrue();
        var note = (await world.NoteAsync())!.Body;
        note.ShouldContain(world.Git.Task.RemoteWorktreePath!);
        note.ShouldContain(tip);
        note.ShouldContain(world.Git.Baseline);
        note.ShouldContain("-StartRef");
    }

    [Test]
    public async Task Force_pushed_origin_tip_still_blocks_with_reviewed_source_recovery()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        await world.Git.EnsureRunnerAsync();
        await world.Git.RunAsync(world.Git.Runner, "checkout", "--orphan", "force-root");
        File.WriteAllText(Path.Combine(world.Git.Runner, "force.txt"), "unrelated root");
        await world.Git.RunAsync(world.Git.Runner, "add", "force.txt");
        await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "force root");
        var tip = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
        await world.Git.RunAsync(world.Git.Runner, "checkout", world.Git.Branch);
        await world.Git.RunAsync(world.Git.Runner, "reset", "--hard", tip);
        await world.Git.RunAsync(world.Git.Runner, "push", "--force", "origin", "HEAD:" + world.Git.FullRef);

        await world.SettleAsync(RunnerSettlementWorld.Report("Pushed a rewritten branch."));

        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.Diverged);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        var note = (await world.NoteAsync())!.Body;
        note.ShouldContain("-RecoverReviewedSource");
        note.ShouldContain(tip);
    }

    [Test]
    public async Task Runner_push_settles_success_without_claim()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");

        await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        var evidence = world.Evidence()!;
        evidence.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var primary = evidence.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved);
        primary.Origin.ShouldBe(ProgressOrigin.Primary);
        primary.VerifiedSha.ShouldBe(s);
        evidence.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        evidence.RemoteSync.ConfirmedSha.ShouldBe(s);
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        (await world.Git.HeadAsync()).ShouldBe(s);
        world.Task.ProgressBaselineJson.ShouldBe(world.Git.Task.ProgressBaselineJson);
        world.Task.WorktreeBaseSha.ShouldBe(world.Git.Baseline);

        var note = await world.NoteAsync();
        note.ShouldNotBeNull();
        note!.Body.ShouldContain(s);
        note.Body.ShouldContain(DelegationGitFacts.FormatHeader(1, 1));
        (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("runner_sync", StringComparison.Ordinal));
    }

    [Test]
    public async Task No_push_settles_with_specific_reason()
    {
        // The branch is on origin, still at B: nothing new was pushed.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.SettleAsync(RunnerSettlementWorld.Report("Done, all pushed."));
            AssertNoPush(world, RemoteSettlementSyncReasons.NoPushedProgress, "Done, all pushed.");
            world.Task.FailureReason!.ShouldContain(RemoteSettlementSyncReasons.MirrorUnavailable);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
            (await world.EventsAsync()).ShouldNotContain(e => e.Detail.Contains("progress=none"));
        }

        // The exact branch never reached origin at all.
        await using (var world = await RunnerSettlementWorld.CreateAsync(pushBranch: false))
        {
            await world.SettleAsync(RunnerSettlementWorld.Report("Done."));
            AssertNoPush(world, RemoteSettlementSyncReasons.BranchNotPushed, "Done.");
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
            (await world.EventsAsync()).ShouldNotContain(e => e.Detail.Contains("progress=none"));
        }

        // The report claims C, but only an earlier S reached origin.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            var s = await world.Git.RunnerPushAsync("work.txt", "pushed");
            File.WriteAllText(Path.Combine(world.Git.Runner, "later.txt"), "unpushed");
            await world.Git.RunAsync(world.Git.Runner, "add", "later.txt");
            await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "unpushed");
            var c = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");

            await world.SettleAsync(RunnerSettlementWorld.Report("Done.\n" + world.Claim(c)));

            AssertNoPush(world, RemoteSettlementSyncReasons.ReportedCommitNotPushed, "Done.");
            world.Evidence()!.ClaimedSha.ShouldBe(c);
            world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
            (await world.Git.HasObjectAsync(c)).ShouldBeFalse();
        }
    }

    [Test]
    public async Task No_push_on_a_plan_role_settles_succeeded_with_a_visible_warning()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(AgentTaskRole.Plan);
        await world.SettleAsync(RunnerSettlementWorld.Report("Plan complete.", next: "code"));
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        var warnings = (await world.EventsAsync()).Where(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("progress=none", StringComparison.Ordinal)).ToArray();
        warnings.Length.ShouldBe(1);
        warnings[0].Detail.ShouldContain(RemoteSettlementSyncReasons.NoPushedProgress);
        warnings[0].Detail.ShouldContain(world.Git.FullRef);
        warnings[0].Detail.ShouldContain(world.Git.Baseline);
        var note = (await world.NoteAsync())!.Body;
        note.ShouldContain(warnings[0].Detail);
        note.IndexOf("progress=none", StringComparison.Ordinal).ShouldBeLessThan(
            note.IndexOf("Plan complete.", StringComparison.Ordinal));
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
    }

    [Test]
    [Arguments(AgentTaskRole.Investigate)]
    [Arguments(AgentTaskRole.TestDesign)]
    [Arguments(AgentTaskRole.Review)]
    [Arguments(AgentTaskRole.Custom)]
    public async Task C788_NonCodeNoPushRoleMatrix(AgentTaskRole role)
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(role);
        Guid? subject = role == AgentTaskRole.Review ? await world.AddReviewSubjectAsync(world.Git.Baseline) : null;
        var report = subject is { } id
            ? "No branch change.\n" + DelegationReportFormatter.FindingToken(world.TaskId, "clean")
                + $"\n--- review evidence ---\nsubjectTaskId: {id:D}\nreviewedSourceSha: {world.Git.Baseline}\n"
                + "reviewedSourceClean: true\nordinaryScopeCompleted: Full\n\n"
            : "No branch change.";
        await world.SettleAsync(RunnerSettlementWorld.Report(report));
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        var warning = (await world.EventsAsync()).Where(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("progress=none", StringComparison.Ordinal)).ToArray();
        warning.Length.ShouldBe(1);
        warning[0].Detail.ShouldContain(world.Git.FullRef);
        (await world.NoteAsync())!.Body.ShouldContain(warning[0].Detail);
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        if (subject is not null)
        {
            await using var db = world.CreateContext();
            var row = await db.StageOutcomes.SingleAsync(o => o.StageTaskId == world.TaskId);
            row.SubjectTaskId.ShouldBe(subject);
            row.ReviewedSourceSha.ShouldBe(world.Git.Baseline);
            row.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full);
        }
    }

    [Test]
    public async Task C788_ClaimWarningDoesNotDuplicateNoPush()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(AgentTaskRole.Plan);
        await world.SettleAsync(RunnerSettlementWorld.Report("No push.\n[antiphon-progress:foreign deadbee]"));
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.ClaimWarning.ShouldNotBeNull();
        var warnings = (await world.EventsAsync()).Where(e => e.Type == AgentTaskEventType.Warning).ToArray();
        warnings.ShouldContain(e => e.Detail.Contains("progress=unavailable", StringComparison.Ordinal));
        warnings.ShouldNotContain(e => e.Detail.Contains("progress=none", StringComparison.Ordinal));
        (await world.NoteAsync())!.Body.ShouldNotContain("progress=none");
    }

    [Test]
    [Arguments(WorkspaceMode.ReadOnly)]
    [Arguments(WorkspaceMode.Shared)]
    public async Task C788_NonWorktreeDoesNotGetNoPushWarning(WorkspaceMode workspace)
    {
        await using var world = await C544World.CreateAsync();
        var created = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "Write a plan.", Title: "CARD-0788 local plan", Role: AgentTaskRole.Plan,
            Workspace: workspace, WorkingDirectory: world.RepositoryPath,
            Card: world.Card.Id.ToString("D")));
        var session = await world.DispatchAsync(created.Id);
        await using (var seed = world.CreateContext())
        {
            var task = await seed.AgentTasks.SingleAsync(t => t.Id == created.Id);
            task.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(new(
                1, CompletionProgressAssessment.NoAttributedProgress, Reason: "no_movement"));
            await seed.SaveChangesAsync();
        }
        await world.SeedTurnAsync(session, created.Id,
            "Plan complete.\n\n--- next stage ---\nnext: code\nhandoff: Implement the plan.\n"
            + DelegationReportFormatter.ReportToken(created.Id, "done"));
        await world.Services.GetRequiredService<AgentTaskReplyService>()
            .OnTurnEndAsync(session, CancellationToken.None);
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == created.Id)
            .ToArrayAsync()).ShouldNotContain(e => e.Detail.Contains("progress=none", StringComparison.Ordinal));
        var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.TaskId == created.Id
            && n.Kind == LandNotificationKind.TaskCompletion);
        if (note is not null)
            TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson)!.NoteHeader
                .ShouldNotContain("progress=none");
    }

    [Test]
    public async Task Unmarked_completion_gets_the_code_progress_policy()
    {
        // (a) An unmarked report claims C, but only an earlier S reached origin.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            var s = await world.Git.RunnerPushAsync("work.txt", "pushed");
            File.WriteAllText(Path.Combine(world.Git.Runner, "later.txt"), "unpushed");
            await world.Git.RunAsync(world.Git.Runner, "add", "later.txt");
            await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "unpushed");
            var c = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
            await world.EndDelegateSessionAsync();

            await world.SettleAsync(RunnerSettlementWorld.Report("Done.\n" + world.Claim(c)), closingVerdict: false);

            AssertNoPush(world, RemoteSettlementSyncReasons.ReportedCommitNotPushed, "Done.");
            world.Task.ReportEvidence.ShouldBe(AgentTaskReportEvidence.UnmarkedAfterNudge);
            world.Evidence()!.ClaimedSha.ShouldBe(c);
            world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Completed);
            (await world.EventsAsync()).ShouldNotContain(e => e.Detail.Contains("progress=none"));
            (await world.NoteAsync())!.Body.ShouldNotContain("progress=none");
        }

        // (b) An unmarked report after nothing new was pushed: the branch is still at B.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.EndDelegateSessionAsync();

            await world.SettleAsync(RunnerSettlementWorld.Report("Done, all pushed."), closingVerdict: false);

            AssertNoPush(world, RemoteSettlementSyncReasons.NoPushedProgress, "Done, all pushed.");
            world.Task.ReportEvidence.ShouldBe(AgentTaskReportEvidence.UnmarkedAfterNudge);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Completed);
            (await world.EventsAsync()).ShouldNotContain(e => e.Detail.Contains("progress=none"));
            (await world.NoteAsync())!.Body.ShouldNotContain("progress=none");
        }

        // (c) The same fallback publishes an unpushed runner tip before judging progress.
        await using (var world = await RunnerSettlementWorld.CreateAsync(mirrorPublish: true))
        {
            await world.Git.EnsureRunnerAsync();
            File.WriteAllText(Path.Combine(world.Git.Runner, "fallback.txt"), "runner work");
            await world.Git.RunAsync(world.Git.Runner, "add", "fallback.txt");
            await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "fallback work");
            var tip = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
            await world.EndDelegateSessionAsync();

            await world.SettleAsync(RunnerSettlementWorld.Report("Done in runner mirror."), closingVerdict: false);

            world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
            world.Task.ReportEvidence.ShouldBe(AgentTaskReportEvidence.UnmarkedAfterNudge);
            world.Evidence()!.RemoteSync!.MirrorPushed.ShouldBeTrue();
            world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(tip);
            (await world.Git.HeadAsync()).ShouldBe(tip);
        }
    }

    [Test]
    public async Task Bind_refusal_recovery_blocks_an_unconfirmed_runner_branch()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var older = await world.Git.RunnerPushAsync("work.txt", "runner work");
        var s = await world.Git.RunnerPushAsync("more.txt", "later runner work");
        world.Git.Git.Clear();
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();

        // The held report names a commit in full, but origin's tip is a later one: the named commit
        // is not the pushed tip, so nothing correlates the tip with this task's work.
        await world.RecoverThroughWatchdogAsync($"Implemented and pushed {older}.");

        // Blocked with a repair handoff, not Succeeded, and the checkout was never fast-forwarded.
        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Task.NextHandoff!.ShouldContain("fresh completion report");
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.TipNotReported);
        world.Evidence()!.RemoteSync!.ObservedSha.ShouldBe(s);
        world.Git.Git.Commands.ShouldNotContain(x => IsMergeCommand(x));
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged
            || e.Type == AgentTaskEventType.Completed);
        world.Services.GetRequiredService<RecordingSessionStopper>().Killed.ShouldBeEmpty();
        Directory.Exists(world.Git.Worktree).ShouldBeTrue();
        world.Task.WorktreePath.ShouldBe(world.Git.Task.WorktreePath);

        // Downstream: the caller is told to decide, never to review, and land refuses the branch.
        var note = await world.NoteAsync();
        note.ShouldNotBeNull();
        note!.Body.ShouldContain("next=decide");
        note.Body.ShouldNotContain("next=review");
        await using (var scope = world.Services.CreateAsyncScope())
        {
            await Should.ThrowAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<AgentTaskLandService>()
                .RequestAsync(world.TaskId, new LandAgentTaskRequest(), CancellationToken.None));
        }

        // Only a fresh, correlated report confirms S.
        await replies.AnswerAsync(world.TaskId, "Report the pushed commit.", AnswerOrigin.Cli, null, CancellationToken.None);
        await world.ReloadAsync();
        world.Task.Status.ShouldBe(AgentTaskStatus.Working);
        await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."),
            DelegationReportFormatter.TaskMarker(world.TaskId) + "\n\nReport the pushed commit.");

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
    }

    [Test]
    public async Task Bind_refusal_recovery_confirms_the_pushed_tip_its_held_report_names()
    {
        // D1: the push exists only in the runner's clone and the bare origin, and the full SHA only
        // in the delegate's own done report as the server received it. The desktop checkout is at
        // the dispatch base and the desktop's Claude projects root holds no runner transcript, so
        // neither of the older arms can see the work.
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
        (await world.Git.HasObjectAsync(s)).ShouldBeFalse();
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);

        await world.RecoverThroughWatchdogAsync($"Implemented and pushed {s} to {world.Git.Branch}.");

        // Settled like a correlated report: the own branch's tip is the named commit and descends from B.
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldNotBe(PipelineHandoffKind.Decide);
        var progress = world.Evidence().ShouldNotBeNull();
        progress.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        progress.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
            .VerifiedSha.ShouldBe(s);
        progress.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        progress.RemoteSync.ConfirmedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
        world.Task.ProgressBaselineJson.ShouldBe(world.Git.Task.ProgressBaselineJson);
        var events = await world.EventsAsync();
        events.ShouldContain(e => e.Type == AgentTaskEventType.Merged && e.Detail.Contains(s, StringComparison.Ordinal));
        events.ShouldNotContain(e => e.Type == AgentTaskEventType.Blocked || e.Type == AgentTaskEventType.Failed);
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        world.Services.GetRequiredService<RecordingSessionStopper>().Killed.ShouldBeEmpty();
        var note = await world.NoteAsync();
        note.ShouldNotBeNull();
        note!.Body.ShouldContain(s);
        note.Body.ShouldNotContain("next=decide");
    }

    [Test]
    public async Task Bind_refusal_recovery_names_only_full_commit_ids()
    {
        // The held report abbreviates the pushed tip (as `git log --oneline` prints it). Only a full
        // object id names a commit: the tip stays unconfirmed and the checkout untouched.
        foreach (var length in new[] { 7, 12 })
        {
            await using var world = await RunnerSettlementWorld.CreateAsync();
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
            world.Git.Git.Clear();

            await world.RecoverThroughWatchdogAsync($"Implemented and pushed {s[..length]}.");

            var row = "abbreviated to " + length;
            world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, row + ": " + Why(world));
            world.Task.FailureCode.ShouldBeNull(row);
            world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide, row);
            world.Git.Git.Commands.ShouldNotContain(x => IsMergeCommand(x), row);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline, row);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged, row);
        }
    }

    [Test]
    public async Task Bind_refusal_recovery_stays_blocked_unless_the_named_tip_descends()
    {
        // The named commit IS origin's tip, but it does not descend from the dispatch base.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.Git.EnsureRunnerAsync();
            await world.Git.RunAsync(world.Git.Runner, "checkout", "--orphan", "stray");
            File.WriteAllText(Path.Combine(world.Git.Runner, "stray.txt"), "unrelated history");
            await world.Git.RunAsync(world.Git.Runner, "add", "stray.txt");
            await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "stray");
            var stray = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
            await world.Git.RunAsync(world.Git.Runner, "push", "--force", "origin", "HEAD:" + world.Git.FullRef);

            await world.RecoverThroughWatchdogAsync($"Pushed {stray}.");

            world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
            world.Task.FailureCode.ShouldBeNull();
            world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
            world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.Diverged);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
        }

        // The held report is done but names no commit: nothing to confirm, and nothing is fetched.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
            world.Git.Git.Clear();

            await world.RecoverThroughWatchdogAsync("All done and pushed.");

            world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
            world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
            AssertNoSync(world, s);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        }
    }

    [Test]
    public async Task Bind_refusal_recovery_commits_the_completion_obligation()
    {
        // Blocked (nothing named) and Succeeded (the named tip confirmed) recoveries of a profile-v1
        // task both owe the caller the durable CARD-0544 D-9 completion, committed with the settlement.
        foreach (var confirm in new[] { false, true })
        {
            var row = confirm ? "confirmed" : "blocked";
            await using var world = await RunnerSettlementWorld.CreateAsync(profiled: true);
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");

            await world.RecoverThroughWatchdogAsync(confirm ? $"Implemented and pushed {s}." : "Implemented and pushed.");

            world.Task.Status.ShouldBe(confirm ? AgentTaskStatus.Succeeded : AgentTaskStatus.Blocked, row + ": " + Why(world));
            var settlement = (await world.EventsAsync()).Single(e =>
                e.Type == (confirm ? AgentTaskEventType.Completed : AgentTaskEventType.Blocked)
                && e.Detail == world.Task.Result);
            await AssertCompletionObligationAsync(world, settlement, row, decide: !confirm);
        }
    }

    [Test]
    public async Task Runner_sync_block_commits_the_completion_obligation()
    {
        // The ordinary report path: the lease stays busy for the whole sync budget, so the runner
        // sync blocks. A profile-v1 task still owes the caller its durable decide completion.
        await using var world = await RunnerSettlementWorld.CreateAsync(profiled: true, controlledSyncClock: true);
        await world.Git.RunnerPushAsync("work.txt", "runner work");

        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var settle = world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
            (await System.Threading.Tasks.Task.WhenAny(world.LeaseBusy.First, settle)).ShouldBe(world.LeaseBusy.First,
                "the settlement sync must wait for a busy lease within its budget");
            world.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
            await settle;
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        var settlement = (await world.EventsAsync()).Single(e => e.Type == AgentTaskEventType.Blocked);
        await AssertCompletionObligationAsync(world, settlement, "runner sync block", decide: true);
        await AssertNoDebtAsync(world);
    }

    private static async Task AssertCompletionObligationAsync(
        RunnerSettlementWorld world, AgentTaskEvent settlement, string row, bool decide)
    {
        var obligation = (await world.ObligationsAsync()).ShouldHaveSingleItem(row);
        obligation.SourceEventId.ShouldBe(settlement.Id, row);
        obligation.ParentSessionId.ShouldBe(world.CallerSessionId, row);
        var snapshot = TaskCompletionNotification.TryReadSnapshot(obligation.CompletionSnapshotJson).ShouldNotBeNull(row);
        snapshot.TaskId.ShouldBe(world.TaskId, row);
        snapshot.SourceEventId.ShouldBe(settlement.Id, row);
        snapshot.Status.ShouldBe(world.Task.Status, row);
        snapshot.RawResult.ShouldBe(world.Task.Result, row);
        snapshot.ProfileVersion.ShouldBe(1, row);
        snapshot.Round.ShouldBe(VerificationRound.Final, row);
        snapshot.NoteHeader.ShouldContain("verification=Final", Case.Sensitive, row);
        if (decide)
        {
            snapshot.NextStage.ShouldBe("decide", row);
            snapshot.NoteHeader.ShouldContain("next=decide", Case.Sensitive, row);
        }
        else
            snapshot.NoteHeader.ShouldNotContain("next=decide", Case.Sensitive, row);
        obligation.Body.ShouldStartWith(snapshot.NoteHeader, Case.Sensitive, row);

        // Delivered through the outbox, not the legacy direct note.
        obligation.State.ShouldBe(LandNotificationState.AwaitingReceipt, row);
        var queued = (await world.QueuedForAsync(obligation.Id)).ShouldNotBeNull(row);
        queued.AgentSessionId.ShouldBe(world.CallerSessionId, row);
        queued.Body.ShouldBe(obligation.Body, row);
        obligation.QueueMessageId.ShouldBe(queued.Id, row);
    }

    [Test]
    public async Task Settlement_sync_fences_a_competing_retirement()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(fenced: true);
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
        var journal = new WorkspaceReservationJournal(
            world.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        // Retirement's own coordinates: worktree path, source full ref, repository path.
        var retirement = new WorkspaceReservationCommand(
            WorkspaceReservationKey.For(world.Git.Task.WorktreePath, world.Git.FullRef, world.Git.Task.RepoPath),
            WorkspaceReservationKind.Retirement, world.TaskId, RetirementId: Guid.NewGuid());
        world.Git.Git.BlockOn = args => args.Contains("--ff-only");

        // Settlement's sync holds the consumer slot while the fast-forward is in flight.
        var settle = world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
        (await System.Threading.Tasks.Task.WhenAny(world.Git.Git.Entered, settle)).ShouldBe(world.Git.Git.Entered);
        var during = await journal.TryClaimRetirementAsync(retirement, CancellationToken.None);
        world.Git.Git.OpenGate();
        await settle;

        during.Accepted.ShouldBeFalse();
        during.Reason.ShouldBe("workspace_in_use");
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
        world.Git.Git.Blocked.ShouldBe(1);
    }

    [Test]
    public async Task Sync_uncertainty_blocks_and_reply_retries()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true);
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");

        // The lease stays busy for the sync's whole budget (waited on inside it, then given up).
        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var settle = world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
            (await System.Threading.Tasks.Task.WhenAny(world.LeaseBusy.First, settle)).ShouldBe(world.LeaseBusy.First);
            world.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
            await settle;
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Task.Result!.ShouldContain("Implemented and pushed.");
        // CARD-1082 D-2. Classify records Pending before the progress read. The objects were not
        // local, so the Code read stays indeterminate and the verdict stays Blocked (R-2).
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Pending);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBeNull();
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        await AssertNoDebtAsync(world);
        (await world.EventsAsync()).ShouldContain(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains(RemoteSettlementSyncReasons.LeaseBusy, StringComparison.Ordinal));
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);

        // The existing reply path: a new prompt watermark, never a replay of the old done token.
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
        await replies.AnswerAsync(world.TaskId, "The lease is free again; report once more.",
            AnswerOrigin.Cli, null, CancellationToken.None);
        await replies.OnTurnEndAsync(world.SessionId, CancellationToken.None);
        await world.ReloadAsync();
        world.Task.Status.ShouldBe(AgentTaskStatus.Working);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);

        await world.SettleAsync(RunnerSettlementWorld.Report("Reported again."),
            DelegationReportFormatter.TaskMarker(world.TaskId) + "\n\nThe lease is free again; report once more.");

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        world.Evidence()!.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
            .VerifiedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
    }

    [Test]
    public async Task Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements()
    {
        // The real deferred-report sweep re-hands the runner task's marked report while the lease
        // stays busy across several sweeps. Each attempt waits one slice, not the whole budget, so
        // the same sweep settles the other task's report; the runner task stays open (not Blocked)
        // until the cumulative wait reaches Delegation:RunnerSyncBudgetSeconds, then blocks lease-busy.
        await using var world = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true);
        await world.Git.RunnerPushAsync("work.txt", "runner work");
        await world.SeedReportAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
        var other = await world.AddLocalReportedTaskAsync(RunnerSettlementWorld.Report("Investigated.", next: "none"));
        new DelegationSettings().RunnerSyncBudgetSeconds.ShouldBe(120);
        RemoteWorkspaceService.DefaultLeaseWaitSlice.ShouldBe(TimeSpan.FromSeconds(10));
        static TimeSpan Seconds(int n) => TimeSpan.FromSeconds(n);

        async System.Threading.Tasks.Task SweepAsync(TimeSpan wait, string row)
        {
            var busy = world.LeaseBusy.Next();
            var sweep = world.SweepDeferredReportsAsync();
            (await System.Threading.Tasks.Task.WhenAny(busy, sweep)).ShouldBe(busy,
                row + ": the sweep must re-hand the runner report into the busy lease");
            world.SyncClock!.Advance(wait);
            await sweep.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard);
        }

        async System.Threading.Tasks.Task StillOpenAsync(string row)
        {
            world.Task.Status.ShouldBe(AgentTaskStatus.Working, row + " " + Why(world));
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Blocked, row);
        }

        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();

            // Cumulative wait since the lease was first seen busy, in brackets.
            await SweepAsync(Seconds(10), "sweep 1 [10 s]");
            (await world.StatusOfAsync(other)).ShouldBe(AgentTaskStatus.Succeeded,
                "the other report settles in the same sweep");
            await StillOpenAsync("sweep 1");
            world.SyncClock!.Advance(Seconds(50));
            await SweepAsync(Seconds(10), "sweep 2 [70 s]");
            await StillOpenAsync("sweep 2");
            world.SyncClock.Advance(Seconds(35));
            await SweepAsync(Seconds(10), "sweep 3 [115 s]");
            await StillOpenAsync("sweep 3: the budget is not yet spent");
            await SweepAsync(Seconds(5), "sweep 4 [120 s]");
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Task.Result!.ShouldContain("Implemented and pushed.");
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        (await world.EventsAsync()).Count(e => e.Type == AgentTaskEventType.Blocked).ShouldBe(1);
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        await AssertNoDebtAsync(world);
    }

    [Test]
    public async Task Refused_sync_never_autosaves_or_releases_workspace()
    {
        // Dirt on the desktop checkout.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.Git.RunnerPushAsync("work.txt", "runner work");
            File.WriteAllText(Path.Combine(world.Git.Worktree, "scratch.txt"), "desktop dirt");

            await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));

            await AssertRefusedAsync(world, RemoteSettlementSyncReasons.Dirty);
            File.ReadAllText(Path.Combine(world.Git.Worktree, "scratch.txt")).ShouldBe("desktop dirt");
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        }

        // A desktop commit the runner never saw: divergent histories.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.Git.RunnerPushAsync("work.txt", "runner work");
            var local = await world.Git.DesktopCommitAsync("desktop.txt", "desktop only");

            await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));

            await AssertRefusedAsync(world, RemoteSettlementSyncReasons.Diverged);
            (await world.Git.HeadAsync()).ShouldBe(local);
        }
    }

    [Test]
    public async Task Uncorrelated_and_nonfinal_turns_never_fetch()
    {
        foreach (var verdict in new[] { "blocked", "failed" })
        {
            await using var world = await RunnerSettlementWorld.CreateAsync();
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
            world.Git.Git.Clear();

            await world.SettleAsync("I stopped here.\n" + DelegationReportFormatter.ReportToken(world.TaskId, verdict));

            world.Task.Status.ShouldBe(verdict == "blocked" ? AgentTaskStatus.Blocked : AgentTaskStatus.Failed);
            world.Task.FailureCode.ShouldBeNull();
            AssertNoSync(world, s);
            (await world.Git.HasObjectAsync(s)).ShouldBeFalse();
        }

        // A foreign prompt's report, then an unmarked answer to the owning prompt (nudged, not final).
        {
            await using var world = await RunnerSettlementWorld.CreateAsync();
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
            world.Git.Git.Clear();

            await world.SettleAsync(RunnerSettlementWorld.Report("Someone else's work."),
                DelegationReportFormatter.TaskMarker(Guid.NewGuid()));
            world.Task.Status.ShouldBe(AgentTaskStatus.Working);
            AssertNoSync(world, s);

            await TurnSeeding.SeedTurnAsync(world.CreateContext, world.SessionId,
                DelegationReportFormatter.TaskMarker(world.TaskId), "Still working on it.", closingVerdict: false);
            await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(world.SessionId, CancellationToken.None);
            await world.ReloadAsync();
            world.Task.Status.ShouldBe(AgentTaskStatus.Working);
            AssertNoSync(world, s);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        }
    }

    [Test]
    public async Task Artifact_and_git_summary_use_fetched_commit()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(AgentTaskRole.Plan);
        await world.Git.EnsureRunnerAsync();
        Directory.CreateDirectory(Path.Combine(world.Git.Runner, "docs"));
        var s = await world.Git.RunnerPushAsync(Path.Combine("docs", "plan.md"), "the plan\n");
        // A stale same-named file in the main checkout must not stand in for the pushed one.
        Directory.CreateDirectory(Path.Combine(world.Git.Desktop, "docs"));
        File.WriteAllText(Path.Combine(world.Git.Desktop, "docs", "plan.md"), "stale\n");

        await world.SettleAsync(RunnerSettlementWorld.Report("Plan written.", next: "code", artifact: "docs/plan.md"));

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Task.DeliverablePath.ShouldBe("docs/plan.md");
        world.Task.DeliverableRef.ShouldBe(s);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
        var note = await world.NoteAsync();
        note!.Body.ShouldContain(DelegationGitFacts.FormatHeader(1, 1));
    }

    /// <summary>
    /// CARD-1082 V-20 / G-11. A Review whose only sync failure is a spent desktop lease settles
    /// Succeeded on the report. A cut at settlement-before-commit rolls the debt back with the
    /// verdict; the same transcript then settles once.
    /// </summary>
    [Test]
    public async Task C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(
            AgentTaskRole.Review, profiled: true, controlledSyncClock: true);
        var subject = await world.AddReviewSubjectAsync(world.Git.Baseline);
        var counter = new StatementCounter();
        world.Interceptors.Add(counter);
        world.ConfigureServices = s => s.AddSingleton<LandDeliveryBoundary>(new ThrowBoundary("settlement-before-commit"));
        await world.RestartServicesAsync();
        var report = ReviewReport(world, subject);

        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var settle = world.SettleAsync(report);
            await FinishBusySettleAsync(world, settle, world.LeaseBusy.First);
            world.Task.Status.ShouldBe(AgentTaskStatus.Working, Why(world));
            await AssertNoDebtAsync(world);
            await using (var db = world.CreateContext())
                (await db.StageOutcomes.CountAsync(o => o.StageTaskId == world.TaskId)).ShouldBe(0);

            world.ConfigureServices = null;
            await world.RestartServicesAsync();
            counter.Reset();
            var retryBusy = world.LeaseBusy.Next();
            var retry = world.Services.GetRequiredService<AgentTaskReplyService>()
                .OnTurnEndAsync(world.SessionId, CancellationToken.None);
            await FinishBusySettleAsync(world, retry, retryBusy);
            await world.ReloadAsync();
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        var debtSql = DebtStatements(counter);
        Console.WriteLine($"C1082-SQL path=pending-review statements={counter.Statements} debtSql={debtSql}");
        debtSql.ShouldBe(1);
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.None);
        (world.Task.NextHandoff ?? "").ShouldNotContain("Runner sync blocked");
        world.Task.LandRequestedAt.ShouldBeNull();
        var sync = world.Evidence()!.RemoteSync!;
        sync.State.ShouldBe(RemoteSettlementSyncState.Pending);
        sync.ObservedSha.ShouldBe(world.Git.Baseline);
        sync.ConfirmedSha.ShouldBeNull();
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        world.Git.Git.Commands.ShouldNotContain(x => IsMergeCommand(x));
        var pendingEvents = await world.EventsAsync();
        pendingEvents.ShouldNotContain(e => e.Type == AgentTaskEventType.Failed || e.Type == AgentTaskEventType.Merged);
        pendingEvents.ShouldNotContain(e => e.Detail.StartsWith("Merge-back failed", StringComparison.Ordinal));
        world.Services.GetRequiredService<RecordingSessionStopper>().Killed.ShouldBeEmpty();
        var warnings = (await world.EventsAsync()).Where(e => e.Type == AgentTaskEventType.Warning).Select(e => e.Detail).ToArray();
        warnings.ShouldContain(d => d.Contains("synced later", StringComparison.Ordinal));
        warnings.ShouldNotContain(d => d.Contains("then reply", StringComparison.Ordinal));
        var obligation = (await world.ObligationsAsync()).Single();
        obligation.Body.ShouldContain("desktop-sync=pending");
        obligation.Body.ShouldNotContain("next=decide");
        var settlement = (await world.EventsAsync()).Single(e => e.Id == obligation.SourceEventId);
        await AssertCompletionObligationAsync(world, settlement, "pending review", decide: false);
        await using (var db = world.CreateContext())
        {
            var row = await db.StageOutcomes.SingleAsync(o => o.StageTaskId == world.TaskId);
            row.SubjectTaskId.ShouldBe(subject);
            row.ReviewedSourceSha.ShouldBe(world.Git.Baseline);
            var debt = await db.AgentTaskSyncDebts.SingleAsync(d => d.TaskId == world.TaskId);
            debt.SettlementEventId.ShouldBe(obligation.SourceEventId);
            AssertPendingDebt(debt, world, world.Git.Baseline, settlement);
        }

        await using (var db = world.CreateContext())
        {
            var committed = await db.AgentTaskSyncDebts.AsNoTracking().SingleAsync(d => d.TaskId == world.TaskId);
            db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
            {
                Id = Guid.NewGuid(),
                TaskId = committed.TaskId,
                Attempt = committed.Attempt,
                SettlementEventId = committed.SettlementEventId,
                RunnerId = committed.RunnerId,
                WorktreePath = committed.WorktreePath,
                RemoteWorktreePath = committed.RemoteWorktreePath,
                RepositoryPath = committed.RepositoryPath,
                FullRef = committed.FullRef,
                BaselineSha = committed.BaselineSha,
                SourceSha = committed.SourceSha,
                DesktopBeforeSha = committed.DesktopBeforeSha,
                EndpointFingerprint = committed.EndpointFingerprint,
                State = committed.State,
                ReasonCode = committed.ReasonCode,
                Attempts = committed.Attempts,
                NextAttemptAt = committed.NextAttemptAt,
                Revision = committed.Revision,
                CreatedAt = committed.CreatedAt,
                UpdatedAt = committed.UpdatedAt,
            });
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
        }
        await using (var db = world.CreateContext())
            (await db.AgentTaskSyncDebts.CountAsync(d => d.TaskId == world.TaskId)).ShouldBe(1);
    }

    /// <summary>
    /// CARD-1082 V-21. Code whose pushed tip is already a local object settles Succeeded Pending.
    /// The desktop HEAD stays at the baseline and no merge runs.
    /// </summary>
    [Test]
    public async Task C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true);
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
        await world.Git.RunAsync(world.Git.Desktop, "fetch", "--no-tags", "origin", world.Git.FullRef);
        (await world.Git.HasObjectAsync(s)).ShouldBeTrue();
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        world.Git.Git.Clear();

        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var settle = world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
            await FinishBusySettleAsync(world, settle, world.LeaseBusy.First);
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Task.NextStage.ShouldNotBe(PipelineHandoffKind.Decide);
        world.Evidence()!.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        world.Evidence()!.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
            .VerifiedSha.ShouldBe(s);
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Pending);
        world.Evidence()!.RemoteSync!.ObservedSha.ShouldBe(s);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBeNull();
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        world.Git.Git.Commands.ShouldNotContain(x => IsMergeCommand(x));
        var pendingEvents = await world.EventsAsync();
        pendingEvents.ShouldNotContain(e => e.Type == AgentTaskEventType.Failed || e.Type == AgentTaskEventType.Merged);
        pendingEvents.ShouldNotContain(e => e.Detail.StartsWith("Merge-back failed", StringComparison.Ordinal));
        var note = (await world.NoteAsync())!.Body;
        note.ShouldContain($"source {s} (desktop-sync=pending)");
        note.ShouldContain(DelegationGitFacts.FormatHeader(1, 1));
        note.ShouldNotContain("then reply");
        await using var db = world.CreateContext();
        var debt = await db.AgentTaskSyncDebts.SingleAsync(d => d.TaskId == world.TaskId);
        var settlement = (await world.EventsAsync()).Single(e => e.Id == debt.SettlementEventId);
        settlement.Type.ShouldBe(AgentTaskEventType.Completed);
        AssertPendingDebt(debt, world, s, settlement);
    }

    /// <summary>
    /// CARD-1082 V-22. A Code report that pushed nothing fails on its own no-progress verdict.
    /// The lease still leaves a Pending debt for the observed baseline tip.
    /// </summary>
    [Test]
    public async Task C1082_CodeLeaseBusyEqualTipFailsOnItsOwnVerdict()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true);
        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var settle = world.SettleAsync(RunnerSettlementWorld.Report("Done, all pushed."));
            await FinishBusySettleAsync(world, settle, world.LeaseBusy.First);
        }

        AssertNoPush(world, RemoteSettlementSyncReasons.NoPushedProgress, "Done, all pushed.");
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Pending);
        world.Evidence()!.RemoteSync!.ObservedSha.ShouldBe(world.Git.Baseline);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBeNull();
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        world.Git.Git.Commands.ShouldNotContain(x => IsMergeCommand(x));
        Directory.Exists(world.Git.Worktree).ShouldBeTrue();
        var note = (await world.NoteAsync())!.Body;
        note.ShouldContain(RemoteSettlementSyncReasons.NoPushedProgress);
        note.ShouldContain("synced later");
        note.ShouldNotContain("then reply");
        await using var db = world.CreateContext();
        var debt = await db.AgentTaskSyncDebts.SingleAsync(d => d.TaskId == world.TaskId);
        var settlement = (await world.EventsAsync()).Single(e => e.Id == debt.SettlementEventId);
        settlement.Type.ShouldBe(AgentTaskEventType.Failed);
        AssertPendingDebt(debt, world, world.Git.Baseline, settlement);
    }

    /// <summary>
    /// CARD-1082 V-2. The kill switch keeps today's Blocked settlement. The synchronized settle
    /// above it is the no-debt statement count.
    /// </summary>
    [Test]
    public async Task C1082_DisabledSettingKeepsLeaseBusyBlocked()
    {
        await using (var synced = await RunnerSettlementWorld.CreateAsync())
        {
            var counter = new StatementCounter();
            synced.Interceptors.Add(counter);
            await synced.RestartServicesAsync();
            await synced.Git.RunnerPushAsync("work.txt", "runner work");
            counter.Reset();
            await synced.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
            var debtSql = DebtStatements(counter);
            Console.WriteLine($"C1082-SQL path=synchronized statements={counter.Statements} debtSql={debtSql}");
            synced.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(synced));
            synced.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
            debtSql.ShouldBe(0);
            await AssertNoDebtAsync(synced);
        }

        await using var world = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true, syncDebt: false);
        var blocked = new StatementCounter();
        world.Interceptors.Add(blocked);
        await world.RestartServicesAsync();
        await world.Git.RunnerPushAsync("work.txt", "runner work");
        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            blocked.Reset();
            var settle = world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
            await FinishBusySettleAsync(world, settle, world.LeaseBusy.First);
        }

        var blockedSql = DebtStatements(blocked);
        Console.WriteLine($"C1082-SQL path=kill-switch statements={blocked.Statements} debtSql={blockedSql}");
        blockedSql.ShouldBe(0);
        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Task.NextHandoff!.ShouldStartWith("Runner sync blocked");
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        (await world.EventsAsync()).ShouldContain(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains(RemoteSettlementSyncReasons.LeaseBusy, StringComparison.Ordinal)
            && e.Detail.Contains("then reply", StringComparison.Ordinal));
        // Today's blocked note carries the unavailable warning, not the pending-debt vocabulary.
        // "Runner sync blocked" is the handoff, asserted above.
        var note = (await world.NoteAsync())!.Body;
        note.ShouldContain("Runner sync unavailable");
        note.ShouldContain("then reply");
        note.ShouldNotContain("synced later");
        note.ShouldNotContain("desktop-sync=pending");
        await AssertNoDebtAsync(world);
    }

    /// <summary>
    /// CARD-1082 V-23. After the review settles Pending, the dispatcher sweep fast-forwards the
    /// recorded source and leaves the settlement evidence alone.
    /// </summary>
    [Test]
    public async Task C1082_PendingDebtRecoversThroughTheDispatcherSweep()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(
            AgentTaskRole.Review, profiled: true, controlledSyncClock: true);
        var subject = await world.AddReviewSubjectAsync(world.Git.Baseline);
        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var settle = world.SettleAsync(ReviewReport(world, subject));
            await FinishBusySettleAsync(world, settle, world.LeaseBusy.First);
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        var evidence = world.Task.CompletionProgressEvidenceJson;
        world.DebtClock.SetUtcNow(DateTimeOffset.UtcNow.AddMinutes(1));
        var swept = await world.SweepSettlementSyncAsync();
        swept.ShouldBe(1);
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded);
        world.Task.CompletionProgressEvidenceJson.ShouldBe(evidence);
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Pending);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBeNull();
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        (await world.ObligationsAsync()).Count.ShouldBe(1);
        await using var db = world.CreateContext();
        var debt = await db.AgentTaskSyncDebts.SingleAsync(d => d.TaskId == world.TaskId);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Ready);
        debt.ConfirmedSha.ShouldBe(world.Git.Baseline);
        debt.SourceSha.ShouldBe(world.Git.Baseline);
    }

    private static string ReviewReport(RunnerSettlementWorld world, Guid subject) =>
        RunnerSettlementWorld.Report(
            "No branch change.\n" + DelegationReportFormatter.FindingToken(world.TaskId, "clean")
            + $"\n--- review evidence ---\nsubjectTaskId: {subject:D}\nreviewedSourceSha: {world.Git.Baseline}\n"
            + "reviewedSourceClean: true\nordinaryScopeCompleted: Full\n\n",
            next: "none");

    private static async Task FinishBusySettleAsync(
        RunnerSettlementWorld world, System.Threading.Tasks.Task running, System.Threading.Tasks.Task busy)
    {
        (await System.Threading.Tasks.Task.WhenAny(busy, running)).ShouldBe(busy,
            "the settlement sync must wait for a busy lease within its budget");
        world.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
        await running;
    }

    private static async Task AssertNoDebtAsync(RunnerSettlementWorld world)
    {
        await using var db = world.CreateContext();
        (await db.AgentTaskSyncDebts.CountAsync(d => d.TaskId == world.TaskId)).ShouldBe(0);
    }

    private static void AssertPendingDebt(
        AgentTaskSyncDebt debt, RunnerSettlementWorld world, string source, AgentTaskEvent settlement)
    {
        debt.TaskId.ShouldBe(world.TaskId);
        debt.Attempt.ShouldBe(world.Task.Attempt);
        debt.Attempt.ShouldBe(1);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Pending);
        debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        debt.SourceSha.ShouldBe(source);
        debt.BaselineSha.ShouldBe(world.Git.Baseline);
        debt.FullRef.ShouldBe(world.Git.FullRef);
        debt.WorktreePath.ShouldBe(world.Task.WorktreePath);
        debt.RemoteWorktreePath.ShouldBe(world.Task.RemoteWorktreePath);
        debt.RunnerId.ShouldBe(world.Task.RunnerId);
        debt.RepositoryPath.ShouldBe(world.Task.RepoPath);
        debt.DesktopBeforeSha.ShouldBe(world.Git.Baseline);
        debt.EndpointFingerprint.ShouldNotBeNullOrWhiteSpace();
        debt.Attempts.ShouldBe(0);
        debt.Revision.ShouldBe(0);
        debt.ConfirmedSha.ShouldBeNull();
        debt.SettlementEventId.ShouldBe(settlement.Id);
        debt.NextAttemptAt.ShouldNotBeNull();
        debt.CreatedAt.ShouldBe(debt.UpdatedAt);
    }

    private static int DebtStatements(StatementCounter counter) =>
        counter.Texts.Count(t => t.Contains("AgentTaskSyncDebts", StringComparison.Ordinal));

    private static void AssertNoPush(RunnerSettlementWorld world, string reason, string body)
    {
        world.Task.Status.ShouldBe(AgentTaskStatus.Failed, Why(world));
        world.Task.FailureCode.ShouldBe(AgentTaskFailureCode.CompletedWithoutProgress);
        world.Task.FailureReason!.ShouldContain(reason);
        world.Evidence()!.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        world.Evidence()!.Reason.ShouldBe(reason);
        world.Task.Result!.ShouldContain(body);
        Directory.Exists(world.Git.Worktree).ShouldBeTrue();
    }

    private static async Task AssertRefusedAsync(RunnerSettlementWorld world, string reason)
    {
        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Refused);
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(reason);
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
        (await world.EventsAsync()).ShouldContain(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains(reason, StringComparison.Ordinal));
        world.Services.GetRequiredService<RecordingSessionStopper>().Killed.ShouldBeEmpty();
        Directory.Exists(world.Git.Worktree).ShouldBeTrue();
        world.Task.WorktreePath.ShouldBe(world.Git.Task.WorktreePath);
        await AssertNoDebtAsync(world);
        // The caller dispatches from the header: decide, not the report's own next: review.
        var note = await world.NoteAsync();
        note!.Body.ShouldContain("next=decide");
        note.Body.ShouldNotContain("next=review");
    }

    /// <summary>Failure context only: the settled reason and the persisted progress/sync evidence.</summary>
    private static string Why(RunnerSettlementWorld world) =>
        world.Task.FailureReason + " | evidence=" + world.Task.CompletionProgressEvidenceJson;

    /// <summary>Outside the Shouldly expression tree: Split's optional argument cannot appear in one.</summary>
    private static bool IsMergeCommand(string command) => command.Split(' ').Contains("merge");

    private static void AssertNoSync(RunnerSettlementWorld world, string s)
    {
        world.Git.Git.Commands.ShouldNotContain(x => RunnerCompletionProgressTests.IsSyncCommand(x)
            || x.Contains(s, StringComparison.Ordinal));
    }

    private sealed class ThrowBoundary(string cut) : LandDeliveryBoundary
    {
        public override System.Threading.Tasks.Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            boundary == cut
                ? System.Threading.Tasks.Task.FromException(new IOException("C1082 injected precommit failure"))
                : System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class StatementCounter : DbCommandInterceptor
    {
        public int Statements { get; private set; }
        public List<string> Texts { get; } = [];

        public void Reset()
        {
            Statements = 0;
            Texts.Clear();
        }

        private void Hit(DbCommand command)
        {
            Statements++;
            Texts.Add(command.CommandText);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Hit(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Hit(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Hit(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Hit(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
