# CARD-0607 — `Unreadable_bound_file_is_unknown` file-lock flake

**Date:** 2026-09-22
**Stage:** Investigate
**Verdict:** Root cause **confirmed** and **reproduced**. Test-only race; the production code path
under test is correct and carries no equivalent defect.

## Summary

`TranscriptTailerObservationTests.Unreadable_bound_file_is_unknown`
(`tests/Antiphon.SessionRunner.Tests/TranscriptTailerObservationTests.cs:25`) creates its own
transcript, starts a real `TranscriptTailer` against it, and then tries to make the file unreadable
by taking an **exclusive** handle:

```csharp
await using var locked = new FileStream(world.Path, FileMode.Open, FileAccess.Read, FileShare.None);
```

The tailer the test just started is concurrently performing its **first tail-loop read** of that same
file with `FileShare.ReadWrite | FileShare.Delete`
(`src/Antiphon.SessionRunner/TranscriptTailer.cs:393-394`). `FileShare.None` is incompatible with any
already-open handle, so when the test's open lands inside the tailer's read window Windows refuses it
with

```
IOException: The process cannot access the file '...\antiphon-c79-obs-<guid>\projects\cwd\<guid>.jsonl'
because it is being used by another process.
```

and the test fails at its own line 25 — not inside the tailer.

## Evidence

### 1. The throwing open is the test's, not production's

Stack trace from the CARD-0079 CP-9 run (delegate `ce048398`,
`.antiphon/CP-9-649b843d9f104b03a13bbba24fda8127/run.trx`), and identical in my own reproduction:

```
IOException: The process cannot access the file
  'C:\Users\lndco\AppData\Local\Temp\antiphon-c79-obs-cf2f0db6608542eda22ab8b7bc925251\projects\cwd\31ffa791-...jsonl'
  because it is being used by another process.
  at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(...)
  at System.IO.FileStream..ctor(String path, FileMode mode, FileAccess access, FileShare share)
  at Antiphon.SessionRunner.Tests.TranscriptTailerObservationTests.Unreadable_bound_file_is_unknown()
     in ...\TranscriptTailerObservationTests.cs:25
```

This is confirmed by elimination too: every production open of this path is inside an `IOException`
handler and can never surface as a test failure —
`TranscriptTailer.cs:265-270` (guarded by `catch (Exception ex) when (ex is IOException or
UnauthorizedAccessException) → CompactionTailObservation.Unavailable()` at `TranscriptTailer.cs:240-243`)
and `TranscriptTailer.cs:393-394` (guarded by `catch (IOException)` at `TranscriptTailer.cs:410-413`).

### 2. Reproduced

Built to `bin-c607/`, ran `Antiphon.SessionRunner.Tests.exe --treenode-filter
"/*/*/TranscriptTailerObservationTests/*"` in a loop:

| Condition | Result |
|---|---|
| Idle machine, 40 iterations | **0 failures** |
| 12 CPU spinners on 8 cores, 30 iterations | **5 failures** (iterations 4, 6, 7, 9, 16) |

Every failure was `Unreadable_bound_file_is_unknown` with the stack above; the other four tests in the
class passed in every iteration. This matches the reported behaviour exactly: it fails only inside a
big loaded run, and passes in isolation and on rerun.

### 3. The holder is the tail loop's first read — measured, not inferred

I temporarily replaced line 25 with a retry loop that records `Tailer.Snapshot().Entries.Count` before
the first open attempt (the snapshot is populated by `ProcessPending`, which runs only *after* the
tail loop's read handle is closed — `TranscriptTailer.cs:401-407`). 25 iterations under the same load:

```
attempts=2 waitMs=15 entriesAtFirstTry=0 entriesNow=3
attempts=2 waitMs=12 entriesAtFirstTry=0 entriesNow=3
attempts=2 waitMs=11 entriesAtFirstTry=0 entriesNow=3
attempts=2 waitMs=24 entriesAtFirstTry=0 entriesNow=3
attempts=2 waitMs=36 entriesAtFirstTry=0 entriesNow=3
attempts=3 waitMs=37 entriesAtFirstTry=0 entriesNow=3
                                (6 / 25 iterations)
```

`entriesAtFirstTry=0` in **every** collision, `entriesNow=3` once the lock cleared: at the moment of
refusal the tailer had opened the file and had not yet finished the read. Nothing else in the process
holds that path. The probe was reverted; the working tree is clean.

### 4. Why the window is wide enough to hit

```
TryBind sets BoundTranscriptPath            TranscriptTailer.cs:657
  → LocateAsync returns                     TranscriptTailer.cs:554
  → tail loop: new FileStream(...)           TranscriptTailer.cs:393   (µs later, same thread)
  → await fs.ReadAsync(...)                  TranscriptTailer.cs:398   ← handle held across an await
  → handle closed, then ProcessPending        TranscriptTailer.cs:400-407
```

Meanwhile the test's `BoundWorld.CreateAsync` polls `BoundTranscriptPath` every 50 ms
(`TranscriptTailerObservationTests.cs:96-97`), so it normally observes the bind ~45–50 ms *after*
`TryBind` — by which time an unloaded read (sub-millisecond) is long closed. The handle's lifetime is
bounded by **thread-pool scheduling latency of the `ReadAsync` continuation**, not by disk I/O. Under
saturation that continuation is delayed tens of milliseconds, the handle stays open past the test's
next 50 ms poll, and the exclusive open is refused. Measured clear-times of 11–37 ms *after* the
test's first attempt put total handle lifetime at roughly 55–85 ms in the failing cases.

This is why the flake is load-dependent, appears only in large runs, and vanishes on rerun.

## Is this exposing a real production race? No.

* Both transcript readers in `TranscriptTailer` request `FileShare.ReadWrite | FileShare.Delete`
  (`TranscriptTailer.cs:266`, `TranscriptTailer.cs:394`), as do `CodexTranscriptTailer.cs:246` and
  `GrokTranscriptTailer.cs:204`. Those modes are mutually compatible, so the tail loop and
  `ObserveCompactionSilenceAsync` can never lock each other out, and the tailer never blocks Claude's
  writer or a delete/rename of the transcript.
* `grep -rn "FileShare.None" src/ server/ client/` finds no reader of a Claude `.jsonl` at all — the
  hits are lock files, custody ledgers and atomic temp-file writes. Nothing in production ever takes
  an exclusive handle on a transcript, so the collision the test manufactures cannot occur in
  production.
* The production behaviour the test asserts is already correct in both orders: whether the tailer read
  first or the test locked first, `ReadBoundFileAsync` on a locked file yields
  `CompactionObservationStatuses.Unavailable`. Only the *acquisition of the lock* is racy, never the
  assertion.

`TranscriptTailer.cs:393-394` holding a read handle across an `await` is therefore a benign
production detail; it is only the test's `FileShare.None` that turns it into a failure.

## Remaining uncertainties

* I reproduced under synthetic CPU saturation (12 spinners / 8 cores). I did not re-run the original
  full CP-9 filter to reproduce in situ; the stack, the file-name shape (`antiphon-c79-obs-*`) and the
  `entriesAtFirstTry=0` measurement make the mechanism the same one, but the *frequency* under a real
  suite run is unmeasured.
* `HerdrPaneDisposalGuardedLiveTests` also mutates the process-wide `CLAUDE_CONFIG_DIR` but is **not**
  in the `[NotInParallel("ClaudeConfigDirEnv")]` group that every other `CLAUDE_CONFIG_DIR` test joins.
  That is a separate latent hazard, unrelated to this IOException (it cannot hold a handle on this
  test's temp file), and I did not investigate it.
* Whether any other test in the repo has the same shape — an exclusive lock taken on a file a live
  in-process poller reads — was not exhaustively audited. `RunnerRestartPreflightSafetyTests.cs:223`,
  `InterimVerificationReadinessTests.cs:102`, `OutputDistillationDeliveryTests.cs:178` and
  `RunnerProcessProbeTests.cs:828` use the same `FileShare.None` idiom and are worth a glance during
  the fix.

## Not done, noted

* **Fix idea (test-only, no production change):** in `Unreadable_bound_file_is_unknown`, make the
  exclusive open deterministic rather than racing the live tailer — either wait for the tailer's first
  read to complete before locking (`Tailer.Snapshot().Entries.Count` reaching the written line count is
  the existing, already-measured signal), or retry the `FileShare.None` open with a short bounded
  deadline. Do not widen `TranscriptTailer`'s `FileShare` flags or add a production retry: the
  production path has no exclusive reader and needs neither.
