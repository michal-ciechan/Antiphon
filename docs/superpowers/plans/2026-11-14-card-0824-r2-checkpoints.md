# CARD-0824 F1-F4 round 2: Application roots seam

Task/landing owner: `1a125507-3ada-4753-a1c2-5462483a3e19`. Original producer
Code owner: `d684bc25-111b-46a0-b474-c7255a7a66d5`.
Base: `27a43f25221c1508f12a2c6a4b89500d0050986a`.
Parent: [accepted plan](2026-11-14-card-0824-all-roles-enablement-plan.md).

The repair preserves the exact `GitSettings` argument and default `create=false`
of `WorktreeRoots.Resolve`. DelegationWorktreeService already owns its allowed
edge. Move that call behind a method there, used by both preflight and producer.
Do not normalize again, change case/separators, create a directory, consult a
different settings instance, change admission, or extend the allowlist.

The repository witness inspects listed and unlisted forbidden edges regardless
of warn mode and rejects ambiguous forbidden references. Its pre-fix assertion
red is required; the full guard inventory must change VIOLATIONS 1 to 0 with
LISTED 162 unchanged. Deliberate old-edge restoration belongs to SourceLanding
Mutation, method-scoped on this witness, pending ordinary Review.

## Performance budget

Rows run serially, with a provisional 10-minute row/build bound and separate
slot admission, under CARD-0824/CARD-1906 load constraints. First red/green
provides the current sample; no timing-only repetitions. Main cold build is
historically 4-10 minutes; body guard scan is seconds to a minute. Native output
bootstrap of the coordinator and derived-index driver is explicitly required.
Other F checkpoints remain the parent's closed rows CP-1..CP-7, CP-14..CP-16,
CP-23..CP-30, plus its named guard union and gate-off callers from the brief.
Run each bounded committed group separately; no whole Unit or assembly run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| R2-RED | red | tests/Antiphon.Tests -> bin-c0824-r2-red/ | portable-layering-red | `/*/*/C1729LayeringGuardTests/*` | warn-mode producer edge, existing query witness, inventory | one producer assertion red before fix; VIOLATIONS 1 LISTED 162 | 10 | 5 | true |
| R2-GREEN | fix | tests/Antiphon.Tests -> bin-c0824-r2-green/ | portable-layering-green | `/*/*/C1729LayeringGuardTests/*` | same full guard after seam | all pass; VIOLATIONS 0 LISTED 162 | 10 | 5 | true |
| R2-ROOTS | fix | tests/Antiphon.Tests -> bin-c0824-r2-green/ | portable-managed-roots | `/*/*/DelegationTestServicesTests/*` | caller settings, relative and absolute paths, native separators, no mkdir, missing config, DI | every method and argument, 0 failed | 11 | 2 | true |
| CP-1 | F1 | tests/Antiphon.Tests -> bin-c0824-r2-green/ | portable-intent | `/*/*/(C0824RefOnlyProvisionTests*)\|(C0824RefOnlyIdentityTests*)\|(C0824RefOnlyGateTests*)\|(ServerGitProcessTests*)\|(DelegationTestServicesTests*)/*` | F1-F4, recorded mode, Git start sites, DI | all listed, 0 failed | 17 | 8 | true |
| CP-2 | F2 | tests/Antiphon.Tests -> bin-c0824-r2-green/ | portable-publication | `/*/*/C0824RefOnlyPreparationTests/*` | P1-P4 | 4 methods, 0 failed/skipped | 4 | 7 | true |
| CP-3 | F3 | tests/Antiphon.Tests -> bin-c0824-r2-b/ | linux-dispatch | `/*/*/(C0824RefOnlyProvisionTests*)\|(C0824RefOnlyPreparationTests*)\|(RemoteWorkspacePreparerTests*)/*` | F1-F6,P1-P6, legacy prep | all listed, 0 failed | 13 | 10 | true |
| CP-4 | F3 | tests/Antiphon.Tests -> bin-c0824-r2-b/ | portable-statement-budget | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | CARD-1782 statement budget | exact method, 0 failed | 1 | 6 | true |
| CP-5 | F4 | tests/Antiphon.Tests -> bin-c0824-r2-c/ | linux-lifecycle | `/*/*/(C0824RefOnlyProducerLifecycleTests*)\|(C0824RefOnlySyncTests*)\|(C0824RefOnlyDebtTests*)\|(C0824RefOnlyContinuationTests*)/*` | L1-L5, CAS/debt/reply | all listed, 0 failed | 8 | 10 | true |
| CP-6 | F4 | tests/Antiphon.Tests -> bin-c0824-r2-c/ | portable-reader-regression | `/*/*/(C0824RefOnlyReadTests*)\|(C0824RefOnlyDeliverableTests*)\|(C0824RefOnlyConsumerTests*)\|(C0824RefOnlyReceiptTests*)/*` | bytes, identity, receipt | all listed, 0 failed | 4 | 9 | true |
| CP-7 | F4 | tests/Antiphon.Tests -> bin-c0824-r2-c/ | portable-land-regression | `/*/*/(C0824RefOnlyLandingTests*)\|(C0824RefOnlyCleanupTests*)\|(C0824RefOnlyBoundaryTests*)/*` | late land, cleanup, recovery refusal | all listed, 0 failed | 3 | 9 | true |
| CP-14 | F3 | tests/Antiphon.Tests -> bin-c0824-r2-d/ | portable-dispatch-impact-a | `/*/*/(AgentTaskLandHeldNotificationTests*)\|(AgentTaskLandNotificationParkFollowUpTests*)\|(AgentTaskLandNotificationPersistenceTests*)\|(AgentTaskLandNotificationReconcileLoadTests*)\|(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandNotificationRescanTests*)/*` | CARD-1782 notification callers | every named class, 0 failed | 6 | 10 | true |
| CP-15 | F3 | tests/Antiphon.Tests -> bin-c0824-r2-d/ | portable-dispatch-impact-b | `/*/*/(AgentTaskLandReceiptTests*)\|(AgentTaskLandReceiptWatermarkSafetyTests*)\|(AgentTaskLandReceiptWatermarkTests*)\|(ExpectationDebtTests*)\|(LandReceiptScanCacheTests*)\|(PreDispatchFailureDeliveryTests*)\|(ReceiptScanDestinationIntegrationTests*)/*` | CARD-1782 notification callers | every named class, 0 failed | 7 | 10 | true |
| CP-16 | F1 | tests/Antiphon.Server.Git.Tests -> bin-c0824-r2-git/ | portable-git-start-sites | `/*/*/(GitIndexLockTests*)\|(ServerGitAssemblyTests*)/*` | CARD-1782 extracted Git start sites | every named class, 0 failed | 2 | 5 | true |
| CP-23 | G1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-r2-runner/ | portable-runner-roster | `/*/*/(ProcessSpawnLimitTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | assembly limiter census and shared exclusion pin | every named class, 0 failed | 4 | 3 | true |
| CP-24 | G1 | tests/Antiphon.Agents.Pty.Tests -> bin-c0824-r2-pty/ | portable-pty-roster | `/*/*/(ProcessSpawnLimitTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | assembly limiter census and shared exclusion pin | every named class, 0 failed | 4 | 3 | true |
| CP-25 | F1 | tests/Antiphon.Tests -> bin-c0824-r2-e/ | portable-legacy-base | `/*/*/DelegationWorktreeTests/(C508_ReuseKeepsRecordedBase*)\|(C508_BaseResolutionRequiresOwnedLease*)` | existing base and repository-lease callers | both exact intended methods, 0 failed | 2 | 6 | true |
| CP-26 | F2 | tests/Antiphon.Tests -> bin-c0824-r2-e/ | portable-legacy-sync | `/*/*/RunnerSettlementSyncTests/(Pushed_tip_fast_forwards_exact_owned_checkout*)\|(Endpoint_change_is_not_followed*)\|(C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource*)` | existing Checkout settlement callers | all 3 intended methods, 0 failed | 3 | 6 | true |
| CP-27 | F4-policy | tests/Antiphon.Tests -> bin-c0824-r2-e/ | portable-native-policy | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | exact impacted Linux exclusion-policy methods | both methods, 0 failed | 2 | 6 | true |
| CP-28 | F4-policy | tests/Antiphon.Tests -> bin-c0824-r2-f/ | portable-nightly-policy | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreTests*)/*` | impacted policy reader and repinned live-lane contract | every named class, 0 failed | 2 | 7 | true |
| CP-29 | G1 | tests/Antiphon.Tests -> bin-c0824-r2-e/ | portable-fixture-teardown | `/*/*/ClassTeardownTrackerTests/*` | shared producer host closes before shared database disposal | 3 methods, 0 failed | 3 | 2 | true |
| CP-30 | F4-policy | tests/Antiphon.Tests -> bin-c0824-r2-e/ | portable-policy-pin-regression | `/*/*/(NativeLinuxPolicyWitnessMutationTests*)\|(NativeLinuxPolicyMatcherParityTests*)/*` | exact exclusion pin and existing independent rejection/matcher controls | every named class, 0 failed | 13 | 6 | true |
| R2-PREP | final | tests/Antiphon.Tests -> bin-c0824-r2-b/ | portable-preparation-alone | `/*/*/RemoteWorkspacePreparerTests/*` | required standalone preparation regression | all methods, 0 failed | 1 | 5 | true |
| R2-GUARDS | final | tests/Antiphon.Tests -> bin-c0824-r2-f/ | portable-guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | exact brief guard union | every named class, 0 introduced failures | 11 | 8 | true |
| R2-IMPACT | final | tests/Antiphon.Tests -> bin-c0824-r2-f/ | portable-impact-guards | `/*/*/(ProcessSpawnLimitTests*)\|(TestTimingOperationLabelTests*)\|(ReleaseImpactEnvelopeTests*)/*` | impacted hand rows and parent final adjacent guards | every named class, 0 introduced failures | 3 | 4 | true |
| R2-OFF-DISPATCH | final | tests/Antiphon.Tests -> bin-c0824-r2-g/ | portable-gate-off-dispatch | `/*/*/(AgentTaskDispatchBaseGuardTests*)\|(AgentTaskMidTurnRefineTests*)/*` | default legacy dispatcher, touched caller tests | every named class, 0 failed | 2 | 7 | true |
| R2-OFF-LAND | final | tests/Antiphon.Tests -> bin-c0824-r2-g/ | portable-gate-off-land | `/*/*/(AgentTaskLandPreparationIdentityTests*)\|(AgentTaskLandAdmissionControlledTests*)\|(AgentTaskLandBoundaryControlledTests*)\|(AgentTaskLandConcurrencyControlledTests*)/*` | default legacy land admission and custody | every named class, 0 failed | 4 | 7 | true |
| R2-OFF-SETTLE | final | tests/Antiphon.Tests -> bin-c0824-r2-g/ | portable-gate-off-settlement | `/*/*/(RunnerTaskSettlementTests*)\|(RunnerSettlementSyncTests*)/*` | default legacy settlement and touched fixture | every named class, 0 failed | 2 | 7 | true |
| R2-RUNNER-POLICY | final | tests/Antiphon.SessionRunner.Tests -> bin-c0824-r2-runner/ | portable-runner-policy | `/*/*/NativeLinuxPolicyWitnessTests/*` | shared native-policy change in runner assembly | all methods, 0 failed | 1 | 2 | true |
| R2-PTY-POLICY | final | tests/Antiphon.Agents.Pty.Tests -> bin-c0824-r2-pty/ | portable-pty-policy | `/*/*/NativeLinuxPolicyWitnessTests/*` | shared native-policy change in Pty assembly | all methods, 0 failed | 1 | 2 | true |

Bounded source group packing for this round: A (R2-GREEN, R2-ROOTS, CP-1, CP-2),
B (CP-3, CP-4, R2-PREP), C (CP-5..CP-7), D (CP-14, CP-15),
E (CP-25..CP-27, CP-29, CP-30), F (CP-28, R2-GUARDS, R2-IMPACT),
G (R2-OFF-DISPATCH, R2-OFF-LAND, R2-OFF-SETTLE). Each group is a separate
checkpoint run, serial, at one committed SHA, with one fresh isolated main build
whose verified provenance is shared by its exact-filter rows. This is an output
packing amendment only: parent CP IDs, filters, assertions, floors and estimates
are retained. Parent estimates conservatively include a cold build per row;
each group sums to at most 28 minutes. The first real cold sample was 493.553s,
body guard scan 0.213s, startup 28.341s, host wall 29.040s, source 2150a37ac3b5,
run 20261114-184101-1958. It is one loaded sample, not a p95.
Run Git, runner and Pty rows separately, each in its declaring project.
No whole-assembly selection or native Windows acceptance is added by this packing.
Windows CP-8 and live canary remain pending, never credited by Linux skips.
Parent F/P/L acceptance and all PC variants retain their original obligations.
No ordinary V-n/R-n acceptance IDs are declared by the parent F scope.
