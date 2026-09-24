using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryMutationLeaseTests
{
    [Test]
    public async Task C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        if (!OperatingSystem.IsWindows()) throw new TUnit.Core.Exceptions.SkipTestException("Windows inherited-handle boundary");
        var rootScript = Path.Combine(fixture.Root, "output-root.ps1");
        var script = Path.Combine(fixture.Root, "output-child.ps1");
        var ready = Path.Combine(fixture.Root, "output-child.json");
        await File.WriteAllTextAsync(script, """
            $ownedProcess = [Diagnostics.Process]::GetCurrentProcess()
            [IO.File]::WriteAllText($args[0] + '.tmp', (@{ Pid = $ownedProcess.Id; StartTicks = $ownedProcess.StartTime.ToUniversalTime().Ticks } | ConvertTo-Json -Compress))
            [IO.File]::Move($args[0] + '.tmp', $args[0])
            Start-Sleep -Seconds 90
            Write-Output 'fixture child exited'
            """);
        await File.WriteAllTextAsync(rootScript, """
            $ErrorActionPreference = 'Stop'
            $commandArgs = $args[2..($args.Length - 1)]
            & git @commandArgs
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            Add-Type -TypeDefinition @'
            using System;
            using System.Runtime.InteropServices;
            public static class FixtureHandles {
                [DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int which);
                [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
            }
            '@
            if (-not [FixtureHandles]::SetHandleInformation([FixtureHandles]::GetStdHandle(-12), 1, 0)) { throw 'fixture stderr inheritance failed' }
            $childStart = [Diagnostics.ProcessStartInfo]::new('pwsh')
            $childStart.UseShellExecute = $false
            $childStart.CreateNoWindow = $true
            $childStart.RedirectStandardError = $true
            foreach ($part in @('-NoProfile', '-File', $args[0], $args[1])) { $childStart.ArgumentList.Add($part) }
            [void][Diagnostics.Process]::Start($childStart)
            """);
        var runner = new OutputHoldingGit(Path.Combine(fixture.Root, "home"), fixture.TaskId, rootScript, script, ready);
        Process? root = null;
        Process? child = null;
        var operation = runner.RunOwnedAsync(fixture.Source,
            ["commit", "--allow-empty", "-m", "output descendant"],
            (pid, ticks, _) => { root = Process.GetProcessById(pid); root.StartTime.ToUniversalTime().Ticks.ShouldBe(ticks); return Task.CompletedTask; }, CancellationToken.None);
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(ready)) await Task.Delay(50, budget.Token);
            using var record = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(ready, budget.Token));
            child = Process.GetProcessById(record.RootElement.GetProperty("Pid").GetInt32());
            child.StartTime.ToUniversalTime().Ticks.ShouldBe(record.RootElement.GetProperty("StartTicks").GetInt64());
            root.ShouldNotBeNull();
            await root.WaitForExitAsync(budget.Token);
            child.HasExited.ShouldBeFalse();
            operation.IsCompleted.ShouldBeFalse("the descendant still owns stdout after the Git root exited");
            var common = await fixture.Git.CommonDirectoryAsync(fixture.Repository, CancellationToken.None);
            Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").ShouldNotBeEmpty(
                "a root exit cannot clear standing ownership while redirected output is still live");
            await using var admission = await new RepositoryMutationLease(fixture.Git).TryAcquireAsync(fixture.Repository, CancellationToken.None);
            admission.ShouldBeNull();
        }
        finally
        {
            if (child is not null) { if (!child.HasExited) child.Kill(true); await child.WaitForExitAsync(); child.Dispose(); }
            if (root is not null) { if (!root.HasExited) root.Kill(true); await root.WaitForExitAsync(); root.Dispose(); }
            await operation;
        }
        var afterProvider = new RepositoryMutationLease(fixture.Git);
        await using var after = await afterProvider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        after.ShouldNotBeNull("acknowledged root and output completion clears only the owned journal");
        await fixture.AssertRemoteSourceAsync();
    }

    private sealed class OutputHoldingGit(string home, Guid taskId, string rootScript, string childScript, string ready)
        : LandingGitFixture.FixtureGit(home, taskId)
    {
        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            base.ConfigureProcess(start);
            // The real command executes Git, then leaves a real descendant with stdout only.
            // Explicit stderr non-inheritance makes omission of stdout draining load-bearing.
            start.FileName = "pwsh";
            foreach (var argument in new[] { "-NoProfile", "-File", rootScript, childScript, ready }) start.ArgumentList.Add(argument);
        }
    }

    [Test]
    [Arguments("malformed")]
    [Arguments("torn-start")]
    [Arguments("directory-replaced-by-file")]
    public async Task C448_C24_UnreadableJournalStateCannotAdmitAWriter(string variant)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var common = await fixture.Git.CommonDirectoryAsync(fixture.Repository, CancellationToken.None);
        var directory = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(directory);
        if (variant == "directory-replaced-by-file")
        {
            Directory.Delete(directory); // Fixture-owned, confirmed empty.
            await File.WriteAllTextAsync(directory, "unknown bytes");
        }
        else await File.WriteAllTextAsync(Path.Combine(directory, variant == "torn-start" ? "child.json.tmp" : "child.json"), "unknown bytes");
        var leases = new RepositoryMutationLease(fixture.Git);
        await using var held = await leases.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        held.ShouldBeNull("unknown or unreadable standing child evidence must hold repository admission");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var hooks = Path.Combine(fixture.Root, "cancel-hooks");
        Directory.CreateDirectory(hooks);
        await File.WriteAllTextAsync(Path.Combine(hooks, "pre-commit"), "#!/bin/sh\nmkdir -p .antiphon\nprintf ready > .antiphon/owned-hook.ready\nsleep 90\n");
        await fixture.RequiredAsync(fixture.Repository, "config", "core.hooksPath", hooks);
        await fixture.RequiredAsync(fixture.Repository, "config", "user.name", "Fixture");
        await fixture.RequiredAsync(fixture.Repository, "config", "user.email", "fixture@example.invalid");
        var provider = new RepositoryMutationLease(fixture.Git);
        await using var lease = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        lease.ShouldNotBeNull();
        using var unrelated = StartSleeper();
        using var cancellation = new CancellationTokenSource();
        Process? child = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutation = new LandingGit().RunOwnedAsync(fixture.Source, ["commit", "--allow-empty", "-m", "owned cancel"],
            (pid, ticks, _) =>
            {
                child = Process.GetProcessById(pid);
                child.StartTime.ToUniversalTime().Ticks.ShouldBe(ticks);
                started.SetResult();
                return Task.CompletedTask;
            }, cancellation.Token);
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await started.Task.WaitAsync(budget.Token);
            var marker = Path.Combine(fixture.Source, ".antiphon", "owned-hook.ready");
            while (!File.Exists(marker))
            {
                if (mutation.IsCompleted) await mutation;
                await Task.Delay(50, budget.Token);
            }
            child!.HasExited.ShouldBeFalse();
            await using (var contender = await provider.TryAcquireAsync(fixture.Source, CancellationToken.None))
                contender.ShouldBeNull();
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => await mutation);
            child.HasExited.ShouldBeTrue("cancellation must await the exact owned process before returning");
            unrelated.HasExited.ShouldBeFalse();
            provider.Owns(lease, lease.CommonDirectory).ShouldBeTrue();
            Directory.EnumerateFiles(Path.Combine(lease.CommonDirectory, "antiphon", "children")).ShouldBeEmpty();
            await lease.DisposeAsync();
            await using var admitted = await provider.TryAcquireAsync(fixture.Source, CancellationToken.None);
            admitted.ShouldNotBeNull();
            File.Exists(Path.Combine(lease.CommonDirectory, "antiphon", "landing.lock")).ShouldBeTrue();
            await fixture.AssertRemoteSourceAsync();
        }
        finally
        {
            cancellation.Cancel();
            try { try { await mutation; } catch (OperationCanceledException) { } }
            finally
            {
                child?.Dispose();
                if (!unrelated.HasExited) unrelated.Kill(true);
                await unrelated.WaitForExitAsync();
            }
        }
    }

    private static Process StartSleeper()
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 90" }) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }

    [Test]
    [Arguments("alive")]
    [Arguments("reused")]
    [Arguments("unknown")]
    [Arguments("exited")]
    public async Task C448_C24_StandingJournalFencesAdmissionByStartIdentity(string state)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var provider = new RepositoryMutationLease(fixture.Git);
        var journal = await RepositoryChildJournal.BeginAsync(fixture.Repository, CancellationToken.None);
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 90" }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        try
        {
            if (state != "unknown")
                await journal.StartedAsync(child.Id, child.StartTime.ToUniversalTime().Ticks + (state == "reused" ? 1 : 0), CancellationToken.None);
            if (state == "exited") { child.Kill(true); await child.WaitForExitAsync(); }
            await using var admitted = await provider.TryAcquireAsync(fixture.Source, CancellationToken.None);
            admitted.ShouldBeNull("unacknowledged root exit or reused PID cannot prove descendant exit");
            if (state != "exited") child.HasExited.ShouldBeFalse("admission never kills a recorded or reused PID");
        }
        finally
        {
            if (!child.HasExited) child.Kill(true);
            await child.WaitForExitAsync();
            journal.Exited(child);
        }
        await using var after = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        after.ShouldNotBeNull();
    }

    // CARD-0661. A git child that exits before the journal reads its start identity. On Linux the
    // exited child is reaped and Process.StartTime throws; Windows still reads it through the
    // handle. Either way the command must not fail, the record must name the child, and the file
    // must keep fencing admission until its owner acknowledges the exit.
    [Test]
    public async Task C661_AlreadyExitedChildIsJournalledInsteadOfThrowing()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var provider = new RepositoryMutationLease(fixture.Git);
        var journal = await RepositoryChildJournal.BeginAsync(fixture.Repository, CancellationToken.None);
        var start = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--version");
        using var child = Process.Start(start)!;
        await child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync(); // Exited, and on Linux reaped, before its identity is read.

        await journal.StartedAsync(child, CancellationToken.None);

        var common = await fixture.Git.CommonDirectoryAsync(fixture.Repository, CancellationToken.None);
        var path = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
        var record = System.Text.Json.JsonSerializer.Deserialize<RepositoryChildJournal.ChildRecord>(await File.ReadAllTextAsync(path))!;
        record.ProcessId.ShouldBe(child.Id);
        (record.Completed || record.StartTicks is not null)
            .ShouldBeTrue("the child is identified by its start identity or recorded as completed");
        await using (var fenced = await provider.TryAcquireAsync(fixture.Source, CancellationToken.None))
            fenced.ShouldBeNull("the journal fences admission until its owner acknowledges the exit");
        journal.Exited(child);
        File.Exists(path).ShouldBeFalse();
        await using var after = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        after.ShouldNotBeNull();
    }

    // CARD-0661 (review c09b17c8 #2). A real mutating Git child exits before RunOwnedAsync reads its
    // start identity, which the platform can no longer report (Linux after reaping; the seam makes
    // that deterministic on Windows, which still reads it through the handle). The command succeeds
    // and the started callback is skipped, so the caller's persisted child state stays unknown:
    // after a crash here landing recovery refuses with interrupted_process_requires_inspection
    // rather than trusting a fabricated identity. The owner still clears its own journal on return.
    [Test]
    public async Task C661_FastExitingOwnedChildLeavesTheCallersChildStateUnknown()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var git = new IdentityLostGit(Path.Combine(fixture.Root, "home"), fixture.TaskId);
        // The caller's shape (AgentTaskLandingProtocol.OwnedAsync): persist intent with no identity,
        // let the callback persist the identity, clear only after the command returns.
        var op = new Antiphon.Server.Domain.Entities.AgentTaskLanding { TaskId = fixture.TaskId, ChildOperation = "commit" };
        var saves = new List<(string? Operation, int? ProcessId, long? StartTicks)>();
        void Save() => saves.Add((op.ChildOperation, op.ChildProcessId, op.ChildProcessStartTicks));
        Save();

        var result = await git.RunOwnedAsync(fixture.Source, ["commit", "--allow-empty", "-m", "fast exit"],
            (pid, ticks, _) =>
            {
                op.ChildProcessId = pid;
                op.ChildProcessStartTicks = ticks;
                Save();
                return Task.CompletedTask;
            }, CancellationToken.None);

        result.Succeeded.ShouldBeTrue("a child that exited before its identity was read must not fail the command");
        git.Reads.ShouldBe(1, "one identity read serves the journal and the caller");
        git.ExitedAtRead.ShouldBeTrue("the seam only reports a genuinely exited child as unidentifiable");
        saves.Count.ShouldBe(1, "the started callback is skipped for an unidentifiable child");
        op.ChildOperation.ShouldBe("commit");
        op.ChildProcessId.ShouldBeNull("the caller's persisted child state stays unknown, never a fabricated PID");
        op.ChildProcessStartTicks.ShouldBeNull("the caller's persisted child state stays unknown, never a fabricated identity");
        (await fixture.RequiredAsync(fixture.Source, "log", "-1", "--format=%s")).Trim().ShouldBe("fast exit");

        var common = await fixture.Git.CommonDirectoryAsync(fixture.Repository, CancellationToken.None);
        Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children")).ShouldBeEmpty(
            "the owner acknowledges its completed child once the streams drain");
        await using var after = await new RepositoryMutationLease(fixture.Git).TryAcquireAsync(fixture.Repository, CancellationToken.None);
        after.ShouldNotBeNull();
        await fixture.AssertRemoteSourceAsync();
    }

    // CARD-0661 (review 047193d4). The same fast exit through the real AgentTaskLandingProtocol and
    // Postgres: the owned rebase child exits before its identity is read, and the worker dies before
    // the protocol's clearing save. The persisted landing names the child operation with no identity,
    // and crash recovery refuses as needs-inspection instead of trusting or discarding that state.
    [Test]
    public async Task C661_FastExitingOwnedChildPersistsUnknownIdentityAndRecoveryRefuses()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var git = new IdentityLostGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId) { Armed = false };
        git.BeforeCommand = (_, arguments) =>
        {
            git.Armed = arguments.Contains("rebase"); // Only the protocol's owned rebase loses its identity.
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        h.ConfigureServices = services => services.AddSingleton<Antiphon.Server.Application.Interfaces.ILandingGit>(git);
        await h.RestartServicesAsync();
        // The worker dies after the child exits, before the protocol clears its child state.
        h.Fault.Matches = op => git.Reads > 0 && op.ChildOperation is null;

        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());

        h.Fault.Triggered.ShouldBeTrue();
        git.Reads.ShouldBe(1, "exactly the owned rebase read its identity");
        git.ExitedAtRead.ShouldBeTrue("the seam only reports a genuinely exited child as unidentifiable");
        await using (var observer = h.CreateContext())
        {
            var persisted = await observer.AgentTaskLandings.AsNoTracking()
                .SingleAsync(o => o.TaskId == h.Fixture.TaskId && o.Active);
            persisted.Phase.ShouldBe(Antiphon.Server.Domain.Enums.LandPhase.RebaseStarted);
            persisted.ChildOperation.ShouldNotBeNull().ShouldContain("rebase");
            persisted.ChildProcessId.ShouldBeNull("an unidentifiable child is never persisted with a fabricated PID");
            persisted.ChildProcessStartTicks.ShouldBeNull("an unidentifiable child is never persisted with a fabricated identity");
        }

        h.Fault.Matches = null;
        h.ConfigureServices = null;
        await h.RestartServicesAsync();
        await h.RunAsync();

        var recovered = (await h.OperationAsync()).ShouldNotBeNull();
        recovered.LastReason.ShouldBe("interrupted_process_requires_inspection");
        recovered.ChildOperation.ShouldNotBeNull("recovery keeps the unknown child for inspection");
        recovered.ChildProcessId.ShouldBeNull();
        recovered.ChildProcessStartTicks.ShouldBeNull();
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim()
            .ShouldBe(h.Fixture.SeedSha, "a refused recovery publishes nothing");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class IdentityLostGit(string home, Guid taskId) : LandingGitFixture.FixtureGit(home, taskId)
    {
        public bool Armed { get; set; } = true;
        public int Reads { get; private set; }
        public bool ExitedAtRead { get; private set; }

        protected override bool TryReadStartIdentity(Process child, out long startTicks)
        {
            if (!Armed) return base.TryReadStartIdentity(child, out startTicks);
            Reads++;
            child.WaitForExit(); // A genuinely fast-exiting child: stdout is already being drained.
            ExitedAtRead = child.HasExited;
            startTicks = 0;
            return false;
        }
    }

    [Test]
    public async Task C448_C24_KilledWorkerLeavesLiveGitChildFenced()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var hooks = Path.Combine(fixture.Root, "hooks");
        Directory.CreateDirectory(hooks);
        await File.WriteAllTextAsync(Path.Combine(hooks, "pre-commit"), "#!/bin/sh\nsleep 90\n");
        await fixture.RequiredAsync(fixture.Repository, "config", "core.hooksPath", hooks);
        var worker = Path.Combine(fixture.Root, "crash-worker.ps1");
        await File.WriteAllTextAsync(worker, """
            $ErrorActionPreference = 'Stop'
            [Reflection.Assembly]::LoadFrom($args[0]) | Out-Null
            $git = [Antiphon.Server.Infrastructure.Git.LandingGit]::new()
            $operation = $git.RunAsync($args[1], [string[]]@('commit', '--allow-empty', '-m', 'owned child'), [Threading.CancellationToken]::None)
            $result = $operation.GetAwaiter().GetResult()
            if (-not $result.Succeeded) { throw ('fixture_' + $result.Diagnostic) }
            """);
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", worker, typeof(LandingGit).Assembly.Location, fixture.Source })
            start.ArgumentList.Add(arg);
        // This worker uses the real LandingGit, so it does not inherit FixtureGit's process
        // overrides. Pin its identity and blocking hook instead of borrowing global Git config.
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(fixture.Root, "home", "empty-config");
        start.Environment["GIT_AUTHOR_NAME"] = "C448 Fixture";
        start.Environment["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid";
        start.Environment["GIT_COMMITTER_NAME"] = "C448 Fixture";
        start.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid";
        start.Environment["GIT_CONFIG_COUNT"] = "3";
        start.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgSign";
        start.Environment["GIT_CONFIG_VALUE_0"] = "false";
        start.Environment["GIT_CONFIG_KEY_1"] = "credential.helper";
        start.Environment["GIT_CONFIG_VALUE_1"] = "";
        start.Environment["GIT_CONFIG_KEY_2"] = "core.hooksPath";
        start.Environment["GIT_CONFIG_VALUE_2"] = hooks;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Process? child = null;
        using var unrelated = StartSleeper();
        try
        {
            var common = await fixture.Git.CommonDirectoryAsync(fixture.Repository, CancellationToken.None);
            var directory = Path.Combine(common, "antiphon", "children");
            RepositoryChildJournal.ChildRecord? record = null;
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (record?.ProcessId is null && !process.HasExited)
            {
                budget.Token.ThrowIfCancellationRequested();
                if (Directory.Exists(directory))
                {
                    var path = Directory.EnumerateFiles(directory, "*.json").SingleOrDefault();
                    if (path is not null) record = System.Text.Json.JsonSerializer.Deserialize<RepositoryChildJournal.ChildRecord>(await File.ReadAllTextAsync(path));
                }
                if (record?.ProcessId is null) await Task.Delay(50, budget.Token);
            }
            record.ShouldNotBeNull(process.HasExited ? await error : "child start must be acknowledged");
            record.ProcessId.ShouldNotBeNull(process.HasExited ? await error : "child start must be acknowledged");
            child = Process.GetProcessById(record.ProcessId.Value);
            child.StartTime.ToUniversalTime().Ticks.ShouldBe(record.StartTicks!.Value);
            process.Kill(entireProcessTree: false); // Actual OS worker death, not an in-process exception.
            await process.WaitForExitAsync();
            child.HasExited.ShouldBeFalse();
            var recoveryWorker = Path.Combine(fixture.Root, "recovery-worker.ps1");
            var recoveryEvidence = Path.Combine(fixture.Root, "recovery-admission.json");
            await File.WriteAllTextAsync(recoveryWorker, """
                $ErrorActionPreference = 'Stop'
                [Reflection.Assembly]::LoadFrom($args[0]) | Out-Null
                $git = [Antiphon.Server.Infrastructure.Git.LandingGit]::new()
                $leases = [Antiphon.Server.Infrastructure.Git.RepositoryMutationLease]::new($git)
                foreach ($repository in @($args[1], $args[2])) {
                    $lease = $leases.TryAcquireAsync($repository, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
                    if ($null -ne $lease) { $lease.DisposeAsync().GetAwaiter().GetResult(); exit 10 }
                }
                $ownedProcess = [Diagnostics.Process]::GetCurrentProcess()
                [IO.File]::WriteAllText($args[3], (@{ Worker = $ownedProcess.Id; StartTicks = $ownedProcess.StartTime.ToUniversalTime().Ticks; HeldSource = $true; HeldMain = $true } | ConvertTo-Json -Compress))
                """);
            await RunChildAsync("pwsh", ["-NoProfile", "-File", recoveryWorker,
                typeof(LandingGit).Assembly.Location, fixture.Source, fixture.Repository, recoveryEvidence], expectedExit: 0);
            using (var recovery = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(recoveryEvidence)))
            {
                recovery.RootElement.GetProperty("Worker").GetInt32().ShouldNotBe(Environment.ProcessId);
                recovery.RootElement.GetProperty("HeldSource").GetBoolean().ShouldBeTrue();
                recovery.RootElement.GetProperty("HeldMain").GetBoolean().ShouldBeTrue();
                LandingEvidence.Write(fixture.TaskId, "C24_recovery_worker", recovery.RootElement);
            }
            var provider = new RepositoryMutationLease(fixture.Git);
            await using (var sourceAdmission = await provider.TryAcquireAsync(fixture.Source, CancellationToken.None))
                sourceAdmission.ShouldBeNull();
            await using (var mainAdmission = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None))
                mainAdmission.ShouldBeNull();
            child.HasExited.ShouldBeFalse();
            unrelated.HasExited.ShouldBeFalse("worker recovery must not terminate an unrelated fixture process");
            await RecoverChildrenAsync(fixture.Source, 3, "-Execute", "-ConfirmDescendantsExited");
            child.HasExited.ShouldBeFalse("recovery must preserve the live recorded child");
            child.Kill(true);
            await child.WaitForExitAsync();
            await using (var after = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None))
                after.ShouldBeNull("worker death alone cannot acknowledge descendant completion");
            await RecoverChildrenAsync(fixture.Source, 3); // Preview retains even confirmed-dead records.
            await RecoverChildrenAsync(fixture.Source, 3, "-Execute"); // Descendant confirmation is required.
            await RecoverChildrenAsync(fixture.Source, 0, "-Execute", "-ConfirmDescendantsExited");
            await using var recovered = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
            recovered.ShouldNotBeNull("explicit recovery after the owned tree exits must restore repository admission");
            File.Exists(Path.Combine(common, "antiphon", "landing.lock")).ShouldBeTrue();
            unrelated.HasExited.ShouldBeFalse();
            await fixture.AssertRemoteSourceAsync();
        }
        finally
        {
            if (child is not null) { if (!child.HasExited) child.Kill(true); await child.WaitForExitAsync(); child.Dispose(); }
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            if (!unrelated.HasExited) unrelated.Kill(true);
            await unrelated.WaitForExitAsync();
        }
    }

    [Test]
    [Arguments("alive")]
    [Arguments("reused")]
    [Arguments("unknown")]
    [Arguments("malformed")]
    [Arguments("torn")]
    [Arguments("wrong-repository")]
    [Arguments("busy")]
    public async Task C448_D1_RecoveryPreservesLiveOrAmbiguousEvidence(string state)
    {
        using var repo = new ScratchGitRepo("antiphon-journal-recovery");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        var provider = new RepositoryMutationLease(git);
        await using var busy = state == "busy" ? await provider.TryAcquireAsync(repo.Path, CancellationToken.None) : null;
        var journal = await RepositoryChildJournal.BeginAsync(repo.Path, CancellationToken.None);
        using var child = StartSleeper();
        try
        {
            if (state != "unknown")
                await journal.StartedAsync(child.Id, child.StartTime.ToUniversalTime().Ticks + (state == "reused" ? 1 : 0), CancellationToken.None);
            var path = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
            if (state == "malformed") await File.WriteAllTextAsync(path, "invalid json");
            if (state == "torn") File.Move(path, path + ".tmp");
            if (state == "wrong-repository")
                await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(
                    new RepositoryChildJournal.ChildRecord(1, repo.WorktreeRoot, child.Id, child.StartTime.ToUniversalTime().Ticks)));
            var recoverable = state == "reused";
            await RecoverChildrenAsync(repo.Path, recoverable ? 0 : 3, "-Execute", "-ConfirmDescendantsExited");
            Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children")).Count().ShouldBe(recoverable ? 0 : 1);
            child.HasExited.ShouldBeFalse("recovery never kills a live or reused PID");
            await using var admission = await provider.TryAcquireAsync(repo.Path, CancellationToken.None);
            if (recoverable) admission.ShouldNotBeNull();
            else admission.ShouldBeNull();
        }
        finally
        {
            if (!child.HasExited) child.Kill(true);
            await child.WaitForExitAsync();
        }
    }

    // CARD-0661. A fast child's owner saw its exact handle exit before reading a start identity and
    // saved Completed=true with no StartTicks; the owner then crashed before draining its output.
    // The server keeps the record fencing admission (a restart cannot prove descendant exit), and
    // the explicit script recovers it only after -Execute -ConfirmDescendantsExited. It must never
    // look up the recorded PID's start time: that PID may be reused and Linux start times are
    // unstable across readers (CARD-0668). Incomplete or foreign completed records stay retained.
    [Test]
    [Arguments("completed")]
    [Arguments("completed-without-pid")]
    [Arguments("completed-wrong-repository")]
    public async Task C661_CompletedRecordIsRecoveredOnlyUnderExplicitConfirmation(string state)
    {
        using var repo = new ScratchGitRepo("antiphon-journal-completed");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        await RepositoryChildJournal.BeginAsync(repo.Path, CancellationToken.None);
        var start = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--version");
        int exitedId;
        using (var exited = Process.Start(start)!)
        {
            await exited.StandardOutput.ReadToEndAsync();
            await exited.WaitForExitAsync();
            exitedId = exited.Id;
        }
        var path = Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children"), "*.json").Single();
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new RepositoryChildJournal.ChildRecord(1,
            state == "completed-wrong-repository" ? repo.WorktreeRoot : common,
            state == "completed-without-pid" ? null : exitedId, null, Completed: true)));

        // Server path: every fresh provider (as after a restart) stays fenced and names the recovery.
        foreach (var provider in new[] { new RepositoryMutationLease(git), new RepositoryMutationLease(git) })
        {
            await using (var fenced = await provider.TryAcquireAsync(repo.Path, CancellationToken.None))
                fenced.ShouldBeNull("a completed root cannot prove its descendants exited");
            (await provider.DescribeUnavailableAsync(repo.Path, CancellationToken.None))
                .ShouldNotBeNull().ShouldContain("recover-repository-children.ps1");
        }
        File.Exists(path).ShouldBeTrue("the server never clears a child record on its own");

        // Script path: preview and unconfirmed execution retain it.
        await RecoverChildrenAsync(repo.Path, 3);
        await RecoverChildrenAsync(repo.Path, 3, "-Execute");
        File.Exists(path).ShouldBeTrue("recovery requires explicit descendant confirmation");

        var recoverable = state == "completed";
        await RecoverChildrenAsync(repo.Path, recoverable ? 0 : 3, "-Execute", "-ConfirmDescendantsExited");
        File.Exists(path).ShouldBe(!recoverable);
        await using var admission = await new RepositoryMutationLease(git).TryAcquireAsync(repo.Path, CancellationToken.None);
        if (recoverable) admission.ShouldNotBeNull("a confirmed completed record no longer fences the repository");
        else admission.ShouldBeNull("an incomplete or foreign completed record stays retained");
    }

    private static async Task RecoverChildrenAsync(string repository, int expectedExit, params string[] options)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Antiphon.sln"))) root = root.Parent;
        root.ShouldNotBeNull();
        await RunChildAsync("pwsh", ["-NoProfile", "-File",
            Path.Combine(root.FullName, "scripts", "recover-repository-children.ps1"), "-Repository", repository, .. options], expectedExit);
    }

    [Test]
    public async Task C448_V13_WindowsJunctionAndOtherProcessShareTheLease()
    {
        if (!OperatingSystem.IsWindows()) throw new TUnit.Core.Exceptions.SkipTestException("Requires Windows junctions");
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var alias = Path.Combine(fixture.Root, "alias");
        await RunChildAsync("cmd.exe", ["/c", "mklink", "/J", alias, fixture.Source], expectedExit: 0);
        try
        {
            (File.GetAttributes(alias) & FileAttributes.ReparsePoint).ShouldBe(FileAttributes.ReparsePoint);
            var provider = new RepositoryMutationLease(fixture.Git);
            await using var lease = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
            lease.ShouldNotBeNull();
            (await provider.TryAcquireAsync(alias, CancellationToken.None)).ShouldBeNull();
            var worker = Path.Combine(fixture.Root, "lease-worker.ps1");
            await File.WriteAllTextAsync(worker, """
                try {
                    $stream = [IO.File]::Open($args[0], [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                    $stream.Dispose()
                    exit 10
                } catch [IO.IOException] { exit 0 }
                """);
            await RunChildAsync("pwsh", ["-NoProfile", "-File", worker,
                Path.Combine(lease.CommonDirectory, "antiphon", "landing.lock")], expectedExit: 0);
            provider.Owns(lease, lease.CommonDirectory).ShouldBeTrue();
            await fixture.AssertRemoteSourceAsync();
        }
        finally { Directory.Delete(alias, recursive: false); }
    }

    private static async Task RunChildAsync(string executable, string[] arguments, int expectedExit)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(budget.Token); }
        catch (OperationCanceledException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw;
        }
        await Task.WhenAll(output, error);
        child.ExitCode.ShouldBe(expectedExit, await error);
    }

    [Test]
    public async Task C448_V13_LeaseUsesCommonRepositoryIdentity()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var provider = new RepositoryMutationLease(fixture.Git);
        var otherProvider = new RepositoryMutationLease(fixture.Git);
        var first = await provider.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        first.ShouldNotBeNull();
        await using (first)
        {
            provider.Owns(first, await fixture.Git.CommonDirectoryAsync(fixture.Source, CancellationToken.None)).ShouldBeTrue();
            otherProvider.Owns(first, first.CommonDirectory).ShouldBeFalse();
            (await otherProvider.TryAcquireAsync(fixture.Source, CancellationToken.None)).ShouldBeNull();
            await using var independent = await otherProvider.TryAcquireAsync(fixture.Remote, CancellationToken.None);
            independent.ShouldNotBeNull();
        }
        provider.Owns(first, first.CommonDirectory).ShouldBeFalse();
        File.Exists(Path.Combine(first.CommonDirectory, "antiphon", "landing.lock")).ShouldBeTrue();
        await using var reacquired = await otherProvider.TryAcquireAsync(fixture.Source, CancellationToken.None);
        reacquired.ShouldNotBeNull();
        await fixture.AssertRemoteSourceAsync();
    }
}
