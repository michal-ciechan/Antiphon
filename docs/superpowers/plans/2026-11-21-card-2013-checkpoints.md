# CARD-2013 PtyHost incremental-build verification

Task base `e56d82553331879b133d6a2b25af9268bf8d8d7c`. The Linux
Antiphon.Tests build needs a native PtyHost executable even with
`UseAppHost=false` for the outer graph. The same unchanged build must
reuse managed compiler outputs while still staging the runnable apphost
beside its managed DLL at the root of the test output.

## Verification design

- V-1. Build Antiphon.Tests in its native Debug output with
  `UseAppHost=false` and keep the normal MSBuild file log.
- V-2. Repeat that exact build at the same committed SHA. Assert zero
  compiler invocations and check the staged executable's ELF magic,
  execute permission and adjacent managed DLL. The pre-fix repeat
  failed this compiler-count assertion at `9fd964d5677f4ca6ba4798b38c182e3337156ebf`.
  MSBuild took 29.48 seconds and compiled PtyHost and PtyHost.Protocol
  twice each into their ordinary `obj/Debug/net9.0/` paths. Compare the
  fixed repeat with that sample.
- V-3. `PtyHostDeploymentGuardTests`, all three launch contracts.
- V-4. Isolated AppHost and dev-aspire fixture scripts. They exercise
  launch, version, health, stop and worktree guards without live ports.
- R-1. The standing nine-class guard union, including policy, mechanical
  contracts, platform guidance and C1729 layering.
- R-2. `DerivedImpactIndexTests` after the driver DLL is available.
- R-3. Project-instance checks and the seven additional classes derived
  from Antiphon.Tests.csproj. Include `BuildOutputNestingTests` to
  check the resulting checkpoint output tree and its helper assets.
- R-4. Refresh graph records via the land-refresh script only and verify
  `audit-land-verification-graph.ps1 -Check`. Capture any inherited
  `SatelliteResourceLanguages` refusal.
- R-5. Recompute the nightly input digest and read the sealed nightly
  policy. Reseal only for an actual protected-input change.
- R-6. Run the evidence diff and merge-tree over the full task.
  Confirm the branch was pushed. Windows launch code is outside the
  changed Linux build target. No database or shared runtime is needed.
- R-7. Run the five remote-script contract methods selected by the
  existing `scripts/*.sh` impact row for the witness script.

The verification uses two native-output build measurements and two
isolated checkpoint builds. The whole Unit lane is left to nightly/rc.
There is no bound PC in this Code task. Any positive controls belong to
SourceLanding Mutation.

## Performance budget

The first native build may require a cold compile. The prior Linux
investigation measured the unchanged rebuild at 19–48 seconds, including
the PtyHost pair. This task records one before/after sample at each SHA,
with admission time separate. Antiphon.Tests checkpoint rows share a
single cold build. SessionRunner needs its own cold build. Test phases
and slot waits are recorded in checkpoint receipts. Estimates are
provisional and based on CARD-2013 and the November 20 measurements.
CP-7 covers the build-definition and scoped-selector invariants. Its
bound includes the two fixture-heavy classes LandNonCodeSkipTests and
ImpactedClassesSelectorTests. No class requires a full-assembly run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | incremental-warm | `sh scripts/test-ptyhost-incremental-build.sh warm` | V-1 | exit 0 | n/a | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | n/a | incremental-proof | `sh scripts/test-ptyhost-incremental-build.sh verify` | V-2 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c2013-runner/` | ptyhost-deploy | `/*/*/PtyHostDeploymentGuardTests/*` | V-3 | 0 failed | 3 | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | n/a | apphost | `pwsh -NoProfile -Command 'function Wait-Load { while ([double]::Parse(([IO.File]::ReadAllText("/proc/loadavg") -split " ")[0], [Globalization.CultureInfo]::InvariantCulture) -ge 14) { Start-Sleep -Seconds 30 } }; $names = @("test-apphost-server-version","test-apphost-health-window","test-apphost-graceful-stop","test-apphost-lock-hints","test-apphost-main-worktree-guard","test-dev-postgres-optout"); foreach ($n in $names) { Wait-Load; & pwsh -NoProfile -File ("scripts/" + $n + ".ps1"); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }; foreach ($case in @("Recovery","Launcher","Alert","Guards")) { Wait-Load; & pwsh -NoProfile -File scripts/test-apphost-dead-launch.ps1 -Case $case; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }'` | V-4 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | `tests/Antiphon.Tests -> bin-c2013-guards/` | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | 0 failed | 84 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | CP-5 | impact-index | `/*/*/DerivedImpactIndexTests/*` | R-2 | 0 failed | 27 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | CP-5 | project-definition | `/*/*/(BuildOutputNestingTests*)\|(CheckpointToolProjectContractTests*)\|(ImpactedClassesSelectorTests*)\|(LandGraphAuditProjectInstanceTests*)\|(LandNonCodeSkipTests*)\|(NightlyPolicyTests*)\|(PlanCoverageBindingTests*)\|(ReleaseImpactRuntimeTests*)\|(ReleaseImpactScriptTests*)/*` | R-3 | 0 failed and each named class executes | 130 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | all | n/a | land-audit | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-9 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; if (-not (Test-NightlyCoreAvailable)) { throw "nightly core unavailable" }; $policy = [Antiphon.Nightly.NightlyPolicy]::Read((Get-Location).Path, ""); $lines = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; $d = $lines[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success -or $d -cne $m.Groups[1].Value) { throw ("input digest drift computed=" + $d) }; Write-Output ("policyHash=" + $policy.Hash); Write-Output ("inputDigest=" + $d)'` | R-5 | exit 0 | n/a | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-10 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef e56d82553331879b133d6a2b25af9268bf8d8d7c -HeadRef HEAD` | R-6 | exit 0 | n/a | 1 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-11 | all | CP-5 | shell-path-impact | `/*/*/RemoteScriptContractTests/(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | R-7 | 0 failed and each named method executes | 5 | 1 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
