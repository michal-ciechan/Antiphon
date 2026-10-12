# CARD-1199 repair 11

Outcome: both confinement entry points now refuse a worktree shape that is not the tracked file. A directory, symlink, submodule, nested repo, unreadable path, or a path with untracked or ignored descendants is left untouched. The row exits 2 with `refused: tampered` and `manual restore required`. The lossless sentence is printed only when every non-tracked path captured before the build still matches.

Tested code: `ab56dbb79a2d639d0b0118216ce5d7f2184c5e53` on `feat/card-task-2cf84f6c` in `/work/worktrees/task-2cf84f6c`. Landing owner is Code task `2cf84f6c`. This report commit, if present, is docs only and was not part of the checkpoint SHA.

## Red proof

Commit `c979b19b73bbfb15af02d0dc9d244d782e822423` added the nine-row class while production restore was still `eb752b5c02ecc926e1a93bcaebd8a7e20fc7ce2d`. Isolated build lease `a5f09360-9038-451e-b4a8-f440ed8f6c1c`. The class run exited 2: 9 total, 5 failed, 4 succeeded, 0 skipped.

The five failures were the same on the tool and on `scripts/run-checkpoint.ps1`. Each restored the tracked ancestor, deleted the user bytes, and still printed `lossless because the pre-build tracked tree was clean`.

- directory-on-file: `TAMPERED tracked.txt`, `TAMPERED tracked.txt/secret.txt`, `restored from f257e70a2b295e26938f9387d7a80b59ee96e190`, secret file gone.
- file-on-directory: `TAMPERED dir`, `TAMPERED dir/a.txt`, user file gone.
- symlink-on-file: the link was restored away.
- untracked-descendant: `TAMPERED tracked.txt`, `user.txt` gone.
- ignored-descendant: `TAMPERED tracked.txt`, `user.dat` gone.

staged-untracked, modified-tracked, deleted-tracked, and clean-build already passed on that production code.

## Fix

`BuildConfinement.Settle` and `Complete-CheckpointBaseline` classify each changed tracked path before restore. A regular file, or an absence under real directory ancestors, is restored as before. Anything else is a conflict: the worktree bytes stay, the index is left alone for that path and its held descendants, and the row lists `CONFLICT <path> <reason>; manual restore required` plus `worktree bytes changed; manual restore required`. A path absent from HEAD is unstaged only when it is not held by a conflict. Git command failure is still exit 8. The hanging-git timeout path was not changed.

The lossless sentence requires a matching pre-build manifest of non-tracked files (`git ls-files -z --others --ignored --exclude-standard`, kind, size, and SHA-256). An unreadable stamp never allows the claim. A clean tree does not print the sentence.

## Green proof

After `ab56dbb79a2d639d0b0118216ce5d7f2184c5e53`, `shape_conflict_rule` was 9 passed, 0 failed, 0 skipped in 2m 53s (lease `81a1b83e-c9f1-4df8-b5bf-9f38b3750fc9`, waited=15s). `tracked_snapshot_contains_committed_files` passed 1/1 (lease `e0eab118-c891-4167-baa3-86cb5ff946d8`). `namespace_census_matches_compiled_checkpoint_cases` passed 1/1 (lease `8d5a4e0b-2049-45cb-a005-016770dc1307`). Census selected stays 480.

## Official checkpoint

Run `20261012-195915-8475`, tool exit 0, wall 15m27s, `--keep-outputs`, one isolated build `bin-c1199r11/`, source `ab56dbb79a2d639d0b0118216ce5d7f2184c5e53` clean, `buildSource=verified`. Plan file `/tmp/c1199-r11-plan.md` (not committed). Evidence: `.antiphon/checkpoints/20261012-195915-8475/report.md`.

```
CHECKPOINT CP-1 commit=ab56dbb79a2d639d0b0118216ce5d7f2184c5e53 build=ok filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*|BaselineComparerTests*|RunSchedulerTests*|CheckpointManifestTests*|CheckpointTaskOwnershipTests*|CheckpointToolCopyCleanupTests*|WaitCommandTests*|RunStateStoreTests*|CheckpointAppTests*|CheckpointNamespaceCensusUsageTests*|CheckpointOutputConfinementTests*|CheckpointTrackedBaselineTests*)/* executed=130 passed=130 failed=0 skipped=0 slot=granted waited=30s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=ab56dbb79a2d639d0b0118216ce5d7f2184c5e53 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_unstaged* executed=1 passed=1 failed=0 skipped=0 slot=granted waited=60s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=ab56dbb79a2d639d0b0118216ce5d7f2184c5e53 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_unstaged* executed=1 passed=1 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=ab56dbb79a2d639d0b0118216ce5d7f2184c5e53 build=reused filter=/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_unstaged* executed=1 passed=1 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=ab56dbb79a2d639d0b0118216ce5d7f2184c5e53 build=reused filter=/*/*/CheckpointShapeConflictTests/shape_conflict_rule* executed=9 passed=9 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
```

`hanging_git_is_refused_and_killed_within_the_bound` passed inside CP-1 at 15.310s. Two further `--no-build` passes on that same output passed at 16.563s (lease `12bd8531-be27-4962-8296-21ee2cb67e9c`, waited=0s) and 18.132s (lease `27da28ed-7dd0-4b79-bf65-be9f91b4190c`, waited=0s). A `Repeat=3` row cannot share CP-1's build, and a second OutputPath would be a second isolated build, so those two passes are the unlisted remainder of the three-pass requirement.

## Other checks

`scripts/check-evidence-diff.ps1` over `eb752b5c02ecc926e1a93bcaebd8a7e20fc7ce2d..ab56dbb79a2d639d0b0118216ce5d7f2184c5e53`: commits=4, entries=0, violations=0. `git merge-tree --write-tree` against fetched `origin/master` exited 0. `git ls-files` count 7416. Worktree clean of tracked changes after the isolated `bin-c1199r11` and `bin-c1199r11tool` directories were removed.

Runner defaults revision 2, `kindDefaults` empty, global runner server2. Three session runners: desktop and server2 accepting work, server2-temp stale and draining.

Linux only. `CheckpointShapeConflictTests` is in `windowsSkips`. Windows junctions remain an R2 disclosure. The plan names no PC rows. Post-land Mutation was not run. Every PC stays pending for method-scoped SourceLanding Mutation. Restart: none.

## Commits

- `c979b19b73bbfb15af02d0dc9d244d782e822423` test(CARD-1199): refuse restore when the worktree shape is not the tracked file
- `830ba4d74a0ce2b916ad99ce976bde06b521bc29` fix(CARD-1199): refuse restore when the worktree shape is not the tracked file
- `2cb8ece0bbb58d9864b51f640c019483baa56d24` fix(CARD-1199): compare conflicting paths without a PowerShell overload trap
- `ab56dbb79a2d639d0b0118216ce5d7f2184c5e53` test(CARD-1199): unlink the fixture symlink before root cleanup
