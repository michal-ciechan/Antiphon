# CARD-1743 Review round 1 (task f442a78f): Hangfire jobs in Postgres

Verdict: NOT CLEAN. Two blockers (B-1 stale durable registrations for disabled jobs, B-2 the production storage bootstrap has no committed witness). Seven advisories go to cards and do not hold the land.

Subject `origin/feat/card-task-933d9118` tip `b2733b00ccec9a7ca69f14bcf2145cdc881a3d33` (Code task `933d9118-59c7-40e1-932f-4c089b88f09c`, Grok). Base `edb2c20156afa4da503b1b29fb83f1f953987bd5`. Red commit `c37da744b30113df7ad0dd7ab34830365c5a555c`. Read-only review; the subject branch was not checked out, rebased, committed to or pushed. `origin/master` was `8b75f315467f7b25c021ec1c0299c04e7398c0b8` at review time (the brief named `c2965dc7b1a3`; master advanced). `git merge-tree --write-tree origin/master HEAD` exit 0, tree `6e767744dfcb0c370b4c601de086b319642f1b39`, no conflicts. `scripts/check-evidence-diff.ps1 -Repository . -BaseRef edb2c201… -HeadRef b2733b00…`: commits=2 entries=0 violations=0, exit 0 (the Code task's exit 2 was a shallow-clone refusal, not a violation).

## Verification executed

Checkpoint run `20261107-180818-ed22` (one build, `tests/Antiphon.Tests -> bin-c1743-r1/`, source `b2733b00…` state=clean buildSource=verified, plan `.antiphon/card-1743-review-r1-plan.md`, ignored):

| Row | Filter | Executed | Passed | Failed | Wall |
|---|---|---:|---:|---:|---|
| CP-1 | `/*/*/HangfireStorageSelectionTests/*` | 5 | 5 | 0 | 18.4 s (build 313.7 s) |
| CP-2 | `/*/*/HangfirePostgreSqlStorageTests/*` | 3 | 3 | 0 | 40.4 s |
| CP-3 guard batch | `(PackageVersionPinContractTests*)\|(DotnetToolManifestContractTests*)\|(C1729LayeringGuardTests*)\|(HangfireStartupSafetyTests*)\|(WorktreeResidueRegistrationTests*)\|(ExpectationWatchdogJobTests*)\|(RunnerSlotRulesTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(ReleaseBranchModelDocumentationTests*)\|(RunnerPushCredentialDocsTests*)\|(GrokRunnerImageContractTests*)/*` | 215 | 214 | 1 | 66.1 s |

Run wall 7m06s. The CP-3 red is `WorktreeResidueRegistrationTests.C459_ScheduledWorkerExecutesBothLanes` (`run.FinishedAt should not be null`, 4.1 s). Unlisted run, reason: isolate that red. `scripts/build-slot.ps1 -Label c1743-r1-isolate-C459-residue -- dotnet tests/Antiphon.Tests/bin-c1743-r1/Antiphon.Tests.dll --treenode-filter "/*/*/WorktreeResidueRegistrationTests/*"` (lease `75257605`, held 33 s): 3/3 passed, 31.8 s. See A-6. `validate --evidence … --rows CP-1,CP-2 --expected-source-sha b2733b00…` exit 2 `report_failed` (the red row blocks whole-run validation), so `reviewedSourceClean` is false.

The Unit lane was not rerun (brief). The six Code-task Unit failures (`RemoteScriptContractTests` x5, `TestTimingOperationLabelTests` x1) are not caused by this diff: the range touches Hangfire configuration, settings, `server/Antiphon.Server.csproj` and three docs; those classes read `scripts/c590-remote.sh`, the compose files and test call-site timing labels, none of which this range changes (blobs equal at base per the Code report).

Production-path probe (no slot; reflection over the kept build, scratchpad script): `AddHangfire`-style `ConfigureStorage` with `Storage=PostgreSql` yields `Hangfire.PostgreSql.PostgreSqlStorage` with `SchemaName` honoured, `PrepareSchemaIfNecessary=false`, `StartupConnectionMaxRetries=0`, `AllowDegradedModeWithoutStorage=false`, factory `NpgsqlConnectionFactory` bound to the configured host/port, no password in a failure; default settings yield `InMemoryStorage` with an 8-day expiry.

## Blockers

### B-1 Durable storage keeps disabled recurring jobs alive
- Where: `server/Program.cs:1181-1182` (residue, `if (residue.Enabled)` only), `:1200-1202` (expectation watchdog, `ShouldRun` only), `:1205-1209` (runner slot-reconcile and retire, `if (phoneHome.Enabled)` only). Only census got the remove arm (`HangfireConfiguration.RegisterCensusJob`).
- Failure: with `Hangfire:Storage=PostgreSql` a job registered on one start persists in the `hangfire` schema; turning its flag off and restarting no longer removes it. `WorktreeResidueJob`, `RunnerSlotReconcileJob` and `RunnerRetireJob` (via `WorktreeResidueSweepService`, `RunnerSlotService.ReconcilePendingReleasesAsync`, `RunnerRetireService`) have no `Enabled` re-check, so a disabled residue sweep keeps releasing orphaned Launch rows and persisting runs, and disabled phone-home reconcile/retire keep auditing seats. `ExpectationWatchdogJob` self-gates, but its row still fires a no-op every minute.
- Why: in-memory storage purged every registration at process exit; the durable store removes that implicit purge. The brief's check "jobs removed when disabled" fails for four of five conditional registrations. This is a reachable fail-open in the new mode and the card's single-worker contract depends on flags being authoritative.
- Fix: mirror `RegisterCensusJob` for residue, watchdog, slot-reconcile and retire (`RemoveIfExists` on the disabled arm, same ids), and add a witness per job on PostgreSql storage: register with the flag on, re-register with it off through the production registration helper, assert absence on a new storage instance (`HangfirePostgreSqlStorageTests` pattern) plus the InMemory unit twin.

### B-2 The production storage bootstrap has no committed witness
- Where: `HangfireConfiguration.ConfigureStorage` PostgreSql branch, the only path `server/Program.cs:989-993` calls. Every committed witness drives `CreateStorage`, a test-only twin Program never calls.
- Failure: a wiring defect in the production branch (bootstrapper options, schema name, factory, degraded mode) passes all eight witnesses. The probe above shows the branch is correct today; a probe is not a committed witness and the production-factory rule (`docs/testing-and-build.md#os-branch-witnesses`) says a seam cannot replace it.
- Why: `CreateStorage` was added to keep tests off the DI container; it duplicates the production wiring instead of exercising it.
- Fix: one Unit witness that builds a `ServiceCollection`, calls `AddHangfire(config => ConfigureStorage(config, pg, "Host=127.0.0.1;Port=1;…"))`, resolves `JobStorage`, asserts `PostgreSqlStorage` with `SchemaName`, `PrepareSchemaIfNecessary=false`, `StartupConnectionMaxRetries=0`, `AllowDegradedModeWithoutStorage=false` and that resolution opened no connection (an unreachable port proves it). Then either make `CreateStorage` delegate to the same bootstrap or drop it.

## Advisories (cards; no land hold)

- A-1 `HangfireWorkerLease.Dispose` is registered on `ApplicationStopping` (`Program.cs:1229`), which fires before hosted services stop; the lock is released up to `ShutdownTimeout` (15 s) before `BackgroundJobServer` drains, so a successor can acquire it while this worker still runs. Register on `ApplicationStopped`.
- A-2 The lease is a plain session lock on one non-pooled connection with no keepalive. A Postgres restart drops the lock while this worker keeps running, so the guard silently vanishes; a hard host crash leaves the lock held until the server's TCP keepalive expires and the successor is refused. Document the recovery (`pg_terminate_backend` of the lock holder), consider `Keepalive=` and a periodic re-check.
- A-3 `IsConnectivityFailure` folds `PostgresException` (28P01 auth failure, 42501 privilege, 3D000 missing database) and any `InvalidOperationException` not starting with "Hangfire " into "not reachable" and drops the inner exception. The message never carries the connection string, so attach the inner exception or log its `SqlState`.
- A-4 Dependency notes: `Hangfire.PostgreSql` 1.21.1 is LGPLv3 (Hangfire.Core already is); transitive `Dapper` 2.0.123, `Dapper.AOT` 1.0.48 (ships `Dapper.AOT.Analyzers.dll` and `Microsoft.SqlServer.TransactSql.ScriptDom.dll`, which now run in the server compile), `Microsoft.CSharp` 4.7.0; the `Npgsql` floor 6.0.11 resolves to the repo's 9.0.3 through `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4, no conflict. The repo has no central package management or lock files; `PackageVersionPinContractTests` (no floating versions) passes. No doc line records the new dependency or that the schema install needs CREATE on the database (the app role owns the auto-created database; compose uses the superuser).
- A-5 `Desktop_configuration_does_not_turn_durable_storage_on` pins prose in `docs/docker-stack.md`; keep the configuration pins, move the prose pins to a documentation test or drop them.
- A-6 `C459_ScheduledWorkerExecutesBothLanes` is a co-scheduling flake: red in the 13-class batch, green alone. It writes process-global Hangfire state (`GlobalConfiguration.Configuration.UseStorage/UseActivator`) shared with `HangfireStartupSafetyTests`' web host in the same process. Not caused by this range (the only diff code that process executed is the InMemory branch, identical to base). Debug card.
- A-7 Recurring rows now store job type names (`…WorktreeResidueJob, Antiphon.Server`). `AddOrUpdate` at every start rewrites recurring definitions, so renames self-heal for recurring jobs; enqueued or scheduled instances of a renamed job stay Failed in the `hangfire` schema. Note in docs.

## Verified clean

- Default path: `Program` boot with default settings resolves `InMemoryStorage` (HangfireStartupSafetyTests 13/13 in CP-3); `PreparePostgreSqlStorage` and the lease sit behind `UsesPostgreSql`; `RegisterCensusJob`'s remove arm is harmless on InMemory; desktop `appsettings.json`, AppHost and root compose carry no `Storage`.
- Connection string: `ConnectionStrings:DefaultConnection`, the EF string, never logged (test and probe).
- Schema: separate `hangfire` schema, `public` refused by the validator; `Install.v3.sql` creates the schema if absent and tables `schema, counter, hash, job, state, jobqueue, list, server, set, jobparameter, lock`; versioned additive scripts in serializable transactions, no advisory lock inside the installer. No EF migration in the range; the additive rule holds.
- Ordering: `dbContext.Database.Migrate()` (`Program.cs:1124`) completes before `PreparePostgreSqlStorage` (`:1174`) and the lease (`:1176`), serially in one scope; `pg_try_advisory_lock(int,int)` never blocks and its two-key space is distinct from the repo's `hashtext` bigint keys. Session lock on a dedicated connection: released when the backend sees the socket close (process exit or kill).
- Fail-closed at startup: unreachable database throws from the explicit probe before any job registration; the storage constructor opens no connection (`PrepareSchemaIfNecessary=false`), so nothing degrades to in-memory.
- Idempotency: `AddOrUpdate` keyed by job id; two registrations then a new storage instance show one row (CP-2). Worker count 1 and queues unchanged. PostgreSql defaults: poll 15 s, invisibility 30 min, native transactions.
- Red-first: `c37da744b` compiles with stubs and the Code report shows 6/8 witnesses red by assertion; each witness has a named single-line mutant (census remove arm, prepare emptied, lock check removed, `CreateStorage` forced in-memory, doc phrase). The eight PCs stay pending for method-scoped SourceLanding Mutation.

## Residue

Kept build outputs (red run; the tool's `clean` verb requests manual removal and the brief forbids recursive force deletion): `/work/worktrees/task-f442a78f/tests/Antiphon.Tests/bin-c1743-r1/` and `/work/worktrees/task-f442a78f/.antiphon/checkpoints/20261107-180818-ed22/builds/bin-c1743-r1`. Scratch probes under the session scratchpad. Nothing deployed, restarted or run on the desktop.
