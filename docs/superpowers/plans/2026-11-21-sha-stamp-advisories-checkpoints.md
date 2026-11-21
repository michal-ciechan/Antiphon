# SHA stamp advisories (CARD-2027 through CARD-2031)

Base commit `ce0eb1c5fbccdf8b2918643a319b88386435b761`. Task `877ebad8`.

The deploy launchers must pass a validated checkout HEAD at the build boundary. The
Windows C1022 proof must compare the launched pty-host assembly to the runner's
staged input and require known SHA stamps when validating deployment identity.
The phone-home launcher warns for an unverified existing build and still launches.

## Verification design

- R-1 / CP-1. Before the fixes, see all three new legacy rows, the no-build warning,
  and the stamp-decoy test fail in their intended assertions.
- R-2 / CP-2. Before the fix, prove the C1022 comparison accepts a stale
  same-version binary and accepts an unknown stamp in deploy mode.
- V-1 / CP-3. Run the complete stamp contract and phone-home supervision harness.
  Capture the production launcher warning with an unstamped runner DLL and verify
  that a staged executable is still invoked. The AST audit also checks the
  single-argument PowerShell forwarding on the AppHost launch path.
- V-2 / CP-4 and CP-7. Exercise the C1022 comparison against real assembly
  metadata and contents. Verify the deploy gate and SessionRunner limiter roster.
  R-3 is native Windows coverage of the three existing C1022 launch methods.
  They drive `SessionRunnerRuntime` and its production `PtyHostLauncher` using
  `ResolvedPtyHostSourceDir`. Select
  `/*/*/C1022BackendLaunchTests/(Unset_owned_host_runs_modern*)|(Configured_owned_host_runs_modern*)|(Declared_modern_overrides_inherited_inbox*)`
  at the reviewed SHA. Estimate 6 minutes. For deployment identity evidence,
  set `ANTIPHON_C1022_REQUIRE_DEPLOY_STAMP=1` and build the fixture with
  `SourceRevisionId` set to the full SHA. Native execution is pending.
- V-3 / CP-5 and CP-8. Cover the nine standing guard classes, derived impact
  index, and other assembly limiter rosters selected by the task diff. CP-9 also
  covers the documentation and script classes selected by the derived index.
- V-4 / CP-6. Evaluate the land graph, evidence diff and both nightly pins.
- V-5 / CP-9 and manual. Check the touched script syntax and ASCII guards,
  the SHA refusal before startup/build, and the two activation instructions.
  R-4 is native Windows execution of the phone-home harness's no-build row
  and an actual Windows PowerShell 5.1 parse of changed launch scripts.
  Estimate 5 minutes. Native execution is pending.
- R-5. At authorized rc activation, verify the AppHost exits 0 and
  `/api/version` reports the activating checkout SHA. Refresh the desktop
  supervisor with `-Hard` and verify the full runner `build.commitSha`.
  Desktop execution is pending. Portable argument parsing and the modeled
  phone-home launch are covered by CP-3.

The portable rows cover named classes and methods only. The native obligations
need Windows and the activation obligation needs the reviewed rc window.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-sha-adv-red-a/` | legacy-and-decoys-red | `/*/*/SourceRevisionStampContractTests/(Deployed_builds_stamp_head*)\|(PowerShell_census_rejects_stamp_decoys*)\|(Phone_home_no_build_warns_and_launches*)` | R-1 | SourceRevisionStampContractTests | 8 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-sha-adv-red-b/` | backend-identity-red | `/*/*/C1022BackendLaunchTests/(Unknown_build_is_rejected*)\|(Different_assemblies_with_the_same*)` | R-2 | C1022BackendLaunchTests | 2 | 6 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-sha-adv-a/` | stamp-and-harness | `/*/*/(SourceRevisionStampContractTests*)\|(C1744G3PhoneHomeSupervisionTests*)/*` | V-1 | SourceRevisionStampContractTests,C1744G3PhoneHomeSupervisionTests | 18 | 6 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-sha-adv-b/` | backend-identity | `/*/*/C1022BackendLaunchTests/(Unknown_build_is_rejected*)\|(Different_assemblies_with_the_same*)` | V-2 | C1022BackendLaunchTests | 2 | 4 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | S2 | CP-3 | guards-and-index | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)\|(ProcessSpawnLimitTests*)/*` | V-3 | TestClassificationPolicyTests,TestClassificationGuardTests,SlowTestRegistryMergeTests,SlowTestTripwireTests,NativeLinuxPolicyWitnessTests,MechanicalPcContractTests,TaskPlatformGuidanceTests,C1729LayeringGuardTests,TestLaneCategoryGuardTests,DerivedImpactIndexTests,ProcessSpawnLimitTests | 111 | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | S2 | n/a | land-evidence-and-pins | `env PATH="${ANTIPHON_CHECKPOINT_VERIFICATION_PATH:-$PATH}" pwsh -NoProfile -Command '& ./scripts/audit-land-verification-graph.ps1 -Check; $graphExit = $LASTEXITCODE; & ./scripts/check-evidence-diff.ps1 -BaseRef ce0eb1c5fbccdf8b2918643a319b88386435b761 -HeadRef HEAD; $evidenceExit = $LASTEXITCODE; . ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; if (-not (Test-NightlyCoreAvailable)) { throw "nightly core unavailable" }; $policy = [Antiphon.Nightly.NightlyPolicy]::Read((Get-Location).Path, ""); $lines = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest); $d = ($lines \| Select-Object -Last 1).Trim(); $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + [char]39 + "([0-9a-f]{64})" + [char]39)); if (-not $m.Success -or $d -cne $m.Groups[1].Value) { throw ("input digest drift computed=" + $d) }; Write-Output ("policyHash=" + $policy.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("graphExit=" + $graphExit + " evidenceExit=" + $evidenceExit); if ($graphExit -ne 0 -or $evidenceExit -ne 0) { exit 1 }'` | V-4 | exit 0, both pins match | n/a | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | S2 | CP-4 | runner-guards | `/*/*/(PtyHostDeploymentGuardTests*)\|(ProcessSpawnLimitTests*)/*` | V-2 | PtyHostDeploymentGuardTests,ProcessSpawnLimitTests | 5 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-sha-adv-pty/` | pty-limiter | `/*/*/ProcessSpawnLimitTests/*` | V-3 | ProcessSpawnLimitTests | 2 | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-9 | S2 | CP-3 | impacted-scripts-and-docs | `/*/*/(DockerStackDocumentationTests*)\|(LinuxServerRestartGateTests*)\|(NightlyScriptsTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(ToolchainFloorWarningTests*)/*` | V-3, V-5 | DockerStackDocumentationTests,LinuxServerRestartGateTests,NightlyScriptsTests,StandingPipelinePolicyDocumentationTests,ToolchainFloorWarningTests | 71 | 4 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |

### Cost

Estimated portable Final cost is 29 minutes before slot wait. Three isolated
test-project builds reuse their output for later filters. Bootstrap of the
checkpoint driver and regeneration of the committed impact index are separate
build or generation work. Each test row requires fresh TRX identities and counts.

[Unbound Mutation candidates](../mutations/2026-11-21-sha-stamp-advisories-candidate.md)
retain every PC and variant for SourceLanding Mutation.
