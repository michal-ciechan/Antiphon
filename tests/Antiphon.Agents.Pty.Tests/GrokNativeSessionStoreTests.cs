using System.Diagnostics;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class GrokNativeSessionStoreTests
{
    private const string SymlinkPrivilegeSkipReason =
        "CARD-1675: link witnesses require SeCreateSymbolicLinkPrivilege on Windows "
        + "(ERROR_PRIVILEGE_NOT_HELD, 1314); plain-file cases still run.";

    private const string JunctionSkipReason =
        "CARD-1675: directory junctions exist only on Windows; the dangling directory symlink and walk seam cases cover Linux.";

    [Test]
    public void Probe_distinguishes_missing_and_found_storage()
    {
        var home = Path.Combine(Path.GetTempPath(), $"c466-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        var id = Guid.NewGuid();
        try
        {
            GrokNativeSessionStore.Probe(home, id).ShouldBe(NativeSessionPresence.Missing);
            Directory.CreateDirectory(Path.Combine(home, "sessions", "foreign-cwd-encoding", id.ToString("D")));
            GrokNativeSessionStore.Probe(home, id).ShouldBe(NativeSessionPresence.Found);
            GrokNativeSessionStore.Probe(home, Guid.NewGuid()).ShouldBe(NativeSessionPresence.Missing);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Test]
    public void Probe_reports_unavailable_for_non_directory_storage()
    {
        // CARD-1669: Unix maps ENOTDIR to DirectoryNotFoundException; a file at the sessions root is
        // still unreadable storage, never missing history.
        var home = Path.Combine(Path.GetTempPath(), $"c466-unavailable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        try
        {
            File.WriteAllText(Path.Combine(home, "sessions"), "synthetic non-directory storage failure");
            GrokNativeSessionStore.Probe(home, Guid.NewGuid()).ShouldBe(NativeSessionPresence.Unavailable);
        }
        finally { Directory.Delete(home, recursive: true); }
    }
    [Test]
    [Arguments("sessions", "file")]
    [Arguments("sessions", "file-link")]
    [Arguments("sessions", "dangling-link")]
    [Arguments("home", "file")]
    [Arguments("home", "file-link")]
    [Arguments("home", "dangling-link")]
    [Arguments("parent", "file")]
    [Arguments("parent", "file-link")]
    [Arguments("parent", "dangling-link")]
    [Arguments("sessions", "dangling-dir-link")]
    [Arguments("home", "dangling-dir-link")]
    [Arguments("parent", "dangling-dir-link")]
    public void Probe_reports_unavailable_for_a_non_directory_at_any_level_above_the_store(string level, string kind)
    {
        // CARD-1675: Windows and Unix both report a file at GROK_HOME or above as a missing path;
        // the first existing ancestor of {GROK_HOME}/sessions being a non-directory is unreadable
        // storage, never missing history. A dangling link is a store whose target cannot be read;
        // Windows reports a directory link to a missing directory as an existing directory.
        var root = Path.Combine(Path.GetTempPath(), $"c1675-ancestor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var (home, blocker) = level switch
            {
                "sessions" => (root, Path.Combine(root, "sessions")),
                "home" => (Path.Combine(root, "home"), Path.Combine(root, "home")),
                _ => (Path.Combine(root, "parent", "home"), Path.Combine(root, "parent")),
            };
            var real = Path.Combine(root, "real.txt");
            File.WriteAllText(real, "synthetic non-directory storage failure");
            try
            {
                switch (kind)
                {
                    case "file": File.Copy(real, blocker); break;
                    case "file-link": File.CreateSymbolicLink(blocker, real); break;
                    case "dangling-link": File.CreateSymbolicLink(blocker, Path.Combine(root, "absent")); break;
                    default: Directory.CreateSymbolicLink(blocker, Path.Combine(root, "absent-dir")); break;
                }
            }
            catch (Exception ex) when (kind != "file" && IsMissingSymlinkPrivilege(ex, OperatingSystem.IsWindows()))
            {
                Skip.Test(SymlinkPrivilegeSkipReason);
                return;
            }
            GrokNativeSessionStore.Probe(home, Guid.NewGuid()).ShouldBe(NativeSessionPresence.Unavailable);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Probe_reports_missing_for_a_missing_chain_of_directories()
    {
        // CARD-1675: walking up past absent directories to an existing directory stays Missing.
        var root = Path.Combine(Path.GetTempPath(), $"c1675-chain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            GrokNativeSessionStore.Probe(Path.Combine(root, "parent", "home"), Guid.NewGuid()).ShouldBe(NativeSessionPresence.Missing);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    [Arguments("sessions")]
    [Arguments("home")]
    [Arguments("parent")]
    public void Probe_reports_unavailable_for_a_junction_to_a_missing_directory(string level)
    {
        // CARD-1675: a junction needs no privilege, and Windows reports a junction whose target is
        // gone as an existing directory; unreachable storage is never a fresh-start location.
        if (!OperatingSystem.IsWindows())
            Skip.Test(JunctionSkipReason);
        var root = Path.Combine(Path.GetTempPath(), $"c1675-junction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var (home, blocker) = level switch
            {
                "sessions" => (root, Path.Combine(root, "sessions")),
                "home" => (Path.Combine(root, "home"), Path.Combine(root, "home")),
                _ => (Path.Combine(root, "parent", "home"), Path.Combine(root, "parent")),
            };
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            CreateJunction(blocker, target);
            GrokNativeSessionStore.Probe(home, Guid.NewGuid()).ShouldBe(NativeSessionPresence.Missing, "junction-to-existing-directory");
            Directory.Delete(target);
            GrokNativeSessionStore.Probe(home, Guid.NewGuid()).ShouldBe(NativeSessionPresence.Unavailable, "junction-to-missing-directory");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments("sessions")]
    [Arguments("home")]
    [Arguments("parent")]
    public void Walk_treats_a_directory_whose_link_target_is_absent_as_unavailable(string level)
    {
        // CARD-1675: models the Windows shape (Directory.Exists true, File.Exists false for a link or
        // junction to a missing directory) so every OS proves the walk consults the link target.
        var root = Path.Combine("store-root", "c1675");
        var sessions = Path.Combine(root, "parent", "home", "sessions");
        var linked = level switch
        {
            "sessions" => sessions,
            "home" => Path.Combine(root, "parent", "home"),
            _ => Path.Combine(root, "parent"),
        };
        bool DirectoryExists(string path) => path == linked || linked.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        GrokNativeSessionStore.FirstExistingAncestorIsNonDirectory(sessions, _ => false, DirectoryExists, path => path == linked)
            .ShouldBeTrue("broken-directory-link-unavailable");
        GrokNativeSessionStore.FirstExistingAncestorIsNonDirectory(sessions, _ => false, DirectoryExists, _ => false)
            .ShouldBeFalse("reachable-directory-link-missing");
        GrokNativeSessionStore.FirstExistingAncestorIsNonDirectory(sessions, path => path == linked, _ => false, _ => false)
            .ShouldBeTrue("file-ancestor-unavailable");
    }

    [Test]
    public void Link_target_check_reads_only_an_unreachable_link_as_absent()
    {
        // CARD-1675: a plain directory or a link to an existing directory is a store; a dangling link
        // or a link loop is not, and the check never throws.
        var root = Path.Combine(Path.GetTempPath(), $"c1675-target-{Guid.NewGuid():N}");
        var plain = Path.Combine(root, "plain");
        Directory.CreateDirectory(plain);
        try
        {
            GrokNativeSessionStore.LinkTargetIsAbsent(plain).ShouldBeFalse("plain-directory");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "to-plain"), plain);
                Directory.CreateSymbolicLink(Path.Combine(root, "dangling"), Path.Combine(root, "absent"));
                Directory.CreateSymbolicLink(Path.Combine(root, "chain"), Path.Combine(root, "dangling"));
                Directory.CreateSymbolicLink(Path.Combine(root, "loop-a"), Path.Combine(root, "loop-b"));
                Directory.CreateSymbolicLink(Path.Combine(root, "loop-b"), Path.Combine(root, "loop-a"));
            }
            catch (Exception ex) when (IsMissingSymlinkPrivilege(ex, OperatingSystem.IsWindows()))
            {
                Skip.Test(SymlinkPrivilegeSkipReason);
                return;
            }
            GrokNativeSessionStore.LinkTargetIsAbsent(Path.Combine(root, "to-plain")).ShouldBeFalse("link-to-existing-directory");
            GrokNativeSessionStore.LinkTargetIsAbsent(Path.Combine(root, "dangling")).ShouldBeTrue("dangling-directory-link");
            GrokNativeSessionStore.LinkTargetIsAbsent(Path.Combine(root, "chain")).ShouldBeTrue("chain-to-dangling-link");
            GrokNativeSessionStore.LinkTargetIsAbsent(Path.Combine(root, "loop-a")).ShouldBeTrue("link-loop");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Symlink_privilege_skip_recognises_only_the_windows_privilege_error()
    {
        // CARD-1675 (CARD-1028 idiom): exercise the skip predicate on every host.
        var io = new IOException("privilege missing", unchecked((int)0x80070522));
        var denied = new UnauthorizedAccessException { HResult = unchecked((int)0x80070522) };
        IsMissingSymlinkPrivilege(io, true).ShouldBeTrue("symlink-privilege-io-recognized");
        IsMissingSymlinkPrivilege(denied, true).ShouldBeTrue("symlink-privilege-unauthorized-recognized");
        IsMissingSymlinkPrivilege(new System.ComponentModel.Win32Exception(1314), true).ShouldBeTrue("symlink-privilege-win32-recognized");
        IsMissingSymlinkPrivilege(io, false).ShouldBeFalse("symlink-linux-errors-propagate");
        IsMissingSymlinkPrivilege(denied, false).ShouldBeFalse("symlink-linux-denial-propagates");
        IsMissingSymlinkPrivilege(new IOException("A required privilege is not held by the client"), true)
            .ShouldBeFalse("symlink-message-alone-does-not-skip");
        IsMissingSymlinkPrivilege(new UnauthorizedAccessException(), true).ShouldBeFalse("symlink-access-denied-does-not-skip");
        IsMissingSymlinkPrivilege(new IOException("other Win32 error", unchecked((int)0x80070521)), true)
            .ShouldBeFalse("symlink-other-win32-error-does-not-skip");
        IsMissingSymlinkPrivilege(new IOException("other facility", unchecked((int)0x80040522)), true)
            .ShouldBeFalse("symlink-other-facility-does-not-skip");
        IsMissingSymlinkPrivilege(new InvalidOperationException { HResult = unchecked((int)0x80070522) }, true)
            .ShouldBeFalse("symlink-other-exception-type-does-not-skip");
    }

    private static bool IsMissingSymlinkPrivilege(Exception error, bool windows) =>
        windows && (error is IOException { HResult: unchecked((int)0x80070522) } // HRESULT_FROM_WIN32(ERROR_PRIVILEGE_NOT_HELD)
            or UnauthorizedAccessException { HResult: unchecked((int)0x80070522) }
            or System.ComponentModel.Win32Exception { NativeErrorCode: 1314 });

    private static void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("cmd.exe did not start for mklink /J");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        output.Wait();
        if (process.ExitCode != 0 || !File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("mklink /J failed: " + error);
    }
}
