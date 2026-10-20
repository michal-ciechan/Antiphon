using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandVerificationWindowNativeTests
{
    [Test]
    public async Task C1379_PreflightUsesARealReleasedMutationHandle()
    {
        Windows();
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        var leases = new RepositoryMutationLease(f.Git);
        await using var execution = await new LandExecutionLease(f.Git).TryAcquireAsync(f.Repository, CancellationToken.None);
        await using var mutation = await leases.TryAcquireAsync(f.Repository, CancellationToken.None);
        mutation.ShouldNotBeNull();
        execution.ShouldNotBeNull();
        await using var window = await leases.OpenVerificationWindowAsync(mutation, execution,
            Identity(f), CancellationToken.None);
        window.ShouldNotBeNull();
        var path = Path.Combine(mutation.CommonDirectory, "antiphon", "landing.lock");
        (await ProbeAsync(path)).ShouldBeFalse();
        await mutation.DisposeAsync();
        (await ProbeAsync(path)).ShouldBeTrue("an independent process sees the actual R handle close");
        await using var preflight = await leases.TryAcquirePreflightAsync(f.Repository,
            RepositoryPreflight.Fetch(Guid.NewGuid(), f.SeedSha), new(null, RepositoryLeasePurposes.StartRefFetch),
            CancellationToken.None);
        preflight.ShouldNotBeNull();
    }

    [Test]
    public async Task C1379_ExecutionHandleExcludesASecondLand()
    {
        Windows();
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        await using var execution = await new LandExecutionLease(f.Git).TryAcquireAsync(f.Repository, CancellationToken.None);
        execution.ShouldNotBeNull();
        var common = await f.Git.CommonDirectoryAsync(f.Repository, CancellationToken.None);
        var path = Path.Combine(common, "antiphon", "land-execution.lock");
        (await ProbeAsync(path)).ShouldBeFalse("the native handle excludes another process throughout verification");
        await execution.DisposeAsync();
        (await ProbeAsync(path)).ShouldBeTrue();
    }

    [Test]
    public async Task C1379_ExitedRootWithUnjoinedOutputStillFences()
    {
        Windows();
        foreach (var pipe in new[] { "stdout", "stderr" })
        {
            await using var f = new LandingGitFixture();
            await f.InitializeAsync();
            await using var child = new LandVerificationWindowHarness(DescendantScript.Replace("__PIPE__", pipe));
            var leases = new RepositoryMutationLease(f.Git);
            await using var execution = await new LandExecutionLease(f.Git).TryAcquireAsync(f.Repository, CancellationToken.None);
            await using var mutation = await leases.TryAcquireAsync(f.Repository, CancellationToken.None);
            mutation.ShouldNotBeNull();
            execution.ShouldNotBeNull();
            var identity = Identity(f);
            await using var window = await leases.OpenVerificationWindowAsync(mutation, execution, identity, CancellationToken.None);
            window.ShouldNotBeNull();
            var common = mutation.CommonDirectory;
            await mutation.DisposeAsync();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var verify = child.Verifier.VerifyAsync(f.Source, null,
                new(f.TaskId, identity.OperationId, identity.RequestId, CommonDirectory: common, WindowId: window.Id),
                budget.Token);
            Process? descendant = null;
            try
            {
                await child.Started.Task.WaitAsync(budget.Token);
                await child.RootProcess!.WaitForExitAsync(budget.Token);
                using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(child.ReleasePath + ".child.json", budget.Token));
                descendant = Process.GetProcessById(ready.RootElement.GetProperty("Pid").GetInt32());
                descendant.StartTime.ToUniversalTime().Ticks.ShouldBe(ready.RootElement.GetProperty("StartTicks").GetInt64());
                descendant.HasExited.ShouldBeFalse();
                verify.IsCompleted.ShouldBeFalse(pipe + " remains owned by the live descendant");
                await using var ordinary = await leases.TryAcquireAsync(f.Repository, budget.Token);
                ordinary.ShouldBeNull();
                await using var scoped = await leases.TryAcquirePreflightAsync(f.Repository,
                    RepositoryPreflight.Fetch(Guid.NewGuid(), f.SeedSha),
                    new(null, RepositoryLeasePurposes.StartRefFetch), budget.Token);
                scoped.ShouldBeNull("an exited verifier root is never a live compatible journal");
            }
            finally
            {
                child.Release();
                if (descendant is not null)
                {
                    await descendant.WaitForExitAsync(CancellationToken.None);
                    descendant.Dispose();
                }
                await verify;
            }
        }
    }

    private static RepositoryVerificationIdentity Identity(LandingGitFixture f) =>
        new(f.TaskId, Guid.NewGuid(), 1, Guid.NewGuid(), f.SeedSha, f.Source, f.Repository, f.SourceRef, f.TargetRef);

    private static void Windows()
    {
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("Windows native handle qualification; separate CP-9 lane");
    }

    private static async Task<bool> ProbeAsync(string path)
    {
        var script = Path.Combine(Path.GetDirectoryName(path)!, "c1379-probe-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(script,
            "param($p) try { $f=[IO.File]::Open($p,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $f.Dispose(); exit 0 } catch [IO.IOException] { exit 17 }");
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-File", script, path,
                 }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            File.Delete(script);
        }
        process.ExitCode.ShouldBeOneOf(0, 17);
        return process.ExitCode == 0;
    }

    private const string DescendantScript = """
        param([string]$Release, [string]$Results)
        $pipe = '__PIPE__'
        $childScript = Join-Path $PSScriptRoot 'descendant.ps1'
        [IO.File]::WriteAllText($childScript, @'
        param($release, $ready, $pipe)
        $p = [Diagnostics.Process]::GetCurrentProcess()
        [IO.File]::WriteAllText($ready + '.tmp', (@{ Pid=$p.Id; StartTicks=$p.StartTime.ToUniversalTime().Ticks } | ConvertTo-Json -Compress))
        [IO.File]::Move($ready + '.tmp', $ready)
        while (-not (Test-Path -LiteralPath $release)) { Start-Sleep -Milliseconds 20 }
        if ($pipe -eq 'stdout') { [Console]::Out.WriteLine('joined') } else { [Console]::Error.WriteLine('joined') }
        '@)
        Add-Type -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        public static class NativePipeHandles {
            [DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int which);
            [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);
        }
        '@
        $other = if ($pipe -eq 'stdout') { -12 } else { -11 }
        if (-not [NativePipeHandles]::SetHandleInformation([NativePipeHandles]::GetStdHandle($other), 1, 0)) { throw 'fixture inheritance failed' }
        $start = [Diagnostics.ProcessStartInfo]::new('pwsh')
        $start.UseShellExecute = $false
        foreach ($arg in @('-NoProfile','-File',$childScript,$Release,($Release + '.child.json'),$pipe)) { $start.ArgumentList.Add($arg) }
        [Diagnostics.Process]::Start($start) | Out-Null
        while (-not (Test-Path -LiteralPath ($Release + '.child.json'))) { Start-Sleep -Milliseconds 20 }
        exit 0
        """;
}
