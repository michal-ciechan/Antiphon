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

## Verification design

TestDesign for the plan above (task `131d57ee`, 2026-09-12, worktree at `ba02210f`). The fix design is unchanged; this section adds the verification contract Code implements and Mutation executes. Measured on this machine while designing (three samples each, cold-ish host): bare `pwsh.exe -NoProfile -Command 'exit 0'` 3,700 / 2,675 / 2,469 ms; bare `powershell.exe` 2,337 / 1,567 / 1,921 ms; the fixture's current AST preflight driver (entry plus helper parse and the three AST loops, exit 0) 6,213 / 3,562 / 4,683 ms on pwsh and 2,785 / 4,001 / 3,258 ms on powershell. That preflight cost is the whole per-`Run` saving the cache can deliver; the real execution child stays. pwsh is 7.6.6, powershell is 5.1.19041.6456, Docker server 29.5.3.

Shell-identity facts Code must handle (S2 step 3): `where.exe pwsh.exe` resolves first to `C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.6.0_x64__8wekyb3d8bbwe\pwsh.exe` (MSIX package; the exe and its adjacent `System.Management.Automation.dll` are readable, SHA-256 and `VersionInfo` both succeed) and second to `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`, a zero-byte `ReparsePoint` app-execution alias whose bytes cannot be read (`ReadAllBytes` fails with "The file cannot be accessed by the system"). A child started as `pwsh.exe` reports `(Get-Process -Id $PID).Path` equal to the package exe. `C:\Program Files\PowerShell\7\pwsh.exe` does not exist here. `powershell.exe` has no adjacent engine assembly; its engine is the GAC `System.Management.Automation\v4.0_3.0.0.0__31bf3856ad364e35\System.Management.Automation.dll`. The resolver therefore walks PATH in order, takes the first candidate that is a regular, non-reparse, readable file, and the fixture launches that absolute path (never the bare name); a zero-byte or reparse candidate is skipped, and no readable candidate is a typed failure. Engine fingerprint: adjacent SMA when present (pwsh), else the GAC SMA for an exe under `System32\WindowsPowerShell` (powershell), else fail closed. V-25 and V-29 pin this.

### Inspection

- Read in full: `TestDbFixture.cs` (static container, assembly hooks, every accessor, clone/drop/terminate helpers, `IsolatedTestSchema`, `TransactionalTestBase`), `TestDbFixtureIsolationTests.cs` (six methods), `LandQueueRaceWorker.cs` (`RunAsync`, `RunPairAsync`, `Rendezvous`), `BridgeQueueHarness.CreateAsync` (the `options.ConnectionString ?? TestDbFixture.ConnectionString` short-circuit at line 82 is why an explicit worker never touches the default store), `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents` (plus the two `C488_*` methods that delegate to it), `ProductionRunnerGuard.cs` and `PtyBackendEnvGuard.cs` with their pin tests, `ProcessSpawnLimit.cs` and `ProcessSpawnLimitTests.cs` in both assemblies (Antiphon.Tests is a must-carry list; SessionRunner.Tests is an exact population), `SessionDeliveryProfileTests.cs` (five methods, all `CreateDbContextOptions()` default path, Unit), `TestClassificationPolicyTests.cs`, `TestLaneCategoryGuardTests.cs`, `tests/Shared/TestClassificationMetadata.cs` (`unregistered-marked` / `unmarked-registered` / `missing-reason`), both `slow-tests-allowlist.txt` files, `RestartFixture.cs` (constructor copy and hash, `Script`, `Process` with the 45 s kill, `Run` preflight and execution, `Trace`, `DecodeCapturedMilestones`), `platform.ps1`, `RunnerRestartHealthTests.cs` (43 rows), `RunnerRestartDiagnosticTests.cs` (12 rows in the same partial class), `RunnerRestartScriptCompatibilityTests.cs` (8 rows), `scripts/restart-session-runner.ps1`, and the function inventory of `scripts/session-runner-restart-health.ps1` (top level is five function definitions only; no `$PSScriptRoot` use).
- Consumer census in `Antiphon.Tests`: 67 files read `TestDbFixture.ConnectionString`, 5 read `MaintenanceConnectionString`, 252 call sites use `CreateDbContextOptions()` with no argument, 103 files call `CreateIsolatedSchemaAsync`, 5 classes take `[ClassDataSource<TestDbFixture>(Shared = SharedType.PerTestSession)]` (TUnit constructs the fixture; construction must stay inert), 1 class derives `TransactionalTestBase`, nothing references `InitializeAsync`/`DisposeAsync` by name.
- Missing setup recorded: there is no controlled seam at the container/start/migrate/protect boundary today (the container is a static field); Code adds an internal lifecycle type with an injectable factory, and the controlled class below owns instances of it. There is no fresh-process probe helper other than `LandQueueRaceWorker.RunPairAsync`; the lazy probe class reuses its shape (already built assembly location, exact `--treenode-filter`, env marker, producer-owned root under `.antiphon/acceptance/card-0476/`, bounded budget, `Kill(entireProcessTree: true)` on budget). There is no preflight-cache type; `RestartPreflightCache` and a `ShellIdentity` resolver are new. `RestartFixture` today exposes no child counters; it gains per-fixture `ValidatorChildStarts`, `ExecutionChildStarts`, `ScriptChildStarts` plus the evidence sequence records, and an optional constructor cache parameter so a test can own a cold cache instead of clearing the assembly one. The new Slow classes must be registered in each project's `slow-tests-allowlist.txt` with an adjacent reason (`C487_G059/G060` otherwise fail) and tagged Unit xor Integration.
- Boundaries, S1: first caller kind {sync `ConnectionString`, `MaintenanceConnectionString`, `CreateDbContextOptions()`, instance `CreateDbContext()`, `CreateIsolatedSchemaAsync`, explicit-string options, bare construction} x concurrency {single, 8-wide} x bootstrap outcome {ready, fault at construct / start / migrate / protect, cleanup also fails} x teardown timing {never requested, starting, ready, faulted, repeated, access after} x caller context {thread pool, single-threaded SynchronizationContext} x process shape {in-process controlled, fresh child with no consumer, fresh child with mixed consumers, worker child, worker failure}. Mapped: V-1..V-10, V-18 (controlled), V-11..V-15 (fresh process), V-16/V-17/V-19 (existing lanes). Excluded: fault injected during a clone `CREATE DATABASE` against real Postgres (controlled V-9 covers it; a real one needs a Postgres fault injector).
- Boundaries, S2: key dimension {entry, helper, platform, wrapper, validator, shell path, exe SHA, exe version, engine SHA} x edit shape {content change, same-length with restored timestamp, CRLF-vs-LF only, BOM added} x validator outcome {exit 0, exit 1, throw, timeout, canceled} x concurrency {single, 4 same-key, different-key during execution} x shell {pwsh, powershell} x tamper site {copied entry, copied helper, copied platform, wrapper, missing file, write attempt while bound} x tamper kind {forbidden command, member call, dynamic command, parser error, non-inert helper, missing core, core bypass}. Mapped: V-20..V-26 (controlled matrix), V-27..V-37 (real shells), V-38/V-39 (existing classes and census). Excluded: BOM added is a byte change like any other (covered by the content row); a tampered *repository* script (fixtures snapshot; the repo copy is not an input after creation, V-35 proves it).

### Delivery inventory

No session-input, queue, tracker or transcript delivery path changes in this card; there is no `UserPrompt` evidence to require and none is claimed. Three asynchronous handoffs do change and are inventoried so Review can see what each test proves and what it cannot.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| Lazy readiness (S1) | the one bootstrap task started by the first default consumer | every waiter on `ConnectionString`, `MaintenanceConnectionString`, default options, instance context, `CreateIsolatedSchemaAsync` | none; in-process task state, per test process | none by design (D-4): a fault is terminal, every later waiter gets the same fault, a new process is the retry | the ready connection string in the waiter's hands plus the seam counters (`Create`, `Start`, `Migrate`, `Protect` each 1); V-3 proves no waiter completes before `ALLOW_CONNECTIONS false` |
| Worker verdict (S1 step 1, unchanged contract, moved hook) | the eager marker hook in the child test process | the parent's `RunPairAsync` | the child's `<pid>.result.json` and `<pid>.absent` files under the parent-owned root, plus the child exit code | parent budget (100 s) kills and reaps the children and retains stdout/stderr logs | two receipts with the same `row`, the parent MVID, `inputs == 0`, the new `dbLifecycle == "never-requested"` field, exit 0 (V-14); a failure is exit 1 with no test results (V-15) |
| Preflight approval (S2 step 6) | the caller that validates on a miss | concurrent same-key callers, later fixtures in the same process | none; in-memory per test process (D-5) | none: a failed, thrown, timed-out or canceled validation leaves no entry and the next caller validates again (V-22) | the validator child count (one per distinct key) with an execution child per accepted `Run` (V-23, V-27, V-28) and the per-invocation evidence record naming `cached`, `validatorChildStarted`, `executionChildStarted` |

Substitutes declared: the fake factory in `TestDbFixtureLifecycleTests` stands in for Testcontainers, Npgsql and EF; it proves ordering, counting, fault and disposal semantics, not that Postgres accepts the statements (V-12, V-13 and the existing isolation methods close that gap against a real container). The fake validator in `RunnerRestartPreflightCacheTests` stands in for a real shell child; it proves key and gate semantics, not that the AST checks reject anything (V-30..V-32 run the real validator on both shells). Neither a seam counter nor a cache statistic alone is delivery acceptance: the accepting evidence for S1 is a consumer holding a working connection to a migrated store in a fresh process (V-12), and for S2 an accepted `Run` whose real child produced the configured outcome after a cache hit (V-27).

### Proves it works now

S1 controlled (new `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycleTests.cs`, `[Category("Unit")]`, hermetic, no limiter). Each test owns a `new TestDbFixtureLifecycle(fakeFactory)`; the fake exposes counters `Create`, `Start`, `Migrate`, `Protect`, `DisposeOwned`, `Drop`, `ClearedPools` (list of connection strings), gates (`TaskCompletionSource`) per stage, and a per-stage fault switch. Nothing in this class touches the static `TestDbFixture` singleton. Every wait in this class is bounded with `WaitAsync(TimeSpan.FromSeconds(30))` so a deadlock is a red assertion, not a hang.

- V-1: concurrent first default access shares one bootstrap | Unit | `Eight_concurrent_first_callers_share_one_bootstrap`: 8 callers on the thread pool (2 sync connection string, 1 maintenance, 2 default options, 1 instance context, 2 clone) start while the `Start` gate is held, then release | `Create == 1`, `Start == 1`, `Migrate == 1`, `Protect == 1`; all 8 results carry the same shared connection string; both clone names distinct.
- V-2: each entry point as the first caller | Unit | `Each_default_entry_point_initializes_exactly_once` with `[Arguments]` for `connection`, `maintenance`, `options`, `context`, `clone` | after the call `Create == 1`; a second call of the same kind leaves `Create == 1`; `maintenance` returns `Database=postgres;Pooling=false` derived from the ready string.
- V-3: readiness is not published early | Unit | `Readiness_waits_for_template_protection`: hold each stage gate in turn (after `Start`, after `Migrate`, after `IS_TEMPLATE`, before `ALLOW_CONNECTIONS`) | while any gate is held the sync and async waiters are `!IsCompleted` after a 200 ms observation; after the final gate releases all complete within the bound.
- V-4: sync adapter cannot deadlock a captured context | Unit | `Synchronous_access_completes_under_a_single_threaded_synchronization_context`: install a single-threaded pumping `SynchronizationContext` on the test thread, call `ConnectionString` synchronously from that thread while the fake bootstrap awaits a gate released by a thread-pool timer 100 ms later | the call returns the ready string within the 30 s bound; the bootstrap continuation ran on a thread-pool thread (assert `SynchronizationContext.Current is null` inside the fake).
- V-5: explicit connection strings bypass initialization | Unit | `Explicit_options_never_initialize`: `CreateDbContextOptions("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p")` and `new TestDbFixture()` on a fresh lifecycle | `Create == 0`; the options' connection string equals the explicit string; `IsRequested == false`.
- V-6: teardown state table | Unit | `Teardown_matrix` with `[Arguments]` `never-requested`, `starting`, `ready`, `faulted`, `repeated`, `access-after`, `clone-dispose-after` | never-requested: `Create == 0`, `DisposeOwned == 0`, teardown completes; starting: teardown awaits the held gate then `DisposeOwned == 1`; ready: `DisposeOwned == 1`; faulted: `DisposeOwned == 1` (from bootstrap) and teardown adds none; repeated: three concurrent teardowns share one task, `DisposeOwned == 1`; access-after: a default access started after teardown throws `ObjectDisposedException` naming the fixture and `Create` is unchanged; clone-dispose-after: disposing a clone handle after teardown throws the same and `Create` is unchanged.
- V-7: fault semantics | Unit | `Bootstrap_fault_is_terminal_for_all_waiters` with `[Arguments]` `construct`, `start`, `migrate`, `protect`, `protect-and-cleanup-fails` | 4 concurrent waiters all observe an exception whose message contains the injected sentinel; a fifth access after the fault rethrows without `Create` advancing; `DisposeOwned == 0` for `construct` (no container existed) and `1` otherwise; for `protect-and-cleanup-fails` the surfaced exception's message still contains the original sentinel and its `InnerException`/`AggregateException` carries the cleanup sentinel (no swallow, no replacement).
- V-8: clone path on a faulted lifecycle | Unit | `Clone_request_on_a_faulted_lifecycle_does_not_drop` | `CreateIsolatedSchemaAsync` throws the bootstrap fault; `Drop == 0`; `Create == 1`.
- V-9: clone partial cleanup | Unit | `Clone_creation_failure_drops_once_and_preserves_the_clone_error` with `[Arguments]` `drop-succeeds`, `drop-fails` | `Drop == 1`; the thrown exception is the clone error (message contains the clone sentinel); when drop also fails its sentinel is attached, not substituted.
- V-10: pool clearing scoped to the shared store | Unit | `Bootstrap_clears_only_the_shared_pool` | `ClearedPools` is exactly one entry equal to the shared connection string; no `ClearAllPools` call (the fake's `ClearAll` counter is 0).
- V-18: explicit consumers proceed while bootstrap is in flight | Unit | `Explicit_options_are_not_blocked_by_an_in_flight_bootstrap`: hold the `Start` gate, request a default access, then build explicit options | the explicit call returns immediately (under 1 s) while the default waiter is still `!IsCompleted`.

S1 fresh process (new `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLazyInitializationTests.cs`, `[Category("Integration")]`, `[Category("Slow")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, registered in `tests/Antiphon.Tests/slow-tests-allowlist.txt` with reason "CARD-0476 fresh-process lazy DB probes, Docker cold start"). Parent methods launch `dotnet <Antiphon.Tests.dll>` from `typeof(...).Assembly.Location` with an exact `--treenode-filter "/*/*/TestDbFixtureLazyInitializationTests/<ChildMethod>"`, `--report-trx --report-trx-filename child.trx --results-directory <root>`, env `ANTIPHON_C476_PROBE=<json: root, depth=1, fault?>`, a 150 s budget, `Kill(entireProcessTree: true)` on expiry, stdout/stderr retained under the root. Child methods throw `SkipTestException` when the marker is absent, assert `depth == 1` before anything else, and never launch. Parent methods throw `SkipTestException` when the marker is present (inherited), so a class-filtered child cannot recurse. The fixture's `[After(Assembly)]` writes `lifecycle.json` under the marker root when the marker is present: `state`, `create`, `start`, `migrate`, `protect`, `disposeOwned`, `teardownDispose`, `containerId` (may be null), `runnerBaseUrl`, `ptyBackend` (null or the value), `mvid`. No connection string, credential or environment dump is written.

- V-11: DB-free selection starts nothing | Integration, fresh child | `A_db_free_exact_method_selection_constructs_and_starts_no_database` launches `Child_db_free_selection_touches_no_database`; the child constructs `new TestDbFixture()`, builds explicit options, asserts the guards (`SessionRunner__BaseUrl == http://127.0.0.1:1`, `Delegation__CheckInterpreterEnabled == false`, `ANTIPHON_PTY_BACKEND` null) and `TestDbFixture.Lifecycle.IsRequested == false` | child exit 0; `child.trx` has exactly one executed result, named `Child_db_free_selection_touches_no_database`, outcome Passed; `lifecycle.json` has `state == "never-requested"`, `create == 0`, `start == 0`, `teardownDispose == 0`, `runnerBaseUrl == "http://127.0.0.1:1"`, `ptyBackend == null`.
- V-11b: recursion guard | Integration, fresh child | `A_class_filtered_child_skips_every_parent_probe`: launch with the class filter and the marker | child exit 0; TRX shows every parent method Skipped and every child method Skipped or Passed; no nested `probe-*` directory under the root; `lifecycle.json` `create == 0`.
- V-11c: depth cap | Integration, fresh child | `A_child_at_depth_two_refuses_before_any_work`: marker with `depth=2`, exact filter on `Child_db_free_selection_touches_no_database` | child exit nonzero; TRX result Failed with message containing `depth`; no `lifecycle.json` counters above 0.
- V-12: mixed real consumers cold-start once | Integration, fresh child, real Postgres | `Mixed_first_consumers_share_one_real_bootstrap` launches `Child_mixed_consumers_initialize_once`; the child fires 8 concurrent first callers (2 sync `ConnectionString` via `Task.Run`, 1 `MaintenanceConnectionString`, 2 `CreateDbContextOptions()`, 1 `new TestDbFixture().CreateDbContext()` then `OpenConnectionAsync`, 2 `CreateIsolatedSchemaAsync`), then queries `pg_database` for `antiphon_tmpl` and checks each clone | child exit 0, one Passed result; `lifecycle.json` `create == 1`, `start == 1`, `migrate == 1`, `protect == 1`, `teardownDispose == 1`, `containerId` non-null; the child asserted all connection strings share host/port/database `antiphon_test`, shared store `GetPendingMigrationsAsync` empty, template row `datistemplate == true` and `datallowconn == false`, both clones migrated and `Agents` empty; after child exit the parent runs `docker inspect <containerId>` and asserts a nonzero exit (owned container absent).
- V-13: started-container failure capstone | Integration, fresh child, real Postgres | `A_post_start_fault_cleans_up_the_owned_container` launches `Child_post_start_fault_is_terminal` with marker `fault=after-migrate` (the seam is honored only under the marker; the fault throws `InvalidOperationException("C476-FAULT after-migrate")` from the bootstrap after migration) | child exit 0 (the child method passes by asserting the fault); the child asserted 3 concurrent waiters all got a message containing `C476-FAULT`, a later access rethrew with `create` still 1, and `CreateIsolatedSchemaAsync` threw the same without a drop; `lifecycle.json` `state == "faulted"`, `create == 1`, `start == 1`, `disposeOwned == 1`, `teardownDispose == 0`, `containerId` non-null; parent `docker inspect <containerId>` nonzero.
- V-14: worker regression retained and extended | Integration, existing method | `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents` (unchanged assertions) plus `RunPairAsync` now asserting each receipt's `mvid` equals `typeof(LandQueueRaceWorker).Assembly.ManifestModule.ModuleVersionId` and `dbLifecycle == "never-requested"` (the worker writes `TestDbFixture.Lifecycle.State` into its receipt) | two receipts, same `row`, `inputs == 0`, exit 0, one keyed row, `ConflictException` and `DbUpdateException` as before.
- V-15: worker failure exits 1 before any test | Integration, fresh child | `TestDbFixtureLazyInitializationTests.A_failing_worker_exits_1_without_running_tests`: marker `ANTIPHON_C467_QUEUE_WORKER` set to settings whose `Root` is outside the owned prefix, filter `/*/*/ProcessSpawnLimitTests/Caps_concurrent_process_spawning_tests_at_one`, `--report-trx` | exit 1; stderr contains `InvalidOperationException`; no TRX file is produced or it contains zero results; `lifecycle.json` absent (no probe marker) and the launch took under 60 s.
- V-16: guards eager in a normal run | Integration, existing | `ProductionRunnerGuardTests.Every_program_boot_in_this_assembly_is_pointed_at_a_dead_runner`, `PtyBackendEnvGuardTests.The_suite_ignores_an_inherited_pty_backend` | pass; V-11 proves the same state in a DB-free child.
- V-17: profile tests execute real queries under Unit | Unit lane, existing | `/*/*/*/*[Category=Unit]` fresh TRX | the five `SessionDeliveryProfileTests` methods appear by name with outcome Passed; the lane's TRX names no `TestDbFixtureLazyInitializationTests` row.
- V-19: existing isolation and census | Integration and Unit, existing plus edits | `TestDbFixtureIsolationTests` six methods; `ProcessSpawnLimitTests.Process_spawning_classes_carry_the_limiter` with `typeof(TestDbFixtureLazyInitializationTests)` added; `TestClassificationPolicyTests.C487_G059`/`C487_G060`; `TestLaneCategoryGuardTests` | all pass; `MigrateAsync_on_a_clone_is_a_version_check` keeps its 2 s bound.

S2 controlled (new `tests/Antiphon.SessionRunner.Tests/RunnerRestartPreflightCacheTests.cs`, `[Category("Unit")]`, no limiter, no processes). Each test owns a `new RestartPreflightCache(validator: fake, resolver: fake)`; the fake validator counts invocations, can return exit 0/1, throw, honor cancellation, or block on a gate; the fake resolver returns a scripted `ShellIdentity` per call. Inputs are real temp files so hashing is exercised on bytes.

- V-20: same inputs in two roots hit | Unit | `Identical_inputs_in_different_roots_share_one_validation` | validator invocations 1 after two `ApproveAsync` calls; the second result is `Hit`.
- V-21: key matrix | Unit | `Each_key_dimension_invalidates_independently` with `[Arguments]` `entry`, `helper`, `platform`, `wrapper`, `validator`, `shell-path`, `shell-exe-sha`, `shell-version`, `shell-engine-sha`, `entry-same-length-restored-timestamp`, `entry-crlf-to-lf`, `entry-bom` | after warming, the changed dimension yields validator invocations 2; an unchanged control row (`root-only`) yields 1; for `entry-same-length-restored-timestamp` the file's `LastWriteTimeUtc` equals the pre-edit value when the second approval runs.
- V-22: failures never prime | Unit | `Failed_thrown_timed_out_and_canceled_validation_grant_no_approval` with `[Arguments]` `exit-1`, `throw`, `timeout`, `canceled` | first call surfaces the failure; second call invokes the validator again (2); after "repair" (validator scripted to exit 0) the third call validates (3) and the fourth is a `Hit` (still 3).
- V-23: concurrent misses coalesce and the gate is released before execution | Unit | `Concurrent_same_key_callers_share_one_validation_and_run_separately`: 4 callers on a gated validator, each with its own execution callback that, inside, calls `ApproveAsync` for a different key | validator invocations 1 for the shared key; 4 execution callbacks each invoked once; the different-key approval inside an execution callback completes within 5 s (gate not held across execution).
- V-24: bound inputs revalidated at publication | Unit | `Changed_input_between_validation_and_publication_is_refused`: the validator gate lets the test rewrite the entry file before releasing | no key inserted (a later same-original-bytes call validates again); the caller receives a typed refusal; execution callback 0.
- V-25: resolver | Unit | `Resolver_picks_the_first_readable_regular_candidate_and_never_substitutes` with `[Arguments]` `readable-first`, `zero-byte-first`, `reparse-first` (a directory symlink or junction candidate), `none-readable`, `other-shell-only` using a temp PATH with dummy exe files | resolved path is the first readable candidate; identity carries its SHA-256 and `FileVersionInfo`; `none-readable` and `other-shell-only` throw a typed `ShellResolutionException` naming the requested shell; no `powershell.exe` is ever returned for a `pwsh.exe` request.
- V-26: identity drift refused at launch | Unit | `Shell_identity_drift_before_launch_refuses_execution`: resolver scripted A at lookup, B at recheck | typed refusal; execution callback 0; no key inserted for B.

S2 real shells (new `tests/Antiphon.SessionRunner.Tests/RunnerRestartPreflightSafetyTests.cs`, `[Category("Integration")]`, `[Category("Slow")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, registered in `tests/Antiphon.SessionRunner.Tests/slow-tests-allowlist.txt` with reason "CARD-0476 real-shell preflight cache safety, two shells"). Every method constructs its fixtures with a private `new RestartPreflightCache()` so miss/hit counts are deterministic and the assembly cache is never cleared. Evidence goes under `.antiphon/c420-evidence/<root>/` as today plus `preflight-<seq>.json` per `Run`.

- V-27: cross-root reuse with a real execution child each time and no outcome cache | Integration | `Same_content_in_two_roots_validates_once_and_executes_twice` with `[Arguments("pwsh.exe")]`, `[Arguments("powershell.exe")]`: fixture A default config (`healthy`), fixture B `healthyAt = 999999` with `-WaitOnly -TimeoutSec 1` | A: `ValidatorChildStarts == 1`, `ExecutionChildStarts == 1`, outcome `healthy`; B: `ValidatorChildStarts == 0`, `ExecutionChildStarts == 1`, outcome `wait-expired`, exit 2; B's `preflight-1.json` has `cached == true`, `validatorChildStarted == false`, `executionChildStarted == true`.
- V-28: shells are independent keys | Integration | `Each_shell_misses_once_then_hits`: one cache; `Run("pwsh.exe")` twice, then `Run("powershell.exe")` twice on the same fixture | validator starts after each call: 1, 1, 2, 2; execution starts 1, 2, 3, 4; the two identities differ in path, SHA and version.
- V-29: resolved identity is the launched process | Integration | `Resolved_shell_is_the_process_that_runs` with both shells: `Script("(Get-Process -Id $PID).Path")` and `$PSVersionTable.PSVersion` | the child's reported path equals the resolver's normalized path; `SHA256(File.ReadAllBytes(path))` equals the identity's exe hash; for pwsh the identity's engine hash equals the hash of the adjacent `System.Management.Automation.dll`; for powershell it equals the GAC engine hash; the reported version equals the identity's version.
- V-30: forbidden but harmless entry edits are rejected before execution | Integration | `Warm_cache_rejects_a_tampered_entry_before_any_child` with `[Arguments]` (shell x tamper) for `forbidden-command` (`Remove-Item -LiteralPath '<root>\owned.txt'`), `member-call` (`[System.IO.File]::Delete('<root>\owned.txt')`), `dynamic-command` (`& (Get-Command Remove-Item) -LiteralPath '<root>\owned.txt'` written as `$c='Remove-Item'; & $c ...`) appended to the copied entry after a warming `Run`, with `owned.txt` created in the fixture root first | `Run` throws; message names the offending command/member/dynamic check; `ExecutionChildStarts` unchanged (1); `ValidatorChildStarts` advanced (2); `owned.txt` still exists; `trace.jsonl` byte-identical to after the warming run; `state` and `pid` sentinels unchanged.
- V-31: malformed and non-inert inputs | Integration | `Malformed_syntax_non_inert_helper_missing_core_and_core_bypass_are_rejected` with `[Arguments]` (shell x `parser-error` appends `if (` to the copied entry; `non-inert-helper` appends `Set-Content -LiteralPath '<root>\owned.txt' -Value x` at top level of the copied helper; `missing-core` renames `Invoke-RunnerRestart` to `Invoke-RunnerRestartX` in the copied helper; `core-bypass` inserts `Get-Random | Out-Null` at the top of `Invoke-RunnerRestart`'s body) | `Run` throws with the matching message (`parser`, `not inert`, `Invoke-RunnerRestart`, `core bypassed platform: Get-Random`); execution count unchanged; for `non-inert-helper` `owned.txt` does not exist afterwards.
- V-32: wrapper contract and missing inputs | Integration | `Wrapper_tampering_and_missing_inputs_refuse_before_any_child` with `[Arguments]` `wrapper-absolute-import` (rewrite the wrapper to dot-source the repository helper by absolute path), `wrapper-extra-line`, `platform-missing` (delete the copied platform), `helper-missing` | `Run` throws before any child (`ValidatorChildStarts` and `ExecutionChildStarts` both unchanged); message names the contract or the missing file.
- V-33: bound inputs deny writes and both shells read them | Integration | `Bound_inputs_deny_writes_until_the_child_exits` with both shells: the fixture's test-only `OnBeforeExecution` hook (invoked while handles are held, after validation) attempts `File.WriteAllText` on the copied entry, `File.Delete` on the copied platform, and `File.Move` on the wrapper | each attempt throws `IOException`; `Run` completes with the configured outcome (the child read the held files); after `Run` returns the same writes succeed; a second row `pre-held-write-handle` opens the copied entry with `FileShare.None` before `Run` and expects `Run` to fail before any child with both counts unchanged.
- V-34: a safe same-length edit revalidates and executes | Integration | `Safe_same_length_edit_with_restored_timestamp_revalidates` with both shells: flip one character inside the entry's comment block, restore `LastWriteTimeUtc` | `ValidatorChildStarts` 2, `ExecutionChildStarts` 2, outcome `healthy` both times.
- V-35: an existing fixture executes its own snapshot | Integration | `Fixture_executes_its_copied_helper_not_the_repository`: insert `[System.IO.File]::WriteAllText('<root>\snapshot-marker','x')` at the top of the copied helper's `Invoke-RunnerRestart` body (member call, passes validation) | `Run` outcome `healthy`; `snapshot-marker` exists; the repository helper is byte-identical before and after (the test never writes it).
- V-36: whole restart selection with fresh TRX | Integration, existing classes | `dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c476/ -- --treenode-filter '/*/*/(RunnerRestartHealthTests*)|(RunnerRestartScriptCompatibilityTests*)|(RunnerRestartPreflightSafetyTests*)/*' --report-trx --report-trx-filename restart.trx --results-directory .antiphon/c476-restart-01` | TRX has 55 `RunnerRestartHealthTests` results (43 Health plus 12 diagnostic partial rows), 8 `RunnerRestartScriptCompatibilityTests` results, all Passed with unchanged assertions; both shell rows present for `Wait_exception_still_emits_one_final_result_without_more_controls`, `Only_current_attempt_and_process_records_define_phase`, `Diagnostic_reader_survives_rotation_gaps_and_partial_records`, `Touched_scripts_parse_as_ascii_on_both_shells`, `Documented_arguments_execute_the_expected_mode`, `Hung_http_response_is_canceled_at_wait_deadline`; the assembly cache evidence shows exactly 2 successful preflights for the shared cache across Health and Compatibility (one per shell) and one execution child per accepted `Run`.
- V-37: `Script` and `DecodeCapturedMilestones` stay uncached | Integration | `Script_and_decoder_launch_a_child_every_call`: warm the cache, call `Script("exit 0")` three times and `DecodeCapturedMilestones` twice | `ScriptChildStarts == 5`; `ValidatorChildStarts` and `ExecutionChildStarts` unchanged.
- V-38: owned-child timeout retained | Integration | `Hung_child_is_killed_at_the_fixture_deadline`: fixture `ChildTimeout = 2 s` (default stays 45 s, asserted), `Script("Start-Sleep 10")` | throws `TimeoutException` within 4 s; the child process has exited afterwards (`Process.GetProcessById` throws).
- V-39: census and lane guards | Unit, existing edited | `ProcessSpawnLimitTests.Process_spawning_classes_are_exactly_the_limiter_population` with `typeof(RunnerRestartPreflightSafetyTests)` added and `RunnerRestartPreflightCacheTests` carrying no limiter; the shared classification guard for the Slow registry | pass.

### Guards the regression

- R-1: eager DB start returns | V-11 `lifecycle.json` `create == 0` and `start == 0` in a DB-free child.
- R-2: double or racing initialization | V-1 `Create.ShouldBe(1)` under 8 concurrent first callers; V-12 `create == 1` against real Postgres.
- R-3: premature readiness (a consumer sees an unprotected template) | V-3 `IsCompleted.ShouldBeFalse()` while the `ALLOW_CONNECTIONS` gate is held.
- R-4: lost worker dispatch | V-14 `Directory.GetFiles(root, "*.absent").Length.ShouldBe(2)` and two receipts; V-15 `ExitCode.ShouldBe(1)` with no TRX results.
- R-5: bootstrap retry after a fault | V-7 `Create.ShouldBe(1)` after the fifth access.
- R-6: recursive initialization from cleanup | V-8 `Drop.ShouldBe(0)`; V-6 `clone-dispose-after` `Create` unchanged.
- R-7: partial container leaked on fault | V-7 `DisposeOwned.ShouldBe(1)`; V-13 `docker inspect` nonzero.
- R-8: sync-context deadlock | V-4 completion within the 30 s bound.
- R-9: process-wide pool clearing disturbs explicit consumers | V-10 `ClearAll.ShouldBe(0)` and exactly one scoped clear.
- R-10: a cached failure | V-22 second call `Invocations.ShouldBe(2)`.
- R-11: a skipped real execution or cached outcome | V-27 `ExecutionChildStarts.ShouldBe(1)` on fixture B with outcome `wait-expired`.
- R-12: a key that ignores a dimension | V-21 each row `Invocations.ShouldBe(2)`.
- R-13: constructor-only hashing | V-34 `ValidatorChildStarts.ShouldBe(2)`; V-30 rejection after a warm run.
- R-14: unsafe copied content executing | V-30 and V-31 `ExecutionChildStarts` unchanged and `owned.txt` present.
- R-15: live repository import | V-35 `snapshot-marker` exists.
- R-16: shell substitution or drift | V-25 `other-shell-only` throws; V-26 execution 0; V-28 counts 1, 1, 2, 2.
- R-17: census drift for new process classes | V-19 and V-39.
- R-18: the Health/Compatibility rows changing outcome under the cache | V-36 fresh TRX counts and outcomes.

### Guard inventory

- G-1: S1.2 / DB-A the default database is neither constructed nor started without a default consumer | PC-1
- G-2: S1.2 the gate creates the lifecycle task once | PC-2
- G-3: S1.2 / S1.6 no initialization after disposal starts | PC-3
- G-4: S1.2 bootstrap runs on the thread pool; the sync adapter cannot deadlock a captured context | PC-4
- G-5: S1.3 readiness published only after `IS_TEMPLATE true` and `ALLOW_CONNECTIONS false` | PC-5
- G-6: S1.3 bootstrap builders never call the lazy default accessors (no self-wait) | PC-6
- G-7: S1.4 explicit connection strings bypass initialization | PC-7
- G-8: S1.4 / S1.7 a bootstrap fault in `CreateIsolatedSchemaAsync` never enters the drop path | PC-8
- G-9: S1.5 pool clearing scoped to the shared store | PC-9
- G-10: S1.6 never-requested teardown is a no-op | PC-10
- G-11: S1.6 in-flight teardown awaits the attempt then disposes once | PC-11
- G-12: S1.6 repeated teardown shares one disposal | PC-12
- G-13: S1.7 / D-4 all waiters receive the original fault and no retry occurs | PC-13
- G-14: S1.7 bootstrap disposes the partial container once and preserves the original exception | PC-14
- G-15: S1.7 cleanup failure is attached, never substituted for the original | PC-15
- G-16: S1.7 clone partial cleanup retained with the clone error preserved | PC-16
- G-17: S1.1 worker dispatch stays eager and awaits `RunAsync` before exiting 0 | PC-17
- G-18: S1.1 worker failure exits 1 before any test executes | PC-18
- G-19: D-3 `ProductionRunnerGuard` and `PtyBackendEnvGuard` remain eager and independent of the DB lifecycle | PC-19
- G-20: DB-E the worker child never starts a private database | PC-20
- G-21: S1.4 clone disposal uses the ready state and never starts a container | PC-21
- G-22: S2.3 / S2.4 key includes the resolved shell path | PC-22
- G-23: S2.3 key includes the shell executable SHA-256 | PC-23
- G-24: S2.3 key includes the shell executable version | PC-24
- G-25: S2.3 key includes the engine assembly fingerprint | PC-25
- G-26: S2.4 key includes the copied entry bytes | PC-26
- G-27: S2.4 key includes the copied helper bytes | PC-27
- G-28: S2.4 key includes the copied platform bytes | PC-28
- G-29: S2.4 key includes the wrapper bytes | PC-29
- G-30: S2.4 key includes the validator identity | PC-30
- G-31: S2.4 bytes are hashed without newline/BOM normalization | PC-31
- G-32: S2.6 a key is inserted only on validator exit 0 | PC-32
- G-33: S2.6 an exception, timeout or cancellation grants no approval | PC-33
- G-34: S2.6 bound inputs must still be valid at publication | PC-34
- G-35: S2.7 a hit skips only the validator; every accepted `Run` launches the real child | PC-35
- G-36: S2.5 inputs are read and hashed at every `Run` | PC-36
- G-37: S2.5 write/delete-denying handles are held from hashing through child exit | PC-37
- G-38: S2.5 a handle that cannot be held fails before execution | PC-38
- G-39: S2.5 shell identity is rechecked immediately before launch and drift refused | PC-39
- G-40: S2.3 the other shell is never substituted | PC-40
- G-41: S2.2 parser errors are rejected | PC-41
- G-42: S2.2 a missing expected core function is rejected | PC-42
- G-43: S2.2 a non-inert helper import is rejected | PC-43
- G-44: S2.2 the entry command allowlist is enforced | PC-44
- G-45: S2.2 unchecked member calls in the entry are rejected | PC-45
- G-46: S2.2 unchecked dynamic commands in the entry are rejected | PC-46
- G-47: S2.2 core commands outside the platform allowlist are rejected | PC-47
- G-48: S2.2 the wrapper is checked against its exact contract before any import | PC-48
- G-49: D-6 / S2.1 an existing fixture executes its own byte snapshot, never a live repository import | PC-49
- G-50: S2.6 the gate is released before real execution | PC-50
- G-51: S2.6 concurrent same-key misses perform one validation | PC-51
- G-52: S2.8 `Script` and `DecodeCapturedMilestones` are never served from the cache | PC-52
- G-53: S2.7 the owned-child timeout/kill-and-await is preserved | PC-53
- G-54: S2.7 / D-5 every accepted `Run` writes current config (no outcome or config cache) | PC-54
- G-55: S1 probes: a parent probe method skips when the marker is inherited (no recursion) | PC-55
- G-56: S1 probes: a child refuses above depth 1 | PC-56

Guards = 56, mapped = 56, missing = 0, duplicate PC mappings = 0. Not inventoried as guards: the per-invocation evidence records (measurement support, exercised by V-27/V-36 but not safety-critical) and the pre-existing `SafeDatabaseName` check, which this plan does not touch.

### Positive controls

Each PC: apply the mutation, rebuild into `bin-c476pc/` (forward slash), run only the named method with `--treenode-filter "/*/*/<Class>/<Method>"` (an `[Arguments]` row is still selected by method; the row named is the one that must be red), confirm the named assertion is the red one, restore, refresh the restored file's timestamp, rebuild, run green, and verify the DLL MVID in the output differs between red and green. Fresh-process PCs (PC-1, PC-17..PC-20, PC-55, PC-56) rebuild the assembly the child loads, so the child's `mvid` in the evidence must match the freshly built DLL. No PC injects a live stop/restart; PC-44 uses the fixture-owned `owned.txt` only.

- PC-1: break G-1 by making the marker dispatch hook call `TestDbFixture.Lifecycle.EnsureReadyAsync()` after the worker check; expect `TestDbFixtureLazyInitializationTests.A_db_free_exact_method_selection_constructs_and_starts_no_database` red at `lifecycle.create.ShouldBe(0)`.
- PC-2: break G-2 by replacing the once-gate with unconditional task creation on every default access; expect `TestDbFixtureLifecycleTests.Eight_concurrent_first_callers_share_one_bootstrap` red at `Create.ShouldBe(1)`.
- PC-3: break G-3 by deleting the disposed check in the gate; expect `Teardown_matrix("access-after")` red at `Should.Throw<ObjectDisposedException>` (a new create occurred instead).
- PC-4: break G-4 by starting the bootstrap with `Task.Factory.StartNew(..., TaskScheduler.FromCurrentSynchronizationContext())` and awaiting internally with context capture; expect `Synchronous_access_completes_under_a_single_threaded_synchronization_context` red at the 30 s `WaitAsync` timeout assertion.
- PC-5: break G-5 by publishing the ready state after migration, before the two `ALTER DATABASE` statements; expect `Readiness_waits_for_template_protection` red at `IsCompleted.ShouldBeFalse()` for the `before-ALLOW_CONNECTIONS` gate.
- PC-6: break G-6 by building the migration options through `CreateDbContextOptions()` (default) inside the bootstrap body; expect `Eight_concurrent_first_callers_share_one_bootstrap` red at the 30 s bound (self-wait).
- PC-7: break G-7 by reading `ConnectionString` unconditionally at the top of `CreateDbContextOptions(string?)`; expect `Explicit_options_never_initialize` red at `Create.ShouldBe(0)`.
- PC-8: break G-8 by moving the readiness await inside the `try` after the clone name is chosen so the `catch` runs `DropClonedDatabaseAsync`; expect `Clone_request_on_a_faulted_lifecycle_does_not_drop` red at `Drop.ShouldBe(0)`.
- PC-9: break G-9 by restoring `NpgsqlConnection.ClearAllPools()` in the bootstrap; expect `Bootstrap_clears_only_the_shared_pool` red at `ClearAll.ShouldBe(0)`.
- PC-10: break G-10 by making teardown call `EnsureReadyAsync()` before disposing; expect `Teardown_matrix("never-requested")` red at `Create.ShouldBe(0)`.
- PC-11: break G-11 by returning from teardown when the state is `Starting`; expect `Teardown_matrix("starting")` red at `DisposeOwned.ShouldBe(1)`.
- PC-12: break G-12 by creating a fresh disposal task on every teardown call; expect `Teardown_matrix("repeated")` red at `DisposeOwned.ShouldBe(1)`.
- PC-13: break G-13 by resetting the gate to null in the fault path; expect `Bootstrap_fault_is_terminal_for_all_waiters("migrate")` red at `Create.ShouldBe(1)` after the fifth access.
- PC-14: break G-14 by deleting the bootstrap `catch` that disposes the owned container; expect `Bootstrap_fault_is_terminal_for_all_waiters("start")` red at `DisposeOwned.ShouldBe(1)`.
- PC-15: break G-15 by throwing the cleanup exception instead of the original when both fail; expect `Bootstrap_fault_is_terminal_for_all_waiters("protect-and-cleanup-fails")` red at `Message.ShouldContain("C476-FAULT")` (original sentinel).
- PC-16: break G-16 by swallowing the drop failure and rethrowing it in place of the clone error; expect `Clone_creation_failure_drops_once_and_preserves_the_clone_error("drop-fails")` red at the clone-sentinel message assertion.
- PC-17: break G-17 by calling `Environment.Exit(0)` before awaiting `LandQueueRaceWorker.RunAsync`; expect `AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents` red at `Directory.GetFiles(root, "*.absent").Length.ShouldBe(2)`.
- PC-18: break G-18 by replacing `Environment.Exit(1)` in the worker catch with `return`; expect `TestDbFixtureLazyInitializationTests.A_failing_worker_exits_1_without_running_tests` red at `ExitCode.ShouldBe(1)` (the child ran the filtered test and exited 0).
- PC-19: break G-19 by returning early from `ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner` when `TestDbFixture.Lifecycle.IsRequested` is false; expect `A_db_free_exact_method_selection_constructs_and_starts_no_database` red at `lifecycle.runnerBaseUrl.ShouldBe("http://127.0.0.1:1")`.
- PC-20: break G-20 by removing `ConnectionString = ...` from the worker's `HarnessOptions` (default store); expect `C467_V09_KeyedQueueRacesAndDistinctEvents` red at `dbLifecycle.ShouldBe("never-requested")` (each child booted its own container; bounded, no recursion).
- PC-21: break G-21 by making `DropClonedDatabaseAsync` call `EnsureReadyAsync()` instead of reading the ready state; expect `Teardown_matrix("clone-dispose-after")` red at `Create.ShouldBe(1)` (a second create).
- PC-22: break G-22 by omitting the normalized path from the key; expect `RunnerRestartPreflightCacheTests.Each_key_dimension_invalidates_independently("shell-path")` red at `Invocations.ShouldBe(2)`.
- PC-23: break G-23 by omitting the exe SHA-256 from the key; expect `...("shell-exe-sha")` red at `Invocations.ShouldBe(2)`.
- PC-24: break G-24 by omitting the version; expect `...("shell-version")` red at `Invocations.ShouldBe(2)`.
- PC-25: break G-25 by omitting the engine fingerprint; expect `...("shell-engine-sha")` red at `Invocations.ShouldBe(2)`.
- PC-26: break G-26 by hashing the entry path string instead of its bytes; expect `...("entry")` red at `Invocations.ShouldBe(2)`.
- PC-27: break G-27 by omitting the helper hash; expect `...("helper")` red at `Invocations.ShouldBe(2)`.
- PC-28: break G-28 by omitting the platform hash; expect `...("platform")` red at `Invocations.ShouldBe(2)`.
- PC-29: break G-29 by omitting the wrapper hash; expect `...("wrapper")` red at `Invocations.ShouldBe(2)`.
- PC-30: break G-30 by replacing the validator identity with a constant; expect `...("validator")` red at `Invocations.ShouldBe(2)`.
- PC-31: break G-31 by normalizing CRLF to LF and stripping a UTF-8 BOM before hashing; expect `...("entry-crlf-to-lf")` red at `Invocations.ShouldBe(2)`.
- PC-32: break G-32 by inserting the key on any validator completion regardless of exit code; expect `Failed_thrown_timed_out_and_canceled_validation_grant_no_approval("exit-1")` red at `Invocations.ShouldBe(2)`.
- PC-33: break G-33 by catching the validator exception and continuing to insert; expect `...("throw")` red at `Invocations.ShouldBe(2)`.
- PC-34: break G-34 by skipping the post-validation snapshot comparison; expect `Changed_input_between_validation_and_publication_is_refused` red at the typed-refusal `Should.ThrowAsync` (approval granted).
- PC-35: break G-35 by returning the previous `RestartResult` on a cache hit without launching; expect `RunnerRestartPreflightSafetyTests.Same_content_in_two_roots_validates_once_and_executes_twice("pwsh.exe")` red at fixture B `ExecutionChildStarts.ShouldBe(1)`.
- PC-36: break G-36 by computing the input hashes once in the constructor and reusing them in `Run`; expect `Safe_same_length_edit_with_restored_timestamp_revalidates("pwsh.exe")` red at `ValidatorChildStarts.ShouldBe(2)`.
- PC-37: break G-37 by opening the bound inputs with `FileShare.ReadWrite | FileShare.Delete`; expect `Bound_inputs_deny_writes_until_the_child_exits("pwsh.exe")` red at the first `Should.Throw<IOException>`.
- PC-38: break G-38 by catching the handle-open `IOException` and proceeding; expect `Bound_inputs_deny_writes_until_the_child_exits("pre-held-write-handle")` red at `ExecutionChildStarts.ShouldBe(0)`.
- PC-39: break G-39 by deleting the pre-launch identity recheck; expect `Shell_identity_drift_before_launch_refuses_execution` red at `Executions.ShouldBe(0)`.
- PC-40: break G-40 by falling back to `powershell.exe` when `pwsh.exe` resolves to nothing readable; expect `Resolver_picks_the_first_readable_regular_candidate_and_never_substitutes("other-shell-only")` red at `Should.Throw<ShellResolutionException>`.
- PC-41: break G-41 by passing `[ref]$null` for parse errors and dropping the error-count check in the validator text; expect `Malformed_syntax_non_inert_helper_missing_core_and_core_bypass_are_rejected("pwsh.exe","parser-error")` red at `Should.ThrowAsync` (a partial AST passed).
- PC-42: break G-42 by guarding the core loop with `if ($core)`; expect `...("pwsh.exe","missing-core")` red at `Should.ThrowAsync`.
- PC-43: break G-43 by deleting the inert-import loop; expect `...("pwsh.exe","non-inert-helper")` red at `Should.ThrowAsync`, with `owned.txt` also gone.
- PC-44: break G-44 by adding `Remove-Item` to the entry allowlist; expect `Warm_cache_rejects_a_tampered_entry_before_any_child("pwsh.exe","forbidden-command")` red at `Should.ThrowAsync` and `File.Exists(owned).ShouldBeTrue()`.
- PC-45: break G-45 by deleting the `InvokeMemberExpressionAst` loop; expect `...("pwsh.exe","member-call")` red at `Should.ThrowAsync`.
- PC-46: break G-46 by deleting the `-not $name` dynamic-command branch; expect `...("pwsh.exe","dynamic-command")` red at `Should.ThrowAsync`.
- PC-47: break G-47 by deleting the core command loop; expect `...("pwsh.exe","core-bypass")` red at `Should.ThrowAsync`.
- PC-48: break G-48 by skipping the wrapper contract comparison; expect `Wrapper_tampering_and_missing_inputs_refuse_before_any_child("wrapper-absolute-import")` red at `ValidatorChildStarts.ShouldBe(0)` (a child was launched).
- PC-49: break G-49 by writing the wrapper to dot-source the repository helper by absolute path; expect `Fixture_executes_its_copied_helper_not_the_repository` red at `File.Exists(snapshotMarker).ShouldBeTrue()`.
- PC-50: break G-50 by holding the async gate across the execution callback; expect `Concurrent_same_key_callers_share_one_validation_and_run_separately` red at the 5 s different-key completion bound.
- PC-51: break G-51 by removing the gate so each miss validates; expect the same method red at `Invocations.ShouldBe(1)`.
- PC-52: break G-52 by returning the previous `(Exit, Output)` from `Script` when the driver text matches the last call; expect `Script_and_decoder_launch_a_child_every_call` red at `ScriptChildStarts.ShouldBe(5)`.
- PC-53: break G-53 by ignoring `ChildTimeout` in `Process` (plain `WaitForExitAsync`); expect `Hung_child_is_killed_at_the_fixture_deadline` red at `Should.ThrowAsync<TimeoutException>` after the 10 s sleep completes.
- PC-54: break G-54 by writing `config.json` only on a cache miss; expect `Same_content_in_two_roots_validates_once_and_executes_twice("pwsh.exe")` red at fixture B `Outcome.ShouldBe("wait-expired")` (it ran A's config and reported `healthy`).
- PC-55: break G-55 by deleting the inherited-marker skip from the parent probe methods; expect `A_class_filtered_child_skips_every_parent_probe` red at the "no nested probe directory" assertion (the depth cap stops the grandchild, so the run stays bounded).
- PC-56: break G-56 by deleting the depth assertion in the child methods; expect `A_child_at_depth_two_refuses_before_any_work` red at `ExitCode.ShouldNotBe(0)`.

### Out of scope

- A real Postgres fault during `CREATE DATABASE ... TEMPLATE` (controlled V-9 covers the cleanup semantics; there is no Postgres fault injector and one is not worth adding for a test fixture).
- Timing thresholds in correctness tests: no test asserts that lazy startup or the cache is faster; savings are measured by the S3 procedure with fresh TRX and reported separately.
- The full Unit lane as a zero-DB benchmark: `SessionDeliveryProfileTests` keeps Postgres in that lane by design (D-7); V-17 only proves the five methods still run.
- `TranscriptAdoptionSafetyTests`, PTY delivery settings, native project sequencing and both process limiter widths: unchanged by plan; the separately gated Git trial is not part of this verification.
- Hook-order behaviour among the three `[Before(Assembly)]` hooks: the plan forbids a new ordering assumption; V-11 asserts the guards' effect, not their order.
- `--list-tests` output as coverage evidence, per the testing owner; every V/R row cites executed TRX results.

### Cost

Filters use `--property:OutputPath=bin-c476/` (Code) and `bin-c476pc/` (Mutation) with a forward slash; `dotnet run --project tests/<X> --no-build ... -- --treenode-filter ... --report-trx --report-trx-filename <n>.trx --results-directory <fresh dir>`. Projects run sequentially; nothing here co-schedules `Antiphon.Agents.Pty.Tests`. Minutes are estimated on this machine unless marked measured; the measured inputs are the shell floors and preflight cost above and the plan's 24–37 s historical DB bootstrap.

| Item | Suite and filter | Minutes |
|---|---|---|
| Setup/build | `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c476/` and the same for `tests/Antiphon.SessionRunner.Tests` (ledger-reset graph 1.5 min measured by CARD-0222; two projects with restore) | 6 (estimated) |
| S1 controlled | `Antiphon.Tests` `/*/*/TestDbFixtureLifecycleTests/*` (V-1..V-10, V-18; hermetic) | 1 (estimated) |
| S1 fresh process | `Antiphon.Tests` `/*/*/TestDbFixtureLazyInitializationTests/*` (V-11, V-11b, V-11c, V-15 at ~15 s each with no Docker; V-12 and V-13 at one cold container each, ~45 s) | 3 (estimated) |
| S1 existing lanes | `Antiphon.Tests` `/*/*/(TestDbFixtureIsolationTests*)|(ProductionRunnerGuardTests*)|(PtyBackendEnvGuardTests*)|(ProcessSpawnLimitTests*)|(TestClassificationPolicyTests*)|(TestLaneCategoryGuardTests*)/*` then `/*/*/AgentTaskLandNotificationRecoveryTests/C467_V09_KeyedQueueRacesAndDistinctEvents` (one cold container plus two worker children) | 3 (estimated) |
| Unit lane | `Antiphon.Tests` `/*/*/*/*[Category=Unit]` (70 s outer measured 2026-09-10 before this change; still one cold container for the profile methods) | 2 (estimated) |
| S2 controlled and census | `Antiphon.SessionRunner.Tests` `/*/*/*/*[Category=Unit]` (V-20..V-26, V-39) | 1 (estimated) |
| S2 real shells and existing rows | `Antiphon.SessionRunner.Tests` `/*/*/(RunnerRestartHealthTests*)|(RunnerRestartScriptCompatibilityTests*)|(RunnerRestartPreflightSafetyTests*)/*` (Health 143 s historical minus ~54 cached preflights at 3–5 s each measured, plus ~40 safety rows at one or two real children each) | 6 (estimated) |
| Ordinary V/R floor (Code) | sum of the seven rows above | 22 (estimated) |
| PC floor, hermetic (Mutation) | PC-2..PC-16, PC-21 (Antiphon.Tests controlled, 16) and PC-22..PC-34, PC-39, PC-40, PC-50, PC-51 (SessionRunner controlled, 17): 33 x ~1.5 (incremental build ~1 min, method run seconds, restore, rebuild, green) | 50 (estimated) |
| PC floor, real shell (Mutation) | PC-35..PC-38, PC-41..PC-49, PC-52..PC-54 (16) x ~2 (each row launches two to four shells at 2.5–6 s measured) | 32 (estimated) |
| PC floor, fresh process and Docker (Mutation) | PC-1, PC-18, PC-19, PC-55, PC-56 (child at ~15 s, x ~2.5) and PC-17, PC-20 (cold containers, x ~4) | 21 (estimated) |
| Total verification floor | setup/build + V/R + every PC red/restore/green | 125 (estimated) |

Savings: method-scoped PC cycles against `RunnerRestartPreflightSafetyTests` cost one or two shell rows (about 10 s) instead of the 2.5-minute Health class, and against `TestDbFixtureLifecycleTests` they cost seconds instead of a 45-second cold container; that is roughly 60 minutes below class-scoped cycles across the 56 PCs. Mutation may shard the hermetic PCs across two worktrees off the task branch; the real-shell, fresh-process and Docker PCs stay on the one-wide process-spawn lane and `Antiphon.Tests` shards must not overlap on the same Docker host during PC-17/PC-20. Once landed, every DB-free exact-method selection in `Antiphon.Tests` also saves the 24–37 s bootstrap, including the 33 hermetic PC cycles in this very plan.

Bodies read; boundaries mapped; guards = 56, mapped = 56, missing = 0, duplicate PC mappings = 0; every PC is a compiling defect with a named method and red assertion; cost is numeric and labelled.

Next stage: **code**.
