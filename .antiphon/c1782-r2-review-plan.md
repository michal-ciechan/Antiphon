# CARD-1782 Review round 2 (task 11b71861) verification

Scope: the round-2 diff de795edd6446f303d6396b856d5d13ff1183cd58..11133747a1826743829b0d73c685eacf582b1c1d. Touched classes, the guard batch, the bundle/doc readers (including MechanicalPcContractTests.C479_V05b), the selector's own row for this diff, and the OS witness policy check. Not the whole Unit lane (round 2 budget: 40 minutes).

## Performance budget

One isolated build shared by the TUnit rows; rows serial. ImpactedClassesSelectorTests spawns pwsh per case (10-14 s per walk on loaded server2); round 1 measured 164 s for 7 tests, round 2 has 18 tests (~34 walks), estimate 10 minutes. Readers are doc-reading Unit tests; discovery of a 20-operand OR filter dominates.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | R2 | `tests/Antiphon.Tests -> bin-r1782c/` | selector-witnesses | `/*/*/ImpactedClassesSelectorTests/*` | D-1 witnesses (10 malformed shapes + switch clash), seed replays, guard tests | 18 passed; no failed/skipped | 18 | 10 |
| CP-2 | R2 | CP-1 | guard-batch | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | named guard batch | 50 passed; no failed/skipped | 50 | 3 |
| CP-3 | R2 | CP-1 | bundle-readers | `/*/*/(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)\|(ScopedVerificationInstructionTests*)\|(CheckpointRepeatDocumentationTests*)\|(InstructionBundleTests*)\|(CheckpointManifestDocumentationTests*)\|(PerTaskFinalUnitLaneWitnessTests*)\|(RepairSourceDocumentationTests*)\|(ReviewRoundsPolicyTests*)\|(StableBuildOutputDocumentationTests*)\|(MechanicalPcContractTests*)\|(PostLandMutationContractTests*)\|(DockerStackDocumentationTests*)\|(Server2StagingServerContractTests*)\|(PackageVersionPinContractTests*)\|(RunnerPushCredentialDocsTests*)\|(GrokRunnerImageContractTests*)\|(AdditiveMigrationRuleWitnessTests*)\|(VerificationRoundInstructionTests*)\|(AgentBundleAttachmentTests*)/*` | byte caps, bundle pins, C479_V05b, CARD-1785 witness | all passed; no failed/skipped | 290 | 8 |
| CP-4 | R2 | CP-1 | selector-row | `/*/*/(TestTimingOperationLabelTests*)/*` | impacted-classes row selected for base..tip | 2 passed; no failed/skipped | 2 | 1 |
| CP-5 | R2 | n/a | os-witness-policy | `pwsh -NoProfile -File scripts/test-os-witnesses.ps1 -PolicyOnly` | docs/testing-and-build.md#os-branch-witnesses | exit 0 | n/a | 3 |
