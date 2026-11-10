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
    }

    [Test]
    public void Wrapped_32603_frame_is_the_same_account_bootstrap()
    {
        var observation = CodexStartupScreen.Classify(Welcome(Wrapped), Wrapped);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ToString().ShouldBe("AccountBootstrap");
    }

    [Test]
    public void Fatal_line_above_a_ready_composer_stays_not_ready()
    {
        var screen = CodexStartupFixtures.InsertBeforeComposer(CodexStartupFixtures.P3, Exact);
        var observation = CodexStartupScreen.Classify(screen, Exact);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ToString().ShouldBe("AccountBootstrap");
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

    private static string Welcome(string error) =>
        "OpenAI Codex (v0.160.0)\npermissions: YOLO mode\n" + error + "\n";
}
