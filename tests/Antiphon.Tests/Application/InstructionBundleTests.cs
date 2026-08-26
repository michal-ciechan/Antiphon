using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0058 slice 1 — the bundle catalog and the composer.
///
/// <para>Everything here is pure: the catalog is embedded in the server assembly and the composer
/// takes no services, so these tests exercise the real thing with no database, no clock and no pty.
/// The two constants that FORWARD to the catalog (<c>CheckInterpretation.Contract</c>,
/// <c>DelegationReportFormatter.OrchestratorContract</c>) are pinned here as forwards; that the
/// check interpreter's own suite still passes UNMODIFIED against the moved text is the proof the
/// move was faithful, and it must stay that way.</para>
/// </summary>
public class InstructionBundleTests
{
    [Test]
    public void the_orchestrator_preset_prompt_is_embedded_and_not_attachable()
    {
        var text = AgentPresets.LoadOrchestratorTemplate();
        text.ShouldContain("{project}");
        text.ShouldContain("{board}");
        text.ShouldContain("{directory}");
        InstructionBundles.Attachable.Select(b => b.Key).ShouldNotContain("Presets.orchestrator-prompt");
        InstructionBundles.All.Keys.ShouldNotContain("Presets.orchestrator-prompt");
        AgentPresets.Find("orchestrator")!.SystemPromptTemplate.ShouldBe(text);
    }

    [Test]
    public void the_catalog_holds_exactly_the_bundles_that_ship()
    {
        // Pinned rather than counted: the resource glob is `Bundles\*.md` minus README.md, so a file
        // added, renamed or accidentally embedded shows up HERE rather than in an agent's system
        // prompt. Adding a bundle is meant to cost this one line.
        InstructionBundles.All.Keys.Order().ShouldBe([
            "board-api", "check-interpreter", "delegate-basics", "orchestrator",
            // One per AgentReplyStyle value (CARD-0060), style-normal included — see AgentReplyStyles
            // for why the one that is never composed still ships as a file.
            "style-caveman", "style-explanatory", "style-normal", "style-terse",
        ]);
    }

    [Test]
    public void every_bundle_has_text_and_an_eight_hex_digit_content_version()
    {
        foreach (var bundle in InstructionBundles.All.Values)
        {
            bundle.Text.ShouldNotBeNullOrWhiteSpace(bundle.Key);
            bundle.Text.ShouldNotContain("\r", customMessage:
                $"{bundle.Key}: line endings are normalised to LF on load, so the version cannot "
                + "change with nothing but the checkout");
            bundle.Text.Trim().ShouldBe(bundle.Text, $"{bundle.Key}: outer blank space is trimmed on load");
            bundle.Version.Length.ShouldBe(8, bundle.Key);
            bundle.Version.ShouldMatch("^[0-9a-f]{8}$", $"{bundle.Key}: lowercase hex, stable to read aloud");
        }
    }

    [Test]
    public void the_version_is_the_content_hash_so_two_bundles_never_share_one()
    {
        var versions = InstructionBundles.All.Values.Select(b => b.Version).ToList();

        versions.Distinct().Count().ShouldBe(versions.Count);
        // Same catalog, same versions, forever — the load is cached and derived from content alone.
        InstructionBundles.Get(InstructionBundles.DelegateBasics).Version
            .ShouldBe(InstructionBundles.All[InstructionBundles.DelegateBasics].Version);
    }

    [Test]
    public void no_bundle_carries_a_channel_preamble_placeholder()
    {
        // ChannelPreamble.Render substitutes over the WHOLE composed append (AgentControlService),
        // so a bundle containing either token would have it rewritten out from under it — with the
        // agent's name or its channel list appearing mid-rule. Cheaper to pin than to restructure
        // the render, and this is the pin.
        foreach (var bundle in InstructionBundles.All.Values)
        {
            bundle.Text.ShouldNotContain(ChannelPreamble.AgentNamePlaceholder, customMessage: bundle.Key);
            bundle.Text.ShouldNotContain(ChannelPreamble.ChannelsPlaceholder, customMessage: bundle.Key);
        }
    }

    [Test]
    public void a_rendered_bundle_leads_with_its_versioned_header()
    {
        var bundle = InstructionBundles.Get(InstructionBundles.DelegateBasics);

        bundle.Header.ShouldBe($"[bundle:delegate-basics v{bundle.Version}]");
        bundle.Render().ShouldBe($"{bundle.Header}\n{bundle.Text}");
        bundle.Stamp.ShouldBe($"delegate-basics v{bundle.Version}");
    }

    [Test]
    public void every_bundle_summarises_itself_in_its_opening_sentence()
    {
        // The attachment picker (slice 6) shows this next to the key, so an operator is not choosing
        // from filenames. Derived rather than a second field: every bundle already opens by saying
        // what it is, and a hand-written summary would be one more thing to leave stale.
        InstructionBundles.Get(InstructionBundles.Orchestrator).Summary
            .ShouldBe("You are an orchestrator.");
        InstructionBundles.Get(InstructionBundles.BoardApi).Summary
            .ShouldBe("Working the Antiphon board.");

        foreach (var bundle in InstructionBundles.All.Values)
        {
            bundle.Summary.ShouldNotBeNullOrWhiteSpace(bundle.Key);
            bundle.Summary.Length.ShouldBeLessThanOrEqualTo(200, bundle.Key);
            bundle.Summary.ShouldNotContain("\n", customMessage: bundle.Key);
        }
    }

    [Test]
    public void an_unknown_key_throws_and_names_the_ones_that_exist()
    {
        var ex = Should.Throw<KeyNotFoundException>(() => InstructionBundles.Get("delegate-basic"));

        ex.Message.ShouldContain("delegate-basics", customMessage: "a typo must be diagnosable from the message");
        ex.Message.ShouldContain("server/Bundles/");
    }

    // ---- composition order ---------------------------------------------------------------------

    [Test]
    public void bundles_compose_in_declared_order_under_their_headers()
    {
        var composed = InstructionBundleComposer.Compose(
            [InstructionBundles.Orchestrator, InstructionBundles.DelegateBasics]);

        composed.Bundles.Select(b => b.Key).ShouldBe(["orchestrator", "delegate-basics"]);
        composed.Text.IndexOf("[bundle:orchestrator", StringComparison.Ordinal)
            .ShouldBeLessThan(composed.Text.IndexOf("[bundle:delegate-basics", StringComparison.Ordinal));
        composed.Text.ShouldBe(string.Join(
            InstructionBundleComposer.BlockSeparator,
            InstructionBundles.Get(InstructionBundles.Orchestrator).Render(),
            InstructionBundles.Get(InstructionBundles.DelegateBasics).Render()));
    }

    [Test]
    public void a_key_reachable_twice_is_composed_once()
    {
        // The shape this prevents is real from slice 6 on: a bundle attached to an agent by hand that
        // its role already carries. Paying for it twice is the small cost; the agent READING the same
        // rules twice, and inferring emphasis from the repetition, is the real one.
        var composed = InstructionBundleComposer.Compose(
            [InstructionBundles.DelegateBasics, InstructionBundles.BoardApi, InstructionBundles.DelegateBasics]);

        composed.Bundles.Select(b => b.Key).ShouldBe(["delegate-basics", "board-api"]);
        CountOccurrences(composed.Text, "[bundle:delegate-basics").ShouldBe(1);
    }

    [Test]
    public void the_style_block_lands_after_the_bundles_and_the_agents_own_append_lands_last()
    {
        // The order the whole design rests on. The agent's own contract goes last because it is the
        // most specific thing anybody said about this one agent; a general bundle must not get the
        // final word over it. (The style slot is CARD-0060's; any bundle key exercises the position.)
        const string own = "You are Antiphon-Opus. Never speak for the operator.";
        var composed = InstructionBundleComposer.Compose(
            [InstructionBundles.DelegateBasics],
            styleBundleKey: InstructionBundles.BoardApi,
            systemPromptAppend: own);

        var basics = composed.Text.IndexOf("[bundle:delegate-basics", StringComparison.Ordinal);
        var style = composed.Text.IndexOf("[bundle:board-api", StringComparison.Ordinal);
        var appendAt = composed.Text.IndexOf(own, StringComparison.Ordinal);
        basics.ShouldBeLessThan(style);
        style.ShouldBeLessThan(appendAt);
        composed.Text.ShouldEndWith(own, customMessage: "nothing composes after the agent's own text");
        composed.Bundles.Select(b => b.Key).ShouldBe(["delegate-basics", "board-api"]);
        composed.Stamps.ShouldBe([
            InstructionBundles.Get(InstructionBundles.DelegateBasics).Stamp,
            InstructionBundles.Get(InstructionBundles.BoardApi).Stamp,
        ]);
    }

    [Test]
    public void with_no_bundles_the_composition_is_the_agents_own_append_byte_for_byte()
    {
        // The property that lets the standing-agent launch path adopt the composer without changing
        // one byte of what it passes today — and, in CARD-0060, that makes the Normal reply style
        // (which resolves to no style bundle at all) a no-op for every agent that already exists.
        const string own = "You are {agentName}. Channels: {channels}.\r\n\r\nTrailing space kept. ";

        var composed = InstructionBundleComposer.Compose(systemPromptAppend: own);

        composed.Text.ShouldBe(own);
        composed.Bundles.ShouldBeEmpty();
        composed.IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void nothing_to_compose_is_empty_so_the_flag_is_omitted_entirely()
    {
        InstructionBundleComposer.Compose().IsEmpty.ShouldBeTrue();
        InstructionBundleComposer.Compose([], null, "   ").IsEmpty.ShouldBeTrue(
            "a whitespace-only append counts as absent, matching the existing launch gate");
        InstructionBundleComposer.Compose().ShouldBe(ComposedInstructions.Empty);
    }

    // ---- the two forwards ----------------------------------------------------------------------

    [Test]
    public void the_check_interpreter_contract_forwards_to_its_bundle()
    {
        CheckInterpretation.Contract.ShouldBe(InstructionBundles.TextOf("check-interpreter"));
        CheckInterpretation.Contract.ShouldNotContain(
            "[bundle:", customMessage: "the forward is the TEXT — a header on the reconciled agent row "
            + "would be a behaviour change, and CheckInterpreterProvisionerTests would say so");
        CheckInterpretation.Contract.ShouldContain($"contract v{CheckInterpretation.ContractVersion}");
    }

    [Test]
    public void the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact()
    {
        DelegationReportFormatter.OrchestratorContract
            .ShouldBe(InstructionBundles.TextOf(InstructionBundles.Orchestrator));
        // Spot-pins on the moved text: this contract had no test of its own before the move, so these
        // are what say the move carried it rather than paraphrased it.
        DelegationReportFormatter.OrchestratorContract.ShouldStartWith("You are an orchestrator.");
        DelegationReportFormatter.OrchestratorContract.ShouldContain("Delegate everything else");
        DelegationReportFormatter.OrchestratorContract.ShouldContain("-Reply");
    }

    [Test]
    public void delegate_basics_carries_the_standing_rules_and_none_of_the_days_state()
    {
        var text = InstructionBundles.TextOf(InstructionBundles.DelegateBasics);

        text.ShouldContain("FOREGROUND");
        text.ShouldContain("DO NOT SUB-DELEGATE");
        text.ShouldContain("COMMIT AND PUSH EACH SLICE");
        text.ShouldContain("FORWARD slash");
        text.ShouldContain("PRE-EXISTING RED");

        // The line between a rule and today's state, which is the reason this directory exists: a
        // bundle carrying either of these would be WRONG tomorrow, and wrong for every agent at once.
        text.ShouldNotContain("CS8604", customMessage: "a warning count is state — it belongs in the brief");
        text.ShouldNotContain("JobObject", customMessage: "today's known-red test names are state");
    }

    // ---- the command-line budget ---------------------------------------------------------------

    [Test]
    public void the_worst_case_composition_measured_sits_far_under_the_budget()
    {
        // THE MEASUREMENT THAT SETS THE GUARD (plan §9). The worst case anyone can construct today:
        // every bundle in the catalog at once — no role asks for that — plus the longest system-prompt
        // append that actually ships, the Telegram preset. Counted in UTF-16 chars because
        // CreateProcessW's ~32 767 limit is a char count, not a byte count.
        //
        // Measured 2026-08-17: board-api 2 607, delegate-basics 2 216, check-interpreter 1 276,
        // orchestrator 1 156, telegram preset 1 802 => 9 198 composed chars, 31% of the 30 000 budget
        // and 28% of the OS limit. That is what makes 30 000 a runaway stop rather than a working
        // constraint, and it is why the guard can afford to THROW instead of truncating. The
        // assertion is a headroom bound rather than the exact number so ordinary prose edits do not
        // fail it — an order-of-magnitude growth does.
        var budget = new DelegationSettings().CommandLineBudgetChars;
        var everything = InstructionBundles.All.Keys.Order().ToList();

        var composed = InstructionBundleComposer.Compose(
            everything, systemPromptAppend: ChannelPreamble.TelegramPresetTemplate);

        var detail = string.Join(", ", composed.Bundles.Select(b => $"{b.Stamp} {b.Text.Length}"))
            + $", telegram-preset {ChannelPreamble.TelegramPresetTemplate.Length}"
            + $" => composed {composed.Text.Length} chars against a budget of {budget}";
        composed.Text.Length.ShouldBeLessThan(budget / 2, detail);
        // And it fits with every other launch argument beside it, which is what the guard measures.
        Should.NotThrow(() => InstructionBundleComposer.EnsureWithinCommandLineBudget(
            composed,
            ["--name", "task-1a2b3c4d", "--model", "opus", "--session-id", Guid.NewGuid().ToString("D")],
            budget,
            "worst case"));
    }

    [Test]
    public void an_oversized_composition_throws_and_names_what_to_shrink()
    {
        // Never truncates: an agent running under half a contract, with nothing on screen to say so,
        // is worse than a launch that fails loudly.
        var composed = InstructionBundleComposer.Compose(
            [InstructionBundles.DelegateBasics],
            systemPromptAppend: new string('x', 40_000));

        var ex = Should.Throw<InvalidOperationException>(() =>
            InstructionBundleComposer.EnsureWithinCommandLineBudget(
                composed, ["--name", "task-1a2b3c4d"], 30_000, "task 1a2b3c4d"));

        ex.Message.ShouldContain("task 1a2b3c4d");
        ex.Message.ShouldContain("30,000");
        ex.Message.ShouldContain("delegate-basics v", customMessage: "the message names each bundle and its size");
        ex.Message.ShouldContain("Nothing was truncated");
    }

    [Test]
    public void the_other_arguments_count_towards_the_budget_not_just_the_append()
    {
        // The guard exists to bound the whole command line, so a composition that fits on its own can
        // still be refused beside a very long argument list. Sized to fail on the total alone.
        var composed = InstructionBundleComposer.Compose([InstructionBundles.DelegateBasics]);
        var fat = Enumerable.Repeat(new string('a', 500), 20).ToList();

        composed.Text.Length.ShouldBeLessThan(10_000, "the composition alone is nowhere near the budget");
        Should.Throw<InvalidOperationException>(() =>
            InstructionBundleComposer.EnsureWithinCommandLineBudget(composed, fat, 10_000, "fat args"));
    }

    // ---- the role map --------------------------------------------------------------------------

    [Test]
    [Arguments(AgentTaskRole.Code)]
    [Arguments(AgentTaskRole.Plan)]
    [Arguments(AgentTaskRole.Review)]
    [Arguments(AgentTaskRole.Test)]
    [Arguments(AgentTaskRole.Docs)]
    [Arguments(AgentTaskRole.Custom)]
    public void every_worker_role_carries_the_delegate_basics(AgentTaskRole role)
    {
        InstructionBundles.ForDelegate(AgentTaskKind.Worker, role).ShouldBe(["delegate-basics"]);
    }

    [Test]
    public void a_sub_orchestrator_carries_its_own_contract_first_then_the_basics()
    {
        InstructionBundles.ForDelegate(AgentTaskKind.Orchestrator, AgentTaskRole.Plan)
            .ShouldBe(["orchestrator", "delegate-basics"]);
    }

    [Test]
    public void a_check_task_carries_nothing()
    {
        // The standing check interpreter has no tools and a deny-all hook: "commit and push each
        // slice" is an instruction it cannot obey, and its own contract already rides its agent row.
        InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Check).ShouldBeEmpty();
    }

    [Test]
    public void the_board_api_bundle_is_on_no_role_by_default()
    {
        // It exists for standing agents that work the board, attached per agent (plan slice 6). Every
        // delegate paying for card-API rules it will never use is what the role map is for.
        foreach (var role in Enum.GetValues<AgentTaskRole>())
        foreach (var kind in Enum.GetValues<AgentTaskKind>())
            InstructionBundles.ForDelegate(kind, role).ShouldNotContain(InstructionBundles.BoardApi);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
