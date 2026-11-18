# CARD-1984 review (round 2, Final, diff-only)

Verdict: clean. No blocker.

Reviewed source `8a815959599673e50cc101031e67121e12d7160b` on `origin/feat/card-task-632bba95`. Range `e0fd69f6bb36c79698f734330b02d775475af2d7..8a815959599673e50cc101031e67121e12d7160b`. Code owner `632bba95-784e-4e77-af0d-63f6bbd73f65` (Code, Succeeded, branch `feat/card-task-632bba95`). Author is Claude. This review is Grok. Round 1 review task `0425a1cc-fcf2-4d7c-85af-1109c2d22d69`.

The range is 9 files, +599/−29: the amendment plan, one Gotcha #18 sentence, `FixtureExecutable`, the wide-lane witness, three script fixtures, a limiter comment, and `tests/derived-impact-index.json`. No product source. Positive controls stay pending. Mutation is waived (operator 2026-11-12).

## B-1 helper

`FixtureExecutable` writes a unique `c1984-exec-<guid>` file under the temp directory, then starts `/usr/bin/install -m <mode> -- <source> <dest>` and waits for that child before returning. The test process never holds a write descriptor on the destination. `Copy` installs the source directly. `Start` throws `PlatformNotSupportedException` unless `OperatingSystem.IsLinux()`. Sync and async each use a new 30 second install deadline inside the helper.

This host has `/usr/bin/install`, GNU coreutils 9.1. Every call site sits behind `NativeLinuxGate.Require`, `[NativeLinuxOnly]`, or an `OperatingSystem.IsLinux()` assertion, so a Windows run of these methods does not reach `/usr/bin/install`. The wide lane's Windows width is 1; these methods do not execute there.

The helper comment says install unlinks any existing file first. A scratch probe on this host's GNU install 9.1 replaced a non-busy regular file and kept the same inode. The race property still holds: the write descriptor belongs to the install child, callers wait for that child before any exec, and each test uses its own temp path.

## Census

64 methods in the four families: HostJq 40 (38 `ScriptModelSpawnLimit`, 2 `ProcessSpawnLimit`) and Rolling 24 (`RollingVolumeRecycleScriptTests` 11, `RollingProductionMountTests` 12, `RollingVolumeRecycleCensusChurnTests` 1). W-10 walks the 62 wide-lane methods.

`C1025_Preflight_precedes_every_phase` and `C1025_Preflight_is_fresh_for_each_entry` are back on `ProcessSpawnLimit` and on `StrictKeep`. Both still go through `C1025RolloutFixture` to `C1008WrapperFixture`. Only the fresh-preflight method has key-less `[NotInParallel]`. Its body and `C1008_FIXTURE_CHILD_LIMIT` are unchanged. The diff adds the attribute, the 17.4 s comment, the helper's own 30 s install deadlines, and `ShouldBeEmpty` assertions on the new witnesses. No existing deadline, timeout, or assertion was loosened.

`C1025_Check_rejects_canonical_leaf_symlink` now uses `FixtureExecutable.Copy` for the disclosed overwrite case.

## W-10 and W-11

`aed5c821ba3ff30667f195816610693757c52f38` is the witness commit, before `0a53ea203b` (the fix). `In_process_executable_write_is_a_finding` asserts `ShouldNotBeEmpty` on six synthetic bodies. `Out_of_process_or_interpreted_script_is_not_a_finding` asserts `ShouldBeEmpty` on three. `Checkout_writer_reach_is_a_finding` asserts `ShouldNotBeEmpty` against a same-named `C1008WrapperFixture` stand-in. The live walks assert `ShouldBeEmpty`. CP-21 at the reviewed tip executed 36, passed 36, failed 0, skipped 0.

This review did not re-execute `aed5c821`. The commit message describes the walker and does not paste a "10 bodies / 2 findings" log. That count lives in the amendment prose. Red-first is supported by commit order and by assertions that fail when a finding is present. It is not supported by a red receipt from this review.

## Checkpoint

One serial checkpoint-tool run, `ANTIPHON_LINUX_COMPILER_POLICY=0`, `TEMP=/tmp`, no `-Runner`, no `-Platform`. 1-minute load at start was 3.30. The tool took its own build slot. Native cold output `bin-c1984-r2/`, deleted after the run. Receipt `scripts/validate-checkpoint-receipt.ps1` on `.antiphon/checkpoints/20261118-094956-d641/report.json` printed `CHECKPOINT SOURCE VALID` for `8a815959599673e50cc101031e67121e12d7160b`, 7 rows.

| CP | Result | Timing |
|---|---|---|
| CP-21 `ScriptModelSpawnLaneTests` | 36 passed, 0 failed, 0 skipped | 11.356 s |
| CP-22 `ProcessSpawnLimitTests` | 57 passed, 0 failed, 0 skipped | 14.844 s |
| CP-23 `HostJqPrerequisiteScriptTests` | 40 passed, 0 failed, 0 skipped | 272.052 s |
| CP-24 24 widened rolling methods | 63 passed, 0 failed, 0 skipped | 177.795 s |
| CP-25 C0824, RetiredTempContainerScript, HostJqFixtureClosure | 21 passed, 0 failed, 0 skipped | 244.171 s |
| CP-26 11-class guard union | 114 passed, 0 failed, 1 skipped | 215.609 s |
| CP-27 selector and timing labels | 52 passed, 0 failed, 0 skipped | 282.061 s |

Every row: commit `8a815959599673e50cc101031e67121e12d7160b`, dirty=0, sourceState=clean, buildSource=verified, slot=granted, waited=0s. One build, reused after. Wall 26m49s. Sequential-equivalent 20m18s. Unlisted commands: none. Verdict GREEN, exit 0. Host Debian GNU/Linux 12, 24 cores.

The CP-26 skip is `C1210_Windows_linux_only_outcomes_are_exact` on Linux. The plan expects 0 failed and at least 100 executed. 114 executed meets that. The receipt validator accepted the skip.

Recorded slow classes, all inside green rows: HostJq 587 s (40 tests), RollingVolumeRecycleScript 511 s (50), ImpactedClassesSelector 273 s (50), RetiredTempContainerScript 202 s (16), DerivedImpactIndex 147 s (27), WindowsUnitPlatformAdmission 62 s (6). None is a failure. The run is under 30 minutes.

Stress (HostJq 19/20, Rolling 10/10) stays extra evidence. This review did not repeat it.

## Other checks

`scripts/check-evidence-diff.ps1` over the range: commits=7, entries=0, violations=0.

`scripts/impacted-classes.ps1` kinds: `native-linux-gate-roster`, `spawn-limit-rosters`, `test-lane-category`. No migrations kind. Downgrade selector count: 0. The plan leaves `RemoteScriptContractTests`, the `VolumeRetirement*` classes, `RetiredTempContainerHostTests`, and `C1364BoundLandWaitProofTests` on the nightly Unit lane. They were not added to this selection.

`git merge-tree --write-tree` against the brief pin `f4d7830332f652376a84c42b45bc5c8d0f4fa07f` wrote tree `1934ee436c9e8646806fc422e3618aeef1877583`, exit 0. Live `origin/master` is `9cb2550e74a5dd18cae82b4788abf02e50521435`; merge-tree wrote `72888c860dd4d4b1a635b02c20126cd928b10dec`, exit 0. No merge was made.

No production factory or OS-selected production branch changed, so there is no OS-branch witness to bind. No new migration. No new session input wait. Parking does not apply.

R1: the new witnesses walk compiled methods. They do not poll a file, screen, or queue. R2: no new exact-text assertion on generated output. R3: no terminal-state change. R4: limiter attributes moved, and CP-22 ran `ProcessSpawnLimitTests` 57/57. The roster stays a class floor. R5: this review saw no repeated passes-alone red. The amendment records the 30 s preflight miss as pre-existing, alone and at the base, and names an orchestrator card. This stage left that deadline unchanged.

## Advisories

These do not hold the land. They belong on cards.

- The helper comment overstates GNU install. On coreutils 9.1 a non-busy replace can keep the inode. The out-of-process write still removes the ETXTBSY window this fix targets.
- Strict-lane write-then-exec remains beside the wide lane. `C1008_Host_proof_file_omits_credentials` and `C1008_Host_proof_admits_only_an_authenticated_private_origin` still write a shebang git shim in the test process and chmod it, on `ProcessSpawnLimit`, and both names are on `StrictKeep`. A wide-lane `Process.Start` in the same process can fork that descriptor. D-11 scopes the new rule to the wide lane, and round 1 allowed write-then-exec to stay on the strict limiter. Those two methods are unchanged by this diff.
- `C1025_Preflight_is_fresh_for_each_entry` still uses the 30 s wrapper deadline. CP-23 alone was 17.4 s, so 30 s is under twice that sample. The key-less `[NotInParallel]` is the isolation this diff adds. Rebudgeting is a later card.

## Evidence

--- review evidence ---
subjectTaskId: 632bba95-784e-4e77-af0d-63f6bbd73f65
reviewedSourceSha: 8a815959599673e50cc101031e67121e12d7160b
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: e0fd69f6bb36c79698f734330b02d775475af2d7..8a815959599673e50cc101031e67121e12d7160b
