# CARD-1844: HangfirePostgreSqlStorageTests, one run (2026-11-18)

Verify task `31c87f48`. Read-only on source. Master read: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (`origin/master`, fetched this run, ancestor of HEAD). No SQL against the desktop database. No restart, deploy, or volume change. Container `antiphon-postgres` and volume `antiphon_pgdata` were not present on this daemon and were not touched.

## Verdict

`HangfirePostgreSqlStorageTests` ran once on this server2 runner and passed: **22 total, 22 passed, 0 failed, 0 skipped**, TUnit duration **40s 684ms**, exit 0. It used a throwaway Testcontainers database on the nested dockerd, not production Postgres. The container was gone after the process exited.

The move still pins PostgreSQL **16.14** (Debian `16.14-1.pgdg13+1`), not the floating `postgres:16` tag. That tag's content on this runner is already **16.16**. The deploy script accepts the 16.14 image only as `postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`. The readable tag `postgres:16.14` is refused as `image-floating`.

## The run

Class: `HangfirePostgreSqlStorageTests` at `tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs:355`. Category Integration, `[NotInParallel]`. Filter: `/*/*/HangfirePostgreSqlStorageTests/*`. The two prose pins in `HangfireLeaseDocumentationTests` (`:1337`) are a different class and were not in this filter.

Load before the build was `4.83 11.18 8.79`. Load before the test was `14.84 16.09 11.59` (1-minute under 16). Both went through `scripts/build-slot.ps1`. `ANTIPHON_LINUX_COMPILER_POLICY=0`, `-p:UseAppHost=false`, native default output, no `bin-*` directory present.

| Step | Lease | Result |
|---|---|---|
| `dotnet build tests/Antiphon.Tests -m:1 -p:UseAppHost=false` | `6db67727-dc3e-4a32-bab0-ccf25f23e8f9`, waited 0s, held 227s | Build succeeded. 0 errors, 2173 warnings. Elapsed `00:03:45.29` |
| `dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/HangfirePostgreSqlStorageTests/*' --timeout 570s --output Detailed` | `bfd8be2a-01e2-4ede-b4ca-07364768a76e`, waited 0s, held 43s | Exit 0. Wall `2026-11-18T06:02:14Z` to `2026-11-18T06:02:58Z` |

TUnit v1.44.0, .NET 9.0.23. Summary line: `total: 22 / failed: 0 / succeeded: 22 / skipped: 0 / duration: 40s 684ms`.

Every method passed:

| Method | Time |
|---|---|
| Recurring_job_survives_a_new_storage_instance_without_duplicating | 1s 340ms |
| Disabled_census_stays_absent_on_a_new_storage_instance | 970ms |
| Disabled_residue_stays_absent_on_a_new_storage_instance | 839ms |
| Disabled_watchdog_stays_absent_on_a_new_storage_instance | 1s 104ms |
| Disabled_slot_reconcile_stays_absent_on_a_new_storage_instance | 989ms |
| Disabled_retire_stays_absent_on_a_new_storage_instance | 950ms |
| A_second_worker_lease_is_refused_until_the_first_is_released | 131ms |
| Worker_lease_stays_held_until_hosted_services_finish_stopping | 272ms |
| Worker_lease_is_reacquired_after_its_backend_is_terminated | 300ms |
| Worker_lease_refuses_to_continue_when_another_worker_holds_the_lock | 258ms |
| Worker_lease_stops_the_host_when_another_worker_holds_the_lock | 475ms |
| Worker_lease_ignores_another_database_holder_and_stops_for_its_own | 1s 662ms |
| Worker_lease_treats_a_reused_pid_with_another_start_as_another_worker | 309ms |
| Worker_lease_reclaims_its_own_backend_when_its_connection_is_gone | 309ms |
| Worker_lease_reclaims_a_new_backend_it_lost_right_after_taking_the_lock | 420ms |
| Worker_lease_sets_a_sixty_second_crash_expiry | 241ms |
| Worker_lease_keeps_its_own_backend_when_the_check_query_fails | 291ms |
| Worker_lease_bounds_the_unlock_command_timeout | 189ms |
| Authentication_failure_names_authentication_and_keeps_the_inner_exception | 176ms |
| Privilege_failure_names_privilege_and_keeps_the_inner_exception | 262ms |
| Missing_database_names_the_missing_database_and_keeps_the_inner_exception | 87ms |
| Recurring_job_type_is_rewritten_and_an_enqueued_row_is_left_alone | 937ms |

No product, fixture, or environment failure.

### Where the database came from

`TestDbOperations.PostgresImage` is `postgres:16-alpine` (`tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:27`). `CreateIsolatedSchemaAsync` starts one Testcontainers Postgres for the assembly and clones a schema per test. This run hit a baked image instead of migrating: stderr `C1420 schema-cache image key=84f61209628d457bde91297ca976bdb8 ms=10137` (`TestDbFixtureLifecycle.cs:669`). That image is `antiphon-test-pg:84f61209628d457bde91297ca976bdb8`, parent `sha256:6a2e324ebe59af6031c4c1cd96c1d5072c2ebc36563d2196365e43b8297c062d`, which is the local `postgres:16-alpine`.

Testcontainers created container `b5932a56340f` on nested dockerd 27.5.1 (`unix:///var/run/docker.sock`, containerized Debian 12). Database name in the readiness check was `antiphon_test`. The log stopped that container at testcontainers time `00:00:28.12`. After exit, `docker ps -a --filter id=b5932a56340f` was empty. Ryuk was not enabled; owner-sweep removes the labelled container (`TestDbContainerOwnership.cs:12-16`). Two older exited containers (`unruffled_johnson`, `c1609rda1b88c089c722bb7-metadata`) were left alone. Nothing named `antiphon-postgres` existed.

This runner can run the class. It needs the nested Docker socket and the already-cached `postgres:16-alpine` (or its baked child). It does not need a Postgres reachable on 5432 or 17280.

## Version pin

Operator rule 2026-11-18: migrate to the same PostgreSQL as the desktop, and upgrade the major later. Desktop statement in the brief: image `postgres:16`, image id `sha256:f40a5645a21f34c966c46a283e6b7fd5f4a7d4fc1768a5842f34b38423e90c8f`, server 16.14 (`Debian 16.14-1.pgdg13+1`). That image id was not present on this runner. The binary below was executed here.

| Image | What this runner executed | Identity |
|---|---|---|
| `postgres:16.14` (same manifest list as `postgres:16.14-trixie`, Hub last updated 2026-08-05) | `postgres (PostgreSQL) 16.14 (Debian 16.14-1.pgdg13+1)` | Manifest `sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`. Local image id after pull `sha256:8a4b045886180c64f518ca6a4b0791b15fb997f3b3b619bd5ee141914668db55`. Throwaway `card1844-pg1614-ver` removed |
| `postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` | same `16.14 (Debian 16.14-1.pgdg13+1)`, same image id | Script-legal form. Throwaway `card1844-pg16pin-ver` removed |
| Local tag `postgres:16` | `postgres (PostgreSQL) 16.16 (Debian 16.16-1.pgdg13+1)` | Image id `sha256:6b7038c60456795649816e6ad91b015284a75af3bca7d236b7c6124b56a1d9a3`, repo digest `sha256:dd2bf3ce2dc83a81674cf7f217fcfd8a5009a4b325dda5b843bfee82a06cff6f`, created 2026-11-12. Throwaway `card1844-pg16-ver` removed. Hub's floating `16` moved to 16.16 on 2026-11-15 |
| `postgres:16-alpine` | `postgres (PostgreSQL) 16.15` (no Debian package line) | Image id `sha256:6a2e324ebe59af6031c4c1cd96c1d5072c2ebc36563d2196365e43b8297c062d`, repo digest `sha256:0cefaabd882e42bcb2a2f3dc56d3ce4770560ba62fd7f197f1a2e008fd5731ab`. Throwaway `card1844-pg16a-ver` removed |

Pin for the move: **`postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`**. That is 16.14 on pgdg13 (Debian 13 / trixie, not Alpine). `postgres@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` is the same digest and also matches the script.

`Test-Server2PostgresImage` (`scripts/deploy-server2-postgres.ps1:71-77`) accepts only `postgres:16@sha256:<64 hex>` or `postgres@sha256:<64 hex>`. It returns `image-alpine` for any alpine reference and `image-floating` for anything else, including `postgres:16.14@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`. A pin of the local `postgres:16` digest `dd2bf3ce…` would pass the regex and install **16.16**.

### What 16.16 and alpine differ by

Both Debian images are major 16, pgdg13, glibc. 16.16 is two minor releases newer than 16.14 (`16.14` to `16.15` to `16.16`). The CARD-1974/1975 review measured its fingerprint SQL on `postgres:16.16` (`docs/superpowers/reviews/2026-11-17-card-1974-1975-review-r2.md:7`). Using that image for the move is a minor upgrade.

`postgres:16-alpine` here is 16.15 and musl, not the Debian pgdg13 package. The CARD-1920 plan review records that glibc permutes non-option tokens and musl does not (`docs/superpowers/reviews/2026-11-17-card-1920-plan-review.md:83`). The Hangfire class ran on that alpine lineage via the baked image. The CARD-1844 plan keeps tests on `postgres:16-alpine` (`011a6349a`, "What stays unchanged", and D-2). The operator rule says throwaway drills and test clusters use the same 16.14 Debian image. Those two instructions disagree. This pass did not change the fixture.

### Compose settings (not queried from a database)

Desktop `docker-compose.dev.yml:17-40`: image `postgres:16` (floating), command `shared_preload_libraries=pg_stat_statements`, `pg_stat_statements.track=top`, `pg_stat_statements.max=5000`, database and user `antiphon`, port `17280:5432`, volume `pgdata`, container name `antiphon-postgres`, no memory or CPU limit.

CARD-1406 `docker-compose.server2-postgres.yml:11-37`: same three postgres `-c` settings, same database and user names, image from `ANTIPHON_POSTGRES_IMAGE`, password and bind from the env file, port `5432`, volume name `antiphon-server2-postgres_pgdata`, `mem_limit: 4g`, `cpus: 4.0` (a ceiling, not a reservation; no `cpuset`).

Extensions other than the preloaded `pg_stat_statements` are **UNOBSERVED**. Neither compose file installs more, and this task did not query a database.

The later major upgrade is already CARD-1988 (Backlog). No second card was filed.

## CARD-1844 acceptance

Card is InProgress. Description is a plan request (revisions 19). The plan text is commit `011a6349a7b0f0d730387c2a523e23a924b18277` on `feat/card-task-17db3443`. That file is not on master `e229fb4d`.

| Item | State | Evidence |
|---|---|---|
| Plan answers topology, dependents, network, cutover shape, slice order | DONE as text, unlanded | `011a6349a` |
| S1 AppHost owner sentence and `Database endpoint {Host}:{Port}` log | NOT DONE | `Antiphon.AppHost/Program.cs:19-25` is still the external-container comment. The required sentence is absent |
| S2 `DatabaseEndpoint.ps1` and `-DatabaseProbeOnly` | NOT DONE | `scripts/lib/DatabaseEndpoint.ps1` is absent on this tree |
| S3 docker-stack / bootstrap contract and `test-card1844-postgres-contract.ps1` | NOT DONE | that witness is absent. `docs/docker-stack.md` has no CARD-1844 owner subsection |
| CP-1 / CP-2 / CP-3 | NOT DONE | the three witness scripts are absent, so they were not run |
| `HangfirePostgreSqlStorageTests` (Review 85da972b pending item, not a plan checkpoint) | DONE | this run, 22/22 |
| Windows PowerShell 5.1 parse of the S2 scripts | UNOBSERVED | those scripts are not on this tree, and this host has no `powershell.exe` |
| Positive controls | UNOBSERVED | not in this brief |
| Empty CARD-1406 cluster | NOT DONE | TCP to `100.93.77.126:5432`, `192.168.4.34:5432`, and `127.0.0.1:5432` was connection refused |
| Host dockerd data-root free space | UNOBSERVED | `docker info` here is nested dockerd (`name=7c9c24dac62d`, root `/var/lib/docker`, "containerized"). The LV this container sees is 705G, 425G used, **252G free, 63%** on `/`, `/work`, and `/var/lib/docker`, which meets a 20 GiB gate on that filesystem |
| CPU isolation beyond `cpus: 4.0` | NOT DONE | `docker-compose.server2-postgres.yml:21-22` |
| Cutover window, scratch drill on server2, backup retarget | NOT DONE | no listener on 5432. Desktop dump timings were not remeasured |
| F-1 / F-2 repair completeness | UNOBSERVED by this task | `scripts/cutover-postgres-server2.ps1:613` carries the CARD-1974 comment that `pg_sequences` has no `is_called`. The cutover witnesses were not run |
| Do not touch `antiphon_pgdata` | DONE for this task | no production container or volume operation |

Hangfire on the desktop stays InMemory until configuration sets `PostgreSql` (`server/Application/Settings/HangfireSettings.cs:23-33`). This green class does not by itself land S1–S3.

## Decision

Use `postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` for the empty cluster and for any throwaway drill. Do not use the floating `postgres:16` tag or digest `dd2bf3ce…` (16.16). Do not pass `postgres:16.14@…` to `deploy-server2-postgres.ps1` until that script accepts it. Leave the major upgrade on CARD-1988 until after the 16.14 move is stable.

--- next stage ---
next: decide
handoff: Operator pins the empty CARD-1406 cluster to postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b (16.14 pgdg13). The readable tag postgres:16.14 is refused by deploy-server2-postgres.ps1:75. Floating postgres:16 on this runner is 16.16. HangfirePostgreSqlStorageTests is 22/22 and does not land S1-S3. Whether test and drill clusters leave postgres:16-alpine is the remaining image decision; CARD-1988 stays the later major upgrade.
artifact: docs/investigations/2026-11-18-card-1844-hangfire-run.md
