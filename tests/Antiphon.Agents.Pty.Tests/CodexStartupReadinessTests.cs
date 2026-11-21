using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class CodexStartupReadinessTests
{
    [Test]
    public void Grounded_loaded_model_composer_layouts_are_ready()
    {
        Ready(CodexStartupFixtures.P1).ShouldBeTrue("V-1: P-1");
        Ready(CodexStartupFixtures.P2).ShouldBeTrue("V-1: P-2");
        Ready(CodexStartupFixtures.P3).ShouldBeTrue("V-1: P-3");
        Ready(CodexStartupFixtures.DerivedWriteTestsHint).ShouldBeTrue("V-1: Write tests hint");
        Ready(CodexStartupFixtures.BlankComposer(CodexStartupFixtures.P3)).ShouldBeTrue("V-1: blank composer");
        Ready(CodexStartupFixtures.AsciiGlyph(CodexStartupFixtures.P3)).ShouldBeTrue("V-1: ASCII >");
        Ready(CodexStartupFixtures.WithCrlf(CodexStartupFixtures.P3)).ShouldBeTrue("V-1: CRLF");
        Ready(CodexStartupFixtures.WithTrailingPadding(CodexStartupFixtures.P3)).ShouldBeTrue("V-1: trailing padding");
        Ready(CodexStartupFixtures.ReplaceModelValue(CodexStartupFixtures.P3, "other-model low"))
            .ShouldBeTrue("V-1: model/effort not hard-coded");
        Ready(CodexStartupFixtures.ClipFooter(CodexStartupFixtures.P3))
            .ShouldBeFalse("V-1: clipped footer is unknown");
        Ready(CodexStartupFixtures.BannerOnly(CodexStartupFixtures.P3))
            .ShouldBeFalse("V-1: banner-only is unknown");
    }

    [Test]
    public void Missing_codex_banner_is_unknown()
    {
        var screen = CodexStartupFixtures.DropBanner(CodexStartupFixtures.P3);
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-1: missing-banner layout");
        observation.Reason.ShouldBe(CodexStartupReason.Unknown, "R-1");
    }

    [Test]
    public void Empty_model_row_is_unknown()
    {
        var screen = CodexStartupFixtures.EraseModelValue(CodexStartupFixtures.P3);
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-2: only the model value erased");
        observation.Reason.ShouldBe(CodexStartupReason.Unknown, "R-2");
    }

    [Test]
    public void Loading_model_never_borrows_the_footer_model()
    {
        var screen = CodexStartupFixtures.ReplaceModelValue(CodexStartupFixtures.P3, "loading");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-3: P-3's model changed to loading");
        observation.Reason.ShouldBe(CodexStartupReason.Loading, "R-3");
    }

    [Test]
    [Arguments("appended", "› Ask Codex to do anything please")]
    [Arguments("unknown-hint", "› Do something clever")]
    [Arguments("continuation", "› Ask Codex to do anything\n  and also this")]
    [Arguments("pasted-chip", "› Ask Codex to do anything  [pasted]")]
    public void Composer_content_must_be_empty_or_an_exact_supported_hint(string _, string composer)
    {
        string screen;
        if (composer.Contains('\n'))
        {
            var parts = composer.Split('\n');
            screen = CodexStartupFixtures.InsertAfterComposer(
                CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.P3, parts[0]),
                parts[1]);
        }
        else
        {
            screen = CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.P3, composer);
        }

        CodexStartupScreen.Classify(screen).IsReady
            .ShouldBeFalse("R-4: appended text, unknown hint, continuation and pasted chip");
    }

    [Test]
    public void Historical_composer_above_an_unknown_bottom_is_not_ready()
    {
        var screen = CodexStartupFixtures.HistoricalComposerAboveUnknownBottom(CodexStartupFixtures.P3);
        CodexStartupScreen.Classify(screen).IsReady
            .ShouldBeFalse("R-5: decoy pair above an unknown bottom");
    }

    [Test]
    [Arguments("missing", "")]
    [Arguments("partial", "  gpt-6-astra xhigh")]
    [Arguments("context-left", "  100% context left")]
    public void Missing_or_partial_footer_keeps_the_composer_unknown(string _, string footer)
    {
        var screen = CodexStartupFixtures.ReplaceFooter(CodexStartupFixtures.P3, footer);
        CodexStartupScreen.Classify(screen).IsReady
            .ShouldBeFalse("R-6: missing, partial and unknown footer, including P-3 with 100% context left");
    }

    [Test]
    [Arguments("1/3")]
    [Arguments("3/3")]
    public void Starting_mcp_blocks_every_progress_fraction(string fraction)
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3,
            $"Starting MCP servers ({fraction}): cua_repl, node_repl (2s  esc to interrupt)");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-7: 1/3 and 3/3 over otherwise positive P-3");
        observation.Reason.ShouldBe(CodexStartupReason.McpBoot, "R-7");
    }

    [Test]
    public void Booting_mcp_blocks_an_otherwise_ready_layout()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3,
            "Booting MCP server: codex_apps (0s • esc to interrupt)");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-8");
        observation.Reason.ShouldBe(CodexStartupReason.McpBoot, "R-8");
    }

    [Test]
    public void Incomplete_mcp_startup_is_a_blocker()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3,
            "MCP startup incomplete");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-9");
        observation.Reason.ShouldBe(CodexStartupReason.McpIncomplete, "R-9");
    }

    [Test]
    [Arguments("working", "• Working (12s • esc to interrupt)")]
    [Arguments("interrupt", "◦ Working (1s • esc to interrupt)")]
    public void Working_or_interrupt_state_withholds_readiness(string _, string indicator)
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(CodexStartupFixtures.P3, indicator);
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-10: each isolated busy indicator");
        observation.Reason.ShouldBe(CodexStartupReason.Working, "R-10");
    }

    [Test]
    public void Queue_mode_footer_blocks_a_loaded_model()
    {
        var screen = CodexStartupFixtures.ReplaceFooter(
            CodexStartupFixtures.P3,
            "  tab to queue message                                                                               100% context left");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-11");
        observation.Reason.ShouldBe(CodexStartupReason.QueueMode, "R-11");
    }

    [Test]
    public void Queued_follow_up_blocks_an_empty_composer()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.BlankComposer(CodexStartupFixtures.P3),
            "Queued follow-up inputs");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-12");
        observation.Reason.ShouldBe(CodexStartupReason.QueuedFollowUp, "R-12");
    }

    [Test]
    public void Uncleared_trust_is_not_a_ready_composer()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3,
            "Do you trust the contents of this directory?\nYes, continue");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-13");
        observation.Reason.ShouldBe(CodexStartupReason.Trust, "R-13");
    }

    [Test]
    public void Blocking_update_is_distinct_from_a_static_notice()
    {
        Ready(CodexStartupFixtures.P1).ShouldBeTrue("R-14: unmodified static notice still passes");
        Ready(CodexStartupFixtures.P3).ShouldBeTrue("R-14: P-3 static update notice still passes");
        var picker = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3,
            "Press enter to continue");
        var observation = CodexStartupScreen.Classify(picker);
        observation.IsReady.ShouldBeFalse("R-14: picker");
        observation.Reason.ShouldBe(CodexStartupReason.BlockingUpdate, "R-14");
    }

    [Test]
    public void V0156_and_v0158_update_modal_is_a_blocking_update()
    {
        // CARD-0777: this modal drew "enter continue · esc skip", classified Unknown, and was the
        // screen behind CARD-0772's reason=Unknown desktop timeouts.
        foreach (var modal in new[] { CodexStartupFixtures.V0156UpdateModal, CodexStartupFixtures.V0158UpdateModal })
        {
            modal.ShouldNotContain("Press enter to continue");
            var observation = CodexStartupScreen.Classify(modal);
            observation.IsReady.ShouldBeFalse();
            observation.Reason.ShouldBe(CodexStartupReason.BlockingUpdate);
        }

        Ready(CodexStartupFixtures.P3).ShouldBeTrue("the dismissed-version banner above a live composer still passes");
    }

    [Test]
    public void V0158_footer_only_model_with_empty_composer_is_ready()
    {
        var screen = CodexStartupFixtures.V0158FooterOnlyReady;
        screen.ShouldNotContain("model:");

        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeTrue("the measured desktop composer was idle despite the missing model row");
        observation.Reason.ShouldBe(CodexStartupReason.Ready);
        observation.NormalizedRegion.ShouldContain("GPT-5.6-Luna low");
    }

    [Test]
    public void Footer_only_fallback_does_not_repair_an_empty_or_loading_model_row()
    {
        var screen = CodexStartupFixtures.V0158FooterOnlyReady;
        var emptyRow = screen.Replace("  permissions: YOLO mode", "  model:\n  permissions: YOLO mode", StringComparison.Ordinal);
        var loadingRow = screen.Replace("  permissions: YOLO mode", "  model: loading\n  permissions: YOLO mode", StringComparison.Ordinal);
        CodexStartupScreen.Classify(emptyRow).Reason.ShouldBe(CodexStartupReason.Unknown);
        CodexStartupScreen.Classify(loadingRow).Reason.ShouldBe(CodexStartupReason.Loading);
    }

    [Test]
    public void Footer_only_fallback_still_requires_the_exact_idle_composer()
    {
        var screen = CodexStartupFixtures.V0158FooterOnlyReady.Replace(
            "› Ask Codex to do anything", "› Do something clever", StringComparison.Ordinal);
        CodexStartupScreen.Classify(screen).IsReady.ShouldBeFalse();
    }

    [Test]
    public void V0158_windows_sandbox_onboarding_is_a_named_blocker()
    {
        var observation = CodexStartupScreen.Classify(CodexStartupFixtures.V0158WindowsSandboxSetup);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.Sandbox);
    }

    [Test]
    [Arguments("sandbox setup")]
    [Arguments("input disabled")]
    public void Sandbox_setup_and_input_disabled_are_blockers(string phrase)
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(CodexStartupFixtures.P3, phrase);
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-15: each isolated phrase");
        observation.Reason.ShouldBe(CodexStartupReason.Sandbox, "R-15");
    }

    [Test]
    public void Sign_in_modal_is_not_ready()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3,
            "Please sign in");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeFalse("R-16");
        observation.Reason.ShouldBe(CodexStartupReason.SignIn, "R-16");
    }

    [Test]
    public void Incident_frames_are_never_ready()
    {
        CodexStartupScreen.Classify(CodexStartupFixtures.N1).IsReady
            .ShouldBeFalse("N-1 combined incident");
        CodexStartupScreen.Classify(CodexStartupFixtures.N2).IsReady
            .ShouldBeFalse("N-2 combined incident");
    }

    [Test]
    public void Codex_0170_shortcuts_row_under_the_footer_is_ready()
    {
        // Measured idle grid from server2 session 149a0bb6 (codex-cli 0.170.0). The last
        // non-empty row is the shortcuts row; nothing is drawn under it.
        const string sol = """
              >_ OpenAI Codex (v0.170.0)
                 /work/worktrees/ws-02-d3
              permissions: YOLO mode

              Tip: When goals are enabled, use /goal to set or inspect the objective for a long-running task.


            › Ask Codex to do anything

              GPT-6.2-Sol high · /work/worktrees/ws-02-d3
              ← for agents · ? for shortcuts                                                              ⚠ 2 warnings · f2 to view
            """;
        var solObs = CodexStartupScreen.Classify(sol);
        solObs.IsReady.ShouldBeTrue("0.170 Sol idle grid");
        solObs.Reason.ShouldBe(CodexStartupReason.Ready, "0.170 Sol idle grid");
        solObs.NormalizedRegion.ShouldContain("GPT-6.2-Sol high");
        solObs.NormalizedRegion.ShouldContain("GPT-6.2-Sol high · /work/worktrees/ws-02-d3");
        solObs.NormalizedRegion.ShouldContain("› Ask Codex to do anything");
        solObs.NormalizedRegion.ShouldNotContain("for agents");

        var astra = sol
            .Replace(
                "Tip: When goals are enabled, use /goal to set or inspect the objective for a long-running task.",
                "Tip: You can resume a previous conversation by running codex resume.",
                StringComparison.Ordinal)
            .Replace(
                "GPT-6.2-Sol high · /work/worktrees/ws-02-d3",
                "GPT-6.2-Astra high · /work/worktrees/ws-03-d3",
                StringComparison.Ordinal);
        var astraObs = CodexStartupScreen.Classify(astra);
        astraObs.IsReady.ShouldBeTrue("0.170 Astra idle grid");
        astraObs.Reason.ShouldBe(CodexStartupReason.Ready, "0.170 Astra idle grid");
        astraObs.NormalizedRegion.ShouldContain("GPT-6.2-Astra high");
        astraObs.NormalizedRegion.ShouldNotContain("for agents");

        var v0160 = CodexStartupScreen.Classify(CodexStartupFixtures.V0160Idle);
        v0160.IsReady.ShouldBeTrue("0.160 same-line warning chip stays the footer");
        v0160.Reason.ShouldBe(CodexStartupReason.Ready, "0.160 same-line warning chip stays the footer");

        const string signIn = """
              Welcome to Codex, OpenAI's command-line coding agent

              Choose how you want to use Codex.

            > 1. Sign in with ChatGPT
                 Usage included with Plus, Pro, Business, and Enterprise plans

              2. Sign in with Device Code
                 Sign in from another device with a one-time code

              3. Sign in another way
                 Use an API key or another provider

              Press enter to continue
            """;
        var signInObs = CodexStartupScreen.Classify(signIn);
        signInObs.IsReady.ShouldBeFalse("0.170 sign-in chooser");
        signInObs.Reason.ShouldBe(CodexStartupReason.SignIn, "0.170 sign-in chooser");

        var modal = CodexStartupScreen.Classify(CodexStartupFixtures.V0158UpdateModal);
        modal.IsReady.ShouldBeFalse("update modal");
        modal.Reason.ShouldBe(CodexStartupReason.BlockingUpdate, "update modal");

        const string shortcutsOnly = """
              >_ OpenAI Codex (v0.170.0)

            › Ask Codex to do anything

              ← for agents · ? for shortcuts                                                              ⚠ 2 warnings · f2 to view
            """;
        var shortcutsOnlyObs = CodexStartupScreen.Classify(shortcutsOnly);
        shortcutsOnlyObs.IsReady.ShouldBeFalse("shortcuts row is not a ready footer by itself");

        // Both phrases sit above the model footer. There is no shortcuts row under it,
        // so the footer must stay the last line.
        var noRowBelow = CodexStartupFixtures.V0160Idle.Replace(
            "Back for another round?",
            "Back for another round? ← for agents · ? for shortcuts",
            StringComparison.Ordinal);
        noRowBelow.ShouldContain("for agents");
        noRowBelow.ShouldContain("for shortcuts");
        var noRowObs = CodexStartupScreen.Classify(noRowBelow);
        noRowObs.IsReady.ShouldBeTrue("for-agents text with no row under the footer must not step off the model line");
        noRowObs.Reason.ShouldBe(CodexStartupReason.Ready);
        noRowObs.NormalizedRegion.ShouldContain("GPT-6.1-Sol low");
    }

    private static bool Ready(string screen) => CodexStartupScreen.Classify(screen).IsReady;
}
