using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S1-S5: the dispatcher's boot disposition on a real sweep. Design V-3..V-9, V-12,
/// V-13, V-15, V-16. Fixture: the isolated PostgreSQL schema, the
/// <c>BootStallWorkingTickCharacterizationTests.BootStallWorld</c> service graph (moved to a
/// shared helper by S1), a <c>FakeTimeProvider</c> instead of the system clock, a
/// <c>ListedInventoryRunner</c> plus an <c>ISessionRunnerDirectory</c> stub for the owning
/// inventory, a <c>RecordingSessionStopper</c>, and a <c>StubWorkspaceProgressProbe</c>. The
/// shipped eight-minute boot wait, twenty-minute model wait and 240-minute ceiling stay in force;
/// elapsed time is arranged by back-dating rows and advancing the fake clock, never by loosening
/// a deadline. Every method asserts the destructive counters (stopper, runner kills, starts,
/// releases, compaction stops, inputs) and the task identity (status, attempt, concurrency
/// token, session binding, failure fields) as well as the named outcome.
/// </summary>
[Category("Integration")]
public class BootStallDetectionTests
{
    /// <summary>
    /// V-3. Each shape is DetectOnly: no status/attempt/token change, no Failed or Retried event,
    /// stopper empty, runner destructive counters zero, the named reason retained in the log.
    /// working-listed and working-empty-list: true Working wins before inventory is read (zero
    /// inventory calls). idle-listed-running, idle-listed-exited, idle-wrong-generation: an
    /// explicit interrupt marker after the prompt makes Working false; any listing, any status,
    /// any generation vetoes. unavailable-remote, missing-directory, null-list: unknown inventory
    /// vetoes. terminal-row-reconciler-owned: the session row is Stopped with EndedAt; the boot
    /// sweep writes no boot Warning and changes nothing (A-1: the dead-session reconciler owns
    /// terminal rows). dispatched-pending-brief: a Dispatched task whose brief row is Pending
    /// while the session answers another prompt; the boot sweep writes no boot Warning (A-2: the
    /// delivery watchdog owns a task whose brief is still Pending). dispatched-sent-brief-detects:
    /// the real-world shape, a Dispatched task whose brief row is Sent and whose prompt is
    /// transcript-confirmed; DetectOnly with exactly one BootStallDetected Warning and no
    /// failure (task status never gates detection; the brief row does).
    /// </summary>
    [Test]
    [Arguments("working-listed")]
    [Arguments("working-empty-list")]
    [Arguments("idle-listed-running")]
    [Arguments("idle-listed-exited")]
    [Arguments("idle-wrong-generation")]
    [Arguments("unavailable-remote")]
    [Arguments("missing-directory")]
    [Arguments("null-list")]
    [Arguments("terminal-row-reconciler-owned")]
    [Arguments("dispatched-pending-brief")]
    [Arguments("dispatched-sent-brief-detects")]
    public Task C1151_Listed_or_unknown_session_is_untouched(string shape) =>
        Card1151Pending.Skip("S1", nameof(C1151_Listed_or_unknown_session_is_untouched));

    /// <summary>
    /// V-4. An unresolved prompt-only Working boot stays open (Working, attempt 1, same binding,
    /// FailureCode null, no Failed event, stopper empty) under every other clock: general-20m
    /// (model wait breached), ceiling-240m (role ceiling breached), custom-ceiling-earlier (a
    /// RolePolicy ceiling of 5 minutes), model-wait-shorter-than-boot (ModelWait 5, Boot 8),
    /// workspace-progress (probe reports a file change; no early failure, no late failure
    /// either), boot-notification-disabled (BootModelWaitDeadlineMinutes 0: no 8-minute event,
    /// the 20-minute operator event instead, still no failure). model-reply-returns-to-
    /// ordinary-policy: one Thinking row after the prompt returns the task to the ordinary
    /// non-killing general failure at 20 minutes (today's
    /// a_session_that_produced_one_thinking_row_takes_the_old_non_killing_failure), stopper
    /// still empty.
    /// </summary>
    [Test]
    [Arguments("general-20m")]
    [Arguments("ceiling-240m")]
    [Arguments("custom-ceiling-earlier")]
    [Arguments("model-wait-shorter-than-boot")]
    [Arguments("workspace-progress")]
    [Arguments("boot-notification-disabled")]
    [Arguments("model-reply-returns-to-ordinary-policy")]
    public Task C1151_Boot_protection_survives_all_deadlines(string clock) =>
        Card1151Pending.Skip("S1", nameof(C1151_Boot_protection_survives_all_deadlines));

    /// <summary>
    /// V-5. Operator threshold max(bootDueAt, promptAt + 20 min). before-threshold: one
    /// BootStallDetected Warning, no NeedsOperator. at-threshold: exactly one
    /// BootStallNeedsOperator at equality on the fake clock. after-threshold: still exactly one.
    /// restart-after-threshold: a new service provider and dispatcher scope after the threshold
    /// finds the durable events and writes nothing more. clock-rewind-after-detection: the fake
    /// clock steps back below the threshold after detection; no new episode key, no second
    /// Detected event, no failure; stepping forward again writes the single operator event.
    /// Every argument: same attempt, same token, same binding, no input, stopper empty, the
    /// Warning Detail carries the episode key fields and never the prompt text canary.
    /// </summary>
    [Test]
    [Arguments("before-threshold")]
    [Arguments("at-threshold")]
    [Arguments("after-threshold")]
    [Arguments("restart-after-threshold")]
    [Arguments("clock-rewind-after-detection")]
    public Task C1151_Operator_escalation_preserves_the_attempt(string moment) =>
        Card1151Pending.Skip("S2", nameof(C1151_Operator_escalation_preserves_the_attempt));

    /// <summary>
    /// V-6. Injected faults in the separate telemetry context: event-read (duplicate-key read
    /// throws), insert, commit, publish (event bus throws), later-unrelated-save (the sweep's own
    /// context saves an unrelated row after a failed telemetry transaction). Decisive: the
    /// disposition is unchanged (Working, attempt 1, no Failed/Retried event, stopper empty), no
    /// Warning row leaks through the later save, the sweep reports zero failures for the tick,
    /// and the next tick records the event once.
    /// </summary>
    [Test]
    [Arguments("event-read")]
    [Arguments("insert")]
    [Arguments("commit")]
    [Arguments("publish")]
    [Arguments("later-unrelated-save")]
    public Task C1151_Telemetry_failure_never_changes_disposition(string fault) =>
        Card1151Pending.Skip("S2", nameof(C1151_Telemetry_failure_never_changes_disposition));

    /// <summary>
    /// V-9. repeated-tick: three ticks, one BootStallDetected. service-recreation: a second
    /// provider over the same schema, still one. concurrent-contexts: two scoped dispatchers
    /// ticking concurrently on a barrier, still one (the task lock serializes them).
    /// later-real-prompt: a second real UserPrompt after the first episode is recorded creates
    /// exactly one new key and one new Detected event; the old key gets nothing further.
    /// </summary>
    [Test]
    [Arguments("repeated-tick")]
    [Arguments("service-recreation")]
    [Arguments("concurrent-contexts")]
    [Arguments("later-real-prompt")]
    public Task C1151_Warnings_deduplicate_per_episode(string shape) =>
        Card1151Pending.Skip("S2", nameof(C1151_Warnings_deduplicate_per_episode));

    /// <summary>
    /// V-7 (conditional on decision Q-1 keeping the plan's safe-absent branch). The only fixture
    /// that satisfies the whitelist: a Running session row, a real prompt followed by an explicit
    /// interrupt marker (Working false by the shared query), an owning-runner inventory that
    /// positively lacks the id, a directory-resolved runner whose transcript read answers
    /// TerminalComplete for the same AcceptedStartedAt, a quiet available workspace, no
    /// obligations. No real runner produces this pair (F-1), so the fixture is explicitly
    /// contradictory. first-attempt-retries-once: Failed ProviderUnresponsive then Queued attempt
    /// 2 at the same kind/tier, one ProviderUnresponsive incident, no alias hold.
    /// exhausted-attempt-fails-once: attempt 2 stays Failed, reason names the alias, no third
    /// attempt. retry-infrastructure-error-stays-failed: the internal requeue throws; Failed is
    /// retained and a warning is logged. second-absent-failure-holds-alias: a second proven-absent
    /// failure on the same kind/alias inside BootStallRepeatHoldMinutes places the AutoDetected
    /// hold (the ledger counts committed failures only). All four: stopper empty, runner Kills/Releases/
    /// CompactionStops 0, no KillAsync compensation.
    /// </summary>
    [Test]
    [Arguments("first-attempt-retries-once")]
    [Arguments("exhausted-attempt-fails-once")]
    [Arguments("retry-infrastructure-error-stays-failed")]
    [Arguments("second-absent-failure-holds-alias")]
    public Task C1151_Safe_absent_boot_keeps_failure_and_retry(string attempt) =>
        Card1151Pending.Skip("S3", nameof(C1151_Safe_absent_boot_keeps_failure_and_retry));

    /// <summary>
    /// V-8 (conditional on Q-1). The V-7 fixture, with the named change applied by an interceptor
    /// between the first evidence pass and the final recheck under the queue gate and task lock:
    /// new-working-row (an AssistantText lands), inventory-entry (the owning inventory now lists
    /// the id), generation-change (StartedAt moves), attempt-change (Attempt bumped). Decisive:
    /// no Failed status, no Retried event, no requeue, stopper empty, the task row as the
    /// interceptor left it.
    /// </summary>
    [Test]
    [Arguments("new-working-row")]
    [Arguments("inventory-entry")]
    [Arguments("generation-change")]
    [Arguments("attempt-change")]
    public Task C1151_Race_revokes_absent_failure(string race) =>
        Card1151Pending.Skip("S3", nameof(C1151_Race_revokes_absent_failure));

    /// <summary>
    /// V-12. A detected Working task keeps its seat: no AgentTaskPark row, no RunnerSeatRelease
    /// row, runner Releases 0, stopper empty, task Working, with BlockedTaskParking Enabled false
    /// (parking-off) and true (parking-on). The session-scoped BootReplyWatchdogService sweep in
    /// the same fixture raises no LivenessProbeFailed incident and calls no stopper because the
    /// task stays open.
    /// </summary>
    [Test]
    [Arguments("parking-off")]
    [Arguments("parking-on")]
    public Task C1151_Detection_does_not_release_or_park(string parking) =>
        Card1151Pending.Skip("S5", nameof(C1151_Detection_does_not_release_or_park));

    /// <summary>
    /// V-13. FullCommandCounter over every context (the sweep's and the telemetry writer's) plus
    /// runner call counters, on the ten paths of the design's statement table. Code pins the
    /// measured exact totals; TestDesign supplies the inherited prefix and the per-path cap, and
    /// each argument prints its full roster. young-preview: 0 delta. working-first-detection: 3.
    /// working-repeated-episode: 2 (A-7: no runner pull, no second evaluation). operator-
    /// escalation: 3 plus one runner pull. identity-changed-before-event: 1. event-save-fault: 3.
    /// non-working-listed: 5 and one inventory read. absent-proof-refused: 8, two inventory reads,
    /// one transcript read. safe-absent-final-revalidation and safe-absent-race-abort: 10, two
    /// inventory reads, one transcript read (both conditional on Q-1).
    /// </summary>
    [Test]
    [Arguments("young-preview")]
    [Arguments("working-first-detection")]
    [Arguments("working-repeated-episode")]
    [Arguments("operator-escalation")]
    [Arguments("identity-changed-before-event")]
    [Arguments("event-save-fault")]
    [Arguments("non-working-listed")]
    [Arguments("absent-proof-refused")]
    [Arguments("safe-absent-final-revalidation")]
    [Arguments("safe-absent-race-abort")]
    public Task C1151_Boot_branch_statement_counts(string path) =>
        Card1151Pending.Skip("S5", nameof(C1151_Boot_branch_statement_counts));

    /// <summary>
    /// V-15. The delivery watchdog's stopper stays conditional. real-idle-failure-cleans-up: a
    /// Dispatched task, Pending brief, idle session, ten minutes: Failed and the existing kill.
    /// working-withholds: the same with a Working transcript: Failed, no kill. stale-or-
    /// unsuccessful-failure-withholds: the Failed write is refused by a concurrency fault; no
    /// kill. In every argument a BootStallDetected Warning present on the task is never read as
    /// a delivery failure and never routes to the stopper.
    /// </summary>
    [Test]
    [Arguments("real-idle-failure-cleans-up")]
    [Arguments("working-withholds")]
    [Arguments("stale-or-unsuccessful-failure-withholds")]
    public Task C1151_Delivery_watchdog_stopper_requires_real_safe_failure(string shape) =>
        Card1151Pending.Skip("S5", nameof(C1151_Delivery_watchdog_stopper_requires_real_safe_failure));

    /// <summary>
    /// V-16. A detected Working task retried by the explicit AgentTaskService.RetryAsync entry
    /// still stops the delegate (stopper contains the session) and requeues at the same tier;
    /// the internal absent-proof requeue entry is not reachable from the public Retry, Reroute
    /// or Escalate requests (no public parameter, no HTTP-bound bool).
    /// </summary>
    [Test]
    public Task C1151_Explicit_retry_retains_operator_semantics() =>
        Card1151Pending.Skip("S3", nameof(C1151_Explicit_retry_retains_operator_semantics));
}
