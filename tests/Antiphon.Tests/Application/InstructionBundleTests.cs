using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data.Seeding;
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
[Category("Unit")]
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
    public void the_orchestrator_preset_enables_remote_control_and_the_full_feature_pipeline()
    {
        var orchestrator = AgentPresets.Find(AgentPresets.Orchestrator)!;
        orchestrator.RemoteControlEnabled.ShouldBeTrue();
        orchestrator.DefaultWorkflowTemplateId.ShouldBe(AgentPresets.FullFeaturePipelineTemplateId);
        orchestrator.DefaultWorkflowTemplateId.ShouldBe(DatabaseSeeder.BmadFullTemplateId);

        var worker = AgentPresets.Find(AgentPresets.Worker)!;
        worker.RemoteControlEnabled.ShouldBeFalse();
        worker.DefaultWorkflowTemplateId.ShouldBeNull();
    }

    [Test]
    public void the_catalog_holds_exactly_the_bundles_that_ship()
    {
        // Pinned rather than counted: the resource glob is `Bundles\*.md` minus README.md, so a file
        // added, renamed or accidentally embedded shows up HERE rather than in an agent's system
        // prompt. Adding a bundle is meant to cost this one line.
        InstructionBundles.All.Keys.Order().ShouldBe([
            "board-api", "check-interpreter", "delegate-basics", "diagnose", "orchestrator",
            "output-distiller",
            // CARD-0146 S3: one standing-rule block per pipeline-stage role. Adding a stage is
            // meant to cost this one line plus the ForDelegate map.
            "stage-code", "stage-investigate", "stage-plan", "stage-review", "stage-test-design",
            // One per AgentReplyStyle value (CARD-0060), style-normal included — see AgentReplyStyles
            // for why the one that is never composed still ships as a file.
            "style-brief", "style-caveman", "style-explanatory", "style-normal", "style-terse",
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
        CheckInterpretation.Contract.ShouldContain("exactly one physical line, at most 240 characters");
        CheckInterpretation.Contract.ShouldNotContain("3-5 lines");
        CheckInterpretation.OutputFormatReminder.ShouldContain("exactly one physical line, at most 240 characters");
        CheckInterpretation.OutputFormatReminder.ShouldNotContain("3-5 lines");
        var reporting = DelegationReportFormatter.CheckReportingContract(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), 20_000);
        reporting.ShouldContain("exactly one physical line, at most 240 characters");
        reporting.ShouldNotContain("3-5 lines");
        reporting.ShouldNotContain("--- next stage ---");
    }

    [Test]
    public void the_diagnose_contract_forwards_to_its_bundle_with_the_pinned_hard_rules()
    {
        Diagnosis.Contract.ShouldBe(InstructionBundles.TextOf(InstructionBundles.Diagnose));
        Diagnosis.Contract.ShouldNotContain(
            "[bundle:", customMessage: "the forward is the TEXT — a header on the reconciled agent row "
            + "would be a behaviour change, and DiagnoseProvisionerTests would say so");
        Diagnosis.Contract.ShouldContain($"contract v{Diagnosis.ContractVersion}");
        Diagnosis.Contract.ShouldStartWith("You are the Antiphon DIAGNOSE agent (contract v");
        Diagnosis.Contract.Length.ShouldBeLessThanOrEqualTo(3_000);
        Diagnosis.Contract.ShouldContain(
            "NEVER change, judge, summarise or restate the work. You name it or you label it.");
        Diagnosis.Contract.ShouldContain(
            "NEVER invent a CARD id, a number or a name that is not in the request. Copy or omit.");
        Diagnosis.Contract.ShouldContain(
            "USE NO TOOLS. You have none, and a tool call is refused before it runs.");
        Diagnosis.Contract.ShouldContain("the request is the whole input.");
        Diagnosis.Contract.ShouldContain(
            "Exactly one physical line before the closing line: no preamble, no bullets, no");
        Diagnosis.Contract.ShouldContain("explanation, no sign-off, no second option.");
        Diagnosis.TitleFormatReminder.ShouldContain("never `blocked`");
        Diagnosis.LabelsFormatReminder.ShouldContain("never `blocked`");
        DelegationReportFormatter.DiagnoseReportingContract(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), 20_000)
            .ShouldNotContain("--- next stage ---");
    }

    [Test]
    public void the_output_distiller_contract_forwards_to_its_bundle_with_the_pinned_invariants()
    {
        OutputDistillation.Contract.ShouldBe(InstructionBundles.TextOf(InstructionBundles.OutputDistiller));
        OutputDistillation.Contract.ShouldNotContain(
            "[bundle:", customMessage: "the forward is the TEXT — a header on the reconciled agent row "
            + "would be a behaviour change, and OutputDistillerProvisionerTests would say so");
        OutputDistillation.Contract.ShouldContain($"contract v{OutputDistillation.ContractVersion}");
        OutputDistillation.Contract.ShouldStartWith("You are the Antiphon OUTPUT DISTILLER (contract v");
        OutputDistillation.Contract.Length.ShouldBeLessThanOrEqualTo(3_000);
        OutputDistillation.Contract.ShouldContain(
            "NEVER invent, round, rename or paraphrase an identifier or a number. Copy it or leave it out.");
        OutputDistillation.Contract.ShouldContain(
            "NEVER change the outcome. A report that is blocked or failed stays blocked or failed in your");
        OutputDistillation.Contract.ShouldContain(
            "NEVER investigate. Do not read files, run commands or search. USE NO TOOLS — you have none,");
        OutputDistillation.Contract.ShouldContain(
            "Bullets only, one fact each, at most 12. No heading, no preamble, no sign-off. Nothing after");
        OutputDistillation.Contract.ShouldContain(
            "NEVER drop `next:` or `handoff:` from a `--- next stage ---` block present in the report.");
        OutputDistillation.OutputFormatReminder.ShouldContain("never `blocked`");
        DelegationReportFormatter.DistillReportingContract(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), 20_000)
            .ShouldContain("--- next stage ---");
    }

    [Test]
    public void the_distill_reporting_contract_never_offers_blocked_and_keeps_the_handoff_anchor()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var reporting = DelegationReportFormatter.DistillReportingContract(id, 20_000);
        reporting.ShouldContain("Never emit a `blocked` report token");
        reporting.ShouldContain(DelegationReportFormatter.ReportToken(id, "done"));
        reporting.ShouldContain(DelegationReportFormatter.ReportToken(id, "failed"));
        reporting.ShouldContain("`next:` and `handoff:`");
        reporting.ShouldContain("--- next stage ---");
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
        DelegationReportFormatter.OrchestratorContract.ShouldContain("StoppedBeforeFirstPrompt");
        DelegationReportFormatter.OrchestratorContract.ShouldContain("AuthenticationRequired");
        DelegationReportFormatter.OrchestratorContract.ShouldContain("grok login");
        DelegationReportFormatter.OrchestratorContract.ShouldContain("CompletedWithoutProgress");
        DelegationReportFormatter.OrchestratorContract.ShouldContain("Do not paste, log, or repeat credentials");
        DelegationReportFormatter.OrchestratorContract.ShouldContain(
            "If you are channel-bound (Slack/Telegram), the chat sees two kinds of turn.");
        DelegationReportFormatter.OrchestratorContract.ShouldContain(
            "your whole reply is exactly `NO_REPLY`");
        DelegationReportFormatter.OrchestratorContract.ShouldContain(
            "A delegate's own `[[attach:]]` reaches only you, as text.");
        // CARD-0296: the pointer at the read-oriented HTTP surface. Without it an orchestrator
        // greps MapGet for routes that do not exist and reads the 404s as a broken server.
        DelegationReportFormatter.OrchestratorContract.ShouldContain("docs/ops-http.md");
        // CARD-0017: the standing delegate-the-reading rule. Pin phrases, not the paragraph, so a
        // later wording tweak of the carve-out does not fail this; the negative pin is what keeps
        // the old "single file read" loosening from creeping back.
        DelegationReportFormatter.OrchestratorContract.ShouldContain("Delegate the reading.");
        DelegationReportFormatter.OrchestratorContract.ShouldContain("quote exactly or must judge personally");
        DelegationReportFormatter.OrchestratorContract.ShouldContain("frontier-tier");
        DelegationReportFormatter.OrchestratorContract.ShouldNotContain("single file read");
    }

    [Test]
    public void delegate_basics_carries_the_standing_rules_and_none_of_the_days_state()
    {
        var text = InstructionBundles.TextOf(InstructionBundles.DelegateBasics);

        text.ShouldContain("FOREGROUND");
        text.ShouldContain("DO NOT SUB-DELEGATE");
        text.ShouldContain("COMMIT AND PUSH EACH SLICE");
        text.ShouldContain("IS the explicit request");
        text.ShouldContain("never a \"next step\" to offer");
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
        // THE MEASUREMENT THAT SETS THE GUARD (plan §9). The worst case a launch can construct:
        // every non-specialist bundle in the catalog at once — no role asks for that — plus the
        // longest system-prompt append that actually ships, the Telegram preset. Specialist
        // contracts (check-interpreter, diagnose, output-distiller) ride SystemPromptAppend on
        // their own seat and ForDelegate returns [] for those roles, so composing them WITH the
        // rest of the catalog is not a launch anyone can have. Counted in UTF-16 chars because
        // CreateProcessW's ~32 767 limit is a char count, not a byte count.
        //
        // Measured 2026-08-17: board-api 2 607, delegate-basics 2 216, check-interpreter 1 276,
        // orchestrator 1 156, telegram preset 1 802 => 9 198 composed chars, 31% of the 30 000 budget
        // and 28% of the OS limit. Re-measured 2026-08-30 after CARD-0250's channel-bound paragraph
        // (orchestrator 2 441, telegram preset 2 189) plus catalog growth since: 15 307 composed,
        // 51% of the budget. Re-measured 2026-09-02 after CARD-0017's delegate-the-reading paragraph
        // (orchestrator 5 143) plus catalog growth since: 18 426 composed, 61% of the budget.
        // Re-measured 2026-09-03 after CARD-0339's v4 one-line check-interpreter contract
        // (check-interpreter 2 323) plus catalog growth since: 20 376 composed, 68% of the budget.
        // Re-measured 2026-09-03 after CARD-0352's diagnose bundle (diagnose 2 245) plus catalog
        // growth since: 22 987 composed, 77% of the budget. Re-measured 2026-09-05 after CARD-0146
        // S3's five stage bundles (~3 750 body chars plus headers): composing every catalog key at
        // once crossed 4/5 of 30 000, which is expected — no launch composes the whole catalog, and
        // the realistic (Worker, Code) set is pinned separately below. Re-measured 2026-09-05
        // CARD-0330: adding output-distiller pushed the all-keys composition over 30 000 (30 645);
        // specialist keys are now excluded, which is the launch that can actually happen. The bound
        // here is the budget itself: the guard still THROWS rather than truncating.
        var budget = new DelegationSettings().CommandLineBudgetChars;
        var specialistKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            InstructionBundles.CheckInterpreter,
            InstructionBundles.Diagnose,
            InstructionBundles.OutputDistiller,
        };
        var everything = InstructionBundles.All.Keys
            .Where(k => !specialistKeys.Contains(k))
            .Order()
            .ToList();

        var composed = InstructionBundleComposer.Compose(
            everything, systemPromptAppend: ChannelPreamble.TelegramPresetTemplate);

        var detail = string.Join(", ", composed.Bundles.Select(b => $"{b.Stamp} {b.Text.Length}"))
            + $", telegram-preset {ChannelPreamble.TelegramPresetTemplate.Length}"
            + $" => composed {composed.Text.Length} chars against a budget of {budget}";
        composed.Text.Length.ShouldBeLessThan(budget, detail);
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
    [Arguments(AgentTaskRole.Test)]
    [Arguments(AgentTaskRole.Docs)]
    [Arguments(AgentTaskRole.Custom)]
    [Arguments(AgentTaskRole.Debug)]
    [Arguments(AgentTaskRole.Coverage)]
    [Arguments(AgentTaskRole.Commit)]
    [Arguments(AgentTaskRole.Deploy)]
    [Arguments(AgentTaskRole.Merge)]
    public void a_helper_worker_role_carries_only_the_delegate_basics(AgentTaskRole role)
    {
        InstructionBundles.ForDelegate(AgentTaskKind.Worker, role).ShouldBe(["delegate-basics"]);
    }

    [Test]
    [Arguments(AgentTaskRole.Investigate, "stage-investigate")]
    [Arguments(AgentTaskRole.Plan, "stage-plan")]
    [Arguments(AgentTaskRole.TestDesign, "stage-test-design")]
    [Arguments(AgentTaskRole.Code, "stage-code")]
    [Arguments(AgentTaskRole.Review, "stage-review")]
    public void a_stage_worker_carries_its_stage_bundle_then_the_basics(AgentTaskRole role, string stageKey)
    {
        InstructionBundles.ForDelegate(AgentTaskKind.Worker, role).ShouldBe([stageKey, "delegate-basics"]);
    }

    [Test]
    public void an_investigate_worker_carries_the_stage_bundle_and_a_docs_worker_does_not()
    {
        // Positive control for the IsStage map: Docs is a helper, Investigate is a stage. Dropping
        // the IsStage guard in ForDelegate makes Investigate match Docs and this goes red.
        InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Investigate)
            .ShouldBe([InstructionBundles.StageInvestigate, InstructionBundles.DelegateBasics]);
        InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Docs)
            .ShouldBe([InstructionBundles.DelegateBasics]);
    }

    [Test]
    public void a_sub_orchestrator_carries_its_own_contract_first_then_the_basics()
    {
        InstructionBundles.ForDelegate(AgentTaskKind.Orchestrator, AgentTaskRole.Plan)
            .ShouldBe(["orchestrator", "delegate-basics"]);
        InstructionBundles.ForDelegate(AgentTaskKind.Orchestrator, AgentTaskRole.Investigate)
            .ShouldBe(["orchestrator", "delegate-basics"],
                "a sub-orchestrator is not a pipeline stage even when its role is one");
    }

    [Test]
    [Arguments(AgentTaskRole.Check)]
    [Arguments(AgentTaskRole.Distill)]
    [Arguments(AgentTaskRole.Diagnose)]
    public void a_specialist_task_carries_nothing(AgentTaskRole role)
    {
        // A standing specialist has no tools and a deny-all hook: "commit and push each
        // slice" is an instruction it cannot obey, and its own contract already rides its agent row.
        InstructionBundles.ForDelegate(AgentTaskKind.Worker, role).ShouldBeEmpty();
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

    // ---- CARD-0146 S3 stage bundles -------------------------------------------------------------

    [Test]
    [Arguments(InstructionBundles.StageInvestigate)]
    [Arguments(InstructionBundles.StagePlan)]
    [Arguments(InstructionBundles.StageTestDesign)]
    [Arguments(InstructionBundles.StageCode)]
    [Arguments(InstructionBundles.StageReview)]
    public void each_stage_bundle_is_ascii_and_under_the_size_cap(string key)
    {
        var text = InstructionBundles.TextOf(key);

        text.Length.ShouldBeLessThanOrEqualTo(2_500, $"{key} is {text.Length} chars; stage bundles cap at 2,500");
        foreach (var ch in text)
        {
            ((int)ch).ShouldBeLessThan(128, $"{key} is not ASCII-safe (U+{(int)ch:X4})");
        }
        // Kind and today's state belong in routing / the brief, never here.
        text.ShouldNotContain("Grok");
        text.ShouldNotContain("Claude");
        text.ShouldNotContain("Codex");
        text.ShouldNotContain("fable");
        text.ShouldNotContain("JobObject");
        text.ShouldNotContain("CS8604");
    }

    [Test]
    public void stage_bundle_invariants_are_pinned_by_substring()
    {
        var investigate = InstructionBundles.TextOf(InstructionBundles.StageInvestigate);
        investigate.ShouldContain("Forbidden to design or implement a fix");
        investigate.ShouldContain("Not done, noted");

        var plan = InstructionBundles.TextOf(InstructionBundles.StagePlan);
        plan.ShouldContain("A design that only lives in chat is not a plan");
        plan.ShouldContain("## Verification design");
        plan.ShouldNotContain("WIP");

        var testDesign = InstructionBundles.TextOf(InstructionBundles.StageTestDesign);
        testDesign.ShouldContain("Every guard that protects a safety-critical assertion gets a PC-n positive control");
        testDesign.ShouldContain("### Positive controls");
        testDesign.ShouldContain("do not rewrite the fix design");

        var code = InstructionBundles.TextOf(InstructionBundles.StageCode);
        code.ShouldContain("Run each PC-n as red-then-green");
        code.ShouldContain("next: land only when every PC went red-then-green");
        code.ShouldNotContain("fast-forward");
        code.ShouldNotContain("deploy-local");

        var review = InstructionBundles.TextOf(InstructionBundles.StageReview);
        review.ShouldContain("Read-only");
        review.ShouldContain("Do not fix anything");
    }

    [Test]
    public void a_realistic_code_worker_composition_stays_under_the_command_line_budget()
    {
        // Role pair (stage-code + delegate-basics) plus the one attachment a Code delegate
        // actually carries (board-api). No launch composes the whole catalog.
        var budget = new DelegationSettings().CommandLineBudgetChars;
        var keys = InstructionBundles.ForDelegate(
            AgentTaskKind.Worker, AgentTaskRole.Code, [InstructionBundles.BoardApi]);

        keys.ShouldBe([
            InstructionBundles.StageCode,
            InstructionBundles.DelegateBasics,
            InstructionBundles.BoardApi,
        ]);
        var composed = InstructionBundleComposer.Compose(keys);
        composed.Text.Length.ShouldBeLessThan(budget);
        Should.NotThrow(() => InstructionBundleComposer.EnsureWithinCommandLineBudget(
            composed,
            ["--name", "task-1a2b3c4d", "--model", "opus", "--session-id", Guid.NewGuid().ToString("D")],
            budget,
            "Worker/Code"));
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
