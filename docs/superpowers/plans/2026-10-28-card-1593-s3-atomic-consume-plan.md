# CARD-1593 S3: atomic consume for supervisor start, retained return and queue flush

Task 62fb542c, Plan stage. Base `origin/rc/2026-10-27` = `aa6dfd24a51db7c02e8722a05a21817bf4df407a`.
All line numbers are at that base unless marked otherwise. No product code changed in this task.

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
| 2 | `AgentSupervisorService.TryHandleCapacityWaitAsync` `:507-508` | before the start | duplicate-key exit `:512-516`, compaction block `:523-524`, `StartSupervisedRestartAsync` false `:525-526`/`:571`, start throws (`:236-245`) |
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
| The supervisor start has one commit to join | **Partly.** The interactive path `AgentControlService.StartInteractiveSessionAsync` has one reservation transaction (`AgentControlService.cs:543`) with two commit points: resume arm `AcceptAsync` `:686` -> `CommitAsync` `:687`, new-row arm `AcceptAsync` `:796` -> `CommitAsync` `:797`. `AcceptAsync` (`:802-887`) ends in `SaveChangesAsync` `:886`. The card arm (`_cardService.SpawnAsync`, `:291-299`) commits in `CardService`, not here. Early returns that start nothing: launch-owned `:198-199`, already live `:225-230`, reservation-live `:556-559`. |
| The supervised-fresh reservation already checks the capacity wait | **Yes, against the spent state.** `:599-611` require `wait.State == Admitted && OutcomeReason == Start`, and `AcceptAsync` moves the wait to the new session with an `ExecuteUpdate` filtered on `State == Admitted` (`:806-816`). Both encode "redeemed before start" and must change with the consume point. |
| Progress for a standing wait is keyed on the receipt | **No.** `OnTranscriptAsync` keys on `wait.SessionId` (`CapacityRecoveryService.cs:883-889`); `SessionId` is not in `HasExecutionReceipt` (`CapacityRecoveryPolicy.cs:92-96`). Moving `SessionId` does not change re-arm behaviour. |
| Retained return can share the dispatcher context's transaction | **Risky.** The retained loop runs on the tick's `_db` (`AgentTaskDispatcher.cs:602-652`), which tracks every queued row of the tick; a transaction there would flush unrelated tracked edits under the provider lock (Rule P). `TryClaimCountedSlotAsync` (`CapacityRecoveryService.cs:845-863`) takes `pg_advisory_xact_lock` with no explicit transaction, so the lock ends with the statement; its only production caller is `:637`. |
| The queue consume can go at "delivered" | **No (see D-S3-4).** Delivery confirmation (`DeliverAsync`, `SessionMessageQueueService.cs:2760-2763`) can outlast the 120 s grant; a revoke mid-delivery re-keys the message (`PrepareNextAttemptOnAsync` `:1401-1407`) and the consume would refuse after the provider was used. The queue already charges its own attempt at the `Sent` stamp and "deliberately" never refunds it (`:2712-2714`). |
| A pre-`Sent` flush exit is rare | **No.** Between the redeem (`:2586`) and the `Sent` save there are: quota-suppressed recheck `:2707-2710`, expired briefs `:2717`, spill-hold/restaging exits (`:2644-2700`), enter-only confirm `:2700-2701`. Each returns without delivery after the spend. |
| `HeldForRemotePrep` can be observed later | **Yes.** `RemoteHoldForAsync` (`AgentTaskDispatcher.cs:1238-1250`) returns a hold while `_remotePrep.IsInFlight(task.Id, out since)` (`:1245`), before the gate (`:949-959`) and with no transaction open. `since` is the preparation start. |
| Grant renewal exists | **No.** `GrantedAt` is written only by `GrantReadyAsync` (`:212-215`) and cleared by `ClearGrant` (`:1224-1230`). |

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

After S3 `RedeemAsync` has no production caller. It stays (service-level tests in
`CapacityRecoveryAcceptanceTests` call it 27 times) with an XML note "test and compatibility
only"; S3e adds a source-scan guard that `server/` has no `.RedeemAsync(` call site.

**D-S3-2 Supervisor (leak 2): consume inside the start reservation.**
- *Consume point:* `AgentControlService.StartInteractiveSessionAsync`, between `await AcceptAsync(...)`
  and `reservation.CommitAsync` on both arms (`:686/:687`, `:796/:797`):
  `RedeemOnAsync(_db, wait.Id, wait.ActionKey, wait.ExecutionKind, Start, ct, consume: true, receipt: Admitted, stampSessionId: accepted.Id, stampSessionStartedAt: accepted.StartedAt)`.
  `AcceptAsync`'s save (`:886`) leaves the tracker clean, so Rule P's assertion
  (`RejectDirtyLaunchRows`) holds. The session row (`:774/:775` or the resume edits `:658-685`),
  `agent.PersistentSessionId`, `intent.AutomaticFreshConsumed`, the session and intent
  `CapacityRecoveryActionKey` and the spend commit together or not at all.
- *Plumbing:* a new internal record `CapacityStartAdmission(Guid WaitId, string ActionKey, AgentKind Kind)`.
  The supervisor calls new `internal AgentControlService.StartForCapacityAsync(agentId, request, admission, ct)`
  (public `StartAsync` signature unchanged) and passes it in `SupervisedFreshReservation` for the
  fresh arm. `StartInteractiveSessionAsync` gets it as a parameter.
- *Key stamps move into the reservation:* the supervisor's `session.CapacityRecoveryActionKey = ...`
  and `state.CapacityRecoveryActionKey = ...` plus the save (`AgentSupervisorService.cs:510-522`)
  are deleted. The new-row arm already sets the session key (`AgentControlService.cs:728`, extend
  to `admission?.ActionKey`); the resume arm sets `previous.CapacityRecoveryActionKey`; `AcceptAsync`
  sets `intent.CapacityRecoveryActionKey`.
- *Pre-check:* `:599-611` becomes a read-only "grant still matches" check (`IsGrantCandidate(wait)`
  and the provider row's `GrantedWaitId/GrantedActionKey`), code unchanged
  (`standing_automatic_fresh_capacity_required`). The authoritative check is the consume under
  the provider lock.
- *Supervisor order:* `TryHandleCapacityWaitAsync` keeps the grant-match read (`:500-505`), then
  runs the duplicate-key check (`:510-516`, read-only now) and `BlocksAutomaticRestartAsync`
  (`:523`) **before** calling the start. No `RedeemAsync`.
- *Card arm:* a capacity-recovery start whose agent has pending card work goes through
  `CardService.SpawnAsync` (`:291-299`), outside this reservation. S3 consumes in a separate
  7->8 leaf after `SpawnAsync` returns (own scope, `RedeemOnAsync(..., Admitted)` + commit). The
  crash window there under-spends (one unpaced launch), never strands.
  *Rejected:* threading the consume into `CardService.SpawnAsync` (another service's transaction,
  a larger blast radius than this slice). *Rejected:* refusing a capacity start with card work
  (base starts it; a behaviour change outside the leak).

**D-S3-3 Retained return (leak 3): one transaction for slot, task flag and consume.** New
`CapacityRecoveryService.ReturnRetainedAsync(Guid taskId, Guid waitId, string actionKey, AgentKind kind, int? localLimit, CancellationToken ct)`
on its own scope:
1. `BEGIN`; if `localLimit` is set: counted advisory (rank 1) and the count from
   `TryClaimCountedSlotAsync` (`:851-861`) inside this transaction; over the limit -> rollback, `false`.
2. `AgentTasks` row `FOR UPDATE` (rank 3); recheck `Working && CapacityWaitRetained && CapacityWaitId == waitId`;
   lost -> rollback, `false`.
3. `CapacityWaitRetained = false`; `SaveChangesAsync`.
4. `RedeemOnAsync(consume: true, receipt: Admitted, path: Dispatch)` (ranks 7, 8). Refusal -> rollback, `false`.
5. `COMMIT`, `true`.

The tick (`AgentTaskDispatcher.cs:631-652`) replaces `TryRedeemCapacityWaitAsync(..., consume:true)`
and `TryClaimCountedSlotAsync` with one call. Its local `active` pre-filter (`:613-614`) and the
remote occupancy pre-filter (`:617-630`) stay as cheap skips. On `true` the tick sets the
tracked `retained.CapacityWaitRetained = false` and marks that property unmodified (the row is
already committed), so `:651-652` does not write it again. `TryRedeemCapacityWaitAsync`'s
`consume:true` branch (`:1779-1782`) is deleted; the helper keeps only the gate.
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
- *What changes for the brief's "retries about every 120 s":* only pre-`Sent` exits stop spending
  and stop waiting for re-arm; they retry on the next flush under the still-held grant, bounded
  by D-12.
- *`ApplyCapacityHoldAsync` stamp (`:5561-5573`) stays before the consume.* It is a selection, not
  a spend, and `ActionPending` waits are not re-armed by `RearmStalledAdmissionsAsync`.
- *Rejected:* consume at the delivered verdict (Ground truth row 7). *Rejected:* refunding the
  admission on post-`Sent` failure (S1 rejected redeem-then-compensate; a crash between leaks).
- `BackendUnreachable` refunds the queue attempt (`:2819-2829`) but keeps the admission. Accepted:
  revalidation does not increment, so it costs one re-arm delay, not an attempt.

**D-S3-5 Supervisor failure contract (S1 D-9 applied to the start).** Every exit after the grant-match read:

| Class | Exits | Outcome | Bound |
|---|---|---|---|
| S-0 pre-start skip | duplicate action key, `BlocksAutomaticRestartAsync`, launch-owned `:198`, already live `:225`/`:556` | no spend, no write; grant left reserved | D-12 revokes after 120 s, no increment; each condition is temporary (live agent progresses the wait by report boundary; compaction by CARD-0079) |
| S-1 consume refusal | grant revoked or moved between the read and the consume (`grant-mismatch`, `not-action-pending`, `clock-not-due`, `feature-disabled`) | `RedeemOnAsync` throws `ConflictException("capacity_consume_refused")` inside the reservation; the reservation rolls back (no session, no pointer move); the supervisor catches that code and returns `false` without backoff | the reconciler owns the wait |
| S-2 durable start refusal | `standing_resume_*` conflicts handled by `HoldRefusedFreshAsync` (`AgentSupervisorService.cs:550-572`), `capacity_recovery_*` (`AgentControlService.cs:201-222`), `standing_automatic_fresh_*`, `specialist_start_refused` | reservation rolled back or never opened; then `ReleaseAgentReservationAsync(agentId, kind, "start-refused")` (new, 7->8 leaf on its own scope: cancel the unfinished `ActionPending`/revalidation wait for that agent, clear its grant; `AdmissionCount` unchanged) | the next tick has no unfinished wait and takes the ungated path with its own holds (continuity, suspend, latch). This is the base outcome after `MaxAttempts`, without the three wasted cycles |
| S-3 start throws (infrastructure, model, runner) | `RecordStartFailureInFreshScopeAsync` path (`:236-245`) | reservation rolled back; backoff recorded as today; then `ChargeFailedStartAsync(waitId, actionKey, kind)` (new, 7->8 leaf): consume in `Admitted` mode if the grant still matches. A failed launch is a real attempt | `MaxAttempts` -> `Exhausted` -> ungated restart, as base |
| S-4 commit-ack lost | `reservation.CommitAsync` throws | reread the agent pointer and the wait on a fresh scope: committed -> treat as success; not committed -> S-3 | as S-3 |

*Rejected:* leaving S-3 at "no spend". Repeated infrastructure failures would then cycle the
grant forever with no attempt cap, which is leak 1's unbounded shape.
*Rejected:* cancelling on S-3. It drops pacing while the wall may still be on.

**D-S3-6 `HeldForRemotePrep` (leak 6): renew the grant while the preparation is observed in flight.**
New `CapacityRecoveryService.RenewGrantAsync(Guid waitId, string actionKey, AgentKind kind, DateTime preparationStartedAt, CancellationToken ct)`,
own scope, 7->8 leaf: under the provider lock, if `GrantedWaitId/GrantedActionKey` still match
and `now - preparationStartedAt < CapacityRecovery:RemotePrepGrantHoldSeconds` (new setting,
default 600, 0 disables), set `GrantedAt = now`. Called from the `RemoteHoldForAsync` branch
(`AgentTaskDispatcher.cs:949-959`) only when the hold is "preparation in flight" (`:1245`) and the
task has an unfinished wait. No task lock is held there (Rule X).
- *Bound:* the preparer's own timeout ends `IsInFlight`; the setting caps renewals; a dead server
  stops renewing and D-12 revokes 120 s later. `AdmissionCount` is untouched (S1 D-3 I4 holds).
- *Trade-off:* other waiters of that provider wait during the preparation. S1 D-10 chose the
  opposite; this slice reverses it only within the cap.
- *Rejected:* advancing `GrantedAt` once at the `HeldForRemotePrep` commit by a fixed budget. It
  would be a provider write inside the claim (Rule P needs a clean tracker there) and survives a
  preparer that died.
- *Ship:* master only until the server2 preparation p95 (S1 D-10) is measured; D-S3-8.

**D-S3-7 Lock order.** No rank changes. S1 D-1's table, Rule P and Rule X are the contract.
S3 rows (they replace S1 L6, L16, L17 and add L26):

| # | Path | Site(s) after S3 | Sequence | Conformance |
|---|---|---|---|---|
| L16' | Supervisor start, interactive | `AgentSupervisorService.cs:127` tx, agent (5), commit `:226-227`; `AgentControlService.cs:176` `LockAgentAsync` (autocommit); reservation `:543`: sessions `FOR UPDATE` (4) `:546-547` -> agent (5) `:548` -> supervision state, pending queue rows, session insert/edits, incidents -> save `:886` -> **7** consume -> **8** -> commit | 4 -> 5 -> (4: pending queue rows, pre-existing) -> 7 -> 8 | Rule P: tracker clean at 7 (asserted). Rule X: no row lock held while waiting on 7 from another connection; 7 is taken on the reservation's own connection after its rows. The pre-existing 5 -> 4 on pending queue rows takes no rank 6/7 and is detectable (n/c) |
| L16c | Supervisor start, card arm | `CardService.SpawnAsync` own tx; then own-scope 7 -> 8 consume | n/c, then leaf | conforms |
| L16f | Supervisor S-2 release / S-3 charge | own scope, 7 -> 8 | leaf | conforms |
| L6' | Retained return | `ReturnRetainedAsync`: counted (1) -> task `FOR UPDATE` (3) -> save -> **7** -> **8** -> commit | 1 -> 3 -> 7 -> 8 | conforms; the counted lock now spans the transaction |
| L17' | Queue flush | gate `RedeemGateAsync` (own scope leaf); `Sent` tx: queue rows (4, under the in-process session gate) -> `FreezeCompletionRenderingAsync` rows -> save -> **7** -> **8** -> commit | 4 -> 7 -> 8 | conforms; `RejectDirtyLaunchRows` passes (no task/agent/session dirty) |
| L26 | Remote-prep renewal | `RemoteHoldForAsync` branch -> `RenewGrantAsync` own scope | 7 -> 8 leaf | conforms |

**D-S3-8 Shipping.** S3a (primitive), S3b (supervisor), S3c (retained), S3d (queue) and S3e
(guards) ship to `rc/2026-10-27` first, then master as the same patch. rc carries S1 and these are
the same card's leaks. S3f (remote-prep renewal) ships to master only; the rc port waits for the
measured p95 and a separate decision. The rc pick drops this plan doc (rc carries no
`docs/superpowers/plans`).

**D-S3-9 Next stage.** The brief names `next: review` of this plan. The plan carries its own
`## Verification design` and `### Checkpoints`, so Code can follow an approving Review directly.

## Slices

| Slice | Files | Change | Tests (red first) |
|---|---|---|---|
| S3a | `server/Application/Services/CapacityRecoveryService.cs`, new `server/Application/Services/CapacityConsumeReceipt.cs` | D-S3-1: `receipt` parameter, `Admitted` mode, revalidation accepted, session stamp; `RedeemAsync` note | V-S3-1 |
| S3b | `AgentSupervisorService.cs` (`:230-245`, `:470-527`, catch list `:547-551`), `AgentControlService.cs` (`:150-160`, `:162`, `:291-317`, `:543-887`), `CapacityRecoveryService.cs` (`ReleaseAgentReservationAsync`, `ChargeFailedStartAsync`) | D-S3-2, D-S3-5 | V-S3-2..V-S3-5 |
| S3c | `AgentTaskDispatcher.cs` (`:631-652`, `:1764-1783`), `CapacityRecoveryService.cs` (`ReturnRetainedAsync`) | D-S3-3 | V-S3-6, V-S3-7 |
| S3d | `SessionMessageQueueService.cs` (`:2577-2590`, `:2737-2749`) | D-S3-4 | V-S3-8..V-S3-10 |
| S3e | `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` | Rule P witnesses for L16', L6', L17'; no-production-caller guard for `RedeemAsync` | V-S3-11, V-S3-12 |
| S3f (master) | `AgentTaskDispatcher.cs` (`:949-959`), `CapacityRecoveryService.cs` (`RenewGrantAsync`), `server/Application/Settings/SupervisionSettings.cs` (`CapacityRecoverySettings.RemotePrepGrantHoldSeconds`) | D-S3-6 | V-S3-13 |

Each slice commits its red tests first (git-common), then the fix.

## Verification design

"Red at base" means red on the slice's test commit before its product commit, with the named
assertion; a build or fixture error is not red. A guard passes at base and names a method-scoped
positive control (PC) that must turn it red.

| ID | Test | Asserts | Base |
|---|---|---|---|
| V-S3-1 | `CapacityAdmissionLifecycleTests/C1593_S3_revalidation_grant_is_consumable_in_a_transaction` `[Arguments("launch")]`, `("admitted")` | a revalidation grant (`Admitted`, `NeedsRevalidationGrant`, `SelectedMessageId` set) consumed inside a caller transaction is accepted without increment; `admitted` additionally asserts the row equals what `RedeemAsync` writes (state, reason, clock, grant cleared, no new receipt field) and that a rollback leaves the grant. The test commit calls the base overload (`consume: true`, `launchSessionId`) for both arguments; the fix commit switches the `admitted` argument to `receipt: Admitted` | red at base, both arguments: `not-action-pending` (`CapacityRecoveryService.cs:343-344`) |
| V-S3-2 | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_skip_does_not_spend` `[Arguments("compaction-block")]`, `("duplicate-key")` | granted standing wait; the skip condition holds; after the tick `AdmissionCount == 0`, wait `ActionPending`, grant still the wait, no start | red at base: `AdmissionCount == 1`, grant null (`RedeemAsync` `:507` ran first) |
| V-S3-3 | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_refusal_releases_the_wait` | `StartSupervisedFreshAsync` refused with `standing_resume_delivery_pending` (S-2): wait `Canceled`/`start-refused`, grant cleared, `AdmissionCount == 0`, continuity held; the next tick reaches the ungated path (no capacity branch) | red at base: `AdmissionCount == 1`, wait `Admitted` |
| V-S3-4 | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_start_and_consume_commit_together` `[Arguments("commit-visibility")]`, `("consume-revoked")` | `commit-visibility`: a `DbTransactionInterceptor` on the harness context runs at `TransactionCommittingAsync` of the transaction that inserted the new `AgentSessions` row and, from a separate connection, reads the wait: still `ActionPending`, `AdmissionCount == 0`, grant held (the spend is not visible before the start commits); after the tick `AdmissionCount == 1`, `Admitted`, `SessionId == ` the new session. `consume-revoked`: a test `LandDeliveryBoundary` point `capacity-start-before-consume` revokes the grant on another connection: no new session row, pointer unchanged, `AutomaticFreshConsumed` false, `AdmissionCount == 0`, no backoff recorded | red at base: `commit-visibility` reads `Admitted`/`AdmissionCount == 1` inside the interceptor (base committed `RedeemAsync` at `AgentSupervisorService.cs:507-508` before the start); `consume-revoked`: the boundary is not on the base path, so base launches and the "no new session row" assertion fails |
| V-S3-5 | `CapacityRecoverySupervisionTests/C1593_S3_supervisor_start_throw_charges_one_attempt` | start throws infrastructure (S-3) three times across re-grants: `AdmissionCount` 1, 2, 3 then `Exhausted`, then the ungated restart runs | guard (base also spends once per failed start). PC: delete the `ChargeFailedStartAsync` call -> red (`AdmissionCount == 0` after the first failure) |
| V-S3-6 | `CapacityRecoveryTaskTests/C1593_retained_slot_filled_after_snapshot_does_not_spend` | local limit 1, snapshot count 0, `AfterQueuedSnapshotAsync` inserts one counted `Working` row; retained task holds the grant: `AdmissionCount == 0`, grant still held, `ActionPending`, `CapacityWaitRetained` still true | red at base: `AdmissionCount == 1`, `Admitted` (investigation item 3) |
| V-S3-7 | `CapacityRecoveryTaskTests/C1593_retained_return_commits_flag_and_admission_together` | interceptor throws `XX000` from `TransactionCommittingAsync` on the return transaction: `CapacityWaitRetained` true, `AdmissionCount == 0`, grant held; next tick (fault spent) returns once with `AdmissionCount == 1` | red at base: the spend commits before the flag (`AdmissionCount == 1`, flag true after tick 1) |
| V-S3-8 | `CapacityRecoveryQueueTests/C1593_flush_exit_before_sent_does_not_spend` `[Arguments("spill-hold")]`, `("quota-suppressed")` | stamped head, grant held; force the pre-`Sent` exit (`HoldSpillBodyForDeliveryAsync` false / a matching supervision quota suppression): `AdmissionCount == 0`, grant still the wait, message `Pending`, `DeliveryAttempts == 0` | red at base: `AdmissionCount == 1`, grant null |
| V-S3-9 | `CapacityRecoveryQueueTests/C1593_flush_sent_and_admission_commit_together` `[Arguments("commit-fault")]`, `("consume-revoked")` | `commit-fault`: interceptor throws `XX000` on the `Sent` commit: message `Pending`, nothing typed (fake runner write count 0), `AdmissionCount == 0`, grant held. `consume-revoked`: grant revoked at test boundary `capacity-queue-before-consume`: same outcome and `FlushResult.Nothing` | red at base: `commit-fault` has the spend committed before the stamp (`AdmissionCount == 1`); `consume-revoked`: the boundary is not on the base path, so base types the message (write count 1) |
| V-S3-10 | `CapacityRecoveryQueueTests/C1593_failed_delivery_after_sent_rearms_as_revalidation` | transport throw after `Sent`: `AdmissionCount == 1`, `Admitted`, `SelectedMessageId` set, message `Pending`; after 121 s reconcile `NeedsRevalidationGrant`; regrant; redelivery consumes without increment and the message is `Sent` | red at base only on the last step (base `RedeemAsync` accepts revalidation, so this is a **guard**). PC: drop the revalidation clause from the S3a state check -> the redelivery returns `Nothing` |
| V-S3-11 | `CapacityLockOrderTests/C1593_S3_path_blocked_on_row_holds_no_provider_lock` `[Arguments("supervisor-start")]`, `("retained-return")`, `("queue-sent")` | a test connection holds, respectively, the agent row, the retained task row, the head queue row `FOR UPDATE`; the path's backend waits on it (`pg_stat_activity`); `pg_locks` shows no `antiphon.capacity.kind.*` key for that pid; release, the path completes with one spend | guards at base (base spends before, on another connection). PCs: move each consume ahead of its row write (bypassing `RejectDirtyLaunchRows`) -> red on the capacity-key assertion |
| V-S3-12 | `CapacityLockOrderTests/C1593_S3_wrapper_has_no_production_caller` | source scan of `server/**/*.cs` finds no `.RedeemAsync(` | red at base: three call sites |
| V-S3-13 (master) | `CapacityAdmissionLifecycleTests/C1593_remote_prep_keeps_the_grant_while_in_flight` `[Arguments("in-flight")]`, `("past-cap")` | `in-flight`: after `HeldForRemotePrep`, advance 121 s with `IsInFlight` true, tick, then `GrantReadyAsync`: grant still the wait, `AdmissionCount == 0`. `past-cap`: setting 60 s, advance past it: the grant expires (`grant-expired`) | `in-flight` red at base (S1 V-4 c5a outcome). `past-cap` guard; PC: drop the cap check -> red |

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

Deterministic means no sleeps: `FakeTimeProvider`, `pg_stat_activity`/`pg_locks` polling with a
30 s deadline, and `LandDeliveryBoundary` points `capacity-start-before-consume` and
`capacity-queue-before-consume` (test-only, as S1's `capacity-consume-before`).

### Red witnesses (run before each product commit, not in the table)

One build-slot build of the test commit, then `--no-build`:
- `/*/*/CapacityAdmissionLifecycleTests/C1593_S3_revalidation_grant_is_consumable_in_a_transaction`
- `/*/*/CapacityRecoverySupervisionTests/(C1593_S3_supervisor_skip_does_not_spend*)|(C1593_S3_supervisor_refusal_releases_the_wait*)|(C1593_S3_supervisor_start_and_consume_commit_together*)`
- `/*/*/CapacityRecoveryTaskTests/(C1593_retained_slot_filled_after_snapshot_does_not_spend*)|(C1593_retained_return_commits_flag_and_admission_together*)`
- `/*/*/CapacityRecoveryQueueTests/(C1593_flush_exit_before_sent_does_not_spend*)|(C1593_flush_sent_and_admission_commit_together*)`
- `/*/*/CapacityLockOrderTests/C1593_S3_wrapper_has_no_production_caller`
- master S3f: `/*/*/CapacityAdmissionLifecycleTests/C1593_remote_prep_keeps_the_grant_while_in_flight`
  (expect `in-flight` red, `past-cap` green).

PCs (method-scoped, run once, restored): V-S3-5, V-S3-10, V-S3-11 (three arguments), V-S3-13 `past-cap`.

## Performance budget

Per [test performance standards](../../test-performance-standards.md) and
[docs/testing-and-build.md#performance-standards](../../testing-and-build.md):
- **Measured (S1 rounds on server2):** single capacity methods 1.2-11.4 s; first test per host
  about 10-11 s (shared Postgres plus clone); leased `tests/Antiphon.Tests` builds 217-399 s
  compile, 220-520 s with slot wait. Whole Unit is over 25 min and is not run.
- **Shared setup:** one shared Postgres per test host, one cloned database per test
  (`CapacityRecoveryTestSupport`, `TestDbFixture.CreateIsolatedSchemaAsync`); supervisor tests
  reuse `AgentSupervisionTests.BuildHarness` once per test; no per-test server boot.
- **Phases recorded per row:** slot wait, build, host start, per test (clone, act, assert).
- **Budget:** 21 new results (V-S3-1 2, V-S3-2 2, V-S3-3 1, V-S3-4 2, V-S3-5 1, V-S3-6 1,
  V-S3-7 1, V-S3-8 2, V-S3-9 2, V-S3-10 1, V-S3-11 3, V-S3-12 1, plus V-S3-13 2 on master).
  Estimated p95 12 s per method, so the method timeout and in-test lock deadline are 30 s
  (about 2x, provisional until Code records p95 from TRX). V-S3-5 drives three re-grants on
  `FakeTimeProvider`, no wall time.
- **Row estimates:** CP-1 about 15 min including the build; CP-2..CP-6 3-8 min each. The round
  stays under 45 min of test time; a row over 2x its estimate is a Debug item, never a timeout raise.

### Checkpoints

Lane: Linux, server2 (`GET /api/runner-defaults`: `globalRunnerId=server2`, revision 2). No
`-Runner` or `-Platform` pin. Build once with `scripts/build-slot.ps1` (`UseAppHost=false`,
`--property:OutputPath=bin-c1593s3/`), then `dotnet exec --no-build` on the built DLL per row
(S1 D-11: the checkpoint reader cap still applies; Code reports CHECKPOINT-shaped lines with TRX path and SHA-256).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S3e | `tests/Antiphon.Tests -> bin-c1593s3/` | c1593-s3-lifecycle | `/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)\|(CapacityLockOrderTests*)/*` | V-S3-1, V-S3-11, V-S3-12, S1 regression | 26 + 16 existing + 2 + 4 new = 48 (rc); +2 V-S3-13 on master = 50; 0 failed/skipped | 48 | 15 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S3e | CP-1 | c1593-s3-supervisor | `/*/Antiphon.Tests.Application/CapacityRecoverySupervisionTests/*` | V-S3-2..V-S3-5, D8, V16-V18 | 6 existing + 6 new = 12, 0 failed/skipped | 12 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S3e | CP-1 | c510-capacity | `/*/*/StandingRestartEscalationTests/(C510_S2_capacity_grant_is_required_and_follows_fresh*)\|(C510_S2_cleared_model_hold_grant_starts_resume_on_the_next_tick*)` | D-S3-2 regression | 2 args + 1 = 3, 0 failed/skipped | 3 | 4 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-4 | S3e | CP-1 | c1593-s3-retained-queue | `/*/Antiphon.Tests.Application/(CapacityRecoveryTaskTests*)\|(CapacityRecoveryQueueTests*)/*` | V-S3-6..V-S3-10, V19, V12-V15 | Task 6 + 2 new = 8; Queue 8 + 5 new = 13; total 21, 0 failed/skipped | 21 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-5 | S3e | CP-1 | capacity-regress | `/*/Antiphon.Tests.Application/(WallRerouteDispatchTests*)\|(ReceiptFailureDeliveryTests*)/*` | S1 regression | 15 + 21 = 36, 0 failed/skipped | 36 | 8 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-6 | S3e | CP-1 | c508-guard | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` | S1 regression | 97, 0 failed/skipped | 97 | 8 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

Suffix check at base: `CapacityAdmissionLifecycleTests*`, `CapacityLockOrderTests*`,
`CapacityRecoveryTaskTests*`, `CapacityRecoveryQueueTests*`, `WallRerouteDispatchTests*` and
`ReceiptFailureDeliveryTests*` each match one class (the partial files
`CapacityAdmissionLifecycleTests.Recovery.cs`/`.Remote.cs` are the same class;
`CapacityRecoveryTaskTests` has no `CapacityRecoveryTaskTests<X>` sibling). CP-2 and CP-6 use an
exact class segment. `C510_S2_capacity_grant_is_required_and_follows_fresh*` and
`C510_S2_cleared_model_hold_grant_starts_resume_on_the_next_tick*` have no sibling prefixes in
`StandingRestartEscalationTests`. New method names are not prefixes of each other
(`C1593_S3_supervisor_start_and_consume_commit_together` vs
`C1593_S3_supervisor_start_throw_charges_one_attempt` differ before any shared full name). Code
re-checks counts in the fresh TRX: the existing counts above are the brief's (26, 16, 15, 21, 97)
plus this plan's reading (Supervision 6, Task 6, Queue 8 results).

Not run: whole Unit lane, native Windows (a live rc qualification owns the desktop), checkpoint
tool sealed receipts (reader cap, S1 D-11).

### Landing

Cut from `origin/rc/2026-10-27`. Before Review: `git fetch`, `git merge-tree --write-tree` against
`origin/rc/2026-10-27` and `origin/master`; S3f only against master. Run
`scripts/check-evidence-diff.ps1 -Repository . -BaseRef aa6dfd24a51db7c02e8722a05a21817bf4df407a -HeadRef <tip>`.

## Risks

1. **Every standing capacity restart now runs through `StartInteractiveSessionAsync`'s consume.**
   A bug there stops standing restarts that have a wait. Mitigation: D8, C510_S2 and V16-V18 run
   unchanged (CP-2, CP-3); S-2 releases to the ungated path rather than stranding.
2. **S-2 classification is a code list.** A new durable conflict code that is not listed falls
   into S-3 (charged, bounded by `MaxAttempts`), the base outcome. Safe direction.
3. **Card arm under-spends in a crash window** (D-S3-2). One unpaced launch at most.
4. **Retained/queue overlap.** A retained task's wall wait can also be the wait a queue row is
   stamped with (`HasTerminalCapacityHoldAsync` on the same session). After S3 whichever consumer
   commits first spends; the other refuses `grant-mismatch` and holds, as base `RedeemAsync` did.
   No double spend: both consume under the same provider lock.
5. **Revalidation now consumable in the claim** (D-S3-1). Only legacy leaked dispatch rows can be
   in that state; consuming them without increment is `RedeemAsync`'s base semantics.
6. **Leak 6 renewal delays other waiters** up to the cap. Master only, setting defaults to 600 s,
   0 disables; tune from the server2 preparation p95.
7. **Pre-`Sent` exits retry under a held grant** until D-12. A persistently failing pre-`Sent`
   exit (spill hold) holds the provider up to 120 s per cycle with no attempt cap. The queue's
   own withhold counters bound the message; the plan records it as residual and Code reports the
   withhold path that ends it.
