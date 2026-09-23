using System.Net.WebSockets;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0631 D-4: the one writer for one phone-home socket. Replies, request-limit errors,
/// heartbeats and events all send through it, so two producers never write the same socket at
/// once. It belongs to exactly one connection: a task that outlives its connection keeps this
/// writer and this socket, and can never send onto the replacement.
/// </summary>
internal sealed class PhoneHomeConnectionWriter(WebSocket socket, int maxUtf8Bytes)
{
    public WebSocket Socket { get; } = socket;
    public int MaxUtf8Bytes { get; } = maxUtf8Bytes;

    public async Task SendAsync(PhoneHomeFrame frame, CancellationToken ct)
    {
        await PhoneHomeFraming.WriteFrameAsync(Socket, frame, MaxUtf8Bytes, ct);
    }
}
