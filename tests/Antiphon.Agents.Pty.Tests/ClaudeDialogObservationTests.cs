using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

public class ClaudeDialogObservationTests
{
    // Permission/idle shapes transcribed in ClaudeStartupTrustPromptTests (Claude 2.1.258).
    // Question variants are protocol fixtures; native qualification is separately owed.
    private const string Idle = "────────────────────\n❯ \n────────────────────\n? for shortcuts";
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
}
