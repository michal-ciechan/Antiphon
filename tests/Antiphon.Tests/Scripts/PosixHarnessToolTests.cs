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
    [Arguments("Microsoft.Management.Infrastructure.Native.Unmanaged.dll")]
    [Arguments("microsoft.management.infrastructure.native.unmanaged.DLL")]
    public void Hidden_path_keeps_first_windows_name_and_still_excludes_banned_tools(string secondName)
    {
        var scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "posix-path-witness-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(scratch, "first")).FullName;
            var second = Directory.CreateDirectory(Path.Combine(scratch, "second")).FullName;
            const string name = "Microsoft.Management.Infrastructure.Native.Unmanaged.dll";
            File.WriteAllText(Path.Combine(first, name), "first PATH entry");
            File.WriteAllText(Path.Combine(second, secondName), "second PATH entry");
            string[] banned = ["chmod", "stat", "mkfifo", "ln", "cat", "grep", "tr"];
            foreach (var tool in banned)
            {
                File.WriteAllText(Path.Combine(first, tool), "blocked first");
                File.WriteAllText(Path.Combine(second, tool), "blocked second");
            }

            using var hidden = HiddenPath.FromDirectories([first, second], windows: true, banned);
            var links = Directory.GetFiles(hidden.DirectoryPath);
            links.Length.ShouldBe(1, "Windows PATH names collide regardless of case");
            new FileInfo(links[0]).ResolveLinkTarget(true)!.FullName.ShouldBe(Path.Combine(first, name));
            File.ReadAllText(links[0]).ShouldBe("first PATH entry");
            foreach (var tool in banned)
                File.Exists(Path.Combine(hidden.DirectoryPath, tool)).ShouldBeFalse("banned tool: " + tool);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public void Snapshot_cases_complete_without_chmod_or_stat_on_path()
        => RunSnapshotCases(OperatingSystem.IsWindows());

    [Test]
    public void Snapshot_windows_selection_runs_link_and_acl_assertions_without_posix_tools()
    {
        SnapshotCases(windows: true).ShouldBe(["StagingRefusesLink", "WindowsAclRules"],
            "Windows must run assertions instead of the script's POSIX-only SKIP cases");
        RunSnapshotCases(windows: true);
    }

    private static string[] SnapshotCases(bool windows)
        // The harness skips Unix ownership and extensionless sh fakes on Windows;
        // its ACL scenario exercises the Windows staging protection instead.
        => windows ? ["StagingRefusesLink", "WindowsAclRules"]
            : ["StagingRefusesLink", "StagingRefusesForeignRoot", "DrillRefusesIdPrefix"];

    private static void RunSnapshotCases(bool windows)
    {
        using var hidden = HiddenPath.Without("chmod", "stat");
        foreach (var scenario in SnapshotCases(windows))
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
        result.Stdout.ShouldContain("PASS capture-identity-exit-0");
        result.Stdout.ShouldContain("PASS capture-identity-exit-1");
        result.Stdout.ShouldContain("PASS capture-shell-resolver-windows-shape");
        result.Stdout.ShouldContain("PASS capture-shell-resolver-skips-bin-sh");
        result.Stdout.ShouldContain("PASS capture-shell-absent-skips");
        if (OperatingSystem.IsLinux())
            result.Stdout.ShouldContain("PASS capture-shell-seam-starts-resolved-shell");
        result.Stdout.ShouldContain("OK");
    }

    [Test]
    public void Capture_identity_resolves_git_sh_instead_of_hard_coded_bin_sh()
    {
        NativeLinuxGate.Require(
            "The Windows-shaped capture resolver witness executes a planted sh.exe script. Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path runs the production case on Windows through the same resolver (NativeLinuxGate, docs/testing-and-build.md).");
        using var hidden = HiddenPath.Without("mkfifo", "chmod", "ln");
        var result = Run("test-qualification-timing.ps1", hidden.DirectoryPath, 60_000, "-Case", "CaptureShell");
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
        result.Stdout.ShouldContain("PASS capture-shell-resolver-windows-shape");
        result.Stdout.ShouldContain("PASS capture-shell-resolver-skips-bin-sh");
        result.Stdout.ShouldContain("PASS capture-shell-seam-starts-resolved-shell");
        result.Stdout.ShouldContain("PASS capture-shell-absent-skips");
        result.Stdout.IndexOf("PASS capture-identity-exit-0", StringComparison.Ordinal).ShouldBeLessThan(0, result.Stdout);
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

    [Test]
    public void Cutover_window_completes_without_cat_grep_or_tr_on_path()
    {
        NativeLinuxGate.Require(
            "CARD-1407 window fakes are POSIX sh scripts; cat, grep and tr are external. Windows skips these cases because an extensionless sh fake is not a Windows executable.");
        using var hidden = HiddenPath.Without("cat", "grep", "tr");
        var result = Run("test-cutover-postgres-server2.ps1", hidden.DirectoryPath, 120_000, "-Case", "W6");
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
        AssertNoMissingCommand(result);
        result.Stdout.ShouldContain("PASS W-6");
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
            => FromDirectories((Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries), OperatingSystem.IsWindows(), banned);

        internal static HiddenPath FromDirectories(IEnumerable<string> directories, bool windows, params string[] banned)
        {
            var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
                "posix-hide-" + Guid.NewGuid().ToString("N")));
            var blocked = new HashSet<string>(banned, StringComparer.Ordinal);
            var seen = new HashSet<string>(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var directory in directories)
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
