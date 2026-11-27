# Server2 disk protection SourceLanding controls

All controls are pending for SourceLanding Mutation of landing owner
`abfd18ed-34d4-4318-bbc6-18ae42d464d4`. Use a bound pack and exact test
method filters. PC-1 through PC-6 moved with the evidence sweep to CARD-2095.

| ID / variant | Deliberate defect | Exact witness |
|---|---|---|
| PC-7a | Remove rollout admission configuration. | WorkspaceDiskAdmissionTests.ShippedRunnersHoldOneByteBelowTheAbsoluteFloor |
| PC-7b | Pick measurements by runner ID even for a shared filesystem. | WorkspaceDiskAdmissionTests.SharedFilesystemHoldsBothRoutesAndRecoversFromEither |
| PC-8 | Omit the degraded-sample warning. | WorkspaceDiskAdmissionTests.MissingStaleAndInvalidSamplesEmitBoundedDiagnostics |
| PC-9a | Skip the row post-lease check. | CheckpointDiskAdmissionTests.RowRechecksSpaceAfterAdmissionAndReleasesTheLease |
| PC-9b | Skip the scheduler build post-lease check. | CheckpointDiskAdmissionTests.SchedulerRechecksSpaceAfterAdmissionAndReleasesTheLease |
| PC-9c | Skip the scheduled test post-lease check. | CheckpointDiskAdmissionTests.SchedulerRechecksSpaceAfterAdmissionAndReleasesTheLease |
| PC-9d | Skip the baseline build post-lease check. | BaselineComparerTests.BaselineRechecksSpaceAfterGrantAndReleasesTheLease |
| PC-9e | Skip the baseline test post-lease check. | BaselineComparerTests.BaselineRechecksSpaceAfterGrantAndReleasesTheLease |
| PC-10a | Omit IntermediateOutputPath in checkpoint graph evaluation. | CheckpointEvidencePlacementTests.HistoricalProjectEvidenceDestinationsAreRefusedBeforeRestore |
| PC-10b | Omit MSBuildProjectExtensionsPath in checkpoint graph evaluation. | CheckpointEvidencePlacementTests.HistoricalProjectEvidenceDestinationsAreRefusedBeforeRestore |
| PC-10c | Validate only the first evaluated node for each project path. | CheckpointEvidencePlacementTests.ReferenceSpecificPropertiesAreCheckedForEveryOccurrenceOfTheSameProject |
| PC-10d | Remove the leased direct build's trusted import. | R-5 historical build refusal |
| PC-11a | Bypass the POSIX entry's guard. | checkpoint launch refuses low disk before starting dotnet |
| PC-11b | Bypass the Windows cmd guard. | Test-WindowsCheckpointBootstrap |
| PC-11c | Skip the guard in the PowerShell exe entry. | CliEntryTests.Checkpoint_entry_refuses_low_space_before_publishing_or_launching and Test-WindowsCheckpointBootstrap |
| PC-11d | Remove the SDK checkpoint project's initial space check. | R-3 native raw SDK entry |
| PC-12a | Bypass the dispatch hold before remote preparation. | PhoneHomeTaskDispatchProjectionTests.LowDiskRecoveryDeliversTheCompleteQueuedBrief |
| PC-12b | Skip scheduling recovery for Running sessions with persisted missing-brief debt. | PhoneHomeTaskDispatchProjectionTests.LowDiskRecoveryDeliversTheCompleteQueuedBrief |
