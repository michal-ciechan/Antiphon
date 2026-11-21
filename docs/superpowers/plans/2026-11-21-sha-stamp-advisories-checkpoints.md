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
- V-1 / CP-3. Run the complete stamp contract and existing phone-home script harness.
- V-2 / CP-4. Run the two portable C1022 witnesses and the deployment guard on Linux.
  R-3 is the three native C1022 launch methods on Windows. Use
  `/*/*/C1022BackendLaunchTests/(Unset_owned_host_runs_modern*)|(Configured_owned_host_runs_modern*)|(Declared_modern_overrides_inherited_inbox*)`
  on the exact reviewed source. Estimate 6 minutes. Execution is pending on Windows.
- V-3 / CP-5. Run the nine standing guard classes and the derived impact index class.
- V-4 / CP-6. Check the land graph, evidence diff and nightly pins.
- V-5. Inspect the rollout documentation and launcher syntax. The first desktop
  runner restart on the activating checkout requires `-Hard` and SHA confirmation.
  R-4 is native execution of the phone-home harness's no-build row and validation
  of Windows PowerShell 5.1 syntax. Estimate 5 minutes. Execution is pending on Windows.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-sha-adv-red-a/` | legacy-and-decoys-red | `/*/*/SourceRevisionStampContractTests/(Deployed_builds_stamp_head*)\|(PowerShell_census_rejects_stamp_decoys*)\|(Phone_home_no_build_warns_and_launches*)` | R-1 | SourceRevisionStampContractTests | 8 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-sha-adv-red-b/` | backend-identity-red | `/*/*/C1022BackendLaunchTests/(Unknown_build_is_rejected*)\|(Different_assemblies_with_the_same*)` | R-2 | C1022BackendLaunchTests | 2 | 6 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-sha-adv-a/` | stamp-and-harness | `/*/*/(SourceRevisionStampContractTests*)\|(C1744G3PhoneHomeSupervisionTests*)/*` | V-1 | SourceRevisionStampContractTests,C1744G3PhoneHomeSupervisionTests | 21 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-sha-adv-b/` | backend-identity-and-deployment-guard | `/*/*/(C1022BackendLaunchTests*)\|(PtyHostDeploymentGuardTests*)/*` | V-2 | C1022BackendLaunchTests,PtyHostDeploymentGuardTests | 5 | 7 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | S2 | CP-3 | guards-and-index | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | V-3 | TestClassificationPolicyTests,TestClassificationGuardTests,SlowTestRegistryMergeTests,SlowTestTripwireTests,NativeLinuxPolicyWitnessTests,MechanicalPcContractTests,TaskPlatformGuidanceTests,C1729LayeringGuardTests,TestLaneCategoryGuardTests,DerivedImpactIndexTests | 105 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | S2 | n/a | land-evidence-and-pins | `pwsh -NoProfile -Command '& ./scripts/audit-land-verification-graph.ps1 -Check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; & ./scripts/check-evidence-diff.ps1 -BaseRef ce0eb1c5fbccdf8b2918643a319b88386435b761 -HeadRef HEAD; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; . ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; if (-not (Test-NightlyCoreAvailable)) { throw "nightly core unavailable" }; $policy = [Antiphon.Nightly.NightlyPolicy]::Read((Get-Location).Path, ""); $lines = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest); $d = ($lines \| Select-Object -Last 1).Trim(); $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + [char]39 + "([0-9a-f]{64})" + [char]39)); if (-not $m.Success -or $d -cne $m.Groups[1].Value) { throw ("input digest drift computed=" + $d) }; Write-Output ("policyHash=" + $policy.Hash); Write-Output ("inputDigest=" + $d)'` | V-4 | exit 0, both pins match | n/a | 7 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |

### Cost

Estimated portable Final cost is 32 minutes before slot wait. Two isolated
project outputs are reused where possible. The checkpoint tool bootstrap is
unlisted if a build is needed. The derived-impact driver bootstrap and two
regeneration passes are unlisted generation work.

PC-1 has comment, executable quoted command, and dead splat variants. PC-2 removes
the loaded host hash comparison or the deploy SHA guard. PC-3 removes the
phone-home warning. Method-scoped mutation is pending for SourceLanding Mutation.
