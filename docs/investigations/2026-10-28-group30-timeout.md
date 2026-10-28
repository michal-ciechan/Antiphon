# Group 30 of run 79b742de: a budget overrun, not a hang

Task 62619f4b, 2026-10-28, Investigate (read-only, native Windows desktop). I ran no test, build or dotnet
command. I parsed stored journals, timing JSON and diag files with PowerShell only, and ran `git fetch`/`git show`
for two branches. I did not touch the live run (supervisor PID 145140), `C:\Antiphon\releases` or `native-run.lock`.
Evidence was read up to 05:24Z, while group 34 was running.

## Verdict

**Confirmed: group 30 did not hang.** It ran tests without a break until the 3,600 s watchdog killed it. From
about 600 s to the kill, exactly one test was in flight at every 120 s sample, and no interval longer than 1 s
had nothing in flight. The test in flight at the kill had run for 40 s, which is a normal duration for it. The
planner priced the chunk at **874.5 s** (`pricingBasis: model`). None of its 30 classes has a row in
`scripts/lib/nightly-class-costs.json`, so they get the model price of 0.5 to 8 s per node. Ten of the classes
run one test at a time (wall span equals the summed test-seconds), and those ten alone need about 4,100 s.

Group 30 is the same chunk as in run 8f055f87, with the same 30 classes and 954 tests in the same order. There it
finished in **3,264 s**, which was 91% of the watchdog: a near miss. This run is slower on the same tests (ratios
below), so the chunk crossed 3,600 s. It is a pure budget overrun.

## Sources

- Timing root `C:\Antiphon\releases\candidates\rc-2026-10-27-f84228f285dd\logs\timing-79b742de44354190bb55fd35914496f3\`:
  `groups-planned.json` (181 groups), and the journal `cases-163860-67d56aacf14f4f7aad68d7b9471c5772.jsonl`
  (header `groupId all-group-30`, epoch 03:20:39.08Z).
- `...\2026-10-27-2215-79b742de\antiphon-all-group-30.timing.json`: `why timeout`, `trxMissing true`,
  started 03:20:34.37Z, `lastPhaseUtc` 04:20:39.63Z, `runWallSeconds 3605.038`. The test log holds only the
  3 header lines.
- `antiphon-all-group-30-execution\log_261028032034834.diag` (3,341,296 B, 0 NUL bytes, 8,777 lines).
- Baseline: the 8f055f87 journal `rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87\timing\cases-2196-5a02c44fd5d143f48f12491d6542223c.jsonl`
  (`all-group-30`, 954/954 complete, last end 3,263.9 s).
- Planner model: `scripts/lib/nightly-tests-impl.ps1:132-186`. An ordinary node costs 0.5 s and a Slow node
  1.5 s. A Slow node in a Land or Delivery type costs 4 s. Land, Delivery or Check in the type name doubles the
  cost. The watchdog fallback is 3,600,000 ms (`:1212`).

## Q1: Group 30

Journal and diag agree. The journal has 877 starts and 876 completes: **876 Passed, 0 Failed, 0 Skipped**. The
last diag line, at `2026-10-28T04:19:59.9389629`, is the TUnit `TestNodeUpdateMessage` (InProgress) for
`ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).5.1.0`
(display `C519_Converter_handoff(False, enqueue...`). That matches the last journal `start` at 3,560.9 s. The
kill came 39.7 s later. Earlier cases of the same method took 29.7, 36.9 and 32.6 s in this run, and 23-27 s in
8f055f87.

In flight at the kill: that one test. Never started: 6 more `ChannelOutboundUnifiedTransportTests` cases and all
71 `SessionReconciliationServiceTests` tests (3.7 s in 8f055f87). That makes 78 of 954 tests not completed.

Per class, in run order (s = summed test-seconds from the journal; span = first start to last end):

| class | tests | 79b742de s | span | 8f055f87 s | model price max |
|---|---|---|---|---|---|
| 14 opening classes run in parallel: AgentServiceIntegration, AgentSessionServiceIntegration, AgentTaskLand{FailureDiagnostic,Monitoring,Request,SourcePersistence}, AgentTaskPipelineStatus, AgentTaskWorktreeBaseCreate, BlockedReplyReportRetention, BootStallDetection, CheckCompactionReceipt, InternalDecisionPolicy, RemoteControlMaintenanceQueue, TaskInputWeakArmBoundary | 484 | 2,763 | 20-259 s | 2,031 | |
| OrchestratorInstructionsRefresh (serial) | 50 | 298.5 | 298 | 230.4 | 75 |
| CheckCompactionCrash (serial) | 9 | 101.5 | 101 | 78.2 | 27 |
| ReviewLoop (serial) | 18 | 195.3 | 195 | 192.2 | 27 |
| HerdrLabelFollowFlow, PostLandMutationContract, PostLandMutationReceiptPolicy | 74 | 44.5 | | 35.2 | |
| PostLandMutationAdmission (serial) | 27 | 561.4 | 562 | 466.0 | 216 |
| PostLandMutationWorktree (serial) | 33 | 672.6 | 673 | 638.4 | 264 |
| RepairSourceRecoveryLanding | 4 | 99.9 | 66 | 89.0 | 32 |
| ReviewEvidenceRecovery (serial) | 21 | 239.3 | 239 | 238.2 | 31.5 |
| RoutingExhaustedPlacement, SessionRuntimeActionRecovery, WorktreeRetirementRace | 125 | 215.9 | 52 | 208.4 | |
| ChannelOutboundUnifiedCrash (serial) | 21 | 837.3 | 837 | 619.4 | 31.5 |
| ChannelOutboundUnifiedTransport (serial) | 17 | 792.9 for 10 done | 793 | 935.1 for 17 | 25.5 |
| SessionReconciliationService | 71 | not started | | 3.7 | 106.5 |

"Model price max" is nodes × the largest model unit for the type (1.5, 3 for Check, 8 for Land/Delivery). The
journal does not carry Slow categories, so the real model price is at or below this figure.

Concurrency samples (in flight at t): 14 at 120 s, 2 at 240-480 s, then **1** at every sample from 600 s to
3,600 s. The class sequence after 600 s is strictly serial: Admission, Worktree, ReviewEvidence, RoutingExhausted,
Crash, Transport.

**Slowdown versus 8f055f87:** Transport started at 2,768 s here against 2,325 s there (+443 s). The same 10
Transport cases took 792.9 s here against 580.6 s there (1.37×). Crash took 1.35× as long, Admission 1.20×,
OrchestratorInstructionsRefresh 1.30×, and the parallel opening block 1.36×. ReviewLoop, ReviewEvidence and
PostLandMutationWorktree were within 5%. The 8f055f87 Transport tail after case 4 took 354.5 s. Scaled by 1.37
it would take about 485 s, so the estimated wall need here is about **4,050 s**, or about 3,270 s at the
8f055f87 pace. Either way the chunk is priced at 874.5 s.

The journal does not show why this run is slower on the same desktop. The diag has no error or retry burst near
the slow tests. Uncertainty: host load from other sessions is the obvious candidate, but I have no stored
evidence for it.

## Q2: Rows for `scripts/lib/nightly-class-costs.json` (group 30)

These use the 9a16e2cb schema and convention: `nodes` is the completed-test count in the journal, `seconds` is
the rounded summed serial test-seconds, and `basis` is `measured`. Source journal for every row:
`timing/cases-163860-67d56aacf14f4f7aad68d7b9471c5772.jsonl` at rc-2026-10-27-f84228f285dd, run 79b742de. This
run is the slower of the two, so its figures are the conservative choice. The rows cover every class whose
measured total is above the model price maximum and above 40 s.

| class (`Antiphon.Tests.Application.`) | nodes | seconds | 8f055f87 for comparison | runs serially |
|---|---|---|---|---|
| ChannelOutboundUnifiedCrashTests | 21 | 837 | 619 | yes |
| ChannelOutboundUnifiedTransportTests | 10 | 793 | 935 / 17 (complete) | yes |
| AgentTaskPipelineStatusTests | 69 | 730 | 468 | no (span 36 s) |
| PostLandMutationWorktreeTests | 33 | 673 | 638 | yes |
| PostLandMutationAdmissionTests | 27 | 561 | 466 | yes |
| AgentTaskLandSourcePersistenceTests | 35 | 395 | 264 | no |
| AgentTaskLandRequestTests | 30 | 361 | 259 | no |
| OrchestratorInstructionsRefreshTests | 50 | 299 | 230 | yes |
| AgentTaskLandMonitoringTests | 25 | 295 | 214 | no |
| BootStallDetectionTests | 59 | 247 | 177 | no |
| ReviewEvidenceRecoveryTests | 21 | 239 | 238 | yes |
| ReviewLoopTests | 18 | 195 | 192 | yes |
| BlockedReplyReportRetentionTests | 19 | 175 | 141 | yes |
| AgentTaskWorktreeBaseCreateTests | 23 | 138 | 120 | no |
| WorktreeRetirementRaceTests | 54 | 119 | 125 | no |
| CheckCompactionReceiptTests | 27 | 106 | 66 | no |
| CheckCompactionCrashTests | 9 | 102 | 78 | yes |
| RepairSourceRecoveryLandingTests | 4 | 100 | 89 | partly (span 66 s) |
| TaskInputWeakArmBoundaryTests | 17 | 48 | 36 | yes |
| SessionRuntimeActionRecoveryTests | 17 | 45 | 34 | no |
| HerdrLabelFollowFlowTests | 19 | 44 | 35 | yes |

- Borderline: `AgentTaskLandFailureDiagnosticTests` measured 192 s for 26 tests, against a model maximum of 208 s.
  Its real model price depends on Slow tags, which the journal does not record.
- The Transport row follows the 9a16e2cb convention: the denominator is completed tests, 79.3 s per node, so
  about 1,348 s for 17 nodes. If one row needs a complete class, use the 8f055f87 complete total (17 nodes,
  935 s, journal `cases-2196-5a02c44fd5d143f48f12491d6542223c.jsonl`). Under either figure Transport goes into a
  launch of its own.
- Rows marked "no" hold summed test-seconds for tests that ran in parallel. Packing by them overstates their
  wall time, though never understates it, which matches how the 9a16e2cb rows were built.

Group 30 then sums to about 7,400 serial test-seconds, so the 900 s packer would split it into about eight
launches.

## Q3: Group 28 in this run matches 8f055f87

Journal `cases-379188-a3a9e472636e4b829f3585dc8df5c752.jsonl` (`all-group-28`, epoch 02:10:49.01Z): 492 starts,
491 completes, **491 Passed, 0 Failed**. The diag's last line is at 03:08:52.63Z, the same second as the last
journal write. The kill came at 03:10:54.05Z (`runWallSeconds 3608.044`). The test in flight at the kill was
`BlockedTaskParkReclaimTests.C1065_ClaimAndReplyInvalidateLegacyCandidate`, started at 3,483.7 s and running
about 121 s. It took 245.3 s in 8f055f87, so this is not a hang. The class order is identical to 8f055f87.

| class | tests | 79b742de s | 8f055f87 s (the value of the 9a16e2cb row) |
|---|---|---|---|
| DelegateBranchDelivery | 7 | 370.6 | 333.7 (334) |
| BlockedTaskParkDelivery | 6 | 610.8 | 575.4 (575) |
| PostLandMutationCustody | 56 | 1,224.8 | 1,110.3 (1,110) |
| RepairSourceDispatch | 41 | 336.7 | 302.2 (302) |
| RepairSourceSettlement | 36 | 520.3 | 491.0 (491) |
| SessionRuntimeActionRouting | 35 | 114.6 | 111.8 |
| BlockedTaskParkReclaim | 15 of 38 done | 624.7 | 906.8 (907) |
| HerdrLabelFollow | 33 | 162.2 | 97.3 |
| ChannelBridge, ChannelInboundRecovery, CodexStartupDelivery, LandEvidenceWarningDelivery, ApiErrorRecoveryService | 190 | not started | |

Same mechanism, same classes, same serial order. This run is 6-11% slower on the five complete 9a16e2cb classes. The rows are
therefore slightly low for this host's current pace, but each packed launch still sits far under 3,600 s.
Uncertainty: I did not re-pack with 9a16e2cb to confirm each split launch's margin.

## Q4: Every non-blank `why` so far, and the risk list

**All 33 finished groups have a non-blank `why`.** Groups 28 and 30 are `timeout` (`trxMissing true`, no
`completedAt`). Groups 1-27, 29 and 31-33 are `timing-read-refused`. Group 34 was running at 05:24Z. Not every
refusal comes from the 256 KiB TRX cap: group 12's TRX is 8,535 B and group 4's is 40,449 B, both far under
262,144. In `test-phase-evidence.ps1` at f84228f2, a refusal also comes from the shared 200 ms deadline
(`:1612-1627`) or from `Get-QualificationPhaseSums` (`:1303`). That function walks every `cases-*.jsonl` in the
shared timing root (`:1340`), and the root held tens of multi-MB journals. I did not check whether CARD-1597 also
covers this path.

Finished groups, measured against price (pred = summed test-seconds from both runs' journals, larger per node):

| chunk | price (basis) | wall s | pred serial s | ratio |
|---|---|---|---|---|
| 28 | 900 (model) | 3,608 kill | 5,095 | 5.7 |
| 30 | 874.5 (model) | 3,605 kill | 7,412 | 8.5 |
| 31 | 674.5 (model) | 1,485 | 4,020 | 6.0 |
| 32 | 535.5 (model) | 946 | 2,718 | 5.1 |
| 33 | 451.5 (model) | 1,048 | 4,197 | 9.3 |
| 26 / 29 / 23 | 900 / 900 / 900 | 1,562 / 577 / 479 | 2,055 / 2,072 / 1,930 | 2.1-2.3 |

Not yet run (147 groups). Risk ranking uses each class's largest measured pace from either run. "Serial lane" sums
only classes whose span equals their test-seconds, which is a rough lower bound on wall time. Model groups 36-59
contain 8-49 tests each with no measurement.

| chunk | price (basis) | serial lane s | pred s | 8f055f87 wall of same-numbered group | risk |
|---|---|---|---|---|---|
| **65** | 900 (model) | 3,363 | 5,112 | 3,596 kill | **expected timeout**: the 9a16e2cb rows are not in this candidate, and at this run's 1.06-1.37× pace it needs well over 3,600 s. Top classes: WorktreeGuardedCleanup 1,593, CompletedCardWorktreeRemoval 942, LandingRemovalControl 761, SettledWorktreeRemoval 668 |
| split-19 | 1,214.5 (pace) | 2,037 | 2,037 | | medium: RemoteScriptContractTests, serial, 1.7× price, about 2,450 s at 1.2× |
| 36 | 302.5 (model) | 1,654 | 3,061 | 1,647 | medium-low: ratio 10.1. BlockedTaskParkResume 553, WorktreeResidueRecovery 487, CardDoneWorktreeCleanup 477, CodexCliObservationGap 439 |
| split-20 / split-21 | 892 / 883 (pace) | 1,497 / 1,481 | same | | low: RemoteScriptContractTests serial, 1.7× |
| 76 | 262 (mixed) | 1,220 | 1,400 | 1,360 | low: ReleaseTagAllocator 407, TestSelection 279 |
| 77 | 57 (model) | 1,139 | 1,270 | 1,107 | low wall, largest price ratio (22×): ChangeAwareRelease 569, TestSelectionDependency 202 |
| 71 / 72 | 1,933 / 1,420 (observed-bound) | 1,134 / 884 | | 1,134 / 884 | none |

Classes with measured need far above price (largest ratios) that also want rows: group 33 (9.3×), 36 (10.1×), 77
(22×), 31 (6.0×), 32 (5.1×). Their per-class figures are in the two journals named above and in the 79b742de
journals `cases-284244-...` (g31) and `cases-143596-...` (g32). I did not tabulate those rows here.

## Not done, noted

- Fix idea: add the Q2 rows, plus rows for the heavy classes of groups 31-33, 36 and 77, to
  `nightly-class-costs.json` before the next candidate is cut.
- I did not establish why this run is 6-37% slower than 8f055f87 on the same tests.
- `cases-114056-25cf76158acf4d27ac66bb998519e77d.jsonl` (0 B, created 05:01:20, when group 33 started) was locked
  by another process, so I could not read it. Group 33's predicted figures come from the 8f055f87 journals.
