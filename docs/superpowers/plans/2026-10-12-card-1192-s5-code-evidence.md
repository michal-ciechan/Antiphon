# CARD-1192 S5 Code evidence (task f36e645d)

S5 selects case-owned work-volume storage for F-3 donor, staged copies and schema-3 recovery. F-8 recovery setup/observations and F-9 mutation/restore callers use that payload backend. Ordinary real Seed/saved/cold cases retain the host default. Host control markers, previews, receipts and ledgers remain host files. S6/S7 are unimplemented by this task. Restart: none.

Original Code task / landing owner: f36e645d-7b79-49bc-a6ba-42ac792f7865 (prompt ID f36e645d).
Branch: feat/card-task-f36e645d. Worktree: /work/worktrees/task-f36e645d.
Task base: a61db0f08448b87421b8dd9334f9f2631691cde7.
Final implementation/test source: 9564ab3ad84f087389d848f02a0c53eb2b62c865.
Plan: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md.

## Changes and scope

- scripts/c590-remote.sh: positional payload filesystem/function dispatch; writer validation for pruning-capable tree validator; streamed donor transfers under pipefail; named work mounts for npm verification/import/comparison; payload recovery checks and failure cleanup; F-3 activation/readback/rerun; F-8 recovery creation from the existing admitted package volume; F-9 volume mutation/backups/restoration. Volume mode refuses saved-donor redirect and non-schema-3 markers rather than handing logical paths to host consumers.
- scripts/fixtures/c1192-workspace-contract.sh: non-root readonly-directory model and incomplete-package reader/writer case. Reader removal is denied by filesystem permissions; the original package must survive. Writer acceptance must prune the staged incomplete package and publish its acceptance marker.
- scripts/fixtures/c913-seed-contract.sh: shared Seed recipient/fault matrix, actual F-3 wrapper with donor tar/process-state model, actual F-9 corruption/restoration loop, and shared Prune matrix with volume recovery. The wrapper models npm/smoke tools whose existing S3/earlier tests own those boundaries. Unrelated fixture warm/inventory/cleanup controls retain their existing checkpoint owners; they are not credited as proof here.
- tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs: CP-21/22 methods; adapt C1066 extraction to the new seam and C1195 positional-source import while preserving the same exact root-mode/recipient assertions.
- Plan: S5 proof disclosure and G-140/PC-140 and G-141/PC-141 pending Mutation; updated costs. All six After cells already were S5; no selector repair needed.

Final round is the explicitly narrowed S5 house-rule selection CP-21..26. The brief explicitly says no broad Unit lane and excludes other CP rows; therefore this report does not claim the generic whole-Unit/whole-class Final profile. No full-assembly run or live Fixture/Seed/rollout was made. Required live W-1 remains rollout-owned, after S7. No timeout or assertion was loosened.

## Baseline red and guarded defects

Tests were committed/pushed before production changes at ffbd61599a57b635e6b4ca1c69c4990a3eb25faf, whose production script is identical to S4 base.
Bound checkpoint run 20261012-193652-5054 executed CP-21=1 failed and CP-22=1 failed (zero skipped), sourceState=clean/buildSource=verified. CP-21 output: `FAIL fixture-seed-volume-accepted [case=fixture-seed-volume-accepted]`; CP-22 output: `FAIL volume-recovery-valid [case=volume-recovery-valid] RESULT false CacheRecoveryInvalid`.

The separately gated pre-change writable-pruning recipient reported `reader-cannot-prune=1` and `writer-prunes-incomplete-stage=0`, with permission-denied removal and CacheDonorVersionIncomplete. Thus changing transfer validation from reader to writer repairs a witnessed failure. CP-21 asserts both facts equal 1 after the fix. The shell model emits facts and exited 0; the zero writer fact is the guarded red observation, not a falsely reported shell failure.

Case -> mutation -> red: selecting host for fixture recovery rejects/misplaces the volume recipient; skipping reuse loses the cache sentinel; skipping unsafe-tree/integrity/import/smoke/save/restart/reconnect/publication guards fails exact diagnosis/no-marker or independent order/recipient assertions; using reader for pruning yields writer-prunes-incomplete-stage=0. Recovery validation bypasses accept modified package/npm bytes, executable bits, digest or missing/linked/escaped trees. The real F-9 loop is followed by independent bytes/mode/manifest checks, so missing restoration cannot self-compare green. Deliberate mutation cycles were not run by Code.

## Checkpoint history

Run 20261012-194411-8a19 at 5b22a6467fde483942f393c556bcf69e00b4ffba passed all six rows (11 executions, zero failed/skipped). Source was then changed to strengthen actual F-3/F-9 and volume Prune coverage; the final run below supersedes that source for Review. No unchanged green proof was repeated. Every checkpoint is bound because ANTIPHON_TASK_TOKEN was present (never printed). Runner-defaults and session-runners were read; no host/OS was pinned.

## Unlisted commands and diagnostics

The checkpoint tool bootstrap used the host build-slot gate and bin-c1192-tool/. Its first invocation refused an abbreviated expected SHA before starting rows; subsequent runs used the full committed SHA. Bootstrap slot=granted waited=0s. That implicit tool build was additional to the one isolated Antiphon.Tests build per checkpoint source group. The selected project builds use UseAppHost=false and bin-c1192-s5/; no build named tests was used.

Short shell recipients ran only through scripts/build-slot.ps1 to establish readonly baseline red and diagnose the newly modeled helper argv/path boundaries before another isolated test build. Fixture-seed diagnostics initially failed on helper argv and recursive mount substitution, then passed after correction. The expanded wrapper diagnostic initially had a shell syntax error from report-script generation, then passed after correction. Volume-recovery initially exposed replacement of a fixture-content suffix, then passed after prefix-only argument mapping. Volume-prune passed its authority/refusal matrix. The F-8 admission diagnostic initially exposed missing modeled npm/content setup; after adding that recipient it passed foreign-label refusal, the valid-marker precondition and F-9 restoration. These changed-source diagnostics are superseded by CP-21/22 on the final committed SHA; none are claimed as clean checkpoint certificates. Every diagnostic slot was granted, waited=0s; no unleased driver, timeout widening or assertion loosening.

## Ordinary invariant outcomes / remaining ownership

S5-assigned V-5, V-6, R-2, R-5, R-6, R-7: see final CP-21..26 counts below. V-9's seed failure/publication part is CP-23; full finalization is CP-34 in S7 and is not passed by this task.
V-1/V-2/V-3/V-4 and R-1/R-3 earlier-slice proofs were not rerun. V-7/V-8 and R-4/R-8, plus the remaining root/retention, evidence and finalization variants, belong to S6/S7. They are not marked passed here. Real saved-donor/marker/seed CP-17..20 retain their S4 ownership and were not rerun as forbidden out-of-slice rows.

Every plan PC-1..PC-141 and each listed variant remains pending SourceLanding Mutation. S5 specifically binds PC-58..PC-84, PC-90 and new PC-140/PC-141; PC-25's readonly-reader behavior has an additional ordinary witness. Runtime fixture negative inputs are ordinary controls and do not discharge any plan Mutation ID. Mutation owns deliberate mutants, expected assertion-red, restoration/green and missing-control discovery. Ordinary Review comes next; caller then lands original Code task and commissions SourceLanding Mutation.

## Final receipts

Final scoped invariant roster: PASS for every listed S5 requirement (each is assessed only against assigned S5 CPs).

| ID | Required S5 outcome |
|---|---|
| V-5 | CP-21 recipient bytes/modes, source-copy failure, integrity/import order and volume recovery; host defaults covered only through this slice's named regression rows |
| V-6 | CP-21 seed/reuse; CP-22 F-8 source admission, valid-marker/F-9 restore, corruption/link/escape refusal and volume Prune authority; CP-25 host Prune |
| V-9 | CP-23 original failure/outer publication guard, plus actual wrapper ordinary failure; full disposal verdict remains S7 |
| R-2 | CP-23 saved exit/subshell publication assertions and actual wrapper no-success-on-failure |
| R-5 | CP-26 executes import from 0755 source, proves widening first and restores root to exactly 0700 without changing payload |
| R-6 | CP-24 six nounset-expanded tree variants with exact diagnosis; donor source unchanged |
| R-7 | CP-25 full shared Prune regression; S4 Seed/marker/saved-donor CP-17..20 not rerun |


### 20261012-193652-5054 (unedited CHECKPOINT lines)

CHECKPOINT CP-21 commit=ffbd61599a57b635e6b4ca1c69c4990a3eb25faf build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-193652-5054/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=ffbd61599a57b635e6b4ca1c69c4990a3eb25faf sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=ffbd61599a57b635e6b4ca1c69c4990a3eb25faf build=reused filter=/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-193652-5054/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=ffbd61599a57b635e6b4ca1c69c4990a3eb25faf sourceState=clean buildSource=verified

### 20261012-194411-8a19 (unedited CHECKPOINT lines)

CHECKPOINT CP-21 commit=5b22a6467fde483942f393c556bcf69e00b4ffba build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-194411-8a19/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=5b22a6467fde483942f393c556bcf69e00b4ffba sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=5b22a6467fde483942f393c556bcf69e00b4ffba build=reused filter=/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-194411-8a19/rows/CP-22/run.trx slot=granted waited=30s dirty=0 source=5b22a6467fde483942f393c556bcf69e00b4ffba sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=5b22a6467fde483942f393c556bcf69e00b4ffba build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-194411-8a19/rows/CP-23/run.trx slot=granted waited=30s dirty=0 source=5b22a6467fde483942f393c556bcf69e00b4ffba sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=5b22a6467fde483942f393c556bcf69e00b4ffba build=reused filter=/*/*/RemoteScriptContractTests/C1066_Fixture_tree_fault_reaches_validator_under_nounset executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-194411-8a19/rows/CP-24/run.trx slot=granted waited=15s dirty=0 source=5b22a6467fde483942f393c556bcf69e00b4ffba sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=5b22a6467fde483942f393c556bcf69e00b4ffba build=reused filter=/*/*/RemoteScriptContractTests/C1195_Seed_import_chmod_restores_volume_root executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-194411-8a19/rows/CP-26/run.trx slot=granted waited=15s dirty=0 source=5b22a6467fde483942f393c556bcf69e00b4ffba sourceState=clean buildSource=verified
CHECKPOINT CP-25 commit=5b22a6467fde483942f393c556bcf69e00b4ffba build=reused filter=/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-194411-8a19/rows/CP-25/run.trx slot=granted waited=15s dirty=0 source=5b22a6467fde483942f393c556bcf69e00b4ffba sourceState=clean buildSource=verified

### 20261012-195239-419a (unedited CHECKPOINT lines)

CHECKPOINT CP-21 commit=47d0c670c03d7f818acd3679fb712ce39069291c build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-195239-419a/rows/CP-21/run.trx slot=granted waited=30s dirty=0 source=47d0c670c03d7f818acd3679fb712ce39069291c sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=47d0c670c03d7f818acd3679fb712ce39069291c build=reused filter=/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-195239-419a/rows/CP-22/run.trx slot=granted waited=45s dirty=0 source=47d0c670c03d7f818acd3679fb712ce39069291c sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=47d0c670c03d7f818acd3679fb712ce39069291c build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-195239-419a/rows/CP-23/run.trx slot=granted waited=15s dirty=0 source=47d0c670c03d7f818acd3679fb712ce39069291c sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=47d0c670c03d7f818acd3679fb712ce39069291c build=reused filter=/*/*/RemoteScriptContractTests/C1066_Fixture_tree_fault_reaches_validator_under_nounset executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-195239-419a/rows/CP-24/run.trx slot=granted waited=15s dirty=0 source=47d0c670c03d7f818acd3679fb712ce39069291c sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=47d0c670c03d7f818acd3679fb712ce39069291c build=reused filter=/*/*/RemoteScriptContractTests/C1195_Seed_import_chmod_restores_volume_root executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-195239-419a/rows/CP-26/run.trx slot=granted waited=30s dirty=0 source=47d0c670c03d7f818acd3679fb712ce39069291c sourceState=clean buildSource=verified
CHECKPOINT CP-25 commit=47d0c670c03d7f818acd3679fb712ce39069291c build=reused filter=/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-195239-419a/rows/CP-25/run.trx slot=granted waited=15s dirty=0 source=47d0c670c03d7f818acd3679fb712ce39069291c sourceState=clean buildSource=verified

### 20261012-200019-f02b (unedited CHECKPOINT lines)

CHECKPOINT CP-21 commit=9564ab3ad84f087389d848f02a0c53eb2b62c865 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_seed_and_rerun_keep_payloads_in_volume executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-200019-f02b/rows/CP-21/run.trx slot=granted waited=45s dirty=0 source=9564ab3ad84f087389d848f02a0c53eb2b62c865 sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=9564ab3ad84f087389d848f02a0c53eb2b62c865 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-200019-f02b/rows/CP-22/run.trx slot=granted waited=105s dirty=0 source=9564ab3ad84f087389d848f02a0c53eb2b62c865 sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=9564ab3ad84f087389d848f02a0c53eb2b62c865 build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-200019-f02b/rows/CP-23/run.trx slot=granted waited=75s dirty=0 source=9564ab3ad84f087389d848f02a0c53eb2b62c865 sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=9564ab3ad84f087389d848f02a0c53eb2b62c865 build=reused filter=/*/*/RemoteScriptContractTests/C1066_Fixture_tree_fault_reaches_validator_under_nounset executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-200019-f02b/rows/CP-24/run.trx slot=granted waited=15s dirty=0 source=9564ab3ad84f087389d848f02a0c53eb2b62c865 sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=9564ab3ad84f087389d848f02a0c53eb2b62c865 build=reused filter=/*/*/RemoteScriptContractTests/C1195_Seed_import_chmod_restores_volume_root executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-200019-f02b/rows/CP-26/run.trx slot=granted waited=15s dirty=0 source=9564ab3ad84f087389d848f02a0c53eb2b62c865 sourceState=clean buildSource=verified
CHECKPOINT CP-25 commit=9564ab3ad84f087389d848f02a0c53eb2b62c865 build=reused filter=/*/*/RemoteScriptContractTests/C913_Prune_validates_recovery_without_refilling_image_packs executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f36e645d/.antiphon/checkpoints/20261012-200019-f02b/rows/CP-25/run.trx slot=granted waited=30s dirty=0 source=9564ab3ad84f087389d848f02a0c53eb2b62c865 sourceState=clean buildSource=verified

Final receipt: .antiphon/checkpoints/20261012-200019-f02b/report.json. CHECKPOINT SOURCE VALID source=9564ab3ad84f087389d848f02a0c53eb2b62c865 rows=6 (exit 0). Independently parsed each fresh TRX and checked exact class/method and expanded result counts: CP-21=1, CP-22=1, CP-23=1, CP-24=6, CP-25=1, CP-26=1; all Passed, no skipped/failed. Final total 11/11. Every row slot=granted; waited=45s,105s,75s,15s,30s,15s respectively. The isolated build separately waited=105s, slot=granted. Source dirty=0, sourceState=clean, buildSource=verified. One transient bound-owner HTTP timeout recovered; no unbound fallback. No tests remain red. No unchanged proof repetitions, loaded repetitions or deliberate mutants were commissioned.

Rerun through the checkpoint tool: run --plan docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md --after S5 --expected-source-sha <full committed HEAD>, then wait every exit 75 until terminal. Bootstrap any tool build through scripts/build-slot.ps1 with an isolated forward-slash OutputPath and UseAppHost=false; selected test outputs are bin-c1192-s5/.

Git integration audit before this report-only commit: fetched origin/master=593278bf2ebaa04c3eb615f2d3421ca800fc7710; git merge-tree --write-tree HEAD origin/master exit 0, tree=c3b6e710ddfe4567ebab38099084f3baab9a4d7c. git ls-files count=7434 before adding this individual report (7435 afterwards). Worktree clean before the report. Full-range evidence guard through tested source: commits=7, entries=0, violations=0. The final report-only range guard and remote equality are checked after publication; exact final SHA is in the caller summary and external .antiphon/task-f36e645d.md.

The checkpoint tool removed all project bin-c1192-s5/ outputs after green. Tool bootstrap bin-c1192-tool/ is removed after validation. Checkpoint build-log directories and JSON/TRX receipts remain gitignored in .antiphon/checkpoints; they are evidence, not project binary outputs.

--- next stage ---
next: review
handoff: Review S5 implementation and scoped Final CP-21..26 at tested source 9564ab3ad84f087389d848f02a0c53eb2b62c865. Confirm readonly pruning and F8 foreign-source refusal, clean receipts, and PC-1..141 pending. Caller lands original Code task then commissions SourceLanding Mutation; S6/S7 and W-1 remain separate.
artifact: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md
