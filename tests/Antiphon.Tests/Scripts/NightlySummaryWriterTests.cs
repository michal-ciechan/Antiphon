using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-2005. A Full run summary is streamed. The same shape still exhausts a
/// capped ConvertTo-Json, and nightly-report accepts the streamed document.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlySummaryWriterTests
{
    [Test]
    [Timeout(180_000)]
    public async Task C2005_SummaryWriterStreamsWithinABoundedHeap()
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-nightly-summary-writer.ps1");
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            ArgumentList = { "-NoProfile", "-File", script },
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["ANTIPHON_LINUX_COMPILER_POLICY"] = "0";
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        process.ExitCode.ShouldBe(0, stdout + "\n" + stderr);
        stdout.ShouldContain("STRUCTURAL-CLEAN");
        stdout.ShouldContain("OLD-OOM");
        stdout.ShouldContain("NEW-BOUNDED");
        stdout.ShouldContain("PREFIX-OK");
        stdout.ShouldContain("UID-ONCE");
        stdout.ShouldContain("REPORT-ACCEPTED");
        stdout.ShouldContain("C2005-OK");
    }
}
