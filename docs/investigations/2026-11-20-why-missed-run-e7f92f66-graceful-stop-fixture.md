# Why missed: graceful-stop fixture dropped a new dot-source

rc run e7f92f66, scripts/all, `scripts/test-apphost-graceful-stop.ps1`: 4 passed, 7 failed.
T-1 through T-6 and T-12 each died in the temp copy at `restart-apphost.ps1:104`:

```
. (Join-Path $PSScriptRoot 'lib/windows-phone-home-env.ps1')
The term '...\scripts\lib\windows-phone-home-env.ps1' is not recognized
```

T-7, T-8, T-9 and T-10 passed. T-9 and T-10 pass `-StopOnly`, which skips that dot-source.
The desktop log directory is not readable from this runner. The assertion text above is the
quote in `docs/investigations/2026-11-20-run-e7f92f66-ended.md` (origin `feat/card-task-b7e96a42`
`4b09d9ccd8ee655566a7f70542a5813bc685bd9b`, section 1b). The same seven cases are the ones that
execute the copied restart script without `-StopOnly`.

Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

## Introducing commit/card

introducing commit/card: `e1b42f895ce3df67214ad3968778f2cf850f15fb` (CARD-1744)

`fix(c1744): supervise desktop-win from phone-home.json`, 2026-11-19. Cherry-pick of
`10de7e41f951e423846d4d9962a2fff3220da574`. It added the dot-source. It is in rc cut
`26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248` and absent from the previous green scripts run
a6278259 (checkout `997f9b8d9`).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review task `ea0373d4`, round 1 of S4. The review file named by the amendment is not in this
  checkout. The closed list that Code was required to run is
  [2026-11-19-card-1744-s4-s5-plan.md](../superpowers/plans/2026-11-19-card-1744-s4-s5-plan.md)
  CP-10 through CP-14, as amended by
  [2026-11-19-card-1744-s4-review-amendment.md](../superpowers/plans/2026-11-19-card-1744-s4-review-amendment.md)
  (CP-11 floor 29):
  `/*/*/C1744G3PhoneHomeSupervisionTests/*`,
  `/*/*/(C1744G3PhoneHomeSupervisionTests*)|(RestartApphostLockHintTests*)|(LinuxServerRestartGateTests*)/*`,
  `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder`,
  `/*/*/PtyHostDeploymentGuardTests/*`,
  `/*/*/DockerStackDocumentationTests/*`.
  Scope of that review is the S4 code slice, not the rc scripts suite.

## Outside the selection

why the failing test was outside that selection (lane/category/path): scripts lane,
`scripts/test-apphost-graceful-stop.ps1`, rc `scripts/all`. The S4 closed list and the short
Merge guard union never name this harness. `scriptCensus` runs it only in the rc scripts suite.
`C1744G3PhoneHomeSupervisionTests` copies the helper for the autostart script. It does not copy
`restart-apphost.ps1` into the graceful-stop fixture.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/restart-apphost.ps1` -> `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1`

There is no TUnit class for this harness. `tests/derived-impact-index.json` does not map
`scripts/lib/windows-phone-home-env.ps1` at all. It maps `scripts/restart-apphost.ps1` only to
`DockerStackDocumentationTests`, `LinuxServerRestartGateTests`, and `ProcessSpawnLimitTests`.
None of those copy the fixture. Follow-up, not done here: a selector row from
`scripts/restart-apphost.ps1` and `scripts/lib/windows-phone-home-env.ps1` to this script
command. The index's targets are TUnit classes, so that row is a script target, not a new class edge.

## Exemption

same cause as an already written note: none

## Guard

`scripts/lib/apphost-fixture-closure.ps1` parses literal
`. (Join-Path $PSScriptRoot '...')` / `"..."` lines and walks them. The graceful-stop fixture
copies that closure instead of a hand list. T-13 fails when any parsed file is absent from the
fixture, and fails closed if the parser misses `lib/windows-phone-home-env.ps1`. T-14 appends a
synthetic dot-source that itself dot-sources a second file; both must be copied. A new library
in the recognized form fails T-13 until the copier picks it up. A form the parser does not
recognize is still caught by the cases that execute the copied script.

The same copier is what `test-apphost-dead-launch.ps1`, `test-apphost-lock-hints.ps1`,
`test-apphost-health-window.ps1`, and `test-apphost-server-version.ps1` now use. Those four
were not in the rc `scriptCensus`, which is why scripts/all reported only this harness.
`test-apphost-main-worktree-guard.ps1` runs the real linked worktree and exits on the worktree
guard before the phone-home dot-source. `dev-aspire.ps1` dot-sources the helper with
`Join-Path $root`, and the graceful-stop fixture replaces that file with an inert stub.
`test-cutover-postgres-server2.ps1` reads the restart script as text. `test-restart-*.ps1` and
`test-session-runner-restart*.ps1` do not exist.

`Get-WindowsPhoneHomeLaunchRefusal` returns null when `C:\Antiphon\runner\phone-home.json` is
absent. The rc log died at the missing file, before that check. The 2026-11-19 desktop census
recorded the file absent. A later phase-2 file makes every non-`-StopOnly` restart exit 3.
That is production behavior, not this miss.
