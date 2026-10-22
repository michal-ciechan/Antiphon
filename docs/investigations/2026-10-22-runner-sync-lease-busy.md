# Runner settlement sync lease contention, 2026-10-22

Debug task `16d74064`. Read-only production observations at 09:14-09:18 UTC;
source audit at `origin/master` **b59706530950ad5fb334c216a6f18a4e9bfd5103**.
No production settings, services, worktrees or qualification runs were changed.
Only this document and two new board cards were written.

## Finding

There are three overlapping effects:

1. **A real old debt keeps reacquiring the repository mutation lease.** Task
   `76fe1072-b65f-46ce-898c-1c927faffc4e`, CARD-0100 S1+S2 review 2, succeeded on
   October 14. Its debt was still Pending at **2,168 attempts** on October 22.
   Four Held events on three land tasks positively identify it as the current
   `worktree-settlement` lease owner. This is repeated recovery, not an eight-day
   continuously held lock. CARD-1446 already fixes the unlimited retries on master,
   but **the running server has not activated that fix**.
2. **Ordinary sync competes with land/dispatch and is excluded by land verification
   windows.** Even when verification releases the repository lock, an ordinary sync
   acquire has no compatible preflight and is refused by the window. The generic
   `runner_sync_lease_busy` reason does not distinguish these cases. Actual sync
   waits also never enter the land-admission waiter registry: its tests manually
   register a settlement waiter, but production does not. New **CARD-1479** tracks
   this missing producer wiring.
3. **A Pending completion note is historical, not a live debt status.** In the
   measured cohort, eight debts were already Ready and three Superseded, while the
   original warning and `progressEvidence.remoteSync` still said Pending. The eight
   Ready debts took **50.3-1,715.2 seconds** after settlement, median **884.0 seconds
   (14.7 minutes)**. Two Code reports really did block on lease busy and required
   another caller Reply. The problem is material, but the sampled data does not
   support “almost every completion remains unsynced.”

## Evidence scope and current debts

Authenticated GETs used `ANTIPHON_API` and the inherited task-token header without
printing either credential. Board scope was Antiphon
`8988ca03-7414-47ad-b0b6-51556c701703`, project
`d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

| Read | Result and limitation |
|---|---|
| `/api/version`, 09:14:11Z | `ceb6e3d6b78471db6831258699c493b8eb2d619b`, capability `land-v2` |
| `/api/agent-tasks?boardId=...&since=2026-10-22T00:00:00Z`, 09:14:56Z | 118 task summaries; all 118 detail GETs succeeded. Includes older open tasks by the documented `since` rule. Of these, 107 runner tasks completed today: 99 Succeeded, 5 Canceled, 3 Failed. |
| `/api/agent-tasks/{id}` for that cohort | 11 debt records: 8 Ready, 3 Superseded, 0 Pending/Held in this cohort at their individual reads. These are rolling observations, not one DB snapshot. |
| `/api/agent-tasks/76fe1072`, 09:14:12Z / 09:17:46Z | Pending, attempts 2,167 / 2,168; `runner_sync_inspection_unavailable`; `confirmedSha=null`, `sourceReadyAt=null` |
| `/api/attention?boardId=...`, 09:15:10Z | One `settlement-sync-debt:` item, for `76fe1072`; other kinds also occur. Attention excludes fresh Pending and paid debts, so it is not a census. |
| `/api/agent-tasks/pipeline?projectId=...`, 09:17:47Z | Three queued tasks, all `awaitingDispatch`; no repository owner was exposed by those rows at this instant. |
| Unbounded board task list | Aborted at the explicit 30-second HTTP deadline. Not used as absence evidence. |

**The total number of persisted debts now is not exposed by a census API.** Task
detail returns only the newest row for the task's current attempt, with no
CreatedAt/UpdatedAt/DesktopBeforeSha. See `AgentTaskService.cs:2872-2877` and
`AgentTaskDtos.cs:483`. The complete route map and endpoint/source search expose no
debt-list route. Enumerating current task details would still miss previous-attempt
debts. The observations above establish **at least 12 distinct current records**;
they do not establish a fleet-wide total or prove there are no other Held rows.
Use the read-only SQL below for that answer.

The earlier [CARD-1446 investigation](2026-10-22-card-1446-stale-landings-and-sync-debt.md)
reported **118 rows at 01:21-01:40Z**: 110 Ready, 7 Superseded, 1 Pending at 2,080
attempts. Those are historical DB counts, not this investigation's current census.
Ready is the paid terminal state, not open debt; there is no enum member Confirmed.

### Every debt found in today's cohort

Times are UTC on October 22. Created is the matching Pending settlement event
(the producer assigns the debt's CreatedAt from that settlement instant).
“Wait” is Pending settlement to SourceReadyAt, including retry pacing and Git work;
it is **not** a stopwatch measurement of exclusive lock wait.

| Task | State at read | Attempts | Created | SourceReadyAt | Wait seconds |
|---|---|---:|---|---|---:|
| `371ebe06` | Ready | 4 | 01:32:51 | 01:44:15 | 684.0 |
| `2beb76d6` | Superseded | 3 | 01:34:09 | null | n/a |
| `1a07bb3b` | Superseded | 2 | 03:30:23 | null | n/a |
| `bd490132` | Ready | 2 | 03:32:16 | 03:34:09 | 112.9 |
| `0e93cdd7` | Ready | 1 | 04:59:10 | 05:00:00 | 50.3 |
| `5d4be758` | Ready | 7 | 05:15:45 | 05:39:51 | 1,446.1 |
| `7e34f413` | Ready | 7 | 05:18:34 | 05:43:05 | 1,471.6 |
| `447440a0` | Ready | 8 | 06:34:39 | 07:03:14 | 1,715.2 |
| `0f389926` | Superseded | 4 | 06:50:32 | null | n/a |
| `8a0bbf02` | Ready | 5 | 08:58:51 | 09:13:43 | 892.6 |
| `8e3c334d` | Ready | 5 | 09:00:57 | 09:15:32 | 875.3 |

Ready sample n=8: min 50.3s, continuous median 883.95s, nearest-rank p90/max
1,715.2s; attempts 1-8, median 5. All eight have ConfirmedSha equal to SourceSha.
Superseded means recovery is no longer owed after retirement/missing registration;
it does not prove a fast-forward occurred. Creation ages at approximately 09:18Z
range from 17 minutes to 7h45m in this cohort.

The separate old debt is `79267872-4b88-4cdc-b762-28674b509cd1`, created
**2026-10-14T08:09:16.929749Z**, age **8d1h8m** at the second read. Its source is
`be8c25f96a79bc07c5b0a4a9792e9b2fc44e5e2b`; next attempt moved from 09:15:53Z
to 09:22:33Z. Claims are paced, not made on every dispatcher tick.

### Operator SQL for a complete, consistent census

Run against the desktop application's database using its existing authorized SQL
connection, not the runner's test database. This task did not execute this SQL.
The schema is confirmed by `20261006212209_AddAgentTaskSyncDebts.cs`.

```sql
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '15s';

SELECT current_timestamp AS observed_at,
       CASE "State" WHEN 0 THEN 'Pending' WHEN 1 THEN 'Ready'
                    WHEN 2 THEN 'Held' WHEN 3 THEN 'Superseded' END AS state,
       count(*) AS debts, min("CreatedAt") AS oldest_created,
       max("CreatedAt") AS newest_created,
       max(current_timestamp - "CreatedAt") AS oldest_age,
       min("Attempts") AS min_attempts, max("Attempts") AS max_attempts
FROM "AgentTaskSyncDebts"
GROUP BY "State" ORDER BY "State";

SELECT d."Id", d."TaskId", d."Attempt", d."RunnerId",
       CASE d."State" WHEN 0 THEN 'Pending' WHEN 1 THEN 'Ready'
                      WHEN 2 THEN 'Held' WHEN 3 THEN 'Superseded' END AS state,
       d."CreatedAt", current_timestamp - d."CreatedAt" AS age,
       d."UpdatedAt", d."Attempts", d."ReasonCode", d."NextAttemptAt",
       d."SourceReadyAt", d."DesktopBeforeSha", d."SourceSha", d."ConfirmedSha",
       d."WorktreePath"
FROM "AgentTaskSyncDebts" d
ORDER BY d."State", d."CreatedAt", d."Id";

SELECT count(*) AS paid,
       min(extract(epoch FROM "SourceReadyAt" - "CreatedAt")) AS min_seconds,
       percentile_cont(0.5) WITHIN GROUP
         (ORDER BY extract(epoch FROM "SourceReadyAt" - "CreatedAt")) AS p50_seconds,
       percentile_cont(0.9) WITHIN GROUP
         (ORDER BY extract(epoch FROM "SourceReadyAt" - "CreatedAt")) AS p90_seconds,
       max(extract(epoch FROM "SourceReadyAt" - "CreatedAt")) AS max_seconds
FROM "AgentTaskSyncDebts"
WHERE "State" = 1 AND "ConfirmedSha" = "SourceSha"
  AND "SourceReadyAt" IS NOT NULL;
ROLLBACK;
```

Keep the first query unscoped for the requested complete debt count. For Antiphon
only, join AgentTasks on TaskId and its board/card scope; do not silently call a
project subset the fleet. The second query reports attempts for every row, including
prior attempts that the task API hides. Neither table nor API records individual
acquire durations, so this SQL cannot yield a true lease-wait histogram.

## What owns the lease and why the old task appears

`RunnerSyncLeaseWaits` is only an in-memory task-to-first-busy-time map. It owns no
lock and supplies no telemetry endpoint. The real custody is
`IRepositoryMutationLease`: an exclusive FileStream on
`<common-git-dir>/antiphon/landing.lock`, plus verification-window and child-journal
admission (`RepositoryMutationLease.cs:47-87`). Owner metadata records a fresh
acquisition ID/time and is removed on disposal; task status is just descriptive.
A Succeeded task can legitimately own this lease while recovering its debt.

| Stage | Behavior |
|---|---|
| Validate and observe | `RemoteWorkspaceService.cs:493-647` validates checkout before acquiring; observes the exact remote ref. `TaskProgressGit` needs the repository lease if the object must be fetched. |
| Sync mutation | `RemoteWorkspaceService.cs:648-735` acquires purpose `worktree-settlement`, revalidates identity, pins S, checks ancestry, runs `merge --ff-only S` in the task checkout and rechecks postconditions. |
| Ordinary report | First-busy time survives service scopes; polling is 1s, at most 10s per attempt, cumulative budget 120s by default. `runner_sync_lease_waiting` defers settlement; budget exhaustion becomes `runner_sync_lease_busy`. Defaults are source facts, not an inspected settings override. |
| Durable debt | `SettlementSyncRecoveryService` selects at most 32 due rows, claims each before Git, increments Attempts, then runs `SyncSettledAsync(singleAttempt:true)`. A busy acquire returns immediately. Backoff is 1, 2, 4, then 5 minutes. Inspection Unavailable still maps to Pending. |
| Master cap | Commit `78297d86442be434218c316df49fd554943034a1` checks `RunnerSyncDebtMaxAttempts` (default 48; zero disables) **before Git**, moves exhausted Pending to Held, and only restamps Held hourly. |

The live SHA's `SettlementSyncRecoveryService` has **no cap**. The cap commit is not
its ancestor. RemoteWorkspaceService, AgentTaskWorktreeBaseResolver and
WorktreeBaseInspection are byte-identical between that live SHA and the audited
master. This is direct source/version evidence of activation skew, not inference
from health. An eventual caller-owned rc port/activation can stop the poison row's
Git retries; this Debug did not restart the stack or interfere with qualification.
Held does not automatically become Ready after a manual repair; CARD-1459 already
tracks that separate limitation.

These exact task events establish old-task ownership:

| Land task | Held event UTC | Owner text |
|---|---|---|
| `36786bbc` | 08:39:07 | `76fe1072... (worktree-settlement); status=Succeeded` |
| `36786bbc` | 08:49:52 | same |
| `75c03be9` | 08:49:53 | same |
| `c80c961f` | 08:55:26 | same |

The cohort contains 72 “lease ... occupied” observations: 56 land on 23 waiting
tasks, 7 dispatch on 3 tasks, 7 worktree-settlement on 4 tasks, 2 unclassified on 2
tasks. Counts overlap by task and are observations, not occupancy shares. No sampled
event attributes a holder to maintenance. CARD-1462's maintenance work is planned;
it would be incorrect to blame an unobserved maintenance holder.

The old debt is a demonstrated competing owner, but the precise inspection failure
and its hold duration require desktop server logs. Retrieve the
`Settlement sync for task ... could not inspect its checkout` lines via the owner
procedure in [logs.md](../logs.md), then inspect that exact desktop checkout. The
generic reason also covers non-exception failed reads; absence of a log line alone
does not identify the failed Git command. This runner cannot inspect
`C:\Antiphon\worktrees\card-task-76fe1072`. No lock deletion, reset or direct repair
is justified by these observations.

### Starvation and the missing waiter producer

The [CARD-1462 option A plan](../superpowers/plans/2026-10-22-card-1462-option-a-desktop-land-cost-plan.md)
already covers long land holds, cleanup/publication windows, shorter dispatch
holds and batching. Its predecessor measurement found seven back-to-back lands on
October 21 holding R for 165-543s each. That is historical corroboration, not a new
hold-duration measurement here.

While a land verifies, it keeps execution lease L and opens a verification marker.
`RepositoryVerificationWindows.cs:48-100` accepts only compatible preflights;
`RemoteWorkspaceService` uses ordinary `TryAcquireAsync`, so it cannot use that
window. Build-slot waits can therefore block sync without occupying landing.lock.
Simply adding more windows under option A does not admit sync through them.

Outside windows, `AgentTaskLandService.YieldToDispatchAsync` consults
`RepositoryLeaseWaiters`. Production registration sites are the dispatcher and
StartRefAvailability; sync has **none**. `RunnerSyncLeaseWaits` is a different map.
`AgentTaskLandDispatchYieldTests.C672_a_waiting_dispatch_holds_the_land_at_admission_until_it_clears`
has a `WorktreeSettlement` argument but manually inserts its waiter. Thus the test
proves the consumer's response to a fabricated registration, not the production
chain. CARD-1427/1428 retain dispatch waiters; they do not repair this omission.
Serial lands can repeatedly win sync's retry gaps. Current evidence demonstrates
contention and long completion latency, not permanent starvation of all syncs.

## User-visible harm and stale-source readers

* Eleven sampled tasks emitted `Runner sync pending ... synced later`; all eleven
  subsequently reached a terminal debt state. Recovery deliberately writes only
  the debt, never rewrites the settlement warning/progress snapshot. For example,
  `447440a0` still has remoteSync Pending but syncDebt Ready with matching source
  and confirmed `6e81228d830e3ff9b3e780391591ee424c813202`.
* `53dfb959` produced a 3,523-character done report at **06:52:43Z** but settled
  Blocked with `runner_sync_lease_busy`. Caller replied at 06:56:29Z; Succeeded at
  07:01:49Z, **545.3s** after the block. `242906fa` similarly blocked at
  **06:58:05Z**, was replied to at 06:58:19Z and succeeded at 07:02:06Z,
  **241.0s** later. These reports were retained, not lost; successful completion was
  gated and extra turns were spent. CARD-1109 already owns unfetched Code progress,
  CARD-1133 its status wording, and CARD-1043 review rebinding. The current API's
  evidence is the later synchronized result, so these events alone do not prove
  which subcase caused the first block. `c25ee080` also had a sync timeout.
* Before the 120s budget is spent, `AgentTaskReplyService.StillWaitingForLease`
  leaves the task open and saves no settlement. Logs, not completion-event time,
  are needed to measure that earlier withheld interval.
* There is retained evidence of a desktop tip behind the pushed source: Code task
  `0e93cdd7` recorded localObserved
  `3a74807d442b5ddac35b46f1ebc9917b0219b0ce` and remote/source
  `964f81b03285f6c10e0cb9eb33e91e9fd45e6f2a` while Pending; its debt became Ready
  50.3s later. `8a0bbf02`, `8e3c334d` and `bd490132` likewise retain distinct
  local/source observations. These are the server's settlement-time observations,
  not a fresh filesystem read by this Debug. Conversely, Review `371ebe06` already
  recorded localObserved equal to source `5036eefba95f96ff8a1d06b8a22794782380eb01`
  while its debt was Pending. Thus Pending alone does not prove HEAD is behind.
  The SQL's DesktopBeforeSha and a subsequent desktop inspection distinguish
  genuinely behind from merely unconfirmed; null ConfirmedSha is not a distance.

**Landing source:** `AgentTaskLandSourceResolver.cs:109-169` reads the local branch
ref, fetches/pins the authoritative remote source and, on Behind, requires it to
equal ExpectedSourceSha. It creates a separate land worktree; it does not need the
sync sweep to fast-forward the task directory. Existing
`SettlementSyncDebtLandingTests.C1082_PendingSyncOwnerLandsOnPushedBranch` covers
this contract. There *are* source-worktree reads later:
`AgentTaskLandingProtocol.ValidateTicketAsync` checks source registration and
tracked/sequencer state after verification. CARD-1463 already tracks the resulting
dirty-source refusals; those reads do not select the old checkout HEAD as the
publication source. Therefore “lands never read the desktop worktree” is too broad.

**Review source:** a new Worktree Review explicitly cut with `-StartRef <full S>`
can review the pushed source independently of sync. A ReadOnly Review aimed at the
old desktop path can read stale files. More subtly, automatic same-card inheritance
and explicit Task base selection use local `refs/heads` through
`WorktreeBaseInspection.BranchCommitsAsync`; `CardSiblingInventory` and
`AgentTaskWorktreeBaseResolver` do not consult debts or remote-sync evidence. They
can omit a pending source whose old local B is already in target, or select B as
the clean quiescent explicit task tip. This is a static data-flow finding; no
specific incorrectly certified live Review was established. New **CARD-1480**
tracks it separately. Until resolved, the Pending warning's blanket “Review ...
use the pushed branch” is safe only when the review actually binds the pushed SHA.

## Smallest actionable change

First, the caller should schedule the existing CARD-1446 cap through the approved
rc rollout when qualification permits; no duplicate code for the 2,168-attempt debt
is needed. Then implement **CARD-1479**, one 30-60-minute Code task, without changing
custody or settlement policy:

1. Inject the existing singleton `RepositoryLeaseWaiters` into ordinary sync. At
   either actual busy boundary (observation fetch or mutation acquisition), register
   `WorktreeSettlement` using the resolved common-directory key and task ID.
   Keep `VerificationCompatible=false`: this slice does not expand window authority.
2. Keep the original Since while returning LeaseWaiting between sweep scopes.
   Clear the registration once the corresponding acquire succeeds, on terminal
   results (including exhausted budget/Pending), and on cancellation/exception.
   Make end-of-report cleanup cover early eligibility exits, so a canceled or
   changed task cannot retain a stale waiter. Preserve the existing cumulative
   wait map's retry semantics; clearing the fairness entry need not reset its clock.
3. Keep durable `singleAttempt` recovery unregistered between attempts. A five-minute
   backoff is not runnable demand and must not make each new land waste its 90s
   yield budget. Do not add batching or faster debt retries in this slice.
4. Exercise the production registration path and actual land admission together.
   The land's existing 90s yield remains bounded. This buys an opportunity between
   lands, not a universal sync latency guarantee while windows/other holders remain.

Rejected for this small slice: a second independent sync lock or lease-free
fast-forward would lose exclusion with retirement, land cleanup, branch changes
and child journals. Although refs/index are scoped, the custody protocol is not.
A compatible sync preflight is plausible future work, but requires source-path/ref
exclusion and native custody tests. Longer waits already have a bounded mechanism
and would merely delay reports. Bulk sync under one long R hold would amplify the
poison-row problem. G1-G8 Git batching belongs to CARD-1462.

### Red witness and acceptance

Use a new `RunnerSyncLeaseFairnessTests` class, existing SyncWorld/landing harness,
one shared registry and FakeTimeProvider. Proposed methods:

| V | Method / observable |
|---|---|
| V-1 | `Busy_sync_makes_next_land_yield`: real sync sees a held R; no test calls Register; next real land admission is Held/yield before Git. The current producer leaves Snapshot empty and permits admission. This is the committed red witness. |
| V-2 | `Fetch_wait_registers_before_objects_are_local`: remote S absent locally, observation lease busy; same worktree-settlement waiter appears without a fetch or checkout mutation. |
| V-3 | `Waiter_survives_slices_with_original_since`: two real service scopes return LeaseWaiting, preserve Since and cause bounded land yield; after release sync succeeds and removes the waiter. |
| V-4 | `Every_terminal_exit_clears_waiter`: table-driven acquisition/success, refusal, exhausted budget, exception, cancellation and changed eligibility; no fairness ghost after any exit. Do not count internal rows as separate executions. |
| V-5 | `Debt_backoff_leaves_no_waiter`: singleAttempt busy debt preserves Pending/retry pacing but leaves no runnable waiter. |
| V-6 | `Verification_window_remains_exclusive`: ordinary sync stays refused during the real live verification window and cannot modify source or target; release permits subsequent sync. |
| V-7 | `Production_services_share_the_waiter_registry`: resolve the production registrations and drive refusal through that instance; land observes the same waiter. No reflection-only registration assertion. |

Commit/push the V-1 test before the fix. It must compile and fail its missing-waiter
or land-yield assertion; zero tests/build errors are not red. Then commit the fix
and all seven tests. Run the table as the closed ordinary scope on server2 using
the checkpoint tool (`run --plan <this-file> --rows ... --expected-source-sha <sha>`,
then wait until exit is not 75). All drivers use the host build-slot gate; no
unleased retry on exit 4. Keep each run's source frozen and remove task-owned
alternate outputs after the tool's cleanup. No Windows qualification is required
or authorized for this slice.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-test | `tests/Antiphon.Tests -> bin-c1479-red/` | sync-waiter-red | `/*/Antiphon.Tests.Application/RunnerSyncLeaseFairnessTests/Busy_sync_makes_next_land_yield` | V-1 red witness | 1 executed, 1 expected assertion failure; build succeeds | 1 | 6 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1479-green/` | sync-waiter-green | `/*/Antiphon.Tests.Application/RunnerSyncLeaseFairnessTests/*` | V-1..V-7 | all 7 methods passed, 0 failed/skipped | 7 | 9 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1479-regression/` | sync-custody-regression | `/*/Antiphon.Tests.Application/(RunnerSettlementSyncTests*)\|(AgentTaskLandDispatchYieldTests*)\|(RepositoryLeaseWaitersTests*)/*` | Existing fast-forward/identity/retirement guards, 120s budget and 10s slices, bounded yield and registry cleanup | all selected passed, 0 failed/skipped | 43 | 12 |

CP-3 floor is 30+6+7 existing Test methods, with argument expansion potentially
raising executed count. No assembly-wide run is requested. Estimated verification
27 minutes plus about 20-30 minutes implementation/reviewable evidence. These are
proposed checkpoints, **not executed results**. CARD-1480 needs its own plan and
must not expand this Code task.

## Board disposition and verification

Before filing, completed `card.ps1 search -Board Antiphon -All` enumeration for
CARD-1427, 1428, 1446, 1462, settlement (123), stale (327), sync waiter (0),
turnstile (0), pending sync (2), and stale local (1); fetched relevant hits in full.
Existing owners retained: CARD-0672, 1427/1428, 1446/1459, 1462, 1109/1133, 1043,
1448, and 1463. Filed only:

* **CARD-1479**, `89602d90-1ea7-47c6-a181-ab81058d3bd1`: missing settlement-waiter
  producer; next Code with the closed scope above.
* **CARD-1480**, `9373bbe8-c1cf-4cdc-92ab-31ef02debdf0`: debt-blind same-card base
  selection; separate Plan, static risk without a claimed live wrong-review incident.

CARD-1479 was created successfully; the CLI then exited 1 while formatting a
Windows card-file path on Linux (`Cannot find drive C`). A subsequent GET confirmed
the stored card, so creation was not retried. CARD-1480 used JSON output successfully.

No builds or tests were run in this docs-only Debug. API observations, code paths,
schema and source/version comparisons support the findings; the checkpoint red
witness remains for Code. The missing full DB census, exact lock-time distribution,
desktop HEADs, and precise old-task inspection failure remain explicit operator
evidence gaps. Generated local API receipts are ignored under `.antiphon/sync-debug`;
the actionable observations and SQL are retained here because the mirror is disposable.

--- next stage ---
next: code
handoff: Implement CARD-1479 only, with V-1 red first and CP-1..CP-3. Caller separately schedules the existing CARD-1446 rc activation and CARD-1480 Plan; preserve the running Windows qualification.
artifact: docs/investigations/2026-10-22-runner-sync-lease-busy.md
