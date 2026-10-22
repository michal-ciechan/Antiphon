# CARD-1404 follow-up: measured group-10 costs within the existing watchdog

Plan task `996ed989`, 2026-10-22. Base: `db1e72fd34acd52c4ab2436de28f0d1af3578b42`.
Ownership: CARD-1404 (Review); CARD-1410 (Done) supplies the existing packer. This is the bounded
group-10 part of CARD-0856 S3, not its lane/concurrency or fixture-performance work.

Split the former group 10 using three corrected cost rows. Keep the 3,600,000 ms process watchdog.
Two complete classes launch alone; provisionally price Publication at 60 seconds per node so the
existing method splitter gives it five launches. The production change is cost data, not a new
watchdog algorithm. This removes the demonstrated sizing defect; it does not promise a green
qualification while the independent assertion failures remain.

The [Debug report](../../investigations/2026-10-22-rc-group-10-timeout-debug.md) was copied unchanged
from task `79bfbadb` and is committed with this plan. Its historical handoff, paths and conclusions
remain attributed to that investigation. The original expanded evidence was read in full at
`C:\Antiphon\worktrees\card-task-79bfbadb\.antiphon\task-79bfbadb.md`.

## Ground truth

| Brief/card assumption | What the inspected code/evidence does | Plan consequence |
|---|---|---|
| Correct measured class costs in `tests/test-execution-policy.json` | That file owns `watchdogs.antiphon=3600000` and the profile/exclusions/hash. `Get-NightlyClassCostTable` in `scripts/lib/nightly-tests-impl.ps1:142` reads `scripts/lib/nightly-class-costs.json`; the three classes have no rows. | Edit the actual cost table. Leave the policy file and its hash unchanged; verify the watchdog through the policy reader. |
| A batching/watchdog derivation change may be needed | `Get-NightlyLaunchClassCost` uses measured seconds/nodes before category weights. First-fit decreasing isolates a class at or above 900 seconds; a class above 2400 seconds splits by method (`:184`, `:218`, `:342`). The execution call uses the policy watchdog (`:1069`). | Existing mechanics suffice. No production logic change. |
| The old group costs 900 seconds | 43 Slow Land nodes x 8 + 52 non-Slow Land nodes x 1 + 63 Slow Land nodes x 8 = 900. The first two classes alone took 2407.119 seconds in R and 3487.891 seconds in P. | Adding `Slow` to PersistenceFailure would still underprice it. Use measured class totals. |
| A persistent set of tests hangs | R has 141 passed, one completed Error, one InProgress and 15 unstarted nodes. Multiline terminal records invalidate the older single-line census. Only `C688_Repair_AlreadyCanonicalRecordsLocalIdentity(True)` was unfinished, admitted about 29 seconds before the kill. | No per-test timeout, retry, hang repair or process intervention. |
| Publication has a measured complete-class duration | R completed 47 of 63 nodes; P completed only two. Neither group has a TRX. | Its row is explicitly a provisional `pace` bound, never a complete measurement or coverage credit. |
| The existing calibration witness remains valid after the data correction | `Test-C1410_MeasuredCostGroups` asserts 5-8 estimated serial hours. Its unchanged listing has about 7.9 hours before these rows; the correction adds 6420 seconds (1.783 hours). | Correct this estimate assertion explicitly, with its own red/green pair. Preserve launch, census, suffix and watchdog assertions. |
| All five failures have new fixture cards | CARD-1510/1511/1512 cover groups 5/11/10 respectively. The two group-3 occurrences have no dedicated card from this investigation. | Keep the fixture repairs and two narrow Debug follow-ups separate from cost calibration. |

Code locations above are at the plan base; the Debug report's source line references are at the
candidate SHA. `Resolve-NightlySuiteChunks` still groups only native, profile-aware runs with
nonempty exclusions; nightly/no-exclusion behavior is outside this correction.

## Measurements and proposed rows

Read-only inputs (no live process, checkout, state or log writes):

- **R:** candidate `0714a9a89e4bff65aae7e0b4b94098694aeb931c`, run
  `d2777e21ef4c4a3e9b1fb315ff8f5602`;
  `C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b\logs\2026-10-22-1606-d2777e21\antiphon-all-group-10-execution\log_261022164232613.diag`.
  SHA-256 `e8671368e82505822306f825d957c0ade2a79d96918bb6c43e25cbcaa209b44b`.
- **P:** candidate `ceb6e3d6b784`, run `2026-10-22-0157-27a1b370`;
  `C:\Antiphon\releases\candidates\rc-2026-10-20-ceb6e3d6b784\logs\2026-10-22-0157-27a1b370\antiphon-all-group-10-execution\log_261022025311997.diag`.
  SHA-256 `a6103e3b4bca0aa2a0fa3975992580e5aea96129f4d339f060c74960b7a45864`.
- R's adjacent `antiphon-profile.discovery.json` supplies the 158-node roster and method counts.
  SHA-256 `b29a51298dac5003b84072e4b22cd70b30b6218eb90f34969f223970c754d565`.

The independent remeasurement reads complete timestamp-delimited diagnostic records, including
multiline stdout/exceptions, keys updates by UID and retains the final state. Sum `TimingProperty`
durations for final Passed/Error results only; do not charge InProgress as completed. R reproduces
141 Passed / 1 Error / 1 InProgress, P 97 Passed / 1 InProgress. No TUnit execution or harness test
was run to obtain these numbers.

All three names below have the prefix `Antiphon.Tests.Application.` in the JSON.

| Class | R completed / selected | R summed seconds | P completed / selected | P summed seconds | New `nodes` / `seconds` / `basis` |
|---|---:|---:|---:|---:|---|
| `AgentTaskLandCheckpointMatrixTests` | 43 / 43 | 933.337673 | 43 / 43 | 1549.837433 | 43 / 1560 / `measured` |
| `AgentTaskLandPersistenceFailureTests` | 52 / 52 | 1473.781126 | 52 / 52 | 1938.053795 | 52 / 1980 / `measured` |
| `AgentTaskLandPublicationTests` | 47 / 63 | 1144.271438 | 2 / 63 | 68.536326 | 63 / 3780 / `pace` |

For the complete classes take the slower complete total and round upward to the next minute.
Publication's largest completed node across R/P is 47.2861088 seconds (P maximum 34.2749673).
Multiply that observed maximum by 1.25 and round upward to the next ten seconds: 60 seconds/node;
63 x 60 = 3780 seconds. This deliberately bounds the observed sample instead of treating its mean
as the unseen tail. It cannot establish a maximum for an unobserved method. The 15-minute split
target leaves substantial margin for that uncertainty and for launch/DB/TRX overhead.

Retain all six existing rows and their provenance. Append the two diagnostic-run identities,
complete/partial distinction, rounding rule and this plan path to the table's `source` text.
Keep the existing format and Windows calibration metadata. The loader currently applies the same
conservative table on every host; adding platform dispatch or changing the refresh script is not
part of this repair. Do not feed missing TRX files to `refresh-nightly-class-costs.ps1` or merge
multiple runs into an average that loses the slower complete sample.

## Decisions

- **D-1: Correct the existing cost table, not the policy schema.** The brief's proposed data location
  differs from the loader's actual source. Rejected: moving the table into the policy, duplicating
  prices there, or changing its hash without a policy change; each adds unnecessary behavior.
- **D-2: Split the old group.** Keep 900-second packing, 2400-second lone-class split threshold,
  30 operands and the 3600-second watchdog. CheckpointMatrix becomes one 1560-second launch;
  PersistenceFailure one 1980-second launch; Publication becomes five method launches with
  15/15/15/15/3 nodes at 900/900/900/900/180 estimated seconds. Seven launches replace one,
  with no mixed-class launch containing any of these classes. Group numbers will change; find
  them by class/filter, never by the old number 10. Rejected: extending all watchdogs, a special
  group-number timeout, or a higher global target; those conceal the bad input and enlarge kills.
- **D-3: Retain fixed-watchdog headroom.** Estimated margins before start/report overhead are
  34 minutes, 27 minutes, and at least 45 minutes respectively. Six extra launches cost overhead
  (roughly 1.5-4.5 minutes using CARD-1410's 15-45-second assumption), but avoid losing an hour
  without a TRX. This is a completion-budget correction, not a claim of faster test bodies.
- **D-4: Use a provisional bound for Publication.** Its `pace` row must remain labelled partial.
  Rejected: calling 47-node extrapolation a measured 63-node run, or scheduling a native
  Publication rerun in this task. A later release-owned Windows qualification supplies the first
  complete result and may replace the bound. Do not require that expensive run to verify JSON.
- **D-5: Correct the stale estimate witness openly.** The three old rows total 900 seconds;
  the replacements total 7320 seconds. The unchanged recorded listing therefore gains exactly
  6420 seconds. Replace C1410's 5-8-hour estimate band with 9-10 hours, documented as an estimate
  for that fixed fixture and those prices, not a measured wall-clock SLA. This is not a test
  timeout increase. Keep the 60-100 launch band, 900-second multi-item cap, 3600-second maximum,
  exact census, suffix safety, oversized-method splitting and profile assertions unchanged.
- **D-6: Ordinary verification is Linux/offline PowerShell only.** Both runner-defaults and
  session-runners GETs were read on 2026-10-22; the default resolved to an eligible Linux runner,
  with Windows also present. Capacity is a dispatch-time fact. Re-read both routes at dispatch;
  omit `-Runner`, and use `-Platform Linux` to enforce the brief's no-desktop-execution constraint.
  Checkpoint lane: **Linux offline script/policy**. No Windows build, test, discovery launch,
  process probe or live qualification change. Evidence paths above are provenance, not routing.
- **D-7: Keep ownership separate.** This Plan owner lands its two Markdown deliverables as docs.
  A separate Code owner implements S1-S3 and gets ordinary Review. Do not mutate CARD-1404's frozen
  reviewed source or duplicate CARD-1410/1510/1511/1512. Coordinate these three prices with the
  CARD-0856 S3 owner so a broad refresh does not overwrite the conservative calibration.

These are the implementation decisions; no caller answer is required. No session that waits for
input is added or changed, so the CARD-1083 parking/release checklist does not apply.

## Failure ownership

The five completed failures are distinct from the budget kill. Cards were read in full using
`scripts/card.ps1 get ... -Board Antiphon`; no duplicate was filed.

| Group | Failure | Owner / remaining work |
|---|---|---|
| 3 | `DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent`: OpenConsole.exe deletion denied after exit assertions passed | Still uncarded as this occurrence. CARD-1020 is the older cleanup contract, not a diagnosed repair. Commission exact-method Debug with file/handle observations. |
| 3 | `ClaudeAdapterEffortPromptTests.Local_adapter_keeps_requested_effort_and_probes_after_clearance(xhigh)`: readiness false after effort selection | Still uncarded as this occurrence. CARD-0791 concerns different runner methods. Commission exact-method Debug preserving probe/peer evidence. |
| 5 | `RunnerProcessProbeTests.Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs` | CARD-1510, Backlog: launcher PATH fixture. |
| 10 | `AgentTaskLandPublicationTests.C448_V04_IgnoredFilesSurviveConfirmedPublication(.antiphon/inbox/photo.png)` | CARD-1512, Backlog: retained-evidence fixture expectation. A completed error, not caused by the kill. |
| 11 | `AgentTaskLandRemovalMatrixTests.C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries(2, submodule)` | CARD-1511, Backlog: impossible second-boundary fixture after mandatory early preservation. |

Full-page `-All` searches for each of the two group-3 method names returned zero cards during
this Plan. Their mechanisms remain unproved; do not prescribe retries or larger PTY budgets.
The caller can commission their card/Debug ownership separately. Historical failed/missing-TRX
coverage stays red/incomplete even after this plan and the cost repair land.

## Slices

**P0, this docs owner:** commit/push the unchanged Debug report and this plan. No production/test
file is changed here. The plan includes verification design so the next implementation stage is Code.

**S1, Code red witness:** add `scripts/fixtures/nightly/c1404-group10-listing.txt`, a curated source
fixture in the existing `class|S-or--|Method=count,...` form. It contains only the three classes,
43/52/63 nodes, with the exact method names/counts from R; annotate run identity and discovery hash.
Add `Test-C1404_Group10MeasuredBudget` to `scripts/test-nightly-tests.ps1`. Read the real cost and
policy files and call the real `Resolve-NightlySuiteChunks` with Windows rc exclusions against this
fixture. Do not stub the cost table or packer. Commit/push, then CP-1 must fail its outcome assertion
on missing/underpriced rows and the unsplit three-class launch, not a parse/setup error.

**S2, cost data:** add exactly the three rows above to `scripts/lib/nightly-class-costs.json` and
extend its provenance. Commit/push. CP-2 proves the new small fixture green; CP-3 exposes the
existing C1410 calibration assertion as red solely on `suite-hours`. Record the actual estimate
and roster before changing that assertion. If other reasons fail, diagnose rather than widen them.

**S3, calibration contract:** change only the fixed-fixture estimate band/comment in
`Test-C1410_MeasuredCostGroups` as D-5 specifies. Update the stale 5-8-hour comment in
`scripts/lib/nightly-tests-impl.ps1` to explain the corrected approximately 9.6-hour model estimate;
do not change executable packing/watchdog logic. In `docs/testing-and-build.md`, correct the
Windows-rc paragraph's obsolete single-chunk wording: the antiphon cap is per generated launch,
and link this plan/Debug report for the corrected group-10 evidence. Preserve the historical
watchdog report and its recorded results. Commit/push, then CP-4 is the narrow green.

No C# tests, test categories, fixtures used by product tests, limiter widths, profile exclusions,
clock budgets, watchdog values or refresh algorithms change. Code runs the evidence-diff guard
over its full base..HEAD and reports every commit/push and checkpoint outcome.

## Verification design

This folds the requested minimal test design into the plan. There are two behaviors: calibrated
group-10 selection, and a truthful existing full-listing estimate witness. Each gets one red and
one green. All four commands are offline script cases; the C1410 case simulates its recorded
listing in memory and does not execute 19,012 native tests. No suite or product build is authorized.

`Test-C1404_Group10MeasuredBudget` emits one aggregate `Assert-C487` result with reason details:

- **V-1, independent data oracle:** literal evidence-derived expected totals 1560/1980/3780 and
  node denominators 43/52/63, complete `measured` versus partial `pace` basis. Verify class costs
  through `Get-NightlyLaunchClassCost`; expected values do not come from the cost file itself.
- **V-2, actual packing:** resolve the real rc policy's Windows exclusions, which are nonempty;
  require seven launches for this 158-node fixture, two isolated complete-class launches and five
  Publication method launches. Resolve filters independently against fixture nodes, not merely
  against each chunk's attached `nodes`. Every one of 158 UIDs appears exactly once, no extra UID;
  every emitted filter is admitted by `Get-NightlyNativeExecutionArguments`. No class is excluded.
- **V-3, watchdog fit:** use `Read-NightlyExecutionPolicy` (including its hash validation) and
  `Get-NightlyWatchdogMs`; require 3600000 ms, largest launch 1980 seconds, and Publication splits
  at most 900 seconds with the 15/15/15/15/3 node multiset. Compare each launch's independent
  evidence-priced node sum to its emitted seconds. Restoring the old three-row absence must fail.
- **R-1, broader existing contract:** C1410 retains all existing coverage/suffix/profile/budget
  assertions. The raw pre-exclusion listing arithmetic is 28,401.5 + 6420 = 34,821.5 seconds
  (9.673 hours); its required-roster estimate is slightly lower because exclusions still apply.
  CP-3 must show `suite-hours` as the only failure reason, then CP-4 must pass under D-5. This
  explicitly replaces the historical 5-8-hour assumption using evidence, not a silent green fix.

Fixture detail needed for the split: Publication's method case counts are 12 for
`C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt`; 8 for
`C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment`; 7 for
`C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance`; four each for
`C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut`,
`C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained`, and
`C448_V09_PushAndConfirmationPreserveCompetingRemoteState`; three for
`C448_V04_IgnoredFilesSurviveConfirmedPublication`; two each for
`C448_V09_PushExitCannotReplaceRemoteConfirmation`, `C448_V22_ConfirmedPublicationUsesSavedReceipt`,
`C448_V33_CleanupRetriesDoNotEmitAnotherPublication`, and
`C688_Repair_AlreadyCanonicalRecordsLocalIdentity`; thirteen other methods have one case each.
The complete R discovery file is authoritative for the named fixture; do not reconstruct names
from display strings or omit argument variants. The largest method costs 720 seconds, so no
indivisible-method exception exceeds the 900-second target here.

These three transcribed source-fixture rows let Code work without access to the Windows logs.
Expand each count into stable unique fixture UIDs; they are selection-test identities, not claims
of fresh native execution. Keep the run/discovery hash above as fixture provenance.

```text
Antiphon.Tests.Application.AgentTaskLandCheckpointMatrixTests|S|C448_C09_ResumedTargetCannotAdoptAnEarlierAncestor=1,C448_V15_ChangedRecoveryPreparationNeverAdoptsNewWork=11,C448_V15_ResumeUsesOnlyAcknowledgedCheckpoints=11,C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates=18,C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop=2
Antiphon.Tests.Application.AgentTaskLandPersistenceFailureTests|-|C448_F01_SourcePinResultMustBeAcknowledgedBeforeTheNextPin=2,C448_F03_TargetIntentAcknowledgement=2,C448_F04_PushIntentAcknowledgement=2,C448_F04_PushResultAcknowledgementCannotAuthorizeCleanup=2,C448_F05_PublicationReceiptAcknowledgement=2,C448_F06_CleanupIntentAcknowledgement=2,C448_F07_PublicationSettlementIsAtomic=2,C448_F07_RefusalEvidenceAndTerminalEventCommitTogether=2,C448_V16_AcknowledgedCheckpointsGateDependentMutations=22,C448_V23_DeliveryFailureKeepsTheCommittedPublicationAndEvent=1,C448_V23_NotificationFailureCannotReplaceConfirmedPublication=1,C467_V06_AtomicSettlementFaultMatrix=12
Antiphon.Tests.Application.AgentTaskLandPublicationTests|S|C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut=4,C448_V04_IgnoredFilesSurviveConfirmedPublication=3,C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment=8,C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained=4,C448_V08_RejectedPushWithIndependentContainmentIsAlreadyPresent=1,C448_V09_PushAndConfirmationPreserveCompetingRemoteState=4,C448_V09_PushExitCannotReplaceRemoteConfirmation=2,C448_V22_ConfirmedPublicationUsesSavedReceipt=2,C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection=1,C448_V33_CleanupRetriesDoNotEmitAnotherPublication=2,C494_LostPushAckReconcilesRealTargetBeforeSourceChecks=1,C642_ProtocolInspectionsAreIdentityAndStatus=1,C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt=12,C665_DisposableOnlyIgnoredContentIsRemovedOnLand=1,C665_ProtectedIgnoredRefusalNamesPaths=1,C688_DirtyCanonicalCheckoutLandsWithResidue=1,C688_LandNeverRunsGitInTheSourceWorktree=1,C688_LocalMasterAheadOfOriginRefuses=1,C688_LocalMasterBehindOriginRebasesOntoOrigin=1,C688_PushPrecedesCanonicalAdvance=1,C688_RebaseAndVerifyRunInTheLandWorktree=1,C688_Repair_AlreadyCanonicalRecordsLocalIdentity=2,C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance=7,C688_TargetCheckedOutElsewhereIsResidueNotRefusal=1
```

Use the checkpoint tool, one run per committed slice group, selecting the rows below and binding
`--expected-source-sha` to that slice's full SHA. Bootstrap any required tool build through
`scripts/build-slot.ps1`; command rows take the checkpoint runner's slot. Keep the controller in
the foreground and continue `wait` after exit 75 until it settles. Preserve unedited CHECKPOINT
lines, script assertion counts, exit codes, source receipts and the reason for each intentional
red. CP-1/CP-3 are expected diagnostic red and never claimed as green qualification. CP-2/CP-4
are ordinary green. No unlisted rerun unless a new change/failure requires the same row again.

Use fresh ignored result directories; no generated logs, JSON, receipts or TRX are committed.
Every row is serial in the **Linux offline script/policy lane**, with no desktop execution.
No post-land mutation campaign is added: the two committed red-before-fix witnesses are the
requested positive controls; any later mutation scope needs its own commission.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---:|---|---|
| CP-1 | S1 | n/a | group10-cost-red | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1404_Group10MeasuredBudget` | V-1, V-2, V-3 red | exit 1; exactly 1 script assertion failed, 0 passed; underpricing/unsplit reasons, no setup error | n/a | 1 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S2 | n/a | group10-cost-green | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1404_Group10MeasuredBudget` | V-1, V-2, V-3 | exit 0; exactly 1 script assertion passed, 0 failed; 158 UIDs once in 7 launches | n/a | 1 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S2 | n/a | listing-estimate-red | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1410_MeasuredCostGroups` | R-1 red | exit 1; exactly 1 script assertion failed, 0 passed; only suite-hours reason | n/a | 5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-4 | S3 | n/a | listing-estimate-green | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1410_MeasuredCostGroups` | R-1 | exit 0; exactly 1 script assertion passed, 0 failed; all existing non-estimate assertions retained | n/a | 5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

## Cost and handoff

Closed ordinary verification budget: 12 minutes, including the two intentional red observations;
approximately 20-30 minutes authoring. No Windows execution is hidden in that estimate. Plan
validation consisted of source/card reads, read-only log arithmetic and Markdown/Git checks;
implementation checkpoints remain unrun.

Land these docs through Plan owner `996ed989`. Dispatch Code on a fresh assigned branch with this
plan and the three-row calibration; do not resume or edit a branch frozen under Review. Code's
ordinary Review precedes its own land. A future release owner admits the change to the candidate
and checks all seven affected filters for final TRX/census completeness and elapsed headroom.
That future gate must preserve independent assertion failures and cannot relabel R as complete.
Do not stop or modify supervisor PID 133964, run d2777e21, or its candidate to apply this repair.

--- next stage ---
next: code
handoff: Implement S1-S3: three evidence-priced class-cost rows and four closed Linux offline checkpoints. Keep the 60-minute watchdog and use existing method splitting; no desktop execution or live qualification changes. Land the Plan/report through their docs owner; fixture cards and group-3 Debug follow-ups remain separate.
artifact: docs/superpowers/plans/2026-10-22-card-1404-group-10-measured-budget-plan.md
