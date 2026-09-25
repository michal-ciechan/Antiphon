using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Shouldly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0585 S6 (D-7): the C487-style harness wrapper, lifted out of
/// <see cref="NightlyVerificationContractTests"/> so <see cref="RunCheckpointScriptTests"/> reuses
/// the same body and cleanup. The default timeout is 120 s; longer cases supply their own budget.
/// </summary>
internal static class ScriptHarness
{
    /// <summary>Runs one named case of a C487-style harness and requires its exact PASS inventory for <paramref name="prefix"/>.</summary>
    internal static Task RunHarnessCaseAsync(string harness, string prefix, string caseName, int expectedRows, params string[] requiredRows)
        => RunHarnessCaseAsync(harness, prefix, caseName, expectedRows, requiredRows, 120);

    internal static async Task RunHarnessCaseAsync(string harness, string prefix, string caseName, int expectedRows, string[] requiredRows, int timeoutSeconds)
    {
        var results = Path.Combine(Path.GetTempPath(), prefix.ToLowerInvariant() + "-nightly-" + Guid.NewGuid().ToString("N"));
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", harness);
        var startInfo = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Case", caseName, "-ResultsDirectory", results })
            startInfo.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("pwsh did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;

            process.ExitCode.ShouldBe(0, output);
            output.ShouldContain("C487 HARNESS EXIT CODE: 0", Case.Sensitive, output);
            var lines = output.ReplaceLineEndings("\n").Split('\n');
            lines.ShouldNotContain(l => l.StartsWith("FAIL ", StringComparison.Ordinal), output);
            var passed = lines.Where(l => l.StartsWith("PASS " + prefix + " ", StringComparison.Ordinal)).Select(l => l[5..]).ToList();
            passed.Count.ShouldBe(expectedRows, $"{caseName} named assertion inventory\n{output}");
            foreach (var row in requiredRows)
                passed.ShouldContain(row, $"{caseName} must assert '{row}'\n{output}");
            Regex.IsMatch(output, $@"C487: {expectedRows} passed, 0 failed, {expectedRows} rows").ShouldBeTrue(output);
        }
        finally
        {
            try { Directory.Delete(results, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
