# Phone-home deadlock repair, round 2

Round 2 starts at `d2c192f3dfe3c74fbce53d608b6547d635942d93`. The full repair starts at `8195ad899a2481f4a4c992d036ab401f41d439cf`. Review dd57df06 reproduced replacement recovery waiting for the old transport and observed a one-hour Retry-After reaching the runner's timer.

## Acceptance and regression coverage

- V-1. An open replacement publishes its epoch and notifies eligibility observers, answers the recovery List, and confirms a queued prompt from a complete UserPrompt on that epoch while the previous close remains held. A late old Disconnect preserves the live connection and dispatch eligibility.
- V-2. The runner bounds Retry-After delta-seconds, HTTP-date and total reconnect delay to 30 seconds. It preserves the one-second busy hint and equal jitter, ignores a malformed hint, and delays the first retry after a normal close. The clock inspects the actual timer.
- V-3. Shutdown drains superseded socket cleanup. Cleanup faults are observed and logged.
- R-1. Registration remains bounded at the directory gate and does not mint a ticket while busy. Existing eligibility observer notifications remain correct.
- R-2. Check the prior HostLoad ingest timeout on the narrow scope and report the original batch duration and available load evidence. Apply both R5 checks before changing a budget.
- R-3. Run the policy, checkpoint selection, mechanical, platform and layering guards and their companion inventory check.
- R-4. The full repair range includes the server's phone-home registration endpoint. Run the contract snapshots for that glob match.

The changed behavior is shared across runner platforms. The narrow round-2 selection covers the directory, observer, queue and runner reconnection classes. The full-range path-glob matches add the wire contracts, test-lane census and fixture cleanup guard.

## Positive controls for SourceLanding Mutation

See the [unbound candidates](../mutations/2026-11-27-phone-home-deadlock-round-2-candidate.md) for the four controls and their method filters.

### Cost

The prior Final took 31m54s, including four serial builds. The red pair has separate server and runner builds. Final shares the server output across the replacement and guard rows and builds the runner and contract projects once each. Initial estimates use the prior build receipts and provisional narrowed-row timings. Queue admission and execution are measured separately.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-ph2-red-server-retry/` | server-red | `/*/*/PhoneHomeQueuedTurnTests/Open_replacement_recovers_and_confirms_queued_prompt_while_old_socket_closes` | V-1 | One failure before the repair | 1 | 11 |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-ph2-red-runner/` | runner-red | `/*/*/PhoneHomeConnectionServiceTests/Oversized_retry_after_arms_a_timer_of_at_most_thirty_seconds` | V-2 | Both hint arms fail before the repair | 2 | 2 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-ph2-server/` | replacement | `/*/*/(PhoneHomeQueuedTurnTests*)\|(PhoneHomeDirectoryTests*)\|(RunnerEligibilityObserverTests*)\|(C1744G2HostLoadKeyTests*)/*` | V-1, V-3, R-1, R-2 | All listed, 0 failed | 36 | 12 |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-ph2-runner/` | reconnect | `/*/*/(PhoneHomeConnectionServiceTests*)\|(Card1619RunnerExitTests*)/*` | V-2 | All listed, 0 failed | 38 | 5 |
| CP-5 | S2 | CP-3 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-3 | All listed, 0 failed | 87 | 2 |
| CP-6 | S2 | CP-3 | guard-companion | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-3 | Both methods pass | 2 | 1 |
| CP-7 | S2 | `tests/Antiphon.E2E -> bin-ph2-contracts/` | contracts | `/*/*/ContractSnapshotTests/*` | R-4 | All listed, 0 failed | 5 | 4 |
