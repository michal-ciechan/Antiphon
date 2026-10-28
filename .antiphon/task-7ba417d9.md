# Final Review 7ba417d9: overlapped replacement verification (subject 9255dc5a)

Reviewed tip 8a905b30125cd2abbfffd190075832d96481e6f1 (base rc/2026-10-27 87db8c07e4264cffd4cc0e9bfbbc12cfaa44a2b4) on server2/Linux.
Verdict: one defect (D-1). The stated premise of the fix is wrong, and on Linux the change adds the worker-pool dependency the test is meant to forbid.

## Runs (all through scripts/build-slot.ps1, in a `git archive` scratch copy of the tip; the worktree was not touched)

| Run | Result |
|---|---|
| Tip build of Antiphon.Tests (Debug) | succeeded, 0 errors |
| Tip, one --no-build run of 7 classes | 61 total: 47 passed, 0 failed, 14 skipped |
| - CheckpointWindowsReadModeTests | 10/10 passed |
| - HostCleanupCheckpointReceiptTests | 8/8 passed |
| - UnsafeDeleteRecoveryTests (real Linux IO, 1175/1176/1177 recovery) | 5/5 passed |
| - HostCleanupCheckpointCustodyTests | 13/13 passed |
| - CheckpointOwnedOutputTests | 10/10 passed |
| - CheckpointStatePublicationTests | 1/1 passed |
| - CheckpointRecoveryWindowsTests | 14 NotExecuted (Windows-only; OS-branch witness still pending on Windows) |
| Fix reverted (base ReplacementPublication.cs, tip test) | 10 total, 5 failed: all five `write` rows, InvalidOperationException at ReplacementPublication.Matches:68. The test goes red. |
| Base test + base source on Linux | 6 total, 1 failed: `(write, 32)` failed the open-count assertion. The original red was **not Windows-only**: it was a test expectation that ignored the verification open. |
| Linux runtime probe (net9.0, every pool worker blocked, same fd flags as Matches) | sync `FileStream.Read`: 808 bytes in 4-5 ms. `ReadAsync().AsTask().GetAwaiter().GetResult()`: **stalled >3 s with a capped pool; 969 ms with thread injection**, even when called from a non-pool thread |
| scripts/check-evidence-diff.ps1 base..tip | commits=2 entries=0 violations=0 |

There is no plan or `### Checkpoints` manifest for this task. The brief's narrow class list was the commissioned scope, so Unit was not run as a whole.

## D-1 (finding): the verification read now depends on a worker-pool thread, without a stall deadline

- **Where:** `src/Antiphon.HostCleanup/Checkpoints/ReplacementPublication.cs:64-73`, plus the test model at `tests/Antiphon.Tests/Checkpoints/CheckpointWindowsReadModeTests.cs:85-95,116-121`.
- **Failure:** Every `ReplacementPublication.Publish` call (SafeWrite.Text/Bytes, TestRootGuard, CheckpointTempRootSweep index/state) now reads back through `ReadAsync(...).GetAwaiter().GetResult()`.
  - On Linux, .NET runs a FileStream `ReadAsync` as a pread queued to the thread pool. The probe shows it blocks until a worker is free: about 1 s per read under saturation, and indefinitely when the pool is capped.
  - The old synchronous `Read` did an inline pread and finished in 4 ms under the same saturation.
  - On Windows, completion of an overlapped ReadAsync is delivered through the pool's IO completion dispatch. This was not measured here and needs a native witness.
  - Unlike CheckpointFileReader, `Matches` has no `StallDeadline`/`WaitAsync` bound, so the wait has no limit. A stalled verification never reaches the `replacement-unverified` / 1175-1177 recovery path, so neither the temporary nor the backup is released.
- **Why:** The fix assumes "a synchronous Stream.Read on a non-overlapped handle queues behind blocked workers". That is false: a sync Read on a sync handle calls ReadFile/pread inline on the calling thread. The stall class closed by ee48cd2e4/ffe95255 was **ReadAsync** on a synchronous handle.
  - The new `ModeledWindowsStream.Read` override counts any synchronous Read, on any handle, as a worker wait. The test therefore forbids the one read form that has no pool dependency, and requires the form that does.
  - The native g62 red was only the open-count expectation (1 open where the write path makes 2). It reproduces on Linux at base.
- **Fix (Code, owner 9255dc5a):**
  1. In `Matches`, read with a synchronous `stream.Read` loop. Keep the new no-follow/CLOEXEC and OPEN_REPARSE_POINT|SEQUENTIAL_SCAN|OVERLAPPED open flags if wanted: on .NET 6+ a sync Read on an overlapped Windows handle waits on an event, with no pool. Alternatively, keep the Linux branch synchronous and bound any async form with the CheckpointFileReader deadline.
  2. Restore the test model so that only `ReadAsync` on a non-overlapped handle counts as a worker wait. Keep `opens = 2` for `write` and the 0x08200000 flag check on every open. If the overlapped flag stays, keep 0x48200000.
  3. Add a Linux witness that a saturated pool does not delay `Matches`. For example, a seam or test that fails if a `ReadAsync` is issued from Matches, or a probe-style test that blocks pool workers and uses its own process or limiter. That makes the protection go red on the real runtime instead of only on the model.

## Advisories

- **A-1 (behaviour equivalence, otherwise OK):** The comparison is unchanged: buffer of expected+1, loop to EOF or full, `read == expected.Length && SequenceEqual`. Partial reads are handled, and the EOF probe has no off-by-one. An empty expected array gives a 1-byte buffer, which matches only an empty file.
  - Exceptions caught are still IOException/UnauthorizedAccessException, so a vanished or locked file still returns false and goes to the same recovery path. UnsafeDeleteRecoveryTests passed 5/5 on real Linux IO.
  - The Linux open adds O_CLOEXEC|O_NONBLOCK; O_NONBLOCK has no effect on a regular file. The disposal order (`using` handle and stream) is unchanged.
  - `CheckpointPlatform.Other` now passes UnixFlags=0. This is unreachable in practice, because native OpenRead throws on other OSes, both before and after the change.
  - Matches never refused hard links or oversize; it only refused following links (no-follow). That is unchanged.
- **A-2 (CheckpointWrite.ReadNoFollow / MoveOverwrite, SafeWrite.cs:314-321):** The synchronous `CopyTo` on a synchronous handle is pool-independent. It is **not** a defect, and it is the safer form (see D-1). `SafeWrite.MoveOverwrite` has no callers in src/tools/tests, so it is not reachable from the tested contract.
  - The other synchronous reads in the chain are `File.ReadAllText` of /proc in CheckpointNativeIo, which is Linux-only and pool-independent.
- **A-3 (test coupling):** `opens == 2` for `write` pins the internal call count (verify, then custody digest). That is acceptable as a witness of the extra open, but it will need updating if the digest ever reuses the verification read.
- **A-4:** The 14 CheckpointRecoveryWindowsTests cases, and the Windows half of D-1, need a native Windows run. No green result here, nightly included, discharges them.

--- review evidence ---
subjectTaskId: 9255dc5a-c385-4060-b8a2-38cedeb513a4
reviewedSourceSha: 8a905b30125cd2abbfffd190075832d96481e6f1
reviewedSourceClean: false
ordinaryScopeCompleted: Interim
