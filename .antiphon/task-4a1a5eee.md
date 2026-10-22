# CARD-1402 Code report (task 4a1a5eee)

Branch `feat/card-task-4a1a5eee`, worktree `/work/worktrees/task-4a1a5eee`, base `294fa3d1a81ff97abb285af3180d93ee4d4b25f6`.
Plan: `docs/superpowers/plans/2026-10-22-card-1402-launch-ceiling-follow-ups-plan.md`.

## Outcome

All three card items are implemented. The follow-up witness went red on its guarded defect and green after the fix; the whole affected class is green on the committed fix source with validated clean receipts. The Unit-lane row (CP-2) did not complete: it hit its derived 30-minute deadline on a loaded host, so V-4 is not discharged (details below).

## Commits

| Commit | What |
|---|---|
| `b7c6caae74dcad98c363063a3621157248d38824` | test(CARD-1402): red witness `C1402_Rearm_follow_up_saved_before_cancel_returns_still_rearms`; `FakeAgentProtocolAdapter.OnTurnWait`; same-burst witness asserts the stall kill at StallTimeoutMs; CARD-1402 plan; CARD-1395 CP-1 row aligned (Min 10, unlisted 16/16 selection recorded) |
| `0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f` | fix(CARD-1402): `AgentSessionService.ReadLaunchPromptCeilingAsync` keys the ceiling to the cancelled TurnEnd's stored sequence (fallback: highest stored sequence when that row is not stored yet); `docs/session-runtime-invariants.md` |

Tested SHA for the closed list: `0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f` (the report commit after it changes only `.antiphon/task-4a1a5eee.md`).

## Card items

1. Ceiling window: `ReadLaunchPromptCeilingAsync` returns the session's highest `TurnEnd` row with `StopReason` cancelled at the moment the wait returns; a follow-up stored after that row is above the ceiling and re-arms even when it was stored before the query ran. When no cancelled TurnEnd row is stored yet (the Codex adapter reads the verdict from the runner's row before the server tail stores it), nothing after it is stored either, so the highest stored sequence remains the ceiling. That fallback is today's behaviour kept for that case, not a new one (plan D-2); the only Cancelled producer in production is `RunnerCodexAdapter.TryBuildTranscriptVerdictAsync`.
2. Same-burst witness: `C1382_Rearm_same_burst_follow_up_after_provisional_slash_arm_still_rearms` now ends with `AssertStallKillAtTimeoutAsync`: a scan at StallTimeoutMs - 1 s kills nothing and leaves both attempts StreamingTurn; a scan at StallTimeoutMs + 1 s kills both (KillCount 1 each, adapter disposed, attempt Stalled, session Failed).
3. Plan/report alignment: CARD-1395 plan CP-1 row Min 9 -> 10 with a note that its filter selects the 10 `C1382_Rearm_*` methods and that the 16/16 claim (whole class plus one `RunAttemptStallDetectorTests` method) was an unlisted selection. This task's CP-1 runs the whole class (16 methods) and reports exactly that count.

## Red witness (narrow run, outside the closed list, as the brief asked)

Checkpoint tool run `20261022-062455-9c64` at the witness commit, plan `.antiphon/c1402-red-plan.md` (ignored, one row), filter = the single new method:

```
CHECKPOINT CP-1 commit=b7c6caae74dcad98c363063a3621157248d38824 build=ok filter=/*/*/AgentSessionCancelledTurnTests/C1402_Rearm_follow_up_saved_before_cancel_returns_still_rearms executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-4a1a5eee/.antiphon/checkpoints/20261022-062455-9c64/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b7c6caae74dcad98c363063a3621157248d38824 sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Application.AgentSessionCancelledTurnTests.C1402_Rearm_follow_up_saved_before_cancel_returns_still_rearms (CP-1) ShouldAssertException: armed.Attempt.Phase     should be RunPhase.StreamingTurn     but was RunPhase.AwaitingInput
```

The failure is the guarded defect: the follow-up stored before the cancelled wait returned was dropped as the launch prompt.

## Closed list (checkpoint tool run `20261022-063004-a30a`, `--after S1`, expected SHA `0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f`)

```
source: 0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f state=clean buildSource=verified
CHECKPOINT CP-1 commit=0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f build=ok filter=/*/*/AgentSessionCancelledTurnTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-4a1a5eee/.antiphon/checkpoints/20261022-063004-a30a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=200.0705039s startup=50.7502778s testsWall=8.3706122s teardown=2.4804006s hostWall=61.60131s
CHECKPOINT CP-2 commit=0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=30m slot=granted waited=0s dirty=0 source=0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 33m35s  sequential-equivalent: 31m04s  builds: 1  max-concurrent-builds: 1  rows: 1 green 1 red 0 skipped
verdict: RED exit=5
```

Evidence: `/work/worktrees/task-4a1a5eee/.antiphon/checkpoints/20261022-063004-a30a/report.json` (and `report.md`). Reruns: 0.

CP-1 covers V-1, V-2, V-3: all 16 methods of `AgentSessionCancelledTurnTests` passed, including the new witness, the extended same-burst witness and `C1382_Rearm_launch_prompt_saved_before_cancel_stays_waiting` (the D-2 fallback with no TurnEnd row stored).

CP-2 (V-4, Unit lane) timed out at the deadline the tool derives from EstimatedMinutes 10 (max(15, 3x10) = 30 min) and wrote no TRX. The host was running five Code sessions and another `Antiphon.Tests` build at the time; the test host was alive (54% CPU) when the deadline hit. The partial row console (`rows/CP-2/console.log`) shows three distinct failing tests, none in the touched area and none proven inherited (no base run was made):

- `Registry_matches_compiled_metadata`: `missing classes: Antiphon.Tests.TestHelpers.TestSchemaSnapshotTests`, `unregistered-marked ...TestSchemaSnapshotTests`
- `C1155_RosterNamesEveryDispatchWarningE2EMethod`: roster drift (the recent CARD plan at the base already listed this one as inherited)
- `shape_conflict_rule(...)` cases, 8 seen, each ~18 s: `toolNote should be "ok" but was "receipt-invalid: negative qualification mismatch"`

V-4 is therefore exact missing execution for this task, not a claimed pass. To run it once as the same row with a deadline that fits the loaded host (delete any `tests/Antiphon.Tests/bin-c1402-ceiling/` first; it was removed after this run):

```
dotnet exec tools/Antiphon.Checkpoints/bin-<tool>/Antiphon.Checkpoints.dll start --plan docs/superpowers/plans/2026-10-22-card-1402-launch-ceiling-follow-ups-plan.md --rows CP-2 --row-timeout 90m --expected-source-sha <tip>
```

## Unlisted builds/runs

- `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1402-tool/ --property:UseAppHost=false` through `scripts/build-slot.ps1` (slot granted, waited 0 s): the checkpoint driver itself. Output deleted at the end.
- The red witness run above (one row, one method), stated per the brief.

## Invariants and PCs

- V-1 green (red first, see above). V-2 green. V-3 green. V-4 not run to completion (CP-2 timeout).
- PC-1, PC-2, PC-3 (plan) pending for method-scoped SourceLanding Mutation; none were executed here.

## Other checks

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 294fa3d1a81ff97abb285af3180d93ee4d4b25f6 -HeadRef 0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f`: `commits=2 entries=0 violations=0`, exit 0 (rerun over the final tip after the report commit; see the final message).
- `git merge-tree --write-tree origin/master HEAD` at origin/master `ac9ac4a9ab9680c4474dc1a6dfd56532c33a2e4e`: exit 0, no conflicts (tree `87259c569347cfb5a6e4e9e5a67007fa895279c0`).
- RC: CARD-1382 (`ab2a1683e`) and CARD-1395 (`02aaaf471`) are not on `rc/2026-10-20` (`ceb6e3d6b`), and the card records CARD-1395 as master only. This change is master only; no cherry-pick.
- `bin-c1402-red/` and `bin-c1402-ceiling/` were deleted after their runs; `bin-c1402-tool/` deleted before reporting.
