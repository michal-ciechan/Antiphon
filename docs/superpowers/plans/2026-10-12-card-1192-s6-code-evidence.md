# CARD-1192 S6 Code report (ef734e84)

S6 moves F-5 ordinary package payloads and mask mounts to the case-owned work/mask volumes. Scoped Final CP-27..32 passed 8/8, with zero failed/skipped and SHA-qualified clean receipts.

Original Code task / landing owner: ef734e84-7f50-4bd2-b7d2-202e0e7151aa.
Branch: feat/card-task-ef734e84.
Worktree: /work/worktrees/task-ef734e84 (runner mirror; desktop checkout unavailable here).
Task base: 2fc32850ccf28ecab89ca3ac88f4a3d3a5a587f5.
Implementation/test source: e08d789d2726820e9c5fecfd2ea7a5147a48b897.
Plan: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md.
Evidence document: docs/superpowers/plans/2026-10-12-card-1192-s6-code-evidence.md.
Restart: none.

## Changes

- scripts/c590-remote.sh: F-5 generates ordinary payloads via the case writer at /fixture/apphost; imports from the readonly/nocopy work mount with a positional subtree; preserves metadata, executable bits and final chmod 0700. Mask validates current-run labels and emptiness, admits exactly one of three packs at 9.0.20, and mounts the case mask readonly/nocopy. Apphost and masked consumers register before create/start, carry owner/run/role labels and use the existing identity-checked cleanup ledger, with consumers released before the standing reader/writer.
- scripts/fixtures/c1192-workspace-contract.sh: actual F-5 recipient model, independent host decoy and cache bytes/modes, mask argv/emptiness, smoke failure and failed-start cleanup, exact foreign-label diagnoses. Failed-start registration is checked after clearing prior successful mask registrations. Retention parses the actual generated Compose documents and executes sentinel writes/reads; observes recreate bytes, external survival, temp/private separation and nonexternal negative deletion. Image/compiler tools and UID enforcement are modeled; real UID/image qualification remains W-1.
- tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs: new CP-27 test; adapt the existing CP-28 positional import snippet without weakening root/bytes assertions; CP-29 executes retention in addition to preserved UID/nocopy pins; CP-30 executes the parent diagnosis witness. All shell children have the assembly process limiter.
- Plan: S6 implementation bindings and additional pending G/PC-142..148; costs now include all 148 controls. CP-27..32 already carried After S6; no selector fix was needed.

The F-8 refill tarball was volume-backed in S3; F-8 recovery copy/observations and F-9 mutation/backups/restoration were migrated in S5 to avoid host consumers of logical paths. Their source functions and the full control/group/variant roster remain unchanged here. No remaining independent S6 payload was found there. S7 still owns host control-tree removal and finalization/evidence audit. No S7 implementation or other checkpoint row was run.

## Scope and limits

Final uses the brief's explicit closed S6 house-rule selection CP-27..32. The brief forbids the broad Unit lane and out-of-slice rows, overriding its generic Final profile. This is scoped S6 Final proof, not a whole-class/whole-Unit certificate and not completion of CARD-1192. No full assembly, real Docker Fixture, live Seed, rollout, deploy or restart was run. W-1 remains rollout-owned after S7/Review/land. No assertion or timeout was loosened.

ANTIPHON_TASK_TOKEN was present: all checkpoint runs were bound. Its value was never printed. GET /api/runner-defaults and GET /api/session-runners were read; no host/OS override was pinned.

## Baseline and guards

Tests were committed/pushed before production changes. adb7ada8d68182b20c1f6204d69b1f20fd6ab90a CP-27 failed on my harness insertion syntax error, not the guarded defect. Fixed harness at 7ae783d6407662872f05b5c5483fd8afab30f145 has the same production script as the task base. Its CP-27 executed 1, failed 1, skipped 0; the fresh TRX shows the intended method and assertion: apphost-volume-payload expected 1, observed 0, C1192_EXIT=0. Host-decoy, readonly-import and three-mask facts were also 0. This is the authoritative pre-slice red witness.

Case -> mutation -> expected red: restoring the host payload/bind fails apphost-volume-payload and host-decoy; deleting readonly fails apphost-import-readonly; removing mask readonly/nocopy or broadening target fails mask-readonly/mask-nocopy/mask-exact-pack; skipping work/mask/npm admission fails exact foreign-label refusal; moving masked registration after start fails the fresh mask-partial-start-cleaned witness; deleting early consumer release fails mask-consumer-before-workspace, witnessed red on actual prior committed source. CP-28 executes 0755 copy widening and requires exact 0700 restoration/bytes; removing chmod is red. CP-29 observes retained bytes and private resources independently; nonexternal packages or missing temp down-v fails. CP-30 requires the original parent-shell diagnosis; CP-31 covers invalid/long/leading-zero/measured budget cases; CP-32 keeps all three nounset warm variants. Deliberate mutants were not run.

## Run history and unlisted commands

- 20261012-203724-587c at adb7ada8d68182b20c1f6204d69b1f20fd6ab90a: CP-27 harness syntax red, superseded by repaired baseline.
- 20261012-204235-4470 at 7ae783d6407662872f05b5c5483fd8afab30f145: CP-27 guarded assertion red on pre-slice production.
- 20261012-204724-4d75 at a613d860e389021a709e4630a42a0755a0fd476d: all six rows, 8 passed, 0 failed/skipped. Superseded after strengthening exact refusal diagnoses and removing stale ledger witnesses.
- 20261012-205005-6ea8 at 400aca1297512f252002573da768fb995fb34fe4: all six rows, 8 passed, 0 failed/skipped. Superseded by the consumer-before-workspace cleanup fix and new order witness.
- 20261012-205351-3202 at e08d789d2726820e9c5fecfd2ea7a5147a48b897: final six rows, 8 passed, 0 failed/skipped; source qualified.

One isolated Antiphon.Tests build per committed checkpoint group, UseAppHost=false, bin-c1192-s6/. The manifest imports seven build definitions, but only the S6 build was executed. Checkpoint runs/waits used the built Antiphon.Checkpoints DLL; it acquires the host slots for its own drivers. Its extra bootstrap build went through scripts/build-slot.ps1 with bin-c1192-s6-tool/, slot=granted waited=90s, 0 errors and 1 inherited nullable warning.

Four additional short shell diagnostics went through scripts/build-slot.ps1: repaired baseline recipient (facts red, shell exit 0, slot=granted waited=15s); implementation apphost recipient (14 facts passed, slot=granted waited=0s); retention recipient (6 facts passed, slot=granted waited=0s). The fourth diagnostic ran the new witness against exact prior committed source 400aca1297512f252002573da768fb995fb34fe4: fourteen apphost facts stayed 1, mask-consumer-before-workspace=0 (guarded red observation, shell exit 0), slot=granted waited=0s. It copied exact Git source, not a deliberate mutant; its temporary source tree was removed. These diagnose recipient boundaries before isolated test builds; they do not replace clean checkpoint receipts. Syntax checks, TRX inspection, receipt validation and evidence/Git audits are read-only checks. No unleased driver, retry policy, deliberate mutation or loaded repetition was used. No unchanged proof selection repeated; each green selection verifies a changed committed source.

## Required IDs

S6-assigned V-7, V-2 (new consumers), R-4, R-5, R-6 are assessed against CP-27..32 only. Outcomes/counts are finalized below.
V-1/V-3/V-4/V-5/V-6 and full earlier V-2 variants and R-1/R-2/R-3/R-7 retain earlier-slice checkpoint ownership and were not rerun. V-8/V-9 and R-8, plus final disposal/export variants, remain S7. They are not marked passed here.
Every plan PC-1..PC-148 and every listed variant remains pending method-scoped SourceLanding Mutation. S6 specifically binds PC-91..105 and PC-142..148. Runtime Fixture controls are ordinary inputs and do not discharge plan Mutation controls. Mutation owns red/restore/green and missing-control discovery. No unbound candidate was commissioned; the candidate root docs/superpowers/mutations/ was unused. Review is next; caller lands the original Code task and commissions SourceLanding Mutation.

## Final outcomes and provenance

| ID | Actual S6 outcome |
|---|---|
| V-1 | Not rerun; earlier-slice owner |
| V-2 | PASS for S6 consumer-before-workspace order and failed masked-start cleanup, CP-27; earlier lifecycle variants not rerun |
| V-3 | Not rerun; S2 owner |
| V-4 | Not rerun; S3 owner; refill source unchanged in manual audit |
| V-5 | Not rerun; S4/S5 owner |
| V-6 | Not rerun; S5 owner; F-8/F-9 seams unchanged in manual audit |
| V-7 | PASS, CP-27/28/29; volume import/mask, exact root restoration, retention/private separation |
| V-8 | Full payload/evidence audit pending S7; S6 host decoy passed CP-27 only |
| V-9 | Full finalization pending S7 |
| R-1 | Not rerun; earlier and S7 rows excluded |
| R-2 | Not rerun; earlier and S7 rows excluded |
| R-3 | Not rerun; S2/S7 owner |
| R-4 | PASS, CP-30/31; parent diagnosis, invalid/oversized/leading-zero/measured scaling |
| R-5 | PASS for assigned apphost/root/retention variants, CP-28/29; seed CP-26 excluded |
| R-6 | PASS for assigned warm arguments, CP-32=3; earlier npm/tree arguments excluded |
| R-7 | Not rerun; S4/S5 owner |
| R-8 | Pending S7 |
| W-1 | Not run; rollout-owned native image/UID qualification after S7/Review/land |

Final run 20261012-205351-3202 tested e08d789d2726820e9c5fecfd2ea7a5147a48b897. CP-27=1, CP-28=1, CP-29=1, CP-30=1, CP-31=1, CP-32=3; all Passed. Every fresh TRX was independently parsed for exact class/method and nonzero expanded count. Receipt validation: CHECKPOINT SOURCE VALID source=e08d789d2726820e9c5fecfd2ea7a5147a48b897 rows=6 (exit 0). Every final row slot=granted waited=0s; isolated S6 build slot=granted waited=0s. Every final CHECKPOINT line has dirty=0, sourceState=clean, buildSource=verified. No tests remain red.

Manual source/manifest audit: G=148 unique=148, PC=148 unique=148; all six After cells=S6. Refill tarball, recovery copy, fixture Prune, contract controls and roster bodies match the task base exactly. S6 manual source audit is complete; live W-1 remains unexecuted.

Rerun: checkpoint tool run --plan docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md --after S6 --expected-source-sha <full committed HEAD>, then wait each exit 75 until terminal. Bootstrap its DLL through scripts/build-slot.ps1, dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1192-s6-tool/ --property:UseAppHost=false; execute the built Antiphon.Checkpoints.dll run/wait verbs. The executor gates every test driver and executes only S6 build/rows.

Git audit at tested source: fetched origin/master=227a00a0e159d25984c367cc054153ecd28e20bf; git merge-tree --write-tree HEAD origin/master exit 0, tree=85dadf5829df8d4ba9dbd575d8fd07b9bd077c3b. git ls-files=7438 before this individual evidence document (7439 afterwards). Source worktree clean. Final publication SHA, full-range evidence guard and remote equality are recorded after the report-only commit in the caller summary/external report.

Checkpoint tool removed all project bin-c1192-s6/ outputs after green. Bootstrap bin-c1192-s6-tool/ is removed after validation. JSON/TRX/log payloads remain gitignored in .antiphon/checkpoints; only this individual Markdown evidence document is committed.

### 20261012-203724-587c (unedited CHECKPOINT lines)

CHECKPOINT CP-27 commit=adb7ada8d68182b20c1f6204d69b1f20fd6ab90a build=ok filter=/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-203724-587c/rows/CP-27/run.trx slot=granted waited=45s dirty=0 source=adb7ada8d68182b20c1f6204d69b1f20fd6ab90a sourceState=clean buildSource=verified

### 20261012-204235-4470 (unedited CHECKPOINT lines)

CHECKPOINT CP-27 commit=7ae783d6407662872f05b5c5483fd8afab30f145 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-204235-4470/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=7ae783d6407662872f05b5c5483fd8afab30f145 sourceState=clean buildSource=verified

### 20261012-204724-4d75 (unedited CHECKPOINT lines)

CHECKPOINT CP-28 commit=a613d860e389021a709e4630a42a0755a0fd476d build=reused filter=/*/*/RemoteScriptContractTests/C1195_Fixture_import_chmod_restores_volume_root executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-204724-4d75/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=a613d860e389021a709e4630a42a0755a0fd476d sourceState=clean buildSource=verified
CHECKPOINT CP-27 commit=a613d860e389021a709e4630a42a0755a0fd476d build=ok filter=/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-204724-4d75/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=a613d860e389021a709e4630a42a0755a0fd476d sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=a613d860e389021a709e4630a42a0755a0fd476d build=reused filter=/*/*/RemoteScriptContractTests/C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-204724-4d75/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=a613d860e389021a709e4630a42a0755a0fd476d sourceState=clean buildSource=verified
CHECKPOINT CP-29 commit=a613d860e389021a709e4630a42a0755a0fd476d build=reused filter=/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-204724-4d75/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=a613d860e389021a709e4630a42a0755a0fd476d sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=a613d860e389021a709e4630a42a0755a0fd476d build=reused filter=/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-204724-4d75/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=a613d860e389021a709e4630a42a0755a0fd476d sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=a613d860e389021a709e4630a42a0755a0fd476d build=reused filter=/*/*/RemoteScriptContractTests/C1066_Fixture_warm_probe_uses_argument_log_under_nounset executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-204724-4d75/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=a613d860e389021a709e4630a42a0755a0fd476d sourceState=clean buildSource=verified

### 20261012-205005-6ea8 (unedited CHECKPOINT lines)

CHECKPOINT CP-28 commit=400aca1297512f252002573da768fb995fb34fe4 build=reused filter=/*/*/RemoteScriptContractTests/C1195_Fixture_import_chmod_restores_volume_root executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205005-6ea8/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=400aca1297512f252002573da768fb995fb34fe4 sourceState=clean buildSource=verified
CHECKPOINT CP-27 commit=400aca1297512f252002573da768fb995fb34fe4 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205005-6ea8/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=400aca1297512f252002573da768fb995fb34fe4 sourceState=clean buildSource=verified
CHECKPOINT CP-29 commit=400aca1297512f252002573da768fb995fb34fe4 build=reused filter=/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205005-6ea8/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=400aca1297512f252002573da768fb995fb34fe4 sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=400aca1297512f252002573da768fb995fb34fe4 build=reused filter=/*/*/RemoteScriptContractTests/C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205005-6ea8/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=400aca1297512f252002573da768fb995fb34fe4 sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=400aca1297512f252002573da768fb995fb34fe4 build=reused filter=/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205005-6ea8/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=400aca1297512f252002573da768fb995fb34fe4 sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=400aca1297512f252002573da768fb995fb34fe4 build=reused filter=/*/*/RemoteScriptContractTests/C1066_Fixture_warm_probe_uses_argument_log_under_nounset executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205005-6ea8/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=400aca1297512f252002573da768fb995fb34fe4 sourceState=clean buildSource=verified

### 20261012-205351-3202 (unedited CHECKPOINT lines)

CHECKPOINT CP-28 commit=e08d789d2726820e9c5fecfd2ea7a5147a48b897 build=reused filter=/*/*/RemoteScriptContractTests/C1195_Fixture_import_chmod_restores_volume_root executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205351-3202/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=e08d789d2726820e9c5fecfd2ea7a5147a48b897 sourceState=clean buildSource=verified
CHECKPOINT CP-27 commit=e08d789d2726820e9c5fecfd2ea7a5147a48b897 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Apphost_mask_is_an_empty_exact_volume_mount executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205351-3202/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=e08d789d2726820e9c5fecfd2ea7a5147a48b897 sourceState=clean buildSource=verified
CHECKPOINT CP-29 commit=e08d789d2726820e9c5fecfd2ea7a5147a48b897 build=reused filter=/*/*/RemoteScriptContractTests/C1195_Retention_probe_is_app_uid_and_does_not_copy_image_content executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205351-3202/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=e08d789d2726820e9c5fecfd2ea7a5147a48b897 sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=e08d789d2726820e9c5fecfd2ea7a5147a48b897 build=reused filter=/*/*/RemoteScriptContractTests/C1194_Fixture_prune_scales_budget_outside_arithmetic_substitution executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205351-3202/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=e08d789d2726820e9c5fecfd2ea7a5147a48b897 sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=e08d789d2726820e9c5fecfd2ea7a5147a48b897 build=reused filter=/*/*/RemoteScriptContractTests/C1194_Fixture_budget_scale_refuses_empty_observation executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205351-3202/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=e08d789d2726820e9c5fecfd2ea7a5147a48b897 sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=e08d789d2726820e9c5fecfd2ea7a5147a48b897 build=reused filter=/*/*/RemoteScriptContractTests/C1066_Fixture_warm_probe_uses_argument_log_under_nounset executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ef734e84/.antiphon/checkpoints/20261012-205351-3202/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=e08d789d2726820e9c5fecfd2ea7a5147a48b897 sourceState=clean buildSource=verified

--- next stage ---
next: review
handoff: Review S6 source and scoped Final CP-27..32 at e08d789d2726820e9c5fecfd2ea7a5147a48b897, including consumer order, exact refusal and fresh-registration witnesses. All PC-1..148 remain pending; caller lands original Code task then commissions SourceLanding Mutation. S7/W-1 remain separate.
artifact: docs/superpowers/plans/2026-10-12-card-1192-s6-code-evidence.md
