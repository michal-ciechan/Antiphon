using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class CodexResponseAnalyzerTests
{
    [Test]
    public void IsAskingQuestion_ignores_question_mark_in_echoed_prompt()
    {
        const string prompt = "Please inspect this?";
        const string raw = "\x1b]0;codex\aPlease inspect this?\r\nI inspected it and found no blocker.\r\n";

        CodexResponseAnalyzer.IsAskingQuestion(raw, prompt).ShouldBeFalse();
    }

    [Test]
    public void IsAskingQuestion_detects_question_in_response()
    {
        const string prompt = "Continue the work";
        const string raw = "Continue the work\r\nI need one decision: should I update the tests too?\r\n";

        CodexResponseAnalyzer.IsAskingQuestion(raw, prompt).ShouldBeTrue();
    }

    [Test]
    public void ExtractResponse_strips_ansi_and_prompt_echo()
    {
        const string prompt = "Run analysis";
        const string raw = "\x1b[32mRun analysis\x1b[0m\r\n\x1b[1mAnalysis complete\x1b[0m\r\n";

        CodexResponseAnalyzer.ExtractResponse(raw, prompt).ShouldBe("Analysis complete");
    }

    // The echo is hard-wrapped by the terminal at the window width, so the prompt is not
    // literally present in the snapshot — the break lands between "has a " and "question?".
    // Shape copied from a real ConPTY capture, where cmd's cwd prefix pushed the echo past
    // column 120. Before the wrap-aware match this left the whole echo in the response.
    private const string WrappedEchoRaw =
        "\rC:\\deep\\worktree\\path\\tests\\Antiphon.Tests\\bin-verify>echo answer has no question & rem prompt has a \r\n"
        + "question?\x1b[K\r\n"
        + "\x1b]0;cmd.exe\aanswer has no question\r\n";

    [Test]
    public void ExtractResponse_strips_a_prompt_echo_wrapped_at_the_terminal_margin()
    {
        const string prompt = "echo answer has no question & rem prompt has a question?";

        CodexResponseAnalyzer.ExtractResponse(WrappedEchoRaw, prompt)
            .ShouldBe("answer has no question");
    }

    [Test]
    public void IsAskingQuestion_ignores_question_mark_in_a_wrapped_prompt_echo()
    {
        const string prompt = "echo answer has no question & rem prompt has a question?";

        CodexResponseAnalyzer.IsAskingQuestion(WrappedEchoRaw, prompt).ShouldBeFalse();
    }

    [Test]
    public void IsAskingQuestion_still_detects_a_question_after_a_wrapped_prompt_echo()
    {
        const string prompt = "review the adapter and tell me what you think of the wrapping";
        const string raw =
            "\rC:\\deep\\worktree\\path>review the adapter and tell me what you think of the \r\n"
            + "wrapping\r\n"
            + "Should I also update the tests?\r\n";

        CodexResponseAnalyzer.IsAskingQuestion(raw, prompt).ShouldBeTrue();
    }

    [Test]
    public void TrustPromptDetector_matches_compacted_codex_directory_prompt()
    {
        const string raw = "Do you trust the contents of this directory?\r\n";
        const string screen = "› 1. Yes, continue  2. No, quit";

        CodexTrustPromptDetector.IsVisible(raw, screen).ShouldBeTrue();
    }
}
