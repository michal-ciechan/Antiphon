# CARD-1105 audit repair D1-D6 — completed Code report a3532cb8

The authorized check-order repair passed: **77/77 fresh cases, zero failed or skipped**. Origin advertisement is checked before recovery-tip enumeration; both checks remain fail-closed and the existing assertion is unchanged. All commissioned ordinary V/R checks are complete using this affected-row rerun plus the prior full-run evidence for eight unaffected rows (96 cases). This supersedes the earlier blocked report and its unexecuted proposal. Next: **review**. Every positive control remains pending SourceLanding Mutation.

Task: **a3532cb8-5815-4bad-8c6e-72931b340785**. Original Code/landing owner: **6192d44b-9ec0-43a8-aed1-81fdb4565fc2**. Branch: **feat/card-task-a3532cb8**. Worktree: **/work/worktrees/task-a3532cb8**. The desktop checkout C:\Antiphon\worktrees\card-task-a3532cb8 is not reachable here.

Task base: **4bfc7379df6465df75481ac5a0f8069d435f3755**. Original full-candidate base: **b5e78700ae9a76430c13d75cc03399055dd82e59**. Final tested source (R3): **57178fff2b2d1486d734b1e86ecb6a05480a18a2**. Prior full run (R2): **fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219**. Source slices 554faf1354cd98fd6b33e8f3ec00f496bddaa894, fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 and 57178fff2b2d1486d734b1e86ecb6a05480a18a2 were committed and pushed before their checkpoint runs. Report-only commits do not relabel test receipts; the final report commit and matching remote SHA are given in the caller summary/publication record.

Changed deliverables: scripts/c590-remote.sh; scripts/fixtures/c1105-git-audit.cjs; tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs; docs/docker-stack.md; docs/investigations/2026-10-07-card-1105-git-audit-promisor.md (plan and closed manifest); this report. The last production patch only relocates the existing worktree-list/consider_tips block after origin-advertisement validation. Static comparison confirms the complete production line multiset is unchanged and the test source is byte-for-byte unchanged (SHA-256 18a0e4567329bdf00bbbaa5e1e2c14013effb1c50bffbc6e386b6d27ef09d024).

## Final repair and scope

The prior full run had one introduced diagnostic failure: C1008_Recycle_refuses_uninspectable_git expected check=origin-advertisement but got check=for-each-ref when broken origin tracking was seen by recovery enumeration first. Both runs refused removal as RecycleGitAuditUnknown. The caller explicitly authorized one additional, final repair round. Moving the unchanged origin proof first makes the receipt name the first failing check deterministically. The unchanged method now passes in a fresh TRX (executionId 4669eca3-94cd-46c2-8913-f9b44b061372, duration 173.2791011s), including deleted/missing/stale origin diagnostics and the later fault cases. Both fail-closed checks are still required; no assertion, timeout, retry policy or fixture was changed in R3.

R3 ran CP-2, CP-11, CP-7 and CP-12. CP-11 covers the audit/receipt matrix and all D1-D6 cases. CP-7's full class exercises published, dirty and unpublished real Git repositories through retirement. CP-12's full wrapper class is expressly required by the caller. CP-2 is the registry guard. Every intended class, method and parameter variant was checked in fresh TRX, with unique execution IDs, fresh start/end times, nonzero counts and unchanged original rosters.

The other eight rows retain their green evidence from run 20261007-193034-be09, source fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 (96 cases). CP-1 pins unchanged documentation contract sentences; CP-3 pins seed/compose/deploy text outside the audit; CP-4 invokes compose/mount/generation helpers, not the Git audit; CP-5 uses wrapper/cleanup/bridge fixtures without a materialized Git repository; CP-6 exercises prerequisite admission and refuses before the Git audit; CP-8, CP-9 and CP-10 use empty-work Git inventories or refuse before the audit, so none enters the reordered per-repository checks. No assertion, timeout, filter, expected count, or fixture changed in this extra round. Whole Unit and deliberate PC mutants remain outside Code's commissioned scope.

The scope is bounded to the recycle script and named script contracts. No service implementation for delivery, landing, leases or persistence changed; no unbounded shared impact or full-assembly run was introduced. No whole-Unit run was performed or claimed. Deferred-to-final ordinary IDs: **none**. PCs are a separate pending stage, not deferred ordinary checks.

## Ordinary outcomes

| ID | Actual outcome |
|---|---|
| V-1 | PASS: all 10 retained recovery-tip variants, R3 CP-11 |
| V-2 | PASS: all 10 seed/content variants, R3 CP-11 |
| V-3 | PASS: all 5 flag/stat variants, R3 CP-11 |
| V-4 | PASS: all 3 required-object-loss variants and missing-blob positive seed, R3 CP-11 |
| V-5 | PASS: actual deploy clone line with genuinely absent blobs, R3 CP-11 and retained shape measurement |
| V-6 | PASS: outage preserves saved proof and restoration resumes, R3 CP-11 |
| R-1 | PASS: full 12-row ordinary scope completed; 77 affected/registry cases R3 plus 96 unaffected cases R2, explicitly source-attributed |
| R-2 | PASS: no lazy fetch and required environment at every Git start in all 29 new cases, R3 CP-11 |
| R-3 | PASS: unchanged classification/receipt/UID/count-failure assertions, R3 CP-11 |

| CP | Source run | Executed | Passed | Failed | Skipped | Driver time | Slot | Waited |
|---|---|---:|---:|---:|---:|---|---|---|
| CP-1 | R2 | 14 | 14 | 0 | 0 | 0m 4s | granted | 0s |
| CP-2 | R3 | 3 | 3 | 0 | 0 | 0m 6s | granted | 0s |
| CP-3 | R2 | 6 | 6 | 0 | 0 | 0m 3s | granted | 0s |
| CP-11 | R3 | 37 | 37 | 0 | 0 | 8m 24s | granted | 0s |
| CP-4 | R2 | 12 | 12 | 0 | 0 | 1m 5s | granted | 0s |
| CP-5 | R2 | 13 | 13 | 0 | 0 | 3m 4s | granted | 0s |
| CP-6 | R2 | 39 | 39 | 0 | 0 | 7m 1s | granted | 0s |
| CP-8 | R2 | 5 | 5 | 0 | 0 | 3m 47s | granted | 0s |
| CP-10 | R2 | 4 | 4 | 0 | 0 | 1m 30s | granted | 0s |
| CP-7 | R3 | 21 | 21 | 0 | 0 | 11m 30s | granted | 0s |
| CP-9 | R2 | 3 | 3 | 0 | 0 | 15m 5s | granted | 0s |
| CP-12 | R3 | 16 | 16 | 0 | 0 | 18m 1s | granted | 0s |

R3 run **20261007-205352-2b6a**: wall **40m 48s**, one isolated test build **166.5662988s**, 636 warnings / 0 errors, UseAppHost=false, serial drivers, TUNIT_MAX_PARALLEL_TESTS=1. Maximum concurrent builds=1, rows=1. All build/test leases were granted with waited=0s. The current source stayed clean and frozen throughout. No repeated proof run was added after green.

The only additional build in this extra round was the necessary checkpoint-tool bootstrap after prior cleanup: 1 build, 8.54s compiler time / 9s lease held, 1 nullable warning / 0 errors, lease aa20cd5a-86fd-436d-b0c8-ba3278e24899, slot=granted waited=0s. Read-only static, TRX and receipt inspections are not extra test drivers. Warning totals are observations, not a claim that each warning was independently baseline-verified.

R3 leases, unedited:

```text
2026-10-07T20:53:54.5159793+00:00 BUILD SLOT granted lease=885bc121-7241-4798-889a-fe0a2de58805 label="build:bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T20:56:41.3041132+00:00 BUILD SLOT granted lease=4c912a7e-56ea-47fa-a9a6-ef115abce58e label="CP-2@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T20:56:46.9191554+00:00 BUILD SLOT granted lease=4d35d526-7bf7-4091-b06e-59087d72357d label="CP-11@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T21:05:10.6610628+00:00 BUILD SLOT granted lease=b62c31c2-86db-4cfb-87d2-5dfd72581349 label="CP-7@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T21:16:40.8115609+00:00 BUILD SLOT granted lease=86e9490e-54c0-432b-95f5-bd7a7e59dfde label="CP-12@bin-c1105-promisor" waited=0s maxcpucount=6
```

Source validation exited 0 for all four R3 rows and separately for the eight unaffected R2 rows:

```text
CHECKPOINT SOURCE VALID source=57178fff2b2d1486d734b1e86ecb6a05480a18a2 rows=4
CHECKPOINT SOURCE VALID source=fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 rows=8
```

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

## Earlier attempts and supplementary red evidence

R2 run 20261007-193034-be09 executed the entire twelve-row list: 173 executed / 172 passed / 1 failed / 0 skipped, wall 72m39s. Its build took 164.0312175s, with 637 warnings and zero errors. The one failure was the diagnostic order repaired by R3. The prior whole-run source validator correctly exited 2 (row_failed); the eight unaffected rows now selected for retained evidence validate against their own SHA. The red result remains in the unedited receipts below and is not recast as a passing run.

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

## Provenance, publication and cleanup

Latest fetch: origin/master=3eeea31cc261c7265e81b1b05c8bc7b40079886e. The tested source merges cleanly onto it: git merge-tree --write-tree HEAD origin/master exited 0, tree 99b3cecb58d7bdef12ea9061e6fd82ce9a33e878. No rebase, reset, amend or force-push occurred. The final report-only commit is checked and pushed after this report is written, then the remote ref is verified with git ls-remote. The exact final SHA and full-range guard result accompany the caller summary.

Source-range evidence guards both passed, unedited:

```text
EVIDENCE range base=4bfc7379df6465df75481ac5a0f8069d435f3755 head=57178fff2b2d1486d734b1e86ecb6a05480a18a2
EVIDENCE result commits=4 entries=2 violations=0 base=4bfc7379df6465df75481ac5a0f8069d435f3755 head=57178fff2b2d1486d734b1e86ecb6a05480a18a2
EVIDENCE range base=b5e78700ae9a76430c13d75cc03399055dd82e59 head=57178fff2b2d1486d734b1e86ecb6a05480a18a2
EVIDENCE result commits=6 entries=2 violations=0 base=b5e78700ae9a76430c13d75cc03399055dd82e59 head=57178fff2b2d1486d734b1e86ecb6a05480a18a2
```

The full task-base..final-report-HEAD guard is also required after the report commit; its final output is retained as evidence-diff-task-final-round3.log, with the broader original-base range in evidence-diff-full-final-round3.log. No generated evidence is committed. The only committed evidence artifact is this standalone Markdown report, under the allowed size limit.

Checkpoint cleanup reports cleanedOutputs=true. All producer-owned bin-c1105-promisor project outputs and the checkpoint shadow copy were removed; the bootstrap bin-a3532cb8-tool directory was removed after checking its exact path and producer DLL hash. The remaining paths named bin-c1105-promisor inside checkpoint/builds are evidence directories containing build.log, not project binaries. All owned runs have completed and no test/build process remains.

Native evidence: /work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-205352-2b6a/ (report.json, report.md, fresh TRX, console/build logs and source provenance). Supplementary evidence: /work/worktrees/task-a3532cb8/.antiphon/a3532cb8-evidence/ (roster-round3.json, order-round3.json, receipt-round3.log, bootstrap-round3.log, guards and publication record). Evidence preserved outside the retiring mirror: /work/code-evidence/a3532cb8/checkpoints/20261007-205352-2b6a/ and /work/code-evidence/a3532cb8/round3/; earlier evidence remains intact in its existing directories. Current report copy: /work/code-evidence/a3532cb8/round3/task-a3532cb8.md.

The earlier Review and recovered Code report were read from /work/review-evidence/4a14585d/review.md and code-report.txt because the named branch report files were absent. GET /api/runner-defaults and GET /api/session-runners were read through the configured control-plane API. No host pin was embedded and no production host execution or deployment occurred. Git test remotes use local file transport; Docker/HTTP boundaries are isolated fixtures.

To reproduce the authorized selection, bootstrap tools/Antiphon.Checkpoints through scripts/build-slot.ps1 into an isolated output, then run its DLL with: run --plan docs/investigations/2026-10-07-card-1105-git-audit-promisor.md --rows CP-2,CP-11,CP-7,CP-12 --serial --expected-source-sha 57178fff2b2d1486d734b1e86ecb6a05480a18a2 --max-wait 50s; continue wait --run <id> --max-wait 50s until exit is not 75. Check out the named source before replay; do not label a later report-only HEAD as the tested source.

## Pending Mutation and activation

Every control remains pending for method-scoped SourceLanding Mutation: PC-1, PC-2, PC-3; PC-D1 (stash, reflog, reflog-old, reflog-symlink, recovery, secondary, worktree-ref, linked-head, linked-private, bare-head); PC-D2 (seed-untracked, seed-tracked, seed-ignored, seed-empty-ignored, seed-stash, staged, modified); PC-D3 (assume, skip, intent, sparse, racy); PC-D4 (missing-commit, missing-tree, missing-head-tree); PC-D5 (filter, clone-flags); PC-D6; PC-doc (DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only). The plan maps each method and deliberate defect. Code baseline reds do not discharge any PC, and no red/restore/green mutation qualification is claimed.

restart: **none**. Owner of later activation: **caller/orchestrator**. Separate Review is next. After Review, the caller lands original Code owner 6192d44b-9ec0-43a8-aed1-81fdb4565fc2 with this repair adopted and commissions SourceLanding Mutation. No land or deployment is part of this Code task.

## Unedited CHECKPOINT receipts — all attempts

Earlier red/stopped attempt evidence is retained for provenance. Only the source-attributed final selection above supplies the completed ordinary verdict.

### 20261007-191556-a81f — source 554faf1354cd98fd6b33e8f3ec00f496bddaa894

```text
CHECKPOINT CP-1 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=ok filter=/*/*/DockerStackDocumentationTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1087_Host_census_filters_and_names_cause*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_audits_work_as_1654*)|(C1008_Recycle_audits_promisor_checkout*)|(C1105_Git_audit_*) executed=36 passed=34 failed=2 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=554faf1354cd98fd6b33e8f3ec00f496bddaa894 build=reused filter=/*/*/RollingProductionMountTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-191556-a81f/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=554faf1354cd98fd6b33e8f3ec00f496bddaa894 sourceState=clean buildSource=verified
```

### 20261007-193034-be09 — source fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219

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

### 20261007-205352-2b6a — source 57178fff2b2d1486d734b1e86ecb6a05480a18a2

```text
CHECKPOINT CP-2 commit=57178fff2b2d1486d734b1e86ecb6a05480a18a2 build=ok filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-205352-2b6a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=57178fff2b2d1486d734b1e86ecb6a05480a18a2 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=57178fff2b2d1486d734b1e86ecb6a05480a18a2 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1087_Host_census_filters_and_names_cause*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_audits_work_as_1654*)|(C1008_Recycle_audits_promisor_checkout*)|(C1105_Git_audit_*) executed=37 passed=37 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-205352-2b6a/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=57178fff2b2d1486d734b1e86ecb6a05480a18a2 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=57178fff2b2d1486d734b1e86ecb6a05480a18a2 build=reused filter=/*/*/RetiredTempContainerHostTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-205352-2b6a/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=57178fff2b2d1486d734b1e86ecb6a05480a18a2 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=57178fff2b2d1486d734b1e86ecb6a05480a18a2 build=reused filter=/*/*/RollingVolumeRecycleScriptTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-a3532cb8/.antiphon/checkpoints/20261007-205352-2b6a/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=57178fff2b2d1486d734b1e86ecb6a05480a18a2 sourceState=clean buildSource=verified
```

## All checkpoint build and row lease identities

### 20261007-191556-a81f

```text
2026-10-07T19:15:58.7013910+00:00 BUILD SLOT granted lease=6e9e32a2-3737-4208-a61e-0dd6b52040e7 label="build:bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:19:56.4105283+00:00 BUILD SLOT granted lease=60d3c501-7411-4006-b957-0b357d62ef59 label="CP-1@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:20:00.5987552+00:00 BUILD SLOT granted lease=f077294c-5d73-42f4-8155-94d688a6ce70 label="CP-2@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:20:07.1260587+00:00 BUILD SLOT granted lease=ae8b8a4c-a75a-45fe-94c1-bf0a193c2ec6 label="CP-3@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:20:11.4143114+00:00 BUILD SLOT granted lease=990f6d72-5799-40db-aa0c-120993bc8fa0 label="CP-11@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:26:07.9162680+00:00 BUILD SLOT granted lease=d9ecafad-8dd3-4048-818d-d9b90bedb659 label="CP-4@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:27:10.9067201+00:00 BUILD SLOT granted lease=27c86411-abac-402e-81ec-71be89fafe8c label="CP-5@bin-c1105-promisor" waited=0s maxcpucount=6
```

### 20261007-193034-be09

```text
2026-10-07T19:30:37.2034815+00:00 BUILD SLOT granted lease=3121626b-d59f-4c96-8717-415ed9fcb133 label="build:bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:33:21.4494903+00:00 BUILD SLOT granted lease=4d5a9242-d194-42c0-9a9c-89a0140d9c4f label="CP-1@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:33:25.1416388+00:00 BUILD SLOT granted lease=23eeed8d-9fb9-4daa-ba2f-5b4ecccd0af2 label="CP-2@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:33:31.0004079+00:00 BUILD SLOT granted lease=2925a669-cb81-46d1-bc18-f4e032437f0b label="CP-3@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:33:34.3795362+00:00 BUILD SLOT granted lease=b111fb97-6c07-4dfe-9f9a-087c4598995a label="CP-11@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:40:41.8309232+00:00 BUILD SLOT granted lease=587c8fe2-035b-4949-ba81-348d37638781 label="CP-4@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:41:46.9397010+00:00 BUILD SLOT granted lease=a3ec83ec-daa9-4b78-8811-566e55253048 label="CP-5@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:44:50.8285527+00:00 BUILD SLOT granted lease=d212c6e0-96f5-441a-994e-28ad795ef596 label="CP-6@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:51:52.0125861+00:00 BUILD SLOT granted lease=57ade37a-4a5b-4b41-ad95-c597824706b3 label="CP-8@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:55:38.9581426+00:00 BUILD SLOT granted lease=2db6b215-8f76-439a-8b00-6754b0c4913c label="CP-10@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T19:57:09.0114240+00:00 BUILD SLOT granted lease=d20d8fdd-9b4b-4667-9342-ef21842e4b2d label="CP-7@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T20:09:03.5507650+00:00 BUILD SLOT granted lease=6e378e07-3d98-418d-9e5b-82f213836680 label="CP-9@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T20:24:08.6366478+00:00 BUILD SLOT granted lease=28ef5b16-f878-49cc-94d4-ded2a6192397 label="CP-12@bin-c1105-promisor" waited=0s maxcpucount=6
```

### 20261007-205352-2b6a

```text
2026-10-07T20:53:54.5159793+00:00 BUILD SLOT granted lease=885bc121-7241-4798-889a-fe0a2de58805 label="build:bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T20:56:41.3041132+00:00 BUILD SLOT granted lease=4c912a7e-56ea-47fa-a9a6-ef115abce58e label="CP-2@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T20:56:46.9191554+00:00 BUILD SLOT granted lease=4d35d526-7bf7-4091-b06e-59087d72357d label="CP-11@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T21:05:10.6610628+00:00 BUILD SLOT granted lease=b62c31c2-86db-4cfb-87d2-5dfd72581349 label="CP-7@bin-c1105-promisor" waited=0s maxcpucount=6
2026-10-07T21:16:40.8115609+00:00 BUILD SLOT granted lease=86e9490e-54c0-432b-95f5-bd7a7e59dfde label="CP-12@bin-c1105-promisor" waited=0s maxcpucount=6
```

--- next stage ---
next: review
handoff: Review the D1-D6 audit repair and final check-order fix; 77 affected/registry cases pass on 57178fff2b2d1486d734b1e86ecb6a05480a18a2 plus 96 unaffected prior cases. Preserve all pending SourceLanding PCs. Original landing owner: 6192d44b-9ec0-43a8-aed1-81fdb4565fc2.
artifact: docs/investigations/2026-10-07-card-1105-git-audit-promisor.md
