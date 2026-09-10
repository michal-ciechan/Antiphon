# CARD-0478: Run Mutation after Review and landing

Date: 2026-09-10. Stage: Plan. Verification design is a separate TestDesign dispatch.
Inspected checkout: `9a913f012785728a6a75146adc54b3aa75293520`.

The default becomes Code -> Review -> Land -> asynchronous Mutation in a fresh worktree at the confirmed landed commit. The original implementation card may be Done while its linked verification card remains open. Mutation findings create linked remediation work; they never silently reland the original branch or revert master.

The operator has explicitly accepted that a guard regression can remain on master until this battery detects it. This changes the timing of deliberate positive controls, not the requirement to implement tests, run ordinary V/R, review the diff, or obtain valid landing evidence. The brief cites CARD-0475's 179 PCs / 530-minute estimate and CARD-0461's 117 PCs / 479-minute estimate as the motivating pre-land delays; these are supplied estimates, not measurements made here.

## Ground truth

| Assumption | Inspected behavior | Consequence |
|---|---|---|
| Mutation is an executable gate in the handoff parser. | `PipelineHandoff.cs` parses destinations, maps stage roles, and enriches reports; it does not enforce a transition graph. `DelegationReportFormatter.StageHandoffContract` already accepts `review`, `land`, `mutation`, `decide`, and `none`. | Preserve the vocabulary and ordinals. Change stage instructions and explicit orchestration recipes, not the parser's meaning. |
| Review already runs on every implementation. | `stage-code.md` says `next: mutation`, even with zero PCs. `stage-mutation.md` selects Review only for hard/safety-critical work, requested Review, or substantive deviations. The loop and delegate skill describe Review as optional. | Make ordinary Review the default for every Code completion. |
| Review can run before Mutation with its current instructions. | `stage-review.md` requires judging Mutation evidence. Its asynchronous-delivery audit also requires tracing V/R/PC evidence and rejects missing delivery coverage. | Remove dependence on executed PCs while retaining ordinary-test evidence, test-design/guard inspection, and delivery-audit obligations. |
| A new worktree will contain exactly the reviewed commit. | `DelegationWorktreeService.CreateForTaskAsync` chooses `MergeTargetRef ?? HEAD`; normal dispatch uses target/master lineage. `IWorktreeManager.CreateAsync` can take a commit, but there is no public independent snapshot selector in `CreateAgentTaskRequest` / `delegate.ps1`. | Add a narrow confirmed-landing selector. Do not equate current master HEAD with the commit this battery must test. |
| The Code SHA is the landed SHA. | `AgentTaskLanding` retains original, rebased, verified, and observed remote SHAs. `AgentTaskLandingState.HasPublication` validates structured containment evidence. Cleanup is a separate fact. | Pin the battery to `VerifiedSourceSha` of a confirmed publication operation, not `OriginalSourceSha` or a later `ObservedRemoteTargetSha`. |
| Mutation can use a normal Worktree task unchanged. | Successful Worktree settlement calls `TryMergeBackAsync`; that method calls `CommitAllChangesAsync` even when no merge target is set. | A fresh Mutation worktree needs a no-auto-commit/no-merge settlement path. A leftover mutant must never become an automatic commit. |
| Closing the original card can also express pending verification. | `CardWorkTransitionService` skips Done/Canceled/NeedsDecision cards. `AgentTaskPipelineStatusService.BuildReady` excludes terminal cards; open tasks are projected separately. Review -> Done remains an explicit move. | Give pending verification its own ordinary card. Do not depend on a Done card producing a new Mutation-ready row. |
| A finding row moves or reopens a card. | `StageOutcome` is evidence only. A decision belongs on a move/reopen revision and the attention feed. | The orchestrator must record and route findings explicitly; a finding marker alone is not remediation tracking. |
| Background means the worker can leave commands running. | `delegate-basics.md` requires foreground ownership and awaiting every command. | Only the caller proceeds asynchronously. Each Mutation worker still supervises its full battery to settlement. |
| Updating the four requested prose files is sufficient. | Code, TestDesign, delegate-basics, the orchestrator bundle, bundle tests, and the testing owner also carry ownership assumptions. Bundles are embedded and applied at launch. | Update all active instructions coherently and prove loaded composition during rollout. Historical plans/reports remain historical. |

## Decisions

### D-1: Ordinary Review is mandatory before land

The implementation path is:

```text
Investigate (when needed) -> Plan -> TestDesign (separate for medium/hard)
  -> Code: implement tests + ordinary V/R + commit/push; next: review
  -> Review: read-only diff/design/ordinary-evidence judgement
       defects -> next: code, using the original retained Code branch
       clean   -> next: land, carrying the original Code landing owner
  -> caller: record verification obligation, then -Land <code-task-id>
  -> confirmed publication: dispatch Mutation on the verification card
                            deploy/close original card without waiting for PCs
  -> Mutation: clean -> next: none; findings -> next: decide for triage
```

Code keeps reporting each V/R outcome and every PC/variant pending, including a zero-PC inventory. It never defaults directly to land. Review inspects the plan's guard inventory and whether ordinary tests exist and were exercised. It may reject an absent regression test or inadequate ordinary evidence immediately; it must not require deliberate red/restore/green execution before approving the diff. It does not introduce PC execution under a different name. Review runs the relevant claimed ordinary tests as its present contract requires.

Review is separate for easy/medium/hard. Excluding optional Investigate and mechanical helpers, the dispatch counts become 4 for easy (Plan with folded TestDesign, Code, Review, Mutation), 5 for medium, and 5 for hard; Investigate adds one. Plan remains mandatory. Hard/safety-critical labels affect review depth, not its placement. Existing landing verification, conflict handling, and explicit required deployment/acceptance criteria remain in force.

Rejected: retain optional Review (does not implement the requested ordinary Review gate); run a small mandatory PC subset pre-land (reintroduces a PC gate); rename Review to Verify (breaks an existing alias).

### D-2: The landing outcome, not another report token, starts the background branch

Review reports `next: land`. The caller orders the original Code task's landing and waits for the normal correlated outcome. A confirmed `Landed`, `AlreadyPresent`, or `LandedWithResidue` publication is eligible; cleanup residue does not delay the battery. `Queued land`, task Succeeded, a local target advance, a push exit code, historical event prose, and `LandRefused` are not publication evidence.

Use the structured operation ID and the existing `HasPublication` policy. Define:

- `C`: ordinary-tested and reviewed Code SHA.
- `O`: confirmed landing operation ID for the original Code task.
- `L`: `O.VerifiedSourceSha`, the committed source whose remote containment is established.
- `R`: `O.ObservedRemoteTargetSha`, which may contain commits after L.

Mutation tests L. It may test a rebased L different from C; retain both identities and the original plan/V/R/Review evidence. Existing land verification handles changed-base verification. A conflict repair or substantive implementation change still needs ordinary V/R and Review before a new land request. A later advance or revert of master does not silently change an already commissioned battery's source.

No `post-land`, `done`, or `background` next-stage token is added. Land is an operation, not an AgentTaskRole. Its caller-side continuation dispatches Mutation using the explicit recipe below; there is no synthetic stage report and no rewriting Code's stored `next: review` into `mutation`. Existing `mutation` tokens continue to parse for historical reports and explicitly commissioned retries.

### D-3: An ordinary verification card is the durable pending record

Before requesting the implementation land, the orchestrator records one linked verification card on the same board. Title: `Post-land verification: <original identifier>`. Use existing card APIs/`card.ps1`, full board/card GUIDs for durable identity, and label `post-land-verification`. The description contains a stable key `post-land-verification:<original-code-task-guid>`, original card and Code/Review task IDs, reviewed C, plan path, pending PC inventory, commissioning project (including a null project), and `publication pending`. Link the verification card back from the original card with a content revision. Do not replace an existing description wholesale or overwrite human metadata.

The companion is an ordinary Backlog card until a task dispatches; it is not a new column, card status, or task role. It is an explicitly commissioned continuation, so picking rules exclude these cards from fresh feature Plan/Code work. Do not Spawn it as a card-owned session. The description is the queue of work still owed before an AgentTask can be accepted. The visible card survives a caller interruption or a 409 capacity/provider refusal.

After the landing outcome, update that same card with O/L/R and dispatch Mutation bound to its GUID. Record the accepted Mutation task ID in a revision. Once accepted, the normal durable AgentTask queue, checks, reports, and card transitions own running-state visibility. If dispatch is refused, retain the pending card and record the reason and next action. The original card can still close; a missing dispatch is not a passing battery.

This is an explicit orchestrator workflow, not a new service that creates cards or spends model quota from an orchestration tick. On resumption, inspect the linked card/thread and task records before creating or dispatching again. A missing response is not authority to create a duplicate. The stable key makes interrupted creation discoverable; it is not a database uniqueness guarantee. The commissioning orchestrator serializes these actions. If conflicting duplicate companions exist, select the authoritative record and reconcile explicitly before dispatching.

Rejected: a memory-only promise to run PCs later (lost after compaction/restart); an attention-only pending item (not a work queue); a new verification table/automatic dispatch engine (unnecessary for this explicit workflow); marking the original card InProgress until hours of PCs finish (keeps completion tied to the removed gate).

### D-4: Done means the implementation shipped; verification has its own outcome

The original card may be moved to Done once ordinary V/R and Review have passed, publication is confirmed, the verification obligation is durably recorded, and any other explicit card acceptance/deployment conditions are satisfied. Its close reason states C, O/L, the Review task, and `post-land Mutation pending: <verification-card>`. Do not say fully verified or PC-clean.

There is no `Landed, verification pending` workflow status. That phrase is descriptive text on the original card/verdict. The verification card stays open until its obligation has a terminal disposition:

| Verification outcome | Card/report action |
|---|---|
| All PCs/variants and missing-control discovery complete, restored clean | Mutation reports `done`, `next: none`; caller closes verification card with L, counts, evidence references, and restoration verdict. |
| Confirmed coverage gap or product defect | Mutation reports `done`, `next: decide`; caller creates linked remediation card(s), records their IDs on the verification card, and keeps verification open. A completed investigation is not a clean battery. |
| Incomplete battery, infrastructure failure, interruption, or unknown source | `failed` or `blocked` as appropriate, with remaining IDs and recovery facts. Keep verification open; absence of evidence never becomes clean. |
| Explicitly canceled/superseded verification | Record the operator/caller-authorized disposition and successor card/task/landed SHA. Use Canceled when abandoning a run, not Done/Clean. |

Do not reopen the original Done card automatically. Its shipped history remains accurate. A human can reopen it through the existing explicit route if that better describes a changed acceptance decision. No card movement or finding creates an unsolicited alert-sink message.

### D-5: Add a narrow landing-bound Worktree dispatch seam

Proposed public surface: `delegate.ps1 -SourceLanding <full-operation-guid>`, serialized as nullable `sourceLandingOperationId` on `CreateAgentTaskRequest` and persisted on `AgentTask`. This flag does not exist yet. It is required by the new default Mutation recipe, not an arbitrary `-BaseRef` that can select unlanded code.

Admission and dispatch requirements:

1. Accept only a Worker with Role Mutation and Workspace Worktree. Reject ReadOnly, Shared, Orchestrator, OnAgent/standing Agent, or an explicit merge target for this mode. Resolve normal authorization, commissioning project, repository roots, card binding, pins, provider availability and capacity; the source operation cannot grant access or move the request to a different project's capacity bucket.
2. Load the named landing operation and its source task; require structured `HasPublication`, same authorized repository and commissioning project, and a distinct verification card on the original board. A missing binding or binding to the source implementation card is refused for this mode. Select L from the operation, never a caller-supplied SHA or current master HEAD. Preserve the operation with an additive nullable FK using restricted deletion; leave historical task rows null. Expose this identity and L through task detail for recovery/inspection.
3. Persist the source operation identity before provisioning. At dispatch, resolve the same operation and use L as the independent creation base for `IWorktreeManager.CreateAsync` under the existing repository lease. Keep `MergeTargetRef` null even for a nested caller. Do not repurpose `WorktreeBaseSha` as the request: it remains the observed checkout base and must equal L.
4. Use a new managed `card-task-<mutation-id>` branch/directory and a fresh process carrying stage-mutation. Do not reuse Code's worktree, an old Mutation process, the main checkout, or an unverified standing pin. Existing reuse/creation safety checks still apply. A metadata/Git/worktree mismatch on retry preserves the checkout and refuses dependent execution; no reset, rebase, or force checkout to hide it.
5. Recheck HEAD=L and clean tracked source/index before the first mutant. Inventory outputs/untracked files; baseline tests must actually run against L. Fetching/resolving a missing commit may use existing approved Git infrastructure, but never substitute R or the latest branch tip if L is unavailable.

The source relation remains immutable through task retries. A subsequent battery at a repair's new landing operation is a new task on the same verification card, explicitly identifying which earlier attempt it supersedes. Admission refuses a second open Mutation for the same source operation, even if the request names a different companion card; include Queued/Dispatched/Working/Blocked in this check and use a transaction/admission lock in the existing task-create path so concurrent requests cannot both launch it. Return the existing task identity with the refusal. A terminal attempt may be retried explicitly after its restoration/evidence has been assessed. Never automatically launch replacements after provider authentication/quota refusals.

The low-level worktree manager already accepts a commit as a base. Extend the application seam rather than constructing Git commands in the orchestrator or changing the general rule that feature Worktree tasks start from their merge target/master lineage.

### D-6: Snapshot Mutation has no publication or autosave path

Before `CommitAllChangesAsync` or any local merge/rebase, the Worktree settlement path must recognize Role Mutation and return a verification-only result. Guard the underlying worktree service as well as its caller so an alternate settlement path cannot auto-commit a mutant. Explicit `-Land` of a Mutation task is refused. Snapshot source metadata must survive settlement, failure, cancellation and restart. Do not classify a successful Mutation as missing implementation progress solely because it made no commits.

For this post-land mode the worker never commits or pushes from its snapshot, even for plan/evidence amendments. Store logs and restoration records outside its worktree, under a persistent repository-owned verification root keyed by source operation and Mutation task IDs. Its authoritative Result includes the complete PC matrix or references a durable evidence artifact. Any tracked plan/test/production amendment is separate caller-commissioned Docs/Code work on a normal branch with ordinary review/landing. Update delegate-basics to make this explicit exception to generic commit-and-push instructions.

Method-scoped green -> compiling defect -> intended assertion red -> byte restoration -> fresh rebuild -> restored green remains mandatory. Preserve nonzero counts, exact assertion, per-variant red/green paths, SHA, and final source/index checks. Missing-control discovery still runs on zero-PC plans; justify no applicable guard when appropriate. A missing assertion is a coverage defect, not permission to author a repair in the snapshot. Await every owned command and retain contaminated worktrees after interruptions for recovery.

Cleanup is independent of publication and clean/failed test outcomes. Add a verification-specific typed removal purpose and an explicit cleanup entry point (`delegate.ps1 -CleanupVerification <mutation-task-id>`), not permission to call `-Land`. Reuse existing repository leases, process-ownership checks, creation identity and guarded removal infrastructure. Require a terminal task, no live owner/commands, exact managed branch/worktree registration, HEAD equal to L, clean index/tracked/untracked/ignored inventory after removing only task-owned outputs, no sequencer, and durable evidence outside the tree. Unknown files, moved HEAD, dirty source or uncertain children retain residue with an actionable reason. Capture/recheck all deletion coordinates under the lease. No force deletion, fake publication receipt, or transfer of the Code operation's cleanup authority to another worktree.

A restored failed battery can be cleaned with the same explicit checks; task success is not deletion authority. Cleanup failure remains visible through existing residue reporting and does not turn the original land or a completed battery red. The caller records any retained residue in the verification verdict.

### D-7: Findings create remediation work; revert is a deliberate response

Mutation's post-land finding report names Where / Failure / Why / Fix, original card, verification card, C/O/L, offending PC/guard, actual baseline behavior, expected assertion, and evidence. Distinguish:

- Surviving mutant/missing detection: a test coverage gap; it does not by itself prove the current implementation is defective.
- Reproducible failure on unmutated L: a possible landed product regression. Establish pre-existing versus introduced behavior where needed; a red fixture/build/environment is not a product verdict.
- Non-compiling/equivalent/inapplicable mutant or incomplete evidence: repair the verification design or execution; never silently count it killed or recommend a product revert solely for this reason.

The caller reads the full report and explicitly creates a remediation card for each independently actionable issue (group one root cause with its controls). Its description links the original and verification cards, source operation/task, L, repro and severity, and remaining battery scope. Prevent duplicates by checking the verification thread and exact finding/guard identity first. The original card's public record gets a concise link to the finding; preserve its original close verdict.

Default response is forward fix: new Plan/TestDesign as needed -> Code at current target lineage -> ordinary V/R -> Review -> land that repair's Code task. Record the new O2/L2 on the existing verification card and commission a new snapshot pass that proves the repaired detection and covers still-applicable pending controls. A repair can reuse this verification card instead of recursively opening another companion; old Found evidence at L remains historical and is never relabeled Clean at L2.

For an active severe production regression, the caller assesses containment versus forward fix using impact and current target state. A revert is a normal new reviewed commit on a fresh remediation branch, with appropriate ordinary verification and an explicit landing decision. Never reset/force-push master, automatically revert on a surviving mutant, or land the already-cleaned original task again. If containment versus forward fix needs operator judgement, put the concrete question and evidence on the remediation card's NeedsDecision move/reopen revision; existing attention exposes it. Without a real decision, proceed with the authorized forward-fix workflow rather than parking every finding in NeedsDecision.

All completed post-land findings initially use `next: decide` to prevent a generic `next: code` continuation from resuming the original completed card/snapshot. This is caller triage with a concrete report, not a mandatory permission request: answer from existing authority when possible, create/bind the remediation card, then dispatch its named stage. The Mutation worker does not create cards or sub-delegate. Operationally blocked reports use `blocked` and a specific missing prerequisite; do not conflate them with completed findings.

### D-8: Update every active recipe in one coherent release

| File | Required final instruction |
|---|---|
| `server/Bundles/stage-code.md` | Ordinary V/R, commit/push, PC inventory pending; `next: review` for every completed implementation including zero PCs. Retain original landing owner and restart target. Never default to land or Mutation. |
| `server/Bundles/stage-review.md` | Ordinary pre-land read-only review, no executed-PC dependency. Inspect coverage/design and real ordinary delivery evidence; PC results are pending for post-land. `next: land/code/decide` unchanged. Carry the original Code owner. |
| `server/Bundles/stage-mutation.md` | Require confirmed source operation/L, fresh snapshot and source/index checks. PCs and missing-control discovery; no snapshot commits/repairs/land/deploy. Clean `next: none`, completed finding `next: decide`, incomplete runs truthful failed/blocked. Point to loop/testing owners for details. |
| `docs/orchestration-loop.md` | Replace cycle, role table, optional-Review/complexity counts, close-card meaning, and the entire current CARD-0470 Shared-in-Code-worktree recipe. Add D-2..D-7's explicit companion-card, outcome, snapshot, triage and cleanup mechanics. Explain land-outcome continuation and companion-card picking; keep tracker writes explicit. |
| `.claude/skills/antiphon-delegate/SKILL.md` | Reorder recipes Code, Review, Land, Mutation. Update role descriptions, counts/cost ownership, SourceLanding/CleanupVerification options, companion-card binding and landing owner semantics. Remove the retained Code Shared Mutation default and its stale pre-upgrade Debug recipe from active instructions. |
| `server/Bundles/stage-test-design.md` | Preserve guard inventory, all PC variants, delivery inventory and separate costs; specify that Code executes V/R, Review judges ordinary evidence before land, and Mutation executes PCs afterward. |
| `server/Bundles/delegate-basics.md` | Scope generic commit/push/amendment rules to exclude post-land Mutation snapshots; retain foreground, restoration, precise filters and safe sharding rules. Repair work belongs to separate caller-commissioned tasks. |
| `server/Bundles/orchestrator.md`, `server/Bundles/README.md` | State the new default, explicit landing-outcome continuation, companion-card tracking, and triage exception for Mutation's `next: decide`. Keep the ordinary parsed-header rule elsewhere. |
| `docs/agent-card-lifecycle.md`, `docs/testing-and-build.md`, `docs/ops-http.md` | Describe shipped-versus-verification verdicts without changing CardStatus; document snapshot custody/cleanup and public source-operation/cleanup surfaces. |

Keep stage bundles ASCII and within their existing 2,500-character budget; link to owner documents instead of copying this full plan into them. `PipelineHandoff.cs`, `PipelineHandoffKind`, AgentTaskRole and `DelegationReportFormatter` vocabulary need no behavioral change. Pin this compatibility in tests. `verify` continues to alias Review; no OrchestrationStage Mutation is added. Optional `-Stage Verify` remains existing outcome accounting, not a new clean/failed state machine.

The new default recipe, after the extension ships (placeholders are illustrative):

```powershell
# Ordinary pre-land Review: retained Code worktree, full Code report and plan in the file.
$reviewGoal = Get-Content -LiteralPath '<review-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Review -Card <original-card-guid> -ReadOnly -Dir '<code-worktree>' -Title 'review implementation' -Goal $reviewGoal

# After Review's next=land: record/link companion through card.ps1, then order publication.
pwsh -NoProfile -File scripts/delegate.ps1 -Land <original-code-task-id>

# Only after a confirmed publication outcome, using that operation's full GUID.
$mutationGoal = Get-Content -LiteralPath '<mutation-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Mutation -Card <verification-card-guid> -Worktree -SourceLanding <operation-guid> -Title 'post-land mutation checks' -ExpectAbout <pc-floor-plus-analysis> -Goal $mutationGoal

# After settlement/restoration and durable evidence: explicit guarded cleanup, never another land.
pwsh -NoProfile -File scripts/delegate.ps1 -CleanupVerification <mutation-task-id>
```

The Mutation brief carries the previous Review handoff verbatim, original/verification card and Code/Review task IDs, C/O/L/R, plan and full reports/evidence, complete pending PC inventory, restart target for context, and the persistent evidence root. Use the same commissioning project; directory/card choice must not silently change it. Background Mutation never performs the restart.

### D-9: Rollout preserves active work and old report meanings

Land this plan and a separate TestDesign artifact before Code. Do not deploy a prose-only subset: SourceLanding validation, no-autosave/no-land protection, guarded cleanup, updated instructions and their tests ship together. The new metadata is additive; no historical reports/tasks, stage ordinals, card statuses, pins, holds or quotas are rewritten.

For CARD-0478's own Code dispatch on the old server, explicitly override the old stage-code bundle in the file-backed brief: implement all slices, run V/R, commit/push and return `next: review`. This token is already understood. Give its ordinary Review the explicit pre-land contract. After Review, the caller records the verification card and lands Code. Deploy through the canonical main-checkout runbook and verify the loaded source-selector behavior and new Code/Review/Mutation bundle composition directly. Health alone is insufficient. Then use the new SourceLanding recipe for CARD-0478's own post-land battery. No ad hoc reset-based or legacy Shared fallback is the default bootstrap path.

The planning brief emitted by this runtime omitted `mutation` from its displayed vocabulary although the inspected formatter includes it. This reinforces checking loaded behavior instead of assuming source checkout equals running version; this plan's own next token is existing `test-design`.

For currently running CARD-0475/CARD-0461 or other batteries, finish/restore the active mutant and await owned commands before changing the workflow. Obtain the full current report and source/restoration state through the responsible delegate. If adopting the new order explicitly, preserve completed evidence at its tested SHA, obtain ordinary Review, record the outstanding companion obligation, then land. The post-land pass must test the actual L, with affected controls rerun after rebase/code changes; evidence for C must not be silently relabeled L. Do not kill active cycles, cancel tasks, or clean retained worktrees merely to free the land gate. Reuse a valid completed pass at the exact same commit only with recorded identity and applicability; missing controls still remain owed.

Rolling back instructions affects future dispatches only. Keep additive metadata and safe snapshot handling while any sourced tasks remain; rolling an older binary back over them requires first restoring/settling those tasks. No live rollout, card transitions, dispatch, restart, or active-battery intervention is performed by this Plan task.

## Implementation slices

| Slice | Files and behavior | Required test surfaces |
|---|---|---|
| S1: Persist and admit a confirmed source | `server/Domain/Entities/AgentTask.cs`; `server/Application/Dtos/AgentTaskDtos.cs`; `AgentTaskService.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated migration and model snapshot; `scripts/delegate.ps1`; task-detail DTO/client type parity. Nullable source operation FK, structured publication/authorization/workspace checks, same-operation open-task admission, visible provenance. | New `PostLandMutationAdmissionTests`, existing `AgentTaskServiceIntegrationTests`, `MutationAdmissionTests`, script-body tests in `RoutingPinScriptTests`, schema/HTTP parity. |
| S2: Exact snapshot and safe settlement | `DelegationWorktreeService.cs`, `AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`, `AgentTaskLandService.cs`; nearest no-progress/recovery owner if needed. Create at L independently of merge target, compose fresh bundle, validate recovery, prevent all automatic snapshot commit/merge/publication paths. | New `PostLandMutationWorktreeTests`, `MutationDispatchTests`, `DelegationWorktreeTests`, `DelegateBundleLaunchTests`, no-progress/retry regression tests and a real temporary Git repository. |
| S3: Verification-only cleanup | `WorktreeRemovalRequest.cs`, `server/Infrastructure/Git/GuardedWorktreeRemoval.cs`, `IWorktreeRemovalEvidence.cs` / infrastructure evidence implementation, narrow task cleanup service/endpoint and `delegate.ps1` verb. Explicit identity-checked cleanup; evidence and unknown children/files retained. Follow existing boundary/style conventions rather than placing Git in the endpoint. | New `PostLandMutationCleanupTests`, existing `WorktreeRemovalAuthorityTests`, `WorktreeManagerTests`, landing-removal regressions. Exercise wrong task/path/repo/SHA and interruption boundaries, not just a success fake. |
| S4: Active stage contracts and workflow | All files in D-8's table; update `InstructionBundleTests`' CARD-0470 substring expectations where ownership/order changed. Preserve still-valid legacy role/token/dispatch tests. Add role-aware pre-land and post-land recipe coverage. | `InstructionBundleTests`, `DelegationReportFormatterTests`, `PipelineHandoffParseTests`, launch composition tests, and a targeted consistency audit excluding historical plans/reports. |
| S5: End-to-end acceptance and rollout record | New `PostLandMutationWorkflowTests`, focused card/pipeline tests, updated operator docs and durable verification evidence. Prove original Done and companion progress independently; verify loaded runtime after the canonical deployment. | Actual task-create/dispatch/settlement and card services with isolated storage/runner, existing land notification and session delivery fixtures; bounded live post-deploy probe owned by caller. |

Do not add a general base-ref picker, a new workflow engine, automatic card creation on ticks, a full PC-result parser, a new board status, or fleet-wide model/scheduling policy. A new required worktree/cleanup mechanism beyond these narrow seams returns to Plan rather than bypassing existing guards. The exact files for new fixtures/endpoints are implementation choices within the named owners; existing guard paths must be read before editing them.

## TestDesign handoff

This section names acceptance obligations, not executed tests or an executable Verification design. TestDesign must inspect the named fixtures/helpers and append the required `## Verification design` with V/R IDs, independently bypassable guards, exact assertion methods and every compiling PC variant. Do not turn this into a text-only verification plan.

1. Code settlement with pending PCs routes to Review, including zero-PC plans. Review approval routes to land without a Mutation task/report. Review failures still return Code and preserve the original landing owner. Legacy `next: mutation`, `verify -> review`, unmarked/unknown handling and helper behavior remain compatible.
2. A real temporary Git landing rebases C to L, then the target advances to R. SourceLanding creates at L after Code's worktree/branch have been removed. Cover Landed, AlreadyPresent, publication with residue, and refused/unconfirmed/legacy/malformed evidence. Neither source C nor latest R can satisfy the exact L assertion.
3. Invalid role/mode, source authorization/project/board/repository mismatches, explicit merge target, missing source and same-source concurrent creates are rejected before launching or writing Git. Persisted queued tasks and restarts keep the same source identity. Normal Code worktree lineage is unchanged.
4. Source Mutation launches a fresh supported-provider composition in its own managed path; no Code checkout, OnAgent/standing process, warm stale bundle or shared writer fallback. Code on another card may proceed while Mutation runs, subject to unchanged global/project/provider limits and real shared test-resource constraints.
5. Through actual settlement, a dirty deliberately mutated Worktree report must not produce an automatic commit, merge, push, land, or deletion. A restored successful no-commit report is legitimate work. Exercise alternate settlement/retry/cancellation paths, direct lower-service entry and explicit -Land refusal, not only a bundle string.
6. Cleanup checks the right task/registration/Git directory/source SHA/creation identity and all file/process boundaries under a lease. A move, dirty file, unknown output or interrupted child retains evidence and source. Logs remain readable after successful cleanup. Cleanup cannot borrow O's authority to remove the Code tree or a sibling.
7. The original card is Done while a separately bound verification task is queued/working/blocked; its status never regresses from companion activity. Verification card/task remains visible. Refused dispatch leaves a recoverable pending card; clean settlement uses next none; a completed finding uses next decide without relanding or reopening the original.
8. Exercise finding triage fixtures for a coverage-only survivor, a reproduced product defect, invalid mutation and unavailable environment. Preserve severity/evidence and original/verification/remediation links. A repaired L2 supersedes the outstanding scope explicitly and does not rewrite L's Found report. Card decisions use revision/attention behavior and emit no alert-sink message.
9. Inventory the existing landing-outcome and Mutation completion delivery paths reused here: durable land operation/request/notification identity to caller, accepted task to worker, settled task/report to caller. Exercise the changed task mode through real queue/delivery services for busy and already-eligible recipients and recovery after enqueue/crash boundaries. Session receipt requires the complete matching UserPrompt transcript, not task Succeeded, queue insertion, Sent, or a transport ACK. State fake-provider limitations; existing delivery mechanics are not redesigned.
10. Cross-file contract audit must find no active default saying Code -> Mutation -> optional Review -> Land, no required executed-PC evidence in ordinary Review, no generic commit/push instruction applicable to sourced snapshots, and no retained-Code-directory Mutation recipe. Preserve all substantive test/guard requirements and stage bundle size/ASCII limits.

Use `dotnet run --project tests/Antiphon.Tests` with the testing owner's isolated output procedure and focused classes. Every PC red/green cycle is exact-method scoped with actual nonzero execution and the intended assertion. Script HTTP fixtures must never dispatch real providers. Process-spawning fixtures use their assembly-local limiter, isolated test schemas and random runner; never production 17204 or concurrent Pty assembly runs. Client type/build checks are required if request/detail types change; use `scripts/test-client.ps1` for affected client tests. No full assembly, real browser or provider spending is implied by this Plan.

Cost must separately name Code authoring plus ordinary V/R, ordinary Review, post-land PC execution plus analysis, and the caller's loaded-runtime acceptance. Give suite/filter-backed numeric floors after fixture inspection. Expected critical-path savings are approximately the old PC floor, minus added ordinary Review and bookkeeping; total verification work is not removed and extra worktree/card handling adds cost. These are scheduling estimates, never deadlines or permission to skip controls.

Plan completion is this committed/pushed artifact. The active guidance and runtime changes above belong to Code after the separate TestDesign stage; none are claimed implemented or tested here.
