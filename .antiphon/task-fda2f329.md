# CARD-1673 Review round 1 (task fda2f329): clean

Subject: feat/card-task-0ce3bb38 tip 284413498f37c3d98de77f0c8d12446fca4fcada (code commit 16b05370f), base master c4e7aa06e.
Owner Code task: 0ce3bb38-2c9f-4b2b-b71d-47eb6f805b26. Host: server2 (Linux). All runs native output, UseAppHost=false, TEMP=/tmp, one build-slot lease each.

## (1) Scope
`git diff --stat c4e7aa06e..HEAD`: 8 test files + the Code report only; no product, csproj, pins, policy or script change.
`InsertSetupAtEntry` is a verbatim lift of master's inline block (same regex `^trap 'ec=\$\?;` Multiline, same count!=1 throw text, same `Insert(entry[0].Index, injection + "\n")`), so the 19 fixture users run the same script text.

## (2) Mutants (reapplied by me, restored with `git checkout -- <exact path>`, tree verified clean after)
C# mutants were env-gated (`R1673_MUTANT`) in one gated Antiphon.Tests build (lease 2e051fd1); script mutants used the clean tip build; PC-3d had its own SessionRunner.Tests build (lease 0f2db269).

| Mutant | Edit | Witness result |
|---|---|---|
| M56-WB | WSR:220-221 runner/slot/generation terms OR'd away | C1673_ReleaseOnATaskRebound...(generation/runner/slot) 3/3 red: "should throw ConflictException"; other 7 in class green |
| M56-WR | WSR:221 `ReleasedAt == null` OR'd away | C1673_ReleaseRecordedConcurrently... red: should throw; 9 others green |
| M49-L | WSR:196 HoldingLease pre-check skipped | C1649_RecordReleaseStampsOnlyTheStuckAttempt... red: expected task_live, was attempt_mismatch (attempt-2 Working row; the later attempt-1 live rows still answer task_live via the conditional update, so only the new pin catches it) |
| M62-A1c | DRS:577 `skipped ??= lastFailure` skipped | C1673_BudgetSpent... red: tasks.Status should be Failed |
| M65-X | helper count!=1 throw skipped | No_... red (threw ArgumentOutOfRange, not InvalidOperation), Two_... red (did not throw), One_... green |
| M65-B | c590-remote.sh:9051 refusal -> `|| :` | Temp_recycle_generation_binding..._alone red at File.Exists(reached) (topology layer reached; exit was still 2 from that layer). topology_layer..._alone and the A-3 vector green (A-3 masked, as Code said) |
| M65-T | :9376 `c1008_owned_mounts` call -> `C1008_OWNED=$(jq 'map(.Topology={})')` | Temp_recycle_topology_layer..._alone red: Exit should be 2. generation_binding..._alone and A-3 green |
| PC-3d | SessionRunnerRuntime:3767 `_exitCode = null` | Dead_host_with_a_recorded_exit... red: ExitCode should be 7, was null; 5 others green, 1 platform skip |

Isolation of M65-B/M65-T: each layer witness is red only under its own mutant and green under the other, so neither depends on the other layer being removed.

## (3) Race shape
`AfterStuckRecordIsRead` fires once (asserted Fired == 1) after the first AgentTaskEvents reader (the stuck-record check), on a separate context that commits, before the transaction and the conditional ExecuteUpdate. The asserted code is `workspace_slot_release_changed`: a change made before the binding read would answer `..._mismatch`, so the assertion pins the read-to-write window. A stale-read stamp would not throw and goes red. Event assertions are exact: WB `ShouldHaveSingleItem` over all task events + StartsWith ReleaseStuck; WR single ReleaseRecorded event ending ": racer", racer stamp unchanged.

## (4) Windows
Both new VolumeRetirementContentTests methods: class `[NativeLinuxOnly]` + first-statement `NativeLinuxGate.Require`, and the class has the rc policy `platform: windows` exclusion. The roster rows are in `WindowsUnitSkipRoster.NativeSourceOnly` (with CARD-1665's rows), not `Native` as the Code report says. That is correct: `C1210_Host_roster_matches_windows_shaped_observation` requires zero `Native` rows for this class. C1008HostFixtureSetupInsertionTests is portable: pure string/Regex, no process, LF-normalised sources. WindowsUnitPlatformAdmissionTests green (closure test included).

## (5) Tip runs (clean build of 284413498, lease ac64a24f)
| Selection | Total | Failed | Skipped |
|---|---|---|---|
| PhoneHomeRollingRunnerTests + DataRetentionServiceTests + C1656RecordReleaseHardeningTests + C1008HostFixtureSetupInsertionTests + WindowsUnitPlatformAdmissionTests | 154 | 0 | 1 (C1210_Windows_linux_only_outcomes_are_exact, Windows-only) |
| VolumeRetirementContentTests chunk A/B/C | 8+5+6 = 19 | 0 | 0 |
| SessionRunner.Tests (PtyHostAdoption*) | 7 | 0 | 1 (modern conpty backend absent on Linux) |

Not run (per brief scope): RollingVolumeRecycleScriptTests (~49 min class; only the helper extraction changed there, covered by the helper tests + 19 fixture users), rest of the Unit lane.

## Merge / evidence
- check-evidence-diff c4e7aa06e..284413498: commits=3 entries=2 violations=0, exit 0.
- merge-tree onto origin/master 7b920d84a (advanced: CARD-1653 S2, CARD-1647, land-graph): clean, no file overlap. Master's SessionRunnerRuntime change touches the live-host adopt path and socket dir (default off), not the dead-host CreateAdoptedExited path. Not re-run on the merged tree.
- rc/2026-11-01 10a826b9d: a cherry-pick of 16b05370f conflicts. C1656RecordReleaseHardeningTests.cs is absent on rc (modify/delete), and DRS tests, PhoneHome drain tests and RollingVolumeRecycleScriptTests conflict. The witnesses need the CARD-1649/1656/1662/1665/1670 bases on rc first. This change lands on master only.

## Advisories
- A-1: the Code report says the roster rows are in `WindowsUnitSkipRoster.Native`; they are in `NativeSourceOnly`. The code is right; only the report wording is wrong.
- A-2: an rc port is not a clean pick (see above).

Cleanup: every bin/obj this review built was deleted with literal paths. Tree clean apart from this report.
