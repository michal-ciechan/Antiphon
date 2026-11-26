# CARD-2083 custody release retry

Persist the explicit Linux custody seal that was withheld while its host was present as one pending slot-release intent per runner, session and generation. Retry the original binding through reconciliation. Keep the runner's host and namespace checks in control of the cgroup kill. A drained dead-host release remains Unknown and supplies no receipt or snapshot-removal authority. Use a two-minute interval per intent and the existing five-minute Attention threshold. Include the existing audited close route in the Attention evidence.

| ID | Acceptance |
| --- | --- |
| V-1 | After the host exits, reconciliation replays the exact bound seal, settles the intent on drained recovery and leaves the execution receipt absent. Native Linux confirms the real runner withholds while its host lives, then drains the original cgroup after host exit. |
| V-2 | A persistent host keeps one row through repeated observations, exposes the close route in Attention, and stops getting retries after operator closure. No in-cadence RPC, plain session kill or unconditional slot release occurs. |
| R-1 | Existing verification cleanup, deferred generation kills, operator release and the ghost producer retain their behavior. Draft/policy guard results are required. |
| R-2 | Pending retries survive incident pruning. No schema change. Evidence-diff and merge-tree are required. |

The closed selection uses a warm shared server test build for the green integrations and guards. The native Linux witness uses the installed helpers and the fixture's private custody namespace. The ordinary estimates below total 24 minutes across both source groups. No class impact is currently unbounded. Any selector match from the final Git range must be accounted for before expanding to a larger run.

| PC | Exact witness | Deliberate mutation |
| --- | --- | --- |
| PC-1 | VerificationCleanupServiceTests/C2083_Late_host_exit_is_recovered_without_another_cleanup | Drop the custody-intent retry branch in reconciliation. |
| PC-2 | VerificationCleanupServiceTests/C2083_Live_host_keeps_one_intent_until_operator_close | Insert a fresh intent for each observation of the persistent host. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
| --- | --- | --- | --- | --- | --- | --- | ---: | ---: | --- |
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c2083-red/ | pre-fix witnesses | `/*/*/VerificationCleanupServiceTests/(C2083_Late_host_exit_is_recovered_without_another_cleanup*)\|(C2083_Live_host_keeps_one_intent_until_operator_close*)` | V-1, V-2 red | Two intended assertion failures | 2 | 8 | true |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c2083-green/ | custody and adjacent consumers | `/*/*/(VerificationCleanupServiceTests*)\|(RunnerSlotEndpointTests*)\|(PhoneHomeDeferredKillTests*)\|(C2074IncidentCapIntentTests*)\|(GhostSeatReleaseTests*)/*` | V-1, V-2, R-1, R-2 | 0 failed, 0 skipped | 20 | 9 | true |
| CP-3 | S2 | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-1 | 0 failed, 0 skipped | 55 | 4 | true |
| CP-4 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c2083-linux/ | native custody | `/*/*/LinuxRunnerCustodyLifecycleTests/C1614_Lost_host_explicit_release_kills_but_stays_unknown` | V-1 native Linux | 1 passed | 1 | 3 | true |
