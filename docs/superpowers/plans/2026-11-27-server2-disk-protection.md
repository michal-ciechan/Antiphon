# Server2 disk protection verification

Base lineage begins at 561591c562f31dd4fe0fc8f940f2ea7e947ce965. The repair starts
at 20bb691ff5a638a3fbd61f2edd869ccbd10a148c. Review b9a6a79b evaluated
0219b4182264361bba40f228a8891dae5777078e. Keep 24 hours as the default and
minimum for the evidence sweep. Disk admission's absolute floor is 20 GiB.
The attention item has its own percentage and free-space settings.

## Invariants and controls

| ID | Invariant | Test | SourceLanding control |
|---|---|---|---|
| V-1 | A cwd-only holder prevents removal. | WorkingDirectoryAloneRetainsEvidence | PC-1 omit the cwd link. |
| V-2 | The protected-component policy covers every descendant. | ProtectedDescendantRetainsWholeEvidenceDirectory | PC-2 omit the protected names from the descent. |
| V-3 | A release binds its full owner to one directory identity. | AReplacementAtTheReleasedPathIsRetained | PC-3 skip the pre-move directory identity check. |
| V-4 | A new reference on an already queued task after quarantine restores the same directory. | ReferenceAddedByAnExistingTaskDuringRenameRestoresEvidence | PC-4 omit the post-move task detail check. |
| R-1 | Paths outside the five roots refuse even when listed. | ReleaseOutsideEvidenceRootsIsRejected | PC-5 add the sibling retained root to the allowlist. |
| R-2 | Apply considers only explicit release entries. | ApplyRemovesOnlyListedEvidence | PC-6 enumerate a sibling and submit it as released. |
| V-5 | Shipped primary and rollout routes hold below 20 GiB and admit at the floor. | ShippedRunnersHoldOneByteBelowTheAbsoluteFloor | PC-7 remove rollout admission configuration. |
| V-6 | Missing, stale and invalid samples produce bounded diagnostics. | MissingStaleAndInvalidSamplesEmitBoundedDiagnostics | PC-8 remove the degraded-sample warning. |
| V-7 | The row and scheduler check again under the lease and release it upon refusal. | RowRechecksSpaceAfterAdmissionAndReleasesTheLease, SchedulerRechecksSpaceAfterAdmissionAndReleasesTheLease | PC-9a skip the row recheck. PC-9b skip the scheduler build recheck. PC-9c skip the scheduled row recheck. |
| V-8 | An externally launched historical build checks effective write paths before restore. | HistoricalProjectEvidenceDestinationsAreRefusedBeforeRestore | PC-10a omit IntermediateOutputPath. PC-10b omit MSBuildProjectExtensionsPath. |
| R-3 | The Linux and native Windows entries check space before bootstrap. | Launcher witnesses | PC-11a bypass the POSIX check. PC-11b bypass the Windows check. |
| R-4 | The timer targets unused builder cache older than 24 hours. | Inspect service and timer | Host activation is operator work after landing. |

### Checkpoints

The product row uses one isolated build and exact class union. The guard row
covers the six-class policy union and named mechanical, layering, guidance and
fixture/plan guards. The CARD-2093 repair is on its own branch and Merge carries it.
The inherited plan guard remains red until that repair joins the candidate.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | repairs | tests/Antiphon.Tests -> bin-ad7e-disk/ | disk | `/*/*/(Server2EvidenceRetentionTests*)\|(WorkspaceDiskAdmissionTests*)\|(CheckpointDiskAdmissionTests*)\|(CheckpointEvidencePlacementTests*)/*` | V-1–V-8, R-1, R-2 | Server2EvidenceRetentionTests,WorkspaceDiskAdmissionTests,CheckpointDiskAdmissionTests,CheckpointEvidencePlacementTests | 19 | 20 | true |
| CP-2 | repairs | tests/Antiphon.Tests -> bin-ad7e-guards/ | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | policy and source guards | TestClassificationPolicyTests,TestClassificationGuardTests,SlowTestRegistryMergeTests,SlowTestTripwireTests,NativeLinuxPolicyWitnessTests,MechanicalPcContractTests,TaskPlatformGuidanceTests,C1729LayeringGuardTests,TestLaneCategoryGuardTests,TestFixtureCleanupGuardTests,PlanCheckpointClassSelectionGuardTests | 87 | 20 | true |
| CP-3 | repairs | CP-2 | guards | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | live gate inventory | LiveLaneInventoryTests | 2 | 1 | true |
