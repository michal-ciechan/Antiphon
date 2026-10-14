# CARD-1257 CommitOnSettle fixture repair

Code/landing owner: `25f93db0-e3ea-4f89-8547-69b33e181589`.
Task base and fetched master: `a02389cab727d508532e6d3db8ff6cbcf3eb7366`.

Repair only the private workspace fixture in `CommitOnSettlePolicyTests` using
`ScratchGitRepo`. Preserve the production default-worktree guard, every assertion,
and all 12 existing results. Add the assembly-local process-spawn limiter for Git.
Correct CP-9's executed floor to 12 in the matrix plan and its test-design owner.

## Verification design

- V-1: before the fix, the isolated `Create_persists_each_commitOnSettle_value`
  method produces three `workspace_default_not_git` failures on master source.
- V-2: before the fix, the full class produces 12 results: three pass, nine fail
  at the same workspace guard. After the fix, all 12 reach their existing assertions
  and pass, including all five formerly failing methods (nine argument cases).
- V-3: audit other task-create fixtures with non-Git temporary directories. Record
  source-inspection candidates separately from observed failures. Execute the two
  directly matching classes below on unchanged master source; report inherited
  reds without changing their assertions or expanding this fixture repair.
- R-1: full affected class, including invalid policy rejection, project policy
  updates, and Merge/Commit children, passes after repair.
- R-2: matrix CP-9 selects the full class with Min 12; no assertion, skip, production
  guard, or timeout is changed. Review the diff and run the full-range evidence guard.

No new test methods: this repair restores existing behavioral witnesses. Red-first
runs are ordinary fixture diagnostics, not positive controls. PC-1 (pending for
SourceLanding Mutation): remove Git initialization from this fixture and run only
`/*/*/CommitOnSettlePolicyTests/Create_persists_each_commitOnSettle_value`; require
the three workspace-refusal failures, restore, and verify those three green.
CARD-0097 PC-1–PC-49 and their named variants remain pending and are not discharged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1257-red/` | isolated-red | `/*/*/CommitOnSettlePolicyTests/Create_persists_each_commitOnSettle_value` | V-1 | exactly 3 failed with workspace_default_not_git; 0 skipped | 3 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | class-red | `/*/*/CommitOnSettlePolicyTests/*` | V-2 red | exactly 12 executed, 3 passed, 9 workspace_default_not_git failures, 0 skipped | 12 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | CP-1 | adjacent-audit | `/*/*/(AgentTaskScopeOverlapCreateTests*)\|(AgentTaskProjectScopeTests*)/*` | V-3 | all 12 executed; classify each failure against the unchanged master source | 12 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1257-green/` | class-green | `/*/*/CommitOnSettlePolicyTests/*` | V-2, R-1 | exactly 12 passed, 0 failed/skipped | 12 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this single table to an ignored manifest; set `timeouts.rowMinutes: 40`
(build deadline) and run CP-1–CP-3 at S1, CP-4 at S2. Pass the committed HEAD as
`--expected-source-sha`, `--total-timeout 150m`, and wait to completion. Linux
checkpoint builds use `UseAppHost=false`. Build the checkpoint tool once through
`scripts/build-slot.ps1` into `bin-c1257-tool/`; this bootstrap is the only unlisted
build. Validate receipts with both the tool and `validate-checkpoint-receipt.ps1`;
intentional red receipts must not be labeled qualified green.

### Cost

29 minutes estimated driver time plus authoring and host slot waits, within the
90-minute dispatch budget. No full assembly or whole Unit lane: the specific brief
excludes it and this change has no shared production impact. No live acceptance or
restart is required. Final merge uses fetched `origin/master` without rebase;
check `git merge-tree --write-tree`, remote branch SHA, and clean worktree.
