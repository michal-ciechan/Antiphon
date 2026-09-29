# CARD-0788: make review-evidence misattribution visible at settlement and land, and keep same-card base inspection inside its budget

Date: 2026-09-29. Stage: Plan; verification design is a separate TestDesign dispatch (the brief
did not fold it in). Baseline inspected: `15b66136a6d2081754935bfdc88139e761ed4e57`
(origin/master at planning time; this branch is at the same commit).

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

## Verification design (draft; TestDesign finalizes ids, floors and PCs)

Ordinary coverage map:

| Id | Proves | Where |
|---|---|---|
| V-1 | Adoption and owner refusals name both identities and the fitting flag; codes unchanged | `AgentTaskLandApprovalRequestTests` C788_* and extended C488_* |
| V-2 | Settlement warns on subject-tip and review-base contradictions, binds anyway, stays idempotent, and the caller's header carries the code | `ReviewEvidenceConsistencyTests` |
| V-3 | Pruned rows cost no git; contained branches classify in one query; the safety probe runs only for maximal tips; decisions unchanged | `AgentTaskWorktreeBaseResolverTests` C788_*, `WorktreeBaseSelectionTests` |
| V-4 | Incomplete Auto-mode inspection blocks for a previewed source or a Review; fresh Code keeps the safe base | `AgentTaskDispatchBaseGuardTests` |
| V-5 | A non-Code runner settlement with no push settles Succeeded with `progress=none` in the event and the note header; Code is unchanged | `RunnerTaskSettlementTests`, `TaskCompletionContinuationTests` |
| R-1 | Landing boundary unchanged (adoption CAS/lease/refusal-before-publication) | `AgentTaskLandAdoptionTests` |
| R-2 | CARD-0807 settlement/adoption feed unchanged | `ReviewEvidenceSettlementTests`, `AgentTaskReviewEvidenceTests` |
| R-3 | CARD-0442/0508/0540 selection semantics unchanged | `T0442_V02`-`V10`, `V29`; C508/C540 |
| R-4 | Other dispatch base guards unchanged | remaining `AgentTaskDispatchBaseGuardTests` |
| R-5 | Runner sync and progress evaluation unchanged | `RunnerCompletionProgressTests` |
| R-6 | Unit lane green | `[Category=Unit]` |

### Cost

Ordinary Code floor is the sum of `EstimatedMinutes` below: group A 70 minutes, group B 20
minutes, plus authoring. Windows estimates are not given; every row runs on Linux.

### Checkpoints

Group A rows (`After` S1, S3, S4, S5) run once after group A is committed; group B (`After` S2)
runs once after S2 is committed on a CARD-0807-inclusive base. Every row is Linux-capable
(real git via `ScratchGitRepo`; `UseAppHost=false` is the tool's Linux default). No row pins a
runner or platform; the lane is whatever `GET /api/runner-defaults` names (server2, linux, at
planning time). Serial rows spawn processes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1,S3,S4,S5 | `tests/Antiphon.Tests -> bin-c788a/` | unit | `/*/*/*/*[Category=Unit]` | R-6, V-5 (shape) | >= 3000 executed, 0 failed | 3000 | 15 | false |
| CP-2 | S1,S3,S4,S5 | CP-1 | land-admission | `/*/Antiphon.Tests.Application/(AgentTaskLandApprovalRequestTests*)\|(AgentTaskLandAdoptionTests*)/*` | V-1, R-1 | all listed, 0 failed | 36 | 12 | true |
| CP-3 | S1,S3,S4,S5 | CP-1 | base-inspection | `/*/Antiphon.Tests.Application/(AgentTaskWorktreeBaseResolverTests*)\|(WorktreeBaseSelectionTests*)\|(AgentTaskDispatchBaseGuardTests*)/*` | V-3, V-4, R-3, R-4 | all listed, 0 failed | 55 | 25 | true |
| CP-4 | S1,S3,S4,S5 | CP-1 | progress-warning | `/*/Antiphon.Tests.Application/(RunnerTaskSettlementTests*)\|(RunnerCompletionProgressTests*)/*` | V-5, R-5 | all listed, 0 failed | 24 | 18 | true |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c788b/` | evidence-consistency | `/*/Antiphon.Tests.Application/(ReviewEvidenceConsistencyTests*)\|(ReviewEvidenceSettlementTests*)\|(AgentTaskReviewEvidenceTests*)/*` | V-2, R-2 | all listed, 0 failed | 33 | 20 | true |

Red/green expectations per row: CP-1 is green before and after (S5's Unit shape test is the only
new Unit method and must first fail against the production line by asserting the `progress=none`
prefix). CP-2's two C788 tests fail on the baseline message text; the extended C488 assertions
fail on the baseline as well. CP-3's `C788_PrunedRows*` and `C788_CheckoutSafety*` fail on the
baseline by command count; `T0442_V30`'s updated arms fail on the baseline by status (Dispatched
vs Blocked). CP-4's new Plan-role test fails on the baseline because no `progress=none` warning
exists. CP-5's mismatch tests fail on the baseline because no warning event is written. A new
test that cannot be made red this way is a stub.

Positive controls are executed by the Mutation stage after land, method-scoped, from the list
in the handoff section; Code and Review report only the rows above.

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
