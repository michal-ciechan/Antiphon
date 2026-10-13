using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-1219 D-2: refuses an identical transcript-fault or transcript-bound record seen from the
/// same runner within <c>window</c>, so a frame the transport delivers twice (including across a
/// reconnect) records one incident. The key is the parsed record itself (record value equality),
/// so the runner's genuine five-minute repeat, which carries a higher <c>Repeat</c> and a longer
/// <c>UnboundSeconds</c>, is a different key and always records, exactly as the local lane does.
/// Time is the connection clock's monotonic timestamp. At most <c>capacity</c> keys are held; a
/// new key past capacity evicts the oldest admitted key, and a re-admitted key becomes the newest.
/// Callers may overlap during a reconnect handoff, so <see cref="Admit(SessionRunnerTranscriptFaultEvent)"/>
/// runs under a lock.
/// </summary>
internal sealed class TranscriptIncidentReplayGuard(TimeProvider clock, TimeSpan window, int capacity = 512)
{
    private readonly object _gate = new();
    private readonly Dictionary<object, LinkedListNode<(object Key, long Stamp)>> _seen = new();
    private readonly LinkedList<(object Key, long Stamp)> _order = new();

    public bool Admit(SessionRunnerTranscriptFaultEvent fault) => AdmitKey(fault);

    public bool Admit(SessionRunnerTranscriptBoundEvent bound) => AdmitKey(bound);

    private bool AdmitKey(object key)
    {
        lock (_gate)
        {
            if (_seen.TryGetValue(key, out var node))
            {
                if (clock.GetElapsedTime(node.Value.Stamp) < window)
                    return false;
                _order.Remove(node);
                _seen.Remove(key);
            }
            else if (_seen.Count >= capacity && _order.First is { } oldest)
            {
                _order.RemoveFirst();
                _seen.Remove(oldest.Value.Key);
            }

            _seen[key] = _order.AddLast((key, clock.GetTimestamp()));
            return true;
        }
    }
}
