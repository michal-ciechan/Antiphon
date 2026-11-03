using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class GrokNativeSessionStoreTests
{
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
    public void Probe_reports_unavailable_for_a_non_directory_at_any_level_above_the_store(string level, string kind)
    {
        // CARD-1675: Windows and Unix both report a file at GROK_HOME or above as a missing path;
        // the first existing ancestor of {GROK_HOME}/sessions being a non-directory is unreadable
        // storage, never missing history. A dangling link is a store whose target cannot be read.
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
            // Requires Windows file-symlink privilege; a fixture failure is an explicit environmental
            // failure, never a pass.
            switch (kind)
            {
                case "file": File.Copy(real, blocker); break;
                case "file-link": File.CreateSymbolicLink(blocker, real); break;
                default: File.CreateSymbolicLink(blocker, Path.Combine(root, "absent")); break;
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
}
