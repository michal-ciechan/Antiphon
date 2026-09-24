using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
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
    public async Task Runner_push_settles_success_without_claim()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");

        await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, world.Task.FailureReason);
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
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
        }

        // The exact branch never reached origin at all.
        await using (var world = await RunnerSettlementWorld.CreateAsync(pushBranch: false))
        {
            await world.SettleAsync(RunnerSettlementWorld.Report("Done."));
            AssertNoPush(world, RemoteSettlementSyncReasons.BranchNotPushed, "Done.");
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
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
    public async Task Sync_uncertainty_blocks_and_reply_retries()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");

        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, world.Task.FailureReason);
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Task.Result!.ShouldContain("Implemented and pushed.");
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
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

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, world.Task.FailureReason);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        world.Evidence()!.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
            .VerifiedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
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

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, world.Task.FailureReason);
        world.Task.DeliverablePath.ShouldBe("docs/plan.md");
        world.Task.DeliverableRef.ShouldBe(s);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
        var note = await world.NoteAsync();
        note!.Body.ShouldContain(DelegationGitFacts.FormatHeader(1, 1));
    }

    private static void AssertNoPush(RunnerSettlementWorld world, string reason, string body)
    {
        world.Task.Status.ShouldBe(AgentTaskStatus.Failed, world.Task.FailureReason);
        world.Task.FailureCode.ShouldBe(AgentTaskFailureCode.CompletedWithoutProgress);
        world.Task.FailureReason!.ShouldContain(reason);
        world.Evidence()!.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        world.Evidence()!.Reason.ShouldBe(reason);
        world.Task.Result!.ShouldContain(body);
        Directory.Exists(world.Git.Worktree).ShouldBeTrue();
    }

    private static async Task AssertRefusedAsync(RunnerSettlementWorld world, string reason)
    {
        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, world.Task.FailureReason);
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
    }

    private static void AssertNoSync(RunnerSettlementWorld world, string s)
    {
        world.Git.Git.Commands.ShouldNotContain(x => x.Contains("fetch", StringComparison.Ordinal)
            || x.Contains("ls-remote", StringComparison.Ordinal) || x.Contains("merge", StringComparison.Ordinal)
            || x.Contains(s, StringComparison.Ordinal));
    }
}
