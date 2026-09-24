using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using SyncWorld = Antiphon.Tests.Application.RunnerSettlementSyncTests.SyncWorld;

namespace Antiphon.Tests.Application;

// CARD-0657 S2. Completion progress for a runner-bound Worktree task is judged against ONE
// prepared observation of the task's own pushed branch, taken before the commit matcher runs.
// Real Git throughout: the runner clone is the only place commits are made and pushed.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerCompletionProgressTests
{
    [Test]
    public async Task Own_pushed_tip_without_claim_is_primary_progress()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "runner work");

        var evaluated = await Evaluator(world).EvaluateAsync(world.Task, "Done, pushed.", CancellationToken.None);

        evaluated.IsProgress.ShouldBeTrue(evaluated.Reason);
        var primary = evaluated.Evidence.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved);
        primary.Origin.ShouldBe(ProgressOrigin.Primary);
        primary.VerifiedSha.ShouldBe(s);
        evaluated.AllowsAutomaticWorkspaceMutation.ShouldBeTrue();
        evaluated.Evidence.RemoteSync.ShouldNotBeNull();
        evaluated.Evidence.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        evaluated.Evidence.RemoteSync.ConfirmedSha.ShouldBe(s);
        evaluated.Evidence.RemoteSync.FullRef.ShouldBe(world.FullRef);
        (await world.HeadAsync()).ShouldBe(s);
    }

    [Test]
    public async Task Unpushed_claim_cannot_borrow_older_push()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "pushed");
        // The runner commits C after S but never pushes it.
        File.WriteAllText(Path.Combine(world.Runner, "later.txt"), "unpushed");
        await world.RunAsync(world.Runner, "add", "later.txt");
        await world.RunAsync(world.Runner, "commit", "-m", "unpushed");
        var c = await world.RunAsync(world.Runner, "rev-parse", "HEAD");
        world.Git.Clear();

        var evaluated = await Evaluator(world).EvaluateAsync(world.Task, "Done.\n" + Claim(world, c), CancellationToken.None);

        evaluated.IsNegative.ShouldBeTrue(evaluated.Reason);
        evaluated.Reason.ShouldBe(RemoteSettlementSyncReasons.ReportedCommitNotPushed);
        evaluated.Evidence.ClaimedSha.ShouldBe(c);
        evaluated.Evidence.RemoteSync!.ConfirmedSha.ShouldBe(s);
        (await world.HasObjectAsync(c)).ShouldBeFalse();
        world.Git.Commands.ShouldNotContain(x => IsSyncCommand(x) && x.Contains(c, StringComparison.Ordinal));
    }

    [Test]
    public async Task Uses_one_remote_observation()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "first");
        var prepared = await world.Service().SyncAsync(world.Task, CancellationToken.None);
        prepared.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        var s2 = await world.RunnerPushAsync("more.txt", "second");
        world.Git.Clear();

        var evaluated = await Evaluator(world).EvaluateAsync(world.Task, "Done.", prepared, CancellationToken.None);

        evaluated.IsProgress.ShouldBeTrue(evaluated.Reason);
        evaluated.Evidence.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
            .VerifiedSha.ShouldBe(s);
        evaluated.Evidence.RemoteSync!.ConfirmedSha.ShouldBe(s);
        evaluated.Evidence.Sources!.ShouldNotContain(x => x.RemoteObserved == s2 || x.VerifiedSha == s2);
        world.Git.Commands.ShouldNotContain(x => IsSyncCommand(x));
        (await world.HeadAsync()).ShouldBe(s);
    }

    [Test]
    public async Task Direct_evaluation_prepares_remote_source()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "runner work");
        (await world.HeadAsync()).ShouldBe(world.Baseline);

        // The public entry point with no prepared result reaches the same sync guard.
        var evaluated = await Evaluator(world).EvaluateAsync(world.Task, "Done.", CancellationToken.None);

        evaluated.Evidence.RemoteSync.ShouldNotBeNull();
        evaluated.Evidence.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        (await world.HeadAsync()).ShouldBe(s);
        evaluated.IsProgress.ShouldBeTrue(evaluated.Reason);

        // A refused guard reached the same way is uncertainty, not a verdict.
        await using var dirty = await SyncWorld.CreateAsync();
        await dirty.RunnerPushAsync("work.txt", "runner work");
        File.WriteAllText(Path.Combine(dirty.Worktree, "scratch.txt"), "dirt");
        var refused = await Evaluator(dirty).EvaluateAsync(dirty.Task, "Done.", CancellationToken.None);
        refused.IsIndeterminate.ShouldBeTrue(refused.Reason);
        refused.Reason.ShouldBe(RemoteSettlementSyncReasons.Dirty);
    }

    [Test]
    public async Task Local_and_repair_claim_rules_are_unchanged()
    {
        // A local task: no runner, no sync service. A desktop commit is ordinary Primary progress.
        await using (var world = await SyncWorld.CreateAsync())
        {
            var local = world.TaskWith(t => t.RunnerId = null);
            var d = await world.DesktopCommitAsync("local.txt", "desktop work");
            world.Git.Clear();

            var evaluated = await new TaskCompletionProgressService(world.Git)
                .EvaluateAsync(local, "Done.", CancellationToken.None);

            evaluated.IsProgress.ShouldBeTrue(evaluated.Reason);
            evaluated.Evidence.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
                .VerifiedSha.ShouldBe(d);
            evaluated.Evidence.RemoteSync.ShouldBeNull();
            world.Git.Commands.ShouldNotContain(x => IsSyncCommand(x));
        }

        // A local task whose branch moved only on origin still needs a task-scoped claim.
        await using (var world = await SyncWorld.CreateAsync())
        {
            var local = world.TaskWith(t => t.RunnerId = null);
            var s = await world.RunnerPushAsync("elsewhere.txt", "remote only");

            var unclaimed = await Evaluator(world).EvaluateAsync(local, "Done.", CancellationToken.None);
            unclaimed.IsNegative.ShouldBeTrue(unclaimed.Reason);
            unclaimed.Reason.ShouldBe("unclaimed_or_unmatched_commit");
            unclaimed.Evidence.RemoteSync.ShouldBeNull();
            (await world.HeadAsync()).ShouldBe(world.Baseline);

            var claimed = await Evaluator(world).EvaluateAsync(local, "Done.\n" + Claim(world, s), CancellationToken.None);
            claimed.IsProgress.ShouldBeTrue(claimed.Reason);
            claimed.Evidence.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
                .Origin.ShouldBe(ProgressOrigin.PrimaryRemote);
            (await world.HeadAsync()).ShouldBe(world.Baseline);
        }
    }

    [Test]
    public async Task Sync_failure_cannot_use_desktop_file_rescue()
    {
        await using var world = await SyncWorld.CreateAsync();
        await world.RunnerPushAsync("work.txt", "runner work");
        File.WriteAllText(Path.Combine(world.Worktree, "scratch.txt"), "desktop dirt");
        var files = new FreshFiles();

        var evaluated = await new TaskCompletionProgressService(world.Git, files, TimeProvider.System, world.Service())
            .EvaluateAsync(world.Task, "Done.", CancellationToken.None);

        evaluated.IsProgress.ShouldBeFalse();
        evaluated.IsIndeterminate.ShouldBeTrue(evaluated.Reason);
        evaluated.Reason.ShouldBe(RemoteSettlementSyncReasons.Dirty);
        evaluated.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
        evaluated.Evidence.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Refused);
        (await world.HeadAsync()).ShouldBe(world.Baseline);
        File.ReadAllText(Path.Combine(world.Worktree, "scratch.txt")).ShouldBe("desktop dirt");
    }

    [Test]
    public Task Persisted_remote_evidence_round_trips()
    {
        var s = new string('a', 40);
        var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved, null, null, null,
            [new CompletionProgressSource(ProgressOrigin.Primary, CompletionProgressAssessment.ProgressObserved, VerifiedSha: s)],
            new RemoteSyncEvidence(2, RemoteSettlementSyncState.Synchronized, "refs/heads/feat/card-task-0badf00d", s, s, null));

        var json = TaskProgressJson.SerializeEvidence(evidence);
        json.ShouldContain("\"remoteSync\"");
        json.ShouldContain("\"Synchronized\"");
        var back = TaskProgressJson.TryReadEvidence(json)!;
        back.RemoteSync.ShouldBe(evidence.RemoteSync);
        TaskProgressJson.ToDto(back)!.RemoteSync.ShouldBe(evidence.RemoteSync);

        var refused = RemoteSyncEvidence.From(3, new RemoteSettlementSyncResult(
            RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.Diverged, "refs/heads/x", s, s,
            DesktopAfterSha: s));
        refused.ConfirmedSha.ShouldBeNull();
        refused.Reason.ShouldBe(RemoteSettlementSyncReasons.Diverged);

        // Evidence written before this card has no remoteSync and still parses.
        var old = TaskProgressJson.TryReadEvidence(
            "{\"schemaVersion\":1,\"assessment\":\"NoAttributedProgress\",\"reason\":\"no_movement\"}")!;
        old.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        old.RemoteSync.ShouldBeNull();
        TaskProgressJson.SerializeEvidence(old).ShouldNotContain("remoteSync");
        return Task.CompletedTask;
    }

    [Test]
    public async Task Missing_remote_sync_dependency_is_unavailable()
    {
        await using var world = await SyncWorld.CreateAsync();
        await world.RunnerPushAsync("work.txt", "runner work");
        world.Git.Clear();

        var evaluated = await new TaskCompletionProgressService(world.Git, new FreshFiles())
            .EvaluateAsync(world.Task, "Done.", CancellationToken.None);

        evaluated.IsIndeterminate.ShouldBeTrue(evaluated.Reason);
        evaluated.Reason.ShouldBe(RemoteSettlementSyncReasons.DependencyUnavailable);
        evaluated.Evidence.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        world.Git.Commands.ShouldNotContain(x => IsSyncCommand(x));
        (await world.HeadAsync()).ShouldBe(world.Baseline);
    }

    private static TaskCompletionProgressService Evaluator(SyncWorld world) =>
        new(world.Git, null, TimeProvider.System, world.Service());

    /// <summary>A fetch, an advertisement read or a merge; never an ancestry query such as merge-base.</summary>
    internal static bool IsSyncCommand(string command) =>
        command.Split(' ').Any(token => token is "fetch" or "ls-remote" or "merge");

    private static string Claim(SyncWorld world, string sha) => $"[antiphon-progress:{world.TaskId:D} commit={sha}]";

    /// <summary>A file probe that always sees a fresh desktop change: the rescue a sync failure must not reach.</summary>
    private sealed class FreshFiles : IWorkspaceProgressProbe
    {
        public Task<WorkspaceProgressArm> ProbeProgressAsync(
            string? workingDirectory, DateTime since, bool sharedCheckout, CancellationToken ct) =>
            Task.FromResult(new WorkspaceProgressArm(true, DateTime.UtcNow, null, false));
    }
}
