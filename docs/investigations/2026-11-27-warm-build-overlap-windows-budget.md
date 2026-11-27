# Windows warm-build overlap watchdog

The group 112 test-only change is on hold for product review. The failing
source contains the exclusive-create repair and still reopens its own
exclusive gate and append handles. CARD-2085 has a candidate for those
reopens. The overlap fixture also deliberately contends for the cache lock,
which is a separate path with a bounded Windows sleep.

## Source and observed timings

| Windows qualification run | Pin | Overlap fixture | Ordinary warm replay |
| --- | --- | --- | --- |
| `1fe763a9` | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | 13.87 s, exit 130 | 27.1 s, passed |
| `419ed669`, group 112 | `5c0883bf203d122fdc7d4556e79087af2a683669` | 5.98 s, exit 130 | 9.3 s, passed |

The supplied durations cover whole tests, including setup and teardown.
The overlap scheduler's exit 130 comes from its five-second caller token.
Both overlap observations are censored and completion-time p95 on Windows
is unknown. The phase where the new run exhausted the watchdog is also
unknown. The Windows host was reserved by the qualification run.

Both the exact rc pin and fetched master `390da67d4a2db173adb8c3f386ac89dd42d4b974`
contain `b01ab6294dcd627534d1f48c1481e6ce60aaf1c3`, the native
`OpenCreate` held-handle fix. Both contain exactly the same `SafeWrite.cs`
blob, `3d252625546b30e4530764ffd1a2a518910b5dee`. The copy at the CARD-2085
product repair `e202ea50e2d69a75ca2bca331a04eaa0a7f1e74c` differs and that
commit is absent from both histories. The rc pin also lacks the candidate's
round-two watchdog restore `ede7911468d6c1869c21e8200ce233357b4755d8` and
already had a literal five-second watchdog. The test failure cannot
qualify the CARD-2085 candidate.

## Work inside the overlap watchdog

1. The watchdog starts just before the fixture reacquires the cache gate.
   During the first warm build, the scheduler verifies source, prepares and
   validates the partition and seed seal, invalidates the seal before the
   fake build writes, then records output custody and reseals the partition.
   The two in-window seal writes use staging files, durable flush and
   same-volume replacement. The gate and seed data were originally created
   before the watchdog started.
2. The second build's partition key matches the first row's key. The scheduler
   includes that key in `activePartitions`, so `Prepare` selects cold before
   touching the borrowed partition or seal. The fake build slot waits for the
   first row to enter its fake driver. The cold build still does source
   observations, output preflight, fresh output creation, writes a fake DLL
   and records its build binding. [Scheduler](../../tools/Antiphon.Checkpoints/Execution/RunScheduler.cs)
3. The second fake build deliberately calls `TryOpen` on the cache while the
   outer fixture's `FileShare.None` gate is still live and the first row is
   waiting. `TrustedPath(run.lock)` calls `OutputCustody.Stat` before trying
   to acquire a new gate. On Windows, its metadata `CreateFile` requests
   `GENERIC_READ` and hits sharing violation 32. The custody loop makes
   21 attempts with 20 sleeps of 50 ms, for one nominal second of sleep plus
   syscall and scheduling overhead. The retry has no cancellation token.
   `TryOpen` declines the contended cache and only then does the fixture
   release the first row. Linux uses `statx` for the metadata probe and
   reaches the separate gate acquisition without that sleep.
   [Overlap fixture](../../tests/Antiphon.Tests/Checkpoints/CheckpointWarmBuildTests.cs) ·
   [Cache admission](../../tools/Antiphon.Checkpoints/Execution/CheckpointIntermediateCache.cs) ·
   [Native custody probe](../../src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs)

The fake driver and fake slots run in-process, so this interval includes no
child startup or broker wait. Successful cold admission creates no managed
seal for the second build. The source identifies the fixed contention delay
and the filesystem work around it. The Windows records do not allocate the
rest of the watchdog to metadata, durable flush, Defender, NTFS or scheduling.
The ordinary replay case also exercises the held cache gate and has no
equivalent five-second caller watchdog.

## Product findings on the exact pin

| Writer | Interleaving and consequence |
| --- | --- |
| `CheckpointWrite.OpenGate` | A terminal publisher, tool-copy cleanup or test-root registration acquires an exclusive stream. Before it returns that stream, `Remember(full)` asks `FilesystemIdentityReader` to open the same path with `GENERIC_READ`. Its own handle keeps the path unshareable for every retry. Windows spends one nominal second sleeping and returns an unbound custody identity before publication or cleanup can proceed. |
| `CheckpointWrite.OpenAppend` with `FileShare.None` | The optional `CheckpointUsageEvent.Write` journal opens its append stream exclusively. `Remember(full)` attempts the same path probe before the append reaches the caller, incurring the same retry and unbound identity. If `C804_ROOT_EVENTS` was configured, this fixture can call the journal during root setup and teardown, outside its watchdog. The supplied run data does not establish that setting. |
| `CheckpointWrite.OpenAppend` with default sharing | Executor and process logs reopen the path for metadata too. Their read/write/delete sharing is compatible with the custody probe, so their own append handle does not cause the fixed retry. The overlap uses neither real process logging nor terminal publication. |
| `CheckpointWrite.OpenCreate` and the cache seal/props writers | The production native I/O supplies a `FileStream`, whose held handle provides identity. The pin's fallback path probe is restricted to a custom I/O implementation returning a different stream type. Cache staging files close before pathname rechecks and replacement, and seal validation closes before invalidation. |

The first two rows are product self-reopens. The separately requested
cache acquisition can still spend a second against a bona fide live owner
on the CARD-2085 candidate. Review the already pushed held-stream implementation
and its existing gate/append red witness `f9096cd25fdcff6946769186061411f9cb875912`.
Assess the cache contender with its own phase timing before attributing the
remaining Windows wall time. [Shared writer](../../src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs) ·
[Usage journal](../../tools/Antiphon.Checkpoints/Execution/CheckpointUsageEvent.cs)

## Deferred test budget

Propose 15 seconds on Windows and 5 seconds on Linux after product review.
Fifteen seconds rounds up from twice the 5.98 s post-repair whole-test
observation with room for loaded host variation. This is a provisional
budget informed by a censored sample, with the 13.87 s older run as
historical context. Review `cd5da02e` recorded a provisional 1,436 ms
nearest-rank Linux p95 over 11 samples, including six at server2 load
31–48. Those observations retain the existing Linux limit for this change.

- **CARD-2088 R5 follow-up** — After the current desktop run releases its
  slot and an eligible candidate is pinned, collect 20 native Windows
  watchdog-window samples from the exact overlap method under representative
  qualification load. Record candidate SHA, host/load, slot/build time,
  setup and seed, gate open, first build and row entry, second slot wait,
  intentional sharing retry, scheduler completion, verification and teardown.
  Keep canceled observations with their last phase. Reassess the watchdog
  at approximately twice the measured p95 of comparable completed windows.
- For the deferred change, have the Linux witness exercise both budget
  choices and hold the second fake build until caller cancellation. Require
  exit 130 with `caller-requested`. Include the last reached phase and elapsed
  times in the normal overlap's failure message. Validate the affected
  methods and the six-class guard union plus
  `PlanCheckpointClassSelectionGuardTests` on the committed candidate.

## Why missed

The prior [Windows sharing-delay investigation](2026-11-26-why-missed-group-113-warm-build-overlap.md)
records the introducing commits, approving review selections and the native
coverage gap for the same sharing mechanism. Its existing CARD-1782
cache and writer path-to-`CheckpointWarmBuildTests` mapping proposal
continues to cover the smallest overlap replay. CARD-2085 adds the direct
native gate/append witness. The timing debt and product review are open.
