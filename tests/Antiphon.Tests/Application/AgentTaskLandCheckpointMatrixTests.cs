using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
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
            if (cut == "C16") h.Fixture.Git.AfterUnregister = _ =>
            {
                fired = true;
                throw new InterruptedBoundary();
            };
            h.Fixture.Git.AfterCommand = (_, args, result) =>
            {
                if (!fired && result.Succeeded && (cut switch
                    { "C12" => args[0] == "push", "C16" => false,
                      _ => args[0] == "update-ref" && args.Contains("-d") }))
                { fired = true; throw new InterruptedBoundary(); }
                return Task.CompletedTask;
            };
            await Should.ThrowAsync<InterruptedBoundary>(() => h.RunAsync());
            fired.ShouldBeTrue();
            h.Fixture.Git.AfterCommand = null;
            h.Fixture.Git.AfterUnregister = null;
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
        var aside = WorktreeSetAside.SetAsidePath(h.Fixture.Source);
        if (cut is "C16" or "C17")
        {
            h.Fixture.Git.RegistrationDrops.ShouldHaveSingleItem().ShouldBe(h.Fixture.Source);
            Directory.Exists(before.GitDirectory).ShouldBeFalse();
            (await h.Fixture.Git.RegistrationsAsync(h.Fixture.Repository, default))
                .ShouldNotContain(r => LandingGit.PathsEqual(r.Path, h.Fixture.Source));
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            if (cut == "C16")
            {
                WorktreeSetAside.Read(before.CommonDirectory, h.Fixture.Source).ShouldNotBeNull();
                (await File.ReadAllTextAsync(Path.Combine(aside, "feature.txt"))).ShouldBe("valuable feature\n");
            }
            else
            {
                Directory.Exists(aside).ShouldBeFalse();
                WorktreeSetAside.Read(before.CommonDirectory, h.Fixture.Source).ShouldBeNull();
            }
        }
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
        if (before.RemoteConfirmedAt is not null)
        {
            // Published coordinate refusal escapes to the drain's durable failure settlement.
            var refusal = await Should.ThrowAsync<Exception>(() => h.RunAsync());
            refusal.Message.ShouldBe("pending_operation_coordinates_changed");
            await h.FailAsync(refusal);
        }
        else await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(before.Id);
        after.LastReason.ShouldBe(before.RemoteConfirmedAt is null ? "pending_operation_coordinates_changed"
            : LandFailureDiagnostic.InterruptedAfterPublication);
        after.RemoteConfirmedAt.ShouldBe(before.RemoteConfirmedAt, "a historical receipt is retained, never transferred to new coordinates");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("merge") || a[0] == "push" || a.Contains("remove") || a.Contains("-d"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "refs/heads/equal-sha-alternative")).Trim().ShouldBe(source);
        if (cut != "C17") (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
        if (cut is not ("C16" or "C17")) Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        if (cut == "C16")
        {
            WorktreeSetAside.Read(before.CommonDirectory, h.Fixture.Source).ShouldNotBeNull();
            (await File.ReadAllTextAsync(Path.Combine(aside, "feature.txt"))).ShouldBe("valuable feature\n");
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop(bool dropped)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var ready = Path.Combine(h.Fixture.Root, "removal-ready.json");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { typeof(LandingSafetyHarness).Assembly.Location, "--treenode-filter",
            "/*/*/AgentTaskLandCheckpointMatrixTests/C665_*" }) start.ArgumentList.Add(arg);
        start.Environment[LandingRemovalCrashWorker.Marker] = System.Text.Json.JsonSerializer.Serialize(
            new LandingRemovalCrashWorker.Request(h.Fixture.Root, h.Fixture.TaskId,
                dropped ? "C665-after-drop" : "C665-before-drop", ready));
        start.Environment["ANTIPHON_C448_TEST_CONNECTION"] = h.Schema.ConnectionString;
        using var worker = System.Diagnostics.Process.Start(start)!;
        var output = worker.StandardOutput.ReadToEndAsync();
        var error = worker.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (!File.Exists(ready) && !worker.HasExited) await Task.Delay(100, budget.Token);
            File.Exists(ready).ShouldBeTrue(worker.HasExited ? await error : "real removal boundary not reached");
            var op = (await h.OperationAsync()).ShouldNotBeNull();
            op.Phase.ShouldBe(LandPhase.CleanupStarted);
            var aside = WorktreeSetAside.SetAsidePath(h.Fixture.Source);
            var record = WorktreeSetAside.Read(op.CommonDirectory, h.Fixture.Source).ShouldNotBeNull();
            record.SetAsidePath.ShouldBe(aside);
            record.GitDirectory.ShouldBe(op.GitDirectory);
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            Directory.Exists(op.GitDirectory).ShouldBe(!dropped);
            var rows = await h.Fixture.Git.RegistrationsAsync(h.Fixture.Repository, default);
            rows.Any(r => LandingGit.PathsEqual(r.Path, h.Fixture.Source)).ShouldBe(!dropped);
            (await File.ReadAllTextAsync(Path.Combine(aside, "feature.txt"))).ShouldBe("valuable feature\n");
            worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync(budget.Token);
            await h.RestartServicesAsync();
            await h.RunAsync();
            var recovered = (await h.OperationAsync()).ShouldNotBeNull();
            recovered.Id.ShouldBe(op.Id);
            recovered.RemoteConfirmedAt.ShouldBe(op.RemoteConfirmedAt);
            if (!dropped)
            {
                // Recovery restores the intact tree, then respects the previously spent command
                // slot. A new request is needed to authorize another registration drop.
                recovered.LastReason.ShouldBe("cleanup_command_slot_spent");
                Directory.Exists(op.GitDirectory).ShouldBeTrue();
                (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "feature.txt"))).ShouldBe("valuable feature\n");
                WorktreeSetAside.Read(op.CommonDirectory, h.Fixture.Source).ShouldBeNull();
                await h.RepostAsync();
                await h.RunAsync();
                recovered = (await h.OperationAsync()).ShouldNotBeNull();
            }
            recovered.Cleanup.ShouldBe(LandCleanupStatus.Complete, recovered.LastReason);
            Directory.Exists(aside).ShouldBeFalse();
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            Directory.Exists(op.GitDirectory).ShouldBeFalse();
            WorktreeSetAside.Read(op.CommonDirectory, h.Fixture.Source).ShouldBeNull();
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", op.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
            await h.Fixture.AssertRemoteSourceAsync();
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
            await Task.WhenAll(output, error);
        }
    }

    // Cleanup intentionally catches ordinary failures. Cancellation models an interruption that
    // escapes the coordinator; the worker-death rows also exercise abrupt process exit.
    private sealed class InterruptedBoundary : OperationCanceledException;
}
