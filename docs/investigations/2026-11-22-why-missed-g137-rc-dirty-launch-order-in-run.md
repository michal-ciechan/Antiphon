# Why g137 missed the stopped-seed contract drift

Windows Full run `c9816b02` on candidate `rc-2026-12-01-ac8339c754a8` reported six `RcDirtyLaunchOrderTests` failures in `antiphon-all-group-137.trx`. The caller supplied the failure messages from the desktop TRX. Each named `full-seed-function-remains` as its only failed assertion.

## Introducing commit and card

W2 nightly C# port S13, `b1d3930516832bd4267302e61f7ab5497532f3ff`, changed the return type of `DispatchSchedule.StoppedSeedOrder(object[], object)` from `PSObject` to `object` to support both PowerShell and plain callers. The method stayed public and static. The CARD-1836 dirty planner's off-switch contract checked for the literal `public static PSObject StoppedSeedOrder(`. Its in-process port `1861f1a25d7a0c4ef6331f75dc9135fe5928a6b4` carried the literal over from PowerShell.

All six tests call `AssertOffSwitch`, which read the tracked `DispatchSchedule.cs` from the repository above `AppContext.BaseDirectory`. `Contract` formats the `DO <method>.contract` message from the test instance's error list. The repair checks for the compiled public static method with the existing input types. The separate assertion that the dirty-order code does not reference the Full seed planner remains in place.

## Approving reviews and selection

The S13 plan lists `RcFailedFirstExecutionTests`, `RcFailedFirstPlannerTests` and `RcStoppedRunSeedTests`. The introducing commit records a 12-case parity comparison that included stopped-run seeding. The available records do not identify an approving Review, its Full or Interim scope, or a receipt for its actual test selection. [W2 nightly port plan](../superpowers/plans/2026-11-21-w2-nightly-port-slices.md)

## Outside the selection

`RcDirtyLaunchOrderTests` is an Integration class under `tests/Antiphon.Tests/Scripts`. It was absent from the S13 selection even though the pre-S13 derived index already mapped `DispatchSchedule.cs` to this class. The earlier Windows Full run `3528e6b1` used a pre-S13 source revision with the `PSObject` signature. The `06bca216` native replay on master `e3806b6278227b5acb364c6cd530da45e260a031` covered four `RcDirtyOrderCSharpTests` methods from g137, each three times. That replay's source already had the same `DispatchSchedule.cs` and `RcDirtyLaunchOrderTests.cs` as the later candidate. The different class selection accounts for the apparent standalone pass. [Native Windows replay](2026-11-22-windows-g71-g137-native-recheck.md)

On Linux at `ac8339c754a84ab53ca07708c091d4e79bd1fb5b`, one leased build produced the assembly for two serial checkpoint rows with filter `/*/*/RcDirtyLaunchOrderTests/*`, `ANTIPHON_LINUX_COMPILER_POLICY=0` and `--timeout 570s`. Each fresh TRX recorded 0 passed, 6 failed and 0 skipped with the same assertion as Windows. The first run cleared the qualification variables. The second set the supervisor's `ANTIPHON_QUALIFICATION_RUN_ID`, the chunk runner's five `ANTIPHON_TEST_PHASE_*` variables and its `C487_INVOCATION` and `C487_RUN_DIR` labels using local evidence paths. The seed path, digest, cross-candidate acknowledgement and source candidate are nightly arguments consumed upstream of the test host. The failure reproduced on the committed master tree in both environments.

## Cheapest pre-land check

`scripts/lib/Antiphon.Nightly/DispatchSchedule.cs` → `/*/*/RcDirtyLaunchOrderTests/*`. Include the existing CARD-1782 derived edge when selecting a port slice that changes the dispatch schedule. The six Integration tests expose this contract drift in an isolated Linux class run.

The native rerun is owed on the next integrated Windows candidate after the current native lock is released. From its built checkout, run `pwsh -NoProfile -File scripts/build-slot.ps1 -Label g137-dirty-launch-order -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/*/RcDirtyLaunchOrderTests/*' --timeout 570s --report-trx` and require six passes.

## Exemption

None. The earlier g137 note covers separate Git cleanup, process resolution and timing failures. [Earlier g137 investigation](2026-11-21-why-missed-rc-run-3528e6b1-group-137.md)
