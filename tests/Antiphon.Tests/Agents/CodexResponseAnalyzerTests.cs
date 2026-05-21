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

    [Test]
    public void TrustPromptDetector_matches_compacted_codex_directory_prompt()
    {
        const string raw = "Do you trust the contents of this directory?\r\n";
        const string screen = "› 1. Yes, continue  2. No, quit";

        CodexTrustPromptDetector.IsVisible(raw, screen).ShouldBeTrue();
    }
}
