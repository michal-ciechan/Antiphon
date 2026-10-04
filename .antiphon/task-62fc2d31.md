# CARD-0849 Code evidence — task 62fc2d31

Status: BLOCKED / next Code. Caller refinement received 2026-10-04 narrows the remaining work to C944/C957 and the plan checkpoint rows, explicitly forbids another whole-Unit run, and sets a 45-minute wrap-up budget. C944/C957 is fixed and previously qualified 4/4. The server still marks original Code task 62fc2d31 Failed after its four-hour ceiling; a fresh focused checkpoint attempt at committed HEAD 196d4c6e05189e42c2a1a75fa8f9c77d5980e1be was refused with exit7 owner-ended before any row started. Latest test-only amendment 625b0df66ed080f043b68ecfd2888d0f0edf715b remains UNVERIFIED by compile/TUnit. Native Windows CP-7/8 also remain unrun. Active checkpoint ownership and the Windows lane are required before Review. No whole-Unit or full-class rerun is requested by the amended scope.

Last production-changing SHA: a059af5ecd40d7a8b500806fd775a2a8f868aee0. Last tested code SHA: a059af5ecd40d7a8b500806fd775a2a8f868aee0. Later test/report/design commits are not being represented as tested by earlier receipts. The final pushed tip is in the caller report/progress marker.

Original Code task/landing owner: `62fc2d31-6179-40c8-85b0-d216cbe83744`.
Branch: `feat/card-task-62fc2d31`. Worktree: `/work/worktrees/task-62fc2d31`.
Task base: `7f489f066c69caa2ee1380361e8d0135dd451db9`.
Parent plan: `docs/superpowers/plans/2026-10-04-card-0849-net9-packs-seed-contract-plan.md`.
Verification: `docs/superpowers/plans/2026-10-04-card-0849-net9-packs-seed-contract-test-design.md`.

## Setup

Read runner-defaults (revision 2, unresolved references empty) and session-runners.
Linux and Windows eligible, temp unavailable. This delegate has no Windows checkout access; CP-7/8 requested as caller-commissioned Windows work. Local nested Docker is reachable (Linux, 27.5.1). SDK 10.0.401.
Installed task-local jq 1.7.1 in ignored `.antiphon/c913-tools/`, validating Dockerfile-pinned SHA-256 5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5.

Declared checkpoint-tool bootstrap at task base: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c913-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c913-tool/ --nologo`. Exit 0, 0 errors, 1 inherited nullable warning; elapsed 11.43s; slot=granted waited=0s, lease c0eb03ee-b287-4963-835f-54c2e8442d28, released after 12s.

## First red-first attempt (invalid proof; setup repairs required)

Actual tested SHA `514878838d279f7b0cdc2830bc04f23cc8bc559b`; source clean, build provenance verified. CP-1: six failures, five shell extraction setup errors and the expected T6 assertion. CP-2: zero results because the plan omitted TUnit OR wildcard hints. Neither row is accepted as the red-first proof.

CHECKPOINT CP-1 commit=514878838d279f7b0cdc2830bc04f23cc8bc559b build=ok filter=/*/*/RemoteScriptContractTests/C913_* executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-123436-4514/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=514878838d279f7b0cdc2830bc04f23cc8bc559b sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=514878838d279f7b0cdc2830bc04f23cc8bc559b build=reused filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-123436-4514/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=514878838d279f7b0cdc2830bc04f23cc8bc559b sourceState=clean buildSource=verified

Generated TRX/JSON/logs remain ignored under `.antiphon/checkpoints/20261004-123436-4514/`. The full CHECKPOINT lines above are unedited.

## Historical pending snapshot (superseded by later results)

V-1..V-8 and R-1..R-4: pending ordinary acceptance. CP-1..CP-8: no accepted outcome yet. Whole Unit lane and full affected classes: pending. Q-1 is post-Review operational scope, not a Code pass. PC-1 through PC-165, all variants: pending post-land SourceLanding Mutation. No deliberate mutant executed. Restart: none; deployment/activation owner remains caller.

## Corrected red-first proof

Actual SHA `173598c78b124adb56195b19be521786f88e712d`; clean receipts, verified build. Fresh TRX: six Seed and three image methods all execute, all fail at their specified witness, no skips. This is intentional diagnostic red, not acceptance. Exact witnesses: ordinary-only-accepted; schema3-published; schema3-valid; schema3-prune-success; absent-temp-accepted; schema3-receipt-accepted; aspnet-framework-reference; sources-and-fallbacks-cleared; package-mount-contract.

CHECKPOINT CP-1 commit=173598c78b124adb56195b19be521786f88e712d build=ok filter=/*/*/RemoteScriptContractTests/C913_* executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-123829-34d9/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=173598c78b124adb56195b19be521786f88e712d sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=173598c78b124adb56195b19be521786f88e712d build=reused filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-123829-34d9/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=173598c78b124adb56195b19be521786f88e712d sourceState=clean buildSource=verified

## First acceptance attempt

Actual SHA `066341bc46cbab7d0f0ad5ea8c817f0af4299b0a`. CP-3 43/47 passed; four adapter failures: legacy Prune missing schema context, new Seed remap recursively altered staging path, donor gate image inspection fake returned failure, saved Seed lacked manifest helper extraction. CP-4 2/3 passed; owned-init assertion searched the wrong argument order. CP-5 stopped before cases because a login shell discarded the temporary jq PATH. jq installed instead to the already-supported user bin `/home/app/.local/bin`, same verified binary, no system profile changes. These are not claimed inherited failures.

CHECKPOINT CP-5 commit=066341bc46cbab7d0f0ad5ea8c817f0af4299b0a build=n/a filter=pwsh -NoProfile -File scripts/test-deploy-server2.ps1 -RequireJq executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=066341bc46cbab7d0f0ad5ea8c817f0af4299b0a sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=066341bc46cbab7d0f0ad5ea8c817f0af4299b0a build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=43 failed=4 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-125739-51eb/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=066341bc46cbab7d0f0ad5ea8c817f0af4299b0a sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=066341bc46cbab7d0f0ad5ea8c817f0af4299b0a build=reused filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*) executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-125739-51eb/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=066341bc46cbab7d0f0ad5ea8c817f0af4299b0a sourceState=clean buildSource=verified

## 20261004-131027-c332

CP-5 executed 24 groups / 66 invocations / 227 assertions, zero failures. CP-3/4 did not execute: one new Shouldly overload caused compilation errors. Fixed in fed5ed29b. The mixed-run heading buildSource=unknown makes the CP-5 selection fail the automatic receipt validator (source_ineligible); retain execution evidence, requalify command separately.

CHECKPOINT CP-5 commit=153940e2a511eb9dd4ed1b35421904c7641baa44 build=n/a filter=pwsh -NoProfile -File scripts/test-deploy-server2.ps1 -RequireJq executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=153940e2a511eb9dd4ed1b35421904c7641baa44 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=153940e2a511eb9dd4ed1b35421904c7641baa44 build=failed filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=153940e2a511eb9dd4ed1b35421904c7641baa44 sourceState=clean buildSource=unknown
CHECKPOINT CP-4 commit=153940e2a511eb9dd4ed1b35421904c7641baa44 build=failed filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=153940e2a511eb9dd4ed1b35421904c7641baa44 sourceState=clean buildSource=unknown

## 20261004-131949-bda9

CP-3 selected 47 but only 23 executed: 20 pass, 3 fail, 24 skip due to direct test host PATH missing jq. No skipped row accepted. Failures: Fixture summary array-concatenation precedence; old Prune write trace included the newly real read-only legacy query; Deploy reported RecycleToolsMissing from jq absence. CP-4 executed 3/3 green. Production summary parser and trace split repaired in a9b22dabd; subsequent commands explicitly prefix /home/app/.local/bin.

CHECKPOINT CP-3 commit=409816c277640fbb30137ce45e30bba3d74cdb36 build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=23 passed=20 failed=3 skipped=24 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-131949-bda9/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=409816c277640fbb30137ce45e30bba3d74cdb36 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=409816c277640fbb30137ce45e30bba3d74cdb36 build=reused filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-131949-bda9/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=409816c277640fbb30137ce45e30bba3d74cdb36 sourceState=clean buildSource=verified

## 20261004-132324-d219

47 executed: 46 passed, 1 failed, 0 skipped. T2 donor-ID fake changed both initial and post-restart observations identically, so the expected refusal was not exercised. Corrected boundary injection in 0cfe6c69c; not inherited red.

CHECKPOINT CP-3 commit=a9b22dabdacdb518f4f8b1ef081e2d38b5a9cacb build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=46 failed=1 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-132324-d219/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a9b22dabdacdb518f4f8b1ef081e2d38b5a9cacb sourceState=clean buildSource=verified

## 20261004-133210-e472

47 executed: 46 passed, 1 failed, 0 skipped. T2 reached all 19 owned-child crash barriers and reopened held/accepted state correctly; saved ordinary import found a real empty donor array under nounset (ids: unbound variable). Initialized both donor census arrays in 65f864b7f4f737d917ea3571f4084d82d0de2642.

CHECKPOINT CP-3 commit=58f2f9690764e4bfd4883f815b1b179d487b6ac0 build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=46 failed=1 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-133210-e472/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=58f2f9690764e4bfd4883f815b1b179d487b6ac0 sourceState=clean buildSource=verified

## 20261004-134202-b004

CP-3: 47 executed, 45 pass, 2 fail, 0 skip. New saved-source fixture lacked ROOT; Fixture apphost exposed a dynamically shadowed root in the Docker trace shim. Fixed fixture setup and preserved command positional arguments in fb52b6949. CP-4 3/3 green. CP-5 24 groups/66 invocations/227 assertions, no failures/skips. validate-checkpoint-receipt.ps1 -Rows CP-4,CP-5 accepted both rows at 9044a18dfaaa822fa00fbcaa28751f7091246d2a; clean source, verified build provenance. Source was frozen throughout.

CHECKPOINT CP-5 commit=9044a18dfaaa822fa00fbcaa28751f7091246d2a build=n/a filter=pwsh -NoProfile -File scripts/test-deploy-server2.ps1 -RequireJq executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=9044a18dfaaa822fa00fbcaa28751f7091246d2a sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=9044a18dfaaa822fa00fbcaa28751f7091246d2a build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=45 failed=2 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-134202-b004/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=9044a18dfaaa822fa00fbcaa28751f7091246d2a sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=9044a18dfaaa822fa00fbcaa28751f7091246d2a build=reused filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-134202-b004/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=9044a18dfaaa822fa00fbcaa28751f7091246d2a sourceState=clean buildSource=verified

## 20261004-135252-6b9b

CP-3: 47 executed, 47 passed, 0 failed/skipped. Fresh TRX inspected; expanded age/role/budget/headroom/legacy-recovery, saved-source, crash, archive alias and inventory matrices pass. Receipt validator accepted CP-3 at fb52b6949f912323dce1e2b14befa2610857e3ba. Build 99.66s; tests 181.60s; slot=granted waited=0s.

CHECKPOINT CP-3 commit=fb52b6949f912323dce1e2b14befa2610857e3ba build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=47 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-135252-6b9b/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=fb52b6949f912323dce1e2b14befa2610857e3ba sourceState=clean buildSource=verified

## 20261004-135954-a3ce

Loaded run at d757d93f923c9f677c83c2c42533a2e02d95dbef: CP-3 47 executed, 45 pass, 2 timeout failures, 0 skip. T2 and T4 hit the existing 60s LinuxShell bound. Observed 24 `yes` CPU stress workers parent PID 24385, cwd /work/worktrees/task-d662028c, with load average 30-35; left foreign processes untouched. Stress workers exited before the unchanged-source CP-3 rerun. No timeout/assertion change. CP-4 3/3 passed and receipt validated at the actual tested SHA. This is one loaded execution, not a Review-authorized repeat-budget extension; unchanged selection has at most one normal rerun so far. No inherited-failure attribution: these are this task's new tests.

CHECKPOINT CP-3 commit=d757d93f923c9f677c83c2c42533a2e02d95dbef build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=45 failed=2 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-135954-a3ce/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=d757d93f923c9f677c83c2c42533a2e02d95dbef sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=d757d93f923c9f677c83c2c42533a2e02d95dbef build=reused filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-135954-a3ce/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=d757d93f923c9f677c83c2c42533a2e02d95dbef sourceState=clean buildSource=verified

## 20261004-141543-d299

Unchanged-source normal rerun after external CPU stress exited: 47 executed, 46 pass, 1 fail, 0 skip. Prune passed at the unchanged 60s limit. T2 completed its prior matrices and failed the newly appended busy-main live acceptance because the fixture retained C590_SAVED_DONOR from the saved-source cases, correctly causing CacheDonorSourceConflict. Cleared that test state in ee5d402e8. Also made optional inventory faults occur only after main succeeds, asserted exact refusal diagnoses, and checked the main smoke independently. These are test fixes, not inherited failures or production assertion relaxations.

CHECKPOINT CP-3 commit=d757d93f923c9f677c83c2c42533a2e02d95dbef build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=46 failed=1 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-141543-d299/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=d757d93f923c9f677c83c2c42533a2e02d95dbef sourceState=clean buildSource=verified

## 20261004-142157-7483

CP-3 final focused Seed acceptance at ee5d402e81657861dc2123030f7d87c416d6113d: 47 executed, all passed, no skips. Fresh XML roster confirms C849=18, C905=2, C912=9, C913=6, C973=12. Receipt validator accepted clean source and verified build provenance. No repeats requested after this green. The later whole Unit lane is the independently required Final scope, not a proof repetition.

CHECKPOINT CP-3 commit=ee5d402e81657861dc2123030f7d87c416d6113d build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=47 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-142157-7483/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=ee5d402e81657861dc2123030f7d87c416d6113d sourceState=clean buildSource=verified

## Declared Final profile supplement

`.antiphon/c913-final-unit.yaml` runs exactly `/*/*/*/*[Category=Unit]` with one isolated `bin-c913-final-linux/` build. Both named full affected classes are Category Unit: RemoteScriptContractTests and CodexRunnerImageContractTests. This unlisted checkpoint/build is required by the Final brief beyond the appendix's focused eight rows. Initial estimate 15 minutes; standard 3x/45-minute row cap set before first execution, never widened. No full-assembly run. The generated YAML stays ignored.

## Whole Unit timeout and loop repair

Run `20261004-142922-6299` at `ee5d402e81657861dc2123030f7d87c416d6113d` built successfully but hit the derived total limit at 40m02s before any TRX. No counts or full-Unit pass are claimed. Console failures were C944_All_cache_loop_variables_are_local and the C957 loop scratch variant: c849_smoke had introduced an undeclared pack loop variable. The initial build slot was granted, waited=0s; the final timeout line below reports skipped/unknown and is retained verbatim. The owned test host was gone before editing source.

CHECKPOINT CP-Final-Unit commit=ee5d402e81657861dc2123030f7d87c416d6113d build=n/a filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=total slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown

Declared base diagnostic on detached base `7f489f066c69caa2ee1380361e8d0135dd451db9`: C944 plus all three C957 variants, 4/4 pass, no skips, fresh TRX inspected, clean/verified provenance. This establishes that the loop defect was introduced, not inherited.

CHECKPOINT CP-C944-Base commit=7f489f066c69caa2ee1380361e8d0135dd451db9 build=ok filter=/*/*/RemoteScriptContractTests/(C944_All_cache_loop_variables_are_local*)|(C957_Scratch_mutation_controls_go_red_and_restore*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/c913-base/.antiphon/checkpoints/CP-C944-Base-20261004-151603-222f/run.trx slot=granted waited=0s dirty=0 source=7f489f066c69caa2ee1380361e8d0135dd451db9 sourceState=clean buildSource=verified
CHECKPOINT CP-C944-Base EXIT CODE: 0

Fixed by `0cdfb5cd9f9658f7279837924ac5f19f39af762b` (pushed before next build): local pack variable; wrapper test intercepts Git observation with explicit dirty-source refusal so later dirty Mutation snapshots reach the target guard. No deliberate CARD-0849 mutant was run.

Declared extra CP-C944-Repair tests exactly those four introduced findings. First external-manifest launch refused before build because its After token differed from the imported three-token S1/S2/S3 group; corrected only ignored YAML. One earlier attempted manifest validation also refused before build (validate is the receipt validator). These are not test results.

The Unit discovery/help drivers were gated (slot=granted waited=0s). Discovery printed 14392 names but did not establish category-filtered execution; that number is not a floor or acceptance count. Source census finds 12 exact namespaces with Unit attributes. The replacement Final supplement `.antiphon/c913-unit-parts.yaml` runs their union, retaining Category=Unit, serial namespace rows and one isolated bin-c913-unit-parts build. Estimated 54 minutes plus build; original per-test limits unchanged, per-row caps <=45 minutes. It is a bounded partition after the missing-TRX total timeout, not a widened timeout or full-assembly run. Both named affected classes are included in full in Scripts and Infrastructure.

## 20261004-152618-3cb1

Final focused repair at 0cdfb5cd9f9658f7279837924ac5f19f39af762b: CP-3 47/47 (18 C849, 2 C905, 9 C912, 6 C913, 12 C973), CP-4 3/3, CP-C944-Repair 4/4; zero skipped/failed. All fresh TRX inspected. Receipt validator accepted all three rows, clean source and verified build provenance. Build 139.97s; CP-3 test host 232.93s; total 6m33s. All slot=granted waited=0s.

CHECKPOINT CP-3 commit=0cdfb5cd9f9658f7279837924ac5f19f39af762b build=ok filter=/*/*/RemoteScriptContractTests/(C849_*)|(C905_*)|(C912_*)|(C913_*)|(C973_*) executed=47 passed=47 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-152618-3cb1/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=0cdfb5cd9f9658f7279837924ac5f19f39af762b sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=0cdfb5cd9f9658f7279837924ac5f19f39af762b build=reused filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-152618-3cb1/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=0cdfb5cd9f9658f7279837924ac5f19f39af762b sourceState=clean buildSource=verified
CHECKPOINT CP-C944-Repair commit=0cdfb5cd9f9658f7279837924ac5f19f39af762b build=reused filter=/*/*/RemoteScriptContractTests/(C944_All_cache_loop_variables_are_local*)|(C957_Scratch_mutation_controls_go_red_and_restore*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-152618-3cb1/rows/CP-C944-Repair/run.trx slot=granted waited=0s dirty=0 source=0cdfb5cd9f9658f7279837924ac5f19f39af762b sourceState=clean buildSource=verified

## 20261004-153316-941f: real image revealed archive mode defect

Actual source 0cdfb5cd9f9658f7279837924ac5f19f39af762b. CP-5 24 groups/66 invocations/227 assertions, zero failures/skips, receipt validator accepted. CP-6 built one image successfully (sha256:15866f6c171ac8a62fd8276ff1c2d71b9c064ed8b49ee7c8fe7502d97aeb69f1, matching revision), then 10/11 rows passed; net9-offline failed ImagePackMissing:Microsoft.NETCore.App.Host.linux-x64. This is a real failure, not qualified V-1. All five created owned volumes were removed by the wrapper.

Read-only uid-1654 inspection of that immutable image showed the pack exists but apphost is mode0744, uid/gid2001 from the pinned archive. The new executable-pack guard correctly refused it. No inherited-failure attribution is made from this one image. Repair normalizes extraction ownership and that apphost to0755; synthetic real-tar fixture now starts at0744 and asserts recipient0755. PC-3 gains two explicit extraction variants, both pending Mutation.

CHECKPOINT CP-5 commit=0cdfb5cd9f9658f7279837924ac5f19f39af762b build=n/a filter=pwsh -NoProfile -File scripts/test-deploy-server2.ps1 -RequireJq executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=0cdfb5cd9f9658f7279837924ac5f19f39af762b sourceState=clean buildSource=notApplicable
CHECKPOINT CP-6 commit=0cdfb5cd9f9658f7279837924ac5f19f39af762b build=n/a filter=pwsh -NoProfile -File scripts/verify-card0660-codex-image.ps1 -Target session-testing -Image "${C913_IMAGE:?}" -SourceRevision "${C913_SHA:?}" -ResultsRoot "${C913_RESULTS:?}" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=0cdfb5cd9f9658f7279837924ac5f19f39af762b sourceState=clean buildSource=notApplicable

## Archive recipient mode regression at base

Repair commit a059af5ecd40d7a8b500806fd775a2a8f868aee0 was committed/pushed before tests. Declared extra command row reads the unchanged base Dockerfile at 7f489f066c69caa2ee1380361e8d0135dd451db9 with the new committed fixture; no production mutation. Fresh command result is exit1 with exactly FAIL host-pack-public-executable. The fixture starts the archive apphost at0744, extracts using the base production chain and observes its actual recipient mode. The base checkout remained clean. This is intended regression red, not an image-qualification pass.

CHECKPOINT CP-ArchiveMode-Base commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=bash scripts/fixtures/c913-image-contract.sh archive /work/worktrees/task-62fc2d31/.antiphon/c913-base executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=a059af5ecd40d7a8b500806fd775a2a8f868aee0 sourceState=clean buildSource=notApplicable

## 20261004-154541-b416: image repair accepted

Actual tested source a059af5ecd40d7a8b500806fd775a2a8f868aee0. CP-4 3/3 passed, zero skips; fresh method roster inspected. CP-6 one fresh image build, all11 rows pass, exit0. Receipt validator accepted both rows: clean source, verified build provenance for CP-4, notApplicable for command CP-6. Both slot=granted waited=0s. The immutable image is sha256:acfc53c111cf8adc4b49033e57d669a2ebcdadb6c27d05ecbf68cef8f25b69cb, tag antiphon-c913-62fc2d31:a059af5e-1545, revision a059af5ecd40d7a8b500806fd775a2a8f868aee0. Evidence root .antiphon/c913-image-a059-1545.

Native recipient receipt (unaltered):

C913_PACK /usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/9.0.20
C913_PACK /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/9.0.20
C913_PACK /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/9.0.20
C660_ROW net9-offline ok SDK=10.0.401 uid=1654 restore=0 build=0 native-run=0 stdout=net9-offline-ok empty-mounted-pair network=none

CHECKPOINT CP-6 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=pwsh -NoProfile -File scripts/verify-card0660-codex-image.ps1 -Target session-testing -Image "${C913_IMAGE:?}" -SourceRevision "${C913_SHA:?}" -ResultsRoot "${C913_RESULTS:?}" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=a059af5ecd40d7a8b500806fd775a2a8f868aee0 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-4 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=ok filter=/*/*/CodexRunnerImageContractTests/(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)|(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-154541-b416/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a059af5ecd40d7a8b500806fd775a2a8f868aee0 sourceState=clean buildSource=verified

Both qualification owners c660q-20261004T153621Z-a6bd68 and c660q-20261004T154546Z-268cf8 had zero volumes remaining in label-scoped docker volume ls after completion. No foreign Docker resources removed.

## SourceLanding Mutation pending roster

Every entry below and every internal variant named in the verification design remains pending. Code ordinary fault-input cases, initial assertion reds, inherited C957 scratch controls, and Docker success do not discharge any PC. Mutation owns deliberate CARD-0849 source defects, assertion-red/restore/green cycles and missing-control discovery after Review and landing by the original Code task. Q-1 remains a separate post-Review caller-owned operational Fixture.

| PC | Planned defect | Exact method | Expected witness | Status |
|---|---|---|---|---|
| PC-1 | Break G-1: Change one hex digit in NET9_SDK_SHA512 | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `pin-pair` | Pending (all variants) |
| PC-2 | Break G-2: Replace the sha512sum pipeline with true while preserving valid shell syntax | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `bad-archive-no-extract` | Pending (all variants) |
| PC-3 | Break G-3: Delete only the host-pack tar member, its post-extract test and its apphost chmod | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `host-pack-member` | Pending (all variants) |
| PC-4 | Break G-4: Delete only the core-reference tar member and its post-extract test | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `core-ref-member` | Pending (all variants) |
| PC-5 | Break G-5: Delete only the ASP.NET-reference tar member and its post-extract test | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `aspnet-ref-member` | Pending (all variants) |
| PC-6 | Break G-6: Remove the final COPY --from=net9-packs instruction | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `packs-visible-after-sdk-copy` | Pending (all variants) |
| PC-7 | Break G-7: Change the build-stage base from sdk:10.0 to sdk:9.0 | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | `sdk10-policy` | Pending (all variants) |
| PC-8 | Break G-8: Delete volume-nocopy from the net9 package mount | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `package-mount-contract` | Pending (all variants) |
| PC-9 | Break G-9: Use the package volume as the scratch source | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `private-scratch-contract` | Pending (all variants) |
| PC-10 | Break G-10: Change net9 child network from none to bridge | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `network-none` | Pending (all variants) |
| PC-11 | Break G-11: Change only net9 child user to 0:0 | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `uid-1654` | Pending (all variants) |
| PC-12 | Break G-12: Append a foreign volume to initialization arguments | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `owned-init-only` | Pending (all variants) |
| PC-13 | Break G-13: Add a host Docker socket bind to the net9 child | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `isolated-child-allowlist` | Pending (all variants) |
| PC-14 | Break G-14: Replace the package emptiness predicate with true | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `warm-packages-refused` | Pending (all variants) |
| PC-15 | Break G-15: Replace the scratch emptiness predicate with true | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `warm-scratch-refused` | Pending (all variants) |
| PC-16 | Break G-16: Replace the home emptiness predicate with true | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `warm-home-refused` | Pending (all variants) |
| PC-17 | Break G-17: Bypass the host-pack existence/executable predicate | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `missing-host-refused` | Pending (all variants) |
| PC-18 | Break G-18: Bypass only the core-reference predicate | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `missing-core-ref-refused` | Pending (all variants) |
| PC-19 | Break G-19: Bypass only the ASP.NET-reference predicate | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `missing-aspnet-ref-refused` | Pending (all variants) |
| PC-20 | Break G-20: Remove fallbackPackageFolders clear from generated NuGet.Config | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `sources-and-fallbacks-cleared` | Pending (all variants) |
| PC-21 | Break G-21: Set NuGetAudit true in the restore invocation | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `audit-and-workload-disabled` | Pending (all variants) |
| PC-22 | Break G-22: Set UseAppHost to false in the generated project | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `native-project-contract` | Pending (all variants) |
| PC-23 | Break G-23: Remove the ASP.NET type use from Program.cs | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `aspnet-compiled` | Pending (all variants) |
| PC-24 | Break G-24: Ignore a nonzero restore exit | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `restore-exit-refused` | Pending (all variants) |
| PC-25 | Break G-25: Ignore a nonzero build exit | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `build-exit-refused` | Pending (all variants) |
| PC-26 | Break G-26: Replace direct executable capture with a constant success token | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `native-run-required` | Pending (all variants) |
| PC-27 | Break G-27: Remove the framework-package absence check after execution | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `downloaded-framework-refused` | Pending (all variants) |
| PC-28 | Break G-28: Ignore image revision mismatch in SkipBuild branch | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `revision-mismatch-refused` | Pending (all variants) |
| PC-29 | Break G-29: Accept ok text when child exit is nonzero | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `child-exit-required` | Pending (all variants) |
| PC-30 | Break G-30: Accept any C660_ROW ok line for net9-offline | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `matching-row-required` | Pending (all variants) |
| PC-31 | Break G-31: Broaden cleanup enumeration to all volume names | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` | `cleanup-owned-volumes-only` | Pending (all variants) |
| PC-32 | Break G-32: Remove NUGET_SCRATCH inherited environment check in c849_smoke | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `smoke-environment-refused` | Pending (all variants) |
| PC-33 | Break G-33: Run the deployment smoke driver directly instead of through build-slot.ps1 | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `smoke-lease-required` | Pending (all variants) |
| PC-34 | Break G-34: Call build-slot.ps1 from the network-none probe | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `offline-no-inner-lease` | Pending (all variants) |
| PC-35 | Break G-35: Restore the AppHostDonorMissing guard in generic tree validation | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `ordinary-only-accepted` | Pending (all variants) |
| PC-36 | Break G-36: Remove incomplete version cleanup | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `incomplete-stage-only` | Pending (all variants) |
| PC-37 | Break G-37: Return success when filtered complete-version count is zero | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `empty-full-refused` | Pending (all variants) |
| PC-38 | Break G-38: Accept a regular file in place of a version directory | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `invalid-layout-refused` | Pending (all variants) |
| PC-39 | Break G-39: Return success from c849_validate_seed_relative | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `unsafe-relative-refused` | Pending (all variants) |
| PC-40 | Break G-40: Remove type l from staged-tree forbidden-entry search | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `symlink-refused` | Pending (all variants) |
| PC-41 | Break G-41: Remove staged-tree links +1 predicate | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `hardlink-refused` | Pending (all variants) |
| PC-42 | Break G-42: Remove type p from staged-tree forbidden-entry search | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `special-entry-refused` | Pending (all variants) |
| PC-43 | Break G-43: Ignore nonzero staged npm cache verify exit | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `npm-before-manifest` | Pending (all variants) |
| PC-44 | Break G-44: Enumerate only packages when generating the manifest | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `manifest-covers-npm` | Pending (all variants) |
| PC-45 | Break G-45: Emit a constant file digest instead of hashing bytes | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `manifest-binds-bytes` | Pending (all variants) |
| PC-46 | Break G-46: Emit constant 000 executable bits | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `manifest-binds-exec` | Pending (all variants) |
| PC-47 | Break G-47: Remove LC_ALL=C sorted path ordering | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `manifest-order-independent` | Pending (all variants) |
| PC-48 | Break G-48: Ignore duplicate manifest paths | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `duplicate-record-refused` | Pending (all variants) |
| PC-49 | Break G-49: Accept an absolute manifest record path | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `unsafe-record-refused` | Pending (all variants) |
| PC-50 | Break G-50: Skip comparison of imported packages with stage manifest | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `package-import-corruption-no-marker` | Pending (all variants) |
| PC-51 | Break G-51: Skip comparison of imported npm with stage manifest | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `npm-import-corruption-no-marker` | Pending (all variants) |
| PC-52 | Break G-52: Publish final marker immediately before calling c849_smoke | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `smoke-failure-no-marker` | Pending (all variants) |
| PC-53 | Break G-53: Ignore failure moving stage to recovery | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `recovery-save-failure-no-marker` | Pending (all variants) |
| PC-54 | Break G-54: Ignore docker start failure | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `restart-failure-no-marker` | Pending (all variants) |
| PC-55 | Break G-55: Remove post-restart donor ID comparison | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `donor-id-change-no-marker` | Pending (all variants) |
| PC-56 | Break G-56: Bypass final c849_status_zero reconnected gate | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `reconnect-failure-no-marker` | Pending (all variants) |
| PC-57 | Break G-57: Write marker directly to C849_READY instead of temp plus rename | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `publication-failure-no-partial-marker` | Pending (all variants) |
| PC-58 | Break G-58: Point incomplete-version removal at saved source instead of stage | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `saved-source-byte-identical` | Pending (all variants) |
| PC-59 | Break G-59: Accept nonempty unmarked targets as a completed import | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `interrupted-import-held` | Pending (all variants) |
| PC-60 | Break G-60: Replace confined stage condition with nonempty-stage only | `RemoteScriptContractTests.C849_Seed_refuses_invalid_donors_and_partial_payloads` | `cleanup-sibling-retained` | Pending (all variants) |
| PC-61 | Break G-61: Bypass pre-stop c849_status_zero check | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `busy-donor-no-stop` | Pending (all variants) |
| PC-62 | Break G-62: Convert failed donor ps call into empty output | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `unknown-process-no-stop` | Pending (all variants) |
| PC-63 | Break G-63: Bypass active-process zero comparison | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `writer-no-stop` | Pending (all variants) |
| PC-64 | Break G-64: Skip saved-branch c849_no_temp_containers | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `saved-temp-present-no-copy` | Pending (all variants) |
| PC-65 | Break G-65: Remove post-copy c849_prune_idle call only | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `saved-late-busy-no-import` | Pending (all variants) |
| PC-66 | Break G-66: Remove per-target c849_no_cache_attachments call | `RemoteScriptContractTests.C913_Full_marker_binds_verified_recovery_before_publication` | `saved-late-attachment-no-import` | Pending (all variants) |
| PC-67 | Break G-67: Treat schema=4 kind=full as schema3 | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `unknown-schema-refused` | Pending (all variants) |
| PC-68 | Break G-68: Permit payload-sha256 in schema3 full | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `mixed-marker-refused` | Pending (all variants) |
| PC-69 | Break G-69: Use first-value-wins for duplicate kind keys | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `duplicate-marker-refused` | Pending (all variants) |
| PC-70 | Break G-70: Remove marker symlink rejection | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `symlink-marker-refused` | Pending (all variants) |
| PC-71 | Break G-71: Skip manifest file SHA-256 comparison with marker | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `manifest-digest-refused` | Pending (all variants) |
| PC-72 | Break G-72: Trust marker/manifest without rehashing recovery | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `recovery-bytes-refused` | Pending (all variants) |
| PC-73 | Break G-73: Allow recovery path through a symlink outside cache root | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `recovery-escape-refused` | Pending (all variants) |
| PC-74 | Break G-74: Ignore docker image inspect failure for retained image | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `recovery-image-refused` | Pending (all variants) |
| PC-75 | Break G-75: Ignore wrong packages identity in schema3 marker | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `marker-volume-refused` | Pending (all variants) |
| PC-76 | Break G-76: Compare mutable packages against recovery manifest on every readiness call | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `cache-churn-accepted` | Pending (all variants) |
| PC-77 | Break G-77: Route legacy marker through schema3 cache-churn acceptance | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `legacy-payload-change-refused` | Pending (all variants) |
| PC-78 | Break G-78: Rewrite a valid legacy marker as schema3 during readiness | `RemoteScriptContractTests.C913_Marker_versions_keep_legacy_full_and_cold_contracts_distinct` | `legacy-marker-byte-identical` | Pending (all variants) |
| PC-79 | Break G-79: Change full-required check to allow cold | `RemoteScriptContractTests.C912_Cold_marker_has_distinct_validation_and_full_context_refusal` | `full-context-refused` | Pending (all variants) |
| PC-80 | Break G-80: Compare only kind and digest, ignoring schema | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `both-schema-refused` | Pending (all variants) |
| PC-81 | Break G-81: Ignore digest-type when comparing full receipts | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `both-digest-type-refused` | Pending (all variants) |
| PC-82 | Break G-82: Ignore digest value when comparing full receipts | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `both-digest-value-refused` | Pending (all variants) |
| PC-83 | Break G-83: Accept duplicate schema fields by taking the first | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `receipt-shape-refused` | Pending (all variants) |
| PC-84 | Break G-84: Print smoke=passed for full marker reuse | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `reuse-smoke-not-run` | Pending (all variants) |
| PC-85 | Break G-85: Accept full Retired with no matching smoke-summary | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `full-smoke-evidence-required` | Pending (all variants) |
| PC-86 | Break G-86: Call c849_smoke unconditionally in case_verify_runner_caches | `RemoteScriptContractTests.C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads` | `cold-no-smoke` | Pending (all variants) |
| PC-87 | Break G-87: Bypass recovery existence guard | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `missing-recovery-no-delete` | Pending (all variants) |
| PC-88 | Break G-88: Bypass recovery-manifest comparison immediately before deletion | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `changed-recovery-no-delete` | Pending (all variants) |
| PC-89 | Break G-89: Bypass recovery image inspect | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `missing-image-no-delete` | Pending (all variants) |
| PC-90 | Break G-90: Bypass preview source-sha comparison | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `stale-source-no-delete` | Pending (all variants) |
| PC-91 | Break G-91: Remove preview age range check | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `stale-time-no-delete` | Pending (all variants) |
| PC-92 | Break G-92: Skip volumes.txt digest comparison | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `preview-digest-no-delete` | Pending (all variants) |
| PC-93 | Break G-93: Skip comparison of volumes-now.txt with preview | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `changed-volume-no-delete` | Pending (all variants) |
| PC-94 | Break G-94: Move validation into per-target destructive loop | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `second-target-invalid-no-delete` | Pending (all variants) |
| PC-95 | Break G-95: Remove nested-mount refusal in c849_prune_validate_tree | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `nested-mount-no-delete` | Pending (all variants) |
| PC-96 | Break G-96: Replace content deletion with removal of volume root | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `roots-and-recovery-retained` | Pending (all variants) |
| PC-97 | Break G-97: Execute legacy framework-copy branch for schema3 | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `schema3-no-framework-copy` | Pending (all variants) |
| PC-98 | Break G-98: Skip legacy refill branch | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `legacy-refill-required` | Pending (all variants) |
| PC-99 | Break G-99: Ignore nonzero ordinary refill exit | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `refill-failure-no-success` | Pending (all variants) |
| PC-100 | Break G-100: Skip exact C849_REFILL receipt check | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `refill-receipt-required` | Pending (all variants) |
| PC-101 | Break G-101: Skip main c849_smoke after refill | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `prune-smoke-required` | Pending (all variants) |
| PC-102 | Break G-102: Skip final c849_budget_gate | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `over-budget-no-success` | Pending (all variants) |
| PC-103 | Break G-103: Insert drain/clear mutation before success | `RemoteScriptContractTests.C913_Prune_validates_recovery_without_refilling_image_packs` | `admission-held` | Pending (all variants) |
| PC-104 | Break G-104: Bypass live runner counter guard in c849_prune_idle | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `maintenance-busy-no-delete` | Pending (all variants) |
| PC-105 | Break G-105: Bypass active-process guard in c849_prune_idle | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `maintenance-process-no-delete` | Pending (all variants) |
| PC-106 | Break G-106: Permit foreign project/service attached container | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `maintenance-attachment-no-delete` | Pending (all variants) |
| PC-107 | Break G-107: Ignore busy broker response | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `maintenance-broker-no-delete` | Pending (all variants) |
| PC-108 | Break G-108: Remove CacheSeedAlreadyReady refusal | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `reset-marker-no-delete` | Pending (all variants) |
| PC-109 | Break G-109: Remove per-target docker ps -aq recheck | `RemoteScriptContractTests.C849_Prune_refuses_stale_or_busy_authority` | `reset-late-attachment-no-delete` | Pending (all variants) |
| PC-110 | Break G-110: Restore production-donor docker cp in c849_fixture_apphost | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `no-production-payload-read` | Pending (all variants) |
| PC-111 | Break G-111: Convert failed docker ps into empty successful census | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `failed-census-refused` | Pending (all variants) |
| PC-112 | Break G-112: Accept absent temp with omitted retiredAt | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `unknown-status-refused` | Pending (all variants) |
| PC-113 | Break G-113: Treat retired status as absence despite a found running container | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `contradictory-container-refused` | Pending (all variants) |
| PC-114 | Break G-114: Convert docker inspect failure to container=absent | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `failed-inspect-refused` | Pending (all variants) |
| PC-115 | Break G-115: Ignore existing owned-name volume at fixture setup | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `occupied-fixture-refused` | Pending (all variants) |
| PC-116 | Break G-116: Remove the run-prefix check from volume cleanup | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `foreign-ledger-no-remove` | Pending (all variants) |
| PC-117 | Break G-117: Accept duplicate control ID in place of missing expected ID | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `fixture-roster-refused` | Pending (all variants) |
| PC-118 | Break G-118: Accept any failing command as an expected control red | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `setup-error-not-control-red` | Pending (all variants) |
| PC-119 | Break G-119: Allow credential field through receipt projection | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `toxic-receipt-refused` | Pending (all variants) |
| PC-120 | Break G-120: Insert dotnet restore in c849_cold_seed | `RemoteScriptContractTests.C912_Cold_volumes_seed_accepts_busy_main_without_packages` | `no-package-network-or-slot-call` | Pending (all variants) |
| PC-121 | Break G-121: Add a main sessions==0 requirement | `RemoteScriptContractTests.C912_Cold_volumes_seed_accepts_busy_main_without_packages` | `main-unchanged` | Pending (all variants) |
| PC-122 | Break G-122: Treat failed volume census as empty | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-census-error` | Pending (all variants) |
| PC-123 | Break G-123: Treat failed container inspect as empty mounts | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-inspect-error` | Pending (all variants) |
| PC-124 | Break G-124: Use docker ps -q instead of -aq in all-container census | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-stopped-mount` | Pending (all variants) |
| PC-125 | Break G-125: Skip both canonical bind-overlap checks | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `cold-bind-overlap-refused` | Pending (all variants) |
| PC-126 | Break G-126: Remove retiredAt condition from temp predicate | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-temp-unretired` | Pending (all variants) |
| PC-127 | Break G-127: Remove has(runnerSessions) and its type condition | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-temp-counter-omitted` | Pending (all variants) |
| PC-128 | Break G-128: Use glob-star emptiness test that misses dotfiles | `RemoteScriptContractTests.C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets` | `preflight-no-write-hidden` | Pending (all variants) |
| PC-129 | Break G-129: Remove only the P6 proof call | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | `phase-proof-recorded-P6` | Pending (all variants) |
| PC-130 | Break G-130: Bypass ID equality against C849_COLD_MAIN_ID | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | `recheck-refused-P1` | Pending (all variants) |
| PC-131 | Break G-131: Bypass image equality against C849_COLD_MAIN_IMAGE | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | `changed-image-refused` | Pending (all variants) |
| PC-132 | Break G-132: Bypass equality against C849_COLD_MAIN_MOUNTS | `RemoteScriptContractTests.C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries` | `changed-mounts-refused` | Pending (all variants) |
| PC-133 | Break G-133: Remove C849_COLD_PRESENT check before init | `RemoteScriptContractTests.C912_Cold_seed_refuses_when_created_volume_disappears_before_init` | `no-unlabelled-auto-created-volume` | Pending (all variants) |
| PC-134 | Break G-134: Ignore probe return code after a rename failure | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | `probe-refused-probe-rename-fail` | Pending (all variants) |
| PC-135 | Break G-135: Treat timeout 124 as successful probe | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | `probe-refused-probe-timeout` | Pending (all variants) |
| PC-136 | Break G-136: Ignore failed docker rm of probe helper | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | `probe-refused-cleanup-fail` | Pending (all variants) |
| PC-137 | Break G-137: Replace exact canary unlink with recursive parent deletion | `RemoteScriptContractTests.C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit` | `probe-refused-no-marker-or-outside-write` | Pending (all variants) |
| PC-138 | Break G-138: Allow payload-sha256 field in cold marker | `RemoteScriptContractTests.C912_Cold_marker_has_distinct_validation_and_full_context_refusal` | `malformed-marker-refused` | Pending (all variants) |
| PC-139 | Break G-139: Require historical cold image to exist without fallback | `RemoteScriptContractTests.C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune` | `available-main-helper` | Pending (all variants) |
| PC-140 | Break G-140: Skip compose project check on fallback main | `RemoteScriptContractTests.C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune` | `helper-refused-foreign-main` | Pending (all variants) |
| PC-141 | Break G-141: Require empty packages on marker reuse | `RemoteScriptContractTests.C912_Cold_marker_has_distinct_validation_and_full_context_refusal` | `marker-reuse-no-probe-or-restore` | Pending (all variants) |
| PC-142 | Break G-142: Permit foreign-cache as packages volume | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `name exit=2` | Pending (all variants) |
| PC-143 | Break G-143: Drop local driver predicate | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `driver exit=2` | Pending (all variants) |
| PC-144 | Break G-144: Drop options predicate | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `options exit=2` | Pending (all variants) |
| PC-145 | Break G-145: Drop owner label predicate | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `owner exit=2` | Pending (all variants) |
| PC-146 | Break G-146: Drop schema label predicate | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `schema exit=2` | Pending (all variants) |
| PC-147 | Break G-147: Drop actual_role comparison | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `role exit=2` | Pending (all variants) |
| PC-148 | Break G-148: Accept root-owned 0:0:700 in stat comparison | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `uid-owner-refused` | Pending (all variants) |
| PC-149 | Break G-149: Accept 1654:1654:755 in stat comparison | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `mode exit=2` | Pending (all variants) |
| PC-150 | Break G-150: Bypass root test ! -L and test -d | `RemoteScriptContractTests.C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | `symlink exit=2` | Pending (all variants) |
| PC-151 | Break G-151: Recursively chmod payload files during prepare | `RemoteScriptContractTests.C849_Cache_prepare_is_idempotent_and_preserves_payloads` | `preserved antiphon-runner-cache-nuget-packages` | Pending (all variants) |
| PC-152 | Break G-152: Skip CacheUnmarkedInUse guard | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `unmarked-consumer-refused` | Pending (all variants) |
| PC-153 | Break G-153: Accept wrong scratch volume in mount receipt | `RemoteScriptContractTests.C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims` | `wrong-mount-refused` | Pending (all variants) |
| PC-154 | Break G-154: Ignore actual /tmp mode in c849_assert_mounts | `RemoteScriptContractTests.C913_Fixture_uses_owned_payloads_and_proven_absent_temp` | `tmp-mode-refused` | Pending (all variants) |
| PC-155 | Break G-155: Remove dot-dot segment rejection in Resolve-Entry so packages/../../escape is mapped | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | `nested-traversal-refused` | Pending (all variants) |
| PC-156 | Break G-156: Ignore seen.Add returning false | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | `duplicate-archive-refused` | Pending (all variants) |
| PC-157 | Break G-157: Permit SymbolicLink in Read-Tar kind admission | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | `symlink code=2 diagnosis=CacheDonorUnsafeEntry` | Pending (all variants) |
| PC-158 | Break G-158: Silently continue past FIFO in Read-Directory instead of refusing, avoiding a blocking FIFO open | `RemoteScriptContractTests.C849_Saved_donor_rejects_unsafe_archives_empty_payload_and_busy_counters` | `directory-fifo-refused` | Pending (all variants) |
| PC-159 | Break G-159: Remove packages budget comparison in Check-Entry | `RemoteScriptContractTests.C849_Saved_donor_rejects_declared_size_bomb_before_writing` | `size-bomb code=2 diagnosis=CacheBudgetExceeded` | Pending (all variants) |
| PC-160 | Break G-160: Remove the -band 511 mask in Write-Entry | `RemoteScriptContractTests.C913_Seed_accepts_complete_ordinary_packages_without_framework_packs` | `unsafe-mode-masked` | Pending (all variants) |
| PC-161 | Break G-161: Call c849_prepare from c849_preview | `RemoteScriptContractTests.C849_Prune_preview_is_read_only_and_bounded` | `preview-read-only` | Pending (all variants) |
| PC-162 | Break G-162: Change budget OVER threshold from >= to > | `RemoteScriptContractTests.C849_Prune_preview_is_read_only_and_bounded` | `100=OVER` | Pending (all variants) |
| PC-163 | Break G-163: Remove sudo -n from observed Docker root realpath | `RemoteScriptContractTests.C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo` | `OBSERVE_EXIT=0` | Pending (all variants) |
| PC-164 | Break G-164: Remove host-lane refusal before cache case dispatch | `RemoteScriptContractTests.C849_Cache_cases_use_only_the_validated_host_lane` | `wrong-lane-no-effect` | Pending (all variants) |
| PC-165 | Break G-165: Move prepare after seed_runner_checkout in case_deploy_temp_runner | `RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance` | `deploy-prepare-before-checkout` | Pending (all variants) |

| Additional PC-3 variant | Exact method | Expected witness | Status |
|---|---|---|---|
| Remove only chmod0755 for host apphost | CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing | host-pack-public-executable | Pending |
| Remove only --no-same-owner | CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing | host-pack-image-owner | Pending |

The original PC-3 missing-host-member mutant must also remove that member's new chmod, along with its post-extract test, to remain a valid executable archive chain. This is a design amendment for Mutation, not an executed mutant.

## Ordinary invariant status at the recorded tested SHAs

These are prior committed-source outcomes, not acceptance of the later unverified test amendment. Re-run CP-3/4 and full affected classes after that amendment before Review.

| ID | Actual outcome | Evidence |
|---|---|---|
| V-1 | PASS | T7 CP-4 3/3 group and real CP-6 11/11 at a059af5ecd40d7a8b500806fd775a2a8f868aee0; native uid1654, SDK10.0.401, three exact9.0.20 packs, networknone, empty mounted pair, restore/build/native exits0 and exact token |
| V-2 | PASS Linux | T1 in CP-3 47/47 at0cdfb5cd9f9658f7279837924ac5f19f39af762b: ordinary packages, saved directory/tar, unsafe/incomplete inputs |
| V-3 | PASS Linux | T2 same CP-3: independent golden manifest, package/npm recipient comparison, handoff faults and19 owned-child crash barriers, saved/live donor acceptance |
| V-4 | PASS Linux | T3 same CP-3: schema3/full legacy/cold markers, strict records, immutable recovery mutations, mutable cache churn, no format rewrite |
| V-5 | PASS Linux | T4 same CP-3: destructive temp-tree sentinels, authority/age/headroom/budget exact boundaries, ordinary refill, native-smoke receipts, admission held |
| V-6 | PASS Linux | T5 same CP-3: synthetic owned Fixture, exact diagnoses, optional temp only on proved retirement after census, no production payload reads |
| V-7 | Linux PASS; Windows PENDING | T6 CP-3 passed. CP-7 native Windows not run; inaccessible from assigned mirror, caller-commissioned lane requested |
| V-8 | PASS | T8/T9 CP-4 at a059af5ecd40d7a8b500806fd775a2a8f868aee0; actual checked-out probe/wrapper branches executed with process-boundary shims |
| R-1 | Linux PASS; Windows companion PENDING | CP-3 all18 C849 and9 C912 passed; CP-7 front-door/cold verification companion not run |
| R-2 | Linux PASS; Windows PENDING | CP-5 at0cdfb5cd9f9658f7279837924ac5f19f39af762b:24groups/66invocations/227assertions, zero failures/skips; CP-8 native Windows+WSL jq not run |
| R-3 | Linux PASS; Windows rolling companion PENDING | CP-3 all12 C973 results passed; CP-5 includes realjq retirement/coldmarker cases; CP-8 not run |
| R-4 | PASS Linux | CP-3 both C905 results passed |

Q-1 real operational Fixture and rollout/canary/Both/Retired gates: PENDING caller after Review, explicitly outside ordinary Code rows. No live Seed/Prune/Reset, restart or deployment performed. Restart=none; activation owner=caller.

## Preliminary history guard

Read-only scripts/check-evidence-diff.ps1 over complete task base..a059af5ecd40d7a8b500806fd775a2a8f868aee0, exit0:

EVIDENCE range base=7f489f066c69caa2ee1380361e8d0135dd451db9 head=a059af5ecd40d7a8b500806fd775a2a8f868aee0
EVIDENCE result commits=18 entries=0 violations=0 base=7f489f066c69caa2ee1380361e8d0135dd451db9 head=a059af5ecd40d7a8b500806fd775a2a8f868aee0

A final full-range guard follows the report/documentation commit; its exact result is delivered in the final message because a commit cannot contain its own SHA. Generated checkpoint/TRX/JSON/log/archive payloads remain ignored.

## 20261004-155350-dbb2: Final Unit interrupted by task ownership ending

Actual source a059af5ecd40d7a8b500806fd775a2a8f868aee0 stayed clean. The one build completed in118.48s. Four completed namespace TRX files contain787 passed,0 failed,2 skipped (789 result records). Fresh XML rosters inspected: root23/23; Infrastructure575 passed+2 Windows-only skips; AgentTui143/143; Resilience46/46. Full CodexRunnerImageContractTests was observed13/13. Scripts ran27m16s before owner-ended and has NO TRX; namespaces6..12 were not run. No full Unit or full RemoteScript pass is claimed.

Receipt validation of even completed rows1,3,4 exits2 with row_heading_disagreement because the interrupted run heading has buildSource=unknown. Their counts are diagnostic observations, not clean acceptance certificates. All actual acquired slots were granted with waited=0s; CP-Unit-5 lease68226c20-e87a-43d3-b91c-eb3b6e3a7dc0 was released. The tool's interrupted-row CHECKPOINT lines below remain unedited, including skipped/unknown fields.

CHECKPOINT CP-Unit-1 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=ok filter=/*/Antiphon.Tests/*/*[Category=Unit] executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-155350-dbb2/rows/CP-Unit-1/run.trx slot=granted waited=0s dirty=0 source=a059af5ecd40d7a8b500806fd775a2a8f868aee0 sourceState=clean buildSource=verified
CHECKPOINT CP-Unit-2 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=reused filter=/*/Antiphon.Tests.Infrastructure/*/*[Category=Unit] executed=575 passed=575 failed=0 skipped=2 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-155350-dbb2/rows/CP-Unit-2/run.trx slot=granted waited=0s dirty=0 source=a059af5ecd40d7a8b500806fd775a2a8f868aee0 sourceState=clean buildSource=verified
CHECKPOINT CP-Unit-3 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=reused filter=/*/Antiphon.Tests.AgentTui/*/*[Category=Unit] executed=143 passed=143 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-155350-dbb2/rows/CP-Unit-3/run.trx slot=granted waited=0s dirty=0 source=a059af5ecd40d7a8b500806fd775a2a8f868aee0 sourceState=clean buildSource=verified
CHECKPOINT CP-Unit-4 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=reused filter=/*/Antiphon.Tests.Infrastructure.Resilience/*/*[Category=Unit] executed=46 passed=46 failed=0 skipped=0 trx=/work/worktrees/task-62fc2d31/.antiphon/checkpoints/20261004-155350-dbb2/rows/CP-Unit-4/run.trx slot=granted waited=0s dirty=0 source=a059af5ecd40d7a8b500806fd775a2a8f868aee0 sourceState=clean buildSource=verified
CHECKPOINT CP-Unit-5 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=/*/Antiphon.Tests.Scripts/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-Unit-6 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=/*/Antiphon.Tests.TestHelpers/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-Unit-7 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=/*/Antiphon.Tests.ApiKeys/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-Unit-8 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=/*/Antiphon.Tests.Domain.StateMachine/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-Unit-9 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=/*/Antiphon.Tests.Agents/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-Unit-10 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=/*/Antiphon.Tests.Migrations/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-Unit-11 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=/*/Antiphon.Tests.Checkpoints/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-Unit-12 commit=a059af5ecd40d7a8b500806fd775a2a8f868aee0 build=n/a filter=/*/Antiphon.Tests.Application/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown

Unexecuted Windows-only methods observed in Infrastructure:

- LandingRemovalPolicyControlTests.C665_LockedFileMidDeleteResumesOnLaterPass
- LandingRemovalPolicyControlTests.C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded

Both explicitly Skip.Test on non-Windows (sharing-mode file locks). They are not passed or blamed on this change.

Server task detail returned failureReason: Ran4h00m against the240-minute ceiling for roleCode; taskFailed, not escalated/retried; session not killed. Owner PID22567 and inspected test descendants3466,3657,3939,3048,3351,3410 were gone after tool exit. Earlier owner reads had one502 and two12s timeouts; final reason was the explicit task ceiling, not an asserted test failure.

## Unverified test amendment and continuation

Commit625b0df66ed080f043b68ecfd2888d0f0edf715b (pushed) adds canonical failure witnesses to actual existing predicates, SDK10/pin/member assertion names, binds the maintenance fixture to the required C849 method, and adds a Reset attachment appearing on the second proof with all three payload sentinels checked. No assertion or timeout was loosened. bash-n, PowerShell AST parsing and git-diff-check passed; compilation and execution are PENDING because checkpoint ownership ended. No deliberate CARD-0849 mutant was used.

The late label audit initially found107 nonliteral witness strings; many are generated by existing loops (warm caches, symlink/hardlink, cold proof phases, volume roles and helper variants). The amendment aligns the other diagnostic names. Continuation must finish a method-local witness audit, including the exact wrong-lane/preview contracts, and verify the amendment; no complete165-control sensitivity claim is made. Mutation still owns deliberate red/restore/green and missing-control discovery.

Required Code continuation under the caller refinement, keeping the original Code task as landing owner:

1. Restore active checkpoint ownership or commission a fresh verification task against this pushed branch. Do not unset task identity, alter the owner guard, or retry through an unguarded test driver. The task remains Failed while its session remains Running; the latter does not authorize a checkpoint under the tool contract.
2. Compile/verify the latest test amendment with the exact CP-3 and CP-4 rows and the explicitly requested C944/C957 repair filter. Prefix PATH with /home/app/.local/bin for jq. The ignored .antiphon/c913-final-repair.yaml imports the eight-row manifest and adds CP-C944-Repair with four expected results. Inspect fresh TRX for all47+3+4 results and no skips; rerun red rows, comparing any other failure with a focused clean-base execution. No whole-Unit, namespace or full-class rerun: the latest caller refinement supersedes those earlier continuation requests.
3. Caller commissions native Windows CP-7 (3 exact results) and CP-8 (24 groups/66 invocations/227 assertions, native pwsh plus real WSL jq). The assigned desktop checkout is explicitly unreachable from this mirror. Prior lane requests remain unresolved; no Linux substitute is credited as Windows qualification.
4. The other permitted rows are CP-5 and CP-6. CP-5 is qualified at 0cdfb5cd9f9658f7279837924ac5f19f39af762b; CP-6 is qualified at a059af5ecd40d7a8b500806fd775a2a8f868aee0. A fresh run under active ownership can qualify them at the latest SHA. Use a fresh image tag/results root and the full committed source SHA for CP-6. CP-1/2 retain the completed intentional S0 assertion-red evidence; they are not acceptance rows to recreate with deliberate mutants.
5. Once the amended ordinary verification is complete, next Review. Caller lands the original Code task after Review and commissions SourceLanding Mutation. Q-1 remains post-Review operational work; all165 PC IDs/all variants, including both additional PC-3 extraction variants, remain pending.

The original Final profile is amended by the caller's explicit narrow verification instruction, not converted to Interim. No omitted Unit/full-class execution is marked passed. Outstanding ordinary work: latest-amendment CP-3/4 and C944/C957 verification, native Windows CP-7/8, plus the requested plan-row reruns under active ownership. V-7/R-1/R-2/R-3 Windows companions and revalidation of the amended tests remain incomplete. The earlier Unit timeouts/interruption are historical diagnostic evidence only.

Restart: none performed. Runner image activation and any eventual restart belong to the caller. No server/runner service was restarted, deployed, or landed.

## Implemented source changes

- docker/session-runner-grok/Dockerfile and verify-codex-image.sh: preserve pinned SDK9 archive and SDK10 policy; normalize image-pack ownership/apphost0755; prove native net9 plus ASP.NET references with fresh mounted caches, private home and networknone.
- scripts/verify-card0660-codex-image.ps1: fresh image/results gates, full clean SHA, immutable image identity, owned cache initialization/mount isolation, strict row/exit grading and partial-create cleanup.
- scripts/c590-remote.sh: generic ordinary package import, deterministic recovery manifest and imported-byte comparison, schema3 full readiness/publication/recovery, strict typed receipts, Prune authority/recovery checks and ordinary refill, donor-independent owned Fixture and proven optional-temp inventory. Legacy full markers and CARD-0912 cold schema2 remain distinct.
- scripts/verify-card0849-caches.ps1: strict receipt shapes/privacy, compatible Both tuples, smoke evidence and exact Fixture roster; docs/docker-stack.md documents the contracts.
- scripts/fixtures/c913-* and both named test classes: executable filesystem/process-boundary cases, independent manifest oracle,19 owned-child crash barriers and restrictive archive mode regression. c973-marker-reader fixture follows real legacy readiness. Latest witness/maintenance/Reset additions are unverified as stated above.

## Closed checkpoint status

| ID | Actual outcome |
|---|---|
| CP-1 | Six intended assertion failures, no skips, at173598c78b124adb56195b19be521786f88e712d; regression red proof |
| CP-2 | Three intended assertion failures, no skips, same SHA; regression red proof |
| CP-3 | Qualified47/47 at0cdfb5cd9f9658f7279837924ac5f19f39af762b; re-run required after unverified test amendment |
| CP-4 | Qualified3/3 at a059af5ecd40d7a8b500806fd775a2a8f868aee0; re-run required after unverified test amendment |
| CP-5 | Qualified24groups/66invocations/227assertions at 0cdfb5cd9f9658f7279837924ac5f19f39af762b |
| CP-6 | Qualified11/11 real image at a059af5ecd40d7a8b500806fd775a2a8f868aee0 after observed permission failure and repair |
| CP-7 | NOT RUN: native Windows lane unavailable here |
| CP-8 | NOT RUN: native Windows+WSLjq lane unavailable here |

All complete qualification runs used the slot gate; slot=granted waited=0s. No unleased build or timeout widening. Extra runs were declared for checkpoint bootstrap, required Final Unit, the introduced C944/C957 baseline/repair, actual archive-mode regression at base, and discovery/help; discovery counts never credited as executions. Unchanged selections stayed within normal/loaded repetition budgets; no Review-authorized flake budget extension was used.

## Cleanup and custody

All owned checkpoint/command runs have ended. Removed79 owned bin-c913-* directories (26 detached-base outputs,52 earlier/partial main test outputs,1 checkpoint-tool bootstrap), then removed the clean detached diagnostic worktree. Its generated evidence was copied to ignored `.antiphon/c913-baseline-evidence/checkpoints/`; the original CHECKPOINT lines and payload bytes were not edited. Main checkpoint/image evidence remains in ignored `.antiphon/checkpoints/` and `.antiphon/c913-image-*`. The qualified image tag remains available locally; its volumes are gone. No foreign process, worktree, image or volume was removed.

Only this individual Markdown report (below1MiB) and the verification-design amendment are committed in the final report slice. No TRX, JSON, logs, archives, generated YAML or evidence directories are added. Worktree/branch remain `/work/worktrees/task-62fc2d31` and `feat/card-task-62fc2d31`; original Code/landing owner remains `62fc2d31-6179-40c8-85b0-d216cbe83744`.

## Caller refinement and ended-owner refusal (2026-10-04 16:38 UTC onward)

Read .antiphon/inbox/887c4b85-a24a-4f5a-af2d-c78ed7370510.md in full. It prohibits repeating whole Unit after the 40-minute no-TRX timeout (also observed by caller on CARD-0892/CARD-1030), permits only C944/C957 and plan checkpoint rows, requires comparison of other failures to known base failures, and sets a 45-minute wrap-up budget. No broader test selection was launched after this refinement.

GET /api/runner-defaults returned revision2 with no unresolved references; GET /api/session-runners reported eligible Linux and Windows hosts, temp unavailable. No runner was pinned. GET /api/agent-tasks/62fc2d31-6179-40c8-85b0-d216cbe83744 still returned summary.status=Failed, completedAt=2026-10-04T16:27:37.062068Z, while the session was Running. The failure reason remained the four-hour Code ceiling. An asynchronous request asked the caller to restore active ownership or commission remaining verification, and reiterated native Windows CP-7/8.

A declared checkpoint-tool bootstrap was needed because the earlier owned output cleanup had removed its executable. Command: pwsh -NoProfile -File scripts/build-slot.ps1 -Label c913-checkpoint-bootstrap-refinement -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c913-tool/ --nologo. Exit0,0errors,1 unchanged nullable warning, build14.82s. This compiles only the checkpoint tool; it does not verify the uncompiled test amendment.

BUILD SLOT granted lease=71e3c5b2-4a29-40c6-8b7f-2967c61e9013 waited=15s maxcpucount=6
BUILD SLOT released lease=71e3c5b2-4a29-40c6-8b7f-2967c61e9013 held=16s

Focused invocation: PATH=/home/app/.local/bin:$PATH dotnet tools/Antiphon.Checkpoints/bin-c913-tool/Antiphon.Checkpoints.dll run .antiphon/c913-final-repair.yaml --rows CP-3,CP-4,CP-C944-Repair --expected-source-sha 196d4c6e05189e42c2a1a75fa8f9c77d5980e1be --max-wait 45s. Actual exit7; no run directory, row, build slot or TRX was created. This is the complete unedited output:

CHECKPOINT owner owner-ended

The refusal is not a test failure or baseline comparison. C944/C957's existing clean-base4/4 and fixed-source4/4 evidence remains above; there is no fresh test failure to classify. No ownership identity was removed and no driver bypassed the guard. The tool bootstrap output was cleaned again after exit. All foreground commands were awaited, and no checkpoint executor remains. Final full-task evidence-diff guard and pushed SHA are reported in the caller summary.
