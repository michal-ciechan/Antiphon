using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

public class ClaudeDialogObservationTests
{
    // Permission/idle shapes transcribed in ClaudeStartupTrustPromptTests (Claude 2.1.258).
    // Question variants are protocol fixtures; native qualification is separately owed.
    private const string Idle = "────────────────────\n❯ \n────────────────────\n? for shortcuts";
    // Live AZ Care screen, 2026-10-26: the 120-column hint bar wraps its "/rc" badge onto its own row.
    private static readonly string WrappedRcIdle =
        new string('─', 110) + " AZ Care ─\n>\n" + new string('─', 120) + "\n"
        + "  ⏵⏵ bypass permissions on (shift+tab to cycle) · ← for agents" + new string(' ', 38) + "◐ medium · /effort\n"
        + new string(' ', 115) + "/rc";
    private static string WrappedRcComposer(string draft, string hints) =>
        new string('─', 102) + " school-revision ─\n" + draft + "\n" + new string('─', 120) + "\n"
        + hints + "\n" + new string(' ', 115) + "/rc";
    private const string Permission = "Bash command\nDo you want to proceed?\n❯ 1. Yes\n  2. No\nEnter to confirm · Esc to cancel";

    [Test]
    public void C680_OnlyCurrentBoundedDialogsBlock()
    {
        foreach (var (label, screen, state) in new (string, string?, ClaudeDialogState)[]
        {
            ("permission", Permission, ClaudeDialogState.Blocked),
            ("old permission above live composer", Permission + "\n" + Idle, ClaudeDialogState.Clear),
            ("quoted prose", "The guide says Enter to confirm and Esc to cancel.\n" + Idle, ClaudeDialogState.Clear),
            ("draft containing choice text", Idle.Replace("❯ ", "❯ Enter to confirm · Esc to cancel"), ClaudeDialogState.Clear),
            ("single question", "Which option?\n❯ 1. First\n  2. Second\nEnter to select · Esc to cancel", ClaudeDialogState.Blocked),
            ("multi question", "Which options?\n❯ [ ] First\n  [ ] Second\nSpace to select · Enter to submit", ClaudeDialogState.Blocked),
            ("free text question", "Your answer?\n❯ Type something\nEnter to submit · Esc to cancel", ClaudeDialogState.Blocked),
            ("unknown choice", "Select mode\n❯ Fast\n  Slow\nEnter to confirm", ClaudeDialogState.Blocked),
            ("effort choice", "Use Fable 5.1 at high effort by default?\n> Keep xhigh\nSwitch Fable 5.1 to high effort", ClaudeDialogState.Blocked),
            ("idle", Idle, ClaudeDialogState.Clear),
            ("idle with wrapped /rc badge (Claude 2.1.310)", WrappedRcIdle, ClaudeDialogState.Clear),
            ("weekly usage banner (Claude 2.1.310)", WrappedRcComposer("> ",
                "  ⏵⏵ bypass permissions on (shift+tab to cycle)\n"
                + "  You've used 77% of your weekly limit · resets Oct 28, 11pm (Europe/London) · /usage-credits to keep using Claude Co…"),
                ClaudeDialogState.Clear),
            ("pasted draft hint (Claude 2.1.310)", WrappedRcComposer("> [Pasted text #1 +4 lines]",
                "  paste again to expand" + new string(' ', 92) + "/rc"), ClaudeDialogState.Clear),
            ("wrapped /rc badge with status (Claude 2.1.310)", WrappedRcIdle.Replace("/rc", "/rc connecting…"),
                ClaudeDialogState.Clear),
            ("prose under the composer", Idle + "\n/rc is a command", ClaudeDialogState.Unknown),
            ("working", "Reading files…\n✻ Working… (esc to interrupt)", ClaudeDialogState.Clear),
            ("stale working prose", "Earlier: esc to interrupt\nSome new unrecognized screen", ClaudeDialogState.Unknown),
            ("missing", null, ClaudeDialogState.Unknown),
            ("blank", "  \n", ClaudeDialogState.Unknown),
            ("partial", "❯", ClaudeDialogState.Unknown),
            ("oversized", new string('x', 65_537) + Idle, ClaudeDialogState.Unknown),
            ("too many rows", new string('\n', 513) + Idle, ClaudeDialogState.Unknown),
        })
            ClaudeDialogObservation.Observe(screen).State.ShouldBe(state, label);

        var permission = ClaudeDialogObservation.Observe(Permission);
        permission.Kind.ShouldBe(ClaudeBlockingPromptKind.ToolPermission);
        permission.CanCancel.ShouldBe(true);
        ClaudeDialogObservation.Observe("Do you want to proceed?\n1. Yes\n2. No").CanCancel.ShouldBeNull();
    }

    // Claude 2.1.322, Antiphon-Decisions session 7b80d3e0, 2026-11-10: typing /remote-control
    // opens a slash autocomplete under the composer and covers the hint bar. The draft is still
    // the command. A choice menu (numbered answers) under the box stays unreadable.
    [Test]
    public void C1833_SlashAutocompleteUnderComposerIsClear()
    {
        var rule = new string('─', 40);
        var screen = string.Join('\n',
            rule,
            ">\u00A0/remote-control",
            rule,
            "  > /remote-control                        Control this session from your phone or claude.ai/code",
            "    /rc-status                             Show remote-control (bridge) health",
            "                                           / DROPPED / NO-RC (user)",
            "    /launch-remote                         Launch a new Claude Code remote-control session");

        ClaudeScreen.TryReadComposer(screen, out var content).ShouldBeTrue();
        content.ShouldBe("/remote-control");
        ClaudeDialogObservation.Observe(screen).State.ShouldBe(ClaudeDialogState.Clear);

        var choiceUnderBox = string.Join('\n',
            rule, "> ", rule, "  1. Yes", "  2. No", "Enter to confirm · Esc to cancel");
        ClaudeScreen.TryReadComposer(choiceUnderBox, out _).ShouldBeFalse();
    }

    // Review e29fdd97. The committed choice sits at indent 2, which the palette reader already
    // rejects. Eight spaces is enough for the continuation predicate, so the same choice under
    // the captured palette is read as the composer and Clear.
    [Test]
    public void C1833_ChoiceAtIndent8UnderPaletteIsNotClear()
    {
        var screen = CapturedPalette() + "\n" + string.Join('\n',
            "        1. Yes",
            "        2. No",
            "        3. No, and tell Claude what to do differently",
            "        Enter to confirm · Esc to cancel");

        ClaudeScreen.TryReadComposer(screen, out _).ShouldBeFalse("choice-8space-under-palette");
        ClaudeDialogObservation.Observe(screen).State.ShouldNotBe(ClaudeDialogState.Clear);
    }

    // A wrapped description is not "any row indented at least 8". Prose at that indent, a choice
    // parked on the description column, and the confirm footer are not palette rows.
    [Test]
    public void C1833_IndentAloneIsNotAPaletteContinuation()
    {
        foreach (var (label, extra) in new (string, string)[]
        {
            ("indent-8 prose", "        not a slash description"),
            ("choice on the description column", new string(' ', 41) + "1. Yes"),
            ("parenthesised choice", "        (1) Yes"),
            ("bracketed choice", "        [1] Keep"),
            ("yes no rows", "        Yes\n        No"),
            ("do you want", "        Do you want to proceed?"),
            ("confirm footer", "        Enter to confirm · Esc to cancel"),
        })
        {
            var screen = CapturedPalette() + "\n" + extra;
            ClaudeScreen.TryReadComposer(screen, out _).ShouldBeFalse(label);
            ClaudeDialogObservation.Observe(screen).State.ShouldNotBe(ClaudeDialogState.Clear, label);
        }
    }

    // Enter on a slash palette selects the highlighted row. A draft of /remote-control must not
    // be Clear while the highlight is a different command.
    [Test]
    public void C1833_HighlightedCommandMustEqualDraft()
    {
        var screen = CapturedPalette(highlighted: "/resume");
        ClaudeScreen.TryReadComposer(screen, out var content).ShouldBeTrue();
        content.ShouldBe("/remote-control");
        ClaudeDialogObservation.Observe(screen).State.ShouldNotBe(
            ClaudeDialogState.Clear, "highlight-is-resume-draft-is-remote-control");

        foreach (var command in new[] { "/clear", "/compact", "/logout" })
        {
            var other = CapturedPalette(highlighted: command);
            ClaudeDialogObservation.Observe(other).State.ShouldNotBe(ClaudeDialogState.Clear, command);
        }
    }

    // A readable palette does not erase a blocking dialog that is still on the screen.
    [Test]
    public void C1833_BlockingDialogWithAPaletteIsNotClear()
    {
        var palette = CapturedPalette();
        foreach (var (label, dialog) in DialogFixtures())
        {
            ClaudeDialogObservation.Observe(dialog + "\n" + palette).State
                .ShouldNotBe(ClaudeDialogState.Clear, label + " above palette");
            ClaudeDialogObservation.Observe(palette + "\n" + dialog).State
                .ShouldNotBe(ClaudeDialogState.Clear, label + " below palette");
        }

        ClaudeDialogObservation.Observe(palette).State.ShouldBe(ClaudeDialogState.Clear);
    }

    private static string CapturedPalette(string highlighted = "/remote-control")
    {
        var rule = new string('─', 40);
        var prefix = "  > " + highlighted;
        var pad = Math.Max(2, 41 - prefix.Length);
        var highlight = prefix + new string(' ', pad)
            + "Control this session from your phone or claude.ai/code";
        return string.Join('\n',
            rule,
            ">\u00A0/remote-control",
            rule,
            highlight,
            "    /rc-status                             Show remote-control (bridge) health",
            "                                           / DROPPED / NO-RC (user)",
            "    /launch-remote                         Launch a new Claude Code remote-control session");
    }

    private static IEnumerable<(string Label, string Screen)> DialogFixtures()
    {
        yield return ("trust 2.1.258", """
            Quick safety check: Is this a project you created or one you trust?
            > No, exit
              Yes, I trust this folder
            Enter to confirm · Esc to cancel
            """);
        yield return ("numbered trust", """
            Quick safety check: Is this a project you created or one you trust?
            ❯ 1. Yes, I trust this folder
              2. No, exit
            Enter to confirm · Esc to cancel
            """);
        yield return ("permission 1/2", Permission);
        yield return ("permission 1/2/3", """
            Bash command
            Do you want to proceed?
            ❯ 1. Yes
              2. No
              3. No, and tell Claude what to do differently
            Enter to confirm · Esc to cancel
            """);
        yield return ("boxed permission", """
            ╭──────────────────────────────────────────────────────────╮
            │ Bash command                                             │
            │ Do you want to proceed?                                  │
            │ ❯ 1. Yes                                                 │
            │   2. No, and tell Claude what to do differently          │
            ╰──────────────────────────────────────────────────────────╯
            """);
        yield return ("bypass warning", """
            Claude Code is running with bypass permissions enabled; permission prompts are unavailable.
            Do you want to proceed?
            ❯ 1. Yes
              2. No
            Enter to confirm · Esc to cancel
            """);
        yield return ("usage picker", """
            Select extra usage
            ❯ 1. Use credits
              2. Stop
            Enter to confirm · Esc to cancel
            """);
        yield return ("remote control menu", """
            Remote Control
            This session is available in the Claude mobile app and at
            https://claude.ai/code/session_SYNTHETIC_C514_FIXTURE.
              Disconnect this session
              Show QR code
            > Continue
            Enter to select · Esc to continue
            """);
        yield return ("rate limit", "You've hit your session limit · resets 6:10pm (Europe/London)");
        yield return ("login", "Login expired · Please run /login");
        yield return ("model picker", """
            Select model
            ❯ 1. Opus
              2. Sonnet
            Enter to confirm · Esc to cancel
            """);
        yield return ("resume picker", """
            Resume session
            ❯ 1. antiphon-decisions
              2. other
            Enter to confirm · Esc to cancel
            """);
        yield return ("effort choice", """
            Use Fable 5.1 at high effort by default?
            > Keep xhigh
            Switch Fable 5.1 to high effort
            """);
        yield return ("codex trust", CodexStartupFixtures.V0156TrustPrompt);
        yield return ("codex update", CodexStartupFixtures.V0156UpdateModal);
        yield return ("codex update 0.158", CodexStartupFixtures.V0158UpdateModal);
        yield return ("codex signed out", CodexStartupFixtures.V0156SignedOut);
    }
}
