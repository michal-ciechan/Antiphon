# CARD-1287 Linux Unit roster qualification

Status: **qualified by two clean executions of the frozen Linux roster**. Source built once: **f6310a452a911b45b0a0946feb9ac49dcc71bbfd**. No product or test code changed.

The roster contains **373 of 408 compiled Unit classes**, one fully qualified class per line; **35 classes are excluded** below. Expected result census is **4516 executed, 0 failed, 0 skipped per final run**, derived from the completed candidate after removing the ten reader variants. The two actual executions below match that census. The failed candidate was not filtered or subtracted to manufacture a passing run or a wall time.

The adjacent `-filter.txt` file contains the exact TUnit OR filter. Every operand has the documented trailing wildcard; two prefix collisions with Integration classes are explicitly excluded. Fresh TRX identities must equal the roster, every compiled method must have a result, and executed counts must match in both runs.

## Reproduction

Run from the repository root on native Linux with the pinned source and fresh result directories. These shell commands pass the filter literally, matching the recorded native argv. The retained Node driver also uses literal argument arrays. A direct PowerShell native invocation of the large wildcard filter stalled before creating a child and was stopped; its empty `run1/console.log` is retained and counts as zero test executions.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1287-linux-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1287-67a7e42a/ --property:UseAppHost=false --nologo
roster_filter=$(cat docs/investigations/2026-10-15-card-1287-linux-unit-filter.txt)
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1287-linux-final1 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1287-67a7e42a/ --property:UseAppHost=false -- --treenode-filter "$roster_filter" --output Detailed --report-trx --report-trx-filename run.trx --results-directory "$PWD/.antiphon/c1287-67a7e42a/final1"
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1287-linux-final2 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1287-67a7e42a/ --property:UseAppHost=false -- --treenode-filter "$roster_filter" --output Detailed --report-trx --report-trx-filename run.trx --results-directory "$PWD/.antiphon/c1287-67a7e42a/final2"
```

For an exact-SHA replay in a clean `f6310a452a911b45b0a0946feb9ac49dcc71bbfd` checkout, read `roster_filter` from the retained `/work/evidence/task-67a7e42a/raw/final1/filter.txt` instead of the repository-relative path. The original build preceded creation of these three documentation/roster files, so they are absent at that Git SHA. The filter values and native argv are identical either way. Rebuilding the later artifact branch produces its own Git source identity; carrying this frozen list to a different source SHA requires a new build/census/qualification rather than inheriting these results.

The build passed with 0 errors (1056 warnings), MSBuild wall 193.96s, a granted slot with zero wait, and 194s lease hold. All tests reuse that original output; documentation commits do not change the compiled source. Runtime: Debian 12, .NET 9.0.20, SDK 10.0.401, TUnit 1.44 / Microsoft Testing Platform 2.2.2. TUNIT_MAX_PARALLEL_TESTS is unset; process-spawning cases share the assembly's one-wide limiter. This is a shared-host sample, with one owned assembly driver at a time.

## Exclusions

Class granularity also removes portable methods sharing an excluded class. Windows-only bodies include explicit skips and early returns; their reported passing returns do not prove native execution. Historical Windows failures remain Windows observations, with conservative cross-host exclusions where stated. All other current Linux native contract classes remain in the roster. Causes are tracked by CARD-1287, its Windows triage group letters, or the cited prior CARD-1253 base proof. The routing-owner and compiled-metadata guards (`OrchestratorInstructionsGuidanceTests` and `TestClassificationGuardTests`) were retained and passed in the candidate and both final runs.

| Class | Cause and evidence |
|---|---|
| `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/AgentTui/AgentTuiSecretProtectorTests.cs |
| `Antiphon.Tests.Agents.AgentRegistrySettingsTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Agents/AgentRegistrySettingsTests.cs |
| `Antiphon.Tests.Agents.RunnerGrokAdapterReadyTests` | CARD-1287 selector boundary: required TUnit trailing-wildcard class operand also selects an Integration suffix class; excluded to keep this roster Unit-only. |
| `Antiphon.Tests.Application.AgentExecutableResolverTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/AgentExecutableResolverTests.cs |
| `Antiphon.Tests.Application.AgentPinPathTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/AgentPinPathTests.cs |
| `Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/ClaudeRemoteControlLaunchArgsTests.cs |
| `Antiphon.Tests.Application.DelegationReportFormatterTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/DelegationUnitTests.cs |
| `Antiphon.Tests.Application.DirectoryBrowseServiceTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/DirectoryBrowseServiceTests.cs |
| `Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/GrokRulesTransportCompatibilityTests.cs |
| `Antiphon.Tests.Application.OutboundConversionManifestTests` | CARD-1287 selector boundary: required TUnit trailing-wildcard class operand also selects an Integration suffix class; excluded to keep this roster Unit-only. |
| `Antiphon.Tests.Application.PtyDeliveryCeilingsTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/PtyDeliveryCeilingsTests.cs |
| `Antiphon.Tests.Application.SessionDeliveryProfileTests` | CARD-1287 Linux platform exclusion: explicit Windows-only method/fixture admission. Source: tests/Antiphon.Tests/Application/SessionDeliveryProfileTests.cs |
| `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests` | CARD-1287: Linux candidate observed a four-second supervisor expiration after the ordinary project probe printed PASS (elapsed 4019ms, expired=true, SIGKILL). Other nine variants passed; intermittent status unconfirmed. Evidence: candidate1/run.trx. |
| `Antiphon.Tests.Checkpoints.CheckpointImportTests` | CARD-1287 Linux inherited checkpoint environment/rerun failure (CARD-1253 base proof). |
| `Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests` | CARD-1287 Linux inherited executor acknowledgement/cleanup failures (CARD-1253 base proof). |
| `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests` | CARD-1287 group D: conservative exclusion for exposure to the triaged Windows Git-fixture teardown path; no current Linux failure is asserted. |
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

The nested C1008/C1210 provenance audits require a legitimate checkpoint-built source stamp; they are outside this plain-build roster. No such stamp was fabricated. Whole-Unit and Windows qualification remain outside this frozen Linux subset.

## Completed candidate (failed; not qualified)

The committed 374-class candidate at runtime HEAD `25d38896715e1927e85c7f09661822228a6561f8` completed once from the original `f6310a452a911b45b0a0946feb9ac49dcc71bbfd` binary. Only the three roster/documentation paths differ from that build SHA. Compiled source, tests and dependencies were not edited or rebuilt.

| Run | Executed | Passed | Failed | Skipped | Outer wall | TRX wall | Classes |
|---|---:|---:|---:|---:|---:|---:|---:|
| candidate1 | 4526 | 4525 | 1 | 0 | 2844.911s | 2839.116s | 374 |

Fresh TRX class identities equal all 374 roster entries. Every compiled test method in those classes has a result; missing/extra classes and missing methods are all zero. Native driver exit was 2. The host slot was granted with zero wait.

The sole failure is `Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes(project, ordinary)`: the four-second supervisor expired at 4,019ms, killed PowerShell with SIGKILL, and returned exit 1 even though stdout already contained `PASS project held=False`. TUnit reports 4.384s. This is an observed harness startup/exit deadline failure, not a confirmed intermittent flake; no isolated rerun or timeout change was made. The other nine reader arguments passed. The class was quarantined under CARD-1287 before the two final executions. This failed run is retained and contributes no passing gate certificate.

The old Linux CP-3 resolved manifest records `estimatedMinutes: 7`, `timeoutMinutes: 21`; its terminal report records timeout after 1261.672s with no TRX. This reduced candidate instead completed after 47m24.911s while making progress through serial real-build and native shell/PowerShell matrices. Underbudgeting is a sufficient explanation for an aggregate cutoff; the old Normal console cannot identify its active class or prove/exclude a separate hang. No full-Unit duration or historical critical path is claimed.

Main assembly SHA-256: `cf0b0487b4e9527cc31cd471b652f9db91887de4426b348708dc33cf06a704a9`. Its existing assembly informational version is `1.0.0+f6310a452a911b45b0a0946feb9ac49dcc71bbfd`. Candidate TRX SHA-256: `8b53e52791169c741bbd8887ebf427ccdd051ab75afa12745c70ea7f5164a991`. No checkpoint source stamp was fabricated.

Raw evidence is retained at `/work/evidence/task-67a7e42a/raw/`, including `candidate1/run.trx`, full console, exact argv/filter, timing, method/class audit, original build log/index and compiled metadata. The completed final aggregate executions are reported below.

## Final qualification at the pinned source

| Run | Executed | Passed | Failed | Skipped | Classes | Outer wall | TRX start/finish wall | Slot wait | Exit |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| final1 | 4516 | 4516 | 0 | 0 | 373 | 2534.527s | 2528.622s | 0s | 0 |
| final2 | 4516 | 4516 | 0 | 0 | 373 | 2433.533s | 2427.491s | 0s | 0 |

The outer walls are **42m14.527s** and **40m33.533s**. The observed spread is **100.994s (1m40.994s)**; the second run was **3.985% faster relative to the first**. This is a two-observation range on this shared host, not a percentile or worst-case guarantee. Both runs exceed the former 21-minute row limit. These are aggregate execution walls, not sums of passing-class durations. Build time and any future slot queue remain separate budget components.

Both native drivers exited 0 with granted slots and zero wait. Fresh TRX identities equal all 373 roster classes; all **2866 compiled test methods** have results, and the class/method case-count multisets match between runs. Missing/extra classes, missing methods and unexpected outcomes are zero. Neither final run has a failure or skip, so no differing failure/flaky class was found in the final pair. The reader failure remains an observed failed candidate case with intermittent status unconfirmed.

Both final runs used runtime checkout HEAD `b4a23d738673b994d31306adfa44abcd9c7dfc6a`, with zero tracked dirt before/after and unchanged main-assembly bytes. The only Git delta from the actual compiled source `f6310a452a911b45b0a0946feb9ac49dcc71bbfd` is the three roster/documentation paths in this change; no product/test input was changed or rebuilt after the original clean build. Both runs have identical binary, driver, roster and filter hashes. The later result-note commit is reporting only and is not relabeled as the tested source.

| Binding | SHA-256 |
|---|---|
| Main assembly | `cf0b0487b4e9527cc31cd471b652f9db91887de4426b348708dc33cf06a704a9` |
| Roster bytes | `716018884f1a68c923958dde2c65fc7150ecb78d123972fd2d671bfadbe28168` |
| Filter bytes | `9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7` |
| Final driver | `5044ee0a8d0a952d320ae40e73242130ad2596889cf7fe850d2ee560c974e0e8` |
| final1 TRX | `8ba9fb28bbc832502d10b2033849e5f89f6c3da793f1ab0bb4eb938d3d419d75` |
| final2 TRX | `8af7763a3494b29610960aa5ae5a64221b1aaf4563edecb85a87d31f1f4a1b85` |

Complete TRX files:

- `/work/evidence/task-67a7e42a/raw/final1/run.trx`
- `/work/evidence/task-67a7e42a/raw/final2/run.trx`
- Failed historical candidate: `/work/evidence/task-67a7e42a/raw/candidate1/run.trx`

The same evidence root retains exact argv, start/terminal timing, per-case outcomes, method/class audits, compiled metadata, source index, informational version, host identity, original build log and qualification comparison. The `interim/` sibling is explicitly a partial pre-terminal snapshot and is not qualification evidence.

## Limits and cleanup

This qualifies the explicit 373-class Linux subset at the pinned compiled SHA. The full Unit lane, Windows coverage and the excluded checkpoint-built provenance audits remain unqualified here. The [published Windows triage](2026-10-15-card-1287-windows-unit-lane-5ca376de.md) and source-path inspection supplied historical exclusion background; the raw Windows bundle was not reachable from this runner. Conservative class-to-group exposure mappings are not new Linux failure reproductions or historical bisection results. No product/test repair, assertion relaxation, timeout extension or checkpoint source-stamp fabrication occurred.

All owned foreground build/test drivers completed. Removed **28 producer-inventoried `bin-c1287-67a7e42a` output roots**, with none remaining. Raw consoles retain fixture temporary-root cleanup diagnostics; no blanket temporary-directory deletion was performed. Generated logs, TRX, HTML and JSON remain ignored and outside the committed deliverables.

The release train can reuse the class list and literal command above, with a window based on the observed 40m34s–42m15s test-driver range plus its separate build/queue allowance. Ordinary Review should assess the explicit coverage exclusions and this evidence before adoption; the former whole-Unit timeout is not relabeled as a pass.
