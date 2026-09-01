using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public class ModelAliasTests
{
    [Test]
    [Arguments("fable", "fable")]
    [Arguments("Fable", "fable")]
    [Arguments("Fable 5", "fable")]
    [Arguments("claude-fable-5", "fable")]
    [Arguments("claude-fable", "fable")]
    [Arguments("opus", "opus")]
    [Arguments("Opus 5", "opus")]
    [Arguments("claude-opus-5", "opus")]
    [Arguments("sonnet", "sonnet")]
    [Arguments("Sonnet 5", "sonnet")]
    [Arguments("claude-sonnet-5", "sonnet")]
    [Arguments("haiku", "haiku")]
    [Arguments("Haiku 4.5", "haiku")]
    [Arguments("claude-haiku-4-5", "haiku")]
    [Arguments("grok-4.6", "grok-4.6")]
    [Arguments("gpt-5.6-sol", "gpt-5.6-sol")]
    [Arguments("gpt-5.6-terra", "gpt-5.6-terra")]
    [Arguments("gpt-5.6-luna", "gpt-5.6-luna")]
    [Arguments("*", "*")]
    public void Normalize_maps_known_family_text(string raw, string expected)
    {
        ModelAlias.Normalize(AgentKind.ClaudeCode, raw).ShouldBe(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("<synthetic>")]
    [Arguments("bogus-family")]
    [Arguments("claude-mystery-9")]
    public void Normalize_returns_null_for_unknown_text(string? raw)
    {
        ModelAlias.Normalize(AgentKind.ClaudeCode, raw).ShouldBeNull();
    }

    [Test]
    [Arguments("fable", "fable")]
    [Arguments("Fable", "fable")]
    [Arguments("HAIKU", "haiku")]
    [Arguments("grok-4.6", "grok-4.6")]
    [Arguments("*", "*")]
    public void CanonicalHoldAlias_accepts_known_aliases_and_star(string raw, string expected)
    {
        ModelAlias.CanonicalHoldAlias(raw).ShouldBe(expected);
    }

    [Test]
    [Arguments("claude-fable-5")]
    [Arguments("Fable 5")]
    [Arguments("bogus")]
    [Arguments("")]
    [Arguments(null)]
    public void CanonicalHoldAlias_rejects_tui_names_and_unknown_text(string? raw)
    {
        ModelAlias.CanonicalHoldAlias(raw).ShouldBeNull();
    }
}
