# CARD-1105 audit repair 3 (task 1a2ce245)

Outcome: D1 and D2 are fixed. The audit now checks every entry of every Git directory against a closed list; anything outside the list refuses. A count-only `rev-list` failure now has a test that goes red under the PC-2 mutation. The false docker-stack sentence is corrected and pinned. All 12 rows are green on the tested source: 216 tests executed, 216 passed.

- Tested source: `ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0`. The branch HEAD adds one commit on top of it, and that commit only adds this report file.
- Branch: `feat/card-task-1a2ce245` (fast-forward from `039b26612437f035fd86310857e69185cb3c254e`).
- Worktree: `/work/worktrees/task-1a2ce245`.
- Landing owner: Code task `6192d44b-9ec0-43a8-aed1-81fdb4565fc2` (original Code task, as given in the brief).
- Plan and evidence note: `docs/investigations/2026-10-07-card-1105-git-audit-promisor.md` (section "Repair 3"). The checkpoint evidence is under `.antiphon/checkpoints/20261008-001223-6831/` (gitignored).
- No production host was contacted. The whole Unit lane was not run: it is outside the commissioned 12-row scope, so it is neither deferred nor claimed as passed.

## Files changed

- `scripts/c590-remote.sh`: the helper program's tip collection.
  - New functions: `consider_gitdir`, `consider_pseudoref`, `consider_refs`, `consider_packed`, `consider_logs` and `consider_info`.
  - `consider_tips` now keeps only `for-each-ref`, `rev-parse HEAD` and the new `git-dir-layout` check.
  - The repository loop calls `consider_gitdir "$top" common` before `consider_tips`.
  - The closed list is written in the code comment.
- `scripts/fixtures/c1105-git-audit.cjs`:
  - Added the `HIDDEN-LOCATIONS` table (42 rows; adding a location means adding one row).
  - Added the `long-lived` positive variant.
  - Added an optional assertion on the receipt's check name.
- `scripts/fixtures/c1008-fake-docker.sh`: new `count-exit128` fault that fails only `rev-list --count`.
- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`:
  - New `C1105_Git_audit_hidden_locations` test. A `MethodDataSource` reads its cases from the fixture table.
  - `long-lived` added to `C1105_Git_audit_seed_content`.
  - New `countOnly` block in `C1008_Recycle_audits_promisor_checkout`.
- `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`: three new required pins, plus a check that the false sentence is gone.
- `docs/docker-stack.md`: the audit paragraph is rewritten to match the code.
- `docs/investigations/2026-10-07-card-1105-git-audit-promisor.md`:
  - Corrected the D1 paragraph.
  - Added V-7 and V-8, and updated the R-2 count.
  - Changed the PC-2 detector and added PC-D7.
  - Added the closure table and the baseline and mutation evidence.
  - Updated the checkpoint counts: CP-11 is now 80 cases (14 methods), and the total is 216.

No assertion, timeout or existing fixture fault was weakened or deleted. The existing `exit128` fault is unchanged; `count-exit128` is an added fault.

## Per defect

**D1 (P1): the linked worktree reflog was not read.**
- Change: reflogs are now read from every Git directory: the common directory and each `worktrees/<id>/logs`.
- Each worktree's `rev-parse --absolute-git-dir` must be the common directory or a direct child of `$top/worktrees`; otherwise the audit refuses with `check=git-dir-layout`.
- Regression row `linked-reset` reproduces Review's shape: commit in a detached linked worktree, then `reset --hard HEAD~1`. It now refuses with `RecycleUnpublishedWork`.
- Row `linked-reflog` isolates the reflog-only case.
- Red at 039b2661: the old helper exits 0 with `repositories=2 partial=2`.
- Red under mutation:
  - Making `logs)` skip linked directories turns `linked-reflog` red.
  - Turning off the `worktrees)` recursion turns `linked-reset`, `linked-reflog`, `linked-orig-head` and `linked-fetch-head` red.
- Documentation: the false sentence is replaced. `Main_volume_recycling_is_scripted_only` now pins:
  - "in each linked worktree's own logs";
  - the list of pseudorefs;
  - "an entry outside it refuses `RecycleGitAuditUnknown`";
  - the absence of the old sentence.
- Restoring the old sentence turns that test red (1 failed, 88 ms).

**D2 (P2): ORIG_HEAD and FETCH_HEAD were ignored.**
- Change: in every Git directory, ORIG_HEAD, FETCH_HEAD, REBASE_HEAD, BISECT_HEAD, AUTO_MERGE and MERGE_AUTOSTASH are now publication tips.
- A missing object fails the existing complete-graph traversal and refuses with `RecycleGitAuditUnknown` and `check=rev-list-objects` (row `orig-head-missing`).
- The "unless the object is a published commit" exception never applies. Every commit reachable from origin must already be present locally, so the audit cannot prove a missing object is published.
- MERGE_HEAD, CHERRY_PICK_HEAD and REVERT_HEAD keep the stricter existing behaviour: they refuse as `lock-*`. This is now also checked in every linked directory (row `linked-merge-head`).
- Red at 039b2661 (the old helper exits 0) for:
  - `orig-head`, `orig-head-missing` and `linked-orig-head`;
  - `fetch-head` (a real fetch from another URL) and `linked-fetch-head`;
  - `rebase-head`, `bisect-head`, `auto-merge` and `merge-autostash`.
- Red under mutation: moving each name from the tip list to the ignored list turns its rows red (table below).

**Positive rows still pass:**
- `published-fetch-head`: the deploy's `fetch --no-tags origin <branch>`.
- `published-orig-head`.
- `published-linked`: a linked worktree reset between published commits.
- `published-gc`: gc writes `info/refs` and `packed-refs`.
- `long-lived`:
  - several filtered fetches;
  - a remote branch that gc recorded in `info/refs`, then pruned on origin;
  - the deploy fetch's FETCH_HEAD;
  - reflogs containing only `clone:` entries;
  - the HEAD blob still absent.
- The existing `seed`, `fetch-only`, promisor and resume proofs are unchanged and green.

**PC-2: a failed count must never become zero.**
- Before: the mutation stayed green because the `exit128` fault fails every `rev-list` call, so the object traversal refused before the count was reached.
- Now: `count-exit128` lets the traversal succeed and fails only `rev-list --count`. The `countOnly` block requires `check=rev-list status=128` and no volume removal.
- Proof: with the count line's fallback mutated to `|| count=0`, `C1008_Recycle_audits_promisor_checkout` fails at `countOnly.Removed` (1 failed, 1m00s). With the source restored it is green.

## Closed list (systematic closure)

The full table is in the investigation note and the code comment. In summary:

- **Publication tips:** `HEAD` (raw file plus `rev-parse`), `refs/**` (raw parse plus `for-each-ref`), `packed-refs` (raw parse), `logs/**` in every Git directory, and the six pseudorefs above. `stash` (`refs/stash` plus its reflog) and `refs/notes` are tips through the refs and reflogs.
- **Refuse unknown:**
  - in-progress operations: MERGE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD, `rebase-merge`, `rebase-apply`, `sequencer`, `BISECT_*` and `NOTES_MERGE_*`;
  - `*.lock` files at the top level or under `refs`;
  - `refs/replace`, loose or packed;
  - a non-empty `info/grafts`;
  - `reftable`, `modules` and `shallow`;
  - an index in a bare Git directory;
  - any unparseable or non-regular ref, reflog or pseudoref;
  - any unknown entry in `info/` (`gitdir-info`) or at the top level (`gitdir-entry`, for example `lost-found`).
- **Index:** a worktree index keeps the existing status checks and raw-hash checks.
- **Named as not tips (decisions for Review):**
  - `objects/`: an object that no location names is outside the audit, the same as for `git gc`.
  - `info/refs`: repack writes it as a copy of refs, and those refs are read directly. A stale entry names an object that no live location names. Treating it as a tip would falsely refuse every long-lived clone where gc ran before a remote branch was pruned.
  - Configuration, hooks, description and message files hold no object IDs and are ignored.
- **Accepted false refusals:**
  - submodule repositories (`modules`);
  - Git directories that use reftable storage;
  - editor swap files or other unknown files left in a Git directory;
  - a leftover `AUTO_MERGE`, which names a tree and so refuses unknown at `tip-commit`.

## Baseline and mutation proof

The 42-row table was run with the 039b2661 `scripts/c590-remote.sh` checked out (TUnit, method scope `C1105_Git_audit_hidden_locations*`):

- 22 rows failed. In every one of them the old helper accepted the private commit with exit 0: `linked-reflog`, `linked-reset`, `linked-orig-head`, `linked-fetch-head`, `orig-head`, `orig-head-missing`, `fetch-head`, `rebase-head`, `bisect-head`, `auto-merge`, `merge-autostash`, `sequencer`, `bisect-state`, `notes-merge`, `ref-lock`, `replace`, `replace-packed`, `info-entry`, `unknown-entry`, `modules`, `reftable`, `bare-index`.
- 20 rows passed. These are 15 retained refusal rows and 5 positive rows. They are regression coverage, not new detections.
- Each of the 15 retained refusal rows is covered twice: by the raw parse and by Git's own `for-each-ref`, `rev-parse` or the early marker check. A Mutation variant has to remove both to turn the row red.

Each mutation below was applied to a copy of the helper, not to the worktree. In every case the positive `seed` and `long-lived` rows stayed green.

| Mutation | Rows that went red |
|---|---|
| `logs)` skips linked directories | linked-reflog |
| `worktrees)` recursion off | linked-reset, linked-reflog, linked-orig-head, linked-fetch-head |
| ORIG_HEAD moved to the ignored list | orig-head, linked-orig-head, orig-head-missing |
| FETCH_HEAD moved to the ignored list | fetch-head, linked-fetch-head |
| REBASE_HEAD moved to the ignored list | rebase-head |
| AUTO_MERGE moved to the ignored list | auto-merge |
| MERGE_AUTOSTASH moved to the ignored list | merge-autostash |
| BISECT_HEAD and BISECT_* moved to the ignored list | bisect-head, bisect-state |
| `sequencer` removed from the interrupted-operation list | sequencer |
| `BISECT_*` removed from the interrupted-operation list | bisect-state |
| `NOTES_MERGE_*` removed from the interrupted-operation list | notes-merge |
| ref `*.lock` refusal removed | ref-lock (the audit refuses as unpublished instead of `lock-ref`) |
| loose `refs/replace` refusal removed | replace |
| packed `refs/replace` refusal removed | replace-packed |
| `gitdir-info` catch-all made a no-op | info-entry |
| `gitdir-entry` catch-all made a no-op | unknown-entry |
| `modules` moved to the ignored list | modules |
| reftable check removed | reftable |
| bare-index check made a no-op | bare-index |

## Checkpoint (final, fresh, at ad54ea9c)

Run `20261008-001223-6831`:
- `--serial`, `--rows` set to all 12 rows, one isolated build (`bin-c1105-promisor/`, UseAppHost=false, 183 s).
- Result: exit 0, GREEN. Wall 83m22s. 216 tests executed, 216 passed.
- `validate --evidence ... --expected-source-sha ad54ea9c` printed `CHECKPOINT SOURCE VALID source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 rows=12`.
- The tool reported `unlisted: none` and deleted its `bin-c1105-promisor/` output itself.

CHECKPOINT CP-1 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=ok filter=/*/*/DockerStackDocumentationTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-3/run.trx slot=granted waited=60s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1087_Host_census_filters_and_names_cause*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_audits_work_as_1654*)|(C1008_Recycle_audits_promisor_checkout*)|(C1105_Git_audit_*) executed=80 passed=80 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-11/run.trx slot=granted waited=15s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RollingProductionMountTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-4/run.trx slot=granted waited=15s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RetiredTempContainerScriptTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-5/run.trx slot=granted waited=15s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/HostJqPrerequisiteScriptTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)|(C1105_Generation_identity_refuses_mismatch*)|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)|(C1008_Recycle_exact_default_volumes*)|(C1008_Recycle_dry_run_never_mutates*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-8/run.trx slot=granted waited=30s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)|(C1105_Resume_identity_refusals*)|(C1105_Resume_state_init_image_follows_the_journal*)|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RetiredTempContainerHostTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)|(C1008_Recycle_receipt_records_disk_and_partial_failure*)|(C1008_Recycle_refuses_references_and_unknown_census*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-9/run.trx slot=granted waited=15s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 build=reused filter=/*/*/RollingVolumeRecycleScriptTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-1a2ce245/.antiphon/checkpoints/20261008-001223-6831/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=ad54ea9cba8fa1faac01f2adbe83e5120cf0dff0 sourceState=clean buildSource=verified

Row wall times (host): CP-1 6 s, CP-2 13 s, CP-3 4 s, CP-11 10m31s, CP-4 66 s, CP-5 3m46s, CP-6 7m23s, CP-8 4m01s, CP-10 1m56s, CP-7 12m17s, CP-9 16m03s, CP-12 19m29s.

The TRX for CP-11 contains every intended case: 42 `C1105_Git_audit_hidden_locations`, 11 `seed_content`, 10 `recovery_tips`, 5 `index_content`, 3 `object_completeness`, 1 `resume`, and the 8 C1008/C1087 methods.

## Every build and test run

1. Checkpoint tool driver build to `bin-c1105r3drv/`. Lease a7aa0f1e, slot granted, held 10 s, 0 errors. The output was deleted afterwards.
2. Pre-check checkpoint run `20261007-234848-58ab` on `42c379b6`.
   - Rows `--rows CP-1,CP-11`, `--serial`.
   - CP-1 14/14 and CP-11 80/80, slot granted, 15m39s wall, one build of 338 s.
   - Its purpose was to catch a red CP-11 before the 80-minute run. It is superseded by run 10.
3. Mutation-proof build of `tests/Antiphon.Tests` to `bin-c1105r3mut/`. Lease e4f9dd82, waited 15 s, held 195 s, 0 errors. All 27 of its output directories were deleted.
4. `C1008_Recycle_audits_promisor_checkout` with the count mutant: 1 failed, as intended. 1m00s, lease a4f57e62.
5. `Main_volume_recycling_is_scripted_only` with the old sentence restored: 1 failed, as intended. 88 ms, lease a4aff2b0.
6. `C1105_Git_audit_hidden_locations*` with the 039b2661 script: 42 total, 22 failed, 20 passed. 19 s, lease f0e36db5.
7. An invalid OR filter across two classes: 0 tests ran (filter parse error, exit 134). Lease 40170edc, held 1 s.
8. Green controls on the restored source:
   - promisor plus hidden locations: 43/43 in 1m41s, lease 95a93851;
   - `Main_volume_recycling_is_scripted_only`: 1/1 in 1 s, lease bcd5372d.
9. Unlisted runs outside the build slot: direct `node scripts/fixtures/c1105-git-audit.cjs <row> <script>` invocations. These are the same body each C# case runs: node plus git in /tmp, with no dotnet process. They were used for:
   - the baseline matrix (42 rows plus `long-lived`, against the new and the old script);
   - 30 earlier C1105 variants on the new script;
   - the 19-mutant matrix, each mutant run against its rows plus `seed` and `long-lived`.
   - They are disclosed here as unleased. Every claim they support was confirmed again through TUnit in runs 6, 8 and 10.
10. Final run `20261008-001223-6831`, described above.

## Other checks

- Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef b5e78700ae9a76430c13d75cc03399055dd82e59 -HeadRef HEAD` at ad54ea9c gave `commits=9 entries=3 violations=0`. It is rerun at the report commit before the final message.
- Merge with master: origin/master is now `526cea60cb8c8c23e6bf60bfaf5d084433a3a13d`, 5 commits past e2c51501, and none of them touch the shared files. `git merge-tree --write-tree HEAD origin/master` exits 0.
- Platform: `GET /api/runner-defaults` returned revision 2, globalRunnerId server2, no kind defaults. `GET /api/session-runners` returned:
  - desktop: Windows, capacity 2, occupied 0, accepting;
  - server2: Linux, capacity 10, draining, not accepting;
  - server2-temp: Linux, capacity 10, occupied 7, accepting. This run used server2-temp.
  - No host or platform pin was used.

## PCs pending for SourceLanding Mutation

The following stay pending; none is discharged by this round:
- PC-1;
- PC-2 (its detector is now the count-only block);
- PC-3;
- PC-D1 through PC-D6 with all their variants;
- PC-doc (now also the false-sentence restore);
- PC-D7, one variant per hidden-location row, with the mutation for each named above.

The 15 retained refusal rows need a two-guard variant to go red.

Restart: none. This is a deploy-time script plus tests, with no server or runner code.
