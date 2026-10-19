# CARD-1332 — bundle argv verification design

This is the verification addendum to [the fix plan](2026-10-19-card-1332-bundle-argv-plan.md), fetched from `feat/card-task-52cf1bd5` at **6d2a12a11f702fbbe668755282cfdc0cb0954c40**. It does not replace the fix design or its S1/S2 boundaries. This file's checkpoint table supersedes the proposal table for commissioning Code. Land to **master only**; no RC, activation, runner change or runtime-limit change.

Inspected task base: **a0f3c4f3e728655933da1782981a88fb2b7b3a56**. The relevant bundle, owner and InstructionBundleTests inputs have no diff from the fetched plan tip. Read CARD-1332 through `card.ps1 get CARD-1332 -Board Antiphon`: card **e326c46a-fd69-4287-a640-ffcd1776068e**, board **8988ca03-7414-47ad-b0b6-51556c701703**, two revisions at inspection. No builds, test execution or mutation execution occurred in TestDesign.

The fixed limits are **11,500** normalized orchestrator-body UTF-16 units, **28,000** estimated argv early threshold, **29,500** reserve boundary and **30,000** unchanged default/transport ceiling. The plan's candidate estimates **10,787 / 26,827** are illustrative source measurements, not acceptance constants or measured test results. Final Code must report its actual values.

## Verification design

### Inspection

- `InstructionBundleTests.cs`: bodies of the forwarding, autonomy LF/CRLF, worst-composition, overflow, other-arguments, blocked-seat and embedded-source/header tests; `Collapse`, `PinSentence`, and `C1213Expected.FirstUse` | embedded/source boundary -> V-2..V-5, R-1..R-3.
- `InstructionFragmentTests.cs`: all bodies, synthetic Bundles/Sha8/Count helpers, especially `C1213_ExpandedBudgetIsEnforced` | expansion and exact/one-less admission -> R-3; no new fragment parser work.
- `RepairSourceDocumentationTests.cs`: all three tests, AssertRecipe, FindRepoRoot; `StandingPipelinePolicyDocumentationTests.cs`: bodies and PolicyCopy/Collapse/ReadRepoFile; `OrchestratorInstructionsGuidanceTests.cs`: bodies and PolicyCopy/Slice | existing owner assertions and copy-specific migrations -> V-1/V-2. The new ownership fixture uses these as its nearest fixtures.
- `TaskPlatformGuidanceTests.cs`, including RunnerDefaultGuidanceTests and both root locators; `KeepSixDocumentationTests.cs`; `PostLandRetrospectiveContractTests.cs`, including RetrospectiveSection/RepoFile | placement and neighboring unchanged instruction blocks -> V-1/V-2/R-1.
- `CheckpointRepeatDocumentationTests.cs`, CheckpointTestBase hooks in `CheckpointTestScope.cs`, CheckpointFixtures.RepoRoot in `CheckpointTestSupport.cs`, DockerStackDocuments.FindRoot and DelegateScriptRunner.RepoRoot | raw pin and repository binding -> R-2. The selected checkpoint test creates no temporary scope root or child process.
- `InstructionBundleComposer.cs`: Compose, EnsureWithinCommandLineBudget, ComposedInstructions; `InstructionBundles.cs`: ForDelegate, Load, ReadNormalised, HashOf, InstructionBundle.Render; AgentTaskEnums and DelegationSettings default | real production arithmetic/resource boundaries -> V-3/V-4/V-5/R-3. No production API or helper is changed by this design.
- `server/Bundles/orchestrator.md` and README; owner recovery/StartRef/adoption, standing-pipeline, placement, verification and Code/Review/Land/Mutation sections in `docs/orchestration-loop.md`; checkpoint schema, filter grammar, coverage bindings, build slots, mutation and delivery sections in `docs/testing-and-build.md` | editing and evidence contracts -> all V/R. Other routing, queue, provider and runtime algorithms are excluded below.

Paths without a directory above are under `tests/Antiphon.Tests/Application/`, except the named checkpoint helpers under `tests/Antiphon.Tests/Checkpoints/` and DockerStackDocuments under Infrastructure.

### Delivery inventory

**No new or changed asynchronous delivery path.** This card edits static instruction content and tests. It neither changes a producer/queue handoff nor adds a recovery worker. The relevant composition boundary is:

| Producer | Destination | Persistence / durable identity | Recovery | Observable evidence and limit |
|---|---|---|---|---|
| Committed bundle + owner text -> compiled embedded catalog -> real Compose | In-memory launch instruction payload | Git SHA, full source hash and content-derived bundle/fragment stamps; no new durable delivery record | Existing launch/refresh behavior unchanged | E compares normalized source, resource text, version/header and composed inclusion. A measures the real composed payload. Neither proves that a running agent received or followed it. |
| Existing launch/refresh/session delivery | Existing agent/session | Existing session identity and its recorded bundle stamp, unchanged | Existing queue/relaunch/recovery, unchanged and excluded | No receipt is claimed by this card. A launch request, queue insert, event, Sent flag or ack would not prove delivery. Session-input acceptance would require the matching complete UserPrompt transcript. |

Accordingly no busy-recipient, already-eligible-recipient or crash/enqueue-recovery test is commissioned: there is no changed asynchronous handoff to exercise. This is an explicit scope exclusion, not a substitute for recipient evidence. Embedded-resource tests substitute only for static payload assembly; source text tests substitute only for instruction presence; neither proves transport or model compliance. If Code changes launch, refresh, queue, storage, session-input or recovery behavior, return to **Plan** for the real producer-to-recipient queue inventory, all handoff failures and complete transcript witnesses; do not accept this table for that larger change.

### Proves it works now

The three new methods live in `tests/Antiphon.Tests/Application/OrchestratorBundleOwnershipTests.cs`, Category Unit. Use the existing repository-root finder; assert every start/end index and anchor count before slicing. No databases, mocked transports, processes, global Console capture, wall-clock waits or fixture framework are needed.

- **V-1: full procedures remain at usable owners** | source contract | `OrchestratorBundleOwnershipTests.C1332_OwnersContainRelocatedProcedures`, CP-1 and CP-2 | four TUnit argument cases: child, pipeline, verification, placement. Check five exact `<a id="..."></a>` anchors, each once: delegate-start-ref, repairsource-failed-owner-recovery, standing-pipeline-policy, verification-rounds, runner-placement. Each anchor must immediately introduce its expected preserved heading, not an unrelated section. Child checks three separately bounded windows: Continuing a sibling's work to Delegate branch names; failed-owner recovery to Continuing; Landing a StartRef repair to its next peer heading. Pipeline ends at Keep-six; placement ends at the next bold topic; verification ends at the next H2. Assertions use independent literals, not expected text loaded from the plan or the bundle. Preserve complete existing owner AssertRecipe and phrase arrays. Add exact pins for the relocation map: excluded StartRef flags, recorded branch/base authority, version observation, owner/source Review distinction, recovery/publication/history, occupancy/limits/override rules, explicit Interim flags/prerequisites/final latch, inheritance/OS scope and authorized settings writes. Each `c1332-owner-...` label in the PC table must exist at the corresponding assertion; multi-item recipes expand the suffix to the literal item. Missing anchor is an assertion red, not an IndexOutOfRange failure.
- **V-2: the embedded bundle keeps operative rules and real owner links** | resource/source contract | `OrchestratorBundleOwnershipTests.C1332_BundleKeepsRulesAndOwnerPointers`, CP-2 and CP-5..CP-9 | four independently reported arguments using the same family names. Read InstructionBundles.TextOf(Orchestrator), collapse whitespace for prose, and inspect that family's compact paragraph. Assert the complete candidate rules from S2 with independent literals, including below-cap Backlog pull and all four override predicates. Require all five canonical owner URIs and resolve their exact unique anchors in the real owner file. A pointer appearing elsewhere cannot excuse a missing rule in the relevant paragraph. Preserve existing owner/AGENTS/skill arrays; branch only the bundle-copy assertions. No case or old owner assertion is deleted. Labels `c1332-bundle-...` / `c1332-pointer-...` in the PC table are binding obligations.
- **V-3: one fixed body allocation works on LF and CRLF** | normalized source + independently rebuilt resource | `OrchestratorBundleOwnershipTests.C1332_OrchestratorBodyFitsItsAllocation`, CP-2; E in CP-4 | two arguments false/true for a physical-source LF versus in-memory CRLF checkout. Normalize exactly with ReplaceLineEndings("\n").Trim(), then .Length, and assert <= 11,500 at `c1332-body-allocation`. Print measured body/limit/signed headroom. Keep the actual source assertion, not just a synthetic boundary. In the same method exercise its private measurement helper with a fixed independent boundary probe: 11,497 ASCII x, one internal LF and one supplementary Unicode scalar (two UTF-16 units) = 11,500. Surround it with trim-only whitespace and render the internal LF as CRLF in the CRLF case; assert 11,500 at `c1332-body-utf16` / `c1332-body-normalized`. Run the probe before the actual-source cap so either helper defect reaches its intended assertion even when final bundle headroom is small. The private helper may return normalized text and count: first compare text to the independent LF probe at c1332-body-normalized, then its UTF-16 count at c1332-body-utf16. This makes byte-counting or skipped normalization detectably wrong without changing production normalization. Remove only the obsolete 14,310 size assertion from the autonomy test.
- **V-4: aggregate growth fails before the reserve** | real embedded catalog/composer/admission | `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget`, CP-3 | preserve Enum.GetValues over both kinds and every non-specialist role (currently 2 x 14 = 28 diagnostic rows, **one TUnit execution**), board-api, style-explanatory and the unchanged CRLF Telegram preset. Use a literal D-format GUID, e.g. 11111111-2222-3333-4444-555555555555. After printing the matrix, assert new DelegationSettings().CommandLineBudgetChars equals literal 30,000 at `c1332-default-budget`; never derive the early cap from current size. Collect/print all measurements first. For every composition independently calculate sum(arg.Length + 3) + (empty ? 0 : composed.Text.Length + 28); call the real EnsureWithinCommandLineBudget with the exact estimate (accept, `c1332-estimate-exact`) and estimate-minus-one (throw, `c1332-estimate-one-less`). Cover the helper's empty branch using ComposedInstructions.Empty and a fixed nonempty argument vector in this same method. Then preserve raw composition <30,000, call the real guard at 30,000 - 500 (`c1332-argv-reserve`), and assert estimate <= fixed 28,000 (`c1332-argv-early`), in that order so reserve controls reach the reserve before early rejection.
- **V-5: passing and failing runs explain their measurements** | CP-3 method assertions plus required log inspection | same method as V-4 | stdout/TRX attachment contains all 28 kind/role rows and a deterministic maximum row (document tie handling), body/composed/argv counts, 30,000/29,500/28,000 limits and each limit minus estimate as signed headroom. Each row names bundle/fragment contributions without exposing prompt contents. Use an additive ledger of rendered bundle-template lengths, separators, fragment replacement deltas and the literal append. Print raw fragment sizes as annotations, not additional summands after expansion. Independently assert ledger sum == composed.Text.Length at `c1332-contribution-total`; do not set a component to “total minus everything else” to force equality. Cross-check displayed argv through V-4's real admission calls. Inspect required output in the S2 assertion-red and final-green receipts; missing/truncated fields leave V-5 incomplete even if the method passes. Output-format presence is a diagnostic usability acceptance check; the safety-critical arithmetic has PCs. Do not add a standalone test of a test-only formatter.

### Guards the regression

- **R-1: unchanged operational instructions and owner content survive relocation** | CP-4..CP-9 and CP-11 | retain existing literal assertions for delegated reading/autonomy, incident/authentication/credentials, blocked reports/seats/Reply/Refine, live-settings precedence, keep-six and retrospective trigger. Inspect a source diff against the plan base to confirm every byte outside the five replacement regions remains unchanged, including stage-report parsing, channel delivery and SourceLanding recipe. The raw pin alone cannot authorize a semantically wrong repin; ordinary Review must inspect those regions and the new expected SHA.
- **R-2: source and built payload remain bound to their approved bytes** | CP-4 and CP-10 | E requires source-normalized Text, computed SHA version, Header/Render and real composed inclusion; P requires the recomputed raw LF SHA. Leave all other existing stage-code/repeat-budget assertions intact. A resource constructor mutation provides an assertion-level stale-content surrogate; deliberately executing an old DLL is not fresh-build evidence.
- **R-3: existing refusal counts expanded fragments and other arguments and never truncates** | CP-12 and CP-13 | F's synthetic 1,500-character fragment must compose intact, accept exact budget, reject one less, name the fragment and say Nothing was truncated; its shipped loop keeps the 500 reserve. X must throw on the 40,000-character append with subject/budget/bundle/estimate/final-runner diagnostics. Y must reject fat otherArgs while the append alone fits. CP-13 closes this explicit plan obligation omitted from its proposed roster; it adds two executed tests and one estimated minute.

Recipe aliases below are exact-method bindings, not permission to run a whole class:

| Alias | Exact method | Cases per method run |
|---|---|---:|
| O | `OrchestratorBundleOwnershipTests.C1332_OwnersContainRelocatedProcedures` | 4 |
| B | `OrchestratorBundleOwnershipTests.C1332_BundleKeepsRulesAndOwnerPointers` | 4 |
| L | `OrchestratorBundleOwnershipTests.C1332_OrchestratorBodyFitsItsAllocation` | 2 |
| A | `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` | 1 |
| E | `InstructionBundleTests.C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders` | 1 |
| C | `InstructionBundleTests.the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact` | 1 |
| S | `InstructionBundleTests.C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs` | 1 |
| U | `InstructionBundleTests.orchestrator_bundle_points_to_operational_autonomy_without_growing` | 2 |
| P | `CheckpointRepeatDocumentationTests.repeat_budget_reaches_code_briefs_without_bundle_growth` | 1 |
| F | `InstructionFragmentTests.C1213_ExpandedBudgetIsEnforced` | 1 |
| X | `InstructionBundleTests.an_oversized_composition_throws_and_names_what_to_shrink` | 1 |
| Y | `InstructionBundleTests.the_other_arguments_count_towards_the_budget_not_just_the_append` | 1 |
| K | `KeepSixDocumentationTests.bundle_says_the_server_does_not_dispatch_and_names_the_hold_script` | 1 |
| T | `PostLandRetrospectiveContractTests.C811_V3_OrchestratorBundleCarriesTheSibling` | 1 |
| I | `OrchestratorInstructionsGuidanceTests.The_bundle_names_the_file_route_compaction_and_precedence_and_dropped_the_swapped_sentences` | 1 |

### Guard inventory

Each row is one named guard, mapped to one distinct PC. Separate overrides, owner links,
allocation, reserve, early threshold, source identity and estimator checks cannot discharge
one another. Where one guard has several equivalent forms of omission (for example the
excluded-flag set), each listed variant is mandatory and runs separately. Existing checked
instructions remain guards even when their bytes are intended to remain unchanged.
Operational examples, explanatory prose and field formatting are not independent safety
guards; their owner assertions and manual inspection remain ordinary obligations.

| Guard | Plan reference and safety-critical invariant | Control |
|---|---|---|
| G-1 | S1/D-4: unique owner anchor delegate-start-ref | PC-1 |
| G-2 | S2/D-4: embedded pointer to delegate-start-ref | PC-2 |
| G-3 | S1/D-4: unique owner anchor repairsource-failed-owner-recovery | PC-3 |
| G-4 | S2/D-4: embedded pointer to repairsource-failed-owner-recovery | PC-4 |
| G-5 | S1/D-4: unique owner anchor standing-pipeline-policy | PC-5 |
| G-6 | S2/D-4: embedded pointer to standing-pipeline-policy | PC-6 |
| G-7 | S1/D-4: unique owner anchor verification-rounds | PC-7 |
| G-8 | S2/D-4: embedded pointer to verification-rounds | PC-8 |
| G-9 | S1/D-4: unique owner anchor runner-placement | PC-9 |
| G-10 | S2/D-4: embedded pointer to runner-placement | PC-10 |
| G-11 | S1/D-4: StartRef dispatch requires an isolated worktree | PC-11 |
| G-12 | S1/D-4: prohibited StartRef combinations stay excluded | PC-12 |
| G-13 | S1/D-4: recorded branch is the identity | PC-13 |
| G-14 | S1/D-4: choosing a base grants no landing authority | PC-14 |
| G-15 | S1/D-4: version support and recorded-base observation precede reliance | PC-15 |
| G-16 | S1/D-4: failed-owner Review binds the original owner and exact source | PC-16 |
| G-17 | S1/D-4: recovery command names source SHA, evidence and recovery verb | PC-17 |
| G-18 | S1/D-4: a queued recovery is not publication | PC-18 |
| G-19 | S1/D-4: Failed history survives recovery | PC-19 |
| G-20 | S1/D-4: adoption Review names the separate source | PC-20 |
| G-21 | S2/D-4: bundle StartRef requires Worktree | PC-21 |
| G-22 | S2/D-4: bundle forbids branch replacement | PC-22 |
| G-23 | S2/D-4: bundle consumes reported WorktreeBranch | PC-23 |
| G-24 | S2/D-4: bundle explicitly denies base-derived land authority | PC-24 |
| G-25 | S2/D-4: bundle recovery requires owner-bound Clean Final/Full Review | PC-25 |
| G-26 | S2/D-4: bundle names explicit reviewed-source recovery | PC-26 |
| G-27 | S1/S2/D-4: actual RepairSource is ineligible for FromTask | PC-27 |
| G-28 | S1/D-4: owner retains pipeline occupancy and effective-limit interpretation | PC-28 |
| G-29 | S2/D-4: dispatch reads the project-scoped effective settings | PC-29 |
| G-30 | S2/D-4: lower effective cap bounds dispatch | PC-30 |
| G-31 | S2/D-4: concurrent stages have separate worktrees | PC-31 |
| G-32 | S2/D-4: host budget writes require operator authority | PC-32 |
| G-33 | S2/D-4: Code depth includes every occupied/pending state | PC-33 |
| G-34 | S2/D-4: Code cap gates new Plan work | PC-34 |
| G-35 | S2/D-4: overlapping Code defers even with a free slot | PC-35 |
| G-36 | S2/D-4: override requires population=open | PC-36 |
| G-37 | S2/D-4: override requires canOverride=true | PC-37 |
| G-38 | S2/D-4: override requires axis=absolute | PC-38 |
| G-39 | S2/D-4: override excludes same-stage occupants | PC-39 |
| G-40 | S2/D-4: project populations stay isolated | PC-40 |
| G-41 | S1/D-4: Interim requires explicit admission and complete baseline selection | PC-41 |
| G-42 | S2/D-4: first Code and Review are Final/Full | PC-42 |
| G-43 | S2/D-4: Interim requires explicit admission | PC-43 |
| G-44 | S2/D-4: a refused verification round cannot silently fall back | PC-44 |
| G-45 | S2/D-4: clean Interim cannot authorize land | PC-45 |
| G-46 | S1/D-4: inherited platform is retained at its owner | PC-46 |
| G-47 | S2/D-4: normal placement defers to runtime defaults | PC-47 |
| G-48 | S2/D-4: explicit Any unpins | PC-48 |
| G-49 | S2/D-4: an OS pin is scoped to the required piece | PC-49 |
| G-50 | S2/D-4: pipeline ticks cannot write defaults | PC-50 |
| G-51 | S1/D-4: settings writes require user authority, current revision and Human provenance | PC-51 |
| G-52 | S2/D-3: fixed normalized body allocation is 11,500 | PC-52 |
| G-53 | S2/D-3: allocation measures normalized UTF-16 content | PC-53 |
| G-54 | S2/D-2: aggregate-only growth fails at fixed 28,000 | PC-54 |
| G-55 | S2/D-1: independent 500 reserve remains at 29,500 | PC-55 |
| G-56 | S2/D-1: default budget stays 30,000 | PC-56 |
| G-57 | S2/D-2: diagnostic argv estimate charges other arguments | PC-57 |
| G-58 | S2/D-2: diagnostic argv estimate charges append flag and quoting | PC-58 |
| G-59 | S2/D-2: estimate boundary is inclusive and independently cross-checked | PC-59 |
| G-60 | S2/D-2: diagnostic contribution sum does not double-count fragment expansion | PC-60 |
| G-61 | S2/D-5: built resource text equals normalized disk source | PC-61 |
| G-62 | S2/D-5: version/header binds actual content | PC-62 |
| G-63 | S2/D-5: raw approved-bytes pin notices any unapproved content edit | PC-63 |
| G-64 | S2/D-1: expanded fragments count toward admission | PC-64 |
| G-65 | S2/D-1: overflow refuses instead of truncating or accepting | PC-65 |
| G-66 | S2/D-1: non-append arguments count toward admission | PC-66 |
| G-67 | S2/D-4: preserved delegation/read authority remains operative | PC-67 |
| G-68 | S2/D-4: launch incident guidance survives | PC-68 |
| G-69 | S2/D-4: authentication/credential guidance survives | PC-69 |
| G-70 | S2/D-4: blocked reports require an answer with the correct authority | PC-70 |
| G-71 | S2/D-4: parking-off seats have no automatic release deadline | PC-71 |
| G-72 | S2/D-4: Refine cannot replace Reply on a Blocked task | PC-72 |
| G-73 | S2/D-4: keep-six does not dispatch on the server | PC-73 |
| G-74 | S2/D-4: retrospective distinction survives the neighboring replacement | PC-74 |
| G-75 | S2/D-4: live settings retain precedence and read-only custody | PC-75 |
| G-76 | S2/D-4: autonomy pointer remains intact | PC-76 |
| G-77 | S2/D-4: channel reply and attachment contract survives | PC-77 |
| G-78 | S2/D-4: parsed stage-routing block stays byte-identical | PC-78 |
| G-79 | S2/D-4: SourceLanding recipe stays byte-identical | PC-79 |

### Positive controls

**Execution order is Code ordinary V/R -> ordinary Review -> confirmed master land ->
SourceLanding Mutation.** Code's commissioned test-first reds are not substitutes for this
battery. Review judges the pending design before land. Every PC below is required; none ran
in TestDesign. Do not describe this artifact as PC-clean.

Shared recipe and reachability:

- **O** mutates only a bounded owner section in docs/orchestration-loop.md; the exact O method reads that file and executes all four argument cases. Keep anchors/headings intact except the explicitly named anchor variants. **B** mutates orchestrator.md, then rebuilds; the exact B method reads the embedded resource and all four arguments execute. Both types must fail the named semantic/anchor assertion, not slicing or resource-loading setup.
- **L/A** use the exact production source/composer and the test helpers identified in V-3/V-4/V-5. Padding is ASCII text, appended inside the normalized body, with no placeholders. Bind padding at landed SHA L using its measured matrix: compute the delta to the stated target, account for an inserted LF, then independently verify the final real estimate; never assume the proposed 26,827 survived Code. Only the intended threshold is violated for the early control. A reserve control is run separately.
- **E/P/F/X/Y/C/S/K/T/I/U** use the existing method body inspected above. Apply the one compiling content/arithmetic defect named in the row, retain all target assertions, and rebuild before that exact method. New test-helper mutations are explicitly test-guard controls; production text/estimator mutations are contract/admission controls. No runtime deployment is involved.
- Each cell's variants are individual cycles, never one multi-defect mutation. Keep per-variant baseline, patch, successful build, expected assertion red, exact restoration and fresh green receipts. A wrong assertion, build failure, fixture exception, missing argument row or zero tests is not the intended red. Restore source/index bytes and refresh timestamps before a fresh build; prove clean restoration at L. Own/await every child and build slot, leave no task-owned alternate outputs, and keep evidence at the assigned external root.
- All filters below are exact methods. O/B have MinExecuted=4, L/U=2, the rest=1. Do not combine PCs into class filters. A runner unable to select the four/two argument results is a binding/execution problem, not permission to claim one internal loop as four executions.
- Classification for O/B/C/S/K/T/I/U/P: instruction-contract defect; L and A estimator/ledger mutations: test-guard defect; A default-budget, E, F, X and Y mutations: production-guard/resource defect. Every row has a concrete recipe, variant count, case, oracle and direct reachability above. A survivor is a coverage finding, not a product regression. An unmutated-L failure is separately classified as a product/test regression or infrastructure failure after diagnosis.

**Binding status: binding required.** Proposed labels and new private helpers do not exist yet.
After Code, bind each payload to the actual exact method, landed source file/span, old/new
bytes or deterministic patch, source SHA, argument roster, expected assertion and build
recipe. A compiling mutation must leave the detecting assertion present. A named method,
a green Code row, a syntactic coverage match or this table does not bind a payload.
Unbound payloads remain **binding required after Code** and after Review; the post-land
binding step must not manufacture an executable pack from prose. Preserve all variants.
Use the exact filters below in the local inherited mutation driver and the external evidence
layout required by docs/testing-and-build.md.

| PC | Guard | Compiling defect / recipe and case | Variants | Detecting filter | Expected assertion red |
|---|---|---|---:|---|---|
| PC-1 | G-1 | O/child: (a) remove the exact anchor; (b) duplicate it inside its section. Preserve headings and all procedure text. | 2 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-anchor-delegate-start-ref: count == 1; observed 0 / 2 |
| PC-2 | G-2 | B/child: change only the bundle URI fragment from #delegate-start-ref to #delegate-start-ref-missing; rebuild the resource. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-pointer-delegate-start-ref: exact canonical URI present and target count == 1 |
| PC-3 | G-3 | O/child: (a) remove the exact anchor; (b) duplicate it inside its section. Preserve headings and all procedure text. | 2 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-anchor-repairsource-failed-owner-recovery: count == 1; observed 0 / 2 |
| PC-4 | G-4 | B/child: change only the bundle URI fragment from #repairsource-failed-owner-recovery to #repairsource-failed-owner-recovery-missing; rebuild the resource. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-pointer-repairsource-failed-owner-recovery: exact canonical URI present and target count == 1 |
| PC-5 | G-5 | O/pipeline: (a) remove the exact anchor; (b) duplicate it inside its section. Preserve headings and all procedure text. | 2 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-anchor-standing-pipeline-policy: count == 1; observed 0 / 2 |
| PC-6 | G-6 | B/pipeline: change only the bundle URI fragment from #standing-pipeline-policy to #standing-pipeline-policy-missing; rebuild the resource. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-pointer-standing-pipeline-policy: exact canonical URI present and target count == 1 |
| PC-7 | G-7 | O/verification: (a) remove the exact anchor; (b) duplicate it inside its section. Preserve headings and all procedure text. | 2 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-anchor-verification-rounds: count == 1; observed 0 / 2 |
| PC-8 | G-8 | B/verification: change only the bundle URI fragment from #verification-rounds to #verification-rounds-missing; rebuild the resource. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-pointer-verification-rounds: exact canonical URI present and target count == 1 |
| PC-9 | G-9 | O/placement: (a) remove the exact anchor; (b) duplicate it inside its section. Preserve headings and all procedure text. | 2 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-anchor-runner-placement: count == 1; observed 0 / 2 |
| PC-10 | G-10 | B/placement: change only the bundle URI fragment from #runner-placement to #runner-placement-missing; rebuild the resource. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-pointer-runner-placement: exact canonical URI present and target count == 1 |
| PC-11 | G-11 | O/child: replace the owner requirement `It requires \`-Worktree\`` with `It permits Shared`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-startref-worktree |
| PC-12 | G-12 | O/child: remove one exclusion per variant: -Shared, -ReadOnly, -OnAgent, -Agent, -RepairSource, -SourceLanding. Scope edits to the exclusion sentence. | 6 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-startref-excluded-<flag> |
| PC-13 | G-13 | O/child: remove the WorktreeBranch sentence, leaving the command example intact. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-recorded-branch |
| PC-14 | G-14 | O/child: replace `sets no merge target` with `sets a merge target`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-base-only |
| PC-15 | G-15 | O/child: remove the sentence requiring GET /api/version and task-detail requested/recorded base. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-support-observation |
| PC-16 | G-16 | O/child recovery sub-section: remove only `subjectTaskId` from the subject-binding clause; keep command and heading. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-review-subject |
| PC-17 | G-17 | O/child recovery command: delete one flag per variant: -ExpectedSourceSha, -ReviewEvidenceId, -RecoverReviewedSource. | 3 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-recovery-command-<flag> |
| PC-18 | G-18 | O/child recovery: remove `a queued request is not publication` while retaining the status command. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-publication |
| PC-19 | G-19 | O/child recovery: replace `remain historical` with `are cleared` in the Failed-status sentence. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-failed-history |
| PC-20 | G-20 | O/child adoption sub-section: replace `repair task` with `original owner` in the Review-subject instruction. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-adoption-subject |
| PC-21 | G-21 | B/child: remove only -Worktree from the compact StartRef command. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-startref-worktree |
| PC-22 | G-22 | B/child: remove only `never git checkout -B`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-no-checkout |
| PC-23 | G-23 | B/child: replace `reported WorktreeBranch` with `reconstructed branch`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-recorded-branch |
| PC-24 | G-24 | B/child: remove `A base grants no land authority.` | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-base-only |
| PC-25 | G-25 | B/child: replace `owner-bound Clean Final/Full Review` with `source Review`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-owner-review |
| PC-26 | G-26 | B/child: remove -RecoverReviewedSource from the compact instruction. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-recovery |
| PC-27 | G-27 | B/child: delete `A -RepairSource task cannot be used with -FromTask.` Keep the owner link. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-no-repairsource-adoption |
| PC-28 | G-28 | O/pipeline: remove one literal per variant from the scoped route explanation: concurrencyScopes, hosts, runners, runnerDefaults, parallelRemaining, queuedRemaining. | 6 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-pipeline-field-<field> |
| PC-29 | G-29 | B/pipeline: replace `/api/agent-tasks/pipeline?projectId=<id>` with `/api/agent-tasks/pipeline`; keep the owner link. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-effective-route |
| PC-30 | G-30 | B/pipeline: replace `never exceed the lower effective cap` with `use the larger cap`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-lower-cap |
| PC-31 | G-31 | B/pipeline: remove `each in its own -Worktree`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-stage-worktrees |
| PC-32 | G-32 | B/pipeline: replace `need an operator request` with `need no operator request`. Assert the complete original sentence, not the contained substring. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-host-authority |
| PC-33 | G-33 | B/pipeline: remove one token per variant: in-flight, queued, ready. | 3 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-code-depth-<state> |
| PC-34 | G-34 | B/pipeline: replace `at cap, start no new Plan toward Code` with `at cap, start another Plan toward Code`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-no-plan-at-cap |
| PC-35 | G-35 | B/pipeline: remove the unchanged `Defer Code touching ... same source area ... even with a free slot.` sentence. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-overlap |
| PC-36 | G-36 | B/pipeline: change only population=open to population=queued. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-override-population |
| PC-37 | G-37 | B/pipeline: change only canOverride=true to canOverride=false. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-override-allowed |
| PC-38 | G-38 | B/pipeline: change only axis=absolute to axis=role. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-override-axis |
| PC-39 | G-39 | B/pipeline: remove only `and no same-stage occupant`; retain the other three predicates. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-override-stage |
| PC-40 | G-40 | B/pipeline: remove `Other projects' work never counts against yours.` | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-project-isolation |
| PC-41 | G-41 | O/verification: delete one independently pinned item per variant: AllowInterim, InterimVerification:Enabled, nightly readiness prerequisite, -VerificationSubject, -VerificationBaselineOutcome, -VerificationSelectionFile. | 6 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-interim-<item> |
| PC-42 | G-42 | B/verification: change `first Code/Review is Final/Full` to `first Code/Review is Interim`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-first-final |
| PC-43 | G-43 | B/verification: remove only `Interim requires explicit admission`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-interim-admission |
| PC-44 | G-44 | B/verification: remove only `never silently change rounds`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-no-fallback |
| PC-45 | G-45 | B/verification: replace `goes to Final Review, never land` with `goes to land`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-final-before-land |
| PC-46 | G-46 | O/placement: remove one inheritance clause per variant: predecessor, card, otherwise Any; preserve the heading and routes. | 3 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-inherit-<source> |
| PC-47 | G-47 | B/placement: replace `Normally omit -Runner and -Platform` with `Always pass -Runner server2`. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-runtime-placement |
| PC-48 | G-48 | B/placement: remove `Use -Platform Any to unpin.` | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-any-unpins |
| PC-49 | G-49 | B/placement: remove the complete OS-required-piece sentence (including own filter/budget and leave-rest-unpinned). | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-os-scope |
| PC-50 | G-50 | B/placement: replace `Never write defaults on a pipeline tick.` with `Write defaults on every pipeline tick.` | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_BundleKeepsRulesAndOwnerPointers` | c1332-bundle-no-tick-write |
| PC-51 | G-51 | O/placement: independently remove user-requested authority, current revision, reason, or Human provenance from the settings recipe. | 4 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | c1332-owner-settings-<item> |
| PC-52 | G-52 | L: append LF + enough ASCII x to orchestrator.md for exactly 11,501 normalized UTF-16 units. Do not change the test cap or raw pin; run only L. | 1 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OrchestratorBodyFitsItsAllocation` | c1332-body-allocation: actual 11501 > 11500 |
| PC-53 | G-53 | L: change only the test measurement helper to count UTF-8 bytes (variant a), then separately omit CRLF normalization (b). Use supplementary Unicode and CRLF boundary probes described in V-3. | 2 | `/*/*/OrchestratorBundleOwnershipTests/C1332_OrchestratorBodyFitsItsAllocation` | c1332-body-utf16 / c1332-body-normalized |
| PC-54 | G-54 | A: append padding to board-api.md so the real maximum estimate is exactly 28,001; orchestrator body stays <=11,500 and every row stays <=29,500. Rebuild; never change caps or expected assertions. | 1 | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | c1332-argv-early: maximum 28001 > 28000 |
| PC-55 | G-55 | A: pad board-api.md to maximum estimate 29,501. Print the complete matrix; execute the reserve admission before the early assertion. | 1 | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | c1332-argv-reserve: real EnsureWithinCommandLineBudget rejects budget 29500 |
| PC-56 | G-56 | A: change only DelegationSettings.CommandLineBudgetChars default from 30_000 to 30_001. | 1 | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | c1332-default-budget: actual 30001 != literal 30000 |
| PC-57 | G-57 | A: in the new private estimator, omit the 36-character D-format session ID argument (including its +3 overhead). Keep real production admission unchanged. | 1 | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | c1332-estimate-exact: real guard rejects underreported estimate |
| PC-58 | G-58 | A: change the estimator's nonempty-append overhead from 28 to 27. | 1 | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | c1332-estimate-exact: real guard rejects estimate one short |
| PC-59 | G-59 | A: change the production admission comparison from total <= budgetChars to total < budgetChars. | 1 | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | c1332-estimate-exact: exact estimate wrongly refused |
| PC-60 | G-60 | A: add the expanded fragment length a second time in the new diagnostic additive ledger while leaving composed.Text and the argv estimator intact. | 1 | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | c1332-contribution-total: component sum != composed.Text.Length |
| PC-61 | G-61 | E: in InstructionBundles.Load, append one space to the loaded orchestrator Text only, after normalizing; rebuild. Keep resource present so this is an assertion red, not resource/fixture failure. | 1 | `/*/*/InstructionBundleTests/C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders` | orchestrator-text |
| PC-62 | G-62 | E: change HashOf to return a valid fixed eight-hex hash unequal to the orchestrator source hash; preserve Text. | 1 | `/*/*/InstructionBundleTests/C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders` | orchestrator-version |
| PC-63 | G-63 | P: add one ASCII character to an otherwise unchanged bundle sentence, leaving the approved raw SHA untouched. | 1 | `/*/*/CheckpointRepeatDocumentationTests/repeat_budget_reaches_code_briefs_without_bundle_growth` | orchestrator-approved-bytes |
| PC-64 | G-64 | F: change production appendChars calculation to subtract the rendered fragment length. Preserve Compose output. | 1 | `/*/*/InstructionFragmentTests/C1213_ExpandedBudgetIsEnforced` | expanded-budget: exact-minus-one unexpectedly accepted |
| PC-65 | G-65 | X: replace the overflow throw in EnsureWithinCommandLineBudget with return; keep the method compiling. | 1 | `/*/*/InstructionBundleTests/an_oversized_composition_throws_and_names_what_to_shrink` | Should.Throw<InvalidOperationException>: no exception for 40000-character append |
| PC-66 | G-66 | Y: set production otherChars to zero; leave the append calculation intact. | 1 | `/*/*/InstructionBundleTests/the_other_arguments_count_towards_the_budget_not_just_the_append` | Should.Throw<InvalidOperationException>: no exception for fat argument vector |
| PC-67 | G-67 | C: remove one complete protected sentence per variant: `Delegate the reading.`; exact-quote/personal-judgment carve-out; `Delegate everything else ...`; `About to Edit, Write, or build ...`. | 4 | `/*/*/InstructionBundleTests/the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact` | the matching existing ShouldContain phrase assertion |
| PC-68 | G-68 | C: remove only StoppedBeforeFirstPrompt from the bundle. | 1 | `/*/*/InstructionBundleTests/the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact` | existing ShouldContain("StoppedBeforeFirstPrompt") |
| PC-69 | G-69 | C: independently remove AuthenticationRequired, CompletedWithoutProgress, grok login, or `Do not paste, log, or repeat credentials` from the bundle. | 4 | `/*/*/InstructionBundleTests/the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact` | the matching existing ShouldContain phrase assertion |
| PC-70 | G-70 | S: replace `never NO_REPLY a blocked note` with `use NO_REPLY for a blocked note`. | 1 | `/*/*/InstructionBundleTests/C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs` | c1083-orchestrator-report |
| PC-71 | G-71 | S: replace `Parking defaults off (BlockedTaskParking:Enabled=false); do not assume a release deadline.` with `Parking releases every seat automatically.` | 1 | `/*/*/InstructionBundleTests/C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs` | c1083-orchestrator-seat |
| PC-72 | G-72 | S: change `-Refine returns 409 there` to `-Refine resumes it there`. | 1 | `/*/*/InstructionBundleTests/C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs` | c1083-orchestrator-refine |
| PC-73 | G-73 | K: remove `The server does not dispatch` from the bundle's keep-six line. | 1 | `/*/*/KeepSixDocumentationTests/bundle_says_the_server_does_not_dispatch_and_names_the_hold_script` | existing ShouldContain("The server does not dispatch") |
| PC-74 | G-74 | T: remove `is not a retrospective` from the bundle, preserving `A 409` and the section boundaries. | 1 | `/*/*/PostLandRetrospectiveContractTests/C811_V3_OrchestratorBundleCarriesTheSibling` | existing ShouldContain("is not a retrospective") |
| PC-75 | G-75 | I: independently remove `outranks` or `Never edit it.` from the unchanged live-settings paragraph. | 2 | `/*/*/OrchestratorInstructionsGuidanceTests/The_bundle_names_the_file_route_compaction_and_precedence_and_dropped_the_swapped_sentences` | the matching existing ShouldContain phrase assertion |
| PC-76 | G-76 | U: change only the autonomy URI's fragment to a nonexistent anchor. | 1 | `/*/*/InstructionBundleTests/orchestrator_bundle_points_to_operational_autonomy_without_growing` | existing full autonomy-pointer ShouldContain assertion |
| PC-77 | G-77 | C: independently remove `your whole reply is exactly \`NO_REPLY\`` or `A delegate's own \`[[attach:]]\` reaches only you, as text.` | 2 | `/*/*/InstructionBundleTests/the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact` | the matching existing ShouldContain phrase assertion |
| PC-78 | G-78 | P: remove the unchanged paragraph beginning `A pipeline-stage report` from orchestrator.md, leaving its approved SHA untouched. | 1 | `/*/*/CheckpointRepeatDocumentationTests/repeat_budget_reaches_code_briefs_without_bundle_growth` | orchestrator-approved-bytes; Review additionally compares the unchanged region to the plan base |
| PC-79 | G-79 | P: remove the unchanged paragraph beginning `Default workflow: Code -> ordinary Review` from orchestrator.md, leaving its approved SHA untouched. | 1 | `/*/*/CheckpointRepeatDocumentationTests/repeat_budget_reaches_code_briefs_without_bundle_growth` | orchestrator-approved-bytes; Review additionally compares the unchanged region to the plan base |

The P controls for unchanged stage-routing/SourceLanding text prove a content change trips
the raw pin. They do **not** prove that a newly approved pin is semantically correct.
Ordinary Review's unchanged-region diff and owner-rule inspection are required separately.
Printing a diagnostic is not delivery, and manual output inspection is not a PC assertion.

Before implementation handoff: **guards=79, mapped=79, missing=0,
duplicate PC maps=0**. All **79 PCs / 117 variants** are scheduled, with
no optional controls; executed here **0**. Before the post-land verification handoff, all
PCs/variants must actually run break/red/restore/green and have their receipts. Missing
bindings or runs remain pending/incomplete, never silently excluded to reduce Cost.

### Out of scope

- Runtime transports, runner ceiling implementation, provider quoting/Node hops, Grok rules files, arbitrary appends/attachments, specialist seats and role-policy tool additions. The shipped fixture is a reproducible supported composition, not a universal launch-size proof. Default-budget and existing composer refusal tests stay in scope.
- Queue persistence/recovery, launch/refresh integration, busy/eligible recipient delivery, PTY input, parking and stop behavior. No implementation in those areas changes; text checks cannot certify those runtime invariants. A changed handoff requires replanning as stated in Delivery inventory.
- New tests for every catalog member's allocation; the plan allocates only orchestrator. README prose is reviewed against the actual numbers/rules, not pinned word-for-word. AGENTS/skills/routing settings remain unedited and their existing owner phrase assertions remain.
- Unit namespace/full assembly, database, browser, native Windows and live-provider runs. All selected methods are portable source/resource tests. No suite run, repeated green run or standalone formatter-test suite is authorized.
- RC publication, deployed activation, operational setting writes or notification to users. Master publication is a caller-owned later stage.
- Exhaustive mutations of unrelated runtime gates or all examples in unchanged documentation. The controls target the safety assertions in this plan; existing broader suites remain their owners' responsibility.

### Checkpoints

Use this closed table, not the proposal table in the fix plan. CP-1 closes S1;
CP-2..CP-13 close S2. One isolated build or same-After build reference and one exact
filter per row. Sum of the ordinary execution floors is **45** across both slices
(**4** at S1, **41** at S2); loop iterations/diagnostic lines are not executions.
CP-2 includes the unchanged four owner arguments again to detect S2 relocation damage.

Commit/push the test-only slice before its expected-red run, then the fix before its
ordinary green group. Commission these additional red groups explicitly: S1 uses CP-1's
method (four missing-anchor assertions); S2 uses the CP-2 class (four owner cases green,
four missing-pointer plus two oversized-body cases red) and CP-3's exact method
(29,493 > 28,000 while the existing reserve still accepts). Use separate fresh outputs,
record assertion identities/nonzero counts, and stop at each expected red.
They are test-first evidence, not ordinary green receipts. If input drift changes 29,493,
report the actual measurement; do not change 28,000 or remove reserve proof.

Use the checkpoint tool once per committed slice group with this plan and the exact source
SHA. Await run/wait until the exit is not 75. Bootstrap builds take the host build-slot gate;
exit 4 means not run, never unleased retry. CP-2 output reuse is only at the same committed
S2 SHA with verified build binding. Run the required range evidence-diff check at Code and
Review, retain unedited CHECKPOINT lines and fresh TRX rosters, and keep generated outputs
ignored. Final Review repeats the complete ordinary selection at its exact reviewed tip;
any duplicated S1/S2 owner execution remains explicitly recorded, not relabeled as a new case.
Omit Runner/Platform pins for this portable lane; re-read placement when commissioning.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1332-owner/` | portable-unit-owners | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | V-1 | all 4 cases, 0 failed/skipped | 4 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1332-bundle/` | portable-unit-allocation | `/*/*/OrchestratorBundleOwnershipTests/*` | V-1, V-2, V-3 | 4 owner + 4 pointer + 2 line-ending cases, 0 failed/skipped | 10 | 8 |
| CP-3 | S2 | CP-2 | portable-unit-argv | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | V-4, V-5 | 1 executed, 0 failed/skipped; required diagnostic fields present | 1 | 1 |
| CP-4 | S2 | CP-2 | portable-unit-contract | `/*/*/InstructionBundleTests/(the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact*)\|(orchestrator_bundle_points_to_operational_autonomy_without_growing*)\|(C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs*)\|(C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders*)` | V-3, R-1, R-2 | 5 cases across 4 methods, 0 failed/skipped | 5 | 1 |
| CP-5 | S2 | CP-2 | portable-unit-repair | `/*/*/RepairSourceDocumentationTests/*` | V-2 | all 3 methods, 0 failed/skipped | 3 | 1 |
| CP-6 | S2 | CP-2 | portable-unit-pipeline | `/*/*/StandingPipelinePolicyDocumentationTests/(the_policy_phrases_are_pinned_in_every_copy*)\|(the_bundle_no_longer_states_the_old_override_rule*)` | V-2 | 4 copies + 1 override method, 0 failed/skipped | 5 | 1 |
| CP-7 | S2 | CP-2 | portable-unit-settings | `/*/*/OrchestratorInstructionsGuidanceTests/(The_policy_copies_keep_every_pinned_phrase_and_each_names_the_file*)\|(The_bundle_names_the_file_route_compaction_and_precedence_and_dropped_the_swapped_sentences*)` | V-2 | both methods, 0 failed/skipped | 2 | 1 |
| CP-8 | S2 | CP-2 | portable-unit-platform | `/*/*/TaskPlatformGuidanceTests/(Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes*)\|(Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning*)` | V-2 | both methods, 0 failed/skipped | 2 | 1 |
| CP-9 | S2 | CP-2 | portable-unit-placement | `/*/*/RunnerDefaultGuidanceTests/(Bundles_read_runtime_defaults_without_pinning_location*)\|(Platform_pins_are_explicit_and_bundles_defer_to_orchestrator_contract*)` | V-2 | both methods, 0 failed/skipped | 2 | 1 |
| CP-10 | S2 | CP-2 | portable-unit-pin | `/*/*/CheckpointRepeatDocumentationTests/repeat_budget_reaches_code_briefs_without_bundle_growth` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-11 | S2 | CP-2 | portable-unit-policy | `/*/Antiphon.Tests.Application/(KeepSixDocumentationTests*)\|(PostLandRetrospectiveContractTests*)/*` | R-1 | all 7 methods in the two named classes, 0 failed/skipped | 7 | 1 |
| CP-12 | S2 | CP-2 | portable-unit-fragments | `/*/*/InstructionFragmentTests/C1213_ExpandedBudgetIsEnforced` | R-3 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-13 | S2 | CP-2 | portable-unit-overflow | `/*/*/InstructionBundleTests/(an_oversized_composition_throws_and_names_what_to_shrink*)\|(the_other_arguments_count_towards_the_budget_not_just_the_append*)` | R-3 | both methods, 0 failed/skipped | 2 | 1 |

### Cost

All times below are **estimates**, not measurements. Slot contention, package-cache cold starts
and unexpected inherited failures are excluded and must be reported separately. No current
full-suite wall time is asserted.

- **Ordinary V/R floor (Code): 27 minutes** = CP-1 8 + CP-2 8 + CP-3..CP-13 11 x 1.
  The two building rows include setup/build time for their isolated outputs; do not add those
  builds twice. Code bootstrap allowance = **2 minutes** outside the table. Commissioned
  assertion-red groups = **17 minutes** (S1 owner method 8; S2 ownership class 8 plus argv
  method 1, sharing only that test-only SHA's own build). Code execution allowance =
  **46 minutes** (2 + 17 + 27), plus **60–90 minutes** authoring = **106–136 minutes**.
- **PC floor (Mutation): 702 minutes** = **117 variants x 6 minutes**, using the exact
  filters in the PC table. Per variant allow baseline build/method **2 minutes**, compiling
  defect + red build/method **2 minutes**, restoration + fresh green build/method **2 minutes**.
  This deliberately budgets three method phases rather than pretending a green CP covers a
  future mutated build. Mutation setup/binding/first-build allowance = **8 minutes**;
  execution floor including setup = **710 minutes**. Additional binding analysis/reporting is
  not hidden in ordinary Code time.
- Per-filter allocation of the 702-minute PC floor: **O 46 variants / 276 min**;
  **B 34 / 204**; **L 3 / 18**; **A 7 / 42**; **E 2 / 12**; **P 3 / 18**;
  **F 1 / 6**, **X 1 / 6**, **Y 1 / 6**; **C 11 / 66**; **S 3 / 18**;
  **K 1 / 6**, **T 1 / 6**, **I 2 / 12**, **U 1 / 6**.
  Aliases bind to the exact methods above, not class/suite selections.
- **Required ordinary + PC floors: 729 minutes** (27 + 702).
  **Total commissioned execution including setup and test-first reds: 756 minutes**
  (Code setup 2 + red 17 + ordinary 27 + Mutation setup 8 + PC phases 702).
  Ordinary Review's separate complete selection adds an estimated **27 minutes**, making
  **783 minutes** execution across Code/Review/post-land Mutation, before authoring and analysis.
  Code and Review do not wait for PCs before implementation land; the companion retains them.
- The large PC number is visible because independent safety predicates and required variants
  are individually controlled. It is not permission to run whole classes per mutation or to
  silently delete variants. Calibrate actual phase times on the first bound controls and update
  the companion's estimate with measured evidence, without changing this frozen obligation.
- **Quantified ordinary savings:** same-S2 build reuse avoids eleven redundant isolated builds
  versus rebuilding all thirteen rows. At an estimated seven build-only minutes each, this saves
  **77 minutes** (104 -> 27) while adding the previously omitted two overflow methods.
  No extra PC batching saving is claimed (**0 minutes**): most controls share the same source
  file or detecting method, so they do not satisfy the independent-file/method batching rule.
  No savings are invented against a full suite whose current duration is unmeasured.

## Handoff

The fix plan remains unchanged. Land this addendum and make both artifacts available to Code.
Code implements S1 then S2, binds all new labels/methods, preserves the four/two argument
rosters, runs the commissioned assertion-red groups and the thirteen ordinary rows, and
returns **next: review**. Review checks ordinary evidence, rule relocation and all pending
controls before master land. The caller then commissions binding and all 79 PCs / 117
variants at the actual landed source through the post-land companion. Binding or execution
gaps never become verified by passage through Code.

No human product choice or unverifiable changed delivery seam blocks Code. No build, test
or positive-control result is claimed by this TestDesign artifact.
