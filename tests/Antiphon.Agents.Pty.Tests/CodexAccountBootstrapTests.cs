using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-1508. The 2026-10-22 desktop frame is a provider bootstrap timeout, not Unknown,
/// not a sign-in, and not a quota hold. Readiness still withholds and still sends nothing.
/// </summary>
[Category("Unit")]
public class CodexAccountBootstrapTests
{
    public const string Exact =
        "Error: account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code -32603)";

    public const string Wrapped =
        "Error: account/read failed during TUI bootstrap: account/read failed:\nworkspace routing discovery timed out\n(code -32603)";

    [Test]
    public void Exact_pre_prompt_frame_is_account_bootstrap()
    {
        var observation = CodexStartupScreen.Classify(Welcome(Exact), Exact);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ToString().ShouldBe("AccountBootstrap");
        observation.Reason.ShouldNotBe(CodexStartupReason.SignIn);
        observation.Reason.ShouldNotBe(CodexStartupReason.Unknown);
        CodexAccountBootstrap.TryOutcome(
            Welcome(Exact), Exact, "elapsedMs=43954", "/tmp/antiphon-codex-startup/example.txt", 1)
            .ShouldBe(
                "AccountBootstrap: Error: account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code -32603); elapsedMs=43954; capture=/tmp/antiphon-codex-startup/example.txt; exitCode=1");
    }

    [Test]
    public void Wrapped_32603_frame_is_the_same_account_bootstrap()
    {
        var observation = CodexStartupScreen.Classify(Welcome(Wrapped), Wrapped);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ToString().ShouldBe("AccountBootstrap");
    }

    [Test]
    public void Sentence_in_an_agent_message_above_a_ready_composer_stays_ready()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, "• " + Exact);
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeTrue();
        observation.Reason.ShouldBe(CodexStartupReason.Ready);
        IdlePointer(screen);
        NoBootstrapOutcome(screen, Exact);
    }

    [Test]
    public void A_bare_32603_on_a_ready_composer_stays_ready()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, "internal error (code -32603)");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeTrue();
        observation.Reason.ToString().ShouldNotBe("AccountBootstrap");
        observation.Reason.ShouldNotBe(CodexStartupReason.SignIn);
    }

    [Test]
    public void Usage_limit_32603_is_not_an_account_bootstrap_or_a_sign_in()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, "You have hit your usage limit (code -32603)");
        var observation = CodexStartupScreen.Classify(screen);
        observation.IsReady.ShouldBeTrue();
        observation.Reason.ToString().ShouldNotBe("AccountBootstrap");
        observation.Reason.ShouldNotBe(CodexStartupReason.SignIn);
    }

    [Test]
    public void Account_read_without_the_discovery_timeout_is_not_this_failure()
    {
        var screen = Welcome(
            "Error: account/read failed during TUI bootstrap: account/read failed: something else (code -32603)");
        var observation = CodexStartupScreen.Classify(screen, screen);
        observation.Reason.ToString().ShouldNotBe("AccountBootstrap");
        observation.Reason.ShouldNotBe(CodexStartupReason.SignIn);
    }

    [Test]
    public void Sign_in_wins_when_the_rendered_screen_is_sign_in()
    {
        var screen = "Welcome to Codex\nSign in with ChatGPT\n" + Exact;
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.SignIn);
    }

    [Test]
    public void Sign_in_wins_when_only_the_raw_tail_has_the_bootstrap_error()
    {
        const string screen = "Welcome to Codex\nSign in with ChatGPT\nPress enter to continue\n";
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.Reason.ShouldBe(CodexStartupReason.SignIn);
        observation.IsReady.ShouldBeFalse();
    }

    [Test]
    public async Task Fatal_frame_withholds_readiness_without_sending_input()
    {
        var time = new FakeTimeProvider();
        var writes = new List<string>();
        var diagnostics = new List<string>();
        var screen = Welcome(Exact);
        var gate = CodexReadyWait.WaitAsync(
            _ => Task.FromResult<CodexStartupSnapshot?>(new CodexStartupSnapshot(screen, Exact)),
            new CodexReadyWaitOptions
            {
                TimeProvider = time,
                MaxWait = TimeSpan.FromMilliseconds(200),
                PollInterval = TimeSpan.FromMilliseconds(50),
                Settle = TimeSpan.FromMilliseconds(50),
                OnDiagnostic = diagnostics.Add,
            },
            (input, _) =>
            {
                writes.Add(input);
                return Task.CompletedTask;
            });

        var watch = Stopwatch.StartNew();
        while (!gate.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(10);
        }

        (await gate.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBeFalse();
        writes.ShouldBeEmpty();
        var log = string.Join('\n', diagnostics);
        log.ShouldContain("not-ready reason=AccountBootstrap");
        log.ShouldNotContain("account/read failed");
    }

    [Test]
    public void Quoting_composer_stays_readable()
    {
        var screen = CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.P3, "› " + Exact);
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.Unknown);
        observation.Reason.ShouldNotBe(CodexStartupReason.AccountBootstrap);
        CodexPointerComposerEvidence.IsComplete(screen, Exact).ShouldBeTrue();
        CodexPointerComposerEvidence.MatchingPrefixLength(screen, Exact).ShouldBe(Compact(Exact));
        NoBootstrapOutcome(screen, Exact);
    }

    [Test]
    public void Ready_screen_with_the_sentence_only_in_the_raw_tail_stays_ready()
    {
        var observation = CodexStartupScreen.Classify(CodexStartupFixtures.P3, Exact);
        observation.IsReady.ShouldBeTrue();
        observation.Reason.ShouldBe(CodexStartupReason.Ready);
        IdlePointer(CodexStartupFixtures.P3);
        NoBootstrapOutcome(CodexStartupFixtures.P3, Exact);
    }

    [Test]
    public void Sentence_in_scrollback_above_the_banner_stays_ready()
    {
        var screen = Exact + "\n\n(older turn)\n\n" + CodexStartupFixtures.P3;
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeTrue();
        observation.Reason.ShouldBe(CodexStartupReason.Ready);
        IdlePointer(screen);
        NoBootstrapOutcome(screen, Exact);
    }

    [Test]
    public void Sentence_in_a_code_block_above_a_ready_composer_stays_ready()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(CodexStartupFixtures.P3, "```");
        screen = CodexStartupFixtures.InsertBeforeComposer(screen, Exact);
        screen = CodexStartupFixtures.InsertBeforeComposer(screen, "```");
        var observation = CodexStartupScreen.Classify(screen, screen);
        observation.IsReady.ShouldBeTrue();
        observation.Reason.ShouldBe(CodexStartupReason.Ready);
        IdlePointer(screen);
        NoBootstrapOutcome(screen, screen);
    }

    [Test]
    public void Partially_drawn_composer_prefix_stays_readable()
    {
        const string partial = "Error: account/read failed during TUI bootstrap:";
        var screen = CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.P3, "› " + partial);
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.Unknown);
        CodexPointerComposerEvidence.IsComplete(screen, partial).ShouldBeTrue();
        CodexPointerComposerEvidence.IsComplete(screen, Exact).ShouldBeFalse();
        CodexPointerComposerEvidence.MatchingPrefixLength(screen, Exact).ShouldBe(Compact(partial));
        NoBootstrapOutcome(screen, Exact);
    }

    [Test]
    public void Glyphless_partial_row_on_ready_chrome_stays_unknown()
    {
        var screen = CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.P3, Exact);
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.Unknown);
        CodexPointerComposerEvidence.IsComplete(screen, Exact).ShouldBeFalse();
        CodexPointerComposerEvidence.MatchingPrefixLength(screen, Exact).ShouldBe(0);
        NoBootstrapOutcome(screen, Exact);
    }

    [Test]
    public void Resumed_session_with_the_old_error_in_history_stays_ready()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, "• Resumed session 700478bc");
        screen = CodexStartupFixtures.InsertBeforeComposer(screen, Exact);
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeTrue();
        observation.Reason.ShouldBe(CodexStartupReason.Ready);
        IdlePointer(screen);
        NoBootstrapOutcome(screen, Exact);
    }

    [Test]
    public void Wrapped_quote_in_the_composer_stays_readable()
    {
        const string idle = "› Ask Codex to do anything";
        const string block =
            "› Error: account/read failed during TUI bootstrap: account/read failed:\n"
            + "  workspace routing discovery timed out\n"
            + "  (code -32603)";
        var screen = CodexStartupFixtures.P3.Replace(idle, block, StringComparison.Ordinal);
        const string body =
            "Error: account/read failed during TUI bootstrap: account/read failed:"
            + "  workspace routing discovery timed out"
            + "  (code -32603)";
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.Unknown);
        CodexPointerComposerEvidence.IsComplete(screen, body).ShouldBeTrue();
        CodexPointerComposerEvidence.MatchingPrefixLength(screen, body).ShouldBe(Compact(body));
        NoBootstrapOutcome(screen, Exact);
    }

    [Test]
    public void Composer_and_raw_tail_quoting_the_sentence_stay_readable()
    {
        var screen = CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.P3, "› " + Exact);
        var observation = CodexStartupScreen.Classify(screen, screen);
        observation.Reason.ShouldBe(CodexStartupReason.Unknown);
        CodexPointerComposerEvidence.IsComplete(screen, Exact).ShouldBeTrue();
        CodexPointerComposerEvidence.MatchingPrefixLength(screen, Exact).ShouldBe(Compact(Exact));
        NoBootstrapOutcome(screen, screen);
    }

    [Test]
    public void Colored_fatal_frame_clause_drops_csi()
    {
        const string colored =
            "Error: account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code \u001b[31m-32603\u001b[0m)";
        var screen = Welcome(colored);
        var observation = CodexStartupScreen.Classify(screen, colored);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.AccountBootstrap);
        var outcome = CodexAccountBootstrap.TryOutcome(
            screen, colored, "elapsedMs=43954", "/tmp/antiphon-codex-startup/example.txt", 1);
        outcome.ShouldBe(
            "AccountBootstrap: Error: account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code -32603); elapsedMs=43954; capture=/tmp/antiphon-codex-startup/example.txt; exitCode=1");
        outcome.ShouldNotContain("[31m");
        outcome.ShouldNotContain("\u001b");
    }

    [Test]
    public void Blank_rendered_raw_tail_is_still_account_bootstrap()
    {
        var observation = CodexStartupScreen.Classify(" \n", Exact);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.AccountBootstrap);
        var outcome = CodexAccountBootstrap.TryOutcome(
            " \n", Exact, "elapsedMs=43954", "/tmp/antiphon-codex-startup/example.txt", 1);
        outcome.ShouldNotBeNull();
        outcome.ShouldContain("AccountBootstrap: Error: account/read failed during TUI bootstrap");
        outcome.ShouldContain("(code -32603)");
    }

    private static void IdlePointer(string screen)
    {
        var hint = CodexStartupScreen.HintAskAnything;
        CodexPointerComposerEvidence.IsComplete(screen, hint).ShouldBeTrue();
        CodexPointerComposerEvidence.MatchingPrefixLength(screen, hint).ShouldBe(Compact(hint));
    }

    private static void NoBootstrapOutcome(string rendered, string raw) =>
        CodexAccountBootstrap.TryOutcome(
            rendered, raw, "elapsedMs=43954", "/tmp/antiphon-codex-startup/example.txt", 1)
            .ShouldBeNull();

    private static int Compact(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
                count++;
        }

        return count;
    }

    private static string Welcome(string error) =>
        "OpenAI Codex (v0.160.0)\npermissions: YOLO mode\n" + error + "\n";
}
