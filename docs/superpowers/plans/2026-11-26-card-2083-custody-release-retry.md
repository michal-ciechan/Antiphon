# CARD-2083 custody release retry

Persist the explicit Linux custody seal that was withheld while its host was present as one pending slot-release intent per runner, session and generation. Retry dead-host recovery for the original binding through reconciliation. Require the prior seal and keep the runner's host and namespace checks in control of the cgroup kill. A drained dead-host release remains Unknown. Snapshot removal requires a valid custody receipt. Use a two-minute interval per intent and the existing five-minute Attention threshold. Include the existing audited close route in the Attention evidence.

| ID | Acceptance |
| --- | --- |
| V-1 | After the host exits, reconciliation recovers the exact sealed binding, settles the intent on drained recovery and leaves the execution receipt absent. Native Linux uses the real phone-home dispatcher and runner to verify refusal before a seal and while the host lives, then verifies recovery of the original cgroup after host exit. |
| V-2 | A persistent host keeps one row through repeated observations, exposes the close route in Attention, and stops getting retries after operator closure. The retry cadence survives overlapping passes. Older peers leave the intent pending. |
| R-1 | Existing verification cleanup, deferred generation kills, operator release and the ghost producer retain their behavior. Policy, class selection and layering guards are required. |
| R-2 | Pending retries survive incident pruning. No schema change. Evidence-diff and merge-tree are required. |

The closed selection shares one server test build for the green integrations and guards and one runner test build for native custody and adjacent controls. The native Linux witness constructs the production SessionRunnerRuntime with Linux custody qualified and uses the installed helpers in the fixture's private custody namespace. The estimates below total 28 minutes across both source groups. All affected classes are bounded. The isolated operator check uses the real list and close routes within CP-2, with the fixture's own token.

| PC | Exact witness | Deliberate mutation |
| --- | --- | --- |
| PC-1 | VerificationCleanupServiceTests/C2083_Late_host_exit_is_recovered_without_another_cleanup | Drop the custody-intent retry branch in reconciliation. |
| PC-2 | VerificationCleanupServiceTests/C2083_Live_host_keeps_one_intent_until_operator_close | Insert a fresh intent for each observation of the persistent host. |
| PC-3 | LinuxRunnerCustodyLifecycleTests/C2083_Recovery_only_requires_a_prior_seal_and_waits_for_the_host_to_exit | Route the recovery-only request through the general live-producer seal. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
| --- | --- | --- | --- | --- | --- | --- | ---: | ---: | --- |
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c2083-red/ | pre-fix witnesses | `/*/*/VerificationCleanupServiceTests/(C2083_Late_host_exit_is_recovered_without_another_cleanup*)\|(C2083_Live_host_keeps_one_intent_until_operator_close*)` | V-1, V-2 red | Two intended assertion failures | 2 | 8 | true |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c2083-green/ | custody and adjacent consumers | `/*/*/(VerificationCleanupServiceTests*)\|(RunnerSlotEndpointTests*)\|(PhoneHomeDeferredKillTests*)\|(C2074IncidentCapIntentTests*)\|(GhostSeatReleaseTests*)/*` | V-1, V-2, R-1, R-2 | 0 failed, 0 skipped | 20 | 9 | true |
| CP-3 | S2 | CP-2 | guards and scheduler registration | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)\|(RunnerSlotRulesTests*)/*` | V-2 local schedule, R-1 guards | 0 failed, 0 skipped | 58 | 4 | true |
| CP-4 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c2083-linux/ | native custody | `/*/*/LinuxRunnerCustodyLifecycleTests/*` | V-1 native Linux | 7 passed | 7 | 3 | true |
| CP-5 | S2 | CP-4 | runner controls and impacted slot custody | `/*/*/(RunnerCustodyReleaseTests*)\|(PhoneHomeCommandDispatcherTests*)\|(C0824WorkspaceSlotCustodyTests*)/*` | R-1 runner and workspace mutation lock | 0 failed, 0 skipped | 20 | 4 | true |
