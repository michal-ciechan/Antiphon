# Merge batch 183 (task db41dabc)

Master 3db1a43b02246706dc28075fce7e7b5e84804ddb -> b02f110fb67be04a4b25e3712ab469b4bdced43b (fast-forward, master unmoved at push time).
Landed: (1) rc g80 teardown fix (6e2dbcd6), (3) CARD-1842 S2+S5+S6 (d36127b5), (4) CARD-1830 GitInProcess mirror (024cf048).
Excluded: (2) CARD-1304 r2 (1ae8dfd4), (5) CARD-1843 (3e96732b).

## Exclusions

- CARD-1304 (73a92dbf): its own derived-impact-index refresh (f5ae5ec47/b916545ce) adds SessionRuntimeActionRoutingTests as a consumer of
  AgentTaskLandNotificationService.cs and pushes RolePolicyToolBundleDispatchTests into the capped sample. `impacted-classes.ps1 -Path
  server/Application/Services/AgentTaskLandNotificationService.cs` then swaps (RolePolicyToolBundleDispatchTests*) for
  (SessionRuntimeActionRoutingTests*). ImpactedClassesSelectorTests.C1801_non_merge_land_notification_filter_names_every_class
  (LandNotificationDerivedFilter constant) and C1801_why_missed_paths_select_their_classes_and_windows_filters go red. Proven at the
  subject's own tip: a detached 73a92dbf worktree prints the routing class and master prints the RolePolicy class. Its Review ran no
  ImpactedClassesSelectorTests. Fix owed: update the expected constants in ImpactedClassesSelectorTests.cs (LandNotificationDerivedFilter
  and the why-missed Antiphon.Tests tuple) to the new derived output, then re-review.
- CARD-1843 (8d426d26): on the combined tree with CARD-1830, its correct remainderProjects map moves InProcessGitCommandsTests into a new
  tests/Antiphon.GitInProcess.Tests row. Its own C1801_why_missed_paths... constant still expects that class in the Antiphon.Tests tuple,
  so the test goes red. This is a semantic conflict between subject 4 and subject 5, excluded as the later subject. Fix owed: re-cut
  CARD-1843 on the new master (b02f110fb) and drop InProcessGitCommandsTests from the Antiphon.Tests why-missed tuple. It also needs a
  GitInProcess.Tests row expectation and must re-check the frozen merge oracle (merge_subset_matches_the_hand_selector).
- Attempt 1 (all 5 subjects, local only, never pushed) ran union 476: 474 passed and only these 2 C1801 tests failed (36m12s). C1729
  VIOLATIONS 0. The branch was re-cut locally from master without subjects 2 and 5.

## Per-commit table (git cherry -v, every + non-.antiphon commit; patch-id vs source)

| landed | source | patch-id | subject |
|---|---|---|---|
| 11974e9a1ce6ad282b214b9027c4ea02e7ea9bcb | 07ce32d964070db31287331da80630b662bce28a | SAME | test(rc-g80): red - assembly DB teardown must wait for an open After(Class) |
| 749f9e6bd942a230847e31085feb73bcb7eb1a00 | 89cd8e125a8116c35503c2e9b0b3ccbdb35a8f93 | SAME | fix(rc-g80): wait for open After(Class) hooks before disposing the test store |
| d784f3087fb6101789692362297d8a481e181157 | a1d75c7119dae55d506453dbf441616c41ed97eb | SAME | fix(rc-g80): adopt the ca4e9ffb teardown wait and Windows extract byte-for-byte |
| ec870381ea91e42fd998c7a4c1336128d6ee9454 | b37ba1dafc24777ac8581b50da714bec919ccf30 | SAME | docs: why run ae84478d g80 LandNotificationParkFollowUp red was missed |
| f88cd7d30b6e35dc94548cf88c70c803e8eee986 | b7d3169d29a7f6f3c94f0cfb6f015768bd63cf3c | SAME | test(CARD-1842): S2 expected-duration roster sync witnesses |
| 71c6119f77dad82d71815a808beff9ef91002249 | 56a37f9edfc88cd027e90a8c8e23ef2e736681ff | SAME | feat(CARD-1842): S2 expected-duration roster beside PlatformTaxonomy |
| f8917136d7e05fb2c0ff56179628f7a18e0a8adf | 651dd2d4cb47be7ab533cc0b38041bca26502c0b | SAME | fix(CARD-1842): import Microsoft.CodeAnalysis for IsKind in the roster scanner |
| 5cdc5be426cf2d4b743340478bbb18f422c045c1 | b8f33a029ab017f626cd24b97b5245eab89975a5 | SAME | test(CARD-1842): exporter copies measurement identity from a fixture cost row |
| 681a418cdb81a7a43f5e2ed578b2a213557ea85e | 9efd4c1e4e2710597a101cc22047409c24477431 | SAME | test(CARD-1842): compare roster sync messages and doc sentences case-sensitively |
| f75d9d0e69a995b860d0b3cc9f652de4c443fb44 | 239ed7c8364bd4bd2aeeac649a2b829a5c505af1 | SAME | test(CARD-1842): S5 watch-qual ETA line witnesses (red) |
| 0688f2a317a043ca12779ac5505f9d5377ebf894 | ed39201b7a2d737db2857521008ba570791f2816 | SAME | test(CARD-1842): S5 harness keeps one-line results as arrays; pin every status parameter |
| d2260c67f259b52d55d1b2e72dc082a359611968 | eebe303959df26326b785d9166a5204b0dcf838c | SAME | feat(CARD-1842): S5 watch-qual prints the read-only ETA line with -TimingRoot |
| cf99704ee7b85b0f0fcb12a626444aef7796cc89 | 4625d49ad68b6c1e7f7bb6a30e9b04bc04567eb2 | SAME | test(CARD-1842): S6 ledger duration and v2 host witnesses (red) |
| 306a5ec42f38765f15dd3e98134309002f79b5cb | c4d508b6232de572419cd5c1877161abfa47ca6f | SAME | feat(CARD-1842): S6 ledger durations on v1, chunk host only on antiphon-rc-ledger-v2 |
| 1f77f66637e9f87d81074c9f367c53c82663bc0a | 87643270cddb9fb469f1ee81e5393c1065c1b731 | SAME | test(CARD-1842): S6 Get-Throw discards block output so a returned document cannot mask the assertion |
| 32d00564e1ddd8ada53c752062c16e7908cb38f8 | 510f9cfc38398b4f48103befb23883a67ac3cde1 | SAME | test(card-1830): witness that the GitInProcess tests have not left Antiphon.Tests |
| 0686117e5ecb2caa852c9655d2caeb30c78b5dd9 | 092561fac85977cf8ef0f3cdbf13a4ef490713a1 | SAME | test(card-1830): mirror the GitInProcess tests and register the suite |
| 4c1fd182d5afc67148837bbffea1243045758558 | 7d3cb89c349e7e3bf5c8ae61bc98841e5580ed82 | SAME | chore(card-1830): refresh the derived impact index |
| 1d9eb261edfcc03b492b17a5201133201953fefd | 8542269577c30fcb2413278d127c1dba61439a24 | SAME | docs(card-1830): plan, witnesses and checkpoint rows for the GitInProcess mirror |
| 4581774b1cefeec3fa9fcb0404dbbb4fd50917a7 | - | new | chore: refresh land-graph records for the landing batch |
| b02f110fb67be04a4b25e3712ab469b4bdced43b | - | new | test: regenerate the derived impact index for merge batch 183 |

Skipped: .antiphon report commits 2e4e1b1e4, 508044026, c538cff06 (and 8d426d265 with CARD-1843). The CARD-1830 branch land-graph
refresh f15c9b88c was replaced by a fresh one. The fresh refresh came from scripts/land-graph-land-refresh.ps1 on the attempt-1 tree,
whose graph files are byte-identical. It was carried as 4581774b1 and -Check approved it on the final tree.

## Pins and land graph

- policyHash 4ab004cf02e2d8adb80f4744030bd23fae3e2fd9b2cafff94822ba43b7bc5719 -> a1d534f78407944871f438cc0d0d008d6aa0b107db645ee1396796bfa504e99c
  and PinnedInputDigest d54af40756ac84a442b0c65b8b515c6933a2f4be024d4c94c32be8a979e33c27 -> ba3268c2df4dd127bfb011d5b26c701d8780603040646f7b9738fc0f83a64bc1.
  Both moved inside the CARD-1830 pick 0686117e5. On the final tree stored == computed (Read-NightlyExecutionPolicy; -PrintInputDigest),
  so no re-pin commit was needed. Master was not moved by the operator during the batch. The brief's 50c33c11/8510514a were stale.
- Land graph: 12096F4700F1A0FD67D55140EA6764CCB046D1F9FD82875092ECFDB98CE86ACA/79 -> 0876A0B74FD4FD3A185C531EB90E69F54AC90C95BE994EA6D5AC2FED7AF6F26B/80,
  pins d7655100a49fb339dc2f0ced795d0f0ef2bcbad3 unchanged. Changed records: Antiphon.sln, server/Antiphon.Server.csproj,
  src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj, tests/Antiphon.GitInProcess.Tests/Antiphon.GitInProcess.Tests.csproj,
  tests/Antiphon.Tests/Antiphon.Tests.csproj. The script posted its card note to CARD-1830. `audit-land-verification-graph.ps1 -Check`
  on b02f110fb: approved.
- Derived impact index: the regen printed changed (rc-g80 + CARD-1842 + CARD-1830 classes) and was committed as b02f110fb. In-run
  committed_index_matches_the_builder, capped_real_paths and merge_subset_matches_the_hand_selector all passed.
- check-evidence-diff 3db1a43b0..b02f110fb: commits=21 entries=0 violations=0. No migrations (selector migrations=False).

## Final-tree checks at b02f110fb (one solution build, UseAppHost=false, 0 errors, 3m43s)

- Union B on Antiphon.Tests, one process: 411/411 passed, 27m06s, C1729 `VIOLATIONS 0 LISTED 162`. It covered the subjects
  (ClassTeardownTrackerTests, AgentTaskLandNotificationParkFollowUpTests, ExpectedDurationSyncTests, WatchQualEtaLineTests,
  RcLedgerDurationTests, RcIncrementalLedgerTests, RcQualificationWatcherScriptTests, ImpactedClassesSelectorTests,
  MergeHighRadiusSubsetTests), the guard union without the policy batch (MechanicalPc, TaskPlatformGuidance, C1729, TestLaneCategory,
  ProcessSpawnLimit, C1514LandingSplitBudget, DerivedImpactIndex), the fixed high-radius classes, both land rows, the selector classes
  LiveLaneRunner/NightlyCore/ServerGitProcess and land-3 (LandGraphAuditProjectInstance, LandingGitBatching).
- Policy guard batch in its own process: 52/52 (1m40s), no stack overflow.
- A5 pair (ReleaseGatePlatformTests C1686_LinuxOnlyCensus + LinuxOnlyWindowsExclusionPolicy): 2/2.
- Antiphon.GitInProcess.Tests run once as a project: 96 total, 93 passed, 3 failed. The 3 are
  LibGit2VsGitWorkingTreeVerdictTests.Status_and_ls_files_verdicts_match_git cases long-path-untracked,
  same-size-edit-mtime-restored-settled and same-size-edit-mtime-restored-inside-the-index-second. They are inherited: the CARD-1830
  Code report proved the same 3 red in Antiphon.Tests at base 3db1a43b0 (Windows MAX_PATH/NTFS stat semantics on Linux). These are not
  flakes, so there was no rerun.
- SessionRunner ProcessSpawnLimitTests 2/2; Agents.Pty ProcessSpawnLimitTests 2/2; E2E ContractSnapshotTests 5/5.
- import --plan of the documented Merge rows (.antiphon/merge183-plan.md: tests, land-1, land-2, land-3, A5, e2e as the selector emits them
  for this range): imported 6 rows, 0 warnings.
- Note: without CARD-1843 the selector still lists the moved GitInProcess classes in the Antiphon.Tests row (they run 0 there) and
  emits no GitInProcess.Tests row. This is the defect CARD-1843 fixes. The project was run directly.

## Owed

- Native Windows: CARD-1830 GitInProcess mirror project (the 3 verdict cases are expected green only on Windows); the rc-g80 teardown
  fix on the native lane (rc run group 80); CARD-1842 scripts are Linux-run only here.
- Mutation PCs for rc-g80, CARD-1842 S2/S5/S6 and CARD-1830, if the pipeline schedules them.
- Debug/Code for the CARD-1304 constant update and the CARD-1843 re-cut (above); both then need a Merge.
- Not in this batch: CARD-1841, CARD-0747 Debug 3593eb2f, CARD-1844 S1/S2.

--- next stage ---
next: done
handoff: master b02f110fb67be04a4b25e3712ab469b4bdced43b; CARD-1304 and CARD-1843 excluded with fixes owed
artifact: .antiphon/task-db41dabc.md
