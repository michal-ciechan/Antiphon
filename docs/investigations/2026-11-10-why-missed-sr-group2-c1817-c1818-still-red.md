# Why missed: session-runner group 2 C1817/C1818 still red

One committed note for the second miss of rc chunk session-runner all-group-2.
Run d88de419 on rc/2026-11-16 = `6f8741d872d63507827014f972ccf71196722d4a`
reported `C1817RunnerRepositoryMaintenanceTests` 5 failed and
`C1818GcOwnerRobustnessTests` 4 failed. The earlier fixture fix
`a4ea62dd3bfaa20b20ab8609c9352825b96c9974` is in that cut. CARD-1781.

The sibling extract
`docs/investigations/2026-11-10-run-d88de419-sr group2 C1817/C1818 still red-windows-failure.md`
was not on origin at the fetch for this Debug (`git log --all` for `*d88de419*` was empty).
The 5+4 split below is the run 6591e378 TRX in
`docs/investigations/2026-11-11-run-6591e378-final-result.md` on
`origin/feat/card-task-d6579463` at `7a3c45c3f82e36c7b992d5b4b66316cd52127b47`,
minus the four C1817 failures that fix addressed, plus the commit-fixture test that fix added.

## Failure

TEST FIXTURE DEFECT. Product `RunnerRepositoryMaintenance` is unchanged.

Run 6591e378, before `a4ea62dd3`, failed 8 C1817 and 4 C1818. The TRX stacks are
`System.IO.FileSystem.RemoveDirectoryRecursive` into each class's private `Delete`,
which caught only `IOException`. Git for Windows marks the loose object and
`commit-graph` read-only, and `Directory.Delete` then throws `UnauthorizedAccessException`.

| Class | Method | 6591e378 message | After a4ea62dd3 |
|---|---|---|---|
| C1817 | `C1817_gc_budget_kills_the_child` | reason `git` instead of `budget` | POSIX child replaced; not a delete failure |
| C1817 | `C1817_gc_child_strips_inherited_git_config` | outcome `failed` instead of `ran` | same |
| C1817 | `C1817_skipped_while_task_git_in_flight` | `TimeoutException` at `WaitAsync` | `/bin/sleep` replaced |
| C1817 | `C1817_skipped_while_worktree_or_land_in_flight` | `TimeoutException` at `WaitAsync` | same |
| C1817 | `C1817_gc_pass_reduces_loose_objects` | `UnauthorizedAccessException` on `commit-graph` in `Delete` | `Delete` unchanged |
| C1817 | `C1817_skipped_while_lease_held` | `UnauthorizedAccessException` on a 38-character loose object in `Delete` | `Delete` unchanged |
| C1817 | `C1817_skipped_while_pool_repository_lock_held` | same loose-object delete | `Delete` unchanged |
| C1817 | `C1817_invalid_count_read_still_answers` | same loose-object delete | `Delete` unchanged |
| C1817 | `C1817_commit_fixture_pins_identity_and_disables_signing` | absent from 6591e378 (added by the fix); `Repo(..., 1)` then the same `Delete` | new delete failure |
| C1818 | `C1818_one_repository_fault_does_not_abort_the_pass` | loose-object `UnauthorizedAccessException` in `Delete` | `Delete` unchanged |
| C1818 | `C1818_budget_kill_reports_a_lock_the_gc_child_wrote` | same | `Delete` unchanged |
| C1818 | `C1818_budget_kill_leaves_a_lock_held_by_an_outside_process` | same | `Delete` unchanged |
| C1818 | `C1818_symlinked_git_directory_is_not_a_repository` | same | `Delete` unchanged |

That is 5 C1817 and 4 C1818 still red. The four non-delete C1817 rows are the ones
`a4ea62dd3` changed. The final-result note listed every 6591e378 row, including the
eight delete failures, as already fixed by that commit. The rerun kept the delete rows.

`a4ea62dd3` replaced `/bin/sleep`, `/bin/true`, `/bin/sh`, and `stat`, and it pinned
repo-local git identity. Reviews 211c38d3 A-1 and 5b3c6c26 A-5 had named those tools.
That clears the four non-delete C1817 rows. The TRX stacks for the other eight rows
are the read-only delete, and both `Delete` helpers still caught only `IOException`.

Linux red, before this fix, commit `96f27da65f95ac0a1f9988bf4420e61e0a263497`:
`C1817_readonly_loose_object_delete_removes_the_tree` threw
`UnauthorizedAccessException: Access to the path '.../objects/4d/de070c607382e53f749f4b220511c93de63b' is denied`
from `RemoveDirectoryRecursive` at `C1817RunnerRepositoryMaintenanceTests.Delete`.
Linux `Directory.Delete` ignores a read-only file. The witness marks the loose object's
parent read-only, which is the refusal this host raises. The green path is
`ReadOnlyDirectory.Delete`, already used by the group-130 fixture.

No row was added to `tests/expected-platform-skips.json`. `tests/impacted-classes.json`
stays pinned by `ImpactedClassesSelectorTests`.

## Introducing commit/card

introducing commit/card: `d18bf2e00812a6854744722857176f1ff7eef274` (CARD-1817) adds the C1817 `Delete` that catches only `IOException`. `58454a7a709431c8e974f17328dc75e68a41623f` (CARD-1818) adds the same helper. The incomplete repair is `a4ea62dd3bfaa20b20ab8609c9352825b96c9974` (cherry-pick of `6a7eb10efaf42dedd6a9f54d662a286f6370cbd8`, Debug 7742a3d9), which left both helpers in place.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 211c38d3, scope Full of the Linux subject (Final round 1, range
  `72fada66545c2dc96a0710827fbc57558700dd61..a6cdc4101fcaec7db97f217342c038ecd12e127d`).
  SessionRunner.Tests `(C1818*)|(C1817*)|(C1441*)|(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)|(PhoneHomeCommandDispatcherTests*)`
  78/78. Report `5fb7796f575a569170de7094b837ca207e17183d`. A-1 names `/bin/true`,
  `/bin/sleep`, and `File.CreateSymbolicLink`. It does not name `Directory.Delete`.
- 5b3c6c26, scope the round-2 diff (Final round 2, range
  `a6cdc4101fcaec7db97f217342c038ecd12e127d..a8e4325b23c34b08f81ad10cb2b7172195a6d775`),
  verdict CLEAN. SessionRunner.Tests
  `(C1818*)|(C1817*)|(C1441*)|(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)`
  34/34. Antiphon.Tests guard filter 68/68. Report
  `fe5961ca43f71147fe427a7abb487f5aaa46ae92`. A-5 adds `/bin/sh`, `stat -c`, and
  `/proc/<pid>/fd`.
- Debug 7742a3d9 had no Review. Fixture-only handoff was land. Its Linux selection,
  recorded in `abf7eba141fc290017cdd018abdf915077213158`, was SessionRunner.Tests
  `(C1818*)|(C1817*)|(C1441*)|(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)|(PhoneHomeCommandDispatcherTests*)`
  26/26 on the C1817 and C1818 classes, and the Antiphon.Tests ten-class union 135/135.
  Those runs execute `Directory.Delete` on git trees this host can remove. They stay
  green with the `IOException`-only catch.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Windows Full qualification lane, Integration, `tests/Antiphon.SessionRunner.Tests/C1817RunnerRepositoryMaintenanceTests.cs` and `tests/Antiphon.SessionRunner.Tests/C1818GcOwnerRobustnessTests.cs`

The class names were inside the Linux selections. Linux `Directory.Delete` removes a
read-only loose object when the parent directory is writable, so the finally blocks
passed. The Windows Full lane is where git for Windows sets the read-only bit and
`RemoveDirectoryRecursive` throws. Debug 7742a3d9 ran before the 6591e378 final-result
note attributed those stacks to `a4ea62dd3`, and its selection had no read-only parent seam.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.SessionRunner.Tests/C1817*.cs`, `tests/Antiphon.SessionRunner.Tests/C1818*.cs`, `tests/Shared/ReadOnlyDirectory.cs` -> `/*/*/(C1817RunnerRepositoryMaintenanceTests*)|(C1818GcOwnerRobustnessTests*)/*`

Proposed row only (not inserted). The Linux seam inside that filter is
`C1817_readonly_loose_object_delete_removes_the_tree`. A class run that omits it
stays green on Linux for the same reason the 7742a3d9 selection did.

```json
{
  "id": "runner-gc-readonly-delete",
  "globs": [
    "tests/Antiphon.SessionRunner.Tests/C1817*.cs",
    "tests/Antiphon.SessionRunner.Tests/C1818*.cs",
    "tests/Shared/ReadOnlyDirectory.cs"
  ],
  "classes": [
    "C1817RunnerRepositoryMaintenanceTests",
    "C1818GcOwnerRobustnessTests"
  ]
}
```

Project is `tests/Antiphon.SessionRunner.Tests`. The earlier `runner-gc-owner` row in
`docs/investigations/2026-11-10-why-missed-c1817-c1818-windows.md` selects the same
classes and still cannot see this refusal unless the read-only parent seam is in the run.

## Exemption

same cause as an already written note: none

The POSIX and identity miss is `docs/investigations/2026-11-10-why-missed-c1817-c1818-windows.md`.
This note is the read-only `Directory.Delete` refusal that commit left in place.
The group-130 loose-object delete is `docs/investigations/2026-11-10-why-missed-post-land-early-warning-g130.md`.
