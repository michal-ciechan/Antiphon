using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// CARD-0045: this test host refuses to inherit <c>ANTIPHON_PTY_BACKEND</c>. One copy per
/// pty-touching test assembly, because the hook is assembly-scoped; the full rationale lives on
/// <c>tests/Antiphon.Agents.Pty.Tests/PtyBackendEnvGuard.cs</c>.
///
/// <para>The E2E project's only pty today is the headed <c>ClaudeHarness</c>, and the headless path
/// spawns none — so this guard is pre-emptive, covering the residual risk named in the plan: a
/// future E2E test that spawns a pty and inherits whatever the launching shell exported. Note the
/// ADR documented "export the variable to sweep E2E on modern" as a feature; CARD-0045 retires that
/// knob deliberately, because a suite whose meaning depends on its launcher is the bug.</para>
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
