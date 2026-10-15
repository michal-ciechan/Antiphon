# CARD-1287 Windows Unit roster qualification

Status: final qualification pending after two failed candidate runs; two executions of the revised roster are required. Compiled source: **cdb18eef5013bfa45336dfd304a022b489686bbc**. No product or test code changed.

The roster contains **366 of 409 compiled Unit classes**, with **43 whole-class exclusions** below. One fully qualified class appears per line in the adjacent `-roster.txt`; the adjacent `-filter.txt` supplies the literal TUnit OR expression. Each operand has the trailing wildcard required by the pinned discovery hint extractor. Prefix collisions are excluded rather than admitting Integration classes. Compiled metadata, actual TRX class/method identities and result counts determine coverage; listing tests is not execution evidence.

The historical Windows all-passing-class candidate came from the 5,591-case run at `a30b7f4297400f010ce70188ac99ebe55be51931`, not this source. Current compiled metadata has three Unit classes absent from that historical TRX: `CodexAdapterRawResponseTests`, `D2AdvisoryDocumentationTests` and `HostCleanupCheckpointCustodyTests`; the first two are retained; HostCleanupCheckpointCustodyTests is excluded after both candidate runs failed. `MatrixDefaultComplexityCreateTests` is no longer a compiled Unit class. Repaired groups A/V/X are not treated as current failures: `OrchestratorInstructionsGuidanceTests` (CARD-1286), `DelegationHarnessCensusTests` and `TestLaneCategoryGuardTests` (CARD-1298) are retained. `CheckpointTrackedBaselineTests` remains excluded for its explicit Windows platform skips.

## Reproduction

Run from the native Windows repository root, with fresh results directories. The test runs reuse the original binary built at the pinned SHA; the later roster/evidence commits change only these three artifact paths. Rebuilding at an artifact commit creates a new build identity and requires its own qualification. To replay exactly at the compiled SHA, supply the retained filter from `C:\Antiphon\worktrees\card-task-b7430380\.antiphon\c1287-b7430380\filter.txt` because these artifact files do not exist at that SHA.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1287-windows-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1287-b7430380/ --nologo
$rosterFilter = (Get-Content -Raw docs/investigations/2026-10-15-card-1287-windows-unit-filter.txt).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1287-windows-run1 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1287-b7430380/ -- --treenode-filter $rosterFilter --output Detailed --report-trx --report-trx-filename run.trx --results-directory "$PWD/.antiphon/c1287-b7430380/run1"
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1287-windows-run2 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1287-b7430380/ -- --treenode-filter $rosterFilter --output Detailed --report-trx --report-trx-filename run.trx --results-directory "$PWD/.antiphon/c1287-b7430380/run2"
```

The retained driver uses these same literal argument arrays, waits in the foreground and captures console/timing separately. No `-NoSlot`, timeout extension, assertion relaxation or fabricated checkpoint build-source stamp is used. TUNIT_MAX_PARALLEL_TESTS is unset. One owned Antiphon.Tests driver runs at a time; no PTY assembly is co-scheduled by this task. Runtime: Windows 10.0.19045, SDK 10.0.300, .NET 9.0.21, TUnit 1.44.

The isolated build passed with **0 errors, 1,058 warnings**, MSBuild elapsed **125.74s**, outer wall **265.786s**, including the host memory-floor queue. Its slot was granted and held for 127s. Assembly informational version: `1.0.0+cdb18eef5013bfa45336dfd304a022b489686bbc`.

## Exclusions

Every exclusion is at class granularity, so passing portable methods within these classes are also outside this gate. Causes are historical Windows observations or explicit platform admission, not new reproductions at the pinned SHA. Group letters refer to the [published full Windows run](2026-10-15-card-1287-windows-unit-lane-5ca376de.md) and its retained 0afb0cee triage. CARD-1287 tracks the roster qualification. Native Linux coverage remains independently qualified by its own roster; Windows skips and passing early-return branches do not prove native Linux behavior.

| Class | Cause / triage group or card |
|---|---|
| `Antiphon.Tests.Agents.RunnerGrokAdapterReadyTests` | CARD-1287 selector boundary: trailing-wildcard operand also admits Antiphon.Tests.Agents.RunnerGrokAdapterReadyTestsPty. |
| `Antiphon.Tests.Application.OrchestratorInstructionsRendererTests` | B: CRLF retained by LF splitting exceeds the matrix byte cap (CARD-0097/CARD-1258). |
| `Antiphon.Tests.Application.OutboundConversionManifestTests` | CARD-1287 selector boundary: trailing-wildcard operand also admits Antiphon.Tests.Application.OutboundConversionManifestTestsPump. |
| `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests` | CARD-1287 Windows platform exclusion: Skipped: Linux FIFO and process-group qualification |
| `Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests` | C: bounded receipt read/admission failures under aggregate load; passes alone; precise internal operation unresolved (CARD-1199). |
| `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` | D/E: read-only Git object teardown and core.autocrlf mismatch in hardened admission (CARD-0804/CARD-1241). CARD-1287 Windows platform exclusion: Skipped: the statx stamp (inode and ctime) is read on Linux only |
| `Antiphon.Tests.Checkpoints.CheckpointFixtureProcessTests` | CARD-1287 Windows platform exclusion: Skipped: FIFO config proof runs on Linux; Skipped: process-group custody proof runs on Linux |
| `Antiphon.Tests.Checkpoints.CheckpointImportTests` | C: bounded reader cancellation and output/admission refusals under aggregate load (CARD-1199). |
| `Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests` | C: bounded receipt reads and launch acknowledgement under aggregate load (CARD-1199). |
| `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified. |
| `Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests` | CARD-1287 Windows platform exclusion: Skipped: Linux symlink proof; Windows junction remains R2 |
| `Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests` | C/G: bounded reads and four-second cold child deadline; whitespace variant also failed isolation (CARD-1199). CARD-1287 Windows platform exclusion: Skipped: Linux FIFO qualification requires Linux |
| `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure. |
| `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests` | CARD-1287 Windows platform exclusion: Skipped: Linux source-shape qualification |
| `Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests` | H: five-second scheduler owner-loss wait failed aggregate and passed alone (CARD-1199). |
| `Antiphon.Tests.Checkpoints.CheckpointTempRootSweepTests` | I: five-second sweep budget versus exact removal counts; passed alone. |
| `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified. |
| `Antiphon.Tests.Checkpoints.DetachedLauncherTests` | CARD-1287 Windows platform exclusion: Skipped: DetachedLauncher setsid proof runs on Linux; Review runs the Windows half. |
| `Antiphon.Tests.Checkpoints.OutputCleanupTests` | D/F/W: Git object teardown, /bin/false Windows assumption, mixed-slash expectation; original timeout retained (CARD-0804/CARD-1199/CARD-1264). CARD-1287 Windows platform exclusion: Skipped: Windows junction variant is unverified; Skipped: hanging-git proof runs on Linux; Skipped: seal-time custody needs statx; Skipped: seal-time custody needs statx; off Linux nothing is adopted |
| `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests` | J: full-run coverage admission failure; isolated pass; internal diagnostic unresolved (CARD-1046/CARD-1001/CARD-1199). |
| `Antiphon.Tests.Checkpoints.PlanCoverageCommandTests` | CARD-1287 Windows platform exclusion: Skipped: coverage outside-project FIFO read sentinel requires Linux mkfifo |
| `Antiphon.Tests.Checkpoints.PlanCoverageHandleTests` | J: full-run coverage intake failure; isolated pass; internal diagnostic unresolved (CARD-1199). CARD-1287 Windows platform exclusion: Skipped: FIFO sentinel requires Linux mkfifo; Skipped: hardlink sentinel requires Linux link(2) |
| `Antiphon.Tests.Checkpoints.RunStateStoreTests` | K: concurrent overwrite move access denied and unjoined reader after exception (CARD-1199). |
| `Antiphon.Tests.Checkpoints.SafeDeleteProcessBoundaryTests` | CARD-1287 Windows platform exclusion: Skipped: FIFO-held monitor proof runs on Linux; Skipped: process-group custody proof runs on Linux |
| `Antiphon.Tests.Infrastructure.CodexRootExecContractTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1210: Codex heal contracts require native Linux with unshare user+mount namespaces and a bind-mountable /state |
| `Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests` | L: Windows/WSL worktree identity translation fails before intended assertions (CARD-0849/CARD-0913). |
| `Antiphon.Tests.Infrastructure.GitMinimumVersionGuardTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1260: the Git floor guard runs under /bin/sh with GNU sort -V, as in the Debian images; Windows skips explicitly. |
| `Antiphon.Tests.Infrastructure.GithubCredentialHelperTests` | CARD-1287 Windows platform exclusion: Skipped: The POSIX credential helper runs on Linux. |
| `Antiphon.Tests.Infrastructure.GrokSha256PinTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly. |
| `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1210: jq image probes require native Linux (POSIX modes, symlinks and executable identity); Windows skips explicitly. |
| `Antiphon.Tests.Scripts.C1008PlatformContractTests` | M: nested audit requires legitimate checkpoint build-source stamp, absent from plain build (CARD-1050). |
| `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1210: C1025/C1058 host jq contracts require native Linux (bash, flock, /proc/self/fd, GNU coreutils); Windows skips explicitly. |
| `Antiphon.Tests.Scripts.RefreshGithubTokenRemoteCommandTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1245: the fake bw/ssh are POSIX sh executables; Windows skips explicitly. |
| `Antiphon.Tests.Scripts.RemoteScriptContractTests` | N/O/P/Q/R: extracted dependency, real chown, acquisition ledger, cleanup witness and dynamic scope defects (CARD-1066/CARD-1191/CARD-1192/CARD-1208). CARD-1287 Windows platform exclusion: Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` | S: raw Windows path passed to Bash before host assertions (CARD-0994/CARD-1008). CARD-1287 Windows platform exclusion: Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` | S: raw Windows path passed to Bash before wrapper assertions (CARD-0994/CARD-1008). |
| `Antiphon.Tests.Scripts.RollingProductionMountTests` | S: raw Windows path passed to Bash before mount assertions (CARD-0994/CARD-1008). CARD-1287 Windows platform exclusion: Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleCensusChurnTests` | CARD-1287 Windows platform exclusion: Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests` | T: legacy jq harness exceeds ten-minute method budget; child wait ignores cancellation (CARD-1210). CARD-1287 Windows platform exclusion: Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. |
| `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests` | M: nested audit requires legitimate checkpoint build-source stamp, absent from plain build (CARD-1210). |
| `Antiphon.Tests.TestHelpers.ControlledLandingGitTests` | U: registration-only deletion sharing error 32; owner unresolved (CARD-0665/CARD-0688). |

| `Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests` | CARD-1287 qualification finding Q1: csproj_has_no_project_references fails in both candidate runs because Antiphon.Checkpoints.csproj references Antiphon.HostCleanup; that reference was added by 367c8ee4cdcc877aa71f1a46d30c410d628edc3b. One failed method among four cases. No parent execution or repair performed. |
| `Antiphon.Tests.Infrastructure.HostCleanupCheckpointCustodyTests` | CARD-1287 qualification finding Q2 / CARD-0987: eleven of twelve methods fail in both candidate runs: ten output-owner-missing refusals and one missing /c987/.../bin-owned dictionary key. The virtual Linux-path fixture and Windows Path.GetFullPath canonicalization disagree (source-based inference). No native deletion regression is established and no repair performed. |

## Execution evidence

The **368-class candidate failed twice** and is not a gate certificate. Both runs matched all 368 class identities and all 2,921 compiled methods; missing/extra classes and methods were zero. Both used runtime HEAD `ac9f7bff08bc740dfdca58431aa59732da48be6f`, clean tracked status and the original unchanged compiled assembly.

| Candidate | Cases | Passed | Failed | Skipped | Outer wall | TRX wall | Driver exit |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| run1 | 4595 | 4583 | 12 | 0 | 281.979s | 278.352s | 2 |
| run2 | 4595 | 4583 | 12 | 0 | 276.770s | 272.341s | 2 |

The same twelve methods fail in both runs: `CheckpointToolProjectContractTests.csproj_has_no_project_references`, plus every `HostCleanupCheckpointCustodyTests` method except `Links_and_special_entries_are_not_adopted`. Ten custody failures are `IOException: refused: output-owner-missing`; `Custody_window_checks_owner_mode_and_times` instead throws `KeyNotFoundException` for its virtual `/c987/.../bin-owned` root. Exact method rows, assertions and stack traces are retained in each `run.trx` and `cases.json`. These findings are present at the pinned task base, not introduced by roster/evidence edits. The project reference and virtual fixture came from the recent CARD-0987 slice; no historical parent test was run, and no product regression beyond the contract/fixture failures is claimed.

Candidate class/method/outcome count multisets are identical; **no class failed in only one candidate run**. One passing `DispatchHoldLedgerTests.C672_ClassOf_maps_every_hold_sentence` display argument embeds a fresh task GUID, so literal display strings differ while the selected method and case counts match. Its source creates a new GUID; this is not an outcome flake. Both candidates had granted slots with zero wait. The two observed failing classes are wholly excluded from the final roster (sixteen total cases, including four passing cases), leaving an expected **4,579 executed cases across 366 classes and 2,905 compiled methods**. The final executions below are fresh whole-roster runs; their timings and passes are not obtained by subtracting failed cases from these candidates.

Final runs pending. Raw evidence is retained under `C:\Antiphon\worktrees\card-task-b7430380\.antiphon\c1287-b7430380`, including the original build log, source index, compiled class/method metadata, exact filter and argv, timing, fresh TRX and class/method audit. Generated payloads remain ignored.
