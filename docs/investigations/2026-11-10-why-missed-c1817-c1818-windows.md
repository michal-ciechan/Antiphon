# Why missed: C1817/C1818 runner gc fixtures on Windows

One committed note for rc run 6591e378, chunk session-runner all-group-2, rc/2026-11-15 =
`ac6d00022e940ee3b90584c7d313567b4491c498`. Windows reported
`C1817RunnerRepositoryMaintenanceTests` 8 failed and `C1818GcOwnerRobustnessTests` 4 failed.
The sibling read `docs/investigations/2026-11-10-run-6591e378-session-runner-group2-windows-failure.md`
was not on origin at the time of this Debug (`git log --all` and a fetch of `origin/master`).
The 8+4 split below is reconstructed from the tests as they stood on that rc, matching the
counts exactly. CARD-1781.

## Failure

Both classes are `[Category("Integration")]` in `tests/Antiphon.SessionRunner.Tests`.
On the rc they launch POSIX tools and, for three C1817 methods, `git commit` with no
repo-local identity. Product `RunnerRepositoryMaintenance` already skips a reparse-point
`.git`, leaves `packed-refs.lock` in place, and kills with `entireProcessTree: true`.
This Debug changes tests only.

C1817 had 14 methods on the rc. Eight cannot complete on a desktop git-for-Windows host:

| Method | Fixture defect |
|---|---|
| `C1817_gc_pass_reduces_loose_objects` | `Directory.CreateSymbolicLink` (privilege) and `Repo(..., 15)` commits |
| `C1817_gc_budget_kills_the_child` | `/bin/sleep` plus `/proc/<pid>` |
| `C1817_gc_child_strips_inherited_git_config` | `/bin/true` |
| `C1817_skipped_while_worktree_or_land_in_flight` | `Hold` starts `/bin/sleep` |
| `C1817_skipped_while_task_git_in_flight` | `Hold` starts `/bin/sleep` |
| `C1817_skipped_while_lease_held` | `Repo(..., 1)` commit, no repo-local `user.name` / `gpgsign` / `hooksPath` |
| `C1817_skipped_while_pool_repository_lock_held` | same commit fixture |
| `C1817_invalid_count_read_still_answers` | same commit fixture |

The other six C1817 methods do not spawn a POSIX child and commit zero times
(`invalid_count_spawn`, `due`, `first_pass`, `lease_skip`, `disabled`, `status_omits`).

C1818 had 10 methods. Four both commit and use a POSIX tool or a symlink:

| Method | Fixture defect |
|---|---|
| `C1818_one_repository_fault_does_not_abort_the_pass` | `/bin/true` |
| `C1818_budget_kill_reports_a_lock_the_gc_child_wrote` | `/bin/sh` writing `packed-refs.lock` |
| `C1818_budget_kill_leaves_a_lock_held_by_an_outside_process` | `/bin/sh`, `/proc/<pid>/fd`, `stat -c` |
| `C1818_symlinked_git_directory_is_not_a_repository` | `File.CreateSymbolicLink` |

The other six are the retry test (no repository) and five source or DI reads.
Review 211c38d3 A-1 predicted the `/bin/true`, `/bin/sleep`, and symlink failures.
Review 5b3c6c26 A-5 added `/bin/sh`, `stat -c`, and `/proc/<pid>/fd`. That is the
four C1818 failures. The three commit-only C1817 methods are the remainder that
makes the C1817 count 8. A desktop global gitconfig (missing identity, `gpgsign`,
or a hook) fails those commits. The Windows TRX was not on this mirror, so those
three names are the methods that fit the count.

Linux red, before the fix, commit `327e47be6c8732ae079de4857b597cb64880140a`:
`C1817_forced_windows_fixture_uses_no_posix_tool` saw `sleep` where the Windows
seam requires `cmd.exe`, and `C1817_commit_fixture_pins_identity_and_disables_signing`
saw a config with no `user.name`. The green fixture uses `cmd.exe`, a powershell
holder, a content hash, `GetProcessById`, and a junction when symlink creation
throws `IOException` or `UnauthorizedAccessException`. `Repo` sets `user.name`,
`user.email`, `commit.gpgsign=false`, and `core.hooksPath` to `/dev/null` or `NUL`.

No row was added to `tests/expected-platform-skips.json`. The behaviour exists on
Windows with those tools. CARD-1832 has not landed. `tests/impacted-classes.json`
is pinned by `ImpactedClassesSelectorTests` and is not edited here.

## Introducing commit/card

introducing commit/card: `d18bf2e00812a6854744722857176f1ff7eef274` (CARD-1817) and `58454a7a709431c8e974f17328dc75e68a41623f` (CARD-1818)

CARD-1817 product `eb1bff5cded6ed08e6d54b94c1f58b6d2d713962` and its tests
`d18bf2e00812a6854744722857176f1ff7eef274` are both 2026-11-09 19:28:48Z and are
ancestors of `origin/master`. The brief names that land master batch 148-ish.
This checkout has no commit message containing `batch 148`.

CARD-1818 test `58454a7a709431c8e974f17328dc75e68a41623f` (`test(card-1818): red
witnesses for gc owner and stop faults`, 2026-11-10 06:40:32Z) introduces the
`/bin/true`, `/bin/sleep`, and symlink witnesses. The live-lock red
`e753cc6e908dec981d107d826af14acb4f0ee7df` adds `/bin/sh`, `stat -c`, and
`/proc/<pid>/fd`. Product follow-ups on the same timestamp, also ancestors of
`origin/master`: `4c5eb59b43f53d3cf9f908eece3143c2ea752896` (keep the gc owner
and stop kill list) and `df1248539b163c0bdd30d1b800cefefe0339179c` (leave a
packed-refs lock). The brief names that land master batch 157. This checkout
has no commit message containing `batch 157`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 211c38d3, scope Full of the Linux subject (Final round 1, range
  `72fada66545c2dc96a0710827fbc57558700dd61..a6cdc4101fcaec7db97f217342c038ecd12e127d`),
  verdict one blocker D-1. Selection actually run, Linux server2:
  SessionRunner.Tests `(C1818*)|(C1817*)|(C1441*)|(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)|(PhoneHomeCommandDispatcherTests*)`
  78/78 (C1818 9, C1817 14). Antiphon.Tests guard-style filter 59/59, plus
  `C1729LayeringGuardTests` 9/9. Stop smoke C1507 passed=38 failed=0.
  Report `5fb7796f575a569170de7094b837ca207e17183d` says the Windows pieces were
  read, not run. A-1 names `/bin/true`, `/bin/sleep`, and `File.CreateSymbolicLink`
  with no OS gate. Owed native Windows: taskkill `/T /F`, Win32_Process identity,
  PS 5.1 JSON. That run was not executed.
- 5b3c6c26, scope the round-2 diff only (Final round 2, range
  `a6cdc4101fcaec7db97f217342c038ecd12e127d..a8e4325b23c34b08f81ad10cb2b7172195a6d775`),
  verdict CLEAN. Selection actually run, Linux server2:
  SessionRunner.Tests `(C1818*)|(C1817*)|(C1441*)|(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)`
  34/34 (C1818 10, C1817 14, C1441 6). Antiphon.Tests
  `(C1818*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)`
  68/68. Stop smoke C1507 passed=38 failed=0.
  Report `fe5961ca43f71147fe427a7abb487f5aaa46ae92`. A-5: the new witnesses add
  `/bin/sh`, `stat -c`, and `/proc/<pid>/fd`, still with no runtime OS skip and
  no Windows exclusion. The round-1 native Windows witnesses stay pending.
- CARD-1817 review a38b6048, report commit `b04f5070a82150b08119313a447698a0848b8df9`,
  CLEAN round 2 of `df669d160`. Its A-5 was a symlinked `.git` still followed
  (later closed by CARD-1818). It did not name `/bin` tools as a Windows failure.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Windows Full qualification lane, Integration, `tests/Antiphon.SessionRunner.Tests/C1817RunnerRepositoryMaintenanceTests.cs` and `tests/Antiphon.SessionRunner.Tests/C1818GcOwnerRobustnessTests.cs`

The class names were inside both Linux selections. The Linux lane runs `/bin/sleep`,
`/bin/true`, `/bin/sh`, `stat`, and `Directory.CreateSymbolicLink` successfully, and
the review hosts have a git identity that lets `git commit` succeed. The Windows
Full lane is the one that executes the same Integration classes against git for
Windows, where those processes and an unprivileged symlink fail. Both reviews
recorded that native Windows check as owed and did not schedule it, so the Linux
lane could not see this failure.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.SessionRunner.Tests/C1817*.cs`, `tests/Antiphon.SessionRunner.Tests/C1818*.cs`, `tests/Antiphon.SessionRunner.Tests/MaintenanceFixture.cs`, `src/Antiphon.SessionRunner/RunnerRepositoryMaintenance.cs` -> `/*/*/(C1817RunnerRepositoryMaintenanceTests*)|(C1818GcOwnerRobustnessTests*)/*`

Proposed row only (not inserted). `"windows": "required"` so the Linux filter still
runs and `windowsFilters` lists the same classes as owed on the native Windows lane:

```json
{
  "id": "runner-gc-owner",
  "globs": [
    "tests/Antiphon.SessionRunner.Tests/C1817*.cs",
    "tests/Antiphon.SessionRunner.Tests/C1818*.cs",
    "tests/Antiphon.SessionRunner.Tests/MaintenanceFixture.cs",
    "src/Antiphon.SessionRunner/RunnerRepositoryMaintenance.cs"
  ],
  "classes": [
    "C1817RunnerRepositoryMaintenanceTests",
    "C1818GcOwnerRobustnessTests"
  ],
  "windows": "required"
}
```

Project is `tests/Antiphon.SessionRunner.Tests`. The stop-supervisor PS 5.1 /
taskkill witnesses stay a separate owed native run. They are not this row.

## Exemption

same cause as an already written note: none
