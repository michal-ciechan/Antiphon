# Server2 disk protection verification

Base lineage begins at 561591c562f31dd4fe0fc8f940f2ea7e947ce965. The repair starts
at 20bb691ff5a638a3fbd61f2edd869ccbd10a148c. Review b9a6a79b evaluated
0219b4182264361bba40f228a8891dae5777078e. The evidence sweep and its controls moved to CARD-2095 at
`9f101466bef25907f887b035dc6edef8238f4990`. Disk admission's absolute floor is 20 GiB.
The attention item has its own percentage and free-space settings.

## Invariants and controls

| ID | Invariant | Test | SourceLanding control |
|---|---|---|---|
| V-5 | Shipped primary and rollout routes hold below 20 GiB and admit at the floor. | ShippedRunnersHoldOneByteBelowTheAbsoluteFloor | PC-7 remove rollout admission configuration. |
| V-6 | Missing, stale and invalid samples produce bounded diagnostics. | MissingStaleAndInvalidSamplesEmitBoundedDiagnostics | PC-8 remove the degraded-sample warning. |
| V-7 | The row and scheduler check again under the lease and release it upon refusal. | RowRechecksSpaceAfterAdmissionAndReleasesTheLease, SchedulerRechecksSpaceAfterAdmissionAndReleasesTheLease | PC-9a skip the row recheck. PC-9b skip the scheduler build recheck. PC-9c skip the scheduled row recheck. |
| V-8 | An externally launched historical build checks effective write paths before restore. | HistoricalProjectEvidenceDestinationsAreRefusedBeforeRestore | PC-10a omit IntermediateOutputPath. PC-10b omit MSBuildProjectExtensionsPath. |
| R-3 | The Linux and native Windows entries check space before bootstrap. | Linux launcher witness, Test-WindowsCheckpointBootstrap | PC-11a bypass the POSIX check. PC-11b bypass the Windows check. |
| R-4 | The timer targets unused builder cache older than 24 hours. | Inspect service and timer | Host activation is operator work after landing. |
| V-9 | A held task dispatches after the sample recovers and the same recipient gets a complete matching UserPrompt. | LowDiskRecoveryDeliversTheCompleteQueuedBrief | PC-12 bypass the dispatch hold before remote prep. |

### Checkpoints

CP-1 covers the disk and placement invariants with one isolated build and an
exact class union. CP-2 covers the six-class policy union and the named mechanical,
layering, guidance and fixture/plan guards. CARD-2093 is on its own branch and
Merge carries it. The inherited plan guard remains red until that repair joins
the candidate.

The red CP-1 measured 8m44s to the build-ready boundary, including slot wait,
and 1m14s of test execution. The rows below reuse the isolated CP-1 artifacts
for focused neighboring selections, avoiding extra cold builds against the
same project. CP-9 and CP-10 run after the Antiphon.Tests rows. The broad
dispatcher file has unbounded derived impact, which is recorded in the impact
manifest. The named dispatcher, receipt, policy and notification classes
below cover the matching declared rows. Nightly and rc own full lanes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | repairs | tests/Antiphon.Tests -> bin-ad7e-disk/ | disk | `/*/*/(WorkspaceDiskAdmissionTests*)\|(CheckpointDiskAdmissionTests*)\|(CheckpointEvidencePlacementTests*)/*` | V-5–V-8 | WorkspaceDiskAdmissionTests,CheckpointDiskAdmissionTests,CheckpointEvidencePlacementTests | 11 | 14 | true |
| CP-2 | repairs | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | policy and source guards | TestClassificationPolicyTests,TestClassificationGuardTests,SlowTestRegistryMergeTests,SlowTestTripwireTests,NativeLinuxPolicyWitnessTests,MechanicalPcContractTests,TaskPlatformGuidanceTests,C1729LayeringGuardTests,TestLaneCategoryGuardTests,TestFixtureCleanupGuardTests,PlanCheckpointClassSelectionGuardTests | 87 | 2 | true |
| CP-3 | repairs | CP-2 | guards | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | live gate inventory | LiveLaneInventoryTests | 2 | 1 | true |
| CP-4 | repairs | CP-1 | dispatch recovery | `/*/*/PhoneHomeTaskDispatchProjectionTests/LowDiskRecoveryDeliversTheCompleteQueuedBrief` | V-9 | LowDiskRecoveryDeliversTheCompleteQueuedBrief | 3 | 4 | true |
| CP-5 | repairs | CP-1 | runner and checkpoint neighbors | `/*/*/(RowVerbTests*)\|(RunSchedulerTests*)\|(CheckpointLauncherTests*)\|(C1677DiskAlarmTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointTerminalReceiptTests*)\|(HostCleanupCheckpointReceiptTests*)\|(ProcessSpawnLimitTests*)\|(PhoneHomeRollingRunnerTests*)\|(RefOnlyProductionCompositionTests*)\|(DelegationTestServicesTests*)\|(DelegationHarnessCensusTests*)\|(Card1277PtyHostFixtureTests*)/*` | runner and checkpoint regression | RowVerbTests,RunSchedulerTests,CheckpointLauncherTests,C1677DiskAlarmTests,CheckpointReceiptSafetyTests,CheckpointTerminalReceiptTests,HostCleanupCheckpointReceiptTests,ProcessSpawnLimitTests,PhoneHomeRollingRunnerTests,RefOnlyProductionCompositionTests,DelegationTestServicesTests,DelegationHarnessCensusTests,Card1277PtyHostFixtureTests | 13 | 6 | true |
| CP-6 | repairs | CP-1 | dispatcher budgets | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | dispatcher statement budgets | C1149_C1150_Statement_budgets | 1 | 1 | true |
| CP-7 | repairs | CP-1 | native exclusion policy | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | OS policy | C1686_LinuxOnlyCensus,LinuxOnlyWindowsExclusionPolicy | 2 | 1 | true |
| CP-8 | repairs | CP-1 | dispatcher notification state | `/*/*/(AgentTaskLandHeldNotificationTests*)\|(AgentTaskLandNotificationParkFollowUpTests*)\|(AgentTaskLandNotificationPersistenceTests*)\|(AgentTaskLandNotificationReconcileLoadTests*)/*` | dispatcher notification regression | AgentTaskLandHeldNotificationTests,AgentTaskLandNotificationParkFollowUpTests,AgentTaskLandNotificationPersistenceTests,AgentTaskLandNotificationReconcileLoadTests | 4 | 2 | true |
| CP-9 | repairs | tests/Antiphon.SessionRunner.Tests -> bin-ad7e-runner/ | spawn roster | `/*/*/ProcessSpawnLimitTests/*` | runner spawn roster | ProcessSpawnLimitTests | 1 | 4 | true |
| CP-10 | repairs | tests/Antiphon.Agents.Pty.Tests -> bin-ad7e-pty/ | spawn roster | `/*/*/ProcessSpawnLimitTests/*` | PTY spawn roster | ProcessSpawnLimitTests | 1 | 4 | true |
| CP-11 | repairs | CP-1 | policy pin | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)/*` | native exclusions preserve live-lane sentinels and policy | LiveLaneRunnerTests,NightlyCoreGoldenTests | 2 | 2 | true |
| CP-12 | repairs | CP-1 | dispatcher notification recovery | `/*/*/(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandNotificationRescanTests*)\|(AgentTaskLandReceiptTests*)\|(AgentTaskLandReceiptWatermarkSafetyTests*)/*` | dispatcher notification regression | AgentTaskLandNotificationRecoveryTests,AgentTaskLandNotificationRescanTests,AgentTaskLandReceiptTests,AgentTaskLandReceiptWatermarkSafetyTests | 4 | 2 | true |
| CP-13 | repairs | CP-1 | dispatcher receipt neighbors | `/*/*/(AgentTaskLandReceiptWatermarkTests*)\|(ExpectationDebtTests*)\|(LandReceiptScanCacheTests*)\|(ReceiptScanDestinationIntegrationTests*)/*` | dispatcher notification regression | AgentTaskLandReceiptWatermarkTests,ExpectationDebtTests,LandReceiptScanCacheTests,ReceiptScanDestinationIntegrationTests | 4 | 2 | true |
| CP-14 | repairs | CP-1 | dispatcher failure delivery | `/*/*/PreDispatchFailureDeliveryTests/*` | dispatcher notification regression | PreDispatchFailureDeliveryTests | 1 | 2 | true |
| CP-15 | guard-witnesses | tests/Antiphon.Tests -> bin-e258-red/ | red-first | `/*/*/WorkspaceDiskAdmissionTests/SharedFilesystemHoldsBothRoutesAndRecoversFromEither` | V-5 | SharedFilesystemHoldsBothRoutesAndRecoversFromEither | 2 | 2 | true |

## Native and manual witnesses

R-3 Linux uses the real `scripts/antiphon` entry with a controlled `df` and a
dotnet launch sentinel in `scripts/test-checkpoint-disk-guard.mjs`. The Windows
obligation uses a clean copy of `scripts\antiphon.cmd` from the tested SHA and a
caller volume with less than 20 GiB free. It must print `workspace_disk_low`,
exit 9, and leave the bootstrap cache absent. Run the required impacted Windows
`Card1277PtyHostFixtureTests` and `GrokDelegateEndToEndTests` separately on Windows.

```powershell
scripts\antiphon.cmd build-slot --label checkpoint-windows-bootstrap -- pwsh -NoProfile -File scripts/test-checkpoint-bootstrap-win.ps1 -LowSpaceDirectory <owned-low-space-directory>
```

R-4 checks the hourly timer and the service's exact builder-prune invocation.
Host activation follows land.
