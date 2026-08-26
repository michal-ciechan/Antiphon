using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>Guard rail for CARD-0118: headed real-service launches never inherit ~/.codex.</summary>
public class CxSessionTestHomeTests
{
    [Test]
    public void Headed_launch_env_uses_the_dedicated_test_home()
    {
        var env = CxSession.HeadedEnv();

        env.ShouldContainKey("CODEX_HOME");
        env["CODEX_HOME"].ShouldBe(CxSession.TestHome);
        env["CODEX_HOME"].ShouldNotBe(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
    }
}
