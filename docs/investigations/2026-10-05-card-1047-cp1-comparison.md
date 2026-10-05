# CARD-1047 S1: CP-1 source comparison

Task `2ba71f6e`, original Code landing owner
`034f38be-9e2a-42fa-996a-ad7c8401dd35`. Assigned worktree:
`C:\Antiphon\worktrees\card-task-2ba71f6e`; branch `feat/card-task-2ba71f6e`.
Task base: `4913f2b79d9cbd868e7951f962d86e0bcaf5732f`.
Plan: [Windows harness plan](../superpowers/plans/2026-10-04-card-1047-windows-script-harness-plan.md).

The earlier source passed CP-1 twice, and the task base passed the comparison
rerun. No source repair or revert is justified by these observations. Keep the
later commit's emergency sweep: its regression has not been established. The
previous red results remain real observations, not proof of an inherited defect
or a resolved flake. Qualification of the final documentation commit is pending
at this document's authoring boundary; its fresh evidence belongs in the task
report, not an amendment that changes the tested SHA.

The prior report was absent from the committed tree despite the brief's stated
location. It was read in full at
`C:\Antiphon\worktrees\card-task-47b86ea8\.antiphon\task-47b86ea8.md`.
It records earlier-source CP-1 14/14 and final-source CP-1 12/14, with N11 native
readiness timeout (11.6241602s) and W1 execution/cleanup timeout plus emergency
native error 5 (12.1319191s). Those are prior-task observations, not new runs here.

## What the source diff actually changes

`5dfeec42725a9166e4017e4b278d60a42258fa7b..4913f2b79d9cbd868e7951f962d86e0bcaf5732f`
changes only `ScriptHarnessProcessFixture.ObservedTree.AssertDeadBeforeEmergencySweep`:
it restores `EmergencyStop` in a finally and aggregates assertion/sweep failures.
It changes no native launch, handle snapshot/wait ordering, readiness deadline,
execution/cleanup budget, assertion or filter. W1's Passing invocation does not
establish a Tree, so it does not call this changed helper. N11 calls it after
readiness and cleanup assertions; its reported failure happened before readiness.
There is no demonstrated direct path from this change to either prior failure.
Indirect ambient timing is possible, but these measurements do not establish it.

No new tests or deliberate mutants were authored. Existing native outcome tests
and their assertions were retained unchanged. Linux/shared coordinator behavior
was not changed. No assertion, deadline, filter or native budget was loosened.

## Observed load and fresh results

Read GET `/api/runner-defaults` and `/api/session-runners`: revision 2, eligible
Windows and Linux lanes plus an unavailable descriptor. Execution used the
assigned Windows worktree, no host pin, and regular installed PowerShell 7.
Load snapshots are ignored JSON under `.antiphon/` in this worktree.

| Run | Actual source SHA | CPU at start | Available memory | Competing Antiphon.Tests | Result |
|---|---|---:|---:|---:|---|
| Earlier 1 | `5dfeec42725a9166e4017e4b278d60a42258fa7b` | 62% | 11,561,536 KiB | 0 | 14 passed, 0 failed/skipped |
| Earlier 2 | same earlier SHA | 70% | 10,836,568 KiB | 0 | 14 passed, 0 failed/skipped |
| Current-tip comparison | `4913f2b79d9cbd868e7951f962d86e0bcaf5732f` | 73% | 12,109,224 KiB | 0 | 14 passed, 0 failed/skipped |

Each start saw three existing dotnet processes, PIDs 13148/16056/31948, started
before this task. Their cumulative CPU was about 9/24/18 seconds. These snapshots
are ambient load observations, not controlled stress or proof that CPU caused red.
No competing test process was killed. Two earlier-source executions exhaust this
brief's requested earlier-source comparison; there is no open-ended repeat loop.

One admitted bootstrap used `scripts/build-slot.ps1`, `bin-c1047-tool/`,
slot granted, waited=0s, held=5s, build 4.13s, exit 0; one existing CS8602 warning.
No other administrative build was run. The two build-bearing CP-1 runs used the
checkpoint tool's plan run, serial, row timeout 10m, total timeout 20m, expected
SHA equal to committed HEAD, with every exit-75 wait awaited to terminal.

Earlier 2 used the tool's `row --no-build`, the same exact CP-1 filter and output,
Min=14, both class Expect tokens, expected committed SHA. Its owned foreground
wrapper capped the entire invocation at 10m including slot wait (stricter than
20m total). This admitted repeat avoided another graph build. That verb emits
the source-qualified CHECKPOINT line but does not persist a structured source
receipt. An attempted validator call to its nonexistent source.json failed; no
validator-approved certificate is claimed for that repeat. Its fresh TRX is real
execution evidence. The two plan-run report.json validators returned SOURCE VALID.

Unedited CHECKPOINT lines:

```text
CHECKPOINT CP-1 commit=5dfeec42725a9166e4017e4b278d60a42258fa7b build=ok filter=/*/*/(ScriptHarnessProcessTests*)|(ScriptHarnessWindowsOwnershipTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-2ba71f6e\.antiphon\checkpoints\20261005-030121-a224\rows\CP-1\run.trx slot=granted waited=0s dirty=0 source=5dfeec42725a9166e4017e4b278d60a42258fa7b sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=5dfeec42725a9166e4017e4b278d60a42258fa7b build=reused filter=/*/*/(ScriptHarnessProcessTests*)|(ScriptHarnessWindowsOwnershipTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-2ba71f6e\.antiphon\earlier-repeat\CP-1-20261005-030446-9489\run.trx slot=granted waited=0s dirty=0 source=5dfeec42725a9166e4017e4b278d60a42258fa7b sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=4913f2b79d9cbd868e7951f962d86e0bcaf5732f build=ok filter=/*/*/(ScriptHarnessProcessTests*)|(ScriptHarnessWindowsOwnershipTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-2ba71f6e\.antiphon\checkpoints\20261005-030606-ecac\rows\CP-1\run.trx slot=granted waited=0s dirty=0 source=4913f2b79d9cbd868e7951f962d86e0bcaf5732f sourceState=clean buildSource=verified
```

Earlier 1 wall 156.766243s, build 98.2687765s, test wall 40.9922489s.
Current comparison wall about 156s, build 95.582861s, test wall 41.810069s.
All build and row slots granted, waited=0s. Source stayed frozen through each run.
The current-tip rerun was necessary to test the regression hypothesis before
discarding the shared fixture's safety repair; it was the same named CP-1 row.

All fresh TRX files must be independently compared against the exact roster below.
Earlier 1/2 audits found actual=14, missing=0, extra=0, each method Passed. The
task report records the comparison/final-tip audits and actual outcomes.

| ID | Method |
|---|---|
| N1 | ScriptHarnessProcessTests.Live_root_timeout_kills_root_child_and_grandchild |
| N2 | ScriptHarnessProcessTests.Exited_root_with_stdout_holder_times_out_and_kills_descendant |
| N3 | ScriptHarnessProcessTests.Exited_root_with_stderr_holder_times_out_and_kills_descendant |
| N4 | ScriptHarnessProcessTests.Caller_cancellation_kills_tree_before_returning |
| N5 | ScriptHarnessProcessTests.Passing_case_preserves_inventory_and_argument_boundaries |
| N6 | ScriptHarnessProcessTests.Nonzero_exit_preserves_output_and_cleans_results |
| N7 | ScriptHarnessProcessTests.Bad_pass_inventory_still_fails |
| N8 | ScriptHarnessProcessTests.Completed_root_with_silent_descendant_releases_owner |
| N9 | ScriptHarnessProcessTests.Root_exit_racing_deadline_still_cleans_owner |
| N10 | ScriptHarnessProcessTests.High_volume_on_both_streams_completes |
| N11 | ScriptHarnessProcessTests.Repeated_timeouts_do_not_leak_owners |
| W1 | ScriptHarnessWindowsOwnershipTests.Child_is_assigned_before_first_instruction |
| W2 | ScriptHarnessWindowsOwnershipTests.Assignment_failure_never_resumes_child |
| W3 | ScriptHarnessWindowsOwnershipTests.Resume_failure_terminates_suspended_child |

## Commission boundary

The explicit repair brief overrides its generic Final profile: CP-1 only, no whole
Unit lane or assembly. S2/S3, CP-2 and CP-4..CP-7 stay deferred, not passed.
V-1/R-1 have green W1-W3 observations, W9 deferred. V-3 has all N1-N11 green.
R-2 has the N subset green, W4/W5/W10 deferred. R-3 has N2/N3/N8/N10 green,
W6-W8 deferred. V-2, V-4, V-5 and the final R-4 qualification remain deferred.
CP-1's exact-roster/source audit is complete for the two receipt-bearing source
observations, not a claim about final 32/21/24/18 coverage. Manual observations
within green methods include real readiness, retained death handles, separate
EOF, stop-before-dispose and no emergency rescue; deferred W methods earn no credit.

All SourceLanding Mutation controls remain pending: PC-1, PC-2, PC-3, PC-4,
PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-11, PC-12, PC-13 (alias/reparse arms),
PC-14 (active-member arm), PC-15 (query-failure arm), PC-16, PC-17, PC-18,
PC-19, PC-20. No controls are discharged by ordinary green. No new variants.
Restart: none; caller/original landing owner owns any later activation.
