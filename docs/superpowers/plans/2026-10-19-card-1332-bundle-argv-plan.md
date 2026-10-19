# CARD-1332 — give the orchestrator bundle durable argv headroom

Master-only; the RC is closed. This plan changes delivered instructions, their owner documentation, and their tests. It does not change transport, launch admission, runtime settings, or session lifecycle. TestDesign remains a separate next stage; the verification proposal below is its starting point, not authority to skip that stage.

## Outcome

Replace duplicated procedures in `server/Bundles/orchestrator.md` with short mandatory rules and precise owner links. Cap its LF-normalized, trimmed body at **11,500 UTF-16 characters**, and fail the existing shipped-composition test above **28,000 estimated argv characters**. Retain both the **30,000** transport/default budget and the existing **500-character reserve** check at **29,500**. Print measurements and headroom even on passing runs.

An in-memory candidate using the five replacements below reduces the current orchestrator body from 13,453 to **10,787** and the existing worst-case fixture from 29,493 to **26,827**: 1,173 below the new early threshold, 2,673 below the reserve boundary, and 3,173 below the transport ceiling. These are source-derived estimates, not a TUnit run or proof of every possible operator append.

## Evidence and ground truth

Inspected task base: `f99f8bffb5887d43fafae61ae979ae9e11a36f80`. CARD-1332 has zero revisions. CARD-1330's full description and six revisions were read, together with its investigation, plan and stored Review report.

Fetched prerequisite branches:

- `feat/card-task-ed28451e`, investigation tip `d204ca43d99bf2daeb59f0a7c65a98e61cae2045`: [investigation](../../investigations/2026-10-17-card-1330-investigation.md).
- `feat/card-task-537fced1` and `feat/card-task-b2d71f07`, both at `1eab4a4d2d206cc92555b69241d56b65169b36a0`: [repair plan](2026-10-18-card-1330-orchestrator-argv-reserve-plan.md). Review reported seven executed tests passing across five narrow rows, and explicitly described the seven-character margin as reconstructed rather than printed.

At the task base, `server/Bundles/`, `InstructionBundleComposer.cs` and `ChannelPreamble.cs` have no diff from that reviewed tip. PowerShell source measurement reconfirmed body length 13,453 and raw SHA-256 `7b34e63a29128753e2da3667af497d19c687aebe7a13943fe130a83394f6fd41`. Rendered contributions are orchestrator 13,485; expanded delegate-basics 7,524; board-api 5,347; style-explanatory 619; separators 8; the investigation's unchanged CRLF Telegram preset 2,386. Composition is 29,369, plus 124 for the fixture's arguments/append overhead: 29,493. No builds or tests were run during Plan.

| Card assumption | Code / repository truth | Consequence |
|---|---|---|
| The orchestrator is seven characters below the reserve. | Confirmed for the existing shipped-role fixture, including CRLF preset and fragment expansion. | The premise holds; no further investigation is required before TestDesign. |
| The 500 reserve is a production setting. | It is subtracted in `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` and `InstructionFragmentTests.C1213_ExpandedBudgetIsEnforced`. Production composer admission uses the configured budget; runners retain their separate ceiling. | Add an earlier test threshold without changing admission or either existing limit. |
| Raw file bytes are the argv size. | `InstructionBundles` LF-normalizes and trims embedded text, adds hash headers; `Compose` expands fragments, inserts separators and preserves the append verbatim. The guard counts UTF-16 units, not UTF-8 bytes. | Measure normalized body for the local cap and actual composed text for the aggregate guard. Keep CRLF coverage. |
| A bundle-size guard is missing. | `orchestrator_bundle_points_to_operational_autonomy_without_growing` already permits 14,310 LF source characters; stage bundles have separate caps. | Tighten the orchestrator-specific budget instead of introducing budgets/configuration for the entire catalog. |
| Owner procedures can simply be removed from the bundle. | `RepairSourceDocumentationTests`, `StandingPipelinePolicyDocumentationTests`, `OrchestratorInstructionsGuidanceTests`, `TaskPlatformGuidanceTests` and `RunnerDefaultGuidanceTests` pin duplicated procedure text or fleet examples in the bundle. | Preserve full owner assertions; replace only bundle-copy assertions with mandatory rules plus resolvable owner links. |
| All versions need hand-maintained pins. | `CheckpointRepeatDocumentationTests` pins raw orchestrator SHA; embedded versions derive from LF-trimmed content. `C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders` checks source against the built resource. | Recompute the raw pin with the edit; rebuild and retain the independent embedded-resource witness. |
| One default composition represents arbitrary launches. | The existing test enumerates non-specialist roles and both task kinds, with board-api/style-explanatory and Telegram. Custom appends, arbitrary attachments and live role-policy additions are outside this fixture. | Keep its existing scope explicit; retain overflow refusal tests and the runner's final check. |

## Decisions

- **D-1 — Keep the runtime limits and composer unchanged.** This is an instruction-footprint problem. Reject raising `CommandLineBudgetChars`, spending the 500 reserve, truncating instructions, removing standard attachments from the fixture, or changing transport as part of this card.
- **D-2 — Enforce 28,000 in the shipped-composition test.** This is a failing early regression guard, not a log-only warning: it leaves 1,500 characters before the existing reserve boundary. Keep the explicit 29,500 check as a separate assertion. The simulated candidate fits with 1,173 additional characters available before the new guard. Reject a threshold derived from today's measured value: growth must consume a fixed allowance rather than automatically move the goalpost.
- **D-3 — Allocate 11,500 to the normalized orchestrator body only.** The candidate leaves 713 characters within that allocation. With the other current contributions unchanged, even the full allocation estimates 27,540, below the new aggregate threshold. This local guard explains where growth occurred; the aggregate guard still catches growth in other bundles, fragments and the preset. Reject arbitrary caps for every catalog member and a new configurable budget registry.
- **D-4 — Move procedures, preserve operative rules.** The owner retains the complete recipe, exclusions, evidence identity and authority boundaries. The bundle keeps the action trigger, mandatory constraint and owner link. Explicitly preserve the existing delegation/read carve-out, launch/authentication incident rules, stage-report parsing, blocked-seat/answer verbs, keep-six line, post-land retrospective paragraph, live-settings precedence, autonomy pointer, channel delivery contract and SourceLanding recipe.
- **D-5 — Preserve coverage when moving pins.** Old owner-content assertions remain. Only assertions that require detail in the bundle move to the owner or become rule/link assertions. Do not bless the whole result solely by updating the SHA. Reject deleting documentation tests to obtain green or replacing semantic checks with a length-only assertion.
- **D-6 — Two landable slices, ordinary Review required.** Owner preparation lands independently; the bundle edit, pin migrations and budget guard land together in the second slice. Red test commits are intermediate evidence, not landable slices. Instructions and test selection are excluded from D-2's narrative-only advisory shortcut in `docs/orchestration-loop.md`.

These are implementation decisions within the card's requested scope. No unresolved product or operator decision blocks the plan.

## Relocation map and candidate wording

Add explicit, stable anchors to existing sections of `docs/orchestration-loop.md`, keeping the current headings so other links and section-based tests remain valid. Preserve the existing `repairsource-failed-owner-recovery` anchor. Add only missing detail to its current owner; do not copy a second complete procedure elsewhere.

| Bundle range | Owner | Detail retained at the owner |
|---|---|---|
| `To start a child at another base` through the end of failed-owner recovery | Add `delegate-start-ref` before **Continuing a sibling's work (CARD-0613)**; existing `repairsource-failed-owner-recovery`; existing **Landing a -StartRef repair** section | Worktree requirement; excluded flags; recorded branch; base versus landing authority; version-support check; owner versus source Review identity; exact recovery/adoption commands; publication/status checks and historical Failed status. |
| Opening board-pipeline paragraph through the line before `A keep-six reminder`; separately the `A 409 concurrency_limit` paragraph | Add `standing-pipeline-policy` before **Standing pipeline policy** | Policy defaults and host examples, occupancy fields, effective lower caps, Code-depth counting/pulling, overlap deferral, operator-only host writes and all override predicates. Keep the intervening keep-six and retrospective paragraphs verbatim. |
| `Verification rounds (CARD-0544)` paragraph | Add `verification-rounds` before **Interim and Final verification rounds** | First Code/Review Final, explicit admission, role opt-in, subject/baseline/selection flags, no silent fallback, Interim-to-Final routing, latched-owner land requirement and current disabled status. Add the CLI flag recipe here if it is not already present in this section. |
| Final `Platform: the pipeline` paragraph | Add `runner-placement` before **Runner defaults are not model pins** | Runner defaults, follow-up/card inheritance, explicit Any unpinning, legitimate OS requirements, scoped OS-only work, forbidden habit-based pinning, and user-authorized revision/reason/Human-provenance settings writes; a pipeline tick does not write defaults. |

The following replacement text is a measured starting point. Rewording is allowed only while retaining the listed semantics and meeting both fixed budgets. The rest of the bundle stays byte-for-byte unchanged, including its blocked-seat paragraph: parking is off by default and there is no automatic input-wait release deadline. No input-wait behavior is added or changed.

Child base / repair replacement:

```text
To change a child's base, use -Worktree -StartRef <full-sha>; never git checkout -B.
Use its reported WorktreeBranch. A base grants no land authority.
Read docs/orchestration-loop.md#delegate-start-ref for exclusions and -FromTask adoption.
For a failed -RepairSource owner, require owner-bound Clean Final/Full Review and
-RecoverReviewedSource; follow docs/orchestration-loop.md#repairsource-failed-owner-recovery.
A -RepairSource task cannot be used with -FromTask.
```

Opening pipeline replacement (retain the current keep-six/overlap/retrospective text after it):

```text
When you are working a board through its pipeline, read the effective-settings route (CARD-0881):
GET /api/agent-tasks/pipeline?projectId=<id> (or ?unscoped=true). Use its limits and
docs/orchestration-loop.md#standing-pipeline-policy; never exceed the lower effective cap.
Host budget writes need an operator request. Run stages in parallel, each in its own -Worktree.
Land confirmed work promptly. Keep Code at its depth cap, counting in-flight, queued and ready.
Below cap, pull the lowest-rank unstarted Backlog card; at cap, start no new Plan toward Code.
```

Override replacement (retain `A 409` after the retrospective paragraph, whose test uses that boundary):

```text
A 409 `concurrency_limit` permits -IgnoreConcurrencyLimit only for population=open,
canOverride=true, axis=absolute and no same-stage occupant; otherwise defer.
Other projects' work never counts against yours.
```

Verification replacement:

```text
Verification: first Code/Review is Final/Full. Interim requires explicit admission; never
silently change rounds. A clean Interim goes to Final Review, never land.
Follow docs/orchestration-loop.md#verification-rounds for prerequisites and flags.
```

Platform replacement:

```text
Platform: read GET /api/runner-defaults and GET /api/session-runners. Do not embed a fleet location.
Normally omit -Runner and -Platform; runtime defaults and inherited platform apply.
Use -Platform Any to unpin. Pin only an OS-required piece with its own filter and budget;
leave the rest unpinned. Never write defaults on a pipeline tick.
Follow docs/orchestration-loop.md#runner-placement for inheritance, pinning and authorized settings changes.
```

## Slices

### S1 — Make the owner procedures directly addressable

Files: `docs/orchestration-loop.md`; new `tests/Antiphon.Tests/Application/OrchestratorBundleOwnershipTests.cs`.

Add the four anchors and retain/add the relocation-map details in the existing owner sections. Add a Unit-category `C1332_OwnersContainRelocatedProcedures` method with four argument cases: child-base/recovery, pipeline, verification, placement. Its assertions are independent literals scoped to each actual owner section, including the existing recovery anchor and recipe, rather than reading expectations from this plan. Check each anchor exists exactly once and its section contains the required rules. Keep existing headings intact.

Commit/push the test first and record assertion red on the missing anchors. Commit/push the owner update, then CP-1. This slice is independently landable and does not touch delivered bundle bytes or existing pins.

### S2 — Shrink the bundle and guard future growth

Files:

- `server/Bundles/orchestrator.md` and `server/Bundles/README.md`.
- `tests/Antiphon.Tests/Application/OrchestratorBundleOwnershipTests.cs` and `InstructionBundleTests.cs`.
- `tests/Antiphon.Tests/Application/RepairSourceDocumentationTests.cs`, `StandingPipelinePolicyDocumentationTests.cs`, `OrchestratorInstructionsGuidanceTests.cs`, and `TaskPlatformGuidanceTests.cs` (also contains `RunnerDefaultGuidanceTests`).
- `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs`.

Apply the five replacements. Migrate procedure-copy pins deliberately: `RepairSourceDocumentationTests.AssertRecipe` continues to check the complete owner recipe; its bundle assertions check owner-bound Review, recovery/adoption distinction and the two links. In the two pipeline guidance classes, preserve the original phrase arrays for AGENTS, owner and skill copies; give the bundle its own compact-rule assertions. Do not remove the bundle case from coverage. Platform tests retain full inheritance and OS-scope assertions on the owner and check the compact dispatch rules/link on the bundle. No edits to AGENTS, skills, routing services or settings are required.

Add four argument cases to `C1332_BundleKeepsRulesAndOwnerPointers`, reading the **embedded** orchestrator contract and checking the rules above and their links against real owner anchors. Add `C1332_OrchestratorBodyFitsItsAllocation` with LF and CRLF source cases; normalize with the catalog's LF + Trim policy, require at most 11,500 UTF-16 units, and report actual/limit/headroom. Remove the obsolete 14,310 assertion from the autonomy-pointer test; retain that test's pointer and line-ending assertions. There is one authoritative orchestrator allocation, not conflicting caps.

Extend `the_worst_case_composition_measured_sits_far_under_the_budget` in place. Preserve the current role/kind enumeration, attachments, CRLF preset and real composer. Use a fixed GUID fixture (same D-format width) for reproducible diagnostics. Compute the estimate in a private test helper matching the guard's documented arithmetic: sum of each argument's UTF-16 length plus three; add zero for an empty append, otherwise composed length plus 28. Cross-check it against the **real** `EnsureWithinCommandLineBudget`: exact estimate is accepted and estimate-minus-one is refused. This ensures a diagnostic calculation cannot silently diverge from the production guard; no new production API is needed.

Collect and print the matrix before enforcing limits. Each line names kind/role, measured composed/argv characters, 30,000 transport limit, 29,500 reserve boundary, 28,000 early threshold and all three signed headrooms. Include bundle/fragment names and sizes without double-counting fragment expansion. Print a maximum row identifying the worst kind/role. Then assert the existing raw-composition bound, the existing 500-reserve admission check, and estimated argv <= 28,000. The test name stays stable. Do not lower or duplicate the reserve in a second setting.

README documents the body-versus-composition measurements, fixed early threshold, owner-link editing rule, diagnostics and raw-pin/rebuild requirement. Recompute the raw SHA pin from the final LF file; do not hard-code the content-derived rendered version. Commit/push the new failing size/early/pointer tests before the bundle fix, preserving red evidence; commit/push the bundle/pin/assertion migration as a coherent fix before CP-2 through CP-12. This completed slice is independently landable after S1.

## Verification design — proposal for separate TestDesign

TestDesign must finalize exact literals, case rosters and method-scoped post-land controls, then confirm or amend this closed manifest before Code. Do not grow the scope to a Unit namespace/lane or full suite. Counts below are executed TUnit cases, not loop iterations.

| ID | Behavior / regression | Red-first witness and decisive green evidence |
|---|---|---|
| V-1 | Full moved procedures remain accessible at stable owner anchors. | S1 test-only commit fails named missing-anchor assertions. CP-1 executes four owner-section cases; each checks procedure constraints, not merely file existence. |
| V-2 | The delivered bundle carries mandatory rules and usable owner pointers. | On S1, the new S2 pointer cases fail missing links. CP-2 checks embedded rules plus target anchors; CP-5..CP-9 preserve owner detail and migrate only bundle-copy pins. |
| V-3 | Orchestrator body has a fixed 11,500 allocation on LF and CRLF checkouts. | S2 test-only commit sees 13,453 and fails the size assertion in both cases. CP-2 passes both normalized-source cases; CP-4 independently binds source to rebuilt embedded content. |
| V-4 | Aggregate growth fails before the existing reserve is consumed. | S2 test-only commit reports 29,493 > 28,000 while the old 29,500 check still accepts. CP-3 must pass the unchanged fixture under both thresholds. Its exact-estimate / one-less guard calls cross-check arithmetic. |
| V-5 | Measured size and headroom are visible on success and failure. | Before S2 the passing budget test emits no measurement. The S2 red run and CP-3 green console/TRX attachment must both contain measured argv and all named limits/headrooms; review these bounded output lines, not just exit code. No test of a test-only formatter is required. |
| R-1 | Delegation, autonomy, blocked seats, report/answer verbs and resource stamps survive. | CP-4 retains the existing contract tests. Post-land controls remove one protected rule or use stale embedded bytes and must fail the corresponding method. These unchanged behaviors are regression coverage, not claimed baseline defects. |
| R-2 | Keep-six, retrospective semantics, approved bytes and fragment-expanded admission survive. | CP-10..CP-12 retain their existing independent oracles. The old raw SHA must fail after the bundle change until intentionally updated; fragment exact-fit/overflow assertions remain intact. |

For Code's two red-first groups, use the same exact methods as their green rows, on committed/pushed test-only source. Record the source SHA, assertion identity and nonzero executed count. For the early guard, report expected failure separately from the old reserve check's success. A compile failure, missing fixture or zero executed tests is not red evidence. These commissioned red runs are additional to the final-green table; report their time separately. A green baseline regression gets a planned post-land control, not a fabricated pre-fix failure. Keep all deliberate semantic mutants pending for SourceLanding Mutation.

Placement was inspected on 2026-10-19 using **GET /api/runner-defaults** (revision 2, no per-kind exception) and **GET /api/session-runners** (eligible Linux and Windows lanes available). Every checkpoint below is the **portable Unit lane in Antiphon.Tests**, with no native OS or production-service dependency. The `portable-unit-*` group names identify that lane. Omit `-Runner` and `-Platform`; re-read live placement at commissioning. No fleet address or host is pinned by this plan.

Use the checkpoint tool, one run per committed slice group with the exact expected source SHA; wait until its exit is not 75 and never settle with a running process. The tool takes host build slots. Any bootstrap build takes `scripts/build-slot.ps1`; record it separately. Exit 4 is not run/blocked, never permission to run without a lease. Reuse outputs only within the same `After` group; retain source-qualified receipts, clean up owned alternate outputs, and keep generated evidence ignored. Code/Review run `scripts/check-evidence-diff.ps1` over task base..pushed HEAD. Preserve the checkpoint lines and fresh TRX roster in the stored report.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1332-owner/` | portable-unit-owners | `/*/*/OrchestratorBundleOwnershipTests/C1332_OwnersContainRelocatedProcedures` | V-1 | all 4 cases, 0 failed/skipped | 4 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1332-bundle/` | portable-unit-allocation | `/*/*/OrchestratorBundleOwnershipTests/*` | V-1, V-2, V-3 | 4 owner + 4 pointer + 2 line-ending cases, 0 failed/skipped | 10 | 8 |
| CP-3 | S2 | CP-2 | portable-unit-argv | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | V-4, V-5 | 1 executed, 0 failed/skipped; required diagnostic fields present | 1 | 1 |
| CP-4 | S2 | CP-2 | portable-unit-contract | `/*/*/InstructionBundleTests/(the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact*)\|(orchestrator_bundle_points_to_operational_autonomy_without_growing*)\|(C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs*)\|(C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders*)` | V-3, R-1 | 5 cases across 4 methods, 0 failed/skipped | 5 | 1 |
| CP-5 | S2 | CP-2 | portable-unit-repair | `/*/*/RepairSourceDocumentationTests/*` | V-2 | all 3 methods, 0 failed/skipped | 3 | 1 |
| CP-6 | S2 | CP-2 | portable-unit-pipeline | `/*/*/StandingPipelinePolicyDocumentationTests/(the_policy_phrases_are_pinned_in_every_copy*)\|(the_bundle_no_longer_states_the_old_override_rule*)` | V-2 | 4 copies + 1 override method, 0 failed/skipped | 5 | 1 |
| CP-7 | S2 | CP-2 | portable-unit-settings | `/*/*/OrchestratorInstructionsGuidanceTests/(The_policy_copies_keep_every_pinned_phrase_and_each_names_the_file*)\|(The_bundle_names_the_file_route_compaction_and_precedence_and_dropped_the_swapped_sentences*)` | V-2 | both methods, 0 failed/skipped | 2 | 1 |
| CP-8 | S2 | CP-2 | portable-unit-platform | `/*/*/TaskPlatformGuidanceTests/(Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes*)\|(Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning*)` | V-2 | both methods, 0 failed/skipped | 2 | 1 |
| CP-9 | S2 | CP-2 | portable-unit-placement | `/*/*/RunnerDefaultGuidanceTests/(Bundles_read_runtime_defaults_without_pinning_location*)\|(Platform_pins_are_explicit_and_bundles_defer_to_orchestrator_contract*)` | V-2 | both methods, 0 failed/skipped | 2 | 1 |
| CP-10 | S2 | CP-2 | portable-unit-pin | `/*/*/CheckpointRepeatDocumentationTests/repeat_budget_reaches_code_briefs_without_bundle_growth` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-11 | S2 | CP-2 | portable-unit-policy | `/*/Antiphon.Tests.Application/(KeepSixDocumentationTests*)\|(PostLandRetrospectiveContractTests*)/*` | R-2 | all 7 methods in the two named classes, 0 failed/skipped | 7 | 1 |
| CP-12 | S2 | CP-2 | portable-unit-fragments | `/*/*/InstructionFragmentTests/C1213_ExpandedBudgetIsEnforced` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |

### Cost

Ordinary Code floor: **26 minutes** (8 + 8 + ten 1-minute rows), excluding slot contention and tool bootstrap. Budget another **15–20 minutes** for the two commissioned red groups and **60–90 minutes** for authoring/pin migration: approximately **100–140 minutes** for Code. Separate TestDesign: **20–30 minutes**. Ordinary Review repeats the final required selection with fresh provenance; budget **26 minutes** execution plus semantic review. No suite run or repeat-after-green is commissioned. Post-land method-scoped controls are a separate estimated obligation for TestDesign to size.

## Completion and handoff

Land S1 and S2 to master through the normal pipeline; no RC publication or service restart is commissioned here. Bundle edits affect newly composed launches under the existing refresh mechanism. Review must inspect the rule-to-owner mapping and required test receipts, not approve on size and hashes alone.

TestDesign receives this plan and the named prerequisite evidence. It should finalize negative controls for each moved-rule/link family, cap violation, aggregate-only growth (outside orchestrator), diagnostic consistency and stale embedded content; retain the narrow portable lane and fixed limits. If text changes make the proposed measurements stale, report new measured values rather than relaxing the caps. The next stage is **test-design**.
