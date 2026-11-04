# Review bdaa665c: CARD-1705 scanner bounded queue-lock wait (round 1 of 3)

Subject: origin `feat/card-task-37b01055` at `96d06ebc333ebbb3a516583a1bc741ce07822c8c` (red witness `9bbadd8b3becd17c740a068ef4602517d12e95a0`, base `b5f97136167d942484aa23ea8660cf50cc800eff`). Code landing owner: task 37b01055.

**Verdict: found. 2 defects.** The land-notification phase is sound: no lost notice, no side effect on timeout, semaphore count correct, catch ordering correct, only the scan passes a bound. But the same hosted loop still blocks every caller behind one held lock in its legacy-captured phase (D-1), and the order guarantee has no test that can go red (D-2).

## Verified sound (items 1-4)

1. **No lost notice, order kept inside the land phase.** A skipped notice leaves only in-memory changes (`ConcurrencyToken`, `EnqueueAttempts++`), and `ChangeTracker.Clear()` throws them away. Nothing is saved before the enqueue on that path: the existing-row recovery sets `QueueMessageId`, which skips the enqueue branch, and every other earlier branch returns. The note stays Queued with no attempt, error or backoff, and the next pass retries it. The busy-set check (`AgentTaskLandNotificationService.cs:151`) comes before the `before-enqueue` boundary and before the enqueue. So a later notice for A in the same pass is skipped even when the lock frees after A1's timeout. The set is created once per pass, outside the 128-row page loop (`AgentTaskLandNotificationHostedService.cs:94`), so it covers every page.
2. **No side effect on timeout.** `SessionMessageQueueService.cs:689-693`: a timed-out `WaitAsync(bound, ct)` returns false and throws before the `try`, so there is no `Release`. A cancelled wait throws `OperationCanceledException` without acquiring the lock, which is the same as before. The pre-lock code for a WhenIdle enqueue with no `executionTaskId` only reads (`RequireSessionAsync`). `GetLock` is a plain `GetOrAdd` with no reference count to leak. `onCreated` runs only under the lock.
3. **Catch ordering.** The busy catch (`:390`) comes before the general catch (`:396`). `lockWait` is passed at exactly one call site (`:172`), and only when `busySessions` is not null. No other `EnqueueAsync` caller can receive `SessionQueueBusyException`. On the null path, behaviour is unchanged.
4. **Cost.** Each scan costs at most 1 s per distinct held caller, because later notices for the same caller are skipped without waiting. That is bounded by the number of callers. Not a concern.

## Defects

**D-1: the hosted scan still stalls every caller behind one held lock, in its legacy-captured phase.**
- Where: `server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs:53-85`. The call chain is `LegacyCheckNotePublicationService.RecoverCapturedAsync` -> `ProduceAsync` (`LegacyCheckNotePublicationService.cs:227`) -> `AgentTaskLandNotificationService.ReconcileAsync(id, ct)` with no busy set -> `EnqueueAsync`, which waits unbounded at `SessionMessageQueueService.cs:693`.
- Failure: production registers the service (`Program.cs:603`), so `capturedEnabled` is true. Take a Captured publication whose interpretation run has succeeded, or whose deadline has passed, and whose parent session's queue lock is held: for example, a delivery held at `queue-before-typing`, as in the card's C467_V32 evidence. The scan's captured phase waits on that lock with no limit. The land-notification phase further down the same `ExecuteAsync` loop never runs, so every other caller's notices wait. That is the CARD-1705 failure, through a sibling phase.
- Why: the claim is that the hosted scan no longer holds other sessions behind one session's lock. The fix bounded only the third phase.
- Fix: create the per-pass busy set before the captured phase and thread it through `RecoverCapturedAsync`/`ProduceAsync` into `ReconcileAsync`. The simpler option: when called from the scan, do not reconcile inline. `ProduceAsync` already commits the Queued `AgentTaskLandNotification` row (`NextAttemptAt = now`) before it reconciles, so the land phase of the same pass picks it up with the bound. Add a witness: one Captured publication that is due for the held session, plus a free session's land notice. Assert that one scan returns within 10 s with the free notice enqueued (it fails on `96d06ebc3`).

**D-2: the per-session order guarantee (the busy-set skip) has no test that can go red.**
- Where: `tests/Antiphon.Tests/Application/AgentTaskLandNotificationRecoveryTests.C1705.cs`, which holds the lock for the whole first scan.
- Failure: delete the `busySessions?.Contains(session)` skip (`AgentTaskLandNotificationService.cs:151-155`) and C1705 stays green. Each later busy note just waits its own 1 s and times out; a few seconds is still inside the 10 s bound, and every busy note still ends with no row. The case the brief names is unwitnessed: A1 times out, the lock frees, then A2 is reached and enqueued ahead of A1.
- Why: this is the order half of "no lost or reordered notice". Without the skip, A2 jumps ahead of A1, and no test fails.
- Fix: seed A1 < B < A2 by id. Hold A's lock and release it from the `before-enqueue` boundary for B's notice (after A1 has timed out). After scan 1, assert that A2 has no `QueueMessageId`. After scan 2, assert that A1's queue row was created before A2's.

## Advisories

- **A-1 (brief's advisory): `CompletionNoteWorkHostedService.RecoverMissingAsync` has the same problem.** `CompletionNoteWorkHostedService.cs:138` calls `queue.EnqueueAsync(...)` unbounded inside its per-task loop. The `TaskSupervisionNoticeService.RecoverAsync` it calls first (`:101`) also enqueues. So one held caller lock stalls the completion-note recovery walk and its sweep for every task behind it. That needs a follow-up card.
- **A-2: order across producers.** A producer path (dispatcher/reply, still unbounded) can enqueue A's later notice while the scan has left A1 for the next pass. The same reordering already happened before this change (a backoff `NextAttemptAt`, or an earlier producer), so this is not a new defect.
- **A-3:** `lockWait` is silently ignored for `MessageSendMode.Now`. The scan never uses Now, so this is harmless.

## Runs (Linux server2, one build-slot lease each, `UseAppHost=false`, native output)

| Run | SHA | Total | Passed | Failed | Skipped |
|---|---|---|---|---|---|
| Witness `C1705_*` (red commit) | 9bbadd8b3 | 1 | 0 | 1 | 0 |
| Witness `C1705_*` (tip) | 96d06ebc3 | 1 | 1 | 0 | 0 |
| Scanner class set, 19 classes (28m51s) | 96d06ebc3 | 405 | 405 | 0 | 0 |

- The red failed for the right reason: `WaitForScanAsync(1, 10 s) should be True but was False`, "one held caller lock must not keep the notification scan from returning".
- The 19 classes were AgentTaskLandNotificationRecoveryTests, AttentionFeedNoiseTests, AgentTaskLandMonitoringTests, AgentTaskCommitEndpointTests, TurnEndGapRestartDeliveryTests, AgentTaskCommitChildChainTests, AgentTaskWorktreeLockOutcomeTests, DispatchBaseNotificationTests, CompletionWarningDeliveryTests, ContinuationSettlementTests, PipelineHandoffPilotTests, PipelineSpeedIntegrationTests, ReviewEvidenceConsistencyTests, ReviewEvidenceDeliveryTests, VerificationRoundDeliveryTests, AgentTaskLandEnqueueTests, AgentTaskLandHeldNotificationTests, CheckNoteDeliveryHandoffTests and PermissionWaitDeliveryTests. That covers every user of the hosted service, C544DeliveryRig, LandOutcomeDeliveryHarness, TurnEndGapDeliveryHarness and PermissionWaitFixture.
- **Not run:** the classes that call `ReconcileAsync` directly with no busy set (for example AgentTaskLandReceipt*, AgentTaskLandQueue*, PostLandMutationDelivery*, PreDispatchFailureDelivery*). Their path is unchanged by this diff (`busySessions` null means `lockWait` null and the busy catch's filter is false), and the 60-minute budget did not allow another roughly 30-minute run. The whole Unit lane was not run either; the operator accepted Interim scope for this master-only card. ordinaryScopeCompleted: Interim.
- PCs (the 1 s bound, the busy-set skip, the busy catch's position, the clear on busy) stay pending for Mutation.

## Checks

- `check-evidence-diff` over b5f971361..96d06ebc3: 2 commits, 0 entries, 0 violations.
- `merge-tree` was clean against `96882045e` (the brief's master), against the current `origin/master` `4c1296357` (three group-125 roster commits since then) and against `origin/feat/card-task-ff8f8f3f` `5ab8e14fb`.
- Cleanup: I deleted the scratch base worktree and my bin/obj outputs.
