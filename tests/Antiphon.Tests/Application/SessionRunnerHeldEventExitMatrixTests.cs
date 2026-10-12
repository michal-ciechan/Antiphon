using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class SessionRunnerHeldEventExitMatrixTests
{
    [Test]
    [Timeout(30_000)]
    [Arguments("success", "none", false, 1, 0)]
    [Arguments("success", "none", true, 0, 1)]
    [Arguments("success", "before-list", false, 1, 0)]
    [Arguments("success", "before-list", true, 0, 1)]
    [Arguments("success", "during-list", false, 1, 0)]
    [Arguments("success", "during-list", true, 0, 1)]
    [Arguments("success", "error-handler", false, 1, 0)]
    [Arguments("success", "error-handler", true, 0, 1)]
    [Arguments("success", "final-pull", false, 1, 0)]
    [Arguments("success", "final-pull", true, 0, 1)]
    [Arguments("success", "callback", false, 1, 0)]
    [Arguments("success", "callback", true, 0, 1)]
    [Arguments("timeout", "none", false, 1, 0)]
    [Arguments("timeout", "none", true, 0, 1)]
    [Arguments("timeout", "before-list", false, 1, 0)]
    [Arguments("timeout", "before-list", true, 0, 1)]
    [Arguments("timeout", "during-list", false, 1, 0)]
    [Arguments("timeout", "during-list", true, 0, 1)]
    [Arguments("timeout", "error-handler", false, 1, 0)]
    [Arguments("timeout", "error-handler", true, 0, 1)]
    [Arguments("timeout", "final-pull", false, 1, 0)]
    [Arguments("timeout", "final-pull", true, 0, 1)]
    [Arguments("timeout", "callback", false, 1, 0)]
    [Arguments("timeout", "callback", true, 0, 1)]
    [Arguments("throws", "none", false, 1, 0)]
    [Arguments("throws", "none", true, 0, 1)]
    [Arguments("throws", "before-list", false, 1, 0)]
    [Arguments("throws", "before-list", true, 0, 1)]
    [Arguments("throws", "during-list", false, 1, 0)]
    [Arguments("throws", "during-list", true, 0, 1)]
    [Arguments("throws", "error-handler", false, 1, 0)]
    [Arguments("throws", "error-handler", true, 0, 1)]
    [Arguments("throws", "final-pull", false, 1, 0)]
    [Arguments("throws", "final-pull", true, 0, 1)]
    [Arguments("throws", "callback", false, 1, 0)]
    [Arguments("throws", "callback", true, 0, 1)]
    [Arguments("never", "none", false, 1, 0)]
    [Arguments("never", "none", true, 0, 1)]
    [Arguments("never", "before-list", false, 1, 0)]
    [Arguments("never", "before-list", true, 0, 1)]
    [Arguments("never", "during-list", false, 1, 0)]
    [Arguments("never", "during-list", true, 0, 1)]
    [Arguments("never", "error-handler", false, 1, 0)]
    [Arguments("never", "error-handler", true, 0, 1)]
    [Arguments("never", "final-pull", false, 1, 0)]
    [Arguments("never", "final-pull", true, 0, 1)]
    [Arguments("never", "callback", false, 1, 0)]
    [Arguments("never", "callback", true, 0, 1)]
    public async Task C1205_Every_held_exit_has_an_explicit_outcome(
        string list, string cancellation, bool blocked, int expectedAccepted, int expectedAbandoned)
    {
        // PC-120..PC-167 bind these 48 literal outcome rows in declaration order.
        // Responsive: drop the common offer while still suppressing its copy (silent loss).
        // Blocked: bypass bounded preservation for this exit (no disposal/warning).
        // PC-170 removes following-copy suppression. PC-171 removes the list wait bound.
        await using var h = new Harness(list, cancellation, blocked);
        await h.Pump.StartAsync(h.Host.Token);
        await h.Handler.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancellation == "during-list") h.Host.Cancel();
        h.Handler.ListRelease.TrySetResult();

        if (list == "never" && !h.Host.IsCancellationRequested)
        {
            // Handler ignores cancellation AND never returns until fixture teardown.
            // Advance only after the pump's list deadline timer is registered.
            await FinishesAsync(h.Clock.ListTimerRegistered.Task);
            h.Clock.Advance(TimeSpan.FromSeconds(3));
        }

        var offered = await FinishesAsync(h.Bus.Entered.Task);
        offered.ShouldBeTrue("every held exit must offer the original output");
        if (blocked && offered)
        {
            // Deadline registration precedes invocation; there is no scheduler sleep.
            h.Clock.Advance(TimeSpan.FromSeconds(3));
            var disposed = await FinishesAsync(h.Handler.Stream.Disposed.Task);
            disposed.ShouldBeTrue("blocked delivery must unwind the iterator before fixture release");
            h.Bus.Release.Task.IsCompleted.ShouldBeFalse();
            h.Bus.Token.IsCancellationRequested.ShouldBeTrue();
        }
        else
        {
            // Wait for the post-payload read (or disposal), so a duplicated next yield
            // cannot hide behind cancellation immediately after the first acceptance.
            await Task.WhenAny(h.Handler.Stream.NextRead.Task, h.Handler.Stream.Disposed.Task)
                .WaitAsync(TimeSpan.FromSeconds(5));
            h.Host.Cancel();
            await h.Pump.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }

        h.Bus.Accepted.ShouldBe(expectedAccepted);
        h.Log.Lines.Count(s => s.Contains("Held live event delivery abandoned")).ShouldBe(expectedAbandoned);
        h.Bus.Attempts.ShouldBe(1);
        h.Handler.Stream.Disposed.Task.IsCompleted.ShouldBeTrue();
        if (!blocked) h.Bus.Text.ShouldContain("c1205-matrix-original");
        h.Host.Cancel();
        await h.Pump.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        h.Pump.ExecuteTask!.IsCompleted.ShouldBeTrue();
    }

    [Test]
    [Timeout(30_000)]
    [Arguments("read-fault")]
    [Arguments("dispose-fault")]
    public async Task C1205_Iterator_failure_after_preservation_does_not_redeliver(string fault)
    {
        // PC-168/169 drop the offer/copy and defer dispatch until a later ordinary event;
        // the next read/disposal faults before that event, leaving Accepted at zero.
        await using var h = new Harness("success", "none", false, fault);
        await h.Pump.StartAsync(h.Host.Token);
        await h.Handler.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Handler.ListRelease.TrySetResult();
        await h.Log.Disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Bus.Accepted.ShouldBe(1);
        h.Bus.Attempts.ShouldBe(1);
        h.Handler.Stream.Disposed.Task.IsCompleted.ShouldBeTrue();
        h.Log.Lines.Count(s => s.Contains("Held live event delivery abandoned")).ShouldBe(0);
    }

    private static async Task<bool> FinishesAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); return true; }
        catch (TimeoutException) { return false; }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public CancellationTokenSource Host { get; } = new();
        public MatrixClock Clock { get; } = new();
        public Handler Handler { get; }
        public Bus Bus { get; }
        public MatrixLog Log { get; }
        public SessionRunnerEventPump Pump { get; }
        private readonly ServiceProvider _services;
        private readonly HttpClient _http;
        private readonly Scopes _scopes;

        public Harness(string list, string cancellation, bool blocked, string? fault = null)
        {
            Handler = new Handler(list, cancellation, Host, fault);
            Bus = new Bus(blocked, cancellation, Host);
            Log = new MatrixLog(list, cancellation, Host);
            _http = new HttpClient(Handler, false) { Timeout = Timeout.InfiniteTimeSpan };
            var settings = Options.Create(new SessionRunnerSettings
            {
                BaseUrl = "http://runner.invalid", ListTimeoutSeconds = 3,
                EventStreamIdleTimeoutSeconds = 600, EventReconnectDelayMs = 60_000,
            });
            var services = new ServiceCollection();
            services.AddSingleton<ISessionRunnerClient>(new SessionRunnerHttpClient(
                _http, new Factory(Handler), settings, time: Clock));
            services.AddSingleton<IEventBus>(Bus);
            services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<TimeProvider>(Clock);
            services.AddLogging();
            services.AddSingleton<AgentSessionRuntime>();
            _services = services.BuildServiceProvider();
            _scopes = new Scopes(_services.GetRequiredService<IServiceScopeFactory>());
            Pump = new SessionRunnerEventPump(_scopes, settings, Log, Clock);
        }

        public async ValueTask DisposeAsync()
        {
            Bus.Release.TrySetResult();
            Handler.ListRelease.TrySetResult();
            Handler.NeverRelease.TrySetResult();
            Host.Cancel();
            await Pump.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(_scopes.Disposals).WaitAsync(TimeSpan.FromSeconds(5));
            await Handler.RequestFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Pump.Dispose();
            _http.Dispose();
            Handler.Dispose();
            await _services.DisposeAsync();
            Host.Dispose();
        }
    }

    private sealed class MatrixClock : FakeTimeProvider
    {
        public TaskCompletionSource ListTimerRegistered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            // Real HTTP idle timers do not use this provider. The first 3s timer is
            // the subscription list bound, created before the never-returning call.
            if (dueTime == TimeSpan.FromSeconds(3)) ListTimerRegistered.TrySetResult();
            return timer;
        }
    }

    private sealed class Scopes(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private readonly ConcurrentBag<Task> _disposals = [];
        public Task[] Disposals => _disposals.ToArray();
        public IServiceScope CreateScope()
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposals.Add(done.Task);
            return new Scope(inner.CreateScope(), done);
        }
        private sealed class Scope(IServiceScope inner, TaskCompletionSource done) : IServiceScope, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider => inner.ServiceProvider;
            public void Dispose() { inner.Dispose(); done.TrySetResult(); }
            public async ValueTask DisposeAsync()
            {
                if (inner is IAsyncDisposable a) await a.DisposeAsync(); else inner.Dispose();
                done.TrySetResult();
            }
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class Handler(string list, string cancellation, CancellationTokenSource host, string? fault)
        : HttpMessageHandler
    {
        private int _lists;
        public TaskCompletionSource ListEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ListRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource NeverRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RequestFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MatrixStream Stream { get; } = new(fault);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/events") return new(HttpStatusCode.OK) { Content = new StreamContent(Stream) };
            if (path.EndsWith("/transcript", StringComparison.Ordinal))
            {
                // Only successful lists can enter a final pull. All other final-pull
                // cells inject at catch-up exit instead (see Log), while still held.
                if (cancellation == "final-pull") host.Cancel();
                ct.ThrowIfCancellationRequested();
                return Json("{\"entries\":[],\"lastSequence\":0}");
            }
            if (path != "/sessions") throw new InvalidOperationException(path);
            if (Interlocked.Increment(ref _lists) == 1) return Json("[]");
            ListEntered.TrySetResult();
            try
            {
                // Controlled outcome is independent of host cancellation. In particular
                // throws + error-handler observes an ordinary caught fault, not OCE.
                await ListRelease.Task;
                if (list == "never") await NeverRelease.Task;
                if (list == "throws") throw new HttpRequestException("controlled list fault");
                if (list == "timeout") throw new OperationCanceledException("controlled list deadline");
                return Json("[{\"sessionId\":\"12051205-1205-4120-8120-120512051205\",\"pid\":1,\"startedAt\":\"2026-10-12T00:00:00Z\",\"status\":\"Running\",\"lastSequence\":4,\"exitReason\":\"Unknown\"}]");
            }
            finally { RequestFinished.TrySetResult(); }
        }
        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class MatrixStream(string? fault) : Stream
    {
        private readonly byte[] _frame = Encoding.UTF8.GetBytes(
            "event: SessionOutput\ndata: {\"sessionId\":\"12051205-1205-4120-8120-120512051205\",\"sequence\":4,\"text\":\"c1205-matrix-original\"}\n\n");
        private int _offset;
        public TaskCompletionSource NextRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_offset < _frame.Length)
            {
                var n = Math.Min(buffer.Length, _frame.Length - _offset);
                _frame.AsMemory(_offset, n).CopyTo(buffer);
                _offset += n;
                return n;
            }
            NextRead.TrySetResult();
            if (fault == "read-fault") throw new IOException("controlled iterator fault");
            if (fault == "dispose-fault") return 0;
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        protected override void Dispose(bool disposing)
        {
            Disposed.TrySetResult();
            base.Dispose(disposing);
            if (fault == "dispose-fault") throw new IOException("controlled disposal fault");
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long l) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class Bus(bool blocked, string cancellation, CancellationTokenSource host) : IEventBus
    {
        public int Attempts, Accepted;
        public string Text { get; private set; } = "";
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
        {
            if (eventName != "AgentTextDelta") return Task.CompletedTask;
            Interlocked.Increment(ref Attempts);
            Token = ct;
            Entered.TrySetResult();
            if (blocked)
            {
                if (cancellation == "callback") host.Cancel();
                return Release.Task; // Deliberately non-cooperative, joined at teardown.
            }
            ct.ThrowIfCancellationRequested();
            Text = System.Text.Json.JsonSerializer.Serialize(payload);
            Interlocked.Increment(ref Accepted);
            if (cancellation == "callback") host.Cancel();
            return Task.CompletedTask;
        }
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MatrixLog(string list, string cancellation, CancellationTokenSource host)
        : ILogger<SessionRunnerEventPump>
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> format)
        {
            var text = format(state, ex);
            Lines.Enqueue(text);
            if (cancellation == "before-list" && text.Contains("pump.subscription-catch-up start"))
                host.Cancel();
            if (cancellation == "error-handler" && text.Contains("Subscription catch-up failed"))
                host.Cancel(); // F1: caught ordinary list error, before its return.
            // Nonexistent phase cells use the last catch-up boundary, rather than
            // pretending failed lists performed a transcript pull or an error handler.
            if (text.Contains("pump.subscription-catch-up end") &&
                ((cancellation == "error-handler" && list == "success") ||
                 (cancellation == "final-pull" && list != "success")))
                host.Cancel();
            if (text.Contains("event stream disconnected")) Disconnected.TrySetResult();
        }
    }
}
