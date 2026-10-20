# CARD-1366: restore the scoped Mutation tier rule

Plan base and fetched `origin/master`: `e8bd995b0481e838c0110217dc4f680fe4de8374`,
observed 2026-10-20. Target: **master only**. The operator approved restoring the
wording. This dispatch changes only this plan; implementation remains a Code task.

## Ground truth

| Card assumption | Observed source or live behavior | Consequence |
|---|---|---|
| The orchestrator lost the CARD-0479 tier instruction. | `server/Bundles/orchestrator.md`, in the `Default workflow:` paragraph, has `binding-only mechanical-execution; one open Mutation per O.`. `server/Bundles/README.md`, the Tiers paragraph in `docs/orchestration-loop.md`, and `.claude/skills/antiphon-delegate/SKILL.md` retain the full sentence quoted below. | Restore this specific Mutation rule, not a general preference for a provider or for Low. The missing text is `Commission `, ` at explicit Frontier, then eligible `, and ` at Low`: 54 UTF-16 units. |
| CARD-1332 removed the text. | The card's correction is confirmed by `git show 68be4213412614fd6fa3a62429ce3513308a4707`: CARD-0479 deliberately shortened it to recover argv headroom on 2026-10-10. The comment at `MechanicalPcContractTests.BundleRoutingRule` and the CARD-1355 plan still misattribute it to CARD-1332. | Correct those two attributions without rewriting historical test outcomes. |
| Bundle sections are generated into `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS`. | These are separate channels. `OrchestratorInstructionsSnapshotBuilder` reads live settings; `OrchestratorInstructionsRenderer.Compose` renders sections including the matrix and pins; `OrchestratorInstructionsService` writes the versioned file through `AntiphonDataPaths` and persists its state. The path/URL environment variables and `GET /api/orchestrator-instructions` expose that generated content. | Do not edit the generated file, renderer, settings, or database. Its source and precedence are documented in `docs/agent-instruction-file-contract.md`. |
| The README is launch text. | `server/Antiphon.Server.csproj` embeds `Bundles/*.md` except `README.md`. `InstructionBundles` normalizes and hashes embedded text; `InstructionBundleComposer` composes it under `[bundle:orchestrator v<hash8>]`. `ForDelegate` attaches it to Orchestrator tasks; standing agents can carry it by attachment. | Edit `server/Bundles/orchestrator.md`; keep the README's mirrored paragraph aligned. Rebuild the server resource through the test project. Version changes automatically, at a subsequent composing launch. |
| Restoring the rule might override current routing. | Live GETs on 2026-10-20 (generated instructions `v42bddad9`, revision 390, default Medium) confirm the matrix's ordinary order: Codex/Frontier, Grok/Frontier, ClaudeCode/Frontier (`fable`); Review is Grok, Codex, ClaudeCode. Direct effective Mutation and Review reads confirm all three complexities; the Mutation stage-pin read is empty. `AgentTaskService.CreateAsync` rejects explicit complexity plus kind/level. Its default-row path excludes explicit kind/level, with a warning. `RoutingCandidates.Compose` filters explicit requests and applies Required/Preferred pins. | Ordinary work keeps the matrix. CARD-0479's explicit tier requests are scoped exceptions already supported by create, subject to pins; they do not establish a provider fallback order. A conflicting Required pin requires resolution, never a silent downgrade or `-IgnoreRoutingPin`. |
| Low is safe for any Mutation work. | `docs/mechanical-pc-contract.md` permits Low only for an already bound `mechanically-eligible` pack. Binding, discovery, and higher-tier-required rows stay off Low. | Preserve eligibility and one-open-Mutation-per-O in the restored instruction. |
| There is insufficient body headroom. | In-memory LF/Trim measurement of this base is 10,822 UTF-16 units. The exact replacement below produces 11,077, leaving 423 under the fixed 11,500 body allocation. | Restoration fits the body allocation. The existing full-composition argv test must still establish aggregate headroom after rebuilding; this measurement is not a test pass. |

Platform preflight read both `GET /api/runner-defaults` (revision 2) and
`GET /api/session-runners`: eligible Linux and Windows runners are available.
All planned checks are portable Unit content/composition checks, including the
existing synthetic CRLF case. Use runtime placement; omit `-Runner` and
`-Platform`. No host address or fleet location is part of this plan.

## Decisions

- **D-1 — Restore the original sentence, qualified by current policy.** Replace
  the shortened fragment at the end of the bundle's `Default workflow:`
  paragraph with the exact three sentences below. In the README's matching
  paragraph, retain its original first sentence and append the same two
  qualifying sentences. Keep all three in that paragraph, with no blank line
  between them. This recovers the operator-approved instruction while making the
  matrix/pin boundary and Low eligibility explicit.

  ```text
  Commission binding-only at explicit Frontier, then eligible mechanical-execution at Low; one open Mutation per O.
  Ordinary dispatch follows the live Role × complexity matrix; these scoped tier requests still respect routing pins. Low requires a bound mechanically-eligible pack; see docs/mechanical-pc-contract.md.
  ```

  Rejected: shortening the README to match the loss; relying solely on an owner
  pointer; repeating today's provider order in standing text; changing routing
  code or pins to make Low the ordinary Mutation default. Each either drops the
  approved instruction or changes the matrix policy.
- **D-2 — Extend the existing content witness.** Re-anchor
  `MechanicalPcContractTests.C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow`
  instead of adding a parallel test. Keep its five argument cases and its
  paragraph/owner-pointer checks. The bundle case will check the complete rule
  and the two qualifications in disk text and composed Orchestrator text, and
  compare the normalized workflow paragraph with the README. Literal expected
  sentences must live in test code, not be derived from the source under test.
  Reject free-word searches: unrelated `eligible` in placement previously hid
  this loss.
- **D-3 — Preserve fixed transport limits.** Update only the approved raw hash in
  `CheckpointRepeatDocumentationTests` from final LF bytes and its explanatory
  comment. Leave 11,500 body, 28,000 early composition, 29,500 reserve boundary,
  and 30,000 transport limits fixed. The composition fixture measures the added
  text automatically; no fixture-size expectation or ceiling needs inflation.
- **D-4 — Bound scope and stages.** No runtime routing, generated-settings,
  session lifecycle, deployment, or RC changes. This instruction change still
  needs ordinary Review under D-2 advisory policy. The brief requests the red/
  green design but does not explicitly fold TestDesign into Plan, so the next
  stage is **test-design**, to confirm this bounded verification section before
  Code. No unresolved product decision or provisional default is required.

## Implementation slices

### S1 — Commit the failing content expectation

Change `tests/Antiphon.Tests/Application/MechanicalPcContractTests.cs`:

1. Set `BundleRoutingRule` to the full `RoutingRule`, preserving its literal
   expectation and existing owner-pointer assertion.
2. In the bundle argument case, use the existing `Paragraph` helper to require
   the two additional literal sentences from D-1 in the same workflow paragraph.
   Repeat the assertions on
   `InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Orchestrator, AgentTaskRole.Custom)).Text`.
   Require the source, composed, and README workflow paragraphs to agree after
   the helper's whitespace normalization. Keep the existing other four cases.
3. Correct the constant's comment: CARD-0479 commit `68be4213412614fd6fa3a62429ce3513308a4707`
   shortened the sentence; CARD-1366 restores the explicit rule and qualification.

Commit and push S1 before CP-1. Against the unchanged bundle, the first failure
must be the existing `c1355-bundle-routing-rule` assertion on the orchestrator
argument; the other four argument cases pass. This is the single requested
pre-fix witness, not a post-land mutation campaign.

### S2 — Restore the bundle and reconcile its guards

Change only:

- `server/Bundles/orchestrator.md` and `server/Bundles/README.md`: D-1 text.
- `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs`:
  recompute `orchestrator-approved-bytes` from final LF bytes, explain CARD-1366's
  deliberate restoration, and retain the existing assertions and size limits.
- `docs/superpowers/plans/2026-10-19-card-1355-c479-v07-anchor-plan.md`:
  correct the table row and causality paragraph that blame CARD-1332. Name the
  actual CARD-0479 commit and intentional headroom trade-off, preserving that
  plan's original scope, decisions, witness results, and costs.

Commit and push S2 before CP-2. Run no whole-Unit, namespace, full assembly,
native, client, or live-agent suite for this text-only change. Code reports both
slice SHAs and actual counts, checks the complete task base..HEAD with
`scripts/check-evidence-diff.ps1`, and hands off ordinary Review. Master-only
publication does not claim activation on RC product hosts.

## Verification design

The oracle is the actual instruction paragraph in disk and embedded/composed
content. These tests establish shipped text and transport headroom, not model
obedience or new routing behavior.

| ID | Coverage and named test | Expected expanded results |
|---|---|---|
| V-1 | `MechanicalPcContractTests.C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow`: full rule, matrix/pin qualification, bound Low eligibility, README/composed parity, retained owner links and existing recipe checks | 5 |
| R-1 | `OrchestratorBundleOwnershipTests.C1332_OrchestratorBodyFitsItsAllocation`: LF and synthetic CRLF normalization, fixed body cap | 2 |
| R-2 | `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget`: actual role/kind compositions, additive argv ledger and fixed thresholds | 1; internal matrix rows are not extra tests |
| R-3 | `CheckpointRepeatDocumentationTests.repeat_budget_reaches_code_briefs_without_bundle_growth`: final raw bundle pin plus retained repeat-policy guards | 1 |

**Red witness:** CP-1 on S1 must build successfully, execute five results, and
report exactly four passed / one failed / zero skipped, with the orchestrator
argument failing `c1355-bundle-routing-rule`. Zero results, compilation/fixture
failure, or another failing case does not qualify. Preserve its real nonzero
exit and receipt; do not describe S1 as green. No mutation or reset is needed.

**Narrow green:** CP-2 on S2 executes exactly nine results across the four named
methods, all passing and none skipped. Its class-and-method OR selector uses
CARD-0403 syntax; verify the actual TRX roster, including the five arguments and
two body cases. An unexpected expanded roster is incomplete evidence.

Run through `tools/Antiphon.Checkpoints` with this plan and `--rows CP-1` after
S1, then a separate run with `--rows CP-2` after S2. Await each terminal result;
exit 75 requires another `wait`. Bootstrap any needed checkpoint-tool build via
`scripts/build-slot.ps1` with an isolated forward-slash `bin-.../` output and
use its no-build invocation; row builds take the checkpoint gate themselves.
Do not acquire an outer slot around row execution. Record any tool-bootstrap
build separately as infrastructure, not an extra test checkpoint.

Preserve unedited CHECKPOINT lines, tested SHAs, expanded counts, failure names,
slot waits and source/build provenance in the Code report. Keep generated
receipts ignored. Finish owned commands before editing, remove task-created
alternate build outputs after execution, and leave no running executor.

### Cost

Two isolated builds and two narrow filters: approximately 16 minutes plus host
slot wait and authoring. No repeated green run is planned. A non-assertion red
requires diagnosis, not a broader suite or loosened limits.

### Checkpoints

`portable-unit-*` names the lane; runtime defaults choose the eligible host.
The expected-red CP-1 is intentionally non-green and is not a landing gate;
CP-2 is the final green acceptance selection.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1366-red/` | portable-unit-red | `/*/*/MechanicalPcContractTests*/C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow*` | V-1 red witness | exactly 5 executed, 4 passed, 1 failed at c1355-bundle-routing-rule on the orchestrator argument, 0 skipped | 5 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1366-green/` | portable-unit-green | `/*/*/(MechanicalPcContractTests*)\|(OrchestratorBundleOwnershipTests*)\|(InstructionBundleTests*)\|(CheckpointRepeatDocumentationTests*)/(C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow*)\|(C1332_OrchestratorBodyFitsItsAllocation*)\|(the_worst_case_composition_measured_sits_far_under_the_budget*)\|(repeat_budget_reaches_code_briefs_without_bundle_growth*)` | V-1, R-1, R-2, R-3 | exactly 9 executed, 9 passed, 0 failed/skipped; all four named methods | 9 | 8 |
