# CARD-0506 S4 Code round 3 — 34a23fb5 (Review 7d7fe999 R2-F1..R2-F4)

Source commit: 66120a3563744996fd9dab389e0d2ad88a972e44 on feat/card-task-34a23fb5 (from e5f622d8f; original landing owner 2d3fef7b-c47b-415c-b6de-14115878e464). Docs only: docs/orchestration-loop.md, docs/project-context.md.

## Coverage-statement audit (new CARD-0506 sections)

| Statement | Finding |
|---|---|
| orchestration-loop: "Other refetch cases ... not covered" (R2-F1) | False. Replaced by a table of `C506 S2R3 rollback-global-clean`, `rollback-project-{project,global}-clean`, `rollback-dirty-{global,project}`, `reused-revision`, `same-revision-new-content` (DispatchConcurrencyEditor.test.tsx) with their asserted values/PUT bodies. Only occupancy-only refetch kept as untested (grep: no occupancy-only refetch test; source comment cites Review 7826b06f diagnostic). |
| project-context: seed publication "no test" (R2-F2) | False. Cites `DispatchConcurrencySettingsTests.Concurrent_initialization_writes_one_seed`: one MockEventBus event named DispatchConcurrencyChanged after two concurrent initializers; not a delivery witness. |
| orchestration-loop: limitations heading over unmount row (R2-F3) | Notification is asserted (`successes()` length 1, new global editor Revision 7, Code enabled); moved out of the table. Cache invalidation after unmount: no test asserts it (grep unmount/invalidat). |
| orchestration-loop: restore-during-PUT and timestamp-collision limitations | True: the diagnostics exist only in source comments/plan, not in any test file; committed neighbours (`late-put-newer-get`, `after-failed-refresh-equal-data`) test the increasing/new-timestamp cases. Heading reworded "with no committed test". |
| orchestration-loop: "A 422 for the Reason field is not covered" | True for a server 422 (F422 used only for roles.Code.maxParallel and roles). Narrowed, and cites `C506 V03 reason <variant>` (dispatchConcurrencyModel.test.ts) for client validation. |
| antiphon-api: rejected policy PUT on a store with no global row | True: wire 422 bodies run after ReadGlobalAsync; DispatchConcurrencySettingsTests rejections run after ReadGlobalAsync; PolicyTests Reject is parser-only. Unchanged. |
| ops-http | No coverage statement. |

Runner/file labels (R2-F4): intro limits Vitest attribution to `C506 …` titles; the script row names TUnit `DispatchConcurrencyScriptTests.Set_reads_then_sends_exact_snapshot` in tests/Antiphon.Tests/Scripts/DispatchConcurrencyScriptTests.cs; every navigation row now names its .test.tsx file (all titles grep-verified in the named files). Forbidden-word scan of added lines: only the UI label "Clear all overrides".

## Verification

Unlisted build: checkpoint tool bootstrap via scripts/build-slot.ps1 into tools/Antiphon.Checkpoints/bin-code-34a23fb5-tool/ (granted, exit 0, 1 existing warning), removed afterwards. Manifest: external scratch copy of /tmp/antiphon-review-7d7fe999/guards.md with SHA/output name changed. No whole-Unit lane.

run: 20261014-002351-91e3   manifest: /work/worktrees/task-34a23fb5/.antiphon/checkpoints/20261014-002351-91e3/manifest.resolved.yaml
source: 66120a3563744996fd9dab389e0d2ad88a972e44 state=clean buildSource=verified
CHECKPOINT CP-1 commit=66120a3563744996fd9dab389e0d2ad88a972e44 build=ok filter=/*/*/(CheckpointRepeatDocumentationTests*)|(DockerStackDocumentationTests*)|(RepairSourceDocumentationTests*)|(PostLandMutationContractTests*)|(OrchestratorInstructionsGuidanceTests*)|(RunnerBranchContractDocumentationTests*)|(MechanicalPcContractTests*)|(StandingBootDocumentationTests*)|(PipelineDefinitionDocumentationTests*)|(CommitOnSettleDocumentationTests*)|(TaskPlatformGuidanceTests*)|(PostLandRetrospectiveContractTests*)|(InstructionBundleTests*)|(CheckpointManifestDocumentationTests*)|(BlockedTaskParkProjectionTests*)|(DelegationCapabilityContractTests*)|(SessionRunnerAbsenceEvidenceDocumentationTests*)|(PublishedRoutingDocumentationTests*)|(BootStallDocumentationTests*)|(StandingPipelinePolicyDocumentationTests*)/* executed=196 passed=196 failed=0 skipped=0 trx=/work/worktrees/task-34a23fb5/.antiphon/checkpoints/20261014-002351-91e3/rows/CP-1/run.trx slot=granted waited=90s dirty=0 source=66120a3563744996fd9dab389e0d2ad88a972e44 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=90s build=175.7549226s startup=11.8239479s testsWall=0.873848s teardown=0.9476792s hostWall=13.6457809s
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0

validate-checkpoint-receipt: CHECKPOINT SOURCE VALID source=66120a3563744996fd9dab389e0d2ad88a972e44 rows=1

TRX census: 196 Passed, 0 other, 20 classes (same per-class counts as Review 7d7fe999).
