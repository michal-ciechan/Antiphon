# Task 2fa788b5 (CARD-1469, Code, Final): standing operating rules into the orchestrator prompts

Outcome: done. The nine operator rules live once in `docs/orchestration-loop.md#standing-operating-rules`; the `orchestrator` bundle, AGENTS.md and both skills carry a one-line pointer each (22 lines added across owners). The bundle's raw SHA-256 pin was recorded red at 76d41e2d, then recomputed; the single checkpoint run at fdeceb67 is green and validated. Rules 1-2 for section 8 of the generated file are operator-owned config: the exact commands are below, not executed.

Branch `feat/card-task-2fa788b5`, worktree `/work/worktrees/task-2fa788b5`, base a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49, landing owner = this task.
Commits: 76d41e2d6f6bfd7f78a234d756fd14299c083501 (S1 owner text, pointers, plan; pin red), fdeceb673deb33a8e2d578d8eca1988aaa34d893 (S2 pin recomputed; tested SHA), then this report commit.
Plan: `docs/superpowers/plans/2026-10-22-card-1469-standing-operating-rules-plan.md`. Evidence: `.antiphon/checkpoints/20261022-090145-1fbe/` (ignored), red TRX `.antiphon/c1469-red/red.trx` (ignored).

## Survey (read-only, before editing)

| Owner | Where (file:line) | Finding |
|---|---|---|
| (a) Generated instructions, section 8 "Operator standing instructions" | `server/Application/Services/OrchestratorInstructionsRenderer.cs:239`; `OrchestratorInstructionsSnapshotBuilder.cs:53`; setting `server/Application/Settings/OrchestratorInstructionsSettings.cs:31` (`Delegation:OrchestratorInstructions:StandingInstructions`), validation `DelegationSettings.cs:1427-1448` (max 10 lines x 300 chars, no `{{key:`); `IOptions`, "Restart to change" (`OrchestratorInstructionsSettings.cs:4`) | No repo default (`server/appsettings.json:310` `Delegation` block has no `OrchestratorInstructions` key). No API or script writes it; `POST /api/orchestrator-instructions/refresh` only re-renders (`docs/ops-http.md:141`). Per-machine store: `dotnet user-secrets --id antiphon-server` or gitignored `server/appsettings.Development.json` (`docs/bootstrap.md:205-207`). Live copy 2026-10-22 (v6576112c rev 454): two rollout lines in section 8; section 9 keep-six `Antiphon-Orchestrator: on; target 7`. |
| (b) Launch bundles | Files `server/Bundles/*.md`, embedded by `server/Antiphon.Server.csproj:26`; catalog `InstructionBundles.cs:311-330`, version = first 8 hex of SHA-256 over LF-normalised trimmed text (`InstructionBundles.cs:24`); composed by `InstructionBundleComposer`; `ForDelegate` gives Orchestrator tasks `orchestrator` + `delegate-basics` (`InstructionBundles.cs:233-236`) | Pins: raw-SHA `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs:42-45`; 11,500-unit body allocation `OrchestratorBundleOwnershipTests.cs:284` (11,077 at base); 28,000 composition threshold `InstructionBundleTests.cs:683`; embedded = disk `InstructionBundleTests.cs:1095`; phrase pins `OrchestratorInstructionsGuidanceTests.cs:73-105`, `StandingPipelinePolicyDocumentationTests.cs:74`, `KeepSixDocumentationTests.cs:16`, `MechanicalPcContractTests.cs:142`, `RepairSourceDocumentationTests.cs:33`, `PostLandRetrospectiveContractTests.cs:55`, `ChannelOutboundContractTests.cs:48`. `board-api.md` (card API usage) and `style-brief.md` (reply style) carry no policy. |
| (c) AGENTS.md + import contract | `AGENTS.md:77-89` "Cards and tracker"; `docs/agent-instruction-file-contract.md:41-61` (generated file excluded from imports and bundles; precedence: live file > bundle numbers > docs) | Pinned by `OrchestratorInstructionsGuidanceTests.cs:87-105,119-127`, `StandingPipelinePolicyDocumentationTests.cs:126`, `KeepSixDocumentationTests.cs:35-45` (exactly one AGENTS bullet may contain both "server setting" and "The server does not dispatch"). |
| (d) `docs/orchestration-loop.md` | `### Standing pipeline policy` line 506 (anchor `standing-pipeline-policy`), keep-six line 591, post-land activation check line 360, verification rounds anchor line 2409 | Owner of pipeline policy: full text goes here. |
| (e) Skills | Repo-level `.claude/skills/antiphon-orchestrator/SKILL.md` (§0 line 13; policy slice starts at line 29) and `.claude/skills/antiphon-delegate/SKILL.md` (`## Rules` line 308) | Pinned by `OrchestratorInstructionsGuidanceTests.cs:94`, `StandingPipelinePolicyDocumentationTests.cs:61`, `MechanicalPcContractTests.cs:139` (routing rule counted once), `PostLandRetrospectiveContractTests`, `RunnerBranchContractDocumentationTests`, `CommitOnSettleDocumentationTests`, `PostLandMutationContractTests`. |

Card binding verified: task row `cardIdentifier=CARD-1469`, title "Carry the standing orchestration rules into the orchestrator prompts, bundles, docs and skills" matches the brief.

## Changes

- `docs/orchestration-loop.md`: new `<a id="standing-operating-rules"></a>` + `### Standing operating rules (operator, 2026-10-22; CARD-1469)` after the keep-six section (15 lines: intro + rules 1-9, one or two sentences each). Numbers owned by the generated file are referenced (section 9 keep-six target; "the operator said 7 on 2026-10-22"), not copied.
- `server/Bundles/orchestrator.md`: one pointer paragraph after "Never edit it." (3 lines). Body 11,389 of 11,500 units (headroom 111); worst-case composition 571 under the 28,000 early threshold; new version `vcf6bdf1d`.
- `AGENTS.md`: one pointer bullet after the pipeline-policy bullet (line 83).
- `.claude/skills/antiphon-orchestrator/SKILL.md` line 31 and `.claude/skills/antiphon-delegate/SKILL.md` (Rules) : one pointer line each.
- `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs`: pin `2ec1bd25…0129` -> `d46d51948734b70e09aba019882896042a713d7c70fcaefde491e37f723e1534` with a CARD-1469 comment.
- `docs/agent-instruction-file-contract.md`: unchanged (it owns the import mechanism, not policy).

## Operator action for rules 1-2 (section 8; not executed, settings are operator-owned)

On the desktop main checkout (indices 0 and 1 are the two existing rollout lines; if those live in `server/appsettings.Development.json` append there instead), then restart because the setting is `IOptions`:

```
dotnet user-secrets set "Delegation:OrchestratorInstructions:StandingInstructions:2" "Failures get a Debug task: a failed dispatch/launch/infra failure gets a Debug delegate now with the exact error, times, task ids and what else ran; one retry is first aid, never the whole response. Search the board before filing the fix card. docs/orchestration-loop.md#standing-operating-rules" --id antiphon-server
dotnet user-secrets set "Delegation:OrchestratorInstructions:StandingInstructions:3" "Keep the in-flight target on the landing host: it counts server2 tasks landing onto master (section 9 has the number); desktop-only Windows work is extra. Refill after every completion; raise the project cap via dispatch-concurrency.ps1 set, reason = operator words, never -IgnoreConcurrencyLimit." --id antiphon-server
pwsh -NoProfile -File scripts/restart-apphost.ps1
```

Both lines are under 300 characters (295, 297) and carry no `{{key:` placeholder.

## Verification

Red witness V-1 (direct, build-slot gated; reason: the brief's red witness for the hash pin): at 76d41e2d6f6bfd7f78a234d756fd14299c083501, `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1469-red/` (`BUILD SLOT granted lease=9c116c06-4f2a-4062-805e-267607acba58 waited=0s maxcpucount=6`, held 309s) then `dotnet run --no-build … --treenode-filter '/*/*/(CheckpointRepeatDocumentationTests*)|(OrchestratorBundleOwnershipTests*)|(InstructionBundleTests*)/(repeat_budget_reaches_code_briefs_without_bundle_growth*)|(C1332_OrchestratorBodyFitsItsAllocation*)|(the_worst_case_composition_measured_sits_far_under_the_budget*)'` (`BUILD SLOT granted lease=324f5578-6557-4bb7-97f6-04a2a5b2788e waited=0s`): total 4, failed 1 (`repeat_budget_reaches_code_briefs_without_bundle_growth` at `orchestrator-approved-bytes`, expected 2ec1bd25…), skipped 0; the allocation (2 cases, measured=11389 limit=11500) and composition controls passed. Exit 2. The `bin-c1469-red` directories were deleted afterwards.

Checkpoint run (one run, `start --plan … --after S2 --expected-source-sha fdeceb67…`, then `wait`): run `20261022-090145-1fbe`, verdict GREEN exit=0, wall 5m30s, builds 1, unlisted: none.

```
commit: fdeceb673deb33a8e2d578d8eca1988aaa34d893  branch: feat/card-task-2fa788b5  worktree: /work/worktrees/task-2fa788b5  host: Debian GNU/Linux 12 (bookworm) cores=24
source: fdeceb673deb33a8e2d578d8eca1988aaa34d893 state=clean buildSource=verified
CHECKPOINT CP-2 commit=fdeceb673deb33a8e2d578d8eca1988aaa34d893 build=reused filter=/*/*/(InstructionBundleTests*)|(MechanicalPcContractTests*)|(ChannelOutboundContractTests*)/(the_worst_case_composition_measured_sits_far_under_the_budget*)|(orchestrator_bundle_points_to_operational_autonomy_without_growing*)|(C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders*)|(C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow*)|(Server_and_gateway_keep_source_and_transport_boundaries*) executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-2fa788b5/.antiphon/checkpoints/20261022-090145-1fbe/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=fdeceb673deb33a8e2d578d8eca1988aaa34d893 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=292.7406624s startup=4.7721678s testsWall=0.0814537s teardown=0.6890555s hostWall=5.5424282s
CHECKPOINT CP-1 commit=fdeceb673deb33a8e2d578d8eca1988aaa34d893 build=ok filter=/*/*/(OrchestratorBundleOwnershipTests*)|(OrchestratorInstructionsGuidanceTests*)|(StandingPipelinePolicyDocumentationTests*)|(KeepSixDocumentationTests*)|(RepairSourceDocumentationTests*)|(PostLandRetrospectiveContractTests*)|(ReleaseBranchModelDocumentationTests*)|(RunnerBranchContractDocumentationTests*)|(CommitOnSettleDocumentationTests*)|(CheckpointRepeatDocumentationTests*)|(PostLandMutationContractTests*)/* executed=80 passed=80 failed=0 skipped=0 trx=/work/worktrees/task-2fa788b5/.antiphon/checkpoints/20261022-090145-1fbe/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=fdeceb673deb33a8e2d578d8eca1988aaa34d893 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=0s startup=5.4951279s testsWall=0.7906281s teardown=0.6828096s hostWall=6.9685658s
```

`scripts/validate-checkpoint-receipt.ps1 -Evidence …/report.json -ExpectedSourceSha fdeceb67…`: `CHECKPOINT SOURCE VALID source=fdeceb673deb33a8e2d578d8eca1988aaa34d893 rows=2`, exit 0.

TRX inspection: CP-1 contains every intended class (OrchestratorBundleOwnershipTests 10 cases, OrchestratorInstructionsGuidanceTests 6, StandingPipelinePolicyDocumentationTests 11, KeepSixDocumentationTests 3, RepairSourceDocumentationTests 3, PostLandRetrospectiveContractTests 4, ReleaseBranchModelDocumentationTests 2, RunnerBranchContractDocumentationTests 8, CommitOnSettleDocumentationTests 2, CheckpointRepeatDocumentationTests 1, PostLandMutationContractTests 30), all Passed. CP-2 contains the five named methods (1 + 2 + 1 + 5 + 1 = 10), all Passed.

Invariants: V-1 red then green (above). R-1..R-5 (CP-1) passed. R-6..R-8 (CP-2) passed. Ordinary scope for this docs/prompt task: the brief's minimal-testing profile (narrow pins) replaces the whole Unit lane; no whole-lane run was made. repeat-proof: one normal repetition per selection, none after green. No manual acceptance beyond the operator command above. PCs: none planned (docs and prompt text only); nothing pending for Mutation.

Other builds/tests outside the checkpoint run: the red-witness build+run above, and `dotnet build tools/Antiphon.Checkpoints` (build-slot lease 713c5887-504e-4441-963a-61bd20c8a1ab, held 5s) to run the tool from its dll. No other build or test driver ran.

## Landability and rc

- `git merge-tree --write-tree origin/master HEAD` at origin/master b59706530950ad5fb334c216a6f18a4e9bfd5103: exit 0, tree 1e0b265b90a85bab387cafdf678b228e1e5063cb, no conflicts. Master's drift since base touches only `docs/orchestration-loop.md` elsewhere (CARD-1430/1461 docs); `server/Bundles/orchestrator.md` and the pin are unchanged on master, so the pin survives the landing rebase.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef a38e1e1bd… -HeadRef fdeceb67…`: `commits=2 entries=0 violations=0`, exit 0 (re-run over the report commit below).
- rc: the AppHost and server2 run `rc/2026-10-20` (tip ceb6e3d6b78471db6831258699c493b8eb2d619b), whose `server/Bundles/orchestrator.md` has the same bytes as the task base (sha 2ec1bd25…) and the same pin test, AGENTS.md and skills. The bundle text reaches orchestrators only after the rc pick and a restart onto the new rc tip. Port (reviewed cherry-pick of 76d41e2d and fdeceb67, dropping `docs/superpowers/plans/2026-10-22-card-1469-standing-operating-rules-plan.md` as rc carries no plan docs): `server/Bundles/orchestrator.md`, `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs`, `docs/orchestration-loop.md`, `AGENTS.md`, `.claude/skills/antiphon-orchestrator/SKILL.md`, `.claude/skills/antiphon-delegate/SKILL.md`. Simulated with `git merge-tree --write-tree --merge-base` per commit onto ceb6e3d6: both clean (trees 90e99e90…, 099ea557…). The section 8 lines are config on the desktop and need no port.
