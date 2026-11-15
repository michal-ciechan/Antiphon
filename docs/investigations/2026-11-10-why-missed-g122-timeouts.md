# Why missed: g122 timeouts (Hangfire lease ticks starve the test thread pool)

One committed note for the rc/2026-11-21 group-122 failure on the native Windows run `b7812833` (cut `64e3d78c487d51570f708492ab67b5db6d0de9a9`). Evidence: `C:\Antiphon\releases\candidates\rc-2026-11-21-64e3d78c487d\logs\2026-11-15-0434-b7812833\antiphon-all-group-122.trx` and `antiphon-all-group-122-tests.log`.

## Failure

Three `TimeoutException`s in one process (296 tests, 293 passed):

- `C1307ToolRemovalTests.Read_deadline_leaves_marker` 6.3 s, line 78: `stream.Entered.Task.WaitAsync(5 s)`.
- `HostCleanupCheckpointReceiptTests.Receipt_size_and_deadline_are_bounded` 30.3 s, line 119: the same `Entered` wait.
- `HangfireStorageSelectionTests.Unreachable_tick_retries_until_the_crash_expiry_then_stops_the_host` 23.1 s: `[Timeout(20_000)]`.

The Hangfire test started at 05:09:32.516 and C1307 at 05:09:32.515. Both waits time out before a `Task.Run` body got a pool thread, which points to thread-pool starvation, not a slow read. Several unrelated passing tests in the same window stretched to 26-45 s and finished together at 05:10:04.2.

Cause. `HangfireWorkerLease.StartSupervision` uses a `System.Threading.Timer`, and every period queued a new `OnTick` whether the last one had finished or not. Each tick takes `lock (_gate)` and opens a connection to the unreachable `127.0.0.1:1` with `Timeout=1`. On Windows a refused loopback connect takes about 2 s: 2,059 ms measured on this desktop, against under 1 ms on Linux. With the test's 50 ms period, about 20 pool threads per second parked on `_gate` behind one 1-2 s tick, which starved every other test in the process.

Classification: a product defect (overlapping timer ticks) that only appears on Windows and only bites under the 50 ms test period. In production the 15 s period bounds it to a few parked threads before the 60 s crash expiry stops the host. It is not a regression. `git log b559bfba2..64e3d78c4` touches none of `HangfireConfiguration.cs`, `HangfireDurableStorageTests.cs`, `CheckpointFileReader.cs`, `C1307ToolRemovalTests.cs` or `HostCleanupCheckpointReceiptTests.cs`. The previous run `34d41b7e` put all three tests in group 121 and already showed the stretch below the thresholds: Hangfire 8.36 s, Receipt 5.81 s.

Reproduction on the Windows desktop, with no other load, these three tests alone in one process at the cut-equivalent code (`015f20f0e`, which adds only a null probe): Read_deadline failed (5.4 s), Receipt failed (7.5 s), Hangfire passed (9.5 s). After the fix: 4/4 passed, then 5 repeats passed 15/15, all under 2 s (Read_deadline 0.32-0.91 s, Receipt 0.43-0.88 s, Hangfire 1.20-1.70 s). The new deterministic witness `A_slow_tick_does_not_overlap_the_next_tick` holds one tick in a probe and counts concurrent entries. It needs no network, so it is red on any platform before the fix.

Fix: `OnTick` is single-flight (`Interlocked.Exchange(ref _ticking, 1)`), so a period that arrives during a running tick is skipped.

## Introducing commit/card

introducing commit/card: `cbc08c44f6d43b8f0c9017878749d89cdd09e059` (CARD-1774)

`cbc08c44f` (2026-11-08, "test(card-1774): witness tick retry, own backend, and the unlock bound") added the 50 ms-period unreachable-tick test. `0774dfa3dd8f9e5392981418bed21de0e18937e9` (CARD-1774, "retry an unreachable lease tick") made an unreachable tick return and retry instead of stopping the host. Before that, the first failed tick stopped the host, so ticks never piled up. The overlapping `Timer` itself came with CARD-1768 (`51f90a0d58da803279858582c475658c77a06228`).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `0754711b-cf1f-495e-a338-e1ade5d06117` (Review CARD-1774 round 1, Succeeded). Scope and exact filter were not re-read for this note; the subject's checkpoints are the Hangfire classes in `HangfireDurableStorageTests.cs`, run on Linux.
- `52f47408-6b15-4820-9bcc-714fc14444c3` (Review CARD-1774 round 2, holder scoped to the database, Succeeded), diff-only on the same file.
- `b7ecc0ea-f11f-4ecf-ad65-4d9754d63cef` (Merge batch 130), final-tree checks only.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Unit lane, category `Unit`, path `tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs` (the starving test). The victims are `tests/Antiphon.Tests/Infrastructure/C1307ToolRemovalTests.cs` and `HostCleanupCheckpointReceiptTests.cs`.

The Hangfire class was in the selection and passed. It could not fail there: the reviews ran on Linux, where a refused connect to port 1 returns in under 1 ms, so ticks never overlap. The starvation only shows on Windows, where the connect takes about 2 s. Even there it takes a co-scheduled test that needs the pool within a few seconds, and the review selections never put the C1307 or host-cleanup receipt classes in the same process as the Hangfire class. The tests are byte-identical in the previous run, which put them in one group too and passed under its thresholds.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Infrastructure/Agents/HangfireConfiguration.cs` -> `/*/*/HangfireStorageSelectionTests/A_slow_tick_does_not_overlap_the_next_tick`

That witness is deterministic on Linux: a probe holds one tick, and the check asserts that no second tick enters. It needs no Windows connect timing. Also glob `tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs` to the same filter.

## Exemption

same cause as an already written note: none

No existing why-missed note covers timer-tick overlap starving the shared test-process pool.
