# Windows Full run `79d9e6f9` failures

## Group 134 · C1323 marker and policy row

Nightly S16 (`b5ed36161f01d59cf7f2172aaf49ffa914675b13`) moved the capture-shell checks into `QualificationTimingTests` and removed the old `PosixHarnessToolTests` test and marker.
The CARD-1323 Windows exclusion still named both the deleted capture test and the surviving cutover test, so the marker and policy method sets differed.
S16's recorded verification covered the ported and edited classes and the derived index. The A5 method was outside those short subject selections.
Use the existing `linux-only-policy` removal trigger for `[NativeLinuxOnly(` lines and execute its selected `ReleaseGatePlatformTests/LinuxOnlyWindowsExclusionPolicy` row on the combined tree.

## Group 134 · C1210 roster closure

The same S16 commit removed the capture test's runtime gate. The baseline TRX had 252 source identities and 253 roster entries, with only `Capture_identity_resolves_git_sh_instead_of_hard_coded_bin_sh` on the roster side.
S16's short verification omitted the portable closure method. The existing `native-linux-gate-roster` path trigger does not include `PosixHarnessToolTests.cs`.
Route additions and removals of `NativeLinuxGate.Require` or `C1008HostFixture.RequireNativeLinux` in `tests/Antiphon.Tests/**/*.cs` to `WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates`.
The [g132 retrospective](2026-11-10-why-missed-g132-windowsunitplatformadmission.md) describes the same coverage gap for gate additions.

## Gate 9 · Retirement audit and CRLF checkouts

The audit fixtures seeded plain LF content. Their linked-checkout cases exercised Git directory layout with the same bytes in the worktree and index.
The Windows launcher added an indexed LF blob with `text eol=crlf`, so a real Git checkout on Linux first exposed the false dirty result on the rollout volume.
For future checkout conversions, commit the attribute and let Git create the linked checkout. Check the byte difference and require Ready for the clean checkout and refusal for both a content edit and the same conversion on an undeclared path.

## Group 137 · Merge-subset kind lists

The [read-only Git fixture sweep](2026-11-22-why-missed-git-fixture-readonly-delete.md) (Debug `62b60ff2`) landed as `fd1a0c831415b5d76b42bb949311ecf75849cdf9` and added `test-fixture-cleanup` for `tests/**/*.cs`. Both historical ranges match that glob.
The sweep's short pre-land scope centered on fixture teardown and its guard. Merge runs the subject's narrow witness rows, which left the Unit `MergeHighRadiusSubsetTests` outside the selection until Windows Full.
When `tests/impacted-classes.json` changes, run the two historical range methods in `MergeHighRadiusSubsetTests` and the two historical kind-list methods in `ImpactedClassesSelectorTests`. The same coverage gap caused the [group 135 failure](2026-11-16-why-missed-g135-merge-subset-kinds.md).

## Group 145 · Git fixture cleanup

Nightly S19 (`cad15e980841e1713f645d19f354227832303761`) landed after the fixture cleanup sweep and added a raw recursive delete of the fixture's `.git` directory in `NightlyRunTests.C487_G002`.
Short Merge replays the subject's own witnesses, which left the fixture cleanup guard outside S19's selection on the combined tree.
Apply the existing `test-fixture-cleanup` trigger for `tests/**/*.cs` during short Merge and run `/*/*/TestFixtureCleanupGuardTests/*` on the combined tree before landing matching changes.

## Group 146 · Rolling recycle Git audit fixture

CARD-2043 (`c3781c45acef1e7a15b8e0e6bd3ed41b0afb285b`) raised `GIT_CONFIG_COUNT` from 2 to 7 to disable hooks, fsmonitor, user attributes, external diffs, and submodule recursion during retirement audits.
Short Merge selected the CARD-2043 dry-run preview method. The 40 cases of `C1594_Antiphon_evidence_gitdir` were outside that selection and shared a fixture that still expected 2.
Include this method in the short witness selection when changing the audit helper's Git environment.

## Group 138 · Live-gate disposition

`LiveLaneInventoryTests.EveryLiveGateHasDisposition` reported one unmapped gate after the Nightly S15 port (`75d96a8c7`). The scanner sees the `ANTIPHON_HEADED_TESTS` literal in `NightlyTestsRunTests.C487_G044`. The test supplies the value in a fixture environment and checks that the launcher clears it before starting the child. It runs without a headed opt-in, so the scanner hit is a false positive.
The data-only repair adds the generator's `diagnostic` disposition to `tests/live-lanes.json`. Short verification for changes that introduce a recognized gate shape should run `EveryLiveGateHasDisposition` to catch catalog drift before Windows Full.
Scanner changes from `65aae6b4` and `2920c586` are deferred. Follow-up scanner coverage needs to recognize a real lookup with the `target` named argument first and ignore environment lookup text printed as output.
