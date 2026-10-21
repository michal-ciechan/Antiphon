# CARD-1416: the "Windows" bridge-harness red cluster (14 reds)

Status: **mechanisms confirmed for 8 of 14, reconstructed for 4, open for 2. None of them is
Windows-specific.** The cluster looks Windows-only because no Linux lane runs these classes.

Source of the cluster: cause H in `docs/investigations/2026-10-21-rc-2026-10-20-windows-qualification-reds.md`
(branch `feat/card-task-41256176`, commit `92c62bcc`).

## Reproduction (Windows desktop, 2026-10-21)

Tested source: master `47e6916c7590b101c9336fab84398fa93b68878f`, the task branch tip with no edits.
One build: `scripts/build-slot.ps1 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1416/`.
Then one process per class:
`dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1416/ -- --treenode-filter "/*/*/<Class>/*" --report-trx`,
each through `build-slot.ps1`. Packing does not apply: each process ran a single class.

| Class | total | failed | failed names (assertion identical to the rc runs) |
|---|---:|---:|---|
| AgentSystemPromptLaunchTests | 29 | 5 | Start_with_system_prompt_append_passes_flag_on_fresh_launch, Second_start_on_a_live_session_is_a_no_op_and_does_not_rebootstrap, Resume_not_found_fallback_delivers_fresh_bootstrap, Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap, Launch_note_yields_to_an_owed_channel_row |
| AgentTaskCatchUpSettlementTests | 5 | 3 | all three `catch_up_settle*` cases: `settled.Status` Dispatched instead of Succeeded |
| AgentTaskInputFallbackTests | 13 | 2 | `Runner_write_failure_types_only_the_durable_api_pointer(flush)`, `Ordinary_spill_errors_keep_their_existing_delivery_behavior(flush)` |
| AgentSessionRuntimeActivityTests | 1 | 1 | `writes.Count` should be 2 but was 1 |
| AgentControlServiceIntegrationTests | 33 | 1 | `Resume_of_a_mid_turn_session_writes_a_restart_boundary_and_queues_the_auto_continue`: Sequence contains no elements (line 887) |
| AgentSessionRuntimeTests | 45 | 1 | `Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart`: rows `[]` |
| AgentTaskCheckSweepTests | 36 | 1 | `the_flush_cancels_a_settled_check_already_typed_once_before_it_retypes`: Status Pending instead of Canceled |

All 14 reproduce with one class per process. They are deterministic and are not caused by packing.

### Bisect point: before the CARD-1259 series

A detached worktree was built at `d994c20b70dadddfb73375bfae09a94d04c16a51`, the parent of the first
CARD-1259 code commit `22b3e4793`. The same seven class runs gave these results:

| Class | failed at d994c20b7 | flipped green |
|---|---:|---|
| AgentTaskCatchUpSettlementTests | 0 / 5 | all 3 |
| AgentTaskInputFallbackTests | 0 / 13 | both |
| AgentControlServiceIntegrationTests | 0 / 33 | the restart-boundary case |
| AgentSessionRuntimeTests | 0 / 43 | the sequence-restart case |
| AgentSystemPromptLaunchTests | 5 / 29 | none (same 5 names) |
| AgentSessionRuntimeActivityTests | 1 / 1 | none |
| AgentTaskCheckSweepTests | 1 / 36 | none |

The TRX for each of the 7 flipped names records `outcome="Passed"`. The worktree and every
`bin-c1416*` output directory were removed afterwards.

## Mechanisms

### M1: CARD-1259 made transcript ingestion and its side effects deferred and store-bound; four fixtures never caught up (7 reds, confirmed)

- `server/Application/Services/AgentSessionRuntime.cs:1219` (`PersistTranscriptAsync`) throws
  `InvalidOperationException("Transcript ingestion requires SessionStateStore.")` when the runtime has no
  `SessionStateStore`. Lines 1233-1237 swallow the exception, record a persist failure and return
  `PersistResult.Empty`, so nothing is stored.
- Transcript side effects no longer run inline. Settlement (phase 6), refinement confirmation (phase 2),
  channel replies (phase 4) and the rest go through `SessionRuntimeActionCoordinator`. `Actions()` at
  lines 1130-1134 resolves the coordinator with `GetRequiredService`. `SyncTranscriptAsync` (lines
  1115-1127) persists, then `RequestAsync(sessionId, "catchup")`, and swallows any throw. Actions execute
  only in the hosted worker (`RunAsync`) or in an explicit `DrainPageAsync`.
- Introduced by `dd039a405` "WIP CARD-1259 S2: persist deferred transcript actions and own bounded workers"
  (authored on server2). The store requirement and the `"catchup"` request were both added in that
  commit (`git log -S`). In the same commit `BridgeQueueHarness` registers the store and the coordinator
  but starts the worker only on `StartRuntimeActions` (default false). Other suites were migrated to an
  explicit drain: `AgentSessionRuntimeTests.Persist.cs:484`, `ChannelReplyDurabilityTests.cs:127`,
  `AgentTaskMidTurnRefineTests.cs:353`.
- Fixtures that were not migrated:
  - `AgentSessionRuntimeTests.cs:208-213` builds `new AgentSessionRuntime(eventBus, settings, scopeFactory, ...)`
    with no store. Ingestion throws and the rows come back `[]`.
  - `AgentTaskCatchUpSettlementTests.cs:224-254` (`RuntimeFor`) registers neither the store nor the
    coordinator. Persist fails, `Actions()` throws, `SyncTranscriptAsync` swallows it, and the task stays
    `Dispatched`.
  - `AgentTaskInputFallbackTests` (2, flush path) and `AgentControlServiceIntegrationTests:887`
    (restart-boundary row). Both flip exactly across the CARD-1259 range. Their fixture wiring was not
    traced line by line. `WriteRestartBoundaryIfInterruptedAsync` (line 1251) has the same `_states`
    requirement.

### M2: the harness clock change makes the first activity write a no-op (1 red, reconstructed)

`AgentSessionRuntimeActivityTests.cs:22-45` injects a `FakeTimeProvider` fixed at 2026-09-24 12:00:00.
Since `79c68358a` (CARD-0735, 2026-09-26), `BridgeQueueHarness.cs:315,329` stamps the seeded session's
`LastSeenAt` from that injected clock. Before that commit it used `DateTime.UtcNow`. The first
`ObserveOutputAsync` at `burstStart` claims the throttle slot (`AgentSessionRuntime.cs:2266-2292`,
`ClaimActivityWrite`, 2306) and writes the same value that is already stored. EF marks no modification,
so the interceptor counts nothing. Only the +1001 ms write counts, giving 1 instead of 2. The test dates
from `b092fabb2` (09-24), when the seeded value differed from the fake clock. The red is consistent at
both commits tested.

### M3: AgentSystemPromptLaunchTests (5 reds): 4 stale since 2026-09-09/13, 1 newer (reconstructed / open)

- Four of the reds (incident, conflict, `Created.Count` 1, `Created[1]` out of range) are already recorded
  as pre-existing on master `2fb81db3` on 2026-09-13. They are byte-identical at both ends and were
  reproduced in a detached worktree. The record is commit `14e692a9f` in
  `docs/superpowers/plans/2026-09-07-card-0417-channel-reply-conciseness-plan.md` ("Recorded runs and
  known pre-existing red").
- `Second_start...`: since `344125472` (2026-09-09, "preserve standing conversation identity and
  explicit owned recovery"), `AgentControlService.cs:193-195` refuses `Fresh` while a session is live or
  launch-owned. The test still expects an idempotent no-op. The product refusal is deliberate.
- The two resume→fresh fallback cases and the incident case are probably the same change: explicit
  owned recovery replaced the automatic fallback. This is not traced line by line.
- `Launch_note_yields_to_an_owed_channel_row` is absent from the 09-13 list and present on 10-19
  (`42cbbd04`). It is red before CARD-1259. The commit that introduced it was not found.

### M4: AgentTaskCheckSweepTests (1 red, open)

The red is present before CARD-1259 (`d994c20b7`). The test file was last changed on 09-22 (`c050f6b28`).
The mechanism behind `FlushIfIdleAsync` not cancelling the settled check was not traced.

## Linux and master

- None of the seven classes is in the Linux CARD-1287 roster
  (`docs/investigations/2026-10-15-card-1287-linux-unit-roster.txt`: 0 hits each) or the Windows unit
  roster. All seven are `[Category("Integration")]`, and those rosters are Unit-only (373 classes).
- The green Linux rc final at `df6b422c` (`docs/investigations/2026-10-19-rc-linux-final.md` on
  `feat/card-task-aaee7f74`: 4528 executed, 4528 passed) therefore does not cover these classes.
- M1, M2 and M3 are in DI wiring, EF change tracking and control flow. None of that code has an OS
  branch, so all 14 are expected to fail on Linux as well. Linux was not run, as instructed.
- All 14 fail on master `47e6916c7` (this run). They are not rc-only.

## Remaining uncertainty

- M1: the exact fixture path for the two InputFallback reds and the AgentControl red is inferred from
  the bisect, not traced.
- M3: the origin commits for the three fallback/incident reds and for `Launch_note_yields`.
- M4: the whole mechanism.
- Linux behaviour is inferred from the absence of an OS branch. It was not executed.

## Not done, noted

- Fix idea M1: give the four fixtures `SessionStateStore` + `SessionRuntimeActionCoordinator` and an
  explicit `DrainPageAsync`, the way CARD-1259 migrated its own suites. Do not add a Windows exclusion.
- Fix idea M2: seed `LastSeenAt` distinct from `burstStart`, or advance the clock once before the burst.
- Fix idea M3: update the `Second_start` expectation to the 09-09 refusal. For the fallback cases, a
  decision is needed (test or product).
- Fix idea M4: none until it is traced.
