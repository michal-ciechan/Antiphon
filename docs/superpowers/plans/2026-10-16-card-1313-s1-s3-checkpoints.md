# CARD-1313 S1-S3 execution manifest

Derived from the approved change-aware selection plan, restricted by Code brief 209c77e6 to S1-S3. CP-1 through CP-8 retain the approved filters, build reuse, counts and serial flags. The original runner request refused before building because EstimatedMinutes accepts positive integers only. CP-6/7/8 estimates are represented by its minimum value, 1 minute; no test timeout or assertion is changed. Original estimates remain in the full plan for cost history. Windows/all and S4-S7 rows remain deferred, not passed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1313-selection/` | catalog | `/*/*/TestSelectionCatalogTests/*` | V-1 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 6 | false |
| CP-2 | S1-S3 | `CP-1` | policy | `/*/*/ReleaseGatePolicyTests/(C599_ProfileSchema*)\|(C599_ProfileSuites*)\|(C599_EligibilityCensus*)\|(C599_ExpandedCoverage*)` | V-1,R-1 | exactly 4 outer executions; named roster; 0 failed/skipped | 4 | 1 | false |
| CP-3 | S1-S3 | `CP-1` | two-sha | `/*/*/TestSelectionTests/*` | V-2 | exactly 8 outer executions; named roster; 0 failed/skipped | 8 | 2 | false |
| CP-4 | S1-S3 | `CP-1` | dependencies | `/*/*/TestSelectionDependencyTests/*` | V-3 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 1 | false |
| CP-5 | S1-S3 | `CP-1` | exact-execution | `/*/*/TestSelectionExecutionTests/*` | V-4,R-1 | exactly 3 outer executions; named roster; 0 failed/skipped | 3 | 2 | true |
| CP-6 | S1-S3 | `CP-1` | discovery-regression | `/*/*/NightlyDiscoveryParserTests/C1044_DiscoveryReconciliation` | R-1 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 1 | false |
| CP-7 | S1-S3 | `CP-1` | terminal-regression | `/*/*/NightlyExecutionDiagnosticTests/D13_ExecutionGuards` | R-1 | exactly 1 outer executions; named roster; 0 failed/skipped | 1 | 1 | false |
| CP-8 | S1-S3 | `CP-1` | core-limiter | `/*/Antiphon.Tests/ProcessSpawnLimitTests/(Caps_concurrent_process_spawning_tests_at_one*)\|(Process_spawning_classes_carry_the_limiter*)` | R-1 | exactly 2 outer executions; named roster; 0 failed/skipped | 2 | 1 | false |
