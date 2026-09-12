# CARD-0476: lazy test database startup and restart preflight caching

Plan task: `22016441`. Inspected checkout: `2a19fb40bab147d23e678bad504f501a0e0e3dc6`.
Investigation: task `224b7084`, full Result read through `scripts/delegate.ps1 -Status`;
that investigation inspected `0c29c7b3`. The current implementation still supports
both bounded changes. Implementation and measured savings are pending.

## Ground truth

| Card assumption | Current source/evidence | Planning consequence |
|---|---|---|
| Every selection pays for PostgreSQL | `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs` constructs a static container and its assembly hook starts it, migrates `antiphon_test`, and creates/protects `antiphon_tmpl`. Historical fixed cost is approximately 24–37 seconds. | Defer container construction and the entire bootstrap until the first default-store consumer. Preserve the template and clone mechanism. |
| Lazy startup makes the whole Unit lane DB-free | `Application/SessionDeliveryProfileTests.cs` has five PostgreSQL-backed methods and remains `Category("Unit")`. | Promise zero bootstrap only for selections with no DB consumer. Keep this class and its category unchanged here. |
| The assembly hook only starts the DB | It first runs `LandQueueRaceWorker` when its marker is present, then exits the child with 0 or 1. Workers use a parent-owned connection through `BridgeQueueHarness`. | Extract the worker dispatch into an eager hook; removing the hook wholesale would break the race test or recursively run its parent. |
| Callers can all await initialization | Callers use synchronous `ConnectionString`, default `CreateDbContextOptions()`, instance `CreateDbContext()`, and asynchronous `CreateIsolatedSchemaAsync()`. Explicit connection strings also exist. | One shared initialization task must cover every default entry point. Explicit connections must bypass it. Bootstrap cannot call a default options/maintenance accessor that waits on itself. |
| Cloning needs redesign | `TestDbFixtureIsolationTests` already has six tests for migrated, empty, separate and disposable clones; `CloneLock` serializes template copying. Investigation reports roughly 140–180 ms per clone. | Retain clone locking, database naming, retry behavior and isolation. |
| Restart rows each need two shell launches | `Fixtures/RunnerRestart/RestartFixture.cs::Run` runs an AST preflight and then the actual entry script. Rows can call `Run` repeatedly. Historical health evidence is 143.23 seconds / 55 rows, not isolated preflight time. | Cache only successful structural validation; measure the portion actually removed. |
| Constructor hashing proves what will execute | The constructor verifies entry copy equality once. Its generated helper wrapper imports the live repository helper and fixture platform script by absolute path. | Hash actual execution files at `Run`, bind imports to fixture-owned byte copies, and prevent changes between validation and execution. Root names cannot be the cache identity. |
| Diagnostic coverage is a separate class | `RunnerRestartDiagnosticTests.cs` declares `partial class RunnerRestartHealthTests`. Compatibility is `RunnerRestartScriptCompatibilityTests`. | Run the Health class to include diagnostic methods; a `RunnerRestartDiagnosticTests` class filter selects nothing. |
| Transcript tests share this opportunity | Investigate found no analogous preflight in `TranscriptAdoptionSafetyTests`; its historical 71.21 seconds / 44 rows is not removable shell-startup cost. | Close this cache hypothesis and exclude that class from the implementation scope. |
| Git parallelism is a ready speed fix | Investigation identified four plausible isolated Git candidates but no repeated two-wide timing/receipt/residue proof. Existing `ProcessSpawnLimit` and its guard require one-wide execution. | Keep a separately authorized trial only; no default concurrency change. The old 78-minute ceiling is not a current saving estimate. |

## Decisions

- **D-1 — separate TestDesign.** Lifecycle concurrency, child dispatch and a safety
  preflight need executable behavioral and negative controls. This plan fixes the
  design and acceptance obligations; TestDesign adds the exact V/R/PC inventory
  before Code. No user decision blocks these two slices.
- **D-2 — one assembly-owned lazy DB lifecycle.** Keep the existing fixture API and
  per-process ownership. Use a concrete internal lifecycle object with one shared
  task, rather than changing hundreds of consumers. A new fast assembly, shared
  cross-process container/template, warm test host and in-memory provider are
  larger changes with different fidelity/lifecycle risks and are deferred.
- **D-3 — eager safety and worker dispatch remain eager.** Preserve
  `ProductionRunnerGuard` and `PtyBackendEnvGuard`, their assembly attributes, and
  the worker's marker, exit codes, owned-root checks and parent-database contract.
  Laziness applies solely to the default database. Do not change test categories,
  PTY delivery settings, native project sequencing or process limiter width.
- **D-4 — faults are terminal for that DB lifecycle.** All callers observe the same
  startup failure; do not automatically retry bootstrap or hand out a partly
  migrated database. Clean up partially created resources and require a new test
  process for a fresh attempt. Keep the existing narrowly scoped clone retries.
- **D-5 — cache structural approval in memory, per test process.** Key success by
  resolved shell identity, exact input bytes and validator identity. Do not use a
  path-only, timestamp-only or persistent cache. No health result, configuration,
  trace, clock, platform state or execution subprocess is shared. This is a
  narrowly authorized test-fixture lifetime, not production global state.
- **D-6 — validate the files that execute.** Copy the production helper and fixture
  platform into each existing unique fixture root, retaining exact source bytes;
  use a deterministic relative-import wrapper. This small ownership change avoids
  importing live source that may change after preflight. Constructor equality alone
  and hashing repository HEAD are insufficient. No production restart script change
  is needed.
- **D-7 — report measured, scoped benefit.** Lazy startup helps DB-free exact-method
  and scoped runs, including DB-free mutation arms; the full Unit lane still starts
  PostgreSQL. Cache savings exclude every real execution and direct `Script` call.
  Neither historical total is a speed-up promise. Reduced dispatch policy remains
  inactive; the current testing owner's Unit-plus-affected-classes recipe applies.

## S1 — lazy database lifecycle

Primary file: `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs`.
Keep the lifecycle implementation here or in one adjacent concrete
`TestDbFixtureLifecycle.cs`; do not create a general test-host framework.

1. Move marker dispatch to a separately named `[Before(Assembly)]` method, preferably
   beside `LandQueueRaceWorker`. It returns immediately outside worker mode. In worker
   mode it awaits the existing `RunAsync`, then exits with the existing verdict.
   Preserve both eager environment guards as independent assembly hooks. Do not make
   their execution depend on database access or on the lazy task completing. Do not
   introduce a new hook-order assumption; if dispatch needs a guard earlier, use an
   explicit idempotent guard application rather than relying on declaration order.
2. Replace the eagerly constructed container with one lazily created lifecycle task
   that returns a ready state containing the owned container and raw shared/maintenance
   connection strings. A small synchronized gate creates that task once and prevents
   initialization after disposal starts. Start its async work on the thread pool once
   so synchronous callers cannot block a captured caller synchronization context;
   internal awaits must also avoid caller-context dependence. Never use `.Result` or
   `.Wait()` to wrap failures. The synchronous adapter uses `GetAwaiter().GetResult()`
   on this same task; async callers await it. Keep an unannotated `InitializeAsync`
   forwarding method if useful, but no DB-starting assembly hook.
3. The bootstrap body constructs and starts PostgreSQL, obtains the raw connection
   string directly from the container, builds options with that explicit string,
   migrates the shared DB, closes its migration context, then creates and protects
   the template. Private maintenance-string/options builders accept a raw string;
   they never call `ConnectionString`, default options, or the lazy initializer.
   Publish readiness only after both `IS_TEMPLATE true` and `ALLOW_CONNECTIONS false`
   succeed. This preserves an empty template before shared-store consumers write.
4. Route access as follows:

   | API | Initialization behavior |
   |---|---|
   | `ConnectionString` | Synchronously await readiness, then return ready shared string. |
   | `MaintenanceConnectionString` | Same ready state; derive/read its maintenance string. |
   | `CreateDbContextOptions(null)` | Use the shared synchronous path. |
   | `CreateDbContextOptions(explicitString)` | Build options directly, even before any default DB exists. |
   | `new TestDbFixture()` | No container construction, Docker contact or startup. |
   | Instance `CreateDbContext()` | Default options path, therefore readiness before returning. |
   | `CreateIsolatedSchemaAsync()` | Await readiness before taking `CloneLock` and before the clone-attempt cleanup region. |
   | Clone disposal | Use the already-ready owning state; cleanup must never start a new container. |

5. Move bootstrap's existing `NpgsqlConnection.ClearAllPools()` to clearing only the
   shared database pool used by migration. Eager startup previously ran before tests;
   lazy startup can now overlap explicit-connection consumers. Clearing every process
   pool would unnecessarily disturb those consumers. Keep maintenance connections
   unpooled, source/template backend termination scoped by database, and clone pool
   clearing scoped to that clone. This is a necessary consequence of moving startup.
6. Define teardown explicitly. Never requested: no-op, with no `.Value`/task creation.
   Starting: await the existing attempt, then release any resulting owned container.
   Ready: dispose it once after normal test teardown. Failed: bootstrap owns cleanup
   of the partial container; assembly teardown must not start, retry or double-dispose
   it. Repeated teardown shares one disposal task. Calls that begin after disposal
   starts fail clearly. Do not promise disposal concurrent with arbitrary live test
   contexts; assembly teardown remains after consumers.
7. Bootstrap catches failure from construction/start/migrate/template protection and
   attempts owned-container disposal before exposing the fault. Preserve the original
   startup exception; surface cleanup failure as additional evidence without replacing
   it or swallowing a resource leak. In `CreateIsolatedSchemaAsync`, a bootstrap fault
   must not enter `DropClonedDatabaseAsync` and recursively request the same failed
   initialization. Once clone creation is attempted, retain partial-clone cleanup;
   preserve the clone error if its cleanup also fails.

Tests/files: extend `TestDbFixtureIsolationTests.cs`; add a small
`TestDbFixtureLifecycleTests.cs` for instance-owned controlled lifecycle cases and
`TestDbFixtureLazyInitializationTests.cs` for fresh-process behavioral probes.
Use controlled factories only at the startup/disposal boundary; actual database
acceptance still uses Testcontainers and EF. Do not reset the shared singleton
between concurrent tests. Fresh-process probes launch only their exact method in
the already built assembly, use a bounded child budget and producer-owned evidence
root, and cannot recursively launch themselves. Add the process limiter to the
probe class and its existing census in `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs`.

Use `LandQueueRaceWorker.cs` and its existing parent test for the worker regression;
add only non-secret lifecycle/guard evidence to its receipt if needed. Never write a
connection string, credential or environment dump to evidence. `BridgeQueueHarness`
and `TransactionalTestBase` retain their public behavior.

### S1 acceptance obligations for TestDesign

- **DB-A:** A fresh DB-free exact-method execution proves zero container construction
  and zero start attempts, including assembly teardown; constructing the fixture and
  using explicit options do not initialize it. Assert eager production-runner and PTY
  guard state in this same DB-free child. A container census/log absence alone is not
  sufficient evidence; observe the actual factory/start boundary without triggering it.
- **DB-B:** Concurrent first default access through sync connection/options/context
  and async clone entry points shares one startup/migration/template operation and
  cannot return readiness early. Controlled gated tests cover each entry point as the
  first caller and the sync-context deadlock case; one fresh-process integration probe
  exercises mixed callers against real PostgreSQL. Avoid four redundant cold containers.
- **DB-C:** All six existing isolation methods pass: separate database/no SearchPath;
  migrated and empty; rows isolated across two clones; dispose drops clone; four
  concurrent clones are unique/empty/migrated; `MigrateAsync` remains a version check.
  Also verify shared DB migration and template protection. Retain existing bounds.
- **DB-D:** Fail construction/start/migration/template protection through a narrow
  test seam. All waiters receive the original failure, later access does not retry,
  no clone/drop path runs for bootstrap failure, and owned disposal occurs once.
  Cover never-started, in-flight and repeated teardown deterministically. Include one
  real started-container failure/cleanup capstone with owned-container absence evidence;
  use controlled state tests for the rest instead of repeatedly booting Docker.
- **DB-E:** `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents`
  still gets two successful child receipts for the same queued-row identity, one durable
  keyed row, and no adapter inputs. Retain child exits/assembly MVID evidence and prove
  no child private DB startup. A worker failure still exits 1 without normal test execution.
- **DB-F:** The five `SessionDeliveryProfileTests` execute real queries under the
  unchanged Unit category. An explicit-connection consumer stays usable while default
  bootstrap initializes; bootstrap only clears/terminates connections it owns.

## S2 — content- and shell-keyed successful preflight cache

Primary file: `tests/Antiphon.SessionRunner.Tests/Fixtures/RunnerRestart/RestartFixture.cs`.
Add one nearby concrete `RestartPreflightCache.cs` if extraction makes the cache's
success/failure behavior independently testable. Its assembly-owned instance is
shared by fixture instances; tests can construct separate instances rather than
clearing shared state.

1. Keep the entry copy byte-for-byte identical to `scripts/restart-session-runner.ps1`.
   Copy the real helper under a distinct filename in the same fixture `scripts`
   directory and copy `Fixtures/RunnerRestart/platform.ps1` there too. The entry's
   expected `session-runner-restart-health.ps1` remains a fixture wrapper that imports
   the copied production helper followed by the copied platform, using fixed relative
   paths. Verify all three source/copy byte equalities at creation. The helper currently
   has no `$PSScriptRoot` dependency; preserve its production function bodies unchanged.
   Each new fixture snapshots current source; an existing fixture executes its own
   snapshot, never a live repository import.
2. Extract the existing AST validator into stable text with entry/helper paths passed
   as arguments, so temporary root interpolation does not alter validator identity.
   Preserve the entry/core allowlists, dynamic/member-call checks and inert-helper
   import requirement. Explicitly reject parser errors and missing expected core;
   malformed input cannot become a cached success. Check the wrapper against its
   exact generated contract before any import; include the platform bytes in the
   input identity. Do not generalize this into a new PowerShell sandbox.
3. Resolve the requested shell to the exact executable to launch, without a preliminary
   shell process. Identity includes normalized absolute executable path, executable
   SHA-256 and version, plus the adjacent PowerShell engine assembly fingerprint
   where applicable. Fail if resolution or required fingerprinting is unavailable;
   never substitute the other shell. Use that resolved executable for both validation
   and real execution. `pwsh.exe` and `powershell.exe` therefore require independent
   approvals. Re-resolve/re-fingerprint at each `Run`; no filename-only identity.
4. Build an immutable key from that shell identity plus SHA-256 of the actual copied
   entry, copied production helper, copied platform, exact wrapper, and stable validator
   bytes/schema identity. Hash bytes without newline/BOM normalization. Do not include
   root/config/arguments in the structural key: identical script inputs in different
   row roots should hit. Validator edits automatically invalidate approval; do not rely
   solely on a manually remembered version bump.
5. Read/hash the execution files at every `Run`, including on cache hits. Hold read
   handles that deny writes/deletion to the fixture input files from hashing through
   preflight and real child exit on Windows, releasing them on every outcome. This
   binds the validation and execution to one byte snapshot. Recheck the chosen shell
   identity immediately before launch and refuse drift. If the file-sharing discipline
   cannot be honored, fail before execution; a constructor hash or a post-execution
   check cannot substitute. Tests must confirm both PowerShell versions can read these
   held files. Concurrent `Run` on the same mutable fixture is not a new supported API.
6. Use a success-key set and a small async gate around lookup/validation/publication.
   The gate is released before the actual restart execution, and coordinates concurrent
   misses so identical inputs perform one successful preflight. Insert a key only when
   the validator process exits zero and the bound inputs are still valid. A validation
   failure, exception, timeout, cancellation or changed/missing input grants no approval;
   later calls may validate again. No fault task or negative verdict remains cached.
7. A key miss runs the existing real-shell AST check against the owned input files.
   Safe changed content may pass a new validation; unsafe changed content must fail
   before the real restart child starts or any fixture control operation occurs. A
   wrapper contract mismatch refuses outright. A cache hit skips only the validator
   child. Every accepted `Run` still writes current config, launches the real entry,
   reads its output/trace, checks exit/final JSON agreement and records its evidence.
   Keep the 45-second owned-child timeout/kill-and-await behavior.
8. Keep `Script` and `DecodeCapturedMilestones` executions uncached. Compatibility's
   direct script parsing and HTTP tests still execute as before. Preserve fresh per-row
   roots, clocks, state/PID sentinels, configuration and control traces. Add bounded
   per-invocation cache/preflight/execution timing and child-count evidence, using a
   sequence inside each fixture's evidence directory so repeated `Run` records are
   retained. This is measurement support in the existing evidence path, not a profiler.

Tests/files: add `RunnerRestartPreflightCacheTests.cs` (controlled, Unit) and
`RunnerRestartPreflightSafetyTests.cs` (real shells, Integration and existing
`ParallelLimiter<ProcessSpawnLimit>`). Add the latter to the exact-population census
in `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimitTests.cs`. Existing regression
coverage lives in `RunnerRestartHealthTests.cs`, `RunnerRestartDiagnosticTests.cs`
(same Health class) and `RunnerRestartScriptCompatibilityTests.cs`. TestDesign may
place new cases in an existing class to avoid an unnecessary fixture API seam, but
must preserve the separate controlled and real-process proof obligations.

### S2 acceptance obligations for TestDesign

- **PF-A:** Same inputs in two different roots and repeated `Run` calls reuse one
  successful preflight; every call still creates a real execution child. Changed
  config/mode produces the corresponding different result/trace, proving no outcome
  cache. Count validator and execution starts separately.
- **PF-B:** Changing each key dimension independently invalidates success: entry,
  real helper, platform, wrapper, validator and shell identity. Include same-length
  byte edits with restored timestamps. Controlled cases test the full key matrix;
  real-shell capstones use each of `pwsh.exe` and `powershell.exe` and demonstrate
  separate first misses followed by hits.
- **PF-C:** Warm the cache, change the fixture's copied entry or helper to include a
  forbidden but harmless command targeting only fixture-owned evidence, then run
  again. Validation rejects it, the real execution child count does not advance,
  trace/control counts do not change and sentinels survive. Never inject a live
  stop/restart command as a positive control. Include malformed syntax and non-inert
  helper imports; the actual copied entry, not only repository source, is the subject.
- **PF-D:** Failed/thrown/timed-out validation never primes the cache; repair followed
  by retry validates. Concurrent same-key callers share successful validation without
  sharing their execution/state. A missing input, wrapper tampering or a write/replacement
  attempt at the validation-to-execution boundary cannot execute unvalidated bytes.
- **PF-E:** All Health rows, including diagnostic partial-class methods and repeated
  wait-only calls, retain their existing outcomes/deadlines/side-effect assertions.
  Both-shell compatibility and real local HTTP deadline cases still run. Actual
  counts come from fresh TRX, not the historical 55-row figure or source row counts.

## S3 — scoped verification and measurement handoff

Update `docs/testing-and-build.md` only after implementation to describe lazy startup,
the unchanged PostgreSQL-dependent Unit class, eager hooks and the measured benefit.
Keep broad/nightly policy, `Slow` classification, `LandVerifyFilter` and production
verifier settings unchanged. Do not modify generated `docs/cards/` files.

TestDesign must append `## Verification design` with V/R/PC-to-method mappings,
expected expanded rows, negative assertion targets and evidence paths. In particular,
controls must detect eager DB startup, duplicate or recursive initialization, premature
readiness, missed partial cleanup, lost worker dispatch, missing cache key components,
cached failures and skipped real execution. Assign controls to actual method-scoped
selectors; build/fixture errors or zero-test runs do not prove detection. Mutation
must rebuild restored source and prove fresh red/green assembly identity.

Coverage-to-class and known rerun selectors (run from the implementation worktree):

| Coverage | Project / selector |
|---|---|
| Current foreground policy; profile DB usage; controlled lifecycle tests | `Antiphon.Tests`: `/*/*/*/*[Category=Unit]` (verify the five profile methods are present). |
| Cloned stores and lazy real-process acceptance | `Antiphon.Tests`: `/*/*/(TestDbFixtureIsolationTests*)|(TestDbFixtureLazyInitializationTests*)/*` (second class is proposed). |
| Worker dispatch and parent-owned DB | `Antiphon.Tests`: `/*/*/AgentTaskLandNotificationRecoveryTests/C467_V09_KeyedQueueRacesAndDistinctEvents`. |
| Eager guard behavior in normal execution | `Antiphon.Tests`: `/*/*/(ProductionRunnerGuardTests*)|(PtyBackendEnvGuardTests*)/*`; DB-free child acceptance additionally proves these do not depend on bootstrap. |
| Controlled cache and limiter census | `Antiphon.SessionRunner.Tests`: `/*/*/*/*[Category=Unit]`. |
| Real preflight safety, Health/diagnostics and both-shell compatibility | `Antiphon.SessionRunner.Tests`: `/*/*/(RunnerRestartHealthTests*)|(RunnerRestartScriptCompatibilityTests*)|(RunnerRestartPreflightSafetyTests*)/*` (last class is proposed). |

Build once per affected project into the producer-owned `bin-c476/` output using
`dotnet build tests/<Project> --property:OutputPath=bin-c476/ --nologo`; execute via:

```powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c476/ -- --treenode-filter '/*/*/(TestDbFixtureIsolationTests*)|(TestDbFixtureLazyInitializationTests*)/*' --report-trx --report-trx-filename db.trx --results-directory .antiphon/c476-db-01
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c476/ -- --treenode-filter '/*/*/(RunnerRestartHealthTests*)|(RunnerRestartScriptCompatibilityTests*)|(RunnerRestartPreflightSafetyTests*)/*' --report-trx --report-trx-filename restart.trx --results-directory .antiphon/c476-restart-01
```

Use a fresh empty results directory for every invocation and inspect executed
class/method names, outcomes, both shell argument rows and nonzero counts. These
are proposed post-Code commands, not runs performed during Plan. Apply the same
runner form to every row above; no namespace/full-assembly default. Run projects
sequentially, particularly never co-scheduling `Antiphon.Tests` with
`Antiphon.Agents.Pty.Tests` or other native/PTY work. New process tests carry the
assembly-local limiter. This card requires no live runner, app restart or deploy.

Measurement is bounded to the two optimizations:

1. Record base and implementation SHAs, build output/MVID, shell versions, filter,
   fresh TRX/evidence location, host-load notes, outer wall, discovery and test-body
   time. Record DB construction/start/migration/template/teardown separately where
   instrumentation is present; do not label the residual build/host time as discovery.
2. For DB-free savings, use the existing exact
   `/*/*/ProcessSpawnLimitTests/Caps_concurrent_process_spawning_tests_at_one` selector
   in fresh `Antiphon.Tests` processes on both revisions. Run three paired samples
   in alternating order using already built outputs; retain raw values and median/range.
   The new DB-free probe proves zero resource attempts; the existing method gives a
   comparable unchanged workload. One DB-using cold acceptance run checks that the
   cost moved to first access and correctness remains. Full Unit is not the zero-DB
   benchmark and no blanket 35-second saving is claimed for it.
3. For cache attribution, use three paired fresh-process runs of Health's existing
   `Explicit_60_second_wait_can_be_continued_without_restart` method, which calls
   `Run` twice, then one paired whole-Health run to measure cross-row reuse. Preserve
   raw preflight versus execution costs and child counts where available; baseline
   uses two shell children per `Run`. In the changed whole selection expect one
   successful preflight per distinct content/shell/validator key, plus one real child
   per accepted `Run`. Direct `Script` calls are separately accounted and unchanged.
4. Report savings only for identical executed rows, separating summed TRX body time
   from outer wall. No timing threshold belongs in ordinary correctness tests.
   If hashing/snapshot overhead cancels the saved shell launches, report that result
   and return the cache slice for adjustment rather than claim a historical total
   as recovered time. Keep the independently useful lazy-DB slice separable.

## Separately gated Git-only trial methodology

This section does not authorize trial execution or implement/default-enable parallelism.
The caller may commission it separately after S1/S2; it is not an acceptance gate for
these two changes. Do not modify either process limiter, its guard or lane attributes
to widen execution as part of S1/S2. Census additions for the new process test classes
remain required.

If commissioned, start with only `AgentTaskLandIdentityMatrixTests`,
`AgentTaskLandRemovalMatrixTests`, `AgentTaskLandCheckpointMatrixTests` and
`AgentTaskLandCleanupSafetyTests`. Re-audit current static/environment state, owned
Git roots/remotes/config, DB clone/lease ownership and cleanup, including disposal
exceptions that could skip filesystem cleanup. Use two isolated Git-only shards of
the same committed build with each existing per-process limiter still one-wide;
compare the same partition executed sequentially versus concurrently. Account for
each shard's separate DB bootstrap in total wall time. No concurrent native/PTY
project, FakeClaude or unrelated process-spawning test lane is permitted.

Freeze the method/argument partition and retain fresh TRX proving identical full row
coverage, no duplicates/omissions, and actual cross-shard child overlap of at most two
Git test bodies. Use at least three paired baseline/trial runs under quiet conditions
and three under representative normal non-test load, alternating order. Preserve
timeouts, current receipt/refusal assertions and the unresolved pre-verifier stall
signal from CARD-0475; do not exclude a stalled row to manufacture a speed-up.

Record total and per-shard wall/body times, command timelines, clone wait/contention,
receipt/queue identities and assertions, child exit/reaping evidence, owned database,
worktree and fixture-root inventories before/after, and cleanup exceptions. Any missing
receipt, new timeout/refusal, unreaped owned child, unexplained residue or DB contention
failure stops the trial and is compared with an isolated one-wide rerun. A passing trial
needs identical green coverage, no unexplained residue and repeatable measured wall
improvement in both load regimes. It supports a later default-policy decision only;
it does not enable two-wide execution or prove other landing classes safe.

## Closed and deferred

- PTY missing-pump hypothesis: closed as superseded by CARD-0475's live transcript
  pump and exact destination receipt assertions, per Investigate. No PTY edit or
  timeout change in this plan.
- Transcript-adoption preflight-cache hypothesis: closed as unsupported. Preserve
  its real adoption coverage and refusal observation windows.
- Discovery variability, mutation warm-host/template reuse, remaining Git measurement
  gaps and broad profiling: deferred. This plan's small timing records do not reopen
  those projects. The historical mutation saving ceiling is not validated here.

## Delivery

Plan only: this document is the sole intended change. No build/test or speed trial
was run in this stage. Land the Plan commit through the normal delegation landing
operation before dispatching TestDesign so the next worktree contains the artifact.
Next stage: TestDesign, followed by Code for S1/S2 and the small S3 evidence/doc update.
