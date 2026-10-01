# CARD-0891 / CARD-0901: static plan-to-test coverage check

Date: 2026-10-01. Stage: Plan complete; TestDesign follows. Baseline: local `origin/master` at **`2b4d7313324b6eaeadf6e45fb15808bacfad6263`**. Assigned branch `feat/card-task-d8aebb25` starts at `14661919c77b81819bf07a6380f58687225b4dfc`; it is not rebased. A later read-only remote observation returned master `321c7826b656371f788fcc99950a5635e4c8b270`; that is not the source baseline used here. Reconcile prerequisites at Code admission. This stage changes only this file.

## Outcome and scope

Add `coverage --plan <path>` to `tools/Antiphon.Checkpoints`: a read-only, deterministic lint of a plan's promised assertions against its selected C# tests. It reports missing methods, assertion labels and canaries; missing assertion references to promised members; prose it cannot map; and positive-control labels whose preceding assertions lack labels. Bind each obligation to its named test, rather than accepting an occurrence anywhere in the repository.

Ship the inexpensive static alternative explicitly allowed by CARD-0901. A static pass means the stated syntax checks passed, **not that a mutant reached its intended assertion**. Print this distinction in every report. Code runs the command before final checkpoints and pastes its complete output; Review reruns it at the reviewed source. Existing `run`, import, build slots, source receipts and land approval retain their behavior. No automatic new land gate, provider launch, mutation executor or repository-wide plan migration is included.

The operator's 2026-10-01 Final Review rule governs: failure requires an existing-behavior regression or a new reachable fail-open/privacy exposure. Lint findings are evidence to inspect and reconcile, not an independent reason for another Review round. Keep ordinary verification bounded to the changed CLI/parser and existing manifest contracts. The optional executed-reachability extension in D-6 is not commissioned by this plan.

## Evidence and limits

Read CARD-0891 and CARD-0901 in full using `pwsh -NoProfile -File scripts/card.ps1 get <card> -Board Antiphon`. Templates inspected from the frozen `origin/master`: `docs/superpowers/plans/2026-10-01-card-0826-daily-host-cleanup-plan.md` and `2026-10-01-card-0866-disposal-preview-redaction-plan.md`. Owners: `docs/project-context.md`, `docs/testing-and-build.md` (manifest, runner, slots, filters), and `docs/orchestration-loop.md` section 1. This is a Plan delegate reading its own sources; no sub-delegates were dispatched.

Measurements below are source observations from `git show`, `git rev-parse`, `git diff --name-only`, `git ls-remote`, `rg`, bounded source reads and a Node attribute census. No build, test, mutation, provider, deployment or production settings write was performed. Executed counts in this stage: Linux **0**, Windows **0**. All future counts and costs are estimates/rosters, not receipts.

| Observation | Evidence | Limit / consequence |
|---|---|---|
| Existing parser | `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`: `ImportMarkdown`, `ImportFile`, public `SplitRow`, `RosterTokens`, per-OS `TryParseMin`. Import requires the nine ordered columns and understands escaped pipes, optional columns and same-After reuse. | Reuse it; do not introduce a second checkpoint parser or change its admission rules. `RosterTokens` alone does not resolve files or all single-class trailing-wildcard filters. |
| CLI entry and dependencies | `Program.cs` dispatches verbs and already supports repeated option values through `ArgSet.GetAll`. Tool project has YamlDotNet; no existing Roslyn package reference was found in repository project files. | Add one isolated verb and syntax dependency; do not route coverage through `CheckpointApp.CreateRun` or the scheduler. |
| Historical CARD-0866 | Full SHA `1400463a48fc355eba8be3c51236cc809c1c119f`; plan lines 206, 210, 229 contain the V-1/V-4/V-5 promises. `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalRedactionTests.cs` is 423 lines there. | Historical source, not the current implementation. Freeze it before later fixes obscure the omissions. |
| Canary occurrence is insufficient | At that SHA, V-1's inputs contain `c866-user`; V-4 also intentionally preserves that plain value. `Wire.AssertNoPath` excludes other canaries but omits it. | Whole-file substring presence would incorrectly pass V-1. Require an assertion association to V-1, including supported helper calls. |
| Missing field assertions | Historical V-1 checks PaneId, ExpectedSessionId, Shell.Pid and Eligible, but not WorkspaceId/TabId, BackendProtocol, expiry and other promised facts. V-4 checks null Foreground but lacks a distinct empty-Foreground assertion. V-5 checks Eligible/Outcome without its promised exact IdentityUnproven blocker. | Some prose needs explicit mapping; a source occurrence, fixture initializer or another V's assertion is not coverage. |
| CARD-0866 review cost | Four rounds, CP-1 56/56 and CP-2 37/37 surviving the cited redactor mutant. | Card-supplied historical execution evidence; not reproduced here. |
| Historical CARD-0835 | Review 3 source `1d994aac952bed0a65506db0bb84742b7e9183e4` is available. PC rows are at plan lines 535/537/538/540/541/559/560/561. The three test files below are 352/453/475 lines. | Eight unreachable PCs and 86 present names are card-supplied historical findings. Source inspection confirms masking patterns; it does not reproduce eight mutation executions. |
| CARD-0835 masking examples | `C835_StrictAdmission` checks Exit/reason before lease labels; `C835_DriftAndReuse` checks Exit before drift/stamp labels; land admission uses an unlabeled `Should.ThrowAsync`; settlement has earlier assertions; PC-23 has earlier assertions with a different case label. `fixed-vector-parity` occurs in a different method from PC-5's V-5 target. | Static binding and predecessor checks must report these distinctions. PC-23 cannot honestly be called dynamically unreachable solely from “all assertions labeled.” |
| Existing regression census | `CheckpointImportTests`: 20 `[Test]`, no `[Arguments]`; `CheckpointManifestTests`: 6 `[Test]`, no `[Arguments]`. Relevant tooling/testing-doc paths have no difference between the assigned HEAD and frozen origin/master. | Prospective 26 results on either OS; recount after CARD-0885 lands. No test discovery/execution was used. |

The flaky-card descriptions for CARD-0889/0890/0900 were also read. Other hazard names below come from the requested brief and template; their runtime status was not remeasured. Live task occupancy was not queried because this stage dispatches nothing. Supplied “in flight/queued” states are historical commissioning context, not current admission authority.

## Design decisions

### D-1: one tool verb, a separate read-only analyzer

Implement `coverage --plan <repo-relative-or-in-root-absolute-path> [--tests <path> ...] [--format text|json] [--checklist <path>]`, using the existing `--repo-root` convention. Default is text. Resolve the plan once, invoke `PlanTableImporter.ImportFile` for its checkpoint manifest, then analyze plan and test text. Never execute a command from a Filter, code fence, mutation cell or test file. No run directory, cleanup, Git subprocess, slot acquisition, compilation, restore, network call or driver launch occurs inside this verb.

`--tests` accepts repeated literal files, no globs. Automatic selection uses literal `.cs` paths under Scope/implementation test-file cells plus classes in imported TUnit filters. Resolve class declarations only under the referenced test projects; handle partial classes and linked test files already explicitly named by Scope. Support the documented class OR syntax, optional namespace segment, and trailing `*` as a suffix match; report every matched class. A category/full-namespace filter, unresolved class, ambiguous unqualified method, external linked source or non-C# test command requires explicit test paths/checklist binding and is reported, never treated as an empty successful selection. Explicit files do not silently replace missing plan-selected files: report the missing selections too.

Validate nonempty selection, existing readable files, root confinement (including symlink/reparse resolution) and duplicate canonical paths. In production accept `.cs`; the pure analyzer's in-memory source inputs let golden tests use `.cs.txt` fixtures without compiling them. Tests use task-owned temporary roots through established test custody; do not add a cleanup mechanism. Paths are normalized to `/` only in reports; do not rewrite Windows configuration paths.

Rejected: a PowerShell/regex duplicate of the plan importer; making `run` automatically lint every legacy plan; running/importing arbitrary test code; grepping the entire repository to satisfy missing test selection. The command's bootstrap build is separate from its read-only runtime.

### D-2: an explicit grammar, with honest handling of legacy prose

Use original 1-based line/column locations, preserving CRLF/LF equivalence. Scan `## Verification design` until the next level-1/2 heading. Recognize V/R table rows or paragraphs beginning `V-[0-9]+` / `R-[0-9]+`, and PC rows containing `PC-[0-9]+[A-Z]?`. PC tables within this section are included regardless of their level-3 title; the `### Checkpoints` table itself is excluded from assertion extraction. Use `SplitRow` for Markdown cells. A PC row gets its test from its explicit method or its referenced V row; ambiguous/missing binding is a finding. Fenced examples are not scanned as obligations except the optional format below.

Backtick spans are single-line code spans with matching backtick delimiter length; escaped backticks and unmatched spans produce a location-bearing parse diagnostic. Classify spans by their role, not just their spelling:

| Role | Grammar and association |
|---|---|
| Method | C# identifier or dot-qualified identifiers, optionally followed by a parenthesized signature. A V row's method column/leading method is authoritative. Normalize away the signature; retain class qualification and resolve against the selected declarations. Identifier grammar is `[A-Za-z_][A-Za-z0-9_]*` for this first version; unsupported identifiers are unmapped. |
| Label / witness | Nonempty literal named in an assertion/witness/intended-red cell or following “label”, “witness”, “assertion” or “fails at”. Hyphenated lowercase names in decisive-assertion cells are labels; labels do not require hyphens. Match a full label token at a message boundary, allowing a following space/case suffix, never arbitrary substring matches. |
| Canary | Literal governed by “check … absent”, “exclude”, “exclusion”, “omit” or “canary” in the V requirement sentence. Bind to that V/test, even when the same literal is intentionally preserved elsewhere. |
| Member / expected value | An explicit identifier following “verify/check/assert/preserve” or a checklist entry. Split camel/Pascal/underscore names into words for legacy suggestions; exact checklist names remain case-sensitive C# identifiers. |
| Examples / non-obligations | Paths, URLs, filters, shell commands, case-key tables, input/expected-display value cells, language keywords, framework type/attribute mentions and production-location/mutation cells are not assertion labels. Record recognized exclusions in JSON with their reason. A token whose role cannot be classified is `unmapped`, not silently discarded. |

V paragraphs may extend a table row (as CARD-0866's matrix does). Associate text beginning a V ID until the next V/heading/table boundary; do not assign an arbitrary prose paragraph to whichever test happened to appear last. References such as “matrix above” must resolve to that V's earlier paragraph, otherwise report the reference as unmapped. Do not mistake `c866-user` in a Path input table for an assertion; the exclusion sentence at original line 206 makes it an obligation.

For legacy requirement sentences, split lists on commas, `/` and the final “and” outside code spans; retain the exact clause. Recognize identifiers and `empty <member>` / `null <member>` as distinct requirements, including singular/plural word normalization for suggestions. Check only the associated test's assertions. A phrase such as “incarnation facts” has no general trustworthy C# translation: print `PROSE_UNMAPPED` with that phrase and any candidate members. Never silently map it to one convenient field or claim semantic proof from fuzzy name matching. Unknown assertion/helper syntax is likewise visible.

TestDesign may remove that ambiguity with one fenced `plan-coverage-v1` JSON object inside Verification design, or the same object in the optional `--checklist` file. Exact schema: `version: 1`, `items: [{id, test, kind, name, planLine, maps?}]`. `id` is a V/R/PC ID; `test` is class-qualified; `kind` is one of `method`, `label`, `canary`, `member`, `empty`, `null`, `value`; `name` is its literal spelling; `planLine` identifies the original obligation. `maps` optionally quotes the exact legacy clause that this entry interprets. Multiple entries can map one clause (for example all incarnation fields). Reject unknown keys/kinds, duplicate conflicting entries, stale/nonmatching locations, unsupported versions and bindings outside selection. A checklist can resolve an unmapped clause; it cannot suppress an extracted label/canary, skip a V, claim a reason for ignoring a missing assertion, or replace legacy extraction. Merge and deduplicate by obligation identity while retaining all source locations.

For example, a plan promising both empty and null Foreground needs separate `empty` and `null` items for `Foreground`; a canary needs `kind: canary`, not an undifferentiated name occurrence. `member` means a member appears in the asserted actual expression; `value` means the exact enum/constant/literal appears in its expected expression. This deliberately small checklist is an explicit promise, not an assertion-language interpreter.

Rejected: every backtick string is a required assertion; hard-coding CARD numbers or disposal DTO members into the analyzer; a growing English synonym dictionary; a mandatory rewrite of all existing plans; checklist-only extraction that conceals older prose. Historical unmapped prose may remain until its author supplies an explicit mapping.

### D-3: syntax evidence at assertions, not lexical presence in setup

Use a pinned net9-compatible `Microsoft.CodeAnalysis.CSharp` package for syntax parsing only; TestDesign pins the exact version before Code. Do not load a test assembly, create an MSBuild workspace or restore target projects. Small concrete classes under `Coverage/` own parsing, binding, analysis and output. Invalid C# syntax is invalid input, not an empty assertion index.

Initially recognize the repository's Shouldly assertion forms: receiver `.Should*` calls and `Should.Throw` / `Should.ThrowAsync`, including awaited/generic/chained forms. Keep an explicit tested signature table for actual/expected/custom-message positions, including Case arguments. A random `.ShouldSomething()` call does not establish coverage. Unrecognized assertion-looking calls are unmapped. Do not treat the `expected` string in `ShouldBe("text")` as a custom message. Comments, disabled text and declarations cannot satisfy assertions.

Index actual/expected expressions and message expressions separately. A label must occur in a supported custom-message expression. A canary must flow to a supported assertion's exclusion argument; presence only in a fixture, `[Arguments]`, unrelated test, comment or preservation equality does not satisfy it. A member must occur in the actual expression (or a simple local alias of it), not merely in the custom message. `empty` requires `ShouldBeEmpty`, an exact zero-count assertion or equality to an empty collection; `null` requires `ShouldBeNull` or equality to null. Either cannot substitute for the other. `value:IdentityUnproven` requires that expected symbol/value in an assertion, not `Eligible.ShouldBeFalse()`.

Bounded syntax tracing supports literals, const/local aliases, interpolated message literal segments, literal concatenation, literal arrays in `foreach`, and uniquely resolved helper calls within the selected source/class/nested helper. Bind literal helper parameters at their call sites (CARD-0866's `AssertNoPath(json, witness)` and literal canary array are the seed case). Do not infer arbitrary data flow, virtual dispatch, callbacks or return values. Cycles, ambiguous helpers, nonliteral argument transformations and unresolved external helpers return `unmapped` with the call site. Deduplicate observations without losing V/test/surface provenance. A helper reachable from a different test cannot satisfy this test.

This is evidence of an assertion reference, not a proof that the expected value is correct or the asserted branch executes. Do not invent whole-object equality expansion into every DTO field. Prove these limitations with negative fixtures rather than expanding into a general analyzer framework.

Rejected: source substring matching (misses the historical canary defect); a home-grown C# regex parser (comments, strings, nested methods and assertion arguments are material); full compilation/control-flow analysis (cost exceeds this lint); executing helpers to learn their behavior.

### D-4: static PC reachability risks, with no false dynamic certificate

Resolve each PC to its named method and message label. Inspect supported assertions lexically before the target assertion in that method, including supported local helper expansion at the call site. Report each unlabeled or unresolved-message predecessor with its source location. A runtime value like `wrong.Output` is diagnostic content, not a stable label. A label elsewhere in the class is `PC_LABEL_NOT_IN_METHOD`, even if global presence succeeded. Missing methods/labels remain ordinary missing findings.

Different earlier stable labels are reported as `PC_EARLIER_OTHER_LABEL` with the preceding and target labels. This is an advisory static risk, not proof that the first one fails for the proposed mutation. Conditional/loop ordering and unknown helper dispatch stay explicitly unproven. In particular CARD-0835 PC-23 is visible through this advisory; do not claim the minimum “every preceding assertion is labeled” rule proves it reachable. A corrected test can place the intended invariant label on the first detecting assertion, retaining later detail labels.

Group predecessor findings by PC to avoid one line per repeated loop execution. Every PC has a status: `missing-target`, `unlabeled-predecessor`, `unmapped`, or `static-labeled`; include `earlierOtherLabels` where present. Every status carries `reachability=unproven`. Static-labeled PCs with other stable labels may still be execution-order risks. Never emit `reachable=true` or a passed mutation count from this command.

Rejected: presence as reachability; calling all preceding assertions with any message “labeled”; treating another case's label as the intended failure; labeling every earlier assertion with the PC ID merely to silence the checker; making every different setup label a hard failure. The last would add noise without proving a regression.

### D-5: stable output and workflow integration

Output is sorted by plan line/column, obligation ID/kind/name, test path/line and code, with ordinal comparisons. JSON has schemaVersion 1, plan/checklist/source-file SHA-256 digests, selected paths/classes, obligations (including matched locations), exclusions, diagnostics, PC statuses and summary. No timestamps, machine homes, source-file bodies, arbitrary exception messages or environment dumps. Reading errors identify a selected relative path and a bounded fixed reason. JSON text values are escaped. Text uses JSON-quoted string-valued fields so spaces/backslashes cannot corrupt the record grammar.

Exact text envelope and record shapes (the angle-bracket values below are illustrative):

```text
PLAN-COVERAGE schema=1 mode=static plan="<relative-plan>" planSha256=<digest> inputsSha256=<digest> files=<N>
COVERAGE code=MISSING_CANARY planLine=206 planColumn=<N> id=V-1 test="HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels" name="c866-user" testPath="tests/...cs" testLine=0 detail="no associated exclusion assertion"
COVERAGE code=PROSE_UNMAPPED planLine=206 planColumn=<N> id=V-1 test="HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels" name="incarnation facts" testPath="" testLine=0 detail="explicit member mapping required"
PC-COVERAGE id=PC-22 status=unlabeled-predecessor target="unclean-evidence-no-request" test="CheckpointSourceApprovalTests.land_admission_requires_clean_review_source" targetLine=<N> predecessorLine=<N> earlierOtherLabels=[] reachability=unproven
PLAN-COVERAGE-END obligations=<N> matched=<N> missing=<N> unmapped=<N> pcIssues=<N> pcAdvisories=<N> result=<clean|findings|invalid> reachability=unproven
```

Print one COVERAGE record for each missing/unmapped obligation and one PC-COVERAGE record for every PC, including clean static rows. Missing/unlabeled/unmapped PC status contributes once to `pcIssues`; different earlier stable labels contribute once per PC to `pcAdvisories` and remain visible at exit 0. Counts distinguish obligations from diagnostics. Exit 0: no missing, unmapped or PC issues; exit 1: completed analysis with such findings; exit 2: invalid/unreadable inputs or internal analysis failure. Invalid runs print `result=invalid`, never a clean footer. Retain preexisting meanings of these codes for every other verb. JSON carries the same decisions, not an independently implemented analyzer.

Update only the checkpoint sections of `docs/testing-and-build.md` with the grammar, limits and exact Code/Review command/report recipe. Code pastes the complete text envelope and records before the final CHECKPOINT lines, plus a short disposition for remaining advisories. Review runs the same plan/test selection and compares digests/diagnostics to the reviewed files; it does not trust the earlier paste. Read-only coverage may run on a dirty tree for authoring, but final ordinary checkpoint/source receipt requirements remain unchanged. If lint prompts any tracked edit, commit/push it and regenerate evidence at the resulting source; do not relabel old output.

No `server/Bundles` edits are needed: those stages already consume the testing owner and the dispatched plan. Put the requirement in this plan and the owner instead of growing the nearly full composed instructions. Any subsequently authorized bundle change must be net-shorter against its actual integration base and preserve the 30,000-character guard; it is outside this footprint.

Rejected: ad hoc prose reports with no plan locations; exit 0 after unknown analysis; a new checkpoint-receipt schema or SourceLanding gate; silently promoting advisory lint to a Final Review policy change.

### D-6: optional later executed reachability, not part of S1/S2

A later separately planned slice may accept an explicit, reviewed patch/expression and exact method/argument selector for each PC. Reuse SourceLanding Mutation custody and the checkpoint driver's isolated builds/TRX, running baseline-green, compiling-defect/intended-red, restore/fresh-build/green. Require the failure message to contain that PC's label for the declared case; failure at another label, setup/compile error, timeout, zero result or incomplete restoration is not proof. Never translate arbitrary English mutation prose into commands automatically. No mutation is applied by `coverage` in this delivery.

Preserve the eight CARD-0835 fixtures now so this extension has a fixed historical seed. Dynamic acceptance would show all eight original variants failing before their intended labels, and corrected variants reaching those labels. It must use the relevant implementation snapshot, not execute frozen source excerpts as if they were a buildable product. Original Review findings remain historical until that experiment runs.

Incremental estimate: **3–5 hours authoring**, plus **24 phase invocations for eight variants**; allow **4–8 minutes per isolated build/test phase**, plus 20 minutes custody/reporting, or **116–212 minutes verification** before any heavy-test savings. These are estimates, not measurements. This is substantially more expensive than static lint and is neither a dependency nor an acceptance gate for CARD-0891/0901's chosen static alternative. No outstanding operator decision is needed to implement S1/S2.

## Exact implementation footprint and slices

Plan/TestDesign edit this file only. Future Code uses the following closed footprint; tests precede implementation. All tests are deterministic syntax/command tests in `Antiphon.Tests`, tagged Unit, with no provider, database or native host requirements.

| Slice | Exact files | Work and exit |
|---|---|---|
| S1: detecting contracts | New `tests/Antiphon.Tests/Checkpoints/PlanCoverageParserTests.cs`, `PlanCoverageAssertionTests.cs`, `PlanCoveragePcTests.cs`, `PlanCoverageGoldenTests.cs`, `PlanCoverageCommandTests.cs`, `PlanCoverageFixture.cs`; new fixture files enumerated below. This plan only if TestDesign freezes details. | Write all 20 tests and raw-input goldens against public CLI/pure analyzer seams. Minimal compiling declarations and the new CLI case may precede the implementation so red is an assertion failure, not a missing type or unknown verb. Commit/push before the declared preparatory CP-1. |
| S2: analyzer and owner documentation | New `tools/Antiphon.Checkpoints/Coverage/PlanCoverageReader.cs`, `TestAssertionIndex.cs`, `PlanCoverageAnalyzer.cs`, `PlanCoverageReport.cs`, `CoverageCommand.cs`; modify `tools/Antiphon.Checkpoints/Program.cs`, `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj`, `docs/testing-and-build.md`; S1 tests as needed. | Pin syntax package, implement D-1..D-5, wire one verb/help entry and document the workflow. Reuse `PlanTableImporter` without edits. Commit/push, run coverage on the final plan/checklist before final CP-1; retain all evidence. |

Suggested explicit Code scope is the five new Coverage files, tool Program/project, six new test/helper files, `tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/**`, testing owner and this plan. Do not widen to `tools/**`, `tests/**`, scripts or delegation. `Manifest/PlanTableImporter.cs`, `CheckpointApp.cs`, existing regression tests, all Cleanup files and all production/landing/runner code are read-only dependencies.

### Frozen fixtures and provenance

Under `tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/`, add only:

- `c866-plan.md.txt`, `c866-tests.cs.txt`, `c866-checklist.json`, `c866-fixed-tests.cs.txt`, `c866-expected.json`.
- `c835-plan.md.txt`, `c835-state.cs.txt`, `c835-script.cs.txt`, `c835-approval.cs.txt`, `c835-expected.json`.
- `provenance.json` recording full original SHAs, repository paths, original line ranges, unmodified excerpt digests and any synthetic-wrapper line offsets. These paths are literal data, never commands.

For c866, use `git show 1400463a48fc355eba8be3c51236cc809c1c119f:docs/superpowers/plans/2026-10-01-card-0866-disposal-preview-redaction-plan.md` and the test file at that SHA. Freeze the verification paragraphs/table rows for V-1/V-4/V-5 with their original lines, plus enough section/checkpoint context to exercise the public parser. Keep the complete 423-line source as inert text so called helper assertions are available. Fixture assembly may supply a source map/in-memory files; it must not rewrite historical clauses or pretend a constructed excerpt is the full original plan.

The unaugmented legacy run must report V-1's missing exclusion for `c866-user`, V-1's absent/unmapped fact clauses, V-4's empty-Foreground requirement distinct from null, and V-5's missing expected IdentityUnproven value. Assert the required diagnostic identities and original line numbers; unrelated legacy ambiguity may also be reported. For the exact red/green pair, **use the same checklist and plan bytes on both historical and corrected source**. The checklist explicitly expands ambiguous clauses to WorkspaceId, TabId, BackendProtocol, ExpiresAtUtc, incarnation/member facts, expected UUIDs, Code and PlannedTerminationPids, and maps empty/null Foreground and the blocker/value requirements. TestDesign freezes the full mapping after checking the historical DTOs. The fixed source variant adds real assertion syntax and missing inputs; it must not remove promises, add comment-only names or weaken goldens. It reports zero missing/unmapped obligations for this frozen V subset. This does not claim that arbitrary legacy English becomes automatically understood or that every other PC in the full CARD-0866 plan is dynamically proved.

For c835, freeze the V-to-method and eight PC rows from `docs/superpowers/plans/2026-09-30-card-0835-checkpoint-receipt-dirty-tree-plan.md` at `1d994aac952bed0a65506db0bb84742b7e9183e4`, and whole inert source files `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs`, `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs`, `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs`. The expected report contains all eight IDs: PC-5, PC-7A, PC-7B, PC-8, PC-9A, PC-21, PC-22, PC-23. PC-5 is a wrong-method target; the unlabeled predecessors are explicit; PC-23 includes earlier-other-label advisory and unproven reachability. Do not manufacture eight static “unreachable” verdicts to match a historical dynamic claim. Never modify or run those live heavy tests in this card.

Fixtures are loaded from the repository with the existing `CheckpointFixtures.Fixture` path convention, so no test-project resource or Compile change is needed. `.cs.txt` prevents TUnit discovering historical tests. New fixed examples are clearly synthetic; historical fixtures/digests remain immutable.

## Verification design

### Ordinary roster and regression scope

Twenty new, single-result `[Test]` methods; internal negative/positive fixture pairs are assertions, not additional executions. TestDesign may refine spelling/details but must preserve exact counts or revise this table before Code. Add no broad Unit lane. No clock is needed in the analyzer: source text and digests determine output. For command seams use fixed input/outputs, not elapsed-time assertions. Reuse current owned-temp helpers without their native cleanup tests.

| ID | Class (results) | Exact methods and decisive assertions |
|---|---|---|
| V-1 | `PlanCoverageParserTests` (4) | `extracts_verification_obligations_with_original_lines` (`coverage-parser-items`); `classifies_tokens_without_promoting_example_values`; `resolves_closed_test_scope_and_rejects_ambiguity`; `maps_checklist_without_erasing_legacy_requirements`. Cover escapes/fences, CRLF, excluded mutation/input values, partial classes, class OR, explicit-path omissions, invalid/empty selection and additive mappings. |
| V-2 | `PlanCoverageAssertionTests` (5) | `requires_label_at_bound_assertion`; `requires_canary_in_assertion_not_setup` (`coverage-canary-asserted`); `requires_members_and_collection_predicates` (`coverage-empty-distinct`); `reports_unmapped_prose_and_opaque_helpers`; `tracks_literal_helper_arguments_without_executing_source`. Cover Shouldly signatures, comments/other-method/setup decoys, exact enum expected value, aliased members, literal helper witness/canary arrays and cycles. |
| V-3 | `PlanCoveragePcTests` (4) | `reports_unlabelled_predecessor_including_throw_async` (`coverage-pc-predecessor`); `rejects_label_in_other_method`; `reports_earlier_different_label_as_unproven`; `accepts_labeled_sequence_without_claiming_dynamic_proof`. Every PC gets a status and plan/test locations; dynamic output flags remain unproven. |
| V-4 | `PlanCoverageGoldenTests` (3) | `c866_legacy_reports_v1_v4_v5` (`coverage-legacy-v-gaps`); `c866_fixed_checks_clear_same_obligations`; `c835_eight_pc_risks_are_visible`. Pin source provenance and required diagnostic set, the same-map historical-red/fixed-green pair and all eight PC IDs/classifications. |
| V-5 | `PlanCoverageCommandTests` (4) | `coverage_command_preserves_existing_import_contract`; `renders_stable_text_json_and_exit_codes` (`coverage-exit-findings`); `invalid_inputs_cannot_produce_clean_summary`; `coverage_never_starts_driver_or_writes_run_state`. Call the public CLI entry in process; compare text/JSON decisions, digest changes and stable order, malformed/read/path errors, and a driver/launcher seam that fails immediately if invoked. Check directories remain unchanged; do not depend solely on that injected driver to prove no side effects. |
| R-1 | `CheckpointImportTests` (20 existing), `CheckpointManifestTests` (6 existing) | Preserve imported table/escaped filters, environment, serial, reuse/After, per-OS counts, YAML round-trip and validation. Existing tests plus V-5 pin the additive CLI behavior. No modification to the current execution, receipt or land paths is planned. |

Parser fixtures cover Windows drive/backslash spelling as text on either OS; actual confinement uses platform path APIs. Linux is the commissioned execution host. The same pure roster is expected to execute 46 on Windows if later requested; it has no Windows-only branch/skip requirement. Do not dispatch a Windows copy just to duplicate managed syntax evidence. Native symlink/reparse behavior beyond existing confinement APIs is not newly claimed by this static checker; an unresolvable link is invalid input, not “probably inside.”

### Positive controls

Six specific implementation defects, one each; exact named assertions must be the first detecting assertions for the affected result. Place outcome/diagnostic-set assertions before convenience status/count assertions that would otherwise mask the promised label, or give those first checks the same invariant label for the same reason. TestDesign inspects this ordering. No timeout, compiler error or harness failure is credited. Post-land SourceLanding Mutation executes these under the repository's normal lifecycle; they are distinct from the uncommissioned dynamic-checker feature in D-6.

| PC | Concrete mutation | Named detecting test / intended failure |
|---|---|---|
| PC-1 | In `PlanCoverageReader`, omit label/canary entries from a decisive-assertion V cell while preserving methods. | `PlanCoverageParserTests.extracts_verification_obligations_with_original_lines`, `coverage-parser-items`: expected exact obligations/line locations differ. |
| PC-2 | In `TestAssertionIndex`, accept any setup literal as a canary assertion association. | `PlanCoverageAssertionTests.requires_canary_in_assertion_not_setup`, `coverage-canary-asserted`: setup-only case incorrectly loses its missing-canary finding. |
| PC-3 | In `PlanCoverageAnalyzer`, let a null assertion satisfy an empty-collection obligation. | `PlanCoverageAssertionTests.requires_members_and_collection_predicates`, `coverage-empty-distinct`: null-only source incorrectly covers empty Foreground. |
| PC-4 | In the PC analysis, exclude `Should.ThrowAsync` from predecessor inspection. | `PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async`, `coverage-pc-predecessor`: missing predecessor diagnostic. The source being analyzed is inert; no exception-producing product action runs. |
| PC-5 | In `CoverageCommand`, return 0 for a completed report containing a missing obligation. | `PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes`, `coverage-exit-findings`: wrong public command exit after otherwise valid analysis. |
| PC-6 | In `PlanCoverageReader`, discard unrecognized verification prose instead of reporting it. | `PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5`, `coverage-legacy-v-gaps`: required unmapped clause vanishes from the exact diagnostic subset. |

Literal method filters for Mutation are `/*/*/<Class>*/<method>` using the exact class/method pairs above; each phase selects **1** result on Linux or Windows. No argument expansion, uncontrolled clock, real-time margin or synthetic host load. Record baseline/red/restored counts and first failure label, restoring source/timestamps before the final rebuild. All six remain pending until the post-land companion executes them.

### Test-first order and known flakes

S1 introduces all tests/fixtures and compiling seams. Commit/push and run preparatory CP-1 once as the declared red-first exception to its final S1-S2 boundary: at least the named analyzer diagnostic assertions fail, while R-1 remains green. Implement S2 and commit/push. Build the command through the declared bootstrap allowance below, run static coverage, then final CP-1 once on the clean final source. A changed test or implementation justifies an affected CP-1 rerun with a fresh result directory and stated reason. No separate exploratory baseline build or full-suite run is required.

Known hazards excluded by the exact selection: CARD-0791/0794 (provider readiness/snapshot/broken pipe), CARD-0818 (`CheckpointExecutorLogTests.concurrent_callbacks_append_each_line_once_without_overlap`), CARD-0820 (temp contention/cleanup and `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget`), CARD-0828 (checkpoint ownership/executor races), CARD-0848 (`DetachedLauncherTests.executor_survives_its_starter`), CARD-0879 (AlwaysOn DetectTimeout/PaneClosed), CARD-0889 (Codex submit confirmation under load), CARD-0890 (`C448_V15_RealWorkerDeathRecoversDurableBoundaries`, Linux worker initialization), CARD-0900 (`RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` readiness deadline). CARD-0742, CARD-0757 (`ScaledTimeProviderTests.Speed_10`) and CARD-0751 (`HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines`) remain explicit exclusions from this narrow roster. CARD-0742's individual failure was not re-investigated here; do not use its ID as a blanket waiver.

These hazards cannot excuse a selected test failure. Preserve the established orphan-sweep isolation environment below; adopt CARD-0826's landed replacement if it changes that contract, recording the change before execution. No routine repeat battery. If a genuine nondeterministic failure requires repeats, use at most three normal/two loaded repeats under the owner rule; those are ceilings, not targets. Do not create load for this parser task.

### Checkpoints

Closed ordinary list: one isolated build and one exact filter per row. CP-1 executes through the checkpoint tool, which uses `dotnet run --project tests/Antiphon.Tests --no-build`, never `dotnet test`. One preparatory red and one final green are declared; each is one run of this same row. Markdown `\|` becomes literal `|` in a quoted shell filter; every class operand retains trailing `*`. Counts below are source/design counts until fresh TRX confirms them. Windows's 46-result compatible roster is not a commissioned second run: selected Windows count is 0, not a passing skip.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c891-static/` | plan-coverage-static | `/*/*/(PlanCoverageParserTests*)\|(PlanCoverageAssertionTests*)\|(PlanCoveragePcTests*)\|(PlanCoverageGoldenTests*)\|(PlanCoverageCommandTests*)\|(CheckpointImportTests*)\|(CheckpointManifestTests*)/*` | V-1..V-5, R-1 | Linux 46 = 4+5+4+3+4+20+6, final 0 failed/skipped; Windows 0 selected (portable roster 46) | 46 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c891-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Execution, acceptance and handoff

One checkpoint-tool run per committed slice group. Continue `wait` on the same run while it returns 75; do not start a replacement run or end the task with checks still running. The executor acquires its own build/test leases. The only declared build outside the table is bootstrapping the checkpoint tool itself after a relevant tool change, because coverage must be available before final CP-1; label and report that build separately. Do not hold a wrapper lease around the checkpoint executor and make it acquire a second lease. Slot timeout exit 4 is reported, never bypassed.

Future commands from the committed Code checkout (PowerShell; bootstrap output is a task-owned `bin-` child, not shared default output):

```powershell
$sourceSha = (git rev-parse HEAD).Trim()
# Declared bootstrap exception; lease ends before the checkpoint executor starts.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c891-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c891-tool/ --nologo
# Static analysis only; bootstrap is already complete. Add --tests only as D-1 requires.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c891-tool/ -- coverage --plan docs/superpowers/plans/2026-10-01-card-0891-plan-to-test-coverage-check-plan.md
# One isolated build and exact TUnit filter, leases owned inside the tool.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c891-tool/ -- run --plan docs/superpowers/plans/2026-10-01-card-0891-plan-to-test-coverage-check-plan.md --rows CP-1 --expected-source-sha $sourceSha --max-wait 55s
# Only if the preceding wait yielded 75; use the returned run ID, not a new start.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c891-tool/ -- wait --run <returned-run-id> --max-wait 55s
```

The no-build tool front door avoids an implicit unleased bootstrap; it is not a test-host invocation. Review builds the tool at its reviewed source through the same leased bootstrap and reruns coverage with the exact selection/checklist before its required checkpoint evidence. Final reports preserve the generated CHECKPOINT line, including slot/wait, actual counts, dirty/sourceState/buildSource and reruns; also report OS, exact expected/actual roster and any first failure. TestDesign must make this plan's own final checklist unambiguous before Code uses it as an acceptance input; do not add an analyzer bypass for self-checking.

| Card acceptance / constraint | Evidence required |
|---|---|
| CARD-0891: original V-1/V-4/V-5 gaps reported | V-4 frozen legacy golden with original locations; V-2 prevents setup-only `c866-user` from masking the gap. Unmapped fact prose is printed. No historical execution claim. |
| CARD-0891: fixed requirements report none | V-4 same-plan/same-checklist historical versus corrected source pair, zero missing/unmapped obligations in the fixed V subset; both variants retain the full explicit promise set. |
| Presence extraction and precise output | V-1/V-2/V-5 plus CP-1; every unresolved item identifies original plan location and test association, selected source digests are visible. |
| CARD-0901: static alternative before Review | V-3/V-4 predecessor and method binding checks; all eight original PC risks are visible, PC-23 remains an advisory with unproven dynamic reachability. D-6 is explicitly deferred. |
| No existing behavior regression | R-1's 26 existing tests plus V-5 CLI/side-effect contracts. No runtime scheduler/cleanup/receipt/land changes. Remaining analyzer limitations are explicit findings, never fabricated clean coverage. |
| Code and Review consumption | D-5 owner documentation and the commands above; Code output precedes final checkpoint evidence, Review reruns. Apply the operator's regression-only Review standard. |

### Same-source-area collisions

The caller must refresh `/api/agent-tasks/pipeline`, `/api/session-runners`, `/api/runner-defaults`, `/api/hosts` and board-scoped task scopes before dispatch. Use the lower effective stage/host cap, prefer server2, and defer same-area Code. Do not change budgets or area mappings. These are file/source-area decisions, not occupancy measurements.

| Other work | Exact overlap or independence | Code ordering |
|---|---|---|
| CARD-0885, repeat tooling | Exact shared `tools/Antiphon.Checkpoints/Program.cs` and `docs/testing-and-build.md`; shared importer/CLI area. Its plan also changes `Manifest/{CheckpointManifest,PlanTableImporter,ManifestValidator}.cs`, execution/report/receipt files and stage-code. Those last files stay read-only here. | **Serialize AFTER CARD-0885 lands.** Re-read its landed verb/options/importer and recount existing 20+6 tests. This is an explicit prerequisite, not merely a merge preference. |
| CARD-0826, supplied Code task `3a378666` | Exact shared tool `Program.cs`, `Antiphon.Checkpoints.csproj`, testing owner. Cleanup extraction changes our tool dependency graph. Its frozen plan moves `Cleanup/{ProcessIdentity,RunOwnership,TestRootGuard,ToolCopyCleanup}.cs` into `src/Antiphon.HostCleanup/Checkpoints/`, and modifies caller wiring; the brief's “under Cleanup” shorthand must not determine final paths. | **Serialize AFTER CARD-0826 lands** (also required by 0885's plan). Re-read actual landed paths; do not edit/recreate moved cleanup classes. AddHostCleanup migration, `AgentTaskLandService.cs`, server/runner Program, `PhoneHomeCommandDispatcher.cs`, `deploy-server2.ps1`, stage-code/stage-review remain outside our edits. |
| CARD-0886, heavy test speedup | It edits `CheckpointSourceApprovalTests.cs`, `RunCheckpointSourceScriptTests.cs`, landing harnesses and two checkpoint source-state/execution tests. We read historical versions into new inert fixtures, not those live files, and do not run its heavy filter. | **May run beside** after shared prerequisites land, with narrow scopes and ordinary host slots. No live fixture/helper dependency and no timing experiment in this card. Defer only if its admitted scope broadens into our Program, docs or new test paths. |
| CARD-0788 / CARD-0883 | Their `LandApproval.cs`, `AgentTaskLandService.cs`, `AgentTaskLandSourceResolver.cs`, landing tests/EF work are disjoint from this tool-only implementation. No shared landing harness is consumed here. | **May run beside** once their own 0826 dependency is clear. An application-test glob does not intersect these new Checkpoints files; a broader `tests/**` scope must be rechecked. |
| CARD-0881, effective settings | Endpoint/settings, `server/Bundles/orchestrator.md`, AGENTS and orchestration/ops documentation are outside our footprint. | **May run beside**; no endpoint or bundle dependency. Use today's inspection routes until its loaded contract is verified. |

The docs area is weight-allow in `antiphon.areas.json`, but that does not excuse an exact shared paragraph/file edit with 0885/0826. Keeping bundles, application tests and scripts untouched avoids unrelated shared areas. Plan/TestDesign can proceed while prerequisites are queued.

### Cost, activation and rollback

Estimated final ordinary floor: **8 minutes**, one isolated build/46 Linux results. Declared preparatory red adds **8 minutes**; allow **3 minutes per needed leased tool bootstrap** (normally S1 and S2, 6 total). Ordinary Code verification allowance is therefore **22 minutes**, excluding slot waits and justified repairs. Authoring estimate **100–160 minutes**; dispatch estimate **122–182 minutes**. TestDesign should keep this bounded rather than add full product suites. No measured speedup is claimed.

Normal post-land Mutation: six variants, three phases each, one result per phase; estimate **18 x 3 + 10 = 64 minutes** including custody/discovery/reporting. Final ordinary plus Mutation floor is **72 minutes**, or **86** including preparatory red and two bootstraps; authoring is separate. D-6's optional dynamic-checker cost is additional and not hidden in those totals.

No live server/runner activation or migration is needed for this opt-in developer tool. Use the built tool from the intended source; old detached checkpoint executors keep their own versions. Rollback is a forward commit removing the new verb/analyzer/package/docs while preserving existing commands and archived evidence. No reset, force-push, cleanup sweep, provider state change or deployment is part of this task.

TestDesign's handoff must freeze the syntax-package version, exact historical-to-checklist mappings and fixture source offsets, confirm the 20+26 roster and six intended failure labels, and leave dynamic reachability deferred. Code must wait for 0826 and 0885, preserve the small footprint, commit/push each slice, and return ordinary evidence for separate Review before land.

Plan validation used a read-only Node table scan: all checkpoint header/data rows have 11 cells, the filter has seven intended class operands with trailing wildcards, Min is 46, the new-method count is 20, six PC rows are present, and all 13 requested hazard IDs are included. Whitespace validation found no errors. This was document validation, not execution of the C# importer, TUnit, or any positive control.
