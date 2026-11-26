# CARD-2015 graceful-stop selector

Base `e0e747772967f8ed4e2085e79aac21c4b056fa3b`. A Git range that changes either `scripts/restart-apphost.ps1` or `scripts/lib/windows-phone-home-env.ps1` must select `OperatorShutdownEndpointTests` and `PhoneHomeRestartResumeTests` in `tests/Antiphon.Tests`. The endpoint class checks authenticated shutdown acceptance, host stopping, and the starting-launch count. The resume class checks reattachment and the failure record for an interrupted pre-ack launch. The phone-home environment helper supplies the startup refusal checked before the shutdown request in `restart-apphost.ps1`. This card changes selection only. Both classes use test-owned fixtures. No OS-selected production path changes or native acceptance runs are required.

## Verification design

- V-1. Run the same two cases on the committed pre-fix test slice and on the final slice. Each case checks that its historical range changed exactly one script. Expect two missing-kind assertion failures before the fix and two passes after it.
- V-2. Run the full selector class, including the table audit.
- V-3. Run the full derived-index class, including byte comparison against a fresh build of the index. Regenerate the tracked index after editing its inputs.
- V-4. Run the shutdown and restart-resume integration classes.
- V-5. Run the nine-class Code guard batch.
- V-6. Run the remaining class filters selected by the task range's derived edges. Check the actual range output for additional selections.
- R-1. Check the evidence diff from base through the final committed source. Use the current `scripts/antiphon evidence-diff` entry point.
- R-2. Run the land graph audit in Check mode and record its diagnostic and exit.
- R-3. Resolve the actual task range and check `git merge-tree --write-tree origin/master HEAD` after fetching master.

The classes in scope can be bounded by the filters below. The final selection has one isolated build and an estimated 20 minutes of execution including the checks. CP-1 uses the same exact filter on each of the two committed slices. Deliberate mutation remains assigned to SourceLanding, subject to the operator waiver. The potential controls are PC-1a remove the restart glob, PC-1b remove the phone-home helper glob, PC-2a remove the endpoint class and PC-2b remove the resume class. Each uses the CP-1 method.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | final | tests/Antiphon.Tests -> bin-c2015-final/ | graceful-stop-selector | `/*/Antiphon.Tests.Scripts/ImpactedClassesSelectorTests/Graceful_stop_script_changes_select_shutdown_and_resume_tests` | V-1 | 0 failed, 0 skipped on final slice | 2 | 7 | true |
| CP-2 | final | CP-1 | selector | `/*/Antiphon.Tests.Scripts/ImpactedClassesSelectorTests/*` | V-2 | 0 failed, 0 skipped | 1 | 4 | true |
| CP-3 | final | CP-1 | index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-3 | 0 failed, 0 skipped | 1 | 2 | true |
| CP-4 | final | CP-1 | graceful-stop-integrations | `/*/*/(OperatorShutdownEndpointTests*)\|(PhoneHomeRestartResumeTests*)/*` | V-4 | 0 failed, 0 skipped | 5 | 2 | true |
| CP-5 | final | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-5 | 0 failed, 0 skipped | 1 | 2 | true |
| CP-6 | final | CP-1 | derived-impact-consumers | `/*/*/(IncrementalSelectionSoundnessTests*)\|(LandGeneratedArtifactTests*)\|(PostLandEarlyWarningTests*)\|(RcInputManifestTests*)\|(RcInputOwnersMapTests*)\|(ReleaseImpactEnvelopeTests*)\|(IncrementalSelectionSoundnessInvariantTests*)/*` | V-6 | 0 failed, 0 skipped | 1 | 3 | true |
