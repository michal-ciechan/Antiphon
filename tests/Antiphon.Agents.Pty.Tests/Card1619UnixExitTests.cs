using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.TestSupport;
using Porta.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[NativeLinuxOnly("CARD-1619 requires the real Linux Porta PTY wait path")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card1619UnixExitTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Test]
    public Task External_SIGKILL_preserves_signal() => AssertSignalAsync(9);

    [Test]
    public Task External_SIGTERM_preserves_signal() => AssertSignalAsync(15);

    [Test]
    public Task Normal_zero_has_known_no_signal() => AssertNormalAsync(0);

    [Test]
    public Task Normal_nonzero_preserves_code() => AssertNormalAsync(23);

    [Test]
    public async Task Signal_exit_preserves_actual_zero_and_separate_signal()
    {
        await using var child = await Child.StartAsync();
        child.Signal(9);
        var exit = await child.Runner.Completion.WaitAsync(Budget);
        exit.ExitSignal.ShouldBe(9, "actual-signal-separated");
        exit.ExitCode.ShouldBe(0, "Porta's actual code is retained, never 128+signal");
        (await child.Runner.Exited.WaitAsync(Budget)).ShouldBe(0, "legacy completion retains actual integer");
        child.Runner.ExitReason.ShouldBe(PtyExitReason.ProcessExited);
    }

    [Test]
    public async Task Fast_exit_is_replayed_once()
    {
        // First subscription window: force the real upstream reaper to finish before wrapping.
        using (var connection = await PtyProvider.SpawnAsync(Options("exit 23"), default))
        {
            connection.WaitForExit(5000).ShouldBeTrue("real fast child reaped before wrapper construction");
            using IPtySession session = new PortaPtySession(connection);
            session.ReadExitObservation().ShouldBe(new PtyExitObservation(23, 0), "wrapper replay retained");
        }

        // Second window: a live native callback finishes before the runner subscribes.
        // The callback and explicit completed read below deliberately overlap replay.
        var handled = 0;
        var delivered = 0;
        await using var runner = new PtyAgentRunner
        {
            ExitHandledForTest = () => Interlocked.Increment(ref handled),
            BeforeExitSubscriptionForTest = async session =>
            {
                var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                session.Exited += _ => { Interlocked.Increment(ref delivered); exit.TrySetResult(); };
                await session.WriterStream.WriteAsync("release\n"u8.ToArray());
                await session.WriterStream.FlushAsync();
                await exit.Task.WaitAsync(Budget);
                session.ReadExitObservation().ShouldBe(new PtyExitObservation(23, 0));
            },
        };
        await runner.StartAsync("/bin/sh", ["-c", "read release; exit 23"]);
        (await runner.Completion.WaitAsync(Budget)).ShouldBe(new PtyExitObservation(23, 0));
        (await runner.Exited.WaitAsync(Budget)).ShouldBe(23);
        runner.ExitReason.ShouldBe(PtyExitReason.ProcessExited);
        handled.ShouldBe(1, "callback/replay handles reason and cleanup once");
        delivered.ShouldBe(1, "wrapper publishes once");

        // Completion between wrapper subscription and its WaitForExit(0) replay:
        // the upstream event is set before its callback, so either publisher may win.
        using var overlapping = await PtyProvider.SpawnAsync(Options("read release; exit 23"), default);
        var overlapCount = 0;
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IPtySession overlappedSession = new PortaPtySession(overlapping, subscribed =>
        {
            subscribed.Exited += _ => { Interlocked.Increment(ref overlapCount); published.TrySetResult(); };
            overlapping.WriterStream.Write("release\n"u8);
            overlapping.WriterStream.Flush();
            overlapping.WaitForExit(5000).ShouldBeTrue();
        });
        await published.Task.WaitAsync(Budget);
        overlappedSession.ReadExitObservation().ShouldBe(new PtyExitObservation(23, 0));
        overlapCount.ShouldBe(1, "wrapper callback and replay deduplicate");
    }

    [Test]
    public async Task Porta_exitSignal_private_contract_holds()
    {
        using var connection = await PtyProvider.SpawnAsync(Options("exit 23"), default);
        connection.WaitForExit(5000).ShouldBeTrue();
        var diagnostics = new List<string>();
        var reader = new PortaExitSignalReader(diagnostic: diagnostics.Add);
        reader.ReadCompleted(connection).ShouldBe(0, "production accessor validates the real loaded field and both assembly versions");
        connection.ExitCode.ShouldBe(23);
        diagnostics.ShouldBeEmpty();
    }

    [Test]
    [Arguments("missing-field")]
    [Arguments("wrong-field-type")]
    [Arguments("wrong-version")]
    public async Task Unsupported_contract_keeps_signal_unknown(string contract)
    {
        using var connection = await PtyProvider.SpawnAsync(Options("exit 0"), default);
        connection.WaitForExit(5000).ShouldBeTrue();
        var diagnostics = new List<string>();
        var reader = new PortaExitSignalReader(
            fieldName: contract switch
            {
                "missing-field" => "exitSignalX",
                "wrong-field-type" => "terminalProcessTerminatedEvent",
                _ => "exitSignal",
            },
            assemblyVersion: contract == "wrong-version" ? "1.0.8.0" : "1.0.7.0",
            diagnostic: diagnostics.Add);
        reader.ReadCompleted(connection).ShouldBeNull("unsupported-contract-unknown");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].ShouldContain("signal unknown");
    }

    private static async Task AssertSignalAsync(int signal)
    {
        await using var child = await Child.StartAsync();
        child.Signal(signal);
        var exit = await child.Runner.Completion.WaitAsync(Budget);
        exit.ExitSignal.ShouldBe(signal, "real-external-signal-preserved");
        exit.ExitCode.ShouldBe(0);
        child.Runner.ExitReason.ShouldBe(PtyExitReason.ProcessExited);
    }

    private static async Task AssertNormalAsync(int code)
    {
        await using var child = await Child.StartAsync(code);
        await child.Runner.WriteAsync("release\n");
        var exit = await child.Runner.Completion.WaitAsync(Budget);
        exit.ExitCode.ShouldBe(code, "actual-normal-code");
        exit.ExitSignal.ShouldBe(0, "observed-no-signal-is-known");
    }

    private static PtyOptions Options(string script) => new()
    {
        Name = "card1619", App = "/bin/sh", CommandLine = ["-c", script],
        Cwd = AppContext.BaseDirectory, Cols = 80, Rows = 24,
        Environment = new Dictionary<string, string>(),
    };

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    private sealed class Child : IAsyncDisposable
    {
        public PtyAgentRunner Runner { get; } = new();
        private DateTime _started;

        public static async Task<Child> StartAsync(int code = 0)
        {
            var child = new Child();
            try
            {
                var nonce = "READY_" + Guid.NewGuid().ToString("N");
                await child.Runner.StartAsync("/bin/sh", ["-c", $"printf '{nonce}\\n'; read release; exit {code}"]);
                (await child.Runner.WaitForOutputAsync(text => text.Contains(nonce, StringComparison.Ordinal), Budget)).ShouldBeTrue("owned-child-ready");
                child.Runner.Backend!.Backend.ShouldBe(PtyBackend.UnixPty);
                using var process = Process.GetProcessById(child.Runner.Pid!.Value);
                child._started = process.StartTime;
                return child;
            }
            catch { await child.DisposeAsync(); throw; }
        }

        public void Signal(int signal)
        {
            using var process = Process.GetProcessById(Runner.Pid!.Value);
            process.StartTime.ShouldBe(_started, "owned PID/start identity before external signal");
            kill(process.Id, signal).ShouldBe(0, "external signal syscall succeeds");
        }

        public async ValueTask DisposeAsync()
        {
            if (Runner.Pid is not null && !Runner.Exited.IsCompleted)
                (await Runner.KillAsync(Budget)).ShouldBeTrue("fixture joins owned child");
            await Runner.DisposeAsync();
        }
    }
}
