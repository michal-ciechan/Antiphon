# CARD-1469: standing operating rules into the orchestrator prompts

Operator request (2026-10-22): "Make sure these rules are making back to antiphon orchestrator prompt(s)".
Docs and prompt text only, plus the tests that pin them; no behaviour change. Task 2fa788b5 (Code, Final).

## Survey: where orchestrator prompt text comes from

| Owner | Evidence | Role for these rules |
|---|---|---|
| (a) Generated instructions file, section 8 "Operator standing instructions" | `server/Application/Services/OrchestratorInstructionsRenderer.cs:239` renders `standing`; `OrchestratorInstructionsSnapshotBuilder.cs:53` reads `_settings.OrchestratorInstructions.StandingInstructions`; setting class `server/Application/Settings/OrchestratorInstructionsSettings.cs:31` (`Delegation:OrchestratorInstructions:StandingInstructions`, at most 10 lines of 300 chars, no `{{key:`, validated at `DelegationSettings.cs:1427-1448`); `IOptions`, "Restart to change" (`OrchestratorInstructionsSettings.cs:4`). No repo default: `server/appsettings.json` `Delegation` block (line 310) has no `OrchestratorInstructions` key. No API or script writes it; `POST /api/orchestrator-instructions/refresh` only re-renders (`docs/ops-http.md:141`). Per-machine store per `docs/bootstrap.md:205-207`: `dotnet user-secrets --id antiphon-server` or gitignored `server/appsettings.Development.json`. Live copy on 2026-10-22 (`GET /api/orchestrator-instructions`, v6576112c rev 454): two rollout lines; section 9 keep-six `Antiphon-Orchestrator: on; target 7`. | Operator-owned live lines; rules 1-2 belong here as two standing lines (command in the task report), not in repo code. |
| (b) Launch bundles `[bundle:orchestrator v…]`, `[bundle:board-api …]`, `[bundle:style-brief …]` | Files `server/Bundles/*.md`, embedded by `server/Antiphon.Server.csproj:26`, loaded and hashed by `InstructionBundles.cs:311-330` (`Version` = first 8 hex of SHA-256 over LF-normalised trimmed text, `InstructionBundles.cs:24`); composed by `InstructionBundleComposer`; attached to Orchestrator tasks by `ForDelegate` (`InstructionBundles.cs:233-236`). Pins: `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs:42-43` (raw SHA-256 of `orchestrator.md`), `OrchestratorBundleOwnershipTests.cs:284` (11,500-unit body allocation; 11,077 used at base), `InstructionBundleTests.cs:683` (28,000 composition threshold) and `:1095` (embedded = disk), phrase pins in `OrchestratorInstructionsGuidanceTests.cs:73-105`, `StandingPipelinePolicyDocumentationTests.cs:74`, `KeepSixDocumentationTests.cs:16`, `MechanicalPcContractTests.cs:142`, `RepairSourceDocumentationTests.cs:33`, `PostLandRetrospectiveContractTests.cs:55`, `ChannelOutboundContractTests.cs:48`. `board-api.md` is card-API usage, `style-brief.md` is a reply style: neither carries policy. | One pointer paragraph in `orchestrator.md` (the allocation leaves 423 units). Hash pin recomputed. |
| (c) AGENTS.md "Cards and tracker" (`AGENTS.md:77-89`) and the import contract `docs/agent-instruction-file-contract.md` (CLAUDE.md `@AGENTS.md`; generated file excluded from imports, `:56-58`; precedence rule `:60`) | Pinned by `OrchestratorInstructionsGuidanceTests.cs:87-105,119-127`, `StandingPipelinePolicyDocumentationTests.cs:126`, `KeepSixDocumentationTests.cs:35-45`. | One pointer bullet after the pipeline-policy bullet. Contract doc unchanged. |
| (d) `docs/orchestration-loop.md` §1 | Owner of pipeline policy (`### Standing pipeline policy`, line 506; keep-six, line 591). | **Full text, once**: new `### Standing operating rules` after the keep-six section, anchor `standing-operating-rules`. |
| (e) Skills | Repo-level `.claude/skills/antiphon-orchestrator/SKILL.md` (§0 policy defaults, line 13) and `.claude/skills/antiphon-delegate/SKILL.md` (`## Rules`, line 308); pinned by `OrchestratorInstructionsGuidanceTests.cs:94`, `StandingPipelinePolicyDocumentationTests.cs:61`, `MechanicalPcContractTests.cs:139`, `PostLandRetrospectiveContractTests`, `RunnerBranchContractDocumentationTests`, `CommitOnSettleDocumentationTests`, `PostLandMutationContractTests`. | One pointer line each. |

## Slices

- S1: owner text (d), pointers (b)(c)(e), this plan. The raw-SHA pin in `CheckpointRepeatDocumentationTests` goes red (V-1).
- S2: recompute the pin from the final LF bytes (`sha256sum server/Bundles/orchestrator.md`).

## Verification

- V-1 red witness (direct, build-slot gated, after S1): `/*/*/(CheckpointRepeatDocumentationTests*)|(OrchestratorBundleOwnershipTests*)|(InstructionBundleTests*)/(repeat_budget_reaches_code_briefs_without_bundle_growth*)|(C1332_OrchestratorBodyFitsItsAllocation*)|(the_worst_case_composition_measured_sits_far_under_the_budget*)` expects 4 executed: `repeat_budget…` failed at `orchestrator-approved-bytes`, the allocation and composition controls passed.
- R-1..R-5 (CP-1): every documentation/contract class that pins `orchestrator.md`, `docs/orchestration-loop.md`, AGENTS.md or the two skills stays green.
- R-6..R-8 (CP-2): bundle composition under the 28,000 threshold; embedded resource equals disk; the mechanical-PC and channel phrase pins on the bundle.

### Checkpoints

`portable-unit-*` names the lane; runtime defaults choose the eligible host. Both rows share one build in the same After group.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1469/` | portable-unit-pins | `/*/*/(OrchestratorBundleOwnershipTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(KeepSixDocumentationTests*)\|(RepairSourceDocumentationTests*)\|(PostLandRetrospectiveContractTests*)\|(ReleaseBranchModelDocumentationTests*)\|(RunnerBranchContractDocumentationTests*)\|(CommitOnSettleDocumentationTests*)\|(CheckpointRepeatDocumentationTests*)\|(PostLandMutationContractTests*)/*` | R-1..R-5 | all executed passed, 0 failed, 0 skipped; about 80 cases | 60 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1469/` | portable-unit-bundle | `/*/*/(InstructionBundleTests*)\|(MechanicalPcContractTests*)\|(ChannelOutboundContractTests*)/(the_worst_case_composition_measured_sits_far_under_the_budget*)\|(orchestrator_bundle_points_to_operational_autonomy_without_growing*)\|(C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders*)\|(C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow*)\|(Server_and_gateway_keep_source_and_transport_boundaries*)` | R-6..R-8 | exactly 10 executed, 10 passed, 0 failed/skipped | 10 | 8 |
