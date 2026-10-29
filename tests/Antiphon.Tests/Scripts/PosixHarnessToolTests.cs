using System.Diagnostics;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// PATH is a directory of symlinks to every current tool except the named POSIX
// binaries. The harnesses still resolve pwsh and git; an empty PATH fails for
// that reason and would not prove the chmod/stat/mkfifo/ln calls.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PosixHarnessToolTests
{
    [Test]
    public void Snapshot_cases_complete_without_chmod_or_stat_on_path()
    {
        using var hidden = HiddenPath.Without("chmod", "stat");
        foreach (var scenario in new[] { "StagingRefusesLink", "StagingRefusesForeignRoot", "DrillRefusesIdPrefix" })
        {
            var result = Run("test-postgres-snapshot.ps1", hidden.DirectoryPath, 90_000, "-Case", scenario);
            result.Exit.ShouldBe(0, scenario + "\n" + result.Stdout + result.Stderr);
            AssertNoMissingCommand(result);
            result.Stdout.ShouldContain("PASS ");
        }
    }

    [Test]
    public void Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path()
    {
        using var hidden = HiddenPath.Without("mkfifo", "chmod", "ln");
        var result = Run("test-qualification-timing.ps1", hidden.DirectoryPath, 180_000);
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
        AssertNoMissingCommand(result);
        result.Stdout.ShouldContain("OK");
    }

    [Test]
    public void Cutover_w1_completes_without_chmod_on_path()
    {
        using var hidden = HiddenPath.Without("chmod");
        var result = Run("test-cutover-postgres-server2.ps1", hidden.DirectoryPath, 60_000, "-Case", "W1");
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
        AssertNoMissingCommand(result);
        result.Stdout.ShouldContain("PASS W-1");
    }

    private static void AssertNoMissingCommand(CheckpointFixtureProcess.Output result)
    {
        var text = result.Stdout + result.Stderr;
        text.IndexOf("command not found", StringComparison.Ordinal).ShouldBeLessThan(0, text);
        text.IndexOf("is not recognized", StringComparison.Ordinal).ShouldBeLessThan(0, text);
    }

    private static CheckpointFixtureProcess.Output Run(string script, string path, int timeoutMs, params string[] extra)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows()
            ? ScriptHarnessProcessFixture.ResolveInstalledPowerShell() : ResolveTool("pwsh"));
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(CheckpointFixtures.RepoRoot, "scripts", script));
        foreach (var argument in extra)
            start.ArgumentList.Add(argument);
        start.Environment["PATH"] = path;
        return CheckpointFixtureProcess.Run(start, timeoutMs);
    }

    private static string ResolveTool(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException(name);
    }

    private sealed class HiddenPath : IDisposable
    {
        private HiddenPath(string directoryPath) => DirectoryPath = directoryPath;

        public string DirectoryPath { get; }

        public static HiddenPath Without(params string[] banned)
        {
            var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
                "posix-hide-" + Guid.NewGuid().ToString("N")));
            var blocked = new HashSet<string>(banned, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Directory.Exists(directory)) continue;
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(directory);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    if (blocked.Contains(name) || !seen.Add(name)) continue;
                    File.CreateSymbolicLink(Path.Combine(root.FullName, name), file);
                }
            }

            return new HiddenPath(root.FullName);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
