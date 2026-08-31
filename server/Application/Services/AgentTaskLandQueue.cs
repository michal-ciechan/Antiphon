using System.Threading.Channels;

namespace Antiphon.Server.Application.Services;

/// <summary>Non-blocking hand-off for explicit branch landings.</summary>
public sealed class AgentTaskLandQueue
{
    private readonly Channel<LandRequest> _channel = Channel.CreateUnbounded<LandRequest>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public bool TryEnqueue(Guid taskId, string? verifyFilter) =>
        _channel.Writer.TryWrite(new LandRequest(taskId, verifyFilter));

    public IAsyncEnumerable<LandRequest> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    public sealed record LandRequest(Guid TaskId, string? VerifyFilter);
}
