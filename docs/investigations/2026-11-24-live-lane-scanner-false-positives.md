# Live-lane scanner false positives (CARD-2056)

The scanner on master treated every mention of `ANTIPHON_HEADED_TESTS` in a test body as a gate. The g138 repair narrowed that to a textual lookup pattern. Review round 2 (`b6bdc5c9`) found that the pattern missed an executable lookup with `target:` first and flagged a diagnostic string that spelled out a named lookup. [Original g138 investigation](2026-11-22-rc-run-79d9e6f9-failures.md)

| Source in the fixture | Required inventory result |
|---|---|
| `Environment.GetEnvironmentVariable(target: EnvironmentVariableTarget.Process, variable: "ANTIPHON_HEADED_TESTS")` | Headed gate |
| `Environment.GetEnvironmentVariable(variable: "ANTIPHON_HEADED_TESTS")` | Headed gate |
| `Console.WriteLine("Environment.GetEnvironmentVariable(variable: ANTIPHON_HEADED_TESTS)")` | Ordinary test |
| A real positional lookup inside a string interpolation that controls a guard | Headed gate |
| A child environment seeded with the headed flag and an executable lookup of another variable | Ordinary test |

The scanner uses C# invocation syntax to identify the `variable:` argument in either position, or the first positional argument. Only a lookup of the headed flag contributes the headed shape to the existing method scan. The syntax reader uses Roslyn 4.8 APIs for compatibility with the PowerShell host that loads the nightly assembly. The catalog drops the two fixture-only entries, `NightlyTestsRunTests.C487_G044` and `ReleaseGateE2EEvidenceTests.C599_EnvironmentIsolation`. [Scanner](../../scripts/lib/Antiphon.Nightly/LiveLaneGateScan.cs) · [Catalog](../../tests/live-lanes.json)

The Linux red run at `75f57ce01dd3cedf1e2b16278b19320b2967cae1` executed two tests. Both failed with extra gates. The named-argument fixture reported `SeedsChildEnvironment`, and the printed-lookup fixture reported both diagnostic methods. Both named-argument guards and the executable interpolation guard appeared in the red output. The scratch class carries `[Category("Unit")]` for the source census. [Witnesses](../../tests/Antiphon.Tests/Scripts/Nightly/LiveLaneGateScanTests.cs)

The focused checkpoint selects both witnesses, `LiveLaneGateScanTests.ScanGates`, `LiveLaneInventoryTests.EveryLiveGateHasDisposition`, and `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration`. The inventory method is the pre-land check for changes that may introduce or remove a gate disposition.
