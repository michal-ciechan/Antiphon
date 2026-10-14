# CARD-1267, CARD-1268, CARD-1269 checkpoint tool test fixtures

Code and landing owner: `7d8dcb15-1d53-4a48-84f6-5dfa63c320c8`.
Task base: `9cbe00bd52dd77367a34244c9cb9ea7d24539add` (`git merge-base origin/master HEAD`).

Inherited Unit failures in `tests/Antiphon.Tests/Checkpoints`, one group per card, reported by
CARD-1262 Code task 46e9f228 on Linux (see each card). The baseline messages are in run 5a6a
`failures.md` (Code evidence, Sources):

- CARD-1267: `CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns`
  (one run, expected two) and five `RerunPolicyTests` methods (exit 2 where 0 or 1 is
  expected; `reruns=1` absent).
- CARD-1268: `CheckpointProcessIdentityTests.confirmed_dead_local_identity_permits_cleanup`
  (Refused, expected Removed).
- CARD-1269: `TimeoutTests.total_deadline_skips_a_queued_build_without_starting_it` and
  `total_deadline_kills_running_and_skips_queued` (no build started; rows `slot-refused`).

S1 commits this verification design before any reproduction. S2 onwards repairs each
card red-first: decide per test whether the fixture or the product is wrong and fix
that. Preserve every outcome assertion, production guard, skip and deadline; add an
environment guard only for a dependency genuinely unavailable on the runner image.

## Verification design

- V-1 (CARD-1267): CP-1 and CP-2 reproduce the six failures at S1 with the same
  assertions; after repair the full classes pass.
- V-2 (CARD-1268): CP-3 reproduces the confirmed-dead cleanup witness at S1; after
  repair the full class passes with the positive `Removed` witness intact.
- V-3 (CARD-1269): CP-4 reproduces both deadline failures at S1; after repair both
  reach their deadline assertions and the class passes on Linux. Four Windows-only
  methods in the class keep their existing platform skips (disclosure, not passed).
- R-1: adjacent `RowRunnerTests`, `RunSchedulerTests` and `RerunFilterHostTests`
  (CP-5 to CP-7) stay green after the repair.
- R-2: manually inspect the final diff for unchanged outcome assertions, no widened
  deadline, no skip added, no production slot/lease/cleanup guard loosened; run both
  receipt validators, task-base evidence-diff and fetched-master merge-tree.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1267-proof/` | rerun-policy | `/*/*/RerunPolicyTests/*` | V-1 | all 6; baseline 1 passed/5 failed; final 6 passed | 6 | 10 | false | n/a |
| CP-2 | S1 | CP-1 | import | `/*/*/CheckpointImportTests/*` | V-1 | all 21; baseline 20 passed/1 failed; final 21 passed | 21 | 2 | false | n/a |
| CP-3 | S1 | CP-1 | process-identity | `/*/*/CheckpointProcessIdentityTests/*` | V-2 | all 7; baseline 6 passed/1 failed; final 7 passed | 7 | 2 | false | n/a |
| CP-4 | S1 | CP-1 | timeout | `/*/*/TimeoutTests/*` | V-3 | 4 executed on Linux plus 4 Windows-only skips; baseline 2 passed/2 failed/4 skipped; final 4 passed/4 skipped | 4 | 3 | false | n/a |
| CP-5 | S1 | CP-1 | row-runner | `/*/*/RowRunnerTests/*` | R-1 | all 13 passed | 13 | 2 | false | n/a |
| CP-6 | S1 | CP-1 | scheduler | `/*/*/RunSchedulerTests/*` | R-1 | all 15 passed | 15 | 2 | false | n/a |
| CP-7 | S1 | CP-1 | rerun-host | `/*/*/RerunFilterHostTests/*` | R-1 | 1 passed | 1 | 2 | false | n/a |
| CP-8 | S1 | CP-1 | timeout-linux | `/*/*/TimeoutTests/(total_deadline_skips_a_queued_build_without_starting_it*)\|(total_deadline_kills_running_and_skips_queued*)\|(row_deadline_marks_timeout_without_global_driver_kill*)\|(row_deadline_is_max_15_or_3x_estimate*)` | V-3 | exactly the 4 Linux methods passed, 0 failed/skipped | 4 | 2 | false | n/a |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Run all rows
at S1 (baseline) and again after the final repair slice, with the exact committed
expected SHA, `--total-timeout 150m`, no `--keep-outputs`, and wait to completion.
Linux builds use `UseAppHost=false`. Bootstrap the checkpoint tool once through
`build-slot.ps1` into `bin-c1267-tool/`; that is the sole planned unlisted build.
Remove only this task's literal output roots after their drivers exit.

Amendments after S1 (Code evidence, Disclosures): `3dd89f6a4` set CP-4 Min from 8 to 4,
because the tool's `executed` excludes skipped cases (run 9b83 CP-4 line: `executed=4 skipped=4`).
`ba943a15f` added CP-8. `100d51119` escaped CP-8's filter pipes and set its After group to
CP-1. CP-8 has no baseline run (`ls 5a6a/rows` lists CP-1 to CP-7).

Receipt qualification is in the Code evidence Runs table:
- 9b83 kept its outputs, and both validators gave `cleanup_refused`.
- CP-4 in 999a gave `row_failed` (`skipped=4`). CP-8 therefore selects the four Linux
  methods of the same class by name.
- 1b4b ended RED exit=2 `output-ledger-mismatch` on the `RerunFilterHostTests` host
  output (CARD-1272).
- Run f4fc (CP-1 to CP-3, CP-5, CP-6 and CP-8 at `100d5111`) gave
  `CHECKPOINT SOURCE VALID rows=6`.

CP-4 remains the full-class witness. CP-7 remains the rerun-host witness, outside the receipt.

### Cost

About 15 minutes per run of driver time (one build plus seven short rows), two runs,
within the 90-minute task budget. No whole-Unit lane: the brief excludes it, the
change is bounded to fixtures (or a named production seam) of the four affected
classes, and the adjacent classes in R-1 cover the shared rerun/scheduler/deadline
paths. Each proof selection runs at most three normal times; none after green.

Actual: five runs (5a6a, 9b83, 999a, 1b4b, f4fc). Each run's `wall:` is in its `report.md`
and in the Code evidence Runs table. The three-run, none-after-green rule was exceeded. The
Code evidence Repetitions table lists the runs that included each row.

### Mutation

PC IDs are assigned per repaired witness in the Code evidence. They are pending
method-scoped SourceLanding Mutation.
