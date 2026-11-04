using System.Threading.Channels;

namespace Antiphon.Server.Application.Services;

/// <summary>Process-local hints; the journal remains authoritative if a hint is lost.</summary>
public sealed class ChannelInboundWakeSignal
{
    private readonly Channel<Guid> _requests = Channel.CreateUnbounded<Guid>();
    private ChannelWakeWorkerPass? _lastPass;
    public ChannelReader<Guid> Reader => _requests.Reader;
    public void Signal(Guid id) => _requests.Writer.TryWrite(id);

    /// <summary>
    /// CARD-1716 heartbeat: the inbound wake worker's last completed pass (null until one completes).
    /// Read by <c>GET /api/channels/wake-worker</c> and the bridge's stall check.
    /// </summary>
    public ChannelWakeWorkerPass? LastPass => Volatile.Read(ref _lastPass);

    internal void PassCompleted(ChannelWakeWorkerPass pass) => Volatile.Write(ref _lastPass, pass);
}

/// <summary>
/// CARD-1716: one completed pass of the inbound wake worker. <paramref name="RowsSeen"/> pending rows
/// were selected, <paramref name="RowsQueued"/> were handed to the session queue (directly or through
/// the debounce window) and <paramref name="SessionsSkippedBusy"/> sessions kept their rows for the next
/// pass because their queue lock stayed held.
/// </summary>
public sealed record ChannelWakeWorkerPass(
    DateTimeOffset CompletedAt, int RowsSeen, int RowsQueued, int SessionsSkippedBusy, TimeSpan Elapsed);
