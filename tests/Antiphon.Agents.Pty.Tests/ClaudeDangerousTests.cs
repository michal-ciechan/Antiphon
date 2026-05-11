using System.Diagnostics;
using Antiphon.Agents.Pty;
using FluentAssertions;
using Xunit;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Headed integration tests that launch Claude with --dangerously-skip-permissions.
/// Eligible when ANTIPHON_HEADED_TESTS=1 and either cl.ps1 or the global `claude`
/// binary is on PATH. cl.ps1 is preferred; claude is the fallback.
/// </summary>
[Collection("Headed")]
[Trait("Category", "Headed")]
public class ClaudeDangerousTests
{
    // ---------- S19: --dangerously-skip-permissions + --version smoke ----------

    [SkippableFact]
    public async Task Claude_dangerous_version_exits_zero_with_version_string()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions", "--version");
        await runner.StartAsync(app, args);

        var exit = await runner.Exited.WaitAsync(TimeSpan.FromSeconds(30));
        exit.Should().Be(0);

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        clean.Should().MatchRegex(@"\d+\.\d+\.\d+", "version string should appear");
    }

    // ---------- S20: --dangerously-skip-permissions + -p print mode ----------

    [SkippableFact]
    public async Task Claude_dangerous_print_returns_pong()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions", "-p",
            "Reply with the single word PONG and nothing else.");
        await runner.StartAsync(app, args);

        var exit = await runner.Exited.WaitAsync(TimeSpan.FromMinutes(2));
        exit.Should().Be(0);

        var clean = (AnsiStripper.Clean(runner.SnapshotText()) ?? "").ToUpperInvariant();
        clean.Should().Contain("PONG");
    }

    // ---------- S21: TUI ready with --dangerously-skip-permissions ----------

    [SkippableFact]
    public async Task Claude_dangerous_tui_reaches_ready_within_30s()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        ready.Should().BeTrue();

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S22: single prompt round-trip ----------

    [SkippableFact]
    public async Task Claude_dangerous_single_prompt_returns_response()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Say HELLO-MARKER and stop.");
        var done = await new ClaudeDoneDetector().WaitAsync(runner);
        done.Should().BeTrue();

        var clean = (AnsiStripper.Clean(runner.SnapshotText()) ?? "").ToUpperInvariant();
        clean.Should().Contain("HELLO-MARKER");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S23: tool use executes without permission prompt ----------

    [SkippableFact]
    public async Task Claude_dangerous_tool_use_executes_without_permission_prompt()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        await runner.SendLineAsync(
            "Run the PowerShell command `echo TOOL-EXECUTED` via Bash and report what it printed.");
        var done = await new ClaudeDoneDetector { MaxWait = TimeSpan.FromMinutes(3) }.WaitAsync(runner);
        done.Should().BeTrue();

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        // --dangerously-skip-permissions should prevent any confirmation prompts
        string[] permissionKeywords = ["Allow", "Deny", "[y/N]"];
        clean.Should().NotContainAny(permissionKeywords,
            "--dangerously-skip-permissions should bypass all prompts");
        clean.Should().Contain("TOOL-EXECUTED");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S24: sequential prompts share context ----------

    [SkippableFact]
    public async Task Claude_dangerous_sequential_prompts_share_context()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        // Turn 1: seed the number; wait for Claude to acknowledge before sending turn 2.
        await runner.SendLineAsync("Remember the number 4357. Reply with just OK.");
        var firstAck = await runner.WaitForOutputAsync(
            text => (AnsiStripper.Clean(text) ?? "").Contains("OK"),
            TimeSpan.FromMinutes(2));
        firstAck.Should().BeTrue("Claude should acknowledge the first prompt");

        // Brief settle so the TUI reaches the idle prompt before we type again.
        await Task.Delay(500);

        // Turn 2: ask for recall; watch the cumulative buffer for the expected number.
        await runner.SendLineAsync("What number did I ask you to remember? Reply with only the number.");
        var found = await runner.WaitForOutputAsync(
            text => (AnsiStripper.Clean(text) ?? "").Contains("4357"),
            TimeSpan.FromMinutes(2));
        found.Should().BeTrue("Claude should recall the seeded number in the same session");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S26: slow bash task — ClaudeCrunchedDetector waits through sleep ----------

    [SkippableFact]
    public async Task Claude_dangerous_cruncheddetector_waits_through_slow_bash_task()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        await runner.SendLineAsync(
            "Run this bash command: sleep 10. " +
            "When it finishes, say SLOW-TASK-DONE and nothing else.");

        var sw = Stopwatch.StartNew();
        var crunched = await new ClaudeCrunchedDetector { MaxWait = TimeSpan.FromMinutes(3) }
            .WaitAsync(runner);
        sw.Stop();

        crunched.Should().BeTrue("Crunched signal must appear after the task finishes");
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(9),
            "detector must not fire before the 10-second sleep completes " +
            $"(actual: {sw.Elapsed.TotalSeconds:F1}s)");

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        clean.Should().Contain("SLOW-TASK-DONE");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S27: detect when Claude asks the user a clarifying question ----------

    [SkippableFact]
    public async Task Claude_dangerous_detects_claude_asking_a_question()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        // Force Claude to ask exactly one question and stop — making it deterministic.
        await runner.SendLineAsync(
            "Ask me exactly one question: do I prefer tabs or spaces for indentation? " +
            "Output only the question itself. Do not answer it or add any other text.");

        var crunched = await new ClaudeCrunchedDetector().WaitAsync(runner);
        crunched.Should().BeTrue("Claude must complete its question turn");

        ClaudeResponseAnalyzer.IsAskingQuestion(runner.SnapshotText())
            .Should().BeTrue("Claude's response should contain a question mark");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S28: answer Claude's question, verify it processes the reply ----------

    [SkippableFact]
    public async Task Claude_dangerous_can_answer_claude_question_and_receive_acknowledgement()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        // Turn 1 — make Claude ask a question and stop.
        runner.ClearLiveBuffer();
        await runner.SendLineAsync(
            "Ask me what my preferred indentation style is (tabs or spaces). " +
            "Output only the question. Wait for my answer before saying anything else.");

        var firstCrunched = await new ClaudeCrunchedDetector().WaitAsync(runner);
        firstCrunched.Should().BeTrue("Claude should complete its question turn");
        ClaudeResponseAnalyzer.IsAskingQuestion(runner.SnapshotText())
            .Should().BeTrue("first response must be a question");

        // Brief settle so the TUI is at the idle prompt before we type.
        await Task.Delay(500);

        // Turn 2 — answer the question; clear first so the detector scans only this turn.
        runner.ClearLiveBuffer();
        await runner.SendLineAsync("I prefer tabs.");

        var secondCrunched = await new ClaudeCrunchedDetector { MaxWait = TimeSpan.FromMinutes(2) }
            .WaitAsync(runner);
        secondCrunched.Should().BeTrue("Claude should process the answer and respond");

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        clean.ToLowerInvariant().Should().Contain("tab",
            "Claude should acknowledge the tabs preference in its follow-up");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S25: kill mid-flight, no orphans ----------

    [SkippableFact]
    public async Task Claude_dangerous_kill_midflight_no_orphans()
    {
        ClSession.SkipIfNotEligible();

        var orphansBefore = Process.GetProcessesByName("claude").Length;

        await using (var runner = new PtyAgentRunner())
        {
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
                "--dangerously-skip-permissions");
            await runner.StartAsync(app, args, cols: 200, rows: 60);

            await new ClaudeReadyDetector().WaitAsync(runner);

            runner.ClearLiveBuffer();
            await runner.SendLineAsync("Count from 1 to 200 slowly, one number per line.");
            await Task.Delay(TimeSpan.FromSeconds(2));

            var killed = await runner.KillAsync(TimeSpan.FromSeconds(5));
            killed.Should().BeTrue();
        }

        await Task.Delay(2000);
        var orphansAfter = Process.GetProcessesByName("claude").Length;
        (orphansAfter - orphansBefore).Should().BeLessThan(2,
            $"claude.exe should not orphan after kill. before={orphansBefore} after={orphansAfter}");
    }
}
