# CARD-1502 plan: heal a complete-but-flagged-incomplete worktree, close the CARD-1448 recovery gaps (S1)

Source: CARD-1502, from Final Review 49566af9 of CARD-1448 (landed on master as `0498c748a..222ceeb00`;
carried on this branch as identical cherry-picks so the landing rebase drops them).
Slice S1 only. The CARD-1448 destructive-path guards (incomplete schema-2 intent, exactly one
`locked initializing` registration on the branch, branch at InitialSha, and the bounded single
admin-folder delete) are unchanged. The heal and the new guard only add ways to refuse or to
mark complete. Neither removes anything.

## Decisions

- **D-1 (A, liveness heal)** `WorktreeManager.RecoverInterruptedCreationAsync`, still under the
  repository lease and the creation file lock, heals an incomplete schema-2 intent instead of refusing
  it when: the intent has `CreationId`/`InitialSha` for this repository and branch; exactly one
  registration at the path, **unlocked**, on that branch; the directory has a `.git` file; its admin
  directory is under this repository's `worktrees/` and its `gitdir` points back at the path; HEAD is
  `refs/heads/<branch>` and resolves to a commit; the admin `index` exists and there is no `locked` file;
  and `merge-base --is-ancestor InitialSha <branch>` holds (at or ahead of the start). It then saves the
  intent `CreationComplete=true` with `GitDirectory` and logs a Warning. Nothing is removed. CreateAsync
  then refuses with the existing "path already exists" ConflictException, and both conflict-adopt
  paths (DelegationWorktreeService fallback, AgentSessionService card spawn) adopt it as before
  CARD-1448. Any failed condition keeps the CARD-1448 refusal.
- **D-2 (D)** Before removing anything, the recovery refuses when another registration has the
  branch checked out (`update-ref -d` does not check other worktrees).
- **D-3 (C, deferred)** An uncommitted/untracked check before the force removal is left on the card.
  An interrupted checkout's files show as untracked until git writes the index, so a plain status
  check would block every legitimate recovery. It needs a design, not a few lines.

## Coverage added (B)

| Test | Guards |
|---|---|
| `WorktreeManagerGitIntegrationTests.C1502_CompleteCheckoutStillMarkedIncompleteIsHealed` | D-1 heal (red before the fix) |
| `DelegationWorktreeTests.C1502_complete_checkout_that_died_before_its_final_save_is_adopted` | D-1 end to end: retry adopts with its work (red before the fix) |
| `C1502_HealRefusesWithoutAnIndex`, `C1502_HealRefusesABranchNotDescendedFromItsStart` | D-1 conditions (index, ancestry) |
| `C1502_CompleteRecordStillLockedInitializingRemovesNothing` | CARD-1448 recovery fires only for an incomplete intent |
| `C1502_NoDirectoryRecoveryLeavesSiblingRegistrationsAlone` | CARD-1448 bounded admin-folder delete: a live and a missing sibling keep their admin folders and registrations |
| `C1502_BranchCheckedOutElsewhereRemovesNothing` | D-2 (red before the fix) |
| `C1502_RollbackWarnsWhenItSkipsALockedRegistration` | CARD-1448 D-3 log, via a real `worktree add` killed mid-checkout (slow smudge filter, 1 s add timeout) |
| `AgentSessionServiceIntegrationTests.C1502_card_spawn_never_adopts_an_incomplete_worktree(true/false)` | CARD-1448 card-spawn adopt filter, with a complete-adopted control |

Red witnesses were run before the fixes through `scripts/build-slot.ps1` (stated in the Code report).

## Positive controls (pending for SourceLanding Mutation)

- **PC-1** Drop the D-1 heal call (or `return false` in `TryHealCompletedCreationAsync`): the two heal tests go red.
- **PC-2** Drop each D-1 condition in turn (index, ancestry): `C1502_HealRefusesWithoutAnIndex` / `C1502_HealRefusesABranchNotDescendedFromItsStart` go red.
- **PC-3** Remove `CreationComplete: false` from the recovery's early return: `C1502_CompleteRecordStillLockedInitializingRemovesNothing` goes red.
- **PC-4** Delete every admin folder under `worktrees/` in the no-directory arm (or run `worktree prune`): `C1502_NoDirectoryRecoveryLeavesSiblingRegistrationsAlone` goes red.
- **PC-5** Remove the rollback skip Warning: `C1502_RollbackWarnsWhenItSkipsALockedRegistration` goes red.
- **PC-6** Remove `!w.CreationIncomplete` from the AgentSessionService card-spawn filter: `C1502_card_spawn_never_adopts_an_incomplete_worktree(True)` goes red.
- **PC-7** Remove the D-2 guard: `C1502_BranchCheckedOutElsewhereRemovesNothing` goes red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-fix | `tests/Antiphon.Tests -> bin-c1502/` | c1502 | `/*/*/(WorktreeManagerGitIntegrationTests*)\|(DelegationWorktreeTests*)\|(AgentSessionServiceIntegrationTests*)/C1502*` | D-1, D-2, coverage (B) | 10 passed, 0 failed/skipped | 10 | 8 |
| CP-2 | S1-fix | CP-1 | c1502-regression | `/*/*/(WorktreeManagerTests*)\|(WorktreeManagerSafetyTests*)\|(WorktreeManagerStartRefLocalOnlyTests*)\|(DelegationWorktreeTests*)/*` | regression over the other `WorktreeManager*` classes and the whole `DelegationWorktreeTests` (incl. C1448/C1502) | all executed pass, 0 failed/skipped (60) | 55 | 10 |
| CP-3 | S1-fix | CP-1 | c1502-git-regression | `/*/*/WorktreeManagerGitIntegrationTests/(C1213*)\|(C1448*)\|(WorktreeManager_create*)\|(WorktreeManager_list*)\|(WorktreeManager_raw_remove_preserves*)\|(WorktreeManager_raw_remove_cannot*)\|(ScanDelegateWorktrees*)\|(WorktreeManager_remove*)\|(WorktreeManager_try*)\|(WorktreeJanitor*)\|(WorktreeManager_touch*)` | regression over `WorktreeManagerGitIntegrationTests` incl. the CARD-1448 C1448 rows, except the inherited Linux red | all executed pass, 0 failed/skipped (21) | 21 | 4 |

This is a closed list, run once through the checkpoint tool on server2 at the reported tip. Linux only,
and nothing runs on the desktop. CP-3 leaves out `WorktreeManager_raw_remove_refuses_without_authority`,
which is red on Linux by construction (CARD-1448 plan; `exclude` in `tests/linux-test-roster.json`,
CARD-0590). Per the brief (minimal testing) there is no whole Unit lane or whole-suite run.

### Cost

About 22 minutes of rows plus slot waits.

## RC

Depends on CARD-1448 (`0498c748a`, `a23ca2869`): port only after CARD-1448 is on the rc line. The Code
report states the per-commit merge-tree result onto `origin/rc/2026-10-20`.
