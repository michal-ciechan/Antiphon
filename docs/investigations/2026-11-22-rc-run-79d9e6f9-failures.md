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
