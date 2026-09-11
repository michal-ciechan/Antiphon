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


## Verification design

Date: 2026-09-11. Stage: TestDesign. Inspected baseline: **a7f2b493**, containing
the landed Plan. This section specifies checks for Code; S1-S5 are not implemented
or operationally qualified by this stage.

**Dispatch S1-S3 to Code with reduced policy inactive.** Code implements tests
and ordinary V/R; Mutation owns every deliberate PC. Hard-card Review follows
Mutation. The caller lands prerequisites before S4, and dispatches S5 activation
only after S4 passes. Carry original Code task/branch/worktree through those
stages. No live registration, notifications, credentials or runtime changes were
performed by TestDesign. Current Unit-plus-affected requirements still apply.

### Inspection

Complete bodies are distinguished from sampled unchanged neighbors; samples are
not claimed as an exhaustive semantic review.

| Bodies/fixtures read | Boundaries and mapping |
|---|---|
| All three nightly scripts; all NightlyScriptsTests methods; reporter shim/fixtures T1-T10 including T5b | V-2/3/5: destructive path ownership, lock/hop, state, exits, coverage and closure. Existing T3/T7 green fixture is client-only: replace with full green and retain partial green as negative. Shim lacks token-conflict validation and recipient read-back; extend it. |
| Full TestClientFilterTests, RunWrapperAsync and test-client.ps1 | V-3/R-2: real argv and one-file selection. Existing assertion allows a failed single-file run; independently assert actual exit and structured outcomes in new checks. |
| Full TestDurationTripwireTests and fixture builders, tripwire script, lane guard and ProcessSpawnLimitTests in Antiphon.Tests, runner and Agents.Pty | V-4/R-3: 16 duration rows; testId join, threshold, malformed input, XOR, per-assembly limits. Tripwire currently accepts empty results. Runner limiter population is exact; adding consumers must update it honestly. |
| InstructionBundleTests, including C470 composed roles, C467 delivery, cap, role map and composer; formatter C470/task-marker/stage-handoff bodies in DelegationUnitTests; current TestDesign/Code/Mutation/Review bundles | V-7/R-4: normalized 2500 cap, receipt and Mutation ownership. BuildBrief deliberately excludes bundles: verify composer AND brief. No separately landed CARD-0471 completeness suite exists at this baseline; standing completeness instructions do. |
| Six project files and 29-entry slow registry; KafkaConsumerGroupObservationTests setup/container; TelegramContractTests and TelegramLiveChatConformanceTests fake/live branches | V-3/4/9: five unattended .NET projects, E2E manual, disposable broker enabled. Both mixed Telegram classes keep fake execution; only real branches are manual. |
| LandingSafetyHarness DB/lease/crash-worker setup; Boundary V11 captured checkout/old SHA (both rows), hostile-config V32 bodies; checkpoint/cleanup recovery samples; Admission/Concurrency/Verifier declarations | R-5/V-9: retained real-Git safety capstones. Annotation-only changes require semantic inventory preservation; any helper/body change reopens affected-test selection and requires full inspection of those consumers. |
| RunnerRestartHealthTests health/foreign-owner/fixture samples; TranscriptAdoptionSafetyTests pre-existing-transcript refusal, real tailer/tree and serialization | R-6/V-9: unchanged native bodies stay in qualification. Restart/probe/tailer/adoption/helper changes require affected semantic execution immediately. |
| Timing investigation above, testing/build owner, landed orchestration contract; repository-referenced Windmill bridge note | Cost/V-6/9: Linux desktop worker reaches Windows over SSH; independent monitor belongs on server2. Note is historical interface context, not current registration proof. Never use its SQL token-minting example for deployment. |

New runner/coverage/health harnesses and compiled classification helpers do not
exist yet. Code creates them using the inspected reporter HttpShim pattern,
adding command/process identity/clock/filesystem fault seams and durable fake
API/queue state. Invoke production script/helper decisions, not a reimplementation
of their logic. Each fixture owns a GUID root, marker, traces and results.
Real file/process barrier tests own and await both competing child processes.
No fixture resets the shared nightly clone or calls production ports.

The repository project census uses tracked project definitions evaluated for the
recorded build; disposable generated probe projects are fixture outputs, not new
repository obligations. Do not use source-file or display-name counts as its
oracle. State/log paths must be producer-owned and outside the checkout that
sync/clean can replace; include an overlap refusal in the G-004 ownership fixture.

### Delivery inventory

Run identity is (workspace, local due date, WindmillJobId, nativeRunId, SHA,
policyHash); notification identity additionally includes failureKind and
notificationId. Retry preserves logical identity.

| Producer -> destination | Persistence and recovery | Observable receipt / checks |
|---|---|---|
| Scheduled Windmill wrapper -> native bootstrap -> completed Windmill job | Started before sync; awaited SSH/hop; native final state and wrapper structured result; independent monitor handles hard loss | Read-back of job and native summary with matching IDs/SHA/policy and all expected rows; V-2/3/6/9. |
| Nightly reporter -> board/card | Pending report before HTTP; uncertain create/update recovered by run-ID query and fresh concurrency token | Separate GET/revision/discussion contains correct run ID and intended content/status; a 2xx write alone is insufficient. V-5 and S4 fixture-board acceptance. |
| Independent monitor -> notification facility -> authorized operator | Persist failure/recovery event before enqueue; retry pending identity across crashes; dedup only identical events | Correlated notification visible to recipient; live qualification records operator acknowledgment. Enqueue/Sent/transport ACK alone is insufficient. V-6 and S4 live acceptance. |
| Qualification artifact + recent monitor -> dispatch/verification decision | Committed evidence proves initial setup; fresh matching monitor proves current operation | Selection records qualification commit and monitor job/result/time for reviewer read-back; V-7/8/9. |

Offline V-6 drives production wrapper/monitor adapters into a durable fake queue
and a separately readable receiver. Cover busy and already eligible recipients,
enqueue throw, loss before send, after enqueue and before receipt persistence.
Same logical event must recover without duplicate incident. This proves local
recovery logic, not real Windmill facility delivery. S4 repeats receipt acceptance
through the deployed queue/facility to the caller-authorized destination.

No session-input delivery path changes here. If implementation introduces one,
extend TestDesign through the real session queue to matching complete UserPrompt
evidence, busy/eligible recipients and each crash boundary with distinct PCs.
Card read-back proves board persistence, not human attention; live acknowledgment
covers triage ownership. Prompt text tests prove no transport path.

### Executable fixture and evidence contract

Except named existing R regressions, the following are new tests to implement:

- B/E/R/M groups live in scripts/test-nightly-run.ps1,
  scripts/test-nightly-tests.ps1, scripts/test-nightly-report.ps1 and
  scripts/test-nightly-health.ps1 respectively. Expose -Case C487_GNNN
  -ResultsDirectory <fresh-path>; exact function is Test-C487_GNNN. Omitted Case
  runs that harness's cases; preserve reporter's existing baseline too.
  Emit structured case/variant/assertion/start/end/exit/trace evidence; unknown
  case and zero rows fail. Each matrix rows value is its required variant count.
- C methods C487_GNNN belong in new
  Antiphon.Tests.TestHelpers.TestClassificationPolicyTests. Use compiled synthetic
  fixtures, one Arguments row per variant; for duration/limiter checks invoke the
  actual script/census. This process-spawning class carries the local limiter.
  Do not reference one test executable from another.
- Shared tests/Shared/TestClassificationGuardTests.cs contains exactly one
  Registry_matches_compiled_metadata method per linked project. Link it and the
  metadata helper into all six existing .NET projects. Run five unattended
  guard rows; E2E gets one metadata-only validation obligation that proves no
  browser/Program/runner fixture launch.
- P methods C487_GNNN belong in new
  Antiphon.Tests.Application.ScopedVerificationInstructionTests, with declared
  variant counts. Test actual role composition, not isolated markdown fragments.
  Assertions pin required instructions and rubric; V-8 reviews semantic examples.

Use initially absent GUID-qualified result directories for every invocation/arm.
Record executable hash, source SHA, runner version, selector, intended identities
and actual rows. TRX Results join TestDefinitions by testId, never display name.
Missing files, unknown outcomes, unexpected required skips, duplicates, zero
tests and wrong provenance fail. Discovery has its own artifact type and is
never execution evidence. Do not mix UID and tree selectors.

Example commands, with fresh producer-owned suffixes for actual execution:

    dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c487/ --nologo
    dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c487/ -- --treenode-filter '/*/*/TestClassificationPolicyTests/C487_G059' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c487/g059-green-01
    pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C487_G001 -ResultsDirectory .antiphon/c487/g001-green-01

#### Pinned discovery feasibility

A temporary DB-free nine-row net9.0 probe used **TUnit 1.44.0 / MTP 2.2.2 /
.NET 9.0.16**. Retained source/logs/TRX are in this worktree's
.antiphon/c487-probe/. Reproducible source and compact evidence are in the
[discovery note](../../investigations/2026-09-11-card-0487-testdesign-discovery.md).

Unfiltered list-tests found **9** rows: Ordinary **6** (Plain 1, Arguments 2,
MethodDataSource 2, Inherited 1), partial SlowCases **2**, Explicit/OptIn Manual
**1**. Default execution had **8 passed, 0 failed, 0 skipped**: Explicit is
absent, not reported skipped. Slow category selected exactly **2**; parenthesized
Ordinary/SlowCases class-OR selected **8**. Fresh TRX across the three executions
contains **18 passes, 0 failures, 0 skips** with expected identities.

Detailed list-tests still prints display names only. list-tests plus report-trx
is rejected as incompatible. An After(TestDiscovery) hook did not supply list-mode
output. **Unfiltered list-tests plus diagnostic export** did supply 9
DiscoveredTestNodeStateProperty records with UID and TestMethodIdentifierProperty
(assembly/namespace/type/method/signature). Execution diagnostic supplied 8
matching terminal Passed UIDs; only the explicitly excluded Manual UID was absent.
Execution TRX independently corroborated class/method/row outcomes.

S1 implements a strict adapter for this pinned diagnostic seam, with golden
records, same-name collisions, truncation, terminal failure/skip and version-drift
negatives. An equivalent structured MTP exporter is acceptable with identical
proof. Record runner versions and assembly hash on both artifacts; fail unknown
formats. Never parse only display names. V-1 proves metadata-only no-fixture
markers and the 9/8/2/8 sets; E cases test invalid/missing rows.

The probe does not prove every repository data source safe/deterministic. S4
reconciles each assembly's unfiltered discovery UIDs against terminal execution
UIDs, TRX definitions and per-method multiplicity. Unstable identities need a
reviewed deterministic row/count contract tied to source/SHA/safe environment.
Do not serialize arbitrary object/credential values to invent identities.
Unresolved production inventory blocks qualification and returns that seam to
Plan; no coverage reduction is activated while it is unresolved.

### Regression selection

Counts below are exact inspected-source expectations at **a7f2b493**, not claimed
.NET executions. Code records additions and actual expanded results. Full
repository coverage uses an independently discovered exact set at the built SHA;
historical totals are not substituted for current evidence.

| ID / precise selection | Disposition and relevance | Command, cost and reopen condition |
|---|---|---|
| SEL-1: new B/E/R/M **187** rows; reporter baseline **64 assertions** | run-now, V-2/3/5/6, all script/state/coverage/report/monitor guards | Four harnesses without Case, fresh JSON. About 6 minutes, injected clocks/APIs and private roots. Shared helper change selects all its consumers. |
| SEL-2: NightlyScriptsTests **4**, TestClientFilterTests **1** | run-now, R-1/2 | Antiphon.Tests exact two-class OR; about 1 minute plus shared startup. Client dependencies must exist; skip means pending. |
| SEL-3: TestDurationTripwireTests **16**, TestLaneCategoryGuardTests **1**, Antiphon.Tests limiter **3**, runner limiter **2**, Agents.Pty limiter **2** | run-now, R-3 | Named classes per project; projects sequential. About 1 minute plus startup. Semantic fixture/limiter changes reopen scope; no wider parallelism. |
| SEL-4: new TestClassificationPolicyTests **24**; shared guard **5 executed + 1 E2E metadata-only** | run-now, V-4 | Exact class/shared method, serial projects; about 2.5 minutes. No foreign test-executable references or fixture launch in metadata mode. |
| SEL-5: InstructionBundleTests **53**; formatter C470_mutation_brief_and_handoff_contract **1**, a_brief_carries_the_task_marker_so_the_reply_can_be_correlated **1**, the_handoff_block_paragraph_appears_for_every_stage_role **5**; new P class **17** | run-now, V-7/R-4, S5 composition | Instruction/P class-OR; three separate exact formatter-method selections. About 3 minutes including extra Antiphon startup. No Application-wide sweep. |
| SEL-6: current Antiphon.Tests Category=Unit lane | run-now before activation, standing policy | Separate invocation using /*/*/*/*[Category=Unit]. Expected set is unfiltered discovery restricted to effective Unit minus enumerated manual exclusions. Write its concrete count/manifest at implementation SHA before execution; historical 1992 is not current. Allow 1.5 minutes. |
| SEL-7: five eligible .NET universes + all client + explicit script obligations | During rollout, manual-required full qualification; nightly only AFTER S4 | Suite IDs antiphon, session-runner, pty-host, agents-pty, messaging, client, scripts. Required set equals exact eligible discovery minus reviewed explicit exclusions, all Slow included. Two separate full runs; reserve 2-6 hours observation EACH and measure honestly. |
| SEL-8: 29 legacy exemptions; retained Land Boundary/Admission/Concurrency/Verifier/Checkpoint/Cleanup and identified other slow clusters | Annotation/inventory checks run-now; unchanged bodies in SEL-7, then nightly | Migration artifact records exact FQNs/reasons and unchanged method/data/assertion/attribute inventory. Boundary hostile V32 **1**, captured-checkout V11 **2** explicitly retained. Any LandingGit/protocol/lease/LandingSafetyHarness/body change activates affected real/controlled consumers immediately. Historical 77m52s/44m sums are not current costs. |
| SEL-9: RunnerRestartHealthTests, TranscriptAdoptionSafetyTests and actual recovery consumers | Same annotation-only qualification treatment as SEL-8 | Retain all current rows and serialization. Historical 55/44 cases and 143/71 body seconds are provenance only. Script/probe/tailer/adoption/helper semantic changes reopen run-now selection. |
| SEL-10: ChannelBridgeTests; other 25 legacy non-Land exemptions | Metadata migration run-now, unchanged bodies SEL-7 then nightly | Preserve legacy expectation/reason; ChannelBridge references CARD-0484 and remeasurement. No timeout tuning or claim that HEAD still costs 31 seconds per row. |
| SEL-11: E2E, wholly manual Headed/HeadedLong/Explicit/vendor/Herdr, Telegram real branches | Executable bodies unrelated to this annotation/script card; exclusion correctness run-now. manual-required when their contract changes | E2E then needs fresh dist and isolated random runner; fake Telegram legs always stay in SEL-7. Explicit operator/environment owner for affected live acceptance. No skipped manual acceptance credited green. |
| SEL-12: unrelated service/native/landing bodies, including AttentionService example | unrelated except existing Unit baseline/full qualification | Do not append for assembly membership. Shared DB/queue/test-helper changes invalidate exclusion and require consumer inspection/recosting. |

Existing named native R selections total **89** expanded rows; new C/P total
**41**, plus **5** shared native guard rows = **135** selected ordinary native
rows, before the mandated Unit lane and V-1 generated probe. Script assertions
and their expanded cases are reported separately, never added to TRX totals.

Annotation preservation permits class Slow/wholly-manual OptIn plus matching
registry comments only. Compare all other attributes, method/data declarations,
bodies and shared helpers unchanged. S2 records a complete current FQN migration/
manual-exclusion inventory; 29 is the minimum preserved legacy set, not authority
for an AgentTaskLand prefix rule. Every identified cluster needs a disposition.
Helper/body edits are scope drift requiring actual affected semantic checks.

The script census has **13 existing paths**: test-apphost-lock-age,
test-apphost-main-worktree-guard, test-apphost-probe-class,
test-cleanup-claude-sessions, test-cleanup-codex-test-residue, test-client,
test-client-mode, test-deploy-am-service, test-duration-tripwire, test-hooks,
test-nightly-report, test-reap-zombie-agents, test-stage-value-report
(all scripts/*.ps1). Code reads each before granting unattended eligibility,
records wrapper/argument-consumer/probe dispositions and adds new harnesses.
No wildcard execution of this census is permitted.

### Proves it works now

| ID | Code ordinary acceptance |
|---|---|
| V-1 | Generated tiny pinned fixture proves exact 9 discovered / 8 default / 2 Slow / 8 class-OR identities, Arguments/data/inherited/partial/Explicit semantics and metadata-only no-fixture markers. Golden adapter rejects invalid/ambiguous/truncated/version-drift records through E cases. |
| V-2 | B harness **56 rows**. Real bootstrap decisions, disposable state/clone and fake Git/build; real owned file/process barriers for lock/hop. States/traces satisfy every B oracle. |
| V-3 | E harness **54 rows**, actual one-file client acceptance. Prove complete inventories, terminal evidence, safe environment, serial native cleanup, independent build/lint and every exit. Fake clocks produce timeouts without hour-long waits. |
| V-4 | C class **24 rows**, shared **5 native + 1 metadata-only** validations. Preserve six project boundaries, 29 legacy exemptions, all retained capstones, limits and eligibility. |
| V-5 | Reporter **64 baseline assertions + 31 new rows**. Durable fake API validates token refresh/read-back/crash cuts; one incident, truthful partial/full outcomes, assignment/status/reopen restrictions. |
| V-6 | M harness **46 rows**. Fake jobs/schedules/clock/API/queue/recipient exercise production health/wrapper logic, routing, due-time, failure/receipt/dedup/recovery. No live messages. |
| V-7 | P class **17 rows** plus SEL-5 existing **60**. Real compositions preserve current inactive default and gated active selection, all receipt/guard/cost/Mutation rules and 2500 ASCII cap. |
| V-8 | Record reviewer outcomes for **8** semantic selection examples below. Substring assertions are insufficient evidence of agent judgment. |
| V-9 | S4 operational acceptance below: one manual full and subsequent real scheduled full, independent deployment/health/notification read-back. Mandatory before S5. |

V-8 examples: (1) narrow AttentionService selects actual affected class/neighbors;
(2) changed LandingSafetyHarness selects all affected consumers and real V11/V32
plus controlled complements; (3) transcript helper selects adoption/tailer/recovery;
(4) changed E2E remains pending until isolated manual acceptance; (5) prose-only
has inspection and zero executable cost after activation; (6) missing selection
is incomplete; (7) stale nightly removes deferred credit, restores legacy baseline
plus specifically needed deferred coverage; (8) new guard expands V/R/PC and cost.
Reject Slow-only exclusions, unnamed rest-nightly, undefined PCs, uncosted
broadening and discovery/request/transport-ACK substituted for execution/receipt.

### Guards the regression

| ID | Decisive existing assertions |
|---|---|
| R-1 | NightlyScriptsTests **4**: ASCII, runner no Git mutations/alternate OutputPath, isolated/master names, shared-tree WhatIf refusal before execution. Extend ASCII/PS5.1 checks to added native scripts. |
| R-2 | Client filter **1**: exactly one file and absent AttentionPanel neighbor. E cases add true exit and JSON outcome assertions. |
| R-3 | C475_ClassIdentityJoin **6**, ArgumentsCannotWhitelistAClass **1**, UnresolvedIdentityCannotBeExempted **3**, ExpandedRowsAndThresholdAreExact **1**, InvalidInputIsNotGreen **4**, SimpleAndFullNamesAreExact **1** = **16**; lane XOR **1**, limiter populations **3+2+2**. Retain current bodies. |
| R-4 | InstructionBundleTests **53** plus named formatter **7** = **60**. Preserve C470 Mutation, C467 receipt, six stage-cap variants, role map and task/report marker contracts. |
| R-5 | Real Boundary captured-checkout V11 **2** and hostile-config V32 **1**, remaining real capstones and controlled complements stay in qualification. Annotation-only semantic comparison plus V-9 proves inclusion here; semantic changes require amended affected selection. |
| R-6 | Restart/adoption class membership, all current parameter rows, constraints and fixture bodies preserved in current compiled manifest. Historical 55/44 never substitute for its count. |
| R-7 | Reporter T1-T10/T5b: create/update, recent auto-close reopen vs nine-day exclusion, newest run retained within 20000-char history cap, green no-write, fixture log extraction, API-failure card.md and no token. Upgrade T3/T7 green eligibility without deleting closure checks. |

### S4 qualification and activation record

V-9 is work after prerequisites land, not work performed in this stage.
Commit docs/investigations/<acceptance-date>-card-0487-nightly-qualification.md:

1. Read back workspace mc, script/schedule IDs/content hashes, native bootstrap
   hash, API schema/server/desktop versions. Nightly: 0 30 0 * * *,
   Europe/London, desktop, no suite/ref override. Health: every 30 minutes on
   server2 independently of desktop. Budget must cover measured serial chunks;
   preserve native assertion/watchdog deadlines.
2. Build once in proven owned master clone. Retain SHA/policy, safe environment
   names without values, build/lint/dependency outcomes, exact independent
   discovery/chunk manifests and every terminal row/TRX. Five .NET projects are
   required, E2E is manual. All new classes/argument rows/required script/client
   identities must reconcile. Record competing host processes without killing;
   qualify without competing native test load.
3. One **manual full unattended green**, then one **real subsequent 00:30
   scheduled full green**. Record trigger/job/run IDs, SHA/ref/policy, exact sets
   and per-project/chunk expanded pass/fail/skip/exclusion counts, queue/build/
   startup/chunk/total times. Both require zero failed required tests and no
   unknown or unexpectedly skipped coverage. Manual qualification is retained
   separately and still cannot update scheduled-green state or close its card.
   If SHAs differ, each run needs its own exact manifest.
4. Deployed reporter on a dedicated fixture board: red creates one readable
   run-correlated incident; repeated red updates; partial green does not close;
   full scheduled-policy green closes only Backlog/unassigned. Agent/session
   assignment and non-Backlog remain protected. Retain separate GET/revision
   evidence after each action.
5. Isolated qualification jobs inject sync/SSH/worker loss, reporter failure,
   missing/stale registration and desktop queue outage. Do not disable the live
   schedule for a control. Prove durable monitor failure, enqueue, receipt,
   restart recovery, dedup and received recovery through the actual facility.
   Use only caller-authorized destination; record receiver-visible identity and
   operator acknowledgment. Operator owns morning triage.
6. At activation, read current matching monitor success. Missing/failed or older
   than **60 minutes** invalidates deferral credit. Missing start at/after
   **01:00 London**, missing complete expected due run at/after **08:00 London**
   are unhealthy. Pre-deadline can be pending; yesterday's green cannot cover a
   missed daily obligation. Keep failures visible until repaired/acknowledged;
   no monitor auto-kill, restart or spend.

S5 requires the committed record plus current health. If honest coverage cannot
fit the morning SLA, return schedule/partition budget to Plan, never shrink tests.
If destination/acknowledgment is unavailable, S1-S3 can finish but S4/S5/card
completion remain pending; offline implementation does not depend on that choice.

### Guard inventory

**Guards=143, distinct mapped PCs=143, missing=0, duplicate PC mappings=0.**
The matrix below is the inventory and definition index. G-NNN maps exclusively
to PC-NNN, executed by exact case/method C487_GNNN in its named owner.
Rows are separate input variants of that guard; independently bypassable
completeness/provenance/assignment predicates have separate IDs even in one file.

Where an oracle names an existing assertion, the new exact case invokes the
same decision/assertion or the mapping is amended to the existing exact method
with its full expanded count before execution. Other guards must be satisfied
so the intended defect reaches its assertion. If another guard masks it, assert
the forbidden operation attempt or redesign the fixture; a survivor is a gap.
Newly discovered independent guards get new IDs and recosting.

### Positive controls

**Mutation only**: confirm Code SHA=HEAD, clean tracked source/index, no running
Code command and inventoried untracked outputs. Establish shared green baseline;
apply each compiling defect, observe exact intended assertion red, restore bytes,
refresh timestamps/rebuild, verify fresh binaries, rerun same exact method green.
Native controls use exact C487 method, script controls only exact -Case. Fresh
evidence contains every named variant: **228 rows per red/green battery**.

No class/namespace/full-script PC broadening. Controls sharing a file run
separately. Independent-file batching is allowed only under CARD-0451 with
per-PC evidence; no such savings are assumed in the floor. Parse/build/fixture
errors, zero tests and stale binaries are invalid arms. Never mutate live jobs,
shared clones or credentials. Never commit mutants. Restore before returning
missing detection to Code; Review judges evidence read-only.

Prompt mutants alter composed instructions and prove the contract check only;
they do not establish agent behavior or delivery. Operational V-9 failure
acceptance complements, and does not replace, this battery.

#### B: scripts/nightly-run.ps1

Owner: `scripts/test-nightly-run.ps1`; ordinary V-2. 25 guards, 56 expanded rows per arm.

| Guard / PC | Rows | Safety invariant / variants | Deliberate defect | Exact case's decisive assertion |
|---|---:|---|---|---|
| G-001 / PC-001 | 8 | Shared clone paths are refused after canonicalization (main, main child, worktree root/child; case and dot aliases). | Return false from shared-tree refusal. | C487_G001: Refusal=3; fake Git trace has zero reset/clean calls; sentinel bytes unchanged. |
| G-002 / PC-002 | 2 | Linked Git worktrees are refused wherever located (.git file and common-directory mismatch). | Accept linked worktree as independent clone. | C487_G002: No reset/clean; foreign sentinel and worktree registration preserved. |
| G-003 / PC-003 | 3 | Reparse traversal cannot escape clone/state/log ownership (leaf, ancestor, swapped-before-delete). | Skip reparse resolution/recheck. | C487_G003: Refusal before mutation; external sentinel remains. |
| G-004 / PC-004 | 3 | Run filesystem context requires proven ownership (unmarked clone, wrong origin, state/log overlap with cleaned checkout). | Accept directory existence as valid run-context ownership. | C487_G004: No reset/clean/prune; rejected ownership field named; state and foreign sentinels preserved. |
| G-005 / PC-005 | 3 | Log pruning requires exact producer receipt and resolved containment (foreign old directory, active run, escaped child). | Select old directories solely by age. | C487_G005: All three protected directories retained. |
| G-006 / PC-006 | 1 | Lock acquisition is atomic. | Replace exclusive create/open with exists-then-write. | C487_G006: Two barrier-synchronized owned children yield exactly one owner; loser never invokes Git. |
| G-007 / PC-007 | 2 | A live lock owner is never displaced by age (239 and 241 minutes). | Force stale-lock replacement despite proven live owner. | C487_G007: Refusal=live-owner; zero replacement/delete attempts in executor trace; original owner/state retained. |
| G-008 / PC-008 | 3 | Hop continuation binds run ID plus parent PID/start identity (wrong run, reused PID, unrelated parent). | Accept any live parent PID. | C487_G008: Continuation refuses; no child build/sync. |
| G-009 / PC-009 | 1 | The parent holds the lock until the awaited child exits. | Release lock immediately after hop launch. | C487_G009: Third contender remains refused while child barrier is held. |
| G-010 / PC-010 | 2 | Only the current owner releases a lock (loser, replaced identity). | Delete the named lock unconditionally in finally. | C487_G010: Owner's lock still exists and retains identity. |
| G-011 / PC-011 | 1 | Started is durable before first fetch/build. | Move Started after fetch. | C487_G011: Injected fetch loss leaves readable Started with the attempted run ID and phase. |
| G-012 / PC-012 | 2 | State replacement is atomic (before replace, after replace). | Write directly over the state file. | C487_G012: Independent reader sees whole old/new JSON, never partial JSON. |
| G-013 / PC-013 | 3 | Persistence failure is a failing run (Started, progress, final). | Swallow write failure and exit zero. | C487_G013: Exit nonzero, no complete-green advancement; failure phase retained where writable. |
| G-014 / PC-014 | 1 | Run directories are unique under equal clock stamps. | Use minute-only names. | C487_G014: Two sequential runs at frozen time have different GUID directories and intact artifacts. |
| G-015 / PC-015 | 1 | A refused contender cannot overwrite last attempt. | Enable final-state write on lock refusal. | C487_G015: Active run record is byte-identical after contender exit. |
| G-016 / PC-016 | 3 | Manual/feature/NoReport runs have separate state roots. | Use the scheduled StateRoot for overrides. | C487_G016: Scheduled last attempt and green records unchanged; manual result names private root. |
| G-017 / PC-017 | 1 | Complete-green advancement requires complete expected coverage. | Drop coverageComplete predicate. | C487_G017: Prior green pointer retained after incomplete run. |
| G-018 / PC-018 | 1 | Complete-green advancement requires master ref. | Drop master-ref predicate. | C487_G018: Feature-ref run cannot advance scheduled-green pointer. |
| G-019 / PC-019 | 1 | Complete-green advancement requires scheduler provenance. | Drop scheduled-trigger predicate. | C487_G019: Manual full green cannot advance scheduled-green pointer. |
| G-020 / PC-020 | 1 | Complete-green advancement requires delivered report. | Drop reportDelivered predicate. | C487_G020: Unreceived report cannot advance scheduled-green pointer. |
| G-021 / PC-021 | 2 | Scheduled runs execute again at the same SHA (previous full green, previous client green). | Restore unchanged-SHA shortcut. | C487_G021: Fake test executor invoked on both dates; distinct attempts recorded. |
| G-022 / PC-022 | 4 | Preflight and build failures cannot become green (fetch, Docker, disk, build). | Ignore failing phase and set success. | C487_G022: Exit nonzero; required coverage incomplete; reporter receives phase failure. |
| G-023 / PC-023 | 3 | Every nonzero report exit survives the bootstrap (1, 2, 3). | Force reportExit to zero before aggregate verdict. | C487_G023: Final status nonzero, reportDelivered=false for every row; test outcome unchanged. |
| G-024 / PC-024 | 2 | Test red survives successful reporting (assertion failure, timeout). | Derive overall success from reporter exit alone. | C487_G024: Overall red while reportDelivered=true. |
| G-025 / PC-025 | 2 | Self-update forwards identity, options and failure (nonzero child, missing completion). | Substitute hop success/accept missing result. | C487_G025: Outer nonzero; one run identity, no second reset or duplicate report. |

#### E: scripts/nightly-tests.ps1 and coverage/evidence helper

Owner: `scripts/test-nightly-tests.ps1`; ordinary V-3. 33 guards, 54 expanded rows per arm.

| Guard / PC | Rows | Safety invariant / variants | Deliberate defect | Exact case's decisive assertion |
|---|---:|---|---|---|
| G-026 / PC-026 | 3 | Policy covers every IsTestProject project (add, rename, omit a project). | Remove project-universe equality check. | C487_G026: Inventory error names the unmatched project before execution. |
| G-027 / PC-027 | 2 | Policy schema/hash are authoritative (unsupported schema, stale hash). | Trust supplied hash/version without recomputing. | C487_G027: No complete result or green pointer. |
| G-028 / PC-028 | 3 | Explicit empty suite selections are errors (empty string, commas, whitespace). | Normalize to an empty successful loop. | C487_G028: Nonzero selection result, zero launches. |
| G-029 / PC-029 | 2 | Unknown suite/chunk selections are refused (typo, missing chunk). | Silently drop unknown IDs. | C487_G029: Nonzero result naming the unknown ID. |
| G-030 / PC-030 | 3 | Evidence is fresh and invocation-owned (old timestamp, wrong run directory, previous invocation file). | Read the latest result regardless of provenance. | C487_G030: coverageComplete=false despite exit zero. |
| G-031 / PC-031 | 3 | Missing/malformed/empty execution files fail closed. | Treat absent parse output as zero failures. | C487_G031: coverageComplete=false; diagnostic names the file/parse failure. |
| G-032 / PC-032 | 3 | Execution identities resolve from definitions/UIDs (missing definition, duplicate identity, conflicting class). | Use display names or last duplicate wins. | C487_G032: Identity error; same-display neighboring class cannot satisfy expectation. |
| G-033 / PC-033 | 2 | Counters must agree with expanded terminal rows (inflated total, failed count hidden). | Accept aggregate counters independently. | C487_G033: Red with row/counter mismatch. |
| G-034 / PC-034 | 1 | Nonzero test process exit cannot be green despite passing rows. | Ignore process exit. | C487_G034: testsPassed=false with exit 1 and passing row evidence. |
| G-035 / PC-035 | 1 | Failed required rows cannot be green despite process exit zero. | Ignore failed terminal outcomes. | C487_G035: testsPassed=false with exit 0 and a failed row. |
| G-036 / PC-036 | 2 | Required skips are incomplete (Slow skipped, broker skipped). | Count skip as passed/eligible exclusion. | C487_G036: coverageComplete=false and skipped identities listed. |
| G-037 / PC-037 | 1 | Every eligible class occurs in at most one chunk. | Remove disjointness validation. | C487_G037: Duplicate-class error before launches. |
| G-038 / PC-038 | 1 | Every eligible class occurs in some chunk. | Remove exhaustiveness validation. | C487_G038: Missing-class error even when suite totals match. |
| G-039 / PC-039 | 2 | Every expanded row finishes (Arguments row, MethodDataSource row). | Validate only class presence/aggregate total. | C487_G039: Exact missing UID named; replacement pass from another row does not compensate. |
| G-040 / PC-040 | 2 | Discovery is never execution evidence (list-only, diagnostic containing only InProgress). | Accept discovered/in-progress state as terminal pass. | C487_G040: No complete coverage; zero terminal rows rejected. |
| G-041 / PC-041 | 1 | Run SHA must match actual built SHA. | Bypass SHA equality. | C487_G041: coverageComplete=false with exact SHA mismatch. |
| G-042 / PC-042 | 1 | Run ref must match claimed required ref. | Bypass ref equality. | C487_G042: Feature build cannot satisfy master obligation. |
| G-043 / PC-043 | 1 | Execution policy must match discovered and built policy. | Bypass policy equality. | C487_G043: Different policy hash cannot satisfy coverage. |
| G-044 / PC-044 | 3 | Headed/live opt-ins are cleared per child (inherited Headed, HeadedLong, explicit vendor gate). | Inherit caller environment unchanged. | C487_G044: Captured safe child environment has each live gate disabled; fake native cases still selected. |
| G-045 / PC-045 | 2 | Child live credential names are cleared (Telegram and provider synthetic values). | Omit credential sanitization. | C487_G045: Child environment lacks both sentinel values. |
| G-046 / PC-046 | 1 | Credential values never enter retained logs/results. | Serialize unsanitized captured child environment. | C487_G046: All retained bytes lack synthetic credential sentinel. |
| G-047 / PC-047 | 1 | Disposable broker suite is explicitly enabled. | Omit ANTIPHON_BROKER_TESTS for messaging child. | C487_G047: Broker test execution receipt required; skips fail coverage. |
| G-048 / PC-048 | 1 | Broker coverage uses the fixture-owned disposable endpoint. | Supply shared bootstrap in fixture launch configuration. | C487_G048: Captured endpoint is container-owned; external connection trace empty. |
| G-049 / PC-049 | 1 | Native project groups remain serial. | Start the next group before previous exit. | C487_G049: Event trace proves maximum active native groups=1 across all five projects. |
| G-050 / PC-050 | 2 | Next native group waits for owned-child cleanup (normal exit with child, timeout). | Treat parent exit as cleanup completion. | C487_G050: No next launch until owned descendant exit evidence; foreign process untouched. |
| G-051 / PC-051 | 1 | Independent chunks continue after test red when cleanup succeeded. | Break suite loop on first failed assertion. | C487_G051: Later chunk's terminal rows retained; overall result stays red. |
| G-052 / PC-052 | 1 | Existing watchdog bounds cannot silently waive coverage. | Convert timed-out class to an exclusion or enlarge its budget. | C487_G052: Timed-out class incomplete; configured test/watchdog deadlines unchanged. |
| G-053 / PC-053 | 2 | Client wrapper forwards precise argv (file filter, JSON reporter/output path containing spaces). | Use npx/source-text splat or drop one argument. | C487_G053: Exactly requested file and structured result path; neighboring file absent. |
| G-054 / PC-054 | 1 | Client nonzero exit survives JSON reporting. | Force wrapper exit zero after failed Vitest. | C487_G054: Returned exit and visible verdict remain nonzero. |
| G-055 / PC-055 | 1 | Client structured execution evidence is mandatory. | Accept exit zero without JSON. | C487_G055: Client coverage incomplete with missing JSON. |
| G-056 / PC-056 | 1 | Required script census is explicit and safe. | Execute all test-*.ps1 as no-argument jobs. | C487_G056: 13 existing paths each have a disposition; wrappers/argument consumers never wildcard-launched. |
| G-057 / PC-057 | 1 | Frontend build freshness is required before credit. | Reuse pre-existing dist as current build. | C487_G057: Build receipt bound to run/SHA; stale dist rejected. |
| G-058 / PC-058 | 1 | Client lint is a separately required passing step. | Omit lint invocation/result. | C487_G058: Incomplete/red even when build and tests pass. |

#### C: shared classification metadata; duration tripwire; local limiter contracts

Owner: `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests (new)`; ordinary V-4. 18 guards, 24 expanded rows per arm.

| Guard / PC | Rows | Safety invariant / variants | Deliberate defect | Exact case's decisive assertion |
|---|---:|---|---|---|
| G-059 / PC-059 | 1 | Slow marker implies exact registry entry. | Remove marker-to-registry comparison. | C487_G059: Unregistered marked FQN reported. |
| G-060 / PC-060 | 1 | Registry entry implies Slow marker. | Remove registry-to-marker comparison. | C487_G060: Unmarked registered FQN reported. |
| G-061 / PC-061 | 1 | Unknown registry class is invalid. | Ignore unresolved registry entry. | C487_G061: Unknown FQN reported. |
| G-062 / PC-062 | 2 | Duplicate/ambiguous registry entries fail (duplicate FQN, shared simple name). | Deduplicate or choose first match. | C487_G062: Exact duplicate/ambiguity reported. |
| G-063 / PC-063 | 1 | New repository entries require FQN. | Accept legacy simple names in repository mode. | C487_G063: Simple entry refused; separate legacy caller mode still allowed. |
| G-064 / PC-064 | 1 | Every registry entry needs adjacent reason and evidence reference. | Ignore reason/reference validation. | C487_G064: Missing-reason declaration refused. |
| G-065 / PC-065 | 1 | Slow is class-only. | Accept method-level Slow. | C487_G065: Method FQN named as invalid. |
| G-066 / PC-066 | 1 | Partial and combined attributes form one compiled declaration. | Replace compiled metadata with line scanning. | C487_G066: Two-file partial class appears once with both methods/categories. |
| G-067 / PC-067 | 2 | Inherited tests/categories follow pinned runner semantics (InheritsTests present, absent). | Flatten all base methods or discard inherited category. | C487_G067: Metadata matches discovery in both shapes, including omitted base test without InheritsTests. |
| G-068 / PC-068 | 1 | Antiphon.Tests keeps Unit xor Integration. | Permit both lane categories when Slow is present. | C487_G068: Existing lane guard and synthetic mixed-lane check reject the class. |
| G-069 / PC-069 | 2 | Slow does not alter correctness/eligibility (Slow ordinary, Slow OptIn). | Translate Slow to Skip/Explicit or drop unaffected category. | C487_G069: Synthetic ordinary Slow rows execute; manual row remains explicitly excluded. |
| G-070 / PC-070 | 1 | Wholly manual OptIn cannot hide mixed fake/live classes. | Classify Telegram conformance as wholly manual. | C487_G070: Fake leg stays required; only real branch appears in exclusions. |
| G-071 / PC-071 | 1 | Each assembly-local process limiter stays one. | Change ProcessSpawnLimit.Limit to 2. | C487_G071: Existing Caps_concurrent_process_spawning_tests_at_one fails. |
| G-072 / PC-072 | 1 | Changed process-spawning classes retain local limiter and serialization attributes. | Remove limiter from newly changed harness class. | C487_G072: Population census names missing class; annotation snapshot detects serialization loss. |
| G-073 / PC-073 | 2 | Legacy duration matching remains exact and case-insensitive (simple, FQN). | Use substring/display-argument matching. | C487_G073: C475 collision fixture plus new guard rejects unrelated slow class. |
| G-074 / PC-074 | 1 | Duration exemption requires resolved testId-to-definition identity. | Allow unresolved identity to be exempted. | C487_G074: C475_UnresolvedIdentityCannotBeExempted assertions fail. |
| G-075 / PC-075 | 1 | Duration threshold and expanded body sums remain exact. | Change >=5 to >5. | C487_G075: C475_ExpandedRowsAndThresholdAreExact detects missing 5.000-second hit. |
| G-076 / PC-076 | 3 | Duration invalid input cannot be green (empty results, malformed XML, missing path). | Return zero for no rows or parse error. | C487_G076: Nonzero invalid-input outcome; no green message. |

#### R: scripts/nightly-report.ps1

Owner: `scripts/test-nightly-report.ps1`; ordinary V-5. 22 guards, 31 expanded rows per arm.

| Guard / PC | Rows | Safety invariant / variants | Deliberate defect | Exact case's decisive assertion |
|---|---:|---|---|---|
| G-077 / PC-077 | 1 | Incident closure requires complete required coverage. | Drop coverageComplete closure predicate. | C487_G077: Partial-green Backlog incident stays open. |
| G-078 / PC-078 | 1 | Incident closure requires current required policy. | Drop policy closure predicate. | C487_G078: Different-policy green cannot close. |
| G-079 / PC-079 | 1 | Incident closure requires scheduled provenance. | Drop scheduled-trigger closure predicate. | C487_G079: Manual full-master green cannot close. |
| G-080 / PC-080 | 1 | Incident closure requires master ref. | Drop master-ref closure predicate. | C487_G080: Feature scheduled fixture cannot close. |
| G-081 / PC-081 | 1 | NoReport cannot claim delivery/closure eligibility. | Ignore reporting-mode eligibility. | C487_G081: NoReport record cannot close or claim receipt. |
| G-082 / PC-082 | 1 | A non-Backlog card cannot auto-close. | Remove status predicate. | C487_G082: InProgress/Review fixture remains in its column. |
| G-083 / PC-083 | 1 | Agent assignment independently prevents auto-close. | Ignore assignedAgentId while checking eligibility. | C487_G083: Agent-assigned Backlog card remains open. |
| G-084 / PC-084 | 1 | Session ownership independently prevents auto-close. | Ignore ownerSessionId while checking eligibility. | C487_G084: Session-owned Backlog card remains open. |
| G-085 / PC-085 | 1 | Missing terminal-column evidence cannot authorize a move. | Invent/fallback a terminal column. | C487_G085: No move; reporting error or preserved card and discussion. |
| G-086 / PC-086 | 2 | Repeated red is one incident (same run retry, next red run). | Always POST a new card. | C487_G086: Exactly one open fixture incident; next run updates its history. |
| G-087 / PC-087 | 1 | Incomplete/truncated card census cannot prove absence. | Treat truncated no-match list as empty. | C487_G087: No duplicate create; reporting fails or completes pagination then uses existing card. |
| G-088 / PC-088 | 2 | Mutating writes use fresh concurrency tokens (content race, close race). | Reuse the census token after competing update. | C487_G088: Conflict refreshes state/token; changed assignment prevents close; no lost update. |
| G-089 / PC-089 | 2 | Previous failure becomes fixed only after matching pass (omitted method, different argument row). | Use failed-name set subtraction. | C487_G089: Both are not-retested, never fixed. |
| G-090 / PC-090 | 3 | Rerun commands use registry project and exact method (runner, PtyHost, Messaging). | Fallback to Antiphon.Tests or display-name filter. | C487_G090: Correct three project paths and escaped method identities. |
| G-091 / PC-091 | 2 | Accepted card API writes require correlated read-back (wrong run body, missing revision). | Set reportDelivered on POST/PATCH 2xx alone. | C487_G091: reportDelivered=false until correct run ID/revision is readable. |
| G-092 / PC-092 | 2 | Retry after uncertain create/update is idempotent (response lost after persistence, crash before receipt). | Retry blind create or drop pending delivery. | C487_G092: Read-back recovers same card/run; exactly one occurrence of incident/run section. |
| G-093 / PC-093 | 2 | Reporting failure retains actionable card.md (API unavailable, malformed summary). | Skip failure-artifact persistence. | C487_G093: Nonzero result; file contains run/phase/coverage and evidence location. |
| G-094 / PC-094 | 2 | Failure-artifact persistence failure cannot be hidden (disk write denied, state write denied). | Catch and return reporting success. | C487_G094: Nonzero structured failure remains observable to outer monitor. |
| G-095 / PC-095 | 1 | Only a nightly-auto-closed incident may be reopened automatically. | Ignore terminalReason authority. | C487_G095: Human-closed Done card unchanged; complete census may create new incident. |
| G-096 / PC-096 | 1 | Automatic reopen is limited to the existing seven-day window. | Ignore updatedAt cutoff. | C487_G096: Nine-day-old auto-closed card unchanged; complete census may create new incident. |
| G-097 / PC-097 | 1 | DryRun sends zero HTTP mutations. | Ignore DryRun branch. | C487_G097: Zero POST/PATCH/DELETE calls. |
| G-098 / PC-098 | 1 | Reporter does not forward inherited task authority. | Attach synthetic inherited task token. | C487_G098: Headers/logs remain free of fixture sentinel; T10 retained. |

#### M: scripts/windmill/ nightly wrapper, registration, health and receipt helpers

Owner: `scripts/test-nightly-health.ps1`; ordinary V-6. 28 guards, 46 expanded rows per arm.

| Guard / PC | Rows | Safety invariant / variants | Deliberate defect | Exact case's decisive assertion |
|---|---:|---|---|---|
| G-099 / PC-099 | 2 | Registration must exist (missing script, missing schedule). | Treat absent object as healthy. | C487_G099: Durable failed monitor result identifies missing registration. |
| G-100 / PC-100 | 2 | Schedule must be enabled and unpaused (disabled, paused). | Ignore activation state. | C487_G100: Unhealthy even with a recent manual green. |
| G-101 / PC-101 | 1 | Deployed script hash equals reviewed definition hash. | Trust checked-in hash without read-back. | C487_G101: Wrong deployed content refuses qualification. |
| G-102 / PC-102 | 1 | Nightly worker routing is desktop-only. | Ignore deployed tag mismatch. | C487_G102: Non-desktop nightly registration rejected. |
| G-103 / PC-103 | 1 | Scheduled job has no suite/ref override. | Accept overridden job arguments. | C487_G103: Partial/feature scheduled args refuse qualification. |
| G-104 / PC-104 | 1 | Desktop worker version must match server version. | Ignore deployment version mismatch. | C487_G104: Qualification false before native job acceptance. |
| G-105 / PC-105 | 2 | Start grace includes desktop queue delay (unstarted at 01:00, queued beyond grace). | Only inspect started jobs. | C487_G105: Overdue failure at deadline; 00:59:59 remains pending. |
| G-106 / PC-106 | 2 | Morning completeness uses the expected local due run (missing at 08:00, yesterday's green). | Use any recent green. | C487_G106: Missing expected run flagged; pre-deadline state remains pending. |
| G-107 / PC-107 | 4 | London due-time logic handles both DST transitions (day before/after each). | Use UTC midnight or fixed offset. | C487_G107: Exactly one 00:30 local obligation per date; correct UTC identity. |
| G-108 / PC-108 | 1 | Native completion correlates to expected Windmill job/run identity. | Accept different run ID. | C487_G108: No completeness credit from foreign run. |
| G-109 / PC-109 | 1 | Monitor completion matches expected build SHA. | Ignore SHA mismatch. | C487_G109: Monitor fails for wrong build. |
| G-110 / PC-110 | 1 | Monitor completion matches required policy. | Ignore policy mismatch. | C487_G110: Monitor fails for stale-policy coverage. |
| G-111 / PC-111 | 2 | Every wrapper/SSH failure is durable (SSH nonzero, result absent after exit 0). | Return empty/success from bridge. | C487_G111: Windmill structured failure, no fabricated native completion. |
| G-112 / PC-112 | 1 | Monitor requires comprehensive native coverage. | Ignore coverageComplete=false. | C487_G112: Durable incomplete-coverage failure. |
| G-113 / PC-113 | 1 | Monitor treats required test failure as unhealthy. | Ignore testsPassed=false. | C487_G113: Durable test-red result despite successful reporting. |
| G-114 / PC-114 | 1 | Monitor requires report delivery. | Ignore reportDelivered=false. | C487_G114: Durable reporting outage despite test green. |
| G-115 / PC-115 | 2 | Native loss/stale phase is detected (Started never completed, stale activity). | Treat process missing/progress silence as success. | C487_G115: Failed/overdue result; no terminate/restart request for foreign processes. |
| G-116 / PC-116 | 1 | Monitor runs independently of desktop queue. | Assign desktop tag to monitor. | C487_G116: Definition/read-back assertion rejects it; queued-desktop scenario still evaluates on server2. |
| G-117 / PC-117 | 3 | Missing/stale/failed monitor evidence cannot authorize dispatch deferral. | Accept activation artifact without recent monitor. | C487_G117: Readiness false at >60 minutes or failed result; legacy plus needed deferred selection restored. |
| G-118 / PC-118 | 2 | Notification is persisted before enqueue (failure, recovery). | Enqueue without durable notification identity. | C487_G118: Restart can find each pending event; no false delivered state. |
| G-119 / PC-119 | 2 | Enqueue failure is retryable and red (busy sink, unavailable sink). | Drop event or mark delivered when enqueue throws. | C487_G119: Pending durable identity survives; health/reporting remain failed. |
| G-120 / PC-120 | 2 | Transport acceptance is not recipient receipt (202 queued, 200 without recipient read-back). | Mark received on send acknowledgement. | C487_G120: Pending until same notification ID is visible to destination. |
| G-121 / PC-121 | 2 | Receipt correlation rejects other events (wrong run, wrong notification ID). | Match only text/status. | C487_G121: No receipt credit; intended event remains pending. |
| G-122 / PC-122 | 2 | Crash after enqueue/before receipt is recoverable (busy recipient, already eligible). | Forget pending identity on restart. | C487_G122: Same event reaches recipient once logically and receipt becomes durable. |
| G-123 / PC-123 | 2 | Persistent-red deduplication does not hide new failures (same event poll, different failure identity). | Suppress all subsequent failed events. | C487_G123: Same event not spammed; distinct new failure retained/delivered. |
| G-124 / PC-124 | 1 | Recovery cannot clear an unresolved coverage/execution failure. | Clear on any later green-like poll. | C487_G124: Still-incomplete incident remains visible. |
| G-125 / PC-125 | 1 | Recovery cannot clear notification state before recipient receipt. | Mark recovered immediately after send. | C487_G125: Recovery event remains pending until correlated receipt. |
| G-126 / PC-126 | 2 | Unauthorized destination is never silently selected (unset destination, unexpected override). | Fallback to a built-in recipient. | C487_G126: Offline preparation succeeds; live enabling refuses and sends zero messages. |

#### P: server/Bundles/stage-*.md and composed owner instructions

Owner: `Antiphon.Tests.Application.ScopedVerificationInstructionTests (new)`; ordinary V-7. 17 guards, 17 expanded rows per arm.

| Guard / PC | Rows | Safety invariant / variants | Deliberate defect | Exact case's decisive assertion |
|---|---:|---|---|---|
| G-127 / PC-127 | 1 | Reduced policy activation requires committed S4 qualification. | Remove qualification evidence prerequisite from composed contracts. | C487_G127: Composed Code/Review explicitly refuse activation without S4; V-8 rubric rejects missing proof. |
| G-128 / PC-128 | 1 | Reduced policy use requires current matching healthy monitor evidence. | Remove current-health prerequisite from composed contracts. | C487_G128: Composed Code/Review remove deferral credit on stale/missing/failed health; V-8 rubric rejects it. |
| G-129 / PC-129 | 1 | Active Code/Review never append Unit automatically. | Restore unconditional Unit-plus-affected sentence. | C487_G129: Conflict assertion finds forbidden unconditional default in complete compositions. |
| G-130 / PC-130 | 1 | Regression selection schema includes identities/counts/relevance/disposition/cost/command/reopen conditions. | Remove the Regression selection schema obligation. | C487_G130: Exact missing field rejected in composed TestDesign and folded Plan rules. |
| G-131 / PC-131 | 1 | New/changed and affected Slow guards stay run-now. | Permit Slow-only exclusion. | C487_G131: Landing/runner helper example rejected; affected capstones remain selected. |
| G-132 / PC-132 | 1 | Nightly deferral names actual qualified obligation. | Permit unnamed rest-nightly disposition. | C487_G132: Missing suite/policy/readiness example rejected. |
| G-133 / PC-133 | 1 | Affected manual acceptance remains pending until executed. | Count skipped manual acceptance as green. | C487_G133: Manual E2E example rejected without actual acceptance. |
| G-134 / PC-134 | 1 | Scope change requires affected consumers and recosting. | Allow original selection despite changed helper. | C487_G134: New helper/guard scenario forces selection expansion or TestDesign. |
| G-135 / PC-135 | 1 | Independent guard inventory stays one-to-one with defined PCs. | Delete guard count/missing/duplicate completeness requirement. | C487_G135: Missing/duplicate guard-PC example rejected. |
| G-136 / PC-136 | 1 | Code owns ordinary V/R; Mutation owns every PC/variant. | Reassign the PC battery from Mutation to Code. | C487_G136: Actual role compositions preserve C470 rules and all PC obligations. |
| G-137 / PC-137 | 1 | Delivery/receipt inventory survives bundle compaction. | Remove the complete delivery-inventory/receipt paragraph. | C487_G137: Composed design/review receipt assertions fail; this is text protection only. |
| G-138 / PC-138 | 1 | Inspection of test bodies and fixtures remains mandatory. | Delete inspection requirement. | C487_G138: Composed TestDesign lacks required inspection and rejects the artifact. |
| G-139 / PC-139 | 1 | Separate numeric Code and Mutation floors plus total remain mandatory. | Replace the complete Cost contract with an unquantified estimate. | C487_G139: Cost contract rejects missing totals and uncosted PC arms. |
| G-140 / PC-140 | 1 | Every stage bundle remains ASCII. | Insert a non-ASCII code point. | C487_G140: Existing normalized bundle ASCII assertion fails. |
| G-141 / PC-141 | 1 | Every normalized stage bundle remains <=2500 characters. | Append enough ASCII text to exceed 2500. | C487_G141: Existing stage size assertion fails; cap unchanged. |
| G-142 / PC-142 | 1 | Mutation precedes required hard-card Review. | Remove stage-order constraint in composed rules. | C487_G142: C470 role contract requires Mutation then hard Review. |
| G-143 / PC-143 | 1 | Original Code task remains the landing owner. | Remove original-Code-owner requirement. | C487_G143: Composed Code/Mutation/Review preserve owner handoff; no Shared Mutation landing. |


### Out of scope

No new test assemblies, lazy DB startup, higher process concurrency, timeouts
relaxed, real-Git matrices reduced, vendor homes changed, live broker configured
or Windows Scheduled Task added. LandingVerifier, LandVerifyFilter, rebase,
automatic land commands and existing stage order remain owned by their existing
contracts. Annotation preservation is not permission to change test assertions.
No live operational positive control runs in a developer fixture or Mutation.

The source-path-to-test dependency judgment remains TestDesign/Review work; the
execution policy is a universe/eligibility inventory, not an automatic dependency
oracle. Machine checks cannot prove an agent made the right semantic exclusions.
The nine-row probe establishes a feasible inventory seam only; full production
data-source compatibility, live notification receipt and scheduled completeness
remain mandatory V-9 acceptance, with policy inactive until proven.

### Cost

**All future costs below are planning floors/allowances, not measured timings.**
They are mandatory numeric inputs to dispatch budgeting, never a kill deadline
or permission to omit Slow/failed coverage. Record actual build/startup/body/wall
times and recost when they differ. TestDesign ran no broad timing experiment.

| Ordinary Code verification component | Estimated minutes |
|---|---:|
| S1-S3 isolated build/setup (shared by scoped native runs) | 3.0 |
| Current Unit baseline while policy inactive | 1.5 |
| B/E/R/M fixtures, including baseline reporter | 6.0 |
| C metadata/selector/native guard work and V-1 probe | 2.5 |
| Existing wrapper/tripwire/lane/limiter regressions | 2.0 |
| Composed instructions, exact formatter selections and evidence inspection | 3.0 |
| **S1-S3 ordinary floor** | **18.0** |
| Separate S5 build/composition/rubric/current-health read-back | **6.0** |
| **Ordinary Code floor across prerequisite + activation slices** | **24.0** |

S1-S3 may prepare/test gated instructions, but active S5 acceptance still runs
after qualification. Repeated slice dispatches each pay required setup and the
current baseline; these numbers assume one prerequisite verification pass and
one activation pass. All V/R remain required, with shared invocations mapped.

| Mutation component (no batching/concurrency discount) | Count / per-unit estimate | Minutes |
|---|---|---:|
| Fresh setup and green baseline of the designed battery | one combined baseline, output ownership/read-back | 10.0 |
| B/E/R/M exact script PCs | **108** controls x **0.5 min** red+restore+green | 54.0 |
| C/P exact native PCs | **35** controls x **2.5 min** red+rebuild+restore+rebuild+green | 87.5 |
| **Mutation executable floor** | **143 PCs**, **286 exact arm invocations**, **456 expanded red/green rows** | **151.5** |
| Mutation inspection, missing-control search and final report | additional stage allowance | 8.0 |
| **Mutation dispatch floor including analysis/reporting** | round allocation up to 160 minutes; authoring repairs return to Code | **159.5** |

The script per-control allowance includes both PowerShell starts, fixture variants,
restore and result validation. Native per-control allowance includes both
incremental builds, two Antiphon process startups (including DB hook where active),
method rows, restoration and evidence checks. Parameterized rows run within that
exact method invocation; none are omitted from the 456-row count. Reusing one
built DLL across red/green is forbidden. Class/case methods have at most eight
listed variants; if an implemented row is expensive, record the increment.

**Total local verification floor = 24.0 + 151.5 = 175.5 minutes.**
Mutation analysis adds 8 minutes, so the combined Code/Mutation stage verification
allocation is **183.5 minutes**, excluding implementation authoring/repair.
Do not quote the 18-minute ordinary slice as total verification cost.

V-9 is separately mandatory: reserve **120-360 minutes per full observation**,
for **two** full runs (**240-720 minutes**), plus **30 minutes** deployment/
fixture-board/receipt/read-back acceptance. This is **270-750 minutes** of
qualification observation, not a measured full-suite duration or timeout.
Thus initial end-to-end verification reservation has a numeric lower planning
bound of **445.5 minutes = 175.5 + 270**, plus the wall-calendar wait for a real
00:30 fire and any triage/repair. Keep Code free for other work while awaiting
that scheduled acceptance; the card/policy remains unqualified.

Required hard Review additionally reruns the ordinary slices (allow **24 minutes**
under the same assumptions) and judges Mutation/operational evidence read-only.
Landing verification/rebase costs are outside these estimates and are not waived.
Do not rerun full qualification for a prompt-only S5 edit unless it changes the
qualified workload, eligibility, runner/evidence or deployment contract.

Quantified savings: removing a duplicate full-Unit invocation after activation
can avoid historical **65-70 seconds per dispatch**; necessary selected assembly
startup remains. Historical Unit versus broad selection differed by about
**93 minutes with different coverage**; it is not a guaranteed present saving.
No Mutation arm savings are assumed. Once measurements exist, report actual
ordinary selection savings and scheduled amortization separately from Mutation.

### Handoff completeness and stage settlement

Required new battery: B 56 + E 54 + C 24 + R 31 + M 46 + P 17 = **228**
expanded cases; six metadata obligations (five executed, E2E metadata-only);
existing exact native regressions **89**; reporter baseline **64 assertions**.
All **143** G IDs have distinct, defined PC IDs and exact case owners;
missing=0; duplicate mapping=0. New method/case names are implementation
requirements, not claims that tests already exist or passed.

TestDesign validation: temporary pinned probe **18 passes / 0 failures / 0 skips**
across three executions; unfiltered discovery **9**; baseline reporter
**64 assertions passed / 0 failed**. One rejected CLI combination
(list-tests + report-trx) is recorded as feasibility evidence, not a test failure
or a positive control. Repository .NET suites/full nightly were not run.

Next Code implements S1-S3 under the inactive policy, records each V/R and the
concrete built-SHA selection/count manifests, commits/pushes, and marks all
PC-001 through PC-143 pending for Mutation. Mutation then hard Review precede
landing. Caller arranges S4 operational qualification after prerequisite landing;
S5 is a separate gated activation slice. A prerequisite slice may settle complete
without pretending V-9/S5 or the overall card are complete. If implementation
introduces more independent guards, extend this inventory before final handoff.
