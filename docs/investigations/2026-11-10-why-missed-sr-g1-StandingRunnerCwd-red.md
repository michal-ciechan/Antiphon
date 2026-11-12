# Why missed: sr g1 StandingRunnerCwd red

One committed note for rc chunk session-runner all-group-1 in run ae84478d on
rc/2026-11-17 = `3db1a43b02246706dc28075fce7e7b5e84804ddb`. CARD-1781.
`StandingRunnerCwdTests` failed 2 of 24 on the native desktop. The other 1228 tests in the chunk passed.

The Windows messages are in
`docs/investigations/2026-11-10-run-ae84478d-sr-g1-StandingRunnerCwd-red-windows-failure.md`
on `origin/feat/card-task-85666c1b` at `54cf4889225fb8193d138a5186df6465fa461a49`.
That extract was not on origin when this Debug (06aa57e7) started.

## Failure

TEST FIXTURE DEFECT, deterministic on Windows. No product change. This is not load, and not the
CARD-1230 temp-root guard or the CARD-1832 taxonomy.

| Test | Windows message | Frame |
|---|---|---|
| `Symlinked_or_non_directory_standing_checkout_is_refused_at_launch` | `UnauthorizedAccessException: Access to the path 'C:\tmp\c1825-link-...\agents\linked-agent' is denied.` | `System.IO.FileSystem.DeleteFile` in `finally`: `File.Delete(linked)` |
| `Standing_symlink_swapped_before_start_is_refused` | `UnauthorizedAccessException: Access to the path 'C:\tmp\c1834-swap-...\agents\real-agent' is denied.` | `System.IO.FileSystem.DeleteFile` in `finally`: `File.Delete(leaf)` |

On Windows a directory symlink is a directory reparse point. `File.Delete` (`DeleteFile`) refuses it with
access denied. `Directory.Delete(path)` without recursion removes the link and leaves the target. On Linux,
`File.Delete` unlinks the symlink, so both tests were green on server2. The desktop created the symlinks
(the extract lists the leftover links in `C:\tmp`), so the symlink privilege is present there.

Fix on `feat/card-task-06aa57e7`: the test file is byte-identical to
`54cf4889225fb8193d138a5186df6465fa461a49` (`DeleteDirectoryLink`, which uses `Directory.Delete` on Windows
and `File.Delete` elsewhere), plus one new test, `Directory_link_cleanup_removes_the_link_not_the_target`, and its `RepoRoot` helper (additions only).
On every host, that test checks that `DeleteDirectoryLink` is the class's only `File.Delete` site, that the
helper removes a real directory symlink and keeps the target's file, and that it leaves a non-link directory
alone. Linux red: with the base `finally` shape (`File.Delete(leaf)`) put back, the new test failed 1/1 and
listed `["File.Delete(leaf);", "File.Delete(path);"]`. Green: 25/25 in the class.

Run 6591e378 (cut `ac6d00022e940ee3b90584c7d313567b4491c498`) passed this chunk. Neither failing test existed
at that cut. Both came in between `ac6d00022` and `3db1a43b0`.

## Introducing commit/card

introducing commit/card:

- `b4c644d25feabe84b8b9daca073b6ca0e359e24d` (CARD-1825, rc pick `b4a66d255298607521fae5b501ccaf372c6032e9`)
  adds `Symlinked_or_non_directory_standing_checkout_is_refused_at_launch` with `File.Delete(linked)` and
  `File.Delete(agentsLink)`. Fix `7ddbbb085454660bcbefc66b801193eec87bce62` (rc `9654d17a4`) did not touch them.
- `62f08cd78a0b84415416361fcf3e15652747c096` (CARD-1834, rc pick `dd00c2829ea13461fc027205db8e6f07ba34831e`)
  adds `Standing_symlink_swapped_before_start_is_refused` with `File.Delete(leaf)`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 20451c4b, scope Full (CARD-1825 Final round 1, range `ac6d00022..7ddbbb085`, clean), report
  `c7d1bb4b5ba5f91d7e400e810b808772ce9d9e10`. SessionRunner.Tests `StandingRunnerCwdTests` 21/21. Antiphon.Tests
  nine-class guard filter 78/78. `scripts/impacted-classes.ps1 -Range` selected nothing. Linux only. A-4
  lists the native Windows junction/link witness as owed and says it was not run.
- fbdfe9f8, scope Full (CARD-1834 Final round 1, subject `0e937620d416bdcdf980c45edbe068d28493ba9a`, behavior
  clean, `reviewedSourceClean` false), report `4acb46febf8253b2079d7c477d7ad1b39dd11494`.
  `StandingRunnerCwdTests` 24/24, nine-class guard filter 78/78, impacted-classes 0 filters. Linux only. Its A-4
  says the Windows witness "stays pending on Windows".

## Outside the selection

why the failing test was outside that selection (lane/category/path): Windows Full qualification lane, Unit,
`tests/Antiphon.SessionRunner.Tests/StandingRunnerCwdTests.cs`

Both Reviews ran the class by name, on Linux, where `File.Delete` on a directory symlink succeeds. The class is
not in the derived-impact `windowsOnly` roster, and no impacted-classes row requires Windows for it. The owed
native Windows witness (A-4 in both Reviews) was never run before the cut. The defect is in `finally`, so a
review of the assertions would not see it. The rc chunk was the class's first Windows execution.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.SessionRunner.Tests/StandingRunnerCwdTests.cs`, `src/Antiphon.SessionRunner/StandingCheckoutLink.cs`,
added literal `CreateSymbolicLink(` under `tests/Antiphon.SessionRunner.Tests/**` ->
`/*/*/StandingRunnerCwdTests/*` with `"windows": "required"`

This is a proposed row only and is not inserted. `ImpactedClassesSelectorTests` pins the table.

```json
{
  "id": "standing-checkout-links",
  "globs": [
    "tests/Antiphon.SessionRunner.Tests/StandingRunnerCwdTests.cs",
    "src/Antiphon.SessionRunner/StandingCheckoutLink.cs"
  ],
  "added": {
    "glob": "tests/Antiphon.SessionRunner.Tests/**",
    "literals": ["CreateSymbolicLink("]
  },
  "classes": [
    "StandingRunnerCwdTests"
  ],
  "windows": "required"
}
```

On Linux, `Directory_link_cleanup_removes_the_link_not_the_target` now catches this shape in this class.
Link fixtures in other classes still need the Windows row. Known latent site, not fixed here:
`RunnerWorkspaceServiceTests.TryDeleteLink` calls `File.Delete` on directory links and swallows the
`UnauthorizedAccessException`, so on Windows it leaks the link instead of failing.

## Exemption

same cause as an already written note: none

## Owed

Native Windows run of `/*/*/StandingRunnerCwdTests/*` (SessionRunner.Tests) at this branch's tip: 25 passed, and
no new `C:\tmp\c1825-*`, `c1834-*` or `rc-ae84478d-link-*` debris. The debris left by run ae84478d is still on the desktop.
