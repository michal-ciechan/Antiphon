# Close receipt-less preview exemption

Code task `864fbc83`. Branch `feat/card-task-864fbc83`. Base `588747a803943477b465f3926858c6ad5d305629`. No master merge. Nothing was run on server2 and no journal was edited.

## Red

Corrected fixture, old Close, commit source `88200abf4ea3d9eb2e07295d14793087e88719b9` before that commit was created (the same test bytes). `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` then `dotnet run --no-build` through the build slot. Load 1.63. `ANTIPHON_LINUX_COMPILER_POLICY=0`. `TUNIT_MAX_PARALLEL_TESTS=8`. `--timeout 570s`. Filter `/*/*/VolumeRetirementCloseExemptionTests/*`. Test lease `31550e78-d5b6-4092-b4b7-cfa04f35d71f`, waited 0s, held 12s. Exit 2.

```
total: 5
failed: 2
succeeded: 3
skipped: 0
```

`Close_exempts_receiptless_never_ready_previews` saw `SETUP_OK=1`, `CLOSE_EXIT=2`, `CLOSURE=absent`, `SIBLINGS_UNTOUCHED=1`. `Close_wrapper_surfaces_remote_refusal_token` saw `TempRetirementCloseRefused: JournalUnknown` for a remote `TempRetirementCloseRefused: Daemon`. The three refusal cases passed.

An earlier run at `b79015aa10332ed9fe180f0418a84bc979f22673` is not the witness: one sibling id had 31 hex digits and failed the directory-name check before the receipt scan.

## Verification design

- V-1: Close of a consumed PartialRefused receipt (3 intents, 3 effects, owner null) with two receipt-less never-Ready siblings and all four volumes absent writes `closure.json` with removed-observed 3 and unattributed-absence 1, and leaves the siblings untouched.
- V-2: the same fixture still refuses when one sibling has `readyPublished=true`, a live owner, or `close-pending.json`.
- V-3: `Invoke-Server2Bash` returns the remote `TempRetirementCloseRefused` token, keeps `JournalUnknown` for an empty refusal and a secret-shaped token, and does not print the secret.
- V-4: `VolumeRetirementWrapperTests` still accepts a clearable journal and still reports `TempRetirementJournalUnknown` for a weird receipt.
- V-5: the five `remote-scripts` contract methods still match the deploy-temp order.
- R-1: `ReleaseGatePlatformTests` `LinuxOnlyWindowsExclusionPolicy` and `C1686_LinuxOnlyCensus` agree on the new class row.
- R-2: nine-class guard union, 0 failed. `WindowsUnitPlatformAdmissionTests` is not in this row. Min 78 is the documented floor.
- R-3: `scripts/audit-land-verification-graph.ps1 -Check` exits 0. A bare `-Update` is not used.
- R-4: `scripts/check-evidence-diff.ps1` over base..HEAD exits 0.
- R-5: derived-impact index, when it changes, is its own last commit. The driver is built first. The script runs twice: changed, then unchanged.
- R-6: `tests/test-execution-policy.json` changed, so recompute `-PrintInputDigest` and reseal `PinnedInputDigest` only for that change. `LegacyExclusionDigest` stays.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | fix | `tests/Antiphon.Tests -> bin-close-exempt/` | close-exempt | `/*/*/VolumeRetirementCloseExemptionTests/*` | V-1, V-2, V-3 | all listed, 0 failed | 5 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0;TUNIT_MAX_PARALLEL_TESTS=8` |
| CP-2 | fix | CP-1 | retirement-wrapper | `/*/*/VolumeRetirementWrapperTests/*` | V-4 | all listed, 0 failed | 7 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0;TUNIT_MAX_PARALLEL_TESTS=8` |
| CP-3 | fix | CP-1 | remote-scripts | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | V-5 | all listed, 0 failed | 5 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0;TUNIT_MAX_PARALLEL_TESTS=8` |
| CP-4 | fix | CP-1 | linux-only-policy | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | R-1 | all listed, 0 failed | 2 | 6 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0;TUNIT_MAX_PARALLEL_TESTS=8` |
| CP-5 | fix | CP-1 | linux-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | all listed, 0 failed | 78 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0;TUNIT_MAX_PARALLEL_TESTS=8` |
| CP-6 | fix | CP-1 | derived-impact | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-5 | all listed, 0 failed | 27 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0;TUNIT_MAX_PARALLEL_TESTS=8` |
| CP-7 | fix | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-3 | exit 0 | n/a | 3 | true | n/a |
| CP-8 | fix | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 588747a803943477b465f3926858c6ad5d305629 -HeadRef HEAD` | R-4 | exit 0 | n/a | 3 | true | n/a |
