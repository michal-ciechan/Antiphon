using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0045: this test host refuses to inherit <c>ANTIPHON_PTY_BACKEND</c>.
///
/// <para><b>The deliverable is an equivalence</b> — the suite must give the same result whether the
/// variable is set or unset. Slice 1 got there declaratively (every backend-sensitive test names its
/// backend at the constructor site); this is the other half, and it is what protects every future
/// pty test nobody remembers to pin. Without it the suite's meaning depends on who launched it: an
/// Antiphon-spawned shell inherits <c>modern</c> from the runner/AppHost export (CARD-0037), and
/// eight tests asserting inbox-conhost facts went red "because the fix works", while a further
/// thirteen went red on real modern-backend defects in code they were not testing.</para>
///
/// <para><b>What this costs, deliberately:</b> the "sweep the whole suite on modern by exporting one
/// variable" knob dies. That knob <em>was</em> the bug. A modern sweep now means running the tests
/// that declare <c>"modern"</c> (they exist, and they skip when the redistributable is absent —
/// <c>PtyBackendContractTests</c>, the paste-exemption arm of <c>FakeClaudeContractTests</c>,
/// <c>PtyDeliveryCeilingsTests</c>, <c>ShadowCopyStoreTests</c>) or temporarily editing this guard.
/// Both are deliberate acts, which is the point.</para>
///
/// <para>Production propagation is untouched: the runner's config→env export, the AppHost's server
/// env and <c>PtyDeliveryProfile</c>'s two-fact agreement are exactly as CARD-0037 left them.</para>
///
/// <para><b>It restores tests; it does not fix what they found.</b>
/// <c>WaitForQuiet_returns_false_under_continuous_output</c> and
/// <c>DoneDetector_returns_false_under_continuous_output</c> assert backend-agnostic behaviour and
/// fail on modern anyway (a <c>cmd</c> loop echoing continuously reads as QUIET for 2 s) — the same
/// early-window silence as the server-side adapters, on the backend this deployment runs. They pass
/// here because the guard gives them the code default back. To reproduce after the guard, comment
/// out <see cref="ClearInheritedPtyBackend"/> and export <c>ANTIPHON_PTY_BACKEND=modern</c>.</para>
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
        // Say it out loud: a run launched "on modern" must visibly report that the suite ignored it,
        // rather than silently producing a different result from the same commit.
        Console.WriteLine(
            $"[CARD-0045] cleared inherited {PtyBackendPolicy.EnvVar}='{Inherited}' — pty tests "
            + "declare their backend; the suite means the same thing whoever launched it.");
    }
}

[Category("Pty")]
public class PtyBackendEnvGuardTests
{
    /// <summary>
    /// The pin. It cannot set the variable and watch the guard clear it (the guard has already run
    /// by the time any test executes), so it asserts the state the guard leaves behind: nothing
    /// inherited, and the unqualified resolution is the code default. On a machine WITH the
    /// redistributable — this one — the second assertion is only true because the guard ran.
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
