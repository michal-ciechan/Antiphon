using System.Diagnostics;
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
        var current = (await h.OperationAsync()).ShouldNotBeNull();
        if (change == "automatic")
        {
            current.Id.ShouldBe(previous.Id);
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("remove") || a[0] == "push");
        }
        else
        {
            // CARD-0688 D-2/D-3: an explicit repost lands from the unchanged branch through the (reset) land
            // worktree; the task worktree's dirty bytes, sequencer or switched branch are guarded cleanup's
            // concern, which refuses and keeps them. The interrupted operation's recovery evidence is untouched.
            current.Id.ShouldNotBe(previous.Id);
            current.Publication.ShouldBe(LandPublicationOutcome.Landed);
            current.Cleanup.ShouldBe(LandCleanupStatus.Refused);
            current.LastReason.ShouldBe(change switch
            {
                "dirty" => "source_dirty", "active-sequencer" => "active_sequencer", _ => "source_branch_mismatch",
            });
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        }
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
                // CARD-0688 D-4: the target fast-forward (C09) is the canonical step after publication.
                "C09" => LandPhase.PublicationConfirmed, "C12" => LandPhase.PushStarted,
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
                // CARD-0688 D-3: the interrupted rebase lives in the disposable land worktree; the restart refuses
                // it without touching anything, and the task worktree (never rebased) keeps the operator's bytes.
                recovered.LastReason.ShouldBe("interrupted_rebase");
                (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim().ShouldBe(index);
                (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"))).ShouldBe("operator resolution\n");
                h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("reset") || a[0] == "push" || a.Contains("remove"));
                var landAdmin = (await h.Fixture.RequiredAsync(recovered.LandWorktreePath!, "rev-parse", "--absolute-git-dir")).Trim();
                (Path.Exists(Path.Combine(landAdmin, "rebase-merge")) || Path.Exists(Path.Combine(landAdmin, "rebase-apply")))
                    .ShouldBeTrue("the refusal leaves the interrupted rebase for the next request's reset, not the restart");
                // Explicit operator resolution is a separate action: rebase the task branch onto the target, keeping
                // the source side of the conflict, then land that commit with a fresh request.
                await h.Fixture.RequiredAsync(h.Fixture.Source, "reset", "--hard", "HEAD");
                var tip = (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim();
                await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "-X", "theirs", tip);
                var resolved = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
                const string filter = "/*/*/ResolvedFixture/*";
                await h.RequestAsync(filter, expectedSourceSha: resolved);
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
                h.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(cut is "C03" ? 1 : 0);
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
        var crash = new SimulatedServerCrash();
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
                throw crash;
            }
            return Task.CompletedTask;
        };
        SimulatedServerCrash? observedCrash = null;
        try { await h.RunAsync(); }
        catch (SimulatedServerCrash ex) { observedCrash = ex; }
        hit.ShouldBeTrue($"the successful {boundary} command must reach the interruption hook");
        observedCrash.ShouldBeSameAs(crash, "the interruption must escape cleanup, not become a completed residue result");
        var interrupted = (await h.OperationAsync()).ShouldNotBeNull();
        interrupted.Phase.ShouldBe(boundary switch
        {
            "local-advance" => LandPhase.PublicationConfirmed, // CARD-0688 D-4: the canonical fast-forward follows publication
            "push" => LandPhase.PushStarted,
            _ => LandPhase.CleanupStarted,
        });
        interrupted.VerifiedSourceSha.ShouldBe(sha);
        if (boundary == "directory-remove")
        {
            // Observe the acknowledgement gap independently: Git completed, but the durable
            // intent has no result and the branch still awaits its guarded deletion.
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            var registrations = await h.Fixture.Git.RegistrationsAsync(h.Fixture.Repository, CancellationToken.None);
            registrations.ShouldNotContain(r => LandingGit.PathsEqual(r.Path, h.Fixture.Source));
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(sha);
            interrupted.DirectoryRemoved.ShouldBeFalse();
            interrupted.RegistrationRemoved.ShouldBeFalse();
            interrupted.BranchRemoved.ShouldBeFalse();
            interrupted.CleanupCompletedAt.ShouldBeNull();
            await using var db = h.CreateContext();
            var attempt = await db.WorktreeCleanupAttempts.AsNoTracking().SingleAsync(a => a.OperationId == interrupted.Id);
            attempt.InitialCommandId.ShouldNotBeNull();
            attempt.InitialCompletedAt.ShouldBeNull();
            attempt.LastGitOutcomeJson.ShouldBeNull();
        }
        h.Fixture.Git.AfterCommand = null;
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync(); // New provider, DbContext, queue and service; only durable evidence survives.
        var recovered = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(recovered).ShouldBeTrue();
        recovered.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        recovered.Id.ShouldBe(interrupted.Id);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
        h.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(0);
        if (boundary == "directory-remove")
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("worktree") && a.Contains("remove"));
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
        refused.LastReason.ShouldBe("interrupted_rebase"); // CARD-0688 D-3: the land worktree is disposable
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
        h.Fault.Phase = LandPhase.Verified; // CARD-0688: the last checkpoint before the push (no target-advance phase)
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

    [Test]
    [Arguments("prepared")]
    [Arguments("local-target-advanced")]
    [Arguments("push-started")]
    [Arguments("published")]
    public async Task C688_SchemaTwoOperationsOnResume(string phase)
    {
        // CARD-0688 V-15 (D-7): schema-2 work resumes only where the old and new protocols coincide.
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        await h.Git.RequiredAsync(h.Git.Source, "commit", "--allow-empty", "-m", "schema-2 rebase moved the branch here");
        var rebased = h.Git.SourceHead;
        var (seeded, requestId) = await SeedSchemaTwoAsync(h, phase, original, rebased);

        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var op = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == seeded.Id);
        var terminal = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.LandRequestId == requestId && e.IsLandTerminal);
        switch (phase)
        {
            case "prepared":
                op.Phase.ShouldBe(LandPhase.Refused);
                op.LastReason.ShouldBe("landing_schema_superseded");
                terminal.Type.ShouldBe(AgentTaskEventType.LandRefused);
                terminal.Detail.ShouldContain("landing_schema_superseded");
                h.Git.Commands.ShouldNotContain(c => c.Arguments[0] == "push" || c.Arguments.Contains("rebase"));
                break;
            case "local-target-advanced":
            case "push-started":
                op.Publication.ShouldBe(LandPublicationOutcome.Landed);
                h.Git.RemoteTarget.ShouldBe(rebased);
                terminal.Detail.ShouldContain("canonical=already");
                op.ExpectedDeletionSha.ShouldBe(rebased);
                op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
                break;
            default:
                op.Mode.ShouldBe(LandOperationMode.CleanupRetry);
                op.ExpectedDeletionSha.ShouldBe(op.VerifiedSourceSha);
                op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
                h.Git.Commands.ShouldNotContain(c => c.Arguments[0] == "push");
                break;
        }
    }

    [Test]
    public async Task C688_InterruptedRebaseRecoversOnNextRequest()
    {
        // CARD-0688 V-16 (D-3, I-13): an interrupted rebase in the disposable land worktree refuses
        // interrupted_rebase; the next request's reset aborts and cleans it, then lands.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new base");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        var target = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim();
        var reader = new LandingGitFixture.FixtureGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId);
        h.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (!args.Contains("rebase") || args.Contains("--abort") || !result.Succeeded) return;
            // The process dies mid-rebase: leave the sequencer state a real interrupted rebase leaves.
            var admin = (await reader.RunAsync(directory, ["rev-parse", "--absolute-git-dir"], CancellationToken.None)).Output.Trim();
            var state = Path.Combine(admin, "rebase-merge");
            Directory.CreateDirectory(state);
            await File.WriteAllTextAsync(Path.Combine(state, "head-name"), "detached HEAD\n");
            await File.WriteAllTextAsync(Path.Combine(state, "orig-head"), source + "\n");
            await File.WriteAllTextAsync(Path.Combine(state, "onto"), target + "\n");
            throw new SimulatedServerCrash();
        };
        await Should.ThrowAsync<SimulatedServerCrash>(() => h.RunAsync());
        h.Fixture.Git.AfterCommand = null;
        var interrupted = (await h.OperationAsync()).ShouldNotBeNull();
        interrupted.Phase.ShouldBe(LandPhase.RebaseStarted);
        h.Fixture.Git.Commands.Clear();
        await h.RestartServicesAsync();

        await h.RunAsync();

        var refused = (await h.OperationAsync()).ShouldNotBeNull();
        refused.Id.ShouldBe(interrupted.Id);
        refused.Phase.ShouldBe(LandPhase.Refused);
        refused.LastReason.ShouldBe("interrupted_rebase");
        h.Fixture.Git.Commands.ShouldNotContain(c => c.Arguments[0] == "push" || c.Arguments.Contains("rebase") || c.Arguments.Contains("reset"));

        await h.RepostAsync();
        h.Fixture.Git.Commands.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync();

        var landed = (await h.OperationAsync()).ShouldNotBeNull();
        landed.Id.ShouldNotBe(refused.Id);
        landed.Publication.ShouldBe(LandPublicationOutcome.Landed);
        var commands = h.Fixture.Git.Commands;
        var abort = commands.FindIndex(c => c.Arguments.Contains("rebase") && c.Arguments.Contains("--abort"));
        var reset = commands.FindIndex(c => c.Arguments.Contains("reset") && c.Arguments.Contains("--hard"));
        var rebase = commands.FindIndex(c => c.Arguments.Contains("rebase") && !c.Arguments.Contains("--abort") && !c.Arguments.Contains("--quit"));
        abort.ShouldBeGreaterThanOrEqualTo(0);
        reset.ShouldBeGreaterThan(abort);
        rebase.ShouldBeGreaterThan(reset);
        LandingGit.PathsEqual(commands[abort].Directory, landed.LandWorktreePath!).ShouldBeTrue();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private static async Task<(Antiphon.Server.Domain.Entities.AgentTaskLanding Op, Guid RequestId)> SeedSchemaTwoAsync(
        LandingProtocolHarness h, string phase, string original, string rebased)
    {
        var queued = await h.RequestAsync(expectedSourceSha: original);
        var now = DateTime.UtcNow;
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        var published = phase == "published";
        var op = new Antiphon.Server.Domain.Entities.AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = h.Git.TaskId, SchemaVersion = 2, Active = true, CreatedAt = now, UpdatedAt = now,
            Phase = phase switch { "prepared" => LandPhase.Prepared, "local-target-advanced" => LandPhase.LocalTargetAdvanced,
                "push-started" => LandPhase.PushStarted,
                _ => LandPhase.PublicationConfirmed },
            RepositoryPath = h.Git.Repository, CommonDirectory = Path.GetFullPath(h.Git.CommonDir), WorktreePath = h.Git.Source,
            GitDirectory = Path.GetFullPath(h.Git.SourceGitDirectory), SourceFullRef = h.Git.SourceRef,
            OriginalSourceSha = original, ReviewedSourceSha = original, PreparationInputSha = original,
            ApprovalLandRequestId = request.Id, ApprovalKind = request.ApprovalKind, ApprovedAt = now,
            SourceRemoteSha = h.Git.RemoteSource, SourceRemoteRef = h.Git.SourceRef, SourceRemoteFingerprint = h.Git.Fingerprint,
            SourceRemoteObservedAt = now, TargetFullRef = h.Git.TargetRef, TargetBeforeSha = h.Git.SeedSha,
            RemoteBeforeSha = h.Git.SeedSha, RemoteName = "origin", DestinationFullRef = h.Git.TargetRef,
            RemoteFingerprint = h.Git.Fingerprint, VerificationFilter = request.VerifyFilter,
            TargetCheckoutRecorded = true, TargetCheckoutPath = h.Git.Repository,
            SourcePinned = true, TargetPinned = true, PreparedPinned = true, RebasedSourceSha = rebased,
            RebaseStartedAt = now, PreparedAt = now,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}";
        if (phase != "prepared")
        {
            op.VerifiedSourceSha = rebased;
            op.VerifiedAt = now;
            op.VerificationPassed = true;
            op.VerificationStartedAt = now;
            op.LocalTargetAfterSha = rebased;
            await h.Git.RequiredAsync(h.Git.Repository, "update-ref", h.Git.TargetRef, rebased, h.Git.SeedSha);
        }
        if (published)
        {
            h.Git.SetRemoteContainsSource();
            op.Publication = LandPublicationOutcome.Landed;
            op.PushStartedAt = now;
            op.PushExitCode = 0;
            op.RemoteConfirmedAt = now;
            op.ObservedRemoteTargetSha = rebased;
            op.ConfirmationMethod = "push-endpoint-read-fetch-ancestry";
        }
        if (phase == "push-started") op.PushStartedAt = now;
        h.Git.SetPin(op.RecoveryRefPrefix + "/source", original);
        h.Git.SetPin(op.RecoveryRefPrefix + "/target-before", h.Git.SeedSha);
        h.Git.SetPin(op.RecoveryRefPrefix + "/prepared", rebased);
        db.AgentTaskLandings.Add(op);
        await db.SaveChangesAsync();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
        task.ActiveLandingId = op.Id;
        request.LandingOperationId = op.Id;
        await db.SaveChangesAsync();
        h.Git.Commands.Clear();
        return (op, request.Id);
    }

    private sealed class SimulatedServerCrash : Exception;
}
