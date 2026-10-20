using System.Net;
using System.Reflection;
using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class C1307CliCancellationTests : CheckpointTestBase
{
    [Test]
    public async Task Executor_cancel_persists_130()
    {
        foreach (var swallow in new[] { false, true })
        {
            using var cancel = new CancellationTokenSource();
            var entered = Signal();
            var canceled = Signal();
            var release = Signal();
            var slots = new ObservedSlots();
            var driver = new FakeDriver();
            driver.When(_ => true, async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (swallow) { }
                finally
                {
                    canceled.TrySetResult();
                    await release.Task;
                }
                return new DriverResult(0, "", "");
            });
            var run = NewRun();
            var custody = File.ReadAllBytes(Path.Combine(run, "ownership.json"));
            var execute = CheckpointApp.ExecuteAsync(run, cancel.Token, Runtime(driver, slots));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancel.Cancel();
                await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                execute.IsCompleted.ShouldBeFalse();
                new RunStateStore().TryRead(Path.Combine(run, "state.json"))!.Phase.ShouldNotBe("done");
                release.TrySetResult();
                (await execute.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(130);
                AssertTerminal(run, 130, "caller-requested");
                driver.InFlight.ShouldBe(0);
                slots.Acquires.ShouldBe(1);
                slots.Releases.ShouldBe(1);
                File.ReadAllBytes(Path.Combine(run, "ownership.json")).ShouldBe(custody);
                var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
                ReportWriter.RequiredReceiptRefusal(run, state).ShouldBeNull();
                ExecutorOwnershipStore.Write(run, new ProcessIdentityProbe().Current() with { Pid = int.MaxValue, StartUtcTicks = 1 });
                (await new WaitCommand().WaitAsync(run, TimeSpan.Zero, TimeSpan.FromMinutes(1),
                    TextWriter.Null, CancellationToken.None)).ShouldBe(130);
            }
            finally
            {
                cancel.Cancel();
                release.TrySetResult();
                await execute.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        // Cancellation during the final log flush must replace an earlier green
        // report before phase=done is published.
        using var late = new CancellationTokenSource();
        var sink = new CancelOnFlush(late);
        var lateRun = NewRun();
        (await CheckpointApp.ExecuteAsync(lateRun, late.Token, new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null, SourceCapture = _ => CheckpointFixtures.CleanSource(),
            Driver = new FakeDriver(), Slots = new FixedSlotClient("off"), LogSinkFactory = _ => sink,
        })).ShouldBe(130);
        sink.Disposed.ShouldBeTrue();
        AssertTerminal(lateRun, 130, "caller-requested");
    }

    [Test]
    public async Task Main_interrupt_reaches_command_and_unsubscribes()
    {
        var console = new ConsoleSignals();
        var barrier = new CancelBarrier();
        var releaseDriver = Signal();
        var slots = new ObservedSlots();
        var driver = new FakeDriver();
        driver.When(_ => true, async (_, token) =>
        {
            try { await barrier.Hold(token); }
            finally { await releaseDriver.Task; }
            return new DriverResult(0, "", "");
        });
        var repo = TempDir();
        var output = new StringWriter();
        CancellationToken caller = default;
        var main = Antiphon.Checkpoints.Program.MainAsync(RowArgs(repo), console.Subscribe, token =>
        {
            caller = token;
            return new CheckpointApp.Runtime
            {
                CancellationToken = token, Driver = driver, Slots = slots, Output = output,
                SourceCapture = _ => CheckpointFixtures.CleanSource(),
            };
        });
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.CanBeCanceled.ShouldBeTrue();
            console.Raise().Cancel.ShouldBeTrue();
            console.Raise().Cancel.ShouldBeTrue();
            caller.IsCancellationRequested.ShouldBeTrue();
            main.IsCompleted.ShouldBeFalse();
            releaseDriver.TrySetResult();
            (await main.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(130);
            output.ToString().ShouldContain("canceled: caller-requested");
            slots.Releases.ShouldBe(1);
            console.Subscriptions.ShouldBe(1);
            console.Removals.ShouldBe(1);
            console.Handler.ShouldBeNull();
        }
        finally
        {
            releaseDriver.TrySetResult();
            barrier.Release.TrySetResult();
            await main.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var success = new ConsoleSignals();
        (await Antiphon.Checkpoints.Program.MainAsync(
            ["status", "--repo-root", repo, "--results-root", TempDir()], success.Subscribe)).ShouldBe(0);
        success.Removals.ShouldBe(1);
        var invalid = new ConsoleSignals();
        (await Antiphon.Checkpoints.Program.MainAsync(["execute"], invalid.Subscribe,
            token => new CheckpointApp.Runtime { RepoRoot = repo, CancellationToken = token })).ShouldBe(2);
        invalid.Removals.ShouldBe(1);
        var error = new ConsoleSignals();
        await Should.ThrowAsync<InvalidOperationException>(() => Antiphon.Checkpoints.Program.MainAsync(
            RowArgs(repo), error.Subscribe, token => new CheckpointApp.Runtime
            {
                CancellationToken = token, SourceCapture = _ => throw new InvalidOperationException("composition sentinel"),
            }));
        error.Removals.ShouldBe(1);

        foreach (var helper in new[]
        {
            new[] { "smoke-write" }, new[] { "smoke-detach", "\0" },
            new[] { "hold", "--parent", int.MaxValue.ToString() },
        })
        {
            var excluded = new ConsoleSignals();
            await Antiphon.Checkpoints.Program.MainAsync(helper, excluded.Subscribe, token =>
            {
                token.CanBeCanceled.ShouldBeFalse();
                return new CheckpointApp.Runtime { CancellationToken = token };
            });
            excluded.Subscriptions.ShouldBe(0);
            excluded.Removals.ShouldBe(0);
        }
    }

    [Test]
    public async Task Cancel_before_request_read_is_not_crash()
    {
        foreach (var atAck in new[] { false, true })
        {
            var run = NewRun();
            using var cancel = new CancellationTokenSource();
            if (!atAck) cancel.Cancel();
            var driver = new FakeDriver();
            var request = File.ReadAllBytes(Path.Combine(run, "request.json"));
            var custody = File.ReadAllBytes(Path.Combine(run, "ownership.json"));
            (await CheckpointApp.ExecuteAsync(run, cancel.Token, new CheckpointApp.Runtime
            {
                EnvironmentLookup = _ => null, Driver = driver,
                ExecutorAckWrite = (path, identity) =>
                {
                    ExecutorOwnershipStore.Write(path, identity);
                    cancel.Cancel();
                },
            })).ShouldBe(130);
            AssertTerminal(run, 130, "caller-requested");
            driver.Calls.ShouldBeEmpty();
            File.ReadAllBytes(Path.Combine(run, "request.json")).ShouldBe(request);
            File.ReadAllBytes(Path.Combine(run, "ownership.json")).ShouldBe(custody);
        }

        // A canceled executor cannot overwrite somebody else's report to claim
        // that its terminal result was persisted.
        var refusedRun = NewRun();
        var foreign = Path.Combine(refusedRun, "report.md");
        File.WriteAllText(foreign, "foreign receipt sentinel");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var output = new StringWriter();
        (await CheckpointApp.ExecuteAsync(refusedRun, canceled.Token,
            new CheckpointApp.Runtime { Output = output })).ShouldBe(130);
        File.ReadAllText(foreign).ShouldBe("foreign receipt sentinel");
        output.ToString().ShouldContain("checkpoint terminal persistence failed:");
        output.ToString().ShouldContain("unrecorded-existing");
        File.Exists(Path.Combine(refusedRun, "state.json")).ShouldBeFalse();
    }

    [Test]
    public async Task Direct_row_cancel_reaches_slot_and_driver()
    {
        foreach (var phase in new[] { "probe", "acquire", "driver" })
        {
            using var cancel = new CancellationTokenSource();
            var barrier = new CancelBarrier();
            var slots = new ObservedSlots { Barrier = barrier, Phase = phase };
            var driver = new FakeDriver();
            driver.When(_ => true, async (_, token) =>
            {
                await barrier.Hold(token);
                return new DriverResult(0, "", "");
            });
            var command = Antiphon.Checkpoints.Program.RunAsync(RowArgs(TempDir()), new CheckpointApp.Runtime
            {
                CancellationToken = cancel.Token, Slots = slots, Driver = driver,
                SourceCapture = _ => CheckpointFixtures.CleanSource(), Output = TextWriter.Null,
            });
            try
            {
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                barrier.Token.CanBeCanceled.ShouldBeTrue(phase);
                cancel.Cancel();
                (await command.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(130, phase);
                barrier.Token.IsCancellationRequested.ShouldBeTrue(phase);
                driver.InFlight.ShouldBe(0);
                driver.Calls.Count.ShouldBe(phase == "driver" ? 1 : 0);
                slots.Releases.ShouldBe(phase == "driver" ? 1 : 0);
            }
            finally
            {
                cancel.Cancel();
                barrier.Release.TrySetResult();
                await command.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Test]
    public async Task No_caller_cancel_preserves_existing_results()
    {
        foreach (var mode in new[] { "green", "deadline", "crash", "internal-cancel" })
        {
            var run = NewRun();
            var driver = new FakeDriver();
            driver.When(_ => true, (_, _) => mode switch
            {
                "deadline" => throw new OperationCanceledException("internal row deadline"),
                "crash" => throw new IOException("driver crash sentinel"),
                _ => Task.FromResult(new DriverResult(0, "", "")),
            });
            var runtime = mode == "internal-cancel" ? new CheckpointApp.Runtime
            {
                ExecutorAckWrite = (_, _) => throw new OperationCanceledException("internal receipt deadline"),
            } : Runtime(driver, new FixedSlotClient("off"));
            var expected = mode == "green" ? 0 : mode == "deadline" ? 5 : 6;
            (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime)).ShouldBe(expected, mode);
            AssertTerminal(run, expected);

            if (mode != "green") continue;
            var state = File.ReadAllBytes(Path.Combine(run, "state.json"));
            var report = File.ReadAllBytes(Path.Combine(run, "report.json"));
            (await new WaitCommand().WaitAsync(run, TimeSpan.Zero, TimeSpan.FromMinutes(1),
                TextWriter.Null, CancellationToken.None)).ShouldBe(75);
            File.ReadAllBytes(Path.Combine(run, "state.json")).ShouldBe(state);
            File.ReadAllBytes(Path.Combine(run, "report.json")).ShouldBe(report);
        }

        var endedRun = NewRun();
        var ownerDriver = new FakeDriver();
        (await CheckpointApp.ExecuteAsync(endedRun, CancellationToken.None, new CheckpointApp.Runtime
        {
            EnvironmentLookup = name => name switch
            {
                "ANTIPHON_TASK_ID" => OwnerHandler.TaskId,
                "ANTIPHON_SESSION_ID" => OwnerHandler.SessionId,
                "ANTIPHON_API" => "http://owner.invalid",
                "ANTIPHON_TASK_TOKEN" => "synthetic-owner-token",
                _ => null,
            },
            OwnerHandler = new OwnerHandler(), Driver = ownerDriver,
            SourceCapture = _ => CheckpointFixtures.CleanSource(), Slots = new FixedSlotClient("off"),
        })).ShouldBe(7);
        AssertTerminal(endedRun, 7, "owner-ended");
        ownerDriver.Calls.ShouldBeEmpty();
    }

    private string NewRun()
    {
        var manifest = new CheckpointManifest
        {
            Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S3"], Command = "true", EstimatedMinutes = 1 }],
        };
        return CheckpointApp.CreateRun(manifest, new RunRequest { Slots = "off", KeepOutputs = true }, TempDir(),
            new CheckpointApp.Runtime { SourceCapture = _ => CheckpointFixtures.CleanSource() });
    }

    private static CheckpointApp.Runtime Runtime(IDriver driver, IBuildSlotClient slots) => new()
    {
        EnvironmentLookup = _ => null,
        SourceCapture = _ => CheckpointFixtures.CleanSource(),
        Driver = driver,
        Slots = slots,
    };

    private static void AssertTerminal(string run, int exit, string? reason = null)
    {
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json")).ShouldNotBeNull();
        state.Phase.ShouldBe("done");
        state.ExitCode.ShouldBe(exit);
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        report.ExitCode.ShouldBe(exit);
        report.Verdict.ShouldBe(exit == 0 ? "GREEN" : "RED");
        if (reason is not null)
        {
            state.Reason.ShouldBe(reason);
            report.Reason.ShouldBe(reason);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string[] RowArgs(string repo) =>
    [
        "row", "--repo-root", repo, "--name", "CP-CANCEL", "--project", "fixture",
        "--output-path", "bin-c1307/", "--filter", "/*/*/Fixture/*", "--no-build",
    ];

    private sealed class ObservedSlots : IBuildSlotClient
    {
        public int Acquires { get; private set; }
        public int Releases { get; private set; }
        public CancelBarrier? Barrier { get; init; }
        public string? Phase { get; init; }
        public async Task<SlotSession> ProbeAsync(CancellationToken cancellationToken)
        {
            if (Phase == "probe") await Barrier!.Hold(cancellationToken);
            return new SlotSession("enabled", 4);
        }
        public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            if (Phase == "acquire") await Barrier!.Hold(cancellationToken);
            Acquires++;
            return new SlotLease
            {
                State = "granted",
                ReleaseAsync = _ => { Releases++; return Task.CompletedTask; },
            };
        }
    }

    private sealed class CancelBarrier
    {
        public TaskCompletionSource Entered { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public CancellationToken Token { get; private set; }
        public async Task Hold(CancellationToken token)
        {
            Token = token;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
        }
    }

    private sealed class ConsoleSignals : IDisposable
    {
        public ConsoleCancelEventHandler? Handler { get; private set; }
        public int Subscriptions { get; private set; }
        public int Removals { get; private set; }
        public IDisposable Subscribe(ConsoleCancelEventHandler handler)
        {
            Handler.ShouldBeNull();
            Handler = handler;
            Subscriptions++;
            return this;
        }
        public ConsoleCancelEventArgs Raise()
        {
            var signal = (ConsoleCancelEventArgs)Activator.CreateInstance(typeof(ConsoleCancelEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [ConsoleSpecialKey.ControlC], null)!;
            Handler.ShouldNotBeNull().Invoke(this, signal);
            return signal;
        }
        public void Dispose()
        {
            Handler = null;
            Removals++;
        }
    }

    private sealed class CancelOnFlush(CancellationTokenSource cancel) : IExecutorLogSink
    {
        public bool Disposed { get; private set; }
        public Task AppendAsync(string line, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken)
        {
            cancel.Cancel();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OwnerHandler : HttpMessageHandler
    {
        public const string TaskId = "11111111-1111-4111-8111-111111111111";
        public const string SessionId = "22222222-2222-4222-8222-222222222222";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsoluteUri.ShouldBe("http://owner.invalid/api/agent-tasks/" + TaskId);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    summary = new { id = TaskId, agentSessionId = SessionId, status = "Succeeded" },
                    session = new { sessionId = SessionId, status = "Running" },
                })),
            });
        }
    }
}
