# Investigation: rc run group-80 C1677RetentionServerTests Windows failures (task c1c1c031)

Date: 2026-11-04. Base: master `2b342761f22f36f5624f054f9928820447e0df50`. Failed run: Windows Full
`0b05506b` on rc cut `f32aed42e` (rc/2026-11-04), group 80, 4 of 438 failed (DESKTOP-KTLKPIF,
2026-11-04T01:11-01:14Z). Investigated on server2 (Linux). Nothing ran on the desktop.

## Verdict

**Confirmed.** This is a Windows-only test defect, not a product defect. The three V-39 transport-world methods
(`Retain_round_trip_real_runner`, `Busy_runner_retain_is_serialised`, `Retain_recovery_at_each_handoff` x2)
build a real `RunnerWorkspaceRetention` engine with the production `ProcWorkspaceSlotProcessProbe`. On Windows
that probe returns an incomplete scan. The engine then keeps the tree with `holder_scan_unknown`, which is the
fail-closed design. The tests expect the Linux outcome (removed). Production never builds this engine on Windows.

## Mechanism (file:line on master 2b342761f)

1. The test world wires the real probe:
   `tests/Antiphon.Tests/Application/C1677RetentionTransportWorld.cs:74`
   `Engine = new RunnerWorkspaceRetention(service, new ProcWorkspaceSlotProcessProbe(), retiringRoot: Retiring);`
2. Off Linux, the probe reports incomplete:
   `src/Antiphon.SessionRunner/WorkspaceSlotSeams.cs:74` (`_isLinux = isLinux ?? OperatingSystem.IsLinux()`) and
   `:85-86` (`if (!_isLinux || !Directory.Exists(_procRoot)) return new HolderScan(false, []);`).
   The record doc at `:21-25` says so: "false when the process table could not be enumerated (or off Linux):
   retention keeps the tree then."
3. Step 7 of phase A keeps the tree on an incomplete scan:
   `src/Antiphon.SessionRunner/RunnerWorkspaceRetention.cs:369-370`
   (`if (!scan.Complete) return Final(facts with { Reason = WorkspaceRetentionCodes.HolderScanUnknown });`).
   No move and no removal happen.
4. How that produces each rc assertion (rc file line numbers, `f32aed42e`):
   - (a) `:723` `Retain_round_trip_real_runner`: the tree is kept, so `Directory.Exists(path)` is True.
   - (b) `:745` `Busy_runner_retain_is_serialised`: same.
   - (c) `:768` `Retain_recovery_at_each_handoff(reply-lost)`: tick 1 passes because the dropped reply is
     recorded as `unknown`. On tick 2 the runner keeps the tree again (`holder_scan_unknown`), so
     `RemoteWorktreePath` stays set.
   - (d) `:765` `Retain_recovery_at_each_handoff(crash-between-phases)`: the keep comes before the move, so
     `PhaseCut("moved")` never fires. Tick 1 records the runner's real `holder_scan_unknown` instead of the
     crash's `unknown`.

## Reproduction (server2, Linux)

Each run below used one build-slot lease per build and one per test run, with `UseAppHost=false` and `TEMP=/tmp`.

| Run | Filter / change | Result |
|---|---|---|
| Unmodified class, default parallelism | `/*/*/(C1677RetentionServerTests*)/*` | 13/61 passed, 48 failed. All 48 were 45 s `[Timeout]` hits inside `TestDbFixtureLifecycle.DropClonedDatabaseAsync` (shared-Postgres contention on loaded server2), with no assertion failures. This is environmental and unrelated. |
| Unmodified class, `--maximum-parallel-tests 4` | same | **61/61 passed** (43 s) |
| Four failing methods, x3 | `/*/*/(C1677RetentionServerTests*)/(Retain_*)\|(Busy_*)` | **4/4, 4/4, 4/4 passed** |
| Windows reconstruction: `C1677RetentionTransportWorld.cs:74` temporarily set to `new ProcWorkspaceSlotProcessProbe("/proc", isLinux: false)` (the internal S-3 seam) | same 4 | **4/4 failed with the exact Windows signatures**: (a)/(b) `Directory.Exists(path) should be False but was True`, (c) `RemoteWorktreePath should be null but was "/tmp/c1677-transport-…/work/worktrees/task-a1677001"`, (d) `ReasonCode should be "unknown" but was "holder_scan_unknown"` |

The mutation was reverted with `git checkout --`. The tree was rebuilt clean before the x3 runs.

## Production exposure on Windows: none

Production has three independent gates. The test's fake runtime bypasses all of them:
- `src/Antiphon.SessionRunner/Program.cs:88-92`: the engine is constructed only when
  `phoneHome.Enabled && OperatingSystem.IsLinux()`.
- `src/Antiphon.SessionRunner/PhoneHomeRuntimeAdapter.cs:55-56`: `RetentionFeatures(isLinux)` advertises
  `workspaceRetentionV1` only on Linux. `PhoneHomeCommandDispatcher.cs:1030-1035` refuses with a 501 when the
  feature is absent.
- `PhoneHomeRuntimeAdapter.cs:67-70`: `Retention()` also throws off Linux.
- Server side, S1 ships `Runners=["server2"]` (comment at `Program.cs:83-85`).

The test's `RetentionRuntime` (`C1677RetentionTransportWorld.cs:189-199`) advertises the feature unconditionally and
calls the engine directly, so on Windows it exercises a configuration production never builds. If a Windows
runner ever did host the engine, every retain would end `Kept/holder_scan_unknown` and the tree would be kept
forever. That is the intended fail-closed result: S3 owns Windows Restart Manager attribution
(`WorkspaceSlotSeams.cs:57-59`).

## Platform markers: the gap

- The siblings are Linux-only by marker. `C1677RetentionNativeProcessTests` has method-level `[NativeLinuxOnly]`
  (`tests/Antiphon.SessionRunner.Tests/C1677RetentionNativeProcessTests.cs:23,48,74,91,108,137`) and a class
  row (`tests/test-execution-policy.json:900-904`). `C1677TempSweepNativeTests` follows the same pattern.
  The engine tests in Antiphon.SessionRunner.Tests use `FakeHolderScan` (`C1677RetentionScratch.cs:282`).
- `C1677RetentionServerTests` has no marker and no row, and no other class shares `C1677RetentionTransportWorld`.
  Its other 57 cases use the scripted peer, are platform-independent, and passed on Windows (433 passed). The
  exclusion must therefore be **method-scoped**. A class row would drop 57 good Windows cases.

## Master since the cut

`f32aed42e` is an ancestor of master. `git diff f32aed42e 2b342761f` touches the class only by adding three
inventory-name tests (commits 1010768ad, 97e77f9f1, dffee7319). The engine diff (+48/-23) has no holder-scan or
platform change, and `C1677RetentionTransportWorld.cs` is unchanged. **Master does not fix this.**

## Fix for a Code task (test/policy only; not done here)

Same recipe as `f32aed42e` (CARD-0824). After CARD-1686, the census is derived, so the hand roster and
document-class list that `f32aed42e` edited no longer exist:

1. Add `[NativeLinuxOnly("CARD-1677 Linux-only by construction: the real runner engine's holder scan reads /proc; off Linux it is incomplete and retention keeps the tree (holder_scan_unknown)")]`
   on its own line above each of the three methods: `Retain_round_trip_real_runner`, `Busy_runner_retain_is_serialised`,
   `Retain_recovery_at_each_handoff` (one marker covers both `[Arguments]` cases). Add `using Antiphon.TestSupport;`.
   Do not add a class marker.
2. Add one rc exclusion row to `tests/test-execution-policy.json`: `suite: antiphon`,
   `class: C1677RetentionServerTests`,
   `methods: [Busy_runner_retain_is_serialised, Retain_recovery_at_each_handoff, Retain_round_trip_real_runner]`,
   `platform: windows`, `reason: "Linux-only by construction: … The case remains required on Linux."`, `owner: CARD-1677`.
   The census key needs exactly the marked method set (`scripts/test-rc-platform.ps1:621-631`).
3. Reseal `policyHash`. Run `NativeLinuxPolicyWitnessTests/*` in Antiphon.Tests and copy the hash from the
   `policy-hash a != b` diagnostic. Loading through pwsh fails on the System.Text.Json version.
4. Re-record `scripts/test-live-lane-run.ps1` `PinnedInputDigest` (currently `76dc79ac…`, line 20) with
   `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest`. The policy file is a protected input.
5. Leave `tests/test-selection-policy.json` alone. No rule or edge names the class file (checked), so the census
   routing check passes through the full-selection fallback (`test-rc-platform.ps1:633-645`), and
   `selectionCatalog.hash` stays put.
6. Verify: `pwsh -NoProfile -File scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy`; the class on Linux
   (61/61 with `--maximum-parallel-tests 4`); `NativeLinuxPolicyWitnessTests/*` in Antiphon.Tests.

**Conflicts.** Every branch that edits `tests/test-execution-policy.json` collides on the `policyHash` line and
the live-lane `PinnedInputDigest` line, and rows added near each other can also collide. Branches ahead of
master that touch the policy (as of this investigation): `feat/card-task-17fe9eec` and `feat/card-task-5bdb0e6f`
(CARD-1681 re-record), and `feat/card-task-86a12341`, `84692314` and `9ac68027` (CARD-0824 S2). Whichever lands
second re-adds its row on the new tip and re-seals both pins. That is a mechanical re-record, never a merge of
the two hashes. The rc port is the same edit on rc's copy, re-sealed against rc's policy bytes.

A rejected alternative is injecting a complete fake holder scan so the V-39 cases run on Windows. That would test
an engine configuration production never ships on Windows (no engine, no capability), and Windows retention is S3.

## Remaining uncertainty

- The Windows run's per-test console output was not read: desktop paths are unreachable from server2. The
  attribution rests on the exact four-signature reconstruction plus the code path, not on a Windows log.
- The 48 timeout reds at default parallelism are an environmental shared-Postgres drop stall on loaded server2.
  They are not investigated here and are unrelated to the Windows failure.

## Not done, noted

- Fix (Code task): method-scoped `[NativeLinuxOnly]` on the three V-39 methods plus one antiphon rc policy row,
  a re-sealed `policyHash` and a re-recorded live-lane pin.
