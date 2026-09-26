# CARD-0735: MessageQueue delivery tests, from 4.9 minutes to under one

Date: 2026-09-26. Stage: Plan, with `## Verification design` folded in. Next stage: Code, one round. Parent investigation: [CARD-0728](../../investigations/2026-09-25-card-0728-test-duration.md).

Task branch `feat/card-task-22c54e3f`, base `e4b4159d` (equal to `origin/master` at write time). No production code changes are planned or allowed by the card; every file this plan names is under `tests/` or `docs/`, plus one roster JSON.

## Summary

`SessionMessageQueueDeliveryVerificationTests` costs 270 s of medians (292–302 s inside one run) because 93 % of that time is the delivery pipeline waiting on real timeouts that are already compressed as far as the production floors allow. The harness itself costs about 0.1 s per test, so sharing one harness across the class would save at most 11 s and is not worth the isolation it gives up. The class must stay serial: the stranded sweep it exercises reads every queued message in the shared store, and a per-test database clone costs 2 s, more than the test it would isolate.

The fix is a **scaled clock**: a `TimeProvider` that runs the queue's deadlines and polls at a fixed multiple of real time. The queue already takes every wait from its injected `TimeProvider`, so this is a test-helper change only. At speed 10 the class body drops to roughly 45 s, and all 119 methods keep proving what they prove today, because virtual time keeps the same ordering of polls, re-presses, deadlines, grace windows and scheduled transcript rows.

## Ground truth

Measured on server2 from 916 `.trx` files under `/work/worktrees/*/.antiphon/**`, 33 of which ran the class (2026-09-25). Per-method duration is `UnitTestResult/@duration`; class figures are the sum of per-method medians unless stated.

| Card assumes | What the code and TRX show | Consequence |
|---|---|---|
| 119 methods, median 2.3 s, 270 s sum | 119 methods, 123 results per run (two `C561_*` methods carry three `[Arguments]` each), sum of medians 270.1 s, in-run sums 292.8–302.3 s. TRX start-to-finish of one class-only run: 7 m 51 s, of which 36 s is the process lead before the first test. | The class body is what this card can cut; the 36–77 s lead is CARD-0732. |
| Each test pays `BridgeQueueHarness.CreateAsync` (service graph plus an agent and a session insert) | 36 methods that never wait on a delivery deadline run in 0.09–0.22 s, harness included. The harness is ≤ 0.1 s of a test. | Sharing one harness per class is worth ≤ 11 s and is rejected (D-3). |
| "Fake clock for the delivery deadlines" | `SessionMessageQueueService` has 0 `DateTime.UtcNow`, every one of its 11 `Task.Delay` sites passes `_timeProvider`, and every deadline is `UtcNow()` from that provider (`server/Application/Services/SessionMessageQueueService.cs:3453, 3648, 3706–3729, 3847–3876, 3915, 4943, 5122`). The harness already registers `options.TimeProvider ?? TimeProvider.System`. | A clock swap needs no production change. |
| A fake clock is safe | `docs/testing-and-build.md` gotcha (CARD-0222): a frozen clock hangs the six poll loops forever; the rule is "an offset over the real clock, or `FakeTimeProvider` with `AutoAdvanceAmount`, never a frozen instant". `FakeTimeProvider` only fires timers from `Advance`/`GetUtcNow`, and nothing outside the waiting loop calls either, so it would also hang here. | The clock must keep real timers (D-1). |
| The timeouts are part of the assertion (risk: medium) | Five tests schedule a transcript row on **real** `Task.Delay` from `Task.Run` (`SessionMessageQueueDeliveryVerificationTests.cs:1278, 2048, 2137, 2189, 2291`), two assert **real** elapsed time (`:861–865`, `:2335–2341`), and 20 sites stamp rows with `DateTime.UtcNow`. The `UnobservableBaselineConfirmClockToleranceSeconds = 30` compare reads a row's timestamp against the provider's now. | Those sites move onto the harness clock, or the scaled pipeline runs past them (D-5); two of them are the red-first control (PC-A). |
| The class is `[NotInParallel("MessageQueue")]` because of the harness | `FlushStrandedQueuesAsync` (`SessionMessageQueueService.cs:1367–1390`) selects every `SessionQueuedMessages` row needing attention across the whole store, then flushes those sessions. Five tests call it directly and `StrandedAgeSeconds = 0` makes every pending row eligible. A parallel neighbour's pending row would be delivered by this test's sweep. No static mutable state (the per-session `_locks` map is an instance field), no `ParallelLimiter`, no runner, no pty: `EmptyRunnerClient`, `ThrowingAdapterFactory`, in-memory `FakeAgentProtocolAdapter`. | Serial stays (D-2). The coupling is the shared store plus store-wide sweeps, not the harness. |
| A per-test isolated schema would allow parallel runs | `CreateIsolatedSchemaAsync` (CARD-0412) costs 1.8–2.6 s per clone (`TestDbFixtureIsolationTests` medians), behind a process-wide clone lock. | 119 clones ≈ 240 s, more than the 45 s target. Rejected. |
| Neighbours `VerificationRoundDeliveryTests` (139 s), `CheckNoteDeliveryHandoffTests` (98 s), `ChannelBridgeTests` (87 s) add to the lane | 33 of the 40 `MessageQueue` classes build on `BridgeQueueHarness`. Their tests were not audited for real-time scheduling and some already fetch the DI `TimeProvider` for their own services. | The harness default stays `TimeProvider.System`; the scaled clock is opt-in per class (D-6). One follow-up card for the neighbours. |

### Where the 270 s goes

Every wait below is `Task.Delay(…, _timeProvider)` against a `UtcNow()` deadline. Harness test defaults (`BridgeQueueHarness.cs:106–118`): `EvidenceTimeoutSeconds = 1`, `PollIntervalMs = 50`, `PostSubmitAdvanceTimeoutSeconds = 1`, `TranscriptConfirmTimeoutSeconds = 3`, `ReEnterIntervalSeconds = 1`, `PostFailureConfirmGraceSeconds = 3`, `StrandedAgeSeconds = 0`. Production defaults that the harness does not override and that the loops floor: `PostEvidenceSettleMs = 500`, `OverlaySettleMs = 400`, `SubmitAttempts = 3`, and the transcript pull and grace cadence is `Math.Max(1000, PollIntervalMs)` (`:3467, :3729, :3798`), so a 3 s grace is three 1 s polls whatever `PollIntervalMs` says. `EvidenceTimeoutSeconds` is floored at 1 (`RemoteControlRecoveryService.cs:622`, `WaitForSequenceAdvanceAsync`). The settings cannot be compressed further without changing how many polls a window contains.

| Median cluster | Methods | What the pipeline waits on | Sum |
|---:|---:|---|---:|
| 0.09–0.22 s | 36 | Nothing timed: refusals, late-confirm on turn end, working-rule reads, modal barriers | 5 s |
| 0.63–0.82 s | 19 | One verified delivery: 500 ms post-evidence settle, submit advance, transcript confirm found on the first 50 ms poll | 14 s |
| 0.94–1.25 s | 11 | One 1 s evidence or advance window plus settle | 13 s |
| 1.8–2.2 s | 4 | One re-press interval (1 s) inside a confirm that then succeeds, plus settle | 8 s |
| 2.6–2.7 s | 12 | Wedge: 1 s evidence, Esc, retype, 1 s evidence again, settle (CARD-0137 S5 one-shot recovery) | 32 s |
| 3.1–3.3 s | 4 | C561 blind-matcher verdicts: confirm deadline plus one grace poll | 13 s |
| 3.7–3.9 s | 15 | 3 s transcript confirm deadline, three Enters at 1 s, settle | 57 s |
| 4.7 s | 2 | 3 s confirm, row lands at 4 s, next 1 s grace poll confirms | 9 s |
| 6.7–6.8 s | 11 | 3 s confirm plus the full 3 s grace, settle | 74 s |
| 7.4 s | 1 | Two Mode.Now screen-only fallbacks in one test | 7 s |
| 12.8 s | 1 | One delivery plus four stranded sweeps, each a 2.5 s wedge cycle | 13 s |
| 22.9 s | 1 | `Parking_a_channel_bound_agents_message_raises_a_critical_incident`: its own `DeliveryVerificationSettings` block omits the grace, so it waits the **production** 20 s `PostFailureConfirmGraceSeconds` after a 2 s confirm | 23 s |

Timed waiting is about 250 s of the 270 s. At speed 10 that is 25 s, plus about 20 s of real database and harness work, so the class body target is **≤ 60 s** on server2 (stretch 45 s), from 292–302 s today.

### Why the class is serial, mechanism by mechanism

- **Shared static state:** none found. `SessionMessageQueueService` holds `_locks` as an instance `ConcurrentDictionary`; each harness builds its own `ServiceProvider`, `AgentSessionRuntime` and `FakeAgentProtocolAdapter`.
- **`ParallelLimiter<ProcessSpawnLimit>`:** not on this class; nothing spawns.
- **Real Postgres:** yes, the shared default store. Each harness inserts one agent and one session and cleans up by `AgentId` and `TempRoot` on dispose (`BridgeQueueHarness.cs:584–625`). The coupling is `FlushStrandedQueuesAsync` and the turn-end flush reading every session's queue, which is also why forty classes share the key.
- **Fixed waits and polling:** yes, and it is the whole cost. All of it goes through the injected `TimeProvider`.
- **Real runner or pty:** no.

