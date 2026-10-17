# CARD-1320: Keep-six in-flight reminder

Date: 2026-10-17. Plan task: `e22d2834`.
Source inspected: `7b3b9adbe98b18ae306b73cfb220331dc433f801` on `feat/card-task-e22d2834`.
Card: CARD-1320, board Antiphon, id `0d415fa9-abad-4644-b6e1-84d75eb44aad`. Label master-only.
Lane read at plan time from the live API: `GET /api/runner-defaults` revision 2, provenance Human; `GET /api/session-runners` lists more than one eligible host, including a draining Linux runner. This plan pins no host and no platform. Checkpoints name the test lane only.

Verification is folded into this plan. The next stage is code. Each slice is independently landable onto master. Nothing in this plan targets an RC branch.

This plan adds no session that waits for input. The reminder is one WhenIdle message to an existing orchestrator session. Parking stays as it is (CARD-1083): with parking disabled there is no automatic release deadline, and this card does not add one.

## Ground truth

| The card assumes | The code does | What this plan uses |
|---|---|---|
| Section 8 of `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS` is where the toggle is shown. | `OrchestratorInstructionsRenderer` section 8 is already "Operator standing instructions": at most 10 config lines of 300 characters (`Delegation:OrchestratorInstructions:StandingInstructions`). The file is one fleet document. Its hash is the rendered body. Runner occupancy is on the snapshot and is excluded from the delta (`OrchestratorInstructionsDeltaTests.Equal_snapshots_and_occupancy_only_differences_produce_no_delta`). | A rendered section 9, settings only. Live counts stay out of the file so a reminder tick cannot rewrite the version. Section 8 is not the store. |
| In-flight is Queued, Dispatched, Working, and Blocked. | `DispatchConcurrencyPolicy.IsOpen` is Queued + Dispatched + Working. `IsParallel` is Dispatched + Working. Blocked is outside both, and outside `MaxOpenTasks` (`docs/orchestration-loop.md`, `DelegationOpenGate`). The pipeline's `investigateBacklog` already drops cards with an open blocker and lists them as held (CARD-0100). | Count `occupancy.Open` for the orchestrator's project. Do not count Blocked. Dispatchable cards reuse the pipeline backlog rules, then keep only that agent's board. |
| "Up to 6" is a new target. | `Delegation:MaxOpenTasks` already defaults to 6 and is imported once as the LegacyOpen absolute cap. Later values come from dispatch-concurrency, projected on `GET /api/agent-tasks/pipeline`. | The operator target defaults to 6 and is lowered to the effective absolute ceiling. It does not replace `MaxOpenTasks`. |
| The orchestrators home page lists orchestrators with a toggle. | `/orchestrator` renders one fleet `OrchestratorPanel`: pause, resume, and tick. That tick dispatches cards (`OrchestratorEndpoints`, `OrchestratorService.PollTickAsync`). Declared orchestrators are standing agents with the `orchestrator` bundle (`OrchestratorInstructionsRecipients`, `ProjectSetupService.FindDeclaredOrchestrator`). | A new panel on that page, one row per standing orchestrator agent. Pause, resume, and tick stay, and this card does not call them. |
| A settings change tells the orchestrator. | `ChannelPreamble.OrchestratorInstructionsChangedBody` is a WhenIdle system note that says to reply `NO_REPLY`. Recipients are running PtyHost sessions of non-pool, non-specialist agents that carry the orchestrator bundle, plus running orchestrator-kind tasks when notify is All. | Keep that note for a section 9 change. The reminder is a second message and does not say `NO_REPLY`. |
| The scheduler is Hangfire on the main instance. | Recurring jobs register in `Program.cs` only when `Hangfire:ServerEnabled` is true. Storage is in-memory; Postgres is what survives a restart (`docs/bootstrap.md`, Hangfire jobs). Test hosts force the worker off. | One recurring job behind that same gate. Durable clocks live on the new row and on `SessionQueuedMessage`. |
| The server might dispatch. | Create is `AgentTaskService.CreateAsync` plus `DelegationOpenGate`. Spend still needs the existing acceptSpend / Spawn rules. | Not built. See below. |

## Decisions

### D-1. Default off for every orchestrator, including a new one

`enabled` defaults to false whether the agent already exists or is created later. No migration backfill turns anyone on. The first PUT that sets enabled true stamps `EnabledAt`.

Rejected: default on for new orchestrators. The card allows that only if the operator wants it, and this plan does not take that want as given. A new AlwaysOn seat must not start receiving dispatch reminders until someone turns the row on.

### D-2. One row per standing orchestrator agent

The population is the standing half of `OrchestratorInstructionsRecipients`: an agent that carries bundle key `orchestrator`, is not a pool delegate, and is not a standing specialist. `AlwaysOn` is not required. A sub-orchestrator (`AgentTaskKind.Orchestrator`) is not a row. Two agents on one project share that project's open count and each get their own reminder if each is enabled. The operator turns on the seat that should pull cards.

Project scope is `Agent.BoardId` to `Board.ProjectId`. Open-task count is that project, because `MaxOpenTasks` is per project. Dispatchable cards are that board only. An agent with no board is listed, stores a toggle, and is never reminded.

Rejected: one row per project via `FindDeclaredOrchestrator`, which hides every orchestrator except the first. Rejected: counting only tasks parented by the session, which would miss work already in the project cap.

### D-3. The counted population is the existing open cap, not Blocked

`countedOpen` is `DispatchConcurrencyOccupancyDto.Open` for that project (non-specialist Queued + Dispatched + Working). Blocked is reported nowhere in this shortfall.

`effectiveTarget` is `min(storedTarget, ceiling)`. `storedTarget` is an integer from 1 to 24, default 6. `ceiling` is the project's effective `MaxParallel` in LegacyOpen. In SeparateQueues it is `MaxParallel + MaxQueued` when `MaxQueued` has a value, and `MaxParallel` when `MaxQueued` is null. The ceiling only lowers the stored target.

`shortfall` is 0 when `countedOpen >= effectiveTarget` or when the board has no dispatchable card. Otherwise it is `effectiveTarget - countedOpen`.

The reminder names host `remaining` and per-stage `parallelRemaining` from the same pipeline response as constraints. They do not change `shortfall`. Stages run in parallel, and a host's seats are fleet-wide; folding either number into K would under-fill or would count another project's seats as this project's card count.

Rejected: counting Blocked. Rejected: subtracting every stage cap from 6.

### D-4. Dispatchable cards share the pipeline backlog rules

Extract the candidate rules in `AgentTaskPipelineStatusService` (Backlog, not archived, board not archived, column not terminal, `OwnerSessionId` null, no open blocker, label `post-land-verification` excluded, card not already bound to a Queued, Dispatched, Working, Blocked, or Succeeded stage task) into one helper that `GetAsync` and keep-six both call. Rank stays `CardRanking`. The reminder lists at most five identifiers, lowest rank first. `investigateBacklog.items` is only the project-wide top five, so keep-six must not filter that already-truncated list down to a board.

### D-5. Grace is three minutes from the later of enable, create, and completion

A reminder is eligible when shortfall is positive, the toggle is on, no hold is active, and `now` is at least three minutes after the latest of `EnabledAt`, the newest non-specialist `AgentTask.CreatedAt` in the project, and the newest non-specialist `AgentTask.CompletedAt` in the project. Turning the toggle on therefore waits three minutes even when the shortfall is already old. A later create or completion starts the three minutes again.

Last create and last completion are read from tasks, not copied onto the row. `LastReminderEnqueuedAt` is stored, because a sent message is not a task timestamp.

### D-6. One outstanding reminder, then a fifteen-minute backoff

The job enqueues at most one Pending `SessionQueuedMessage` with note header `[keep-six]` for that session. A later tick does not enqueue another while that row is Pending. After it leaves Pending, the next enqueue waits until fifteen minutes after `LastReminderEnqueuedAt`, unless a task in the project was created after that enqueue. A create after the reminder also restarts D-5's three minutes, so the following reminder is not immediate.

The message uses `MessageSendMode.WhenIdle`, `QueuedMessageOrigin.System` (already one-per-turn, not batched), `deliverIfIdle: true`, and note header `[keep-six]`. No new origin. The body is produced by one helper and contains the counts, up to five card identifiers, the sentence "Antiphon has not dispatched", and the hold script. It does not contain `NO_REPLY`.

The session is that agent's persistent session when its status is Running, its backend is PtyHost, and `CardId` is null. Otherwise the job skips. That is `OrchestratorInstructionsRecipients.Accept` without the version check.

Disabling the toggle cancels a still-Pending `[keep-six]` message for that session.

The job takes the Postgres advisory lock `antiphon.keep-six.reminder` for the tick, following `DelegationOpenGate`'s lock shape, so two overlapping fires cannot both enqueue.

Rejected: a new `QueuedMessageOrigin`. Every origin has delivery rules, and System already has the right ones. Rejected: appsettings knobs for grace and backoff. The card's ranges are closed here as 3 and 15.

### D-7. A hold is an explicit row with an expiry, not a guessed quota

`HoldReason` is required, 1 to 200 characters. `HoldUntil` is required, in the future, and at most seven days ahead. While `now < HoldUntil` the job sends nothing. A past hold is ignored and the UI shows it as expired. Model-availability holds, Grok quota, and rollout drains are not inferred. They already appear in section 4 and on the attention feed. The reminder text tells the orchestrator to apply pipeline rules; it does not decide that a single alias hold blocks the board.

Ack stores `LastAcknowledgedAt` and does not enqueue a second message and does not cancel the pending one. Suppression without a dispatch is the hold, not the ack.

Operator PUT of the toggle requires provenance `Human` and a reason, with `expectedRevision` (0 creates the row, a mismatch is 409). Hold and ack accept that same Human write, or the orchestrator's own `X-Antiphon-Task-Token` when the token's standing agent is this agent. Another agent's token is 403. The page toggle does not grow an operator-token gate; pause and tick on this page do not have one. Instructions refresh keeps its operator token.

Rejected: auto-suppress on any model hold.

### D-8. Section 9 is the setting, and it is omitted while every row is off

`OrchestratorInstructionsSnapshot` gains an optional keep-six list at the end, default empty, so existing positional constructors still compile. The renderer appends `## 9. In-flight target` only when at least one row is enabled or has a hold. Each such row is one line: name, on or off, stored target, hold reason and until. A final line counts the orchestrators left off. Live open, shortfall, and timestamps are not rendered. An empty list leaves the body without `## 9`, so today's renderer hashes stay put until the feature is used.

A PUT or a hold change calls `OrchestratorInstructionsService.Signal`. That is the existing settings-changed note, which still says `NO_REPLY`. The reminder is separate and waits out D-5. Ack and the reminder tick do not signal.

The delta gains one keep-six line inside the existing 7-line, 120-character, 700-character caps. Occupancy-only changes stay an empty delta.

### D-9. Hangfire registration is the existing server worker

`HangfireConfiguration` registers `antiphon:keep-six` at `* * * * *` UTC when `Hangfire:ServerEnabled` is true. There is no second enabled flag. The per-agent toggle is the switch; the job no-ops when nobody is enabled. Tests call `ExecuteAsync` on the job. They do not start a worker.

### D-10. Docs and the bundle point at the setting without rewriting pinned policy

Add a new bullet in `AGENTS.md`. Do not edit the line that starts `- An orchestrator working a board`; `StandingPipelinePolicyDocumentationTests` reads that line whole.

Add a short subsection in `docs/orchestration-loop.md` immediately after the standing pipeline policy and before `### Post-land miss retrospective`. Keep every phrase that test pins. Do not add the stale strings it forbids (`three-route`, a session-runners route, a runner-defaults route).

Add one short paragraph to `server/Bundles/orchestrator.md` inside the standing-policy window, after the depth-cap sentences: a keep-six reminder names the shortfall; dispatch up to that shortfall under the same pipeline rules; declare a hold with `scripts/keep-six.ps1`; the server does not dispatch. The bundle is 13482 bytes today and `InstructionBundleTests.orchestrator_bundle_points_to_operational_autonomy_without_growing` caps it at 14310. Stay under that cap. Do not raise it. Recompute the SHA-256 in `CheckpointRepeatDocumentationTests.repeat_budget_reaches_code_briefs_without_bundle_growth` in the same commit as the bundle bytes. The comment on that pin already says to recompute it when `orchestrator.md` changes.

`scripts/keep-six.ps1` is ASCII and sends `X-Antiphon-Task-Token`. `-Hold -Reason -Until` and `-Ack` are the only verbs. It does not create a task.

## What is not built

- No auto-dispatch. No call to `OrchestratorService.PollTickAsync`, `AgentTaskService.CreateAsync`, or any Spawn / acceptSpend path. No change to `OrchestratorTickHostedService`.
- No reminder to a sub-orchestrator task, a worker, a specialist, a pool delegate, or a card session.
- No count of Blocked tasks, and no count of another project's tasks.
- No new message origin, no new SignalR event, and no operator-token gate on the page toggle.
- No default-on migration, no appsettings grace/backoff, and no Windmill or Windows scheduled task.
- Section 8 standing lines are not a keep-six store.
- No positive-control matrix. Post-land Mutation is outside this plan's ordinary scope.

## Slices

Land in order. Each slice's commits are the failing test, then the implementation. Run that slice's checkpoint group only after the implementation commit. A red checkpoint is fixed and rerun as the same CP.

### S1. Setting, API, and section 9

Files: `server/Domain/Entities/KeepSixOrchestrator.cs`; `AppDbContext` DbSet and mapping; one EF migration `AddKeepSixOrchestrator` from `dotnet ef migrations add` (do not hand-write it); `server/Application/Dtos/KeepSixDtos.cs`; `server/Application/Services/KeepSixService.cs`; `server/Api/Endpoints/KeepSixEndpoints.cs`; map the group from `Program.cs`; snapshot, builder, renderer, and delta as D-8.

Row columns: `AgentId` primary key and FK to `Agent` with cascade delete; `Enabled`; `Target`; `Provenance`; `Reason`; `Revision`; `UpdatedAt`; `EnabledAt`; `HoldReason`; `HoldUntil`; `LastReminderEnqueuedAt`; `OutstandingMessageId`; `LastAcknowledgedAt`.

Routes, plural, so they do not collide with `/api/orchestrator/state`:

- `GET /api/orchestrators/keep-six` lists the population. A missing row is enabled false, target 6, revision 0, and writes nothing.
- `PUT /api/orchestrators/{agentId}/keep-six` with `{ expectedRevision, enabled, target, reason, provenance }`.

S1 does not fill shortfall and does not enqueue. GET may return null counts until S2.

Red first: `KeepSixSettingsTests.get_lists_a_declared_orchestrator_as_off_with_target_six_and_creates_no_row` expects HTTP 200. Before the route exists the host returns 404.

### S2. Shortfall

Files: `server/Application/Services/PipelineBacklogSelection.cs` extracted from the pipeline service; `AgentTaskPipelineStatusService` calls it; `server/Application/Services/KeepSixShortfall.cs`; GET fills `countedOpen`, `effectiveTarget`, `dispatchableBacklog`, `shortfall`, `topCards`, `lastDispatchAt`, `lastCompletionAt`, and `badge`.

`badge` is `off`, `healthy` (enabled and shortfall 0), or `under-target` (enabled and shortfall > 0). An active hold does not change the badge.

Red first: `KeepSixShortfallTests.legacy_open_counts_queued_dispatched_and_working_and_not_blocked` fails while the helper returns 0.

### S3. Reminder, hold, and ack

Files: `server/Infrastructure/Agents/KeepSixReminderJob.cs`; `HangfireConfiguration.AddOrUpdateKeepSixJob`; the `ServerEnabled` block in `Program.cs`; hold and ack on the service and endpoints; `KeepSixReminderText` for the body.

- `POST /api/orchestrators/{agentId}/keep-six/hold` with `{ reason, until }`.
- `DELETE /api/orchestrators/{agentId}/keep-six/hold`.
- `POST /api/orchestrators/{agentId}/keep-six/ack`.

Red first: `KeepSixReminderTests.shortfall_past_grace_enqueues_one_whenidle_message_and_creates_no_task` fails while the job enqueues nothing.

### S4. Bundle, docs, and script

Files listed in D-10, plus `docs/bootstrap.md` (one Hangfire table row for `antiphon:keep-six`) and `docs/ops-http.md` (the four routes).

Red first: `KeepSixDocumentationTests.bundle_says_the_server_does_not_dispatch_and_names_the_hold_script` fails until the paragraph and the script exist. Update the bundle SHA in the same commit as the bundle bytes.

### S5. Orchestrators page

Files: `client/src/api/keepSix.ts`; `client/src/features/orchestrator/KeepSixPanel.tsx`; mount it in `OrchestratorPage` on the cards tab, above `OrchestratorPanel`. Leave the pause/tick caption in place: that panel still governs card auto-dispatch, and this one only reminds.

Each row shows the agent name, an "Auto-processing" switch, the badge, `N of T`, the dispatchable count, last reminder, last dispatch, and the active hold reason. The switch PUTs provenance `Human` and a fixed reason `operator toggle`. Target is a number input in the same range as D-3.

Live updates: `refetchInterval` 5000, matching `useOrchestratorState`, and add query key `['orchestrators', 'keep-six']` to the existing `CardChanged`, `AgentTaskChanged`, and `OrchestratorTick` maps in `useSignalRInvalidation.ts`. No new hub event. A mutation invalidates the same key.

Red first: `KeepSixPanel.test.tsx` cannot find `data-testid` `keep-six-badge` until the panel renders it.

## Verification design

Ordinary scope is the checkpoint table. One narrow filter per behaviour. No full assembly, no client suite, no new checkpoint-tool test, so `Get-NamespaceCensus` `selected` stays unchanged. Off Windows the checkpoint tool adds `UseAppHost=false`; these rows do not set it. Integration rows use `AntiphonWebAppFactory` the way `OrchestratorInstructionsEndpointTests` does, with their own agents, boards, and projects. The test host leaves `Hangfire:ServerEnabled` false.

V-1 `KeepSixSettingsTests.get_lists_a_declared_orchestrator_as_off_with_target_six_and_creates_no_row` GET returns one off row, target 6, revision 0, and leaves the table empty.

V-2 `KeepSixSettingsTests.put_human_enables_and_stores_target_reason_and_revision` a Human PUT persists enabled, target, reason, and revision 1, and a second PUT with the stale revision is 409.

V-3 `KeepSixSettingsTests.put_rejects_a_non_human_provenance` provenance other than Human is 400 and writes nothing.

V-4 `KeepSixSettingsTests.put_rejects_a_target_outside_one_to_twenty_four` target 0 and target 25 are rejected.

V-5 `KeepSixSettingsTests.put_on_a_pool_delegate_or_specialist_is_not_found` a pool delegate and a standing specialist are 404.

V-6 `KeepSixProjectionTests.all_off_omits_section_9_and_leaves_the_rendered_body_unchanged` an empty keep-six list renders no `## 9` and matches the render of a snapshot built without the field.

V-7 `KeepSixProjectionTests.an_enabled_row_appends_section_9_after_section_8_without_live_counts` section 9 follows section 8, names the stored target and the hold, and contains neither a live open count nor a shortfall.

V-8 `KeepSixProjectionTests.delta_names_the_orchestrator_when_the_setting_changes_and_is_empty_when_only_occupancy_changes` the delta names the agent, and an occupancy-only change is still empty.

V-9 `KeepSixProjectionTests.a_hold_is_rendered_and_an_off_count_is_one_line` a held row is rendered and the off population is a single count line.

V-10 `KeepSixShortfallTests.legacy_open_counts_queued_dispatched_and_working_and_not_blocked` three open statuses count and a Blocked task does not.

V-11 `KeepSixShortfallTests.shortfall_is_zero_when_the_board_backlog_has_no_dispatchable_card` an empty candidate list forces shortfall 0 even when open is below target.

V-12 `KeepSixShortfallTests.shortfall_uses_the_minimum_of_stored_target_and_the_absolute_cap` stored 10 against MaxParallel 6 yields effective target 6.

V-13 `KeepSixShortfallTests.other_projects_and_other_boards_do_not_add_dispatchable_cards` another project does not add to open, and another board's card is not a candidate.

V-14 `KeepSixShortfallTests.separate_queues_caps_the_target_by_parallel_plus_queued_ceiling` SeparateQueues with MaxParallel 2 and MaxQueued 4 caps a stored 10 at 6, and a null MaxQueued caps at MaxParallel.

V-15 `KeepSixShortfallTests.top_cards_are_the_five_lowest_rank_unblocked_backlog_cards_on_that_board` rank order, five identifiers, blocked and labeled cards absent.

V-16 `KeepSixReminderTests.toggle_off_enqueues_nothing` an off row with an old shortfall enqueues nothing.

V-17 `KeepSixReminderTests.shortfall_inside_the_grace_period_enqueues_nothing` two minutes after `EnabledAt` enqueues nothing.

V-18 `KeepSixReminderTests.shortfall_past_grace_enqueues_one_whenidle_message_and_creates_no_task` at three minutes the job enqueues one WhenIdle System message with header `[keep-six]` and creates zero `AgentTask` rows. The body contains "Antiphon has not dispatched" and does not contain `NO_REPLY`.

V-19 `KeepSixReminderTests.a_second_tick_while_that_message_is_pending_enqueues_nothing` a second `ExecuteAsync` leaves a single Pending row.

V-20 `KeepSixReminderTests.after_delivery_a_tick_inside_the_backoff_enqueues_nothing_until_a_dispatch_or_fifteen_minutes` a delivered message blocks until a newer create or until fifteen minutes.

V-21 `KeepSixReminderTests.an_unexpired_hold_enqueues_nothing_and_an_expired_hold_does_not` a future hold suppresses and a past hold does not.

V-22 `KeepSixReminderTests.disabling_cancels_the_pending_reminder` PUT enabled false cancels the Pending `[keep-six]` message.

V-23 `KeepSixReminderTests.ack_records_the_time_and_does_not_enqueue_a_second_message` ack sets `LastAcknowledgedAt` and leaves the Pending message in place.

V-24 `KeepSixDocumentationTests.bundle_says_the_server_does_not_dispatch_and_names_the_hold_script` the bundle names `scripts/keep-six.ps1` and says the server does not dispatch, and the script posts hold and ack with `X-Antiphon-Task-Token`.

V-25 `KeepSixDocumentationTests.agents_and_orchestration_loop_point_at_the_server_setting` the new AGENTS bullet and the new orchestration-loop subsection name the server setting and say the server does not dispatch.

V-26 `KeepSixDocumentationTests.bootstrap_and_ops_http_name_the_job_and_the_route` `docs/bootstrap.md` names `antiphon:keep-six` and `docs/ops-http.md` names `GET /api/orchestrators/keep-six`.

V-27 The client test `renders off, healthy, and under-target badges with the counts and an active hold` shows the three badges, `N of T`, the dispatchable count, and the hold reason.

V-28 The client test `the switch puts Human provenance and does not call tick` clicks the switch, asserts the PUT body, and asserts pause/tick were not called.

R-1 `AgentTaskPipelineStatusC100Tests.C100_investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked` still passes after the backlog helper is shared. That is the witness that the extract did not drop or add a held card.

R-2 `CheckpointRepeatDocumentationTests.repeat_budget_reaches_code_briefs_without_bundle_growth` passes with the recomputed orchestrator bundle SHA. The 14310-character cap is unchanged and is not a new test.

R-3 V-6 is also the regression that an unused feature does not change the instructions hash.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1320-s1/` | keep-six-settings | `/*/*/KeepSixSettingsTests/*` | V-1, V-2, V-3, V-4, V-5 | all 5 methods, 0 failed/skipped | 5 | 12 | true |
| CP-2 | S1 | CP-1 | keep-six-projection | `/*/*/KeepSixProjectionTests/*` | V-6, V-7, V-8, V-9, R-3 | all 4 methods, 0 failed/skipped | 4 | 4 | false |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1320-s2/` | keep-six-shortfall | `/*/*/KeepSixShortfallTests/*` | V-10, V-11, V-12, V-13, V-14, V-15 | all 6 methods, 0 failed/skipped | 6 | 8 | false |
| CP-4 | S2 | CP-3 | keep-six-backlog | `/*/*/AgentTaskPipelineStatusC100Tests/C100_investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked` | R-1 | all 1 method, 0 failed/skipped | 1 | 6 | true |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1320-s3/` | keep-six-reminder | `/*/*/KeepSixReminderTests/*` | V-16, V-17, V-18, V-19, V-20, V-21, V-22, V-23 | all 8 methods, 0 failed/skipped | 8 | 14 | true |
| CP-6 | S4 | `tests/Antiphon.Tests -> bin-c1320-s4/` | keep-six-docs | `/*/*/KeepSixDocumentationTests/*` | V-24, V-25, V-26 | all 3 methods, 0 failed/skipped | 3 | 8 | false |
| CP-7 | S4 | CP-6 | keep-six-bundle-pin | `/*/*/CheckpointRepeatDocumentationTests/*` | R-2 | all 1 method, 0 failed/skipped | 1 | 3 | false |
| CP-8 | S5 | n/a | keep-six-ui | `pwsh -NoProfile -File scripts/test-client.ps1 KeepSixPanel` | V-27, V-28 | 2 passed, 0 failed | n/a | 3 | false |

### Cost

Ordinary Code floor is the sum of `EstimatedMinutes`: 12 + 4 + 8 + 6 + 14 + 8 + 3 + 3 = 58 minutes, plus authoring. Run one checkpoint-tool invocation per slice group (`--after S1`, then S2, then S3, then S4, then the client row). Do not run the full table as one sitting unless the slice commits are already stacked.

```
checkpoints: docs/superpowers/plans/2026-10-17-card-1320-keep-six-in-flight-plan.md@<plan commit sha> section "### Checkpoints"
```
