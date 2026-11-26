# CARD-2087 operator status verification, round 2

Amendment to [CARD-2087 operator status verification](2026-11-26-card-2087-operator-status.md) for the Review round 1 defects (Review task 1a6c7132 at `914050c1f`). The full task base stays e29c634eee5d972daa76461c85465a1ad9e2c51e; this round's diff starts at 914050c1f611e78ed472504940928ac3d1e63483.

- V-4 (D-1) verifies a live `*.trx` whose root is not a TeamTest `TestRun` is reported unverified, is not counted finished, and does not abort the command.
- V-5 (D-2) verifies unknown remaining chunks are named in an UNVERIFIED line and the projection is marked `lowerBound`, and that previous-run discovery skips a newer stopped attempt whose TRX files do not cover its own `dispatch-schedule.json`.
- V-6 (D-3) verifies the `--json` key set (top level, `master`, `rc`, `rc.eta`) is identical on a healthy observation and when every API read fails and no rc run is found; the shape is documented in the status-update skill.
- V-7 (D-4) verifies release-layout discovery against decoy state and supervisor records whose run id shares the live run's 8-hex prefix and carries a different heartbeat.
- V-8 (D-5) verifies a hung attention read costs only its own short deadline, task details are read concurrently (a two-party rendezvous answers only when both are in flight), and the whole call is bounded by the status budget.
- R-1 to R-3 from the base plan are rerun at the round-2 tip (status classes, guard union with PlanCheckpointClassSelectionGuardTests, the `nightly-live-lane` impact row). R-4 (instruction readers) is not rerun: round 2 does not touch `AGENTS.md`.

The red row runs at S3 (the witness commit) and must fail each D-n witness at its own assertion; the D-4 decoy method is green there because the defect is in the test's power, not the product. Its red belongs to PC-6 in SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c2087-r2red/` | r2-red | `/*/*/OperatorStatusTests/(Exact_eta_matches*)\|(Stopped_partial*)\|(Live_trx_without*)\|(Json_shape_keeps*)\|(Slow_sources_are*)` | V-4, V-5, V-6, V-8 | 5 fail at their D-n assertions before the fix | 5 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | S4 | `tests/Antiphon.Tests -> bin-c2087-r2/` | r2-status | `/*/*/(OperatorStatusTests*)\|(StatusSnapshotScriptTests*)\|(RegisteredCallerScriptTests*)/*` | V-4, V-5, V-6, V-7, V-8, R-1 | all listed pass | 15 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | S4 | CP-7 | r2-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-2 | all listed pass | 53 | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-9 | S4 | CP-7 | r2-impact | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)/*` | R-3 | all listed pass | 2 | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
