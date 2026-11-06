# Task aa3c54ff: rc/2026-11-08 cut from master (NOT CUT)

Outcome: I did NOT cut rc/2026-11-08. The tree check is red: 8 landing tests fail at master df1fc145181a3bedf444f9113f822f30d3b136f6.
The failure is platform-independent, so the Windows rc run would fail too. A1-A6 all pass.

## Pin

- master = df1fc145181a3bedf444f9113f822f30d3b136f6. I checked `git ls-remote` at 13:29 UTC and the pin was unchanged throughout.
- If the branch had been cut, the candidate id would be rc-2026-11-08-df1fc145181a. The name validates: `Test-ReleaseGateCandidateRef` returns Ok, Kind long-lived.
  No script refuses the name. release-cut.ps1 and release-candidate.ps1 use `^rc/\d{4}-\d{2}-\d{2}$`, and rc-qualification-status.ps1 does no ref validation.

## A-series

- A1: `git ls-remote origin refs/heads/rc/2026-11-08` is EMPTY.
- A2: rc/2026-11-07 (1038d07e6), rc/2026-11-06 (5a95ae7a5) and rc/2026-11-05 (147034055) are all ancestors of the pin. `git cherry` printed 0 lines, so there are no '+' commits.
- A3: policyHash stored 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72 equals recomputed 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72; Read-NightlyExecutionPolicy ok.
  PinnedInputDigest 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939 equals the `-PrintInputDigest` output 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939.
- A4: `audit-land-verification-graph.ps1 -Check` with the Debug evaluator dll printed `approved` (exit 0). digest=D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, count=69, 5 not-built. There was no mismatch. I did not run -Update.
- A5: `test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy` 6/6 PASS; `-Case C1686_LinuxOnlyCensus` 62/62 PASS.
- A6: `test-rc-coverage-admission.ps1` 7/7 PASS. The harness defines exactly 7 Assert-C487 rows, not 8, and the file is unchanged since 1038d07e6.
  `git diff --stat 5a95ae7a5..pin -- scripts/lib` is EMPTY: scripts/lib has not changed at all since that candidate.

## Tree verification (build df1fc145, Linux server2)

- One whole-solution build: `dotnet build Antiphon.sln -p:UseAppHost=false`, one lease, 0 errors, 1927 warnings, no NETSDK1047, 11m43s (lease held 704 s).
- R1 (Antiphon.Tests): 66/66 pass, 1m25s. NativeLinuxPolicyWitness 3, TestClassificationPolicy 39, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, MechanicalPcContract 11, TaskPlatformGuidance 5.
- R2: 413 pass, 2 skipped (Windows-only "Sharing-mode locks"), 0 failed, 2m45s. LegacyReleasePolicy 123, Settings 34, Switch 30, C1677RetentionServer 71, C0824WorkspaceSlotServer 24 (no timeouts), LandingRemovalPolicyControl 131+2 skip.
- **R3: 224 pass, 2 skipped (Windows-only C1048), 8 FAILED, 6m07s.** GhostSeatRelease 24, ChannelBridge 41, BuildSlotClient 13, BuildSlotScript 18+2 skip, LandVerificationBuildSlot 14, LandSourceIdentity 22, C0824RefOnlyLanding 6 all passed.
  - AgentTaskLandSourceFreshnessTests 54 pass / 5 FAIL: C498_NonReviewedRoleLandsWithDefaultTarget x4 (Plan/TestDesign x equal/behind) and C688_BehindLocalBranchLandsFromRemoteWithoutFastForward.
  - AgentTaskLandSourcePersistenceTests 32 pass / 3 FAIL: C498_MonitorPassDuringObservedInspection(0/300/900).
- R4 (Antiphon.SessionRunner.Tests): 172/172 pass, 32 s. C0824WorkspaceSlotCustody 19 (well inside 60 s), C0824WorkspaceSlotColdReset 12, C1722GitTimeoutBudget 98 (the session-runner git-timeout witness), BuildSlotBroker 17, Endpoint 7, EndToEnd 3, MaxHold 6, Metrics 5, Settings 2, NativeLinuxPolicyWitness 3.
- NOT RUN: I stopped once the cut was refused and the 90-minute budget was spent. These rows still need a run: RcQualificationWatcherScriptTests, ExecutionLedger (ReleaseGatePublicationAuthorityTests C599A_ExecutionLedger*, durations vs 300 s), ServerGitProcessTests, AgentReportStore{Budget,Load}Tests and ReportBudgetWorkspacePortabilityTests, RcFailedFirst{Execution,Planner}Tests, C1729LayeringGuardTests, C1708ReflogSalvageTests, and the migration/model-snapshot classes.

## Root cause of the 8 reds

I proved this with a temporary diagnostic (Console.WriteLine in the WorktreeGuardedCleanup catch-all plus op.LastReason in the test). The diagnostic was reverted and never committed.

1. op.Cleanup is Refused and op.LastReason is `cleanup_evidence_storage_unavailable`. That reason comes from the catch-all in server/Infrastructure/Git/WorktreeGuardedCleanup.cs:127.
2. The exception it swallows is `System.InvalidOperationException: unsupported controlled git command: ls-files -z`.
   It is thrown by tests/Antiphon.Tests/TestHelpers/ControlledLandingGit.cs:532 (ValidateCommand). The call comes from `GuardedWorktreeRemoval.LateUntrackedAsync` (server/Infrastructure/Git/GuardedWorktreeRemoval.cs:579), called from RemoveDirectoryAsync:178.
3. LateUntrackedAsync was added by aecdae2b6 "fix(c1717): refuse a regular file that appears after the clean inspection". That commit is in this range, part of the CARD-1717/S4a stack.
4. ControlledLandingGit refuses every command it does not know ("No default-success"), so the failure does not depend on platform. The Windows rc run would hit the same 8 reds.
   For C688, cleanup stops before the branch delete, so the expected `update-ref --no-deref -d` never runs.
5. Re-running the 8 tests with ANTIPHON_GIT_INPROCESS=off still gave 8/8 red, which rules out the libgit2 reads in a27ce632f.
6. The S4a integration checkpoint plan (docs/superpowers/plans/2026-11-06-card-0824-s4a-integration-checkpoints.md) never selected the AgentTaskLandSource* landing-protocol classes. CARD-1715 ran them before the previous cut, 1038d07e6.
7. Open question: does real (non-fixture) publication cleanup still complete? Real git supports `ls-files -z`, so the gap is most likely fixture-only.
   The Debug task should still confirm it, and check whether the other ControlledLandingGit gaps from aecdae2b6, ddd75783c and 60cb6015b matter: `ls-files --stage -z`, `ls-tree -r -z --full-tree`, `hash-object`.

Suggested repair: a Debug/Code task on master to teach ControlledLandingGit the S4a removal reads. Run AgentTaskLandSourceFreshnessTests, AgentTaskLandSourcePersistenceTests and the other ControlledLandingGit/LandingProtocolHarness users (for example LandingProtocolHarnessTests) as whole classes. After that lands, take a new cut from master (next free name rc/2026-11-08 is still free).

## Changelog 1038d07e6..df1fc1451 (89 commits; 106 files, +8241/-172)

- PRODUCT (server 58 files, +2182/-123):
  - CARD-1717 S4a hardening: GuardedWorktreeRemoval +535, WorktreeNoFollowDelete +563 (Linux no-follow delete), WorktreeSetAside, the switch re-read at every destructive step, TaskWorktreeRetirementService, DesktopLegacyReleasePolicyService, LandingGit destructive gate.
  - CARD-1708 reflog salvage refs: WorktreeReflogSalvage +217 (refs/antiphon/salvage, aged prune under lease and switch).
  - CARD-0824 S4a integration: unregister/branch-delete/queued mirror switch reads; S4a x S5b-1 RefOnly seam fix.
  - Operator's in-process libgit2 reads: InProcessGitStatus +234, GitStatusConfigAgreement +208. ANTIPHON_GIT_INPROCESS=off disables them.
  - CARD-1729 layering: byte-identical moves into Application/Core and Services (P0-A1/A2/E/F1), no logic change.
- tests: 31 files, +4417. C1717/C1708/C0824 witnesses, C1722 git-timeout floor, FailedFirst fixture, report template read-only reset, layering guard.
- scripts: 1 file (+9/-1). docs: 16 files (+1633).
- CARD-1713 (LeaseMaxHoldHours 48), 1715, 1716, 1719/1721, 1723, 1591 S1 and S5b-1 were all already in 1038d07e6, so they are not new in this range.
- Migrations: 0 new since 1038d07e6.
  The desktop host runs 919d8d42f (rc/2026-11-03, from GET /api/version). It would apply 5 migrations: 20261103210620_Card0824BranchDeletion, 20261105024334_ChannelInboundInputWithheld, 20261105052528_ChannelQueuedWithholdScanIndex, 20261105110702_Card0824RefOnlyDesktopCoordinates, 20261105121558_Card0824RefOnlyDesktopCleanup.
  I did not read server2's host version.

## Housekeeping

- I pushed no rc ref, no tag, nothing to an existing rc/*, and nothing to master.
- I removed all bin/obj outputs (104 dirs) and the land-graph canary. Scratch TRX files are in my session scratchpad only.
- Builds beyond the one solution build: four incremental `dotnet build tests/Antiphon.Tests` diagnostic builds, one lease each, all deleted. One of them was killed by my wrapper timeout while waiting on a saturated slot queue (4/4).
