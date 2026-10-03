# CARD-1005: opt-in selected-class census

## Outcome and stage boundary

Add an explicit checklist promise that every test method selected by the plan's
whole-class checkpoint filters belongs to the checklist method roster, and that
the roster contains no extra methods. Emit a distinct finding when those sets
differ. A plan without the promise keeps its existing coverage report bytes.

This is a Plan artifact, not Code admission. The dispatch did not fold TestDesign
into Plan. Next is **test-design**: finish the independent guard/positive-control
audit and freeze the verification details below before commissioning Code.
No implementation, build, test execution or activation occurred in Plan.

## Evidence and ground truth

Inspected assigned source `bb5fa774cd56f85ee6f0b1122c198192427e5ddf`, also the
remote master SHA returned by `git ls-remote origin refs/heads/master` during this
dispatch. The local `origin/master` ref was older; it was not treated as today's
master. CARD-1005's complete description and history contain no completion verdict;
its only revision is this Plan dispatch. CARD-0999's description, history and Done
verdict explicitly leave this work to CARD-1005. The complete board search for
`class-filter census` returns those two cards. The premise is still valid.

| Card assumption | What the inspected code does | Consequence |
|---|---|---|
| CARD-0999 reconciles declared counts | `Coverage/PlanCoverageReader.cs:ReadCounts` recognizes class-cell counts and the literal total sentence. `PlanCoverageAnalyzer.cs:ValidateCounts` counts distinct bound method obligations by requirement/class. | Preserve this independent check and its two existing diagnostic codes. |
| Selected classes can grow without growing frozen promises | The reader skips checkpoint rows. `CoverageCommand.Run` selects files from their class filters but passes only source files to the analyzer. No selected-class versus checklist set comparison exists. | The new check must retain actual class-selection context, not infer it from the selected file list. |
| A class-only count does not promise a complete checklist | `ValidateCounts` skips class-specific counts with zero bound methods; the ID-wide total still checks zero. Parser, Command and Golden tests protect the CARD-0999 repair. | Never enable census from count prose, existing R-1 wording, class names, or the presence of a checklist alone. |
| Checklist format can carry the opt-in | `MergeChecklist` accepts exactly `version` and `items` at the root, rejects unknown/duplicate keys, and rejects simultaneous inline/external checklists. | Add one optional strictly typed key; preserve all other validation. |
| Indexed methods are executable tests | `TestAssertionIndex.Methods` includes every method declaration, including helpers and lifecycle methods. Legacy fixtures intentionally omit `[Test]`. | Census needs a separate test-declaration view. Do not change general obligation/helper resolution. |
| A selected file equals a selected class | One file can contain several classes; scope/explicit files also enter the source set. Current class matching accepts OR and trailing wildcard operands, while the method segment is not used for file selection. | Census must apply the actual project/namespace/class selection and reject unsupported method/category shapes rather than census every loaded method. |
| Frozen reports must remain byte-identical | Schema 1 serializes source hashes, `inputsSha256`, obligations and diagnostics. Internal `CountPromises` does not serialize. | Store census state internally. Compare both tool versions against the **same** final input tree, including identical test source bytes. |
| CARD-1001 is still separate work | Its opened-handle reader, cached role-specific size limits and `PlanCoverageHandleTests` are already in assigned source. | Preserve those reads; include their regression class when changing command selection. Do not reimplement file confinement. |
| New tests fit only the card's named paths | The testing owner requires updating `scripts/lib/checkpoint-usage.ps1:Get-NamespaceCensus` whenever checkpoint test cases are added. It currently says 365. | Include a mechanical count-only scope extension and run its existing compiled-census test. Do not change registry policy or cleanup behavior. |

Required owners read: `AGENTS.md`, `docs/project-context.md`, the relevant stage and
handoff sections of `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`,
`docs/ops-http.md`, and the checkpoint/coverage/census sections of
`docs/testing-and-build.md`.

## Decisions

**D-1 — Explicit checklist flag.** Extend version 1 with optional boolean
`selectedClassCensus`. Only literal JSON `true` enables the promise; absence and
`false` disable it. Support the existing inline fence and external `--checklist`
equally. Reject null, strings, numbers, duplicate keys and conflicting checklist
sources as `CHECKLIST_INVALID`, exit 2. Keep unknown-key rejection. Example:

```json
{"version":1,"selectedClassCensus":true,"items":[]}
```

The empty list in this syntax example is an empty roster, not an exemption: a
selected test then produces a mismatch. Reject an inferred census from prose or a
default-on change because either breaks the explicitly frozen promise set. A new
CLI switch or checklist version adds an unnecessary second activation mechanism.
No unconfirmed product preference is needed for this choice.

**D-2 — Compare method identities, not cardinalities.** Let S be the union of
supported test methods selected by all TUnit whole-class checkpoint rows. Let R
be distinct successfully resolved obligations with `FromChecklist == true` and
`Kind == "method"`, across V/R/PC IDs. The promise is S = R. Prose-only methods,
labels and canaries cannot fill a missing roster entry. Qualification aliases,
repeated requirement references and overlapping filters count a resolved method
once. Use resolved class plus declaration identity (path/span) rather than display
strings. Same-sized sets containing different methods still fail. A bound helper
or a method from a scope-only/unselected class in R is an extra roster member.
Missing/ambiguous checklist bindings retain their existing invalid-input verdict;
do not turn them into a successful census by dropping them from R.

**D-3 — Bounded static selection.** Support the existing whole-class filter form
with assembly `*`, namespace `*` or an exact namespace, literal/OR class operands
and trailing class wildcards, and method segment exactly `*`. Keep project
boundaries, fully qualified class identities, partial declarations and union
semantics. A namespace operand is an exact namespace, not a prefix that silently
includes child namespaces. Loaded files, sibling/nested classes and explicit
`--tests` files do not enlarge S unless selected themselves.

S includes directly declared methods bearing syntactically recognized TUnit
`Test`/`TestAttribute`, including qualified spellings. Helpers and `[Before]`/
`[After]` methods are excluded. An Arguments/data-source/Repeat expansion changes
result counts, not the single method identity; the existing declared-result check
continues to report its own unknown expansions. Do not run test discovery or load
an assembly to construct S.

Do not certify unsupported selection as complete. A command/category/method-only
checkpoint row, unsupported filter syntax, ambiguous attribute/type alias, or an
inherited/generated/conditional test shape (including runtime-dependent skip or
explicit-test eligibility) that the syntax index cannot establish
produces `CLASS_CENSUS_UNMAPPED` (exit 1). A base chain resolved from the already
read project candidates and proven to contain no tests may be admitted; inherited
test execution is outside this increment. Do not add those supporting declarations
to the legacy serialized source list. An opt-in pure-analyzer call lacking trusted
checkpoint selection context likewise yields unmapped, never a census over all
supplied files. Existing invalid manifest/path/source errors remain exit 2.
Absence/false bypasses these additional checks entirely.

**D-4 — Distinct, actionable findings.** Emit `CLASS_CENSUS_MISMATCH`, exit 1, for
each distinct member of S minus R or R minus S. Use `Test` for the fully qualified
method, `TestPath`/`TestLine` for its declaration, and stable detail values
`selected method absent from checklist` or `checklist method not selected`.
For S minus R, identify the earliest selecting CP row and its original plan
line/column; for R minus S use the checklist obligation's requirement/location.
Deduplicate across overlapping filters. An unmapped diagnostic identifies the
unsupported CP/shape and reason. Keep existing deterministic sorting and JSON/text
rendering; a matching census adds no obligations or output records. These findings
affect `result`/exit through existing diagnostic handling; they do not redefine
the summary's missing-assertion count or PC reachability.

**D-5 — Preserve old behavior structurally.** Add only internal census settings/
selection state to `PlanCoverageReport` or an internal context type. Do not add a
serialized property, bump `schemaVersion`, alter legacy source selection/order or
change the meaning of `CountPromises`. Retain a default-off path through the same
existing obligation analysis. Keep both the CARD-0891 plan and the c999 frozen
fixture untouched. Put new tests in `PlanCoverageCensusTests.cs` so this card need
not extend the old frozen input classes.

**D-6 — Placement and scope.** `GET /api/runner-defaults` and
`GET /api/session-runners` were read during Plan. Defaults revision 2 resolves to
an available, dispatch-eligible Linux runner; an eligible Windows runner is also
advertised. No fleet address or fixed runner belongs in this plan. Authoring is
platform-neutral. The ordinary verification rows below use the Linux lane because
the retained Command/Handle regressions include native FIFO/hardlink checks and
the byte comparison uses POSIX tools. Omit `-Runner`; only the execution stage
needs `-Platform Linux` for these rows. Re-read availability when dispatching.

**D-7 — Activation is local tooling.** Land the tool, tests and owner documentation
together after clean ordinary Review. Rebuild/update the checkout's checkpoint
tool before authors use the new flag. Old binaries reject the new key, so upgrade
the reader before opting any live plan in. There is no database, server, runner,
AppHost or UI rollout. Do not retrofit historical plans. SourceLanding Mutation
remains a separate post-land obligation, not a claim made by Plan or Code.

## Implementation slices

| Slice | Files | Change and named verification |
|---|---|---|
| S1 — contract and census | `tools/Antiphon.Checkpoints/Coverage/PlanCoverageReader.cs`, `PlanCoverageReport.cs`, `CoverageCommand.cs`, `PlanCoverageAnalyzer.cs`, `TestAssertionIndex.cs`; optional new internal `Coverage/ClassCensusSelection.cs`; new `tests/Antiphon.Tests/Checkpoints/PlanCoverageCensusTests.cs`; count only in `scripts/lib/checkpoint-usage.ps1` | Parse D-1, carry imported checkpoint selection/class identity into analysis, establish the bounded test-method set and implement D-2..D-5. Add the 12 proposed single-result tests below. Increase the existing 365 census by the actual new case count (377 if this roster stays unchanged). Use `CheckpointTestBase` for scratch-root custody and the existing inert-source/`CoverageCommand` seams. Commit and push the coherent slice before long verification. |
| S2 — documentation and acceptance | `docs/testing-and-build.md`; this plan only for finalized verification/evidence | Document exact key, set semantics, supported filters, unmapped boundaries, both new finding codes, exits, compatibility, and reader-before-plan activation. TestDesign freezes method names/PCs/counts before Code. Commit and push; run the closed ordinary checkpoint group after S1-S2. |

Keep `Manifest/PlanTableImporter.cs`, the checkpoint scheduler/runtime, CLI options,
`ConfinedFileReader.cs`, shared test helpers, frozen plans/fixtures, and production
services unchanged. The one out-of-card-path edit is the owner-mandated census
integer, not a change to the usage script's behavior. No new packages are needed.
Do not refactor unrelated assertion or path handling while plumbing census context.

## Verification design

### Inspection

Read the bodies of all six current `PlanCoverage*Tests` classes, the inert
`PlanCoverageFixture`, `CheckpointTestBase`/`CheckpointTestScope`, and the compiled
namespace-census guard. Inspected the importer and import/manifest fixture seams
and counted their test attributes. Current coverage results by source roster are
Parser 8, Assertion 5, PC 4, Golden 4, Command 10 and Handle 16: **47** total.
Handle's argument rows, not its six methods, account for 16. Import adds 20,
Manifest adds 6, and the compiled namespace-census check adds 1.
All figures here are source/design counts, not execution receipts.

No asynchronous product delivery path is added. The observable receipt is the
coverage command's JSON/text report and exit status. Fake-driver and filesystem
inventory assertions must establish that the opted-in command still only reads.

### Proposed behavior and regression roster

All 12 new methods below live in `PlanCoverageCensusTests`, carry Category Unit,
and have one TUnit result each. Fixture variants may be local loops but are not
extra executed results. TestDesign must split a method if separate safety guards
cannot each get a decisive reachable assertion.

| ID | Proposed method | Required observation |
|---|---|---|
| V-1 | `PlanCoverageCensusTests.checklist_flag_accepts_true_false_and_absence` | Only true enables census; inline/external forms agree. Unrelated count prose never activates it. |
| V-2 | `PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input` | Wrong JSON types, duplicate flag, unknown keys and inline/external conflicts remain invalid, exit 2. |
| V-3 | `PlanCoverageCensusTests.census_matches_exact_checklist_roster` | Two selected Test declarations, two resolved checklist method items: clean, no new output records. Empty roster with selected tests fails. |
| V-4 | `PlanCoverageCensusTests.census_reports_new_or_omitted_selected_method` | Add a selected Test without an item, or remove an item with no declared numeric count: exact missing-member finding and exit 1. Updating its item restores clean. |
| V-5 | `PlanCoverageCensusTests.census_rejects_unselected_or_non_test_roster_methods` | A bound scope-only method or helper in the checklist is an extra roster member. An unresolved/ambiguous binding remains invalid. |
| V-6 | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts` | Same-size wrong roster reports both differences; short/FQN aliases resolve once. A prose method or label item cannot substitute for a checklist method item. |
| V-7 | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project` | Exact/OR/trailing-wildcard filters, two namespaces (including a child namespace), sibling/nested classes and another project produce only the intended set. An unselected explicit file does not enlarge it. |
| V-8 | `PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations` | Repeated CP class selection and repeated checklist references deduplicate; tests in both partial declarations are required. |
| V-9 | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once` | Test/TestAttribute/qualified forms count; lifecycle/helpers do not; Arguments and data-source expansions remain one method each. |
| V-10 | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery` | Unsupported row/filter/inheritance/alias shapes and absent analyzer context give named unmapped findings instead of clean; no executable discovery. Include a resolvable test-free base chain. |
| R-1 | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts` | Absent/false never introduce census findings; original count mismatch and zero-binding/class-only behavior persist. Compare complete expected report shape, not just a clean exit. |
| R-2 | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only` | Public Program/CoverageCommand path gives stable JSON/text diagnostics, coordinates and exits for a mismatch; fake driver calls and newly written run-state files stay zero. |

R-3 is the mandatory byte comparison below. R-4 is all 47 existing coverage cases,
26 import/manifest cases and the independent compiled namespace census. R-5 is
the normal Unit lane, including test classification and the namespace-counter
change. Existing frozen tests must still bind all 72 obligations. No broad full
assembly run is needed for this isolated tool change.

### Legacy byte comparison and build preparation

Freeze the pre-change master tool at the observed baseline SHA above in a detached,
task-owned checkout `.antiphon/c1005-baseline`; do not reset or rebase this branch.
Build its tool to `bin-c1005-master/` and the final branch tool to
`bin-c1005-tool/`, through `scripts/build-slot.ps1`. These two tool-only builds are
explicit bootstrap exceptions to the table; record both source SHAs and outcomes.
They precede the checkpoint run so the command row has no implicit build dependency.
Run no build/test driver outside its gate. Do not edit source during a run.

For each of these unchanged plan paths, run **both binaries against the same final
task root**, with identical arguments and input files:

1. `docs/superpowers/plans/2026-10-01-card-0891-plan-to-test-coverage-check-plan.md`
2. `tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c999-frozen-plan.md.txt`

Require exit 0 for each invocation, byte equality of each raw JSON pair and equal
SHA-256 hashes. Preserve all four JSON files and hashes. Do not strip digests,
coordinates, source entries or fields, and do not rebaseline the frozen fixtures.
Comparing old-tool/old-source with new-tool/new-source would conflate source hash
changes with analyzer behavior; it is not this acceptance test. The optional flag
does not appear in either input.

### TestDesign completion obligations

Complete a guard inventory with one method-scoped PC for each independently
bypassable guard: opt-in gating, strict flag validation, true filter/project/class
selection, test-versus-helper classification, checklist-origin/kind gating,
resolved identity/deduplication, both set-difference directions, refusal to certify
unknown selection, unchanged legacy serialization and read-only CLI routing.
Inspect the actual assertion sites before freezing exact labels and mutations.
Do not invent a runtime test-execution certificate from syntax-only coverage.

Add exact positive-control targets, assertion labels, reachable setup and numeric
Mutation cost to this section; reconcile its method roster and namespace count.
Supply the active coverage checklist and exact explicit test-file arguments for
linting this plan: its Unit/category and comparison/command rows need that existing
fallback. Leave this plan's own census flag absent; the new opt-in is exercised by
the small inert whole-class fixture plans, not by pretending a Unit-lane filter
is a statically enumerable class filter.
The command comparison below is Linux-specific by design. PC runs happen after
land against the SourceLanding snapshot and remain pending at ordinary Code/Review.
This Plan dispatch performs no PCs and does not claim the audit is complete.

### Cost

Initial ordinary execution estimate: CP-1 8 + CP-2 12 + CP-3 2 = **22 minutes**,
plus two gated tool bootstraps at 3 minutes each = **28 minutes**, excluding slot
wait and implementation. Allow approximately 60-90 minutes implementation plus
the ordinary floor for a Code dispatch. These are estimates, not measurements.
Unit Min 1992 is the existing CARD-1001 planning floor, not a fresh census; CP-1
separately requires every new method. TestDesign must reconcile the current Unit
floor and expected names before Code. Full assembly verification buys no relevant
additional selection/roster coverage; the exact coverage group and Unit lane do.

### Checkpoints

Proposed closed ordinary group, to be finalized by TestDesign. All rows use the
**Linux lane**, reflected in their Group names; no host is pinned. CP-1 expects
86 = 47 existing coverage + 12 new + 20 import + 6 manifest + 1 census results.
The counter check is an existing test, not a thirteenth new test. CP-2 reuses the
same committed S1-S2 build. Preserve every actual class/method result and clean
source receipt, not just the floor. The compatibility command uses the two
bootstrap outputs already prepared above; raw redirection preserves JSON bytes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1005-final/` | linux-unit-coverage | `/*/*/(PlanCoverageCensusTests*)\|(PlanCoverageParserTests*)\|(PlanCoverageAssertionTests*)\|(PlanCoveragePcTests*)\|(PlanCoverageGoldenTests*)\|(PlanCoverageCommandTests*)\|(PlanCoverageHandleTests*)\|(CheckpointImportTests*)\|(CheckpointManifestTests*)\|(CheckpointNamespaceCensusUsageTests*)/*` | V-1..V-10, R-1, R-2, R-4 | all 86 listed results, 0 failed/skipped | 86 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1005-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | CP-1 | linux-unit | `/*/*/*/*[Category=Unit]` | R-5 | >= 1992 executed, 0 failed/skipped | 1992 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c1005-disabled;TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-3 | S1-S2 | n/a | linux-cli-legacy-json | `sh -eu -c 'mkdir -p .antiphon/c1005-compat; for p in docs/superpowers/plans/2026-10-01-card-0891-plan-to-test-coverage-check-plan.md tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c999-frozen-plan.md.txt; do n=$(basename "$p"); dotnet .antiphon/c1005-baseline/tools/Antiphon.Checkpoints/bin-c1005-master/net9.0/Antiphon.Checkpoints.dll coverage --repo-root "$PWD" --plan "$p" --format json > ".antiphon/c1005-compat/$n.master.json"; dotnet tools/Antiphon.Checkpoints/bin-c1005-tool/net9.0/Antiphon.Checkpoints.dll coverage --repo-root "$PWD" --plan "$p" --format json > ".antiphon/c1005-compat/$n.branch.json"; cmp ".antiphon/c1005-compat/$n.master.json" ".antiphon/c1005-compat/$n.branch.json"; sha256sum ".antiphon/c1005-compat/$n.master.json" ".antiphon/c1005-compat/$n.branch.json"; done'` | R-3 | four exit-0 coverage invocations; two byte-identical JSON pairs and two matching hash pairs | n/a | 2 | true | n/a |

## Execution and activation order

1. TestDesign completes and commits this verification section, including the guard
   audit, exact assertion targets, final count and costs. Its completion admits Code.
2. Code captures the master bootstrap, implements and pushes S1 then S2 on its
   assigned branch, and builds the final tool bootstrap. Report baseline/new-tool
   source identities. Run coverage on the finalized plan as required by the testing
   owner; dispose of findings explicitly before final ordinary checkpoints.
3. Run the checkpoint tool once for the committed S1-S2 group with
   `run --plan docs/superpowers/plans/2026-10-03-card-1005-opt-in-class-census-plan.md
   --after S1-S2 --expected-source-sha <full-code-sha>`. Bootstrap through the gate;
   the checkpoint executor owns subsequent row leases. Wait until completion,
   continuing the same run after exit 75. Validate receipts and report CP-n counts,
   skips, failures, and the two JSON hash pairs. A slot timeout is not a test pass.
4. Ordinary Review reruns the finalized selection and comparison at its exact
   reviewed SHA. Verify a clean source certificate and unchanged frozen inputs.
   No source edit or claim of deployment is part of Review.
5. Land tool/tests/docs, advance the consuming checkout and rebuild its tool. Only
   then may authors add `selectedClassCensus: true` to new or deliberately revised
   checklists. Do not restart shared services for this tooling-only change.
6. Keep the separate post-land Mutation obligation and its exact source binding.
   Clean only producer-owned output directories from both builds/worktrees after
   all commands have finished, retaining comparison reports and checkpoint receipts.

## Plan-stage validation

Validation for this dispatch is document/source inspection and checkpoint-table
structure, not runtime verification. New production/test/script edits: zero.
Builds executed: zero. TUnit results executed: zero. Legacy JSON hashes have not
been measured in Plan; CP-3 commissions that evidence for Code and Review.
The lightweight document check found three checkpoint rows with 11 cells each,
positive minute estimates, all 12 proposed methods, and both compatibility input
files. `git diff --check` passed. This was not the compiled checkpoint importer.
