using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using FluentAssertions;
using Xunit;

namespace Antiphon.Agents.Pty.Tests;

[Trait("Category", "Pty")]
public class ClaudeDetectorsTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows() => Skip.IfNot(IsWindows, "ConPTY only on Windows");

    [SkippableFact]
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
        ready.Should().BeTrue();

        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [SkippableFact]
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
        done.Should().BeTrue();

        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [SkippableFact]
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
        done.Should().BeFalse();

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
