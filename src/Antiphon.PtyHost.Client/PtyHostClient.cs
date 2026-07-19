using System.IO.Pipes;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.PtyHost.Client;

/// <summary>
/// The runner's connection to one pty-host. Owns the pipe, a single read loop routing frames to
/// either the one in-flight request or the output/exit events, and a write gate. Requests are
/// single-flight by design - the protocol has no correlation ids because the runner never has a
/// reason to overlap Launch/Attach/Status calls on one session.
/// </summary>
public sealed class PtyHostClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readLoop;
    private TaskCompletionSource<PtyHostMessage>? _pendingReply;

    /// <summary>Live output chunk with its host-assigned sequence number.</summary>
    public event Action<long, string>? OnOutput;

    public event Action<ExitedMessage>? OnExited;

    /// <summary>Pipe dropped (host died or runner is being torn down). Null exception = clean EOF.</summary>
    public event Action<Exception?>? OnDisconnected;

    public HelloAckMessage Hello { get; }

    private PtyHostClient(NamedPipeClientStream pipe, HelloAckMessage hello)
    {
        _pipe = pipe;
        Hello = hello;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Connects (with retry while the host's pipe server comes up) and completes the hello.</summary>
    public static async Task<PtyHostClient> ConnectAsync(
        string pipeName, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(2));
                    await pipe.ConnectAsync(connectCts.Token);
                }

                await PtyHostFraming.WriteAsync(pipe, new HelloMessage(PtyHostProtocol.Version), ct);
                var reply = await PtyHostFraming.ReadAsync(pipe, ct);
                if (reply is not HelloAckMessage hello)
                    throw new InvalidOperationException($"Expected helloAck, got {reply?.GetType().Name ?? "EOF"}.");

                return new PtyHostClient(pipe, hello);
            }
            catch (Exception ex) when (
                ex is IOException or TimeoutException
                || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                last = ex;
                await pipe.DisposeAsync();
                await Task.Delay(150, ct);
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
        }

        throw new TimeoutException($"Could not connect to pty-host pipe '{pipeName}' within {timeout}.", last);
    }

    public async Task<LaunchedMessage> LaunchAsync(LaunchMessage launch, CancellationToken ct)
    {
        var reply = await RequestAsync(launch, ct);
        return reply switch
        {
            LaunchedMessage launched => launched,
            ErrorMessage error => throw new InvalidOperationException($"Host launch failed ({error.Code}): {error.Message}"),
            _ => throw new InvalidOperationException($"Unexpected launch reply {reply.GetType().Name}."),
        };
    }

    /// <summary>
    /// Attaches at <paramref name="lastSeq"/>. Returns null on success (replay + live output flow
    /// via <see cref="OnOutput"/>), or the <see cref="ResyncMessage"/> when the requested point has
    /// fallen out of the host's ring - rebuild from the ansi log, then re-attach at the resync seq.
    /// </summary>
    public async Task<ResyncMessage?> AttachAsync(long lastSeq, CancellationToken ct)
    {
        var reply = await RequestAsync(new AttachMessage(lastSeq), ct);
        return reply switch
        {
            AttachedMessage => null,
            ResyncMessage resync => resync,
            ErrorMessage error => throw new InvalidOperationException($"Host attach failed ({error.Code}): {error.Message}"),
            _ => throw new InvalidOperationException($"Unexpected attach reply {reply.GetType().Name}."),
        };
    }

    public async Task<StatusReplyMessage> GetStatusAsync(CancellationToken ct)
    {
        var reply = await RequestAsync(new StatusRequestMessage(), ct);
        return reply switch
        {
            StatusReplyMessage status => status,
            ErrorMessage error => throw new InvalidOperationException($"Host status failed ({error.Code}): {error.Message}"),
            _ => throw new InvalidOperationException($"Unexpected status reply {reply.GetType().Name}."),
        };
    }

    public Task InputAsync(string data, CancellationToken ct) => SendAsync(new InputMessage(data), ct);

    public Task SendLineAsync(string line, CancellationToken ct) => SendAsync(new SendLineMessage(line), ct);

    public Task ResizeAsync(int cols, int rows, CancellationToken ct) => SendAsync(new ResizeMessage(cols, rows), ct);

    public Task KillAsync(TimeSpan timeout, CancellationToken ct) =>
        SendAsync(new KillMessage((int)timeout.TotalMilliseconds), ct);

    public Task ClearLiveBufferAsync(CancellationToken ct) => SendAsync(new ClearLiveBufferMessage(), ct);

    /// <summary>Final ack: host deletes its manifest and exits. The pipe drops right after.</summary>
    public Task ShutdownAsync(CancellationToken ct) => SendAsync(new ShutdownMessage(), ct);

    private async Task<PtyHostMessage> RequestAsync(PtyHostMessage request, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<PtyHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _pendingReply, tcs);
            await SendAsync(request, ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            Volatile.Write(ref _pendingReply, null);
            _requestGate.Release();
        }
    }

    private async Task SendAsync(PtyHostMessage message, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            await PtyHostFraming.WriteAsync(_pipe, message, ct);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await PtyHostFraming.ReadAsync(_pipe, _lifetime.Token);
                if (message is null)
                    break;

                switch (message)
                {
                    case OutputMessage output:
                        OnOutput?.Invoke(output.Seq, output.Chunk);
                        break;

                    case ExitedMessage exited:
                        OnExited?.Invoke(exited);
                        break;

                    case LaunchedMessage or AttachedMessage or ResyncMessage or StatusReplyMessage:
                        Volatile.Read(ref _pendingReply)?.TrySetResult(message);
                        break;

                    case ErrorMessage error:
                        var pending = Volatile.Read(ref _pendingReply);
                        if (pending is null || !pending.TrySetResult(message))
                        {
                            // Unsolicited error (e.g. rejected fire-and-forget input) - nothing to
                            // fail; the session state machine will observe consequences if any.
                        }

                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Volatile.Read(ref _pendingReply)?.TrySetException(
            failure ?? new EndOfStreamException("pty-host pipe closed."));
        OnDisconnected?.Invoke(failure);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _pipe.DisposeAsync();
        try
        {
            await _readLoop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Read loop teardown is best-effort.
        }

        _lifetime.Dispose();
        _writeGate.Dispose();
        _requestGate.Dispose();
    }
}
