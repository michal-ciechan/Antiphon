using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRecoveryTests
{
    [Test]
    [Arguments("automatic")]
    [Arguments("dirty")]
    [Arguments("active-sequencer")]
    [Arguments("changed-symbolic-branch")]
    public async Task C448_V17_UnsafeOrAutomaticRepostCannotDiscardInterruptedEvidence(string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.RebaseStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        await h.RestartServicesAsync();
        await h.RunAsync();
        var previous = (await h.OperationAsync()).ShouldNotBeNull();
        previous.Phase.ShouldBe(LandPhase.Refused);
        if (change == "dirty") await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"), "manual work\n");
        if (change == "active-sequencer") Directory.CreateDirectory(Path.Combine(previous.GitDirectory, "sequencer"));
        if (change == "changed-symbolic-branch") await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "-b", "different-owner");
        if (change != "automatic") await h.RepostAsync();
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync();
        (await h.OperationAsync())!.Id.ShouldBe(previous.Id);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("remove") || a[0] == "push");
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        if (change == "dirty") (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"))).ShouldBe("manual work\n");
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", previous.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V31_FailedReplacementKeepsThePreviousOperationRetryable()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Verifier.Passed = false;
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.LandVerifyFilter = "/*/*/V31Refuse/*";
            await db.SaveChangesAsync();
        }
        await h.RunAsync();
        var previous = (await h.OperationAsync()).ShouldNotBeNull();
        previous.Phase.ShouldBe(LandPhase.Refused);
        h.Verifier.Passed = true;
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "new retry evidence");
        await h.RepostAsync();
        h.Fixture.Git.BeforeCommand = (_, a) => Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(
            a.Contains("get-url") ? new(128, "", "fixture configuration lookup failed") : null);
        await h.RestartServicesAsync();
        await h.RunAsync();
        var stillActive = await h.OperationAsync();
        stillActive.ShouldNotBeNull("failed replacement preparation must not deactivate the only operation while the task still points to it");
        stillActive.Id.ShouldBe(previous.Id);
        h.Fixture.Git.BeforeCommand = null;
        await h.RepostAsync();
        await h.RestartServicesAsync();
        await h.RunAsync();
        var replacement = (await h.OperationAsync()).ShouldNotBeNull();
        replacement.Id.ShouldNotBe(previous.Id);
        replacement.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        await using var observer = h.CreateContext();
        (await observer.AgentTaskLandings.SingleAsync(o => o.Id == previous.Id)).Active.ShouldBeFalse();
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", previous.RecoveryRefPrefix + "/source")).Trim().ShouldBe(previous.OriginalSourceSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("C03")]
    [Arguments("C05")]
    [Arguments("C09")]
    [Arguments("C12")]
    [Arguments("C14")]
    [Arguments("C16")]
    [Arguments("C17")]
    public async Task C448_V15_RealWorkerDeathRecoversDurableBoundaries(string cut)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        if (cut == "C05")
        {
            await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"), "source change\n");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "keep.txt");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "source conflict");
            source = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
            await File.WriteAllTextAsync(Path.Combine(h.Fixture.Repository, "keep.txt"), "target change\n");
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "add", "keep.txt");
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "-m", "target conflict");
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        }
        var ready = Path.Combine(h.Fixture.Root, "worker-ready.json");
        var script = Path.Combine(h.Fixture.Root, "protocol-worker.ps1");
        await File.WriteAllTextAsync(script, """
            $ErrorActionPreference = 'Stop'
            $assembly = [Reflection.Assembly]::LoadFrom($args[0])
            $type = $assembly.GetType('Antiphon.Tests.TestHelpers.LandingSafetyHarness', $true)
            $method = $type.GetMethod('RunCrashWorkerAsync', [Reflection.BindingFlags]'Public,Static')
            $task = $method.Invoke($null, [object[]]@($args[1], $args[2], $args[3], $args[4]))
            $task.GetAwaiter().GetResult()
            """);
        var start = new System.Diagnostics.ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", script, typeof(LandingSafetyHarness).Assembly.Location,
                     h.Fixture.Root, h.Fixture.TaskId.ToString(), cut, ready }) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_C448_TEST_CONNECTION"] = h.Schema.ConnectionString;
        using var worker = System.Diagnostics.Process.Start(start)!;
        var stdout = worker.StandardOutput.ReadToEndAsync();
        var stderr = worker.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (!File.Exists(ready) && !worker.HasExited) await Task.Delay(100, budget.Token);
            File.Exists(ready).ShouldBeTrue(worker.HasExited ? await stderr : "required crash cut not reached");
            var interrupted = (await h.OperationAsync()).ShouldNotBeNull();
            var expected = cut switch { "C03" => LandPhase.Inspected, "C05" => LandPhase.RebaseStarted,
                "C09" => LandPhase.TargetAdvanceStarted, "C12" => LandPhase.PushStarted,
                "C14" => LandPhase.PublicationConfirmed, _ => LandPhase.CleanupStarted };
            interrupted.Phase.ShouldBe(expected);
            if (cut == "C03")
            {
                interrupted.SourcePinned.ShouldBeFalse("worker dies before the source-pin result is saved");
                (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", interrupted.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
            }
            worker.Kill(entireProcessTree: false);
            await worker.WaitForExitAsync();
            string? index = null;
            if (cut == "C05")
            {
                await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"), "operator resolution\n");
                await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "keep.txt");
                index = (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim();
            }
            h.Fixture.Git.Trace.Clear();
            start.ArgumentList[6] = "resume";
            using (var resumedWorker = System.Diagnostics.Process.Start(start)!)
            {
                var resumedOutput = resumedWorker.StandardOutput.ReadToEndAsync();
                var resumedError = resumedWorker.StandardError.ReadToEndAsync();
                using var resumedBudget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                try { await resumedWorker.WaitForExitAsync(resumedBudget.Token); }
                finally
                {
                    if (!resumedWorker.HasExited) resumedWorker.Kill(true);
                    await resumedWorker.WaitForExitAsync();
                    await Task.WhenAll(resumedOutput, resumedError);
                }
                resumedWorker.ExitCode.ShouldBe(0, await resumedError);
                h.Fixture.Git.Trace.AddRange(System.Text.Json.JsonSerializer.Deserialize<string[][]>(
                    await File.ReadAllTextAsync(ready + ".resume-trace.json"))!);
            }
            var recovered = (await h.OperationAsync()).ShouldNotBeNull();
            recovered.Id.ShouldBe(interrupted.Id);
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", recovered.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
            if (cut == "C05")
            {
                recovered.LastReason.ShouldBe("interrupted_rebase_requires_inspection");
                (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim().ShouldBe(index);
                (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"))).ShouldBe("operator resolution\n");
                h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a[0] == "push" || a.Contains("remove"));
                // Explicit operator resolution is a separate action after the conservative restart refusal.
                await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "--continue", "--no-edit");
                var resolved = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
                await h.RepostAsync();
                await using (var request = h.CreateContext())
                {
                    var task = await request.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
                    task.LandVerifyFilter = "/*/*/ResolvedFixture/*";
                    await request.SaveChangesAsync();
                }
                await h.RestartServicesAsync();
                await h.RunAsync();
                var explicitRetry = (await h.OperationAsync()).ShouldNotBeNull();
                explicitRetry.Id.ShouldNotBe(recovered.Id);
                explicitRetry.OriginalSourceSha.ShouldBe(resolved);
                explicitRetry.VerificationPassed.ShouldBeTrue();
                h.Verifier.Calls.ShouldBe(1);
                explicitRetry.Cleanup.ShouldBe(LandCleanupStatus.Complete);
                (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", recovered.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
            }
            else
            {
                recovered.Cleanup.ShouldBe(LandCleanupStatus.Complete);
                recovered.VerifiedSourceSha.ShouldBe(source);
                if (cut != "C03") h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
                h.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(cut is "C03" or "C09" ? 1 : 0);
                (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(source);
            }
            await h.Fixture.AssertRemoteSourceAsync();
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }

    [Test]
    [Arguments("local-advance")]
    [Arguments("push")]
    [Arguments("directory-remove")]
    public async Task C448_V16_RestartReconcilesAcknowledgementGap(string boundary)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        var hit = false;
        h.Fixture.Git.AfterCommand = (_, args, result) =>
        {
            if (!hit && result.Succeeded && (boundary switch
                {
                    "local-advance" => args.Contains("merge") && args.Contains("--ff-only"),
                    "push" => args[0] == "push",
                    _ => args.Contains("worktree") && args.Contains("remove"),
                }))
            {
                hit = true;
                throw new SimulatedServerCrash();
            }
            return Task.CompletedTask;
        };
        await Should.ThrowAsync<SimulatedServerCrash>(() => h.RunAsync());
        hit.ShouldBeTrue();
        var interrupted = (await h.OperationAsync()).ShouldNotBeNull();
        interrupted.Phase.ShouldBe(boundary switch
        {
            "local-advance" => LandPhase.TargetAdvanceStarted,
            "push" => LandPhase.PushStarted,
            _ => LandPhase.CleanupStarted,
        });
        interrupted.VerifiedSourceSha.ShouldBe(sha);
        h.Fixture.Git.AfterCommand = null;
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync(); // New provider, DbContext, queue and service; only durable evidence survives.
        var recovered = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(recovered).ShouldBeTrue();
        recovered.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        recovered.Id.ShouldBe(interrupted.Id);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
        h.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(boundary == "local-advance" ? 1 : 0);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(sha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V17_InterruptedRebaseRequiresInspectionAndExplicitRepost()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Fixture.Git.AfterCommand = (_, args, result) =>
        {
            if (args.Contains("rebase") && result.Succeeded) throw new SimulatedServerCrash();
            return Task.CompletedTask;
        };
        await Should.ThrowAsync<SimulatedServerCrash>(() => h.RunAsync());
        h.Fixture.Git.AfterCommand = null;
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync();
        var refused = (await h.OperationAsync()).ShouldNotBeNull();
        refused.Phase.ShouldBe(LandPhase.Refused);
        refused.LastReason.ShouldBe("interrupted_rebase_requires_inspection");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--abort") || a[0] == "push" || a.Contains("remove"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", refused.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.RepostAsync();
        await h.RestartServicesAsync();
        await h.RunAsync();
        var completed = (await h.OperationAsync()).ShouldNotBeNull();
        completed.Id.ShouldNotBe(refused.Id);
        completed.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", refused.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V31_UnknownSchemaCannotResumeMutation()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.TargetAdvanceStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        await using (var db = h.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Fixture.TaskId);
            op.SchemaVersion = 999;
            await db.SaveChangesAsync();
        }
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        try { await h.RunAsync(); }
        catch (InvalidOperationException) { /* Older protocol discovers schema only after mutation. */ }
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        var resumedMutation = h.Fixture.Git.Trace.Any(a => a[0] == "push" || a.Contains("merge") || a.Contains("rebase") || a.Contains("remove"));
        resumedMutation.ShouldBeFalse("unknown schema must refuse before any resumed mutation");
        (await h.OperationAsync())!.LastReason.ShouldBe("landing_schema_unsupported");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class SimulatedServerCrash : Exception;
}
