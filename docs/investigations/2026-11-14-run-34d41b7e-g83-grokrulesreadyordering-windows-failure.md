# Run 34d41b7e group 83: GrokRulesReadyOrderingTests Windows failure (2026-11-14)

Read-only extract. Run 34d41b7e, Windows desktop Full qualification, rc/2026-11-20 =
`b559bfba2e9a420495122d27171352fe9b7e1ba4` (checkout `C:\Antiphon\releases\checkout-8e08368a`).
Sources read: `antiphon-all-group-83.trx`, `antiphon-all-group-83-tests.log`,
`antiphon-all-group-83.timing.json`, `antiphon-all-group-83-execution\log_261114135440636.diag`
under `C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e\`.
No other process, lock or file was touched.

## Verdict

**This is a timing/load failure, not a deterministic product regression and not the g54/g58
stale-contract pattern.** The test runs the real `GrokRulesRefreshService` against the
**real wall clock** (`BridgeQueueHarness` default `TimeProvider.System`) with
`InitializationTimeoutSeconds = 1`. It was the first test of its class to run, in the cold
first wave of a 263-test chunk where about 50 heavy integration tests started in the same
millisecond. The 1-second rules deadline expired before the queue flush stamped the launch rules
row, so `ReconcileAsync` failed it with `timeout`. That happened before the test's fault injection,
so the `(timeout)` argument is a coincidence of ordering, not the cause.

The 1-second budget does no work in this test. The test later forces the deadline into the past
itself (line 101). Raising it, or giving the harness a controllable clock, removes the race
without loosening an assertion.

## Failed test

| Field | Value |
|---|---|
| Test | `Antiphon.Tests.Application.GrokRulesReadyOrderingTests.Ambiguous_input_preserves_launch_and_waits_for_transcript(timeout)` |
| Duration | 00:00:15.0129980 |
| Start / end (UTC) | 13:54:52.532 to 13:55:07.545 |
| Chunk | total 263, failed 1, succeeded 262, skipped 0, run wall 141.8 s, chunk start 13:54:40.40 |
| Host | TUnit 1.44, Windows 10.0.19045, .NET 9.0.23, MTP 2.2.2 |

Assertion (test line 85):

```
ShouldAssertException: written
    should not be null but was

Additional Info:
    the uncertain Input really reached the peer: Antiphon.Server.Application.Exceptions.ConflictException: grok_rules_initialization_failed: timeout
```

Expected: `written` is non-null, meaning the Input frame reached the fake peer. Actual: null.
Launch faulted with `ConflictException(code=grok_rules_initialization_failed, message="timeout")`
inside the 8 s `WaitAsync`, so `error` is that exception and not a `TimeoutException`.

Top frames of the captured `error`:

```
at GrokRulesRefreshService.InitializeAsync(Guid sessionId, CancellationToken ct)  GrokRulesRefreshService.cs:line 176
at GrokRulesRefreshService.InitializeAsync(...)                                   GrokRulesRefreshService.cs:line 163
at AgentSessionService.InitializeGrokRulesAsync(AgentSession session, ...)        AgentSessionService.cs:line 2588
at AgentSessionService.LaunchInteractiveProcessAsync(...)                         AgentSessionService.cs:line 678 / 771
at AgentSessionService.LaunchInteractiveAsync(...)                                AgentSessionService.cs:line 434 / 534
at GrokRulesReadyOrderingTests.Ambiguous_input_preserves_launch_and_waits_for_transcript(String fault)  GrokRulesReadyOrderingTests.cs:line 83
```

Assertion frame: `GrokRulesReadyOrderingTests.cs:85` (`written.ShouldNotBeNull`).

Stdout only: `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.GrokRulesReadyOrderingTests.1.1.Ambiguous_input_preserves_launch_and_waits_for_transcript(System.String).1.1.0`.
No stderr. The `.diag` adds no server or application log lines. It only repeats the same
exception text in TestNodeUpdate messages.

## Mechanism (code at b559bfba2)

`GrokRulesRefreshService.InitializeAsync`:

```
158  var deadline = clock.GetUtcNow().UtcDateTime.AddSeconds(settings.Value.InitializationTimeoutSeconds);
159  ... ExecuteUpdateAsync(... RulesDeadlineAt = deadline)
163  while (true) {
167      await runtime.CatchUpTranscriptAsync(sessionId, ct);
168      await ReconcileAsync(sessionId, ct);
...
175      if (session.GrokRulesState == GrokRulesState.Failed)
176          throw new ConflictException(session.GrokRulesFailure ?? ..., "grok_rules_initialization_failed");
177      await queue.FlushSessionAsync(sessionId, ct);
178-182  if (row Sent/Pending && DeliveryAttempts > 0 && verdict null) throw new InputDeliveryUncertainException(sessionId);
183      await Task.Delay(TimeSpan.FromMilliseconds(250), clock, ct);
```

`Judge` (called by `ReconcileAsync`): when `row.DeliveryAttempts > 0` with Sent/Pending and a null
verdict, it returns early (CARD-1892, line 380). Otherwise line 389 runs:
`if (row.RulesDeadlineAt is { } deadline && now >= deadline) Fail(session, row, "timeout");`.

`SessionMessageQueueService` flush (lines 2950-2966) stamps `DeliveryAttempts++`,
`Status = Sent` and `LastDeliveryStartedAt` **before** typing. It then calls the adapter, whose
`BeforeInput` hook is where the test sets `written`.

So a `timeout` failure requires a `ReconcileAsync` that runs at least 1 s after line 158 while the
launch row still has `DeliveryAttempts == 0`. That means the first flush had not yet stamped and
committed the row. If `BeforeInput` had been entered and the frame had reached the peer, `written`
would be non-null and the loop would end in `InputDeliveryUncertainException`, not `timeout`. The
logs cannot say which of these took the time:

- (a) the first `CatchUpTranscriptAsync` + `ReconcileAsync` took 1 s or more. This is the first
  Grok-rules launch in the process: cold EF query compilation and JIT, under about 50 concurrent
  cold tests.
- (b) a first flush returned without claiming the row, and a later iteration crossed the deadline.

Both are budget overruns of a 1 s wall-clock window. Neither is a wrong returned state.
The clock is real: the test passes no `TimeProvider`/`ClockSpeed`, and
`BridgeQueueHarness.cs:31-32` documents "The default remains TimeProvider.System". The
`FakeTimeProvider transportClock` drives only `PhoneHomeTestHost`, not the rules service.

The 15.01 s duration is setup plus a launch that faulted in under 8 s plus disposal. The logs do
not split those phases. The test's own deadline CTS is 15 s, so the closeness may be a coincidence
or a dispose/wait. The Debug task can confirm this with a local timing run.

## Passing siblings (same class, same process, run sequentially after it: `[NotInParallel("MessageQueue")]`)

| Test | Result | Start (UTC) | Duration |
|---|---|---|---|
| `Ambiguous_input_preserves_launch_and_waits_for_transcript(timeout)` | **Failed** | 13:54:52.532 | 15.013 s |
| `Ambiguous_input_preserves_launch_and_waits_for_transcript(cancellation)` | Passed | 13:55:07.555 | 6.779 s |
| `Ambiguous_input_preserves_launch_and_waits_for_transcript(disconnect)` | Passed | 13:55:14.335 | 2.607 s |
| `Receipt_and_refresh_are_committed_while_readiness_holds_all_input(False)` | Passed | 13:55:16.943 | 1.021 s |
| `Receipt_and_refresh_are_committed_while_readiness_holds_all_input(True)` | Passed | 13:55:17.964 | 6.184 s |

The `cancellation` and `disconnect` cases run code identical to the `timeout` case up to and
including `written = await peer.WaitForAsync(...)`. The `fault` branch comes only after that point.
Both passed in the same process minutes later, when the process was warm. This bounds the cause to
the pre-Input window (launch → rules initialization → first flush), and to timing rather than logic.
The durations also fall as the process warms: 6.8 s, 2.6 s, then 1.0 s.

## What ran at the same time

The failing test started at 13:54:52.532, 12 s into the chunk. Only 4 tests had started before it,
so it was in the first wave. More than 120 tests overlapped its 15 s window. The heaviest were:

- `DispatchConcurrencyMigrationTests.Initialize_from_nondefault_config_then_restart` (69.7 s), `Upgrade_populated_database_preserves_task_rows` (68.8 s), `Interrupted_initialization_rolls_back_seed_and_history`
- `AgentTaskLandingPersistenceTests.C448_V31_MigrationAndConcurrentOperations` (64.0 s, started 13:54:52.532), `C448_V31_ConcurrentOperationsAreFenced`
- `DelegationBriefCeilingPtyTests.The_same_brief_typed_inline_silently_loses_a_whole_chunk` (40.4 s, Pty, started 13:54:52.532)
- `CardFilePrivacyBoundaryAcceptanceTests.*` (incl. 19.9 s `Hostile_notes_...`), `CardFileIgnoreAcceptanceTests.*` (git-heavy, 8 cases starting 13:54:52-13:55:00)
- `ExpectationSnapshotTests.C650_Persisted_lands_...` (14.4 s), `ExpectationWatchdogScenarioTests.C650_Idle_and_working_...` (13.7 s)
- `DefaultRunnerCreateTests.*` (7 cases, up to 9.4 s), `ComplexityChainServiceTests.*` (10 cases, all started 13:54:52.532)
- `PhoneHomeTaskDispatchProjectionTests.*` (15 cases), `PhoneHomeTranscriptIncidentTests.*` (5), `SessionStateCommitTests.*` (8, about 8-11 s each), `LandRestartReplayTests.C1379_...` (17.7 s), `RunnerDefaultsWireTests.*`, `RunnerRetireJobTests.*`, `HostEndpointTests.*`, `AgentServiceIntegrationTests.*`

Chunk p50 was 1.97 s and p95 was 9.37 s. Fixture setup took 3.8 s and the build took 583 s
(timing.json).

## Is it Windows-specific?

- **Paths and line endings: no.** `CaptureReceiptAsync` (receipt path `/private/instructions/grok/...`
  and sha/byte validation) completed before line 158. Otherwise the stack would point at
  `CaptureReceiptAsync`/`ValidateExpectedReceipt`, not line 176. The payload is `"private rules\n"`
  and is hashed by the same code on both sides.
- **Timer resolution: no.** About 15.6 ms of Windows timer granularity on a 250 ms delay cannot
  consume a 1000 ms budget.
- **Load and process ordering: yes, in effect.** The failure needs at least 1 s between line 158 and
  the first committed flush stamp. On this desktop that happened in the cold first wave of a
  263-test chunk, with git-, Pty- and migration-heavy neighbours, during a Full qualification.
  The Linux CARD-1892 proof ran the class alone.
- Linux evidence: plan `docs/superpowers/plans/2026-11-13-card-1892-input-uncertainty.md` row
  `CP-4 | all | CP-2 | rules-readiness-uncertainty | /*/*/GrokRulesReadyOrderingTests/* | ... | 5 | 1`
  ran on server2 and passed (`CP-4 5/5`, full group at `780ac1aca`). The same plan says twice
  "Native Windows proof remains owed by orchestrator" and "Native Windows remains caller-owned".
  No master or Linux run of this class with a failure was found. The class-only filter ran it
  with little or no concurrent load.

## What changed in this cut

- `Ambiguous_input_preserves_launch_and_waits_for_transcript` is **new in this cut**. It is absent
  from `origin/rc/2026-11-19`: `git grep` finds no match. It was added by `edaa607c8`
  (test(card-1892): pin unknown delivery launch survival, 2026-11-13) and amended by `47d86c514`
  (correct receipt fixture). It is on master.
- Product change it covers: `5b80144d3` fix(card-1892): retain live sessions and attempts after
  ambiguous Input. It added the `InputDeliveryUncertainException` check at
  `GrokRulesRefreshService.cs:178-182`, the early return in `Judge` (line 380), and the
  `SessionMessageQueueService` catch that keeps the Sent attempt instead of reverting it. None of
  these touch the pre-stamp window that expired here. The deadline (line 158) and the
  `timeout` Fail (line 389) predate this cut.
- So the new piece is the test's **1-second real-clock budget** (`GrokRulesSettings {
  InitializationTimeoutSeconds = 1 }`, test line 41) on a launch path that does real DB work. The
  sibling `Receipt_and_refresh_...` test uses the default settings and passed.

## Why it was missed before rc

CP-4 ran `GrokRulesReadyOrderingTests` as a single class on Linux, so it absorbed no cold-start
contention. The test's correctness depends on a sub-second wall-clock window that the selection
never exercised under chunk load. Native Windows proof was explicitly left to the orchestrator.
The plan does not show the 1 s budget as a timing assumption. The performance standard's
"budget ~2x p95" rule was not applied to this in-test timeout.

## Suggested Debug direction (for the next stage, not done here)

Remove the race without loosening an assertion:

- Option 1: set `InitializationTimeoutSeconds` well above cold-path p95 (for example 30). The test
  already forces expiry itself at line 101 (`refresh.RulesDeadlineAt = DateTime.UtcNow.AddSeconds(-1)`).
- Option 2: register a controllable `TimeProvider` for the rules service, so the deadline cannot
  elapse before the flush stamp. Line 183's `Task.Delay(..., clock, ...)` would then need
  advancing.

Then run a red-under-load proof, such as a local repeat with a parallel heavy sibling, and run the
class on native Windows. Check whether other tests pair `InitializationTimeoutSeconds = 1` with
the real clock. `git grep` at b559bfba2 shows only this test sets it.
