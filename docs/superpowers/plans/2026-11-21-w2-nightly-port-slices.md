# W2-nightly port slices (ps1 to C#)

Status: plan, 2026-11-21. Read-only survey at master `93ed6adb1`. It refines the W2-nightly row of
`docs/superpowers/plans/2026-11-19-ps1-to-csharp-migration-plan.md` (section 4.2) into 1-3 h slices.

Direction (decided): plain C# in `scripts/lib/Antiphon.Nightly` (no PSObject or System.Management.Automation);
entries become `antiphon-scripts <cmd>` subcommands (run through `scripts/antiphon` / `scripts/antiphon.cmd`);
ported ps1 files are deleted with no shims and no fallbacks; tests are C# in-process and never launch a ps1 for
ported code (a test may launch a still-PowerShell production entry until it is ported). Windmill entries
(`nightly-run.ps1`, `nightly-health.ps1`, `release-cut.ps1`) stay 5.1 launchers. The restart chain
(`session-runner-restart-health.ps1`, `restart-apphost.ps1`, `apphost-common.ps1`, ...) stays PowerShell and must not
depend on the compiled core. `scripts/test-live-lane-run.ps1` pins a digest over its ProtectedInputs.

Done before this plan: rc ledger/admission/boundary/chunk digest/input manifest/census/baseline/incremental
plan/forced full/historical evidence, post-land early warning, evidence policy, nightly probe supervisor, rc dirty
order and shadow sample. rc-run-evidence landed right after the survey (6bd756265: RcRunEvidenceRecorder.cs,
RcChunkOutcome.cs; nightly-run-impl.ps1 and nightly-tests-impl.ps1 call it directly), and the rc port corpus feeds plain
values (e8c0b7102). Line numbers in nightly-*-impl.ps1 below are from before those commits.

## 1. Dependency graph

### 1.1 Dot-source edges (`*` = outside W2-nightly; lazy = inside a function)

Entries:
- `nightly-run.ps1` -> nightly-common, nightly-coverage, release-gate*, release-selection*, nightly-run-impl
- `nightly-tests.ps1` -> nightly-common, nightly-coverage, release-gate*, release-selection*, nightly-tests-impl
- `nightly-health.ps1` -> nightly-common, lib/nightly-health
- `summarize-test-phases.ps1` -> nightly-common, test-phase-evidence (re-runs itself in pwsh under 5.1)
- `select-test-rosters.ps1` -> nightly-common, test-selection*, test-timings (lazy)
- `run-live-lane.ps1` -> live-lane-run
- `live-lane-status.ps1` -> live-lane-receipt
- `nightly-report.ps1` -> nothing (leaf; HTTP through `-HttpShim`)

Libraries:
- nightly-run-impl -> release-gate*, nightly-coverage, rc-run-evidence, test-phase-paths, release-selection* (:125),
  toolchain-floor (lazy :640)
- nightly-tests-impl -> nightly-dispatch, nightly-coverage, nightly-core, qualification-supervisor-heartbeat,
  rc-run-evidence, test-phase-evidence, release-selection* (:680); lazy toolchain-floor (:32),
  nightly-owned-process (:553), checkpoint-process-runner*, build-driver-environment*, test-timings (:1891-1921)
- qualification-supervisor-heartbeat -> test-phase-evidence
- test-phase-evidence -> nightly-common, nightly-core, test-phase-paths
- test-phase-paths -> nightly-core
- nightly-common -> nightly-core, the `-SeamsPath` file (:397)
- nightly-coverage -> nightly-core (conditional)
- nightly-owned-process -> Add-Type `nightly-owned-process.cs` (914 lines, not in the core project),
  build-driver-environment* (lazy)
- live-lane-run -> nightly-common, release-gate*, nightly-owned-process, live-lane-outcome
- live-lane-receipt -> live-lane-fingerprint -> rc-project-selection*, live-lane-attestation -> test-selection*
- live-lane-manifest -> nightly-common, nightly-coverage, nightly-core, nightly-tests-impl
- qualification-supervisor -> rc-qualification-status*
- c487-harness -> checkpoint-process-runner* (lazy)
- Leaves: nightly-core (loader), nightly-dispatch (but see 1.2), lib/nightly-health, test-timings, toolchain-floor,
  qualification-host-stats, live-lane-outcome, nightly-report.ps1.

### 1.2 Cross-file calls that shape the port

- nightly-dispatch and nightly-tests-impl are one unit. `Invoke-NightlyDispatchCoordinator` and
  `Invoke-NightlyPreparedSchedule` dot-source `Prepare-NightlySuiteExecution`, `Invoke-NightlyScheduledChunk` and
  `Complete-NightlySuiteExecution` into the scope of `Invoke-AntiphonNightlyTests`, sharing about 20 locals
  (`$policy`, `$LogRoot`, `$invocationId`, `$overallFailed`, `$coverageComplete`, `$testsPassed`, `$reasons`,
  `$suiteResults`, `$npmOk`, `$dotnetOk`, `$activeNative`, `$envMap`, ...). nightly-tests-impl L682-1883 plus
  nightly-dispatch L49-135 port as one stateful C# class.
- `Invoke-NightlyProduceDiscovery` (the only function in nightly-coverage) calls `Invoke-NightlyOwnedProcess` from
  nightly-tests-impl through scope; that is why live-lane-manifest dot-sources nightly-tests-impl. From the nightly
  libraries live-lane-manifest uses only `Get-NightlyFileSha256`, `Invoke-NightlyProduceDiscovery`,
  `Test-NightlyCoreAvailable`.
- nightly-run-impl calls release-gate* (`Enter/Exit-ReleaseGateNativeLock`, `$ReleaseGateProfiles`),
  release-selection* (`Assert-ReleaseSelectionCapability`, `New-/Read-ReleaseSelectionAttempt`,
  `Invoke-ReleaseSelectionHop`), nightly-common (lock, ownership, git, state), `Get-ToolchainFloorWarnings`,
  `Resolve-QualificationTimingRoot`. The `-SelectionAttemptPath` tests hop goes through `Invoke-ReleaseSelectionHop`:
  a JSON-splatted pwsh child whose switch list is read from the AST of `nightly-tests.ps1`
  (`release-selection.ps1:354-447`).
- nightly-tests-impl calls test-phase-evidence (`Remove-TestPhaseVariables`, `Enable-TestPhaseExecutionEnvironment`,
  `Get-TestPhaseClassBasisMap`, `Get-TestPhaseTimingDirectory`, `Write-TestPhasePlannedManifest`,
  `Write-TestPhaseFailureMarker`, `Open/Close-TestPhaseExecutedContext`, `Complete-TestPhaseExecutedProjection`,
  `Write-TestPhaseExecutedManifest`, `Invoke-TestPhaseRunSummary`), `Write-QualificationActiveChunkIdentity`,
  `Write-TestTimingEvent`, `Get-ToolchainFloorReport`, release-selection* (`Read-ReleaseSelectionAttempt`,
  `Resolve-ReleaseSelectionMembers`).
- The heartbeat calls `Read-TestPhaseGenerationPrefix`.
- lib/nightly-health uses only nightly-common, never the core (runs under 5.1): `Get-NightlyUtcNow`,
  `Get-NightlyLondonTimeZone`, `ConvertTo-NightlyLondonLocal`, `ConvertFrom-NightlyJsonMap`, `Write-NightlyAtomicJson`,
  `Import-NightlySeams`, `New-NightlyRunId`.
- live-lane-fingerprint calls rc-project-selection (`Read-RcProjectGraph`, `Resolve-RcProjectInput`,
  `Read-RcProjectBlob`) and test-selection (`Invoke-TestSelectionGit`, `Get-TestSelectionBlob`,
  `Get-TestSelectionSorted`, `Get-TestSelectionDigest`, `ConvertTo-TestSelectionCanonicalJson`; C# twin at
  `Antiphon.Scripts/PsJson.cs:155`).

### 1.3 Outside callers that keep in-scope files alive

| File | Kept alive by |
|---|---|
| nightly-common.ps1 | release-gate, release-selection, release-cut (`New-NightlyRunId`), release-candidate, release-status, register-release-gates, publish-release, promote-release; `ReleaseAuthority.Scripts` digest (`ReleaseAuthority.cs:255`; `FileSha256` throws on a missing file, :295); ProtectedInputs; the `helper-nightly` row in `test-selection-policy.json:3603` |
| nightly-core.ps1 | release-gate, release-selection (`Get-AntiphonScriptsCli`), test-selection, publish-release, promote-release, export-nightly-prior-failures, export-nightly-stopped-run-seed, merge-nightly-seeds; `ReleaseAuthority.Scripts`; ProtectedInputs |
| nightly-coverage.ps1 | dot-sourced without calls by release-selection:194, publish-release:64, promote-release:39; `ReleaseAuthority.Scripts`; ProtectedInputs |
| test-phase-paths.ps1 | `release-cut.ps1:123` (`Resolve-QualificationTimingRoot`) |
| toolchain-floor.ps1 | the restart chain: `session-runner-restart-health.ps1:56`, `restart-server.ps1:12`. Stays permanently |
| test-timings.ps1 | `run-checkpoint.ps1:588` (W2-checkpoint), select-test-rosters |
| qualification-supervisor.ps1 | only `scripts/qualification/{start-supervisor,supervise-rc,observe,qualification-stop}.ps1` |
| heartbeat | `scripts/qualification/supervise-rc.ps1:34` |
| c487-harness.ps1 | about 40 drivers in other families (test-build-slot, test-run-checkpoint, test-release-gate, test-change-aware-release, ...) |
| nightly-run.ps1 | Windmill; `release-cut.ps1:175` (in-process `&`, `-PassThru`, `-SeamsPath`, reads `.ExitCode` and `.Refusal`); `qualification/launch-rc.ps1:74` |

W2-rc harness cases that use nightly-run-impl or nightly-tests-impl functions:

| Harness | What it uses |
|---|---|
| test-release-gate.ps1 | `New-C599RunFx` (:975) copies nightly-run, nightly-tests, nightly-report, select-test-rosters into a seeded clone; `Invoke-C599Run`; C599_ExecutionArtifacts, C599_Prerequisites, C599_MasterStateIsolation, C599_RunSummaryIdentity, C599_SharedLock, C599_Continuation |
| test-rc-platform.ps1 | C1323_PlatformExecution, C1415_SkipExclusions, LinuxSessionRunnerAdmission |
| test-change-aware-release.ps1 | `New-Fx` copy list (:42), `Invoke-FxRoster`, C1313_ParameterHops (runs release-cut with seams) |
| test-rc-roster-repairs.ps1 | `Test-Costs`, `Test-Explicit` |
| test-class-cost-measurement.ps1 | `Get-NightlyClassCostTable`, `Get-NightlyLaunchClassCost` |
| test-release-selection-hop-seed.ps1 | `Write-NightlyHopFailure`, the hop cases |
| tests/fixtures/card1336-release-gate-followups.ps1 | `New-C1336Candidate` |

### 1.4 Thin wrappers versus files that still hold logic

| File | Thin over the core | Still holds logic |
|---|---|---|
| nightly-tests-impl (1948) | `Get-NightlyLaunchClassCost`, `ConvertTo-NightlyProfileLaunchGroups`, `ConvertTo-NightlyBoundaryLaunchGroups` | `Get-NightlyClassCostTable`, the partition loop in `...Indexed`, the ceilings in `Get-NightlyProfileLaunchPlan`, the executor L682-1883 (NightlyPolicy x14, QualificationTiming x10, NightlyChunkVerdict x10, NightlyWatchdog x8) |
| test-phase-evidence (2420) | TRX and stream readers; summary rows (partly `TestPhaseSummaryFast`); text building (`TestPhaseText` x32) | the journal and publication state machine L597-1780; planned and executed manifests; run-summary orchestration |
| nightly-common (1082) | `Get-NightlyParentProcess`, `Write-NightlyAtomicJson` | lock and reclaim L534-1079 (about 550 lines), reparse and ownership L43-270, process environment, seams |
| nightly-run-impl (928) | `NightlyRunParts.*`, `ReleaseGateCredit.*` | the run core L127-809, the hop, the result record |
| nightly-core (436) | evidence-file facade L298-436, `Read/Write-NightlyJsonFile` | the loader |
| nightly-dispatch (135) | 3 wrappers over `DispatchSchedule` | `Write-NightlyDispatchSchedule`, the coordinator, the prepared schedule |
| nightly-coverage (58), test-phase-paths (61) | mostly thin | `Invoke-NightlyProduceDiscovery` orchestration; the 5.1 fallback |
| nightly-owned-process (54) | `NativeProcessOwner.Start` | WindowsApps pwsh resolution; `BuildDriverEnvironment` |
| heartbeat (644), lib/nightly-health (956), test-timings, toolchain-floor, qualification-host-stats (not wired), qualification-supervisor, nightly-report, the live-lane libraries | - | all logic. live-lane-manifest also holds about 845 lines of inline C# (L1045-1270, L1742-2362) that move as they are |

## 2. Seams

Today: `$script:NightlySeams` (`nightly-common.ps1:378`) holds UtcNow, Git, StartProcess, IsProcessAlive,
GetProcessStartTimeUtc, GetParentProcess, ResolveReparse, DockerInfo, DiskFree, TracePath, WatchdogSnapshot (and,
read elsewhere, GetBootId, GetHostId, ToolchainVersions, HostPlatform, E2EPrerequisites, WaitCleanup,
NativeActiveCount, WindmillApi, RecipientView, NotificationSink, AllowOfflineNotify). It travels as a ps1 file,
dot-sourced in every process and forwarded across both hops; release-cut passes the same file to release-gate's
seams. Other seams: nightly-report `-HttpShim`; the Fixture parameter set of `run-live-lane.ps1`;
`NativeProcessHooks`; c487-harness `Write-C487Seams` and `Write-C487NativePassSeams` (a fake test host that writes
TRX, diag and discovery from the argv).

Copy: `PostLandSettings` (`PostLandEarlyWarning.cs:96`; delegates, filled by the CLI from the environment, built
directly by tests), the `EvidenceGit` delegate (`EvidencePolicy.cs:35`), the injected clock in
`NightlyProbeSupervisor.Run` (:228), `NativeProcessHooks`, `CheckpointProcessRunner` `beforeStart` (:42); test side
`FakeTimeProvider`, `JumpingClock`, `RcInputFixtureRepository`, `Antiphon.ScriptHarnessHost` modes, the staged
`OwnedChild`. Do not repeat: the static `ReleaseAuthority.CardReader` (:246), the statics
`QualificationTiming.BudgetWatch`/`ReadLog` (:333-337; why `QualificationTimingTests` is NotInParallel), the static
`EvidenceFiles`.

Recommendation: one `NightlyHost` per run; the CLI uses `NightlyHost.Production(...)`, tests construct it; nothing
static.

```csharp
public delegate NightlyGitResult NightlyGit(string workingDirectory, IReadOnlyList<string> arguments);
public sealed record NightlyLaunch(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory,
    int TimeoutMilliseconds, IReadOnlyDictionary<string, string> Environment, string LogPath,
    NightlyCustody Custody /* Watched (hops) | Owned (job custody) */, NativeProcessHooks Hooks);
public interface INightlyLauncher { NightlyLaunchResult Run(NightlyLaunch l); bool WaitCleanup(NightlyLaunchResult r); int NativeActiveCount { get; } }
public interface INightlyProcessTable { int CurrentPid { get; } DateTime? StartTimeUtc(int pid); bool IsAlive(int pid); NightlyParent Parent(int pid); string BootId(); string HostId(); }
public interface INightlyHostProbe { string Platform(); NightlyProbeRow Docker(); NightlyProbeRow DiskFree(); string ToolVersion(string tool); NightlyE2EResult E2E(...); }
public sealed class NightlyHost {
  TimeProvider Clock; NightlyGit Git; INightlyLauncher Launcher; INightlyProcessTable Processes; INightlyHostProbe Probe;
  EvidenceFileSystem Evidence;            // instance over System.IO.Abstractions; EvidenceFiles static becomes a facade until S28
  Func<string,string> ResolveReparse; Action<ProcessStartInfo> PrepareChild;   // BuildDriverEnvironment, filled by the CLI
  Func<SelectionAttemptRequest, SelectionAttemptResult> SelectionAttempts;      // CLI: select-release in-process, release-selection-worker.ps1 on NotHandled
  TextWriter Out, Error, Trace;
}
```

Families add their own seam objects: `NightlyHealthSeams` (Windmill API, watchdog snapshot, notification sink,
recipient view, AllowOfflineNotify), `NightlyReportHttp` (method, uri, headers, body), `LiveLaneRunSeams`.

Rules:
1. Seams replace OS observation only, never decisions (as the comment on `Get-NightlyExecutionPlatform` says).
2. Seams never cross a process. The CLI has no seams flag. Each launcher keeps its `-SeamsPath` parameter for
   binding, but refuses a non-empty value with exit 3 `seams-path-unsupported`, still printing the completion record.
3. Hops are asserted at the launcher seam: a `RecordingLauncher` captures the argv (as the C487 EXEC trace does).
   Where a test needs the child's behaviour (ParameterHops), an `InProcessHop` launcher parses the argv with
   `NightlyTestsOptions.Parse` and calls `NightlyTests.Run` with the same host.
4. The one pwsh child the nightly CLI keeps is W2-rc's existing `release-selection-worker.ps1` fallback, through
   `SelectionAttempts` (precedent: `PostLandCommand` still starts `impacted-classes.ps1` under pwsh).
5. New C# test helpers in S01 under `tests/Antiphon.Tests/Scripts/Nightly/`: `NightlyFixture` (twins of
   `New-C487OwnedClone`, `Write-C487Trx/Execution/Discovery/Evidence`), `FakeTestHost.NativePass(rows, nodes,
   omitDiscovery)`, `RecordingLauncher`, `FakeGit` (reply table plus per-subcommand gate), `FakeProcessTable`.
6. Keep `ParallelLimiter<ProcessSpawnLimit>` only on tests that start a real child.

## 3. Ordered slices (bottom-up)

"Cases" means driver cases moved in-process. A driver is deleted once all its cases have moved.

- **S01 Foundation (2.5 h).** New core files `NightlyHost.cs`, `NightlyLaunch.cs` (`WatchedLauncher` with
  `Start-NightlyProcess` semantics including the environment swap), the default `NightlyGit` (the
  `Invoke-NightlyGit` flags), `NightlyArgs.cs` (`--kebab value` and switches, comma lists, repeated or unknown flag ->
  usage). `Antiphon.Scripts/Program.cs` gets a name->handler table; existing commands do not change. New
  `scripts/lib/antiphon-cli.ps1` (ASCII, 5.1-safe), `Invoke-AntiphonCli -Command -Arguments [-ResultFile]`: builds
  with the `scripts/antiphon` msbuild line into `bin-launcher` under `bin-launcher/build.lock`, runs from a copy
  (`bin-launcher/run-<hash>/`) because a nightly-run process holds its dll for hours on Windows, exits 9 with one
  stderr line on build or start failure, `ANTIPHON_SCRIPTS_DLL` overrides the dll for tests, passes flags only for
  non-empty values and trims trailing `\` (5.1 drops empty native args; a trailing backslash escapes the quote). Test
  helpers from section 2 rule 5. `Antiphon.Tests.csproj`: `ProjectReference` to Antiphon.Scripts with
  `ReferenceOutputAssembly="false"` plus a StagedHelper as OwnedChild does (:79/:126); land-graph refresh. Deletes
  nothing; no pins.
- **S02 Core goldens in-process (2.5 h).** `scripts/test-nightly-core.ps1` (1379, case NC CoreMatchesFallback, 15
  areas) -> `NightlyCoreGoldenTests` calling the core directly against `scripts/fixtures/nightly/core-golden/*.json`
  (regenerate any missing golden from the ps1 once, before deleting it). Delete the driver and `NightlyCoreTests`;
  re-key the `tests/impacted-classes.json` row `nightly-live-lane`. Must come first: it dot-sources
  nightly-tests-impl, test-phase-evidence, nightly-coverage and c487-harness, and calls functions S07-S10 delete.
- **S03 Leaves (2.5 h).** toolchain-floor: C# twin `ToolchainFloor.cs` called by `nightly-run-impl.ps1:637-644`
  and `nightly-tests-impl.ps1:27-41`; the ps1 stays for the restart chain with a parity test.
  qualification-host-stats: port to `QualificationHostStats.cs` (still not wired) and delete the ps1. test-timings:
  C# twin `TestTimings.cs` called at `nightly-tests-impl.ps1:1904-1925`; the ps1 stays for run-checkpoint with a
  parity test on `antiphon-test-timing-v1`. No pins.
- **S04 Owned process into the core (2 h).** Move `scripts/lib/nightly-owned-process.cs` to
  `Antiphon.Nightly/NativeProcessOwner.cs` (the ps1 already skips Add-Type when the type is loaded);
  `nightly-owned-process.ps1` keeps its two functions until S17/S21. `scripts/test-nightly-native.ps1` -> in-process
  with OwnedChild; delete the driver; re-point the Group4 source-text checks. No pins.
- **S05 nightly-health (3 h; parallel with S03/S04).** Port `lib/nightly-health.ps1` (40 functions) plus the
  London-timezone, JsonMap and atomic-JSON helpers to `NightlyHealth.cs`, `NightlyHealthWindmill.cs`,
  `NightlyHealthNotifications.cs` (production notification sink stays null, CARD-0545 D-9). CLI `nightly-health`;
  `scripts/nightly-health.ps1` becomes a 5.1 launcher. Delete `lib/nightly-health.ps1` and
  `scripts/test-nightly-health.ps1`; nightly-common helpers only health used stay as dead code until S27 (one pin
  move). Cases: HealthFx, C487_G099-G126, C544_*, C545_JobResultFetch, C545_WatchdogFresh, C545_WatchdogStale,
  C545_ReadinessRouting. `RunnerRestartScriptCompatibilityTests`: drop `lib/nightly-health.ps1` from the parse list;
  replace `Nightly_health_closures_resolve_helpers_under_the_call_operator` with an in-process Windmill-adapter test.
  The launcher keeps `#requires -Version 5.1`, ASCII, all 11 parameters with defaults, the unchanged Windmill command
  (`powershell -File ... -RepositoryPath -ReadinessConfigPath`), the exit code, `-PassThru` (read back from
  `--result-file`). Risks: readiness gains a build of `C:\src\Antiphon` every 30 minutes (a broken build exits 9 and
  `last-monitor.json` goes stale); split into S05a (C# and tests, unwired) and S05b (wire and delete) if long.
- **S06 nightly-report, re-pin 1 (3 h).** `nightly-report.ps1` -> `NightlyReport.cs` with the `NightlyReportHttp`
  seam and the one 60 s retry on the host clock; CLI `nightly-report`. The report hop at `nightly-run-impl.ps1:690-722`
  calls `Invoke-AntiphonCli` (CLI unavailable = `reportExit 3`, not a fallback). `ReleaseGateReportDeliveryTests`
  (Antiphon.E2E) runs the report in-process through a new ProjectReference to Antiphon.Nightly (land-graph refresh).
  Delete `nightly-report.ps1` and `test-nightly-report.ps1` (cases C487_G077-G104 have no C# wrapper today; the
  nightly scripts suite runs them as an unattended scriptCensus row). Policy: drop the scriptCensus row
  `test-nightly-report` (`test-execution-policy.json:2020`) and the `scripts/test-nightly-report.ps1` prerequisite
  (`test-selection-policy.json` about :3076); recommended: extend the P-AUTHORITY pattern (:195) with
  `scripts/lib/Antiphon\.(Nightly|Scripts)/.*` so logic leaving `nightly-*.ps1` keeps forcing Full. Reseal
  `catalogHash` -> `selectionCatalog.hash` -> `policyHash`, then re-pin `PinnedInputDigest`. Edit the G056 inventory
  (`test-nightly-tests.ps1:506`) and the `New-C599RunFx` copy list.
- **S07 Journal part A (3 h).** test-phase-evidence L597-1187 -> `TestPhaseJournal.cs` (time zones, writer
  environment, unsafe-directory check, commit barrier and fault, refused and open contexts, journal root, seed
  rows/header/cache, envelope text, generation prefix and metadata, empty and seed generations, projected document, gap
  memory, existing-generation import) plus the head helpers (`Write-TestPhaseTextAtomic`, `Read-TestPhaseBoundedBytes`,
  `Read-TestPhaseJsonl*`) on `TestPhaseFiles`, `TestPhaseStreamReaders`, `EvidenceFileSystem`. ps1 callers call the
  C#. Cases: the journal cases of `ManifestPublicationHarness.ps1` -> `RcManifestJournalTests`;
  `AstraFixesBTests.FlushFailure`. Risk: envelope and generation bytes must stay identical (recovery re-reads old runs).
- **S08 Journal part B (3 h).** L1188-1780: Open/Close executed context, header, commit file, envelope append,
  prefix rescan, gap records, executed-chunk append, publication state, `Complete-TestPhaseExecutedProjection`,
  `Read-TestPhaseExecutedPrefix`, `Invoke-TestPhaseExecutedRecovery`, `Get-TestPhasePublicationQuality`. Delete
  `tests/Antiphon.Tests/TestHelpers/ManifestPublicationHarness.ps1` (1905; filed under W2-rc, but its subject is this
  journal). C#: `RcManifestRecoveryTests`, `RcManifestIntegrationTests`, `AstraFixesBTests.JournalPublish`.
- **S09 Manifests and summary (3 h).** L407-596 and L1781-2420: planned and executed manifests,
  `Complete-TestPhaseLaunch`, `ConvertTo-TestPhaseSummaryRows`, `Write-TestPhaseSummaryFiles`,
  `Invoke-TestPhaseRunSummary`, owner observation, planned index and documents, read warm-up. CLI
  `summarize-test-phases`; delete `scripts/summarize-test-phases.ps1` (only docs call it). `C1514NativeProbe` loses its
  `powershell.exe` re-run path.
- **S10 Heartbeat (3 h).** `qualification-supervisor-heartbeat.ps1` -> `QualificationHeartbeat.cs`. `supervise-rc.ps1`
  (W2-rc) loads `nightly-core.ps1` and calls the C#; nightly-tests-impl calls `WriteActiveChunkIdentity`. Delete the
  heartbeat ps1, its driver and `tests/Antiphon.Tests/Scripts/Fixtures/rc-full-watchdog-witness.ps1`. Land outside an
  rc qualification window.
- **S11 Executor leaves (3 h; parallel with S07-S10).** `NightlyTestsPlan.cs`: the class-cost table, all launch-group
  and plan functions, `Resolve-NightlySuiteChunks`, `Test-NightlySuiteIsNative`, `Test-NightlyE2EPrerequisites`,
  `Resolve-NightlyTestHost`, the discovery-seconds functions, `Get-NightlyNativeExecutionArguments`, the npm launcher,
  `Get-NightlyExecutionPlatform`, `Set-NightlyOutcome`, the `$NightlyLaunch*` constants. Cases: C1397, C1404, C1584,
  C1577, G2865, C1410, C1044_FixtureClassChunks, C1044_UnattendedCases. W2-rc cases whose subject lives here:
  RcRosterRepairTests C1561/C1566/C1567, `ReleaseGatePlatformTests` C1415/LinuxSessionRunnerAdmission,
  `ClassCostMeasurementTests`, the argv rows of C599_Prerequisites and C599_ExecutionArtifacts.
- **S12 Chunk runner (3 h).** `Invoke-NightlyScheduledChunk` (L854-1241), the owned-process, cleanup and active-count
  functions, the observation and measured-phase writers, `Register-NightlyRcDiscoveryOnce` ->
  `NightlyChunkRunner.Run(context, suiteState, chunk) -> NightlyChunkOutcome`. For this slice the ps1 function becomes
  about 20 lines that apply the outcome to its caller's scope. Cases: D13_*, C1419*, Group4*, C1391*;
  `test-nightly-summary-witness.ps1` -> in-process, delete it.
- **S13 Prepare, complete and dispatch (3 h).** `Prepare-`/`Complete-NightlySuiteExecution` and all of
  nightly-dispatch -> `NightlySuiteRunner.cs`, `NightlyDispatch.cs`; delete `nightly-dispatch.ps1`. Move only the
  failed-first and stopped-seed cases that call dispatch functions (FFPreparedArm, FFPreparationArm,
  FFPreparationSequencing, GlobalReplayPriority, FFPreviousRunChain, FFPreviousOrder, MergeChunkOrder).
- **S14 Failed-first and stopped-seed rest, re-pin 2 (3 h).** Move the remaining cases; delete both drivers. Policy:
  the scriptCensus row `test-nightly-failed-first` (:2044) and the P-AUTHORITY paths entry
  (`test-selection-policy.json:201`); edit G056, reseal, re-pin. The `ActualLoopFollowsSchedule` timeout and the
  `nightly-class-costs.json` rows for these classes go stale.
- **S15 NightlyTests main, unwired (3 h).** `Invoke-AntiphonNightlyTests` (L1340-1882), `Write-NightlyRunSummary`,
  `Invoke-NightlyExactRoster`, the toolchain preflight, `Invoke-NightlyProduceDiscovery` (as `NightlyDiscovery.Produce`)
  -> `NightlyTests.Run` plus `NightlyTestsOptions.Parse`. Add `ReleaseSelectionAttempt.cs` (Read, Assert-capability,
  Resolve-members). Cases: C487_G026-G058 (G056 in C#), G144, G1341, C1698 summary, PrerequisiteGate, C1044 discovery,
  `NightlyScriptsTests.Shared_tree_WhatIf`. Two implementations exist for one slice.
- **S16 Driver sweep (3 h).** `test-qualification-timing.ps1` (run by `PosixHarnessToolTests`) -> in-process (its
  release-cut legs already tolerate the S20 refusal); `test-astra-fixes-b.ps1` -> in-process (`AstraStaleCore` and
  `AstraCompleteCorePath` stay as pwsh launches of `nightly-core.ps1` alone: the loader is still production
  PowerShell); convert the inline pwsh scripts in `RcFullTimingContractTests` and `TestPhaseSupervisorTests`; delete
  both drivers.
- **S17 nightly-tests wired (3 h; largest blast radius; land outside an rc window).** CLI `nightly-tests`. Delete
  `scripts/nightly-tests.ps1`, `lib/nightly-tests-impl.ps1`, `scripts/test-nightly-tests.ps1`,
  `lib/test-phase-evidence.ps1`. The tests hop in nightly-run-impl (:646-656) goes through `Invoke-AntiphonCli`, never
  `Invoke-ReleaseSelectionHop`; `live-lane-manifest.ps1:46` drops its dot-source and calls `NightlyDiscovery.Produce`;
  release-selection's `Read-ReleaseSelectionAttempt` delegates to the C#. Delete from release-selection.ps1
  `Invoke-ReleaseSelectionHop`, `ConvertTo-ReleaseSelectionHopParameters` and the switch-drift/listing helpers. W2-rc
  edits: `ReleaseSelectionHopSeedTests` hop cases; `C599_SelectionHopExitCodes` and `C599_ParameterHops` (read
  `nightly-tests.ps1` text); the mode-list checks in `RcDirtyLaunchOrderTests` and `RcIncrementalShadowTests`; the copy
  lists in `New-C599RunFx`, `New-Fx`, `New-C1336Candidate` (`ReleaseGateFollowupTests`); the tests half of C1323;
  `Invoke-FxRoster`.
- **S18 Locks (3 h; parallel after S01).** The nightly-common lock (L534-1079) -> `NightlyRunLock.cs`; release-gate
  L80-183 -> `ReleaseGateNativeLock.cs` (the release-gate functions become thin calls; the nightly-common copies stay
  dead until S27). Cases: C1386_*, C1389_*, C1396_*, C599_SharedLock, C599_Continuation. Add a format test proving
  C#-written lock records are read correctly by the still-PowerShell readers (`Read-NightlyLockRecord`,
  `rc-qualification-status.ps1`, `Read-RcQualificationRecord`).
- **S19 NightlyRun, unwired (3 h).** `Invoke-AntiphonNightlyRunCore`, the EOL repair, hop failure, state write,
  watched command, result record, ownership and reparse helpers -> `NightlyRun.cs`, `NightlyRunOptions`. New hop
  rule: after the sync, hop to the clone's `nightly-run.ps1 -NoSync` with today's argv whenever the running CLI's
  product root differs from CheckoutRoot (replaces the file-hash comparison, L493-499). The tests hop runs
  `dotnet exec <own dll> nightly-tests`; the report runs in-process. Cases: G001-G026, C545_*, C1313, C1854, C1698
  run, PrerequisiteSettle.
- **S20 nightly-run launcher (3 h).** CLI `nightly-run`. Delete `lib/nightly-run-impl.ps1` and
  `scripts/test-nightly-run.ps1`. W2-rc edits: `Invoke-C599Run` cases -> `ReleaseGateRunTests` in-process; the run
  half of C1323; HopFailureRecord; card1336; `ChangeAwareReleaseTests.C1313_ParameterHops` re-scoped to an AST check
  that release-cut's splat keys are a subset of the launcher parameters plus the in-process hop;
  `WorktreeDefaultCallerTests` through the staged dll. The launcher keeps `#requires -Version 5.1`, ASCII, the whole
  parameter block and defaults (including the exact line
  `    [ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full',`,
  `C:\Antiphon\nightly\checkout`, the help text naming origin/master), the compact JSON completion record as the last
  stdout line, the exit code, `-PassThru` (run-core property names; release-cut reads `ExitCode` and `Refusal`) from
  `--result-file`, the refusal of a non-empty `-SeamsPath`, and no dot-sources.
- **S21 Live lane: outcome, run and pin owner (3 h).** Port live-lane-outcome, live-lane-run and the native mode of
  run-live-lane; CLI `live-lane-run` (still always native-unqualified, exit 20). The pin owner moves to C#:
  ProtectedInputs, `Get-ProtectedInputDigest`, `Read-Census` and the sentinels -> `LiveLaneInputDigest.cs`, CLI
  `live-lane-input-digest`; the list is unchanged, so the C# digest must equal the current pin (no re-pin). Delete the
  two libraries, `run-live-lane.ps1`, both drivers, `nightly-owned-process.ps1`. Operator approval needed: the
  `contractFiles` in `tests/live-lane-inputs.json` must be re-keyed to the C# sources, which moves the lane
  fingerprint and resets the paid-commission receipts.
- **S22 Attestation, fingerprint, receipt and status (3 h; blocked on CI-2).** Port the three libraries and
  `live-lane-status.ps1`; the canonical JSON writer moves from PsJson into Antiphon.Nightly; CLI `live-lane-status`.
  Blocked: the fingerprint needs the rc-project-selection graph that CI-2 owns (wait for CI-2, or get the CI owner's
  agreement to reuse the graph walk in `Antiphon.Scripts/RcProjectSelection.cs`).
- **S23-S25 live-lane-manifest (3 x 3 h).** S23 the gate scan (L1043-1667); S24 discovery, roster, export, receipt,
  scratch (L49-1042, L1643-1707); S25 the census (L1708-3046) and deletion of the file and its driver.
  `LiveClaudeInventoryTests` (Antiphon.Agents.Pty.Tests) gets a ProjectReference to Antiphon.Nightly.
  `$LiveLaneProtectedPaths` and `$LiveLaneAllowedSeedPaths` keep their entries (checked against the historical seed
  and pre-S1 commits); the C# paths are added beside them. CI must re-key the `live-lane-manifest` row in
  `rc-input-reader-census.json`.
- **S26 Small nightly entries (1.5 h).** `export-nightly-prior-failures`, `export-nightly-stopped-run-seed`,
  `merge-nightly-seeds` become CLI commands.
- **S27 Residue, re-pin 3 (3 h; last).** Shrink `nightly-common.ps1` and `nightly-core.ps1` to what W2-rc still calls
  (check each function with git grep). Delete `nightly-coverage.ps1` (drop its dot-sources from release-selection:194,
  publish-release:64, promote-release:39; drop it from `ReleaseAuthority.Scripts` - an extra key in an older record is
  ignored, :799-806; re-point the chain assertions in `ReleaseImpactScriptTests` :98-102; move
  `NativeLinuxPolicyMatcherParityTests` in-process). Delete `test-phase-paths.ps1` and inline its resolver into
  `release-cut.ps1:120-126`. Re-pin: drop the three ps1 from ProtectedInputs; any retyping of NightlyCore.cs,
  DiscoveryIndex.cs, DiagnosticLog.cs, DiagScan.cs, NightlyPolicy.cs or ExecutionPolicy.cs happens here only.
- **S28 Typed core APIs (2.5 h).** Remove the PSObject and hashtable overloads of core types whose last PowerShell
  caller was in W2-nightly (DispatchSchedule, LaunchPlanner, StoppedSeedFiles, TestPhaseJournalRead,
  PriorFailureHistory, NightlyWatchdog, NightlyChunkVerdict, `QualificationTiming.Update`); remove the static
  EvidenceFiles and QualificationTiming facades. PowerShellStandard stays: W2-rc PowerShell still passes PSObjects
  into ReleaseGateCredit, ReleaseGateTags, ReleaseAuthority, NightlyDocuments, NightlyPolicy, ExecutionPolicy,
  NightlyTrx, RcRunEvidence.

## 4. CLI subcommands

Launchers translate `-Name value` to `--name value`, pass a switch as a bare flag, join `[string[]]` with commas, and
omit empty values. Exit 9 is reserved for the launcher.

| Command | Flags | Exit |
|---|---|---|
| `nightly-run` | `--suites --no-report --no-sync --checkout-root --log-root --ref --remote-url --state-root --selection-mode --repository --baseline-sha --baseline-receipt-path --qualification-path --timing-root --train-manifest-path --release-intent-path --selection-attempt-path --selection-attempt-digest --selection-path --selection-digest --attempt --prior-failures-path --prior-failures-digest --accept-cross-candidate-seed --seed-source-candidate-id --trigger --profile --expected-sha --candidate-id --release-root --coordination-root --run-id --continue-run-id --continue-parent-pid --continue-parent-started-at --what-if --result-file` | today's codes; record line last |
| `nightly-tests` | `--repo-root --log-root --suites --sha --git-ref --trigger --profile --expected-sha --run-id --selection-mode --release-root --candidate-id --repository`, the same `--baseline-*`/`--selection-*`/`--attempt`/`--prior-failures-*`, `--accept-cross-candidate-seed --seed-source-candidate-id --rc-evidence-root --policy-path --allow-shared-tree --what-if` | `ExitCode`; 3 on refusal |
| `nightly-health` | `--state-root --expected-script-hash --expected-policy-hash --authorized-destination --repository-path --project-id --watchdog-snapshot-url --expected-watchdog-instance-id --readiness-config-path --result-file` | `ExitCode` |
| `nightly-report` | `--summary [--previous-summary] [--dry-run] [--board] [--api]` | as today (3 = API dead) |
| `summarize-test-phases` | `--run-directory [--top 50] [--recover-executed-manifest] [--timing-root]` | 0/1 |
| `live-lane-run` | `--lane --candidate-ref --expected-sha --manifest --coordination-root --results-root` | result code (20 = native-unqualified) |
| `live-lane-status` | `--lane [--candidate-sha] [--repository-root] [--attestation] [--history] [--reuse-policy AlwaysRerun\|Bounded24h] [--context] [--use-record]` | 0/2 |
| `live-lane-input-digest` | `[--root]` | 0 |
| `export-nightly-prior-failures` | `--run-directory --output-path` | 0/2 (2 = refused, the error record on stdout, as the script did) |
| `export-nightly-stopped-run-seed` | `--run-directory --output-path --source-run-id --source-candidate-id --source-sha --timing-root` | 0/1 |
| `merge-nightly-seeds` | forwards the current argv (`-Seed` and `-Proven` repeat) | as today |

## 5. Test classes per slice

DerivedImpactIndexTests runs in every slice after regenerating `tests/derived-impact-index.json`. `*` = new class.

| Slice | Classes |
|---|---|
| S01 | NightlyArgsTests*, AntiphonCliLauncherTests*, PostLandEarlyWarningTests, EvidencePolicyWorkflowTests, NightlyProbeSupervisorTests (plus the land-graph audit) |
| S02 | NightlyCoreGoldenTests*, ImpactedClassesSelectorTests |
| S03 | ToolchainFloorTwinTests*, TestTimingsTwinTests*, ToolchainFloorWarningTests, TestTimingReportTests, RcFullTimingContractTests |
| S04 | NightlyNativeOwnershipTests, RcFullTimingContractTests, TestPhaseSupervisorTests |
| S05 | NightlyHealthTests*, NightlyVerificationContractTests, NightlyScriptsTests, RunnerRestartScriptCompatibilityTests, AntiphonCliLauncherTests |
| S06 | NightlyReportTests*, ReleaseGateReportDeliveryTests, NightlyScriptsTests, NightlyPolicyTests, TestSelectionCatalogTests, TestSelectionTests, LiveLaneRunnerTests, QualificationGraphContractTests, ReleaseGateRunTests |
| S07 | RcManifestJournalTests, AstraFixesBTests, TestPhaseSummaryTests |
| S08 | RcManifestRecoveryTests, RcManifestIntegrationTests, RcManifestJournalTests, AstraFixesBTests |
| S09 | TestPhaseSummaryTests, TestPhaseSupervisorTests, RcFullTimingContractTests |
| S10 | RcFullWatchdogHarnessTests, RcFailedFirstExecutionTests, QualificationTimingTests |
| S11 | C1514LandingSplitBudgetTests, NightlyDiscoveryParserTests, RcRosterRepairTests, ReleaseGatePlatformTests, ClassCostMeasurementTests, ReleaseGateE2EEvidenceTests |
| S12 | NightlyExecutionDiagnosticTests, NightlySummaryWitnessTests, TestPhaseSupervisorTests, RcFullTimingContractTests |
| S13 | RcFailedFirstExecutionTests, RcFailedFirstPlannerTests, RcStoppedRunSeedTests |
| S14 | RcFailedFirstExecutionTests, RcFailedFirstPlannerTests, RcFailureHistoryTests, RcStoppedRunSeedTests, NightlyPolicyTests, TestSelectionCatalogTests, TestSelectionTests, LiveLaneRunnerTests |
| S15 | NightlyTestsRunTests*, NightlyPrerequisiteTests, NightlyDiscoveryParserTests, NightlyScriptsTests |
| S16 | QualificationTimingTests, PosixHarnessToolTests, AstraFixesBTests, RcFullTimingContractTests, TestPhaseSupervisorTests |
| S17 | NightlyTestsRunTests, ReleaseGateRunTests, ReleaseSelectionHopSeedTests, RcDirtyLaunchOrderTests, RcIncrementalShadowTests, NightlyScriptsTests, ReleaseGateFollowupTests, ChangeAwareReleaseTests, ReleaseGatePlatformTests, LiveLaneInventoryTests |
| S18 | NightlyDeadOwnerLockTests, NightlyReclaimRaceTests, ReleaseGateRunTests, NightlyLockFormatTests* |
| S19 | NightlyRunTests*, NightlyScriptsTests, ToolchainFloorWarningTests, NightlyPrerequisiteTests, NightlyVerificationContractTests |
| S20 | NightlyRunTests, ReleaseGateRunTests, ReleaseGatePlatformTests, ReleaseSelectionHopSeedTests, ReleaseGateFollowupTests, ChangeAwareReleaseTests, WorktreeDefaultCallerTests, RcDirtyLaunchOrderTests, RcIncrementalShadowTests, AntiphonCliLauncherTests |
| S21 | LiveLaneOutcomeTests, LiveLaneExecutableTests, LiveLaneRunnerTests, CheckpointCallerBudgetTests |
| S22 | LiveLaneAttestationTests, LiveLaneFingerprintTests, LiveLaneReceiptTests |
| S23-S25 | LiveLaneInventoryTests, LiveClaudeInventoryTests (+ RcInputCensus for S25) |
| S26 | RcFailureHistoryTests, RcStoppedRunSeedTests |
| S27 | NightlyPolicyTests, TestSelectionCatalogTests, TestSelectionTests, LiveLaneRunnerTests, ReleaseAuthorityTests, ReleaseGatePublicationAuthorityTests, ReleaseImpactScriptTests, NativeLinuxPolicyMatcherParityTests, ImpactedClassesSelectorTests, SelectorLineEndingAttributeTests |
| S28 | NightlyCoreGoldenTests, RcFailedFirstExecutionTests, RcStoppedRunSeedTests, QualificationTimingTests, NightlyTestsRunTests |

## 6. Total, blockers and reorders

About 80 h over 28 slices (estimate). Critical path S01 -> S02 -> S07-S10 -> S12-S17 -> S19-S21 -> S25 -> S27, about
50 h. Side lanes: (S03, S04, S05, S06, S11, S18) and (S22-S24, S26). Three re-pins (S06, S14, S27), one at a time:
each moves `tests/test-execution-policy.json`.

1. The nightly slices edit W2-rc test harnesses (section 1.3 cases and copy lists); S06, S11, S17, S18, S20 carry them.
2. Small W2-rc ports are pulled forward: the release-gate native lock (S18) and the release-selection attempt reader
   (S15/S17), with the PowerShell functions delegating; the release-selection hop helpers become dead and are
   deleted. `New-ReleaseSelectionAttempt` stays W2-rc through the worker protocol.
3. nightly-common, nightly-core and nightly-coverage cannot be deleted inside W2-nightly: W2-rc callers dot-source
   them, `ReleaseAuthority.Scripts` pins all three (hashing a missing file throws), and `Get-ProtectedInputDigest`
   returns '' when a pinned input is missing (`test-live-lane-run.ps1:39`). End state is S27's shrink; only
   nightly-coverage is deleted.
4. Stay PowerShell (mark in the migration plan): `toolchain-floor.ps1` (restart chain; C# twin plus parity test);
   `c487-harness.ps1` (about 40 drivers in other families); `test-timings.ps1` until W2-checkpoint;
   `qualification-supervisor.ps1` (all callers are W2-rc qualification scripts: reassign to W2-rc);
   `select-test-rosters.ps1` (a selector-identity file, `ReleaseSelection.cs:134,225`, `release-selection.ps1:49,56`,
   `test-selection.ps1:275,286`, tied to test-selection and CI's canonical JSON: move to W2-rc with release-selection).
5. CI ownership (change-impact plan section 3.3): S22 depends on CI-2's graph (`tools/ChangeImpact` does not exist
   yet); S25 needs the CI owner to re-key the `rc-input-reader-census.json` row; W2-nightly must not port
   `test-selection.ps1` (the canonical JSON comes from moving PsJson).
6. `ManifestPublicationHarness.ps1` moves from W2-rc to S08 (its subject is the test-phase journal).
7. Operator approval: `tests/live-lane-inputs.json` contractFiles (S21) resets the paid lane receipts; review
   approves all three re-pins.
8. Windows lock risk: a nightly-run process holds its dll for hours, so the S01 launcher runs from a copy of the build;
   `scripts/antiphon` today runs in place.
9. Unwrapped coverage: the default-loop cases of test-nightly-tests, -run and -health (C487_G001-G126) only run by
   hand, and test-nightly-report only as a census row. Port them as tests; do not drop them.
10. Rc windows: S10 (supervise-rc) and S17/S20 land outside a live rc qualification window. Every slice touches
    `scripts/lib/**`, which forces a Full selection.
