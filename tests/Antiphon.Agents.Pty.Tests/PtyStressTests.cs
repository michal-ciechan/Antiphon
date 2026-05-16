using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("PtyStress")]
public class PtyStressTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!(IsWindows)) throw new SkipTestException("ConPTY only on Windows");
    }

    [Test]
    public async Task Spawn_dispose_loop_does_not_leak_processes()
    {
        SkipIfNotWindows();
        const int iterations = 50;

        int conhostBefore = Process.GetProcessesByName("conhost").Length;
        int handleBefore = Process.GetCurrentProcess().HandleCount;

        using var bat = new TempBatch("@echo off\r\nexit /b 0\r\n");
        for (int i = 0; i < iterations; i++)
        {
            var runner = new PtyAgentRunner();
            await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });
            await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10));
            await runner.DisposeAsync();
        }

        // Allow conhost teardown a moment.
        await Task.Delay(2000);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        int conhostAfter = Process.GetProcessesByName("conhost").Length;
        int handleAfter = Process.GetCurrentProcess().HandleCount;

        int conhostDelta = conhostAfter - conhostBefore;
        int handleDelta = handleAfter - handleBefore;

        // Allow some noise: other tools spawn conhost; handles fluctuate via runtime + TUnit.
        conhostDelta.ShouldBeLessThan(5,
            $"conhost.exe count must not climb significantly. before={conhostBefore} after={conhostAfter}");
        handleDelta.ShouldBeLessThan(500,
            $"handle count must not climb. before={handleBefore} after={handleAfter}");
    }

    [Test]
    public async Task Rapid_kill_before_first_read_succeeds_x20()
    {
        SkipIfNotWindows();
        const int iterations = 20;

        for (int i = 0; i < iterations; i++)
        {
            var runner = new PtyAgentRunner();
            await runner.StartAsync(Cmd, new[] { "/c", "ping -n 30 127.0.0.1 > nul" });
            // No delay — kill immediately.
            var killed = await runner.KillAsync(TimeSpan.FromSeconds(3));
            killed.ShouldBeTrue($"iteration {i} should kill within 3s");
            await runner.DisposeAsync();
        }
    }

    [Test]
    public async Task Child_emitting_1MB_does_not_OOM_runner()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();

        // 1MB roughly: 16384 lines × ~64 bytes each ≈ 1MB.
        // Use small ring buffer to confirm cap respected; live buffer grows but bounded by GC.
        using var bat = new TempBatch(
            "@echo off\r\n" +
            "for /L %%i in (1,1,16384) do @echo line-%%i-padding-padding-padding-padding-padding-padding\r\n" +
            "exit /b 0\r\n");

        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path }, cols: 200, rows: 50);
        var exit = await runner.Exited.WaitAsync(TimeSpan.FromSeconds(60));
        exit.ShouldBe(0);

        // Ring buffer capacity is 4096 chunks; should be at cap.
        runner.Output.Count.ShouldBeLessThanOrEqualTo(runner.Output.Capacity);
        runner.SnapshotText().Length.ShouldBeGreaterThan(500_000,
            "live buffer should hold the bulk of 1MB output");
        runner.SnapshotText().ShouldContain("line-16384-padding");
    }
}
