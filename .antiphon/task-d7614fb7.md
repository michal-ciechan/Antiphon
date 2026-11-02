# CARD-1570 S1 Review round 1 (d7614fb7) - clean, no defects

Subject: feat/card-task-aea81717, code 33827066b74754a1129e01fe0da0480e58b01b9e, tip e660864f969d39f63124307bc0fca4a30ec05ed0 (report only after the code), base e18a1177d9e042ac5986cf8bdebc5b47abb7ca11. Landing owner: Code task aea81717.

## Verdict on the priorities
1. Receipt stays invalid when it must. The kept set is filled only on `clean.KeptBecauseRed` (CheckpointApp.cs:449-453). OutputCleanup.cs:41 returns that early, only when exitCode != 0 && !cleanOnRed, BEFORE any delete is attempted. So a red, timeout or slot-refused run has a nonzero exit (and so does an owner-ended one). Nothing after the sweep resets the exit to 0 (CheckpointApp.cs:528-555 only turns green into red). Both validators check exit/verdict before rows (ReportValidator.cs:36, validate-checkpoint-receipt.ps1:71), so `report_failed` holds with or without -Rows. Branches walked: green leftover -> still-present + exit Invalid (unchanged). Cleanup refusal / cleanup-failed exception -> only reachable when not kept (unchanged). Foreign or pre-existing output -> not in CreatedOutputPaths -> still-present. A file added inside a kept output by a second process changes nothing (the run is already red). A new bin-<id> elsewhere -> still-present. No path makes an invalid receipt valid.
2. Kept-set identity. It is exactly `state.CreatedOutputPaths`, the before/after snapshot from RecordCreatedOutputs (OutputCleanup.cs:128-139), compared through context.PathComparison after trimming the trailing separator. A path the cleanup tried and failed to delete can never be in it: no delete runs on the kept branch.
3. Witnesses: W-1 and W-1b assert outcomes (refusal list, kept dir, both validators x {all, CP-1}).
4. Docs clause: consistent with ClassifyReportAsync (AgentTaskReplyService.cs:4027-4051). There is no other `RECEIPT GAP` or contrary `failed` rule in docs/ or .claude/skills.
5. Gates: all green (below).

## Runs (all on server2, Linux)
Checkpoint run 20261102-141422-2c14 @e660864f969d39f63124307bc0fca4a30ec05ed0 (sourceState=clean, buildSource=verified, unlisted: none, one build, bin-r7614 deleted):
- CP-1 CheckpointSourceExecutionTests/*: executed=11 passed=11 failed=0 skipped=0
- CP-2 five doc classes: executed=106 passed=106 failed=0 skipped=0
- CP-3 five cleanup/receipt classes: executed=65 passed=65 failed=0 skipped=4. The 4 skips are Windows-only: windows_sharing_violation_retries_then_adopts, windows_set_clock_and_foreign_dacl_are_not_adopted, windows_copied_old_change_time_is_adopted, windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write.
- validate: whole receipt INVALID row_failed (the CP-3 skips, expected), from both the tool and the script. `--rows CP-1,CP-2` gives VALID rows=2 in both.
Mutants (build-slot leases, native output, source restored, tree clean):
- M1 = base CheckpointApp.cs: W-1 failed `should be empty but had ["still-present"]`, W-1b passed (total 2, failed 1).
- M2 = `remaining` empty whenever anything was kept: W-1b failed (`should be ["still-present"] but was []`), W-1 passed (total 2, failed 1).
Gates: land graph -Check: approved, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE, exit 0 (no -Update). check-evidence-diff e18a1177d..e660864f9: commits=3 entries=1 violations=0, exit 0. merge-tree onto origin/master 5376ac529: clean. Chained --merge-base onto origin/rc/2026-11-01: clean. Master-only is adequate.
Unlisted runs, with reasons: `dotnet run --project tools/Antiphon.Checkpoints` (the tool front door, its build ran outside a slot); the M1 build and run and the M2 build+run, each in its own build-slot lease (a 9-10 min queue at 4/4); the LandGraphAudit Release build in a slot.

## Not run
The whole Unit lane (Final profile ordinary scope). Server2 slots were saturated: each lease waited 4-10 min. The lane is ~70 min wall. So ordinaryScopeCompleted=Interim. PCs PC-1..PC-3 stay pending for SourceLanding Mutation; M1/M2 above are review mutants, not PC discharge.

## Advisories (no rerun needed)
- A-1 docs/orchestration-loop.md:1617: "settlement follows the closing token alone" overstates it slightly. A `done` can still wait on a lease or be reclassified as completed-without-progress (AgentTaskReplyService.cs:4037-4041). Suggest "settlement follows the closing token".
- A-2 same line: the example "another row red" could be read as allowing `done` while a test is really red. Consider "another, unrelated or inherited, row red".
- A-3: the mirror's native tests/Antiphon.Tests/bin holds the M2 mutant build (not evidence, source clean).
