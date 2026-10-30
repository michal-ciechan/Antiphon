using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// First method includes the once-per-session image preparation (120s provisional)
// and its native body (180s). CARD-1609 owns collection of normal phase samples.
[Timeout(300_000)]
[Category("Integration")]
[Category("Slow")]
[ClassDataSource<VolumeRetirementDockerEnvironment>(Shared = SharedType.PerTestSession)]
public sealed class VolumeRetirementDockerTests(VolumeRetirementDockerEnvironment environment)
{
    internal const string NativeLinuxReason = "CARD-1609: native Docker retirement witnesses require Linux inside the owned daemon; Windows skips explicitly.";

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task Read_only_namespace_and_host_proof_are_real(CancellationToken cancellationToken)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await environment.RunAsync("N1", cancellationToken);
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task Replacement_and_concurrent_reference_block_delete(CancellationToken cancellationToken)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await environment.RunAsync("N2", cancellationToken);
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task Exact_deletion_and_crash_resume_preserve_other_volumes(CancellationToken cancellationToken)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await environment.RunAsync("N3", cancellationToken);
    }
}

// Construction does no IO: Windows reaches the first-statement gate before work.
// TUnit owns/disposes this shared source after the selected native methods.
public sealed class VolumeRetirementDockerEnvironment : IAsyncDisposable
{
    private string? _root;

    public async Task RunAsync(string nativeCase, CancellationToken cancellationToken)
    {
        if (_root is null)
        {
            using var setup = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "prepare");
            var prepared = await InvokeAsync(["--prepare"], 120, cancellationToken);
            var match = Regex.Match(prepared.Output, @"(?m)^C1609_REAL_ROOT=(/tmp/c1609-real-[A-Za-z0-9]+)$");
            if (match.Success) _root = match.Groups[1].Value;
            prepared.Exit.ShouldBe(0, "native preparation is not an assertion-red witness: " + prepared.Output);
            _root.ShouldNotBeNull(prepared.Output);
        }
        var result = await InvokeAsync(["--case", nativeCase, "--root", _root!], 180, cancellationToken);
        Console.WriteLine(result.Output);
        result.Exit.ShouldBe(0, nativeCase + " production retirement witness: " + result.Output);
        result.Output.ShouldContain("C1609_NATIVE " + nativeCase + " passed cleanup=absent");
    }

    public async ValueTask DisposeAsync()
    {
        if (_root is null) return;
        using var teardown = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "cleanup");
        var cleaned = await InvokeAsync(["--cleanup", "--root", _root], 60, CancellationToken.None);
        Console.WriteLine(cleaned.Output);
        cleaned.Exit.ShouldBe(0, "owned native cleanup: " + cleaned.Output);
        cleaned.Output.ShouldContain("C1609_NATIVE session cleanup=absent");
    }

    private static async Task<(int Exit, string Output)> InvokeAsync(string[] args, int seconds, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("node")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = DelegateScriptRunner.RepoRoot
        };
        psi.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1609-retirement-real-cases.mjs"));
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var span = TestTiming.BeginProcess("node", psi.ArgumentList.Count, "cli");
        using var child = Process.Start(psi)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
            throw;
        }
        return (child.ExitCode, await stdout + await stderr);
    }
}
