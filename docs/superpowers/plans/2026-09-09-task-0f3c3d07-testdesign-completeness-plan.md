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
