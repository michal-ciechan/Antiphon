# CARD-1192 S7 round-5 Code documentation repair evidence (4ede83bf)

Historical round-5 repair of Review c0c7c3e0 F1: C1192_Foreign_volume_is_refused_after_create checks work/mask acquisition preservation; the finalizer scope statement below applies. Executable source is unchanged. Explicit Final repair scope passed 39/39, zero failures/skips. W-1 live acceptance is NOT claimed.

Landing owner / original Code task for this repair: 4ede83bf-a652-450a-865b-e8063143be27, transferred by the brief from 16459e79-5010-458e-adf4-0e03b65b98ee.
Branch: feat/card-task-4ede83bf.
Worktree: /work/worktrees/task-4ede83bf.
Desktop checkout: C:\Antiphon\worktrees\card-task-4ede83bf (not reachable here).
Task base: 58fdd916315e16f357ed39d4278320e0401eb9e3; fast-forward-only.
Repair commit and actual tested SHA: 8567e3be0aeccb9b25b02387044dbdf512aeb951, pushed.
This evidence publication advances HEAD with Markdown only. The final full SHA is reported in the caller copy and completion; receipts remain bound to the actual tested SHA.
Plan: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md.
Evidence: docs/superpowers/plans/2026-10-13-card-1192-s7-r5-code-evidence.md.
Full caller copy: /work/worktrees/task-4ede83bf/.antiphon/task-4ede83bf.md.
Restart: none. No land, deployment, live Fixture, old-run cleanup or deliberate Mutation.

## Changed claims and manual audit

- docs/docker-stack.md: C1192_Foreign_volume_is_refused_after_create (CP-44) checks the work/mask refusal/drop/no-mount/no-delete outcome. Finalization describes label-checked work/mask and registered caches subject to CARD-1208.
- Plan: D-5, resource lifetime, V-1/V-2, R-8, CP-2/43 recipes and G-9..11/PC-9..10 are scoped to work/mask ownership protection or allowed names. Round 5 originally qualified the no-untested-guard claim; the stable finalizer scope statement now replaces its exception list. The checkpoint table, test filters, assertions and timeouts are unchanged.
- Four historical S7 evidence notes (s7-code, s7-repair-code, s7-doc-repair, s7-r4-code): known-exception lists include CARD-1208; owned-volume/foreign-preservation descriptions now name work/mask/helpers where applicable. Cache acquisition audit explicitly discloses retained registration and foreign deletion. Historical CHECKPOINT lines are byte-identical to base.
- c590-remote.sh comments: work/mask registration and foreign-acquisition claims name the resource class; finalizer comments include the exact exception.
- c1192-workspace-contract.sh comments: foreign-acquisition checks cover work/mask, and named limits are stated. Unknown-probe comment names work/mask.
- RemoteScriptContractTests comments: C1192_Foreign_volume_is_refused_after_create checks work/mask acquisition; the finalizer scope statement applies.

Historical round-5 near-context search for every/all/always/never/cannot/guarantee/any/none/confirmed/positively/untouched around ownership/foreign/cleanup/acquisition produced 79 matches. Read the surrounding S7 claims, named-controller/helper contracts and historical diagnostic context. Unrelated rollout/recycling/host-lane policies retain their own scope. Search records remain ignored at .antiphon/s7-r5-final-wording-audit.txt. Bash syntax and git diff --check pass. No executable lines, test assertions or deadlines changed; no new test or guarded-defect red is claimed for this documentation-only repair.

Fixture finalizer deletion is protected only where a named S7 test says so (work and mask volume acquisition ownership; unknown outcomes retained as residue). The inherited finalizer also deletes some objects by name or fixed roster (foreign cache volumes, same-name replacements behind legacy name-only markers, unacquired fixed-name consumers, and possibly others): the complete audit and fix are CARD-1208; Compose teardown is exit-status-only (CARD-1207); live Docker acceptance (W-1) is pending.

## Executable-line proof against task base

Full-line shell comments and C# comments were removed independently from base and working files; newline-joined remaining bytes match. The edited source hunks contain comments only.

```text
COMMENTS-IGNORED DIFF scripts/c590-remote.sh identical sha256=0a066ee9d52b359a25629bab0a2ff527c54fb87697dda155edc38a509e7de3ec
COMMENTS-IGNORED DIFF scripts/fixtures/c1192-workspace-contract.sh identical sha256=e1e664f57137d7a1f7ceb57a306e6cb969cf9c80ccbf25b84f92349fa5db3310
COMMENTS-IGNORED DIFF tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs identical sha256=1941b600a8a6b7b3d8430d3b9004ac70367bd1cfa6e2e813803bb42393bb2a7e
```

## Final ordinary verification

Checkpoint run 20261013-003428-ebd9 terminated exit 0; every exit-75 continuation was awaited. State start/end wall 376.662 seconds. One actual isolated bin-c1192-s7/ build, UseAppHost=false, forward-slash trailing OutputPath, 123.617 seconds, 694 warnings/0 errors, slot=granted waited=0s maxcpucount=6. Six imported build definitions were unused; no build ID named tests. Rows all held granted slots. No retry, repetition, loaded proof, broad Unit, whole class or full assembly run.

CHECKPOINT SOURCE VALID source=8567e3be0aeccb9b25b02387044dbdf512aeb951 rows=21. Every receipt is dirty=0 sourceState=clean buildSource=verified. Independent fresh TRX audit verified exact class/method filters, CP-44's literal 19-method source roster, Passed outcomes, timestamps within this run, 21 distinct TestRun IDs and 39 distinct execution IDs. There are 35 distinct methods due to mandated adjacent overlap, not 39 distinct methods.

| CP | executed/passed/failed/skipped | slot= | waited= |
|---|---|---|---|
| CP-33 | 1/1/0/0 | granted | 0s |
| CP-34 | 1/1/0/0 | granted | 0s |
| CP-35 | 1/1/0/0 | granted | 0s |
| CP-36 | 1/1/0/0 | granted | 0s |
| CP-37 | 1/1/0/0 | granted | 0s |
| CP-38 | 1/1/0/0 | granted | 0s |
| CP-39 | 1/1/0/0 | granted | 0s |
| CP-40 | 1/1/0/0 | granted | 0s |
| CP-41 | 1/1/0/0 | granted | 0s |
| CP-42 | 1/1/0/0 | granted | 0s |
| CP-43 | 1/1/0/0 | granted | 0s |
| CP-44 | 19/19/0/0 | granted | 15s |
| CP-45 | 1/1/0/0 | granted | 30s |
| CP-46 | 1/1/0/0 | granted | 15s |
| CP-47 | 1/1/0/0 | granted | 15s |
| CP-48 | 1/1/0/0 | granted | 15s |
| CP-49 | 1/1/0/0 | granted | 15s |
| CP-50 | 1/1/0/0 | granted | 15s |
| CP-51 | 1/1/0/0 | granted | 15s |
| CP-52 | 1/1/0/0 | granted | 15s |
| CP-53 | 1/1/0/0 | granted | 15s |

## Every V/R disposition under the explicit repair brief

| ID | Actual outcome |
|---|---|
| V-1 | PASS CP-33 acquisition and CP-53 work/mask unknown registration/records; full S1 excluded. |
| V-2 | PASS CP-35/45..53 disposal/uncertainty/refusal; the finalizer scope statement applies. |
| V-3 | NOT RERUN: full S2 matrix outside explicit brief. |
| V-4 | NOT RERUN: full S3 matrix outside explicit brief. |
| V-5 | PASS CP-33 actual seed filesystem recipients; full backend matrix excluded. |
| V-6 | PASS CP-33 actual recovery validator; earlier full control matrix excluded. |
| V-7 | PASS CP-44 C1195 root/import/retention; full CP-27 excluded. |
| V-8 | PASS CP-33/36/41/42 payload/receipts/census/privacy. |
| V-9 | PASS CP-34/45..53 detected refusal, primary failure, EXIT, actual JSON/marker; finalizer scope statement above. |
| R-1 | PASS CP-37/44 C1189 tools/unlink. |
| R-2 | PASS CP-35/44 C1191 ordering/failure/disposal. |
| R-3 | PASS CP-38/39/44 C1193 environment/log paths/bytes. |
| R-4 | PASS CP-44 C1194 budgets. |
| R-5 | PASS CP-44 C1195 mode/root/retention. |
| R-6 | NOT RERUN: C1066 argument-expanded matrices outside explicit brief. |
| R-7 | PASS CP-43 inventory/recipient and CP-33 seed; earlier host/saved/prune matrices excluded. |
| R-8 | PASS CP-40..44 lane/receipts/inventory/adjacent contracts. |
| W-1 | NOT RUN: pending rollout-owned live acceptance. |

Final for the commissioned repair selection CP-33..53; no ordinary row required by this brief is deferred to Final. Whole Unit/full RemoteScriptContractTests/S1-S6/live Fixture were expressly excluded, never claimed passed. No unbounded class selection or full-assembly driver. Manual work completed: claim/exception audit, comment-stripped diff, preserved historical records, syntax/whitespace, fresh TRX/source validation, output custody and merge/evidence-history checks.

## Mutation, platform and custody

Post-land Mutation was NOT run. PC-1..PC-174 and every variant remain PENDING method-scoped SourceLanding Mutation; PC-1..152 variants remain as individually enumerated in the plan. PC-153..159: fourteen each; PC-160: four identity variants; PC-161..167: exit-zero removal/object-present; PC-168: exit-2 missing text; PC-169: wrong name; PC-170: wrong kind/socket; PC-171: mixed output; PC-172: premature marker; PC-173: four work/mask x persistent/single-probe variants; PC-174: two work/mask single-probe record-erasure variants. Mutation owns deliberate mutant/red/restore/green and missing-control discovery. No unbound candidate commissioned under docs/superpowers/mutations/.

GET /api/runner-defaults returned revision 2; GET /api/session-runners succeeded. No Runner or Platform pin or embedded fleet location.
Only unlisted build: checkpoint-tool bootstrap required in the fresh mirror, gated by scripts/build-slot.ps1, bin-c1192-r5-tool/, UseAppHost=false, slot=granted waited=0s maxcpucount=6; 1 warning/0 errors. No other test driver. Source remained committed and frozen throughout.

After terminal exit and validation, removed exactly 28 task-created bin-c1192-s7/ and bin-c1192-r5-tool/ directories using the recorded inode/birth identity, containment and real-directory checks. No pre-existing outputs/default bin/obj removed. Executor shadow tool was removed by terminal wait. Raw TRX/JSON/logs remain ignored at .antiphon/checkpoints/20261013-003428-ebd9; no generated payload relocated or committed.

Fetched origin/master=e19186dc8e156216a2875c8c87237b94b7a857f8. At tested source, git merge-tree --write-tree HEAD origin/master exited 0, tree=bdf1bace7c1b93e3ebca57cd3d97fd3617f3cb7d. Evidence guard passed task base..tested SHA (1 commit, 0 entries/violations) and complete S7 candidate base 2b5aa6aeeaa3f26062b59609019e92d43575105d..tested SHA (22 commits, 0 entries/violations). Final publication checks are appended to the caller copy after this report commit; no rebase/amend/reset/merge.

## Rerun commands

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1192-r5-review-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1192-r5-review-tool/ --property:UseAppHost=false --nologo
$sha = git rev-parse HEAD
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1192-r5-review-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md --after S7 --expected-source-sha $sha --max-wait 45s
# Continue wait --run <printed-run-id> --max-wait 45s after exit 75 until terminal.
```

## Unedited CHECKPOINT lines

```text
CHECKPOINT CP-33 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/(C1189_*|C1191_*|C1193_*|C1194_*|C1195_*|C1192_Summary_*|C1192_Foreign_*|C1192_Harness_*|C1192_Helper_*)* executed=19 passed=19 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-44/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-45/run.trx slot=granted waited=30s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-46/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-47/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-48/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-49/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-50/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-51/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-52/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=8567e3be0aeccb9b25b02387044dbdf512aeb951 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Acquisition_unknown_ownership_retains_registration_and_residue executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ede83bf/.antiphon/checkpoints/20261013-003428-ebd9/rows/CP-53/run.trx slot=granted waited=15s dirty=0 source=8567e3be0aeccb9b25b02387044dbdf512aeb951 sourceState=clean buildSource=verified
```
