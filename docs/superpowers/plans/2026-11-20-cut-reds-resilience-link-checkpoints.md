# Cut-reds Resilience link checkpoints

Debug task `6ec151de`. Start `c87e1bf17d2caebe4d626a6551145aa4d968b3aa` on `feat/card-task-300f4dc1`. Branch `feat/card-task-6ec151de`. No master merge.

## Red

`dotnet build tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj` at `c87e1bf17d2caebe4d626a6551145aa4d968b3aa`, slot lease `725c671c-1cde-4b91-83ec-eb3efc20425a`, waited 0s, maxcpucount 6, load `3.43 2.99 2.87`. `ANTIPHON_LINUX_COMPILER_POLICY=0`. No `LINUX COMPILER` line. Exit 1. 2 errors:

```
tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs(800,65): error CS0103: The name 'RequestedModelAliasSchema' does not exist in the current context [tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj]
tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs(812,65): error CS0103: The name 'RequestedModelAliasSchema' does not exist in the current context [tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj]
```

`Antiphon.Resilience.Tests` link-includes `TestDbFixtureLifecycle.cs` and does not link `RequestedModelAliasSchema.cs`. No other csproj link-includes `TestDbFixtureLifecycle.cs` or `MigrationTestDatabase.cs`. `RequestedModelAliasSchema` is referenced from the lifecycle file only (the migration helper mentions it in a comment). `Antiphon.Tests` compiles both files through its own directory.

## Verification design

- V-1: Resilience.Tests builds with 0 errors after the link.
- V-2: three Resilience tests that enter `TestDbFixture` (assembly hook calls `TestDbFixture.InitializeAsync`, which uses the linked lifecycle).
- V-3: `C0824RefOnlySnapshotMigrationTests` and `C1514HostLoadMigrationTests` stay green. Argument rows count separately: C0824 is 4, C1514 is 11 (2 methods plus 6 and 3 arguments), Min 15.
- R-1: Linux guard union, including `C1729LayeringGuardTests` at `VIOLATIONS 0 LISTED 162`. `WindowsUnitPlatformAdmissionTests` is not in this row.
- R-2: `scripts/audit-land-verification-graph.ps1 -Check` exits 0. A mismatch caused by this csproj is refreshed with `scripts/land-graph-land-refresh.ps1 -Base` set to the commit whose definition fingerprint equals the recorded digest. A bare `-Update` is not used.
- R-3: derived-impact index, when it changes, is its own last commit. The driver is built first through the build slot. The script runs twice: changed, then unchanged.
- R-4: nightly pin inputs are not edited. Recompute `-PrintInputDigest` and `Read-NightlyExecutionPolicy` and report them. Reseal only if a protected input changes.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | fix | `tests/Antiphon.Resilience.Tests -> bin-cutreds/` | resilience-smoke | `/*/*/DatabaseReadResilienceTests/(Transient_read_fault_succeeds_on_a_new_context_against_postgres*)\|(Ef_wrapper_around_a_transient_postgres_error_is_retried*)\|(Syntax_and_constraint_faults_run_once*)` | V-1, V-2 | 3 passed, 0 failed | 3 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | fix | `tests/Antiphon.Tests -> bin-cutreds-tests/` | cut-reds-classes | `/*/*/(C0824RefOnlySnapshotMigrationTests*)\|(C1514HostLoadMigrationTests*)/*` | V-3 | all listed, 0 failed | 15 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | fix | CP-2 | linux-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | all listed, 0 failed | 1 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | fix | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-2 | exit 0 | n/a | 3 | true | n/a |
