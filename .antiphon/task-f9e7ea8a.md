# CARD-1105 S3 code report

Outcome: S3 (refusal battery and temp previous-generation cleanup) is green. The closed checkpoint `20261007-011208-0697` passed 6/6 rows on `92f870eb899b28c0fd2791a700699a85362ea303`. A following repair, `88deeea7ebfdeeee8f4feac5cab62de25a9d87b6`, aligns the script-lane bridge host to the target SHA. `RetiredTempContainerScriptTests` then passed 13/13. S4 is not done. Redeploy-old must not run until S4 lands.

Landing owner / original Code task: f9e7ea8a. Branch: `feat/card-task-f9e7ea8a`. Worktree: `/work/worktrees/task-f9e7ea8a`. Task base: `f8d3ba0f245f27addb1d271532847c008987ce33` (fast-forward only; not rebased). Restart: none.

## What landed

`scripts/c590-remote.sh`

- `c1008_previous_generation` allows `image_count==0` only for `TEMP_PROJECT` (null image tag and id, `C1008_PREVIOUS_SHA` set). Main still requires `image_count==1`.
- `c1008_bind_generation` no longer runs the vacuous `runners=0` jq. `runners` stays in the local list. Stack SHA must still equal `saved_sha`. Leftovers stay pinned by `c1008_reconcile_owned`.
- `case_retire_temp_containers` derives the previous generation only when the project owns a session-runner or state-init, after the exited-state check and before the volume loop. Empty owned does not derive one. The target `c1008_compose_model` still runs first. A mismatch refuses through `c994_refuse` and does not `docker rm`.

Tests and plan

- `C1008HostFixture.Run` deadline is 120s. The wrapper deadline stays 30s. A deploy-parent Run measures 22–30s and exceeded 30s at load 19–23 (method wall 53.5s; parent `8b8436a17` baseline 53.03s; standalone at load 6–11 passed in 47s). 120s still cancels a hang inside the 20-minute row timeout. Under CP-3 the redeploy method passed in 00:00:57.6.
- `C1105_Generation_identity_refuses_mismatch` (V-3): six refusal classes, including the edited-journal `previousSha` case. CP-3 duration 00:00:30.8.
- `C1105_Temp_cleanup_uses_previous_generation` (V-6): previous temp model, 14 mounts, no `/run/antiphon/github-token`; a different `SOURCE_REVISION` refuses `RecycleGenerationMismatch` with no mutation.
- Host tests that build a current-generation session-runner or state-init stamp target SHA `a`*40 (`AlignSameGeneration`) so the current 15-mount roster matches. The constructor default remains the previous SHA.
- CP-10 covers `C1105_Compose_cmp_refuses_before_removal`, `C1105_Resume_identity_refusals`, `C1105_Resume_state_init_image_follows_the_journal`, and `C1105_Resume_foreign_leftover_is_not_an_image_mismatch`. CP-7 Expect and Min are 16. PC-12, PC-13, and PC-14 are recorded and pending.
- Repair: `C994_Bridge_transport_preserves_literal_context` constructs `C1008HostFixture(main:false)` directly and adds a current-generation session-runner. The fixture was still stamping the previous SHA, so cleanup refused `RecycleContainerStateUnknown` (`jq: mount count`, exit 2, removed=0). The test now stamps `server2-temp` `buildVersion` and `temp.env` `SOURCE_REVISION` to `a`*40 before `retire-temp-containers`. The exit-0 assertion is unchanged. `c590-remote.sh` was not edited for this repair. The other script host, which runs `retire-temp-runner` and expects `RunnerCounterUnknown`, was left alone.

## Checkpoints

Run `20261007-011208-0697`. Source `92f870eb899b28c0fd2791a700699a85362ea303` state=clean buildSource=verified. `--after S3 --serial --keep-outputs --row-timeout 20m --total-timeout 120m`. One isolated test build (CP-3, 117s). CP-4, CP-5, CP-6, CP-7, and CP-10 reused it. Tool unlisted: none. Wall 60m45s. Sequential-equivalent 58m46s. Builds 4. Max-concurrent-builds 1. Rows 6 green, 0 red, 0 skipped. Verdict GREEN exit=0. Evidence: `.antiphon/checkpoints/20261007-011208-0697/report.md`. Every row slot=granted waited=0s dirty=0.

CP-7 host wall was 19m32s, past its 14-minute estimate and inside the 20-minute row timeout. The timeout was not widened.

CHECKPOINT CP-3 commit=92f870eb899b28c0fd2791a700699a85362ea303 build=ok filter=/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)|(C1105_Generation_identity_refuses_mismatch*)|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)|(C1008_Recycle_exact_default_volumes*)|(C1008_Recycle_dry_run_never_mutates*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f9e7ea8a/.antiphon/checkpoints/20261007-011208-0697/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=92f870eb899b28c0fd2791a700699a85362ea303 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=92f870eb899b28c0fd2791a700699a85362ea303 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)|(C1008_Recycle_receipt_records_disk_and_partial_failure*)|(C1008_Recycle_refuses_references_and_unknown_census*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f9e7ea8a/.antiphon/checkpoints/20261007-011208-0697/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=92f870eb899b28c0fd2791a700699a85362ea303 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=92f870eb899b28c0fd2791a700699a85362ea303 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1087_Host_census_filters_and_names_cause*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_audits_work_as_1654*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f9e7ea8a/.antiphon/checkpoints/20261007-011208-0697/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=92f870eb899b28c0fd2791a700699a85362ea303 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=92f870eb899b28c0fd2791a700699a85362ea303 build=reused filter=/*/*/RetiredTempContainerHostTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-f9e7ea8a/.antiphon/checkpoints/20261007-011208-0697/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=92f870eb899b28c0fd2791a700699a85362ea303 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=92f870eb899b28c0fd2791a700699a85362ea303 build=reused filter=/*/*/RollingVolumeRecycleScriptTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-f9e7ea8a/.antiphon/checkpoints/20261007-011208-0697/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=92f870eb899b28c0fd2791a700699a85362ea303 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=92f870eb899b28c0fd2791a700699a85362ea303 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)|(C1105_Resume_identity_refusals*)|(C1105_Resume_state_init_image_follows_the_journal*)|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f9e7ea8a/.antiphon/checkpoints/20261007-011208-0697/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=92f870eb899b28c0fd2791a700699a85362ea303 sourceState=clean buildSource=verified

CP-8 and CP-9 are S4 and were not run. The whole Unit lane was not run; the brief's closed list wins over the Final profile.

## Legacy classes (brief-required, outside or overlapping the S3 rows)

Cited from the checkpoint where the class actually executed:

- `RetiredTempContainerHostTests`: CP-6, 21 passed / 0 failed. V-6 and R-5.
- `RollingVolumeRecycleScriptTests`: CP-7, 16 passed / 0 failed. R-6.
- Remote `C1008_` / `C1087_` / `C1105_` in `RemoteScriptContractTests`: CP-3 (5) + CP-4 (3) + CP-5 (7) + CP-10 (4) = 19 passed / 0 failed. V-2, V-3, V-4, R-4.

Serial `--no-build` extras against `bin-c1105-c` at `92f870eb`, `TUNIT_MAX_PARALLEL_TESTS=1`, each slot=granted waited=0s:

- `RollingProductionMountTests`: 6 passed / 0 failed. Lease `9b830a84`, held=72s. V-1 and R-2.
- `HostJqPrerequisiteScriptTests`: 39 passed / 0 failed. Lease `a11b5f33`, held=428s.
- Compose text pins (five `RemoteScriptContractTests` deploy-parent pins): 5 passed / 0 failed. Lease `109e6488`, held=4s. Reconfirms R-3. V-5 / R-3 already passed on S2 CP-2.
- Registry guard (`TestClassificationGuardTests` and `SlowTestTripwireTests`): 3 passed / 0 failed. Lease `57eeca36`, held=6s.
- `RetiredTempContainerScriptTests` on that SHA: 12 passed / 1 failed. Lease `5fe15a87`, held=198s, build-slot exit 2. The failure was `C994_Bridge_transport_preserves_literal_context` (exit 2, `RecycleContainerStateUnknown`, mount count).

Repair, verified on `88deeea7ebfdeeee8f4feac5cab62de25a9d87b6`:

- Rebuild `bin-c1105-e/`, UseAppHost=false. Slot=granted lease=`327aaa6a-a3f2-42c1-85c4-f343f1060674` waited=0s held=149s. 0 errors.
- `/*/*/RetiredTempContainerScriptTests/*`, serial. Slot=granted lease=`4d969707-d1cc-4046-8842-ae7d6e89bf1d` waited=0s held=176s. Total 13, failed 0, succeeded 13, skipped 0, duration 2m 52s. The bridge method passed in 00:00:10.0. TRX `/tmp/c1105-repair/run.trx`.

The S3 checkpoint was not re-run. The repair does not change production script behavior.

Explained unlisted builds: checkpoint-tool bootstrap `bin-c1105-tool` (lease `3b75c27d`, waited=0s, held=5s); pre-commit smoke build (lease `526d4581`, waited=0s, held=122s) plus the method smokes before `92f870eb` (host filter 4/4, lease `82475579`, held=265s; V-3 1/1, lease `4909089d`, held=28s). The repair rebuild above is the build for the script-class rerun.

## Negative controls

Six Code negative controls, method-scoped, each went red (exit 2, total 1, failed 1) and were restored before `92f870eb`. `bash -n scripts/c590-remote.sh` was clean after restore. These are not the Mutation PCs.

- V-6 target model replaced with `:` — previous-generation cleanup no longer matched.
- Status SHA compare replaced with `:` — V-3 no longer refused.
- Image-id compare replaced with `:` — `image-inspect-wrong` no longer refused.
- Previous-model `c1008_compose_source` token changed from `RecycleGenerationUnknown` to `RecycleComposeMismatch` — V-3 went red. The target `c1008_compose_model` line was not touched.
- Mount-count clause replaced so an extra bind is ignored — the foreign-mount case went red.
- Saved-versus-derived `previousSha` compare replaced with `:` — the edited journal was no longer `RecycleResumeMismatch`.

## V and R

- V-1 passed. `RollingProductionMountTests` 6/6, including `C1105_Previous_generation_roster_is_derived_from_its_compose`.
- V-2 passed. CP-3 redeploy method, duration 00:00:57.6, exit recorded by the row (5/5).
- V-3 passed. CP-3, duration 00:00:30.8. Six refusal classes.
- V-4 passed. CP-3 dry-run method.
- V-5 passed on S2 CP-2 and the five compose text pins passed again here (5/5).
- V-6 passed. CP-6.
- V-7 not this slice. S4 real Docker.
- R-1 not this slice. S4 docs pin.
- R-2 passed. Five `RollingProductionMountTests.C994_*` methods inside the 6/6 class run.
- R-3 passed. Compose text pins 5/5, and the S2 order pin remains.
- R-4 passed. CP-3 + CP-4 + CP-5 + CP-10.
- R-5 passed. CP-6, 21/21 (20 `C994_*` plus the new V-6 method).
- R-6 passed. CP-7, 16/16.
- R-7 not this slice. S4 existing real-Docker outcomes.

## PCs pending

PC-1 through PC-14 stay pending for method-scoped SourceLanding Mutation. None were run as Mutation cycles. PC-12 deletes the early `cmp` block. PC-13 restores the `runners=0` image-equality refusal. PC-14 replaces the session-runner image compare with `true`.

## Evidence

`scripts/check-evidence-diff.ps1 -BaseRef f8d3ba0f245f27addb1d271532847c008987ce33 -HeadRef 88deeea7ebfdeeee8f4feac5cab62de25a9d87b6`

EVIDENCE result commits=2 entries=0 violations=0

This report file is a later docs commit. The caller summary carries the re-run over base..HEAD.

## Platform

`GET http://127.0.0.1:17202/api/runner-defaults` and `GET http://127.0.0.1:17202/api/session-runners` both failed: curl exit 7, connection refused. This slice does not dispatch. Production server2 was not contacted. No deploy phase was run.

## S4 before redeploy-old

Redeploy-old must not run until S4 proves all of the following:

1. Real nested Docker `C-previous-generation-main`: a main container from the previous 10-mount model is reclaimed and the replacement mounts `/run/antiphon/github-token` read-only.
2. `C-previous-generation-foreign-mount`: an extra bind is refused before stop, and volumes are retained.
3. `scripts/fixtures/c1008-recycle-real-cases.mjs` copies the file `$SERVER2_COMPOSE` names. Today `C1008_Real_docker_comparison` fails `ExpectedSuccess:C-default:RecycleComposeMismatch` because the shim serves `<root>/<project>.json` regardless of `-f` and `SERVER2_COMPOSE` is unset. The count line must become `cases=34 base=5 changed=29 failures=0`.
4. `docs/docker-stack.md` CARD-1105 section and `Mount_generation_protocol_is_documented` (R-1): both refusal codes, `SOURCE_REVISION`, `C1008_GENERATION`, the add/remove protocol, and the gate 6 sentence.

The live rollout is operator work after land. The card stays open until redeploy-old and retire-temp receipts exist.

## Commits

- `92f870eb899b28c0fd2791a700699a85362ea303` fix(CARD-1105): prove temp cleanup against its own generation
- `88deeea7ebfdeeee8f4feac5cab62de25a9d87b6` fix(CARD-1105): align script-lane temp cleanup to its generation

Range stat: 6 files, +154 / −20.

--- next stage ---
next: review
handoff: CARD-1105 S3 is green: refusal battery, temp previous-generation cleanup, Run deadline 120s (redeploy 57.6s), CP-10 added, vacuous runners=0 jq removed, CP-7 is 16, script-lane bridge aligned to the target SHA. PC-1..PC-14 pending. S4 must prove real Docker reclaim, foreign-mount refusal, SERVER2_COMPOSE copy (cases=34 failures=0), and the docker-stack.md pin before redeploy-old.
artifact: docs/superpowers/plans/2026-10-06-card-1105-compose-mount-generation-plan.md
