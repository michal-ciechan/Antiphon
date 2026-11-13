# CARD-1899 Linux reply-spill fixture repair

Landing owner / original Code task: `92916ddf`. Base:
`5c7e34c59e03829db975847dd12efddcfe591459` (current master at dispatch).
Scope: `OutputDistillationDeliveryTests`, its instance fixture and generated impact index.
No product settings, native backend policy, timeout or delivery assertions change.
CARD-1900's separate report-custody defects are excluded from repair.

## Design and acceptance

The Debug attribution at `8597a0030` on `feat/card-task-37cb8e72`,
`docs/investigations/2026-11-13-reply-inline-linux-fixture-attribution.md`, supplies
the existing red: 78 cases, 59 passed / 19 failed on master
`c1fd1b7bc1eb905d2285b4258879955dda4fe96d`, TRX
`2d3ac682-42d3-49b2-aa67-82cc23b10b4a`. Its standalone batching witness
is 0/1, TRX `7a3b677b-1048-4b5b-a133-c5f0fa2e26bc`.
Reuse these inherited reds; do not spend another full baseline build.

Copy the fixture's selected reply/brief/write envelope into the three UnixPty
option properties before constructing the real `PtyDeliveryProfile`. Assert all
three effective limits. Windows continues to use `modern` / `inbox` selectors
with unchanged effective values. Linux still resolves UnixPty but receives the
explicit simulated envelope. Default Unix limits remain 14400/43200/86400.

Wrap fixture creation in failure cleanup and make disposal tolerate partial
initialization. Nested finally blocks release the provider, isolated database and
owned workspace even if another release fails. A durable root is required by
`AgentReportStore`; retaining the owned `.antiphon/test-output/card-0419/<id>`
root with guaranteed finally removal preserves that production invariant.

| ID | Required result |
|---|---|
| V-1 | Original 78 delivery cases all pass; 19 inherited ceiling reds repaired, intact raw reports, spill and batching assertions retained. |
| V-2 | Standalone same-root batching method passes through real settlement/distillation/delivery. |
| V-3 | New setup-failure cases before and after provider construction preserve the original exception, delete the repository and cloned database, and dispose instantiated services. |
| R-1 | Six-class classification/registry/native-policy/impact guard union passes. |
| R-2 | Mechanical contract, task platform guidance and layering guards pass. |
| R-3 | Evidence diff over full base..HEAD and conflict-free merge-tree against fetched master. |
| R-4 | Diff-based impacted-class selector has no unaccounted selected classes; derived index regenerated and committed separately if changed. |

Manual acceptance: inspect Linux TRX identities and all named methods; inspect
effective Windows branch without changing its assertions; retain caller-supplied
fact that rc Windows run 97150a0b did not report these failures. Native Windows
execution is owed by the orchestrator, not claimed here. No restart required.

## Positive controls (pending SourceLanding Mutation)

PC-1: omit conservative Unix reply option mapping; run exactly
`OutputDistillationDeliveryTests.Report_boundaries` with its argument variants.
PC-2: omit conservative Unix single-write mapping; run exactly
`OutputDistillationDeliveryTests.Same_root_applied_notes_batch_into_one_intact_spill_file`.
PC-3a/PC-3b: bypass failure cleanup before/after provider build; run exactly
`OutputDistillationDeliveryTests.Failed_fixture_setup_releases_workspace_database_and_provider`.
PC-4: omit Unix brief option mapping; the fixture's effective-brief assertion
must fail in the same batching method. All variants remain pending; Mutation
owns deliberate mutants, red/restore/green and discovery of missing controls.

## Performance budget

Debug 37cb8e72 measured full class 250.52s outer wall on Linux master,
singleton 102.49s, fallback 137.44s, admission 124.20s; cold builds 251-411s.
These are small, contended historical samples, not p95. Provisional budget:
delivery 5m, singleton 2m, guard union 5m, additional guards 2m; cold build
7m, total execution/build target 21m excluding admission. No test deadline
changes. Importer's existing 15m row floor / 3x estimate is retained as a tooling
exception under CARD-1404, not a newly measured p95 or widened test timeout.
Stop and report any selection over 30m for Debug, retaining its timing.

Shared `TestDbFixture` owns database template/startup once per process. Each
delivery case uses a cloned database and disposable owned Git workspace; no new
container/host/bootstrap setup. TRX records per-case duration and membership;
opt-in TestTiming journals retain available fixture/process phases. Uninstrumented
HTTP/IO/database phase boundaries and remainder stay unknown, never zero.
Admission uses the finite standard broker budget, reported separately as slot
and waited. No full assembly or Unit lane: invariant is fixture envelope parity,
resource cleanup and the explicitly named guards. No unbounded shared impact.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1899/` | delivery-fixture | `/*/*/OutputDistillationDeliveryTests/*` | V-1,V-3 | 80 passed, zero failed/skipped | 80 | 5 |
| CP-2 | S1-S2 | CP-1 | isolated-batching | `/*/*/OutputDistillationDeliveryTests/Same_root_applied_notes_batch_into_one_intact_spill_file` | V-2 | 1 passed, zero failed/skipped | 1 | 2 |
| CP-3 | S1-S2 | CP-1 | classification-impact-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)/*` | R-1,R-4 | all six classes, zero failed/skipped | 6 | 5 |
| CP-4 | S1-S2 | CP-1 | final-contract-guards | `/*/*/(MechanicalPcContractTests)\|(TaskPlatformGuidanceTests)\|(C1729LayeringGuardTests)/*` | R-2 | all three classes, zero failed/skipped | 3 | 2 |

### Cost

Ordinary execution floor 14 estimated minutes plus cold build and authoring;
single isolated output is reused across the closed rows. Driver build first;
checkpoint tool bootstrap is an explicitly listed prerequisite, each under its
own build slot. Index regeneration is a driver invocation under a slot, not an
extra test selection. Red rows alone rerun after diagnosis/repair. No repetitions
are required after green. Evidence stays ignored under `.antiphon/card1899/`
and the checkpoint run's immutable directory; optional Markdown report is tracked.
