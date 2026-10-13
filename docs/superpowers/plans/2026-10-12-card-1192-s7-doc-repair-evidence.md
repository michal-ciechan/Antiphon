# CARD-1192 S7 round-3 documentation repair evidence (d25ba49f)

Historical round-3 wording was rejected by Review 0a3d75f7 F1: post-create unknown ownership lost registration and residue. Round 4 adds CP-53 and repairs that path. Fixture finalizer deletion is protected only where a named S7 test says so (work and mask volume acquisition ownership; unknown outcomes retained as residue). The inherited finalizer also deletes some objects by name or fixed roster (foreign cache volumes, same-name replacements behind legacy name-only markers, unacquired fixed-name consumers, and possibly others): the complete audit and fix are CARD-1208; Compose teardown is exit-status-only (CARD-1207); live Docker acceptance (W-1) is pending. The historical CHECKPOINT records below are unchanged.

Review 2ec97f2d F1 documentation claim was narrowed. The executable Compose
limitation remains inherited and separately owned by CARD-1207. Closed repair
verification passed 38/38 executions, zero failures/skips. W-1 live acceptance
is pending; this is not live Fixture acceptance.

Original Code task / current landing owner: d25ba49f-2fde-4413-8cba-a3ad72165dc7,
transferred by this brief from 0ee48ce0-372f-4a8d-af03-18197f3e85d7.
Branch: feat/card-task-d25ba49f.
Worktree: /work/worktrees/task-d25ba49f; desktop checkout unavailable.
Task base: 4b4ad8c8f4335f22214c0dbde6e6eca986018e78.
Actual tested SHA / expected source SHA: a6e37d459d030ced87ac3c8af0047d9767a9604b.
Plan: docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md.
Prior evidence: docs/superpowers/plans/2026-10-12-card-1192-s7-repair-code-evidence.md.
Caller copy: /work/worktrees/task-d25ba49f/.antiphon/task-d25ba49f.md.
Restart: none. No land, deployment, live Fixture or deliberate Mutation.

## Documentation and comment audit

Searched docker-stack.md, the plan, prior S7 repair evidence, c590-remote.sh
comments and RemoteScriptContractTests comments for cleanup/absence claims using
every/all/always/never/cannot/guarantee/confirm and cleanup/removal/residue/absence/
finalization/disposal/resource/teardown terms; read surrounding S7 paragraphs.
Unrelated deployment/recycling policies were retained in their own scope.

Changed claims:

- docker-stack.md: replaced the general temporary-resource absence claim with
  acquisition/removal confirmation (subsequently rejected for acquisition by
  Review 0a3d75f7; round 4 uses the named CP-45..53 scopes instead); qualified removal acknowledgements, detected failures
  and consumer-release attempts. Explicitly documented Compose exit-status-only
  teardown and undetected residual Compose-created volumes after down -v succeeds.
- c590-remote.sh: scoped the removal-acknowledgement comment to its shared helper;
  scoped the repeated-finalization comment to consumer release; added the Compose
  exception immediately before the finalizer; changed quiesce-every-consumer to
  attempt release of the listed consumers. W-1 remains pending.
- Plan: qualified D-6/V-9 and resource-free completion language; recorded the
  enforced boundaries and CARD-1207 exclusion. W-1 requires independent observation
  of Compose-created volumes too; finalizer success does not supply that evidence.
- Prior evidence: qualified its opening verdict, every-removal claim and broad
  manual audit claim. Historical CHECKPOINT lines and tested SHA remain unchanged.
- RemoteScriptContractTests: scoped the S1 cleanup comment to its registered
  cache/work/mask proof and stated the Compose exception.

No executable shell or C# test lines changed. A comparison against the task base
excluding full-line shell/C# comments was byte-identical after newline joining:

```text
COMMENTS-IGNORED DIFF scripts/c590-remote.sh identical sha256=cb130c3b26182e666952455a6a559bdb654710408d53318edb673ae119cdd9e4
COMMENTS-IGNORED DIFF tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs identical sha256=1bce9dbac2227a46ae3ce5127e9b86caa316476a8cb6de23c09661b826cbad9d
```

Implementation diff --stat: 5 files, 54 insertions, 24 deletions; Markdown and
comments only. bash -n and git diff --check passed. No tests or assertions were
added or changed, so no new red proof is claimed. The previous evidence retains
historical guarded-defect red observations; intentional PC cycles remain pending.

## Final closed repair verification

Checkpoint run 20261012-232353-08bc reached terminal exit 0 after all exit-75
waits were awaited. Wall 294.82s. One S7 build ran, six other imported definitions
were unused. bin-c1192-s7/, UseAppHost=false, maxcpucount=6, 0 errors/694 warnings;
build slot=granted waited=0s. No build ID named tests. No broad Unit lane or full
RemoteScriptContractTests class was run, as explicitly prohibited by this brief.

Fresh TRX inspection checked each row's intended class/method, actual Passed
results, nonzero/exact counts and start time after the run start. CP-44 contains
exactly 19 admitted adjacent methods. The 38 executions overlap four methods
between CP-44 and the exact rows; no distinct-method census inflation is claimed.
Receipt validator returned CHECKPOINT SOURCE VALID for the actual tested SHA,
rows=20: dirty=0, sourceState=clean, buildSource=verified. Evidence/report-only
publication follows testing; receipts are not relabeled to the report commit.

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
| CP-44 | 19/19/0/0 | granted | 0s |
| CP-45 | 1/1/0/0 | granted | 0s |
| CP-46 | 1/1/0/0 | granted | 0s |
| CP-47 | 1/1/0/0 | granted | 0s |
| CP-48 | 1/1/0/0 | granted | 0s |
| CP-49 | 1/1/0/0 | granted | 0s |
| CP-50 | 1/1/0/0 | granted | 0s |
| CP-51 | 1/1/0/0 | granted | 0s |
| CP-52 | 1/1/0/0 | granted | 0s |

## V/R and acceptance dispositions

| ID | Actual outcome in the explicit repair scope |
|---|---|
| V-1 | CP-33 acquisition exercised; full S1 acquisition matrix excluded. |
| V-2 | PASS S7 disposal/unknown/refusal/ordering, CP-35/45..52; Compose residue detection excluded (CARD-1207). |
| V-3 | S2 matrix not rerun; outside brief. |
| V-4 | S3 matrix not rerun; outside brief. |
| V-5 | PASS CP-33 actual fixture seed recipients; earlier full backend matrix excluded. |
| V-6 | PASS CP-33 recovery validation; earlier full recovery-control matrix excluded. |
| V-7 | PASS CP-44 adjacent C1195 root/import/retention; full CP-27 matrix excluded. |
| V-8 | PASS CP-33/36/41/42 payload/evidence/census/privacy. |
| V-9 | PASS CP-34/45..52 finalization/primary failure/strict EXIT/JSON/marker; detected failures only, Compose limitation explicit. |
| R-1 | PASS CP-37 and CP-44 C1189 image-tool/unlink methods. |
| R-2 | PASS CP-35 and CP-44 C1191 ordering/failure methods. |
| R-3 | PASS CP-38/39 and CP-44 C1193 environment/log methods. |
| R-4 | PASS CP-44 C1194 budget methods. |
| R-5 | PASS CP-44 C1195 root/mode/retention methods. |
| R-6 | Earlier C1066 matrices not rerun; outside brief. |
| R-7 | PASS CP-43 fixture inventory/recipient and CP-33 seed observations; earlier full host/saved/prune methods excluded. |
| R-8 | PASS CP-40..43 lane/receipts/inventory and CP-44 adjacent contracts. |
| W-1 | NOT RUN; pending rollout-owned live acceptance after Review/land. |

Final for the explicit documentation repair scope. No required ordinary row is
deferred to Final. S1-S6/full matrices excluded by the brief are not claimed
passed. Required manual work was the wording/behavior-boundary audit, comment-
ignored comparison, syntax/whitespace check and merge-tree check; all completed.

All PC-1..PC-172 and every variant remain PENDING method-scoped SourceLanding
Mutation. PC-153..159: fourteen variants each; PC-160: four work/helper identity
variants; PC-161..167: successful-removal/object-present; PC-168: exit-2 missing
text; PC-169: wrong name; PC-170: wrong kind/socket; PC-171: mixed output;
PC-172: premature marker. Post-land Mutation was NOT run. Mutation owns deliberate
mutants, red/restore/green and missing-control discovery. No unbound candidate
under docs/superpowers/mutations/ was commissioned.

## Custody and rerun

The sole extra build was the gated checkpoint-tool bootstrap, needed because
this fresh mirror had no compiled tool: bin-c1192-r3-tool/, UseAppHost=false,
slot=granted waited=0s, 0 errors/1 warning. Every checkpoint build/row was gated.
No rerun, known-flaky policy, Repeat flag, loaded repetition, widened timeout or
loosened assertion. Source stayed frozen throughout the run. Tool green cleanup
removed its S7 outputs and terminal wait removed its executor shadow copy;
the bootstrap output is removed separately. Generated TRX/JSON/logs remain
ignored in their original .antiphon/checkpoints directory.

GET /api/runner-defaults returned revision 2; GET /api/session-runners confirmed
eligible Linux placement. No Runner/Platform pin or fleet location embedded.

Fetched origin/master=0973be4121b89249f092a6a5f4e9542797b86a2c. Merge-tree at
tested source exit 0, tree=862cc5f2a56467ed84fdc9adae50b08678198cce.
Full task-range check-evidence-diff is run again after the evidence commit;
final publication SHA and remote equality are in the caller copy/final report.
All slices are pushed fast-forward-only; no rebase/amend/reset/force push.

Rerun: build the checkpoint tool through scripts/build-slot.ps1 with
UseAppHost=false and bin-c1192-r3-tool/, then run its DLL with run --plan
docs/superpowers/plans/2026-10-11-card-1192-fixture-work-volumes-plan.md --after S7
--expected-source-sha <committed HEAD> --max-wait 45s. Continue wait <run-id>
--max-wait 45s until terminal. Validate report.json against the actual tested SHA.

## Unedited CHECKPOINT lines

CHECKPOINT CP-34 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Finalization_precedes_success_and_preserves_primary_failure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=ok filter=/*/*/RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1191_Fixture_exit_removes_owned_workspace_without_host_sudo executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Evidence_export_preserves_fixture_roster_without_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1189_Fixture_escape_link_is_removed_after_pc08_and_on_exit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_exit_trap_copies_race_logs_before_tree_removal executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1193_Fixture_log_copy_skips_symlinks_and_copies_regular_files executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C849_Cache_receipts_exclude_credentials_and_payloads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C913_Fixture_uses_owned_payloads_and_proven_absent_temp executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C913_Receipts_reject_mixed_digest_types_and_false_smoke_claims executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_consumer_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/(C1189_*|C1191_*|C1193_*|C1194_*|C1195_*|C1192_Summary_*|C1192_Foreign_*|C1192_Harness_*|C1192_Helper_*)* executed=19 passed=19 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_helper_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-46/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_masked_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-47/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_work_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-48/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_mask_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-49/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_cache_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-50/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Daemon_loss_after_compose_refuses_success executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-52/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=a6e37d459d030ced87ac3c8af0047d9767a9604b build=reused filter=/*/*/RemoteScriptContractTests/C1192_Cleanup_network_requires_positive_absence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d25ba49f/.antiphon/checkpoints/20261012-232353-08bc/rows/CP-51/run.trx slot=granted waited=0s dirty=0 source=a6e37d459d030ced87ac3c8af0047d9767a9604b sourceState=clean buildSource=verified
