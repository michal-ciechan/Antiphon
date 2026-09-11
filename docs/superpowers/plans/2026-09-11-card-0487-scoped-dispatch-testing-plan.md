# CARD-0487: scoped dispatch verification with a proven nightly backstop

Date: 2026-09-11. Stage: Plan; complexity: hard. Separate TestDesign is required.
Inspected checkout: `11b6c7ff0adf2ae69a0ad13c1c61196eda3e5ff5`.

The named Windmill nightly is absent, so reducing dispatch verification on the
assumption that it already protects landed changes is unsafe. Establish and prove
the scheduled backstop first, retain the existing test projects, make slow cost
and execution eligibility explicit, then replace the mandatory whole-Unit sweep
with TestDesign's affected-test selection. Preserve every real-Git capstone and
every safety guard's distinct positive control.

This document designs the change. It does not register jobs, send notifications,
change running services, or authorize a test omission before the readiness gate.

## Ground truth: investigate the nightly first

### Direct operational evidence

On 2026-09-11 at **11:33:27 UTC**, read-only SQL over the existing
`ssh mc@server2` connection inspected Windmill's `schedule`, `script`, `v2_job`
and `v2_job_completed` tables in workspace `mc`. At **11:36:04 UTC**, a second
read-only query searched all workspaces to exclude a misplaced registration:

| Observation | Result | Consequence |
|---|---|---|
| Schedules with `nightly` in their path or target script | **0 rows**, including outside `mc` | There is no enabled, disabled or paused nightly schedule in this database. |
| Script records with `antiphon_nightly` in their path, including archived/deleted versions | **0 rows** | `u/lndcobra/antiphon_nightly_tests` is not registered. |
| Retained jobs with `antiphon_nightly` in their runnable path | **0 rows** | No retained scheduler execution evidence. This does not prove that no historical job ever existed outside retention. |
| Other known Antiphon schedules and `desktop_heartbeat` | Present and enabled | The query reached the expected live deployment; absence is not inferred from a failed connection. |
| `C:\Antiphon\nightly\last-run.json` | Failed, September 4, feature ref `feat/card-task-2aab4b1c`, SHA `829e516a797044a354894790c06d6f8ebb7944e8` | Seven-day-old local evidence, not a recent master nightly. |
| Referenced `logs\2026-09-04-1748\summary.json` | Only `client` selected: **719 passed, 3 failed, 0 skipped**; builds succeeded | A manual/client qualification run cannot establish backend coverage. `reportExit=0` records that this run's reporting command succeeded. |
| Local nightly log directories | Only `2026-09-04-1735` and `2026-09-04-1748` | No local evidence of intervening scheduled execution. |

The planning task retained the SQL and output at
`C:\Antiphon\worktrees\card-task-8afb5da3\.antiphon\task-8afb5da3-nightly-audit.sql`
and `.txt`. They contain no credentials. Reproduce the essential census without
minting a token or modifying Windmill:

```powershell
$nightlySql = @'
BEGIN READ ONLY;
SELECT now() AS observed_at;
SELECT workspace_id,path,enabled,script_path,schedule,timezone,on_failure
FROM schedule WHERE path ILIKE '%nightly%' OR script_path ILIKE '%nightly%';
SELECT workspace_id,path,archived,deleted,tag,timeout
FROM script WHERE path ILIKE '%antiphon_nightly%';
SELECT j.workspace_id,j.id,j.runnable_path,j.created_at,c.status,c.completed_at
FROM v2_job j LEFT JOIN v2_job_completed c USING(id)
WHERE j.runnable_path ILIKE '%antiphon_nightly%'
ORDER BY j.created_at DESC LIMIT 15;
COMMIT;
'@
$nightlySql | ssh -o BatchMode=yes -o ConnectTimeout=10 mc@server2 'docker exec -i windmill-windmill_db-1 psql -X -U windmill -d windmill -v ON_ERROR_STOP=1'
Get-Content -Raw -LiteralPath 'C:\Antiphon\nightly\last-run.json'
```

The repository-referenced Windmill skill is
`C:\Users\lndco\.claude\skills\windmill\SKILL.md`. It describes workspace
`mc`, desktop-tag routing and the Linux-worker-to-Windows SSH bridge. This audit
used read-only SQL, not its temporary-superadmin-token creation procedure.

### Source contracts versus the card's assumptions

| Assumption | Actual code/evidence | Design consequence |
|---|---|---|
| The documented 00:30 Europe/London job is operating. | Documentation describes the intended deployment; live registration is absent. CARD-0124's plan is not deployment evidence. | Registration, execution and monitoring are prerequisites, not optional follow-up. |
| Nightly runs the full repository suite. | `nightly-tests.ps1` defaults to `antiphon,agents-pty,client`; only `e2e` is additionally accepted. It omits `Antiphon.SessionRunner.Tests`, `Antiphon.PtyHost.Tests`, and `Antiphon.Messaging.Tests`. Solution build does not execute them. | Define and guard an exhaustive project/eligibility inventory. |
| All native tests run without special activation. | Headed/Explicit tests self-skip; runner has live Herdr tests. Messaging has `ANTIPHON_BROKER_TESTS` container coverage and optional real Telegram branches. | Separate unattended container/native coverage from manual/provider/live-service acceptance. |
| `succeeded=true` means comprehensive execution. | Suite success uses process exit; parsed counts can be absent. No fresh TRX is requested. Zero selected suites after empty/comma arguments are possible. | Require fresh, nonzero, reconciled execution results, not exit zero alone. |
| A green run at the same SHA can satisfy the next full nightly. | `nightly-run.ps1` tests only `last.succeeded` and matching SHA before the self-update hop; selected suites, ref, eligibility and policy are absent from the cache key. | A client-only green must never suppress a full run. Remove scheduled unchanged-SHA reuse initially. |
| Green reporting closes only an appropriate complete incident. | Reporter trusts `summary.succeeded`; a partial green can close an unassigned Backlog nightly card. Previous failures absent from a new run are labeled fixed without proving they reran. | Coverage-aware reporting and explicit not-retested state. Preserve existing assignment/status restrictions. |
| All reporting failures make the outer job fail. | Bootstrap only treats report exit **3** specially; other nonzero reporting exits can be lost when tests return zero. Early sync failures have no test summary/card. | Propagate every nonzero report failure and independently monitor failures before reporting starts. |
| One nightly lock prevents all overlap. | File creation is non-atomic and a live lock older than 240 minutes can be replaced. Overrides still share fixed `last-run.json`/lock paths. | Exclusive ownership across the whole run/self-update hop; no live-owner replacement by age. Isolate manual state. |
| The current watchdog is sized for comprehensive coverage. | Antiphon watchdog is 60 minutes; historical **partial** runs exceeded it. No completed current full-run timing exists. | Do not promise completion within that budget; reconcile bounded serial chunks and measure the resulting full schedule. |
| Unit/Integration applies uniformly to every project. | `TestLaneCategoryGuardTests` scans only `Antiphon.Tests`; other projects also use Pty/PtyHost/Headed or no lane category. | Preserve the existing XOR contract; do not silently extend it and relabel every project. |
| A slow marker will remove all fast-lane overhead. | `TestDbFixture.Before(Assembly)` initializes PostgreSQL unconditionally. Filtering omits bodies, not build or that hook. | Record the remaining startup cost; lazy initialization stays CARD-0476. |

`.github/workflows/ci.yml` describes client/Messaging and Windows PtyHost/runner
jobs, not Antiphon.Tests or Agents.Pty full coverage. Its current hosted execution
and health were not audited. Neither that YAML nor the publish workflow is a
substitute for the missing nightly.

### Timing evidence and its limits

Read in full before design:

- `docs/investigations/2026-09-11-card-0474-full-category-timing-breakdown.md`.
- Original CARD-0474 report at
  `C:\Antiphon\worktrees\card-task-722e21e7\.antiphon\task-722e21e7.md`.
- `docs/testing-and-build.md`, including CARD-0451's exact-method PC requirement,
  fresh TRX, CARD-0403 OR selection and process sequencing.
- CARD-0487's full description and referenced CARD-0476/CARD-0484 descriptions;
  current TestDesign/Code/Mutation/Review bundles containing CARD-0470/0471's
  inspection, cost and guard-inventory requirements.

Historical CARD-0474: 1,992 Unit cases took 70.29 seconds outer wall;
492 broad cases took 94m23.46s. Its three principal real-Git matrices contributed
77m51.6s of serialized case work. CARD-0482 recovered another 25.2 and 18.9 minutes
of case sums for checkpoint/cleanup matrices from an interrupted run. These are
older and partly shared-host samples, not the current full-suite wall time.
CARD-0475 has since split controlled matrices from retained real capstones;
do not reuse the old counts to size or classify the rewritten classes blindly.

The full runner sample was 551 cases, 354.07 seconds outer, 0.265 seconds discovery:
RunnerRestartHealthTests 143.23 seconds and TranscriptAdoptionSafetyTests 71.21
seconds of body sums. PostgreSQL startup was roughly 24-37 seconds per process;
clones around 140-180 ms. Herdr ping timings measure protocol overhead, not headed
test duration. No new tests or performance experiments ran in this Plan stage.

## Decisions

### D-1: gate the policy change on an operational backstop

Land implementation and classification first. Activate the reduced-dispatch
policy only after D-5's qualification evidence exists. Until then, preserve the
current Unit-plus-affected-integration contract and any additional already
required checks; do not use this plan to drop coverage or launch gratuitous broad
runs. A missing nightly is a prerequisite to repair, not a reason to silently
spend hours on every unrelated dispatch.

Qualification distinguishes **complete coverage**, **passing tests**, and
**working failure reporting**. A completed red run with an actionable card proves
reporting but is not the initial green qualification. Pre-existing failures need
repair; marking them skipped or silently accepting a red baseline does not qualify.

### D-2: explicit Slow classification, orthogonal to correctness and eligibility

Use ordinary TUnit `[Category("Slow")]` on a **class**. Keep the existing Unit xor
Integration rule for Antiphon.Tests. `Slow` is an expected-cost declaration,
never `[Skip]`, `[Explicit]`, a timeout change, or a reason to omit affected tests.
No mandatory `Fast` complement: absence means no declared slow expectation, not
a promise of a pure/in-memory or sub-five-second class.

Use the existing per-project `slow-tests-allowlist.txt` shape as the cost registry.
Keep `tests/Antiphon.Tests/slow-tests-allowlist.txt` at its current path and retain
the tripwire's `-Allowlist` argument. Add a corresponding file for each classified
.NET project (empty with explanatory comments when appropriate). New repository
entries use exact fully qualified class names. Preserve exact case-insensitive
simple/FQN matching for caller-supplied legacy files; never match display names,
arguments, prefixes or substrings.

Each registry entry has an adjacent reason comment and evidence/card reference:
real Git/protocol capstone, subprocess matrix, deliberate timeout window,
measured aggregate cost, or explicit legacy expectation pending remeasurement.
Slow means either expected cases at or above the existing **5 seconds** tripwire,
or material class aggregate cost (initial review trigger **60 seconds** body sum).
The second trigger proposes review, not automatic retagging or a new flaky timing
assertion. Log body sums separately from class/runner elapsed time and startup.

The paired annotation and registry form one guarded declaration: the annotation
enables TUnit selection; the registry supplies evidence and TRX exemptions. Add a
shared classification guard, compiled into each participating test project, that
reads compiled test metadata and its project registry and requires set equality.
Reject unknown classes, duplicate/ambiguous entries, missing reasons and method-
level Slow annotations. A partial class is one compiled class. Handle inherited
test methods and attributes explicitly. Avoid extending the current line-based
census, which does not recognize all partial/multi-attribute declarations.

Retain `TestLaneCategoryGuardTests`'s existing XOR protection. Share the new guard
as linked test source under `tests/Shared/`; do not make one test executable
reference another. Its metadata/export mode must not launch test fixtures. Expose
the compiled class/category inventory for nightly planning and developer queries.
`/*/*/*/*[Category=Slow]` is the simple query/execution form; TestDesign must prove
the exact selected identities using fresh execution results before publishing
any exclusion or compound-filter recipe. `--list-tests` is inventory only.

Initial migration must audit these groups, not apply an `AgentTaskLand*` prefix:

| Group | Initial treatment |
|---|---|
| Existing Antiphon.Tests allowlist | Preserve exemptions and add matching markers/reasons. Historical allowances are conservative expectations, not claims every member is presently slow. Remove an exemption only with evidence and review. |
| Retained AgentTaskLandBoundary/Admission/Concurrency/Verifier tests | Slow; preserve exact methods, arguments, real Git, assertions and process limits. V32 hostile rebase config and both V11 checked-out-target rows remain mandatory when affected. |
| AgentTaskLandCheckpointMatrixTests and AgentTaskLandCleanupSafetyTests | Inspect integrated CARD-0475 shape; classify retained real matrices using the recorded cost, without moving/replacing their bodies. |
| Other AgentTaskLand classes | Read bodies/helpers to distinguish DB-only orchestration, controlled matrices and real-Git protocol work. Name alone is not evidence. Resolve each identified slow cluster in the migration inventory. |
| RunnerRestartHealthTests / TranscriptAdoptionSafetyTests | Slow for aggregate/process/wait cost, despite many individual rows under 5 seconds. Keep runner-local limiter and ClaudeConfigDirEnv serialization. |
| Other native/PTY/headed classes | Native does not automatically mean slow. Add measured or explicitly expected Slow entries; do not invent headed timing from Herdr ping results. |
| ChannelBridgeTests | Classify observed cost honestly with CARD-0484 reference while its fix is pending. Rebaseline after that fix; no timeout optimization here. |

Keep execution eligibility separate. Add an `OptIn` category to **wholly** manual
classes, retaining Headed/HeadedLong/Explicit and environment guards. Record
method-specific or mixed fake/live branches in the execution inventory instead of
excluding an entire mixed class: Telegram conformance must still run its fake
leg. Docker-backed broker tests are safe unattended coverage once explicitly
enabled against their disposable Redpanda container; they are not manual merely
because they currently use an environment opt-in.

Tripwire changes remain additive: use each project's registry, preserve CARD-0475's
testId-to-TestDefinitions join, unresolved-identity failure and threshold behavior,
and add machine-readable totals if needed by nightly. Missing/malformed/empty
execution evidence cannot establish green. A Slow allowance exempts only the
known duration threshold; it never exempts failed/skipped/missing tests. No nightly
job automatically adds a new allowlist entry.

### D-3: keep the current assemblies; separate execution artifacts now

| Alternative | Fast-lane benefit | Cost/risk | Decision |
|---|---|---|---|
| Categories + exact affected-class selections in existing projects | Avoids unneeded bodies immediately; one build and shared fixture startup per combined invocation | Still compiles/discovers the current graph and starts Antiphon.Tests PostgreSQL | **Choose now.** Proven selector model and least coverage migration risk. |
| One `Antiphon.Tests.Slow` collecting landing, runner and headed tests | Removes some source/discovery from existing assemblies | Couples unrelated dependency graphs; internal helpers, linked fixtures, assembly hooks and worker entry paths must move; duplicates startup; shared limiter does not cross assemblies | Reject as a single heterogeneous project. |
| Separate Landing.Integration / Runner.Native / Headed projects | Clear physical boundaries and optional build graph | More builds/startups and fixture packaging; a migrated assembly needs its own production-runner guard, one-wide limiter and sequencing; no measured incremental compile saving | Defer, retaining a decision trigger below. |
| Separate pure/DB-free fast assembly or lazy DB initialization | Could avoid the measured fixed DB startup | Not accomplished by merely moving slow Git matrices: many other integrations still need DB; overlaps CARD-0476 | Leave to CARD-0476. |

LandingSafetyHarness is internal and depends on TestDbFixture, real Git fixture,
DI helpers, persistence and crash-worker behavior. Antiphon.Tests also links
FakeHerdrServer and stages fake CLI outputs. Runner's fixtures, transcript files,
environment groups and process limiter belong to a different graph. These are
concrete migration costs, not just project-file edits.

Reconsider physical separation only after recording clean and incremental build,
discovery, fixture and execution times for a representative narrow change. Compare
one existing-project invocation against an isolated prototype with identical
coverage and all safety hooks. Report cold/warm costs and duplicate nightly work;
do not equate removed lines/classes with saved time. Runner discovery at 0.265s
does not justify a split by itself. This card does not require that prototype.

Store scheduled results by project and chunk (including slow groups) in one run
directory. This provides independent timing/failure artifacts without moving
tests or weakening their shared assembly-local limits.

### D-4: comprehensive means an explicit, checked execution universe

Add `tests/test-execution-policy.json` with schema/version and a stable policy hash.
It inventories every test project (`IsTestProject=true`), the client test command,
required script checks, execution mode, safe environment, opt-in exclusions and
registry path. Guard newly added/renamed projects against silent omission. It is
not a source-path-to-test dependency oracle; TestDesign still makes that judgment.

Default unattended obligation:

| Suite | Scheduled coverage |
|---|---|
| `antiphon` | All eligible Antiphon.Tests classes, Unit and Integration, Slow included, all real-Git capstones. |
| `session-runner` | All eligible Antiphon.SessionRunner.Tests, including restart/transcript-adoption safety. |
| `pty-host` | All eligible Antiphon.PtyHost.Tests using owned hosts on Windows. |
| `agents-pty` | All eligible Antiphon.Agents.Pty.Tests, including fake CLI/native contracts; manual vendor canaries excluded explicitly. |
| `messaging` | All eligible Antiphon.Messaging.Tests plus disposable-container broker coverage; fake service legs run; live credentials are not inherited. |
| `client` | Full `scripts/test-client.ps1`; fresh client build and lint are recorded independently. |
| `scripts` | Explicit offline script-test inventory, starting with nightly report/runner/monitor/selection tests. Census all `scripts/test-*.ps1`; distinguish runnable harness, wrapper, required arguments and manual probe. No unsafe wildcard execution. |
| `e2e` | Explicit manual exception initially; requires fresh client/dist and its isolated random runner. Not part of unattended coverage credit. |

A complete unattended run is not a claim that headed vendor, live-service or E2E
acceptance ran. Inventory each excluded category/class/method/branch, environment
requirement and change trigger. E2E/manual changes require targeted acceptance
when affected; TestDesign may not defer them to a nightly that excludes them.

Explicitly set headed/live opt-ins off in the child environment, clear test live-
credential names without printing values, and set the broker test opt-in only for
the fixture that owns its container. Audit other eligibility gates before first
activation. Preserve backend environment guards, fake-provider isolation and
production-runner refusal. Do not change vendor homes or pass a live broker.

Build once in the isolated clone. Execute process-spawning project groups
**sequentially**; each assembly retains `ParallelLimiter<ProcessSpawnLimit>` at
one and its existing NotInParallel groups. No Git-only concurrency trial here.
The scheduler's desktop tag serializes only its own jobs; record other host test
processes and avoid qualification under competing native test load. Do not kill
foreign test runs to obtain a clean sample.

Request a fresh TRX per .NET invocation and a machine-readable client result
through `test-client.ps1`, preserving its actual exit code and all existing callers.
Record full SHA/ref, invocation ID, policy hash, selected classes/methods, expanded
results, failures, skips/reasons, start/end and output paths. Validate fresh files,
nonzero expected execution, matched identities and counters, plus process exit.
Missing results, unexpected skipped required tests and unknown coverage are red.
Known manual exclusions appear separately; they are never counted as passed.

Because a full current Antiphon run is unmeasured, support **serial class chunks**
with a disjoint, exhaustive membership manifest. Generate membership from the
built assembly's unfiltered test-class inventory, exclude only explicit manual
obligations, and reconcile it with executed identities after all chunks. Every
eligible class occurs exactly once; all parameter rows must finish. Estimate
chunks around 20-30 minutes from available timings, leaving unmeasured classes
explicit; keep a single class whole unless separately designed row partitioning
is proven. Use the documented parenthesized class-OR syntax and verify matches.
No hardcoded list may silently omit a new class. Inventory/discovery output is
never execution evidence. A failing chunk must not prevent remaining independent
chunks from reporting coverage, provided owned child cleanup succeeded.

Class metadata alone cannot detect a missing parameter row. Capture the pinned
runner's **unfiltered expanded-case discovery inventory** for the same build and
safe environment, then reconcile executed per-method row identities/counts with
it, alongside class membership. TestDesign must prove this inventory path on
TUnit 1.44 (including data sources and Explicit cases); filtered `--list-tests`
is specifically not trusted. If a data source has unstable identities, give it a
reviewed deterministic case/count contract. Unknown expected coverage fails
qualification; aggregate suite totals cannot compensate for one missing class
or parameter row. Do not redesign test data sources merely to fit an unproven
report parser: return the unverifiable seam to Plan if the pinned runner cannot
supply an adequate inventory.

Keep existing test/assertion deadlines. Do not blindly increase the 60-minute
suite watchdog or advertise an unmeasured completion SLA. Give bounded chunks the
existing budget; a timeout is incomplete/red, not a test exclusion. A single class
that still exceeds it requires diagnosis/redesign or a separately reviewed
operational-budget decision, not a hidden timeout edit. Record per-chunk startup
overhead so partitioning does not become one expensive process per cheap class.

### D-5: honest run state, registration, monitoring and qualification

**Native runner and evidence:**

1. Introduce a producer-owned `StateRoot` with default
   `C:\Antiphon\nightly`; test/manual roots stay separate. Keep paths in Windows
   backslash form. Establish canonical clone ownership before reset/clean/prune;
   reject shared/worktree/reparse escapes and unproven foreign directories. Tests
   use disposable roots, never the shared nightly clone or production ports.
2. Acquire an atomic exclusive lock for the full run, including self-update.
   A live run is never displaced because its timestamp is old. Parent retains
   ownership through its awaited hop; accept continuation only for the verified
   parent/run identity. Atomic run-state writes and GUID-qualified directories
   avoid timestamp collisions. Refusal does not overwrite the active run's record.
3. Write Started before fetch/build, progress with phase and last activity, and
   final status in finally. Hard process loss is detected externally. Keep
   `last-run.json` as last attempt and a separate `last-complete-green.json`
   containing full coverage provenance. Failed persistence itself is nonzero.
4. Remove scheduled unchanged-SHA skipping in this release. Daily reruns also
   catch environment drift and make freshness honest. Manual partial/feature-ref/
   NoReport runs cannot replace scheduled-green state or auto-close its incident.
5. Record independent `coverageComplete`, `testsPassed`, `reportDelivered` and
   overall status. Propagate every nonzero reporter exit. Reporter handles build,
   preflight, timeout and incomplete results, retaining card.md on API failure.
   Test failure stays red after successful card delivery.
6. Preserve one open nightly card, fresh concurrency-token writes, and the existing
   Backlog/unassigned-only auto-close rule. Add the stronger condition: a complete
   scheduled green for the required policy and master ref. Previous failures are
   fixed only with matching passing identities; otherwise they are not retested.
   Emit rerun commands using the correct project and class/method identity for
   every newly supported suite, not the current Antiphon.Tests fallback.

**Windmill deployment:** store reviewed script and schedule definitions under
`scripts/windmill/`, including the named nightly, health monitor and registration
instructions. Reuse the desktop SSH bridge; do not add a Windows Scheduled Task.
The native bootstrap remains responsible for syncing only the isolated clone to
origin/master. The wrapper forwards failures and returns a small structured
completion record (run ID, SHA, policy, completeness, report result, evidence path)
for server-side monitoring; console logs alone are not the health contract.

Register `u/lndcobra/antiphon_nightly_tests` in workspace `mc`, desktop tag,
`0 30 0 * * *`, Europe/London, no suite/ref override. Match the worker/server
versions and inspect the deployed script hash and actual args. Do not infer
registration from a checked-in definition. Use a measured aggregate job budget
covering build, sequential chunks and reporting, without an outer timeout that
preempts the promised workload. Desktop queue delay is included in monitoring.

Add `u/lndcobra/antiphon_nightly_health` on a **server2 worker**, every 30 minutes;
it must not queue behind the desktop tests. It checks the nightly registration,
enabled/paused state, scheduled jobs, last attempted/complete run and notification
delivery status. Missing/disabled schedule, overdue start, timeout, missing result,
incomplete coverage or failed reporting produces a durable failed monitor result.
Use a 30-minute start grace after 00:30 and flag absence of a complete expected
run by 08:00 Europe/London. That morning deadline is an operational default to
qualify: if honest full coverage cannot fit, revise the schedule/partition plan
before activation, not the coverage set. Never terminate a foreign process or
healthy running session because the monitor is red.

Test failures normally surface on the existing nightly board card. Configure
Windmill failure/recovery notification for execution/reporting/monitor outages
using the existing notification facility and an explicitly authorized operator
destination; do not silently add a recipient. The caller/operator owns enabling
and acknowledging that external notification path. Implementation can prepare
and test payloads with a fake sink first. Absence of a morning card is never green.
The operator named by the activation record owns morning triage; persistent
failure stays visible until acknowledged/repaired. This is an operational alert,
not a decision-question replacement or an auto-dispatch/spend action.

**Readiness gate, recorded in a committed qualification artifact:**

- A manual full unattended run on a recorded master SHA completes with expected
  project/class/expanded coverage, including slow capstones and container tests;
  **zero failed required tests**, only enumerated manual exclusions.
- A real subsequent scheduled 00:30 run completes and matches its Windmill job,
  native run ID, SHA/policy and retained evidence. A manual trigger alone is not a
  scheduled-fire proof. Record measured build/chunk/total durations and queue delay.
- Controlled failure acceptance proves test-red to exactly one visible card,
  repeated-red update, partial-green nonclosure, full-green eligible closure, and
  assigned-card preservation. Use a dedicated fixture board for deliberate
  failures; never pollute or reset the shared master clone to fake a failure.
- Test sync/SSH/worker/report failures and missing/stale schedule through an
  isolated qualification job and fake API/sink, then verify the authorized live
  notification path once. Record receipt/read-back, not just request acceptance.
  Do not disable a real production schedule for a positive control.
- Deployed definitions, their hash, safe environment, independent monitor,
  notification destination/acknowledgment and current healthy result are recorded.

If any prerequisite is missing, the policy remains inactive. This is a staged
rollout, not a blanket demand that Code wait idly for the next night: finish and
land prerequisite slices, then let the caller arrange the scheduled acceptance
and dispatch the activation slice. The final card is not complete before it.

### D-6: TestDesign supplies the execution selection, including exclusions

Add a `### Regression selection` section to the standing verification design,
building on Inspection, V/R, Guard inventory and Cost. Do not duplicate or replace
them. For each changed contract, shared helper and plausible regression neighbor:

| Field | Required content |
|---|---|
| Identity | Stable selection ID, exact project and class/methods or justified category; expanded-count expectation with source/SHA. |
| Relevance | Changed path/contract/helper and mapped V/R IDs; actual test bodies/fixtures inspected. |
| Disposition | `run-now`, `nightly`, `manual-required`, or `unrelated`; one reason per named group. |
| Cost/eligibility | Slow status/reason, ordinary build/startup/run estimate, process group, opt-ins and environment needs. |
| Command/evidence | Supported precise filter/wrapper, intended identities, fresh TRX/result directory and zero-test refusal. |
| Deferral conditions | For nightly: exact scheduled suite/policy obligation and readiness evidence. For manual: trigger, owner/environment and required acceptance. |
| Reopen trigger | Production/helper/fixture changes that invalidate this exclusion or broaden the required group. |

Selection dispositions have precise meanings:

- **run-now:** all new/changed tests and affected existing regressions/capstones.
  Slow tests remain run-now when they protect the changed invariant; they cannot
  be dropped to meet an estimate.
- **nightly:** existing unaffected neighboring/broad coverage deliberately deferred
  to the qualified scheduled obligation. Name it; do not write only "rest nightly".
- **manual-required:** affected opt-in/E2E/vendor/live-service acceptance not supplied
  by unattended nightly. A skipped required manual test remains pending, not green.
- **unrelated:** a bounded family has no changed dependency/invariant. It should not
  be rerun for this dispatch. Do not exhaustively enumerate thousands of unrelated
  methods; include plausible neighbors and each known slow cluster with a reason.

After activation, Code/Review execute the union of V/R plus `run-now` and required
manual acceptance, **without automatically appending the full Unit lane**. A
whole Unit/category sweep is an explicit selection for a cross-cutting change,
not the default. Empty executable selections for prose-only changes need a
documented inspection check and zero-cost explanation. Missing selections are
an incomplete TestDesign artifact, not permission to guess a broad filter.

Code checks actual touched paths/helpers against the selection before execution.
When implementation reveals a new dependency, add and cost the affected tests
and report the delta; return to TestDesign when the exclusion judgment or safety
mapping needs redesign. A small discovered regression can be added without a
permission round trip. Never treat pre-approval as permission to miss a real guard.
Review rejects unexplained omissions, unexpected selector expansion, skipped
required tests, broadening without the invariant/cost, and stale readiness claims.

Known-slow examples the standing guide must include:

- A narrow AttentionService change selects its affected class and actual contract
  neighbors; no landing or restart matrices merely because they share an assembly.
- LandingGit/landing protocol/lease/shared LandingSafetyHarness changes require
  the affected real-Git identity/publication/cleanup/concurrency capstones and
  controlled coverage. V32 hostile configuration and V11 checked-out-target proof
  cannot be replaced by fake Git or deferred when their semantics are affected.
- Runner restart/probe/script fixture changes activate RunnerRestartHealthTests;
  transcript binding/adoption/tailer/helper changes activate
  TranscriptAdoptionSafetyTests and the actual recovery neighbors.
- A fixture/helper change that reaches all its consumers cannot retain a narrow
  single-service selection merely to keep cost low. List consumers or justify
  a broad run before starting it.

Keep a readiness check at dispatch/verification time: the activation artifact
proves setup, while a recent monitor result proves current operation. If the
backstop becomes stale/incomplete/unreported, night-deferred coverage is no longer
credited. Report degradation; restore the previous Unit-plus-affected default
and run any specifically deferred coverage now needed to establish the change,
or repair the backstop before depending on that deferral. Do not silently force
full repository sweeps on every card or claim that Unit alone covers all omissions.

### D-7: retain Mutation completeness and production landing behavior

Every independent safety guard remains inventoried and mapped 1:1 to a distinct
defined PC-n, including variants and discovered missing guards. All ordinary V/R
still run. Mutation keeps exact-method red/restore/green, fresh nonzero evidence,
restored timestamps/fresh binaries and independent-file batching rules from
CARD-0451. `nightly`/`unrelated` selections do not waive any PC. Ordinary cost and
Mutation cost remain separate, with a numeric total and every PC arm included.

Do not change stage ordering, PC scheduling ownership, `LandVerifyFilter`,
LandingVerifier, rebase verification or automatic land commands in this card.
CARD-0478/0479 have nearby plans; integrate the current landed stage contract
without silently adopting their unimplemented transitions. If landing verification
still dominates costs, report it for its owner; this policy cannot bypass it.

### D-8: compact launch instructions, detailed owner document

`docs/testing-and-build.md` owns the detailed selection/readiness schema and
examples. Update TestDesign/Code/Review bundles and orchestration dispatch guidance
together so generic instructions cannot re-add Unit or broad sweeps. Keep an
inactive/active rollout condition explicit until qualification lands.

All stage bundles remain ASCII and at most 2,500 characters. StageTestDesign is
already **2,494 characters** after the catalog's `ReplaceLineEndings("\n").Trim()`
normalization (2,529 in the raw CRLF file); it has only six characters of headroom.
Adding the requirement needs compaction. Preserve guard completeness, receipt
inventory, inspection and numeric costs in the actual composed prompt; put
extended examples in the owner document. Test the composed role bundles, not
just a new sentence in one markdown file. Do not raise the cap.

## Implementation slices and targeted verification owners

These are acceptance requirements for a separate TestDesign pass, not a completed
V/R/PC design. TestDesign must inspect all touched test bodies and neighboring
fixtures and bind exact methods, counts, defects and costs before Code.

### S1: coverage inventory, execution evidence and native state correctness

Files: new `tests/test-execution-policy.json`; `scripts/nightly-run.ps1`,
`scripts/nightly-tests.ps1`, `scripts/nightly-report.ps1`; `scripts/test-client.ps1`
only for additive machine-readable output; nightly fixture files.

Implement D-4/D-5 coverage, state/lock/ref isolation, every-nonzero propagation and
report semantics. Add bounded injectable command/state/clock seams so tests prove
behavior without real Git reset, AppHost, a production runner or an overnight run.
Registry-driven project paths replace suite-specific rerun fallbacks.

Tests: extend `NightlyScriptsTests` and `scripts/test-nightly-report.ps1`; add
`scripts/test-nightly-run.ps1` and `scripts/test-nightly-tests.ps1` or equivalent
isolated fixture harnesses. Read/retain `TestClientFilterTests` when modifying its
wrapper. Required adversarial cases include omitted project/class/argument rows,
zero suite/zero tests, stale/malformed/missing results, wrong SHA/policy, partial
green, reporting exits 1/2/3, same-SHA scope change, build/preflight failure,
concurrent lock acquisition, live old owner, lost process/state write and unsafe
clone paths. Prove serial ordering and retained cleanup before starting the next
native group. All script daemons/bootstrap files remain ASCII/PS5.1-compatible.

### S2: classification and guard-preserving migration

Files: `scripts/test-duration-tripwire.ps1`; per-project slow registries; new
`tests/Shared/TestClassificationGuardTests.cs` and metadata helper; affected
`.csproj` links; annotation-only edits to audited slow/manual test classes.

Tests: preserve/extend `TestDurationTripwireTests`, `TestLaneCategoryGuardTests`,
and `ProcessSpawnLimitTests`; compiled-metadata classification tests cover partial,
inherited, combined-attribute and same-simple-name cases. Include synthetic small
Slow/OptIn/ordinary fixtures to prove selector behavior without executing a whole
real-Git matrix just to test category selection. The full prerequisite run proves
retained real tests remain scheduled. Classification body/assertion edits or
fixture moves are scope drift and require affected semantic verification.

### S3: reproducible Windmill registration and independent health monitoring

Files: new versioned definitions/wrappers under `scripts/windmill/`, health logic
and offline fixtures/tests, plus nightly runbook section. Use the deployed API
schema and read-back checks; no direct SQL mutations in deployment tooling.
Keep credentials in managed stores and never place a token in definitions/logs.

Tests: fake job/schedule/clock/API/sink scenarios for absent registration, disabled
schedule, missed start, queued desktop, incomplete job result, native failure,
report failure, DST/local due-time calculation, deduplication and recovery.
Verify the monitor is routed off the desktop queue. Monitoring receipt and
recovery guards need PCs; text tests cannot prove delivery.

### S4: qualify the backstop and retain measured acceptance

After prerequisite changes land, deploy/read back S3 and execute D-5 acceptance.
Record the master SHA, policy hash, deployed versions, scheduled job ID, run ID,
all per-suite/chunk counts, explicitly excluded coverage, observed durations,
failure/notification receipt and named morning owner in a committed
`docs/investigations/<date>-card-0487-nightly-qualification.md`.

Tests here are operational acceptance, not permission to modify production guards
or send unapproved messages. Stage work can settle with S1-S3 complete and S4
pending; CARD-0487 cannot claim an active reduced policy until S4 passes.

### S5: activate explicit TestDesign selection and enforce the prompt contract

Files: `server/Bundles/stage-test-design.md`, `stage-code.md`, `stage-review.md`,
`stage-plan.md` if needed for folded easy designs; `delegate-basics.md` and
`orchestrator.md` only where conflicting defaults exist;
`docs/testing-and-build.md`, `docs/orchestration-loop.md`.

Tests: `InstructionBundleTests`, applicable composed-brief tests in
`DelegationReportFormatterTests` (`DelegationUnitTests.cs`) and any already-landed
CARD-0471 completeness contract suite. Pin active selection/readiness obligation,
no automatic Unit append, exact counts, scope-change handling and preserved
V/R/PC/receipt ownership. Include semantic review examples: a narrow service,
cross-cutting landing helper, transcript adoption, manual E2E, prose-only change,
missing selection, stale backstop and newly discovered guard. Reject exclusions
that merely say Slow or omit PCs. Text checks protect the prompt contract only.

## TestDesign handoff and cost expectations

Separate TestDesign is mandatory: this is hard work spanning safety-sensitive
filesystem ownership, evidence completeness, monitoring, filtering and prompt
composition. It must append `## Verification design` with Inspection, Delivery
inventory, Regression selection, V/R, complete Guard inventory, every distinct
PC/variant, Out of scope and separate numeric Code/Mutation costs. No placeholder
counts or undefined PC references at Code handoff.

For this card, default ordinary regression owners are the named nightly,
classification, duration, process-limit, client-wrapper and composed-instruction
tests. Real landing/adoption/native bodies are not changed by annotations alone;
their comprehensive execution belongs to S4's one scheduled qualification set,
not a repeated broad filter in every S1/S2/S3 dispatch. If helpers/production code
change, expand that selection explicitly. Until S4 activation, respect the current
standing default; do not use this paragraph as a policy bypass.

Planning estimates, **not measured costs**:

- TestDesign inspection/mapping: 30-60 minutes; no broad suite timing experiment.
- S1-S3 ordinary verification: 2-5 minutes isolated build, 1-2 minutes existing
  Unit baseline where required, 5-15 minutes bounded script/selector/metadata
  fixture checks, 2-5 minutes instruction/wrapper regressions: **10-27 minutes**,
  excluding implementation and the separate qualification run.
- S4: budget a separate **2-6 hour observation window**, not a claimed current
  full-suite duration or a timeout authorization; then one real scheduled fire.
  Actual counts/timing and the morning SLA must be reconciled before activation.
- S5 ordinary verification: **3-8 minutes** estimated build plus named composed
  contract checks. Do not rerun S4's whole suite for prompt-only activation.
- Mutation is **additional**. TestDesign must calculate a numeric floor after
  binding the independent lock/path/evidence/selection/monitor/report guards and
  all their red/restored-green arms; this Plan deliberately does not invent a PC
  count or pass an unfinished cost table to Code.

Expected savings come from not executing unrelated bodies. Historical Unit versus
broad selection differed by about 93 minutes, with different coverage; that is not
a current guaranteed saving. Omitting a whole Unit invocation can additionally
avoid its measured roughly 65-70 seconds when its necessary classes are already
in a combined affected-class run. It does not eliminate the remaining selected
assembly's DB startup. Nightly chunks add startup cost and should batch multiple
landed changes once per scheduled run. Record actual scope/cost deltas after
activation; CARD-0476 owns further startup or concurrency optimization.

## Boundaries, rollout and completion

No project split, production landing changes, lazy DB work, parallelism increase,
timeout relaxation, matrix reduction, vendor-home changes or new Windows schedule
is included. CARD-0475 and CARD-0484 may change baseline timing/class bodies;
reconcile those landed changes rather than duplicating their fixes. No new scope
area is necessary; name the concrete script/test/bundle paths in dispatch scopes.

Rollback reduced dispatch defaults if monitoring loses coverage/freshness; retain
Slow classification and repaired nightly evidence. A failed schedule/report never
turns skipped coverage into green. Broad scheduled failures get actionable triage,
not suppression to make a new fast policy look successful.

Completion requires S1-S5, measured/read-back S4 evidence, complete TestDesign and
Mutation obligations, hard-card Review, and a current healthy backstop. The Plan
itself is complete with the chosen defaults. The next action is to land this
artifact and dispatch **TestDesign**, not Code or live schedule registration.
