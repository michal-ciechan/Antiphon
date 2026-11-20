# CARD-1981 c487 probe leak fix: Review round 2

Task 4334d7fa (Review, ordinary round 2, diff only, blockers only). Reviewer: Claude Opus 5.5.
Author of the subject: Grok.

- Subject: `origin/feat/card-task-1ddb3ae6` at `5ea533563ae34d4d193ddff8ae4edbb0ca94c2f1`.
- Diff reviewed: `c434223edbf1b4e7231e19ccc1ecdbd4408a65a8..5ea533563ae34d4d193ddff8ae4edbb0ca94c2f1`
  (red `2a323fc2`, fix `942d1fff`, plan note `5ea53356`).
- Round 1 blocker D-1: `ProbeOwnedRoot` tracked the shared stable root
  `antiphon-owned-c4870000000000000000000000000000`, so `OwnedTempRoot.ReleaseAll`
  deleted fresh sibling directories that other processes own.

## Verdict: CLEAN

D-1 is closed. No new blocker and no regression in the diff.

## Questions from the brief

1. **D-1 closed, and the probe still cleans up after itself.** `ProbeOwnedRoot`
   (`TestClassificationPolicyTests.cs:684`) now only creates and normalizes the root. It no
   longer calls `OwnedTempRoot.Track`. The only remaining `Track` in the c487 path is the per-run
   directory in `WithTempDirectoryAsync` (`:782`). That directory has a fresh GUID name and is
   released in `finally` by `ReleaseTempDirectory`, which calls `OwnedTempRoot.Release` once the
   directory is gone. If the delete fails, the directory stays tracked. `ReleaseAll` then removes
   it from the `[After(Assembly)]` hook and from `ProcessExit`. Both hooks touch only this
   process's own GUID directory and never the shared root. A run that is killed before any hook
   runs leaves its GUID directory behind. The next `WithTempDirectoryAsync("c487-probe-")` sweeps
   it once its last write is more than 24 hours old (`:731`, `StaleProbeAge`). The one directory
   that stays for good is the empty stable root. This is by design: it is a single entry, it
   matches the registered `antiphon-owned` sweep family (`RunnerTempSweep.cs:119`), and that
   sweep is off. The release witness tests the real path: a child `dotnet exec` with a scratch
   `TMPDIR` calls `ReleaseAll`, then exits, which runs the assembly hook and `ProcessExit`. The
   parent then re-asserts the root, the fresh sibling, the near-miss and the unrelated directory,
   with marker bytes. If the filter matched zero tests, the child would exit non-zero and the
   parent would fail, so this witness can go red.
2. **The sweep cannot delete what it does not own.** Each entry must pass all of these checks:
   the root's parent is the temp directory, the entry is a direct child, the name is exactly
   `c487-probe-` plus 32 lowercase hex digits, the entry is not a reparse point, and the last
   write is more than 24 hours ago. A fresh sibling, a non-probe name and an uppercase near-miss
   are all skipped. The round-1 test at `:315` and the new release witness cover these cases. A
   directory that another live process holds is safe unless that process created it more than
   24 hours ago and has not written to it directly since. Probe runs last minutes (the child
   timeout is 5 minutes), so this is not a practical risk.
3. **The reparse-point check looks at the entry itself, not the link target.** `IsProbeReparsePoint` calls
   `File.GetAttributes(full)` on the enumerated entry. `full` comes from `Path.GetFullPath`, which
   does not resolve links. On Unix, .NET reads that attribute with `lstat`, and the witness
   asserts `ReparsePoint` on the link before it sweeps. The check runs before the age read, so a
   link is never dated, followed, or passed to `ReleaseTempDirectory`. It fails closed: an
   `IOException` or `UnauthorizedAccessException` counts as a reparse point and is skipped.
   **Linux symlink: observed** (witness green here). **Windows junction: UNOBSERVED.** A junction
   carries `FILE_ATTRIBUTE_REPARSE_POINT`, so the same check should apply, but this round did not
   run it on Windows.
4. **No other site uses the tracked-shared-root pattern.** `ProbeOwnedRoot` has three callers:
   the stale-sibling test (`:317`), the release witness child (`:427`) and
   `WithTempDirectoryAsync` (`:771`). None of them tracks the root. Repo-wide `OwnedTempRoot.Track`/
   `CreateOwned` callers either create a fresh GUID directory (`OwnedTempLeakTests`, the c487
   foreign parent) or a per-task GUID directory
   (`DelegationDispatchRecoveryBoundaryTests.BriefIdentity.cs:839`, `antiphon-c1150-<taskId>`).
   No other stable, shared name is tracked.

## Evidence (server2, this checkout at the reviewed SHA)

- Build: `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` through `build-slot.ps1`.
  Slot `lease=e8abee25-5707-4896-9434-a8ba03ba9437 waited=0s held=274s`, 0 errors, native
  default output. `load1=15.71` at start.
- Witnesses: `/*/*/TestClassificationPolicyTests/(C487_probe_release_leaves_fresh_foreign_siblings*)|(C487_probe_stale_sweep_skips_a_probe_named_reparse_point*)`
  with `--no-build -p:UseAppHost=false --timeout 570s` and `ANTIPHON_LINUX_COMPILER_POLICY=0`.
  The run waited for load from 22.9 down to `load1=9.17`. Result: total 2, failed 0, skipped 0,
  duration 12s 775ms. Slot `lease=fe6c04dd-04e0-438a-ae11-0cc4960dff90 waited=0s held=18s`.
  No `/tmp/antiphon-owned-c487…`, `/tmp/c487-release-*` or `/tmp/c487-reparse-*` remained
  afterwards.
- Red at base: the author recorded 2/2 red at `2a323fc2` in the plan. This round did not
  reproduce it (the brief limits the rerun to the build and the two witnesses). The code supports
  it: at the base, `Track(root)` makes `ReleaseAll` delete the root, and without the reparse
  check the sweep removes the backdated link.
- `check-evidence-diff.ps1` over the round range `c434223e..5ea53356`: 3 commits, 0 violations.
  Over the full task range `1443b7eb..5ea53356`: 8 commits, 0 violations.
- `git merge-tree --write-tree`: clean against the brief's master `f66039b1` (tree `33d95fe8`),
  and clean against the fetched `origin/master` `e54065fe` (tree `ef08c839`).
- The plan diff (V-5, V-6, the CP renumbering, the new CP-3 driver build, the 45-result floor)
  matches the two added methods. The class's earlier 43 results plus these 2 give 45.

## Advisory (does not block the land; caller to card)

- A-1 (pre-existing, outside this diff): `SweepStaleProbeDirectories` checks the root's parent
  but does not check whether the stable root itself is a reparse point. On a shared Linux
  `/tmp`, another uid could plant `/tmp/antiphon-owned-c487…` as a symlink. The probe would then
  create its directories, and run the sweep, inside the link target. The sweep would still delete
  only stale entries named exactly `c487-probe-<32 hex>` that this uid can delete, so the impact
  is small. The fix would be to refuse, or fall back, when `ProbeOwnedRoot` resolves to a
  reparse point.

--- next stage ---
next: land
handoff: CARD-1981 c487 probe fix Review r2 CLEAN at 5ea533563ae34d4d193ddff8ae4edbb0ca94c2f1: D-1 closed (stable root untracked, per-run dirs still released/swept), reparse skip on entry; witnesses 2/2 green; land feat/card-task-1ddb3ae6 with -ExpectedSourceSha 5ea533563ae34d4d193ddff8ae4edbb0ca94c2f1.
artifact: docs/superpowers/reviews/2026-11-20-c487-probe-review-r2.md
