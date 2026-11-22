# CARD-2052 Review round 1 repair: checkpoints

Repair of Review d1e0f4ca (round 1) blockers D-1, D-2 and D-3 on the owned-output deletion path
(`CheckpointOutputLedger.Remove` through `TrackedIndexScope`). Round: Final. Base
`ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91`; round-1 subject `c0173afa346eb64aa022f80e4a749504e03179d9`.

## Verification design

- V-1 (D-1): `index_staged_during_the_tracked_read_leaves_the_file_in_place` and
  `index_staged_between_decision_and_unlink_leaves_the_file_in_place`: through the real
  `CleanOwnedOutputs`, the target is staged during its own tracked read, or after its decision and before its
  unlink. The target survives, is refused, and the two untracked owned files are removed.
- V-2 (D-2): `equal_size_index_rewrite_with_unmoved_metadata_leaves_the_file_in_place`: an equal-size in-place
  index rewrite whose stat metadata does not move (seam) makes the target tracked. Reuse is proven active before
  the rewrite (zero reads between the two deep unlinks); the target survives.
- V-3 (D-3): `core_worktree_rewrite_with_unmoved_config_metadata_leaves_the_file_in_place` (core.worktree written
  into the repository config in place, config metadata unmoved: git calls the target tracked; it survives),
  `config_include_reads_the_index_for_every_entry` (an include declines reuse) and
  `linked_worktree_common_config_worktree_reads_the_index_again` (a linked worktree's common config.worktree stops
  reuse at once).
- V-4: the two earlier CARD-2052 reuse witnesses stay green (index reads <= 10 for 46 entries).
- R-1: the eight classes Review ran, with the four OutputCleanupTests Windows-only skips now rostered in
  `WindowsUnitSkipRoster.WindowsOnly` (receipt `skip_identity_unlisted` before).
- R-2: Final guards: policy sweep, mechanical contract, TaskPlatformGuidanceTests, layering, lane category,
  fixture cleanup, platform admission (roster closure), spawn limits.
- R-3: `scripts/impacted-classes.ps1 -Range ecc9bfaa5..HEAD` classes (45 in Antiphon.Tests plus
  `ProcessSpawnLimitTests` in Antiphon.Agents.Pty.Tests and Antiphon.SessionRunner.Tests, and the two owed/capped
  SafeWrite.cs classes), split into rows of at most 25 minutes. No migrations row.

All six new witnesses were red at `a03470bc95f5712b7d18bed5f66d9a7e4221828f` (four tracked targets unlinked, two
missed re-reads) and green at the fix. PCs stay pending for SourceLanding Mutation (mutation waived this round).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2052-final/ | named-eight | `/*/*/(OutputCleanupTests*)\|(CheckpointOwnedOutputTests*)\|(HostCleanupCheckpointExtractionTests*)\|(CheckpointToolCopyCleanupTests*)\|(PreflightCommandLineBudgetTests*)\|(SafeDeleteProcessBoundaryTests*)\|(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | V-1,V-2,V-3,V-4,R-1 | 154 pass; 4 rostered OutputCleanupTests Windows skips | 154 | 15 | true |
| CP-2 | all | CP-1 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(ProcessSpawnLimitTests*)/*` | R-2 | >= 40 executed, 0 failed | 40 | 8 | true |
| CP-3 | all | CP-1 | impacted-cleanup | `/*/*/(AnchoredPathDeleteWitnessTests*)\|(C1307ToolRemovalTests*)\|(CheckpointBuildBindingPathTests*)\|(CheckpointExecutorLogTests*)\|(CheckpointLargeStampTests*)\|(CheckpointOrFilterTests*)\|(CheckpointProcessIdentityTests*)\|(CheckpointSelectedOutputTests*)\|(CheckpointStatePublicationTests*)\|(CheckpointTaskOwnershipTests*)\|(EvidenceFolderTests*)\|(HostCleanupCheckpointAuthorityTests*)\|(ReapedProcStatIdentityTests*)\|(TempRootSweepAnchoredDeleteTests*)\|(UnsafeDeleteRecoveryTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointShadowCustodyTests*)/*` | R-3 | >= 60 executed, 0 failed | 60 | 20 | true |
| CP-4 | all | CP-1 | impacted-windows-named | `/*/*/(CheckpointRecoveryWindowsTests*)\|(CheckpointWindowsAnchoredDeleteTests*)\|(CheckpointWindowsAnchoredPathDeleteWitnessTests*)\|(CardFilePrivacyGitTests*)\|(CardFilePrivacyPathTests*)\|(RcBridgeProbePlatformTests*)/*` | R-3 | >= 10 executed, 0 failed; Linux skips reported by identity | 10 | 10 | true |
| CP-5 | all | CP-1 | impacted-selection | `/*/*/(CheckpointAdmissionContractTests*)\|(CheckpointImportTests*)\|(DerivedImpactIndexTests*)\|(IncrementalSelectionSoundnessTests*)\|(LandGeneratedArtifactTests*)\|(OuterRowBuildStampTests*)\|(PhaseJournalKillDiagnosticTests*)\|(RcInputManifestTests*)\|(RcInputOwnersMapTests*)\|(ReleaseImpactEnvelopeTests*)/*` | R-3 | >= 60 executed, 0 failed | 60 | 25 | true |
| CP-6 | all | CP-1 | impacted-runner | `/*/*/(RowRunnerTests*)\|(WaitCommandTests*)/*` | R-3 | >= 10 executed, 0 failed | 10 | 20 | true |
| CP-7 | all | CP-1 | impacted-source-script | `/*/*/RunCheckpointSourceScriptTests*/*` | R-3 | >= 5 executed, 0 failed | 5 | 25 | true |
| CP-8 | all | tests/Antiphon.Agents.Pty.Tests -> bin-c2052-pty/ | pty-spawn-limit | `/*/*/ProcessSpawnLimitTests*/*` | R-2,R-3 | >= 1 executed, 0 failed | 1 | 10 | true |
| CP-9 | all | tests/Antiphon.SessionRunner.Tests -> bin-c2052-sr/ | sessionrunner-spawn-limit | `/*/*/ProcessSpawnLimitTests*/*` | R-2,R-3 | >= 1 executed, 0 failed | 1 | 10 | true |
