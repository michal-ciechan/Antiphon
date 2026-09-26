using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointExecutorLogTests
{
    [Test]
    public async Task concurrent_callbacks_append_each_line_once_without_overlap()
    {
        var sink = new GatedSink { GateFirstAppend = true };
        await using var writer = new ExecutorLogWriter(sink);
        var first = writer.AppendAsync("BUILD SLOT grant id=A");
        await sink.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = writer.AppendAsync("BUILD SLOT release id=B");
        sink.ActiveWrites.ShouldBe(1);
        sink.Overlaps.ShouldBe(0);
        sink.OpenFirst();
        await Task.WhenAll(first, second);
        await writer.FlushAsync();
        sink.Lines.Count.ShouldBe(2);
        sink.Lines.Count(line => line.EndsWith("BUILD SLOT grant id=A", StringComparison.Ordinal)).ShouldBe(1);
        sink.Lines.Count(line => line.EndsWith("BUILD SLOT release id=B", StringComparison.Ordinal)).ShouldBe(1);
        sink.Overlaps.ShouldBe(0);

        var callbackSink = new GatedSink { GateFirstAppend = true };
        await using var callbackWriter = new ExecutorLogWriter(callbackSink);
        var producerA = Task.Run(() => callbackWriter.Note("BUILD SLOT grant id=callback-A"));
        await callbackSink.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var producerB = Task.Run(() => callbackWriter.Note("BUILD SLOT release id=callback-B"));
        callbackSink.OpenFirst();
        await Task.WhenAll(producerA, producerB);
        await callbackWriter.FlushAsync();
        callbackSink.Lines.Count.ShouldBe(2);
        callbackSink.Lines.Count(line => line.EndsWith("id=callback-A", StringComparison.Ordinal)).ShouldBe(1);
        callbackSink.Lines.Count(line => line.EndsWith("id=callback-B", StringComparison.Ordinal)).ShouldBe(1);
        callbackSink.Overlaps.ShouldBe(0);
    }

    [Test]
    public async Task log_io_failure_publishes_a_terminal_error_without_recursive_logging()
    {
        foreach (var failure in new[] { "open", "append", "flush" })
        {
            var (run, runtime, sink) = NewRun();
            runtime = new CheckpointApp.Runtime
            {
                EnvironmentLookup = _ => null,
                Driver = runtime.Driver,
                Slots = runtime.Slots,
                LogSinkFactory = _ => failure == "open" ? throw new IOException("C762 sentinel open") : sink,
            };
            sink.FailAt = failure;
            var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime);
            exit.ShouldBe(ExitCodes.ExecutorCrashed);
            var state = new RunStateStore().TryRead(Path.Combine(run, "state.json")).ShouldNotBeNull();
            state.Phase.ShouldBe("done");
            state.ExitCode.ShouldBe(ExitCodes.ExecutorCrashed);
            state.Reason.ShouldContain("C762 sentinel");
            File.ReadAllText(Path.Combine(run, "report.md")).ShouldContain("C762 sentinel");
            sink.FailureAttempts.ShouldBeLessThanOrEqualTo(1);
        }
    }

    [Test]
    public async Task terminal_report_waits_for_all_callbacks_and_log_flush()
    {
        var (run, runtime, sink) = NewRun();
        sink.GateFlush = true;
        var execute = CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime);
        try
        {
            await sink.FlushEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            execute.IsCompleted.ShouldBeFalse();
            new RunStateStore().TryRead(Path.Combine(run, "state.json"))!.Phase.ShouldNotBe("done");
            sink.Lines.Any(line => line.Contains("done exit=0", StringComparison.Ordinal)).ShouldBeTrue();
        }
        finally { sink.OpenFlush(); }
        (await execute).ShouldBe(0);
        sink.Events.IndexOf("flush").ShouldBeLessThan(sink.Events.IndexOf("dispose"));
        new RunStateStore().TryRead(Path.Combine(run, "state.json"))!.Phase.ShouldBe("done");
        File.ReadAllText(Path.Combine(run, "report.md")).ShouldContain("exit=0");

        var (liveRun, _, _) = NewRun();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeDriver();
        driver.When(_ => true, async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return new DriverResult(0, "", "");
        });
        var liveRuntime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null, Driver = driver, Slots = new FixedSlotClient("off"),
        };
        var live = CheckpointApp.ExecuteAsync(liveRun, CancellationToken.None, liveRuntime);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            File.ReadAllText(Path.Combine(liveRun, "executor.log")).ShouldContain("execute rows=1");
        }
        finally
        {
            release.TrySetResult();
            await live;
        }
    }

    private static (string Run, CheckpointApp.Runtime Runtime, GatedSink Sink) NewRun()
    {
        var repo = CheckpointFixtures.TempDir();
        var manifest = new CheckpointManifest();
        manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 });
        var request = new RunRequest { Slots = "off", KeepOutputs = true };
        var run = CheckpointApp.CreateRun(manifest, request, repo);
        var sink = new GatedSink();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            Driver = new FakeDriver(),
            Slots = new FixedSlotClient("off"),
            LogSinkFactory = _ => sink,
        };
        return (run, runtime, sink);
    }

    private sealed class GatedSink : IExecutorLogSink
    {
        private readonly TaskCompletionSource _firstRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _flushRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FlushEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Lines { get; } = [];
        public List<string> Events { get; } = [];
        public bool GateFirstAppend { get; set; }
        public bool GateFlush { get; set; }
        public string? FailAt { get; set; }
        public int ActiveWrites => Volatile.Read(ref _active);
        public int Overlaps { get; private set; }
        public int FailureAttempts { get; private set; }

        public async Task AppendAsync(string line, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _active) > 1)
            {
                Overlaps++;
                Interlocked.Decrement(ref _active);
                throw new IOException("overlapping append");
            }
            try
            {
                if (FailAt == "append")
                {
                    FailureAttempts++;
                    throw new IOException("C762 sentinel append");
                }
                if (GateFirstAppend && Lines.Count == 0)
                {
                    FirstEntered.TrySetResult();
                    await _firstRelease.Task;
                }
                Lines.Add(line);
                Events.Add("append");
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (FailAt == "flush")
            {
                FailureAttempts++;
                throw new IOException("C762 sentinel flush");
            }
            if (GateFlush)
            {
                FlushEntered.TrySetResult();
                await _flushRelease.Task;
            }
            Events.Add("flush");
        }

        public ValueTask DisposeAsync()
        {
            Events.Add("dispose");
            return ValueTask.CompletedTask;
        }
        public void OpenFirst() => _firstRelease.TrySetResult();
        public void OpenFlush() => _flushRelease.TrySetResult();
    }
}
