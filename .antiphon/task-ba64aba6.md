# Review ba64aba6 - round 1, equivalence scope: master port of rc top-ups 52-53

Subject: feat/card-task-e13fbf72 tip a479faafe25b44754cb02440aac81bebcb5c2b7e (73 commits on master df92aa7b7).
rc: origin/rc/2026-10-27 5010e23f0b394ecd4a1ea70e47f7f0c868f24f15. Current origin/master 0493af102923b4d0d34e736376fd67030dbe12cb.

Verdict: CLEAN. No blocker. Land tip a479faafe25b44754cb02440aac81bebcb5c2b7e.

## 1. Completeness (git cherry -v origin/master origin/rc/2026-10-27, recomputed)

95 rc-only commits against master. 73 are patch-id equal to the port (git cherry a479faafe rc). The other 22:

- .antiphon/task-*.md rc integration records, no behaviour: 665bf12b1, f3aa3b43e, a13e544e2, afdeebdc1, 105792f7d, 335b4fbb3.
- Add/drop pair 59deddd30 / 97756db4e: both touch only .antiphon/task-00ae50cf.md (+32/-32), net zero.
- Already on master in another form, with every touched file byte-identical between rc and the port tip:
  - CARD-1574 125c720b9 and 2b7383b24 (ResumeReceiptReadinessTests.cs and the card-1574 plan): SAME.
  - CARD-1610 3cb215cf3, 9c271b7e1, 80b3a59c4: FailedTaskSessionOwnership.cs, AgentTaskDispatcher.PoolSupervision.cs, AgentTaskPoolTests.cs, GrokDelegateDispatchTests.cs and TaskSupervisionVisibilityTests.cs are SAME. AgentTaskDispatcher.cs, AgentTaskService.cs, AttentionService.Seats.cs and session-runtime-invariants.md differ only by master-only later work (see section 2).
  - C1379 b9575b5e6, f90ea8f9a, b226c226b, 64c7537aa, f0caf7762 (LandRestartReplayTests.cs and the debug doc): SAME.
  - C514 / report-budget docs f866c0f04 and 193524ac9: SAME.
  - CARD-1619 policy db8a211f2 and 1f3794102 = master 67f80cd7d and 651eb516b (both ancestors of master, same subjects).
- The hello-retry and native-exit commits are patch-id equal on master (git cherry marks them `-`).

Policy: I checked both files with the owner tooling (Read-NightlyExecutionPolicy from scripts/lib/nightly-common.ps1 and scripts/lib/nightly-policy.ps1). Each stated hash matches its computed hash: rc 833dc822... and port tip 9cd8d728.... A leaf-level JSON diff gives rc 1034 leaves and tip 1038, with 0 lost and 4 added. The 4 added leaves are the master scriptCensus row `test-qualification-graph`. Master is a strict superset, and no rc policy row is lost.

## 2. Tree equivalence (git diff rc a479faafe over the 66 ported files)

65 of the 66 files are byte-identical to rc. The only difference is docs/testing-and-build.md: +14 lines of the master C487 read-only fixture paragraph, with no rc line removed.

The worker's other named files are not touched by the port. Their rc-to-tip differences are all master-only:
- AgentTaskDispatcher.cs: BOM drop, CARD-1610 S2 ceiling hold and supervision notices.
- AgentTaskService.cs: successor fence, cost rollup, cancel gate, ceiling and notice DTOs.
- AttentionService.Seats.cs: notice state.
- session-runtime-invariants.md: the CARD-1610 S2 ceiling supervision section.
- test-execution-policy.json: as in section 1.

There is no unexplained hunk.

## 3. Series behaviour on this tree

I made one Antiphon.Tests build (Debug, build-slot, bin-rvba64, 0 errors) at a479faafe with a clean tree. Counts are taken from TRX:

| Class | Passed | Notes |
|---|---|---|
| RcStoppedRunSeedTests | 20 | |
| RcFailedFirstPlannerTests | 2 | |
| RcFailedFirstExecutionTests | 2 | JournalCustodySurvivesInterleaving 1m49s; ActualLoopFollowsSchedule 6m08s |
| OwnedProcessTreeTests | 14 | |
| LandingGitTests | 65 | |
| RepositoryMutationLeaseTests | 28 | 2 not executed: Windows-only SkipTestException C448_V28_ExitedRootKeepsItsJournal..., C448_V13_WindowsJunction... |
| RcRosterRepairTests | 3 | |
| C1514LandingSplitBudgetTests | 5 | C1404, C1577, C1584, C1410, G2865 |
| NativeLinuxPolicyWitnessTests | 3 | Antiphon.Tests copy |

Total 142 passed, 0 failed, 2 skipped, 24m41s. FullBytesAndCoverageConserved took 11m19s under load.

Policy witnesses in the other three tests/Shared suites each ran 3 passed, 0 failed, each from its own build: SessionRunner.Tests, PtyHost.Tests and Agents.Pty.Tests.

My first attempt at the series run was killed by my own 595 s foreground timeout and gives no verdict. The second run above is the evidence.

## 4. Mutants

Each mutant went red, then I restored the source with git checkout and the tree was clean.

- M1, tree-kill pid identity: OwnedProcessTree.cs:198, `if (live != entry.StartTicks)` changed to `if (live is null)`. Separate build. Red 1/14: C448_ReusedPidWithLaterStartTicksIsNotSignalled.
- M2, snapshot-before-kill: OwnedProcessTree.cs:182, a `kill(entry.Pid)` added inside the snapshot pass. Separate build. Red 5/14: C448_SnapshotIsSealedBeforeTheFirstKill, KillOrderSignalsTheRootFirst, ReusedPid..., StartAfterTheSnapshotClock..., ProductionKillPassesTheServerPid.
- M3, seed failed-method priority: nightly-stopped-run-seed.ps1:536, `if($row.hasNodes){` changed to `if($false){`, which falls back to class-level. Red 3/3: FailedMethodBands, RealPackerFailedMethodFollows, RealShapeFailedMethodBand.
- M4, digest guard seed exemption: nightly-dispatch.ps1:127, the guard now applies to any PreviousRun. Red 2/2: DigestGuardLeavesSeedOrder (kept=previous-run-plan-digest-missing), RealPackerRegroupKeepsFailedFirst.

Restored green on the clean build: OwnedProcessTreeTests C448_* (14) plus the 5 seed methods, 19 passed, 0 failed.

## 5. Gates

- `scripts/audit-land-verification-graph.ps1 -Check`, with the evaluator built Release through build-slot: approved, digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806, count=69. The canary was removed. I did not run -Update.
- `check-evidence-diff.ps1 -Repository . -BaseRef df92aa7b7 -HeadRef a479faafe`: commits=73, violations=0.
- `git merge-tree --write-tree` onto current origin/master 0493af102 (113 commits past df92aa7b7, a fast-forward): clean, tree 5a406c265177965fb542065487d5bb46388b1d9a. Zero files overlap between the port and df92..0493.
- CARD-1609 feat/card-task-52590db3 (tip ccbb84f70, merge-base 98fc3ceb6): it changes 62 files and none of them is among the port's 66. merge-tree of 52590db3 against the port, and against (0493 + port), is clean. Informational: I find no conflict for slice ae950679. The overlaps the worker listed (nightly scripts, LandingGit.cs, ScriptHarnessProcessContractTests.cs) are files the port touches, and 52590db3's own changes do not include them.

## Advisories

None blocking.
- RcFailedFirstExecutionTests and RcFailedFirstPlannerTests are long under load: FullBytes 11m19s, ActualLoop 6m08s. Budget a series run at about 25 minutes, not inside one 10-minute foreground window.

## Scope statement

I ran the commissioned equivalence selection in full. I did not rerun the whole Unit lane on this tree, so ordinaryScopeCompleted is Interim by the profile's literal definition.

I used no checkpoint-tool receipts; the evidence is build-slot TRX at a479faafe with a clean tree. That is why reviewedSourceClean is false.

I deleted all bin-rvba64* and bin-land-audit outputs.
