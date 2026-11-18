# CARD-1984 round 2: ETXTBSY fix on the wide lane (plan amendment)

Date: 2026-11-18. Task `632bba95` (Code, Opus). Review round 1: `0425a1cc`,
`docs/superpowers/reviews/2026-11-17-card-1984-s1-s2-review.md` at `de2e3e2a3edd8b49b47605a1c6914b4c776defe8`.
Reviewed tip: `e0fd69f6bb36c79698f734330b02d775475af2d7`. Round 2 is the diff from that SHA.

This amendment carries the round-2 decision note and checkpoint rows. They are not in
`2026-11-17-card-1984-spawn-lane-plan.md` because `master` added that plan in its own commits. A branch
edit of the file would make `git merge-tree` against `origin/master` report an add/add conflict.

## Decision

- **D-11 (2026-11-18, review B-1).** No file written in the test process is exec'd on the wide lane.
  - **Why.** A concurrent `Process.Start` forks a copy of each open write handle, and the copy lives until that
    child's exec. Linux then refuses an exec of the file with ETXTBSY.
  - **Rule.** Wide-lane methods create executables with `FixtureExecutable`, which uses `/usr/bin/install` in a
    child.
  - **Witness.** W-10 (`Wide_lane_methods_write_no_executable_in_process`) holds the rule.

## Round-2 witnesses

| W | Test | Red at `aed5c821` because | Green at the fix because |
|---|---|---|---|
| W-10 | `Wide_lane_methods_write_no_executable_in_process`. Controls: `In_process_executable_write_is_a_finding` (6 rows) and `Out_of_process_or_interpreted_script_is_not_a_finding` (3 rows: helper then direct start, bash interpreter, helper plus a data write) | 10 bodies write an executable in process. Examples: `HostJqFixture.Identity`, which caused B-1; `WriteTool`; `Existing`; `HomeShadow`; `C1008HostFixture.Run`; `InstallPreviewHook`; the C1099 shim | every executable is made by `FixtureExecutable` |
| W-11 | `Wide_lane_methods_reach_no_checkout_writer`, control `Checkout_writer_reach_is_a_finding` | `C1025_Preflight_precedes_every_phase` and `C1025_Preflight_is_fresh_for_each_entry` reach `C1008WrapperFixture`, which runs the real `deploy-server2.ps1 -Rolling` from the checkout and writes `.antiphon/rolling-server2` there | both are back on `ProcessSpawnLimit` and on the D-5 keep list |

The W-10 rule is a compiled-code walk. A walked body is a finding when both of these hold:

- It calls an in-process write: `File.Write*`, `Append*`, `Create*`, `OpenWrite`, `Copy` or `Replace`, a
  `FileInfo` write, or a `FileStream`/`StreamWriter` constructor.
- It makes a file executable in one of these ways:
  - in the same body, it sets a unix mode, names `chmod`, or calls a helper whose own body sets a mode;
  - a `"#!"` literal or a string-returning `"#!"` builder reaches the write before any `FixtureExecutable` call.

It cannot see one case: an in-process overwrite of a file that the helper already made executable. The
re-census found one such case (`C1025_Check_rejects_canonical_leaf_symlink`), and it now uses the helper. ETXTBSY
cannot be reproduced deterministically in-process. Holding the forked copy would need a child that never execs,
and .NET has no safe way to fork without exec. A scratch probe (not committed) measured it instead: 8 writers,
8 spawning threads, 32,000 execs per mode.

| Mode | ETXTBSY |
|---|---|
| direct exec | 74 |
| through `bash -c` | 1 |
| `bash <file>` | 0 |
| `install` then direct exec | 0 |

## Checkpoints

One `--serial` run for the R2 group. No `-Runner`/`-Platform`. Export `ANTIPHON_LINUX_COMPILER_POLICY=0`.
The stress runs are not rows. They are plain leased `dotnet run --no-build` runs, with
`ANTIPHON_TEST_SCRIPT_MODEL_SPAWN_LIMIT=6`, `--timeout 570s` and a `dmesg` segfault count after each batch:

- HostJq alone, 20 times;
- the CP-24 selection, 10 times.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-21 | R2 | tests/Antiphon.Tests -> bin-c1984-r2/ | lane-witness | `/*/*/ScriptModelSpawnLaneTests/*` | W-1..W-5, W-10, W-11 and their controls | every case, 0 failed/skipped | 36 | 15 | true |
| CP-22 | R2 | CP-21 | spawn-rosters | `/*/*/ProcessSpawnLimitTests/*` | W-6 rosters with two HostJq methods strict | 0 failed | 40 | 3 | true |
| CP-23 | R2 | CP-21 | hostjq-wide | `/*/*/HostJqPrerequisiteScriptTests/*` | B-1 fix; 38 wide and 2 strict methods | 40 cases, 0 failed | 40 | 6 | true |
| CP-24 | R2 | CP-21 | rolling-widened | `/*/*/(RollingVolumeRecycleScriptTests*)\|(RollingProductionMountTests*)\|(RollingVolumeRecycleCensusChurnTests*)/(C1008_Option_manifest_is_strict*)\|(C1008_Documentation_and_transport_pins_match*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt*)\|(C1594_Cross_sha_cleanup_image_binds_the_retired_temp*)\|(C1594_Older_retirement_receipt_is_ignored*)\|(C1594_Disagreeing_cross_sha_images_refuse*)\|(C1594_Antiphon_evidence_gitdir*)\|(C1008_Preview_helper_removal_is_owned*)\|(C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands*)\|(C1371_Post_removal_count_drift_does_not_abort*)\|(C994_Compose_model_preserves_service_specific_mounts*)\|(C994_Named_volume_identity_and_topology_are_exact*)\|(C994_Bind_sources_are_canonical_and_typed*)\|(C994_Secrets_match_read_only_files*)\|(C994_Tmpfs_configuration_survives_exit*)\|(C1105_Previous_generation_roster_is_derived_from_its_compose*)\|(C1105_V218_compose_model_accepts_the_host_shape*)\|(C1105_V218_previous_model_accepts_the_host_shape*)\|(C1105_Relative_secret_target_is_refused*)\|(C1105_Relative_secret_target_equal_to_its_name_is_accepted*)\|(C1105_Absolute_secret_target_keeps_the_base_rule*)\|(C1105_Malformed_secret_target_is_refused*)\|(C1099_Fixture_census_skips_a_vanished_foreign_container*)` | S2 widened methods with the fixture change (fake docker, preview hook, C1099 shim) | 63 cases, 0 failed | 63 | 6 | true |
| CP-25 | R2 | CP-21 | c1008-host-consumers | `/*/*/(C0824BranchDeletionRecycleAuditTests*)\|(RetiredTempContainerScriptTests*)\|(HostJqFixtureClosureTests*)/*` | strict-lane users of the changed `C1008HostFixture.Run`, plus the HostJq fixture closure guard | 0 failed | 19 | 6 | true |
| CP-26 | R2 | CP-21 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | the brief's 11-class guard union; C1729 VIOLATIONS 0 LISTED 162 | 0 failed | 100 | 8 | true |
| CP-27 | R2 | CP-21 | selector-timing | `/*/*/(ImpactedClassesSelectorTests*)\|(TestTimingOperationLabelTests*)/*` | selector and timing-label guards | 0 failed | 10 | 5 | true |

Every OR operand names exactly one class in `tests/` (checked with `grep -rhoE "class <Name>[A-Za-z0-9_]*"` on 2026-11-18).
The CP-24 method operands are the 24 wide-lane method names in those three classes, each with the trailing `*` the tool requires; each is the prefix of exactly one method in the three files (checked 2026-11-18). `C1594_Antiphon_evidence_gitdir`
contributes its `[Arguments]` cases.

The remaining users of `C1008HostFixture.Run` stay with the nightly Unit lane under the round's Final profile:
`RemoteScriptContractTests`, the four `VolumeRetirement*` classes, `RetiredTempContainerHostTests` and `C1364BoundLandWaitProofTests`.
The change there is mechanical: the same fake-CLI bytes and mode 0755, now written by a child.
