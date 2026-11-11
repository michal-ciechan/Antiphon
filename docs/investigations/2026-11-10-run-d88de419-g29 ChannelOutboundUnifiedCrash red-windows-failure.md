# Run d88de419 g29: ChannelOutboundUnifiedCrash red on Windows

Read-only extract, task 62d5b3f9. Qualification run `d88de419025b4d368fb75108e4de0c36`
(`2026-11-11-1142-d88de419`, still running at this read), chunk `antiphon-all-group-29`,
rc/2026-11-16 = `6f8741d872d63507827014f972ccf71196722d4a`, desktop Windows 10.0.19045,
TUnit 1.44.0, .NET 9.0.22. Nothing was run, restarted or created under `C:\Antiphon`
outside this worktree. No Docker or process was touched.

Sources read:

- `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\logs\2026-11-11-1142-d88de419\`
  `antiphon-all-group-29.trx`, `antiphon-all-group-29-tests.log`,
  `antiphon-all-group-29.timing.json`, `antiphon-all-group-29-execution\log_261111140609096.diag`.
  No `console.log` or `*.hang.txt` exists for this chunk.
- Run 6591e378 `antiphon-all-group-28.trx`. In that run the class was in group 28, not 29.
- The `.trx` files for the class in all 20 earlier Windows rc runs under
  `C:\Antiphon\releases\candidates\rc-2026-1*\logs\2026-*`.
- Final-result note for 6591e378: `docs/investigations/2026-11-11-run-6591e378-final-result.md`
  at `7a3c45c3f82e36c7b992d5b4b66316cd52127b47` on `origin/feat/card-task-d6579463`.

## Verdict

**Timing/load flake. Not a regression in this cut, and not deterministic.** One matrix cell
waited 30 s for the crash probe's cut marker. In that time the probe neither wrote the marker
nor exited. No assertion on returned state ran. The cause of the stall cannot be pinned from
the evidence: the harness throws a bare `TaskCanceledException` and drops the child's
stdout and stderr. The probe's logging is silent (`AddLogging()` with no provider). The first
Debug step is diagnostics, not a budget change (see Fix direction).

## Chunk summary

| Field | Value |
|---|---|
| Chunk | `antiphon-all-group-29` |
| Started / completed | 2026-11-11T14:06:08.815Z / 14:24:56.097Z |
| Build | 710.7 s (shared run build, `buildSlot` not-used) |
| Run wall | 1127.2 s |
| Tests | 84 total, 83 passed, **1 failed**, 0 skipped |
| Classes | `C1729ParkFingerprintTests` (1), `CodexStartupDeliveryTests` (62), `ChannelOutboundUnifiedCrashTests` (21) |
| p50 / p95 / max | 0.903 s / 73.998 s / 108.191 s |
| `lastPhase` | complete, `why` empty |

## The failed test

`Antiphon.Tests.Application.ChannelOutboundUnifiedCrashTests.C519_Captured_death_needs_no_wake_signal(trailing)`
(`PreparationAsync("trailing", "capture-committed")`).

- Outcome: Failed. Duration `00:00:37.8614085`. TRX start 14:10:25.745Z, end 14:11:03.607Z.
- Message: `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TaskCanceledException: A task was canceled.`
- Expected vs actual: there is no value assertion. `ReachAsync` expected the probe's cut marker
  (`point = "capture-committed"`) or a probe exit within 30 s. Actual: neither happened, and the
  `Task.Delay(25, timeout.Token)` poll was cancelled.
- Stack (top frames, paths under `C:\Antiphon\releases\checkout\`):

```
at Antiphon.Tests.TestHelpers.UnifiedOutboundTransport.WaitForAsync(Func`1 predicate)            UnifiedOutboundTransport.cs:225
at Antiphon.Tests.TestHelpers.UnifiedOutboundTransport.OwnedProbe.ReachAsync(String point)        UnifiedOutboundTransport.cs:249
at Antiphon.Tests.Application.ChannelOutboundUnifiedCrashTests.PreparationAsync(String kind, String cut)  ChannelOutboundUnifiedCrashTests.cs:43
   (async frames :56, :68, :68)
   --- TUnit internals omitted ---
```

Line 225 is `while (!predicate()) await Task.Delay(25, timeout.Token);`, with `timeout` a fixed
30 s `CancellationTokenSource`. Line 249 is `await WaitForAsync(() => File.Exists(marker) || process.HasExited);`.
Line 43 is `await child.ReachAsync(cut);`. So the failure is the 30 s reach poll, before any check on
the `ChannelOutboundDeliveries` row.

- Stderr: none recorded. The TRX `StdErr` is empty. `OwnedProbe` reads the child's stdout and
  stderr into tasks, but surfaces them only from `ReachAsync` when the child exited. On the timeout
  path they are discarded. The probe is then killed by `DisposeAsync`.
- Stdout (the test's own, complete):

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.ChannelOutboundUnifiedCrashTests.1.1.C519_Captured_death_needs_no_wake_signal(System.String).2.1.0
[testcontainers.org 00:04:12.30] Docker container b0ea92280efe created
[testcontainers.org 00:04:13.31] Start Docker container b0ea92280efe
[testcontainers.org 00:04:14.45] Add file to tar archive: Content length: 219 byte(s), Target file: "/testcontainers.sh", UID: 0, GID: 0, Mode: 0755
[testcontainers.org 00:04:14.45] Copy tar archive to container: Content length: 219 byte(s), Docker container: b0ea92280efe
[testcontainers.org 00:04:14.48] Wait for Docker container b0ea92280efe to complete readiness checks
[testcontainers.org 00:04:15.72] Docker container b0ea92280efe ready
[testcontainers.org 00:04:48.01] Delete Docker container b0ea92280efe
```

Timeline: testcontainers' relative clock `00:04:10.12` matches the previous cell's end at 14:10:25.74Z.

| UTC (approx.) | Event |
|---|---|
| 14:10:25.75 | Test start |
| 14:10:27.9 | Redpanda container created (2.2 s) |
| 14:10:31.3 | Redpanda ready (3.4 s create to ready; the previous cell took 2.2 s) |
| 14:10:31.3 to about 14:10:33.6 | Schema clone, topic, fake Slack, harness, trailing seed (turn end, drain, wait for root receipt), probe start: **at most about 2.3 s, so the parent side was healthy** |
| about 14:10:33.6 to 14:11:03.6 | `ReachAsync` 30 s poll: no marker, no exit |
| 14:11:03.6 | Container deleted, test failed |

The probe stalled for the full 30 s. `UnifiedRecoveryProbe.RunAsync` also carries its own 30 s
deadline, which starts only after child runtime startup. The parent's 30 s therefore always expires
first. That makes a probe-side timeout, and its stderr, unobservable by construction.

What ran at the same time: nothing in the chunk. The class is `[NotInParallel]` with
`ParallelLimiter<ProcessSpawnLimit>`. The TRX rows are strictly sequential. The previous row ended
14:10:25.744 and the next began 14:11:03.621. No other chunk of the run overlapped
14:10:25 to 14:11:04 (per-chunk `*.timing.json`). The only out-of-order chunk was `antiphon-all-group-118`
(12:16:15 to 12:21:54, `lastPhase=timeout`, `why=completed-but-hung`), which ended almost two hours earlier.
Host load from outside the qualification (AppHost, agent sessions, other worktree builds) is not
recorded in these logs. The diag file holds only TUnit message-bus traces for this window and no
probe output.

## Passing siblings (bound the cause)

All 20 other cells of the class passed in the same chunk, sequentially, on the same build and the same Docker:

| Method | main | trailing | machine |
|---|---|---|---|
| `C519_Before_capture_death_is_discovered` | 28.9 s | 30.1 s | 32.5 s |
| `C519_Captured_death_needs_no_wake_signal` | 33.2 s | **FAILED 37.9 s** | 35.7 s |
| `C519_Partial_stage_death_retries_preparation` | 29.5 s | 30.9 s | 32.8 s |
| `C519_Complete_stage_death_preserves_snapshot` | 30.1 s | 25.0 s | 28.6 s |
| `C519_Attempt_death_stays_uncertain` | 104.6 s | 108.2 s | 104.4 s |
| `C519_Accepted_death_stays_uncertain` | 74.0 s | 74.9 s | 68.0 s |
| `C519_Published_death_never_replays` | 25.0 s | 29.4 s | 32.8 s |

What this bounds:

- The same method passed for `main` and `machine` immediately before and after the failing cell.
- Every other `trailing` cell passed. That includes three that go through the identical
  probe path to capture: discovery of the open root, `DiscoverRootAsync`, `CaptureAsync`.
  `Before_capture(trailing)` stops at `capture-before-commit`, inside the same `CaptureAsync`
  call, one statement before the failing barrier.
- So the code path from seed to `capture-committed` works for the trailing kind in this cut and on
  this host. The failing cell did not reach it in 30 s once.

All 62 `CodexStartupDeliveryTests` and the `C1729ParkFingerprintTests` row also passed. They ran before
the crash class and do not overlap it.

## Deterministic or timing

**Timing/load.** The evidence:

1. The failure is the expiry of a fixed 30 s poll (`TaskCanceledException` at `WaitForAsync` line 225).
   No `Shouldly` assertion on a returned row or state failed.
2. The identical path passed in 5 sibling cells in the same chunk, including 3 trailing cells.
3. History: the class passed **420 of 420 cells in the 20 earlier Windows rc runs**
   (27a1b370 to 6591e378, 21 cells each). This is the first failure, at **1 of 441** overall.
4. This run is slow across the class. Median preparation-cell time is **30.9 s** in d88de419, against
   22.2 s in 6591e378 (1.39x) and 17.4 to 30.7 s across all 20 earlier runs. Every C519 cell is
   slower than in 6591e378 group 28: `Captured(trailing)` took 22.0 s there and passed,
   `Attempt(trailing)` 79.8 s there against 108.2 s here.
5. Docker was slower around the failure. Redpanda create-to-ready took 3.4 s in the failing cell,
   then 5.8 s and 6.75 s in the next two machine cells. The other cells took 1.4 to 2.7 s.
   PostgreSQL is a Docker testcontainer too (`TestDbFixture`), so the probe's DB calls share that VM.

The size of the stall argues against a budget that is merely tight. A preparation cell runs three probes:
the cut probe, then two `RecoverAsync` probes of at least three 1 s cycles each. In this run's passing
cells the whole container-ready-to-delete span is 23 to 30 s. The cut probe's reach phase is
therefore roughly 5 to 10 s under this run's load. The failing cell needed more than 30 s, a 3 to 6x
outlier. That points to a discrete stall in the child process. Candidates are a WSL2/Docker or
PostgreSQL stall, a slow `dotnet` child cold start, or a swallowed discovery exception that the
next cycle retried. It does not point to uniform slowness. The evidence cannot tell these apart,
because the child's output is discarded and its logging is silent.

## What changed between the cuts (ac6d00022 to 6f8741d87)

There are 213 commits. Nothing changed in the class, the probe or the harness:

- `tests/Antiphon.Tests/Application/ChannelOutboundUnifiedCrashTests.cs`: unchanged.
- `tests/Antiphon.Tests/TestHelpers/UnifiedOutboundTransport.cs`: unchanged.
- `tests/Antiphon.ChannelOutbound.Probe/**`: unchanged.
- `server/Application/Services/ChannelOutbound*`, `server/Infrastructure/Supervision/ChannelOutbound*`,
  `server/Infrastructure/Files/ChannelOutbound*`: unchanged.
- `server/Application/Services/ChannelReplyDispatcher.cs`: `e7660e4be` (CARD-1455) only changes two
  `LogInformation` calls to `LogWarning` for abandoned channel debt. It does not change behaviour on this path.
- `server/Infrastructure/Data/AppDbContext.cs` plus migration `20261110213738_Card1294ShadowCohort`:
  an additive shadow-event table (CARD-1294). The probe does not migrate and does not touch the table.

In 6591e378 the class was in `antiphon-all-group-28`. All 21 cells passed there (02:51 to 03:03Z,
21.4 to 24.5 s for preparation cells). 6591e378's `antiphon-all-group-29` held different classes
(`AgentExecutableResolverTests`, `AgentTaskLandQueuedReceiptTests`, `DelegationWorktreeTests`) and passed.
So "group 29 not failing before" reflects renumbering, not new tests in the chunk.

## Rerun

**Not rerun.** The task's cleanup rule forbids touching Docker and asks for no added load on the live run.
A rerun of this cell starts PostgreSQL, Redpanda and Ryuk testcontainers on the desktop Docker. It also
needs an `Antiphon.Tests` build (about 12 min) beside the live qualification. Both would load the host
whose load is the suspected cause. The flake rate above comes from history instead: 1 of 441
cells, 0 of 420 before this run. **Native Windows proof is owed.** The Debug stage should run
`--treenode-filter "/*/*/ChannelOutboundUnifiedCrashTests/C519_Captured_death_needs_no_wake_signal"`
three or more times on Windows once d88de419 finishes, through `scripts/build-slot.ps1`, with the
diagnostics below in place.

## Fix direction (for Debug; do not weaken assertions)

1. **Make the timeout diagnosable.** On expiry, `OwnedProbe.ReachAsync` should report elapsed time,
   `process.HasExited`, whether `marker` or `marker.tmp` exists, and the child's stdout and stderr.
   It should kill the child first so the stream tasks complete.
2. **Give the probe a voice.** `UnifiedRecoveryProbe` registers `AddLogging()` with no provider, so
   `ChannelOutboundDiscoveryService`'s `LogWarning` on a failed `DiscoverRootAsync`, and the hosted
   service's `LogError`, are silent. Add a stderr console provider, and write phase timestamps
   (runtime up, host started, end of each cycle, barrier hit) to stderr.
3. **Order the budgets.** The parent's reach budget must be strictly longer than the child's own 30 s
   `deadline`, so a child-side stall surfaces as `Child exited before cut: <stderr>`. Tie it to the
   test's existing 120 s `deadline` (today only checked at the end) rather than a free-standing 30 s.
   Set the final value at about 2x the reach p95 measured from step 2 timestamps under Full-run load,
   per `docs/test-performance-standards.md` R5. There is no measured reach p95 today. The about 5 to 10 s
   estimate above is derived, not measured, so it does not justify a number yet.
4. If step 2 shows a swallowed discovery or capture exception that a later cycle retries, that is a
   product race to fix, not a budget.

## Why missed

- **The failure needs host load that pre-rc gates never apply.** Code and Review run narrow filters
  on lightly loaded hosts, mostly server2 Linux. The class passed 420 of 420 Windows cells in 20 Full rc
  runs, at medians of 17 to 31 s. This run was the slowest on record for the class (median 30.9 s),
  and the single stall landed in it.
- **The fixed 30 s reach poll was never measured against a loaded p95.** The test has a 120 s
  per-cell deadline, but its inner waits (`WaitForAsync`, `CompleteAsync`) are hard-coded at 30 s,
  equal to the child's own 30 s deadline. No phase timing is recorded, so no review could compare the
  budget with data (R5).
- **The failure mode was built blind.** On timeout the harness discards the child's stdout and stderr,
  and the child logs to no provider. A stall therefore leaves only `TaskCanceledException`, and the
  first occurrence cannot be root-caused from the run artefacts.
- **Nothing in the cut touched this path**, so impact-based selection and review had nothing to flag.
  The diff above is empty for the class, the probe, the harness and the channel outbound services.

## Next

Debug task. Add the diagnostics (fix directions 1 and 2) and order the budgets (3), then measure
reach p95 on native Windows under load and set the budget from it. Re-run the cell natively on
Windows after d88de419 completes. For this run's seed and rerun list, treat
`antiphon-all-group-29` as a flake rerun: one red cell, no code change implicated.
