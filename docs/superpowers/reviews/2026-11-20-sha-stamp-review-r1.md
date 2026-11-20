# CARD-1514 deploy-only SHA stamp: review round 1

Date: 2026-11-20. Review task `a1de8d74`, ordinary round 1, Final.
Subject: `origin/feat/card-task-da4f0cb2` at `48c72753763b490dc962f45b4a9be6d8b8cae65b` (Code task `da4f0cb2`). The author was Grok and the reviewer is Claude Opus.
Range: `9dbcd3471ef0d46ef96fcf101bd5214253415309..48c72753763b490dc962f45b4a9be6d8b8cae65b` (7 commits, 16 files).

## Verdict

CLEAN. No blockers and no regressions. Every binary that is deployed or compared to a SHA is still stamped, and nothing that reads the stamp from an ordinary build refuses `unknown`. The advisories below do not hold the land.

Scope: Interim. Eleven of the twelve plan rows ran green at the reviewed SHA in one checkpoint run (`20261120-164553-2553`, receipt validated). CP-3 (`PtyHostDeploymentGuardTests`, which needs a second, SessionRunner.Tests build) was not run because the brief allowed one build. From the source, its three assertions pass at the tip: `run-daemon.ps1` still contains `$BuildProjectDir` and the literal `dotnet build` (in its header comment at line 12), and `autostart-session-runner.ps1` and `Antiphon.AppHost/Program.cs` are unchanged. A Final completion needs only CP-3.

## 1. Every build that is deployed or compared to a SHA

| Path | Stamped | Evidence |
|---|---|---|
| `restart-apphost.ps1` -> `dev-aspire.ps1` -> AppHost build -> `Antiphon.Server` | yes | `dev-aspire.ps1:158-163` reads `git -C $root rev-parse HEAD`, refuses unless 40 hex (`$ErrorActionPreference = 'Stop'` at line 16, so `Write-Error` terminates), and runs `dotnet build -p:SourceRevisionId=$sourceRevision` in the AppHost directory. `Antiphon.AppHost.csproj:24` is a plain `ProjectReference` to `server/Antiphon.Server.csproj`. The Aspire 9.3 SDK only adds `ReferenceOutputAssembly=false` (`aspire.apphost.sdk/9.3.0/Sdk/Sdk.targets:27-34`); `ResolveProjectReferences` still builds the server, and a command-line global property flows into every `MSBuild` task call unless it is in `GlobalPropertiesToRemove`/`RemoveProperties`. Aspire removes only `TargetFramework;RuntimeIdentifier;SelfContained` (`Aspire.Hosting.AppHost.targets:170`, and that call is only `GetTargetFrameworks`). The server's own references (Server.Git, Observability, ...) get the same property. DCP starts the server with `dotnet run --no-build`, so it is not rebuilt unstamped. `Two_heads_produce_identical_assembly_inputs` (CP-8) proves the flow for a plain `ProjectReference` with the real `Directory.Build.props`. |
| Desktop session runner and fake gateway: AppHost `AddDaemonProcess` -> `run-daemon.ps1 -BuildProjectDir` (`DaemonProcessService.cs:95,278-281`), `autostart-session-runner.ps1:133`, `restart-session-runner.ps1` (soft and `-Hard` both relaunch through run-daemon) | yes, warn-and-unstamped when HEAD is unreadable | `scripts/run-daemon.ps1:145-151`. `Antiphon.PtyHost` arrives through the runner's `ProjectReference` (`Antiphon.SessionRunner.csproj:36`), so it is stamped too; the `Exec` child PtyHost builds that would drop the property are test-project only. |
| server2 server and runner images (`deploy-server2.ps1`, `restart-linux-server.ps1`, `c590-remote.sh`, staging server) | yes, unchanged | `Dockerfile:19,26`, `docker/session-runner-grok/Dockerfile:36-41` (runner, PtyHost, FakeGrok), `docker/tests/Dockerfile:40-42`; `c590-remote.sh:253,1416,1419` pass `--build-arg SOURCE_REVISION=$SHA`; compose files pass `${SOURCE_REVISION:-unknown}` from the env file c590 writes (`c590-remote.sh:166-176`). The Docker context never relied on `.git`. `EnableSourceLink=false` removes nothing the image needs (`AntiphonVersion` reads the `+<sha>` suffix; SourceLink only fed PDB URLs). |
| Windows phone-home runner | yes | `scripts/start-windows-phone-home-runner.ps1:70-72` (refuses; `$ErrorActionPreference='Stop'` at 37). `-NoBuild` runs whatever exe is on disk (unchanged behaviour, see A-3). |
| Nightly watchdog binary | yes | `scripts/deploy-nightly-watchdog.ps1:176-179` (refuses only when it builds its own default publisher; an injected `-PublishRunner` is the test seam). |
| NuGet packages | yes | `.github/workflows/publish-nuget.yml:49-54`, `-p:EnableSourceLink=true -p:SourceRevisionId=${{ github.sha }}`. No other workflow builds. |
| Nightly, rc qualification, checkpoint, land verification, `post-land-early-warning`, release scripts | no, by design | None reads an assembly stamp: `grep` of `tools/Antiphon.Checkpoints`, `tools/Antiphon.LandGraphAudit*`, `AgentTaskLand*`, `LandVerification*`, `scripts/lib/nightly-*`, `rc-*`, `release-*`, `checkpoint-*` finds only the checkpoint tool's `--version` banner (`Program.cs:76`). `publish-release.ps1`, `release-cut.ps1`, `promote-release.ps1` build nothing. |
| Legacy non-Aspire paths: `dev-start.ps1:62` (`dotnet run` server), `dev-upgrade.ps1:29`, `restart-server.ps1:36` | no | Now `unknown`. Nothing compares them to a SHA. See A-1. |

No deployed-and-compared path is unstamped.

## 2. Runtime readers of the stamp on a non-deploy build

- `GET /api/version` (`VersionEndpoints.cs:20-21`) reports `unknown` from a test or dev build. Only deploy-time scripts compare it (`restart-apphost.ps1` exit 5, `cutover-postgres-server2.ps1:1064-1067`, `deploy-am-service.ps1:325`, `release-status.ps1`), and each reads a deployed server.
- `restart-apphost.ps1 -ExpectedServerSha` and the HEAD compare go through `Test-AppHostBuildShaFormat`/`Resolve-AppHostLoadedIdentity`, so `unknown` is `malformed` and exit 5. That is the intended gate.
- Runner `buildVersion`/`commitSha` (`RunnerBuildIdentity.cs`, `SessionRunnerRuntime.cs:1112`, `PhoneHomeRuntimeAdapter.cs:117`) become null/`unknown` on an unstamped runner; the server only displays them (`DiagnosticsBundleService.cs:97,115`) and `check-daemon-build.ps1:49-51` prints "build identity unavailable". Nothing refuses.
- Checkpoint `--expected-source-sha` and `buildSource=verified` use the git source snapshot and the build-source stamp file, never the assembly version.
- Tests: no other test asserts a 40-hex runner or server stamp (`RunnerCapabilitiesTests` uses a literal DTO; `OutputDistillationApplyCanaryTests:301` and `RestartPreflightCache.cs:115` only record it).

## 3. The two test edits

- `HealthEndpointTests` now accepts `40-hex|unknown`, but adds `InformationalVersion == AntiphonVersion.Informational` and `EndsWith("+" + Version)`, plus three `ResolveSha` parse cases for the deployed form. The deploy guarantee moved to `restart-apphost.ps1` exit 5 and the static witness. Not a weakening of a deploy path.
- `C1022BackendLaunchTests` (Windows only, UNOBSERVED here) now compares each loaded DLL's `ProductVersion` to the runner's informational version. On an unstamped build both are `1.0.0+unknown`, so the "loaded host is the same build" check is constant for any unstamped pty-host. See A-2. It is not a deploy path.

## 4. Land-graph refresh

`040bfe249` touches only `LandVerificationBuildGraph.evaluation.txt` and `.manifest.txt`, with the land-refresh script's commit subject; the pins row removal is in the production commit `0aa86b9e6` with the target it pinned. The SDK line moves `10.0.204 -> 10.0.404`, which master's own refreshes do on server2 every day. `EnableSourceLink` was added to the precheck property allowlist (`LandGraphPrecheck.cs:227`). The stamp value is not a graph input: `SourceRevisionId` stays a fixed evaluation property (`unknown`), already allowlisted. Master changed no graph input since the base (`git diff --name-only 9dbcd347 origin/master` has no csproj/props/targets/graph file). CP-6 `-Check` exited 0 at the tip.

## 5. The witness

`SourceRevisionStampContractTests`, 8 results. At `0d798cc26` the claimed 7 failed / 1 passed is consistent with the source: the one that passes there, `Checkpoint_nightly_and_land_builds_are_unstamped`, is a forward guard (it fails only if someone adds the property to those builders), not a witness of this change. Not re-run at the red commit (one-build rule).

Mutants were not run. The checkpoint run deleted its output when it finished, the brief allowed one build, and the standing authority waives mutation checks. Read from the source, each removal is caught. Removing the property from `deploy-nightly-watchdog.ps1`, from a `publish-nuget.yml` pack line or from a Dockerfile `RUN dotnet publish` makes `Deploy_surfaces_stamp_every_product_build` list that invocation. Removing it from `dev-aspire.ps1` turns that `Deployed_builds_stamp_head` case, `Restart_apphost_refuses_an_unstamped_server` and the surface census red. Deleting the `$buildArgs +=` line in `run-daemon.ps1` turns its `Deployed_builds_stamp_head` case and the splat check red, because the comment above that line does not contain `-p:SourceRevisionId=`. Removing it from the phone-home start script turns its case and the census red. Restoring the git `Target` in the props turns `Ordinary_builds_never_read_git_for_the_stamp` and `Two_heads_produce_identical_assembly_inputs` red.

The witness is textual and can be fooled: see A-4.

## 6. PowerShell 5.1 and ASCII

The four edited scripts have no non-ASCII bytes and parse with zero errors. The added syntax (`-match`, `-notmatch`, `.ToLowerInvariant()`, `+=` on an array, `throw`) is 5.1 syntax. `Directory.Build.props` has two pre-existing non-ASCII dashes (same at the base); it is not a PowerShell file.

HEAD-not-40-hex cases: a shallow clone and a detached worktree both answer `rev-parse HEAD` with a full SHA. A tree without `.git` refuses in `dev-aspire.ps1` and the phone-home start script, and warns in `run-daemon.ps1`; with `git` missing, `& git ... 2>$null` under `Continue` is a non-terminating error and yields an empty string (checked in pwsh). No Docker path runs `dev-aspire.ps1`; Docker stamps through the build arg.

## 7. Merge and pins

`git merge-tree --write-tree origin/master HEAD` against `24c284ca8cb87dd4f1c04fd06de2b6da60f56592` conflicts only in `tests/derived-impact-index.json`. The land replay must regenerate the index. `PinnedInputDigest` is `15137e48...a9365` on the branch and on master; `Directory.Build.props` is not a protected input, and CP-11 recomputed it and it equals the pin (exit 0). The guard floor 84 is the sum of the nine named classes (45+1+5+2+3+11+5+11+1); `RunnerDefaultGuidanceTests` (4) shares a file with `TaskPlatformGuidanceTests` but is a different class outside the filter, so 88 counted a class the row does not name. Legitimate.

## Checkpoint run

Run `20261120-164553-2553`, started with `--rows CP-1,CP-2,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12 --expected-source-sha 48c72753763b490dc962f45b4a9be6d8b8cae65b` on server2 (1-minute load 10.4 at start, under the brief's 14). Source `48c72753763b490dc962f45b4a9be6d8b8cae65b` `state=clean buildSource=verified`, every row `slot=granted dirty=0`, `unlisted: none`. `validate --evidence report.json --expected-source-sha 48c72753...` printed `CHECKPOINT SOURCE VALID rows=11`. Wall time 24m45s. The cold `Antiphon.Tests` build took 270 s.

| CP | Result |
|---|---|
| CP-1 `SourceRevisionStampContractTests` | 8/8 passed, 0 skipped |
| CP-2 `HealthEndpointTests` | 5/5 |
| CP-3 `PtyHostDeploymentGuardTests` | NOT RUN (second build; one-build rule) |
| CP-4 three `DockerStackContractTests` revision methods | 3/3 |
| CP-5 `LinuxServerRestartGateTests` | 17/17 |
| CP-6 `audit-land-verification-graph.ps1 -Check` | exit 0 |
| CP-7 seven AppHost fixture scripts | exit 0 (434 s) |
| CP-8 `Two_heads_produce_identical_assembly_inputs` | 1/1 |
| CP-9 nine-class guard union | 84/84 (floor 84) |
| CP-10 `DerivedImpactIndexTests` | 27/27 |
| CP-11 nightly pins | exit 0, digest equals the pin |
| CP-12 `check-evidence-diff.ps1` `9dbcd347..HEAD` | exit 0 |

Unlisted runs, each through its own build-slot lease: `dotnet build tools/Antiphon.Checkpoints` (the runner itself, 8 s) and `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` (the CP-10 prerequisite named in the plan, 3 s). No PC rows exist; none are discharged.

## Unobserved (Windows)

- `C1022BackendLaunchTests`: a stale pty-host from another unstamped build would pass the identity check (A-2). It could break only if the runner and pty-host stamps differ, for example a pty-host DLL left from a stamped deploy build in the same output.
- `scripts/test-windows-phone-home-runner.ps1`: it reads the start script's AST. The added `git` command and `throw` statement could trip a census of named commands if the census is strict about the command set; not observed.
- `DaemonStartupDiagnosticsTests`, `DaemonLogRotationTests`, `RunnerRestartScriptCompatibilityTests`: their run-daemon fixture passes `-BuildProjectDir 'fixture-only'`, so `git -C fixture-only` fails and the fixture now logs an extra `WARNING ... building unstamped` line before each build. The visible assertions count builds and launch records, not log lines, so it should stay green.
- `WindowsUnitPlatformAdmissionTests`.

## Activation

The first restart onto this change is self-verifying: `scripts/restart-apphost.ps1` runs the new `dev-aspire.ps1` from disk, and exit 0 requires `GET /api/version` to report the 40-hex source-root HEAD. An unstamped server is exit 5 with the restart lock retained. After exit 0, confirm `GET /api/version` shows `version` equal to the canonical HEAD and `informationalVersion` ending in `+<that sha>`.

The desktop session-runner supervisor is a long-lived `run-daemon.ps1` loop already parsed into memory. Until it is replaced, its soft relaunches run the old build line without `-p:SourceRevisionId`, and the new `Directory.Build.props` no longer stamps from git, so the runner reports no commit SHA. Do one planned `scripts/restart-session-runner.ps1 -Hard` after the AppHost restart, then check `scripts/check-daemon-build.ps1` (or runner capabilities `build.commitSha`) equals HEAD. Nothing gates on the runner SHA, so this is a reporting gap, not an outage.

## Advisories (do not hold the land; file as cards)

- A-1. Legacy non-Aspire paths `dev-start.ps1`, `dev-upgrade.ps1` and `restart-server.ps1` now build an unstamped server. Nothing compares them, but the witness's surface census does not see them (it lists only `dev-aspire.ps1` at the root, and its regex has no `dotnet run`). Either stamp them or say in the props comment that they are unstamped.
- A-2. `C1022BackendLaunchTests.DescribeBuild` compares two `1.0.0+unknown` strings on every ordinary build, so it no longer distinguishes a stale pty-host. Compare the loaded `Antiphon.PtyHost.dll` hash or MVID with the copy in the runner's output instead.
- A-3. `start-windows-phone-home-runner.ps1 -NoBuild` runs whatever exe is on disk, which may now be an unstamped test build. Consider a warning when `-NoBuild` finds an `unknown` stamp.
- A-4. The static witness can be fooled: `Deploy_surfaces_stamp_every_product_build` skips any invocation whose previous character is a quote (a `"dotnet publish ..."` string handed to `Start-Process` or `Invoke-Expression` is never checked); a `# -p:SourceRevisionId` comment on the same line satisfies it; the splatted-`dotnet @` check is a whole-file `Contains`, so a comment or an `if ($false)` branch satisfies it, and `Deployed_builds_stamp_head` checks the text of the line, not that the branch runs.
- A-5. Activation: the first desktop runner restart after landing must be `-Hard` (see Activation). Worth one line in the land note.
