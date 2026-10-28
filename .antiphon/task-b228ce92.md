# Review b228ce92: CARD-1601 receipt reader off the thread pool (Final review, Interim scope)

Subject: task e9436c0d, branch feat/card-task-e9436c0d, tip 9563933408345a2b01d8b27ddf554b6b15b7f2bc
(tested implementation 25c715b051b046a2863caf74d67f2e2484ebe281 plus a report-only commit). Base is rc/2026-10-27
aa6dfd24a51db7c02e8722a05a21817bf4df407a, which is still the rc tip. Host: server2 Linux, 24 CPUs, .NET 9.

**Verdict:** no regression, and nothing fails open on Linux. Recommendation: land on rc. One stale doc line remains
(D-1, advisory). Windows overlapped completions are still dispatched through the pool (A-1, advisory, could not be
executed here).

## Runs

All runs went through scripts/build-slot.ps1. There was one slot build of tests/Antiphon.Tests with UseAppHost=false
into bin-rv1601/ (0 errors, 152 s held). Tests then ran with `dotnet exec --no-build` against the built DLLs.

| Run | Selection | Result |
|---|---|---|
| contract (normal) | 12 classes (the author's 91-test selection) | 91/91 passed, 0 skipped, 83 s |
| loaded run 1 (4 owned node CPU burners, joined) | same | 91/91 passed, 108 s |
| loaded run 2 (4 burners, joined) | same | 91/91 passed, 143 s |

Every run produced a fresh TRX with nonzero per-class counts:

| Class | Tests |
|---|---|
| CheckpointReceiptStallBudgetTests | 6 |
| CheckpointWindowsReadModeTests | 10 |
| CheckpointEofStallTests | 10 |
| CheckpointLargeStampTests | 6 |
| CheckpointLargeStampScriptTests | 3 |
| HostCleanupCheckpointReadDeadlineTests | 2 |
| HostCleanupCheckpointReceiptTests | 8 |
| HostCleanupCheckpointCustodyTests | 13 |
| C1307CancellationTests | 5 |
| C1307ToolRemovalTests | 9 |
| CheckpointSlotExecutorTests | 13 |
| CheckpointScriptReaderDeadlineTests | 6 |

CheckpointSlotExecutorTests took 5.5 s normally and 8.6 s and 8.5 s under load. Review 445477ba measured 7.3 s at the
6 s stopgap, so the dedicated thread adds no measurable slowdown.

Other checks:
- **Evidence diff** (`scripts/check-evidence-diff.ps1 -BaseRef aa6dfd24a -HeadRef 956393340`): commits=7, entries=1
  (the author's Markdown report), violations=0.
- **Landing:** an old-style merge-tree (the brief forbids `--write-tree`) is clean against rc. Against master
  fe73cfd2e it conflicts in 5 files, matching the author's list: .antiphon/task-af2b78c8.md,
  docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, docs/testing-and-build.md (2 hunks),
  scripts/lib/checkpoint-file-reader.ps1, and src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs.

Probes ran as pwsh with Add-Type in the scratchpad, against the built Antiphon.HostCleanup.dll and the embedded reader.
The ReadBudget class is byte-identical between the C# and PowerShell readers.

## (1) Pool-saturation probe (445477ba method: N pool tasks each read a healthy 85-byte file once)

Each cell gives the number refused and the slowest read.

| Reader | N=64 | N=128 | N=200 | N=400 | cap 4, 4 callers | cap 4, 8 callers | cap 4, 64 callers |
|---|---|---|---|---|---|---|---|
| tip compiled | 0, 22 ms | 0, 18 ms | 0, 16 ms | 0, 20 ms | 0, completes | 0, completes | 0, 4 ms |
| tip embedded | 0, 12 ms | 0, 25 ms | 0, 16 ms | 0, 29 ms | 0, completes | 0, completes | 0, 2 ms |
| base embedded (control) | n/a | 59/128 | 137/200 | n/a | hang: 60 s watchdog, 0 completed | n/a | n/a |

The control's refusals all read `read-stalled: 0 of 85 bytes`. In the base control's cap-4 case, the 4 reads were
refused only after the pool was restored. The control proves the probe can go red. On the tip, nothing was refused and
nothing hung at any level.

## (2) Fail-closed

These ran in the embedded reader on the real clock through its openStream seam, with 100 concurrent callers on
dedicated threads.

- **Never-progressing cancellable read:** all 100 were refused with `read-stalled: 0 of 85 bytes; no progress within
  3s` at 3.000-3.028 s. Threads went 33 to 33 after refusal, with 0 `checkpoint-file-read` threads left. Open handles
  were 0/100 after 500 ms, and there were 0 unobserved task exceptions. There is no leak.
- **Uncancellable inline-blocked syscall (the stated trade-off):** all 100 were refused at 3.1 s, which is the 100 ms
  handoff. While they stayed blocked, 100 reader threads and 100 handles were retained, and threads rose from 33 to
  133. Once the syscall returned, threads went back to 33 and handles to 0. This is one non-pool background thread per
  truly hung syscall. Previously a pool worker was held the same way and the caller hung, so this is an improvement,
  not a new risk.
- **Trickle of 1 byte per 2.9 s (real time):** refused at 31.0 s with `read-deadline-exceeded: 10 of 85 bytes; overall
  limit 31s`. The late syscall then released its thread and handle.
- **Caller cancellation at 1 s:** surfaced as a TaskCanceledException (an OperationCanceledException) at 1.0 s, never as
  an IOException.
- **Stream exception:** InvalidOperationException("boom-from-stream") reaches the caller unchanged through
  ExceptionDispatchInfo. It is caught on the worker, so nothing is unobserved.
- **1 GiB (the inclusive content limit, sparse file, so this measures syscall and SHA-256 throughput):** compiled Digest
  took 11.5 s and embedded took 4.6 s, against a 1054 s cap. The 1 MiB/s floor is 10-100x below a contended HDD or SSD,
  so it is not too tight. It is loose only for a trickling source, which the 3 s silence window still catches when the
  source goes quiet, and the worst case is now bounded at about 17.5 min instead of unbounded.
- **No-follow and custody:** the native open, flags, custody and growth code are unchanged.
- **Windows:** the open, flags and `NativeHandle.OpenStream` are unchanged; only Linux gets the new `LinuxReadStream`.
  What changed for Windows is supervision: the read now runs on the dedicated thread, and refusal comes from the
  caller's monitor polling instead of a pool timer, so a Windows refusal no longer needs a worker.
  CheckpointWindowsReadModeTests passed 10/10 in all three runs.

## (3) Dedicated-thread design

- **Thread cost:** for 3000 sequential 85-byte reads, p50 was 465 µs on the tip embedded reader against 410 µs on the
  base embedded reader (about +55 µs). The mean was 511 µs against 805 µs. The compiled reader's p50 was 554 µs.
  This is negligible.
- **Shared state:** `result` and `error` are written before `_completed` is set under `_gate`, and they are read only
  after that is observed under the same lock. `_stop` is cancelled and disposed only under `_gate`, and only while it
  is not completed, so it is never cancelled after disposal. The buffers, hash and MemoryStream are owned by the worker
  closure, so a late worker never touches a caller-disposed object.
- **Shutdown:** the thread has `IsBackground = true`, so a blocked read never holds the process open.

## (4) Leftover window text

- The C# and PowerShell constants are both 3 s, and no 6 s constant is left. The tests use `StallDeadline`, or the
  pinned `within 3s` text that the brief required.
- docs/test-performance-standards.md has no reference to the window.
- CheckpointConfinementLedgerTests.cs:603 still has a cosmetic `within 3s` stub string. It is consistent with the
  window.

**D-1 (advisory, docs only):**
- **Where:** docs/testing-and-build.md:1591.
- **Failure:** the Windows read-mode paragraph still says worker availability can "consume the six-second window". That
  is CARD-1599 text this branch did not revert, and it contradicts :1584, which now says three seconds.
- **Why:** the brief asks for no contradictory text.
- **Fix:** change it back to "three-second" (the base wording). Fold this into the master port, which has to resolve a
  conflict in this same file anyway. Under D-2 it does not need a Review rerun.

**A-1 (advisory, not executable here):**
- **Where:** the Windows overlapped `FileStream.ReadAsync` path.
- **Why:** this is my reading of .NET 7+, not something I ran. Windows I/O completion pollers normally queue
  completions to worker threads. A healthy Windows read whose completion is starved of workers can therefore still be
  refused at 3 s, now without the 6 s margin.
- **Failure:** this only refuses (fail-closed). It is no worse than rc base, which was also 3 s with a pool timer that
  could hang outright.
- **Fix:** native Windows qualification under load, or a follow-up that reads synchronously on the dedicated thread on
  Windows as well.

**A-2 (advisory, diagnostics only):**
- **Where:** `ReadBudget.Run` and `Check`.
- **Failure:** the overall and caller-cancel refusal objects stored in `_refusal` can be thrown on both the caller and
  the worker. The stack trace on the caller's exception can then be overwritten.
- **Fix:** throw a fresh exception per thread.

## Not run and disclosures

- **Not run:** the whole Unit lane (the brief excludes it, so ordinaryScopeCompleted is Interim) and native Windows.
- **No checkpoint-tool receipts:** these were direct gated runs, so reviewedSourceClean is false.
- **No plan:** there is no CARD-1601 plan or `### Checkpoints` table, so no CP-n rows could be matched.
- **PCs:** PCs remain pending.
- **Leftover outputs:** under the delete-nothing rule, 32 gitignored `bin-rv1601/` directories are left in this mirror.
- **Evidence location:** logs, TRX and probe output are in /work/worktrees/task-b228ce92/.antiphon/rv1601/. The probe
  scripts are in the session scratchpad.
