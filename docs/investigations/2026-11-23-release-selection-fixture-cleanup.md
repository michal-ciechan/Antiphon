# Release-selection fixture cleanup guard red on master (2026-11-23)

Debug task 29c987d3. The seeded Windows Full run on master `8d64518a9` failed one Final-guard case:
`TestFixtureCleanupGuardTests.Git_fixtures_use_read_only_safe_directory_cleanup`, with
"Use ReadOnlyDirectory.Delete for Git fixture trees" at
`tests/Antiphon.Tests/Scripts/ReleaseSelectionFixtures.cs:69 (directory)`.

## Reproduction

On server2, at `8d64518a97405a855afd694591e85ddfca06cad6`, one build-slot build of `tests/Antiphon.Tests`
and the exact method filter gave 1 failed, 0 passed, with the same single violation. The guard parses
test sources with Roslyn, so it does not depend on the platform or on timing. The red is a deterministic
new regression, not a Windows-only difference or a flake. It is the only flagged site.

## Introducing commit

`794aa6f1f` (2026-11-23 09:25Z, "refactor(scripts): port release selection and the rc project planner to
the core (unit 8)") added `ReleaseSelectionFixtures.cs`. `SelectionRepository.Commit("", ...)` empties
the working tree of a `git init` fixture and called `DirectoryInfo.Delete(true)` on each child directory.
The guard (`fd1a0c831`, 2026-11-22 10:21Z) was already on its base. The previous seed predates
`794aa6f1f`, which is why the earlier runs were green.

## Fix

Test-only: `ReadOnlyDirectory.Delete(directory.FullName)`, the helper from the 38-site sweep. It clears
read-only bits before the recursive delete. On Windows, a read-only file in the tree makes a raw
recursive delete throw. Only working-tree children are removed (`.git` is skipped). The behaviour is
otherwise unchanged. `scripts/regenerate-derived-impact-index.ps1` reports the index unchanged.

## Why it was missed

- `794aa6f1f` is one of the remaining-ps1 plan units, and an operator session pushed it straight to
  master. It did not go through a Code/Review/Merge pipeline. A short Merge would have selected the
  guard through the existing `test-fixture-cleanup` trigger (`tests/**/*.cs`) in
  `tests/impacted-classes.json`.
- The commit's verification named only its own ported classes (`ReleaseSelectionAttemptTests`,
  `ChangeAwareReleaseTests`, `RcProjectSelectionTests`, ...). Those pass on Linux, because a raw
  recursive delete only fails on Windows when a file in the tree is read-only.
- Before pushing a new or ported test fixture straight to master, run the Final-guards filter from
  [docs/testing-and-build.md](../testing-and-build.md) (85 cases, about 2 minutes on server2).
