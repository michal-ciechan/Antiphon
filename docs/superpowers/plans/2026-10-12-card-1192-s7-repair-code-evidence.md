# CARD-1192 S7 round-2 Code repair evidence (0ee48ce0)

Historical round-2 proof: CP-45..52 cover registered finalizer probes after Compose and place C849_FIXTURE after successful finalization. Review 0a3d75f7 later found post-create unknown ownership dropping registration; round 4 adds CP-53 for that path. Fixture finalizer deletion is protected only where a named S7 test says so (work and mask volume acquisition ownership; unknown outcomes retained as residue). The inherited finalizer also deletes some objects by name or fixed roster (foreign cache volumes, same-name replacements behind legacy name-only markers, unacquired fixed-name consumers, and possibly others): the complete audit and fix are CARD-1208; Compose teardown is exit-status-only (CARD-1207); live Docker acceptance (W-1) is pending. Final closed scope passed 38/38, zero failures/skips. Review 2ec97f2d found a separate documentation F1: Compose teardown checks only down -v exit status, so residual Compose-created volumes after exit zero are not detected by this finalizer (inherited limitation, CARD-1207, outside S7). Live W-1 remains pending.

Original Code task / new landing owner: 0ee48ce0-372f-4a8d-af03-18197f3e85d7, transferred by the brief from ce4980da-9fb7-4ddc-821a-1ae22299bdf6.
Branch: feat/card-task-0ee48ce0.
Worktree: /work/worktrees/task-0ee48ce0; desktop checkout unavailable.
Task base: 276e0f30b4ca410ceb641924758fe201f49e7e50.
Actual final tested SHA: 59090d9f0860e34c27c8386445ab467d4a770f0a.
Historical red tested SHA: c83f9ca1656180cd3b7b76a95e213adc1d26edd1.
Plan: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md.
Evidence: docs/superpowers/plans/2026-10-12-card-1192-s7-repair-code-evidence.md.
Caller copy: /work/worktrees/task-0ee48ce0/.antiphon/task-0ee48ce0.md.
Restart: none. No live Fixture, rollout, deploy, old-run cleanup, land or Mutation.

## Changes and manual audit

scripts/c590-remote.sh now captures both probe streams and distinguishes present, positively absent and unknown. Exit 1 alone never means absence. An exact recognized missing-resource reply must match both kind and identity; all other nonempty lines must also be recognized (blank/JSON [] framing is allowed). Daemon/socket errors, exit 2, timeout 124, empty nonzero, permission errors, wrong names/kinds, mixed and unrecognized messages are unknown. Explicit volume get/no-such-volume, container no-such-object and network not-found forms are supported. Consumer/helper/work/mask/cache/network removal through the shared resource-removal helper is followed by an absence probe, even exit-zero removal. Compose down -v does not use that helper: its exit-status-only check does not detect residual Compose-created volumes (CARD-1207).

CP-45..52 exercise consumer/helper release, registered work/mask/cache removal and network finalization. The former blind consumer-removal loop uses the shared release path. Those selected unknown finalizer results write names/acquired IDs to residue and unknown-cleanup-outcome to fixture-cleanup-ledger.txt. They did not cover post-create acquisition; Review 0a3d75f7 demonstrated that missing registration/residue path, now assigned to CP-53. Markers and registration ledgers remain outside the removable controller. Primary diagnosis/status, later cleanup attempts and idempotent failed finalization remain intact; the finalizer scope statement applies.

The real evidence function had emitted C849_FIXTURE before cleanup. That marker now appears only after successful finalization, immediately before accepted publication. Summary/roster bytes remain unchanged. Documentation states refusal on unknown outcomes and pending W-1; the historical evidence note identifies its superseded cleanup verdict.

The test fixture and RemoteScriptContractTests execute actual strict wrapper/EXIT/finalizer, JSON writer and evidence/census function. External inventory/payload producers are substituted in the cleanup proof. Three successful Compose teardowns precede each fault. Seven exact boundary methods cover fourteen internal cases each (98) plus global daemon loss (99 modeled scenarios, eight TUnit methods). Independent filesystem observations retain both work/mask volumes and writer/reader helpers. No assertion or timeout was weakened. Plan adds CP-44..52 (After S7, wildcard namespace filters) and PC-153..172. Final report publication changes only Markdown; checkpoint filters and production/test source remain as tested.

Manual audit covered the finalizer consumer/helper/work/mask/cache/network absence/removal branches. It did not establish Compose-created volume absence; Review 2ec97f2d subsequently demonstrated that limitation (CARD-1207). Remaining code=1 namespace comparisons consume the shared tri-state result, never raw Docker status. Syntax, ASCII and Git diff checks passed. A gated acquired-ID diagnostic executed the real consumer release against unavailable Docker: both recorded name and distinct 64-hex ID were retained, with the ID in the unknown ledger. Native Docker/UID/archive/NuGet-lock behavior remains W-1; timeout is modeled at the CLI status boundary.

## Historical red and final qualification

Run 20261012-223718-483c: CP-45..52 each 1 executed, 0 passed, 1 failed, 0 skipped, at daemon-verdict. Fresh TRX exact class/method/assertion checked. Production in tests-first c83f9ca1656180cd3b7b76a95e213adc1d26edd1 is byte-identical to base 276e0f30b: both c590-remote.sh blobs are 7cb0a1b9d07c661ba23fffd1b4e1e051cb6e4417. Tests did not exist in the old commit, so this preserves exact baseline production while reporting the actual tests-first repository SHA.

Global baseline observation: daemon-exit=0; receipt={"accepted":true,"diagnosis":"","exit":0}; daemon-teardown=1; daemon-verdict=0; daemon-residue=0; daemon-ledger=0; daemon-all-resources-retained=1; all four exact work/helper residue predicates=0. Per-boundary tables also observed empty/permission/socket/unrecognized exit-1 fail-open and exit-zero removal-with-retained-object fail-open. Exit 2/timeouts refused but lacked unknown ledger entries.

Marker red at 1aa20e201 (production still 87e246ecb): actual evidence emitted a premature marker while receipt was accepted:false and exit=2. Observed daemon-teardown=1, daemon-marker=0, daemon-verdict=0, and all retained-resource/residue/ledger predicates=1. Gated ordinary historical observation, not a deliberate PC cycle.

Case -> mutation -> red: unknown exit 1 at each boundary -> substitute absence there -> daemon-verdict (all eight methods historically red); exit-zero removal retaining an object -> skip post-inspection -> rm-lie-verdict (historical facts 0); omit ledger -> daemon-ledger; admit exit-2 missing text/wrong name/kind/socket/mixed reply -> corresponding table verdict; move marker before cleanup -> daemon-marker (historically witnessed). The plan binds all mutations and expected assertions; deliberate patch/red/restore/green is pending Mutation.

Final run 20261012-224830-b566: exit 0, wall 368s. One S7 isolated build executed; seven definitions imported, six unused. UseAppHost=false, bin-c1192-s7/, no build ID named tests, 0 errors/695 existing warnings. Build slot=granted waited=0s. Every final row slot=granted waited=0s.

Fresh TRX: 20 distinct TestRun IDs, 38 distinct execution IDs, exact RemoteScriptContractTests class/intended method; CP-44 has exactly 19 admitted adjacent methods. All Passed, zero skipped. The mandated adjacent selection overlaps four S7 methods. Receipt validator: CHECKPOINT SOURCE VALID source=59090d9f0860e34c27c8386445ab467d4a770f0a rows=20. All final lines and structured receipts use 59090d9f0860e34c27c8386445ab467d4a770f0a, dirty=0, sourceState=clean, buildSource=verified. Report-only publication changes HEAD; no receipt is relabeled.

## Counts

| CP | executed/passed/failed/skipped | slot/waited |
|---|---|---|
| CP-33 | 1/1/0/0 | granted/0s |
| CP-34 | 1/1/0/0 | granted/0s |
| CP-35 | 1/1/0/0 | granted/0s |
| CP-36 | 1/1/0/0 | granted/0s |
| CP-37 | 1/1/0/0 | granted/0s |
| CP-38 | 1/1/0/0 | granted/0s |
| CP-39 | 1/1/0/0 | granted/0s |
| CP-40 | 1/1/0/0 | granted/0s |
| CP-41 | 1/1/0/0 | granted/0s |
| CP-42 | 1/1/0/0 | granted/0s |
| CP-43 | 1/1/0/0 | granted/0s |
| CP-44 | 19/19/0/0 | granted/0s |
| CP-45 | 1/1/0/0 | granted/0s |
| CP-46 | 1/1/0/0 | granted/0s |
| CP-47 | 1/1/0/0 | granted/0s |
| CP-48 | 1/1/0/0 | granted/0s |
| CP-49 | 1/1/0/0 | granted/0s |
| CP-50 | 1/1/0/0 | granted/0s |
| CP-51 | 1/1/0/0 | granted/0s |
| CP-52 | 1/1/0/0 | granted/0s |

## Required V/R dispositions

| ID | Actual scoped outcome |
|---|---|
| V-1 | Acquisition exercised by CP-33; full earlier CP-1/3 variants not separately rerun. |
| V-2 | PASS S7 disposal/unknown/refusal/ordering, CP-35/45..52 plus acquired-ID audit; earlier S1 scope excluded. |
| V-3 | S2 full matrix not rerun; outside brief. |
| V-4 | S3 full matrix not rerun; outside brief. |
| V-5 | PASS CP-33 actual fixture seed recipients; full earlier backend matrix not rerun. |
| V-6 | PASS CP-33 actual recovery validation; earlier CP-21/22/25 matrix not rerun. |
| V-7 | PASS adjacent C1195 root/import/retention; full CP-27 matrix not rerun. |
| V-8 | PASS CP-33/36/41/42 payload/evidence/census/privacy. |
| V-9 | PASS CP-34/45..52 primary failure, strict EXIT, actual JSON, cleanup/marker verdict. |
| R-1 | PASS CP-37 and selected adjacent C1189 image-tool/unlink contracts. |
| R-2 | PASS CP-35 and selected adjacent C1191 ordering/failure contracts. |
| R-3 | PASS CP-38/39 and selected adjacent C1193 environment/log contracts. |
| R-4 | PASS selected adjacent C1194 budget contracts. |
| R-5 | PASS selected adjacent C1195 root/mode/retention contracts. |
| R-6 | Earlier C1066 CP-14/24/32 matrices not rerun; outside brief. |
| R-7 | PASS CP-43 fixture inventory/recipient and CP-33 seed observations; earlier host/saved/prune methods not rerun. |
| R-8 | PASS CP-40..43 lane/receipts/inventory. |
| W-1 | NOT RUN; rollout-owned native acceptance pending after Review/land. |

Final for the explicit closed repair brief. Whole Unit lane/full RemoteScriptContractTests class are prohibited here and not claimed. Landed S1-S6 CP-1..32 are excluded, not silently passed or deferred-to-final. No required ordinary row remains deferred. W-1/PCs retain separate owners.

All PC-1..PC-172 and every variant are PENDING method-scoped SourceLanding Mutation. New PC-153..159 retain fourteen variants each; PC-160 four work/helper identity variants; PC-161..167 successful-removal/object-present; PC-168 exit-2 missing text; PC-169 wrong name; PC-170 wrong kind/socket; PC-171 mixed output; PC-172 premature marker. Historical red/ordinary green/nightly discharge none. Mutation owns deliberate mutants, restoration/green and missing-control discovery. No unbound candidate under docs/superpowers/mutations/ was commissioned. Caller reviews and lands this original repair Code task, then commissions SourceLanding Mutation.

## Drivers, setup and custody

Extra gated drivers and reasons: checkpoint-tool bootstrap needed in this mirror (bin-c1192-r2-tool/, UseAppHost=false, granted/0s, 1 existing warning/0 errors); exact-baseline daemon diagnostic (granted/45s); repaired all-boundary diagnostic (granted/0s, 99 cases, held 85s); actual-marker red (granted/0s); acquired-ID manual audit (granted/0s). These are diagnostic/manual facts, not additional credited CPs.

Early importer launches refused before drivers (table blank-line/OR escaping/selection issues). Run 20261012-223022-dccd was stopped and awaited after a launcher argument-order error; no guarded red credit. Run 20261012-224447-4bfd was stopped and awaited during build to repair the premature success marker; no methods credited. Both terminal waits returned 6 with executor dead/no phase=done. No source edit occurred under a running test/build. The unrelated C913 launcher was restored before any subsequent build. Valid historical red and Final green supersede these setup runs.

No widened timeout, loosened assertion, retry/known-flaky policy, Repeat flag or loaded repetition. Completed proof selections ran once per changed source state; green was not repeated. All owned commands/stops/waits finished. GET /api/runner-defaults revision 2 and GET /api/session-runners showed eligible Linux placement; no Runner/Platform pin. Task token present, never printed; checkpoint owner guard active.

Green cleanup removed task-created compiled bin-c1192-s7/ outputs. Bootstrap output removed separately after validation. Direct diagnostics cleaned private roots. Ignored TRX/JSON/log/build-receipt folders remain under .antiphon/checkpoints; none are committed or relocated. Only individual Markdown evidence is published.

Fetched origin/master=4f252abf4a5f41057cf871d04bc93cf8da96640a; merge-tree at tested HEAD exit 0, tree=63cdcb48ff872d5b863b72cbe84948bcab10f942. Publication SHA/remote equality/full task-range evidence guard are appended to the caller copy after this report commit. All meaningful slices pushed fast-forward-only; no amend/rebase/reset/force push.

Rerun: gated checkpoint-tool bootstrap with bin-c1192-r2-tool/ and UseAppHost=false; run its DLL with run --plan docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md --after S7 --expected-source-sha <committed HEAD> --max-wait 45s. Continue wait <run-id> --max-wait 45s for every exit 75 until terminal; validate report.json against actual tested SHA. The tool gates its own isolated S7 build and exact rows.

## Unedited historical CHECKPOINT lines

CHECKPOINT CP-45 commit=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 build=ok filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-223718-483c/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-223718-483c/rows/CP-46/run.trx slot=granted waited=0s dirty=0 source=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-223718-483c/rows/CP-47/run.trx slot=granted waited=0s dirty=0 source=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-223718-483c/rows/CP-48/run.trx slot=granted waited=0s dirty=0 source=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-223718-483c/rows/CP-49/run.trx slot=granted waited=0s dirty=0 source=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-223718-483c/rows/CP-50/run.trx slot=granted waited=0s dirty=0 source=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-223718-483c/rows/CP-52/run.trx slot=granted waited=0s dirty=0 source=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-223718-483c/rows/CP-51/run.trx slot=granted waited=0s dirty=0 source=c83f9ca1656180cd3b7b76a95e213adc1d26edd1 sourceState=clean buildSource=verified

## Unedited Final CHECKPOINT lines

CHECKPOINT CP-34 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/(C1189_*|C1191_*|C1193_*|C1194_*|C1195_*|C1192_Summary_*|C1192_Foreign_*|C1192_Harness_*|C1192_Helper_*)* executed=19 passed=19 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-46/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-47/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-48/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-49/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-50/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-52/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=59090d9f0860e34c27c8386445ab467d4a770f0a build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-0ee48ce0/.antiphon/checkpoints/20261012-224830-b566/rows/CP-51/run.trx slot=granted waited=0s dirty=0 source=59090d9f0860e34c27c8386445ab467d4a770f0a sourceState=clean buildSource=verified
