# CARD-1232: durable caller notification for pre-dispatch failures

Plan date: 2026-10-20. Target: **master only**. Next stage: **TestDesign**.

## Current-master finding

The gap remains at fetched `origin/master`
`9177159a0711948ab8c0f81f9ab03db2056204fc`, also this task's base.
This is a source inspection finding, not an executed failure reproduction.
No build or test was run during Plan.

The card's example is still reachable: an unknown role-policy tool bundle causes
`AgentTaskDispatcher.TickAsync` to call `FailAndNotifyAsync` before dispatch.
`FailAsync` saves Failed and its event without a notification because this caller
does not supply `deliveryFailureNotificationId`. Only afterward does the caller
arm/save a reminder and try to insert an unkeyed queue row. Both reminder arming
and the legacy reminder scan depend on `CheckEnabled`. A process loss between
those saves, or an insert failure with checks disabled, leaves no delivery owner.

### Ground truth

Paths below are relative to the repository; line numbers refer to the base above.

| Card premise / planning question | Current code | Consequence |
|---|---|---|
| Pre-dispatch failure can commit without a durable notification. | `server/Application/Services/AgentTaskDispatcher.cs:4436` (`FailAndNotifyAsync`) calls `FailAsync` without an obligation ID; `:7694` only creates an outbox row when that optional ID exists. Reminder arming and enqueue follow the first save. | Confirmed crash window. Fix the failure write, not just the later enqueue catch. |
| A failed insert is not retried with checks disabled. | `ArmFailureReminder` at `:6558` returns when disabled; `RemindUnacknowledgedFailuresAsync` at `:4882` recovers existing DeliveryFailure rows, then returns when disabled. | Recovery already works without checks **if an obligation exists**. |
| The delivery watchdog has the desired precedent. | `FailNeverStartedAsync` at `:2433` supplies an ID, and at `:2483` enqueues with `sourceLandNotificationId`. `TryFailArm2ConditionalAsync` also commits the failure/event/outbox together. | Reuse `LandNotificationKind.DeliveryFailure`; no schema migration is needed. |
| Which other pre-dispatch failures share the gap? | Three preflight branches call `FailAsync` directly: existing-agent/Worktree conflict (`:5488`), repair preparation failure (`:5655`), and progress-baseline failure (`:5768`). Each commits its claim transaction before returning. Credential refusals use `FailAndNotifyAsync` with `commitBeforeNotify`. | Put obligation creation in the shared failure writer, including these direct calls. |
| Can existing recovery be reused? | `RecoverDeliveryFailureNotificationsAsync` at `:4935` runs before the optional-check gate. `server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs` also scans on boot and every five seconds. | Keep both existing recovery owners. |
| Delivery must reach the caller through the real queue. | `server/Infrastructure/Data/AppDbContext.cs:2130` gives notifications unique `SourceEventId`; queue rows have unique non-null `SourceLandNotificationId`. `AgentTaskLandNotificationService.ReconcileAsync` checks complete prompt evidence. | Identity must be per failure event; enqueue, Sent, polling and result-read stamps are not receipt. |
| CARD-0507 exposed the two missing tests. | `RolePolicyToolBundleDispatchTests.the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt` covers eligible, busy, failed insert with checks enabled, and post-enqueue interruption. `docs/superpowers/plans/2026-10-13-card-0507-test-design.md:133` explicitly excludes these two gaps. `ReceiptFailureDeliveryTests` covers analogous watchdog cuts. | Extend producer coverage using the real queue; existing green tests alone cannot close CARD-1232. |

## Decisions

- **D-1 — Commit the obligation in `FailAsync`.** When `DispatchedAt == null`,
  `ReplyTo == Session` and `ParentSessionId` exists, add one non-legacy
  `AgentTaskLandNotification` of kind `DeliveryFailure` alongside the Failed
  transition and its new Failed event. Reuse an explicitly supplied notification
  ID rather than minting a second row. Its source event, destination, complete
  formatted body, digest, creation time and initial due time are durable before
  any caller enqueue. This covers the three direct preflight callers as well as
  the wrapper. Reject a second outbox/table and startup reconstruction from mutable
  Failed task rows: the existing outbox has the identity, retention and receipt
  machinery, and an immutable payload avoids guessing after retry.
- **D-2 — Keep the existing caller text and commit boundary.** Finalize failure
  reason, abandoned-commit warning and `git` guidance before saving the snapshot.
  Let `FailAsync` return the created notification (nullable for routes without an
  obligation) and accept the wrapper's optional formatting facts, or an equivalent
  small private helper. Do not reconstruct a different body after the commit.
  Inside a claim, save in that transaction and enqueue only after
  `commitBeforeNotify` commits; direct preflight returns rely on the scanner after
  their existing commit. Keep release/cleanup after commit. Reject enqueue inside
  the transaction: the queue uses another context and may wait on the task row.
- **D-3 — One durable identity for immediate delivery and recovery.** The wrapper's
  immediate enqueue uses the saved Body/digest, `SourceTaskId`, `task:<RootTaskId:N>`
  and `sourceLandNotificationId = notification.Id`, preserving eligible inline
  delivery and `WhenIdle` for a busy caller. Recovery uses that same ID through
  `AgentTaskLandNotificationService`. A retry of the task does not erase an old
  failure obligation; a later failure event owns a distinct one. Reject task-ID
  deduplication and an additional unkeyed reminder, which can lose later outcomes
  or submit the same failure twice.
- **D-4 — Delivery retries are independent of checks.** For new pre-dispatch
  failures owned by this outbox, clear `NextCheckAt` in the failure write and do
  not subsequently call `ArmFailureReminder`. Leave the historical no-outbox
  reminder path available for old records. Keep the existing recovery pass ahead
  of the `CheckEnabled` return. Reconciliation retries failed inserts at persisted
  `NextAttemptAt` using the existing exponential delay (5 seconds doubled by
  attempt, capped at 300 seconds); a direct enqueue failure leaves its already
  due obligation available to the next scan. No `CheckMaxCount` limit applies.
  Missing/stopped destinations remain owed with the existing five-minute retry.
  Reject simply removing the CheckEnabled guard: it leaves the commit gap and
  makes the optional reminder budget the delivery guarantee.
- **D-5 — Preserve the existing delivery bounds.** A queued notification reuses its
  one keyed row. It does not mint a replacement for a canceled, missing or
  attempt-exhausted authoritative row. Those states stay unconfirmed under the
  existing outbox diagnostics; this card does not bypass queue attempt limits.
  Confirmation requires the matching complete caller UserPrompt above the attempt
  floor, including existing verified spill handling. Polling or reading the task
  cannot discharge this obligation.
- **D-6 — Scope and compatibility.** No migration, new setting, new scheduler,
  historical backfill, native terminal change, automatic task retry, or RC rollout.
  Post-dispatch failures keep their current behavior except the already keyed
  watchdog path, which must remain green. Board/None routes and missing parent
  routes create no caller obligation. The fix is prospective; already-lost
  historical notes remain a limitation.

## Delivery contract

| Producer | Destination | Atomic persistence | Recovery | Receipt |
|---|---|---|---|---|
| Pre-dispatch `FailAsync`, reached by the wrapper or a direct preflight refusal | Snapshotted parent session | Failed task + Failed event + DeliveryFailure row in the same SaveChanges transaction, or the enclosing claim transaction | Immediate keyed enqueue after commit; dispatcher recovery before check gating; boot/periodic outbox scan; existing stranded queue recovery | Complete matching UserPrompt after the queue attempt floor, then Confirmed on that notification |

The durable chain is `FailedEvent.Id -> notification.SourceEventId`, then
`notification.Id -> queue.SourceLandNotificationId`, then the queue's attempt
baseline and matching transcript sequence. Recovery renders the saved payload,
even if the task is retried or its current fields change. The successful path
must preserve completion-note stamps for existing consumers without treating
those stamps as delivery proof.

No session lifecycle or input-wait behavior is added or changed. `WhenIdle` waits
for the caller's existing turn to end; this is not session parking or a release
deadline. With parking disabled there is no automatic input-wait release deadline
(CARD-1083).

## Slices

| Slice | Files | Work and tests |
|---|---|---|
| S1 — Commit the red witnesses | New `tests/Antiphon.Tests/Application/PreDispatchFailureDeliveryTests.cs`; a small adjacent fixture only if necessary | Add the two current-master witnesses below through the real dispatcher and queue. Follow the inspected `RolePolicyToolBundleDispatchTests` producer and `ReceiptFailureDeliveryTests` persistence interceptors; use `TestDbFixture.CreateIsolatedSchemaAsync`. Commit/push before executing the red selection. |
| S2 — Persist and deliver | `server/Application/Services/AgentTaskDispatcher.cs`; the S1 test file | Implement D-1–D-5 in `FailAsync`/`FailAndNotifyAsync`. Preserve existing watchdog IDs and transaction ownership. Suppress the new failure's legacy reminder. Add direct preflight and route-exclusion controls. Commit/push, then execute only the named green checkpoints. |
| S3 — Update affected contracts | `tests/Antiphon.Tests/Application/RolePolicyToolBundleDispatchTests.cs`; `tests/Antiphon.Tests/Application/AgentTaskDispatchFailureTests.cs`; `docs/session-runtime-invariants.md` | Update the existing real-queue refusal test to expect keyed recovery and the immutable body instead of reminder text/NextCheckAt. Replace `a_dispatch_failure_arms_a_reminder` with a durable-obligation assertion. Preserve legacy reminder tests on deliberately seeded no-outbox historical rows. Delete owned notification rows before their source events during fixture cleanup. Document the prospective pre-dispatch contract beside the delivery-watchdog contract. |

The outbox entity, EF mapping, notification service, hosted scanner and queue are
reuse dependencies, not planned production edits. If TestDesign finds a missing
seam, add only a narrowly scoped fixture/interceptor before widening production.

## Test handoff

TestDesign is a separate stage: it must append the executable **Verification
design**, guard/positive-control map and closed checkpoint manifest before Code.
The following are proposed method names and required oracles, not claimed results.

| Behavior | One red witness | Narrow green and controls |
|---|---|---|
| B-1 — A failed commit survives loss before reminder/enqueue. | `Pre_dispatch_failed_commit_keeps_the_caller_obligation`: unknown-tool refusal; interrupt immediately after the first committed Failed save, independently of whether an outbox row was added. On base, Failed/event exist but the required obligation count is **0**, not 1. Do not trigger the cut only on an outbox insert: it would never fire on base. | Same method: one immutable obligation linked to the Failed event; fresh services with checks disabled recover it to one complete caller prompt. Include eligible and busy callers. Companion before-save fault proves task/event/outbox all roll back together, then a fresh dispatch succeeds in recording the failure. |
| B-2 — Failed enqueue recovers with checks disabled. | `Pre_dispatch_failed_insert_recovers_with_checks_disabled`: disable checks before dispatch; fault the first queue insert; assert Failed, zero queue rows/prompts, fault reached. Recreate services, advance fake time to the saved due time (use a fixed advance for the base with no row), run the real recovery pass and flush. On base the expected caller prompt remains **absent**. | Same method: recover exactly one keyed row and complete matching UserPrompt; obligation becomes Confirmed. Repeat recovery/flush and advance past the former reminder window with checks enabled: no second row or submission. Include busy and already eligible callers. |
| B-3 — Direct preflight callers and transaction ownership are covered. | `Preflight_failure_obligation_commits_with_its_claim`: use the existing-agent/Worktree refusal through the real claim; base commits Failed with zero obligations. | Same method: pre-commit interruption rolls back the pair; post-commit recovery delivers once without waiting on an uncommitted task row. TestDesign also binds existing repair-source and progress-baseline refusal fixtures to an obligation assertion. Board/None/missing-parent controls have zero obligations. |
| B-4 — A committed enqueue is reused after interruption. | `Pre_dispatch_recovery_reuses_the_committed_queue_row`: after the failure fix, remove only the immediate enqueue's notification key; interrupted delivery plus outbox recovery must fail the one-row/one-submission assertion. This is a later mutation control, not a claimed base failure. | Same method: cuts after queue commit, after attempt claim, and after accepted prompt/before receipt save; rebuild services and preserve the same keyed row. Accepted-prompt recovery confirms late without a second submission. Assert full payload and attempt-floor evidence, not only header/ID. |

Use fake time for retry eligibility and explicit persistence/adapter barriers;
no sleeps, real provider, production runner or injected notification rows as a
substitute for running the failure producer. If the queue also uses fake time,
drive its timers explicitly while awaiting its operation; an unadvanced fake
clock can hang queue verification. All spawned local operations must be awaited.
The adapter substitute records submitted composer bytes as transcript UserPrompt;
it proves service/queue recovery, not native PTY behavior.

### Checkpoint scope for TestDesign

All rows use the **portable .NET Integration lane with isolated PostgreSQL and a
fake adapter**. No Windows/PTY requirement exists. The live
`GET /api/runner-defaults` and `GET /api/session-runners` were read on 2026-10-20;
placement remains the live default. Omit `-Runner` and `-Platform`; do not pin a
fleet location.

| Candidate | After | Exact method selection | Purpose |
|---|---|---|---|
| CP-1 | S1 red; S2 green | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_commit_keeps_the_caller_obligation` | B-1 |
| CP-2 | S1 red; S2 green | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_insert_recovers_with_checks_disabled` | B-2 |
| CP-3 | S2 | `/*/*/PreDispatchFailureDeliveryTests/Preflight_failure_obligation_commits_with_its_claim` | B-3; bind remaining two preflight fixtures during TestDesign |
| CP-4 | S2 | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_reuses_the_committed_queue_row` | B-4 |
| CP-5 | S3 | `/*/*/RolePolicyToolBundleDispatchTests/the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt` | Existing four producer-to-recipient paths with new ownership |
| CP-6 | S3 | `/*/*/ReceiptFailureDeliveryTests/Caller_failure_obligation_survives_persistence_cuts` | Shared FailAsync/watchdog regression: existing 12 variants |

TestDesign must add the exact changed reminder assertion, historical no-outbox
control, and non-session route control filters, then bind case counts, expected
assertions and estimates. No whole-Unit, whole assembly, or unrelated class run.
Each manifest row names this lane and one exact filter; reuse an isolated build
only within the same committed slice group. Use the checkpoint tool/build-slot
gate, keep source frozen during runs, retain expected-red evidence, and delete
owned alternate outputs before settlement. Missing counts or fixture failures
are not a red witness. Plan does not authorize a broad exploratory run.

## Acceptance and limits

The two original losses are closed only when the new producer tests show atomic
failure/obligation persistence and check-independent recovery to a complete caller
prompt. Immediate and recovered paths must converge on one keyed row, with no
legacy duplicate, and existing watchdog coverage must pass. A crash test must
discard service instances and contexts, retaining only database and recipient
state. This plan makes no exactly-once claim for an unobservable terminal; it
keeps the existing queue's receipt and retry rules.

No product decision is outstanding. TestDesign should retain these decisions,
complete the narrow verification design and hand off to Code on master.
