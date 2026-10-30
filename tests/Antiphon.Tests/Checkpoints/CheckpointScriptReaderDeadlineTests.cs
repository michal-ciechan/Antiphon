using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1363: scripts/lib/checkpoint-file-reader.ps1 bounds a read by a stall window that every delivered chunk re-arms,
// and run-checkpoint.ps1 reports an entry the reader refused as unreadable, apart from tampered. The witness script paces
// a manual TimeProvider inside the stream it injects, so no reader case waits on the wall clock; the ledger cases run
// real git with a hard link on Linux and NTFS. A second link deterministically makes the
// entry unprovable without changing its bytes, privileges, ACLs, or handle sharing.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointScriptReaderDeadlineTests : CheckpointTestBase
{
    [Test]
    [Arguments("progress")]
    [Arguments("stall")]
    [Arguments("trickle")]
    [Arguments("unreadable")]
    [Arguments("tampered-beside-unreadable")]
    [Arguments("windows-open")]
    public async Task script_reader_re_arms_its_stall_window_and_the_stamp_reports_unreadable_apart_from_tampered(string mode)
    {
        var ledgerCase = mode is "unreadable" or "tampered-beside-unreadable";
        if (ledgerCase && !OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            Skip.Test("Hard-link ledger qualification requires Linux or Windows; the reader cases run on every platform");
        var root = TempDir();
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts/test-checkpoint-reader-deadline.ps1");
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "-NoProfile", "-File", script, "-Repository", CheckpointFixtures.RepoRoot, "-Scratch", root, "-Mode", mode })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        // The bound covers two Add-Type compilations and a 7.5 MiB virtual read on a loaded host; the clock inside is manual.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true); await process.WaitForExitAsync();
            throw new ShouldAssertException("HUNG: killed and awaited at the two-minute hard bound " + await stdout + await stderr);
        }
        var output = await stdout + await stderr;
        process.ExitCode.ShouldBe(0, output);
        output.ShouldContain("PASS " + mode, Case.Sensitive);
    }
}
