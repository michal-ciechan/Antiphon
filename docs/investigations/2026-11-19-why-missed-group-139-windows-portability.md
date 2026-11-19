# Why missed: group-139 Windows scratch-removal portability

Windows Full rc run `e7f92f66` on `rc/2026-11-29`, chunk `antiphon-all-group-139`
(TRX `C:\Antiphon\releases\candidates\rc-2026-11-29-26a99f1a747d\logs\2026-11-19-1430-e7f92f66\antiphon-all-group-139.trx`):
366 of 368 passed. The two failures are in `OwnedTempLeakTests` (CARD-1981 slice 1).

`Review_scratch_remove_refuses_a_symlinked_foreign_worktree` read the foreign
worktree README as `seed\r\n` and expected `seed\n`. Git for Windows checks the
committed blob out with CRLF when `core.autocrlf` is true. The assertion is the
proof the symlink target was not deleted or rewritten, so it stays exact modulo
that newline.

`Review_scratch_remove_refuses_a_dirty_nested_worktree` asked porcelain to
contain the `Path.Combine` path. Git printed the same long temp path with `/`.
The comparison stays case-insensitive and now normalises both sides with
`Path.GetFullPath` and `/`. The recorded Windows text was the long form, not an
8.3 short name.

## Introducing commit/card

introducing commit/card: `e86e1d9b08896e59002d9d85a6bef601764d192e` (CARD-1981 slice 1; landed on master by the replay tip `0cbe40ee1686903c7c1ffd27f8cdd8404afd5ca6`)

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No review write-up in this checkout names a review task id for CARD-1981 slice 1. The dispatch records that the review checked Linux only. The committed Linux selection is `docs/superpowers/plans/2026-11-18-card-1981-checkpoints.md`: CP-1 `/*/*/OwnedTempLeakTests/*` and CP-2 `/*/*/C1677TempFamilyRegistryTests/*`, both on server2. Code task `6af33109-b5ef-426f-960f-83619dc327a1`. That plan has no Windows row for these two methods.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Unit, `tests/Antiphon.Tests/TestHelpers/OwnedTempLeakTests.cs`. The class ran on Linux server2 and in the Linux merge verification. Run `e7f92f66` is the first Windows execution. Merge verification has no Windows lane for a new test, so a CRLF checkout and a `/` porcelain path never met the assertions.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/OwnedTempLeakTests.cs` -> `/*/*/OwnedTempLeakTests/*`

That entry reruns the class when the file changes. It does not execute it on Windows. The check that would have failed before the rc run is a Windows execution of `/*/*/OwnedTempLeakTests/Review_scratch_remove_*`, or the lint proposed below.

## Exemption

same cause as an already written note: none.

`docs/investigations/2026-11-15-why-missed-g139-compose-fixture-delete-label.md` is an older group-139 chunk with a different cause (a missing timing-operation label).

## Proposed guard

Not built. Two shapes are enough for a lint over a Merge diff, or a Windows-compat scan of tests added since the merge base:

- `File.ReadAllText` (or `ReadAllTextAsync`) compared with `ShouldBe` to a literal that contains `\n`, when the file was committed and checked out by git in the same test.
- `git worktree list --porcelain` (or any git porcelain) compared with `ShouldContain` to a `Path.Combine` / `DirectorySeparatorChar` path that is not normalised to `/` on both sides.

A test-written `File.WriteAllText(..., "...\n")` round-trip is a different shape. The Windows run reached the porcelain assertion after `keep-me\n`, so that exact check already holds and this repair leaves it.
