# CARD-0664 — workspace-use consumer slots are never released (investigation)

Date: 2026-09-24 · Stage: Investigate · Runner: server2 (Linux) · Source: `c8f4cdf9`

Fix: [the CARD-0664 plan](../superpowers/plans/2026-09-24-card-0664-consumer-slot-release-plan.md)
(owner-liveness claim with grace, explicit owner-end releases, startup and residue-run reconcile).

## Verdict

**Confirmed.** Every workspace-use `Launch` reservation that production code takes stays
`Active = true` for good. The only method that deactivates one,
`WorkspaceReservationJournal.ReleaseConsumerAsync`, has no production caller. Only tests call it.
`TryClaimRetirementAsync` refuses a claim when any active non-Retirement row overlaps the worktree,
whatever that row's owner status. So a SettledTask-lane retirement of a worktree that was ever
launched, answered, refined or land-requested is refused, and it will keep being refused.

I reproduced this for the land and answer paths against a real Postgres (Testcontainers). The
delegate session-launch path and the dispatch path are reconstructed from code. The dispatch
repro did not reach admission in the fixture; see below.

The leak does **not** cause the `cleanup=Refused` that lands report. That refusal is
`ignored_content_preserved` from the guarded remover, which never consults reservations. The leak
blocks the *backstop* (the settled-task retirement sweep) that would otherwise pick those
worktrees up. So worktree accumulation has three stacked gates, and fixing this card removes only
one of them (see "Relation to worktree accumulation").

## Mechanism

1. **Acquire.** `WorkspaceUseAdmission.RequireConsumerAsync`
   (`server/Application/Services/WorkspaceUseAdmission.cs:16-21`) calls
   `journal.TryAdmitConsumerAsync` and **throws away the returned snapshot** (id + generation).
   The method returns `Task`, so no caller can ever hold the handle it would need to release.
2. **Persist independently.** `TryAdmitConsumerAsync`
   (`server/Infrastructure/Data/WorkspaceReservationJournal.cs:21-55`) opens its **own DI scope,
   DbContext and transaction** (`:23-25`) and commits the `Launch` row (`:51-53`). The caller's
   later rollback or exception cannot undo it. The answer repro below leaked a row even though
   `AnswerAsync` then threw `ConflictException`.
3. **Never release.** The only code that sets `Active = false` on a `WorkspaceUseReservations` row
   is `ReleaseConsumerAsync` (`WorkspaceReservationJournal.cs:117-126`). I grepped every `.cs` file
   under `server/` and `src/`: nothing else writes that table. There is no `ExecuteUpdate` and no raw
   SQL. Callers of `ReleaseConsumerAsync`:
   - `tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs:80`. This test releases the
     row **by hand** to simulate what production is assumed to do, then asserts retirement wins.
   - `tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs:310`.
   - No production caller.
4. **Refuse retirement.** `TryClaimRetirementAsync` (`WorkspaceReservationJournal.cs:57-69`) loads
   every active row, keeps those that are `Same` as the worktree key, and returns
   `workspace_in_use` if any overlap is not this retirement's own row (`:65-69`). It does not check
   `TaskId`, task status or session status. `Same`
   (`server/Application/Dtos/WorktreeRetirementDtos.cs:107-114`) needs an equal canonical path.
   An empty ref, or a CommonDirectory equal to the path, is treated as compatible, so a
   Cwd-only session row matches too.
5. **Downstream.** `TaskWorktreeRetirementService.ClaimAsync`
   (`server/Application/Services/TaskWorktreeRetirementService.cs:386-408`) returns `false`.
   `TryRetireAsync` then returns residue `retirement_claim_refused` (`:293-294`), and the sweep
   records the candidate as `Refused` and persists a cooldown
   (`server/Application/Services/WorktreeResidueSweepService.cs:204-210`). If the claim were somehow
   won, `FindLiveConsumersAsync` (`WorkspaceUseAdmission.cs:31-37`) would still return the same
   leaked `Launch` rows (their `RetirementId` is null), giving `live_owner`
   (`TaskWorktreeRetirementService.cs:296-305`).

## Every acquisition and its intended release

"Keys on worktree" means the row's canonical path is the task's `card-task-*` leaf, so it blocks
that leaf's retirement. A row keyed on the repository root does not block leaf retirement. It
still leaks and grows the table.

| # | Site | Trigger | Key at acquisition | Attribution | Blocks leaf retirement? | Release in prod |
|---|------|---------|--------------------|-------------|-------------------------|-----------------|
| A1 | `AgentTaskService.cs:3784-3793` via `CreateAsync` `:1304` | every task create | `WorktreePath ?? WorkingDirectory`: repo root for a new Worktree task; the target dir for Shared/ReadOnly `-Dir <worktree>` (e.g. `docs/orchestration-loop.md:1279` ordinary Review `-ReadOnly -Dir '<code-worktree>'`) | TaskId | Only when WorkingDirectory is a worktree leaf | none |
| A2 | same helper via `RequeueAsync` `:2663` (retry/reroute/escalate) | requeue | the task's `WorktreePath` once it exists | TaskId | **yes** | none |
| A3 | same helper via `CreateMergeTaskAsync` `:2902`, `CreateCommitTaskAsync` `:3035` | merge/commit tasks | their WorkingDirectory | TaskId | when that dir is a leaf | none |
| A4 | `AgentTaskDispatcher.cs:3958-3968` | dispatch claim, **before** `CreateForTaskAsync` `:4097` | first dispatch of a Worktree task: `WorktreePath` still null, so repo root; re-dispatch: the leaf | TaskId | re-dispatch only | none (also survives the dispatch tx rollback, point 2) |
| A5 | `AgentSessionService.cs:2950-2993` via `StartAsync` `:189`, `LaunchInteractiveProcessAsync` `:455`, `ResumeInterruptedLaunchAsync` `:660`, `ResumeAsync` `:1439` | **every delegate session launch/resume**: dispatcher sets `session.Cwd = claimed.WorktreePath ?? WorkingDirectory` (`AgentTaskDispatcher.cs:4218`) and enqueues (`:4355`) into `LaunchInteractiveProcessAsync` | `session.Cwd` (the desktop leaf, even when the process runs on a remote runner) plus the task's branch/repo | SessionId only (`TaskId = null`) | **yes, for every dispatched Worktree task** | none |
| A6 | `AgentTaskLandService.cs:89-94` in `RequestAsync` | every land request that passes the status checks, including ones that later 409 | the task's own leaf | TaskId | **yes, always** | none |
| A7 | `AgentTaskReplyService.cs:3971-3981` via `AnswerAsync` `:299`, `RefineAsync` `:485` | answer/refine | the leaf | TaskId | **yes** | none |
| A8 | `AgentControlService.cs:952-956` | adopt a Herdr pane | the pane cwd | **neither TaskId nor SessionId** | when cwd is a leaf | none, and it cannot be attributed later |
| A9 | `WorkspaceUseAdmission.FenceCompletedAsync` `:26-29` (from `TaskWorktreeRetirementService.cs:340-344`) | retirement Complete | the retired leaf, `HistoricalFence` | RetirementId | n/a | none: **intentional**; a permanent fence against reuse of a retired path |

Intended release point: no production code, doc or plan states one. The CARD-0459 design built
`ReleaseConsumerAsync(id, generation)` as the release primitive. Nothing was ever wired to call it
at task settlement, session end or land completion, and `RequireConsumerAsync`'s `Task` return type
throws away the handle it would need.

Consequence: A5 alone covers **every** dispatched Worktree task (A6 covers every landed one). So,
from code, no ordinary delegate worktree can be retired by the SettledTask lane once it has run.

## Reproduction (Testcontainers Postgres, server2)

I added three temporary tests to `WorktreeRetirementRaceTests` (they use the existing `RaceWorld`
fixture). Each drives one production path, marks the task terminal, then calls the production
`TryClaimRetirementAsync`. The healthy expectation `claim.Accepted == true` was asserted. The
tests were **not committed**, and the file was restored with `git checkout`. Build: one isolated
build (`--property:OutputPath=bin-inv664/ UseAppHost=false`, 3m13s, then a 1m23s incremental
rebuild to print the dispatch failure reason). Filter:
`/*/*/WorktreeRetirementRaceTests/C664_*`. The `bin-inv664` directories were deleted.

| Test | Path driven | Observed | Result |
|------|-------------|----------|--------|
| `C664_Repro_LandAdmissionLeaksAndBlocksRetirement` | `AgentTaskLandService.RequestAsync` on a Succeeded Worktree task | `activeRows=1 Launch/task=<owner>/ref=refs/heads/feat/card-task-land/released=` (null) → `retirementAccepted=False reason=workspace_in_use` | **red (leak reproduced)** |
| `C664_Repro_AnswerAdmissionLeaksAndBlocksRetirement` | `AgentTaskReplyService.AnswerAsync` on a Blocked task; task then set Succeeded | `answerError=ConflictException: The delegate's session is no longer available.`, **yet** `activeRows=1 Launch/task=<task>` → `retirementAccepted=False reason=workspace_in_use` | **red (leak reproduced, including leak-on-refused-operation)** |
| `C664_Repro_DispatchAdmissionLeaksAndBlocksRetirement` | `AgentTaskDispatcher.TickAsync` | task `Failed: Dispatch failed before a session existed: git_exit_128` (fixture dir is not a git repo), `activeRows=0`, retirement accepted | **inconclusive**: failed before admission `:3964`; dispatch/session paths (A4/A5) not reproduced |

Totals: 3 run, 2 failed as the leak predicts, 1 inconclusive. Windows-only (ConPTY/pty-host) tests
were not in scope and did not run.

## Relation to worktree accumulation (~650 worktrees)

Live evidence, read-only over `$ANTIPHON_API` (`GET /api/agent-tasks?status=Succeeded&since=2026-09-19`
then `GET /api/agent-tasks/{id}` → `landing`): 278 Succeeded Worktree tasks since 2026-09-19.

| landing.publication / cleanup / reason | tasks |
|---|---|
| none (never landed: Plan/Review/Investigate/TestDesign worktrees, unlanded Code) | 188 |
| Landed / **Refused / `ignored_content_preserved`** | **57** |
| Landed / Complete | 29 |
| Unconfirmed / NotStarted / `rebase_conflict` | 3 |
| AlreadyPresent / Complete | 1 |

For example, CARD-0653 Code task `2060e274` (landed as `e33fb16c`) shows `phase=CleanupStarted,
cleanup=Refused, reason=ignored_content_preserved`.

Three independent gates keep a worktree alive:

1. **Land cleanup (Publication lane): not affected by this leak.** `AgentTaskLandingProtocol.cs:366-405`
   calls `GuardedWorktreeRemoval`, which refuses whenever `git ls-files --others --ignored
   --exclude-standard` returns anything (`GuardedWorktreeRemoval.cs:61-66`, `:252`;
   `LandingGit.cs:144-152`). `.gitignore` ignores `.antiphon/` (`:55`), `bin/`, `obj/` and `bin-*/`.
   Every delegate worktree receives `.antiphon/inbox/<brief>.md`; this one has it. Any build leaves
   `obj/`. So 57 of 86 (66%) confirmed lands leave residue. The residue sweep's Publication lane only
   re-queues the same cleanup (`WorktreeResidueSweepService.cs:235-258`), so the same refusal
   presumably repeats. That repetition is inferred, not measured. **This is the dominant cause of
   `cleanup=Refused` and deserves its own card.**
2. **SettledTask lane authority.** A task is only retired with an explicit active release
   (`POST /api/agent-tasks/{id}/worktree-retirement`, created only by `scripts/worktree-residue.ps1`;
   otherwise `release_required`). The sweep also retires nothing unless `WorktreeResidue:Execute` is
   true (default `false`, `docs/bootstrap.md:481`). The 188 never-landed worktrees have no other
   route at all.
3. **This card.** Even with a release and `Execute=true`, the claim for any worktree that ran a
   session (A5) or was land-requested (A6) is refused with `workspace_in_use` → `retirement_claim_refused`.

So the leak makes the backstop permanently unable to act on essentially every delegate worktree.
Fixing gates 1 and 2 without this one would still leave every tree `Refused` at claim time.

Secondary effect: `TryAdmitConsumerAsync` and `TryClaimRetirementAsync` both
`Where(r => r.Active).ToListAsync()` over the **whole table** (`WorkspaceReservationJournal.cs:26-28`,
`:62-64`) and filter `Same` in memory. With nothing ever released, every create, dispatch, launch,
answer and land does O(all rows ever admitted) work, and that cost grows without bound.

## Could not measure

- **Desktop DB not reachable from server2.** There is no `psql` in the container, no route to the
  desktop Postgres, and no API endpoint exposes `WorkspaceUseReservations`, so I could not count
  active Launch rows on terminal tasks. Read-only SQL for the operator to run on the desktop:

```sql
-- active rows by kind (0=Launch, 1=Retirement, 2=HistoricalFence)
SELECT "Kind", count(*) FROM "WorkspaceUseReservations" WHERE "Active" GROUP BY 1;
-- active Launch rows whose task is terminal (4=Succeeded, 5=Failed, 6=Canceled)
SELECT count(*) FROM "WorkspaceUseReservations" r JOIN "AgentTasks" t ON t."Id" = r."TaskId"
 WHERE r."Active" AND r."Kind" = 0 AND t."Status" IN (4,5,6);
-- active Launch rows whose session is terminal (4=Stopped, 5=Failed)
SELECT count(*) FROM "WorkspaceUseReservations" r JOIN "AgentSessions" s ON s."Id" = r."SessionId"
 WHERE r."Active" AND r."Kind" = 0 AND s."Status" IN (4,5);
-- unattributable active Launch rows (A8)
SELECT count(*) FROM "WorkspaceUseReservations" WHERE "Active" AND "Kind" = 0 AND "TaskId" IS NULL AND "SessionId" IS NULL;
-- ever released (expected 0 outside tests)
SELECT count(*) FROM "WorkspaceUseReservations" WHERE "ReleasedAt" IS NOT NULL;
```

## Remaining uncertainties

- A4 and A5 (dispatch and session launch) are reconstructed from code, not reproduced. The
  dispatch fixture fails at `git_exit_128` before admission. Confidence is high: A5's key
  resolution (`AgentSessionService.cs:2968-2991`) falls back to `t.WorktreePath == path`. Even with
  no task match, a Cwd-only key is `Same` as any ref at that path.
- The production value of `WorktreeResidue:Execute` is unknown. If it is false, the sweep has never
  attempted a SettledTask retirement, and no `retirement_claim_refused` rows will exist yet even
  though the leak is real.
- Which ignored paths trip the 57 `ignored_content_preserved` refusals: `.antiphon/` vs `obj/` vs
  others. The stored reason does not list paths.
- The ~650 figure is the caller's number and was not measured here. The worktree root is on the
  desktop.

## Fix design (input to Plan; not implemented)

Goal: a Launch row lives exactly as long as its owner. Recommended shape:

1. **Owner-derived liveness at the decision point.** In `TryClaimRetirementAsync` and
   `FindLiveConsumersAsync`, treat a `Launch` row as blocking only if its owner is live: `TaskId`
   task in Queued/Dispatched/Working/Blocked, or `SessionId` session in
   Created/Starting/Running/Stopping. Unattributed rows (A8) block only within a bounded age. This
   is self-healing for the existing backlog and needs no migration, and it keeps the reservation
   useful as a same-transaction fence against a launch racing a claim. It must run inside the claim
   transaction, and the owner status must be read with the same lock discipline, or an owner could
   re-launch between check and claim.
2. **Explicit release at owner end (hygiene and table size).** Have `RequireConsumerAsync` return
   the snapshot. Release it: A5 at session terminal transition; A1/A2/A4/A7 at task terminal
   settlement (release all active rows with that `TaskId`); A6 in a `finally` when the land request
   reaches its outcome, or immediately when `RequestAsync` throws after admission. Also stop
   admitting before a failure-prone step: A4 before the worktree exists, A7 before the session
   check.
3. **One-time backfill** marking `Active=false, ReleasedAt=now` on rows whose owner is terminal.
   Only needed if (1) is not adopted, or to shrink the table scan.
4. Give A8 an attributable owner (the adopted session id), or drop its row when adoption ends.

Plan must decide 1 vs 2 vs both. (1) alone fixes the refusal. (2) alone leaves the existing
backlog unless (3) also ships.

## Red tests for the Plan / TestDesign stage (not written; described)

All go in `tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs` on the existing
`RaceWorld`. Each is red at `c8f4cdf9` and should go green with the fix.

- `C664_LandRequestThenTerminal_RetirementClaimAccepted`: A6. Reproduced red above
  (`workspace_in_use`).
- `C664_AnswerRefusedAfterAdmission_LeavesNoActiveLaunch`: A7. Reproduced red above: the row
  survives a thrown `AnswerAsync`.
- `C664_SessionLaunchThenSessionStopped_RetirementClaimAccepted`: A5. Drive
  `AgentSessionService.LaunchInteractiveAsync` with the fake adapter and a Cwd equal to the leaf,
  then set the session Stopped and the task Succeeded, and claim.
- `C664_RedispatchThenTerminal_RetirementClaimAccepted`: A2/A4. Needs a real git repo in the
  fixture so dispatch passes `ProbeConfiguredDefaultAsync`.
- `C664_LiveOwnerStillBlocksRetirement`: guard. A `Launch` row whose task is Working must still
  make the claim return `workspace_in_use`. This keeps the CARD-0459 race intent.
- Keep existing `C459_*` tests green. `C459_RequeueReservesWorkspace` manually releases at `:80`;
  after the fix that manual release should be unnecessary.

## Not done, noted

- No code changed. The temporary repro tests were reverted and not committed.
- A separate card is recommended for gate 1: guarded removal refuses on *any* ignored path, and
  every delegate worktree has `.antiphon/inbox/` plus build `obj/`. That causes the 57/86
  `cleanup=Refused` lands.
