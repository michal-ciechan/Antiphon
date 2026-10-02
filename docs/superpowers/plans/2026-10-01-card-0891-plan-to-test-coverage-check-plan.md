# CARD-0891 / CARD-0901: static plan-to-test coverage check

Date: 2026-10-01; TestDesign freeze: 2026-10-02. Original Plan baseline: `2b4d7313324b6eaeadf6e45fb15808bacfad6263`. TestDesign branch `feat/card-task-fc39aaef` starts at `d15c81541ed00a0ccc11fd28209cf64862f798be`, also the observed remote master at this freeze. No rebase was performed. The evidence reconciliation below supersedes admission assumptions, not D-1..D-6. This stage changes only this file. **Code admission remains conditional on CARD-0826 landing and the post-land reread below.**

## Outcome and scope

Add `coverage --plan <path>` to `tools/Antiphon.Checkpoints`: a read-only, deterministic lint of a plan's promised assertions against its selected C# tests. It reports missing methods, assertion labels and canaries; missing assertion references to promised members; prose it cannot map; and positive-control labels whose preceding assertions lack labels. Bind each obligation to its named test, rather than accepting an occurrence anywhere in the repository.

Ship the inexpensive static alternative explicitly allowed by CARD-0901. A static pass means the stated syntax checks passed, **not that a mutant reached its intended assertion**. Print this distinction in every report. Code runs the command before final checkpoints and pastes its complete output; Review reruns it at the reviewed source. Existing `run`, import, build slots, source receipts and land approval retain their behavior. No automatic new land gate, provider launch, mutation executor or repository-wide plan migration is included.

The operator's 2026-10-01 Final Review rule governs: failure requires an existing-behavior regression or a new reachable fail-open/privacy exposure. Lint findings are evidence to inspect and reconcile, not an independent reason for another Review round. Keep ordinary verification bounded to the changed CLI/parser and existing manifest contracts. The optional executed-reachability extension in D-6 is not commissioned by this plan.

## Evidence and limits

Read CARD-0891 and CARD-0901 in full using `pwsh -NoProfile -File scripts/card.ps1 get <card> -Board Antiphon`. Templates inspected from the frozen `origin/master`: `docs/superpowers/plans/2026-10-01-card-0826-daily-host-cleanup-plan.md` and `2026-10-01-card-0866-disposal-preview-redaction-plan.md`. Owners: `docs/project-context.md`, `docs/testing-and-build.md` (manifest, runner, slots, filters), and `docs/orchestration-loop.md` section 1. This is a Plan delegate reading its own sources; no sub-delegates were dispatched.

Original Plan-stage measurements below are source observations from `git show`, `git rev-parse`, `git diff --name-only`, `git ls-remote`, `rg`, bounded source reads and a Node attribute census. No build, test, mutation, provider, deployment or production settings write was performed in Plan. TestDesign additionally performed the authorized outside-repository package probe recorded below. Executed TUnit counts in both stages: Linux **0**, Windows **0**. All future counts and costs are estimates/rosters, not receipts.

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

Use `Microsoft.CodeAnalysis.CSharp` **5.9.0** for syntax parsing only; the TestDesign package receipt below pins compatibility and the per-project PackageReference convention. Do not load a test assembly, create an MSBuild workspace or restore target projects. Small concrete classes under `Coverage/` own parsing, binding, analysis and output. Invalid C# syntax is invalid input, not an empty assertion index.

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

The unaugmented legacy run must report V-1's missing exclusion for `c866-user`, V-1's absent/unmapped fact clauses, V-4's empty-Foreground requirement distinct from null, and V-5's missing expected IdentityUnproven value. Assert the required diagnostic identities and original line numbers; unrelated legacy ambiguity may also be reported. For the exact red/green pair, **use the same checklist and plan bytes on both historical and corrected source**. The checklist explicitly expands ambiguous clauses to WorkspaceId, TabId, BackendProtocol, ExpiresAtUtc, incarnation/member facts, expected UUIDs, Code and PlannedTerminationPids, and maps empty/null Foreground and the blocker/value requirements. TestDesign freezes the full mapping after checking the historical DTOs. The fixed source variant is the unmodified landed c866 test blob pinned below, with its real assertion syntax and missing inputs; it must not remove promises, add comment-only names or weaken goldens. It reports zero missing/unmapped obligations for this frozen V subset. This does not claim that arbitrary legacy English becomes automatically understood or that every other PC in the full CARD-0866 plan is dynamically proved.

For c835, freeze the V-to-method and eight PC rows from `docs/superpowers/plans/2026-09-30-card-0835-checkpoint-receipt-dirty-tree-plan.md` at `1d994aac952bed0a65506db0bb84742b7e9183e4`, and whole inert source files `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs`, `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs`, `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs`. The expected report contains all eight IDs: PC-5, PC-7A, PC-7B, PC-8, PC-9A, PC-21, PC-22, PC-23. PC-5 is a wrong-method target; the unlabeled predecessors are explicit; PC-23 includes earlier-other-label advisory and unproven reachability. Do not manufacture eight static “unreachable” verdicts to match a historical dynamic claim. Never modify or run those live heavy tests in this card.

Fixtures are loaded from the repository with the existing `CheckpointFixtures.Fixture` path convention, so no test-project resource or Compile change is needed. `.cs.txt` prevents TUnit discovering historical tests. Additional in-memory parser examples are clearly synthetic; both historical and landed-corrected c866 blobs and their digests remain immutable.

## Verification design

### Ordinary roster and regression scope

Twenty new, single-result `[Test]` methods; internal negative/positive fixture pairs are assertions, not additional executions. TestDesign may refine spelling/details but must preserve exact counts or revise this table before Code. Add no broad Unit lane. No clock is needed in the analyzer: source text and digests determine output. For command seams use fixed input/outputs, not elapsed-time assertions. Reuse current owned-temp helpers without their native cleanup tests.

| ID | Class (results) | Exact methods and decisive assertions |
|---|---|---|
| V-1 | `PlanCoverageParserTests` (4 results) | `PlanCoverageParserTests.extracts_verification_obligations_with_original_lines`; label `coverage-parser-items`. Original source locations survive obligation extraction. |
| V-1 | `PlanCoverageParserTests` (continued) | `PlanCoverageParserTests.classifies_tokens_without_promoting_example_values`; label `coverage-parser-roles`. Example values stay excluded from assertion obligations. |
| V-1 | `PlanCoverageParserTests` (continued) | `PlanCoverageParserTests.resolves_closed_test_scope_and_rejects_ambiguity`; label `coverage-scope-closed`. Only complete unambiguous confined selections are accepted. |
| V-1 | `PlanCoverageParserTests` (continued) | `PlanCoverageParserTests.maps_checklist_without_erasing_legacy_requirements`; label `coverage-checklist-additive`. Checklist mappings retain every extracted legacy obligation. |
| V-2 | `PlanCoverageAssertionTests` (5 results) | `PlanCoverageAssertionTests.requires_label_at_bound_assertion`; label `coverage-label-bound`. Labels must occur in supported message arguments of the bound test. |
| V-2 | `PlanCoverageAssertionTests` (continued) | `PlanCoverageAssertionTests.requires_canary_in_assertion_not_setup`; label `coverage-canary-asserted`. Setup literals cannot satisfy exclusion assertions. |
| V-2 | `PlanCoverageAssertionTests` (continued) | `PlanCoverageAssertionTests.requires_members_and_collection_predicates`; label `coverage-empty-distinct`. Null assertions cannot satisfy empty collection obligations. |
| V-2 | `PlanCoverageAssertionTests` (continued) | `PlanCoverageAssertionTests.reports_unmapped_prose_and_opaque_helpers`; label `coverage-unmapped-visible`. Unresolved prose or required helper evidence produces findings. |
| V-2 | `PlanCoverageAssertionTests` (continued) | `PlanCoverageAssertionTests.tracks_literal_helper_arguments_without_executing_source`; label `coverage-helper-literals`. Supported literal helper evidence binds to the calling test. |
| V-3 | `PlanCoveragePcTests` (4 results) | `PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async`; label `coverage-pc-predecessor`. Unlabelled ThrowAsync predecessors remain visible. |
| V-3 | `PlanCoveragePcTests` (continued) | `PlanCoveragePcTests.rejects_label_in_other_method`; label `coverage-pc-method`. Labels from another method cannot satisfy the PC target. |
| V-3 | `PlanCoveragePcTests` (continued) | `PlanCoveragePcTests.reports_earlier_different_label_as_unproven`; label `coverage-pc-other-label`. Earlier different labels produce an advisory with unproven reachability. |
| V-3 | `PlanCoveragePcTests` (continued) | `PlanCoveragePcTests.accepts_labeled_sequence_without_claiming_dynamic_proof`; label `coverage-pc-unproven`. A fully labelled sequence still reports unproven reachability. |
| V-4 | `PlanCoverageGoldenTests` (3 results) | `PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5`; label `coverage-legacy-v-gaps`. The unaugmented historical input retains the required unmapped incarnation clause. |
| V-4 | `PlanCoverageGoldenTests` (continued) | `PlanCoverageGoldenTests.c866_fixed_checks_clear_same_obligations`; label `coverage-fixed-same-obligations`. Identical explicit obligations distinguish historical gaps from corrected assertion evidence. |
| V-4 | `PlanCoverageGoldenTests` (continued) | `PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible`; label `coverage-eight-pc-risks`. All eight historical PC identities retain their specified static findings. |
| V-5 | `PlanCoverageCommandTests` (4 results) | `PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract`; label `coverage-import-preserved`. The new CLI verb preserves the existing importer contract. |
| V-5 | `PlanCoverageCommandTests` (continued) | `PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes`; label `coverage-exit-findings`. A valid analysis with missing obligations returns exit 1. |
| V-5 | `PlanCoverageCommandTests` (continued) | `PlanCoverageCommandTests.invalid_inputs_cannot_produce_clean_summary`; label `coverage-invalid-never-clean`. Invalid inputs return exit 2 without a clean summary. |
| V-5 | `PlanCoverageCommandTests` (continued) | `PlanCoverageCommandTests.coverage_never_starts_driver_or_writes_run_state`; label `coverage-no-side-effects`. Coverage leaves driver calls and run-state writes at zero. |
| R-1 | `CheckpointImportTests` (20 existing), `CheckpointManifestTests` (6 existing) | All 26 class-qualified methods in the checklist are required. |

The scenario matrix is unchanged. Parser cases cover escapes/fences, CRLF, excluded mutation/input values, partial classes, class OR, explicit-path omissions, invalid/empty selection and additive mappings. Assertion cases cover Shouldly signatures, comments/other-method/setup decoys, exact enum expected values, aliased members, literal helper witness/canary arrays and cycles. PC cases check status and plan/test locations for every PC. Golden cases pin provenance and required diagnostic identities plus the identical-plan/checklist red/green pair. Command cases invoke the public CLI in process and compare text/JSON decisions, digest changes, stable order and malformed/read/path failures; their fail-fast driver seam is supplemented by a before/after directory inventory. Existing R-1 methods preserve imported tables, escaped filters, environment, serial, reuse/After, per-OS counts, YAML round-trip and validation. Each row's label belongs to the substantive assertion for that method's contract; a label on an unrelated status check is insufficient. These details refine the same 20 results, not a new lane.

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

### Frozen final acceptance checklist

This is this plan's one active plan-coverage-v1 block. It binds all 20 new methods, their 20 substantive assertion labels, all 26 existing methods and the six PC targets to the rows above. Run the public coverage verb against this full plan with normal test selection; do not suppress extraction, skip a row, special-case this card or supply an empty replacement checklist. The fixture checklist later in this document is ordinary JSON outside Verification design and is not this plan's acceptance input. If rows move in Code, update these original 1-based planLine values in the same committed edit.

```plan-coverage-v1
{
  "version": 1,
  "items": [
    {"id":"V-1","test":"PlanCoverageParserTests.extracts_verification_obligations_with_original_lines","kind":"method","name":"PlanCoverageParserTests.extracts_verification_obligations_with_original_lines","planLine":158},
    {"id":"V-1","test":"PlanCoverageParserTests.extracts_verification_obligations_with_original_lines","kind":"label","name":"coverage-parser-items","planLine":158,"maps":"Original source locations survive obligation extraction."},
    {"id":"V-1","test":"PlanCoverageParserTests.classifies_tokens_without_promoting_example_values","kind":"method","name":"PlanCoverageParserTests.classifies_tokens_without_promoting_example_values","planLine":159},
    {"id":"V-1","test":"PlanCoverageParserTests.classifies_tokens_without_promoting_example_values","kind":"label","name":"coverage-parser-roles","planLine":159,"maps":"Example values stay excluded from assertion obligations."},
    {"id":"V-1","test":"PlanCoverageParserTests.resolves_closed_test_scope_and_rejects_ambiguity","kind":"method","name":"PlanCoverageParserTests.resolves_closed_test_scope_and_rejects_ambiguity","planLine":160},
    {"id":"V-1","test":"PlanCoverageParserTests.resolves_closed_test_scope_and_rejects_ambiguity","kind":"label","name":"coverage-scope-closed","planLine":160,"maps":"Only complete unambiguous confined selections are accepted."},
    {"id":"V-1","test":"PlanCoverageParserTests.maps_checklist_without_erasing_legacy_requirements","kind":"method","name":"PlanCoverageParserTests.maps_checklist_without_erasing_legacy_requirements","planLine":161},
    {"id":"V-1","test":"PlanCoverageParserTests.maps_checklist_without_erasing_legacy_requirements","kind":"label","name":"coverage-checklist-additive","planLine":161,"maps":"Checklist mappings retain every extracted legacy obligation."},
    {"id":"V-2","test":"PlanCoverageAssertionTests.requires_label_at_bound_assertion","kind":"method","name":"PlanCoverageAssertionTests.requires_label_at_bound_assertion","planLine":162},
    {"id":"V-2","test":"PlanCoverageAssertionTests.requires_label_at_bound_assertion","kind":"label","name":"coverage-label-bound","planLine":162,"maps":"Labels must occur in supported message arguments of the bound test."},
    {"id":"V-2","test":"PlanCoverageAssertionTests.requires_canary_in_assertion_not_setup","kind":"method","name":"PlanCoverageAssertionTests.requires_canary_in_assertion_not_setup","planLine":163},
    {"id":"V-2","test":"PlanCoverageAssertionTests.requires_canary_in_assertion_not_setup","kind":"label","name":"coverage-canary-asserted","planLine":163,"maps":"Setup literals cannot satisfy exclusion assertions."},
    {"id":"V-2","test":"PlanCoverageAssertionTests.requires_members_and_collection_predicates","kind":"method","name":"PlanCoverageAssertionTests.requires_members_and_collection_predicates","planLine":164},
    {"id":"V-2","test":"PlanCoverageAssertionTests.requires_members_and_collection_predicates","kind":"label","name":"coverage-empty-distinct","planLine":164,"maps":"Null assertions cannot satisfy empty collection obligations."},
    {"id":"V-2","test":"PlanCoverageAssertionTests.reports_unmapped_prose_and_opaque_helpers","kind":"method","name":"PlanCoverageAssertionTests.reports_unmapped_prose_and_opaque_helpers","planLine":165},
    {"id":"V-2","test":"PlanCoverageAssertionTests.reports_unmapped_prose_and_opaque_helpers","kind":"label","name":"coverage-unmapped-visible","planLine":165,"maps":"Unresolved prose or required helper evidence produces findings."},
    {"id":"V-2","test":"PlanCoverageAssertionTests.tracks_literal_helper_arguments_without_executing_source","kind":"method","name":"PlanCoverageAssertionTests.tracks_literal_helper_arguments_without_executing_source","planLine":166},
    {"id":"V-2","test":"PlanCoverageAssertionTests.tracks_literal_helper_arguments_without_executing_source","kind":"label","name":"coverage-helper-literals","planLine":166,"maps":"Supported literal helper evidence binds to the calling test."},
    {"id":"V-3","test":"PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async","kind":"method","name":"PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async","planLine":167},
    {"id":"V-3","test":"PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async","kind":"label","name":"coverage-pc-predecessor","planLine":167,"maps":"Unlabelled ThrowAsync predecessors remain visible."},
    {"id":"V-3","test":"PlanCoveragePcTests.rejects_label_in_other_method","kind":"method","name":"PlanCoveragePcTests.rejects_label_in_other_method","planLine":168},
    {"id":"V-3","test":"PlanCoveragePcTests.rejects_label_in_other_method","kind":"label","name":"coverage-pc-method","planLine":168,"maps":"Labels from another method cannot satisfy the PC target."},
    {"id":"V-3","test":"PlanCoveragePcTests.reports_earlier_different_label_as_unproven","kind":"method","name":"PlanCoveragePcTests.reports_earlier_different_label_as_unproven","planLine":169},
    {"id":"V-3","test":"PlanCoveragePcTests.reports_earlier_different_label_as_unproven","kind":"label","name":"coverage-pc-other-label","planLine":169,"maps":"Earlier different labels produce an advisory with unproven reachability."},
    {"id":"V-3","test":"PlanCoveragePcTests.accepts_labeled_sequence_without_claiming_dynamic_proof","kind":"method","name":"PlanCoveragePcTests.accepts_labeled_sequence_without_claiming_dynamic_proof","planLine":170},
    {"id":"V-3","test":"PlanCoveragePcTests.accepts_labeled_sequence_without_claiming_dynamic_proof","kind":"label","name":"coverage-pc-unproven","planLine":170,"maps":"A fully labelled sequence still reports unproven reachability."},
    {"id":"V-4","test":"PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5","kind":"method","name":"PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5","planLine":171},
    {"id":"V-4","test":"PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5","kind":"label","name":"coverage-legacy-v-gaps","planLine":171,"maps":"The unaugmented historical input retains the required unmapped incarnation clause."},
    {"id":"V-4","test":"PlanCoverageGoldenTests.c866_fixed_checks_clear_same_obligations","kind":"method","name":"PlanCoverageGoldenTests.c866_fixed_checks_clear_same_obligations","planLine":172},
    {"id":"V-4","test":"PlanCoverageGoldenTests.c866_fixed_checks_clear_same_obligations","kind":"label","name":"coverage-fixed-same-obligations","planLine":172,"maps":"Identical explicit obligations distinguish historical gaps from corrected assertion evidence."},
    {"id":"V-4","test":"PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible","kind":"method","name":"PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible","planLine":173},
    {"id":"V-4","test":"PlanCoverageGoldenTests.c835_eight_pc_risks_are_visible","kind":"label","name":"coverage-eight-pc-risks","planLine":173,"maps":"All eight historical PC identities retain their specified static findings."},
    {"id":"V-5","test":"PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract","kind":"method","name":"PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract","planLine":174},
    {"id":"V-5","test":"PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract","kind":"label","name":"coverage-import-preserved","planLine":174,"maps":"The new CLI verb preserves the existing importer contract."},
    {"id":"V-5","test":"PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes","kind":"method","name":"PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes","planLine":175},
    {"id":"V-5","test":"PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes","kind":"label","name":"coverage-exit-findings","planLine":175,"maps":"A valid analysis with missing obligations returns exit 1."},
    {"id":"V-5","test":"PlanCoverageCommandTests.invalid_inputs_cannot_produce_clean_summary","kind":"method","name":"PlanCoverageCommandTests.invalid_inputs_cannot_produce_clean_summary","planLine":176},
    {"id":"V-5","test":"PlanCoverageCommandTests.invalid_inputs_cannot_produce_clean_summary","kind":"label","name":"coverage-invalid-never-clean","planLine":176,"maps":"Invalid inputs return exit 2 without a clean summary."},
    {"id":"V-5","test":"PlanCoverageCommandTests.coverage_never_starts_driver_or_writes_run_state","kind":"method","name":"PlanCoverageCommandTests.coverage_never_starts_driver_or_writes_run_state","planLine":177},
    {"id":"V-5","test":"PlanCoverageCommandTests.coverage_never_starts_driver_or_writes_run_state","kind":"label","name":"coverage-no-side-effects","planLine":177,"maps":"Coverage leaves driver calls and run-state writes at zero."},
    {"id":"R-1","test":"CheckpointImportTests.imports_the_card_0688_table","kind":"method","name":"CheckpointImportTests.imports_the_card_0688_table","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.unescapes_pipes_in_filters","kind":"method","name":"CheckpointImportTests.unescapes_pipes_in_filters","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.maps_cp_reuse_to_the_same_build","kind":"method","name":"CheckpointImportTests.maps_cp_reuse_to_the_same_build","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.command_rows_become_command_checkpoints","kind":"method","name":"CheckpointImportTests.command_rows_become_command_checkpoints","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.derives_roster_tokens_from_the_filter","kind":"method","name":"CheckpointImportTests.derives_roster_tokens_from_the_filter","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.refuses_the_legacy_eight_column_table","kind":"method","name":"CheckpointImportTests.refuses_the_legacy_eight_column_table","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.warns_when_estimate_exceeds_row_timeout","kind":"method","name":"CheckpointImportTests.warns_when_estimate_exceeds_row_timeout","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.imports_the_card_0723_table","kind":"method","name":"CheckpointImportTests.imports_the_card_0723_table","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.serial_import_round_trips_and_excludes_other_rows","kind":"method","name":"CheckpointImportTests.serial_import_round_trips_and_excludes_other_rows","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.environment_map_round_trips_without_changing_serial","kind":"method","name":"CheckpointImportTests.environment_map_round_trips_without_changing_serial","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns","kind":"method","name":"CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.scheduler_passes_test_parallel_limit_only_to_serial_rows_process","kind":"method","name":"CheckpointImportTests.scheduler_passes_test_parallel_limit_only_to_serial_rows_process","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.invalid_serial_is_refused_before_execution","kind":"method","name":"CheckpointImportTests.invalid_serial_is_refused_before_execution","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.unknown_duplicate_and_malformed_columns_are_refused","kind":"method","name":"CheckpointImportTests.unknown_duplicate_and_malformed_columns_are_refused","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.invalid_environment_names_and_duplicates_are_refused","kind":"method","name":"CheckpointImportTests.invalid_environment_names_and_duplicates_are_refused","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.annotated_reuse_resolves_only_an_earlier_filter_build","kind":"method","name":"CheckpointImportTests.annotated_reuse_resolves_only_an_earlier_filter_build","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.filter_shorthand_and_trailing_environment_prose_are_refused","kind":"method","name":"CheckpointImportTests.filter_shorthand_and_trailing_environment_prose_are_refused","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.cross_after_reuse_is_refused_without_a_relaxation_flag","kind":"method","name":"CheckpointImportTests.cross_after_reuse_is_refused_without_a_relaxation_flag","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.conflicting_projects_cannot_share_an_output_identity","kind":"method","name":"CheckpointImportTests.conflicting_projects_cannot_share_an_output_identity","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointImportTests.run_plan_and_import_resolve_the_committed_table_identically","kind":"method","name":"CheckpointImportTests.run_plan_and_import_resolve_the_committed_table_identically","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointManifestTests.parses_yaml_and_defaults","kind":"method","name":"CheckpointManifestTests.parses_yaml_and_defaults","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointManifestTests.rejects_backslash_or_trailing_space_output_path","kind":"method","name":"CheckpointManifestTests.rejects_backslash_or_trailing_space_output_path","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointManifestTests.rejects_duplicate_or_unknown_build_ids","kind":"method","name":"CheckpointManifestTests.rejects_duplicate_or_unknown_build_ids","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointManifestTests.rejects_reuse_across_different_after","kind":"method","name":"CheckpointManifestTests.rejects_reuse_across_different_after","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointManifestTests.after_selector_parses_ranges_and_all","kind":"method","name":"CheckpointManifestTests.after_selector_parses_ranges_and_all","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"R-1","test":"CheckpointManifestTests.results_root_must_be_under_worktree","kind":"method","name":"CheckpointManifestTests.results_root_must_be_under_worktree","planLine":178,"maps":"All 26 class-qualified methods in the checklist are required."},
    {"id":"PC-1","test":"PlanCoverageParserTests.extracts_verification_obligations_with_original_lines","kind":"label","name":"coverage-parser-items","planLine":190},
    {"id":"PC-2","test":"PlanCoverageAssertionTests.requires_canary_in_assertion_not_setup","kind":"label","name":"coverage-canary-asserted","planLine":191},
    {"id":"PC-3","test":"PlanCoverageAssertionTests.requires_members_and_collection_predicates","kind":"label","name":"coverage-empty-distinct","planLine":192},
    {"id":"PC-4","test":"PlanCoveragePcTests.reports_unlabelled_predecessor_including_throw_async","kind":"label","name":"coverage-pc-predecessor","planLine":193},
    {"id":"PC-5","test":"PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes","kind":"label","name":"coverage-exit-findings","planLine":194},
    {"id":"PC-6","test":"PlanCoverageGoldenTests.c866_legacy_reports_v1_v4_v5","kind":"label","name":"coverage-legacy-v-gaps","planLine":195}
  ]
}
```

For the six PCs, the target is the first assertion executed after arranging/acting on the affected input, with no earlier Shouldly checks hidden in test helpers. The required first detecting assertions are: PC-1 exact obligation/location sequence equality (coverage-parser-items); PC-2 presence of the setup-only missing-canary obligation (coverage-canary-asserted); PC-3 presence of the empty-Foreground gap for null-only input (coverage-empty-distinct); PC-4 presence of the ThrowAsync predecessor with its source location (coverage-pc-predecessor); PC-5 public CLI exit equals 1 on otherwise valid findings (coverage-exit-findings); PC-6 presence of the unmapped incarnation-facts diagnostic at original c866 plan line 206 on the **unaugmented** legacy run (coverage-legacy-v-gaps). Assert those invariants before summary counts/status conveniences. PC-6 does not use the checklist-augmented run, which intentionally maps that clause.

These six distinct defects and labels remain the positive-control table's closed list. Ordering is a frozen authoring requirement, not an inspection of tests that do not yet exist; Code and Review must inspect the implemented first assertion, and post-land Mutation must demonstrate each intended failure. D-6 dynamic-checker work stays deferred.

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

The no-build tool front door avoids an implicit unleased bootstrap; it is not a test-host invocation. Review builds the tool at its reviewed source through the same leased bootstrap and reruns coverage with the exact selection/checklist before its required checkpoint evidence. Final reports preserve the generated CHECKPOINT line, including slot/wait, actual counts, dirty/sourceState/buildSource and reruns; also report OS, exact expected/actual roster and any first failure. The frozen final acceptance checklist above is the self-check input: require zero missing/unmapped obligations and zero PC issues, with all six PC statuses static-labeled and reachability=unproven. Preserve any advisories with a disposition; do not add an analyzer bypass for self-checking.

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
| CARD-0885, repeat tooling | Exact shared `tools/Antiphon.Checkpoints/Program.cs` and `docs/testing-and-build.md`; shared importer/CLI area. Its plan also changes `Manifest/{CheckpointManifest,PlanTableImporter,ManifestValidator}.cs`, execution/report/receipt files and stage-code. Those last files stay read-only here. | **CARD-0885 prerequisite satisfied at 80867765.** The freeze below rereads its landed options/importer and confirms 20+6. Preserve that code and repeat the integration census after CARD-0826 lands. |
| CARD-0826, replacement Code task `863ee584` | Exact shared tool `Program.cs`, `Antiphon.Checkpoints.csproj`, testing owner. Cleanup extraction changes our tool dependency graph. Its frozen plan moves `Cleanup/{ProcessIdentity,RunOwnership,TestRootGuard,ToolCopyCleanup}.cs` into `src/Antiphon.HostCleanup/Checkpoints/`, and modifies caller wiring; the brief's “under Cleanup” shorthand must not determine final paths. | **Serialize AFTER CARD-0826 lands** (also required by 0885's plan). Re-read actual landed paths; do not edit/recreate moved cleanup classes. AddHostCleanup migration, `AgentTaskLandService.cs`, server/runner Program, `PhoneHomeCommandDispatcher.cs`, `deploy-server2.ps1`, stage-code/stage-review remain outside our edits. |
| CARD-0886, heavy test speedup | It edits `CheckpointSourceApprovalTests.cs`, `RunCheckpointSourceScriptTests.cs`, landing harnesses and two checkpoint source-state/execution tests. We read historical versions into new inert fixtures, not those live files, and do not run its heavy filter. | **May run beside** after shared prerequisites land, with narrow scopes and ordinary host slots. No live fixture/helper dependency and no timing experiment in this card. Defer only if its admitted scope broadens into our Program, docs or new test paths. |
| CARD-0788 / CARD-0883 | Their `LandApproval.cs`, `AgentTaskLandService.cs`, `AgentTaskLandSourceResolver.cs`, landing tests/EF work are disjoint from this tool-only implementation. No shared landing harness is consumed here. | **May run beside** once their own 0826 dependency is clear. An application-test glob does not intersect these new Checkpoints files; a broader `tests/**` scope must be rechecked. |
| CARD-0881, effective settings | Endpoint/settings, `server/Bundles/orchestrator.md`, AGENTS and orchestration/ops documentation are outside our footprint. | **May run beside**; no endpoint or bundle dependency. Use today's inspection routes until its loaded contract is verified. |

The docs area is weight-allow in `antiphon.areas.json`, but that does not excuse an exact shared paragraph/file edit with 0885/0826. Keeping bundles, application tests and scripts untouched avoids unrelated shared areas. Plan/TestDesign can proceed while prerequisites are queued.

### Cost, activation and rollback

Estimated final ordinary floor: **8 minutes**, one isolated build/46 Linux results. Declared preparatory red adds **8 minutes**; allow **3 minutes per needed leased tool bootstrap** (normally S1 and S2, 6 total). Ordinary Code verification allowance is therefore **22 minutes**, excluding slot waits and justified repairs. Authoring estimate **100–160 minutes**; dispatch estimate **122–182 minutes**. TestDesign should keep this bounded rather than add full product suites. No measured speedup is claimed.

Normal post-land Mutation: six variants, three phases each, one result per phase; estimate **18 x 3 + 10 = 64 minutes** including custody/discovery/reporting. Final ordinary plus Mutation floor is **72 minutes**, or **86** including preparatory red and two bootstraps; authoring is separate. D-6's optional dynamic-checker cost is additional and not hidden in those totals.

No live server/runner activation or migration is needed for this opt-in developer tool. Use the built tool from the intended source; old detached checkpoint executors keep their own versions. Rollback is a forward commit removing the new verb/analyzer/package/docs while preserving existing commands and archived evidence. No reset, force-push, cleanup sweep, provider state change or deployment is part of this task.

The TestDesign freeze below supplies the package, mappings, fixture coordinates and exact roster. Dynamic reachability remains deferred. Code must satisfy the post-0826 admission condition below, preserve the small footprint, commit/push each slice, and return ordinary evidence for separate Review before land.

Plan validation used a read-only Node table scan: all checkpoint header/data rows have 11 cells, the filter has seven intended class operands with trailing wildcards, Min is 46, the new-method count is 20, six PC rows are present, and all 13 requested hazard IDs are included. Whitespace validation found no errors. This was document validation, not execution of the C# importer, TUnit, or any positive control.

## TestDesign freeze evidence (2026-10-02)

CARD-0891 and CARD-0901 were read in full with `scripts/card.ps1 get <card> -Board Antiphon`. Repository observations use `git show`, `git log`, `git diff`, `git ls-remote`, `rg` and a source-attribute census, not test discovery. Repository builds/tests: **0**; positive-control executions: **0**. The only compiled program was the commissioned disposable package probe below. No analyzer or tests were implemented by TestDesign.

### Package receipt

Pin `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" />` in `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj`. On 2026-10-02 the [NuGet flat-container feed](https://api.nuget.org/v3-flatcontainer/microsoft.codeanalysis.csharp/index.json) returned **5.9.0** as its greatest stable version (exclude versions containing `-`); the preceding stable versions were 5.6.0 and 5.3.0. The [package framework/dependency metadata](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/5.9.0) includes netstandard2.0, which the actual net9.0 restore selected. This is a syntax API dependency, not a workspace/compiler-execution feature.

Checked at `d15c81541ed00a0ccc11fd28209cf64862f798be`: the tool explicitly targets `net9.0`, already pins YamlDotNet 16.3.0 in its own project, and there is no tracked `Directory.Packages.props` or `ManagePackageVersionsCentrally` setting. `Directory.Build.props` does not supply a target-framework/package-version override. Follow the existing per-project convention; do not introduce central package management for this change.

Probe custody: `mktemp -d /tmp/c891-roslyn-XXXXXX` printed **`/tmp/c891-roslyn-5CuLvX`**. Only that outside-repository directory held Parse.csproj, Program.cs, packages, obj and bin. The project targeted net9.0 with the exact PackageReference above; SDK was 10.0.401. Commands were wrapped individually by `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c891-scratch-<restore|build|parse> -- ...`; all three acquired host slots immediately (maxCpuCount 6).

- Restore: `dotnet restore /tmp/c891-roslyn-5CuLvX/Parse.csproj --packages /tmp/c891-roslyn-5CuLvX/packages --source https://api.nuget.org/v3/index.json --nologo`. The resulting assets target was net9.0, with compile/runtime asset `lib/netstandard2.0/Microsoft.CodeAnalysis.CSharp.dll` at 5.9.0 and an empty restore-diagnostic list.
- Build: `dotnet build /tmp/c891-roslyn-5CuLvX/Parse.csproj --no-restore --nologo -nodeReuse:false --property:UseSharedCompilation=false`; exit 0, **0 warnings, 0 errors**, 8.07 seconds reported by MSBuild.
- The one source file called `CSharpSyntaxTree.ParseText` on `class Example { void Check() { actual.ShouldBe(42, "coverage-parse"); } }`, counted error diagnostics and InvocationExpressionSyntax nodes, and returned nonzero unless they were 0 and 1 respectively. `dotnet /tmp/c891-roslyn-5CuLvX/bin/Debug/net9.0/Parse.dll` returned 0 and printed `syntax errors=0 invocation count=1`. No semantic compilation of that input occurred: the deliberately unresolved actual/Shouldly symbols demonstrate syntax-only use.
- After all probe processes exited, `rm -r /tmp/c891-roslyn-5CuLvX` removed that literal owned path. No repository project/output was touched. This is one package compatibility probe, not a CP-1 or TUnit receipt.

### Post-0885 baseline and remaining admission condition

CARD-0885 is contained at **`808677658cc418dc439d906de4526aaea32e231a`**. Remote master was **`d15c81541ed00a0ccc11fd28209cf64862f798be`**; the checkout is that commit even though the local origin/master ref still pointed to 80867765. `git diff 80867765 d15c8154 -- tools/Antiphon.Checkpoints tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs tests/Antiphon.Tests/Checkpoints/CheckpointManifestTests.cs docs/testing-and-build.md` was empty. Thus the current-master census below also describes the landed 0885 tool; no fetch/rebase of this task branch is needed.

The public verbs at this source are run, start, wait, status, stop, report, validate, import, row, clean and execute; help/version switches and the existing hold/smoke-detach plumbing are also present. Coverage is absent. Program's named options are after, baseline, clean-on-red, dotnet, dry-run, evidence, expect, expected-repeat, expected-source-sha, filter, heartbeat, json, keep-outputs, known-flaky, manifest, max-wait, merge, min-executed, msbuild-property, name, no-build, older-than, out, output-path, parallel, plan, project, repeat, repo-root, results-root, row-timeout, rows, run, serial, slots and total-timeout; hold separately reads parent. `ArgSet.GetAll` retains repeated values. Preserve these paths and their repeat/source-receipt behavior. In particular run/start selection and row accept repeat, validate accepts expected-repeat, and imported manifests now carry repeat through the existing Manifest/Execution/Report/State/Trx implementation. PlanTableImporter accepts optional EstimatedMinutesWindows, Serial, Environment and Repeat columns; CheckpointRow.Repeat defaults to 1. The validator refuses nonpositive/overflowing Repeat x Min, repeated command rows and a reused build with a different repeat. Tool reports use schema 3 for the existing execution receipt; the new coverage JSON remains its separate schemaVersion 1. The new verb's format/checklist/tests options do not replace these options.

The exact existing class census is still **CheckpointImportTests 20 + CheckpointManifestTests 6**, each single-result Test with no Arguments. New roster remains **4+5+4+3+4=20**. **Delta: 0 existing results, 0 new results, 0 checkpoint rows, 0 estimated minutes.** Keep CP-1 Min/Expect 46 and all Cost numbers (8 final, 8 preparatory, 6 bootstrap, 22 ordinary; 64 Mutation; 72/86 combined) unchanged. Repeat support adds no repeat battery to this task.

**Code start condition:** first confirm CARD-0826's complete required cleanup extraction/caller wiring has landed on the integration target (the brief identifies replacement Code task `863ee584`, replaying slice 1; a running task or an S1 commit is not land evidence). Start Code from a target containing both that land and 80867765, carrying this frozen plan forward through the normal task/landing flow. Re-read the actual landed tool before editing it; do not rebase this pushed TestDesign branch. Until that condition holds, Code is deferred, not authorized to reconstruct the future dependency graph from this plan.

That reread must cover tool Program/project, CheckpointApp, Commands/WaitCommand, Evidence/EvidenceFolder, State/RunStateStore, current Manifest classes, the testing owner and the landed temp helpers. CARD-0826 S2 moves Cleanup/{ProcessIdentity,RunOwnership,TestRootGuard,ToolCopyCleanup}.cs to **src/Antiphon.HostCleanup/Checkpoints/**, adds its project reference and threads cleanup dependencies through those callers (its plan lines 165, 197, 208 and 842). Do not re-add old Cleanup files or edit the new library. Our only existing Code edit files remain **Program.cs, Antiphon.Checkpoints.csproj and docs/testing-and-build.md**, on top of both predecessors; CheckpointApp/Manifest/cleanup wiring stays read-only.

At admission recount: the exact Import/Manifest method lists below, their Test/Arguments/data-source/skip attributes and any additional classes matched by the seven trailing-wildcard operands; confirm the 20 planned methods still contribute one each. Recheck Program verbs/options/repeat behavior, importer API and table columns, runtime/driver seam, package convention/target framework, tool project references, owned-temp helper and orphan-sweep environment. Record any roster delta in Expect/Min and any evidenced cost delta before running Code checkpoints. **Only these integration-dependent observations remain conditional on CARD-0826.** The NuGet pin/probe, immutable historical fixture bytes/mappings, new 20-method design, six PC labels and D-6 deferral do not depend on that land.

### Immutable raw fixture coordinates

Full revisions: historical c866 **1400463a48fc355eba8be3c51236cc809c1c119f**; corrected c866 landed **1485abaabf614b5d5e21d81314df31c879f39b0c**; historical c835 **1d994aac952bed0a65506db0bb84742b7e9183e4**. `git log --all -- <test-path>` and `git show <sha>:<path>` confirm the revisions. Copy Git blob bytes, not a platform-normalized checkout or line-numbered command output. Line ranges below are 1-based inclusive; byte ranges are zero-based half-open. All full blobs include the final LF; synthetic wrapper offset is **0**.

| Fixture | Revision / original path | Lines / bytes | SHA-256 of raw blob |
|---|---|---|---|
| c866-plan.md.txt | historical c866; docs/superpowers/plans/2026-10-01-card-0866-disposal-preview-redaction-plan.md | 1–359 / [0,69779) | c4f1382cbe489a0cb67fd08806a30211ffd5d215b2a1a0681b70b3647354d5e9 |
| c866-tests.cs.txt | historical c866; tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalRedactionTests.cs | 1–423 / [0,24121) | 592110809696d50eb7d445303fb6cf09a4fef5d37ec008ea1b648823af85b8f0 |
| c866-fixed-tests.cs.txt | corrected c866; same test path | 1–571 / [0,35935) | f3f651547b707027da6c7f0149e8298a87efccb807a30a20f3939ca0a2e6822a |
| c835-plan.md.txt | historical c835; docs/superpowers/plans/2026-09-30-card-0835-checkpoint-receipt-dirty-tree-plan.md | 1–757 / [0,71301) | eb888f2036444c4f1584b8a127d761b10fed4bf62530981a25bb41353d2c03fc |
| c835-state.cs.txt | historical c835; tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs | 1–352 / [0,17760) | 1a026b8521a362973e41a358292b8e19c3c5e7920264224decb17ba5ca7e3693 |
| c835-script.cs.txt | historical c835; tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs | 1–453 / [0,27004) | cf8109841f2b705d3379be906a0671a6347f6b81ff0e1d03ee9c3225b202f112 |
| c835-approval.cs.txt | historical c835; tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs | 1–475 / [0,26442) | 63d442f3f242944c24a82e40c4a5a93a978ad3c25cb8e2bc3ab0d2d2bae18dd7 |

Keep whole raw plan fixtures. For the pure analyzer's **explicitly scoped** c866 V-1/V-4/V-5 golden input, retain original lines 128 (Verification heading), 188–206 (V-1 paragraph/matrix), 210 (V-4 paragraph), 214–218 (V table heading/header/V-1), 221–222 (V-4/V-5 rows), 227–231 (V-5 scenarios), and 313–322 (checkpoint context). Replace other lines with their newline only in the in-memory projection, retaining original line numbers and recording these ranges in provenance.json. Both historical/fixed analyses receive identical projected plan bytes and identical checklist bytes; importer-contract tests may separately use the full raw plan. This scope is fixture construction under the existing in-memory seam, not a production option to suppress obligations. Never call a projected result a clean report for the entire historical plan.

Historical c866 assertion regions: V-1 21–76; V-4 178–249; V-5 251–293; Wire 325–402, including RoundTrip 352–383, AssertPreviewPaths 384–389 and the exclusion array/assertion 390–397. Corrected regions: AssertV1Facts 21–62, V-1 64–134, V-4 256–355, V-5 357–409, Wire 447–550, AssertPreviewPaths 524–529, AssertCanaryAbsent/AssertCanary 530–537, AssertNoPath 538–545. These are navigation offsets, not permission to drop helpers from the full inert sources.

For c835 retain in-memory original lines 248, 388–394, 399, 401–402, 413–415, 519–529, 535, 537–538, 540–541, 559–561 and 711–731; blank other lines as above. This binds exactly the eight commissioned PCs using the original V/method rows and all necessary checkpoint context. Full-legacy analysis can report other prose, but its findings cannot replace these eight golden identities.

| c835 PC / plan line | Bound class.method | Raw detecting location and required static observation |
|---|---|---|
| PC-5 / 535 | CheckpointSourceStateTests.script_and_tool_snapshots_agree | Method begins 205; fixed-vector-parity is at 60 in clean_and_ignored_outputs_match_head (begins 36). PC_LABEL_NOT_IN_METHOD; missing-target. |
| PC-7A / 537 | RunCheckpointSourceScriptTests.C835_StrictAdmission | Target 67; predecessors 50, 53 and 64 use runtime Output, with other stable labels at 54–56/65–66. unlabeled-predecessor. |
| PC-7B / 538 | RunCheckpointSourceScriptTests.C835_StrictAdmission | Target 56; runtime-message predecessors 50/53 and different label 54/55. unlabeled-predecessor. |
| PC-8 / 540 | RunCheckpointSourceScriptTests.C835_DriftAndReuse | Target 143; predecessors include 97/99/106/118/142 with runtime messages and unlabeled 112. unlabeled-predecessor. |
| PC-9A / 541 | RunCheckpointSourceScriptTests.C835_DriftAndReuse | Target 119; predecessors include 97/99/106/112/118. unlabeled-predecessor. |
| PC-21 / 559 | CheckpointSourceApprovalTests.settlement_persists_source_assertion | Target 37; unlabeled 31/32 and earlier settlement label at 33. unlabeled-predecessor. |
| PC-22 / 560 | CheckpointSourceApprovalTests.land_admission_requires_clean_review_source | Target 72; Should.ThrowAsync at 70–71 has no message (also lexical assertions at 64–66). unlabeled-predecessor. |
| PC-23 / 561 | CheckpointSourceApprovalTests.recovery_and_resume_recheck_source_assertion | Targets 136–137 and 144–147; earlier stable ordinary-case label begins 93 and is used at 100/105/108/128–135. Include PC_EARLIER_OTHER_LABEL, earlier locations and reachability=unproven; conditional target alternatives remain explicit. Do not call the static evidence a proven dynamic failure. |

### Frozen c866 checklist and interpretation

Save the following exact version-1 object as c866-checklist.json. It is a fixture specification, deliberately outside this plan's Verification design. Every test is class-qualified; planLine addresses the **historical c866 plan**, not this document. The same object is required for historical and corrected sources. The unaugmented run has no checklist and must still report the incarnation-facts ambiguity. The augmented historical run must retain, at minimum, missing c866-user exclusion (V-1/206), missing WorkspaceId/TabId/BackendProtocol/ExpiresAtUtc assertions (V-1/206), missing empty Foreground (V-4/210), and missing Blockers/IdentityUnproven evidence (V-5/229). The corrected V subset must clear those same obligations.

```json
{
  "version": 1,
  "items": [
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"method","name":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","planLine":188},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"method","name":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","planLine":210},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"Claims","planLine":210,"maps":"Exercise raw exact-claim metadata in an eligible occupied subcase."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"SessionId","planLine":210,"maps":"Exercise raw exact-claim metadata in an eligible occupied subcase."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"Live","planLine":210,"maps":"Exercise raw exact-claim metadata in an eligible occupied subcase."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"ChildPid","planLine":210,"maps":"Exercise raw exact-claim metadata in an eligible occupied subcase."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"ChildStartedAtUtc","planLine":210,"maps":"Exercise raw exact-claim metadata in an eligible occupied subcase."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"PaneLabel","planLine":210,"maps":"Preserve optional nulls and empty collections distinctly."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"Origin","planLine":210,"maps":"Preserve optional nulls and empty collections distinctly."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"AgentKind","planLine":210,"maps":"Preserve optional nulls and empty collections distinctly."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"ExecutableName","planLine":210,"maps":"Preserve optional nulls and empty collections distinctly."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"Foreground","planLine":210,"maps":"Preserve optional nulls and empty collections distinctly."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"empty","name":"Foreground","planLine":210,"maps":"Preserve optional nulls and empty collections distinctly."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"PlannedTerminationPids","planLine":210,"maps":"An eligible safe fixture retains its exact planned PID sequence and closes once."},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"method","name":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","planLine":222},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"label","name":"path-label-excluded","planLine":218},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"label","name":"display-field-masked","planLine":218},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"label","name":"durable-review-path-excluded","planLine":218},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"canary","name":"secret-home","planLine":206},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"canary","name":"c866-user","planLine":206},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"canary","name":"c866-host","planLine":206},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"canary","name":"private-share","planLine":206},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"canary","name":"control-secret","planLine":206},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"WorkspaceLabel","planLine":206,"maps":"Assert every listed field equals `[redacted]`"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"TabLabel","planLine":206,"maps":"Assert every listed field equals `[redacted]`"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"PaneLabel","planLine":206,"maps":"Assert every listed field equals `[redacted]`"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Source","planLine":206,"maps":"Assert every listed field equals `[redacted]`"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Origin","planLine":206,"maps":"Assert every listed field equals `[redacted]`"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"AgentKind","planLine":206,"maps":"Assert every listed field equals `[redacted]`"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"BackendVersion","planLine":206,"maps":"Assert every listed field equals `[redacted]`"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"value","name":"[redacted]","planLine":206,"maps":"Assert every listed field equals `[redacted]`"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"PreviewId","planLine":206,"maps":"IDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"PaneId","planLine":206,"maps":"IDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"WorkspaceId","planLine":206,"maps":"IDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"TabId","planLine":206,"maps":"IDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"TerminalId","planLine":206,"maps":"IDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"OperationId","planLine":206,"maps":"IDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"BackendProtocol","planLine":206,"maps":"protocol"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"BackendInstanceId","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ShellPid","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Shell","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Pid","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ExecutableName","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"StartedAtUtc","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ParentPid","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"NativeSessionIds","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Foreground","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"AffectedProcesses","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Claims","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"SessionId","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Live","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ChildPid","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ChildStartedAtUtc","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"WouldLeaveTabEmpty","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Eligible","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"GuardAvailable","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ProcessInventoryComplete","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Blockers","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"GuardMode","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"AtomicClose","planLine":206,"maps":"incarnation facts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ExpiresAtUtc","planLine":206,"maps":"expiry"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ExpectedSessionId","planLine":206,"maps":"expected UUIDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"ExpectedNativeSessionId","planLine":206,"maps":"expected UUIDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"null","name":"ExpectedNativeSessionId","planLine":206,"maps":"expected UUIDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Code","planLine":206,"maps":"refusal code"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"value","name":"IdentityUnproven","planLine":206,"maps":"refusal code"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"PlannedTerminationPids","planLine":206,"maps":"planned PIDs"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Outcome","planLine":218,"maps":"complete/incomplete verdict and close counts"},
    {"id":"V-1","test":"HerdrPaneDisposalRedactionTests.Preview_redacts_path_labels","kind":"member","name":"Closes","planLine":218,"maps":"complete/incomplete verdict and close counts"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"label","name":"safe-evidence-preserved","planLine":221},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"label","name":"unsafe-leaf-null","planLine":221},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"WorkspaceLabel","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"TabLabel","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"PaneLabel","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"Source","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"Origin","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"AgentKind","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"BackendVersion","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"ExecutableName","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"grok.exe","planLine":210,"maps":"explicit internal cases"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"","planLine":210,"maps":"explicit internal cases"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"Review α 日本語","planLine":210,"maps":"explicit internal cases"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"c866-user","planLine":210,"maps":"explicit internal cases"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"antiphon-session-token","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"attached","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"grok","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"0.8.2","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"value","name":"pwsh.exe","planLine":210,"maps":"Known safe claim Source `antiphon-session-token`, Origin `attached`, AgentKind `grok`, backend version `0.8.2`, and `pwsh.exe`/`grok.exe` survive unchanged."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"PaneLabel","planLine":210,"maps":"null pane label"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"Origin","planLine":210,"maps":"null claim Origin/AgentKind"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"AgentKind","planLine":210,"maps":"null claim Origin/AgentKind"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"ExecutableName","planLine":210,"maps":"null process names"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"empty","name":"Foreground","planLine":210,"maps":"empty foreground"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"null","name":"Foreground","planLine":210,"maps":"null foreground"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"WorkspaceId","planLine":221,"maps":"IDs"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"TabId","planLine":221,"maps":"IDs"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"TerminalId","planLine":221,"maps":"IDs"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"ExpectedSessionId","planLine":221,"maps":"IDs"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"PlannedTerminationPids","planLine":221,"maps":"planned PIDs"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"Eligible","planLine":221,"maps":"eligibility"},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"Outcome","planLine":210,"maps":"An eligible safe fixture retains its exact planned PID sequence and closes once."},
    {"id":"V-4","test":"HerdrPaneDisposalRedactionTests.Redaction_preserves_safe_display_and_nulls","kind":"member","name":"Closes","planLine":210,"maps":"An eligible safe fixture retains its exact planned PID sequence and closes once."},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"label","name":"raw-identity-still-refused","planLine":229},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"ExecutableName","planLine":229,"maps":"exact IdentityUnproven blocker"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"Eligible","planLine":229,"maps":"exact IdentityUnproven blocker"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"Blockers","planLine":229,"maps":"exact IdentityUnproven blocker"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"Code","planLine":229,"maps":"exact IdentityUnproven blocker"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"Outcome","planLine":229,"maps":"exact IdentityUnproven blocker"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"Closes","planLine":229,"maps":"exact IdentityUnproven blocker"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"value","name":"IdentityUnproven","planLine":229,"maps":"exact IdentityUnproven blocker"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"value","name":"pwsh.exe","planLine":229,"maps":"POST must expose `pwsh.exe`"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"value","name":"Refused","planLine":229,"maps":"Execute Refused"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"label","name":"raw-claim-kind-not-normalized","planLine":230},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"AgentKind","planLine":230,"maps":"exact matching must now refuse IdentityUnproven"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"Source","planLine":230,"maps":"exact matching must now refuse IdentityUnproven"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"Origin","planLine":230,"maps":"exact matching must now refuse IdentityUnproven"},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"value","name":"Closed","planLine":230,"maps":"Unchanged raw claim must still make preview eligible and execution Closed."},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"value","name":"[redacted]","planLine":230,"maps":"Unchanged raw claim must still make preview eligible and execution Closed."},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"label","name":"raw-stamp-changed","planLine":231},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"member","name":"AffectedProcesses","planLine":231,"maps":"Confirm eligible preview and displayed basename."},
    {"id":"V-5","test":"HerdrPaneDisposalRedactionTests.Redaction_does_not_promote_unproven_process_identity","kind":"value","name":"PaneChanged","planLine":231,"maps":"Exact raw Stamp must produce Refused/PaneChanged"}
  ]
}
```

The value item whose name is the empty string explicitly requires the empty-string expected literal (including a supported literal-array loop); it is not an omitted name. Labels still must be nonempty. The raw exact-claim metadata clause also requires Claims/SessionId/Live/ChildPid/ChildStartedAtUtc in V-4; corrected assertions are at 293–297.

The checked DTO authority is historical c866 src/Antiphon.SessionRunner.Contracts/HerdrPaneDisposalContracts.cs, lines 3–53 (3051 bytes; SHA-256 a7983d990f8bfdf9e29c90ff20a41d96d4bd97d2007bc960128cb0c2ae2a7db5). Preview members are at 19–33, process fields at 12–13, claim fields at 9–10, receipt Code/expected UUIDs at 36–40, and IdentityUnproven's wire value is herdr_pane_identity_unproven at 47. There is no guessed IncarnationFacts member: the mapping expands to the actual member identifiers above. Leaf identifiers retain actual-expression provenance; this syntax-only schema does not encode every receiver/index or prove values are correct on every surface.

Corrected AssertV1Facts checks workspace w1, tab w1:t2, terminal term_000000000002, protocol 20, shell PID 4242 and start 2026-01-01T00:00:00Z; it checks the captured backend instance, expected session, null native ID, expiry FixedNow plus two minutes, claim child/null facts, inventory completeness, blockers and the conditional planned sequence [4242]/empty. Receipt Code has its own assertion at 120–121. Preserve all that raw source, including both shell and affected-record assertions; do not turn whole-object equality into invented field coverage. The static checklist requires the named references; ordinary golden review inspects the stronger raw assertions.

Corrected V-1's user-canary exclusion is at 106 and flows through 530–537; the historical omission is in 390–397 despite setup occurrences at 33–36/42. Corrected V-4 separately asserts null Foreground at 321 and empty Foreground at 331; plain c866-user is intentionally preserved through the literal-array loop at 259–267, not treated as an exclusion there. Corrected V-5 asserts Blockers and the expected IdentityUnproven symbol at 370/392, plus receipt Code at 372/394; Eligible=false alone cannot satisfy them.

Helper analysis must stay obligation-specific: a field reference inside uniquely bound AssertV1Facts is syntactic member evidence even when its expected value is a runtime parameter. This does not establish that runtime value. Likewise the local Label(field) return expression is **not** inferred as a stable label; none of those v1-field labels is a required target in this frozen subset. The required path/display/safe/raw labels have direct literal segments or the supported literal helper-parameter associations. If required evidence depends on an unsupported return/callback, report unmapped; do not widen D-3 or whitelist this fixture to obtain green. Any such implementation finding must be reported and reconciled, not removed from the expected obligations.

The 20 new class-qualified methods and the 26 current existing names are fully enumerated in the active checklist. The latter were read from their source attributes, not inferred from a previous plan count. No Arguments or data-source attributes occur on those 26 methods at the observed master. Dynamic reachability remains unproved for both historical datasets and for all six future controls until their separately commissioned Mutation execution.

TestDesign document validation: the active checklist has 72 entries (20 new methods + 20 labels + 26 existing methods + 6 PC targets); the historical checklist has 121 mapping entries. Repeated identical obligations with different maps clauses are additive aliases, not conflicting requirements, and deduplicate under D-2 while retaining the clause locations. Both JSON objects parse; all planLine/maps references resolve against their respective plan bytes. Seven raw fixture blob hashes, byte counts and line ranges match Git. The 20+6 existing method names match the current-master source census. CP-1 remains byte-for-byte unchanged with seven class operands, 11 cells, Min 46 and estimate 8. The scratch directory is absent and only this plan is changed. These are document/source checks, not analyzer/TUnit/PC execution receipts.

## Code admission reconciliation (2026-10-02)

Assigned base `b26c97658e712c53083e6777e5753bbf336011cc` contains CARD-0885
and CARD-0826's landed replay. The Code brief supersedes the conditional assumption
that CARD-0826 necessarily extracted checkpoint cleanup: its landing added the
HostCleanup project and migration, but left the original tool Cleanup files and
caller wiring intact. Those planned extraction dependencies are moot for this
read-only verb. All files this closed footprint needs exist; no reconstruction or
HostCleanup/caller edit is required.

Reread: Program/project, CheckpointApp runtime/driver seams, WaitCommand,
EvidenceFolder, RunStateStore, Manifest classes, owned-temp helpers and testing
owner. The tool remains net9.0 with per-project package pins. Import still supports
Serial/Environment/Repeat and retains repeated CLI option values. TempDir custody
and `C804_ORPHAN_SWEEP_ROOT=c891-disabled;TUNIT_MAX_PARALLEL_TESTS=1` are unchanged.

Admission census: CheckpointImportTests 20 and CheckpointManifestTests 6,
no Arguments/data-source/skip attributes. The seven CP-1 wildcard operands select
only the five new classes and those two existing classes. New single-result methods
remain 4+5+4+3+4=20. Delta from freeze: existing 0, new 0, rows 0; CP-1 remains 46.
The brief additionally commissions one normal Unit lane at the end, overriding
this plan's earlier narrow-lane statement. That lane is separately disclosed in the
Code report, with its actual expanded counts and any failures.
