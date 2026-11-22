# CARD-1939 boot-prompt watchdog diagnostics

Code task `582a015c-869f-40ad-b0e2-65b576039c29`. Base `2f544fb373d2f2fbca49515fdd3f2f0431e527e8`. Final.
The invariant is that refunding the retry charge cannot erase operator-visible evidence that the task brief was attempted. Store the brief's history in a durable audit summary keyed to its queue row. Save it alongside the attempt stamp and the composer verdict. The watchdog's failure reason and Failed event detail carry the count, first attempt time and last composer-evidence reason. Older rows use their available stamps and verdict.

## Performance budget

S1 adds the red witness with production code at base. S2 qualifies the repair and regenerated index with one shared cold Antiphon.Tests build. The estimates are provisional except for the watchdog class's recorded 227 serial seconds (nightly class costs, 86 historical nodes). Use the task's 570-second foreground wait and shared fixture-owned PostgreSQL. The fake adapter drives normal queue writes and exposes whether Enter was sent. Preserve the checkpoint receipts, fresh TRX and test phase journal under the run root. No unbounded class selection. No native OS branch changes. The Windows-only impacted E2E method is outside authorized Linux execution, and the separate incident's cold Windows witness belongs to the follow-up.

## Verification design

- V-1. Two failed production queue attempts both refund to zero without submitting. Expiry reports two attempts, the first start and the composer failure in the task and event. S1 must fail at the count assertion.
- V-2. A queued brief with zero attempts and no start stamp still reports never attempted in both surfaces. S1 and S2 must pass.
- V-3. Existing watchdog and queue classes exercise unchanged delivery decisions and retry accounting.
- V-4. A legacy Pending row with a zero charge, attempt stamp and composer verdict reports the known minimum and the last recorded timestamp.
- R-1. Run the policy, mechanical contract, platform, layering and derived index guards.
- R-2. Run Linux classes from the matching dispatcher and queue impact rows. The Windows-only selector is `/*/*/AgentTaskLandDeliveryE2ETests/C488_ReviewDeliveryCrashMatrix_Batch4`.
- R-3. Check full-range evidence policy, OS branch inventory, land-graph audit and a conflict-free merge-tree against fetched master.
- PC-1. If commissioned, remove the attempt history from the watchdog's evidence read and run the exact V-1 method. The two-attempt assertion must fail. SourceLanding Mutation owns execution.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1939-base/` | red-witness | `/*/*/AgentTaskDeliveryWatchdogTests/(Refunded_boot_prompt_attempts_are_reported_with_the_first_start_and_composer_failure*)\|(A_boot_prompt_with_no_attempt_still_reports_never_attempted*)` | V-1, V-2 | 1 expected assertion failure and 1 passed | 2 | 8 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1939-final/` | watchdog | `/*/*/AgentTaskDeliveryWatchdogTests/*` | V-1, V-2, V-3, V-4 | zero failed | 1 | 10 | true |
| CP-3 | S2 | CP-2 | queue | `/*/*/SessionMessageQueueDeliveryVerificationTests/*` | V-3 | zero failed | 1 | 4 | true |
| CP-4 | S2 | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | R-1 | every named class and zero failed | 1 | 2 | true |
| CP-5 | S2 | CP-2 | composition | `/*/*/(PhoneHomeRollingRunnerTests*)\|(RefOnlyProductionCompositionTests*)\|(DelegationTestServicesTests*)\|(DelegationHarnessCensusTests*)/*` | R-2 | every named class and zero failed | 1 | 2 | true |
| CP-6 | S2 | CP-2 | land-notification | `/*/*/(AgentTaskLandHeldNotificationTests*)\|(AgentTaskLandNotificationParkFollowUpTests*)\|(AgentTaskLandNotificationPersistenceTests*)\|(AgentTaskLandNotificationReconcileLoadTests*)/*` | R-2 | every named class and zero failed | 1 | 2 | true |
| CP-7 | S2 | CP-2 | input-delivery | `/*/*/(AgentTaskMidTurnRefineTests*)\|(AttentionServiceTests*)\|(CodexCliObservationGapTests*)\|(GrokRulesCompactionRecoveryTests*)\|(GrokRulesReadyOrderingTests*)\|(LandRefusalDeliveryWitnessTests*)\|(PhoneHomeOutageMentionTests*)\|(SessionMessageQueuePhoneHomeDropTests*)\|(SessionQueueAbandonTests*)\|(StandingSessionSwitchConcurrencyTests*)/*` | R-2 | every named class and zero failed | 1 | 4 | true |
| CP-8 | S2 | CP-2 | codex-boot | `/*/*/CodexBootPointerCanaryTests/*` | R-2 | real CLI case executed and passed | 1 | 2 | true |
| CP-9 | S2 | CP-2 | land-recovery | `/*/*/(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandNotificationRescanTests*)\|(AgentTaskLandReceiptTests*)\|(AgentTaskLandReceiptWatermarkSafetyTests*)/*` | R-2 | every named class and zero failed | 1 | 2 | true |
| CP-10 | S2 | CP-2 | land-receipt | `/*/*/(AgentTaskLandReceiptWatermarkTests*)\|(ExpectationDebtTests*)\|(LandReceiptScanCacheTests*)\|(PreDispatchFailureDeliveryTests*)\|(ReceiptScanDestinationIntegrationTests*)/*` | R-2 | every named class and zero failed | 1 | 2 | true |

R-3 commands use the committed generated index. Restart: none.
