# CARD-1046: bind local assertion helpers and exact PC filters

The coverage lint should find assertions reached through a statically bound local
function and bind a PC's literal method filter. Reconcile the two reported plans
using the existing explicit checklist format, while retaining genuine missing
assertions and predecessor findings. Verification design is folded into this
plan; the next implementation stage is Code, with a 50-minute estimate and a
60-minute budget. No operator decision or accepted-unmapped exception is needed.

## Ground truth

Inspected and reproduced at clean base
`87ab9c33a71b2a6d85bcb96ae3fedc871b794f51`, 2026-10-04. The live CARD-1046
description names CARD-1035 and CARD-1037. This is a syntax lint, not executed
coverage or proof that a mutant reaches its intended assertion.

| Card assumption | What the code actually does | Consequence |
|---|---|---|
| Assertion helpers are not bound. | `tools/Antiphon.Checkpoints/Coverage/TestAssertionIndex.cs` indexes only `MethodDeclarationSyntax`. It already expands uniquely resolved same-outer-class methods, including partial declarations, and substitutes literal positional/named/default arguments. | Preserve existing method-helper behavior; add local callables rather than a new analyzer or semantic compilation. |
| CARD-1035's target label exists but is missed. | `Expand` explicitly excludes invocations with a local-function ancestor. Neither `AssertChildren` nor `AssertReceiptAsync` is indexed. Both are called twice by `C1035_ChildCwdMatchesCertifiedRoot`. | Reproduction: exit 1, obligations=1, matched=0, missing=1, unmapped=4, pcIssues=1; PC-1 is missing-target. |
| Indexing local declarations alone will fix that case. | The current arity check caps argument count at parameter count. `AssertChildren(int count, params string[] phases)` is called with three and four arguments. | Local resolution must understand a terminal params array. |
| CARD-1035 should then automatically exit 0. | The phase assertion at `RunCheckpointSourceScriptTests.cs:31` has no message and precedes the target at line 33. `AnalyzePc` correctly refuses an unlabeled predecessor. Most bold V/R headings in the historical plan are not extracted; the reproduction contains only its PC obligation. | Add a truthful phase assertion label, retaining its predicate. Do not suppress the newly visible finding or claim the lint covers that plan's whole runtime sequence. |
| CARD-1037's PC labels are missing from tests. | Its three primary labels are present. `PlanCoverageReader` reads only the last cell of PC rows and never reads their separate exact-filter cell. All four extracted PC obligations therefore have an empty test. | Reproduction: exit 1, obligations=26, matched=11, missing=4, unmapped=11, pcIssues=4. Bind literal filters rather than guessing from prose. |
| All CARD-1037 prose findings need a new binder. | Eleven tokens are input spellings or expected values, not labeled assertions. The existing version-1 checklist can map exact clauses to typed promises without suppressing labels/canaries. PC-3's mutation error code is also mistakenly written as a baseline assertion label. | Reconcile that document with existing typed mappings and distinguish mutation outcomes from baseline assertions; do not expand English heuristics. |
| The implementation is in scripts. | The implementation is `Coverage/{PlanCoverageReader,TestAssertionIndex,PlanCoverageAnalyzer,PlanCoverageReport,CoverageCommand}.cs` under the checkpoint tool. `scripts/run-checkpoint.ps1` executes checkpoints; it is not this binder. | Production changes are limited to the reader and assertion index. Script runtime and receipt formats remain unchanged. |

Plan-time evidence: a host-leased alternate-output tool build succeeded in 28
seconds with 0 errors and the existing CS8602 warning in
`Execution/TaskOwnerGuard.cs:170`. Both real-plan coverage commands completed
with the counts above. No TUnit tests or mutation controls ran during Plan.
The build used `scripts/build-slot.ps1 -Label c1046-plan-lint -- dotnet build
tools/Antiphon.Checkpoints --property:OutputPath=bin-c1046-plan/ --nologo`;
coverage ran via the produced DLL. These are observed baseline findings, not
failures introduced by this documentation change.
The existing tool successfully imported this plan's six checkpoint rows. Full
coverage of this new plan is an S1 check because its five test methods do not
exist yet; no implementation-green result is claimed here.

## Decisions

- **D-1: Extend bounded callable expansion.** Keep `IndexedMethod` and the
  public method/census roster unchanged. Internally represent either a method
  or local function with its body/expression body, parameters, declaration,
  containing class and source path. Reuse assertion extraction and the existing
  depth-12/cycle guard for both declaration kinds. Local functions never become
  standalone tests. Rejected: semantic compilation, execution, a whole-program
  call graph, or indiscriminately scanning local bodies for labels.
- **D-2: Follow calls, with lexical ownership.** Expand a uniquely resolved
  unqualified local-function invocation in place in the existing assertion
  sequence. Resolve the nearest enclosing lexical block first, including a
  declaration after its call; local names shadow class methods. Do not borrow a
  sibling block's declaration, another test's local function, or an uncalled
  local body. Traverse a selected callable's own body while pruning nested local
  declarations, then expand those only at calls. Preserve same-class/partial
  helper resolution. A member-access call cannot accidentally bind a local
  function merely because the name matches. Ambiguity, recursion, excessive
  depth and opaque assertion-helper dispatch retain findings. No broad changes
  to lambda/semantic dispatch or the existing class-method resolver are in scope.
- **D-3: Bind arguments conservatively.** Share positional/named/default
  parameter binding between both callable kinds. Admit a final params array
  with zero, one array, or multiple trailing arguments; collect only supported
  literal evidence. Preserve caller bindings and lexically visible literal
  aliases for captured labels, with local parameters/locals shadowing outer
  names. Do not search sibling/local-function bodies for alias initializers.
  Dynamic return values, ambiguous overloads and unsupported argument shapes
  remain unknown; do not manufacture labels. This covers the exact CARD-1035
  shape and literal helper chaining without general dataflow analysis.
- **D-4: Read an exact PC-filter cell as a binding.** For a PC table with a
  separate detecting-filter cell between its mutation and final expected-red
  cells, recognize a single code span of the form `/*/*/Class/Method` or
  `/*/Exact.Namespace/Class/Method`. Class and method must be literal identifiers
  (qualified class names are allowed); neither may contain wildcard, OR,
  category or command syntax. Produce a class-qualified method identity, using
  the exact namespace when supplied. Leave mutation text out of promise
  extraction and retain expected-red label coordinates from the original cell.
  Preserve legacy explicit-method and V-reference binding. If an explicit
  method and filter disagree, or multiple/unsupported filter candidates are
  supplied, emit a binding finding and never silently choose one. Use a finding
  code counted by the existing report, such as `METHOD_UNMAPPED`; unresolved
  targets must retain exit 1. Rejected: treating a whole-class filter as one
  method or inferring the test from a matching label elsewhere.
- **D-5: Preserve truthful PC ordering.** Inline local assertions at their call
  sites, preserving helper declaration path/line in matches. Keep the existing
  predecessor policy: unlabeled predecessors are issues, different stable
  labels are advisories, and reachability is always unproven. No exit-code,
  JSON schema, census opt-in, digest or receipt changes. Rejected: a general
  accepted-unmapped list, blanket helper exemptions, or changing exit 1 to 0.
- **D-6: Reconcile the reported examples explicitly.** Add only a custom message
  to the phase comparison in CARD-1035's test. In the CARD-1037 plan, use the
  already supported inline checklist for prose values; remove code formatting
  from input spellings only. Preserve every behavioral promise and mutation
  instruction. A mutation's future exception code is not a baseline assertion.
  Do not change old evidence or rewrite old reports as green.
- **D-7: Portable, bounded implementation.** One atomic Code slice changes the
  two binder files, focused tests and documentation. No new dependency, service,
  runtime launch policy, accepted-findings configuration or deployment is needed.
  Build/run the tool from the committed checkout. Omit runner and platform pins;
  no source or plan embeds a fleet location.

## Implementation footprint and slices

**S1 — Binder, regressions and explicit example reconciliation (one commit).**

1. Change `tools/Antiphon.Checkpoints/Coverage/TestAssertionIndex.cs` for D-1
   through D-3 and D-5, and `Coverage/PlanCoverageReader.cs` for D-4. Prefer a
   small private callable adapter over duplicating the assertion visitor. Keep
   `PlanCoverageAnalyzer`, `CoverageCommand` and report contracts unchanged.
2. Add `tests/Antiphon.Tests/Checkpoints/PlanCoverageBindingTests.cs` containing
   exactly the five non-parameterized tests below. Reuse `PlanCoverageFixture`
   and `CheckpointTestBase` for temporary worlds. Keep test source snippets as
   inert strings; they must never compile or execute as selected source.
3. Update `scripts/lib/checkpoint-usage.ps1`'s independent checkpoint census for
   those five added cases (377 at this base, therefore 382 absent intervening
   additions). Reconcile any intervening change rather than copying a stale
   total. Keep this script ASCII-only.
4. In `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs`, give the
   existing phase comparison the message `child-phase-matches-request`.
   Do not alter the compared values, launch sequence or target label.
5. In `docs/superpowers/plans/2026-10-04-card-1037-remote-follow-up-launch-guard-plan.md`,
   remove backticks only from input spellings local/DESKTOP in V-1/V-2. Add an
   inline version-1 checklist with exact original-clause mappings and current
   line numbers for the table below. Keep the method on each mapping identical
   to the row's bound method. Change PC-3's expected-red prose to say the new
   refusal occurs, leaving its stable error code as prose or in the mutation
   cell, not as an additional baseline label. Keep the target label formatted.
6. Update `docs/testing-and-build.md`, Static plan-to-test coverage, to specify
   called local functions, supported params shapes, exact PC filters and the
   explicit-checklist recipe. In the CARD-1035 plan's execution paragraph,
   replace the prospective blanket allowance for unmapped harness assertions
   with the requirement to reconcile individual findings; retain historical
   evidence and commands as historical text. Commit/push before verification.

CARD-1037 mappings (values are assertion expectations, not input enumeration):

| Row | Exact legacy clause | Typed checklist promises |
|---|---|---|
| V-1 | `ValidationException.StatusCode == 422` | member StatusCode and value 422 |
| V-1 | `follow_up_remote_pool_unsupported` | value follow_up_remote_pool_unsupported |
| V-1 | `FollowUpOnTask` | value FollowUpOnTask |
| V-1 | `-Worktree` | value -Worktree |
| V-1 | `-StartRef` | value -StartRef |
| V-1 | `-OnAgent` | value without -OnAgent, matching the actual asserted token |
| V-3 | `workspace_existing_agent_conflict` | value workspace_existing_agent_conflict |
| R-4 | `phone_home_worktree_refused` | value phone_home_worktree_refused |

For example, each checklist item uses version-1 fields id, test, kind, name,
planLine and maps. Two items may map the same status clause. Existing mapping
merges remove only the corresponding unmapped prose; labels and canaries must
remain obligations. No external checklist or schema extension is needed.

## Verification design

### Inspection and setup

Before editing, read the two binder files, `PlanCoverageAnalyzer.AnalyzePc`,
`PlanCoverageReport.Summary`, the existing assertion/parser/PC/golden tests,
`ClassCensusSelection`, and the three real example methods named above. The
baseline observations already isolate the problem; no further production probe
or broad Unit run is necessary. Fixtures use controlled C# strings and Markdown,
with original line coordinates asserted. A whole real plan is also linted in
V-5 so fixture-only success cannot hide the actual params/filter/prose shapes.

### Delivery inventory

Synchronous local CLI: plan/selected source -> reader/index/analyzer -> text or
JSON plus process exit. There is no queue, database, session message, provider
call or asynchronous user delivery. Existing read-only CLI regression tests
guard driver inactivity and deterministic output. No receipt of user delivery
or dynamic mutant reachability is claimed by this lint.

### Proves it works now

All five methods below belong to the new Unit class, one TUnit result each.
Internal case loops do not increase checkpoint execution floors. Give decisive
assertions literal messages as listed; keep setup/precondition assertions labeled
so this plan's own PCs have only advisory predecessors.

| ID | Test | Required observations |
|---|---|---|
| V-1 | `PlanCoverageBindingTests.called_local_functions_and_params_bind_at_call_site` | Label `c1046-local-bind`. A called local helper with the CARD-1035 signature binds the target in both three- and four-argument calls. Cover block and expression bodies, async local calls, positional/named/default literal arguments, zero/multiple/explicit-array params, one captured literal label and one local-to-class helper chain. Assert actual match declaration paths/lines and exit 0, not merely absence of HELPER_UNMAPPED. |
| V-2 | `PlanCoverageBindingTests.local_helper_scope_does_not_invent_evidence` | Label `c1046-local-scope`. Negative worlds: uncalled local target, target in another test, same-name helper in a sibling block, an in-scope shadowing local without the target, opaque receiver dispatch, ambiguous callable and local recursion/depth overflow. Require missing or unmapped findings and exit 1; no target match may be borrowed from a decoy. A correctly called nearest-scope helper remains green. |
| V-3 | `PlanCoverageBindingTests.literal_pc_filter_binds_method_without_reading_mutation_cell` | Label `c1046-pc-filter`. Four-column PC rows bind literal class/method filters, with wildcard or exact namespace; the mutation cell contains a decoy method/label that must not become a promise. Assert test identity, original label line/column, target match and unproven reachability. Keep legacy explicit-method/V-reference shapes. Conflicting bindings, multiple filters and wildcard/OR/category/command method selections remain findings, never a clean guessed binding. |
| V-4 | `PlanCoverageBindingTests.local_helper_predecessors_keep_order_and_reachability` | Label `c1046-pc-order`. Put a local declaration before the caller's earlier assertions and invoke it later. With an unlabeled assertion before the target inside the helper, assert exact predecessor/target lines, issue status and exit 1. Label that predecessor in the input string and assert only advisories, static-labeled status and exit 0. Delete the target from the input string and require missing-target. Every variant retains unproven reachability. |
| V-5 | `PlanCoverageBindingTests.historical_plans_bind_without_suppression` | Label `c1046-real-plans`. Run CoverageCommand in-process against the real reconciled CARD-1035 and CARD-1037 plans and actual selected files. Each exits 0, has no missing/unmapped obligations, and has only static-labeled PCs with unproven reachability (one target for CARD-1035, three primary targets for CARD-1037). Compare two analyses for deterministic results. Inspect target matches, not just summary counts. No source execution or PostgreSQL fixture is needed. |

### Guards the regression

| ID | Test selection | Purpose |
|---|---|---|
| R-1 | PlanCoverageAssertionTests (5), PlanCoverageParserTests (8), PlanCoveragePcTests (4), PlanCoverageGoldenTests (4), exactly their existing methods | Preserve class/partial helper literals, named arguments, unresolved helpers, assertion association, clause mappings, stale-checklist refusal, coordinates, historical findings and predecessor policy. These 21 source-counted methods are the CP-2 closed roster. Do not loosen raw golden expectations to accommodate a false positive. |
| R-2 | `PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract` | Checkpoint import remains unchanged. |
| R-3 | `PlanCoverageCommandTests.renders_stable_text_json_and_exit_codes` | Preserve schema, deterministic ordering, digests and 0/1/2 results. |
| R-4 | `PlanCoverageCommandTests.coverage_never_starts_driver_or_writes_run_state` | Syntax lint remains read-only. |
| R-5 | `PlanCoverageCensusTests.census_excludes_helpers_and_counts_parameterized_method_once` | Helpers remain outside the test census. |
| R-6 | `PlanCoverageCensusTests.census_unions_overlapping_filters_and_partial_declarations` | Preserve declaration identity and partial-class selection. |
| R-7 | `PlanCoverageCensusTests.opt_out_preserves_legacy_reports_and_declared_counts` | No accidental selected-census opt-in or old-plan count changes. |
| R-8 | `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases` | The independent namespace census tracks the five real additions. |
| R-9 | `RunCheckpointSourceScriptTests.C1035_ChildCwdMatchesCertifiedRoot` | One existing script Integration method confirms the custom-message-only edit leaves actual child cwd, phases, reuse and clean receipts intact. |

### Guard inventory and positive controls

Four changed behaviors, one PC each. V-5 is their integration acceptance, not
a fifth independently changed guard. Existing checklist merge and report rules
receive regression coverage; this card does not mutate those unchanged rules.
Guards=4; mapped=4; missing=0; duplicate maps=0.

| Guard | Behavior | Control |
|---|---|---|
| G-1 | A supported called local helper, including params arity, supplies assertion evidence. | PC-1 |
| G-2 | Evidence belongs to the callable reached in lexical scope. | PC-2 |
| G-3 | An exact PC-filter cell binds its detecting method. | PC-3 |
| G-4 | Expanded assertions retain call-site predecessor order and diagnostics. | PC-4 |

| PC | Compiling production mutation | Exact detecting method and expected red |
|---|---|---|
| PC-1 | Disable only local-callable expansion, retaining class-method expansion. | `PlanCoverageBindingTests.called_local_functions_and_params_bind_at_call_site` fails at label `c1046-local-bind`. |
| PC-2 | Admit a same-name local from a sibling lexical block as a candidate for an otherwise unresolved call. | `PlanCoverageBindingTests.local_helper_scope_does_not_invent_evidence` fails at label `c1046-local-scope` because the sibling decoy supplies forbidden target evidence. |
| PC-3 | Ignore the separate literal-filter cell while retaining the old expected-red-cell parser. | `PlanCoverageBindingTests.literal_pc_filter_binds_method_without_reading_mutation_cell` fails at label `c1046-pc-filter`. |
| PC-4 | In the new local expansion path, discard unlabeled assertions before returning evidence to the caller. | `PlanCoverageBindingTests.local_helper_predecessors_keep_order_and_reachability` fails at label `c1046-pc-order` because its required predecessor issue disappears. |

After ordinary Review and implementation land, a separately commissioned
SourceLanding Mutation performs baseline/red/exact-restoration/green for each
PC. Each phase uses precisely `/*/*/PlanCoverageBindingTests/<the one named
method>`; never run the class for a PC. These mutations overlap one source file
except PC-3; execute sequentially for the bounded budget. Require the intended
assertion red, not compile/fixture errors or zero tests. Keep source frozen while
running, preserve per-PC receipts externally and restore all tracked bytes.
The snapshot does not commit/push. Ordinary Code runs no deliberate mutations.

### Execution lane and evidence

Read `GET /api/runner-defaults` and `GET /api/session-runners` immediately before
dispatch. Plan read both at approximately 19:14 UTC on 2026-10-04: revision 2
preferred an available eligible Linux runner; a Windows runner was also eligible
and an alternate entry was stale/draining. These are observations, not routing
constants. Omit `-Runner` and `-Platform`; use `-Platform Any` only to clear a
future OS pin. CP-1 through CP-5 use the portable checkpoint-tool Unit lane;
CP-6 uses the portable script Integration lane with fake dotnet children, Git and
PowerShell. No second-OS or whole-Unit qualification is required.

Commit/push all S1 source first. Supporting bootstrap and syntax commands are
explicitly budgeted outside the TUnit rows below. From the checkout root:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1046-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1046-tool/ --nologo
$tool = 'tools/Antiphon.Checkpoints/bin-c1046-tool/Antiphon.Checkpoints.dll'
$plan = 'docs/superpowers/plans/2026-10-04-card-1046-coverage-binder-plan.md'
$sha = (git rev-parse HEAD).Trim()
dotnet $tool coverage --plan $plan
dotnet $tool coverage --plan docs/superpowers/plans/2026-10-04-card-1035-checkpoint-child-cwd-plan.md
dotnet $tool coverage --plan docs/superpowers/plans/2026-10-04-card-1037-remote-follow-up-launch-guard-plan.md
dotnet $tool run --plan $plan --after S1 --expected-source-sha $sha --max-wait 50s
```

All three syntax checks must exit 0 after reconciliation. Different-label PC
advisories are retained and explained, never reported as dynamic reachability.
The explicit built-DLL invocation avoids an implicit unleased tool build; the
checkpoint tool owns row build slots. If run/wait returns 75, continue foreground
`wait --run <run-id> --max-wait 50s` on the same DLL until terminal. A slot timeout
is not-run, never permission to bypass the lease. Do not edit source in flight.
After a fix, commit/push and rerun only the affected listed rows, retaining their
source/build provenance; do not silently reuse output stamped at an older SHA.

Code and Review preserve complete static-lint outputs and unedited CHECKPOINT
lines with actual SHA, counts and provenance. Validate the resulting report
with `scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json>
-ExpectedSourceSha <sha> -Rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6`; run
`scripts/check-evidence-diff.ps1` over the full candidate range. Evidence stays
ignored. Delete task-owned alternate outputs across projects only after all
owned processes exit. Confirm an inherited red with its exact filter at the
recorded base; do not rerun an assembly or widen timeouts/assertions.

### Cost and exclusions

Ordinary checkpoint floor: 14 minutes, one test-project build reused by five
rows, 34 TUnit executions (5 new + 29 existing), no repeats after green.
Allow 2 minutes for leased tool bootstrap/three syntax checks, 30 minutes for
implementation/tests/docs and 4 minutes for receipts/report/cleanup: **50 minutes
Code**, ceiling **60**, excluding reported slot waits. Separate Review uses the
same 14-minute checkpoint floor and its own source-bound tool build/lint.
Separate Mutation estimate: four method-scoped cycles at 4 minutes each plus
6 minutes shared setup/restoration, **22 minutes**. These are estimates, not
measured test durations; Plan measured only the 28-second tool bootstrap.

Excluded: whole Unit/assembly/namespace runs, general C# semantic analysis,
arbitrary English-to-assertion inference, delegate/lambda reachability redesign,
cross-type helper discovery, runtime executor/receipt changes, broad historical
plan migration and new suppression schemas. If a new required behavior exceeds
this boundary, report the concrete missing case and revised cost instead of
quietly broadening Code. The two original plan files receive only the stated
reconciliation; their historical runtime evidence remains historical.

### Checkpoints

Code reconciled CP-2/3/4 OR operands after fresh zero-test TRX results: the pinned TUnit discovery requires a trailing wildcard in each operand (CARD-0403). The intended class/method roster and execution floors remain exact; inspect every executed name for suffix-pattern over-selection.

All rows close the same committed S1. CP-1 through CP-5: portable checkpoint-tool
Unit lane. CP-6: portable script Integration lane. Roster counts below count
native TUnit results, not fixture loops or the script's copied three-result TRX.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1046/` | binding-regressions | `/*/Antiphon.Tests.Checkpoints/PlanCoverageBindingTests/*` | V-1, V-2, V-3, V-4, V-5 | all 5 named methods, 0 failed/skipped | 5 | 8 |
| CP-2 | S1 | CP-1 | binder-legacy-contract | `/*/Antiphon.Tests.Checkpoints/(PlanCoverageAssertionTests*)\|(PlanCoverageParserTests*)\|(PlanCoveragePcTests*)\|(PlanCoverageGoldenTests*)/*` | R-1 | all 21 existing methods, 0 failed/skipped | 21 | 1 |
| CP-3 | S1 | CP-1 | coverage-cli-contract | `/*/Antiphon.Tests.Checkpoints/PlanCoverageCommandTests/(coverage_command_preserves_existing_import_contract*)\|(renders_stable_text_json_and_exit_codes*)\|(coverage_never_starts_driver_or_writes_run_state*)` | R-2, R-3, R-4 | all 3 named methods, 0 failed/skipped | 3 | 1 |
| CP-4 | S1 | CP-1 | coverage-census-identity | `/*/Antiphon.Tests.Checkpoints/PlanCoverageCensusTests/(census_excludes_helpers_and_counts_parameterized_method_once*)\|(census_unions_overlapping_filters_and_partial_declarations*)\|(opt_out_preserves_legacy_reports_and_declared_counts*)` | R-5, R-6, R-7 | all 3 named methods, 0 failed/skipped | 3 | 1 |
| CP-5 | S1 | CP-1 | checkpoint-census | `/*/Antiphon.Tests.Checkpoints/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | R-8 | named method, 0 failed/skipped | 1 | 1 |
| CP-6 | S1 | CP-1 | child-phase-label | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests/C1035_ChildCwdMatchesCertifiedRoot` | R-9 | named method, 0 failed/skipped | 1 | 2 |

## Publication and handoff

This Plan task commits and pushes only its assigned task branch. The caller
lands this documentation commit on master promptly through managed landing,
then dispatches Code with this artifact and its exact landed SHA. The runner
branch contract forbids directly pushing master or rebasing the task branch;
publication on master is therefore caller-owned. No application restart is
required for the plan or for a tool subsequently run from its built checkout.
