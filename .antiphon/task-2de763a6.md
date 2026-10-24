Fixed the remaining Linux watchdog fixture residue; the final five-class run passed 485/485 with zero skips.

## Change and attribution

Base: `ca250de0b48ca222cacdbaa3a3793ac0993c521f` (also fetched origin/master at final verification).
Verified code commit: `e7eddf58af7945f858bab77a3b7a49364f734c96`.
Branch: `feat/card-task-2de763a6`.

Only `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs` changed. The test
`a_task_waiting_on_a_dead_subagent_is_swept_after_the_subagent_grace` now calls the existing
`RetireSeededTaskAsync(task)` helper in `finally`. Every existing assertion is retained, including
the later watchdog test's global `stopper.Killed.ShouldBeEmpty()` assertion. No product code,
timeouts, test selections, schemas or sibling group-26 classes changed.

The baseline failure was
`LaunchResumedAt_two_minutes_ago_is_inside_the_watchdog_window`: its own task correctly remained
Dispatched, but the global watchdog killed a different session. A read-only query of the running
test database identified that session as the earlier dead-subagent fixture, with its fan-out
transcript and still-Pending report nudge. The fixture deliberately leaves an overdue Dispatched
task to prove that an undelivered nudge cannot settle it, then previously left that task available
to later global sweeps. Its cleanup belongs after those assertions, including when they fail.

This reproduced at the assigned base both in the full five-class selection and with only the
producer and observer tests selected: **1 passed / 1 failed**. The unchanged observer passes in
the final full selection after cleanup. This is shared-database fixture residue, not evidence
that the production LaunchResumedAt grace window is wrong.

Cluster classification:

- **Missing observation:** the old Windows candidate `a97194ee7a4a` predates the landed shared-helper
  wiring in reply terminal attachment, check-note recipient/interpreter attachment, crash-worker
  attachment and cleanup-retry callers. These paths already use `ClaudeDialogFixture` at the
  assigned base and passed on Linux; no further copies or helper edits were needed.
- **Old-generation observation:** the landed restart fixture already rebinds the interpreter
  observation after its resumed generation is persisted. The automatic-restart case passed in
  both Linux selections.
- **Fixture residue:** one remaining failure reproduced and was repaired here, as above.
- **Stale screen format / product defect / load-induced:** none reproduced in this selection.
  Existing master also contains `48f5f1b1a` (ordinary guarded clear-composer recovery), so success
  at this newer source does not isolate every historical Windows failure to a fixture edit.
  Exact attribution of the unavailable Windows failures remains unverified.

## Verification

Counts are passed / failed / skipped, extracted from fresh TRX and checked against phase case IDs.

| Class | Base ca250de0b | Fixed e7eddf58a |
|---|---:|---:|
| AgentTaskReplyIntegrationTests | 341 / 0 / 0 | 341 / 0 / 0 |
| CheckNoteDeliveryHandoffTests | 19 / 0 / 0 | 19 / 0 / 0 |
| AgentTaskDeliveryWatchdogTests | 85 / 1 / 0 | 86 / 0 / 0 |
| VerificationRoundDeliveryTests | 20 / 0 / 0 | 20 / 0 / 0 |
| WorktreeLandingCleanupRetryTests | 19 / 0 / 0 | 19 / 0 / 0 |
| Total | 484 / 1 / 0 | 485 / 0 / 0 |

The baseline and fixed runs executed identical 485-case rosters: no additions or omissions,
one Failed-to-Passed outcome, and no Passed-to-Failed outcomes. Both original test assertions
and the five assertions in the repaired producer remain intact.

All drivers ran under `scripts/build-slot.ps1`, with granted slots, zero admission wait and
maxcpucount 6. Base build: **4m11.36s**, 0 errors / 1197 warnings. Fixed build: **2m11.09s**,
0 errors / 1197 warnings. The warnings include existing analyzer warnings.
Base tests: **9m09.538s** (driver slot held 552s), exit 2. Red pair: **34.252s** (slot held 41s),
exit 2 with the expected assertion failure. Fixed tests: **8m52.933s** (slot held 537s), exit 0.
No aborted, zero-count or build-failure run is credited as a test witness.

Builds had 600-second hard caps, full selections 900 seconds, and the two-test witness 180 seconds.
These are provisional driver bounds using the observed local durations; no test timeout was widened.
This task finished within its 60-minute budget. No full-suite, Unit, native Windows, release or tag
operation was run. Every owned driver and child was awaited before cleanup.

Host: Linux container `0c18bb7f591e`, Debian 12, .NET 9.0.21, 24 logical CPUs. A mid-baseline
load sample was 20.15 / 15.15 / 10.91. Per-test duration and available phase/external-call
instrumentation were retained for both full runs. Instrumentation does not cover every external
boundary; unavailable phases remain unknown and unattributed time remains remainder.

Exact build command, with separate labels/logs for base and fixed source:

```sh
timeout 600 pwsh -NoProfile -File scripts/build-slot.ps1 -Label group25-watchdog-fix-build -SlotWaitMinutes 5 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-g25/ --nologo
```

Exact full selection (each OR operand has its own trailing wildcard):

```sh
timeout 900 pwsh -NoProfile -File scripts/build-slot.ps1 -Label group25-fixed-tests -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-g25/ -- --treenode-filter '/*/Antiphon.Tests.Application/(AgentTaskReplyIntegrationTests*)|(CheckNoteDeliveryHandoffTests*)|(AgentTaskDeliveryWatchdogTests*)|(VerificationRoundDeliveryTests*)|(WorktreeLandingCleanupRetryTests*)/*' --report-trx --report-trx-filename fix.trx --results-directory /work/worktrees/task-2de763a6/.antiphon/evidence-2de763a6/fix
```

The baseline used the identical selection, with base.trx and a separate results directory.
The red pair used this method selection with the same compiled baseline:

```text
/*/*/AgentTaskDeliveryWatchdogTests/(a_task_waiting_on_a_dead_subagent_is_swept_after_the_subagent_grace*)|(LaunchResumedAt_two_minutes_ago_is_inside_the_watchdog_window*)
```

## Native rows still owed

Rerun the following rows on native Windows at the eventual landed/picked source, under its host
build-slot gate and an isolated output directory. This task makes no Windows qualification claim.
The supplied Windows total was 524 versus 485 locally; its case roster is unavailable, so the
39-case difference is unresolved and must be reconciled from fresh native receipts.

| Row | Exact filter |
|---|---|
| G25-1 | `/*/*/AgentTaskReplyIntegrationTests/*` |
| G25-2 | `/*/*/CheckNoteDeliveryHandoffTests/*` |
| G25-3 | `/*/*/AgentTaskDeliveryWatchdogTests/*` |
| G25-4 | `/*/*/VerificationRoundDeliveryTests/*` |
| G25-5 | `/*/*/WorktreeLandingCleanupRetryTests/*` |

## Evidence and landing

Raw generated evidence remains ignored at
`/work/worktrees/task-2de763a6/.antiphon/evidence-2de763a6/`: build and test logs, base/base.trx,
red-pair/red-pair.trx, fix/fix.trx, timing-base and timing-fix case sidecars, source/assembly-hash
provenance, roster-comparison.json, watchdog-residue.txt and cleanup.json. Essential outcomes and
provenance are preserved in this committed report; raw generated files are not committed.
All **33 inventoried task-owned bin-g25 directories** were removed after the runs exited.

`git diff --check` passed. Evidence-history policy passed for base..code-tip (0 violations).
Fresh origin/master remained `ca250de0b48ca222cacdbaa3a3793ac0993c521f`.
`git merge-tree --write-tree origin/master e7eddf58af7945f858bab77a3b7a49364f734c96`
was clean, tree `12a5fbad9438f79fd94e981a2534287674b026d9`.
No rebase, reset, amend, branch switch, target merge or force-push occurred. No Review was run,
as explicitly directed by the operator brief. Landing does not provide native rc qualification.

--- next stage ---
next: land
handoff: Land the watchdog fixture cleanup; all 485 selected Linux cases pass. Native G25-1 through G25-5 remain owed.
artifact: .antiphon/task-2de763a6.md
