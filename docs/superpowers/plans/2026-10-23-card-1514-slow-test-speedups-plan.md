# CARD-1514: reduce slow-test work without losing witnesses

Plan owner `f4f07e8f`, 2026-10-23. Inspected source and fetched master:
`5073726ce95955a09cf0a40b64bfdd501cf99562`.

Start with the checkpoint class's scheduling boundary, then the script harness's
repeated setup. Preserve every fault cut, argument vector, independent observation and
real recovery operation. Database template cloning and the wider Git lane already
exist; rebuilding either is not the first saving. Windows attribution remains an
explicit measurement obligation, not a predicted eight-to-elevenfold improvement.

This is a Plan deliverable. The brief did **not** fold the independent TestDesign
stage into this dispatch. The closed selections below constrain that stage; it must
bind the baseline/candidate manifests and finish the verification design before Code.
No performance experiment, implementation checkpoint or Windows operation was run
by this Plan owner.

## Evidence and ground truth

Read together:

- [Bounded Linux profile](../../investigations/2026-10-22-card-1514-slow-test-profile.md)
  and [all per-test observations](../../investigations/2026-10-22-card-1514-per-test-observations.md),
  executable source `ffa98a8f76588c12df6b0437d24b541762b95f09`.
- [Windows group-10 Debug](../../investigations/2026-10-22-rc-group-10-timeout-debug.md)
  and the landed [CARD-1404 measured-budget plan](2026-10-22-card-1404-group-10-measured-budget-plan.md).
- [CARD-0856 wall-time plan](2026-10-22-card-0856-windows-wall-time-plan.md), which
  already owns schema-cache/Git-lane rollout and broader launch packing.

The profile's raw evidence and small profile tool are available in the assigned
Linux environment at `/work/test-evidence/card-1514-85ca6a8c/`. That is a historical
evidence coordinate, not a runner-placement setting. The Windows log paths in the
two historical reports are not mounted here.

| Card/brief assumption | What the code/evidence actually does | Consequence |
|---|---|---|
| PersistenceFailure takes 24–32 minutes | Windows completed totals were 1473.781 and 1938.054 s. The later Linux run was 52 passes, 169.93 summed test seconds, 249.21 driver seconds, median 2.65 s, p95 8.16 s. Sources, host load and instrumentation differ. | Use those numbers as budgets and provenance, not an OS-controlled comparison or a before/after result. |
| Per-test PostgreSQL create/migrate can become template cloning | `TestDbFixture.InitializeAsync` warms one process-owned container; `TestDbOperations.CloneAsync` already does `CREATE DATABASE ... TEMPLATE antiphon_tmpl` under a clone lock. CARD-1420 already caches schema bootstrap. `LandingSafetyHarness.InitializeAsync` clones a private DB and seeds one task. | Keep private DBs, contexts and fault interceptors. Do not add a second cloning/cache system or a shared mutable landing DB. |
| The 13.78 s Persistence DB gap is all removable seeding | It includes clone, DI and seed work. Across 52 cases its median/p95 was 0.220/0.392 s. | 13.78 s is a ceiling on the entire observed gap, not the expected saving from sharing one task row. |
| One landing repository clone can be shared read-only | `LandingGitFixture.InitializeAsync` uses ten Git commands, including a private observer clone. `AssertRemoteSourceAsync` fetches into new observer refs; `PushIndependentAsync` fetches, resets, commits and pushes there. The seed commits `fixture-owner.txt` with the unique task ID; crash workers validate it. | The observer is mutable. Share only immutable setup inputs; retain private canonical, source, bare remote, observer, HOME and task identity. Never copy linked-worktree administration. |
| Repeated landing invocations are redundant | Persistence has 101 service entries for 52 nodes. Its failure/restart/recovery calls prove distinct durable cuts; named wrappers also exercise explicit historical witnesses. CARD-1462 already batched production Git reads/pins. | Do not delete calls, alias tests, phase arguments, restarts or independent fetches. No new production Git cache, status suppression or fake landing implementation. |
| Fixed waits explain most landing time | No class-wide fixed sleep was found. The two CheckpointMatrix worker-death nodes total 58.20 s; their readiness poll is 100 ms with a two-minute bound. Publication's 12 delivery nodes use the real queue and system time. | Poll replacement alone can remove at most roughly 0.2 s across the two successful worker cases. Investigate queue wait contribution before changing its clock. |
| All three landing classes are serial | Persistence already has `GitProcessSpawnLimit`; the profile explicitly forced its width to one. CheckpointMatrix and Publication remain strict. CheckpointMatrix has 41 Git-only nodes and two nodes starting real dotnet workers. | Qualify existing Persistence scheduling without claiming it as new code. Physically separate checkpoint worker tests before moving the remaining class; mixed spawners stay strict. |
| RecoveryBoundary's 3740.74 s sum is a class cliff | Its driver wall was 68.40 s, test-interval union 46.409 s, with up to 96 overlapping tests. Seven query-budget failures are CARD-1522. | Do not serialize it, increase its concurrency, share its DB or promise an hour saved. Leave that repair with its owner. |
| RemoteScriptContract has a measured 4591 s class time | 4591 is a Windows pace estimate. Linux stopped at 587.19 driver seconds: 29 completed passes, no TRX. `C1008_Recycle_resume_requires_matching_receipt` was interrupted at 457.604 s. | Finish attribution by bounded methods/scenarios, not another whole-class timeout. Deadline cancellation is not a product assertion failure or a completed timing. |
| Each remote test node is one script invocation | The interrupted method contains 16 independent fixtures and 32 script runs. `C1008HostFixture` materializes four real Compose configurations per fixture using Node and `docker compose config`. C1374's host witness expands into 32 transport vectors and already replaces retry sleeps with logging. | Expose scenario boundaries, cache immutable materialization, retain all 32 executions and transport vectors. Do not optimize a sleep that is already fake. |
| Raising/splitting the old group budget is this card's speedup | CARD-1404 already installed 1560/1980/3780-second rows, with Publication marked `pace`, and uses the existing method splitter. The watchdog is still 3600000 ms. | Preserve the watchdog and historical outcomes. Any class move must carry its cost/selection mapping; splitting is not a test-body speedup. |

The instrumented landing profiles include 2362/2141/2111 extra snapshot Git reads
for Persistence/CheckpointMatrix/Publication. Their wrapper interval unions are
85.85/91.29/120.70 s; initial Git setup totals are 10.41/12.84/19.95 s. Those columns
overlap. Never add them, or report disabling capture as faster product tests.

## Decisions

- **D-1 — Preserve semantic work.** Keep all original scenario vectors, assertions,
  independent connections/observers, fault-trigger checks, subprocess death cuts,
  restarts and post-recovery checks. Rejected: replacing the landing matrices with
  mocks, dropping apparently duplicated phase wrappers, sampling a matrix or
  loosening a count/deadline/assertion to obtain green.
- **D-2 — Share immutable inputs only.** Use a class-owned, lazily initialized
  Compose-materialization fixture in S2. Keep all Docker-state JSON, journals,
  HTTP traces, Git refs and databases private to each scenario. Rejected: a shared
  `LandingSafetyHarness`, transaction rollback around multi-connection recovery,
  one mutable bare remote, or a common observer checkout. These would erase the
  isolation and independent-observation contracts under test.
- **D-3 — Do not rebuild DB setup.** Existing template cloning and schema caching
  are retained. A seeded class DB template would still need task IDs and absolute
  paths rebound, and could accidentally carry fault/recovery rows. Its maximum
  measured budget is small. A future DB proposal needs separate clone/seed/DI
  timings first; no such change is in the executable slices below.
- **D-4 — Use existing limiter definitions.** Keep `ProcessSpawnLimit=1` and the
  bounded `GitProcessSpawnLimit`. S3 moves the two worker nodes into a separate
  strict class, then makes the remaining class Git-only. Rejected: assigning a
  mixed class to the wider lane, raising global parallelism, allowing Bash/Node
  scenarios onto the Git lane, or overlapping Antiphon.Tests with Pty.Tests.
- **D-5 — Keep setup optimization opt-in until measured.** S5 is a small private
  Git-setup pilot on Persistence only. A prepared **read-only bare object source**
  is an optional later candidate, not a shared mutable remote. Reject copying
  `.git/worktrees`, alternates/hardlink coupling, sharing the task-specific seed,
  and treating diagnostic or assertion fetch/status calls as redundant.
- **D-6 — Bound the script cliff before tuning it.** S1 extracts the existing
  16 scenarios into separately reported argument cases without changing their
  bodies. Keep the original class and categories so the historical cost row and
  native-Linux gate remain identifiable. Rejected: rerunning all 499 historical
  nodes just to profile one unfinished method, increasing its timeout, or
  replacing generated production Bash with a hand-coded success model.
- **D-7 — Clock changes are local and measurement-gated.** S4 may use the existing
  timer-capable `ScaledTimeProvider` at speed five in the one delivery method.
  Use a single clock throughout its two harnesses and notification service.
  Rejected: a frozen clock, global queue setting changes, shortened production
  waits, a fake successful receipt, or scaling the real worker-crash protocol.
- **D-8 — Placement is discovered, not embedded.** On 2026-10-23 both
  `GET /api/runner-defaults` and `GET /api/session-runners` were read. Revision 2
  resolved the default to an eligible Linux runner; the configured temporary
  runner was unavailable/draining. Re-read both at dispatch. Omit `-Runner`;
  use `-Platform Linux` here because the brief forbids desktop execution and the
  host script witnesses require native Linux. Name the resolved runner identity
  in receipts so the required server2 comparisons are comparable. No IP, host
  directory or permanent fleet pin is an execution default in this plan.
- **D-9 — Windows attribution is separate from activation.** No desktop probe,
  discovery, build, test, setting change, restart or live qualification access
  is commissioned here. Defender exclusions are an operator matter; record
  observations for the operator, never add an exclusion or disable protection.
  A master land is not activation or Windows qualification.
- **D-10 — Use a bounded experiment, then decide from evidence.** One matched
  baseline and one candidate execution per selected measurement is the default,
  with build/startup/slot wait reported separately. Report an inconclusive result
  when host interference overwhelms the delta; no automatic rerun campaign.
  Conditional S4/S5 work stops without implementation when its measured phase
  is too small to justify the change. This is an engineering exit criterion,
  not a request for human approval.

These decisions do not add or change a live session waiting for input. The
CARD-1083 release checklist is therefore not applicable. Nothing here introduces
a parking deadline; parking disabled still means no automatic release deadline.

## Recommended order and expected savings

Savings below are **planning hypotheses for the selected Linux work**, not new
measurements and not Windows forecasts. Each includes zero because no matched
candidate has been run. Do not sum overlapping setup and scheduling effects.

| Order | Slice | Expected saving / bound | Risk and reason for order |
|---|---|---|---|
| Prerequisite | Complete narrow attribution using the profile tool; reuse already complete historical evidence only as context | 0 s; prevents tuning the wrong cost | Small read/tooling work; exposes the censored method and cache-key inputs. |
| 1 | S3: isolate checkpoint workers and use the existing Git lane for the other 41 nodes | Historical serial body budget is 157.57 s plus 58.20 s workers. Ideal width-four reduction of the former is 118.18 s; plan for **0–100 s** class-wall reduction after contention/startup. | Medium: scheduling and selection/cost metadata change; the largest quantified opportunity, using an existing limiter. No worker or assertion is simulated. |
| 2 | S1: expose remote resume scenarios | Approximately 0 s of body work; removes the single 16-scenario reporting cliff and allows bounded profiling | Low: move existing bodies, preserve 16 fixtures/32 runs and exact assertions. Necessary before S2's comparison. |
| 3 | S2: class-owned immutable Compose inputs | Let `m` be measured mean time per materialization. C1374 alone removes up to 124 of 128 renders; resume removes up to 60 of 64. At an **illustrative, unmeasured** 0.1–0.3 s/render this is 12–37 s and 6–18 s respectively. Actual saving is bounded by measured setup time and includes rebinding/copy cost. | Medium: path rebinding/cache isolation need an independent real-render oracle. Highest repeated setup count; no protocol steps are removed. |
| 4, only if measured | S4: one coherent queue test clock | **0–20 s** across the 12 delivery cases; maximum saving is 80% of measured clock-governed wait time, not 80% of their whole duration | Low-medium: mixed clocks can make due times incorrect or introduce races; do not proceed if clock waits are under 5 s total. |
| 5, only if measured | S5: fewer private Git setup commands | **0–2 s** across Persistence's 52 nodes is the working estimate for one fewer initial push per fixture; all initial setup was only 10.41 s | Low-medium and small return. Do not add template-copy complexity unless a later same-source profile shows setup becoming material. |
| Separate obligation | Windows file-based attribution, then a future authorized exact-method probe | 0 s claimed until measured | No interference with the current Windows qualification; determines whether host latency still dominates. |

The already-landed Persistence Git lane deserves a width-one/default-width
comparison during the separate CARD-0856 qualification, but it is not a new
CARD-1514 speedup. A theoretical 169.93/4 result is not a measurement: database
clone locking, disk contention and shared-host load remain. Do not widen
RecoveryBoundary or move Publication wholesale merely because their names match.

## Slices and equivalence obligations

### S1 — expose and finish the remote method profile

Files: `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`; the Code owner's
ignored profile adapter/measurement report. Keep `C1374CensusProbes.cs` unchanged.

Replace only `C1008_Recycle_resume_requires_matching_receipt` with four methods
in the **same class**, each retaining `ProcessSpawnLimit` and the native-Linux
prerequisite guard:

| New method | Argument cases | Original scenario mapped to it |
|---|---:|---|
| `C1008_Recycle_resume_after_seed_failure` | 1 | Seed interruption at `recreating`, then successful resume without repeated deletion |
| `C1008_Recycle_resume_after_partial_failure` | 5 | Original standalone `rm-second-failed`, then stop-failed, owned-rm-failed, rm-first-failed, rm-third-failed; preserve the standalone case's journal and removal assertions |
| `C1008_Recycle_resume_rejects_receipt_drift` | 8 | sourceSha, project, context, dryRun, composeDigest, store, generation, stop-receipt |
| `C1008_Recycle_resume_accepts_only_recorded_replacement` | 2 | Original foreign=false/true replacement-generation pair |

Keep the per-case two-run sequence and intermediate persisted JSON reload. Do not
turn the original special `rm-second-failed` path into a weaker generic case.
Move code mechanically before any helper caching. An explicit source ledger maps
each original assertion and setup action to exactly one new case. The class's
historical 499-node calibration becomes 514 semantic executions if no intervening
change occurred: one node replaced by sixteen, no other node changed. Discovery
must establish the actual current count; the historical count is not a hard-coded
new census. Preserve the old `pace` row's historical denominator/provenance; more
reported nodes must not be used to reduce its total estimate.

**Before/after:** the old diagnostic proves only a 457.604 s lower bound. Prefer
one exact old-method attempt through the bounded profile adapter; if it still
hits the same cap, retain that censoring and use the assertion/scenario ledger
for equivalence. Do not retry to completion by extending the cap. Run the four
new methods separately for attribution, retaining counts 1/5/8/2 and all 32
script invocations. Report complete per-method wall/phase totals and the union
of their scenario IDs. Also profile the exact existing C1374 host method once
as S2's 32-vector baseline; never rerun the whole remote class.

**Proof:** code-move equivalence and complete scenario/action ledger, followed by
unchanged outcome assertions for all sixteen cases. No new product assertion is
needed merely to prove a rename. Unexpected assertion red gets Debug attribution;
a cancellation-only old run does not supply a red/green correctness pair.

### S2 — materialize immutable Compose inputs once per selected class

Files: `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`
(`C1008HostFixture` constructor), `RemoteScriptContractTests.cs`,
`C1374CensusProbes.cs`, new `tests/Antiphon.Tests/TestHelpers/C1008ComposeTemplate.cs`,
and new `tests/Antiphon.Tests/Scripts/C1514ComposeTemplateTests.cs`.
Read `scripts/fixtures/c994-production-compose-model.mjs` as the real oracle;
change that helper only if exposing its existing rendering result is necessary.
Do not change production Compose YAML or `scripts/c590-remote.sh`.

Use an explicitly passed, class-owned fixture (the existing TUnit per-class data
source pattern), not a process-global mutable cache. Initialization is lazy:
text-only tests must not acquire Docker/Node prerequisites. One owner renders
current/previous x main/temp models from the checked-out source. It retains
immutable serialized results and owns its temporary root until class teardown.
Only the S1 cases and C1374 host path opt in initially; other fixture consumers
keep their current path until separately measured and commissioned.

Each scenario receives a deep copy and its own inert files/directories. Rebind
only typed fixture-root paths in bind mounts, secret-file entries and other
explicitly inventoried model fields. Unknown root-bearing fields refuse cache
reuse and render normally. No arbitrary JSON/string replacement, no use of a
previous scenario's model object, and no pointer to a disposed template root.
Retain current-versus-previous distinction, project name, temp overlay, volume
ordering and the previous-only github-token bind difference. Key reuse to the
exact Compose/helper bytes, mode/project and renderer identity; changes bypass
reuse. Never make a mutated input look like a cache hit.

`C1374CensusProbes.RunVector` also currently constructs a wrapper fixture on its
host path. Move that construction into the wrapper-only branch only if the
source/effect ledger confirms no host oracle uses it; record that saving
separately. All real host script calls, fresh journals, transport faults,
attempt/sleep logs and mutation/refusal assertions remain.

**Before/after:** same S1 semantic cases and C1374 vectors, same runner, limiter,
source inputs and capture mode. Count actual Node/Compose materializations,
constructor wall, script wall and total test interval union. Include the initial
template render and disposal in the candidate total; do not compare a warmed
candidate with a cold baseline while hiding initialization.

**Proof:** three new exact methods:

- `C1514_Cached_models_equal_fresh_materialization`: compare candidate models at
  two distinct scenario roots with independent real renderer output for all four
  combinations, including bind/secret sources and mount signatures.
- `C1514_Scenario_mutation_cannot_change_template_or_sibling`: mutate scenario A's
  Docker/model/receipt state, prove B and the serialized template unchanged,
  dispose A, then execute B's real witness.
- `C1514_Changed_inputs_bypass_cached_materialization`: change one owned input
  identity at a time and require fresh render or explicit refusal, never stale
  success. Missing tools are errors, not a reason to skip the oracle.

TestDesign supplies method-scoped positive controls for omitted rebinding,
shared mutable nodes and stale reuse. Commit failing witnesses before fixes
where applicable; an assertion failure is required, not missing methods or a
compiler error. Existing protocol witnesses remain the semantic acceptance.

### S3 — split the checkpoint worker cliff without changing its witness

Files: `tests/Antiphon.Tests/Application/AgentTaskLandCheckpointMatrixTests.cs`,
new `AgentTaskLandCheckpointWorkerTests.cs` in that directory,
`tests/Antiphon.Tests/ProcessSpawnLimitTests.cs`,
`tests/Antiphon.Tests/slow-tests-allowlist.txt`,
`scripts/lib/nightly-class-costs.json`, `scripts/test-nightly-tests.ps1`,
new `scripts/fixtures/nightly/c1514-checkpoint-split-listing.txt`, and
new `tests/Antiphon.Tests/Scripts/C1514LandingSplitBudgetTests.cs`.

Move `C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop(bool)` intact
to the new Integration/Slow strict class. Update its child selector to its new
class. Keep the real child, file boundary evidence, two-minute bound, kill/join,
output drains, owner sentinel, parent-owned DB and restart/recovery assertions.
Keep `LandingRemovalCrashWorker`/`TestWorkerModes` semantics unchanged. The old
class retains its four methods/41 expanded cases and becomes Git-limited only
after a full helper/partial/Windows-path process audit establishes Git-only work.
Update both limiter rosters; do not weaken their forbidden-helper checks.

Class movement must not silently fall back to a cheap unmeasured nightly price.
Keep the original 1560-second measured 43-node observation in provenance. Until
Windows per-method evidence is available, conservatively allocate the **entire
1560 seconds to each resulting class**, denominators 41 and 2, labelled `pace`
with explicit split-bound provenance. This intentionally overprices the sum;
it is not two measured timings. Publication 3780/63 and Persistence 1980/52 stay.
Do not use Linux speedup to lower a Windows cost row.

Leave the historical C1404/C1410 listing fixtures intact. The new fixture is an
explicit projection of C1404's original 158 scenarios: 41+2+52+63, with the worker
identities mapped one-to-one. Update the C1404 script case to check the original
evidence values and this projection against the real current table/packer. Its
assertions continue to require all 158 semantic identities once, the unchanged
watchdog and publication method splitting; the justified launch count changes
from seven to eight. Keep the old raw evidence oracle, and explain the changed
class/price expectations rather than silently replacing a historical report.
Run C1410's unchanged calibration contract too. If its fixed estimate band
fails, record the arithmetic before proposing a separate explicit correction.

The new strict `C1514LandingSplitBudgetTests` is a thin established ScriptHarness
wrapper over those two exact offline cases, not a full script-suite invocation.
It gives the combined narrow checkpoint an executable cost/roster witness.

**Before/after:** original 43 cases at width one versus the same 43 mapped cases
at the existing default Git width, with strict workers still one-wide. Clear the
profile tool's historical forced-width-one override on the candidate and record
the actually resolved default; do not change limiter definitions. Record
41-node Git interval union, two-node worker time, combined wall, child maximum
concurrency, DB clone wait and startup separately. Shared fixture/process state
must not leak between the concurrent cases. This is a scheduling saving, not a
reduction in service entries, tests or Git operations per scenario.

**Proof:** mechanical relocation of both worker arguments, equality of the
43-scenario assertion ledger, unchanged real worker outcomes, existing limiter
guards plus the split-budget oracle. TestDesign must include a precise guard
red for placing the worker on the Git lane or losing its mapping; no broad
mutation of the whole class. Ordinary verification contains all 43 cases.

### S4 — accelerate only measured queue waiting

File: `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.Delivery.cs`.
Read `BridgeQueueHarness.cs` and `ScaledTimeProvider.cs`; their defaults stay.

First time the queue flush/reconcile portions of
`C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt` separately
from Git/DB setup. If clock-governed waits total less than five seconds across
its 12 arguments, record no-op/deferred and do not add a new clock configuration.

Otherwise create one `ScaledTimeProvider(5)` before initializing the landing
harness, assign it to `h.Clock`, pass that same provider to BridgeQueueHarness,
and use it in `AgentTaskLandNotificationService`. Replace this method's direct
`DateTime.UtcNow` due-time setup with that provider's UTC time. Preserve retry
attempt counts and clock-domain durations. Do not change the queue's defaults
or make its fake adapter declare delivery without the transcript entry.

**Before/after:** exact 12 arguments with speed one/five, same capture mode and
runner, per-phase timing. **Equivalence:** same busy/idle and before-enqueue/
queue-inserted branches; failure remains RetryPending/unconfirmed; resumed row
identity, retained bytes, digest, one complete caller UserPrompt above the
baseline, confirming sequence and single submitted body all still asserted.
TestDesign must trace any real-clock deadline in the fake adapter before using
five; if it invalidates the comparison, retain the original clock and report it.

This narrow selection intentionally does not execute the unrelated inherited
CARD-1512 photo-path failure. That failure is not waived or repaired by this card.

### S5 — small, opt-in private Git setup reduction

Files: `tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs`,
`LandingSafetyHarness.cs`, new `LandingGitFixtureSetupTests.cs` in TestHelpers,
and `tests/Antiphon.Tests/Application/AgentTaskLandPersistenceFailureTests.cs`.

First use the profile's initial-command intervals to price the two fixture
pushes. If saving one of them is indistinguishable from host variation, record
no-op/deferred. Otherwise add an explicit setup option used by Persistence:
create the private source branch/worktree before publishing either initial ref,
then push target and source together to the **private** bare remote. Keep the
unique task-owned seed commit, independent observer clone and trace clearing at
the same ready boundary. Default setup remains unchanged for other consumers;
there is no global fixture migration hidden in a one-class checkpoint.

**Before/after:** all 52 Persistence cases at Git width one, evidence setting
unchanged, with initial Git command count 10 to 9 per fixture and unchanged
protocol entry counts for each semantic case. Report actual time, including
any extra setup validation. **Equivalence:** new
`C1514_Compact_setup_matches_private_fixture_contract` creates two independent
fixtures and checks the owner bytes, seed/ref values, registered worktree,
fetchable remote source and absence of cross-fixture refs; the existing 52
assertion bodies remain unchanged. A missing initial source push must fail the
independent fetch/ref oracle. TestDesign scopes that control to this method.

Do **not** follow this small edit with unpriced general caching. A read-only
prepared bare seed would require private clones with `--no-hardlinks`, no
alternates, fresh task-specific owner commit and independent remotes; extra
clones/copies can exceed ten initial commands' cost. Commission that only after
measuring a materially larger setup budget. Reusing the current observer or
status/fetch results across mutation boundaries remains rejected.

## Profile method and measurement acceptance

The existing profile tool is `profile.mjs` in the historical evidence root above;
SHA-256 `6d8f8bfe46900b17e5a36f5102e6f65fc434576573ff37738db45394489e36c0`.
Its current interface accepts a class only, hard-codes the old source SHA and
`bin-card1514/`, and overwrites a class-specific result directory. **Do not run it
unchanged against a new candidate.** The old binary output was removed.

TestDesign must specify a small task-owned adapter, kept with ignored evidence,
that accepts an exact filter, actual source SHA, built-output receipt and fresh
result root. It should consume the checkpoint invocation's TRX/diagnostics and
timing rather than run a second hidden test pass. Retain the original tool hash
and the adapter hash/argv in the report. Its multiline diagnostic parser is
`diag-summary.mjs`, SHA-256
`4b0109be8115dc47fabfa024cc56d188f084fefb6e9374c71aad44dd2df2d5f3`;
validate it by reproducing the existing Persistence 52/0/0 and Remote 29-pass
censored census. Analysis of existing files is not another test invocation.

For each slice, record a row with:

`slice; baseline SHA; candidate SHA; tool/adapter hashes; runner identity;
filter and mapped scenarios; cache cold/hit; limiter width; capture mode;
build seconds; slot-wait seconds; bootstrap seconds; driver wall;
test-interval union; summed test seconds; per-method median/p95;
setup/clone/seed or materialization seconds; Git/process counts;
passed/failed/skipped/unfinished; delta; evidence hashes`.

Use monotonic intervals for elapsed time, UTC only for joining observations.
Keep DB-clone wait separate from seed time; if unavailable, label the combined
gap as such. Per-test Git counts under parallelism must join fixture/task and
process identity, not a shared time window. Unassignable counts stay unassigned.
Do not add summed concurrent durations to driver wall or subtract unrelated
build-slot waits from the tests. Report cold schema bootstrap separately.

Use the same capture mode on both sides. Historical full landing capture is
expensive; optional deep capture may diagnose a representative method, but it
cannot be enabled only on the baseline. Profile adapter instrumentation itself
must be identical and bound to each source. No timing threshold assertion is
added to ordinary tests: improvement is a measured report claim.

An accepted slice has equivalent semantic coverage, no introduced failures,
complete expected execution receipts, and a phase/count change consistent with
the claimed saving. If wall time does not improve, report that fact and either
retain only an independently useful structural change (S1) or omit the proposed
optimization. One pair can demonstrate an observed delta, not a statistical SLA.

## Cheap Windows attribution, outside this execution dispatch

1. Ask the release/evidence owner for **copies of completed artifacts**, using
   the exact R/P paths and hashes already recorded in CARD-1404's plan. Do not
   read or probe the live candidate from this Linux task. Parse those copies
   locally with the same multiline parser; recover the 52-node Persistence and
   43-node Checkpoint method distributions, not only aggregate totals. Cost:
   approximately 5–10 minutes of offline analysis, no Windows execution.
2. Join only already-recorded build-slot waits, bootstrap cache state, process
   version and timestamped host/memory samples. Missing synchronized data remains
   unknown. This can allocate startup versus test time; it cannot prove Defender
   causation. Do not compare old Windows timings with a changed Linux source as
   a matched OS experiment.
3. If that leaves the Windows amplification unresolved, the release owner may
   commission a **later**, exact-method Windows probe after the live qualification
   permits it. Use `C448_F04_PushResultAcknowledgementCannotAuthorizeCleanup`
   (two cases), the same executable source as a Linux counterpart, Git width one,
   same capture and the existing build-memory policy. One fresh bounded execution
   per OS; record child launch-to-exit versus Git trace2 runtime, DB clone/seed,
   queue time, available memory and slot wait. No full class/full rc rerun for
   attribution. Dispatch then re-reads runner routes and uses `-Platform Windows`
   only for that OS-required work; it is not a checkpoint authorized here.
4. If launch overhead dominates, give the operator the evidence and request their
   assessment of any already available Defender/ETW data. Do not install tools,
   elevate, change exclusions, kill compilers or modify host settings. If SQL,
   memory or source-version differences dominate, report those instead.

No credible numerical Windows saving is asserted by this plan. Windows gate
headroom and current-candidate acceptance belong to the release owner after
reviewed changes reach the rc candidate. The historical failed/missing-TRX
qualification remains failed/incomplete.

## Verification handoff — separate TestDesign

TestDesign completes the required `## Verification design` in its committed
artifact, using this plan's decisions and source ledger. It must bind the
profile adapter, immutable cache oracle, exact PC methods and ordinary acceptance
scope; it must not expand into a namespace or full-suite run. New test names
below are proposed files/methods, not claims that they already exist.

The table is the **closed selection contract**, one narrow acceptance selection
per implementation slice. Each active optimization owes a baseline and candidate
profile for that same semantic selection. TestDesign materializes those two
source-bound executions explicitly, plus only necessary exact-method red rows,
in the executable manifest; it cannot hide them as unlisted profiling. Baselines
for newly added helper guards do not require nonexistent tests: execute existing
semantic consumers and map the newly introduced guard counts separately. S1's
old single method maps to its four new methods, as enumerated above.

The post-change rows below run once per committed slice. A prior candidate receipt
may serve as the next baseline only when filter, source inputs, capture, widths
and cache state truly match. S4/S5 must be explicitly recorded as activated or
deferred **before** the Code manifest is frozen; conditional rows are not silently
skipped during Code. Keep their source unchanged if deferred.

All rows use the **Linux native test / owned PostgreSQL lane**, resolved to the
eligible server2 runner at dispatch. Script children keep the strict process
limiter; Git-only classes keep the bounded Git limiter. `Serial=true` prevents
another checkpoint/build in this task from contaminating a measured pair; it
does not silently serialize TUnit or claim an idle shared host. Every child is
owned and joined before cleanup. Test projects never co-schedule with Pty.Tests.

Use the checkpoint tool once per committed slice group and wait through exit 75
until it completes. Every build/test driver takes a host build slot; never nest
two slot owners or bypass an exit-4 refusal. Bind `--expected-source-sha` to the
tested full SHA, validate receipts, keep raw output ignored, preserve unedited
CHECKPOINT lines and actual counts in the report, and remove only inventoried
task-owned `bin-*` outputs after their processes exit. Code/Review run
`scripts/check-evidence-diff.ps1` over the complete recorded base..HEAD.

Do not repair CARD-1512 or CARD-1522 in these slices. If an affected selection
fails, reproduce only that method at its base before claiming inherited red;
do not rerun an entire assembly for attribution. A known failure remains red,
not a passing performance result. The selected S4 method avoids CARD-1512's
unrelated photo case; RecoveryBoundary is outside the implementation selection.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-c1514-s3/` | checkpoint-lane-split | `/*/*/(AgentTaskLandCheckpointMatrixTests*)\|(AgentTaskLandCheckpointWorkerTests*)\|(ProcessSpawnLimitTests*)\|(C1514LandingSplitBudgetTests*)/*` | V-3: all 43 mapped witnesses, limiter guards, exact offline budget/roster cases | 65 passed, 0 failed/skipped: 41 matrix, 2 worker, existing 20 limiter cases, 2 script-wrapper cases | 65 | 12 | true | ANTIPHON_TEST_GIT_SPAWN_LIMIT= |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1514-s1/` | remote-resume-scenarios | `/*/*/RemoteScriptContractTests/C1008_Recycle_resume_*` | V-1: scenario/assertion bijection and per-method attribution | exactly 16 passed, 0 failed/skipped; four methods with 1/5/8/2 cases, 32 script runs | 16 | 14 | true | n/a |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1514-s2/` | immutable-compose-inputs | `/*/*/(RemoteScriptContractTests*)\|(C1514ComposeTemplateTests*)/(C1008_Recycle_resume_*)\|(C1374_Host_census_recovers_transient_reads*)\|(C1514_*)` | V-2: fresh-render equality, invalidation, isolation; V-1 consumers unchanged | 20 passed, 0 failed/skipped: 16 resume, 1 host census with 32 vectors, 3 template methods | 20 | 16 | true | n/a |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1514-s4/` | delivery-clock | `/*/*/AgentTaskLandPublicationTests/C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt` | V-4: coherent clock and real complete-prompt delivery | exactly 12 passed, 0 failed/skipped | 12 | 8 | true | n/a |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1514-s5/` | private-git-setup | `/*/*/(AgentTaskLandPersistenceFailureTests*)\|(LandingGitFixtureSetupTests*)/*` | V-5: private setup equivalence plus all 52 persistence witnesses | 53 passed, 0 failed/skipped: 52 persistence, 1 setup equivalence method | 53 | 10 | true | ANTIPHON_TEST_GIT_SPAWN_LIMIT=1 |

CP-2/CP-3 are one logical selection per slice, **not permission for another long
single invocation**. Their executable TestDesign manifests must partition the
already named methods into bounded invocations using one source-bound isolated
build per slice, with `--no-build` reuse. The 8-case drift method is the largest
known unit; if its baseline exceeds one foreground window, divide those exact
eight cases into named method groups before Code, preserving the complete map.
Never raise the 600-second test timeout or the rc watchdog. The same principle
applies to the old S1 baseline: at most the existing bounded attempt, retain
partial evidence on timeout, and continue via the equivalence ledger.

### Cost and settlement

The table budgets 60 minutes for candidate build/acceptance work across all five
slices (42 minutes for S1–S3). Baseline profiles add up to the same order of cost;
exact-method controls add their separately priced runs. These are future Code/
TestDesign costs, not a claim that all implementation fits this Plan's 40-minute
budget. Reuse builds within a source-bound slice; never reuse binaries across a
source change. Implement and publish one meaningful slice at a time, with ordinary
Review before landing and the repository's post-land control workflow where owed.

The Plan owner commits/pushes this document and is the docs landing owner. The
caller can land this plan independently of Code. No source changes, product
deployment or Windows acceptance are included in that docs publication.

--- next stage ---
next: test-design
handoff: Bind the five narrow slice selections to baseline/candidate manifests, finish method-scoped controls and the profile adapter, and activate or defer S4/S5 from their phase budgets. Order S3, S1, S2 first. Preserve every witness and the Windows/no-desktop boundary; no whole Remote class rerun or watchdog increase.
artifact: docs/superpowers/plans/2026-10-23-card-1514-slow-test-speedups-plan.md
