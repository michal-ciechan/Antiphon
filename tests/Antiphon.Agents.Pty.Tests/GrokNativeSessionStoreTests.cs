using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

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
    public void Probe_reports_unavailable_for_Windows_non_directory_storage()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("Windows file-as-directory I/O failure premise; Unix reports DirectoryNotFound (Missing)");
        var home = Path.Combine(Path.GetTempPath(), $"c466-unavailable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        try
        {
            File.WriteAllText(Path.Combine(home, "sessions"), "synthetic non-directory storage failure");
            GrokNativeSessionStore.Probe(home, Guid.NewGuid()).ShouldBe(NativeSessionPresence.Unavailable);
        }
        finally { Directory.Delete(home, recursive: true); }
    }
}
