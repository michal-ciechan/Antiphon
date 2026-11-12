# Merge batch 184 (task 9d003ac4)

master b02f110fb67be04a4b25e3712ab469b4bdced43b -> 24a3e6059ffad1e5e57234b2a9488b52bc9da112 (fast-forward, first push attempt). No rc/* ref touched. Master did not move during the batch (re-fetched before the push).

## Picks (cherry-pick -x, linear, 22/22 clean, patch-id MATCH 22/22)

| # | new | source | subject |
|---|---|---|---|
| 1 | ab1b5696f | c81492732 | g133 fix(scripts): select-release requires the server-git, resilience and git-in-process rc suites |
| 2 | 02258bbca | 43f704aa3 | g133 docs: why-missed note |
| 3 | 27eea23cf | 78cc738f8 | g97 test(card-0824): red first |
| 4 | a24e32209 | f7f303e83 | g97 fix(tests): C1017 temp-root refusal admits a temp source with a persistent report root |
| 5 | 3a1bb095a | 00ef8370b | g97 docs: why-missed note |
| 6-19 | 10b795a98..7f632fb9b | 62d385d02..7b2df5107 | CARD-1304 round 2 re-land, all 14 commits (tests, 2 derived-index refreshes, 2 evidence docs, selector pins) |
| 20 | 348273a0c | b357b7854 | CARD-1841 test (red first) |
| 21 | 0a74771fc | 40563ebfc | CARD-1841 fix: staged helpers reuse their already built references |
| 22 | 79a516c27 | a9c843630 | CARD-1841 derived impact index |
| 23 | 24a3e6059 | (new) | chore: refresh land-graph records for the landing batch (sanctioned refresh script, own commit) |

Skipped: aa4c8af36 (.antiphon/task-cb632056.md report only). Not picked: cc1a00bb7 (desktop duplicate of the g133 fix, per brief).
Full patch-id table: scratchpad patchids.txt (all MATCH, matched against the last `cherry picked from` line, since CARD-1304 commits are themselves re-cut picks).

## Exclusions
None.

## Checks on the final tree 24a3e6059
- check-evidence-diff b02f110fb..HEAD (pre-refresh tip): commits=22 entries=0 violations=0.
- Migrations: none in the batch.
- sln build -m:1 UseAppHost=false: 0 errors (3m55s); rebuilt after the refresh commit (embedded manifest): 0 errors.
- land-graph: -Check before refresh refused only "manifest does not match the selected graph" (CARD-1841 csproj). `land-graph-land-refresh.ps1 -Base b02f110fb -Card CARD-1841`: digest 0876A0B74FD4FD3A185C531EB90E69F54AC90C95BE994EA6D5AC2FED7AF6F26B -> B0933881E8256BD9DEF52BF190009B9B536AA7AE33AC1FD24422ABB6D885B991, count 80 -> 80, pins d7655100a49fb339dc2f0ced795d0f0ef2bcbad3 -> 415e66232d0d44ad4e59c579d332c8935147402e, changed record tests/Antiphon.Tests/Antiphon.Tests.csproj (manifest, pins, evaluation files). -Check after: exit 0 approved (twice, last after the test runs).
- Pins: policyHash stored == computed a1d534f78407944871f438cc0d0d008d6aa0b107db645ee1396796bfa504e99c; PinnedInputDigest stored == computed ba3268c2df4dd127bfb011d5b26c701d8780603040646f7b9738fc0f83a64bc1. No re-pin. The brief's 50c33c11/8510514a are stale.
- regenerate-derived-impact-index.ps1: "derived-impact-index unchanged" (no commit).
- impacted-classes -MergeSubset: migrations=False; kinds land-graph-audit-project, spawn-limit-rosters, test-lane-category; added ProcessSpawnLimitTests, TestLaneCategoryGuardTests and LandGraphAuditProjectInstanceTests (land-3 row).
- import --plan of the documented Merge rows (selector filters, messaging dropped, land-3 added): 6 rows imported, 0 warnings.
- ONE batched Antiphon.Tests union, 41 classes: 592/592 passed, 0 skipped, 31m34s. Includes ReleaseSelectionCliTests 2, all 14 C0824RefOnly* classes 52, CardDoneWorktreeCleanupTests 22, ImpactedClassesSelectorTests 40, SessionRuntimeActionRoutingTests 35, SessionRuntimeActionTests 28, StagedHelperBuildTests 2, LandNonCodeSkipTests 112, LandGraphAuditProjectInstanceTests 1, the fixed subset and land-1/land-2 classes, and the guard union MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 9 (VIOLATIONS 0 LISTED 162), TestLaneCategoryGuard 1, ProcessSpawnLimit 57, C1514LandingSplitBudget 5, DerivedImpactIndex 26.
- Policy guard batch in its own process: 52/52, 1m25s, no stack overflow.
- A5 pair (ReleaseGatePlatformTests C1686_LinuxOnlyCensus|LinuxOnlyWindowsExclusionPolicy): 2/2.
- E2E ContractSnapshotTests: 5/5 (Kafka connect-refused noise only).
- No flakes, no reruns.

CARD-1304 coverage note: its changed test classes are SessionRuntimeActionRoutingTests and SessionRuntimeActionTests (whole classes run). The unchanged CP-4/7..13 method rows in other classes (SessionRunnerEventPump, AgentSessionRuntime, SessionMessageQueueDeliveryVerification, ChannelReplyLateConfirmDispatch, SessionStateCommit/ReviewReplyDispatcher/HerdrStatusDisagreement, SessionRunnerHeldEventDelivery/AgentTaskMidTurnRefine) were not re-run; .antiphon/task-f144c9e8.md is not reachable from this mirror, so the class set was taken from the card's committed evidence doc.

## Owed
- Native Windows: g133 ReleaseSelectionCli on Windows (desktop read cc1a00bb7 already proved 17/17 for the identical fix); scripts/lib change forces the Windows restart-impact obligation; CARD-1841 StagedHelperBuild/LandNonCodeSkip on the Windows lane; g97 C0824RefOnlyCleanup on the next native rc run.
- Mutation PCs for CARD-1841 (BuildProjectReferences / OutDir-removal mutants) and CARD-1304.
- rc port of these subjects is a separate decision (master only in this batch).
