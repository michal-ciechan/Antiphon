using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1691 H1/H2: every ScriptHarness case passes -ResultsDirectory, so only a direct call
// reaches New-C487Root's default. That default must be a fresh root under the process temp
// directory, never inside the repository (its fixture repositories would nest in the worktree),
// and must not need TEMP, which Linux login shells leave unset.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C487HarnessRootDefaultTests
{
    private const string Probe = """
        $ErrorActionPreference = 'Stop'
        . (Join-Path $env:C1691_LIB 'nightly-common.ps1')
        . (Join-Path $env:C1691_LIB 'c487-harness.ps1')
        $root = New-C487Root
        'TEMP=' + (ConvertTo-NightlyCanonicalPath ([IO.Path]::GetTempPath()))
        'ROOT=' + $root
        """;

    [Test]
    public async Task C1590_DefaultRootIsUnderTempWithoutTempVariable()
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", Probe }) start.ArgumentList.Add(arg);
        start.Environment["C1691_LIB"] = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib");
        // Windows keeps TMP, so GetTempPath still answers; Linux answers TMPDIR or /tmp.
        start.Environment.Remove("TEMP");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        var output = await stdout + await stderr;
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        var root = lines.SingleOrDefault(line => line.StartsWith("ROOT=", StringComparison.Ordinal))?["ROOT=".Length..];
        try
        {
            process.ExitCode.ShouldBe(0, output);
            root.ShouldNotBeNullOrWhiteSpace(output);
            var temp = lines.Single(line => line.StartsWith("TEMP=", StringComparison.Ordinal))["TEMP=".Length..];
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            root.StartsWith(Path.TrimEndingDirectorySeparator(temp) + Path.DirectorySeparatorChar, comparison).ShouldBeTrue(output);
            var repo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DelegateScriptRunner.RepoRoot));
            Path.GetFullPath(root).StartsWith(repo + Path.DirectorySeparatorChar, comparison).ShouldBeFalse(output);
            Directory.Exists(root).ShouldBeTrue(output);
            Directory.EnumerateFileSystemEntries(root).ShouldBeEmpty(output);
        }
        finally
        {
            // The probe creates one empty directory; remove exactly that path, wherever a mutant put it.
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
                Directory.Delete(root);
        }
    }
}
