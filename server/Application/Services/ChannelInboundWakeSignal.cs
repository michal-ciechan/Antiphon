using System.Threading.Channels;

namespace Antiphon.Server.Application.Services;

/// <summary>Process-local hints; the journal remains authoritative if a hint is lost.</summary>
public sealed class ChannelInboundWakeSignal
{
    private readonly Channel<Guid> _requests = Channel.CreateUnbounded<Guid>();
    public ChannelReader<Guid> Reader => _requests.Reader;
    public void Signal(Guid id) => _requests.Writer.TryWrite(id);
}
