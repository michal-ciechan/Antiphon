# Stopped-run seed layout verification

Fix round for the stopped-run seed builder. One Antiphon.Tests build covers the
original seed methods, the sibling-timing cases, the failed-first chain methods,
the neighbour classes, and CP-20..CP-23. Session runner, pty host, and agents pty
each get one build for the process-spawn limiter and the Linux policy witness.
No test-execution-policy.json change. Do not run the land-graph -Update.

Synthetic fixtures only. The finished run d47848cc is not read.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-d63-seed1/` | seed-chain-neighbours | `/*/*/(RcStoppedRunSeedTests*)\|(RcFailedFirstPlannerTests*)\|(RcFailedFirstExecutionTests*)\|(RcFailureHistoryTests*)\|(RcFullWatchdogHarnessTests*)\|(C1514LandingSplitBudgetTests*)\|(RcRosterRepairTests*)\|(ScriptHarnessProcessContractTests*)\|(ProcessSpawnLimitTests*)\|(SlowTestTripwireTests*)\|(TestLaneCategoryGuardTests*)\|(TestClassificationPolicyTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | seed CP-1..CP-8, sibling timing, chain replays, neighbours, spawn limit, policy witness | all selected green | 70 | 40 | true | TEMP=/tmp |
| CP-2 | S1 | `CP-1` | admitted-cost | `/*/Antiphon.Tests.Scripts/RcFullWatchdogTests/CapFitsAdmittedPlannedCost` | chain admitted cost | exact method | 1 | 8 | true | TEMP=/tmp |
| CP-3 | S1 | `CP-1` | ledger-bytes | `/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged` | chain ledger bytes | exact method | 1 | 8 | true | TEMP=/tmp |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-d63-runner1/` | runner-witness | `/*/*/(ProcessSpawnLimitTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | runner spawn limit and policy witness | both classes green | 5 | 10 | true | TEMP=/tmp |
| CP-5 | S3 | `tests/Antiphon.PtyHost.Tests -> bin-d63-pty1/` | pty-witness | `/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/*` | pty host policy witness | class green | 3 | 8 | true | TEMP=/tmp |
| CP-6 | S4 | `tests/Antiphon.Agents.Pty.Tests -> bin-d63-agents1/` | agents-witness | `/*/*/(ProcessSpawnLimitTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | agents pty spawn limit and policy witness | both classes green | 5 | 10 | true | TEMP=/tmp |
