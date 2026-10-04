using System.Net.WebSockets;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Antiphon.Tests.TestHelpers;

internal enum PhoneHomeReceiveMode { Fault, Cancellation, ObserveOnly }

/// <summary>Controls one receive on a real upgraded connection, without a competing native read.</summary>
internal sealed class PhoneHomeReceiveControl(PhoneHomeReceiveMode mode) : IAsyncDisposable
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _receiveFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<WebSocketException> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _requestCancellation = new();
    private CancellationToken _originalToken;
    private CancellationToken _effectiveToken;
    private CancellationToken _stoppingToken;
    private WebSocket? _socket;
    private bool _started;

    public Exception? EndpointFault { get; private set; }
    public Exception? PipelineFault { get; private set; }
    public WebSocketException? ObservedReceiveException { get; private set; }
    public OperationCanceledException? ObservedReceiveCancellation { get; private set; }
    public bool OriginalCanceledAtInjection { get; private set; }
    public bool EffectiveCanceledAtInjection { get; private set; }
    public bool HostStoppingAtInjection { get; private set; }
    public WebSocketState? SocketStateAtInjection { get; private set; }

    public Task WaitForReceiveAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    public Task WaitForCompletionAsync() =>
        _completed.Task.WaitAsync(TimeSpan.FromSeconds(PhoneHomeProtocol.CloseHandshakeSeconds + 2));

    public async Task ObservePipelineAsync(HttpContext context, RequestDelegate next)
    {
        if (_started)
            throw new InvalidOperationException("A receive control belongs to one connect request.");
        _started = true;
        try { await next(context); }
        catch (Exception ex)
        {
            PipelineFault = ex;
            throw;
        }
        finally { _completed.TrySetResult(); }
    }

    public async Task InvokeEndpointAsync(HttpContext context, RequestDelegate next, CancellationToken stoppingToken)
    {
        var feature = context.Features.Get<IHttpWebSocketFeature>()
            ?? throw new InvalidOperationException("The WebSocket middleware must run first.");
        _originalToken = context.RequestAborted;
        _stoppingToken = stoppingToken;
        using var linked = mode == PhoneHomeReceiveMode.Cancellation
            ? CancellationTokenSource.CreateLinkedTokenSource(_originalToken, _requestCancellation.Token)
            : null;
        try
        {
            if (linked is not null)
                context.RequestAborted = linked.Token;
            if (mode != PhoneHomeReceiveMode.ObserveOnly)
                context.Features.Set<IHttpWebSocketFeature>(new ControlledFeature(feature, this));
            await next(context);
        }
        catch (Exception ex)
        {
            EndpointFault = ex;
            throw;
        }
        finally
        {
            context.Features.Set(feature);
            context.RequestAborted = _originalToken;
        }
    }

    public void ThrowOnReceive(WebSocketException exception)
    {
        if (mode != PhoneHomeReceiveMode.Fault)
            throw new InvalidOperationException("Fault injection requires fault mode.");
        CaptureInjection();
        if (!_release.TrySetResult(exception))
            throw new InvalidOperationException("Receive was already released.");
    }

    public void CancelRequest()
    {
        if (mode != PhoneHomeReceiveMode.Cancellation)
            throw new InvalidOperationException("Request cancellation requires cancellation mode.");
        CaptureInjection();
        _requestCancellation.Cancel();
    }

    private void CaptureInjection()
    {
        if (!_entered.Task.IsCompletedSuccessfully)
            throw new InvalidOperationException("Wait for receive entry before injection.");
        OriginalCanceledAtInjection = _originalToken.IsCancellationRequested;
        EffectiveCanceledAtInjection = _effectiveToken.IsCancellationRequested;
        HostStoppingAtInjection = _stoppingToken.IsCancellationRequested;
        SocketStateAtInjection = _socket?.State;
    }

    private async Task<WebSocketReceiveResult> ReceiveAsync(CancellationToken ct)
    {
        _effectiveToken = ct;
        _entered.TrySetResult();
        try
        {
            var exception = await _release.Task.WaitAsync(ct);
            ObservedReceiveException = exception;
            throw exception;
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            ObservedReceiveCancellation = ex;
            throw;
        }
        finally { _receiveFinished.TrySetResult(); }
    }

    // Host teardown invokes this before disposing Kestrel, including when an assertion failed.
    public async Task CancelAndWaitAsync()
    {
        _requestCancellation.Cancel();
        _release.TrySetCanceled();
        if (_started)
            await WaitForCompletionAsync();
        if (_entered.Task.IsCompleted)
            await _receiveFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    public async ValueTask DisposeAsync()
    {
        try { await CancelAndWaitAsync(); }
        finally { _requestCancellation.Dispose(); }
    }

    private sealed class ControlledFeature(IHttpWebSocketFeature inner, PhoneHomeReceiveControl control)
        : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => inner.IsWebSocketRequest;
        public async Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
        {
            var socket = await inner.AcceptAsync(context);
            control._socket = socket;
            return new ControlledSocket(socket, control);
        }
    }

    private sealed class ControlledSocket(WebSocket inner, PhoneHomeReceiveControl control) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
        public override string? CloseStatusDescription => inner.CloseStatusDescription;
        public override WebSocketState State => inner.State;
        public override string? SubProtocol => inner.SubProtocol;
        public override void Abort() => inner.Abort();
        public override void Dispose() => inner.Dispose();
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            inner.CloseAsync(status, description, ct);
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            inner.CloseOutputAsync(status, description, ct);
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) =>
            inner.SendAsync(buffer, type, endOfMessage, ct);
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) =>
            control.ReceiveAsync(ct);
        public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
        {
            var result = await control.ReceiveAsync(ct);
            return new ValueWebSocketReceiveResult(result.Count, result.MessageType, result.EndOfMessage);
        }
    }
}
