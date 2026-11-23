using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointLargeStampScriptTests : CheckpointTestBase
{
    [Test]
    public Task C1532_script_stamp_matches_full_digest() => Run("stamp");

    [Test]
    public Task C1532_script_digest_limits_stalls_and_eof() => Run("reader");

    // The stamp scripts stay ASCII. Whether a PowerShell parser accepts them is a host fact, not a C# one: every row
    // that runs them parses them under pwsh 7, which they require, so a Windows PowerShell 5.1 parse proves nothing.
    [Test]
    [Arguments("scripts/run-checkpoint.ps1")]
    [Arguments("scripts/lib/checkpoint-file-reader.ps1")]
    [Arguments("scripts/test-checkpoint-large-stamp.ps1")]
    public void C1532_stamp_scripts_are_ascii(string relative)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CheckpointFixtures.RepoRoot, relative));
        Array.FindIndex(bytes, static b => b > 127).ShouldBe(-1, relative + " has a non-ASCII byte");
    }

    private async Task Run(string mode)
    {
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(CheckpointFixtures.RepoRoot, "scripts/test-checkpoint-large-stamp.ps1"),
            "-Root", TempDir(), "-Repository", CheckpointFixtures.RepoRoot, "-ToolDll", typeof(Antiphon.Checkpoints.Program).Assembly.Location, "-Mode", mode }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new ShouldAssertException("large stamp harness exceeded 90 seconds"); }
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
        (await stdout).ShouldContain("C1532 " + mode + " PASS");
    }
}
