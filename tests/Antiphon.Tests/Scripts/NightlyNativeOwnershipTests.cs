using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyNativeOwnershipTests
{
    [Test]
    public Task C1039_AssignBeforeResume() => RunAsync(nameof(C1039_AssignBeforeResume));
    [Test]
    public Task C1039_DescendantExit() => RunAsync(nameof(C1039_DescendantExit));
    [Test]
    public Task C1039_DrainBeforeReturn() => RunAsync(nameof(C1039_DrainBeforeReturn));

    private static async Task RunAsync(string name)
    {
        OperatingSystem.IsWindows().ShouldBeTrue("This native checkpoint requires Windows; missing prerequisites are not skips.");
        var fixture = Path.Combine(AppContext.BaseDirectory, "nightly-owned-child", "OwnedChild.exe");
        File.Exists(fixture).ShouldBeTrue("The checkpoint build must stage the executable, never compile inside the test.");
        var root = Path.Combine(Path.GetTempPath(), "c1039-native-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-nightly-native.ps1"),
                     "-Case", name, "-ResultsDirectory", root, "-FixtureExecutable", fixture }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Native harness failed to start.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;
            process.ExitCode.ShouldBe(0, output + "\nRetained evidence: " + root);
            output.ShouldContain("C1039 NATIVE " + name, Case.Sensitive);
            output.ShouldNotContain("FAIL C1039", Case.Sensitive);
            Directory.Delete(root, recursive: true);
        }
        finally
        {
            // The harness owns/join its fixture handles in finally. This bounded fallback
            // kills its exact tree; the adapter's kill-on-close job contains descendants.
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}
