# CARD-0494 Code verification (Final profile) - 2026-09-28

Verified commit: `0c8d1cf52156691a1c9a0a11f9f943751147c51c` on `feat/card-task-1ef5f77e` (Code task 1ef5f77e).
No production or test source changed in this round; it records verification only.

## CP-4 (native `AgentTaskLandDeliveryE2ETests`)

Run `20260928-021137-832b`, `--rows CP-4 --row-timeout 140m --total-timeout 150m`
(the tool parses bare durations as seconds; `m` is required).

```
CHECKPOINT CP-4 commit=0c8d1cf52156691a1c9a0a11f9f943751147c51c build=ok filter=/*/*/AgentTaskLandDeliveryE2ETests/* executed=52 passed=50 failed=2 skipped=0 trx=C:\Antiphon\worktrees\card-task-1ef5f77e\.antiphon\checkpoints\20260928-021137-832b\rows\CP-4\run.trx slot=granted waited=0s
SLOW CLASS Antiphon.E2E.AgentTaskLandDeliveryE2ETests 5442s tests=52 (CP-4)
```

- All 24 V-3/R-3 cases pass in the fresh TRX: `C488_ReviewToLandReceiptMatrix` 6/6,
  `C488_ReviewEvidenceCrashRecovers` 1/1, `C488_ReviewDeliveryCrashMatrix` 9/9,
  `C494_ReviewReceiptRejectsFalseEvidence` 8/8.
- Red: `C467_V28_LostFlushWakeupRecoversOnIdleCaller` and its pure alias
  `C488_ApprovalLostFlushRecovers`. The receipt arrives, then the test fails at `:154` because no
  `completion-scan-*.observation.json` exists. This red is **inherited**. The same method at base
  `2f050e3d` (branch point from master) fails on the same assertion: 1 test, 1 failed. CARD-0550
  recorded the same red as inherited at master `39623574` and filed it as CARD-0782.
- Duration finding: the class runs for 90m47s with 52 cases, against a plan estimate of 35m, and the
  tool's default row timeout of 3 x 35 = 105m leaves little margin. The E2E process kept using CPU
  and new fixture roots kept appearing through the whole run, so it was not hung.
  Earlier rounds never got a result because of timeouts or the owner ending the task:
  `20260927-220927-8937` hit the total timeout, `20260928-004751-63b1` had a total timeout of 3m
  and `20260928-005116-40ca` ended with owner-ended.
- A fresh worktree needs `client/dist` (`npm ci && npm run build`) or every case fails in about
  6s at `EnsureClientBundleIsCurrent`. Run `20260928-020710-980e` failed this way and was stopped.

## Unit lane

This run is not in the plan's Checkpoints table. The Final profile requires the whole Unit lane.
Run `20260928-034625-ffa9` used a one-row scratch manifest:

```
CHECKPOINT UNIT commit=0c8d1cf52156691a1c9a0a11f9f943751147c51c build=ok filter=/*/*/*/*[Category=Unit] executed=3434 passed=3433 failed=1 skipped=2 trx=C:\Antiphon\worktrees\card-task-1ef5f77e\.antiphon\checkpoints\20260928-034625-ffa9\rows\UNIT\run.trx slot=granted waited=0s
```

- Red: `EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open`. It passes
  3/3 when run alone from the same build. The branch has no diff under `tools/` or
  `tests/Antiphon.Tests/Checkpoints`. The test's 300ms `Task.Delay` release races
  `TryRemoveToolCopy` retries under full-lane load. This is an unrelated flake.

## Prior rounds (not re-run, per brief)

CP-1 94/94, CP-2 63/63 and CP-3 59/59 are green at the same commit in run
`C:\Antiphon\worktrees\card-task-a31d53c1\.antiphon\checkpoints\20260927-220927-8937`.

## Pending

CARD-0488 PCs and 494-PC-1..494-PC-32 stay pending for post-land Mutation.
