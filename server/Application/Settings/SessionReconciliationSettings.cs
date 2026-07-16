namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Tuning for the periodic session/agent reconciliation sweep — the poll-based backstop that keeps
/// DB status honest when runner events are missed (runner crash/restart, dropped SSE stream, or a
/// process that died without the runner noticing). See SessionReconciliationService.
/// </summary>
public sealed class SessionReconciliationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>How often the sweep runs.</summary>
    public int IntervalMs { get; set; } = 15_000;

    /// <summary>
    /// How long a Starting session may be absent from the runner before it is presumed dead —
    /// covers the gap between the DB row being created and the background launch reaching the runner.
    /// </summary>
    public int StartingGraceMs { get; set; } = 90_000;

    /// <summary>
    /// How long an agent may sit in Working with no live session before being flipped to Failed.
    /// Generous on purpose: normal transitions (session hand-over between cards, launch queue lag)
    /// must never be mistaken for death.
    /// </summary>
    public int AgentGraceMs { get; set; } = 120_000;
}
