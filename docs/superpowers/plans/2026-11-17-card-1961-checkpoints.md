# CARD-1961 checkpoint extract (Code task fae5c49d)

This is the CARD-1961 plan extract that the reviewed
[CARD-1960/1961 workspace-slot fixes plan](2026-11-17-card-1960-1961-workspace-slot-fixes-plan.md)
asks for ("rows B* belong to the CARD-1961 Code task, each run in its own plan extract").
The clean plan file is not edited.

The checkpoint ids are renamed because `scripts/check-os-witnesses.ps1` reads `CP-<digits>` only.
CP-B1 is CP-1, CP-B2 is CP-2, CP-B3 is CP-3. Every other cell of those rows is the plan's, except
CP-1 `EstimatedMinutes`, which is 15 so the first row's cold build has the 45-minute derived deadline.
CP-4 and CP-5 are the task brief's remaining guard classes and the two named selector/timing classes.
They reuse CP-3's build. Positive controls stay pending; mutation is waived (operator 2026-11-12).

## Verification design

W-B1 and W-B2 are methods on `C0824WorkspaceSlotResetTests` (CP-1), with V-2, V-27 and the other reset rows.
CP-2 is the CARD-1782 pool neighbourhood. CP-3 is the plan's nine-class guard union. CP-4 adds
`DerivedImpactIndexTests` and `WindowsUnitPlatformAdmissionTests` (the eleven-class remainder; the
Linux skip of `C1210_Windows_linux_only_outcomes_are_exact` is the roster skip the receipt validator accepts).
CP-5 is `TestTimingOperationLabelTests` and `ImpactedClassesSelectorTests`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | B1 | tests/Antiphon.SessionRunner.Tests -> bin-c1961-reset/ | linux-slot-reset | `/*/*/C0824WorkspaceSlotResetTests/*` | W-B1, W-B2, V-2, V-27 and the other reset rows | 12 methods, 0 failed/skipped | 12 | 15 | true |
| CP-2 | B1 | tests/Antiphon.SessionRunner.Tests -> bin-c1961-impact/ | linux-slot-impact | `/*/*/(C0824WorkspaceSlotCustodyTests*)\|(C0824WorkspaceSlotRehomeTests*)\|(C0824WorkspaceSlotColdResetTests*)\|(C0824WorkspaceSlotProcessScanTests*)\|(C1817RunnerRepositoryMaintenanceTests*)/*` | CARD-1782 pool rows, plus dirt-before-reset, quarantine release and cold reset unchanged | every named class, 0 failed | 69 | 15 | true |
| CP-3 | B1 | tests/Antiphon.Tests -> bin-c1961-guards/ | portable-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | NativeLinux policy for the new Linux-only methods, and categories | 0 failed | 78 | 12 | true |
| CP-4 | B1 | CP-3 | portable-guards-remainder | `/*/*/(DerivedImpactIndexTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | eleven-class remainder: derived index and the Windows-admission class whose Linux skip is rostered | every named class, 0 failed | 30 | 12 | true |
| CP-5 | B1 | CP-3 | portable-named-guards | `/*/*/(TestTimingOperationLabelTests*)\|(ImpactedClassesSelectorTests*)/*` | task-brief timing and impacted-class selector rows | every named class, 0 failed | 30 | 12 | true |

Notes on the counts (the plan's, renumbered):

- CP-1 Min is 12: the ten existing reset methods plus W-B1 and W-B2.
- CP-2 Min is 69: Custody 19, Rehome 18, ColdReset 12, ProcessScan 3 and C1817 17.
- CP-3 Min is 78, the documented nine-class floor.
- CP-4 and CP-5 Min values are floors under the counted methods (derived 27, Windows admission 6, timing 2, selector 31).
