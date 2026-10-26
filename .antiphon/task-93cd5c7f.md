# Debug report: rc qualification group 71

Linux reproduction completed successfully: RollingVolumeRecycleScriptTests passed 30/30, with 0 failures and 0 skips. The four Windows failures remain unreproduced/native-only follow-up; their cause is unconfirmed. No product, fixture, assertion, skip or timeout change is justified by this evidence. This commit contains this investigation report only.

Tested source: bedea1c03cd788d83b2ff54c40bfb603927bd69c, which matched fetched origin/rc/2026-10-26 at dispatch and reproduction. The assigned branch was already based there; no branch switch, rebase or merge occurred. Tracked source was clean before and after execution. No desktop operation or native qualification lock was touched.

## Verification

Host: d66b7f130eb4, Debian GNU/Linux 12, SDK 10.0.403, .NET test runtime 9.0.22, TUnit 1.44.0.

One isolated build, granted build slot, maxcpucount 6, admission wait 0 seconds: exit 0, 0 errors, 1196 warnings, 3m38.07s build duration (219s lease hold).

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-93cd5c7f-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-debug-93cd5c7f/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-93cd5c7f-baseline -- dotnet run --no-build --no-restore --project tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-debug-93cd5c7f/ --property:UseAppHost=false -- --treenode-filter '/*/*/RollingVolumeRecycleScriptTests/*' --results-directory .antiphon/debug-93cd5c7f/baseline --report-trx --report-trx-filename baseline.trx
```

One exact-class run, granted build slot, admission wait 0 seconds: exit 0; 30 executed, 30 passed, 0 failed, 0 skipped, 0 timed out. Test wall interval: 2026-10-26T20:40:51.4934574Z to 2026-10-26T21:05:45.5795758Z (24m54.086s; runner summary 24m54.171s); lease hold 1498s. No second run was needed because no tested source changed. This is Linux reproduction evidence, not native Windows qualification credit.

The compiled DLL was 68,771,840 bytes (over the checkpoint reader's 64 MiB limit), so the brief's direct --no-build route was used. DLL SHA-256: EB21A2991AF171FA36A716CE629D1D969995493318BB2440D091EAA7F869DF59.

Raw evidence remains ignored under /work/worktrees/task-93cd5c7f/.antiphon/debug-93cd5c7f/: build.log, baseline.log, build-provenance.json, baseline/baseline.trx and the HTML report. TRX SHA-256: 2F365859C3C6F60CE1A5ECF56BC91FF484B74653B20F91C9F3D0327AB6A12603. All 33 task-owned bin-debug-93cd5c7f directories were deleted after the run; their exact inventory is retained as owned-output-directories.json. Generated payloads are not committed.

## Classification and limits

No Linux-reproduced failure cluster. Windows path/shell semantics, missing prerequisites and load-induced timing remain hypotheses; the supplied digest has no failing method identities or error text for this chunk. No Windows-semantics defect was established, so no speculative patch or string witness was added.

All four legacy-roster modes independently report C973_JQ assertions=31 failures=0. The absent/missing-shell/failing-shell modes preserve the 62-invocation roster; present preserves the 66-invocation roster and ran with -RequireJq. Their observed Linux durations are roughly three minutes each, below the existing 400-second per-mode deadline. This single sample is not p95 and cannot justify a Windows timeout adjustment.

Per-test durations below are TRX test-body observations. Phase attribution and individual external-call spans are unavailable, not zero; build and admission are reported separately above. No timeout policy was changed.

| Test case (all in RollingVolumeRecycleScriptTests) | Outcome | Duration |
| --- | --- | --- |
| C1008_Busy_routed_and_land_in_flight_refuse | Passed | 00:01:39.7529811 |
| C1008_Documentation_and_transport_pins_match | Passed | 00:00:01.0952637 |
| C1008_Legacy_rolling_and_jq_rosters_remain(absent) | Passed | 00:03:03.4551057 |
| C1008_Legacy_rolling_and_jq_rosters_remain(failing-shell) | Passed | 00:03:04.4073771 |
| C1008_Legacy_rolling_and_jq_rosters_remain(missing-shell) | Passed | 00:02:57.3737095 |
| C1008_Legacy_rolling_and_jq_rosters_remain(present) | Passed | 00:03:19.0051179 |
| C1008_Option_manifest_is_strict | Passed | 00:00:01.2281733 |
| C1008_Present_or_unknown_temp_keeps_null_refusal | Passed | 00:00:40.4358136 |
| C1008_Refusal_receipts_do_not_leak_secrets | Passed | 00:00:27.8996009 |
| C1008_Retired_absent_null_is_accepted | Passed | 00:00:25.8315820 |
| C1008_Same_sha_and_partial_retries_are_safe | Passed | 00:00:21.8742643 |
| C1008_Transport_scripts_are_ascii | Passed | 00:00:00.0160260 |
| C1008_Wrapper_option_manifest_is_strict | Passed | 00:00:12.2015288 |
| C1008_Wrapper_refusal_receipts_do_not_leak_secrets | Passed | 00:00:01.6081754 |
| C1008_Wrapper_retired_absent_null_is_accepted | Passed | 00:00:05.3045443 |
| C1087_Census_filters_open_work_and_pending_lands | Passed | 00:00:42.6981357 |
| C1087_Check_census_phase_is_read_only | Passed | 00:00:03.6785137 |
| C1087_Project_resolves_by_repository_identity | Passed | 00:00:45.9318496 |
| C1087_Read_failures_name_their_cause | Passed | 00:00:45.7173406 |
| C1317_Census_tolerates_a_task_that_settles_without_a_land | Passed | 00:00:04.7959177 |
| C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1 | Passed | 00:00:08.4524137 |
| C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1 | Passed | 00:00:07.7962586 |
| C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance | Passed | 00:00:07.2126744 |
| C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands | Passed | 00:00:03.0610651 |
| C1353_Wrapper_waits_for_a_bound_land_then_proceeds | Passed | 00:00:06.9414673 |
| C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail | Passed | 00:01:00.7141383 |
| C1361_Wrapper_census_tolerates_open_status_changes_between_passes | Passed | 00:01:14.0737384 |
| C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts | Passed | 00:02:06.3426587 |
| C1371_Post_removal_count_drift_does_not_abort | Passed | 00:00:25.1585218 |
| C1374_Wrapper_census_recovers_transient_reads | Passed | 00:00:48.9866865 |

## Native follow-up row

After the desktop qualification owner releases its native lane, retrieve the four original failing method names, error messages and durations from the group-71 TRX under C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87. The four legacy arguments are a plausible count match only; this report does not attribute the failures to them.

Rerun row: Antiphon.Tests / RollingVolumeRecycleScriptTests / filter /*/*/RollingVolumeRecycleScriptTests/*. Use the normal Windows lane and existing WSL/bash/jq prerequisites; preserve all assertions and platform gates. One direct build-slot build and one --no-build invocation avoid the checkpoint assembly-size limit:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group71-native-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-group71-native/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group71-native -- dotnet run --no-build --no-restore --project tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-group71-native/ -- --treenode-filter '/*/*/RollingVolumeRecycleScriptTests/*' --results-directory .antiphon/group71-native --report-trx --report-trx-filename group71-native.trx
```

The four explicitly native-Linux host methods retain their Windows skips; inspect all additional skips as incomplete coverage. Bind the rerun to its actual source SHA and preserve TRX errors and per-case timings. No native row was executed by this task.

## Landing observations

The pre-report rc merge-tree check was conflict-free. The required master merge-tree check at unchanged tested base bedea1c03cd788d83b2ff54c40bfb603927bd69c against fetched origin/master b6db2de3dd0c0bad2ba30823d01882ca6fc9388e found inherited add/add conflicts in .antiphon/task-af2b78c8.md and docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md. No merge was applied and these unrelated report files were not changed. This task has no product or fixture fix to carry to rc/master; its sole deliverable is this report. Preserve the native follow-up rather than claiming the Windows failures fixed.

--- next stage ---
next: land
handoff: Evidence-only report; no reproduced Linux defect or code fix. Native group-71 rerun and original failure text remain necessary; inherited master report-file conflicts require landing-owner triage.
artifact: .antiphon/task-93cd5c7f.md
