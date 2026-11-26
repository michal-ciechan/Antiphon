# Docs-only commits recompile every assembly: the HEAD SHA stamp and SourceLink (2026-11-20)

Base: master `ce3670f5fbfe08c1a6d6cff694cce9f4d78f7825`. Task `c2b35b62` (Investigate). Operator question:
"Can we somehow stop doing that? Or only do that on specific release builds?"

## Verdict

**CONFIRMED, with a correction.** A docs-only commit recompiles all 40 assemblies of the
`tests/Antiphon.Tests` closure. That costs about the same as a cold build. There are **two** per-commit
compile inputs, not one:

1. `Directory.Build.props:32-48` (`SetSourceRevisionIdFromGit`) runs `git rev-parse HEAD` in every
   project. It writes the SHA into `SourceRevisionId`, and so into `obj/<name>.AssemblyInfo.cs`
   (`AssemblyInformationalVersion("1.0.0+<sha>")`).
2. The SDK's built-in **SourceLink** writes `obj/<name>.sourcelink.json`, which is
   `{"documents":{"<repo>/*":"https://raw.githubusercontent.com/michal-ciechan/Antiphon/<HEAD sha>/*"}}`.
   That file is passed to `csc /sourcelink:`, so it is a CoreCompile input. SourceLink is on by
   default (SDK 10.0.404, `Sdks/Microsoft.SourceLink.Common/build/Microsoft.SourceLink.Common.props:16`,
   `EnableSourceLink` defaults to `true`). It does not depend on our target.

Neutralising only the stamp (run C) still recompiled all 40 assemblies. Making the stamp constant **and**
turning SourceLink off (run E) rebuilt a docs-only commit in **18.8 s**, recompiling only 2 assemblies.
Those 2 recompile on every build, even with no change (run D); that is a separate defect, noted below.

`Microsoft.Build.Tasks.Git.targets:34` fills `SourceRevisionId` only when it is empty. Our props
pre-set it to `unknown` (`Directory.Build.props:24`), so the AssemblyInfo SHA comes from our target, not
from SourceLink.

## Measurements (server2, 2026-11-20)

Setup:
- A throwaway detached worktree of `ce3670f5` was used.
- Every build was `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false` in the
  native default output, with `ANTIPHON_LINUX_COMPILER_POLICY=0`.
- Each build ran through `scripts/build-slot.ps1` (one lease per build, `maxcpucount=6`, `waited=0s`).
  Each started only when the 1-minute load was below 16.
- Logs used `-flp` normal verbosity plus `-bl`.

How each column was measured:
- **Compiled** counts the distinct `csc /out:` paths in the log.
- **Skipped** counts the `Skipping target "CoreCompile"` lines.
- **held** is the build-slot lease time. **elapsed** is MSBuild's `Time Elapsed`.

| Run | HEAD | Extra properties | held | elapsed | Compiled | Skipped | Load at start |
|---|---|---|---|---|---|---|---|
| A cold | ce3670f5 | none | 524 s | 8:42.7 | 40 | 4 | 14.9 |
| B docs-only commit, stamped | 3c22d950 | none | 622 s | 10:19.8 | **40** | 4 | 13.1 (rose to 36–40) |
| D same commit again, stamped | 3c22d950 | none | 50 s | 0:48.2 | 2 | 42 | 16.0 |
| C0 switch to a constant stamp | 3c22d950 | `SourceRevisionId=dev` | 566 s | 9:23.9 | 40 | 4 | 14.9 |
| C docs-only commit, constant stamp | e690897c | `SourceRevisionId=dev` | 420 s | 6:56.4 | **40** | 4 | 14.0 |
| E0 also SourceLink off | e690897c | `dev` + `EnableSourceLink=false` | 33 s | 0:30.6 | 2 | 42 | 12.7 |
| E docs-only commit, constant + no SourceLink | d71d06f0 | `dev` + `EnableSourceLink=false` | **21 s** | **0:18.8** | **2** | 42 | 13.3 |
| G AppHost build, deploy-style stamp | d71d06f0 | `SourceRevisionId=<HEAD>` + `EnableSourceLink=false` | 133 s | 2:10.7 | 10 | 0 | ~13 |
| P0 proposed patch applied (switch) | 17834d3b | none | 253 s | 4:11.3 | 40 | 4 | 14.5 |
| **P docs-only commit on the proposed patch** | 7cf4135c | **none** | **27 s** | **0:24.2** | **2** | 42 | 14.9 |

Notes on the runs:
- The 2 assemblies in D, E0 and E are `Antiphon.PtyHost.dll` and `Antiphon.PtyHost.Protocol.dll`. They
  recompile on every build, even with no change (D).
- After C, `obj/Antiphon.Observability.AssemblyInfo.cs` read `1.0.0+dev`, so the stamp was constant.
  But `Antiphon.Observability.sourcelink.json` still named `e690897c…` (the new HEAD). That is why C
  recompiled everything.
- After G, `server/obj/.../Antiphon.Server.AssemblyInfo.cs`, `Antiphon.Observability` and
  `Antiphon.AppHost` read `1.0.0+d71d06f0…`. So a command-line `SourceRevisionId` on the AppHost build
  reaches the server through `ProjectReference`. `Antiphon.SessionRunner` stayed `+dev`: it is not in
  the AppHost closure, and `run-daemon.ps1` builds it separately.
- E0 did not recompile even though the `/sourcelink:` input disappeared. MSBuild's CoreCompile check
  compares timestamps, so removing an input does not dirty it. The first build after the switch keeps
  the old PDB SourceLink map, which is harmless.

- P0 and P ran with the proposed patch applied and committed in the throwaway worktree, and no extra
  properties. After P, the AssemblyInfo read `1.0.0+unknown` and no `obj/*.sourcelink.json` was written.

One more invalidator showed up: **a change to `Directory.Build.props` itself, mtime included.**
- After the witness red run, `git checkout` restored `Directory.Build.props` and the three scripts to
  their patched content. The next build (Q) changed only one test file, yet it recompiled all 40
  assemblies (3:23.3).
- `Directory.Build.props` feeds every project through `$(MSBuildAllProjects)`, an input to CoreCompile.
  The `.ps1` files are not compile inputs.
- So the cause is the rewritten props file. This is inferred and was not isolated by a separate touch-only
  run. It happens only when the props file really changes, not once per commit.

### Slot worktrees keep bin/obj (refinement)

Yes. Before this task built anything, its slot mirror `/work/worktrees/ws-04-d2` already held
`bin/` and `obj/` from the previous task:
- 43 `obj/**/*.AssemblyInfo.cs` files.
- 40 of them were stamped `+6fbf68815ca7…` (master commit `6fbf68815`, 10:39Z), written 10:41–10:43Z.

So a new task does start "warm". But its HEAD differs, so today every project recompiles anyway: run B
(a warm tree plus one docs commit) cost as much as cold run A.

The same happens **inside** a task. Every commit a delegate makes (WIP commits, "commit before a long
test run", red-then-green commits) changes HEAD. The next native build then recompiles the whole closure
(B/C: 420–622 s) instead of only what changed (E: 21 s).

### Checkpoint tool outputs are cold by construction (refinement)

The stamp has no effect on checkpoint builds, which are always cold:
- `BuildStep.AppendSharedLayout` (`tools/Antiphon.Checkpoints/Execution/BuildStep.cs:43-51`) adds
  `ArtifactsPath=<project>/bin-<id>/artifacts` and `UseArtifactsOutput=true`
  (`BuildConfinement.LayoutFor`, `tools/Antiphon.Checkpoints/BuildConfinement.cs:81-89`). So `obj/` too
  lives under the per-run `bin-<id>`.
- `SourceRunGuard` refuses a pre-existing output (`tools/Antiphon.Checkpoints/Evidence/SourceRunGuard.cs:118`,
  `pre-existing-output`).
- `CheckpointOutputCleanup` deletes the created outputs after a green run and keeps them when the run is
  red (`tools/Antiphon.Checkpoints/Cleanup/OutputCleanup.cs:5-10, 95-110`).

Every checkpoint Build row therefore pays a full cold build: about run A, **≈ 524 s** for the
`Antiphon.Tests` closure on a loaded server2, restore included. This patch does not change that.

## Who reads the stamp

What each reader needs:
- **any** means any value, or only "the same value as another binary from the same build".
- **40-hex** means a full SHA that must equal a git HEAD.

The last column says when the reader needs a real stamp:
- **Deploy** means only the build whose binary is deployed and compared needs the real SHA.
- **Test** means an ordinary test build must still satisfy it.

| Reader | file:line | Needs | When |
|---|---|---|---|
| `AntiphonVersion.Informational/Sha` (server) | `server/Application/Services/AntiphonVersion.cs:13-20` | 40-hex, or `unknown` | Deploy |
| `GET /api/version` `version` | `server/Api/Endpoints/VersionEndpoints.cs:20-21` | 40-hex | Deploy |
| restart-apphost exit 0/5 (equals source HEAD or `-ExpectedServerSha`) | `scripts/restart-apphost.ps1:391-397, 428-447`; `scripts/apphost-common.ps1:601-617` | 40-hex | Deploy (dev-aspire AppHost build) |
| cutover restart `-ExpectedServerSha $RcTip` | `scripts/cutover-postgres-server2.ps1:432, 537-538` | 40-hex | Deploy |
| restart-linux-server gate | `scripts/restart-linux-server.ps1:132, 146` (deleted 2026-11-26, unit 64 of `docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md`: `LinuxServerRestart.IsSha` and `Identity`) | 40-hex | Deploy (Docker, `SOURCE_REVISION`) |
| release-status running SHA to publication journal | `scripts/release-status.ps1:62` | 40-hex | Deploy |
| delegate.ps1 bind-failure diagnosis ("served build X") | `scripts/delegate.ps1:567-590` | 7-hex for display | Deploy (advisory) |
| Diagnostics bundle server/runner SHA | `server/Application/Services/DiagnosticsBundleService.cs:81, 93, 99` | any | Deploy (display) |
| `RunnerBuildIdentity` (`CommitSha` only for `+<40hex>`) | `src/Antiphon.SessionRunner/RunnerBuildIdentity.cs:11-34` | 40-hex, else null | Deploy |
| Runner capabilities `Version` | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:1112`; `PhoneHomeRuntimeAdapter.cs:117` | 40-hex, or `unknown` | Deploy |
| Phone-home status `buildVersion` | `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:777` | 40-hex | Deploy |
| deploy-server2 rollout gates (`buildVersion -eq $Sha`) | `scripts/deploy-server2.ps1:308, 1057, 1338-1458` | 40-hex | Deploy (Docker, `SOURCE_REVISION`) |
| c590 rollout cases (`.buildVersion == $sha`) | `scripts/c590-remote.sh:5891` | 40-hex | Deploy (Docker) |
| check-daemon-build stale warning | `scripts/check-daemon-build.ps1:48-58` | 40-hex, else "unavailable" | Deploy (advisory) |
| Runner error text "built from <7>" | `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:594` | any | display |
| Phone-home accept log | `server/Api/Endpoints/SessionRunnerEndpoints.cs:350-352` | any | log |
| Pty-host `HostVersion` vs runner version | `src/Antiphon.PtyHost/PtyHostServer.cs:16-18`; `SessionRunnerRuntime.cs:3750-3756` | same value per build (log-only on mismatch) | any |
| `LiveTiming` build tag | `src/Antiphon.Observability/LiveTiming.cs:114-119` | any (`unknown` when not hex) | log |
| FakeGateway build info | `src/Antiphon.Messaging.FakeGateway/Program.cs:65-72` | 40-hex, else null | Deploy (daemon; run-daemon builds it) |
| `antiphon-checkpoints --version` | `tools/Antiphon.Checkpoints/Program.cs:74-77` | any | display |
| **HealthEndpointTests** (2 methods: `^[0-9a-f]{40}$`, not `unknown`) | `tests/Antiphon.Tests/Application/HealthEndpointTests.cs:26-42, 45-57` | **40-hex** | **Test, goes red if unstamped** (patched) |
| **C1022BackendLaunchTests** (3 Windows-only methods: `CommitSha.ShouldNotBeNull`, ProductVersion contains it) | `tests/Antiphon.SessionRunner.Tests/C1022BackendLaunchTests.cs:79, 92-94, 124-127` | **40-hex** | **Test, goes red if unstamped** (patched to "same stamp in runner and host") |
| UnifiedOutboundTransport `ExpectedSourceSha` (the '+' suffix) | `tests/Antiphon.Tests/TestHelpers/UnifiedOutboundTransport.cs:199-200`; probe `tests/Antiphon.ChannelOutbound.Probe/Program.cs:103-104`, `UnifiedRecoveryProbe.cs:135-136` | any non-null (null-checked and echoed) | Test, fine |
| Evidence logging of the stamp | `tests/Antiphon.Tests/Agents/CodexBootWedgeProbeTests.cs:107`, `RunnerGrokAdapterReadyTestsPty.cs:250`, `tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs:301` | any | Test, fine |
| `RunnerBuildIdentity.Resolve()` as a constructor input (about 14 sites) | e.g. `tests/Antiphon.SessionRunner.Tests/BlockedParkWireTests.cs:86` | any | Test, fine |
| Dockerfile stamp text pin | `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs:1246-1250` | `-p:SourceRevisionId=$SOURCE_REVISION` present | unaffected |
| Land-graph admitted names (`SourceRevisionId`, `_GitHeadSha`…) | `tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs:222-234`; `tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs:33`; `tests/.../LandGraphEvaluationEnvironmentTests.cs:99,112` | name allow-list, not a value | unaffected (see FT-2) |
| Checkpoint build provenance (`buildSource=verified`) | `tools/Antiphon.Checkpoints/Execution/RowRunner.cs:140-141, 254, 293` | does **not** read the assembly stamp (no `InformationalVersion` reader in the tool beyond `--version`) | unaffected |
| `PortaExitSignalReader` / `TestSchemaCache` | `src/Antiphon.Agents.Pty/PortaExitSignalReader.cs:16,66`; `tests/.../TestSchemaCache.cs:283-289` | third-party and EF package versions, not ours | unrelated |

Builds that stamp today and must keep stamping (their binary is deployed and compared):

| Build | Where | Today | Proposed |
|---|---|---|---|
| Desktop AppHost + server (restart-apphost → dev-aspire) | `dev-aspire.ps1:158` `dotnet build` in `Antiphon.AppHost` | implicit (props Exec) | `-p:SourceRevisionId=<HEAD>`; refuse if HEAD is unreadable |
| Desktop session runner + fake gateway daemons | `scripts/run-daemon.ps1:145` (`BuildProjectDir`, AppHost `Program.cs` `AddDaemonProcess`) | implicit | `-p:SourceRevisionId=<HEAD>`; warn and build unstamped if HEAD is unreadable |
| Windows phone-home runner | `scripts/start-windows-phone-home-runner.ps1:70` | implicit | `-p:SourceRevisionId=<HEAD>`; refuse if HEAD is unreadable |
| Docker server / runner / fixture images | `Dockerfile:26`, `docker/session-runner-grok/Dockerfile:39-41`, `docker/tests/Dockerfile:42` | explicit `-p:SourceRevisionId=$SOURCE_REVISION` (`.dockerignore` excludes `.git`) | unchanged |
| NuGet pack (Messaging packages) | `.github/workflows/publish-nuget.yml:49-54` | implicit stamp plus SourceLink | `-p:EnableSourceLink=true -p:SourceRevisionId=${{ github.sha }}` |

Release scripts (`publish-release.ps1`, `release-cut.ps1`, `promote-release.ps1`, `release-candidate.ps1`)
build no binaries. They work with git SHAs and the running `/api/version`, so they are unaffected.

Builds that need **no** per-commit value:
- task worktree builds;
- checkpoint rows;
- land verification (`AgentTaskLandService.cs:2010-2016`, `dotnet build --artifacts-path <land-verify generation>`);
- nightly test builds (`scripts/lib/nightly-tests-impl.ps1:1929`, `scripts/lib/nightly-core.ps1:72`);
- `post-land-early-warning.ps1:1107`;
- the land audit tool build (`scripts/lib/land-audit-tool.ps1:92`).

After the patch, the test readers in the table accept the constant: HealthEndpointTests and C1022 are
patched, and the rest need "any".

## FT-2 land build graph fingerprint

The stamp **value** is not a fingerprint input:
- The land graph digest is computed over the tracked definition files' content
  (`docs/orchestration-loop.md` around 2570-2585).
- "Land itself only compares the definition digest; it never runs this evaluator"
  (`scripts/audit-land-verification-graph.ps1:7`).
- `git rev-parse` output never enters it. So the per-commit SHA does not move FT-2.

The **edit** to `Directory.Build.props` is a definition change, so it moves the graph once:
- `server/Application/Services/LandVerificationBuildGraph.pins.txt:1` pins the `SetSourceRevisionIdFromGit`
  target by hash. The patch removes that line. The pins are hand-committed: a bare `-Update` refuses with
  "commit source and pins before auditing" (`scripts/audit-land-verification-graph.ps1:8-11`).
- `EnableSourceLink` is not in the precheck's `AllowedProperties`
  (`tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs:214-234`: "A new name is a reviewed edit
  here"). The patch adds it.
- The Code slice must then run `scripts/audit-land-verification-graph.ps1 -Update`, which regenerates
  `LandVerificationBuildGraph.manifest.txt` and `.evaluation.txt`, and then `-Check`.
- UNKNOWN: whether the unconditional `<EnableSourceLink>false</EnableSourceLink>` element also needs an
  element pin. `-Check` will say.
- The `_GitHeadSha`, `_GitHeadShaTrimmed` and `_GitRevParseExitCode` admitted names become unused. No
  test enforces usage (they appear only in the precheck list and in the `LandNonCodeSkipTests.cs:1541-1552`
  fixture), so the patch leaves them.

## Design

The minimal change uses no new property name. The existing `SourceRevisionId` is the opt-in:

1. In `Directory.Build.props`:
   - Delete the `SetSourceRevisionIdFromGit` target.
   - Keep `<SourceRevisionId Condition="'$(SourceRevisionId)' == ''">unknown</SourceRevisionId>`. That
     element is unchanged, so its pin is unchanged.
   - Add an unconditional `<EnableSourceLink>false</EnableSourceLink>`. It is unconditional so it reads
     no property, and a command-line `-p:` still overrides it.

   Ordinary builds then carry `1.0.0+unknown` and have no `sourcelink.json`.
2. Deployed builds pass `-p:SourceRevisionId=<40-hex HEAD>`:
   - `dev-aspire.ps1` (AppHost plus server; G proves the server is reached);
   - `scripts/run-daemon.ps1` (runner and fake gateway);
   - `scripts/start-windows-phone-home-runner.ps1`;
   - the Dockerfiles (already explicit);
   - the NuGet pack workflow, which also passes `-p:EnableSourceLink=true`.
3. Tests:
   - HealthEndpointTests asserts that `/api/version` equals the in-process stamp, `40-hex|unknown`,
     plus a pure `ResolveSha` parse case.
   - C1022 asserts that runner and pty-host carry the same stamp.

Why not a new `-p:AntiphonStampRevision=true` property, as the brief suggested? A new name would need
three more edits:
- an `AllowedProperties` entry;
- an `EvaluationExpressionPolicy.ReviewedEnvironmentProperties` entry, because a conditional reads it;
- an exact element pin.

It would also keep a `git` Exec in every project's evaluation. `SourceRevisionId` is already admitted,
pinned and passed by the Dockerfiles.

Risk: an unstamped binary gets deployed.
- **Desktop server.** `/api/version` reports `unknown`. `Resolve-AppHostLoadedIdentity` returns
  `malformed` (`apphost-common.ps1:613-615`), so `$identityVerified` stays false. At the wait deadline,
  restart-apphost prints `REFUSED: server build unverified (exit 5)` and exits 5 with the lock kept
  (`restart-apphost.ps1:432-447`). **The guard still catches it, but only after the no-progress window, not
  at once.** Making it fail fast is noted below.
- **Docker runner.** `buildVersion` would be `unknown`, which `deploy-server2.ps1` rejects (`-cne $Sha`).
  But Docker builds already pass the stamp explicitly.
- **Desktop runner.** There is no hard guard: check-daemon-build is advisory. The static witness covers it.

Static guard: `SourceRevisionStampContractTests` (new) fails in either case:
- the props regains a `git`/`Exec`/`SetSourceRevisionIdFromGit`, or loses `EnableSourceLink=false`;
- any of the three deploy scripts stops validating and passing `-p:SourceRevisionId=$sourceRevision`.

`DockerStackContractTests.cs:1246-1250` already pins the Dockerfiles.

The proposed patch, `docs/investigations/2026-11-20-sha-stamp-recompiles.patch` (10 files, not
applied), changes these files:
- `Directory.Build.props`
- `dev-aspire.ps1`
- `scripts/run-daemon.ps1`
- `scripts/start-windows-phone-home-runner.ps1`
- `.github/workflows/publish-nuget.yml`
- `server/Application/Services/LandVerificationBuildGraph.pins.txt` (drops the target pin)
- `tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs` (adds `EnableSourceLink`)
- `tests/Antiphon.Tests/Application/HealthEndpointTests.cs`
- `tests/Antiphon.SessionRunner.Tests/C1022BackendLaunchTests.cs`
- new `tests/Antiphon.Tests/Infrastructure/SourceRevisionStampContractTests.cs`

It does **not** include the regenerated land-graph manifest or evaluation files. Those must come from
`-Update`, never from a hand edit. `git apply --check` is clean on `ce3670f5`.

## Proposed patch verified

In the throwaway worktree (base `ce3670f5` plus probe docs commits), the patch file was applied and
committed as `17834d3b`:
- **Build:** `tests/Antiphon.Tests` built with 0 errors (P0). That closure includes
  `Antiphon.LandGraphAudit.Core`, so the `AllowedProperties` edit compiled.
  `tests/Antiphon.SessionRunner.Tests` built with 0 errors, so the C1022 edit compiled.
- **Docs-only commit:** with no extra properties, the rebuild recompiled only the PtyHost pair, in 24.2 s
  (P). The stamped tree took 420–622 s for the same kind of commit (B/C).
- **Witness, green:** `SourceRevisionStampContractTests` on the patched tree gave total 4, succeeded 4.
- **Witness, red:** with `Directory.Build.props`, `dev-aspire.ps1`, `scripts/run-daemon.ps1` and
  `scripts/start-windows-phone-home-runner.ps1` restored to their `ce3670f5` content (`--no-build`, the
  test reads the files at run time), it gave total 4, **failed 4**. So every case can go red.
- **Witness fix:** the first witness draft text-matched `rev-parse` and tripped on the new props comment.
  The shipped version asserts on XML elements: no `Target`, no `Exec`, a single unconditional
  `EnableSourceLink=false`, and a single `SourceRevisionId` of `unknown`.

Not run:
- HealthEndpointTests needs Postgres (`AntiphonWebAppFactory`).
- C1022 and the Windows phone-home harness are Windows-only.
- The land-graph `-Update`/`-Check` and the pwsh restart harnesses were not run either.

All of these belong to the Code slice (rows below).

## Red witnesses and checkpoint rows for the Code slice

| CP | Build | Filter / command | Expect | Red at base |
|---|---|---|---|---|
| CP-1 | `tests/Antiphon.Tests -> bin-c2b3-a/` | `/*/*/SourceRevisionStampContractTests/*` | 4 executed (1 + 3 `[Arguments]`), 0 failed | yes: base props has `rev-parse`/`Exec`, scripts lack the stamp |
| CP-2 | CP-1 | `/*/*/HealthEndpointTests/*` (Integration, Postgres) | 5 executed (2 + 3 args) | the two existing methods go red on an unstamped build if the test edit is dropped |
| CP-3 | `tests/Antiphon.SessionRunner.Tests -> bin-c2b3-b/` | `/*/*/(DaemonStartupDiagnosticsTests*)\|(RunnerRestartScriptCompatibilityTests*)\|(PtyHostDeploymentGuardTests*)\|(DaemonLogRotationTests*)/*` | all green (run-daemon.ps1 consumers) | no (regression row) |
| CP-4 | CP-1 | `/*/*/(DockerStackContractTests*)\|(LogRetentionTests*)\|(LinuxServerRestartGateTests*)\|(LandGraphEvaluationEnvironmentTests*)\|(LandGraphEvaluationScriptTests*)\|(LandGraphLandRefreshTests*)\|(LandNonCodeSkipTests*)/*` | all green | no (regression row) |
| CP-5 | command | `scripts/audit-land-verification-graph.ps1 -Update`, commit, then `-Check` | exit 0, manifest/evaluation regenerated | n/a |
| CP-6 | command | `scripts/test-apphost-server-version.ps1`, `test-apphost-dead-launch.ps1`, `test-apphost-health-window.ps1`, `test-apphost-graceful-stop.ps1`, `test-apphost-lock-hints.ps1`, `test-apphost-main-worktree-guard.ps1`, `test-dev-postgres-optout.ps1` | green | no |
| CP-7 (Windows lane) | `tests/Antiphon.SessionRunner.Tests` | `/*/*/C1022BackendLaunchTests/*`; `scripts/test-windows-phone-home-runner.ps1` | green on Windows (skips on Linux, which counts as `row_failed`, so run it on the desktop lane only) | the base assertion is red on an unstamped Windows test build |
| CP-8 (acceptance measurement) | command | in a fresh worktree: build `tests/Antiphon.Tests`, make a docs-only commit, rebuild warm | at most 2 distinct `csc /out:` (the PtyHost pair) | base: 40 |

## Estimated saving per build class

| Build class | Today (measured) | After (measured or estimated) |
|---|---|---|
| Land verify, docs-only candidate (desktop, warm `land-verify` generation, whole `Antiphon.sln` with 59 projects) | 454–757 s, median 524 s (FT-0 doc) | No-op incremental floor plus the PtyHost pair. Desktop floor UNKNOWN; server2 floor for the 40-assembly closure is 19–48 s (D/E). Estimate: about 1 min, saving about 7–11 min per docs-only land that is not skipped. |
| Land verify, code candidate | full-closure recompile | Only changed projects and their dependents recompile. Saving = unchanged projects × compile share. Not measured. |
| Task worktree, first build in a reused ws-NN slot | about cold: B 622 s ≥ A 524 s | Recompiles projects whose sources differ between the previous slot HEAD and the new base. 21 s for none; up to cold when master churned widely. |
| Task worktree, rebuild after the task's own commit (WIP, red→green, report commit) | 420–622 s (B, C) | **21 s (E)** plus the changed projects. This is the largest repeated saving: it applies to every commit-then-build cycle. |
| Checkpoint tool Build row | cold by construction, ≈ 524 s (A) | **unchanged** (fresh `bin-<id>/artifacts`, deleted after green) |
| Nightly build (`C:\Antiphon\nightly\checkout`) | UNKNOWN (not measured; whether the checkout is warm between nights is UNKNOWN) | at most "unchanged projects since the last night"; likely small |
| Deploy builds (AppHost, daemons, Docker) | full closure | unchanged: they still stamp. G: AppHost closure, 10 assemblies, 133 s warm on server2 |

## Remaining uncertainties

- The desktop land-verify no-op floor was not measured, because this task ran on server2 only.
- Whether the new `<EnableSourceLink>` element needs a land-graph element pin. `-Update`/`-Check` decides.
- Nightly warmness and its build time.
- C1022 and the Windows phone-home harness were not run: they are Windows-only.

## Not done, noted

- `Antiphon.PtyHost` and `Antiphon.PtyHost.Protocol` recompile on **every** build, even with no change
  (D, E0, E: 2 assemblies, about 15–30 s). This is probably the CARD-1664 RID child build sharing their
  `obj/`. It is worth its own card.
- restart-apphost could fail fast (exit 5 at the first healthy probe that reports `unknown`) instead of
  waiting out the no-progress window.
- Checkpoint Build rows stay cold by design. A warm per-worktree checkpoint output would be a separate
  design.
