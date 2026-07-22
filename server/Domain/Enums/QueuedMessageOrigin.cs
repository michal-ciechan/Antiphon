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
}
