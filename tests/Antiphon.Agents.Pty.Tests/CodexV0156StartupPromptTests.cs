using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0662: codex-cli 0.156.1 renamed the folder-trust modal ("Trust this folder?" /
/// "› 1. Trust and continue  2. Quit") and shows a sign-in chooser above "Press enter to
/// continue". Fixtures are the CARD-0660 Linux captures.
/// </summary>
[Category("Unit")]
public class CodexV0156StartupPromptTests
{
    private const string LegacyTrustScreen = """
        Do you trust the contents of this directory?
        › 1. Yes, continue  2. No, quit
        """;

    [Test]
    public void V0156_trust_modal_is_recognised_on_the_current_screen()
    {
        var screen = CodexStartupFixtures.V0156TrustPrompt;
        CodexTrustPromptDetector.IsVisibleOnCurrentScreen(screen).ShouldBeTrue();
        CodexTrustPromptDetector.IsVisible(screen).ShouldBeTrue();
        CodexTrustPromptDetector.IsVisible(rawSnapshot: null, renderedScreen: screen).ShouldBeTrue();
    }

    [Test]
    public void V0156_trust_modal_classifies_as_Trust()
    {
        var observation = CodexStartupScreen.Classify(CodexStartupFixtures.V0156TrustPrompt);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.Trust);
    }

    [Test]
    public void V0156_accept_is_authorised_only_while_Trust_and_continue_is_highlighted()
    {
        CodexTrustPromptDetector.IsAcceptSelectedOnCurrentScreen(CodexStartupFixtures.V0156TrustPrompt)
            .ShouldBeTrue("highlight on 1. Trust and continue");

        var quit = CodexStartupFixtures.V0156TrustPromptQuitHighlighted;
        CodexTrustPromptDetector.IsVisibleOnCurrentScreen(quit).ShouldBeTrue("still the trust modal");
        CodexTrustPromptDetector.IsAcceptSelectedOnCurrentScreen(quit)
            .ShouldBeFalse("Enter would take 2. Quit");
    }

    [Test]
    public void Legacy_trust_wording_is_still_recognised_and_accepted()
    {
        CodexTrustPromptDetector.IsVisible(LegacyTrustScreen).ShouldBeTrue();
        CodexTrustPromptDetector.IsVisibleOnCurrentScreen(LegacyTrustScreen).ShouldBeTrue();
        CodexTrustPromptDetector.IsAcceptSelectedOnCurrentScreen(LegacyTrustScreen).ShouldBeTrue();
        CodexStartupScreen.Classify(LegacyTrustScreen).Reason.ShouldBe(CodexStartupReason.Trust);
    }

    [Test]
    public void V0156_signed_out_screen_classifies_as_SignIn_not_BlockingUpdate()
    {
        var observation = CodexStartupScreen.Classify(CodexStartupFixtures.V0156SignedOut);
        observation.IsReady.ShouldBeFalse();
        observation.Reason.ShouldBe(CodexStartupReason.SignIn);
    }

    [Test]
    public void V0156_ready_capture_is_neither_trust_nor_sign_in()
    {
        var screen = CodexStartupFixtures.V0156Ready;
        CodexTrustPromptDetector.IsVisible(screen).ShouldBeFalse();
        CodexTrustPromptDetector.IsVisibleOnCurrentScreen(screen).ShouldBeFalse();
        CodexTrustPromptDetector.IsAcceptSelectedOnCurrentScreen(screen).ShouldBeFalse();
        var reason = CodexStartupScreen.Classify(screen).Reason;
        reason.ShouldNotBe(CodexStartupReason.Trust);
        reason.ShouldNotBe(CodexStartupReason.SignIn);
        CodexTrustPromptDetector.IsAcceptSelectedOnCurrentScreen(CodexStartupFixtures.V0156SignedOut)
            .ShouldBeFalse();
    }

    [Test]
    public async Task Ready_wait_answers_the_V0156_trust_modal_with_one_Enter_then_settles()
    {
        var time = new FakeTimeProvider();
        var writes = new List<string>();
        var readyReads = 0;
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                lock (writes)
                {
                    if (writes.Count == 0)
                        return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.V0156TrustPrompt));
                }

                Interlocked.Increment(ref readyReads);
                return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3));
            },
            Options(time, maxMs: 60_000),
            (input, _) =>
            {
                lock (writes)
                    writes.Add(input);
                return Task.CompletedTask;
            });

        await WaitUntilAsync(() => Volatile.Read(ref readyReads) >= 1);
        await Task.Delay(25);
        time.Advance(TimeSpan.FromMilliseconds(1_050));
        (await gate).ShouldBeTrue();
        writes.ShouldBe(["\r"]);
    }

    [Test]
    public async Task Ready_wait_never_sends_Enter_while_Quit_is_highlighted()
    {
        var time = new FakeTimeProvider();
        var writes = new List<string>();
        var diagnostics = new List<string>();
        var reads = 0;
        var options = Options(time, maxMs: 500);
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult<CodexStartupSnapshot?>(
                    Snap(CodexStartupFixtures.V0156TrustPromptQuitHighlighted));
            },
            new CodexReadyWaitOptions
            {
                TimeProvider = options.TimeProvider,
                Settle = options.Settle,
                MaxWait = options.MaxWait,
                PollInterval = options.PollInterval,
                BootStatusThreshold = options.BootStatusThreshold,
                OnDiagnostic = d => { lock (diagnostics) diagnostics.Add(d); },
            },
            (input, _) =>
            {
                lock (writes)
                    writes.Add(input);
                return Task.CompletedTask;
            });

        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1);
        await Task.Delay(25);
        time.Advance(TimeSpan.FromMilliseconds(500));
        (await gate).ShouldBeFalse();
        writes.ShouldBeEmpty();
        diagnostics.ShouldContain(d => d.Contains("reason=Trust", StringComparison.Ordinal));
    }

    private static CodexReadyWaitOptions Options(FakeTimeProvider time, int maxMs) => new()
    {
        TimeProvider = time,
        Settle = TimeSpan.FromMilliseconds(1_000),
        MaxWait = TimeSpan.FromMilliseconds(maxMs),
        PollInterval = TimeSpan.FromMilliseconds(50),
        BootStatusThreshold = TimeSpan.FromMilliseconds(10_000),
    };

    private static CodexStartupSnapshot Snap(string screen) => new(screen, screen);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
                throw new TimeoutException("condition not met");
            await Task.Yield();
        }
    }
}
