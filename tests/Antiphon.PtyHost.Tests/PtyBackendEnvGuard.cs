using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// CARD-0045: this test host refuses to inherit <c>ANTIPHON_PTY_BACKEND</c>. One copy per
/// pty-touching test assembly, because the hook is assembly-scoped; the full rationale lives on
/// <c>tests/Antiphon.Agents.Pty.Tests/PtyBackendEnvGuard.cs</c>.
///
/// <para>Measured identical on both arms today (20 tests, 0 failures either way) — the guard is
/// here because this assembly starts real hosts that own real ptys, and "identical today" is not a
/// property anyone maintains on purpose.</para>
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
    /// <summary>
    /// The pin: the guard has already run by the time any test executes, so this asserts the state
    /// it leaves behind. On a machine WITH the redistributable the second assertion is only true
    /// because the guard ran.
    /// </summary>
    [Test]
    public void The_suite_ignores_an_inherited_pty_backend()
    {
        Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar).ShouldBeNull(
            $"the [Before(Assembly)] guard must clear {PtyBackendPolicy.EnvVar} before any test "
            + $"runs (this process inherited '{PtyBackendEnvGuard.Inherited ?? "<unset>"}')");

        PtyBackendPolicy.Resolve().Backend.ShouldBe(
            PtyBackend.InboxConhost,
            "an unqualified resolution must be the code default, so a test that does not declare a "
            + "backend gets the same pty on every machine and from every launcher");
    }
}
