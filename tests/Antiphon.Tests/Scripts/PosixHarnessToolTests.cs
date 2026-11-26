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

    // Unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the CARD-1405 snapshot cases ran
    // scripts/test-postgres-snapshot.ps1 here with chmod and stat hidden. The snapshot is C# now (PostgresSnapshotTests): it reads
    // owners and modes through geteuid and statx and creates owner-only directories through the .NET mode API, so it starts no
    // POSIX tool at all, and its cases run in process on both systems.

    // W2-nightly S16: the qualification timing witnesses (the capture identity, the capture shell resolver and the FIFO,
    // mode and link cases) are in-process QualificationTimingTests; they make FIFOs and set modes through libc and start
    // no POSIX tool, so they need no hidden PATH here.

    // Unit 55 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the CARD-1407 cutover cases W1 (the -WhatIf
    // preview with chmod hidden) and W6 (the landPending refusal and hold release with cat, grep and tr hidden) ran
    // scripts/test-cutover-postgres-server2.ps1 here; no test launches a .ps1. Both checked only that one-off PowerShell script
    // and its POSIX sh fakes, which stand in for the restart, maintenance, keep-six and repository-preview launchers, so they
    // cover no C#: AppHostRestartTests, AppHostWatchdogStateObserverScriptTests, KeepSixCommandTests and
    // RepositoryChildRecoveryCommandTests run those commands. The cutover ran on 2026-11-18; the owner keeps the script and
    // its driver as PowerShell (2026-11-26), and the driver stays runnable by hand.

    private sealed class HiddenPath : IDisposable
    {
        private HiddenPath(string directoryPath) => DirectoryPath = directoryPath;

        public string DirectoryPath { get; }

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
