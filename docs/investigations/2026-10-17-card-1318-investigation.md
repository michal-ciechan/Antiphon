# CARD-1318: Windows seal docs still say Linux-only, and the landed window is weaker

Read-only. No product code was changed and no test lane was run. The claims are the source on this branch, the same text on `origin/master`, and the stored CARD-1310 Review.

This branch is `feat/card-task-4bdc6ab5` at `6f36219bc2d93706aa79261d2ce86b91600c2a1a`, which is `origin/rc/2026-10-15`. `origin/master` is `9978f65274aa8fcac25c09547a3170f69c40a7bf`; its merge-base with this tip is this tip, so master contains the rc plus later commits. The Windows seal code is `6a42c44eef6641baf35dc86a3bf723d72a3ea895`, reviewed at `a625778ef9dbf532d1458afa8182e6119a6a1e30` (Review task `f82bf074-08be-4f2a-86dd-5abfc5fc2942`, succeeded 2026-10-17T06:00:39Z, `reviewedSourceClean: true`). That commit is on the rc via `f691d3c176ee5e02bf5c0516b7b82c44f5e6b908` and is an ancestor of the diagnosis merge `4ebdd47ded247558f72e2af8c740130f0b8f5513`. Nothing from `6a42c44ee` to this tip touches the custody method, `OutputCleanupTests.ExternalBuild`, or `docs/testing-and-build.md`.

All three items are real now. (a) is an rc docs fix and should ship on `rc/2026-10-15`. (b) and (c) are master-only hardening: they change which Windows files the landed seal adopts, and on master the types have moved.

## (a) Confirmed: the docs and the ExternalBuild skip still deny Windows adoption

The seal that landed does adopt on Windows. `OutputCustody.Stat` returns null only for `CheckpointPlatform.Other` (`tools/Antiphon.Checkpoints/Cleanup/OutputCustody.cs:56`). The class comment states the landed rule (`OutputCustody.cs:14-17`): after the observed exit, `CustodyStat` adopts a regular file or directory with one link that is not a reparse. `windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write` (`tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:1216-1219`) is the Windows witness, and Review reran it green (checkpoint run `20261017-064648-3f99`, CP-1 executed=1 passed=1).

The owner doc still describes the pre-seal rule, at the lines the review named:

- `docs/testing-and-build.md:466` ends the custody sentence with "off Linux nothing is adopted."
- `docs/testing-and-build.md:491` says adoption is "only for entries inside the seal-time custody window above (Linux only)".

`OutputCleanupTests.ExternalBuild` still skips every non-Linux caller (`OutputCleanupTests.cs:1327-1331`):

```
// Exited stands for the driver-observed build exit; seal-time custody uses Linux statx.
if (!OperatingSystem.IsLinux())
    Skip.Test("seal-time custody needs statx; off Linux nothing is adopted");
```

That helper is what `external_build_payload_is_adopted_at_seal_and_removed_on_green` and the other CARD-1242 matrix methods call (`OutputCleanupTests.cs:150` and the callers through `:369`). The skip is still right for this helper: those methods use Linux `ctime`, `ln`, and `UtcNow` without `AfterFileClock`. The message is the false part. It says the platform adopts nothing. The two earlier skips at `OutputCleanupTests.cs:319` and `:349` say only "seal-time custody needs statx" and were not named.

`git grep` of `origin/master` finds the same two doc sentences at the same line numbers and the same skip at `OutputCleanupTests.cs:1331`. A master-only edit leaves `origin/rc/2026-10-15` saying the rc's own seal does not exist. Line 472's "off Linux each entry is hashed" is the admission-stamp rule (Linux inode+ctime versus hashing every entry). It is not this seal. Leave it.

Not done, noted: on the rc, replace those two seal sentences with the landed Windows rule, and change the ExternalBuild comment and skip so they say this helper needs Linux statx, not that Windows adopts nothing.

Red-first, one filter, runnable on Linux: add `windows_seal_docs_match_the_landed_adoption` which reads `docs/testing-and-build.md` and `OutputCleanupTests.cs` and fails while "off Linux nothing is adopted" or the line-491 "(Linux only)" seal phrase is present. It goes red before the text edit.

`/*/*/OutputCleanupTests/windows_seal_docs_match_the_landed_adoption`

## (b) Confirmed: the Windows window does not check owner, mode, or a real change time

Review f82bf074 disclosed this and did not block the land: both owner sides are `uint.MaxValue`, the owner-only mode check is vacuous, and `Changed` is NTFS creation time, which the same account can set. Links and junctions stay refused. That source is unchanged.

`CheckpointNativeIo.Owner` is `GetEuid()` only on Linux and `uint.MaxValue` otherwise (`src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs:11`). The Windows `CustodyStat` return passes that same property as the stat owner (`CheckpointNativeIo.cs:64`). It never reads a SID. `OutputCustody.Refusal` then compares `entry.Owner` to `window.Owner` (`OutputCustody.cs:34`), and `For` requires `dir.Owner == context.Io.Owner` (`OutputCustody.cs:48`). On Windows both sides are `uint.MaxValue` for every path that stats, so `foreign-owner` cannot fire.

The mode check is `(dir.Mode & 0x3F) == 0` (`OutputCustody.cs:48`), the low six bits (group and other). Windows fills mode with only the type nibble: directory `0x4000`, file `0x8000`, reparse `0xA000` (`CheckpointNativeIo.cs:55-58`). `0x4000 & 0x3F` is 0, so every directory passes. The root is not created owner-only either: `CreateExclusiveDirectory` calls `MakeWindows(path, IntPtr.Zero)` on Windows and `mkdir` mode 448 (`0700`) on Unix (`CheckpointNativeIo.cs:28-31`). `CreateFreshDirectory` records the fresh root only when that same owner-and-mode test passes (`tools/Antiphon.Checkpoints/Cleanup/SafeWrite.cs:311-312`). The vacuous pass is why the Windows seal has a window at all. Making `For` strict without also creating an owner-only DACL stops `RecordFreshRoot`, `For` returns null, and every Windows green row is `cleanup_refused` again.

`Changed` is `FileTimeToUnixNs(info.Creation)` (`CheckpointNativeIo.cs:60`). Last write goes to `Modified` (`CheckpointNativeIo.cs:64`). Linux `Stat` puts statx ctime in `Changed` (`CheckpointNativeIo.cs:94-96`). NTFS creation time does not move on a later write, and the file owner can set both creation and last-write. A post-exit write whose times are then set back inside `[root creation, observed exit]` satisfies `Refusal` (`OutputCustody.cs:35-37`). Linux ctime is assigned by the kernel and moves forward on the write, so the same stamp-back still returns `changed-after-build`. The virtual fixture does not show this: its `CustodyStat` returns the stored stat (`tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointFixture.cs:167`) and `Custody_window_checks_owner_mode_and_times` drives that Linux-shaped stat (`HostCleanupCheckpointCustodyTests.cs:99-131`).

On `origin/master`, CARD-0987 commit `04edbada8` moved `OutputCustody.cs` and `SafeWrite.cs` to `src/Antiphon.HostCleanup/Checkpoints/`. The `For` / `Refusal` / `Stat` text matches. `CustodyStat` starts at `CheckpointNativeIo.cs:98` and the body matches, including `MakeWindows(path, IntPtr.Zero)` at line 94. `SafeWrite.CreateFreshDirectory` still gates `RecordFreshRoot` at lines 311-312. The PowerShell facade `ExclusiveDirectory.Create(string)` (`ExclusiveDirectory.cs:18` on master) also passes `IntPtr.Zero`; the seal path is the context overload, which calls `Io.CreateExclusiveDirectory`. A patch against this rc worktree edits `tools/Antiphon.Checkpoints/Cleanup/`, which master has deleted.

Not done, noted: on master, create the fresh root with an owner-only DACL, fill `Owner` and the mode check from that DACL, and stop treating settable NTFS creation time as ctime, without dropping `RecordFreshRoot` for an owner-only root.

Red-first, one filter, Windows, on a master branch: `windows_set_clock_and_foreign_dacl_are_not_adopted`. After the observed exit, `SetCreationTimeUtc` and `SetLastWriteTimeUtc` back inside the window must leave the file unrecorded; a root DACL that grants another SID must make `For` return null; an owner-only root must still seal and delete. Today the stamped file is adopted and the shared DACL still opens a window, so the test is red before the fix.

`/*/*/OutputCleanupTests/windows_set_clock_and_foreign_dacl_are_not_adopted`

This pass did not execute that probe. The mechanism is the landed source, which matches the review.

## (c) Confirmed: a failed open of an existing path is unknown, and cleanup refuses

`CustodyStat` opens with `CreateFile` access `0x80000000` (`GENERIC_READ`), share `7`, disposition `3` (`OPEN_EXISTING`), flags `FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT` (`CheckpointNativeIo.cs:41-42`). An invalid handle on a path that still exists returns kind `unknown` (`CheckpointNativeIo.cs:43-46`). `GetFileInformationByHandle` failure and `IOException` / `UnauthorizedAccessException` do the same (`CheckpointNativeIo.cs:51-52`, `:67-69`). `OutputCustody.Stat` turns `unknown`, inode 0, or changed 0 into null (`OutputCustody.cs:58`). `AdoptCreated` returns without a record when the stat is null (`SafeWrite.cs:103-104`). `CheckpointOutputLedger.Check` then returns `output-ledger-mismatch; left in place: ` for the first unrecorded path (`OwnedOutputLedger.cs:50-52`). `OutputCleanup` adds that to the refusals (`OutputCleanup.cs:107-108`) and `ReportValidator.Validate` returns `cleanup_refused` (`tools/Antiphon.Checkpoints/Report/ReportValidator.cs:26-27`).

A holder that did not grant `FILE_SHARE_READ` makes that `CreateFile` fail. The code does not read the Win32 error, so sharing violation 32 is not distinct from any other failed open. Review f82bf074 stated the holder it had in mind: a lingering compiler process. It also stated that this did not happen in run `20261017-064648-3f99`, where `bin-r1310/` was adopted and deleted. This Linux pass did not reproduce a Windows compiler handle. The fail-closed chain above is what the source does on any failed open.

Not done, noted: on master, retry only Win32 error 32 for a short bound after the observed exit, then adopt if the stat succeeds; a handle that never drops stays unrecorded.

Red-first, one filter, Windows, on a master branch: `windows_sharing_violation_retries_then_adopts`. Hold the pre-exit payload with `FileShare.None` across the first stat, release it inside the retry bound, and require adoption. Today one invalid `CreateFile` leaves it unrecorded, so the test is red. A handle held for the whole bound must still refuse.

`/*/*/OutputCleanupTests/windows_sharing_violation_retries_then_adopts`

## Remaining uncertainties

- (b) and (c) were not executed on Windows here. (b) follows from the stat fields the seal commit writes. (c)'s compiler cause is the review's statement; the code treats every failed open of an existing path as `unknown`, not only a compiler.
- Tightening (b) without an owner-only create path brings back the `cleanup_refused` defect CARD-1310 closed. That is why (b) and (c) stay off the rc.
- `docs/investigations/2026-10-15-card-1287-windows-unit.md` on master quotes the old skip as a historical exclusion row. It is not a contract test. This pass found no test that pins the skip string.
