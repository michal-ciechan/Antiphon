# TestDesign completeness before handoff

Date: 2026-09-09. Stage: Plan. Task: `0f3c3d07`.
Verification design is a separate TestDesign dispatch. No owning card ID was supplied; the filename uses the task ID rather than inventing a card number.

Tighten the TestDesign standing contract so `next: code` requires a recorded completeness check: inspected test bodies/fixtures, an exhaustive guard-to-PC inventory, executable cases, and a numeric verification floor with a numerical basis. This is an instruction-bundle change; it does not add a server-side report validator.

## Ground truth

Baseline inspected: `c8b27d2e`.

| Brief assumption or question | Observed source | Consequence |
|---|---|---|
| The cost requirement needs adding. | `server/Bundles/stage-test-design.md` already requires `### Cost` under `## Verification design`, with `verification floor ~ <N> min`. Its final handoff condition only says Build must be able to execute without inventing anything. | Preserve the existing heading and floor wording; explicitly make a filled numeric floor and its basis a pre-handoff check. |
| Every safety guard should already receive a PC. | The invariant already says every guard protecting a safety-critical assertion gets a PC-n. The template has individual PC rows, but no inventory or reconciliation back to the plan. | Add a guard inventory and require full-plan reconciliation, distinct mappings, and resolved PC references. Merely counting existing PC rows cannot establish completeness. |
| Existing test fixtures are inspected before names are fixed. | The bundle says to read the plan first, but does not require reading test bodies or fixtures. | Require reading every existing test file the plan touches and its relevant fixture/helper implementations before finalizing V/R case names, with a concise inspection record. |
| The bundle can grow without affecting delivery. | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` caps every stage body at 2,500 characters, requires ASCII, and excludes provider names/current incident state. The same class checks catalog composition and command-line budgets. | Keep the new standing text compact, ASCII and provider-neutral; preserve the cap. |
| An edit immediately updates running agents. | `server/Bundles/README.md` and `InstructionBundles.Load` describe embedded, LF-normalized, content-hashed text composed at launch. | No version constant changes. Code reports `restart: server`; the caller handles landing/restart, and a fresh delegate launch receives the rebuilt text. Existing work is not retroactively repaired. |
| PC ownership is stable across nearby work. | The checked-out bundle still says Build runs PCs. The separate `2026-09-09-card-0470-code-mutation-split-plan.md` D-6 proposes Mutation ownership and separate ordinary/PC cost floors while retaining the total. | Reconcile that exact overlap when implementing; preserve whichever ownership contract has actually landed. This task changes completeness, not the stage pipeline. |

`tests/Antiphon.Tests/Application/InstructionBundleTests.cs` was read in full, including method bodies and its local `CountOccurrences` helper. It is a pure Unit-category suite using the real embedded catalog/composer, without a database/pty fixture. Existing stage invariant pins cover the PC rule and unchanged fix-design boundary, but do not prove that an agent obeys the proposed completion check.

The debug findings in the supplied brief are accepted as the incident evidence. CARD-0466's implementation, late additions, outstanding verification execution, and historical plan are outside this task.

## Decisions

### D-1: Make handoff eligibility explicit in the canonical bundle

Add a final self-check immediately before the `next:` instructions, and require the verification design to record its outcome. `next: code` is allowed only after every check passes. Finish ordinary omissions within TestDesign; return `next: plan` when the design cannot be verified, naming the gap. Keep `next: decide` for human defaults and the existing reporting/commit contract.

This is an agent obligation that can be reviewed in the artifact. Rejected: claiming settlement will reject incomplete reports; adding a Markdown parser, API/state changes, or an automatic scheduling/cost gate. Those are separate implementation work and are not necessary to tighten this bundle.

### D-2: Require a numeric floor with a usable basis

Keep `### Cost` nested under the existing `## Verification design`; do not introduce a competing top-level cost heading. Require the existing suites/filters list, an explicit numeric total in minutes, and component estimates for setup/build, V/R execution, and every PC red/restore/green cycle. Identify estimates versus measured timings. The components must support the stated total; explain any shared setup or concurrency saving rather than silently double-counting or omitting work.

A missing block, `<N>`, `TBD`, prose-only duration, or an unexplained total cannot pass. A numeric zero needs an explicit reason and must not hide prescribed execution. Verification time is a floor, not the complete Code authoring estimate or permission to skip rows. If CARD-0470 lands first, retain its ordinary/PC floors and require the same numerical basis plus the total.

Rejected: another example-only Cost block, a fixed 60-minute default, or requiring new benchmark runs during every TestDesign. Estimates are acceptable when labeled and broken down.

### D-3: Inventory the plan's guards before reconciling PCs

Add `### Guard inventory` with `G-n | plan reference + guard/invariant | PC-n` information. Enumerate every safety-critical guard/invariant named anywhere in the plan, including rejection paths and ownership checks, not only guards already represented in the V/R or PC list. Split predicates that can independently be bypassed; each needs its own PC ID and deliberate mutation, even when the same test method can detect several.

Reconcile the inventory against the plan, then the inventory against fully specified PC rows. Record guard count, mapped count, zero missing mappings, and zero duplicate PC mappings. Counts supplement the source references; they cannot replace the completeness review. A missing or non-executable PC is a gap to resolve, not an Out of scope escape for a safety guard. An empty inventory must state why no safety-critical guards apply. Additional optional controls may still be designed.

Rejected: one umbrella PC for multiple guards, inferred coverage from passing V/R cases, or adding historical CARD-0466 examples to every future agent's prompt.

### D-4: Read fixtures and expose relevant boundary combinations

Add a lightweight `### Inspection` record: each planned existing test file and relevant fixture/helper bodies read, plus boundary combinations mapped to V/R IDs or an explicit exclusion reason. Filename discovery, existence checks and method-signature searches do not satisfy it. For a new test file, inspect the nearest reusable fixture; if none exists, record that fact and the proposed setup.

Use the plan and fixture data to identify meaningful combinations, such as historical/current/same-current selection or source-owner/target-owner permutations where those dimensions exist. Do not mandate an exhaustive Cartesian product or unrelated repository-wide reading. The record must make an omitted relevant boundary visible before Code discovers it.

### D-5: Keep the change small and merge-safe

Implement only the standing bundle and the owning orchestration document's list of TestDesign subsections. Preserve existing invariants, V/R structure, execution/restoration obligations, routing vocabulary, provider neutrality and content-hash versioning. Do not rewrite other stage bundles, historical plans, generated `docs/cards/` files, runtime code, or test infrastructure.

No new tests that merely mirror prompt wording are needed for this prose change. Use the existing bundle contract suite for packaging and invariant regressions, plus semantic review of the explicit acceptance examples below. A focused green suite demonstrates that the prompt still ships correctly; it does not establish universal agent compliance.

## Proposed bundle text

The following replacement is concrete for the inspected baseline. Its LF-normalized, trimmed body is 2,267 ASCII characters (cap: 2,500). When resolving CARD-0470's overlapping edit, carry these completeness obligations forward into the landed version instead of restoring old PC ownership or deleting its cost split.

```markdown
You are writing the verification design for a landed plan.

INVARIANTS: Read the plan doc first. Append `## Verification design`; do not rewrite the fix design. Every guard that protects a safety-critical assertion gets a PC-n positive control.

Before finalizing V/R case names, read every existing test file the plan will touch, including test bodies and relevant fixtures/helpers; locating files or signatures is insufficient. For new files, read the nearest reusable fixture.

Required sub-structure:

## Verification design
### Inspection
- <test file + fixtures/helpers read> | <boundary combinations -> V/R IDs, or exclusion reason>
### Proves it works now
- V-1: <behaviour> | <layer: unit | integration | E2E | live probe> | <test or command> | <expected>
### Guards the regression
- R-1: <future change that would reintroduce the defect> | caught by <test> because <assertion>
### Guard inventory
- G-1: <plan reference + safety-critical guard/invariant> | PC-1
Inventory every safety-critical guard/invariant named in the plan, not just those with tests. Map each 1:1 to a distinct PC-n; split independently bypassable guards even if they share a test. If none, say why.
### Positive controls
- PC-1: break <G-1 guard> by <one-line edit>; expect <exact test method> red at <assertion>
  Build runs each: break, see red, revert, see green, and reports all three.
### Out of scope
- <what is deliberately not tested, and why>
### Cost
- suites forced: <assemblies / filters>; verification floor ~ <N> min
- basis: <setup/build + V/R runs + every PC red/green cycle, in minutes; estimated or measured>

Before handoff, check and record: all planned test files/fixtures read; inventory reconciled against every safety-critical guard/invariant in the plan; guards=N, mapped=N, missing=0, duplicate PC mappings=0; all referenced PCs defined and cases executable; Cost has a numeric total in minutes supported by its breakdown. Missing Cost, placeholders/TBD, or unmapped guards make the design incomplete. A zero floor needs an explicit reason.

next: code only after these checks pass. Complete omissions before handoff; plan when the design as written cannot be verified (name the gap); decide when defaults need a human.

Commit and push the updated plan doc.
```

## Implementation slice

| Slice | Files | Work and validation |
|---|---|---|
| S1: Tighten the standing contract | `server/Bundles/stage-test-design.md`; `docs/orchestration-loop.md` section 3's TestDesign substructure list | Apply D-1 through D-5; add Inspection and Guard inventory to the owner's list while leaving detailed rules in the bundle. Recheck ASCII/2,500-character limit, existing invariants and the separate CARD-0470 overlap. Run the existing `InstructionBundleTests` class and review the acceptance examples. |

Existing verification target: `tests/Antiphon.Tests/Application/InstructionBundleTests.cs` (read, run; no edit planned). Preserve its size cap and command-line budget assertions. No client, runner, database, E2E, provider launch or live-broker checks are required for this instruction-only scope.

Use the repository's foreground TUnit path and an owned alternate output ending in a forward slash, for example:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-tdcomplete/ -- --treenode-filter "/*/*/InstructionBundleTests/*"
```

Record nonzero executed counts and failures; preserve only the task-owned output inventory for cleanup under `docs/testing-and-build.md`. An observed source mismatch already exists in `delegate_basics_carries_the_standing_rules_and_none_of_the_days_state`: the assertion expects `COMMIT AND PUSH EACH SLICE`, while the current bundle says `COMMIT AND PUSH EACH MEANINGFUL SLICE OR FIX`. This is an unexecuted baseline observation, not a confirmed failing run. If it fails, verify that exact method at the base commit and report it; do not alter unrelated delegate-basics wording or weaken its test in this slice.

## Acceptance examples for TestDesign

TestDesign should append the executable V/R/PC design to this document after it lands. It should use the existing suite and a bounded semantic review, without starting a live agent experiment or inventing unrelated product mutations. This task changes no executable safety guard; any empty applicable guard inventory should state that reason explicitly.

| Example | Required design-review verdict |
|---|---|
| V/R/PC cases are complete but Cost is absent, still contains `<N>`/`TBD`, lacks numeric units, or has no component basis. | Incomplete; cannot hand off to Code. Adding just a Cost heading does not cure it. |
| Two independently bypassable safety guards are in the plan, but the inventory omits one; or it lists both but maps both to PC-1. | Incomplete even if V/R is green. Reconcile the entire plan, provide distinct PCs with mutations and expected assertions, and record zero missing/duplicate mappings. |
| Every inventory row references a PC, but a referenced PC has no definition or executable detecting assertion. | Incomplete; a label alone is not a positive control. |
| The design lists file paths/signatures but has not read a planned test file's bodies or its relevant fixture, or silently omits an applicable boundary combination. | Incomplete; inspect the bodies/setup and record the combination's V/R coverage or justified exclusion before fixing case names. |
| No existing test file or safety-critical guard applies. | Explain the empty inventory and nearest-fixture search/new setup; still provide executable cases and a justified numeric cost floor. Do not fabricate irrelevant guards. |
| Inspection is evidenced, every plan guard maps to a distinct executable PC, V/R is executable, and component estimates support the numeric floor. | Eligible for `next: code`; estimates may be approximate and labeled. |

## Delivery and next stage

This Plan dispatch changes only this artifact, then commits and pushes it. Land the plan through the caller's normal worktree operation before dispatching TestDesign. TestDesign adds `## Verification design`; Code later implements S1. There are no unresolved user decisions.

Implementation rollout is the normal bundle rebuild/server restart followed by a fresh delegate launch, owned by the caller. Do not restart shared services from this planning worktree.

## Verification design

TestDesign task `4ee2f764`, 2026-09-09, inspected at `2284bf1d`. This section specifies Code's verification of S1 without changing D-1 through D-5. It adds no product tests, test infrastructure or runtime validator. The existing suite verifies packaging and established invariants; the bounded semantic review verifies whether the new instructions actually require the intended decisions. Neither proves universal agent compliance.

### Inspection

| Source read | Bodies / setup inspected | Relevant boundaries and coverage |
|---|---|---|
| `tests/Antiphon.Tests/Application/InstructionBundleTests.cs` | Entire file, every test body and argument row, including local `CountOccurrences`. Pure `[Category("Unit")]` tests exercise the real embedded catalog/composer. No database, clock, runner, external fixture or DI setup is used. This is the only existing test file selected by S1; it will be run, not edited. | TestDesign versus other stage/helper/specialist roles; body size and ASCII; normalized/hash-versioned resources; composition/deduplication and total argument budget: V-1, R-1/R-2. Existing delegate-basics assertion/source mismatch: R-3. |
| `server/Application/Services/InstructionBundles.cs`; `InstructionBundleComposer.cs` | Catalog loading, LF normalization/trim, SHA-256 version, `ForDelegate`/`StageKeyFor`, rendering, deduplication, complete command-line budget and drift comparison bodies read. These are the suite's actual collaborators, not new test fixtures. | Freshly rebuilt embedded text versus checked-out markdown or an already-running agent; unchanged role map/version mechanism: V-1, R-2, V-5. No edits to these implementations are planned. |
| `server/Bundles/stage-test-design.md`; `server/Bundles/README.md`; `docs/orchestration-loop.md` section 3 | Current complete TestDesign body, bundle ownership/delivery rules and the owner's exact subsection-list paragraph read. The delegate-basics commit paragraph was also read to assess the named baseline mismatch. | Instruction obligation versus server enforcement; adding the two subsections without copying the full contract; unchanged reporting/commit contract: V-2..V-5, R-3/R-4. |
| This complete plan, including all six acceptance-example rows; CARD-0470 D-6/D-7 ownership/cost contract and current stage-file state | Reviewed the proposed text, all decisions, scope and acceptance examples. At this baseline, stage-test-design still assigns PCs to Build and `stage-mutation.md` does not exist; the presence of CARD-0470's plan is not evidence that its implementation landed. | Numeric/missing/placeholder/unexplained cost, one-to-one/omitted/duplicate/undefined controls, read-body versus signature-only inspection, explained versus unexplained empty inventories, applicable versus excluded boundaries, and ownership before/after CARD-0470: V-2/V-4 and the semantic cases below. |

There is no new test file or fixture to design. Historical/current/same-current selection and source-owner/target-owner are examples for other plans, not dimensions implemented by S1; reopening those CARD-0466 cases is excluded. The applicable boundaries for this card are explicitly listed above and exercised below rather than a product-state Cartesian matrix.

### Proves it works now

All V/R execution below belongs to Code after S1. TestDesign records inspection and validates this artifact only. Use one full run of the focused existing class for the overlapping automated IDs; do not rerun the class for each row. Record per-ID outcomes even when one command covers several IDs.

| ID | Behavior and layer | Exact check | Required evidence / expected result |
|---|---|---|---|
| V-1 | Revised prompt still packages and composes correctly; Unit | Run `InstructionBundleTests` with the command below. In particular require executed results for `each_stage_bundle_is_ascii_and_under_the_size_cap` including its stage-test-design argument, `stage_bundle_invariants_are_pinned_by_substring`, `a_stage_worker_carries_its_stage_bundle_then_the_basics` including TestDesign, and both existing realistic/worst-case composition budget methods. | Fresh, nonzero executed counts; updated TestDesign body is ASCII and <=2,500 normalized characters; existing safety-PC, Positive controls and unchanged-fix-design pins remain; stage bundle precedes delegate-basics; current catalog/size/argument limits remain intact. Record implementation SHA and owned build output. Preserve any already-landed CARD-0470 catalog/test changes rather than imposing this plan's old key list. |
| V-2 | Completeness is mandatory before next Code; bounded semantic review | Read the final edited bundle and apply every case S-1..S-12 below, including all named variants. Record expected verdict, actual verdict and the exact bundle sentence(s) that require it in the evidence file. | The final text explicitly demands a recorded self-check, complete inspection/case/guard accounting and justified numeric minutes before `next: code`. Every invalid example is incomplete, every valid one eligible, without importing a missing requirement from this plan or the reviewer's preferences. Ambiguous/optional wording fails V-2 even if V-1 passes. |
| V-3 | Canonical subsection list stays in sync; document review | Read `docs/orchestration-loop.md` section 3 beside the final bundle. | Owner lists Inspection, V-n, R-n, Guard inventory, PC-n, Out of scope and Cost, in the bundle's order. Detailed checking rules remain canonical in the bundle. No competing top-level `## Cost` instruction; original `## Verification design` and `### Cost` nesting remains. |
| V-4 | CARD-0470 merge preserves ownership and cost semantics; source/diff review | Record Code's base SHA and read the actually landed stage-test-design, stage-code, delegate-basics and relevant owner paragraph before editing; compare the final S1 diff to that base. | If the split has landed, retain Mutation PC ownership, ordinary and PC floors plus numeric total; never copy the proposed old `Build runs each` sentence over it. If it has not landed, retain the existing owner and do not introduce a new role/token/pipeline in S1. In both cases the cost basis accounts for all prescribed work and S-12 passes. A CARD-0470 integration/rebase change requires repeating this review and the affected class once, not silently taking one entire side. |
| V-5 | Scope and delivery remain as planned; diff/contract review | `git diff <code-base-sha> HEAD --name-only` and the full two-file diff, followed by a read of the final handoff instructions. | S1 changes only `server/Bundles/stage-test-design.md` and section 3's subsection list in `docs/orchestration-loop.md`. No tests/runtime/config/other bundles/historical plans are changed. Existing read-plan/append-only, reporting, commit/push and plan/decide routing meanings remain. Code reports `restart: server`; caller-owned rebuild/restart and a subsequent fresh launch are still needed for rollout. Do not claim the running server or existing agents already carry the new text. |

**Focused execution and evidence.** From Code's worktree, after its checkpoint commit, run in the foreground:

```powershell
$verificationRoot = 'C:\Antiphon\verification\card0471'
$verificationRun = Join-Path $verificationRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verificationRun -Force | Out-Null
git rev-parse HEAD | Set-Content -LiteralPath (Join-Path $verificationRun 'commit.txt')
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-tdcomplete/ -- --treenode-filter '/*/*/InstructionBundleTests/*' --results-directory $verificationRun --report-trx --report-trx-filename bundle-tests.trx *> (Join-Path $verificationRun 'bundle-tests.log')
$verificationExit = $LASTEXITCODE
Set-Content -LiteralPath (Join-Path $verificationRun 'exit.txt') -Value $verificationExit
Write-Output "Bundle tests exit=$verificationExit evidence=$verificationRun"
```

Use a task-owned alternate output and record its inventory; if `bin-tdcomplete/` already belongs to another producer, choose `bin-tdcomplete-4ee2f764/` consistently instead. Never use `--no-build` after editing the embedded bundle, and do not edit source while the run is active. Inspect the fresh TRX's executed method names, parameter cases and counts; exit zero alone or `--list-tests` is not execution evidence. Build/fixture failures and zero tests are verification failures. Preserve the actual native exit code, including a red suite, and apply R-3 rather than declaring success. The single class is the required suite, not the full assembly, namespace, client, E2E or Pty suites.

Write the bounded semantic and scope review to `<verificationRun>\semantic-review.md`, recording the tested commit, final LF-normalized bundle length, S-case verdicts/references, V-3/V-4/V-5 results and any confirmed inherited failure. Keep these results outside the worktree so normal landing cleanup cannot remove them. No new tracked validation script or prose-mirroring test is needed.

**Semantic review procedure and concrete cases.** These are ordinary instruction-review examples, not source mutations or PC execution. They deliberately include invalid example artifacts. First use this fixed premise for S-1..S-10: an illustrative plan has exactly two independently bypassable safety guards, G-1 and G-2; its otherwise-valid verification design records relevant test/fixture bodies read, every applicable boundary mapped to executable V/R cases, G-1->PC-1 and G-2->PC-2 with distinct compiling defects and exact detecting assertions, and `guards=2, mapped=2, missing=0, duplicate PC mappings=0`. Its estimated Cost states setup/build 2 min + V/R 3 min + PC-1 cycle 2 min + PC-2 cycle 2 min = total verification floor 9 min. This premise is a review fixture, not a claim of safety guards or executable PCs in S1. Each row changes only the stated dimension; evaluate variants separately and record the named sentence from the final bundle that forces the verdict. No provider/agent experiment or Markdown parser is involved.

| Case | Example or independently evaluated variants | Expected verdict and reason |
|---|---|---|
| S-1 | Leave the premise intact, with estimates labeled and the final self-check recorded. | Eligible for `next: code`. Approximate numerical estimates suffice; no new benchmark run is required. This valid control prevents turning the requirement into a mandatory measurement or a fixed-duration rule. |
| S-2 | Remove Cost entirely; leave only its heading; leave `<N>`; use `TBD`; give only `a few minutes`; give bare `9` without minutes. | Each is incomplete. Heading/placeholder/prose/unitless values do not supply a numeric floor in minutes. Completing the cases alone cannot permit handoff. |
| S-3 | Give `verification floor 9 min` with no components; or keep components 2+3+2+2 but claim total 4 min without explanation; or omit PC-2's cycle from the basis while still prescribing it. | Each is incomplete. The numerical basis must support the total and account for every prescribed cycle. A plausible-looking number is insufficient. |
| S-4 | Give total 0 min, no explanation; or total 0 with the explanation `documentation only` while still requiring the premise's test runs and two PC cycles. | Each is incomplete. An explanation must justify zero and cannot erase work the design itself requires. The counterexample does not mean this card has a zero ordinary-verification floor. |
| S-5 | Omit G-2 from the inventory while claiming `guards=1, mapped=1, missing=0, duplicate PC mappings=0`. | Incomplete. Reconciliation must start from every safety guard anywhere in the plan; internally consistent inventory counts alone miss the omitted guard. |
| S-6 | Inventory both G-1 and G-2 but map both to PC-1; or merge them into one umbrella guard/PC while they can be bypassed independently. | Each is incomplete. Split independently bypassable guards and provide distinct PC IDs/mutations, even if one detecting method can serve both. Passing V/R or two table rows cannot cure duplicate coverage. |
| S-7 | Map G-2 to undefined PC-2; or define PC-2 with only a name and no executable defect/test/assertion. | Each is incomplete. Every inventory reference must resolve to a fully executable control. |
| S-8 | Claim inspection from filenames/existence/signatures only; read a planned test body but not its relevant setup helper; inspect all files but silently omit one applicable boundary combination. | Each is incomplete. Actual body/setup reading and boundary accounting precede final case names. No unrelated repository-wide reading or exhaustive Cartesian product is implied. |
| S-9 | A relevant combination has explicit V/R coverage; alternatively a nonapplicable combination has a concrete exclusion reason. | Both may be complete for the boundary check. A bare `out of scope` for a safety guard still fails S-5/S-7; a justified nonapplicable combination is different from an unmapped applicable safety guard. |
| S-10 | All substantive rows are present but no result of the required pre-handoff self-check is recorded. | Incomplete. The instructions must require the check **and its recorded outcome**, not just show a template. Ordinary omissions are completed in TestDesign; an unverifiable fix design returns to Plan naming its gap, and human defaults retain Decide. |
| S-11 | Independently use a prose-only example with no applicable safety guard or existing/new test file: state why those are absent, that no new fixture/setup is needed, provide an executable document review and estimated ordinary floor 2 min + PC floor 0 min = total 2 min. Variant: a proposed new test file exists but its nearest reusable fixture was only located. | First can be eligible: empty inventories are explained, all applicable work is costed, and no unrelated guard/test is invented. The new-test variant is incomplete until the nearest fixture is read, or absence of one and the proposed setup are recorded. An empty applicable guard inventory is not a license to omit Cost or Inspection. |
| S-12 | Under landed CARD-0470, use ordinary floor 5 min (2 setup+3 V/R) + PC floor 4 min (2+2) = total 9 min and retain Mutation ownership. Variant: delete the total; omit one cycle; change PC ownership back to Build; or claim a shared-setup/concurrency saving without numerical explanation. | Complete example is eligible; each variant fails the relevant numerical-accounting or ownership review. Before the split lands, the same 9-minute total/basis is valid with the existing execution owner; S1 does not require inventing a new role. A justified saving must say which component is shared and what arithmetic changes. |

If the edited bundle cannot unambiguously justify a required verdict (for example it requires a number but allows unsupported zero), revise its compact wording within S1's existing D-1..D-5 scope and the 2,500-character cap. Do not add a server validator or alter unrelated tests to make the review pass. Re-review changed cases and rerun the focused class after the final bundle edit; report what actually passed, not merely the expected verdicts printed here.

### Guards the regression

| ID | Future regression | Detecting check and decisive assertion |
|---|---|---|
| R-1 | Growth/non-ASCII/provider-specific incident text or lost old invariants prevents the stage from shipping correctly. | V-1's existing `each_stage_bundle_is_ascii_and_under_the_size_cap` and `stage_bundle_invariants_are_pinned_by_substring`: <=2,500, ASCII and existing forbidden-string/invariant assertions. Do not raise the cap or delete the old safety-PC/append-only pins. |
| R-2 | Packaging/role ordering/argument limits drift while editing prompt text. | Existing `the_catalog_holds_exactly_the_bundles_that_ship`, `every_bundle_has_text_and_an_eight_hex_digit_content_version`, `a_stage_worker_carries_its_stage_bundle_then_the_basics`, `a_key_reachable_twice_is_composed_once`, `the_worst_case_composition_measured_sits_far_under_the_budget`, `a_realistic_code_worker_composition_stays_under_the_command_line_budget`, `an_oversized_composition_throws_and_names_what_to_shrink`, and `the_other_arguments_count_towards_the_budget_not_just_the_append`, all in V-1's one run. They cover the actual embedded catalog/composer and existing refusal boundary. No production guard is modified by S1. |
| R-3 | A pre-existing failure is hidden by broadening scope or changing unrelated standing wording. | Existing `delegate_basics_carries_the_standing_rules_and_none_of_the_days_state` currently expects `COMMIT AND PUSH EACH SLICE`, while the source says `COMMIT AND PUSH EACH MEANINGFUL SLICE OR FIX`. This remains an unexecuted observation at TestDesign. If V-1 fails there, rerun **that exact method** at Code's recorded base in a disposable detached checkout with its own output, using filter `/*/*/InstructionBundleTests/delegate_basics_carries_the_standing_rules_and_none_of_the_days_state`. Record both failures/assertions/counts and SHAs. Confirm only the actual failing method, not the whole suite. Any other failure needs the same exact-method comparison before being called inherited. If base passes, the failure belongs to this change. Preserve S1's two-file scope and report a confirmed inherited red explicitly; never label the full class green or weaken that assertion here. |
| R-4 | New headings exist but obligations become optional, guard counts cease to cover the full plan, inspection is reduced to discovery, or CARD-0470 semantics are overwritten. | S-2..S-10's invalid examples must still be incomplete and S-1/S-9/S-11's valid cases remain eligible; S-12 plus V-3/V-4 verify subsection/ownership/cost parity. These regression checks are bounded semantic review, not additional substring tests or evidence of server-side rejection. |

### Guard inventory

**Applicable safety-critical implementation guards: none.** The complete plan was reconciled, including Ground truth, D-1..D-5, proposed text, S1, all acceptance examples and delivery constraints. Its implementation changes only TestDesign instructions and the owner's subsection list. The cost/inspection/completeness conditions are review obligations, not executable safety rejection/ownership guards; D-1 explicitly rejects a server-side validator and D-5 keeps runtime/test infrastructure unchanged. Historical guards mentioned in the supplied incident and illustrative G/PC labels in the semantic-review fixture are not guards implemented by this task.

| Plan reference / invariant reviewed | Applicable coverage and PC decision |
|---|---|
| D-1: complete, recorded handoff self-check and existing next-stage meanings | V-2/S-10 and V-5; instruction acceptance check, no source-mutatable runtime guard. |
| D-2: numeric minutes, supported components, explained zero and preserved total/split | V-2/S-1..S-4/S-12 and V-4; numerical semantic review, no runtime gate. |
| D-3: all-plan inventory, distinct mappings, executable references and explained empty case | V-2/S-5..S-7/S-11; hypothetical future guard accounting, no S1 product safety predicates. |
| D-4: test/fixture bodies and relevant boundaries before final case names | Inspection above and V-2/S-8/S-9/S-11; review obligation, no fixture implementation changes. |
| D-5 / Ground truth: 2,500-character, ASCII, composition and version constraints; narrow scope and launch delivery | V-1/V-3/V-5, R-1/R-2/R-3; existing packaging safeguards are exercised unchanged by the existing suite. This card does not implement/change their predicate logic and does not mutate unrelated composer/runtime code. |

Reconciliation result: **guards=0, mapped=0, missing=0, duplicate PC mappings=0; referenced implementation PCs=0, undefined implementation PCs=0.** The table accounts for all decisions instead of treating the empty inventory as a shortcut. If implementation expands into executable safety behavior, this zero is invalid: return to the appropriate design stage and add distinct guards/controls rather than silently stretching S1.

### Positive controls

**PC count: 0; deliberate source-mutation cycles: 0.** No applicable implementation safety guard is changed, as explained above. D-5 explicitly uses the existing bundle suite and bounded semantic examples, without new prose-mirroring tests or unrelated product mutations. S-2..S-10 and S-12's invalid examples are ordinary semantic counterexamples; they are not claimed as assertion-level PC reds. Do not manufacture a PC by deleting expected words, exceeding the size cap or disabling an unchanged composer guard merely to populate this heading.

The PC execution floor is therefore 0 minutes for a concrete reason: no red/restore/green cycle is prescribed. This does not zero the build, suite or review work. If CARD-0470's default is active when Code completes, follow its normal handoff even for a zero-PC plan; the designated PC worker verifies applicability/missing-control scope under that contract. This plan does not bypass that stage, reassign its work to Code, or authorize unrelated mutations. Additional required applicable controls change the inventory and estimate before execution.

### Out of scope

- New tests mirroring prompt prose, new test fixtures, a Markdown/report parser, API validation, scheduling/cost enforcement, stage-role changes, or edits outside S1. No claim that settlement now rejects an incomplete design.
- Reopening CARD-0466, implementing/redoing CARD-0470, reading unrelated integration fixtures, actual provider launches, live agent-compliance experiments, client/browser/Pty/full-assembly runs, database or broker probes.
- Running Code's suite or semantic review on a not-yet-implemented bundle during TestDesign. This dispatch only appends the design, checks artifact consistency and commits/pushes it. Code supplies actual V/R execution results later.
- Shared-server restart or an extra live delegate solely as a test. Code reports the required server restart; rollout and observation of the next normal fresh launch belong to the caller under the bundle owner/runbook. A build/test result is not evidence that an existing live session has refreshed.

### Cost

**Suites forced:** only `tests/Antiphon.Tests` filtered to `/*/*/InstructionBundleTests/*`, once after the final S1 edit, plus the 12-row semantic review and source/document reviews above. All parameter variants in S-2..S-12 are included in that review allowance. No client, integration, E2E or Pty suite is forced. A confirmed failure needs the precise conditional base replay in R-3, not another entire assembly.

| Component | Estimated minutes | Basis / owner |
|---|---:|---|
| Setup and isolated-output restore/build | 3 | Code records checkpoint SHA, output ownership and fresh evidence directory; `dotnet run` builds the real embedded bundle. |
| Focused class execution and TRX inspection | 2 | One `InstructionBundleTests` run covers V-1/R-1/R-2 and detects R-3; require actual nonzero results and intended parameter cases. |
| Bounded semantic review | 3 | Read final instructions once and record every S-1..S-12 variant's verdict and enforcing sentence; covers V-2 and R-4. |
| Ownership/subsection/scope review and result recording | 2 | V-3/V-4/V-5 plus final report; no additional suite or live service. |
| All prescribed PC red/restore/green cycles | 0 | Zero applicable implementation guards and zero prescribed mutation cycles, explicitly reconciled above. |

**Ordinary V/R verification floor ~ 10 min; PC execution floor ~ 0 min; total verification floor ~ 10 min** (=3+2+3+2+0). All timings are estimates, not measurements from TestDesign. Setup/build is charged once for the shared class run; no speculative parallel savings. This is verification time, not Code's complete writing/debugging estimate. Add authoring time to the 10-minute ordinary floor for Code's ExpectAbout. If R-3 is observed, reserve an additional estimated 3 minutes for its isolated base replay and report actual extra time; build failures or a slow environment require honest revised estimates, not skipped checks. Caller rollout and any zero-PC Mutation applicability/analysis dispatch are separate from this execution floor, not implicitly free or included in Code's PC tail.

### Handoff completeness check

- Inspection: the only planned existing test file was read in full, with its local helper and relevant catalog/composer bodies; no new test file/fixture is prescribed. Applicable boundaries map to V/R and all exclusions are explicit.
- Guard reconciliation: guards=0, mapped=0, missing=0, duplicate PC mappings=0; no undefined implementation PC references, with the full-plan rationale recorded.
- Executability: 5 V rows, 4 R rows, 12 semantic case groups with explicit variant verdicts, one exact existing suite command and a conditional exact-method base replay. No new test name, runtime validator or live experiment has to be invented by Code.
- Numeric cost: ordinary 10 min + PC 0 min = total 10 min, with labeled component estimates; prescribed work is not hidden by the zero-PC explanation.
- Design status: complete and eligible for `next: code`. These checks validate the verification design, not an unimplemented S1 or unrun product tests. Code's report must give actual per-V/R outcomes, fresh test counts/failures and review-evidence paths, confirmed inherited failures if any, preserved CARD-0470 ownership, and `restart: server`.
