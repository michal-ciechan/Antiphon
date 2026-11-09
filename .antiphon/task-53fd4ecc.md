# Merge 53fd4ecc: land CARD-1811, cut rc/2026-11-14

Outcome: CARD-1811 is landed on master at 7bee43614495293afbae1b0f4add019d11dacb9c and
rc/2026-11-14 is cut at that same SHA. The candidate id is rc-2026-11-14-7bee43614495.

## Step 0: landing CARD-1811

- Picked with `cherry-pick -x` onto master 55d0ca6c84af44b853357cf6860a932d5c5ae12a. Both picks applied cleanly.
  - 87b7872e3e… (from red 6d1ac8f7e)
  - 7bee43614495293afbae1b0f4add019d11dacb9c (from fix 1164028af)
  - The why-missed note `docs/investigations/2026-11-09-why-missed-capability-script-dpapi-linux.md` was already inside the fix commit, so there was no separate note commit.
- The pins were recomputed on the picked tree. All three were unchanged, so no repin commit was needed:
  - policyHash: stored = computed = 9cf98d229e32ca52e76d6b8946856776c7c390e3027e2eae191f5042aca3b697
  - live-lane PinnedInputDigest: stored = computed = b587b96f62c5d652f2de4db1488fabcd0d050a5b34d35aa83af2cca04d10f031
  - LegacyExclusionDigest is 25dae45a…, verified by NativeLinuxPolicyWitnessTests passing in the R2 guard run below.
  - All three values are valid for 7bee43614.
- Tests after picking:
  - R1 `CapabilityScriptTests*|ReleaseGatePlatformTests*|LiveLaneRunnerTests*`: 13 total, 12 passed, 0 failed, 1 skipped. The skip is `issue_does_not_print_the_token_and_dpapi_round_trips`, which CARD-1811 intends to skip off Windows.
  - R2 policy guard batch (TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitnessTests), in its own process: 52/52 passed.
- land-graph -Check: exit 0, approved, digest=E4F112735EABDB3E5B1DF17A4E521710A71D1E4EA3F1E4863D0F97F1E298444C, count=75. It was run with `-ToolPath` set to the Debug build of tools/Antiphon.LandGraphAudit. No refresh was needed and -Update was never used.
- Master push: a plain fast-forward from 55d0ca6c8 to 7bee43614, accepted on the first attempt.
- After my push, the operator pushed four commits directly to master, which is now 25b347f49:
  - d15459456, 99e2ffa35, 364e27133, 25b347f49 (perf live-lane/land-tests, plus a land-graph re-record)
  - They are not in the cut and were not verified here. My pin values are valid for 7bee43614 only. 99e2ffa35 re-records the land graph, so the -Check digest at 25b347f49 will differ from E4F11273.

## Pin, prechecks and cut

The pin is 7bee43614495293afbae1b0f4add019d11dacb9c, which was the new master after Step 0.

- A1: `git ls-remote origin refs/heads/rc/2026-11-14` was empty both before the build and immediately before the cut.
- A2: for each of rc/2026-11-13 (653f1145d), 11-12 (5fef2c23b), 11-11 (3364197bf), 11-10 (74a8004f4) and 11-09 (0e7aef037), `git cherry <pin> origin/<rc>` produced 0 lines. Every one of those rc lines is an ancestor of the pin.
- A3 (computed at the pin):
  - policyHash: stored 9cf98d22… = computed 9cf98d22…
  - PinnedInputDigest: stored b587b96f… = computed b587b96f…
- A4: land-graph -Check exit 0, digest E4F11273…, count 75.
- A5 (scripts/test-rc-platform.ps1):
  - `-Case LinuxOnlyWindowsExclusionPolicy`: 6 passed, 0 failed
  - `-Case C1686_LinuxOnlyCensus`: 62 passed, 0 failed
- A6: test-rc-coverage-admission.ps1 passed 8/8.
- Name check: `Test-ReleaseGateCandidateRef rc/2026-11-14` returns Ok, Kind long-lived, CandidateId rc-2026-11-14-<sha12>. No script refuses the name.
  - The only validation in release-cut.ps1, release-candidate.ps1 and rc-qualification-status.ps1 is the `^rc/YYYY-MM-DD$` pattern plus a date parse.
  - Nothing checks for a future date.
- Cut: `git push origin 7bee43614495293afbae1b0f4add019d11dacb9c:refs/heads/rc/2026-11-14` (plain push, new branch).
  - `git ls-remote origin refs/heads/rc/2026-11-14` now returns `7bee43614495293afbae1b0f4add019d11dacb9c refs/heads/rc/2026-11-14`, an exact match.

## Tree verification

- Build: one whole-solution `dotnet build Antiphon.sln -p:UseAppHost=false` under one build-slot lease.
  - 0 errors, 2117 warnings.
  - Wall time 4m04s; the slot was held 245 s.
  - No NETSDK1047 race, so -m:1 was not needed.

Test rows, all run `--no-build` with one lease per command:

| Row | Classes | Result | Time |
|---|---|---|---|
| R3a | contracts, checkpoints, scripts, rc and spawn classes (MechanicalPcContract, TaskPlatformGuidance, CheckpointColdBuild, CheckpointTerminalReceipt, C1217ConditionalSkipContract, CheckpointSourceExecution, XmlInputSecurity, C1729LayeringGuard, ReviewTestQualityChecklistWitness, MergeHighRadiusSubset, ImpactedClassesSelector, TestSelection, LandGraphLandRefresh, Server2ProductionServerContract, DockerStackContract, ServerRepositoryMirrorScript, ServerGitProcess, ServerImagePushCredential, GithubCredentialHelper, PwshPrivateCache, RcQualificationWatcherScript, RcFailedFirstExecution, RcFailedFirstPlanner, EphemeralHttpListener, ProcessSpawnLimit, PhaseJournalKillDiagnostic, Card1277PtyHostFixture) | 391/391 passed | 27m24s |
| R3b | server and DB classes (C0824DesktopLegacyRelease, C1677RetentionServer, C0824WorkspaceSlotServer, LandingRemovalPolicyControl, AgentTaskLandBoundaryControlled, AgentTaskLandBoundary, ProjectReadinessBatch, PreDispatchFailureDelivery, RepositoryMutationLease, AgentTaskLandNotification*, SettlementSyncDebt*, C1677DiskAlarm, TranscriptSinceClient, HangfireStorageSelection, HangfirePostgreSqlStorage, C0824WorkspaceSlotRoleGate, Card1761RunnerReadPoll, C1514TaskLoadWindow, AgentTaskLandReceiptWatermark*) | 961 total, 949 passed, 0 failed, 12 skipped | 6m50s |
| R3c | 24 Antiphon.Tests migration classes, including Card1773TranscriptPromptStampMigration, TranscriptHotPathMigration, AdditiveMigrationRuleWitness and TestMigrationFingerprint | 88 total, 87 passed, **1 failed** | 8m10s |
| R4 | methods C1802*/C1805* in BlockedTaskSyncRecovery, RunnerBranchContractDocumentation, RunnerTaskSettlement, RunnerCompletionProgress and SettlementSyncDebtPolicy, plus ReleaseGatePublicationAuthorityTests/C599A_ExecutionLedger* | 12/12 passed | |
| R5 | LandGraphEvaluationTests*, run detached | 155/155 passed | 14m44s |
| R6 | SessionRunner.Tests: NativeLinuxPolicyWitness and TestClassificationGuard (the CP-4 SessionRunner policy witnesses), C1793*, C1677TempFamilyRegistry*, TranscriptSinceRead, PtyConditionalRead, ProcessSpawnLimit | 29/29 passed | 43 s |
| R7 | Antiphon.E2E: ContractSnapshotTests*, AntiphonAppFixtureHostPathTests* | 8/8 passed | |

- R3c failure: `C0824RefOnlySnapshotMigrationTests.Downgrade_refuses_recorded_snapshot_and_preserves_its_bytes(debt)`.
  - The BeforeTest hook `PrepareHistoricalBaseline` exceeded its 60000 ms timeout inside `MigrationTestDatabase.CreateAsync` while 24 migration classes ran in parallel.
  - Rerun alone once on the same build: 2/2 passed. The (debt) case took 2.8 s and (park) took 4.4 s.
  - This is timing-only: a hook timeout under load. It is not one of the named known flakes; I treated it as timing-only and continued.
- ExecutionLedger durations, all under 300 s: C599A_ExecutionLedger 36.4 s, Chunks 38.1 s, Joins 35.9 s, Markers 33.8 s, Receipt 30.5 s, Counts 21.7 s.
- None of the known master load flakes appeared in these rows. CapabilityScriptTests DPAPI is fixed and now skips off Windows.
- Not run:
  - ProcessSpawnLimitTests in Antiphon.Agents.Pty.Tests. It would need a separate lease and project; the Antiphon.Tests and SessionRunner.Tests copies did run.
  - The Messaging.Tests InboxConversationMigrationTests. It covers the messaging service, not server migrations.

## Changes since the previous cut (git log 653f1145d..7bee43614)

67 commits.

**Migrations hosts will apply:** NONE. `git diff --name-only 653f1145d..7bee43614 -- server/Migrations` is empty.

**PRODUCT** (14 files under server/src):

- CARD-1802 S2 (a222f41e4): a complete runner report whose desktop lease is busy now settles with a sync-debt row instead of blocking. Touches TaskCompletionProgressService, SettlementSyncDebtPolicy, RemoteSettlementSyncDtos and AgentTaskEndpoints.
- CARD-1805 (39cfde8d0): the unfetched gate reads absence and the recovery exception, and the residual is documented. Touches ITaskProgressGit and RemoteWorkspaceService.
- CARD-1804 (187ed2589): retention gets a cross-process tick lock, a terminal run state, a host-stop wait, and preview cancel accepted. Touches RunnerWorktreeRetentionService/Ticks and Program.cs.
- CARD-1793 (e5f423f83): the per-session pty pwsh cache is owned by the host, with a stale cache sweep and checkpoint tool install. Touches src/Antiphon.Hosting/PwshPrivateCache.cs and SessionRunnerRuntime.cs.
- CARD-1789 (c0121bfd0): server/Bundles/stage-code.md wording, a sort-after-master clause and one migration owner.
- Land-graph manifest/evaluation records (55d0ca6c8).

**Scripts and tools:**

- CARD-1801: why-missed selector rows, the Windows lane field, the Merge subset and land row split.
- perf(selection) in 69d234409, ba88abdbf and c6c4c20d9, and perf(rc-projects) in f7fc1573e.
- CARD-1806: restart-apphost finds the dashboard on 17205.
- CARD-1805: the delegate.ps1 status line.

**Tests:**

- CARD-1811: DPAPI case skips off Windows, with a Linux rc row.
- CARD-1808: EphemeralHttpListener binds a fresh HttpListener on every retry.
- CARD-1807: Checkpoints spawn-limit census.
- CARD-1809: Review test-quality checklist.
- rc 231f573c group fixes: group 2 ProcessSpawnLimit/PhaseJournalKill, group 4 Card1277, group 71 PreDispatch, group 96 CRLF, and group 117 RepositoryMutationLease.
- Load-flake fixes: C1759 hosted scan, RemoteWorkspacePreparer cancel, and C467_V32.
- CARD-1590 cleanup witness and the live-lane read-only proofs.

**Docs:** why-missed notes, investigations, the AppHost activation record for rc/2026-11-13, and the am-service deploy record.

## Cleanup

All bin/obj, E2E TestOutput and test-created bin-c585h-* directories in the worktree were removed with `git clean -f -dX -- tests src server tools scripts samples Antiphon.AppHost`. The land-graph canary removed itself.
