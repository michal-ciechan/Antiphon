using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointExecutorLogTests : CheckpointTestBase
{
    [Test]
    [Timeout(60_000)]
    public async Task concurrent_callbacks_append_each_line_once_without_overlap(CancellationToken cancellationToken)
    {
        var sink = new GatedSink { GateFirstAppend = true };
        await using var writer = new ExecutorLogWriter(sink);
        var first = writer.AppendAsync("BUILD SLOT grant id=A");
        await sink.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var second = writer.AppendAsync("BUILD SLOT release id=B");
        sink.ActiveWrites.ShouldBe(1);
        sink.Overlaps.ShouldBe(0);
        sink.OpenFirst();
        await Task.WhenAll(first, second);
        await writer.FlushAsync(cancellationToken);
        sink.Lines.Count.ShouldBe(2);
        sink.Lines.Count(line => line.EndsWith("BUILD SLOT grant id=A", StringComparison.Ordinal)).ShouldBe(1);
        sink.Lines.Count(line => line.EndsWith("BUILD SLOT release id=B", StringComparison.Ordinal)).ShouldBe(1);
        sink.Overlaps.ShouldBe(0);

        var callbackSink = new GatedSink { GateFirstAppend = true };
        await using var callbackWriter = new ExecutorLogWriter(callbackSink);
        var producerA = Task.Run(() => callbackWriter.Note("BUILD SLOT grant id=callback-A"));
        await callbackSink.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var producerB = Task.Run(() => callbackWriter.Note("BUILD SLOT release id=callback-B"));
        callbackSink.OpenFirst();
        await Task.WhenAll(producerA, producerB);
        await callbackWriter.FlushAsync(cancellationToken);
        callbackSink.Lines.Count.ShouldBe(2);
        callbackSink.Lines.Count(line => line.EndsWith("id=callback-A", StringComparison.Ordinal)).ShouldBe(1);
        callbackSink.Lines.Count(line => line.EndsWith("id=callback-B", StringComparison.Ordinal)).ShouldBe(1);
        callbackSink.Overlaps.ShouldBe(0);
    }

    [Test]
    [Timeout(15_000)]
    public async Task note_returns_when_the_sink_resumes_on_the_caller_context(CancellationToken cancellationToken)
    {
        // The caller thread posts the sink's completion back onto itself. Note used to block
        // that same thread inside AppendAsync().GetResult(), so the completion never ran.
        using var caller = new CallerThread();
        var sink = new CallerContextSink();
        var writer = new ExecutorLogWriter(sink);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        caller.Post(() =>
        {
            try
            {
                writer.Note("BUILD SLOT grant id=caller-context");
                finished.TrySetResult();
            }
            catch (Exception ex)
            {
                finished.TrySetException(ex);
            }
        });
        var completed = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
        completed.ShouldBe(finished.Task, sink.Diagnostic);
        await finished.Task;
        sink.Lines.Count.ShouldBe(1);
        sink.Lines[0].EndsWith("id=caller-context", StringComparison.Ordinal).ShouldBeTrue();
        await writer.DisposeAsync();
    }

    [Test]
    [Timeout(15_000)]
    public async Task repeat_dispose_completes_after_the_sink_dispose_faults()
    {
        var sink = new FaultingDisposeSink();
        var writer = new ExecutorLogWriter(sink);
        var first = await Should.ThrowAsync<IOException>(async () => await writer.DisposeAsync());
        first.Message.ShouldBe("injected log dispose");
        sink.Disposes.ShouldBe(1);
        await writer.DisposeAsync();
        sink.Disposes.ShouldBe(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task publication_failure_plus_log_close_failure_stays_invalid()
    {
        var (run, runtime, sink) = NewRun();
        sink.ThrowOnDispose = true;
        var io = new OneShotFinishingStateIo();
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
        var output = new StringWriter();
        runtime = new CheckpointApp.Runtime
        {
            CleanupContext = context,
            SourceCapture = _ => CheckpointFixtures.CleanSource(),
            EnvironmentLookup = _ => null,
            Driver = runtime.Driver,
            Slots = runtime.Slots,
            LogSinkFactory = _ => sink,
            Output = output,
        };
        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime);
        exit.ShouldBe(ExitCodes.Invalid);
        output.ToString().ShouldContain("checkpoint log disposal failed");
        sink.Disposes.ShouldBe(1);
        io.Denied.ShouldBe(1);
        var state = new RunStateStore(context).TryRead(Path.Combine(run, "state.json")).ShouldNotBeNull();
        state.ExitCode.ShouldNotBe(ExitCodes.ExecutorCrashed);
        (state.Reason ?? "").ShouldNotContain("executor.log or executor failure");
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), CheckpointApp.Json);
        report.ShouldNotBeNull().ExitCode.ShouldBe(ExitCodes.Invalid);
    }

    [Test]
    public async Task log_io_failure_publishes_a_terminal_error_without_recursive_logging()
    {
        foreach (var failure in new[] { "open", "append", "flush" })
        {
            var (run, runtime, sink) = NewRun();
            runtime = new CheckpointApp.Runtime
            {
                SourceCapture = _ => CheckpointFixtures.CleanSource(),
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
            SourceCapture = _ => CheckpointFixtures.CleanSource(),
            EnvironmentLookup = _ => null, Driver = driver, Slots = new FixedSlotClient("off"),
        };
        var live = CheckpointApp.ExecuteAsync(liveRun, CancellationToken.None, liveRuntime);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ReadWhileWriterHolds(Path.Combine(liveRun, "executor.log")).ShouldContain("execute rows=1");
        }
        finally
        {
            release.TrySetResult();
            await live;
        }
    }

    private static string ReadWhileWriterHolds(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private (string Run, CheckpointApp.Runtime Runtime, GatedSink Sink) NewRun()
    {
        var repo = TempDir();
        var manifest = new CheckpointManifest();
        manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 });
        var request = new RunRequest { Slots = "off", KeepOutputs = true };
        var run = CheckpointApp.CreateRun(manifest, request, repo,
            new CheckpointApp.Runtime { SourceCapture = _ => CheckpointFixtures.CleanSource() });
        var sink = new GatedSink();
        var runtime = new CheckpointApp.Runtime
        {
            SourceCapture = _ => CheckpointFixtures.CleanSource(),
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

        public int Disposes { get; private set; }
        public bool ThrowOnDispose { get; set; }

        public ValueTask DisposeAsync()
        {
            Disposes++;
            Events.Add("dispose");
            if (ThrowOnDispose)
                throw new IOException("injected log dispose");
            return ValueTask.CompletedTask;
        }
        public void OpenFirst() => _firstRelease.TrySetResult();
        public void OpenFlush() => _flushRelease.TrySetResult();
    }

    private sealed class CallerThread : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;

        public CallerThread()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "executor-log-caller" };
            _thread.Start();
        }

        public void Post(Action action) => _queue.Add(action);

        public void Dispose() => _queue.CompleteAdding();

        private void Run()
        {
            SynchronizationContext.SetSynchronizationContext(new QueueContext(_queue));
            foreach (var action in _queue.GetConsumingEnumerable())
                action();
        }

        private sealed class QueueContext : SynchronizationContext
        {
            private readonly BlockingCollection<Action> _queue;
            public QueueContext(BlockingCollection<Action> queue) => _queue = queue;
            public override void Post(SendOrPostCallback callback, object? state) => _queue.Add(() => callback(state));
            public override void Send(SendOrPostCallback callback, object? state)
            {
                using var done = new ManualResetEventSlim(false);
                Exception? error = null;
                _queue.Add(() =>
                {
                    try { callback(state); }
                    catch (Exception ex) { error = ex; }
                    finally { done.Set(); }
                });
                done.Wait();
                if (error is not null)
                    ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
    }

    /// <summary>
    /// Completes the write only by posting back to the synchronization context that entered
    /// AppendAsync. A caller blocked inside that call never runs the post.
    /// </summary>
    private sealed class CallerContextSink : IExecutorLogSink
    {
        public List<string> Lines { get; } = [];
        public string Diagnostic => $"sink entered={Entered:O} completion={Completed:O} context={Context}";
        private DateTimeOffset Entered;
        private DateTimeOffset Completed;
        private string? Context;

        public async Task AppendAsync(string line, CancellationToken cancellationToken)
        {
            var context = SynchronizationContext.Current;
            Entered = DateTimeOffset.UtcNow;
            Context = context?.GetType().Name ?? "thread-pool";
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Complete() { Completed = DateTimeOffset.UtcNow; done.TrySetResult(); }
            if (context is not null)
                context.Post(_ => Complete(), null);
            else
                ThreadPool.QueueUserWorkItem(_ => Complete());
            await done.Task;
            cancellationToken.ThrowIfCancellationRequested();
            Lines.Add(line);
        }

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FaultingDisposeSink : IExecutorLogSink
    {
        public int Disposes { get; private set; }
        public Task AppendAsync(string line, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Disposes++;
            throw new IOException("injected log dispose");
        }
    }

    /// <summary>
    /// Denies the finishing state.json publication once, then lets later writes through
    /// so a crashed-executor rewrite would be visible.
    /// </summary>
    private sealed class OneShotFinishingStateIo : ForwardingNativeIo
    {
        public int Denied { get; private set; }

        public override void Move(string source, string destination, bool overwrite)
        {
            if (Denied == 0
                && destination.EndsWith(Path.DirectorySeparatorChar + "state.json", StringComparison.Ordinal)
                && File.ReadAllText(source).Contains("\"phase\": \"finishing\"", StringComparison.Ordinal))
            {
                Denied++;
                throw new IOException("injected disk full", unchecked((int)0x80070070));
            }

            base.Move(source, destination, overwrite);
        }
    }
}
