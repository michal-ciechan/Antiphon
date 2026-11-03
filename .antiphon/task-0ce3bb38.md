# CARD-1673 Code task 0ce3bb38: witnesses written, NOT yet run (server2 disk full)

Branch feat/card-task-0ce3bb38, base master c4e7aa06e9f8c119f7dd59f52e38ed413293c9f2.
Witness commit 16b05370fec764ce10453aafcf2ba020a3c79a75 (test only; no product, csproj or pins edit).

## Status

- All witnesses are written and committed; none has a verified red-against-mutant or green-on-master
  result yet. Server2's root LV hit 100% (ENOSPC: 0-260 MB free from 04:22 to 04:37 UTC).
- One gated Antiphon.Tests build succeeded (lease 92e2b793, waited 0 s, held 267 s). The baseline
  round r00 (lease 4b6e506d, waited 0 s, held 81 s) crashed with exit 134: Postgres
  `53100 could not write to file "pg_wal/xlogtemp.109": No space left on device` and the test host's
  console write threw `IOException: No space left on device`. Its two failures
  (C1670_ReleaseOnATaskDeleted..., C1666_..no_drain_start..) are disk failures, not results.
- Restored: every gated edit was reverted with `git checkout -- <exact path>`, both untracked
  `B23Mutant.cs` helpers were removed, and the gated bin/obj outputs (server, Antiphon.Tests,
  SessionRunner, DockerStack.Fixture, ChannelOutbound.Probe) were deleted. No `B23_MUTANT` string
  remains in any bin dll. Tracked tree clean at 16b05370.
- SessionRunner.Tests was never built (PC-3d unrun).

## Witness map

| # | Mutant | Witness | Expected red against the mutant |
|---|---|---|---|
| 1 | M56-WB (WSR:220-221 runner/slot/generation terms dropped) | C1656RecordReleaseHardeningTests.C1673_ReleaseOnATaskReboundWhileItIsRecordedIsRefusedChanged (generation, runner, slot) | no ConflictException: the rebound lease is stamped |
| 2 | M56-WR (WSR:221 release-null term dropped) | C1656RecordReleaseHardeningTests.C1673_ReleaseRecordedConcurrentlyIsRefusedChangedAndNotRestamped | no ConflictException; stamp overwritten; two recorded events |
| 3 | M49-L (WSR:196 live pre-check removed) | PhoneHomeRollingRunnerTests.C1649_RecordReleaseStampsOnlyTheStuckAttemptOfATaskThatIsNotLive (row attempt 2 Working now asserts workspace_slot_release_task_live) | code workspace_slot_release_attempt_mismatch |
| 4 | M62-A1c (DRS:577 `skipped ??= lastFailure` dropped) | DataRetentionServiceTests.C1673_BudgetSpentByRangesThatRecoverStillReportsFailed | Tasks pass Completed, not Failed |
| 5 | M65-X (fixture exactly-one throw removed) | C1008HostFixtureSetupInsertionTests (3 methods; helper C1008HostFixture.InsertSetupAtEntry) | 0 traps: ArgumentOutOfRangeException not InvalidOperationException; 2 traps: no throw |
| 6a | M65-B (c590-remote.sh:9051 refusal removed) | VolumeRetirementContentTests.Temp_recycle_generation_binding_refuses_a_present_owned_container_alone (wraps c1008_owned_mounts via `declare -f` rename in the fixture `extra`; marker must stay absent) | topology layer reached (marker written) |
| 6b | M65-T (:9376 c1008_owned_mounts skipped) | VolumeRetirementContentTests.Temp_recycle_topology_layer_refuses_a_present_owned_container_alone (bind_generation wrapper hides the project's containers from its census) | no refusal (M65-BT ran to exit 0) |
| 7 | PC-3d (SessionRunnerRuntime:3767 CreateAdoptedExited exit code null) | PtyHostAdoptionTests.Dead_host_with_a_recorded_exit_is_registered_with_that_exit_code (crafted manifest, DeadProcessProbe) | ExitCode null, not 7 |
| 8 | M66-F (RSAE:35 future start within bound) | existing RunnerStateAlarmExclusionTests.C1671_a_drain_start_in_the_future_fails_safe_and_counts_as_overrun | by reading: `Overran(future, T0, 30).ShouldNotBeNull()` fails on the mutant's null; not yet run |

6a/6b are new Native rows in WindowsUnitSkipRoster.Native (as CARD-1665 did), so the C1210
WindowsUnitPlatformAdmissionTests must also run.

## Resume recipe (one gated build per project, one dotnet run lease per round)

Gated edits (env `B23_MUTANT`, comma list), with `internal static class B23Mutant { public static bool On(string id) => (System.Environment.GetEnvironmentVariable("B23_MUTANT") ?? "").Split(',').Contains(id); }`
untracked in `server/B23Mutant.cs` and `src/Antiphon.SessionRunner/B23Mutant.cs`:

- WSR: `if (!B23Mutant.On("M49-L") && HoldingLease.Contains(task.Status))`; before the update
  `var mWB = B23Mutant.On("M56-WB"); var mWR = B23Mutant.On("M56-WR");` and the WHERE
  `t.Attempt == attempt && (mWB || (runner && slot && generation terms)) && (mWR || t.RemoteWorkspaceSlotReleasedAt == null)`.
- DRS: `if (!B23Mutant.On("M62-A1c")) skipped ??= lastFailure;`
- RSAE: `|| (since > now && !B23Mutant.On("M66-F"))`
- SessionRunnerRuntime CreateAdoptedExited: `_exitCode = B23Mutant.On("PC-3d") ? null : manifest.ExitCode,`
- Fixture InsertSetupAtEntry, before the count check: env contains M65-X -> `return source.Insert(entry[0].Index, injection + "\n");`
- Script (no build, in place, restore with `git checkout -- scripts/c590-remote.sh`): M65-B = line 9051
  `|| c1008_refuse RecycleContainerStateUnknown` -> `|| :`; M65-T = line 9376 call replaced by
  `C1008_OWNED="$(printf '%s' "$owned" | jq -c 'map(.Topology={})')"`.

Antiphon.Tests filter:
`/*/*/(C1656RecordReleaseHardeningTests*)|(PhoneHomeRollingRunnerTests*)|(DataRetentionServiceTests*)|(RunnerStateAlarmExclusionTests*)|(C1008HostFixtureSetupInsertionTests*)|(VolumeRetirementContentTests*)/(C1656_*)|(C1670_*)|(C1673_*)|(C1649_*)|(C1662_*)|(C1671_*)|(C1666_*)|(One_*)|(No_*)|(Two_*)|(Temp_recycle*)|(Fixture_setup*)`

Rounds: r00 baseline; r01 M56-WB + M62-A1c + M65-X; r02 M56-WR + M66-F + script M65-B; r03 M49-L +
script M65-T; r99 restored rebuild. SessionRunner.Tests: `/*/*/PtyHostAdoptionTests/*` baseline and PC-3d.
Then the final class runs the brief names (PhoneHomeRollingRunnerTests, DataRetentionServiceTests,
C1656RecordReleaseHardeningTests, C1008HostFixtureSetupInsertionTests, the two new + the A-3
VolumeRetirementContentTests methods only, WindowsUnitPlatformAdmissionTests, SessionRunner.Tests
PtyHostAdoption classes).
