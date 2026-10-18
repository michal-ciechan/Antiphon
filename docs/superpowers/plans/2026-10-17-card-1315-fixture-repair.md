# CARD-1315 fixture repair

Code owner: d83a15d5. Branch: feat/card-task-d83a15d5.
Base: 40bb77c9ca355e2039b02d6864cc18c92e1ec9a4 (CARD-1315 production helper already committed).

## Scope and repair

Review 83dbae9b: production `c849_fixture_resource_state` is sound. Four existing
RemoteScriptContractTests go red because two fixtures were not aligned with it.
This repair edits fixtures only.

- D1. `scripts/fixtures/c913-seed-contract.sh` absent `docker volume inspect` prints
  `[]` on stdout and `Error response from daemon: get <name>: no such volume` on
  stderr, then returns 1. Modes fixture-seed and volume-recovery.
- D2. `scripts/fixtures/c973-marker-reader.sh` extracts `c849_fixture_resource_state`
  for `case_verify_runner_caches_retired`.
- Witness. `scripts/fixtures/c1315-fail-closed.sh` mode `modern-volume-absent` feeds
  that exact daemon text, including stdout `[]`, and requires volume state 1 and
  network state 2. `CacheFixtureCompletionTests.Modern_daemon_missing_volume_text_is_positive_absence`
  runs it.

Do not change `scripts/c590-remote.sh`. Do not fix
`C1192_Npm_seed_install_and_refill_use_work_volume` (`refill-volume-source=0`),
inherited at 5e5d5cc728949420647d15a932766e70755a6f01.

## Verification design

- V-1: the three C1192 fixture modes and `C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers`
  for `case_verify_runner_caches_retired` pass.
- V-2: `Modern_daemon_missing_volume_text_is_positive_absence` passes. Removing the
  modern arm from a sourced copy of `c849_fixture_resource_state` makes the same
  text return 2; that local copy is not a checkpoint.
- R-1: the other named RemoteScriptContractTests methods in CP-2 stay green.
  `C1192_Npm_seed_install_and_refill_use_work_volume` stays red with
  `refill-volume-source=0`, the same failure as at 5e5d5cc7.

No new PC. Parent production controls stay pending for SourceLanding Mutation.
Candidate directory: `docs/superpowers/mutations/`. This repair does not discharge one.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | repair | `tests/Antiphon.Tests -> bin-c1315-completion/` | fixture-completion | `/*/*/CacheFixtureCompletionTests/*` | V-2 | 19 executed, 0 failed/skipped | 19 | 10 |
| CP-2 | repair | `tests/Antiphon.Tests -> bin-c1315-remote/` | fixture-remote | `/*/*/RemoteScriptContractTests/(C1192_Fixture_has_no_host_payload_tree_or_payload_bind*)\|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*)\|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest*)\|(C1192_Workspace_registration_is_absent_only_and_survives_prepare*)\|(C1192_Race_programs_and_feed_share_the_work_volume*)\|(C1192_Npm_seed_install_and_refill_use_work_volume*)\|(C1192_Apphost_mask_is_an_empty_exact_volume_mount*)\|(C1208_Deadline_fixture_keeps_required_logs_and_omits_unrelated_setup*)\|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*)` | V-1, R-1 | 14 executed, 13 passed, 1 inherited failure refill-volume-source=0, 0 skipped | 14 | 15 |

### Cost

Twenty-five estimated minutes plus slot wait. One unlisted bash proof shows the
witness returns unknown when the modern arm is deleted from a copy of the helper.
No repeated green proof. CP-2's inherited npm failure is not a repair row.
