using System.Diagnostics;
using Confluent.Kafka;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.Redpanda;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// One Redpanda broker shared by test classes instead of one per test: <see cref="C519"/> serves the C519 transport and
/// crash classes, so at most one such broker is up even when their tests interleave; each class disposes it after it
/// ends (a later test starts a new one). The classes run one test at a time (ProcessSpawnLimit), and every scenario
/// has its own topic (with its own size
/// cap, deleted when the scenario is disposed), consumer group, database schema and gateway (UnifiedOutboundTransport),
/// so no test sees another's broker state; a test must not change broker-wide state. Started by the first test that
/// needs it, with the class's own start (its timing and budget); a later test that reuses it prints
/// `<label> shared=true`. Before each reuse the broker must answer a metadata request (Testcontainers' State is cached,
/// not live), allowed 20 seconds and asked twice because a loaded host can be slow: one that still does not answer
/// prints `shared=replaced` and is disposed (bounded; a failed disposal is logged and left to the reaper) and replaced.
/// After two failed starts in a row within FailFastWindow the remaining tests fail at once with the last start's error
/// instead of each spending its own startup budget; a start after that window is tried again. Every step under the
/// gate has its own bound (two 30-second probes, a 60-second disposal, a start bounded by StartBound, whose
/// failed-start disposal is bounded too), so a gate
/// holder always finishes and no caller abandons work it started: a caller waits for the gate at most
/// <see cref="GateWait"/> (more than the longest holder) and then does its own bounded work. A test's own scenario
/// deadline starts after it has the broker. <see cref="StopAsync"/> runs after the class, under the same gate.
/// </summary>
internal sealed class SharedRedpandaBroker(Func<Task<RedpandaContainer>> start, string label)
{
    public const string Image = "docker.redpanda.com/redpandadata/redpanda:v25.3.4";
    private const string C519Label = "C1934_BROKER_PHASE";
    public static readonly TimeSpan GateWait = TimeSpan.FromSeconds(420);
    private static readonly TimeSpan StartBound = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan FailFastWindow = TimeSpan.FromMinutes(10);

    /// <summary>The broker of the C519 transport and crash classes, started with the CARD-1934 phase timing.</summary>
    public static SharedRedpandaBroker C519 { get; } = new(StartTimedAsync, C519Label);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private RedpandaContainer? _broker;
    private int _startFailures;
    private Exception? _startFailure;
    private DateTimeOffset _lastStartFailure;

    public async Task<RedpandaContainer> GetAsync()
    {
        if (!await _gate.WaitAsync(GateWait))
            throw new TimeoutException("the class's shared Redpanda broker was held by another test past every bound");
        try
        {
            if (_broker is { } current)
            {
                if (await AnswersAsync(current) || await AnswersAsync(current))
                {
                    Console.WriteLine(label + " shared=true");
                    return current;
                }
                Console.WriteLine(label + " shared=replaced");
                _broker = null;
                await DisposeAsync(current, label);
            }
            if (_startFailures >= 2 && DateTimeOffset.UtcNow - _lastStartFailure < FailFastWindow)
                throw new InvalidOperationException(
                    "the shared Redpanda broker failed to start twice in a row within " + FailFastWindow.TotalMinutes
                    + " minutes; this test did not try again", _startFailure);
            var starting = start();
            try
            {
                _broker = await starting.WaitAsync(StartBound);
            }
            catch (Exception ex)
            {
                // A start past its bound is left to finish on its own: whatever it starts is disposed.
                if (ex is TimeoutException)
                    _ = starting.ContinueWith(async late => { if (late.IsCompletedSuccessfully) await DisposeAsync(late.Result, label); },
                        TaskContinuationOptions.ExecuteSynchronously);
                _startFailures++;
                _startFailure = ex;
                _lastStartFailure = DateTimeOffset.UtcNow;
                throw;
            }
            _startFailures = 0;
            _startFailure = null;
            return _broker;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        if (!await _gate.WaitAsync(GateWait))
        {
            Console.WriteLine(label + " after-class gate not acquired; the reaper removes any broker left");
            return;
        }
        try
        {
            if (_broker is { } broker)
            {
                _broker = null;
                await DisposeAsync(broker, label);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // A live answer from the broker itself: cluster metadata. The client is built, asked and disposed off this thread,
    // and the whole probe is bounded; an abandoned probe is observed.
    private static async Task<bool> AnswersAsync(RedpandaContainer broker)
    {
        try
        {
            var bootstrap = broker.GetBootstrapAddress();
            var asking = Task.Run(() =>
            {
                using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
                return admin.GetMetadata(TimeSpan.FromSeconds(20)).Brokers.Count > 0;
            });
            _ = asking.ContinueWith(attempt => _ = attempt.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            return await asking.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>Disposal of a broker that is gone may itself fail or hang (Docker): bounded, logged, never thrown.</summary>
    public static async Task DisposeAsync(RedpandaContainer broker, string label)
    {
        try
        {
            await broker.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.WriteLine(label + " dispose-failed " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // CARD-1934 (ChannelOutboundUnifiedTransportTests). C519_Partial_prompt_requires_complete_receipt,
    // C519_Enabled_loss_notice_reaches_adapter and C519_Size_refusal used to pass their 120-second deadline to this same StartAsync. The broker is now shared
    // (C519, above): StartTimedAsync gives every start 120 seconds, and each caller bounds its wait for the shared
    // broker (GateWait; each test's own deadline starts once it has the broker). On 08cc3dc the exact main
    // UID spent 12.3s, 16.0s and 13.1s of the old 30-second token on Docker time before the Redpanda
    // container existed (load 21.63, 35.46 and 27.30), 40 to 53 percent of that budget, before Kafka
    // readiness. A failing-tail p95 is unavailable: those three passes finished inside 30 seconds, and
    // R5 would set the budget from about twice that p95. This aligns QueueToAdapterAsync and
    // C519_Converter_handoff with the sibling 120 seconds. Delivery assertions and every other timeout
    // stay as they are. Phases are TestTiming FixtureSetup spans: Ryuk create/start until the default
    // reaper reaches MaintainingConnection, Redpanda container create until Created (image check plus
    // Docker create), then Created until Started. That last span is UnsafeStartAsync through
    // UntilMessageIsLogged("Started Kafka API server"), including the container start and the
    // startup-script copy that precede the log line. C1934_BROKER_PHASE repeats the same durations
    // on the test output.
    private static async Task<RedpandaContainer> StartTimedAsync()
    {
        var broker = new RedpandaBuilder(Image).Build();
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await TimeOwnedBrokerStartAsync(broker, startup.Token);
            return broker;
        }
        catch
        {
            await DisposeAsync(broker, C519Label);
            throw;
        }
    }

    private static async Task TimeOwnedBrokerStartAsync(RedpandaContainer broker, CancellationToken startup)
    {
        var ryukReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var redpandaCreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var kafkaReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<ResourceReaperStateEventArgs> onReaper = (_, args) =>
        {
            if (args.State == ResourceReaperState.MaintainingConnection
                && args.Instance.SessionId == ResourceReaper.DefaultSessionId)
                ryukReady.TrySetResult();
        };
        ResourceReaper.StateChanged += onReaper;
        broker.Created += (_, _) => redpandaCreated.TrySetResult();
        broker.Started += (_, _) => kafkaReady.TrySetResult();
        long? ryukMs = null;
        long? createMs = null;
        long? kafkaMs = null;
        var starting = broker.StartAsync(startup);
        try
        {
            try
            {
                var ryuk = Stopwatch.StartNew();
                try
                {
                    using (TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "c1934-ryuk-create-start"))
                    {
                        if (ResourceReaper.IsUnavailable && TestcontainersSettings.ResourceReaperEnabled)
                            await WhenBrokerSignalAsync(ryukReady.Task, starting, startup);
                    }
                }
                finally
                {
                    ryukMs = ryuk.ElapsedMilliseconds;
                }

                var create = Stopwatch.StartNew();
                try
                {
                    using (TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "c1934-redpanda-container-create"))
                        await WhenBrokerSignalAsync(redpandaCreated.Task, starting, startup);
                }
                finally
                {
                    createMs = create.ElapsedMilliseconds;
                }

                var kafka = Stopwatch.StartNew();
                try
                {
                    using (TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "c1934-kafka-api-server"))
                        await WhenBrokerSignalAsync(kafkaReady.Task, starting, startup);
                }
                finally
                {
                    kafkaMs = kafka.ElapsedMilliseconds;
                }

                await starting;
            }
            catch
            {
                // The phase wait already surfaced the startup failure. Await StartAsync so its
                // exception is observed, then keep the original failure.
                try
                {
                    await starting;
                }
                catch (Exception)
                {
                }

                throw;
            }
        }
        finally
        {
            ResourceReaper.StateChanged -= onReaper;
            Console.WriteLine("C1934_BROKER_PHASE ryukMs=" + PhaseMs(ryukMs)
                + " createMs=" + PhaseMs(createMs) + " kafkaMs=" + PhaseMs(kafkaMs));
        }
    }

    private static async Task WhenBrokerSignalAsync(Task signal, Task starting, CancellationToken startup)
    {
        var done = await Task.WhenAny(signal, starting).WaitAsync(startup);
        if (!ReferenceEquals(done, signal))
            await starting;
    }

    private static string PhaseMs(long? value) =>
        value is long ms ? ms.ToString(System.Globalization.CultureInfo.InvariantCulture) : "incomplete";
}
