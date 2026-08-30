using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// The named, computable conditions that make a piece of work "stuck" (CARD-0035 §D1). Every member
/// is a predicate over stored state — nothing here is a heuristic reading of a screen, and nothing
/// is inferred from silence alone.
///
/// <para>The numbering is part of the contract: the client maps kind → label/colour/actions, so a
/// member is APPENDED, never renumbered.</para>
/// </summary>
public enum AttentionKind
{
    /// <summary>The delegate asked a question. Only a human answer moves this.</summary>
    BlockedQuestion = 0,

    /// <summary>
    /// A queued message hit <c>MaxDeliveryAttempts</c> and PARKED: still Pending, still visible, and
    /// no automatic path will ever type it again (CARD-0055).
    /// </summary>
    ParkedMessage = 1,

    /// <summary>An open task whose session row is missing, Stopped, Failed, or already ended.</summary>
    DeadSession = 2,

    /// <summary>Dispatched, past the grace, and the session has written not one transcript entry.</summary>
    NeverStarted = 3,

    /// <summary>
    /// The delegate reported, but the report carried no correlation marker, so the task could not be
    /// settled from it (<see cref="AgentIncidentKind.DelegateReportUncorrelated"/>).
    /// </summary>
    UncorrelatedReport = 4,

    /// <summary>
    /// Well past the caller's own estimate AND idle at the prompt — the "finished but never
    /// reported" shape. A session that is mid-turn is deliberately NOT a member: genuinely slow is
    /// not stuck, and a view that says otherwise is not worth opening.
    /// </summary>
    PastExpectedIdle = 5,

    /// <summary>The check budget ran out with the task still open — nobody is watching it any more.</summary>
    ChecksSpent = 6,

    /// <summary>
    /// The runner and the database disagree about a session: runner-Running against a DB row that
    /// says Failed or Stopped (Error — the row is wrong, and a wrong row silently disables check-ins
    /// to that session), or a runner session with no DB row at all (Warning — unclaimed is suspect,
    /// not proven broken, and it is usually somebody's live work). ABSENT rather than empty when
    /// <see cref="AttentionDto.RunnerConsulted"/> is false.
    /// </summary>
    SessionDisagreement = 7,

    /// <summary>
    /// Error-or-worse incidents in the recency window that no row above already carries, collapsed
    /// per agent and kind. Ungrouped this is the noisiest condition by an order of magnitude
    /// (measured 2026-08-17 on the live database: 107 raw rows, 4 grouped).
    /// </summary>
    RecentCriticalIncident = 8,

    /// <summary>Context, not an alarm: tasks that Failed inside the recency window.</summary>
    RecentFailure = 9,

    /// <summary>
    /// Closing on a deadline that will FAIL the task: the role's hard wall-clock ceiling, or — while
    /// the session is mid-turn — the phase-aware model-wait / local-execution deadline (CARD-0020
    /// S2/S3, <see cref="TaskDeadlinePolicy"/>). Surfaced from 80% of the limit, the way
    /// <c>NeverStartedGrace</c> previews the delivery watchdog, so the operator sees it while a
    /// human still has the option of a reply, a check or a cancel.
    ///
    /// <para>It sits BELOW <see cref="PastExpectedIdle"/> in the first-match order on purpose. That
    /// condition covers "past the estimate and NOT mid-turn" and explicitly declines the mid-turn
    /// case — a session that is working is "never listed for being slow, however far past the
    /// estimate it has run" — and the mid-turn case is exactly the hole this fills. An idle task
    /// keeps the more explanatory row; a working one falls through to this.</para>
    /// </summary>
    Overdue = 10,

    /// <summary>
    /// Dispatched, past the delivery grace, the brief's own queue row is still <c>Pending</c>, and
    /// the session is mid-turn (CARD-0117 S5). The delivery watchdog declined to fail-and-kill on
    /// that evidence (CARD-0055) and handed the bound to <c>TaskDeadlinePolicy</c>. Computed from
    /// live state — no new incident kind. Ordered below <see cref="NeverStarted"/> and above
    /// <see cref="UncorrelatedReport"/>.
    /// </summary>
    BriefUndelivered = 11,

    /// <summary>
    /// A working session whose transcript rows keep landing and none of them is new (CARD-0153,
    /// <c>TaskProgressPolicy</c>). Computed live from the same verdict the stall sweep raises on
    /// — the row exists because the condition holds now, not because it held once. Ordered after
    /// <see cref="PastExpectedIdle"/> (which declines the mid-turn case) and before
    /// <see cref="Overdue"/>. Detection only: Reply, Cancel, OpenDrawer; never a kill.
    /// </summary>
    ProgressStalled = 12,

    /// <summary>A card is parked until a human makes and records a decision.</summary>
    CardNeedsDecision = 13,

    /// <summary>
    /// A card that has sat In Progress with nobody on it past <c>CardTransitions:StaleAfterDays</c>
    /// (CARD-0040 §2.5): no open bound task, no live session, not owned by a card session. Computed
    /// at read time from rows that already exist — no storage, and no alert sink, because it is a
    /// state rather than an incident.
    ///
    /// <para>DETECTION ONLY, the CARD-0153 rule: nothing here moves the card, kills anything or
    /// dispatches anything. Warning, so it lands in the <c>suspect</c> group rather than demanding
    /// an answer now.</para>
    /// </summary>
    CardStalled = 14,

    /// <summary>
    /// A task that Failed before it was ever dispatched, whose caller has not yet heard (CARD-0231).
    /// Error, so it lands in the counted "Broken" group rather than the collapsed RecentFailure
    /// history. The reminder machinery's own state is the predicate: armed while
    /// <c>DispatchedAt == null &amp;&amp; NextCheckAt != null</c>, gone the moment anything
    /// acknowledges it. Not subject to RecentFailure's 24-hour window.
    /// </summary>
    FailureUnacknowledged = 15,

    /// <summary>
    /// An orchestrator session did a cold investigation run — consecutive source reads with no
    /// dispatch and no report naming those files (CARD-0247). Warning, Process group, after
    /// <see cref="FailureUnacknowledged"/>. Detection only: the row exists because the sweep
    /// wrote an <c>OrchestratorInvestigation</c> incident, and nothing here dispatches or kills.
    /// </summary>
    OrchestratorInvestigation = 16,
}

/// <summary>
/// Which of the verbs the server already serves apply to a row (CARD-0035 §D3). No new server verbs
/// exist for this view — the list names existing endpoints so the client never has to infer them
/// from the kind.
/// </summary>
public enum AttentionAction
{
    /// <summary>Answer a blocked delegate in place — <c>POST /api/agent-tasks/{id}/reply</c>.</summary>
    Reply = 0,

    /// <summary><c>POST /api/agent-tasks/{id}/retry</c>.</summary>
    Retry = 1,

    /// <summary><c>POST /api/agent-tasks/{id}/cancel</c>.</summary>
    Cancel = 2,

    /// <summary><c>POST /api/agent-tasks/{id}/escalate</c>.</summary>
    Escalate = 3,

    /// <summary><c>POST /api/sessions/{id}/messages/{messageId}/send-now</c> — bypasses parking.</summary>
    SendNow = 4,

    /// <summary><c>DELETE /api/sessions/{id}/messages/{messageId}</c>.</summary>
    CancelMessage = 5,

    /// <summary>Read the check digest before deciding — the right answer is often "leave it".</summary>
    OpenDrawer = 6,

    /// <summary><c>POST /api/sessions/{id}/kill</c>. Slice 2's disagreement rows only.</summary>
    KillSession = 7,

    /// <summary>Open the agent's incident drawer.</summary>
    OpenAgent = 8,

    /// <summary>Open the card that is waiting on a decision.</summary>
    OpenCard = 9,
}

/// <summary>
/// One thing that needs a human, and enough of the why to decide without opening anything.
/// </summary>
/// <param name="Severity">
/// Critical = needs you now, Error = broken, Warning = suspect. The client groups on this, so it is
/// the row's rank as well as its colour.
/// </param>
/// <param name="Headline">One server-computed line naming the condition in the row's own numbers.</param>
/// <param name="Evidence">
/// The derivation, in text: the incident message, the failure reason, the parked body, and — for a
/// task that has been checked — the tail of its latest check digest.
/// </param>
/// <param name="SinceUtc">When the condition began, best-effort. Ordering key within a severity.</param>
/// <param name="SubtreeCostUsd">
/// Rolled-up spend for the task and everything under it. On the row rather than buried in a report:
/// what a stuck delegate has already cost is half the decision about what to do with it.
/// </param>
public sealed record AttentionItemDto(
    AttentionKind Kind,
    AlertSeverity Severity,
    Guid? TaskId,
    Guid? SessionId,
    Guid? AgentId,
    Guid? MessageId,
    string Title,
    string Headline,
    string Evidence,
    DateTime? SinceUtc,
    decimal? SubtreeCostUsd,
    IReadOnlyList<AttentionAction> Actions,
    Guid? CardId = null,
    Guid? BoardId = null);

/// <param name="RunnerConsulted">
/// Whether the session runner answered this sweep. False means the runner-derived condition
/// (<see cref="AttentionKind.SessionDisagreement"/>) is ABSENT rather than empty — the difference
/// between "nothing disagrees" and "nobody asked", which the client must not collapse.
/// </param>
public sealed record AttentionDto(
    DateTime GeneratedAt,
    bool RunnerConsulted,
    IReadOnlyList<AttentionItemDto> Items);

/// <summary>Counts for the global navigation badge, without returning the attention rows.</summary>
public sealed record AttentionSummaryDto(
    int Open,
    int Decisions,
    DateTime GeneratedAt)
{
    public static AttentionSummaryDto From(AttentionDto attention) => new(
        attention.Items.Count(item => item.Kind != AttentionKind.RecentFailure),
        attention.Items.Count(item => item.Kind == AttentionKind.CardNeedsDecision),
        attention.GeneratedAt);
}
