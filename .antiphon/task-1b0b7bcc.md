# Code 1b0b7bcc - CARD-1448 never reuse an incomplete worktree

Plan: docs/superpowers/plans/2026-10-22-card-1448-incomplete-worktree-plan.md (imports clean: 3 rows).
Source: docs/investigations/2026-10-22-runner-sync-identity-mismatch.md section 6 (Debug 8e5fd2d6).
Branch feat/card-task-1b0b7bcc, base e4db491e751edc8ae695dc7f3b45b42491ea04c4.

## Commits

- 854698e5b4adaedde3c6fdc7808f55c56bd1bae5 test: C1448 witnesses (+ unset `WorktreeInfo.CreationIncomplete` seam)
- 9cfd37712f8d852e97a1f5b898fef7b6d94f1586 fix: D-1, D-2, D-3 and the plan
- 32cf4743f5a64b87eed533bf138f999f36f2fa11 report; first checkpoint run (see below)
- this commit: CP-3 split into CP-3/CP-4 leaving out one inherited Linux red; CP-1..CP-4 run once
  through the checkpoint tool on this tip (CHECKPOINT lines in the final Code message).

## Red witness (stated unlisted run, at 854698e5b)

`scripts/build-slot.ps1 -Label c1448-red-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1448-red/`
(slot granted, waited=525s, build exit 0) then `-Label c1448-red-run -- dotnet run --no-build ...
--treenode-filter '/*/*/(WorktreeManagerGitIntegrationTests*)|(DelegationWorktreeTests*)/C1448_*'`
(slot granted, waited=300s): total 8, failed 5, succeeded 3, skipped 0.

| Test | Before fix |
|---|---|
| C1448_ListMarksAnInterruptedCreationIncomplete | red: CreationIncomplete false |
| C1448_InterruptedCreationIsRecreatedFresh | red: ConflictException "Worktree path already exists" |
| C1448_InterruptedCreationWithoutDirectoryIsRecreated | red: "Stale worktree registration requires creation/recovery ownership evidence" |
| C1448_incomplete_creation_is_not_adopted_when_recovery_refuses | red: no throw (half checkout adopted) |
| C1448_interrupted_creation_is_recreated_and_its_base_recorded | red: README.md missing (half checkout adopted) |
| C1448_OtherLockReasonRemovesNothing | guard, green |
| C1448_MovedBranchRemovesNothing | guard, green |
| C1448_complete_creation_is_still_adopted | guard, green |

An earlier attempt at the same selection with an implicit `dotnet run` build was killed by my 590 s
foreground timeout (slot wait 150 s, MSBuild child nodes exited prematurely); no tests ran; its
output was deleted.

## Landability

- `git merge-tree --write-tree origin/master HEAD` clean (master 445df8a805dac8525203081c03f6e58fa3f9ff3d).
- Per-commit merge-tree onto origin/rc/2026-10-20 (0714a9a89e4bff65aae7e0b4b94098694aeb931c): both
  commits clean. WorktreeManager.cs, DelegationWorktreeService.cs, WorktreeInfo.cs and both test
  files are byte-identical on rc; AgentSessionService.cs differs but the one-line hunk applies. Drop
  the plan doc from an rc pick (rc carries no plans).
- `scripts/check-evidence-diff.ps1` base..9cfd37712: commits=2 violations=0.

## Not covered

- D-3 (warning log) and the AgentSessionService card-spawn filter have no checkpoint.
- PCs: none planned; every mutation control stays pending for SourceLanding Mutation (suggested:
  drop the `!w.CreationIncomplete` filter, the fallback refusal, each D-2 condition, the
  old-value on update-ref).

## First checkpoint run (32cf4743f, run 20261022-170015-54a7) - superseded

```
CHECKPOINT CP-1 commit=32cf4743f5a64b87eed533bf138f999f36f2fa11 build=ok filter=/*/*/WorktreeManagerGitIntegrationTests/C1448_* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-1b0b7bcc/.antiphon/checkpoints/20261022-170015-54a7/rows/CP-1/run.trx slot=granted waited=180s dirty=0 source=32cf4743f5a64b87eed533bf138f999f36f2fa11 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=32cf4743f5a64b87eed533bf138f999f36f2fa11 build=reused filter=/*/*/DelegationWorktreeTests/C1448_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-1b0b7bcc/.antiphon/checkpoints/20261022-170015-54a7/rows/CP-2/run.trx slot=granted waited=195s dirty=0 source=32cf4743f5a64b87eed533bf138f999f36f2fa11 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=32cf4743f5a64b87eed533bf138f999f36f2fa11 build=reused filter=/*/*/(WorktreeManager*)|(DelegationWorktreeTests*)/* executed=81 passed=80 failed=1 skipped=0 trx=/work/worktrees/task-1b0b7bcc/.antiphon/checkpoints/20261022-170015-54a7/rows/CP-3/run.trx slot=granted waited=120s dirty=0 source=32cf4743f5a64b87eed533bf138f999f36f2fa11 sourceState=clean buildSource=verified
```

The one red is `WorktreeManagerGitIntegrationTests.WorktreeManager_raw_remove_refuses_without_authority`
("worktree list --porcelain should not contain <path>"). It is inherited: at base e4db491e7 in a
detached worktree, `build-slot.ps1 -Label c1448-base-build` (waited=30s, build exit 0) then
`-Label c1448-base-run` (waited=0s) with filter
`/*/*/WorktreeManagerGitIntegrationTests/WorktreeManager_raw_remove_refuses_without_authority`:
total 1, failed 1, same assertion. The test asserts that a still-registered path is absent from porcelain,
which only holds on Windows (forward-slash porcelain); the class is Linux `exclude` (CARD-0590).
The red row kept its build output, so that receipt validates `cleanup_refused`. The plan now splits
CP-3 into CP-3/CP-4 and leaves that test out.
