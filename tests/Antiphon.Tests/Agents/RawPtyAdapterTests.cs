using System.Diagnostics;
using System.Runtime.InteropServices;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Agents;

[NotInParallel("Pty")]
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public class RawPtyAdapterTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    private static AgentLaunchSpec SpecForCmd(string[] args, string? cwd = null) => new(
        DefinitionName: "raw-cmd",
        Kind: AgentKind.Raw,
        Exe: Cmd,
        Args: args,
        Env: new Dictionary<string, string>(),
        Cwd: cwd ?? Environment.CurrentDirectory,
        Cols: 120,
        Rows: 30);

    [Test]
    public async Task Starts_and_emits_text_delta_for_echo()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch("@echo off\r\necho HELLO_E2A\r\nexit /b 0\r\n");
        await using var adapter = new RawPtyAdapter();

        var captured = new List<string>();
        adapter.OnTextDelta += chunk => { lock (captured) captured.Add(chunk); };

        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/c", bat.Path }), CancellationToken.None);
        var exit = await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(15));

        exit.ShouldBe(0);
        string aggregated;
        lock (captured) aggregated = string.Concat(captured);
        aggregated.ShouldContain("HELLO_E2A");
    }

    [Test]
    public async Task Send_prompt_round_trips_via_interactive_shell()
    {
        SkipIfNotWindows();
        await using var adapter = new RawPtyAdapter();

        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/q", "/k", "@echo off & prompt $G" }), CancellationToken.None);

        await adapter.WaitForReadyAsync(CancellationToken.None);
        await adapter.SendPromptAsync("echo round_trip_marker_42", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        result.RawSnapshot.ShouldContain("round_trip_marker_42");
        result.IsAskingQuestion.ShouldBeFalse();
    }

    [Test]
    public async Task Send_input_writes_raw_keystrokes_to_session()
    {
        SkipIfNotWindows();
        await using var adapter = new RawPtyAdapter();

        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/q", "/k", "@echo off & prompt $G" }), CancellationToken.None);
        await adapter.WaitForReadyAsync(CancellationToken.None);
        await adapter.SendInputAsync("echo raw_input_marker_42\r", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.RawSnapshot.ShouldContain("raw_input_marker_42");
    }

    [Test]
    public async Task Resize_updates_pty_without_throwing()
    {
        SkipIfNotWindows();
        await using var adapter = new RawPtyAdapter();

        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/q", "/k", "@echo off & prompt $G" }), CancellationToken.None);

        await adapter.ResizeAsync(100, 24, CancellationToken.None);
        await adapter.SendPromptAsync("echo resize_marker_42", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.RawSnapshot.ShouldContain("resize_marker_42");
    }

    [Test]
    public async Task Kill_terminates_within_2s()
    {
        SkipIfNotWindows();
        await using var adapter = new RawPtyAdapter();
        await adapter.StartAsync(SpecForCmd(new[] { "/c", "ping -n 60 127.0.0.1 > nul" }), CancellationToken.None);

        await Task.Delay(300);
        var sw = Stopwatch.StartNew();
        var killed = await adapter.KillAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        sw.Stop();

        killed.ShouldBeTrue();
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2.5));
    }

    [Test]
    public async Task Wait_for_ready_returns_true_after_data()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch("@echo off\r\necho READY_MARK\r\n");
        await using var adapter = new RawPtyAdapter();
        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/c", bat.Path }), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeTrue();
        await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task Snapshot_methods_return_marker_after_echo()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch("@echo off\r\necho SNAP_MARK_001\r\nexit /b 0\r\n");
        await using var adapter = new RawPtyAdapter();
        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/c", bat.Path }), CancellationToken.None);
        await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(15));

        adapter.SnapshotRawOutput().ShouldContain("SNAP_MARK_001");
        adapter.SnapshotRenderedScreen().ShouldContain("SNAP_MARK_001");
    }

    [Test]
    public async Task Dispose_during_active_session_returns_within_3s()
    {
        SkipIfNotWindows();
        var adapter = new RawPtyAdapter();
        await adapter.StartAsync(SpecForCmd(new[] { "/c", "ping -n 60 127.0.0.1 > nul" }), CancellationToken.None);

        await Task.Delay(300);
        var sw = Stopwatch.StartNew();
        await adapter.DisposeAsync();
        sw.Stop();

        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task StartAsync_called_twice_throws()
    {
        SkipIfNotWindows();
        await using var adapter = new RawPtyAdapter();
        using var bat = new PtyTempBatch("@echo off\r\nexit /b 0\r\n");
        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/c", bat.Path }), CancellationToken.None);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            adapter.StartAsync(SpecForCmd(new[] { "/d", "/c", bat.Path }), CancellationToken.None));

        await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task Methods_throw_when_not_started()
    {
        await using var adapter = new RawPtyAdapter();
        Should.Throw<InvalidOperationException>(() => adapter.SnapshotRawOutput());
        Should.Throw<InvalidOperationException>(() => adapter.SnapshotRenderedScreen());
        await Should.ThrowAsync<InvalidOperationException>(() => adapter.SendPromptAsync("x", CancellationToken.None));
    }

    [Test]
    public async Task Wait_for_turn_complete_does_not_succeed_on_a_stripped_empty_slow_start()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch(
            "@echo off\r\nping -n 5 127.0.0.1 > nul\r\necho SLOW_START_BODY\r\nprompt $G\r\n");
        await using var adapter = new RawPtyAdapter();
        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/q", "/k", bat.Path }), CancellationToken.None);

        await adapter.SendPromptAsync("rem card-0052", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        VisiblePtyOutput.HasVisibleOutput(result.RawSnapshot).ShouldBeTrue(
            "a completed empty turn is the card title — the snapshot must have visible text");
        result.RawSnapshot.ShouldContain("SLOW_START_BODY");
    }

    [Test]
    public async Task Wait_for_ready_on_a_silent_shell_still_returns_true_after_ready_grace()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch(
            "@echo off\r\nprompt $S\r\nping -n 10 127.0.0.1 > nul\r\n");
        await using var adapter = new RawPtyAdapter();
        // Silent batch: no echo, space prompt, ping to nul. Title OSC is not
        // visible. ReadyGrace must still fire — do not wait forever.
        await adapter.StartAsync(SpecForCmd(new[] { "/d", "/q", "/c", bat.Path }), CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);
        sw.Stop();

        ready.ShouldBeTrue("silent raw shells stay valid — ReadyGrace, not forever-wait");
        sw.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(450),
            $"ReadyGrace is 500ms; `_ => true` used to return instantly. snapshot={adapter.SnapshotRawOutput()}");
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        await adapter.KillAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
    }
}
