# CARD-1593: capacity leaks left after S1

Read on 2026-10-28 at `HEAD` `4563e52529b23052856cb0f1e0d01b383cf7590f` (`origin/rc/2026-10-27`, branch `feat/card-task-bcb5bc45`). No production file was edited. No test was executed. The mechanism is this source, the plan `docs/superpowers/plans/2026-10-27-card-1593-capacity-admission-lock-order-plan.md`, and the S1 Final Review reports `dfe987ab5` (task 0248b0c4) and `40f68976e` (task 3b5e0eb8).

S1 on this rc line is `480280efcff682e4d610c1908e3ebf79f7724918` (consume inside the claim) and `87db8c07e4264cffd4cc0e9bfbbc12cfaa44a2b4` (reread the live session before spending). Master `83df22ac7540f3b5fb1c1a48d865dcbba2bc7efc` is the same liveness subject and is not an ancestor of this tip.

**Confirmed.** The dispatch claim now spends the grant in the same commit as `Dispatched`. Six leftovers named at review are still open. One of them re-holds the provider with no attempt cap. Three spend an admission and then skip the work. The quota path is a lock inversion, not a spent admission. `HeldForRemotePrep` no longer spends.

## What S1 already closed

`AgentTaskDispatcher.ConsumeAndCommitLaunchAsync` (`server/Application/Services/AgentTaskDispatcher.cs:8122-8171`) calls `RedeemOnAsync(..., consume: true)` and then `CommitAsync` on the claim transaction. `CapacityRecoveryService.RedeemOnAsync` (`server/Application/Services/CapacityRecoveryService.cs:313-389`) increments `AdmissionCount`, writes `StartAccepted` plus `LaunchSessionId`, clears the grant, and does not commit. A rollback undoes that spend. The pre-claim gate passes `consume: false` (`AgentTaskDispatcher.cs:964-966` -> `TryRedeemCapacityWaitAsync` `:1772-1776` -> `RedeemGateAsync`).

`RedeemAsync` (`CapacityRecoveryService.cs:240-304`) is the other wrapper. It commits on its own scope: `AdmissionCount++` (`:286`), state `Admitted` (`:290-292`), grant cleared (`:298`), `CommitAsync` (`:303`). It writes no `LaunchSessionId`, so `CapacityRecoveryPolicy.HasExecutionReceipt` (`CapacityRecoveryPolicy.cs:92-96`) is false unless some earlier write set `SelectedMessageId`.

## How a spent admission comes back

Two clocks, both `2 * AdmissionIntervalSeconds`. The default interval is 60 (`SupervisionSettings.cs:72` via `CapacityRecoverySettings`), so both are 120 seconds (`CapacityRecoveryPolicy.cs:87-90`). The attempt cap is `WallDeathCap`, default 3 (`SupervisionSettings.cs:235`, `CapacityRecoverySettings.EffectiveMaxEpisodeAttempts` `:80-84`), unless `MaxEpisodeAttempts` is set.

**D-12, an unredeemed grant.** `RedeemOnAsync` and `RedeemAsync` do not read `GrantExpired`. Only `GrantReadyAsync` (`CapacityRecoveryService.cs:150-166`) does, through `StillHoldsGrant` (`:1157-1173`) and `GrantExpired` (`:1176-1178`). An expired grant candidate is re-armed by `RearmRevokedGrantOnAsync` (`:1203-1222`): `AdmissionCount` stays, `DueAt` becomes now plus the stall timeout, outcome `grant-expired`. The provider can be given to someone else. This is the clock the brief calls D-12.

**Admitted with no receipt.** `RearmStalledAdmissionsAsync` (`:1320-1367`) selects `Admitted`, not `NeedsRevalidationGrant`, `UpdatedAt` older than the same 120 seconds. No receipt: `PrepareNextAttemptOnAsync` (`:1370-1407`) moves the ordinal and sets `Ready`, or sets `Exhausted` / `max-episode-attempts` when `AdmissionCount >= MaxAttempts` (`:1375-1382`). A receipt (`SelectedMessageId`, `LaunchSessionId`, `LaunchReceipt`, or `DispatchAttemptId`) takes the other branch (`:1347-1356`): `NeedsRevalidationGrant = true`, same action key, no new attempt. `IsGrantCandidate` (`CapacityRecoveryPolicy.cs:28-31`) is true for `Ready`, `ActionPending`, and `Admitted && NeedsRevalidationGrant`. A plain `Admitted` wait is not granted again until that re-arm.

`RedeemAsync` clears the grant at the commit, so the provider is free during the 120-second wait. D-12 does not apply to that row: there is no grant left to expire. The wait is the re-arm clock.

## 1. Non-Postgres commit fault. High. Unbounded

Review 3b5e0eb8 advisory A-1. S1 introduced it. Base failed the task.

**Trigger.** A claim reaches `ConsumeAndCommitLaunchAsync`. `CommitAsync` throws, the durable reread is still `Queued`, and the exception chain contains no `PostgresException`. The existing witness `commit-server-error` throws `PostgresException` `XX000` from `TransactionCommittingAsync` (`CapacityAdmissionLifecycleTests.cs:655-676`) and is class 4: the task fails and the reservation is released (`CapacityAdmissionLifecycleTests.cs:324-330`). An `IOException`, or any other non-Postgres exception, from that same interceptor is not that arm.

**Spend.** `RedeemOnAsync` has already cleared the grant and incremented `AdmissionCount` inside the open claim transaction (`CapacityRecoveryService.cs:374-388`).

**Missing release.** `AgentTaskDispatcher.cs:8161-8164`. `IsTransientCapacity` (`:8231-8239`) is only `40P01`, `40001`, and `55P03`. `IsPostgres` (`:8242-8250`) is false, so the catch returns `LaunchCommit.Refused`. It does not call `ReleaseFailedReservationAsync` (`:8197-8204`). `AbandonClaimAsync` (`:8063-8104`) rolls the commit back, which puts the grant and `AdmissionCount` back. The task stays `Queued`. The warning is deduped on the exact detail (`:8214-8218`), so later ticks add nothing.

**Strand.** The grant is still held, so this is D-12, not the Admitted re-arm. Each hold lasts until `GrantExpired` (120 seconds from `GrantedAt`). Re-arm does not increment `AdmissionCount` (the increment was rolled back), `AttemptWouldExhaust` stays false, and the next grant is spent into the same fault. The cycle does not stop at `MaxAttempts`. The provider is not held forever in one grant, and it is re-acquired on every cycle. Other waiters run only in the `DueAt` gap.

**Smallest fix.** Count consecutive non-Postgres `Refused` outcomes for that wait. On the cap, take the class-4 path already used at `:8167`: `ReleaseFailedReservationAsync` with `claim-consume-failed`, then rethrow into `FailAndNotifyAsync`. Do not treat the first fault as fatal: the reread-`Queued` branch exists so an acknowledgement lost before the commit is visible can still land (`:8152-8154`). A new column is what the plan rejected for an rc slice (D-9). A counter in the existing warning is useless because the warning is deduped to one row.

**Red first.** New argument `non-postgres-commit-fault` on `CapacityAdmissionLifecycleTests.C1593_consume_failure_leaves_the_task_queued`. Interceptor: the `CommitServerError` shape throwing `IOException` from `TransactionCommittingAsync`, twice. Today the second tick is `Queued`, `AdmissionCount == 0`, `GrantedWaitId` still the wait, `Failures == 0`. The fix expects the second tick `Failed`, wait `Canceled` / `claim-consume-failed`, grant cleared.

## 2. Supervisor restart. High for a standing agent. Bounded

Plan L16. `RedeemAsync` still runs before the start.

**Trigger.** `SuperviseAsync` sees an unfinished wait (`AgentSupervisorService.cs:209-212`; `Admitted` is unfinished). It commits and disposes the supervision transaction (`:226-227`), then `TryHandleCapacityWaitAsync` (`:230`). The provider grant matches (`:500-505`). `RedeemAsync` commits (`:507-508`). Any of these exits then skips the start and does not release:

- the persistent session already carries this `ActionKey` (`:512-516`)
- `CheckCompactionAdmission.BlocksAutomaticRestartAsync` (`:523-524`)
- `StartSupervisedRestartAsync` returns false (`:525-526`), including a durable fresh refusal that returns false at `:571`
- the start throws, and `RecordStartFailureInFreshScopeAsync` (`:241-244`, `:374-460`) records backoff and does not touch the wait

**Spend.** `CapacityRecoveryService.cs:286-303`.

**Missing release.** No `ReleaseClaimReservationAsync` and no supersede on those exits.

**Strand.** The grant is cleared at the redeem, so the provider is free immediately. The wait is plain `Admitted` with no receipt. `IsGrantCandidate` is false, and the outer check at `:209` is still true, so the tick returns false and does not fall through to the ungated restart at `:248`. `RearmStalledAdmissionsAsync` re-arms after 120 seconds, or exhausts at attempt 3. After `Exhausted` the outer check is false and the normal restart runs. Not permanent. A standing agent stays down for about three re-arm cycles, and each cycle has spent an admission on a start that did not happen.

**Smallest fix.** Move the spend into the start commit: `RedeemOnAsync(consume: true)` in the same transaction as the session row the start inserts, with `LaunchSessionId` set. The plan's S3 sentence is that shape. The compaction and duplicate-key checks move above the spend.

**Red first.** `CapacityAdmissionLifecycleTests.C1593_supervisor_refusal_does_not_spend`. Seed an AlwaysOn agent whose wait is granted, and a `BlocksAutomaticRestartAsync` true (or a `standing_resume_delivery_pending` conflict from `StartSupervisedFreshAsync`). Today `AdmissionCount == 1`, state `Admitted`, `GrantedWaitId` null, and the next `SuperviseAsync` does not call start. The fix expects `AdmissionCount == 0`, state still `ActionPending`, grant still held.

## 3. Retained return. Medium. Bounded, then the gate drops

Plan L6. Local counted slot only.

**Trigger.** A `Working` task with `CapacityWaitRetained` and a local empty `RunnerId`. The snapshot at `AgentTaskDispatcher.cs:510-516` is under `localLimit`, so the pre-check at `:613-614` does not `continue`. `TryRedeemCapacityWaitAsync(..., consume: true)` (`:633-635`) commits `RedeemAsync` (`:1779-1782`). `TryClaimCountedSlotAsync` (`:636-639`, `CapacityRecoveryService.cs:845-863`) then returns false.

The pre-check and the slot count the same population, but the snapshot is taken at `:510` and the redeem commits before the re-count. `AfterQueuedSnapshotAsync` (`:537`, `internal` at `:3987`) already runs in that window; tests set it (`DispatcherAdmissionFixture.cs:116`). A counted `Working` row inserted there makes the slot refuse after the spend. Two overlapping ticks do the same. The remote occupancy check `continue`s at `:630` before the redeem, so a full runner does not spend.

`TryClaimCountedSlotAsync` takes `pg_advisory_xact_lock` with no explicit transaction (`:851-853`). The lock ends with the statement. That is the plan's other S3 note. It does not by itself spend an admission.

**Spend.** `RedeemAsync` `:286-303`.

**Missing release.** The `continue` at `AgentTaskDispatcher.cs:639`. `CapacityWaitRetained` stays true. `SaveChanges` at `:652` is not reached for that task.

**Strand.** Provider free (grant cleared). No receipt, so re-arm burns the attempt, up to 3, then `Exhausted`. `FindUnfinishedCapacityWaitAsync` (`:1738-1750`) ignores `Exhausted`, and `TryRedeemCapacityWaitAsync` (`:1756-1757`) returns true when the wait is null. The next tick that finds a free slot clears `CapacityWaitRetained` with no grant. The task is not stuck. The pacing gate is what ends. Not a permanent provider hold.

**Smallest fix.** One transaction: counted-slot lock, the count, and `RedeemOnAsync(consume: true)` only if the count is under the limit. A false count commits nothing. Keep `consume: true` off the `RedeemAsync` wrapper for this caller (`:1779`).

**Red first.** `CapacityRecoveryTaskTests.C1593_retained_slot_filled_after_snapshot_does_not_spend`. Limit 1, snapshot count 0, `AfterQueuedSnapshotAsync` inserts one counted `Working` row, retained task holds the grant. Today `AdmissionCount == 1`, state `Admitted`, `CapacityWaitRetained` still true. The fix expects `AdmissionCount == 0`, grant still held, `ActionPending`.

## 4. Queue flush. Medium. Live path retries forever

Plan L17. The plan's `MaxAttempts` bound does not apply to the live stamp.

**Trigger.** The flush head has `CapacityRecoveryActionKey` and `CapacityWaitId`. The production writer is `ApplyCapacityHoldAsync` (`SessionMessageQueueService.cs:5561-5573`): a Channel or Scheduled row on a session whose newest recovery is a terminal hold (`HasTerminalCapacityHoldAsync`, `:5629-5644`), and the provider grant matches. It sets the key, the wait id, and `SelectedMessageId` before the redeem. `StampQueueActionAsync` (`CapacityRecoveryService.cs:968-986`) does the same and has no production caller. `EnqueueAsync`'s `capacityRecoveryActionKey` parameter (`SessionMessageQueueService.cs:799-800`) is unused outside tests.

`RedeemAsync` then commits (`:2579-2589`). Delivery has not run. Any later failure leaves the spend: spill hold `:2676`, transport `:2791`, spill-write `:2805`, missing spill `:2828`, forbidden `:2857`, truncated `:2862`, other verdict `:2869`. `RevertRunAsync` puts the message back to `Pending`. Nothing releases the wait.

**Spend.** `CapacityRecoveryService.cs:286-303`, after `SelectedMessageId` is already set.

**Missing release.** No release between `:2589` and those `FlushResult.Failed` returns. A `!result.Ok` at `:2588` returns `Nothing` without spending; that path is fine.

**Strand.** The grant is cleared. `HasExecutionReceipt` is true because of `SelectedMessageId`, so re-arm sets `NeedsRevalidationGrant` and does not increment toward `Exhausted` (`:1347-1356`). `RedeemAsync` on a revalidation grant does not increment (`:270-287`). The message is undeliverable until that re-arm (120 seconds), then it is granted again, and a failing delivery repeats. Provider free between the redeem and the next grant. Not permanent.

A head whose key was set without `SelectedMessageId` would take `PrepareNextAttemptOnAsync` and, after three spends, `Exhausted`. The flush would then return `Nothing` forever (`:2588`). No production caller does that today. `PrepareNextAttemptOnAsync` (`:1401-1407`) only rewrites messages that already have the old key; it does not clear `SelectedMessageId`.

**Smallest fix.** Call `RedeemOnAsync(consume: true)` in the transaction that marks the run `Sent`, and pass `launchSessionId: sessionId` so the receipt is the session. A rollback before `Sent` keeps the grant. Do not commit `Admitted` at `:2586`.

**Red first.** `CapacityRecoveryQueueTests.C1593_flush_failure_does_not_spend`. Stamp via `ApplyCapacityHoldAsync`'s conditions (or `StampQueueActionAsync`, which is what that class already calls), grant the wait, and force `HoldSpillBodyForDeliveryAsync` false. Today `AdmissionCount == 1`, state `Admitted`, grant null, and the next flush returns `Nothing` until re-arm. The fix expects `AdmissionCount == 0`, grant still held, message `Pending`.

## 5. Quota reply. Medium as a deadlock. No admission spend

Plan L23, and the fence L24 when the settle is the transcript-action scope. Not an admission leak.

**Trigger.** `HandleApiErrorTurnAsync` classifies a quota wall (`AgentTaskReplyService.cs:1948-1950`). It dirties the task (`:1972-1979`, including `CapacityWaitId = null`) and the recovery row (`:1980-1988`) with no transaction, then `SupersedeTaskWaitsOnAsync` (`:1991`).

`SupersedeTaskWaitsOnAsync` opens its own transaction when there is none (`CapacityRecoveryService.cs:502-503`), takes the provider lock (`:506`), and `SaveChangesAsync` (`:533`) flushes the dirty task under that lock. In `OnTranscriptActionAsync` the context has `RuntimeActionTaskFence` set (`AgentTaskReplyService.cs:121`). `AppDbContext.SaveChangesAsync` (`AppDbContext.cs:163-164`) then locks the session and then the task, still inside the supersede transaction. Order is provider, then session, then task.

**Spend.** None. Supersede sets unfinished waits to `Superseded` and clears a grant it holds (`CapacityRecoveryService.cs:521-527`). That is a release. There is no `AdmissionCount++`.

**Missing release.** There is no missing release on the success path. On `40P01` the `finally` disposes the uncommitted transaction (`:537-541`). The grant is unchanged. `OnTurnEndLockedAsync` logs and returns (`:147-151`). `OnTranscriptActionAsync` does not catch (`:123-124`). The task stays `Working` or `Dispatched` and the next settle retries. The provider is not stranded.

**Strand.** Neither permanent nor a D-12 hold caused by this path. A concurrent locker that holds this task and then wants the provider deadlocks with this save. The S1 claim does not: it locks only `Queued` rows (`AgentTaskDispatcher.cs` claim filter, plan D-5). The partner the plan names is the released-seat continuation on the same task.

**Smallest fix.** The plan's S2 shape: one transaction, `AgentTasks` `FOR UPDATE` first, session `FOR UPDATE` when the fence is on, then the task and recovery edits, then the ambient supersede. Swap the fence to task then session (`AppDbContext.cs:163-164`). The plan already names the witness: `CapacityLockOrderTests.C1593_path_blocked_on_task_holds_no_provider_lock` argument `quota-blocked`.

**Red first.** That argument. A second connection holds the task `FOR UPDATE`. The quota settle blocks on it. Today `pg_locks` shows a granted `antiphon.capacity.kind.*` for that backend. The fix expects no kind key while the backend waits.

## 6. HeldForRemotePrep. Low. Reservation, not a spend

S1 removed the consume. The original card's "admission consumed during remote preparation" is not this tip.

**Trigger.** The gate passes with `consume: false` (`AgentTaskDispatcher.cs:964-966`). `PrepareRemoteWorkspaceAsync` returns null, `RemoteWorktreePath` is null (`:6017-6018`). The claim commits with the task still `Queued` (`:6021-6022`) and returns `HeldForRemotePrep` (`:6026`). `ConsumeAndCommitLaunchAsync` is not called. Later ticks hit `RemoteHoldForAsync` (`:1245-1246`) before the gate (`:949-959`) and do not redeem.

**Spend.** None. `AdmissionCount` stays 0. The grant stays `GrantedWaitId`.

**Missing release.** Nothing clears the grant at `:6021`. The reconciler does, when `GrantExpired` (`CapacityRecoveryService.cs:1176-1188`) and the wait is still a grant candidate. `RearmRevokedGrantOnAsync` (`:1203-1222`) does not increment `AdmissionCount`.

**Strand.** Until prep finishes, or until D-12 (120 seconds from `GrantedAt`), whichever is first. Not permanent. The plan's D-10 says a server2 mirror often outlasts that 120 seconds, so the waiter loses the slot and waits out `DueAt` while other waiters run. This read did not measure that p95. A recorded mirror is returned as-is (`AgentTaskDispatcher.cs:7349-7350`); the next granted tick consumes once.

**Smallest fix.** On the `HeldForRemotePrep` commit, set `GrantedAt` forward by the preparer's remaining budget so D-12 does not revoke a live prep. Leave `AdmissionCount` alone. A later card, as D-10 says. Do not redeem here.

**Red first.** New argument on `CapacityAdmissionLifecycleTests.C1593_preparation_restart_and_dispatch_consume_one_admission`, or a sibling `C1593_remote_prep_keeps_the_grant_past_the_stall_timeout`. Advance past `StalledAdmissionTimeout` while `IsInFlight` is true, run `GrantReadyAsync`. Today the grant is cleared, `OutcomeReason` is `grant-expired`, `AdmissionCount == 0` (V-4 c5a already asserts that). The fix expects `GrantedWaitId` still the wait. V-4 c5a stays the witness that an idle grant still expires.

## Slice order

| Order | Item | Ship | Why this order |
|---|---|---|---|
| 1 | Non-Postgres commit fault | rc, then master | S1 regression. Only unbounded provider re-hold. The class-4 release already exists. |
| 2 | Supervisor, then retained return, then queue flush | rc, then master | Same `RedeemAsync`-before-work class. Supervisor stalls a standing agent; retained return drops the gate after three spends; the live queue path only delays a message. One card, three commits. |
| 3 | Quota settle and the fence swap | master, then rc port | Lock order only. Cannot meet the S1 `Queued` claim. The plan's S2 sequence. |
| 4 | Extend `GrantedAt` across `HeldForRemotePrep` | rc when the prep p95 is measured | Not a spend. D-10 already accepted the revoke. |

None of these are master-only. S2 is master-first because the plan says so, and the port is the same patch. S3 was "a separate card before CARD-1593 closes"; that card can land on `rc/2026-10-27` and then master. The desktop qualification named in the brief was not touched.

## Not done, noted

No product change. The fixes above are the next plan's sentences, not this tree. The server2 preparation p95 that D-10 asks for was not queried.

## Uncertainties

- Whether two dispatcher ticks overlap in production often enough to hit the retained window. The window is real; `AfterQueuedSnapshotAsync` makes the test deterministic without that measurement.
- The prep p95. The code path does not depend on it. The severity of item 6 does.
- A message keyed without `SelectedMessageId` would stick after three spends. No production caller of that shape was found.
