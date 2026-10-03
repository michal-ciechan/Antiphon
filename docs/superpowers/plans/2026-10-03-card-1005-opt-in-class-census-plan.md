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

This **TestDesign freeze, task fcb8859a, 2026-10-03**, supersedes the introductory
Plan-only handoff and the proposed counts; D-1..D-7 and S1-S2 are unchanged.
Inspected source: **46e35eded4b6170418e91f6ea04eccbc290cf252**. All new method
names below remain the plan's original twelve single-result methods. Internal
fixture vectors are assertions, not additional TUnit results. No build, repository
test, dynamic discovery, PC or compatibility invocation ran in this dispatch.

### Inspection

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| All six existing PlanCoverage test classes: Parser, Assertion, Pc, Golden, Command, Handle | V-1..V-10, R-1..R-4; strict checklist, binding/count independence, legacy helper handling, frozen 72 obligations, opened-handle confinement |
| Entire PlanCoverageFixture; CheckpointTestBase/CheckpointTestScope; FakeDriver and CheckpointFixtures in CheckpointTestSupport | All twelve new methods; inert snippets do not compile/execute, owned scratch roots, public CLI and no-driver seam |
| Entire CheckpointImportTests and CheckpointManifestTests; their three table fixtures and legacy-min refusal; importer header/row/payload/roster parsing | R-4; exact table import, escaped OR operands, command/category fallback, reused-build constraint |
| CheckpointNamespaceCensusUsageTests method, UsageLibrary process/custody helper, CheckpointRoster.CompiledCases and Get-NamespaceCensus literal/skip list | R-5; 365 existing expanded checkpoint cases and 377 after this card |
| TestLaneCategoryGuardTests; shared TestClassificationGuardTests and TestClassificationMetadata; Antiphon.Tests.csproj compile links | R-5; Unit lane and registry guards, inherited categories, shared linked test counted once |
| CoverageCommand.Run/MatchesClass/ProjectSources; reader Read/MergeChecklist/Keys/ReadCounts; analyzer Analyze/ValidateCounts/ResultCount; all TestAssertionIndex and PlanCoverageReport; Program.RunAsync coverage route | V/R and G-1..G-99; actual file-selection context is missing today, new internal context must not leak into serialized Sources |
| TimeoutTests Windows skip bodies, LandingRemovalPolicyControlTests two Windows-only cleanup methods and conditional link skips, RemoteScriptContractTests shell prerequisite helpers; DispatchHoldLedgerTests.HoldSentences | R-5; six deterministic Linux exclusions, bash/jq/pwsh and link prerequisites; 18 data-source results, not one |
| CARD-0959 freeze format; testing/build manifest, coverage, census and Mutation sections; orchestration TestDesign/handoff and post-land Mutation sections | Method-specific controls, closed verification scope, importer admission and separate estimated costs |

Read-only census used rg for attributes/classes and source counting, excluding
comments/string fixtures and abstract/explicit classes, joining partial types,
and adding the csproj-linked shared classification test. Source figures are not
compiled discovery receipts. The checkpoint namespace has **346 methods**, with
24 Arguments attributes over five methods, hence **365 results**. Focused classes:
Parser 8 + Assertion 5 + Pc 4 + Golden 4 + Command 10 + Handle 16 = **47**;
Import 20 + Manifest 6 + independent census 1 = **27**; plus 12 new = **86**.
Keep scripts/lib/checkpoint-usage.ps1's independent selected literal **365 -> 377**;
never derive it from the selected roster under test. No new test lives in a frozen
input class or changes the existing golden/frozen fixtures.

Unit source accounting: **2,765 methods**, including 252 argument methods with
1,464 rows, gives 3,977; replace the one HoldSentences placeholder with its 18
literal yielded cases: **3,994 selected**. Add 12 new: **4,006 selected**. Six
Windows-only methods below are excluded from the Linux filter: **4,000 executions**.
Checkpoint Unit contribution is 302 before this card; the other 63 checkpoint
results are Integration and do not enter CP-2. No dynamic/generated result count
is inferred for the new feature from this separate authoring census.

Missing setup is commissioned in S1, not claimed present: new
PlanCoverageCensusTests with private fixture writers for a valid project/plan,
inline/external checklist serialization (the old Checklist helper cannot emit the
new key), per-case original row coordinates, independent expected diagnostic
records, and whole filesystem path/type/content-hash snapshots. Use TempDir from
CheckpointTestBase; keep every addition in the new test file. Trusted selection
context is produced by CoverageCommand; direct analyzer tests use the new optional
internal context seam, plus an explicit null-context vector. Do not synthesize S
from the same implementation helper used by the assertion oracle.

### Delivery inventory

There is **no new or changed asynchronous delivery path**. Producer is the
synchronous coverage reader/analyzer; destination is the caller's TextWriter/stdout
and returned exit status. Input identity is plan SHA + checklist SHA + ordered
source digests (InputsSha256). There is no queue, recipient session, persistence
handoff, acknowledgement or recovery worker. An interrupted invocation is rerun
against the same input tree; a partial output is not a valid receipt.

R-2 consumes the complete actual command output and status; R-3 consumes both
complete raw JSON pairs from real built CLI binaries. FakeDriver substitutes only
the checkpoint process driver and proves zero calls through that seam. Filesystem
snapshots prove no created/deleted/changed in-root files; they do not prove absence
of arbitrary out-of-root I/O by all conceivable future code. Inert source containing
throwing constructors and a file-writing test body is never invoked. Syntax-only
fixtures cannot certify actual TUnit discovery, generated tests, runtime eligibility,
or session delivery. No UserPrompt/real-queue test is applicable; none is claimed.
Read-only guards G-78/G-79 have separate controls. Code/Review inspect that the
coverage route still bypasses all execution/discovery services.

### Proves it works now

All methods below are in **PlanCoverageCensusTests**, Category Unit, one [Test]
result each, no Arguments/data source attributes, no Skip, and no spawned child.
V-1..V-10 are CP-1/CP-2 command/reader/analyzer tests over inert C#; R-1/R-2 are
also CP-1/CP-2. The exact methods and per-guard witnesses are frozen below.

| ID | Exact method | Required behavior and expected observation |
|---|---|---|
| V-1 | `PlanCoverageCensusTests.checklist_flag_accepts_true_false_and_absence` | Cross inline/external checklist source with absent/false/true. Select two tests but list one: absent/false keep legacy output semantics; true alone reports the missing method. Count prose and an R-1 total never opt in. |
| V-2 | `PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input` | Both sources reject null/string/number/array/object flag values, duplicate flag, unknown root key, wrong version; inline plus external and two inline fences reject. Assert CHECKLIST_INVALID and exit 2, even when the remaining fixture matches. |
| V-3 | `PlanCoverageCensusTests.census_matches_exact_checklist_roster` | Exact two-method roster and genuinely empty selected class/empty roster are clean; a nonempty selected class with empty items fails. Compare obligations/diagnostics/summary fields: a match adds no census records. |
| V-4 | `PlanCoverageCensusTests.census_reports_new_or_omitted_selected_method` | Append a third selected Test and separately remove a checklist item, with no numeric count promise. Assert exact missing-member tuple and exit 1; restore the item and require clean. |
| V-5 | `PlanCoverageCensusTests.census_rejects_unselected_or_non_test_roster_methods` | Extra scope-only test and helper each produce the extra-member tuple; unresolved and ambiguous checklist bindings remain invalid/exit 2. Set every unrelated selected method up with a valid item. |
| V-6 | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts` | Same-size wrong roster produces both differences. Prose-only methods and label/canary-only items leave the selected method missing. Qualified/short checklist aliases bind once; V/R/PC method items all participate. |
| V-7 | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project` | Literal, fully qualified, OR and trailing-wildcard classes; wildcard/exact namespaces; exact namespace versus child namespace; two projects; same-file sibling/nested classes; scope/explicit-file decoys. Assert selected identity differences, not file counts. |
| V-8 | `PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations` | Overlapping/repeated CP rows and repeated V/R/PC items deduplicate; two files for one partial class contribute both directly declared tests. The earliest original selecting row owns each missing finding. |
| V-9 | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once` | Test, TestAttribute and qualified forms are recognized; helpers and Before/After are excluded. Arguments/data-source/Repeat expand executions but each is one method identity. Dynamic result-count promises still yield their existing diagnostic. |
| V-10 | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery` | Every unsupported selection/shape vector below gives CLASS_CENSUS_UNMAPPED, exit 1, with CP/shape reason; a proven test-free base chain is accepted. Pure analyzer with no trusted selection context is unmapped. Use inert source only. |

### Guards the regression

| ID | Test/command | Decisive assertion |
|---|---|---|
| R-1 | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts` | Absent/false reports retain schema/source/digests/obligations; declared-count deficits/surpluses, class-only zero-binding skip, total zero-binding failure, scoped dedup and dynamic-count unmapped all remain. Include helper method binding, preserving the old general index. |
| R-2 | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only` | Run real Program.RunAsync coverage dispatch with Runtime.Output, FakeDriver and in-root fixtures. Assert exact JSON/text tuples/order/exit/summary and filesystem bytes before/after, zero driver calls. Repeat identical invocations and permute source/row order with expected stable ordering. |
| R-3 | CP-3's two historical plans, old/new binaries against the identical final input tree | Four exit-zero invocations; two raw cmp equalities and two SHA-256 pair equalities; no removed fields or rebased expected JSON |
| R-4 | All 47 existing coverage and 26 Import/Manifest results in CP-1 | Existing assertions intact, including 72 matched frozen obligations and CARD-1001 opened-handle/cached-limit regressions |
| R-5 | CP-1 namespace guard plus CP-2 Linux Unit lane | Literal 377 equals compiled non-explicit namespace cases; registry/lane guards green; 4,000 Unit executions, zero failed/skipped |

### Fixture and boundary freeze

Each vector starts from a small otherwise-valid whole-class plan with V-1 at a
known nonblank original line, a real tests/Sample directory, and two directly
annotated tests A.First/A.Second. Keep expected identities/coordinates literal
and independent of production set construction. Labels below are literal Shouldly
custom messages on the test's own assertions, including all setup assertions.
Where several observations form one invariant, assert one tuple/sequence at its
named label. Gather observations first and put unrelated setup failures at separate
labelled assertions; the PC table names the detecting assertion, not a fixture
exception. Use local cases in stable listed order; Mutation applies the stated
narrow defect so earlier cases remain green. If implementation makes another
label fire first, repair/freeze the actual reachable target before handoff.

- V-1: absence and false cross both supported and unsupported selection; census
  gating cannot be implemented only around comparison while still rejecting a
  legacy category/command plan. True runs through inline and external paths.
- V-2: all five nonboolean JSON kinds, both duplicate values/orders, unknown key,
  version 2, two inline fences, inline plus external with equal AND conflicting
  flags. Compare exact CHECKLIST_INVALID plus Invalid=true/exit 2. No defaulting
  malformed input into absence. Existing item validation is unchanged, exercised
  by Parser regressions; this card does not reinterpret its schema.
- V-3/V-4: both sets empty; S nonempty/R empty; S=R; missing addition and item
  deletion; restored item; no count prose. V-5/V-6 additionally cover R nonempty
  with empty S (selected helper-only class), same-size unequal sets, extra helper,
  scope-only test, unresolved/ambiguous bindings, qualified aliases and repeated IDs.
  V, R and PC checklist items use method kind; label/canary items have real matching
  assertions so they cannot fail earlier for an unrelated missing label.
- V-7: two namespaces One and One.Child with same simple class/method names,
  sibling/nested types in one loaded file, tests/Sample versus tests/Other with
  unique decoy methods, exact/qualified/OR/trailing-wildcard operands and explicit
  source decoys. Select nested class positively in a separate supported FQN vector;
  merely selecting its outer class does not select it. Explicit linked sources
  admitted by the legacy reader remain outside S unless the row selects their class
  in the owning project. Do not change the legacy serialized file selection.
- V-8: select the same partial class by overlapping rows; both part files have a
  unique method, select each by repeated/qualified aliases, remove one item. CP-2
  may precede CP-1 in original text: the earliest text row, not lexical ID, owns
  the missing-member coordinates. Repeat under LF/CRLF without changing positions.
- V-9: bare Test/TestAttribute and TUnit.Core.Test/TUnit.Core.TestAttribute and
  global:: qualification; unannotated helper and Before/After. Two Arguments rows,
  a MethodDataSource, class/parameter data source and Repeat each leave one declared
  identity. A numeric results promise on dynamic expansion remains
  CHECKLIST_COUNT_UNMAPPED; without such prose census still succeeds.
- V-10: command row, category row, exact method segment, assembly name instead of *,
  wildcard namespace and unsupported class operand grammar each get their own
  refusal control. For these fixtures supply --tests and an active checklist, so
  old command fallback cannot hide the new exit-1 finding behind INPUT_INVALID.
  A recognized whole-class row with zero matching class remains legacy invalid
  exit 2. Test-attribute alias/shadowing, ambiguous base alias, inherited test,
  unknown base, generated declaration marker, conditional region, dynamic Skip and
  Explicit are separate shapes. Put each unsafe shape on a selected class; put the
  same unsafe shape in an unselected loaded sibling as a clean control. Fully
  resolved, test-free two-level base chain is the accepted boundary; its supporting
  file stays out of serialized Sources. This is conservative static admission,
  not a promise to understand arbitrary generators or preprocessor configurations.
- R-1: absent/false reports are compared to independent expected JSON trees and
  exact key/value/source/digest expectations for each identical input. Absent and
  false inputs naturally have different hashes from each other; do not compare
  their raw bytes directly. R-3 is the authoritative old/new byte comparison.
- R-2: public Program route uses both JSON/text formats and exit 0/1/2; repeated
  matching/mismatching/unmapped/invalid worlds; observed entire path/type/hash map
  and FakeDriver.Calls. Driver returns harmless success if called so an unexpected
  call reaches the zero-call assertion instead of throwing in setup. The checkpoint
  runtime receives Launch/Wait delegates that fail closed if invoked. Test sources
  and generated expected values remain inert; no assembly loading is needed.

### Guard inventory

Each row is a distinct scoped invariant with its own PC; no shared PC mapping.
Controls PC-85..PC-91 and PC-99 requalify the existing selected-file boundary used by
census plumbing; they do not commission a ConfinedFileReader production edit.
Native open-flag/ABI implementation stays with CARD-1001; the named boundary
contracts and ordinary native Handle regressions are retained here.

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-1 | D-1/D-5; Absent flag bypasses census checks | PC-1 |
| G-2 | D-1/D-5; Explicit false bypasses all census checks | PC-2 |
| G-3 | D-1; Inline true enables census | PC-3 |
| G-4 | D-1; External true enables census | PC-4 |
| G-5 | D-1; Only literal booleans are accepted | PC-5 |
| G-6 | D-1; Duplicate flag keys rejected | PC-6 |
| G-7 | D-1; Unknown root keys rejected | PC-7 |
| G-8 | D-1; Inline/external source conflict rejected | PC-8 |
| G-9 | D-1; Two inline checklists rejected | PC-9 |
| G-10 | D-1; Version remains exactly one | PC-10 |
| G-11 | D-2; Missing selected member detected | PC-11 |
| G-12 | D-2; Extra unselected member detected | PC-12 |
| G-13 | D-2; Empty roster is not exemption | PC-13 |
| G-14 | D-2; Equality uses identities, not cardinalities | PC-14 |
| G-15 | D-2; Roster requires checklist origin | PC-15 |
| G-16 | D-2; Roster requires method kind | PC-16 |
| G-17 | D-2; R requirement items may supply roster methods | PC-17 |
| G-18 | D-2; Qualification aliases resolve to one identity | PC-18 |
| G-19 | D-2; Distinct declarations cannot collapse by short name | PC-19 |
| G-20 | D-2; Repeated checklist references deduplicate | PC-20 |
| G-21 | D-2; Missing checklist binding stays invalid | PC-21 |
| G-22 | D-2; Ambiguous checklist binding stays invalid | PC-22 |
| G-23 | D-3; Selection retains build project boundary | PC-23 |
| G-24 | D-3; Exact namespace excludes child namespace | PC-24 |
| G-25 | D-3; Wildcard namespace admits all matching classes | PC-25 |
| G-26 | D-3; Literal class operand requires exact class | PC-26 |
| G-27 | D-3; Qualified class operand retains enclosing identity | PC-27 |
| G-28 | D-3; OR selects every operand | PC-28 |
| G-29 | D-3; Trailing class wildcard expands | PC-29 |
| G-30 | D-3; Loaded sibling class is not selected | PC-30 |
| G-31 | D-3; Selecting outer class does not select nested class | PC-31 |
| G-32 | D-3; Explicit/scope file does not enlarge S | PC-32 |
| G-33 | D-3; Selection unions CP rows | PC-33 |
| G-34 | D-3; Partial declarations all contribute | PC-34 |
| G-35 | D-3; Bare Test declaration recognized | PC-35 |
| G-36 | D-3; Attribute suffix recognized | PC-36 |
| G-37 | D-3; Qualified TUnit attribute recognized | PC-37 |
| G-38 | D-3; Unannotated helper excluded from S | PC-38 |
| G-39 | D-3; Lifecycle methods excluded from S | PC-39 |
| G-40 | D-3; Arguments results are one method identity | PC-40 |
| G-41 | D-3; Data sources retain declared method identity | PC-41 |
| G-42 | D-3; Repeat retains declared method identity | PC-42 |
| G-43 | D-3; Command row cannot certify complete census | PC-43 |
| G-44 | D-3; Category selection cannot certify census | PC-44 |
| G-45 | D-3; Method-specific filter cannot certify whole class | PC-45 |
| G-46 | D-3; Assembly operand must be wildcard | PC-46 |
| G-47 | D-3; Namespace operand must be wildcard or exact | PC-47 |
| G-48 | D-3; Unsupported class grammar refused | PC-48 |
| G-49 | D-3; Trusted selection context required | PC-49 |
| G-50 | D-3; Ambiguous Test attribute alias is not guessed | PC-50 |
| G-51 | D-3; Ambiguous type/base alias is not guessed | PC-51 |
| G-52 | D-3; Inherited tests cannot certify census | PC-52 |
| G-53 | D-3; Unresolved base is not assumed test-free | PC-53 |
| G-54 | D-3; Generated-file test shape is not certified | PC-54 |
| G-55 | D-3; Conditional test shape is not certified | PC-55 |
| G-56 | D-3; Runtime skip eligibility is not certified | PC-56 |
| G-57 | D-3; Explicit eligibility is not certified | PC-57 |
| G-58 | D-3; Proven empty base chain is admitted | PC-58 |
| G-59 | D-4; Missing-member diagnostic retains declaration identity | PC-59 |
| G-60 | D-4; Missing-member provenance is earliest original CP row | PC-60 |
| G-61 | D-4; Extra-member provenance belongs to checklist obligation | PC-61 |
| G-62 | D-4; Overlapping selection findings deduplicate | PC-62 |
| G-63 | D-4; Diagnostics have deterministic ordering | PC-63 |
| G-64 | D-4; Mismatch determines exit/result | PC-64 |
| G-65 | D-4; Unmapped census determines exit/result | PC-65 |
| G-66 | D-4; Census findings do not become missing assertions | PC-66 |
| G-67 | D-4; Static census does not certify PC reachability | PC-67 |
| G-68 | D-5; Census state remains nonserialized | PC-68 |
| G-69 | D-5; Report schema stays one | PC-69 |
| G-70 | D-5; Legacy source selection/order unchanged | PC-70 |
| G-71 | D-5; Legacy digest algorithm unchanged | PC-71 |
| G-72 | D-4/D-5; Matching census adds no obligations | PC-72 |
| G-73 | D-5; Declared-count validation remains independent | PC-73 |
| G-74 | D-5; Class-only zero-binding count is skipped | PC-74 |
| G-75 | D-5; ID-wide total checks zero bound methods | PC-75 |
| G-76 | D-5; Declared counts remain requirement scoped | PC-76 |
| G-77 | D-5; Unknown dynamic result counts remain unmapped | PC-77 |
| G-78 | D-5; Coverage route never starts execution driver | PC-78 |
| G-79 | D-5; Coverage route writes no run state or input files | PC-79 |
| G-80 | S1 census extension; Namespace counter is independent and current | PC-80 |
| G-81 | D-3/D-5; Invalid manifest remains exit two | PC-81 |
| G-82 | D-3/D-5; Invalid C# remains exit two | PC-82 |
| G-83 | D-3/D-5; General obligation index still resolves helpers | PC-83 |
| G-84 | D-3; Unsafe unselected siblings do not poison selection | PC-84 |
| G-85 | D-5/R-4; Canonical selected path stays under root | PC-85 |
| G-86 | D-5/R-4; Opened file must be regular | PC-86 |
| G-87 | D-5/R-4; Opened file must have exactly one link | PC-87 |
| G-88 | D-5/R-4; Opened-handle final path stays under root | PC-88 |
| G-89 | D-5/R-4; Uncached source/project reads obey byte cap | PC-89 |
| G-90 | D-5/R-4; Cached bytes obey the current role cap | PC-90 |
| G-91 | D-5/R-4; Read uses the verified open handle | PC-91 |
| G-92 | D-2; PC requirement items may supply roster methods | PC-92 |
| G-93 | D-3; Global-qualified TUnit attribute recognized | PC-93 |
| G-94 | D-3; Shadowed Test attribute is not guessed | PC-94 |
| G-95 | D-3; Generated attribute shape is not certified | PC-95 |
| G-96 | D-5; Declared counts remain class scoped | PC-96 |
| G-97 | D-5; Declared counts deduplicate resolved aliases | PC-97 |
| G-98 | D-4/D-5; Matching census adds no diagnostics | PC-98 |
| G-99 | D-5/R-4; Uncached plan/checklist reads obey document cap | PC-99 |

### Positive controls

These are executable specifications **after S1-S2 implements the frozen methods**,
not claims of existing census code. Apply a compiling production/script defect
at the named reader/selection/analyzer/report/routing seam; never weaken the test.
Each last cell names the exact test and the first expected detecting assertion.
The defect is scoped to the stated vector where needed to avoid an earlier,
unrelated assertion. A build error, invalid fixture, zero test count or another
failure label does not qualify. Guard-specific Shouldly witnesses in the new test
file use the literal labels below. Existing test targets retain their read labels.

Mutation runs baseline, break/red, exact restore/green after confirmed land using
only local inherited children in the SourceLanding snapshot and the copied
unchanged run-checkpoint driver in the external evidence root. No commits/pushes
from that snapshot. Every phase uses only its named method filter in the Cost
table, with the stated MinExecuted, fresh results/output paths and source hashes.
Controls sharing a file/method run sequentially; no broad class or Unit substitution.
Restore byte-for-byte and verify a clean snapshot before the next PC. Code runs
ordinary V/R; Review judges this design and ordinary evidence before land.

| PC | Compiling defect / otherwise-valid fixture | Expected red at exact method and assertion |
|---|---|---|
| PC-1 | Break G-1: Default missing selectedClassCensus to true in MergeChecklist (and the no-checklist default). Use missing-roster and unsupported-row absence vectors. | `PlanCoverageCensusTests.checklist_flag_accepts_true_false_and_absence`, label `c1005-pc-1`: Absent inputs have zero CLASS_CENSUS findings and their legacy verdict. |
| PC-2 | Break G-2: Treat presence of the flag, rather than its boolean value, as opt-in. Use false plus otherwise unsupported method row with explicit tests. | `PlanCoverageCensusTests.checklist_flag_accepts_true_false_and_absence`, label `c1005-pc-2`: False has the same legacy diagnostic shape; no new unmapped/mismatch. |
| PC-3 | Break G-3: Drop the enabled value only when the checklist came from an inline fence. | `PlanCoverageCensusTests.checklist_flag_accepts_true_false_and_absence`, label `c1005-pc-3`: Inline true with one missing item emits exactly its mismatch. |
| PC-4 | Break G-4: Drop the enabled value only on the external checklist path. | `PlanCoverageCensusTests.checklist_flag_accepts_true_false_and_absence`, label `c1005-pc-4`: External true with one missing item emits exactly its mismatch. |
| PC-5 | Break G-5: Replace the nonboolean rejection with an enabled=false fallback. Exercise null, string, number, array and object. | `PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input`, label `c1005-pc-5`: Each vector has CHECKLIST_INVALID, Invalid=true, exit 2. |
| PC-6 | Break G-6: Remove only the duplicate-name disjunct in Keys; keep unknown-key rejection. | `PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input`, label `c1005-pc-6`: Duplicate selectedClassCensus, either order/value, remains invalid/2. |
| PC-7 | Break G-7: Remove only the allowed-name disjunct in Keys; keep duplicate rejection. | `PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input`, label `c1005-pc-7`: selectedClassCensusExtra=true remains invalid/2. |
| PC-8 | Break G-8: Delete the inlineJson plus external conflict invalidation; retain both parsers. | `PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input`, label `c1005-pc-8`: Both equal and opposing source flags are invalid/2. |
| PC-9 | Break G-9: Delete the inlineJson-is-not-null invalidation when a second fence closes. | `PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input`, label `c1005-pc-9`: Two individually valid fences are invalid/2. |
| PC-10 | Break G-10: Admit version 2 as if it were 1. | `PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input`, label `c1005-pc-10`: Version 2 with valid true flag and exact roster is invalid/2. |
| PC-11 | Break G-11: Suppress the S-minus-R diagnostic for the newly appended Third method only. | `PlanCoverageCensusTests.census_reports_new_or_omitted_selected_method`, label `c1005-pc-11`: Exactly Third is missing; add its item and the rerun is clean. |
| PC-12 | Break G-12: Skip the R-minus-S enumeration for the scope-only Extra method. | `PlanCoverageCensusTests.census_rejects_unselected_or_non_test_roster_methods`, label `c1005-pc-12`: Extra has checklist method not selected and exit 1. |
| PC-13 | Break G-13: Return early from census when R.Count==0. | `PlanCoverageCensusTests.census_matches_exact_checklist_roster`, label `c1005-pc-13`: Two selected tests with empty items produce exactly two missing members. |
| PC-14 | Break G-14: Return clean when S.Count==R.Count without comparing membership. | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts`, label `c1005-pc-14`: Two equal-sized different sets produce both exact differences. |
| PC-15 | Break G-15: Remove FromChecklist from the R predicate, retaining method-kind check. | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts`, label `c1005-pc-15`: A prose-only Second method remains missing from checklist. |
| PC-16 | Break G-16: Remove Kind==method from R, retaining FromChecklist. | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts`, label `c1005-pc-16`: A matched label/canary-only Second item does not fulfill its method roster entry. |
| PC-17 | Break G-17: Exclude R- IDs from R but keep V- and PC- method items. | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts`, label `c1005-pc-17`: Exact roster with Second supplied by R-1 stays clean. |
| PC-18 | Break G-18: Key R by obligation.Test display string instead of resolved declaration. | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts`, label `c1005-pc-18`: Short plus FQN alias for the same method has one identity and no finding. |
| PC-19 | Break G-19: Key both sets by method.Name only. Use One.A.Same versus Two.A.Same with FQN bindings. | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts`, label `c1005-pc-19`: Wrong namespace roster still yields one difference each direction. |
| PC-20 | Break G-20: Keep repeated resolved R items as a list and report duplicate members as extra instead of distinct identities. | `PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations`, label `c1005-pc-20`: Repeated V/R/PC references retain the same empty finding set. |
| PC-21 | Break G-21: Clear report.Invalid for the zero-resolution checklist branch only. | `PlanCoverageCensusTests.census_rejects_unselected_or_non_test_roster_methods`, label `c1005-pc-21`: Unresolved binding keeps MISSING_METHOD plus Invalid=true and exit 2. |
| PC-22 | Break G-22: Clear report.Invalid for the multiple-resolution checklist branch only. | `PlanCoverageCensusTests.census_rejects_unselected_or_non_test_roster_methods`, label `c1005-pc-22`: Ambiguous binding keeps METHOD_UNMAPPED plus Invalid=true and exit 2. |
| PC-23 | Break G-23: Union candidates from tests/Other into the selected project set, despite no CP selecting Other. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-23`: No missing member for Other.ProjectDecoy. |
| PC-24 | Break G-24: Use StartsWith(namespace+dot) for census namespace matching. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-24`: One.Child.A.ChildOnly is excluded when namespace is One. |
| PC-25 | Break G-25: Treat namespace * as an exact literal namespace. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-25`: Matching A in both One and Two has its required missing-member findings. |
| PC-26 | Break G-26: Use StartsWith for an operand without a trailing star. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-26`: Selecting A excludes AB.Decoy. |
| PC-27 | Break G-27: Discard the qualifier before matching an FQN class operand. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-27`: One.A does not select Two.A; explicitly selected One.Outer.Inner is supported. |
| PC-28 | Break G-28: Evaluate only the first valid class operand. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-28`: A-or-B with B item omitted reports B.Second. |
| PC-29 | Break G-29: Remove the star then compare equality only. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-29`: A* includes AExtra.Second and reports its omitted item. |
| PC-30 | Break G-30: Take all top-level classes from each matched file into S. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-30`: Same-file Other.Decoy does not enlarge S. |
| PC-31 | Break G-31: Include all descendant method declarations under the selected outer syntax node. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-31`: Outer.Inner.Decoy is absent from S until explicitly selected. |
| PC-32 | Break G-32: Treat every explicitly loaded source class as selected. | `PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project`, label `c1005-pc-32`: Explicit tests/Other/Unselected.cs has no selected-missing diagnostic. |
| PC-33 | Break G-33: Replace accumulated S with the latest row set. | `PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations`, label `c1005-pc-33`: Disjoint A/B rows require both A.First and B.Second. |
| PC-34 | Break G-34: Use only the first syntax declaration of each partial class. | `PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations`, label `c1005-pc-34`: Missing item for the second part reports that declaration path/line. |
| PC-35 | Break G-35: Recognize TestAttribute but exclude the exact short Test spelling. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-35`: BareTest is a selected member and its absent item is reported. |
| PC-36 | Break G-36: Recognize Test but reject short TestAttribute. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-36`: SuffixTest absent item is reported. |
| PC-37 | Break G-37: Reject ordinary QualifiedNameSyntax Test attributes while keeping short and global:: forms. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-37`: TUnit.Core.Test and TUnit.Core.TestAttribute methods are selected. |
| PC-38 | Break G-38: Admit every indexed method into S regardless of Test attribute. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-38`: Exact test roster is clean despite an unannotated Helper. |
| PC-39 | Break G-39: Treat Before/After attribute presence as Test eligibility. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-39`: Exact test roster is clean despite Before(Test) and After(Test). |
| PC-40 | Break G-40: Require one roster identity per Arguments attribute by suffixing ordinal to S. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-40`: Two-argument method needs exactly one item. |
| PC-41 | Break G-41: Drop Test methods with DataSource attributes from S. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-41`: Method/class/parameter data source cases still require their declared test item. |
| PC-42 | Break G-42: Drop Test methods bearing Repeat from S. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-42`: Repeat method still requires exactly one item. |
| PC-43 | Break G-43: Continue past an opted-in command row without adding unmapped. Use an otherwise matched recognized row plus explicit tests. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-43`: Command row produces CLASS_CENSUS_UNMAPPED with its CP/reason and exit 1. |
| PC-44 | Break G-44: Allow category row to bypass the unsupported-selection finding. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-44`: Category row is unmapped/1 with explicit tests and valid checklist. |
| PC-45 | Break G-45: Ignore the method segment when it is not *. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-45`: Exact-method row is unmapped/1, not a clean whole-class census. |
| PC-46 | Break G-46: Admit a named assembly operand without a finding. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-46`: Named-assembly row is unmapped/1. |
| PC-47 | Break G-47: Treat One* namespace as a supported prefix. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-47`: Wildcard namespace prefix is unmapped/1. |
| PC-48 | Break G-48: On unsupported class operand (A? or nontrailing wildcard), omit its unmapped diagnostic and use the supplied explicit source. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-48`: Unsupported operand is unmapped/1 rather than clean or INPUT_INVALID/2. |
| PC-49 | Break G-49: When selection context is null, census all supplied files as trusted S. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-49`: Opt-in direct analyzer without context is unmapped/1 even if roster matches all files. |
| PC-50 | Break G-50: Skip using-alias validation and infer eligibility only from the final attribute spelling. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-50`: A using Test alias to another attribute yields unmapped/1. |
| PC-51 | Break G-51: Treat an ambiguous base alias as a resolved empty base. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-51`: Two possible base declarations with ambiguous alias yield unmapped/1. |
| PC-52 | Break G-52: Ignore a resolved base containing Test declarations and certify direct methods only. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-52`: Selected derived class with inherited tests is unmapped/1. |
| PC-53 | Break G-53: Return a known-empty base when type resolution fails. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-53`: Missing external base yields unmapped/1. |
| PC-54 | Break G-54: Ignore selected .g.cs/auto-generated file markers. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-54`: Selected generated-file declaration shape is unmapped/1. |
| PC-55 | Break G-55: Ignore conditional directives on selected class/test declarations. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-55`: Selected conditional test is unmapped/1 for active and inactive syntax vectors. |
| PC-56 | Break G-56: Ignore Skip/custom SkipAttribute eligibility when classifying selected declarations. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-56`: Selected runtime-dependent Skip shape is unmapped/1. |
| PC-57 | Break G-57: Ignore Explicit on selected class/test. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-57`: Selected Explicit shape is unmapped/1. |
| PC-58 | Break G-58: After successfully proving the two-level base chain test-free, emit CLASS_CENSUS_UNMAPPED instead of admitting that proven branch. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-58`: Known test-free base chain is clean; helper/lifecycle methods alone are not tests. |
| PC-59 | Break G-59: Use the selecting source filename/line 1 instead of the method declaration in the missing-member record. | `PlanCoverageCensusTests.census_reports_new_or_omitted_selected_method`, label `c1005-pc-59`: Exact tuple has fully qualified Third, relative TestPath and actual TestLine. |
| PC-60 | Break G-60: Attribute missing-member findings to last selecting row or sorted CP id. | `PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations`, label `c1005-pc-60`: Overlapping CP-2-before-CP-1 fixture points to CP-2 original line/column under LF and CRLF. |
| PC-61 | Break G-61: Stamp extras with the CP location rather than the matching checklist requirement/location. | `PlanCoverageCensusTests.census_rejects_unselected_or_non_test_roster_methods`, label `c1005-pc-61`: Extra tuple has its R requirement, original line/column and checklist method not selected detail. |
| PC-62 | Break G-62: Emit a missing-member finding per selecting row instead of per distinct method. | `PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations`, label `c1005-pc-62`: One missing method selected three times yields one diagnostic. |
| PC-63 | Break G-63: Reverse the final sorted diagnostics list. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-63`: Literal expected JSON/text diagnostic sequence matches original-coordinate order. |
| PC-64 | Break G-64: Exclude CLASS_CENSUS_MISMATCH from non-PC findings in Summary.Result. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-64`: Mismatch-only public command returns (1, findings). |
| PC-65 | Break G-65: Exclude CLASS_CENSUS_UNMAPPED from non-PC findings in Summary.Result. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-65`: Unmapped-only public command returns (1, findings). |
| PC-66 | Break G-66: Include CLASS_CENSUS_MISMATCH in Summary.Missing. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-66`: Missing-assertion count stays zero in a census-only mismatch. |
| PC-67 | Break G-67: Report reachable/proven in CoveragePc or CoverageSummary when census matches. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-67`: PC and summary reachability stay unproven in JSON and text. |
| PC-68 | Break G-68: Add public bool CensusEnabled => false to PlanCoverageReport instead of keeping all census state internal. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-68`: Exact top-level key set has no census state property. |
| PC-69 | Break G-69: Change SchemaVersion from 1 to 2. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-69`: Both opt-out representations report schemaVersion 1. |
| PC-70 | Break G-70: Add a test-free supporting base candidate file to report.Sources. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-70`: Legacy source array remains the literal original selected paths/classes/hashes in order. |
| PC-71 | Break G-71: Append an enabled/disabled census marker when calculating InputsSha256. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-71`: InputsSha256 equals independent SHA over the existing exact concatenation. |
| PC-72 | Break G-72: Add a synthetic method obligation or clean-census diagnostic when S equals R (choose the obligation insertion for this control). | `PlanCoverageCensusTests.census_matches_exact_checklist_roster`, label `c1005-pc-72`: Matching fixture has exactly the original obligation sequence; no synthetic match obligation. |
| PC-73 | Break G-73: Skip ValidateCounts when census is absent/false. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-73`: Original row deficit/surplus still emits CHECKLIST_COUNT_MISMATCH with exact values. |
| PC-74 | Break G-74: Remove the class-count zero-bound-method continue in ValidateCounts. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-74`: Class-only regression row retains no numeric-count finding. |
| PC-75 | Break G-75: Skip every count promise when methods.Length==0, even with empty promise.Class. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-75`: All 1 total with no bound methods still has expected=1 actual=0 and exit 1. |
| PC-76 | Break G-76: Remove obligation.Id==promise.Id from ValidateCounts selection. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-76`: Wrong-requirement method cannot fill V-1 class count. |
| PC-77 | Break G-77: Return 1 instead of null for the dynamic ResultCount branch. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-77`: Dynamic numeric promise keeps CHECKLIST_COUNT_UNMAPPED, independent of matching method census. |
| PC-78 | Break G-78: In Program.RunAsync coverage case call runtime.Driver.RunAsync once with a harmless DriverRequest before normal CoverageCommand.Run. FakeDriver returns success. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-78`: FakeDriver call count is exactly zero; no setup throw or real process. |
| PC-79 | Break G-79: In CoverageCommand.Run after valid root resolution write a bounded .antiphon-c1005-state marker inside root, then analyze normally. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-79`: Whole in-root path/type/content-hash snapshot is unchanged. |
| PC-80 | Break G-80: Change only Get-NamespaceCensus selected from 377 back to 365. | `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`, label `scripts/lib/checkpoint-usage.ps1 Get-NamespaceCensus is stale against the compiled checkpoint cases`: Compiled cases equal the script literal. |
| PC-81 | Break G-81: Clear imported-manifest failure by returning an empty clean report for invalid manifest only. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-81`: Invalid manifest public result is invalid/2. |
| PC-82 | Break G-82: On project index syntax errors, return an empty clean report rather than the existing invalid-input result. | `PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only`, label `c1005-pc-82`: Malformed selected C# public result is invalid/2. |
| PC-83 | Break G-83: Filter TestAssertionIndex.Methods to Test-annotated declarations globally. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-83`: Legacy opt-out helper method binding remains matched, without MISSING_METHOD. |
| PC-84 | Break G-84: Check unsupported test shapes across all loaded classes rather than only selected classes. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-84`: Selected supported class stays clean with an unselected conditional/explicit sibling. |
| PC-85 | Break G-85: Remove the root-prefix refusal in CoverageCommand.Confined, keeping existence checks. | `PlanCoverageCommandTests.existing_outside_project_is_refused_by_root_boundary`, label `coverage-existing-outside-root-boundary`: The root-boundary diagnostic is missing or unconfined selected path, rather than a later opened-handle refusal. |
| PC-86 | Break G-86: Remove the !regular branch in ConfinedFileReader.Refusal. | `PlanCoverageHandleTests.opened_metadata_decision_table`, label `coverage-opened-metadata-decision`: Nonregular metadata row yields selected file is not regular. |
| PC-87 | Break G-87: Remove the links!=1 refusal in ConfinedFileReader.Refusal. | `PlanCoverageHandleTests.opened_metadata_decision_table`, label `coverage-opened-metadata-decision`: Both zero and two-link rows yield selected file has multiple links. |
| PC-88 | Break G-88: Remove finalPath root-prefix refusal in ConfinedFileReader.Refusal. | `PlanCoverageHandleTests.opened_metadata_decision_table`, label `coverage-opened-metadata-decision`: Outside final-path row yields opened file is outside root. |
| PC-89 | Break G-89: Change only SourceLimit from 1 MiB to 2 MiB. The existing oversized test uses a hardcoded 1 MiB independent of that constant. | `PlanCoverageHandleTests.oversized_selected_file_is_refused`, label `coverage-size-limit-refused`: Project/source oversized rows refuse/2; plan/checklist controls remain unchanged. |
| PC-90 | Break G-90: Move the content.Bytes>limit check inside the cache-miss branch only, so a hit returns the bytes without the current role check. | `PlanCoverageHandleTests.cached_plan_bytes_still_obey_the_source_limit`, label `coverage-cached-source-limit-refused`: Plan/C# polyglot reused as source refuses/2. |
| PC-91 | Break G-91: After verification/callback, return File.ReadAllText(path) instead of consuming the verified handle. | `PlanCoverageHandleTests.read_uses_the_verified_handle_after_path_replacement`, label `coverage-read-from-verified-handle`: Content equals verified bytes despite path replacement. |
| PC-92 | Break G-92: Exclude PC- IDs from R but keep V- and R- method items. | `PlanCoverageCensusTests.census_compares_resolved_identities_not_counts`, label `c1005-pc-92`: Exact roster with Second supplied by PC-1 stays clean. |
| PC-93 | Break G-93: Reject AliasQualifiedNameSyntax/global:: roots while keeping ordinary qualified attributes. | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once`, label `c1005-pc-93`: global::TUnit.Core.Test and global::TUnit.Core.TestAttribute methods are selected. |
| PC-94 | Break G-94: Skip same-scope TestAttribute type-shadow check while retaining using-alias validation. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-94`: Local TestAttribute with short Test spelling yields unmapped/1. |
| PC-95 | Break G-95: Ignore GeneratedCode attribute on selected declarations in an ordinary .cs file. | `PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery`, label `c1005-pc-95`: GeneratedCode selected test shape is unmapped/1. |
| PC-96 | Break G-96: Remove the resolved declaring-class predicate from ValidateCounts, retaining requirement ID. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-96`: OtherTests.Second bound to the same V-1 cannot fill DemoTests result count. |
| PC-97 | Break G-97: Remove DistinctBy(path, span) from ValidateCounts, keeping ID/class predicates. | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts`, label `c1005-pc-97`: Short/FQN duplicate method obligations still count once for declared results. |
| PC-98 | Break G-98: Append a CLASS_CENSUS_MATCH diagnostic after an equal-set census. | `PlanCoverageCensusTests.census_matches_exact_checklist_roster`, label `c1005-pc-98`: Matching fixture diagnostics is empty; the earlier obligation-only assertion remains green. |
| PC-99 | Break G-99: Change only DocumentLimit from 4 MiB to 8 MiB. The existing oversized test uses a hardcoded 4 MiB independent of that constant. | `PlanCoverageHandleTests.oversized_selected_file_is_refused`, label `coverage-size-limit-refused`: Plan/checklist oversized rows refuse/2; project/source controls remain unchanged. |

Audit: **bodies read; guards=99, mapped=99, missing=0, duplicate PC maps=0**.
All PCs name an executable mutation, reachable fixture, exact method and detecting
assertion; 18 distinct method filters, no placeholder controls. Any implementation
that cannot expose one of these observations needs a freeze amendment before Code
handoff. No product preference or unverifiable runtime-delivery seam is outstanding.

### Out of scope

- Runtime TUnit discovery, semantic compilation, arbitrary generators, conditional
  symbol evaluation and inherited test execution: D-3 deliberately returns unmapped
  for shapes whose selected declared-method set cannot be established.
- Full Antiphon.Tests assembly and unrelated Integration lanes: this tooling change
  requires the focused group plus the normal Linux Unit lane, not fleet/session
  tests. Both compatibility plans and every focused method remain mandatory.
- Six Windows-only Unit methods are excluded from CP-2 on Linux: TimeoutTests.
  windows_quick_row_finishes_beside_a_slow_row, windows_row_arguments_round_trip_intact,
  windows_chatty_row_drains_interleaved_stdout_and_stderr and
  windows_row_timeout_kills_the_start_b_grandchild; LandingRemovalPolicyControlTests.
  C665_LockedFileMidDeleteResumesOnLaterPass and
  C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded. They exercise unchanged
  Windows process/lock behavior, not census. No other Linux skip is admitted.
- Full Cartesian product of every unsupported shape with every filter spelling:
  independent controls isolate each refusal; V-1 crosses opt-out with unsupported
  rows and V-10 crosses selected versus unselected ownership. More combinations
  would repeat the same boundary without introducing a distinct guard.
- Scheduler, launch queues, transcript/session delivery, file-reader native ABI,
  deployment and historical plan opt-ins remain unchanged. Static reports prove
  no recipient delivery or dynamic PC reachability.

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

### Code admission and plan coverage

Code starts from this committed freeze after the caller checks current active
source footprints and availability. Recount source deltas if its base differs;
retain exactly these twelve methods and adjust 377 only for actual intervening
checkpoint additions/removals. The CARD-1001 opened-handle implementation must be
present. No new file beyond the existing S1 optional internal context and new test
file is authorized; no change to D-1..D-7 is required by this freeze.

Use the pre-change tool bootstrap for real importer admission before editing:
its built DLL's import --plan this artifact --out .antiphon/c1005-code-import.yml
must return 0 and preserve all three row filters, minima and estimates. It is a
read/import admission action, not another repository test. This docs-only dispatch
has not run the importer. The final tool bootstrap runs the same import and the
coverage lint below without another build. The plan's own selectedClassCensus flag
remains **absent**: its Unit/category/command rows intentionally need explicit files.

The following inline checklist is active, with original planLine coordinates.
It supplements method/PC extraction rather than replacing it. Code must preserve
coordinates or regenerate these literal line numbers after a document edit. The
new tests are intentionally missing before S1; only the final implemented source
can satisfy this lint. Every PC target label must bind to the corresponding method.
A static-labeled PC still has reachability=unproven. Record each advisory instead
of treating static coverage as a executed mutation receipt.

```plan-coverage-v1
{
  "version": 1,
  "items": [
    {"id":"V-1","test":"PlanCoverageCensusTests.checklist_flag_accepts_true_false_and_absence","kind":"method","name":"PlanCoverageCensusTests.checklist_flag_accepts_true_false_and_absence","planLine":228},
    {"id":"V-2","test":"PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input","kind":"method","name":"PlanCoverageCensusTests.checklist_flag_rejects_invalid_or_conflicting_input","planLine":229},
    {"id":"V-3","test":"PlanCoverageCensusTests.census_matches_exact_checklist_roster","kind":"method","name":"PlanCoverageCensusTests.census_matches_exact_checklist_roster","planLine":230},
    {"id":"V-4","test":"PlanCoverageCensusTests.census_reports_new_or_omitted_selected_method","kind":"method","name":"PlanCoverageCensusTests.census_reports_new_or_omitted_selected_method","planLine":231},
    {"id":"V-5","test":"PlanCoverageCensusTests.census_rejects_unselected_or_non_test_roster_methods","kind":"method","name":"PlanCoverageCensusTests.census_rejects_unselected_or_non_test_roster_methods","planLine":232},
    {"id":"V-6","test":"PlanCoverageCensusTests.census_compares_resolved_identities_not_counts","kind":"method","name":"PlanCoverageCensusTests.census_compares_resolved_identities_not_counts","planLine":233},
    {"id":"V-7","test":"PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project","kind":"method","name":"PlanCoverageCensusTests.census_tracks_filter_class_namespace_and_project","planLine":234},
    {"id":"V-8","test":"PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations","kind":"method","name":"PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations","planLine":235},
    {"id":"V-9","test":"PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once","kind":"method","name":"PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once","planLine":236},
    {"id":"V-10","test":"PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery","kind":"method","name":"PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery","planLine":237},
    {"id":"R-1","test":"PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts","kind":"method","name":"PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts","planLine":243},
    {"id":"R-2","test":"PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only","kind":"method","name":"PlanCoverageCensusTests.census_public_cli_is_deterministic_and_read_only","planLine":244},
    {"id":"R-4","test":"PlanCoverageParserTests.extracts_verification_obligations_with_original_lines","kind":"method","name":"PlanCoverageParserTests.extracts_verification_obligations_with_original_lines","planLine":246},
    {"id":"R-4","test":"PlanCoverageParserTests.classifies_tokens_without_promoting_example_values","kind":"method","name":"PlanCoverageParserTests.classifies_tokens_without_promoting_example_values","planLine":246},
    {"id":"R-4","test":"PlanCoverageParserTests.resolves_closed_test_scope_and_rejects_ambiguity","kind":"method","name":"PlanCoverageParserTests.resolves_closed_test_scope_and_rejects_ambiguity","planLine":246},
    {"id":"R-4","test":"PlanCoverageParserTests.maps_checklist_without_erasing_legacy_requirements","kind":"method","name":"PlanCoverageParserTests.maps_checklist_without_erasing_legacy_requirements","planLine":246},
    {"id":"R-4","test":"PlanCoverageParserTests.declared_total_still_rejects_zero_bound_methods","kind":"method","name":"PlanCoverageParserTests.declared_total_still_rejects_zero_bound_methods","planLine":246},
    {"id":"R-4","test":"PlanCoverageParserTests.declared_row_counts_reject_missing_and_extra_bound_methods","kind":"method","name":"PlanCoverageParserTests.declared_row_counts_reject_missing_and_extra_bound_methods","planLine":246},
    {"id":"R-4","test":"PlanCoverageParserTests.declared_counts_keep_class_and_requirement_bindings","kind":"method","name":"PlanCoverageParserTests.declared_counts_keep_class_and_requirement_bindings","planLine":246},
    {"id":"R-4","test":"PlanCoverageParserTests.declared_results_expand_arguments_without_inventing_dynamic_counts","kind":"method","name":"PlanCoverageParserTests.declared_results_expand_arguments_without_inventing_dynamic_counts","planLine":246},
    {"id":"R-4","test":"PlanCoverageAssertionTests.requires_label_at_bound_assertion","kind":"method","name":"PlanCoverageAssertionTests.requires_label_at_bound_assertion","planLine":246},
    {"id":"R-4","test":"PlanCoverageAssertionTests.requires_canary_in_assertion_not_setup","kind":"method","name":"PlanCoverageAssertionTests.requires_canary_in_assertion_not_setup","planLine":246},
    {"id":"R-4","test":"PlanCoverageAssertionTests.requires_members_and_collection_predicates","kind":"method","name":"PlanCoverageAssertionTests.requires_members_and_collection_predicates","planLine":246},
    {"id":"R-4","test":"PlanCoverageAssertionTests.reports_unmapped_prose_and_opaque_helpers","kind":"method","name":"PlanCoverageAssertionTests.reports_unmapped_prose_and_opaque_helpers","planLine":246},
    {"id":"R-4","test":"PlanCoverageAssertionTests.tracks_literal_helper_arguments_without_executing_source","kind":"method","name":"PlanCoverageAssertionTests.tracks_literal_helper_arguments_without_executing_source","planLine":246},
    {"id":"R-4","test":"PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async","kind":"method","name":"PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async","planLine":246},
    {"id":"R-4","test":"PlanCoveragePcTests.rejects_label_in_other_method","kind":"method","name":"PlanCoveragePcTests.rejects_label_in_other_method","planLine":246},
    {"id":"R-4","test":"PlanCoveragePcTests.reports_earlier_different_label_as_unproven","kind":"method","name":"PlanCoveragePcTests.reports_earlier_different_label_as_unproven","planLine":246},
    {"id":"R-4","test":"PlanCoveragePcTests.accepts_labeled_sequence_without_claiming_dynamic_proof","kind":"method","name":"PlanCoveragePcTests.accepts_labeled_sequence_without_claiming_dynamic_proof","planLine":246},
    {"id":"R-4","test":"PlanCoverageGoldenTests.c780_class_only_regression_row_keeps_master_findings","kind":"method","name":"PlanCoverageGoldenTests.c780_class_only_regression_row_keeps_master_findings","planLine":246},
    {"id":"R-4","test":"PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5","kind":"method","name":"PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5","planLine":246},
    {"id":"R-4","test":"PlanCoverageGoldenTests.c866_fixed_checks_clear_same_obligations","kind":"method","name":"PlanCoverageGoldenTests.c866_fixed_checks_clear_same_obligations","planLine":246},
    {"id":"R-4","test":"PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible","kind":"method","name":"PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract","kind":"method","name":"PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes","kind":"method","name":"PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.invalid_inputs_cannot_produce_clean_summary","kind":"method","name":"PlanCoverageCommandTests.invalid_inputs_cannot_produce_clean_summary","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.coverage_never_starts_driver_or_writes_run_state","kind":"method","name":"PlanCoverageCommandTests.coverage_never_starts_driver_or_writes_run_state","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.outside_project_fifo_is_refused_without_opening","kind":"method","name":"PlanCoverageCommandTests.outside_project_fifo_is_refused_without_opening","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.existing_outside_project_is_refused_by_root_boundary","kind":"method","name":"PlanCoverageCommandTests.existing_outside_project_is_refused_by_root_boundary","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.frozen_checklist_preserves_all_72_obligations","kind":"method","name":"PlanCoverageCommandTests.frozen_checklist_preserves_all_72_obligations","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.class_only_frozen_result_count_has_no_findings","kind":"method","name":"PlanCoverageCommandTests.class_only_frozen_result_count_has_no_findings","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.dropped_frozen_method_is_a_checklist_count_finding","kind":"method","name":"PlanCoverageCommandTests.dropped_frozen_method_is_a_checklist_count_finding","planLine":246},
    {"id":"R-4","test":"PlanCoverageCommandTests.extra_frozen_method_is_a_checklist_count_finding","kind":"method","name":"PlanCoverageCommandTests.extra_frozen_method_is_a_checklist_count_finding","planLine":246},
    {"id":"R-4","test":"PlanCoverageHandleTests.hardlinked_selected_file_is_refused","kind":"method","name":"PlanCoverageHandleTests.hardlinked_selected_file_is_refused","planLine":246},
    {"id":"R-4","test":"PlanCoverageHandleTests.oversized_selected_file_is_refused","kind":"method","name":"PlanCoverageHandleTests.oversized_selected_file_is_refused","planLine":246},
    {"id":"R-4","test":"PlanCoverageHandleTests.in_repo_fifo_project_is_refused_within_15_seconds","kind":"method","name":"PlanCoverageHandleTests.in_repo_fifo_project_is_refused_within_15_seconds","planLine":246},
    {"id":"R-4","test":"PlanCoverageHandleTests.opened_metadata_decision_table","kind":"method","name":"PlanCoverageHandleTests.opened_metadata_decision_table","planLine":246},
    {"id":"R-4","test":"PlanCoverageHandleTests.read_uses_the_verified_handle_after_path_replacement","kind":"method","name":"PlanCoverageHandleTests.read_uses_the_verified_handle_after_path_replacement","planLine":246},
    {"id":"R-4","test":"PlanCoverageHandleTests.cached_plan_bytes_still_obey_the_source_limit","kind":"method","name":"PlanCoverageHandleTests.cached_plan_bytes_still_obey_the_source_limit","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.imports_the_card_0688_table","kind":"method","name":"CheckpointImportTests.imports_the_card_0688_table","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.unescapes_pipes_in_filters","kind":"method","name":"CheckpointImportTests.unescapes_pipes_in_filters","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.maps_cp_reuse_to_the_same_build","kind":"method","name":"CheckpointImportTests.maps_cp_reuse_to_the_same_build","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.command_rows_become_command_checkpoints","kind":"method","name":"CheckpointImportTests.command_rows_become_command_checkpoints","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.derives_roster_tokens_from_the_filter","kind":"method","name":"CheckpointImportTests.derives_roster_tokens_from_the_filter","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.refuses_the_legacy_eight_column_table","kind":"method","name":"CheckpointImportTests.refuses_the_legacy_eight_column_table","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.warns_when_estimate_exceeds_row_timeout","kind":"method","name":"CheckpointImportTests.warns_when_estimate_exceeds_row_timeout","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.imports_the_card_0723_table","kind":"method","name":"CheckpointImportTests.imports_the_card_0723_table","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.serial_import_round_trips_and_excludes_other_rows","kind":"method","name":"CheckpointImportTests.serial_import_round_trips_and_excludes_other_rows","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.environment_map_round_trips_without_changing_serial","kind":"method","name":"CheckpointImportTests.environment_map_round_trips_without_changing_serial","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns","kind":"method","name":"CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.scheduler_passes_test_parallel_limit_only_to_serial_rows_process","kind":"method","name":"CheckpointImportTests.scheduler_passes_test_parallel_limit_only_to_serial_rows_process","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.invalid_serial_is_refused_before_execution","kind":"method","name":"CheckpointImportTests.invalid_serial_is_refused_before_execution","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.unknown_duplicate_and_malformed_columns_are_refused","kind":"method","name":"CheckpointImportTests.unknown_duplicate_and_malformed_columns_are_refused","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.invalid_environment_names_and_duplicates_are_refused","kind":"method","name":"CheckpointImportTests.invalid_environment_names_and_duplicates_are_refused","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.annotated_reuse_resolves_only_an_earlier_filter_build","kind":"method","name":"CheckpointImportTests.annotated_reuse_resolves_only_an_earlier_filter_build","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.filter_shorthand_and_trailing_environment_prose_are_refused","kind":"method","name":"CheckpointImportTests.filter_shorthand_and_trailing_environment_prose_are_refused","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.cross_after_reuse_is_refused_without_a_relaxation_flag","kind":"method","name":"CheckpointImportTests.cross_after_reuse_is_refused_without_a_relaxation_flag","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.conflicting_projects_cannot_share_an_output_identity","kind":"method","name":"CheckpointImportTests.conflicting_projects_cannot_share_an_output_identity","planLine":246},
    {"id":"R-4","test":"CheckpointImportTests.run_plan_and_import_resolve_the_committed_table_identically","kind":"method","name":"CheckpointImportTests.run_plan_and_import_resolve_the_committed_table_identically","planLine":246},
    {"id":"R-4","test":"CheckpointManifestTests.parses_yaml_and_defaults","kind":"method","name":"CheckpointManifestTests.parses_yaml_and_defaults","planLine":246},
    {"id":"R-4","test":"CheckpointManifestTests.rejects_backslash_or_trailing_space_output_path","kind":"method","name":"CheckpointManifestTests.rejects_backslash_or_trailing_space_output_path","planLine":246},
    {"id":"R-4","test":"CheckpointManifestTests.rejects_duplicate_or_unknown_build_ids","kind":"method","name":"CheckpointManifestTests.rejects_duplicate_or_unknown_build_ids","planLine":246},
    {"id":"R-4","test":"CheckpointManifestTests.rejects_reuse_across_different_after","kind":"method","name":"CheckpointManifestTests.rejects_reuse_across_different_after","planLine":246},
    {"id":"R-4","test":"CheckpointManifestTests.after_selector_parses_ranges_and_all","kind":"method","name":"CheckpointManifestTests.after_selector_parses_ranges_and_all","planLine":246},
    {"id":"R-4","test":"CheckpointManifestTests.results_root_must_be_under_worktree","kind":"method","name":"CheckpointManifestTests.results_root_must_be_under_worktree","planLine":246},
    {"id":"R-5","test":"CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases","kind":"method","name":"CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases","planLine":247},
    {"id":"R-5","test":"TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration","kind":"method","name":"TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration","planLine":247},
    {"id":"R-5","test":"TestClassificationGuardTests.Registry_matches_compiled_metadata","kind":"method","name":"TestClassificationGuardTests.Registry_matches_compiled_metadata","planLine":247}
  ]
}
```

Exact lint invocation (built final tool, no implicit build; exit 0 required):

```sh
dotnet tools/Antiphon.Checkpoints/bin-c1005-tool/net9.0/Antiphon.Checkpoints.dll coverage \
  --repo-root "$PWD" \
  --plan docs/superpowers/plans/2026-10-03-card-1005-opt-in-class-census-plan.md \
  --tests tests/Antiphon.Tests/Checkpoints/PlanCoverageCensusTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/PlanCoverageParserTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/PlanCoverageAssertionTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/PlanCoveragePcTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/PlanCoverageGoldenTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/PlanCoverageCommandTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/PlanCoverageHandleTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/PlanCoverageFixture.cs \
  --tests tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/CheckpointManifestTests.cs \
  --tests tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs \
  --tests tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs \
  --tests tests/Shared/TestClassificationGuardTests.cs \
  --format json > .antiphon/c1005-plan-coverage.json
```

Setup requires a qualified Linux lane with the pinned SDK, pwsh, bash, jq,
symlink/hardlink support and the existing checkpoint build-slot broker. No host is
pinned and no provider credentials are needed. The two bootstraps take the host
build-slot gate; the checkpoint tool takes each row's gate. CP-2 uses its own
isolated build output rather than CP-1 reuse. The method-segment exclusion syntax
uses parenthesized unary NOT and AND as documented by
[Microsoft's graph query filter specification](https://github.com/microsoft/testfx/blob/main/docs/mstest-runner-graphqueryfiltering/graph-query-filtering.md).
The first real CP-2 receipt must reconcile the full method roster and minimum;
zero tests or unexpected skips are a failed admission, never a reason to lower it.

### Checkpoints

Closed ordinary group after committed S1-S2, all on Linux. Each TUnit row has one
isolated build and one exact filter; CP-3 is a non-TUnit command using the two
explicit tool bootstraps. CP-1 selects all 86 listed results; CP-2 selects 4,000.
Their 86-result overlap is deliberate: focused attribution plus normal Unit
interaction/classification coverage. Preserve all per-row counts and clean-source
receipts. CP-3 keeps the original plan's two raw byte comparisons unchanged.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1005-final/` | linux-unit-coverage | `/*/*/(PlanCoverageCensusTests*)\|(PlanCoverageParserTests*)\|(PlanCoverageAssertionTests*)\|(PlanCoveragePcTests*)\|(PlanCoverageGoldenTests*)\|(PlanCoverageCommandTests*)\|(PlanCoverageHandleTests*)\|(CheckpointImportTests*)\|(CheckpointManifestTests*)\|(CheckpointNamespaceCensusUsageTests*)/*` | V-1..V-10, R-1, R-2, R-4, R-5 | all 86 listed results, 0 failed/skipped | 86 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1005-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1005-unit/` | linux-unit | `/*[Category=Unit]/*/*/(A*)\|(B*)\|(C4*)\|(C5*)\|(C60*)\|(C61*)\|(C64*)\|(C65*)\|(C664*)\|(C665_D*)\|(C665_E*)\|(C665_F*)\|(C665_J*)\|(C665_N*)\|(C665_P*)\|(C665_R*)\|(C665_U*)\|(C67*)\|(C68*)\|(C71*)\|(C721_S*)\|(C75*)\|(c78*)\|(C8*)\|(C9*)\|(Ca*)\|(Ce*)\|(Cg*)\|(Ch*)\|(cl*)\|(Co*)\|(Cr*)\|(cs*)\|(Cu*)\|(D*)\|(E*)\|(F*)\|(G*)\|(H*)\|(i*)\|(J*)\|(K*)\|(L*)\|(M*)\|(n*)\|(O*)\|(P*)\|(Q*)\|(R*)\|(s*)\|(T*)\|(U*)\|(V*)\|(wa*)\|(we*)\|(Wh*)\|(wid*)\|(Windows_d*)\|(windows_e*)\|(windows_l*)\|(Windows_p*)\|(windows_s*)\|(Windows_v*)\|(wit*)\|(wo*)\|(Wr*)\|(X*)\|(Y*)\|(z*)` | R-5 | >= 4000 executed, 0 failed/skipped; all Linux-eligible Unit names | 4000 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c1005-disabled;TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-3 | S1-S2 | n/a | linux-cli-legacy-json | `sh -eu -c 'mkdir -p .antiphon/c1005-compat; for p in docs/superpowers/plans/2026-10-01-card-0891-plan-to-test-coverage-check-plan.md tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c999-frozen-plan.md.txt; do n=$(basename "$p"); dotnet .antiphon/c1005-baseline/tools/Antiphon.Checkpoints/bin-c1005-master/net9.0/Antiphon.Checkpoints.dll coverage --repo-root "$PWD" --plan "$p" --format json > ".antiphon/c1005-compat/$n.master.json"; dotnet tools/Antiphon.Checkpoints/bin-c1005-tool/net9.0/Antiphon.Checkpoints.dll coverage --repo-root "$PWD" --plan "$p" --format json > ".antiphon/c1005-compat/$n.branch.json"; cmp ".antiphon/c1005-compat/$n.master.json" ".antiphon/c1005-compat/$n.branch.json"; sha256sum ".antiphon/c1005-compat/$n.master.json" ".antiphon/c1005-compat/$n.branch.json"; done'` | R-3 | four exit-0 coverage invocations; two byte-identical JSON pairs and two matching hash pairs | n/a | 2 | true | n/a |

### Cost

All times are **estimated**, not measured. Ordinary V/R floor (Code) is
**25 minutes = CP-1 8 + CP-2 15 + CP-3 2**, including both isolated test builds.
The two gated tool bootstraps/import/lint preparation cost **6 minutes** (3 each).
Code ordinary setup/build + V/R floor is therefore **31 minutes**, excluding
implementation and slot wait. Ordinary results total **4,086 executions** across
the two rows (86 + 4,000), plus four real CLI invocations/two comparisons.
No PC execution is charged to Code. Authoring remains the plan's separate estimate.

Mutation uses one baseline per distinct exact method/source, then **4-minute red
build/run + 0.25-minute restore/check + 4-minute green build/run = 8.25 minutes
per PC**, including each isolated build. Baselines also cost four minutes each.
The table names all exact filters, control counts and floors. Min is per invocation;
parameterized existing methods use the method prefix plus star and inspect every
argument result. An internal vector is never an execution count.

| Exact method filter | PCs | MinExecuted | Baseline minutes | Red/restore/green minutes | Family floor minutes |
|---|---|---:|---:|---:|---:|
| `/*/*/PlanCoverageCensusTests/checklist_flag_accepts_true_false_and_absence` | PC-1, PC-2, PC-3, PC-4 | 1 | 4 | 4 x 8.25 = 33 | 37 |
| `/*/*/PlanCoverageCensusTests/checklist_flag_rejects_invalid_or_conflicting_input` | PC-5, PC-6, PC-7, PC-8, PC-9, PC-10 | 1 | 4 | 6 x 8.25 = 49.5 | 53.5 |
| `/*/*/PlanCoverageCensusTests/census_reports_new_or_omitted_selected_method` | PC-11, PC-59 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/PlanCoverageCensusTests/census_rejects_unselected_or_non_test_roster_methods` | PC-12, PC-21, PC-22, PC-61 | 1 | 4 | 4 x 8.25 = 33 | 37 |
| `/*/*/PlanCoverageCensusTests/census_matches_exact_checklist_roster` | PC-13, PC-72, PC-98 | 1 | 4 | 3 x 8.25 = 24.75 | 28.75 |
| `/*/*/PlanCoverageCensusTests/census_compares_resolved_identities_not_counts` | PC-14, PC-15, PC-16, PC-17, PC-18, PC-19, PC-92 | 1 | 4 | 7 x 8.25 = 57.75 | 61.75 |
| `/*/*/PlanCoverageCensusTests/census_unions_overlapping_filters_and_partial_declarations` | PC-20, PC-33, PC-34, PC-60, PC-62 | 1 | 4 | 5 x 8.25 = 41.25 | 45.25 |
| `/*/*/PlanCoverageCensusTests/census_tracks_filter_class_namespace_and_project` | PC-23, PC-24, PC-25, PC-26, PC-27, PC-28, PC-29, PC-30, PC-31, PC-32 | 1 | 4 | 10 x 8.25 = 82.5 | 86.5 |
| `/*/*/PlanCoverageCensusTests/census_excludes_helpers_and_counts_parameterized_method_once` | PC-35, PC-36, PC-37, PC-38, PC-39, PC-40, PC-41, PC-42, PC-77, PC-93 | 1 | 4 | 10 x 8.25 = 82.5 | 86.5 |
| `/*/*/PlanCoverageCensusTests/census_reports_unmapped_selection_without_dynamic_discovery` | PC-43, PC-44, PC-45, PC-46, PC-47, PC-48, PC-49, PC-50, PC-51, PC-52, PC-53, PC-54, PC-55, PC-56, PC-57, PC-58, PC-84, PC-94, PC-95 | 1 | 4 | 19 x 8.25 = 156.75 | 160.75 |
| `/*/*/PlanCoverageCensusTests/census_public_cli_is_deterministic_and_read_only` | PC-63, PC-64, PC-65, PC-66, PC-67, PC-78, PC-79, PC-81, PC-82 | 1 | 4 | 9 x 8.25 = 74.25 | 78.25 |
| `/*/*/PlanCoverageCensusTests/opt_out_preserves_legacy_reports_and_declared_counts` | PC-68, PC-69, PC-70, PC-71, PC-73, PC-74, PC-75, PC-76, PC-83, PC-96, PC-97 | 1 | 4 | 11 x 8.25 = 90.75 | 94.75 |
| `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | PC-80 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/PlanCoverageCommandTests/existing_outside_project_is_refused_by_root_boundary` | PC-85 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/PlanCoverageHandleTests/opened_metadata_decision_table*` | PC-86, PC-87, PC-88 | 5 | 4 | 3 x 8.25 = 24.75 | 28.75 |
| `/*/*/PlanCoverageHandleTests/oversized_selected_file_is_refused*` | PC-89, PC-99 | 4 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/PlanCoverageHandleTests/cached_plan_bytes_still_obey_the_source_limit` | PC-90 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/PlanCoverageHandleTests/read_uses_the_verified_handle_after_path_replacement` | PC-91 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |

PC floor (Mutation) is **888.75 minutes**: 18 method baselines x4 = 72,
plus 99 cycles x8.25 = 816.75. External driver/evidence setup adds **4 minutes**,
so Mutation admission + PC floor is **892.75 minutes**. Combined Code + Mutation
floor is **923.75 minutes = 6 + 25 + 4 + 72 + 816.75**. A separate ordinary Review
with both bootstraps/all rows adds 31, bringing the estimate to **954.75 minutes**.
These are scheduling estimates, not a promised elapsed duration or permission to
omit a control. This dispatch ran zero repository builds/tests; runtime costs remain unmeasured.

Reuse the baseline only after exact restoration at the same source. Compared with
one additional baseline per PC, 18 instead of 99 baselines saves **324 estimated
minutes** ((99-18)x4); measured savings=0. The PC plan represents **259 method-result
executions** including expanded existing argument rows, separate from ordinary
4,086. No savings are claimed from combining controls that share a file/method.
Review repeats ordinary rows only; new changes/failures justify only affected reruns.

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

## Plan-stage validation (historical)

Validation for this dispatch is document/source inspection and checkpoint-table
structure, not runtime verification. New production/test/script edits: zero.
Builds executed: zero. TUnit results executed: zero. Legacy JSON hashes have not
been measured in Plan; CP-3 commissions that evidence for Code and Review.
The lightweight document check found three checkpoint rows with 11 cells each,
positive minute estimates, all 12 proposed methods, and both compatibility input
files. `git diff --check` passed. This was not the compiled checkpoint importer.

## TestDesign document validation and handoff

Read-only checks: unchanged D-1..D-7/S1-S2 and compatibility command; three
eleven-cell checkpoint rows; 99 unique guard/PC pairs; 18 method filters; 78 active
checklist method items with valid original V/R coordinates; 86 focused and 4,000
Linux Unit result floors; 25-minute CP sum; 923.75-minute combined Code/Mutation
estimate. This is document/source validation, not compiled importer or runtime
proof. No source/test/fixture/script change, build, repository test or PC run was
performed by TestDesign. All frozen methods are executable after the named S1 setup.

--- next stage ---
next: code
handoff: Start from this committed CARD-1005 freeze after current-base census and active-footprint checks plus real three-row importer admission. Implement unchanged S1-S2, twelve new methods, namespace literal 377, CP floors 86/4000 and both raw legacy JSON comparisons. Run ordinary V/R only; 99 method-scoped PCs remain for post-land SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-03-card-1005-opt-in-class-census-plan.md

### Code-stage runtime admission correction (CARD-1005)

The first closed run, `20261003-162835-0116`, refused CP-2 before test
execution: the pinned Microsoft.Testing.Platform 2.2.2 / TUnit 1.44 parser
rejects unary NOT. The identical filter refusal was reproduced using the
unchanged `bb5fa774` baseline test runtime. This is a filter-admission repair,
not a smaller verification profile: CP-2 still selects every current
Linux-eligible Unit method, excludes exactly the same six Windows-only
methods, requires 4,000 executions and zero failed/skipped results, and
retains the original 15-minute estimate, derived 45-minute row deadline and environment. CP-1/CP-3 are unchanged.

The replacement is one positive OR filter with the category constraint once
on the assembly segment (the property bag applies to the whole test node). Prefixes were derived from the compiled Unit method roster; none can
match the six excluded method names under the pinned parser's case-insensitive
matching. A manual parser-membership check covered all 2,777 compiled Unit
method identities: exactly the six exclusions were false, all other Unit
methods were true, and all identities with Integration metadata were false.
Argument-expanded cases are 4,006 total and 4,000 eligible (including the
18-case hold-sentence source). The fresh CP-2 TRX must reconcile those exact
identities and counts before any green claim. The mechanical syntax correction
is recorded here as S2 finalized verification evidence; no assertion, census
minimum, timeout, invariant, or positive control was relaxed.

Evidence: `.antiphon/c1005-unit-roster.csv`,
`.antiphon/c1005-filter-admission.log`,
`.antiphon/c1005-base-filter-refusal.log`, and the first run's CP-2 console.
The first admitted positive expression placed Category=Unit on every method
operand. Run `20261003-165835-e5ba` passed CP-1 (86/86) and CP-3, but CP-2
remained in TUnit MetadataDependencyExpander/MetadataFilterMatcher pre-registration
regex matching without a TRX. A managed-stack-only diagnostic confirmed that
location, and the owned run was explicitly stopped before changing the plan;
its Unit row is incomplete, not passed. The final expression deduplicates
case-insensitive equivalent prefixes and applies Category=Unit once before the
method alternatives. The same exhaustive 2,777-identity parser admission passed.
The final red-row rerun selects only CP-2 from this closed table: CP-1/CP-3 already
passed with byte-identical implementation/test source and unchanged selections.
No production/test file changed since `f42849b3`, no assertions or deadlines were
relaxed, and fresh source-qualified CP-2 evidence is still required. Evidence:
`.antiphon/c1005-unit-stack.txt`, `.antiphon/c1005-filter-admission-root.log`.