using System.Threading.Channels;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The hand-off between the schedule sweep and the worker that fires a claimed row (CARD-0057 D4).
/// Unbounded on purpose: the producer has already claimed each row by advancing NextFireAt, so it
/// cannot queue the same fire twice on consecutive ticks, and a bounded channel would have to
/// either block the sweep or drop a fire that has already been counted.
/// </summary>
public sealed class ScheduleFireQueue
{
    private readonly Channel<FireClaim> _channel = Channel.CreateUnbounded<FireClaim>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public bool TryEnqueue(FireClaim claim) => _channel.Writer.TryWrite(claim);

    public IAsyncEnumerable<FireClaim> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public int PendingCount => _channel.Reader.Count;

    public bool TryDequeue(out FireClaim claim) => _channel.Reader.TryRead(out claim!);
}

/// <param name="DueAt">The NextFireAt value this claim won, i.e. the occurrence that was due.</param>
/// <param name="Manual">fire-now: bypasses grace and does not advance recurrence.</param>
public sealed record FireClaim(Guid ScheduleId, DateTime DueAt, int FireNumber, bool Manual = false);
