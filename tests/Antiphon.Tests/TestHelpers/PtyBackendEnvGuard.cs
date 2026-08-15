using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0045: this test host refuses to inherit <c>ANTIPHON_PTY_BACKEND</c>. One copy per
/// pty-touching test assembly, because the hook is assembly-scoped; the full rationale lives on
/// <c>tests/Antiphon.Agents.Pty.Tests/PtyBackendEnvGuard.cs</c>.
///
/// <para>This assembly spawns ptys two ways — directly (<c>RawPtyAdapterTests</c>,
/// <c>CodexAdapterLocalShellTests</c>, <c>SlashCommandMenuReconciliationTests</c>) and through
/// <c>DirectSessionRunnerClient</c> → <c>SessionRunnerRuntime</c> → a detached pty-host that
/// inherits this process's environment block. Both routes ran on whatever backend the launching
/// shell exported; both now get the code default unless a test declares otherwise (the host-mediated
/// route declares via <c>DirectSessionRunnerClient(ptyBackend:)</c>, CARD-0045 slice 3).</para>
///
/// <para><b>The guard restores these tests, it does not fix what they found.</b>
/// <c>RawPtyAdapterTests</c> and <c>CodexAdapterLocalShellTests</c> assert backend-AGNOSTIC
/// behaviour and still fail deterministically on the modern backend (typed input into an
/// interactive <c>cmd /k</c> draws no child output inside the adapter's 2 s quiet window) — a real
/// defect in production server code, on the backend this deployment actually runs. They pass here
/// because the guard gives them the code default back, which is what they always meant; the defect
/// is CARD-0045 slice 5's to record. To reproduce it after this guard, comment out
/// <see cref="ClearInheritedPtyBackend"/> and run with <c>ANTIPHON_PTY_BACKEND=modern</c> — the
/// adapters build a bare <c>new PtyAgentRunner()</c> and deliberately grew no backend parameter in
/// this card (§4.3).</para>
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
