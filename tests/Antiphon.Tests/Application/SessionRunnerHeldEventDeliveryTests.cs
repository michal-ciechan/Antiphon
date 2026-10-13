using System.Net;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class SessionRunnerHeldEventDeliveryTests
{
    [Test]
    [Timeout(30_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1205_Blocked_held_callback_stops_on_host_cancellation(bool cancelDuringList)
    {
        // PC-115 restores CancellationToken.None plus an unbounded callback await.
        // PC-116 omits the host link. Both leave ExecuteTask pending until our release.
        await using var h = new DeliveryHarness(blockCallback: true);
        await h.Pump.StartAsync(h.Host.Token);
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancelDuringList)
            h.Host.Cancel();
        else
            h.Handler.FailList.TrySetResult();
        await h.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Host.Cancel();

        var stopped = await FinishesAsync(h.Pump.ExecuteTask!);
        stopped.ShouldBeTrue("held callback must not prevent host shutdown before fixture release");
        h.Handler.Stream.Disposed.ShouldBeTrue();
        h.Bus.Release.Task.IsCompleted.ShouldBeFalse();
        h.Bus.Token.CanBeCanceled.ShouldBeTrue();
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(1);
        h.Bus.Attempts.ShouldBe(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1205_Blocked_held_callback_stops_at_monotonic_deadline()
    {
        // PC-117 removes the timer/wait bound; the callback and iterator stay pending.
        await using var h = new DeliveryHarness(blockCallback: true);
        await h.Pump.StartAsync(h.Host.Token);
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.FailList.TrySetResult();
        await h.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Time.Advance(TimeSpan.FromSeconds(3));

        var disconnected = await FinishesAsync(h.Log.Disconnected.Task);
        disconnected.ShouldBeTrue("deadline must dispose the iterator and reach reconnect before fixture release");
        h.Host.IsCancellationRequested.ShouldBeFalse();
        h.Handler.Stream.Disposed.ShouldBeTrue();
        h.Bus.Release.Task.IsCompleted.ShouldBeFalse();
        h.Bus.Token.IsCancellationRequested.ShouldBeTrue();
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(1);
        h.Bus.Attempts.ShouldBe(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1205_Last_transcript_pull_cancellation_preserves_one_output()
    {
        // PC-118 skips the common offer when the host is already cancelled, leaving
        // following-copy suppression unchanged. Accepted.ShouldBe(1) then observes zero.
        // PC-119 links the already-cancelled host before the initial preservation offer.
        await using var h = new DeliveryHarness(blockCallback: false, holdLastPull: true);
        await h.Pump.StartAsync(h.Host.Token);
        await h.Handler.PullEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Host.Cancel();
        await h.Pump.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        h.Bus.Accepted.ShouldBe(1);
        h.Bus.Attempts.ShouldBe(1);
        h.Bus.AcceptedText.ShouldContain("c1205-final-pull-output");
        h.Handler.Stream.Disposed.ShouldBeTrue();
        h.Handler.Pulls.ShouldBe(1);
        h.Handler.Lists.ShouldBe(2);
    }

    [Test]
    [Timeout(30_000)]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task C1205_Catchup_exception_survives_bounded_held_delivery(bool catchupThrows, bool blockCallback)
    {
        // PC-172 removes the finally's captured-exception filter: the original
        // catch-up cancellation type/message is lost only on the blocked throw row.
        // PC-173 waits via Task.WhenAny instead of awaiting the callback: scope cleanup
        // remains, but its late fault is not observed and the log barrier is missed.
        // PC-174 skips the offer on exceptional unwind; the completed control loses output.
        // PC-175 removes bounded delivery on normal unwind; disconnect stays pending.
        await using var h = new DeliveryHarness(blockCallback, successfulList: !catchupThrows);
        await h.Pump.StartAsync(h.Host.Token);
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.FailList.TrySetResult();
        var offered = await FinishesAsync(h.Bus.Entered.Task);
        offered.ShouldBeTrue("exceptional unwind must offer the held output before disposing its iterator");
        if (blockCallback) h.Time.Advance(TimeSpan.FromSeconds(3));
        var disconnected = await FinishesAsync(h.Log.Disconnected.Task);
        disconnected.ShouldBeTrue("bounded preservation must unwind before callback release");

        h.Handler.Stream.Disposed.ShouldBeTrue();
        h.Bus.Attempts.ShouldBe(1);
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(blockCallback ? 1 : 0);
        if (catchupThrows)
        {
            h.Log.DisconnectException!.GetType().ShouldBe(typeof(OperationCanceledException));
            h.Log.DisconnectException.Message.ShouldBe("controlled second-list deadline");
        }
        else
            h.Log.DisconnectException.ShouldBeOfType<TimeoutException>();
        if (!blockCallback)
        {
            h.Bus.Accepted.ShouldBe(1);
            h.Bus.AcceptedText.ShouldContain("c1205-final-pull-output");
        }
        else
        {
            h.Bus.Release.Task.IsCompleted.ShouldBeFalse();
            h.Bus.Accepted.ShouldBe(0);
            h.Bus.Release.TrySetException(new InvalidOperationException("controlled late callback fault"));
            var observed = await FinishesAsync(h.Log.LateFault.Task);
            observed.ShouldBeTrue("the retained callback's late fault must be observed");
            h.Log.LateFaultException.ShouldBeOfType<InvalidOperationException>();
            h.Log.LateFaultException!.Message.ShouldBe("controlled late callback fault");
            await Task.WhenAll(h.ScopeDisposals).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task<bool> FinishesAsync(Task task)
    {
        // Real time is an outer hang guard only. The stimulus is cancellation or fake
        // monotonic time; the unreleased consumer gate has no other completion path.
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); return true; }
        catch (TimeoutException) { return false; }
    }

    [Test]
    [Timeout(30_000)]
    [Arguments(false, false, "async")]
    [Arguments(false, true, "async")]
    [Arguments(true, false, "async")]
    [Arguments(true, true, "async")]
    [Arguments(false, false, "sync")]
    [Arguments(false, true, "sync")]
    [Arguments(true, false, "sync")]
    [Arguments(true, true, "sync")]
    [Arguments(false, false, "host")]
    [Arguments(false, true, "host")]
    [Arguments(true, false, "host")]
    [Arguments(true, true, "host")]
    [Arguments(false, false, "deadline")]
    [Arguments(false, true, "deadline")]
    [Arguments(true, false, "deadline")]
    [Arguments(true, true, "deadline")]
    public async Task C1205_Callback_fault_preserves_original_identity(
        bool catchupThrows, bool callbackCancels, string timing)
    {
        // T54 / PC-176..179: a callback fault is not an abandoned offer. Calling the
        // actual readiness handler directly exposes even OCE under host cancellation,
        // which BackgroundService's outer shutdown catch intentionally consumes.
        await using var h = new DeliveryHarness(blockCallback: true, successfulList: !catchupThrows);
        Exception callbackFault = callbackCancels
            ? new OperationCanceledException("independent callback cancellation", new CancellationToken(true))
            : new InvalidOperationException("controlled callback fault");
        if (timing != "async")
        {
            h.Bus.BeforeReturn = () =>
            {
                if (timing == "host") h.Host.Cancel();
                if (timing == "deadline") h.Time.Advance(TimeSpan.FromSeconds(3));
                throw callbackFault;
            };
        }
        var delivery = h.RunHeldAsync();
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.FailList.TrySetResult();
        await h.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (timing == "async") h.Bus.Release.TrySetException(callbackFault);
        Exception? observed = null;
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { observed = ex; }

        observed.ShouldBeSameAs(catchupThrows ? h.Handler.CatchupFault : callbackFault);
        observed!.StackTrace.ShouldContain(catchupThrows ? nameof(DeliveryHandler)
            : timing == "async" ? nameof(AgentSessionRuntime.ObserveOutputAsync)
            : nameof(C1205_Callback_fault_preserves_original_identity));
        h.Bus.Attempts.ShouldBe(1);
        h.Bus.Accepted.ShouldBe(0);
        h.Handler.Stream.Disposed.ShouldBeTrue();
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(0);
        if (catchupThrows) h.Log.Exceptions.ShouldContain(callbackFault);
        await Task.WhenAll(h.ScopeDisposals).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    [Timeout(30_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1205_Completed_callback_has_no_abandonment(bool catchupThrows)
    {
        // T55 / PC-180: a responsive callback keeps one completed offer.
        await using var h = new DeliveryHarness(blockCallback: false, successfulList: !catchupThrows);
        var delivery = h.RunHeldAsync();
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.FailList.TrySetResult();
        Exception? observed = null;
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { observed = ex; }
        observed.ShouldBeSameAs(catchupThrows ? h.Handler.CatchupFault : null);
        h.Bus.Accepted.ShouldBe(1);
        h.Bus.AcceptedText.ShouldContain("c1205-final-pull-output");
        h.Bus.Attempts.ShouldBe(1);
        h.Handler.Stream.Disposed.ShouldBeTrue();
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(0);
    }

    [Test]
    [Timeout(30_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1205_Deadline_before_late_fault_retains_abandonment(bool catchupThrows)
    {
        // T56 / PC-181: once abandonment is decided a later fault is observed,
        // without replacing the earlier catch-up exception or replaying output.
        await using var h = new DeliveryHarness(blockCallback: true, successfulList: !catchupThrows);
        var delivery = h.RunHeldAsync();
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.FailList.TrySetResult();
        await h.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Exception? observed = null;
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { observed = ex; }
        if (catchupThrows) observed.ShouldBeSameAs(h.Handler.CatchupFault);
        else observed.ShouldBeOfType<TimeoutException>(); // Existing deadline-only reconnect signal.
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(1);
        h.Handler.Stream.Disposed.ShouldBeTrue();
        h.Bus.Attempts.ShouldBe(1);
        h.Bus.Accepted.ShouldBe(0);
        var lateFault = new InvalidOperationException("fault after abandonment decision");
        h.Bus.Release.TrySetException(lateFault);
        await h.Log.LateFault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Log.LateFaultException.ShouldBeSameAs(lateFault);
        await Task.WhenAll(h.ScopeDisposals).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    [Timeout(30_000)]
    [Arguments(false, "host", "invalid", false)]
    [Arguments(false, "host", "independent", true)]
    [Arguments(false, "host", "host", false)]
    [Arguments(false, "host", "delivery", false)]
    [Arguments(false, "host", "aggregate", false)]
    [Arguments(false, "host", "custom", false)]
    [Arguments(false, "host", "none", true)]
    [Arguments(false, "deadline", "invalid", false)]
    [Arguments(false, "deadline", "independent", true)]
    [Arguments(false, "deadline", "host", false)]
    [Arguments(false, "deadline", "delivery", false)]
    [Arguments(false, "deadline", "aggregate", false)]
    [Arguments(false, "deadline", "custom", false)]
    [Arguments(false, "deadline", "none", true)]
    [Arguments(false, "none", "invalid", false)]
    [Arguments(false, "none", "independent", true)]
    [Arguments(false, "none", "host", false)]
    [Arguments(false, "none", "delivery", false)]
    [Arguments(false, "none", "aggregate", false)]
    [Arguments(false, "none", "custom", false)]
    [Arguments(false, "none", "none", true)]
    [Arguments(true, "host", "invalid", true)]
    [Arguments(true, "host", "independent", true)]
    [Arguments(true, "host", "host", true)]
    [Arguments(true, "host", "delivery", true)]
    [Arguments(true, "host", "aggregate", true)]
    [Arguments(true, "host", "custom", true)]
    [Arguments(true, "host", "none", true)]
    [Arguments(true, "deadline", "invalid", true)]
    [Arguments(true, "deadline", "independent", true)]
    [Arguments(true, "deadline", "host", true)]
    [Arguments(true, "deadline", "delivery", true)]
    [Arguments(true, "deadline", "aggregate", true)]
    [Arguments(true, "deadline", "custom", true)]
    [Arguments(true, "deadline", "none", true)]
    [Arguments(true, "none", "invalid", true)]
    [Arguments(true, "none", "independent", true)]
    [Arguments(true, "none", "host", true)]
    [Arguments(true, "none", "delivery", true)]
    [Arguments(true, "none", "aggregate", true)]
    [Arguments(true, "none", "custom", true)]
    [Arguments(true, "none", "none", true)]
    public async Task C1205_Settled_callback_classification_table(
        bool catchupThrows, string timing, string kind, bool expectedCallbackLog)
    {
        // T57 / PC-183..186. Literal expectations cover the 24 Review controls/failures
        // and tokenless/no-cancellation cells. Actual callback and real iterator execute.
        await using var h = new DeliveryHarness(blockCallback: true, successfulList: !catchupThrows);
        Exception? callbackFault = null;
        h.Bus.BeforeReturn = () =>
        {
            if (timing == "host") h.Host.Cancel();
            if (timing == "deadline") h.Time.Advance(TimeSpan.FromSeconds(3));
            callbackFault = CallbackException(kind, h.Bus.Token, h.Host.Token);
            throw callbackFault;
        };
        var delivery = h.RunHeldAsync();
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.FailList.TrySetResult();
        Exception? observed = null;
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { observed = ex; }
        callbackFault.ShouldNotBeNull();
        observed.ShouldBeSameAs(catchupThrows ? h.Handler.CatchupFault : callbackFault);
        observed!.StackTrace.ShouldContain(catchupThrows ? nameof(DeliveryHandler)
            : nameof(C1205_Settled_callback_classification_table));
        h.Log.Exceptions.Contains(callbackFault).ShouldBe(expectedCallbackLog,
            "literal table expectation for original callback warning");
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(0);
        h.Bus.Attempts.ShouldBe(1);
        h.Bus.Accepted.ShouldBe(0);
        h.Handler.Stream.Disposed.ShouldBeTrue();
        await Task.WhenAll(h.ScopeDisposals).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    [Timeout(30_000)]
    [Arguments(false, "host", "invalid", true)]
    [Arguments(false, "host", "independent", true)]
    [Arguments(false, "host", "host", false)]
    [Arguments(false, "host", "delivery", false)]
    [Arguments(false, "host", "aggregate", true)]
    [Arguments(false, "host", "custom", true)]
    [Arguments(false, "host", "none", true)]
    [Arguments(false, "host", "completed", false)]
    [Arguments(false, "deadline", "invalid", true)]
    [Arguments(false, "deadline", "independent", true)]
    [Arguments(false, "deadline", "host", true)]
    [Arguments(false, "deadline", "delivery", true)]
    [Arguments(false, "deadline", "aggregate", true)]
    [Arguments(false, "deadline", "custom", true)]
    [Arguments(false, "deadline", "none", true)]
    [Arguments(false, "deadline", "completed", false)]
    [Arguments(true, "host", "invalid", true)]
    [Arguments(true, "host", "independent", true)]
    [Arguments(true, "host", "host", false)]
    [Arguments(true, "host", "delivery", false)]
    [Arguments(true, "host", "aggregate", true)]
    [Arguments(true, "host", "custom", true)]
    [Arguments(true, "host", "none", true)]
    [Arguments(true, "host", "completed", false)]
    [Arguments(true, "deadline", "invalid", true)]
    [Arguments(true, "deadline", "independent", true)]
    [Arguments(true, "deadline", "host", true)]
    [Arguments(true, "deadline", "delivery", true)]
    [Arguments(true, "deadline", "aggregate", true)]
    [Arguments(true, "deadline", "custom", true)]
    [Arguments(true, "deadline", "none", true)]
    [Arguments(true, "deadline", "completed", false)]
    public async Task C1205_Late_callback_classification_table(
        bool catchupThrows, string timing, string kind, bool expectedCallbackLog)
    {
        // T58 / PC-187..189. Join the observer's scope before checking warnings,
        // so an absent log is deterministic rather than a log-barrier timeout.
        await using var h = new DeliveryHarness(blockCallback: true, successfulList: !catchupThrows);
        var delivery = h.RunHeldAsync();
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.FailList.TrySetResult();
        await h.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (timing == "host") h.Host.Cancel();
        else h.Time.Advance(TimeSpan.FromSeconds(3));
        Exception? observed = null;
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { observed = ex; }
        if (catchupThrows) observed.ShouldBeSameAs(h.Handler.CatchupFault);
        else if (timing == "host") observed.ShouldBeOfType<OperationCanceledException>();
        else observed.ShouldBeOfType<TimeoutException>(); // Unchanged T53 reconnect signal.
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(1);
        h.Handler.Stream.Disposed.ShouldBeTrue();
        h.Bus.Release.Task.IsCompleted.ShouldBeFalse();
        Exception? lateFault = null;
        if (kind == "completed") h.Bus.Release.TrySetResult();
        else
        {
            lateFault = CallbackException(kind, h.Bus.Token, h.Host.Token);
            h.Bus.Release.TrySetException(lateFault);
        }
        await Task.WhenAll(h.ScopeDisposals).WaitAsync(TimeSpan.FromSeconds(5));
        h.Log.Exceptions.Any(ex => ReferenceEquals(ex, lateFault)).ShouldBe(expectedCallbackLog,
            "literal table expectation for original late callback warning");
        if (expectedCallbackLog) h.Log.LateFaultException.ShouldBeSameAs(lateFault);
        h.Bus.Attempts.ShouldBe(1);
        h.Bus.Accepted.ShouldBe(0);
    }

    [Test]
    [Timeout(30_000)]
    [Arguments(false, "host")]
    [Arguments(true, "host")]
    [Arguments(false, "deadline")]
    [Arguments(true, "deadline")]
    public async Task C1205_Completed_callback_wins_cancelled_wait(bool catchupThrows, string timing)
    {
        // T59 / PC-190. Settle successfully before the bound is observed.
        await using var h = new DeliveryHarness(blockCallback: true, successfulList: !catchupThrows);
        h.Bus.BeforeReturn = () =>
        {
            h.Bus.Release.TrySetResult();
            if (timing == "host") h.Host.Cancel();
            else h.Time.Advance(TimeSpan.FromSeconds(3));
        };
        var delivery = h.RunHeldAsync();
        await h.Handler.SecondListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.FailList.TrySetResult();
        Exception? observed = null;
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { observed = ex; }
        observed.ShouldBeSameAs(catchupThrows ? h.Handler.CatchupFault : null);
        h.Log.Lines.Count(line => line.Contains("Held live event delivery abandoned")).ShouldBe(0);
        h.Bus.Attempts.ShouldBe(1);
        h.Handler.Stream.Disposed.ShouldBeTrue();
        await Task.WhenAll(h.ScopeDisposals).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Exception CallbackException(string kind, CancellationToken delivery, CancellationToken host) => kind switch
    {
        "invalid" => new InvalidOperationException("original callback fault"),
        "independent" => new OperationCanceledException("independent callback cancellation", new CancellationToken(true)),
        "host" => new OperationCanceledException("host callback cancellation", host),
        "delivery" => new OperationCanceledException("delivery callback cancellation", delivery),
        "none" => new OperationCanceledException("tokenless callback cancellation"),
        "aggregate" => new AggregateException("aggregate callback fault", new InvalidOperationException("inner")),
        "custom" => new HeldCallbackException("custom callback fault"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class HeldCallbackException(string message) : Exception(message);


    private sealed class DeliveryHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HttpClient _http;
        private readonly TrackedScopeFactory _scopes;
        public CancellationTokenSource Host { get; } = new();
        public FakeTimeProvider Time { get; } = new();
        public DeliveryHandler Handler { get; }
        public DeliveryBus Bus { get; }
        public DeliveryLog Log { get; } = new();
        public Task[] ScopeDisposals => _scopes.Disposals;
        public SessionRunnerEventPump Pump { get; }

        public async Task RunHeldAsync()
        {
            // Real HTTP iterator plus actual production handler/runtime; awaiting the
            // returned Task exposes its original exception without reflection wrapping.
            var client = _services.GetRequiredService<ISessionRunnerClient>();
            await client.ListAsync(Host.Token);
            await foreach (var evt in client.StreamEventsAsync(Host.Token))
            {
                if (!evt.SubscriptionReady) continue;
                var method = typeof(SessionRunnerEventPump).GetMethod("HandleSubscriptionReadyAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                await (Task)method.Invoke(Pump, [evt, client,
                    _services.GetRequiredService<AgentSessionRuntime>(), Host.Token])!;
                break;
            }
        }

        public DeliveryHarness(bool blockCallback, bool holdLastPull = false, bool successfulList = false)
        {
            Handler = new DeliveryHandler(holdLastPull, successfulList);
            Bus = new DeliveryBus(blockCallback);
            _http = new HttpClient(Handler, false) { Timeout = Timeout.InfiniteTimeSpan };
            var settings = Options.Create(new SessionRunnerSettings
            {
                BaseUrl = "http://runner.invalid", ListTimeoutSeconds = 30,
                EventStreamIdleTimeoutSeconds = 600, EventReconnectDelayMs = 60_000,
            });
            var client = new SessionRunnerHttpClient(_http, new SharedFactory(Handler), settings, time: Time);
            var services = new ServiceCollection();
            services.AddSingleton<ISessionRunnerClient>(client);
            services.AddSingleton<IEventBus>(Bus);
            services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<TimeProvider>(Time);
            services.AddLogging();
            services.AddSingleton<AgentSessionRuntime>();
            _services = services.BuildServiceProvider();
            _scopes = new TrackedScopeFactory(_services.GetRequiredService<IServiceScopeFactory>());
            Pump = new SessionRunnerEventPump(_scopes, settings, Log, Time);
        }

        public async ValueTask DisposeAsync()
        {
            // Also joins baseline-red callbacks: no orphaned pump or held HTTP request.
            Bus.Release.TrySetResult();
            Host.Cancel();
            await Pump.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            // The bounded wait may end before a non-cooperative callback. Join its retained
            // scope after releasing it, before disposing the root provider (also on red).
            await Task.WhenAll(_scopes.Disposals).WaitAsync(TimeSpan.FromSeconds(5));
            Pump.Dispose();
            _http.Dispose();
            Handler.Dispose();
            await _services.DisposeAsync();
            Host.Dispose();
        }
    }

    private sealed class TrackedScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private readonly List<Task> _disposals = [];
        public Task[] Disposals { get { lock (_disposals) return _disposals.ToArray(); } }
        public IServiceScope CreateScope()
        {
            var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_disposals) _disposals.Add(disposed.Task);
            return new TrackedScope(inner.CreateScope(), disposed);
        }

        private sealed class TrackedScope(IServiceScope inner, TaskCompletionSource disposed) : IServiceScope, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider => inner.ServiceProvider;
            public void Dispose() { inner.Dispose(); disposed.TrySetResult(); }
            public async ValueTask DisposeAsync()
            {
                if (inner is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
                else inner.Dispose();
                disposed.TrySetResult();
            }
        }
    }

    private sealed class SharedFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class DeliveryHandler(bool holdLastPull, bool successfulList) : HttpMessageHandler
    {
        public OperationCanceledException CatchupFault { get; } = new("controlled second-list deadline");
        public int Lists, Pulls;
        public TaskCompletionSource SecondListEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FailList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PullEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TrackedStream Stream { get; } = new(Encoding.UTF8.GetBytes(
            "event: SessionOutput\ndata: {\"sessionId\":\"12051205-1205-4120-8120-120512051205\",\"sequence\":4,\"text\":\"c1205-final-pull-output\"}\n\n"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/sessions")
            {
                var call = Interlocked.Increment(ref Lists);
                if (call == 2 && !holdLastPull)
                {
                    SecondListEntered.TrySetResult();
                    await FailList.Task.WaitAsync(ct);
                    // Precisely the list deadline exception, without a real timer or sleep.
                    if (!successfulList) throw CatchupFault;
                }
                var body = call == 1 || successfulList ? "[]" : "[{\"sessionId\":\"12051205-1205-4120-8120-120512051205\",\"pid\":1,\"startedAt\":\"2026-10-12T00:00:00Z\",\"status\":\"Running\",\"lastSequence\":4,\"exitReason\":\"Unknown\"}]";
                return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
            if (path.EndsWith("/transcript", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Pulls);
                PullEntered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("held pull unexpectedly completed");
            }
            if (path == "/events")
                return new(HttpStatusCode.OK) { Content = new StreamContent(Stream) };
            throw new InvalidOperationException("Unexpected request " + path);
        }
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class DeliveryBus(bool blockCallback) : IEventBus
    {
        public Action? BeforeReturn { get; set; }
        public int Attempts, Accepted;
        public string AcceptedText { get; private set; } = "";
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
        {
            if (eventName != "AgentTextDelta") return Task.CompletedTask;
            Interlocked.Increment(ref Attempts);
            Token = ct;
            Entered.TrySetResult();
            BeforeReturn?.Invoke();
            if (blockCallback) return Release.Task; // Deliberately ignores cancellation.
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Accepted);
            AcceptedText = System.Text.Json.JsonSerializer.Serialize(payload);
            return Task.CompletedTask;
        }

        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class DeliveryLog : ILogger<SessionRunnerEventPump>
    {
        public ConcurrentQueue<Exception> Exceptions { get; } = new();
        public ConcurrentQueue<string> Lines { get; } = new();
        public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LateFault { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? DisconnectException { get; private set; }
        public Exception? LateFaultException { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (level < LogLevel.Warning) return;
            var text = formatter(state, exception);
            Lines.Enqueue(text + " " + exception?.GetType().Name);
            if (exception is not null) Exceptions.Enqueue(exception);
            if (text.Contains("event stream disconnected", StringComparison.Ordinal))
            {
                DisconnectException = exception;
                Disconnected.TrySetResult();
            }
            if (text.Contains("Abandoned held-event callback later failed", StringComparison.Ordinal))
            {
                LateFaultException = exception;
                LateFault.TrySetResult();
            }
        }
    }

}
