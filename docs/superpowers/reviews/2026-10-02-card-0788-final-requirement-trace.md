# CARD-0788 final Code requirement trace

This supplements the [S1–S5 handoff](2026-10-02-card-0788-code-handoff.md). Each row maps a plan sentence containing *verify*, *assert*, *must*, *exact*, *never*, or *always* to a reachable assertion. Test paths below are relative to `tests/Antiphon.Tests/Application/`; the number is the method's declaration line. A `No` row is an explicit later-stage obligation, not an ordinary Code assertion claimed green here.

| Plan line | Test and decisive assertion (`file:line`) | Covered? |
|---|---|---|
| 25 | `AgentTaskWorktreeBaseResolverTests.cs:30` `C788_PrunedRowsCostNoGitCommandsAndKeepWarnings`: command count and warnings | Yes |
| 27 | `RunnerTaskSettlementTests.cs:167` `No_push_on_a_plan_role_settles_succeeded_with_a_visible_warning`: warning in settlement header | Yes |
| 56 | `AgentTaskLandApprovalRequestTests.cs:133` `C788_AdoptionSubjectMismatchNamesSourceAndFlag`: source ID and `-FromTask` | Yes |
| 60 | `ReviewEvidenceSettlementTests.cs:242` `C807_SourceReviewFeedsAdoption`: stale confirmed tip warns and still admits the source | Yes |
| 61 | `AgentTaskLandAdoptionTests.cs:471` `C788_RecoveryMismatchDetailSurvivesRestart`: recovery identity/flag; desktop push repair is outside this card | Yes |
| 103 | `ReviewEvidenceConsistencyTests.cs:158` `C788_RepeatedSettlementKeepsOneWarningPerCode`: status, finding, scope, next, handoff and singleton outcome/events on replay | Yes |
| 187 | `docs/orchestration-loop.md` and `docs/ops-http.md`: warning-code paragraphs inspected in the source diff | Yes (manual doc check) |
| 203 | `AgentTaskLandApprovalRequestTests.cs:74,93,116,133,153` `C488_Evidence*`, `C788_*Mismatch*`: IDs, full refs/SHAs, flags | Yes |
| 205 | `AgentTaskWorktreeBaseResolverTests.cs:30,57,83,145,177`: eligible rows, command ceilings, explicit source and land hold | Yes |
| 219–225 | `docs/superpowers/plans/2026-09-29-card-0788-review-evidence-attribution-and-base-inspection-plan.md:479-503`: G-1..G-23 PCs are reserved for post-land SourceLanding Mutation | No (later Mutation) |
| 226–227 | `ReviewEvidenceConsistencyTests.cs:29,55,81,111`: settlement through `AgentTaskReplyService`, persisted review base and subject facts | Yes |
| 234–235 | `docs/superpowers/plans/2026-09-29-card-0788-review-evidence-attribution-and-base-inspection-plan.md:559-582`: source roster and fixed floors; checkpoint receipts are reported separately | Yes (roster) |
| 255 | Post-land `/api/version` and live canary require publication/activation | No (later Land) |
| 274 | `AgentTaskWorktreeBaseResolverTests.cs:706` `T0442_V29_inspection_budget_never_selects_a_partial_inventory`: configured/default five seconds and reached deadline hook | Yes |
| 276 | `AgentTaskWorktreeBaseResolverTests.cs:582` `T0442_V08_local_commit_availability_is_explicit`: reached failed common-directory lookup remains row-local | Yes |
| 279–280 | `AgentTaskWorktreeBaseResolverTests.cs:270,177`: landed branch sees no Git fault call, while pending land remains held | Yes |
| 283 | `ReviewEvidenceConsistencyTests.cs:29,55,111`: full distinct A/B/C and Review's own base | Yes |
| 312 | `ReviewEvidenceConsistencyTests.cs:268`, `CompletionWarningDeliveryTests.cs:58`, `LandEvidenceWarningDeliveryTests.cs:42`: named cut reached and recovered | Yes |
| 315 | Same three recovery methods: scan after receipt does not submit another prompt | Yes |
| 331 | The V/R named roster is exercised by CP-1..CP-7; individual result counts are in receipts | Yes (roster) |
| 340 | `AgentTaskLandApprovalRequestTests.cs:133,153`: source identity label/ID, owner shape and recovery flag | Yes |
| 349 | `AgentTaskLandAdoptionTests.cs:471`: reached recheck, immutable evidence, fresh-provider detail and no publication | Yes |
| 357 | `ReviewEvidenceConsistencyTests.cs:29,55,81,111,158,198,216,268,328`: all named V-2 methods are reachable | Yes |
| 362 | `ReviewEvidenceConsistencyTests.cs:111`: remote wins; absent/non-full fallback and distinct ObservedSha, ClaimedSha and alternate-origin facts are excluded | Yes |
| 376 | `AgentTaskWorktreeBaseResolverTests.cs:30,57,83,112,145,177`: all named V-3 methods are reachable | Yes |
| 382 | `AgentTaskWorktreeBaseResolverTests.cs:177`: non-Succeeded pending land remains a hold | Yes |
| 385 | `AgentTaskWorktreeBaseResolverTests.cs:30,57,83,112`: old algorithm's command and candidate behavior fails these assertions | Yes |
| 407 | `AgentTaskDispatchBaseGuardTests.cs:475,587,596,663,700`: incomplete predicate and role/preview cases | Yes |
| 418 | `TaskCompletionContinuationTests.cs:48`: no invented confirmed tip, remote then Primary | Yes |
| 428 | `RunnerTaskSettlementTests.cs:206`: claim warning suppresses duplicate no-push line | Yes |
| 442 | `CompletionWarningDeliveryTests.cs:21,58`, `LandEvidenceWarningDeliveryTests.cs:22,42`: named V-6/V-7 methods | Yes |
| 451 | Same delivery methods plus `ReviewEvidenceConsistencyTests.cs:216,268`: full wire/spill text and keyed receipt | Yes |
| 465 | `TaskCompletionContinuationTests.cs:48` and CP-1: Unit lane floor is 3000, not a source census | Yes (receipt in final report) |
| 474 | `docs/superpowers/plans/2026-09-29-card-0788-review-evidence-attribution-and-base-inspection-plan.md:479-503`: exact PC method filters are later Mutation work | No (later Mutation) |
| 477 | Same PC table: parameterized method prefix with trailing wildcard; no literal suffix | No (later Mutation) |
| 479–503 | Each G-n has one specified compiling defect and named decisive label in the plan; execution belongs to post-land SourceLanding Mutation | No (later Mutation) |
| 512–513 | PC red/build/restoration classifications and noninterference are Mutation procedure gates | No (later Mutation) |
| 518 | PC evidence binds separate landed SHAs; neither group is landed in this worktree | No (later Mutation) |
| 542 | `TaskCompletionContinuationTests.cs:48` and `RunnerTaskSettlementTests.cs:244`: Code policy contrast and no-progress shape | Yes |
| 568–569 | CP-1..CP-7 exact filters and fixed floors in plan lines 594–602; receipts report actual roster | Yes (receipt in final report) |

The `No` rows are preserved as pending by the Final profile: every PC requires method-scoped SourceLanding Mutation, and the version/live checks require later activation. No EF model, DbContext, snapshot, migration or Review bundle changed.

## Final Windows and clock-skew corrections

| Item | Test label / assertion | Resolution |
|---|---|---|
| 3: future dispatch | `AgentTaskReplyIntegrationTests.C788_NoChangeFutureDispatchCleansUp` (`no-change-future-dispatch`, ordinary no-change and committed controls, and committed future dispatch): unchanged worktree is removed; only attributed ordinary committed work reaches `feat/parent` | The plan has no retention rule for a negative elapsed interval. S5 still records no-movement evidence and a warning; cleanup-only merge-back removes an unchanged branch and retains a branch with commits. Future no-change was red at e9d56b50. CP-8 covers 4 results. |
| 1: Windows checkout paths | `AgentTaskWorktreeBaseResolverTests.C788_CheckoutSafetyProbesOnlyMaximalTips` (2 results): maximal checkout's status command is present and ancestor's absent after slash normalization | Expected and observed paths are compared in one separator form. CP-3 covers both results. |
| 2: Windows review reports | `ReviewEvidenceSettlementTests.C807_IgnoredEvidenceSettlesWithoutApproval`, `C807_RepeatedSettlementIsIdempotent`, `C807_WarningPreservesOtherWarnings`: `Presented()` finds the evidence block's blank-line delimiter | `C544World.ReviewReport` returns LF on every OS. Its consumers were inspected for CRLF dependence; none requires it. CP-5 covers these labels. |

The Windows CP-3, CP-5, and CP-1 Unit rows remain a separate desktop task. All 23 PCs remain pending the paused Mutation stage.
