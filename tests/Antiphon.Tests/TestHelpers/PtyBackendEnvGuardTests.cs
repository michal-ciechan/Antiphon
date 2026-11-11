using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Integration")]
public class PtyBackendEnvGuardTests
{
    /// <summary>Assert the assembly hook left the platform default and no ambient selector.</summary>
    [Test]
    public void The_suite_ignores_an_inherited_pty_backend()
    {
        Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar).ShouldBeNull(
            $"the [Before(Assembly)] guard must clear {PtyBackendPolicy.EnvVar} before any test "
            + $"runs (this process inherited '{PtyBackendEnvGuard.Inherited ?? "<unset>"}')");

        var decision = PtyBackendPolicy.Resolve();
        decision.Backend.ShouldBe(OperatingSystem.IsWindows() ? PtyBackend.ModernConPty : PtyBackend.UnixPty,
            "the isolated platform default requires the shipped pair on Windows");
        decision.FellBack.ShouldBeFalse(decision.Reason);
        decision.Deprecated.ShouldBeFalse();
    }
}
