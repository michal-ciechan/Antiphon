using System.Diagnostics;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

// Headed tests spawn real Claude TUI sessions — run them serially to avoid
// parallel API quota contention and quiet-period detector interference.
[NotInParallel("Headed")]
[Category("Headed")]
public class ClaudeHeadedTests
{
    // ---------- S11: cl --version smoke ----------

    [Test]
    public async Task Cl_version_via_pty_exits_zero_with_version_string()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--version");
        await runner.StartAsync(app, args);

        var exit = await runner.Exited.WaitAsync(TimeSpan.FromSeconds(60));
        exit.ShouldBe(0);

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        clean.ShouldMatch(@"\d+\.\d+\.\d+", "version string should appear");
    }

    // ---------- S12: cl --print non-interactive prompt ----------

    [Test]
    public async Task Cl_print_mode_returns_response()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "-p", "Reply with the single word PONG and nothing else.");
        await runner.StartAsync(app, args);

        var exit = await runner.Exited.WaitAsync(TimeSpan.FromMinutes(2));
        exit.ShouldBe(0);

        var clean = (AnsiStripper.Clean(runner.SnapshotText()) ?? "").ToUpperInvariant();
        clean.ShouldContain("PONG");
    }

    // ---------- S13: TUI ready detection ----------

    [Test]
    public async Task Cl_tui_reaches_ready_with_default_detector()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        var detector = new ClaudeReadyDetector();
        var ready = await detector.WaitAsync(runner);
        ready.ShouldBeTrue();

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S14: single prompt round-trip ----------

    [Test]
    public async Task Cl_headed_single_prompt_returns_response()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Say HELLO-MARKER and stop.");
        var done = await new ClaudeDoneDetector().WaitAsync(runner);
        done.ShouldBeTrue();

        var clean = (AnsiStripper.Clean(runner.SnapshotText()) ?? "").ToUpperInvariant();
        clean.ShouldContain("HELLO-MARKER");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S15: sequential two-prompt session shares context ----------

    [Test]
    public async Task Cl_headed_sequential_prompts_share_context()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Remember the number 7919. Reply with just OK.");
        await new ClaudeDoneDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("What number did I ask you to remember? Reply with only the number.");
        await new ClaudeDoneDetector().WaitAsync(runner);

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        clean.ShouldContain("7919");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S16: tool use ----------

    [Test]
    public async Task Cl_headed_tool_use_returns_systeminfo()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(),
            "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Run `systeminfo` via Bash and tell me the OS Name in one short sentence.");
        var done = await new ClaudeDoneDetector { MaxWait = TimeSpan.FromMinutes(3) }.WaitAsync(runner);
        done.ShouldBeTrue();

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        clean.ShouldContain("Windows");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S17a: /exit clean shutdown ----------

    [Test]
    public async Task Cl_headed_exit_command_completes_in_5s()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        var sw = Stopwatch.StartNew();
        await runner.SendLineAsync("/exit");
        var exited = await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5))) == runner.Exited;
        sw.Stop();

        exited.ShouldBeTrue($"/exit should complete within 5s (took {sw.Elapsed})");
    }

    // ---------- S17b: kill mid-flight, no orphans ----------

    [Test]
    public async Task Cl_headed_kill_midflight_no_orphans()
    {
        ClSession.SkipIfNotEligible();

        var orphansBefore = Process.GetProcessesByName("claude").Length;

        await using (var runner = new PtyAgentRunner())
        {
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
            await runner.StartAsync(app, args, cols: 200, rows: 60);

            await new ClaudeReadyDetector().WaitAsync(runner);

            runner.ClearLiveBuffer();
            await runner.SendLineAsync("Count from 1 to 200 slowly, one number per line.");
            await Task.Delay(TimeSpan.FromSeconds(2)); // let claude start streaming

            var killed = await runner.KillAsync(TimeSpan.FromSeconds(5));
            killed.ShouldBeTrue();
        }

        await Task.Delay(2000);
        var orphansAfter = Process.GetProcessesByName("claude").Length;
        (orphansAfter - orphansBefore).ShouldBeLessThan(2,
            $"claude.exe should not orphan after kill. before={orphansBefore} after={orphansAfter}");
    }
}

[NotInParallel("Headed")]
[Category("HeadedLong")]
public class ClaudeHeadedLongTests
{
    // ---------- S18: --resume preserves context across runner instances ----------

    [Test]
    public async Task Cl_headed_resume_preserves_context_across_runner_instances()
    {
        ClSession.SkipIfNotEligible();

        // Run 1: seed context, capture session id from exit message.
        string? sessionId = null;
        await using (var runner = new PtyAgentRunner())
        {
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
            await runner.StartAsync(app, args, cols: 200, rows: 60);

            await new ClaudeReadyDetector().WaitAsync(runner);

            runner.ClearLiveBuffer();
            await runner.SendLineAsync("Remember the codeword BANANA-ZULU-9. Reply OK.");
            await new ClaudeDoneDetector().WaitAsync(runner);

            await runner.SendLineAsync("/exit");
            await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(8)));

            var transcript = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
            // claude prints `claude --resume <id>` near the end on /exit.
            var match = System.Text.RegularExpressions.Regex.Match(
                transcript, @"--resume\s+([0-9a-f-]{8,})");
            match.Success.ShouldBeTrue($"could not find resume id. tail:\n{transcript[^Math.Min(500, transcript.Length)..]}");
            sessionId = match.Groups[1].Value;
        }

        sessionId.ShouldNotBeNullOrEmpty();

        // Run 2: --resume and ask for the codeword.
        await using (var runner2 = new PtyAgentRunner())
        {
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--resume", sessionId!);
            await runner2.StartAsync(app, args, cols: 200, rows: 60);

            // --resume sessions load prior context on top of normal startup; allow extra time.
            await new ClaudeReadyDetector { MaxWait = TimeSpan.FromMinutes(1), MinTotalWait = TimeSpan.FromSeconds(15) }.WaitAsync(runner2);

            runner2.ClearLiveBuffer();
            await runner2.SendLineAsync("Look at the conversation history above and tell me: what codeword was mentioned in the first message? Reply with just the codeword.");
            await new ClaudeDoneDetector().WaitAsync(runner2);

            var clean = (AnsiStripper.Clean(runner2.SnapshotText()) ?? "").ToUpperInvariant();
            clean.ShouldContain("BANANA-ZULU-9");

            await runner2.SendLineAsync("/exit");
            await Task.WhenAny(runner2.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
            await runner2.KillAsync(TimeSpan.FromSeconds(2));
        }
    }
}
