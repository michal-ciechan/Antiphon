# Phone-home deadlock repair, round 2

Round 2 starts at `d2c192f3dfe3c74fbce53d608b6547d635942d93`. The full repair starts at `8195ad899a2481f4a4c992d036ab401f41d439cf`. Review dd57df06 reproduced replacement recovery waiting for the old transport and observed a one-hour Retry-After reaching the runner's timer.

## Acceptance and regression coverage

- V-1. An open replacement publishes its epoch and notifies eligibility observers, answers the recovery List, and confirms a queued prompt from a complete UserPrompt on that epoch while the previous close remains held. A late old Disconnect preserves the live connection and dispatch eligibility.
- V-2. The runner bounds Retry-After delta-seconds, HTTP-date and total reconnect delay to 30 seconds. It preserves the one-second busy hint and equal jitter, ignores a malformed hint, and delays the first retry after a normal close. The clock inspects the actual timer.
- V-3. Shutdown drains superseded socket cleanup. Cleanup faults are observed and logged.
- R-1. Registration remains bounded at the directory gate and does not mint a ticket while busy. Existing eligibility observer notifications remain correct.
- R-2. Check the prior HostLoad ingest timeout on the narrow scope and report the original batch duration and available load evidence. Apply both R5 checks before changing a budget.
- R-3. Run the policy, checkpoint selection, mechanical, platform and layering guards and their companion inventory check.

The changed behavior is shared across runner platforms. Scope is bounded by the directory, observer, queue, and runner reconnection classes and the affected classes named in the checkpoint rows. No full-assembly scope is required.

## Positive controls for SourceLanding Mutation

| PC | Change | Exact witness |
| --- | --- | --- |
| PC-1 | Block synchronously on superseded disposal while holding the directory gate. | `/*/*/PhoneHomeQueuedTurnTests/Open_replacement_recovers_and_confirms_queued_prompt_while_old_socket_closes` |
| PC-2 | Await superseded cleanup before replacement notification. | `/*/*/PhoneHomeQueuedTurnTests/Open_replacement_recovers_and_confirms_queued_prompt_while_old_socket_closes` |
| PC-3a | Accept oversized delta-seconds without the retry ceiling. | `/*/*/PhoneHomeConnectionServiceTests/Oversized_retry_after_arms_a_timer_of_at_most_thirty_seconds` |
| PC-3b | Accept an oversized HTTP-date without the retry ceiling. | `/*/*/PhoneHomeConnectionServiceTests/Oversized_retry_after_arms_a_timer_of_at_most_thirty_seconds` |
| PC-4 | Drop the late Disconnect live-identity check. | `/*/*/PhoneHomeQueuedTurnTests/Open_replacement_recovers_and_confirms_queued_prompt_while_old_socket_closes` |

### Cost

The prior Final took 31m54s, including four serial builds. This round budgets two assembly builds for the red pair and two for the Final. Initial estimates use the prior 502s server and 97s runner build receipts and provisional narrowed-row timings. Keep queue wait separate from execution.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-ph2-red-server/` | server-red | `/*/*/PhoneHomeQueuedTurnTests/Open_replacement_recovers_and_confirms_queued_prompt_while_old_socket_closes` | V-1 | One failure before the repair | 1 | 9 |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-ph2-red-runner/` | runner-red | `/*/*/PhoneHomeConnectionServiceTests/Oversized_retry_after_arms_a_timer_of_at_most_thirty_seconds` | V-2 | Both hint arms fail before the repair | 2 | 2 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-ph2-server/` | replacement | `/*/*/(PhoneHomeQueuedTurnTests*)\|(PhoneHomeDirectoryTests*)\|(RunnerEligibilityObserverTests*)\|(C1744G2HostLoadKeyTests*)/*` | V-1, V-3, R-1, R-2 | All listed, 0 failed | 4 | 10 |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-ph2-runner/` | reconnect | `/*/*/(PhoneHomeConnectionServiceTests*)\|(Card1619RunnerExitTests*)/*` | V-2 | All listed, 0 failed | 28 | 3 |
| CP-5 | S2 | CP-3 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-3 | All listed, 0 failed | 87 | 2 |
| CP-6 | S2 | CP-3 | guard-companion | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-3 | Both methods pass | 2 | 1 |
