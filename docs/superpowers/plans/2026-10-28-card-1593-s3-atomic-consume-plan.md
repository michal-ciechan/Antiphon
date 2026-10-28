# CARD-1593 S3: atomic consume for supervisor start, retained return and queue flush

Task 62fb542c, Plan stage. Base `origin/rc/2026-10-27` = `aa6dfd24a51db7c02e8722a05a21817bf4df407a`.
All line numbers are at that base unless marked otherwise. No product code changed in this task.

**Amendment 1 (task f264f937, 2026-10-28) after design review 0b34dc40.** It fixes the five
blocking defects and records the advisories. See `## Amendment log` at the end. Changed sections:
- Ground truth: row 8 rewritten, new rows 9-11.
- D-S3-1: an own-scope helper.
- D-S3-3: a retained task with no wait, or with the feature off.
- D-S3-4: an Enter-only consume.
- D-S3-5: a full supervisor exit table and an S-4 reread under `FOR UPDATE`.
- D-S3-6: default 0.
- D-S3-7: rows L16', L17' and L17e.
- D-S3-8: master first, in three Code rounds.
- Slices, verification, performance budget, checkpoints, landing and risks.

**Amendment 2 (task e92ad0f5, 2026-10-28) after design review 8f6e5b17.** It fixes the three
Round A blocking defects (D-1 to D-3), records the Round B decision for D-4, takes advisories
A-1 to A-10 and corrects the line drift. See `## Amendment log (review 8f6e5b17)`. Changed
sections:
- Ground truth: rows 3 and 4 corrected, new rows 13-16.
- D-S3-2: the pre-check classification, the card-arm placement and throw rule, the boundary plumbing.
- D-S3-4: the Enter-only bound.
- D-S3-5: catch lines, the exact outer S-2 list, release call sites, the S-0c row, S-2h in the
  tracker rule, S-T and S-3 wording.
- D-S3-7: L16', L16c, L17' and the S1 L18 note.
- New D-S3-10 (Round B decision on the reconciler re-key).
- Verification (V-S3-4, V-S3-5b, V-S3-5c, V-S3-11), budget, checkpoints, risks.

Line numbers were re-read for this amendment at `aa6dfd24a`; `git diff --stat origin/master` over
`server/` is empty, so they hold at master `fe73cfd2e` too.

Every file this plan touches is byte-identical at `origin/master` `fe73cfd2e` and at the rc base.
That covers the five services, `SupervisionSettings.cs`, `CardService.cs`,
`SessionReconciliationService.cs` and `tests/Antiphon.Tests/Application/` (`git diff --stat` is
empty). The line numbers therefore hold on master as well.

Inputs:
- Investigation bcb5bc45, `docs/investigations/2026-10-28-card-1593-capacity-leaks.md` on
  `origin/feat/card-task-bcb5bc45` (read at `4563e525`; its line numbers match this base for the
  files below except where re-read here).
- S1 plan `docs/superpowers/plans/2026-10-27-card-1593-capacity-admission-lock-order-plan.md` on
  `origin/feat/card-task-d94003bc`. This plan reuses its rank table (D-1), Rule P, Rule X, Rule F,
  the D-2 reserve/consume/release lifecycle, the D-4 rollback model, the D-9 failure classes and
  D-12 (expiry is reconciler-only). It refers to them as S1 D-n.

In scope: leak 2 (supervisor), leak 3 (retained return), leak 4 (queue flush), leak 6
(`HeldForRemotePrep` grant expiry). Out of scope: leak 1 (commit-fault bound, shipped) and leak 5
(quota-reply lock inversion, its own plan).

## Problem

S1 moved the dispatch claim's consume into the claim transaction. Three other consumers still
call the own-scope wrapper `CapacityRecoveryService.RedeemAsync`
(`server/Application/Services/CapacityRecoveryService.cs:240-304`), which increments
`AdmissionCount` (`:286`), writes `Admitted` (`:290`), clears the grant (`:298`) and **commits**
(`:303`) before the caller does the work it paid for:

| Leak | Caller | Spend | Work that can then not happen |
|---|---|---|---|
| 2 | `AgentSupervisorService.TryHandleCapacityWaitAsync` `:507-508` | before the start | duplicate-key exit `:512-516`, compaction block `:523-524`, `StartSupervisedRestartAsync` false `:525-526`/`:571`, start throws (`:240-245`) |
| 3 | `AgentTaskDispatcher` retained return `:633-635` -> `:1779-1782` | before the counted slot | `TryClaimCountedSlotAsync` false `:636-639` (stale snapshot `:510`, `AfterQueuedSnapshotAsync` `:537`, overlapping ticks) |
| 4 | `SessionMessageQueueService` flush `:2577-2590` | before composing and before the `Sent` stamp `:2725-2749` | every `return FlushResult.Nothing/Failed` between `:2590` and the `Sent` save |

Leak 6 is not a spend. The claim commits `HeldForRemotePrep` with the task `Queued`
(`AgentTaskDispatcher.cs:6017-6026`) and the grant still reserved; the reconciler revokes it at
`GrantExpired` (120 s from `GrantedAt`, `CapacityRecoveryService.cs:1176-1178`) while the
preparation is still in flight, and grants the provider to another waiter (S1 D-10).

## Ground truth (card/brief assumption vs code at `aa6dfd24a`)

| Assumption | What the code does |
|---|---|
| `RedeemOnAsync(consume:true)` can be reused as-is by the three callers | **No.** It refuses any wait not `ActionPending` (`CapacityRecoveryService.cs:343-344`), so a receipt-backed revalidation grant (`Admitted && NeedsRevalidationGrant`, `CapacityRecoveryPolicy.cs:28-31`) can never be consumed in a transaction. It also requires `launchSessionId` (`:327-328`) and always writes `StartAccepted` + `LaunchSessionId` + `DispatchAttemptId` (`:376-381`). |
| The supervisor consume can write the S1 launch receipt (`StartAccepted`) | **No.** `StartAccepted` is never re-armed: `RearmStalledAdmissionsAsync` selects only `Admitted` (`:1328-1331`). A standing start that launches and then dies before a report boundary would leave the wait unfinished forever, and the outer check `AgentSupervisorService.cs:209-212` would then block the ungated restart forever. `CapacityRecoverySupervisionTests.Card0412_D8_stalled_standing_start_rearms_and_actually_starts_again` (`:22-100`) pins the opposite: `HasExecutionReceipt == false` after the start, re-arm, a second launch, `AdmissionCount == 2`. |
| The supervisor start has one commit to join | **Partly.** The interactive path `AgentControlService.StartInteractiveSessionAsync` has one reservation transaction (`AgentControlService.cs:543`) with two commit points: resume arm `AcceptAsync` `:686` -> `CommitAsync` `:687`, new-row arm `AcceptAsync` `:796` -> `CommitAsync` `:797`. `AcceptAsync` (`:802-888`) ends in `SaveChangesAsync` `:887`. The card arm (`_cardService.SpawnAsync`, `:302-307`) commits in `CardService`, not here. Early returns that start nothing: launch-owned `:198-199`, already live `:225-230`, reservation-live `:557-561`. |
| The supervised-fresh reservation already checks the capacity wait | **Yes, against the spent state.** `:602-615` require `wait.State == Admitted && OutcomeReason == Start`, and `AcceptAsync` moves the wait to the new session with an `ExecuteUpdate` filtered on `State == Admitted` (`:808-816`). Both encode "redeemed before start" and must change with the consume point. |
| Progress for a standing wait is keyed on the receipt | **No.** `OnTranscriptAsync` keys on `wait.SessionId` (`CapacityRecoveryService.cs:883-889`); `SessionId` is not in `HasExecutionReceipt` (`CapacityRecoveryPolicy.cs:92-96`). Moving `SessionId` does not change re-arm behaviour. |
| Retained return can share the dispatcher context's transaction | **Risky.** The retained loop runs on the tick's `_db` (`AgentTaskDispatcher.cs:602-652`), which tracks every queued row of the tick; a transaction there would flush unrelated tracked edits under the provider lock (Rule P). `TryClaimCountedSlotAsync` (`CapacityRecoveryService.cs:845-863`) takes `pg_advisory_xact_lock` with no explicit transaction, so the lock ends with the statement; its only production caller is `:637`. |
| The queue consume can go at "delivered" | **No (see D-S3-4).** Delivery confirmation (`DeliverAsync`, `SessionMessageQueueService.cs:2760-2763`) can outlast the 120 s grant; a revoke mid-delivery re-keys the message (`PrepareNextAttemptOnAsync` `:1401-1407`) and the consume would refuse after the provider was used. The queue already charges its own attempt at the `Sent` stamp and "deliberately" never refunds it (`:2712-2714`). |
| A pre-`Sent` flush exit is rare | **No.** Between the redeem (`:2586`) and the `Sent` save, these exits return **without** delivery after the spend: quota-suppressed recheck (`:2707-2710`), expired briefs (`:2717`), spill-hold/restaging (`:2644-2680`), pointer defer (`RecoverPointerAsync` -> `DeferPointersAsync`, `SessionMessageQueueService.CodexPointers.cs:146-147`) and an unavailable redelivery snapshot (`:2690-2697`). Two exits **do deliver**. Enter-only confirm (`:2700-2701`) and pointer recovery (`:2684` -> `CodexPointers.cs:143-144`) both reach `EnterOnlyConfirmLockedAsync` (`:3380`), which presses Enter (`:3426`) on a body that was typed earlier and so submits a provider turn without a `Sent` stamp (amendment D-2). |
| Every Enter-only caller sits behind the flush gate | **No.** `EnterOnlyConfirmLockedAsync` has three callers. Two come from `DeliverNextLockedAsync` on a `Pending` head: `:2701`, and `:2684` via pointer recovery. One comes from interrupted-`Sent` recovery (`:3358`, and `:3345` via pointer recovery), on rows that are already `Sent`. `RecoverPointerAsync` is also reached from the human retry at `:1508`. After S3 a `Sent` row has already paid, because its stamp committed the consume. |
| A retained task always returns through a consume | **No.** `TryRedeemCapacityWaitAsync` returns true with **no** consume when there is no unfinished wait (`AgentTaskDispatcher.cs:1756-1757`). That covers `Exhausted`, `Canceled` and `Superseded` waits. The tick skips the redeem entirely when capacity recovery is disabled (`:631-635`). Retained tasks are not orphan-swept. |
| A standing start whose commit acknowledgement is lost is recovered by interrupted-launch recovery | **Only as a backstop.** The enqueue sits after `CommitAsync` (`AgentControlService.cs:687-688`, `:797-798`). Pass 1c (`SessionReconciliationService.cs:289-343`) resumes only a `Starting` row that the runner **already serves**. A row that was never enqueued is ended by pass 1 after `StartingGraceMs` (`:202-213`). The supervisor then sees a dead agent, and the `Admitted` wait (no receipt) re-arms. That costs one attempt and stays bounded by `MaxAttempts`. |
| `HeldForRemotePrep` can be observed later | **Yes.** `RemoteHoldForAsync` (`AgentTaskDispatcher.cs:1238-1250`) returns a hold while `_remotePrep.IsInFlight(task.Id, out since)` (`:1245`), before the gate (`:949-959`) and with no transaction open. `since` is the preparation start. |
| Grant renewal exists | **No.** `GrantedAt` is written only by `GrantReadyAsync` (`:212-215`) and cleared by `ClearGrant` (`:1224-1230`). |
| The fresh pre-check can simply become a grant check (amendment 2) | **Not as written.** `AgentControlService.cs:606-613` also requires `intent.CapacityRecoveryActionKey == wait.ActionKey` (`:610`), which holds only because the supervisor stamps the intent first (`AgentSupervisorService.cs:521`). S3 deletes that stamp, so `:610` must go. The pre-check's only code is `standing_automatic_fresh_capacity_required`, which S-2 releases, so a grant race there would cancel the wait (review D-1). |
| `CardService.SpawnAsync` leaves the agent consistent (amendment 2) | **No.** It does not set the agent pointer. `StartCoreAsync` saves `PersistentSessionId`/`Status` at `AgentControlService.cs:320-327`, after the spawn returns. The card arm is reachable only from the non-reserve start: the reserve arm refuses card work at `:281-282` and `:588-589` (review D-3). |
| The reconciler writes no queue row under the provider lock (amendment 2) | **No.** Grant expiry (`GrantReadyAsync` `:141` -> `:157` `RecordRevokedGrantAsync` -> `:1188` `RearmRevokedGrantOnAsync` -> `:1215`), stall re-arm (`:1342` -> `:1359`) and new-wall (`BumpWaveAsync` `:808` -> `:827`) all reach `PrepareNextAttemptOnAsync`, which loads and re-keys `SessionQueuedMessages` rows (`:1401-1407`). They are written at the provider transaction's save, under rank 7. The expiry path does not consult `SelectedMessageId` (review D-4). |
| `SessionQueuedMessages.CapacityRecoveryActionKey` may repeat (amendment 2) | **No.** It has a filtered unique index (`AppDbContext.cs:1785-1788`). The key format is `{waitId:N}:{ordinal}` (`CapacityRecoveryPolicy.cs:16-17`), and enqueue and both stamps set the key and `CapacityWaitId` together (`SessionMessageQueueService.cs:799-800`, `:5569-5571`, `CapacityRecoveryService.cs:980-982`). |
| The reconcile pass and the supervisor pass are independent (amendment 2) | **No.** `AgentSupervisorHostedService.cs:146` (`ReconcileAsync`) and `:148` (`supervisor.TickAsync`) share one `try`, so a reconcile exception skips that tick's supervisor pass and the later sweeps. |

## Decisions

**D-S3-1 One consume primitive, two receipt modes.** `RedeemOnAsync` gains a `CapacityConsumeReceipt receipt`
parameter (default `Launch`, S1 behaviour, unchanged for the claim):
- `Launch`: S1 D-2 (`StartAccepted`, `SessionId = LaunchSessionId`, `DispatchAttemptId`; requires the session).
- `Admitted`: exactly what `RedeemAsync` writes today (`:283-298`): `AdmissionCount++` unless
  revalidation, `NeedsRevalidationGrant = false`, `State = Admitted`, `OutcomeReason = path`,
  provider clock and grant cleared. No receipt field is written, so `HasExecutionReceipt` is
  unchanged from base for that wait (the queue's `SelectedMessageId` is already set by the stamp).
  Optional `stampSessionId`/`stampSessionStartedAt` set `wait.SessionId`/`SessionStartedAt`
  (progress key, not a receipt) for the fresh supervisor arm, replacing the `ExecuteUpdate` at
  `AgentControlService.cs:806-816`.

Both modes also accept a revalidation grant: the state check `:343` becomes
`ActionPending || (Admitted && NeedsRevalidationGrant)`. Revalidation never increments (as `RedeemAsync` `:270-287`).
Refusals write nothing in consume mode (S1 D-9 class 1); `RedeemGateAsync` (`consume:false`)
still records durable refusals on its own scope.
- *Why `Admitted` and not `Launch` for S3:* the three S3 consumers have no task lifecycle that
  ends the wait (Ground truth row 2). `Admitted` keeps the base re-arm and `MaxAttempts` bound
  (D8 stays green unchanged). The leak is the commit point, not the state written.
- *Rejected:* a separate `ConsumeAdmittedOnAsync` copy. Two copies of the grant check under the
  provider lock is how `RedeemAsync` and `RedeemOnAsync` already drifted (`not-action-pending`).
- *Rejected:* `StartAccepted` + extending `RearmStalledAdmissionsAsync` to `StartAccepted`. It
  would also re-arm S1 dispatch receipts, which S1 D-3 I3 relies on never happening.

*Own-scope helper (amendment).* `ConsumeAdmittedAsync(Guid waitId, string actionKey, AgentKind kind, CapacityRedemptionPath path, CancellationToken ct)`
opens its own scope and transaction and takes rank 7 then rank 8. It calls
`RedeemOnAsync(consume: true, receipt: Admitted)` and commits on `Ok`; it rolls back on a refusal
and returns the refusal. It is the single shape for every consume that has no row transaction to
join:
- the card arm (D-S3-2),
- `ChargeFailedStartAsync` (S-3),
- the Enter-only consume (D-S3-4).

It differs from `RedeemAsync` only in the D-S3-1 state check, and in that a refusal writes
nothing (S1 D-9 class 1). *Rejected:* keeping `RedeemAsync` for these sites. It records
`Exhausted`/`Deferred` rows on refusal (`:258-280`), and the S1 consume-mode contract forbids
that.

After S3 `RedeemAsync` has no production caller. It stays (service-level tests in
`CapacityRecoveryAcceptanceTests` call it 27 times) with an XML note "test and compatibility
only". The S3e source-scan guard (V-S3-12) checks that `server/` has no `.RedeemAsync(` call
site. It can go green only on a branch that carries S3b, S3c and S3d (review A-7), so it rides
the last of them (D-S3-8).

**D-S3-2 Supervisor (leak 2): consume inside the start reservation.**
- *Consume point:* `AgentControlService.StartInteractiveSessionAsync`, between `await AcceptAsync(...)`
  and `reservation.CommitAsync` on both arms (`:686/:687`, `:796/:797`):
  `RedeemOnAsync(_db, wait.Id, wait.ActionKey, wait.ExecutionKind, Start, ct, consume: true, receipt: Admitted, stampSessionId: accepted.Id, stampSessionStartedAt: accepted.StartedAt)`.
  `AcceptAsync`'s save (`:887`) leaves the tracker clean, so Rule P's assertion
  (`RejectDirtyLaunchRows`) holds. The session row (`:709-741` or the resume edits `:652-683`),
  `agent.PersistentSessionId`, `intent.AutomaticFreshConsumed`, the session and intent
  `CapacityRecoveryActionKey` and the spend commit together or not at all.
- *Plumbing:* a new internal record `CapacityStartAdmission(Guid WaitId, string ActionKey, AgentKind Kind)`.
  The supervisor calls new `internal AgentControlService.StartForCapacityAsync(agentId, request, admission, ct)`
  (public `StartAsync` signature unchanged) and passes it in `SupervisedFreshReservation` for the
  fresh arm. `StartInteractiveSessionAsync` gets it as a parameter.
  `StartSupervisedRestartAsync`'s `string? capacityActionKey` parameter becomes
  `CapacityStartAdmission? admission` (the ungated path passes `null`, as it passes `null` today).
- *Test boundaries (amendment 2).* `AgentControlService` has no `LandDeliveryBoundary` today. It
  gains an optional constructor parameter `LandDeliveryBoundary? landBoundary = null`, the same
  shape as `CapacityRecoveryService` (`CapacityRecoveryService.cs:26`, `:33`). Production DI
  leaves it null. The points are `capacity-start-before-precheck` (immediately before
  `AgentControlService.cs:602`), `capacity-start-before-consume` (between `AcceptAsync` and the
  consume on both arms) and, in the supervisor, `capacity-supervisor-before-start`.
- *Admission for the outer catches (amendment 2).* `SuperviseAsync` declares
  `CapacityStartAdmission? admission = null` before the `try` at `AgentSupervisorService.cs:228`,
  and `TryHandleCapacityWaitAsync` sets it (through a `StrongBox<CapacityStartAdmission?>`
  argument) right after the grant-match read (`:500-505`) succeeds. The outer S-1/S-2/S-2h/S-3
  catches use it for the release and the charge. When it is still null, the throw came before
  any grant was matched, and the catches behave exactly as base (no release, no charge).
- *Key stamps move into the reservation:* the supervisor's `session.CapacityRecoveryActionKey = ...`
  and `state.CapacityRecoveryActionKey = ...` plus the save (`AgentSupervisorService.cs:512-522`)
  are deleted. The new-row arm already sets the session key (`AgentControlService.cs:728`, extend
  to `admission?.ActionKey`); the resume arm sets `previous.CapacityRecoveryActionKey`; `AcceptAsync`
  sets `intent.CapacityRecoveryActionKey`.
- *Pre-check (amendment 2, review 8f6e5b17 D-1).* The fresh arm's check at
  `AgentControlService.cs:602-615` is rewritten. It stays read-only: an AsNoTracking read of the
  wait (`:602-605`, unchanged) and of the provider row, and no rank 7. Each mismatch has one of
  two classes:

  | Clause | Code | Class |
  |---|---|---|
  | no unfinished wait while `supervisedFresh.CapacityActionKey` is set | `standing_automatic_fresh_capacity_required` | S-2 (durable; the release finds no wait and is a no-op) |
  | an unfinished wait while `supervisedFresh.CapacityActionKey` is null | `standing_automatic_fresh_capacity_required` | not on the capacity branch: only the ungated path calls with a null key, and its own catch handles it as base |
  | `wait.ExecutionKind != spec.Kind` | `standing_automatic_fresh_capacity_required` | S-2 (the composed launch kind is not the waiting provider; durable) |
  | `wait.ActionKey != supervisedFresh.CapacityActionKey` | `capacity_consume_refused` | S-1 |
  | `!(IsGrantCandidate(wait) \|\| wait.State == ActionPending)` (the supervisor's own test, `AgentSupervisorService.cs:497`) | `capacity_consume_refused` | S-1 |
  | provider row missing, or `GrantedWaitId != wait.Id`, or `GrantedActionKey != wait.ActionKey` | `capacity_consume_refused` | S-1 |

  - `:610` (`intent.CapacityRecoveryActionKey != wait.ActionKey`) is **deleted**. The supervisor
    no longer stamps the intent before the start (`AgentSupervisorService.cs:521` is deleted),
    and `AcceptAsync` stamps it after the pre-check. Keeping the clause would refuse every
    capacity fresh start.
  - `:611-612` (`State != Admitted`, `OutcomeReason != Start`) are **deleted** and replaced by
    the two grant clauses above. They encoded "redeemed before start".
  - *Why S-1 for a grant race:* a grant revoked or moved between the supervisor's grant-match
    read (`AgentSupervisorService.cs:500-505`) and this check is the same race that the consume
    catches one step later. Both are owned by the reconciler: no release, no cancel, no backoff,
    no charge. Classing it S-2 would cancel the wait and send the next tick down the ungated
    path, which drops pacing through a race exit.
  - *Where this differs from the review's fix:* the review kept `ActionKey !=
    supervisedFresh.CapacityActionKey` as durable (S-2). It is a race too. A wait's `ActionKey`
    changes only in `PrepareNextAttemptOnAsync` (`CapacityRecoveryService.cs:1385-1387`), that
    is, in a reconciler re-arm after a grant expiry, a stall or a new wall. A different unfinished
    wait returned by `:602` also shows up as a key mismatch. In both cases the reconciler owns the
    wait, so the class is S-1.
  - The non-reserve arm (`_control.StartAsync`, `AgentSupervisorService.cs:535`) has no
    pre-check. Its consume is the check, and a refusal there is S-1 as well.
  - The authoritative check stays the consume under the provider lock. The pre-check exists so
    that a moved grant fails before composition work and the queue-lock wait.
  - Witness: V-S3-4 `precheck-revoked`.
- *Supervisor order:* `TryHandleCapacityWaitAsync` keeps the grant-match read (`:500-505`), then
  runs the duplicate-key check (`:512-516`, read-only now) and `BlocksAutomaticRestartAsync`
  (`:523`) **before** calling the start. No `RedeemAsync`.
- *Card arm (amendment 2, review D-3).*
  - *Reach.* Only the non-reserve arm reaches it. The reserve arm refuses card work at
    `AgentControlService.cs:281-282` and again inside the reservation at `:588-589`
    (`standing_resume_card_work_pending`).
  - *What the spawn does and does not save.* `CardService.SpawnAsync` (`CardService.cs:1601`,
    called at `AgentControlService.cs:302-307`) commits its own launch. It does **not** set the
    agent pointer. `StartCoreAsync` sets `PersistentSessionId`, `Status` and `UpdatedAt` and
    saves them at `:320-327`.
  - *Placement.* The consume runs **after the `:326` save and before the event publish at
    `:328`**, only when the start carries a `CapacityStartAdmission`. The call is
    `ConsumeAdmittedAsync(admission.WaitId, admission.ActionKey, admission.Kind, Start, ct, stampSessionId: sessionId, stampSessionStartedAt: <the spawned row's StartedAt, AsNoTracking>)`.
    `ConsumeAdmittedAsync` gains the two optional stamp parameters and passes them to
    `RedeemOnAsync`.
  - *Throw rule.* The call is wrapped in `catch (Exception ex) when (!ct.IsCancellationRequested)`.
    That covers a refusal, a transient (`40P01`, `40001`, `55P03`), a connection loss and any
    other exception from the consume's own 7->8 scope. On any of them the consume rolls back,
    the method logs at Warning with the wait id, action key and session id, and **the launch
    stands**. `StartCoreAsync` continues to `:328` and returns normally, so the supervisor sees
    `true`. This is class **S-0c** (D-S3-5): no backoff, no charge, and the wait as it was. A
    cancellation propagates after the pointer save. It is S-C, and the launch stands too.
  - *Why after `:326`.* If the consume sat before the save and threw:
    1. the launched card session would keep a stale pointer;
    2. the exception would reach S-3, and `RecordStartFailureAsync` could not see the launch as
       owned at `AgentSupervisorService.cs:384`, so it would record a backoff;
    3. the S-3 charge would consume;
    4. after the re-arm, the supervisor would call `ResolveStartCardAsync`/`SpawnAsync` again
       for an agent whose card already has a running session.
  - *Direction.* It under-spends: at most one unpaced launch per failure (Risk 3). Because
    `SessionId` is not stamped on a failed consume, transcript progress on the card session does
    not progress the wait. The wait therefore cycles at D-12 like Risk 9 until that session dies,
    and the next capacity start then consumes normally.
  - Witness: V-S3-5c (`spends-after-spawn`, `consume-throws`).
  *Rejected:* threading the consume into `CardService.SpawnAsync` (another service's transaction,
  a larger blast radius than this slice). *Rejected:* refusing a capacity start with card work
  (base starts it; a behaviour change outside the leak).

**D-S3-3 Retained return (leak 3): one transaction for slot, task flag and consume.** New
`CapacityRecoveryService.ReturnRetainedAsync(Guid taskId, (Guid Id, string Key, AgentKind Kind)? wait, int? localLimit, CancellationToken ct)`
on its own scope:
1. `BEGIN`; if `localLimit` is set: counted advisory (rank 1) and the count from
   `TryClaimCountedSlotAsync` (`:851-861`) inside this transaction; over the limit -> rollback, `false`.
2. `AgentTasks` row `FOR UPDATE` (rank 3); recheck **only** `Working && CapacityWaitRetained`;
   lost -> rollback, `false`. (Amendment D-1: no `CapacityWaitId == waitId` recheck. The wait
   comes from `FindUnfinishedCapacityWaitAsync`, which matches `TaskId` or `ConsumerKey`, not
   the task's `CapacityWaitId` pointer.)
3. `CapacityWaitRetained = false`; `SaveChangesAsync`.
4. Only when `wait` is not null **and** `IsEnabled`: `RedeemOnAsync(wait.Id, wait.Key, wait.Kind, Dispatch, consume: true, receipt: Admitted)`
   (ranks 7, 8). Refusal -> rollback, `false`. When `wait` is null or the feature is off, there
   is no consume. That is exactly base: `:631-635` skips the redeem when disabled, and
   `:1756-1757` returns true with no wait.
5. `COMMIT`, `true`.

The tick (`AgentTaskDispatcher.cs:631-652`) replaces `TryRedeemCapacityWaitAsync(..., consume:true)`
and `TryClaimCountedSlotAsync` with one call. Before the call it keeps:
- its local `active` pre-filter (`:613-614`) and the remote occupancy pre-filter (`:617-630`);
- when `_capacityRecovery.IsEnabled`, `FindUnfinishedCapacityWaitAsync`. A non-null wait then
  keeps the base cheap gate of `:1762-1769`: skip when it is `WaitingForHold`, and skip when the
  AsNoTracking provider row does not name this wait and key. Without this gate every ungranted
  retained task would take the counted lock, a task row lock and the provider lock on every tick.

A null wait, or a disabled feature, calls `ReturnRetainedAsync(task.Id, null, localLimit)`.
On `true` the tick sets the tracked `retained.CapacityWaitRetained = false` and marks that
property unmodified (the row is already committed), so `:651-652` does not write it again.
`TryRedeemCapacityWaitAsync`'s `consume:true` branch (`:1779-1782`) is deleted; the helper keeps
only the gate.
- *Rejected (review D-1):* always consume and recheck `CapacityWaitId == waitId`. A retained
  task whose wait is `Exhausted`/`Canceled`/`Superseded`, or that runs with the feature off,
  would refuse on every tick and stay `Working` and retained forever. Retained tasks are not
  orphan-swept, so nothing would end it.
- Witnesses: V-S3-6b `no-unfinished-wait` and `feature-disabled` (guards at base; the PC deletes
  the null branch so that the method always consumes), and V-S3-6c for the remote arm.
- *Why own scope:* Rule P on the tick context is unprovable (Ground truth row 6). The new
  method's transaction holds exactly 1 -> 3 -> 7 -> 8.
- *Remote retained tasks* pass `localLimit: null`: no counted lock, the same task-flag-plus-consume commit.
- *Rejected:* moving only the consume after `TryClaimCountedSlotAsync`. The slot lock is
  statement-scoped, so a second tick can still fill the slot between the two calls.

**D-S3-4 Queue flush (leak 4): consume with the `Sent` stamp.**
- *Gate:* `:2577-2590` calls `RedeemGateAsync` (read-only for the grant; records clock-not-due
  deferral as `RedeemAsync` did) instead of `RedeemAsync`. A refusal still returns `Nothing`.
- *Consume point:* the save that stamps the run `Sent` (`:2737-2749`). When `head.CapacityWaitId`
  and `head.CapacityRecoveryActionKey` are set, that save runs in a transaction (reuse the
  existing `renderTx` shape at `:2740-2746`, opened for both branches), then
  `RedeemOnAsync(db, waitId, key, kind, Queue, ct, consume: true, receipt: Admitted)` after the
  queue-row save and `FreezeCompletionRenderingAsync`, then `COMMIT`.
- *Refusal or class-2/4 failure at the consume:* roll back, reload the `run` entries (S1 D-4: the
  rolled-back save accepted their `Sent` values in the tracker), return `FlushResult.Nothing`.
  Nothing was typed. Class 4 additionally logs; the queue's own retry/withhold logic owns the message.
- *Post-`Sent` failures* (transport `:2779-2793`, spill-write `:2795-2807`, unreachable `:2817-2835`,
  forbidden/truncated/other `:2837-2869`) keep the spend. The attempt happened, exactly as the
  queue's `DeliveryAttempts` is charged and not refunded (`:2712-2714`). The wait is `Admitted`
  with `SelectedMessageId`, so re-arm is revalidation (no increment) and the next grant redelivers.
  That 120 s cadence for a **real** failed delivery is the base N-3 design, kept on purpose.
- *Enter-only consume (amendment D-2).* `EnterOnlyConfirmLockedAsync` gains a parameter
  `CapacityStartAdmission? admission`. When it is set, the method calls
  `ConsumeAdmittedAsync(admission.WaitId, admission.ActionKey, admission.Kind, Queue, ct)`
  **immediately before** `SendInputAsync(sessionId, "\r", ct)` (`:3426`). That is after
  `CanWriteResumeInputAsync`, the specialist spill, the snapshot and the pointer late-confirm and
  observe checks (`:3388-3423`), so none of those exits spends. On a refusal it returns
  `FlushResult.Nothing` **without pressing Enter**. A Herdr-unreachable throw from the Enter
  itself (`:3428-3431`) keeps the spend. The attempt was made, and this is the same direction as
  a post-`Sent` failure.
  - Callers that pass the admission: the two `DeliverNextLockedAsync` calls on a `Pending` head,
    `:2701` and `:2684` via `RecoverPointerAsync` (it threads the admission through). Each passes
    one only when `_capacityRecovery.IsEnabled` and `head.CapacityWaitId`/`CapacityRecoveryActionKey`
    are set.
  - Callers that pass `null`: interrupted-`Sent` recovery (`:3345`, `:3358`) and the human retry
    (`:1508`). An interrupted `Sent` row already paid at its `Sent` stamp after S3. At base this
    path never redeemed either, so requiring a grant there would block crash recovery behind
    the wall.
  - *Why own scope and spend-first:* a keystroke cannot join a database transaction. Committing
    the consume first and then pressing Enter fails toward a spent admission with no turn. That
    is the queue's own stated direction for a crash between the stamp and the write
    (`:2712-2714`). The reverse order fails toward an unpaced turn with no clock, which is the
    leak. *Bound (amendment 2, review A-6):* only the first consume of a wall episode increments
    `AdmissionCount`. After it the wait is `Admitted` with `SelectedMessageId`, re-arms as
    revalidation and never increments again, and Enter-only never charges `DeliveryAttempts`
    (`:3380-3477`). So `MaxAttempts` does **not** bound a repeating Enter-failure cycle. Its real
    bound is the message: one grant per `StalledAdmissionTimeout` (120 s), each paced by the
    provider clock, until the head leaves `Pending` (confirmed, canceled, expired or settled by a
    person) or the session's waits end. That is exactly base (round-1 A-3), because base also
    spends at `:2586` before Enter-only. See Risk 12.
  - *Rejected (review D-2):* treating Enter-only as a non-delivering exit. It submits a provider
    turn. Once the consume moves to the `Sent` stamp, a stamped head with `DeliveryAttempts > 0`
    (a wall hit that `ApplyCapacityHoldAsync` `:5561-5573` stamped, or a revalidation
    redelivery) would be submitted with the grant still held, `NextAdmissionAt` not advanced and
    `AdmissionCount` not moved.
  - Witness: V-S3-8b.
- *What changes for the brief's "retries about every 120 s":* only the non-delivering pre-`Sent`
  exits stop spending and stop waiting for re-arm. They retry on the next flush under the
  still-held grant, bounded by D-12.
- *Residual (review A-2, pre-existing at base).* Two post-spend exits cancel the selected message
  and leave the wait `Admitted` with a receipt: `CancelJustClaimedExpiredBriefsAsync` (`:2753`)
  and `OptionalBriefExpiredException` (`:2758-2761`). It re-arms as revalidation and is regranted
  on every expiry, with no consumer and no `MaxAttempts` bound, until the session's waits end.
  S3 does not change that. It is recorded as Risk 8 and left for a follow-up card that finishes
  the wait when its selected message is canceled.
- *`ApplyCapacityHoldAsync` stamp (`:5561-5573`) stays before the consume.* It is a selection, not
  a spend, and `ActionPending` waits are not re-armed by `RearmStalledAdmissionsAsync`.
- *Rejected:* consume at the delivered verdict (Ground truth row 7). *Rejected:* refunding the
  admission on post-`Sent` failure (S1 rejected redeem-then-compensate; a crash between leaks).
- `BackendUnreachable` refunds the queue attempt (`:2819-2829`) but keeps the admission. Accepted:
  revalidation does not increment, so it costs one re-arm delay, not an attempt.

**D-S3-5 Supervisor failure contract (S1 D-9 applied to the start).** Every exit after the
grant-match read is listed below. Row order is the classification order. The first matching row
wins.

*Catch site (amendment D-4b; lines corrected in amendment 2).* The S3 catches sit in the outer
handler around `TryHandleCapacityWaitAsync` (`AgentSupervisorService.cs:228-245`). They go
**beside** the existing hold catch at `:232` and **before** the generic `catch (Exception)` at
`:240`, in this order:
1. S-1: `ConflictException` with code `capacity_consume_refused`, thrown by the consume or by the
   fresh pre-check (D-S3-2).
2. S-2h: the hold codes. This is the existing `:232` catch, extended with the release.
3. S-2: `ConflictException` with exactly these codes:
   - `capacity_recovery_not_standing`, `capacity_recovery_suspended` and
     `capacity_recovery_liveness_latched` (`AgentControlService.cs:201-222`);
   - `standing_start_intent_revoked` (`:582-583`, suspend or latch set during the start; new in
     amendment 2, the same durable meaning as `capacity_recovery_suspended`);
   - `standing_automatic_fresh_capacity_required` (only the durable clauses remain, D-S3-2);
   - `standing_automatic_fresh_ineligible` (`:178-180`, `:595-597`);
   - `specialist_start_refused` (`:182-183`).

   No `standing_resume_*` code is in this list (see the release call sites below).
4. S-T: the transient class.
5. S-3: the generic catch, `:240`.

This placement covers both start arms. The non-reserve `_control.StartAsync` (`:535`) is outside
`StartSupervisedRestartAsync`'s try, and that try's filter (`:547-551`) lists `standing_resume_*`
only. A refusal from either arm therefore propagates to the outer handler. If the S-1 catch sat
inside `StartSupervisedRestartAsync`, the non-reserve arm's refusal would reach `:240` instead,
and be charged with a backoff and an S-3 consume.

*Release call sites (amendment 2, review A-2).* `ReleaseAgentReservationAsync` is called in
exactly three places, and only when the start carried a `CapacityStartAdmission`:
1. **Inside `StartSupervisedRestartAsync`, for the codes its own filter handles
   (`:547-551`).** The call goes after `held == true`, that is after the `if (!held) throw;` at
   `:569` and the warning at `:570`, and before `return false` at `:571`:
   `ReleaseAgentReservationAsync(agent.Id, admission.Kind, "start-refused", ct)`.
   - When `_scopeFactory` is set it runs in the same fresh scope as `HoldRefusedFreshAsync`;
     otherwise it runs after the `_db.ChangeTracker.Clear()` at `:566`.
   - The ungated path calls `StartSupervisedRestartAsync` with `admission: null` and does not
     release.
2. **In the outer S-2 catch,** with reason `"start-refused"`.
3. **In the outer S-2h catch,** after `ObserveAsync`, with reason `"start-held"`.

*A rethrown `standing_resume_*` is S-3.* When `HoldRefusedFreshAsync` returns false (`!held`:
the generation moved, a concurrent human start won, or `CanReserve` no longer holds), `:569`
rethrows. The code is not in the outer S-2 list, so the generic S-3 catch takes it:
- `RecordStartFailureInFreshScopeAsync` runs, and returns early without a backoff when a launch
  already owns the pointer (`:384`);
- then the S-3 charge runs.

This is base behaviour: base also reached `:240` and had already spent at `:507`. The same holds
for every `standing_resume_*` code thrown by the non-reserve arm or by the reservation itself
(`standing_resume_current_changed` `:552`/`:554`/`:563`, `standing_resume_target_active`
`:534`/`:556`/`:621`, `standing_resume_current_active` `:195`/`:560`) and for
`standing_resume_runner_unavailable` (`:524`, `:531`).

*Tracker rule (amendment D-4e, S1 D-4; S-2h added in amendment 2, review A-1).* The reservation
runs on the supervisor's scoped `_db`. The tick reuses that context across agents. After any
rollback on S-1, S-2h, S-T, S-4 (not committed), S-2 or S-3, `AcceptAsync` may already have saved
the pointer, intent and session edits into that context's tracker. S-2h is included because the
reservation's hold throws at `AgentControlService.cs:585`/`:587` roll back and then reach
`ObserveAsync` on `_db`. So:
- the catch calls `_db.ChangeTracker.Clear()` before any further write on `_db`, including
  S-2h's `ObserveAsync`;
- the S-2/S-3 bookkeeping keeps running in a fresh scope when `_scopeFactory` is set, as base
  does for refusals (`:558-563`).

The witnesses assert that no `Added`/`Modified` entry is left on the harness context after the
tick returns (V-S3-4 `consume-revoked` and `precheck-revoked`, V-S3-4b).

| Class | Exits | Outcome | Bound |
|---|---|---|---|
| S-0 pre-start skip | duplicate action key (`:512-516`), `BlocksAutomaticRestartAsync` (`:523`), launch-owned (`AgentControlService.cs:198-199`), already live (`:225-230`/`:557-561`) | no spend, no write; the grant stays reserved | D-12 revokes after 120 s with no increment and regrants on the next cycle. Compaction is ended by CARD-0079 and launch ownership by the launch itself. **Residual (review A-6, pre-existing):** a live agent that a human started fresh does **not** progress the wait. Progress keys on `wait.SessionId` (`CapacityRecoveryService.cs:883-889`), and `OrphanedWaits` (`:665-680`) never cancels standing waits. The grant therefore cycles every expiry until that agent dies (Risk 9) |
| S-0c card-arm consume failed (amendment 2, review D-3) | the post-spawn `ConsumeAdmittedAsync` (D-S3-2 card arm) refuses or throws anything but a cancellation | caught inside `StartCoreAsync` after the `:326` pointer save; logged at Warning; the consume's own scope rolls back; the launch stands and the start returns normally (`true`); no backoff, no charge, wait and grant as they were | one unpaced launch (Risk 3). The wait cycles at D-12 until the card session dies (no `SessionId` stamp, as Risk 9); then the next capacity start consumes normally |
| S-1 grant race | the grant was revoked or moved between the supervisor's grant-match read and the consume. Raised either by the fresh pre-check's grant clauses (D-S3-2, amendment 2) or by a `RedeemOnAsync` refusal at the consume: `grant-mismatch`, `missing-grant`, `not-action-pending`, `clock-not-due`, `feature-disabled` | `ConflictException("capacity_consume_refused")`; the reservation rolls back (no session, no pointer move); the outer S-1 catch clears the tracker and returns `false`; no release, no cancel, no backoff, no charge | the reconciler owns the wait |
| S-2h hold (amendment D-4a) | `HerdrSupervisionStateService.HeldCode` and `StandingContinuityState.HeldCode`, from either arm, thrown before the reservation (`AgentControlService.cs:192`, `:197`) or inside it (`:585`, `:587`, `:975`, `:1007`; the reservation rolls back) | tracker cleared; as base, `_herdrSupervision.ObserveAsync(agent.Id, false, false, ct)` and `false` with no backoff; **then** `ReleaseAgentReservationAsync(agentId, kind, "start-held")` | a hold does not clear with time; a person or a recovery clears it. **Chosen: S-2 (release).** *Rejected: S-0.* The grant would cycle through D-12 every 120 s for the whole hold, and block every other waiter of that provider kind. When the hold clears, the ungated path restarts through its own holds, as after any S-2 release. No re-grant loop forms (review A-1): the supervisor's early returns at `AgentSupervisorService.cs:133-144` and `:148-149` precede the capacity branch, and a new wait needs a new wall observation, which a dead, held agent cannot produce |
| S-2 durable start refusal | `standing_resume_*` codes handled **and held** by `HoldRefusedFreshAsync` (`AgentSupervisorService.cs:547-572`; release call site 1); the outer S-2 list above (release call site 2) | the reservation was rolled back or never opened; then `ReleaseAgentReservationAsync(agentId, kind, "start-refused")` (new, a 7->8 leaf on its own scope: cancel the agent's unfinished `ActionPending`/revalidation wait, clear its grant; `AdmissionCount` unchanged) | the next tick has no unfinished wait and takes the ungated path with its own holds (continuity, suspend, latch). This is the base outcome after `MaxAttempts`, without the three wasted cycles |
| S-T transient (amendment D-4c) | any exception whose chain holds a `PostgresException` with SQLSTATE `40P01`, `40001` or `55P03`, from the reservation's locks, its save or the consume | reservation rolled back; tracker cleared; return `false` with **no backoff and no charge** (S1 D-9 class 2); the grant stays reserved | `40P01` and `40001` need a concurrent partner. A `55P03` can repeat for as long as a foreign lock holder persists (review A-7): with no backoff that costs one reservation attempt per tick until D-12 revokes the grant (120 s), then the next grant cycle. The reservation now holds ranks 4 and 5 while it waits on rank 7, so it can be a deadlock victim more often than base was |
| S-C cancellation (amendment D-4d) | `OperationCanceledException` while `ct.IsCancellationRequested` (shutdown) | not caught: `:240`'s filter (`!ct.IsCancellationRequested`) already lets it propagate; the reservation is disposed (rolled back); no backoff, no charge. On the card arm a cancellation after the `:326` save leaves the launch standing (as S-0c) | D-12 revokes the grant; the next process regrants |
| S-4 commit acknowledgement lost (amendment D-3) | `reservation.CommitAsync` throws (any exception, including the transient class) | see the S-4 procedure below | as S-3, or a committed start |
| S-3 start throws (infrastructure, model, runner) | every other exception reaching `:240`, including a `standing_resume_*` rethrown at `:569` (`!held`) and a `SpawnAsync` throw on the card arm | reservation rolled back; backoff recorded as today (`RecordStartFailureInFreshScopeAsync`); then `ChargeFailedStartAsync(waitId, actionKey, kind)`, which is `ConsumeAdmittedAsync(..., Start)` and runs only if the grant still matches. A failed launch is a real attempt. A throw after the reservation committed (enqueue `:688`/`:798`, event publish `:328`) finds the grant already cleared and charges nothing, so nothing is spent twice. **A `ModelDisabledException` (`AgentControlService.cs:249`, review A-3):** `RecordStartFailureAsync` links the model hold to the wait first (`AgentSupervisorService.cs:385-400`), so the charge after it usually refuses with `not-action-pending` and nothing is spent. That is accepted: a model hold, not the provider wall, blocked the start | `MaxAttempts` -> `Exhausted` -> ungated restart, as base |

*S-4 procedure (amendment D-3).* Inside `StartInteractiveSessionAsync`, wrap each arm's
`CommitAsync` in a `try`. On a throw:
1. Dispose the reservation (a rollback attempt that tolerates a throw), then
   `_db.ChangeTracker.Clear()`.
2. Open a **fresh scope** and transaction. Take `SELECT … FROM "Agents" WHERE "Id" = @agent FOR UPDATE`.
   This is rank 5 and a leaf: the fresh transaction holds no other row lock. The in-doubt
   reservation still holds that row's lock until the server resolves its `COMMIT`, so the read
   waits the doubt out. A dropped client connection makes the server abort the transaction and
   release the lock. Then reread `agent.PersistentSessionId` and the session row
   (`Id == accepted.Id && StartedAt == acceptedGeneration`). Commit the fresh transaction, which
   wrote nothing.
3. **Committed**, meaning the pointer names the accepted session at its generation: call
   `_launchQueue.EnqueueInteractiveSession(...)` with exactly the arguments the success path
   would have used, and return the session id. The consume committed with the start, so
   `AdmissionCount == 1`, and the supervisor records no backoff.
4. **Not committed:** rethrow the original exception. It reaches the outer handler and is
   classified there as S-T or S-3. The S-3 charge then refuses or consumes against the real,
   resolved state, under the provider lock, so it can never double-spend.
5. **The reread itself throws** (database unavailable): log at Warning and rethrow the
   **original** exception. It is classified as S-T or S-3. Backstop when the commit did apply:
   - the never-enqueued `Starting` row is ended by reconciliation pass 1 after `StartingGraceMs`
     (`SessionReconciliationService.cs:202-213`);
   - the supervisor then sees a dead agent;
   - the `Admitted` wait (no receipt) re-arms through `RearmStalledAdmissionsAsync`.

   That costs at most one attempt and stays bounded by `MaxAttempts`. It is named as Risk 10.

*Rejected (review D-3):* a fresh-scope reread **without** the agent row lock. It can see "not
committed" while the `COMMIT` is still in doubt. S-3 would then record a backoff for a start
that does commit (S1 D-9 class 3).
*Rejected:* "committed -> treat as success" with no enqueue. The enqueue sits after
`CommitAsync`, so the session would stay `Starting` with nothing launching it until pass 1
killed it.

*Rejected:* leaving S-3 at "no spend". Repeated infrastructure failures would then cycle the
grant forever with no attempt cap, which is leak 1's unbounded shape.
*Rejected:* cancelling on S-3. It drops pacing while the wall may still be on.
*Rejected:* charging S-T. A deadlock victim made no attempt, and S1 D-9 class 2 already settled
that.

**D-S3-6 `HeldForRemotePrep` (leak 6): renew the grant while the preparation is observed in flight.**
New `CapacityRecoveryService.RenewGrantAsync(Guid waitId, string actionKey, AgentKind kind, DateTime preparationStartedAt, CancellationToken ct)`,
own scope, 7->8 leaf: under the provider lock, if `GrantedWaitId/GrantedActionKey` still match
and `now - preparationStartedAt < CapacityRecovery:RemotePrepGrantHoldSeconds` (new setting,
**default 0 = off**, amendment per review A-4; Code tests it with an explicit 600), set `GrantedAt = now`. Called from the `RemoteHoldForAsync` branch
(`AgentTaskDispatcher.cs:949-959`) only when the hold is "preparation in flight" (`:1245`) and the
task has an unfinished wait. No task lock is held there (Rule X).
- *Bound:* the preparer's own timeout ends `IsInFlight`; the setting caps renewals; a dead server
  stops renewing and D-12 revokes 120 s later. `AdmissionCount` is untouched (S1 D-3 I4 holds).
- *Trade-off:* other waiters of that provider wait during the preparation. S1 D-10 chose the
  opposite; this slice reverses it only within the cap.
- *Rejected:* advancing `GrantedAt` once at the `HeldForRemotePrep` commit by a fixed budget. It
  would be a provider write inside the claim (Rule P needs a clean tracker there) and survives a
  preparer that died.
- *Ship:* master only, default 0 (off), until the server2 preparation p95 (S1 D-10) is measured; never ported to rc in this card (D-S3-8).
- *Enforcement (review A-4).* The dispatcher enforces the cap: `RenewGrantAsync` reads the in-memory `since`. The reconciler enforces only the 120 s after the last renewal. Renewal also stops whenever an earlier `continue` in the tick skips the task (`AgentTaskDispatcher.cs:849-943`: root budget, sibling guard, platform gate). D-12 then revokes, which is safe. While a renewal holds a provider, for up to the cap, every other waiter of that kind waits.

**D-S3-7 Lock order.** No rank changes. S1 D-1's table, Rule P and Rule X are the contract.
S3 rows (they replace S1 L6, L16, L17 and add L26):

| # | Path | Site(s) after S3 | Sequence | Conformance |
|---|---|---|---|---|
| L16' | Supervisor start, interactive | `AgentSupervisorService.cs:127` tx, agent (5), commit `:226-227`; `AgentControlService.cs:177` `LockAgentAsync` (autocommit); reservation `:543`: sessions `FOR UPDATE` (4) `:546-548` -> agent (5) `:549` -> supervision state, pending queue rows, session insert/edits, incidents -> save `:887` -> **7** consume -> **8** -> commit | 4 -> 5 -> (4: pending queue rows, pre-existing) -> 7 -> 8 | Rule P: the save at `:887` leaves the tracker clean, so `RejectDirtyLaunchRows` is vacuous here (review A-1). The real guarantee is that **nothing is written after the consume** on that connection before `COMMIT`, and V-S3-11 asserts it with a command interceptor. No runner or network await sits between the consume and the commit: `ListAsync` (`:527`) and composition run before `:543`, and the enqueue runs after the commit. The reservation holds the in-process queue locks (`:538-541`) while it waits on rank 7. `CapacityRecoveryService` never takes a queue lock, so that wait forms no hidden cycle. The S-4 reread is a fresh-scope leaf on rank 5. Rule X: no row lock held while waiting on 7 from another connection; 7 is taken on the reservation's own connection after its rows. The pre-existing 5 -> 4 on pending queue rows takes no rank 6/7 and is detectable (n/c). *Cross-process (amendment 2, review A-5):* the transferred pending rows (`:822-831`) may carry a stamped key that the reconciler re-keys under rank 7 (D-S3-10). In-process the reconciler and the supervisor run serially on one hosted loop (`AgentSupervisorHostedService.cs:146-148`), so only a second server on the same database could collide. That is a detected `40P01`, which S-T handles, and Round B's D-S3-10 removes the re-key from rank 7 |
| L16c | Supervisor start, card arm (non-reserve arm only) | `CardService.SpawnAsync` own tx; `AgentControlService.cs:326` pointer save (autocommit on `_db`, agent row only); then own-scope 7 -> 8 consume, its failure caught (S-0c) | n/c, then n/c, then leaf | conforms: the `:326` save holds no lock after it returns, so the consume waits on 7 holding nothing |
| L16f | Supervisor S-2 release / S-3 charge | own scope, 7 -> 8 | leaf | conforms |
| L6' | Retained return | `ReturnRetainedAsync`: counted (1) -> task `FOR UPDATE` (3) -> save -> **7** -> **8** -> commit | 1 -> 3 -> 7 -> 8 | conforms; the counted lock now spans the transaction. It serializes with L1, as intended (review A-1). With no wait, or with the feature off, the sequence is 1 -> 3 -> commit, which takes no capacity lock |
| L17' | Queue flush | gate `RedeemGateAsync` (own scope leaf); `Sent` tx: queue rows (4, under the in-process session gate) -> `FreezeCompletionRenderingAsync` rows -> save -> **7** -> **8** -> commit | 4 -> 7 -> 8 | **Conforms only with D-S3-10 (corrected in amendment 2, review D-4).** `RejectDirtyLaunchRows` passes (no task/agent/session dirty). The round-1 claim that "no 7 -> 4 forms" was wrong. It covered only `RearmStalledAdmissionsAsync`, which skips receipt-backed waits. The grant-**expiry** path re-keys every queue row with the old key while it holds rank 7 (`:141` -> `:157` -> `:1188` -> `:1215` -> `:1401-1407`), and it does not consult `SelectedMessageId`. The wait being consumed at the `Sent` stamp is exactly that `ActionPending` stamped wait, and D-12 allows a consume on an expired but unrevoked grant. So this 4 -> 7 meets the reconciler's 7 -> 4. PostgreSQL detects the cycle (`40P01`), but it is a rank-table violation. Round B therefore ships D-S3-10, which removes every queue write from rank 7. S1 L18 ("capacity reconcile ... conforms") inherits the same error: it is not a 7 -> 8 leaf while `PrepareNextAttemptOnAsync` writes queue rows, and it conforms again only after D-S3-10 |
| L17e | Enter-only consume (D-S3-4) | `EnterOnlyConfirmLockedAsync` -> `ConsumeAdmittedAsync` own scope, then the Enter keystroke | 7 -> 8 leaf | conforms if the queue context holds no open transaction at that point. `renderTx` is opened only at the `Sent` save, but it is guarded by `CurrentTransaction is null` (`:2740`), so some caller may hold one. Code adds `Debug.Assert(db.Database.CurrentTransaction is null)` at the call and checks every caller of the three entry points. If any caller holds a transaction with row locks, the consume must instead run on that context (`RedeemOnAsync(db, …)`, 4 -> 7 -> 8, Rule X) and the plan must be amended |
| L26 | Remote-prep renewal | `RemoteHoldForAsync` branch -> `RenewGrantAsync` own scope | 7 -> 8 leaf | conforms |

**D-S3-10 Round B decision: no queue write under the provider lock (amendment 2, review D-4).**
This is a decision only. The Round B amendment designs its witnesses and Code details before the
Round B Code task is cut. Round A does not depend on it.

*Chosen: option (a), re-key outside the provider lock, eagerly.*
- `PrepareNextAttemptOnAsync` becomes capacity-only for **every** caller, which matches the
  independent plan's invariant. It deletes the queue re-key (`CapacityRecoveryService.cs:1398-1407`).
  The callers are:
  - expiry: `GrantReadyAsync` `:157` -> `:1188` -> `:1215`;
  - stall re-arm: `RearmStalledAdmissionsAsync` `:1359`;
  - new wall: `BumpWaveOnAsync` `:827`, reached from `BumpWaveAsync` `:794` and from a
    caller's context at `ApiErrorRecoveryService.cs:588`;
  - `PrepareNextAttemptAsync` `:752`.
- A new own-scope `RealignQueueKeysAsync(IReadOnlyCollection<Guid> waitIds, CancellationToken ct)`
  takes **no advisory lock**. For each wait it runs one short transaction:
  1. read the wait AsNoTracking (current `ActionKey` and `Version`; skip it unless unfinished);
  2. update `SessionQueuedMessages`, setting `CapacityRecoveryActionKey = @current` and
     `CapacityWaitVersion = @version` where the key starts with `{waitId:N}:` and differs from
     `@current`, and no row already holds `@current`.

  That is a rank-4 leaf. It matches on the deterministic key prefix (`CapacityRecoveryPolicy.cs:16-17`)
  as well as on `CapacityWaitId`, so a row whose two fields were ever written apart is still
  found. Base re-keyed by equality with the previous key. Because every write sets both fields
  together (Ground truth, amendment 2), the two matches select the same rows.
- *When it runs:*
  - right after each provider transaction that moved an `ActionKey` commits: per kind in
    `GrantReadyAsync` after `:234`, per candidate in `RearmStalledAdmissionsAsync` after its
    save, and after `BumpWaveAsync`'s and `PrepareNextAttemptAsync`'s saves.
    `ApiErrorRecoveryService.cs:588` runs on its caller's transaction. It calls the realign
    after that caller commits, or it relies on the start-of-reconcile heal below; the Round B
    amendment picks one after reading that caller.
  - once at the **start** of `ReconcileAsync`, for every unfinished wait that has a stale-keyed
    row. This heals a crash between a provider commit and its realign before any grant in that
    pass.
- *Why there is no stranded or duplicate key:*
  - A wait whose key just moved is not granted in the same provider transaction. Expiry and a
    new wall set `DueAt = now + StalledAdmissionTimeout` (`:1220`), and the grant loop skips a
    future `DueAt` (`:195-196`).
  - A stall re-arm's realign commits before `GrantReadyAsync` runs in the same pass.
  - A crash leftover is healed at the start of the next pass, before its grants.
  - So no grant of the new key, and no `ApplyCapacityHoldAsync` stamp of it
    (`SessionMessageQueueService.cs:5557-5573`, which needs a grant), can precede the realign
    in-process.
  - While a row's key is stale, the flush gate refuses (no grant matches it) and the hold
    selection (`:5579-5597`) treats it as a row whose key names no current wait, as at base for
    any unmatched key. That is a delay, not a spend.
  - The `NOT EXISTS` clause turns a residual multi-server collision with the unique index into a
    logged skip instead of a `23505`.
- *Lock result:* the reconciler then holds rank 7 only over capacity rows (a 7 -> 8 leaf, which
  S1 L18 claimed). The realign holds only queue rows, and it waits on a `Sent` transaction's row
  while holding nothing. The L17' cycle cannot form, and the reconciler can no longer be that
  cycle's `40P01` victim.
- *Witnesses (designed in the Round B amendment):*
  - `C1593_S3_expiry_rekey_holds_no_provider_lock_while_queue_row_locked`: a test connection
    holds the stamped head row `FOR UPDATE`, and a reconcile runs at grant age >= 120 s.
    `GrantReadyAsync` commits without waiting. The realign backend waits on the row with no
    `antiphon.capacity.kind.*` key in `pg_locks`, and after release the row carries the new key.
    Red at base: the reconcile backend waits on the row while holding the provider key.
  - `C1593_S3_reconcile_heals_stale_queue_key_before_grant`: a seeded stale-keyed row with the
    wait already re-keyed. One `ReconcileAsync` realigns it before granting.

*Where this differs from the independent plan (`origin/feat/card-task-30f56bd5` `63036f8e`, its
D-5 and lock row "Queue rekey / expiry / new wall").* That plan also removes the queue write from
rank 7, and that invariant is adopted. It replaces the eager re-key with a consumer-side repair
under the queue locks, limited to never-attempted rows, and changes the semantics of attempted
rows (late-confirm first, never overwrite attempt coordinates). This plan does not adopt that
repair:
1. *Unique index.* A lazily repaired stale row can collide with a null-key row that
   `ApplyCapacityHoldAsync` (`:5561-5573`) has already stamped with the current key, which gives
   a `23505`. Base avoids this only because its re-key is eager. The eager realign keeps that
   property.
2. *Transferred rows.* A standing start moves pending rows to the new session (`AgentControlService.cs:822-831`).
   A repair scoped to the consuming session's queue locks can miss rows that moved. The realign
   matches by wait, not by session.
3. *Scope.* The attempted-row semantics belong with S3d's receipt rules, not with a lock-order
   fix. The realign changes only where the base re-key runs, not what it writes.

*Rejected: option (b), accept the detected `40P01`.* The rank violation would remain. Every
victim on the reconciler side would also skip that tick's supervisor pass and later sweeps,
because `AgentSupervisorHostedService.cs:146-148` share one `try`, and witnesses for both victims
would only pin a defect. Giving `ReconcileAsync` its own `try` is reasonable hardening for other
reconcile faults, but S3 does not need it after (a). It is left to the orchestrator to file.

*Rejected:* re-keying in a separate transaction after the provider commit with **no**
start-of-pass heal. A crash between the two commits would strand the row: its old key would never
match a later grant, and a later `PrepareNextAttemptOnAsync` would compare against the new key
only.

Round B stays after Round A, master-only (D-S3-8). D-S3-10 adds files to S3d
(`CapacityRecoveryService.cs`, and `ApiErrorRecoveryService.cs` if that caller realigns itself).

**D-S3-8 Shipping (amended per the review's slicing recommendation, adopted).** There are three
Code rounds, all on master first:
- **Round A: S3a + S3b, the High supervisor leak.**
  - One Code task cut from `origin/master`. The touched files are identical to the rc base, so
    every line number in this plan holds there.
  - Its rows are CP-1..CP-6, including the widened standing-restart coverage (CP-5). It needs a
    Review, then a master land.
  - **The rc port comes only after a clean master Review.** `rc/2026-10-27` is
    mid-qualification. Whether the current rc needs the port at all is the orchestrator's call.
  - The port cherry-picks the S3a and S3b commits, drops this plan doc (rc carries no
    `docs/superpowers/plans`), re-runs CP-1..CP-6 on the rc tip, and goes through its own
    Review.
- **Round B: S3c + S3d + S3e.**
  - A Code task cut from master after round A lands. It is **master-only** until it has soaked.
  - S3c fixes a stale snapshot only. S3d is the most complex slice and carries D-2.
  - S3e (V-S3-12) rides this round because it is the last of S3b/S3c/S3d on master (review A-7).
    V-S3-12 cannot go green on rc until S3c and S3d are ported, and that port needs a separate
    decision.
- **Round C: S3f.**
  - Master-only, with `RemotePrepGrantHoldSeconds` **default 0 (off)**. It is never ported to rc
    in this card.
  - *Renew or release (review 8f6e5b17 A-10, deferred).* The independent plan releases the grant at `HeldForRemotePrep` and reacquires it; this plan renews `GrantedAt`. With the cap and default 0, renewal is acceptable. The choice is made in the Round C amendment, not now.
  - It is cut after round B lands, because it touches `AgentTaskDispatcher.cs` and
    `CapacityRecoveryService.cs`, the same source area as S3c. The same-area rule defers it.

*Rejected:* the round-1 order (all of S3a-S3e to rc first). rc is mid-qualification, and S3c/S3d
fix lower-severity leaks with more complex changes.

**D-S3-9 Next stage.** `next: review` of this amended plan. The amendments add new behaviour:
- the S-4 reread procedure and its enqueue;
- the Enter-only consume and its caller split;
- the catch placement and the S-2h/S-T classes.

These are design changes, not mechanical edits, so a Review should confirm them before Code. The
plan carries its own `## Verification design` and `### Checkpoints`, so Code can follow an
approving Review directly.

*Amendment 2.* Design review 8f6e5b17 named the open Round A decisions: D-1 to D-3 and A-2. This
amendment settles each of them in the text:
- the pre-check classes and the clauses that are deleted;
- the `in-doubt` S-4 witness;
- the card-arm placement, its throw rule and S-0c;
- the release call sites and the S-3 class for a rethrown `standing_resume_*`.

Round A (S3a + S3b) is now mechanical: `next: code` for Round A on master, followed by the Code
Review. The Code Review must also check the three places where this amendment departs from the
review's literal text:
1. a pre-check `ActionKey` mismatch is S-1, not S-2 (D-S3-2);
2. `standing_start_intent_revoked` joins the S-2 list (D-S3-5);
3. the `in-doubt` witness enqueues the test transaction before the reservation releases (V-S3-5b).

Round B needs its own plan amendment for the D-S3-10 witnesses before its Code task.

## Slices

| Slice | Round | Files | Change | Tests (red first) |
|---|---|---|---|---|
| S3a | A | `server/Application/Services/CapacityRecoveryService.cs`, new `server/Application/Services/CapacityConsumeReceipt.cs` | D-S3-1: `receipt` parameter, `Admitted` mode, revalidation accepted, session stamp, `ConsumeAdmittedAsync`; `RedeemAsync` note | V-S3-1 |
| S3b | A | `AgentSupervisorService.cs` (`:121-245` admission holder and outer catches, `:467-527`, `:529-573` admission parameter and release call site 1), `AgentControlService.cs` (constructor `LandDeliveryBoundary`, `:150-160`, `:162`, `:320-328` card-arm consume after the pointer save, `:543-888` consume and S-4, `:602-615` pre-check), `CapacityRecoveryService.cs` (`ReleaseAgentReservationAsync`, `ChargeFailedStartAsync`, `ConsumeAdmittedAsync` stamp parameters) | D-S3-2, D-S3-5 | V-S3-2..V-S3-5c, V-S3-11 `supervisor-start` |
| S3c | B | `AgentTaskDispatcher.cs` (`:608-652`, `:1752-1783`), `CapacityRecoveryService.cs` (`ReturnRetainedAsync`) | D-S3-3 | V-S3-6..V-S3-7, V-S3-11 `retained-return` |
| S3d | B | `SessionMessageQueueService.cs` (`:2577-2590`, `:2684`, `:2701`, `:2737-2749`, `:3380-3431`), `SessionMessageQueueService.CodexPointers.cs` (`:124-148`), `CapacityRecoveryService.cs` (`:1398-1407`, `RealignQueueKeysAsync`, its call sites; D-S3-10), possibly `ApiErrorRecoveryService.cs` (`:588`) | D-S3-4, D-S3-10 | V-S3-8..V-S3-10, V-S3-11 `queue-sent` |
| S3e | B (last) | `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` | no-production-caller guard for `RedeemAsync` | V-S3-12 |
| S3f | C | `AgentTaskDispatcher.cs` (`:949-959`), `CapacityRecoveryService.cs` (`RenewGrantAsync`), `server/Application/Settings/SupervisionSettings.cs` (`CapacityRecoverySettings.RemotePrepGrantHoldSeconds`, default 0) | D-S3-6 | V-S3-13 |

V-S3-11 is one method. It lands in S3b with its `supervisor-start` argument, and S3c and S3d each
add their own argument. Each slice commits its red tests first (git-common), then the fix.

## Verification design

"Red at base" means red on the slice's test commit before its product commit, with the named
assertion; a build or fixture error is not red. A guard passes at base and names a method-scoped
positive control (PC) that must turn it red. Every supervisor witness also asserts that no
`Added`/`Modified` entry is left on the harness context after a tick that returned `false`
(tracker rule, D-S3-5).

| ID | Test | Asserts | Base |
|---|---|---|---|
| V-S3-1 | `CapacityAdmissionLifecycleTests/C1593_S3_revalidation_grant_is_consumable_in_a_transaction` `[Arguments("launch")]`, `("admitted")` | a revalidation grant (`Admitted`, `NeedsRevalidationGrant`, `SelectedMessageId` set) consumed inside a caller transaction is accepted without increment; `admitted` additionally asserts the row equals what `RedeemAsync` writes (state, reason, clock, grant cleared, no new receipt field) and that a rollback leaves the grant. The test commit calls the base overload (`consume: true`, `launchSessionId`) for both arguments; the fix commit switches the `admitted` argument to `receipt: Admitted` | red at base, both arguments: `not-action-pending` (`CapacityRecoveryService.cs:343-344`) |
| V-S3-2 | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_skip_does_not_spend` `[Arguments("compaction-block")]`, `("duplicate-key")` | granted standing wait; the skip condition holds; after the tick `AdmissionCount == 0`, wait `ActionPending`, grant still the wait, no start | red at base: `AdmissionCount == 1`, grant null (`RedeemAsync` `:507` ran first) |
| V-S3-3 | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_refusal_releases_the_wait` `[Arguments("standing_resume_delivery_pending")]`, `("capacity_recovery_suspended")`, `("herdr-hold")` | `standing_resume_delivery_pending`: `StartSupervisedFreshAsync` refuses (S-2); the wait is `Canceled`/`start-refused`, the grant is cleared, `AdmissionCount == 0` and continuity is held. `capacity_recovery_suspended` and `herdr-hold` are race exits. The supervisor already skips suspended and held agents at `AgentSupervisorService.cs:133`/`:148`, so the test sets the condition on another connection at a new test-only boundary, `capacity-supervisor-before-start`. That boundary is reached in `TryHandleCapacityWaitAsync` after the grant-match, duplicate-key and compaction checks, immediately before the start. For `capacity_recovery_suspended`, the non-reserve arm then refuses at `AgentControlService.cs:209-213` (S-2, review D-5), with the same wait outcome. For `herdr-hold`, the start throws `HeldCode` at `:197` (S-2h); the wait is `Canceled`/`start-held`, there is no backoff and the Herdr observation is recorded. In every argument the next tick reaches the ungated path (no capacity branch) | red at base: `AdmissionCount == 1`, wait `Admitted`. The boundary is not on the base path, so the two race arguments start normally at base: `Admitted`, launched |
| V-S3-4 | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_start_and_consume_commit_together` `[Arguments("commit-visibility")]`, `("consume-revoked")`, `("precheck-revoked")` | `commit-visibility`: a `DbTransactionInterceptor` on the harness context runs at `TransactionCommittingAsync` of the transaction that inserted the new `AgentSessions` row and, from a separate connection, reads the wait: still `ActionPending`, `AdmissionCount == 0`, grant held (the spend is not visible before the start commits); after the tick `AdmissionCount == 1`, `Admitted`, `SessionId == ` the new session. `consume-revoked`: a test `LandDeliveryBoundary` point `capacity-start-before-consume` revokes the grant on another connection: no new session row, pointer unchanged, `AutomaticFreshConsumed` false, `AdmissionCount == 0`, no backoff recorded. `precheck-revoked` (amendment 2, review D-1): the reserve arm (`CanReserve` true); the `capacity-start-before-precheck` boundary (immediately before `AgentControlService.cs:602`) revokes the grant on another connection (clears `GrantedWaitId`/`GrantedActionKey`, as D-12 would). Assert the start throws `capacity_consume_refused` and the tick returns `false`; the wait is **not** `Canceled` and keeps its state (`ActionPending`) and `ActionKey`; the provider grant is still the null the test wrote (no release ran); `AdmissionCount == 0`; `RestartBackoffFailures` and `NextRestartAt` unchanged; no new session row; and a clean tracker | red at base: `commit-visibility` reads `Admitted`/`AdmissionCount == 1` inside the interceptor (base committed `RedeemAsync` at `AgentSupervisorService.cs:507-508` before the start); `consume-revoked` and `precheck-revoked`: the boundaries are not on the base path, so base launches (the "no new session row" and `AdmissionCount == 0` assertions fail) |
| V-S3-4b | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_exit_without_attempt_neither_charges_nor_backs_off` `[Arguments("consume-refused-nonreserve")]`, `("transient-40P01")`, `("canceled")` | `consume-refused-nonreserve`: `CanReserve` is false, so the start goes through `_control.StartAsync` (`:535`). The `capacity-start-before-consume` boundary revokes the grant. Assert no session row, `RestartBackoffFailures` and `NextRestartAt` unchanged, `AdmissionCount == 0`, and a clean tracker. This pins the S-1 catch beside `:232` (review D-4b). `transient-40P01`: an interceptor throws `PostgresException 40P01` from the reservation's consume save. Assert that nothing commits, there is no backoff, `AdmissionCount == 0`, the grant is still the wait and the tracker is clean. The next tick (fault spent) starts once with `AdmissionCount == 1`. `canceled`: the token is canceled at the boundary. Assert that `OperationCanceledException` propagates, the reservation rolls back, there is no backoff and `AdmissionCount == 0` | red at base, all three: base spends before the start (`AdmissionCount == 1`). The first argument's boundary is not on the base path, so base launches. `transient-40P01` also records a backoff at base |
| V-S3-5 | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_start_throw_charges_one_attempt` | the start throws an infrastructure error (S-3) three times across re-grants: `AdmissionCount` 1, 2, 3, then `Exhausted`, then the ungated restart runs | guard (base also spends once per failed start). PC: delete the `ChargeFailedStartAsync` call -> red (`AdmissionCount == 0` after the first failure) |
| V-S3-5b | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_lost_commit_ack_launches_once` `[Arguments("committed")]`, `("not-committed")`, `("in-doubt")` | `committed`: an interceptor throws from `TransactionCommittedAsync` of the reservation, after the server commit. Assert `AdmissionCount == 1`, wait `Admitted` with `SessionId` = the new session, exactly **one** launch (the fake adapter launch count, and the launch queue enqueued once with the accepted generation), and no backoff. `not-committed`: an interceptor throws `XX000` from `TransactionCommittingAsync`. Assert no session row, a backoff recorded (S-3), and `AdmissionCount == 1` from the S-3 charge with exactly one charge. `in-doubt` (amendment 2, review D-2): it proves that the reread **waits** under `FOR UPDATE`. Use the non-reserve resume arm (`CanReserve` false, a resumable `previous`), so the accepted session id equals the old pointer and only `StartedAt` tells the generations apart. At `TransactionCommittingAsync` of the reservation the interceptor: (1) captures the accepted session id, `acceptedGeneration` and the consume's wait values from the harness tracker; (2) starts a test transaction on its own connection that runs `SELECT … FROM "Agents" WHERE "Id" = @agent FOR UPDATE`. That statement blocks, because the reservation still holds the row (`:549`). (3) It polls `pg_stat_activity` until that backend waits on a `Lock`; (4) then it throws `XX000`, so the reservation never commits. S-4 step 1 rolls back, PostgreSQL grants the row to the test transaction first (its request was queued first), and S-4 step 2's reread queues behind it. The test transaction, standing in for the in-doubt `COMMIT`, writes the committed state: `AgentSessions.StartedAt = acceptedGeneration`, `Status = Starting`, and the wait as the consume would have left it (`Admitted`, `AdmissionCount = 1`, `SessionId`, grant cleared). It polls `pg_stat_activity` for a second backend waiting on a `Lock` for that row (the reread), or for the tick task to finish, with a 30 s deadline, and **then** commits. Assert exactly one enqueue with `acceptedGeneration`, no backoff (`RestartBackoffFailures`, `NextRestartAt` unchanged), and `AdmissionCount == 1`, the value the test wrote, unchanged by any S-3 charge | `committed` and `in-doubt` are red at base: base sees the throw from `CommitAsync`, never enqueues (zero launches) and records a backoff. `not-committed` is a guard. PCs: (1) replace the S-4 reread with "assume committed" -> `not-committed` red (an enqueue for a missing row, no backoff); (2) **drop `FOR UPDATE` from the S-4 reread** -> `in-doubt` red: the reread runs before the test transaction commits, sees the old `StartedAt`, rethrows to S-3, records a backoff and enqueues nothing. *Departure from the review's step 1:* the review had the test take the agent row and write **at** `TransactionCommittingAsync`. While the reservation still holds the row that would block the interceptor's own thread forever. Queuing the test request first and writing after the rollback keeps the same proof without a self-deadlock |
| V-S3-5c | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_card_start_spends_after_spawn` `[Arguments("spends-after-spawn")]`, `("consume-throws")` | the agent has pending card work, `CanReserve` is false, and its granted standing wait takes the card arm (`CardService.SpawnAsync`). `spends-after-spawn`: assert one spawn, `AdmissionCount == 1`, `Admitted`, `SessionId == spawn.SessionId` and the grant cleared. `consume-throws` (amendment 2, review D-3): a one-shot `DbCommandInterceptor`, registered for the consume's own scope, throws `PostgresException XX000` on the consume's wait `UPDATE`. Assert one spawn; `PersistentSessionId == spawn.SessionId` and `Status == Running`, saved; the tick returns `true`; no backoff (`RestartBackoffFailures` and `NextRestartAt` unchanged); `AdmissionCount == 0`, wait `ActionPending`, grant still the wait; and a clean tracker | `spends-after-spawn` is a guard (base spends before the spawn). PC: delete the post-spawn `ConsumeAdmittedAsync` -> red (`AdmissionCount == 0`). `consume-throws` is red at base: base spends at `:507` before the spawn and the interceptor never fires, so `AdmissionCount == 1`. Second PC, for the throw rule: delete the S-0c catch -> red (the exception reaches S-3, and the charge, with the one-shot fault spent, consumes: `AdmissionCount == 1`) |
| V-S3-6 | `CapacityRecoveryTaskTests/C1593_retained_slot_filled_after_snapshot_does_not_spend` | local limit 1, snapshot count 0, `AfterQueuedSnapshotAsync` inserts one counted `Working` row; retained task holds the grant: `AdmissionCount == 0`, grant still held, `ActionPending`, `CapacityWaitRetained` still true | red at base: `AdmissionCount == 1`, `Admitted` (investigation item 3) |
| V-S3-6b | `CapacityRecoveryTaskTests/C1593_retained_return_without_live_wait_resumes` `[Arguments("no-unfinished-wait")]`, `("feature-disabled")` | `no-unfinished-wait`: the retained task's wait is `Exhausted`, with a local slot free. `feature-disabled`: capacity recovery is off and the wait is `ActionPending`. In both, one tick sets `CapacityWaitRetained == false`, the task counts as active, and the wait row is byte-unchanged (no consume) | guards (base returns both without a consume, `AgentTaskDispatcher.cs:631-635`/`:1756-1757`). PC: delete `ReturnRetainedAsync`'s null/disabled branch so that it always consumes and rechecks the wait -> red (still retained) |
| V-S3-6c | `CapacityRecoveryTaskTests/C1593_retained_remote_return_commits_flag_and_admission` | a remote retained task (`RunnerId` set; runner limit 2, occupancy 0) holds the grant. One tick sets the flag false, `AdmissionCount == 1`, `Admitted`, grant cleared, and takes no counted advisory (`localLimit: null`) | guard (base consumes for a remote return too). PC: pass `wait: null` on the remote arm -> red (`AdmissionCount == 0`) |
| V-S3-7 | `CapacityRecoveryTaskTests/C1593_retained_return_commits_flag_and_admission_together` | interceptor throws `XX000` from `TransactionCommittingAsync` on the return transaction: `CapacityWaitRetained` true, `AdmissionCount == 0`, grant held; next tick (fault spent) returns once with `AdmissionCount == 1` | red at base: the spend commits before the flag (`AdmissionCount == 1`, flag true after tick 1) |
| V-S3-8 | `CapacityRecoveryQueueTests/C1593_flush_exit_before_sent_does_not_spend` `[Arguments("spill-hold")]`, `("quota-suppressed")` | stamped head, grant held; force the pre-`Sent` exit (`HoldSpillBodyForDeliveryAsync` false / a matching supervision quota suppression): `AdmissionCount == 0`, grant still the wait, message `Pending`, `DeliveryAttempts == 0` | red at base: `AdmissionCount == 1`, grant null |
| V-S3-8b | `CapacityRecoveryQueueTests/C1593_enter_only_confirm_spends_before_enter` `[Arguments("pending-head")]`, `("pointer-recovery")`, `("consume-revoked")` | `pending-head`: a stamped `Pending` head with `DeliveryAttempts == 1` and its body visible whole on screen, with the grant held. The flush presses Enter once (fake runtime input `"\r"`). Assert `AdmissionCount == 1`, `Admitted`, `NextAdmissionAt` advanced and the grant cleared. `pointer-recovery`: the same for a Codex pointer head through `RecoverPointerAsync` (`CompleteComposer`). `consume-revoked`: the `capacity-enter-before-consume` boundary revokes the grant. Assert that no Enter is sent, `FlushResult.Nothing`, `AdmissionCount == 0` and the message is still `Pending` | `pending-head` and `pointer-recovery` are guards (base spends at `:2586`). PC: delete the Enter-only consume -> red (`AdmissionCount == 0`, null clock, review D-2). `consume-revoked` is red at base: the boundary is not on the base path, so base presses Enter |
| V-S3-9 | `CapacityRecoveryQueueTests/C1593_flush_sent_and_admission_commit_together` `[Arguments("commit-fault")]`, `("consume-revoked")` | `commit-fault`: interceptor throws `XX000` on the `Sent` commit: message `Pending`, nothing typed (fake runner write count 0), `AdmissionCount == 0`, grant held. `consume-revoked`: grant revoked at test boundary `capacity-queue-before-consume`: same outcome and `FlushResult.Nothing` | red at base: `commit-fault` has the spend committed before the stamp (`AdmissionCount == 1`); `consume-revoked`: the boundary is not on the base path, so base types the message (write count 1) |
| V-S3-10 | `CapacityRecoveryQueueTests/C1593_failed_delivery_after_sent_rearms_as_revalidation` | transport throw after `Sent`: `AdmissionCount == 1`, `Admitted`, `SelectedMessageId` set, message `Pending`; after 121 s reconcile `NeedsRevalidationGrant`; regrant; redelivery consumes without increment and the message is `Sent` | red at base only on the last step (base `RedeemAsync` accepts revalidation, so this is a **guard**). PC: drop the revalidation clause from the S3a state check -> the redelivery returns `Nothing` |
| V-S3-11 | `CapacityLockOrderTests/C1593_S3_path_blocked_on_row_holds_no_provider_lock` `[Arguments("supervisor-start")]` (S3b), `("retained-return")` (S3c), `("queue-sent")` (S3d) | a test connection holds a row `FOR UPDATE`: for `supervisor-start`, the involved `AgentSessions` row, which is the first lock the reservation takes (`AgentControlService.cs:546-548`; amended per review A-4: holding the agent row would block at the autocommit `LockAgentAsync` `:177`, before the reservation); for `retained-return`, the retained task row; for `queue-sent`, the head queue row. The path's backend waits on it (`pg_stat_activity`), and `pg_locks` shows no `antiphon.capacity.kind.*` key for that pid. The lock is released and the path completes with one spend. **Amended (review A-1):** a `DbCommandInterceptor` on the path's connection records every command after the consume's provider-state `UPDATE`. The only command allowed before the transaction ends is `COMMIT`. This replaces the vacuous `RejectDirtyLaunchRows` check | guards at base (base spends earlier, on another connection). PCs: `supervisor-start`: move the consume to immediately after `BeginTransactionAsync` (`:543`), ahead of the session locks -> red on the capacity-key assertion (the blocked backend holds the provider key) **and** on the nothing-after-consume assertion. `retained-return` and `queue-sent`: move each consume ahead of its last row write (bypassing `RejectDirtyLaunchRows`) -> red on both assertions |
| V-S3-12 | `CapacityLockOrderTests/C1593_S3_wrapper_has_no_production_caller` | a source scan of `server/**/*.cs` finds no `.RedeemAsync(` | red at base: three call sites. It rides round B (the last of S3b/S3c/S3d on master) |
| V-S3-13 (round C, master) | `CapacityAdmissionLifecycleTests/C1593_remote_prep_keeps_the_grant_while_in_flight` `[Arguments("in-flight")]`, `("past-cap")`, `("default-off")` | `in-flight`: setting 600; after `HeldForRemotePrep`, advance 121 s with `IsInFlight` true, tick, then `GrantReadyAsync`. Assert the grant is still the wait and `AdmissionCount == 0`. `past-cap`: setting 60; advance past it, and the grant expires (`grant-expired`). `default-off`: no setting; at 121 s the grant expires exactly as at base | `in-flight` red at base (S1 V-4 c5a outcome). `past-cap` and `default-off` are guards. PC for `past-cap`: drop the cap check -> red. PC for `default-off`: default the setting to 600 -> red |

**Existing tests that change meaning, and how their strength is kept.**
- `CapacityRecoverySupervisionTests.Card0412_D8_*` and `StandingRestartEscalationTests.C510_S2_capacity_grant_is_required_and_follows_fresh`
  (2 args) and `C510_S2_cleared_model_hold_grant_starts_resume_on_the_next_tick`: **no edit**.
  `Admitted` mode preserves `AdmissionCount == 1/2`, no receipt, `SessionId == newId`. If any
  assertion fails, Code stops and reports a plan amendment instead of editing the test.
- `Card0412_V18_accepted_action_key_is_not_relaunched`: no edit; the duplicate-key read still
  refuses, now without spending (V-S3-2 `duplicate-key` asserts the count).
- `CapacityRecoveryQueueTests.Card0412_V12_*`: no edit; the stamped head still delivers once
  with one admission.
- `CapacityRecoveryTaskTests.Card0412_V19_try_claim_*` call `TryClaimCountedSlotAsync` directly;
  it stays (statement-scoped semantics unchanged), so no edit.
- `CapacityAdmissionLifecycleTests`, `CapacityLockOrderTests`, `WallRerouteDispatchTests`,
  `ReceiptFailureDeliveryTests`, `AgentTaskDispatchBaseGuardTests`: unchanged; `Launch` mode is
  the default, so S1's claim behaviour is byte-identical except that a revalidation grant is now
  consumable (no S1 test seeds one; `grep not-action-pending tests/` is empty at base).


- `StandingRestartEscalationTests` (52 results across 9 partials; review D-5 counted 51, and a
  re-count at the base gives 52: 1+1+2+22+4+1+1+18+2) and `AgentSupervisionTests` (10): **no
  edit**. CP-5 runs both whole classes after S3b. The `Guards` variants `herdr-hold`,
  `suspended`, `latched` and `disabled` cover the ungated reservation, not the capacity branch.
  If any of them goes red, Code stops and reports a plan amendment.

Deterministic means no sleeps. The tests use:
- `FakeTimeProvider`;
- `pg_stat_activity`/`pg_locks` polling with a 30 s deadline;
- test-only `LandDeliveryBoundary` points, as S1's `capacity-consume-before`:
  `capacity-supervisor-before-start`, `capacity-start-before-precheck`, `capacity-start-before-consume`,
  `capacity-queue-before-consume` and `capacity-enter-before-consume`.

### Red witnesses (run before each product commit, not in the table)

One build-slot build of the slice's test commit, then `--no-build`. Plain method segments run
every `[Arguments]` case.
- S3a: `/*/*/CapacityAdmissionLifecycleTests/C1593_S3_revalidation_grant_is_consumable_in_a_transaction`
- S3b: `/*/*/CapacityRecoverySupervisionTests/(C1593_S3_supervisor_skip_does_not_spend*)|(C1593_S3_supervisor_refusal_releases_the_wait*)|(C1593_S3_supervisor_start_and_consume_commit_together*)|(C1593_S3_supervisor_exit_without_attempt*)|(C1593_S3_supervisor_lost_commit_ack*)|(C1593_S3_supervisor_card_start*)`
  (expect `lost_commit_ack` `committed` and `in-doubt` red and `not-committed` green; `card_start` `consume-throws` red and `spends-after-spawn` green; every other case red).
- S3c: `/*/*/CapacityRecoveryTaskTests/(C1593_retained_slot_filled_after_snapshot_does_not_spend*)|(C1593_retained_return_commits_flag_and_admission_together*)`
- S3d: `/*/*/CapacityRecoveryQueueTests/(C1593_flush_exit_before_sent_does_not_spend*)|(C1593_flush_sent_and_admission_commit_together*)|(C1593_enter_only_confirm_spends_before_enter*)`
  (expect only `consume-revoked` of `enter_only` red).
- S3e: `/*/*/CapacityLockOrderTests/C1593_S3_wrapper_has_no_production_caller`
- S3f: `/*/*/CapacityAdmissionLifecycleTests/C1593_remote_prep_keeps_the_grant_while_in_flight`
  (expect `in-flight` red; `past-cap` and `default-off` green).

PCs (method-scoped, each run once and restored): V-S3-5, V-S3-5b `not-committed` and `in-doubt` (drop `FOR UPDATE`), V-S3-5c `spends-after-spawn` and `consume-throws` (delete the S-0c catch),
V-S3-6b (both arguments), V-S3-6c, V-S3-8b `pending-head` and `pointer-recovery`, V-S3-10, V-S3-11
(one per argument, in its own slice), V-S3-13 `past-cap` and `default-off`.

Suffix check for the red-witness OR filters. `C1593_S3_supervisor_start_and_consume_commit_together*`
and `C1593_S3_supervisor_start_throw_charges_one_attempt` differ at `start_a` and `start_t`, and
`C1593_S3_supervisor_card_start*` differs from both at `card_` (amendment 2), and
neither is a prefix of the other. `C1593_S3_supervisor_exit_without_attempt*` and
`C1593_S3_supervisor_lost_commit_ack*` have no sibling with the same prefix. In the Queue filter,
`C1593_enter_only_*` matches no other method, and neither does any Task-filter prefix.

## Performance budget

Per [test performance standards](../../test-performance-standards.md) and
[docs/testing-and-build.md#performance-standards](../../testing-and-build.md):
- **Measured (S1 rounds on server2):**
  - single capacity methods 1.2-11.4 s;
  - first test per host about 10-11 s (shared Postgres plus a clone);
  - leased `tests/Antiphon.Tests` builds 217-399 s to compile, 220-520 s with the slot wait.

  Whole Unit is over 25 min and is not run. `StandingRestartEscalationTests` and
  `AgentSupervisionTests` have no recorded p95. The two `Transient` methods loop four isolated
  schemas each, so CP-5 is the row most likely to run long.
- **Shared setup:** one shared Postgres per test host, and one cloned database per test
  (`CapacityRecoveryTestSupport`, `TestDbFixture.CreateIsolatedSchemaAsync`). Supervisor tests
  reuse `AgentSupervisionTests.BuildHarness` once per test. There is no per-test server boot.
- **Phases recorded per row:** slot wait, build, host start, and per test the clone, act and
  assert.
- **Budget:** 39 new results (amendment 2 adds `precheck-revoked`, `in-doubt` and `consume-throws`):

  | Slice | New results |
  |---|---|
  | S3a | 2 |
  | S3b | 18: V-S3-2 2, V-S3-3 3, V-S3-4 3, V-S3-4b 3, V-S3-5 1, V-S3-5b 3, V-S3-5c 2, V-S3-11 1 |
  | S3c | 6: V-S3-6 1, V-S3-6b 2, V-S3-6c 1, V-S3-7 1, V-S3-11 1 |
  | S3d | 9: V-S3-8 2, V-S3-8b 3, V-S3-9 2, V-S3-10 1, V-S3-11 1 |
  | S3e | 1 |
  | S3f | 3 |

  The estimated p95 is 12 s per method, so the method timeout and the in-test lock deadline are
  30 s (about 2x, provisional until Code records p95 from TRX). V-S3-5 drives three re-grants on
  `FakeTimeProvider`, with no wall time.
- **Run estimates:** one checkpoint run per After group, and each run stays under 30 min.

  | Run | Rows | Estimate |
  |---|---|---|
  | S3a | CP-1..CP-3 | about 29 min |
  | S3b | CP-4..CP-6 | about 27 min |
  | S3c | CP-7..CP-9 | about 22 min |
  | S3e | CP-10..CP-12 | about 23 min |
  | S3f | CP-13..CP-14 | about 20 min |

  A row over 2x its estimate is a Debug item, never a timeout raise. Code records the CP-5 p95
  for the next plan.

### Checkpoints

Lane: Linux, server2. `GET /api/runner-defaults` returns `globalRunnerId=server2` (revision 2),
and `GET /api/session-runners` lists `desktop` (windows), `server2` and `server2-temp` (linux).
There is no `-Runner` or `-Platform` pin. Each After group builds once with
`scripts/build-slot.ps1` (`UseAppHost=false`, `--property:OutputPath=bin-c1593s3<x>/`), then runs
`dotnet exec --no-build` on the built DLL for each row. The S1 D-11 checkpoint reader cap still
applies, so Code reports CHECKPOINT-shaped lines with the TRX path and SHA-256. *Re-checked in amendment 2 (review A-9):* the cap is still 67,108,864 bytes at `aa6dfd24a` (`scripts/lib/checkpoint-file-reader.ps1:196`), so there are still no sealed receipts. Per S1 D-11, the Code Review re-runs CP-1..CP-6 itself, saves its TRX under `.antiphon/`, and reports `reviewedSourceClean: false` with the reason "reader cap: sealed receipts unavailable", never `true`. Raising the cap is a separate card.

Each round's Code task runs only its own rows: round A runs CP-1..CP-6, round B runs CP-7..CP-12
and round C runs CP-13..CP-14. If the tool cannot select rows, extract that round's rows into an
ignored `.antiphon/` plan copy. The rc port of round A re-runs CP-1..CP-6 on the rc tip.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S3a | `tests/Antiphon.Tests -> bin-c1593s3a/` | c1593-s3a | `/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)\|(CapacityLockOrderTests*)/*` | V-S3-1, S1 regression | 26 + 16 existing + 2 new = 44, 0 failed/skipped | 44 | 15 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S3a | CP-1 | c1593-s3a | `/*/Antiphon.Tests.Application/(WallRerouteDispatchTests*)\|(ReceiptFailureDeliveryTests*)/*` | S1 claim regression (state check widened) | 15 + 21 = 36, 0 failed/skipped | 36 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S3a | CP-1 | c1593-s3a | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` | S1 regression | 97, 0 failed/skipped | 97 | 8 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-4 | S3b | `tests/Antiphon.Tests -> bin-c1593s3b/` | c1593-s3b | `/*/Antiphon.Tests.Application/CapacityRecoverySupervisionTests/*` | V-S3-2..V-S3-5c, D8, V16-V18 | 6 existing + 17 new = 23, 0 failed/skipped | 23 | 13 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-5 | S3b | CP-4 | c1593-s3b | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(AgentSupervisionTests*)/*` | every standing start (review D-5) | 52 + 10 = 62, 0 failed/skipped | 62 | 12 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-6 | S3b | CP-4 | c1593-s3b | `/*/Antiphon.Tests.Application/CapacityLockOrderTests/C1593_S3_path_blocked_on_row_holds_no_provider_lock` | V-S3-11 `supervisor-start` (L16') | 1, 0 failed/skipped | 1 | 2 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-7 | S3c | `tests/Antiphon.Tests -> bin-c1593s3c/` | c1593-s3c | `/*/Antiphon.Tests.Application/CapacityRecoveryTaskTests/*` | V-S3-6..V-S3-7, V19 | 6 existing + 5 new = 11, 0 failed/skipped | 11 | 12 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-8 | S3c | CP-7 | c1593-s3c | `/*/Antiphon.Tests.Application/CapacityLockOrderTests/C1593_S3_path_blocked_on_row_holds_no_provider_lock` | V-S3-11 `supervisor-start`, `retained-return` | 2, 0 failed/skipped | 2 | 2 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-9 | S3c | CP-7 | c1593-s3c | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` | dispatcher regression | 97, 0 failed/skipped | 97 | 8 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-10 | S3e | `tests/Antiphon.Tests -> bin-c1593s3e/` | c1593-s3e | `/*/Antiphon.Tests.Application/CapacityRecoveryQueueTests/*` | V-S3-8..V-S3-10, V12-V15 | 8 existing + 8 new = 16, 0 failed/skipped | 16 | 12 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-11 | S3e | CP-10 | c1593-s3e | `/*/Antiphon.Tests.Application/(WallRerouteDispatchTests*)\|(ReceiptFailureDeliveryTests*)/*` | delivery regression | 36, 0 failed/skipped | 36 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-12 | S3e | CP-10 | c1593-s3e | `/*/Antiphon.Tests.Application/CapacityLockOrderTests/*` | V-S3-11 (3 args), V-S3-12, S1 regression | 16 existing + 3 + 1 = 20, 0 failed/skipped | 20 | 5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-13 | S3f | `tests/Antiphon.Tests -> bin-c1593s3f/` | c1593-s3f | `/*/Antiphon.Tests.Application/CapacityAdmissionLifecycleTests/*` | V-S3-13, V-S3-1, S1 regression | 26 + 2 + 3 = 31, 0 failed/skipped | 31 | 12 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-14 | S3f | CP-13 | c1593-s3f | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` | dispatcher regression | 97, 0 failed/skipped | 97 | 8 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

**Suffix check, at the base and identical on master.**
- These patterns each match exactly one class: `CapacityAdmissionLifecycleTests*` (3 partial
  files, one class), `CapacityLockOrderTests*`, `WallRerouteDispatchTests*`,
  `ReceiptFailureDeliveryTests*`, `StandingRestartEscalationTests*` (9 partials, one class) and
  `AgentSupervisionTests*`. None of these names is a prefix of another class, and
  `CapacityRecoverySupervisionTests` does not start with `AgentSupervisionTests`.
- CP-3, CP-4, CP-7, CP-9, CP-10, CP-12, CP-13 and CP-14 use an exact class segment.
- CP-6 and CP-8 use an exact method segment, which runs every `[Arguments]` case present at that
  slice.
- Code re-checks the counts in the fresh TRX. The existing counts are re-verified at this base:
  Lifecycle 26, LockOrder 16, WallReroute 15, ReceiptFailure 21, base guard 97, Supervision 6,
  Task 6, Queue 8, StandingRestartEscalation 52 and AgentSupervision 10.

Not run:
- the whole Unit lane;
- native Windows (a live rc qualification owns the desktop);
- checkpoint tool sealed receipts (reader cap, S1 D-11);
- `CapacityRecoverySupervisionTests`/`StandingRestartEscalationTests` in rounds B and C (S3c, S3d
  and S3f do not touch the supervisor or `AgentControlService`).

### Landing

- **Round A:** cut from `origin/master`. Before Review: `git fetch`, then
  `git merge-tree --write-tree origin/master HEAD`. Run
  `scripts/check-evidence-diff.ps1 -Repository . -BaseRef <master base> -HeadRef <tip>`.
  - After a clean master Review and land, and only if the orchestrator decides the current rc
    needs it, port to rc. The port cherry-picks the S3a and S3b product and test commits onto
    `origin/rc/<current>`, without this plan doc. Prove each pick with a chained
    `merge-tree --merge-base`, re-run CP-1..CP-6 on the rc tip, then take a separate Review.
- **Rounds B and C:** cut from `origin/master` after the previous round lands. They are
  master-only, with the same merge-tree and evidence-diff checks.

## Risks

1. **Every standing capacity restart now runs through `StartInteractiveSessionAsync`'s consume.**
   A bug there stops standing restarts that have a wait. Mitigation:
   - D8, C510_S2 and V16-V18 run unchanged;
   - CP-5 runs the whole `StandingRestartEscalationTests` and `AgentSupervisionTests` classes;
   - S-2 and S-2h release to the ungated path rather than stranding.
2. **S-2 classification is a code list.** A new durable conflict code that is not listed falls
   into S-3 (charged, bounded by `MaxAttempts`), the base outcome. Safe direction.
3. **The card arm under-spends in a crash window or on a failed consume** (D-S3-2, S-0c). One unpaced launch per failure, never a strand, never a second spawn: the pointer is saved at `:326` before the consume (amendment 2).
4. **Retained/queue overlap.** A retained task's wall wait can also be the wait a queue row is
   stamped with (`HasTerminalCapacityHoldAsync` on the same session). After S3 whichever consumer
   commits first spends. The other refuses with `grant-mismatch` and holds, as base `RedeemAsync`
   did. There is no double spend, because both consume under the same provider lock.
5. **Revalidation is now consumable in the claim** (D-S3-1). This also opens the dispatcher's
   pre-claim gate (`AgentTaskDispatcher.cs:1764-1776`) to `Admitted` revalidation task waits.
   Legacy leaked dispatch rows are one source, and a retained task that is later requeued with
   its `Admitted` wait is another (review A-5). Consuming them without an increment is
   `RedeemAsync`'s base semantics.
6. **The leak 6 renewal delays other waiters** up to the cap. It is master only, the setting
   defaults to **0 (off)**, and it is tuned from the server2 preparation p95 before anyone turns
   it on.
7. **Non-delivering pre-`Sent` exits retry under a held grant** until D-12. A persistently
   failing pre-`Sent` exit (spill hold) holds the provider up to 120 s per cycle with no attempt
   cap. The queue's own withhold counters bound the message. The plan records this as a residual,
   and Code reports the withhold path that ends it.
8. **Residual (review A-2, pre-existing).** A message selected after the spend can be canceled
   by an expired brief (`:2753`, `:2758-2761`). Its wait then re-arms as revalidation and is
   regranted on every expiry with no consumer. This needs a follow-up card that finishes the
   wait when its selected message is canceled. The orchestrator files it.
9. **Residual (review A-6, pre-existing).** An S-0 skip for an agent that a human started fresh
   cycles the grant every expiry until that agent dies (progress keys on `wait.SessionId`, and
   `OrphanedWaits` never cancels standing waits). This needs a follow-up card.
10. **S-4 backstop costs one attempt.** When the `FOR UPDATE` reread itself fails and the commit
    did apply, pass 1 ends the never-enqueued `Starting` row and the `Admitted` wait re-arms.
    That is one wasted attempt, bounded by `MaxAttempts`, and the supervisor restarts the agent.
11. **S-2h releases pacing.** When a hold clears, the restart is ungated, as after any S-2
    release. Accepted: holding the grant through a hold of unbounded length would starve every
    other waiter of that provider kind.
12. **The Enter-only consume spends first (bound restated in amendment 2, review A-6).** A crash
    between the consume commit and the Enter keystroke spends one admission with no turn. The
    reverse order would be the leak. Spending before Enter is base-equivalent, because base also
    spends at `:2586` before Enter-only.
    - Only the first consume of a wall episode increments `AdmissionCount`. After it the wait is
      `Admitted` with `SelectedMessageId`, so every later redelivery is a revalidation that never
      increments, and Enter-only never charges `DeliveryAttempts` (`:3380-3477`).
    - "Bounded by `MaxAttempts`" was therefore wrong. The count over-spends by at most one per
      wall episode. The wall-clock cost has no attempt bound: a repeating Enter-failure cycle
      regrants once per `StalledAdmissionTimeout` (120 s), each grant paced by the provider
      clock, until the head leaves `Pending` (confirmed, canceled, expired, or settled by a
      person) or the session's waits end.
    - That is exactly base (round-1 A-3). A follow-up card can bound it with a per-message
      Enter-only counter; S3 does not change it.
13. **A raced `standing_automatic_fresh_ineligible` releases pacing (amendment 2).** The code
    has two sites:
    - durable eligibility at `AgentControlService.cs:178-180` (not AlwaysOn, a pool delegate, a
      specialist);
    - a racy recheck at `:595-597` (`CanReserve` turned false, or a compaction block appeared
      after the supervisor's checks).

    S-2 releases both. The racy site therefore drops pacing on a rare race, and the ungated path
    then applies its own `CanReserve` and compaction gates. Splitting the code so that the racy
    site is S-1 is a follow-up, not S3: it changes an existing conflict code that
    `StandingRestartEscalationTests` pins.
14. **The card arm's failed consume leaves the wait cycling (amendment 2, S-0c).** The wait has no
    `SessionId` stamp, so the live card session does not progress it. The grant cycles at D-12
    until that session dies. This is the same residual shape as Risk 9 and is bounded by the
    session's life.

## Amendment log (review 0b34dc40)

| Review item | Disposition | Where |
|---|---|---|
| D-1 retained strand | fixed: no consume when there is no wait or the feature is off; recheck the task flag only; keep the cheap grant pre-read | D-S3-3, V-S3-6b, V-S3-6c, L6' |
| D-2 Enter-only without spend | fixed: own-scope consume immediately before the Enter keystroke for `Pending` heads, including pointer recovery; interrupted-`Sent` and human retry pass null | Ground truth rows 8-9, D-S3-4, L17e, V-S3-8b |
| D-3 S-4 lost ack | fixed: fresh-scope reread under `FOR UPDATE` on the agent row; if committed, enqueue; if not, rethrow to S-T/S-3; the reread failing has a named backstop | D-S3-5 S-4 procedure, Ground truth row 11, V-S3-5b, Risk 10 |
| D-4 exit table | fixed: (a) S-2h release; (b) catch beside `:232` before `:236`; (c) S-T no charge, no backoff; (d) S-C; (e) tracker clear | D-S3-5, V-S3-3, V-S3-4b |
| D-5 coverage | fixed: CP-5 runs both whole classes (52 + 10; the review's 51 re-counted as 52); new witnesses for the card arm (V-S3-5c), S-4 (V-S3-5b), `capacity_recovery_suspended` (V-S3-3) and the remote retained return (V-S3-6c); V-S3-11 asserts nothing after the consume | Checkpoints, Verification |
| A-1 | recorded: the Rule P assertion is vacuous after a save, so V-S3-11 now asserts no command after the consume; the L17' re-key note; L6' serializes with L1 | D-S3-7, V-S3-11 |
| A-2 | residual | D-S3-4, Risk 8 |
| A-3 | accepted as stated (there is no double spend; the bound is the queue's `DeliveryAttempts`/withhold) | D-S3-4 post-`Sent` |
| A-4 | S3f defaults to 0, master only, never ported in this card; enforcement notes | D-S3-6, D-S3-8 |
| A-5 | Risk 5 widened | Risks |
| A-6 | S-0 bound text corrected; residual | D-S3-5, Risk 9 |
| A-7 | V-S3-12 rides round B | D-S3-1, D-S3-8, Slices |
| Slicing | adopted: A = S3a+S3b on master, then rc after a clean master Review; B = S3c+S3d+S3e master-only; C = S3f master-only, default 0 | D-S3-8 |

## Amendment log (review 8f6e5b17)

| Review item | Disposition | Where |
|---|---|---|
| D-1 pre-check grant race | fixed. A grant mismatch at the pre-check throws `capacity_consume_refused` (S-1). `standing_automatic_fresh_capacity_required` is kept only for no wait and a kind mismatch. `:610` and `:611-612` are deleted. *Differs from the review:* an `ActionKey` mismatch is S-1 too, because only a reconciler re-arm moves the key. New witness `precheck-revoked` | Ground truth (amendment 2), D-S3-2 pre-check, D-S3-5 S-1 row, V-S3-4 |
| D-2 S-4 `FOR UPDATE` unwitnessed | fixed: V-S3-5b `in-doubt`, whose PC drops `FOR UPDATE`. *Differs from the review:* the test transaction queues on the agent row before the reservation rolls back, and writes after it. Taking the row at `TransactionCommittingAsync` would block the interceptor's own thread forever | V-S3-5b, red witnesses, PCs |
| D-3 card-arm placement and throw | fixed: the consume runs after the `:326` pointer save and before `:328`; every non-cancellation exception is caught, logged, and the launch stands (new class S-0c); witness `consume-throws` | D-S3-2 card arm, D-S3-5 S-0c, L16c, V-S3-5c, Risks 3 and 14 |
| D-4 re-key under rank 7 against the `Sent` transaction | decision (Round B): option (a). `PrepareNextAttemptOnAsync` becomes capacity-only, and an own-scope, lock-free `RealignQueueKeysAsync` runs after each provider commit and at the start of `ReconcileAsync`. The independent plan's invariant is adopted, but not its consumer-side repair (unique index, transferred rows, scope). Option (b) is rejected. L17' and the S1 L18 note are corrected | D-S3-10, L17', Slices S3d |
| A-1 | S-2h added to the tracker rule; the no-re-grant-loop reasoning recorded | D-S3-5 tracker rule, S-2h row |
| A-2 | release call sites named (inside `StartSupervisedRestartAsync` after `held`, the outer S-2 and S-2h catches); a `standing_resume_*` rethrown at `:569` is S-3 (base); outer S-2 list made exact (`standing_start_intent_revoked` added) | D-S3-5 catch site, release call sites |
| A-3 | `ModelDisabledException` named in the S-3 row | D-S3-5 S-3 row |
| A-4 | V-S3-11 `supervisor-start` holds the involved `AgentSessions` row; its PC moves the consume ahead of the session locks | V-S3-11 |
| A-5 | cross-process note added | L16' |
| A-6 | "bounded by `MaxAttempts`" corrected for Enter-only: at most one count per wall episode, and no attempt bound on the 120 s regrant cycle (as base) | D-S3-4, Risk 12 |
| A-7 | S-T: a `55P03` can repeat while a foreign holder persists | D-S3-5 S-T row |
| A-8 | lines corrected: `:236` -> `:240`, `:531` -> `:535`, `:176` -> `:177`, `:886` -> `:887`, `:806-816` -> `:808-816`, `:510-522` -> `:512-522`, `:599-611` -> `:602-615`, `:291-299` -> `:302-307`, `:319` -> `:328`, `:556-559` -> `:557-561`. *Not taken:* `AgentSupervisorService` `:127` -> `:126`. Line 127 is the `BeginTransactionAsync`; line 126 is its comment | throughout |
| A-9 | the reader cap is re-checked and still applies; the Code Review reports `reviewedSourceClean: false` with the S1 D-11 reason | Checkpoints |
| A-10 | deferred to the Round C amendment | D-S3-8 Round C |
