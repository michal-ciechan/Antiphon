using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0324: Grok 1.0.13's OAuth device-approval / welcome sign-in screens. Any one
/// compact-normalised anchor is a match. Never auto-answered; evaluated only at launch.
/// </summary>
[Category("Unit")]
public class GrokSignInPromptDetectorTests
{
    // Verbatim from the CARD-0324 plan's live measurement (grok 1.0.13, empty GROK_HOME).
    private const string MeasuredDeviceApprovalScreen = """
          ~/AppData\Local\Temp\...\scratchpad\cwd-fresh
                                                         Connecting...
                                         Approve in your browser to finish signing in.
                                                           FYED-XF4N
                                            Make sure your browser shows this code.
                                            If it doesn't open, click here to copy.
                                       Copying not working? Click here to show full URL.
                                                    Waiting for approval...
                                                         ctrl+q  quit
        """;

    private const string WelcomeTokenScreen = """
        Login with grok.com
        Paste your token here
        Switch account
        Logout
        ctrl+q  quit
        """;

    private const string TrustScreen = """
        Do you trust the contents of this directory?
            C:\Antiphon\worktrees\card-task-8e8e1ce3

                     Yes, proceed                 y
                     No, quit                     n
        """;

    private const string ReadyScreen = """
        C:\Antiphon\worktrees\card-task-8e8e1ce3

        >
        """;

    [Test]
    public void Matches_the_measured_device_approval_screen()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen(MeasuredDeviceApprovalScreen).ShouldBeTrue();
    }

    [Test]
    public void Matches_approve_in_your_browser()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("Approve in your browser to finish signing in.")
            .ShouldBeTrue();
    }

    [Test]
    public void Matches_waiting_for_approval()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("Waiting for approval...").ShouldBeTrue();
    }

    [Test]
    public void Matches_make_sure_your_browser_shows_this_code()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("Make sure your browser shows this code.")
            .ShouldBeTrue();
    }

    [Test]
    public void Matches_paste_your_token_here_welcome()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen(WelcomeTokenScreen).ShouldBeTrue();
        GrokSignInPromptDetector.IsVisibleOnScreen("Paste your token here").ShouldBeTrue();
    }

    [Test]
    public void Matches_open_this_url_in_your_browser_to_approve()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("Open this URL in your browser to approve")
            .ShouldBeTrue();
    }

    [Test]
    public void Matches_could_not_open_a_browser()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("Could not open a browser").ShouldBeTrue();
    }

    [Test]
    public void Matches_sign_in_to_grok()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("Sign in to Grok").ShouldBeTrue();
    }

    [Test]
    public void Matches_login_with_and_ctrl_q_together()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("Login with grok.com\nctrl+q  quit")
            .ShouldBeTrue();
    }

    [Test]
    public void Login_with_alone_is_not_the_sign_in_screen()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("Login with the team's SSO")
            .ShouldBeFalse();
    }

    [Test]
    public void Ready_screen_is_not_sign_in()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen(ReadyScreen).ShouldBeFalse();
    }

    [Test]
    public void Trust_dialog_is_not_sign_in()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen(TrustScreen).ShouldBeFalse();
        GrokTrustPromptDetector.IsVisibleOnScreen(TrustScreen).ShouldBeTrue();
    }

    [Test]
    public void A_working_turn_is_not_sign_in()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen("""
            Worked for 1.7s
            FAKE response to: implement the detector
            >
            """).ShouldBeFalse();
    }

    [Test]
    public void A_reply_that_mentions_approval_without_the_anchors_is_not_sign_in()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen(
            "The user must approve the plan before I continue. Run grok login only if asked.")
            .ShouldBeFalse();
    }

    [Test]
    public void Empty_or_null_is_not_sign_in()
    {
        GrokSignInPromptDetector.IsVisibleOnScreen(null).ShouldBeFalse();
        GrokSignInPromptDetector.IsVisibleOnScreen("").ShouldBeFalse();
        GrokSignInPromptDetector.IsVisibleOnScreen("> ").ShouldBeFalse();
    }

    [Test]
    public void BlockReason_names_GROK_HOME_and_grok_login()
    {
        var home = @"C:\Users\mike\.grok";
        var reason = GrokSignInPromptDetector.BlockReason(home);
        reason.ShouldContain("ProviderSignInRequired");
        reason.ShouldContain(Path.Combine(home, "auth.json"));
        reason.ShouldContain("grok login");
        reason.ShouldContain("Nothing was typed");
    }
}
