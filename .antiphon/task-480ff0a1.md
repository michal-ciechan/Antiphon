# CARD-1593 S3 Round A r2 Final Review (480ff0a1)

**Verdict: one defect (D-1).** The product fixes for A-1, A-2 and A-3 are correct. The new A-1 witness is flaky by construction: it was red 6/6 at the tip on loaded server2, and that includes the CP-4 class-only shape. The Unit lane did not finish inside the 150-minute wall cap, so the scope is **Interim**.

- Subject: `feat/card-task-fe424853` at `25e0d313cc0cd0d1329d36f1c5f51191d6c7ce24`. Round 2 is `515130f8a` and `25e0d313c`, on top of round A `8a8b9bde3` (task a8eb1f8b).
- The real base is `fe73cfd2eed86de506a6a307e870c44b05b94451`, not 86d21b50. Relative to fe73, the branch touches only capacity services and tests.
- The author's report is not on the branch. I read it from `GET /api/agent-tasks/fe424853`.
- `git merge-tree --write-tree origin/master(daebb762e) HEAD` exits 0 with tree `9cdae09b7`.
- `check-evidence-diff.ps1 -BaseRef fe73cfd2e -HeadRef 25e0d313c` exits 0: 15 commits, 0 entries, 0 violations.

## Runs

There was one slot build of `tests/Antiphon.Tests` (`UseAppHost=false`, `OutputPath=bin-r480ff0a1/`, lease ed4cc8e9, 188 s, 0 errors). Every run is `dotnet exec --no-build` inside `scripts/build-slot.ps1`. I did not use the checkpoint tool. The reader cap means there are no sealed receipts.

| Run | Selection | Result |
|---|---|---|
| Unit lane (lease 20414417) | `/*/*/*/*[Category=Unit]` | **Not completed.** It hit `timeout 9000` (exit 124) after 150 min with no TRX and no summary. One genuine red came before the cap: `RemoteScriptContractTests.C1192_Cleanup_consumer_requires_positive_absence`, a shell-harness 60 s timeout. Rerun alone at the tip it was green (1/1, 58.7 s, TRX f522e702). That test is not in the diff and is load-sensitive. The `C1008_*`/`C1514T_*` "failures" were printed after the SIGTERM (OperationCanceled, 0 ms, fail-fast), so they are artifacts of the cap. |
| Named integrations (lease 772475cf) | 12 classes, one invocation | **331/332**, TRX 55d6aede. Lifecycle 28, LockOrder 17 (incl. CP-6), WallReroute 15, ReceiptFailure 21, BaseGuard 97, StandingRestartEscalation 52, AgentSupervision 10, StandingRestartAccounting 21, AgentControlServiceIntegration 33, RecoveryQueue 8, RecoveryTask 6, RecoverySupervision **23/24** |
| CP map | | CP-1 45 green, CP-2 36 green, CP-3 97 green, **CP-4 red (D-1)**, CP-5 62 green, CP-6 green |
| Red witness (pre-fix: 515130f8a server files with the 25e0d313c test bytes) | `CapacityRecoverySupervisionTests/(…exit_without_attempt*)\|(…lost_commit*)` | 4/7. The expected three were red: `transient-40P01` (log `""`, so no 40P01), `not-committed` (no `RelationalTransaction.CommitAsync` frame), and `reread_bounds_the_tick` (25.07 s vs <12 s). TRX d417e223 |
| D-1 repro at the tip | method alone x3, class alone x1 | red 4/4, 25.12–25.21 s (class run 23/24) |

## D-1: the A-1 witness polls `pg_stat_activity` inside its own transaction (flaky false red)

- **Where.** `tests/Antiphon.Tests/Application/CapacityRecoverySupervisionTests.C1593S3.cs:1107`, which calls `RereadWaitingAsync(connection, tx)`, and the query at `:1153-1166`. Both are in `BoundedRereadStandIn`. The landing owner is the subject Code task, fe424853.
- **Failure.** At `25e0d313c`, `C1593_S3_supervisor_lost_commit_reread_bounds_the_tick` fails `elapsed < 12s` with 25.03–25.21 s. It did so in 6/6 runs on server2 under load: the integration run, alone x3, class-only (the CP-4 shape), and one diagnostic run. The author's CP-4 run passed (2.52 s) when the host was quieter.
- **Why.** PostgreSQL collects `pg_stat_activity` state and query once, at first access in a transaction, and shows the same data until that transaction ends. The stand-in polls inside the transaction that holds the agent row. If the reread has not yet issued its `FOR UPDATE` at the first poll (about 10 ms after the publish), the stand-in never sees it. `sawWaiter` stays false, and the stand-in holds the row until its 25 s safety, which blocks the tick.
  - The diagnostic copy, which left the subject untouched, logged in-transaction `waiting=False` with *no* active backends, even at release while the tick was still blocked.
  - The same build with the poll moved to a fresh connection was green: the waiter was seen at +0.12 s and cleared at +2.09 s (the 2 s bound), and the tick took 2.84 s.
  - The product bound itself works. Only the witness is defective.
- **Fix.** Poll `pg_stat_activity` on a separate connection with no open transaction, or run `SELECT pg_stat_clear_snapshot()` before each poll. Then rerun CP-4 while the host is loaded.

## Advisories A-1..A-3 (round 1)

- **A-1: closed.**
  - The reread bound is `LostCommitRereadTimeoutSeconds` (`AgentControlService.cs:75`, default 120). A value below 1 clamps to 120 (`:1064`).
  - 120 is the D-12 / `StalledAdmissionTimeout` grant bound, which is inside round 1's 2–5 min range.
  - A timeout is caught as a reread failure (`:910`). That path logs a Warning and rethrows the original exception to S-T or S-3. This is the plan's step-5 fallback (Risk 10: pass 1 ends the never-enqueued `Starting` row, and `RearmStalledAdmissionsAsync` re-arms at one attempt).
  - The witness asserts that a commit which really happened is not charged: `AdmissionCount` 1, `Admitted`, `lost-commit-applied`, grant cleared.
  - The reread is a fresh-scope leaf on rank 5. The in-process queue locks are still held across it, but the hold is now bounded.
  - The witness injects a 2 s bound and pins the 120 default. It is red at 515130f8a (reproduced).
- **A-2: closed.** `AgentSupervisorService.cs:269-273` logs a Warning with `{AgentId}` and `{SqlState}`. It still clears the tracker and returns false. The witness was red before the fix and is green at the tip.
- **A-3: closed.** The three sites use `ExceptionDispatchInfo.Capture(commitEx).Throw()` (`:901,915,921`). The exception object and its classification are unchanged, and the unreachable `return;` is harmless. The witness was red before the fix and is green at the tip.

## Whole-change priorities

Round 2 adds no lock and no IO inside the reservation, so the round 1 lock-order and exit-table conclusions (S-0..S-4, S-0c) still hold. The standing-restart classes (52 + 10 + 21 + 33) are green.

## New advisories (no rerun needed)

- **A-6.** After a reread timeout with the commit still unresolved, the fresh-scope S-3 bookkeeping and charge can queue behind that commit's row and provider locks. Each wait is bounded only by Npgsql's default 30 s `CommandTimeout`, and no override is configured. The tick is therefore bounded at about 120 s plus 30 s per blocked command, not at 120 s. That is pathological and not unbounded.
- **A-7.** In `ConsumeCardAdmissionAsync`, the `startedAt` read (`AgentControlService.cs:1022`) sits outside the S-0c `try`. A throw there takes S-3 (backoff plus charge) instead of S-0c. That is still one spend for one launch.
- **A-8.** `InDoubtStandIn` (`:1004-1016`) uses the same in-transaction poll. It falls back to committing after 30 s and still passes because the default bound is 120 s. Fix it together with D-1.
- **Unit lane.** This lane needs more than 150 min on loaded server2. The next Final Review should use the CARD-1573 shards or a quieter host.

## Cleanup

I removed all 33 `bin-r480ff0a1` directories and the scratch tree. I did not touch the subject branch or its worktree.
