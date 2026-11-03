# CARD-1673 Code task 0ce3bb38: survivor witnesses, red against each mutant, green at HEAD

Branch feat/card-task-0ce3bb38, base master c4e7aa06e9f8c119f7dd59f52e38ed413293c9f2.
Witness commit 16b05370fec764ce10453aafcf2ba020a3c79a75 (test only; no product, csproj or pins edit).
The first attempt hit server2 ENOSPC (04:22-04:37 UTC, baseline crashed, nothing counted). After the
reclaim (task e937b0c3) everything below was run from scratch on server2 (Linux), tested source
fb9b6f87c774a078a10791d54201838a38120653 (16b05370 plus this report file only).

## Witnesses and mutant results

| # | Mutant | Witness | Red against mutant (assertion) | Round |
|---|---|---|---|---|
| 1 | M56-WB: WSR:220-221 runner, slot and generation terms dropped | C1656RecordReleaseHardeningTests.C1673_ReleaseOnATaskReboundWhileItIsRecordedIsRefusedChanged(generation/runner/slot) | 3/3 cases: should throw ConflictException, did not | r01 |
| 2 | M56-WR: WSR:221 release-null term dropped | C1656RecordReleaseHardeningTests.C1673_ReleaseRecordedConcurrentlyIsRefusedChangedAndNotRestamped | should throw ConflictException, did not | r02 |
| 3 | M49-L: WSR:196 live-status pre-check removed | PhoneHomeRollingRunnerTests.C1649_RecordReleaseStampsOnlyTheStuckAttemptOfATaskThatIsNotLive (attempt 2 Working row now asserts the code) | code should be workspace_slot_release_task_live, was workspace_slot_release_attempt_mismatch | r03 |
| 4 | M62-A1c: DRS:577 `skipped ??= lastFailure` dropped | DataRetentionServiceTests.C1673_BudgetSpentByRangesThatRecoverStillReportsFailed | Tasks Status should be Failed, was Completed | r01 |
| 5 | M65-X: fixture exactly-one throw removed (insert at first match) | C1008HostFixtureSetupInsertionTests (helper C1008HostFixture.InsertSetupAtEntry) | No_..._zero: threw ArgumentOutOfRangeException, not InvalidOperationException; Two_..._two: did not throw; One_... stays green (correct) | r01 |
| 6a | M65-B: c590-remote.sh:9051 refusal -> `|| :` | VolumeRetirementContentTests.Temp_recycle_generation_binding_refuses_a_present_owned_container_alone | File.Exists(reached) should be False, was True (the topology layer was reached) | r02 |
| 6b | M65-T: :9376 c1008_owned_mounts replaced by `map(.Topology={})` | VolumeRetirementContentTests.Temp_recycle_topology_layer_refuses_a_present_owned_container_alone | Exit should be 2, was 0 | r03 |
| 7 | PC-3d: SessionRunnerRuntime:3767 CreateAdoptedExited exit code null | PtyHostAdoptionTests.Dead_host_with_a_recorded_exit_is_registered_with_that_exit_code | ExitCode should be 7, was null | s01 |
| 8 | M66-F: RSAE:35 future drain start treated as within bound | existing RunnerStateAlarmExclusionTests.C1671_a_drain_start_in_the_future_fails_safe_and_counts_as_overrun | Overran(future, T0, 30) should not be null but was: already dead, no new test | r02 |

As expected, the existing A-3 vector Temp_recycle_refuses_a_present_owned_container_before_any_audit
stayed green under M65-B alone and M65-T alone (masked). The new layer witnesses close that.

6a/6b wrap the real functions through the fixture's `extra` hook (a `declare -f` rename). No
script edit is needed. Both are Native rows in WindowsUnitSkipRoster.Native, as CARD-1665's
were.

## Rounds (one dotnet run lease each, native output, UseAppHost=false, TEMP=/tmp)

Antiphon.Tests filter (29 tests):
`/*/*/(C1656RecordReleaseHardeningTests*)|(PhoneHomeRollingRunnerTests*)|(DataRetentionServiceTests*)|(RunnerStateAlarmExclusionTests*)|(C1008HostFixtureSetupInsertionTests*)|(VolumeRetirementContentTests*)/(C1656_*)|(C1670_*)|(C1673_*)|(C1649_*)|(C1662_*)|(C1671_*)|(C1666_*)|(One_*)|(No_*)|(Two_*)|(Temp_recycle*)|(Fixture_setup*)`

| Round | B23_MUTANT; script edit | Total | Failed | Lease (waited / held) |
|---|---|---|---|---|
| build (gated) | n/a | n/a | n/a | be1db0a1 (0 s / 208 s) |
| r00 baseline | none | 29 | 0 | 380f9cce (0 s / 155 s) |
| r01 | M56-WB, M62-A1c, M65-X | 29 | 6 | 563fdf77 (0 s / 84 s) |
| r02 | M56-WR, M66-F; script M65-B | 29 | 3 | 0fa52c8f (0 s / 87 s) |
| r03 | M49-L; script M65-T | 29 | 2 | 25fd8745 (0 s / 103 s) |
| SR build (gated) | n/a | n/a | n/a | 787ee5d5 (0 s / 20 s) |
| s00 SR baseline `/*/*/PtyHostAdoptionTests/*` | none | 7 | 0 (1 skipped, modern conpty absent on Linux) | abc01648 (0 s / 13 s) |
| s01 | PC-3d | 7 | 1 | 3337710b (0 s / 13 s) |

## Final runs at the restored tree (rebuilt with no gates; no B23_MUTANT string in any bin dll)

| Run | Selection | Total | Failed | Skipped | Lease (waited / held) |
|---|---|---|---|---|---|
| rebuild Antiphon.Tests | n/a | n/a | n/a | n/a | 2fef6b38 (0 s / 157 s) |
| f1 | PhoneHomeRollingRunnerTests (incl. the Drain partial) | 34 | 0 | 0 | dbf8a81a (0 s / 70 s) |
| f2 | DataRetentionServiceTests, C1656RecordReleaseHardeningTests, C1008HostFixtureSetupInsertionTests, WindowsUnitPlatformAdmissionTests | 120 | 0 | 1 (C1210_Windows_linux_only_outcomes_are_exact: Windows-only by design) | f1cb7ebe (0 s / 99 s) |
| f3a/f3b/f3c | VolumeRetirementContentTests, all 19 methods in 3 chunks | 8+5+6 | 0 | 0 | 438ca316 (0 s / 305 s), 5d3e701c (0 s / 55 s), f3bc3e4c (0 s / 192 s) |
| rebuild SR.Tests | n/a | n/a | n/a | n/a | f821255b (0 s / 54 s) |
| f4 | SessionRunner.Tests `(PtyHostAdoption*)` | 7 | 0 | 1 (modern backend, no conpty.dll) | 29941dd8 (0 s / 24 s) |

RollingVolumeRecycleScriptTests (the 49-minute class) was not run. Its only change is the extraction
of InsertSetupAtEntry, which is covered by the helper tests and by every C1008HostFixture user in
VolumeRetirementContentTests (19/19). The Windows child roster audit (C1210_Windows_...) for the two
new Native rows needs the Windows lane; nothing ran on the desktop.

## Restoration

Every gated edit was reverted with `git checkout -- <exact path>` (DataRetentionService.cs,
RunnerStateAlarmExclusion.cs, WorkspaceSlotReleaseService.cs, SessionRunnerRuntime.cs,
RollingVolumeRecycleScriptTests.cs). scripts/c590-remote.sh was restored after each of r02 and r03.
The untracked server/B23Mutant.cs and src/Antiphon.SessionRunner/B23Mutant.cs were removed. The
bin/obj build outputs and the scratch TRX and logs were deleted after the report commit.

PCs: all of the above stay pending for method-scoped SourceLanding Mutation.
