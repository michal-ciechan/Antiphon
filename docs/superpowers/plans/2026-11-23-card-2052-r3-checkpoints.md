# CARD-2052 Review round 3 repair: checkpoints

This repairs Review 2b79e113 (round 3; note `docs/superpowers/reviews/2026-11-23-card-2052-cleanup-review-r3.md` on
`feat/card-task-2b79e113` at `0d927a18`). Round: Final. The branch was re-cut on master
`0f5fea9f158bad684030538d4cb908e8c467ff94` by cherry-picking the round-2 chain (`feat/card-task-a3ce9932` at
`8f0a7960471ecbcbad8dabae67aa5530c9dcec26`). This table replaces `2026-11-23-card-2052-r2-checkpoints.md` for this
round. The native Windows rows (six rows, 40 executions) stay in `2026-11-23-card-2052-r2-checkpoints-windows.md`,
because they cannot run on server2.

Blocker 1: a config include made the stamp incomplete, but the in-process decision still bound its version.
`DecisionStillHolds` then compared a stamp that had never seen the included file. Now a decision binds only a
Reusable stamp, meaning complete and settled. Any of these makes the decision unbound:

- an include or includeIf in any config git consults, at repository or user/system level;
- per-worktree config;
- a racy index.

The stamp now also observes every included file, recursively and whatever the include's condition. An unbound entry
is asked again, with no reuse, after content validation. It is kept when the stamp re-read after that lookup differs
from the stamp its decision observed. git's own repository setup reads `.git/config` without following includes. So
git still calls the target untracked after an included `core.worktree` edit, and the stamp comparison is what keeps
the file.

Blocker 2: when no repository could be found, the fallback recheck returned success. `CheckpointOutputLedger.Remove`
now records the owning repository's identity when it starts: the checkout root, `.git`, the git directory and the
common directory, each with its kind and filesystem id. `requireRepository` (passed when `AllowOutsideRepository` is
false) refuses the removal when there is no such repository. Each entry re-verifies the identity before its decision
and again as the last observation before its unlink. An unavailable or changed repository keeps the entry. Outputs
deliberately placed outside every repository keep their separate policy.

## Verification design

- V-1 (blocker 1): `included_config_core_worktree_edited_before_unlink_leaves_the_file_in_place` runs the real
  `CleanOwnedOutputs` on the native filesystem with `include.path = extra.config`. Just before the target's unlink,
  after its in-process decision and content hash, the included file gains `core.worktree = <output>/d`. That setting
  maps the committed root `target.dll` onto the owned target. Expected: the target survives and the refusal names
  it, while the two untracked owned siblings are removed. Red at `57cfe7ac34304d8bfffcb65996826bf089e9f2d8` (the
  target was unlinked).
- V-2 (blocker 2): `discovered_repository_gone_before_final_check_leaves_the_file_in_place` runs the real
  `CleanOwnedOutputs` over `ForwardingNativeIo`, so every check goes to git. Just before the target's unlink, the
  target is staged and `.git` is moved away. Expected: the target survives, the refusal names `repository`, the
  siblings are removed, and no in-process index reads happen. Red at `57cfe7ac`.
- V-3: every earlier witness stays green:
  - round 1: staging during the read, staging before the unlink, the equal-size rewrite, repository-config
    `core.worktree` and linked-worktree `config.worktree`;
  - round 2: git-fallback staging during the hash;
  - both reuse witnesses.

  `config_include_reads_the_index_for_every_entry` now expects two index reads for the second deep entry: one for the
  decision and one for the lookup after validation. An include no longer binds anything.
- V-4 (manual): re-measure the production `CleanOwnedOutputs` on 2,190 files in 40 directories (856,657,920 bytes),
  in a clone that carries this tip's index and has no include. Then run the same driver on 200 files with an include,
  to show what the slow path costs.
- V-5: CP-9 builds the derived-impact driver before CP-10, as in round 2.
- R-1 to R-3 are unchanged from round 2: the named classes, the Final guards (CP-3, CP-4) and the impacted-classes
  selection. This round changes no production file beyond the three already touched and adds no class, so there are
  no new selection rows. There is no migrations row.

PCs stay pending for SourceLanding Mutation. The native Windows rows stay pending until a Windows host runs them.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2052-r3/ | output-cleanup-linux | `/*/*/OutputCleanupTests/(a*)\|(b*)\|(c*)\|(d*)\|(e*)\|(f*)\|(g*)\|(h*)\|(i*)\|(j*)\|(l*)\|(m*)\|(n*)\|(o*)\|(p*)\|(r*)\|(s*)\|(t*)\|(u*)\|(wrapper_*)\|(windows_seal_docs_*)` | V-1,V-2,V-3,R-1 | 56 pass, 0 skipped | 56 | 15 | true | |
| CP-2 | all | CP-1 | named-seven | `/*/*/(CheckpointOwnedOutputTests*)\|(HostCleanupCheckpointExtractionTests*)\|(CheckpointToolCopyCleanupTests*)\|(PreflightCommandLineBudgetTests*)\|(SafeDeleteProcessBoundaryTests*)\|(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | R-1 | >= 100 executed, 0 failed, 0 skipped | 100 | 3 | true | |
| CP-3 | all | CP-1 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(ProcessSpawnLimitTests*)/*` | R-2 | >= 40 executed, 0 failed, 0 skipped | 40 | 8 | true | |
| CP-4 | all | CP-1 | platform-admission-linux | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_*)\|(C1210_Native_*)\|(C1210_Roster_*)\|(C1210_Audit_*)\|(C1210_Host_*)\|(C1226_*)` | R-2 | >= 6 executed, 0 failed, 0 skipped | 6 | 4 | true | |
| CP-5 | all | CP-1 | impacted-cleanup | `/*/*/(AnchoredPathDeleteWitnessTests*)\|(C1307ToolRemovalTests*)\|(CheckpointBuildBindingPathTests*)\|(CheckpointExecutorLogTests*)\|(CheckpointLargeStampTests*)\|(CheckpointOrFilterTests*)\|(CheckpointProcessIdentityTests*)\|(CheckpointSelectedOutputTests*)\|(CheckpointStatePublicationTests*)\|(CheckpointTaskOwnershipTests*)\|(HostCleanupCheckpointAuthorityTests*)\|(ReapedProcStatIdentityTests*)\|(TempRootSweepAnchoredDeleteTests*)\|(UnsafeDeleteRecoveryTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointShadowCustodyTests*)/*` | R-3 | >= 60 executed, 0 failed, 0 skipped | 60 | 8 | true | TMPDIR=/tmp |
| CP-6 | all | CP-1 | evidence-folder-linux | `/*/*/EvidenceFolderTests/(red_run_*)\|(rerun_text_*)\|(green_run_*)\|(non_image_*)\|(host_and_git_*)\|(git_snapshot_*)` | R-3 | >= 6 executed, 0 failed, 0 skipped | 6 | 3 | true | TMPDIR=/tmp |
| CP-7 | all | CP-1 | card-privacy-git-linux | `/*/*/CardFilePrivacyGitTests/(Managed_*)\|(Unmerged_*)\|(Subdirectory_*)\|(Git_operation_*)\|(Literal_NUL_*)\|(AutoCommit_*)\|(Index_only_*)\|(Changed_generated_*)\|(Tracked_stale_*)\|(Containment_*)` | R-3 | >= 10 executed, 0 failed, 0 skipped | 10 | 3 | true | |
| CP-8 | all | CP-1 | rc-bridge-linux | `/*/*/RcBridgeProbePlatformTests/(Linux_probe_*)\|(Forced_*)\|(Docker_*)\|(Agent_*)\|(Session_*)\|(Health_*)` | R-3 | >= 6 executed, 0 failed, 0 skipped | 6 | 3 | true | |
| CP-9 | all | n/a | derived-impact-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --verbosity minimal` | V-5 | exit 0 | n/a | 5 | true | |
| CP-10 | all | CP-1 | impacted-selection | `/*/*/(CheckpointAdmissionContractTests*)\|(CheckpointImportTests*)\|(DerivedImpactIndexTests*)\|(IncrementalSelectionSoundnessTests*)\|(LandGeneratedArtifactTests*)\|(OuterRowBuildStampTests*)\|(PhaseJournalKillDiagnosticTests*)\|(RcInputManifestTests*)\|(RcInputOwnersMapTests*)\|(ReleaseImpactEnvelopeTests*)/*` | R-3,V-5 | >= 60 executed, 0 failed, 0 skipped | 60 | 12 | true | |
| CP-11 | all | CP-1 | impacted-runner | `/*/*/(RowRunnerTests*)\|(WaitCommandTests*)/*` | R-3 | >= 10 executed, 0 failed | 10 | 3 | true | |
| CP-12 | all | CP-1 | impacted-source-script | `/*/*/RunCheckpointSourceScriptTests*/*` | R-3 | >= 5 executed, 0 failed | 5 | 5 | true | |
| CP-13 | all | tests/Antiphon.Agents.Pty.Tests -> bin-c2052-r3-pty/ | pty-spawn-limit | `/*/*/ProcessSpawnLimitTests*/*` | R-2,R-3 | >= 1 executed, 0 failed | 1 | 8 | true | |
| CP-14 | all | tests/Antiphon.SessionRunner.Tests -> bin-c2052-r3-sr/ | sessionrunner-spawn-limit | `/*/*/ProcessSpawnLimitTests*/*` | R-2,R-3 | >= 1 executed, 0 failed | 1 | 8 | true | |
| CP-15 | all | CP-1 | r3-file-survival | `/*/*/OutputCleanupTests/(included_config_core_worktree_*)\|(discovered_repository_gone_*)` | V-1,V-2 | 2 pass, 0 skipped | 2 | 3 | true | |
