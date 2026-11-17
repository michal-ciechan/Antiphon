# C1514 native factory test: absent HistoryPath is unbound by design (rc/2026-11-24)

Debug task `4d0255a8`, 2026-11-17. Base `300c8a99d0d267a39791410b7540189ab702f251` (current
`origin/master`; contains rc pin `870a93d21f2b`). Linux only: nothing ran on the desktop.

## Failure

Windows Full run `06863906697748fc8354bcbe527119e9` on `rc/2026-11-24` (`870a93d21f2b`), chunk
`session-runner-all-profile-17`, 2026-11-17T04:53:42Z. TRX (recorded by the caller):
`C:\Antiphon\releases\candidates\rc-2026-11-24-870a93d21f2b\logs\2026-11-17-0029-06863906\session-runner-all-profile-17.trx`,
total=1 executed=1 passed=0 failed=1, 0.107 s.
`C1514HostLoadNativeTests.Windows_ProductionFactoryPreservesHostSamples` threw `ShouldAssertException` at
`C1514HostLoadNativeTests.cs:38` (`VerifyAsync`, called from line 28):
`absentProvider.GetRequiredService<IOptions<HostStatsSettings>>().Value.HistoryPath should be
"C:\Users\lndco\AppData\Local\antiphon\host-load" but was null`.
This class had never run natively in an earlier qualification run, so there is no Windows baseline.

## Determination: the test is stale, production is correct

1. The test (`4d0105ea1`, CARD-1514, 2026-11-07) asserts the pre-CARD-1893 contract: options binding
   in `AddHostStats` filled an absent `HistoryPath` with
   `Path.Combine(GetFolderPath(LocalApplicationData), "antiphon", "host-load")`.
2. CARD-1893 changed that deliberately. `9cf3e4917` ("harden collector paths", cherry-pick of
   `ccd0d185d`, Merge 253) deleted the `settings.HistoryPath ??= ...` line from
   `src/Antiphon.SessionRunner/HostStatsRoutes.cs` and added `HostLoadStoragePath.Resolve`.
   Without `DoNotVerify`, `GetFolderPath(LocalApplicationData)` returns `""` on Linux when
   `~/.local/share` does not exist, and the old line then produced the cwd-relative
   `antiphon/host-load` under the read-only application directory. The collector plan
   (`docs/superpowers/plans/2026-11-16-card-1893-collector-plan.md`, lines 9-10) states the new rule:
   resolve an absent path from absolute local application data using `DoNotVerify`, fall back to
   absolute user-profile data, never cwd.
3. The new contract is pinned by `C1893CollectorDiagnosticsTests.Production_factory_keeps_absent_path_optional_and_resolves_absolute_profile_data`:
   `settings.HistoryPath.ShouldBeNull("absence must not be replaced by a cwd-relative path during options binding")`.
   That is the exact opposite of `C1514HostLoadNativeTests.cs:38`. Both cannot pass on one host.
   CARD-1893 updated its own tests and missed the older native test that asserts the same option.
4. What a null `HistoryPath` means now: history is NOT disabled (`HistoryEnabled` still defaults to
   true). `HostLoadCaptureService.ExecuteAsync` resolves the path lazily at the collector's first
   loop iteration, after `Task.Yield`: `GetFolderPath(LocalApplicationData, DoNotVerify)` and
   `GetFolderPath(UserProfile, DoNotVerify)` go to `HostLoadStoragePath.Resolve(null, local, profile)`,
   which returns `local\antiphon\host-load` when `local` is fully qualified, else
   `profile/.local/share/antiphon/host-load`, else throws; a throw or open failure becomes a
   bounded collector diagnostic (`stage=resolve|open`, `pathClass`) and a 5 s retry, never a host
   crash. The diagnostic path class for the default is `local-data`.
5. Durable history on the Windows desktop runner is unchanged. Neither
   `src/Antiphon.SessionRunner/appsettings.json` nor any deploy script sets `HistoryPath`, so the
   runner takes the lazy default. On Windows `LocalApplicationData` always exists for a loaded
   profile, so `DoNotVerify` and the old verified lookup give the same value:
   `%LOCALAPPDATA%\antiphon\host-load`, the value the failure message printed as expected.

## The test's checks and the service account question

`VerifyAsync(arm, expectedCores)` builds three providers: (a) `absentProvider` with no
`SessionRunner:HostStats:HistoryPath` key (the failing assertion), (b) an empty-string key, which
must throw `InvalidOperationException` from `HostStatsSettings.Validate` (unchanged, still correct),
(c) a configured private root. On (c) it asserts the production `SystemHostStatsProbe` factory
picked `arm` (`WindowsHostStatsProbe` with `GetActiveProcessorCount(ALL_PROCESSOR_GROUPS)` cores,
or `LinuxHostStatsProbe` with `/sys/devices/system/cpu/online` cores), reads two samples a second
apart, and round-trips one projected sample through `HostLoadSpool`.

The account does not matter. The expected string is computed from `Environment.GetFolderPath` in
the test process itself, not hard-coded; `lndco` in the message is just that account's profile.
On Windows .NET resolves `LocalApplicationData` with `SHGetKnownFolderPath` for the process token,
not from the `LOCALAPPDATA` environment variable, so a supervisor started with
`Win32_Process.Create` (CARD-1667) sees the same folder as the collector it hosts. The run proved
the lookup worked there: it printed a full `C:\Users\...` value. The failure is the `null` actual.

## Fix (test only)

`tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeTests.cs`, `VerifyAsync`: the absent-path
provider must keep `HistoryPath` null, and the default the collector resolves for this process
(`HostLoadStoragePath.Resolve(null, LocalApplicationData DoNotVerify, UserProfile DoNotVerify)`,
the same inputs as `HostLoadCaptureService`) must equal
`Path.Combine(LocalApplicationData DoNotVerify, "antiphon", "host-load")` with that folder fully
qualified. On Windows this is the old `%LOCALAPPDATA%\antiphon\host-load` value, so the native arm
still proves the durable default; on Linux it pins the `local-data` branch of the resolver, which
no other Linux test pinned (C1893 pins only the profile fallback and the configured path).
No production file changes.

The default-path decision is already factored: `HostLoadStoragePath.Resolve(configured, localData,
userProfile)` is pure and takes the platform folders as parameters (CARD-1893). A Linux test cannot
feed it Windows-shaped strings, because `Path.IsPathFullyQualified("C:\\...")` is false on Linux;
the Linux witness therefore drives the same function with the Linux folders.

## Red first

The Linux-runnable witness for the same reason is the class's own Linux arm,
`Linux_ProductionFactoryPreservesHostSamples`: it shares `VerifyAsync` and line 38 and has been
committed since `4d0105ea1`. It is red at the base for the identical assertion (CP-1), and green
after the fix (CP-2). A new witness asserting the CARD-1893 contract cannot be red at the base:
production already satisfies it (C1893 pins it), the stale expectation is the only defect.

## Second native class

`C1514HostLoadNativeOverheadTests` does not share this defect. Both arms launch full runners with
`--SessionRunner:HostStats:HistoryPath <root>/spool` and never assert the options default, so the
absent-path change does not reach them. Its Windows arm has never run natively either; its own
risks (four 140 s children, 1-point CPU and 25 ms p95 budgets on a loaded Windows host) are
unmeasured. Prediction: no `HistoryPath` failure; any red there is a new cause.

## Why missed

Written to the template in
[2026-11-17-why-missed-c1514-native-history-path.md](2026-11-17-why-missed-c1514-native-history-path.md).

## Owed

Native Windows proof of the fix is owed to the next rc Full run: chunk
`session-runner-all-profile-17` (or wherever the class lands), filter
`/*/*/C1514HostLoadNativeTests/Windows_ProductionFactoryPreservesHostSamples`. It is not claimed here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | red | tests/Antiphon.SessionRunner.Tests -> bin-c1514np-red/ | native-red | `/*/*/C1514HostLoadNativeTests/Linux_ProductionFactoryPreservesHostSamples` | D-1 | intended assertion failure at C1514HostLoadNativeTests.cs:38, HistoryPath was null | 1 | 15 |
| CP-2 | green | tests/Antiphon.SessionRunner.Tests -> bin-c1514np-green/ | native-green | `/*/*/C1514HostLoadNativeTests/Linux_ProductionFactoryPreservesHostSamples` | D-1 | 0 failed | 1 | 15 |
| CP-3 | green | CP-2 | collector-and-stats | `/*/*/(C1893CollectorDiagnosticsTests*)\|(C1514HostLoadSpoolTests*)\|(C1514HostLoadWireTests*)\|(C1514HostLoadSampleTests*)\|(C1514HostLoadAuthorityTests*)\|(HostStatsEndpointTests*)\|(HostStatsSamplerTests*)\|(HostStatsStoreTests*)\|(RunnerCapabilitiesTests*)/*` | R-1 | all listed, 0 failed | 9 | 7 |
| CP-4 | green | tests/Antiphon.Tests -> bin-c1514np-tests/ | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-2 | all listed, 0 failed, C1210 Windows skip expected | 11 | 15 |
| CP-5 | green | CP-4 | server-ingest-replay | `/*/*/(HostTopConsumerUpgradeReplayTests*)\|(C1514HostLoadHistoryTests*)/*` | R-1 | all listed, 0 failed | 5 | 5 |
| CP-6 | green | CP-4 | selector-timing | `/*/*/(TestTimingOperationLabelTests*)\|(ImpactedClassesSelectorTests*)/*` | R-3 | all listed, 0 failed | 2 | 8 |

D-1: the absent-path contract on the native factory test. R-1: CARD-1893 collector CP-2 and CP-4
classes. R-2: the 11-class guard union (C1729 must print `VIOLATIONS 0 LISTED 162`). R-3: timing
labels and the selector.

## Results

Two runs through `tools/Antiphon.Checkpoints` (`start --plan <this file> --serial`), plan imported
read-only first (`import --plan`, 6 rows). Every slot `granted waited=0s`; outputs deleted by the tool
(CP-1's kept `bin-c1514np-red/` removed by hand). Load at start: CP-1 `17.07 24.45 29.62` (started a
reading above the 16 rule; it fell to 11.4 just before), green run `13.20 20.87 23.81`.

Red run `20261117-051305-9fff` at `9479e49e78734da5177521d7c6ffd46b9c8f6828` (base plus these notes only):

```
CHECKPOINT CP-1 commit=9479e49e78734da5177521d7c6ffd46b9c8f6828 build=ok filter=/*/*/C1514HostLoadNativeTests/Linux_ProductionFactoryPreservesHostSamples executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-4d0255a8/.antiphon/checkpoints/20261117-051305-9fff/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9479e49e78734da5177521d7c6ffd46b9c8f6828 sourceState=clean buildSource=verified
FAILED Antiphon.SessionRunner.Tests.C1514HostLoadNativeTests.Linux_ProductionFactoryPreservesHostSamples (CP-1) ShouldAssertException: absentProvider.GetRequiredService<IOptions<HostStatsSettings>>().Value.HistoryPath     should be "/home/app/.local/share/antiphon/host-load"     but was null -> rows/CP-1/failures.md
```

The stack ends at `C1514HostLoadNativeTests.cs:line 38` in `VerifyAsync`, the Windows line.

Green run `20261117-053608-6ba0` at `e221907dfa93ad55019c6675811180957aad82ba` (fix `dc86acc476b534c17c7a352f92d2db91572a5f13` plus index regen), `verdict: GREEN exit=0`, wall 26m10s:

```
CHECKPOINT CP-2 commit=e221907dfa93ad55019c6675811180957aad82ba build=ok filter=/*/*/C1514HostLoadNativeTests/Linux_ProductionFactoryPreservesHostSamples executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4d0255a8/.antiphon/checkpoints/20261117-053608-6ba0/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=e221907dfa93ad55019c6675811180957aad82ba sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=e221907dfa93ad55019c6675811180957aad82ba build=reused filter=/*/*/(C1893CollectorDiagnosticsTests*)|(C1514HostLoadSpoolTests*)|(C1514HostLoadWireTests*)|(C1514HostLoadSampleTests*)|(C1514HostLoadAuthorityTests*)|(HostStatsEndpointTests*)|(HostStatsSamplerTests*)|(HostStatsStoreTests*)|(RunnerCapabilitiesTests*)/* executed=52 passed=52 failed=0 skipped=0 trx=/work/worktrees/task-4d0255a8/.antiphon/checkpoints/20261117-053608-6ba0/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=e221907dfa93ad55019c6675811180957aad82ba sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=e221907dfa93ad55019c6675811180957aad82ba build=ok filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=114 passed=114 failed=0 skipped=1 trx=/work/worktrees/task-4d0255a8/.antiphon/checkpoints/20261117-053608-6ba0/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=e221907dfa93ad55019c6675811180957aad82ba sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=e221907dfa93ad55019c6675811180957aad82ba build=reused filter=/*/*/(HostTopConsumerUpgradeReplayTests*)|(C1514HostLoadHistoryTests*)/* executed=26 passed=26 failed=0 skipped=0 trx=/work/worktrees/task-4d0255a8/.antiphon/checkpoints/20261117-053608-6ba0/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=e221907dfa93ad55019c6675811180957aad82ba sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=e221907dfa93ad55019c6675811180957aad82ba build=reused filter=/*/*/(TestTimingOperationLabelTests*)|(ImpactedClassesSelectorTests*)/* executed=52 passed=52 failed=0 skipped=0 trx=/work/worktrees/task-4d0255a8/.antiphon/checkpoints/20261117-053608-6ba0/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=e221907dfa93ad55019c6675811180957aad82ba sourceState=clean buildSource=verified
```

CP-2 printed `C1514 native factory=Antiphon.SessionRunner.SystemHostStatsProbe arm=Antiphon.SessionRunner.LinuxHostStatsProbe cores=24`.
CP-4 classes: TestClassificationPolicyTests 42, DerivedImpactIndexTests 27, C1729LayeringGuardTests 11,
MechanicalPcContractTests 11, WindowsUnitPlatformAdmissionTests 7 (6 passed, 1 NotExecuted),
TaskPlatformGuidanceTests 5, SlowTestRegistryMergeTests 5, NativeLinuxPolicyWitnessTests 3,
SlowTestTripwireTests 2, TestClassificationGuardTests 1, TestLaneCategoryGuardTests 1.
`C1729LayeringGuardTests` printed `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`; `DerivedImpactIndexTests`
printed `derived-impact windowsOnly count=31 linuxOnly count=1`. The NotExecuted case is
`WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`
(`Skipped: CARD-1210: the child skip-roster audit requires Windows placement.`).
`validate --expected-source-sha e221907dfa93ad55019c6675811180957aad82ba --rows CP-2,CP-3,CP-5,CP-6`:
`CHECKPOINT SOURCE VALID rows=4`. The whole-run validate returns `row_failed` for CP-4 `skipped=1` only.

Derived impact index: driver built first (`dotnet build scripts/regenerate-derived-impact-index/...`
under the build slot), run 1 `changed` (one edge, `HostLoadStoragePath.cs` -> `C1514HostLoadNativeTests`,
because the test now names that type), run 2 `unchanged`; committed as `e221907df`.
Nightly pins untouched (no input changed): policyHash `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`,
PinnedInputDigest `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`,
LegacyExclusionDigest `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`.
Mutation checks waived by the operator (2026-11-12). Native Windows proof: owed (see Owed).
