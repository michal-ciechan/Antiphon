# Review fed106e2: group 104 Windows receipt read and git-custody (round 1, diff-only)

Subject: `feat/card-task-ea320b29` at `289c06287db17459207a18d58e589d4a267975d4`. Range reviewed:
`679b696bd178e3c8bb14b32fcd04a1c26b148435..289c06287db17459207a18d58e589d4a267975d4` (5 commits).
Worker report read at `/work/worktrees/task-ea320b29/.antiphon/task-ea320b29.md`. It is not on the branch.

Verdict: **STOP. Three defects.** D-1 and D-2 are in the new Windows read path. Linux cannot execute that path,
and no witness models it. D-3 is a regression in the platform-neutral retry. The git-custody half is sound.

## Defects

### D-1: every Windows read posts a completion packet for a stack OVERLAPPED to the thread-pool IOCP

- **Where:** `src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs:619` (`: base(handle, FileAccess.Read)`)
  together with `:664-669` (`EventHandle = ev`, a stack-local `NativeOverlapped`). The same code is in
  `scripts/lib/checkpoint-file-reader.ps1:340` and `:377-382`.
- **Failure:** `WindowsReadStream` derives from `FileStream`. The base constructor builds
  `AsyncWindowsFileStreamStrategy`, and `OSFileStreamStrategy`'s constructor calls
  `handle.EnsureThreadPoolBindingInitialized()`, which runs `ThreadPoolBoundHandle.BindHandle`. The handle is then
  associated with the pool's completion port. Sources: dotnet/runtime release/10.0 `OSFileStreamStrategy.cs:24` and
  `SafeFileHandle.Windows.cs:158-191`. .NET does not set `FILE_SKIP_COMPLETION_PORT_ON_SUCCESS` for files.
  - `ReadPinned` then calls `ReadFile` with an `OVERLAPPED` whose `hEvent` low-order bit is clear. Windows therefore
    queues a completion packet to that port for each read, including one that completes at once. The packet carries
    the address of the stack local.
  - The pool poller runs `IOCompletionCallbackHelper.PerformSingleIOCompletionCallback`, which calls
    `Overlapped.GetOverlappedFromNative`. That reads a `GCHandle` from the pointer-sized slots after the struct
    (`Overlapped.cs:222-226`). Here those slots are stale stack memory on the `checkpoint-file-read` thread.
  - Expected result: an access violation or invalid `GCHandle`, which crashes the test host, checkpoint tool or
    pwsh. At best it invokes an arbitrary callback. The first 32-byte owner-file read in group 104 would hit this.
- **Why:** this is new in the range. The base used `FileStream`'s own reads, which allocate the overlapped through
  `ThreadPoolBinding`.
- **Fix:** do not bind the handle. Derive from `Stream`, not `FileStream`, or never construct the async strategy.
  Alternatively set `EventHandle = ev | 1` so no completion-port packet is queued. Then add a witness that fails if
  `WindowsReadStream` constructs a `FileStream` base or omits the low bit, for example a source or reflection check,
  since Linux cannot run it.

### D-2: the cancel path frees the OVERLAPPED, buffer and event while the read can still be pending

- **Where:** `CheckpointNativeIo.cs:680-698` and `:632-636` / `:650-654`. The same code is in
  `checkpoint-file-reader.ps1:393-409` and `:352-368`.
- **Failure:** on a stall, the token is cancelled. The code calls `CancelIoEx` and then
  `WaitForSingleObject(ev, 100)`, ignores that result, and throws. The `finally` blocks then:
  - close the event,
  - unpin the rented buffer and return it to `ArrayPool.Shared`,
  - unwind the frame that holds `NativeOverlapped`.

  `CancelIoEx` only requests cancellation. A filter-held or network IRP that is slow to cancel is exactly the stalled
  read this code refuses. If it completes after 100 ms, the kernel writes `IO_STATUS_BLOCK` into reused stack, copies
  data into an array another renter may own (and that the GC may have moved), and signals a closed or recycled handle.
- **Why:** this breaks the documented invariant at `CheckpointFileReader.cs:159-161` and in
  `docs/testing-and-build.md`: "a syscall that cannot be cancelled retains its handle, buffer and sink on its owning
  thread until it returns".
- **Fix:** after `CancelIoEx`, wait for completion with `GetOverlappedResult(..., bWait: true)` or an infinite wait on
  the event before the `finally` releases anything. The caller is already released by `Run`'s 100 ms handoff, so
  boundedness holds. Alternatively allocate the OVERLAPPED and buffer natively and leak them deliberately when
  completion is not observed.

### D-3: the zero-byte retry accepts a file truncated during a stall

- **Where:** `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:123-138`; the same code is in
  `checkpoint-file-reader.ps1:120-135`.
- **Failure:** the inspected size is 32. The first read returns 0 bytes after 4 s, and the second returns 0 bytes at
  once. On the tip that is accepted as a 0-byte read, so `Digest` gives length 0 and the empty SHA.
  - At the base, that `count == 0` after the window failed `budget.Check()`. It refused with `read-stalled: 0 of 32`,
    because total 0 ≠ size 32.
  - With the new `WindowsReadStream`, a zero-byte read at offset 0 of a non-empty file can only be EOF, which means a
    truncated file. So the retry's only real-world trigger is the case it now accepts.
- **Probe:** `stalled_zero_then_prompt_zero_is_refused` was a scratch test, not committed, run on the tip build. It
  failed because `ReceiptFile.ReadBytes` did not throw. The base outcome above comes from reading the code; I did not
  build the base.
- **Pre-existing part:** a prompt zero read for a 32-byte inspected file is accepted at the base too. My probe
  `prompt_zero_is_refused` also fails at the tip. That is not a regression, but one fix closes both.
- **Fix:** on a clean EOF, require `total == metadata.Size` and that `handle.Inspect().Size` is unchanged, or refuse.
  Otherwise drop the retry.

## Checked and sound

- **Git custody:**
  - Only an `InvalidOperationException` that starts with `process custody` maps to `git-custody`.
  - A real deadline still returns `TimedOut` and stays `git-timeout` (`CheckpointProcessRunner.cs` `RunCaptured` catch).
  - `process did not start` stays `git-timeout`.
  - `CustodyRefusal(assigned, exited)` cannot hide a timeout. A child that exited before assignment keeps its result.
- **Offset handling:** the overlapped offset advances by bytes read. EOF codes 38 and 995 are handled. A fresh
  manual-reset event per read needs no reset. `GetOverlappedResult` with `bWait=false` is called only after the event
  is signalled. `CancelIoEx` targets the right handle and OVERLAPPED.
- **Late bytes still refuse:** `Check()` runs before `Progress`. `Progress` and `RearmStall` change the cancellation
  source only under `_gate`, and `Token` is read only by the I/O thread.
- **The caller stays bounded:** handoff is 100 ms and `continue` happens only when the I/O thread cleared the stall.
- **What the witnesses model:** only the `Consume`/`ReadBudget` loop, through fake `MemoryStream`s.
  `WindowsReadStream`, the overlapped event, `CancelIoEx`, IOCP binding and the Restart Manager holder are not
  modelled at all.

## Advisories (card candidates)

- **A-1:** `FormatStall` calls the holder lookup (Windows Restart Manager, Linux `/proc` scan) while holding `_gate`,
  on the caller thread inside `Run`. That native call is synchronous with no bound, so overall and caller cancellation
  are not observed during it. Resolve the holder on the I/O thread after the refusal, or bound it.
- **A-2:** `LinuxHolder` stops after the first 32 pids in `/proc`, which are low system pids, so it will almost never
  name a real holder.
- **A-3:** a child that exits before job assignment now keeps its result even if it left descendants outside the job.
  Hooks and fsmonitor are disabled, so this is unlikely for git.
- **A-4:** `_holder` is written without the lock and read under it. This is benign.
- **A-5:** the worker report lives only in the worker's worktree, `.antiphon/task-ea320b29.md`, not on the branch.
  The anchored-delete test asserts `null`, so it still fails if a live child really loses job assignment; only the
  message changed.

## Verification (Linux, server2, tip 289c06287; one build lease `72a7cb07`)

- **Build:** `tests/Antiphon.Tests` into `bin-fed106e2`: 0 errors.
- **Witness class `CheckpointReadStallWitnessTests`:** 5/5 passed at the tip.
- **Checkpoints namespace `/*/Antiphon.Tests.Checkpoints/*/*`** with `--maximum-parallel-tests 4`, about 41 min:
  802 total, 764 passed, 36 skipped, 2 failed. The two failures were my two uncommitted probes, failing as predicted.
  Excluding them: 800 total, 0 failed, matching the worker.
- **Land graph `-Check`**, using the evaluator built in that lease: `approved`, count=69, digest
  `D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE`. No `-Update`.
- **`check-evidence-diff`:** commits=5, entries=0, violations=0, exit 0.
- **Merge-tree onto origin/master and onto rc/2026-11-01** (both `8e1dea4e26c26e8b23d1cdafc2c0db83aa39a6af`): exit 0,
  tree `910761c344007ac46f9ae3b6023c57a340eca2f6`.
- **Not rerun, because of the 50-minute budget and the STOP:**
  - red at `2d22dda8c`,
  - the worker's three mutants.

  The worker's evidence for both stands but was not independently reproduced.
- **Native Windows:** no row exercises `WindowsReadStream`. D-1 and D-2 come from reading the code and the .NET 10
  sources, not from a native run.
- **Cleanup:** the probe file and all 36 `bin-fed106e2` directories were removed by literal path. The canary was
  cleaned up.
