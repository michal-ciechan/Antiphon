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
        // PC-173 removes the late callback observer; the late-fault barrier is missed.
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
                    if (!successfulList) throw new OperationCanceledException("controlled second-list deadline");
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
