# CARD-0519 S12f — Code task ff6afff6

S12f did not qualify: the five closed checkpoint rows executed 53 results,
52 passed and one failed, with no skips. CP-72's unchanged-base rerun reproduced
the same classification failure (3 executed, 2 passed, 1 failed). No production,
test, configuration, assertion, timeout or plan changes were made.

Original Code task / landing owner: `ff6afff6-faba-4c46-8438-6a1942f7b506`.
Branch: `feat/card-task-ff6afff6`.
Worktree: `/work/worktrees/task-ff6afff6`.
Task base and actual tested source: `a1d014fc0ede59f21ddb573fb369e6abce85a700`.
The later Markdown-only report commit is not the tested SHA. Its full pushed SHA
and the full task-range evidence-guard result are in the final task response.

Plan: `docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md`,
section `S12 split selection (Plan 02c16198, 2026-10-05)` and the main
`### Checkpoints` table, as present at the tested source.

## Results and required repair

| Row | Coverage | Executed | Passed | Failed | Skipped | Row seconds |
|---|---|---:|---:|---:|---:|---:|
| CP-70 | V-13 idle main/trailing/machine | 3 | 3 | 0 | 0 | 568.5184405 |
| CP-77 | V-13 busy main/trailing/machine | 3 | 3 | 0 | 0 | 609.7541435 |
| CP-71 | V-13 converter/refusal/prompt/notice | 11 | 11 | 0 | 0 | 378.4910323 |
| CP-72 | Affected Unit classification/tripwire guards | 3 | 2 | 1 | 0 | 6.36656 |
| CP-73 | Four full CARD-1074 dispatch/resume classes | 33 | 33 | 0 | 0 | 52.1075559 |
| CP-72 rerun | Same exact guard row, unchanged base/build | 3 | 2 | 1 | 0 | See rerun TRX |

CP-73 exceeds its 31-result minimum because the current complete class rosters
contain 8 interrupted-launch, 15 dispatch-failure, 7 launch-ownership and 3 brief-
recovery results. All intended classes and methods were inspected in fresh TRX.
The 17 transport results cover the complete current transport class across three
manifest rows. Their recipient is fake Slack through the real isolated broker
and gateway; worker input uses the fixture protocol adapter and complete stored
UserPrompt receipts.

The sole failing method is
`Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata`.
Both executions reported exactly:

```text
missing classes: Antiphon.Tests.Application.TaskParkPublicationTests
missing-reason Antiphon.Tests.Application.CodexCliObservationGapTests
unregistered-marked Antiphon.Tests.Application.TaskParkPublicationTests
```

At the assigned base, `TaskParkPublicationTests` is marked Integration and Slow
but has no entry in `tests/Antiphon.Tests/slow-tests-allowlist.txt`.
`CodexCliObservationGapTests` is listed immediately after
`DirectoryLinkFixtureWindowsTests` without its own reason comment. These are
existing registry omissions, not a CARD-0519 delivery or converter failure.
Both the initial selection and the exact-row rerun ran the unchanged committed
task base, so no stash or additional baseline checkout was needed to establish
that this task inherited the failure. No broad baseline was run.

The plan explicitly says: "If any final row fails, the repair is a new Code slice
and the three groups rerun at the new frozen SHA." The caller should commission
that registry repair, retain the existing guard assertions and deadlines, and
requalify S12e/S12f/S12g at one resulting source SHA. This task consumed zero
source-repair rounds and one failure-driven exact-row rerun. It cannot hand off
ordinary verification as complete or proceed to a clean Review yet.

The existing S12e report at commit `58e4e2990` names tested source
`b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4`, whereas this brief assigns
`a1d014fc0ede59f21ddb573fb369e6abce85a700`. Do not relabel either receipt or infer
the plan's common-final-SHA activation gate from these two different candidates.

## Scope, V/R disposition and activation

Round: Final profile v1, with the brief's explicit S12f-only closed selection.
D-S12-7 assigns the whole Unit lane to caller-owned post-land qualification and
withdraws CP-60; no whole Unit, namespace, assembly or baseline sweep ran here.
Unit guards do not establish delivery, landing, lease or persistence behavior.
The bounded integration rows provide the assigned delivery and dispatch/resume
evidence. No unbounded shared source impact was introduced. The plan budgets
about 30 minutes for this group; the initial tool run took 28m42s.

| ID | Actual outcome in this task |
|---|---|
| V-1 | Not rerun; belongs to earlier schema slice. |
| V-2 | Not rerun; belongs to earlier capture slice. |
| V-3 | Not rerun; belongs to earlier materialization slice. |
| V-4 | Not rerun; belongs to earlier unified-path slice. |
| V-5 | Not rerun; belongs to earlier discovery slices. |
| V-6 | Not rerun; belongs to earlier trailing-recovery slice. |
| V-7 | Not rerun; belongs to earlier retry-policy slice. |
| V-8 | Not rerun; belongs to earlier failure-recording slice. |
| V-9 | Not rerun; belongs to earlier metadata-repair slice. |
| V-10 | Not rerun; belongs to earlier retention slice. |
| V-11 | Deferred to S12g CP-74; not claimed passed here. |
| V-12 | Deferred to S12g CP-75; not claimed passed here. |
| V-13 | Passed all 17 current results through CP-70/77/71 at the recorded SHA. |
| R-1 | Not rerun; retains earlier-slice evidence only. |
| R-2 | Not rerun; retains earlier-slice evidence only. |
| R-3 | Not rerun; retains earlier-slice evidence only. |
| R-4 | Not rerun; retains earlier-slice evidence only. |
| R-5 | S12e CP-67, not rerun or credited at this SHA. |
| R-6 | Not rerun; retains earlier-slice evidence only. |
| R-7 | Not rerun; retains earlier-slice evidence only. |
| R-8 | S12e CP-61/69, not rerun or credited at this SHA. |
| R-9 | S12e CP-68, not rerun or credited at this SHA. |
| R-10 | Not rerun; retains earlier-slice evidence only. |
| R-11 | Not rerun; retains earlier-slice evidence only. |
| R-12 | Deferred to S13 CP-29 on Windows; not claimed passed here. |

Also pending: CP-72 repair/green, common-candidate qualification of all final
groups, S13 documentation, caller-owned whole Unit qualification, ordinary Review
and post-land SourceLanding Mutation. Earlier V/R receipts are not reissued by
this report. No live manual activation is assigned to S12f. The automated manual-
recovery actions inside the transport cases completed with their original oracles.

`ChannelOutboundSettings.UnifiedRecoveryEnabled` retains default false, and no
configuration was changed. Restart: **none**. The caller/orchestrator owns later
server activation after all D-S12-8 gates; this task requests no runner restart.

## Execution and provenance

Read `GET /api/runner-defaults` and `GET /api/session-runners` before execution.
Defaults revision 2 named server2; the catalogue included desktop and both Linux
entries, with server2 draining and server2-temp accepting work. No fleet location,
Runner pin or Platform pin was embedded in commands.

Only one additional build ran: the required checkpoint-tool bootstrap, through
the host build-slot gate. It passed with zero errors and one CS8602 warning in
existing `TaskOwnerGuard.cs:170`. Its lease had `slot=granted waited=0s`,
maxcpucount=6, held 5s.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c519-s12f-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-ff6afff6-tool/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-ff6afff6-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md --after S12f --expected-source-sha a1d014fc0ede59f21ddb573fb369e6abce85a700 --row-timeout 15m --total-timeout 60m --serial --max-wait 50s
dotnet tools/Antiphon.Checkpoints/bin-ff6afff6-tool/Antiphon.Checkpoints.dll wait --run 20261006-015648-1dc4 --max-wait 50s
```

Wait continued after exit 75 until terminal exit 1. The executor completed and
was confirmed absent before cleanup. The initial run used one isolated build
(`bin-c519-cp70/`, 106.746939s) and five serial rows with
`TUNIT_MAX_PARALLEL_TESTS=1`. Every build/row lease reported
`slot=granted waited=0s`. The report's 64 build entries are imported manifest
entries; 63 are unused, and only the CP-70 build actually ran.

The mandatory red-row rerun reused the same certified build and source. It was
failure-driven confirmation, not an extra green proof repetition or a baseline
sweep. It exited 1 with the same error and `slot=granted waited=0s`:

```sh
TUNIT_MAX_PARALLEL_TESTS=1 dotnet tools/Antiphon.Checkpoints/bin-ff6afff6-tool/Antiphon.Checkpoints.dll row --name CP-72 --project tests/Antiphon.Tests --output-path bin-c519-cp70/ --no-build --filter '/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/*' --min-executed 3 --expect TestClassificationGuardTests,SlowTestTripwireTests --expected-source-sha a1d014fc0ede59f21ddb573fb369e6abce85a700 --results-root .antiphon/checkpoints/ff6afff6-rerun
```

Independent receipt validation:

```sh
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261006-015648-1dc4/report.json -ExpectedSourceSha a1d014fc0ede59f21ddb573fb369e6abce85a700 -Rows CP-70,CP-77,CP-71,CP-73
# exit 0: CHECKPOINT SOURCE VALID source=a1d014fc0ede59f21ddb573fb369e6abce85a700 rows=4
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261006-015648-1dc4/report.json -ExpectedSourceSha a1d014fc0ede59f21ddb573fb369e6abce85a700 -Rows CP-72
# exit 2: CHECKPOINT SOURCE INVALID reason=row_failed
```

All initial CHECKPOINT lines state `dirty=0 sourceState=clean buildSource=verified`
with the exact expected SHA. CP-72 has clean source provenance but failed tests;
it is not a clean passing certificate. The structured report and fresh TRX remain
ignored in `.antiphon/checkpoints/20261006-015648-1dc4/`, with the rerun under
`.antiphon/checkpoints/ff6afff6-rerun/CP-72-20261006-022545-43a2/`.
Generated receipts/logs/TRX were not staged. Only this individual Markdown report
is committed. All 28 owned `bin-c519-cp70` directories and the one checkpoint-tool
bootstrap output were removed after the foreground commands finished.

Full-range evidence guard command after committing this report:

```sh
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef a1d014fc0ede59f21ddb573fb369e6abce85a700 -HeadRef HEAD
```

Its actual outcome and the verified remote branch tip are reported in the final
task response; test receipts remain bound to the earlier source SHA.

## Pending positive controls

Every PC and every variant remains pending method-scoped SourceLanding Mutation:
PC-1..PC-100 (including all path/cut variants, especially PC-89..PC-96),
PC-1059-1..PC-1059-6; PC-S4-1..PC-S4-4 and PC-19/22/23's named S4 variants;
PC-S5-1..PC-S5-4; PC-S6-1..PC-S6-5 (all three independent PC-S6-2 variants);
PC-S7-1 and PC-45's owner/version/expiry/state variants; PC-S8-1..PC-S8-2;
PC-S9-1's reset-cursor and maximum-pages variants; PC-S10-1a/b/c/d/e,
PC-S10-2a/b/c, PC-S10-3a/b/c and PC-84's root/machine variants;
PC-S12-1, PC-S12-2, PC-S12-3, PC-S12-4 and PC-S12-5.

S12-specific variants remain distinct: converter idle/busy and each crash cut;
existing brief by ExecutionTaskId, rules SourceTaskId plus marker, transcript-only
receipt and stale native timestamp; launch refusal survival and subsequent brief;
PC-S12-5 separate idle and busy method cycles across main/trailing/machine and
both gateway timings. No deliberate mutants, red/restore/green cycles or missing-
control discovery were performed here. Ordinary green does not discharge a PC.

## Unedited CHECKPOINT lines and inspected TRX roster

CP-72 rerun (reruns=1; original line preserved without adding tokens):

```text
CHECKPOINT CP-72 commit=a1d014fc0ede59f21ddb573fb369e6abce85a700 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-ff6afff6/.antiphon/checkpoints/ff6afff6-rerun/CP-72-20261006-022545-43a2/run.trx slot=granted waited=0s dirty=0 source=a1d014fc0ede59f21ddb573fb369e6abce85a700 sourceState=clean buildSource=verified
```

The rerun TRX contains the same three exact methods listed under CP-72 below:
Registry_matches_compiled_metadata failed, and both SlowTestTripwireTests methods
passed. Initial run lines follow, unedited:
```text
CHECKPOINT CP-70 commit=a1d014fc0ede59f21ddb573fb369e6abce85a700 build=ok filter=/*/*/ChannelOutboundUnifiedTransportTests/C519_Queue_to_adapter* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ff6afff6/.antiphon/checkpoints/20261006-015648-1dc4/rows/CP-70/run.trx slot=granted waited=0s dirty=0 source=a1d014fc0ede59f21ddb573fb369e6abce85a700 sourceState=clean buildSource=verified
CHECKPOINT CP-77 commit=a1d014fc0ede59f21ddb573fb369e6abce85a700 build=reused filter=/*/*/ChannelOutboundUnifiedTransportTests/C519_Queue_to_busy_adapter* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ff6afff6/.antiphon/checkpoints/20261006-015648-1dc4/rows/CP-77/run.trx slot=granted waited=0s dirty=0 source=a1d014fc0ede59f21ddb573fb369e6abce85a700 sourceState=clean buildSource=verified
CHECKPOINT CP-71 commit=a1d014fc0ede59f21ddb573fb369e6abce85a700 build=reused filter=/*/*/ChannelOutboundUnifiedTransportTests/(C519_Size_refusal*)|(C519_Converter_handoff*)|(C519_Partial_prompt_requires_complete_receipt*)|(C519_Enabled_loss_notice_reaches_adapter*) executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-ff6afff6/.antiphon/checkpoints/20261006-015648-1dc4/rows/CP-71/run.trx slot=granted waited=0s dirty=0 source=a1d014fc0ede59f21ddb573fb369e6abce85a700 sourceState=clean buildSource=verified
CHECKPOINT CP-72 commit=a1d014fc0ede59f21ddb573fb369e6abce85a700 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-ff6afff6/.antiphon/checkpoints/20261006-015648-1dc4/rows/CP-72/run.trx slot=granted waited=0s dirty=0 source=a1d014fc0ede59f21ddb573fb369e6abce85a700 sourceState=clean buildSource=verified
CHECKPOINT CP-73 commit=a1d014fc0ede59f21ddb573fb369e6abce85a700 build=reused filter=/*/*/(AgentSessionInterruptedLaunchResumeTests*)|(AgentTaskDispatchFailureTests*)|(AgentSessionLaunchQueueOwnershipTests*)|(DelegationBriefRecoveryTests*)/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-ff6afff6/.antiphon/checkpoints/20261006-015648-1dc4/rows/CP-73/run.trx slot=granted waited=0s dirty=0 source=a1d014fc0ede59f21ddb573fb369e6abce85a700 sourceState=clean buildSource=verified
```

### CP-70

- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Queue_to_adapter — C519_Queue_to_adapter(main): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Queue_to_adapter — C519_Queue_to_adapter(trailing): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Queue_to_adapter — C519_Queue_to_adapter(machine): Passed.

### CP-77

- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Queue_to_busy_adapter — C519_Queue_to_busy_adapter(main): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Queue_to_busy_adapter — C519_Queue_to_busy_adapter(trailing): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Queue_to_busy_adapter — C519_Queue_to_busy_adapter(machine): Passed.

### CP-71

- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Partial_prompt_requires_complete_receipt — C519_Partial_prompt_requires_complete_receipt: Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Enabled_loss_notice_reaches_adapter — C519_Enabled_loss_notice_reaches_adapter: Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Size_refusal — C519_Size_refusal: Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Converter_handoff — C519_Converter_handoff(False, conversion-task-committed): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Converter_handoff — C519_Converter_handoff(True, conversion-task-committed): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Converter_handoff — C519_Converter_handoff(False, conversion-dispatched): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Converter_handoff — C519_Converter_handoff(True, conversion-dispatched): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Converter_handoff — C519_Converter_handoff(False, enqueue-refused): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Converter_handoff — C519_Converter_handoff(True, enqueue-refused): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Converter_handoff — C519_Converter_handoff(False, result-committed): Passed.
- Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Converter_handoff — C519_Converter_handoff(True, result-committed): Passed.

### CP-72

- Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata — Registry_matches_compiled_metadata: Failed.
- Antiphon.Tests.TestHelpers.SlowTestTripwireTests.allowlist_file_exists_and_has_spawn_lane_entries — allowlist_file_exists_and_has_spawn_lane_entries: Passed.
- Antiphon.Tests.TestHelpers.SlowTestTripwireTests.unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not — unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not: Passed.

### CP-73

- Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests.Adapter_without_attach_takes_the_not_resumable_arm — Adapter_without_attach_takes_the_not_resumable_arm: Passed.
- Antiphon.Tests.Application.AgentSessionLaunchQueueOwnershipTests.Owns_is_true_from_enqueue_until_the_launch_settles — Owns_is_true_from_enqueue_until_the_launch_settles: Passed.
- Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests.Already_Running_row_is_a_noop — Already_Running_row_is_a_noop: Passed.
- Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests.Non_delegate_Starting_row_does_not_attach_and_kills_through_the_runner — Non_delegate_Starting_row_does_not_attach_and_kills_through_the_runner: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_gone_caller_disarms_the_reminder — a_gone_caller_disarms_the_reminder: Passed.
- Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests.Sign_in_block_persists_LaunchBlock — Sign_in_block_persists_LaunchBlock: Passed.
- Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests.Ready_false_fails_kills_the_adapter_and_the_agent — Ready_false_fails_kills_the_adapter_and_the_agent: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_dropped_note_disarms — a_dropped_note_disarms: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_read_disarms_the_reminder — a_read_disarms_the_reminder: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_status_poll_disarms_the_reminder — a_status_poll_disarms_the_reminder: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_pending_note_is_not_duplicated — a_pending_note_is_not_duplicated: Passed.
- Antiphon.Tests.Application.AgentSessionLaunchQueueOwnershipTests.ResumeInterrupted_registers_before_running_and_a_second_call_is_a_noop — ResumeInterrupted_registers_before_running_and_a_second_call_is_a_noop: Passed.
- Antiphon.Tests.Application.AgentSessionLaunchQueueOwnershipTests.A_faulted_resume_still_releases_ownership — A_faulted_resume_still_releases_ownership: Passed.
- Antiphon.Tests.Application.AgentSessionLaunchQueueOwnershipTests.C514_Failed_enqueue_cannot_drop_launch_responsibility — C514_Failed_enqueue_cannot_drop_launch_responsibility: Passed.
- Antiphon.Tests.Application.DelegationBriefRecoveryTests.Interrupted_dispatch_backfills_the_missing_brief_on_resume — Interrupted_dispatch_backfills_the_missing_brief_on_resume: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_parked_failure_note_is_re_sent — a_parked_failure_note_is_re_sent: Passed.
- Antiphon.Tests.Application.AgentSessionLaunchQueueOwnershipTests.C514_Two_recovery_workers_resume_one_original_work — C514_Two_recovery_workers_resume_one_original_work: Passed.
- Antiphon.Tests.Application.AgentSessionLaunchQueueOwnershipTests.C514_Old_deferred_work_never_targets_replacement — C514_Old_deferred_work_never_targets_replacement: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_dispatch_failure_arms_a_reminder — a_dispatch_failure_arms_a_reminder: Passed.
- Antiphon.Tests.Application.AgentSessionLaunchQueueOwnershipTests.Queued_launch_carries_the_explicit_accepted_generation_and_never_re_reads_a_replaced_row — Queued_launch_carries_the_explicit_accepted_generation_and_never_re_reads_a_replaced_row: Passed.
- Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests.Delegate_Starting_row_attaches_becomes_Running_and_flushes_the_pending_brief — Delegate_Starting_row_attaches_becomes_Running_and_flushes_the_pending_brief: Passed.
- Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests.Interrupted_Grok_start_recovers_committed_runner_receipt_once_before_work — Interrupted_Grok_start_recovers_committed_runner_receipt_once_before_work(True): Passed.
- Antiphon.Tests.Application.DelegationBriefRecoveryTests.Resume_never_duplicates_an_existing_brief — Resume_never_duplicates_an_existing_brief: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_git_timeout_fails_one_task_not_the_tick — a_git_timeout_fails_one_task_not_the_tick: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_dispatch_that_throws_before_a_session_exists_tells_the_caller — a_dispatch_that_throws_before_a_session_exists_tells_the_caller: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.C467_V14_LandReceiptCannotDisarmMissingReportReminder — C467_V14_LandReceiptCannotDisarmMissingReportReminder(False): Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_reminder_is_never_armed_for_a_board_only_task — a_reminder_is_never_armed_for_a_board_only_task: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.C467_V14_LandReceiptCannotDisarmMissingReportReminder — C467_V14_LandReceiptCannotDisarmMissingReportReminder(True): Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.the_reminder_budget_ends_and_says_so — the_reminder_budget_ends_and_says_so: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_lost_failure_note_is_re_sent_on_the_ramp — a_lost_failure_note_is_re_sent_on_the_ramp: Passed.
- Antiphon.Tests.Application.AgentTaskDispatchFailureTests.a_sent_note_disarms_the_reminder — a_sent_note_disarms_the_reminder: Passed.
- Antiphon.Tests.Application.DelegationBriefRecoveryTests.Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched — Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched: Passed.
- Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests.Interrupted_Grok_start_recovers_committed_runner_receipt_once_before_work — Interrupted_Grok_start_recovers_committed_runner_receipt_once_before_work(False): Passed.
