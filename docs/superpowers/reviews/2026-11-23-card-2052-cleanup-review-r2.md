# CARD-2052 round 2 review

**Return to Code.** Owned-output cleanup still accepts an untracked decision with no bound index version.

| Where | Failure | Why it matters | Repair |
|---|---|---|---|
| `TrackedIndexScope.DecisionStillHolds` (line 61), `CheckpointDelete.TrackedInProcess` (line 355), `PreflightRoots` (line 466), `CheckpointOutputLedger.DeleteRecorded` (line 122) | A successful Git fallback can leave the version empty. A successful in-process read can do the same when the stamp is unavailable. The final check accepts an empty version. | In a read-only check on Linux, intent-to-add, skip-worktree, symlinked `.git`, and explicit `core.worktree` all recognized a tracked control and left the untracked decision unbound. Staging during the subsequent content hash changes neither the bytes nor the creation record. This leaves round 1 D-1 open. | Require authoritative evidence for every repository decision that permits an unlink. Recheck the effective index after content validation and retain the entry whenever that check cannot establish its state. Cover one real fallback with a tracked-file survival assertion. |

For a ledger-owned output, I would accept the scheduling gap between a final authoritative Git check and its native unlink. This tip leaves a wider interval. Even on the bound path, the last index-trailer read precedes config filesystem reads, which can retry on Windows, and the native anchored-delete checks. Creation identity and a content digest do not prove a file is still untracked. The evidence does not establish a need to take Git's writer lock.

The repository include and linked common config.worktree witnesses passed. In an empty main worktree with a distinct configured root, Git and libgit2 agreed on that root and the production index reader deferred to Git. The user/system config loop does not inspect include contents for its reuse flag. The checkpoint process isolates those config levels, and the hardened Git child suppresses them. This review found no independent D-3 mapping failure on that production path.

D-2's settled-index and equal-size rewrite controls passed. A read-only check also recognized tracked controls after a same-size/same-mtime index replacement and a checkout change, and in a sparse checkout. A bare repository has no working tree. The four hooks have only internal setters and no production assignment, configuration binding or environment switch.

The author's gated transcript at `a03470bc95f5712b7d18bed5f66d9a7e4221828f` shows 47 passed, six failed and four skipped. Four failures asserted file survival and two asserted re-read counts. The valid green at `59f578da7be9c72b467eb166f84de2ff9e10298b` was 53/0/4. The intervening `db0988e7f` build failed with CS0029 and its stale-binary run provides no evidence. The six assertions remained intact through the reviewed tip.

CP-3's original failure was `TempRootSweepAnchoredDeleteTests.fifo_and_socket_inside_the_root_are_removed`. The fixture rejects socket names at 108 bytes before calling bind or cleanup. The author reran 165 tests green with the published short TMPDIR. This is a deterministic fixture-path limit, independent of the cleanup change.

## Final verification

Reviewed `c0173afa346eb64aa022f80e4a749504e03179d9..aad1b18fe45761cec4667033b727c14a649b115c`. The repair source is Code task `0cefa0d7-1af5-4d9f-a19e-6734185e5929`. The original landing owner is `800d74b2-65f4-45ae-8c4f-5d1e8261ade6`.

Run `20261123-000412-2d88` executed CP-1 through CP-9 on Linux in one checkpoint invocation, with a 100-minute wall cap and a 25-minute row limit. CP-3 used the committed rerun plan's `TMPDIR=/tmp`. All nine filters, projects and floors matched the closed table. The nine distinct TRX runs recorded 721 result identities, 682 executions, 681 passes, one failure and 39 skips. All three builds and all nine rows reported the reviewed SHA, clean source and verified build provenance. The combined validator exited 2 with `report_failed`.

| Row | Passed | Failed | Skipped |
|---|---:|---:|---:|
| CP-1, eight named classes | 154 | 0 | 4 |
| CP-2, Final guards | 148 | 0 | 1 |
| CP-3, impacted cleanup | 165 | 0 | 1 |
| CP-4, impacted platform checks | 20 | 0 | 33 |
| CP-5, impacted selection | 140 | 1 | 0 |
| CP-6, row runner and wait | 25 | 0 | 0 |
| CP-7, checkpoint source script | 25 | 0 | 0 |
| CP-8, Pty spawn limit | 2 | 0 | 0 |
| CP-9, SessionRunner spawn limit | 2 | 0 | 0 |

**CP-5 prerequisite.** `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged` invokes the derived-impact generator with `-NoBuild`. Its default-output DLL was absent on the fresh checkout. The same fixture command failed the same way in an archive of the round-1 SHA. Code's transcript shows a separate gated regeneration before its green CP-5. After a gated helper build, the exact method passed 1/1 on the original bound CP-5 test binary. Receipt `CP-5-prerequisite-20261123-002928-2ffd` validated. Make the helper build an explicit, gated prerequisite in the plan.

**Native evidence.** CP-3's held-file retry and 26 CP-4 identities are absent from the reviewed SHA's Windows skip roster. The 26 comprise 14 in `CheckpointRecoveryWindowsTests`, seven in `CheckpointWindowsAnchoredDeleteTests` and five in `CheckpointWindowsAnchoredPathDeleteWitnessTests`. Those three classes and `CardFilePrivacyPathTests` executed zero cases on Linux. Qualify the skips and supply the missing native Windows executions. All PCs remain pending for SourceLanding Mutation.

**Performance.** The 46-entry read-count regression passed. In a separate scratch checkout at the same source SHA with 11,436 index entries, production cleanup removed all 2,190 generated file copies (855,536,640 bytes, 40 subdirectories) in 38.868 s, with seven in-process index reads and zero refusals. This is a controlled fixture. The author's 49.046 s and 43.909 s are observations on built outputs. The older 369.22 s baseline came from a different run. The nine-row Review took 24m47s, including 75.04 s of admission-only waiting. Its red result retained outputs, so its 0.113 s finalization phase does not measure removal. The author's original 31m54s run crosses the performance standard's 30-minute Debug threshold.

**Scope and provenance.** The impact selector's 45 main-project classes, two additional project filters, platform-admission method and capped names are covered by the plan. The downgrade selector matched 58 tracked test source files across the repository and selected **zero migration rows** for the candidate. `scripts/antiphon evidence-diff` against `ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91` found 12 commits, one evidence entry and zero violations. Merge-tree against fetched master `b0473c5245c474755a3e69922cdfe0186d15e4a7` returned `a80443253dc81cc5fc2b1d801e1752e48ecac72e` without conflicts. The diff changes cleanup, its tests and skip roster, the plans, and Code's report.

The additional gated commands built the checkpoint tool and evidence-diff launcher, observed native Git layouts without deleting a target, timed a scratch cleanup because the red run retained its outputs, and isolated CP-5's missing dependency. Exploratory invocation failures before a product result were excluded from the counts. The only tracked Review change is this note.

<details>
<summary>Checkpoint lines from the reviewed tip</summary>

```text
CHECKPOINT CP-1 commit=aad1b18fe45761cec4667033b727c14a649b115c build=ok filter=/*/*/(OutputCleanupTests*)|(CheckpointOwnedOutputTests*)|(HostCleanupCheckpointExtractionTests*)|(CheckpointToolCopyCleanupTests*)|(PreflightCommandLineBudgetTests*)|(SafeDeleteProcessBoundaryTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointCompatibilityTests*)/* executed=154 passed=154 failed=0 skipped=4 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=25.120s wall=25.628s
CHECKPOINT CP-2 commit=aad1b18fe45761cec4667033b727c14a649b115c build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(TestFixtureCleanupGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(ProcessSpawnLimitTests*)/* executed=148 passed=148 failed=0 skipped=1 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=136.428s wall=137.147s
CHECKPOINT CP-3 commit=aad1b18fe45761cec4667033b727c14a649b115c build=reused filter=/*/*/(AnchoredPathDeleteWitnessTests*)|(C1307ToolRemovalTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointExecutorLogTests*)|(CheckpointLargeStampTests*)|(CheckpointOrFilterTests*)|(CheckpointProcessIdentityTests*)|(CheckpointSelectedOutputTests*)|(CheckpointStatePublicationTests*)|(CheckpointTaskOwnershipTests*)|(EvidenceFolderTests*)|(HostCleanupCheckpointAuthorityTests*)|(ReapedProcStatIdentityTests*)|(TempRootSweepAnchoredDeleteTests*)|(UnsafeDeleteRecoveryTests*)|(CheckpointReceiptSafetyTests*)|(CheckpointShadowCustodyTests*)/* executed=165 passed=165 failed=0 skipped=1 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=120.522s wall=121.202s
CHECKPOINT CP-4 commit=aad1b18fe45761cec4667033b727c14a649b115c build=reused filter=/*/*/(CheckpointRecoveryWindowsTests*)|(CheckpointWindowsAnchoredDeleteTests*)|(CheckpointWindowsAnchoredPathDeleteWitnessTests*)|(CardFilePrivacyGitTests*)|(CardFilePrivacyPathTests*)|(RcBridgeProbePlatformTests*)/* executed=20 passed=20 failed=0 skipped=33 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=27.614s wall=28.333s
CHECKPOINT CP-5 commit=aad1b18fe45761cec4667033b727c14a649b115c build=reused filter=/*/*/(CheckpointAdmissionContractTests*)|(CheckpointImportTests*)|(DerivedImpactIndexTests*)|(IncrementalSelectionSoundnessTests*)|(LandGeneratedArtifactTests*)|(OuterRowBuildStampTests*)|(PhaseJournalKillDiagnosticTests*)|(RcInputManifestTests*)|(RcInputOwnersMapTests*)|(ReleaseImpactEnvelopeTests*)/* executed=141 passed=140 failed=1 skipped=0 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=399.058s wall=399.402s
CHECKPOINT CP-6 commit=aad1b18fe45761cec4667033b727c14a649b115c build=reused filter=/*/*/(RowRunnerTests*)|(WaitCommandTests*)/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-6/run.trx slot=granted waited=30s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=30s executed=11.613s wall=42.109s
CHECKPOINT CP-7 commit=aad1b18fe45761cec4667033b727c14a649b115c build=reused filter=/*/*/RunCheckpointSourceScriptTests*/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-7/run.trx slot=granted waited=15s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=15s executed=136.747s wall=152.225s
CHECKPOINT CP-8 commit=aad1b18fe45761cec4667033b727c14a649b115c build=ok filter=/*/*/ProcessSpawnLimitTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-8/run.trx slot=granted waited=30s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=30s executed=4.567s wall=35.120s
CHECKPOINT CP-9 commit=aad1b18fe45761cec4667033b727c14a649b115c build=ok filter=/*/*/ProcessSpawnLimitTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/20261123-000412-2d88/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=7.646s wall=8.443s
wall: 24m47s  sequential-equivalent: 15m49s  builds: 3  max-concurrent-builds: 1  rows: 8 green 1 red 0 skipped
verdict: RED exit=1
CHECKPOINT CP-5-prerequisite commit=aad1b18fe45761cec4667033b727c14a649b115c build=reused filter=/*/*/DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-02-d4/.antiphon/checkpoints/CP-5-prerequisite-20261123-002928-2ffd/run.trx slot=granted waited=0s dirty=0 source=aad1b18fe45761cec4667033b727c14a649b115c sourceState=clean buildSource=verified
```

</details>
