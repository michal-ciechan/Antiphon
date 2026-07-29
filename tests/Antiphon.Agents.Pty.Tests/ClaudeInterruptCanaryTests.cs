using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Interrupt canary (live miss 2026-07-29: a session sat "working" forever after Mike rejected a
/// tool call, stranding every WhenIdle delivery). Pins, against REAL Claude, the two surfaces the
/// interrupt-idle fix depends on — with both the terminal output and the session JSONL ("the logs")
/// as evidence that the session has genuinely stopped working:
///
///  1. TERMINAL: mid-turn the TUI shows the "esc to interrupt" affordance; after an Esc the
///     spinner/affordance is gone and the composer is back — the screen-level "stopped working".
///  2. LOGS (session JSONL): the aborted turn is recorded as a USER message starting with
///     "[Request interrupted" and NO turn-completion follows it. That marker is what
///     <see cref="TranscriptKinds.IsInterruptPrompt"/> keys on — the server's IsWorkingAsync and
///     the client's isWorking() both treat it as the turn's end (their handling is pinned by
///     SessionMessageQueueServiceTests and SessionTranscriptPanel.test.tsx; THIS test pins that
///     real Claude still emits the marker those checks depend on).
///
/// Opt-in headed: <c>ANTIPHON_HEADED_TESTS=1</c> + claude on PATH; self-skips otherwise.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
public class ClaudeInterruptCanaryTests
{
    private const string EscToInterrupt = "esc to interrupt";

    [Test]
    public async Task Esc_mid_turn_stops_work_on_screen_and_writes_the_interrupt_marker_to_the_jsonl()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        await using var runner = new PtyAgentRunner();
        // NO --dangerously-skip-permissions: the marker-writing interrupt shape is a REJECTED tool
        // permission dialog (the live miss was Mike rejecting a tool call mid-turn). A plain Esc
        // during a streaming response — or even during an auto-approved tool — UNWINDS the turn
        // instead: prompt restored to the composer, nothing persisted, NO marker (observed across
        // three probe runs against the real CLI while building this canary).
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        // Ask for a tool call: Claude proposes it and blocks on the permission dialog — persisted
        // tool use, mid-turn. That is the state the live miss interrupted. NOTE: the command must
        // NOT be on the safe-command allowlist (a bare `echo` auto-approves and completes without
        // any dialog — observed live); a powershell invocation reliably prompts.
        await runner.SendLineAsync(
            "Use the Bash tool to run exactly this command: powershell -Command \"Write-Host interrupt-canary-probe\"");

        // TERMINAL evidence the session is WORKING: the tool permission dialog is on screen.
        var dialogDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        var dialogShown = false;
        while (DateTime.UtcNow < dialogDeadline)
        {
            var screen = runner.SnapshotScreen();
            if (screen.Contains("interrupt-canary-probe")
                && screen.Contains("Do you want", StringComparison.OrdinalIgnoreCase))
            {
                dialogShown = true;
                break;
            }
            await Task.Delay(250);
        }
        dialogShown.ShouldBeTrue(
            "the Bash permission dialog must be on screen (the mid-turn state). Screen:\n" + runner.SnapshotScreen());
        Console.WriteLine("WORKING SCREEN (excerpt):\n" + Tail(runner.SnapshotScreen(), 600));

        // Reject it — the live-miss interrupt.
        await runner.WriteAsync("\x1b");

        // TERMINAL evidence the session STOPPED: the dialog (and any working affordance) leaves the
        // rendered screen — the rendered screen is current state, unlike the append-only raw buffer.
        var stopped = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var screen = runner.SnapshotScreen();
            if (!screen.Contains("Do you want", StringComparison.OrdinalIgnoreCase)
                && !screen.Contains(EscToInterrupt, StringComparison.OrdinalIgnoreCase))
            {
                stopped = true;
                break;
            }
            await Task.Delay(500);
        }
        stopped.ShouldBeTrue(
            "after the rejection the permission dialog must leave the screen — the terminal-level proof "
            + "the session stopped working. Screen:\n" + runner.SnapshotScreen());
        Console.WriteLine("STOPPED SCREEN (excerpt):\n" + Tail(runner.SnapshotScreen(), 600));

        // Clear any composer text the interrupt restored (Esc on an idle composer clears it) so the
        // follow-up prompt doesn't glue onto the interrupted one.
        await Task.Delay(500);
        await runner.WriteAsync("\x1b");
        await Task.Delay(500);

        // The interrupted session must still accept input (composer alive): a quick follow-up turn.
        // Wait on the " for Ns" done pattern, NOT the reply text — the typed prompt's own composer
        // echo would satisfy a text match instantly. A completed turn also guarantees Claude has
        // created/flushed the session JSONL (its file creation is lazy).
        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Reply with the single word PONG and nothing else.");
        var followUp = await runner.WaitForOutputAsync(
            s => System.Text.RegularExpressions.Regex.IsMatch(s, @" for \d+s"), TimeSpan.FromMinutes(2));
        followUp.ShouldBeTrue("an interrupted session must remain usable for the next turn");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(10)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        // LOG evidence: the session JSONL records the aborted turn as a USER message carrying the
        // interrupt marker — the exact record the transcript pipeline normalizes to the UserPrompt
        // entry that IsWorkingAsync / isWorking() treat as the turn's end.
        string? jsonlPath = null;
        for (var i = 0; i < 15 && jsonlPath is null; i++)
        {
            jsonlPath = FindSessionJsonl(sessionId);
            if (jsonlPath is null) await Task.Delay(1000);
        }
        jsonlPath.ShouldNotBeNull($"session JSONL for {sessionId} must exist under ~/.claude/projects");
        Console.WriteLine($"Session JSONL: {jsonlPath}");

        var markerText = FindInterruptMarkerText(jsonlPath!);
        markerText.ShouldNotBeNull(
            "the JSONL must contain a user record starting with \"[Request interrupted\"");
        Console.WriteLine($"OBSERVED INTERRUPT MARKER: {markerText}");

        // Tie the observation to the code under test: the shared contract helper — the single
        // predicate the server AND client working checks build on — must recognize the real marker.
        TranscriptKinds.IsInterruptPrompt(TranscriptKinds.UserPrompt, markerText)
            .ShouldBeTrue("TranscriptKinds.IsInterruptPrompt must match what real Claude writes");
    }

    private static string? FindSessionJsonl(string sessionId)
    {
        var projects = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (!Directory.Exists(projects))
            return null;
        return Directory
            .EnumerateFiles(projects, $"{sessionId}.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    // A user record's content is either a plain string or a content-block array with text items;
    // return the first user text that starts with the interrupt marker.
    private static string? FindInterruptMarkerText(string jsonlPath)
    {
        foreach (var line in File.ReadLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line) || !line.Contains(TranscriptKinds.InterruptedPromptPrefix))
                continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var type) && type.GetString() != "user")
                    continue;
                if (!root.TryGetProperty("message", out var message)
                    || !message.TryGetProperty("content", out var content))
                    continue;

                if (content.ValueKind == JsonValueKind.String
                    && content.GetString() is { } s
                    && s.TrimStart().StartsWith(TranscriptKinds.InterruptedPromptPrefix, StringComparison.Ordinal))
                    return s;

                if (content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("text", out var text)
                            && text.GetString() is { } t
                            && t.TrimStart().StartsWith(TranscriptKinds.InterruptedPromptPrefix, StringComparison.Ordinal))
                            return t;
                    }
                }
            }
        }
        return null;
    }

    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];
}
