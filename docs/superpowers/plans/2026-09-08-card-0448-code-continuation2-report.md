# CARD-0448 continuation 2 triage and implementation evidence

Worktree: `C:\Antiphon\worktrees\card-task-c86499fb`, branch `feat/card-task-c86499fb`, starting commit `0dea285a`.
**Status: Code in progress; full V/R/C/F/PC acceptance is not yet complete. No land/deploy.**
The accepted matrix still owns acceptance; a passing subset below is not a complete-matrix claim.

## All 25 task-related legacy failures

Classification was made from the production contract and fixture setup before changing assertions.
Two failures represent missing supported creation functionality; the others encode obsolete authority,
API, or wording contracts. Positive publication/local-cleanup tests are retained and migrated, not replaced
with blanket refusal assertions. Unowned creation fixtures are deliberately distinct from owned recovery.

| Original exact filter | Triage | Required resolution |
|---|---|---|
| `/*/*/AgentTaskLandRequestTests/fail_async_writes_land_failed_and_clears_the_pending_row` | OUTDATED wording assertion | Assert structured operation/destination evidence and safe generic error text, not legacy origin/master or raw exception prose. |
| `/*/*/DelegationWorktreeTests/an_unregistered_leftover_directory_is_already_cleaned_up_not_a_merge_failure` | OUTDATED unsafe assertion | Missing/unregistered checkout without a receipt cannot prove a merge. Assert unknown registration, no success claim, and retained surviving bytes. |
| `/*/*/DelegationWorktreeTests/a_self_removed_worktree_is_already_cleaned_up_not_a_merge_failure` | OUTDATED unsafe assertion | Missing/unregistered checkout without a receipt cannot prove a merge. Assert unknown registration, no success claim, and retained surviving bytes. |
| `/*/*/DelegationWorktreeTests/land_conflict_is_reported_and_the_worktree_is_left_for_the_merge_delegate` | OUTDATED API fixture | Retired split API cannot authorize publication. Migrate the same real-Git scenario to AgentTaskLandService and assert committed publication, cleanup and remote/ref state. |
| `/*/*/DelegationWorktreeTests/land_push_rejection_keeps_the_rebased_branch_and_worktree` | OUTDATED API fixture | Retired split API cannot authorize publication. Migrate the same real-Git scenario to AgentTaskLandService and assert committed publication, cleanup and remote/ref state. |
| `/*/*/DelegationWorktreeTests/land_with_upstream_set_deletes_the_branch` | OUTDATED API fixture | Retired split API cannot authorize publication. Migrate the same real-Git scenario to AgentTaskLandService and assert committed publication, cleanup and remote/ref state. |
| `/*/*/WorktreeResidueSweepTests/classify_landed_succeeded_ignores_untracked_only` | OUTDATED unsafe assertion | Legacy Landed event and TTL do not authorize deletion; untracked contents remain protected. Assert evidence-required/dirty retention and zero removal. Separately fixed the real always-dirty parser defect exposed in this path. |
| `/*/*/WorktreeResidueSweepTests/classify_first_match_wins_for_each_label` | OUTDATED unsafe assertion | Legacy Landed event and TTL do not authorize deletion; untracked contents remain protected. Assert evidence-required/dirty retention and zero removal. Separately fixed the real always-dirty parser defect exposed in this path. |
| `/*/*/WorktreeResidueSweepTests/execute_false_touches_nothing_and_execute_true_removes_only_the_eligible_row` | OUTDATED unsafe assertion | Legacy Landed event and TTL do not authorize deletion; untracked contents remain protected. Assert evidence-required/dirty retention and zero removal. Separately fixed the real always-dirty parser defect exposed in this path. |
| `/*/*/DelegationWorktreeTests/already_landed_arm_pushes_a_target_that_is_ahead_of_origin` | OUTDATED wording assertion | Assert structured operation/destination evidence and safe generic error text, not legacy origin/master or raw exception prose. |
| `/*/*/DelegationWorktreeTests/land_happy_path_rebases_a_moved_base_and_pushes_the_fast_forward` | OUTDATED API fixture | Retired split API cannot authorize publication. Migrate the same real-Git scenario to AgentTaskLandService and assert committed publication, cleanup and remote/ref state. |
| `/*/*/DelegationWorktreeTests/a_failed_worktree_add_leaves_no_registration_branch_or_directory` | REAL regression | Safe positive creation recovery was removed. Added creation intent, unchanged-content rollback, and index-preserving reconstruction from recorded registration ownership. |
| `/*/*/DelegationWorktreeTests/healing_re_attaches_the_task_branch_and_keeps_its_commits` | REAL regression | Safe positive creation recovery was removed. Added creation intent, unchanged-content rollback, and index-preserving reconstruction from recorded registration ownership. |
| `/*/*/DelegationWorktreeTests/an_unlocked_registration_whose_directory_is_gone_is_healed_too` | OUTDATED assertion | Fixture manually creates a registration with no ownership receipt. Preserve and refuse it; the manager-owned reconstruction companion still must succeed. |
| `/*/*/DelegationWorktreeTests/a_locked_registration_whose_directory_is_gone_is_healed_and_the_task_dispatches` | OUTDATED assertion | Fixture manually creates a registration with no ownership receipt. Preserve and refuse it; the manager-owned reconstruction companion still must succeed. |
| `/*/*/DelegationWorktreeTests/prepare_land_aborts_an_interrupted_rebase_first` | OUTDATED unsafe assertion | Blanket abort erases operator resolutions. Assert legacy refusal and exact retained staged index/file/rebase metadata. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeManager_remove_treats_unregistered_leftover_directory_as_already_clean` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeJanitor_prunes_stale_unregistered_leftover_directory` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeJanitor_retries_residue_before_the_ttl` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeManager_try_remove_reports_directory_residue_when_a_file_is_held` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeManager_try_remove_keeps_an_unmerged_branch_and_names_the_ahead_count` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeManager_try_remove_deletes_a_merged_branch_whose_upstream_is_behind` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeManager_remove_still_throws_when_the_worktree_is_locked` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeManager_remove_deletes_worktree_and_branch` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |
| `/*/*/WorktreeManagerGitIntegrationTests/WorktreeJanitor_prunes_stale_worktrees` | OUTDATED unsafe assertion | Raw remove and TTL paths lack typed authority. Assert refusal and retained directory/ref/metadata, including locked and unregistered residues. Typed positive removal remains in integrated landing/local merge suites. |

## Additional defects found

- `ParsePorcelainDirtiness` initialized both flags true, so even empty successful status was dirty. It now distinguishes clean, tracked, untracked and ignored output; malformed status stays conservative.
- The category census had two pre-existing omissions. The saved Grok evidence test is Unit. Herdr's mixed class is split into Unit title tests and Integration database tests.
- A server-only file lease had no standing fence after process death. Added pre-start file journals and PID/start identity checks during lease admission; verifier and Git child coverage is being completed.
- Local child merge advanced a moving source ref and lacked an expected target SHA/clean-checkout guard. It now pins the source/target commits, uses target CAS or checked-out fast-forward, and verifies the resulting checkout.

## Runs

- `continuation2-triage-01`: build error (incorrect enum name in migrated fixture), no execution credit.
- `continuation2-triage-02.trx`: 66 executed, 63 passed, 3 failed, 0 skipped. Failures: remaining generic-error wording assertion; mixed Herdr lane declaration; a scan assertion changed too broadly during test migration. All three corrected before rerun.
- Further runs and remaining matrix work are recorded below once verified.

## Measured continuation runs (2026-09-08)

Every count below comes from a nonzero fresh TRX except the explicitly rejected zero-execution rows. Outputs are below `tests/Antiphon.Tests/bin-c448/TestResults/` unless marked `bin-c448-next`. Logs are in this worktree's `.antiphon/` directory. Earlier runs predate the later fixes and are not substituted for the final regression.

| Run | Executed | Passed | Failed | Skipped | Interpretation |
|---|---:|---:|---:|---:|---|
| continuation2-triage-03.trx | 68 | 68 | 0 | 0 | Corrected legacy failure scope and creation/ignored-content companions. |
| continuation2-safety-02.trx | 67 | 67 | 0 | 0 | Earlier integrated safety subset. |
| continuation2-os-crash-01.trx | 7 | 7 | 0 | 0 | Historical: its C03 pause was actually after all pins (C04), and resume used a new parent scope. Superseded by recovery-03. |
| continuation2-boundaries-01.trx | 32 | 32 | 0 | 0 | Source/metadata checkpoints, target sequencers, pin rechecks and nested creation lease. |
| continuation2-recovery-02 | 0 | 0 | 0 | 0 | Rejected CLI filter; no execution credit. |
| continuation2-recovery-03.trx | 54 | 54 | 0 | 0 | 26m51s; corrected actual C03 pin/save gap and second OS recovery workers, checkpoint/crossed-coordinate rows, leases, cancellation and real verifier. |
| continuation2-matrix-01.trx | 129 | 128 | 1 | 0 | 37m28s. Only failure: malformed-git fixture attempted to overwrite Windows-hidden .git; corrected fixture attributes before the explicit corruption. This run does not cover later source changes. |
| continuation2-policy-red-01.trx (next) | 0 | 0 | 0 | 0 | Rejected combined method filter; no RED credit. |
| continuation2-verification-policy-red-02.trx (next) | 11 | 5 | 6 | 0 | Intended Shouldly RED: changed-base/filter/pin/skip evidence accepted by old policy. |
| continuation2-verification-policy-green-01.trx (next) | 11 | 11 | 0 | 0 | Rebuilt exact verification-policy fix. |
| continuation2-replacement-red-02.trx (next) | 1 | 0 | 1 | 0 | Intended null-active-operation assertion. Shouldly's displayed source excerpt is stale because the test source moved after this binary was built; the exact method and custom preservation message identify the executed assertion. |
| continuation2-replacement-green-01.trx (next) | 1 | 1 | 0 | 0 | Failed replacement retains the old active operation, then a fresh retry completes with a new operation and retained history. |
| continuation2-descendant-01.trx (next) | 1 | 1 | 0 | 0 | Initial real-process output fixture; insufficient for the stdout-only mutant, as described below. |

`continuation2-next-build-01.log` built the separate output with 0 errors (133 existing/obsolete-test warnings). The two post-build policy fixes were rebuilt by the green verification run. No main-checkout or live-service output was used.

## Additional defects and final-direction changes

- Source and task coordinates are reread after fetch and at preparation/verification/advance/push boundaries. Recovery pins are reread, not trusted from booleans. The captured target checkout, symbolic branch, active sequencer and exact SHA are checked through target advancement.
- Low-level removal rereads durable authority after its final status inspection, closing a task-coordinate revision gap immediately before deletion. A committed publication and cleanup intent still require fresh remote proof and exact source identity. Local cleanup uses its captured parent authority.
- Verification skip evidence is now semantic: `base_unchanged` requires unchanged S/P and no omitted selected filter; exact containment is distinct from a rebased preparation. Source/target/prepared pins are prerequisites where applicable.
- Replacement of a terminal refused operation is atomic with the task's active-operation pointer. Failed replacement preparation leaves the old operation active; history and pins remain.
- Canonical repository leases cover raw creation as well as dispatcher nesting, settlement/local merge and card review. Two more hand-built session-test worktree fixtures now use the common graph; their exact legacy methods still need the final run.
- Creation intent is atomically recorded before add. Rollback is limited to the invocation's unchanged unfinished creation, including empty ignored status. Recorded owned missing checkouts can be rebuilt from their admin/index without forced checkout. Unowned/mature/changed residue is retained.
- Standing child journals precede Git/creation/verifier launch. Live, reused, dead-but-unacknowledged, torn or unreadable journal state holds admission. Cancellation awaits the owned child and output before releasing acknowledgement. A dead root PID alone does not authorize clearing a crash orphan. This is conservative admission fencing, not a general OS sandbox for arbitrary detached hook processes.
- The normal Git completion path now drains both streams before clearing its journal. The first mutant that restored stderr-only waiting survived because the fixture also held stderr; it is rejected as control evidence. The revised real-process fixture explicitly leaves stdout inherited while preventing stderr inheritance. Run `20260908-180529-703d44` passed 1/1, failed the intended assertion 1/1 under the stderr-only mutant, then rebuilt and passed 1/1 after exact restoration. This establishes that specific PC45 subvariant; the remaining ownership/lock variants still require their own evidence.
- `LandingCleanup` is an appended event value with per-operation publication/cleanup/mode snapshots. Cleanup retries do not count as new publication. Terminal evidence, stage outcomes, events and pending state commit together; delivery/event-bus failure preserves confirmed publication.
- The alternate-output cleanup script inventories and retains unknown `bin-*` paths. It no longer invokes wildcard recursive deletion or robocopy mirroring. An independently executed old-script mutant erased the fixture sentinel and failed its preservation assertion.

## Current evidence locations and unfinished acceptance

- Detailed executed rows so far: `.antiphon/continuation2-executed.json` (earlier-run snapshot; final refresh pending).
- Sequence-numbered command/committed-row evidence: `.antiphon/continuation2-boundary-io`, `continuation2-recovery-io`, `continuation2-matrix-io`. Later runs additionally capture before/after file hashes, index hashes, registrations, local/remote refs and committed operation/events.
- Seven earlier independent controls: `.antiphon/continuation2-controls/manifest.json` (standing admission, publication count, atomic refusal, monotonic publication, target checkout, creation ignored bytes, wildcard script). These are specific subvariants, not whole PC-family passes.
- Expanded production controls: `.antiphon/continuation2-control-cases.json`, `.antiphon/run-continuation2-expanded-controls.py`, `.antiphon/continuation2-controls-expanded/manifest.json`. Every variant must have a passing baseline, intended assertion RED, exact source restoration and rebuilt GREEN. Surviving/invalid controls remain unfinished.
- Refreshed destructive-caller census: `.antiphon/continuation2-caller-census.txt`. `WorkspaceHookService.RunBeforeRemoveAsync` has no production caller. `GitService.DeleteBranchAsync` is reached by explicit `WorkflowEngine.DeleteWorkflowAsync`, not by landing; its existing remote delete is outside landing authorization. Runtime storage/fixture cleanup is separate from task-worktree cleanup. The complete caller/barrier review remains required.

The final combined regression, all remaining named V/R/C/F variants, and all PC subvariants are still pending. New boundary tests and the first corrected matrix row must be executed on the final source. Do not infer readiness from the aggregate passing counts above. The older continuation matrix remains an unfinished ledger until its individual rows are superseded with exact current evidence.
