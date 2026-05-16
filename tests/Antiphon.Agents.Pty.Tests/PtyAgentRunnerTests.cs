using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[NotInParallel("Headed")]
[Category("Pty")]
public class PtyAgentRunnerTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!(IsWindows)) throw new SkipTestException("ConPTY only on Windows");
    }

    // ---------- S01: spawn + capture ----------

    [Test]
    public async Task Spawn_and_capture_known_exit_code()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\necho marker-xyz\r\nexit /b 42\r\n");

        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var exit = await runner.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        exit.ShouldBe(42);
        runner.SnapshotText().ShouldContain("marker-xyz");
    }

    // ---------- S05: lifecycle ----------

    [Test]
    public async Task Kill_terminates_within_2s()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        await runner.StartAsync(Cmd, new[] { "/c", "ping -n 60 127.0.0.1 > nul" });

        await Task.Delay(300);
        var sw = Stopwatch.StartNew();
        var killed = await runner.KillAsync(TimeSpan.FromSeconds(2));
        sw.Stop();

        killed.ShouldBeTrue();
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2.5));
    }

    [Test]
    public async Task Pid_null_before_start_set_after()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        runner.Pid.ShouldBeNull();

        using var bat = new TempBatch("@echo off\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });
        runner.Pid.ShouldNotBeNull();
        runner.Pid!.Value.ShouldBeGreaterThan(0);

        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task StartAsync_called_twice_throws()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nping -n 30 127.0.0.1 > nul\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        await Should.ThrowAsync<InvalidOperationException>(
            () => runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path }));

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task WriteAsync_before_StartAsync_throws()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        await Should.ThrowAsync<InvalidOperationException>(
            () => runner.WriteAsync("hi"));
    }

    [Test]
    public async Task Resize_before_StartAsync_throws()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        Should.Throw<InvalidOperationException>(() => runner.Resize(80, 24));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Exited_completes_exactly_once()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nexit /b 7\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var exit1 = await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        var exit2 = await runner.Exited; // re-await same task
        exit1.ShouldBe(7);
        exit2.ShouldBe(7);
        runner.Exited.IsCompleted.ShouldBeTrue();
    }

    [Test]
    public async Task Dispose_mid_read_returns_within_3s()
    {
        SkipIfNotWindows();
        var runner = new PtyAgentRunner();
        await runner.StartAsync(Cmd, new[] { "/c", "ping -n 60 127.0.0.1 > nul" });

        await Task.Delay(200);
        var sw = Stopwatch.StartNew();
        await runner.DisposeAsync();
        sw.Stop();
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }

    // ---------- S02: streaming + ring buffer + OnData ----------

    [Test]
    public async Task OnData_fires_for_each_chunk()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        var hits = 0;
        var buf = new System.Text.StringBuilder();
        runner.OnData += s => { Interlocked.Increment(ref hits); lock (buf) buf.Append(s); };

        using var bat = new TempBatch("@echo off\r\necho onecho twoecho three\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));

        hits.ShouldBeGreaterThan(0);
        lock (buf) buf.ToString().ShouldContain("onecho");
    }

    [Test]
    public async Task Multiple_OnData_subscribers_each_receive_chunks()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        int a = 0, b = 0, c = 0;
        runner.OnData += _ => Interlocked.Increment(ref a);
        runner.OnData += _ => Interlocked.Increment(ref b);
        runner.OnData += _ => Interlocked.Increment(ref c);

        using var bat = new TempBatch("@echo off\r\nfor /L %%i in (1,1,5) do echo line-%%i\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));

        a.ShouldBeGreaterThan(0);
        a.ShouldBe(b);
        a.ShouldBe(c);
    }

    [Test]
    public async Task Output_ringbuffer_keeps_chunks_for_replay()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\necho replay-marker\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));

        var snap = runner.Output.Snapshot();
        snap.Length.ShouldBeGreaterThan(0);
        string.Concat(snap).ShouldContain("replay-marker");
    }

    [Test]
    public async Task SnapshotText_returns_concatenated_chunks()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        // Single bat with two distinct markers separated by short delay so each lands in a separate read,
        // but we only assert the concat contains both — terminal may visually overwrite via ANSI but the
        // raw byte stream we capture preserves both.
        using var bat = new TempBatch("@echo off\r\necho marker-aaa\r\nping -n 2 127.0.0.1 > nul\r\necho marker-bbb\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path }, cols: 200, rows: 50);
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(15));

        var text = runner.SnapshotText();
        text.ShouldContain("marker-aaa");
        text.ShouldContain("marker-bbb");
        text.IndexOf("marker-aaa", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("marker-bbb", StringComparison.Ordinal));
    }

    [Test]
    public async Task ClearLiveBuffer_resets_snapshot_only()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\necho before\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));

        runner.SnapshotText().ShouldContain("before");
        var ringBefore = runner.Output.Snapshot().Length;

        runner.ClearLiveBuffer();
        runner.SnapshotText().ShouldBeEmpty();
        runner.Output.Snapshot().Length.ShouldBe(ringBefore, "ring buffer is independent of live buffer");
    }

    // ---------- S03: stdin ----------

    [Test]
    public async Task Stdin_round_trip_via_pwsh()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        await runner.StartAsync("pwsh.exe", new[] { "-NoProfile", "-NoLogo" });

        // wait for prompt
        var ready = await runner.WaitForOutputAsync(s => s.Contains(">"), TimeSpan.FromSeconds(10));
        ready.ShouldBeTrue("pwsh prompt should appear within 10s");

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("'echo-token-9F3X'");
        var matched = await runner.WaitForOutputAsync(
            s => s.Contains("echo-token-9F3X"), TimeSpan.FromSeconds(5));
        matched.ShouldBeTrue();

        await runner.SendLineAsync("exit");
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Stdin_large_write_64KB_does_not_truncate()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        await runner.StartAsync(
            "pwsh.exe",
            new[]
            {
                "-NoProfile",
                "-NoLogo",
                "-Command",
                "$line=[Console]::In.ReadLine(); Write-Output $line.Length"
            });

        var big = new string('x', 65_536);
        runner.ClearLiveBuffer();
        await runner.WriteAsync($"{big}\r");

        var matched = await runner.WaitForOutputAsync(
            s => s.Contains("65536"), TimeSpan.FromSeconds(30));
        matched.ShouldBeTrue();

        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Concurrent_SendLineAsync_calls_do_not_corrupt_child_input()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        await runner.StartAsync(
            "pwsh.exe",
            new[]
            {
                "-NoProfile",
                "-NoLogo",
                "-Command",
                "while (($line = [Console]::In.ReadLine()) -ne 'DONE') { Write-Output \"OUT:$line\" }"
            });

        var messages = Enumerable.Range(1, 10)
            .Select(i => $"MSG-{i:00}")
            .ToArray();
        await Task.WhenAll(messages.Select(message => runner.SendLineAsync(message)));
        await runner.SendLineAsync("DONE");
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));

        var output = runner.SnapshotText();
        foreach (var message in messages)
            output.ShouldContain($"OUT:{message}");
    }

    // ---------- S04: resize ----------

    [Test]
    public async Task Resize_after_start_does_not_throw()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        await runner.StartAsync("pwsh.exe", new[] { "-NoProfile", "-NoLogo" });
        await runner.WaitForOutputAsync(s => s.Contains(">"), TimeSpan.FromSeconds(10));

        runner.Resize(80, 24);
        runner.Resize(200, 60);
        runner.Resize(40, 10);

        await runner.SendLineAsync("exit");
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Resize_rejects_invalid_dimensions()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nping -n 5 127.0.0.1 > nul\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        Should.Throw<ArgumentOutOfRangeException>(() => runner.Resize(0, 24));
        Should.Throw<ArgumentOutOfRangeException>(() => runner.Resize(80, -1));

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S06: wait helpers ----------

    [Test]
    public async Task WaitForOutput_matches_within_budget()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nping -n 2 127.0.0.1 > nul\r\necho TARGET-MARKER\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var matched = await runner.WaitForOutputAsync(
            s => s.Contains("TARGET-MARKER"), TimeSpan.FromSeconds(10));
        matched.ShouldBeTrue();
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task WaitForOutput_returns_false_on_timeout()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nping -n 10 127.0.0.1 > nul\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var matched = await runner.WaitForOutputAsync(
            s => s.Contains("NEVER-WILL-APPEAR"), TimeSpan.FromMilliseconds(500));
        matched.ShouldBeFalse();
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task WaitForQuiet_detects_quiet_after_burst()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nfor /L %%i in (1,1,3) do echo burst-%%i\r\nping -n 5 127.0.0.1 > nul\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var quiet = await runner.WaitForQuietAsync(
            quietPeriod: TimeSpan.FromMilliseconds(800),
            maxWait: TimeSpan.FromSeconds(10));
        quiet.ShouldBeTrue();
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task WaitForQuiet_returns_false_under_continuous_output()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\n:loop\r\necho noisy-%random%\r\nping -n 1 127.0.0.1 > nul\r\ngoto loop\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var quiet = await runner.WaitForQuietAsync(
            quietPeriod: TimeSpan.FromSeconds(2),
            maxWait: TimeSpan.FromSeconds(3));
        quiet.ShouldBeFalse();
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Wait_helpers_honour_cancellation_token()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nping -n 30 127.0.0.1 > nul\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sw = Stopwatch.StartNew();
        var result = await runner.WaitForOutputAsync(
            s => s.Contains("NEVER"), TimeSpan.FromSeconds(30), cts.Token);
        sw.Stop();
        result.ShouldBeFalse();
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));

        using var quietCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        sw.Restart();
        var quiet = await runner.WaitForQuietAsync(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            quietCts.Token);
        sw.Stop();
        quiet.ShouldBeFalse();
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ---------- S07: env + cwd ----------

    [Test]
    public async Task Env_vars_propagate_to_child()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        var env = new Dictionary<string, string>
        {
            ["ANTIPHON_TEST_VAR"] = "marker-7K9X"
        };
        using var bat = new TempBatch("@echo off\r\necho ENV=%ANTIPHON_TEST_VAR%\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path }, env: env);
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));

        runner.SnapshotText().ShouldContain("marker-7K9X");
    }

    [Test]
    public async Task Cwd_honoured_by_child()
    {
        SkipIfNotWindows();
        var temp = Path.Combine(Path.GetTempPath(), $"antiphon-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            await using var runner = new PtyAgentRunner();
            using var bat = new TempBatch("@echo off\r\necho CWD=%CD%\r\nexit /b 0\r\n");
            await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path }, cwd: temp);
            await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));

            runner.SnapshotText().ShouldContain(temp);
        }
        finally
        {
            try { Directory.Delete(temp); } catch { }
        }
    }
}
