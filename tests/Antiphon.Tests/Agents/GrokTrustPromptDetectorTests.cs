using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0315: Grok Build 1.0.13's directory-trust dialog is not Codex's, even though the
/// question text is the same. The detector must fire on the live worktree screen and must
/// not fire on Codex's "Yes, continue" form — the answer keys differ (<c>y</c> vs Enter).
/// </summary>
[Category("Unit")]
public class GrokTrustPromptDetectorTests
{
    // Flattened from the three CARD-0315 stuck sessions (82232d90, 3bb7bde0, 8e8e1ce3).
    private const string GrokTrustScreen = """
        Do you trust the contents of this directory?
            C:\Antiphon\worktrees\card-task-8e8e1ce3

        Grok Build may run or modify contents in this directory,
                         posing security risks.

                     Yes, proceed                 y
                     No, quit                     n

                                                   Grok Build  1.0.13 [stable]
        """;

    private const string CodexTrustScreen = """
        Do you trust the contents of this directory?
        › 1. Yes, continue  2. No, quit
        """;

    [Test]
    public void Matches_the_live_CARD_0315_worktree_screen()
    {
        GrokTrustPromptDetector.IsVisible(GrokTrustScreen).ShouldBeTrue();
        GrokTrustPromptDetector.IsVisibleOnScreen(GrokTrustScreen).ShouldBeTrue();
        GrokTrustPromptDetector.AffirmativeKey.ShouldBe("y");
    }

    [Test]
    public void Does_not_match_Codex_yes_continue()
    {
        GrokTrustPromptDetector.IsVisible(CodexTrustScreen).ShouldBeFalse();
        CodexTrustPromptDetector.IsVisible(CodexTrustScreen).ShouldBeTrue();
    }

    [Test]
    public void Codex_detector_does_not_match_Grok_yes_proceed()
    {
        CodexTrustPromptDetector.IsVisible(GrokTrustScreen).ShouldBeFalse();
    }

    [Test]
    public void Empty_or_composer_screen_is_not_the_dialog()
    {
        GrokTrustPromptDetector.IsVisible(null).ShouldBeFalse();
        GrokTrustPromptDetector.IsVisible("").ShouldBeFalse();
        GrokTrustPromptDetector.IsVisibleOnScreen("> ").ShouldBeFalse();
    }
}
