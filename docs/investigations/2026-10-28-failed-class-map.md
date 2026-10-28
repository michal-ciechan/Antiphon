# Failed-class map: run 8f055f87 -> rc/2026-10-27 f84228f2 -> live run 79b742de

Task a4a62906, 2026-10-28, Investigate (read-only). No test, build or process-spawning command was run;
the candidate directories, `native-run.lock` and the live supervisor were not touched. Times are UTC.

Sources: failed run `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`
(`summary.json`, 333 chunk TRX files, chunk `*-tests.log`, `*-execution\*.diag`, `timing\groups-executed.json`);
live run `...\rc-2026-10-27-f84228f285dd\logs\2026-10-27-2215-79b742de` and its timing root
`...\logs\timing-79b742de44354190bb55fd35914496f3` (`groups-planned.json`, `groups-executed.json`);
`C:\Antiphon\releases\task-1873094e\supervisor-heartbeat.json`; `git log 105792f7d22f..origin/rc/2026-10-27`
(107 commits); native rerun report `docs/investigations/2026-10-27-native-rerun-scripts-checkpoint-release-gate.md`
on `origin/feat/card-task-fb80be16` (native Windows, master 718bd29, lane-equivalent to rc d1d090f7);
`docs/investigations/2026-10-27-scripts-lane-product-repairs.md` and `...-unsafe-delete-and-replace-recovery.md` on rc.

## 1. What failed in 8f055f87

Started 2026-10-26 09:46, ended 2026-10-27 04:43 (68,220 s), outcome `TESTS`. Client (1,841 Vitest) and the scripts
suite passed. TRX holds 137 Failed rows across 62 classes. The two timed-out chunks have no TRX; their console logs add
3 failed cases in 3 classes, so the total is **140 failed cases in 65 classes**, matching the brief.

Failed classes. Format: count class [8f055f87 chunk] first failure message, abbreviated.

- E2E: AgentE2ETests 1 [e2e g5] terminal locator not visible · AgentTaskLandDeliveryE2ETests 2 [e2e s2,s34]
  `Landed` vs `LandedWithResidue` · BoardE2ETests 1 [e2e s39] locator not visible · DispatchBaseWarningDeliveryE2ETests 2
  [e2e s44,s45] `C508_MismatchWarning*` timeout · PipelineGlanceE2ETests 1 [e2e g9] 565 > 391 px ·
  WorkflowDeleteTests 2 [e2e s61] navigation timeout
- Agents.Pty: FakeClaudeContractTests 1 [g1] `--append-system-prompt` quoting
- SessionRunner: CompletedCardWorkspaceRemovalTests 4 [g1] UnauthorizedAccess on fence path ·
  DaemonStartupDiagnosticsTests 6 [g2] timeout · GrokRulesHttpAcceptanceTests 1 [g4] 500 ·
  WorkspaceSourceVerificationWireTests 1 [g4] repository identity hash (separator)
- Application: DispatcherSiblingInspectionSchedulingTests 1 [g19] · LandGraphEvaluationTests 1 [g26] audit exit 1 ·
  SessionRuntimeActionTests 1 [g29] timeout · TerminalRunnerSeatReleaseTests 1 [g32] `landing.lock` in use ·
  HerdrAlwaysOnChannelParityTests 2 [g34] queued message Pending · CardFilePrivacySyncTests 1, RemoteControlRecoveryPtyIntegrationTests 2,
  SessionMessageQueuePtyIntegrationTests 5 [g35] · CodexCliObservationGapTests 1 (grok remote receipts),
  SessionQueueReceiptPlumbingTests 9 [g36] · ChannelOutboundRecoveryTests 2 [g37] file sharing IOException ·
  SessionMessageQueueGrokPtyIntegrationTests 1 [g38] · AgentTaskStandingAgentDispatchTests 1 [g42] DbUpdateException ·
  GrokDelegateEndToEndTests 3, PhoneHomeCompletedCardTransportTests 1 [g49] · AgentTaskPipelineEndpointTests 1 [g51] ·
  DelegationBriefCeilingPtyTests 2, GitWorkspaceServiceIndexLockTests 1 [g54] · GrokSignInRuntimeTests 1 [g58] canceled ·
  LandNonCodeSkipTests 12 [split 14-16] verifier called / SDK record
- Checkpoints: CheckpointConfinementLedgerTests 3, CheckpointRecoveryWindowsTests 6, OutputCleanupTests 3 [g61] ·
  BaselineComparerTests 2, CheckpointAdmissionContractTests 1, CheckpointAppTests 1, CheckpointBuildBindingPathTests 3,
  CheckpointTempUsageTests 2 [g62] · C1307CliCancellationTests 5, CheckpointRepeatBuildIsolationTests 1,
  CheckpointToolSourceTests 1, EvidenceFolderTests 2, PlanCoverageBindingTests 2, RerunPolicyTests 4, RunStateStoreTests 1 [g63].
  Most of these failed with `receipt-read-refused ... read-stalled`.
- Infrastructure: LandingGitTests 1 (C1076 slow-push budget), RepositoryChildRecoveryTests 1 (C452_R2),
  WorktreeManagerGitIntegrationTests 1 (C1502, `\` vs `/`) [g65, console log only] · AmServiceDeployReadinessTests 1,
  HostCleanupCheckpointExtractionTests 1, WorktreeRemovalAuthorityTests 1 [g66]
- Scripts: RollingVolumeRecycleScriptTests 4 [g71] · EvidenceDiffGuardTests 1 [g74] · ScriptHarnessWindowsOwnershipTests 1 [g75] ·
  C1008PlatformContractTests 2, CardRelationshipScriptTests 1, ReleaseGateRunTests 2, RunCheckpointRepeatScriptTests 2 [g76] ·
  ChangeAwareReleaseTests 1, RcQualificationStatusTests 1, ReleaseGatePlatformTests 1, WindowsUnitPlatformAdmissionTests 2 [g77] ·
  ReleaseGateFollowupTests 1 [g78] · RemoteScriptContractTests 7 [split 21,22,24]

**Required cases without terminal evidence.** I could not reproduce the figure of 179 from the stored evidence. What the
evidence does show:
- `summary.json` `unexecuted-outcome` reasons: 527 NotExecuted rows, which are 264 unique methods in 49 classes across 33 chunks.
  By class (unique methods / rows): RemoteScriptContractTests 40/234 [split 18-21,24]; HostJqPrerequisiteScriptTests 39 [g75];
  RetiredTempContainerHostTests 21, ScriptHarnessLinuxOwnershipTests 21 [g74]; CheckpointShapeConflictTests 2/17,
  CheckpointTrackedBaselineTests 13, OutputCleanupTests 11, CheckpointBoundedReaderTests 2/11, CheckpointRecoveryLinuxTests 10,
  CheckpointStampSafetyTests 1/10, CheckpointFixtureRoutingTests 1/4, CheckpointInheritedFileTests 1/4,
  CheckpointConfinementLedgerTests 1, CheckpointOwnedOutputTests 1, CheckpointReceiptSafetyTests 1 [g61];
  CheckpointOutputConfinementTests 5, PlanCoverageHandleTests 2/5, CheckpointScriptReaderDeadlineTests 1/2,
  CheckpointSourceGitBoundsTests 2, CheckpointVerifierBoundsTests 1/2, PlanCoverageCommandTests 1 [g62];
  CheckpointFixtureProcessTests 2, SafeDeleteProcessBoundaryTests 2, DetachedLauncherTests 1 [g63];
  JqRunnerImageContractTests 5/16, GrokSha256PinTests 11, GithubCredentialHelperTests 6 [g66];
  CodexRootExecContractTests 2, GitMinimumVersionGuardTests 1 [g67]; RollingProductionMountTests 12, C1364BoundLandWaitProofTests 3 [g76];
  RefreshGithubTokenRemoteCommandTests 3, RetiredTempContainerDockerTests 1, RollingVolumeRecycleDockerTests 1 [g77];
  RollingVolumeRecycleCensusChurnTests 1, NightlyProbeSupervisorTests 1 [g78]; RollingVolumeRecycleScriptTests 4 [g71];
  RetiredTempContainerScriptTests 3 [g73]; BuildSlotScriptTests 1 [g75]; PhoneHomeMirrorJoinTests 6 [g37];
  PhoneHomeCompletedCardTransportTests 1 [g49]; LandGraphEvaluationTests 1 [g26]; UnixDelegateLaunchArgvTests 1/2 [g57];
  ClaudeDialogWatchdogCanaryTests 1 [g3]; UnixPtyArgvTests 5/17 [agents-pty g1]; LinuxCgroupCustodyTests 8 [pty-host g1];
  SessionRunner CompletedCardWorkspaceRemovalTests 2/3 [g1], RunnerWorkspaceBindingTests 1 [g2], HostStatsProbeLiveTests 1 [g4].
  Most are named for Linux, Unix, Docker, bash or FIFO behaviour.
- The two timed-out chunks have no TRX at all (`missing-trx`). Unfinished cases: g28, 12 in progress at kill plus about 245
  never started (the live plan has 704 for g28); g65, 24 in progress plus about 127 never started (live plan has 975).

**Timed-out chunks** (both `exitCode 1`, `timedOut true`, `trxMissing true`):

| Chunk | Classes (treenode filter) | Ran | State at timeout |
|---|---|---|---|
| antiphon all-group-28 | Application: AgentCreateRace, AgentTaskServiceIntegration, ApiErrorRecoveryService, BlockedTaskParkDelivery, BlockedTaskParkReclaim, ChannelBridge, ChannelInboundRecovery, CodexStartupDelivery, DelegateBranchDelivery, HerdrLabelFollow, LandEvidenceWarningDelivery, OutputDistillationGate, PostLandMutationCustody, RepairSourceDispatch, RepairSourceSettlement, SessionRuntimeActionRouting, TaskInputAttentionCost (17) | 13:24:57 → 14:24:55, 3,606.5 s | 447 passed in the first 17 s. 11 tests (HerdrLabelFollow ×2, OutputDistillationGate ×8, AgentTaskServiceIntegration `OnAgent_defaults_stage_to_FollowUp...`) went InProgress by 13:25:14 and never finished. After that the diag logs no progress for 59 min; the next event is one ChannelBridgeTests start at 14:24:55, the second before the kill. Console log is empty past its header. This is a wedge, including pure-logic tests, not slow progress. |
| antiphon all-group-65 | Infrastructure: CompletedCardWorktreeRemoval, DockerDelivery{Barrier,Database,Observation}, DockerStackContract, FileUseCapture, GitService, GrokRunnerImageContract, HostCleanupCheckpoint{Authority,Compatibility,Custody}, HostCleanupOwnership, Landing{AdmissionControl,GitProfile,Git,IdentityControl,RemovalControl,RemovalPolicyControl,SourceBoundaryControl,SourceFreshness}, LandRecovery{Checkout,IgnoredNestedRepository}, RepositoryChildRecovery, SettledWorktreeRemoval, WorktreeDeleteAccessProbe, WorktreeGuardedCleanup, WorktreeLockDiagnostics, WorktreeManagerGitIntegration (28) | 19:06:47 → 20:06:45, 3,607.9 s | 810 passed, 11 skipped, 3 errors (above). 24 tests stayed InProgress, mostly git/landing tests started 19:08-19:13 (GitService ×3, HostCleanupCheckpointAuthority ×2, LandingAdmissionControl ×5, LandingGit ×5, LandRecoveryCheckout ×2, LandRecoveryIgnoredNested ×3, LandingRemovalControl, LandingSourceFreshness ×2 started 19:25). No progress 19:26:37 → 20:06:04 (39.5 min). Then WorktreeManagerGitIntegration resumed and was mid-test at the kill. This is slow progress with stuck git children, not a total wedge. |

## 2. Fix status on rc/2026-10-27 f84228f2

Legend:
- **N**: the fix is on rc and a native Windows pass is recorded. fb80be16 ran on the lane-equivalent master 718bd29; "live" means the 79b742de TRX.
- **P**: a fix is on rc, but no native result exists after it. The live run gives the first one.
- **L**: the fix was verified on Linux only, as its report says. Native confirmation is still owed.
- **O**: obsolete fixture.
- **E**: no rc commit, but the case passed natively in fb80be16. Likely environmental or transient.
- **OPEN**: no fix on rc.

Commit platform is not recorded in commit bodies. "P" means only that no native result exists.

| Class | Status | Commits on rc (105792f7..f84228f2) |
|---|---|---|
| DispatcherSiblingInspectionSchedulingTests | N (live g19 232/232 passed) | 3d345b419 |
| C1307CliCancellation, CheckpointAdmissionContract, CheckpointApp, CheckpointBuildBindingPath, CheckpointToolSource, EvidenceFolder, RerunPolicy, PlanCoverageBinding | N (fb80be16: 15/17 read-stall cases pass) | ee48cd2e4 reader overlapped IO (+ d99601cda, 1bb97168a, fb3b5d271) |
| BaselineComparer, CheckpointTempUsage, CheckpointConfinementLedger | N (fb80be16) | c7cf98647/c5abebfd9; 763f9f8f0/855ef3bb3/a4cd1612b |
| GitWorkspaceServiceIndexLock, HostCleanupCheckpointExtraction | N (fb80be16) | 0aa7bd4a7/20470a9b5; db1929daf (+3d385f2a9 receipt count, Linux) |
| ScriptHarnessWindowsOwnership, RunCheckpointRepeatScript, EvidenceDiffGuard, AmServiceDeployReadiness | E (fb80be16 pass) | none |
| RunStateStoreTests | L (356abafc, 74632d02 Linux; fb80be16 natively still red before the fix) | 454103717, 4b10ed879, d42f1574c |
| WorktreeRemovalAuthorityTests | L (356abafc) | 767224270 bin-* custody |
| ReleaseGateRun, ReleaseGateFollowup, ChangeAwareRelease, RcQualificationStatus, ReleaseGatePlatform | L for the escaped-origin product fix; P for the pwsh WindowsApps-alias harness fix | 3bf2c6df2, 697d6009b (origin); e4dc37dac (ScriptHarnessProcess.cs) |
| — residual: ChangeAwareRelease C1313_BaselineAuthority | OPEN (pipe-drain timeout unresolved per 356abafc) | — |
| — residual: RcQualificationStatus | OPEN (fb80be16 installed control still failed directory-timestamp inventory) | — |
| CheckpointRecoveryWindowsTests | P (fb80be16 still 9/6/0 before these; one staged-copy reader stall unresolved) | e4dc37dac, f3e302eb9, 4b10ed879 custody |
| OutputCleanupTests | P for the `/bin/false` case; OPEN for concurrency git-timeouts (serial 38/0, parallel 3-18 fail) | c1a710211 |
| CheckpointRepeatBuildIsolationTests | OPEN (`output-inventory-failed`, exception masked) | — |
| LandNonCodeSkip, LandGraphEvaluation | P (land-graph SDK provenance top-up after fb80be16). LandGraph's 120 s real-solution timeout is OPEN | 2d85434c8, 1daa1232f, c68fc2bfd, e7622789f, 5485bb701, d67dbecff, f0fecaa13, fd979f1e8, 4e9fb836c, 17e600530; e4dc37dac |
| CardRelationshipScript, RollingVolumeRecycleScript, RemoteScriptContract | P (fb80be16 still red before e4dc37dac) | e4dc37dac |
| WindowsUnitPlatformAdmissionTests | P | a41358a47 roster (+952012984, 1e75ee6ed) |
| C1008PlatformContractTests | OPEN (checkpoint source stamp absent; no commit) | — |
| E2E AgentE2E / PipelineGlance / Board / WorkflowDelete / AgentTaskLandDelivery | P | 6647f8db3+3c3c05059 / a3df02c2b+d1d090f76 / 9f00008fa / 57f0c26c4 / b9888dc0c+f84228f28 |
| E2E DispatchBaseWarningDeliveryE2ETests (C508_MismatchWarningReachesCaller, ...WaitsForCaller) | O (C508 capacity expectations obsolete; CARD-1593 capacity admission not in rc) | none |
| SessionRunner CompletedCardWorkspaceRemoval / DaemonStartupDiagnostics / GrokRulesHttpAcceptance / WorkspaceSourceVerificationWire | P | fac80d23a+a614e2066+71f618a7e / 05e7d78cd+4e2feaa93 / 3d345b419 / a2aadd0c9+3788b0d79 |
| AgentTaskPipelineEndpoint, AgentTaskStandingAgentDispatch, CardFilePrivacySync, ChannelOutboundRecovery | P | a73a7848f+3d345b419, cb44359ef, 08165415d, 3e319ca1d+0a97b42d8 |
| GrokDelegateEndToEnd, GrokSignInRuntime, SessionQueueReceiptPlumbing | P | ac9bee88f+2a1dc8b79+3d345b419; 3d345b419+ea1ff2531/b3860c5f9; 2a1dc8b79+50837d623+71f618a7e |
| HerdrAlwaysOnChannelParity, RemoteControlRecoveryPty, SessionMessageQueuePty, SessionMessageQueueGrokPty, DelegationBriefCeilingPty | P | 5db3ee2e2+71f618a7e; 71f618a7e; 71f618a7e+8d13108e1; 71f618a7e; 8d13108e1 |
| CodexCliObservationGapTests | P (inferred: its fixture `CodexCliRemoteDeliveryFixture` got a POSIX rules path) | 72ed08d5b (+bae511d59) |
| PhoneHomeCompletedCardTransport, TerminalRunnerSeatRelease | P | 7fc47ff72; fde037c2d (joins the worker that held `landing.lock`) |
| FakeClaudeContractTests | OPEN (rc only adds a new portable witness, +52 lines; the failing quoting assertion is unchanged) | 311f6853a, 838fbcad0 |
| SessionRuntimeActionTests | OPEN (timeout; no commit) | — |
| LandingGitTests C1076, RepositoryChildRecoveryTests C452_R2, WorktreeManagerGitIntegrationTests C1502 | OPEN (no commit; C1502 is a `\`/`/` path mismatch) | — |
| Timed-out g28 (17 classes) | OPEN, cause unproven. Candidate: the sign-in-incident FK lock cycle fix 119311065/bff189ae5 (a DB lock wait would explain a wedge). No evidence ties them | — |
| Timed-out g65 (28 classes) | OPEN (stuck git children, cause unknown) | — |

Unexecuted-outcome classes have no class-specific fix, except PhoneHomeCompletedCardTransport (7fc47ff72) and OutputCleanup.
In the live run, ClaudeDialogWatchdogCanaryTests.C680 is again NotExecuted ("Native watchdog qualification is explicitly opt-in",
g3), so that evidence reason recurs. The qualification timing recorder fixes (31ff14b31, 926fceb6e, 1a5b7c59c, d8de8e6c5,
06f05a97e) and the CRLF selector pin (bf9bb8bb4) are runner-side and attach to no class.

## 3. Live run 79b742de: plan, placement, ETA

Heartbeat at 00:20:40: `hangSuspected:false`, idle 0, 19 Antiphon.Tests TRX. Started 22:15:30, build done 22:23,
group 1 started about 22:26. The first 19 chunks have no Failed rows (1,981 passed, 1 NotExecuted: C680 above). Group 20 has been
running since 00:15:43. `groups-planned.json` currently holds only the **antiphon** suite: 181 chunks (g1-81, split 1-27,
profile 46/47/152-216/1470-1476), 59,054 planned seconds, `requiredPartitionExact true`, `missingIds []`. The planner rewrites
this file per suite; in 8f055f87 the final file held only observability. So the session-runner, pty-host, agents-pty, e2e
and markdown-pdf placement is not readable yet. In 8f055f87 the suites ran in this order: antiphon (09:58 → 00:18 next day),
session-runner (00:30-00:51), pty-host (00:51-01:05), agents-pty (01:07-01:10), messaging, e2e (01:21-04:39),
markdown-pdf, observability. That totals about 4.4 h after antiphon.

The chunk numbering matches 8f055f87 for the failed groups, with one exception: LandGraphEvaluationTests is now in g24, not g26.
**Every previously failed Antiphon.Tests class and every class of the two timed-out chunks is in the plan.** None is missing.

ETA method. Each remaining chunk's duration is its 8f055f87 wall time for the same chunk name, starting from g20 at 00:15:43.
- **A** uses those times as they are.
- **B** scales them by 2.08, the measured ratio for g1-19 (live 6,521 s vs prior 3,137 s), and caps each chunk at its 3,610 s timeout.

The live run is so far running about 2× slower than 8f055f87.

| Live chunk | Previously failed classes it contains | First native result starts ~ (A / B) |
|---|---|---|
| g19 | DispatcherSiblingInspectionScheduling | done: passed |
| g24 | LandGraphEvaluation | 00:46 / 01:20 |
| g28 | 17 timed-out classes (repeat-hang watch: >60 min means timeout) | 01:59 / 03:39 |
| g29 | SessionRuntimeAction | 02:59 / 04:39 |
| g32 | TerminalRunnerSeatRelease | 04:26 / 06:45 |
| g34 | HerdrAlwaysOnChannelParity | 04:58 / 07:52 |
| g35 | CardFilePrivacySync, RemoteControlRecoveryPty, SessionMessageQueuePty | 05:08 / 08:12 |
| g36 | CodexCliObservationGap, SessionQueueReceiptPlumbing | 05:18 / 08:35 |
| g37 / g38 | ChannelOutboundRecovery / SessionMessageQueueGrokPty | 05:46 / 09:32 · 05:53 / 09:46 |
| g42 | AgentTaskStandingAgentDispatch | 06:11 / 10:25 |
| g49 | GrokDelegateEndToEnd, PhoneHomeCompletedCardTransport | 06:51 / 11:48 |
| g51 / g52 / g54 / g58 | AgentTaskPipelineEndpoint / GitWorkspaceServiceIndexLock / DelegationBriefCeilingPty / GrokSignInRuntime | 06:59-07:08 / 12:04-12:24 |
| g61 | CheckpointConfinementLedger, CheckpointRecoveryWindows, CheckpointTempUsage, OutputCleanup | 07:10 / 12:28 |
| g62 | BaselineComparer, CheckpointAdmissionContract, CheckpointApp, CheckpointBuildBindingPath | 07:24 / 12:55 |
| g63 | C1307CliCancellation, CheckpointRepeatBuildIsolation, CheckpointToolSource, EvidenceFolder, PlanCoverageBinding, RerunPolicy, RunStateStore | 07:31 / 13:11 |
| g65 | LandingGit, RepositoryChildRecovery, WorktreeManagerGitIntegration + 25 other timed-out classes | 07:35 / 13:18 |
| g66 | AmServiceDeployReadiness, HostCleanupCheckpointExtraction, WorktreeRemovalAuthority | 08:35 / 14:19 |
| g71 | RollingVolumeRecycleScript (1-class chunk, planned 1,933 s) | 08:43 / 14:36 |
| g74 / g75 | EvidenceDiffGuard / ScriptHarnessWindowsOwnership | 09:26 / 16:06 · 09:36 / 16:25 |
| g76 | C1008PlatformContract, CardRelationshipScript, ReleaseGateRun, RunCheckpointRepeatScript | 09:50 / 16:55 |
| g77 / g78 | ChangeAwareRelease, RcQualificationStatus, ReleaseGatePlatform, WindowsUnitPlatformAdmission / ReleaseGateFollowup | 10:13 / 17:42 · 10:31 / 18:21 |
| split 12-18 | LandNonCodeSkip | 11:35 / 20:32 |
| split 19-27 | RemoteScriptContract | 12:07 / 21:39 → 12:21 / 22:08 |
| later suites (not yet planned) | SessionRunner ×4 classes, Agents.Pty FakeClaudeContract, E2E ×6 classes | A: SessionRunner ~13:00, E2E ~13:50-17:10. B: SessionRunner ~22:50, E2E ~23:40 → about 07:00 on 10-29 |

Caveats. Chunk membership can differ between runs under the same name, so durations are approximate. B puts antiphon at about 22:40
and the whole run near 03:00-07:00 on 10-29. That is past the memory's "about 24 h reserve" from 22:15. Whether the supervisor or
the chunk policy has a whole-run deadline was not checked.

## Not done, noted

- Fix idea, one line: g28's wedge after 17 s across unrelated pure-logic classes points to a shared-fixture or DB lock wait; the
  live g28 diag is the place to capture lock or stack state if it wedges again.
- The 179 figure stays unreconciled. A search of `docs`/`.antiphon` on `origin/feat/card-task-7e719c57` did not find it, and
  the orchestrator digest that produced it was not located.
