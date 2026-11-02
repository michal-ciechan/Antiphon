# CARD-1570 S1 — task aea81717 (Code, Final, Linux server2)

Outcome: S1 implemented and green. A red run's kept outputs are no longer recorded as the `still-present` cleanup refusal; real leftovers are still refused. Docs clause added. S2 not implemented (policy).

Branch feat/card-task-aea81717, worktree /work/worktrees/task-aea81717, base e18a1177d9e042ac5986cf8bdebc5b47abb7ca11 (master). Original Code task / landing owner: aea81717.

Commits:
- 53750e1b249c8cb0d5fe04fcd75d43eeb44ff3aa test: W-1/W-1b witnesses (red)
- 33827066b74754a1129e01fe0da0480e58b01b9e fix + docs (tested SHA)

## Fix
tools/Antiphon.Checkpoints/CheckpointApp.cs: when CleanOwnedOutputs returns KeptBecauseRed, remember state.CreatedOutputPaths. The final sweep sets CleanedOutputs=false for any present output, but adds `still-present` only for present paths that are not in that kept set. The red run still exits non-zero and validates `report_failed`. A refusal from CleanOwnedOutputs, the `cleanup-failed` exception path, a green-run leftover and an output this run did not create are all still recorded.
docs/orchestration-loop.md (Code checkpoint paragraph, "A Code brief names the checkpoint list"): receipt-only gap -> `done` + `RECEIPT GAP:` + `next: code`, never `failed` (settlement follows the closing token, AgentTaskReplyService.ClassifyReportAsync); a red run's receipt fails `report_failed`, not `cleanup_refused`. No bundle edit.

## Witnesses (CheckpointSourceExecutionTests)
- W-1 red_run_kept_outputs_are_not_recorded_as_cleanup_refusals: ExecuteAsync, CP-1 green and CP-2 red on bin-c1570, KeepOutputs=false, CleanOnRed=false. Asserts exit 1, CleanupRefusals empty, CleanedOutputs false, output kept, report.md has no `refused: still-present`, and ReportValidator plus validate-checkpoint-receipt.ps1 return report_failed with and without -Rows CP-1.
  Red at 53750e1b: `should be empty but had ["still-present"]` (build-slot run: 1 failed, 1 passed). Green at 33827066.
- W-1b red_run_still_refuses_output_it_did_not_create: same red run plus a foreign other/bin-c1570 that the run did not create -> ["still-present"], cleanup_refused in both validators. Green at base and at the fix.
- Mutants (native build-slot builds, restored): "keep recorded again" is the base code, so W-1 goes red (above). "Real refusals suppressed too" (`remaining` empty whenever anything was kept) makes W-1b red: `should be ["still-present"] but was []`, 1 failed, 1 passed. Restored with git checkout; tree clean.
- All-green guards stay green: host_written_entries..._cleaned_on_green (SOURCE VALID) and cleanup_refusals_disqualify_green_rows_in_both_validators.

## Checkpoint runs (ignored local plans .antiphon/card-1570-s1-checkpoints.md and card-1570-s1-affected.md; no committed plan exists for this card)
Run 20261102-133240-dce2 @33827066, exit 0, CHECKPOINT SOURCE VALID rows=2 (tool and script):
CHECKPOINT CP-1 commit=33827066b74754a1129e01fe0da0480e58b01b9e build=ok filter=/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-aea81717/.antiphon/checkpoints/20261102-133240-dce2/rows/CP-1/run.trx slot=granted waited=90s dirty=0 source=33827066b74754a1129e01fe0da0480e58b01b9e sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=33827066b74754a1129e01fe0da0480e58b01b9e build=reused filter=/*/Antiphon.Tests.Application/(D2AdvisoryDocumentationTests*)|(InstructionBundleTests*)|(KeepSixDocumentationTests*)|(OrchestratorInstructionsGuidanceTests*)|(StandingPipelinePolicyDocumentationTests*)/* executed=106 passed=106 failed=0 skipped=0 trx=/work/worktrees/task-aea81717/.antiphon/checkpoints/20261102-133240-dce2/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=33827066b74754a1129e01fe0da0480e58b01b9e sourceState=clean buildSource=verified
TRX: CP-1 has 11 results, both red_run_* Passed. CP-2 class counts: D2 7, InstructionBundle 78, KeepSix 3, OrchestratorInstructionsGuidance 6, StandingPipelinePolicy 12.
Run 20261102-134619-04c4 @33827066, exit 0 GREEN, but validate says row_failed because skipped>0:
CHECKPOINT CP-3 commit=33827066b74754a1129e01fe0da0480e58b01b9e build=ok filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*)|(CheckpointStatePublicationTests*)|(CheckpointAdmissionContractTests*)|(CheckpointReceiptSafetyTests*)|(CheckpointSelectedOutputTests*)/* executed=65 passed=65 failed=0 skipped=4 trx=/work/worktrees/task-aea81717/.antiphon/checkpoints/20261102-134619-04c4/rows/CP-3/run.trx slot=granted waited=60s dirty=0 source=33827066b74754a1129e01fe0da0480e58b01b9e sourceState=clean buildSource=verified
The 4 skips are Windows-only tests that skip on Linux by construction (windows_set_clock_and_foreign_dacl_are_not_adopted, windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write, windows_copied_old_change_time_is_adopted, windows_sharing_violation_retries_then_adopts).

Unlisted builds and runs, with reasons:
- `dotnet run --project tools/Antiphon.Checkpoints -- import`: one tool build outside a slot, the documented front door, used to validate the tables. Disclosed.
- build-slot native builds of tests/Antiphon.Tests: the red witness at 53750e1b, mutant M2, and the restore at HEAD so the native output is not a mutant. Plus a run per red check (slots waited 0-15s).
- build-slot Release build of tools/Antiphon.LandGraphAudit for -Check.

Not run: the whole Unit lane. The brief asked for narrow checkpoint-tool classes, and standing rule 8 bars whole-Unit runs from Code. It is deferred to Final, not marked passed.

## Gates
- Land graph -Check (-ToolPath native Release dll): `approved`, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE, exit 0. -Update was not run.
- check-evidence-diff over e18a1177d..33827066: commits=2 entries=0 violations=0, exit 0.
- merge-tree onto origin/master: clean. Per-commit chained cherry-pick merge-tree onto origin/rc/2026-11-01: both commits clean (CheckpointApp.cs is identical on rc and base). rc carry is optional: the checkpoint tool serves Code tasks, not the rc gate, so master-only is enough unless the operator wants rc Code delegates to get the fix from the rc tip.

## PCs pending for Mutation
PC-1: re-record the keep (drop the Kept filter) -> W-1 red. PC-2: suppress all still-present once anything is kept -> W-1b red. PC-3: drop keptBecauseRed.AddRange -> W-1 red. All are method-scoped on CheckpointSourceExecutionTests/red_run_*.

## Open questions
- S2 (letting --rows qualify green rows of a mixed run) is not implemented. It is a policy question: the admissible exit set and whether an ancestor-SHA receipt covers the tip.
- The investigation also suggested a distinct `outputs: kept (red)` report.md line. Not done: report.md already prints outputs and verdict, and adding the line was not needed for S1.
