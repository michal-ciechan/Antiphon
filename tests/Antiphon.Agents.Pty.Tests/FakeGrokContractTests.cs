using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Locks the Grok TUI submit/CLI contract against FakeGrok — the same ConPTY path as the real
/// <c>grok</c> CLI, without auth or a live model. Mirrors <see cref="FakeClaudeContractTests"/>
/// for the behaviours Antiphon's queue and profile probes actually depend on.
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class FakeGrokContractTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeGrokExe =>
        Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");

    private static void SkipIfUnavailable()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeGrokExe))
            throw new SkipTestException($"fakegrok.exe not staged at {FakeGrokExe} — build the solution first");
    }

    private static async Task<PtyAgentRunner> LaunchReadyFakeAsync(
        IDictionary<string, string>? env = null,
        string[]? args = null)
    {
        var launch = Stopwatch.StartNew();
        var runner = new PtyAgentRunner("inbox");
        var fakeEnv = env is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(env);
        // CARD-0128 S1: leave the fake's own input-burst trace in the captured test log.
        fakeEnv["ANTIPHON_FAKE_DEBUG_INPUT"] = "1";
        await runner.StartAsync(FakeGrokExe, args ?? [], cols: 120, rows: 30, env: fakeEnv);
        runner.Backend!.Backend.ShouldBe(PtyBackend.InboxConhost);
        // CARD-0050 S3: runaway bound — success returns on the banner. Same spawn-latency
        // class as fakeclaude's 15s miss under the concurrent double-suite load.
        var ready = await runner.WaitForOutputAsync(
            s => s.Contains("Fake Grok ready"),
            TimeSpan.FromSeconds(45));
        launch.Stop();
        ready.ShouldBeTrue(
            $"fake Grok should print its readiness banner; spawn-to-banner={launch.Elapsed.TotalMilliseconds:F0}ms; "
            + "screen: " + runner.SnapshotScreen() + "; raw: " + runner.SnapshotText());
        runner.ClearLiveBuffer();
        return runner;
    }

    private static async Task AssertOutputAsync(
        PtyAgentRunner runner,
        string assertion,
        string because,
        Func<string, bool> predicate)
    {
        var wait = Stopwatch.StartNew();
        var matched = await runner.WaitForOutputAsync(predicate, TimeSpan.FromSeconds(5));
        wait.Stop();
        matched.ShouldBeTrue(
            $"{assertion} ({because}); elapsed={wait.Elapsed.TotalMilliseconds:F0}ms; "
            + $"screen dump:{Environment.NewLine}{runner.SnapshotScreen()}{Environment.NewLine}"
            + $"raw output:{Environment.NewLine}{runner.SnapshotText()}");
    }

    private static void AssertScreenDoesNotContain(PtyAgentRunner runner, string value, string assertion)
    {
        runner.SnapshotText().ShouldNotContain(
            value,
            customMessage: $"{assertion}; screen dump:{Environment.NewLine}{runner.SnapshotScreen()}{Environment.NewLine}"
            + $"raw output:{Environment.NewLine}{runner.SnapshotText()}");
    }

    [Test]
    public async Task Version_flag_prints_the_measured_grok_line_and_exits()
    {
        SkipIfUnavailable();
        var psi = new ProcessStartInfo(FakeGrokExe, "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0);
        output.Trim().ShouldBe("grok 1.0.5 (fakegrok) [stable]");
    }

    [Test]
    public async Task Models_command_prints_the_measured_prose_catalogue()
    {
        SkipIfUnavailable();
        var psi = new ProcessStartInfo(FakeGrokExe, "models")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0);
        output.ShouldContain("Default model: grok-4.6");
        output.ShouldContain("* grok-4.6 (default)");
        output.ShouldContain("- grok-4.5");
    }

    [Test]
    public async Task Text_and_CR_in_one_write_submits()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("queued message\r");

        // Measured against real grok 1.0.5 (GrokCanaryTests): every \r is Enter — there is no
        // Claude-style paste window on unbracketed input. This is the opposite of FakeClaude's
        // contract, which this fake wrongly copied before the canaries.
        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:queued message"), TimeSpan.FromSeconds(5));
        submitted.ShouldBeTrue("text+CR in one write submits on real Grok");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Body_then_separate_CR_submits()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("queued message");
        await Task.Delay(25);
        await runner.WriteAsync("\r");

        await AssertOutputAsync(
            runner,
            assertion: "submit marker",
            because: "body followed by a separate CR must submit",
            predicate: s => s.Contains("SUBMITTED:queued message"));

        // The measured turn-end signals (grok 1.0.5): "Worked for 1.7s" — DECIMAL seconds, which
        // the " for \d+s" integer regex does not match — and an idle OSC title of plain "grok",
        // never Claude's ✳.
        await AssertOutputAsync(
            runner,
            assertion: "turn-end marker",
            because: "turn end prints the measured decimal-seconds line",
            predicate: s => s.Contains("Worked for 1.7s"));
        await AssertOutputAsync(
            runner,
            assertion: "idle OSC title",
            because: "the idle OSC title is plain 'grok'",
            predicate: s => s.Contains("\x1b]0;grok\x07"));
        AssertScreenDoesNotContain(runner, "✳", "idle screen must not contain Claude's busy glyph");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Unbracketed_body_with_CR_line_endings_fragments_into_partial_turns()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("line one\rline two");
        await Task.Delay(25);
        await runner.WriteAsync("\r");

        var first = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:line one"), TimeSpan.FromSeconds(5));
        first.ShouldBeTrue("a mid-body CR must submit the fragment before it");
        var second = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:line two"), TimeSpan.FromSeconds(5));
        second.ShouldBeTrue("the tail after a mid-body CR must submit on the following Enter");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Unbracketed_body_with_LF_line_endings_submits_as_one_turn_with_newlines_dropped()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var body = "HEAD first line of a big paste\n"
            + string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line {i} " + new string('x', 60)))
            + "\nTAIL last line";
        await runner.WriteAsync(body);
        await Task.Delay(25);
        await runner.WriteAsync("\r");

        var intact = await runner.WaitForOutputAsync(
            s =>
            {
                var flat = s.Replace("\r", "").Replace("\n", "");
                return System.Text.RegularExpressions.Regex.IsMatch(
                    flat, @"SUBMITTED:(?:(?!FAKE response).)*HEAD first line(?:(?!FAKE response).)*TAIL last line");
            },
            TimeSpan.FromSeconds(5));
        intact.ShouldBeTrue("LF endings must stay in the composer and submit as one turn");
        // Measured 1.0.5: LFs are DROPPED — lines join with NO separator ("…big pasteline 1 …").
        // The SUBMITTED echo escapes surviving newlines as \n, so their absence pins the drop.
        runner.SnapshotText().ShouldContain("big pasteline 1");
        runner.SnapshotText().ShouldNotContain("SUBMITTED:HEAD first line of a big paste\\n");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Enter_on_an_empty_composer_submits_nothing()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("\r");
        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:"), TimeSpan.FromSeconds(2));
        submitted.ShouldBeFalse("bare Enter on an empty composer is a no-op");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Session_id_writes_grok_session_files_under_GROK_HOME()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string> { ["GROK_HOME"] = home },
                args: ["--cwd", cwd, "--session-id", sessionId, "--model", "grok-4.5"]);

            await runner.WriteAsync("remember this");
            await Task.Delay(25);
            await runner.WriteAsync("\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("SUBMITTED:remember this"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            File.Exists(Path.Combine(sessionDir, "summary.json")).ShouldBeTrue();
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            await WaitForUpdatesAsync(
                updatesPath,
                "user_message_chunk",
                "remember this",
                "agent_message_chunk",
                "turn_completed",
                "\"stop_reason\":\"end_turn\"",
                "_x.ai/session/update");

            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Report_line_knob_appends_the_task_token_to_the_agent_message_chunk()
    {
        SkipIfUnavailable();
        var taskId = "a1b2c3d4";
        var prompt = $"[antiphon-task:{taskId}] do the work [antiphon-task:{taskId}]";
        var token = $"[antiphon-report:{taskId} done]";

        var onLast = await AgentChunkLastLineAsync(
            prompt,
            env: new Dictionary<string, string> { ["ANTIPHON_FAKE_REPORT_LINE"] = "1" });
        onLast.ShouldBe(token);

        var offLast = await AgentChunkLastLineAsync(prompt, env: null);
        offLast.ShouldNotBe(token);
        offLast.ShouldStartWith("FAKE response to:");
    }

    [Test]
    public async Task Submit_while_working_emits_cancelled_then_the_new_user_chunk()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string>
                {
                    ["GROK_HOME"] = home,
                    ["ANTIPHON_FAKE_SUBMIT_WHILE_WORKING"] = "cancel",
                },
                args: ["--cwd", cwd, "--session-id", sessionId]);

            await runner.WriteAsync("first turn\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("SUBMITTED:first turn"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            await runner.WriteAsync("Proceed as planned\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("SUBMITTED:Proceed as planned"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            var text = await WaitForUpdatesAsync(
                updatesPath,
                "\"stop_reason\":\"cancelled\"",
                "Proceed as planned");

            var cancelledAt = text.IndexOf("\"stop_reason\":\"cancelled\"", StringComparison.Ordinal);
            var secondUserAt = text.IndexOf("Proceed as planned", StringComparison.Ordinal);
            cancelledAt.ShouldBeGreaterThanOrEqualTo(0);
            secondUserAt.ShouldBeGreaterThan(cancelledAt,
                "measured order: turn_completed cancelled, then the new user_message_chunk 43ms later");
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Compact_command_writes_the_measured_pair_and_no_turn_completed()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string> { ["GROK_HOME"] = home },
                args: ["--cwd", cwd, "--session-id", sessionId]);

            await runner.WriteAsync("/compact\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("SUBMITTED:/compact"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            var text = await WaitForUpdatesAsync(
                updatesPath,
                "compaction_checkpoint",
                "auto_compact_completed",
                "\"tokens_before\":106112",
                "\"tokens_after\":34833");
            text.ShouldNotContain("turn_completed");
            text.ShouldNotContain("user_message_chunk");

            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// CARD-0281 S0: the armed turn writes the measured Grok API-error pair (retry_state failed
    /// + turn_completed stop_reason=error with agent_result) and no agent_message_chunk. The
    /// next turn answers normally.
    /// </summary>
    [Test]
    public async Task An_armed_payment_required_turn_writes_the_measured_error_pair()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string>
                {
                    ["GROK_HOME"] = home,
                    ["ANTIPHON_FAKE_API_ERROR"] = "payment_required",
                },
                args: ["--cwd", cwd, "--session-id", sessionId]);

            await runner.WriteAsync("doomed turn\r");
            (await runner.WaitForOutputAsync(
                    s => s.Contains("usage balance exhausted"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("the dead turn renders the 402 text, not a FAKE response");

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            var text = await WaitForUpdatesAsync(
                updatesPath,
                "\"sessionUpdate\":\"retry_state\"",
                "\"type\":\"failed\"",
                "\"error_type\":\"api\"",
                "\"stop_reason\":\"error\"",
                "\"agent_result\":",
                "usage balance exhausted",
                "user_message_chunk");
            text.ShouldNotContain("agent_message_chunk");
            text.ShouldNotContain("\"stop_reason\":\"end_turn\"");

            await runner.WriteAsync("Continue\r");
            (await runner.WaitForOutputAsync(
                    s => s.Contains("FAKE response to: Continue"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("a turn after the armed one must answer normally");
            var after = await WaitForUpdatesAsync(updatesPath, "\"stop_reason\":\"end_turn\"");
            after.ShouldContain("\"stop_reason\":\"end_turn\"");

            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Permission_denied_emits_the_unverified_403_card_wording()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string>
                {
                    ["GROK_HOME"] = home,
                    ["ANTIPHON_FAKE_API_ERROR"] = "permission_denied",
                },
                args: ["--cwd", cwd, "--session-id", sessionId]);

            await runner.WriteAsync("doomed\r");
            (await runner.WaitForOutputAsync(
                    s => s.Contains("status 403") || s.Contains("permission-denied"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("the 403 card wording should reach the screen");

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            var text = await WaitForUpdatesAsync(
                updatesPath,
                "\"stop_reason\":\"error\"",
                "status 403 Forbidden",
                "monthly spending limit");
            text.ShouldContain("\"type\":\"failed\"");

            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Server_error_mode_writes_a_500_failed_pair()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string>
                {
                    ["GROK_HOME"] = home,
                    ["ANTIPHON_FAKE_API_ERROR"] = "server_error",
                },
                args: ["--cwd", cwd, "--session-id", sessionId]);

            await runner.WriteAsync("doomed\r");
            (await runner.WaitForOutputAsync(
                    s => s.Contains("currently at capacity"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            var text = await WaitForUpdatesAsync(
                updatesPath,
                "\"stop_reason\":\"error\"",
                "status 500 Internal Server Error");
            text.ShouldContain("\"type\":\"failed\"");

            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Question_tool_submit_while_open_writes_completed_update_and_no_user_chunk()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string>
                {
                    ["GROK_HOME"] = home,
                    ["ANTIPHON_FAKE_QUESTION_TOOL"] = "1",
                },
                args: ["--cwd", cwd, "--session-id", sessionId]);

            await runner.WriteAsync("brief that opens a question\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("QUESTION-OPEN"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            await runner.WriteAsync("Proceed as planned (Recommended)\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("QUESTION-ANSWERED"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            var text = await WaitForUpdatesAsync(
                updatesPath,
                "\"title\":\"ask_user_question\"",
                "\"status\":\"completed\"",
                "User has answered your questions:",
                "Proceed as planned (Recommended)");

            var completedAt = text.IndexOf("\"status\":\"completed\"", StringComparison.Ordinal);
            completedAt.ShouldBeGreaterThan(0);
            var afterCompleted = text[completedAt..];
            afterCompleted.ShouldNotContain("user_message_chunk");
            afterCompleted.ShouldContain("User has answered your questions:");
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Question_tool_Esc_does_not_complete_the_tool()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string>
                {
                    ["GROK_HOME"] = home,
                    ["ANTIPHON_FAKE_QUESTION_TOOL"] = "1",
                },
                args: ["--cwd", cwd, "--session-id", sessionId]);

            await runner.WriteAsync("brief that opens a question\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("QUESTION-OPEN"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            await runner.WriteAsync("\u001b");
            (await runner.WaitForOutputAsync(
                s => s.Contains("QUESTION-ESC-IGNORED"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            var text = await WaitForUpdatesAsync(updatesPath, "\"title\":\"ask_user_question\"");
            text.ShouldNotContain("\"status\":\"completed\"");
            text.ShouldNotContain("QUESTION-ANSWERED");
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Resume_of_a_missing_session_exits_nonzero()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        try
        {
            var psi = new ProcessStartInfo(FakeGrokExe, $"--resume {Guid.NewGuid():D}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.Environment["GROK_HOME"] = home;
            using var process = Process.Start(psi)!;
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.ShouldNotBe(0);
            stderr.ShouldContain("Session not found");
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// CARD-0243: launch fakegrok, submit one marked prompt, return the last line of the
    /// <c>agent_message_chunk</c> text. Used to pin the report-line knob on and off.
    /// </summary>
    private static async Task<string> AgentChunkLastLineAsync(
        string prompt, IDictionary<string, string>? env)
    {
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        var fakeEnv = env is null
            ? new Dictionary<string, string> { ["GROK_HOME"] = home }
            : new Dictionary<string, string>(env) { ["GROK_HOME"] = home };
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: fakeEnv,
                args: ["--cwd", cwd, "--session-id", sessionId]);

            await runner.WriteAsync(prompt);
            await Task.Delay(25);
            await runner.WriteAsync("\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("SUBMITTED:"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            var updatesPath = Path.Combine(sessionDir, "updates.jsonl");
            var text = await WaitForUpdatesAsync(updatesPath, "agent_message_chunk", "turn_completed");
            await runner.KillAsync(TimeSpan.FromSeconds(2));
            return LastAgentChunkLine(text);
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    private static string LastAgentChunkLine(string updates)
    {
        foreach (var line in updates.Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            if (!line.Contains("agent_message_chunk", StringComparison.Ordinal))
                continue;
            using var doc = JsonDocument.Parse(line);
            var text = doc.RootElement
                .GetProperty("params")
                .GetProperty("update")
                .GetProperty("content")
                .GetProperty("text")
                .GetString() ?? "";
            var lines = text.Replace("\r\n", "\n").Split('\n');
            return lines[^1];
        }

        throw new InvalidOperationException("updates.jsonl had no agent_message_chunk");
    }

    /// <summary>
    /// CARD-0050 S3: poll-with-deadline + <see cref="FileShare.ReadWrite"/>, the same shape
    /// FakeClaude's transcript wait gained in slice 1. A single <c>File.ReadAllTextAsync</c>
    /// the instant SUBMITTED appears loses the race against the writer's append; the sidecar
    /// attached on deadline miss tells late-vs-lost-vs-starved.
    /// </summary>
    private static async Task<string> WaitForUpdatesAsync(string path, params string[] needles)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var text = "";
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    text = await ReadSharedTextAsync(path);
                    if (needles.All(n => text.Contains(n)))
                        return text;
                }
                catch (IOException)
                {
                    // Mid-append; try again.
                }
            }
            await Task.Delay(50);
        }

        var missing = string.Join(", ", needles.Where(n => !text.Contains(n)));
        text.Length.ShouldBeGreaterThan(
            0,
            $"updates.jsonl at {path} was still missing [{missing}] at the 10s deadline. "
            + $"Timing sidecar (process-start → per-record stamps):\n{ReadTimingSidecar(path)}");
        foreach (var needle in needles)
            text.ShouldContain(needle, customMessage:
                $"updates.jsonl at {path} was still missing [{needle}] at the 10s deadline. "
                + $"Timing sidecar:\n{ReadTimingSidecar(path)}\nContents:\n{text}");
        return text;
    }

    private static async Task<string> ReadSharedTextAsync(string path)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static string ReadTimingSidecar(string path)
    {
        try
        {
            var sidecar = path + ".timing";
            return File.Exists(sidecar)
                ? string.Join("\n", File.ReadAllLines(sidecar))
                : "(no sidecar written — the fake never reached its first append)";
        }
        catch (Exception ex)
        {
            return $"(sidecar unreadable: {ex.Message})";
        }
    }
}
