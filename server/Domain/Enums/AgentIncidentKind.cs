namespace Antiphon.Server.Domain.Enums;

/// <summary>What happened to a supervised agent. Append-only audit; feeds UI + alerts.</summary>
public enum AgentIncidentKind
{
    /// <summary>The agent's session died unexpectedly (non-zero exit / vanished).</summary>
    Crash = 0,

    /// <summary>A supervised start attempt threw.</summary>
    StartFailure = 1,

    /// <summary>A restart was scheduled (records attempt #, delay, and absolute next-retry time).</summary>
    RestartScheduled = 2,

    /// <summary>The agent ran healthily long enough to reset the backoff ladder.</summary>
    Recovered = 3,

    /// <summary>The backoff ladder crossed a tier boundary (hourly = Warning, daily = Critical).</summary>
    BackoffEscalated = 4,

    /// <summary>A human stopped an always-on agent; supervision is suspended until a manual start.</summary>
    SuspendedByUser = 5,

    /// <summary>A start cleared the suspension/backoff state.</summary>
    ResumedByUser = 6,

    /// <summary>Remote-control bridge found degraded (slice 3).</summary>
    RcDegraded = 7,

    /// <summary>Remote control re-armed in place (slice 3).</summary>
    RcReArmed = 8,

    /// <summary>Session restarted while idle to restore remote control (slice 3).</summary>
    RcRestart = 9,

    /// <summary>A liveness probe (round-trip healthcheck) failed (slice 3).</summary>
    LivenessProbeFailed = 10,

    /// <summary>
    /// A delivered message could not be verified in the Claude composer (or the submit produced
    /// no output) — the terminal is treated as wedged. Replaces the removed TUI echo probe.
    /// </summary>
    DeliveryVerificationFailed = 11,

    /// <summary>
    /// The session's context was compacted (compact-boundary transcript record). Info-level,
    /// recorded WITHOUT an alert — compaction is normal operation; the timeline row exists so
    /// operators can correlate behaviour changes with compactions.
    /// </summary>
    ContextCompacted = 12,

    /// <summary>
    /// A message delivery THREW before the terminal accepted it (runner 500/unreachable/timeout)
    /// — distinct from <see cref="DeliveryVerificationFailed"/>, where the write landed but the
    /// composer showed no evidence. The message is reverted to Pending for redelivery. Live miss
    /// 2026-08-09: briefs marked Sent before a failed write stranded four delegated tasks
    /// silently (CARD-0003/CARD-0018).
    /// </summary>
    DeliveryTransportFailed = 13,

    /// <summary>
    /// A message larger than <c>DelegationSettings.PtyInlineSafeChars</c> was typed into a terminal.
    /// Measured on 2026-08-10: above roughly 4 300 characters a single write to the ConPTY input
    /// pipe silently loses whole 1024-byte chunks of the body, so the result reads as a complete
    /// message and passes a head-or-tail liveness check. Delivery still proceeds (refusing would
    /// strand the message), but it is never again invisible: this incident is the record that the
    /// recipient may have read a splice.
    ///
    /// Note this fires on SIZE alone and is therefore not a complete guard: on 2026-08-11 four
    /// bodies well under the ceiling lost everything before their final 1024-byte chunk, raising
    /// no incident at all. See <see cref="DelegateReportUncorrelated"/>, which catches the
    /// consequence rather than the size.
    /// </summary>
    OversizedTerminalDelivery = 14,

    /// <summary>
    /// The runner could not safely bind a transcript to this agent's session and is running
    /// WITHOUT one — nothing is ingested, working/idle reads permanently idle, and a channel-bound
    /// agent cannot dispatch replies at all. Refusing is the safe outcome (CARD-0006: the
    /// alternative bound an agent to the human operator's own Claude conversation on nothing but
    /// "same cwd, written recently"), but it must never be silent. Critical when the agent has a
    /// channel binding, Warning otherwise.
    /// </summary>
    TranscriptBindFailed = 15,

    /// <summary>
    /// A transcript was bound by HEURISTIC rather than by the exact <c>&lt;session-id&gt;.jsonl</c>
    /// filename (cwd discovery, a mid-session fork, or the restart migration shim). Info-level,
    /// recorded WITHOUT an alert, mirroring <see cref="ContextCompacted"/>: the bind passed every
    /// adoption rule, but which file an agent reads from should be on the record.
    /// </summary>
    TranscriptBoundByDiscovery = 16,

    /// <summary>
    /// A delegate ended a turn with a report, but the prompt that turn answered did not carry the
    /// task's correlation marker — so the task could not be settled from it. The delegate does the
    /// work, reports, and the task sits Dispatched forever with no surface saying why.
    ///
    /// Live miss 2026-08-11 (CARD-0003): three tasks stranded overnight because the pty dropped
    /// everything before the final 1024-byte chunk of the brief, taking the head-only marker with
    /// it. The condition was already detected in code and logged at DEBUG, under a file sink set to
    /// Information — so the one event that explained three dead tasks was written nowhere.
    /// </summary>
    DelegateReportUncorrelated = 17,

    /// <summary>
    /// A delegated task settled WITHOUT the text of the response that ended its turn — so the stored
    /// report is whatever else the turn produced (mid-turn narration, most likely) and not the
    /// delegate's verdict. Raised when CARD-0046's grace expires: either settled Succeeded on that
    /// fallback text, or failed outright because the turn produced no text at all.
    ///
    /// Live miss 2026-08-13/14 (CARD-0046): six delegates lost 4 573-6 296-character reports to a
    /// bare TurnEnd emitted by the thinking record of the same API response, and every surface said
    /// the task had succeeded. The identity gate now waits for that response's own text; this is
    /// what fires when waiting does not produce it, so the SECOND occurrence is visible instead of
    /// the sixth. Warning: the task is settled either way, and a human deciding whether to re-run it
    /// needs to know the report may be preamble.
    /// </summary>
    DelegateFinalMessageMissing = 18,

    /// <summary>
    /// A delegated task settled while background subagents its turn had launched never reported —
    /// so the stored report is the turn's announcement of that work, not its outcome. Raised when
    /// CARD-0046 slice 4's <c>SubagentGraceMinutes</c> expires with a launch still unanswered.
    ///
    /// Live miss 2026-08-14 (CARD-0046 §1.4): task 26421cf2 announced four parallel reviewers,
    /// legitimately ended its turn, and was settled and priced on the announcement at 07:44:10 —
    /// then folded all four reviews into a 6 195-character verdict at 07:48:06, four minutes after
    /// its task was closed. Settlement now waits for the notifications; this is what fires when
    /// waiting does not produce them, because a background subagent can die without ever notifying.
    /// </summary>
    DelegateSubagentsNeverReported = 19,

    /// <summary>
    /// Reconciliation found a session the DB had written off as Failed while the session runner was
    /// still serving it, proved the process was alive, and wrote the row back to Running.
    ///
    /// Live miss 2026-08-16 (CARD-0056): a launch-verification false positive marked a healthy,
    /// working orchestrator session Failed, and the reconciler only ever looked the other way
    /// (DB-live vs runner-dead), so the mismatch was invisible forever — the process ran on,
    /// billable and unclaimed, while the supervisor started a replacement against the false signal.
    /// Warning rather than Info: something upstream declared a live session dead, and the incident
    /// carries the FailureReason that was wrong so the cause is on the record.
    /// </summary>
    SessionReAdopted = 20,

    /// <summary>
    /// A reply an agent owed an external chat was never published — the correlation was abandoned
    /// unanswered (its TTL expired with no matching turn) or could not be routed to a conversation.
    /// A human asked a question and got silence while the agent believes it answered.
    ///
    /// Always Critical: a channel correlation exists only because a real person is waiting on it,
    /// and this is the incident that stops the loss being invisible. Live miss 2026-08-17
    /// (CARD-0067): the reply routes lived only in process memory, a restart at 09:05:01Z wiped
    /// four of them mid-conversation, and the Family agent emitted a 42-person guest list twice
    /// into nothing. <c>OnTurnEndAsync</c> returned SILENTLY on the resulting miss, so no log line,
    /// no incident and no surface anywhere recorded 47 minutes of silence. The correlation is now
    /// durable (<see cref="Entities.SessionQueuedMessage.ChannelReplySettledAt"/>), and every
    /// channel correlation now ends in exactly one of two states: a published reply, or this.
    /// </summary>
    ChannelReplyLost = 21,

    /// <summary>
    /// A delegated task's turn was killed by the API itself — an API-error stub ended it (usage
    /// limit, 5xx, auth-expired; <c>TranscriptKinds.IsApiErrorStub</c>) — so there is no report and
    /// the error text must never be stored as one. Warning normally; Critical when the agent is
    /// channel-bound (a human is waiting on a dead line — the CARD-0055/0067 severity rule) or the
    /// error class is NeedsHuman (nothing automatic can ever fix auth-expired/model-not-found).
    ///
    /// Live miss 2026-08-17 (CARD-0071): two limit-killed delegates settled Succeeded with
    /// "You've hit your usage limit…" stored as their Result — the limit message WAS the report,
    /// and every surface said the tasks had finished. The incident carries <c>git status --short</c>
    /// of a shared checkout so a human deciding about the dead task sees any uncommitted exposure.
    /// </summary>
    ApiErrorTurnDied = 22,

    /// <summary>
    /// An idle auto-compact (CARD-0082) did not produce a <c>(manual)</c> CompactBoundary within
    /// <c>ContextCompactionSettings.BoundaryTimeoutMinutes</c> of confirmed submission, or a
    /// Supervision-origin <c>/compact</c> spent its delivery attempts and was canceled rather than
    /// parked. Warning: a later sweep re-derives the condition; this is the record that this
    /// attempt did not take. Unclaimed sessions (no owning Agent) still raise this as a standalone
    /// incident/alert with no session-targeted notification — there is no caller to tell.
    /// </summary>
    AutoCompactFailed = 23,

    /// <summary>
    /// A delegated task that was about to be Failed for zero ingested transcript rows was instead
    /// settled Succeeded because the working directory had positive evidence the work happened
    /// (commits on the task branch, or a cwd-matching JSONL whose later content cites the task).
    /// Warning: Succeeded is the right status — the work landed — but C1–C4 still refused the
    /// transcript, and the existing <see cref="TranscriptBindFailed"/> stays on the timeline.
    ///
    /// Live miss 2026-08-18 (CARD-0085): task 9a5b93a3 finished and pushed <c>b9ecf40</c>, then
    /// the delivery watchdog wrote Failed because the session had zero <c>TranscriptEntries</c>
    /// (C4 never saw a typed user-prompt head). Failed is what makes a less-careful caller
    /// redispatch on top of already-pushed work. Recovery does not bind or ingest the refused
    /// file and does not change C1–C4.
    /// </summary>
    DelegateBindRefusalRecovered = 24,

    /// <summary>
    /// A message was submitted (a matching <c>UserPrompt</c> row exists) but the stored text does
    /// not contain the full body — measured truncation, not a size-before-send prediction.
    /// Distinct from <see cref="OversizedTerminalDelivery"/> (14), which fires on size alone and
    /// then types anyway. The message is parked immediately; the session is not killed; nothing
    /// re-types (that would double-send a clipped body). Warning normally; Critical when the
    /// agent is channel-bound — a parked channel reply is a human waiting on a splice.
    ///
    /// Live miss 2026-08-10 (CARD-0024 / task c7151848): a 5 471-char report reached the parent
    /// as 379 characters (head + tail, 5 × 1024 bytes dropped from the middle) and was marked
    /// Sent because identity matches the surviving head.
    /// </summary>
    TruncatedTerminalDelivery = 25,

    /// <summary>
    /// The standing check interpreter could not produce a reading — it could not be provisioned
    /// or queued, it did not answer within the wait budget, or the settled interpretation was
    /// failed/empty — so the caller received the deterministic digest instead.
    ///
    /// Live miss 2026-08-17–19 (CARD-0079): twenty timed-out interpretations over 48 hours, every
    /// check falling back to <c>(unverified digest — interpreter unavailable: no reading within
    /// 60s)</c>, and zero incidents after the specialist's last <see cref="Recovered"/>. The
    /// digest still ships (CARD-0047's floor); this is what makes the fallback unmissable.
    /// Warning; not raised for backlog ("interpreter busy"), which is load rather than a dead
    /// specialist. Deduped per specialist so a burst of due checks is one incident, not one per
    /// check.
    /// </summary>
    CheckInterpreterUnavailable = 26,

    /// <summary>
    /// A <see cref="TranscriptBindFailed"/> refusal has been CONTINUOUS for
    /// <c>TranscriptBindingSettings.StuckAfterMinutes</c> and is still going. Critical regardless of
    /// channel binding, and deduped per session so it fires roughly once per
    /// <c>StuckRepeatMinutes</c> rather than once per underlying repeat.
    ///
    /// Live miss 2026-08-17–20 (CARD-0101): <see cref="TranscriptBindFailed"/> repeats every five
    /// minutes at Warning forever with no cap and no escalation — session 5409c537 logged 37
    /// identical incidents over three hours, six sessions ran unbound for up to 3h6m, and the only
    /// severity that reaches a human is reserved for channel-bound agents. A delegate task agent is
    /// never channel-bound, so the entire cascade was Warning-only: visible to anyone querying the
    /// database and to nobody else. This is the row that says "still broken, and it has been for
    /// hours" instead of the thirty-eighth identical row that says "broken".
    /// </summary>
    TranscriptBindStuck = 27,

    /// <summary>
    /// The three views of "what sessions exist" have diverged past a threshold: the session runner
    /// is serving far more sessions than the database claims, or far more <c>Antiphon.PtyHost</c>
    /// processes are alive than there are runner sessions with a live agent child.
    ///
    /// <para>Warning normally; Critical past a hard ceiling, because the difference between
    /// "drifting" and "dozens of stray processes" is the difference between something to look at
    /// this week and something eating the machine now.</para>
    ///
    /// <para>Live miss 2026-08-20 (CARD-0102): 39 orphaned pty-hosts — every one an
    /// <c>AntiphonAppFixture</c> session the E2E suite started and never stopped, up to ten hours
    /// old — were found and killed BY HAND during triage of an unrelated production incident, where
    /// they cost real investigation time by looking exactly like its symptom. The data was already
    /// there: <c>SessionReconciliationService</c>'s third pass fetches the runner's full session
    /// list once per sweep (CARD-0056) and the server log said "46 runner sessions with no DB row at
    /// all" four hours before the human found it. Nothing alerted. This is the alert.</para>
    ///
    /// <para><b>It never reaps.</b> That constraint is inherited from CARD-0056 and is not
    /// negotiable: the false positive that created that card fired on a perfectly healthy session,
    /// and the unclaimed session was the operator's own live conversation. Unclaimed is a reason to
    /// TELL somebody, never a reason to kill.</para>
    ///
    /// <para>Deduped by <c>PtyHostCensusAlertState</c> — one raise per repeat window while the
    /// condition holds, and an escalation always goes through — so it cannot become the CARD-0101
    /// refusal storm (37 identical Warnings in three hours, nobody acted, then silence read as a
    /// fix).</para>
    /// </summary>
    PtyHostCensusDiverged = 28,

    /// <summary>
    /// The session runner explicitly reported that it cannot tail the transcript format an agent
    /// kind requires. Refusing the launch (and withholding the never-started kill) prevents a
    /// stale daemon from silently destroying a working delegate (CARD-0112).
    /// </summary>
    RunnerBuildStale = 29,

    /// <summary>
    /// Consecutive subscription-usage polls of one session failed (CARD-0143). Warning, never
    /// Critical: a missing quota reading is a convenience, not a broken agent. Deduped per
    /// session and not re-raised until a poll succeeds, so a permanently-broken poll is
    /// visible without writing 48 identical rows a day.
    /// </summary>
    SubscriptionUsagePollDegraded = 30,

    /// <summary>
    /// A queued or Mode.Now body was refused because its first token is in this kind's
    /// <c>LocalCommandContract.Forbidden</c> map — nothing was typed. Codex <c>/usage</c> is the
    /// founding case: it opens a picker whose highlighted option redeems the account's one
    /// usage-limit reset (CARD-0141 / CARD-0137 S3). Error, never Critical, never a kill:
    /// refusing the body is the whole remedy, and retrying it is pointless. The message parks
    /// immediately. No migration: stored as an int on the existing column.
    /// </summary>
    ForbiddenTerminalBody = 31,

    /// <summary>
    /// A Dispatched/Working task whose session is mid-turn, still writing transcript rows, and
    /// has made no novel progress for <c>StallDetection.StallMinutes</c> (CARD-0153). "Novel"
    /// means a fingerprint (tool name+input, tool-result text, assistant text) not already seen
    /// in the look-back window, or a file/commit in the worktree newer than that. Thinking is
    /// never progress; a user prompt always is.
    ///
    /// <para><b>Detection only. It never kills, never auto-escalates, never auto-compacts, never
    /// retypes.</b> Five of the last twelve reliability cards in this repo are a kill or a retype
    /// fired on evidence that turned out to be wrong; a stall detector's evidence is weaker than
    /// any of those — "nothing new for a while" is exactly what a slow, legitimate task also
    /// looks like — so this kind gets a row the operator can act on, not a trigger finger.</para>
    ///
    /// <para>Warning at the stall threshold; re-raised as Error once
    /// <c>EscalateToErrorAfterMinutes</c> (default 90) has passed; Critical only when the owning
    /// agent is channel-bound, and only at that Error step. Deduped per stall episode against
    /// the newest row of this kind on the same session: same episode is not re-raised unless the
    /// severity step changed; a novel row then another stall is a new episode. Survives a
    /// server restart (no in-memory set). No migration: stored as an int on the existing
    /// column.</para>
    /// </summary>
    TaskProgressStalled = 32,

    /// <summary>
    /// An operator (or script) launched anyway after the CARD-0136 quota gate tripped,
    /// via <c>ignoreSubscriptionQuota</c>. Warning, never Critical: they chose this.
    /// Not deduped — every override is a separate decision. One row per overridden start.
    /// </summary>
    SubscriptionQuotaOverridden = 33,
}
