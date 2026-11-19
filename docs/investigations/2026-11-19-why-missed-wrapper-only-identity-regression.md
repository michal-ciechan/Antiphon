# Why missed: wrapper-only census identity refusal

`LiveLaneRunnerTests.WrapperOnlyPaidManifest` fails on master `c83d14d4858a66e41e2c51c4631869db99d0a2c7`.
The harness reaches `full-inputs`, then the next isolated census throws:

```
nightly-core-identity-mismatch: required-identity-unavailable; start a fresh pwsh
```

Reproduced on that tip through `scripts/build-slot.ps1` (lease `57da5920-3928-4188-91c8-e9443843bc0a`,
waited 46s, held 11s, 1-minute load 11.43). Fifteen `C1545` rows passed. The failure is
`C1545 WrapperOnlyPaidManifest/harness`, `Exception calling "Invoke"`, at
`scripts/test-live-lane-run.ps1` `Read-Census` (`$ps.Invoke()`, line 268 before the fix).
The other three `LiveLaneRunnerTests` methods do not call `Read-Census`.

This is a test-fixture miss, not a live-lane launch refusal. `valid-context` through
`native-disabled` passed in the same process before the census. A real nightly or live-lane
launch dots the full checkout, which can name a build identity. The paid wrapper is not a
separate entry point that refuses at startup.

## Exact condition

`Read-Census` runs the pinned scripts in a new in-process runspace. `Copy-ProtectedInputs`
copies `scripts/lib/Antiphon.Nightly/NightlyCore.cs` and does not copy
`src/Antiphon.IO.WriteBehind`. `Get-NightlyCoreBuildIdentity` returns null at
`scripts/lib/nightly-core.ps1:24` because that directory does not exist under the copy.

The first census is the real checkout. It builds and `Add-Type`s `Antiphon.Nightly.dll`
into the process. The copy's runspace still sees `Antiphon.Nightly.TestPhaseJson`.
`Test-NightlyCoreAvailable` then calls `Assert-NightlyLoadedCoreIdentity`
(`nightly-core.ps1:196-198`). Line 236 throws because the identity is null:

```
nightly-core-identity-mismatch: required-identity-unavailable; start a fresh pwsh
```

Before the assert, a loaded `TestPhaseJson` was accepted with no identity check
(`6ab139c48662677c3d99b6c129bf1551e1cfe193`, `Test-NightlyCoreAvailable`). That is the
path the pinned reader documents: the copy supplies the PowerShell, and the process keeps
the checkout core.

## Introducing commit

`2a9ffa3446f0352f4a03ec259986694f6b700540` (2026-11-19 18:31:49Z),
`fix(nightly): surface deferred write failures, publish the journal before ack, refuse a mismatched core`.
It added `Assert-NightlyLoadedCoreIdentity` and the null-identity throw. The parent is
`6ab139c48662677c3d99b6c129bf1551e1cfe193`.

That commit is an ancestor of both bases the Astra reviews treated as green:
`3f24857e8251ba0eb735f4b6dd1f4c963f3cc05d` and `5f0804e425384f8b03f88b833fbdad0e93f966d3`.
`acb578f395905b5164f57e6a8a5c590b0e32ba20` only appended `; start a fresh pwsh` to the same
throw and re-checks a runspace whose core flag is already true. The failing runspace is new,
so its flag starts unset. The throw that fires is the loaded-type path from `2a9ffa344`.

## What the reviews ran

Astra B's closed lists do not name `LiveLaneRunnerTests`:

- `docs/superpowers/plans/2026-11-19-astra-fixes-b-checkpoints.md` runs `AstraFixesBTests`,
  `NightlyCoreTests`, `NightlyScriptsTests`, and the guard union.
- `docs/superpowers/plans/2026-11-19-astra-b-advisories-checkpoints.md` does the same.
  Its base is `3f24857e`.

`LiveLaneRunnerTests` is not in the guard union
(`TestClassificationPolicyTests`, `TestClassificationGuardTests`, `SlowTestRegistryMergeTests`,
`SlowTestTripwireTests`, `NativeLinuxPolicyWitnessTests`, `DerivedImpactIndexTests`,
`MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`,
`TestLaneCategoryGuardTests`, `WindowsUnitPlatformAdmissionTests`). Those rows prove the
loader refuses a stale or partial core when the script root is the full checkout. They do
not dot a protected-input copy.

Astra A's plan does name the class (`docs/superpowers/plans/2026-11-19-astra-fixes-a-checkpoints.md`
CP-6, `/*/*/LiveLaneRunnerTests/*`). That plan's code is `ea9e076e` on base `d86d0f39`,
before `2a9ffa344`.

## Selector that would have caught it

A row `/*/*/LiveLaneRunnerTests/WrapperOnlyPaidManifest`, or the class
`/*/*/LiveLaneRunnerTests/*`, on the commit that changed
`Assert-NightlyLoadedCoreIdentity`. The class is an integration consumer of the loader,
not a classification guard, so it stays off the nine-class guard union.
`WindowsUnitPlatformAdmissionTests` stays off the Linux row: `C1210_Windows_linux_only_outcomes_are_exact`
skips here, and a skip is not a pass.

## Fix

`0ecf8ba031b0652f471002b975cecf159bcacedd` re-dots the checkout's `nightly-core.ps1` after
the pinned scripts in `Read-Census`. The pinned census functions stay. The loader's
`$PSScriptRoot` is the checkout, so the already-loaded core matches the cache identity.
The same witness then passed 21/21 (lease `2fd0e655-b965-4936-98c7-8b83e364f94c`, held 16s).

Astra B is unchanged. A stale or partial core on a root that can name an identity is still
refused. A complete matching core is still accepted. The refusal text is unchanged.
Protected inputs were not edited, so the census pin is not resealed.
