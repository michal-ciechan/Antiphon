using System.Threading.Channels;
using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>Bounded wakeups only. Persisted Pending completion notes are the recovery source.</summary>
public sealed class CompletionNoteFlushQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(128)
    { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    public bool TryEnqueue(Guid sessionId)
    {
        if (!_pending.TryAdd(sessionId, 0)) return true;
        if (_channel.Writer.TryWrite(sessionId)) return true;
        _pending.TryRemove(sessionId, out _);
        return false;
    }
    public void Complete(Guid sessionId) => _pending.TryRemove(sessionId, out _);
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public sealed record SpecialistFailure(SpecialistSpec Spec, string Reason);

/// <summary>Optional incident delivery cannot occupy the serial model-request worker.</summary>
public sealed class SpecialistFailureQueue
{
    private readonly Channel<SpecialistFailure> _channel = Channel.CreateBounded<SpecialistFailure>(32);
    public bool TryEnqueue(SpecialistFailure failure) => _channel.Writer.TryWrite(failure);
    public IAsyncEnumerable<SpecialistFailure> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
