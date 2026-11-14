# CARD-1892 g90 Final Review

Review task `3afa3cfa`. Subject Debug task `9c1feeba-6f57-4d75-a55c-816d68525f57`, tip `ec0bb426228e410e50065789af8c980aaf2afaf7`, range `8cec1e74e75819ef20756c228add987ce4ae4170..ec0bb426228e410e50065789af8c980aaf2afaf7`. Round 1, Final, full ordinary scope. Product commit under review: `dcb068f89c33caebaa543f0413fd254e501b1261`.

Verdict: one blocker. The widened switch guard behaves as specified for a live unresolved attempt, and a never-confirmable Sent row on that session has no operator control that clears it.

## D-1 Blocker: stuck Sent row has no operator resolution

Where: `server/Application/Services/AgentControlService.cs` lines 688-692, together with `SessionMessageQueueService.CancelAsync` (Pending only), `BuildQueueDtoAsync` (Pending only), `HoldComposerlessUnknownLockedAsync`, `RecordTransportFailureAsync`, `AttentionService.BuildRecentIncidentItemsAsync` (OpenAgent only), `DataRetentionService` (persistent-session exclusion), and `StandingSessionRecovery` fresh/select, which call the same start.

Failure: While the agent's persistent session has a queue row with `Status=Sent`, `DeliveryVerdict=null`, and `DeliveryAttempts>0`, fresh start, history select, and automatic fresh return HTTP 409 `standing_resume_delivery_pending` ("Resolve attempted pending input in /sessions/{id}/queue before switching."). A row that can never late-confirm stays in that state. The queue named by the 409 lists Pending rows only. `DELETE /api/sessions/{id}/messages/{messageId}` on that Sent id is a silent success: `CancelUnderLockAsync` returns false, and the response is the unchanged Pending queue. Composerless recovery (any running kind other than Claude and Codex) records an incident after the interrupted-attempt age and returns without reverting the row. The incident text asks for explicit recovery. The attention action is OpenAgent. `RecordTransportFailureAsync` writes `DeliveryTransportFailed` without `FailureReason=input-delivery-unknown` and does not change the row; that notice leaves the 24-hour window while the row remains. Same-session resume is allowed and leaves the row Sent. Stopping the session removes it from the composerless sweep. Retention will not delete a session that is still some agent's `PersistentSessionId`. Start fresh and Select in the recovery dialog re-enter the same 409. Automatic fresh, when the supervisor attempts it, holds `AutomaticFreshRefused` and tells the operator to inspect pending input; that hold lists Pending ids only.

Claude and Codex can later revert the same row to Pending inside `RecoverDeliveryRunLockedAsync` when the delivery generation changes or the composer reads Clear after the age gate. A same-session resume bumps `StartedAt`, which is one way that generation changes. The reverted Pending row is then listed and cancellable. That path does not run for a composerless kind, and it does not run while the session is stopped. A session that cannot be started never reaches it.

Why: The widened guard is durable. No existing control writes a verdict on that Sent row, cancels it, or overrides the switch. The accepted decision was an attention hold and no production timer. The dead or abandoned case does not get an action that performs the recovery the incident names.

Fix: Add an operator-visible abandon or verdict for that Sent row, or an audited switch override, and show the row on the queue the 409 names. Do not add a production timer.

## What the guard does correctly

The new predicate is session-scoped: `AgentSessionId == sourceId`, `Status == Sent`, `DeliveryVerdict == null`, `DeliveryAttempts > 0`. It runs inside the same transaction that updates the pointer, after the process-local queue locks for the involved sessions and `SELECT FOR UPDATE` on those sessions. Confirmed `Delivered` and `LateConfirmed` rows do not block. An unconfirmed row on the selected session does not block. Resuming the source conversation does not block. Pending rows stay on the previous `NeverAttempted` check. `Reservation_and_real_queue_flush_serialize_in_both_orders(true)` passed inside the 28 switch tests: an in-flight lost reply and the switch serialize, and the switch is refused.

`IX_SessionQueuedMessages_AgentSessionId_Status_Sequence` leads the lookup. On the shared test Postgres, a 100,000-row clone planned as a bitmap index scan on that prefix, then a filter on null verdict and attempts, about 0.1 ms and 4 buffer hits. The scratch table was dropped.

HTTP status is 409 (`ConflictException`).

## Assertion inventory

No assertion was removed.

`PhoneHomeOutageMentionTests.In_flight_mention_Enter_late_confirms_without_retyping`: the status expectation changed from `QueuedMessageStatus.Pending` to `QueuedMessageStatus.Sent`. Added `DeliveryVerdict.ShouldBeNull()` and `h.QueueClock.Advance(TimeSpan.FromMinutes(1))` on the existing `OutageQueueClock` (`TimeProvider`). Still asserted: `DeliveryAttempts == 1`, a non-null baseline, late confirmation, zero retype, and the receipt.

`GrokRulesCompactionRecoveryTests.Captured_native_boundary_reaches_one_durable_refresh_without_idle_input` (in-flight arm): added `SubmittedBodies.ShouldBeEmpty("confirming the unknown attempt is receipt-only")` and a second `FlushSessionAsync`. The single refresh submit and the ordinary row staying Pending remain.

## Selector, pins, index, CARD-1926

`ImpactedClassesSelectorTests` 45/45. `input-delivery-uncertainty` now watches `InputDeliveryUncertainException.cs`, `SessionMessageQueueService.cs`, and `AgentControlService.cs`, and selects `PhoneHomeOutageMentionTests`, `GrokRulesCompactionRecoveryTests`, `StandingSessionSwitchConcurrencyTests`, `CodexCliObservationGapTests`, `GrokRulesReadyOrderingTests`, plus the existing `LandRefusalDeliveryWitnessTests`, `AgentTaskMidTurnRefineTests`, and `SessionMessageQueuePhoneHomeDropTests`. The g54 row is outside this diff.

`tests/derived-impact-index.json` regenerated with the driver built first. The script printed `derived-impact-index unchanged`. `regenerate_script_reports_changed_then_unchanged` then passed 1/1. The earlier failure inside the 107-row was that test's `-NoBuild` path before the driver dll existed.

Nightly pins recomputed on this tree, old and new the same. `policyHash` `ecc5d4fabc326645bf8b7e7970cdc22c1a9d195a83e737a66ccf157f9cb0c0c1`. Live-lane `PinnedInputDigest` `67e50252fffbb65df7cf45d9187bb8a9cdaaec9ef12333103b883c60f313f3b8`. `tests/impacted-classes.json` is not one of the digest inputs.

CARD-1926 is not repaired on this branch. `C0824WorkspaceSlotCustodyTests.cs` has no commit in the range. Advisory: it stays carded.

## Compose and migration

`origin/master` is still `8cec1e74e75819ef20756c228add987ce4ae4170`. `git cherry` lists all nine subject commits as unique. `git merge-tree --write-tree origin/master HEAD` produced `aca2b211245eae44bc4bd7523cda69fd592bad12` with no conflicts. The composed tree is this tip. `check-evidence-diff` over the range: 9 commits, 0 violations.

Merge-subset: `migrations=false`, so the downgrade selector count is 0. Impacted classes still include the delivery and switch witnesses plus the land-notification, checkpoint, contract, and lane-category rows already on the range.

## Tests at the reviewed SHA

Leased `scripts/build-slot.ps1` runs, `UseAppHost=false`, `TEMP=/tmp`, `--no-build` after one green `dotnet build tests/Antiphon.Tests` (0 errors). No row's test duration exceeded 10 minutes. No pure timeout, so none was retried for load. Host load was high at the start of the suite (about 25) and lower by the later rows.

| Row | Result |
|---|---|
| StandingSessionSwitchConcurrencyTests | 28 passed |
| PhoneHomeOutageMentionTests | 8 passed |
| GrokRulesCompactionRecoveryTests | 11 passed |
| StandingSessionSelection/Ownership/QueueSwitch/RecoveryHttp | 54 passed |
| AgentControl | 33 passed |
| StandingContinuityRecovery + StandingRestartEscalation | 63 passed |
| LandRefusalDeliveryWitnessTests | 24 passed |
| AgentTaskMidTurnRefineTests | 39 passed |
| SessionMessageQueuePhoneHomeDropTests | 5 passed |
| GrokRulesReadyOrderingTests | 5 passed |
| ImpactedClassesSelectorTests | 45 passed |
| CodexCliObservationGapTests | 6 passed, 1 failed |
| Named guard union including DerivedImpactIndexTests | 106 passed, 1 failed, then the failed test passed alone |

The guard failure was `regenerate_script_reports_changed_then_unchanged` because `Antiphon.DerivedImpactIndex.dll` was absent. After the driver build it passed. That 107-count is this brief's named union, not the nine-class union of 78.

`CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original` failed at line 359: status was `Sent`, expected `Pending`, after `REMOTE_FIXTURE_REFUSAL` `ConditionalInput` / `phone_home_unsupported_target`. The same method, run alone at bare `origin/master` `8cec1e74e75819ef20756c228add987ce4ae4170`, failed the same way (1 failed, 1m 06s, load about 8 to 12). `CodexCliObservationGapTests.cs` and `SessionMessageQueueService.cs` are unchanged on this branch. Inherited. Advisory only: the selector now names that class, so an impacted run of this path will see the pre-existing red.

## Mutants

Judged from the tests already run. Mutation execution was waived.

Narrowing the guard back to Pending-only makes `Reservation_and_real_queue_flush_serialize_in_both_orders(true)` fail: it expects `standing_resume_delivery_pending` and the row staying on B.

Dropping the session predicate makes `Unconfirmed_attempt_on_selected_history_does_not_block_switch` fail.

Dropping `DeliveryAttempts > 0` has no existing control with `Sent`, a null verdict, and zero attempts that expects the switch to succeed. Production stamps the attempt count in the same write as `Sent`. Advisory test gap.

## Advisories that do not hold the land

- CARD-1926 stays carded.
- Inherited C1029 Pending-versus-Sent expectation.
- No zero-attempt Sent control.
- The 409 text still says pending input while the new refusal is a Sent row the queue does not list.
- Card spawn, Herdr adopt, dispatcher dispatch, and boot-wedge relaunch still move `PersistentSessionId` without this guard. They also skipped the old Pending guard.

## Evidence limits

`reviewedSourceClean` is false. This card has no checkpoint manifest, and these runs are build-slot leases rather than checkpoint-tool receipts. The host is Linux. Native Windows proof stays owed. Positive controls stay pending. Parking stays disabled, so this refusal is not an input wait with an automatic release deadline. The blocker is a durable 409, not a parked seat.
