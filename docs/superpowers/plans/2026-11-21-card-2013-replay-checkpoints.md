# CARD-2013 PtyHost incremental-build replay

Replay base `ce0eb1c5fbccdf8b2918643a319b88386435b761`.
Original Code source `f20b770504eb787d47c309d5826af62c586a043e`.
The [original plan](2026-11-21-card-2013-checkpoints.md) records the first
witness and its timings.

## Verification design

- V-1. Warm the native Debug build of Antiphon.Tests with `UseAppHost=false`.
  Capture the normal MSBuild log. Use the same build arguments and source for
  the following sample.
- V-2. On the replayed witness commit, the second unchanged build must expose
  the duplicated compiler invocations. At the fully replayed tip the same
  build must invoke the compiler zero times. Both samples must reach the
  actual Linux `CopyOwnedPtyHostAppHost` target and stage an executable ELF
  alongside the managed DLL at the test output root. The target is gated off
  on Windows. Its producer and protocol dependency now get an SDK artifacts
  root specific to the inner apphost restore and build. No application startup
  files or runtime factories change.
- V-3. Run the three PtyHost deployment guards.
- V-4. Run the isolated AppHost and dev-aspire fixtures, including all 131
  server-version assertions and the selected health, stop, lock, worktree,
  opt-out and dead-launch cases. The fixtures use private state and simulated
  system dependencies.
- R-1. Run the nine-class policy, mechanical, platform and layering guard union.
- R-2. Build the derived-index driver in its native output. Regenerate the
  index twice before final tests. The first run can update stale inherited
  entries and the second must reproduce its bytes. Commit the updated index
  last and run `DerivedImpactIndexTests`.
- R-3. Verify the project definition and artifact layout with
  `BuildOutputNestingTests` and the eight classes selected by the project
  change. The isolated output must stage its helpers without nested copies.
- R-4. Refresh graph records in one commit through
  `scripts/land-graph-land-refresh.ps1` from the revision matching the
  recorded input fingerprint. Run the real graph audit with `-Check`.
  Require exit 0 with passage through the differential canary.
- R-5. Recompute the live-lane digest and verify the sealed nightly policy.
  Reseal only if the replay changes a protected input.
- R-6. Check for prohibited evidence over the full replay history and check
  merge-tree against fetched master.
- R-7. Run the five exact methods selected for the new shell path by the
  `remote-scripts` impact row.
- R-8. Run the additional graph, release-impact and regenerated-index
  classes selected by the changed records. The landing fixtures own their Git
  remotes and PostgreSQL databases.

The warm and second-build rows are the native Linux witness. The earlier
failure is a regression witness, and there are no assigned positive-control
variants in this plan. Nightly and rc own the complete Unit lane.

## Performance budget

The earlier second-build samples were 29.48 s before the fix and 23.08 s
after it. Record single-sample MSBuild times, compiler counts and slot waits
for the replay. One cold SessionRunner build and one cold Antiphon.Tests
build serve the named class rows. The controlled home and private AppHost
fixture state close launch checks without live services. The shared
assembly-lifetime test database is used only by selected fixture consumers.
Row estimates are provisional and keep setup, physical build, test execution
and admission waits visible in the corresponding logs and receipts. The
cases bound the affected build layout, graph records, selectors and deployment
contracts by class or method. No full assembly is needed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | incremental-warm | `sh scripts/test-ptyhost-incremental-build.sh warm` | V-1 | exit 0 | n/a | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | n/a | incremental-proof | `sh scripts/test-ptyhost-incremental-build.sh verify` | V-2 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c2013-replay-runner/` | ptyhost-deploy | `/*/*/PtyHostDeploymentGuardTests/*` | V-3 | 0 failed | 3 | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | n/a | apphost | `pwsh -NoProfile -Command 'function Wait-Load { while ([double]::Parse(([IO.File]::ReadAllText("/proc/loadavg") -split " ")[0], [Globalization.CultureInfo]::InvariantCulture) -ge 14) { Start-Sleep -Seconds 30 } }; $names = @("test-apphost-server-version","test-apphost-health-window","test-apphost-graceful-stop","test-apphost-lock-hints","test-apphost-main-worktree-guard","test-dev-postgres-optout"); foreach ($n in $names) { Wait-Load; & pwsh -NoProfile -File ("scripts/" + $n + ".ps1"); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }; foreach ($case in @("Recovery","Launcher","Alert","Guards")) { Wait-Load; & pwsh -NoProfile -File scripts/test-apphost-dead-launch.ps1 -Case $case; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }'` | V-4 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | `tests/Antiphon.Tests -> bin-c2013-replay-tests/` | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | 0 failed | 84 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | CP-5 | impact-index | `/*/*/DerivedImpactIndexTests/*` | R-2 | 0 failed | 27 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | CP-5 | project-definition | `/*/*/(BuildOutputNestingTests*)\|(CheckpointToolProjectContractTests*)\|(ImpactedClassesSelectorTests*)\|(LandGraphAuditProjectInstanceTests*)\|(LandNonCodeSkipTests*)\|(NightlyPolicyTests*)\|(PlanCoverageBindingTests*)\|(ReleaseImpactRuntimeTests*)\|(ReleaseImpactScriptTests*)/*` | R-3 | 0 failed and each named class executes | 130 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | all | n/a | land-audit | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-9 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-core.ps1; if (-not (Test-NightlyCoreAvailable)) { throw "nightly core unavailable" }; $policy = [Antiphon.Nightly.NightlyPolicy]::Read((Get-Location).Path, ""); $lines = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; $d = $lines[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success -or $d -cne $m.Groups[1].Value) { throw ("input digest drift computed=" + $d) }; Write-Output ("policyHash=" + $policy.Hash); Write-Output ("inputDigest=" + $d)'` | R-5 | exit 0 | n/a | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-10 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef ce0eb1c5fbccdf8b2918643a319b88386435b761 -HeadRef HEAD` | R-6 | exit 0 | n/a | 1 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-11 | all | CP-5 | shell-path-impact | `/*/*/RemoteScriptContractTests/(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | R-7 | 0 failed and each named method executes | 5 | 1 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-12 | all | CP-5 | generated-record-impact | `/*/*/(LandGeneratedArtifactTests*)\|(LandGraphEvaluationTests*)\|(LandGraphLandRefreshTests*)\|(ReleaseImpactEnvelopeTests*)\|(ReleaseImpactEvaluationTests*)/*` | R-8 | zero failed and each named class executes | 5 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
