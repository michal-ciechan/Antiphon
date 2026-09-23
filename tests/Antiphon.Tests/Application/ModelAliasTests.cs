using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
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
    [Arguments("Opus 5.5", "opus")]
    [Arguments("claude-opus-5.5", "opus")]
    [Arguments("claude-opus-5-5", "opus")]
    [Arguments("sonnet", "sonnet")]
    [Arguments("Sonnet 5", "sonnet")]
    [Arguments("claude-sonnet-5", "sonnet")]
    [Arguments("haiku", "haiku")]
    [Arguments("Haiku 4.5", "haiku")]
    [Arguments("claude-haiku-4-5", "haiku")]
    [Arguments("grok-4.7", "grok-4.7")]
    [Arguments("grok-4.6", "grok-4.6")]
    [Arguments("gpt-6-astra", "gpt-6-astra")]
    [Arguments("GPT-6-Astra", "gpt-6-astra")]
    [Arguments("astra", "gpt-6-astra")]
    [Arguments("gpt-6-sol", "gpt-6-sol")]
    [Arguments("GPT-6-Sol", "gpt-6-sol")]
    [Arguments("sol", "gpt-6-sol")]
    [Arguments("gpt-5.6-sol", "gpt-5.6-sol")]
    [Arguments("gpt-5.6-terra", "gpt-5.6-terra")]
    [Arguments("gpt-5.6-luna", "gpt-5.6-luna")]
    [Arguments("*", "*")]
    public void Normalize_maps_known_family_text(string raw, string expected)
    {
        ModelAlias.Normalize(AgentKind.ClaudeCode, raw).ShouldBe(expected);
    }

    /// <summary>
    /// CARD-0611 follow-up: an <c>Opus 5.5</c> usage-limit hold line folds to <c>opus 5 5</c>
    /// (and <c>claude opus 5 5</c>), which the original <see cref="ModelAlias"/> arms missed —
    /// the CARD-0022/0309 hold then had no alias to key on.
    /// </summary>
    [Test]
    [Arguments("Opus 5.5")]
    [Arguments("opus 5.5")]
    [Arguments("opus-5-5")]
    [Arguments("Claude Opus 5.5")]
    [Arguments("claude-opus-5.5")]
    [Arguments("claude_opus_5_5")]
    public void Normalize_maps_opus_5_5_hold_text(string raw)
    {
        ModelAlias.Normalize(AgentKind.ClaudeCode, raw).ShouldBe(ModelAlias.Opus);
    }

    /// <summary>
    /// The 5.5 arms must stay exact: an unreleased point release is still unknown text, so the
    /// caller falls back to the session's launch alias instead of holding a guessed model.
    /// </summary>
    [Test]
    [Arguments("Opus 6")]
    [Arguments("opus 5.6")]
    [Arguments("claude-opus-5-9")]
    public void Normalize_returns_null_for_unrecognised_opus_point_release(string raw)
    {
        ModelAlias.Normalize(AgentKind.ClaudeCode, raw).ShouldBeNull();
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
    [Arguments("grok-4.7", "grok-4.7")]
    [Arguments("gpt-6-astra", "gpt-6-astra")]
    [Arguments("gpt-6-sol", "gpt-6-sol")]
    [Arguments("*", "*")]
    public void CanonicalHoldAlias_accepts_known_aliases_and_star(string raw, string expected)
    {
        ModelAlias.CanonicalHoldAlias(raw).ShouldBe(expected);
    }

    [Test]
    [Arguments("claude-fable-5")]
    [Arguments("Fable 5")]
    [Arguments("astra")]
    [Arguments("grok-4.6")]
    [Arguments("gpt-5.6-sol")]
    [Arguments("bogus")]
    [Arguments("")]
    [Arguments(null)]
    public void CanonicalHoldAlias_rejects_tui_names_and_unknown_text(string? raw)
    {
        ModelAlias.CanonicalHoldAlias(raw).ShouldBeNull();
    }

    /// <summary>
    /// CARD-0611: the Codex High rung moved from <c>gpt-5.6-sol</c> to <c>gpt-6-sol</c>, so the
    /// bare TUI word "Sol" has to land on the model a High dispatch actually launches — the same
    /// move CARD-0169's bump made for bare "grok". <c>gpt-5.6-sol</c> is still a live catalog
    /// model (priority 4) and still normalizes, so an existing hold row keeps its meaning; it is
    /// only gone from the delegatable ladder, which is what makes it un-holdable above.
    /// </summary>
    [Test]
    public void The_bare_sol_word_follows_the_codex_high_rung()
    {
        ModelAlias.Normalize(AgentKind.Codex, "Sol").ShouldBe(ModelAlias.Gpt6Sol);
        ModelAlias.Normalize(AgentKind.Codex, "GPT-6-Sol").ShouldBe(ModelAlias.Gpt6Sol);
        ModelAlias.Normalize(AgentKind.Codex, "gpt 6 sol").ShouldBe(ModelAlias.Gpt6Sol);

        // The retired 5.6 slug keeps its own identity rather than folding into the new pin.
        ModelAlias.Normalize(AgentKind.Codex, "gpt-5.6-sol").ShouldBe(ModelAlias.Gpt56Sol);
        ModelAlias.Normalize(AgentKind.Codex, "GPT-5.6-Sol").ShouldBe(ModelAlias.Gpt56Sol);

        // And the ladder the hold vocabulary is derived from names the new pin, not the old one.
        ModelLevelAliases.ForCodex(AgentModelLevel.High).ShouldBe(ModelAlias.Gpt6Sol);
        ModelAlias.DelegatableAliases
            .Where(entry => entry.Kind == AgentKind.Codex)
            .Select(entry => entry.Alias)
            .ShouldBe(["gpt-6-astra", "gpt-6-sol", "gpt-5.6-terra", "gpt-5.6-luna"]);
    }
}
