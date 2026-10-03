# CARD-0965 Code report

Original Code / landing owner: b630f3bf-9528-4a6c-a669-903d56c66361.
Branch: feat/card-task-b630f3bf.
Worktree: /work/worktrees/task-b630f3bf.
Start: c6d5d56b5b4c565d36157e21de9c85029cc45b53.
Implementation SHA: 95509608e53986b9ac49ae0023f2cf7b1631bca8.
Commits pushed: e1a0ff9f716b222554c33ef3b03700d03a497fb7 (tests/manifest),
95509608e53986b9ac49ae0023f2cf7b1631bca8 (real phone-home refusal mapping and ordinary-row outcomes).
Restart: none. Test-only; no production change, land or deployment.

Plan: docs/superpowers/plans/2026-10-03-card-0965-remaining-input-proofs.md.
Parent: docs/superpowers/plans/2026-10-01-card-0888-runner-bound-refinement-spill-plan.md.
Checkpoint run: .antiphon/checkpoints/20261003-105339-6074/.
Scratch/baseline evidence: .antiphon/c965-strength/ and .antiphon/c965-*.log.
Invalid selection evidence: .antiphon/c965-strength-base2/.

## Card acceptance quoted

“Pending ordinary proofs ... V-16, V-20, V-22, V-29, V-32.”
“The fixture needs a modern/large desktop profile so the test proves that runner-bound input ignores it.”
“Add a sweep test where a parked row with a task-input: prefix and a malformed key ... stays Pending.”
“Decide whether to keep the incident, and pin it in Ordinary_spill_errors_keep_their_existing_delivery_behavior.”
“Neither test seeds a pre-existing AgentTaskEvents row before the up-migration.”

## Changes

- tests/Antiphon.Tests/TestHelpers/TaskInputSpillFixture.cs: optional large injected
  PtyDeliveryProfile and captured logger provider. Default callers retain existing
  ceilings. Profile requests modern; its settings deliberately remain large when
  the Linux backend falls back. No actual Windows PTY claim.
- tests/Antiphon.Tests/Application/AgentTaskInputSpillTests.cs: verifies the large
  profile exists before requiring the runner-bound spill at enqueue.
- tests/Antiphon.Tests/Application/AgentTaskInputFallbackTests.cs: real queue ->
  PhoneHomeRunnerClient -> WebSocket -> PhoneHomeCommandDispatcher -> writer
  failure caused by an ordinary file at `.antiphon`. Captures the stable refusal,
  zero terminal inputs, persisted/typed API pointer, one Warning and authorized
  full-body HTTP GET. Adds a valid capability positive control/refusal; public
  task/event HTTP privacy and formatted/structured logging privacy. Ordinary
  row remains Pending with its attempt retained and no task fallback Warning.
- tests/Antiphon.Tests/Application/TaskInputReadFailureTests.cs: stable task,
  session, message, condition key/time across both polls; actual SQL-write and
  runner-input probes. Terminal condition has a pre-settlement positive control
  and an independent BlockedQuestion that remains after settlement.
- tests/Antiphon.Tests/Application/ParkedMessageSweepServiceTests.cs: malformed
  length, uppercase GUID and non-GUID keys remain Pending while a valid keyed
  machine row is canceled.
- tests/Antiphon.Tests/Application/CapacityRecoveryCompatibilityTests.cs: downgrade
  to immediately before AddAgentTaskEventInputBody, insert a real pre-existing
  event with raw SQL while the column does not exist, migrate up and require
  unchanged owner/detail and null InputBody.

No existing assertion was weakened, deleted or skipped. Existing methods were
strengthened with additional witnesses; two new methods were added.

## Item 5 decision and follow-ups

Production unchanged. SessionMessageQueueService has separate pre-input refusal
branches at durable-now, send-now and flush. Only the general flush transport
catch currently calls RecordTransportFailureAsync. Restoring incident visibility
consistently across the three paths is not the authorized one-line production
change; its helper also increments completion-failure telemetry and resolves a
standing agent through PersistentSessionId. Keep the delivery result, and commission
an ordinary-row incident repair separately if desired. Pending/body/attempt/Warning
outcomes are pinned in the named existing test.

Board searches `spill incident` and `ordinary spill` returned zero; `incident
visibility` returned CARD-0965, read in full. Keep this follow-up on CARD-0965 item 5.
Windows parent CP-5 remains pending under CARD-0960. Parent PC-1 through PC-36,
including every variant in its frozen guard table, remain pending for separately
commissioned method-scoped SourceLanding Mutation; these Code spot checks do not
discharge any PC. The parent V-row groups' additional scratch strength proofs
V-16/V-20/V-22 and V-29/V-32 remain pending unless separately recorded below.

## Unlisted commands and scratch proof

Setup exceptions: checkpoint-tool bootstrap through build-slot (granted, waited
240s, held 5s; one existing nullable warning, zero errors), npm ci through
build-slot (granted, waited 210s, held 21s; 765 packages installed). An initially
queued STRENGTH-BASE driver was canceled before it received a slot, to avoid
overlapping builds in the shared project graph. No tests/build ran from it.

STRENGTH-BASE used an unsupported combined method filter: build succeeded, zero
tests selected, exit 3. This is invalid proof, not green. Exact method baselines
reused its source-qualified build at e1a0ff9f716b222554c33ef3b03700d03a497fb7:
CEILING-BASE and SWEEP-BASE each executed/passed 1, failed/skipped 0.

Three independent brief-authorized diagnostic mutations were compiled together;
each test selection remained exact-method scoped. CEILING-RED used the desktop
profile for remote refinement and failed `runner-ceiling-spill` (1/0/1/0).
SWEEP-RED removed the in-memory key filter and failed
`malformed-task-input-stays-pending` (1/0/1/0). MIGRATION-RED defaulted old
InputBody values to an empty string and failed `legacy-event-input-body-null`
(1/0/1/0). All are assertion failures, not compilation/fixture/timeout failures.
Dirty diagnostic receipts are explicitly distinct from ordinary clean receipts.

All production bytes restored exactly, git diff empty, restored files touched
before rebuild. SHA-256 pairs (backup and restored file identical):
AgentTaskReplyService: 74e531da2ac034c82b71035908ddb982c2d814ebedf2bae941b968873d35bd70.
ParkedMessageSweepService: e2a6c7681d5f2364047ea1c058f9f2dbe3590bb64a04e719978aba0cf0370aa6.
AddAgentTaskEventInputBody: 95d905c26bc95a095ae4bea272aa22698c1ea3bb88ad5ad067bb09f69514ee3a.

No routine repetitions or full-assembly runs. Unit is required because this task
changes a shared test fixture; it does not cover delivery/leases/landing/persistence.
The additional bounded integration rows cover the input feature and migration.

CP-4 actually ran 29 Vitest tests, all passed, against the task SHA. Parent's
historical 24-count expectation predates later integrated tests; no client source
was changed. Command evidence: rows/CP-4/console.log and logs/client-tests.log.

Credential note: an early environment inspection included the current task bearer
in tool output. No credential was committed or reproduced in this report.

## Ordinary receipts and V/R outcomes

Append the fresh completed checkpoint report, TRX method census and actual V/R
outcomes below. Unfinished rows and missing strength proofs are pending, never passed.

## Actual stopped-run outcome

Timeboxed partial: CP-1/2/3 passed 31/27/8, zero failed/skipped; CP-4 passed 29 Vitest. CP-6 Unit was stopped before TRX/counters; its log records jq-dependent skips. CP-7 never started. No complete Final qualification or Review readiness is claimed. Stop joined the executor; its shadow copy was removed. The remaining Unit holder exited, and the broker subsequently showed zero leases for this task.

The stopped run has no terminal report.json/report.md; state.json preserves tool-produced row lines and clean verified build snapshots for completed rows. wait returned executor-died-without-phase=done after the deliberate stop. No counts or receipts were invented for incomplete rows.

Unedited checkpoint-tool lines from state.json:

```text
CHECKPOINT CP-1 commit=95509608e53986b9ac49ae0023f2cf7b1631bca8 build=ok filter=/*/*/(AgentTaskInputSpillTests*)|(AgentTaskRefineTests*)|(AgentTaskReplyOverlayTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/checkpoints/20261003-105339-6074/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=95509608e53986b9ac49ae0023f2cf7b1631bca8 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=95509608e53986b9ac49ae0023f2cf7b1631bca8 build=ok filter=/*/*/(AgentTaskInputFallbackTests*)|(PhoneHomeSpillTests*)|(PhoneHomeSpillTransportTests*)|(DurableRunnerSpillReceiptTests*)/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/checkpoints/20261003-105339-6074/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=95509608e53986b9ac49ae0023f2cf7b1631bca8 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=95509608e53986b9ac49ae0023f2cf7b1631bca8 build=ok filter=/*/*/TaskInputReadFailureTests*/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/checkpoints/20261003-105339-6074/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=95509608e53986b9ac49ae0023f2cf7b1631bca8 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=95509608e53986b9ac49ae0023f2cf7b1631bca8 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=95509608e53986b9ac49ae0023f2cf7b1631bca8 sourceState=clean buildSource=notApplicable
```

Unedited direct-driver diagnostic/baseline lines:

```text
CHECKPOINT CEILING-BASE commit=e1a0ff9f716b222554c33ef3b03700d03a497fb7 build=reused filter=/*/*/AgentTaskInputSpillTests*/Runner_ceiling_ignores_the_modern_desktop_profile executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/c965-strength/CEILING-BASE-20261003-103928-a63c/run.trx slot=granted waited=31s dirty=0 source=e1a0ff9f716b222554c33ef3b03700d03a497fb7 sourceState=clean buildSource=verified
CHECKPOINT SWEEP-BASE commit=e1a0ff9f716b222554c33ef3b03700d03a497fb7 build=reused filter=/*/*/ParkedMessageSweepServiceTests*/Malformed_task_input_keys_stay_pending executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/c965-strength/SWEEP-BASE-20261003-104206-c518/run.trx slot=granted waited=45s dirty=0 source=e1a0ff9f716b222554c33ef3b03700d03a497fb7 sourceState=clean buildSource=verified
CHECKPOINT CEILING-RED commit=e1a0ff9f716b222554c33ef3b03700d03a497fb7 build=ok filter=/*/*/AgentTaskInputSpillTests*/Runner_ceiling_ignores_the_modern_desktop_profile executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/c965-strength/CEILING-RED-20261003-104444-7cd0/run.trx slot=granted waited=0s dirty=3 source=e1a0ff9f716b222554c33ef3b03700d03a497fb7+dirty:eee6356b0afd8e5145d0049bf627f9b375b441782973f2f80cf9c71daec7dbdd sourceState=dirty buildSource=verified
CHECKPOINT SWEEP-RED commit=e1a0ff9f716b222554c33ef3b03700d03a497fb7 build=reused filter=/*/*/ParkedMessageSweepServiceTests*/Malformed_task_input_keys_stay_pending executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/c965-strength/SWEEP-RED-20261003-104944-e63e/run.trx slot=granted waited=0s dirty=3 source=e1a0ff9f716b222554c33ef3b03700d03a497fb7+dirty:eee6356b0afd8e5145d0049bf627f9b375b441782973f2f80cf9c71daec7dbdd sourceState=dirty buildSource=verified
CHECKPOINT MIGRATION-RED commit=e1a0ff9f716b222554c33ef3b03700d03a497fb7 build=reused filter=/*/*/CapacityRecoveryCompatibilityTests*/Input_body_upgrade_preserves_a_preexisting_event_with_null_body executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b630f3bf/.antiphon/c965-strength/MIGRATION-RED-20261003-105126-5523/run.trx slot=granted waited=0s dirty=3 source=e1a0ff9f716b222554c33ef3b03700d03a497fb7+dirty:eee6356b0afd8e5145d0049bf627f9b375b441782973f2f80cf9c71daec7dbdd sourceState=dirty buildSource=verified
```

## Fresh TRX census and all V/R IDs

CP-1 fresh counters: <Counters total="31" executed="31" passed="31" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />
- Antiphon.Tests.Application.AgentTaskInputSpillTests: 15 results
- Antiphon.Tests.Application.AgentTaskRefineTests: 9 results
- Antiphon.Tests.Application.AgentTaskReplyOverlayTests: 7 results
CP-2 fresh counters: <Counters total="27" executed="27" passed="27" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />
- Antiphon.Tests.Application.AgentTaskInputFallbackTests: 9 results
- Antiphon.Tests.Application.DurableRunnerSpillReceiptTests: 11 results
- Antiphon.Tests.Application.PhoneHomeSpillTests: 4 results
- Antiphon.Tests.Application.PhoneHomeSpillTransportTests: 3 results
CP-3 fresh counters: <Counters total="8" executed="8" passed="8" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />
- Antiphon.Tests.Application.TaskInputReadFailureTests: 8 results

| ID | Actual executed method outcome | Remaining proof limitation |
|---|---|---|
| V-1 | Passed : Runner_codex_refinement_writes_complete_body_before_pointer | Method execution passed; no broader new TestDesign-strength claim. |
| V-2 | Passed : Runner_claude_refinement_uses_the_same_inbox_route | Method execution passed; no broader new TestDesign-strength claim. |
| V-3 | Passed : Runner_grok_refinement_is_forced_to_a_join_safe_pointer | Method execution passed; no broader new TestDesign-strength claim. |
| V-4 | Passed : Runner_ceiling_ignores_the_modern_desktop_profile | Method execution passed; no broader new TestDesign-strength claim. |
| V-5 | Passed : Bound_pointer_measures_utf8_after_guid_path_rewrite | Method execution passed; no broader new TestDesign-strength claim. |
| V-6 | Passed : Two_same_tick_refinements_keep_distinct_paths_and_bodies | Method execution passed; no broader new TestDesign-strength claim. |
| V-7 | Passed : Local_desktop_refinement_keeps_its_file_and_pointer_contract | Method execution passed; no broader new TestDesign-strength claim. |
| V-8 | Passed : Runner_blocked_reply_reaches_the_inbox_with_task_attribution | Method execution passed; no broader new TestDesign-strength claim. |
| V-9 | Passed : Runner_small_reply_remains_inline | Method execution passed; no broader new TestDesign-strength claim. |
| V-10 | Passed : Open_question_reply_remains_unmarked_now_and_toolresult_confirmed | Method execution passed; no broader new TestDesign-strength claim. |
| V-11 | Passed : Missing_runner_cwd_selects_api_without_a_server_write | Method execution passed; no broader new TestDesign-strength claim. |
| V-12 | Passed : Queued_refinement_still_amends_the_goal | Method execution passed; no broader new TestDesign-strength claim. |
| V-13 | Passed : Fallback_input_body_survives_complete_prompt_and_restart | Method execution passed; no broader new TestDesign-strength claim. |
| V-14 | Passed : Refinement_send_now_upgrades_the_same_owned_row | Method execution passed; no broader new TestDesign-strength claim. |
| V-15 | Passed : Pending_refinement_age_remains_visible_without_delivery_attempts | Method execution passed; no broader new TestDesign-strength claim. |
| V-16 | Passed : Runner_write_failure_types_only_the_durable_api_pointer | PARTIAL: only SendNow path; flush/durable-now cases and prescribed signal-based readiness remain; V-group scratch remains. |
| V-17 | Passed : Runner_path_refusal_never_writes_outside_the_mirror | Method execution passed; no broader new TestDesign-strength claim. |
| V-18 | Passed : Input_failure_after_write_never_selects_fallback | Method execution passed; no broader new TestDesign-strength claim. |
| V-19 | Passed : Restart_after_fallback_commit_replays_the_same_api_pointer | Method execution passed; no broader new TestDesign-strength claim. |
| V-20 | Passed : Input_endpoint_requires_the_recipient_task_token | Requested additional V-group scratch proof remains pending. |
| V-21 | Passed : Input_endpoint_binds_task_event_and_recipient_session | Method execution passed; no broader new TestDesign-strength claim. |
| V-22 | Passed : Input_body_is_absent_from_task_summary_events_and_logs | V-group scratch and plan minimal-endpoint HTTP harness remain pending. |
| V-23 | Passed : Fallback_commit_failure_sends_no_replacement_input | Method execution passed; no broader new TestDesign-strength claim. |
| V-24 | Passed : Ordinary_spill_errors_keep_their_existing_delivery_behavior | Method execution passed; no broader new TestDesign-strength claim. |
| V-25 | Passed : Own_assistant_read_complaint_appears_while_working | Method execution passed; no broader new TestDesign-strength claim. |
| V-26 | Passed : Legacy_windows_pointer_complaint_is_visible | Method execution passed; no broader new TestDesign-strength claim. |
| V-27 | Passed : Other_task_path_or_turn_does_not_create_attention | Method execution passed; no broader new TestDesign-strength claim. |
| V-28 | Passed : Quoted_user_tool_text_and_provider_silence_do_not_create_attention | Method execution passed; no broader new TestDesign-strength claim. |
| V-29 | Passed : Repeated_attention_reads_keep_one_stable_condition | PARTIAL: command/input probes added, but explicit one-minute controlled poll-clock advancement and V-group scratch remain. |
| V-30 | Passed : Unrelated_assistant_progress_does_not_hide_the_complaint | Method execution passed; no broader new TestDesign-strength claim. |
| V-31 | Passed : Later_delivered_input_supersedes_the_old_complaint | Method execution passed; no broader new TestDesign-strength claim. |
| V-32 | Passed : Terminal_task_removes_open_input_attention | Unrelated-condition witness added; requested V-group scratch remains pending. |
| V-33 | Entire attentionVisuals file passed: 29 tests | Historical parent count 24 is stale. |
| R-1 | AgentTaskRefineTests: 9/9 passed | None in this selected class. |
| R-2 | AgentTaskReplyOverlayTests: 7/7 passed | None in this selected class. |
| R-3 | PhoneHomeSpillTests 4/4, PhoneHomeSpillTransportTests 3/3, DurableRunnerSpillReceiptTests 11/11 passed | None in these selected classes. |

Final pending: whole Unit lane (CP-6), both full compatibility classes (CP-7), restored-green sweep/migration phases, and the stated V-16/20/22/29/32 strength/design work. Windows parent CP-5 is outside this dispatch, still pending CARD-0960. No deployment/manual activation was performed or required for this test-only slice.

## Every parent PC / variant remains pending

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
New malformed-key filter and legacy nullable migration witnesses also require Mutation-owned control inventory/discovery; no new PC number was fabricated by Code.

## Code continuation / publication

Continue Code from the pushed task branch. Finish the pending proof details and Final rows at committed HEAD, then request ordinary Review; after a clean Review the caller lands original Code owner b630f3bf-9528-4a6c-a669-903d56c66361 and commissions SourceLanding Mutation. No land/deploy occurred. Restart remains none.

The final evidence-only commit publishes this report and state.json after verification stopped. Its SHA is in the final task response; it is not represented as the SHA tested by the 95509608 receipts. Fresh qualification at the published tip remains pending.

Cleanup: checkpoint clean removed its declared outputs; the exact owned baseline/red/bootstrap inventory removed 53 additional output roots. A final bounded inventory found zero bin-c965-* roots. Aborted Unit teardown has no complete receipt; any marked test-temp recovery remains with the guarded test-temp policy. No source edits occurred while builds/tests were active.
