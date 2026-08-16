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

    /// <summary>
    /// Whether the third pass may write a DB-dead-but-runner-alive session back to Running
    /// (CARD-0056). Off leaves the mismatch visible as an alert and changes nothing.
    ///
    /// <para>Re-adoption is the DEFAULT action for that mismatch, and deliberately so: the case
    /// that created it was a perfectly healthy session — the operator's own live conversation —
    /// marked Failed by a launch-verification false positive while it was working normally. A pass
    /// that resolved the mismatch by killing would have killed it mid-sentence. Killing here needs
    /// prior operator intent (a <c>Stopped</c> row) or an operator acting on the now-visible
    /// session; "no agent claims it" is never enough.</para>
    /// </summary>
    public bool ReAdoptEnabled { get; set; } = true;

    /// <summary>
    /// How many times one session may be re-adopted per server uptime before the pass stops and
    /// escalates to Critical. A session that keeps flapping between Failed and runner-Running is a
    /// state for a human, not a loop to run forever. In-memory, so a restart clears it.
    /// </summary>
    public int MaxReAdoptionsPerSession { get; set; } = 3;
}
