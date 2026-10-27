# Test performance standards

These are standing engineering instructions from the operator (2026-10-23), applying to
Plan, TestDesign, Code, Review and release qualification. MUST is mandatory; SHOULD requires
a recorded reason when omitted. Preserve historical timings and failed receipts; they do not
authorize repeating expensive work or crediting missing verification.

## Rules

1. **R1 - Debug runs over 30 minutes.** Any test selection, batch, checkpoint row or complete
   run expected or observed to exceed 30 minutes MUST trigger a Debug task with timing evidence,
   exact selection, source SHA, host/load and run identity. A retry alone is insufficient;
   split or repair based on the findings. A healthy long run is not automatically a hung process.
   Rationale: investigate expensive work before repeatedly paying its cost.
   Enforcement: Plan performance budget, orchestrator standing rule 10 and Review's Debug check.

2. **R2 - Separate slow components.** Design SHOULD consider extracting slow sections into
   their own projects and test categories, with explicit dependency direction, so changes select
   those sections and their downstream dependents. CARD-1545 owns scoped rc qualification;
   CARD-1546 owns extracting the git operations handler into its own csproj.
   Rationale: independent components make selective testing defensible.
   Enforcement: Plan architecture/selection decision and Review checklist; automated scoped rc
   qualification is not yet enforced: CARD-1545; git project extraction: CARD-1546.

3. **R3 - Share expensive setup.** AppHost, web host, database container, migrations and seed
   data MUST be set up ONCE PER RUN (or once per assembly/session), never per test or class.
   Per-test isolation MUST use snapshots, template/cloned databases or transactions rather than
   replaying shared setup. Tests whose subject is startup, migrations or teardown MUST record
   why a fresh instance is necessary and measure that work explicitly.
   Rationale: preserve isolation without repeatedly paying infrastructure startup costs.
   Enforcement: Plan fixture lifetime/isolation design and Review checklist; remaining fixture
   optimization is not yet enforced: CARD-1514.

4. **R4 - Record where time goes.** Every test run MUST retain per-test durations, exact
   batch/group membership and phase timings for container/Docker start, readiness waits, data
   preparation, web-host start, migrations, database snapshot/restore/clone, test body and teardown.
   External-call timing MUST distinguish database, IO/filesystem, HTTP, CLI/process and other
   calls. Record unavailable phases as unknown and unattributed time as remainder, never zero;
   report wall time separately from nested/parallel span totals. Join records by run/test/group,
   source SHA, platform, host and category; separate build/slot waits from execution (CARD-1531).
   The live application MUST expose comparable startup phases through ActivitySource/OpenTelemetry
   spans and structured logs, without secrets or payload contents. Before the NEXT full rc run,
   instrumentation and the planned/executed group manifest MUST be ready to capture per-test time,
   membership and phase attribution; retain the resulting evidence before crediting that run.
   Rationale: comparable lab and live measurements locate the cost instead of guessing.
   Enforcement: Plan recording design and Review evidence checklist; complete instrumentation is
   not yet enforced: CARD-1514; duration analysis: CARD-1314. The requested plan entry point is
   `docs/superpowers/plans/2026-10-23-test-timing-instrumentation-plan.md`; CARD-1514 names its
   implementation plan `docs/superpowers/plans/2026-10-23-card-1514-test-timing-instrumentation-plan.md`.
   Link these when landed; neither exists at this document's initial source revision.
   The next Windows nightly or rc qualification emits, from the run directory alone:
   `summary.json` `timingRoot`; `timingChunks` (queue wait, build, discovery, run wall,
   per-test p50/p95/max, slowest 20, `lastPhase`, `lastPhaseUtc`, `why`); and `suiteTimeline`
   (per-suite start and end UTC). Per-test JSONL lives under that timing root. A sidecar
   `<suite>-<chunk>.timing.json` sits beside each TRX, including chunks that time out or
   produce no TRX. Fixture and process-wait seconds stay null when the JSONL has no matching
   complete record. The qualification wrapper does not take a build slot and does not distill
   output, so those two fields stay null (`buildSlot` `not-used`). See
   [testing-and-build.md](testing-and-build.md#opt-in-per-test-phase-timing-card-1514).

5. **R5 - Measure timeout budgets.** Per-test and per-row timeouts MUST be about 2x measured
   p95 duration, not generous by default: a 40-second test gets at most approximately two minutes.
   A longer timeout MUST carry a recorded justification, measurement reference and tracking card.
   Checkpoint and test-execution-policy cost rows MUST derive from measured durations (CARD-1404).
   A small or missing sample MUST be labelled provisional, with a bounded collection plan and a
   card; it is not a measured p95. Queue admission and execution budgets MUST be reported separately
   (CARD-1531), with finite bounds for each.
   Rationale: detect regressions promptly without hiding them behind inflated estimates.
   Enforcement: Plan performance budget and Review checklist; automatic measured timeout
   derivation is not yet enforced: CARD-1404. Existing importer defaults are implementation facts,
   not compliant budgets; record exceptions until changed through the ordinary pipeline.

6. **R6 - Bound external calls.** Tests and live startup MUST measure external database, IO,
   HTTP, CLI/process and other calls individually, with cancellation/deadlines appropriate to
   each measured operation, including readiness polling, process exit/output drain and teardown.
   SHOULD use correlated spans and structured duration/outcome fields shared with R4.
   Rationale: a whole-test timeout alone cannot identify or contain a slow dependency.
   Enforcement: Plan call-boundary inventory and Review checklist; broad timing coverage is
   not yet enforced: CARD-1514.

7. **R7 - Categorise the monorepo.** Every project and test MUST belong to an app category;
   run manifests MUST map every test to its batch/category and capture the direct ProjectReference
   graph plus transitive downstream dependents. Categories MUST distinguish messaging, server,
   Claude runner, Codex runner, shared pty runner and git operations. A change to messaging selects
   messaging plus affected dependent batches. Unknown ownership/graph inputs MUST fall back to
   Full; scoped rc gate credit requires the separate CARD-1545 qualification policy, not this doc.
   Rationale: project boundaries and downstream closure preserve coverage while reducing work.
   Enforcement: Plan category/dependency inventory and Review selection audit; complete app batches
   and graph-based rc selection are not yet enforced: CARD-1545 (ordinary selection: CARD-1313).

8. **R8 - Design and review the budget.** Every Plan MUST contain a `## Performance budget`
   stating expected test/row/batch/run duration, phase budgets, fixture lifetime/isolation,
   recording location, measured p95/sample provenance and the timeouts it sets under R5.
   Plans without tests MUST explicitly say so. TestDesign MUST refine this budget into the
   checkpoint costs. Review MUST check new/changed tests for per-test/per-class setup that should
   be shared, timeouts beyond about 2x measured p95 without justification/card, missing phase or
   external-call recording, category/dependency gaps and any over-30-minute Debug obligation.
   Rationale: make performance a design and acceptance concern on every change.
   Enforcement: [Plan format and Review checklist](testing-and-build.md#performance-standards)
   and source Plan/Review bundle pointers; this is a human review obligation, not a new guard.

9. **R9 - Act on routine speedups.** Routine reversible actions such as stopping an owned stuck
   run through its supported stop command (and joining cleanup), dispatching Debug for a batch
   over 30 minutes or rerunning a preflight MUST NOT wait for operator permission. The orchestrator
   MAY stop an abandoned or uncreditable qualification after preserving evidence and proving
   process ownership. This does not authorize killing another session or bypassing custody.
   Destructive/one-way actions, secret handling and publish/promote/tag actions still require
   their existing explicit authorization; task-branch commit/push authorization remains valid.
   Rationale: remove routine delays while preserving ownership and publication boundaries.
   Enforcement: orchestrator standing rule 10 and existing operational/custody gates.

## Existing work and measurement provenance

The Antiphon board was checked on 2026-10-23; reuse these cards rather than filing duplicates:
CARD-0856 (270-minute Windows full run; integration DB/git dominate, contended historical sample),
CARD-1314 (duration buckets), CARD-1313 (change-aware ordinary selection and timing), CARD-1404
(measured cost packing), CARD-1514 (slow fixtures and next-run timing instrumentation), CARD-1531
(admission versus execution deadlines), CARD-1545 (app batches and downstream scoped rc), and
CARD-1546 (git csproj). Their presence is not evidence that their implementation has landed.

CARD-1563 supplies an opt-in [timeout audit lint](timeout-audit.md): inventory declarations,
join retained TRX/checkpoint timings, propose rounded 2x observed p95, and fail `--check`
for sufficient comparable history exceeding the recommendation without an exact recorded
exception. Small/unknown-context samples and legacy class totals remain provisional with
a bounded collection obligation. It does not tighten existing watchdogs or qualify release
coverage; apply measured proposals through an ordinary reviewed change.
