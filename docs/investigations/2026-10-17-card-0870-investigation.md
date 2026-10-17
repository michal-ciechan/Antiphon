# CARD-0870 stalled-delegate slug collision

Confirmed on `9b4d3c89a5912795a51956d84a5a235f5de2ffd6` (`feat/card-task-ad2a2a28`).
`origin/master` is `f01104c85c2c862ff9ca6e5388c58c415fb62590` and
`origin/rc/2026-10-15` is `5e5d5cc728949420647d15a932766e70755a6f01`.
The files cited below have an empty diff against both tips. The card is still
open: the named stall witness is red on Windows, and the duplicate-key save
it was filed for is now a latent requeue bug. No whole lane was run.

## What the card recorded

Both methods were already red at merge-base `d7456a2352d15391f37eca8781c16db499a3759d`
(2026-09-30), before CARD-0778. The stored failure was Postgres `23505` on
`IX_Agents_Slug` while the dispatcher saved the retry of stalled task
`db29b25b`, logged as `Failed to dispatch task`. The second method was not
run alone. The suspicion on the card was that its `Failed to dispatch` line
was that leftover retry in the shared test database.

## Historical mechanism (reconstructed)

`IX_Agents_Slug` is unique (`AppDbContext.cs:1265`). A pool delegate's slug
is `task-` plus `DelegationReportFormatter.Short` (`DelegationReportFormatter.cs:20`,
eight hex chars). `ResolveAgentAsync` inserts that slug when `task.AgentId`
is null (`AgentTaskDispatcher.cs:7357-7383`) and the first dispatch pins
`claimed.AgentId = agent.Id` (`AgentTaskDispatcher.cs:5982`).

At `d7456a23` the overdue sweep called `TryFailBootStallAsync`
(`AgentTaskDispatcher.cs:2830`, call at `:2776`). That failed the attempt and
retried. `RequeueCoreAsync` set `task.AgentId = null` after the ephemeral
release (`AgentTaskService.cs:3126` then; `AgentTaskService.cs:3806-3809` now).
`RemoveEphemeralAgentAsync` keeps the row when the session is not terminal
and marks it idle for the janitor (`AgentTaskService.cs:3915-3928`,
`PoolDelegateRelease.cs:63-76`). The next `TickAsync` loads every `Queued`
row (`AgentTaskDispatcher.cs:470-473`) and `ResolveAgentAsync` inserts a
second agent with the same slug. Save throws `23505`. The tick catch logs
`Failed to dispatch task {ShortId}` and fails the task with
`Dispatch failed before a session existed` (`AgentTaskDispatcher.cs:1012-1020`).

The Windows E2E stopper records the kill and does not mark the session
Stopped unless `StopsSessionsIn` is set (`RecordingSessionStopper.cs:26-45`).
`BuildHarness` registers it without that connection string
(`GrokDelegateEndToEndTests.cs:1020`). A kill therefore leaves the session
non-terminal, so the agent row survives the nulling of `AgentId`.
`CleanupAsync` deletes the session, messages, and transcript, and leaves the
agent row and the task (`GrokDelegateEndToEndTests.cs:1166-1183`). The class
shares `TestDbFixture` (`GrokDelegateEndToEndTests.cs:1185`), so a leftover
`Queued` retry is visible to the next method's tick. That is the fallout
path the card suspected.

## Current boot path

`67b40444ad6c6204897e7eab8a9ad1ad70e5e6ff` (`refactor(CARD-1151): S3 remove the automatic boot tail (option B)`)
is an ancestor of this tip, of `origin/master`, and of `origin/rc/2026-10-15`.
`TryFailOverdueAsync` calls `BootStallDetectAsync` and returns false
(`AgentTaskDispatcher.cs:3757-3761`). `BootStallDetectAsync` records one
warning and does not fail, requeue, stop, or retry
(`AgentTaskDispatcher.BootStall.cs:111-120`; file header at `:9-16`).
The `23505` insert is off this test's path once the sweep runs.

The witness still asserts the retired contract. The comment at
`GrokDelegateEndToEndTests.cs:362` says the body is stale and that CARD-1151
S6 CP-36 owns the flip. After `FailOverdueTasksAsync` it expects
`ProviderUnresponsive`, reason containing `Provider never answered the boot prompt`,
`Queued`, and attempt 2 (`GrokDelegateEndToEndTests.cs:503-510`), plus a kill
(`:516-519`). `C1162_Grok_conpty_witness_name_states_the_legacy_kill_and_retry`
pins the current name and rejects both the old CARD-0870 name and the
detection-until-explicit-retry name (`BootStallDocumentationTests.cs:273-291`).

## This run

Build and both methods used `tests/Antiphon.Tests/bin-c0870/Antiphon.Tests.exe`
on the clean tip above. No whole class and no lane.

Stall filter
`/*/*/GrokDelegateEndToEndTests/a_provider_that_never_answers_boot_is_killed_and_retried_once_legacy_card_0353*`:

- Slot `6e37ad5c-3f28-4b18-b9ea-2b4c93d98193`, waited 45s on the memory floor
  (4847MB available, floor 6144MB), `maxcpucount` 4, held 225s, released.
- TUnit: total 1, failed 1, succeeded 0, skipped 0, duration 3m 43s 134ms.
  Method time 2m 15s 444ms. Exit 1.
- `TaskCanceledException: A task was canceled` at
  `AgentSessionLaunchQueue.WaitForIdleAsync` (`AgentSessionLaunchQueue.cs:216`)
  from `GrokDelegateEndToEndTests.cs:417`.
- The pre-idle `Dispatched` asserts at `:408-413` had already passed.
- Log has no `IX_Agents_Slug`, no `23505`, and no `Failed to dispatch`.
- Session `a1519b95-c392-49c8-8004-232fcb0d3e42`. Three pids were captured
  within 340ms (process tree at start). One restart-boundary warning, then
  `Transcript state unavailable` / `Transcript ingestion requires SessionStateStore`
  at `AgentSessionRuntime.cs:1063`, then one
  `Delivery verification failed ... unobservable baseline`
  (`SessionMessageQueueService.cs:4118-4122`).

Claude filter
`/*/*/GrokDelegateEndToEndTests/a_ClaudeCode_worker_still_launches_claude_types_its_brief_inline_and_prices_on_the_claude_ladder*`,
separate process:

- Slot `7e9d6791-9228-43e1-88ab-c8a8aba7db9d`, waited 0s, held 77s, released.
- TUnit: total 1, failed 0, succeeded 1, skipped 0, duration 1m 15s 997ms.
  Exit 0.

The Claude method is green on its own. Its historical failure matches a
leftover `Queued` retry from the stall method. The current sweep does not
create that leftover.

## Why WaitForIdle cancelled

Grok dispatch puts the instruction bundle in `GrokRulesPayload`
(`AgentTaskDispatcher.cs:7082-7083`). `AddDelegationWorktreeGraph` registers
`GrokRulesRefreshService` with default `GrokRulesSettings`
(`DelegationTestServices.cs:48-49`). `InitializationTimeoutSeconds` defaults
to 480 (`GrokRulesTransport.cs:19`). `PrepareLaunchAsync` sets
`GrokRulesGeneration` and `Pending` (`GrokRulesRefreshService.cs:124-127`).

After ready, `InitializeGrokRulesAsync` calls `InitializeAsync`
(`AgentSessionService.cs:2468-2472`). That method loops until `Ready` or
`Failed` (`GrokRulesRefreshService.cs:162-178`). `Ready` requires a
transcript `UserPrompt`, a turn end, and `ANTIPHON_RULES_ACK`
(`GrokRulesRefreshService.cs:337-360`). The loop's own deadline check is
inside `Judge` (`:377`), so a missing ack waits out the 480s timeout.

`BuildHarness` does not register `SessionStateStore`. `PersistTranscriptAsync`
throws `Transcript ingestion requires SessionStateStore` and returns
`PersistResult.Empty` (`AgentSessionRuntime.cs:1063-1081`). The pump calls
`SyncTranscriptAsync` every 150ms (`GrokDelegateEndToEndTests.cs:1069-1078`),
which is the warning storm. No `TranscriptEntries` are written, so `Judge`
cannot see an ack. `WaitForIdleAsync` throws only while a launch task is
still incomplete (`AgentSessionLaunchQueue.cs:198-220`). A faulted launch is
treated as settled. This launch was still running at 2 minutes.

Ready had already returned: the restart-boundary warning is after
`WaitForReadyOrThrowAsync` (`AgentSessionService.cs:545`, then `:617`).
`GrokReadyMaxWaitMs` is 60s and is screen-based. The memory-floor wait is a
confounder for a slow start. It does not explain a launch that is still
incomplete two minutes after ready.

FakeGrok acknowledges a rules prompt before the no-task-reply arm
(`src/Antiphon.FakeGrok/Program.cs:705-740`, no-reply at `:810-818`).
`ANTIPHON_FAKE_NO_TASK_REPLY` is the task brief only. With rows persisted,
rules init can finish and the launch can reach the sweep. The sweep then
leaves the task `Dispatched` on attempt 1, and the asserts at `:503-510` fail.

## Latent collision

Any ephemeral requeue whose session is not terminal still nulls `AgentId`
(`AgentTaskService.cs:3806-3809`) and leaves slug `task-{shortId}` in place
(`AgentTaskService.cs:3923-3928`). The next dispatch inserts the same slug.
That is the card's `23505`. The automatic boot path no longer does that
requeue.

## Not done, noted

Register `SessionStateStore` on `GrokDelegateEndToEndTests.BuildHarness` and
replace the CARD-0353 kill-and-retry asserts with the CARD-1151 detection
contract, updating the C1162 name pin in the same change. Leave
`ResolveAgentAsync` and `RequeueCoreAsync` unchanged.

Red-first witness, one filter, already red on this tip:

```text
/*/*/GrokDelegateEndToEndTests/a_provider_that_never_answers_boot_is_killed_and_retried_once_legacy_card_0353*
```

After the store is registered, the same filter is red on the stale
`ProviderUnresponsive` / `Queued` / attempt 2 asserts. The flip replaces
those with the positive detection asserts (stay `Dispatched`, same session,
attempt 1, `FailureCode` null, stopper not killed, one boot-stall warning).
The Claude filter above is the isolation check and is already green.

## Uncertainties

The launch stack was not sampled. The 480s rules loop is the wait on this
path that outlives the 2-minute idle budget after ready has returned.
`delivery_failed` can also end the loop after `MaxDeliveryAttempts` (default
3); this log shows one unobservable-baseline failure, so that cap had not
been reached at cancellation. The original `db29b25b` database was not
reopened. This host's Postgres was the test container started for the run.
