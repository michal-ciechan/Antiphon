# CARD-1446: stale `AgentTaskLandings` rows and open sync debt (2026-10-22)

Investigate task 2beb76d6 (runner mirror, branch `feat/card-task-2beb76d6`, base 9b39df4e). Read-only
queries against the desktop dev Postgres (`antiphon` database, port 17280, `BEGIN READ ONLY`) at
2026-10-22 01:21-01:40 UTC, plus `GET /api/agent-tasks/76fe1072` and `GET /api/attention`. Code
references are to this branch's tip, which is master 9b39df4e for every file cited. Nothing was
written to the database and no sweep was run.

## Verdict

The "stale" protections found by CARD-1263 are three different things, and only one of them is a
defect in a closing path:

| Kind | Rows | What it is | Should close by | Why it did not |
|---|---|---|---|---|
| A. Published, cleanup refused | 145 landings / 145 tasks (`Active`, `Phase=CleanupStarted`, `Cleanup=Refused`) | The designed "landed with residue" end state: publication confirmed, worktree deliberately kept | A cleanup-only land request (`CleanupRetry` mode) reaching `Complete` | The only two producers of cleanup-only requests are switched off: the daily residue sweep runs with `WorktreeResidue:Execute=false` (holds every candidate), and card-done cleanup has `CardDoneAfterUtc=null` (0 targets ever). No `CleanupOnly` land request has ever existed. |
| B. Refused mid-flight, never marked Refused | 5 landings / 5 tasks (`Phase` 5, 5, 6, 6, 7 with a refusal `LastReason`) | Schema-2/3 operations from 2026-09-16..26 whose land request completed as refused while the operation row stayed non-terminal | `AgentTaskLandingProtocol` terminal rule turning the refusal into `Phase=Refused` | The rule at `server/Application/Services/AgentTaskLandingProtocol.cs:380-388` does not cover `source_changed`/`land_request_identity_conflict` at `TargetAdvanceStarted`, nor `push_rejected` at `PushStarted` (schema 3). Missing terminal transition; no restart or exception involved. |
| C1. Sync debt `Ready` | 110 debts / 110 tasks | **Not open.** `Ready` with `ConfirmedSha = SourceSha` is the paid, terminal state | Nothing; already closed | The CARD-1432 protection rule counted `Ready` as open. That is a classifier misread, not a stale row. |
| C2. Sync debt `Pending` looping | 1 debt (task 76fe1072, Review, Succeeded 2026-10-14) | The sweep has retried it 2,080 times, every ~5 min for 8 days | `Held` after an inspection failure | `SettlementSyncRecoveryService` maps `RemoteSettlementSyncState.Unavailable` (`runner_sync_inspection_unavailable`) back to `Pending` with a 5-minute backoff and no attempt cap. |

Beyond cleanup, the rows cost: one permanent attention warning (C2), a residue sweep that
inventories 5,270 candidates a day and holds all of them (100,398 candidate rows so far), and a
`recovery_debt` refusal in card worktree cleanup for the four Kind A tasks that are Failed/Canceled.
Nothing in land admission, land recovery, dispatch, keep-six or the pipeline view reads these rows.

## 1. How many rows, by state and age

### `AgentTaskLandings` (1,150 rows)

`Active` means "the task's current landing row" (one per task; a replacement deactivates the previous
at `server/Application/Services/AgentTaskLandRequestWriter.cs:211-215`). It is not an in-flight flag:
961 of the 985 `Phase=Complete` rows are `Active=true`, and only `Refused` rows are ever
`Active=false`. The CARD-1432 rule correctly keyed on `Phase`, not `Active`.

| Active | Phase | Publication | Cleanup | Mode | n | oldest `UpdatedAt` | newest `UpdatedAt` |
|---|---|---|---|---|---|---|---|
| true | 3 Prepared | Unconfirmed | NotStarted | Fresh | 1 | 2026-10-22 01:19 | live land during the query |
| true | 5 TargetAdvanceStarted | Unconfirmed | NotStarted | ResumePublication | 2 | 2026-09-16 00:12 | 2026-09-16 00:24 |
| true | 6 LocalTargetAdvanced | Unconfirmed | NotStarted | Fresh/Resume | 2 | 2026-09-25 13:57 | 2026-09-25 16:32 |
| true | 7 PushStarted | Unconfirmed | NotStarted | Fresh | 1 | 2026-09-26 03:01 | 2026-09-26 03:01 |
| true | 9 CleanupStarted | Landed/AlreadyPresent | **Refused** | Fresh 135, Resume 6, CleanupRetry 4 | **145** | 2026-09-09 23:51 | 2026-10-20 08:13 |
| true | 10 Complete | Landed/AlreadyPresent | Complete | | 961 | 2026-09-09 | 2026-10-22 |
| true | 11 Refused | Unconfirmed/Refused | NotStarted | | 19 | 2026-09-09 | 2026-10-20 |
| false | 11 Refused | | | | 24 | | |

Kind A by refusal reason (`LastReason`) and owning task status:

| `LastReason` | n | Task status | `DirectoryRemoved`/`RegistrationRemoved` |
|---|---|---|---|
| `ignored_content_preserved` | 127 | 126 Succeeded, 1 Canceled | 0 / 0 |
| `evidence_retention_unavailable` | 7 | Succeeded | 0 / 0 |
| `worktree_removal_incomplete` | 6 | 5 Succeeded, 1 Failed | 6 / 6 (directory and registration gone, branch kept) |
| `cleanup_additional_budget_expired` | 4 | 3 Succeeded, 1 Failed | 0 / 0 |
| `nested_registration` | 1 | Failed | 0 / 0 |

Age of the 145: 102 older than 21 days, 35 between 7 and 21 days, 8 under 7 days. All 145 are the
task's `ActiveLandingId`; 138 have a land request bound to them and every one of those requests is
`Completed` with no `TerminalFailureCode`; 7 (2026-09-09..11) predate request binding. No task has a
`TaskWorktreeRetirements` row (that table is empty). All 145 worktree paths are under
`C:\Antiphon\worktrees\`. Of the 1,043 `WorktreeCleanupAttempts` rows, the 124 bound to these
operations all have `RetryIntentAt` null: no cleanup was ever retried.

Kind B rows (all `LandAttempt=1`, request `State=Completed`, `ChildProcessId` null, task Succeeded):

| Task | Phase | Schema | `LastReason` | `UpdatedAt` |
|---|---|---|---|---|
| 7f3e581c | TargetAdvanceStarted | 2 | `source_changed` | 2026-09-16 00:12 |
| 73a2660b | TargetAdvanceStarted | 2 | `land_request_identity_conflict` | 2026-09-16 00:24 |
| 68a64314 | LocalTargetAdvanced | 2 | `source_changed` | 2026-09-25 13:57 |
| 78c5f868 | LocalTargetAdvanced | 2 | `remote_changed_before_push` | 2026-09-25 16:32 |
| fc89bde9 | PushStarted | 3 | `push_rejected` | 2026-09-26 03:01 |

### `AgentTaskSyncDebts` (118 rows; CARD-1082, first row 2026-10-07)

| State | `ReasonCode` | n | Runner | `ConfirmedSha = SourceSha` | Attempts | Created |
|---|---|---|---|---|---|---|
| 0 Pending | `runner_sync_inspection_unavailable` / `runner_sync_lease_busy` (alternating) | 1 | server2 | no | 2,080 and rising | 2026-10-14 08:09 |
| 1 Ready | `settlement_sync_ready` | 110 | 91 server2, 19 server2-temp | **yes, all 110** | 1..45, mean 5.7 | 2026-10-07..21 |
| 3 Superseded | `settlement_sync_superseded_by_retirement` | 7 | | | 3..5 | 2026-10-07..21 |

`AgentTaskSyncDebtState` has no state named Confirmed (`server/Domain/Entities/AgentTaskSyncDebt.cs:7-13`).
`Ready` is set only when the desktop and remote both equal `SourceSha`
(`server/Application/Services/SettlementSyncRecoveryService.cs:152-155,174-180`), and the attention
projection says "Ready and Superseded warn nothing" (`server/Application/Services/SettlementSyncDebtAttention.cs:9-11,64-71`).
The sweep selects only Pending and Held (`SettlementSyncRecoveryService.cs:36-40`). Ready is paid.

### Other CARD-1432 protections on terminal tasks

| Rule | Tasks | Detail |
|---|---|---|
| Land request Queued/Held/Running/NeedsResolution | 7 | 6 `NeedsResolution` requests (5 from 2026-09-17..22 also unadopted, 1 from 2026-10-20) plus 1 recovery-source link |
| `SourceResolutionState=AdvanceStarted` and `RecoveryAdoptedAt` null on a `Completed` request | 9 | 2026-10-01..20; 7 Succeeded, 2 Failed tasks |
| Active retirement | 0 | table empty |

### Reconstructing the CARD-1263 protected set

Terminal tasks (Succeeded/Failed/Canceled) with a `C:\Antiphon\worktrees\` path: 5,268. Protected
under the rule as applied: **278** (CARD-1263 saw 92 because the other 186 worktrees were already
gone from disk). With `Ready` debts no longer counted: **168**. With Kind A also excluded: **23**
(the 7 open requests, 9 unadopted resolutions, 5 Kind B rows, 1 Pending debt, 1 live land). No task is
protected by both a `Ready` debt and a Kind A landing.

## 2. Which path should have closed each kind, and why it did not

### Kind A: published, cleanup refused (145)

Mechanism, from code:

1. After publication the protocol runs cleanup and records the result at
   `server/Application/Services/AgentTaskLandingProtocol.cs:436-446`: `Cleanup = Refused` when the
   guarded removal is not clean, and the transition to `Complete` happens **only** when it is clean
   (`:446`). The state machine's only exit from `CleanupStarted` is `Complete`, which requires
   directory, registration and branch all removed (`server/Application/Services/AgentTaskLandingState.cs:39-40`);
   `Refused` is forbidden once publication exists (`:41`).
2. The land request still completes: `CompleteRequest` sets `State=Completed`
   (`server/Application/Services/AgentTaskLandService.cs:1762-1768`) and the outcome event is
   `LandedWithResidue`. The DB agrees: all 138 bound requests are Completed with no failure code.
3. Residue is intentional. `ignored_content_preserved` is the CARD-0452/0665 ignored-content gate
   refusing to delete a tree with retained ignored content (`server/Infrastructure/Git/GuardedWorktreeRemoval.cs:42,115,132`);
   `evidence_retention_unavailable` is the retention service being absent
   (`server/Application/Interfaces/IWorktreeEvidenceRetention.cs:34`); `cleanup_additional_budget_expired`
   is the cleanup time budget (`server/Infrastructure/Git/WorktreeGuardedCleanup.cs:109-111`).
4. The designed closer is a cleanup-only land request (`CleanupOnly=true`,
   `Origin=ScheduledCleanup`) created by `RequestCleanupRetryCoreAsync`
   (`AgentTaskLandService.cs:352-400`), which the protocol runs in `CleanupRetry` mode
   (`AgentTaskLandingProtocol.cs:144-149`). Its two callers:
   - the daily residue sweep's Publication lane, which selects exactly these rows
     (`server/Application/Services/WorktreeResidueSweepService.cs:165-168`) but only queues a retry
     when `execute` is true (`:241-249`), otherwise `Held`/`publication_retry` (`:260-262`). Shipped
     `WorktreeResidue:Execute` is `false` (`server/appsettings.json:215`; "activation is a
     commissioned deploy", `docs/orchestration-loop.md:2189-2190`; `docs/bootstrap.md:539`). The
     last 8 runs (2026-10-14..21, 09:00 UTC daily) all have `Execute=false`, 4,401..5,270 candidates,
     `Queued=0`, everything Held; the Publication lane has 3,541 `Held/publication_retry` candidate
     rows and nothing else.
   - card-done cleanup (`server/Application/Services/CardWorktreeCleanupExecutor.cs:226-227`),
     which never admits a target while `CardDoneAfterUtc` is null
     (`server/Application/Services/CardWorktreeCleanupPolicy.cs:15`; `server/appsettings.json:216`):
     `CardWorktreeCleanupTargets` and `CardWorktreeCleanupEndpoints` are both empty.

   Result: of 1,402 land requests, 0 have `CleanupOnly=true`. Nothing has ever retried a refused
   cleanup, so the rows sit at the only state the model allows for them.

Not the cause: restart during land (`ChildProcessId` null on all 145, requests Completed), the
CARD-0331 restart-unsafe queue (no `TerminalFailureCode`, `LandAttempt` 1), or an exception path
(`InterruptedAfterPublication` sets `Cleanup=Pending`, `AgentTaskLandService.cs:1299-1302`; none
of the 145 is `Pending`).

### Kind B: mid-phase refusal without the Refused transition (5)

The protocol's refusal handler at `AgentTaskLandingProtocol.cs:372-393` stores `LastReason` on
every refusal but transitions to `Refused` only for an enumerated set of (phase, reason) pairs:
`RebaseStarted`; `Inspected`/`RecoveryPinned`/`Prepared` without a lock code;
`landing_schema_superseded`; schema 3 `remote_changed_before_push` at `Verified`/`PushStarted`;
schema 1/2 `remote_changed_before_push`/`source_changed` at `LocalTargetAdvanced`/`PushStarted`.
The five rows fall outside it: `source_changed` and `land_request_identity_conflict` at
`TargetAdvanceStarted` (schema 2), `source_changed`/`remote_changed_before_push` at
`LocalTargetAdvanced` on schema 2 (should match the last arm; see uncertainty 2), and `push_rejected`
at `PushStarted` on schema 3 (`:355`, not in the list). The request was completed as refused by
the land service, so nothing later revisits the operation. Each task has a Succeeded status and
one landing row; the tasks are Sep 2026 (CARD-0688 schema-2 era) and no new row has appeared since
2026-09-26, so this is historical rather than ongoing.

### Kind C1: `Ready` debts (110)

Closed correctly by `SettlementSyncRecoveryService.SaveAsync` (`:174-180`). The CARD-1432 rule
("the task of an `AgentTaskSyncDebts` row in Pending/Ready/Held",
`docs/investigations/2026-10-21-card-1432-desktop-repo-maintenance.md:111`) and therefore the
CARD-1263 classifier treated the success state as open. 110 of CARD-1263's "111 tasks with open
sync debt" are this.

### Kind C2: the `Pending` debt that never ends (task 76fe1072)

Transcript of events (`GET /api/agent-tasks/76fe1072`): the first settlement at 2026-10-14 08:02
reported `Runner sync unavailable: runner_sync_inspection_unavailable`; the caller accepted at
08:05; the second settlement at 08:09 minted the debt with `runner_sync_lease_busy` ("synced
later"). Since then the sweep has claimed it 2,080 times. Code path:

1. The sweep claims any `Pending` row whose `NextAttemptAt` is due
   (`SettlementSyncRecoveryService.cs:36-40,101-130`) and bumps `Attempts` with
   `BackoffMinutes` capped at 5 (`:281`). There is no attempt ceiling and no age ceiling.
2. The inspection throws, which `RemoteWorkspaceService` turns into
   `RemoteSettlementSyncState.Unavailable` / `runner_sync_inspection_unavailable`
   (`server/Application/Services/RemoteWorkspaceService.cs:444-450`); a busy lease yields
   `runner_sync_lease_busy`.
3. `SaveAsync` treats as `Held` only `Refused`, `NotApplicable`, `BaselineUnavailable` and
   `BranchNotPushed` (`:156-158`). `Unavailable` and `LeaseBusy` fall to the final `else`, which keeps
   the row `Pending` and reschedules it (`:186-191`). `RegistrationGoneAsync` would supersede it only
   if the directory were missing or a retirement existed (`:271-279`); the directory exists and the
   retirement table is empty.

So the row loops at 5-minute cadence forever. The desktop-side reason the inspection fails is not
visible from the runner (uncertainty 3).

## 3. Does anything beyond cleanup read these rows?

| Consumer | Reads | Effect of the stale rows |
|---|---|---|
| Attention feed | `AttentionService.cs:3561-3562` selects Pending/Held debts; Pending warns after 30 min | One standing `SessionDisagreement` "Desktop sync debt" item for 76fe1072 (verified in `GET /api/attention?boardId=8988ca03…`, 1 of 230 items). Ready debts and landings produce none. |
| Residue sweep | `WorktreeResidueSweepService.cs:165-168,236-262` | Every daily run re-inventories the 145 Kind A rows and writes a Held candidate per row (3,541 rows to date; 100,398 candidate rows across lanes). Cost and noise only. |
| Card worktree cleanup (desktop) | `CardWorktreeCleanupExecutor.cs:199-201,217-222` | Succeeded tasks with a Kind A row are eligible for a publication cleanup retry (the intended path, currently gated off); the 4 Failed/Canceled Kind A tasks return `recovery_debt`. Kind B tasks also return `recovery_debt`. |
| Mirror cleanup (runner) | `CardWorktreeMirrorCleanupService.cs:165-167` | Kind A rows have `RemoteConfirmedAt`, so they do not block; Kind B rows do (`recovery_debt`). |
| Land admission | `AgentTaskLandService.cs:178-194` (published → inherit, cleanup retry), `:602-607` (resume only for the same request) | A fresh `-Land` on a Kind A task becomes a `CleanupRetry`, which is correct. A recovery-mode request on a Kind B task is refused `publication_unconfirmed` (`:184-187`); an ordinary request on a schema-2 Kind B row is refused `landing_schema_superseded` and then marked Refused (`AgentTaskLandingProtocol.cs:101-103,383`), which self-heals it. |
| Land monitor / recovery | `AgentTaskLandMonitorService.cs:36` | Reads the landing only for a pending request; none of the stale rows has one. No effect. |
| Dispatch, keep-six, pipeline view | grep of `server/Application/Services` for `AgentTaskLandings`/`AgentTaskSyncDebts` | No reader outside land, cleanup, retention, attention and task detail. No effect on admission counts or seats. |
| Data retention | `DataRetentionService.cs:429` | Any landing row for a member of a root tree makes the tree ineligible; this applies to all 1,150 landings, not only the stale ones. The stale rows do not change that outcome. |
| Task detail | `AgentTaskService.cs:2788-2789,2872-2875` | Shows `landing`/`syncDebt` as stored. Display only. |

## 4. Safe sweep rule and fix (not done, noted)

- Sweep rule, Kind A: these are not stale; the row is correct and the worktree is protected by
  design. Treat `Phase=CleanupStarted` and `Cleanup=Refused` with a `Completed` bound request as
  terminal for cleanup purposes, and enable the designed retry (`WorktreeResidue:Execute=true`,
  25 actions/day) once `ignored_content_preserved` is reviewed, or add an explicit terminal cleanup
  outcome (for example `Cleanup=Abandoned`) that an operator sweep may set.
- Sweep rule, Kind B: a 5-row one-off; `Phase in (5,6,7)`, `Publication=Unconfirmed`,
  `RemoteConfirmedAt` null, bound request `Completed`, task terminal, older than 7 days may be set
  to `Phase=Refused, Publication=Refused`. Code fix: extend the terminal rule at
  `AgentTaskLandingProtocol.cs:380-388` to cover `source_changed`/`land_request_identity_conflict`
  at `TargetAdvanceStarted` and `push_rejected` at `PushStarted`.
- Sweep rule, Kind C1: no sweep; fix the CARD-1432/1263 classifier to count only Pending and Held.
- Sweep rule, Kind C2: move a `Pending` debt to `Held` after N attempts or after the
  `RunnerSyncDebtAttentionMinutes` window has long passed; in `SaveAsync` treat
  `RemoteSettlementSyncState.Unavailable` as `Held` (hourly recheck) instead of `Pending`.

## Uncertainties

1. Desktop overrides of `WorktreeResidue:Execute` and `CardDoneAfterUtc` (user secrets or an
   environment-specific appsettings on the AppHost) are not visible from the runner. The DB evidence
   (every run `Execute=false`, 0 cleanup-only requests, 0 card-done targets) says the effective
   values match the shipped ones.
2. Two Kind B rows (`source_changed`/`remote_changed_before_push` at `LocalTargetAdvanced`, schema 2)
   look covered by the last arm of the terminal rule. Either operator precedence in that `||`/`&&`
   chain (`&&` binds `op.SchemaVersion != 3 && (...) && op.Phase is ...` before `||`; it reads as
   intended) or the refusal was raised on a path that bypasses the handler. Reconstructing needs the
   2026-09-25 server log.
3. Why the desktop inspection of `C:\Antiphon\worktrees\card-task-76fe1072` throws is a desktop-side
   fact (git state of that checkout, server log entry "Settlement sync for task … could not inspect
   its checkout"). Not reachable from this mirror.
4. CARD-1263's 92 on-disk worktrees could not be matched one-to-one to the 278 protected tasks
   because the classification CSV is on the desktop; the counts above are the DB-side superset.

## Not done, noted

- No fix designed or applied; no database write; no sweep run.
- Fix candidates are the four one-liners in section 4.
