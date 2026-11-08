# CARD-1782 round 3 repair checkpoints

Port the impacted-classes selector onto master and restore the stage-review pointer. No OS-selected production behavior changes. The selector script and table are portable; the witness is test-only.

## Performance budget

Apply docs/testing-and-build.md#performance-standards. CP-1 includes one cold `tests/Antiphon.Tests` build (round-2 checkpoint build was 337 s) plus a 10-class filter. CP-2 reuses that build. Estimates are allowances, not a measured p95 for this repair. Row deadline is `max(15, 3 * EstimatedMinutes)`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1782r3/` | selector-readers | `/*/*/(ImpactedClassesSelectorTests*)\|(AdditiveMigrationRuleWitnessTests*)\|(InstructionBundleTests*)\|(TaskPlatformGuidanceTests*)\|(VerificationRoundInstructionTests*)\|(ReviewRoundsPolicyTests*)\|(AgentBundleAttachmentTests*)\|(CheckpointRepeatDocumentationTests*)\|(MechanicalPcContractTests*)\|(TestTimingOperationLabelTests*)/*` | D-3, selector, bundle readers | 0 failed | 120 | 8 | true |
| CP-2 | all | CP-1 | guard-batch | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | guard batch | 0 failed | 45 | 4 | true |
