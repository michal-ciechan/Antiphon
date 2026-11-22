# Why missed in rc run 3528e6b1

## Introducing commit and card

Commit `47ed086b3ec73827db099648f377da0e271f8e91` (CARD-1514, FT-2)
introduced `Ft2Repo` in `LandNonCodeSkipTests.cs`. Its disposal used recursive
`Directory.Delete`, which throws `UnauthorizedAccessException` when it tries
to delete a read-only loose Git object on Windows. The fixture only caught
`IOException`. Windows qualification of `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`
reported seven failures in chunks `all-split-12`, `all-split-13` and
`all-split-15`.

## Approving review

Review `c5ce4172` was Full (Final profile) at
`72958a92a7fb0e6222f1bb8ae097cf281d16c1ad`. Its report, stored in
`23d1cf6ca190fa3dd671eb7c713a21d46f16544b`, records a Linux run of
`LandNonCodeSkipTests` and `AgentTaskLandingStateTests` with 205 passes,
including all ten original FT-2 results. The [original checkpoint list](../superpowers/plans/2026-11-20-fast-track-ft2-checkpoints.md)
also records Windows as unobserved.

## Outside the selection

The tests ran in the Linux lane. The missing coverage was native Windows
teardown in `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs`.
Linux permits unlinking a read-only file from a writable directory. The new
test removes write permission from the containing object directory on Linux
and sets the object file's read-only attribute on Windows. A Linux probe
executing the original disposal body reproduced the exception with a
`0444` object in a `0555` directory.

## Cheapest pre-land check

Proposed impacted-classes entry with `windows: required`:

`tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs` → `/*/*/LandNonCodeSkipTests/C_FT2_*`

This native selection includes the seven rc failures and the new disposal
test. The native Windows rerun is owed.

## Exemption

None.
