# task-db2df8ea report: CARD-1640, auto worktree base never silently uses the target

Branch feat/card-task-db2df8ea, worktree /work/worktrees/task-db2df8ea (runner mirror), base 3a49b60133327f4441f06698ef3369b1d277d049 (master).
Original Code task (landing owner): db2df8ea. No plan or evidence doc (the brief carried no plan; no `### Checkpoints` table, so no checkpoint-tool run).

Commits:
- e5c4793d6e0a52c2cd143d60d91d394a4a71574d test: red witnesses (T0442_V08, C1640 create)
- 2940ad1fdbb7f535863eeff379d4b80498d97bc2 fix
- 4b945e6fb550a5165e3cfc4623e109688dda3396 test: Auto dispatch witness; Target-mode deleted-sibling test restored; C1349 updated

## Policy and why
Auto mode only (`-StartRef`, `-BaseTask` and `-FreshWorktree` keep their old behaviour):
1. Read the remote-tracking ref. When a sibling's `refs/heads/<b>` is missing, the resolver reads `refs/remotes/origin/<b>` locally with one `for-each-ref` call (no network) and continues from it, with a warning naming `origin/<b> @ sha`. This covers a branch deleted locally after publication.
2. Fetch at create. A branch that is still unreadable is fetched at create (`StartRefAvailability.TryFetchSourceBranchAsync`: `fetch --no-tags --no-write-fetch-head origin +refs/heads/<b>:refs/remotes/origin/<b>`, bounded, under the start-ref lease), and the preview is computed again. Create is the only place a fetch happens, because CARD-0666 says dispatch never fetches (no network call in the claim or under a row lock).
3. Refuse loudly. A same-destination, same-repository, unlanded Succeeded sibling that is still unreadable (no local commit, no tracking ref, or a git error reading its repository identity) makes the resolver return `Unknown/source_unreadable`. The first warning names the branch and the remedy: sync or push the branch and retry, `-StartRef <its pushed sha>`, or `-FreshWorktree`.
   - At create the request is refused with 409 `worktree_base_unreadable`, and no row is created.
   - At dispatch `CardWorktreeBaseSelection.SyncUnverified` now also covers `source_unreadable`, so the existing block path blocks the task with the reason and the remedy. **AgentTaskDispatcher.cs, the runner slot pool and CreateAgentTaskRequest were not touched.**
4. `-FreshWorktree` (Target mode) still returns Target with warnings. An explicit `-BaseTask` naming a remote-only or missing branch still refuses with `requested_source_invalid`.

Files: server/Application/Services/{AgentTaskWorktreeBaseResolver,WorktreeBaseInspection,StartRefAvailability,AgentTaskService}.cs; tests/Antiphon.Tests/Application/{AgentTaskWorktreeBaseResolverTests,AgentTaskWorktreeBaseCreateTests,AgentTaskDispatchBaseGuardTests,DispatchSiblingInspectionTests}.cs.

## Tests I changed on purpose
These tests pinned the old behaviour (a warning, then the target):
- T0442_V08 `remote_only` now continues from the tracking SHA. `missing_local_branch` and `git_error` now return `Unknown/source_unreadable` with the remedy. All five scenarios now also check that `-FreshWorktree` gives Target.
- C1349_ContainedInventory `absent local ref` and `prefix-neighbor ref` now expect `source_unreadable`, 3 `for-each-ref` calls (one of them under `refs/remotes/origin`) and `GitCommands <= 5`, still with no cherry/log.

Correction during the work: I first rewrote `a_sibling_whose_branch_was_deleted_is_silent`. Its fixture seeds a Target-mode (`-FreshWorktree`) task, so its old red was meaningless. I restored the original test byte-for-byte in 4b945e6f and added a real Auto-mode witness, `C1640_auto_dispatch_blocks_on_an_unreadable_sibling_instead_of_using_the_target`.

New tests: `C1640_create_fetches_an_unreadable_source_or_refuses_by_name(on_origin|nowhere)` (with a bare origin) and the C1640 Auto dispatch witness.

## Runs (all build-slot, native output, UseAppHost=false, TEMP=/tmp, Linux server2)
| # | source | filter | result | slot / waited |
|---|---|---|---|---|
| build red | e5c4793d | Antiphon.Tests | ok | 9890553f waited=150s |
| red | e5c4793d tests + base server | V08*, C1640*, deleted-sibling | 6/8 failed as intended (V08 git_error, missing_local_branch, remote_only; C1640 create x2; the wrong deleted-sibling edit) | 53d7623b waited=75s |
| build green | 2940ad1f | Antiphon.Tests | ok | c80ffdc6 waited=30s |
| green 1 | 2940ad1f | 6 classes (below) | 242/245; 3 red were stale tests, fixed in 4b945e6f | 53646e24 waited=0s |
| build red2 | 4b945e6f tests + base server (server checked out at 3a49b601, then restored) | Antiphon.Tests | ok | 22be3c64 waited=0s |
| red2 | same | C1640*, C1349_ContainedInventory* | 3/8 failed as intended (absent, prefix-neighbor -> Target; Auto dispatch -> Dispatched); the other 5 passed | 3913436e waited=150s |
| build green2 | 4b945e6f | Antiphon.Tests | ok | 8c7fe576 waited=75s |
| **green2** | **4b945e6f** | AgentTaskWorktreeBaseResolverTests, AgentTaskWorktreeBaseCreateTests, AgentTaskWorktreeBaseSyncDebtTests, AgentTaskDispatchBaseGuardTests, DispatchSiblingInspectionTests, StartRefAvailabilityTests (whole classes) | **245/246, skipped 0**; C508_GuardRefMismatchWarnedOnce(0,True) red (Queued, not Dispatched) | 3d7d74de waited=30s |
| rerun | 4b945e6f | AgentTaskDispatchBaseGuardTests/C508_GuardRefMismatchWarnedOnce | 4/4 green | 1783cf36 waited=135s |
| land audit build | 4b945e6f | tools/Antiphon.LandGraphAudit (Release, bin-land-audit as the script requires; deleted afterwards) | ok | 0338d6bf waited=151s |

- C508(0,True) has no siblings, so it returns before any changed code. It passed in green 1 on the same fix and passed again alone. I am treating it as a load flake.
- Unlisted runs, with reasons: an empty-filter run (0 tests; the `(Name)` grammar matches nothing) was rerun as green 1. Red2 was needed because the corrected witnesses could only be shown red against the base source.
- TRX files: tests/Antiphon.Tests/bin/Debug/net9.0/TestResults/c1640-{red,red2,green,green2,c508}.trx (ignored).
- Minimal testing (operator) overrides the Final profile. The whole Unit lane was not run, and no V-n/R-n exist.

## Gates
- Land graph `-Check`: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE (unchanged, as expected).
- check-evidence-diff 3a49b601..4b945e6f: commits=3, entries=0, violations=0.
- `git merge-tree --write-tree` HEAD onto origin/master: clean. Onto origin/rc/2026-11-01: clean.

## Residuals and decisions for the caller
1. A whole-inspection git error (for example a failed `for-each-ref`) still returns `Unknown/git_inspection_error`. Under the CARD-0788 dispatcher rule that blocks only a Review task or a previewed source; otherwise the task dispatches from the target with warnings. Closing this needs AgentTaskDispatcher.cs, which CARD-0824 S1 owns, so I did not change it. It is worth a card after S1 lands.
2. Behaviour change: a Succeeded, unlanded same-card sibling whose branch was deleted with no Landed event and no origin copy now refuses Auto creates and blocks Auto dispatches on that card until `-FreshWorktree` or `-StartRef` is given.
3. A handoff create (the path under locks) never fetches. It refuses with `worktree_base_unreadable`, the same as Ambiguous.

## Pending for Mutation (PCs, none discharged)
- PC-a: `SyncUnverified` covering `source_unreadable` (removing it lets the Auto dispatch witness dispatch).
- PC-b: the remote-tracking read in the resolver (V08 remote_only).
- PC-c: `unreadable.Add` in the identity catch (V08 git_error) and in the no-commit branch (V08 missing_local_branch, C1349 absent).
- PC-d: the create refusal `worktree_base_unreadable` (C1640 nowhere).
- PC-e: the create fetch and re-resolve (C1640 on_origin).
- PC-f: the Auto-only guard on the remote read (V08 explicit `requested_source_invalid`).
