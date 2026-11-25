# Why the desktop orchestrator missed completion notes on November 24–25, 2026

Twelve delegate tasks produced fourteen completion notes across the two reported intervals. The desktop server queued every one to the correct, live Claude session. Its runner had adopted the session and could no longer attest to the state of Claude's dialog. Delivery therefore stopped before typing. The same gate held a November 12 keep-six reminder. The provider's separate Monitor masked this condition until the last monitor window ended.

## Incident evidence

All times below are UTC. The read-only desktop API identified server build `6ed4cb58cd75fe386e94965165d96752452431a8`, standing agent `a392cbc4-0fc0-4603-b4d0-5198d1929718`, and persistent, running session `b2f1a869-9bc5-4974-b8ae-53686448fd20` (started November 12 at 03:49:05). The session was bound. The server task event log, caller queue, land outbox, attention projection and indexed provider transcript are the evidence sources. The physical desktop Serilog files were unavailable from the server2 worktree.

At approximately 12:03 on November 25, paging the caller's messages through the last cursor returned **1,640 pending**. Of those, 1,638 had zero charged attempts and two had three. The live response reported `working=false`, `modalBlocked=true` and `Claude dialog observation unavailable: adopted session`. Each completion below had one persisted withhold, the same reason and zero charged attempts. Sequence identifies the row in this particular caller's queue. The time columns give the server's queue creation and first recorded refusal, to the second.

| Task | Date | Queued | First withheld | Sequence | Queue message |
|---|---|---|---|---:|---|
| af84cf03 (comparison) | Nov 24 | 22:43:23 | 22:43:28 | 16699 | 40ff5d30-3336-4731-908e-6aa0c343b089 |
| 34eb04d1 (comparison) | Nov 24 | 23:03:02 | 23:03:23 | 16702 | a70d3a0e-6c40-422b-b1cd-2d453629781c |
| 7b7f98d5 (comparison) | Nov 24 | 23:07:30 | 23:07:35 | 16708 | 224d1c36-5285-4669-aead-99ca87ea362a |
| 373fdb0c (Blocked) | Nov 24 | 23:11:46 | 23:11:52 | 16712 | 78f016b0-d406-4a51-9fe1-390cbd90df02 |
| d4a69ad5 (comparison) | Nov 24 | 23:22:53 | 23:25:47 | 16720 | 2a7e66ea-59a2-41f9-9e05-db1884a2656c |
| 373fdb0c (Succeeded) | Nov 24 | 23:27:53 | 23:27:59 | 16722 | a3832313-9f96-4196-b740-413400f645d1 |
| e8184d81 | Nov 24 | 23:41:55 | 23:42:01 | 16734 | f4de229d-b456-49e7-ad13-b9057b4d4186 |
| cbcea57b | Nov 24 | 23:44:31 | 23:44:38 | 16736 | 1975f2b7-2d4b-4352-ac4b-7aead573245c |
| aa089566 | Nov 24 | 23:48:54 | 23:48:58 | 16742 | 131d03e6-eed5-4f99-a1d7-1e80b2581b9a |
| a2ff679e (Blocked) | Nov 24 | 23:54:35 | 23:54:45 | 16745 | 936aa906-cc12-437d-9b23-69bc8c275f3b |
| 7280f7d0 | Nov 25 | 00:07:45 | 00:07:50 | 16749 | e5c8704d-5c6d-4181-9fed-0c8f8f66cc28 |
| f9d27fc2 | Nov 25 | 00:33:22 | 00:33:27 | 16752 | 9572b321-57a4-4aad-bbcf-a83ee94616a8 |
| a2ff679e (Succeeded) | Nov 25 | 07:28:03 | 07:28:08 | 16755 | ebb5ea60-8f89-4130-a290-63ffb422acd3 |
| ddbbf70a | Nov 25 | 07:40:02 | 07:40:06 | 16766 | 9fbe0d73-c0b4-4a73-8f3d-95ceb50d9c4b |
| a3cc66ed | Nov 25 | 07:54:42 | 07:54:48 | 16779 | a960ea26-5817-45c2-a731-fffad0d91cf7 |
| a1f714d3 | Nov 25 | 08:21:14 | 08:21:19 | 16785 | 08e56019-45f4-4e1c-8aee-e0ed45de4b17 |
| ab5b5fe7 | Nov 25 | 08:28:40 | 08:28:45 | 16786 | f24d6e04-5c9b-4608-928d-1da07c7595f9 |
| ec24e3e3 | Nov 25 | 09:26:55 | 09:27:00 | 16789 | bdb998ee-99b8-40e4-b6dc-4d4379e5b4c1 |

All twelve had reached Succeeded by the investigation. For example, f9d27fc2's event at 00:33:12.570408 recorded a 4,630-character report alongside `runner_reported_commit_not_pushed`. Its completion was queued ten seconds later. Publication, cleanup and runner synchronization can affect landing independently. None explains an already-created row with a persisted pre-input refusal. No session was parked or replaced to deliver these reports.

### What reached the caller

The suggested four comparison tasks really did land. All four outcome notifications were still `AwaitingReceipt` on the same parent, with both `confirmedAt` and `confirmingPromptSequence` null. Their notification IDs were `0a4289d7-1fd1-45df-9ed8-d9a9432c65a5`, `3fb39314-64da-496c-b3a8-7bddc14c412c`, `6e3a820e-e46a-451b-afca-afe929c4dbd9` and `3c43743a-12cf-4975-9306-3e9a7da96d1d`. The first outcome was committed at 23:14:55.091626. At 23:19:55.462653 the server logged “Warning: outcome receipt unconfirmed” for the obligation linked to queue message `401d7f26-4dfc-4794-841f-4caebf194667`.

The scoped task list returned 31 other project tasks completed on November 24 before 22:00. Their land-request details exposed 54 notification rows, all AwaitingReceipt. This additional sample supplied no confirmed land-note control.

The positive example of an actual wake is in the provider transcript. A native Monitor notification at 22:22:14.833 (sequence 2417975) led to an explicit status check for af84cf03. A second monitor woke the caller at 22:52:19.332. The last wake at 23:22:24.599 (2430158) led to status calls at 23:22:29.188 and land actions. That turn ended at 23:25:38.850. The next Monitor call in the examined interval was at 11:59:05.829. The recorded CompactBoundary fell at **00:21:50.835**, sequence 2443525. Its synthetic continuation names historical task IDs. It cannot confirm delivery of an individual note. The rows were already withheld on both sides of that boundary.

The operator prompted Claude at 07:21:58.561 and 11:54:50.080. The latter was followed by an explicit task status command at 11:54:56.761. At 11:57:54.351 Claude called its own ReadNotifications and got “No queued notifications.” That tool reads the provider inbox. Antiphon's PostgreSQL-backed session queue was still full.

### Keep-six and Checks

Keep-six was enabled with configured and effective target **7** and no hold. Outstanding reminder `60e0427d-87fa-4725-a96c-5cd7790cbccf` (sequence 10762) was queued on November 12 at 14:55:15.564699 and first withheld at 14:55:23.979663. It had zero attempts and no acknowledgement. The job's singleton-pending rule therefore suppressed another queue message on every later eligible tick. The live projection showed seven open and 333 dispatchable backlog at sampling. That snapshot cannot reconstruct a minute-by-minute historical shortfall.

No Check-origin row for the reported cohorts supplied an independent wake. The Check service can capture a check and then supersede delivery as soon as a completion row exists. For 7280f7d0, the capture and supersession events span 00:07:50.668701 to 00:08:02.190112. The session already had aged-note attention. In the investigation snapshot, 1,634 pending-note entries were withheld, and the session/task attention sample included 305 caller-note and 484 unconfirmed land-outcome entries. None of those independently started a Claude turn.

## Delivery path and why the gap passed review

`AgentTaskReplyService` consumes the worker TurnEnd and report, persists terminal status, and routes Session replies by `ParentSessionId`. Profiled completions persist a `TaskCompletion` obligation alongside settlement. `AgentTaskLandNotificationHostedService` materializes and reconciles that obligation. Ordinary completions use the sourced delegation queue with their idempotency stamp. `SessionMessageQueueService` flushes WhenIdle after an idle observation or at TurnEnd. Before input, `ReadDialogWithholdAsync` asks the runner for dialog evidence. `SessionRunnerRuntime.AdoptCoreAsync` marks adoption. Its dialog observation cannot establish the original input state. `ChargeDurableWithholdAsync` persists this refusal and leaves the row pending. The complete submitted prompt is the delivery receipt.

CARD-1591 deliberately retained that refusal. Earlier reviews rejected unsafe adopted-terminal input. Its salvage plan shipped withhold accounting and left native waking to CARD-1592. The November 12 CARD-0747 investigation describes aged obligations on this same session. The November 20–25 history in delivery, report parsing, runner sync and parking did not introduce the observed predicate. The field evidence predates that window. November 24's delegate-command text and restart-command refactors do not clear the adoption flag.

`VerificationRoundDeliveryTests` already exercises real settlement, reconciliation, queueing and whole-prompt receipt. Its fake caller can become writable after TurnEnd. The adopted-dialog tests require zero writes, the withhold tests check accounting, the keep-six tests require at most one pending queue row, and Check tests permit handing off to an existing completion. Each contract could pass independently while a permanently adopted idle caller received no actionable input. The missing regression was to keep the terminal unwritable and make the independent wake produce a task ID the parent could read.

## Fix and activation

The caller's existing terminal queue remains the place to inspect a completion's receipt. A second path now supplies a short provider-native notice with task IDs. Claude arms an `asyncRewake` command hook at SessionStart and Stop. Once a minute the hook polls a session-bound server endpoint. An overdue report causes the hook to exit 2, which Claude documents as a wake even while idle. The parent can then fetch the complete report with its own `scripts/antiphon delegate status <id>`. The target checkout and the generated sibling workspace both install the hook. An uninterrupted two-week idle watch requests one renewal turn before its provider timeout. [Claude Code command hooks](https://code.claude.com/docs/en/hooks#run-hooks-in-the-background)

After ten minutes the server selects the current settlement's unacknowledged reports. The selection covers plain queue rows, receipt-backed completions and missing enqueues. It includes a reported Blocked state. The server checks the caller's identity, returns at most 16 IDs, and throttles repeats to ten minutes per session. A matching parent read or delivery confirmation removes that obligation from the wake list. A per-session CallerNoteUndelivered attention item uses the same selection and is visible even with the hook offline. Existing per-message attention and receipt history remain available. A server process restart may repeat an advisory once because the cooldown is in memory.

Keep-six uses the existing project counts, effective target, grace, hold and backoff. An elapsed 15-minute interval can produce a fresh native shortfall notice even if the original terminal reminder is still pending. Report reads, terminal receipts, reminder throttles and explicit keep-six acknowledgements remain distinct.

### Operator verification after reviewed activation

1. Confirm `GET /api/version` serves the reviewed fix and that the orchestrator's effective Claude workspace has the Stop and SessionStart hooks. For a generated sibling, the existing `orchestrator-workspace setup <project> --cli claude` command writes its managed settings. The incident caller worked directly in the checkout. After reviewed server and migration activation, relaunch the caller with its session identity and accepted launch timestamp in the environment. Inspect `/hooks` there and allow a normal turn to end so the hook arms.
2. Use the next ordinary delegated task with a Session reply. Record its task ID, caller session ID and settlement time. Leave the caller idle and let the ten-minute grace elapse. In an adopted session, the original queue row may still show zero attempts. Within one successful minute poll, expect Claude to wake and read the named task. Verify that the per-session unread-report attention count decreases and a later poll interval produces no repeat for that settlement.
3. During a real eligible shortfall, verify the current target, backlog, hold and last dispatch/completion timestamps in keep-six and the project pipeline. With one old terminal reminder pending, observe a native notice with the current shortfall after the grace/backoff and another after the interval if the shortfall persists. Refill or a valid hold should end the reminders. Read the transcript for the resulting action. Only a matching submitted prompt confirms the terminal note itself.
4. Until the product and workspace are active, re-arm the existing provider Monitor when its window ends and explicitly poll owned delegate statuses. A land action, idle badge or empty ReadNotifications result alone did not establish delivery in this incident.

## Regression evidence

The same method ran through a server2 build slot on each commit. It uses real settlement and queue services, isolated PostgreSQL, a fake adopted terminal, loopback Kestrel, and the Node command from the installed Claude settings. The clock advances inside the test. The live desktop Claude process was outside the test boundary.

| Commit | Outcome | TUnit duration | Build slot |
|---|---|---:|---|
| `9324981ca87a015bd10cc361b0d00cebe4041e96` | 1 failed, 0 passed. The guarded report was pending and the wake endpoint returned HTTP 404 instead of 200. | 44.548 s | `37cdbbb5-d911-4d89-ab46-c8cf9cbb3ec9`, held 1,014 s including build |
| `65eebeb532398f54244d4d30a1ee22058e309d4f` | 1 passed, 0 failed, 0 skipped. The hook exited 2 with the exact task ID and shortfall. The test checked authentication, duplicate suppression, repeat timing, attention before and after the parent read, zero terminal submissions and an unconfirmed original obligation. | 55.733 s | `276a6dd4-38da-4101-b83e-39a7cbe9cc4d`, held 371 s including build |

To reproduce this method on server2:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label caller-wake -- dotnet run --project tests/Antiphon.Tests -- --treenode-filter '/*/*/CallerWakeTests/Adopted_caller_wakes_for_an_unread_report_and_a_withheld_shortfall'
```
