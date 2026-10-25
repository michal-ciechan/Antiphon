Qualification incomplete at the 75-minute budget: three fixture defects repaired and pushed; 167 passed, 10 expected Linux skips, 27 incomplete/interrupted. No group-71 green claim.

# Run group 71 Debug evidence

Assigned branch: feat/card-task-b6d3db3b. Started at fetched origin/master 0d51344da28bcb5dea4f37a86c7434bc4c606da4. No rebase, merge, tags, deployment, native desktop lock or desktop process was used. The caller's standing authority covered these fixture repairs; no additional approval was requested.

## Changes and attribution

- d86e3b3d08ab07bd2931e1e190780cb223ee157e — RetiredTempContainerHostTests.C994_Rollout_lock_covers_final_census reproduced at the base with its first barrier absent after the existing 15-second wait. Use the actual empty-census path, which still takes the real rollout flock and persists the final receipt, instead of paying unrelated container-generation/removal proofs inside the concurrency barrier. Every assertion and both 15/10-second waits remain. Green at d86e3b3d08ab07bd2931e1e190780cb223ee157e: 1/1, 7.64s.
- 79eb2ab4e78b742063ab8202533084ccf4c7d65e — RunCheckpointSourceScriptTests.C835_TerminalEvidence reproduced refusal `pre-existing-build-source` where it expected the zero-test verdict. The fixture reused a build output that current exclusive output ownership correctly refuses. Each build now selects a fresh output; NoBuild retains the most recently selected output. The interruption witness collides with the next run log after admission, preserving every existing interruption/source/receipt assertion. Production checkpoint safety is unchanged.
- 10f6d87a79ba60860894588f1a9385bdc84d5874 — all ten ScriptHarnessWindowsOwnershipTests failed on Linux with PlatformNotSupportedException or kernel32.dll lookup. An assembly fixture was not the right place to gate tests that can perform P/Invoke before invoking that fixture. Added Before(Test) admission on the Windows-only class, before any native setup. Native assertions are unchanged. Rerun: 10 skipped, 0 failed, platform runner exit 8 because zero tests executed; this is expected Linux admission evidence, not native qualification.

All three changes are fixtures/tests only. No Claude input delivery, production script or runtime changes; CARD-0680 helpers are not implicated by the reproduced script-fixture defects. No sibling-owned files or nightly cost inventory were changed.

## Execution and provenance

All build/test drivers used scripts/build-slot.ps1. Direct compiled `dotnet run --no-build` was explicitly commissioned because the checkpoint inventory reader rejects this >64 MiB test DLL. No CHECKPOINT receipt or reviewedSourceClean assertion is claimed.

- Base build: exit 0, 257.60s, slot wait 0s, lease 990d3aaa-ad75-49f0-b257-107249c604a6, maxcpucount 6.
- Lock fixture build: exit 0, 158.97s, slot wait 45s, lease 58ab0f41-8519-4422-8b23-bc7346d1f4c3.
- Source fixture build: exit 0, 143.33s, slot wait 0s, lease 6622c5a7-7573-442c-962e-ed2bb2cc4606.
- Windows admission build: exit 0, 104.01s, slot wait 0s, lease 9cf00bc4-d359-426e-b3ce-51dca8b28b8c.

Initial/lock builds included analyzers. The source and Windows-gate builds were incremental test-assembly builds with --no-restore --no-dependencies and RunAnalyzers=false; TUnit source generation and C# compilation still ran. All four used OutputPath=bin-b6d3db3b/ and UseAppHost=false. Host: Debian 12/Linux x64, SDK 10.0.402, .NET runtime 9.0.21, TUnit 1.44.0.0. Observed load samples ranged approximately 6–24 (one-minute load), with other host work contending for slots.

The combined exact 14-class base invocation hit its 2100-second watchdog: 70 valid passes, one genuine lock-barrier failure, then shutdown/cancellation artifacts. No TRX. The 600-second quick continuation completed all 26 RunCheckpointScriptTests, repaired lock 1/1, and source strict admission/child-CWD controls before its watchdog; TerminalEvidence was a genuine assertion failure. DriftAndReuse's later `Checkpoint produced no results` occurred during watchdog shutdown and is not a product defect. The first final continuation completed Linux/process ownership, exposed Windows platform errors and passed source strict admission/receipt validation; it was deliberately stopped and awaited before adding the Windows gate. Its interrupted DiagnosticReceipts is not a genuine regression. All interrupted receipts and logs are retained.

Per-test timing sidecars retain run/group identities and method membership. Most legacy cases have no nested phase scopes; their wall time is remainder/unknown external-call attribution, never zero process/IO cost. Summed test wall is useful class occupancy; envelope wall in an interleaved run also contains sibling classes.

Evidence root: /work/worktrees/task-b6d3db3b/.antiphon/evidence-b6d3db3b/. Initial and quick commands/rosters, all build logs, phased timing JSONL, native-path witness, TRX for completed continuations, and exact outstanding methods are retained there. Generated payloads remain ignored.

## Windows string witness

The successful gated Node witness is windows-string-witness.mjs (three assertions), with windows-string-witness-node.log. It forces a Windows root without a native lock, shell or Docker child. It demonstrates: unescaped Windows backslashes invalidate the literal JSON replacement in LoadComposeV218; the Bash injection retains Windows-absolute paths that are not POSIX-absolute; and the evidence-path injection canary embeds a drive colon into a Git path. These are portability witnesses, not proof of the original native failure cause. A first Python launcher attempt could not start because Python is not installed; Node was then used successfully.

Reproduce its central pure-string check:

```js
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const root = String.raw`C:\Users\fixture\AppData\Local\Temp\c1008-host-fixture`;
assert(path.win32.isAbsolute(root) && !path.posix.isAbsolute(root));
const raw = fs.readFileSync('scripts/fixtures/c1105-compose-v218-config.json', 'utf8');
JSON.parse(raw);
assert.throws(() => JSON.parse(raw.replaceAll('/inert/c1105', root)), SyntaxError);
```

## Native rerun rows

Linux greens do not establish the recorded Windows failures' causes. On the native Windows qualification host, rerun separate exact class rows after landing, using `/*/*/<ClassName>/*` (using the commissioned direct --no-build fallback through scripts/build-slot.ps1 and fresh isolated output (the same >64 MiB inventory-reader limit still applies)):

RetiredTempContainerHostTests; RollingProductionMountTests; RollingVolumeRecycleScriptTests; RetiredTempContainerScriptTests; RunCheckpointSourceScriptTests; RunCheckpointScriptTests; EvidenceDiffGuardTests; EvidenceApprovedFixtureGuardTests; EvidenceDeletionGuardTests; EvidenceSupplementalDeletionGuardTests. Also run ScriptHarnessWindowsOwnershipTests (10 native executions required) and ScriptHarnessProcessTests (11). ReleaseGatePublicationAuthorityTests is a separate 15-case cost/coverage row: its Linux sum was 998.27s (16m38s), not a cheap companion to the failing classes. ScriptHarnessLinuxOwnershipTests passed 21/21 here and has no native-Windows qualification credit.

The four evidence classes (42/42), checkpoint harness (26/26), retirement wrapper (13/13), production mounts (12/12), and publication authority (15/15) did not reproduce any Windows failure on Linux. Unfinished Linux classes must not be described as non-reproducing; use the exact outstanding list below. No speculative fixes were made for the original Windows-only clusters.

## Final per-class Linux outcomes

These are distinct methods, taking the latest completed outcome. Known outer-watchdog/owned-stop cancellations are incomplete, not genuine assertion failures. Source cases combine two passes at 79eb2ab4e78b742063ab8202533084ccf4c7d65e and four at 10f6d87a79ba60860894588f1a9385bdc84d5874; all other resumed greens name their committed slice in the provenance above.

```text
RetiredTempContainerHostTests: 13 passed / 0 failed / 0 skipped / 8 not completed; latest test wall=610.47s
RollingProductionMountTests: 12 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=62.37s
RollingVolumeRecycleScriptTests: 8 passed / 0 failed / 0 skipped / 19 not completed; latest test wall=178.06s
RunCheckpointSourceScriptTests: 6 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=226.00s
RetiredTempContainerScriptTests: 13 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=156.78s
EvidenceDiffGuardTests: 19 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=111.76s
RunCheckpointScriptTests: 26 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=490.83s
EvidenceApprovedFixtureGuardTests: 7 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=81.74s
EvidenceDeletionGuardTests: 6 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=218.26s
EvidenceSupplementalDeletionGuardTests: 10 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=60.97s
ScriptHarnessLinuxOwnershipTests: 21 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=62.63s
ScriptHarnessProcessTests: 11 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=42.81s
ScriptHarnessWindowsOwnershipTests: 0 passed / 0 failed / 10 skipped / 0 not completed; latest test wall=0.13s
ReleaseGatePublicationAuthorityTests: 15 passed / 0 failed / 0 skipped / 0 not completed; latest test wall=998.27s
TOTAL passed=167 failed=0 skipped=10 missingOrRed=27
```

Slow-class wall measurements: RunCheckpointScriptTests 490.83s (combined envelope 498.55s); EvidenceDeletionGuardTests 218.26s (envelope 362.07s); ReleaseGatePublicationAuthorityTests 998.27s (envelope 998.28s); RollingVolumeRecycleScriptTests only partial: 164.04s across eight passes, plus 14.02s in the watchdog-canceled case. Its full class cost is unavailable. RetiredTempContainerHostTests is likewise partial. Do not reprice from partial sums.

The final bounded continuation ran Windows admission (10 skipped, exit 8), source-final 4/4 passed, retired-script 13/13 passed, production-mounts 12/12 passed, then reached its 570s watchdog in rolling-volume. C1008_Retired_absent_null_is_accepted was canceled, not an unmutated defect. It did not start the final retired-host remainder. Earlier ownership: 21 Linux + 11 process passed.

## Exact missing Linux execution

Run only these 27 methods once from the next fetched/landed source, through a leased direct no-build selection after building that source; retain the already completed costs and outcomes. No assertion/timeouts/retries/cost policy were changed to obtain green.

```text
RetiredTempContainerHostTests.C994_Host_main_admission_is_required
RetiredTempContainerHostTests.C994_Host_task_and_land_census_is_complete
RetiredTempContainerHostTests.C994_Census_and_inspect_fail_closed
RetiredTempContainerHostTests.C994_Container_service_and_state_are_restricted
RetiredTempContainerHostTests.C994_Production_mount_topology_is_proven
RetiredTempContainerHostTests.C994_Whole_candidate_set_precedes_removal
RetiredTempContainerHostTests.C994_Image_receipt_lookup_is_bound
RetiredTempContainerHostTests.C994_Fixture_custody_is_bounded
RollingVolumeRecycleScriptTests.C1374_Wrapper_census_recovers_transient_reads
RollingVolumeRecycleScriptTests.C1087_Read_failures_name_their_cause
RollingVolumeRecycleScriptTests.C1087_Project_resolves_by_repository_identity
RollingVolumeRecycleScriptTests.C1087_Census_filters_open_work_and_pending_lands
RollingVolumeRecycleScriptTests.C1087_Check_census_phase_is_read_only
RollingVolumeRecycleScriptTests.C1008_Present_or_unknown_temp_keeps_null_refusal
RollingVolumeRecycleScriptTests.C1008_Busy_routed_and_land_in_flight_refuse
RollingVolumeRecycleScriptTests.C1353_Wrapper_waits_for_a_bound_land_then_proceeds
RollingVolumeRecycleScriptTests.C1008_Same_sha_and_partial_retries_are_safe
RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict
RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain
RollingVolumeRecycleScriptTests.C1008_Wrapper_refusal_receipts_do_not_leak_secrets
RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted
RollingVolumeRecycleScriptTests.C1008_Wrapper_retired_absent_null_is_accepted
RollingVolumeRecycleScriptTests.C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance
RollingVolumeRecycleScriptTests.C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands
RollingVolumeRecycleScriptTests.C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail
RollingVolumeRecycleScriptTests.C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts
RollingVolumeRecycleScriptTests.C1371_Post_removal_count_drift_does_not_abort
```

## Handoff and limits

The three fixture fixes have direct follow-up evidence. Full requested Linux reproduction remains incomplete solely because of the bounded task budget and measured execution/build costs. The 27 methods above still need a Debug continuation; original native failure causes remain unresolved for the fully green Linux classes, with native rerun rows above. No Review was run, per the operator fixture-only exception. Production activation/release qualification is not claimed.

Evidence guard over base..10f6d87a79ba60860894588f1a9385bdc84d5874 passed: commits=3 entries=0 violations=0. Merge-tree against fetched origin/master was clean; its output is merge-tree-final.txt. All launched drivers were awaited; owned build leases were released or already reaped. Task-owned alternate outputs were removed, with exact inventory and final test DLL SHA256 retained in evidence.
