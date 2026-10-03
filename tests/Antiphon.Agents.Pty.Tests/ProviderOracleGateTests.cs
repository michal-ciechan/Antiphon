using System.Reflection;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public sealed class ProviderOracleGateTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("0")]
    [Arguments("true")]
    [Arguments(" 1")]
    [Arguments("1 ")]
    public void Disabled_gate_skips_before_the_oracle_body(string? value)
    {
        var gate = ResolveGate();
        var calls = 0;
        var skip = Should.Throw<SkipTestException>(() =>
        {
            gate(name =>
            {
                name.ShouldBe("ANTIPHON_PTY_PROVIDER_ORACLES");
                return value;
            });
            calls++;
        });

        skip.Message.ShouldBe("Set ANTIPHON_PTY_PROVIDER_ORACLES=1 to opt in to offline Codex/Grok provider oracles");
        calls.ShouldBe(0, "disabled selection must not invoke the oracle body");
    }

    [Test]
    public void Enabled_gate_invokes_the_oracle_body()
    {
        var gate = ResolveGate();
        var calls = 0;
        SkipTestException? blocked = null;
        try
        {
            gate(name =>
            {
                name.ShouldBe("ANTIPHON_PTY_PROVIDER_ORACLES");
                return "1";
            });
            calls++;
        }
        catch (SkipTestException skip)
        {
            blocked = skip;
        }

        blocked.ShouldBeNull("explicit opt-in must admit the oracle body, not skip it");
        calls.ShouldBe(1, "explicit opt-in must reach the oracle body");
    }

    // Reflection keeps this behavioral regression test compilable at the unchanged
    // baseline, where the gate is absent. CreateDelegate preserves the skip exception.
    private static Action<Func<string, string?>> ResolveGate()
    {
        var type = typeof(ProviderOracleGateTests).Assembly.GetType(
            "Antiphon.Agents.Pty.Tests.ProviderOracleGate");
        type.ShouldNotBeNull("ordinary provider oracle selection requires an opt-in gate");
        var method = type.GetMethod("SkipIfNotEnabled", BindingFlags.Public | BindingFlags.Static);
        method.ShouldNotBeNull("the gate must accept an injected environment reader");
        return method.CreateDelegate<Action<Func<string, string?>>>();
    }
}
