# CARD-0788 Code handoff: evidence attribution and base inspection

This Code pass implemented S1–S5 and pushed `feat/card-task-fb1a06fc`. It is **not a completed
Final verification**. The task's 120-minute proof cutoff stopped new verification work; its
150-minute outer time box leaves this handoff rather than a Review dispatch.

## Implemented source

- Land evidence refusals now name the recorded and required subject/ref/SHA and give guidance
  for ordinary owner land, owner recovery, and adoption. Source resolver and both protocol
  rechecks carry that detail into durable refusal events.
- Review settlement compares a full claimed SHA with the Review's own persisted base and with
  the subject's settlement-confirmed remote or Primary tip. Inconsistencies produce warning
  events and completion header lines while retaining the reported evidence binding.
- Same-card base inspection prunes ineligible rows before Git work, batches branch tips and
  containment, probes checkout safety on maximal tips, and uses a five-second default budget.
- Dispatch blocks incomplete Auto inspection when the task is a Review or has a previewed
  Continue/source selection; a fresh Code target preview keeps its safe target behavior.
- Non-Code Worktree settlement surfaces no-pushed-progress and local no-movement facts to the
  caller. An oversized land refusal's saved pointer can be confirmed only when its owned spill
  still equals the immutable notification body.

No migration, DbContext, snapshot, Review bundle, CARD-0826, CARD-0904 or CARD-0905 file changed.

## Ordinary checkpoint receipts

| Row | SHA | Executed / pass / fail / skip | Status |
|---|---|---:|---|
| CP-2 | `acb4e144978a313bcb44f6d672d95ce0badf146f` | 44 / 44 / 0 / 0 | Green |
| CP-3 | `253e0a19d169d50ce1fc9c5f114d53c6745e2727` | 170 / 170 / 0 / 0 | Green before later S5/delivery changes; final-SHA rerun pending |
| CP-5 | `67c0568cb9c580442dbacdea3b39039c3a0ef290` | 58 / 58 / 0 / 0 | Green |
| CP-6 | `20062c166dff6f6d6796c188f663fc2dabd87644` | 11 / 11 / 0 / 0 | Green |
| CP-7 | `20062c166dff6f6d6796c188f663fc2dabd87644` | 7 / 7 / 0 / 0 | Green |
| CP-1 | — | — | **Pending** final Unit lane, floor 3000 |
| CP-4 | — | — | **Pending** full runner progress class row, floor 31 |

All listed green runs used `scripts/run-checkpoint.ps1`, clean committed source and a granted
build slot. The recorded failed intermediate CP-2, CP-5 and CP-6 runs were fixture defects
and were corrected in subsequent commits. CP-3's first two attempted invocations had invalid
CLI arguments and executed zero tests. The named TestDesign PCs and scratch-revert red/green
proofs for new tests were not run. Windows rows and live activation are separate tasks.

## Requirement trace

The line references below are to the frozen plan. **Yes** means a named ordinary test has the
specified assertion in source; it does not upgrade an unrun checkpoint to a passing receipt.
**Pending** means the requested proof or action is still missing. This table covers each plan
sentence containing “verify”, “assert”, “must”, “exact”, “never”, or “always”, including the
historical/scope sentences that do not themselves request a new test.

| Plan line(s) | Requirement and named decisive label | Trace |
|---|---|---|
| 25, 205, 274–280, 374–385 | Avoid Git for pruned branches, batch tips, probe only maximal tips, retain pending land and explicit source; Resolver `C788_PrunedRowsCostNoGitCommandsAndKeepWarnings`, `C788_ContainedSiblingBranchesAreClassifiedInOneQuery`, `C788_CheckoutSafetyProbesOnlyMaximalTips`, `C788_PendingLandRowStillHoldsWithoutInspection`, `C788_RequestedNonSucceededRowStillValidated`, V07–V10/V29; CP-3 170/170. Named command ceilings and fault hooks are in these tests. | Yes; final-SHA CP-3 pending |
| 27, 413–435 | Non-Code warning uses no-push facts and preserves Code policy; Continuation `C788_NoPushedProgressWarningShape`, Runner `No_push_on_a_plan_role_settles_succeeded_with_a_visible_warning`, `C788_NonCodeNoPushRoleMatrix`, `C788_ClaimWarningDoesNotDuplicateNoPush`, `C788_NonWorktreeDoesNotGetNoPushWarning`, `Unmarked_completion_gets_the_code_progress_policy`; CP-4/Unit pending. | Yes in source; checkpoint pending |
| 56, 61 | RepairSource is never adopted with `-FromTask`, and this card does not claim the separate desktop push fix; Adoption `C753_ReviewedRepairAdoptionObeysLocalCasAndRemoteLease`, Approval `C788_AdoptionSubjectMismatchNamesSourceAndFlag`, and unchanged exclusion. | Yes |
| 60, 285–288 | Do not duplicate CARD-0807 adoption feed; strengthen `ReviewEvidenceSettlementTests.C807_SourceReviewFeedsAdoption` with a stale confirmed tip and retain real admission. | Yes, CP-5 |
| 103, 357–370 | Warnings bind evidence without changing finding, status, scope, next or handoff; Consistency `C788_SubjectTipMismatchWarnsAndBinds`, `C788_ReviewBaseMismatchWarnsAndBinds`, `C788_RepeatedSettlementKeepsOneWarningPerCode`, C807 adoption feed. Status/scope/next/handoff are not all explicitly asserted in the new repeated-settlement test. | **Pending** named assertions for all invariant fields |
| 187 | Docs list settlement warning codes for orchestrators; `docs/orchestration-loop.md` and `docs/ops-http.md` source diff. | Yes, manual doc inspection |
| 203, 333–352 | Admission errors name identities, refs, SHA and flags; Approval `C488_EvidenceSubjectMatches`, `C488_EvidenceRefMatches`, `C488_EvidenceShaMatches`, `C788_AdoptionSubjectMismatchNamesSourceAndFlag`, `C788_OwnerMismatchNamesSiblingAndAdoptionShape`; Adoption `C788_RecoveryMismatchDetailSurvivesRestart` checks six boundary/mode arguments, reached hook, durable event and no publication. | Yes, CP-2 |
| 219–225, 479–508, 512–518, 542 | Every G-1..G-23 positive control must go red at its named assertion under the specified compiling defect; preserve restoration and record L/C/O. Neither the 23 mutation cycles nor the brief's scratch-revert red/green proof for every new test ran. | **Pending** all PCs and red/green evidence |
| 226–227, 283, 355–365 | Use real reply settlement, full persisted A/B/C facts and Review's own base; Consistency `C788_SubjectTipMismatchWarnsAndBinds`, `C788_ReviewBaseMismatchWarnsAndBinds`, `C788_ConsistentOrMissingFactsStaySilent`, `C788_RemoteConfirmedTipPrecedesPrimary`; CP-5 58/58. No settlement ls-remote was added. | Yes |
| 234–235, 568–569 | Source-expanded roster and frozen floors: plan final census 23/21/67/16/87/23/8/48/16/11/31/11/7; CP-2/3/5/6/7 floors met. CP-1 and CP-4 not run; no floors lowered. | Partial; **pending** CP-1/CP-4 |
| 255, 598–606 | Verify post-land published SHA, server `/api/version` and live refusal/preview canaries. No land or activation was authorized in this Code worktree. | **Pending** later Land/activation task |
| 273–280 | Real Git argv/command counts and current timeout; resolver tests assert injected deadline/error hooks and excluded branch paths. CP-3 170/170. | Yes; final-SHA CP-3 pending |
| 289–297 | Delivery rigs use real producer/queue/adapter, category, limiter and Slow allowlist. `ReviewEvidenceConsistencyTests`, `CompletionWarningDeliveryTests`, `LandEvidenceWarningDeliveryTests`; CP-5/6/7 green. | Yes |
| 309–325, 442–451 | Completion and refusal receipt loops cover busy/eligible, raw/spill, named cuts, exact warning text, keyed note, prompt above attempt floor and replay idempotence. `C788_ConsistencyWarningReceipt/Recovery`, `C788_ProgressWarningReceipt/Recovery`, `C788_LandEvidenceRefusalReceipt/Recovery`; CP-5/6/7 green. Land refusal spill file content is compared with saved note. | Yes |
| 340–349 | Adoption required identity label/ID, recovery flag, fresh-provider read and no evidence rewrite; Approval `C788_AdoptionSubjectMismatchNamesSourceAndFlag`; Adoption `C788_RecoveryMismatchDetailSurvivesRestart`. | Yes, CP-2 |
| 362 | Valid remote tip outranks Primary; `C788_RemoteConfirmedTipPrecedesPrimary` checks opposing tips. Explicit negative assertions for ObservedSha, ClaimedSha and alternate origin are absent. | **Pending** those negative arms |
| 382, 389–407 | Pending land never pruned; incomplete guard is role/preview-specific; Resolver `C788_PendingLandRowStillHoldsWithoutInspection`; Dispatch V30, `C788_ReviewWithIncompleteInspectionBlocks`, `C788_FreshCodeWithTargetPreviewKeepsSafeBase`, `C788_ExplicitBaseOverridesIncompleteAutoGuard`, `C788_NonIncompleteUnknownKeepsExistingBehavior`; CP-3 green at earlier SHA. | Yes; final-SHA CP-3 pending |
| 418, 428, 500 | Unknown confirmed tip is not invented; claim warning suppresses duplicate no-push line; `C788_NoPushedProgressWarningShape`, `C788_ClaimWarningDoesNotDuplicateNoPush`. Unit and CP-4 were not run. | Yes in source; checkpoint pending |
| 451, 465, 474, 477 | Exact filters used trailing class `*`, no literal parameter suffix; receipt tests assert full wire/spill content. Unit floor 3000 not run. | Partial; **pending** CP-1 |

## Next Code pass

Run CP-1 (one normal Unit lane at the current final SHA), CP-4, and a final-SHA CP-3; execute
the brief's named red/green requirements or explicitly renegotiate its time box; add the
missing invariant/negative assertions identified above and rerun affected rows. Once ordinary
proof is complete, hand off to Review with the Windows and live activation tasks still separate.
