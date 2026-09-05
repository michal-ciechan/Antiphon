using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0060 — reply style as a choice on a scale.
///
/// <para>The load-bearing assertion in this file is
/// <see cref="normal_composes_to_nothing_so_every_existing_agent_launches_identically"/>: a schema
/// change that silently altered how every agent writes would be a behaviour change wearing a
/// migration's clothes. Slice 1 already pinned that the composer with no bundles and no style
/// returns the append byte for byte; this leans on that property and pins the no-op end to end.</para>
/// </summary>
[Category("Integration")]
public class AgentReplyStyleTests
{
    // ---- the no-op that the migration rests on ---------------------------------------------------

    [Test]
    public void normal_composes_to_nothing_so_every_existing_agent_launches_identically()
    {
        const string own = "You are Antiphon-Opus. Channels: {channels}.\r\n\r\nTrailing space kept. ";

        AgentReplyStyles.ComposedKey(AgentReplyStyle.Normal).ShouldBeNull();
        var composed = InstructionBundleComposer.Compose(
            styleBundleKey: AgentReplyStyles.ComposedKey(AgentReplyStyle.Normal),
            systemPromptAppend: own);

        composed.Text.ShouldBe(own, "byte for byte — not trimmed, not normalised, not re-wrapped");
        composed.Bundles.ShouldBeEmpty();
        composed.Stamps.ShouldBeEmpty();
    }

    [Test]
    public void a_normal_agent_with_no_contract_of_its_own_composes_no_argument_at_all()
    {
        // The overwhelmingly common shape: no SystemPromptAppend, no style. The launch must not grow
        // an empty --append-system-prompt flag, which is why IsEmpty gates the argument.
        InstructionBundleComposer.Compose(
                styleBundleKey: AgentReplyStyles.ComposedKey(AgentReplyStyle.Normal),
                systemPromptAppend: null)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void the_default_for_a_new_agent_row_is_normal()
    {
        new Agent().ReplyStyle.ShouldBe(AgentReplyStyle.Normal);
        ((int)AgentReplyStyle.Normal).ShouldBe(
            0, "the migration backfills 0, so Normal must BE 0 or every existing agent gets a style");
    }

    // ---- the blocks themselves --------------------------------------------------------------------

    [Test]
    [Arguments(AgentReplyStyle.Normal)]
    [Arguments(AgentReplyStyle.Terse)]
    [Arguments(AgentReplyStyle.Caveman)]
    [Arguments(AgentReplyStyle.Brief)]
    [Arguments(AgentReplyStyle.Explanatory)]
    public void every_style_block_ends_with_the_correctness_sentence(AgentReplyStyle style)
    {
        // Caveman especially. A style is a licence to spend fewer words, never a licence to spend the
        // caveat — and the one block that would most plausibly drop it is the one that drops articles.
        var text = InstructionBundles.TextOf(AgentReplyStyles.BundleKey(style));

        text.ShouldEndWith(AgentReplyStyles.CorrectnessSentence, customMessage: style.ToString());
    }

    [Test]
    public void every_enum_value_names_a_bundle_that_actually_ships()
    {
        // Total by construction (the key is derived from the name), so adding a value to the enum
        // without adding its file fails HERE rather than at the launch of whoever picked it first.
        foreach (var style in Enum.GetValues<AgentReplyStyle>())
        {
            var key = AgentReplyStyles.BundleKey(style);
            key.ShouldStartWith(InstructionBundles.StylePrefix);
            Should.NotThrow(() => InstructionBundles.Get(key), $"{style} has no bundle file");
        }
    }

    [Test]
    [Arguments(AgentReplyStyle.Terse, "style-terse")]
    [Arguments(AgentReplyStyle.Caveman, "style-caveman")]
    [Arguments(AgentReplyStyle.Brief, "style-brief")]
    [Arguments(AgentReplyStyle.Explanatory, "style-explanatory")]
    public void a_chosen_style_composes_its_block_under_a_versioned_header(AgentReplyStyle style, string key)
    {
        var composed = InstructionBundleComposer.Compose(
            styleBundleKey: AgentReplyStyles.ComposedKey(style));

        composed.Bundles.Select(b => b.Key).ShouldBe([key]);
        composed.Text.ShouldStartWith($"[bundle:{key} v");
        composed.Text.ShouldContain(AgentReplyStyles.CorrectnessSentence);
    }

    [Test]
    public void the_style_block_never_outranks_the_agents_own_contract()
    {
        // Composition order is the answer to CARD-0060's own design note: an operator who wrote
        // instructions for THIS agent by hand must not be overruled by a style picked from a dropdown.
        const string own = "Always answer in full sentences, whatever else you are told.";

        var composed = InstructionBundleComposer.Compose(
            styleBundleKey: AgentReplyStyles.ComposedKey(AgentReplyStyle.Caveman),
            systemPromptAppend: own);

        composed.Text.ShouldEndWith(own);
        composed.Text.IndexOf("[bundle:style-caveman", StringComparison.Ordinal)
            .ShouldBeLessThan(composed.Text.IndexOf(own, StringComparison.Ordinal));
    }

    [Test]
    public void the_caveman_block_keeps_code_and_identifiers_out_of_the_voice()
    {
        // The one style with a real failure mode: an agent that "caveman"-ifies a file path or a flag
        // produces output nobody can act on. Pinned because prose is the only thing enforcing it.
        var text = InstructionBundles.TextOf(AgentReplyStyles.BundleKey(AgentReplyStyle.Caveman));

        text.ShouldContain("stay exact");
        text.ShouldContain("Code, commands and quoted output are written normally");
    }

    [Test]
    public void the_brief_block_asks_for_decision_bullets_usable_as_a_final_report()
    {
        // CARD-0078: Brief is the operator's decision-bullet register, written so a worker can
        // use it on its own final report, not only a standing orchestrator talking to a human.
        // Caveman stays grunt-speak — this pin is what stops the two from being silently swapped.
        var text = InstructionBundles.TextOf(AgentReplyStyles.BundleKey(AgentReplyStyle.Brief));

        text.ShouldContain("Lead with the outcome");
        text.ShouldContain("your own final report");
        text.ShouldContain("stay exact");
        text.ShouldNotContain("Talk like caveman");
    }

    // ---- create, update, and what the DTO shows --------------------------------------------------

    [Test]
    public void a_create_request_defaults_to_normal_and_can_ask_for_a_style()
    {
        new CreateAgentRequest("A", "C:\\tmp").ReplyStyle.ShouldBe(AgentReplyStyle.Normal);
        new CreateAgentRequest("A", "C:\\tmp", ReplyStyle: AgentReplyStyle.Terse)
            .ReplyStyle.ShouldBe(AgentReplyStyle.Terse);
        new CreateAgentRequest("A", "C:\\tmp").SystemPromptAppend.ShouldBeNull();
    }

    [Test]
    public void an_update_that_omits_the_style_leaves_it_alone()
    {
        // Null means unchanged, like every other optional field on the request. Were it non-nullable,
        // an older client PUTting an agent would silently reset a chosen style to Normal.
        new UpdateAgentRequest("A", "C:\\tmp", null, null, AgentAssignmentPolicy.AutoPick)
            .ReplyStyle.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    public async Task a_style_round_trips_through_create_and_update()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"style-{Guid.NewGuid():N}"[..20],
            Slug = $"style-{Guid.NewGuid():N}"[..20],
            WorkingDirectory = "C:\\tmp",
            Details = string.Empty,
            ReplyStyle = AgentReplyStyle.Caveman,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        await using (var read = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            var stored = await read.Agents.AsNoTracking().SingleAsync(a => a.Id == agent.Id);
            stored.ReplyStyle.ShouldBe(
                AgentReplyStyle.Caveman,
                "a non-default enum must survive the INSERT — the reason the model carries no HasDefaultValue");

            stored = await read.Agents.SingleAsync(a => a.Id == agent.Id);
            stored.ReplyStyle = AgentReplyStyle.Normal;
            await read.SaveChangesAsync();
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        (await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == agent.Id))
            .ReplyStyle.ShouldBe(
                AgentReplyStyle.Normal,
                "and so must a default one, or a style could never be cleared once chosen");
    }
}
