# Review 68ebcb70 — round 2 (diff-only) of group 104 overlapped read fixes

Verdict: CLEAN. D-1, D-2 and D-3 are closed in the code at tip `709aa7765394c579fa23a0b5351105572661a2a4`.
Range reviewed: `289c06287db17459207a18d58e589d4a267975d4..709aa7765394c579fa23a0b5351105572661a2a4`. That is fix commit 00de1a588 plus report commit 709aa7765.

## Code reading (Windows path, both copies)

- **D-1 closed.** `WindowsReadStream : Stream` in `CheckpointNativeIo.cs` and in `scripts/lib/checkpoint-file-reader.ps1`. No `FileStream` base or construction appears anywhere in the Windows read path. `OpenRead` returns a raw `CreateFile` SafeFileHandle. `Inspect` uses only GetFileInformationByHandle, GetFinalPathNameByHandle and GetFileType. None of these binds the handle. `LinuxReadStream` is still a FileStream, but it is reached only on non-Windows. `Length`, `Position`, `Seek`, `SetLength` and `Write` throw. `CanSeek` is false. The reader uses none of them.
- **D-2 closed.** The buffer is pinned, and the event is created, inside `ReadPinned` / `WindowsOverlappedRead.Read`. Both are released only in that method's `finally`. Every path after `ReadFile` returns pending ends in one of two ways: (a) the event is signalled and then `GetOverlappedResult(wait: false)` runs, at which point the I/O is already complete; or (b) `CancelIoEx` is followed by `GetOverlappedResult(wait: true)`. Path (b) covers timeout, cancellation and WAIT_FAILED.
  - With bWait true, the call cannot return ERROR_IO_INCOMPLETE.
  - It also cannot be cancelled. It is not alertable, and no CancelSynchronousIo is used.
  - The wait has no bound on the dedicated `checkpoint-file-read` thread, which matches the documented rule. `ReadBudget.Run` still releases the caller after at most 100 ms. The retained SafeFileHandle, the rented array and the reader buffer all stay on the stuck thread because `using var handle` and the `finally` blocks do not run.
  - Synchronous success and ERROR_HANDLE_EOF from `ReadFile` complete without anything left pending. CreateEvent and pin failures happen before `ReadFile`.
  - `Dispose(bool)` is empty. The NativeHandle/`using var handle` owner closes the file handle exactly once. The event is closed exactly once per read.
  - The offset goes into OffsetLow/OffsetHigh on every read and advances by `read` only on success. The C# copy uses `ref _offset`. A cancelled read leaves the offset unchanged, which the witness asserts.
- **D-3 closed.** Both copies accept a clean EOF only when `total == metadata.Size` and a retained-handle re-inspect also equals that size. Otherwise they throw `read-truncated: {total} of {size} bytes; retained size {observed}`.
  - A zero-byte stall retry is followed by a prompt zero. That refuses, because count 0 with total 0 against size 32 reaches the same check.
  - A cancelled in-flight read still goes through the stall catch.
  - An empty (size 0) file is still accepted.
  - A complete read hashes the same bytes as before. The only addition is a second `Inspect` call, so the digest is unchanged.

## Runs (this host, server2 Linux, tip 709aa7765)

- Build: `dotnet build tests/Antiphon.Tests`, OutputPath=bin-rv68ebcb/, UseAppHost=false, lease 91dedf53 (held 428 s). Result: 0 errors.
- `(HostCleanupCheckpointReceiptTests*)|(CheckpointReadStallWitnessTests*)`: 17 total, 17 passed, 0 failed. Lease 67c7e72c. This run includes `windows_read_stream_is_not_a_filestream_in_either_copy` and `cancel_keeps_the_buffer_until_the_kernel_wait_returns`.
- `/*/Antiphon.Tests.Checkpoints/*/*` with `--maximum-parallel-tests 4`, lease 90f52060: **804 total, 767 passed, 1 failed, 36 skipped** in 42m31s, exit 2.
  - The one failure is `TimeoutTests.total_deadline_kills_running_and_skips_queued`. It got `["skipped","skipped"]` where `[execution-budget-exceeded, skipped]` was expected. That test uses a real 200 ms TotalTimeout, and the host load average was 21–31.
  - Rerun alone on the same build (lease 821ccd3d, load 31): 1/1 passed.
  - The diff does not touch this file, and Code's run of the same namespace was 804/0 failed. I classify it as a load-timing flake (advisory A-1), not a regression.
- Mutant (accept short EOF): the C# EOF check was replaced by a bare `break` in a scratch detached worktree. Antiphon.HostCleanup was rebuilt (lease 201455b7) and its DLL swapped into a copied test output. A UTF-16 scan confirmed the `read-truncated` literal was absent from that DLL. Running the same 2 classes gave **17 total, 6 failed**:
  - `stalled_zero_then_prompt_zero_is_refused` and `prompt_zero_read_of_a_nonempty_file_is_refused` failed with "should throw".
  - The lock-step witness failed on source text.
  - Three `HostCleanupCheckpointReceiptTests` methods failed on inspect counts.
  - The worktree was removed afterwards.
- Land graph: `audit-land-verification-graph.ps1 -Check -ToolPath` with the evaluator built through build-slot (lease bd9bc3bc). Result: `approved`, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE. `-Update` was not run, and the canary was removed.
- `check-evidence-diff.ps1 -BaseRef 289c06287 -HeadRef 709aa7765`: commits=2, entries=1, violations=0, exit 0.
- `git merge-tree --write-tree` onto origin/master and origin/rc/2026-11-01 (both 0096f7c5adcedc64fba0f2190d7c3f8bd09c0e74): exit 0 for both, tree 04c8455bd52ca0ba9ccd90b991e20632104798fd.
- All bin-rv68* output directories were deleted.

## Advisories (become cards; not blocking)

- A-1: `TimeoutTests.total_deadline_kills_running_and_skips_queued` uses a 200 ms wall-clock TotalTimeout. Under load (≈25+ load average) both rows can be skipped before CP-1 starts. Move it to a controlled clock or widen the budget per the test-performance standards.
- A-2: In both copies, the `read wait failed: win32=` text on the WAIT_FAILED branch reads the last error after CancelIoEx and GetOverlappedResult have overwritten it. This affects diagnostics only, and the problem predates this diff.
- A-3: The read-truncated text also covers growth, for example `40 of 32`, because a file that grew is refused with the same wording. Consider a `read-size-changed` wording.
- A-4: The ps1 copy has no IWindowsOverlappedOps seam. Its cancel-then-wait-then-release order is witnessed only by the source-text order check in the lock-step witness.

## What only a native Windows run can prove

- That the stack OVERLAPPED is never posted to a pool IOCP.
- That CancelIoEx plus GetOverlappedResult(wait: true) really holds the buffer until the kernel finishes.
- That Restart Manager supplies `holder=`.
- That the eleven group-104 owner-file digests (`read-stalled: 0 of 32 bytes`) now complete under qualification load.

Linux cannot execute any of these. A native Windows rerun of group 104 (Full rc run chunk antiphon/all-group-104) is still required.
