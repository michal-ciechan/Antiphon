# CARD-0965 continuation Code report

TIMEBOXED PARTIAL: 85/86 final integration results passed. V-22 remains pending: the minimal HTTP host logs RequestDelegate metadata that JSON cannot serialize. The explicit delegate metadata converter is now added, but its compile/full CP-2 rerun and fresh published-tip qualification are NOT RUN. Unit and CP-7 completed. next: code; this supersedes the provisional next: review block below.

V-16 now covers SendNow, idle flush and immediate persisted enqueue; V-29 uses a
controlled clock advanced one minute and observes database/input effects. Five
ordinary strength defects failed assertions. Production bytes are restored.
The whole Unit lane passed 3910 results with 52 inherited environmental skips;
the two full compatibility classes passed 18. Published-tip qualification follows
this report's commit and its unedited receipts are in the authoritative task Result.
V-22's newly isolated HTTP harness needs the final CP-2 repair rerun before green.

Current original Code / landing owner: 4c1697dc-d944-46ad-ae9e-085276be1663.
Previous partial owner: b630f3bf-9528-4a6c-a669-903d56c66361; its commits are included.
Branch: feat/card-task-4c1697dc. Worktree: /work/worktrees/task-4c1697dc.
Desktop mirror: C:\Antiphon\worktrees\card-task-4c1697dc (not accessed).
Start: 02e97b63c48ab170715d2d0edbed83f90ae74405. FF-only commits; no fetch/rebase/merge.
Restart: none. No production change, land or deployment.
Final pushed/qualified full SHA is supplied by the task Result and ls-remote.

Plan: docs/superpowers/plans/2026-10-03-card-0965-remaining-input-proofs.md.
Qualification: docs/superpowers/plans/2026-10-03-card-0965-final-qualification.md.
Previous evidence: .antiphon/checkpoints/20261003-105339-6074/ and .antiphon/task-b630f3bf.md.
Continuation diagnostics: .antiphon/c965-continuation/.
Unit/compatibility: .antiphon/checkpoints/20261003-115340-1393/.
HTTP-harness repair run: .antiphon/checkpoints/20261003-121010-50d2/.

## Changes and witnesses

- AgentTaskInputFallbackTests: three parameterized fresh-database V-16 cases use
  the real queue/client/WebSocket/dispatcher/writer with a file blocking .antiphon.
  They observe the before-input refusal, zero file/runtime input, same persisted
  row/key, API-only replay, one Warning and authenticated whole-body read.
  Immediate enqueue's SaveChanges interceptor supplies an admitted task/event key
  and records its original row ID. This is a synthetic internal ownership setup;
  the ordinary overlay API intentionally has no task-input key.
- Readiness: a completion callback on the real endpoint's accepted-connection log
  calls MarkRecovered for the synthetic empty runner; the injected eligibility
  observer completes only for a live dispatch-eligible connection. Store identity
  is checked before starting the scripted peer. This is not production recovery
  worker coverage. No polling helper or real-time sleep predicate remains.
- TaskInputReadFailureTests: fixed poll clock advances one minute; independent
  complaint timestamp and owning IDs are pinned; stable key/time/IDs are compared;
  actual command interception and runner-call interception require zero effects.
  Fresh task/session/queue snapshots and event counts stay unchanged.
- HTTP proofs mount the actual production task-detail/input delegates on a random
  loopback listener, not Program. Other endpoint delegates are not exposed; their
  metadata service placeholders throw if resolved. No production runner is used.
- Framework structured logs may contain MemberInfo values. Their explicit JSON
  converter renders reflection metadata as text. Every property remains present
  and both formatted and structured private-tail assertions remain unchanged.

No assertion was weakened/deleted and no timeout/retry was widened.

## Red evidence and deviations

The literal request for unchanged-production red-first V-16/V-29 was not met:
the implemented feature already passes these added witnesses. Baselines are
reported as green, not renamed red. The ordinary diagnostic defects prove the
guard strength. No dummy defect was committed to manufacture an initial red.

V-16 suppression: 3 assertion failures on Body retaining .antiphon/inbox/.
V-20 foreign-principal admission: 1 assertion failure (Forbidden vs OK).
V-22 body logging: 1 assertion failure on private-tail-888 in entry.Message.
V-29 poll-time episode: 1 assertion failure at condition-time-is-complaint-time
(10:05:00 vs independent transcript timestamp 10:01:41).
V-32 terminal inclusion: 1 assertion failure at terminal-input-condition-count=0
(actual 1), while the independent blocked condition remains.
These are ordinary strength diagnostics, not PC discharge. The preceding Code
task's ceiling, malformed-key and seeded-migration diagnostic reds remain credited;
CP-1/CP-7 qualification supplies their restored-green executions.

All five production files restored byte-for-byte; ledger is
.antiphon/c965-continuation/restoration.txt. git diff was empty after restoration;
timestamps were refreshed before clean rebuilds. Deliberate changes were never
committed. Each diagnostic receipt labels dirty=5 and is not clean Review evidence.

Unlisted runs: precise V-16/V-29 baselines and five precise strength checks are
brief-authorized diagnostics. The checkpoint tool was built as a dependency of
the leased diagnostic build; no separate bootstrap build ran. The first V-16
command had an invalid SHA argument and ran nothing. Its first actual run had
three readiness fixture timeouts (invalid red evidence); the next attempt failed
to compile a nullable Guid assertion. Both defects were corrected and pushed.
The corrected V-16 baseline passed 3 and V-29 baseline passed 1.

The first isolated-HTTP build was deliberately stopped/joined before tests after
finding a missing namespace; wait returned executor-died-without-phase=done. The
commit message says "after compile failure" but no completed compiler failure
receipt exists for that stopped attempt. It is incomplete, not red/green proof.
The next run: CP-2 28 passed/1 failed, caused by unsupported RuntimeMethodInfo
serialization in test log inspection; CP-3 8 passed. The converter repairs this
fixture exception; final qualification reruns the full red CP-2 row.

## Final scope and qualification

Unit: total 3962, executed/passed 3910, failed 0, skipped 52. Fresh TRX agrees.
33 are Windows-only (CARD-0922); 19 need jq (CARD-0927). Source envelope is clean:
dirty=0, sourceState=clean, buildSource=verified at 93b76ea56a640074b5df342fce6574b087691907.
CP-7: ParkedMessageSweepServiceTests 13 and CapacityRecoveryCompatibilityTests 5,
all 18 passed. Malformed_task_input_keys_stay_pending and
Input_body_upgrade_preserves_a_preexisting_event_with_null_body are present.
Strict receipt validation accepts CP-7; rejects CP-6 with row_failed because its
zero-skip policy rejects the inherited 52 skips. This is expected, not a blocker.

Unit ran once, before the later private Integration HTTP harness changes. No Unit
case/shared fixture changed after it. Final qualification covers every named full
affected integration class at published HEAD. It uses one isolated fresh build
and four exact original filters through documented same-group build reuse.
This is the brief's explicitly authorized final qualification; original completed
CP-1..4 were not gratuitously repeated. No full assembly or loaded repetition.
Selections remain within the normal repetition budget; genuine repairs/diagnostic
defects are identified separately. No routine rerun follows green.

Invariants: exact runner-owned input, atomic authorized fallback, body privacy,
stable read-only attention, strict sweep ownership and seeded migration survival.
No unbounded production class impact: only two Integration test classes and plan
documents changed. Unit misses delivery/landing/leases/persistence; named classes
cover this feature's bounded delivery/persistence and migration. No manual live
activation is required for this test-only change. Native Windows proof is outside
this dispatch. Final CP lines/roster are appended in the authoritative task Result
after this evidence commit, binding the final full SHA. There is no deferred-to-Final
ordinary scope once those four rows pass. All PCs remain pending.

## FOLLOW-UPS

Board searches "spill incident" (0) and "incident visibility" (CARD-0965, read)
confirm the incident decision already exists as item 5. No duplicate filed.
Keep ordinary spill delivery behavior; any consistent incident repair across the
three production branches is a separately commissioned Code task.
CARD-0960 is now Done and records the Windows fixture fix; its separate native
Windows confirmation is not claimed by this Linux task. All parent PC variants
remain PENDING for separately commissioned method-scoped SourceLanding Mutation.
After ordinary Review, the caller lands current Code owner 4c1697dc and commissions
SourceLanding Mutation. Restart none for this test-only repair.

## Unedited checkpoint receipts

CHECKPOINT CP-1 commit=95509608e53986b9ac49ae0023f2cf7b1631bca8 build=ok filter=/*/*/(AgentTaskInputSpillTests*)|(AgentTaskRefineTests*)|(AgentTaskReplyOverlayTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/checkpoints/20261003-105339-6074/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=95509608e53986b9ac49ae0023f2cf7b1631bca8 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=95509608e53986b9ac49ae0023f2cf7b1631bca8 build=ok filter=/*/*/(AgentTaskInputFallbackTests*)|(PhoneHomeSpillTests*)|(PhoneHomeSpillTransportTests*)|(DurableRunnerSpillReceiptTests*)/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/checkpoints/20261003-105339-6074/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=95509608e53986b9ac49ae0023f2cf7b1631bca8 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=95509608e53986b9ac49ae0023f2cf7b1631bca8 build=ok filter=/*/*/TaskInputReadFailureTests*/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/checkpoints/20261003-105339-6074/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=95509608e53986b9ac49ae0023f2cf7b1631bca8 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=95509608e53986b9ac49ae0023f2cf7b1631bca8 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=95509608e53986b9ac49ae0023f2cf7b1631bca8 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-6 commit=93b76ea56a640074b5df342fce6574b087691907 build=ok filter=/*/*/*/*[Category=Unit] executed=3910 passed=3910 failed=0 skipped=52 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-115340-1393/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=93b76ea56a640074b5df342fce6574b087691907 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=93b76ea56a640074b5df342fce6574b087691907 build=ok filter=/*/*/(ParkedMessageSweepServiceTests*)|(CapacityRecoveryCompatibilityTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-115340-1393/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=93b76ea56a640074b5df342fce6574b087691907 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=1462102cb4e88dea9c42223a554f8b9dd67ed0cf build=ok filter=/*/*/(AgentTaskInputFallbackTests*)|(PhoneHomeSpillTests*)|(PhoneHomeSpillTransportTests*)|(DurableRunnerSpillReceiptTests*)/* executed=29 passed=28 failed=1 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121010-50d2/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=1462102cb4e88dea9c42223a554f8b9dd67ed0cf sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=1462102cb4e88dea9c42223a554f8b9dd67ed0cf build=ok filter=/*/*/TaskInputReadFailureTests*/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121010-50d2/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=1462102cb4e88dea9c42223a554f8b9dd67ed0cf sourceState=clean buildSource=verified
CHECKPOINT V32-STRENGTH-RED commit=3211f53bece05a9f1af54063c9f0bb37f9bd63cb build=reused filter=/*/*/TaskInputReadFailureTests*/Terminal_task_removes_open_input_attention executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V32-STRENGTH-RED-20261003-115148-dbca/run.trx slot=granted waited=0s dirty=5 source=3211f53bece05a9f1af54063c9f0bb37f9bd63cb+dirty:8e09b9d1488ef55534d70f497edd2697fd309409c14cd52c35b230056e3b9dfe sourceState=dirty buildSource=verified
CHECKPOINT V16-STRENGTH-RED commit=3211f53bece05a9f1af54063c9f0bb37f9bd63cb build=ok filter=/*/*/AgentTaskInputFallbackTests*/Runner_write_failure_types_only_the_durable_api_pointer* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V16-STRENGTH-RED-20261003-114545-dd81/run.trx slot=granted waited=0s dirty=5 source=3211f53bece05a9f1af54063c9f0bb37f9bd63cb+dirty:8e09b9d1488ef55534d70f497edd2697fd309409c14cd52c35b230056e3b9dfe sourceState=dirty buildSource=verified
CHECKPOINT V22-STRENGTH-RED commit=3211f53bece05a9f1af54063c9f0bb37f9bd63cb build=reused filter=/*/*/AgentTaskInputFallbackTests*/Input_body_is_absent_from_task_summary_events_and_logs executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V22-STRENGTH-RED-20261003-114956-e0d0/run.trx slot=granted waited=0s dirty=5 source=3211f53bece05a9f1af54063c9f0bb37f9bd63cb+dirty:8e09b9d1488ef55534d70f497edd2697fd309409c14cd52c35b230056e3b9dfe sourceState=dirty buildSource=verified
CHECKPOINT V16-RED commit=5e8a9e08f2a4cb9d5485fd1881596c7c9c06199c build=ok filter=/*/*/AgentTaskInputFallbackTests*/Runner_write_failure_types_only_the_durable_api_pointer* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V16-RED-20261003-113342-bff6/run.trx slot=granted waited=0s dirty=0 source=5e8a9e08f2a4cb9d5485fd1881596c7c9c06199c sourceState=clean buildSource=verified
CHECKPOINT V29-STRENGTH-RED commit=3211f53bece05a9f1af54063c9f0bb37f9bd63cb build=reused filter=/*/*/TaskInputReadFailureTests*/Repeated_attention_reads_keep_one_stable_condition executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V29-STRENGTH-RED-20261003-115050-516c/run.trx slot=granted waited=0s dirty=5 source=3211f53bece05a9f1af54063c9f0bb37f9bd63cb+dirty:8e09b9d1488ef55534d70f497edd2697fd309409c14cd52c35b230056e3b9dfe sourceState=dirty buildSource=verified
CHECKPOINT V16-BASE commit=02687c74e8d54d7989c7ed992fe2d79e97d2bb6c build=failed filter=/*/*/AgentTaskInputFallbackTests*/Runner_write_failure_types_only_the_durable_api_pointer* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V16-BASE-20261003-113817-3437/run.trx slot=granted waited=0s dirty=0 source=02687c74e8d54d7989c7ed992fe2d79e97d2bb6c sourceState=clean buildSource=unknown
CHECKPOINT V29-BASE commit=6dd0ac62ed7e51d722fd2257f527afb5ea1e0e69 build=reused filter=/*/*/TaskInputReadFailureTests*/Repeated_attention_reads_keep_one_stable_condition executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V29-BASE-20261003-114254-410b/run.trx slot=granted waited=0s dirty=0 source=6dd0ac62ed7e51d722fd2257f527afb5ea1e0e69 sourceState=clean buildSource=verified
CHECKPOINT V16-BASE commit=6dd0ac62ed7e51d722fd2257f527afb5ea1e0e69 build=ok filter=/*/*/AgentTaskInputFallbackTests*/Runner_write_failure_types_only_the_durable_api_pointer* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V16-BASE-20261003-113957-1fca/run.trx slot=granted waited=0s dirty=0 source=6dd0ac62ed7e51d722fd2257f527afb5ea1e0e69 sourceState=clean buildSource=verified
CHECKPOINT V20-STRENGTH-RED commit=3211f53bece05a9f1af54063c9f0bb37f9bd63cb build=reused filter=/*/*/AgentTaskInputFallbackTests*/Input_endpoint_requires_the_recipient_task_token executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/c965-continuation/V20-STRENGTH-RED-20261003-114902-e5dd/run.trx slot=granted waited=0s dirty=5 source=3211f53bece05a9f1af54063c9f0bb37f9bd63cb+dirty:8e09b9d1488ef55534d70f497edd2697fd309409c14cd52c35b230056e3b9dfe sourceState=dirty buildSource=verified

## Every ordinary V/R ID

| V-1 | Passed : Runner_codex_refinement_writes_complete_body_before_pointer | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-2 | Passed : Runner_claude_refinement_uses_the_same_inbox_route | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-3 | Passed : Runner_grok_refinement_is_forced_to_a_join_safe_pointer | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-4 | Passed : Runner_ceiling_ignores_the_modern_desktop_profile | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-5 | Passed : Bound_pointer_measures_utf8_after_guid_path_rewrite | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-6 | Passed : Two_same_tick_refinements_keep_distinct_paths_and_bodies | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-7 | Passed : Local_desktop_refinement_keeps_its_file_and_pointer_contract | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-8 | Passed : Runner_blocked_reply_reaches_the_inbox_with_task_attribution | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-9 | Passed : Runner_small_reply_remains_inline | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-10 | Passed : Open_question_reply_remains_unmarked_now_and_toolresult_confirmed | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-11 | Passed : Missing_runner_cwd_selects_api_without_a_server_write | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-12 | Passed : Queued_refinement_still_amends_the_goal | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-13 | Passed : Fallback_input_body_survives_complete_prompt_and_restart | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-14 | Passed : Refinement_send_now_upgrades_the_same_owned_row | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-15 | Passed : Pending_refinement_age_remains_visible_without_delivery_attempts | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-16 | Passed : Runner_write_failure_types_only_the_durable_api_pointer | Three cases passed; diagnostic suppression red 3/3; minimal-host full-class rerun in final qualification. |
| V-17 | Passed : Runner_path_refusal_never_writes_outside_the_mirror | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-18 | Passed : Input_failure_after_write_never_selects_fallback | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-19 | Passed : Restart_after_fallback_commit_replays_the_same_api_pointer | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-20 | Passed : Input_endpoint_requires_the_recipient_task_token | Ordinary route passed; foreign-task diagnostic red 1/1. |
| V-21 | Passed : Input_endpoint_binds_task_event_and_recipient_session | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-22 | Passed : Input_body_is_absent_from_task_summary_events_and_logs | Prior ordinary green, diagnostic logging red 1/1; new HTTP-host serialization exception repaired, final qualification pending in task Result. |
| V-23 | Passed : Fallback_commit_failure_sends_no_replacement_input | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-24 | Passed : Ordinary_spill_errors_keep_their_existing_delivery_behavior | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-25 | Passed : Own_assistant_read_complaint_appears_while_working | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-26 | Passed : Legacy_windows_pointer_complaint_is_visible | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-27 | Passed : Other_task_path_or_turn_does_not_create_attention | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-28 | Passed : Quoted_user_tool_text_and_provider_silence_do_not_create_attention | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-29 | Passed : Repeated_attention_reads_keep_one_stable_condition | Controlled minute advance passed; poll-time diagnostic red 1/1; fresh CP-3 8/8 green. |
| V-30 | Passed : Unrelated_assistant_progress_does_not_hide_the_complaint | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-31 | Passed : Later_delivered_input_supersedes_the_old_complaint | Inherited ordinary green; exact final full-class qualification is in task Result. |
| V-32 | Passed : Terminal_task_removes_open_input_attention | Independent condition retained; terminal-inclusion diagnostic red 1/1; fresh CP-3 green. |
| V-33 | Entire attentionVisuals file passed: 29 tests | CP-4 inherited 29/29 Vitest, no client changes or rerun. |
| R-1 | AgentTaskRefineTests: 9/9 passed | Inherited ordinary green; exact final full-class qualification is in task Result. |
| R-2 | AgentTaskReplyOverlayTests: 7/7 passed | Inherited ordinary green; exact final full-class qualification is in task Result. |
| R-3 | PhoneHomeSpillTests 4/4, PhoneHomeSpillTransportTests 3/3, DurableRunnerSpillReceiptTests 11/11 passed | Inherited ordinary green; exact final full-class qualification is in task Result. |

## Every PC and variant remains pending

- PC-1: PENDING SourceLanding Mutation; variant: Remote destination: route Refine through desktop File.WriteAllText and omit staging. | `/*/*/AgentTaskInputSpillTests*/Runner_codex_refinement_writes_complete_body_before_pointer*` | `runner-file-present; server-spill-absent` |
- PC-2: PENDING SourceLanding Mutation; variant: Runner ceiling: select ModernConPty limits for runner Claude (not forced-spill Codex). | `/*/*/AgentTaskInputSpillTests*/Runner_ceiling_ignores_the_modern_desktop_profile*` | `runner-ceiling-spill` |
- PC-3: PENDING SourceLanding Mutation; variant: Bound byte limit: measure the temporary path and choose verbose text despite the independently established expanded-path overflow. | `/*/*/AgentTaskInputSpillTests*/Bound_pointer_measures_utf8_after_guid_path_rewrite*` | `final-wire-byte-limit` |
- PC-4: PENDING SourceLanding Mutation; variant: Input identity: reuse the first same-tick input's path/body for the second. | `/*/*/AgentTaskInputSpillTests*/Two_same_tick_refinements_keep_distinct_paths_and_bodies*` | `second-runner-body-exact` |
- PC-5: PENDING SourceLanding Mutation; variant: Lossless storage: truncate InputBody at 4,000 characters. | `/*/*/AgentTaskInputSpillTests*/Fallback_input_body_survives_complete_prompt_and_restart*` | `authorized-body-tail-exact` |
- PC-6: PENDING SourceLanding Mutation; variant: Read lifetime: clear InputBody when matching prompt receipt releases RemoteSpillBody. | `/*/*/AgentTaskInputSpillTests*/Fallback_input_body_survives_complete_prompt_and_restart*` | `input-body-retained-after-receipt` |
- PC-7: PENDING SourceLanding Mutation; variant: Eligible fallback: suppress the transition for the explicit initial before-input write refusal. | `/*/*/AgentTaskInputFallbackTests*/Runner_write_failure_types_only_the_durable_api_pointer*` | `api-only-wire` |
- PC-8: PENDING SourceLanding Mutation; variant: Error phase: let a runtime SendInput exception acquire spill_write_failed_before_input. Assert the returned frame code before later queue gates can mask it. | `/*/*/AgentTaskInputFallbackTests*/Input_failure_after_write_never_selects_fallback*` | `runtime-failure-not-before-input-code` |
- PC-9: PENDING SourceLanding Mutation; variant: Commit boundary: send the replacement pointer before the row/Warning transaction commits. Inject its commit failure. | `/*/*/AgentTaskInputFallbackTests*/Fallback_commit_failure_sends_no_replacement_input*` | `replacement-input-count=0` |
- PC-10: PENDING SourceLanding Mutation; variant: Authentication: make the new route resolve a missing token as its requested task; keep owner/session/event checks otherwise valid. | `/*/*/AgentTaskInputFallbackTests*/Input_endpoint_requires_the_recipient_task_token*` | `missing-token-body-absent` |
- PC-11: PENDING SourceLanding Mutation; variant: Task ownership: omit only exact caller-task equality; use a valid other-task token sharing the recipient session. | `/*/*/AgentTaskInputFallbackTests*/Input_endpoint_requires_the_recipient_task_token*` | `other-task-body-absent` |
- PC-12: PENDING SourceLanding Mutation; variant: Session ownership: omit only current-session versus event-recipient equality for the authenticated task. | `/*/*/AgentTaskInputFallbackTests*/Input_endpoint_binds_task_event_and_recipient_session*` | `wrong-session-body-absent` |
- PC-13: PENDING SourceLanding Mutation; variant: Event ownership: omit only event.AgentTaskId equality; use a foreign input event in the same recipient session. | `/*/*/AgentTaskInputFallbackTests*/Input_endpoint_binds_task_event_and_recipient_session*` | `foreign-event-body-absent` |
- PC-14: PENDING SourceLanding Mutation; variant: Legacy body: substitute capped Detail for a null InputBody. | `/*/*/AgentTaskInputFallbackTests*/Input_endpoint_binds_task_event_and_recipient_session*` | `legacy-null-status-404` |
- PC-15: PENDING SourceLanding Mutation; variant: Public DTO privacy: include InputBody in an existing task-event response projection. | `/*/*/AgentTaskInputFallbackTests*/Input_body_is_absent_from_task_summary_events_and_logs*` | `summary-tail-absent` |
- PC-16: PENDING SourceLanding Mutation; variant: Log privacy: add the full InputBody as a structured log property in the input path. | `/*/*/AgentTaskInputFallbackTests*/Input_body_is_absent_from_task_summary_events_and_logs*` | `log-tail-absent` |
- PC-17: PENDING SourceLanding Mutation; variant: Attention source path: accept an explicit foreign unreadable source path with every task/turn/phrase fact otherwise valid. | `/*/*/TaskInputReadFailureTests*/Other_task_path_or_turn_does_not_create_attention*` | `foreign-path-condition-count=0` |
- PC-18: PENDING SourceLanding Mutation; variant: Attention turn: admit assistant text from an earlier turn of the same session and task/path. | `/*/*/TaskInputReadFailureTests*/Other_task_path_or_turn_does_not_create_attention*` | `foreign-turn-condition-count=0` |
- PC-19: PENDING SourceLanding Mutation; variant: Attention speaker: admit ToolResult text as AssistantText, keeping the complete owning prompt/path/turn valid. | `/*/*/TaskInputReadFailureTests*/Quoted_user_tool_text_and_provider_silence_do_not_create_attention*` | `quoted-condition-count=0` |
- PC-20: PENDING SourceLanding Mutation; variant: Attention projection: omit the new projection call from AttentionService.GetAsync. | `/*/*/TaskInputReadFailureTests*/Own_assistant_read_complaint_appears_while_working*` | `own-condition-count=1` |
- PC-21: PENDING SourceLanding Mutation; variant: Stable episode: reset SinceUtc to poll time on every read. | `/*/*/TaskInputReadFailureTests*/Repeated_attention_reads_keep_one_stable_condition*` | `condition-time-stable` |
- PC-22: PENDING SourceLanding Mutation; variant: Small reply: fit every blocked answer through a refinement pointer even below the queue limit. | `/*/*/AgentTaskInputSpillTests*/Runner_small_reply_remains_inline*` | `inline-answer-exact` |
- PC-23: PENDING SourceLanding Mutation; variant: Question overlay: prefix the marker before the existing Now answer. Capture body before ToolResult matching so this fails without a confirmation timeout. | `/*/*/AgentTaskInputSpillTests*/Open_question_reply_remains_unmarked_now_and_toolresult_confirmed*` | `overlay-answer-unmarked` |
- PC-24: PENDING SourceLanding Mutation; variant: Fallback authority: replace strict conversation-key parsing with event-ID extraction from pointer prose; seed an otherwise valid task/event/session and ordinary queue key, then return the specific pre-input error. | `/*/*/AgentTaskInputFallbackTests*/Ordinary_spill_errors_keep_their_existing_delivery_behavior*` | `unowned-fallback-warning-count=0` |
- PC-25: PENDING SourceLanding Mutation; variant: Prior uncertainty: allow fallback on a later before-input refusal despite an unresolved earlier submission. | `/*/*/AgentTaskInputFallbackTests*/Input_failure_after_write_never_selects_fallback*` | `prior-attempt-wire-unchanged` |
- PC-26: PENDING SourceLanding Mutation; variant: Retype boundary: allow fallback during overlay recovery after the initial body write returned successfully. | `/*/*/AgentTaskInputFallbackTests*/Input_failure_after_write_never_selects_fallback*` | `retype-wire-unchanged` |
- PC-27: PENDING SourceLanding Mutation; variant: Durable replay: keep the API-only Body change in memory while committing the Warning, then recreate services before retry. | `/*/*/AgentTaskInputFallbackTests*/Restart_after_fallback_commit_replays_the_same_api_pointer*` | `restart-api-wire-exact` |
- PC-28: PENDING SourceLanding Mutation; variant: Read-only attention: persist an event from the new projection. The interceptor records commands without blocking them. | `/*/*/TaskInputReadFailureTests*/Repeated_attention_reads_keep_one_stable_condition*` | `attention-write-count=0` |
- PC-29: PENDING SourceLanding Mutation; variant: Prompt evidence: use a clipped owning UserPrompt as complete; the assistant complaint is otherwise eligible. | `/*/*/TaskInputReadFailureTests*/Quoted_user_tool_text_and_provider_silence_do_not_create_attention*` | `clipped-prompt-condition-count=0` |
- PC-30: PENDING SourceLanding Mutation; variant: Supersession boundary: clear a complaint when a later row is merely Pending instead of having a complete delivered prompt. | `/*/*/TaskInputReadFailureTests*/Later_delivered_input_supersedes_the_old_complaint*` | `pending-input-keeps-condition` |
- PC-31: PENDING SourceLanding Mutation; variant: Terminal filter: include the settled task in the new projection's eligibility query; keep its own complete prompt and complaint. | `/*/*/TaskInputReadFailureTests*/Terminal_task_removes_open_input_attention*` | `terminal-input-condition-count=0` |
- PC-32: PENDING SourceLanding Mutation; variant: Implicit referent: accept the refinement-file phrase with two possible owning inputs and no exact source path. | `/*/*/TaskInputReadFailureTests*/Other_task_path_or_turn_does_not_create_attention*` | `ambiguous-input-condition-count=0` |
- PC-33: PENDING SourceLanding Mutation; variant: Attention session: admit the identical task marker/path/turn-shaped complaint from another session. | `/*/*/TaskInputReadFailureTests*/Other_task_path_or_turn_does_not_create_attention*` | `foreign-session-condition-count=0` |
- PC-34: PENDING SourceLanding Mutation; variant: Confinement handoff: swallow the writer's admission refusal and call the runtime anyway. Keep the real writer unchanged. | `/*/*/AgentTaskInputFallbackTests*/Runner_path_refusal_never_writes_outside_the_mirror*` | `refused-path-input-count=0` |
- PC-35: PENDING SourceLanding Mutation; variant: Visual mapping: replace TaskInputUnreadable with an existing wrong nonempty visual (compiles; do not delete a required Record key). | `Vitest -t "^attentionVisuals draws TaskInputUnreadable as a warning with a task target$"` | `unreadable-visual-label; unreadable-task-target` |
- PC-36: PENDING SourceLanding Mutation; variant: Home bucket: route only TaskInputUnreadable to broken before the ordinary Warning fallback. | `Vitest -t "^attentionVisuals keeps unreadable input in the review home bucket$"` | `unreadable-home-review` |

## Production-byte restoration ledger

SessionMessageQueueService.cs backup=A85514B9041AF8AABFEF00DD65C1B27EDD86E3675D5CBB0454BF31B59B5185AF restored=A85514B9041AF8AABFEF00DD65C1B27EDD86E3675D5CBB0454BF31B59B5185AF identical=true
AgentTaskInputService.cs backup=51AE136FCFE716407647D5E4831C296D5EF2B8AD78A7DB06BD5344BC1D39BB71 restored=51AE136FCFE716407647D5E4831C296D5EF2B8AD78A7DB06BD5344BC1D39BB71 identical=true
AgentTaskReplyService.cs backup=74E531DA2AC034C82B71035908DDB982C2D814EBEDF2BAE941B968873D35BD70 restored=74E531DA2AC034C82B71035908DDB982C2D814EBEDF2BAE941B968873D35BD70 identical=true
AttentionService.TaskInputs.cs backup=DCB56ED005D2CBC0C89E57799B7FD67D422895DDBC64447F0E0DCE9501FC45FE restored=DCB56ED005D2CBC0C89E57799B7FD67D422895DDBC64447F0E0DCE9501FC45FE identical=true
AttentionService.cs backup=0CA336DC91B4E5AD73E7BC12F4A1FB9D405C14875323B81EF9520F6087076656 restored=0CA336DC91B4E5AD73E7BC12F4A1FB9D405C14875323B81EF9520F6087076656 identical=true

## Pushed slice commits before final qualification publication

1462102cb4e88dea9c42223a554f8b9dd67ed0cf CARD-0965 import endpoint change-token namespace after compile failure; verification pending
629338dba1e22b8999c18733d9caa598a0ba591e CARD-0965 mount real input and detail endpoints in isolated HTTP host; Program removed from API proofs
93b76ea56a640074b5df342fce6574b087691907 CARD-0965 name fallback witness and pin owning identities; five diagnostic guards red, source restored
3211f53bece05a9f1af54063c9f0bb37f9bd63cb CARD-0965 record V16 three-path and V29 fixed-clock baselines green; Final pending
6dd0ac62ed7e51d722fd2257f527afb5ea1e0e69 CARD-0965 fix nullable row-id assertion compile error; baseline pending
02687c74e8d54d7989c7ed992fe2d79e97d2bb6c CARD-0965 correct readiness fixture after timeout; bind admitted immediate-row ownership
5e8a9e08f2a4cb9d5485fd1881596c7c9c06199c CARD-0965 stage three queue-path proofs and controlled poll clock; verification pending
02e97b63c48ab170715d2d0edbed83f90ae74405 CARD-0965 record partial proof outcome: CP1-4 green; Unit and compatibility pending

--- next stage ---
next: review
handoff: Review test-only input proof completion at the final qualified pushed SHA; inspect red-first deviation, synthetic immediate-row ownership/recovery and inherited Unit skips; then caller lands current Code owner 4c1697dc and commissions SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-03-card-0965-final-qualification.md


## Final qualification outcome at 0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26

CHECKPOINT CP-1 commit=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 build=ok filter=/*/*/(AgentTaskInputSpillTests*)|(AgentTaskRefineTests*)|(AgentTaskReplyOverlayTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 build=reused filter=/*/*/(AgentTaskInputFallbackTests*)|(PhoneHomeSpillTests*)|(PhoneHomeSpillTransportTests*)|(DurableRunnerSpillReceiptTests*)/* executed=29 passed=28 failed=1 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 build=reused filter=/*/*/TaskInputReadFailureTests*/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 build=reused filter=/*/*/(ParkedMessageSweepServiceTests*)|(CapacityRecoveryCompatibilityTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-4c1697dc/.antiphon/checkpoints/20261003-121954-2168/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=0bf1a5c4fa6b30bbeba9e71ddcd641294470ec26 sourceState=clean buildSource=verified

Fresh TRX: CP-1 31/31, CP-2 28/29 (V-22 RequestDelegate serialization fixture exception), CP-3 8/8, CP-7 18/18; no integration skips. All four rows have granted slots, waited=0s, dirty=0, clean source and verified build provenance. Both new sweep/migration methods passed. V-16 three paths and V-29 controlled clock passed. V-1..V-21 and V-23..V-33, R-1..R-3 passed; V-22 pending repaired green. All PC variants pending. No production bytes changed. The final converter repair changes only this test log inspection; no assertion/timeout was loosened. Rerun full CP-2 and then the four-row published-tip qualification at committed HEAD, without repeating Unit. The 60-minute box ended before this further build could be owned and awaited.

--- next stage ---
next: code
handoff: Continue from the pushed 4c1697dc branch: compile and rerun full CP-2 for the added delegate-metadata serializer, then qualify the published tip through the four-row qualification plan; Unit 3910/0/52 and CP-7 18/18 are complete, all PCs pending. Request Review only after ordinary green.
artifact: docs/superpowers/plans/2026-10-03-card-0965-final-qualification.md
