# SHA stamp checkpoints (task da4f0cb2)

Base `9dbcd3471ef0d46ef96fcf101bd5214253415309`. Ordinary builds keep
`SourceRevisionId` as `unknown` and set `EnableSourceLink` false, so a new
commit is not a compile input. A build whose binary is deployed passes
`-p:SourceRevisionId=<40-hex HEAD>`.

Deploy surfaces that stamp: `dev-aspire.ps1` (refuses an unreadable HEAD;
the AppHost `ProjectReference` carries the global property into the server
project), `scripts/run-daemon.ps1` (warns and builds unstamped when HEAD is
unreadable; that is the investigation design), `scripts/start-windows-phone-home-runner.ps1`
(refuses), `scripts/deploy-nightly-watchdog.ps1` (refuses), the three
Dockerfiles (already passed `$SOURCE_REVISION`), and the six `dotnet pack`
lines in `.github/workflows/publish-nuget.yml` (`-p:EnableSourceLink=true`
as well). `samples/EchoGateway` restores already-published packages and is
not an Antiphon deploy binary.

`restart-apphost.ps1` reaches exit 0 only when the dashboard, `/health`, and
`/api/version` are up and the loaded SHA matches source HEAD (or
`-ExpectedServerSha`). `Test-AppHostBuildShaFormat` accepts only 40 or 64
hex, so `unknown` is `malformed` and the script exits 5 with
`REFUSED: server build unverified`. This plan does not restart anything.

## Verification design

- V-1. `SourceRevisionStampContractTests`. Eight results: ordinary props stay constant, three deployed scripts stamp HEAD, the deploy-surface census, the restart refusal, unstamped checkpoint/nightly/land builds, and the two-HEAD probe. The witness was red at `0d798cc26e7b17f43113d7171b05e2bbdf175fca` before these production edits (7 failed, 1 passed).
- V-2. `HealthEndpointTests`. Five results. The HTTP body matches the in-process `AntiphonVersion` stamp (`40-hex` or `unknown`). `ResolveSha` still parses a deployed `+<40-hex>` stamp. The class boots `AntiphonWebAppFactory`, which clones a Testcontainers database. That is not the production Postgres.
- V-3. `PtyHostDeploymentGuardTests`. Three results. The other `run-daemon.ps1` consumers skip on Linux (`DaemonStartupDiagnosticsTests`, `DaemonLogRotationTests`, `RunnerRestartScriptCompatibilityTests`). A skip fails a clean receipt, so those classes are UNOBSERVED, not passed.
- V-4. The three Dockerfile revision methods in `DockerStackContractTests`: `Server_publish_has_revision`, `Runner_publish_has_revision`, `Fixture_publish_has_revision`.
- V-5. `LinuxServerRestartGateTests`. Seventeen results. The class is `NativeLinuxOnly` and runs on Linux.
- V-6. `scripts/audit-land-verification-graph.ps1 -Check` after `scripts/land-graph-land-refresh.ps1` has committed the manifest. Exit 0. This row does not pass `-Update`.
- V-7. Isolated AppHost fixture scripts. Exit 0. They do not bind live ports.
- V-8. `Two_heads_produce_identical_assembly_inputs`. Two temp repos at different HEADs, using the real `Directory.Build.props`, produce the same assembly inputs, and `-p:SourceRevisionId` on the entry project reaches the referenced project. This replaces a full docs-only rebuild of the 40-assembly closure.
- V-9. Nine-class guard union, parenthesized, without `WindowsUnitPlatformAdmissionTests` and without `DerivedImpactIndexTests`. Static floor 88: policy 45, guard 1, merge 5, tripwire 2, native Linux 3, mechanical 11, TaskPlatformGuidance 9, C1729 11, lane 1. C1729 stays VIOLATIONS 0 LISTED 162. Recount from the TRX.
- V-10. `DerivedImpactIndexTests` alone. Floor 27. The driver project is built first; this test project does not produce that DLL.
- V-11. Recomputed `PinnedInputDigest` equals the pin in `scripts/test-live-lane-run.ps1`. `NightlyPolicy.Read` refuses a stale `policyHash`. Reseal only when a protected input changed. `Directory.Build.props` is not a protected input.
- V-12. `scripts/check-evidence-diff.ps1` from `9dbcd3471ef0d46ef96fcf101bd5214253415309` to HEAD.

UNOBSERVED, not executed, not passed:

- `C1022BackendLaunchTests` and `scripts/test-windows-phone-home-runner.ps1`. Windows. The C1022 edit compares informational versions between the runner and pty-host. It does not accept `unknown` as a deployed SHA.
- `WindowsUnitPlatformAdmissionTests`, including `C1210_Windows_linux_only_outcomes_are_exact`, which skips on Linux.
- `DaemonStartupDiagnosticsTests`, `DaemonLogRotationTests`, `RunnerRestartScriptCompatibilityTests`.

Short-test omissions (operator 2026-11-18). Not passed:

- The rest of `DockerStackContractTests` (the class is about 132 tests).
- `LandNonCodeSkipTests`, `LandGraphLandRefreshTests`, `LandGraphEvaluationEnvironmentTests`, `LandGraphEvaluationScriptTests`, and `LogRetentionTests`. Land-graph evidence for this change is V-6.

Follow-up, not fixed here:

- `Antiphon.PtyHost` and `Antiphon.PtyHost.Protocol` recompile on every build with no source change. CARD-1664 RID child `obj/` sharing is the suspected cause.
- `restart-apphost.ps1` could exit 5 on the first healthy probe that reports `unknown` instead of waiting out the no-progress window.
- Checkpoint Build rows stay cold. The stamp fix does not shorten them.

Positive controls: this plan has no PC-n row. None are discharged. Mutation owns method-scoped controls.

Command rows run under `/bin/sh -lc`. A PowerShell `-Command` body is single-quoted so the shell does not expand `$`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-sha-stamp-a/` | stamp-witness | `/*/*/SourceRevisionStampContractTests/*` | V-1 | 0 failed, 0 skipped, executed at least 8 | 8 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | CP-1 | health-stamp | `/*/*/HealthEndpointTests/*` | V-2 | 0 failed, 0 skipped, executed at least 5 | 5 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | `tests/Antiphon.SessionRunner.Tests -> bin-sha-stamp-b/` | ptyhost-guard | `/*/*/PtyHostDeploymentGuardTests/*` | V-3 | 0 failed, 0 skipped, executed at least 3 | 3 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | CP-1 | docker-revision | `/*/*/DockerStackContractTests/(Server_publish_has_revision*)\|(Runner_publish_has_revision*)\|(Fixture_publish_has_revision*)` | V-4 | 0 failed, 0 skipped, executed at least 3 | 3 | 6 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | CP-1 | linux-restart | `/*/*/LinuxServerRestartGateTests/*` | V-5 | 0 failed, 0 skipped, executed at least 17 | 17 | 6 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | n/a | land-check | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | V-6 | exit 0 | n/a | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | n/a | apphost-fixtures | `pwsh -NoProfile -Command '$names = @("test-apphost-server-version","test-apphost-dead-launch","test-apphost-health-window","test-apphost-graceful-stop","test-apphost-lock-hints","test-apphost-main-worktree-guard","test-dev-postgres-optout"); foreach ($n in $names) { & pwsh -NoProfile -File ("scripts/" + $n + ".ps1"); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }'` | V-7 | exit 0 | n/a | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | all | CP-1 | two-heads | `/*/*/SourceRevisionStampContractTests/Two_heads*` | V-8 | 0 failed, 0 skipped, executed at least 1 | 1 | 6 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-9 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-9 | 0 failed, 0 skipped, executed at least 88 | 88 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-10 | all | CP-1 | impact-index | `/*/*/DerivedImpactIndexTests/*` | V-10 | 0 failed, 0 skipped, executed at least 27 | 27 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-11 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; if (-not (Test-NightlyCoreAvailable)) { throw "nightly core unavailable" }; $repo = (Get-Location).Path; $policy = [Antiphon.Nightly.NightlyPolicy]::Read($repo, ""); $lines = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest); $d = ($lines \| Select-Object -Last 1).Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; if ($d -cne $m.Groups[1].Value) { throw ("input digest drift computed=" + $d) }; Write-Output ("policyHash=" + $policy.Hash); Write-Output ("inputDigest=" + $d)'` | V-11 | exit 0 and the printed digest equals the script pin | n/a | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-12 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 9dbcd3471ef0d46ef96fcf101bd5214253415309 -HeadRef HEAD` | V-12 | exit 0 | n/a | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |

### Cost

Ordinary floor is 102 minutes (sum of EstimatedMinutes). CP-1 is a cold
`Antiphon.Tests` build. CP-3 is a cold SessionRunner build. Later test rows
reuse those outputs.

Unlisted, with a reason:

- The red witness run at `0d798cc26e7b17f43113d7171b05e2bbdf175fca` is the required pre-fix proof. It is not a closed-list row.
- `scripts/land-graph-land-refresh.ps1 -Base 9dbcd3471ef0d46ef96fcf101bd5214253415309` runs before CP-6 and commits the manifest. A bare `-Update` is forbidden.
- `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` runs before CP-10. The index regeneration, when the new test class changes `tests/derived-impact-index.json`, is its own last commit and is not a checkpoint row.
