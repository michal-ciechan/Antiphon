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

## Decisions

### D-1 — A scaled real clock, not a virtual one

Add `tests/Antiphon.Tests/TestHelpers/ScaledTimeProvider.cs`: `internal sealed class ScaledTimeProvider(double speed, DateTimeOffset? start = null) : TimeProvider`.

- `GetUtcNow()` returns `start + offset + speed × (real elapsed since construction)`, measured with `Stopwatch`. `speed` must be `> 0`; `speed == 1` is exactly the "offset over the real clock" shape that `AgentSupervisionTests.MutableTimeProvider` has today.
- `Advance(TimeSpan)` adds to `offset`. Like `MutableTimeProvider.Advance` it does not fire pending timers; every queue loop re-reads `UtcNow()` on its next poll, at most 50 ms virtual later, so a jump past a deadline is observed on that poll. The five C561 tests already rely on this.
- `CreateTimer(callback, state, dueTime, period)` delegates to `TimeProvider.System.CreateTimer` with `dueTime / speed` and `period / speed` (`Timeout.InfiniteTimeSpan` preserved, anything positive rounded up to at least 1 ms). `Task.Delay(x, clock, ct)` and `CancellationTokenSource.CancelAfter` therefore run in real time, `speed` times shorter. `GetTimestamp()` and `TimestampFrequency` scale the same way so `GetElapsedTime` agrees with `GetUtcNow`. `LocalTimeZone` is UTC.

Why this shape: the CARD-0222 rule in `docs/testing-and-build.md` (a fake clock handed to the queue must be an offset over the real clock, or `FakeTimeProvider` with `AutoAdvanceAmount`, never a frozen instant) exists because the six poll loops wait on `Task.Delay(poll, _timeProvider)` and compare `UtcNow()` to a deadline; a clock whose timers never fire hangs the process at 0 % CPU. Real timers keep every wait finite. `FakeTimeProvider` with `AutoAdvanceAmount` is not enough here: it moves time only from `GetUtcNow()` and `Advance()`, and a loop parked in `Task.Delay` calls neither, so nothing wakes it (R-B covers the pump variant).

**Speed 10**, as a constant `TestClockSpeed` on the class. The bound is the fake adapter's two real delays: `FakeAgentProtocolAdapter.ComposerFrameDelayMs = 10` and `PromptOutputDelayMs = 10` (`tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs:50, 117`) emit frames on `Task.Run` in real time. The shortest virtual window that must contain such a frame is `PostEvidenceSettleMs = 500` on a 50 ms poll grid; at speed 10 a 10 ms frame is 100 ms virtual, five times inside the settle. Database round trips of 1–5 ms real become 10–50 ms virtual, at most one poll interval, so poll counts within a window (three Enters at 1 s inside a 3 s confirm) hold. Speed 20 would put a 5 ms query past one poll and a frame at 40 % of the settle. If CP-5/CP-6 show any timing failure in three consecutive runs, Code drops the constant to 5, retargets 75 s, and says so in its report; that is the one decision delegated to Code.

### D-2 — The class stays `[NotInParallel("MessageQueue")]`, `Integration`, `Slow`

`FlushStrandedQueuesAsync` and the turn-end flush select rows across the whole store, so two tests in the same store cannot overlap without one delivering the other's message; a per-test clone costs 2 s (`TestDbFixtureIsolationTests`), more than the test it would isolate. `[ParallelLimiter]` on the class would only cap concurrency inside the class and would not keep it apart from the other 39 classes on the key. `[ParallelGroup]` semantics could not be verified on this runner (no TUnit docs offline) and would need all forty classes reasoned about together; out of scope. `Slow` is a cost marker, not a skip; the class stays in `slow-tests-allowlist.txt` with its reason comment updated to the new measured cost.

### D-3 — No shared harness

Thirty-six methods that never wait on a deadline run in 0.09–0.22 s including `BridgeQueueHarness.CreateAsync`, so the harness is at most 0.1 s of a test and 11 s of the class. Sharing one harness would make the five sweep tests and the dispose-time cleanup (`BridgeQueueHarness.cs:584–625`, keyed on `AgentId`/`TempRoot`) order-dependent for an 11 s gain. Rejected.

### D-4 — No duplicates removed

Eight pairs share a scenario and split the assertions between two methods (each pair pins a different card). After D-1 each pair costs about 0.3 s real, so merging would save about 2.5 s of the class while changing the roster's `directTestMethods` and the card-to-pin mapping. Not done in this card; recorded for a later cleanup if the class is ever reorganised:

| Same scenario | Method A | Method B |
|---|---|---|
| Always-on, echo off, WhenIdle (pre-first-turn refund) | `A_pre_first_turn_no_evidence_refunds_the_attempt_and_withholds_the_kill` | `The_refunded_failure_reports_one_warning_not_an_error_per_attempt` |
| Always-on, swallow 99, empty ack, WhenIdle | `Swallowed_submit_reverts_message_and_restarts_always_on_agent` | `A_pre_first_turn_swallowed_submit_still_charges_the_attempt` |
| Non-always-on, echo off, WhenIdle | `Non_always_on_agent_gets_incident_and_revert_but_no_kill` | `The_refund_applies_to_non_always_on_agents_too` |
| Observable, swallow 99, WhenIdle | `Screen_output_advancing_without_a_record_is_no_longer_delivered` | `An_idle_always_on_session_with_no_record_is_still_killed` |
| Observable, echo off, WhenIdle (post-first-turn wedge) | `Wedged_composer_withholds_enter_reverts_message_and_restarts_always_on_agent` | `A_session_that_worked_and_then_stalled_still_charges_the_attempt_and_is_killed` |
| Non-always-on, echo off, Mode.Now | `Mode_Now_failure_still_throws_409_with_no_receipt` | `Card0164_ModeNow_NoComposerEvidence_gets_no_grace` |
| Observable, non-question ToolResult on turn end | `Read_file_ToolResult_does_not_confirm_delivery` | `Claude_ToolResult_without_question_wrapper_does_not_confirm` (an `[Arguments]` pair, not a duplicate) |
| Codex slash usage refused at enqueue | `Codex_slash_usage_is_refused_at_enqueue_with_zero_bytes_typed` | `Codex_slash_usage_with_arguments_is_refused_too` (an `[Arguments]` pair, not a duplicate) |

### D-5 — Every stamp comes from the harness clock, and scheduled rows ride the same clock

At speed 10, virtual time runs ahead of real time by nine times the real elapsed; a 23 s-virtual test drifts 21 s. `UnobservableBaselineConfirmClockToleranceSeconds = 30` compares a row's `Timestamp` with the provider's now (`SessionMessageQueueService.cs:480, 2361, 2701, 3257`), so a row stamped with `DateTime.UtcNow` would silently age toward the tolerance. Therefore:

- `BridgeQueueHarness` exposes `TimeProvider Clock` and `DateTime Now` (`Clock.GetUtcNow().UtcDateTime`), and its ten `DateTime.UtcNow` sites (`BridgeQueueHarness.cs:215, 249, 295, 366, 388, 466, 468, 512, 562, 563`) read `Now`. The static `InsertEntryAsync` gains a `DateTime? createdAtUtc` parameter that the instance wrappers fill from `Now`.
- The class's twenty `DateTime.UtcNow` sites read `h.Now`; C561's `DateTimeOffset.UtcNow` in `PoisonToolCall` and `StubbedUserPrompt` read the test's clock.
- The five fire-and-forget inserts (`SessionMessageQueueDeliveryVerificationTests.cs:1278, 2048, 2137, 2189, 2291`) become `await Task.Delay(x, h.Clock)` inside the same `Task.Run`, so a "4 s" row still lands at virtual 4 s, inside the 3–6 s grace, and the 0.4/0.3/0.2 s rows still land inside the confirm window.
- The two real-elapsed assertions (`:861–865` "< 10 s, only the fallback waits out the 20 s deadline" and `:2335–2341` "< 5 s, must not burn the grace") become `(h.Now - started)`, so they still discriminate: under real elapsed they would pass even if the pipeline ran the fallback (2 s real). PC-D proves it.
- `Mode_Now_waits_for_the_per_session_lock`'s real `Task.Delay(400)` (`:1996`) stays: it bounds a semaphore wait, not a clock-driven path.

### D-6 — Opt-in per class; the harness default is unchanged

`HarnessOptions.ClockSpeed` (`double?`) creates a `ScaledTimeProvider(ClockSpeed.Value)` and registers it; `TimeProvider` (explicit) and `ClockSpeed` together throw `InvalidOperationException`. The default remains `TimeProvider.System`, so the 32 other `BridgeQueueHarness` users on the `MessageQueue` key see the same values as today (`Now` is then `TimeProvider.System.GetUtcNow()`). `VerificationRoundDeliveryTests` (139 s), `CheckNoteDeliveryHandoffTests` (98 s) and `ChannelBridgeTests` (87 s) are a follow-up card after this lands: each needs the same audit of real-time scheduling and stamps that this plan did for one class.

### D-7 — The 22.9 s test states its grace

`Parking_a_channel_bound_agents_message_raises_a_critical_incident` (`:1200–1220`) builds its own `DeliveryVerificationSettings` and omits `PostFailureConfirmGraceSeconds`, so it inherits the production 20 s. It is about severity and the PARKED text, not the grace; it gets `PostFailureConfirmGraceSeconds = 3` like the harness default (three grace polls instead of twenty). Same verdict path, 5.5 s virtual instead of 22.9.

### D-8 — One new Unit class, registered in the Linux roster

`tests/Antiphon.Tests/TestHelpers/ScaledTimeProviderTests.cs` (`[Category("Unit")]`, no database) with the six tests in V-1. The Docker roster validator (`scripts/test-docker-container.ps1:40–43`) refuses an unknown or stale class name, so `tests/linux-test-roster.json` gains a row (lane `local`, shard `local-05`, `disposition: include`, `directTestMethods: 6`, `sourceSha256`) and the `local-05` filter in `backendShards` gains `(ScaledTimeProviderTests*)`; the two class files' `sourceSha256` entries are refreshed from `sha256sum`. Hashes are bookkeeping; the validator checks names.

### D-9 — Docs

`docs/testing-and-build.md`: the CARD-0222 gotcha bullet gains the third allowed shape ("or a `ScaledTimeProvider`, which keeps real timers at a fixed multiple; `BridgeQueueHarness.HarnessOptions.ClockSpeed`"). The `BridgeQueueHarness` summary comment names the option. The `slow-tests-allowlist.txt` reason comment for the class states the new cost. The CARD-0728 investigation is historical and is not edited.

## Rejected alternatives

| Alternative | Why not |
|---|---|
| **R-A** Fully virtual clock with a pump that jumps to the next due timer when the code under test is idle | Fastest in theory (about 20 s for the class), but idle detection is the CARD-0222 trap in a new coat: while the service is between polls with a query in flight, the only pending timer can be the test's own "row at 4 s", and the pump would jump the deadline the test is about. A quiescence heuristic either flakes under load or gives the time back. |
| **R-B** `FakeTimeProvider` with `AutoAdvanceAmount`, or with a pump thread calling `Advance` | `AutoAdvanceAmount` moves only on `GetUtcNow()`, which the parked loop never calls; a fixed-step pump is R-A without the idle detection, with timer callbacks running on the pump thread. |
| **R-C** Compress the harness settings further | Floors are reached: evidence and advance windows are `Math.Max(1, …)` seconds, pull and grace cadence is `Math.Max(1000, PollIntervalMs)`, `SubmitAttempts = 3` is the production count the tests assert, and the 3 s grace has to hold the "row lands at 4 s" pins with a poll to spare. |
| **R-D** Per-test isolated schema and drop `[NotInParallel]` | 119 clones at 2 s each is 240 s, and the process-wide clone lock serialises them. |
| **R-E** Split the class by agent kind and run the parts in parallel | Same store, same store-wide sweeps; splitting changes nothing about the coupling. |
| **R-F** One harness per class | ≤ 0.1 s per test measured (D-3). |
| **R-G** Delete the eight duplicate pairs | 2.5 s after D-1 (D-4). |
| **R-H** Lower the production floors (`Math.Max(1000, …)`) | Production code, and the floor exists because each pull fetches a whole transcript (`SessionMessageQueueService.cs:3795`). |

