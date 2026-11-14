# Run 34d41b7e group 54: LandRefusalDeliveryWitnessTests C1196 pretyping failure (Windows)

Read-only extract. Task cab13091. Run 34d41b7e (C487_INVOCATION 34d41b7e3ccc4ad4b29ed259749ec847) on
rc/2026-11-20 = b559bfba2e9a420495122d27171352fe9b7e1ba4, desktop Windows 10.0.19045, .NET 9.0.23,
TUnit 1.44.0.0. Source read in this worktree at the same SHA.

Logs read (no other files enumerated), under
`C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e\`:
`antiphon-all-group-54.trx` (307810 B), `antiphon-all-group-54-tests.log` (3417 B),
`antiphon-all-group-54.timing.json`, `antiphon-all-group-54-execution\log_261114122926641.diag`.

## Verdict

**Deterministic. The test is stale against the CARD-1892 Input-uncertainty contract; production
behaviour is as designed.** Same root and same fix shape as the CARD-1916 transport arm repaired in
0bdb6cb94 (see `docs/investigations/2026-11-14-card-1916-1919-inherited-reds.md`). Not timing or load.

## Failure

Chunk totals: 168 executed, 167 passed, 1 failed, 0 skipped; wall 3m34s (run 12:29:26Z-12:33:01Z),
build 583 s, p95 10.2 s.

`Antiphon.Tests.Application.LandRefusalDeliveryWitnessTests.C1196_Pretyping_transport_failure_recovers_the_same_row`
- Duration 0.581 s (12:30:48.165Z - 12:30:48.746Z). Timeout attribute 180 s: nowhere near.
- Assertion (Shouldly), message tag `C1196:pretyping-reverted`:

```
ShouldAssertException: (reverted.Status == QueuedMessageStatus.Pending
            && reverted.Attempts == 1
            && reverted.Baseline is not null
            && reverted.StartedAt is not null)
    should be
True
    but was
False

Additional Info:
    C1196:pretyping-reverted
```

- Expected: keyed queue row reverted to `Pending` with one charged attempt and kept baseline/start.
  Actual: the compound predicate is false (Shouldly does not print which conjunct). From the product
  path below the row is `Sent`, null verdict, attempts 1, baseline and start retained, so the
  `Status == Pending` conjunct is the false one.
- Stack top frames:
  - `LandRefusalDeliveryWitnessTests.C1196_Pretyping_transport_failure_recovers_the_same_row(CancellationToken ct)`
    in `...\tests\Antiphon.Tests\Application\LandRefusalDeliveryWitnessTests.cs:514`
  - same method `:533` (async rethrow frame; 533 is `ct.ThrowIfCancellationRequested()`, not a second fault)
  - TaskAwaiter / ExceptionDispatchInfo rethrows; TUnit internals omitted.
- Stdout: only `ANTIPHON_TEST_PHASE_ID ...C1196_Pretyping_transport_failure_recovers_the_same_row(...)`.
  No stderr. The diag log has only the TestNode state-change records for this test (lines 1048-1099),
  no application log lines, no sharing violation, no timeout, no cancellation.
- Earlier assertions in the same test passed: history key present, zero submissions before wakeup,
  wakeup consumed, `world.Fault.Fired == 1`, keyed row exists. So the fault fired once and the queue
  row was found; only its post-fault state differs.

## Cause

The harness cut `RefusalCut.QueueBeforeTyping`
(`tests/Antiphon.Tests/TestHelpers/LandRefusalDeliveryHarness.cs:594-595`) throws
`new IOException("C1196:queue-before-typing")` from `LandDeliveryBoundary.ReachedAsync("queue-before-typing", ...)`.
That boundary is called inside the delivery `try` in
`server/Application/Services/SessionMessageQueueService.cs:2988-2999`, after the row was already
stamped `Sent`, `DeliveryAttempts++`, `LastDeliveryStartedAt`, baseline (lines 2950-2984).

CARD-1892 commit **5b80144d3cc7dcdcdb50c85ee03cd737e1508b41** ("retain live sessions and attempts
after ambiguous Input") added `InputDeliveryUncertainException.Is`
(`server/Application/Exceptions/InputDeliveryUncertainException.cs:13-19`), which classifies
`OperationCanceledException`, `TimeoutException`, `HttpRequestException`, **`IOException`** and two
phone-home transport codes as uncertain. The new catch at `SessionMessageQueueService.cs:3005-3024`
keeps such an attempt `Sent` with a null verdict ("retaining N attempt(s) pending transcript
evidence") and returns `FlushResult.Failed` without `RevertRunAsync`. Only the generic catch
(3025-3036, definitive refusals and non-transport errors) still reverts to `Pending`.

So the test's `IOException` now lands in the uncertain branch and the row is held `Sent`. The test
(last edited 2026-10-24, e050c706f; written under CARD-1196 in 9b587bb96/b4f541580) asserts the
pre-CARD-1892 revert-to-Pending behaviour, and its second half (restart, `FlushStrandedAsync`,
`retried.Attempts == 2`, second attempt delivered) also expects the automatic resend that the
CARD-1892 contract forbids for an unconfirmed attempt (hold Sent/null verdict, no automatic resend,
a complete UserPrompt confirms; `docs/session-runtime-invariants.md` Input uncertainty section).

Old behaviour expected by the test: pre-typing transport failure -> same row reverted to Pending ->
stranded sweep retries once. CARD-1892 contract: an IOException at the Input boundary is ambiguous ->
row held Sent with null verdict, no resend, confirmed only by a complete UserPrompt.

## Sibling tests (same class, all passed; class runs serially)

24 cases in the class, 23 passed, in this order (all 12:29:38Z-12:30:56Z):
C1196_False_receipt_evidence_keeps_the_refusal_open(no-attempt) 11.12 s;
C1196_Receipt_save_crash_recovers_without_retyping(receipt-before-save) 2.14 s;
C1196_Receipt_save_crash_recovers_without_retyping(queue-before-verdict) 2.68 s;
C1196_Lost_wakeup_is_recovered_by_the_stranded_queue(busy) 3.74 s;
C1196_Lost_wakeup_is_recovered_by_the_stranded_queue(eligible) 3.74 s;
C1196_Eligible_recipient_receipt_resolves_older_refusal(immediate) 3.51 s;
C1196_Enqueue_failure_recovers_the_same_obligation(queue-save) 4.54 s;
C1196_Enqueue_failure_recovers_the_same_obligation(before-enqueue) 3.63 s;
C1196_Busy_recipient_waits_for_turn_end_before_resolution 3.44 s;
C1196_Eligible_recipient_receipt_resolves_older_refusal(restart-after-terminal) 3.09 s;
C1196_False_receipt_evidence_keeps_the_refusal_open(head-only) 2.70 s;
C1196_False_receipt_evidence_keeps_the_refusal_open(sent-without-prompt) 8.98 s;
C1196_Crash_after_queue_insert_recovers_the_existing_key 2.88 s;
C1196_False_receipt_evidence_keeps_the_refusal_open(wrong-session) 8.01 s;
C1196_Receipt_does_not_resolve_another_task_or_newer_refusal(non-outcome) 1.79 s;
C1196_Recovery_link_requires_matching_destination_and_digest(missing-digest) 2.23 s;
C1196_Recovery_link_requires_matching_destination_and_digest(wrong-destination) 1.57 s;
**FAILED** C1196_Pretyping_transport_failure_recovers_the_same_row 0.58 s;
C1196_Unlinked_legacy_note_never_submits 0.46 s;
C1196_Recovery_link_requires_matching_destination_and_digest(wrong-digest) 1.56 s;
C1196_Receipt_does_not_resolve_another_task_or_newer_refusal(equal-time-refusal) 1.44 s;
C1196_Receipt_does_not_resolve_another_task_or_newer_refusal(newer-refusal) 1.38 s;
C1196_Receipt_does_not_resolve_another_task_or_newer_refusal(other-task) 1.48 s;
C1196_False_receipt_evidence_keeps_the_refusal_open(at-baseline) 1.62 s.

What they bound: every other cut passes. `before-enqueue` also throws IOException but at
`AgentTaskLandNotificationService` before any queue row exists (no Input boundary), so it still
recovers. `queue-before-verdict` and the receipt/false-receipt/lost-wakeup cases exercise the same
queue flush after or around delivery and pass, so the harness, database and session fixture are
healthy. Only the one cut that throws an IOException inside the Input `try` fails.

## What ran at the same time

Only one overlapping test in the chunk:
`OrchestratorInstructionsRefreshTests.Failed_reads_are_unknown_in_file_and_notice_then_recover(chain-default, False)`
(12:30:46.959Z-12:30:51.280Z, passed). Unrelated area. The other class in the chunk was
AgentTaskLandApprovalRecoveryTests (94 cases, all passed). Chunk classes: 94 + 24 + 50 = 168.

## Why it was missed

- CARD-1892 checkpoints (`docs/superpowers/plans/2026-11-13-card-1892-round3.md` CP-2..CP-9) select
  GrokRulesInitialization/Failure, PhoneHomeLaunchTransport, SessionMessageQueueInterruptedAttempt,
  RunnerTerminalSessionInputEncoding/Generation, AgentSessionCancelledTurn,
  AgentSessionInterruptedLaunchResume, GrokRulesReadyOrdering, AgentSessionLaunchFailure,
  SessionMessageQueuePhoneHomeDrop, attention classes, SessionMessageQueueCodexPointer and guards.
  LandRefusalDeliveryWitnessTests is not named in any CARD-1892 plan/evidence doc. Reviews
  d552541f (round 1) and caeec914 (round 2) used that CP-2/CP-3 union (per the CARD-1916..1919
  investigation).
- Change-aware selection could not find it: in `tests/derived-impact-index.json`,
  `server/Application/Services/SessionMessageQueueService.cs` is a capped path (150 consumers, cap
  25, 125 owed), and LandRefusalDeliveryWitnessTests is not among the 25 listed. The class does not
  reference `InputDeliveryUncertainException`, so the new exception file carries no edge to it.
  Its only index edge is its own harness `tests/Antiphon.Tests/TestHelpers/LandRefusalDeliveryHarness.cs`.
- The CARD-1916..1919 Debug (55b46f7e) repaired the same IOException/HttpRequestException pattern in
  AgentTaskMidTurnRefineTests but ran three affected classes plus a seven-class guard union; it did
  not sweep other fixtures that inject a transport exception inside the queue Input `try`. Its
  bisect identified 5b80144d3 as first bad for that pattern; this failure is the same first-bad
  commit by construction (the IOException classification is introduced there).

## Recommended Debug repair (not done here; read-only task)

Follow the CARD-1916 precedent (0bdb6cb94), choose one, test-only:

1. Keep the test's intent (definitive pre-typing failure reverts the same row and the stranded sweep
   retries once): make the `QueueBeforeTyping` cut throw a definitive non-transport refusal (for
   example `RunnerProblemException(409, ..., "fixture_input_refused")` or another non-uncertain
   exception) instead of `IOException`, rename the test accordingly, and keep every existing
   Pending/attempt/retry assertion unchanged. `BeforeEnqueue` can keep IOException (it is outside the
   Input boundary).
2. Additionally (optional) add an ambiguous arm asserting the CARD-1892 hold: Sent, null verdict,
   attempts 1, no automatic resend after restart/flush, note stays unconfirmed until a complete
   UserPrompt.

Do not change product code, timeouts or loosen assertions. Regenerate the derived impact index if the
fixture gains a new type dependency (as 101b9dae9 did for CARD-1916). Narrow verification:
`--treenode-filter "/*/*/LandRefusalDeliveryWitnessTests/*"` plus `DerivedImpactIndexTests`.
Also worth a sweep: other harness cuts/fixtures that throw IOException, HttpRequestException,
TimeoutException or OperationCanceledException from inside `SessionMessageQueueService`'s Input
`try` and then assert Pending.
