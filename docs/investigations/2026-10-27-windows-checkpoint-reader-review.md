# Final Review: Windows checkpoint overlapped reads

Subject task `a043b0a4-bce3-4f2b-b860-a38b205c6ec3` (Debug), tip `ffe9525531e2c6087a5395b43527c01bf460a8b5` on `feat/card-task-a043b0a4`. Base `origin/rc/2026-10-26` `bedea1c03cd788d83b2ff54c40bfb603927bd69c`. Review branch `feat/card-task-1f8f94c3`. No product file was edited.

## Verdict

The root cause and the fix are right. Both Windows verified readers now pass `FILE_FLAG_OVERLAPPED` (`0x40000000`) with the existing `FILE_FLAG_OPEN_REPARSE_POINT` and `FILE_FLAG_SEQUENTIAL_SCAN` bits. Share mode stays `7`. The Linux open is unchanged. Deadline, chunk re-arm, size caps, retained-handle EOF recheck, and refusal text are unchanged.

.NET 9 `FileStream(SafeFileHandle, FileAccess)` selects its strategy from `SafeFileHandle.IsAsync`. On Windows that property is not a P/Invoke default: when the handle's cached options are unset, `GetFileOptions` calls `NtQueryInformationFile` and sets `FileOptions.Asynchronous` when the file was not opened with `FILE_SYNCHRONOUS_IO_ALERT` or `FILE_SYNCHRONOUS_IO_NONALERT`. `FILE_FLAG_OVERLAPPED` is that case. `ReadAsync` then uses completion-port I/O. Without the flag, `ReadAsync` queues a blocking read on a worker, and the existing three-second stall fires at zero bytes when no worker runs it. That is the signature in run `8f055f87` (`read-stalled: 0 of N bytes; no progress within 3s` for 32, 467, and 808 byte files, and the same shape on a large staged DLL).

The fix does not need a separate `IsAsync` assignment. The public property is get-only; the kernel query supplies it. A synchronous `ReadFile` is not used on the overlapped handle, so this does not turn reads into `ERROR_IO_PENDING` failures. No-follow, sequential scan, pipe refusal (`GetFileType` must be a disk file), share mode, and handle disposal order are unchanged. The custody `CreateFile` in `CheckpointNativeIo` is a metadata open and correctly stays without overlapped I/O.

## Witnesses

Portable witnesses fail on the pre-fix commit and pass on the tip. They are not weakened. They do not execute native `CreateFile`, and they do not discharge the desktop qualification.

Pre-fix `d834e22271a1f6a445da20cf3edf9cfb25bd0d88`, run `20261027-054053-f79e`, `buildSource=verified`, slot granted, waited 0s: executed 11, passed 4, failed 7, skipped 0. All six `CheckpointWindowsReadModeTests.Windows_reads_do_not_queue_synchronous_io_behind_blocked_workers` cases failed with `read-stalled: 0 of N bytes` for 32, 467, 808, and 524305 bytes. `CheckpointScriptReaderDeadlineTests` `windows-open` failed with `FileStream.IsAsync=false`. The four older script modes passed.

Tip run `20261027-053507-a3c7` at `ffe9525531e2c6087a5395b43527c01bf460a8b5`, clean tree, `buildSource=verified`, one build, slot granted, waited 0s, unlisted commands none. `validate` reported `CHECKPOINT SOURCE VALID source=ffe9525531e2c6087a5395b43527c01bf460a8b5 rows=4`.

| CP | Result |
|---|---|
| CP-1 reader witnesses and stall guards | executed 23, passed 23, failed 0, skipped 0 |
| CP-2 affected consumers except EvidenceFolder | executed 23, passed 23, failed 0, skipped 0 |
| CP-3 `recovery_fixture_records_tool_custody_before_cleanup` | executed 1, passed 1, failed 0, skipped 0 |
| CP-4 EvidenceFolder methods other than the Windows share retry | executed 5, passed 5, failed 0, skipped 0 |

An earlier class-wide consumer row on the same tip (run `20261027-052726-23cd`) passed 28 and skipped 1: `EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open` skips off Windows. That row is not in the validated receipt. The method stays a pending native check.

Build for the validated run was 235s. Witness tests took 15.7s, consumers 4.0s, the recovery method 0.24s, and the evidence methods 0.57s. No timeout or assertion was widened.

## Still pending

Native Windows qualification of the real `CreateFile` path, the Windows share-retry method, the other `CheckpointRecoveryWindowsTests` failures, and `CheckpointRepeatBuildIsolationTests` stays with desktop tasks `32365cdf`, `fb80be16`, and `595bc226`. This review did not run those and did not touch the desktop. No positive-control row is defined here; none is discharged.

`check-evidence-diff.ps1` over `bedea1c03cd788d83b2ff54c40bfb603927bd69c..ffe9525531e2c6087a5395b43527c01bf460a8b5`: commits 6, entries 1, violations 0.

`git merge-tree --write-tree origin/rc/2026-10-27 ffe9525531e2c6087a5395b43527c01bf460a8b5` exited 0, tree `74b63cba0f514221e6c8e8d52a493fc116701d86`. The same command against `origin/master` conflicts in `.antiphon/task-af2b78c8.md`, `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, `scripts/c590-remote.sh`, and `tests/Antiphon.Tests/TestHelpers/ClaudeDialogFixture.cs`. Those four also conflict for the rc base `bedea1c03` against master. This delta does not touch them.
