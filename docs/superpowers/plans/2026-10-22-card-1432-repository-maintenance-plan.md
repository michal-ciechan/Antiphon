# CARD-1432 plan: repository maintenance (R1-R5)

Source: investigation `docs/investigations/2026-10-21-card-1432-desktop-repo-maintenance.md`
(Debug e57ff1e3, commit `8d1a6cf7e216c66e63463389b746ded08482902f`). Base: `a550bcbd2b7413093ed1cc978942b82696f4fd04`.
Operator decisions 2026-10-21: adopt R1-R5; "fine to prune and cleanup and pack, and delete old refs don't ask me,
just go for it, better to delete more than less".

Slice order is by value against risk: S1 (R3) and S2 (R2) first, then S3 (R1), S4 (R4), S5 (R5).
Each slice lands on its own. S5 must not start before S4 is merged, because its process counts are measured with S4.

## Ground truth (what the card assumes, what the code does)

| # | Card or investigation assumption | Code at base | Consequence for the plan |
|---|---|---|---|
| T-1 | "-c gc.auto=0 -c maintenance.auto=false on every server git command" | 13 separate git start sites: `LandingGit.cs:73` and `:712` (the land and inspection path), `GitService.cs:339,398`, `WorktreeManager.cs:1165`, `CardFileRepository.cs:328`, `GitWorkspaceService.cs:229,1137`, `DelegationWorktreeService.cs:910`, `DelegationWorkspaceResolver.cs:138`, `OrchestratorWorkspaceLayout.cs:578`, `AgentTaskReplyService.cs:2198`, `ChangeDetectionService.cs:360`. `GitTool.cs:70` runs agent-supplied git. | One helper, applied at every server start site (D-1). The agent tool stays out of scope (D-2). |
| T-2 | `-c` arguments are harmless | Test seams read `start.ArgumentList` positionally. `LandHalfResetFixture.cs:230` matches `args[0] == "update-ref"` with `args.Count == 5`. `LandingGitTests.cs:632` puts a pwsh wrapper's arguments in front of git's. Fixtures already set `GIT_CONFIG_COUNT=3` in `ConfigureProcess` (`LandingGitFixture.cs:177`). | Use the git environment form `GIT_CONFIG_COUNT/KEY_n/VALUE_n`, which git treats like `-c`. Append after any existing entries, and apply after `ConfigureProcess` (D-1). |
| T-3 | The runner already disables auto gc | True only for inspection calls (`RunnerWorkspaceService.cs:986-990`). That code replaces the entries and does not append. | Do not reuse it. The runner is out of scope (D-2). |
| T-4 | "Delete refs when a land completes" | Cleanup sets `Cleanup=Complete` and `Phase=Complete` in memory (`AgentTaskLandingProtocol.cs:441-445`). `AgentTaskLandService` commits the terminal event later. The `finally` block at `AgentTaskLandService.cs:502` still holds the lease scope (`LandRepositoryLeaseScope`, `:492`). | Delete only after the terminal state is committed, while the lease is still held. Put it inside `RunLeasedAsync`'s terminal path, before the scope is disposed (D-3). |
| T-5 | Land refs live under `land/<task>/<landing>/` | `source-observed`, `recover-*-observed` and adopt pins use the **request** id (`AgentTaskLandSourceResolver.cs:115,257,268`). `mirror-observed` uses a fresh random GUID (`CardWorktreeMirrorCleanupService.cs:76`). `cleanup-observed` for a retirement lives under `refs/antiphon/retirement/<id>/` (`LandingGit.cs:1058`). | The rule works per task (`land/<task>/`), not per landing. Matching on the landing prefix would miss the request-id and mirror subtrees. |
| T-6 | "Delete progress refs when a task reaches a terminal state" | Eleven `Status = Succeeded/Failed/Canceled` writes across six services (`AgentTaskService`, `AgentTaskDispatcher`, `AgentTaskReplyService`, `AgentTaskCheckService`, `CheckCompactionContinuationService`, `AgentTaskLandService`). None of them holds the repository lease. | Deleting a packed ref rewrites `packed-refs`. A concurrent land's `update-ref` could then fail on `packed-refs.lock`. So deletion for terminal tasks that never land goes to a frequent leased sweep pass in S3, not to the eleven write sites (D-4). |
| T-7 | Hangfire on the main instance | Recurring jobs are registered in `Program.cs:1087-1110`, inside `if (hangfire.ServerEnabled)`. Each job has a settings class with `Enabled/RecurringJobId/Cron/TimeZoneId` (`WorktreeResidueSettings.cs`), an `[AutomaticRetry(Attempts = 0)]` job class (`WorktreeResidueJob.cs`) and an `AddOrUpdate*` helper (`HangfireConfiguration.cs:59`). | S3 follows that pattern exactly. |
| T-8 | "Yields to queued lands and dispatch waiters" | `RepositoryLeaseWaiters.Any(common)` exists (`RepositoryLeaseWaiters.cs:156`). The lease is `IRepositoryMutationLease.TryAcquireAsync(repo, owner, ct)`. | Add the purpose `RepositoryLeasePurposes.Maintenance`. Skip a repository when `Any(common)` is true before the acquire, and check again between the leased steps. |
| T-9 | Canonical repositories | `Project.LocalRepositoryPath` (`Project.cs:16`). Tasks carry `RepoPath`. | Canonical repositories are the distinct common directories of existing `Project.LocalRepositoryPath` values, deduplicated through `ILandingGit.CommonDirectoryAsync`. |
| T-10 | Per-subcommand histogram | `LandingGitProfile.Record(arguments, elapsed)` already finds the subcommand index after global options (`LandingDtos.cs:120-137`). `Add` merges profiles. | Add one `ConcurrentDictionary<string,long>` keyed by `subcommand[ sub-argument]`, merge it in `Add`, and log it as `histogram=` (S4). |
| T-11 | G5: fetch alone is enough | `ObserveSourceAsync` maps `ls-remote` exit 2 to `source_remote_missing` (`LandingGit.cs:863`). `fetch` of a missing ref exits 128, the same code as a network failure. | G5 fetches first. Only when the fetch fails does it run `ls-remote` to classify the failure. The happy path is one round trip, and the reasons stay the same. |
| T-12 | G4: the remote URL can be read once | `EndpointAsync` runs once per `DestinationAsync`, per observation attempt, and before and after each observation, and again before push (`LandingGit.cs:785-1000`). `LandingGit` already has a per-operation `AsyncLocal` scope (`:271-284`). | Cache the endpoint in that scope, keyed by repository. Re-read it fresh right before `push`, the irreversible step (D-8). |
| T-13 | Stale lock rule | Git ignores a `gc.pid` older than 12 h, but a `maintenance.lock` of any age makes `maintenance run --auto` exit 0 silently (investigation, Outcome). | S3 step 1 removes both when older than 12 h and the recorded PID is not alive. `gc.pid` holds `<pid> <host>`. A different host name means the PID is not checked and only the age applies. |

## Decisions

- **D-1 Use the environment form of `-c`.** `ServerGitProcess.DisableAutoMaintenance(ProcessStartInfo)` lives in `server/Application/Services/ServerGitProcess.cs`, so both Application and Infrastructure start sites can call it. It reads any existing `GIT_CONFIG_COUNT` and appends `gc.auto=0` and `maintenance.auto=false` at index n and n+1. If an existing count is not a valid number, it throws `InvalidOperationException("git_config_count_invalid")` instead of overwriting the entries. It runs after `ConfigureProcess` and before the arguments are added. Rejected: prepending `-c` arguments. That breaks the positional `StartProcess` and `ConfigureProcess` test seams (T-2). The `arguments` list that the profile, `ChangesRegistrations` and the `mutating` check read (`LandingGit.cs:83`) is not changed in either form. Git documents `GIT_CONFIG_COUNT` as equivalent to `-c`, at the same precedence.
- **D-2 Scope of R3.** The thirteen server start sites in T-1 are in scope. `GitTool` (agent-supplied commands) and the runner (`RunnerWorkspaceService`, which already disables auto gc for inspections) are out of scope. A follow-up can add the runner's non-inspection calls (F-1).
- **D-3 Delete land refs at the source, after the terminal commit, under the lease.** Add `LandRefRetention.ReleaseAsync(task, request, lease)`. It is called from `AgentTaskLandService.RunLeasedAsync` after the terminal `SaveChangesAsync` and before the lease scope is disposed. It runs one `for-each-ref --format=%(refname) %(objectname) refs/antiphon/land/<task>/ refs/antiphon/progress/<task>/` and one `update-ref --stdin` with `delete <ref> <oid>` lines, written as UTF-8 without a BOM through the stdin of `RunGitInputBytesAsync` (the investigation's BOM failure). The batch is a mutating child and runs under the `RepositoryChildJournal` like every other `update-ref` (begin before `Process.Start`, started, exited after both streams drain, not-started when no child was created; Review 9e126982 D2).
  - **Task not protected** (the §2 rule, evaluated for this one task, after the commit): delete the `progress/<task>/` subtree, every observation pin, and each `land/<task>/<operation>/` subtree whose operation is superseded. The recovery pins (`source`, `target-before`, `prepared`, `adopt/*`) of the task's latest landing operation (its active landing, and the newest `AgentTaskLandings` row) and of every Refused landing stay: CARD-0448 V-26 says a land's pins survive cleanup and gc, and the refused-retry suite resumes a retry from the Refused operation's pins (Review 9e126982 D1; `LandRefRetention.RetainedOperationsAsync` and `IsDeletable`). The S3 sweep must apply the same retained-operation rule to a terminal task (F-5).
  - **Task protected** (for example a Refused land that can be retried, or an open sync debt): delete only the observation pins under it. These are refs whose path has a segment ending in `-observed`, or a `source-recheck` segment. Recovery pins (`source`, `target-before`, `prepared`) and `adopt/*` stay.
  - A failed deletion is logged as a warning and never changes the land outcome. The S3 sweep is the backstop.
  - Rejected: deleting each observation pin right after its comparison. That costs about 8 more processes per land, against R5.
- **D-4 Terminal tasks that never land get a frequent leased sweep pass, not hooks at the eleven write sites.** S3's job has a second schedule, `RefSweepCron`, default every 15 minutes. It runs leased step 2 only: the full ref sweep. Its task ids come from the one `for-each-ref` over `refs/antiphon/{land,progress}/`, which took 0.2 s after the cleanup. There is no watermark. A watermark would miss a task that was protected at one pass and released later, for example a land that stays Held after the task completes. Rejected: hooking all eleven status writes. None holds the lease (T-6), and a missed site would be a silent leak, which the sweep must catch anyway. This is the one place the plan departs from "at the source". The delay is at most 15 minutes.
- **D-5 Protected set, verbatim from the investigation §2.** It is implemented once, in `AntiphonRefProtection.QueryAsync(db, taskIds?)`, and used by S2 and S3. Tasks with no database row are not protected. That covers "deletes more than less", inside the rule. `refs/heads`, `refs/remotes`, `refs/tags`, `refs/rescue`, `refs/cleanup-kept`, `refs/stash`, `refs/antiphon/retirement/*` and `refs/antiphon/start-ref-probe` are never touched. The last two are transient and owned by their operations. The sweep only reports them when they are older than 24 h (F-2).
- **D-6 The lease is held only for steps 1-3.**
  - Steps 1-3: stale-lock repair, the ref sweep, and the worktree prune plus unlock-and-prune of `locked initializing` registrations older than 24 h with no directory. A registration with an in-flight cut is excluded: any `AgentTasks` row in Queued/Dispatched/Working with that `WorktreePath`.
  - Step 4 runs after the lease is released: `git maintenance run --task=loose-objects --task=incremental-repack --task=commit-graph --task=pack-refs` nightly, and `git gc` (default prune grace, never `--prune=now`) on `WeeklyGcDay` (default Sunday). Step 4 runs with a 3-hour budget and without the D-1 environment entries. It is the one place gc is meant to run.
  - Waiters: if `RepositoryLeaseWaiters.Any(common)` is true before the acquire, the job skips that repository. It is checked again between steps 1, 2 and 3. A waiter that appears ends the leased part early and the job records `yielded`. Step 4 still runs.
  - Busy lease: the job skips with `lease_busy`. An hourly retry schedule (`RetryCron`, default `15 * * * *`) does the leased steps only if tonight's run recorded no successful leased pass.
- **D-7 Thresholds raise attention items and never block.** Defaults: loose objects > 20,000, packs > 50, refs > 15,000, loose ref files > 1,000, worktree registrations > 300, maintenance lock older than 12 h. The counts come from `count-objects -v` (one process), one `for-each-ref --format=x` line count, a `Directory.EnumerateFiles` over `refs/` and the `worktrees/` directory count. The metrics line uses the `Repository maintenance` prefix. Each breach raises one attention item through the existing condition-key attention path, `ConditionKey: "repository-maintenance:<threshold>:<common>"` (`SettlementSyncDebtAttention.cs:58` is the template), and the item clears when the value drops back.
- **D-8 G4 caches the endpoint per operation scope and keeps a fresh read before push.** Every `remote_configuration_changed` and `source_remote_endpoint_changed` check stays. Inside one scope the check compares against the bound endpoint. `PushAsync` and `PushOwnedAsync` re-read it. A scope with no binding, outside a land, behaves as it does today.
- **D-9 Every land check is kept in R5.** G1-G6 change how many processes the checks take, not which checks run. Each existing refusal reason stays reachable, and S5's tests assert the reason strings. G7 and G8 are out of scope (the brief names G1-G6). G8 overlaps CARD-1263.
- **D-10 Settings.** A new `RepositoryMaintenanceSettings` section with:
  - `Enabled` (true);
  - `RecurringJobId` (`antiphon:repository-maintenance`) and `Cron` (`30 3 * * *`);
  - `RefSweepCron` (`*/15 * * * *`) and `RetryCron` (`15 * * * *`);
  - `TimeZoneId` (`Europe/London`) and `WeeklyGcDay` (`Sunday`);
  - `StaleLockHours` (12) and `LockedInitializingHours` (24);
  - the thresholds from D-7.

  The ops route `POST /api/agent-tasks/repository-maintenance/run?mode=full|refs` triggers the job and returns the run id. It sits next to the worktree-residue routes (`AgentTaskEndpoints.cs:129`) and uses the same authorization.
- **D-11 Platform and lane.** Placement default is `server2` (`GET /api/runner-defaults`: globalRunnerId `server2`). Every checkpoint uses temp repos or fake git and runs on Linux. No `-Runner` or `-Platform` pin is needed. Windows PID liveness uses `Process.GetProcessById`, which is cross-platform, and the tests write the `gc.pid` files themselves.

## Slices

### S1 (R3): no auto gc in server git

- Add `server/Application/Services/ServerGitProcess.cs` with `DisableAutoMaintenance(ProcessStartInfo)` (D-1).
- Call it at all thirteen start sites in T-1, right before the argument loop. In `LandingGit` that means both `ExecuteProcessAsync` and `RunGitInputBytesAsync`, after `ConfigureProcess`.
- Tests:
  - New `tests/Antiphon.Tests/Infrastructure/ServerGitProcessTests.cs`.
  - `C1432_land_git_reads_gc_auto_zero`: a temp repo with `gc.auto=6700` and `maintenance.auto=true` in its own config, run through `LandingGitFixture.FixtureGit` (which sets `GIT_CONFIG_COUNT=3`). `RunAsync(["config","--get","gc.auto"])` must return `0`, `maintenance.auto` must return `false`, and `commit.gpgSign` must still return `false`, proving the fixture's entries were appended to and not replaced. This is real git.
  - `C1432_append_preserves_existing_entries` and `C1432_invalid_count_throws`: pure unit tests on `ProcessStartInfo`.
  - `C1432_every_server_git_start_site_disables_auto_maintenance`: a source-text roster that names the thirteen files and asserts that each `ProcessStartInfo` git construction calls `ServerGitProcess.DisableAutoMaintenance` (D-2 lists exclusions). It is a roster over named files, not a repository-wide scan.

### S2 (R2): delete land and progress refs at the source

- Add `server/Application/Services/AntiphonRefProtection.cs` (D-5) and `server/Application/Services/LandRefRetention.cs` (D-3).
- Add `ILandingGit.UpdateRefsAsync(repository, IReadOnlyList<(string Ref, string Oid)> deletes, ct)`, implemented in `LandingGit` with `update-ref --stdin` over `RunGitInputBytesAsync` (no BOM), under the child journal (D2). It refuses any ref outside `refs/antiphon/land/` and `refs/antiphon/progress/`.
- Add the call in `AgentTaskLandService.RunLeasedAsync` after the terminal commit (T-4).
- Tests:
  - New `tests/Antiphon.Tests/Application/LandRefRetentionTests.cs`, with `LandingGitFixture` temp repos and the test DB.
  - `C1432_completed_land_deletes_task_land_and_progress_refs`: an unprotected task. Its request-id, mirror and superseded-operation subtrees and its `progress/<task>/` refs are gone; the landed operation's `source`, `target-before` and `prepared` pins stay. Another task's refs and `refs/heads/*` are unchanged.
  - `C1432_unprotected_task_keeps_latest_and_refused_operation_pins` (Review 9e126982 D1): rows for a Refused, a superseded and a latest landing. The latest and Refused operations keep their recovery pins; the superseded operation, a settled request's `adopt/*`, every observation and the progress pins go.
  - `LandingGitTests.C1432_UpdateRefsBatchRunsUnderTheChildJournal` (D2): a `reference-transaction` hook holds the batch while a journal record with its process id exists; the record is gone after exit.
  - `C1432_protected_task_keeps_recovery_pins_drops_observations`: a task with a `Pending` sync debt. `source`, `target-before`, `prepared` and `adopt/*` stay. `*-observed/*` and `source-recheck/*` are gone. `progress/<task>/settlement-sync` stays.
  - `C1432_protection_rule_rows`: one data row per §2 clause. Each protects a task, and the terminal or missing-row cases do not.
  - `C1432_update_refs_rejects_foreign_namespace` and `C1432_update_refs_old_value_mismatch_applies_nothing`: real git. One stale oid in the batch leaves every ref in place.
- Regression: the existing `LandingProtocolHarnessTests` (10).

### S3 (R1): the `RepositoryMaintenance` Hangfire job

- Add these files:
  - `server/Application/Settings/RepositoryMaintenanceSettings.cs`, with a validator (D-10);
  - `server/Application/Services/RepositoryMaintenanceService.cs`, for steps 1-5 (D-6, D-7), reusing `AntiphonRefProtection` and `ILandingGit.UpdateRefsAsync` for step 2;
  - `server/Infrastructure/Agents/RepositoryMaintenanceJob.cs`, with `[AutomaticRetry(Attempts = 0)]` and the methods `ExecuteFullAsync`, `ExecuteRefsAsync` and `ExecuteRetryAsync`;
  - three `AddOrUpdate*` helpers in `HangfireConfiguration.cs`, registered in `Program.cs` under `hangfire.ServerEnabled && settings.Enabled`;
  - the purpose `RepositoryLeasePurposes.Maintenance`;
  - a `RepositoryMaintenanceRuns` entity and migration (run id, mode, common dir, start and end, lease outcome, counts). The retry schedule reads it;
  - the ops route (D-10).
- Step 2 at scale: the delete list is written as one batch, and it may contain about 19,000 lines (41.5 s in the investigation). Step 2 has a 10-minute budget and checks for waiters before it starts.
- Tests:
  - New `tests/Antiphon.Tests/Application/RepositoryMaintenanceTests.cs`, with temp repos.
  - `C1432_stale_maintenance_lock_and_gc_pid_removed`: a zero-byte `objects/maintenance.lock` and a `gc.pid` with a dead PID, both backdated 13 h, are removed and recorded. Fresh ones (1 h old), and ones with a live PID, stay. This is the red witness.
  - `C1432_ref_sweep_deletes_terminal_and_missing_tasks_only`.
  - `C1432_locked_initializing_registration_without_directory_pruned`: built with a real `git worktree add` on a temp repo, then the directory is deleted and the lock file reason is set to `initializing`. A locked registration that still has a directory, or one whose task is in flight, stays.
  - `C1432_gc_runs_after_lease_release`: a fake `ILandingGit` records the order of lease acquire, release and `maintenance run`.
  - `C1432_waiter_yields_before_step_two`.
  - `C1432_busy_lease_skips_and_retry_runs_once`.
  - `C1432_thresholds_raise_attention`.
  - `C1432_refs_mode_runs_only_the_ref_sweep`.

### S4 (R4): per-subcommand histogram in the land git profile

- In `LandingGitProfile`, the key is the subcommand plus the first non-option sub-argument when the subcommand is `worktree`, `remote`, `config`, `show-ref`, `update-ref`, `ls-files` or `merge-base`. Examples: `worktree list`, `show-ref --verify`.
- `Describe()` and the `Land git profile` log line (`AgentTaskLandService.cs:511`) gain `histogram={...}`, written as `key=n` sorted by key, at most 40 entries plus `other=n`.
- Tests: three new `C1432_` methods in `tests/Antiphon.Tests/Infrastructure/LandingGitProfileTests.cs`:
  - `C1432_histogram_counts_subcommands_after_global_options` (the red witness);
  - `C1432_histogram_merges_in_add`;
  - `C1432_histogram_caps_entries`.

### S5 (R5): batch G1-G6

- **G1.** `RecheckSourceIdentityAndPinsAsync` and the branch read in `RecheckSourceAsync` become one `show-ref --verify <pins...> <sourceRef>`, parsed per line. If any ref is missing, show-ref exits non-zero. That keeps the `recovery_pin_changed` and `source_changed` reasons; on a non-zero exit, the line missing from the output picks the reason.
- **G2.** `PinWithPrefixAsync` is replaced by `update-ref --stdin` with `create <ref> <sha>`. That fails if the ref exists. Only on that refusal does it read the ref back, to decide between `0` (same SHA) and `recovery_ref_collision`. `invalid_recovery_commit` stays: it is checked with one `cat-file -e <sha>^{commit}`.
- **G3.** `GitRefName.IsValid(string)`, a pure C# port of the `check-ref-format` rules that apply. It replaces every `check-ref-format` call in `LandingGit` and `TaskProgressGit`.
- **G4.** The endpoint cache from D-8.
- **G5.** In `ObserveWithPrefixAsync` and `ObserveSourceAsync`, fetch into the pin first. `ls-remote` runs only when the fetch fails, to classify missing versus unreadable (T-11). The three-attempt loop is no longer needed for consistency, because the pin is the observation. One attempt remains.
- **G6.** The inspection's three `config` reads become one `config --get-regexp ^core\.(sparsecheckout|autocrlf|filemode)$`, and `ls-files -v -z` plus `ls-files --stage -z` become one `ls-files --stage -v -z`. This is at `LandingGit.cs:559-585`.
- Tests:
  - New `tests/Antiphon.Tests/Infrastructure/LandingGitBatchingTests.cs`, with real temp repos.
  - `C1432_recheck_reads_pins_and_branch_in_one_process`: the red witness. The profile histogram from S4 shows `show-ref` = 1 per recheck.
  - `C1432_pin_create_refuses_existing_ref_with_other_sha`.
  - `C1432_ref_name_validator_matches_git`: about 30 rows, each checked against real `check-ref-format`.
  - `C1432_observe_one_round_trip_on_success`.
  - `C1432_observe_missing_source_still_reports_missing`.
  - `C1432_inspection_reads_config_once`.
  - `C1432_endpoint_reread_before_push`: the endpoint changes inside a scope, and push refuses with `remote_configuration_changed`.
- Regression: `LandingGitTests` (24), `LandingProtocolGuardTests` (17) and `LandingProtocolHarnessTests` (10). These carry the existing reason-string assertions.

## Verification design

| V | Claim | Test |
|---|---|---|
| V-1 | No server git child reads `gc.auto` other than 0, or `maintenance.auto` other than false, and fixture entries survive | `ServerGitProcessTests.C1432_land_git_reads_gc_auto_zero`, `C1432_append_preserves_existing_entries`, `C1432_invalid_count_throws` |
| V-2 | All thirteen server start sites apply the guard | `ServerGitProcessTests.C1432_every_server_git_start_site_disables_auto_maintenance` |
| V-3 | A completed land leaves an unprotected task only the landed operation's recovery pins; its observation, superseded-operation and `progress/<task>/` refs are gone | `LandRefRetentionTests.C1432_completed_land_deletes_task_land_and_progress_refs` |
| V-4 | A protected task keeps its recovery pins and loses only observations | `LandRefRetentionTests.C1432_protected_task_keeps_recovery_pins_drops_observations`, `C1432_protection_rule_rows` |
| V-5 | The batch is all-or-nothing and refuses foreign refs | `LandRefRetentionTests.C1432_update_refs_*` |
| V-6 | The job repairs stale locks, sweeps refs, prunes stale registrations, runs gc outside the lease, yields and retries | `RepositoryMaintenanceTests.C1432_*` |
| V-7 | The profile line carries a per-subcommand histogram | `LandingGitProfileTests.C1432_*` |
| V-8 | G1-G6 cut processes and keep every reason | `LandingGitBatchingTests.C1432_*` plus the land regression classes |
| V-9 | An unprotected task keeps the recovery pins of its latest and of every Refused landing operation (CARD-0448 V-26, refused-retry pin rule) | `LandRefRetentionTests.C1432_unprotected_task_keeps_latest_and_refused_operation_pins`, `AgentTaskLandPublicationTests.C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection`, `AgentTaskLandRefusedRetryTests.RR_V5_SettledRefusalIsNotAutomaticallyQueued` |
| V-10 | The `update-ref --stdin` batch runs under a child journal record while it runs and leaves none after | `LandingGitTests.C1432_UpdateRefsBatchRunsUnderTheChildJournal` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 test commit | `tests/Antiphon.Tests -> bin-c1432-s1red/` | c1432-s1-red | `/*/Antiphon.Tests.Infrastructure/ServerGitProcessTests/C1432_land_git_reads_gc_auto_zero` | V-1 witness | 1 executed, failed: `gc.auto` read 6700, expected 0 | 1 | 6 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1432-s1/` | c1432-s1 | `/*/Antiphon.Tests.Infrastructure/ServerGitProcessTests/C1432_*` | V-1, V-2 | 4 passed, 0 failed/skipped | 4 | 7 |
| CP-3 | S2 test commit | `tests/Antiphon.Tests -> bin-c1432-s2red/` | c1432-s2-red | `/*/Antiphon.Tests.Application/LandRefRetentionTests/C1432_completed_land_deletes_task_land_and_progress_refs` | V-3 witness | 1 executed, failed: land refs still present | 1 | 6 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1432-s2/` | c1432-s2 | `/*/Antiphon.Tests.Application/(LandRefRetentionTests*)\|(LandingProtocolHarnessTests*)/*` | V-3, V-4, V-5, land regression | about 18 passed (8+ new including row cases, 10 existing), 0 failed/skipped | 15 | 10 |
| CP-5 | S3 test commit | `tests/Antiphon.Tests -> bin-c1432-s3red/` | c1432-s3-red | `/*/Antiphon.Tests.Application/RepositoryMaintenanceTests/C1432_stale_maintenance_lock_and_gc_pid_removed` | V-6 witness | 1 executed, failed: `maintenance.lock` still present | 1 | 6 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1432-s3/` | c1432-s3 | `/*/Antiphon.Tests.Application/RepositoryMaintenanceTests/C1432_*` | V-6 | 8 passed, 0 failed/skipped | 8 | 9 |
| CP-7 | S4 test commit | `tests/Antiphon.Tests -> bin-c1432-s4red/` | c1432-s4-red | `/*/Antiphon.Tests.Infrastructure/LandingGitProfileTests/C1432_histogram_counts_subcommands_after_global_options` | V-7 witness | 1 executed, failed: no histogram entry | 1 | 6 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c1432-s4/` | c1432-s4 | `/*/Antiphon.Tests.Infrastructure/LandingGitProfileTests/*` | V-7, existing profile tests | 9 passed (6 existing + 3), 0 failed/skipped | 9 | 6 |
| CP-9 | S5 test commit | `tests/Antiphon.Tests -> bin-c1432-s5red/` | c1432-s5-red | `/*/Antiphon.Tests.Infrastructure/LandingGitBatchingTests/C1432_recheck_reads_pins_and_branch_in_one_process` | V-8 witness | 1 executed, failed: show-ref count > 1 | 1 | 6 |
| CP-10 | S5 | `tests/Antiphon.Tests -> bin-c1432-s5/` | c1432-s5 | `/*/Antiphon.Tests.Infrastructure/(LandingGitBatchingTests*)\|(LandingGitTests*)/*` | V-8, land git regression | about 31 passed (7 new plus validator rows, 24 existing), 0 failed/skipped | 31 | 12 |
| CP-11 | S5 | CP-10 (-NoBuild) | c1432-s5-protocol | `/*/Antiphon.Tests.Application/(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)/*` | V-8 reason strings through the protocol | 27 passed, 0 failed/skipped | 27 | 10 |
| CP-R1 | S2 repair | `tests/Antiphon.Tests -> bin-c1432-r2/` | c1432-r2 | `/*/Antiphon.Tests.Application/LandRefRetentionTests/*` | V-3, V-4, V-5, V-9 | 37 passed, 0 failed/skipped | 37 | 6 |
| CP-R2 | S2 repair | CP-R1 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Infrastructure/LandingGitTests/*` | V-10, land git regression | 58 passed, 0 failed/skipped | 58 | 8 |
| CP-R3 | S2 repair | CP-R1 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Application/LandingProtocolHarnessTests/(C475_CoverageAllocationMatchesTheLegacyTupleManifest*)\|(C475_AllServicesUseTheRegisteredGit*)\|(C475_AcknowledgedHookReadsCommittedPhase*)\|(C475_SourceAliasContendsOnRepositoryLease*)\|(C475_CleanupUsesGuardedRemoval*)\|(C475_RecreationPreservesCommittedModeAndAttempts*)\|(C475_ModesAreReachedThroughProtocol*)\|(C475_CleanupRetryRequiresFreshRemoteContainment*)` | land regression; `C475_LandingKeepsLeaseThroughProtocol` (4 cases) is red at base 55b70676 and stays a documented non-receipt exclusion | 14 passed, 0 failed/skipped | 14 | 8 |
| CP-R4 | S2 repair | CP-R1 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C494_*)\|(C448_*)\|(C665_*)\|(C642_*)\|(C688_RebaseAndVerifyRunInTheLandWorktree*)\|(C688_PushPrecedesCanonicalAdvance*)\|(C688_DirtyCanonicalCheckoutLandsWithResidue*)\|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)\|(C688_Repair_AlreadyCanonicalRecordsLocalIdentity*)\|(C688_LocalMasterBehindOriginRebasesOntoOrigin*)\|(C688_LocalMasterAheadOfOriginRefuses*)` | V-9 (`C448_V26`, `C448_V09`, `C448_V22`) and the land regression; excluded: `C688_LandNeverRunsGitInTheSourceWorktree` (red at base 55b70676) and `C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance` (its `locked` row self-skips off Windows) | 43 passed, 0 failed/skipped | 40 | 12 |
| CP-R5 | S2 repair | CP-R1 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Application/AgentTaskLandRefusedRetryTests/(RR_V2_StillDirtyTargetCreatesFreshRefusal*)\|(RR_V4_RemoteContainmentAfterRefusalKeepsAlreadyPresentShortcut*)\|(RR_V5_OriginalPendingRequestCannotReplaceRefusal*)\|(RR_V5_EqualPendingTimestampCannotReplaceRefusal*)\|(RR_V5_SettledRefusalIsNotAutomaticallyQueued*)\|(C688_RealLandProfile*)` | V-9 (the refused-retry pin rule); excluded as red at base 55b70676: `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation`, `RR_V3_TargetOnlyAdvanceRequiresFreshSelectedVerification`, `RR_V4_RemoteReadFailureRetriesUnchangedSource`, `RR_V4_TargetStatusFailureRetriesFromFreshEvidence` (2), `RR_V6_PreparationFailurePreservesActiveRefusal` (2), `RR_V6_ReplacementSaveFailureRollsBackOldDeactivation` (2) | 6 passed, 0 failed/skipped | 6 | 8 |
| CP-R6 | S2 repair | CP-R1 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Infrastructure/ServerGitProcessTests/C1432_*` | CP-2's filter (V-1, V-2) on the repair tip | 6 passed, 0 failed/skipped | 4 | 4 |
| CP-R7 | S2 repair | CP-R1 (-NoBuild) | c1432-r2 | `/*/Antiphon.Tests.Infrastructure/LandingGitProfileTests/*` | CP-8's filter (V-7) on the repair tip | 26 passed, 0 failed/skipped | 9 | 4 |

Each slice is its own checkpoint-tool run (`--after S<n>`, and the red row at that slice's test commit). Build
outputs are never reused across After groups. The one exception is CP-11, which shares CP-10's After group. The S2 repair rows CP-R1 to CP-R7 (Review 9e126982) are one run with one build, `--rows CP-R1,CP-R2,CP-R3,CP-R4,CP-R5,CP-R6,CP-R7`; the nine cases red at base 55b70676 in the two land classes and the four `C475_LandingKeepsLeaseThroughProtocol` cases are excluded by method so the receipt stays valid, and are listed in the rows' Covers cells. `Min` is a floor; confirm `executed=` in each CHECKPOINT line. The red rows pass when the run shows the stated failure. A red row that compiles to 0 tests, or fails to build, is not red.

### Cost

The sum of `EstimatedMinutes` is 84 minutes over 11 rows, with 10 builds, on server2. Each slice also needs authoring time. S3 is the largest (a migration and a job), at about 2-3 h. Anything beyond this table needs a stated reason.

## Risks

| Risk | Mitigation |
|---|---|
| A wrong protection rule deletes a pin that a recovery land needs | D-5 is the investigation's rule, already run on the canonical repository (18,996 refs, no incident). The S2 row tests cover each clause. Deletions carry an old-value check. |
| Ref deletion under the lease slows a land | S2 adds 2 processes per land. S3's step 2 runs with no waiter present and stops when one appears. |
| gc outside the lease races a land | Default prune grace (2 weeks), never `--prune=now`, and cruft packs. This is the investigation's run, which overlapped lands safely after 23:45. |
| G5 drops the ls-remote consistency check | The pin is the observation. The `CommitAsync(pin)` read and the ancestry check are kept. |
| G3 validator differs from git | The 30-row oracle test runs against real `check-ref-format`. Inputs are GUIDs and fixed suffixes. |

## Follow-ups (file as cards)

- **F-1:** apply the D-1 guard to the runner's non-inspection git calls.
- **F-2:** report `refs/antiphon/retirement/*` and `start-ref-probe` refs older than 24 h.
- **F-3:** G7 (`rev-list --left-right --count`) and G8 (single-registration read; overlaps CARD-1263).
- **F-4:** after S4 is deployed, compare one week of histograms against the §1 phase table before S5 lands. This is a measurement, not a gate.
- **F-5:** S3's ref sweep applies `LandRefRetention.IsDeletable` with `RetainedOperationsAsync` to each terminal task, so the sweep never deletes the latest or a Refused operation's recovery pins (Review 9e126982 D1).
