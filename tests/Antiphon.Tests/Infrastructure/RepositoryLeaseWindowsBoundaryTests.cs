using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-1819. These cases skip off Windows. A Linux selector row that names them is red.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryLeaseWindowsBoundaryTests
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
}
