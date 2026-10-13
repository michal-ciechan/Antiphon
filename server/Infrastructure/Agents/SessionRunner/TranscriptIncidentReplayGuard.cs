using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-1219 D-2: a bounded, in-memory admission cache for one runner's transcript-fault and
/// transcript-bound records, keyed on the parsed record's value. Admit, refuse, window expiry and
/// eviction are as <c>TranscriptIncidentReplayGuardTests</c> asserts. The pump calls it before the
/// incident service, so an admission is not a written incident. An equal record is refused inside
/// the window whatever produced it, including an equal <c>TranscriptTailer.ReportRootFault</c>
/// repeat (not covered by a test). Callers may overlap during a reconnect handoff, so
/// <see cref="Admit(SessionRunnerTranscriptFaultEvent)"/> runs under a lock.
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
