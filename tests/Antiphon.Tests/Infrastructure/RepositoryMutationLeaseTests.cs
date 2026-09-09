using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
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
