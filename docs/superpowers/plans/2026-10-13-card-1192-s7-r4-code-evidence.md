# CARD-1192 S7 round-4 Code repair evidence (16459e79)

Fixed Review 0a3d75f7 F1: unknown post-create work/mask ownership retains registration and cleanup records. The commissioned Final repair passed 39/39 executions, zero failures/skips. W-1 live acceptance is NOT claimed.

Landing owner for this repair: 16459e79-5010-458e-adf4-0e03b65b98ee (this Code task), transferred by the brief from d25ba49f-2fde-4413-8cba-a3ad72165dc7.
Branch: feat/card-task-16459e79.
Runner worktree: /work/worktrees/task-16459e79.
Desktop checkout: C:\Antiphon\worktrees\card-task-16459e79 (not reachable here).
Task base: 137549874ebffe62afe17cd35d58c648a47fdfff; fast-forward-only, no rebase/reset/amend.
Tests-first slice: 1bc9c827057da05c36556a1fe81efd63512dae88, pushed.
Fix/docs slice and actual tested SHA: 3f4a48eb15456ecfc22304306e6ac8634d92d7ea, pushed.
Final report/documentation publication advances HEAD; its full SHA is in the completion and observed remote progress line. Executable source and tests remain byte-identical to the tested slice. Receipts are not relabeled.
Plan: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md.
Evidence: docs/superpowers/plans/2026-10-13-card-1192-s7-r4-code-evidence.md.
Caller copy: /work/worktrees/task-16459e79/.antiphon/task-16459e79.md.
Restart: none. No land, deployment, live Fixture, old-run removal or deliberate Mutation.

## Implementation and documentation

The work/mask volume registration already preceded create. The defect was dropping that registration for every nonzero ownership result. C1192_Foreign_volume_is_refused_after_create (CP-44) checks that c849_fixture_workspace_create_volume drops registration for wrong work/mask labels (status 3) without mounting or deleting those volumes. Own labels (0) continue; confirmed disappearance (1) refuses while retaining the name; unknown ownership (2) retains the name, records unknown-cleanup-outcome and refuses. Failed driver/options probes also retain an uncertainty record. Cleanup files are initialized before acquisition and preserved at finalizer entry, so a later successful probe/removal cannot erase a one-probe acquisition outage.

CP-53 executes production acquisition, strict wrapper, EXIT, finalizer and JSON publisher against a filesystem Docker boundary. Its four internal cases are work/mask crossed with persistent daemon loss and one failed owner-label probe. It independently checks nonzero/accepted:false, no success marker/receipt, pre-create registration, retained .fixture-volumes registration, the name in fixture-cleanup-residue.txt and unknown-cleanup-outcome in fixture-cleanup-ledger.txt. Persistent outages also require the modeled volume still present. Four scenarios are one TUnit execution, not four tests. CP-44's work/mask confirmed-own/foreign and other assertions are unchanged; no existing assertion or timeout was weakened.

Acquisition audit (grep plus surrounding code):
- Work and mask share workspace_create_volume: fixed the unknown-result drop, registration precedes creation/verification.
- Writer/reader helper_acquire and S6 masked/apphost helpers register .fixture-helpers before docker create/start; helper release keeps unknown names in residue. No same post-verification drop.
- Prepare/seed/race/apphost/npm/contract-control cache names, including the busy cache, enter .fixture-volumes before c849_volume creates/verifies them. Their registries are not dropped on failed verification; the finalizer scope statement applies.
- npm network writes .fixture-network immediately after successful create, before its finalizer inspect. npm-http, donor, busy consumer and prune-smoke markers precede their subsequent start/admission/smoke checks. There is no ownership probe between create acknowledgement and those registrations. These older acknowledgement-dependent acquisitions were inspected, not recast as positive-absence proof.
- Fixed-name restore/lock/npm consumers have an explicit finalizer name roster. Anonymous --rm helpers and Compose-created objects are outside the newly modeled acquisition proof.
No other instance of the work/mask post-ownership-registration drop was found; no unrelated acquisition semantics were changed.

Replaced broad claims in docker-stack.md, the plan, all three historical S7 evidence notes, shell comments and the cleanup-test comment with named CP scopes and known exceptions. The round-4 documentation commit labels old scope/census text historical and narrows D-5 to workspace/helper acquisition. Historical CHECKPOINT records remain untouched.

Fixture finalizer deletion is protected only where a named S7 test says so (work and mask volume acquisition ownership; unknown outcomes retained as residue). The inherited finalizer also deletes some objects by name or fixed roster (foreign cache volumes, same-name replacements behind legacy name-only markers, unacquired fixed-name consumers, and possibly others): the complete audit and fix are CARD-1208; Compose teardown is exit-status-only (CARD-1207); live Docker acceptance (W-1) is pending.

## Guarded-defect red

Checkpoint run 20261012-235635-40c4: terminal wait exit 1, wall 198.43s. CP-53: executed=1 passed=0 failed=1 skipped=0, slot=granted waited=15s. The failure is the intended Shouldly assertion work-daemon-registered: expected 1, actual 0, not a build/fixture error.

Tests did not exist at the original base. The tests-first commit runs the exact base production: both 137549874 and 1bc9c827 c590-remote.sh blobs are 52291750f82721d05007511d79b3ad349dac0167. Actual repository/source receipt SHA is 1bc9c827057da05c36556a1fe81efd63512dae88; it is not relabeled 137549874.

Observed failing output:
```text
work-daemon exit=2 receipt={"accepted":false,"diagnosis":"FixtureWorkspaceForeign","exit":2}
work-daemon-verdict=1
work-daemon-registered=0
work-daemon-residue=0
work-daemon-ledger=0
work-daemon-create-registered=1
work-daemon-retained=1
work-label-once-registered=0
work-label-once-residue=0
work-label-once-ledger=0
mask-daemon-registered=0
mask-daemon-residue=0
mask-daemon-ledger=0
mask-daemon-retained=1
mask-label-once-registered=0
mask-label-once-residue=0
mask-label-once-ledger=0
```

Fresh baseline TRX audit: one intended RemoteScriptContractTests method, one Failed result, fresh timestamp and distinct execution ID. Source/build snapshots are clean/verified. The red receipt is diagnostic and is not claimed green. One isolated bin-c1192-s7/ build, UseAppHost=false, maxcpucount=6, slot=granted waited=30s, 0 errors/694 warnings; six imported build definitions unused. This is ordinary historical red, not a deliberate PC cycle.

## Final verification

Checkpoint run 20261013-000218-64d0: awaited every exit-75 continuation through terminal exit 0, wall 392.30s. One isolated bin-c1192-s7/ build, UseAppHost=false, forward-slash trailing output, maxcpucount=6, slot=granted waited=0s, 0 errors/695 warnings. Six other imported build definitions were unused; no build ID named tests. All rows acquired granted leases; no unleased/NoSlot driver. One transient owner read returned HTTP 502; bound execution recovered without bypass or test retry.

Independent XML audit verified exact per-row rosters, 21 distinct TestRun IDs, 39 distinct execution IDs, actual Passed outcomes and timestamps after run start. CP-44 matched the literal 19-method roster. There are 35 distinct methods because four mandated adjacent methods overlap exact rows; no census inflation. Receipt validator: CHECKPOINT SOURCE VALID source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea rows=21. All final lines and structured receipts are dirty=0, sourceState=clean, buildSource=verified.

| CP | executed/passed/failed/skipped | slot= | waited= |
|---|---|---|---|
| CP-33 | 1/1/0/0 | granted | 0s |
| CP-34 | 1/1/0/0 | granted | 0s |
| CP-35 | 1/1/0/0 | granted | 0s |
| CP-36 | 1/1/0/0 | granted | 0s |
| CP-37 | 1/1/0/0 | granted | 0s |
| CP-38 | 1/1/0/0 | granted | 0s |
| CP-39 | 1/1/0/0 | granted | 0s |
| CP-40 | 1/1/0/0 | granted | 15s |
| CP-41 | 1/1/0/0 | granted | 30s |
| CP-42 | 1/1/0/0 | granted | 15s |
| CP-43 | 1/1/0/0 | granted | 0s |
| CP-44 | 19/19/0/0 | granted | 0s |
| CP-45 | 1/1/0/0 | granted | 0s |
| CP-46 | 1/1/0/0 | granted | 15s |
| CP-47 | 1/1/0/0 | granted | 30s |
| CP-48 | 1/1/0/0 | granted | 30s |
| CP-49 | 1/1/0/0 | granted | 30s |
| CP-50 | 1/1/0/0 | granted | 30s |
| CP-51 | 1/1/0/0 | granted | 30s |
| CP-52 | 1/1/0/0 | granted | 15s |
| CP-53 | 1/1/0/0 | granted | 15s |

## Every V/R disposition in the explicit repair scope

| ID | Actual outcome |
|---|---|
| V-1 | PASS CP-53 post-create unknown registration/records, plus acquisition exercised by CP-33; full S1 matrix excluded. |
| V-2 | PASS CP-35/45..53 registered disposal, uncertainty and refusal; Compose residue detection excluded (CARD-1207). |
| V-3 | NOT RERUN: full S2 race/barrier matrix outside this brief. |
| V-4 | NOT RERUN: full S3 npm matrix outside this brief. |
| V-5 | PASS CP-33 actual fixture seed recipients; earlier full backend matrix excluded. |
| V-6 | PASS CP-33 recovery validation; earlier full recovery-control matrix excluded. |
| V-7 | PASS CP-44 C1195 root/import/retention; full CP-27 matrix excluded. |
| V-8 | PASS CP-33/36/41/42 payload/evidence/census/privacy. |
| V-9 | PASS CP-34/45..53 actual finalization/primary failure/strict EXIT/JSON/marker; detected failures only, known exceptions above. |
| R-1 | PASS CP-37/44 C1189 image-tool/unlink guards. |
| R-2 | PASS CP-35/44 C1191 ordering/failure/disposal guards. |
| R-3 | PASS CP-38/39/44 C1193 environment/log guards. |
| R-4 | PASS CP-44 C1194 budget guards. |
| R-5 | PASS CP-44 C1195 root/mode/retention guards. |
| R-6 | NOT RERUN: C1066 argument-expanded matrices outside brief. |
| R-7 | PASS CP-43 fixture inventory/recipient and CP-33 seed; earlier host/saved/prune matrices excluded. |
| R-8 | PASS CP-40..44 lane/receipts/inventory/adjacent contracts. |
| W-1 | NOT RUN; pending rollout-owned live acceptance after Review/land. |

Final for the explicit repair scope CP-33..53; no required ordinary row is deferred to Final. Whole Unit, full RemoteScriptContractTests, S1-S6 matrices and live Fixture were expressly prohibited by the brief and are not claimed passed. No full-assembly driver was run; no unbounded class selection. Required manual work completed: registration/probe audit, wording/known-exception audit, syntax/whitespace, fresh XML/source validation, merge-tree and evidence-history guards.

## Mutation, custody and publication

All PC-1..PC-174 and every plan variant remain PENDING method-scoped SourceLanding Mutation. PC-153..159: fourteen variants each; PC-160: four identity variants; PC-161..167: exit-zero removal/object-present; PC-168: exit-2 missing text; PC-169: wrong name; PC-170: wrong kind/socket; PC-171: mixed output; PC-172: premature marker. New PC-173 has four work/mask x persistent/single-probe variants; PC-174 has two work/mask single-probe record-erasure variants. Earlier PC-1..152 variants remain as enumerated in the plan. Post-land Mutation was NOT run. Mutation owns deliberate mutants, red/restore/green and missing-control discovery; no unbound candidate was commissioned under docs/superpowers/mutations/.

Only extra build driver: gated checkpoint-tool bootstrap required by this fresh mirror, bin-c1192-r4-tool/, UseAppHost=false, slot=granted waited=15s, 0 errors/1 warning. One initial no-build tool launch used the default apphost setting and failed before launching the checkpoint tool; corrected UseAppHost=false before any checkpoint run. No additional test selection, repetition, loaded repeat, retry, widened timeout or weakened assertion. Every checkpoint state ran at a committed SHA with source frozen throughout.

GET /api/runner-defaults returned revision 2; GET /api/session-runners returned the catalogue and eligible Linux placement. No Runner/Platform pin or fleet location embedded.

Fetched origin/master=118bba94f1dae3499b84260b26d062cd75e1a6c4. Merge-tree at tested source exited 0, tree=c4a9e4e13b6163d65d827a8c404e0b65093e8627. No merge/rebase performed. Evidence-diff at tested source passed both task base..HEAD (2 commits/0 entries/0 violations) and complete inherited S7 candidate base 2b5aa6aeeaa3f26062b59609019e92d43575105d..HEAD (20 commits/0 entries/0 violations). Both are rerun after this report/documentation commit; terminal output is copied to the caller report, with final fetched-master merge-tree result.

After both runs terminated, 29 task-created output directories were inventoried, checked for nonempty allowed names, canonical containment, real-directory/non-link identity and task-time birth records, then removed. Default bin/ and obj/ were untouched. Terminal wait removed each executor shadow tool. Raw TRX/JSON/logs remain ignored in the two original .antiphon/checkpoints run directories; no generated payload moved into source control. Essential CHECKPOINT lines remain unedited below.

## Rerun commands

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1192-r4-review-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1192-r4-review-tool/ --property:UseAppHost=false --nologo
$sha = git rev-parse HEAD
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1192-r4-review-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md --after S7 --expected-source-sha $sha --max-wait 45s
# On exit 75, continue wait --run <printed-run-id> --max-wait 45s until terminal.
```

## Unedited CHECKPOINT lines

Baseline:
```text
CHECKPOINT CP-53 commit=1bc9c827057da05c36556a1fe81efd63512dae88 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Acquisition_unknown_ownership_retains_registration_and_residue executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261012-235635-40c4/rows/CP-53/run.trx slot=granted waited=15s dirty=0 source=1bc9c827057da05c36556a1fe81efd63512dae88 sourceState=clean buildSource=verified
```

Final:
```text
CHECKPOINT CP-34 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-40/run.trx slot=granted waited=15s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-41/run.trx slot=granted waited=30s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-42/run.trx slot=granted waited=15s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/(C1189_*|C1191_*|C1193_*|C1194_*|C1195_*|C1192_Summary_*|C1192_Foreign_*|C1192_Harness_*|C1192_Helper_*)* executed=19 passed=19 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-46/run.trx slot=granted waited=15s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-47/run.trx slot=granted waited=30s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-48/run.trx slot=granted waited=30s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-49/run.trx slot=granted waited=30s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-50/run.trx slot=granted waited=30s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-51/run.trx slot=granted waited=30s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-52/run.trx slot=granted waited=15s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=3f4a48eb15456ecfc22304306e6ac8634d92d7ea build=reused filter=/*/*/RemoteScriptContractTests/C1192_Acquisition_unknown_ownership_retains_registration_and_residue executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-16459e79/.antiphon/checkpoints/20261013-000218-64d0/rows/CP-53/run.trx slot=granted waited=15s dirty=0 source=3f4a48eb15456ecfc22304306e6ac8634d92d7ea sourceState=clean buildSource=verified
```
