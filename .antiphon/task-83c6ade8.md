# CARD-1743 review repair (round 2) — task 83c6ade8

Landing owner: Code task 83c6ade8 (this branch). Predecessor implementation owner 933d9118 stays at `b2733b00ccec9a7ca69f14bcf2145cdc881a3d33`. Branch `feat/card-task-83c6ade8`. Worktree `/work/worktrees/task-83c6ade8`. Desktop checkout was not used. Restart: none. No land, deploy, or stack restart.

This report commit, when it is the branch tip, changes no product source. Tested implementation is `fbd3ef653599ffc8e9e79f67f44f022daebdd8bd`.

## Outcome

B-1 and B-2 from review `f442a78f` are fixed on the branch. A disabled residue, expectation-watchdog, slot-reconcile, or retire flag removes that recurring row at registration, including under PostgreSql. `CreateStorage` resolves `JobStorage` through `AddHangfire` + `ConfigureStorage`. One unit witness checks that production bootstrap path on an unreachable port.

## SHAs

- Repair base: `b2733b00ccec9a7ca69f14bcf2145cdc881a3d33`
- Red (witnesses only, before the remove arms): `7983e827b6c787345d95d6857b4292069b54d1b2`
- Fix and tested implementation: `fbd3ef653599ffc8e9e79f67f44f022daebdd8bd`
- Diff vs base: 3 files, +230 / −17. `HangfireConfiguration.cs`, `Program.cs`, `HangfireDurableStorageTests.cs`. No EF migration. `tests/test-execution-policy.json` unchanged. policyHash remains `82b4f28a4197c7aba6c28678e3113afa2b343ccbd84c905e0e7ea790b87c1d1a`.

## Registration decisions

Recurring registration still runs only when `Hangfire:ServerEnabled` is true (`Program.cs` around the existing block).

| Job | Decision |
|---|---|
| census `antiphon:zombie-census` | `RegisterCensusJob` already removed the row when disabled. Unchanged. |
| residue `antiphon:worktree-residue` | `RegisterWorktreeResidueJob` now removes the row when `WorktreeResidueSettings.Enabled` is false. Program calls it on both arms. |
| retention `antiphon:runner-worktree-retention` | Already removed when disabled. Unchanged. |
| published-branch deletion | Already removed when `DeleteSourceBranchAfterPublication` is false. Unchanged. |
| desktop legacy release `antiphon:desktop-legacy-release` | Already removed when off. Unchanged. |
| expectation watchdog `antiphon:expectation-watchdog` | `RegisterExpectationWatchdogJob` adds only when `ExpectationWatchdogJob.ShouldRun` (server enabled and watchdog enabled); otherwise `RemoveIfExists`. |
| slot reconcile `antiphon:runner-slot-reconcile` | `RegisterRunnerSlotReconcileJob` removes the row when phone-home is disabled. The enabled arm still triggers once at register. |
| retire `antiphon:runner-retire` | `RegisterRunnerRetireJob` removes the row when phone-home is disabled. |
| keep-six `antiphon:keep-six` | Stays unconditional `AddOrUpdateKeepSixJob`. The per-agent toggle is inside the job. There is no server-level off switch, so no remove arm was added. |

No job-body self-gate was added. `RemoveIfExists` drops the recurring definition. It does not cancel an execution that is already queued.

`CreateStorage` is the test twin of Program: PostgreSql still calls `PreparePostgreSqlStorage` first, then both storages resolve `JobStorage` through `services.AddHangfire(config => ConfigureStorage(...))`. The `ServiceProvider` is retained in a static `ConcurrentBag` because disposing it would dispose the `JobStorage` the caller still uses. In-memory no longer constructs `InMemoryStorage` on the side.

## Red, then green

At `7983e827b6`, enabled-only registration, before the remove arms:

- In-memory `Disabled_*` filter: total 5, failed 4, census passed. Lease `5c9ef4c4-d3d4-4bbd-8e58-a772ad37cb95`, waited=0s, held=16s.
- PostgreSql same filter: total 5, failed 4, census passed. Lease `8bf5cf71-a260-4843-9636-47034d1693a0`, waited=0s, held=35s.
- Red build lease `8b30abdd-d311-44cf-bfbf-6511ff0ed733`, waited=0s, held=302s, 0 errors.

Fix build `5c40978e-c9e2-48a5-be1e-84d2c95dec88` failed to compile (internal `PostgreSqlStorage.Options`, and `ConnectionString` is an `NpgsqlConnectionStringBuilder`). Rebuild `480bf55b-2a8d-4429-a270-7ff7b0242435`, waited=0s, held=150s, 0 errors. The witness uses non-public `Options` reflection and asserts `.Host`, `.Port`, and `.Database` so a failure does not print the password.

On `fbd3ef653599ffc8e9e79f67f44f022daebdd8bd`, before the local mutant:

- `HangfireStorageSelectionTests`: total 10, failed 0, duration 5.762s. Lease `47c70cdc-aca3-4aee-a172-d5dcd1d73668`, waited=0s, held=9s.
- `HangfirePostgreSqlStorageTests`: total 7, failed 0, duration 39.801s. Lease `b0a6997e-dea8-4ecd-81bb-764c6a24faaf`, waited=0s, held=44s.

Those two classes were not rerun after they passed.

## Local bootstrap mutant

`PrepareSchemaIfNecessary` was set to `true` only in the working tree. Lease `bb3be76b-28d6-409c-b6f7-734e7b729955`, waited=0s, held=187s, 0 errors. The bootstrap test then failed 1/1 (lease `cfc7ca86-02aa-431f-a6a9-f0d39ddfb140`, waited=0s, held=7s): `PostgreSqlStorage` tried to connect to 127.0.0.1:1 during construction. The file was restored with `git checkout -- server/Infrastructure/Agents/HangfireConfiguration.cs`. The mutant was not committed. The tree was clean at `fbd3ef65` afterward, with `PrepareSchemaIfNecessary = false`.

Restore rebuild so later `--no-build` runs match that commit: lease `ea5a7d8c-9c74-468c-9e6c-365b1c889ee1`, waited=0s, held=80s, 0 errors, elapsed 00:01:19.34. `UseAppHost=false`, `TEMP=/tmp`, native default output, no `OutputPath`.

## Guard batch

One leased run after the restore build. Filter:

`/*/*/(HangfireStartupSafetyTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(DockerStackContractTests*)|(DockerStackDocumentationTests*)|(WorktreeResidueRegistrationTests*)|(ExpectationWatchdogJobTests*)|(RunnerSlotRulesTests*)/*`

Lease `c80685e8-33f4-4b85-90bd-aac9a7c8b0fe`, waited=0s, held=199s. total 235, failed 0, succeeded 235, skipped 0, duration 3m 11s 599ms. Exit 0. `C459_ScheduledWorkerExecutesBothLanes` passed inside the batch, so it was not rerun alone.

An earlier filter that omitted the leading `/*/*/` selected nothing (lease `4525676e-c20f-41d5-b329-0e3bc904cd7f`, waited=0s, held=12s, total 0, exit 8). That run is not evidence. A full-path `|` union from earlier in the session was killed before it could be treated as a result; it is not a count.

The full Unit lane was not rerun. The repair brief's closed list is the touched classes plus this guard batch, inside a 75-minute budget. The lane at `b2733b00ccec9a7ca69f14bcf2145cdc881a3d33` remains the prior baseline: total 6824, passed 6776, skipped 42, failed 6, all six inherited (`RemoteScriptContractTests` and `TestTimingOperationLabelTests`).

## Invariants

- Default storage stays in-memory for eight days: `Default_settings_stay_on_in_memory_storage_for_eight_days` passed in the unit class of 10.
- Desktop configuration does not turn durable storage on: `Desktop_configuration_does_not_turn_durable_storage_on` passed in that same class.
- Unknown storage and schema `public` stay rejected: `Validator_rejects_an_unknown_storage_and_the_public_schema` passed in that class.
- Unreachable PostgreSql still refuses startup, and the message does not include the connection string: `Unreachable_postgresql_storage_refuses_startup` and `PostgreSql_bootstrap_resolves_storage_and_refuses_an_unreachable_database`.
- Bootstrap witness: schema `hangfire_card1743`, `PrepareSchemaIfNecessary=false`, `StartupConnectionMaxRetries=0`, `AllowDegradedModeWithoutStorage=false`, factory host `127.0.0.1`, port `1`, database `antiphon`. Resolution itself does not connect. `PreparePostgreSqlStorage` throws the unreachable refusal.
- Advisory lock `1743/1` still refuses a second worker: `A_second_worker_lease_is_refused_until_the_first_is_released` passed in the PostgreSql class of 7.
- No numbered V-n or R-n in this repair brief. No checkpoint table on this follow-up. No docs edit. `docs/docker-stack.md` was not chased for line numbers because `DockerStackDocumentationTests` passed inside the 235.

## Evidence and merge

`scripts/check-evidence-diff.ps1 -BaseRef b2733b00ccec9a7ca69f14bcf2145cdc881a3d33 -HeadRef HEAD`: exit 0, commits=2, entries=0, violations=0. `git rev-parse --is-shallow-repository` is `false`.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `e87c841ae80c40560ab70c6f9767799d92a79c56`. `origin/master` is `99f3d328cfe1aaaaf211278f24e43b9d11b61438`. Not merged.

## Positive controls pending Mutation

Method-scoped. Zero formal PC-n. None discharged by this round.

- `Default_settings_stay_on_in_memory_storage_for_eight_days`
- `Validator_rejects_an_unknown_storage_and_the_public_schema`
- `Desktop_configuration_does_not_turn_durable_storage_on`
- `Disabled_census_removes_a_previously_registered_job`
- `Disabled_residue_removes_a_previously_registered_job`
- `Disabled_watchdog_removes_a_previously_registered_job`
- `Disabled_slot_reconcile_removes_a_previously_registered_job`
- `Disabled_retire_removes_a_previously_registered_job`
- `Unreachable_postgresql_storage_refuses_startup`
- `PostgreSql_bootstrap_resolves_storage_and_refuses_an_unreachable_database`
- `Recurring_job_survives_a_new_storage_instance_without_duplicating`
- `Disabled_census_stays_absent_on_a_new_storage_instance`
- `Disabled_residue_stays_absent_on_a_new_storage_instance`
- `Disabled_watchdog_stays_absent_on_a_new_storage_instance`
- `Disabled_slot_reconcile_stays_absent_on_a_new_storage_instance`
- `Disabled_retire_stays_absent_on_a_new_storage_instance`
- `A_second_worker_lease_is_refused_until_the_first_is_released`

The four new in-memory removal witnesses and the four new durable removal witnesses already failed at the red SHA before the fix. The bootstrap witness failed under the local `PrepareSchemaIfNecessary = true` mutant and passed again on the restored commit (it is one of the 10). Mutation still owns a fresh method-scoped cycle for each row.
