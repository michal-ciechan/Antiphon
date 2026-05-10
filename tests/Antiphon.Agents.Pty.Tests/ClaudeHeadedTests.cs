using System.Diagnostics;
using Antiphon.Agents.Pty;
using FluentAssertions;
using Xunit;

namespace Antiphon.Agents.Pty.Tests;

[Trait("Category", "Headed")]
public class ClaudeHeadedTests
{
    // ---------- S11: cl --version smoke ----------

    [SkippableFact]
    public async Task Cl_version_via_pty_exits_zero_with_version_string()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--version");
        await runner.StartAsync(app, args);

        var exit = await runner.Exited.WaitAsync(TimeSpan.FromSeconds(60));
        exit.Should().Be(0);

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        clean.Should().MatchRegex(@"\d+\.\d+\.\d+", "version string should appear");
    }

    // ---------- S12: cl --print non-interactive prompt ----------

    [SkippableFact]
    public async Task Cl_print_mode_returns_response()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "-p", "Reply with the single word PONG and nothing else.");
        await runner.StartAsync(app, args);

        var exit = await runner.Exited.WaitAsync(TimeSpan.FromMinutes(2));
        exit.Should().Be(0);

        var clean = (AnsiStripper.Clean(runner.SnapshotText()) ?? "").ToUpperInvariant();
        clean.Should().Contain("PONG");
    }

    // ---------- S13: TUI ready detection ----------

    [SkippableFact]
    public async Task Cl_tui_reaches_ready_within_30s()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        var detector = new ClaudeReadyDetector();
        var ready = await detector.WaitAsync(runner);
        ready.Should().BeTrue();

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S14: single prompt round-trip ----------

    [SkippableFact]
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
        done.Should().BeTrue();

        var clean = (AnsiStripper.Clean(runner.SnapshotText()) ?? "").ToUpperInvariant();
        clean.Should().Contain("HELLO-MARKER");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S15: sequential two-prompt session shares context ----------

    [SkippableFact]
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
        clean.Should().Contain("7919");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S16: tool use ----------

    [SkippableFact]
    public async Task Cl_headed_tool_use_returns_systeminfo()
    {
        ClSession.SkipIfNotEligible();
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        await new ClaudeReadyDetector().WaitAsync(runner);

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Run `systeminfo` via Bash and tell me the OS Name in one short sentence.");
        var done = await new ClaudeDoneDetector { MaxWait = TimeSpan.FromMinutes(3) }.WaitAsync(runner);
        done.Should().BeTrue();

        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        clean.Should().Contain("Windows");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S17a: /exit clean shutdown ----------

    [SkippableFact]
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

        exited.Should().BeTrue($"/exit should complete within 5s (took {sw.Elapsed})");
    }

    // ---------- S17b: kill mid-flight, no orphans ----------

    [SkippableFact]
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
            killed.Should().BeTrue();
        }

        await Task.Delay(2000);
        var orphansAfter = Process.GetProcessesByName("claude").Length;
        (orphansAfter - orphansBefore).Should().BeLessThan(2,
            $"claude.exe should not orphan after kill. before={orphansBefore} after={orphansAfter}");
    }
}

[Trait("Category", "HeadedLong")]
public class ClaudeHeadedLongTests
{
    // ---------- S18: --resume preserves context across runner instances ----------

    [SkippableFact]
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
            match.Success.Should().BeTrue($"could not find resume id. tail:\n{transcript[^Math.Min(500, transcript.Length)..]}");
            sessionId = match.Groups[1].Value;
        }

        sessionId.Should().NotBeNullOrEmpty();

        // Run 2: --resume and ask for the codeword.
        await using (var runner2 = new PtyAgentRunner())
        {
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--resume", sessionId!);
            await runner2.StartAsync(app, args, cols: 200, rows: 60);

            await new ClaudeReadyDetector { MaxWait = TimeSpan.FromMinutes(1) }.WaitAsync(runner2);

            runner2.ClearLiveBuffer();
            await runner2.SendLineAsync("What codeword did I ask you to remember? Reply with just the codeword.");
            await new ClaudeDoneDetector().WaitAsync(runner2);

            var clean = (AnsiStripper.Clean(runner2.SnapshotText()) ?? "").ToUpperInvariant();
            clean.Should().Contain("BANANA-ZULU-9");

            await runner2.SendLineAsync("/exit");
            await Task.WhenAny(runner2.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
            await runner2.KillAsync(TimeSpan.FromSeconds(2));
        }
    }
}
