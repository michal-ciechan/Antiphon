using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;

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

    /// <summary>
    /// Connects (with retry while the host's pipe server comes up) and completes the hello. With a
    /// <paramref name="policy"/> (CARD-1653 S2) each attempt first checks the socket file, then reads
    /// the kernel's peer credentials, and writes the hello only to the expected host. Under Enforce
    /// a failed check throws <see cref="PtyHostPeerRefusedException"/>, which is not retried; under
    /// Observe it is reported once and the connect continues. A null policy is the unchecked connect.
    /// </summary>
    public static async Task<PtyHostClient> ConnectAsync(
        string pipeName, TimeSpan timeout, CancellationToken ct, PtyHostConnectPolicy? policy = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        var observed = false;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = deadline - DateTime.UtcNow;
            if (remaining < TimeSpan.FromMilliseconds(50))
                break;
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PtyHostConnectPolicy.PipeOptionsFor(policy));
            try
            {
                // Do not slice ConnectAsync into 2s cancels: on Linux those aborted connects
                // sit in the listen backlog and the host's Accept never completes (CARD-0490
                // Docker phone-home: socket existed, host waited 30s, runner timed out at 15s).
                var milliseconds = (int)Math.Clamp(remaining.TotalMilliseconds, 50, int.MaxValue);
                var socketFile = policy?.CheckSocketFile is { } check ? CheckSocketFile(check, pipeName) : (SocketFileVerdict?)null;
                if (socketFile is SocketFileVerdict.Refused)
                    Unproven(policy!, ref observed);
                try
                {
                    await Task.Run(() => pipe.Connect(milliseconds), ct);
                }
                catch (UnauthorizedAccessException) when (policy is { CurrentUserOnly: true })
                {
                    // Windows: the pipe server is owned by another account.
                    throw new PtyHostPeerRefusedException(PtyHostPeerRefusedException.MismatchCode);
                }

                if (policy is not null)
                {
                    // Connect waits for an absent socket to appear, so check the file it reached.
                    if (socketFile is SocketFileVerdict.Absent
                        && CheckSocketFile(policy.CheckSocketFile!, pipeName) is not SocketFileVerdict.Ok)
                        Unproven(policy, ref observed);
                    // Before any write: the hello already tells a squatter the session is wanted.
                    if (!AcceptsPeer(policy, pipe.SafePipeHandle))
                        Unproven(policy, ref observed);
                }

                await PtyHostFraming.WriteAsync(pipe, new HelloMessage(PtyHostProtocol.Version), ct);
                var reply = await PtyHostFraming.ReadAsync(pipe, ct);
                // Only a missing reply is the relisten race. A frame that arrived and is not
                // helloAck is a protocol fault and must fail this attempt (CARD-1634).
                if (reply is null)
                    throw new HelloEofException("Expected helloAck, got EOF.");
                if (reply is not HelloAckMessage hello)
                    throw new InvalidOperationException($"Expected helloAck, got {reply.GetType().Name}.");

                return new PtyHostClient(pipe, hello);
            }
            catch (Exception ex) when (
                ex is IOException or TimeoutException or HelloEofException
                || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                // A detached host closes the current pipe and listens again. The reconnect can be
                // accepted by the socket that is already closing, so hello reads EOF. That is the
                // same "server not ready" condition as a refused connect: retry inside this budget.
                // Failing the first EOF made adoption kill a live host (CARD-1632).
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

    // CARD-1653 S2 (D-1/A-4): a proof step that throws (a probe without its libc entry point, a
    // reader or liveness fault) proves nothing. It is a failed check, so Enforce refuses without a
    // write and the caller holds the host; it is never a transport retry or an escape to a kill.
    private static SocketFileVerdict CheckSocketFile(Func<string, SocketFileVerdict> check, string pipeName)
    {
        try { return check(pipeName); }
        catch (Exception) { return SocketFileVerdict.Refused; }
    }

    private static bool AcceptsPeer(PtyHostConnectPolicy policy, SafePipeHandle handle)
    {
        try { return policy.AcceptPeer(policy.ReadPeer(handle)); }
        catch (Exception) { return false; }
    }

    /// <summary>Enforce refuses; Observe reports the fixed code once per connect and continues.</summary>
    private static void Unproven(PtyHostConnectPolicy policy, ref bool observed)
    {
        if (!policy.Observe)
            throw new PtyHostPeerRefusedException(PtyHostPeerRefusedException.MismatchCode);
        if (observed) return;
        observed = true;
        policy.OnObserved?.Invoke(PtyHostPeerRefusedException.MismatchCode);
    }

    public async Task<LaunchedMessage> LaunchAsync(LaunchMessage launch, CancellationToken ct)
    {
        if (launch.VerificationBinding is not null) RequireCustodySupport();
        var reply = await RequestAsync(launch, ct);
        return reply switch
        {
            LaunchedMessage launched => launched,
            ErrorMessage error when error.Code.StartsWith("verification_custody_", StringComparison.Ordinal) =>
                throw new VerificationCustodyException(error.Code),
            ErrorMessage error => throw new InvalidOperationException($"Host launch failed ({error.Code}): {error.Message}"),
            _ => throw new InvalidOperationException($"Unexpected launch reply {reply.GetType().Name}."),
        };
    }

    public Task<VerificationCustodyStatus> GetCustodyAsync(VerificationExecutionBinding binding,
        CancellationToken ct) => GetCustodyAsync(binding, false, ct);

    public async Task<VerificationCustodyStatus> GetCustodyAsync(VerificationExecutionBinding binding,
        bool seal, CancellationToken ct)
    {
        RequireCustodySupport();
        var reply = await RequestAsync(new CustodyRequestMessage(binding, seal), ct);
        return reply switch
        {
            CustodyReplyMessage custody when custody.Custody.Binding == binding => custody.Custody,
            ErrorMessage error => throw new VerificationCustodyException(error.Code),
            _ => throw new VerificationCustodyException("verification_custody_identity_mismatch"),
        };
    }

    private void RequireCustodySupport()
    {
        if (Hello.Features?.Contains("verificationCustodyV1") != true || Hello.HostInstanceId is null)
            throw new VerificationCustodyException("verification_custody_unsupported_backend");
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

                    case LaunchedMessage or AttachedMessage or ResyncMessage or StatusReplyMessage or CustodyReplyMessage:
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

    /// <summary>
    /// Clean EOF where helloAck should have been. Retried inside the connect budget.
    /// Stays an <see cref="InvalidOperationException"/> so a timed-out connect still
    /// reports that EOF as its inner failure.
    /// </summary>
    private sealed class HelloEofException : InvalidOperationException
    {
        public HelloEofException(string message) : base(message) { }
    }
}

/// <summary>
/// CARD-1653 S2: what <see cref="PtyHostClient.ConnectAsync"/> proves about the peer before it writes
/// the hello. A null policy is the unchecked connect.
/// </summary>
public sealed class PtyHostConnectPolicy
{
    /// <summary>Windows only: the client refuses a pipe server owned by another account.</summary>
    public bool CurrentUserOnly { get; init; }

    /// <summary>Linux only: the socket file's type and owner, read before each connect attempt.</summary>
    public Func<string, SocketFileVerdict>? CheckSocketFile { get; init; }

    public Func<SafePipeHandle, PtyHostPeerCredentials?> ReadPeer { get; init; } = PtyHostPeerCredentials.Read;

    /// <summary>True only for the expected host. Unreadable credentials arrive as null.</summary>
    public Func<PtyHostPeerCredentials?, bool> AcceptPeer { get; init; } = _ => false;

    /// <summary>Report a failed check through <see cref="OnObserved"/> and connect anyway.</summary>
    public bool Observe { get; init; }

    public Action<string>? OnObserved { get; init; }

    /// <summary>The client stream options: a null policy keeps <see cref="PipeOptions.Asynchronous"/> exactly.</summary>
    public static PipeOptions PipeOptionsFor(PtyHostConnectPolicy? policy) => policy is { CurrentUserOnly: true }
        ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        : PipeOptions.Asynchronous;
}

/// <summary>CARD-1653 S2: the peer is not the expected host. Never retried; the message is the fixed code.</summary>
public sealed class PtyHostPeerRefusedException(string code) : UnauthorizedAccessException(code)
{
    public const string MismatchCode = "pty_host_peer_mismatch";

    public string Code { get; } = code;
}
