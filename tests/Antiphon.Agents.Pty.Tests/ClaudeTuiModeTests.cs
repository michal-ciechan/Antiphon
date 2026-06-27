using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Guards the assumption our PTY capture relies on: a spawned Claude Code agent renders with the classic
/// (inline) renderer, NOT the fullscreen / alternate-screen-buffer TUI. Fullscreen draws on the terminal's
/// alternate screen (<c>\e[?1049h</c>) with complex cursor positioning that our <c>TerminalScreen</c>
/// reconstruction can't faithfully replay. If a future Claude version starts entering the alternate screen
/// under our launch (even with the classic-renderer env we set), this test fails so we notice.
///
/// Opt-in: needs Windows ConPTY, <c>ANTIPHON_HEADED_TESTS=1</c>, and the <c>claude</c> CLI on PATH.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
public class ClaudeTuiModeTests
{
    [Test]
    public async Task Spawned_claude_uses_classic_renderer_not_alternate_screen()
    {
        ClSession.SkipIfNotEligible();

        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        // The same classic-renderer + no-auto-update env the server injects for Claude agents.
        var env = new Dictionary<string, string>
        {
            ["DISABLE_AUTOUPDATER"] = "1",
            ["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1",
        };

        await runner.StartAsync(app, args, cwd: Environment.CurrentDirectory, env: env, cols: 200, rows: 60);
        var ready = await new ClaudeReadyDetector { MaxWait = TimeSpan.FromSeconds(45) }.WaitAsync(runner);
        if (!ready)
            throw new TUnit.Core.Exceptions.SkipTestException("Claude TUI did not reach a ready state");

        // Give the TUI time to fully paint before sampling the raw stream.
        await Task.Delay(1500);
        var raw = runner.SnapshotText();

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(4)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        // Alternate-screen enter (DECSET 1049 / DECSET 47). Their absence means we're in the classic renderer.
        raw.ShouldNotContain("[?1049h", customMessage: "Claude entered the alternate screen buffer (fullscreen TUI)");
        raw.ShouldNotContain("[?47h", customMessage: "Claude entered the alternate screen buffer (legacy DECSET 47)");
    }
}
