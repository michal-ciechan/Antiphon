# Linux master readiness for the next rc cut

Date: 2026-11-19. Verify task 316b433c. Read-only on the runner mirror except this note. Tip: `c93364585afe99a99c770d23ec8ff7d2dc23bfa0` (branch `feat/card-task-316b433c` before this document). Previous rc cut compared for evidence history: `997f9b8d9ffc4ba870d2fd56caf7c03781591624`.

## Verdict

GREEN (cut now). No red test. The committed derived-impact index matches the builder. `policyHash` and `PinnedInputDigest` match their recomputed values. `scripts/audit-land-verification-graph.ps1 -Check` printed `approved`. `scripts/check-evidence-diff.ps1` over `997f9b8d9ffc..HEAD` reported 147 commits, 0 entries, 0 violations.

No detached comparison worktree was added. Nothing failed, so nothing was re-run at the previous rc cut.

## How it was run

One native `dotnet build` of `tests/Antiphon.Tests/Antiphon.Tests.csproj` (`-p:UseAppHost=false`, no custom output) through `scripts/build-slot.ps1`: exit 0, 0 errors, 2203 warnings, 8m 42s. Test rows used `dotnet run --no-build -p:UseAppHost=false` with `--timeout 570s` and `ANTIPHON_LINUX_COMPILER_POLICY=0`. The guard-union row started at 1-minute load 36.10 after a 90s wait; every later row started below 16. `tests/Antiphon.SessionRunner.Tests` was built once more because its existing output was older than `C1677TempFamilyRegistryTests.cs` (exit 0, 0 errors, 189 warnings).

## Rows

| Row | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| DerivedImpactIndexTests | 27 | 27 | 0 | 0 |
| scripts/test-nightly-core.ps1 -Case CoreMatchesFallback | 35 | 35 | 0 | 0 |
| NightlyCoreTests | 1 | 1 | 0 | 0 |
| Guard union (11 classes) | 88 | 87 | 0 | 1 |
| LongPathsCheckoutScriptTests, C1744G3PhoneHomeSupervisionTests, LinuxSharedCompilerPolicyTests, AgentTaskGetPollMetricsTests, DockerStackDocumentationTests, DockerStackContractTests | 185 | 185 | 0 | 0 |
| CARD-1995 land tests (`/*/*/*/*C1995*`) | 19 | 19 | 0 | 0 |
| C1677TempFamilyRegistryTests (Antiphon.SessionRunner.Tests) | 3 | 3 | 0 | 0 |
| audit-land-verification-graph.ps1 -Check | 1 | 1 | 0 | 0 |
| policyHash and PinnedInputDigest | 2 | 2 | 0 | 0 |
| check-evidence-diff 997f9b8d9ffc..HEAD | 1 | 1 | 0 | 0 |

The guard skip is `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`: "CARD-1210: the child skip-roster audit requires Windows placement." That skip is the declared Linux outcome.

`DerivedImpactIndexTests` includes `committed_index_matches_the_builder`. The committed `tests/derived-impact-index.json` matches the builder. It is not stale. No class list differs.

Nightly harness: `C487: 35 passed, 0 failed, 35 rows`, exit 0. One informational line, not a failure: `NOTE NC CoreMatchesFallback.coverageParts.hidden skipped: FileAttributes.Hidden did not persist; the Windows golden still requires hidden=a.diag`. The row `NC CoreMatchesFallback.coverageParts` then passed. `NightlyCoreTests.CoreMatchesFallback` passed and is the same 35-row case.

Pins:

- policyHash stated and computed: `0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542`
- PinnedInputDigest stated and computed: `a08d61c11698379cd0b1e464c3032681d9cd10ea8eb78fb0f40255838e32050c`

Land graph: tool rebuilt in 14s (the default DLL was missing), `digest=C5B7C0CEC63A241101BD6ED60C1C293CF797CB50F4B111FFED169B4529EC43FC count=80`, then `approved`, exit 0. The `outside-solution` and `not-built` lines are the audit's inventory; they did not refuse.

`NativeLinuxMigrationAudit` is not a script. The tool project is `tools/Antiphon.NativeLinuxMigrationAudit`. It was not run. The land audit lists that project as `not-built`, which is outside the solution graph and did not block approval.
