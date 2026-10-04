using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// Each PTY test assembly clears inherited backend input independently. Tests name any override;
/// the isolated default is modern on Windows with the shipped pair, UnixPty elsewhere (CARD-1022).
/// Process-environment mutators remain excluded by ConPtyEnvironmentIsolationGuardTests.
/// </summary>
public class PtyBackendEnvGuard
{
    /// <summary>The value this process was launched with, kept only so the log can name it.</summary>
    public static string? Inherited { get; private set; }

    [Before(Assembly)]
    public static void ClearInheritedPtyBackend()
    {
        Inherited = Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar);
        if (string.IsNullOrEmpty(Inherited))
            return;

        Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, null);
        Console.WriteLine(
            $"[CARD-0045] cleared inherited {PtyBackendPolicy.EnvVar}='{Inherited}' — pty tests "
            + "declare their backend; the suite means the same thing whoever launched it.");
    }
}

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
