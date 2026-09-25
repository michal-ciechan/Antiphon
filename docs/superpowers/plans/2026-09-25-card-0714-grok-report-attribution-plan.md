# CARD-0714: Attribute reports across provider housekeeping turns

Plan stage, 2026-09-25. Implementation and verification are next: **Code**.

Base: `38fb9897`, including the [investigation](../../investigations/2026-09-25-card-0714-grok-report-attribution.md). Carry that investigation unchanged with this plan. This document changes no production code and claims no executed test evidence.

## Outcome and acceptance

When catch-up ingests the real report and a subsequent provider housekeeping turn together, settlement must select the task's report boundary and that boundary's response. For session `df9109a5-08d5-4832-bd71-996a4233090a`, the result is the report at sequence 271, ended at 272, not the acknowledgement at 275, ended at 276. Task `e9516943` must settle `Succeeded`, `ReportEvidence=Marked`, `NextStage=Review`, with one completion and no new `DelegateReportUncorrelated` incident or delivery-watchdog failure.

Selection remains specific to the current task, dispatch and reply watermark. A real unmarked user/queued turn is a barrier, not permission to search arbitrarily far back for a convenient marker. All acknowledgement-only housekeeping boundaries may be skipped; neither their text nor their response identity becomes the report.

## Ground truth

| Assumption or question | Current code / evidence | Consequence |
|---|---|---|
| Grok lost the brief marker | Investigation: sequence 11 contains the literal current task marker three times; the spill file contains it twice. | Keep exact task-marker matching. No transport or CARD-0584 channel-matcher change. |
| Last `TurnEnd` means the task's report | `AgentTaskReplyService.ExtractMarkedTurnAsync` selects one newest boundary, then a prompt from `TranscriptPromptSpan.TurnPrompts`. | A newer housekeeping boundary hides a valid report. Select boundary and owning prompt together. |
| Filtering the reminder prompt fixes it | `FinalMessageOf` uses the selected boundary's `ApiCallId`; 276 names the acknowledgement response. | Prompt filtering alone misreports the acknowledgement. Preserve 272 and its response ID. |
| Housekeeping currently has one representation | `TranscriptPromptSpan` excludes command wrappers, verified raw command echoes, compaction continuations and Claude `<task-notification>`; `Notifications` retains the last kind. | Keep raw prompt boundaries as well as semantic turn ownership; do not discard notification bookkeeping. |
| All Claude notification responses can be thrown away | `the_last_subagent_notification_settles_the_task_with_the_verdict` deliberately settles the synthesized final report on a notification response. Its helper supplies a current-task closing report token. | Preserve a marked, attributable Claude continuation report, but skip a notification acknowledgement without that token. |
| Codex needs a generic reminder prefix | `CodexTranscriptNormalizer.Normalize` only emits prompt rows for `event_msg/user_message` or `item_completed/UserMessage`. `response_item`, compaction metadata and unknown events emit nothing. `task_complete` is a genuine `TurnEnd`; TUI `AgentMessage.phase=final_answer` carries its `turn_id`. | No evidence for a Codex prompt-text exemption. Keep the normalizer's existing structural exclusions and final-answer identity. |
| Dispatch can be bounded by ingestion time | `TranscriptPromptSpan.LoadAsync` uses native `Timestamp > DispatchedAt`, not `CreatedAt`; null timestamps are retained. `QueuedUserPrompt` uses its enqueue timestamp. | Retain this floor and sequence ordering during the backward search. Backfill must not rejuvenate an old brief. |
| Answering a blocked task only changes time | `RepliedAtSequence` fences the **prompt**, not merely the boundary (CARD-0348). | Every candidate and inherited continuation owner must pass that fence. |
| Watchdog catch-up also settles | `CatchUpTranscriptAsync` is persist-only. `FailNeverStartedAsync` arm 2 trusts a task-scoped historical incident; `SettleDeferredReportsAsync` independently re-enters settlement but gates on the newest boundary. | Re-evaluate after catch-up and before arm-2 failure; the deferred sweep must use the same selected boundary. |
| `ConcurrencyToken` automatically protects a watchdog write | `AppDbContext` configures `AgentTask.ConcurrencyToken` as required, **not** `IsConcurrencyToken`; `FailAsync` uses ordinary SaveChanges. Dispatch claims elsewhere use explicit conditional updates. | Add a narrow conditional arm-2 failure commit; do not rely on implicit EF conflict detection or change the entire entity's mapping. |

Read owners: `docs/project-context.md`, `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, `docs/session-runtime-invariants.md`, `docs/testing-and-build.md`; stage and delegate bundles. This is an Application-layer attribution change, with no domain schema, public API, input encoding, runner launch or working/idle change.

## Decisions

### D-1. A provider allowlist, not a generic XML/reminder heuristic

Resolve the provider from the persisted `AgentSession.AgentKind`. Put the additional pure classification in `server/Application/Services/TaskReportHousekeeping.cs`; keep it internal and stateless. Extend `TranscriptPromptSpan` to expose classified raw prompt rows alongside its existing `TurnPrompts` and `Notifications`. Preserve existing consumers' null-timestamp/queued-prompt semantics. Do not broaden `TranscriptKinds.IsTaskNotificationPrompt` or the three working/idle implementations.

| Provider | Allowed housekeeping shape | Treatment |
|---|---|---|
| Grok | Entire trimmed `<system-reminder> ... </system-reminder>` envelope whose body has the measured `Background task "<id>" completed (exit code: <integer>).` line, `Description: ... | Duration: ...s` line, and `Use get_command_or_subagent_output("<same-id>") to see the full output.` line. IDs are nonempty and must agree; match ordinally. Normalize CRLF to LF and permit outer whitespace, not arbitrary deleted whitespace. | Background completion; exclude from meaningful task prompts and skip its response boundary. Keep the raw row to delimit response extraction. |
| Grok | Valid `[antiphon-grok-rules:<queue-id:N>]` opening, backed by the same session's System queue row with `RulesRefreshKey`, as `GrokRulesRefreshService.IsRefreshPromptAsync` already requires. | Internal rules turn, never a report even if its text quotes a report/task token. Skip its boundary while looking for an eligible earlier task turn. |
| ClaudeCode | Existing leading `<task-notification>` shape, including queued normalization where supported. | Retain in `Notifications` for `tool-use-id` pairing. An acknowledgement boundary is skipped. D-3 defines the narrow substantive continuation exception. |
| ClaudeCode | Existing `<command-name>`, `<local-command-stdout>`, compaction-continuation prefix, and raw slash-command echoes **only with** a matching wrapper in the span. | Existing inert housekeeping semantics remain. A raw `/...` alone remains a real prompt. These records do not create a report boundary. |
| Codex | Structural metadata currently ignored by `CodexTranscriptNormalizer`: `response_item` (including duplicate role-user material), `compacted`, `event_msg/context_compacted`, `task_started`, `turn_context`, unknown metadata. | Already absent from normalized prompt/boundary ownership. Add no prompt-text allowlist entry. `task_complete` must remain a report boundary, not be confused with Grok's background `task_completed`. |
| Other providers / unknown shapes | No new text exemption. Existing legacy housekeeping recognition is retained; this change adds only the Grok completion predicate for Grok sessions. | Ordinary prompt/marker rules. |

Bare `<system-reminder>`, quoted or embedded reminders, text appended outside the envelope, mismatched IDs, missing lines, arbitrary “background task done” prose, and the Grok envelope on a Codex/Claude session are not newly exempt. The allowlist is the measured protocol shape, not proof of who physically typed an identical string. Unknown shapes remain visible and fail closed at attribution; widen the list only with a fixture and evidence. Grok `task_backgrounded`/`task_completed` update events already emit no prompt and need no normalizer change.

### D-2. Select the newest attributable boundary inside the current task's ownership span

Introduce one internal concrete selector (`server/Application/Services/TaskReportTurnSelector.cs`) used by reply extraction and deferred recovery. It returns the selected `TurnEnd`, immediate raw prompt, effective task-owning prompt, response bounds and skip diagnostics, or an explicit no-candidate/barrier outcome. It performs no settlement or delivery. It must retain enough raw prompt information to prevent a timestamp-filtered or housekeeping row from accidentally relabelling another response as the brief's response.

Algorithm:

1. Examine stored `TurnEnd` rows in descending **Sequence**, with raw UserPrompt/QueuedUserPrompt ownership and the existing semantic prompt span. Repeated boundaries for a split response stay associated with the same raw prompt and `ApiCallId`; a prompt need not lie after the immediately preceding duplicate `TurnEnd`.
2. Skip allowlisted background-acknowledgement/rules boundaries, recording their sequences/reasons. Do not use their assistant text as uncorrelated evidence. Do not stop after skipping just one.
3. For an ordinary candidate require the owning prompt's literal `DelegationReportFormatter.TaskMarker(task.Id)`, the existing dispatch floor, and `prompt.Sequence > RepliedAtSequence` when present. A prompt with known native `Timestamp <= DispatchedAt` is ineligible even if its boundary/ingestion time is recent. Keep the existing null-timestamp policy: such a row may be considered, but **only** its exact current-task marker can attribute it; another task's marker never can. Null `DispatchedAt` retains the legacy no-time-floor behavior. No global search for any `[antiphon-task:` or `[antiphon-report:` token.
4. A newer real prompt without this marker stops the backward search and follows the existing uncorrelated/no-text outcome for **its own** response. Do not jump across it to an older marked report. A candidate failing the reply fence returns `PreReplyBoundary`; do not look further back. A known pre-dispatch owning prompt cannot acquire current ownership via a later reminder or a promptless boundary.
5. For the selected task turn preserve `IsReportBoundary` cancellation handling, API-error handling, subagent grace, final-message grace and report classification. A selected cancelled/error task boundary is not permission to fall back to an older success. Housekeeping's cancelled/error response itself remains housekeeping. No new automatic stop or retry behavior.

This is a bounded search through the current dispatch's eligible task history, not a fixed “last N turns” heuristic. Batch metadata reads or page them; do not load all tool output or issue one full-transcript query for every boundary. Do not use `TranscriptTurnWindow.FindOwningPromptAsync` unmodified: its `(previous TurnEnd, this TurnEnd)` ownership window can be empty for split/duplicate boundaries, and its channel queue semantics are a separate contract.

### D-3. Preserve Claude's real notification continuation

A Claude notification response can be selected only when (a) walking past contiguous housekeeping reaches an eligible current-task marked prompt without crossing a real unmarked prompt, (b) that response's own assistant text has a valid current-task closing report token accepted by `TryFindReportToken`, and (c) the existing launch/notification `tool-use-id` pairing and subagent grace allow settlement. It may report done, blocked or failed; do not hardcode success. Select that continuation's boundary and response together. This preserves CARD-0046's final synthesized verdict.

A notification's body quoting a token is not an assistant report. An acknowledgement without its own closing token is skipped to the earlier task boundary. If only housekeeping exists, there is no task report and no uncorrelated-report incident. For Grok's measured completion envelope use the skip rule, not Claude's continuation exception. An unmarked ordinary task response still follows the existing nudge/classification ladder; D-3 adds no universal requirement for a report token to all legacy ordinary turns.

### D-4. Response identity and text bounds move with the boundary

Pass the selected boundary through `FinalMessageOf`, `ResolveFinalMessageState`, `BoundaryFacts`, narration accounting and nudge/report evidence. Sequence 272's `ApiCallId=b3df71fa-7880-4310-a941-4a80990bd8d0:161` must produce sequence 271; never reuse 276's `task-completed-01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7:0`.

Identity-bearing final text may arrive after its own TurnEnd, including after an intervening housekeeping row: accept only that selected ApiCallId inside the same effective task-owner span, capped by the next **real** prompt. Keep the current split-message grace measured from selected `end.CreatedAt`. A housekeeping acknowledgement cannot satisfy missing final text merely by being newer.

For null ApiCallId, disabled final-message grace, and grace-expired joined fallback, restrict the text to the selected physical response window, capped by the next raw turn-opening prompt **including a background notification/reminder**. Inert local-command/compaction records retain their existing semantics. Thus the fallback cannot concatenate the later acknowledgement. Narration/fallback bounds and identity-based late-text bounds are distinct on purpose. Test both the default setting and `FinalMessageGraceSeconds=0`.

### D-5. Recheck before the delivery watchdog fails on an incident

Incident path today: live `AgentSessionRuntime` TurnEnd/late-text handling or CARD-0288's deferred sweep calls `OnTurnEndAsync`; `ExtractMarkedTurnAsync` returns `UncorrelatedReport`; `RecordUncorrelatedReportAsync` records `DelegateReportUncorrelated` once per task via `UncorrelatedReportEvidence`; `FailNeverStartedAsync` pulls transcript, then arm 2 trusts that incident and calls `FailAsync` plus release/kill and the DeliveryFailure outbox. Phone-home recovery and the watchdog's own catch-up persist rows without calling settlement.

Change arm 2 to require a **fresh attribution verdict after catch-up**, not just an incident. Reuse the reply service's per-session settle lock and selector through an internal recheck entry point. It should settle a recovered candidate through the normal path, or return a typed outcome: still uncorrelated, deferred/no report, no longer open, or indeterminate/error. A bare `OnTurnEndAsync` call followed by checking status is insufficient: a valid report waiting for its final text still has open status.

- Keep the existing timeout, `Dispatched`-only scope, Pending-brief precedence, rules barrier, bind-refusal recovery and incident scope. The started test consumes the same allowlist, so Grok housekeeping alone does not prove dispatch delivery.
- Recheck before consuming an eligible incident; also recover an available marked report after catch-up when no incident exists. Deferred/no-report/indeterminate attribution withholds **arm 2**, not the separate genuine never-started arm.
- Keep the reply service's per-session settle lock through the fresh arm-2 judgment and its failure commit, using an internal callback for the dispatcher-owned write; release before kill/delivery. Do not call public `OnTurnEndAsync` recursively while holding that lock. Reload the dispatcher's tracked task, require the same task/session/dispatch/reply generation and still `Dispatched`, and commit failure through an explicit compare-and-set on the observed `ConcurrencyToken` and status, with its event/DeliveryFailure outbox in the same transaction. `AgentTask.ConcurrencyToken` is not an EF concurrency property today. If zero rows match, roll back and produce no Failed event, notification, consumer release or kill; do not refresh and retry failure blindly. Use the existing `FailAsync` rendering/event logic through a narrowly factored arm-2 conditional path, not a global entity mapping change. Ordinary reply settlement takes the same lock; a concurrent reply/requeue through another path invalidates the conditional write.
- Only a still-current uncorrelated response plus the existing eligible incident can authorize arm-2 failure. Catch-up/selector errors do not supply that verdict. If the optional reply dependency is absent, defer arm 2 with a diagnostic rather than asserting historical evidence is current.
- Existing historical incidents remain audit history. Do not delete incidents or auto-reopen tasks already Failed by the old version. The bug fix prevents/reconciles open attempts only.

Some old watchdog tests seed an incident but no assistant report (for example `a_task_whose_report_could_never_be_correlated_fails_instead_of_hanging`). Amend those fixtures to include the genuine unmarked assistant response they claim to represent. Keep their failure/kill/withhold-kill assertions; add a separate incident-only negative.

### D-6. Deferred recovery uses the same decision

`SettleDeferredReportsAsync` must use the selector's boundary/owning prompt and response identity for arms 0/1/subagent-grace, reply-watermark checks and `DeferredReportSweepMarks`. It must not return just because the newest raw boundary belongs to rules refresh or a cancelled housekeeping response. Arm 0's marker search must be limited to the selected eligible response, not any historic assistant row in the session. A changed selected boundary triggers handoff; another reminder alone must not masquerade as progress in the unmarked-nudge ladder. An in-flight settle is still skipped, and the reply service remains the settlement authority.

Add structured debug diagnostics for selected boundary, owning prompt, skipped boundaries/reasons and task ID, without logging prompt/report bodies. No new alert kind or schema. Update the session-runtime owner with the selection/housekeeping invariant and named tests in the Code slice.

### Rejected alternatives

- Accept all results from a bound session: loses the human-turn and reused-session identity guards.
- Only add `<system-reminder>` to `IsHousekeepingPrompt`: assigns the newer acknowledgement's ApiCallId to the old prompt.
- Search assistant text for the last closing token without prompt/floor checks: accepts stale tasks, retries and quoted material.
- Skip every Claude notification response: discards the existing final synthesized report path.
- Classify every `<system-reminder>` or provider `task_complete` as housekeeping: hides real prompts or Codex's only report boundary.
- Fix ingestion by deleting/renaming recorded prompts: loses evidence, affects idle/delivery, and does not repair already-persisted sessions.
- Delete incidents or raise the watchdog timeout: hides the attribution defect and leaves the wrong result path intact.

## Implementation slices

| Slice | Files | Deliverable and tests |
|---|---|---|
| S1 | New `server/Application/Services/TaskReportHousekeeping.cs`, new `TaskReportTurnSelector.cs`; `TranscriptPromptSpan.cs`; new `tests/Antiphon.Tests/Application/TaskReportHousekeepingTests.cs`; `TranscriptPromptSpanTests.cs` | Provider allowlist and classified raw/semantic prompts; candidate selection with current marker, dispatch/reply fences and real-prompt barriers. Pure allowlist tests and span integration controls. |
| S2 | `AgentTaskReplyService.cs`; new partial file `tests/Antiphon.Tests/Application/AgentTaskReplyC714Tests.cs` for existing `AgentTaskReplyIntegrationTests`; reduced fixture helper under `tests/Antiphon.Tests/TestHelpers/Card0714Transcript.cs` | Select and extract the correct response, preserve Claude continuation, error/cancel/grace semantics. Seed explicit sequence/identity evidence; caller receipt tests. |
| S3 | `AgentTaskDispatcher.cs`; `AgentTaskReplyService.cs` recheck entry; make `AgentTaskDeliveryWatchdogTests` partial and add `AgentTaskDeliveryWatchdogC714Tests.cs`; `CodexTranscriptNormalizerTests.cs`; `docs/session-runtime-invariants.md` | Watchdog fresh recheck and stale-context guard; shared deferred selection; Codex structural control; document invariant. Existing watchdog fixture corrections stay here. |

Commit slices before long runs. Code executes ordinary V/R; deliberate broken-production variants belong to post-land Mutation. No runner restart, deployment, board edit, historical task repair or source-code change is part of this Plan dispatch.

## Verification design

### Inspection

Bodies read, not just test names:

- `TranscriptPromptSpanTests`: all seven existing tests and seeding helpers; queued/native/null timestamp, wrapper and notification boundaries -> V-1, R-3/R-4.
- `AgentTaskReplyIntegrationTests`: queued brief/cap/unmarked queued barrier; split/final-response extraction helpers; `SeedSubagentFanOutAsync`, `SeedSubagentNotificationAsync`, announcement/pending/last-notification tests; `NewDeliveryFactory`, `AttachTerminal`, `SeedParentHistoryAsync`, `AssertParentReceivedNoteAsync` -> V-2/V-3/V-4/V-6, R-1/R-2/R-5/R-6/R-7.
- `AgentTaskDeliveryWatchdogTests`: never-started/reuse/Pending and incident arms; `CreateHarness`, `CreateArm0RaceHarnessAsync`, `SeedMarkedReportTurnAsync` shape and dispatcher catch-up seam -> V-5, R-8/R-9.
- `AgentTaskSettlementRaceTests`: in-flight/one-note, blocked-answer watermark and internal rules sweep tests -> R-4/R-9.
- `CodexTranscriptNormalizerTests`: real TUI fixture test and fixture reader; normalizer's flat/thread-item switches and `final_answer` identity -> V-4. This is code/fixture evidence, not a fresh claim about every available Codex CLI version.

Missing setup to implement: explicit raw provider kind on both session/task, a reduced transcript helper with arbitrary sequences and distinct native/store times, repeated housekeeping turns, and access to the same reply singleton from the watchdog harness. Reuse `DelegationTestServices.AddDelegationWorktreeGraph` / `AddGitWorkspaceService`; do not fork a hand-maintained DI graph. Use the existing partial reply class to retain delivery helpers. Watchdog tests use `[NotInParallel]` without a group, scoped assertions and `CatchUpOverride`; never freeze a real-timer queue clock. No live model, production runner or primary provider home is needed.

### Incident fixture

`Card0714Transcript` is a documented **reduced reconstruction from the investigation**, not a claim to include the full captured transcript/report. Keep the recorded chronology and identity. Use a per-test task ID with the same marker shape; retain the incident's literal response IDs, reminder and sequence gaps. Native times are offset together when a timeout test needs “now”; ingest all rows together with fresh CreatedAt for the catch-up case.

| Sequence | Kind / identity | Required material |
|---|---|---|
| 1, 10 | Grok rules prompt / end | Valid System queue-backed rules header; no task report. |
| 11 | UserPrompt | One flat spill-pointer line, current `[antiphon-task:<id8>]` three times. |
| 271 | AssistantText, `b3df71fa-7880-4310-a941-4a80990bd8d0:161` | Distinct multi-paragraph report, next-stage block `next: review`, closing `[antiphon-report:<id8> done]`. Assert its exact expected stored body after normal token processing. |
| 272 | TurnEnd, same ID | `end_turn`, native `15:39:46.465Z`. |
| 273 | UserPrompt | Verbatim five-line reminder below, native `15:39:46.493Z`. |
| 274 | Omitted gap | Investigation does not specify its content; do not invent it as evidence. |
| 275 | AssistantText, `task-completed-01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7:0` | Distinct short acknowledgement, no report token; native `15:39:59.789Z`. |
| 276 | TurnEnd, same reminder ID | `end_turn`, native `15:40:00.089Z`. |

```text
<system-reminder>
Background task "01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7" completed (exit code: 1).
Description: Green-run the three affected integration classes | Duration: 241.6s
Use get_command_or_subagent_output("01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7") to see the full output.
</system-reminder>
```

### Delivery inventory

No new transport is introduced. Changed producer selection is transcript catch-up/live TurnEnd -> selected report -> normal `SettleAsync` terminal/result/event transaction -> existing completion obligation/parent queue -> caller session. Durable identities remain task/root ID, report digest, SourceTaskId and the queue attempt baseline. The existing profile-v1 outbox contract is unchanged. Watchdog failure still uses its DeliveryFailure outbox only after an authorized failure commit.

`C714_Parent_receives_selected_report_once(bool busy)` runs through the real `SessionMessageQueueService` with the existing fake submitted-prompt adapter: seed prior caller history; for busy=true hold a caller turn open, confirm no report submission, then end it and flush; for false flush immediately. Assert exactly one complete queue body in a caller `UserPrompt` above its delivery baseline, correct selected-report digest/body, one completion, and no acknowledgement text. Re-run recovery/OnTurnEnd and assert no duplicate submission. This proves application producer-to-recipient receipt, not native PTY encoding. No new asynchronous persistence handoff is added, so existing outbox crash/enqueue recovery remains out of scope; test the newly affected catch-up-before-settle and stale-watchdog-before-failure cuts in R-8/R-9. A Sent flag or queue count alone cannot pass V-6.

### Proves it works now

All new integration method names below use prefix `C714_`; no automatic helper may append a report token to the acknowledgement.

| ID | Test / layer | Decisive expected outcome |
|---|---|---|
| V-1 | `TaskReportHousekeepingTests` (Unit), `TranscriptPromptSpanTests.C714_Grok_completion_is_housekeeping`, `.C714_Grok_housekeeping_does_not_count_as_started` | Eleven Unit executions: LF/CRLF positive (2), generic reminder (1), quoted/suffixed (2), incomplete/mismatched IDs (2), foreign provider (2), Claude notification identity (1), Codex no text exemption (1). Span keeps raw delimiter, removes only allowed meaningful prompt, retains Claude notifications. |
| V-2 | `AgentTaskReplyIntegrationTests.C714_Grok_batched_replay_uses_report_response(bool disableGrace)` | Both executions settle 271/272's exact report; `Succeeded`, Marked, next Review, one Completed, no Uncorrelated incident or nudge. False is red at baseline on status; true also pins acknowledgement-free joined fallback. |
| V-3 | Same class `.C714_Claude_ack_after_report_uses_report_response`, `.C714_Claude_notification_final_report_still_settles` | Later bare acknowledgement cannot replace report; separately, all paired subagents completed and the notification's own marked final verdict settles once. First is red at baseline on Result; second is a compatibility control. |
| V-4 | Same class `.C714_Codex_final_answer_uses_turn_id`; `CodexTranscriptNormalizerTests.C714_Metadata_cannot_create_a_housekeeping_prompt` | TUI-style final_answer/turn_id settles its own text. Insert existing-format compaction/response_item/unknown metadata around a real captured TUI turn; normalized UserPrompt/AssistantText/TurnEnd identities remain unchanged. Label inserted records synthetic controls. |
| V-5 | `AgentTaskDeliveryWatchdogTests.C714_watchdog_catchup_rechecks_before_failure(bool existingIncident)`, `.C714_sweep_recovers_marked_report_under_housekeeping` | CatchUpOverride supplies complete incident fixture; with and without pre-existing eligible incident, watchdog/deferred sweep recovers correct success on the first pass. No delivery Failed event/outbox/kill; only ordinary success release is permitted. Baseline incident=true fails; sweep baseline strands. |
| V-6 | `AgentTaskReplyIntegrationTests.C714_Parent_receives_selected_report_once(bool busy)` | Both eligibility states yield complete caller receipt of the selected report, once; no acknowledgement or duplicate report. |

### Guards the regression

| ID | New test(s), all in the partial reply class unless qualified | Decisive assertion |
|---|---|---|
| R-1 | `C714_Grok_repeated_housekeeping_is_skipped`; `C714_Grok_rules_then_only_completion_cannot_settle` | Walk multiple reminders/rules turns, not just one; housekeeping alone never settles or records uncorrelated evidence, including quoted task/report tokens in a backed rules turn. |
| R-2 | `C714_Genuine_unmarked_turn_is_barrier(string kind)` (UserPrompt and QueuedUserPrompt); `C714_Codex_unmarked_user_turn_is_barrier`; repair probes `C714_Grok_unmarked_prompt_before_reminder_is_barrier`, `C714_Grok_unmarked_queued_prompt_before_reminder_is_barrier`, `C714_Claude_unmarked_prompt_before_notification_is_barrier` | Earlier marked report plus a later genuine completed unmarked turn remains uncorrelated, never success on older text. Codex includes reminder-looking ordinary text. A real unmarked prompt that shares the turn with a following Grok completion reminder or a Claude task-notification (answer has no closing token) is the same barrier. |
| R-3 | `C714_Current_report_is_chosen_after_old_task_history`; `C714_Predispatch_current_marker_is_rejected(bool equalFloor)`; `C714_Old_task_marker_is_never_current(int timeShape)` (before/after/null timestamp) | Reused session chooses only its current report. Old marker never suffices; even the current ID on a prior attempt cannot pass known native time at/below dispatch, despite higher sequence/fresh CreatedAt. In the after/null negative fixtures put a syntactically valid **current** closing token in the assistant response to the **old** prompt, so the prompt-identity gate is independently necessary; assert no result/nudge/completion attribution. |
| R-4 | `C714_Reply_watermark_prevents_stale_report` plus existing `AgentTaskSettlementRaceTests` | Reminder boundary above reply watermark cannot revive report whose owning prompt is at/below it; task remains Working awaiting answer, no extra note. |
| R-5 | `C714_Grok_selected_report_waits_for_own_text`; `C714_Late_report_text_after_reminder_keeps_own_api_id` | Selected boundary initially lacks its own final text: remain open/no result/no uncorrelated incident within grace despite ack text. Append final text and re-enter: exact report settles. Separate case appends selected-ID text after reminder boundary within the same logical owner span. |
| R-6 | `C714_Selected_error_or_cancel_does_not_fall_back(bool apiError)` | New current-task error/cancel boundary plus reminder must follow original error/cancel semantics; an older done report never wins. Run existing API-error and cancellation controls too. |
| R-7 | V-2 grace-disabled argument and V-3 Claude control | Neither null/disabled-grace fallback nor Claude notification inheritance admits an acknowledgement as Result. |
| R-8 | Watchdog `.C714_watchdog_deferred_report_does_not_fail`, `.C714_watchdog_true_uncorrelated_still_fails`, `.C714_housekeeping_only_still_never_started`, `.C714_ack_only_with_stale_incident_is_not_uncorrelated`, `.C714_sweep_final_grace_uses_selected_boundary` | Historical incident cannot kill a deferred/ack-only attributed span; genuine unmarked report still takes arm 2; no real prompt still takes arm 1. Sweep expires selected response's grace, not reminder's grace. |
| R-9 | Watchdog `.C714_watchdog_does_not_overwrite_live_settlement`; V-6 repeated entry; existing settlement race class | One method exercises two cuts with independent fixtures: (1) pause after suspect load but before recheck lock, settle through the same singleton, then resume: no Failed write/outbox/kill, one completion/note; (2) pause after fresh uncorrelated judgment before conditional write, change the attempt through a separate context (new token/status/session ownership as a requeue would), then resume: zero-row CAS, no failure side effect, new attempt intact. Do not try to enter the same settle lock from a hook already holding it. Use deterministic barriers, not sleeps. |

Roster: reply partial **27 executions** (20 named methods including arguments; repair 1 added the three same-turn barrier probes; repair 2 added `C714_Claude_auto_compaction_continuation_settles`); watchdog partial **9 executions** (8 methods including arguments); span **2**; new Unit **11**; Codex normalizer **1**. Report actual expanded counts. Compatibility controls need not fail at baseline; the principal Grok replay and watchdog incident tests must assert production outcomes that do fail on `38fb9897`. Code implements these tests and ordinary green evidence; Mutation performs the planned deliberate red/restore/green cycles. Do not claim baseline red was observed by this Plan stage.

### Guard inventory and positive controls

Each row maps one independently bypassable guard to one compiling production mutation and one exact decisive method. Mutation runs the method using `/*/*/<Class>/<Method>` (argument expansion is allowed), proves the named assertion red, restores source/timestamps, then proves green. No whole-class mutation runs. `Reply` below means `AgentTaskReplyIntegrationTests`; `Watchdog` means `AgentTaskDeliveryWatchdogTests`.

| Guard | Plan reference | PC | Compiling defect -> exact red witness |
|---|---|---|---|
| G-1 Envelope allowlist | D-1 | PC-1 | Accept any leading `<system-reminder>` -> `TaskReportHousekeepingTests.C714_unknown_system_reminder_is_real_prompt` wrongly classifies ordinary text. |
| G-2 Provider gate | D-1 | PC-2 | Remove Grok-kind predicate -> `TaskReportHousekeepingTests.C714_grok_shape_on_other_provider_is_real_prompt` loses a foreign-provider real prompt. |
| G-3 Boundary skip | D-2 | PC-3 | Always select newest raw TurnEnd -> `Reply.C714_Grok_batched_replay_uses_report_response` fails status or exact Result. |
| G-4 Selected-response/fallback isolation | D-4 | PC-4 | Disable next-housekeeping cap for joined fallback -> `Reply.C714_Grok_batched_replay_uses_report_response` grace-disabled argument includes acknowledgement. |
| G-5 Current-task identity | D-2 | PC-5 | Accept any task marker instead of exact ID -> `Reply.C714_Old_task_marker_is_never_current` post-dispatch/null cases wrongly settle. |
| G-6 Dispatch floor | D-2 | PC-6 | Admit known pre-dispatch prompts (remove eligibility time check, including any raw-candidate equivalent) -> `Reply.C714_Predispatch_current_marker_is_rejected` wrongly settles. |
| G-7 Reply floor | D-2 | PC-7 | Bypass candidate owning-prompt reply-watermark check -> `Reply.C714_Reply_watermark_prevents_stale_report` re-settles stale report. |
| G-8 Real-prompt barrier | D-2 | PC-8 | Continue backward past an unmarked real prompt -> `Reply.C714_Genuine_unmarked_turn_is_barrier` wrongly succeeds. |
| G-9 Cancel protection | D-2 | PC-9 | Skip selected task's cancelled end and continue to older success -> `Reply.C714_Selected_error_or_cancel_does_not_fall_back` cancel argument wrongly succeeds. |
| G-10 API-error protection | D-2 | PC-10 | Skip selected task's API-error end and continue to older success -> same exact method, apiError argument wrongly succeeds. |
| G-11 Own-text grace | D-4 | PC-11 | Use reminder text/identity to satisfy selected final-message grace -> `Reply.C714_Grok_selected_report_waits_for_own_text` prematurely changes status/result. |
| G-12 Claude continuation | D-3 | PC-12 | Skip every notification boundary, even its own marked verdict -> `Reply.C714_Claude_notification_final_report_still_settles` loses the final verdict. |
| G-13 Fresh watchdog attribution | D-5 | PC-13 | Trust eligible incident without post-catch-up recheck -> `Watchdog.C714_watchdog_catchup_rechecks_before_failure` incident=true writes Failed/outbox. |
| G-14 Deferred is not uncorrelated | D-5 | PC-14 | Treat still-open deferred/no-report outcome as uncorrelated -> `Watchdog.C714_watchdog_deferred_report_does_not_fail` writes Failed. |
| G-15 Stale task write | D-5 | PC-15 | Remove status/token/attempt predicates from the arm-2 conditional update, leaving only task ID -> `Watchdog.C714_watchdog_does_not_overwrite_live_settlement` second cut overwrites the new attempt or writes a failure side effect. This is an explicit conditional-update guard; do not rely on nonexistent EF concurrency mapping. |
| G-16 Sweep boundary parity | D-6 | PC-16 | Restore newest-raw-boundary identity for arm 1 -> `Watchdog.C714_sweep_final_grace_uses_selected_boundary` fails to process the selected report's expired grace. |
| G-17 Same-turn UserPrompt barrier | D-2 | PC-17 | Drop `UserPrompt` from `IsTurnOwningKind` so a human prompt sharing a completion turn is not the owner -> `Reply.C714_Grok_unmarked_prompt_before_reminder_is_barrier` wrongly succeeds. |
| G-18 Same-turn QueuedUserPrompt barrier | D-2 | PC-18 | Drop `QueuedUserPrompt` from `IsTurnOwningKind` -> `Reply.C714_Grok_unmarked_queued_prompt_before_reminder_is_barrier` wrongly succeeds. |
| G-19 Unmarked notification owner | D-3 | PC-19 | In `TryClaudeContinuationAsync`, return null before the in-turn owner is judged when the answer has no closing token -> `Reply.C714_Claude_unmarked_prompt_before_notification_is_barrier` wrongly succeeds. |
| G-20 Inert compaction owner | repair 2 | PC-20 | In the inert-housekeeping branch, return null for a marked in-turn owner (apply the Grok wait) -> `Reply.C714_Claude_auto_compaction_continuation_settles` stays Dispatched. |

Guards=20, mapped=20, missing=0, duplicate PC maps=0. Repair 1 added G-17..G-19 for the same-turn unmarked barrier. Repair 2 added G-20: an inert compaction or local-command boundary settles an eligible marked in-turn owner, and the Grok allowlist still waits. These stay pending for SourceLanding Mutation. Existing settlement idempotence and caller delivery guards are unchanged and covered by ordinary controls; this plan creates no new receipt matcher or delivery persistence guard. Test helper names for the eleven Unit executions must match the PC witnesses above; use separate named methods for the remaining positive/negative groups.

### Out of scope

- Native PTY input, transcript normalizer changes, channel matching, working/idle semantics and new provider protocol guesses: no change at those seams.
- Historical Failed-task recovery, live incident deletion, retesting the original CARD-0697 code, or automatically rerunning its delegate: requires a separate operational action.
- E2E browser, external sites, real model spend, whole integration assembly and fresh delivery-outbox fault matrix: no changed surface there. Existing caller receipt controls exercise the affected report selection through the real application queue.
- Positive-control production mutations before land: ordinary Review checks the design; SourceLanding Mutation owns execution and restoration.

### Cost

Estimates, not measured timings: ordinary Code checkpoint floor **29 minutes** below, plus approximately **70-100 minutes** authoring = **99-129 minutes** for dispatch budgeting. Post-land Mutation floor: 20 method-scoped red/restore/green cycles (PC-1..PC-20) at roughly 7 minutes each plus 8 minutes setup = **148 minutes**. No claimed speedup from a measured baseline; savings come from one Antiphon.Tests build reused across six bounded groups instead of one rebuild per case, and no broad integration assembly. Unit is the standard repository lane; integration groups below target only the changed attribution invariants.

Use `scripts/run-checkpoint.ps1` from the worktree, sequentially, in foreground. Linux defaults `UseAppHost=false` (CARD-0671); no dotnet shim. Example CP-2: `pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c714/ -NoBuild -Filter '/*/*/(AgentTaskReplyIntegrationTests*)|(AgentTaskDeliveryWatchdogTests*)|(TranscriptPromptSpanTests*)/C714_*' -MinExecuted 38 -Expect AgentTaskReplyIntegrationTests,AgentTaskDeliveryWatchdogTests,TranscriptPromptSpanTests -ResultsRoot .antiphon/c714-checkpoints`. Use a fresh results path on rerun, actual executed roster/TRX and one report line per CP. CP-1 must explicitly expect `TaskReportHousekeepingTests`; CP-7 expects the existing class and `C714_Metadata_cannot_create_a_housekeeping_prompt`. Check all named cases, not just Min. All rows share `After=all`, so output reuse is valid. Any extra baseline/debug run needs its stated reason. Every Git command needs a finite timeout on server2: the checkpoint script's line 199 calls unbounded `git rev-parse HEAD`; run its PowerShell invocation from a task-local wrapper defining `function global:git { & timeout 20s /usr/bin/git @args }` (first verify that executable path), then invoke the script in the same PowerShell process. This is invocation setup, not a tracked script edit or permission to time-limit the whole test run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c714/` | unit | `/*/*/*/*[Category=Unit]` | V-1 pure allowlist | all 11 new Unit executions plus selected Unit lane, 0 failed/skipped in new class | 11 | 6 |
| CP-2 | all | CP-1 | attribution | `/*/*/(AgentTaskReplyIntegrationTests*)\|(AgentTaskDeliveryWatchdogTests*)\|(TranscriptPromptSpanTests*)/C714_*` | V-1–V-6 application; R-1–R-9; G-20 | all 38 integration executions (34 plus the three repair-1 probes plus the auto-compaction continuation), 0 failed/skipped | 38 | 8 |
| CP-3 | all | CP-1 | reply-controls | `/*/*/AgentTaskReplyIntegrationTests/(a_turn_opened_by_a_queued_brief_settles_the_task*)\|(a_queued_prompt_after_the_brief_caps_the_report_window*)\|(a_queued_prompt_without_the_marker_is_an_uncorrelated_report_not_a_settle_on_the_brief*)\|(a_turn_that_launched_background_agents_does_not_settle_on_its_announcement*)\|(a_task_notification_turn_is_not_an_uncorrelated_report*)\|(the_last_subagent_notification_settles_the_task_with_the_verdict*)\|(a_subagent_that_never_reports_settles_after_the_subagent_grace*)\|(the_final_messages_arrival_settles_the_task_with_the_report*)\|(a_turn_end_with_no_api_call_id_settles_as_it_always_did*)\|(a_Codex_final_answer_with_the_turn_identity_settles_cleanly_without_a_warning*)\|(a_Codex_final_answer_that_arrives_after_its_turn_end_settles_cleanly*)\|(a_retryable_api_error_defers_the_task_and_never_stores_the_error_text*)` | V-3/V-4; R-2/R-5/R-6/R-7 | all 12 named methods, 0 failed/skipped | 12 | 5 |
| CP-4 | all | CP-1 | prompt-span | `/*/*/TranscriptPromptSpanTests/*` | V-1; R-3/R-4 | all 7 existing + 2 new methods, 0 failed/skipped | 9 | 1 |
| CP-5 | all | CP-1 | watchdog-controls | `/*/*/AgentTaskDeliveryWatchdogTests/(a_task_whose_report_could_never_be_correlated_fails_instead_of_hanging*)\|(a_stale_uncorrelated_incident_does_not_take_arm_2*)\|(a_stale_uncorrelated_incident_with_a_sent_brief_is_left_alone*)\|(a_working_session_with_a_pending_brief_is_neither_failed_nor_killed*)\|(arm_2_on_a_working_session_fails_the_task_but_does_not_kill*)\|(a_reused_session_whose_new_brief_never_landed_is_failed*)\|(a_queued_brief_after_dispatch_is_left_alone*)\|(a_cancelled_end_is_skipped_by_the_deferred_report_sweep*)\|(arm_0_skips_a_boundary_at_or_below_the_reply_watermark*)\|(arm_0_still_runs_when_the_grace_hatches_are_zeroed*)\|(arm_0_while_live_on_turn_end_in_progress_enqueues_one_parent_note*)\|(a_second_arm_0_pass_on_an_unchanged_settled_boundary_is_a_noop*)` | V-5; R-4/R-6/R-8/R-9 | all 12 named methods, 0 failed/skipped | 12 | 3 |
| CP-6 | all | CP-1 | settlement-races | `/*/*/AgentTaskSettlementRaceTests/*` | R-1/R-4/R-9 | all existing methods including both rules arguments, 0 failed/skipped | 9 | 3 |
| CP-7 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c714-runner/` | codex-normalization | `/*/*/CodexTranscriptNormalizerTests/*` | V-4 structural control | all 15 existing methods and 1 new C714 metadata method, 0 failed/skipped | 16 | 3 |

The backslashes before pipes above are Markdown table escapes only; pass literal `|` in the shell filter, as in the CP-2 command. CP-4 deliberately reruns the two new span cases while protecting the unchanged seven-case contract. Remove only this producer's recorded bin-c714/bin-c714-runner outputs after verification.
