# CARD-0588: three Windows fixtures skip off Windows

Card: `pwsh -NoProfile -File scripts/card.ps1 get CARD-0588 -Board Antiphon`.
Base: `f125755c1648533c473ce46311c307788b4a9b65`.

`tests/Shared/PlatformTaxonomy.cs` is absent on this base, so the skip is
`NativeWindowsGate.Require` throwing `SkipTestException` with a named reason.
The product is unchanged. Each site keeps the Windows prerequisite that follows
the gate.

## Decision per site

| Site | Why it stays Windows-only |
|---|---|
| `WorktreeLockDiagnosticsWindowsTests.NativeSharingAsync` | `WorktreeNativeIO.Supported` is `OperatingSystem.IsWindows()`. The probe's unsupported result is `UnsupportedPlatform`, and the case expects native error 32. |
| `LandDeliveryFixture.InitializeAsync` | `ConPtyRedistributable` stages win-x64 and the fixture requires `fakegrok.exe`. A Linux land-delivery fixture is a separate port. |
| `OutputDistillationApplyCanaryTests.Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup` | The canary pins `ReplyInlineMaxChars` 14400, the `PtyBackend.ModernConPty` ceiling from `DelegationSettings.CeilingsFor`. The gate is the first statement, before the opt-in check. |

`FakeGrokContractTests` already skips and is not touched. CARD-1226 is the mirror case and is not this card.

## Witnesses

`NativeWindowsFixtureGateTests` is the Linux seam. `C588_Gate_skips_off_windows_with_named_reason` injects `isWindows`. `C588_Worktree_lock_native_fixture_skips_before_work_off_windows` calls both native methods and expects the named skip. `C588_Named_fixture_gates_before_work_and_keeps_windows_prerequisites` parses each site and requires the gate as the first statement, a const reason literal, no `IsWindows().ShouldBeTrue`, and the ConPTY prerequisite on the two fixture sites that have one.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests` | c588-gates | `/*/*/NativeWindowsFixtureGateTests/*` | V-1, V-2, V-3 | 5 passed, 0 failed | 5 | 8 |
| CP-2 | S1 | `tests/Antiphon.Tests` | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | guard union | 135 passed, 0 failed | 135 | 15 |
