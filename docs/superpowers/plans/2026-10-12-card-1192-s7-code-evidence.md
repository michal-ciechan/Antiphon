# CARD-1192 S7 Code evidence (ce4980da)

S7 removes the fixture's host payload root and makes disposal precede success. Its closed Final CP-33..43 group passed 11/11, with zero failures/skips and SHA-qualified clean receipts. Live W-1 acceptance remains pending.

Original Code task / landing owner: ce4980da-9fb7-4ddc-821a-1ae22299bdf6.
Branch: feat/card-task-ce4980da.
Worktree: /work/worktrees/task-ce4980da (runner mirror; desktop checkout unavailable).
Task base: 2b5aa6aeeaa3f26062b59609019e92d43575105d.
Actual tested implementation/plan SHA: 88145843b709b4427cec04d19c1965d50ced5cea.
Plan: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md.
Evidence: docs/superpowers/plans/2026-10-12-card-1192-s7-code-evidence.md.
Bound: ANTIPHON_TASK_TOKEN was present, with this task ID; checkpoint owner guard was active. No token was printed.
Platform reads: GET /api/runner-defaults revision 2 and GET /api/session-runners showed eligible Linux placement. No Runner or Platform pin was supplied.
Restart: none.

## Changes and scope

- scripts/c590-remote.sh: retain the original host maintenance lock, then use the fresh private CASE_DIR/fixture-controller for control documents and logs. Arm EXIT before workspace acquisition. Record device/inode/host UID and revalidate it before exporting or removing the controller. Delete from inside the acquired directory. Stop/join consumers, export only the five regular race/lock logs through guarded destinations, release helpers, remove exact owned volumes/network, then remove the controller. Explicit normal finalization precedes success. A failed finalization keeps a named residue, refuses success and remains idempotently failed through EXIT. Primary diagnosis/status and UnhandledExit fallback survive secondary errors. Early true receipts are rejected. Foreign registered helpers remain untouched and named as residue. Legacy consumers retain the acquired ID/name marker and expected-name validation.
- scripts/fixtures/c1192-workspace-contract.sh and RemoteScriptContractTests.cs: real wrapper/finalizer/EXIT with modeled Docker boundaries and independent resource/file observations; actual seed/recovery validators; host decoys; individual readonly checkout binds; success/failure/secondary failure/early receipt/foreign replacement and helper scenarios; exact summary bytes and full census faults; five retained log bodies and unlink/absence/directory-race gates. The former C1191 sudo-tree pin is explicitly replaced by workspace disposal behavior. No assertion or timeout was loosened.
- scripts/fixtures/c913-seed-contract.sh: retain the full inventory/occupied/control diagnosis matrix; replace the stale unregistered owned-packages cleanup entry with the exact admitted name. Replace the removed host F-5 recipient with the existing strict volume recipient model and independently require its real bytes/mode, mask and failure-cleanup facts. Its occupied fixture wrapper now supplies WROTE with production receipt semantics.
- docs/docker-stack.md: describe controller/volume storage, cleanup verdict and pending W-1; no live acceptance claim. The plan binds S7 and adds PC-149..152.

The brief's binding house rules and plan explicitly require S7's closed rows only and prohibit a broad Unit lane/full assembly. This is scoped Final evidence for that authorized list, not a claim that the generic whole-Unit/full-class Final profile ran. CP-1..32 and earlier V/R variants were not separately rerun. No default verification, native Docker test, rollout, deploy or restart is credited. Every S7 After column already equals S7; no selector repair was needed. The abbreviated prose SHA 2b5aa6aea was invalid; the full branch-contract SHA above was present and used.

## Red witnesses and run history

1. c66f21e063647fe457aea9a8accb8783cdc2fbda contains the new tests with exactly pre-slice production. Run 20261012-212318-8c2c selected CP-33/34/35/36: 4 executed, 4 failed, 0 skipped, all at guarded assertions. CP-33 rejects /tmp/c849-fixture-; CP-34 observes success-last=0; CP-35 observes success-control-removed=0; CP-36 observes evidence-control-removed=0. Fresh TRX method identities/counts were checked.
2. Run 20261012-213050-2d11 at 228120ededb63bb65b7b3a1e71e8445968ce638f selected all S7 rows: 10 passed, CP-43 failed. Its repaired occupied path exposed an uninitialized WROTE in the old harness. Before production changes, a gated direct inventory harness reproduced the inherited owned-ledger-removed failure on pre-slice code; this was not attributed to the implementation.
3. Run 20261012-213556-d76e at 90559cbc35621d35440e278320267bb00c604b5c was explicitly stopped and awaited after a newly added shell witness had a syntax error. No method outcome is credited. Its isolated build had slot=granted waited=0s; wait returned 6 after stop, with executor dead and no phase=done. The harness was repaired and pushed without rewriting history.
4. Run 20261012-213642-7f08 at 31dfde92b40406145896255b8dff543f9beddcd7: CP-33 passed; CP-34 failed at early-exit-refused, observing early-exit-refused=0 and foreign-helper-residue=0 on unchanged production. CP-43 failed on my wrong names for two recipient facts; fixed to apphost-import-bytes-mode and mask-failure-cleaned, retaining both assertions.
5. Final run 20261012-214511-9d96 at 88145843b709b4427cec04d19c1965d50ced5cea: all 11 S7 methods passed, 0 failed/skipped. CP-43 red was rerun green. No tests remain red.

Case -> regression -> expected red: host-root/bind/read restoration fails CP-33's root, seed/recovery recipient and per-bind assertions; publishing before finalization fails CP-34 success-last/early-exit; suppressing cleanup residue or failure changes exact observed verdict/resources; removal without acquired identity deletes the foreign canary; bypassing roster comparisons accepts missing/duplicate lists; deleting unlink/regular-file/absent/-T guards fails their independent filesystem witnesses. Mutation is separately owned; no deliberate mutant was run.

A gated historical-source diagnostic used the exact Git file from 31dfde92b40406145896255b8dff543f9beddcd7 with the corrected current canary: early-exit-refused=0, foreign-helper-residue=0, unregistered-consumer-preserved=0. The final-source diagnostic observed all three=1. These are guarded red observations (the fact-emitting shell itself returns 0), not PC cycles or clean checkpoint receipts. The earlier malformed canary diagnostic is superseded and receives no credit.

## Checkpoint outcomes and required IDs

| CP | Executed / passed / failed / skipped | Slot / waited |
|---|---|---|
| CP-33 | 1 / 1 / 0 / 0 | granted / 0s |
| CP-34 | 1 / 1 / 0 / 0 | granted / 0s |
| CP-35 | 1 / 1 / 0 / 0 | granted / 0s |
| CP-36 | 1 / 1 / 0 / 0 | granted / 15s |
| CP-37 | 1 / 1 / 0 / 0 | granted / 15s |
| CP-38 | 1 / 1 / 0 / 0 | granted / 15s |
| CP-39 | 1 / 1 / 0 / 0 | granted / 0s |
| CP-40 | 1 / 1 / 0 / 0 | granted / 15s |
| CP-41 | 1 / 1 / 0 / 0 | granted / 15s |
| CP-42 | 1 / 1 / 0 / 0 | granted / 15s |
| CP-43 | 1 / 1 / 0 / 0 | granted / 30s |

Every fresh final TRX was independently parsed for exactly one intended class/method and a Passed result: Antiphon.Tests.Scripts.RemoteScriptContractTests and the exact method named by each plan filter. Validator exit 0: CHECKPOINT SOURCE VALID source=88145843b709b4427cec04d19c1965d50ced5cea rows=11. All final lines retain dirty=0, sourceState=clean, buildSource=verified and the exact tested source. One isolated S7 build ran per completed group, UseAppHost=false, bin-c1192-s7/, never a build named tests. The manifest imports seven definitions; only the S7 build executed. Final build slot=granted waited=0s, 0 errors, 695 existing warnings. Final run wall=5m18s.

| ID | Actual outcome in this S7 round |
|---|---|
| V-1 | Earlier lifecycle owner; not separately rerun |
| V-2 | PASS for S7 disposal, exact residue, later cleanup attempts and foreign preservation, CP-34/35; earlier variants not rerun |
| V-3 | S2 owner; not separately rerun |
| V-4 | S3 owner; not separately rerun |
| V-5 | S4/S5 owner; CP-33 additionally passed actual volume seed/recovery recipients; no full earlier backend rerun claimed |
| V-6 | S5 owner; CP-33 additionally passed actual volume recovery controls and restoration; earlier CP-21/22/25 not separately rerun |
| V-7 | S6 owner; CP-43 additionally passed the strict F-5 recipient facts; earlier CP-27/28/29 not separately rerun |
| V-8 | PASS for S7 source audit, volume recipients, binds, summary/census/privacy, CP-33/36/41/42 |
| V-9 | PASS for S7 success/failure/secondary failure/early receipt/idempotent finalization, CP-34; CP-33 also checks actual seed-subshell isolation |
| R-1 | PASS for assigned unlink normal/EXIT variant, CP-37; earlier image-tool rows not rerun |
| R-2 | PASS for assigned disposal/failure preservation, CP-34/35; earlier probe handoff row not rerun |
| R-3 | PASS for assigned five-log order/path/race variants, CP-38/39; HOME/scratch row not rerun |
| R-4 | S6 budget owner; not separately rerun |
| R-5 | S5/S6 root/retention owner; not separately rerun |
| R-6 | S3/S5/S6 nounset owner; not separately rerun |
| R-7 | S4/S5 host/saved/seed/prune owner; not separately rerun; CP-33 adds volume seed/recovery observations only |
| R-8 | PASS for assigned lane/receipt/inventory variants, CP-40..43 |
| W-1 | NOT RUN; rollout-owned after Review/land; native image/UID/archive/NuGet-lock acceptance remains pending |

All PC-1..PC-152 and every listed variant are PENDING method-scoped SourceLanding Mutation. S7 directly binds PC-106..132, PC-137..139 and PC-149..152; other controls retain their original detecting methods. No ordinary green or live Fixture family discharges these. Mutation owns deliberate mutants, red/restore/green and missing-control discovery. No unbound candidate was commissioned; docs/superpowers/mutations/ was unused. Caller reviews, lands this original Code task, then commissions SourceLanding Mutation. Nothing is marked deferred-to-final passed: earlier separate slice rows are not rerun here and W-1/PCs remain pending with their owners.

## Manual consumer audit

- F-1/F-2: rendered Compose models, fake empty credential-path placeholders, mode/ownership receipts and host-path PC-08 self-link are small controller/control artifacts. Cache sentinels/modes remain container-volume operations. Maintenance lock is taken against the original live root and later calls retain its held flag.
- F-3 and shared seed/recovery: fixture activation selects volume before donor generation; donor/tree, archives, stage, npm import and recovery/manifest bytes go through helpers/streams. Marker, accepted seed/smoke/status/hashes are host receipts. Real/saved/cold Seed remain host-backed and were not changed by S7.
- F-4: feed, projects, generated programs and ready/go/release are /fixture/race recipients. Only command stdout race/lock logs are on the controller. Readonly checkout script inputs retain their exact mounts.
- F-5/F-6: ordinary import and mask remain named mounts with root repair; npm tarballs, hash/server programs, lockfiles, node_modules and sentinel reads use work. Host npm logs are small controller artifacts.
- F-7: retention Compose documents remain controller files until teardown; cache/private payloads are volumes.
- F-8/F-9: previews, consumer-state, budgets, markers, backups of authority documents and roster lists are host control files; refill tarball, recovery payload/mode/manifest backups and toxic probe bytes remain in work. Only validated aggregate bytes reach CASE_DIR. All payload variables/subtrees in these seams were audited, including host redirects/test/find/cp/stat/realpath and mounts. No remaining fixture payload host path or payload bind was found.
- Cleanup: actual primary/secondary/early/final success paths observed; five safe logs preserve bytes; foreign replacement files are neither exported nor removed. Small permitted evidence remains, while controller disposal is identity checked. Source syntax and ASCII checks passed. G=152 unique=152, PC=152 unique=152, all 11 After cells=S7.

Offline substitutes establish source/control-flow/path/recipient contracts; native Docker, ownership enforcement, stream behavior and NuGet lock timing remain W-1. SIGKILL/host loss cannot guarantee immediate cleanup. The task did not remove older failed-run host trees.

## Unlisted drivers, repetitions and custody

The extra checkpoint-tool bootstrap build ran through scripts/build-slot.ps1 with bin-c1192-s7-tool/ and UseAppHost=false: slot=granted waited=0s, 0 errors, one existing nullable warning. It was necessary to execute the checkpoint tool from this mirror. All subsequent checkpoint builds/rows took their own slots. No raw unleased build/test driver ran.

Seven short gated shell diagnostics were used to locate boundaries before expensive isolated builds: (1) pre-slice inventory, held 1s, slot=granted waited=0s, inherited owned-ledger-removed red; (2) initial finalizer, held 3s, granted/0s, revealed the extraction shape of the controller-removal function; (3) repaired extraction, held 4s, granted/0s, earlier facts green; (4) prior-source edge witness with initially incomplete canary, held 4s, granted/15s; (5) current-source witness with that incomplete canary, held 14s, granted/0s; (6) corrected canary on exact prior source, held 10s, granted/0s, all three guarded edge facts 0; (7) corrected canary on final source, held 13s, granted/0s, all facts 1. The first tooling read probes/syntax/ASCII/TRX/receipt/Git audits are read-only and do not launch a suite. These diagnostic observations do not replace checkpoint receipts or PC cycles.

No Repeat flag, loaded repetition, retry policy, widened timeout or weakened assertion was used. No unchanged proof selection was run beyond its budget; each checkpoint group verifies a changed committed selection/source. A passing final selection was not repeated. Every owned run/stop/wait was awaited; no run remains in flight.

Checkpoint green removed all bin-c1192-s7/ outputs. The separately owned bootstrap outputs and temporary historical-source/draft files are removed after receipt validation. JSON/TRX/log/checkpoint payloads remain gitignored under .antiphon/checkpoints; only this individual Markdown report is committed.

## Git and rerun

At tested source: fetched origin/master=2b5aa6aeeaa3f26062b59609019e92d43575105d; git merge-tree --write-tree HEAD origin/master exit 0, tree=600447a116de3611bf9df1ac768bfd9bd59be5c0. git ls-files=7441 before this report (7442 afterwards). Source worktree clean. Every meaningful slice was committed/pushed on the assigned fast-forward-only branch; no amend/rebase/reset/force push. No commit_refs failure occurred. Final report-only publication SHA, remote equality and full base..HEAD evidence guard are recorded in the external report/caller summary.

Rerun bootstrap through the host gate:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1192-s7-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1192-s7-tool/ --property:UseAppHost=false`

Then `dotnet tools/Antiphon.Checkpoints/bin-c1192-s7-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md --after S7 --expected-source-sha <full committed HEAD> --max-wait 50s`; use the same DLL's `wait <run-id> --max-wait 50s` for every exit 75, until terminal. Its executor gates the isolated S7 build and exact rows. Qualify its report with `validate --evidence <report.json> --expected-source-sha <tested SHA>`.

## Unedited CHECKPOINT lines

### 20261012-212318-8c2c

CHECKPOINT CP-33 commit=c66f21e063647fe457aea9a8accb8783cdc2fbda build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-212318-8c2c/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=c66f21e063647fe457aea9a8accb8783cdc2fbda sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=c66f21e063647fe457aea9a8accb8783cdc2fbda build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-212318-8c2c/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=c66f21e063647fe457aea9a8accb8783cdc2fbda sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=c66f21e063647fe457aea9a8accb8783cdc2fbda build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-212318-8c2c/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=c66f21e063647fe457aea9a8accb8783cdc2fbda sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=c66f21e063647fe457aea9a8accb8783cdc2fbda build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-212318-8c2c/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=c66f21e063647fe457aea9a8accb8783cdc2fbda sourceState=clean buildSource=verified

### 20261012-213050-2d11

CHECKPOINT CP-33 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-38/run.trx slot=granted waited=15s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-39/run.trx slot=granted waited=30s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-40/run.trx slot=granted waited=30s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-41/run.trx slot=granted waited=30s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=228120ededb63bb65b7b3a1e71e8445968ce638f build=reused filter=/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213050-2d11/rows/CP-42/run.trx slot=granted waited=15s dirty=0 source=228120ededb63bb65b7b3a1e71e8445968ce638f sourceState=clean buildSource=verified

### 20261012-213642-7f08

CHECKPOINT CP-34 commit=31dfde92b40406145896255b8dff543f9beddcd7 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213642-7f08/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=31dfde92b40406145896255b8dff543f9beddcd7 sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=31dfde92b40406145896255b8dff543f9beddcd7 build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213642-7f08/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=31dfde92b40406145896255b8dff543f9beddcd7 sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=31dfde92b40406145896255b8dff543f9beddcd7 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-213642-7f08/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=31dfde92b40406145896255b8dff543f9beddcd7 sourceState=clean buildSource=verified

### 20261012-214511-9d96

CHECKPOINT CP-34 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=88145843b709b4427cec04d19c1965d50ced5cea build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-36/run.trx slot=granted waited=15s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-37/run.trx slot=granted waited=15s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-38/run.trx slot=granted waited=15s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-40/run.trx slot=granted waited=15s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-41/run.trx slot=granted waited=15s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-42/run.trx slot=granted waited=15s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=88145843b709b4427cec04d19c1965d50ced5cea build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ce4980da/.antiphon/checkpoints/20261012-214511-9d96/rows/CP-43/run.trx slot=granted waited=30s dirty=0 source=88145843b709b4427cec04d19c1965d50ced5cea sourceState=clean buildSource=verified

--- next stage ---
next: review
handoff: Review S7 at 88145843b709b4427cec04d19c1965d50ced5cea and clean CP-33..43 receipts, including early receipt, foreign identity, exact disposal and export races. All PC-1..152/W-1 remain pending; caller lands original Code task then commissions SourceLanding Mutation. Restart none.
artifact: docs/superpowers/plans/2026-10-12-card-1192-s7-code-evidence.md
