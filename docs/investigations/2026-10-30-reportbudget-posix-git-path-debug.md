# ReportBudgetWorkspace POSIX Git path qualification failure

Windows rc qualification `d47848cc`, candidate `cde8def41`, group 80 reported 20 failed / 295 passed / 1 skipped. Every failure began in `ReportBudgetWorkspace.GitAsync`: Windows could not execute `/usr/bin/git`. The starting source was `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`.

The helper manually split PATH on `:` and searched for `git` without the Windows executable suffix. A Windows PATH cannot satisfy that lookup; the unconditional fallback was `/usr/bin/git`. Use the existing `ScratchGitRepo.GitInAsync` for template seeding and the existing `ProcessStartInfo("git")` convention (`CheckpointFixtureProcess.Git`, `BuildConfinement.Git`) for remaining launches. No new PATH resolver is introduced.

## Sweep

The complete [file:line inventory](2026-10-30-reportbudget-posix-git-path-sweep.md) records 696 matching lines across 180 files (691 primary hits plus five supplemental PowerShell Bash-resolver locations), at the starting source. All hits are classified, including inert transcript/JSON/source-assertion data. The executable-focused follow-up checked ProcessStartInfo/FileName/DriverRequest/Run sites.

| Classification | Sites | Action |
|---|---|---|
| a: Windows failure | `tests/Antiphon.Tests/TestHelpers/ReportBudgetWorkspace.cs:87-95,129-149` | Reuse installed-Git fixture helper; request bare `git` for remaining launches. |
| a: Windows failure | `tests/Antiphon.Tests/Checkpoints/CheckpointFixtureGitEnvironmentTests.cs:59,61,109,115,122,124` | Replace direct `/bin/sh`, `/bin/echo`, `/bin/sleep` launches with PATH-resolved `pwsh`. Preserve every environment/output/cancellation/kill assertion. |
| b: existing gates | Linux custody, FIFO, shell-script and delayed Git fixtures, plus existing policy exclusions | Leave unchanged; table names gates, Windows alternatives, WSL/container lanes, and Git hook handling. |
| c: unmarked Linux-only test | None | No test removed, newly skipped, relaxed, or moved to another platform. No marker, policy, baseline or CP-36 skip-census edit. |

`AgentPinPublicationTests` is already excluded on Windows by the CARD-0262 policy row; its `flock ... sh -c` is not a newly reachable Windows launch. Git hooks execute through installed Git (including Git for Windows), rather than launching host `/bin/sh`. Container `/tmp` paths stay inside the owned Linux database; portable WSL contracts retain their Windows launcher.

Read `scripts/lib/checkpoint-usage.ps1` (`Get-NamespaceCensus` / declared-name skips), the test execution policy, the CARD-1613 reflection witness contract and the testing owner before this classification. The checkpoint class keeps its four tests and gains no skip. `Slow_git_response_does_not_spend_write_budget` retains its existing NativeLinuxGate: Linux runs 21 budget cases; Windows owes the 20 portable cases and retains that one Linux-only skip.

## Witness and verification

The new `ReportBudgetWorkspacePortabilityTests.Git_launch_uses_the_installed_PATH_command` invokes the real resolver, requires the shared bare-command contract, then launches its returned command and requires a successful `git version` response. It does not scan C# source or emulate Windows. Its red assertion detects the Linux absolute-path result even on Linux, where all the original budget cases could pass.

Red source (committed and pushed before repair): `057ceedae5d31119129669d94781e173defd7125`. Isolated build passed, 0 errors / 1395 warnings, MSBuild wall 163.71 s; host slot wait 285 s. Exact witness filter executed 1, failed 1, passed 0, skipped 0 (TRX `red/red-witness.trx`, 1.925 s test-run duration; slot wait 45 s). Its failure was the intended assertion: expected `git`, actual `/usr/local/bin/git`. This is an assertion red, not a build, fixture, or zero-selection failure.

Repair source (committed and pushed): `88904f5446a6daa6b84efa294c197ce68681f49f`.

The test DLL ProductVersion is `1.0.0+88904f5446a6daa6b84efa294c197ce68681f49f`; SHA-256 `7c955343154f094bd7aa1aa1f8f0a375aaa7a1439460b90ac2e9f8b27b14b3e8`. The real evaluator from that same build has SHA-256 `0136724191dcabd75940a32c63405a7a91beabb49a19882b2514f1d7db30c38f`.

Green isolated build passed, 0 errors / 1395 warnings, MSBuild wall 198.17 s, slot wait 0 s. Fresh Linux TRX results at that repair SHA (all zero failures/skips):

| Exact filter | Passed / total | Test-run duration | Driver wall |
|---|---|---|---|
| `/*/*/ReportBudgetWorkspacePortabilityTests/*` | 1 / 1 | 1.440 s | 8.200 s |
| `/*/*/AgentReportStoreBudgetTests/*` | 21 / 21 | 25.200 s | 32.382 s |
| `/*/*/CheckpointFixtureGitEnvironmentTests/*` | 4 / 4 | 10.858 s | 17.321 s |

The shared test/check lease waited 300 s and was held 99 s. TRX files are in `/work/worktrees/task-dc31de0e/.antiphon/evidence/dc31de0e/green/`, one per class, with exact case names and durations; JSONL phase records are in the sibling `timing/` directory. Build, red, green and graph logs remain in that evidence root. These are single-run samples, not calibrated per-method p95 budgets. The selected tests use owned local Git/child processes; no database, Docker, web host or native Windows setup is used. Existing checkpoint assertions and test attributes were also compared with the baseline and are byte-identical.

All builds/runs used `TEMP=/tmp`, `UseAppHost=false`, `OutputPath=bin-dc31de0e/`, and `scripts/build-slot.ps1` (granted maxcpucount 6). Test runs used `dotnet run --project tests/Antiphon.Tests --no-build`, exact filters and fresh TRX output. Opt-in per-test phase recording was enabled; absent fixture/CLI subphase spans remain unknown. No test timeout was widened; no over-30-minute batch was selected.

Reproduce after rebuilding through the gate: `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-dc31de0e/ --property:UseAppHost=false`; run each class filter below with `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-dc31de0e/ --property:UseAppHost=false -- --treenode-filter <filter> --report-trx` through the same gate. The graph command was `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-dc31de0e/Antiphon.LandGraphAudit.dll`, inside that lease.

No build-definition file changed. Real land graph `-Check`: exit 0, `approved`, digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, count 68; `-Update` was not run. No tags, publish, rebase, native test, live qualification modification or access to `C:\Antiphon`.

## Integration probe

After fetching, `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` passed at rc `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`. No merge or rebase was performed. The git-common probe against fetched master `31f990317038635c2f1e652b6a950905fcf07a31` exited 1 with seven conflicts: `.antiphon/task-af2b78c8.md`, `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, `docs/investigations/2026-10-30-card-1618-sf-aggregate-run-debug.md`, `server/Application/Services/CapacityRecoveryService.cs`, `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`, and the two repaired fixture files. This is an rc-targeted branch; no master landability is claimed, and those conflicts were left untouched.

## Native work owed

After qualification `d47848cc` ends and releases its native lock, run these exact class filters once on Windows from the merged source, using `dotnet run --project tests/Antiphon.Tests` through `scripts/build-slot.ps1` and an isolated output directory:

| Filter | Native expectation |
|---|---|
| `/*/*/AgentReportStoreBudgetTests/*` | 20 pass; the existing delayed-Git Linux-only case remains skipped. |
| `/*/*/CheckpointFixtureGitEnvironmentTests/*` | 4 pass; no skips. |
| `/*/*/ReportBudgetWorkspacePortabilityTests/*` | 1 pass; no skips. |

Linux receipts do not earn native Windows execution credit. No approval was requested: the caller's standing authority already covers this investigation, repair, commits and pushes.
