# CARD-1076 S4 Code report (task be6f17af)

Outcome: S4 (D-6 queue reasons) is implemented and green on Linux. `GET /api/agent-tasks/pipeline` queueReason gains `repositoryLease` and `remotePrep` from the latest current-stint Held row. No Git is run. Existing reasons and the DTO shape are unchanged. The client (D-7) is not this slice. Tested source `776fa4464d0b8f76ecfce20d83b84ff380e50c6c`. Branch `feat/card-task-be6f17af`. Worktree `/work/worktrees/task-be6f17af`. Landing owner / original Code task `be6f17af`. Base `23dc94ea54d547eb23a8e0d4ba6400995d1bd8f5`. Plan `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md`. restart: none (projection, not activated).

`Progress().BehindTaskId` is the enqueue-time snapshot (CARD-1093). With three or more waiters it can name a finished pusher or be null. The API text, the `Progress()` doc, the `ToQueued` comment, and the push-turn test say it is not the exact current holder.

## Commits

- `ce3466070bac1498a7d18bba6b91e31f6ddcf507` CARD-1076 S4: name repository-lease and remote-prep queue reasons from the current stint
- `efdda96b15b78e1750b7d43ef5189a9f66eac887` CARD-1076 S4: give the push-turn waiter its own observation deadline
- `776fa4464d0b8f76ecfce20d83b84ff380e50c6c` CARD-1076 S4: wait for the blocked push before reading the waiter

## Behaviour

The stint floor matches `AgentTaskDispatcher.LoadQueuedHoldIndexAsync`: last `Dispatched` `At`, then the latest `Held` with `At` strictly greater. `DispatchHoldClass.Lease` becomes `repositoryLease`. `heldBy` is `DispatchHoldDetails.LeaseOwnerTaskId` on the owner sentence (`occupied by task <32 hex> purpose=`), else the earliest running land whose `ScopeResolver.KeyFor` equals the queued task's, else empty. A fenced journal that merely contains a 32-hex id does not parse (PC-10 inverse). `DispatchHoldClass.RemotePrep` becomes `remotePrep`. `heldBy` is one holder from `_remotePrep.Progress(task.Id).BehindTaskId`, else empty. Titles are one `AgentTasks` lookup. Precedence stays shared checkout, sibling land, dated pin, then these two, then host budget / concurrency cap, then `awaitingDispatch`.

## Files

- `server/Application/Services/AgentTaskPipelineStatusService.cs`
- `server/Application/Services/DispatchHoldDetails.cs` (`LeaseOwnerTaskId`)
- `server/Application/Services/RemoteWorkspacePreparer.cs` (doc only; behaviour unchanged)
- `docs/antiphon-api.md` (additive reason list, plus the CARD-1093 caveat)
- `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs` (`CreateService` accepts the preparer)
- `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusC1076Tests.cs`
- `tests/Antiphon.Tests/Application/DispatchHoldLedgerTests.cs`
- `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md` (CP-5 floor and R-4 census)

No new `Slow` class. `slow-tests-allowlist.txt` was not edited. The checkpoint tool's `SLOW CLASS` lines are summed durations, not the Slow category.

## Red first

On the dirty tree before `ce3466070`, with the parser stubbed and `ToQueued` not yet reading the hold, `C1076_LeaseOwnerTaskId_parses_only_the_owner_sentence` failed because the stub returned null. `/*/*/AgentTaskPipelineStatusTests/C1076*` executed 8, passed 2, failed 6: the new lease and remote-prep arms were `awaitingDispatch`. The two passes were `previous-stint` (`awaitingDispatch`) and `pin-outranks` (`routingPinNotBefore`), which already matched today's precedence. Those runs are dirty-tree red-first, not the SHA-validated receipt.

## CP-5 repairs

`InFlight.Phase` defaults to `Pushing` before the gate is acquired. The first class run treated that default as "the push holds the turn", started the waiter immediately, and `BehindTaskId` stayed null. A separate 15s clock did not fix it, because the phase check was still true too early. The green test waits until the fake's push call is blocked, which is after this task holds the gate. The 15s bound was not widened.

```
CHECKPOINT CP-5 commit=ce3466070bac1498a7d18bba6b91e31f6ddcf507 build=ok filter=/*/*/AgentTaskPipelineStatusTests/* executed=66 passed=65 failed=1 skipped=0 trx=/work/worktrees/task-be6f17af/.antiphon/c1076-s4-cp5/20261006-095945-19f3/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=ce3466070bac1498a7d18bba6b91e31f6ddcf507 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=efdda96b15b78e1750b7d43ef5189a9f66eac887 build=ok filter=/*/*/AgentTaskPipelineStatusTests/* executed=66 passed=65 failed=1 skipped=0 trx=/work/worktrees/task-be6f17af/.antiphon/c1076-s4-cp5/20261006-100609-621e/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=efdda96b15b78e1750b7d43ef5189a9f66eac887 sourceState=clean buildSource=verified
```

Both failures are `C1076_a_task_waiting_for_its_push_turn_names_the_pushing_task`: `BehindTaskId` null. slot=granted waited=0s.

## Checkpoint CP-5 (run 20261006-101102-a840)

Tool bootstrap `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1076-tool/ --property:UseAppHost=false` under slot `722c6a56-cc43-4dec-8d16-5b98b25e00f9` waited=0s held=5s. That bootstrap is the explained unlisted build. The `run` was `dotnet exec` of that DLL, not a second slot. `--keep-outputs` left `bin-c1076-b` for the two explained runs below.

```
CHECKPOINT CP-5 commit=776fa4464d0b8f76ecfce20d83b84ff380e50c6c build=ok filter=/*/*/AgentTaskPipelineStatusTests/* executed=66 passed=66 failed=0 skipped=0 trx=/work/worktrees/task-be6f17af/.antiphon/c1076-s4-cp5/20261006-101102-a840/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=776fa4464d0b8f76ecfce20d83b84ff380e50c6c sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=104.6432121s startup=42.0882753s testsWall=12.4048751s teardown=1.3929855s hostWall=55.8861509s
```

Verdict GREEN exit=0. Wall 2m41s. Host Debian GNU/Linux 12. The plan's old floor of exact 70 counted four `AgentTaskPipelineEndpointTests` that live in the same file and are a different class, so this filter cannot see them. The partial class is 58 existing results plus 8 new (4 + 3 + 1). The plan row now says exact 66 / min 66. This run is 66/66.

## Ledger and registry guard (explained, not plan rows)

Same `bin-c1076-b` build, `-NoBuild`, same expected SHA.

```
CHECKPOINT C1076-S4-LEDGER commit=776fa4464d0b8f76ecfce20d83b84ff380e50c6c build=reused filter=/*/*/DispatchHoldLedgerTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-be6f17af/.antiphon/c1076-s4-ledger/C1076-S4-LEDGER-20261006-101403-0b2b/run.trx slot=granted waited=0s dirty=0 source=776fa4464d0b8f76ecfce20d83b84ff380e50c6c sourceState=clean buildSource=verified
CHECKPOINT C1076-S4-GUARD commit=776fa4464d0b8f76ecfce20d83b84ff380e50c6c build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-be6f17af/.antiphon/c1076-s4-guard/C1076-S4-GUARD-20261006-101411-9436/run.trx slot=granted waited=0s dirty=0 source=776fa4464d0b8f76ecfce20d83b84ff380e50c6c sourceState=clean buildSource=verified
```

Ledger slot `ade8d17f-1ca0-4969-8286-b299eddc73c4` waited=0s held=6s. Guard slot `579d5eae-ec0b-4b66-8fda-7360b0aa6da0` waited=0s held=8s. Ledger is 22 existing plus `C1076_LeaseOwnerTaskId_parses_only_the_owner_sentence`. Guard is `Registry_matches_compiled_metadata`, `unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not`, and `allowlist_file_exists_and_has_spawn_lane_entries`.

## Rows deferred

| Row | Why |
|---|---|
| CP-4 | `After=all`. Floor exact 55 includes S5 V-15 (`RunnerBranchContractDocumentationTests` +1). This slice ran only the ledger class (23), not the predicate, settings, or docs classes. Not closed. |
| CP-6 | `DispatchHoldVisibilityTests` / R-9. D-5 regression. Not an S4 edit. Not run. |
| CP-7 | Client vitest / V-14 / D-7. S5. The client was not edited. Not run. |
| Unit lane | The brief says no whole-Unit and no baseline sweep. Not run. Not marked passed. |

## Ordinary V/R for S4

| ID | Outcome |
|---|---|
| V-9 `C1076_a_dispatcher_lease_hold_is_the_queued_reason_and_names_the_owner` (4) | Passed inside CP-5 |
| V-10 `C1076_remote_preparation_in_flight_or_backing_off_is_the_queued_reason` (3) | Passed inside CP-5 |
| V-11 `C1076_a_task_waiting_for_its_push_turn_names_the_pushing_task` | Passed inside CP-5 |
| V-12 `C1076_LeaseOwnerTaskId_parses_only_the_owner_sentence` | Passed inside the ledger run |
| R-4 pipeline class, 58 existing | Passed inside CP-5 (66 including the 8 new) |
| R-6 ledger, 22 existing | Passed (23 including V-12) |

V-14, V-15, R-9, and R-12 were not run and are not marked passed. V-1..V-8 and V-13, and R-1..R-3, R-5, R-7, R-8, R-10, R-11, were proved on earlier slices and were not re-run here.

## Positive controls pending for Mutation

PC-8: ignore the `Dispatched` floor. Red witness is V-9 `previous-stint` (`repositoryLease` instead of `awaitingDispatch`). PC-9: map `RemotePrep` to `awaitingDispatch`. Red witness is V-10 `in-flight` and `backoff`. PC-10: `LeaseOwnerTaskId` returns the first 32-hex run in any sentence. Red witness is V-12 on `LeaseFenced("journal of task <guid>")`. PC-1..PC-7 and PC-11 remain pending from S1-S3. PC-12 is the client (S5). Mutation was not run.

## Evidence

`scripts/check-evidence-diff.ps1` over `23dc94ea54d547eb23a8e0d4ba6400995d1bd8f5`..HEAD, including this report commit, is in the caller summary.
