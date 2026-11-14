# rc run 34d41b7e, group 90: three failing classes (Windows)

Read-only extract. Run 34d41b7e, rc/2026-11-20 = `b559bfba2e9a420495122d27171352fe9b7e1ba4`, desktop Windows 10.0.19045, .NET 9.0.23, TUnit 1.44.
The run checkout `C:\Antiphon\releases\checkout-8e08368a` is at HEAD `b559bfba2e9a420495122d27171352fe9b7e1ba4`.
The sources read were `antiphon-all-group-90.trx`, `antiphon-all-group-90-tests.log`, `antiphon-all-group-90.timing.json` and `antiphon-all-group-90-execution\log_261114141536641.diag`, all in `C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e\`.
The TUnit diag file has no application log lines, and the TRX stdout of each failed test holds only its `ANTIPHON_TEST_PHASE_ID`.

## Chunk summary

| | |
|---|---|
| Totals | 383 run, 380 passed, 3 failed, 0 skipped. Run wall 4m 11s (252.3 s) |
| Started / ended | 2026-11-14T14:15:36Z to 14:19:49Z (build 583 s before) |
| Timing | p50 0.236 s, p95 3.409 s, max 30.8 s (`Input_body_upgrade_preserves_a_preexisting_event_with_null_body`) |
| Classes | 30 Application classes |

None of the three test files has changed between `b559bfba2` and master `8cec1e74e`.

## Verdict

All three failures are **deterministic stale-contract** failures caused by CARD-1892.
Commit `5b80144d3` ("retain live sessions and attempts after ambiguous Input") changed the Input-boundary catch in `SessionMessageQueueService`.
An `IOException`, `HttpRequestException`, `TimeoutException` or `OperationCanceledException` at that boundary used to revert the row to Pending. So did a PhoneHome `RequestTimeout` or `ConnectionClosedInFlight`. Now each of them leaves the row **Sent with a null verdict**, waiting for transcript evidence (`InputDeliveryUncertainException.Is`).
Related CARD-1892 composer-less behaviour comes from `ecc9eb971` and `4e4362bec`.
This is the same pattern that the C1196 repair `8cec1e74e` fixed for group 54.

None of the three is a load or timing failure:
- No assertion is a poll expiry, timeout or sharing violation.
- Each duration is at or below the durations of its passing siblings.
- Two of the three failed alone (nothing else ran in the window).
- Two of them were also red on Linux at master `8c06c7e11`, which contains `5b80144d3`, in Code task c995680c. The third was reproduced on Linux by the CARD-1892 sweep 15406ecc as CARD-1924.

One of the three, `Reservation_and_real_queue_flush_serialize_in_both_orders(True)`, also shows a **possible real guard gap**. Debug has to decide it (see below).

## 1. PhoneHomeOutageMentionTests.In_flight_mention_Enter_late_confirms_without_retyping

- **Duration:** 3.062 s, 14:16:13.614 to 14:16:16.676.
- **Assertion** (`PhoneHomeOutageMentionTests.cs:176`). Line 186 in the trace is only the async `await using` frame.
  ```
  ShouldAssertException: attempted.Status
      should be
  QueuedMessageStatus.Pending
      but was
  QueuedMessageStatus.Sent
  ```
- **Expected / actual:** expected Pending, actual Sent.
- **What happens:** the Enter (`"\r"`) is sent through the phone-home client. The peer goes silent, and the harness inserts UserPrompt and TurnEnd and then calls `DisconnectAsync` while that send is in flight. The send therefore fails with a phone-home in-flight close or transport error. That falls into `InputDeliveryUncertainException.Is`, so since CARD-1892 the row is kept as Sent with a null verdict. Before CARD-1892 it was reverted to Pending.
  - Lines 177-185 still state the intended outcome: attempts == 1, a baseline is present, recovery gives `LateConfirmed` with no retyping, and peer b sees 0 Input.
  - Only the intermediate `Pending` expectation is stale.
- **Ran at the same time** (parallel in-process, all passed):
  - `DelegateScriptStartRefTests` (C613 x3)
  - `GitIndexLockPathTests` x2
  - about 20 `GrokRulesInitializationTests` rows
  - `GrokRulesTransportCompatibilityTests`
  - `HerdrSupervisionFailureEvidenceTests`
  - `LandFailureDiagnosticTests`
  - `ModelLevelAliasDisplayTests`
  - `OutboundConversionPublicCreateTests` (9.7 s)
  - `WorkspaceInfoGitIntegrationTests`
- **Passing siblings (7/8):**
  - `Queued_activity_failure_does_not_duplicate_or_block_recovery` (2.99 s)
  - `Split_line_and_completed_newline_accept_one_occurrence` (1.88 s)
  - `Accepted_mention_keeps_original_target_after_source_ends` (1.88 s)
  - `C696Red_Mention_before_first_List_is_persisted` (1.54 s)
  - `Busy_local_and_remote_mentions_wait_for_idle` (2.95 s)
  - `C696Red_Mention_in_reconnect_gap_survives_restart` (1.46 s)
  - `Occurrence_replay_is_idempotent_but_identical_new_mentions_are_distinct` (3.41 s)

  This is the only sibling whose Input transport fails in flight. Every sibling that never loses an Input reply passes.
- **Class:** stale-contract, deterministic. It asserts on persisted row state and has no poll.
- **Same as the c995680c failure?** Yes.
  - The c995680c (CARD-1912) report lists "PhoneHome in-flight mention" among 12 failures that were already there at master base `8c06c7e11` on Linux, alongside `Reservation_and_real_queue_flush_serialize_in_both_orders(True)`.
  - `In_flight_mention_Enter_late_confirms_without_retyping` is the only in-flight mention test in the class.
  - `8c06c7e11` contains `5b80144d3` and is an ancestor of the rc.
- **Repair direction:** change the line-176 expectation to the CARD-1892 contract: `Sent` with `DeliveryVerdict == null`, attempts 1 and baseline set. Keep lines 177-186 unchanged.
  - Do not turn the fault into a definitive refusal. Unlike C1196, here the Enter really is in flight, so the uncertain path is the one under test.

## 2. GrokRulesCompactionRecoveryTests.Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(in-flight, standing)

- **Duration:** 0.579 s, 14:18:52.261 to 14:18:52.840. That is about half of every passing sibling (1.08-1.88 s): the flush returned without typing, so there was no delivery wait.
- **Assertion** (`GrokRulesCompactionRecoveryTests.cs:172`):
  ```
  ShouldAssertException: h.Adapter.SubmittedBodies
      should have single item but had
  0
      items and was
  [] (System.Collections.Generic.List`1[System.String])
  ```
- **Expected / actual:** expected exactly one submitted body, starting with the refresh header. Actual: zero.
- **Assertions that passed** before line 172 (only the in-flight lane reaches 164-170):
  - line 164: nothing typed before the prior delivery finishes
  - line 165: the prior row's attempts == 1
  - line 170: the prior row's verdict is `LateConfirmed`
- **Mechanism:** the in-flight lane seeds the prior row as `Sent` with attempts 1 and baseline 0, which is the composer-less uncertain-attempt shape. After CARD-1892, the flush that late-confirms the uncertain attempt is receipt-only: it settles the prior row and does not move on to type the next body (the refresh) in the same flush. The test still expects the refresh to be typed by that same `FlushSessionAsync` (line 169).
- **Ran at the same time:** nothing. The class is `[NotInParallel]` and the TRX shows no overlapping test.
- **Passing siblings (10/11):**
  - `(startup, standing)`, `(startup, retired-pool)`, `(startup, channel)`
  - `(sync, channel)`, `(sync, retired-pool)`, `(sync, standing)`
  - `(live, retired-pool)`, `(live, channel)`, `(live, standing)`
  - `(blocked-herdr, standing)`

  Only the `in-flight` lane fails, and it is the only lane that seeds an already-attempted Sent row.
- **Real clock:** the class uses the real-clock rules service, as g83 does, but this is not the g83 1 s budget pattern. No deadline or poll is involved, the failure is faster than its siblings, and the outcome is an empty list, not an expiry.
- **Class:** stale-contract, deterministic.
- **Linux comparison:** the CARD-1892 sweep 15406ecc already reproduced this on Linux. Its row F001 (GrokRules*) had 1 failure, filed as **CARD-1924** with the same line, the same message and the same passing `LateConfirmed` assertion. Its classification: "composerless recovery deliberately makes that flush receipt-only … preserve the no-write boundary and drive a subsequent safe flush in the repair."
- **Repair direction:** CARD-1924. After line 170, run a second `FlushSessionAsync`, or the turn-end flush, before line 172. Keep the line 164 no-write boundary.

## 3. StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(True)

- **Duration:** 0.410 s, 14:19:41.827 to 14:19:42.238. The `(False)` arm passed in 0.387 s.
- **Assertion** (`StandingSessionSwitchConcurrencyTests.Delivery.cs:74`; line 93 is the `finally` frame):
  ```
  ShouldAssertException: Task `selection!`
      should throw
  Antiphon.Server.Application.Exceptions.ConflictException
      but did not
  ```
- **Expected / actual:** expected `ConflictException` with code `standing_resume_delivery_pending`. Actual: the history selection succeeded.
- **What passed first:** the line 69 contention witness passed. Selection really blocked on the queue semaphore for 150 ms while delivery held the gate. Serialization therefore still works; only the post-flush refusal is missing.
- **Mechanism:**
  1. `deliveryWins=true` makes the source adapter throw `IOException("synthetic in-flight transport refusal after the durable claim")`.
  2. Before CARD-1892, the generic catch reverted the row to Pending with attempts > 0.
  3. Since CARD-1892, `InputDeliveryUncertainException.Is(IOException)` is true, so the row stays **Sent** with a null verdict.
  4. The switch guard at `server/Application/Services/AgentControlService.cs:684-687` only loads `Status == Pending && RulesRefreshKey == null` rows. A Sent row whose delivery is unresolved is never examined, so no `standing_resume_delivery_pending` is thrown and the selection proceeds.
- **Ran at the same time:** nothing (no TRX overlap).
- **Passing siblings (23/24):**
  - `Reservation_and_real_queue_flush_serialize_in_both_orders(False)`
  - `Reservation_rechecks_changes_committed_after_runner_preflight` (generation, pointer, execution, delivery, owner, card)
  - `Failure_after_reservation_writes_rolls_back_owner_pointer_hold_queue_and_decision`
  - `Concurrent_starts_and_supervisor_reserve_one_generation` (selection, default, automatic, fresh)
  - `Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation` (saved-running, before-spawn, start-rpc)
  - `Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation`
  - `Obsolete_queued_launch_cannot_overwrite_a_newer_outcome` (Running, Failed, Stopped x False, True)
  - `Stop_supersedes_launch_before_spawn_and_before_typing`

  The `Reservation_rechecks…(delivery)` row passes because it seeds an attempted **Pending** row, which the guard still refuses.
- **Class:** stale-contract, deterministic. It is also on the c995680c Linux base-failure list at `8c06c7e11`.
- **Possible real defect:** the guard's own message is "Resolve attempted pending input … before switching", and the test comment says "History selection must then wait and refuse the attempted input". Under the new contract, an attempted-but-unresolved row is Sent with a null verdict, and the guard lets a standing switch proceed past it. The row stays bound to the source (line 83 would still hold), so input does not cross conversations. But the switch does leave an unresolved attempt behind.
- **Decision for Debug.** Choose one:
  - (a) Product fix: widen the guard so it also refuses source rows that are `Sent` with a null `DeliveryVerdict` and attempts > 0. This matches the test's stated invariant.
  - (b) Test fix: make the fault a definitive refusal, as C1196 did. This loses the in-flight-after-durable-claim witness.

  The recommendation is (a), or at minimum a red witness before choosing (b).

## Cross-cutting

| Test | Duration | Same-class siblings | Concurrency | Linux | Class |
|---|---:|---|---|---|---|
| PhoneHome In_flight_mention_Enter_late | 3.06 s | 7/8 pass (1.46-3.41 s) | parallel, all neighbours green | red at master 8c06c7e11 (c995680c) | stale-contract (CARD-1892 Sent-hold) |
| GrokRules …(in-flight, standing) | 0.58 s | 10/11 pass (1.08-1.88 s) | alone | red, CARD-1924 (sweep 15406ecc F001) | stale-contract (CARD-1892 receipt-only flush) |
| Reservation_…(True) | 0.41 s | 23/24 pass | alone | red at master 8c06c7e11 (c995680c) | stale-contract, plus a possible guard gap (decision) |

**Comparison with the CARD-1892 sweep.** At its tip `e16346cfa`, the sweep doc lists CARD-1922 (C1196), CARD-1923 (C1029), CARD-1924 (this Grok row), CARD-1926 (C0824 lease) and a ClaudeAuthProbe isolation candidate. It does **not** yet list `PhoneHomeOutageMentionTests` or `StandingSessionSwitchConcurrencyTests`. Both are new families for the sweep, and both are already red on Linux at master according to c995680c.

**Why missed (for the Debug's note).**
- The behaviour change is in the catch of `SessionMessageQueueService.cs`.
- The `input-delivery-uncertainty` impacted-classes row added by `8cec1e74e` globs only `InputDeliveryUncertainException.cs`. It names `AgentTaskMidTurnRefineTests`, `LandRefusalDeliveryWitnessTests` and `SessionMessageQueuePhoneHomeDropTests`. None of the three classes here is in it.
- The sweep found 257 production consumers that were missing from CARD-1892's Code evidence class list.
- Fix the selection: add these three classes to that row, or glob the queue service's uncertainty path.
