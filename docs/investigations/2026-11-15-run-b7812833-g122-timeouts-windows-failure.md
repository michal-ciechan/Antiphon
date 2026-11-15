# Run b7812833 group 122: three TimeoutExceptions (Windows failure read)

- Run: b7812833 (Full, seeded failed-first), rc/2026-11-21 = `64e3d78c487d51570f708492ab67b5db6d0de9a9`, desktop (8 logical CPUs).
- Chunk: `antiphon-all-group-122` (Antiphon.Tests, 296 tests, 293 passed, 3 failed). Chunk window 05:09:16.7Z - 05:10:31.7Z
  (not 04:50-04:55Z; the run started 04:33Z). `runWallSeconds` 74.8, `p95Seconds` 11.06, `maxSeconds` 45.2.
- Sources read (read-only): `logs/2026-11-15-0434-b7812833/antiphon-all-group-122.trx`, `-tests.log`, `.timing.json`,
  `-execution/log_261115050916937.diag`; comparison TRX/timing from runs 34d41b7e (b559bfba2, group 121), 97150a0b
  (ab7af12c, group 120) and five earlier rc runs.

## Verdict

**Not a regression from b559bfba2..64e3d78c4. It is a Windows-only, load-amplified thread-pool starvation set off by
`HangfireStorageSelectionTests.Unreachable_tick_retries_until_the_crash_expiry_then_stops_the_host` itself**, and the other two
failures are victims of it in the same process. The starvation has a self-inflicted cause in the production lease code's
supervision timer and a test that ticks it every 50 ms against a closed loopback port.

Mechanism (code at the cut tip, unchanged since at least ab7af12c):

1. The test calls `lease.StartSupervision(lifetime, 50 ms)` with connection string `Host=127.0.0.1;Port=1;...;Timeout=1;Pooling=false`
   (`tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs:186-214`).
2. `StartSupervision` uses a `System.Threading.Timer` with no reentrancy guard
   (`server/Infrastructure/Agents/HangfireConfiguration.cs:580-589`). Every 50 ms another `OnTick` is queued to the thread pool
   even while earlier ticks are still running.
3. `OnTick -> EnsureHeld` takes `lock (_gate)` and inside it does synchronous `NpgsqlConnection.Open()` calls
   (`SessionStillHoldsLock` / `OwnOpenBackendStillHolds -> OpenFresh` / `ReplaceConnection`; lines 554-577, 705-715).
4. On Linux a connect to 127.0.0.1:1 is refused in about a millisecond, so each tick finishes at once and nothing piles up.
   On Windows a refused loopback connect takes about 2 s (measured on this desktop: **2,075 ms** for `TcpClient.Connect("127.0.0.1", 1)`),
   so each `Open()` blocks for the whole `Timeout=1` (1 s), and a tick that does more than one open holds `_gate` for 1-3 s.
5. Meanwhile 20 ticks/s arrive. Each one takes a pool thread and blocks on `Monitor.Enter(_gate)`. The pool (minimum 8 threads
   on this host) only injects threads slowly, so the global queue backs up. Every other pool work item in the test process
   (`Task.Run` bodies, `await` continuations, `Task.Delay` callbacks) waits behind that backlog. The longer the test runs, the
   bigger the backlog, so the effect feeds itself. External host load makes the first seconds slower, which grows the
   backlog further.

The production interval is `ReacquireInterval = 15 s` (line 449), so the pile-up needs an `Open()` that blocks for more than
15 s in production. That is unlikely but not impossible (the default Npgsql connect timeout is 15 s). This is noted as a
latent product edge, not as the cause of this failure.

## Failed tests

### 1. `C1307ToolRemovalTests.Read_deadline_leaves_marker` (6.298 s; 05:09:32.515 - 05:09:38.813)

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: The operation has timed out.
  at Antiphon.Tests.Infrastructure.C1307ToolRemovalTests.Read_deadline_leaves_marker() in ...\C1307ToolRemovalTests.cs:78
  at Antiphon.Tests.Infrastructure.C1307ToolRemovalTests.Read_deadline_leaves_marker() in ...\C1307ToolRemovalTests.cs:94
```

- Line 78 is `await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));`. This is a precondition wait for the
  `Task.Run(() => f.Cleanup(...).Remove(f.Run))` body to reach the blocked read stream. It is not the assertion under test.
  Expected: entered within 5 s. Actual: not entered within 5 s.
- There is no expected/actual value assertion. No stderr apart from `ANTIPHON_TEST_PHASE_ID`.
- Started in the same millisecond as the Hangfire tick test (05:09:32.515 vs .516).
- Passing siblings, all started 05:09:32.515 and finished in 0.35-1.32 s: `Bound_parent_swap_is_refused_before_any_marker`,
  `Bound_record_uses_the_anchored_delete`, `Bound_identity_mismatch_is_refused_before_any_marker`,
  **`Mid_removal_cancellation_leaves_marker`** (same `BlockAfterFirstUnlink` + 5 s `Entered` wait, passed in 1.00 s),
  `Complete_removal_and_manual_recovery_are_absent`, `Legacy_unbound_record_is_removed_by_the_base_path_delete`,
  `Custody_refusal_preserves_sentinels`, `Marker_failure_prevents_tool_unlink`.

### 2. `HangfireStorageSelectionTests.Unreachable_tick_retries_until_the_crash_expiry_then_stops_the_host` (23.065 s; 05:09:32.516 - 05:09:55.581)

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: Test 'Unreachable_tick_retries_until_the_crash_expiry_then_stops_the_host' timed out after 00:00:20
--- Task Status: WaitingForActivation ---
--- Timeout Handler Stack Trace ---  (TUnit timer thread only; no stack for the blocked test body)
Standard output: Microsoft.Hosting.Lifetime "Application started." / "Hosting environment: Production" / content root ...\tests\Antiphon.Tests\bin\Debug\net9.0
```

- This is the test's own `[Timeout(20_000)]`. The body was still awaiting (`WaitingForActivation`) after 20 s. The test needs
  0.8 s plus at most 5 s in its own code (`Task.Delay(800 ms)` and then `WhenAny(stopping, Delay(5 s))`), so for it to pass
  its own continuations have to be scheduled.
- The 3 s gap between the 20 s bound and the 23.065 s duration is also scheduling delay. The timeout-handler continuation was
  itself late.
- Passing siblings (all ran after it, 05:09:55.59 - 05:09:59.89): `Crash_expiry_window_follows_the_monotonic_clock_not_the_wall_clock`,
  `Unreachable_postgresql_storage_refuses_startup` (2.04 s, consistent with the 2 s Windows refused-connect),
  `Disabled_retire_removes_a_previously_registered_job`, `Disabled_slot_reconcile_removes_a_previously_registered_job`,
  `Disabled_watchdog_removes_a_previously_registered_job`, `Disabled_residue_removes_a_previously_registered_job`,
  `Disabled_census_removes_a_previously_registered_job`, `Desktop_configuration_does_not_turn_durable_storage_on`,
  `Validator_rejects_an_unknown_storage_and_the_public_schema`, `Default_settings_stay_on_in_memory_storage_for_eight_days`,
  `PostgreSql_bootstrap_resolves_storage_and_refuses_an_unreachable_database`.

### 3. `HostCleanupCheckpointReceiptTests.Receipt_size_and_deadline_are_bounded` (30.333 s; 05:09:33.912 - 05:10:04.246)

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: The operation has timed out.
  at Antiphon.Tests.Infrastructure.HostCleanupCheckpointReceiptTests.Receipt_size_and_deadline_are_bounded() in ...\HostCleanupCheckpointReceiptTests.cs:119
  at Antiphon.Tests.Infrastructure.HostCleanupCheckpointReceiptTests.Receipt_size_and_deadline_are_bounded() in ...\HostCleanupCheckpointReceiptTests.cs:139
```

- Line 119 is again `await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));`: the
  `Task.Run(() => ReceiptJson.Read<RunState>(...))` body did not reach the blocked stream within 5 s. Line 139 is the `finally`
  that releases the stream and awaits the read. That await took most of the remaining ~25 s, because the `Task.Run` body
  itself was still queued.
- No assertion on returned state failed. The 64 MiB size-cap half of the test ran first and did not throw.
- Passing siblings, all finished in 0.02-0.59 s: `Valid_terminal_receipt_is_accepted`, `Receipt_read_errors_do_not_write_or_adopt`,
  `Report_writes_require_verified_readback`, `Terminal_receipt_requires_digest_source_and_exit`, `Receipt_read_uses_verified_handle`,
  `Receipt_types_are_checked_before_open`, `Receipt_shape_and_identity_are_required`. All of them are synchronous and none uses a
  `Task.Run` handshake.

## The decisive evidence: an unrelated in-memory test stalled in lock-step

`HostCleanupExecutionTests.Plan_persist_failure_prevents_delete` and `Explicit_plan_file_is_the_only_preview_write` are pure
in-memory async tests (0.04-0.25 s in every earlier run). In this run they **passed** in 29.67 s. They started at 05:09:34.58
and finished at **05:10:04.248 and 05:10:04.246**, within 2 ms of the failing receipt test (05:10:04.246). `LogRetentionTests`
also finished at 05:10:04.239. Several unrelated pool-dependent tests released at the same instant. That points to a
process-wide scheduling backlog clearing (the lease timer stopping once the abandoned Hangfire body finally ran its `using`
disposal), not to any one test's logic.

The TUnit diag log shows no global freeze. Other tests kept completing across the window (05:09:38.8, 05:09:45.9, 05:09:50.0,
05:09:52.0 ...; the largest inter-message gap in 05:09:32-05:10:04 is 5.0 s). So this was not a GC pause or a suspended
process. Work was being delayed selectively, and that is what thread-pool queue starvation looks like.

## Windows history of the same tests (seconds)

| Run (pin) | Hangfire tick | Receipt_size | Read_deadline | Plan_persist |
|---|---:|---:|---:|---:|
| a6410818 (5fef2c23) | 2.23 | 1.13 | 1.59 | 0.04 |
| 231f573c (653f1145) | 2.20 | 1.71 | 0.44 | 0.21 |
| dce78ee0 (7bee4361) | 2.18 | 2.94 | 0.58 | 0.25 |
| 6591e378 (ac6d0002) | 2.18 | - | 1.20 | 0.00 |
| ae84478d (3db1a43b) | 2.12 | - | 0.94 | 0.00 |
| 0b292fa4 (56e96eab) | 2.49 | 2.13 | 0.86 | 0.00 |
| 97150a0b (ab7af12c) | 2.66 | 1.36 | 1.98 | 0.05 |
| 34d41b7e (b559bfba) | **8.36** | **5.81** | 0.42 | 0.06 |
| b7812833 (64e3d78c) | **23.06 F** | **30.33 F** | **6.30 F** | **29.67** |

In 34d41b7e the same pattern was already visible below the bounds. The receipt test needed 5.81 s, against a 5 s `Entered`
wait plus about 0.7 s of work. The seven `HostCleanupExecutionTests` that started at 15:25:33.26 were followed by a 4.9 s gap
before the next batch at 15:25:38.17.

`git log ab7af12c6990..64e3d78c4` touches none of `server/Infrastructure/Agents/HangfireConfiguration.cs`,
`tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs`, `C1307ToolRemovalTests.cs`,
`HostCleanupCheckpointReceiptTests.cs`, `HostCleanupExecutionTests.cs`, `src/Antiphon.HostCleanup/**` or
`tools/Antiphon.Checkpoints/**`. Chunk composition is the same across 97150a0b g120, 34d41b7e g121 and b7812833 g122: 30 classes,
and the only classes that differ are ones with 0-0.2 s total. The drift is in host conditions, which the starvation loop amplifies:

| Class total (s) | 97150a0b | 34d41b7e | b7812833 |
|---|---:|---:|---:|
| GitTreeTemplateTests | 57.7 | 72.3 | 157.9 |
| KanbanPersistenceTests | 6.4 | 12.8 | 76.0 |
| HostCleanupExecutionTests | 0.2 | 0.3 | 59.4 |
| HostCleanupCheckpointReceiptTests | 5.3 | 8.1 | 32.0 |
| HangfireStorageSelectionTests | 8.3 | 13.8 | 27.4 |
| DbCommandMetricsTests | 4.4 | 13.7 | 19.3 |
| chunk processWaitSeconds | 48.8 | 59.1 | 120.7 |

Across the 285 tests common to b7812833 g122 and 34d41b7e g121, the summed duration is 461.5 s vs 194.0 s (x2.38).

## Host load at the time

- Desktop host-load history: `GET /api/hosts/desktop/load/history?from=04:30Z&to=05:15Z` returned `items: []`,
  `gaps: ["binding-unknown"]`. **The desktop records no host-load samples**, so the CPU at 05:09Z cannot be quoted.
- Qualification chunks run one after another: only group 112 (ended 05:09:16) and group 122 overlap this window.
- Antiphon delegated tasks in flight at 05:09Z (e594649c Code, 00011235 Merge, 73e7dfdd Code, 665816dd Investigate) were all
  on `runnerId=server2`. No desktop-placed task was running. The desktop's other occupants (AppHost, production runner, Docker
  Desktop, interactive sessions) have no load record either.

## Isolated reruns on Linux (server2, `antiphon-server2/session-testing:ab7af12c6990`, .NET 9.0.23, `--cpus 6`)

Each test ran alone through `dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter "/*/*/<Class>/<Method>"`, 5 times.

| Test | 64e3d78c4 (tip) | b559bfba2 (base) |
|---|---|---|
| C1307 Read_deadline_leaves_marker | 5/5 pass; 0.33, 0.46, 0.58, 0.64, 0.64 s | BASE_C1307 |
| Hangfire Unreachable_tick_... | 5/5 pass; 1.03, 1.04, 1.07, 1.02, 1.02 s | BASE_HANGFIRE |
| Receipt_size_and_deadline_are_bounded | 5/5 pass; 0.69, 0.57, 0.83, 0.89, 0.89 s | BASE_RECEIPT |

REPRO_SECTION

## Classification

- **Timing/load, not deterministic.** All three failures are timeouts on scheduling waits:
  `Entered.Task.WaitAsync(5 s)` twice, and the test-level `[Timeout(20_000)]` once. None is an assertion on returned state.
  Every same-class sibling that does not depend on a `Task.Run` handshake passed. All three pass 5/5 alone at the tip on Linux.
- **But not a random flake.** The Hangfire tick test causes the starvation on Windows, through a deterministic mechanism. Its
  severity grows with host load and with how long the test runs. The assembly has **53**
  `Entered.Task.WaitAsync(TimeSpan.FromSeconds(5))` handshakes in total (19 files, including the 3 above), and each one is exposed whenever that test is
  co-scheduled with them.

## Recommendation for the Debug stage

Bound policy (`docs/test-performance-standards.md` R5): widening the 5 s handshakes or the 20 s bound is **not** justified. The
measured p95 on Linux is about 1 s, and on Windows about 2.7 s for the Hangfire test before the drift. The real defect is the
pile-up, and a wider bound would only hide it.

1. Product (red first): make `HangfireWorkerLease` supervision non-reentrant. For example, skip a tick when the previous one is
   still running (`Interlocked` flag or `Monitor.TryEnter(_gate)`), or use a one-shot timer re-armed after each tick. Red
   witness: with a fake connection whose `Open()` blocks for N times the period, assert that at most one `OnTick` is ever in
   flight. A cross-platform route is a blackholed host or an injected opener, rather than Windows' refused-connect latency.
2. Test: stop the tick test from occupying the shared pool for seconds on Windows. Make the probe fail fast (an injected
   failing opener or a refused connect on Windows), or use a period larger than the opener's block time.
3. Then rerun group 122's three tests plus `HostCleanupExecutionTests` on native Windows. Expect the Hangfire test back at about
   2 s and the receipt test below 1 s.

---

Evidence paths (read-only, desktop): `C:\Antiphon\releases\candidates\rc-2026-11-21-64e3d78c487d\logs\2026-11-15-0434-b7812833\antiphon-all-group-122{.trx,-tests.log,.timing.json,-execution\log_261115050916937.diag}`;
`C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e\antiphon-all-group-121{.trx,.timing.json}`.
