namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Who put a queued message on the session's queue. Drives delivery batching: only
/// <see cref="Channel"/> messages coalesce into batched turns; <see cref="Ui"/> and
/// <see cref="System"/> messages always deliver one-per-turn (operator workflows and system
/// notes keep today's semantics).
/// </summary>
public enum QueuedMessageOrigin
{
    /// <summary>Enqueued by a human through the web UI (default; pre-existing rows are Ui).</summary>
    Ui = 0,

    /// <summary>Routed in from an external chat channel by the bridge.</summary>
    Channel = 1,

    /// <summary>Injected by Antiphon itself (bootstrap/restart/compaction-recovery notes).</summary>
    System = 2,

    /// <summary>
    /// A delegated task's completion note, or an answer to a delegate's question. Batches like
    /// <see cref="Channel"/> — five delegates finishing together should produce ONE note, not five
    /// turns — but with a size cap, since task reports are far bigger than chat messages.
    /// </summary>
    Delegation = 3,

    /// <summary>
    /// A scheduled check-in on a running delegate (CARD-0047), delivered to the delegate's CALLER.
    /// Deliberately NOT batched like <see cref="Delegation"/>: checks are small and rare, and
    /// coalescing two of them would merge the state of two different delegates into one note.
    ///
    /// <para>Its own value rather than a reuse of <see cref="Delegation"/> so a check is
    /// distinguishable from a completion everywhere the origin is read — a check must never be
    /// mistaken for a report.</para>
    /// </summary>
    Check = 4,

    /// <summary>
    /// Injected by supervision (CARD-0082 idle auto-compact). Like <see cref="Ui"/> and
    /// <see cref="System"/> it must NOT batch — one <c>/compact</c> is one delivery. The queue
    /// rule for this origin is cancel, never strand / cancel, never park: an auto-compact that
    /// cannot deliver right now is dropped and re-derived by a later sweep, not left Pending for
    /// the next turn-end (which would compact a session that just became active) and not parked
    /// for a human (parking exists for human-owed content).
    /// </summary>
    Supervision = 5,
}
