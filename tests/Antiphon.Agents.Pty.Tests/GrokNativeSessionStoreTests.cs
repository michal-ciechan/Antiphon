using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class GrokNativeSessionStoreTests
{
    [Test]
    public void Probe_distinguishes_missing_found_and_unavailable_storage()
    {
        var home = Path.Combine(Path.GetTempPath(), $"c466-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        var id = Guid.NewGuid();
        try
        {
            GrokNativeSessionStore.Probe(home, id).ShouldBe(NativeSessionPresence.Missing);
            File.WriteAllText(Path.Combine(home, "sessions"), "synthetic non-directory storage failure");
            GrokNativeSessionStore.Probe(home, id).ShouldBe(NativeSessionPresence.Unavailable);
            File.Delete(Path.Combine(home, "sessions"));
            Directory.CreateDirectory(Path.Combine(home, "sessions", "foreign-cwd-encoding", id.ToString("D")));
            GrokNativeSessionStore.Probe(home, id).ShouldBe(NativeSessionPresence.Found);
            GrokNativeSessionStore.Probe(home, Guid.NewGuid()).ShouldBe(NativeSessionPresence.Missing);
        }
        finally { Directory.Delete(home, recursive: true); }
    }
}
