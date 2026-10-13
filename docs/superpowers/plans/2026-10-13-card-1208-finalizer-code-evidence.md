# CARD-1208 Code evidence — Final ordinary verification

Implemented the deletion audit; every required checkpoint now has a passing ordinary result. Review is next. SourceLanding Mutation and live Docker W-1 remain pending.

Original Code / landing owner: b1b731e0-1d07-4589-8c85-94b420b101bd. Branch: feat/card-task-b1b731e0. Worktree: /work/worktrees/task-b1b731e0. Task base: 9f89cc427dca7030cfc93a45ec36e4375d894213. Implementation tip: 4c675e64588c5e3f39a875c203cc34447bc98b39. Restart: none.

Plan: docs/superpowers/plans/2026-10-13-card-1208-finalizer-deletion-audit.md. Stored caller copy: .antiphon/task-b1b731e0.md. This evidence document and final plan clarification are documentation-only publication changes after the tests; receipts retain their actual tested SHAs.

## Result and scope

Named fixture acquisitions record Docker cidfiles at creation. Consumer/helper/network teardown uses acquired IDs or migrates legacy records after checking run labels on the resolved ID. Registered cache volumes require full current-run identity; positively foreign volumes are dropped immediately and recorded as "foreign: left untouched". Unacquired roster objects remain untouched. Unknown/invalid records produce named residue and refuse success, including standalone symlink/directory cidfiles. Escape links use recorded inode identity; diagnostic directories are retained rather than recursively removed. Controller identity/containment guards remain.

Changed: scripts/c590-remote.sh; scripts/fixtures/c1192-workspace-contract.sh; scripts/fixtures/c913-seed-contract.sh (seed/inventory acquisition models); tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs; docs/docker-stack.md; the CARD-1192 stable S7 plan statement; the CARD-1208 plan/evidence. Existing HOME/scratch/path assertions were retained and bound to the acquired launch wrapper. No timeout, retry, Repeat setting or safety assertion was loosened.

The specific brief overrides generic Final defaults: no Unit lane, no whole RemoteScriptContractTests class, no full assembly. Closed ordinary scope CP-33..61: 29 rows, 47 executions in the final cumulative selection, 0 failed/skipped. CP-44 remains exactly 19 adjacent methods. Final SourceLanding controls and W-1 are excluded pending obligations, not claimed passes. Scope is bounded to the named script-contract methods; no unbounded classes/full-assembly cost applies.

## Red proof and repairs

At 89d65491eb80da81af0321a1ec9ff72c374337fd, CP-54..57 each executed 1, failed 1 on ShouldAssertException audit=0. Production scripts/c590-remote.sh was byte-identical to landed task base 9f89cc427: no production edit occurred before this proof. Actual output included volume c849c1192fixture0001-packages, container c849-c1192fixture0001-donor, network c849-c1192fixture0001-npm, and container c849-c1192fixture0001-restore-p1 plus masked. These are guarded deletion defects, not build/fixture failures. The first attempt at 741f8548951417e41f9706e5427e619be4f534c9 failed on duplicate audit fact keys; it is disclosed and receives no defect-proof credit. One earlier launcher invocation refused an incorrect expected SHA before launching any build/test; it was corrected to git HEAD.

First full run at e15865e025850492c2dfb7ac97f5678b78a88646: all 29 rows executed; CP-33/43 failed on outdated seed/inventory models, CP-44 on a command selector tied to docker run instead of the acquired wrapper. All eight new audit methods passed. Targeted repairs kept production checks intact: cidfile support, acquisition registration, prior donor release, verified recovery readmission, and exact wrapper selection. CP-44 then passed all 19. Container/network owner/run/role are now varied independently; empty/malformed/symlink cidfiles are also checked.

The final standalone-record refinement was tested first at 3f40cc432ae2a6581709817190fc3256ebbd5f45 by a direct build-slot-bound fixture assertion: standalone-symlink=0, standalone-directory=0, audit=0, finalization exit=0; the asserting driver exited 1. This is ordinary new input discovery, not deliberate production mutation. Production then stopped skipping malformed admitted records. CP-61's final TRX exercises that same input matrix green. Seed/recovery direct assertions passed before the final repair run.

All failures caused by this implementation/test adaptation were repaired; none is attributed to inherited flakiness. No unchanged green selection was repeated voluntarily. Full CP-44 was rerun because its row was red; after each repair only affected rows ran.

## Final per-checkpoint outcomes and source provenance

Fresh TRX audit independently checked exact class/method rosters, source's 19-method adjacent roster, Passed outcomes, timestamps inside each producing run, distinct TestRun/execution IDs and nonzero counts. Counts below are TUnit results; internal matrix facts are not inflated into executions. Each credited row has dirty=0, sourceState=clean, buildSource=verified, and the committed SHA passed as expected-source-sha. Receipt validation was run separately for each actual SHA and its credited rows.

| CP | executed/passed/failed/skipped | actual tested SHA | slot= | waited= |
|---|---|---|---|---|
| CP-33 | 1/1/0/0 | 4c675e64588c5e3f39a875c203cc34447bc98b39 | granted | 15s |
| CP-34 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 45s |
| CP-35 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 30s |
| CP-36 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 15s |
| CP-37 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-38 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-39 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-40 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-41 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-42 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-43 | 1/1/0/0 | 1325d1378488ae375a58aa8144ddc3bbd3200e81 | granted | 0s |
| CP-44 | 19/19/0/0 | 51ad725dc253427f0beff2d7a7beb76bf1bc160b | granted | 0s |
| CP-45 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-46 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-47 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 15s |
| CP-48 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 45s |
| CP-49 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 15s |
| CP-50 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 15s |
| CP-51 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 15s |
| CP-52 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-53 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-54 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-55 | 1/1/0/0 | 51ad725dc253427f0beff2d7a7beb76bf1bc160b | granted | 0s |
| CP-56 | 1/1/0/0 | 51ad725dc253427f0beff2d7a7beb76bf1bc160b | granted | 0s |
| CP-57 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-58 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-59 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-60 | 1/1/0/0 | e15865e025850492c2dfb7ac97f5678b78a88646 | granted | 0s |
| CP-61 | 1/1/0/0 | 4c675e64588c5e3f39a875c203cc34447bc98b39 | granted | 45s |

## Every required invariant

| ID | Actual outcome |
|---|---|
| V-1 | PASS CP-54, 1 execution, 0 failed/skipped. |
| V-2 | PASS CP-55, 1 execution, 0 failed/skipped. |
| V-3 | PASS CP-56, 1 execution, 0 failed/skipped. |
| V-4 | PASS CP-57, 1 execution, 0 failed/skipped. |
| V-5 | PASS CP-58, 1 execution, 0 failed/skipped. |
| V-6 | PASS CP-59, 1 execution, 0 failed/skipped. |
| V-7 | PASS CP-60, 1 execution, 0 failed/skipped. |
| V-8 | PASS CP-61, 1 execution, 0 failed/skipped. |
| R-1 | PASS CP-33, 1 execution(s), 0 failed/skipped. |
| R-2 | PASS CP-34, 1 execution(s), 0 failed/skipped. |
| R-3 | PASS CP-35, 1 execution(s), 0 failed/skipped. |
| R-4 | PASS CP-36, 1 execution(s), 0 failed/skipped. |
| R-5 | PASS CP-37, 1 execution(s), 0 failed/skipped. |
| R-6 | PASS CP-38, 1 execution(s), 0 failed/skipped. |
| R-7 | PASS CP-39, 1 execution(s), 0 failed/skipped. |
| R-8 | PASS CP-40, 1 execution(s), 0 failed/skipped. |
| R-9 | PASS CP-41, 1 execution(s), 0 failed/skipped. |
| R-10 | PASS CP-42, 1 execution(s), 0 failed/skipped. |
| R-11 | PASS CP-43, 1 execution(s), 0 failed/skipped. |
| R-12 | PASS CP-44, 19 execution(s), 0 failed/skipped. |
| R-13 | PASS CP-45, 1 execution(s), 0 failed/skipped. |
| R-14 | PASS CP-46, 1 execution(s), 0 failed/skipped. |
| R-15 | PASS CP-47, 1 execution(s), 0 failed/skipped. |
| R-16 | PASS CP-48, 1 execution(s), 0 failed/skipped. |
| R-17 | PASS CP-49, 1 execution(s), 0 failed/skipped. |
| R-18 | PASS CP-50, 1 execution(s), 0 failed/skipped. |
| R-19 | PASS CP-51, 1 execution(s), 0 failed/skipped. |
| R-20 | PASS CP-52, 1 execution(s), 0 failed/skipped. |
| R-21 | PASS CP-53, 1 execution(s), 0 failed/skipped. |
| W-1 | NOT RUN; pending rollout-owned live Docker acceptance. |

All required ordinary IDs are complete. None is deferred to another ordinary Final. The CARD-1192 earlier S1–S6 adjacent obligation is CP-44's exact 19-method selection; no broader S1–S6 matrix is claimed. Compose down remains exit-status-only and is CARD-1207's separate scope.

## Build/run accounting

Every committed run used one isolated tests/Antiphon.Tests build (id bin-c1208), forward-slash OutputPath=bin-c1208/, UseAppHost=false on Linux, and the host build-slot gate. No build ID was named tests. Every driver was awaited to a terminal non-75 exit; there are no orphaned runs. Build provenance and per-driver waits are preserved below.

Run 20261013-015626-07b6: commit=741f8548951417e41f9706e5427e619be4f534c9, exit=1, wall=562.1427855s, rows=4. Build bin-c1208: state=ok, slot=granted, waited=0s, source=741f8548951417e41f9706e5427e619be4f534c9, sourceState=clean, buildSource=verified. 

Run 20261013-020639-e056: commit=89d65491eb80da81af0321a1ec9ff72c374337fd, exit=1, wall=314.6423701s, rows=4. Build bin-c1208: state=ok, slot=granted, waited=30s, source=89d65491eb80da81af0321a1ec9ff72c374337fd, sourceState=clean, buildSource=verified. 

Run 20261013-021354-6c1b: commit=e15865e025850492c2dfb7ac97f5678b78a88646, exit=1, wall=467.8454528s, rows=29. Build bin-c1208: state=ok, slot=granted, waited=0s, source=e15865e025850492c2dfb7ac97f5678b78a88646, sourceState=clean, buildSource=verified. 

Run 20261013-022423-c7e1: commit=51ad725dc253427f0beff2d7a7beb76bf1bc160b, exit=1, wall=183.8968941s, rows=6. Build bin-c1208: state=ok, slot=granted, waited=0s, source=51ad725dc253427f0beff2d7a7beb76bf1bc160b, sourceState=clean, buildSource=verified. 

Run 20261013-023102-9d24: commit=1325d1378488ae375a58aa8144ddc3bbd3200e81, exit=1, wall=122.3834111s, rows=2. Build bin-c1208: state=ok, slot=granted, waited=0s, source=1325d1378488ae375a58aa8144ddc3bbd3200e81, sourceState=clean, buildSource=verified. 

Run 20261013-023955-ed7c: commit=4c675e64588c5e3f39a875c203cc34447bc98b39, exit=0, wall=323.3347658s, rows=2. Build bin-c1208: state=ok, slot=granted, waited=0s, source=4c675e64588c5e3f39a875c203cc34447bc98b39, sourceState=clean, buildSource=verified. 

Explained extra drivers: checkpoint-tool bootstrap through scripts/build-slot.ps1, bin-c1208-tool/, UseAppHost=false; passed (1 warning, 0 errors), slot=granted waited=0s, 23s. This bootstrapped the tool and is not checkpoint source qualification. Scratch-copy model diagnostic through build-slot: eight audit inputs passed, slot=granted waited=150s, held=5s; no ordinary TUnit result credited. Committed direct fixture proof through build-slot: seed and recovery passed, new standalone-record assertion red as above, slot=granted waited=0s, held=23s. Bash/Node syntax checks and git/receipt/TRX inspections are read-only checks, not extra test builds. No raw dotnet build/test driver ran outside the gate.

GET /api/runner-defaults returned revision 2 and GET /api/session-runners succeeded. No Runner/Platform pin or fleet address was embedded. No stack restart/deploy or live Docker acceptance was attempted. Native Docker/UID/archive/NuGet-lock/copied behavior remains W-1's pending qualification; stub green does not discharge it.

## Pending Mutation

CARD-1208 PC-1..PC-8 and every listed variant remain PENDING method-scoped SourceLanding Mutation; the plan binds exact detecting methods. Cache-role variants, helper/consumer acquisition variants, independently wrong container/network labels, legitimate release variants, ID/name substitution, legacy migration and unknown/invalid-record variants are all pending. Existing CARD-1192 PC-1..174 and all variants remain pending. No post-land Mutation ran, no deliberate source mutant/red/restore/green cycle was commissioned, and no unbound candidate under docs/superpowers/mutations/ was created. Mutation owns missing-control discovery.

## Unedited CHECKPOINT lines (all attempts)


Run 20261013-015626-07b6:

```text
CHECKPOINT CP-55 commit=741f8548951417e41f9706e5427e619be4f534c9 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_container_replacement_is_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-015626-07b6/rows/CP-55/run.trx slot=granted waited=210s dirty=0 source=741f8548951417e41f9706e5427e619be4f534c9 sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=741f8548951417e41f9706e5427e619be4f534c9 build=ok filter=/*/*/RemoteScriptContractTests/C1208_Foreign_cache_is_dropped_and_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-015626-07b6/rows/CP-54/run.trx slot=granted waited=210s dirty=0 source=741f8548951417e41f9706e5427e619be4f534c9 sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=741f8548951417e41f9706e5427e619be4f534c9 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_network_replacement_is_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-015626-07b6/rows/CP-56/run.trx slot=granted waited=45s dirty=0 source=741f8548951417e41f9706e5427e619be4f534c9 sourceState=clean buildSource=verified
CHECKPOINT CP-57 commit=741f8548951417e41f9706e5427e619be4f534c9 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Unacquired_roster_is_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-015626-07b6/rows/CP-57/run.trx slot=granted waited=60s dirty=0 source=741f8548951417e41f9706e5427e619be4f534c9 sourceState=clean buildSource=verified
```

Run 20261013-020639-e056:

```text
CHECKPOINT CP-54 commit=89d65491eb80da81af0321a1ec9ff72c374337fd build=ok filter=/*/*/RemoteScriptContractTests/C1208_Foreign_cache_is_dropped_and_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-020639-e056/rows/CP-54/run.trx slot=granted waited=75s dirty=0 source=89d65491eb80da81af0321a1ec9ff72c374337fd sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=89d65491eb80da81af0321a1ec9ff72c374337fd build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_container_replacement_is_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-020639-e056/rows/CP-55/run.trx slot=granted waited=90s dirty=0 source=89d65491eb80da81af0321a1ec9ff72c374337fd sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=89d65491eb80da81af0321a1ec9ff72c374337fd build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_network_replacement_is_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-020639-e056/rows/CP-56/run.trx slot=granted waited=30s dirty=0 source=89d65491eb80da81af0321a1ec9ff72c374337fd sourceState=clean buildSource=verified
CHECKPOINT CP-57 commit=89d65491eb80da81af0321a1ec9ff72c374337fd build=reused filter=/*/*/RemoteScriptContractTests/C1208_Unacquired_roster_is_untouched* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-020639-e056/rows/CP-57/run.trx slot=granted waited=30s dirty=0 source=89d65491eb80da81af0321a1ec9ff72c374337fd sourceState=clean buildSource=verified
```

Run 20261013-021354-6c1b:

```text
CHECKPOINT CP-33 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-33/run.trx slot=granted waited=30s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-34/run.trx slot=granted waited=45s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-35/run.trx slot=granted waited=30s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-36/run.trx slot=granted waited=15s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/(C1189_*|C1191_*|C1193_*|C1194_*|C1195_*|C1192_Summary_*|C1192_Foreign_*|C1192_Harness_*|C1192_Helper_*)* executed=19 passed=18 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-46/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-47/run.trx slot=granted waited=15s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-48/run.trx slot=granted waited=45s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-49/run.trx slot=granted waited=15s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-50/run.trx slot=granted waited=15s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-52/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Acquisition_unknown_ownership_retains_registration_and_residue executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-53/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-51/run.trx slot=granted waited=15s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Foreign_cache_is_dropped_and_untouched* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-54/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_container_replacement_is_untouched* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-55/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_network_replacement_is_untouched* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-56/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-57 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Unacquired_roster_is_untouched* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-57/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-59 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Recorded_container_id_does_not_delete_replacement* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-59/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-58 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Acquired_objects_are_removed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-58/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-61 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Unknown_identity_retains_residue* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-61/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
CHECKPOINT CP-60 commit=e15865e025850492c2dfb7ac97f5678b78a88646 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_owned_markers_are_migrated* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-021354-6c1b/rows/CP-60/run.trx slot=granted waited=0s dirty=0 source=e15865e025850492c2dfb7ac97f5678b78a88646 sourceState=clean buildSource=verified
```

Run 20261013-022423-c7e1:

```text
CHECKPOINT CP-43 commit=51ad725dc253427f0beff2d7a7beb76bf1bc160b build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-022423-c7e1/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=51ad725dc253427f0beff2d7a7beb76bf1bc160b sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=51ad725dc253427f0beff2d7a7beb76bf1bc160b build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-022423-c7e1/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=51ad725dc253427f0beff2d7a7beb76bf1bc160b sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=51ad725dc253427f0beff2d7a7beb76bf1bc160b build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_container_replacement_is_untouched* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-022423-c7e1/rows/CP-55/run.trx slot=granted waited=0s dirty=0 source=51ad725dc253427f0beff2d7a7beb76bf1bc160b sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=51ad725dc253427f0beff2d7a7beb76bf1bc160b build=reused filter=/*/*/RemoteScriptContractTests/C1208_Legacy_network_replacement_is_untouched* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-022423-c7e1/rows/CP-56/run.trx slot=granted waited=0s dirty=0 source=51ad725dc253427f0beff2d7a7beb76bf1bc160b sourceState=clean buildSource=verified
CHECKPOINT CP-61 commit=51ad725dc253427f0beff2d7a7beb76bf1bc160b build=reused filter=/*/*/RemoteScriptContractTests/C1208_Unknown_identity_retains_residue* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-022423-c7e1/rows/CP-61/run.trx slot=granted waited=0s dirty=0 source=51ad725dc253427f0beff2d7a7beb76bf1bc160b sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=51ad725dc253427f0beff2d7a7beb76bf1bc160b build=reused filter=/*/*/RemoteScriptContractTests/(C1189_*|C1191_*|C1193_*|C1194_*|C1195_*|C1192_Summary_*|C1192_Foreign_*|C1192_Harness_*|C1192_Helper_*)* executed=19 passed=19 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-022423-c7e1/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=51ad725dc253427f0beff2d7a7beb76bf1bc160b sourceState=clean buildSource=verified
```

Run 20261013-023102-9d24:

```text
CHECKPOINT CP-43 commit=1325d1378488ae375a58aa8144ddc3bbd3200e81 build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-023102-9d24/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=1325d1378488ae375a58aa8144ddc3bbd3200e81 sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=1325d1378488ae375a58aa8144ddc3bbd3200e81 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-023102-9d24/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=1325d1378488ae375a58aa8144ddc3bbd3200e81 sourceState=clean buildSource=verified
```

Run 20261013-023955-ed7c:

```text
CHECKPOINT CP-61 commit=4c675e64588c5e3f39a875c203cc34447bc98b39 build=reused filter=/*/*/RemoteScriptContractTests/C1208_Unknown_identity_retains_residue* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-023955-ed7c/rows/CP-61/run.trx slot=granted waited=45s dirty=0 source=4c675e64588c5e3f39a875c203cc34447bc98b39 sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=4c675e64588c5e3f39a875c203cc34447bc98b39 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b1b731e0/.antiphon/checkpoints/20261013-023955-ed7c/rows/CP-33/run.trx slot=granted waited=15s dirty=0 source=4c675e64588c5e3f39a875c203cc34447bc98b39 sourceState=clean buildSource=verified
```

## Publication and cleanup

All meaningful slices were committed and pushed to the authorized task branch; no amend, rebase, reset or force-push. Full task-range evidence guard and fetched-master merge-tree checks are recorded in the caller copy after publication. Generated TRX/JSON/logs/checkpoint payloads remain ignored. Final green tool cleanup owns bin-c1208/; cleanup removes only this task's recorded bin-c1208-tool/ bootstrap and scratch artifacts. No default bin/obj, other output names or runtime reports are removed.

Rerun from committed HEAD: bootstrap tools/Antiphon.Checkpoints through scripts/build-slot.ps1 with UseAppHost=false and OutputPath=bin-c1208-tool/, then dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1208-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-13-card-1208-finalizer-deletion-audit.md --expected-source-sha <full HEAD> --max-wait 45s. Continue wait --run <id> --max-wait 45s until non-75. Review then caller lands this original Code task and commissions SourceLanding Mutation.

Fresh audit: 29 rows / 47 execution IDs / 43 distinct methods / 29 fresh TestRun IDs / CP-44 source roster 19. All four selected-source receipt validations exited 0 (23 + 3 + 1 + 2 rows). Final green checkpoint tool deleted bin-c1208/ outputs; the task-owned bootstrap bin-c1208-tool/ was removed after terminal waits and validation.

--- next stage ---
next: review
handoff: Review CARD-1208 deletion ownership audit and cumulative clean ordinary CP-33..61 receipts (47 executions; CP-44=19). Land original Code task b1b731e0-1d07-4589-8c85-94b420b101bd only after Review; commission SourceLanding Mutation. PC-1..8 plus CARD-1192 PCs and W-1 remain pending; CARD-1207 excluded. Restart none.
artifact: docs/superpowers/plans/2026-10-13-card-1208-finalizer-code-evidence.md
