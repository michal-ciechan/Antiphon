# CARD-1105 audit repair D1-D6 — Code report a3532cb8

Implementation is pushed, but Final verification is not clean: 172/173 passed, 1 failed. One existing diagnostic assertion still needs a production check-order repair. The brief's two-round cap has been reached; approval for another round was requested while the remaining authorized rows ran. No approval has arrived. Do not land or send this candidate to clean Review yet.

Task: a3532cb8-5815-4bad-8c6e-72931b340785. Original Code/landing owner: **6192d44b-9ec0-43a8-aed1-81fdb4565fc2**. Branch: feat/card-task-a3532cb8. Worktree: /work/worktrees/task-a3532cb8. The desktop checkout C:\Antiphon\worktrees\card-task-a3532cb8 is not reachable here.

Reviewed/task base: 4bfc7379df6465df75481ac5a0f8069d435f3755. Original complete-candidate base: b5e78700ae9a76430c13d75cc03399055dd82e59. Initial implementation commit: 554faf1354cd98fd6b33e8f3ec00f496bddaa894. Actual final tested source: **fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219**. Both implementation slices were committed and pushed immediately. A later report-only commit does not relabel these test receipts.

Initial fetch observed origin/master=3eeea31cc261c7265e81b1b05c8bc7b40079886e and merge-tree exited 0, tree ec239f5171c8b2dc7046b8f26a40a0bdada9133f. Final fetch/merge/remote/evidence-guard facts accompany the final caller report. This branch was never rebased, reset, amended or force-pushed.

Changed source: scripts/c590-remote.sh; scripts/fixtures/c1105-git-audit.cjs; tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs; docs/docker-stack.md; docs/investigations/2026-10-07-card-1105-git-audit-promisor.md. The latter is the plan and closed checkpoint manifest. The new fixture executes extracted production code with only the owned /work path boundary remapped; Git remotes use local file transport and Docker is stubbed at the receipt boundary. Nothing contacted production server2. GET /api/runner-defaults and GET /api/session-runners succeeded against the configured control-plane API; no runner/platform pin or dispatch was used.

## Repairs and detecting evidence

| Defect | Implemented behavior | Red proof against 4bfc7379 |
|---|---|---|
| D1 | All ref namespaces, all worktree/private refs and HEADs including bare, both reflog OIDs; malformed/unsupported logs refuse; replacement objects disabled and grafts refused | stash, reflog, oldest old reflog OID, recovery namespace, secondary-remote-only ref, private worktree ref, linked private ref, bare detached HEAD, reflog symlink were accepted by the reviewed helper and fail their refusal assertions |
| D2 | Missing index is accepted only with no files/subdirectories outside top-level .git, no stash and at most HEAD deletions; content is independently protected by publication proof | no-index stash and empty-HEAD ignored file were accepted at the reviewed SHA; repaired fixtures refuse dirty |
| D3 | Reject hidden/sparse index flags; hash actual tracked file/symlink bytes against the index regardless of cached stat data; status protects staged/intent-to-add changes | assume-unchanged, skip-worktree and a stat-matching changed file fail on reviewed source and refuse on repair; racy fixture first asserts ordinary status is empty |
| D4 | Full unexcluded commit/tree traversal over local/recovery and advertised origin roots; missing blobs allowed; every tip must peel to commit | missing ancestor commit and missing ancestor tree were accepted at reviewed source; repair refuses unknown while a real blobless seed passes |
| D5 | Fixture origin enables filter support and asserts actual missing HEAD blob; same production seed command retained | extracting the original C1008SeedPromisor produced missingBlobs=0 with filteringWarning=true; repair gives missingBlobs=1 and no filtering warning |
| D6 | auditFailure stores diagnostics while saved audit proof survives; first refusal still initializes audit | old wrapper overwrites audit and fails proof equality after local-origin outage; repaired real wrapper/comparison preserves proof and resumes |

D2 policy decision: a previously used checkout emptied to the exact no-index/no-files seed shape has no remaining worktree/index content to lose. It is accepted only after all retained Git refs/reflogs prove publication. No marker or unverifiable seed provenance is inferred. Reflog-only rewritten commits are recoverable work and count; abandonment is not inferred. The actual seed command and fetch-only local fixture leave published reflogs and pass. The supplied 390-repository replay says zero shallow/locks but does not establish live reflog contents; no new live observation was made.

New local audit cases: 29/29 passed in CP-11. Seventeen detecting variants fail on reviewed production source; twelve additional positive/retained regression variants already pass there and are explicitly not claimed as new defect detectors. The racy fixture was strengthened after its first baseline run to assert status really missed the change; its strengthened baseline is red. D5 separately establishes the old fixture's false blobless assumption. No deliberate production mutant was run: governing Code-stage instructions reserve those cycles and missing-control discovery for SourceLanding Mutation.

## Remaining failure and concrete repair

C1008_Recycle_refuses_uninspectable_git fails at RemoteScriptContractTests.cs:628: expected check=origin-advertisement for a deleted/missing/stale origin-tracking reference, but got audit check=for-each-ref status=2 repo=repo. The new all-ref stderr guard encounters the broken origin symbolic ref before the current-origin equality check. Classification remains RecycleGitAuditUnknown and removal is refused; the diagnostic contract is not satisfied. This is introduced by this task, not blamed on an inherited failure. No assertion was changed or weakened.

Proposed next Code slice: move the worktree-list/consider_tips block from before ls-remote to immediately after current origin-advertisement validation, retaining every safety guard. This preserves the existing diagnostic precedence. The exact unexecuted proposal follows at the end of this report. It has not been applied or tested. The brief explicitly says “repair cap two rounds”; continuing requires another authorized round or a fresh Code task. No passing Review is claimed.

Round 1 had two receipt-name failures (object-completeness versus check=rev-list). Round 2 changed the name to rev-list-objects, preserving both existing assertions, and added the reflog-type refusal. Both original name failures are now resolved; the origin-advertisement assertion was reached later in the same method and remains red. No timeout was widened and no retry was added.

## Ordinary outcomes

| ID | Actual outcome |
|---|---|
| V-1 recovery tips | PASS, all 10 variants |
| V-2 seed/content | PASS, all 10 variants |
| V-3 flags/stat data | PASS, all 5 variants |
| V-4 completeness | PASS, all 3 object-loss variants and positive missing-blob seed |
| V-5 true seed shape | PASS, existing C1008 promisor method plus direct shape measurement |
| V-6 saved proof/resume | PASS |
| R-1 closed regression list | FAIL, 172 passed / 1 failed / 0 skipped |
| R-2 no fetch / per-Git environment | PASS in all 29 new cases |
| R-3 classifications/receipts/UID/count failures | FAIL on the one origin-advertisement diagnostic assertion; other CP-11 methods pass |

Final profile uses the brief's explicit 12-row scope. The whole Unit lane and full assembly were excluded, not deferred or claimed passed. No unbounded shared service impact was introduced. All named ordinary rows ran; R-1/R-3 remain unresolved rather than passed. Production activation is outside this offline Code task.

| CP | Executed | Passed | Failed | Skipped | Driver time | Slot | Waited |
|---|---:|---:|---:|---:|---|---|---|
| CP-1 | 14 | 14 | 0 | 0 | 0m 4s | granted | 0s |
| CP-2 | 3 | 3 | 0 | 0 | 0m 6s | granted | 0s |
| CP-3 | 6 | 6 | 0 | 0 | 0m 3s | granted | 0s |
| CP-11 | 37 | 36 | 1 | 0 | 7m 7s | granted | 0s |
| CP-4 | 12 | 12 | 0 | 0 | 1m 5s | granted | 0s |
| CP-5 | 13 | 13 | 0 | 0 | 3m 4s | granted | 0s |
| CP-6 | 39 | 39 | 0 | 0 | 7m 1s | granted | 0s |
| CP-8 | 5 | 5 | 0 | 0 | 3m 47s | granted | 0s |
| CP-10 | 4 | 4 | 0 | 0 | 1m 30s | granted | 0s |
| CP-7 | 21 | 21 | 0 | 0 | 11m 54s | granted | 0s |
| CP-9 | 3 | 3 | 0 | 0 | 15m 5s | granted | 0s |
| CP-12 | 16 | 16 | 0 | 0 | 19m 7s | granted | 0s |

Checkpoint 20261007-193034-be09: total wall 72m 39s. One isolated test build per run, serial drivers, UseAppHost=false, TUNIT_MAX_PARALLEL_TESTS=1, expected-source-sha=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219. Final build: 2m 44s, slot=granted, waited=0s. Source stayed frozen throughout each run. Fresh TRX inspection found 173 distinct executions and the intended full class/method/variant rosters; all existing rosters match the prior Review baseline. The one failed result remains recorded. Do not infer a clean certificate from clean source alone.

Initial checkpoint 20261007-191556-a81f on 554faf1354cd98fd6b33e8f3ec00f496bddaa894 built in 3m 57s (slot=granted, waited=0s). Completed rows: CP-1 14/14, CP-2 3/3, CP-3 6/6, CP-4 12/12, CP-11 34/36 with two failures. CP-5 was stopped in progress with no completed TRX; later rows were not run in that attempt. Stop was awaited and owned processes were confirmed absent before editing. The wait reports exit 6 after explicit stop (state=stopped), not an unexplained process crash. Its outputs were cleaned using the checkpoint tool before the second isolated build.

Initial attempt driver details (all slot=granted, waited=0s):

| Row | Result | Driver time |
|---|---|---|
| CP-1 | executed=14 passed=14 failed=0 skipped=0 | 4.10s |
| CP-2 | executed=3 passed=3 failed=0 skipped=0 | 6.47s |
| CP-3 | executed=6 passed=6 failed=0 skipped=0 | 4.22s |
| CP-11 | executed=36 passed=34 failed=2 skipped=0 | 356.45s |
| CP-4 | executed=12 passed=12 failed=0 skipped=0 | 62.96s |
| CP-5 | Interrupted; completed count unknown, no TRX | About 50s to the recorded stop; lease 27c86411-abac-402e-81ec-71be89fafe8c |

Additional drivers were narrow baseline/shape proofs required by D1-D6, plus the checkpoint bootstrap, not broad unlisted suites. All were leased:

| Driver | Counts/outcome | Lease / duration |
|---|---|---|
| Current helper contracts | 27 passed | BUILD SLOT granted lease=84914b90-61e3-4655-bb63-813694cde498 waited=0s maxcpucount=6; BUILD SLOT released lease=84914b90-61e3-4655-bb63-813694cde498 held=10s |
| Reviewed helper baseline | 27 observations: 14 expected reds, 13 passes | BUILD SLOT granted lease=8db74e26-1b40-43ec-8feb-518342fad5ec waited=0s maxcpucount=6; BUILD SLOT released lease=8db74e26-1b40-43ec-8feb-518342fad5ec held=9s |
| Strengthened racy and empty-HEAD ignored cases | 2 repair passes, 2 reviewed reds | BUILD SLOT granted lease=d3d9b9d1-3179-4040-ae9b-8101427bdd62 waited=0s maxcpucount=6; BUILD SLOT released lease=d3d9b9d1-3179-4040-ae9b-8101427bdd62 held=2s |
| Original/repaired C# seed fixture | 1 reviewed shape red, 1 repaired shape pass | BUILD SLOT granted lease=783576f2-99e9-4d47-93f9-be7ae4d1e959 waited=0s maxcpucount=6; BUILD SLOT released lease=783576f2-99e9-4d47-93f9-be7ae4d1e959 held=0s |
| Unsupported reflog type | 1 repair pass, 1 reviewed red | BUILD SLOT granted lease=6f457e05-e874-457e-bbea-a64064ee01b5 waited=0s maxcpucount=6; BUILD SLOT released lease=6f457e05-e874-457e-bbea-a64064ee01b5 held=1s |
| Checkpoint tool bootstrap | 1 build, 0 errors, 1 pre-existing nullable warning | BUILD SLOT granted lease=48e4d92f-1187-4d40-b070-6833a3ce0762 waited=0s maxcpucount=6; BUILD SLOT released lease=48e4d92f-1187-4d40-b070-6833a3ce0762 held=10s |

Read-only syntax/evidence inspections are not extra test builds. No loaded repetitions, full-assembly run or mutation qualification occurred. Repeated ordinary rows were triggered by the recorded production fix and clean source qualification, not a green-proof repetition loop.

Evidence root: /work/worktrees/task-a3532cb8/.antiphon/a3532cb8-evidence. Checkpoint receipts/TRX/logs: /work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/ and /work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/. The prior Review and recovered Code report were read from /work/review-evidence/4a14585d/review.md and code-report.txt because the named branch report paths were absent. Generated receipts/logs remain ignored; only this Markdown report is committed.

Final source validation: full report correctly refused with exit 2, CHECKPOINT SOURCE INVALID reason=row_failed. Selecting the eleven green rows validated with exit 0: CHECKPOINT SOURCE VALID source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 rows=11. All 173 fresh executions, exact classes and method/variant rosters were independently checked. There is no clean full-candidate certificate.

Build accounting: initial test build 636 warnings / 0 errors; second test build 637 warnings / 0 errors; bootstrap 1 warning / 0 errors. Warning totals are observed counts, not a blanket claim that each warning was baseline-verified. Final cleanup removed all 28 producer-owned bin-c1105-promisor directories through the checkpoint tool; the separate bootstrap directory was removed after checking its exact path and producer DLL hash. Initial cleanup also removed its 28 isolated outputs. No build/test process remains owned by this task.

Final fetch still observed origin/master=3eeea31cc261c7265e81b1b05c8bc7b40079886e. The tested source fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 merges cleanly onto it (merge-tree exit 0, tree 57de812a0aaa258a60a2cab4b661359393009630). Source-range evidence guard b5e78700ae9a76430c13d75cc03399055dd82e59..fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 passed, commits=4, entries=0, violations=0. The full report-commit range and final remote SHA are verified after this report is committed and stated in the caller summary.

For retention after mirror retirement, unedited generated evidence is also copied to /work/code-evidence/a3532cb8 (outside Git; no generated payload is committed). Native checkpoint copies live under checkpoints/<run-id>; the supplementary evidence is under diagnostics.

Rerun command after an authorized committed repair, using a slot-built checkpoint tool: dotnet <tool-output>/Antiphon.Checkpoints.dll run --plan docs/investigations/2026-10-07-card-1105-git-audit-promisor.md --rows CP-11 --serial --expected-source-sha <new-committed-SHA> --max-wait 50s; wait until exit is not 75. A fresh Final commissioning may require the complete twelve-row list again. The proposed patch below passed bash -n only; it has not been applied or behaviorally tested.

## Pending Mutation and activation

Every control remains pending for method-scoped SourceLanding Mutation: PC-1, PC-2, PC-3; PC-D1 (stash, reflog, reflog-old, reflog-symlink, recovery, secondary, worktree-ref, linked-head, linked-private, bare-head); PC-D2 (seed-untracked, seed-tracked, seed-ignored, seed-empty-ignored, seed-stash, staged, modified); PC-D3 (assume, skip, intent, sparse, racy); PC-D4 (missing-commit, missing-tree, missing-head-tree); PC-D5 (filter, clone-flags); PC-D6; PC-doc (DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only). The plan maps each method and deliberate defect. Code baseline reds do not discharge any PC, and no red/restore/green mutation qualification is claimed.

restart: none. Owner of later activation: caller/orchestrator. After ordinary verification and separate Review, the caller lands original Code owner 6192d44b-9ec0-43a8-aed1-81fdb4565fc2 with this repair adopted, commissions SourceLanding Mutation, pulls canonical, restarts AppHost with ExpectedServerSha, then follows check-census, check-host-jq, redeploy-old -DryRun and the authorized redeploy-old of the landed SHA. No land/deploy is this task's next stage.

## Unedited CHECKPOINT receipts

Initial attempt:

```text
CHECKPOINT CP-1 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=ok filter=/*/*/DockerStackDocumentationTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1087_Host_census_filters_and_names_cause*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_audits_work_as_1654*)|(C1008_Recycle_audits_promisor_checkout*)|(C1105_Git_audit_*) executed=36 passed=34 failed=2 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=reused filter=/*/*/RollingProductionMountTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
```

Second attempt:

```text
CHECKPOINT CP-1 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=ok filter=/*/*/DockerStackDocumentationTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1087_Host_census_filters_and_names_cause*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_audits_work_as_1654*)|(C1008_Recycle_audits_promisor_checkout*)|(C1105_Git_audit_*) executed=37 passed=36 failed=1 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RollingProductionMountTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RetiredTempContainerScriptTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/HostJqPrerequisiteScriptTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)|(C1105_Generation_identity_refuses_mismatch*)|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)|(C1008_Recycle_exact_default_volumes*)|(C1008_Recycle_dry_run_never_mutates*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)|(C1105_Resume_identity_refusals*)|(C1105_Resume_state_init_image_follows_the_journal*)|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RetiredTempContainerHostTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)|(C1008_Recycle_receipt_records_disk_and_partial_failure*)|(C1008_Recycle_refuses_references_and_unknown_census*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 build=reused filter=/*/*/RollingVolumeRecycleScriptTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-193034-be09/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 sourceState=clean buildSource=verified
```

## Unexecuted proposed check-order patch

```diff
diff --git a/scripts/c590-remote.sh b/scripts/c590-remote.sh
index 6c6bb5c37..2172b873c 100644
--- a/scripts/c590-remote.sh
+++ b/scripts/c590-remote.sh
@@ -4611,27 +4611,6 @@ while IFS= read -r -d '' entry; do
         true) ;;
         *) refuse_unknown bare 2 "$repo" ;;
     esac
-    audit_repo="$repo"
-    audit_check=worktree-list
-    git -C "$repo" worktree list --porcelain -z > "$scratch/worktrees" 2>/dev/null || fail $?
-    : > "$scratch/tips"
-    consider_tips "$repo"
-    while IFS= read -r -d '' field; do
-        [[ "$field" == worktree\ * ]] || continue
-        audit_check=worktree-path
-        work="${field#worktree }"
-        work="$(readlink -e "$work")" || fail $?
-        [[ "$work/" == "$root/"* ]] || refuse_unknown worktree-confine 0 "$repo"
-        audit_check=worktree-bare
-        work_bare="$(git -C "$work" rev-parse --is-bare-repository 2>/dev/null)" || fail $?
-        if [ "$work_bare" = false ]; then
-            consider_dirty "$work" worktree-status
-        elif [ "$work_bare" != true ]; then
-            refuse_unknown worktree-bare 2 "$work"
-        fi
-        consider_tips "$work"
-    done < "$scratch/worktrees"
-    audit_repo="$repo"
     audit_check=ls-remote
     timeout --kill-after=5s 30s git -C "$repo" ls-remote --heads origin > "$scratch/origin" 2>/dev/null || fail $?
     audit_check=origin-parse
@@ -4656,6 +4635,27 @@ while IFS= read -r -d '' entry; do
         local_refs="$(awk '$2!="refs/heads/HEAD" {print}' "$scratch/local" | sort)" || fail $?
         [ "$origin" = "$local_refs" ] || refuse_unknown origin-advertisement 2 "$repo"
     fi
+    audit_repo="$repo"
+    audit_check=worktree-list
+    git -C "$repo" worktree list --porcelain -z > "$scratch/worktrees" 2>/dev/null || fail $?
+    : > "$scratch/tips"
+    consider_tips "$repo"
+    while IFS= read -r -d '' field; do
+        [[ "$field" == worktree\ * ]] || continue
+        audit_check=worktree-path
+        work="${field#worktree }"
+        work="$(readlink -e "$work")" || fail $?
+        [[ "$work/" == "$root/"* ]] || refuse_unknown worktree-confine 0 "$repo"
+        audit_check=worktree-bare
+        work_bare="$(git -C "$work" rev-parse --is-bare-repository 2>/dev/null)" || fail $?
+        if [ "$work_bare" = false ]; then
+            consider_dirty "$work" worktree-status
+        elif [ "$work_bare" != true ]; then
+            refuse_unknown worktree-bare 2 "$work"
+        fi
+        consider_tips "$work"
+    done < "$scratch/worktrees"
+    audit_repo="$repo"
     audit_check=tips
     sort -u "$scratch/tips" > "$scratch/unique" || fail $?
     [ -s "$scratch/unique" ] || refuse_unknown tips-empty 0 "$repo"

```

--- next stage ---
next: decide
handoff: Authorize one additional Code repair round or a fresh Code continuation for the remaining origin-advertisement receipt assertion. Apply the unexecuted check-order patch above, retain every guard, and rerun CP-11. All PCs stay pending for SourceLanding Mutation. Original landing owner: 6192d44b-9ec0-43a8-aed1-81fdb4565fc2.
artifact: docs/investigations/2026-10-07-card-1105-git-audit-promisor.md
