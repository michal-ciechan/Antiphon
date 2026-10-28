# Review c28cf67e: CARD-1596 anchored temp-root sweep, round 3 (Final round, Interim scope)

Subject: task 6f1d1132-492c-47ce-ab8b-f0b0f6b70f26, which continues 53385321-2910-4a52-b010-3cd4bdb6bd1f.
Original Code landing owner: fd72b5eb-bdad-4e98-a6e1-df6d1a54979e.
Branch: feat/card-task-6f1d1132 at tip 3a0d8c9b006aae6fb7bba1cbb83712428b6b072a.
Base: rc/2026-10-27 4563e52529b23052856cb0f1e0d01b383cf7590f.
Reviewed on server2 (Linux 4.15, /tmp on ext4) with a detached checkout of my own worktree. Nothing was pushed to the subject branch.

**Verdict: clean.** I found no regression or fail-open introduced in a693904c..3a0d8c9b. Round 2's D-1 is closed on the real code path, and no defect from round 1 or round 2 is still open. The items below are advisories only.

**Scope: Interim.** I ran only the eight named classes. The whole Unit lane did not run, there was no Windows execution, and all PCs are still pending.

## Runs (all through scripts/build-slot.ps1, UseAppHost=false, then dotnet exec)

| Run | Commit | Result |
|---|---|---|
| Build tests/Antiphon.Tests, OutputPath=bin-rc28t/ | 3a0d8c9b | 0 errors, 137 s |
| The eight classes, one treenode filter | 3a0d8c9b | 115 total, 115 passed, 0 failed, 0 skipped (20.6 s) |
| Build at a693904c, OutputPath=bin-rc28r/ | a693904c | 0 errors |
| `TempRootSweepAnchoredDeleteTests/(directory_symlink_*)` | a693904c | 2 total, 2 failed (red confirmed; see below) |
| Build of the tip plus an uncommitted probe, OutputPath=bin-rc28p/ | 3a0d8c9b + probe | 0 errors |
| ReviewProbeC28Tests (6 probes, each on a fresh temp fixture) | 3a0d8c9b + probe | 6 passed (see the probe table) |
| check-evidence-diff.ps1 -BaseRef 4563e525 -HeadRef 3a0d8c9b | | commits=11, violations=0 |
| Non-ASCII scan of 1fee5b56..3a0d8c9b | | none |

The red run at a693904c failed for the reasons the witnesses are meant to catch:

- `directory_symlink_to_an_unreadable_child_does_not_abort_other_roots` failed with UnauthorizedAccessException on `<root>/out/locked`. That is the round 2 D-1 abort.
- `directory_symlink_does_not_read_or_modify_the_outside_tree` failed on `io.OutsideReads should be 0`.

Per-class counts from the TRX at the tip:

| Class | Tests |
|---|---|
| TempRootSweepAnchoredDeleteTests | 9 (7 + 2 new) |
| CheckpointTempRootSweepTests | 28 |
| UnsafeDeleteRecoveryTests | 5 |
| AnchoredPathDeleteWitnessTests | 12 |
| HostCleanupCheckpointSweepRaceTests | 4 |
| HostCleanupCheckpointCompatibilityTests | 54 |
| CheckpointStatePublicationTests | 1 |
| ReapedProcStatIdentityTests | 2 |

These counts match the author's 115/0.

## Probe table

The probe file is `<scratchpad>/ReviewProbeC28Tests.cs`. It was never committed and has been moved out of the worktree. Each probe uses one dead-owner root with no grace period.

| Probe | Result at tip 3a0d8c9b |
|---|---|
| PA: `run1/ownership.json` is a file symlink to an outside JSON file, next to a real `request.json` | Skipped as `nested-custody-invalid`. ReceiptFile reads with nofollow, nonblocking, regular-only. Marker kept, outside file intact. |
| PB: FIFOs named `ownership.json` and `manifest.resolved.yaml` | No block (10 ms). Skipped as `nested-custody-invalid`, marker kept. |
| PC: a dangling link, a self-loop link, a file link to an outside 4 KiB file, and a link to an outside FIFO | completed=1 and bytes=0, so no link target was counted. The outside file is still 4096 bytes and the FIFO is intact. |
| PD: a dir-symlink root, then two good roots; three sweeps with MaxRoots=1 | Sweep 1: examined=2, the bad root skipped once, good1 removed. Sweep 2: examined=1, good2 removed. Sweep 3: examined=1, the bad root skipped once. The cursor advanced every sweep. The bad root's marker and index entry survived all three sweeps. |
| PE: the root path itself is a symlink to a marked directory outside | Skipped as `linked-root-or-ancestor`. Outside payload and marker intact. |
| PF: DirectoryNotFoundException thrown from the nested-custody `DirectoryExists(run1/tool)` | Skipped as `inventory-incomplete-or-linked`. The other root completed. The marker of the throwing root was kept. |

## Brief questions

**Q1: can the sweep reach outside the root, and can an exception escape?**

- A directory symlink is classified with one `File.GetAttributes` call (lstat, then stat of the target) and is then refused with `inventory-incomplete-or-linked`. Its target is never listed or opened.
- The nested-custody loop walks only the real directories that `AnchoredInventory` listed. Its probes are `File.Exists` and `Directory.Exists`, which are stats. The reads go through ReceiptFile, which is nofollow, O_NONBLOCK and regular-only.
- A dangling link or a link loop has no directory bit, so it is a leaf that is unlinked as itself.
- On Windows, `ListAnchoredWindows` reports any reparse point as kind `symlink`. A junction, a mount point or a directory symlink therefore carries ReparsePoint|Directory and is refused.
- A same-device bind mount is still crossed, unchanged from base (round 2 advisory).
- Eligible's try/catch(IOException, UnauthorizedAccessException) covers the whole body. That includes DirectoryNotFoundException, PathTooLongException and FileNotFoundException from a vanished link.
- ArgumentException and NotSupportedException are caught inside `ReadMarker`, `IsCandidate` and `RunOwnershipStore.Read`. Path.Combine and GetAttributes cannot raise them for readdir names on Linux.
- The per-root try in SweepOnce now wraps Eligible as well.
- OperationCanceledException: SweepOnce takes no token. ReceiptFile already wrapped OCE as IOException at base, so this is unchanged.

**Q2: is a skipped root's state kept consistent?** Yes, verified by PD.

- Both skip paths return before `MarkDeleting`, so the marker is untouched.
- The index entry is removed only on `absent` or on completion.
- `LastRootId` advances on every examined entry, and `WriteState` runs after the loop, so a bad root does not pin the cursor.
- A bad root costs at most `MaxDescendantEntries` per sweep. It is retried every sweep forever, which matches base SafeTree.

**Q3: are ordinary trees and the earlier guarantees unchanged?** Yes.

- `CollectAnchored`, `DeleteBounded`, `UnlinkEntry` and the Linux anchored IO did not change between 1fee5b56 and 3a0d8c9b.
- All eight classes are green at 115/115.

**Q4: do the Windows masks match base?** Consistent with base. This is from reading the code; nothing ran on Windows.

- **Chain open:** base used CreateFile(GENERIC_READ, share 7, BACKUP_SEMANTICS|OPEN_REPARSE_POINT), and CreateFileW adds SYNCHRONIZE|FILE_READ_ATTRIBUTES. The tip's raw NtCreateFile uses 0x80100000 with share 7 and DIRECTORY_FILE|OPEN_REPARSE_POINT|SYNCHRONOUS_IO_NONALERT. Those are the same access and share. The explicit SYNCHRONIZE is required for SYNCHRONOUS_IO_NONALERT, which also settles round 2's Windows advisory.
- **Truncate:** base used FileStream(FileAccess.Write, FileShare.None), which is CreateFile GENERIC_WRITE plus the implicit SYNCHRONIZE|FILE_READ_ATTRIBUTES. The tip uses 0xC0100000 with share 0, NON_DIRECTORY|OPEN_REPARSE_POINT|SYNC.
- **Unlink:** the mask stays 0x00110080, the same as base's OpenRelative.

## Advisories (no further round needed)

- **A-1, dead code:** `EnumerateDirectories` and `Entries` in CheckpointTempRootSweep.cs (lines 554-578) have no remaining callers. The reparse-point guard in `Entries` is therefore unreachable. It can be deleted.
- **A-2, Windows:**
  - The truncate open requests GENERIC_READ, which base did not. Base had only FILE_READ_ATTRIBUTES through CreateFile mapping, and `GetFileInformationByHandle` needs only that. The only effect is that a file whose ACL denies read access would be retained (fail-closed).
  - The chain open omits FILE_OPEN_FOR_BACKUP_INTENT, which base's BACKUP_SEMANTICS added. That bit matters only when SeBackupPrivilege is enabled.
  - Confirm both in the pending native Windows row.
- **A-3, pre-existing at base, not new:**
  - `ReadMarker` opens `.checkpoint-test-root.json` by path with FileStream: it follows a link, blocks on a FIFO and reads without a bound. It runs at preflight, as it already did at base before SafeTree.
  - `IsDirectorySymlink` and the custody `File.Exists`/`Directory.Exists` stat a link target. Base SafeTree's GetAttributes did the same.
  - I did not execute the FIFO-marker case (it would hang the run).

## Housekeeping (the brief said to delete nothing)

These are left in place for the caller:

- the build outputs `bin-rc28t/`, `bin-rc28p/` and `bin-rc28r/` under each built project in /work/worktrees/task-c28cf67e;
- the six probe fixtures under `/tmp/antiphon-reviewc28-*` (no cleanup ran). The red run cleaned up its own `/tmp/antiphon-sweep-anchor-*` fixtures, and none are left;
- TRX files and logs in the session scratchpad.
