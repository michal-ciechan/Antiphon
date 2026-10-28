# CARD-1593 S3: atomic consume for standing starts, retained returns and queue attempts

Plan task `30f56bd5`, 2026-10-28. Source baseline and every code line below:
`aa6dfd24a51db7c02e8722a05a21817bf4df407a`. Assigned branch: `feat/card-task-30f56bd5`.
This document changes no product code. Independent design Review is next; verification
design is included so an approved plan can proceed to Code without another TestDesign stage.

Read in full: investigation `origin/feat/card-task-bcb5bc45:docs/investigations/2026-10-28-card-1593-capacity-leaks.md`
and the S1 plan on `origin/feat/card-task-d94003bc`,
[capacity admission and lock order](2026-10-27-card-1593-capacity-admission-lock-order-plan.md).
The investigation describes an earlier tip: its leak 1 is already fixed at this baseline
(`AgentTaskDispatcher.cs:8122-8191`). Leak 5, quota-reply/fence order, belongs to its separate plan.
Neither is reopened here. The S1 lock ledger remains the global reference; this plan records
the additional transaction edges introduced by S3.

## Ground truth

`Services/` below means `server/Application/Services/`; test names are under
`tests/Antiphon.Tests/Application/` unless another location is named.

| Card/investigation assumption | Code at the baseline | Design consequence |
|---|---|---|
| Dispatch still consumes before claiming | False after S1: `AgentTaskDispatcher.cs:8130-8194` consumes and receipts inside the launch transaction; pre-gate `:964-966` does not consume. The non-Postgres commit bound is already 2. | Preserve S1 and its 175-result regression roster below. Do not transplant the older D-9 commit catch. |
| Leak 2 is a supervisor cross-connection deadlock | False: `AgentSupervisorService.cs:226-227` commits/disposes its observation transaction before `RedeemAsync` at `:507-508`. | Preserve that separation. The defect is premature consumption, not an existing provider/agent cycle. |
| Supervisor acceptance is one simple insert | `AgentControlService.cs:543-887` reserves either a new session or a new generation of an existing session. `AcceptAsync` writes the agent, supervision intent, pending-message transfers and incidents. Fresh capacity recovery currently requires an already `Admitted` wait at `:602-614`. | Move both resume and fresh consumption after the final acceptance flush, before their commits at `:687` and `:797`; update the fresh prerequisite. |
| A stamped action key proves a start happened | `AgentSupervisorService.cs:512-522` stamps the old session and intent before compaction refusal or start. | An action key alone is selection, not acceptance. Stamp only the accepted generation in the reservation. |
| Leak 3's first local count makes the return safe | The snapshot is at `AgentTaskDispatcher.cs:510-516`; `AfterQueuedSnapshotAsync` is at `:537-538`; redemption commits at `:633-635`; recount can refuse at `:636-639`. `TryClaimCountedSlotAsync` takes an xact advisory without requiring a transaction (`CapacityRecoveryService.cs:845-863`). | One transaction must cover the counted lock, fresh count, retained flag and consume. |
| All ordinary claims already take the counted lock | False: `AgentTaskDispatcher.cs:5544-5554` takes it only for specialist-input claims. | Local counted claims must join the same lane as retained returns, or a new claim can still race a return. |
| Retained return means launching a process | It clears `CapacityWaitRetained` on an already Working task (`:640`), saved only at `:652`. The runner-bound branch already checks host occupancy before redeem (`:618-630`). | Receipt binds the existing session/generation and counted return. No new process and no seat-release authority. |
| Leak 4 has the capacity attempt cap | `ApplyCapacityHoldAsync` writes `SelectedMessageId` at `SessionMessageQueueService.cs:5572` before redemption at `:2586`. `HasExecutionReceipt` treats that selection as a receipt; `RearmStalledAdmissionsAsync:1347-1356` revalidates without another capacity attempt. | Stop pre-stamping a receipt. An admitted queue attempt must have a committed queue attempt as well as the wait receipt. |
| Queue `Sent` means successful delivery | False: `:2726-2749` saves Sent, attempt count, baseline and generation before `DeliverAsync:2761`. Spill hold can fail earlier at `:2675`; transport/verdict failures occur later at `:2778-2869`. | Consume with the durable attempt, not with a later success verdict. Database atomicity cannot include terminal input. |
| Reconciliation is a provider-only leaf | Not for queue rekeying: `PrepareNextAttemptOnAsync:1401-1407` dirties queued messages under the provider held by grant expiry (`:141`) or stalled rearm (`:1342`). | Remove that provider-to-queue write before introducing queue-to-provider consume. Repair cached selection keys at the consumer. |
| Generic interrupted-launch recovery can relaunch a cardless start unchanged | `AgentSessionService.cs:961-980` requires a dispatched task to attach; a cardless interrupted launch is failed for clean relaunch because its launch notes are not durable. | Do not promise same-generation relaunch through that method. Retain its behavior and explicitly rearm a terminal, accepted standing generation under D-3. |
| Leak 6 still spends during `HeldForRemotePrep` | False: `AgentTaskDispatcher.cs:6021-6026` commits Queued without calling consume. The reservation survives until preparation completes or reconciler-only D-12 expiry (default 120 s), whichever wins. | Release this unspent reservation in the preparation commit and defer regrant until preparation readiness. Do not consume or extend a timestamp. |
| `LaunchReceipt` can hold an arbitrary recovery document | It is limited to 80 characters (`server/Infrastructure/Data/AppDbContext.cs:2915`). Existing wait columns include `SessionStartedAt`, `SelectedMessageId` and `DispatchAttemptId`. | Use a bounded versioned receipt discriminator/floor, with identities in existing columns; no migration. |

## Decisions

### D-1. Acceptance is the atomic boundary

A capacity admission pays for a durable executable action: a committed launch generation,
a committed return to counted execution, or a committed queue delivery attempt. It does not
claim that a process ran or a prompt arrived. All three commit the consumer change and the
wait's `StartAccepted` receipt together. Rollback means neither happened. Actual delivery
still requires the complete matching `UserPrompt`; successful work remains a separate event.

Keep the own-scope `RedeemAsync` wrapper for the existing service-level compatibility tests
and legacy recovery contract. Remove it from the three S3 production paths. Do not change
every caller of `TryRedeemCapacityWaitAsync` through one default parameter: dispatch remains
a non-consuming gate, and retained return gets its own transaction helper.

Rejected: redeem then refund on every exit (crash gap, incomplete exit lists); post-commit
receipts (same gap); consuming only after external delivery (unpaced input if the grant is
revoked during delivery); holding the provider lock across runner/network/terminal I/O.

### D-2. Lock ranks and Rule P

Retain S1's order. Within rank 4, acquire sessions in ID order before queue rows in ID order.
Task rows needed by a transaction are locked before sessions; multiple peer tasks use ID
order, except S1's existing claimed-task/previous-token-owner protocol, which is unchanged.

| Rank | Resource and baseline anchor | S3 use |
|---|---|---|
| 0 | Card row, S1 ledger | No new card lock in S3. |
| 1 | Counted-task advisory `antiphon.capacity.counted-tasks`; dispatcher `:5548`, recovery `:851` | Local retained return holds it until commit/rollback; helper refuses an absent transaction. |
| 2 | Standing-specialist owner; S1 ledger | No new owner-first protocol. Existing specialist exception E-1 remains detectable, not expanded. |
| 3 | Task row; dispatcher claim `:5556` and retained edits `:640` | Retained task; remote-preparation task; any Check tasks remapped by control `:754-758`, prelocked before sessions. |
| 4 | Release/session/queue rows; control `:547`, queue session-lock helper `:5658-5662` | Existing/new launch generation; selected queue row; retained session. Existing session is `FOR UPDATE`, not `KEY SHARE`, when generation/status is the predicate. |
| 5 | Physical agent; control `:549`, stop `:1277-1283` | Supervisor start and its supervision intent. Queue transfers must be selected/locked before this rank. |
| 6 | Parallel-task advisory; S1 ledger | No new S3 acquisition; remote prep retains the enclosing claim's existing order. |
| 7 | Provider advisory, ascending kind; recovery `:332` | Last non-capacity lock in each consume/defer transaction. |
| 8 | Capacity wait/provider/hold rows; recovery `:334-388` | Receipt, grant clear, admission count, or an unspent defer/rearm. |

**Rule P: no non-capacity row writes under the provider lock.** S3 is stricter than the S1
exception for already-locked rows: flush all consumer/child rows first, then take rank 7,
write only rank-8 rows, then commit. Extend `RejectDirtyLaunchRows:454-460` for consume to
reject *any* Added/Modified/Deleted non-capacity entry, including queued messages, supervision
state and incidents. A helper does not repair a dirty tracker by saving under the provider.
The consume's capacity-only save must not run a task/session fence. S3 contexts have no
`RuntimeActionTaskFence`; queue receipt effects run after the transcript transaction, as the
runtime owner requires. Do not change the quota/fence code in this task.

**Rule X:** never hold consumer locks on one connection and await a provider lock on another.
The supervisor's observation commit remains before control's reservation. The retained
helper owns a fresh scope before taking any locks. The queue uses its current scope/owned
transaction. In-process queue semaphores are acquired before DB locks; never acquire a
queue semaphore while holding the provider. No runner call, file write, alert or event-bus
publication occurs under rank 7. Capacity lifecycle event/incident rows are saved after
commit on a fresh scope, with their existing deduplication, not inside the receipt save.

| Leak/path | Target sequence and exact consume/release boundary | Receipt / rollback |
|---|---|---|
| 2 supervisor resume | Existing queue gates -> optional Check task locks 3 -> sessions 4 -> transfer queue rows 4 -> agent 5 -> `AcceptAsync` flush `AgentControlService.cs:887` -> provider 7 -> capacity 8 -> commit `:687` | StartAccepted + session ID + **accepted** StartedAt + action key + attempt ID. Resume failure before commit restores old generation and grant. |
| 2 supervisor fresh | Same order; replace the old `Admitted` test `:602-614`; consume after `AcceptAsync:796` completes and before `CommitAsync:797` | New Starting row, agent pointer, bounded-fresh allowance and receipt commit together. Queue transfer is in that transaction. |
| 3 local retained return | Counted 1 -> retained task 3 -> existing session 4 -> fresh local count -> clear flag and flush -> provider 7 -> capacity 8 -> commit | StartAccepted bound to existing session and generation; `OutcomeReason=Dispatch`; no launch. Count refusal/late consume refusal rolls everything back. |
| 3 runner-bound retained return | Task 3 -> session 4; existing runner occupancy checks remain before acceptance, reread task runner/status -> flag flush -> 7 -> 8 -> commit | Same atomic receipt. Does not claim to make the remote runner's external seat inventory transactional or reserve a second process seat. |
| 4 queue first attempt | Session semaphore -> any execution-task locks 3 needed by expiry/frozen-rendering code -> session 4 -> selected run rows 4 -> stamp/freeze/flush `:2726-2749` -> 7 -> 8 -> commit -> delivery | Receipt names selected head, session generation, attempt ID and original transcript floor. No input before successful commit. |
| Queue rekey / expiry / new wall | Grant/rearm stays 7 -> 8 only. Queue consumer later takes 4, refreshes its obsolete unattempted key, flushes, then consumes 7 -> 8 | Remove queue writes `CapacityRecoveryService.cs:1401-1407`; no new cycle between reconciler and queue. |
| 6 preparation hold | Enclosing claim ranks 1/2/3 as applicable -> preparation metadata flush `AgentTaskDispatcher.cs:6021` -> 7 -> 8 defer/clear grant -> same `:6022` commit -> `TryBegin:6025` | No execution receipt, no admission increment. Failed commit restores original reservation. |
| 6 prepared re-entry | Fresh task 3 -> verify prepared path/current route -> 7 -> 8 Ready -> commit; then ordinary grant/claim on later tick | No consume at readiness. Only S1 launch commit consumes. |

Control already writes Check task mappings after taking sessions/agent. For a capacity
start that can reach `:754`, discover the candidate task IDs before reservation, lock them
at rank 3, then verify the set under the session/agent locks. A changed set refuses and
retries without consumption; never discover and lock another task after rank 5. For pending
message transfers at `:635-645`, likewise lock/reload the candidate queue rows before the
agent lock and recheck their identity/attempt state afterward. Existing S1 specialist E-1
(session-to-owner against owner-to-session) is not silently declared fixed: a victim is D-9
class 2. Ordinary standing starts use session-to-physical-agent, matching CARD-1190.

### D-3. Supervisor start and terminal-generation recovery (leak 2)

Replace `TryHandleCapacityWaitAsync:507-526` with selection and a non-consuming gate. Pass an
internal immutable capacity ticket `(waitId, actionKey, executionKind)` to control for both
ordinary automatic resume and supervised fresh; do not add client-selectable authority to
`StartAgentRequest`. Under the reservation, revalidate owner, target generation, kind,
suspension, liveness latch, compaction and bounded-fresh eligibility before any consume.
The fresh path must no longer require pre-existing `Admitted` or a pre-stamped intent.
The final provider-locked recheck is authoritative; an earlier ticket never guarantees admission.

A capacity ticket also refuses pending card work at `AgentControlService.cs:280-282`, before
the alternative `CardService.SpawnAsync:302` path. That path lacks the matching cardless
reservation and must not launch ungated. Ordinary/manual card starts keep their behavior.

Move old-session/intent action stamping into `AcceptAsync` on the **accepted** session.
Duplicate action keys on an old failed session without a matching receipt do not prove
execution and must not strand the wait. A matching accepted receipt makes the operation
idempotent: no new generation, count, incident, allowance or enqueue from the second caller.
The two final acceptance flushes precede consume; enqueue remains after commit.

`StartAccepted` is not in the legacy Admitted rearm query. Therefore add an explicit
standing receipt reconciliation branch, reached before the supervisor's unfinished-wait
early return: discover outside locks; then session -> agent -> provider, reload the wait.
Only a receipt for the current action, owner, session and normalized StartedAt may be
examined. A Starting/Running generation is never rearmed by elapsed time alone. A proven
terminal **matching** generation, with no healthy/work progress, can prepare the next
action once, retaining AdmissionCount and applying the existing restart-backoff due time
and MaxAttempts cap. Clear the old action's receipt fields when advancing its ordinal.
A repeated observation of the old generation cannot advance twice. Explicit Stop/suspension,
compaction, continuity and bounded-fresh brakes still win; no new automatic kill is added.

A crash/lost enqueue after commit leaves the accepted generation and receipt. Existing
session reconciliation determines its actual outcome; the cardless interrupted-launch
path may fail that generation for clean relaunch (`AgentSessionService.cs:967-980`). Only
then does this terminal-generation branch permit a new action and another admission.
Do not claim that `ResumeInterrupted` reconstructs undurable cardless launch notes.

Rejected: just replacing `RedeemAsync` with `RedeemOnAsync` in the supervisor (wrong DbContext
and no start transaction); accepting only fresh starts (resume also mutates StartedAt);
leaving StartAccepted outside all terminal recovery (new permanent standing-agent stall).

### D-4. Retained return is an atomic counted transition (leak 3)

Replace the redeem/count/flag loop body `AgentTaskDispatcher.cs:633-652` with
`TryReturnRetainedAsync` owning one fresh context/transaction per task. Hold the local
counted advisory before the task lock, reload the task and referenced wait, then lock/recheck
the retained session's current generation. Validate Working, retained flag, consumer identity,
task attempt, kind and runner. A missing/mismatched session or current wait refuses; it must
not be interpreted as a free pass. An exhausted current episode likewise cannot fall through
to the helper's old `wait is null => true` behavior. Historical capacity IDs on ordinary
non-retained tasks still count normally.

The first snapshot remains a cheap skip, not authority. The local recount and flag save are
inside the same counted transaction. Flush the flag before `RedeemOnAsync(consume:true)`;
pass the existing session plus receipt metadata D-7. Commit, then increment the tick's local
count/resumed-host accounting and reload its original tracked instance. A failed return
must not detach or dirty the tick's unrelated queued objects. A second concurrent tick
sees the committed flag and does nothing. A second different retained task sees the updated
count before spending. Keep retained-before-new-queued ordering even when `queued.Count==0`.

Extend claim entry `:5544-5554` to take the counted advisory for every local non-specialist
claim entering the counted population, not only specialist-input claims. Before its first
consumer write, recheck the same local count/limit under that lock; a full lane abandons
without consumption. Preserve the existing specialist counted/owner order and take the key
only once. This serializes ordinary new claims with retained returns across ticks. The
common-path change requires all 97 dispatch guards and the S1 lock/receipt regressions.

Remote occupancy stays a conservative precheck using the current declared/configured host
limit and `excludeRetainedSessions:true`. No runner HTTP call occurs under provider or
counted locks. This slice fixes atomic flag/receipt on remote returns too; a fleet-wide
seat-allocation protocol is not invented here.

Rejected: releasing the grant at the `continue` after the recount (still spends and races);
making only the advisory transaction-scoped but consuming in another scope (Rule X).

### D-5. Queue acceptance, retries and cached selection (leak 4)

`ApplyCapacityHoldAsync` and test-support `StampQueueActionAsync` may bind selection keys
on the message, but may not set `wait.SelectedMessageId`. Selection must not create an
execution receipt. Preserve ordering/held headers and human Ui/Supervision bypass semantics.
Remove the own-scope redeem block `SessionMessageQueueService.cs:2579-2591`; a
non-consuming gate may cheaply refuse before spill work, with no consumer transaction held.

After all pre-attempt checks, spill preparation, pointer recovery and suppression checks
(`:2668-2717`), begin the acceptance transaction. Lock/reload its bounded run and generation
in D-2 order; recheck pending state, original attempt identity, selection, task expiry and
current generation. A capacity-selected head is a one-row run: never batch an unselected
held row under its grant, and never fold two capacity actions into one receipt. Other runs
retain existing batching. Complete all attempt/frozen-rendering writes, flush, then consume
the same wait/key with a queue receipt, then commit. Provider lock never encloses `DeliverAsync`.
Flush failure, spill hold failure or late grant refusal before this commit leaves no new
admission, delivery attempt, SelectedMessageId or typed bytes.

After the attempt commit, transport failure, shutdown, truncation or an uncertain receipt
does **not** refund admission. It is a durable attempt now, exactly as S1's accepted launch
survives enqueue failure. Preserve `RevertRunAsync`, attempt baselines, Enter-only recovery,
complete-prompt matching, spill custody, generation checks and the queue's existing
`MaxAttempts`/park/cancel policy. A matching S3 receipt authorizes recovery of **that row and
action**, without calling `RedeemAsync` again or waiting 120 seconds for capacity revalidation.
Known unwritten/temporarily unreachable cases keep the existing queue refund/deferral
rules; those are not an unlimited series of new provider admissions. Unknown input is never
retyped merely because capacity was released. At the queue's attempt cap, retain its existing
human-owed parking behavior; no automatic capacity regrant restarts the row.

Replace reconciler queue rekeying (`CapacityRecoveryService.cs:1398-1407`) with consumer-side
repair. Under queue locks, a never-attempted selected row can follow the same wait's current
ActionKey/Version after expiry or a new wall; the consume then rechecks that key under rank 7.
An attempted row must first pass existing late-confirm/composer/generation recovery. Never
overwrite its original attempt coordinates to make it look unattempted. If a new wall has
invalidated its old receipt, new input requires the new grant; an old receipt can still
confirm old input but cannot authorize another provider action. `PrepareNextAttemptOnAsync`
becomes capacity-only for all its callers, including `BumpWaveAsync`.

Legacy `Admitted + SelectedMessageId` is not automatically accepted as S3 authority. Preserve
legacy revalidation: it must obtain a matching revalidation grant, then commit a real queue
attempt/receipt using the existing no-extra-count revalidation rule. A selected row with
zero attempts is not inferred delivered. No historical count is refunded or invented.

Rejected: consuming in the delivered-verdict save (too late to pace external input);
treating the selected ID as unconditional retry authority; holding a DB transaction across
receipt polling; retaining eager reconciler rekeying (provider-to-queue cycle).

### D-6. Release, then reacquire after remote preparation (leak 6)

This is a reservation optimization, not a correction to S1's admission count. At
`HeldForRemotePrep`, after `:6021`'s metadata flush and before `:6022`'s commit, call an ambient
`DeferForRemotePreparationOnAsync`. Under rank 7 it rechecks wait/key/current Queued task
identity, clears only the matching grant and sets the wait `Deferred`, reason
`remote-preparation`. AdmissionCount, ActionOrdinal, ActionKey and the provider's admission
clock do not advance. No receipt is written. Another eligible wait can be granted immediately.

The existing queued dispatcher scan owns restart recovery; an in-memory callback is only a
wakeup. On a `remote-preparation` wait:

1. Honor runner drain/unavailability, in-flight preparation and `DispatchNotBeforeAt` first.
2. If the committed task still lacks a prepared path and the recorded workspace is valid,
   call the existing preparer's `TryBegin` outside DB locks and remain Deferred. This runs
   before the normal capacity gate, so a crash between commit and `TryBegin` cannot deadlock
   preparation behind its missing grant. Failure/backoff uses the existing preparer policy.
3. Once the task has its current runner's prepared path and passes readiness, take task ->
   provider in a short fresh transaction, recheck marker and path, and change Deferred to
   Ready with reason `remote-preparation-ready`, `DueAt=now`. Do not set a grant directly.
   Grant ordering treats this reason like `grant-expired` and uses DueAt, preventing repeated
   preparation from jumping ahead of already-ready peers. Add it to both the SQL order and
   `CapacityRecoveryPolicy.GrantOrderAt`.
4. The normal grant, gate and S1 claim later launch from the recorded mirror exactly once.
   Rerouting/supersession clears or supersedes this marker through its existing episode path;
   a canceled/failed task is handled by the existing orphan sweep. SourceLanding exclusions
   at `:6017` are unchanged.

Keep D-12 for ordinary idle unspent grants: expiry is enforced only by the reconciler, and an
expired-but-unrevoked grant can still be consumed. Do not extend `GrantedAt`, add a lease
heartbeat, or couple grant validity to process-local `IsInFlight`. The preparer's push budget
is 20 minutes (`DelegationSettings.cs:705`), not evidence for holding a provider that long.
Preparation p95 remains useful operational evidence, but is not a prerequisite to releasing
an unspent grant. Measure ready-path and failure/backoff paths separately if a later extension
proposal is commissioned.

### D-7. Receipt identity and transcript floor without a migration

Add `Services/CapacityConsumeReceipt.cs`: an internal typed receipt descriptor and strict
codec. Use existing columns: `SessionId=LaunchSessionId`, `SessionStartedAt` (normalized),
`DispatchAttemptId` (stable for the accepted action), `SelectedMessageId` only for a queue
attempt. `LaunchReceipt` is bounded ASCII `s3:<start|return|queue>:<floor|unknown>`, at most
80 characters; parsing rejects unknown versions/malformed values. The session ID, generation,
wait/action and selected row stay in their existing columns. Do not put JSON in the 80-char field.
Capture the committed transcript max sequence before acceptance; queue uses its existing
attempt baseline. A missing observable floor stays unknown, not invented zero.

Extend `RedeemOnAsync` with optional internal receipt metadata; S1 callers retain their
current API/semantics. S3 requires valid receipt metadata. After provider reload, verify
kind, action, consumer binding and expected generation/attempt before incrementing. Update
capacity fields only. Matching accepted identity is an idempotent recovery result, not a
second increment; mismatched accepted identity refuses. No generic `StartAccepted => OK` shortcut.

For S3-marked waits, `ObserveTranscriptAsync`/confirmation must use the accepted generation
and floor. Queue confirmation additionally uses that selected row's complete prompt matcher;
an arbitrary later UserPrompt is not its delivery receipt. Return/start work progress requires
a non-error report boundary beyond the accepted floor in the matching generation. Unknown
floor permits only the existing positive correlated evidence, never a negative inference.
Take session -> provider and reread the action before a S3 state transition, so replay or a
new wave cannot resurrect an old receipt. Commit capacity rows before lifecycle notifications.
Legacy/S1 receipts retain their existing interpretation; no destructive backfill.

`TranscriptEntry` has no generation column. Pass the durable
`SessionRuntimeAction.AcceptedGeneration` from `AgentSessionRuntime.cs:398-402` into the
S3 observation overload. Do not substitute current StartedAt for the event generation.
The older direct helper `:766-775` must supply proven generation from its persisted action
or defer S3 progress as unknown. No schema change. Exercise deferred old-action replay
and unknown generation in V2.6; a session-current check alone cannot reject old events.

### D-8. Scope, explicit defaults and release path

Defaults for implementation: capacity remains opt-in under existing settings; no schema,
new scheduler, retry setting, fleet pin, change to parking or broad lock-ledger project.
The strict S3 receipt codec is internal. Existing `RedeemAsync` compatibility semantics and
the shipped S1 fault bound remain. These are concrete design choices for Review, not pending
operator questions.

S3 adds no new session type that waits for input. It does preserve existing parked queue
rows and retained sessions, and deterministic failures below can require a decision.
**Release checklist:** a failed standing start has no newly launched process; a retained
Working session keeps its current owner/seat; a queue row at its cap retains the existing
session. Explicit operator recovery/retry resumes work, and explicit cancellation/stop or
the existing terminal release path releases a seat. With parking disabled (CARD-1083),
there is **no automatic release deadline**. Capacity grant release is not session release.

### D-9. Failure contract for every S3 transaction

The unfiltered failure boundary includes consumer prelocks/rechecks, consumer flush, capacity
save and commit. Never depend on a caller's filtered Worktree catch. It owns rollback and
disposal; discard failed scope state, then reload shared caller instances. Never leave a
tracked receipt available for an unrelated later SaveChanges to resurrect. Cancellation
rolls back and propagates; cleanup uses a short bounded independent token.

| Class | Decision and durable outcome |
|---|---|
| 1: ordinary refusal | Compaction/continuity/intent refusal, full counted slot, invalid selection, unavailable spill, missing/mismatched grant, feature disabled or clock-not-due: no consume. Before a consumer commit, rollback leaves the original grant/count and consumer state. A durable defer is a separate provider leaf, not a partial consumer save. Grant expiry alone is still not a consume refusal (D-12). Remote-preparation defer is the explicit successful non-launch commit D-6. |
| 2: transient DB error | SQLSTATE `40P01`, `40001`, `55P03` anywhere in the boundary: abandon and retry via the normal caller tick/queue wakeup. Count and receipt do not change; no crash ladder/queue attempt is charged for an uncommitted action. Keep a deduped diagnostic outside the failed transaction. |
| 3: commit acknowledgement uncertain | Dispose the failed transaction, then use a fresh context and the same consumer row locks (without provider) to read the durable consumer **and** matching wait receipt. This lock rendezvous serializes behind the old transaction; an unlocked early read is insufficient. Matching committed tuple means accepted once, never fail/refund or consume again. Supervisor leaves launch outcome recovery to D-3; retained observes its cleared flag; queue reconciles its original attempted row before more input. If the locks cannot be acquired or the DB cannot establish the outcome, defer with uncertainty and no new action. Once the locks prove the old transaction ended without acceptance, transient errors are class 2 and other errors class 4. Do not copy S1's historical unbounded non-Postgres catch or alter its shipped bound. |
| 4: deterministic failure / proven rollback | Rule-P violation, non-transient receipt/consumer save failure, or non-transient commit failure proven rolled back: after abandonment take a fresh short provider transaction and conditionally finish only the same unaccepted `(waitId, actionKey)` as `Exhausted`, reason `supervisor-consume-failed`, `retained-consume-failed`, `queue-consume-failed` or `remote-preparation-failed`; clear its matching grant and preserve AdmissionCount. An accepted/newer action is untouchable. Existing exhausted attention presents the actual reason; do not pretend the count hit MaxAttempts. Supervisor and retained lookup must recognize this current exhausted episode and refuse automatic bypass. Queue stays held. Preserve processes; this is a decision state, not automatic kill authority. |
| After accepted commit | Runner/transport/verdict failure cannot undo a committed admission. Apply standing terminal-generation policy or existing queue attempt/receipt recovery. Never convert a successful commit into class 4 because enqueue, publication or alerting threw. |

The class-4 choice uses the existing terminal/attention model rather than adding a migration
or durable fault counter to three consumers. Add a conditional `FailUnacceptedActionAsync`
provider leaf shared by S3; it must protect accepted receipts and newer action keys. Update
`AttentionService.BuildCapacityRecoveryExhaustedItemsAsync:2817-2847` only for these failure
reasons. Existing max-attempt text and actions stay. Explicit manual continuation supersedes
the failed episode through the relevant consumer's existing recovery path; internal automatic
callers must not treat absence from `Unfinished` as permission. No model/runner fault is
silently classified as a permanent database consume fault: preflight infrastructure failures
keep existing bounded backoff, occur before consumption and remain separately diagnosed.

### D-10. Shipping

Each reviewed product slice is an rc bug fix and ships to the caller-selected current rc
line first, then master with the identical executable/test patch. This plan's baseline is
`rc/2026-10-27`; do not assume that is still the landing target when Code settles. Plan and
review documentation goes to master; include it in rc only if the release branch's docs
policy permits it. No S3 product feature is master-only. Leak 5 remains master-first in its
separate plan, then its reviewed rc port. No changes to leak 1.

This task publishes only its assigned branch. The live native qualification `28bbb58e`
(desktop supervisor PID 243364) is outside this work: no native tests, native lock,
restart, tags, publish/release scripts or rc rebase. Linux tests qualify these transaction
paths, not a native Windows release. Landing/activation is a separate caller operation.

## Slices and exact change list

Commit the slice's failing tests before product code, push each commit. One product commit
per slice is preferred; keep its reported test SHA exact. Shared helpers are introduced with
their first caller, not as an untested preliminary refactor.

| Slice | Product files and edits | Test files / obligation |
|---|---|---|
| S3a supervisor | `Services/CapacityConsumeReceipt.cs` (new typed receipt/floor codec); `CapacityRecoveryService.cs` (metadata consume overload, capacity-only guard, conditional failure leaf, receipt transitions and terminal-standing rearm); `AgentSessionRuntime.cs` (carry durable accepted generation to S3 observation); `AgentSupervisorService.cs` (ticket gate, no pre-stamp, accepted/failed-episode handling); `AgentControlService.cs` (internal ticket plumbing, resume/fresh acceptance and prelocks, atomic consume); `AttentionService.cs` (consume-failure reason) | New `S3CapacitySupervisorTests.cs`; `CapacityRecoverySupervisionTests.cs`; `StandingRestartEscalationTests.Capacity.cs` and `.CapacityResume.cs` strengthen receipts; `CapacityRecoveryAttentionTests.cs` gains one failure-reason case. CP-6, CP-10, CP-13, CP-14. |
| S3b retained | `AgentTaskDispatcher.cs` (fresh-scope return helper, counted/consumer transaction, no missing/exhausted-wait bypass, reload tick identity); `CapacityRecoveryService.cs` (`TryClaimCountedSlotAsync` requires ambient transaction; return receipt metadata through S3a helper) | New `S3CapacityRetainedReturnTests.cs`; `CapacityRecoveryTaskTests.cs` upgrades described below. CP-7, CP-11. |
| S3c queue | `SessionMessageQueueService.cs` (selection, one-row capacity run, attempt transaction, receipted retry and legacy revalidation); `CapacityRecoveryService.cs` (selection-only stamp, remove reconciler queue writes); `CapacityConsumeReceipt.cs` (queue matching through existing receipt matcher, no new delivery oracle) | New `S3CapacityQueueTests.cs` and `S3CapacityLockOrderTests.cs`; update `CapacityRecoveryQueueTests.cs` and `CapacityRecoveryGrantLivenessTests.cs`. CP-8, CP-9, CP-12. |
| S3d preparation/final regression | `AgentTaskDispatcher.cs` (defer inside hold commit, durable-marker recovery before capacity gate, readiness re-entry); `CapacityRecoveryService.cs` (ambient defer/readiness helpers, capacity-only writes); `CapacityRecoveryPolicy.cs` (ready rejoin ordering) | New `S3CapacityRemotePreparationTests.cs`; `CapacityAdmissionLifecycleTests.Remote.cs` revised as below; final CP-1..CP-5 and CP-15. |

All paths above are relative to `server/Application/Services` or `tests/Antiphon.Tests/Application`.
`TestHelpers/CapacityRecoveryTestSupport.cs`, `TestHelpers/DispatcherAdmissionFixture.cs`,
the existing supervisor harness in `tests/Antiphon.Tests/Agents/AgentSupervisionTests.cs`,
and `TestHelpers/BridgeQueueHarness.cs` may gain narrowly scoped interceptor/barrier injection
and phase recording. Prefer existing hooks (`AfterQueuedSnapshotAsync`, `LandDeliveryBoundary`,
EF interceptors) to production sleeps. No generated card, settings file, migration or runner
implementation is changed.

### Existing tests that must change, and the assertions retained

Every dispatched task still passes the shared claim/consume machinery, even if it has no
capacity wait. A helper/DI change can affect the full fleet. The five full existing classes
are required, not replaceable by a few new happy paths.

- `CapacityRecoverySupervisionTests.Card0412_D8_stalled_standing_start_rearms_and_actually_starts_again`:
  replace `HasExecutionReceipt == false` with StartAccepted, accepted generation and count 1.
  Keep the real fake-adapter start, failed-generation observation, second actual start and
  count 2; explicitly prove exactly one rearm and no rearm for a still-live generation.
- `Card0412_V18_accepted_action_key_is_not_relaunched` currently ends with the vacuous
  `provider count >= 0`. Replace it with a real accepted receipt fixture, unchanged
  generation/count and zero second enqueue. Add the complementary stale-key/no-receipt
  refusal case in the new supervisor class. Do not use a bare key as accepted authority.
- `StandingRestartEscalationTests.C510_S2_capacity_grant_is_required_and_follows_fresh`
  (Claude/Grok) and `C510_S2_cleared_model_hold_grant_starts_resume_on_the_next_tick`:
  preserve no-start-without-grant, fresh/resume argument, old-session retention, bounded
  fresh allowance and null prompt-confirmation assertions; add the atomic receipt tuple.
  The model-disabled bounded-retry method remains unchanged and is selected too.
- `CapacityRecoveryTaskTests.Card0412_V19_try_claim_ignores_retained_waits` and
  `...try_claim_counts_historical_capacity_wait_id`: wrap calls in explicit transactions;
  retain exact population/count assertions and add advisory lifetime assertions. Upgrade
  `...retained_return_beats_older_queued` from query-only setup to a real dispatch tick:
  one freed slot goes to the retained task, the older queued task remains queued. Seed a
  valid session and granted episode, rather than weakening the new missing-wait check.
- Queue V12 delivery and unselected-row tests must exercise **production** auto-selection
  at least once, not only `StampQueueActionAsync`; retain body/marker, order, origin, hold
  reason and unselected Pending assertions; add complete prompt receipt and single consume.
  The direct stamp helper remains tested as selection-only. V13 still proves enqueue is
  not prompt confirmation.
- `CapacityRecoveryGrantLivenessTests.Card0412_D6_revocation_rearms_and_updates_cached_consumer_keys`
  (expiry and wave): keep ordinal, due time, count 0, old-key refusal and unchanged body/attempt
  assertions. Now assert the cached queue key stays old after reconciliation, then follows
  the new key under consumer selection before its first accepted attempt. Other legacy
  revalidation/fairness tests retain their existing wrapper semantics and receipt assertions.
- `CapacityAdmissionLifecycleTests.Remote.cs` retains all six result slots. The four crash/
  delivery cases now expect unspent Deferred with no grant during preparation, then explicit
  readiness/regrant before launch; retain count 1, one mirror and complete UserPrompt.
  The two expiry-order cases move their clock race **after preparation readiness and a new
  grant**, preserving D-12's reconciler-first versus consume-first assertions. Do not delete
  either order or replace transcript proof with a launch counter.
- `WallRerouteDispatchTests` (15), `ReceiptFailureDeliveryTests` (21), the other lifecycle
  cases, existing lock cases and `AgentTaskDispatchBaseGuardTests` (97) keep their assertions.
  In particular retain S1's transient receipt failure, lost-ack, second non-Postgres fault,
  caller failure obligation, no dropped brief, supersede and stale-tracker witnesses.
  A new unexpected assertion incompatibility is a substantive plan amendment, not permission
  to loosen expectations or add retries.

## Verification design

The following new witnesses are designed to be **red at the stated baseline**. Code must
commit and execute them before each product slice, with the named assertion failure. This
Plan did not run them. Zero tests, compilation/fixture faults and missing new API symbols
are not red evidence: use existing public behavior and interceptors for the red version.
When a new seam is necessary, its test-only plumbing must compile at base without changing
product behavior. New tests use isolated database clones and fake runner adapters, no real
provider or production runner. Counts below count argument results, not method declarations.

| ID / new class | Exact method and results | Required witness and why red at base |
|---|---|---|
| V2.1 `S3CapacitySupervisorTests` | `C1593_S3_supervisor_refusal_preserves_reservation` (2: compaction, fresh-refused) | Drive real supervisor/control handoff. Count 0, ActionPending, matching grant, old generation/allowance unchanged and no enqueue. Base consumes before both refusals. |
| V2.2 same | `C1593_S3_start_commit_binds_receipt` (2: resume, fresh) | Pause after acceptance commit before enqueue using an interceptor. One admission and exact receipt/StartedAt, including resumed same ID. Base has Admitted/no LaunchSessionId. |
| V2.3 same | `C1593_S3_lost_ack_is_not_a_second_admission` (1) | Throw after actual reservation commit, recreate service. Matching accepted receipt survives; second observation neither consumes nor increments generation. Drive existing interrupted terminal handling and rearm only after proven terminal. Base fails the receipt assertion. |
| V2.4 same | `C1593_S3_terminal_generation_rearms_once` (1) | First accepted receipt exists; terminal current generation rearms once, replacement starts once/count 2, replay cannot rearm the replacement. Base fails first receipt assertion; keep second-start assertions. |
| V2.5 same | `C1593_S3_supervisor_consume_failure_is_bounded` (3: 40P01, XX000, commit-IOException) | Fault the session/acceptance save or commit after selection. Transient: original grant/count 0, next tick accepts once. Deterministic/proven rollback: Exhausted reason, no grant/count 0, attention and no ungated restart on repeated ticks. Base leaves a spent Admitted wait. |
| V2.6 same | `C1593_S3_old_transcript_cannot_progress_restart` (1) | Accept resume with an existing transcript, replay old-generation/below-floor prompt and report then a new valid boundary. Old evidence leaves receipt unprogressed; new evidence advances. Base has no accepted receipt/floor. |
| V3.1 `S3CapacityRetainedReturnTests` | `C1593_S3_slot_filled_after_snapshot_does_not_spend` (1) | Local limit 1/count 0; hook inserts a counted Working task; real tick must keep retained flag, grant and count 0. Base spends then continues at :639. |
| V3.2 same | `C1593_S3_return_rollback_keeps_flag_and_grant` (1) | Interceptor fails retained flag save. Verify flag true/count 0/grant intact, no receipt; next call commits once. Base's own-scope redemption survives the fault. |
| V3.3 same | `C1593_S3_counted_return_receipts_existing_session` (1) | Successful real tick: false retained flag, one admission/receipt bound to same generation, no launch, old transcript cannot progress it; repeat tick is a no-op. Base has Admitted/no receipt. |
| V3.4 same | `C1593_S3_two_returns_share_one_slot` (1) | Two independently granted kinds, two ticks, one local slot; synchronize on counted-lock acquisition, not sleeps. Exactly one flag clears and one count increments; the loser still owns its unspent grant. Base's statement-scoped lock/spend-before-count breaks loser assertions. |
| V3.5 same | `C1593_S3_exhausted_return_cannot_bypass_gate` (1) | Current retained episode Exhausted, slot free. Tick must preserve retained flag and zero new execution authority. Base's unfinished lookup returns null and clears the flag. |
| V3.6 same | `C1593_S3_retained_consume_failure_is_bounded` (2: transient, deterministic) | Fault flag transaction; assert D-9 rollback or failure attention, no grant leak and existing process survives. Base spent admission is visible. |
| V3.7 same | `C1593_S3_new_claim_and_return_share_one_slot` (1) | Hold the counted advisory; start an ordinary local claim and retained return on independent contexts. Neither can pass it. After release exactly one enters the counted population; loser has no spend. Base ordinary claim skips that lock and retained return spends before acquiring it. |
| V4.1 `S3CapacityQueueTests` | `C1593_S3_pre_attempt_failure_does_not_spend` (2: spill-hold, attempt-save) | Live terminal-hold auto-selection, not synthetic key-only setup. Fail :2675 or attempt save; assert Pending, original attempts, count 0, grant held and SelectedMessageId null. Base consumes/stamps first. |
| V4.2 same | `C1593_S3_attempt_commit_binds_receipt` (1) | Observe durable state at first adapter write: Sent and count 1, accepted queue receipt matches head/generation/floor. Complete matching prompt then confirms. Base has Admitted. |
| V4.3 same | `C1593_S3_failure_retries_same_receipted_action` (2: transport, truncated) | First accepted attempt fails; retry through existing queue recovery without capacity-clock advance. Preserve identity/count 1; eventually deliver once or reach exact queue cap. Base returns Nothing until revalidation. Retain composer/late-confirm checks. |
| V4.4 same | `C1593_S3_crash_after_attempt_has_one_input` (2: before-input, after-prompt) | Recreate service across cut. Same accepted row/receipt/count; before-input follows safe existing recovery, after-prompt confirms from transcript without retyping. Base fails StartAccepted assertion; byte/body and submission counts remain required. |
| V4.5 same | `C1593_S3_revoked_selection_rebinds_at_consumer` (1) | Reconcile expiry with selected, unattempted message. No queue UPDATE by reconciler; consumer later rebinds and commits once using the new grant. Base eagerly rewrites the queue under provider. |
| V4.6 same | `C1593_S3_legacy_selection_needs_real_acceptance` (1) | Legacy Admitted/SelectedMessageId, zero attempts. Revalidation grants once; real flush creates matching S3 receipt without incrementing historical count. Base remains Admitted. |
| V4.7 same | `C1593_S3_queue_consume_failure_is_bounded` (2: transient, deterministic) | Fault attempt save; preserve original coordinates/count 0; transient retries, deterministic exhausts/clears with attention and no typing. Base spent count remains. |
| VL `S3CapacityLockOrderTests` | `C1593_S3_provider_wait_holds_consumer` (3: supervisor, retained, queue) | Second connection holds provider. Wait for path backend's advisory wait; NOWAIT lock of intended session/task/queue must get 55P03. Interceptor sees clean non-capacity tracker. Base takes provider before these consumer locks. |
| VL2 same | `C1593_S3_reconcile_does_not_write_locked_queue` (1) | Hold selected message FOR UPDATE, trigger grant expiry/new-action preparation. Reconcile must complete without requesting that row; pg_locks/command trace verifies no provider-to-queue wait. Base blocks writing queue while holding provider. |
| V6.1 `S3CapacityRemotePreparationTests` | `C1593_S3_prep_commit_releases_provider` (2: normal, crash-before-begin) | Commit HeldForRemotePrep; count 0, Deferred marker and no grant, a second wait is grantable immediately. Recreated dispatcher restarts prep without grant. Base retains the grant. |
| V6.2 same | `C1593_S3_prep_release_rolls_back_with_claim` (1) | First successful hold proves release (red at base); separate faulted hold commit proves metadata/defer/grant all roll back. No attempt or receipt in either branch. |
| V6.3 same | `C1593_S3_prepared_rejoins_and_launches_once` (1) | Deferred preparer runs past 120 s on fake clock; peers advance. Persisted completion -> Ready -> normal regrant -> exactly one S1 launch/receipt, mirror begin count 1. Base fails immediate release/fairness assertion. |
| VA `CapacityRecoveryAttentionTests` | `C1593_S3_consume_failure_attention_names_reason` (1) | Exhausted/count 0 with a consume-failure reason displays that failure rather than a fabricated max-attempt diagnosis. Base omits reason. |

New results: supervisor 10, retained 8, queue 11, locks 4, preparation 4, attention 1 = 38.
Strengthen red tests with rollback/second-attempt assertions even when the first receipt
assertion is the red-at-base witness; do not settle for asserting only a new field.

Lock tests use two independent DB connections, captured backend PIDs, `pg_stat_activity` and
`pg_locks`, and explicit barriers. Compute advisory key classid/objid on the same database.
Bound all waits; no wall-clock sleeps to manufacture races. Include opposite-direction
stop/session lock overlap in supervisor resume, concurrent terminal cancellation in retained
return, and new-wall/reconciler overlap in queue tests. New scoped guards carry post-land
method-scoped PCs: move consume ahead of flush, omit counted transaction, restore eager queue
rekey, skip generation/floor comparison, omit defer release. Each PC runs the exact owning
method, requires the intended assertion failure, is restored, and runs green. Mutation is a
separate commissioned stage, not a reason to broaden ordinary Code/Review runs.

## Performance budget

Apply [testing/build performance standards](../../testing-and-build.md#performance-standards)
and [the performance owner](../../test-performance-standards.md). No build/test is needed
for this documentation-only Plan. The implementation budget below is **provisional**, not
a claim of measured S3 p95.

Available measurement provenance: the S1 plan on `feat/card-task-d94003bc` reports Linux
capacity methods 1.2-11.4 s, first-host shared Postgres/clone about 10-11 s, compile 217-399 s
and slot wait 220-520 s. It does not supply the per-result sample, host load or a measured p95;
its 12 s figure is an estimate. Review `feat/card-task-3b5e0eb8:.antiphon/task-3b5e0eb8.md`
at reviewed source `0eda11bfbded7b5f764ff475fcaaa01ddbc71d2b` records 975 results/24.5 min
for its broad B batch and database-client exhaustion in another batch. That is neither an
S3 p95 nor permission to repeat those broad batches. Current exact five-class counts were
checked from source at this baseline: 26 + 16 + 15 + 21 + 97 = 175.

Code collects per-result p50/p95/max and per-row wall time during the required red/green
runs, not a separate benchmark pass. Record source SHA, Linux host identity, load, sample
size, arguments, category and phase membership under ignored
`.antiphon/c1593-s3/<source>/<run>/<cp>/` (TRX, timing JSONL, slot/build logs and digests).
Unknown phase spans remain unknown; remainder is explicit. CARD-1404/CARD-1514 own missing
measured budgets/instrumentation; CARD-1531 owns admission-vs-execution timing. Before final
Review, replace provisional row budgets with about 2x the measured comparable p95, or retain
the exact exception and sample gap in the report. Never call an estimate a measurement.

- Shared setup: existing `TestDbFixture` Postgres/template initialization once per test host;
  per-test database clones, no new container/migrations per test or class. One harness/provider
  per scenario; recreation is intentional only for crash/restart witnesses and is timed.
  No AppHost or native test host. Sequential rows, maximum four tests in a host and existing
  assembly-local process limiters for git/fake-process helpers; no Pty assembly co-scheduling.
- Phase estimates per host: shared DB/template readiness 11 s, host discovery/start 5 s;
  per test clone/seed 1-3 s, act/assert 1-8 s, teardown 1-2 s. These are planning allowances
  based on S1, not additive measured spans. Record DB, filesystem/git, fake transport,
  barrier/lock wait and teardown separately. New test expected p95 is provisionally 15 s
  for ordinary cases and 30 s for crash/two-generation cases; set watchdogs 30/60 s respectively.
  Lock/barrier sub-deadline is 15 s, outer lock-witness watchdog 30 s. Fake-clock advances
  cover 120 s expiry and backoff without waiting in real time. Owned external calls inherit
  cancellation, with DB/CLI/fake-transport deadlines bounded by the remaining test watchdog.
- Build separately: one isolated `Antiphon.Tests` build per committed source/slice group,
  4-7 min expected, 14 min initial execution ceiling (2x observed 399 s, rounded). Admission
  wait: explicitly 20 min maximum, recorded separately; exit 4 means not run, never bypass.
  Long slot contention is not test execution. Remove only owned `bin-c1593s3-*` outputs at end.
- CP table estimates total 26 min of execution for a complete final selection (including
  host setup allowances), excluding build/slot wait. This is an unmeasured planning total;
  run once, then only affected rows after a fix. No automatic repeat to improve a percentile.
  Expected or observed selection/batch/run above 30 min requires caller-commissioned Debug
  with timing evidence before further broad execution; splitting for bookkeeping does not
  waive that obligation. Do not enlarge timeouts or loosen assertions to turn red green.
- Dependency selection: changed server admission and queue services are the server/messaging
  categories inside `Antiphon.Tests`; the queue/receipt classes cover the messaging-facing
  dependent behavior, supervisor classes cover standing launch, dispatch guards cover git/
  workspace admission. The test project references Server, SessionRunner and Contracts,
  Resilience, Observability through Server, Messaging projects, test helpers and tools.
  Record the evaluated direct/transitive ProjectReference graph in the run manifest.
  This is scoped ordinary verification, not Full rc or native runner qualification.

### Checkpoints

Lane for **every row**: portable server/database integration on Linux; no native Windows
tests. Live placement was read via both required endpoints on 2026-10-28: runner-defaults
revision 2 selects the available Linux default; the catalogue also lists an available
Windows runner and a drained unavailable temporary Linux runner. These are observations,
not a hard-coded fleet location. Omit `-Runner`; omit `-Platform` for these portable checks
(`-Platform Any` only if unpinning an inherited card restriction). Re-read placement at dispatch.

The brief explicitly authorizes one build-slot build and direct DLL executions. That is the
execution profile for this closed list, superseding the generic checkpoint-tool driver for
this task. One Build ID means one isolated build per source revision; other rows reuse it.
Never rebuild per row. `dotnet exec` already performs no build: `--no-build` is a `dotnet run`
option, not an argument to pass after a test DLL. Use an actual built-DLL path as below.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-c1593s3-final/` | lifecycle | `/*/Antiphon.Tests.Application/CapacityAdmissionLifecycleTests/*` | S1/remote cuts/fault bound | 26 passed, 0 failed/skipped | 26 | 3 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S3 | CP-1 | lock-regression | `/*/Antiphon.Tests.Application/CapacityLockOrderTests/*` | S1 ordering/stop overlap | 16 passed, 0 failed/skipped | 16 | 2 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S3 | CP-1 | wall-reroute | `/*/Antiphon.Tests.Application/WallRerouteDispatchTests/*` | supersede and receipt rollback | 15 passed, 0 failed/skipped | 15 | 1.5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-4 | S3 | CP-1 | receipt-delivery | `/*/Antiphon.Tests.Application/ReceiptFailureDeliveryTests/*` | complete input/caller obligation | 21 passed, 0 failed/skipped | 21 | 3 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-5 | S3 | CP-1 | dispatch-guards | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` | all ordinary task admission guards | 97 passed, 0 failed/skipped | 97 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-6 | S3 | CP-1 | new-supervisor | `/*/Antiphon.Tests.Application/S3CapacitySupervisorTests/*` | V2.1-V2.6 | 10 passed, 0 failed/skipped | 10 | 2 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-7 | S3 | CP-1 | new-retained | `/*/Antiphon.Tests.Application/S3CapacityRetainedReturnTests/*` | V3.1-V3.7 | 8 passed, 0 failed/skipped | 8 | 1 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-8 | S3 | CP-1 | new-queue | `/*/Antiphon.Tests.Application/S3CapacityQueueTests/*` | V4.1-V4.7 | 11 passed, 0 failed/skipped | 11 | 2 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-9 | S3 | CP-1 | new-locks | `/*/Antiphon.Tests.Application/S3CapacityLockOrderTests/*` | VL/VL2, Rule P and X | 4 passed, 0 failed/skipped | 4 | 1 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-10 | S3 | CP-1 | standing-regression | `/*/Antiphon.Tests.Application/CapacityRecoverySupervisionTests/*` | D8/V16-V18 strengthened | 6 passed, 0 failed/skipped | 6 | 1 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-11 | S3 | CP-1 | retained-regression | `/*/Antiphon.Tests.Application/CapacityRecoveryTaskTests/*` | counted population/priority | 6 passed, 0 failed/skipped | 6 | 0.5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-12 | S3 | CP-1 | queue-rearm-regression | `/*/Antiphon.Tests.Application/(CapacityRecoveryQueueTests*)\|(CapacityRecoveryGrantLivenessTests*)/*` | queue 8, liveness 12 | 20 passed, 0 failed/skipped | 20 | 1 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-13 | S3 | CP-1 | fresh-resume-regression | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/(C510_S2_capacity_grant_is_required_and_follows_fresh*)\|(C510_S2_cleared_model_hold_grant_starts_resume_on_the_next_tick*)\|(C510_S2_fresh_model_disabled_keeps_capacity_wait_and_bounded_retry*)` | fresh 2, resume 1, refusal 1 | 4 passed, 0 failed/skipped | 4 | 0.5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-14 | S3 | CP-1 | exhausted-attention | `/*/Antiphon.Tests.Application/CapacityRecoveryAttentionTests/*` | legacy 3 + VA 1 | 4 passed, 0 failed/skipped | 4 | 0.5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-15 | S3 | CP-1 | new-preparation | `/*/Antiphon.Tests.Application/S3CapacityRemotePreparationTests/*` | V6.1-V6.3 | 4 passed, 0 failed/skipped | 4 | 1 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

Total final roster: 252 results (175 required existing + 38 new + 39 additional existing).
`After=S3` names the combined committed S3a-S3d implementation group. All rows share that
group and one final build, satisfying the importer's same-After build-reuse rule. Run the
complete green roster once after all slices; no intermediate broad green rounds. Each row
has one exact filter and one build identity. The separate red-before-product witness profile
below retains its original source SHA and uses one isolated build per red source group.

Red-before-product selection is closed too: for each new method in the witness table, run
`/*/Antiphon.Tests.Application/<Class>/<ExactMethod>` (all stated arguments), sharing the
slice's one red-source build. VA is the one exact new attention method. Do not run the five
full regression classes against knowingly red product source. Red and green evidence have
separate directories. For an unexpected regression, confirm only the failing exact method
at the baseline before assigning inherited/introduced status.

Example Linux commands (each driver separately leased; source must already be committed):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1593s3-build -SlotWaitMinutes 20 -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-c1593s3-final/ --property:UseAppHost=false --no-self-contained
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1593s3-cp1 -SlotWaitMinutes 20 -- dotnet exec tests/Antiphon.Tests/bin-c1593s3-final/Antiphon.Tests.dll --treenode-filter '/*/Antiphon.Tests.Application/CapacityAdmissionLifecycleTests/*' --maximum-parallel-tests 4 --report-trx --report-trx-filename cp1.trx --results-directory .antiphon/c1593-s3/final/CP-1
```

The actual isolated DLL location is verified from the build output before execution; do
not fall back to a shared bin. Record per-row source/build SHA, DLL digest, exact filter,
test-name roster, passed/failed/skipped counts, TRX path/digest, slot wait and phase durations.
Use an execution watchdog around each row at twice its table estimate initially, replaced
by measured budgets as above. Keep all generated evidence ignored. The baseline checkpoint
reader still caps files at 64 MiB (`scripts/lib/checkpoint-file-reader.ps1:196`); manual
CHECKPOINT-shaped evidence is not a sealed receipt and must not be called `reviewedSourceClean:true`.
Any unlisted build/test driver requires a stated failure/scope reason; no whole Unit, whole
assembly, native runner tests or hidden build through `dotnet run`.

Source roster audit at this baseline: each named class prefix resolves to exactly one class
(partial declarations are the same class); CP-13's three method prefixes each match one
method, with argument counts 2/1/1. Proposed `S3Capacity*Tests` classes do not yet exist and
do not collide with existing classes. Code repeats the suffix audit after adding them and
checks the executed names in fresh TRX. OR operands all have parentheses and trailing `*`;
Markdown table pipes are escaped. A listing or exit zero alone does not establish coverage.

## Review and landing gates

Review the atomic tuples, class-3 lock rendezvous, pre-provider flushes, queue retry authority,
legacy handling, terminal-generation rearm and preparation-ready liveness. In particular,
reject a design implementation that calls the old own-scope consume under consumer locks,
claims Sent is delivered, or leaves eager queue writes under the provider.

Before the source handoff, Code runs `scripts/check-evidence-diff.ps1` over its recorded
base..pushed tip and reports exact SHAs. Fetch master and the caller's rc target and run
`git merge-tree --write-tree <target> HEAD` for both; report conflicts, never merge/rebase the
task branch to resolve them. The independent Review judges executable changes and complete
required verification separately from advisory report corrections under orchestration D-2.
No S3 result qualifies or modifies the live native run. Review of this document is the next
stage; Code follows only the approved plan and any review amendments.
