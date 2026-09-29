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
