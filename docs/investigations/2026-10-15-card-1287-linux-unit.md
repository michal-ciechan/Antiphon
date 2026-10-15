# CARD-1287 Linux Unit roster qualification

Status: candidate; qualification pending. Built clean source f6310a452a911b45b0a0946feb9ac49dcc71bbfd once through the host build-slot gate (3m13.96s, 0 errors). No product or test changes. The two requested aggregate runs will reuse that binary.

The roster contains 374 compiled Unit classes, one fully qualified class per line. The filter is the adjacent `-filter.txt` file; class OR operands use the documented trailing wildcard and actual TRX identities must equal the roster.

The prior Linux 21-minute cutoff is an aggregate checkpoint timeout with no terminal TRX, not proof of a hang. `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain` runs four serial child harnesses; `C1008Process` gives each a separate twelve-minute budget while the class method timeout is ten minutes. The child wait does not receive TUnit cancellation. Windows triage group T observed this timeout; `RemoteScriptContractTests` also retains synchronous/unbounded child waits. Both classes are excluded rather than changing code/timeouts. The previous Linux log used Normal output and cannot identify the active class at cutoff.

## Exact commands

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1287-linux-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1287-67a7e42a/ --property:UseAppHost=false --nologo
$filter = (Get-Content docs/investigations/2026-10-15-card-1287-linux-unit-filter.txt -Raw).Trim()
# Use a fresh --results-directory for each run.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1287-linux-run1 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1287-67a7e42a/ --property:UseAppHost=false -- --treenode-filter $filter --output Detailed --report-trx --report-trx-filename run.trx --results-directory .antiphon/c1287-67a7e42a/run1
# Repeat the same command with label/results-directory run2. No retries.
```

## Exclusions

The exclusions are conservative at class granularity, including cross-platform methods sharing an excluded class. Windows-only failure attributions are retained as Windows observations, not asserted to reproduce on Linux. Every entry is tracked under CARD-1287 or its referenced triage group.

| Class | Cause and evidence |
|---|---|
| `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/AgentTui/AgentTuiSecretProtectorTests.cs |
| `Antiphon.Tests.Agents.AgentRegistrySettingsTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Agents/AgentRegistrySettingsTests.cs |
| `Antiphon.Tests.Application.AgentExecutableResolverTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/AgentExecutableResolverTests.cs |
| `Antiphon.Tests.Application.AgentPinPathTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/AgentPinPathTests.cs |
| `Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/ClaudeRemoteControlLaunchArgsTests.cs |
| `Antiphon.Tests.Application.DelegationReportFormatterTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/DelegationUnitTests.cs |
| `Antiphon.Tests.Application.DirectoryBrowseServiceTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/DirectoryBrowseServiceTests.cs |
| `Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/GrokRulesTransportCompatibilityTests.cs |
| `Antiphon.Tests.Application.PtyDeliveryCeilingsTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/PtyDeliveryCeilingsTests.cs |
| `Antiphon.Tests.Application.SessionDeliveryProfileTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/SessionDeliveryProfileTests.cs |
| `Antiphon.Tests.Checkpoints.CheckpointImportTests` | CARD-1287 Linux inherited checkpoint environment/rerun failure (CARD-1253 base proof). |
| `Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests` | CARD-1287 Linux inherited executor acknowledgement/cleanup failures (CARD-1253 base proof). |
| `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests` | CARD-1287 group D: Windows Git fixture teardown failure; conservative whole-class exclusion. |
| `Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests` | CARD-1287 Linux inherited dead-identity cleanup refusal (CARD-1253 base proof). |
| `Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests` | CARD-1287 groups C/E/G: receipt contention, C1241 Git normalization, cold child-start four-second budget. |
| `Antiphon.Tests.Checkpoints.CheckpointRunOwnershipTests` | CARD-1287 Linux inherited unrecorded-existing refusal (CARD-1253 base proof). |
| `Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests` | CARD-1287 groups C/E: receipt contention and C1241 Git normalization. |
| `Antiphon.Tests.Checkpoints.CheckpointTempRootSweepTests` | CARD-1287 group I: sweep budget/removal-count contention flake. |
| `Antiphon.Tests.Checkpoints.EvidenceFolderTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Checkpoints/EvidenceFolderTests.cs |
| `Antiphon.Tests.Checkpoints.OutputCleanupTests` | CARD-1287 groups D/F/U: Git teardown, failing-Git fixture portability, native registration-only sharing failure. |
| `Antiphon.Tests.Checkpoints.RerunPolicyTests` | CARD-1287 Linux inherited rerun exit/count failures (CARD-1253 base proof). |
| `Antiphon.Tests.Checkpoints.RunSchedulerTests` | CARD-1287 group H, owner-loss/scheduling timing flake; Linux D-2 scheduler failure passes at base in isolation. |
| `Antiphon.Tests.Checkpoints.RunStateStoreTests` | CARD-1287 group K: concurrent state replacement access denial observed on Windows; conservatively excluded cross-host. |
| `Antiphon.Tests.Checkpoints.ShadowCopyTests` | CARD-1287 Linux inherited output-create-failed:2 (CARD-1253 base proof). |
| `Antiphon.Tests.Checkpoints.TimeoutTests` | CARD-1287 Linux inherited slot-refused result; also Windows-only process rows (CARD-1253 base proof). Source: tests/Antiphon.Tests/Checkpoints/TimeoutTests.cs |
| `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Infrastructure/LandingRemovalPolicyControlTests.cs |
| `Antiphon.Tests.Scripts.C1008PlatformContractTests` | CARD-1287 group M: requires legitimate checkpoint-build-source.json provenance absent from plain build; also Windows audit rows. Source: tests/Antiphon.Tests/Scripts/C1008PlatformContractTests.cs |
| `Antiphon.Tests.Scripts.RemoteScriptContractTests` | CARD-1287 groups N/O/P/Q/R/S/T: extracted harness, ledger, dynamic scope, native path failures; legacy jq runs and unbounded child waits. Source: tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests` | CARD-1287 group T: four legacy jq harnesses exceed ten-minute method budget; independent child wait ignores cancellation. |
| `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests` | CARD-1287 group M: nested audit requires checkpoint-build-source.json provenance; Windows-specific audit rows. Source: tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs |
| `Antiphon.Tests.TestHelpers.DelegationHarnessCensusTests` | CARD-1287 group V: DelegateBranchDeliveryTests missing shared graph registration. |
| `Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests` | CARD-1287 group X: embedded raw C# FreshProbe mistaken for compiled untagged test. |

| `Antiphon.Tests.Agents.RunnerGrokAdapterReadyTests` | CARD-1287 selector boundary: required TUnit trailing-wildcard class operand also selects an Integration suffix class; excluded to keep this roster Unit-only. |
| `Antiphon.Tests.Application.OutboundConversionManifestTests` | CARD-1287 selector boundary: required TUnit trailing-wildcard class operand also selects an Integration suffix class; excluded to keep this roster Unit-only. |

## Completed candidate (failed; not qualified)

The committed 374-class candidate at runtime HEAD `25d38896715e1927e85c7f09661822228a6561f8` completed once from the original `f6310a452a911b45b0a0946feb9ac49dcc71bbfd` binary. Only the three roster/documentation paths differ from that build SHA. Compiled source, tests and dependencies were not edited or rebuilt.

| Run | Executed | Passed | Failed | Skipped | Outer wall | TRX wall | Classes |
|---|---:|---:|---:|---:|---:|---:|---:|
| candidate1 | 4526 | 4525 | 1 | 0 | 2844.911s | 2839.116s | 374 |

Fresh TRX class identities equal all 374 roster entries. Every compiled test method in those classes has a result; missing/extra classes and missing methods are all zero. Native driver exit was 2. The host slot was granted with zero wait.

The sole failure is `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes(project, ordinary)`: the four-second supervisor expired at 4,019ms, killed PowerShell with SIGKILL, and returned exit 1 even though stdout already contained `PASS project held=False`. TUnit reports 4.384s. This is an observed harness startup/exit deadline failure, not a confirmed intermittent flake; no isolated rerun or timeout change was made. The other nine reader arguments passed. The class will be quarantined under CARD-1287 and the amended roster will be measured twice. This failed run is retained and contributes no passing gate certificate.

The old Linux CP-3 resolved manifest records `estimatedMinutes: 7`, `timeoutMinutes: 21`; its terminal report records timeout after 1261.672s with no TRX. This reduced candidate instead completed after 47m24.911s while making progress through serial real-build and native shell/PowerShell matrices. Underbudgeting is a sufficient explanation for an aggregate cutoff; the old Normal console cannot identify its active class or prove/exclude a separate hang. No full-Unit duration or historical critical path is claimed.

Main assembly SHA-256: `cf0b0487b4e9527cc31cd471b652f9db91887de4426b348708dc33cf06a704a9`. Its existing assembly informational version is `1.0.0+f6310a452a911b45b0a0946feb9ac49dcc71bbfd`. Candidate TRX SHA-256: `8b53e52791169c741bbd8887ebf427ccdd051ab75afa12745c70ea7f5164a991`. No checkpoint source stamp was fabricated.

Raw evidence is retained at `/work/evidence/task-67a7e42a/raw/`, including `candidate1/run.trx`, full console, exact argv/filter, timing, method/class audit, original build log/index and compiled metadata. The two final aggregate executions remain pending.
