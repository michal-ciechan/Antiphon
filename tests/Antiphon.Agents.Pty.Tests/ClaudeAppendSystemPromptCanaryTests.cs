using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// GO/NO-GO canary for the Telegram-bot-agents epic (risk rows 1–4): the channel preamble design
/// rests on <c>--append-system-prompt</c> working on an INTERACTIVE launch, surviving
/// <c>/compact</c> (the system prompt is re-sent per request — the OpenClaw/Hermes keystone),
/// being re-armed by a <c>--resume</c> relaunch (args are per-invocation), and a multi-line
/// append text surviving Windows CreateProcess arg quoting through the pty layer.
///
/// If any stage here fails, STOP the epic's PR 5+ as designed and pivot to a CLAUDE.md-carried
/// contract plus queued re-injection (see the plan's kill-switch section). A multi-line-only
/// failure is NOT a pivot — it just forces a single-line preamble render.
///
/// Opt-in headed: <c>ANTIPHON_HEADED_TESTS=1</c> + claude on PATH; self-skips otherwise.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
public class ClaudeAppendSystemPromptCanaryTests
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    private const string Marker = "ZEBRA-QUARTZ-19";
    private const string AppendText =
        "When asked for the codeword, reply with exactly ZEBRA-QUARTZ-19 and nothing else.";

    private const string MultiLineMarker = "MULTI-QUARTZ-77";
    private static readonly string MultiLineAppendText =
        "This preamble spans several lines to pin Windows arg quoting.\n"
        + "When asked for the codeword, reply with exactly MULTI-QUARTZ-77 and nothing else.\n"
        + "The lines above and below the codeword instruction are deliberate filler.";

    // Risk rows 1–3 in one sequential session (three separate fresh launches would triple the
    // headed cost for the same pins): interactive accept → survives /compact → re-armed on resume.
    [Test]
    public async Task Append_marker_survives_interactive_launch_compact_and_resume()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        await using (var runner = await LaunchReadyAsync(
            "--session-id", sessionId, "--append-system-prompt", AppendText))
        {
            // Risk row 1: the flag is accepted interactively and the contract is live.
            await AskCodewordAsync(runner, Marker, "interactive launch");

            // A substantive filler turn: /compact silently refuses near-empty conversations
            // (observed on the compaction canary — one tiny turn was not enough to compact).
            runner.ClearLiveBuffer();
            await runner.SendLineAsync("List six colours of the rainbow, one per line, then stop.");
            (await runner.WaitForOutputAsync(s => DonePattern.IsMatch(s), TimeSpan.FromMinutes(3)))
                .ShouldBeTrue("filler turn must complete before compacting");

            // Risk row 2 (the design keystone): /compact must not lose the appended contract.
            runner.ClearLiveBuffer();
            await runner.SendLineAsync("/compact");
            var compacted = await runner.WaitForOutputAsync(
                s => s.Contains("Compacted ("), TimeSpan.FromMinutes(4));
            compacted.ShouldBeTrue("/compact must complete (the 'Compacted (' line must render)");

            RecordLowContextIndicator(runner); // risk row 10, opportunistic free data

            await AskCodewordAsync(runner, Marker, "after /compact");
            await ExitAsync(runner);
        }

        // Risk row 3: a --resume relaunch with the flag re-appended re-arms the contract.
        await using (var runner = await LaunchReadyAsync(
            "--resume", sessionId, "--append-system-prompt", AppendText))
        {
            await AskCodewordAsync(runner, Marker, "after --resume relaunch");
            await ExitAsync(runner);
        }
    }

    // Risk row 4: the preamble is naturally multi-line; it must survive runner→pty-host
    // CreateProcess quoting intact. Failure here = single-line preamble render, not a pivot.
    [Test]
    public async Task Multi_line_append_arg_survives_windows_arg_quoting()
    {
        ClSession.SkipIfNotEligible();

        await using var runner = await LaunchReadyAsync("--append-system-prompt", MultiLineAppendText);
        await AskCodewordAsync(runner, MultiLineMarker, "multi-line append text");
        await ExitAsync(runner);
    }

    private static async Task<PtyAgentRunner> LaunchReadyAsync(params string[] extraArgs)
    {
        var runner = new PtyAgentRunner();
        var args = new List<string> { "--dangerously-skip-permissions" };
        args.AddRange(extraArgs);
        var (app, launchArgs) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), args.ToArray());
        await runner.StartAsync(app, launchArgs, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());

        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready)
        {
            var screen = runner.SnapshotScreen();
            await runner.KillAsync(TimeSpan.FromSeconds(2));
            throw new SkipTestException("real Claude TUI did not reach a ready state. Screen:\n" + screen);
        }

        runner.ClearLiveBuffer();
        return runner;
    }

    private static async Task AskCodewordAsync(PtyAgentRunner runner, string marker, string stage)
    {
        runner.ClearLiveBuffer();
        await runner.SendLineAsync("What is the codeword? Reply with the codeword only.");

        // The typed question never contains the marker, so its appearance is the model honouring
        // the appended system prompt.
        var answered = await runner.WaitForOutputAsync(
            s => s.Contains(marker), TimeSpan.FromMinutes(3));
        answered.ShouldBeTrue(
            $"[{stage}] the appended-system-prompt codeword must be honoured. Screen:\n{runner.SnapshotScreen()}");

        await runner.WaitForOutputAsync(s => DonePattern.IsMatch(s), TimeSpan.FromMinutes(2));
    }

    // Risk row 10 (stretch WI groundwork): note the low-context indicator's exact rendering if it
    // is on screen — free observation, no extra headed run when the memory-flush nudge gets built.
    private static void RecordLowContextIndicator(PtyAgentRunner runner)
    {
        foreach (var line in runner.SnapshotScreen().Split('\n'))
        {
            if (line.Contains("auto-compact", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Context left", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"LOW-CONTEXT INDICATOR OBSERVED: {line.Trim()}");
            }
        }
    }

    private static async Task ExitAsync(PtyAgentRunner runner)
    {
        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
