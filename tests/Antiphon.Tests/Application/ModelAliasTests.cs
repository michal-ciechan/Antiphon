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
}
