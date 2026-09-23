using System.Net.WebSockets;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0631 D-4: the one writer for one phone-home socket. Replies, request-limit errors,
/// heartbeats and events all send through it, so two producers never write the same socket at
/// once - a WebSocket permits a single outstanding send, and an overlapping send throws and
/// loses its frame. It belongs to exactly one connection: a task that outlives its connection
/// keeps this writer and this socket, and can never send onto the replacement.
/// </summary>
internal sealed class PhoneHomeConnectionWriter(WebSocket socket, int maxUtf8Bytes)
{
    private static readonly TimeSpan CloseGateWait = TimeSpan.FromSeconds(5);

    // Never disposed: a task that outlived its connection may still be waiting on it, and a
    // SemaphoreSlim without an AvailableWaitHandle holds nothing that needs disposal.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WebSocket Socket { get; } = socket;
    public int MaxUtf8Bytes { get; } = maxUtf8Bytes;

    public async Task SendAsync(PhoneHomeFrame frame, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await PhoneHomeFraming.WriteFrameAsync(Socket, frame, MaxUtf8Bytes, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// End this connection after a send failed: the server then observes a transport failure
    /// instead of a healthy socket that silently lost a reply.
    /// </summary>
    public void Abort()
    {
        try { Socket.Abort(); }
        catch { /* already gone */ }
    }

    /// <summary>
    /// Close without overlapping a data send. The connection token is already cancelled, so a
    /// queued send gives up and an in-progress one ends promptly; if the gate still cannot be
    /// taken, the socket is aborted rather than closed underneath a send.
    /// </summary>
    public async Task CloseAsync(WebSocketCloseStatus status, string description)
    {
        if (!await _gate.WaitAsync(CloseGateWait))
        {
            Abort();
            return;
        }

        try
        {
            await Socket.CloseAsync(status, description, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }
}
