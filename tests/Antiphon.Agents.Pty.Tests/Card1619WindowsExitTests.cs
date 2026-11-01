using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card1619WindowsExitTests
{
    [Test]
    public Task Modern_exit_and_stop_preserve_windows_behavior() => AssertBackendAsync("modern", PtyBackend.ModernConPty);

    [Test]
    public Task Inbox_exit_and_stop_preserve_windows_behavior() => AssertBackendAsync("inbox", PtyBackend.InboxConhost);

    private static async Task AssertBackendAsync(string backend, PtyBackend expected)
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Requires native Windows ConPTY");
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var budget = TimeSpan.FromSeconds(15);
        foreach (var code in new[] { 0, 23 })
        {
            await using var runner = new PtyAgentRunner(backend);
            await runner.StartAsync(cmd, ["/d", "/c", $"exit /b {code}"]);
            runner.Backend!.Backend.ShouldBe(expected, "production factory must select the requested native backend");
            (await runner.Exited.WaitAsync(budget)).ShouldBe(code);
            (await runner.Completion.WaitAsync(budget)).ShouldBe(new PtyExitObservation(code, 0));
        }
        await using var stopped = new PtyAgentRunner(backend);
        var nonce = "READY_" + Guid.NewGuid().ToString("N");
        await stopped.StartAsync(cmd, ["/d", "/c", $"echo {nonce}& set /p release="]);
        try
        {
            stopped.Backend!.Backend.ShouldBe(expected);
            (await stopped.WaitForOutputAsync(text => text.Contains(nonce, StringComparison.Ordinal), budget)).ShouldBeTrue();
            (await stopped.KillAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            // Modern terminates its job with 1; inbox uses .NET TerminateProcess(..., -1).
            var killCode = expected == PtyBackend.ModernConPty ? 1 : -1;
            (await stopped.Exited.WaitAsync(budget)).ShouldBe(killCode);
            (await stopped.Completion.WaitAsync(budget)).ShouldBe(new PtyExitObservation(killCode, 0));
            stopped.ExitReason.ShouldBe(PtyExitReason.KilledByRequest);
        }
        finally
        {
            if (!stopped.Exited.IsCompleted)
                (await stopped.KillAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("join owned Windows child");
        }
    }
}
