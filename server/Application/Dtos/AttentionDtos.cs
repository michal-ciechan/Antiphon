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

    /// <summary>
    /// An inbound channel message the Antiphon consumer group never consumed within the gateway's
    /// budget (CARD-0245 S2). Critical. The row has no required agent: the event can arrive before
    /// the restarted bridge has catalogued the channel. Detection only — never a restart.
    /// </summary>
    InboundUnconsumed = 17,

    /// <summary>
    /// A caller-session Delegation or Check note still Pending past the shared delivery grace
    /// (CARD-0267). The task may already be settled; a busy caller simply has not reached a
    /// WhenIdle delivery point. Detection only — the row does not retry, cancel, kill, or
    /// SendNow, because bypassing WhenIdle can type into a busy caller composer.
    /// </summary>
    CallerNoteUndelivered = 18,

    /// <summary>
    /// A current cardless interactive session has been Running past a two-minute grace, its
    /// owning agent has non-blank Details, and the session has no transcript and no UI-origin
    /// queued message (CARD-0287). The start left Details as standing metadata and never
    /// supplied a prompt. Detection only: this row does not type Details, queue a message, or
    /// auto-fix the start. Only the fresh interactive launch shape qualifies
    /// (<c>CreatedAt == StartedAt</c> and a non-null composed stamp); a resume or Herdr attach
    /// of an empty shell must not raise it.
    /// </summary>
    CardlessDetailsNoPrompt = 19,

    /// <summary>
    /// A live session is holding queued-but-unconverted input: its latest <c>QueueEnqueue</c>
    /// transcript row has no later conversion while the session reads idle (CARD-0292, projected
    /// from an open <c>AgentIncidentKind.QueuedInputNeverConverted</c> incident and re-verified at
    /// read time, so the row exists because the condition holds now). The usual cause is a
    /// blocking TUI modal that swallowed the input — every health signal reads healthy while a
    /// message sits eaten. Detection only: nothing is killed, typed, or Esc'd from here.
    /// </summary>
    QueuedInputStuck = 20,

    /// <summary>
    /// A standing agent outlived the work it was created for (CARD-0239). Two arms, one
    /// exclusion set: not AlwaysOn, not a pool delegate, not channel-bound, no open task
    /// (Queued included), and not a project-root worker on a board that still has cards.
    /// Arm 1 is a live Running session that is idle at the prompt with the newest transcript
    /// older than 8 h. Arm 2 is a leftover identity — Idle/Stopped/Failed/Disconnected, a
    /// worktree cwd or the sole agent on a same-named zero-card board, untouched for 2 days.
    /// Detection only: nothing here stops, kills, retires, or archives an agent. Zero
    /// transcript rows are not this kind (<see cref="CardlessDetailsNoPrompt"/> /
    /// <see cref="NeverStarted"/> own that shape). Idle auto-compaction can delay arm 1 by
    /// refreshing the transcript clock; it does not suppress it.
    /// </summary>
    AgentOutlivedTask = 21,

    /// <summary>
    /// A Dispatched or Working task whose transcript already carries this task's marked
    /// closing line, and whose newest TurnEnd is a report boundary (CARD-0288). The live
    /// observer missed the settle (server-down catch-up is the usual cause); the dispatcher
    /// re-hands on the next tick. Detection only: nothing here settles, retries, or kills.
    /// Kill is not the repair — <c>FailDeadSessionTasksAsync</c> tries settlement first.
    /// </summary>
    ReportUnsettled = 22,

    /// <summary>
    /// Dispatched or Working, the closing-line nudge was issued, the session is idle, and
    /// no report token for this task is in the transcript (CARD-0294). Detection only —
    /// not <see cref="ReportUnsettled"/> (that kind is "marker present, settlement missed").
    /// Once the sweep Blocks, <see cref="BlockedQuestion"/> takes over. Appended after
    /// shipped 22; do not renumber.
    /// </summary>
    UnmarkedWaiting = 23,

    /// <summary>
    /// A model family is paused (CARD-0022): AutoDetected usage-limit hold, or a later CARD-0309
    /// Manual hold. Projected from active <c>ModelAvailabilityHold</c> rows — recency is
    /// lifecycle, no ack. Appended after shipped 23; do not renumber.
    /// </summary>
    ModelAvailabilityHold = 24,

    /// <summary>
    /// A complexity chain (or later a multi-candidate pin) has no available candidate
    /// (CARD-0090). Grouped one Error row per exhausted list source. Recency is lifecycle:
    /// the row disappears when the last blocked task is resumed, rerouted or cancelled.
    /// Appended after shipped 24; do not renumber.
    /// </summary>
    RoutingExhausted = 25,

    /// <summary>
    /// A schedule's last fire skipped, refused, failed, or is stuck at Claimed (CARD-0057 S5).
    /// Warning. Cleared by the next good fire or by disabling. <c>SkippedLate</c> is a fire
    /// row, not this. Appended after shipped 25; do not renumber.
    /// </summary>
    ScheduleMisfired = 26,

    /// <summary>
    /// CARD-0312 S3: a launch's boot prompt was delivered and transcript-confirmed and the model
    /// never answered it — rung 5 of the delivery evidence ladder. Projected from open
    /// <c>AgentIncidentKind.LivenessProbeFailed</c> incidents (kind 10, reused rather than minting
    /// a 48th), re-verified at read time against a live session and a still-unanswered boot
    /// prompt. Warning; Error on the latching third, where the mechanism has stopped restarting.
    /// Appended after shipped 26; do not renumber.
    /// </summary>
    LivenessProbeFailed = 27,

    /// <summary>
    /// An import-origin card from a non-operator author that nobody has rated, still in Backlog
    /// (CARD-0327). Warning, not a decision — <see cref="AttentionSummaryDto"/> is unchanged.
    /// Cleared by a human rating, a move out of Backlog, or archive. Appended after shipped 27;
    /// do not renumber. The plan named this 27 when <see cref="ScheduleMisfired"/> was last;
    /// <see cref="LivenessProbeFailed"/> already occupies 27.
    /// </summary>
    ImportedIssueNeedsReview = 28,
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

    /// <summary>
    /// CARD-0309: <c>DELETE /api/model-availability/{kind}/{alias}</c>. Only on
    /// <see cref="AttentionKind.ModelAvailabilityHold"/> rows that carry kind+alias.
    /// </summary>
    ClearHold = 10,

    /// <summary>
    /// CARD-0294 S1: replay the task's standing authority as the answer —
    /// <c>POST /api/agent-tasks/{id}/continue</c>. Appended after shipped 10; do not renumber.
    /// </summary>
    Continue = 11,
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
    Guid? BoardId = null,
    string? ModelKind = null,
    string? ModelAlias = null);

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
