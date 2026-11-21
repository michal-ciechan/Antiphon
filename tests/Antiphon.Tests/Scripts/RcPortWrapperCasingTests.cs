using System.Globalization;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-2010 A-2, in-process (moved from scripts/test-rc-port-casing.ps1): the shadow proposal, the trust decision and
/// the dirty launch order serialize with the pre-port camelCase names, whatever serializer options the writer uses.
/// Shadow only: effectiveMode stays Full and gateCredit stays false.
/// </summary>
[Category("Unit")]
public sealed class RcPortWrapperCasingTests
{
    private static void AssertCamelCase(string label, object value)
    {
        var json = JsonSerializer.Serialize(value);
        json.Contains("\"effectiveMode\"", StringComparison.Ordinal).ShouldBeTrue(label + " missing effectiveMode: " + json);
        json.Contains("\"EffectiveMode\"", StringComparison.Ordinal).ShouldBeFalse(label + " has EffectiveMode: " + json);
        json.Contains("\"gateCredit\":false", StringComparison.Ordinal).ShouldBeTrue(label + " gateCredit: " + json);
        json.Contains("\"effectiveMode\":\"Full\"", StringComparison.Ordinal).ShouldBeTrue(label + " effectiveMode: " + json);
    }

    [Test]
    public void WrapperJsonStaysCamelCase()
    {
        var seed = string.Concat(Enumerable.Repeat("ab", 32));
        var chunk = new Dictionary<string, object> { ["id"] = "c1", ["suite"] = "alpha", ["boundary"] = "north", ["completelyCarryable"] = true };
        AssertCamelCase("proposal", RcShadowSample.Proposal(new object[] { chunk }, seed, "", RcInputFixtureRepository.SourceRoot));

        var utcNow = DateTimeOffset.Parse("2026-11-13T12:00:00.0000000+00:00", CultureInfo.InvariantCulture);
        AssertCamelCase("trust", RcShadowSample.Trust(null, null, null, null, utcNow, Array.Empty<object>(), Array.Empty<object>(), null, false, null, "", "", ""));

        var obligations = new object[] { new Dictionary<string, object> { ["key"] = "dirty", ["disposition"] = "Dirty" } };
        AssertCamelCase("dirty", RcDirtyOrder.Launch(obligations, Array.Empty<object>(), Array.Empty<object>(), "", "candidate-not-in-history"));
    }
}
