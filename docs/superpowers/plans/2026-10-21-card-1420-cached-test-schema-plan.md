# CARD-1420: cached test schema instead of 212 EF migrations per test process

Card: CARD-1420 (board Antiphon). Evidence: `docs/investigations/2026-10-21-antiphon-tests-time-profile.md`
(Q3, section 4). Base: `3ce6287715e5d4a1e30e67ed1315eacba7a4c995`.

**Goal.** A DB-backed `Antiphon.Tests` process currently pays about 22.8 s between "Postgres ready"
and its first test. The cause is `MigrateAsync` over 212 migrations, followed by template creation.
After this change the first process on a host with a given build migrates once and saves a SQL
snapshot keyed by that build. Later processes restore the snapshot into their own container
(target: a few seconds) and skip EF migrations entirely. Migration correctness stays covered by:

- the miss path,
- a dedicated parity test,
- the existing `*MigrationTests` classes, which still call `MigrateAsync` on their own databases.

Lane: checkpoints run on the default runner, **server2** (`GET /api/runner-defaults`:
`globalRunnerId=server2`). DB rows need that host's Docker, as every existing `TestDbFixture` row
does. No row pins an OS or a host. The cache is portable, and the Windows rc qualification uses the
same code with no extra configuration.

## Ground truth

| Card / brief assumes | Code at base does |
|---|---|
| Reuse a template "across test processes on the same Postgres" (`CREATE DATABASE … TEMPLATE`) | **No Postgres outlives a test process.** `TestDbOperations.CreateAsync` builds a fresh Testcontainers `postgres:16-alpine` per process (`tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:27-35`). Teardown disposes it (`DisposeCoreAsync`, `:276-294`). The template `antiphon_tmpl` already exists, but only **inside** one process (`ProtectTemplateAsync`, `:55-76`). So a cross-process cache has to live outside Postgres, as a file. |
| "Shared-Postgres helpers under tests/" create the databases | One helper owns all of it: `TestDbFixtureLifecycle` (`TestDbFixture.Lifecycle`, `TestDbFixture.cs:22`). Bootstrap order is create → start → `MigrateAsync(antiphon_test)` → clear pool → `CREATE DATABASE antiphon_tmpl TEMPLATE antiphon_test` + `IS_TEMPLATE` + `ALLOW_CONNECTIONS false` (`:296-357`). `CreateIsolatedSchemaAsync` clones `antiphon_tmpl` into `test_<guid>` under a per-process `SemaphoreSlim` (`:91-130`, `:221-250`). `TransactionalTestBase` uses `antiphon_test` directly. The investigation counted 382 caller files. |
| Other test databases | Four places start their own container and run Program's migration, and none use the fixture: `SmokeTests.cs:27`, `AgentTui/AgentTuiPersistenceTests.cs:433`, `Antiphon.Messaging.Tests/.../InboxConversationMigrationTests.cs:19` (Messaging schema) and `Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:29`. They are out of scope (one class each, or not `Antiphon.Tests`). |
| "Migrations assembly hash" | The migrations are in `Antiphon.Server` (`server/Antiphon.Server.csproj`, `server/Migrations/`, 212 migrations plus `AppDbContextModelSnapshot`). `BuildOptions` pins `MigrationsAssembly("Antiphon.Server")` (`TestDbFixtureLifecycle.cs:359-365`). |
| "~23 s is MigrateAsync" | Measured as one 22.8 s window (Postgres ready → first test). It contains migrate **and** template protection, and the split was never measured. Slice S3 measures it before and after. |
| Migrations are schema only | 27 migration files use `InsertData`/`Sql(`, and `20260925120000_EnablePgStatStatements` runs `CREATE EXTENSION pg_stat_statements`. The snapshot must therefore be **schema + data** (including `__EFMigrationsHistory`), not `--schema-only`. |
| Host startup in tests migrates again | `server/Program.cs:1044` runs `dbContext.Database.Migrate()` inside `AntiphonWebAppFactory` hosts. Against a clone this is only a version check, which `TestDbFixtureIsolationTests.MigrateAsync_on_a_clone_is_a_version_check` (`:107-117`) asserts. That check works only if the restored history rows are exact. |
| Production database safety | The fixture never reads `ConnectionStrings__DefaultConnection`. Its only connection string is the owned container's (`StartAsync`, `:37-44`). |
| Existing lifecycle contract | The C476 probe child asserts `migrate == 1` (`TestDbFixtureLazyInitializationTests.cs:111`) and injects a fault at `after-migrate` (`:126`). The unit-level tests drive `ControlledTestDbOperations` counters (`TestDbFixtureLifecycleTests.cs:8-110`). A cache hit skips migrate, so both contracts need an explicit decision (D-7). |

## Decisions

D-1. **Snapshot form: a plain-SQL `pg_dump` of the migrated `antiphon_test`, schema + data.** Use
`--no-owner --no-privileges`. It is produced *inside the owned container* through Testcontainers
`ExecAsync(["pg_dump", "-U", "test", "-d", "antiphon_test", …])`, so the host needs no local
`pg_dump`/`psql` (server2 has neither). Restore writes the bytes into the container (`CopyAsync`) and
runs `psql -v ON_ERROR_STOP=1 --single-transaction -U test -d antiphon_test -f <file>`. Success
requires both exit 0 and empty stderr error lines.

- Rejected: `PostgreSqlContainer.ExecScriptAsync`. It runs `psql` without `ON_ERROR_STOP`, so a
  failing statement still exits 0.
- Rejected: replaying the dump through Npgsql. A plain dump uses `COPY … FROM stdin`.
- Rejected: `pg_dump -Fc`/`pg_restore`. It saves nothing at this size, and a plain dump can be
  diffed when parity fails.

D-2. **Cache key** is the SHA-256 over these inputs, written as 32 hex characters:

- a format constant (`c1420-v1`)
- the bytes of the migrations assembly (`typeof(AppDbContext).Assembly.Location`)
- the image reference `postgres:16-alpine`
- the informational versions of `Microsoft.EntityFrameworkCore.Relational` and
  `Npgsql.EntityFrameworkCore.PostgreSQL`

Any server rebuild that changes the bytes is a miss. One process then pays the full migrate and
publishes the result. That costs at most one 23 s migrate per build, and the RC qualification's 84
launches share one build.

- Rejected: keying on migration IDs only. An edited migration body, or a changed helper or constant
  that a migration calls, would silently restore a stale schema.
- Rejected: keying on `IMigrator.GenerateScript()` output. It still builds every migration's
  operations in-process, an unmeasured share of the 22.8 s, and it is per process.

D-3. **Location.** `ANTIPHON_TEST_SCHEMA_CACHE` selects the cache:

| Value | Behaviour |
|---|---|
| unset | `Path.Combine(Path.GetTempPath(), "antiphon-test-schema-cache")`, host-wide, so all worktrees and processes of one user share it |
| `off` (ordinal ignore-case) | Disabled: always migrate, never read or write |
| anything else | Used as the root directory |

The cache never sits in the repo, so no gitignore change is needed. A relative path is resolved with
`Path.GetFullPath` and then logged.

D-4. **Entry layout and atomic publish.** An entry is `<root>/<key>/schema.sql` plus
`<root>/<key>/manifest.json`. The manifest holds `{format, key, serverAssemblySha256, image,
migrationIds[], sqlSha256, sqlBytes, createdUtc}`.

- Publish writes both files into `<root>/<key>.tmp-<guid>/`, flushes them, then calls
  `Directory.Move` to `<root>/<key>`. That rename is atomic on one volume.
- If the destination exists, another process won. The loser deletes its temp directory and uses its
  own just-migrated store.
- A read is a hit only when the manifest parses, its key and format match, and
  `SHA-256(schema.sql) == sqlSha256`. Anything else is a miss. The bad entry is renamed to
  `<key>.bad-<guid>` and then deleted on a best-effort basis, so a concurrent reader never sees a
  half-deleted entry under the live name.

D-5. **Concurrency is the atomic rename alone; there is no cross-process builder lock.** Concurrent
cold processes each migrate (today's cost) and one publish wins. The rc harness already refuses
native overlap (`nightly-tests-impl.ps1:944-952`), so duplicate cold builds are rare. A lock adds
stale-lock and timeout handling for no correctness gain. Rejected: a `FileShare.None` lock file with
a bounded wait.

D-6. **Bootstrap path.** The lifecycle becomes create → start → (**hit**: restore + verify | **miss**:
migrate, then capture + publish) → clear pool → protect template. Everything after that is unchanged.

- **Verify.** The ordered `"__EFMigrationsHistory"."MigrationId"` rows in the restored database must
  equal the manifest's `migrationIds`. Those must in turn equal the assembly's ordered migration IDs
  (`context.Database.GetMigrations()`, which uses reflection only and needs no model diff).
- **Restore failure** (psql non-zero, error output or a verify mismatch) runs `DROP DATABASE
  antiphon_test WITH (FORCE)` + `CREATE DATABASE antiphon_test`, discards the entry (D-4) and falls
  back to the migrate path in the same bootstrap. A cache defect therefore costs time, never a red
  run.
- **Capture failure** (pg_dump error, IO error) is logged and ignored, and the bootstrap still
  succeeds.
- **Logging.** Each bootstrap writes one line to stderr:
  `C1420 schema-cache <hit|miss|off|fallback> key=<key> ms=<elapsed>`.
- **Counters.** The lifecycle gains `Restore`, `Capture` and `CacheFallback` counters next to
  `Migrate`. The C476 probe payload gains `"restore"` and `"schemaSource"`.

D-7. **Existing contracts keep the migrate path explicitly.**

- `TestDbFixtureLifecycle(TestDbOperations? operations = null, TestSchemaCache? cache = null)`: with
  no operations, the default (production) lifecycle uses `TestSchemaCache.FromEnvironment()`. With
  injected operations and no cache, it uses a **disabled** cache, so the 33 existing
  `TestDbFixtureLifecycleTests` results keep `Migrate == 1` unchanged.
- `TestDbFixtureLazyInitializationTests.LaunchChildAsync` adds `ANTIPHON_TEST_SCHEMA_CACHE=off` to
  each child environment. The C476 child contract (`migrate == 1`, the `after-migrate` fault) stays a
  statement about the migrate path. The parent visibly states it rather than inheriting host state.
- Rejected: rewriting C476 assertions to `migrate + restore == 1`. That weakens an existing witness
  to fit the change.

D-8. **Production safety.** Restore and capture accept only an `OwnedTestDbContainer` whose `Real`
handle is non-null, and only `TestDbFixture.SharedDatabaseName` (`antiphon_test`) as target. The
operations throw for any other name, including `antiphon`. Nothing in the cache path reads a
connection string from configuration or the environment. The container's own Docker exec is the
only client.

D-9. **Cleanup.**

- **On every hit:** touch `manifest.json`'s last-write time.
- **After a publish (best-effort, errors swallowed):**
  - keep the 8 most recently touched entries;
  - delete entries untouched for 14 days;
  - delete `*.tmp-*` and `*.bad-*` directories older than 1 hour.
- **Manual reset:** delete the root directory. That is always safe, and the next process repopulates
  it.

The fixture never drops anything outside `<root>`. It validates that each entry name matches
`^[0-9a-f]{32}(\.tmp-[0-9a-f]{32}|\.bad-[0-9a-f]{32})?$` before deleting it.

D-10. **Migration correctness.** One dedicated parity test (S3) migrates a fresh database and
compares it with a restored one. Every cache miss still runs real `MigrateAsync`. So do the existing
`*MigrationTests` (about 30 files), on their own databases.

## Slices

### S1: `TestSchemaCache` store (filesystem only, no DB)

- New `tests/Antiphon.Tests/TestHelpers/TestSchemaCache.cs`, containing:
  - `FromEnvironment()` and `Disabled`
  - `ComputeKey(serverAssemblyBytes, image, efVersion, npgsqlVersion)`
  - `TryRead(key, out snapshot)`
  - `Publish(key, snapshot)`
  - `Prune(now)`
  - `Touch(key)`

  Implements D-2, D-3, D-4, D-5 and D-9. The clock (`TimeProvider`) and root are injectable.
- New `tests/Antiphon.Tests/TestHelpers/TestSchemaCacheTests.cs` (`[Category("Unit")]`, temp-dir
  per test, deletes it after), 8 tests:
  `Key_is_stable_for_identical_inputs`, `Key_changes_when_server_assembly_bytes_change`,
  `Published_entry_reads_back_verified`, `Concurrent_publishers_keep_one_winner_and_no_temp_dirs`,
  `Sql_hash_mismatch_is_a_miss_and_the_entry_is_discarded`,
  `Missing_manifest_or_sql_is_a_miss`, `Off_never_reads_or_writes`,
  `Prune_keeps_newest_entries_and_removes_stale_temp_and_bad_dirs`.
- Red commit: tests plus a `TestSchemaCache` whose members throw `NotImplementedException`. Green
  commit: the implementation.

### S2: lifecycle wiring (fake operations)

- `TestDbFixtureLifecycle.cs`:
  - new `TestDbOperations` virtuals `CaptureSnapshotAsync(container)`,
    `RestoreSnapshotAsync(container, snapshot)`, `ReadAppliedMigrationsAsync(connectionString)`,
    `GetAssemblyMigrations()` and `ResetSharedDatabaseAsync(maintenance)`;
  - the D-6 bootstrap branch;
  - the D-7 constructor;
  - the D-6 counters and stderr line.

  In S2 the real virtuals throw `NotImplementedException`. That is safe because the default lifecycle
  falls back to migrate (D-6), and S3 fills them in.
- `TestDbFixture.cs`: the probe payload gains `restore` and `schemaSource`.
- `TestDbFixtureLazyInitializationTests.cs`: children get `ANTIPHON_TEST_SCHEMA_CACHE=off` (D-7).
- New `tests/Antiphon.Tests/TestHelpers/TestDbFixtureSchemaCacheLifecycleTests.cs` uses
  `ControlledTestDbOperations` (extended with restore/capture counters and fault switches) and an S1
  cache on a temp root. It has 6 results:
  - `Cache_hit_restores_without_migrating` (`Migrate == 0`, `Restore == 1`, `Protect == 1`)
  - `Cache_miss_migrates_captures_and_publishes`
  - `Failed_restore_resets_and_migrates`, run as `[Arguments("restore-throws")]` and
    `[Arguments("history-mismatch")]`, asserting `ResetSharedDatabase == 1`, `Migrate == 1`, the
    entry discarded, and readiness reached
  - `Disabled_cache_migrates_and_never_publishes`
  - `Capture_failure_still_reaches_ready`
- Red commit: the new tests plus the virtual stubs and fake-op extensions, with the lifecycle not yet
  branching, so the counter assertions fail. Green commit: the wiring.

### S3: real snapshot through the owned container, parity, and measurement

- `TestDbFixtureLifecycle.cs`: real `CaptureSnapshotAsync` (`ExecAsync` pg_dump, exit and stderr
  checked) and `RestoreSnapshotAsync` (`CopyAsync` + psql with `ON_ERROR_STOP=1
  --single-transaction`). Also `ReadAppliedMigrationsAsync`, `GetAssemblyMigrations`, and the D-8
  guards.
- New `tests/Antiphon.Tests/TestHelpers/TestSchemaSnapshotTests.cs` (`[Category("Integration")]`,
  `[Category("Slow")]`, `[NotInParallel("C1420Snapshot")]`). Each test builds its own
  `TestDbFixtureLifecycle` instances, which own their containers, with a temp cache root. The shared
  fixture and the host cache are never used. 3 tests:
  - `Warm_cache_restore_matches_a_fresh_migration`. Lifecycle A misses and publishes. Lifecycle B
    hits (`Migrate == 0`, `Restore == 1`). B's applied history equals the assembly migrations, and
    the normalised `pg_dump --schema-only` plus `--data-only` of B's `antiphon_test` equals A's.
    Normalisation strips only `\restrict`/`\unrestrict` tokens, comments and blank lines.
  - `Parity_comparison_detects_a_dropped_table`. This negative control drops one table in B and
    asserts that the comparison reports exactly that difference, so the parity check cannot be
    vacuous.
  - `Restored_store_clones_and_program_migrate_is_a_version_check`. B serves
    `CreateIsolatedSchemaAsync`, and `MigrateAsync` on that clone executes no migration command (the
    `CountingCommandInterceptor` pattern from `TestDbFixtureIsolationTests:107-117`).
- Red commit: the S3 tests run against the S2 throwing stubs. `Warm_cache…` fails on `Migrate == 0`
  because the hit falls back.
- Docs: add one paragraph to `docs/testing-and-build.md` (shared Postgres / test DB section). It
  covers `ANTIPHON_TEST_SCHEMA_CACHE` (`unset`/`off`/path), the key, cleanup, and the stderr line.

### Red witnesses (run once at each red commit, through `scripts/build-slot.ps1`; not checkpoint rows)

A red row would make the checkpoint run's `validate` refuse the whole run (`row_failed`). Red
witnesses are therefore separate, stated-reason runs. Each one uses a precise
`--treenode-filter`, and the Code report records its counts and the expected assertion text.

| RW | At | Filter | Expect |
|---|---|---|---|
| RW-1 | S1 red commit | `/*/*/TestSchemaCacheTests/*` | 8 executed, 8 failed, each with `NotImplementedException` |
| RW-2 | S2 red commit | `/*/*/TestDbFixtureSchemaCacheLifecycleTests/*` | 6 executed, `Cache_hit_restores_without_migrating` fails on `Migrate` (expected 0, was 1); no result passes vacuously |
| RW-4 | CP-7 repair red commit | `/*/*/TestDbFixtureSchemaCacheLifecycleTests/*` | 8 executed, 2 failed: `Cache_hit_warms_the_migrator_while_restoring` (SchemaSource `fallback`, not `hit`) and `Failed_restore_resets_and_migrates(warm-throws)` (ResetSharedDatabase 0, not 1) |
| RW-3 | S3 red commit | `/*/*/TestSchemaSnapshotTests/Warm_cache_restore_matches_a_fresh_migration` | 1 executed, 1 failed on `Migrate == 0` (stderr shows `schema-cache fallback`) |

### Checkpoints

Lane: server2 (default runner). One build is shared by every row.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-c1420/` | measure-before | `/*/*/AgentTaskCheckInterpreterTests/the_board_hides_interpretation_rows_unless_they_are_asked_for` | V-6 | 1 executed, 0 failed; record TRX start → test start (migrate path) | 1 | 8 | ANTIPHON_TEST_SCHEMA_CACHE=off |
| CP-2 | S3 | CP-1 | store | `/*/*/TestSchemaCacheTests/*` | V-1 | all listed, 0 failed | 8 | 1 | |
| CP-3 | S3 | CP-1 | lifecycle-cache | `/*/*/TestDbFixtureSchemaCacheLifecycleTests/*` | V-2, V-7 | all listed, 0 failed (8 results after the CP-7 repair) | 8 | 1 | |
| CP-4 | S3 | CP-1 | lifecycle-regress | `/*/*/TestDbFixtureLifecycleTests/*` | V-3 | all listed, 0 failed | 33 | 1 | |
| CP-5 | S3 | CP-1 | lazy-init-regress | `/*/*/TestDbFixtureLazyInitializationTests/(A_*)|(Mixed_*)` | V-3 | parent probes only, 0 failed, 0 skipped | 7 | 6 | |
| CP-6 | S3 | CP-1 | snapshot | `/*/*/TestSchemaSnapshotTests/*` | V-4 | all listed, 0 failed | 3 | 4 | |
| CP-7 | S3 | CP-1 | isolation-on-cache | `/*/*/TestDbFixtureIsolationTests/*` | V-5 | all listed, 0 failed; stderr shows `schema-cache hit` or `miss` | 6 | 2 | |
| CP-8 | S3 | CP-1 | measure-after | `/*/*/AgentTaskCheckInterpreterTests/the_board_hides_interpretation_rows_unless_they_are_asked_for` | V-6 | 1 executed, 0 failed; stderr shows `schema-cache hit` (CP-7 populated the key with the same build); record TRX start → test start | 1 | 1 | |

Verification map:

- V-1: store semantics (D-2, D-3, D-4, D-5, D-9)
- V-2: bootstrap branch, fallback and the disabled path (D-6, D-7)
- V-3: existing lifecycle and C476 contracts unchanged (D-7)
- V-4: restored store parity and safety (D-1, D-8, D-10)
- V-5: real clones from a cache-built template
- V-7: a hit warms the EF model differ concurrently with the restore, so the first in-process `MigrateAsync` stays a version check (CP-7 repair)
- V-6: the per-launch saving, before and after on the same build and host

**Measurement report.** The Code report gives the CP-1 and CP-8 figures, TRX `Times/@start` → first
`startTime` and the stderr `ms=` value, and the difference. Expected outcome: CP-8's gap is at
least 15 s shorter than CP-1's. If the saving is under 10 s, the Code stage reports it as a finding
and does not tune it inside this card.

### Unlisted

- RW-1..RW-4 above (red witnesses, stated reason).
- CP-5b (documented non-receipt row, not run): the three `Child_*` methods of `TestDbFixtureLazyInitializationTests` are the CARD-0476 probe children. They run inside the parent probes' child processes, with the probe environment, and CP-5's parents assert their outcomes. Run directly they always skip (`SkipTestException`), and a skipped row blocks a clean receipt, so CP-5 selects the parents only.
- CP-7 repair diagnostics (Code task e05a8c49): throwaway `C1420DiagTests`/`C1420Diag2Tests` (never committed), runs of `TestDbFixtureIsolationTests` hit vs off, and RW-4 itself, each through `scripts/build-slot.ps1`.
- Nothing else. `Antiphon.Tests` is not run in full; CP-4, CP-5 and CP-7 are the regression scope
  for the touched fixture.

### Cost

Ordinary floor: 24 minutes (sum of `EstimatedMinutes`). Add about 15 minutes for the three red
witnesses (each a filtered run on the same isolated build output, rebuilt at its red commit) and
authoring time.

## Out of scope / follow-ups

- The other self-owned containers (`SmokeTests`, `AgentTuiPersistenceTests:433`, Messaging, E2E).
  Each could call the same cache later if measured to matter.
- Per-clone `CREATE DATABASE … TEMPLATE` cost (not measured; investigation section 2).
- The bare-`[NotInParallel]` global-sweep suites (investigation T3).
