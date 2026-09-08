using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandPreparationIdentityTests
{
    [Test]
    public async Task C448_V15_ExplicitRepostCannotReplaceTargetAdvanceIntent()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.TargetAdvanceStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var previous = (await h.OperationAsync()).ShouldNotBeNull();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "new source after advancement intent");
        var retained = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        await h.RepostAsync();
        await h.RestartServicesAsync();
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        var current = (await h.OperationAsync()).ShouldNotBeNull();
        current.Id.ShouldBe(previous.Id, "a request cannot discard unresolved target-advance evidence");
        current.Phase.ShouldBe(LandPhase.TargetAdvanceStarted);
        current.RemoteConfirmedAt.ShouldBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(retained);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", previous.RecoveryRefPrefix + "/source")).Trim().ShouldBe(original);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("source")]
    [Arguments("target")]
    [Arguments("target-checkout")]
    [Arguments("verification-filter")]
    public async Task C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation(string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.LandVerifyFilter = "/*/*/NewPreparation/*";
            await db.SaveChangesAsync();
        }
        h.Fault.Phase = LandPhase.Verified;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var previous = (await h.OperationAsync()).ShouldNotBeNull();
        if (change is "source" or "target")
            await h.Fixture.RequiredAsync(change == "source" ? h.Fixture.Source : h.Fixture.Repository,
                "commit", "--allow-empty", "-m", "new work after verified checkpoint");
        if (change == "target-checkout")
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "checkout", "-b", "another-target-checkout");
        var currentSource = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        if (change != "verification-filter")
        {
            await h.RestartServicesAsync();
            await h.RunAsync();
            var refused = (await h.OperationAsync()).ShouldNotBeNull();
            refused.Id.ShouldBe(previous.Id, "automatic recovery cannot replace changed preparation");
            refused.RemoteConfirmedAt.ShouldBeNull();
        }
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.RepostAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.LandVerifyFilter = change == "verification-filter" ? "/*/*/NewSelectedFilter/*" : "/*/*/NewPreparation/*";
            await db.SaveChangesAsync();
        }
        await h.RestartServicesAsync();
        await h.RunAsync();
        var completed = (await h.OperationAsync()).ShouldNotBeNull();
        completed.Id.ShouldNotBe(previous.Id, "an explicit fresh request must not strand changed work behind an unadvanced Verified checkpoint");
        completed.OriginalSourceSha.ShouldBe(currentSource);
        completed.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        h.Verifier.Calls.ShouldBe(2, "the changed preparation must receive fresh verification");
        await using (var db = h.CreateContext())
            (await db.AgentTaskLandings.SingleAsync(o => o.Id == previous.Id)).Active.ShouldBeFalse();
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", previous.RecoveryRefPrefix + "/source")).Trim().ShouldBe(original);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(completed.VerifiedSourceSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V25_LocalMergeCannotAdoptACommitAfterItsRebase()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        string? otherWriter = null;
        var fired = false;
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (fired || !result.Succeeded || !args.Contains("rebase") || args.Contains("--abort")) return;
            fired = true;
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "another local child writer");
            otherWriter = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        };
        await using var scope = h.Services.CreateAsyncScope();
        await using var db = h.CreateContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId);
        h.Fixture.Git.Trace.Clear();
        var result = await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>()
            .TryMergeBackAsync(task, CancellationToken.None);
        fired.ShouldBeTrue();
        result.Result.ShouldBe(DelegationWorktreeService.MergeResult.Failed,
            "local merge must refuse a commit created after its owned rebase result");
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a.Contains("remove") || a[0] == "push");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(otherWriter);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("advance")]
    [Arguments("same-sha-switch")]
    [Arguments("metadata")]
    [Arguments("metadata-path")]
    [Arguments("metadata-target")]
    [Arguments("metadata-repository")]
    [Arguments("staged")]
    [Arguments("dirty")]
    [Arguments("untracked")]
    public async Task C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation(string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var fired = false;
        var preparedCommitted = false;
        string? retained = null;
        h.Fault.AfterAcknowledged = phase => { if (phase == LandPhase.Prepared) preparedCommitted = true; return Task.CompletedTask; };
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (fired || !result.Succeeded || !args.Contains("rebase") || args.Contains("--abort")) return;
            fired = true;
            if (change == "advance") await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "another writer after owned rebase");
            if (change == "same-sha-switch") await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "-b", "another-owner");
            if (change.StartsWith("metadata", StringComparison.Ordinal))
            {
                await using var db = h.CreateContext();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
                if (change == "metadata-path") task.WorktreePath = Path.Combine(h.Fixture.Root, "another-recorded-source");
                else if (change == "metadata-target") task.MergeTargetRef = "another-recorded-target";
                else if (change == "metadata-repository") task.RepoPath = h.Fixture.Remote;
                else task.WorktreeBranch = "another-recorded-owner";
                await db.SaveChangesAsync();
            }
            if (change is "staged" or "dirty" or "untracked")
            {
                await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, change == "untracked" ? "new.txt" : "keep.txt"), "another writer staged bytes\n");
                if (change == "staged") await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "keep.txt");
            }
            retained = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        fired.ShouldBeTrue();
        preparedCommitted.ShouldBeFalse("Prepared must describe the owned rebase result, not a subsequent writer or revised task");
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        (await h.OperationAsync())!.RemoteConfirmedAt.ShouldBeNull();
        h.Verifier.Calls.ShouldBe(0);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(retained);
        if (change == "staged") (await h.Fixture.RequiredAsync(h.Fixture.Source, "diff", "--cached")).ShouldContain("another writer staged bytes");
        if (change is "staged" or "dirty" or "untracked")
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, change == "untracked" ? "new.txt" : "keep.txt"))).ShouldBe("another writer staged bytes\n");
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
