using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// The named, computable conditions that make a piece of work "stuck" (CARD-0035 §D1). Every member
/// is a predicate over stored state — nothing here is a heuristic reading of a screen, and nothing
/// is inferred from silence alone.
///
/// <para>The numbering is part of the contract: the client maps kind → label/colour/actions, so a
/// member is APPENDED, never renumbered. <see cref="SessionDisagreement"/> is declared here but is
/// populated by slice 2 (it needs the runner diff), so that slice stays purely additive.</para>
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
    /// The runner and the database disagree about a session (runner-Running vs DB-Failed/Stopped, or
    /// a runner session with no DB row at all). SLICE 2 — the projection does not emit this yet.
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
    IReadOnlyList<AttentionAction> Actions);

/// <param name="RunnerConsulted">
/// Whether the session runner answered this sweep. False means the runner-derived condition
/// (<see cref="AttentionKind.SessionDisagreement"/>) is ABSENT rather than empty — the difference
/// between "nothing disagrees" and "nobody asked", which the client must not collapse.
/// </param>
public sealed record AttentionDto(
    DateTime GeneratedAt,
    bool RunnerConsulted,
    IReadOnlyList<AttentionItemDto> Items);
