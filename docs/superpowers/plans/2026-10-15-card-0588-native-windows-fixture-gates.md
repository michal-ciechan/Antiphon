# CARD-0588: native Windows fixtures skip off Windows with a named reason

Card: `pwsh -NoProfile -File scripts/card.ps1 get CARD-0588 -Board Antiphon` (reconciliation
2026-10-13 names the three residual sites). Base: `78e4001788e99ebc04f0590a84db18442f6324c6`.

## Decision per site

| Site | Windows dependency (source) | Verdict |
|---|---|---|
| `tests/Antiphon.Tests/Infrastructure/WorktreeLockDiagnosticsWindowsTests.cs` `NativeSharingAsync` | `server/Infrastructure/Git/WorktreeNativeIO.cs` `Supported => OperatingSystem.IsWindows()`; `WindowsWorktreeDeleteAccessProbe` returns `UnsupportedPlatform` when unsupported | fixture is right to need Windows; skip off Windows |
| `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs` `InitializeAsync` | `ConPtyRedistributable.TryLocate` (doc comment: win-x64 staged) and the staged `fakegrok/fakegrok.exe`; the sibling `FakeGrokContractTests.C467_BusyGatePreservesNativePromptAndTurnEnd` already skips with "requires Windows ConPTY" | skip off Windows; a Linux land-delivery fixture is a separate port |
| `tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs` `Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup` | literal `ReplyInlineMaxChars.ShouldBe(14400)`; `DelegationSettings.CeilingsFor` gives the modern ceiling to `PtyBackend.ModernConPty` and the inbox ceiling otherwise | skip off Windows, gate before the opt-in check |

The product is not changed. Each site keeps its Windows prerequisite assertions after the gate
(`ConPtyRedistributable.TryLocate(...).ShouldBeTrue(why)` in both E2E sites), so a Windows host
with a missing prerequisite still fails red.

## Slices

- S1 (test first): `tests/Shared/NativeWindowsGate.cs` linked into `Antiphon.Tests` and
  `Antiphon.E2E`; `tests/Antiphon.Tests/TestHelpers/NativeWindowsFixtureGateTests.cs`.
  Red manifest: `docs/superpowers/plans/2026-10-15-card-0588-red.md`.
- S2: the three sites call `NativeWindowsGate.Require(NativeWindowsReason)` as their first
  statement, with a `const` reason naming CARD-0588 and the dependency.

## Verification design

- V-1 `C588_Gate_skips_off_windows_with_named_reason`: the gate throws `SkipTestException`
  carrying the literal reason when not Windows and returns on Windows.
- V-2 `C588_Worktree_lock_native_fixture_skips_before_work_off_windows`: off Windows both C443
  methods end in `SkipTestException` with the literal reason (red at S1: `ShouldAssertException`).
- V-3 `C588_Named_fixture_gates_before_work_and_keeps_windows_prerequisites` (one case per site):
  reason const literal, gate as first statement, no `IsWindows()...ShouldBeTrue` call in the
  method, ConPTY prerequisite kept in the E2E sites.
- R-1 adjacent: the C1210 source-gate closure keeps passing with the new `NativeWindowsGate`
  calls in `tests/Antiphon.Tests` (Linux-runnable methods of `WindowsUnitPlatformAdmissionTests`).
- R-2 adjacent: `TestClassificationGuardTests` (lane-xor for the new Unit class) in both
  assemblies; the E2E row is also the compile proof for the two E2E edits.

The class `WorktreeLockDiagnosticsWindowsTests` is not a row: off Windows it now skips, and a
skipped row is rejected by the strict validators; V-2 invokes its methods instead. The two E2E
sites are not executed on Linux (they need a real Program, runner and providers); V-3 covers
their shape.

Positive controls (pending for SourceLanding Mutation, method-scoped):

| PC | Test | Mutation | Expected red |
|---|---|---|---|
| PC-1 | `C588_Gate_skips_off_windows_with_named_reason` | `NativeWindowsGate.Require(string, bool)` returns without throwing when not Windows | `Should.Throw<SkipTestException>` fails |
| PC-2 | `C588_Worktree_lock_native_fixture_skips_before_work_off_windows` (Linux) | restore `OperatingSystem.IsWindows().ShouldBeTrue(...)` in `NativeSharingAsync` | threw `ShouldAssertException`, not skip |
| PC-3 | `C588_Named_fixture_gates_before_work_and_keeps_windows_prerequisites("LandDeliveryFixture")` | move the gate after `ConPtyRedistributable.TryLocate` | `c588-gate-first` |
| PC-4 | same, `("OutputDistillationApplyCanaryTests")` | delete the `ConPtyRedistributable.TryLocate(...).ShouldBeTrue(why)` statement | `c588-windows-prerequisite-kept` |
| PC-5 | same, `("WorktreeLockDiagnosticsWindowsTests")` | change the reason literal | `c588-reason-literal` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c588/` | c588-gates | `/*/*/NativeWindowsFixtureGateTests/*` | V-1, V-2, V-3 | all listed, 0 failed | 5 | 12 |
| CP-3 | S2 | CP-2 | c1210-closure | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform)\|(C1210_Native_linux_entries_guard_before_work)\|(C1210_Roster_closure_matches_source_gates)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes)` | R-1 | all listed, 0 failed | 4 | 2 |
| CP-4 | S2 | CP-2 | tests-classification | `/*/*/TestClassificationGuardTests/*` | R-2 | all listed, 0 failed | 1 | 2 |
| CP-5 | S2 | `tests/Antiphon.E2E -> bin-c588-e2e/` | e2e-classification | `/*/*/TestClassificationGuardTests/*` | R-2 | all listed, 0 failed | 1 | 14 |
