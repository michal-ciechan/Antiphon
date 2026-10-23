using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Observability.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class StartupScriptTests
{
    [Test]
    public async Task V10_Powershell_preserves_values_and_failures_without_logging_payloads(CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "scripts/test-live-startup-timing.ps1"))) root = root.Parent;
        root.ShouldNotBeNull();
        var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(root!.FullName, "scripts/test-live-startup-timing.ps1") }) psi.ArgumentList.Add(arg);
        // This fixture runs pure PowerShell actions and starts no descendants.
        using var process = Process.Start(psi)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(stdout, stderr);
            process.ExitCode.ShouldBe(0, await stderr);
            (await stdout).ShouldContain("6 assertions passed; 2 measured phases");
            Console.WriteLine(await stdout);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }
}
