# Why short verification missed the Git stdout decoding defect in run 3528e6b1 group 113

## Introducing commit and failure

`b6feef2b3ccb0e273f925ac95a7caf1f4e401fa7` added the non-ASCII fixture
and `CheckpointIndexPathsTests.Head_and_stage_listing_read_from_files_are_git_s`
on 2026-11-20. The commit describes the checkpoint confinement file-read
optimization and carries no card number. Windows Full run `3528e6b1` on
`rc/2026-11-30` at `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` reported
one failure in `antiphon-all-group-113.trx` (3.7 seconds). The task brief
supplied the assertion from `Agrees`. Its Git reference read
`dir/caf├®.txt` while `StageListing` read `dir/café.txt`.

The Git command already used `ls-files -z -s`, which emits raw paths with
NUL delimiters. UTF-8 bytes `C3 A9` decode as `é` in UTF-8, `├⌐` in CP437,
and `├®` in CP850. The reported glyphs match CP850. The desktop
console's code-page setting was unavailable. The fixture's Git launcher
left `ProcessStartInfo.StandardOutputEncoding` unset, so
`Process.StandardOutput` used the console encoding. The index reader
already decodes path bytes with strict UTF-8. The production
`Invoke-CheckpointGit` fallback already captures bytes and decodes them
as strict UTF-8 under CARD-1608.

## Prior runs and review evidence

The previous Windows Full run `e7f92f66` used candidate
`26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`. Its source predates the
introducing commit and contains no such method. [Previous run report](2026-11-20-run-e7f92f66-ended.md)

Git records three review-fix commits, rounds 1
(`81aa81445b6fdc5229aaf9957f4ff80e058e6efc`), 2
(`cf03f132533878f2341f2d32b3e6610b43c195e8`) and 3
(`341c8a7d32cd0532d90aa5165bf9fd4234ff5b2e`). Round 3 records a
Windows encoding issue at the PowerShell child boundary and a Linux pass.
It converted the PowerShell listing to UTF-8 hex before writing it.
The test's direct Git capture still used the default encoding. The
available records give no approving review task ID, Full or Interim
scope, exact merge or review selection, or previous native OEM-code-page
pass for the direct comparison.

## Short verification gap

`CheckpointIndexPathsTests` is an Integration class in the main test
assembly. Short merge verification selects narrow witnesses. A Linux
run of this exact class at the failing rc source passed all four methods,
since its default encoding already decodes the fixture correctly. A
Windows witness also needs a legacy code page to expose the default,
or an explicit simulation of that default. The earlier PowerShell fix
covered a different process boundary in the same method.
[Short merge policy](../orchestration-loop.md#merge-verification-is-short-2026-11-18)

## Fixture repair and cheapest pre-land check

The fixture now sets the Git process's stdout encoding to UTF-8. The
stage-listing reference calls use `git -c core.quotepath=false ls-files -z -s`
for all of their comparisons.

| Changed path | Impacted class filter |
|---|---|
| `scripts/lib/CheckpointIndexPaths.cs` | `/*/*/CheckpointIndexPathsTests/*` |
| `tests/Antiphon.Tests/Checkpoints/CheckpointIndexPathsTests.cs` | `/*/*/CheckpointIndexPathsTests/*` |
| `tests/Antiphon.Tests/Checkpoints/CheckpointFixtureProcess.cs` | `/*/*/CheckpointFixtureProcessTests/*` |

For short verification, the exact `fixture_git_stdout_keeps_utf8_under_an_oem_default`
method invokes installed Git through the fixture's capture path, with CP437 and
CP850 as simulated fallbacks for an unset decoder. It reproduces the native
failure on Linux without changing the test host's console encoding. The
native row to add to the next available Windows pass is
`/*/*/CheckpointIndexPathsTests/Head_and_stage_listing_read_from_files_are_git_s`.
Run it on the repaired candidate alongside CARD-2048's C475 once
`3528e6b1` has ended. Record the Windows console code page and use the
failing run's setting if available. CP850 reproduces the observed glyphs.

## Exemption

None. CARD-1608 documents the related production fallback, while group 113
exposed the test fixture's Git reference capture.
