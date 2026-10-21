# CARD-1416: migrate the stale bridge-harness fixtures (M1, M2, M3 Second_start)

Investigation: `docs/investigations/2026-10-21-card-1416-windows-rc-bridge-harness-red-cluster.md`.
Test-only. One slice, S1 (`tests/Antiphon.Tests` only).

## Decisions

- M1, catch-up/runtime/restart boundary: register `SessionStateStore` and
  `SessionRuntimeActionCoordinator` the way the CARD-1259 suites do
  (`AgentSessionRuntimeTests.Persist.cs`, `AgentTaskMidTurnRefineTests.ScopeFactory`). The catch-up
  fixture resolves the runtime from DI, because the coordinator does, and drains with
  `DrainPageAsync` after every `SyncTranscriptAsync`, including the two negative cases, so they
  observe the deferred settlement rather than passing vacuously.
- M1, InputFallback (flush): its fixture (`BridgeQueueHarness`) already had the store. The cause is
  different from the investigation's inference: `FlushSessionAsync` reads Working from the loaded
  store (`SessionMessageQueueService.ReadWorkingAsync`), and the test's TurnEnd went straight to the
  database, so the store still read Working and nothing was typed. `BridgeQueueHarness.MarkIdleAsync`
  writes the TurnEnd under a store lease and reconciles, the twin of `MarkWorkingAsync`.
- M1, AgentControl restart boundary: registering the store was not enough. The test seeds the
  interrupted AssistantText straight into the database after the store has loaded the session, so
  the relaunch read the cached idle snapshot and wrote no boundary. The seed now runs under a store
  lease and reconciles, the same idiom as `MarkWorkingAsync`.
- Open, needs a decision: `catch_up_settles_even_when_channel_reply_dispatch_throws` stays red after
  the migration. In `SessionRuntimeActionCoordinator.DrainCoreAsync` a phase that throws keeps its
  action for retry and stops the page ("A failed earlier action holds this session's order"). The
  AssistantText action fails at phase 4 (`ChannelReplyDispatcher`) and never reaches phase 6
  (`AgentTaskReplyService` settlement), and the later `catchup` action is held behind it. CARD-0288's
  "settlement is its own try/catch" guarantee no longer holds while the dispatcher keeps throwing.
  This is a product question (test or runtime); the assertion is not weakened here.
- M2: advance the fake clock 5 s before the burst so the first write changes `LastSeenAt`.
- M3: `Second_start` expects the `standing_resume_current_active` refusal from 344125472. The
  fixture executable is `/bin/sh` off Windows (the suite's existing idiom), because
  `Environment.SystemDirectory` is empty on Linux and every case in the class failed the spawn check
  there. Windows keeps `cmd.exe`. The other four reds in the class are not touched.

## Verification design

V-1 each M1/M2 test fails at base and passes after S1. V-2 `Second_start` passes with the refusal
expectation. V-3 no other case in the touched classes regresses. CP-7 records the remaining
`AgentSystemPromptLaunchTests` reds (expected: the four excluded M3 cases) in its own run, so the
green receipt can validate. CP-2 and CP-7 hold the known reds and run together; CP-1 and CP-3..CP-6 run together.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1416/` | runtime | `/*/*/AgentSessionRuntimeTests/*` | V-1, V-3 | all listed, 0 failed (1 skipped by attribute) | 44 | 7 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1416b/` | catch-up | `/*/*/AgentTaskCatchUpSettlementTests/*` | V-1, V-3 | 5 executed; open red `catch_up_settles_even_when_channel_reply_dispatch_throws` (decision above) | 5 | 6 |
| CP-3 | S1 | CP-1 | input-fallback | `/*/*/AgentTaskInputFallbackTests/*` | V-1, V-3 | all listed, 0 failed | 13 | 2 |
| CP-4 | S1 | CP-1 | control | `/*/*/AgentControlServiceIntegrationTests/*` | V-1, V-3 | all listed, 0 failed | 33 | 3 |
| CP-5 | S1 | CP-1 | activity | `/*/*/AgentSessionRuntimeActivityTests/*` | V-1 | all listed, 0 failed | 1 | 1 |
| CP-6 | S1 | CP-1 | second-start | `/*/*/AgentSystemPromptLaunchTests/Second_start_on_a_live_session_is_refused_and_does_not_rebootstrap` | V-2 | all listed, 0 failed | 1 | 1 |
| CP-7 | S1 | CP-2 | prompt-launch-class | `/*/*/AgentSystemPromptLaunchTests/*` | V-3 | 29 executed; failures limited to the four excluded M3 cases | 29 | 2 |

### Unlisted

One red baseline at base `327dcf8b6` before S1: the six classes in one process
(`bin-c1416red/`), as the brief requires.

### Cost

Ordinary floor 25 minutes. Run CP-1, CP-3, CP-4, CP-5, CP-6 together and CP-2, CP-7 together.
