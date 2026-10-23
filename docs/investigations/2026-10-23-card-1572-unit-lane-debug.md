# CARD-1572 bounded Unit census and fixes

Six inherited fixture/instruction causes repaired in seven commits; the 90-minute bounded census remains incomplete. No whole-lane green or release qualification is claimed.

## Identity and scope

- Task: 6422905c; branch: feat/card-task-6422905c; server2 Linux mirror: /work/worktrees/task-6422905c.
- Baseline: ffb1031fab448cc09929f8142b41f4fc46c923c9, descendant of commissioned master minimum 4497af9b48136a8003def54331f49ac66fd2dec9.
- Final code tip: 47aa3a0d0533d46d08d6437f8ee71e28bb89459e. Operator brief authorized Debug+Fix directly, without Plan or Review. Landing remains caller-owned.
- Pinned rc tested: 9e97df3f7b3d594c6872a1327502d6877feaf820. The scratch worktree was restored and removed. No rc push, tag, cut, promotion or publication.
- Discovery: TUnit 1.44.0 / MTP 2.2.2, .NET runtime 9.0.21, SDK 10.0.402, Debian Linux, host 0c18bb7f591e. 19,807 assembly cases; 6,075 admitted Unit cases, 456 classes. Policy safe-environment clearing was applied.
- Exact discovery/UID membership: /work/worktrees/task-6422905c/.antiphon/1572/discovery/nodes.json and /work/worktrees/task-6422905c/.antiphon/1572/unit-membership.json. Every class appears in the table below.
- Raw evidence root: /work/worktrees/task-6422905c/.antiphon/1572. Generated TRX/JSON/diagnostics/logs remain ignored; this Markdown retains the essential census, attribution, timings and provenance.

Census evidence summary: 187 classes have complete first-census TRX, 126 have native-only/partial evidence, and 143 have no executed-case evidence (141 in the three unrun groups plus two deferred classes inside unit-02). Later repair evidence is reported separately.

## Repairs and attribution

All initial reds were witnessed on the unchanged committed baseline before source edits. Attribution uses touching-file history and contract inspection; no bisect was claimed.

| Cause | Witness | Classification / history | Repair and green selection |
|---|---|---|---|
| Claude startup/prompt fakes | 16 failures across the three RunnerClaudeAdapter*PromptTests classes, observation-unknown | Fixture defect after guarded writer 4de01c9a50ceb5874294b70f79deaa718b57b908 | Bind accepted generation, epoch, input revision, readable composer and conditional writes; validate finite intents; dispose adapter watchers. Existing input ordering, withheld Enter, probe and dialog assertions retained. 20 adapter cases passed in known-green-fast. |
| Stage instruction literals | 11 failures in InstructionBundleTests, PostLandMutationContractTests, ScopedVerificationInstructionTests, VerificationRoundInstructionTests | Instruction-text drift after e17c636c1168c131b7ac4e99ccffc02653ab0a13 / 76be7abe4fec22265fcc0d2e22d112dc3b8f85c3 | Pin equivalent current wording, including read-only PCs, Final-only land and required V/R. No bundle policy changed. Unchanged non-roster cases passed in known-green-fast. |
| Bounded-reader probe bootstrap | script-eval ordinary/held: missing Set-AntiphonBuildDriverEnvironment; script-git held: emergency watchdog at 20.092s despite PASS output | Fixture dependency/setup; build-driver integration c15961b94b3ddef659af9fb9c32175422faf5e5c (rc has equivalent source under different history) | Import the helper and preload staged Antiphon.HostCleanup instead of compiling the process boundary for every probe. Existing deadlines and no-survivor assertions retained. reader-green passed all 11; script-git held completed in 16.591s (20s unchanged cap), expired=false, survivor=false. |
| Dispatch warning roster | C1155_RosterNamesEveryDispatchWarningE2EMethod | Fixture/manifest drift after 208277fcea8e684f3e6faff465487a50e302c959 added the thirteenth E2E method | Restore CP-14 for C540_PreEnqueueWaitSurvivesSlowMaterialization; update budget 54→58 minutes. Windows E2E remains pending; 4-minute new row estimate is explicitly provisional. All 11 roster guards passed. |
| OS-conditioned pin cases | Two C1483_S3_OsConditionedElementNeedsPin parameters failed; plain OS property parameter passed | Fixture expectation drift after EvaluationExpressionPolicy 2bed847522cacd78a23ccfe93d6039dcb043c5e2 | Exact pins do not bypass unsupported-function refusal. Preserve missing/changed/moved pin assertions and require the precise refusal at @Condition. graph-pins-green covers all three parameters. |
| Linux source gate roster | C1210_Roster_closure_matches_source_gates first found C1374CensusProbes.Check; repair then exposed C1335 | Fixture source roster drift: C1374 addition 698d73f1a0928803a0ef4f6e5999b2ed4f392532; C1364 host witnesses 4e6346f254d6223a1d9c682a6ea8487d7c9f6305; C1335 80390bd197b41c0f20f9c5c9e09bfd227fd01203 / C1338 1b90a473197d4405aedcead1fc0964f8449c829c | Move identical C1374 native gate to its host-only entry point and roster it. Add all five further missing entries (C1364 host admission/post-removal/cleanup, C1335 mirror-owner helper, C1338 dry-run volumes). No exemption or weakened closure check. roster-green is the narrow rerun. |

Commits, in order:

- 47ff5e3cd946926f00ea8bf28792d705098f8232 test(CARD-1572): bind Claude startup fakes to guarded input
- 60e083580b82766dba5a239fd6eb8fb35ffbe3a0 test(CARD-1572): pin the current equivalent stage instructions
- 58435535cabb574789e93a3dc9ce8337e3820a22 test(CARD-1572): preload bounded reader probe dependencies
- 071a5c2ba8d64117e4ff38d396165de479d867f2 test(CARD-1572): restore dispatch warning roster closure
- 643df0f7980752c82778c856c0b67fd607854dc0 test(CARD-1572): honor evaluation refusal in OS pin cases
- 0adc5d8d3e70e9b6af274f4aff99d9c69cbdf306 test(CARD-1572): roster the census probe Linux gate
- 47aa3a0d0533d46d08d6437f8ee71e28bb89459e test(CARD-1572): close all newly added Linux gate roster entries

## Execution and limits

All build/test drivers ran under build-slot.ps1, either directly or as sequential children of its leased batch driver. No unleased retry or NoSlot. One shared baseline build, then necessary rebuilds per repair source tip; rc compiles used a detached scratch worktree. Tests never overlapped each other, and source was frozen during each run.

- First build admission: exit 4 after two minutes, no build executed. Second leased admission: 60 seconds, baseline build 213.14 seconds, 0 errors.
- First repair build: 195.28 seconds, 0 errors. Follow-up roster build: 211.85 seconds, 0 errors. Historical compiler/analyzer warnings retained in logs.
- Rc first three-pick compile: 163.70 seconds, 0 errors. Final four-pick compile: 169.60 seconds, 0 errors.
- Discovery queue: 105 seconds; discovery is inventory, never execution evidence.
- The baseline batch held one lease for 2,150 seconds and stopped at a class-group boundary after its 30-minute collection budget was reached. Each group had a separate hard deadline, at most 620 seconds. CARD-1572 itself owns this over-30-minute diagnostic selection.
- Caps were provisional collection ceilings, not measured p95. No test timeout was widened. Source/host/load/commands, DLL hashes and timestamps are retained per run in run.json. The initial null DLL hash (wrong assumed TFM subdirectory) is corrected append-only in base-build-provenance.json, captured before any source change/rebuild.
- TRX contains per-case duration and class wall span. SumSeconds is nested/parallel span total; SpanSeconds is wall interval for that class. Neither is the group's wall time. Container, readiness, DB, IO/HTTP/CLI subphases and teardown not instrumented in a case remain unknown/unattributed, not zero. No complete startup/phase qualification is claimed.

| Run | Source | Intended cases | Wall seconds (excludes outer slot queue) | Exit | TRX pass/fail/skip |
|---|---|---:|---:|---:|---|
| known-red | ffb1031fab44 | 173 | 218.773 | 2 | 142/31/0 |
| graph-pins-red | ffb1031fab44 | 3 | 15.487 | 2 | 1/2/0 |
| unit-01 | ffb1031fab44 | 276 | 218.455 | 0 | 276/0/0 |
| unit-02 | ffb1031fab44 | 568 | 620.371 | 124 | none; see partial diagnostic |
| unit-03 | ffb1031fab44 | 569 | 12.200 | 0 | 568/0/1 |
| unit-04 | ffb1031fab44 | 570 | 30.882 | 0 | 564/0/6 |
| unit-05 | ffb1031fab44 | 570 | 589.577 | 0 | 571/0/0 |
| unit-06 | ffb1031fab44 | 570 | 41.059 | 0 | 569/0/1 |
| unit-07 | ffb1031fab44 | 570 | 620.397 | 124 | none; see partial diagnostic |
| known-green-fast | 0adc5d8d3e70 | 167 | 30.575 | 2 | 165/1/1 |
| roster-green | 47aa3a0d0533 | 5 | 35.503 | 0 | 4/0/1 |
| reader-green | 47aa3a0d0533 | 11 | 317.989 | 0 | 11/0/0 |
| graph-pins-green | 47aa3a0d0533 | 3 | 5.698 | 0 | 3/0/0 |
| unit-11 | 47aa3a0d0533 | 499 | 328.576 | 124 | none; see partial diagnostic |
| unit-08 | 47aa3a0d0533 | 570 | 1.147 | 124 | none; see partial diagnostic |

## Completed repair evidence

known-green-fast at 0adc5d8d3e70e9b6af274f4aff99d9c69cbdf306: 167 total, 165 passed, 1 failed, 1 skipped. The failure exposed further roster omissions, fixed in 47aa3a0d0533d46d08d6437f8ee71e28bb89459e; retain the historical failure. The 162 cases outside WindowsUnitPlatformAdmissionTests passed and were unchanged by the five-row follow-up. The latest completed repair receipts cover 181 distinct cases: 180 passed, one Windows-only skip, zero unresolved assertion failures. Fresh roster/reader/OS-pin selections and their exact source hashes are shown above. Inspect their nonzero counters before crediting them; a stopped group is incomplete.

## Open work / contaminated outcomes

- The full Unit lane is not certified green. Three groups (141 classes, 1,710 cases) have no execution evidence: unit-08 was launched with only one second left in the collector budget and produced no console, diagnostics or TRX, so it is counted as unrun; unit-09/unit-10 were not launched. Classes marked unrun, count mismatch, or partial below remain pending. The 90-minute budget included fixes and required rc checks, not just test execution.
- unit-02 hit 620 seconds without TRX: 543 Passed, 8 Skipped, 3 native Failed, 14 no terminal record. Only C1210's missing-roster assertion was independent of the deadline. C1364_Host_recycle_post_removal_reports_waited reported phase preflight versus removing at 20:04:21.9447932, about 13ms after SIGTERM's deadline; the shell was interrupted. C1364_Host_cleanup_reports_waited was canceled before execution. Both need clean exact-method runs; do not label either a product regression from this contaminated run.
- unit-07 hit 620 seconds without TRX: 551 Passed, 13 Skipped, 1 native Failed, 5 no terminal record. The final failure C1008_Refusal_receipts_do_not_leak_secrets reports an interrupted previous-compose probe at 20:25:56.0577679, about 30ms after the 20:25:56.027 deadline; keep it deadline-contaminated pending a clean method run.
- Final RemoteScriptContractTests window (unit-11): 328-second cap, no TRX; 29 native passes, six explicit cancellation failures, 464 cases with no terminal record. No independent assertion red was observed. These canceled/missing cases remain pending; the six canceled UID identities are retained in unit-11/partial-nodes.json.
- Do not blindly retry any truncated broad group. Recover only missing/canceled exact methods from the retained UID manifest, then measure the expensive matrix methods individually.
- Windows DispatchBaseWarningDeliveryE2ETests rows CP-2..CP-14 remain pending. Native Windows skip witnesses were skipped on Linux. Rc compile is not rc qualification or activation.

## R1 split proposal (not implemented)

Use one shared isolated build, then serial class/method pieces targeted at about 10 minutes, hard cap below 30 minutes. Never credit a truncated piece. For this Linux host, the observed complete classes can be conservatively packed by adding the maximum complete class wall span seen across census and repair runs (overestimates overlap); proposed packs below each sum to at most 600 observed seconds. Use approximately 2x measured p95 once available; these n=1, contended-host samples are provisional. Do not promote the old Windows pace estimate (RemoteScriptContractTests: 4,591 seconds) into a Linux measurement.

| Proposed piece | Classes | Sum of observed class spans (s) |
|---|---:|---:|
| P-1 | 5 | 600.000 |
| P-2 | 16 | 600.000 |
| P-3 | 167 | 220.442 |

The class table maps every measured class to a proposed pack. For incomplete/unmeasured classes, collect bounded method samples first; keep them in explicit pending pieces until costs are known. In particular:

- C1364BoundLandWaitProofTests: six method pieces; each host method contains many fixture vectors. Preserve vector/boundary assertions and collect each method separately.
- RollingVolumeRecycleScriptTests and RemoteScriptContractTests: own method groups, splitting the expensive host/wrapper matrices from source-text guards. No whole-class estimate below 30 minutes is established.
- HostJqPrerequisiteScriptTests: measured 454.139s class wall span, 39 cases; keep separate from pure policy checks. TestClassificationPolicyTests adds 98.863s of serial script work.
- Checkpoint FIFO/drain/custody tests: separate serial process pieces; bounded-reader held evaluations deliberately spend two 120s deadlines.
- Cache immutable parsed source/compose data once per run where safe; retain isolated mutable fixture state per vector. Avoid replaying immutable parsing and Add-Type compilation. CARD-1514/1404 own fixture/timing/cost improvements; this report changes neither lane policy nor nightly packing.

## Per-class census and durations

P/F/S are historical first-census outcomes, preserving reds even when later repaired. Native-only rows have unknown TRX duration; SpanSeconds uses the native diagnostic emission interval when available, which can include scheduler/logging time. Deadline-adjacent Failed events in partial groups are not independent product reds. Case count mismatches and unrun classes stay pending.

| Class | Group | Intended | Terminal | P/F/S | SumSeconds | SpanSeconds | Evidence | Proposed pack |
|---|---|---:|---:|---|---:|---:|---|---|
| Antiphon.Tests.Agents.AgentLaunchSpecTests | unit-03 | 2 | 2 | 2/0/0 | 0.041 | 0.023 | TRX complete | P-3 |
| Antiphon.Tests.Agents.AgentProtocolAdapterFactoryTests | unit-10 | 6 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.AgentRegistrySettingsTests | unit-06 | 23 | 23 | 22/0/1 | 0.923 | 0.183 | TRX complete | P-3 |
| Antiphon.Tests.Agents.AgentRegistryTests | unit-08 | 17 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.AgentSessionSettingsTests | unit-01 | 2 | 2 | 2/0/0 | 0.073 | 0.177 | TRX complete | P-3 |
| Antiphon.Tests.Agents.CodexAdapterRawResponseTests | unit-01 | 3 | 3 | 3/0/0 | 0.100 | 0.204 | TRX complete | P-3 |
| Antiphon.Tests.Agents.CodexResponseAnalyzerTests | unit-04 | 12 | 12 | 12/0/0 | 1.483 | 0.623 | TRX complete | P-3 |
| Antiphon.Tests.Agents.CodexWorkingIndicatorTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.FakeLlmApiSelfTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.GrokAdapterTests | unit-01 | 2 | 2 | 2/0/0 | 0.327 | 0.309 | TRX complete | P-3 |
| Antiphon.Tests.Agents.GrokCredentialStoreTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.GrokFreshWorktreeQualificationTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.GrokLinuxBlockingPromptTests | unit-08 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.GrokLinuxStartupReadinessTests | unit-07 | 21 | 21 | 21/0/0 | unknown | 1.303 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Agents.GrokModelListParserTests | unit-01 | 2 | 2 | 2/0/0 | 0.170 | 0.845 | TRX complete | P-3 |
| Antiphon.Tests.Agents.GrokSignInPromptDetectorTests | unit-08 | 17 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.GrokStartupCaptureStoreTests | unit-01 | 2 | 2 | 2/0/0 | 0.802 | 0.740 | TRX complete | P-3 |
| Antiphon.Tests.Agents.GrokStartupReadinessTests | unit-09 | 5 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.GrokStartupSizeTests | unit-05 | 18 | 18 | 18/0/0 | 1.569 | 0.399 | TRX complete | P-3 |
| Antiphon.Tests.Agents.GrokTrustPromptDetectorTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 15.866 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Agents.HerdrEnvelopeBodyTests | unit-01 | 2 | 2 | 2/0/0 | 0.004 | 0.754 | TRX complete | P-3 |
| Antiphon.Tests.Agents.OpenCodeAdapterTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 16.013 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests | known-red | 10 | 10 | 1/9/0 | 14.237 | 2.589 | TRX complete | P-3 |
| Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests | known-red | 8 | 8 | 3/5/0 | 1.821 | 0.237 | TRX complete | P-3 |
| Antiphon.Tests.Agents.RunnerClaudeAdapterVerifiedPromptTests | known-red | 2 | 2 | 0/2/0 | 0.346 | 0.194 | TRX complete | P-3 |
| Antiphon.Tests.Agents.RunnerCodexAdapterReadyTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.RunnerCodexAdapterSubmitConfirmTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.RunnerCodexAdapterTurnCompleteTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.RunnerGrokAdapterReadyTests | unit-07 | 22 | 22 | 22/0/0 | unknown | 6.518 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Agents.RunnerGrokAdapterSignInPromptTests | unit-01 | 3 | 3 | 3/0/0 | 3.285 | 1.452 | TRX complete | P-3 |
| Antiphon.Tests.Agents.RunnerGrokAdapterTrustPromptTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 20.310 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.RunnerTerminalSessionGenerationTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.RunnerTerminalSessionInputEncodingTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 16.056 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Agents.TestOwnedPtyHostIdentityTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Agents.TestOwnedPtyHostPolicyTests | unit-07 | 22 | 22 | 22/0/0 | unknown | 0.163 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Agents.TranscriptNormalizerTests | unit-05 | 38 | 38 | 38/0/0 | 1.274 | 0.390 | TRX complete | P-3 |
| Antiphon.Tests.Agents.TranscriptTurnBaselineTests | unit-01 | 3 | 3 | 3/0/0 | 0.090 | 0.056 | TRX complete | P-3 |
| Antiphon.Tests.AgentTui.AgentTuiOperationCoordinatorTests | unit-01 | 1 | 1 | 1/0/0 | 0.115 | 0.115 | TRX complete | P-3 |
| Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests | unit-04 | 65 | 65 | 65/0/0 | 87.836 | 4.864 | TRX complete | P-3 |
| Antiphon.Tests.AgentTui.AgentTuiSmokeScriptTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 16.617 | partial native; no TRX | pending method sample |
| Antiphon.Tests.AgentTui.DollarEnvArgTests | unit-09 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.AgentTui.RunnerProbePowerShellFixtureTests | unit-08 | 15 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.AgentTui.RunnerProcessProbeRedactionTests | unit-04 | 62 | 62 | 62/0/0 | 0.731 | 0.683 | TRX complete | P-3 |
| Antiphon.Tests.ApiKeys.AgentLaunchEnvTests | unit-06 | 31 | 31 | 31/0/0 | 0.565 | 0.195 | TRX complete | P-3 |
| Antiphon.Tests.ApiKeys.ApiKeyPlaceholderTests | unit-08 | 18 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.ApiKeys.ApiKeyProtectorTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 15.879 | partial native; no TRX | pending method sample |
| Antiphon.Tests.ApiKeys.ProviderEndpointProjectionTests | unit-06 | 34 | 34 | 34/0/0 | 0.497 | 0.127 | TRX complete | P-3 |
| Antiphon.Tests.Application.AbsentLaunchPolicyTests | unit-03 | 87 | 87 | 87/0/0 | 0.740 | 0.112 | TRX complete | P-3 |
| Antiphon.Tests.Application.AgentContextContractTests | unit-01 | 2 | 2 | 2/0/0 | 0.186 | 0.403 | TRX complete | P-3 |
| Antiphon.Tests.Application.AgentExecutableResolverTests | unit-02 | 5 | 5 | 4/0/1 | unknown | 15.645 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.AgentPinPathTests | unit-02 | 4 | 4 | 3/0/1 | unknown | 15.645 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.AgentPresetsTests | unit-10 | 7 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.AgentSessionRuntimeBoundedReadTests | unit-01 | 3 | 3 | 3/0/0 | 2.168 | 1.544 | TRX complete | P-3 |
| Antiphon.Tests.Application.AgentTaskDecisionQuestionTests | unit-08 | 18 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.AgentTaskDispatcherPredicateTests | unit-08 | 19 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.AgentTaskLandingStateTests | unit-04 | 82 | 82 | 82/0/0 | 2.448 | 0.383 | TRX complete | P-3 |
| Antiphon.Tests.Application.AgentTaskLandQueueVisibilityTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 15.714 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.AgentTaskLandVerificationEvidenceTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.AgentTaskLivenessTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 15.720 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.AlarmSettingsValidatorTests | unit-01 | 1 | 1 | 1/0/0 | 0.019 | 0.019 | TRX complete | P-3 |
| Antiphon.Tests.Application.ApiErrorClassifierTests | unit-08 | 16 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ApiErrorRetryScheduleTests | unit-10 | 7 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.AreaMapContractTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 15.895 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.AreaMapLoaderTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.AttentionKindWireTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 15.790 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.AttentionSettingsValidatorTests | unit-01 | 2 | 2 | 2/0/0 | 0.053 | 0.035 | TRX complete | P-3 |
| Antiphon.Tests.Application.AwayDigestFormatterTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.BlockedQuestionTests | unit-10 | 7 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.BlockedTaskParkProjectionTests | unit-01 | 2 | 2 | 2/0/0 | 0.183 | 0.188 | TRX complete | P-3 |
| Antiphon.Tests.Application.BoardServiceNeedsHumanReviewTests | unit-10 | 7 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.BootStallDocumentationTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 15.738 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.BootStallPolicyTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.BootWedgeContractTests | unit-01 | 2 | 2 | 2/0/0 | 0.078 | 0.099 | TRX complete | P-3 |
| Antiphon.Tests.Application.CapacityRecoveryPolicyTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CardAliasNormalizationTests | unit-10 | 7 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CardFilePolicyTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 15.746 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.CardFilePrivacyDocumentationTests | unit-01 | 1 | 1 | 1/0/0 | 0.075 | 0.075 | TRX complete | P-3 |
| Antiphon.Tests.Application.CardIdentifierAllocatorTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 15.665 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.CardRankingOrderTests | unit-01 | 3 | 3 | 3/0/0 | 1.635 | 0.902 | TRX complete | P-3 |
| Antiphon.Tests.Application.CardRankingTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CardTaskFileRendererTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CardWorkflowRunFactoryTests | unit-01 | 1 | 1 | 1/0/0 | 1.439 | 1.439 | TRX complete | P-3 |
| Antiphon.Tests.Application.ChannelContractsTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ChannelInboundDebouncerTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 15.951 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ChannelMachineTurnMatchTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ChannelOutboundContractTests | unit-01 | 2 | 2 | 2/0/0 | 1.375 | 0.822 | TRX complete | P-3 |
| Antiphon.Tests.Application.ChannelOutboundEvidenceAccountingTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 16.003 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ChannelOutboundStorageTests | unit-06 | 33 | 33 | 33/0/0 | 16.604 | 16.626 | TRX complete | P-2 |
| Antiphon.Tests.Application.ChannelPromptCorrelationUnitTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ChannelReplyAttachmentReaderTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 16.584 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.CheckCompactionContinuationSettingsTests | unit-01 | 1 | 1 | 1/0/0 | 0.157 | 0.157 | TRX complete | P-3 |
| Antiphon.Tests.Application.CheckInterpretationValidatorTests | unit-05 | 36 | 36 | 36/0/0 | 0.178 | 0.224 | TRX complete | P-2 |
| Antiphon.Tests.Application.CheckpointManifestDocumentationTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CheckScheduleBackoffTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 15.743 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.CheckScheduleElapsedTimesTests | unit-01 | 3 | 3 | 3/0/0 | 0.010 | 0.004 | TRX complete | P-2 |
| Antiphon.Tests.Application.CheckScheduleRoundingTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 15.733 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.CheckSpecialistLaunchPolicyTests | unit-07 | 20 | 20 | 20/0/0 | unknown | 0.194 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ClaudeEffortByRoleSettingsTests | unit-08 | 18 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ClaudeLaunchArgsTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.063 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests | unit-09 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CodexLaunchArgsTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CodexPhoneHomeProjectionTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.175 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ColumnTextTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.013 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.CommitOnSettleDocumentationTests | unit-01 | 2 | 2 | 2/0/0 | 0.029 | 0.027 | TRX complete | P-3 |
| Antiphon.Tests.Application.CommitOnSettlePolicyResolverTests | unit-07 | 24 | 24 | 24/0/0 | unknown | 0.023 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.CommitRecoveryObligationsTests | unit-08 | 17 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CompactionContinuationPolicyTests | unit-08 | 19 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.CompletionHeaderOverlapTests | unit-01 | 3 | 3 | 3/0/0 | 0.338 | 0.127 | TRX complete | P-3 |
| Antiphon.Tests.Application.CompletionNoteWorkQueueTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.121 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ComplexityEffectiveComposeTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ComplexityRoutingComposeTests | unit-08 | 16 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ContextCompactionSettingsTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.030 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.D2AdvisoryDocumentationTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.161 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.DelegateBindRefusalRecoveryTests | unit-09 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DelegateBranchNameTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DelegationAllowedRootsFileTests | unit-01 | 1 | 1 | 1/0/0 | 0.001 | 0.001 | TRX complete | P-3 |
| Antiphon.Tests.Application.DelegationCapabilityContractTests | unit-01 | 2 | 2 | 2/0/0 | 0.583 | 0.553 | TRX complete | P-3 |
| Antiphon.Tests.Application.DelegationCostTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.026 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.DelegationDenyHookPolicyTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.013 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.DelegationKindPricingTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DelegationLeaseSettingsTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DelegationPoolDefaultsTests | unit-01 | 1 | 1 | 1/0/0 | 0.024 | 0.024 | TRX complete | P-3 |
| Antiphon.Tests.Application.DelegationQuestionDetectionTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DelegationReportFormatterTests | unit-03 | 93 | 93 | 92/0/1 | 2.018 | 0.188 | TRX complete | P-3 |
| Antiphon.Tests.Application.DelegationScopeLeaseTests | unit-08 | 16 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DelegationWorkspaceBoundaryTests | unit-08 | 15 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DeliveryBackendCeilingsTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.035 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.DesktopCapabilitiesSnapshotTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.069 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.DiagnosisTests | unit-05 | 42 | 42 | 42/0/0 | 0.840 | 0.259 | TRX complete | P-3 |
| Antiphon.Tests.Application.DiagnosticsRedactorTests | unit-08 | 18 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DirectoryBrowseServiceTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DirectoryMatcherTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.119 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.DispatchBaseWarningRosterTests | known-red | 11 | 11 | 10/1/0 | 3.087 | 0.355 | TRX complete | P-3 |
| Antiphon.Tests.Application.DispatchConcurrencyPolicyTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.DispatchHoldLedgerTests | unit-07 | 23 | 23 | 23/0/0 | unknown | 0.075 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ExpectationDirectiveTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.085 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ExpectationPipelineTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.GitIndexLockTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.GrokComposerScreenTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.GrokDeliveryShapeTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.GrokLaunchArgsTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.GrokNativeSessionResumeTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.019 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.GrokQuestionPopupTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.006 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.GrokRulesLiveEvidenceTests | unit-01 | 1 | 1 | 1/0/0 | 0.582 | 0.582 | TRX complete | P-3 |
| Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests | unit-07 | 24 | 24 | 12/0/12 | unknown | 0.515 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.HerdrAgentKindMapTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.058 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.HerdrLaunchContextTitleTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.005 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.HerdrStatusDisagreementTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.HerdrSupervisionFailureEvidenceTests | unit-07 | 24 | 24 | 24/0/0 | unknown | 7.222 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.HostCleanupWorktreeInventoryTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.259 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.HostStatsAntiphonCountersTests | unit-01 | 3 | 3 | 3/0/0 | 0.183 | 0.080 | TRX complete | P-3 |
| Antiphon.Tests.Application.HostStatsCacheTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.034 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.InstructionBundleTests | known-red | 78 | 78 | 72/6/0 | 1.602 | 0.396 | TRX complete | P-3 |
| Antiphon.Tests.Application.InstructionFileStampTests | unit-09 | 15 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.InstructionFragmentTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.140 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.InternalDecisionPolicyTests | unit-04 | 51 | 51 | 51/0/0 | 1.034 | 0.283 | TRX complete | P-3 |
| Antiphon.Tests.Application.IssueTrackerConfigParserTests | unit-01 | 3 | 3 | 3/0/0 | 3.262 | 1.108 | TRX complete | P-3 |
| Antiphon.Tests.Application.KeepSixDocumentationTests | unit-01 | 3 | 3 | 3/0/0 | 0.055 | 0.582 | TRX complete | P-3 |
| Antiphon.Tests.Application.KeepSixProjectionTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.174 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.LandFailureDiagnosticTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.LandMergeWriterTests | unit-01 | 1 | 1 | 1/0/0 | 0.008 | 0.008 | TRX complete | P-1 |
| Antiphon.Tests.Application.LandReceiptScanCacheTests | unit-03 | 115 | 115 | 115/0/0 | 1.970 | 0.172 | TRX complete | P-3 |
| Antiphon.Tests.Application.LandVerificationBuildSlotTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.LaunchModelArgumentAppenderTests | unit-01 | 2 | 2 | 2/0/0 | 0.090 | 0.086 | TRX complete | P-3 |
| Antiphon.Tests.Application.LeaseTurnScenarioTests | unit-01 | 1 | 1 | 1/0/0 | 1.081 | 1.081 | TRX complete | P-3 |
| Antiphon.Tests.Application.MatrixDefaultComplexityCreateTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.MechanicalPcContractTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.MidTurnInterruptDocumentationTests | unit-01 | 1 | 1 | 1/0/0 | 0.053 | 0.053 | TRX complete | P-3 |
| Antiphon.Tests.Application.MidTurnInterruptPolicyTests | unit-06 | 33 | 33 | 33/0/0 | 0.565 | 0.050 | TRX complete | P-3 |
| Antiphon.Tests.Application.ModelAliasTests | unit-04 | 76 | 76 | 76/0/0 | 0.278 | 0.100 | TRX complete | P-3 |
| Antiphon.Tests.Application.ModelLevelAliasDisplayTests | unit-07 | 24 | 24 | 24/0/0 | unknown | 0.041 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.MutationRoleContractTests | unit-01 | 2 | 2 | 2/0/0 | 0.214 | 0.346 | TRX complete | P-3 |
| Antiphon.Tests.Application.OperatorShutdownCoordinatorTests | unit-01 | 2 | 2 | 2/0/0 | 4.141 | 2.236 | TRX complete | P-3 |
| Antiphon.Tests.Application.OrchestratorBundleOwnershipTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.OrchestratorInstructionsDeltaTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.127 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.OrchestratorInstructionsGuidanceTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.097 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.OrchestratorInstructionsPathTests | unit-01 | 2 | 2 | 2/0/0 | 0.068 | 0.081 | TRX complete | P-3 |
| Antiphon.Tests.Application.OrchestratorInstructionsRecipientTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.082 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.OrchestratorInstructionsRendererTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.OrchestratorInvestigationDetectorTests | unit-08 | 17 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.OrchestratorSettingsTests | unit-01 | 1 | 1 | 1/0/0 | 0.013 | 0.013 | TRX complete | P-3 |
| Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests | unit-07 | 21 | 21 | 21/0/0 | unknown | 0.163 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.OutboundConversionManifestTests | unit-05 | 35 | 35 | 35/0/0 | 0.481 | 0.359 | TRX complete | P-3 |
| Antiphon.Tests.Application.OutputDistillationGateTests | unit-03 | 103 | 103 | 103/0/0 | 7.565 | 0.467 | TRX complete | P-3 |
| Antiphon.Tests.Application.OutputDistillationPolicyTests | unit-07 | 25 | 25 | 25/0/0 | unknown | 0.071 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.OutputDistillationQueueTests | unit-01 | 2 | 2 | 2/0/0 | 0.442 | 0.457 | TRX complete | P-3 |
| Antiphon.Tests.Application.PhoneHomeDirectoryTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 6.856 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.PhoneHomeRunnerRetirementIdentityTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.PhoneHomeRunnerSettingsValidatorTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.PhoneHomeSpillTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.069 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.PhoneHomeTaskRoutingTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.PipelineDefinitionDocumentationTests | unit-01 | 1 | 1 | 1/0/0 | 0.010 | 0.010 | TRX complete | P-3 |
| Antiphon.Tests.Application.PipelineHandoffParseTests | unit-05 | 42 | 42 | 42/0/0 | 0.403 | 0.030 | TRX complete | P-3 |
| Antiphon.Tests.Application.PipelineHandoffRouteDocumentationTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.020 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.PipelineStagesJsonTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.190 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.PlanCatalogServiceTests | unit-06 | 28 | 28 | 28/0/0 | 6.825 | 4.548 | TRX complete | P-3 |
| Antiphon.Tests.Application.PolicyRefreshDeltaTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.037 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.PostLandMutationContractTests | known-red | 30 | 30 | 28/2/0 | 0.268 | 0.057 | TRX complete | P-3 |
| Antiphon.Tests.Application.PostLandMutationPublicationTests | unit-06 | 33 | 33 | 33/0/0 | 0.148 | 0.032 | TRX complete | P-3 |
| Antiphon.Tests.Application.PostLandMutationReceiptPolicyTests | unit-07 | 25 | 25 | 25/0/0 | unknown | 0.383 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.PostLandRetrospectiveContractTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.007 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ProviderCapacityNoticeTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.043 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ProviderContractCatalogTests | unit-05 | 37 | 37 | 37/0/0 | 0.631 | 0.034 | TRX complete | P-3 |
| Antiphon.Tests.Application.ProviderEndpointDocumentationTests | unit-01 | 3 | 3 | 3/0/0 | 0.176 | 0.373 | TRX complete | P-3 |
| Antiphon.Tests.Application.ProviderQuotaRefusalTests | unit-08 | 18 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.PtyDeliveryCeilingsTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.PtyInlineCeilingTests | unit-01 | 3 | 3 | 3/0/0 | 0.018 | 0.059 | TRX complete | P-3 |
| Antiphon.Tests.Application.PublishedRoutingDocumentationTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ReceiptScanDestinationTests | unit-06 | 34 | 34 | 34/0/0 | 0.341 | 0.084 | TRX complete | P-3 |
| Antiphon.Tests.Application.ReleaseBranchModelDocumentationTests | unit-01 | 2 | 2 | 2/0/0 | 0.146 | 0.269 | TRX complete | P-3 |
| Antiphon.Tests.Application.RemoteControlMenuScreenTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.RemoteControlPolicyTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.096 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.RemoteVerificationWorkspaceTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.063 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.RemoteWorktreeMirrorTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.RepairSourceDocumentationTests | unit-01 | 3 | 3 | 3/0/0 | 0.442 | 0.209 | TRX complete | P-3 |
| Antiphon.Tests.Application.RepositoryLeaseWaitersTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.075 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.RestartFailureClassificationTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.072 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ReviewEvidenceParserTests | unit-07 | 21 | 21 | 21/0/0 | unknown | 0.056 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.RolePolicyConfigurationValidationTests | unit-07 | 22 | 22 | 22/0/0 | unknown | 0.463 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.RolePolicyReadServiceTests | unit-08 | 21 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.RolePolicyToolBundleTests | unit-08 | 19 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.RoutingCandidateParseTests | unit-01 | 1 | 1 | 1/0/0 | 1.977 | 1.977 | TRX complete | P-3 |
| Antiphon.Tests.Application.RunnerAbsenceEvidenceValidatorTests | unit-05 | 35 | 35 | 35/0/0 | 0.597 | 0.039 | TRX complete | P-3 |
| Antiphon.Tests.Application.RunnerBranchContractDocumentationTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.RunnerCapabilitiesFailureTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.RunnerContractMapperTranscriptEventTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.214 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.RunnerDefaultGuidanceTests | unit-02 | 4 | 4 | 4/0/0 | unknown | 0.013 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.RunnerReadSourcePinTests | unit-01 | 1 | 1 | 1/0/0 | 0.107 | 0.107 | TRX complete | P-3 |
| Antiphon.Tests.Application.RunnerSlotRulesTests | unit-01 | 2 | 2 | 2/0/0 | 6.947 | 6.938 | TRX complete | P-3 |
| Antiphon.Tests.Application.ScheduleRecurrenceTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.054 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ScopeDriftTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.ScopedVerificationInstructionTests | known-red | 17 | 17 | 16/1/0 | 0.153 | 0.026 | TRX complete | P-3 |
| Antiphon.Tests.Application.ScopeOverlapPolicyTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.016 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.ScopeResolverAreaTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.SeatOccupancyProjectionTests | unit-09 | 15 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.SessionContextUsageTests | unit-06 | 34 | 34 | 34/0/0 | 0.347 | 0.050 | TRX complete | P-3 |
| Antiphon.Tests.Application.SessionDeliveryProfileTests | unit-02 | 7 | 7 | 5/0/2 | unknown | 7.968 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.SessionReAdoptionStateTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.140 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.SessionRunnerAbsenceEvidenceDocumentationTests | unit-01 | 1 | 1 | 1/0/0 | 0.502 | 0.502 | TRX complete | P-3 |
| Antiphon.Tests.Application.SessionRunnerHttpClientHostStatsTests | unit-01 | 3 | 3 | 3/0/0 | 0.776 | 1.082 | TRX complete | P-3 |
| Antiphon.Tests.Application.SessionRunnerSettingsTests | unit-01 | 1 | 1 | 1/0/0 | 0.026 | 0.026 | TRX complete | P-3 |
| Antiphon.Tests.Application.SettlementSyncDebtAttentionTests | unit-01 | 1 | 1 | 1/0/0 | 0.137 | 0.137 | TRX complete | P-3 |
| Antiphon.Tests.Application.SettlementSyncDebtPolicyTests | unit-06 | 31 | 31 | 31/0/0 | 2.188 | 0.367 | TRX complete | P-3 |
| Antiphon.Tests.Application.SharedWriterLeaseProjectionTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.067 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.SiblingInspectionResultsTests | unit-01 | 3 | 3 | 3/0/0 | 0.464 | 0.166 | TRX complete | P-3 |
| Antiphon.Tests.Application.SiblingWarningReducerTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.138 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.SlashCommandCatalogServiceTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.SlashCommandParserTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.321 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.SourceLandingAdmissionTests | unit-01 | 3 | 3 | 3/0/0 | 1.478 | 0.658 | TRX complete | P-3 |
| Antiphon.Tests.Application.SpecialistAttemptEvidenceTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.SpecialistFailurePolicyTests | unit-06 | 30 | 30 | 30/0/0 | 0.260 | 0.038 | TRX complete | P-3 |
| Antiphon.Tests.Application.SpecialistQualificationContractTests | unit-01 | 1 | 1 | 1/0/0 | 0.412 | 0.412 | TRX complete | P-3 |
| Antiphon.Tests.Application.SpecialistRoleContractTests | unit-01 | 2 | 2 | 2/0/0 | 10.050 | 9.971 | TRX complete | P-3 |
| Antiphon.Tests.Application.StandingBootDocumentationTests | unit-01 | 3 | 3 | 3/0/0 | 0.362 | 0.305 | TRX complete | P-3 |
| Antiphon.Tests.Application.StandingBootWatchPolicyTests | unit-06 | 33 | 33 | 33/0/0 | 0.725 | 0.056 | TRX complete | P-3 |
| Antiphon.Tests.Application.StandingPipelinePolicyDocumentationTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.StandingSpecialistHealthPolicyTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.059 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.SubagentNotificationTests | unit-01 | 3 | 3 | 3/0/0 | 0.002 | 0.004 | TRX complete | P-3 |
| Antiphon.Tests.Application.SubscriptionQuotaGateTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.SubscriptionUsageParserTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.131 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.TaskCompletionContinuationTests | unit-05 | 48 | 48 | 48/0/0 | 2.009 | 0.169 | TRX complete | P-3 |
| Antiphon.Tests.Application.TaskCompletionProgressPolicyTests | unit-06 | 34 | 34 | 34/0/0 | 5.210 | 0.268 | TRX complete | P-3 |
| Antiphon.Tests.Application.TaskPlatformGuidanceTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.048 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.TaskReportHousekeepingTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.TierReasoningEffortAgreementTests | unit-01 | 1 | 1 | 1/0/0 | 0.003 | 0.003 | TRX complete | P-3 |
| Antiphon.Tests.Application.TrackerSyncSummaryFormatterTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.TranscriptIncidentReplayGuardTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.023 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.TranscriptLocalCommandEchoTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.009 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.TypedBodySpillTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.UnmarkedWaitingContractTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.020 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.UsageLimitWallParserTests | unit-05 | 39 | 39 | 39/0/0 | 0.638 | 0.036 | TRX complete | P-3 |
| Antiphon.Tests.Application.VerificationReceiptPolicyTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.319 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.VerificationRoundBriefTests | unit-01 | 1 | 1 | 1/0/0 | 2.480 | 2.480 | TRX complete | P-3 |
| Antiphon.Tests.Application.VerificationRoundInstructionTests | known-red | 6 | 6 | 4/2/0 | 0.014 | 0.005 | TRX complete | P-3 |
| Antiphon.Tests.Application.WorkerWorkspaceDefaultMigrationShapeTests | unit-01 | 2 | 2 | 2/0/0 | 0.060 | 0.042 | TRX complete | P-3 |
| Antiphon.Tests.Application.WorkerWorkspaceDefaultStorageTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 14.013 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Application.WorkflowEngineParsingTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.WorkspaceReservationKeyTests | unit-01 | 3 | 3 | 3/0/0 | 0.247 | 0.087 | TRX complete | P-3 |
| Antiphon.Tests.Application.WorkspaceReservationLivenessTests | unit-08 | 20 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Application.WorktreeCleanupPresentationTests | unit-06 | 33 | 33 | 33/0/0 | 52.583 | 1.764 | TRX complete | P-3 |
| Antiphon.Tests.Application.WorktreeCleanupSettingsValidatorTests | unit-01 | 3 | 3 | 3/0/0 | 0.184 | 0.094 | TRX complete | P-3 |
| Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests | unit-04 | 50 | 50 | 50/0/0 | 28.992 | 2.214 | TRX complete | P-3 |
| Antiphon.Tests.Application.WorktreeListParsingTests | unit-01 | 3 | 3 | 3/0/0 | 0.107 | 0.118 | TRX complete | P-3 |
| Antiphon.Tests.CachingChatClientTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.BaselineComparerTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.BuildSlotClientTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.C1307CliCancellationTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.420 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointAppTests | unit-01 | 3 | 3 | 3/0/0 | 8.718 | 4.292 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests | known-red | 11 | 11 | 8/3/0 | 211.724 | 211.746 | TRX complete | P-2 |
| Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests | unit-01 | 3 | 3 | 3/0/0 | 11.719 | 5.063 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests | unit-08 | 20 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointEofStallTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests | unit-01 | 3 | 3 | 3/0/0 | 17.288 | 8.629 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointFixtureProcessTests | unit-01 | 2 | 2 | 2/0/0 | 6.823 | 6.913 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointImportTests | unit-07 | 26 | 26 | 26/0/0 | unknown | 0.948 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointLargeStampScriptTests | unit-01 | 3 | 3 | 3/0/0 | 45.659 | 45.720 | TRX complete | P-1 |
| Antiphon.Tests.Checkpoints.CheckpointLargeStampTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 89.348 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointLineTests | unit-01 | 3 | 3 | 3/0/0 | 0.057 | 0.038 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointManifestTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.497 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointNamespaceCensusUsageTests | unit-01 | 1 | 1 | 1/0/0 | 6.167 | 6.167 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests | unit-02 | 6 | 6 | 5/0/1 | unknown | 139.960 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 4.323 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointReceiptSafetyTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointRepeatConfigurationTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.640 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointRepeatDocumentationTests | unit-01 | 1 | 1 | 1/0/0 | 0.103 | 0.103 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointRepeatHostTests | unit-01 | 3 | 3 | 3/0/0 | 10.490 | 10.495 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointRunOwnershipTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointScriptReaderDeadlineTests | unit-01 | 4 | 4 | 4/0/0 | 16.115 | 16.122 | TRX complete | P-2 |
| Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests | unit-08 | 17 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointSlotContractTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointStampSafetyTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests | unit-07 | 24 | 24 | 24/0/0 | unknown | 10.159 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointTempRootSweepTests | unit-06 | 27 | 27 | 27/0/0 | 24.352 | 5.180 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointTempScopeTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointTimingHarnessTests | unit-08 | 21 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointToolCopyCleanupTests | unit-08 | 16 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.CheckpointToolSourceTests | unit-01 | 2 | 2 | 2/0/0 | 5.123 | 5.030 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.DetachedLauncherTests | unit-01 | 1 | 1 | 1/0/0 | 1.270 | 1.270 | TRX complete | P-1 |
| Antiphon.Tests.Checkpoints.EvidenceFolderTests | unit-02 | 6 | 6 | 5/0/1 | unknown | 1.144 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.ExitCodeTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.061 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.OutputCleanupTests | unit-04 | 49 | 49 | 45/0/4 | 117.824 | 15.662 | TRX complete | P-2 |
| Antiphon.Tests.Checkpoints.PlanCoverageAssertionTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 2.939 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.PlanCoverageBindingTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 203.438 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.PlanCoverageCensusTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.PlanCoverageCommandTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests | unit-01 | 4 | 4 | 4/0/0 | 59.412 | 24.193 | TRX complete | P-2 |
| Antiphon.Tests.Checkpoints.PlanCoverageHandleTests | unit-08 | 16 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.PlanCoverageParserTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.PlanCoveragePcTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.ProcessDriverTests | unit-01 | 1 | 1 | 1/0/0 | 0.079 | 0.079 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.ReapedProcStatIdentityTests | unit-01 | 2 | 2 | 2/0/0 | 0.126 | 0.094 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.ReportMergerTests | unit-01 | 1 | 1 | 1/0/0 | 1.298 | 1.298 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.ReportWriterTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.391 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.RerunFilterHostTests | unit-01 | 1 | 1 | 1/0/0 | 2.668 | 2.668 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.RerunPolicyTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 3.686 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.RowRunnerTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.RunSchedulerTests | unit-08 | 18 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.RunStateStoreTests | unit-01 | 1 | 1 | 1/0/0 | 10.429 | 10.429 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.SafeDeleteProcessBoundaryTests | unit-01 | 2 | 2 | 2/0/0 | 2.566 | 2.567 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.ShadowCopyTests | unit-01 | 1 | 1 | 1/0/0 | 1.391 | 1.391 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.TestPhaseCustodyTests | unit-01 | 1 | 1 | 1/0/0 | 2.959 | 2.959 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.TestSetTimingTests | unit-01 | 4 | 4 | 4/0/0 | 0.316 | 0.318 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.TimeoutTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Checkpoints.TimingJournalTests | unit-01 | 4 | 4 | 4/0/0 | 0.123 | 0.135 | TRX complete | P-3 |
| Antiphon.Tests.Checkpoints.TrxReportTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 0.112 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Checkpoints.WaitCommandTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Domain.StateMachine.CardStateMachineTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Domain.StateMachine.RunAttemptStateMachineTests | unit-01 | 2 | 2 | 2/0/0 | 0.366 | 0.301 | TRX complete | P-3 |
| Antiphon.Tests.Domain.StateMachine.WorkflowStateMachineTests | unit-05 | 47 | 47 | 47/0/0 | 0.100 | 0.027 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.AppDbContextModelTests | unit-01 | 3 | 3 | 3/0/0 | 34.174 | 12.411 | TRX complete | P-2 |
| Antiphon.Tests.Infrastructure.AppHostBrokerSourceGuardTests | unit-01 | 2 | 2 | 2/0/0 | 1.385 | 1.372 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.C1307CancellationTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 4.572 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.C1307ToolRemovalTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 3.067 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests | unit-01 | 4 | 4 | 4/0/0 | 0.592 | 0.189 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.CodexRootExecContractTests | unit-01 | 4 | 4 | 4/0/0 | 28.247 | 91.541 | TRX complete | P-2 |
| Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.CustodyHelperContractTests | unit-10 | 9 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.DindRunnerContractTests | unit-07 | 25 | 25 | 25/0/0 | unknown | 4.321 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.DirectoryBuildRspTests | unit-01 | 2 | 2 | 2/0/0 | 0.241 | 0.141 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.DockerDeliveryBarrierTests | unit-07 | 24 | 24 | 24/0/0 | unknown | 2.444 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.DockerDeliveryObservationTests | unit-06 | 31 | 31 | 31/0/0 | 0.485 | 0.071 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.DockerStackContractTests | unit-03 | 115 | 115 | 115/0/0 | 1.868 | 0.176 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.DockerStackDocumentationTests | unit-09 | 15 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.DotnetToolManifestContractTests | unit-01 | 2 | 2 | 2/0/0 | 0.131 | 0.116 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.E2ECopyOwnedPtyHostAppHostContractTests | unit-01 | 1 | 1 | 1/0/0 | 0.551 | 0.551 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.EvidencePolicyWorkflowTests | unit-01 | 4 | 4 | 4/0/0 | 2.545 | 2.550 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.FileSystemEvidenceRetentionTests | unit-01 | 4 | 4 | 4/0/0 | 4.923 | 2.196 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.GithubCredentialHelperTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.104 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.GitMinimumVersionGuardTests | unit-01 | 1 | 1 | 1/0/0 | 0.592 | 0.592 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.GrokRunnerImageContractTests | unit-07 | 26 | 26 | 26/0/0 | unknown | 0.033 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.GrokSha256PinTests | unit-10 | 11 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests | unit-08 | 16 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointCompatibilityTests | unit-03 | 54 | 54 | 54/0/0 | 15.390 | 1.332 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointCustodyTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointExtractionTests | unit-01 | 3 | 3 | 3/0/0 | 5.980 | 2.109 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointPathTests | unit-01 | 2 | 2 | 2/0/0 | 0.805 | 0.410 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointReadDeadlineTests | unit-01 | 2 | 2 | 2/0/0 | 2.716 | 1.432 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointReceiptTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointSweepRaceTests | unit-01 | 4 | 4 | 4/0/0 | 6.036 | 1.541 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.HostCleanupExecutionTests | unit-08 | 20 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.HostCleanupOwnershipTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.HostCleanupPolicyTests | unit-07 | 22 | 22 | 22/0/0 | unknown | 0.110 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.JqRunnerImageContractTests | unit-08 | 18 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.LandingGitProfileTests | unit-07 | 26 | 26 | 26/0/0 | unknown | 0.076 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.LandingIdentityControlTests | unit-07 | 26 | 26 | 26/0/0 | unknown | 0.116 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests | unit-04 | 72 | 72 | 70/0/2 | 11.343 | 1.718 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.LogRetentionTests | unit-08 | 21 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.OperatorDashboardSessionsTests | unit-01 | 4 | 4 | 4/0/0 | 0.335 | 0.113 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.OperatorTokenFileTests | unit-01 | 4 | 4 | 4/0/0 | 4.963 | 4.399 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.OrchestratorInstructionsMigrationShapeTests | unit-01 | 1 | 1 | 1/0/0 | 10.925 | 10.925 | TRX complete | P-2 |
| Antiphon.Tests.Infrastructure.PackageVersionPinContractTests | unit-01 | 2 | 2 | 2/0/0 | 1.303 | 0.663 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.PostgresPoolConfigurationTests | unit-01 | 1 | 1 | 1/0/0 | 0.138 | 0.138 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.Resilience.HttpReadResilienceTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.Resilience.HttpResilienceRegistrationTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 3.545 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.Resilience.ResilienceBudgetTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 23.365 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.Resilience.ResilienceCircuitTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 8.329 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Infrastructure.Resilience.ResilienceOptionsAndTelemetryTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.Resilience.ResiliencePredicateTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.RunnerPushCredentialDocsTests | unit-01 | 1 | 1 | 1/0/0 | 0.252 | 0.252 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.SessionRunnerGrokLineEndingContractTests | unit-01 | 1 | 1 | 1/0/0 | 0.264 | 0.264 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.ShellFixtureLineEndingContractTests | unit-01 | 2 | 2 | 2/0/0 | 0.342 | 0.343 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.WorktreeDeleteAccessProbeTests | unit-05 | 38 | 38 | 38/0/0 | 0.412 | 0.041 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.WorktreeLockDiagnosticsTests | unit-05 | 37 | 37 | 37/0/0 | 1.138 | 0.102 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.WorktreeManagerSafetyTests | unit-01 | 3 | 3 | 3/0/0 | 0.543 | 0.250 | TRX complete | P-3 |
| Antiphon.Tests.Infrastructure.WorktreeManagerTests | unit-10 | 10 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Infrastructure.WorktreeRemovalDefaultTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.075 | partial native; no TRX | pending method sample |
| Antiphon.Tests.LandDeliveryWaitRoutingTests | unit-01 | 4 | 4 | 4/0/0 | 2.400 | 1.134 | TRX complete | P-3 |
| Antiphon.Tests.Migrations.CommitOnSettleMigrationTests | unit-01 | 3 | 3 | 3/0/0 | 10.678 | 10.247 | TRX complete | P-3 |
| Antiphon.Tests.Migrations.PipelineDefinitionMigrationShapeTests | unit-01 | 1 | 1 | 1/0/0 | 0.514 | 0.514 | TRX complete | P-3 |
| Antiphon.Tests.ProcessSpawnLimitTests | unit-08 | 21 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.ProgressAwareWaitTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 1.677 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Scripts.C1008PlatformContractTests | unit-02 | 6 | 6 | 4/0/2 | unknown | 228.351 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Scripts.C1364BoundLandWaitProofTests | unit-02 | 6 | 6 | 4/2/0 | unknown | unknown | partial native; no TRX | pending method sample |
| Antiphon.Tests.Scripts.DelegateScriptInterruptSwitchTests | unit-01 | 3 | 3 | 3/0/0 | 0.129 | 0.120 | TRX complete | P-3 |
| Antiphon.Tests.Scripts.DelegateScriptRunnerSwitchTests | unit-01 | 3 | 3 | 3/0/0 | 0.350 | 0.117 | TRX complete | P-3 |
| Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests | unit-05 | 39 | 39 | 39/0/0 | 454.105 | 454.139 | TRX complete | P-1 |
| Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests | unit-01 | 2 | 2 | 2/0/0 | 37.227 | 22.004 | TRX complete | P-2 |
| Antiphon.Tests.Scripts.RefreshGithubTokenRemoteCommandTests | unit-01 | 3 | 3 | 3/0/0 | 6.568 | 6.570 | TRX complete | P-2 |
| Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests | unit-01 | 3 | 3 | 3/0/0 | 20.606 | 20.498 | TRX complete | P-2 |
| Antiphon.Tests.Scripts.RemoteScriptContractTests | unit-11 | 499 | 35 | 29/6/0 | unknown | 114.452 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Scripts.RetiredTempContainerHostTests | unit-08 | 21 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Scripts.RetiredTempContainerScriptTests | unit-09 | 13 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Scripts.RollingProductionMountTests | unit-09 | 12 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.Scripts.RollingVolumeRecycleCensusChurnTests | unit-01 | 1 | 1 | 1/0/0 | 0.165 | 0.165 | TRX complete | P-3 |
| Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests | unit-07 | 27 | 19 | 18/1/0 | unknown | 603.019 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Scripts.ScriptHarnessProcessContractTests | unit-07 | 26 | 26 | 26/0/0 | unknown | 0.247 | partial native; no TRX | pending method sample |
| Antiphon.Tests.Scripts.SeedSmokeTargetingPackContractTests | unit-01 | 4 | 4 | 4/0/0 | 0.885 | 0.222 | TRX complete | P-3 |
| Antiphon.Tests.Scripts.TestTimingReportTests | unit-01 | 3 | 3 | 3/0/0 | 7.245 | 7.251 | TRX complete | P-3 |
| Antiphon.Tests.Scripts.VerifyDevStackSmokeTests | unit-01 | 2 | 2 | 2/0/0 | 7.961 | 7.961 | TRX complete | P-3 |
| Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests | unit-02 | 5 | 4 | 3/1/0 | unknown | 48.217 | partial native; no TRX | P-2 |
| Antiphon.Tests.TestHelpers.BuildOutputNestingTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.TestHelpers.CheckCompactionFixtureTests | unit-01 | 1 | 1 | 1/0/0 | 0.150 | 0.150 | TRX complete | P-3 |
| Antiphon.Tests.TestHelpers.ControlledLandingGitTests | unit-04 | 51 | 51 | 51/0/0 | 6.423 | 0.863 | TRX complete | P-3 |
| Antiphon.Tests.TestHelpers.DelegationHarnessCensusTests | unit-02 | 7 | 7 | 7/0/0 | unknown | 9.663 | partial native; no TRX | pending method sample |
| Antiphon.Tests.TestHelpers.DelegationTestServicesTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 2.021 | partial native; no TRX | pending method sample |
| Antiphon.Tests.TestHelpers.DirectoryLinkFixtureTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.943 | partial native; no TRX | pending method sample |
| Antiphon.Tests.TestHelpers.EntityScalarSnapshotConsumerGuardTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 10.084 | partial native; no TRX | pending method sample |
| Antiphon.Tests.TestHelpers.EntityScalarSnapshotTests | unit-06 | 35 | 35 | 35/0/0 | 148.973 | 5.152 | TRX complete | P-3 |
| Antiphon.Tests.TestHelpers.ScaledTimeProviderTests | unit-02 | 6 | 6 | 6/0/0 | unknown | 0.317 | partial native; no TRX | pending method sample |
| Antiphon.Tests.TestHelpers.SlowTestTripwireTests | unit-01 | 2 | 2 | 2/0/0 | 0.280 | 0.185 | TRX complete | P-3 |
| Antiphon.Tests.TestHelpers.TestAppHostPathTests | unit-02 | 5 | 5 | 5/0/0 | unknown | 0.209 | partial native; no TRX | pending method sample |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | unit-05 | 39 | 39 | 39/0/0 | 98.825 | 98.863 | TRX complete | P-1 |
| Antiphon.Tests.TestHelpers.TestDbFixtureLifecycleTests | unit-06 | 33 | 33 | 33/0/0 | 2.203 | 2.220 | TRX complete | P-3 |
| Antiphon.Tests.TestHelpers.TestDbFixtureSchemaCacheLifecycleTests | unit-02 | 6 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests | unit-01 | 1 | 1 | 1/0/0 | 4.030 | 4.030 | TRX complete | P-3 |
| Antiphon.Tests.TestHelpers.TestPhaseSummaryTests | unit-02 | 7 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.TestHelpers.TestPhaseSupervisorTests | unit-01 | 2 | 2 | 2/0/0 | 11.622 | 11.623 | TRX complete | P-2 |
| Antiphon.Tests.TestHelpers.TestSchemaCacheTests | unit-10 | 8 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.Tests.TestHelpers.TestTimingLifecycleTests | unit-01 | 1 | 1 | 1/0/0 | 4.529 | 4.529 | TRX complete | P-3 |
| Antiphon.Tests.TestHelpers.TestTimingRecorderTests | unit-09 | 14 | 0 | 0/0/0 | unknown | unknown | unrun | pending method sample |
| Antiphon.TestSupport.TestClassificationGuardTests | unit-01 | 1 | 1 | 1/0/0 | 14.012 | 14.012 | TRX complete | P-2 |

## Original red identities (TRX)

- Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S3_OsConditionedElementNeedsPin($([MSBuild]::IsOSPlatform('Windows'))) (1.442s): ShouldAssertException: run.ExitCode
- Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S3_OsConditionedElementNeedsPin('$([System·Runtime·InteropServices·RuntimeInformation]::OSDescription)' == 'never') (0.960s): ShouldAssertException: run.ExitCode
- Antiphon.Tests.Agents.RunnerClaudeAdapterVerifiedPromptTests.Prompt_is_delivered_as_encoded_body_then_separate_enter_when_composer_echoes (0.152s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Application.InstructionBundleTests.C470_composed_roles_separate_vr_from_pc (0.079s): ShouldAssertException: code
- Antiphon.Tests.Application.InstructionBundleTests.C1015_Review_remains_read_only_and_checks_history (0.116s): ShouldAssertException: text
- Antiphon.Tests.Application.InstructionBundleTests.stage_bundle_invariants_are_pinned_by_substring (0.065s): ShouldAssertException: code
- Antiphon.Tests.Application.InstructionBundleTests.C1065_BlockedSeatInstructionsStayTrueWhileParkingIsOff (0.047s): ShouldAssertException: text
- Antiphon.Tests.Application.InstructionBundleTests.C1213_ReviewOverrideFollowsFragment (0.045s): ShouldAssertException: readOnly
- Antiphon.Tests.Application.InstructionBundleTests.C1213_StageExceptionsRemain (0.076s): ShouldAssertException: text
- Antiphon.Tests.Agents.RunnerClaudeAdapterVerifiedPromptTests.Dead_composer_fails_the_prompt_send_with_enter_withheld (0.194s): ShouldAssertException: Task `adapter.SendPromptAsync("this prompt never renders", CancellationToken.None)`
- Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests.A_launch_into_an_untrusted_directory_answers_the_dialog_before_reporting_ready (0.213s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests.A_trust_dialog_that_will_not_clear_fails_the_launch_with_a_named_block (0.213s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests.A_painted_but_deaf_tui_is_not_ready (0.235s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.An_effort_dialog_that_never_clears_fails_within_its_budget (0.235s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Application.PostLandMutationContractTests.C478_V11_ComposedContractsKeepOrdinaryReviewBeforePublication (0.011s): ShouldAssertException: review
- Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests.A_healthy_launch_types_nothing_but_the_input_probe (0.237s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests.A_composer_that_will_not_clear_is_not_ready (0.237s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Application.ScopedVerificationInstructionTests.C487_G142 (0.015s): ShouldAssertException: Compose(AgentTaskRole.Review)
- Antiphon.Tests.Application.PostLandMutationContractTests.C478_V11_ActiveContractAndVocabulary (0.022s): ShouldAssertException: review
- Antiphon.Tests.Application.VerificationRoundInstructionTests.C544_InitialFullContract (0.003s): ShouldAssertException: code
- Antiphon.Tests.Application.VerificationRoundInstructionTests.C544_FinalReviewContract (0.004s): ShouldAssertException: review
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Probe_failure_logs_token_elapsed_writes_and_screen (0.319s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Application.DispatchBaseWarningRosterTests.C1155_RosterNamesEveryDispatchWarningE2EMethod (0.309s): ShouldAssertException: SortedNames(byMethod.Keys)
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Disabling_the_probe_does_not_disable_effort_resolution(True) (1.724s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Disabling_the_probe_does_not_disable_effort_resolution(False) (1.724s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Each_captured_dialog_clears_before_the_composer_probe(1) (1.830s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Each_captured_dialog_clears_before_the_composer_probe(2) (1.830s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Each_captured_dialog_clears_before_the_composer_probe(0) (1.831s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.Attach_without_launch_effort_preserves_current (1.834s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests.A_dialog_appearing_during_the_minimum_floor_is_resolved (2.589s): ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
- Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes(script-eval, held) (3.885s): ShouldAssertException: process.ExitCode
- Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes(script-eval, ordinary) (4.480s): ShouldAssertException: process.ExitCode
- Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.changed_readers_bound_fifo_and_exited_child_pipes(script-git, held) (20.246s): ShouldAssertException: process.ExitCode

## Reproduction and evidence

Exact group membership and selectors: /work/worktrees/task-6422905c/.antiphon/1572/base-groups.json; repair selectors: /work/worktrees/task-6422905c/.antiphon/1572/green-groups.json and final-green-groups.json. Run records contain literal argv, platform/load, source SHA and assembly hash. TRX-derived rows: classes.csv, classes.json and test-results.json; partial native terminal identities: unit-02/partial-nodes.json and unit-07/partial-nodes.json. Full diagnostics preserve incomplete identities and cancellation. Representative rerun after a fresh isolated build:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1572-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1572/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1572-reader -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1572/ --property:UseAppHost=false -- --treenode-filter '/*/*/CheckpointBoundedReaderTests/*' --report-trx --report-trx-filename reader.trx --results-directory <fresh-directory>
```

All deliberate known-red witnesses precede the fixes; no mutation controls were commissioned. No production assertion was loosened or timeout increased. No SourceLanding or external executor was used.

## Rc picks and publication checks

Cleanly applied and compiled together against 9e97df3f7b3d594c6872a1327502d6877feaf820:

- 58435535cabb574789e93a3dc9ce8337e3820a22
- 071a5c2ba8d64117e4ff38d396165de479d867f2
- 0adc5d8d3e70e9b6af274f4aff99d9c69cbdf306
- 47aa3a0d0533d46d08d6437f8ee71e28bb89459e

Do not pick the adapter fixture, stage-literal, or OS-function expectation commits onto this rc without their corresponding master production changes: its guarded writer/dialog actions, compressed bundle wording, and EvaluationExpressionPolicy are absent. Same test files do not establish applicability. Rc first/second compile and restoration records: /work/worktrees/task-6422905c/.antiphon/1572/rc-check.json.

Evidence diff over the full task range, merge-tree result, final remote tip and output cleanup are retained beside this report and summarized by the final caller report. The source repair selection is bounded and the lane census remains incomplete; next: decide for caller triage/continued scoped census, with direct landing of verified fixes authorized by the operator brief.

Final hygiene: full seven-commit evidence range passed (0 violations). Conflict-free merge-tree against fetched origin/master: e955e792f5d6e1568463d01e0ba73fa7316a9def. All 30 main bin-c1572 outputs and both sets of 30 rc outputs were removed; both detached scratch incarnations restored and removed. The recorded final census process snapshot has no executing survivors. Generated evidence remains ignored.
