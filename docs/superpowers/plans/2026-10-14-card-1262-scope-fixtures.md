# CARD-1262 scope fixture repair

Code and landing owner: `46e9f228-fd5a-4670-95d3-796bc29b5ff8`.
Task base and initially fetched master: `5665ddb1a3ba730f3b858acc109e65b5d7304d6c`.

S1 commits this verification design before reproducing the seven existing failures.
S2 repairs only the two private workspace fixtures and their scenario setup. Use
`ScratchGitRepo` with a committed HEAD and the assembly-local process limiter.
Shared-writer scenarios must explicitly request Shared now that omitted Workspace
means Worktree. Seed real repository identities consistently with the resolver.
Preserve all existing assertions, production guards, skips and test deadlines.

## Verification design

- V-1: reproduce each of the seven reported cases alone on unchanged base test and
  production source (CP-1 through CP-7), then rerun those rows after repair.
- V-2: run both full affected classes in sequence (CP-8, CP-9). Baseline expects
  project scope 2 passed/4 failed and overlap 3 passed/3 failed, all seven at
  `workspace_default_not_git`. Final expects 6/6 passing in each class.
- V-3: inspect other create fixtures for omitted Workspace and non-Git directories;
  distinguish observed failures from source-only candidates. Run the previously
  repaired `CommitOnSettlePolicyTests` as adjacent smoke (CP-12).
- R-1: full affected classes preserve project precedence, no-project UI tasks,
  parent/merge/retry inheritance, Shared serialisation, Worktree warnings, disjoint
  scopes, ReadOnly exemption and cross-repository isolation (CP-8, CP-9).
- R-2: full `WorktreeDefaultAdmissionTests` proves the production default and
  non-Git refusal still hold (CP-11).
- R-3: whole Unit lane required by the governing Final stage contract (CP-10).
  The task brief's contrary no-Unit instruction is superseded by that contract.
- R-4: manually inspect the final diff for unchanged outcome assertions and no
  production edits; run both receipt validators, full task-base evidence-diff
  validator, fresh-master merge-tree, clean status and remote SHA checks.

No new test methods are needed: the existing behavioral witnesses already fail.
This fixture-only repair defines zero PCs or variants. SourceLanding Mutation owns
missing-control discovery even for zero-PC plans; no deliberate mutants run here.
No live acceptance, deployment or restart is required. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1262-proof/` | card-project | `/*/*/AgentTaskProjectScopeTests/a_card_bound_session_token_uses_the_cards_board_project_before_its_owning_agent` | V-1 | baseline 1 workspace_default_not_git failure; final 1 passed | 1 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | agent-project | `/*/*/AgentTaskProjectScopeTests/a_cardless_standing_agents_session_token_uses_the_agents_board_project` | V-1 | baseline 1 workspace_default_not_git failure; final 1 passed | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | CP-1 | ui-project | `/*/*/AgentTaskProjectScopeTests/a_tokenless_ui_task_has_no_project_scope` | V-1 | baseline 1 workspace_default_not_git failure; final 1 passed | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | CP-1 | child-project | `/*/*/AgentTaskProjectScopeTests/a_child_inherits_its_parent_scope_even_when_it_works_in_a_different_directory` | V-1 | baseline 1 workspace_default_not_git failure; final 1 passed | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | CP-1 | shared-overlap | `/*/*/AgentTaskScopeOverlapCreateTests/a_shared_task_is_told_it_will_wait_behind_the_running_one` | V-1 | baseline 1 workspace_default_not_git failure; final 1 passed | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1 | CP-1 | undeclared-overlap | `/*/*/AgentTaskScopeOverlapCreateTests/an_undeclared_shared_writer_is_told_it_will_wait` | V-1 | baseline 1 workspace_default_not_git failure; final 1 passed | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S1 | CP-1 | other-repo | `/*/*/AgentTaskScopeOverlapCreateTests/a_running_task_in_another_repo_is_told_nothing` | V-1 | baseline 1 workspace_default_not_git failure; final 1 passed | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S1 | CP-1 | project-class | `/*/*/AgentTaskProjectScopeTests/*` | V-2, R-1 | all 6; baseline 2 passed/4 failed; final 6 passed | 6 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S1 | CP-1 | overlap-class | `/*/*/AgentTaskScopeOverlapCreateTests/*` | V-2, R-1 | all 6; baseline 3 passed/3 failed; final 6 passed | 6 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S1 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | R-3 | all Unit cases; classify inherited failures separately | 1 | 12 | true | n/a |
| CP-11 | S1 | CP-1 | default-admission | `/*/*/WorktreeDefaultAdmissionTests/*` | R-2 | all listed, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S1 | CP-1 | policy-adjacent | `/*/*/CommitOnSettlePolicyTests/*` | V-3 | exactly 12 passed, 0 failed/skipped | 12 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this one table to an ignored manifest and set `timeouts.rowMinutes: 40`.
Run CP-1 through CP-9 at S1, then all rows after S2, using exact committed expected
SHA, `--total-timeout 150m`, `--serial`, no `--keep-outputs`, and wait to completion.
Remove only this task's literal compiler-output root after the red run before
reusing its path. Linux builds use `UseAppHost=false`. Bootstrap the checkpoint
tool once through `build-slot.ps1` into `bin-c1262-tool/`; this necessary tool build
is the sole planned unlisted build. Preserve unedited CHECKPOINT lines and inspect
fresh TRX method identities/counts. Both validators must reject intentional red
as green evidence and accept qualified final rows.

### Cost

About 18 minutes baseline and 34 minutes final driver time, plus authoring and slot
waits, within the 90-minute task budget. No full-assembly run: private fixture
changes have bounded impact on the two named classes. Unit is required by the
Final contract; no unbounded shared production impact is claimed. Run each proof
selection at most three normal times, with no repetition after green absent a new
failure. Check compatibility with fetched master using merge-tree only; the
governing git-common contract forbids merging the target into this task branch.
