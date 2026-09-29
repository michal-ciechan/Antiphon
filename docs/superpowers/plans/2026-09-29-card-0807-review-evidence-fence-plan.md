# CARD-0807: make review evidence instructions parseable and rejection visible

Date: 2026-09-29. Stage: Plan; verification design is a separate TestDesign dispatch.
Source inspected: `16da56da5a9c9bc3d93ba5006dd139b072750c26`.

## Outcome and scope

Remove the Markdown fence from the shipped Review example, keep quoted/fenced evidence
ineligible for approval, and explain that rejection to the caller through the existing
completion note. Distinguish the original landing owner from the reviewed source task for
`-FromTask`. Preserve settlement, authorization, scope capping, and landing guards.

This plan covers all four requested changes. It does not implement production changes.
CARD-0788's board description was corrected during planning; its independent timeout and
unpublished-commit observations remain open. TestDesign must append its verification design,
guard/positive-control inventory, cost, and closed checkpoint table before Code dispatch.

## Ground truth

Both full card descriptions were read from the board before design. The live statistics and
CARD-0458 incident below are evidence supplied by CARD-0807, not a new census by this planner.

| Assumption or observation | Code or durable record | Implication |
|---|---|---|
| Copying the Review bundle should produce usable evidence. | `server/Bundles/stage-review.md` encloses its only example in triple backticks. `ReviewEvidence.StandaloneHeadingIndexes` excludes fenced, quoted and indented headings. | The instructions contradict the parser. Fix the example, not the approval boundary. |
| A rejected fenced block is diagnosable. | `ReviewEvidence.TryParse` returns `default` when no standalone heading is found: `Found=false`, `Usable=false`, `Warning=null`, scope Unknown. | Distinguish an ignored evidence-shaped heading from no evidence. |
| There is no warning at all. | `AgentTaskReplyService.RecordDelegateStageOutcomeAsync` already adds a generic Warning event, or `evidence.Warning` for recognized invalid evidence. | Preserve that event; add a specific reason and caller-visible corrective text. A task event alone is insufficient delivery evidence. |
| Rejected evidence always means a null subject. | The settlement starts `subjectId` at `FollowUpOfTaskId`; SHA/ref/repository stay null and a profiled scope stays Unknown when binding fails. | Fresh reviews have null subject; follow-ups can retain a historical subject without approval. Tests must not erase this distinction. |
| The server picks a canonical subject from explicit `-Card` binding. | Settlement reads the GUID from the raw report, loads that task, and copies its branch/repository. `ReviewSubjectAuthorized` permits self, matching follow-up, or same card. A different explicit follow-up subject is rejected before that authorization check. | Do not change card binding, infer a subject from `-StartRef`, or remove the follow-up guard. |
| Adoption evidence should name the original owner. | `AgentTaskLandService.RequestAsync` resolves the adopted source and calls `LandApproval.LoadRecoveryEvidenceAsync(..., source)`. `LoadUsableEvidenceAsync` checks that source's GUID/ref/repository and the expected SHA. `AgentTaskLandAdoptionTests` uses `AddReviewAsync(db, source, reviewed)`. | The source named by `-FromTask` is the evidence subject; the original owner remains the `-Land` target. |
| A clean report tells the caller whether evidence bound. | `BuildParentNoteAsync` supplies `ReviewEvidenceFacts` only for a bound Clean outcome. `DelegationReportFormatter.BuildCompletionNote` omits the evidence header when those facts are absent. | Put the ignored-block diagnostic in the preserved warning/header, without manufacturing evidence facts. |
| Adding several sentences is harmless. | The source bundle is 2,468 characters including its final newline. `TaskPlatformGuidanceTests` caps trimmed stage bundles at 2,480; `InstructionBundleTests` caps them at 2,500. One guidance test pins the current owner sentence verbatim. | Replace/tighten wording and update the affected semantic assertion; preserve the size caps and unrelated obligations. |
| CARD-0788's workaround proves a binding-path bug. | Its stored observations show differing evidence subjects, but its explanation is contradicted by the source trace above. | Preserve the observations and explicitly supersede the causal claim. |

CARD-0807 records 93/219 Review Clean/Found rows without a SHA since 2026-09-24,
including 33/79 Clean rows; all ten sampled null rows had fenced evidence. It identifies
Review `68adb121-7c51-4343-b113-5abd88a3fbd7` and outcome
`95eb09c0-d17d-4d23-a055-4be873878f63` for CARD-0458. No historical row is repaired by this plan.

## Decisions

### D-1: keep explicit standalone evidence as the authority

Change the example in `stage-review.md` to bare, unindented lines and explicitly require
"unfenced, unindented, not quoted". Retain placeholders for a full subject GUID, full reviewed
SHA and Full/Interim/None; scope Full still means the entire required selection ran.

The executable bundle test must use `InstructionBundles.TextOf(InstructionBundles.StageReview)`.
Substitute only those three placeholder values in the full text and feed it unchanged to
`ReviewEvidence.TryParse`. Assert Found/Usable, the supplied GUID, normalized full SHA, Full
scope and no warning. Assert each placeholder was present once so a wording change cannot
quietly bypass substitution. Do not extract just the four inner lines, trim every line,
remove quote prefixes, or strip fences: those approaches would hide this exact regression.

Rejected: accepting fenced/quoted evidence. Reports can quote examples or prior reviews;
turning those into authority would break the intentional CARD-0488 boundary.

### D-2: add a diagnostic without broadening accepted grammar

Extend the existing heading scan to return standalone heading offsets plus whether an exact
evidence heading was skipped for fencing, quotation or indentation. Use one stable code:
`review_evidence_not_standalone`. Detect a whole heading after the relevant presentation
prefixes, not a prose substring or an inline mention. Nested blockquote prefixes and the
existing backtick fence forms need coverage. Continue cutting at the closing report token
before scanning. This is not a CommonMark parser rewrite.

| Scan result before the closing report token | Required parser result |
|---|---|
| No heading-shaped line | Existing default, no warning. |
| Only ignored fenced/quoted/indented heading(s) | Found=true, Usable=false, null parsed subject/SHA, Unknown scope, `review_evidence_not_standalone`. |
| One actual standalone block plus ignored examples | Parse only the actual block; ignored examples neither create duplicates nor poison valid evidence. |
| Two actual standalone blocks | Existing `review_evidence_duplicate`. |
| Actual block with invalid subject/SHA or after next-stage | Preserve the existing specific error and its precedence over ignored examples. |
| Evidence only after the report token | Ignore it as today; no new authority or warning. |

`Found` now means an evidence-shaped construct was encountered, not permission to bind it.
`Usable` remains the gate. Update the old
`C488_QuotedAndFencedBlocksAreNotEvidence` assertions to verify refusal, null parsed
coordinates, Unknown scope and the diagnostic rather than `Found=false`.
`C544_ScopeGrammar` must still report Unknown for fenced scope declarations.

Rejected: a generic missing-evidence warning for every mention, or making malformed evidence
fatal to settlement. Those changes add noise or change stage behavior beyond this defect.

### D-3: distinguish landing owner and reviewed source in the instructions

Use this identity contract in the bundle and in `docs/orchestration-loop.md`, under
"Reviewed owner recovery" (and keep the adjacent repair/continuation guidance consistent):

| Operation | `subjectTaskId` in Review evidence | Landing command shape |
|---|---|---|
| Ordinary owner review | Owner whose pushed branch tip was reviewed | `-Land <owner> -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>` |
| Recover the owner's reviewed branch | That same owner | Same identity flags plus `-RecoverReviewedSource` |
| Adopt a different task's reviewed branch | Source task whose exact current pushed tip was reviewed | `-Land <owner> -FromTask <source> -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>` |

The review's own new worktree GUID is not the subject merely because it checked out that SHA.
`-StartRef` chooses a base; several branches can contain the same SHA, so it cannot identify
the intended source by itself. The caller's brief must name both owner and source when they
differ. Keep explicit `-Card`; a fresh same-card Review may name the repair. If a Review is a
follow-up, its `FollowUpOfTaskId` must match the intended evidence subject; commission a fresh
same-card Review when an old-owner follow-up would conflict.

Do not imply that any task tagged `-RepairSource` is eligible for `-FromTask`:
the adoption admission guard excludes `RepairSourceTaskId != null`. That mode's attribution
rules still apply. The table describes the existing eligible source branch, typically a
separate Code/Worktree repair dispatched with `-StartRef`.

Replace the misleading original-owner placeholder and clarify that the original owner is
carried for landing, while evidence names the reviewed source. Keep the bundle at <=2,480
trimmed characters by tightening surrounding prose without dropping checkpoint, delivery,
scope, read-only, routing or platform requirements. Update
`TaskPlatformGuidanceTests.Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes`
to assert the two identities and all command flags instead of preserving the old ambiguous
sentence. Do not raise either size limit.

Rejected: server-side subject inference, relaxing `review_evidence_subject_mismatch`, removing
`-Card`, or rewriting durable evidence to fit the intended landing.

### D-4: use the existing completion header for the corrective warning

Surface the new parser diagnostic on successful Review completions, in addition to the
existing settlement Warning event. At `AgentTaskReplyService.BuildParentNoteAsync`, derive
the skipped-block warning from the same raw report used for settlement and append it to the
existing `warning` argument, preserving any other warnings. Do not use the distilled report.

The preserved header warning must contain:

- `review-evidence-warning=review_evidence_not_standalone`
- Plain corrective text: evidence was ignored because it was fenced, quoted or indented;
  submit the evidence block as bare lines before the next-stage block.
- No synthetic evidence ID, subject, SHA or Full-scope assertion.

The pure classification can be shared with parsing; do not create a second divergent scanner.
Keep successful standalone reports and genuinely absent blocks on their existing paths.
Warnings for other invalid-evidence reasons and authorization failures retain their current
behavior. This slice targets the skipped-heading defect, not a general review status redesign.

`BuildCompletionNote` already preserves `warning` in `Note.Header`, outside excerpting.
Profile-v1 settlement freezes it in `TaskCompletionNotification.Snapshot.NoteHeader/RawBody`
in the same commit as the outcome and Warning event. Distilled and polled renderings preserve
the header. Delivery recovery must continue rendering the frozen snapshot, without reparsing
mutable task text. Legacy direct notes use the same header composition. Prefer this existing
seam; no new notification kind, attention feed, API field or schema migration is required.

Do not change task status, reported finding, `NextStage`, or `PipelineHandoff` because of this
warning. A Clean/Found outcome can remain historical while lacking approval. The warning
makes that lack actionable; landing's existing gates still decide authority. It does not
automatically send another turn to the reviewer or commission another paid Review.

Rejected: a log/event-only fix (caller still misses the cause), an automatic retry, or a new
alert sink. The existing completion route already has persistence and receipt contracts.

### D-5: preserve historical approval and separate CARD-0788's remaining defects

No migration/backfill, manual StageOutcome edit, generic parser fallback, or automatic
re-approval of old reports. An existing invalid review still requires fresh explicit review
evidence through the normal workflow. Keep the original observations in CARD-0788; mark its
`-Card` root-cause claim and interpretation of the workaround as superseded.

## Implementation slices

| Slice | Files / changes | Required coverage to design |
|---|---|---|
| S1: correct the publishing contract | `server/Bundles/stage-review.md`; `docs/orchestration-loop.md`; `tests/Antiphon.Tests/Application/InstructionBundleTests.cs`; affected assertion in `TaskPlatformGuidanceTests.cs` | Shipped example parses without presentation cleanup; bundle size/composition contracts remain; owner/source instructions describe ordinary recovery and adoption. |
| S2: diagnose ignored evidence | `server/Application/Services/ReviewEvidence.cs`; `tests/Antiphon.Tests/Application/ReviewEvidenceParserTests.cs` | Diagnostic matrix in D-2, mixed valid/ignored blocks, duplicate and placement precedence, LF/CRLF, quote nesting, case-insensitive heading, no-heading and token cutoff. Existing SHA/scope grammar remains. |
| S3: settle and deliver the diagnostic | `server/Application/Services/AgentTaskReplyService.cs`; new `ReviewEvidenceSettlementTests.cs` and `ReviewEvidenceDeliveryTests.cs` under `tests/Antiphon.Tests/Application/`; minimal optional raw-report customization in `TestHelpers/C544DeliveryRig.cs` if needed | Real reply settlement writes specific Warning + no captured SHA/ref/repository + Unknown profiled scope; caller receives the warning through the real queue; valid evidence unchanged; source-subject binding reaches landing admission. |
| S4: record the explanation | CARD-0788 board revision (completed in Plan); final consistency pass over S1 docs and this plan | Fresh board read confirms authoritative correction and retention of independent observations. Never edit generated `docs/cards/` files. |

S1-S3 form one committed implementation group for verification: commit each meaningful
slice, then run the final combined checkpoint selection once for the group. If TestDesign
identifies a justified earlier boundary, it must name that boundary and its cost in the
manifest; do not introduce an ad hoc build per test file.

## TestDesign handoff requirements

The brief did not fold TestDesign into Plan. The following are constraints and inspected
seams for that stage, not an executable checkpoint manifest or claimed test results.

Read the bodies before assigning final test names, V/R IDs, execution floors and PCs.
In particular, several existing `AgentTaskReviewEvidenceTests` methods named for settlement
only parse text or use `StageOutcomeService`; their names do not prove real reply settlement.
Use `C544World.CreateTaskAsync`, `DispatchAsync`, `SeedTurnAsync`, and
`AgentTaskReplyService.OnTurnEndAsync` for new settlement coverage, with isolated schemas.

Required ordinary coverage:

1. The embedded bundle regression test in D-1; the D-2 parser matrix; existing bundle size,
   platform, checkpoint and scope guards. A test that reconstructs a clean evidence block
   instead of consuming the bundle cannot close S1.
2. Fenced and quoted reports through real settlement, including successful Clean and profiled
   Found reviews, the follow-up fallback distinction, repeated settle idempotency, and a valid
   standalone sibling report. Inspect the actual warning/outcome/snapshot, not just the parser.
3. A fresh explicitly card-bound review naming a separate same-card source: settlement must
   capture that source's GUID/ref/repository and exact reviewed SHA. Feed its produced outcome
   ID to adoption admission for `owner <- source`; do not seed the approval row manually.
   Wrong original-owner evidence must still refuse, and a mismatched follow-up must not bind.
   Retain the existing full adoption regression class as landing-boundary coverage.
4. Producer-to-recipient warning delivery for busy and already eligible callers, raw and
   distilled notes, and poll-shrunk rendering. Include recovery at the settlement/outbox,
   enqueue, and frozen rendering/attempt boundaries; cover spill text when the warning/header
   is carried by a spilled completion. Reuse `C544DeliveryRig` and its existing fault seams.
   Observe the complete submitted `UserPrompt` and the same durable notification identity.
   A queue insert, Warning event, Sent bit, or header-only prompt is not delivery proof.

Delivery inventory to expand into the formal design:

| Producer | Destination | Persistence / identity | Recovery | Receipt |
|---|---|---|---|---|
| Review reply settlement / `BuildParentNoteAsync` | Existing parent session completion note | Warning event, StageOutcome, settlement event, profile-v1 TaskCompletion snapshot; notification ID and `task:<root>` key | Existing notification scanner and frozen `CompletionDeliveryJson`; recreated providers at fault cuts | Complete matching caller UserPrompt above attempt floor; pointer plus matching file hash for spill |

`C544DeliveryRig` uses the production reply, notification and queue services with a controlled
terminal whose submitted callback records the UserPrompt. It proves server persistence and
queue behavior, not a real provider's terminal rendering. No new terminal transport is designed.
The existing `VerificationRoundDeliveryTests` supplies patterns for busy/eligible, distillation,
spill, and fault recovery. Extend the rig only at report input; never preinsert an expected
caller receipt or mutate rows to simulate progress.

The checkpoint table must include Unit and the named affected integration classes, with exact
TUnit filters and one shared isolated build for compatible rows. Candidate integrations are
the two new classes, `AgentTaskReviewEvidenceTests`, `AgentTaskLandAdoptionTests`, and the
specific existing completion regressions affected by any helper changes. Do not schedule all
of `AgentTaskReplyIntegrationTests` just because one seam resides in that partial class.
If a new delivery class is Slow/process-spawning, apply the required category, slow roster,
MessageQueue serialization and assembly-local ProcessSpawnLimit.

Safety guards requiring distinct positive controls include the standalone-only boundary,
ignored-example precedence, source subject/ref binding and follow-up authorization, and
warning preservation through the immutable header/recovery path. TestDesign enumerates their
independently bypassable checks and exact mutations. Code runs ordinary V/R; Mutation executes
PCs after land. Plan ran no builds/tests and claims no executed PCs.

## Platform, execution and rollout

`GET /api/runner-defaults` and `GET /api/session-runners` were read on 2026-09-29. The runtime
default resolves to an eligible Linux runner; an eligible Windows runner is also available.
No source change needs a Windows-specific lane. Keep task platform Any and omit `-Runner`
and `-Platform` unless TestDesign finds a specific OS-only fixture. Rediscover placement at
dispatch; no fleet location is a design constant.

Use the checkpoint tool for Code/Review, one run per committed slice group:
`dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this-plan>` followed by `wait`
until exit is not 75, following `docs/testing-and-build.md`. Bootstrap/build/test drivers go
through the required build-slot gate. Do not run this plan until TestDesign supplies its
closed `### Checkpoints` table, real execution floors, and estimates. No broad suite, client
build, production runner launch, or deployment is required for this planning artifact.

After implementation and separate Review, land through the ordinary caller workflow and
verify server activation using the owning runbook and `/api/version`. A newly commissioned
Review must receive the corrected bundle; an existing delegate can retain its earlier
composition. There is no historical-evidence migration. Rollback is a reviewed revert;
already recorded evidence and delivery snapshots retain their existing meaning.

## Planning record and acceptance

CARD-0788 now has correction revision 3, prepended to its retained historical description.
The script reported the successful revision and then failed while rendering card-file status
because Linux cannot resolve the returned Windows `C:` repository path. A fresh `card.ps1 get`
confirmed the correction, historical section, timeout observations and third-occurrence
unpublished-commit report are all present. Do not repeat the write to repair that display error.

The implementation is complete only when the shipped example parses, presentation-only
evidence still cannot approve a land, the caller actually receives its specific warning,
explicitly card-bound source review evidence passes adoption admission, and the owner/source
documentation agrees. Existing authorization, scope and landing guards must remain green.
Next stage: TestDesign; no product decision blocks the plan.

## Verification design

Appended 2026-09-29 by TestDesign task `fc539741`, against pinned plan/source commit
`0170abd2358cf5ccf4fb0d4846bd6f3f48f5f438` (Plan task `b3ff9b03`). D-1 through D-5
remain the fix design. This section supersedes the pending-TestDesign statements above.
All cases and costs below are designed, not executed. Next stage is Code.

### Inspection

| Bodies read | Boundary and coverage |
|---|---|
| `server/Bundles/stage-review.md`, `InstructionBundleTests`, `TaskPlatformGuidanceTests`, `ReviewEvidence` and all `ReviewEvidenceParserTests` | Embedded text, presentation grammar, precedence, scope and byte/character budgets: V-1/V-2, R-1/R-2. |
| `AgentTaskReplyService.RecordDelegateStageOutcomeAsync`, `BuildParentNoteAsync`, `AddCompletionObligationAsync`, settlement call sites and `TryDescribeGitAsync`; `DelegationReportFormatter.BuildCompletionNote`; `TaskCompletionNotification` | Raw authority, authorization, captured coordinates, additive warning, immutable header and atomic obligation: V-3/V-4/V-5, R-3/R-4. |
| All `AgentTaskReviewEvidenceTests`; `VerificationRoundSettlementTests` scope/subject/atomic/applicability cases; `C544World` and `TurnSeeding` | Existing C488 names include parser-only checks and manually inserted outcomes. They remain regressions, not substitutes for the new real-settlement tests. |
| All `AgentTaskLandAdoptionTests`; `LandApproval.LoadUsableEvidenceAsync`/`LoadRecoveryEvidenceAsync`; adoption admission in `AgentTaskLandService.RequestAsync`; `LandingSafetyHarness` setup; `InterimVerificationLandGitTests.C544_FinalPromotionPublishesOnlyReviewedCandidate` | Attach the reply graph to the landing harness's real repository/database. Produce approval through settlement, then test owner/source admission: V-4, R-5. Existing adoption class retains full Git publication/CAS/lease coverage. |
| All `C544DeliveryRig`; `VerificationRoundDeliveryTests` completion receipt/recovery, snapshot, freeze, late replacement, header, pointer, whole-wire, deadline, single-note, stamp and link cases, plus `AssertReceivedOnceAsync` | Reuse the real queue and nine existing cuts; recipient evidence, not enqueue flags: V-6/V-7/V-8, R-6/R-7. Brief and land-refusal legs are unchanged and excluded. |
| `AgentTaskLandNotificationHostedService`, completion branches of `AgentTaskLandNotificationService`, queue distillation/shrink/freeze/replay, `LandNoteReceipt`; `AgentTaskReplyC544UnifiedCompletionTests` and its containing partial-class attributes | Restart scanning, immutable rendering, complete receipt, legacy/profiled coexistence: V-7/V-8, R-7/R-8. |
| `docs/testing-and-build.md` checkpoint/slot/category/PC rules; `docs/session-runtime-invariants.md` profile-v1 completion contract; `docs/orchestration-loop.md` reviewed recovery and post-land Mutation rules | Exact execution floors, process ownership, closed selection and separate Mutation cost. No runtime deployment is required by TestDesign. |

Test file names below are under `tests/Antiphon.Tests/Application/`. New classes are
`ReviewEvidenceSettlementTests` (11 nonparameterized methods) and
`ReviewEvidenceDeliveryTests` (17 nonparameterized methods). Matrix rows are labelled internal
assertions, not extra TUnit executions. All `C807_` method names below are the implementation
roster; retain them so ordinary checkpoints and post-land PCs select the same cases.

Required fixture setup, resolved before handoff:

- Reuse the cloned PostgreSQL database from `TestDbFixture.CreateIsolatedSchemaAsync`;
  despite its name it creates a database, not a shared `SearchPath` schema. Use fresh contexts
  for persisted assertions. Both new classes launch Git via C544 helpers: mark Integration,
  Slow, `[NotInParallel("MessageQueue")]`, and `[ParallelLimiter<ProcessSpawnLimit>]`, and add
  both fully qualified names with reasons to `slow-tests-allowlist.txt`. Delivery matrices
  and scratch Git/database setup justify the Slow cost marker.
- Allow one optional `Func<Guid, string, string>` report transform on
  `C544DeliveryRig.SettleReviewAsync`, applied after padding and before `SeedTurnAsync`.
  Null preserves every existing call. Transform the evidence block only; keep finding and
  next-stage markers outside it. The delegate report is legitimate seeded input. Only the
  adapter's actual `OnSubmitted` callback may create the positive caller UserPrompt.
- Use `CreateTaskAsync` -> `DispatchAsync` -> `SeedTurnAsync` -> `OnTurnEndAsync` for new
  settlement cases. Explicitly set `Stage: OrchestrationStage.Review` for a follow-up fixture, whose default
  stage can otherwise be FollowUp. Use `FinalReview() with { FollowUpOnTask = subject.Id.ToString(),
  Stage = OrchestrationStage.Review }` with its explicit ReadOnly workspace: a prior task with no agent takes
  the supported fresh-delegate continuation path. Before dispatch, a fixture may prepare an
  old null-profile task as C544 applicability tests do; that historical setup is not evidence
  that the create API can commission new null-profile Reviews.
  Do not change terminal status, outcome, queue state or notification state to simulate progress.
- For adoption, use `LandingSafetyHarness.InitializeAsync` and `C544World.AttachAsync` on
  the same schema/repository. Prepare distinct same-card Code/Worktree owner and source,
  separate branch names, a real extra source commit, and push both to the fixture bare remote.
  The source has `RepairSourceTaskId=null`, no SourceLanding binding, no pending land and an
  eligible settled status. A fresh explicit-card Final Review names that source. Never call
  `AgentTaskLandAdoptionTests.AddReviewAsync` or insert its StageOutcome by hand.
- Existing `VerificationRoundDeliveryTests.AssertReceivedOnceAsync` is internal and reusable.
  For legacy direct notes, assert the direct queue body and complete submitted prompt yourself:
  absence of an outbox snapshot is expected, so the profiled helper is inappropriate.

### Delivery inventory

`W` below means both the exact code `review-evidence-warning=review_evidence_not_standalone`
and corrective prose naming fenced/quoted/indented evidence and requiring bare lines before
the next-stage block. Check these in `NoteHeader`, not only in the raw excerpt. Never require
the entire raw report inside a distilled/polled note: the complete *frozen wire rendering* is
the receipt contract.

| Producer -> destination | Persistence and durable identity | Recovery and receipt | Coverage |
|---|---|---|---|
| Review turn -> settlement/parent-note composition | Terminal task/result, specific Warning, Review StageOutcome and Completed event; profile-v1 TaskCompletion snapshot in the same save. Join task ID, outcome ID, SourceEventId, notification ID, parent session, root ID and raw-result digest. | Precommit cut rolls all back. Postcommit cut keeps one obligation and the original header. Replay settlement only when it did not commit. | V-3, V-7; G-9, G-21..23, G-27, G-31/32 |
| Committed obligation -> session queue (`WhenIdle`) | `SourceLandNotificationId`, `SourceTaskId`, destination, digest, and `task:<root:N>`; one row per obligation. | Boot scanner recovers missing insert, lost insert acknowledgement/link, and lost wakeup using recreated services. Same notification and existing row identity after recovery. | V-6/V-7; G-24..26 |
| Queue -> caller terminal | `CompletionDeliveryJson` freezes logical note/header, wire bytes/hash, member queue IDs, optional spill path/hash with the first attempt/floor. | Raw, distilled, poll-shrunk and spilled warning survive; an existing rendering is replayed. Busy means zero submitted bodies until TurnEnd; eligible means deliver on first permitted flush. | V-6/V-7; G-28..34, G-41 |
| Submitted terminal input -> durable confirmation | Actual complete caller UserPrompt in frozen destination, above attempt sequence floor, associated with the same queue/notification. `ConfirmingPromptSequence` identifies it. | Lost attempt/verdict or confirmation save reconciles existing transcript without another complete submission. Spill also requires existing content with the frozen hash. Prefix/header/ID-only text, wrong session/kind and stale text cannot confirm. | V-7/V-8; G-35..39 |
| Legacy successful Review -> direct parent queue | `SourceTaskId`, root conversation key and raw digest; no profile snapshot or new notification kind. | Same warning/header composition, busy/eligible queue flush and complete UserPrompt receipt. Existing legacy restart semantics are unchanged. | V-8; G-40, R-8 |

The rig substitutes a controlled terminal, model summary, readiness reader and injected
save/transaction failures. It exercises production reply, outbox, scanner, queue and receipt
queries over real PostgreSQL and scratch Git. It does not prove a real provider's terminal
rendering, physical process death, or an LLM noticing the warning. Provider recreation at a
fault cut proves persisted recovery. Do not claim OS-crash or external-provider qualification.
The pure receipt-query counterexamples below do not replace producer-to-recipient tests.

### Proves it works now

| ID | Layer and exact test(s) | Required observations |
|---|---|---|
| V-1 | Unit: `InstructionBundleTests.C807_ShippedReviewExampleParses`; updated `TaskPlatformGuidanceTests.Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes` | Read `InstructionBundles.TextOf(StageReview)` in full; assert each of the three placeholder strings occurs exactly once, substitute only GUID/full uppercase SHA/Full, parse unchanged text. Found/Usable true, exact GUID, normalized SHA, Full, Warning null. Never strip fences/quotes/indentation or extract a synthetic example. Guidance assertions cover original landing owner versus reviewed source, `-Land`, `-FromTask`, `-ExpectedSourceSha`, `-ReviewEvidenceId`, Full meaning and retained platform/routing/checkpoint/delivery obligations. Check the D-3 table in the owner doc as well. |
| V-2 | Unit: `ReviewEvidenceParserTests.C807_IgnoredHeadingMatrix`, `C807_MixedExamplesPreserveStandalone`, `C807_StandaloneErrorPrecedence`, `C807_ReportTokenCutsOffEvidence`, `C807_HeadingPrecision`; update existing C488 ignored-block case | Matrix described below; no accepted grammar expansion. |
| V-3 | Integration: `ReviewEvidenceSettlementTests.C807_IgnoredEvidenceSettlesWithoutApproval`, `C807_StandaloneEvidenceAndScope`, `C807_RepeatedSettlementIsIdempotent`, `C807_UnsuccessfulReviewCannotBind`, `C807_AbsentAndOtherInvalidEvidenceKeepBehavior` | Real reply settlement, fresh DB assertions. Ignored matrix: fence/quote/indent x Clean/Found x fresh/follow-up (18 labelled rows), Final/profile-v1. Succeeded and original finding/handoff persist; specific Warning; SHA/ref/repository null, scope Unknown; subject null for fresh and historical follow-up ID retained for follow-up. No synthetic review-evidence/subject/reviewed-sha header bits. Reentry preserves one outcome/Completed event/diagnostic/obligation and their IDs. Standalone siblings cover Clean/Found x Final/Interim (4 rows): bind coordinates and cap Full to Interim; Found is a baseline, never land approval. Explicit failed and blocked report tokens with otherwise valid evidence cannot bind. No heading retains generic missing-evidence behavior; malformed standalone subject/SHA retain their specific warnings and do not gain W. |
| V-4 | Integration: `ReviewEvidenceSettlementTests.C807_SourceReviewFeedsAdoption`, `C807_WrongOwnerEvidenceRefusesAdoption`, `C807_FollowUpSubjectMustMatch`, `C807_SubjectAuthorizationRequired`, `C807_WorktreeSubjectRequired` | Fresh explicit-card Review binds the separate source GUID, exact pushed SHA, full ref and repository, not its own task nor the original owner. Feed its produced outcome ID into `RequestAsync(owner, AdoptFromTaskId: source, ExpectedSourceSha, ReviewEvidenceId)`; assert queued request's owner/source/evidence/ref/SHA. Wrong-owner evidence is produced by a separate real Review using the same reviewed SHA, then must refuse `review_evidence_subject_mismatch` with zero land requests. A follow-up naming its own subject succeeds; one naming another same-card source retains historical subject, captures no SHA/ref/repo and warns. Cross-card, unbound-card and missing-subject negatives cannot bind. Same-card Shared/ReadOnly subjects cannot bind even though authorized by card. |
| V-5 | Integration: `ReviewEvidenceSettlementTests.C807_WarningPreservesOtherWarnings` | In the ReadOnly fixture, dirty a real tracked file and name its backtick path in a fenced-evidence report. `TryDescribeGitAsync` then produces the existing uncommitted-files warning. After real settlement, both that warning and W are present in the preserved header, outside a deliberately short excerpt. Do not inject a sentinel only into an expected string. |
| V-6 | Integration: `ReviewEvidenceDeliveryTests.C807_WarningReceipt` | 16 labelled rows: busy/eligible x fenced/quoted x raw-inline/distilled-inline/polled-inline/raw-spill. Distillation summary deliberately contains neither evidence nor W; assert the queued body/header immediately after applying it, before flush. Polling uses `AgentTaskService.GetAsync(...pollingSessionId...)` and requires a NoteShrunk event plus a genuinely polled rendering containing `Report withheld`, not a raw fallback. For each row invoke the shared receipt assertion with W plus Final/Unknown/original-next header bits, and verify the actual submitted wire body; check no synthetic approval fields. Spilled content must contain W and match the frozen hash. |
| V-7 | Integration: nine `ReviewEvidenceDeliveryTests.C807_Recovery*` methods in the table below; `C807_SnapshotWarningSurvivesResultRewrite`, `C807_WarningReplayRejectsLateDistillation`, `C807_DistillationDeadlineIsNotRenewed` | Every cut reaches its hook, then fresh providers recover the same obligation to complete receipt. Extra cases distinguish immutable snapshot from mutable task result, immutable attempted rendering from late summary, and original hold deadline from a new distiller request. |
| V-8 | Integration: `ReviewEvidenceDeliveryTests.C807_HeaderOnlyPromptIsNotReceipt`, `C807_SpillWarningRequiresMatchingFile`, `C807_ReceiptQueryRequiresDestinationKindAndFloor`, `C807_LegacyWarningReceipt` | Header/prefix/ID-only callback transforms cannot confirm. Header/prefix Truncated rows stay parked after restart; ID-only can recover to exactly one complete prompt. For spill, delete or change the actual file after submission, require unconfirmed/content-mismatch, restore exact bytes and confirm from the existing prompt without another submission. Query controls invoke real `LandNoteReceipt.Prompts` over constructed candidate entries: wrong session, AssistantText/QueuedUserPrompt, at/below sequence floor, missing floor/start, and outside timestamp tolerance all refuse; complete UserPrompt above floor succeeds. These constructed entries are query fixtures, never inserted as positive caller receipts. Legacy null-profile Clean Review, busy/eligible x fenced/quoted (4 rows), gets W in direct NoteHeader and one matching complete UserPrompt. |

Parser matrix (internal labelled rows, each run with LF and CRLF):

- Fences: triple backticks, language-labelled backticks, four-backtick forms already recognized
  by the scanner, indented opening fences, and an unclosed backtick fence. Quote forms: `>`,
  `> `, `>>`, `> >`, including whitespace before nested quote markers. Indentation: one space,
  four spaces and tab. Include case-insensitive heading and trailing horizontal whitespace.
  Every ignored-only row yields Found=true, Usable=false, subject/SHA null, scope Unknown and
  exactly `review_evidence_not_standalone`, even with otherwise valid GUID/SHA/Full fields.
- Mixed ignored examples before and after one standalone block use different GUIDs/SHAs/scopes;
  only the actual block wins, with no warning. Delimit its fields normally before the later
  example so this does not invent a new field-termination grammar. With two actual blocks the
  result remains duplicate. With one invalid subject, invalid SHA or after-next-stage block,
  the existing specific error wins over ignored examples.
- Heading-shaped evidence after the existing closing-token cutoff produces neither authority
  nor warning. Also put ignored evidence before and valid evidence after that cutoff: only
  the pre-token diagnostic remains. Preserve the existing last-closing-token rule.
- Null/empty/whitespace/prose-only/inline-code mentions, heading substrings with extra text and
  a Markdown sentence quoting the marker without a whole heading are no-heading, no-warning.
  Existing 40/64-character SHA, invalid SHA and Full/Interim/None/Unknown/duplicate-scope tests
  remain in Unit. Tilde fences/CommonMark expansion are outside D-2.

Recovery cases all settle a fenced Clean report; quoted warning production is separately
crossed in V-3/V-6. Each method runs busy/eligible x raw-inline/distilled-spill (4 labelled
rows), hence 36 cut scenarios but **9** TUnit executions. Reuse C544Recovery ordering:
apply a summary only when the immediate admission exists; restart never commissions it.

| Exact `ReviewEvidenceDeliveryTests` method | Existing cut | Assertions at the cut, before eventual complete receipt |
|---|---|---|
| `C807_RecoveryBeforeSettlementCommit` | `obligation-insert` | Throws=1; task still Dispatched; zero outcome, Completed event, diagnostic and obligation. Restart, reenter `OnTurnEndAsync`, then assert all five commit once. |
| `C807_RecoveryAfterSettlementCommit` | `settled-committed` | Throws=1; terminal task, Warning, outcome and snapshot all committed once; no queue row yet. Capture original notification ID/header/digest. |
| `C807_RecoveryBeforeEnqueue` | `note-insert` | Throws=1; obligation survives and no queue row commits. Scanner must enqueue it after restart. |
| `C807_RecoveryAfterEnqueue` | `note-committed` | Throws=1; capture committed keyed queue ID even if link acknowledgement is lost. Recovery links/reuses it; no second row or complete prompt. |
| `C807_RecoveryLostWakeup` | `DropCompletionWakeup=true` | Flag consumed and one pending row, zero writes. Recreate provider, boot scan and invoke the production stranded/idle flush; eventual receipt cannot depend on the lost in-memory wakeup. |
| `C807_RecoveryBeforeRenderingCommit` | `spill-written` | Throws=1; attempt/render transaction rolled back, no frozen rendering and no submitted bytes. Spilled variant exercises a written file before rollback. |
| `C807_RecoveryAfterRenderingCommit` | `render-committed` | Throws=1; frozen rendering plus attempt/floor committed, no submitted bytes. Restart replays the same JSON/wire/hash/member IDs; one complete prompt. |
| `C807_RecoveryAfterAttemptCommit` | `attempt-committed` | Throws=1 after real submission but before the Delivered verdict save. Save the actual prompt identity; recovery confirms it without a second complete prompt. |
| `C807_RecoveryBeforeReceiptCommit` | `prompt-accepted` | Throws=1 while confirmation save fails. Existing complete prompt survives; restart records its same sequence and never retypes it. |

All successful recovery rows call `AssertReceivedOnceAsync` with W and compare notification ID,
SourceEventId, StageOutcomeId, digest, parent/root, queue ID (when previously committed), and
frozen rendering (when previously committed). Query fresh DB state before asserting receipt.
For `C807_SnapshotWarningSurvivesResultRewrite`, lose the immediate path at settled-committed,
then rewrite only the mutable task Result to standalone-looking evidence in a foreign context.
The existing snapshot/raw digest/header must remain byte-identical. After Scan and before
Flush, the recovered queue's NoteHeader/body must equal the original snapshot header/raw body;
this catches mutable-result reconstruction even if the later header fallback would repair it.
The Warning/outcome must remain unapproved, and the actual caller must receive W, not the rewritten text. This is an
adversarial input change, not manufactured delivery progress.

For `C807_WarningReplayRejectsLateDistillation`, hold a distillation request until the original
deadline, cut after a raw warning rendering is submitted, then submit that late summary through
`TryApplyDistillationAsync`. It must report `delivery-claimed`, preserve queue body and delivery
JSON, and reconcile the existing whole prompt after restart. For
`C807_DistillationDeadlineIsNotRenewed`, restart while held: same deadline, no new distiller
admission, no early submission, then raw warning receipt after advancing the fixture clock.

### Guards the regression

| ID | Exact coverage and decisive assertion |
|---|---|
| R-1 | V-1 plus the entire Unit lane, especially both existing bundle size tests (2,480 trimmed/2,500 embedded), ASCII, role composition, platform and checkpoint/delivery guidance. A regression to the shipped fenced example makes V-1 unusable; raising size limits is forbidden. |
| R-2 | V-2 plus all existing `ReviewEvidenceParserTests`: ignored headings cannot approve, mixed examples cannot poison actual evidence, and existing SHA/scope grammar remains. |
| R-3 | V-3/V-4 plus all 16 executions of `AgentTaskReviewEvidenceTests`: new real-settlement assertions carry the proof; old manual-outcome/parser tests guard existing APIs and immutable coordinates. |
| R-4 | V-5/V-6: warning is corrective metadata, not a new failure/status/stage or synthetic approval. Existing warning remains and original finding/next/handoff are unchanged. |
| R-5 | V-4 plus the entire `AgentTaskLandAdoptionTests` class (9 methods, 15 argument-expanded executions): original owner status, source eligibility, pinned tips, CAS/remote lease, refusal before publication and preservation of the adopted source worktree. No production land boundary is relaxed. |
| R-6 | V-6/V-7/V-8: same identity across recovery, W in preserved header/content, complete caller input and spill hash. New helper argument must be optional and preserve old calls. |
| R-7 | Existing `VerificationRoundDeliveryTests.C544_CompletionReceipt`, `C544_CompletionRecovery`, `C544_SnapshotRendersRecovery`, `C544_RenderingKeepsHeader`, `C544_CompletionReceiptWholeWire` (5 executions). These exercise default C544 report input after the helper extension and keep valid-review completion behavior. |
| R-8 | `AgentTaskReplyIntegrationTests.C544_shared_commit_on_settle_mints_one_task_completion` (2 argument-expanded executions) and V-8 legacy case: exactly one logical completion, existing Shared/profiled versus legacy behavior preserved. |

### Guard inventory

Scope is the evidence-authority and warning-delivery assertions changed or relied on here.
Each listed guard has its own PC even when several use the same test method; execute mutations
separately. The unchanged land protocol's full destructive-operation guard inventory belongs
to CARD-0753, not a duplicate PC battery in CARD-0807; R-5 reruns that ordinary boundary.

| Guard | Plan reference and independently bypassable invariant | Control |
|---|---|---|
| G-1 | D-1: the embedded example itself is parseable | PC-1 |
| G-2 | D-2: fenced heading cannot authorize evidence | PC-2 |
| G-3 | D-2: quoted heading cannot authorize evidence | PC-3 |
| G-4 | D-2: indented heading cannot authorize evidence | PC-4 |
| G-5 | D-2: ignored examples cannot poison one real block | PC-5 |
| G-6 | D-2: duplicate real blocks refuse | PC-6 |
| G-7 | D-2: evidence after next-stage refuses | PC-7 |
| G-8 | D-2: the closing-token cutoff limits authority/diagnostics | PC-8 |
| G-9 | D-2/D-4: Found alone cannot bind settlement coordinates | PC-9 |
| G-10 | D-3: explicit follow-up subject must match | PC-10 |
| G-11 | D-3: subject authorization remains necessary | PC-11 |
| G-12 | D-3: authorized subject must still be Worktree | PC-12 |
| G-13 | D-3: capture named source GUID, not review GUID | PC-13 |
| G-14 | D-3: capture the actual reviewed full SHA | PC-14 |
| G-15 | D-3: capture the subject's full source ref | PC-15 |
| G-16 | D-3: capture the subject's repository | PC-16 |
| G-17 | D-3: adoption validates evidence against adopted source | PC-17 |
| G-18 | D-3: original-owner evidence cannot approve another source | PC-18 |
| G-19 | D-4: commissioned Interim cannot mint Full | PC-19 |
| G-20 | D-4: unsuccessful Review cannot bind otherwise valid evidence | PC-20 |
| G-21 | D-4: invalid evidence cannot manufacture approval header facts | PC-21 |
| G-22 | D-4: one settlement/reentry yields one outcome/obligation | PC-22 |
| G-23 | D-4: terminal state/outcome/Warning and completion commit together | PC-23 |
| G-24 | D-4: failed enqueue retains recoverable obligation | PC-24 |
| G-25 | D-4: lost enqueue acknowledgement reuses committed keyed row | PC-25 |
| G-26 | D-4: boot scan repairs a lost wakeup | PC-26 |
| G-27 | D-4: recovery takes W from frozen snapshot, not mutable Result | PC-27 |
| G-28 | D-4: rendering and first-attempt claim commit before typing | PC-28 |
| G-29 | D-4: replay uses the committed wire bytes | PC-29 |
| G-30 | D-4: late distillation cannot replace attempted rendering | PC-30 |
| G-31 | D-4: W is in the preserved raw completion header | PC-31 |
| G-32 | D-4: adding W preserves an existing caller warning | PC-32 |
| G-33 | D-4: distilled output retains exact warning header | PC-33 |
| G-34 | D-4: poll shrink retains exact warning header | PC-34 |
| G-35 | D-4: complete UserPrompt, not a header/ID/prefix, confirms | PC-35 |
| G-36 | D-4: spilled receipt requires matching file content | PC-36 |
| G-37 | D-4: receipt is above the attempt floor | PC-37 |
| G-38 | D-4: receipt is in the frozen destination session | PC-38 |
| G-39 | D-4: TaskCompletion receipt is UserPrompt, not housekeeping/queued input | PC-39 |
| G-40 | D-4: legacy direct completion also carries W | PC-40 |
| G-41 | D-4: restart retains the original distillation deadline | PC-41 |

### Positive controls

Code runs V/R only. Mutation runs these **after land** on the exact SourceLanding snapshot,
with baseline -> compiling defect -> intended assertion red -> exact byte restore -> green.
No test/harness assertion is weakened. These are planned controls, not executed evidence.
Use the exact method filter `/*/*/<Class>/<Method>` for each row below; classes are written
as `IB` = `InstructionBundleTests`, `P` = `ReviewEvidenceParserTests`,
`S` = `ReviewEvidenceSettlementTests`, `D` = `ReviewEvidenceDeliveryTests` solely to keep
the table readable. Expansion is literal, not a wildcard class or whole-class run.
Each selected method contributes one execution, regardless of internal matrix rows.

| PC | Compiling defect, isolated to its named guard | Exact method to run; intended red assertion |
|---|---|---|
| PC-1 | Rewrap only the shipped four-line evidence example in triple backticks. | IB.`C807_ShippedReviewExampleParses`: Usable must be true. |
| PC-2 | In the parser's fence branch, treat an exact in-fence heading as a standalone heading. Leave quote/indent checks intact. | P.`C807_IgnoredHeadingMatrix`: fenced Usable must be false and coordinates null. |
| PC-3 | Promote a quote-prefixed heading to the standalone-offset collection instead of ignored; leave its fields untouched. | P.`C807_IgnoredHeadingMatrix`: quote warning must be `review_evidence_not_standalone`, not a standalone subject error. |
| PC-4 | Promote an indented heading to the standalone-offset collection; leave its fields untouched. | P.`C807_IgnoredHeadingMatrix`: indented warning must be the ignored diagnostic. |
| PC-5 | Return the ignored diagnostic whenever any ignored heading exists, even with one actual heading. | P.`C807_MixedExamplesPreserveStandalone`: actual GUID/SHA/Full and Usable=true. |
| PC-6 | Remove the `headings.Count > 1` duplicate return, allowing the first block. | P.`C807_StandaloneErrorPrecedence`: two-real-blocks row requires duplicate/refusal. |
| PC-7 | Remove the after-next-stage return. | P.`C807_StandaloneErrorPrecedence`: placement row requires after-next-stage/refusal. |
| PC-8 | Scan `normalized` instead of `TextBeforeClosingReportToken(normalized)`. | P.`C807_ReportTokenCutsOffEvidence`: post-token-only has no Found/warning; pre-token ignored cannot become usable. |
| PC-9 | Add an ignored-diagnostic branch in settlement assigning `reviewedSha = new string('a', 40)` even though Usable=false. | S.`C807_IgnoredEvidenceSettlesWithoutApproval`: persisted SHA must be null. |
| PC-10 | Disable only the `follow != named` rejection, preserving ordinary authorization. | S.`C807_FollowUpSubjectMustMatch`: mismatched same-card follow-up captures no SHA/ref/repo. |
| PC-11 | Make `ReviewSubjectAuthorized` return true. | S.`C807_SubjectAuthorizationRequired`: foreign/unbound-card source must not bind. |
| PC-12 | Remove only `subject.Workspace != WorkspaceMode.Worktree` from settlement rejection. | S.`C807_WorktreeSubjectRequired`: same-card Shared source has null bound SHA. |
| PC-13 | Assign `subjectId = task.Id` instead of `subject.Id` in the authorized bind branch. | S.`C807_SourceReviewFeedsAdoption`: persisted SubjectTaskId equals the source GUID. |
| PC-14 | Assign a different valid full SHA in that branch, e.g. forty `f` characters; fixture source SHA must differ. | S.`C807_SourceReviewFeedsAdoption`: persisted SHA equals independently read pushed source tip. |
| PC-15 | Assign `reviewedRef = "refs/heads/master"` in that branch. | S.`C807_SourceReviewFeedsAdoption`: persisted full ref equals the distinct source branch. |
| PC-16 | Assign `reviewedRepo = subject.WorktreePath` instead of `subject.RepoPath` (fixture has distinct paths). | S.`C807_SourceReviewFeedsAdoption`: repository equals canonical fixture repository. |
| PC-17 | Pass `task` instead of `source` to `LoadRecoveryEvidenceAsync` at adoption admission. | S.`C807_SourceReviewFeedsAdoption`: real produced source outcome must queue adoption, not throw subject mismatch. |
| PC-18 | Remove only the `row.SubjectTaskId != subject.Id` rejection in `LoadUsableEvidenceAsync`. | S.`C807_WrongOwnerEvidenceRefusesAdoption`: exact error must be subject mismatch (later ref mismatch does not pass this assertion). |
| PC-19 | Make `CapToRound` return the declaration unchanged. | S.`C807_StandaloneEvidenceAndScope`: Interim Full claim persists Interim, not Full. |
| PC-20 | Remove only `task.Status == Succeeded` from settlement binding admission. | S.`C807_UnsuccessfulReviewCannotBind`: failed valid report persists null SHA/Unknown scope. |
| PC-21 | In the local-outcome header branch, allow null SHA and construct `ReviewEvidenceFacts(localOutcome.Id, task.Id, localOutcome.ReviewedSourceSha ?? "")`. Keep the ordinary null-outcome check. | S.`C807_IgnoredEvidenceSettlesWithoutApproval`: snapshot header has no `review-evidence=`, `subject=` or `reviewed-sha=` approval bits. |
| PC-22 | Add a second delegate Review StageOutcome with a fresh ID during the existing outcome producer. | S.`C807_RepeatedSettlementIsIdempotent`: outcome count remains one, with stable ID after reentry. |
| PC-23 | Save the settlement tracker immediately before adding the completion obligation. | D.`C807_RecoveryBeforeSettlementCommit`: obligation-insert failure must leave task Dispatched and zero outcomes/Warnings/Completed events. |
| PC-24 | In `ReconcileAsync`, return for an unlinked TaskCompletion whose `EnqueueAttempts > 0`. | D.`C807_RecoveryBeforeEnqueue`: scanner must recover the failed insert to complete confirmed receipt. |
| PC-25 | In `ReconcileAsync` existing-key branch, return before adopting the existing row/link. | D.`C807_RecoveryAfterEnqueue`: same keyed row must be linked and confirmed after the lost acknowledgement. |
| PC-26 | Exclude TaskCompletion from the hosted scanner's pending-notification query. | D.`C807_RecoveryLostWakeup`: scanner must confirm the actual whole prompt after recovery. |
| PC-27 | When building the profiled recovery enqueue body, substitute current `AgentTasks.Result` for the snapshot body and use its first line as NoteHeader. | D.`C807_SnapshotWarningSurvivesResultRewrite`: after Scan, before Flush, recovered queue header/body must equal original snapshot; final receipt must also retain W. |
| PC-28 | Move the `FreezeCompletionRenderingAsync` call outside and immediately after the first-claim transaction commit. | D.`C807_RecoveryAfterRenderingCommit`: at-cut committed rendering must be nonnull alongside the claim. |
| PC-29 | When `committedWire` exists, append a literal suffix to the body passed to delivery, leaving stored rendering unchanged. | D.`C807_RecoveryAfterRenderingCommit`: submitted body must equal frozen WireText exactly. |
| PC-30 | In `TryApplyDistillationAsync`, replace the already-claimed rejection with assigning `note.Body = distilled`, saving, committing the existing transaction and returning null. | D.`C807_WarningReplayRejectsLateDistillation`: late request must refuse `delivery-claimed` and preserve attempted row bytes. |
| PC-31 | Omit the new diagnostic append in `BuildParentNoteAsync`. | D.`C807_WarningReceipt`: W must exist in snapshot.NoteHeader and complete delivered logical note. |
| PC-32 | Replace existing `warning` with W instead of appending. | S.`C807_WarningPreservesOtherWarnings`: uncommitted-files warning must coexist with W. |
| PC-33 | Pass empty header to `BuildDistilledNoteBody` in the queue's distillation application. | D.`C807_WarningReceipt`: immediately after applying summary, queued NoteHeader/body must both retain W; assert before raw-header fallback can mask the defect. |
| PC-34 | Pass empty header to `BuildPolledNoteBody` in poll shrinking. | D.`C807_WarningReceipt`: actual complete receipt must use a polled rendering with W and `Report withheld`; repeated raw fallback or missing receipt fails. |
| PC-35 | Remove only `PromptSubmissionMatch.IsCompleteIn` from `LandNoteReceipt.IsReceipt`. | D.`C807_HeaderOnlyPromptIsNotReceipt`: header/prefix callback cannot set Confirmed or ConfirmingPromptSequence. |
| PC-36 | Disable the spill-file hash rejection in notification reconciliation. | D.`C807_SpillWarningRequiresMatchingFile`: tampered/missing file must keep notification unconfirmed. |
| PC-37 | Change sequence floor comparison in `LandNoteReceipt.Prompts` from `>` to `>=`. | D.`C807_ReceiptQueryRequiresDestinationKindAndFloor`: complete prompt exactly at floor is excluded. |
| PC-38 | Remove destination-session predicate from `LandNoteReceipt.Prompts`. | D.`C807_ReceiptQueryRequiresDestinationKindAndFloor`: complete foreign-session prompt is excluded. |
| PC-39 | Permit QueuedUserPrompt for TaskCompletion in `AcceptsQueuedPrompt`. | D.`C807_ReceiptQueryRequiresDestinationKindAndFloor`: completion queued-input candidate is excluded. |
| PC-40 | Gate the new warning append on `VerificationProfileVersion != null`. | D.`C807_LegacyWarningReceipt`: null-profile direct header and actual complete caller prompt must contain W. |
| PC-41 | Set the recovery enqueue's distillation hold to `now.AddSeconds(120)` instead of snapshot.DistillDeadlineAt. | D.`C807_DistillationDeadlineIsNotRenewed`: after a settled-committed cut and clock advance, recovered hold equals original snapshot deadline, not restart time. |

For PC-27/33, assert the intermediate recovered/distilled queue body before flush; for PC-34,
require a polled rendering as well as complete receipt. Production's raw-header fallback must
not make those controls falsely green. For PC-41,
the deadline test includes an unlinked post-settlement cut as well as its already-enqueued
case, so it actually reaches the recovery `holdUntil` assignment. For PC-28, move (not copy)
the freeze call; a build failure or an unreached fault is not the intended assertion red.
For PC-21 use a pattern that keeps `localOutcome` definitely assigned; no new evidence class
or product API is required. Each remaining mutation names an existing statement/branch or the
new D-4 append, and has a deterministic contrasting fixture value.

Guard audit: **guards=41, mapped=41, missing=0, duplicate PC maps=0**. Guard-to-PC mapping is
1:1; reuse of a test method is deliberate and does not combine independently bypassable gates.

### Out of scope

- No historical StageOutcome repair/backfill and no automatic fresh reviewer dispatch (D-5).
  S4's authoritative CARD-0788 correction was already read back in Plan; do not repeat the write
  or edit generated card files. The caller retains the unrelated timeout/unpublished-work items.
- No new parser dialect, subject inference from StartRef, relaxed follow-up/landing authorization,
  schema/API/notification kind, broker/channel route, terminal protocol or provider launch.
- No full `AgentTaskReplyIntegrationTests`, full Antiphon assembly, client/E2E build, production
  runner, real model or Windows-only lane. Selected tests have no OS-only requirement. Unit and
  the named integrations are mandatory; known inherited reds are reported/baselined, not skipped.
- Physical kill/crash qualification and remote-runner spill transport are unchanged. The C544
  injected persistence cuts and recreated providers establish the server recovery claim here.

### Cost

All figures are estimates, not measurements; queueing for a host build slot is extra elapsed
time and never authority to bypass it. The closed ordinary Code floor is **60 minutes**:
one isolated build/setup budget of 4 minutes plus 56 minutes for Unit and the exact integration
filters in CP-1..7. Review repeats the same 60-minute selection. Code authoring is budgeted
at 120 additional minutes, so Code dispatch `ExpectAbout` is **180 minutes**.

Post-land Mutation has **41 separate method-scoped controls**. Budget per control includes
baseline, defect edit, isolated red build/run, restoration and isolated green build/run:

| Controls / literal method filters specified above | Count | Minutes each | Minutes |
|---|---:|---:|---:|
| PC-1..8 (IB/P Unit methods) | 8 | 4 | 32 |
| PC-9..22 and PC-32 (S settlement/adoption methods) | 15 | 6 | 90 |
| PC-23..31, PC-33..41 (D receipt/recovery methods) | 18 | 9 | 162 |
| SourceLanding setup, discovery, external evidence/restoration report | 1 | 10 | 10 |
| Mutation floor | 41 controls | | **294** |

Ordinary V/R plus Mutation floors = **354 minutes**; including Code authoring and separate
ordinary Review = **534 minutes**. Mutation can batch only demonstrably independent controls
under the existing policy; no such savings are assumed. Reusing CP-1's build for six subsequent
rows saves an estimated **24 build minutes** versus seven isolated builds (4 minutes each).
Scoped integrations replace the broad reply/full-assembly reruns; no unmeasured runtime saving
is claimed. The large Mutation floor is intentional: independent authorization and persistent
delivery gates cannot be represented by one parser mutation.

### Checkpoints

One committed implementation group, S1-S3; no earlier test/build boundary is introduced.
Every row is serial across test processes so MessageQueue exclusion and the process limiter
are not mistaken for cross-process locks. CP-1 builds `tests/Antiphon.Tests` once; CP-2..7
reuse exactly that output. New classes/methods must be present in the fresh executed roster.

Execution floors are source-derived: Unit has at least 59 existing InstructionBundle executions,
14 parser executions, 8 platform-guidance-file executions, plus 1 new bundle and 5 new parser
methods = **87 known executions**. This is a conservative lower bound, not a prediction of
the full Unit lane total; its filter still runs all Unit tests. Require the named Unit methods
in the roster as well as all new cases. CP-2 = 11 new + 16 existing; CP-3 = 9 cut methods;
CP-4 = 8 remaining new delivery methods; CP-5 = 15 expanded adoption; CP-6 = 2 profile arguments;
CP-7 = 5 existing completion methods. Internal scenario/assertion counts never increase Min.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c807/` | unit | `/*/*/*/*[Category=Unit]` | V-1, V-2, R-1, R-2 | all Unit, named bundle/parser/platform guards present, >= 87 executed, 0 failed | 87 | 12 | true |
| CP-2 | S1-S3 | CP-1 | settlement | `/*/*/(ReviewEvidenceSettlementTests*)\|(AgentTaskReviewEvidenceTests*)/*` | V-3, V-4, V-5, R-3, R-4 | all 27 listed executions, 0 failed/skipped | 27 | 8 | true |
| CP-3 | S1-S3 | CP-1 | warning-recovery | `/*/*/ReviewEvidenceDeliveryTests/C807_Recovery*` | V-7, R-6 | all 9 cut methods, 0 failed/skipped; 36 labelled cut scenarios | 9 | 12 | true |
| CP-4 | S1-S3 | CP-1 | warning-receipt | `/*/*/ReviewEvidenceDeliveryTests/(C807_WarningReceipt*)\|(C807_SnapshotWarningSurvivesResultRewrite*)\|(C807_WarningReplayRejectsLateDistillation*)\|(C807_DistillationDeadlineIsNotRenewed*)\|(C807_HeaderOnlyPromptIsNotReceipt*)\|(C807_SpillWarningRequiresMatchingFile*)\|(C807_ReceiptQueryRequiresDestinationKindAndFloor*)\|(C807_LegacyWarningReceipt*)` | V-6, V-7, V-8, R-4, R-6, R-8 | all 8 listed methods, 0 failed/skipped | 8 | 8 | true |
| CP-5 | S1-S3 | CP-1 | adoption | `/*/*/AgentTaskLandAdoptionTests/*` | R-5 | all 15 argument-expanded executions, 0 failed/skipped | 15 | 7 | true |
| CP-6 | S1-S3 | CP-1 | unified-completion | `/*/*/AgentTaskReplyIntegrationTests/C544_shared_commit_on_settle_mints_one_task_completion*` | R-8 | both profiled arguments, 0 failed/skipped | 2 | 3 | true |
| CP-7 | S1-S3 | CP-1 | existing-completion | `/*/*/VerificationRoundDeliveryTests/(C544_CompletionReceipt*)\|(C544_CompletionRecovery*)\|(C544_SnapshotRendersRecovery*)\|(C544_RenderingKeepsHeader*)` | R-7 | all 5 named methods (Receipt prefix includes ReceiptWholeWire), 0 failed/skipped | 5 | 10 | true |

Use `dotnet run --project tools/Antiphon.Checkpoints -- run --plan
docs/superpowers/plans/2026-09-29-card-0807-review-evidence-fence-plan.md --after S1-S3`.
Bootstrap the tool through `scripts/build-slot.ps1`; row drivers take their own slots.
Continue `wait <run-id>` until exit is not 75 and report every CP-n with actual counts/TRX
and rerun count. Use the tool's supported no-build invocation after bootstrapping; do not
silently add a second test-project build. The manifest's Serial column is authoritative.
Check each exact method roster, including both argument rows for CP-6. Any additional
build/test, baseline investigation of inherited reds, or changed manifest requires a stated
reason. No builds, TUnit runs or PCs were performed in this TestDesign dispatch.
