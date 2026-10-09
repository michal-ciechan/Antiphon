using System.Diagnostics;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0491 S0. Measures one <c>\x03</c> against the real grok.exe during a tool turn.
/// An empty composer must yield exactly one <c>turn_completed stop_reason=cancelled</c>
/// while the process stays alive and a later prompt is confirmed. A draft is the other
/// CP-1 arm: the process must stay alive, and any cancelled row is logged only.
/// Opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>). The red is a changed vendor contract.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("OptIn")]
[Category("HeadedCanary")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokMidTurnInterruptCanaryTests
{
    private const string ToolTurn =
        "Use run_terminal_command to run exactly: powershell -Command Start-Sleep 90; then reply done.";

    private const string SecondTurn = "Reply with exactly GK-SECOND-TURN-OK and use no tools";

    [Test]
    public async Task Ctrl_C_on_an_empty_composer_mid_tool_turn_cancels_the_turn_and_keeps_the_session_alive()
    {
        GkSession.SkipIfNotEligible();
        await using var live = await StartAsync(nameof(Ctrl_C_on_an_empty_composer_mid_tool_turn_cancels_the_turn_and_keeps_the_session_alive));
        try
        {
            var toolStarted = TimeProvider.System.GetTimestamp();
            await SubmitAsync(live.Runner, ToolTurn);
            var tool = await WaitForRowAsync(live.Updates, row => row.Kind == "tool_call", TimeSpan.FromMinutes(2));
            live.Log($"tool_call elapsed_ms={ElapsedMs(toolStarted)} found={tool is not null}");
            tool.ShouldNotBeNull(
                "the turn must reach a tool_call before Ctrl+C. Screen:\n" + GkSession.Tail(live.Runner.SnapshotScreen(), 1200));

            var rowsBefore = GkSession.ReadUpdates(live.Updates).Count;
            var cancelStarted = TimeProvider.System.GetTimestamp();
            await live.Runner.WriteAsync("\x03");
            var completed = await WaitForRowAsync(
                live.Updates,
                row => row.Kind == "turn_completed" && IndexOf(live.Updates, row) >= rowsBefore,
                TimeSpan.FromSeconds(30));
            live.Log($"turn_completed elapsed_ms={ElapsedMs(cancelStarted)} found={completed is not null}");

            var newRows = GkSession.ReadUpdates(live.Updates).Skip(rowsBefore).ToList();
            LogRows(live.Log, newRows);
            var completions = newRows.Where(row => row.Kind == "turn_completed").ToList();
            live.Log($"cancelled_boundaries={completions.Count(row => row.StopReason == "cancelled")}");
            completed.ShouldNotBeNull(
                "one Ctrl+C on an empty composer must produce a turn_completed within 30s. Kinds: "
                + string.Join(" > ", newRows.Select(row => row.Kind)));
            completions.Count.ShouldBe(1, "exactly one new turn_completed");
            completions[0].StopReason.ShouldBe("cancelled");
            live.Log("cancelled _meta: " + MetaOf(completions[0].Raw));

            var survived = !live.Runner.Exited.IsCompleted;
            live.Log($"process_survived={survived}");
            survived.ShouldBeTrue("the process must stay alive after one Ctrl+C");

            var echoStarted = TimeProvider.System.GetTimestamp();
            await live.Runner.WriteAsync("GK-AFTER-CTRLC");
            var echoed = await WaitForScreenAsync(
                live.Runner, screen => screen.Contains("GK-AFTER-CTRLC", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
            live.Log($"composer_echo elapsed_ms={ElapsedMs(echoStarted)} echoed={echoed}");
            echoed.ShouldBeTrue(
                "the composer must echo GK-AFTER-CTRLC within 5s. Screen:\n" + GkSession.Tail(live.Runner.SnapshotScreen(), 800));

            var followStarted = TimeProvider.System.GetTimestamp();
            await SubmitAsync(live.Runner, SecondTurn);
            var user = await WaitForRowAsync(
                live.Updates,
                row => row.Kind == "user_message_chunk" && (row.Text?.Contains("GK-SECOND-TURN-OK", StringComparison.Ordinal) ?? false),
                TimeSpan.FromMinutes(2));
            user.ShouldNotBeNull("the follow-up must land as a user_message_chunk containing GK-SECOND-TURN-OK");
            var end = await WaitForRowAsync(
                live.Updates,
                row => row.Kind == "turn_completed"
                    && row.StopReason == "end_turn"
                    && IndexOf(live.Updates, row) > IndexOf(live.Updates, user),
                TimeSpan.FromMinutes(2));
            live.Log($"follow_up elapsed_ms={ElapsedMs(followStarted)} user_chunk={user is not null} end_turn={end is not null}");
            end.ShouldNotBeNull("a turn_completed stop_reason=end_turn must follow the confirmed user_message_chunk");
            live.Log("follow_up_confirmed=true");
        }
        catch (Exception ex)
        {
            live.Log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
    }

    [Test]
    public async Task Ctrl_C_with_a_draft_in_the_composer_keeps_the_session_alive()
    {
        GkSession.SkipIfNotEligible();
        await using var live = await StartAsync(nameof(Ctrl_C_with_a_draft_in_the_composer_keeps_the_session_alive));
        try
        {
            var toolStarted = TimeProvider.System.GetTimestamp();
            await SubmitAsync(live.Runner, ToolTurn);
            var tool = await WaitForRowAsync(live.Updates, row => row.Kind == "tool_call", TimeSpan.FromMinutes(2));
            live.Log($"tool_call elapsed_ms={ElapsedMs(toolStarted)} found={tool is not null}");
            tool.ShouldNotBeNull(
                "the turn must reach a tool_call before the draft. Screen:\n" + GkSession.Tail(live.Runner.SnapshotScreen(), 1200));

            await live.Runner.WriteAsync("GK-DRAFT");
            var drafted = await WaitForScreenAsync(
                live.Runner, screen => screen.Contains("GK-DRAFT", StringComparison.Ordinal), TimeSpan.FromSeconds(8));
            drafted.ShouldBeTrue(
                "GK-DRAFT must be on screen before Ctrl+C, with no Enter. Screen:\n" + GkSession.Tail(live.Runner.SnapshotScreen(), 800));

            var rowsBefore = GkSession.ReadUpdates(live.Updates).Count;
            await live.Runner.WriteAsync("\x03");
            var aliveStarted = TimeProvider.System.GetTimestamp();
            var loggedExit = false;
            while (TimeProvider.System.GetElapsedTime(aliveStarted) < TimeSpan.FromSeconds(10))
            {
                if (!loggedExit && live.Runner.Exited.IsCompleted)
                {
                    loggedExit = true;
                    live.Log($"process_exited_during_draft_window_ms={ElapsedMs(aliveStarted)} code={live.Runner.Exited.Result}");
                }

                await Task.Delay(100);
            }

            var newRows = GkSession.ReadUpdates(live.Updates).Skip(rowsBefore).ToList();
            LogRows(live.Log, newRows);
            var cancelled = newRows.Count(row => row.Kind == "turn_completed" && row.StopReason == "cancelled");
            live.Log($"cancelled_boundaries={cancelled} (logged, not asserted)");
            foreach (var row in newRows.Where(row => row.Kind == "turn_completed"))
                live.Log("turn_completed _meta: " + MetaOf(row.Raw));

            var survived = !live.Runner.Exited.IsCompleted;
            live.Log($"process_survived={survived} elapsed_ms={ElapsedMs(aliveStarted)}");
            survived.ShouldBeTrue("Ctrl+C with a draft must not kill the session");

            var echoStarted = TimeProvider.System.GetTimestamp();
            await live.Runner.WriteAsync("GK-AFTER-DRAFT");
            var echoed = await WaitForScreenAsync(
                live.Runner, screen => screen.Contains("GK-AFTER-DRAFT", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
            live.Log($"composer_echo elapsed_ms={ElapsedMs(echoStarted)} echoed={echoed}");
            echoed.ShouldBeTrue(
                "the composer must echo typed text after Ctrl+C with a draft. Screen:\n"
                + GkSession.Tail(live.Runner.SnapshotScreen(), 800));
        }
        catch (Exception ex)
        {
            live.Log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
    }

    private static async Task<LiveSession> StartAsync(string testName)
    {
        RequireAuthFile();
        var log = GkSession.MeasurementLog(testName);
        log("grok --version: " + GrokVersion());
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);
        var runner = new PtyAgentRunner("modern");
        try
        {
            await runner.StartAsync(
                GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);
            RefuseSignInScreen(runner);
            var tail = GkSession.Tail(runner.SnapshotScreen(), 1500);
            log("READY-SCREEN-TAIL-BEGIN");
            log(tail);
            log("READY-SCREEN-TAIL-END");
            log("EMPTY-COMPOSER-PROMPT: " + LastNonBlank(tail));
            return new LiveSession(runner, cwd, sessionId, updates, log);
        }
        catch
        {
            try { await runner.KillAsync(TimeSpan.FromSeconds(3)); } catch { /* best effort */ }
            await runner.DisposeAsync();
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
            throw;
        }
    }

    private static void RequireAuthFile()
    {
        if (!File.Exists(Path.Combine(GkSession.DefaultGrokHome, "auth.json")))
            throw new InvalidOperationException("grok auth.json is absent; refusing to log in");
    }

    private static void RefuseSignInScreen(PtyAgentRunner runner)
    {
        var screen = runner.SnapshotScreen();
        if (screen.Contains("Approve in your browser", StringComparison.OrdinalIgnoreCase)
            || screen.Contains("Waiting for approval", StringComparison.OrdinalIgnoreCase)
            || screen.Contains("finish signing in", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("grok presented a sign-in screen; refusing to log in");
        }
    }

    private static async Task SubmitAsync(PtyAgentRunner runner, string text)
    {
        await runner.WriteAsync(text);
        await Task.Delay(50);
        await runner.WriteAsync("\r");
    }

    private static async Task<GrokUpdateRow?> WaitForRowAsync(
        string updatesPath, Func<GrokUpdateRow, bool> predicate, TimeSpan timeout)
    {
        var start = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(start) < timeout)
        {
            var match = GkSession.ReadUpdates(updatesPath).FirstOrDefault(predicate);
            if (match is not null)
                return match;
            await Task.Delay(50);
        }

        return null;
    }

    private static async Task<bool> WaitForScreenAsync(
        PtyAgentRunner runner, Func<string, bool> predicate, TimeSpan timeout)
    {
        var start = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(start) < timeout)
        {
            if (predicate(runner.SnapshotScreen()))
                return true;
            await Task.Delay(50);
        }

        return false;
    }

    private static int IndexOf(string updatesPath, GrokUpdateRow row)
    {
        var rows = GkSession.ReadUpdates(updatesPath);
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Raw == row.Raw)
                return i;
        }

        return -1;
    }

    private static void LogRows(Action<string> log, IReadOnlyList<GrokUpdateRow> rows)
    {
        log("ROW KINDS IN ORDER: " + string.Join(" > ", rows.Select(row => row.Kind ?? row.Method ?? "?")));
    }

    private static string MetaOf(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("params", out var parameters))
                return "<no params>";
            var updateMeta = "<none>";
            var paramsMeta = "<none>";
            if (parameters.TryGetProperty("update", out var update)
                && update.TryGetProperty("_meta", out var updateMetaElement))
            {
                updateMeta = updateMetaElement.GetRawText();
            }

            if (parameters.TryGetProperty("_meta", out var paramsMetaElement))
                paramsMeta = paramsMetaElement.GetRawText();
            return GkSession.Truncate("update._meta=" + updateMeta + " params._meta=" + paramsMeta, 1200);
        }
        catch (JsonException)
        {
            return "<unparsed>";
        }
    }

    private static string GrokVersion()
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(GkSession.GrokExePath, "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            if (!process.Start())
                return "<version not started>";
        }
        catch (Exception ex)
        {
            return "<version failed: " + ex.GetType().Name + ">";
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return "<version timed out>";
        }

        var text = (stdout + " " + stderr).Trim();
        return text.Length == 0 ? "<empty version>" : text;
    }

    private static string LastNonBlank(string screen)
    {
        var line = screen.Replace("\r\n", "\n").Split('\n')
            .Select(row => row.TrimEnd())
            .LastOrDefault(row => row.Trim().Length > 0);
        return line ?? "<none>";
    }

    private static long ElapsedMs(long start) =>
        (long)TimeProvider.System.GetElapsedTime(start).TotalMilliseconds;

    private sealed class LiveSession(
        PtyAgentRunner runner, string cwd, string sessionId, string updates, Action<string> log) : IAsyncDisposable
    {
        public PtyAgentRunner Runner { get; } = runner;
        public string Updates { get; } = updates;
        public Action<string> Log { get; } = log;

        public async ValueTask DisposeAsync()
        {
            try { await Runner.KillAsync(TimeSpan.FromSeconds(3)); } catch { /* best effort */ }
            await Runner.DisposeAsync();
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
        }
    }
}
