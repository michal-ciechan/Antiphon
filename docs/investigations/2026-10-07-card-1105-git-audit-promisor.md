# CARD-1105 recycle git audit on a blobless promisor checkout

The recycle publication audit refused every repository that had promisor or `extensions.partialclone` configuration. The deploy seeds that shape with `git clone --filter=blob:none --no-checkout`, so `redeploy-old` stopped in preflight with `RecycleGitAuditUnknown` before any container or volume was removed.

A promisor checkout is now audited with the same publication proof as any other repository: `git rev-list --count <tip> --not <origin heads>`. `GIT_NO_LAZY_FETCH=1` is exported before every Git command in the helper. Partial-clone configuration is not itself a refusal. A missing commit or tree, a failing `rev-list`, a failing `ls-remote`, a shallow repository, a lock, or any unclassifiable Git exit still refuses `RecycleGitAuditUnknown`. A Git failure is never counted as zero unpublished commits. An unpublished tip still refuses `RecycleUnpublishedWork` (helper classification 3, process exit 2). A dirty worktree still refuses `RecycleWorktreeDirty` (classification 4, process exit 2).

A `--no-checkout` seed has no index. `git status --porcelain` prints only staged deletions of HEAD (`D  <path>`) and the worktree has no file outside `.git`. That shape is the seed, not a dirty worktree. An index with any porcelain output, an untracked file, or a modification still refuses dirty. `git commit --allow-empty` on that seed creates an index and an empty status; the unpublished commit is then caught by `rev-list`.

On refusal the journal `audit` field is one line, `audit check=<command> status=<exit> repo=<path>`. The path is relative to the work volume. A component matching credential, token, secret, password, `ghp_`/`gho_`/`ghu_`/`ghs_`/`ghr_`, or `github_pat_` is `REDACTED`. Git stderr is not copied. On success the same field keeps the existing `tip=` lines and adds `repositories=<n> partial=<n>`. `GithubTokenAbsent` stays a warning. This audit does not read the token; the token is only for secondary receive-pack probes.

The whole Unit lane is outside this ordinary scope. The brief overrides the Final profile's Unit lane, and `/*/*/*/*[Category=Unit]` is not a checkpoint row.

Current `origin/master` at the fast-forward was `16ed20f3af157e0e922a95ab9b4cb90fb2c7e3a2` (docs-only CARD-1074). The dispatch base was `b5e78700ae9a76430c13d75cc03399055dd82e59`.

## Positive controls pending

These stay pending for method-scoped SourceLanding Mutation. The detecting filter for each is `/*/*/RemoteScriptContractTests/C1008_Recycle_audits_promisor_checkout*`.

| PC | Production line | Mutation | Expected red |
|---|---|---|---|
| PC-1 | promisor `case` in `c1008_git_program` that continues when `partial_status` is 0 | restore `[ "$partial_status" = 1 ] \|\| fail "$partial_status"` | published blobless seed is refused |
| PC-2 | `rev-list --count` assignment's `\|\| fail $?` | change that failure to `\|\| count=0` | `gitFault=exit128` is accepted and volumes are removed |
| PC-3 | `export GIT_NO_LAZY_FETCH=1` at the top of the helper | drop `GIT_NO_LAZY_FETCH=1` | missing pack is accepted, or the helper text no longer contains the export |

Code ran each mutation once against the built `C1008_Recycle_audits_promisor_checkout` test and restored `scripts/c590-remote.sh` before the commit. PC-1 refused the published seed (`RecycleGitAuditUnknown`, `check=config-promisor status=0`, `published.Removed.Length`). PC-2 removed volumes when `rev-list` exited 128 (`revList.Removed` was not empty). PC-3 failed the assertion that the helper text contains `GIT_NO_LAZY_FETCH=1`. Those runs are the Code proof. The official red, restore, and green cycles stay with SourceLanding Mutation.

## Checkpoints

Closed list. One isolated build, serial rows, no whole-Unit lane. UseAppHost=false is applied by the checkpoint tool on this host. CP-11 is the git-audit row and CP-12 is the recycle wrapper; both run last.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | promisor | `tests/Antiphon.Tests -> bin-c1105-promisor/` | docs-pins | `/*/*/DockerStackDocumentationTests/*` | doc pins | 14 methods, 0 failed | 14 | 15 | true |
| CP-2 | promisor | CP-1 | registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | registry guard | 3 methods, 0 failed | 3 | 3 | true |
| CP-3 | promisor | CP-1 | compose-text-pins | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)\|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)\|(Deploy_parent_seeds_or_verifies_runner_checkout*)\|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)\|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*)` | compose text pins | 6 methods, 0 failed | 6 | 4 | true |
| CP-4 | promisor | CP-1 | mount-class | `/*/*/RollingProductionMountTests/*` | class regression | 12 methods, 0 failed | 12 | 10 | true |
| CP-5 | promisor | CP-1 | retired-temp-script | `/*/*/RetiredTempContainerScriptTests/*` | class regression | 13 methods, 0 failed | 13 | 10 | true |
| CP-6 | promisor | CP-1 | host-jq | `/*/*/HostJqPrerequisiteScriptTests/*` | class regression | 39 methods, 0 failed | 39 | 15 | true |
| CP-7 | promisor | CP-1 | retired-temp-host | `/*/*/RetiredTempContainerHostTests/*` | class regression | 21 methods, 0 failed | 21 | 15 | true |
| CP-8 | promisor | CP-1 | remote-generation | `/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)\|(C1105_Generation_identity_refuses_mismatch*)\|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)\|(C1008_Recycle_exact_default_volumes*)\|(C1008_Recycle_dry_run_never_mutates*)` | C1008/C1105 | 5 methods, 0 failed | 5 | 15 | true |
| CP-9 | promisor | CP-1 | remote-census-long | `/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_receipt_records_disk_and_partial_failure*)\|(C1008_Recycle_refuses_references_and_unknown_census*)` | C1008 | 3 methods, 0 failed | 3 | 15 | true |
| CP-10 | promisor | CP-1 | remote-resume | `/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)\|(C1105_Resume_identity_refusals*)\|(C1105_Resume_state_init_image_follows_the_journal*)\|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*)` | C1105 | 4 methods, 0 failed | 4 | 12 | true |
| CP-11 | promisor | CP-1 | remote-git-audit | `/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1087_Host_census_filters_and_names_cause*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_audits_promisor_checkout*)` | C1008/C1087 promisor audit | 8 methods, 0 failed | 8 | 15 | true |
| CP-12 | promisor | CP-1 | recycle-wrapper | `/*/*/RollingVolumeRecycleScriptTests/*` | class regression | 16 methods, 0 failed | 16 | 15 | true |
