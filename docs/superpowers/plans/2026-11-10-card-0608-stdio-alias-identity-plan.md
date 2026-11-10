# CARD-0608: restrict CloseStdioAliases to pipe and socket identities

Date: 2026-11-10. Stage: Code. Base: `a69789c77865b2db502500ebd26d58f72a8013c0`.
Card ask: narrow `PosixProcessSpawner.CloseStdioAliases` so a descriptor is closed only when its
`readlink` identity starts with `pipe:[` or `socket:[`. This does not reopen CARD-0594 D-1
(readlink text, not `st_ino`).

## Slices

- S1. Failing Linux witness `PosixProcessSpawnerStdioAliasTests`, plus the Windows rc exclusion
  and the paired `policyHash` / live-lane `PinnedInputDigest` refresh.
- S2. `IsPipeOrSocketIdentity` gates the close. Behaviour is otherwise unchanged: an identity
  outside the recorded set stays open, and non-Linux still returns before the scan.

## Verification design

V-1. `PosixProcessSpawnerStdioAliasTests.CloseStdioAliases_closes_pipe_and_socket_aliases_and_leaves_same_path_files_open`
closes a real `pipe:[` alias and a real `socket:[` alias that are in the recorded set, and leaves
a second pipe whose identity is not in the set open.

V-2. The same method leaves a same-path plain file open, and leaves open a file whose path
contains the text `pipe:[` but does not start with `pipe:[` or `socket:[`.

R-1. The `OperatingSystem.IsLinux()` returns in `StartDetached`, `BecomeSessionLeaderOrExit`,
`RecordStdioIdentity` and `CloseStdioAliases` stay. This Linux task does not execute the
non-Linux return.

R-2. Classification, slow-registry, layering, mechanical-contract and platform-guidance guards
stay green. `tests/impacted-classes.json` is not edited.

R-3. `Card1277PtyHostFixtureTests` runs on Linux because `src/Antiphon.PtyHost/**` matches the
pty-host fixture row. That row is `windows: required`, so the Windows lane stays owed.

R-4. The new `[NativeLinuxOnly]` row agrees with `ReleaseGatePlatformTests.LinuxOnlyWindowsExclusionPolicy`
and `ReleaseGatePlatformTests.C1686_LinuxOnlyCensus`.

R-5. `tests/test-execution-policy.json` matches the nightly-live-lane impacted-class row, so
`LiveLaneRunnerTests` (4) and `NightlyCoreTests` (1) run on this commit.

PC-1 stays pending for SourceLanding Mutation. One local mutant, deleting the
`IsPipeOrSocketIdentity` check, must make V-2 red and is not a discharged PC.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---:|---|---|
| CP-1 | S2 | tests/Antiphon.PtyHost.Tests -> bin-c0608-alias/ | c0608-alias | `/*/*/(PosixProcessSpawnerStdioAliasTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuardTests*)/*` | V-1, V-2 | 0 failed | 5 | 10 | true | TEMP=/tmp;TMPDIR=/tmp |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c0608-guards/ | c0608-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | R-2 | 0 failed | 77 | 20 | true | TEMP=/tmp;TMPDIR=/tmp |
| CP-3 | S2 | CP-2 | c0608-fixture | `/*/*/Card1277PtyHostFixtureTests/*` | R-3 | 0 failed | 3 | 6 | true | TEMP=/tmp;TMPDIR=/tmp |
| CP-4 | S2 | CP-2 | c0608-linux-only | `/*/*/ReleaseGatePlatformTests/(LinuxOnlyWindowsExclusionPolicy*)\|(C1686_LinuxOnlyCensus*)` | R-4 | 0 failed | 2 | 5 | true | TEMP=/tmp;TMPDIR=/tmp |
| CP-5 | S2 | CP-2 | c0608-lane | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreTests*)/*` | R-5 | 0 failed | 5 | 15 | true | TEMP=/tmp;TMPDIR=/tmp |

CP-2 counts the eight named classes: policy 41, classification guard 1, slow-registry 5,
tripwire 2, native-linux witness 3, mechanical contract 11, `TaskPlatformGuidanceTests` 5,
layering 9. The four `RunnerDefaultGuidanceTests` methods in that same file are a different
class and stay outside this filter. CP-5 counts the four live-lane methods and the one
nightly-core method.
