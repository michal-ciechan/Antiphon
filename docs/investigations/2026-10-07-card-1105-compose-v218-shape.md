# CARD-1105 Compose v2.18.1 config shape

origin/master at dispatch: `d36f79f93512ef84589314b32a4fefd24e1bc4ae`.

Task 526af1e5's report and `C:\src\Antiphon\.antiphon\rolling-server2\c727912dd8274b5d` are not on this mirror. No branch or commit names that task. The earlier pushed diagnosis `489758656` (task 281b29ce) is a different refusal: `error("mount set")` for 10 rendered session mounts against an 11-mount roster, before this shape bug.

Host `docker compose config --format json` on Compose v2.18.1 omits a service secret `target` and emits top-level secret declarations as `{name,file,external:false}`. `c1008_compose_model` called `.target|startswith("/")` on the missing target, and both model filters' `allowed(["file","name"])` rejected `external`. `c1008_recycle` mapped the jq throw to `RecycleComposeMismatch`. The checked-in pin is `scripts/fixtures/c1105-compose-v218-config.json`: a v2.32.4 render of `docker-compose.server2-runner.yml` with those two shape edits, inert paths, and 11 session-runner volume and bind rows including `/run/antiphon/github-token`.

## jq and shell readers

| Site | Verdict |
|---|---|
| `c1008_compose_model` and `c1008_previous_model` `secret_dest` | Fixed. An absent `target` defaults to the source name. An explicit relative target is kept and mounted at `/run/secrets/<target>`. Explicit null, empty, and any other non-string are `error("secret")`. The roster then requires `/run/secrets/<source>`, so `target: elsewhere` is `RecycleComposeMismatch` or `RecycleGenerationUnknown` before removal. |
| `c1008_compose_model` secret declaration `allowed(["file","name","external"])` | Fixed. `external` is allowed only when `(external // false) == false`. |
| `c1008_previous_model` secret declaration `allowed(["file","name","external"])` | Fixed the same way. It uses the same `secret_dest` as the compose model. |
| `c1008_previous_model` bind `source\|startswith`, tmpfs `startswith`, secret `file\|startswith` | Safe. Each `startswith` is reached only after a string type check, and `or`/`and` short-circuit. |
| `c1008_compose_model` bind `source\|startswith` and the host-path `startswith` on `$source` | Safe. A null bind source fails string equality before `startswith`. `$source` is a `--arg` string. |
| Private volume `(external // false) == false` and cache `external == true` | Safe. v2.18 omits `external` on private volumes; cache volumes are `external: true` in the Compose file on both versions. |
| Service secret `allowed(["source","target"])` | Safe. `{source}` only is a subset. This file does not set uid, gid, or mode. |
| `tmpfs == ["/run/antiphon"]` and volume `allowed` keys | Safe. The v2.32 render of this file, and the v2.18 delta above, use only `type,source,target,read_only,volume,bind`. The diagnosis did not report another extra key. |
| `c849_fixture_model` `.target` / `.external` equality | Safe. Equality does not call `startswith`. A missing target fails the select. It is not the recycle render. |
| `c849_cold_proof` inspect `Image\|test` and `Destination`/`Source` `startswith` | Safe. Those fields come from `docker inspect`, which Compose v2.18.1 does not reshape. A present mount's Destination and Source are strings. |
| `c1008_container_census` and `c1008_owned_mounts` | Safe. Census type-checks Image before `test` and allows a null tmpfs. Owned mounts compare Destination equality against topology strings the model filters emit. |

No other reader was changed. The repair does not copy the old non-absolute fallback that replaced every relative target with `.source`.

### Default audit

jq `//` replaces null and false only. An explicit JSON null is therefore not a missing key. `secret_dest` uses `has("target")` so an absent key defaults and a present null refuses. Every other `//` and `else` in the two model filters, the census, and the owned-mount inspect filter was re-read. None substitutes a different destination for a present value.

| Site | Verdict |
|---|---|
| `($model.configs // {})`, `($svc.configs // [])`, `($svc.secrets // [])`, `($svc.tmpfs // [])` | Safe. Absent means none. A present value is still length-checked. Compose does not emit boolean false for these. |
| `($m.volume // {})`, `($m.bind // {})` | Safe. Absent options become `{}`. A present object is still key-checked by `allowed`. |
| `($m.read_only // false)`, `nocopy // false` | Safe. Absent and false are the default. Explicit true is kept, because `//` does not replace true. |
| `(external // false) == false` | Safe. Absent and false are the allowed non-external declaration. Explicit true fails. Do not change. |
| Mount `if`/`else` in both model filters | Safe. Each else is `error("mount contract")`, `error("secret")`, `error("ephemeral")`, or the record that just passed the same check. None replaces a secret target with another name. |
| `secret_dest` relative `else` | The destination is `/run/secrets/` plus the preserved target string. It is not `.source`. |
| `c1008_container_census` | Safe. Image is type-checked before `test`. A null tmpfs is allowed explicitly. There is no masking `//`. |
| `c1008_owned_mounts` `(Tmpfs // {})` and `(Mounts // [])` | Safe. They fill a null inspect field. A present object or array is kept. |
| `Mountpoint // original.Mountpoint` | Safe. `//` fills only null or false. A present string path is kept. Docker inspect emits a string or omits the field. |
| `c849_fixture_model` | Safe. Equality only. A missing target fails `select`. It is not the recycle render. |
| `c849_cold_proof` | Safe. Those strings come from `docker inspect`, not `compose config`. |

### Checkpoints

Closed list. One isolated build, serial rows, no whole-Unit lane. UseAppHost=false is applied by the checkpoint tool on this host. CP-3 is the whole mount class, including the four relative-target methods. CP-15 and CP-16 name those methods. Run CP-10 last.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | v218 | `tests/Antiphon.Tests -> bin-c1105-v218/` | v218-model | `/*/*/RollingProductionMountTests/C1105_V218_*` | new model proofs | 2 methods, 0 failed | 2 | 15 | true |
| CP-2 | v218 | CP-1 | v218-dry-run | `/*/*/RemoteScriptContractTests/C1105_V218_dry_run_reaches_the_generation_line*` | dry-run generation line | 1 method, 0 failed | 1 | 8 | true |
| CP-3 | v218 | CP-1 | mount-class | `/*/*/RollingProductionMountTests/*` | class regression | 12 methods, 0 failed | 12 | 10 | true |
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
| CP-15 | v218 | CP-1 | relative-targets | `/*/*/RollingProductionMountTests/(C1105_Relative_secret_target_is_refused*)\|(C1105_Relative_secret_target_equal_to_its_name_is_accepted*)\|(C1105_Absolute_secret_target_keeps_the_base_rule*)\|(C1105_Malformed_secret_target_is_refused*)` | relative secret targets | 4 methods, 0 failed | 4 | 4 | true |
| CP-16 | v218 | CP-1 | relative-dry-run | `/*/*/RemoteScriptContractTests/C1105_Relative_dry_run_refuses_before_removal*` | relative target before removal | 1 method, 0 failed | 1 | 8 | true |

CP-1's filter is a trailing method wildcard and does not include the four `C1105_Relative_` / `C1105_Absolute_` / `C1105_Malformed_` methods. CP-3 does. CP-10 is the 20-minute recycle class and runs last. `C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle` is the compose text pin in CP-5.

PCs stay pending for method-scoped SourceLanding Mutation. No deliberate mutant is part of this checkpoint run. The original shape red proof reverts the target guard and restores `allowed(["file","name"])`. The repair red proof restores `/run/secrets/` plus `.source` for every non-absolute target and drops the string guard: `elsewhere`, malformed targets, and the relative dry-run fail, then the source is restored before this run. A relative target equal to the secret name, and an absolute target, already matched the roster on that fallback, so those two methods stay green under it.
