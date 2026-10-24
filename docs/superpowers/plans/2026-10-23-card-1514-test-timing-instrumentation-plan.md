# CARD-1514: per-test timing and phase evidence for the next rc qualification

Plan owner: task `e981eae8-5bc9-4022-950c-6575f4970c64`. Inspected source and
fetched `origin/master`: `2970a3adb18403e9b5ee4e6490d6fb0142d172a3`.
Fetched `origin/rc/2026-10-20`: `5a126f6fc5ae4e16d803b628716b8f0485a37174`.
These are planning observations on 2026-10-23, not activation or test evidence.
This dispatch is docs only: no build, test, desktop access, restart, or interaction
with the live Windows qualification (supervisor PID 209072).

The operator requires the **next fully qualified Windows rc tip** to retain per-test
elapsed time, exact batch membership, and enough db/host/process/filesystem
attribution to choose later optimizations. This plan includes the requested test
design; Code can execute the closed checkpoints below. No separate TestDesign
round or human policy decision is required. All proposed test names are new unless
explicitly called existing. Estimated costs are not measured results.

The required searches completed with `scripts/card.ps1 search <phrase> -Board
Antiphon -All`: `test timing` (1), `phase` (80), `slow` (96), `profile`, and
`instrument` (10). Full CARD-1314, 1514, 1404, 1531 and 1532 were read. **Reuse and
extend High-priority CARD-1514**, whose existing acceptance already asks where test
time goes. Its description now carries this operator extension (revision 12,
read/write response verified); no duplicate card was filed. Preserve its other
speedup slices. CARD-1314 remains read-only duration bucketing; CARD-1404/1410 own
group pricing; CARD-1531 owns admission clocks; CARD-1532 owns source stamps.

## Ground truth

| Brief assumption / question | Inspected implementation | Design consequence |
|---|---|---|
| Per-test durations need a new stopwatch. | Native execution uses `--report-trx`, a named TRX and diagnostics (`scripts/lib/nightly-tests-impl.ps1`, `Get-NightlyNativeExecutionArguments`, lines 559 onward). Files are named by suite/chunk under `LogRoot`; summary rows retain `trx`, `chunk`, outcome and coverage. `tools/Antiphon.Checkpoints/Report/PhaseTimings.cs` already consumes per-case start/end/duration. | Keep TRX authoritative for final duration/outcome. New clocks measure attribution and hook envelopes. Read retained TRX; never rewrite it. Actual live Windows files were not inspected. |
| Existing coverage parser exposes durations. | `Read-NightlyTrxIdentities` in `scripts/lib/nightly-coverage.ps1:18` reads testId, definition class/method, display and outcome, but discards duration/start/end and sets `Uid=''`. Cross-checking diagnostics is a class/method multiplicity check, not an expanded-case timing join. | A separate timing reader reads duration/timestamps and exact identities from the same XML. Do not widen/change the gate parser or infer an argument case from display order. |
| PHASES explains time inside a test. | `ReportWriter.cs:35` prints slotWait/build/startup/testsWall/teardown/hostWall. `PhaseTimings.Reduce` derives startup/teardown from host UTC boundaries and first/last TRX result, checks monotonic host time and clock discontinuity. Summed case durations are separate from their window. | Preserve PHASES unchanged. It explains the host envelope, not migrations or Git work. Do not add parallel case sums to wall time. |
| No timing infrastructure exists. | CARD-1313 has `TimingRowRecorder`, `TimingJournal`, `scripts/lib/test-timings.ps1`, and `scripts/test-timing-report.ps1`. The exact-roster executor in `nightly-tests-impl.ps1:1310` optionally writes set events using `ANTIPHON_TEST_TIMING_ROOT`. Its report groups comparable set histories; the front door requires PS7 and its library uses `SHA256.HashData`. | Leave that journal/schema/env variable intact. New case-phase evidence has its own version, directory and PS5.1-compatible report. Reuse identity/metric conventions, not the journal's per-append readback/mutex path for thousands of cases. |
| Every group already has an ordered expanded test list. | `ConvertTo-NightlyProfileLaunchGroups` sorts namespaces, uses 900 estimated serial seconds, at most 30 OR operands, prefix-safe singleton exceptions, oversized method chunks and exact exclusion leaves. Normal class groups have `nodes=@()`; method chunks retain nodes. `*-profile.discovery.json`, diagnostic logs and selection discovery exist, and summary has counts/digest/exclusions, but the ordinary group object does not persist a complete ordered UID list plus cost/actuals. | Project the current groups onto the existing discovery index once. Persist the exact required nodes before execution, keeping filters, group sequence, exclusions and cost algorithm identical. |
| Release authority has no membership manifest. | `New-ReleaseGateExecutionPlan` in `scripts/lib/release-authority.ps1:348` already freezes discovery and per-class chunk UIDs in `authority/execution-plan.json`; its ledger validates explicitly named TRX paths/digests, counts and terminal UIDs. Its default per-class chunks are not proof that the nightly cost-packed launches used the same IDs. | Reuse an existing frozen plan where that executor owns the launch. Record the actual executor's chunk identity; never substitute authority class IDs for nightly group IDs or edit the frozen ledger. |
| Database setup means migrations per test/schema. | `TestDbFixture.cs` warms a shared store only for selections that need it. `TestDbFixtureLifecycle.cs` starts one owned PostgreSQL container per assembly lifecycle, restores a CARD-1420 snapshot or migrates/captures it, protects a template, clones **databases**, then drops clones. `CloneAsync` includes a semaphore wait and bounded retry. `TestSchemaSnapshotTests` verifies real migration/restore parity. | Measure assembly fixture startup separately and measure clone/drop per consumer. Do not label each clone as a migration or attribute shared warmup to the first test by accident. |
| Host setup is a per-test constructor. | `AntiphonWebAppFactory` uses lazy cloned DBs and often `SharedType.PerTestSession`. Actual `WebApplicationFactory` server startup is lazy; `ConfigureWebHost` alone is not the startup duration. It has both sync and async disposal paths and a production-runner refusal guard. | Wrap `CreateHost`/`CreateServer` around their base implementation, not only configuration. Shared factory costs retain fixture IDs. Preserve all runner guards and lifecycle behavior. |
| Parallel limiters are process wrappers. | `ProcessSpawnLimit` is assembly-local width 1. `GitProcessSpawnLimit` is a separate, configurable width 2-4 by default. Neither starts a process or exposes queue admission timestamps. Real choke points include `LandingGitFixture.FixtureGit.StartProcess/RunAsync`, `CheckpointFixtureProcess.Run`, and `ScriptHarnessProcess` with native owned process implementations. | Instrument those wrappers without changing either limiter. Limiter queue time before TUnit start remains group scheduling/unattributed time, not invented process-wait time. |
| All filesystem activity goes through one helper. | `tests/Shared/ReadOnlyDirectory.Delete` clears readonly flags, avoids traversing links and deletes a tree; it is linked into multiple projects. Factories and landing/checkpoint temp fixtures also create/delete roots directly. | Measure only shared fixture create/copy/delete boundaries. An uninstrumented direct call remains remainder; no mass test edits or global interception. |
| A static recorder conflicts with project conventions. | `docs/project-context.md` disallows application static state. Test fixtures already have assembly-owned static state and AsyncLocal evidence patterns. The operator explicitly asks for a tiny shared static test API. | Limit the exception to test-only code; instance-owned recorder state behind a static facade. No Application/Domain dependency on test instrumentation. |
| TUnit supports suitable lifecycle hooks. | Package-pinned TUnit 1.44 XML documents `ITestStartEventReceiver.Stage=Early` before instance Before(Test) and `ITestEndEventReceiver.Stage=Late` after After(Test), including throwing tests. `tests/Shared/CheckpointRepeatEvidenceAttribute.cs` already uses assembly event receivers and `StateBag`. | Use those typed APIs and a real TUnit probe to prove expanded identity, setup/teardown, async flow and outcomes. Do not assume an AsyncLocal assigned inside an awaited receiver flows back into the test. |
| Extra log files necessarily invalidate release evidence. | Gate/authority validation reads named manifests, summary, ledger and named TRX digests; `New-ReleaseGateManifest` has an explicit public-field allowlist. It does not treat every sibling log file as authority. | Add a private `timing/` sibling; no new public manifest fields or mutations of gate evidence. Prove identical gate verdicts with/without sidecars. |
| Gitignored timing output anywhere is safe for checkpoints. | `BuildConfinement.ListUntracked` stamps ignored and untracked files except root `.antiphon/`; existing output roots are not generally exempt. Output custody/deletion refuses unowned/changed entries. CARD-1532 is present: streaming stamps in `9a044e401404834260a8c44834bf771a3ed2a399`, follow-up `6b3e7630d2a2bde2aa60eb9344b35cd0a1c18f30`. | Use external run logs, or `.antiphon/<run>/timing` in a task checkout. Never write into bin/obj/tool-copy roots or a prior run. Do not weaken stamp/cleanup rules. |

Related measurement: [CARD-1514 slow profile](../../investigations/2026-10-22-card-1514-slow-test-profile.md)
shows 16,148 Git processes for 52 PersistenceFailure cases, shared bootstrap miss/hit
variation, and 3,740.74 summed RecoveryBoundary test seconds within a 46.409-second
case window. Those are historical, instrumented observations on another SHA;
they justify attribution, not an assumed Windows bottleneck or savings claim.

## Decisions

**D-1 - Ship S1 + S2 as the minimum rc unit; S3 is the bounded extension.**
S1 supplies recorder, three coarse choke-point families (DB, host, process), and
summary. S2 supplies group manifests and qualification environment wiring, plus
the small shared delete-tree scope needed for the minimum fs bucket. The minimum
is per-test TRX duration/outcome + exact planned/actual group membership + coarse
db/host/process/fs/other, with coverage counts and explicit missing attribution.
S3 adds remaining shared scopes. No optimization, parallelism, test reordering,
cost-table refresh, extra full suite, provider session, or watchdog change belongs
in this card. Reject EventPipe/ETW/profilers and process-wide Activity listeners for
this rc: their deployment/platform and attribution costs exceed this need.

**D-2 - A small, opt-in, test-only API and separate schema.** Add
`tests/Shared/TestTiming.cs` (facade, state, scopes, sink),
`tests/Shared/TestTimingAttribute.cs` (assembly receiver), and
`tests/Shared/TestTimingRegistration.cs` (`[assembly: TestTiming]`). Link these
into `Antiphon.Tests` and the small probe host in S1. Other native TUnit projects
may link them in S3; projects without hooks still get TRX rows marked
`phaseCoverage=not-instrumented`, never an apparent zero cost.

`ANTIPHON_TEST_PHASE_ROOT` is the sole runtime enabling path; unset/blank is a true no-op:
no file creation, background activity, redaction, Stopwatch allocation or default
path lookup. `ANTIPHON_TEST_PHASE_RUN_ID`, `_GROUP_ID`, `_ATTEMPT` and `_SUITE`
carry supervisor metadata. Root must be a local absolute run-owned directory;
missing/invalid metadata disables only the recorder with a fixed diagnostic code.
Do not repurpose CARD-1313's `ANTIPHON_TEST_TIMING_ROOT` or auto-enable ordinary
CI/developer runs. All native rc children get these variables; discovery/build
maps and ordinary children of the shared process wrappers changed here clear them.
Direct per-test process launches are outside the choke-point census: segregate
any descendant evidence by host-instance nonce and never merge duplicate native
IDs into the parent's cases merely because the group ID matches.
Tests that explicitly launch the timing probe supply a fresh isolated root.
Snapshot runtime configuration once per test process. Unit/real-helper witnesses
may install an internal instance recorder in their own AsyncLocal scope, without
mutating process environment; that seam never enables ordinary tests. V-1/V-4
also exercise actual child environments, so the seam cannot stand in for opt-in
or lifecycle proof. At completion release interval buffers and cached owners.

The instance recorder supports `BeginPhase(enum phase, fixed operation)` and
`BeginProcess(exeBasename, nullable argc, category)` scopes. The static facade
resolves the current test; no application service needs injection or a dependency.
Use Stopwatch timestamps for intervals and UTC only for human correlation. Allow
phases `db-create`, `db-migrate`, `db-restore`, `db-snapshot`, `db-cleanup`,
`host-startup`, `host-shutdown`, `process-spawn`, `process-wait`, `filesystem`,
`network-loopback`, `our-code`, `fixture-setup`, `fixture-teardown`. Keep fixed
suboperations such as clone, migrate, restore, dump, create-tree, copy-tree,
delete-tree. No arbitrary caller message becomes a phase label.

**D-3 - Correct async ownership, fixture ownership and lifecycle.** Early start
creates a state object in `TestContext.StateBag`; late end closes it once and
captures framework outcome. `AsyncLocal` carries an immutable scope-stack node,
not a mutable Stack shared by child tasks. On every entry, reconcile the cached
state against `TestContext.Current`/StateBag so callback execution-context
restoration cannot lose ownership or leak the previous test into another one.
Aggregate completed intervals under a per-test lock with unique scope IDs.
Concurrent sibling tasks have independent parent chains; closing twice is inert.
A scope begun in a test retains that owner even if disposed in another context.
Late disposal after test closure is discarded/counted, not credited to a new test.

Assembly startup/teardown and shared factory lifecycle use `recordKind=fixture`,
`fixtureId` and group identity, `testId=null`. Shared fixture work is recorded once,
with `shared=true`, even when first use happens inside a test. Its overlap can be
shown as observed waiting in that test but is not added twice to group totals.
Class construction/data-source initialization before start remains fixture or
unattributed time; do not claim the Early hook covers everything before method
execution. Per-test Before/After scopes may supply fixture-setup/teardown labels;
otherwise the envelope covers them without pretending to know their subphase.
TUnit's own outcome is observed, never overridden. No waiting-input session or
release timer is added; CARD-1083 parking behavior is unchanged.

**D-4 - Report intervals honestly.** Each completed test row contains version,
run/group/chunk/attempt/suite, assembly hash or MVID, native test ID, class, method,
safe argument descriptor, start/end UTC, monotonic total, outcome, phase intervals
and aggregated process entries. A process entry is `{exe, category, argc, count}`;
phase spans can reference its numeric ID. `argc=null` means a legacy raw argument
string was used; never parse it just to count it. Record exception/cancellation as
fixed outcome codes, never exception messages or stack traces.
Represent intervals as millisecond offsets from a recorded process monotonic
epoch, with case start/end offsets in the same units. Keep the clock frequency in
the shard header; never compare raw tick values from unrelated process clocks.

Store raw bounded intervals; the report computes inclusive per-phase union and
exclusive wall allocation. Nested children take precedence over enclosing setup
and fixture phases; overlapping sibling phases of different categories produce
an explicit `overlap` bucket, not arbitrary priority. Cap intervals to the recorded
case envelope. A deterministic sweep uses `[start,end)` segments; zero-length
segments have zero cost. Coarse `other` includes overlap, residual work and
unmeasured categories. `our-code` is **unattributed remainder**, not measured CPU
or proof the time was spent in product code. Report its percentage. Never sum
inclusive categories or parallel tests to call the result group wall time.

At test end, seal still-open scopes at that boundary with `incomplete=true`; preserve
uncertainty. A killed host may have starts without completions: summary uses its
TRX if available, otherwise calls it incomplete and never manufactures Passed.
UTC jumps cannot make negative durations: monotonic intervals remain usable;
misaligned TRX/hook windows get a quality flag, not forced normalization.

**D-5 - Exact joins and privacy before convenience.** `testId` is the framework's
expanded `TestDetails.TestId`; group identity includes run + suite + chunk +
attempt and assembly identity, so repeats/retries and different builds cannot
collide. Use direct native-ID/TRX `testId` equality as the join contract, consistent
with the release authority's terminal lookup. V-1 proves this against real pinned
TUnit/MTP output, including parameterized/repeated cases. If it is not equal,
Code must document and test the pinned producer's actual conversion before using
it; a class/method/display/time-order guess is prohibited. Ambiguous/missing joins
remain visible, and do not remove the TRX-only row.

`args` contains ordinal/type/null descriptors, with values omitted by default.
Never call arbitrary argument `ToString`, write raw display names (which may embed
arguments), hash secret argument values, log URLs/headers/connection strings,
environment, cwd, SQL, file paths, process argv/stdout/stderr or exception text.
Class/method and executable basename are normalized and length-bounded. For any
retained free text use the existing `DiagnosticsRedactor(includePaths:false)`
rules through a test-only adapter in Antiphon.Tests; other shared consumers use
strict omission until an adapter exists. Synthetic token canaries test both paths.
Existing `LandingEvidence` writes arguments and is **not** a safe payload template.
The summary must also omit raw TRX displays/output, including in CSV/Markdown;
prefix spreadsheet formula-leading strings and escape Markdown cells. Original
TRX custody/visibility is unchanged.

**D-6 - Best-effort evidence without per-phase I/O.** Keep bounded in-memory spans
per active case (4,096, then increment `droppedPhaseCount`); write a small start row
and one complete JSONL row per test, plus fixture rows and a final health row.
One process owns one `cases-<pid>-<random-start-nonce>.jsonl` shard, opened with
CreateNew. A per-process sink lock protects complete UTF-8 lines; two processes
never append to one file, including after PID reuse. Do not use a fleet mutex,
fsync per phase, reread existing JSONL, or create one file per phase. The sink is
buffered and flushed at complete records and assembly completion. Timing append,
serialization, initialization, disposal and flush exceptions are contained;
disable a failing sink without retries and emit only a fixed failure code once.
No background worker/process or new delivery queue is necessary. Missing shards,
torn final lines and missing health completion are detected by summary and cannot
be called complete timing evidence. A full disk/unwritable path changes no test
exception/result/timeout. The measured overhead includes final flush costs.

**D-7 - Instrument a few real boundaries, preserving their behavior.**

| Slice | Choke point / proposed files | Measurement boundary |
|---|---|---|
| S1 | `TestDbFixtureLifecycle.cs`, `TestDbFixture.cs` | Existing create/start, migration, cache restore/capture, clone and drop calls. Assembly setup/teardown is shared; clone semaphore wait gets fixed `db-create/queue` suboperation. No cache, SQL, retry or connection changes. |
| S1 | `TestHelpers/AntiphonWebAppFactory.cs` | Real base CreateHost/CreateServer, with one outermost startup per factory and both disposal forms deduplicated. Record success/failure duration, keep the refusing runner and owned DB. No Program changes. |
| S1 | `TestHelpers/LandingGitFixture.cs` | Existing FixtureGit.StartProcess measures launch; RunAsync wraps base execution, ending in finally on failure/cancellation; subtract nested spawn union for wait. Existing injection callbacks/argv remain unchanged. No extra processes just to discover names/counts. |
| S2 | `tests/Shared/ReadOnlyDirectory.cs`, `Antiphon.Tests.csproj`, `Antiphon.SessionRunner.Tests.csproj` | One filesystem/delete-tree scope around the existing clear/read-only/delete behavior. These are the two current consumers; link the facade into both in the same commit so neither stops compiling. |
| S3 | `Scripts/ScriptHarnessProcess.cs`, `LinuxScriptHarnessProcess.cs`, `WindowsScriptHarnessProcess.cs`; `Checkpoints/CheckpointFixtureProcess.cs` | Measure owner construction/native start and awaited completion/pipe drain separately where available. Existing cancellation, process-group/Job Object cleanup and errors remain authoritative. Opaque wrappers say combined-wait, not invented launch time. |
| S3 | `TestHelpers/LandingGitFixture.cs`, `Checkpoints/CheckpointTestScope.cs` (`TempDir` and owned `DisposeAsync`), `AntiphonWebAppFactory.cs`, `TestSchemaCache.cs` | Root create/copy/delete and cache file read/write coarse spans, fixture-setup/teardown; do not instrument every File/Directory call. |
| S3 | `AntiphonWebAppFactory.cs`, new `TestHelpers/TestTimingStartupFilter.cs` | Test-only IStartupFilter wraps the existing request pipeline once with a network-loopback/server-pipeline scope. It records duration only, never URL/body/headers or extra requests; it calls/awaits next unchanged. This is server-side in-memory request time, not socket latency. If the request ExecutionContext has no current test, retain the factory fixture ID instead of guessing a client test ID. |

No generic product `Process.Start` interception, production EF interceptor, or
per-test source edits. DB operations' pg_dump/psql execute inside an owned container:
record db-snapshot/restore and known executable/count metadata at that call, not
as host-native process launch. Windows/native owner code is changed only in S3 and
requires its own Windows row before that slice is picked.

**D-8 - Supervisor manifest and opt-in activation.** Add
`scripts/lib/test-phase-evidence.ps1` and dot-source it from
`nightly-tests-impl.ps1`. At the actual group's generation boundary project current
class groups through the same `New-NightlyDiscoveryIndex` required-node view;
method/exclusion leaves use their existing node arrays. Do not independently
reimplement filter matching. Preserve discovery order within each group's
`plannedTestIds`; this is a reproducible manifest order, not promised test execution
order. Assert every required UID belongs to exactly one group. For a native lane
without an existing pre-execution discovery, move its existing discovery ahead of
execution once per suite; no duplicate discovery, test execution or altered filter.
Measure that timing separately from test overhead. No client/Vitest identities are
invented from TUnit; retain that lane's existing JSON timings where present.

Write `timing/groups-planned.json` atomically **before launching** its suite:
version, run/attempt/source/assembly/policy/cost-file digests, suite + chunk ID,
classes/filter, ordered IDs, planned seconds, pricing basis (measured/pace/model),
shared manifest-order label. Preserve the original cost number even when actuals
are very different. The current cost cache retains only seconds-per-node; read
the pinned cost file's `basis` once for sidecar metadata without changing that
cache or pricing. A mixed group retains per-class bases and says `mixed` at group
level. After owned process exit/cleanup and TRX availability, write
`timing/groups-executed.json` with exact TRX IDs/outcomes, actual host wall, case
sum/window, missing/extra IDs, and paths/digests relative to LogRoot. Keep separate
files so planned evidence is never silently rewritten by actuals. Partial runs
retain planned membership and explicitly missing actuals.

Set the new environment variables only on execution child maps. `-Profile rc`
automatically opts into the new sidecar root once S2 is present; ordinary nightly
and developer runs remain unchanged unless explicitly supplied a phase root.
Invoke summary in the supervisor's finally/report path after native children are
joined, catching timing failures separately; preserve the original exit/summary/
coverage/ledger verdict byte semantics. Do not introduce automatic retries. An
instrumentation-incomplete run can still have its original test verdict, but does
not satisfy this operator's timing acceptance and must be disclosed separately.

**D-9 - Standalone summary, no evidence authority changes.** Add
`scripts/summarize-test-phases.ps1 -RunDirectory <existing-log-root> -Top 50`
with new `scripts/lib/test-phase-evidence.ps1`. The former writes only
`timing/summary.json`, `timing/tests.csv`, `timing/summary.md`; it never edits TRX,
summary.json at LogRoot, receipts, selection, cost model, authority or ledger.
Validate path containment, source/run/assembly identity and schema before joining.
Read files once, stream JSONL, index IDs; do not scan all historical evidence or
reread a growing file for every case. A torn final line is ignored with a count;
malformed middle lines, duplicates/conflicts and stale/mixed IDs are reported as
incomplete, not silently averaged. No timing input means a useful TRX-only summary
with explicit unavailable phases. No TRX means no final pass claim.

JSON/CSV provide every test (including failures/skips), hook envelope and TRX total,
exclusive coarse totals, inclusive detailed phase totals, phase coverage, process
counts, group planned/actual, and quality flags. Markdown contains top N by TRX
duration, class totals plus case-window union, group wall/estimated cost comparison,
shared fixture cost, phase totals, overlap and remainder %. Do not call a sum of
concurrent durations a wall-time bottleneck. Keep CARD-1313 report unchanged.
PowerShell is ASCII-only, `#requires -Version 5.1`, no ternary/null-coalescing,
`ConvertFrom-Json -AsHashtable`, `SHA256.HashData`, or PS7-only JSON timestamp
assumptions. Use explicit invariant culture, UTC parsing, and SHA256.Create().

**D-10 - Retention and gate custody.** Supervisor owns `<LogRoot>/timing`; it lives
beside retained TRX and follows that run's retention. Checkpoint/probe tests use
an external fixture evidence directory or `.antiphon/<fresh-run>/timing`, never
bin/obj, the tool shadow copy, a previously sealed output or a source file.
Read-only reports can refer to sidecar digests separately; they are not gate
inputs or new public release assets. Unknown files outside an output tree cannot
grant cleanup authority. Unknown/changed files inside sealed output continue to
refuse cleanup; prove this instead of adding exclusions. CARD-1532's larger stamp
limit does not make mutable evidence in bin safe. No raw generated files are
committed. A timing failure is not grounds to restamp historical evidence.

**D-11 - Lanes, not machines.** Planning reads of GET `/api/runner-defaults` and
GET `/api/session-runners` returned defaults revision 2, an eligible default Linux
lane and an eligible Windows lane. Re-read before dispatch. Portable Code omits
`-Runner` and `-Platform`; native verification uses `-Platform Windows`, omitting
`-Runner` unless deliberately pinning a host. Never embed a fleet filesystem or
host location. Windows qualification is owed and must await the current run;
Linux evidence cannot satisfy the release's Windows coverage contract.

## Slices and commit boundaries

Each slice is 30-60 minutes of authoring; verification/queue waits are budgeted
separately. For each source group, first commit/push compiling witnesses against
missing/no-op behavior, run the named red row, then implement and commit/push.
Do not claim a compile or fixture failure is the red witness. Tests reference real
production/helper types, never a duplicated model of the recorder or batcher.

| Slice | Files and result | Tests / completion |
|---|---|---|
| S0 (this Plan, <=40 min) | This canonical plan and requested-path pointer. Existing CARD-1514 description extension. | Source/API/card inspection and Markdown checks only. Commit/push docs; no build. |
| S1 (45-60 min) | Shared facade/receiver/registration; Antiphon.Tests links and `TestHelpers/TestTimingRedactor.cs`; DB/host/FixtureGit scopes; new summary/library; `tests/Antiphon.TestTiming.Probe/{Antiphon.TestTiming.Probe.csproj,TimingProbeTests.cs}` staged by Antiphon.Tests (one parent project build). | `TestTimingRecorderTests`, `TestTimingLifecycleTests`, `TestTimingChokePointTests`, `TestPhaseSummaryTests`; CP-1 red then CP-2 green. Real PostgreSQL migration/restore/clone/drop, real safe WebApplicationFactory start/dispose and scratch Git. |
| S2 (30-60 min) | Supervisor manifest/env/report wiring, shared readonly-tree scope and compile links for its consumers; `Scripts/TestPhaseSupervisorTests.cs`, `Checkpoints/TestPhaseCustodyTests.cs`; small `docs/testing-and-build.md` timing paragraph after other owners settle. | CP-3 red then CP-4..CP-7 at one frozen S1+S2 SHA/build. This is the minimum complete rc candidate. CP-8 is its owed Windows selection. |
| S3 (30-60 min) | Remaining process/fs/loopback/shared setup scopes listed in D-7; remaining native test-project links for automatic lifecycle rows; `TestTimingRemainingChokePointTests.cs`. Update coverage inventory, never individual tests. | CP-9 red then CP-10; native CP-11 before S3 rc pick. If S3 slips, retain its named debt and pick only verified S1+S2. |

If red scaffolding requires a minimal no-op public API, label it as such and never
land it alone. S1/S2 code and tests are one coordinated change set; do not dispatch
them against overlapping files concurrently with other CARD-1514 work. Keep
`nightly-tests-impl.ps1` and shared documentation ownership serialized with any
active cost/budget tasks. A later S3 source change requires a new build; CP-n build
reuse is only inside a single frozen source group, never across commits.

## Verification design

### Inspection

Read the implementations named in Ground truth and D-7, the CARD-1491 plan format,
`docs/testing-and-build.md` checkpoint/slot/custody sections, release gate/authority
readers, TUnit 1.44 hook documentation in the installed package, the real
`TestSchemaSnapshotTests`/schema-cache lifecycle tests, `ScriptHarnessProcessTests`,
`CheckpointFixtureProcessTests`, `TimingJournalTests`, and `TestTimingReportTests`.
Existing safety classes supply fixtures, not permission to rerun whole classes.
V-1..V-15 below name new test methods with `C1514T_` prefixes to isolate this
extension from other CARD-1514 changes.

### Delivery inventory

| Producer -> destination | Persistence and identity | Recovery / observable receipt |
|---|---|---|
| TUnit Early/Late receiver + shared scopes -> per-process sink | Complete UTF-8 lines; run/suite/group/attempt/assembly/native ID; start/complete and health records | Failed append disables sink; TRX remains authoritative. V-1/V-4 read completed files after a real child exits, prove failed/cancelled cases and missing/torn records, and reconcile exact IDs. |
| Supervisor discovery/group generation -> planned manifest -> child env | Atomic planned file before first launch; exact group/ordered UID partition and source digests | If sidecar persistence fails, preserve test execution/verdict and declare timing incomplete. V-7 captures the real child environment/launch boundary and sees the file before execution. |
| Owned native completion + TRX -> executed manifest -> summary | Separate actual file and fresh input digests; no overwrite of plans | Summary after child cleanup, no background analysis left running. V-8/V-9 read files rather than assert a write call. Missing/extra/stale cases remain visible. |

This is local evidence persistence, not agent/session message delivery; there is no
new session queue or UserPrompt receipt. Tests cover pre-write, partial-write,
post-write/pre-summary and cancelled-host boundaries. In-memory sink assertions
alone cannot prove file persistence or framework identity/lifecycle.

### Proves it works now

Every row starts with a decisive red witness. Exact proposed methods and counts
are the executable contract; loops do not inflate TUnit execution counts.

| ID | Method(s), file and cases | Expected red first; final observable oracle |
|---|---|---|
| V-1 | `TestTimingLifecycleTests.C1514T_NativeLifecycle` (1) | No recorder: no complete rows. Parent launches the staged real TUnit probe OFF then ON: seven results (two parameterized, two repeat expansions, one ordinary pass, one exception, one cancellation), with async child work and instance Before/After. Assert exact child roster and unchanged framework outcomes from TRX, matching framework/native IDs, setup/body/teardown owned by correct ID and completion after After(Test). Parent remains green only by asserting the child failures; child failures are never gate tests. |
| V-2 | `TestTimingRecorderTests.C1514T_AsyncOwnership` (1) and `C1514T_IntervalAlgebra` (1) | Disabled attribution/incorrect summation fails literal ID and duration assertions. Use deterministic timestamp seam: case envelope [0,100), db [10,40) with nested db [15,25), concurrent process [30,60), fs [70,80); child union=60, overlap=10, remainder=40. Assert exclusive categories + overlap + remainder = 100. Async tasks/barriers prove sibling isolation and closing in a different context. |
| V-3 | `TestTimingChokePointTests.C1514T_DbHostGit` (1) | Missing real helper calls gives empty categories. Own cold/warm lifecycle/cache root as TestSchemaSnapshotTests does; verify real migrated and restored histories, cloned DB removed, safe factory responds then disposes, real scratch Git exits. Assert db-migrate/restore/snapshot/create/cleanup, host-startup/shutdown and process-spawn/wait with correct coarse ownership, without connection/argv content. No timing-only fake replacing these actual operations. |
| V-4 | `TestTimingRecorderTests.C1514T_AbsentAndFailure` (7 arguments) | Cases: unset env, blank env, invalid metadata, unwritable path, serialization/write fault, throwing body, cancelled body. Before failure isolation exists, exceptions escape or output appears when disabled. Final: original return/exception/cancellation unchanged, no output when disabled, fixed failure status with no raw error; tests consume a local instance config, never mutate global env concurrently. |
| V-5 | `TestTimingRecorderTests.C1514T_ClosureAndWriters` (4 arguments) | Cases: scope never closed, nested/overlapping scopes, parallel threads, two process writers with same supplied PID but different nonce. Assert one completion per case, closure flags, valid whole JSON lines, distinct shards and no cross-case phases. Two processes use staged probe, under ProcessSpawnLimit. |
| V-6 | `TestTimingRecorderTests.C1514T_SecretsAndBounds` (1) | Unsafe serialization leaks synthetic canaries. Supply secret-shaped argument/URI/header/env/path/exception payloads and an argument whose ToString throws; assert they never enter bytes, only descriptors/exe basename/argc. 4,097 spans exercise bound/drop count. CSV formula-leading and Markdown cell inputs are encoded. |
| V-7 | `TestPhaseSupervisorTests.C1514T_PlannedManifestAndEnvironment` (1) | Current supervisor writes no planned manifest/env. Run the real producer/packer against literal discovery with prefix collisions, >900s class, >30 operands, parameterized method and excluded leaf. Compare before/after filters and ordered launches, literal UID partition, cost basis and exclusions. At the owned-launch boundary read the manifest; one staged harmless probe must also launch through real Invoke-NightlyOwnedProcess and observe the five intended variables using the real ProcessStartInfo environment dictionary. Unset is an actual removed key, distinct from empty. Discovery/build and covered ordinary child wrappers lack phase env. |
| V-8 | `TestPhaseSupervisorTests.C1514T_ActualAndFailurePaths` (1) | Missing actual manifest/finally summary fails. Replay fresh real probe TRX plus owned launch results: success, failed case, cancellation/no TRX, sidecar unwritable, summary throws. Actual IDs come from TRX, source/run binding retained; original suite exit/coverage/cleanup outcomes exactly unchanged. |
| V-9 | `TestPhaseSummaryTests.C1514T_MergeAndQuality` (6 arguments) | Cases: full match, TRX only, torn tail, conflicting duplicate, stale run/assembly, ambiguous/missing ID. Literal XML/JSONL plus a real probe fixture; verify exact CSV/JSON rows, top-N/class/group reductions, shared cost separate, missing coverage and remainder. Never create a pass from phase-only input. |
| V-10 | `TestPhaseCustodyTests.C1514T_GateAndCustody` (1) | A disabled reader may pass a missing summary check, but missing expected sidecars is red. Use real release-gate validation fixture and BuildConfinement types: original good/failed verdicts and input digests identical with valid/malformed extra timing; evidence outside bin survives; new unowned bin entry still refuses cleanup. Small ordinary preexisting files suffice here; do not duplicate CARD-1532's large-file battery or change its rules. |
| V-11 | `TestTimingChokePointTests.C1514T_ReadOnlyTree` (1) | Missing fs scope is red. Delete actual readonly Git-style tree through ReadOnlyDirectory, assert deletion and filesystem phase, no traversed link target. The same method executes natively on Windows. |
| V-12 | `TestPhaseSummaryTests.C1514T_PS51AndRerun` (1) | New summary absent fails invocation. Real PowerShell invocation against saved input produces all three outputs with deterministic semantic contents on rerun, no input byte changes, ASCII source parses. Windows row executes both pwsh and Windows PowerShell 5.1, asserting identical decoded JSON/CSV results. No test catches unsupported PS syntax and calls it a pass. |
| V-13 | `TestTimingChokePointTests.C1514T_Overhead` (1) | Missing records fails coverage assertion. Paired real workload protocol below yields observed overhead; <2% is required, not assumed. Preserve all raw times and counts in ignored evidence. |
| V-14 | `TestTimingRemainingChokePointTests.C1514T_ProcessAndFilesystem` (1) | Remove each new shared wrapper scope: its own positive category/count assertion fails. Real ScriptHarness owner launches harmless local pwsh and dotnet probe, checks cancellation/cleanup and create/copy/delete; parent asserts args and env were not captured. Run on both native OS lanes. |
| V-15 | `TestTimingRemainingChokePointTests.C1514T_LoopbackAndSharedFixture` (1) | Startup filter/scope disabled leaves category/fixture record absent. Real safe factory client request, one shared fixture used by two tests, and owned teardown; literal call count, identical response and exactly one shared cost. Prove the observed request ExecutionContext binding, including suppressed-flow fixture fallback. No live runner contact. |

### Guards the regression

| ID | Bound method | Preserved invariant |
|---|---|---|
| R-1 | V-1 and V-4 methods | Original throwing/cancelled results, setup/teardown order and retries unchanged; no cross-test AsyncLocal leakage. |
| R-2 | V-7/V-8 methods | Identical filter/launch order, required/excluded UID partition, policy/cost values, watchdog arguments and original supervisor exit. No timings feed selection. |
| R-3 | V-10 method | Gate input bytes/digests/verdicts and unknown-output cleanup refusals unchanged. No JSON receipt schema expansion. |
| R-4 | V-3/V-11 methods | DB histories, clone disposal, production-runner refusal, real Git behavior and readonly/link deletion semantics preserved. |
| R-5 | V-6/V-9 methods | No raw secret-bearing fields; missing/ambiguous evidence never counted as complete or zero duration. |
| R-6 | Existing `ScriptHarnessProcessTests.Caller_cancellation_kills_tree_before_returning` and `Passing_case_preserves_inventory_and_argument_boundaries` | Existing owned-process cleanup, argument boundaries and inventory assertions remain green after S3. Native Windows row is mandatory; do not replace by fake owners. |

### Absent-input and interrupted-operation matrix

| Input / operation absent or incomplete | Expected state | Bound witness |
|---|---|---|
| Env unset or blank | No file/directory/state allocation; TRX-only summary when asked | V-4 unset/blank, V-9 TRX only |
| Group/run/attempt missing, root invalid | Recorder disabled with fixed reason; original test outcome | V-4 invalid metadata |
| Unwritable/full path or sink throws | Failed recorder, original pass/failure/cancellation intact | V-4 path/write cases, V-8 |
| Concurrent threads/processes | Whole lines, distinct process shards, no foreign ID ownership | V-2, V-5 writers |
| Scope not closed, nested/out-of-order disposal | Seal at test end, incomplete flag, union math; double dispose inert | V-2/V-5 closure |
| Test throws or cancels | Completion if framework end fires; original failed/cancelled outcome | V-1, V-4 |
| Host killed / no TRX / partial JSONL | Starts/manifest retained, missing final evidence, no manufactured outcome | V-8, V-9 torn tail |
| No phase for a test/project | TRX duration retained, coverage unavailable, residual explicitly unattributed | V-9 |
| Reused PID / retry / duplicate display names | Nonce/attempt/expanded UID separation; no display-order join | V-1, V-5, V-9 |
| Wall clock discontinuity / phase beyond case end | Monotonic duration preserved, clipped span/quality flag, never negative | V-2 interval method |

### Guard inventory

G-1..G-16 below identify the independently bypassable guards and each maps to one
PC. The same method can detect different defects, but each control changes only
its own named guard and asserts its own label.

### Positive controls

These are independently bypassable controls, not one shared switch. Mutation uses
one exact method at a time, a bound pack and the smallest compiling defect; all
mutations are restored before the method's green run. Ordinary Code runs V/R;
post-land Mutation runs these PCs. A zero selection, compile error, timeout or
fixture error is not the expected red. Prefix assertion messages with the label.

| Guard | PC | Compiling defect / exact target method / red assertion label |
|---|---|---|
| G-1 disabled-env no-op (D-2) | PC-1 | Remove env guard; `/*/*/TestTimingRecorderTests/C1514T_AbsentAndFailure` unset case: `disabled-writes-nothing` |
| G-2 sink failure containment (D-6) | PC-2 | Rethrow sink IOException; same method write case: `sink-preserves-outcome` |
| G-3 current-test binding (D-3) | PC-3 | Reuse stale state across TestContext IDs; `/*/*/TestTimingRecorderTests/C1514T_AsyncOwnership`: `owner-is-current-test` |
| G-4 scope union/nesting (D-4) | PC-4 | Sum child spans instead of union; `/*/*/TestTimingRecorderTests/C1514T_IntervalAlgebra`: `exclusive-total-equals-envelope` |
| G-5 shard separation (D-6) | PC-5 | Omit nonce from shard path; `/*/*/TestTimingRecorderTests/C1514T_ClosureAndWriters`: `writers-have-distinct-shards` |
| G-6 data minimization (D-5) | PC-6 | Add raw argument ToString to args field; `/*/*/TestTimingRecorderTests/C1514T_SecretsAndBounds`: `secret-canaries-absent` |
| G-7 bounded data (D-6) | PC-7 | Remove span cap; same method: `span-budget-recorded` |
| G-8 exact group membership (D-8) | PC-8 | Replace required view by all class nodes for one excluded leaf; `/*/*/TestPhaseSupervisorTests/C1514T_PlannedManifestAndEnvironment`: `required-partition-exact` |
| G-9 observational supervisor (D-8) | PC-9 | Replace original suite failure exit with summary success; `/*/*/TestPhaseSupervisorTests/C1514T_ActualAndFailurePaths`: `original-verdict-preserved` |
| G-10 input identity binding (D-9) | PC-10 | Accept stale assembly/run ID; `/*/*/TestPhaseSummaryTests/C1514T_MergeAndQuality`: `stale-input-not-joined` |
| G-11 missing evidence is not success (D-4/D-9) | PC-11 | Turn start-only row into Passed; same method: `missing-terminal-remains-incomplete` |
| G-12 output custody unchanged (D-10) | PC-12 | Put timing sidecar into fixture sealed-bin root instead of evidence root; `/*/*/TestPhaseCustodyTests/C1514T_GateAndCustody`: `timing-outside-cleanup-root` |
| G-13 per-test teardown retained (D-3) | PC-13 | Set end receiver Stage=Early; `/*/*/TestTimingLifecycleTests/C1514T_NativeLifecycle`: `after-hook-in-envelope` |
| G-14 secret-free child propagation (D-2) | PC-14 | Leave phase root in probe's ordinary product-child environment; V-7 method: `only-native-test-child-enabled` |
| G-15 S3 native process lifecycle (D-7) | PC-15 | Move process completion scope before awaited exit/pipe drain; `/*/*/TestTimingRemainingChokePointTests/C1514T_ProcessAndFilesystem`: `wait-includes-owned-completion` |
| G-16 shared cost deduplication (D-3) | PC-16 | Charge shared host startup once per consumer; `/*/*/TestTimingRemainingChokePointTests/C1514T_LoopbackAndSharedFixture`: `shared-fixture-once` |

Inventory: 16 guards, 16 distinct PC mappings, 0 unmapped; PCs **designed, not run**
in this Plan. PC-15/16 belong only to S3. Native deletion/owner semantics retain
existing safety tests; this card does not redesign those guards.

### Overhead protocol

V-13 uses the same built probe/helper binaries, host, isolated data/cache state,
limit widths and exact roster. Warm once, then run nine paired OFF/ON samples in
alternating order (OFF/ON, ON/OFF, ...). A sample is a fixed workload with real DB
clone/drop, safe host request, scratch Git, filesystem create/delete and async
in-process work; choose sufficient fixed repetitions for >=10 seconds per sample,
with one shared setup before measurement and its cost reported separately. This
is a benchmark inside one method/one host, not ten TUnit/full-suite relaunches.
Also measure 100,000 disabled/enabled empty scopes to expose fast-test cost, using
1,000 fresh recorder states with 100 scopes each so the span cap is never reached.
Report absolute ns/scope and bytes/case separately: a near-zero disabled empty
loop is not a sensible denominator for the 2% workload ratio. Do
not let database delay hide a costly scope implementation. No forced GC, sleeps
to dilute overhead, changing test ordering, or new production timeout.

Keep per-sample wall/CPU/allocation/bytes/records/dropped counts; measured interval
includes recorder final flush. Compute paired relative overhead
`(onWall-offWall)/offWall` and its median, plus bootstrap 95% upper bound. Accept
only median and upper bound <2%, matching work/outcomes and zero dropped records.
The 2026-10-24 Debug repair increases the predeclared minimum from five to nine
pairs: one outlier controls the bootstrap median in 5.792% of five-pair resamples,
but only 0.145% of nine-pair resamples. Keep the same bootstrap median, all raw
ratios, ten-second arm minimum and 2% threshold; do not discard slow pairs or
collect until a bound passes. The Linux baseline at eb5f263211ee7edae81ae75e28e5a4f3bc627399
measured median 0.222%, upper 0.358%, scope-only OFF/ON 81 ns/3.28 us and
0/694 allocated bytes per scope. Whole-case ON including serialization/flush
was 7.09 us/scope. Its five-pair method took 147 s; nine pairs provisionally
budget about 227 s (+80 s), within the unchanged 300 s test timeout. Native
Windows CP-8 remains required; these Linux measurements do not qualify it.
A noisy/contended result is inconclusive, not success: fix the overhead or request
one specifically budgeted follow-up measurement, never rerun until green. Run the
same protocol on the Windows preflight. Supervisor manifest/discovery/summary
cost is also timed and reported relative to group wall; it is not silently hidden
in build or slot wait. The complete next rc run records sink wall time and event
counts; a microbenchmark-based estimate of remaining recorder cost is labelled
estimated. That ON-only run is not a counterfactual overhead measurement. The
paired preflight is the <2% witness; no extra full qualification is commissioned
solely to obtain an OFF baseline.

### Checkpoints

Each row has one exact filter, including trailing `*` on every OR operand
(CARD-0931); literal pipes are Markdown-escaped. **One Antiphon.Tests project build
per frozen source group**, shared by all its serial rows; the staged small probe is built
transitively and its launches never build. Red/green sources and native OS lanes
necessarily have separate builds. All new process-spawning classes carry the
assembly-local ProcessSpawnLimit; real DB tests own isolated stores. No
Antiphon.Agents.Pty.Tests is run alongside them. The Lane column is expressed in
the Group name because the importer accepts only its documented column schema.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1514t-s1-red/` | linux-foundation-red | `/*/*/TestTimingLifecycleTests/C1514T_NativeLifecycle*` | V-1 | 1 executed, missing timing assertion fails; staged child executes; no build/fixture failure | 1 | 7 | 10 | true |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1514t-s1/` | linux-foundation | `/*/*/(TestTimingRecorderTests*\|TestTimingLifecycleTests*\|TestTimingChokePointTests*\|TestPhaseSummaryTests*)/C1514T_*` | V-1..V-6, V-9, V-12, R-1, R-4, R-5 | S1 roster 23 results, 0 failed/skipped; S2-only ReadOnlyTree/Overhead methods not yet added | 23 | 12 | 17 | true |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c1514t-s2-red/` | linux-supervisor-red | `/*/*/TestPhaseSupervisorTests/C1514T_PlannedManifestAndEnvironment*` | V-7 | 1 executed, missing planned-file assertion fails | 1 | 7 | 10 | true |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c1514t-final/` | linux-recorder-final | `/*/*/(TestTimingRecorderTests*\|TestTimingLifecycleTests*)/C1514T_*` | V-1, V-2, V-4..V-6, R-1, R-5 | all 15 results, 0 failed/skipped | 15 | 7 | 10 | true |
| CP-5 | S1-S2 | CP-4 | linux-real-fixtures | `/*/*/TestTimingChokePointTests/(C1514T_DbHostGit*\|C1514T_ReadOnlyTree*)` | V-3, V-11, R-4 | 2 executed, 0 failed/skipped | 2 | 3 | 5 | true |
| CP-6 | S1-S2 | CP-4 | linux-manifest-report-custody | `/*/*/(TestPhaseSupervisorTests*\|TestPhaseSummaryTests*\|TestPhaseCustodyTests*)/C1514T_*` | V-7..V-10, V-12, R-2, R-3, R-5 | 10 executed, 0 failed/skipped | 10 | 3 | 5 | true |
| CP-7 | S1-S2 | CP-4 | linux-overhead | `/*/*/TestTimingChokePointTests/C1514T_Overhead*` | V-13 | 1 executed, coverage + overhead protocol accepted | 1 | 4 | 5 | true |
| CP-8 | S1-S2-Windows | `tests/Antiphon.Tests -> bin-c1514t-win/` | windows-minimum-preflight | `/*/*/(TestTimingRecorderTests*\|TestTimingLifecycleTests*\|TestTimingChokePointTests*\|TestPhaseSupervisorTests*\|TestPhaseSummaryTests*\|TestPhaseCustodyTests*)/C1514T_*` | V-1..V-13, R-1..R-5 | all 28 executed, 0 failed/skipped; real Windows + PS5.1 | 28 | 12 | 18 | true |
| CP-9 | S3-red | `tests/Antiphon.Tests -> bin-c1514t-s3-red/` | linux-remaining-red | `/*/*/TestTimingRemainingChokePointTests/C1514T_ProcessAndFilesystem*` | V-14 | 1 executed, missing wrapper attribution assertion fails | 1 | 7 | 10 | true |
| CP-10 | S3 | `tests/Antiphon.Tests -> bin-c1514t-s3/` | linux-remaining | `/*/*/(TestTimingRemainingChokePointTests*\|ScriptHarnessProcessTests*)/(C1514T_*\|Caller_cancellation_kills_tree_before_returning*\|Passing_case_preserves_inventory_and_argument_boundaries*)` | V-14, V-15, R-6 | 4 executed, 0 failed/skipped | 4 | 8 | 12 | true |
| CP-11 | S3-Windows | `tests/Antiphon.Tests -> bin-c1514t-s3-win/` | windows-remaining | `/*/*/(TestTimingRemainingChokePointTests*\|ScriptHarnessProcessTests*)/(C1514T_*\|Caller_cancellation_kills_tree_before_returning*\|Passing_case_preserves_inventory_and_argument_boundaries*)` | V-14, V-15, R-6 | 4 executed, 0 failed/skipped, real Job Object owner | 4 | 8 | 12 | true |

Every class and method operand in an OR group ends in `*`. Prefixes name only
the listed classes/methods; assert the exact executed roster as well as Min so a
future prefix-sharing class cannot silently expand the selection.

CP-2's S1 count is 23: recorder 14 (2 single methods + 7 absence + 4 closure + 1
privacy), lifecycle 1, real fixture 1, summary 7 (6 quality + 1 PS invocation).
CP-4 is recorder 14 + lifecycle 1; CP-6 is supervisor 2 + summary 7 + custody 1;
CP-8 is 15 + 2 real fixtures + 10 report/supervisor/custody + 1 overhead = 28.
These are planned expanded result counts. Preserve them when implementing the
named data rows; document a material roster change before executing a different
selection. No zero result can satisfy a row. All portable CP rows use the Linux lane;
CP-8/11 use Windows only, with no silent platform skip. A nonselected OS row is
owed, not satisfied. Real native Windows execution is not authorized in this Plan.
The After tokens ending in `-red`/`-Windows` are named source/OS groups, selected
explicitly with `--rows`; they are not a request to run all rows in one invocation.

Run the checkpoint tool once per committed source group, select only its rows,
and await completion (`wait` until exit is not 75). Use the repository documented
tool front door and source SHA; it owns the build slot. Every probe invoked by a
row inherits that host slot and must not acquire it recursively or run a build.
Any separate driver uses `scripts/build-slot.ps1`; slot timeout exit 4 means not
run. Retain exact CHECKPOINT lines/TRX/receipt/source provenance and red outcomes.
After a failure, test only the exact failed method at the recorded base before
calling it inherited. Remove task-created bin-* roots through owned cleanup.

### Cost

Initial estimates: S1/S2 authoring 75-120 min; Linux CP-1..7 **43 min**, of which
29 is green scope and 14 red build/witness cost.
Windows minimum CP-8 **18 min**. S3 authoring 30-60 min; CP-9/10 **15 min**, Windows
CP-11 **12 min**. Queue waits are additional and must be reported. Build sharing
saves three redundant final Linux project builds (about 15-24 min at a 5-8 min
build); no unmeasured optimization saving is promised. Minimum first Linux
implementation + Windows preflight is approximately 2.5-3 hours excluding queues.

Mutation floor: 14 minimum PCs + 2 S3 PCs. Exact-method red/restored-green pairs:
PC-1/2/3/4/6/7/10/11 are 2 min each, PC-8/9/13/14 are 4 min each, and PC-5/12
are 6 min each. Estimate minimum PCs 44 min plus isolated pack
build/setup 8 min; S3 PCs 12 min plus 6 min setup. These are separately commissioned
post-land controls, not a second full rc run. Windows native PCs use the Windows
lane. Report actual per-PC counts and costs; never claim these designed PCs ran.

### Out of scope

No CPU sampling, allocation attribution beyond the overhead probe, Defender/ETW,
container-internal syscall tracing, general SQL/HTTP tracing, or full attribution
for direct per-test I/O outside shared helpers. Existing process limiters are not
queue instrumentation. Shared fixture time and residual percentages make those
gaps explicit. No product behavior, timeout, parallel width, ordering, schema
migration, full-suite selection or gate acceptance rule changes. Windows rc
qualification remains the existing operator-owned run, not a Plan test command.

## Rollout, rc pick list and acceptance

Startup impact: the modified library is imported by `scripts/nightly-tests.ps1`
and its harnesses, not AppHost startup. New test code has no product assembly
consumer. Code still records the changed-file OS/startup witness inventory and
runs `scripts/check-os-witnesses.ps1` and `scripts/check-evidence-diff.ps1` over
its full base..HEAD, using fresh receipt provenance. CP-8 proves native script,
environment dictionary and readonly filesystem behavior; CP-11 proves the real
Windows process owner if S3 is included. These are actual native obligations,
not a platform seam or a restart claim.

1. Land the Plan, then commission S1/S2 Code on the current master with this
   manifest; commit/push each red/fix slice, freeze a green tip and obtain ordinary
   Review. CP-8 at that source is an owed native obligation before a Full approval;
   a Linux-only report cannot claim it complete. No Interim approval can authorize
   the land. CARD-1514 optimization
   changes and other fixes retain their own review/evidence.
2. The exact **scope** of the instrumentation pick list is S1 recorder/hooks +
   DB/host/Git helpers + summary + tests/probe, then S2 manifests/env + readonly-tree
   scope/consumer links + tests. Pick S3 only after its native row is green.
   **There are no instrumentation commit SHAs yet.** Code must report ordered full
   red/fix SHAs and the final reviewed master landing SHA; the rc port must replace
   this list with actual source->rc full-SHA mappings before launch. Do not invent
   future SHAs, cherry-pick a no-op scaffold alone, or pick an entire master range.
3. Verified existing stamp fixes are `9a044e401404834260a8c44834bf771a3ed2a399` and
   `6b3e7630d2a2bde2aa60eb9344b35cd0a1c18f30` on master. They are checkpoint-tool
   prerequisites for reliable local verification, **not required rc product picks**
   for writing sidecars. CARD-1531 admission clocks and unrelated optimizations are
   not instrumentation dependencies. Current rc already has database lifecycle,
   cost packer, discovery and CARD-1313 exact-roster timing code. Resolve port
   contexts against that rc, not by importing unrelated projects/policy changes.
4. A separately owned rc port applies only the reviewed instrumentation deltas to
   `rc/2026-10-20`, after the caller's fix picks, and records ordered full SHA picks,
   final rc SHA, relevant file/blob hashes and a new build. Use the same exact
   Windows CP-8 selection against that **final rc source**, CP-11 if S3 is present.
   This is a separately budgeted 18-minute rc-port rerun (12 more for S3), not reuse
   of a master receipt at a different SHA. These Windows rows are **owed**.
   Do not touch the current supervisor PID 209072,
   its files, checkout, environment or descendants; do not reuse its receipts for
   the new tip or update files underneath its process.
5. Before the next full run, the candidate must contain S1+S2, have real Windows
   preflight proving hook identity/readonly cleanup/PS5.1 and <2% overhead, and
   show the actual planned manifest before the first execution launch. Verify the
   child phase env points into that new run's logs and the first completed group
   has matching TRX and JSONL IDs plus usable summary. A preflight with missing
   attribution is not readiness merely because the tests pass.
6. The existing full Windows rc profile then qualifies the new tip. Timing is
   extra private evidence: gate pass/fail/coverage rules remain identical. Retain
   a summary of per-test, class and group times, phase coverage, shared costs,
   remainder and measured overhead with its source/run identity. Unknowns stay
   unknown; do not refresh prices or optimize tests in this run.

This instrumentation activates by rebuilding and launching the **new qualification
supervisor/test binaries** at the final rc SHA. Master land is not activation;
a server/AppHost restart is neither necessary nor proof. Rollback disables the
phase root for a later run or ports a reviewed revert; it does not alter current
receipts and cannot satisfy the operator's requested instrumented run. When time
is short, complete verified S1+S2 and defer S3 explicitly. Per-test time plus group
membership alone is not the promised minimum unless coarse db/host/process/fs/
other coverage and its truthful remainder are also present.

--- next stage ---
next: code
handoff: Implement CARD-1514 timing S1+S2 from this plan with red witnesses, one build per frozen checkpoint group, private sidecars and unchanged gate semantics; bind checkpoint counts, measure <2% overhead, obtain ordinary Review, and report exact rc pick SHAs plus Windows CP-8 owed. Do not touch the live qualification.
artifact: docs/superpowers/plans/2026-10-23-card-1514-test-timing-instrumentation-plan.md
