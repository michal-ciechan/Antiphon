using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandCheckpointMatrixTests
{
    [Test]
    public async Task C448_C09_ResumedTargetCannotAdoptAnEarlierAncestor()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "recorded target before");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        h.Fault.Phase = LandPhase.TargetAdvanceStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var before = (await h.OperationAsync()).ShouldNotBeNull();
        before.TargetBeforeSha.ShouldNotBe(h.Fixture.SeedSha);
        // This target is an ancestor of P, so a generic fast-forward guard cannot mask
        // omission of the stored T0/P boundary. Only fixture-owned target state is reset.
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "reset", "--hard", h.Fixture.SeedSha);
        await h.RestartServicesAsync();
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(before.Id);
        after.LastReason.ShouldBe("target_changed");
        after.RemoteConfirmedAt.ShouldBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("C02", "source")]
    [Arguments("C02", "target")]
    [Arguments("C03", "source")]
    [Arguments("C03", "target")]
    [Arguments("C03", "pin")]
    [Arguments("C07", "source")]
    [Arguments("C07", "target")]
    [Arguments("C08", "source")]
    [Arguments("C08", "target")]
    [Arguments("C09", "source")]
    [Arguments("C09", "target")]
    public async Task C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork(string cut, string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        if (cut == "C03")
        {
            var fired = false;
            h.Fixture.Git.AfterCommand = (_, args, result) =>
            {
                if (!fired && result.Succeeded && args[0] == "update-ref"
                    && args.Any(a => a.EndsWith("/source", StringComparison.Ordinal)))
                { fired = true; throw new InterruptedBoundary(); }
                return Task.CompletedTask;
            };
            await Should.ThrowAsync<InterruptedBoundary>(() => h.RunAsync());
            fired.ShouldBeTrue();
            h.Fixture.Git.AfterCommand = null;
        }
        else
        {
            h.Fault.Phase = cut switch { "C02" => LandPhase.Inspected, "C07" => LandPhase.Prepared,
                "C08" => LandPhase.Verified, _ => LandPhase.TargetAdvanceStarted };
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        }
        var before = (await h.OperationAsync()).ShouldNotBeNull();
        if (change == "pin")
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", before.RecoveryRefPrefix + "/source", h.Fixture.SeedSha);
        else
            await h.Fixture.RequiredAsync(change == "source" ? h.Fixture.Source : h.Fixture.Repository,
                "commit", "--allow-empty", "-m", "new recovery writer");
        var source = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var target = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim();
        var calls = h.Verifier.Calls;
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(before.Id);
        after.RemoteConfirmedAt.ShouldBeNull();
        after.LastReason.ShouldBe(change switch { "source" => "source_changed", "target" => "target_changed", _ => "recovery_pin_failed" });
        h.Verifier.Calls.ShouldBe(calls);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(source);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(target);
        if (change == "pin")
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", before.RecoveryRefPrefix + "/source")).Trim().ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("C02", LandPhase.Inspected, true)]
    [Arguments("C04", LandPhase.RecoveryPinned, true)]
    [Arguments("C06", LandPhase.Prepared, false)]
    [Arguments("C07", LandPhase.Prepared, true)]
    [Arguments("C08", LandPhase.Verified, true)]
    [Arguments("C09", LandPhase.TargetAdvanceStarted, true)]
    [Arguments("C10", LandPhase.LocalTargetAdvanced, true)]
    [Arguments("C11", LandPhase.PushStarted, true)]
    [Arguments("C13", LandPhase.PublicationConfirmed, false)]
    [Arguments("C15", LandPhase.CleanupStarted, true)]
    [Arguments("C21", LandPhase.Complete, false)]
    public async Task C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints(string cut, LandPhase phase, bool committed)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new target base");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        h.Fault.Phase = phase;
        h.Fault.AfterCommit = committed;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        var before = (await h.OperationAsync()).ShouldNotBeNull();
        var calls = h.Verifier.Calls;
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(before.Id);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", after.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        if (cut == "C06")
        {
            after.LastReason.ShouldBe("interrupted_rebase_requires_inspection");
            Directory.Exists(h.Fixture.Source).ShouldBeTrue();
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("merge") || a[0] == "push" || a.Contains("remove"));
            h.Verifier.Calls.ShouldBe(calls);
        }
        else
        {
            new AgentTaskLandingState().HasPublication(after).ShouldBeTrue();
            after.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            after.VerifiedSourceSha.ShouldNotBe(source, "the real rebase must distinguish original S from prepared P");
            h.Verifier.Calls.ShouldBe(calls + (cut is "C02" or "C04" or "C07" ? 1 : 0));
            if (cut is not ("C02" or "C04")) h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
            h.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(cut is "C13" or "C15" or "C21" ? 0 : 1);
            (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(after.VerifiedSourceSha);
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", after.RecoveryRefPrefix + "/prepared")).Trim().ShouldBe(after.VerifiedSourceSha);
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("C09", "source")]
    [Arguments("C09", "destination")]
    [Arguments("C10", "source")]
    [Arguments("C10", "destination")]
    [Arguments("C11", "source")]
    [Arguments("C11", "destination")]
    [Arguments("C12", "source")]
    [Arguments("C12", "destination")]
    [Arguments("C13", "source")]
    [Arguments("C13", "destination")]
    [Arguments("C14", "source")]
    [Arguments("C14", "destination")]
    [Arguments("C15", "source")]
    [Arguments("C15", "destination")]
    [Arguments("C16", "source")]
    [Arguments("C16", "destination")]
    [Arguments("C17", "source")]
    [Arguments("C17", "destination")]
    public async Task C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates(string cut, string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var commandCut = cut is "C12" or "C16" or "C17";
        if (commandCut)
        {
            var fired = false;
            h.Fixture.Git.AfterCommand = (_, args, result) =>
            {
                if (!fired && result.Succeeded && (cut switch
                    { "C12" => args[0] == "push", "C16" => args.Contains("worktree") && args.Contains("remove"),
                      _ => args[0] == "update-ref" && args.Contains("-d") }))
                { fired = true; throw new InterruptedBoundary(); }
                return Task.CompletedTask;
            };
            await Should.ThrowAsync<InterruptedBoundary>(() => h.RunAsync());
            fired.ShouldBeTrue();
            h.Fixture.Git.AfterCommand = null;
        }
        else
        {
            h.Fault.Phase = cut switch { "C09" => LandPhase.TargetAdvanceStarted, "C10" => LandPhase.LocalTargetAdvanced,
                "C11" => LandPhase.PushStarted, "C13" or "C14" => LandPhase.PublicationConfirmed, _ => LandPhase.CleanupStarted };
            h.Fault.AfterCommit = cut != "C13";
            await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
            h.Fault.Triggered.ShouldBeTrue();
        }
        var before = (await h.OperationAsync()).ShouldNotBeNull();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "branch", "equal-sha-alternative", source);
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            if (change == "source") task.WorktreeBranch = "equal-sha-alternative";
            else task.MergeTargetRef = "equal-sha-alternative";
            await db.SaveChangesAsync();
        }
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(before.Id);
        after.LastReason.ShouldBe("pending_operation_coordinates_changed");
        after.RemoteConfirmedAt.ShouldBe(before.RemoteConfirmedAt, "a historical receipt is retained, never transferred to new coordinates");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("merge") || a[0] == "push" || a.Contains("remove") || a.Contains("-d"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "refs/heads/equal-sha-alternative")).Trim().ShouldBe(source);
        if (cut != "C17") (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
        if (cut is not ("C16" or "C17")) Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class InterruptedBoundary : Exception;
}
