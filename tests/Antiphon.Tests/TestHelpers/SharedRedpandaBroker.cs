using Confluent.Kafka;
using Testcontainers.Redpanda;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// One Redpanda broker for a whole test class instead of one per test (C519 transport and crash classes). The classes
/// that use it run one test at a time (ProcessSpawnLimit), and every scenario has its own topic (with its own size
/// cap, deleted when the scenario is disposed), consumer group, database schema and gateway (UnifiedOutboundTransport),
/// so no test sees another's broker state; a test must not change broker-wide state. Started by the first test that
/// needs it, with the class's own start (its timing and budget); a later test that reuses it prints
/// `<label> shared=true`. Before each reuse the broker must answer a metadata request (Testcontainers' State is cached,
/// not live), allowed 20 seconds and asked twice because a loaded host can be slow: one that still does not answer
/// prints `shared=replaced` and is disposed (bounded; a failed disposal is logged and left to the reaper) and replaced.
/// After two failed starts in a row the remaining tests fail at once with the last start's error instead of each
/// spending its own startup budget. Every step under the gate has its own bound (two 30-second probes, a 60-second
/// disposal, a start the class bounds, at most 120 seconds, whose failed-start disposal it bounds too), so a gate
/// holder always finishes and no caller abandons work it started: a caller waits for the gate at most
/// <see cref="GateWait"/> (more than the longest holder) and then does its own bounded work. A test's own scenario
/// deadline starts after it has the broker. <see cref="StopAsync"/> runs after the class, under the same gate.
/// </summary>
internal sealed class SharedRedpandaBroker(Func<Task<RedpandaContainer>> start, string label)
{
    public static readonly TimeSpan GateWait = TimeSpan.FromSeconds(420);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RedpandaContainer? _broker;
    private int _startFailures;
    private Exception? _startFailure;

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
            if (_startFailures >= 2)
                throw new InvalidOperationException(
                    "the class's shared Redpanda broker failed to start twice in a row; this test did not try again",
                    _startFailure);
            try
            {
                _broker = await start();
            }
            catch (Exception ex)
            {
                _startFailures++;
                _startFailure = ex;
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
}
