# CORE.LONGPATHS checkpoints (Code task 016f2cc2)

An rc checkout cloned without `core.longpaths=true` failed
`scripts/test-apphost-main-worktree-guard.ps1` on Windows (rc run a6278259,
`git worktree add` exit 128). The guard now passes `-c core.longpaths=true`
and keeps git's text. `launch-rc.ps1`, the nightly `-NoSync` branch, and
`scripts/qualification/preflight.ps1` refuse `longpaths-not-enabled` unless
local `core.longpaths` is `true`. `release-candidate.ps1` passes
`-c core.longpaths=true` on clone. The nightly sync path is unchanged.

No migration. No positive-control row. Mutation execution is waived
(operator 2026-11-12) and is not discharged. `WindowsUnitPlatformAdmissionTests`
stays off this Linux selection; a Windows skip is not a pass.

## Verification design

- W-1. `scripts/test-longpaths-checkout.ps1`: guard stderr, path arithmetic, launch refusal, `-NoSync` refusal, preflight, clone flag. 19 rows.
- W-2. `LongPathsCheckoutScriptTests` runs that harness.
- W-3. `C1323_PlatformExecution` sets `core.longpaths` on its real `-NoSync` fixture.
- W-4. `C545_ResultLine` and `C1698_RunPrintsToolchainWarning` still pass with the seam that answers `git config --get core.longpaths`.
- W-5. Nine-class guard union, without `WindowsUnitPlatformAdmissionTests`, floor 81.
- W-6. `DerivedImpactIndexTests` alone, floor 27. The regenerate driver is built into the worktree default output before this row.
- W-7. `scripts/audit-land-verification-graph.ps1 -Check`.
- W-8. `scripts/test-live-lane-run.ps1 -PrintInputDigest`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | longpaths | n/a | lp-harness | `pwsh -NoProfile -File scripts/test-longpaths-checkout.ps1` | W-1 | 19 passed, 0 failed, exit 0 | n/a | 15 | true | n/a |
| CP-2 | longpaths | tests/Antiphon.Tests -> bin-lp/ | lp-wrapper | `/*/*/LongPathsCheckoutScriptTests/*` | W-2 | 0 failed, executed 6 | 6 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | longpaths | n/a | lp-c1323 | `pwsh -NoProfile -File scripts/test-rc-platform.ps1 -Case C1323_PlatformExecution` | W-3 | exit 0 | n/a | 15 | true | n/a |
| CP-4 | longpaths | n/a | lp-c545 | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C545_ResultLine` | W-4 | exit 0 | n/a | 12 | true | n/a |
| CP-5 | longpaths | n/a | lp-c1698 | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1698_RunPrintsToolchainWarning` | W-4 | exit 0 | n/a | 12 | true | n/a |
| CP-6 | longpaths | CP-2 | lp-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | W-5 | 0 failed, executed at least 81 | 81 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | longpaths | CP-2 | lp-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | W-6 | 0 failed, executed at least 27 | 27 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | longpaths | n/a | lp-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | W-7 | exit 0 | n/a | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | longpaths | n/a | lp-pins | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` | W-8 | exit 0 | n/a | 5 | true | n/a |
