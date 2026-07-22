using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("Pty")]
public class ClaudeDetectorsTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!(IsWindows)) throw new SkipTestException("ConPTY only on Windows");
    }

    [Test]
    public async Task ReadyDetector_returns_true_when_runner_settles()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\necho boot\r\nping -n 5 127.0.0.1 > nul\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var detector = new ClaudeReadyDetector
        {
            QuietPeriod = TimeSpan.FromMilliseconds(500),
            MaxWait = TimeSpan.FromSeconds(8)
        };
        var ready = await detector.WaitAsync(runner);
        ready.ShouldBeTrue();

        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task DoneDetector_returns_true_after_burst_settles()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\nfor /L %%i in (1,1,3) do echo burst-%%i\r\nping -n 5 127.0.0.1 > nul\r\nexit /b 0\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var detector = new ClaudeDoneDetector
        {
            QuietPeriod = TimeSpan.FromMilliseconds(800),
            MaxWait = TimeSpan.FromSeconds(8)
        };
        var done = await detector.WaitAsync(runner);
        done.ShouldBeTrue();

        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task DoneDetector_returns_false_under_continuous_output()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner();
        using var bat = new TempBatch("@echo off\r\n:loop\r\necho noisy-%random%\r\nping -n 1 127.0.0.1 > nul\r\ngoto loop\r\n");
        await runner.StartAsync(Cmd, new[] { "/d", "/c", bat.Path });

        var detector = new ClaudeDoneDetector
        {
            QuietPeriod = TimeSpan.FromSeconds(2),
            MaxWait = TimeSpan.FromSeconds(3)
        };
        var done = await detector.WaitAsync(runner);
        done.ShouldBeFalse();

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // PINNED-BY: ClaudeCompactionCanaryTests — the exact line real Claude rendered on 2026-07-22.
    [Test]
    public async Task CompactedDetector_matches_pinned_screen_line()
    {
        ClaudeCompactedDetector.Matches("  ⎿  Compacted (ctrl+o to see full summary)").ShouldBeTrue();
        ClaudeCompactedDetector.Matches("Compacted (ctrl+o to see full summary)").ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task CompactedDetector_ignores_ordinary_output()
    {
        ClaudeCompactedDetector.Matches("❯ /compact").ShouldBeFalse("the typed command itself is not the signal");
        ClaudeCompactedDetector.Matches("compact conversation history").ShouldBeFalse();
        ClaudeCompactedDetector.Matches("Context left until auto-compact: 8%").ShouldBeFalse();
        ClaudeCompactedDetector.Matches(null).ShouldBeFalse();
        await Task.CompletedTask;
    }
}
