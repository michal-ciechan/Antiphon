# Final Review 6e90a63b: CARD-1596 anchored path deletes (subject d1e5f56b)

Reviewed `feat/card-task-d1e5f56b` at 5c72a5daafbb50a48e9d0e9d055545ea112dc5e0 (base rc/2026-10-27 f84228f285dd3e4ad7b5dc8260cbc9f819256f50), on server2/Linux. I did not change any source. Every probe and mutation was restored, and `git status` is clean at the tip.

**Verdict: 3 defects (2 regressions, 1 incomplete closure). Next stage: code.**

## Runs (all through scripts/build-slot.ps1, slot granted, OutputPath=bin-rv6e/, UseAppHost=false)

| Run | Source | Selection | Result |
|---|---|---|---|
| build1 + run1 | clean tip 5c72a5da | the 7 named classes, plus the 2 Windows anchored classes | total 93, passed 80, failed 0, skipped 13. All 13 skips are the Windows-only rows: 6 in CheckpointWindowsAnchoredPathDeleteWitnessTests, 7 in CheckpointWindowsAnchoredDeleteTests. Same counts as the author. |
| build3 + run3 | tip + 4 reverts: SafeDelete.cs:94 -> Unlink; ToolCopyAuthority.cs:135 -> Unlink; sweep DeleteObserved -> Unlink/RemoveDirectory; ledger non-empty catch disabled | AnchoredPathDeleteWitnessTests | total 10, failed 5 as expected. Three swapped_parent_* rows fail because the foreign sentinel is deleted (FileNotFoundException on outside/payload). nonempty_owned_directory_is_left_in_place fails with "directory not empty". native_nonempty_owned_directory_is_left_in_place fails with "anchored-unlink-failed: errno=39". The other 5 rows pass, which is expected because they do not depend on the reverts. |
| build2 + run2 | tip + scratch ReviewProbe6e90Tests | 3 probes | 3 red, each confirming a defect (D-1, D-2) |
| build6 + run6 | tip + FIFO probe | 1 probe | red (D-3) |
| build7 + run7 | tip with the sweep DeleteObserved reverted to the base-form Unlink + FIFO probe | 1 probe | green: the base form reclaims the root, so D-3 is a regression |

- I did not run the whole Unit lane, because the brief says "No whole Unit". That leaves `ordinaryScopeCompleted: Interim`.
- I did not use the checkpoint tool, so there are no SHA-validated receipts and `reviewedSourceClean: false`.
- `check-evidence-diff.ps1 -BaseRef f84228f2 -HeadRef 5c72a5da`: commits=2 entries=0 violations=0, exit 0.
- I deleted all 33 `bin-rv6e` directories after confirming that each one resolves inside the worktree.
- PCs remain pending.

## Defects (landing owner: Code d1e5f56b-3397-4cab-ad3f-45e1193441fb)

### D-1: a tool copy sealed by an earlier build is now never removed, and a green `wait` turns Invalid (regression)

- **Where:** `src/Antiphon.HostCleanup/Checkpoints/ToolCopyAuthority.cs:133-148` (the `!entry.Identity.IsBound` and `!_record.RootIdentity.IsBound` refusals). These interact with `ToolCopyCleanup.RemoveSealed` (ToolCopyCleanup.cs:165-185) and `WaitCommand.cs:88-99`.
- **Failure:** a record written before this change has no `identity` or `rootIdentity`. `CheckpointReceiptJson.Parse` only requires reference-typed members, so `FilesystemIdentity` deserializes to its default value. `Load` then succeeds, because `Check()` never looks at identity. Next, `RemoveSealed` publishes `tool-removal-started.json`, and only after that does `Remove` return `tool-authority-entry-mismatch` without deleting anything.
  - The receipt says "tool-removal-incomplete ... tool may be partially removed ... manual inspection required". That is wrong: nothing was removed.
  - Every later Remove or Sweep takes the `PreviousRemoval` path and refuses permanently, even after a rollback.
  - `WaitCommand` turns a green run into `ExitCodes.Invalid` because `!receipt.Removed`.
  - Probe `Probe_legacy_unbound_tool_copy_record_is_removed`: Seal, then strip the identity fields, then re-digest. Output: `load refusal=<none> loaded=True`, `remove=tool-authority-entry-mismatch toolExists=True payloadExists=True`. At the base, the same record is removed.
- **Why:** any run started by the previous build and waited on or swept by this build hits this. That includes every run in flight when this lands on rc. It is fail-closed, but it is a permanent leak with a misleading partial-removal diagnosis.
- **Fix (either):**
  - (a) Bind a legacy entry at removal time. After the existing `Matches()` content, stamp and hash check, read the identity and use it for `DeleteAnchored`. This gives the same immediate-parent protection the sweep has.
  - (b) Refuse an unbound record inside `Check()`/`Load()` with a distinct reason, so no marker is written and the receipt reads "sealed by an earlier build; left in place; remove manually".
  - Add a witness that a legacy-shaped record (identity fields absent) is either removed, or refused before the marker is written. Also cover the `WaitCommand` green-exit consequence.

### D-2: the temp-root sweep is still open to an ancestor swap; only the immediate parent is closed (incomplete closure of the 54a5740c defect)

- **Where:** `CheckpointTempRootSweep.cs:280-286`, `DeleteObserved`.
- **Failure:** the identity is observed by `FilesystemIdentityReader.Read` (statx with AT_FDCWD, which follows intermediate symlinks) immediately before `DeleteAnchored`. `DeleteAnchored` opens the parent path with O_NOFOLLOW, but that flag only covers the last component. A grandparent swapped for a symlink, anywhere between enumeration and the delete, therefore yields the foreign entry's identity, and the anchored check passes.
  - Probe `Probe_sweep_grandparent_swap_keeps_foreign_file` uses root/a/b/payload.bin and swaps root/a for a symlink to outside/ inside `Length()`, which is the call just before `DeleteObserved`. Result: `swapped=True ... sentinelExists=False`, so the foreign outside/b/payload.bin is deleted.
  - Probe `Probe_observed_identity_through_swapped_ancestor_is_refused` reproduces the same thing at the native level.
- **Why:** temp roots hold nested run trees, so most payloads sit below depth 1. The author's stated residual, "an ancestor redirect onto another name of the same inode", understates this case. For the sweep, any different inode is deleted too. The ledger and tool-copy paths refuse this case only because they use a recorded identity (`ancestor_symlink_to_a_different_inode_is_refused`). This is not a regression: the base File.Delete followed links as well.
- **Fix (either):**
  - Record identities during the locked inventory, after `SafeTree`, and pass them to `DeleteObserved`.
  - Or resolve the parent component by component from a root fd, using openat with O_NOFOLLOW|O_DIRECTORY. server2 runs kernel 4.15, so openat2 RESOLVE_NO_SYMLINKS is not available.
  - Add the grandparent-swap witness.

### D-3: the sweep can no longer reclaim a root containing a FIFO or socket on Linux (regression)

- **Where:** `CheckpointTempRootSweep.cs:282-284`. `FilesystemIdentityReader.Read` returns default for Kind "other", and `DeleteObserved` then throws `anchored-identity-unbound`.
- **Failure:** `SafeTree` admits non-regular entries, and `EnumerateFiles` lists them. Each sweep now catches the throw and records `delete-IOException`, so the root stays in "deleting" forever. Ordinal sort order decides whether payloads after the entry are deleted at all.
  - Probe with a FIFO at root/child/d.sock: tip gives `completed=0 rootExists=True`. The base-form delete gives `completed=1 rootExists=False`.
  - Real producers include the .NET runtime's `dotnet-diagnostic-<pid>-*-socket`, which a killed child leaves behind. Checkpoint child hosts get TMPDIR set to the owned sandbox (docs/testing-and-build.md "CARD-1272 ... sets TMPDIR/TEMP/TMP"), and a dead owner is exactly the case the sweep exists for.
  - Note: a socket bound by .NET and then disposed is unlinked on Dispose, so I used a FIFO as the persistent non-regular stand-in.
- **Fix:** let `FilesystemIdentityReader` (or a sweep-local reader) bind fifo, socket and char/block kinds by device and inode, and let `AnchoredDelete.Allowed` accept non-directory, non-symlink entries for a file delete. Add a FIFO-in-root sweep witness.

## Advisories

- **A-1: loose error-code match in `OwnedOutputLedger.IsNonEmptyDirectory`.** The substring `win32=145` also matches 1450-1459, for example ERROR_NO_SYSTEM_RESOURCES 1450. Match the whole code. The failure still fails closed (left in place).
- **A-2: Windows delete semantics differ from base.** `DeleteAnchoredWindows` uses the legacy FileDispositionInfo (class 4). Modern DeleteFileW uses POSIX semantics. If another share-delete handle is open, the name lingers until that handle closes, so the next directory delete returns 145.
  - The ledger then leaves it in place.
  - The sweep retries next pass.
  - The tool copy becomes permanently `tool-removal-incomplete`. Base used Unlink and RemoveDirectory.
  - I could not verify this here. Consider FileDispositionInfoEx (class 21) with DELETE|POSIX_SEMANTICS, falling back to class 4, and have the desktop lane check a held-open reader.
- **A-3: Windows code read is correct; Linux unchanged.**
  - The ':' rejection applies only when `windows: true`. Linux is unchanged (the witness `linux_colon_in_a_file_name_stays_deletable` is green). `Path.GetFileName` never passes a drive qualifier as the name.
  - Access mask 0x00110080 = DELETE|SYNCHRONIZE|FILE_READ_ATTRIBUTES. Options are FILE_SYNCHRONOUS_IO_NONALERT|FILE_OPEN_REPARSE_POINT|DIRECTORY or NON_DIRECTORY, with disposition FILE_OPEN and share 7.
  - The Windows identity comes from the same `GetFileInformationByHandle` volume/index and basic CreationTime on both sides.
- **A-4: non-empty "left in place" (priority 4).**
  - No data loss: the late entry is kept and the reason names the directory.
  - The ledger file was already deleted before rmdir. The residue therefore has no custody and needs manual removal, as it did at the base (which threw instead). The recycle audit, CARD-1590, will see this residue. It is observable through the reason string.
- **A-5: `DeleteCreatedFile` change is safe.** The new Registry.Identity check in `DeleteCreatedFile` duplicates what `MatchesCreation` already requires. It is harmless, and no previously passing creation is now refused. The same holds for the Seal `IsBound` checks, which were already implied by `MatchesCreation` and the ledger `Check`.
- **A-6: remaining path-based deletes are outside the three named sites.** Each one targets only an own-named temporary or index file:
  - sweep index, state temporaries and legacy index: CheckpointTempRootSweep.cs:100, 109, 348, 368, 395 (the index directory is reparse-checked at :127);
  - ReplacementPublication.cs:86;
  - SafeWrite.cs:220 and 260 temporaries;
  - the NativeIo Move backup File.Delete;
  - tools/Antiphon.Checkpoints/Timing/TimingJournal.cs:300.
- **A-7: author-stated residuals confirmed.** Two remain:
  - the same-inode-alias ancestor redirect on recorded-identity sites;
  - a zero birth time matching device and inode.

Input waits: this change adds or alters no input wait.
