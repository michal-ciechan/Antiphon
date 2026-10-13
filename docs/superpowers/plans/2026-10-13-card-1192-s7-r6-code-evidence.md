# CARD-1192 S7 round-6 Code documentation repair evidence (1af8b8ab)

Repaired Review a835b016 F1 within S7: replaced resource-by-resource exception lists with the caller's exact stable finalizer statement and bound remaining preservation claims to named tests. Executable source is unchanged. The commissioned Final selection passed 39/39, zero failures/skips. W-1 live acceptance is NOT claimed.

Landing owner / original Code task: 1af8b8ab-e1f4-4fcb-b3ce-61177c79adb3, transferred by this brief from 4ede83bf-a652-450a-865b-e8063143be27.
Branch: feat/card-task-1af8b8ab.
Worktree: /work/worktrees/task-1af8b8ab.
Desktop checkout: C:\Antiphon\worktrees\card-task-1af8b8ab (unreachable here).
Task base: fec388ab3a819579ac064fcf74f909d8507359e0; fast-forward-only.
Repair slices pushed: 77f8a96e1669ef8ba90833d5f140a5a15e4bf404, 8110b0c38d0b951c08f8558404decf35d567cf9a, d3c8015d3ea575927da192c845c3791a4dc706dc.
Actual final tested SHA: d3c8015d3ea575927da192c845c3791a4dc706dc.
Plan: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md.
Evidence: docs/superpowers/plans/2026-10-13-card-1192-s7-r6-code-evidence.md.
Caller copy: /work/worktrees/task-1af8b8ab/.antiphon/task-1af8b8ab.md.
Restart: none. No land, deploy, live Fixture, old-run cleanup or deliberate Mutation.

## Stable finalizer scope

Fixture finalizer deletion is protected only where a named S7 test says so (work and mask volume acquisition ownership; unknown outcomes retained as residue). The inherited finalizer also deletes some objects by name or fixed roster (foreign cache volumes, same-name replacements behind legacy name-only markers, unacquired fixed-name consumers, and possibly others): the complete audit and fix are CARD-1208; Compose teardown is exit-status-only (CARD-1207); live Docker acceptance (W-1) is pending.

The statement appears verbatim once in docker-stack.md, the plan, all five r1–r5 S7 evidence notes, c590-remote.sh, c1192-workspace-contract.sh and RemoteScriptContractTests.cs. This note uses the same statement. CARD-1208 owns the complete deletion audit/fix, including further undiscovered classes; the note is not a closed exception census.

## Changed claims and keyword audit

- docker-stack.md: replaced the per-resource list; named C1192_Foreign_volume_is_refused_after_create for work/mask preservation.
- Plan: replaced its list and ownership-inventory wording; narrowed D-5's title, resource lifetime and V-2 language to workspace acquisition, modeled recipients and named methods. Removed per-cache exclusion tails in CP recipes; the stable statement applies. No checkpoint selector/table/filter, PC design, assertion or deadline changed.
- Five historical evidence notes: replaced lists with the exact statement; r1 replacement-controller and registered-helper claims now name C1192_Finalization_precedes_success_and_preserves_primary_failure. R4 foreign acquisition names C1192_Foreign_volume_is_refused_after_create. R5's prior audit is explicitly historical. Historical tested SHAs and CHECKPOINT lines remain unchanged.
- c590-remote.sh comments: replaced finalizer exception list; replaced the false never-substitute comment with the actual marker-name/ID branch behavior; named C1192_Finalization_precedes_success_and_preserves_primary_failure for replacement-controller retention.
- Workspace model comments: replaced list; named C1192_Harness_cleanup_stays_inside_its_private_directory for private-directory canaries and C1192_Foreign_volume_is_refused_after_create for work/mask acquisition. Retained the independent deny-real-Docker/source-dispatch policy.
- Test comments: replaced list and named methods for work/mask foreign-create and harness containment claims. Executable assertions and method names remain unchanged.

Searched every/all/always/never/cannot/guarantee/only/none/any/untouched/protected/confirmed/owned across the ten files, yielding 1,070 keyword-bearing lines. The ignored full audit is .antiphon/r6-universal-audit.txt. Reviewed context by category: S7 preservation/deletion claims were replaced or tied to named methods; source predicates and assertion labels are executable and retained; historical run/count/receipt statements preserve their source/time scope; PC expected-red assertions remain pending obligations; unrelated deployment/recycling/cold Seed/host-lane policies describe separate algorithms and retain their existing scope. No generic fixture ownership guarantee remains. The stable statement deliberately includes possibly others.

Manual checks passed: stable-statement copy census, historical CHECKPOINT line equality, comment-stripped executable comparison, shell syntax and git diff --check. No new test was added; no new guarded-defect red or deliberate mutant is claimed.

## Executable-line comparison against task base

Full-line shell comments and C# // comments were removed separately; remaining newline-joined bytes match base. C# raw shell strings remain included. Edited source diff hunks contain comments only.

```text
COMMENTS-IGNORED DIFF scripts/c590-remote.sh identical sha256=0a066ee9d52b359a25629bab0a2ff527c54fb87697dda155edc38a509e7de3ec
COMMENTS-IGNORED DIFF scripts/fixtures/c1192-workspace-contract.sh identical sha256=e1e664f57137d7a1f7ceb57a306e6cb969cf9c80ccbf25b84f92349fa5db3310
COMMENTS-IGNORED DIFF tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs identical sha256=1941b600a8a6b7b3d8430d3b9004ac70367bd1cfa6e2e813803bb42393bb2a7e
```

## Scope and run discipline

The specific repair brief's closed scope wins over the generic Final profile: CP-33..53, including CP-44's 19 adjacent methods. No whole Unit lane, full RemoteScriptContractTests class, full assembly or native W-1 run. No unbounded class selection; no Interim deferrals. Earlier S1–S6 matrices are not claimed rerun.

Unlisted build: fresh-mirror checkpoint-tool bootstrap through scripts/build-slot.ps1, OutputPath=bin-c1192-r6-tool/, UseAppHost=false, slot=granted waited=0s maxcpucount=6, 1 warning/0 errors. This is required to invoke the checkpoint tool; no other raw build/test driver ran.

Interrupted checkpoint run 20261013-010725-5519 at 8110b0c38d0b951c08f8558404decf35d567cf9a: stopped during its isolated build after finding a remaining preservation comment without the named method in the same sentence. Stop and terminal wait were awaited; terminal wait returned 6, executor dead without phase=done. No CP/test outcome is credited. The interrupted build held slot=granted waited=0s; no completed build receipt is claimed. Source was edited only after stop/wait returned. The small fix was committed/pushed before the replacement run. This explains the extra incomplete build attempt; the final selection has one completed isolated build.

No timeout, assertion, retry, Repeat or loaded-proof setting changed. No unchanged green proof was repeated. Task token stayed private and checkpoint owner binding stayed active. GET /api/runner-defaults returned revision 2; GET /api/session-runners succeeded. No Runner/Platform pin or fleet address was introduced. Delivery/landing/leases/persistence product behavior is unchanged; transcript/input-wait/parking invariants are inapplicable to this comment-only repair.

## Mutation and acceptance

Post-land Mutation was NOT run. PC-1..PC-174 and every variant are PENDING method-scoped SourceLanding Mutation. PC-1..152 retain the plan's individual variants; PC-153..159 have fourteen each; PC-160 four identity variants; PC-161..167 exit-zero/object-present; PC-168 exit-2 missing text; PC-169 wrong name; PC-170 wrong kind/socket; PC-171 mixed output; PC-172 premature marker; PC-173 four work/mask × persistent/single-probe variants; PC-174 two work/mask single-probe record-erasure variants. Mutation owns deliberate mutant/red/restore/green and missing-control discovery. No unbound candidate commissioned under docs/superpowers/mutations/.

W-1 remains pending with the rollout owner after Review/land. Ordinary green does not establish native Docker/UID/archive/NuGet-lock/copied acceptance. No decision or permission remains outstanding for this repair; next stage is ordinary Review, then caller lands this original Code task and commissions SourceLanding Mutation.

## Final ordinary verification

Checkpoint run 20261013-011025-1bce terminated exit 0 after every exit-75 continuation was awaited. Wall 806.157 seconds. One completed isolated Antiphon.Tests build to bin-c1192-s7/, UseAppHost=false, trailing forward slash OutputPath; 695 warnings/0 errors, slot=granted waited=90s maxcpucount=6. Six imported definitions were unused; no build ID named tests. The earlier interrupted build is disclosed above.

Independent fresh TRX audit verified exact class/method rosters, the literal CP-44 19-method source roster, Passed outcomes, timestamps within this run, 21 distinct TestRun IDs and 39 distinct execution IDs (35 distinct methods due to mandated adjacent overlap). Receipt validator passed all 21 rows at the actual tested SHA; each CHECKPOINT has dirty=0 sourceState=clean buildSource=verified.

| CP | executed/passed/failed/skipped | slot= | waited= |
|---|---|---|---|
| CP-33 | 1/1/0/0 | granted | 0s |
| CP-34 | 1/1/0/0 | granted | 15s |
| CP-35 | 1/1/0/0 | granted | 15s |
| CP-36 | 1/1/0/0 | granted | 30s |
| CP-37 | 1/1/0/0 | granted | 30s |
| CP-38 | 1/1/0/0 | granted | 90s |
| CP-39 | 1/1/0/0 | granted | 90s |
| CP-40 | 1/1/0/0 | granted | 30s |
| CP-41 | 1/1/0/0 | granted | 15s |
| CP-42 | 1/1/0/0 | granted | 15s |
| CP-43 | 1/1/0/0 | granted | 45s |
| CP-44 | 19/19/0/0 | granted | 45s |
| CP-45 | 1/1/0/0 | granted | 45s |
| CP-46 | 1/1/0/0 | granted | 14s |
| CP-47 | 1/1/0/0 | granted | 15s |
| CP-48 | 1/1/0/0 | granted | 30s |
| CP-49 | 1/1/0/0 | granted | 30s |
| CP-50 | 1/1/0/0 | granted | 15s |
| CP-51 | 1/1/0/0 | granted | 15s |
| CP-52 | 1/1/0/0 | granted | 15s |
| CP-53 | 1/1/0/0 | granted | 15s |

## Every V/R disposition under the specific repair brief

| ID | Actual outcome |
|---|---|
| V-1 | PASS CP-33 acquisition and CP-53 work/mask unknown registration/records; full S1 matrix excluded. |
| V-2 | PASS CP-35/45..53 modeled disposal/uncertainty/refusal; finalizer scope statement applies. |
| V-3 | NOT RERUN: full S2 matrix outside the specific brief. |
| V-4 | NOT RERUN: full S3 matrix outside the specific brief. |
| V-5 | PASS CP-33 actual seed filesystem recipients; full earlier backend matrix excluded. |
| V-6 | PASS CP-33 actual recovery validator; full earlier control matrix excluded. |
| V-7 | PASS CP-44 C1195 root/import/retention; full CP-27 excluded. |
| V-8 | PASS CP-33/36/41/42 payload/evidence/census/privacy. |
| V-9 | PASS CP-34/45..53 detected refusal, primary failure, EXIT, actual JSON/marker; finalizer scope statement applies. |
| R-1 | PASS CP-37/44 C1189 tools/unlink. |
| R-2 | PASS CP-35/44 C1191 ordering/failure/disposal. |
| R-3 | PASS CP-38/39/44 C1193 environment/log paths/bytes. |
| R-4 | PASS CP-44 C1194 budgets. |
| R-5 | PASS CP-44 C1195 mode/root/retention. |
| R-6 | NOT RERUN: C1066 argument-expanded matrices outside the specific brief. |
| R-7 | PASS CP-43 inventory/recipient and CP-33 seed; earlier host/saved/prune matrices excluded. |
| R-8 | PASS CP-40..44 lane/receipts/inventory/adjacent contracts. |
| W-1 | NOT RUN: pending rollout-owned live acceptance. |

Final for this commissioned selection; no required ordinary repair row is deferred. The specific brief excludes whole Unit/full class/S1–S6/live Fixture: none is claimed passed. PCs and W-1 remain pending, not deferred ordinary Final passes.

## Unedited CHECKPOINT lines

```text
CHECKPOINT CP-33 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-34/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-35/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-36/run.trx slot=granted waited=30s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-37/run.trx slot=granted waited=30s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-38/run.trx slot=granted waited=90s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-39/run.trx slot=granted waited=90s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-40/run.trx slot=granted waited=30s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-41/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-42/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-43/run.trx slot=granted waited=45s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/(C1189_*|C1191_*|C1193_*|C1194_*|C1195_*|C1192_Summary_*|C1192_Foreign_*|C1192_Harness_*|C1192_Helper_*)* executed=19 passed=19 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-44/run.trx slot=granted waited=45s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-45/run.trx slot=granted waited=45s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-46/run.trx slot=granted waited=14s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-47/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-48/run.trx slot=granted waited=30s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-49/run.trx slot=granted waited=30s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-50/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-51/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-52/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=d3c8015d3ea575927da192c845c3791a4dc706dc build=reused filter=/*/*/RemoteScriptContractTests/C1192_Acquisition_unknown_ownership_retains_registration_and_residue executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-1af8b8ab/.antiphon/checkpoints/20261013-011025-1bce/rows/CP-53/run.trx slot=granted waited=15s dirty=0 source=d3c8015d3ea575927da192c845c3791a4dc706dc sourceState=clean buildSource=verified
```

## Custody and publication

After terminal wait and validation, removed exactly 29 recorded producer-created bin-c1192-s7/ and bin-c1192-r6-tool/ directories, checking device/inode/birth identity, real-directory status and containment. Pre-run output inventory was empty. Checkpoint receipt directories were excluded. The terminal wait removed its own executor shadow. No pre-existing outputs, default bin/obj, old fixture residue or raw checkpoint/TRX/JSON/log payloads were removed or committed. Cleanup record: .antiphon/r6-cleanup.log; output inventory: .antiphon/r6-created-outputs.json.

This publication also replaces remaining historical per-cache exception tails in plan R-8 and r5 with references to the stable statement; r4/r5 references say finalizer scope statement. These last edits are Markdown only, completed after the run. Checkpoint table/filter roster and the three source files remain byte-identical to the tested slice. Receipts are bound to d3c8015d3ea575927da192c845c3791a4dc706dc, not relabeled to the report commit.

Fetched origin/master=e19186dc8e156216a2875c8c87237b94b7a857f8. At tested source, merge-tree exited 0 (tree cb1f436491a37d7711cf21c980ef92247e79b7fb). Evidence guard passed task base..8110b0c38 (2 commits, 0 entries/violations) and complete S7 base..8110b0c38 (25 commits, 0 entries/violations); final publication results and full SHA are appended to the caller copy after commit. No rebase/reset/amend/merge/force-push.

Rerun from a committed checkout: bootstrap tools/Antiphon.Checkpoints through scripts/build-slot.ps1 with OutputPath=bin-c1192-r6-tool/ and UseAppHost=false, then invoke its no-build run with --plan docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md --after S7 --expected-source-sha <full HEAD> --max-wait 45s. Continue wait --run <run-id> --max-wait 45s until exit is not 75. Validate report.json against that SHA and inspect the exact 21 fresh TRX rosters.

--- next stage ---
next: review
handoff: Review the pushed S7 documentation/comment repair and clean CP-33..53 evidence; executable lines are unchanged. CARD-1208 owns the complete finalizer deletion audit/fix; CARD-1207 and W-1/PC-1..174 remain pending. Landing owner is Code task 1af8b8ab-e1f4-4fcb-b3ce-61177c79adb3. Restart none.
artifact: docs/superpowers/plans/2026-10-13-card-1192-s7-r6-code-evidence.md
