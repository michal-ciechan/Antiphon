# Close exemption advisories

Task `bc823630`, CARD-2040 through CARD-2042. Task base `ce0eb1c5fbccdf8b2918643a319b88386435b761`.

## Verification design

- V-1 exercises the production deploy-time `c1609_temp_retirement_clearable` function and its retirement loader on Linux. The shell fixture uses a private `SERVER2_ROOT` and substitutes an absent volume census. Six receipt-less cases cover acceptance, matching live owner, Ready already published, close pending, symlinked lifecycle, and invalid operation directory name. The Ready and pending cases reach the shared exemption predicate.
- V-2 exercises the PowerShell wrapper with fake `ssh`. Four unrecognized reasons must surface exactly `JournalUnknown`. Existing witnesses preserve `Daemon`, a named retained temp volume, empty stderr, punctuation rejection, and successful Close output.
- V-3 reviews both documentation passages against `c1609_phase_begin`, `c1609_receiptless_preview_exempt`, and the owner start-ticks check.
- R-1 runs the two retirement fixture classes and the five deploy-order methods in `RemoteScriptContractTests`.
- R-2 runs the nine-class guard union and the two Linux-only census/policy methods.
- R-3 runs `DerivedImpactIndexTests` and verifies regeneration stability. If regeneration changes the index, commit the index last.
- R-4 checks the full task evidence diff, the land verification graph, and a merge-tree against fetched `origin/master`.
- R-5 recomputes and checks the live-lane input digest.

One isolated build per committed test group. Final reuses its build for each exact selection. No full assembly is needed. The invariant is that deployment refuses uncleared retirement state and Close surfaces only known reasons. Each affected test class is bounded below. Linux supplies `/proc` start ticks, file custody and a fake executable `ssh`; the same class remains covered by its existing Windows exclusion. The nightly and rc pipelines own the Unit lane.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | red | `tests/Antiphon.Tests -> bin-close-advisories-red/` | red-witness | `/*/*/VolumeRetirementCloseExemptionTests/(Deploy_clearable_checks_receiptless_previews*)\|(Close_wrapper_redacts_unrecognized_remote_refusals*)` | V-1, V-2 | six clearable cases pass and four wrapper cases fail on the committed old wrapper | 10 | 10 | true |
| CP-2 | final | `tests/Antiphon.Tests -> bin-close-advisories-final/` | retirement | `/*/*/(VolumeRetirementCloseExemptionTests*)\|(VolumeRetirementWrapperTests*)/*` | V-1, V-2, R-1 | all selected cases pass | 22 | 12 | true |
| CP-3 | final | CP-2 | remote-order | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | R-1 | all five methods pass | 5 | 1 | true |
| CP-4 | final | CP-2 | repository-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | all nine classes pass | 78 | 2 | true |
| CP-5 | final | CP-2 | linux-census | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | R-2 | both methods pass | 2 | 2 | true |
| CP-6 | final | CP-2 | derived-impact | `/*/*/DerivedImpactIndexTests/*` | R-3 | all selected cases pass | 27 | 3 | true |
| CP-7 | final | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 2 | true |
| CP-8 | final | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef ce0eb1c5fbccdf8b2918643a319b88386435b761 -HeadRef HEAD` | R-4 | exit 0 | n/a | 1 | true |
| CP-9 | final | n/a | pin-digest | `pwsh -NoProfile -Command '$digest = @(& ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $pin = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), "(?m)^\x24script:PinnedInputDigest = \x27([0-9a-f]{64})\x27$").Groups[1].Value; if ($digest -cne $pin -or $pin.Length -ne 64) { throw "live-lane input digest differs from pin" }; Write-Output $digest'` | R-5 | recomputed digest matches the pin | n/a | 1 | true |

### Cost

The provisional Code floor is 34 minutes (10 for committed red and 24 for Final), plus slot admission, index preparation and manual review. The native driver build uses a separate slot before index regeneration because the test project does not build the driver. The estimates use the prior Close Review's shared build and targeted rows as the baseline.
