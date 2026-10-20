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

## Verification design

TestDesign appendix, 2026-10-20, inspected at
`d86355177aba2bd221c0b3b3daf44ed5073c8dff`. This appends executable verification;
D-1–D-6 and S1–S3 above remain the fix design. Target remains **master only**.
No build, test or mutation was executed by TestDesign. Names marked new below are
Code obligations, not existing coverage or claimed green results.

### Inspection

- Read `RolePolicyToolBundleDispatchTests.cs`, including the four-case caller
  delivery method, `BuildProvider`, `NoticeFault`, receipt assertions and cleanup.
  Unknown-tool producer, eligible/busy parent, insert/pre-type cuts -> V-1, V-2,
  R-1. Its present provider does **not** register the notification service/flush
  queue; Code must register both before claiming recovery coverage.
- Read `ReceiptFailureDeliveryTests.cs`, including `VerifyCallerFailureAsync`,
  `CallerFailureFault`, `VerifyDeliveryAsync`, `DeliveryFault`, `OffsetClock` and
  `CreateProvider`; read `CapacityRecoveryTaskTests.CreateDispatcherProvider`.
  Atomic failure/event/outbox, fresh-provider cuts, real queue and late receipt ->
  V-1–V-4, R-2. Do not copy OffsetClock for the new retry tests: it advances real
  UTC and is not deterministic fake time.
- Read `AgentTaskDispatchFailureTests.cs`, its seeding, harness and disarm helpers.
  Changed reminder assertion and deliberately historical no-outbox row -> R-3;
  Board/None/missing-parent -> R-5. The method named board-only currently seeds
  None, so it cannot establish Board coverage. Its shared-store drain is not a
  suitable fixture for the new file.
- Read `WorktreeDefaultContinuationTests.ExplicitWorktreePinRefuses` and its
  agent-seeding shape; `RepairSourceDispatchTests.C499_V05_AmbiguousOrMismatchedRegistrationRefusesBeforeLaunch`,
  `C499_V07_ABaselineIsPersistedOnlyWithTheClaimAndRecapturedOnRetry` and
  `C499_R37_LocalIdentityFailureRefusesDispatch`; `RepairSourceWorld` setup,
  service registration, dispatch and replay; and `ControlledTaskProgressGit`.
  Nearest fixture for new preflight cases -> V-3. R37 fails in repair preparation,
  **not** progress-baseline capture; the latter needs the separate HEAD fault below.
- Read `ClaudeCredentialProbeDispatcherTests.cs`, including the logged-out
  producer, `SeedAsync`, `CreateDispatcher`, `ProbeDirectory` and `ProbeClient`.
  Wrapper inside the claim transaction -> V-3, using its in-process probe only.
- Read `DelegationDispatchRecoveryBoundaryTests.DeadSessionFailedReceipt.cs`
  guidance test and its literal guidance/typed-spill receipt assertions, plus
  AbsentLaunch seed/OpenSweep and AbsentLaunchRepair receipt helpers -> R-4.
  Read `AgentTaskLandReceiptTests` body/false-receipt cases and seeder, and
  `AgentTaskLandNotificationRecoveryTests.C467_V08_RetryAndDestinationMatrix` ->
  V-2, V-6–V-8 recipes; their seeded notifications are substitutes, not this
  card's producer evidence.
- Read `BridgeQueueHarness` options/DI, attach and create branches, transcript
  insertion, `MarkWorkingAsync` and disposal; `FakeAgentProtocolAdapter`
  composer/submission callback; `TestDbFixture` options/isolation/disposal -> all
  new integration cases. Despite its historical name, CreateIsolatedSchemaAsync
  supplies a cloned PostgreSQL **database**, not a shared SearchPath schema.
- Read `AgentTaskDispatcher` failure writer/wrapper, direct preflight branches,
  credential calls, recovery-before-check gate, reminder logic and baseline
  capture; `AgentTaskLandNotificationService` reconciliation and retry function;
  hosted scanner; queue keyed enqueue/commit/inline-admission bodies;
  `LandNoteReceipt`; and RetryAsync admission/reset -> V/R boundaries below.
  Read testing/build checkpoint schema and coverage binding rules, project
  conventions, orchestration stage ordering/D-2, and session-runtime caller
  receipt contracts. Native input, scanner scheduling fairness and unrelated
  landing kinds are excluded below.

### Delivery inventory

All paths join the same durable chain:
Failed event ID -> notification.SourceEventId -> notification.Id ->
queue.SourceLandNotificationId -> queue.Id/attempt floor -> destination
UserPrompt.Sequence -> notification.ConfirmingPromptSequence. Assert the event
belongs to this task and failure, and the queue's SourceTaskId/root conversation
key and content digest match the saved obligation. A task ID alone is not a
failure identity.

| Path | Producer and destination | Persistence boundary | Recovery and observable receipt | Binding |
|---|---|---|---|---|
| Wrapper outside claim | Real TickAsync unknown-tool refusal -> snapshotted parent | Failed + new Failed event + non-legacy DeliveryFailure in one save | Fresh dispatcher recovery with checks disabled, then real queue flush and complete UserPrompt; eligible and busy | V-1, V-2; G-1/2/11–16 |
| Direct preflight | Existing-agent/Worktree conflict, repair preparation or progress-baseline failure -> parent | Same records inside existing claim transaction | After actual commit, fresh recovery/queue; no row or prompt before commit | V-3; G-1/2/10 |
| Wrapper inside claim | Logged-out Claude credential refusal -> parent | Failure save inside claim, commitBeforeNotify before queue context | Immediate eligible delivery or fresh recovery after cut; independent observer sees Failed/event/obligation before any enqueue | V-3; G-10/11 |
| Immediate queue handoff | Wrapper saved body/digest/key -> real SessionMessageQueueService | Queue insert and completion stamp; producer may lose acknowledgement | Key lookup reuses row after queue/attempt/prompt cuts; accepted prompt confirms late without another submission | V-4; G-9/11/17–26 |
| Retry/backstop | Due DeliveryFailure -> saved parent, including after task RetryAsync | Persisted NextAttemptAt, enqueue attempts, authoritative queue link | Dispatcher mandatory pass; existing notification scanner invokes the same reconciler; receipt persisted only from matching complete UserPrompt | V-2, V-5–V-8; G-3–5/13–31 |

Use the production dispatcher, PostgreSQL, notification reconciler and queue in
all new cases. The only recipient substitute is FakeAgentProtocolAdapter: its
OnSubmitted callback persists **the actual submitted composer bytes** as a stamped
UserPrompt, then TurnEnd. Never manufacture a success prompt from notification.Body
without a submission. Deliberately false transcript rows in V-6 are negative
inputs, explicitly separated from the later real submission. This proves service,
persistence and queue delivery; it cannot prove native PTY/provider acceptance,
remote transport or a real daemon restart. No process/runner/provider is launched.
A recording launch sink must stay empty for refusals.

Use one per-case cloned database for every context, including observers, faults
and transcript callbacks. Start from the role-policy fixture; a file-local
PreDispatchFailure fixture may compose its provider with the notification-service
registrations from CapacityRecoveryTaskTests. For Git preflight variants reuse
RepairSourceWorld/ControlledTaskProgressGit with narrow optional test DI/interceptor
configuration if needed; no new production seam is required. Give Git-spawning
cases the assembly's ParallelLimiter<ProcessSpawnLimit>. Keep outputs and synthetic
remotes local. Delete owned notifications before their source events, queue rows
before tasks, and tasks before recipient disposal; dispose every provider/scope.

**Clock and crash recipe.** Use a fixed FakeTimeProvider for dispatcher/reconciler
and all obligation timestamps. It is acceptable to keep BridgeQueueHarness's queue
on its system clock, as the inspected role-policy test does: this explicitly
substitutes only the queue verification timers, not retry eligibility. No sleeps
or elapsed-wall-time assertions. If Code supplies the fake clock to the queue too,
add an awaited, bounded timer pump that advances only after a timer is registered;
never await a fake-timed queue operation with its clock frozen. Anchor transcript
attempt timestamps to the queue clock, not an unrelated future retry clock.

A cut records task ID, boundary name and hit count. Before-save and after-save
faults match the **Failed task transition plus Failed event**, independently of
whether a notification was added. Record the tracked triple as an extra oracle,
not the trigger. For outside-transaction saves, SavedChanges is the committed
boundary. Inside a claim use DbTransactionInterceptor TransactionCommitting/
TransactionCommitted; SavedChanges alone is insufficient. Arm only for the target
failure, ignore earlier seed saves, and use cancellation with the test's token to
stop the rest of that tick (await its exit), so broad exception handling cannot
silently perform a second failure write. Inspect committed rows through a fresh
untracked context before disposing/rebuilding all application services. Keep only
database, clock and recipient transcript/submission ledger across the restart.
A recovered operation gets a fresh, uncanceled token. For the queue-before-commit
order check, record the observer result and cancel before any locked task stamp
when the committed triple is absent, then assert the recorded result explicitly.
A post-commit callback may record the committed triple before signaling the cut.
Never treat a failed fixture or an unreached cut as a behavioral red.

### Proves it works now

Common green oracle **O-receipt**: one keyed row per failure event, saved payload
(or its verified owned spill), expected destination/SourceTaskId/root key/digest,
one actual matching submission, a complete UserPrompt above its attempt floor,
and Confirmed with that prompt's sequence. Repeat reconciliation and flush once;
row ID, body, sequence and submission count remain stable. Confirmed/Sent/ack/event
or completion-note stamps alone cannot satisfy this oracle.

- **V-1 (B-1, new):** atomic failure obligation | Integration |
  `PreDispatchFailureDeliveryTests.Pre_dispatch_failed_commit_keeps_the_caller_obligation`.
  Recipe: real unknown-tool TickAsync, checks disabled before production, literal
  multi-line failure landmarks, and the independent Failed-transition cut above.
  Variants/cases: (eligible, busy) x (before-save, failed-committed), **4 results**.
  Before-save must leave Queued, zero Failed events, zero obligations/notes/prompts;
  recreate without fault, redispatch and continue. After-commit must leave Failed,
  exactly one event and one linked immutable obligation, no enqueue/submission.
  Recreate, recover and satisfy O-receipt. Busy remains Pending with zero attempts
  and no submission until explicit TurnEnd; eligible recovers without a new turn.
  Reachability: cut hit exactly once, refusal reason includes unknown-tool code,
  zero launches. Classification: base-red at obligation count 0 != 1 after the
  committed failure (also after the before-save variant's successful retry);
  same method is the narrow green. PC-1/2 challenge persistence; R-1 supplies
  the immediate busy-path control PC-12.
- **V-2 (B-2, new):** retry despite disabled checks | Integration |
  `PreDispatchFailureDeliveryTests.Pre_dispatch_failed_insert_recovers_with_checks_disabled`.
  Variants: eligible/busy, **2 results**. Fault the first real queue insert with
  checks disabled and CheckMaxCount=0. Assert fault hit, Failed, zero rows/prompts
  and no completion stamp. Recreate services; advance to persisted due time, or
  a fixed +5 seconds on the unfixed base where no obligation exists. First require
  O-receipt after recovery; base-red is missing caller prompt, not an early assertion
  requiring a notification that base cannot create. Green additionally checks
  the obligation existed before recovery and NextCheckAt stayed null.
  In the same test's independently produced second failure, fault eight successive
  reconciler insert attempts; record saved attempts and exact due delays
  5,10,20,40,80,160,300,300 seconds. At due-minus-one-tick the dispatcher recovery
  count is zero; a separate direct ReconcileAsync call also leaves attempts and
  inserts unchanged. At due the next real insert is attempted. Recreate between
  attempts, retain checks disabled and restore insert success after attempt eight;
  reach O-receipt. These eight loop iterations are **not** eight TUnit results.
  Then enable checks, advance two hours and recover/flush again: still one keyed
  row per failure, no unkeyed reminder and CheckCount unchanged. Reachability:
  distinct immediate/recovery fault counters, fresh contexts and due observations.
  Classification: one base-red/green method, PC-13–15/34/35 for retries;
  R-3 supplies the checks-enabled control PC-16.
- **V-3 (B-3, new):** claims commit the obligation before external work |
  Integration, local Git where needed |
  `PreDispatchFailureDeliveryTests.Preflight_failure_obligation_commits_with_its_claim`.
  **6 explicit Arguments results:** pin-before-commit, pin-after-commit,
  repair-preparation, baseline-head, credential-before-commit, credential-committed.
  Pin recipe copies ExplicitWorktreePinRefuses but replies to a real parent.
  Repair recipe uses R37's owner-ref rev-parse failure. Baseline recipe lets repair
  preparation and worktree creation succeed, then fails only rev-parse HEAD for
  the new primary worktree through ControlledTaskProgressGit.BeforeCommand; assert
  that path exists, prep succeeded and the HEAD fault fired. Do not call this
  variant R37 coverage. Credential recipe uses the inspected logged-out ProbeClient
  with a runtime-registered parent and no real runner.
  Before-commit cuts leave Queued and no failure/event/outbox/queue; retry with a
  fresh context records the failure. Post-commit variants leave one atomic triple.
  An enqueue-boundary observer independently reads the committed Failed triple
  before permitting queue insertion in the credential case; record a boolean and
  fail its explicit assertion, not a database timeout. Also record no enqueue
  before commit for direct branches. Fresh mandatory recovery reaches O-receipt
  for each. Classification: added S2 integration proof, not a third S1 red run;
  PC-2/10 exercise rollback/order. Same shared failure-writer mutation as PC-1
  must be capable of breaking the obligation assertion in each direct branch.
- **V-4 (B-4, new):** one keyed handoff survives queue crashes | Integration |
  `PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_reuses_the_committed_queue_row`.
  **6 results:** eligible/busy x queue-committed, attempt-committed,
  prompt-accepted-before-verdict-save. Run real unknown-tool producer. Queue commit
  and attempt commit use the ReceiptFailureDeliveryTests interceptors; the latter
  sees Sent, null verdict and zero submission. For busy callers, end the turn to
  reach the attempt/prompt cut. Prompt-accepted preserves one real UserPrompt and
  one submission while verdict save fails. Recreate services, advance only the
  applicable retry eligibility, recover/flush and require O-receipt on the original
  queue ID. Prompt-accepted ends LateConfirmed with no second submission; an
  attempt-only cut may increase attempts from one to two. Also cut the outbox's
  receipt-before-save boundary once within the prompt-accepted case, recreate and
  confirm the same receipt. Classification: preserved handoff proof; PC-11 is the
  compiling-defect red (not claimed red on current master).
- **V-5 (new):** failure-event identity and immutable snapshot | Integration |
  `PreDispatchFailureDeliveryTests.Pre_dispatch_snapshot_survives_task_retry`,
  **1 result**. Produce failure A with insert held, capture its event/obligation,
  call real AgentTaskService.RetryAsync, edit the task's current parent/title and
  change the unknown bundle key, then dispatch failure B. Use distinct literal
  first/middle/final landmarks and two live recipient adapters. Both obligations
  remain, with different source events/IDs; A's saved destination/body/digest are
  unchanged despite the mutable task and B's are distinct. Recover each through
  the queue to its own recipient O-receipt; neither adapter sees the other's note.
  Include a child task with RootTaskId different from Id so the conversation-key
  assertion cannot self-mask. Classification: S2 regression proof; PC-3–5.
- **V-6 (new):** receipt guards remain effective for DeliveryFailure | Integration |
  `PreDispatchFailureDeliveryTests.Pre_dispatch_receipt_requires_complete_user_prompt`,
  **11 results:** head-only, wrong-session, queued-prompt, old-sequence, old-time,
  no-attempt, sent-only, polled, read, spill-mismatch, valid-spill.
  Produce the obligation through TickAsync; hold queue at the real attempt boundary
  so there is no valid receipt yet (no-attempt holds before that boundary). For
  no-attempt, give the decoy a plausible baseline/start floor while keeping
  DeliveryAttempts zero, then restore those fixture fields before real delivery. Inputs
  are the named decoys; polled/read set only the respective task metadata. Old
  sequence uses a complete prompt at the recorded floor; old-time uses a null
  sequence baseline and a timestamp older than the configured tolerance. All
  negative cases remain unconfirmed with null confirming sequence; sent-only
  retains Sent and no receipt. Then allow the actual adapter to submit and require
  O-receipt. For spill cases produce a long refusal and force the existing owned
  queue spill at the normal 1024-byte limit; a complete pointer UserPrompt with a
  deliberately different file stays unconfirmed, and restoring the exact saved
  body allows confirmation of that same pointer without resubmission. Valid-spill
  confirms directly with exact file bytes. Verify body landmarks independently;
  a self-comparison to the formatter is insufficient. Classification: preserved
  receipt safeguards; PC-17–26. Seeded decoys do not count as producer delivery.
- **V-7 (new):** authoritative queue row is never replaced | Integration |
  `PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_retains_authoritative_row`,
  **3 results:** canceled, missing, exhausted. Produce and link a real busy queue
  row, then cancel it through queue API, delete just that owned row, or set Pending
  at MaxDeliveryAttempts respectively. Recreate/reconcile/flush twice. Original
  queue link retained, zero replacement inserts/submissions, no confirmation;
  diagnostics are queue_canceled_unconfirmed, keyed_queue_row_missing or
  queue_parked_unconfirmed. Restore the same owned row/state/attempt fields from
  the recorded pre-test snapshot as a fixture control and complete O-receipt;
  production must never perform that restoration. Classification: bounds proof;
  PC-27–29, one mutation for each independently bypassable state.
- **V-8 (new):** absent destination remains owed | Integration |
  `PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_waits_for_destination`,
  **3 results:** missing, Stopped, Failed. Cut after producing the obligation,
  before queue insertion; remove the destination record or terminalize it. Fresh
  recovery persists DestinationUnavailable, no queue/prompt, exact +5 minute due
  and the saved payload/identity. Restore the same test-owned destination and
  runtime registration, advance fake time to due and require O-receipt.
  Classification: preserved debt/recovery proof; PC-30/31. A missing parent at
  production is a separate route exclusion in R-5.

### Guards the regression

- **R-1:** existing caller refusal behavior |
  `RolePolicyToolBundleDispatchTests.the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt`,
  its existing **4 results**. Update only the old reminder assumptions: keyed
  immutable body, null NextCheckAt, notification recovery and full UserPrompt.
  Preserve eligible inline receipt, busy no-typing and both fault-hit assertions.
  Update fixture DI and deletion order. Same V-1/V-2/V-4 recipe and O-receipt;
  classification: changed existing regression, no duplicate new method.
- **R-2:** explicit watchdog notification ID is retained |
  `ReceiptFailureDeliveryTests.Caller_failure_obligation_survives_persistence_cuts`,
  existing **12 results**. Preserve all six cuts x busy/eligible, atomic triple,
  complete prompt above floor, completion stamp, one keyed row and one submission.
  Classification: shared FailAsync regression; PC-9 detects ID replacement.
- **R-3:** new failure uses outbox while historical rows retain reminder behavior |
  rename `AgentTaskDispatchFailureTests.a_dispatch_failure_arms_a_reminder` to
  `AgentTaskDispatchFailureTests.a_dispatch_failure_owns_a_durable_obligation`
  (**1 result**): non-Git workspace refusal must store Failed/event/one obligation
  and null NextCheckAt, without spending CheckCount. Keep
  `AgentTaskDispatchFailureTests.a_lost_failure_note_is_re_sent_on_the_ramp`
  (**1 result**) on its explicitly seeded historical no-outbox task: CheckCount=1,
  one reminder and next ramp due; assert zero DeliveryFailure obligations before
  and after. These are persistence/legacy compatibility controls, not recipient
  proof; V-1/V-2 provide receipt. Classification: changed assertion plus existing
  narrow green; PC-16/32. Do not rewrite historical seed as a new producer failure.
- **R-4:** post-dispatch text and behavior stay compatible |
  `DelegationDispatchRecoveryBoundaryTests.C1186_Recovered_failure_preserves_obligation_guidance`,
  existing **4 results**, retains literal abandoned-event IDs, settlement digest,
  git recovery command, warning/unpushed text and complete real-queue receipt
  (including its spill case). Add the narrow assertion that its non-null
  DispatchedAt, non-watchdog failure creates no new DeliveryFailure obligation.
  In mode one, also run a fresh failure through the same wrapper without an insert
  fault, then flush its directly enqueued note through the registered parent queue.
  Assert literal warning/git text in those submitted bytes; the recovered-note
  case alone cannot detect dropped immediate arguments.
  Classification: shared wrapper formatting/exclusion regression; PC-8/33.
- **R-5 (new):** no caller obligation for excluded routes |
  `PreDispatchFailureDeliveryTests.Pre_dispatch_excluded_routes_create_no_obligation`,
  **3 results:** Board with a valid parent, None with a valid parent, Session with
  null parent. Actual unknown-tool TickAsync must fail with its event, no launches,
  zero obligations/queue entries and null NextCheckAt. Valid-parent Session
  delivery is the companion control in V-1, not another parameter. Classification:
  non-session/null-parent exclusion, PC-6/7.

### Guard inventory

Safety-critical scope is the prospective producer and the delivery/recovery bounds
D-1–D-6 explicitly retain. Unchanged native terminal, scanner fairness, cache and
retention internals are not newly certified. Every guard below has its own PC;
several PCs deliberately use the same method but **no PC maps to two guards**.

| Guard | Plan reference and independently bypassable invariant | Control |
|---|---|---|
| G-1 | D-1: every eligible pre-dispatch failure records an obligation | PC-1 |
| G-2 | D-1/D-2: Failed, event and obligation commit or roll back together | PC-2 |
| G-3 | D-3: each later failure event gets a distinct obligation | PC-3 |
| G-4 | D-2/D-3: retries use immutable saved body/digest | PC-4 |
| G-5 | D-3: retries use the saved destination | PC-5 |
| G-6 | D-6: only Session reply routes create this obligation | PC-6 |
| G-7 | D-6: a parent is required at production | PC-7 |
| G-8 | D-6: ordinary post-dispatch failures are excluded | PC-8 |
| G-9 | D-1/D-6: explicit watchdog ID remains authoritative | PC-9 |
| G-10 | D-2: claim commits before wrapper enqueue on another context | PC-10 |
| G-11 | D-3: immediate enqueue and recovery share notification key | PC-11 |
| G-12 | D-3: busy caller receives no input until eligible | PC-12 |
| G-13 | D-4: mandatory recovery precedes optional CheckEnabled gate | PC-13 |
| G-14 | D-4: dispatcher selects only due obligations | PC-14 |
| G-15 | D-4: exponential retry increments and persists across failures | PC-15 |
| G-16 | D-4: outbox-owned failures never arm a duplicate legacy reminder | PC-16 |
| G-17 | D-5: full body, not just its header, is required | PC-17 |
| G-18 | D-5: receipt belongs to destination session | PC-18 |
| G-19 | D-5: DeliveryFailure requires UserPrompt kind | PC-19 |
| G-20 | D-5: receipt is above recorded sequence floor | PC-20 |
| G-21 | D-5: missing sequence uses bounded attempt timestamp floor | PC-21 |
| G-22 | D-5: an actual delivery attempt is required | PC-22 |
| G-23 | D-5: Sent alone cannot confirm | PC-23 |
| G-24 | D-5: LastPolledResultAt cannot confirm | PC-24 |
| G-25 | D-5: ReadAt cannot confirm | PC-25 |
| G-26 | D-5: owned spill pointer requires matching saved contents | PC-26 |
| G-27 | D-5: canceled authoritative queue row is not replaced | PC-27 |
| G-28 | D-5: missing authoritative queue row is not replaced | PC-28 |
| G-29 | D-5: attempt-exhausted authoritative queue row is not replaced | PC-29 |
| G-30 | D-4: missing destination remains owed and retryable | PC-30 |
| G-31 | D-4: terminal destination remains owed and retryable | PC-31 |
| G-32 | D-4: historical no-outbox reminder still works | PC-32 |
| G-33 | D-2/D-6: shared wrapper retains abandoned-commit warning/git facts | PC-33 |
| G-34 | D-4: direct reconciler also refuses an early unlinked enqueue | PC-34 |
| G-35 | D-4: exponential retry delay is capped at 300 seconds | PC-35 |

### Positive controls

All **35 controls are required to run after implementation land**, in SourceLanding
Mutation, including every argument of the exact detecting method. They are pending
here, not executed by TestDesign or a prerequisite for ordinary Code/Review/land.
Code runs V/R; Review judges this inventory and ordinary evidence before land.
Mutation performs baseline-green, compiling break, intended-assertion red, restore,
fresh-build green. Missing/unreached cases, zero tests, timeouts, fixture/DI errors,
compiler errors or equivalent mutants are not successful controls. Store separate
per-PC counts/assertions/restoration evidence; do not batch different mutations
against the same method/file. Each mutation below is one temporary compiling
source edit; never mutate the test oracle.

Use the exact method in the detecting-filter column for red and restored green;
these PC filters intentionally have **no wildcard**. S1's base-red evidence cannot
replace a PC at the landed implementation. Code must bind final production
file/member and assertion label for every row; new method/payload obligations
stay **binding required** until that evidence exists, even after Code. An unbound
payload or unreachable branch must be reported, not silently classified covered.

| PC | Compiling defect breaking its guard | Detecting filter | Required red assertion / decisive case |
|---|---|---|---|
| PC-1 | FailAsync requires an explicitly supplied ID again | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_commit_keeps_the_caller_obligation` | obligation count 0 != 1 after Failed commit |
| PC-2 | Save Failed/event before adding the obligation in a second save | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_commit_keeps_the_caller_obligation` | failed-committed cut sees no obligation; before-save control still rolls back |
| PC-3 | Skip creating a notification when one already exists for task ID | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_snapshot_survives_task_retry` | two failures must have two distinct linked obligations |
| PC-4 | Reconciler renders current task failure instead of saved Body | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_snapshot_survives_task_retry` | A recipient's complete payload differs from saved A/contains B landmark |
| PC-5 | Reconciler resolves current task.ParentSessionId instead of note.ParentSessionId | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_snapshot_survives_task_retry` | A's original recipient has no complete A receipt |
| PC-6 | Broaden creation route predicate to any ReplyTo | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_excluded_routes_create_no_obligation` | Board/None obligation count is nonzero |
| PC-7 | Remove only parent-present predicate, keeping nullable snapshot fields | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_excluded_routes_create_no_obligation` | Session/null-parent obligation count is nonzero |
| PC-8 | Broaden automatic creation to non-null DispatchedAt | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1186_Recovered_failure_preserves_obligation_guidance` | ordinary post-dispatch DeliveryFailure count must be zero |
| PC-9 | Discard supplied watchdog ID and generate a replacement | `/*/*/ReceiptFailureDeliveryTests/Caller_failure_obligation_survives_persistence_cuts` | queue.SourceLandNotificationId differs from the one saved obligation (or second row appears) |
| PC-10 | Move commitBeforeNotify invocation after immediate enqueue | `/*/*/PreDispatchFailureDeliveryTests/Preflight_failure_obligation_commits_with_its_claim` | credential enqueue observer's committed-triple-visible assertion is false, not timeout |
| PC-11 | Pass null sourceLandNotificationId on immediate failure enqueue only | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_reuses_the_committed_queue_row` | queue key mismatch and/or recovery creates a second row |
| PC-12 | Change wrapper failure delivery from WhenIdle to Now | `/*/*/RolePolicyToolBundleDispatchTests/the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt` | busy immediate producer: submission count must remain zero before TurnEnd |
| PC-13 | Return for CheckEnabled=false before recovery | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_insert_recovers_with_checks_disabled` | no complete recovered caller UserPrompt |
| PC-14 | Remove NextAttemptAt predicate from dispatcher recovery selection only | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_insert_recovers_with_checks_disabled` | dispatcher recovered-count is 1 instead of 0 at due-minus-one-tick |
| PC-15 | Return constant 5 from RetrySeconds | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_insert_recovers_with_checks_disabled` | saved second due delta 5 != 10 (later 300 cap controls also execute) |
| PC-16 | ArmFailureReminder for new outbox-owned failure after saving it | `/*/*/AgentTaskDispatchFailureTests/a_dispatch_failure_owns_a_durable_obligation` | checks-enabled producer leaves NextCheckAt non-null |
| PC-17 | Replace LandNoteReceipt full-body conjunction with header/identity match alone | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | head-only must stay unconfirmed |
| PC-18 | Remove session restriction from LandNoteReceipt.Prompts | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | wrong-session must stay unconfirmed |
| PC-19 | Allow QueuedUserPrompt for DeliveryFailure | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | queued-prompt must stay unconfirmed |
| PC-20 | Change sequence comparison from > floor to >= floor | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | old-sequence must stay unconfirmed |
| PC-21 | Return all candidate prompts when sequence baseline is null | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | old-time must stay unconfirmed |
| PC-22 | Scan/confirm even with DeliveryAttempts=0 | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | no-attempt complete decoy must stay unconfirmed |
| PC-23 | Reconcile marks a Sent row Confirmed without scanning receipt | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | sent-only must stay unconfirmed |
| PC-24 | Reconcile marks note Confirmed when task.LastPolledResultAt exists | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | polled must stay unconfirmed |
| PC-25 | Reconcile marks note Confirmed when task.ReadAt exists | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | read must stay unconfirmed |
| PC-26 | Skip saved-body equality check for the owned spill pointer | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt` | spill-mismatch must stay unconfirmed |
| PC-27 | On canceled row clear authoritative link/key and enqueue a replacement | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_retains_authoritative_row` | canceled case retains original ID and no new insert/submission |
| PC-28 | On missing row clear authoritative link and enqueue replacement | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_retains_authoritative_row` | missing case keeps saved queue ID and zero replacement rows |
| PC-29 | On exhausted Pending row clear link/key and enqueue replacement | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_retains_authoritative_row` | exhausted case retains original attempts/ID and zero extra rows |
| PC-30 | Missing destination sets NotRequired instead of DestinationUnavailable | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_waits_for_destination` | missing case state/owed assertion and later O-receipt fail |
| PC-31 | Terminal destination sets NotRequired instead of DestinationUnavailable | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_waits_for_destination` | Stopped/Failed remain owed and recover at +5 minutes |
| PC-32 | Return immediately after mandatory recovery even when checks enabled | `/*/*/AgentTaskDispatchFailureTests/a_lost_failure_note_is_re_sent_on_the_ramp` | historical CheckCount 0 != 1 / no reminder |
| PC-33 | Wrapper omits warning/git arguments from completion-note construction | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1186_Recovered_failure_preserves_obligation_guidance` | mode one immediate no-fault control loses literal warning/git text |
| PC-34 | Remove the reconciler NextAttemptAt > now early return only | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_insert_recovers_with_checks_disabled` | direct early ReconcileAsync increases insert-attempt counter |
| PC-35 | Remove Math.Min(300, ...) from RetrySeconds, preserving doubling | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_insert_recovers_with_checks_disabled` | attempt seven delay 320 != 300 |

### Out of scope

- Native PTY, real provider, real broker, production runner and process-crash
  qualification: no such transport changes. Fake adapter receipts prove only the
  inspected integration boundary. Source observer exceptions model interrupted
  commits; they are not an operating-system power-loss experiment.
- No historical backfill, new schema, automatic task retry, runtime settings,
  parking, session lifecycle, RC publication/deployment or new scheduler.
- Hosted scanner fairness/timer cadence, cache/pruning internals and other landing
  notification kinds are unchanged. Calling the real dispatcher recovery and
  reconciler proves this producer's durable rediscovery, not host startup wiring;
  the already-inspected scanner remains an independent existing backstop.
- Unrelated Unit/whole assembly/native suites and repeated unchanged green runs
  are excluded. The only existing watchdog sweep is its exact 12-case method.

### Checkpoints

Closed ordinary scope; this table supersedes the candidate selections above.
All rows are portable .NET Integration with isolated PostgreSQL/fake adapter;
local Git variants use the process limiter. Omit Runner/Platform pins. Use one
checkpoint-tool run per committed After group, await completion (including every
exit-75 continuation), pass the committed expected source SHA, and retain clean
source/build/TRX receipts. Tool/build drivers use the host build-slot gate.
Use narrow trailing-wildcard ordinary method selections for TUnit compatibility;
PCs remain exact-method. Group S1 is intentionally red; the same selection is
rebuilt green at S2. No S1 output can certify S2 or S3.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1232-red/` | failure-red | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed*` | V-1, V-2 | all 6 executed; expected assertion reds described above; no skip/build/fixture failure | 6 | 4 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1232-fix/` | failure-green | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed*` | V-1, V-2 | all 6, 0 failed/skipped | 6 | 4 |
| CP-3 | S2 | CP-2 | preflight | `/*/*/PreDispatchFailureDeliveryTests/Preflight_failure_obligation_commits_with_its_claim*` | V-3 | all 6, 0 failed/skipped | 6 | 2 |
| CP-4 | S2 | CP-2 | handoff | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_recovery_reuses_the_committed_queue_row*` | V-4 | all 6, 0 failed/skipped | 6 | 1 |
| CP-5 | S2 | CP-2 | snapshot | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_snapshot_survives_task_retry*` | V-5 | 1, 0 failed/skipped | 1 | 1 |
| CP-6 | S2 | CP-2 | receipt | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_receipt_requires_complete_user_prompt*` | V-6 | all 11, 0 failed/skipped | 11 | 2 |
| CP-7 | S2 | CP-2 | recovery-bounds | `/*/*/PreDispatchFailureDeliveryTests/(Pre_dispatch_recovery_retains_authoritative_row*)\|(Pre_dispatch_recovery_waits_for_destination*)` | V-7, V-8 | all 6, 0 failed/skipped | 6 | 1 |
| CP-8 | S2 | CP-2 | routes | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_excluded_routes_create_no_obligation*` | R-5 | all 3, 0 failed/skipped | 3 | 1 |
| CP-9 | S3 | `tests/Antiphon.Tests -> bin-c1232-contracts/` | role-policy | `/*/*/RolePolicyToolBundleDispatchTests/the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt*` | R-1 | all 4, 0 failed/skipped | 4 | 4 |
| CP-10 | S3 | CP-9 | watchdog | `/*/*/ReceiptFailureDeliveryTests/Caller_failure_obligation_survives_persistence_cuts*` | R-2 | all 12, 0 failed/skipped | 12 | 2 |
| CP-11 | S3 | CP-9 | reminder-contract | `/*/*/AgentTaskDispatchFailureTests/(a_dispatch_failure_owns_a_durable_obligation*)\|(a_lost_failure_note_is_re_sent_on_the_ramp*)` | R-3 | both methods, 0 failed/skipped | 2 | 1 |
| CP-12 | S3 | CP-9 | shared-wrapper | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1186_Recovered_failure_preserves_obligation_guidance*` | R-4 | all 4, 0 failed/skipped | 4 | 1 |

Canonical invocation (through the slot gate for the dotnet driver):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1232-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-20-card-1232-pre-dispatch-failure-notification-plan.md --after S1 --expected-source-sha <committed-sha>
```

Use S2 and S3 for later groups. Preserve CP-1's real nonzero assertion result; the
checkpoint tool must not relabel it green. Final Code evidence comprises the
expected-red witness plus CP-2–12 green, with V/R union complete. Run
scripts/check-evidence-diff.ps1 over base..HEAD and static coverage binding after
implementation; any supporting tool bootstrap must be gated and reported.
Delete only inventoried task-owned alternate outputs after the owning run exits.

### Cost

All times below are **estimated**, not measured; no execution receipt exists yet.
Ordinary Code floor is the CP EstimatedMinutes sum:
4+4+2+1+1+2+1+1+4+2+1+1 = **24 minutes** (67 TUnit executions:
6 expected-red and 61 green). This includes three isolated builds, estimated
3 minutes each, and 15 minutes of selections/fixture setup. Code authoring time
is additional. Exact filter rosters and execution floors are in CP-1–12; assertion
counts/internal retry loops never replace MinExecuted.

Post-land Mutation floor: baseline verification of the distinct detecting methods
at landed L, **13 minutes** with one isolated baseline build; each PC then uses its
exact table filter for red and restore/green. Allow **4 minutes per PC** including
both fresh builds and tests (two 1-minute rebuilds of changed source with the
bound pack's unchanged dependencies retained, plus 2 minutes of tests),
**6 minutes each for PC-9 and PC-10**, and **5 minutes
for PC-33**: 32x4 + 6 + 6 + 5 = **145 minutes**, plus the 13-minute baseline =
**158 minutes**. Include
**3 minutes** extra tooling/evidence setup. Total verification budget:
3 + 24 + 158 = **185 minutes**, excluding Code authoring and Review analysis.
Report actual timings and all failed/rerun costs without erasing estimates.

Build reuse saves nine unnecessary ordinary rebuilds versus one build per row:
9 x 3 = **27 estimated minutes**. No whole-Unit or whole-assembly run is paid.
The two original loss witnesses share one red/green prefix selection; each other
behavior has one narrow ordinary selection. Do not save Mutation cost by skipping
controls or calling a compiler/fixture failure red. Future mutation parallelism is
not assumed in this floor.

Handoff census: guards=35, mapped=35, missing=0, duplicate PC maps=0.
All PCs/variants are scheduled and must run in Mutation; executed here=0.
Code must retain the binding-required list until its methods, payload landmarks,
fault reachability and exact assertion labels have concrete source bindings.
