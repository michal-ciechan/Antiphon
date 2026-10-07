# CARD-1105 Compose v2.18.1 config shape

origin/master at dispatch: `d36f79f93512ef84589314b32a4fefd24e1bc4ae`.

Task 526af1e5's report and `C:\src\Antiphon\.antiphon\rolling-server2\c727912dd8274b5d` are not on this mirror. No branch or commit names that task. The earlier pushed diagnosis `489758656` (task 281b29ce) is a different refusal: `error("mount set")` for 10 rendered session mounts against an 11-mount roster, before this shape bug.

Host `docker compose config --format json` on Compose v2.18.1 omits a service secret `target` and emits top-level secret declarations as `{name,file,external:false}`. `c1008_compose_model` called `.target|startswith("/")` on the missing target, and both model filters' `allowed(["file","name"])` rejected `external`. `c1008_recycle` mapped the jq throw to `RecycleComposeMismatch`. The checked-in pin is `scripts/fixtures/c1105-compose-v218-config.json`: a v2.32.4 render of `docker-compose.server2-runner.yml` with those two shape edits, inert paths, and 11 session-runner volume and bind rows including `/run/antiphon/github-token`.

## jq and shell readers

| Site | Verdict |
|---|---|
| `c1008_compose_model` secret `.target\|startswith` | Fixed. Missing target now uses the `c1008_previous_model` guard: `/run/secrets/` plus `.source`. |
| `c1008_compose_model` secret declaration `allowed(["file","name"])` | Fixed. `external` is allowed only when `(external // false) == false`. |
| `c1008_previous_model` secret declaration `allowed(["file","name"])` | Fixed the same way. Its target guard already type-checks. |
| `c1008_previous_model` bind `source\|startswith`, tmpfs `startswith`, secret `file\|startswith` | Safe. Each `startswith` is reached only after a string type check, and `or`/`and` short-circuit. |
| `c1008_compose_model` bind `source\|startswith` and the host-path `startswith` on `$source` | Safe. A null bind source fails string equality before `startswith`. `$source` is a `--arg` string. |
| Private volume `(external // false) == false` and cache `external == true` | Safe. v2.18 omits `external` on private volumes; cache volumes are `external: true` in the Compose file on both versions. |
| Service secret `allowed(["source","target"])` | Safe. `{source}` only is a subset. This file does not set uid, gid, or mode. |
| `tmpfs == ["/run/antiphon"]` and volume `allowed` keys | Safe. The v2.32 render of this file, and the v2.18 delta above, use only `type,source,target,read_only,volume,bind`. The diagnosis did not report another extra key. |
| `c849_fixture_model` `.target` / `.external` equality | Safe. Equality does not call `startswith`. A missing target fails the select. It is not the recycle render. |
| `c849_cold_proof` inspect `Image\|test` and `Destination`/`Source` `startswith` | Safe. Those fields come from `docker inspect`, which Compose v2.18.1 does not reshape. A present mount's Destination and Source are strings. |
| `c1008_container_census` and `c1008_owned_mounts` | Safe. Census type-checks Image before `test` and allows a null tmpfs. Owned mounts compare Destination equality against topology strings the model filters emit. |

No other reader was changed.

### Checkpoints

Closed list. One isolated build, serial rows, no whole-Unit lane. UseAppHost=false is applied by the checkpoint tool on this host. Counts below are source counts before the three new methods; CP-1 and CP-3 include them.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | v218 | `tests/Antiphon.Tests -> bin-c1105-v218/` | v218-model | `/*/*/RollingProductionMountTests/C1105_V218_*` | new model proofs | 2 methods, 0 failed | 2 | 15 | true |
| CP-2 | v218 | CP-1 | v218-dry-run | `/*/*/RemoteScriptContractTests/C1105_V218_dry_run_reaches_the_generation_line*` | dry-run generation line | 1 method, 0 failed | 1 | 8 | true |
| CP-3 | v218 | CP-1 | mount-class | `/*/*/RollingProductionMountTests/*` | class regression | 8 methods, 0 failed | 8 | 10 | true |
| CP-4 | v218 | CP-1 | docs-pins | `/*/*/DockerStackDocumentationTests/*` | doc pins | 14 methods, 0 failed | 14 | 2 | true |
| CP-5 | v218 | CP-1 | compose-text-pins | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)\|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)\|(Deploy_parent_seeds_or_verifies_runner_checkout*)\|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)` | compose text pins | 5 methods, 0 failed | 5 | 4 | true |
| CP-6 | v218 | CP-1 | retired-temp-script | `/*/*/RetiredTempContainerScriptTests/*` | class regression | 13 methods, 0 failed | 13 | 10 | true |
| CP-7 | v218 | CP-1 | host-jq | `/*/*/HostJqPrerequisiteScriptTests/*` | class regression | 39 methods, 0 failed | 39 | 15 | true |
| CP-8 | v218 | CP-1 | registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | registry guard | 3 methods, 0 failed | 3 | 3 | true |
| CP-9 | v218 | CP-1 | retired-temp-host | `/*/*/RetiredTempContainerHostTests/*` | class regression | 21 methods, 0 failed | 21 | 15 | true |
| CP-10 | v218 | CP-1 | recycle-wrapper | `/*/*/RollingVolumeRecycleScriptTests/*` | class regression | 16 methods, 0 failed | 16 | 15 | true |
| CP-11 | v218 | CP-1 | remote-generation | `/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)\|(C1105_Generation_identity_refuses_mismatch*)\|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)\|(C1008_Recycle_exact_default_volumes*)\|(C1008_Recycle_dry_run_never_mutates*)` | C1008/C1105 | 5 methods, 0 failed | 5 | 15 | true |
| CP-12 | v218 | CP-1 | remote-census-long | `/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_receipt_records_disk_and_partial_failure*)\|(C1008_Recycle_refuses_references_and_unknown_census*)` | C1008 | 3 methods, 0 failed | 3 | 15 | true |
| CP-13 | v218 | CP-1 | remote-census-rest | `/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1087_Host_census_filters_and_names_cause*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_audits_work_as_1654*)` | C1008/C1087 | 7 methods, 0 failed | 7 | 12 | true |
| CP-14 | v218 | CP-1 | remote-resume | `/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)\|(C1105_Resume_identity_refusals*)\|(C1105_Resume_state_init_image_follows_the_journal*)\|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*)` | C1105 | 4 methods, 0 failed | 4 | 12 | true |

CP-1's filter is a trailing method wildcard. CP-11 through CP-14 are the Remote `C1008_` / `C1087_` / `C1105_` methods and run last. `C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle` is the compose text pin in CP-5.

PCs stay pending for method-scoped SourceLanding Mutation. No deliberate mutant is part of this checkpoint run. The Code red proof reverts the target guard and restores `allowed(["file","name"])`, runs the new methods, and restores the source before this run.
