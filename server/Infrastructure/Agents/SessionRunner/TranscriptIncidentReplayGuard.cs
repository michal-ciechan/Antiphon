using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-1219 D-2: a bounded, in-memory admission cache for one runner's transcript-fault and
/// transcript-bound records. <c>Admit</c> returns false only when an equal record was admitted less
/// than <c>window</c> ago and its key is still held; otherwise it stamps the record and returns true
/// (<c>TranscriptIncidentReplayGuardTests.First_fault_is_admitted_and_identical_replay_is_refused</c>).
/// The pump calls it before the incident service, and nothing removes the stamp if that call then
/// fails, so the guard counts admissions, not written incidents. The key is the parsed record itself
/// (record value equality), so the runner's five-minute repeat, which carries a higher
/// <c>Repeat</c> and a longer <c>UnboundSeconds</c>, is a different key and is admitted
/// (<c>A_genuine_repeat_with_higher_repeat_count_is_admitted</c>). Time is the clock's monotonic
/// timestamp; a refused record keeps its first stamp, and once <c>window</c> has elapsed the equal
/// record is admitted and re-stamped (<c>Replay_after_the_window_is_admitted_again</c>). At most
/// <c>capacity</c> keys are held; a new key past capacity evicts the oldest admitted key, whose
/// record is then admitted again even inside its window, and a re-admitted key becomes the newest
/// (<c>Guard_evicts_the_oldest_key_past_capacity</c>). Nothing is persisted: a new pump instance
/// starts with no keys. The key is a volatile admission identity only; an incident row the service
/// writes has its own generated <c>AgentIncident.Id</c> and does not store the whole key (the
/// ordinary fault row keeps neither <c>Repeat</c> nor <c>UnboundSeconds</c>). Callers may overlap during a reconnect handoff, so
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
