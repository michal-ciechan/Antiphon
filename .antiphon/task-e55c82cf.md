# CARD-1112 Final Review

Outcome: clean. No defect under the verdict policy (no regression, no false or misleading instruction). Six disclosures below; CARD-1124 filed for the unshipped D-8 projection. Land the Code owner.

## Identity

- Review task: e55c82cf (this report), branch feat/card-task-e55c82cf, mirror /work/worktrees/task-e55c82cf
- subjectTaskId (Code owner): 07622e22-f12d-4f6d-b1d5-356960664356, ref refs/heads/feat/card-task-07622e22
- reviewedSourceSha: a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d (base 3599aa4b5d401112980c368662d476d49e46dff9)
- origin/master moved to ce1c9e80b1f0deaa6e9d37c76649dba09d97ef86 (cff20cb13 CARD-1082 progress attribution, ce1c9e80b the CARD-1108 plan). No file shared with the candidate; `git merge-tree --write-tree origin/master a7b87d2b0` is a clean merge.
- Diff: 10 files, +30 -20. Two owner docs, AGENTS.md, one skill file, two bundle files, the CARD-1065 plan, three test files. No file under server/*.cs, src/ or client/ changed. The bundle and skill files are injected instruction text and were reviewed as production behaviour.

## What ran at the reviewed SHA (one checkpoint-tool run 20261006-213122-ebac, --serial)

Manifest: hand-made markdown checkpoint table in the session scratchpad (outside the tree), one build `tests/Antiphon.Tests -> bin-c1112rv/`, eight rows reusing it, every row exclusive. The tool was built once through scripts/build-slot.ps1 into tools/Antiphon.Checkpoints/bin-c1112rvtool/ (slot granted, waited 0s, held 6s) and that directory was removed with a root-confined rm after validate. The run deleted bin-c1112rv/ itself on green.

| Row | Filter | Executed | Passed | Failed | Skipped |
|---|---|---|---|---|---|
| CP-1 | registry guard: TestClassificationGuardTests, SlowTestTripwireTests | 3 | 3 | 0 | 0 |
| CP-2 | plan CP-10: `/*/*/BlockedTaskParkProjectionTests/C1065_*` | 2 | 2 | 0 | 0 |
| CP-3 | InstructionBundleTests (bundle text, 2,500 stage cap, CARD-0884 500-char headroom) | 72 | 72 | 0 | 0 |
| CP-4 | 13 doc/bundle guard Unit classes (the Code task's R-1 set minus InstructionBundleTests) | 107 | 107 | 0 | 0 |
| CP-5 | 16 Unit classes that read AGENTS.md, SKILL.md, server/Bundles or InstructionBundles text (incl. AgentContextContractTests byte budget) | 328 | 328 | 0 | 1 |
| CP-6 | AgentBundleAttachmentTests, alone in its driver, Testcontainers Postgres | 24 | 24 | 0 | 0 |
| CP-7 | DelegateBundleLaunchTests | 23 | 23 | 0 | 0 |
| CP-8 | GrokRulesCompositionTests (composes delegate-basics text into Grok rules) | 9 | 9 | 0 | 0 |

Total 568 executed, 568 passed, 0 failed, 1 skipped. The skip is DelegationReportFormatterTests.reported_repository_paths_normalize_relative_and_absolute_windows_forms, a SkipTestException off Windows (CARD-0681), inherited and unrelated. Because of it `validate` over the whole report says row_failed; `validate --rows CP-1,CP-2,CP-3,CP-4,CP-6,CP-7,CP-8 --expected-source-sha a7b87d2b...` prints CHECKPOINT SOURCE VALID rows=7. Every row carries slot=granted dirty=0 sourceState=clean buildSource=verified.

CP-1 through CP-5 are exactly the Code task's 513-result selection (its TRX at /work/worktrees/task-07622e22/.antiphon/c1112-unit/c1112-unit.trx lists the same 33 classes: 512 passed, 1 skipped). CP-6 and CP-7 match its 24/24 and 23/23. CP-8 is additional.

Not run, with reason: DelegateLaunchArgvIntegrityTests (Windows-only CommandLineToArgvW parser; desktop lane only), GrokRulesDispatchAcceptanceTests (requires Windows, ANTIPHON_HEADED_TESTS=1 and a real CLI stub), the Antiphon.Agents.Pty.Tests argv tests (literal strings, not the bundle files). Whole-Unit was excluded by the brief.

Evidence guard: scripts/check-evidence-diff.ps1 -BaseRef 3599aa4b5... -HeadRef a7b87d2b...: commits=1 entries=0 violations=0, exit 0.

Plan table: the amended CARD-1065 plan still imports (`import --plan` on a scratch copy from the repo root: 15 rows, CP-10 intact with its filter; the Covers cell is free text to the importer, only the header and column count are validated). `coverage --plan` over the whole 1,500-line plan did not finish within 300 s on the mirror; not a finding.

## Code report check

There is no CARD-1112 checkpoint table; the brief's named set is the ordinary scope and the Code report says so. Red first is recorded (unit 3 of 74 failed = BlockedTaskParkProjectionTests plus InstructionBundleTests before the doc edit; attachment 1 of 1). The three green runs carry slot ids, waited/held times and the OutputPath (bin-c1112/, UseAppHost=false). The one driver that exited 5 before tests (unknown --nologo) is listed with its reason and rerun. Disclosure D: the report text does not name the filter of the 513-result run; it was recovered from the TRX.

## Sentence audit against the code at the base and at origin/master (all true)

1. Reclaim (docs/session-runtime-invariants.md:111, pin c1065-reclaim-page). TerminalRunnerSeatReleaseService.ReclaimLegacyAsync(pageSize, passBudget): `for pass < passBudget { page = parks.NextLegacyPageAsync(pageSize); if empty break; foreach row visit }`; callers pass (32, 3). BlockedTaskParkingService.NextLegacyPageAsync builds one page from two disjoint queries (Id > cursor, then Id <= cursor to fill a short tail), so a row appears at most once per page and at most three times per call; at most 96 visits. The method has no gate (DiscoverScheduledAsync is the one with discovery.Gate). "three passes of up to 32 rows (at most 96 row visits), after wrap-around the same row at most three times, no discovery gate" is true.
2. Blocked and the gate (docs/agent-card-lifecycle.md:10-12, new pin c1065-lifecycle-gate). DelegationOpenGate.OpenStatuses = Queued, Dispatched, Working; Snapshot.AbsoluteLimit = DelegationSettings.MaxOpenTasks (default 6), RoleLimit = RecommendedInFlightFor(role); both count the same rows. CardTaskSettlement and CardWorkTransitionService count Blocked as open for the card. docs/orchestration-loop.md:786-789 says the same ("counting Queued, Dispatched and Working non-specialist tasks but not Blocked ones"; "Blocked is outside MaxOpenTasks and still occupies a runner seat"); its "including Blocked" pin is unchanged and passed in CP-2.
3. Park bound (AGENTS.md:73, pin c1065-park-rule; docs/session-runtime-invariants.md:78, new pin c1065-park-bound). BlockedTaskParkingOptions: SectionName "BlockedTaskParking", Enabled=false, ReclaimExisting=false. TryHandleTaskAsync's Blocked branch returns without release when ParkingEnabled is false; RegisterAndReserveAsync returns PublicationRequired for a Blocked task when parking is off; DiscoverCandidateAsync routes a Blocked owner through TryHandleTaskAsync; TerminalRunnerSeatReleaseOptions.AutomaticEnabled=false and no appsettings file sets it. So a Blocked session's seat is never released automatically in any configuration while parking is off, and nothing automatic releases any waiting session under today's defaults. The linked owner document exists (AgentContextContractTests checks the routing index) and AGENTS.md is 15,716 raw bytes against its 24,576 limit.
4. stage-plan.md (line 11, pin c1065-release-question in three classes). 1,271 chars against the 2,500 stage cap (1,229 headroom), ASCII, one sentence. It is conditional ("only when the plan adds or changes a session that waits for input"), states today's answer, and is a checklist line, not a gate or a required section, so a Plan agent whose plan touches no such session has nothing to do and one whose plan does has a concrete question. It cannot stall or confuse a Plan agent.
5. Plan amendment (D-8, S10, V-25, CP-10) is additive: a dated "Amendment 2026-10-06 (CARD-1112)" paragraph under D-8 and dated amendment text appended to the S10 row, the V-25 row and the CP-10 Covers cell with every original word retained. The amended facts are true: SeatDesktopJoin sets OpenTaskId only for Dispatched or Working, RunnerSlotService.IsOrphan is !pooledWarm && (!live || !openTask), RunnerSlotDtos has no park field, and the shipped doc sentence pins orphan=true for a live Blocked or Queued owner.
6. CARD-1083 is cited on the SKILL.md, AGENTS.md, lifecycle, invariants, delegate-basics and stage-plan sentences. stage-review.md (2,481 bytes) and orchestrator.md (13,671 bytes) are unchanged.

Byte budgets: delegate-basics.md 7,080 -> 7,063 bytes (the "(CARD-1083)" citation paid for by shortening the CARD-0589 sentence; see disclosure B). The worst-case composition test with CARD-0884's 500-char headroom and the 2,500-char stage cap passed in CP-3; the AGENTS.md raw-byte budget passed in CP-5.

Pins: every changed pin now requires a longer exact string than before (strictly stronger), the three new pins (c1065-lifecycle-gate, c1065-park-bound, c1065-card-1083 for SKILL.md) assert real sentences, and the Code report records each going red before the doc edit.

## Disclosures (none is a defect under the verdict policy)

A. D-8 tracking: the Code pass amended the plan and deliberately filed no card; the S10 Final Review item 5 asked for one. Filed CARD-1124 (Backlog, id e2d1fe12-5644-470e-9aa8-db2185c320e3) with the code facts and the SKILL.md "count orphan=true" consequence.
B. delegate-basics.md's CARD-0589 sentence changed from "unbounded concurrent builds left 203 build processes and 1.1 GB free on one host" to "unbounded builds left 203 processes and 1.1 GB free" to keep the bundle size flat. No test pins the old wording (grep "203 build" in tests: none); the meaning is preserved. Worth knowing because it is an unrequested edit to injected text.
C. The pin label c1065-no-autosave was renamed c1065-card-1083 in AgentBundleAttachmentTests, InstructionBundleTests and BlockedTaskParkProjectionTests; the pinned string grew to "Parking will not autosave (CARD-1083)." so the old assertion is still covered.
D. The Code report does not state the filter of its 513-result run; recovered from its TRX (33 classes).
E. Precision: "while it is dormant nothing automatically releases a session that waits for input" is true for every Blocked session in every configuration and for every session under today's defaults. With TerminalRunnerSeatRelease:AutomaticEnabled explicitly true, the terminal seat path can still release a settled (Succeeded/Failed/Canceled) task's idle seat independent of parking; the same doc states that default two lines above the sentence.
F. The pre-existing SKILL.md and orchestration-loop sentence "count orphan=true before concluding the host is busy" (only a citation was added here) overstates reclaimable seats while D-8 is unshipped, because a live Blocked child reads orphan=true yet keeps its seat. Recorded in CARD-1124.

## CARD-1108 overlap, for the landing order

The CARD-1108 plan (docs/superpowers/plans/2026-10-06-card-1108-park-reclaim-follow-ups-plan.md at origin/master ce1c9e80b) rewrites the four pinned S9 sentences slice by slice. The in-flight S1 task 662d3f06 has no pushed commit yet (its branch is at ce1c9e80b, the master tip). HEAD line numbers below are at a7b87d2b0.

- S1 (D-1, D-7; the in-flight task): rewrites docs/session-runtime-invariants.md:113 "FreshLegacyWindow mixes the runner observation clock with park.CreatedAt. An old CompletedAt is not the idle window." and its pin c1065-fresh-window at tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs:75, plus a one-line amendment under PC-171 in the CARD-1065 plan (line 1390 region). No sentence or pin that S1 rewrites is touched by CARD-1112, and CARD-1112's plan amendments sit at lines 344, 501, 890 and 1491, away from PC-171. The two changes are adjacent, not overlapping: CARD-1112 inserts the park-bound sentence at doc:78 and the c1065-park-bound pin at test:72 and rewrites test:73, which shifts the lines the 1108 plan cites as doc:112 and test:73 to doc:113 and test:75. Either order merges cleanly; the second lander must re-read line numbers.
- S2 (D-2, D-3, D-5; not yet dispatched): replaces the "three visits, no discovery gate" sentence (doc:111, pin c1065-reclaim-page test:73), which is exactly the sentence and pin CARD-1112 rewrote, plus "The reconcile job's released total includes those visit counts." (doc:112, pin c1065-reclaim-count test:74) and the callers line (doc:109, pin c1065-reclaim-callers test:70). This is the real collision. If CARD-1112 lands first, the S2 brief should quote the new wording ("A short tail wraps. One call runs at most three passes of up to 32 rows (at most 96 row visits), and after wrap-around the same row at most three times. There is no discovery gate.") as its starting text. If S2 landed first, CARD-1112's edit of that sentence and pin would conflict and have to be redone.
- S4 (D-6): drops CARD-1108 from "Known limits stay on CARD-1097, CARD-1103, CARD-1104, and CARD-1108." (doc:114, unpinned); no overlap.

Recommendation: land CARD-1112 now, before CARD-1108 S2; S1 is unaffected either way.

## PCs

CARD-1065 PC-1 through PC-226 stay pending for method-scoped SourceLanding Mutation; the doc pins do not discharge PC-182 through PC-191.

## Unedited run lines

run: 20261006-213122-ebac   manifest: /work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/manifest.resolved.yaml
commit: a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d  branch: feat/card-task-e55c82cf  worktree: /work/worktrees/task-e55c82cf  host: Debian GNU/Linux 12 (bookworm) cores=24
source: a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d state=clean buildSource=verified
CHECKPOINT CP-1 commit=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d build=ok filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=158.9226485s startup=4.8525536s testsWall=1.6193019s teardown=0.4452693s hostWall=6.9171375s
CHECKPOINT CP-2 commit=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d build=reused filter=/*/*/BlockedTaskParkProjectionTests/C1065_* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=3.9013526s testsWall=0.0565841s teardown=1.1712276s hostWall=5.1291641s
CHECKPOINT CP-3 commit=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d build=reused filter=/*/*/InstructionBundleTests/* executed=72 passed=72 failed=0 skipped=0 trx=/work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=7.9475534s testsWall=0.4785556s teardown=0.8043579s hostWall=9.2304668s
CHECKPOINT CP-4 commit=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d build=reused filter=/*/*/(RunnerBranchContractDocumentationTests*)|(RepairSourceDocumentationTests*)|(PostLandMutationContractTests*)|(DockerStackDocumentationTests*)|(CheckpointRepeatDocumentationTests*)|(PublishedRoutingDocumentationTests*)|(TaskPlatformGuidanceTests*)|(CommitOnSettleDocumentationTests*)|(PostLandRetrospectiveContractTests*)|(CheckpointManifestDocumentationTests*)|(StandingPipelinePolicyDocumentationTests*)|(InstructionFileStampTests*)|(RunnerDefaultGuidanceTests*)/* executed=107 passed=107 failed=0 skipped=0 trx=/work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=0s startup=9.123819s testsWall=0.7940974s teardown=0.6209755s hostWall=10.5388916s
CHECKPOINT CP-5 commit=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d build=reused filter=/*/*/(AgentContextContractTests*)|(ScopeResolverAreaTests*)|(DelegationReportFormatterTests*)|(DelegationScopeLeaseTests*)|(OrchestratorWorkspaceLayoutTests*)|(AgentPresetsTests*)|(ChannelContractsTests*)|(ChannelOutboundContractTests*)|(PolicyRefreshDeltaTests*)|(ScopeDriftTests*)|(SlashCommandCatalogServiceTests*)|(PipelineDefinitionDocumentationTests*)|(CardFilePrivacyDocumentationTests*)|(DockerStackContractTests*)|(ScopedVerificationInstructionTests*)|(VerificationRoundInstructionTests*)/* executed=328 passed=328 failed=0 skipped=1 trx=/work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=36.5101091s testsWall=0.5059175s teardown=1.3484373s hostWall=38.3644642s
CHECKPOINT CP-6 commit=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d build=reused filter=/*/*/AgentBundleAttachmentTests/* executed=24 passed=24 failed=0 skipped=0 trx=/work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=0s startup=40.7524027s testsWall=2.0075923s teardown=1.6984879s hostWall=44.4584825s
CHECKPOINT CP-7 commit=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d build=reused filter=/*/*/DelegateBundleLaunchTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/rows/CP-7/run.trx slot=granted waited=15s dirty=0 source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d sourceState=clean buildSource=verified
PHASES CP-7 slotWait=15s build=0s startup=57.6371729s testsWall=0.1866672s teardown=3.4458364s hostWall=61.2696762s
CHECKPOINT CP-8 commit=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d build=reused filter=/*/*/GrokRulesCompositionTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-e55c82cf/.antiphon/checkpoints/20261006-213122-ebac/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=54.3273729s testsWall=5.9532277s teardown=1.299329s hostWall=61.5799298s
unlisted: none (the tool ran no other build or test command)
wall: 6m59s  sequential-equivalent: 4m18s  builds: 1  max-concurrent-builds: 1  rows: 8 green 0 red 0 skipped
outputs: deleted bin-c1112rv/
verdict: GREEN exit=0
EVIDENCE result commits=1 entries=0 violations=0 base=3599aa4b5d401112980c368662d476d49e46dff9 head=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d
CHECKPOINT SOURCE VALID source=a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d rows=7 (validate --rows CP-1,CP-2,CP-3,CP-4,CP-6,CP-7,CP-8)

--- review evidence ---
subjectTaskId: 07622e22-f12d-4f6d-b1d5-356960664356
reviewedSourceSha: a7b87d2b056fb3f0ecdd7342117ca28fa56ae45d
reviewedSourceClean: true
ordinaryScopeCompleted: Full
