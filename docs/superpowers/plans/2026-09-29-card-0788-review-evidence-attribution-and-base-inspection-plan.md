# CARD-0788: make review-evidence misattribution visible at settlement and land, and keep same-card base inspection inside its budget

Date: 2026-09-29. Stage: Plan with separate TestDesign completed below.
Planning baseline: `15b66136a6d2081754935bfdc88139e761ed4e57` (origin/master at planning time).
TestDesign baseline: plan commit `f23a0bde93300b33c1ba341fc7acb04c6ea75071`.

## Outcome and scope

CARD-0807 corrected this card's root cause: the Review bundle tells reviewers to name the
original landing owner, so a `-StartRef` repair review names the wrong subject and the server
records exactly what it was told. CARD-0807 owns the bundle, the parser diagnostic, the
`review_evidence_not_standalone` header warning and the "Reviewed owner recovery" /
"Repair source" paragraphs of `docs/orchestration-loop.md`. Nothing in this plan changes
evidence identity, infers a subject from a SHA, or rewrites settled approval rows.

What remains for CARD-0788 is that the server had every fact needed to flag the CARD-0417
misattribution at settlement and at land, and said nothing useful in either place; and that
the two "separate open investigations" on the card (branch-inspection timeouts, claimed but
unpushed commits) now have a code-level explanation and a bounded fix each. Five slices:

| Slice | Defect | Files |
|---|---|---|
| S1 | Land refusals for review evidence name neither identity nor the fitting command; for `-FromTask` the message even says "landing owner" when the checked identity is the source | `LandApproval.cs`, `AgentTaskLandService.cs`, `AgentTaskLandSourceResolver.cs`, `docs/ops-http.md` |
| S2 | Review settlement binds evidence whose SHA contradicts the review's own checkout base or the subject's last confirmed pushed tip, with no warning | `AgentTaskReplyService.RecordDelegateStageOutcomeAsync` (after CARD-0807 lands), new `ReviewEvidenceConsistencyTests.cs` |
| S3 | Same-card base inspection pays 3-8 git commands per candidate before it applies any status filter, inspects every same-card branch including read-only Review branches, and runs the 5-command checkout-safety probe on candidates that can never win; the 2 s budget then expires at 3-5 candidates | `AgentTaskWorktreeBaseResolver.cs`, `GitSettings.cs`, `docs/ops-http.md` |
| S4 | An incomplete Auto-mode inspection at dispatch silently cuts the worktree at the target branch, which put a CARD-0417 Review on master | `AgentTaskDispatcher.cs`, `AgentTaskWorktreeBaseResolver.cs`, `AgentTaskDispatchBaseGuardTests.cs` |
| S5 | A non-Code Worktree task that settles with no pushed progress gets no caller-visible warning, so a Plan report claiming a commit that never left the runner reads as success | `TaskCompletionProgressService.cs`, one call site in `AgentTaskReplyService.cs`, `RunnerTaskSettlementTests.cs`, `TaskCompletionContinuationTests.cs` |

This plan ran no builds or tests and claims no executed positive controls.

## Ground truth

The card, CARD-0807's plan, CARD-0603, CARD-0675 and CARD-0753's plan were read in full. Live
task and transcript records were read from `GET /api/agent-tasks/{id}` and
`GET /api/sessions/{id}/transcript` on the desktop server; they are cited by short id.

| The card assumes | What the code and the durable records say | Implication |
|---|---|---|
| Explicit `-Card` plus `-StartRef` makes the server pick the card's canonical owner as evidence subject. | `AgentTaskReplyService.RecordDelegateStageOutcomeAsync` reads `subjectTaskId` from the raw report, loads that task and copies its `WorktreeBranch`/`RepoPath`. Review `ec59b256` (StartRef `8d5050ea`) itself wrote `subjectTaskId: ad020e8c` after listing both branch tips with `ls-remote` (`a12de733` for ad020e8c, `8d5050ea` for fb8fa4a9); its brief had said "Review of the CARD-0417 repair on feat/card-task-fb8fa4a9". | Superseded, as the card's correction already says. CARD-0807 fixes the instruction. This plan adds no binding rule. |
| An internally inconsistent record (right SHA, wrong subject/ref) is undetectable. | At settlement the server held: the review's own `WorktreeBaseSha = 8d5050ea` (`worktreeBaseSource: Explicit`), and the named subject's settlement-confirmed remote tip `progressEvidence.remoteSync.confirmedSha = a12de733` (ad020e8c; fb8fa4a9's is `8d5050ea`, 73423389's is `7fb7bc5c`). `RecordDelegateStageOutcomeAsync` compares neither. | S2: two DB-only consistency checks that warn and still bind. |
| `-FromTask` and `-RecoverReviewedSource` are mutually exclusive dead ends for this record. | They were, correctly: `LandApproval.LoadUsableEvidenceAsync` requires `row.SubjectTaskId == subject.Id` where `subject` is the adoption source for `-FromTask` and the owner otherwise. The refusal message is the same fixed sentence, "Review evidence subject is not this landing owner.", in both modes and names no task, ref or SHA. `delegate.ps1 -Land` prints exactly that `ErrorDetails.Message`. | S1: name both identities and the command shape that would fit; keep the codes. |
| Superseded or failed dispatch attempts stay in the inspection candidate pool and make it slower each time. | `AgentTaskWorktreeBaseResolver.ResolveAsync` takes every same-card Worktree task with a `WorktreeBranch` minus those with a `Landed` event; no status or role filter. `WorktreeBranch` is assigned at worktree creation (`DelegationWorktreeService.CreateForTaskAsync`), so every dispatched task of any role joins the pool for the card's lifetime. Per candidate, before the status check at all: `rev-parse` (1), checkout-safety `worktree list` (once) + `status --porcelain --untracked-files=all` + 3 `rev-parse --git-path` when a checkout survives (up to 4), containment `merge-base --is-ancestor` (+ `rev-list` + `cherry` when not an ancestor) (1-3). Board data: CARD-0417 has 15 such rows (9 Succeeded Reviews, 1 Failed Review, Plan, TestDesign, 3 Code); CARD-0649 has 12 (3 Failed Code, 1 Failed Review). Review `8532af35`'s create preview: `TotalCandidates 7, Inspected 3, GitCommands 10, reason inspection_timeout` at the 2 s default (`GitSettings.WorktreeBaseInspectionTimeoutSeconds`), i.e. at least 200 ms per git command on the desktop. | Confirmed, with a correction: Failed rows are 1-3 of the pool; most of the growth is Succeeded Review/Plan branches that carry no new commits. Status pruning alone would not have saved CARD-0417. S3 reorders and batches. |
| A timed-out default-mode dispatch "fails" with `inspection_timeout`. | Create refuses only `Ambiguous`, or `Unknown` with explicit `-BaseTask` (`worktree_base_source_invalid`). At dispatch, `AgentTaskDispatcher` blocks only on `Ambiguous` or `Unknown` in Task mode; Auto-mode `Unknown` falls through to `CreateForTaskAsync(cardBase: null)` and cuts the branch at the target with `worktree-base-inspection-n` warning intents. `T0442_V30` pins this as "keeps safe base". Review `8532af35` (CARD-0417's first review) shows `worktreeBaseSource: DefaultBranch`, `worktreeBaseRef: master`, `worktreeBaseSha: cd133de9` after that timeout. | The refusals the card saw were the explicit `-BaseTask` path; the Auto path silently reviewed master. S4 narrows CARD-0442's choice: a previewed source or a Review role blocks instead. |
| `-StartRef` dispatches share some unreliable push infrastructure. | All three unpushed cases were Codex sessions on server2. `c03af147` and `be4f9e5f` (Code) settled Failed `CompletedWithoutProgress` with `runner_no_pushed_progress`, which is correct. `7647cb6a` (Plan) settled Succeeded with `progressEvidence.assessment = NoAttributedProgress, reason = runner_no_pushed_progress`, branch still at `c599994f`; its report said "committed ... as `04de0435`" in prose, which `TaskCompletionProgressService.ParseClaim` does not read (it only reads `[antiphon-progress:<task> <sha>]` lines), so `ClaimWarning` is null and `ProgressWarning` returns null for `NoAttributedProgress`. The only trace is a `Completed` event "Alternate or unavailable progress does not authorize merge-back; branch ... left for review." | Not a `-StartRef` defect; the server observed the truth every time. S5 makes the non-Code case caller-visible. Why Codex sessions do not push is a delegate/bundle matter outside this card. |
| `reviewedSourceRef` pointing at the owner's branch is a second bug. | The ref is copied from the named subject's `WorktreeBranch`; it is consistent with the (wrong) subject by construction. | No ref-derivation change. |
| `-RepairSource` repairs are adopted with `-FromTask`. | The docs say so; `AgentTaskLandService.RequestAsync` and `AgentTaskLandSourceResolver.RecoverSourceAsync` refuse `RepairSourceTaskId != null` as an adoption source (`adopt_source_invalid`, `recovery_source_invalid`). CARD-0753 D-2: a `-RepairSource` task attributes onto the owner's ref and lands through `-RecoverReviewedSource` with owner-bound evidence; a `-StartRef` repair lands through `-FromTask` (or directly as its own owner) with evidence naming the repair. | This is the "conventions differ" the brief names. S1's messages state both shapes. The doc paragraph belongs to CARD-0807 D-3; the product question belongs to CARD-0603. |
| (not on the card) The land-time sibling inspection also times out. | `AgentTaskLandService.CollectUnlandedSiblingsAsync` shares the 2 s budget; it already filters to Succeeded/Blocked rows and is advisory (it only decorates the outcome line). `fb8fa4a9`'s and `21412cdd`'s lands both logged "sibling inspection incomplete (inspection_timeout)". | Out of scope; it benefits from S3's budget change only. Noted so nobody reopens it as a new mystery. |

Evidence identity contract this plan relies on (CARD-0753 D-2, restated by CARD-0807 D-3):

| Operation | Evidence `subjectTaskId` | Land shape |
|---|---|---|
| Ordinary owner land | the owner | `-Land <owner> -ExpectedSourceSha <sha> -ReviewEvidenceId <id>` |
| Owner's own pushed branch after Failed/Blocked | the owner | same plus `-RecoverReviewedSource` |
| A `-StartRef` repair's pushed branch | the repair task | `-Land <owner> -FromTask <repair> ...`, or `-Land <repair> ...` when the repair is landed as its own owner (CARD-0649 did this) |
| A `-RepairSource` repair | the owner (work is on the owner's ref) | `-Land <owner> -RecoverReviewedSource ...`; never `-FromTask` |

## Boundary with adjacent cards

- **CARD-0807 (in flight, branches `feat/card-task-712e6984f` and siblings; not on master).** It changes `server/Bundles/stage-review.md`, `ReviewEvidence.cs`, `docs/orchestration-loop.md` lines 76-112 (Reviewed owner recovery, Repair source), and `AgentTaskReplyService.cs` at two places: the `callerWarning` composition just before `RecordDelegateStageOutcomeAsync` is called (line ~898) and the signature/body of `RecordDelegateStageOutcomeAsync` (line ~4307), which now takes the parsed `ReviewEvidence.Result`. This plan does not touch the bundle, the parser, the not-standalone diagnostic or those two doc paragraphs. S2 edits the same method and reuses the same header seam, so S2 is a separate Code group that starts only from a base containing CARD-0807's landed change (D-7). S1, S3, S4 and S5 touch files CARD-0807 does not; S5's single call-site edit is at line ~825 of `AgentTaskReplyService.cs`, outside both CARD-0807 hunks. CARD-0807's `C807_SourceReviewFeedsAdoption` and `C807_WrongOwnerEvidenceRefusesAdoption` remain the settlement-to-admission coverage; S2 adds consistency warnings on top and must not duplicate them.
- **CARD-0603 (Backlog): a `-RepairSource`-succeeded task cannot land when the owner is terminally Failed.** CARD-0753's `-RecoverReviewedSource` is the answer on master; the card stays open for closure. This plan keeps `repair_source_landing_owner_required` and the `RepairSourceTaskId` adoption exclusion exactly as they are. Its secondary item (1), "a workaround task's own commit is not pushed to its own branch", is desktop worktree reuse; S5 does not address it and must not claim to.
- **CARD-0675 (Backlog): landing a `-StartRef` repair needs a manual force-push.** CARD-0753's `-FromTask` adoption is on master (`AdoptFromTaskId`, `LandRecoveryMode.AdoptReviewedSource`); the card stays open for closure. This plan changes no adoption mechanics (pins, `--force-with-lease`, request supersession, CLI flags); S1 only makes its refusals say what they mean.
- **CARD-0442 / CARD-0508 / CARD-0613.** S3 keeps their selection semantics (Target/Continue/WaitForLand/Ambiguous/Unknown, containment via ancestry and cherry, dirty-source exclusion, explicit `-BaseTask` validation, `-StartRef` bypass). S4 reverses one documented choice, "keep safe base on incomplete inspection", for two cases only, and says so in `T0442_V30`.

## Decisions

### D-1: land refusals name both identities and the fitting command; codes stay

`LandApproval.LoadUsableEvidenceAsync` gains the identity it is checking (`Owner`,
`RecoveryOwner`, `AdoptionSource`) and, on `review_evidence_subject_mismatch`,
`review_evidence_ref_mismatch` and `review_evidence_sha_mismatch`, throws a message that
carries: the evidence id; the evidence's recorded subject (short id, `reviewedSourceRef`) and
SHA; the identity this land requires (short id, full ref) and the expected SHA; and one
corrective sentence per mode. For adoption: "-FromTask <source> needs evidence whose
subjectTaskId is <source>; commission a fresh same-card Review naming <source> at <sha>". For
owner/self modes when the evidence names a same-card sibling: "land <sibling> directly, or adopt
it with -Land <owner> -FromTask <sibling> when its pushed tip is the reviewed SHA". The
evidence's actual subject task is loaded for its branch only when a mismatch is being reported.

The two admission call sites in `AgentTaskLandService.RequestAsync` pass the identity. The
asynchronous refusal paths (`AgentTaskLandSourceResolver.RecoverSourceAsync`,
`AgentTaskLandingProtocol`) persist only a code today; they pass `ex.Message` through the
existing `detail` parameter of `RefuseAsync` so `-Status` shows the same sentence. No new
problem-details extension.

Rejected: new codes (`C488_*` tests, `delegate.ps1` and the ops doc key on the existing ones);
relaxing any check; guessing the intended source from the SHA (the card's correction forbids it).

### D-2: settlement warns on evidence that contradicts facts the server already holds; binding is unchanged

After `RecordDelegateStageOutcomeAsync` has authorized and bound a subject, two checks run on
persisted data only, and each adds a `Warning` event plus a header line through the existing
`warning` argument of `BuildParentNoteAsync`, formatted like CARD-0807's:
`review-evidence-warning=<code>: <plain text>`.

| Code | Condition | Text carries |
|---|---|---|
| `review_evidence_sha_not_review_base` | the Review task's own `WorktreeBaseSha` is a full SHA and differs from `reviewedSourceSha` | review base SHA, claimed SHA, "this review's checkout was cut at X; the block claims Y" |
| `review_evidence_subject_tip_mismatch` | the named subject's `CompletionProgressEvidenceJson.RemoteSync.ConfirmedSha` (fallback: its `Primary` source `VerifiedSha`) is a full SHA and differs from `reviewedSourceSha` | subject short id and branch, its confirmed tip "as confirmed at its settlement", the claimed SHA, and "if a different task's pushed branch was reviewed, commission a fresh same-card Review naming that task" |

The evidence still binds with the named subject; landing's gates remain the authority. Missing
facts (null base, no progress evidence, a non-full SHA) are silent. The warnings coexist with
other warnings and never change status, finding, `NextStage` or `PipelineHandoff`. Applied to
CARD-0417: the second warning would have fired on `ec59b256` (subject ad020e8c confirmed at
`a12de733`, claimed `8d5050ea`); the first would have fired on `8532af35` (base `cd133de9`,
claimed `a12de733`).

Known false positive, accepted: a `-RepairSource` repair that pushed onto the owner's ref after
the owner settled makes the owner's confirmed tip stale, so owner-bound evidence at the new tip
warns; the text says "as confirmed at its settlement" and the land succeeds anyway.

Rejected: an `ls-remote` at settlement (network in the settlement path; runner sync already
observed the remote for the subject); refusing to bind (breaks the legitimate stale-tip cases);
a new `StageOutcome` column (the facts live on the tasks; no schema change).

### D-3: base inspection does cheap facts first, batches git, and probes only tips that can win

`AgentTaskWorktreeBaseResolver.ResolveAsync` changes in this order:

1. **DB pre-filter.** From the landed-filtered `kept` set, only rows that are `Succeeded`, have a
   pending land request, or are the explicitly requested `-BaseTask` row are inspected. Every
   other row keeps its existing per-row warning ("Task X branch B is {Status}; it is not an
   automatic source") without the `@ sha` suffix, at zero git commands. `candidate_limit`
   counts inspectable rows.
2. **One tips query.** `git for-each-ref --format='%(objectname) %(refname)' refs/heads/<b1> ...`
   replaces the per-candidate `rev-parse`; a branch absent from the output is "has no local
   commit" as today.
3. **One containment query.** `git for-each-ref --merged=<target> refs/heads/...` marks branches
   whose tip is an ancestor of the target as `Contained` in one command. Remaining candidates are
   grouped by SHA before the existing `rev-list --min-parents=2` and `cherry` probes run once per
   distinct SHA, so the `Unknown` (merge range) and patch-equivalent outcomes are unchanged.
4. **Checkout safety last.** Eligibility, writer check, pairwise `merge-base --is-ancestor` and
   the maximal-tip computation run without the checkout-safety probe. The probe (`worktree list`
   once, then `status` and the three `rev-parse --git-path` calls per surviving checkout) runs only
   for maximal tips and the explicit requested row; an unsafe tip is excluded with the existing
   "dirty or in-progress" warning and the maximal set is recomputed from the remaining eligible
   tips. The resulting decision is the same as today's for every existing test scenario.
5. **Budget.** `GitSettings.WorktreeBaseInspectionTimeoutSeconds` default 2 -> 5. Measured floor
   is 200 ms per command on the desktop; after this slice a card like CARD-0417 costs about
   4 fixed + 2 per distinct uncontained SHA + pairwise + one 5-command probe, roughly 19 commands
   instead of about 60, which still needs more than 2 s under load. The limits remain inspection
   limits, not request timeouts (CARD-0442).

Rejected: a role filter (Plan/TestDesign branches legitimately carry plan commits; a Review that
committed would be silently dropped); dropping the checkout-safety probe (it is the dirty-source
guard, V07); an unbounded budget; inspecting in the background and retrying.

### D-4: an incomplete Auto-mode inspection blocks when a source was previewed or the task is a Review

The selection record gains `Incomplete` (true for `inspection_timeout`, `candidate_limit`,
`git_command_limit`, `git_inspection_error`). At dispatch, `Unknown` in Auto mode blocks with the
Task-mode message shape ("<reason>: <warnings> Retry, or recreate with -BaseTask <previewed task>
/ -StartRef <previewed sha> / -FreshWorktree") when either the create preview
(`WorktreeBasePreviewJson`) had selected `Continue` with a source task, or the task's role is
Review. A fresh Code task whose preview was already `Target` keeps CARD-0442's safe-base
behaviour with the existing warning intents. Create-time behaviour is unchanged (Auto-mode
`Unknown` still creates the task with the preview; dispatch re-resolves).

`T0442_V30`'s `deadline` and `candidate_limit` arms (preview `Continue`, then an incomplete
dispatch inspection) change from "Dispatched at master with a preview-changed warning" to
"Blocked, no worktree, message names the previewed source"; the `pending_land_budget` arm is
unchanged. A new arm covers a Review with a `Target` preview, and one covers a Code task with a
`Target` preview keeping the safe base.

Rejected: blocking on every Auto-mode `Unknown` (reverses CARD-0442 for the case where master is
right); a bounded automatic re-resolution (same cost, same outcome under load).

### D-5: a non-Code Worktree settlement with no pushed progress tells the caller

`TaskCompletionProgressService` gains `NoPushedProgressWarning(evidence)` returning
`progress=none; reason=<reason>; ref=<fullRef> still at <confirmedSha>` when the assessment is
`NoAttributedProgress` and no `ClaimWarning` is present (reasons include
`runner_no_pushed_progress`, `branch_not_pushed`, `no_movement`). `AgentTaskReplyService` calls it
at the existing progress-warning site (line ~825) for tasks whose role is not Code, so the line
lands in the `Warning` event and the caller's note header; Code keeps its failure path and gets
no duplicate line. Status stays Succeeded (CARD-0657 chose not to fail non-Code roles for this).

Rejected: failing non-Code tasks (a status-semantics change beyond this card); parsing prose
commit claims (the structured `[antiphon-progress:]` line is the contract).

### D-6: documentation lands with the code, outside CARD-0807's paragraphs

`docs/ops-http.md`: the land/v2 and reviewed-recovery rows state that a review-evidence 409
names the evidence subject, the required identity and the fitting flag; the worktree-base rows
state the pruning rule, the batched probes, the 5 s default and the D-4 block rule; the progress
row states `progress=none`. `docs/orchestration-loop.md` section 4 ("Checking on a delegate")
gets one paragraph listing the settlement warning codes an orchestrator must act on
(`review-evidence-warning=...`, `progress=none`), placed away from lines 76-112.

### D-7: two Code groups; S2 waits for CARD-0807

Group A is S1, S3, S4, S5 and D-6 docs, dispatched now from master. Group B is S2, dispatched
only from a base that contains CARD-0807's landed `AgentTaskReplyService` change; if CARD-0807
has not landed when group A completes, group A lands alone and group B is a later Code dispatch
on the same card. Both groups run their own checkpoint rows (`After` column). This keeps the
same-source-area rule: no 0788 Code task touches `RecordDelegateStageOutcomeAsync` while
CARD-0807's Code is in flight.

## Implementation slices

| Slice | Files | Tests |
|---|---|---|
| S1 land refusal detail | `server/Application/Services/LandApproval.cs`; `server/Application/Services/AgentTaskLandService.cs` (two call sites); `server/Application/Services/AgentTaskLandSourceResolver.cs` (`detail` pass-through); `docs/ops-http.md` | `AgentTaskLandApprovalRequestTests`: extend `C488_EvidenceSubjectMatches`/`C488_EvidenceRefMatches`/`C488_EvidenceShaMatches` to assert the message names both short ids and refs; new `C788_AdoptionSubjectMismatchNamesSourceAndFlag` (owner + source + evidence naming the owner -> 409 `review_evidence_subject_mismatch`, message names the source as the required subject and says `-FromTask`); new `C788_OwnerMismatchNamesSiblingAndAdoptionShape`. `AgentTaskLandAdoptionTests` stays as the landing-boundary regression class (no change expected). |
| S2 settlement consistency warnings | `server/Application/Services/AgentTaskReplyService.cs` (`RecordDelegateStageOutcomeAsync` after CARD-0807; returns/appends the header warning) | new `tests/Antiphon.Tests/Application/ReviewEvidenceConsistencyTests.cs` on `C544World` (real reply settlement via `CreateTaskAsync`/`DispatchAsync`/`SeedTurnAsync`/`OnTurnEndAsync`): subject tip mismatch warns and still binds; review base mismatch warns and still binds; consistent facts produce no warning; missing facts are silent; both warnings and an unrelated warning coexist in one header; repeated settlement stays idempotent. Existing `AgentTaskReviewEvidenceTests` and CARD-0807's `ReviewEvidenceSettlementTests` are the regression net. |
| S3 resolver | `server/Application/Services/AgentTaskWorktreeBaseResolver.cs`; `server/Application/Settings/GitSettings.cs`; `docs/ops-http.md` | `AgentTaskWorktreeBaseResolverTests` (real git, `ScratchGitRepo`, `GitProcessGate`, `selection.GitCommands`): new `C788_PrunedRowsCostNoGitCommandsAndKeepWarnings` (Failed/Canceled/Working rows), `C788_ContainedSiblingBranchesAreClassifiedInOneQuery` (many Review-style branches at the target plus one Code tip; assert the decision and a command ceiling), `C788_CheckoutSafetyProbesOnlyMaximalTips` (dirty non-maximal candidate adds no `status` calls; dirty maximal tip still excluded), `C788_PendingLandRowStillHoldsWithoutInspection`, `C788_RequestedNonSucceededRowStillValidated`. `T0442_V02`-`V10`, `V29` and `WorktreeBaseSelectionTests` (C508/C540) must stay green unchanged except the `@ sha` suffix on pruned-row warnings. |
| S4 dispatch block | `server/Application/Services/AgentTaskDispatcher.cs`; `server/Application/Services/AgentTaskWorktreeBaseResolver.cs` (`Incomplete`); `docs/ops-http.md` | `AgentTaskDispatchBaseGuardTests`: `T0442_V30` arms updated as D-4 states; new `C788_ReviewWithIncompleteInspectionBlocks`; new `C788_FreshCodeWithTargetPreviewKeepsSafeBase`. |
| S5 progress warning | `server/Application/Services/TaskCompletionProgressService.cs`; `server/Application/Services/AgentTaskReplyService.cs` (line ~825 call site only); `docs/ops-http.md`, `docs/orchestration-loop.md` section 4 | `TaskCompletionContinuationTests` (Unit): `C788_NoPushedProgressWarningShape`; `RunnerTaskSettlementTests` (`RunnerSettlementWorld.CreateAsync(AgentTaskRole.Plan)`, no push): `No_push_on_a_plan_role_settles_succeeded_with_a_visible_warning` asserting Succeeded, `NoAttributedProgress`, a `Warning` event containing `progress=none`, and the same line in `world.NoteAsync()`'s header; `Unmarked_completion_gets_the_code_progress_policy` stays as the Code contrast. |

Scope for the Code briefs (comma-separated areas/globs):
`delegation, docs, server/Application/Services/LandApproval.cs, server/Application/Services/TaskCompletionProgressService.cs, server/Application/Settings/GitSettings.cs, tests/Antiphon.Tests/Application/**, tests/Antiphon.Tests/TestHelpers/**`.
Group B adds nothing; it is the same scope minus S3/S4/S5 files.

## TestDesign handoff requirements

Read the bodies before fixing names, V/R ids, floors and positive controls:

1. **Guards needing a distinct positive control** (Mutation runs them after land; Code runs
   ordinary V/R only): S1 identity labelling (swap `AdoptionSource` for `Owner` in the adoption
   call site: the new assertion on "-FromTask" must fail); S2 comparison direction (compare
   against the review's own SHA instead of the subject's confirmed tip: the mismatch test must
   fail) and "still binds" (a mutation that skips binding on warning must fail the adoption feed
   test in CARD-0807's class); S3 pre-filter (remove the status predicate: the zero-git-command
   assertion must fail) and probe placement (probe every eligible tip: the command ceiling must
   fail); S4 role/preview predicate (drop the Review arm: the Review block test must fail); S5
   role gate (call the warning for Code too: the Code contrast test must see a duplicate line).
2. **Real seams, not parsers.** S2 and S5 evidence must come through `AgentTaskReplyService`
   settlement and the completion note (`C544World`, `RunnerSettlementWorld`), never from calling
   `ReviewEvidence.TryParse` or `ProgressWarning` alone. The Unit-lane shape test for S5 is in
   addition to, not instead of, the settlement test.
3. **Counts.** Confirm `Min` from the roster: `AgentTaskWorktreeBaseResolverTests` and
   `AgentTaskDispatchBaseGuardTests` are argument-expanded; the values below are floors from the
   method counts read here (resolver 10 methods, selection 14, guard 24, approval-request 19,
   adoption 9 methods / 15 results, runner settlement 15, runner progress 8, continuation 24,
   review-evidence 16, CARD-0807 settlement 11) plus the new methods, and must be raised to the
   expanded counts, never lowered.
4. **Categories.** Every class above is `Integration` (`Slow` where already marked) except
   `TaskCompletionContinuationTests` and `LandFailureDiagnosticTests` (Unit). New classes carry
   `Integration`; `ReviewEvidenceConsistencyTests` is `Slow` (C544World spawns processes) and
   goes on `slow-tests-allowlist.txt`; process-spawning classes keep the assembly-local
   `ParallelLimiter<ProcessSpawnLimit>`.
5. **Do not schedule** all of `AgentTaskReplyIntegrationTests`, the full land suite, or the
   full assembly; name the classes. A broad run needs a named invariant and its cost first.

## Verification design

Finalized 2026-09-29 by TestDesign at plan commit
f23a0bde93300b33c1ba341fc7acb04c6ea75071. This is source-inspected design, not executed
test/PC evidence. D-1 through D-7 remain the fix design; D-4 is accepted. This section
supersedes the draft roster assumptions in the handoff above.

**Dispatch gate:** group A closes S1/S3/S4/S5 and D-6. Group B closes S2 only after CARD-0807
has landed. Its Code round 8/task c5973812 edits the same evidence area. The locally available
origin/feat/card-task-c5973812 was inspected at 712e6984f959e0bd75ecc9256902d89e62e986b4:
a fixture reference, not proof of eventual landing or round 8 completion. Before commissioning
B, record CARD-0807's confirmed landing operation and landed SHA, verify that SHA is an
ancestor of B's starting HEAD, and recheck the landed settlement-test roster. Do not use the
in-flight branch as B's base. If A lands first, keep B and its PCs pending on this card.

### Inspection

| Bodies/seams inspected | Boundaries assigned |
|---|---|
| LandApproval evidence loaders; C488 admission evidence tests; AgentTaskLandAdoptionTests recovery fixtures; LandingSafetyHarness; resolver/protocol evidence catches | V-1, R-1: identity labels and all three asynchronous detail producers. |
| AgentTaskReplyService warning composition, binding and completion obligation; TaskProgressDtos; C544World; CARD-0807 ReviewEvidenceSettlementTests.SettleAsync/adoption fixture | V-2, R-2: authorized report coordinates versus independently persisted facts. |
| Base resolver ResolveAsync/inspection accounting; ScratchGitRepo; T0442 V02-V10/V29; V30 dispatch/deadline fixture; C508/C540 partial rosters | V-3/V-4, R-3/R-4: status, pending requests, explicit source, Git identity, containment, unsafe checkouts and incomplete inventory. |
| ProgressWarning; runner no-push/unmarked settlement bodies; RunnerSettlementWorld.SettleAsync/NoteAsync/obligations; continuation roster | V-5, R-5/R-6: NoteAsync returns a queue row, not a recipient receipt. |
| VerificationRoundDeliveryTests.C544_CompletionReceipt/Recovery and C544_LandRefusalReceipt/Recovery, including RefusalRig; C544DeliveryRig/faults; LandOutcomeDeliveryHarness | V-6/V-7/V-8: real producer/queue/complete caller prompt and provider recreation. |

Fixture work required in Code:

- Record actual Git argv and repository through a LandingGit decorator forwarding to real Git;
  clear traces after fixture construction. Count safety commands separately. Retarget
  CandidateDeadlineGit to the new batch operation with the intended branch in its arguments.
  Assert the injection fired and advance past the configured timeout, now five seconds;
  V29's explicitly configured two-second budget stays two seconds. V08's row-local git_error
  must remain a row-local failure: replace RefLookupFailureGit with a failed common-directory
  probe on that candidate's distinct linked checkout, preserving Target plus an unknown-row
  warning. A failed whole batch is instead git_inspection_error/Incomplete and belongs to V4.
  Landed-row cases assert the excluded branch is never inspected. An obsolete rev-parse hook
  that never fires is not green deadline/error coverage.
- C544World's default Review is ReadOnly and has no base. For S2 create a Worktree Review with
  explicit start, or seed the Review's persisted base before settlement for a DB-fact case.
  Seed full distinct A/B/C SHAs. Never substitute the subject's base for the Review's base.
  No additional settlement ls-remote is permitted.
- Strengthen CARD-0807's existing C807_SourceReviewFeedsAdoption with a stale confirmed subject
  tip before settlement. Its inspected fixture has no progress evidence: without this addition,
  it cannot kill "skip binding when warning". Keep its real adoption admission assertions and
  add the warning assertion; do not duplicate its settlement-to-admission test.
- Reuse C544/RefusalRig services for delivery, adding setup callbacks before settlement and
  extracting helpers where needed. New CompletionWarningDeliveryTests and
  LandEvidenceWarningDeliveryTests hold only the scoped cases below. A real local Plan Worktree
  settlement supplies no_movement delivery; RunnerSettlementWorld separately proves real
  runner_no_pushed_progress. Do not insert a fake warning, header or expected receipt.
- New integration classes use Integration, Slow, ParallelLimiter<ProcessSpawnLimit>, and
  NotInParallel("MessageQueue") for queue rigs. Register fully qualified Slow names in
  slow-tests-allowlist.txt, including ReviewEvidenceConsistencyTests. Preserve existing
  categories/limiters. Shape tests stay in Unit TaskCompletionContinuationTests.
  Every DB assertion scopes to fixture-owned IDs.

### Delivery inventory

| Path / producer | Destination and durable identity | Persistence, recovery and receipt |
|---|---|---|
| S1 synchronous RequestAsync | HTTP caller; evidence ID + required owner/source ID + expected SHA | Conflict code/message before admission; no new request or publication. No queue on this path. |
| S1 asynchronous resolver and both protocol evidence rechecks | Original caller session; task + land request + refusal event + notification + SourceLandNotificationId | Real refusal transaction owns event/outcome obligation. Recreated notification service reuses keyed queue row. V-7 requires complete detail in caller UserPrompt above attempt floor, with confirmation naming that sequence. |
| S2 Review consistency warnings / reply settlement | Caller; Review task + stage outcome + completion event + notification + keyed row | Warning events, bound evidence and immutable completion snapshot commit with settlement. Scanner recovers enqueue; queue preserves header through rendering. V-8 proves receipt and replay idempotence. |
| S5 no-progress warning / reply settlement | Caller; non-Code task + completion event + notification + keyed row | Same completion outbox/recovery. V-5 proves runner facts enter event/header; V-6 follows real no-movement settlement to receipt. |

Completion cases cover already eligible and busy callers (zero writes before TurnEnd), raw
inline and distilled/spilled output. Recovery cuts are obligation-insert, settled-committed,
note-insert, note-committed, wakeup-dropped, render-committed, spill-written, attempt-committed
and prompt-accepted, using C544's existing hooks. Assert each cut fired, recreate providers and
drive actual scan/flush. Obligation insertion failure rolls back settlement; replay the same
turn. Other cuts retain the warning snapshot and one logical note. Reconciliation after receipt
must not type again.

S1 refusal cuts: refusal transaction failure; terminal refusal committed before enqueue;
before-enqueue; queue-inserted; prompt accepted before confirmation. Use landing save/boundary
hooks, then retry the owning land/reconcile path without manually advancing notification/queue
state. Inspect persisted request/event/notification detail after recreation and delivered text,
not just the refusal code.

The controlled protocol adapter records actual queue submission in its OnSubmitted UserPrompt.
It proves producer, persistence, content, ordering and receipt matching; it does not prove
physical ConPTY, a live provider or deployment. A queued row, Sent flag, event, ack, or pointer
without verified spill content is insufficient. No expected prompt is seeded independently of
submission. No new transport mechanism is proposed.

### Proves it works now

Exact Code targets follow. Argument rows are separate TUnit results; internal loops are not.

**V-1 — S1 identity and asynchronous detail.**

Extend AgentTaskLandApprovalRequestTests.C488_EvidenceSubjectMatches, C488_EvidenceRefMatches,
C488_EvidenceShaMatches: unchanged conflict code; evidence ID, actual/required IDs and full refs,
claimed/expected SHA and correct mode-specific corrective text. Use distinct refs when IDs agree.

Add C788_AdoptionSubjectMismatchNamesSourceAndFlag and
C788_OwnerMismatchNamesSiblingAndAdoptionShape (one result each). Adoption must explicitly label
the required identity as adoption source and associate its ID with subjectTaskId/FromTask.
Checking that "-FromTask" occurs anywhere is insufficient: owner guidance also contains it.
Owner guidance names direct sibling land or owner adoption. Include RecoveryOwner in fixture
loops with -RecoverReviewedSource and no suggestion to adopt a RepairSourceTaskId task.

Add AgentTaskLandAdoptionTests.C788_RecoveryMismatchDetailSurvivesRestart: six arguments,
{source_resolver, protocol_prepare, protocol_resume} × {owner_recovery, adoption}.
Admit valid evidence, inject a fixture-only evidence mismatch at the relevant recheck and prove
the hook was reached. Product code never rewrites immutable evidence. Fresh-provider reads
retain identities/refs/SHAs/flag, original code and request ID; no target publication. Protocol
rows reach their own catches rather than failing at source resolution. V-7 covers receipt.

**V-2 — S2 persisted comparisons, binding and warning composition.**

New ReviewEvidenceConsistencyTests uses C544 create/dispatch/turn/reply settlement:

| Exact method | Arguments/results | Decisive assertion |
|---|---:|---|
| C788_SubjectTipMismatchWarnsAndBinds | 2: remote confirmed, Primary fallback | Review base = claim B, subject tip A. Only tip code; event/header carry A/B, subject/ref and "as confirmed at its settlement". Outcome still binds subject/ref/repo/B. |
| C788_ReviewBaseMismatchWarnsAndBinds | 2: full 40, full 64 | Review base A, subject confirmed B, claim B. Only base code; coordinates remain bound. Full-64 case seeds persisted facts, not a claimed SHA-256 Git checkout. |
| C788_ConsistentOrMissingFactsStaySilent | 8: all equal, null base, short base, absent JSON, malformed JSON, missing tips, invalid tips, equal full-64 | Other fact equal/absent, so it cannot mask the case. Zero consistency codes; usable block still binds. Non-full remote with no usable Primary is silent. |
| C788_RemoteConfirmedTipPrecedesPrimary | 2: remote matches/Primary differs; remote differs/Primary matches | Warning iff valid confirmed remote differs. Internal rows cover absent/non-full remote fallback to valid Primary; never use ObservedSha, ClaimedSha or alternate origin. |
| C788_BothWarningsCoexistWithOtherHeaderWarnings | 1 | Review base A, claim B, tip C. Each code once in events and immutable header alongside a real dirty-file warning as in C807. |
| C788_RepeatedSettlementKeepsOneWarningPerCode | 1 | Replay OnTurnEnd with both mismatches: same outcome/notification IDs, one event per code, unchanged status/finding/scope/next/handoff. |
| C788_UnauthorizedOrUnusableEvidenceHasNoConsistencyWarning | 4: failed, blocked, foreign card, unusable block | No bound coordinates or new consistency codes; existing refusal/parser behavior unchanged. |

Subtotal **20** results. Binding cases also loop Clean/Found and preserve routed handoff;
C807 covers follow-up authorization. Strengthened C807_SourceReviewFeedsAdoption proves the
accepted stale-tip warning still binds and permits actual admission against a current pushed
source. Landing retains its normal fresh gates. V-8 adds eleven results to this class.

**V-3 — S3 avoided Git work and unchanged source eligibility.**

New AgentTaskWorktreeBaseResolverTests methods:

| Exact method | Arguments/results | Decisive assertion |
|---|---:|---|
| C788_PrunedRowsCostNoGitCommandsAndKeepWarnings | 6: Failed, Canceled, Blocked, Queued, Dispatched, Working | Compare one eligible source with/without pruned rows: same decision/source and identical GitCommands/actual command trace. A within-warning-cap case checks every pruned status/branch warning without "@ sha"; a separate over-candidate-cap case proves no refusal from pruned rows without requiring an unbounded warning list. Zero means zero added Git work, not zero global commands with an eligible source. |
| C788_ContainedSiblingBranchesAreClassifiedInOneQuery | 1 | Twelve Review-style branches at target plus one clean uncontained Code checkout, all same repo: one tips query, one merged query, no per-branch commit lookup, Continue selects Code, at most 11 commands (4 fixed + 2 containment + 5 safety). Internal positive roles Plan/TestDesign/Review with actual commits remain eligible. |
| C788_CheckoutSafetyProbesOnlyMaximalTips | 2: clean nonmaximal, dirty nonmaximal | Chain A < B, both registered, B clean. Only B probed: one worktree list, one status, three git-path calls; safety-command ceiling 5; no safety call on A. |
| C788_UnsafeMaximalRecomputesCandidates | 3: dirty descendant, dirty divergent tip, dirty preferred equal-SHA label | Exclude unsafe branch, preserve warning, recompute clean ancestor/other tip/alias. No early Target, dirty selection or false Ambiguous. T0442 V07 retains all five dirty/in-progress and explicit-source forms. |
| C788_PendingLandRowStillHoldsWithoutInspection | 3: Queued, Held, Running request | Non-Succeeded same-destination source plus candidate cap/deadline/unavailable-repo internal arms yields WaitForLand. Never prune into Target/Continue. |
| C788_RequestedNonSucceededRowStillValidated | 6: Blocked, Failed, Canceled, Queued, Dispatched, Working | Explicit quiescent row inspected/accepted only if safe; active row refused. Dirty/open-writer cases refuse; pending land wins. |

Subtotal **21** new results. Trace assertions must fail the old algorithm even under a generous
total budget. Probe-all can exclude dirty A early and save ancestry work: PC-11 therefore uses
the **safety-command** ceiling and paths, not just an overall command ceiling.

**V-4 — S4 precise dispatch block predicate; D-4 accepted.**

- Rename/update T0442_V30_incomplete_dispatch_inspection_keeps_safe_base_and_land_hold to
  T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold.
  Five arguments: deadline, candidate_cap, git_command_limit, git_inspection_error,
  pending_land_budget (adds two results). First four start with persisted Continue/source
  preview and finish Blocked, no worktree/branch/session, original preview retained; reason,
  source ID/SHA and applicable retry options visible. Pending land remains Queued and resumes
  correctly after completion. Drive real dispatch, not a fabricated selection.
- C788_ReviewWithIncompleteInspectionBlocks: four incomplete reasons above; Review with initial
  Target preview. Add candidates/fault after create so dispatch genuinely inspects. Same
  blocked/no-launch assertions.
- C788_FreshCodeWithTargetPreviewKeepsSafeBase: four reasons; fresh Code remains Dispatched at
  actual target SHA with warning intent and no borrowed source.
- C788_ExplicitBaseOverridesIncompleteAutoGuard: two arguments, FreshWorktree and StartRef,
  Review role. Explicit Target retains existing behavior; StartRef bypasses inspection.
- C788_NonIncompleteUnknownKeepsExistingBehavior: one result, Code/Review internal cases;
  target_missing/unusable-source diagnostics remain outside the new incomplete predicate.
  Assert Incomplete false; V29 and four dispatch faults assert it true for budget/error
  results. Existing explicit Task invalid-source refusals remain authoritative.

Subtotal **13** added results in AgentTaskDispatchBaseGuardTests. Decision, role, preview
mode/source and Incomplete are asserted, not just warning text.

**V-5 — S5 role/evidence gates and runner settlement.**

- Unit TaskCompletionContinuationTests.C788_NoPushedProgressWarningShape: seven arguments,
  runner_no_pushed_progress, branch_not_pushed, no_movement, claim-warning-present,
  ProgressObserved, Indeterminate, missing coordinates. First three emit prefix/reason/full
  ref and available confirmed tip, using RemoteSync then Primary facts. Missing facts must
  not invent a confirmed tip (an unpushed branch has none); mark unknown/absent if rendered.
  Other assessment/claim cases emit no new line.
- RunnerTaskSettlementTests.No_push_on_a_plan_role_settles_succeeded_with_a_visible_warning
  (one): real unchanged remote, Succeeded/NoAttributedProgress, exactly one warning, same line
  in note header above report, correct ref/baseline, no no-progress incident.
- C788_NonCodeNoPushRoleMatrix (four: Investigate, TestDesign, Review, Custom): same real
  settlement assertions; internal absent-remote-branch arm retains branch_not_pushed.
- C788_ClaimWarningDoesNotDuplicateNoPush (one): real Plan no-push settlement with a malformed
  or foreign-task progress token sets ClaimWarning to claim_malformed_or_quoted or
  claim_not_for_this_task, retains progress=unavailable, and adds no progress=none. Assert
  ClaimWarning is populated before checking suppression. A valid but unpushed commit claim
  alone does not set ClaimWarning and is not this fixture. Successful pushed control is silent.
- C788_NonWorktreeDoesNotGetNoPushWarning (two: ReadOnly, Shared): real local create/settle,
  no new warning; do not relabel a runner fixture after capturing its Worktree baseline.
- Strengthen existing Unmarked_completion_gets_the_code_progress_policy **arm (b)** with no
  progress=none in warning events or note header and unchanged Failed/CompletedWithoutProgress.
  The no-claim arm is necessary for PC-19. Apply absence checks to marked
  No_push_settles_with_specific_reason too.

Adds **8** runner results and **7** Unit results. V-6 provides separate recipient evidence.

**V-6/V-7/V-8 — warning receipt and recovery.**

| Coverage / class | Exact methods/arguments | Results |
|---|---|---:|
| V-6 / new CompletionWarningDeliveryTests | C788_ProgressWarningReceipt (busy false/true); C788_ProgressWarningRecovery (nine completion cuts) | 11 |
| V-7 / new LandEvidenceWarningDeliveryTests | C788_LandEvidenceRefusalReceipt (busy false/true); C788_LandEvidenceRefusalRecovery (five refusal cuts) | 7 |
| V-8 / ReviewEvidenceConsistencyTests | C788_ConsistencyWarningReceipt (busy false/true); C788_ConsistencyWarningRecovery (nine completion cuts) | 11 |

Receipt methods loop raw-inline/distilled-spill where supported. Recovery methods loop both
caller states; completion cases also loop both renderings. S2 produces both codes plus another
warning and retains bound evidence. S1 loops owner/adoption and three mismatch kinds. Fresh
fixture IDs per internal combination. Assert complete wire/spill content, exact warning values,
durable correlation, confirmation sequence above attempt floor and one keyed note; reconciling
again adds no prompt. Extract helpers rather than schedule the whole heavy delivery class.
Do not call existing [Test] methods as verification oracles.

### Guards the regression

| ID | Named coverage / decisive regression |
|---|---|
| R-1 | Existing AgentTaskLandAdoptionTests 15 results: local CAS, remote lease, dirty-source refusal, recovery authority and no publication after refusal. |
| R-2 | CARD-0807 ReviewEvidenceSettlementTests 11 inspected results and AgentTaskReviewEvidenceTests 16: original source identity, parser/authorization/scope, adoption feed. Parser/formatter tests are supplementary, not S2 settlement proof. |
| R-3 | Resolver T0442 V02-V10/V29 (46) and WorktreeBaseSelectionTests partials (16): Git identity, ancestry/cherry, equal tips, writers, explicit/fresh modes, cancellation/budgets. |
| R-4 | Existing dispatch guard partials (74): create/dispatch races, pending land, retained worktree, C540 sibling warnings. |
| R-5 | RunnerCompletionProgressTests (8) and original runner settlement (15): one remote observation, local/repair attribution, uncertainty/deferred/recovered settlement, no destructive rescue. |
| R-6 | Unit lane floor stays 3000, including shape/classification guards. This is a lane floor, not an exact source census. |

### Guard inventory and positive controls

Each G-n maps 1:1 to PC-n: **23 guards, 23 mapped, missing 0, duplicate maps 0**.
Unchanged authority/transport guards stay covered by R-1 through R-5 and receipt assertions;
this plan authorizes no change to those mechanics.

Mutation alone runs these after the relevant group's confirmed land. Code runs ordinary V/R;
Review judges those results and pending PCs before land. Exact method filter:
`/*/Antiphon.Tests.Application/<Class>/<Method>*` (trailing wildcard belongs to the method).
Expand the class shorthands below. Min counts the whole method's arguments; where a subset goes
red the decisive arguments are identified. Never filter on a literal parameter suffix.

| Guard / PC | Group; compiling defect | Exact red method; decisive assertion; Min | Cycle minutes |
|---|---|---|---:|
| G-1 / PC-1 | A, S1: pass Owner instead of AdoptionSource at adoption admission | Approval.C788_AdoptionSubjectMismatchNamesSourceAndFlag; source identity label/ID association wrong; 1 | 6 |
| G-2 / PC-2 | A, S1: omit ex.Message at resolver recovery refusal only | Adoption.C788_RecoveryMismatchDetailSurvivesRestart; source_resolver rows lose persisted detail; 6 | 12 |
| G-3 / PC-3 | A, S1: discard protocol preparation catch detail only | Same method; protocol_prepare rows lose detail after restart; 6 | 12 |
| G-4 / PC-4 | A, S1: discard protocol resumed-authority catch detail only | Same method; protocol_resume rows lose detail; 6 | 12 |
| G-5 / PC-5 | B, S2: compare claim to subject tip in review-base check | Consistency.C788_ReviewBaseMismatchWarnsAndBinds; base-only code missing; 2 | 8 |
| G-6 / PC-6 | B, S2: compare subject-tip claim to Review base instead | Consistency.C788_SubjectTipMismatchWarnsAndBinds; tip-only code missing; 2 | 8 |
| G-7 / PC-7 | B, S2: accept non-full persisted fact as comparable | Consistency.C788_ConsistentOrMissingFactsStaySilent; invalid-fact row gains forbidden code; 8 | 10 |
| G-8 / PC-8 | B, S2: prefer Primary over valid RemoteSync confirmed tip | Consistency.C788_RemoteConfirmedTipPrecedesPrimary; opposing-fact arms get wrong warning verdict; 2 | 8 |
| G-9 / PC-9 | B, S2: suppress bound coordinates when consistency warning exists | Settlement.C807_SourceReviewFeedsAdoption; strengthened stale-tip case loses binding/adoption admission; 1 | 10 |
| G-10 / PC-10 | A, S3: remove status pruning, retain later eligibility check | Resolver.C788_PrunedRowsCostNoGitCommandsAndKeepWarnings; added-command equality/candidate cap fails; 6 | 10 |
| G-11 / PC-11 | A, S3: probe every eligible checkout | Resolver.C788_CheckoutSafetyProbesOnlyMaximalTips; safety-command ceiling 5/path census fails; 2 | 8 |
| G-12 / PC-12 | A, S3: treat unsafe maximal checkout as safe | Resolver.T0442_V07_dirty_or_in_progress_checkout_is_excluded; dirty source selected; 5 | 10 |
| G-13 / PC-13 | A, S3: return Target after unsafe maximal instead of recomputing | Resolver.C788_UnsafeMaximalRecomputesCandidates; clean ancestor/alias missing; 3 | 10 |
| G-14 / PC-14 | A, S3: prune non-Succeeded pending rows from the retained inspection/hold inventory | Resolver.C788_PendingLandRowStillHoldsWithoutInspection; pending source fails to hold; 3 | 10 |
| G-15 / PC-15 | A, S3: remove explicit-source exception from pruning | Resolver.C788_RequestedNonSucceededRowStillValidated; quiescent explicit source not inspected/accepted; 6 | 10 |
| G-16 / PC-16 | A, S4: remove Review role arm | Dispatch.C788_ReviewWithIncompleteInspectionBlocks; Dispatched/worktree instead of Blocked/no launch; 4 | 10 |
| G-17 / PC-17 | A, S4: remove preview-Continue/source arm | Dispatch.T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold; first four rows silently cut target; 5 | 12 |
| G-18 / PC-18 | A, S4: drop Incomplete restriction, retain Unknown/Auto/role-preview | Dispatch.C788_NonIncompleteUnknownKeepsExistingBehavior; non-incomplete Review incorrectly blocked; 1 | 8 |
| G-19 / PC-19 | A, S5: invoke new warning for Code too | Runner.Unmarked_completion_gets_the_code_progress_policy arm (b); forbidden progress=none in event/header; 1 | 8 |
| G-20 / PC-20 | A, S5: remove ClaimWarning suppression | Continuation.C788_NoPushedProgressWarningShape; claim-warning row must return null; 7 | 6 |
| G-21 / PC-21 | B, S2: keep events, omit callerWarning consistency append | Consistency.C788_ConsistencyWarningReceipt; complete recipient prompt/spill lacks codes; 2 | 12 |
| G-22 / PC-22 | A, S5: keep event, omit caller-header progress append | CompletionDelivery.C788_ProgressWarningReceipt; recipient lacks progress=none; 2 | 12 |
| G-23 / PC-23 | B, S2 delivery: strip consistency lines from recovered snapshot rendering only | Consistency.C788_ConsistencyWarningRecovery; reached cut loses warning content on recovery; 9 | 20 |

Shorthands: Approval = AgentTaskLandApprovalRequestTests; Adoption = AgentTaskLandAdoptionTests;
Consistency = ReviewEvidenceConsistencyTests; Settlement = ReviewEvidenceSettlementTests;
Resolver = AgentTaskWorktreeBaseResolverTests; Dispatch = AgentTaskDispatchBaseGuardTests;
Runner = RunnerTaskSettlementTests; Continuation = TaskCompletionContinuationTests;
CompletionDelivery = CompletionWarningDeliveryTests.

Mutate the actual equivalent seam if a landed refactor changes its spelling; record the diff.
Build failure, unreached hook, wrong/zero roster or infrastructure error is never intended red.
PCs sharing a file/method run separately. Preserve exact bytes, refresh restored timestamps,
build fresh phase outputs, await every owned command and retain baseline/red/restored-green
TRX with expected assertions. Use the copied unchanged checkpoint driver and external
SourceLanding evidence root per docs/testing-and-build.md. No snapshot commit/push/land or
external executor. Record C/O/L, argument counts, logs and restoration; run missing-control
discovery. A's PCs use A's L and B's PCs B's L; never relabel earlier evidence.

### Out of scope

No evidence inference/rewriting, parser/Review bundle, schema, adoption mechanics, live provider
or push debugging, broad reply/land/full-assembly run. CARD-0807 owns its diagnostic and repair
prose. An unusable block cannot simultaneously bind: coexistence tests use another real warning.
Native Windows transport is excluded because content/selection changes do not alter transport;
the controlled adapter's limit is explicit above. Live activation/canaries remain caller-owned.

### Cost

All times are Linux estimates. Ordinary Code floor is the checkpoint-minute sum: A =
15+15+30+18+25+25 = **128 minutes**, B = **35 minutes**, total **163 minutes**, including
two isolated builds. Authoring and host-slot waiting are additional, separately quoted.
PC cycle estimates include method baseline, compiling-defect build/red and fresh restored
build/green: **232 minutes** (A **156**, B **76**). Add 20 minutes for two snapshot
setups/restoration records and 20 for discovery/reporting: Mutation floor **272 minutes**;
ordinary plus Mutation floor **435 minutes**. No measured savings claimed. Reusing CP-1
across five A filters avoids five redundant assembly builds; required delivery coverage adds cost.

Expected ordinary baseline reds: new S1 messages/detail fail on missing identities; S2
mismatch/receipt cases fail on absent warnings; S3 trace ceilings fail on redundant commands;
updated V30/Review cases fail on silent target dispatch; S5 no-push/receipt cases fail on absent
progress=none. Supporting negative/regression cases may already pass; their assertions must
still detect the specified guard mutations. Code does not run deliberate mutation cycles.

### Roster

Source-expanded counts include partial files. Future minimum executions are not execution
claims. Methods invoking tests or looping assertions still count once.

| Class | Baseline methods/results | Added results | Final results |
|---|---:|---:|---:|
| AgentTaskLandApprovalRequestTests | 19 / 21 | 2 | 23 |
| AgentTaskLandAdoptionTests | 9 / 15 | 6 | 21 |
| AgentTaskWorktreeBaseResolverTests | 10 / 46 | 21 | 67 |
| WorktreeBaseSelectionTests (both partials) | 14 / 16 | 0 | 16 |
| AgentTaskDispatchBaseGuardTests (both partials) | 39 / 74 | 13 | 87 |
| RunnerTaskSettlementTests | 15 / 15 | 8 | 23 |
| RunnerCompletionProgressTests | 8 / 8 | 0 | 8 |
| TaskCompletionContinuationTests (Unit) | 24 / 41 | 7 | 48 |
| AgentTaskReviewEvidenceTests | 16 / 16 | 0 | 16 |
| ReviewEvidenceSettlementTests (CARD-0807 inspected ref) | 11 / 11 | 0 | 11 |
| ReviewEvidenceConsistencyTests | 0 / 0 | 31 | 31 |
| CompletionWarningDeliveryTests | 0 / 0 | 11 | 11 |
| LandEvidenceWarningDeliveryTests | 0 / 0 | 7 | 7 |

CP floors rise: CP-2 **36 -> 44**, CP-3 **55 -> 170**, CP-4 **24 -> 31**,
CP-5 **33 -> 58**. CP-1 retains **3000**; all seven new shape rows and existing 41 continuation
rows must execute. CP-6/CP-7 floors are 11/7. Check each intended method/argument roster, not
just totals. If CARD-0807/base adds cases, raise floors before B's commit; never lower them or
allow unrelated added cases to hide missing required methods.

### Checkpoints

Commit each complete group before its one tool run. Through scripts/build-slot.ps1 launch
dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this-plan> with
--rows CP-1,CP-2,CP-3,CP-4,CP-6,CP-7 for A; --rows CP-5 for B after the landing dependency.
Each row acquires its own driver lease. Resume tool wait while exit is 75; retain/report
CP lines, actual counts, failures/skips and reruns. Linux UseAppHost=false is the tool default;
no runner/platform pin. Serial rows run alone. Missing named cases or unreached fault hooks
fail even when the count floor passes. These are the closed ordinary rows.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1,S3,S4,S5 | `tests/Antiphon.Tests -> bin-c788a/` | unit | `/*/*/*/*[Category=Unit]` | R-6, V-5 (shape) | >= 3000 executed, 0 failed; all named shape rows | 3000 | 15 | false |
| CP-2 | S1,S3,S4,S5 | `CP-1` | land-admission | `/*/Antiphon.Tests.Application/(AgentTaskLandApprovalRequestTests*)\|(AgentTaskLandAdoptionTests*)/*` | V-1, R-1 | all listed, 0 failed/skipped | 44 | 15 | true |
| CP-3 | S1,S3,S4,S5 | `CP-1` | base-inspection | `/*/Antiphon.Tests.Application/(AgentTaskWorktreeBaseResolverTests*)\|(WorktreeBaseSelectionTests*)\|(AgentTaskDispatchBaseGuardTests*)/*` | V-3, V-4, R-3, R-4 | all listed, 0 failed/skipped | 170 | 30 | true |
| CP-4 | S1,S3,S4,S5 | `CP-1` | progress-warning | `/*/Antiphon.Tests.Application/(RunnerTaskSettlementTests*)\|(RunnerCompletionProgressTests*)/*` | V-5, R-5 | all listed, 0 failed/skipped | 31 | 18 | true |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c788b/` | evidence-consistency | `/*/Antiphon.Tests.Application/(ReviewEvidenceConsistencyTests*)\|(ReviewEvidenceSettlementTests*)\|(AgentTaskReviewEvidenceTests*)/*` | V-2, V-8, R-2 | all listed, 0 failed/skipped | 58 | 35 | true |
| CP-6 | S1,S3,S4,S5 | `CP-1` | progress-delivery | `/*/Antiphon.Tests.Application/CompletionWarningDeliveryTests/*` | V-6 | all listed, 0 failed/skipped | 11 | 25 | true |
| CP-7 | S1,S3,S4,S5 | `CP-1` | refusal-delivery | `/*/Antiphon.Tests.Application/LandEvidenceWarningDeliveryTests/*` | V-7 | all listed, 0 failed/skipped | 7 | 25 | true |

## Platform, execution and rollout

`GET /api/runner-defaults`: `globalRunnerId = server2`, no kind defaults. `GET /api/session-runners`:
`desktop` (windows) and `server2` (linux), both dispatch-eligible. Nothing here is OS-specific;
omit `-Runner` and `-Platform`. Builds go through the build-slot gate or the checkpoint tool.
Activation after land: `GET /api/version` SHA check on the desktop server, then one real
`delegate.ps1 -Land ... -FromTask` refusal with wrong-subject evidence read from `-Status` to
confirm the new sentence, and one same-card Review create preview on a card with more than six
retained branches to confirm `GitCommands` and no `inspection_timeout`.

## Planning record

- Board read: CARD-0788 revision 4 including CARD-0807's correction; CARD-0603, CARD-0675,
  CARD-0417 (Done), CARD-0649 (Done). No board write was made by this Plan.
- Live records read: tasks `ec59b256`, `8532af35`, `fb8fa4a9`, `ad020e8c`, `7647cb6a`, `21412cdd`,
  `0e946df2`, `73423389`, `c03af147`, `be4f9e5f`, `cc0fc72f`; transcripts of sessions
  `f39d3030` (ec59b256) and `7e92376a` (7647cb6a); the board task list for candidate counts.
- CARD-0807 plan and code read from `origin/feat/card-task-712e6984f` (not on master).
