using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner.Tests.TestHelpers;

/// <summary>
/// CARD-0631: a scripted, channel-driven WebSocket for the phone-home receive pump and writer.
/// Incoming frames are real serialized <see cref="PhoneHomeFrame"/>s; every completed send is
/// decoded back into a frame. Like a real socket it allows one send at a time: a second send
/// that starts while another is still in progress is counted as an overlap and throws, so a
/// missing send gate is detected deterministically rather than by race probability.
/// </summary>
internal sealed class PhoneHomeTestWebSocket : WebSocket
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly Lock _sync = new();
    private readonly List<PhoneHomeFrame> _sent = [];
    private readonly Queue<SendHold> _holds = new();
    private Exception? _failNextSend;
    private Exception? _failNextReceive;
    private bool _emitClose;
    private WebSocketCloseStatus? _closeStatus;
    private string? _closeDescription;
    private byte[]? _partial;
    private int _partialOffset;
    private int _activeSends;
    private WebSocketState _state = WebSocketState.Open;

    public int Overlaps { get; private set; }
    public int PeakConcurrentSends { get; private set; }

    public IReadOnlyList<PhoneHomeFrame> Sent
    {
        get { lock (_sync) return _sent.ToList(); }
    }

    public void Enqueue(PhoneHomeFrame frame) =>
        _incoming.Writer.TryWrite(JsonSerializer.SerializeToUtf8Bytes(frame, PhoneHomeFraming.Json));

    /// <summary>The peer closes: the pending/next receive returns a Close message.</summary>
    public void CompleteIncoming() => _incoming.Writer.TryComplete();

    /// <summary>
    /// CARD-0716 D-4: the peer sends a close frame. The next receive returns Close and
    /// <see cref="CloseStatus"/> names the status the runner's ended line must log.
    /// State stays Open so a heartbeat send in flight is not turned into the ending fault.
    /// </summary>
    public void EnqueueClose(WebSocketCloseStatus status, string? description)
    {
        lock (_sync)
        {
            _closeStatus = status;
            _closeDescription = description;
            _emitClose = true;
        }

        _incoming.Writer.TryWrite([]);
    }

    /// <summary>CARD-0716 D-4: the next receive throws <paramref name="exception"/>.</summary>
    public void FailNextReceive(Exception exception)
    {
        lock (_sync) _failNextReceive = exception;
        _incoming.Writer.TryWrite([]);
    }

    /// <summary>Hold the next send inside SendAsync until the returned hold is released.</summary>
    public SendHold HoldNextSend()
    {
        var hold = new SendHold();
        lock (_sync) _holds.Enqueue(hold);
        return hold;
    }

    public void FailNextSend(Exception ex)
    {
        lock (_sync) _failNextSend = ex;
    }

    public override WebSocketCloseStatus? CloseStatus
    {
        get { lock (_sync) return _closeStatus; }
    }

    public override string? CloseStatusDescription
    {
        get { lock (_sync) return _closeDescription; }
    }
    public override string? SubProtocol => null;
    public override WebSocketState State
    {
        get { lock (_sync) return _state; }
    }

    public override void Abort()
    {
        lock (_sync) _state = WebSocketState.Aborted;
        _incoming.Writer.TryComplete(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
    }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        lock (_sync) _state = WebSocketState.Closed;
        _incoming.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        CloseAsync(closeStatus, statusDescription, cancellationToken);

    public override void Dispose()
    {
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (_partial is null)
        {
            if (!await _incoming.Reader.WaitToReadAsync(cancellationToken)
                || !_incoming.Reader.TryRead(out var message))
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);

            Exception? fail;
            var emitClose = false;
            lock (_sync)
            {
                fail = _failNextReceive;
                _failNextReceive = null;
                if (_emitClose)
                {
                    emitClose = true;
                    _emitClose = false;
                }
            }

            if (fail is not null)
                throw fail;
            if (emitClose)
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);

            _partial = message;
            _partialOffset = 0;
        }

        var count = Math.Min(buffer.Count, _partial.Length - _partialOffset);
        Array.Copy(_partial, _partialOffset, buffer.Array!, buffer.Offset, count);
        _partialOffset += count;
        var end = _partialOffset == _partial.Length;
        if (end)
            _partial = null;
        return new WebSocketReceiveResult(count, WebSocketMessageType.Text, end);
    }

    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        SendHold? hold;
        Exception? fail;
        lock (_sync)
        {
            _activeSends++;
            PeakConcurrentSends = Math.Max(PeakConcurrentSends, _activeSends);
            if (_activeSends > 1)
            {
                Overlaps++;
                _activeSends--;
                throw new InvalidOperationException("There is already one outstanding 'SendAsync' call for this WebSocket instance.");
            }

            hold = _holds.Count > 0 ? _holds.Dequeue() : null;
            fail = _failNextSend;
            _failNextSend = null;
        }

        try
        {
            if (hold is not null)
            {
                hold.Entered.TrySetResult();
                await hold.Release.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (fail is not null)
                throw fail;
            if (State != WebSocketState.Open)
                throw new WebSocketException(WebSocketError.InvalidState);

            var frame = JsonSerializer.Deserialize<PhoneHomeFrame>(buffer.AsSpan(), PhoneHomeFraming.Json)
                ?? throw new InvalidOperationException("Sent an empty frame.");
            lock (_sync) _sent.Add(frame);
        }
        finally
        {
            lock (_sync) _activeSends--;
        }
    }

    internal sealed class SendHold
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
