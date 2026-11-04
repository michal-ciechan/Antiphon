# Code e778bd07: CARD-1709 mutation survivor witnesses

Test-only. Base master d95d3f2cdce02fd95bf2145ecee979769cfc4c3c. Branch feat/card-task-e778bd07.
Source report: Mutation e24018e3 `docs/investigations/2026-11-04-mutation-0851-1709.md`.

## Commits

| SHA | What |
|---|---|
| 8f869e5600d9aa937b2831e8c71b124f0cbacbeb | Witnesses: four new C1709 tests, B5 tail on the retry witness, bounded StopAsync in the C1709 hosted witnesses and C1360 |
| 2baea8e38761ddfe88b926c55abf6dc67cd40d20 | Compile fix (timer list compared as an array); tested SHA for every run below |

Files: `tests/Antiphon.Tests/Application/TaskSupervisionReceiptTests.C1709.cs`,
`tests/Antiphon.Tests/Application/CompletionNoteDurableSweepTests.cs` (StopAsync bound only).
No production file changed.

## Witnesses and mutants

The mutants were applied in one build behind an env gate (`E78_MUT=<id>`) by an exact-path edit of
`server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs`, then restored with
`git checkout --` and rebuilt. Witness set filter:
`/*/*/(TaskSupervisionReceiptTests*)|(CompletionNoteDurableSweepTests*)/(C1709_*)|(C1360_*)` (9 tests).

| ID | Mutant text | Witness | Red (assertion) | Green (restored) |
|---|---|---|---|---|
| control | gate off on the mutant build | all 9 | 9/9 passed | - |
| B6a | `.RecoverAsync(taskId, ct, new HashSet<Guid>())` (supervision gets its own set) | `C1709_Busy_recipient_keeps_its_supervision_notice_ahead_of_its_completion_note` (i) and `C1709_Pass_whose_only_busy_item_is_a_supervision_notice_retries_before_the_backstop` (ii) | 2/9 failed: (i) parent rows 1, expected 0; (ii) `DueTimes.First()` 15 min, expected 5 s | passed |
| B6b | task branch `RecoverMissingAsync(..., task, ct, new HashSet<Guid>())` | `C1709_Busy_task_pass_retries_skipped_work_before_the_backstop` | 1/9 failed: `DueTimes.ElementAt(1)` 15 min, expected 5 s | passed |
| B5 | `if (busySessions.Count > 0 \|\| true)` | `C1709_Busy_pass_retries_skipped_work_before_the_backstop` (new tail) | 4/9 failed: Retry ("the pass after the retry arms the fifteen-minute backstop" never), plus C1360, the task-pass and two-session witnesses | passed |
| B10 | `foreach busy: _ = Task.Delay(5 s, clock, ct).ContinueWith(RequestSweep)` instead of the `Min` deadline | `C1709_Two_busy_sessions_in_one_pass_arm_one_retry_timer` | 1/9 failed: count of 5 s timers 2, expected 1 | passed |
| B11b | loop `WaitAsync(nextSweep, clock, CancellationToken.None)` | bounded `StopAsync(default).WaitAsync(10 s)` in the four hosted C1709 witnesses and C1360 | 5/9 failed with `TimeoutException` at ~11-25 s; no hang, TRX written | passed |

Equivalents left as-is:
- A12 (CARD-0851 broker-level never-pruned kept-flag set): equivalent; fresh Guid per lease, observable only by reflection.
- B11 (fire-and-forget retry delay ignoring shutdown): equivalent; the landed code has no separate timer, a late RequestSweep writes an unread flag.

## Runs (all at 2baea8e38, restored build, one build-slot lease each)

| Run | Scope | Result | Slot waited / held |
|---|---|---|---|
| build (mutant gates) | Antiphon.Tests | 1st attempt CS1929 (test compile), 2nd succeeded | 0 s / 142 s, 0 s / 106 s |
| witness runs | control + 5 mutants | see table | 0 s each |
| build (restored) | Antiphon.Tests | succeeded; Antiphon.Server.dll has no E78_MUT string | 0 s / 151 s |
| G1 | 13 CARD-1709 classes + 4 new | 369 total, 366 passed, 3 failed (see below) | 0 s / 581 s |
| G1 rerun of the 3 reds | OutputDistillationDeliveryTests three methods (all Arguments cases) | 12/12 passed | 15 s / 73 s |
| GUARD | TestClassificationPolicy/Guard, SlowTestRegistryMerge, SlowTestTripwire | 47/47 | 0 s / 93 s |
| SCAN | 9 CARD-1705 scanner classes (Mutation's set) | 210/210 | 0 s / 1351 s |

G1 reds: `Composed_summary_and_metadata_obey_utf8_spill_guard(1)`, `Unusable_file_uses_api_recovery(corrupt)`,
`Legacy_minimum_and_unicode_boundaries(1201)` in `OutputDistillationDeliveryTests` (null ResultFilePath /
null path). That file is unchanged by this task and the product is byte-identical to base; all three pass
alone on the same build. Load flake under the G1 run, not caused here.

- `scripts/check-evidence-diff.ps1 -BaseRef d95d3f2cd -HeadRef HEAD`: commits=2 violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master = d95d3f2cd): clean.

Not run: the whole Unit lane (brief scoped the named groups and a 45-minute budget; the run took ~70 min);
the checkpoint tool (no plan `### Checkpoints` table for this test-only task).

PCs pending for SourceLanding Mutation: B5, B6a, B6b, B10, B11b witnesses above (method-scoped).
