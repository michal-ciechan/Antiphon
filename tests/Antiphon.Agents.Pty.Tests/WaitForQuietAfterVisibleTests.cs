using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class WaitForQuietAfterVisibleTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    [Test]
    public async Task WaitForQuietAsync_on_a_silent_child_still_returns_true()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nping -n 3 127.0.0.1 > nul\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var quiet = await runner.WaitForQuietAsync(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(15));

        quiet.ShouldBeTrue("WaitForQuietAsync is unchanged: empty+quiet is still quiet");
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task WaitForQuietAfterVisibleAsync_on_a_silent_child_returns_false_before_any_body()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nping -n 3 127.0.0.1 > nul\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var afterVisible = await runner.WaitForQuietAfterVisibleAsync(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1));

        afterVisible.ShouldBeFalse("helper requires visible output; a silent ping is not life");
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task WaitForQuietAfterVisibleAsync_returns_true_only_after_slow_start_body()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch(
            "@echo off\r\nping -n 5 127.0.0.1 > nul\r\necho SLOW_START_BODY\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var sw = Stopwatch.StartNew();
        var afterVisible = await runner.WaitForQuietAfterVisibleAsync(
            TimeSpan.FromMilliseconds(600),
            TimeSpan.FromSeconds(15));
        sw.Stop();

        afterVisible.ShouldBeTrue();
        runner.SnapshotText().ShouldContain("SLOW_START_BODY");
        sw.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(2),
            "must not fire during the ~4s silent ping window");
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
