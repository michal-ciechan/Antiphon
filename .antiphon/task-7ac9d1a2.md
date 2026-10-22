# CARD-1446 milestone 1 (S1-S3) Final Review, task 7ac9d1a2

Subject: Code task `10e48480-1a3d-4449-bb14-023be014bf27`, `feat/card-task-10e48480` at
`7f87594033609fac73e098f36214b073281b1964` (the tested source is `aa4fa734f`; `7f8759403` adds only the report).
Base `d1693f6ee`. Lane: server2 runner mirror (linux). Verdict: **clean**. There are no regressions and no new reachable
fail-open. Six disclosures follow, each with a suggested Backlog follow-up.

## Receipt (checkpoint tool, one run at 7f8759403)

Run `20261022-061635-51f1`: `verdict: GREEN exit=0`. Result of `scripts/validate-checkpoint-receipt.ps1 -Rows CP-2,CP-4,CP-5,CP-6,RV-1`:
`CHECKPOINT SOURCE VALID source=7f87594033609fac73e098f36214b073281b1964 rows=5`. All rows show slot=granted, dirty=0, sourceState=clean
and buildSource=verified (CP-6 is notApplicable).

| Row | Filter | Result |
|---|---|---|
| CP-2 | `/*/*/AbandonedLandingReconcilerTests/*` | 7/7 (V-1 plus 6 guard rows) |
| CP-4 | `/*/*/(SettlementSyncRecoveryTests*)\|(SettlementSyncDebtAttentionTests*)/C1446*` | 4/4: the 3 V-3/V-4 methods and the V-5 method |
| CP-5 | `/*/*/SettlementSyncRecoveryTests/C1082*` | 10/10 |
| CP-6 | Select-String 'Pending or Held' command | exit 0 |
| RV-1 | `/*/*/LandRefRetentionTests/C1432_protection_rule_rows` | 32/32; the `debt-ready` case now expects False |

RV-1 is a legitimate unlisted row. Plan D-3 makes it conditional on `AntiphonRefProtection.cs` existing, and the file is on master.
Besides this run, the only build and test drivers were the tool build (build-slot) and the mutation build and run (build-slot). All
`bin-c1446r-*` output has been deleted, and the tree is clean.

## Red witnesses (read from the Code task's TRX)

- CP-1 at `ab1214259`: `RunAsync ... should be 1 but was 0`. The no-op skeleton caused it, which is the right reason; the plan's "phase
  still PushStarted" is the same root cause, seen one assertion earlier.
- CP-3 at `f8510082e`: `SweepSettlementSyncAsync() should be 0 but was 1`. The row was claimed and attempted, which is the right reason.

## Throwaway mutation (scratch detached worktree, discarded)

There were two independent mutations in two files: `debt.Attempts >= cap` changed to `>`, and `PushExitCode == 0` changed to `== -999`. Filter:
`/*/*/(SettlementSyncRecoveryTests*)|(AbandonedLandingReconcilerTests*)/C1446*` gave total 10 and failed 3:
`C1446_guard_rows(push-exit-zero)`, `C1446_PendingDebtAtAttemptCapIsHeldExhaustedWithoutGit` and
`C1446_LeaseBusyRunsToCapThenHolds`. These are exactly the expected cases; the other 7 passed. All PCs remain pending for the SourceLanding Mutation stage.

## Brief checks

1. **S1.** The query and the in-lock re-check both use `UpdatedAt < now - days`. Every protocol phase transition and save restamps
   `UpdatedAt`, so this is the right timestamp. The rule acts only on resumable unpublished phases, and only when the task is
   Succeeded/Failed/Canceled. It skips the task when any request has `IsPending`, which covers Queued, Held, Running and NeedsResolution, and when its current
   request is anything other than Completed, Superseded or Canceled. The `PushExitCode == 0` exclusion is in place, and
   `Transition(Refused)` still refuses a confirmed publication. The rule is idempotent: the second run returns 0 and writes no
   second event. The re-check happens under the `AgentTasks` row lock (`FOR UPDATE`), the same lock that
   `AgentTaskLandRequestWriter` and the protocol's `SaveAsync` take. The reconciler runs no Git, so it needs no lease. Its effects are
   visible as a Warning event, the log entry and the `landing_abandoned:` LastReason. They are reversible: `CanReplaceRefused` lets a later
   `-Land` replace the operation, and `AbandonedLandingDays=0` disables the reconciler.
2. **S2.** The cap is checked after the episode and registration guards and before the Attempts bump and any Git. A debt at
   Attempts 48 is held (`>=`). A debt at 47 is claimed and becomes 48, and the next claim holds it, so attempt 49 never runs Git.
   The counter is the persisted `Attempts` column, so it survives a restart. A Held debt is never dropped: the hourly restamp keeps it, the
   attention item names `attempts=` and the recovery sentence, and ref protection still covers Held.
3. **S3.** Each debt state traced: Pending is protected and still selected by the sweep. Held is protected and restamped hourly.
   Ready is set only when the desktop and the remote both equal `SourceSha`, and no writer moves a Ready row back (`SaveAsync` updates
   only `State == Pending` rows), so nothing still owed needs its refs. Superseded was already unprotected. "Paid" and "Abandoned"
   are not debt states. Every other CARD-1432 clause is unchanged, and RV-1 shows 32/32.
4. **Config.** `WorktreeResidue:AbandonedLandingDays` (default 7) and `Delegation:RunnerSyncDebtMaxAttempts` (default 48)
   both bind through their existing sections. Their validators reject negative values, and 0 disables each feature (tested: days-zero,
   `C1446_ZeroCapKeepsRetrying`). The job is registered when `WorktreeResidue:Enabled` is true, which is the default
   (`appsettings.json`).
5. **Tests** assert behaviour, and the mutations above go red.
6. **Deferrals.** Nothing on master references S4 or S5 behaviour. The only forward reference is the CARD-1432 plan text for the
   `Cleanup != Abandoned` clause, which is marked as S4c.

## Landability and rc

- Merge check: `git merge-tree --write-tree origin/master HEAD` against origin/master `daf19e530f0d` (which contains daf19e53) exits 0 with no conflicts.
- rc: I chained `merge-tree --merge-base` per commit onto `origin/rc/2026-10-20` (`ceb6e3d6b`). All S1/S2 commits
  (`47687718a`, `ab1214259`, `de4e8f59c`, `fe0c0d81e`, `f8510082e`, `c05f27a6d`, `aa4fa734f`) apply cleanly. S3 `f0beaae1c` gives
  modify/delete conflicts on `AntiphonRefProtection.cs` and both CARD-1432 docs, so S3 is master-only. I did not build on rc.
- `check-evidence-diff.ps1 -BaseRef d1693f6ee -HeadRef 7f8759403`: commits=11, entries=1, violations=0.

## Disclosures (non-blocking; suggested Backlog follow-ups)

- **D-a. A push with an unknown exit is refused after 7 days.** At `PushStarted` with `PushExitCode == null`, the push may have
  reached the remote without the operation recording it. This happens when the push threw (`landing_io_error`) or the process
  died inside `OwnedAsync`, and `ChildOperation` may still be set. The reconciler marks such an operation Refused. Only exit 0 gets
  the "human resolves an unknown publication" treatment (`AbandonedLandingReconciler.cs:82`).
  - Why this is not a fail-open: Refused still cannot follow a confirmed publication. If the commit is on the remote, a later
    `-Land` finds it there and records AlreadyPresent. If it is not, the source branch is untouched. The recovery pins it unprotects
    can be rebuilt.
  - Follow-up: keep `PushStarted` when `PushExitCode is null or 0` or when `ChildOperation` is set, and add a guard row for it.
- **D-b. The reconciler shares the scoped `AppDbContext` with the sweep.** If `SaveChangesAsync` fails on the last candidate, the
  modified operation and the added event stay tracked. `WorktreeResidueSweepService.RunAsync`'s first `SaveChangesAsync` (`:154`)
  then re-saves them outside the task lock. If the failure was a conflict, that save fails the sweep too, which contradicts "its
  failure never fails the sweep". This needs a failed database write and is low-likelihood.
  - Follow-up: call `ChangeTracker.Clear()` in a `finally` in `RefuseAsync`/`RunAsync`, or give the reconciler its own scope.
- **D-c. Test gaps (adequacy, not stubs).** No guard row has a non-terminal task (for example Working) or a current request in
  Running/NeedsResolution without `IsPending`. No test shows that `WorktreeResidueJob` calls the reconciler. The optional
  constructor parameter is null-defaulted; reading the code shows that Microsoft DI does inject it, because it is registered.
- **D-d. An exhausted Held debt has no release path.** This is pre-existing for every Held reason (CARD-1082/1136): the hourly
  re-check runs no Git, so after an operator runs `git pull --ff-only` the debt and its attention item stay forever. Before S2 the
  same row would eventually have been paid on a retry; now it stops after about 3 h 50 min. The plan accepts this trade (D-2).
  - Follow-up: a Held re-check, or an operator route, that confirms desktop == `SourceSha` and moves the debt to Ready.
- **D-e. Living docs are stale (advisory).** `docs/orchestration-loop.md:1639` and `docs/ops-http.md:179` still describe unbounded
  Pending retries and say nothing of the `settlement_sync_attempts_exhausted` reason. `AbandonedLandingDays` is not documented
  anywhere outside the plan. The plan did not list these docs for S1/S2.
- **D-f. The plan's CP-4/CP-12/CP-13/CP-16 filter grammar is unparsable** (the Code report already notes this). The S4/S5 Code task
  must use the `(A*)|(B*)/M*` form.
