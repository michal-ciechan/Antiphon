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

    /// <summary>
    /// Whether the sweep reports the pty-host census it already collects (CARD-0102 / coverage plan
    /// P0-3). Off leaves the numbers computed and unsaid, which is the state that let 39 stray
    /// hosts accumulate for up to ten hours.
    /// </summary>
    public bool CensusAlertEnabled { get; set; } = true;

    /// <summary>
    /// How many runner sessions may be Running with NO database row at all before the census alert
    /// fires. Starts at 10: a handful is ordinary (a launch mid-flight, a row cascade-deleted while
    /// its session lives), and the 2026-08-20 incident sat at 46.
    /// </summary>
    public int UnclaimedSessionAlertThreshold { get; set; } = 10;

    /// <summary>
    /// By how much live <c>Antiphon.PtyHost</c> processes may exceed the number of runner sessions
    /// with a live agent child before the census alert fires. Starts at 5: a host outlives its
    /// child by design (<c>PtyHostLingerHours</c>, so an exit can still be collected), so a small
    /// surplus is the system working.
    ///
    /// <para>This is the half that catches the shape CARD-0102 actually had, and the reason the
    /// card's own proposed remedy would not have worked: those hosts were not lingering orphans
    /// waiting out a TTL, they were LIVE sessions holding interactive <c>cmd.exe</c> children that
    /// never exit. A shorter linger would have collected exactly none of them.</para>
    /// </summary>
    public int PtyHostSurplusAlertThreshold { get; set; } = 5;

    /// <summary>
    /// The hard ceiling above which unclaimed runner sessions are Critical rather than Warning.
    /// Below the measured incident (46), deliberately: the number that produced the card must land
    /// on the severity that reaches a human, not one notch under it.
    /// </summary>
    public int UnclaimedSessionCriticalThreshold { get; set; } = 40;

    /// <summary>
    /// The hard ceiling above which the pty-host surplus is Critical rather than Warning. Below the
    /// measured 39, for the same reason.
    /// </summary>
    public int PtyHostSurplusCriticalThreshold { get; set; } = 20;

    /// <summary>
    /// How long the census alert stays quiet after raising, while the condition still holds. The
    /// sweep runs every <see cref="IntervalMs"/> (15s); without this the alert would write 240 rows
    /// an hour and mean nothing — CARD-0101's refusal fault managed 37 identical Warnings in three
    /// hours and nobody acted on any of them.
    ///
    /// <para>An ESCALATION (Warning becoming Critical) bypasses the window, and the window is
    /// cleared as soon as the condition stops holding, so a recurrence after a quiet period is
    /// reported immediately rather than waiting out a window it did not cause.</para>
    /// </summary>
    public int CensusAlertRepeatMinutes { get; set; } = 60;
}
