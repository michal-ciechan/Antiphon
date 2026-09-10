# CARD-0478: Run Mutation after Review and landing

Date: 2026-09-10. Stage: Plan, with the original TestDesign and a subsequent custody amendment.
Inspected checkout: `9a913f012785728a6a75146adc54b3aa75293520`.

**Current implementation authority:** [Runner custody amendment](2026-09-10-card-0478-runner-custody-amendment.md), written after Code task `62229bff` returned S3 to Plan. Its D-10..D-14 and folded verification design extend this plan's S1-S5. The combined inventory is **230 guards/PCs**, all pending for post-land Mutation; V-1..V-17 and R-1..R-15 apply. The 182-row inventory and cost tables below are the original subtotal, not the current total. Resume Code with both documents after this amendment lands.

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

The process requirement is now supplied by D-10..D-14 in the custody amendment: atomic native Job Object containment, runner-owned receipts bound to each accepted session generation, and a durable task seal freezing every launch attempt. Terminal status, kill success, current process census and worker restoration manifests are not descendant-exit authority. Unsupported backends explicitly refuse; missing/unknown custody retains residue. Reload validated receipts and all attempts through `IWorktreeRemovalEvidence` under the repository lease before any output deletion and at final removal. Cleanup never kills to obtain proof.

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

Land this plan, its completed TestDesign and the runner-custody amendment before resuming Code. The requested custody TestDesign amendment is folded into that Plan artifact. Do not deploy a prose-only subset: SourceLanding validation, no-autosave/no-land protection, runner-owned custody, guarded cleanup, updated instructions and their tests ship together. The new metadata is additive; no historical reports/tasks, stage ordinals, card statuses, pins, holds or quotas are rewritten.

For CARD-0478's own Code dispatch on the old server, explicitly override the old stage-code bundle in the file-backed brief: implement all slices, run V/R, commit/push and return `next: review`. This token is already understood. Give its ordinary Review the explicit pre-land contract. After Review, the caller records the verification card and lands Code. Deploy through the canonical main-checkout runbook and verify the loaded source-selector behavior and new Code/Review/Mutation bundle composition directly. Health alone is insufficient. Then use the new SourceLanding recipe for CARD-0478's own post-land battery. No ad hoc reset-based or legacy Shared fallback is the default bootstrap path.

The planning brief emitted by this runtime omitted `mutation` from its displayed vocabulary although the inspected formatter includes it. This reinforces checking loaded behavior instead of assuming source checkout equals running version; this plan's own next token is existing `test-design`.

For currently running CARD-0475/CARD-0461 or other batteries, finish/restore the active mutant and await owned commands before changing the workflow. Obtain the full current report and source/restoration state through the responsible delegate. If adopting the new order explicitly, preserve completed evidence at its tested SHA, obtain ordinary Review, record the outstanding companion obligation, then land. The post-land pass must test the actual L, with affected controls rerun after rebase/code changes; evidence for C must not be silently relabeled L. Do not kill active cycles, cancel tasks, or clean retained worktrees merely to free the land gate. Reuse a valid completed pass at the exact same commit only with recorded identity and applicability; missing controls still remain owed.

Rolling back instructions affects future dispatches only. Keep additive metadata and safe snapshot handling while any sourced tasks remain; rolling an older binary back over them requires first restoring/settling those tasks. No live rollout, card transitions, dispatch, restart, or active-battery intervention is performed by this Plan task.

## Implementation slices

| Slice | Files and behavior | Required test surfaces |
|---|---|---|
| S1: Persist and admit a confirmed source | `server/Domain/Entities/AgentTask.cs`; `server/Application/Dtos/AgentTaskDtos.cs`; `AgentTaskService.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated migration and model snapshot; `scripts/delegate.ps1`; task-detail DTO/client type parity. Nullable source operation FK, structured publication/authorization/workspace checks, same-operation open-task admission, visible provenance. | New `PostLandMutationAdmissionTests`, existing `AgentTaskServiceIntegrationTests`, `MutationAdmissionTests`, script-body tests in `RoutingPinScriptTests`, schema/HTTP parity. |
| S2: Exact snapshot and safe settlement | `DelegationWorktreeService.cs`, `AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`, `AgentTaskLandService.cs`; nearest no-progress/recovery owner if needed. Create at L independently of merge target, compose fresh bundle, validate recovery, prevent all automatic snapshot commit/merge/publication paths. | New `PostLandMutationWorktreeTests`, `MutationDispatchTests`, `DelegationWorktreeTests`, `DelegateBundleLaunchTests`, no-progress/retry regression tests and a real temporary Git repository. |
| S3: Verification-only cleanup | The custody amendment's S3a native container, S3b host/runner persistence/transport, and S3c guarded removal are required. Extend `WorktreeRemovalRequest.cs`, `GuardedWorktreeRemoval.cs`, `IWorktreeRemovalEvidence` and the cleanup service/API/CLI to consume sealed generation receipts. Unknown children/files and unsupported backends retain residue. | `PtyCustodyTests`, `HostCustodyTests`, `RunnerCustodyTests`, `PostLandMutationCustodyTests`, plus `PostLandMutationCleanupTests` and existing removal tests. V-17 proves real runner receipt to successful guarded removal. |
| S4: Active stage contracts and workflow | All files in D-8's table; update `InstructionBundleTests`' CARD-0470 substring expectations where ownership/order changed. Preserve still-valid legacy role/token/dispatch tests. Add role-aware pre-land and post-land recipe coverage. | `InstructionBundleTests`, `DelegationReportFormatterTests`, `PipelineHandoffParseTests`, launch composition tests, and a targeted consistency audit excluding historical plans/reports. |
| S5: End-to-end acceptance and rollout record | New `PostLandMutationWorkflowTests`, focused card/pipeline tests, updated operator docs and durable verification evidence. Prove original Done and companion progress independently; verify loaded runtime after the canonical deployment. | Actual task-create/dispatch/settlement and card services with isolated storage/runner, existing land notification and session delivery fixtures; bounded live post-deploy probe owned by caller. |

Do not add a general base-ref picker, a new workflow engine, automatic card creation on ticks, a full PC-result parser, a new board status, or fleet-wide model/scheduling policy. The runner-owned custody mechanism in the linked amendment is the approved expansion of these seams after the S3 design return. Further required mechanisms return to Plan rather than bypassing guards. The exact files for new fixtures/endpoints are implementation choices within the named owners; existing guard paths must be read before editing them.

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

Plan completion is the committed/pushed original artifact and its runner-custody amendment with completed verification design. The active guidance and runtime changes belong to Code; none are claimed implemented or tested here.


## Verification design

Added by TestDesign on 2026-09-10 against plan commit `7123a71b`, then amended by the linked runner-custody Plan/TestDesign amendment against `4fbb8e77`. These sections specify executable tests for Code to implement; they do not claim those tests, SourceLanding, or CleanupVerification already exist. D-1..D-14 and S1..S5 are the combined implementation design. Use D-9's file-backed bootstrap override: Code returns `next: review`, ordinary Review precedes land, and the caller dispatches all **230 PCs** to **Role Mutation after confirmed publication and deployment of the complete feature**.

### Inspection

The following bodies and setup were inspected, rather than inferred from test names. New C478 methods below are required additions in `tests/Antiphon.Tests/Application/<Class>.cs`; the names form an execution manifest, not claims about existing discovery.

| Bodies / helper setup inspected | Boundary covered here |
|---|---|
| `MutationAdmissionTests` concurrent-create, role/cap/project/null-bucket cases and CreateService; `AgentTaskLandingStateTests.C448_V31_VerificationEvidenceMustDescribeTheExactCommit`; `AgentTaskLandingState.HasPublication/HasIdentity/HasVerification` | V-1/V-2; R-1/R-2. Existing role gate excludes Blocked, while D-5's same-operation gate must include it. Raise unrelated role/global limits in the same-operation test so they cannot mask that guard. |
| `MutationDispatchTests.RetainedLaunch`, process-cap/writer-lease bodies, CaptureFactory/Harness/Tick; `DelegateBundleLaunchTests.C470_mutation_claude_and_codex_launch_contract` and spec composition | V-3/V-4; R-3. Old retained-Shared cases are historical compatibility tests, not the new default. Fake adapters prove composed launch arguments without paid providers. Add sourced-worktree cases separately. |
| `DelegationWorktreeTests` no-merge-target, no-change, still-dirty, commit-failure and missing-registration bodies; `DelegationWorktreeService` constructor; reply `MergeBackAsync/PersistDeliverThenReleaseAsync/DeliverToParentAsync`; `AgentTaskReplyIntegrationTests` marked/unmarked/wrong-marker/interrupted turns; `AgentTaskCatchUpSettlementTests` catch-up bodies | V-3/V-5; R-3/R-4. Null merge target currently still autosaves. Real settlement must be tested; setting Status directly is insufficient. |
| `LandingGitFixture` initialization, independent observer, capture, isolated Git configuration and disposal; `LandingSafetyHarness` scoped graph/restart, landing, save/transaction fault hooks and crash-worker entry | V-2/V-3/V-6/V-9. Construct distinct C/L/R in an owned bare-remote world; preserve original remote source ref. ControlledVerifier proves invocation/landing sequencing, not actual test correctness. |
| `LandingRemovalPolicyControlTests.C448_V18_EachContentReadingRefusesBeforeItsNextCommand`, durable-authority re-read and `C448_V36_EachAuthorityCoordinatePrecedesMutation` plus RemovalFixture | V-6; R-5/R-6. Mutation-capable downstream fake exposes the first destructive call so another Git guard cannot conceal a missing application guard. Pair with real Git sentinels. |
| `CardWorkTransitionServiceTests` dispatch, success, sibling-open, blocked and failed/canceled bodies; `MutationPipelineTests` settlement, projection, storage/HTTP and seed helpers | V-7/V-8; R-7. Companion has separate GUID/board binding, explicit close and independent task projection. |
| `AgentTaskLandNotificationRecoveryTests` destination/retry, boot scan, keyed queue race and land-vs-report bodies; `AgentTaskLandReceiptTests` false receipts, catch-up/receipt-save and retention bodies; `AgentTaskLandNotificationPersistenceTests` outcome and explicit cleanup bodies | V-9; R-8. The receipt helper defaults to LandRefused: do not use that seed for successful SourceLanding admission. Create a real confirmed operation. |
| `BridgeQueueHarness` service registration, options and `OnSubmitted` callback; `AgentTaskLandNotificationService.ReconcileAsync`; reply persistence/delivery methods above | V-9/V-10; R-8/R-9. The default fake inserts stamped UserPrompt and TurnEnd. Disable/replace it for negative, partial and late-receipt cases; otherwise the fixture manufactures the evidence being withheld. |
| `RoutingPinScriptTests.AssertRetainedDispatch` and stub assertions; `DelegateScriptRunner.RunAsync` argument/environment isolation; `InstructionBundleTests.C470_composed_roles_separate_vr_from_pc` and catalog/composer bodies; `ProductionRunnerGuard` | V-1/V-11/V-12; R-10/R-11. Execute real PowerShell against loopback stub HTTP. Inspect whole embedded composition after rebuild, not checkout HEAD as loaded-runtime evidence. |

**Required fixture additions, within the planned seams.** Add a C478 test world combining the existing isolated Postgres schema, LandingGitFixture/SafetyHarness and BridgeQueueHarness. Register the real worktree graph through `AddDelegationWorktreeGraph`; replace its fake IWorktreeManager where real Git is required. Keep production card/task-create/dispatcher/reply/land/queue/notification services. The orchestration driver invokes explicit card/create/land operations in D-3 order; it is test code, not a new production workflow engine.

Observe settlement's caller guard with a scoped DelegationWorktreeService registration factory that increments a resolution counter before constructing the real sealed service; start counting after provisioning. This distinguishes entering mergeback from the lower service safely refusing it. Direct lower-service cases bypass the caller. Use recording/mutation-capable **existing I/O interfaces** and deterministic barriers for removal checks; do not add a service interface per class. G-2's isolated model fixture has no land-request/notification or other referencing rows that could mask the source-task FK; demonstrate the corresponding unreferenced task can be deleted.

PostLandMutationPublicationTests and PostLandMutationContractTests are Unit; other new classes are Integration. Classes that start Git, pwsh, a runner or a crash worker carry assembly-local `[ParallelLimiter<ProcessSpawnLimit>]`. Use one isolated schema per world and independent DbContexts for races/restarts. Global sweep cases retain `[NotInParallel]` as in the nearest fixture. Use a real/offset clock for the queue, not a frozen instant with real timers. Migration tests use CLI-generated migrations on disposable schemas. Model/FK PCs additionally build a separate disposable schema from the mutated model (`EnsureCreated`, never over a migrated fixture); ordinary V-1 verifies the actual migration chain.

**Non-masking rule.** Every negative starts from a demonstrated admissible/clean/receivable control and corrupts only the named boundary. Raise unrelated caps for same-operation races; use distinct companion GUIDs and deterministic two-context barriers. Keep other authority coordinates coherent and assert *zero destructive requests at the first forbidden boundary*, as well as byte/ref preservation. A later refusal is not proof of an earlier guard. G-124 deliberately supplies a newer late-settlement/original-task evidence timestamp after the original Done revision; otherwise the timestamp guard would mask removal of the terminal-card guard. Shared duplicate publication predicates are removed together where they represent one invariant; independent predicates remain intact.

### Delivery inventory

Session receipt means the matching **complete UserPrompt** in the correct session after the attempt baseline (sequence, or the existing timestamp/tolerance rule when sequence is unavailable). Flattened LF normalization may match through the real matcher; prefix-only and head/tail splices may not. A complete spill pointer counts only with readable, byte-matching durable full content. The stored task Result stays authoritative when completion distillation is enabled.

| Producer -> destination | Durable identity / persistence boundary | Recovery and decisive receipt |
|---|---|---|
| Explicit land request -> land worker -> caller | Code task ID + request ID + O + terminal event ID + notification ID; terminal event/notification transaction, then keyed SessionQueuedMessage | Land queue/notification hosted recovery and CompletionNoteFlushQueue. V-9a uses actual Request/Run and notifier/queue through caller UserPrompt containing O/L and publication verdict; busy and already-idle callers. |
| Accepted sourced task -> dispatcher -> new Mutation worker | Mutation task ID + O + L + accepted session generation; committed queued row before creation, recorded worktree identity, launch queue and boot attempt | Dispatcher/reconciliation scans and complete task-marked SourceLanding brief in the new worker UserPrompt. V-9b cuts after task commit, provisioning, session reservation/launch enqueue, submit and before receipt persistence. No substitute source or task identity. |
| Mutation settlement -> caller | Mutation task ID + immutable O/L + stored Result/next stage + SourceTaskId/content digest/queue ID; task save precedes enqueue/release | Actual reply settlement, missing-note/check recovery, completion flush, interrupted-attempt/late-confirm recovery. V-9c requires complete correlated caller UserPrompt and byte-identical full report retrieval; busy/idle, long spill and raw/distilled cases. |
| Caller observes outcome -> companion revision/explicit dispatch | Stable key `post-land-verification:<code-task-guid>`, full board/card GUIDs, O/L, accepted Mutation GUID | V-7/V-10 restart the **test driver** before/after create/revision/dispatch acknowledgements and discover existing records. Card thread/revision readback is card-write receipt, not session receipt. Stable text is not a DB uniqueness guarantee. |
| Finding -> caller -> explicit remediation | Stored report/guard ID + O/L + original/verification/remediation GUIDs; move/reopen revision carries decision | V-8/V-10 retrieve full received report, explicitly write linked cards and read thread/attention back. No new alert path; caller judgment is not simulated as autonomous model reasoning. |

**Crash/enqueue matrix:** for each of the first three rows cover before producer commit, after producer commit/before enqueue, after queue commit/before producer acknowledgement, lost wakeup, before submit, after complete UserPrompt/before receipt persistence, and after receipt/before release where that path releases an owner. Pre-commit failure leaves no accepted publication/task/outcome; after commit the obligation stays recoverable. Each applicable cut runs with busy and already-eligible recipients. Recreate services from the same DB, discarding in-memory queues. Use the existing owned crash-worker pattern for at least publication-commit and post-submit cuts. Exception injection without recreating services is not process-restart evidence.

Negative receipts cover wrong session, wrong O/task/notification, old sequence/time, QueueEnqueue/QueuedUserPrompt, Sent/transport ACK/screen-only Delivered, no row, prefix and head/tail splice. After genuine receipt, repeat sweeps twice and require no second submission. Stopped/deleted/missing destination or refusal preserves Result/notification/companion obligation and actionable recovery state. Do not claim exactly-once physical delivery across an unknowable transport cut: evidence resolves receipt; uncertain attempts remain visible.

BridgeQueueHarness is the real application queue/runtime path with a deterministic fake terminal/runner transcript boundary. LandingGitFixture is real local Git with no live remote. These prove application durability/correlation and captured input; they do not prove live provider acceptance, model obedience, external brokers or UI rendering. Retain existing PTY transport cases if transport code changes. V-12 is a bounded caller-owned check of deployed composition/source selection; a live delivery claim still requires native transcript receipt.

### Proves it works now

Code first implements and runs every C478 guard method **unmutated**, as ordinary regression tests. Add the named scenarios below. V-1..V-12 are verification groups; a green build alone does not satisfy them.

| ID | Layer / exact planned scenario methods | Setup and expected observation |
|---|---|---|
| V-1 | DB/API/CLI: `PostLandMutationAdmissionTests.C478_V01_PublicSurfaceAndMigration` | Upgrade disposable pre-change schema containing historical tasks; null source fields survive. Create sourced task through HTTP, reload, assert O/L detail parity and restricted FK. Real delegate.ps1 posts full SourceLanding; CleanupVerification selects only cleanup. Normal Code JSON stays compatible. |
| V-2 | Domain/Git: `PostLandMutationPublicationTests.C478_V02_ConfirmedOutcomeMatrix`; `PostLandMutationWorktreeTests.C478_V02_RebasedSnapshotAfterSourceRemoval` | Landed, AlreadyPresent and LandedWithResidue accepted. Create C, advance target to force rebase to L, confirm land, remove Code source through authorized cleanup, advance target to R. Assert pairwise-distinct C/L/R and snapshot HEAD/base == O.VerifiedSourceSha == L. A later target revert still selects L. |
| V-3 | Dispatch/Git: `PostLandMutationWorktreeTests.C478_V03_CreateRestartAndMissingCommit` | O committed before worktree add; new managed path/branch, clean tracked source/index. Restart after accepted create and after provisioning; only exact identity is adopted. Missing L refuses. Ordinary top-level/nested Code lineage unchanged. |
| V-4 | Launch/capacity: `PostLandMutationWorktreeTests.C478_V04_FreshLaunchAndConcurrentCode` | Real launch specs for supported ClaudeCode/Codex/Grok contain stage-mutation once; new session instead of stale warm/standing candidate. Next-card Code runs concurrently within unchanged global/project/provider/process limits and writer/resource constraints. |
| V-5 | Settlement: `PostLandMutationWorktreeTests.C478_V05_SettlementNeverPublishesSnapshot` | Marked done/none report with deliberate tracked/staged/untracked mutant; real OnTurnEnd, repeat/catch-up, failure/cancel and direct lower-service paths. Assert HEAD/index hash/bytes/local and remote refs/registrations, zero commit/merge/rebase/push/remove and no land request. Restored no-commit success stays Succeeded. |
| V-6 | Cleanup: `PostLandMutationCleanupTests.C478_V06_RestoredTerminalCleanupMatrix`; amendment V-17 | Explicit clean Succeeded/Failed/Canceled runs require durable runner-produced descendant-exit receipts for every accepted generation and a frozen task seal. The native V-17 positive removes exactly the authorized snapshot/ref; external evidence and unrelated trees remain. Dead root/live orphan, unknown/missing/unsupported custody, first/final races and interrupted removal retain residue. Seeded receipts test consumer policy only. |
| V-7 | Card/task/pipeline: `PostLandMutationWorkflowTests.C478_V07_OriginalDoneCompanionOpen` | Append companion/reverse link, land and explicitly close original with pending wording, dispatch companion. Companion Queued/Dispatched/Working/Blocked/Failed/Succeeded stays visible; original never regresses. No automatic Done, new column or spawned card session. Null and explicit projects. |
| V-8 | Triage: `PostLandMutationWorkflowTests.C478_V08_FindingDispositionMatrix` | Separate complete reports for coverage survivor, unmutated-L product defect, invalid/noncompiling/equivalent control and unavailable environment. Preserve severity/evidence and retrieve full report. Driver creates linked repairs only for actionable findings; verification stays open. L2 is a new O2/task; old Found/close history unchanged. Decision is revision/attention; alert sink empty. Proves explicit storage/actions, not automatic classification. |
| V-9 | Real producers/queue: `PostLandMutationDeliveryTests.C478_V09a_LandProducerToCaller`, `C478_V09b_AcceptedTaskToWorker`, `C478_V09c_SettledMutationToCaller` | Execute delivery/crash/recipient matrix. Require complete matching UserPrompt at recipients. LandRefused reaches caller but commissions no battery; confirmed publication with residue may commission one. |
| V-10 | Recovery: `PostLandMutationWorkflowTests.C478_V10_ResumeCommissioningAndTriage` | Lost create response discovers existing stable-key companion; conflicting duplicates prevent driver dispatch until explicitly reconciled. Lost task response discovers same-O task. Quota/sign-in/capacity 409 leaves pending record and no fallback. Resume after task accepted before revision acknowledgement; preserve human metadata/concurrency tokens. |
| V-11 | Contracts/settlement: `PostLandMutationContractTests.C478_V11_ActiveContractAndVocabulary`; `PostLandMutationWorkflowTests.C478_V11_CodeReviewLandHeaders` | Audit every D-8 file, excluding historical plans/reports. Compose full bundles/briefs. Pending/zero-PC Code settles Review; clean Review settles Land with Code owner; defect Review returns Code; clean/finding Mutation settles none/decide. Legacy mutation, verify alias, unknown/unmarked/helpers and ordinals stay compatible; no parser transition graph added. |
| V-12 | Client/compiler and caller rollout | Client build/type check covers source request/detail parity; changed executable client behavior gets focused client tests. After canonical deployment, record deployed SHA, loaded SourceLanding acceptance/refusal and Code/Review/Mutation composition hashes. Inspect commissioned snapshot HEAD==L directly. Health alone fails acceptance; never restart from this worktree. |

Positive boundary arms must reach real downstream behavior: confirmed residue passes admission; restored failed battery can clean; different source operations may launch within capacity; terminal prior attempt allows a **new explicit** same-O task after evidence assessment. Null SourceLanding historical tasks retain allowed modes. Code worktree removal is not a prerequisite when its land retains residue.

### Guards the regression

| ID | Regression / decisive assertions |
|---|---|
| R-1 | G-1..G-26: invalid source/mode/auth/card/capacity refused before launch/Git; same-O admission serialized; immutable source and HTTP/CLI parity. |
| R-2 | G-27..G-55: each structured publication predicate independently invalidated; V-2's real positive C/L/R case prevents blanket refusal from passing. |
| R-3 | G-56..G-70, G-80..G-84 and V-3/V-4: exact snapshot, independent launch, restart and normal Code lineage. Worker-only preflight guards are explicitly contract checks plus the actual source commands below. |
| R-4 | G-71..G-79 and V-5: no settlement mergeback/forbidden Git, legitimate no-commit success, durable evidence/metadata and retained interrupted source. |
| R-5 | G-85..G-114 and V-6: first-forbidden-command cleanup refusal, exact path/ref/bytes and no force deletion. |
| R-6 | G-115..G-123 and V-6: partial cleanup facts, independent verdict, source/process/evidence and symbolic-ref checks. |
| R-7 | G-124..G-133 and V-7/V-8/V-10: original Done history, visible companion, explicit closes/decisions, no tick dispatch/alert. |
| R-8 | G-134..G-148 and V-9a/b: durable land/launch obligations and complete matching destination UserPrompt at each cut. |
| R-9 | G-149..G-161 and V-9c: saved full Result, recovery without duplicate submit and complete/fresh/correct-recipient evidence. |
| R-10 | G-162..G-182 and V-11: active recipes and parser compatibility; ordinary Review cannot become a renamed pre-land PC gate. |
| R-11 | V-12: actual loaded selector/bundle evidence; fixtures cannot substitute for caller rollout acceptance. |

### Guard inventory

The census covers the safety assertions in D-1..D-9, including reused publication predicates and worker/caller instruction boundaries. Each G-n has exactly one PC-n; tests below deliberately target independently falsifiable invariants. The method/defect table is the authoritative PC manifest. A single method may enumerate values of the same predicate with Arguments; retain per-case results and never count a masked argument as a killed control.

Worker-only preflight/caller policy guards (PostLandMutationContractTests) are executable instruction regressions. They do not introduce an unplanned runtime preflight API or claim to prove model compliance. Their ordinary acceptance also includes the specified Git commands and explicit workflow-driver scenarios. If Code discovers additional independently bypassable checks in its final implementation, append guards and PCs before handoff; do not silently bundle them into an existing row.

| Guard | Plan / owner | Safety assertion | Control |
|---|---|---|---|
| G-1 | D-5 / S1 | Named source operation exists | PC-1 |
| G-2 | D-5 / S1 | Structured operation retains its source task relation | PC-2 |
| G-3 | D-5 / S1 | Source mode requires Role Mutation | PC-3 |
| G-4 | D-5 / S1 | Source mode requires Worker kind | PC-4 |
| G-5 | D-5 / S1 | Source mode requires Worktree | PC-5 |
| G-6 | D-5 / S1 | Source mode refuses standing/OnAgent binding | PC-6 |
| G-7 | D-5 / S1 | Caller cannot supply a merge target | PC-7 |
| G-8 | D-5 / S1 | A landing operation cannot grant caller repository access | PC-8 |
| G-9 | D-5 / S1 | Operation and authorized task repository must match | PC-9 |
| G-10 | D-5 / S1 | Commissioning project remains identical, including null | PC-10 |
| G-11 | D-5 / S1 | A sourced task must bind a real verification card | PC-11 |
| G-12 | D-5 / S1 | Verification card differs from source implementation card | PC-12 |
| G-13 | D-5 / S1 | Verification card is on the original board | PC-13 |
| G-14 | D-5 / S1 | Admission requires structured HasPublication | PC-14 |
| G-15 | D-5 / S1 | Only one open battery per operation, across companion cards | PC-15 |
| G-16 | D-5 / S1 | Concurrent same-operation creates serialize admission | PC-16 |
| G-17 | D-5 / S1 | Source mode retains absolute task cap | PC-17 |
| G-18 | D-5 / S1 | Source mode retains project-scoped capacity | PC-18 |
| G-19 | D-5 / S1 | Source mode retains provider quota and sign-in refusal | PC-19 |
| G-20 | D-5 / S1 | Required routing pins still constrain sourced tasks | PC-20 |
| G-21 | D-5 / S1 | O is committed before provisioning | PC-21 |
| G-22 | D-5 / S1 | Retries retain original O | PC-22 |
| G-23 | D-5 / S1 | Referenced source operation cannot be deleted | PC-23 |
| G-24 | D-5 / S1 | Task detail exposes O and L for recovery | PC-24 |
| G-25 | D-5 / S1 | CLI transmits full source-operation identity | PC-25 |
| G-26 | D-5 / S1 | CleanupVerification invokes only the explicit cleanup route | PC-26 |
| G-27 | D-2,D-5 / AgentTaskLandingState.HasPublication | Known structured evidence schema | PC-27 |
| G-28 | D-2,D-5 / AgentTaskLandingState.HasPublication | Nonempty operation identity | PC-28 |
| G-29 | D-2,D-5 / AgentTaskLandingState.HasPublication | Nonempty source task identity | PC-29 |
| G-30 | D-2,D-5 / AgentTaskLandingState.HasPublication | Original source is a valid object ID | PC-30 |
| G-31 | D-2,D-5 / AgentTaskLandingState.HasPublication | Target-before is a valid object ID | PC-31 |
| G-32 | D-2,D-5 / AgentTaskLandingState.HasPublication | Source is a heads ref | PC-32 |
| G-33 | D-2,D-5 / AgentTaskLandingState.HasPublication | Target is a heads ref | PC-33 |
| G-34 | D-2,D-5 / AgentTaskLandingState.HasPublication | Source and target refs differ | PC-34 |
| G-35 | D-2,D-5 / AgentTaskLandingState.HasPublication | Destination equals recorded target | PC-35 |
| G-36 | D-2,D-5 / AgentTaskLandingState.HasPublication | Evidence records repository path | PC-36 |
| G-37 | D-2,D-5 / AgentTaskLandingState.HasPublication | Evidence records common Git directory | PC-37 |
| G-38 | D-2,D-5 / AgentTaskLandingState.HasPublication | Evidence records original source worktree | PC-38 |
| G-39 | D-2,D-5 / AgentTaskLandingState.HasPublication | Evidence records original Git directory | PC-39 |
| G-40 | D-2,D-5 / AgentTaskLandingState.HasPublication | Remote fingerprint has required shape | PC-40 |
| G-41 | D-2,D-5 / AgentTaskLandingState.HasPublication | Recovery namespace matches task and operation | PC-41 |
| G-42 | D-2,D-5 / AgentTaskLandingState.HasPublication | Remote containment has a confirmation time | PC-42 |
| G-43 | D-2,D-5 / AgentTaskLandingState.HasPublication | Publication is Landed or AlreadyPresent | PC-43 |
| G-44 | D-2,D-5 / AgentTaskLandingState.HasPublication | L is a valid object ID | PC-44 |
| G-45 | D-2,D-5 / AgentTaskLandingState.HasPublication | R is a valid object ID | PC-45 |
| G-46 | D-2,D-5 / AgentTaskLandingState.HasPublication | Confirmation uses remote read/fetch/ancestry evidence | PC-46 |
| G-47 | D-2,D-5 / AgentTaskLandingState.HasPublication | Verification has an acknowledged timestamp | PC-47 |
| G-48 | D-2,D-5 / AgentTaskLandingState.HasPublication | Verified source recovery pin exists | PC-48 |
| G-49 | D-2,D-5 / AgentTaskLandingState.HasPublication | Verified target recovery pin exists | PC-49 |
| G-50 | D-2,D-5 / AgentTaskLandingState.HasPublication | Rebased source requires prepared pin | PC-50 |
| G-51 | D-2,D-5 / AgentTaskLandingState.HasPublication | L equals rebased source or original if no rebase | PC-51 |
| G-52 | D-2,D-5 / AgentTaskLandingState.HasPublication | Explicit verification or a valid skip is required | PC-52 |
| G-53 | D-2,D-5 / AgentTaskLandingState.HasPublication | base_unchanged skip requires unchanged source | PC-53 |
| G-54 | D-2,D-5 / AgentTaskLandingState.HasPublication | base_unchanged cannot skip requested filter | PC-54 |
| G-55 | D-2,D-5 / AgentTaskLandingState.HasPublication | exact_remote_containment skip requires no rebase | PC-55 |
| G-56 | D-5,D-6 / S2 | Worktree creation uses L, not reviewed C | PC-56 |
| G-57 | D-5,D-6 / S2 | A later target advance cannot select R | PC-57 |
| G-58 | D-5,D-6 / S2 | Nested sourced task never inherits a merge target | PC-58 |
| G-59 | D-5,D-6 / S2 | Snapshot uses a new managed task directory | PC-59 |
| G-60 | D-5,D-6 / S2 | Snapshot has its own managed branch | PC-60 |
| G-61 | D-5,D-6 / S2 | Snapshot launches fresh rather than pooled old Code session | PC-61 |
| G-62 | D-5,D-6 / S2 | Fresh supported-provider launch includes current stage-mutation | PC-62 |
| G-63 | D-5,D-6 / S2 | Snapshot Git creation holds repository mutation lease | PC-63 |
| G-64 | D-5,D-6 / S2 | Retry adopts only the same creation identity | PC-64 |
| G-65 | D-5,D-6 / S2 | Retry rejects an existing checkout at a different HEAD | PC-65 |
| G-66 | D-5,D-6 / S2 | Retry rejects mismatched Git directory | PC-66 |
| G-67 | D-5,D-6 / S2 | Unavailable L never falls back to target tip | PC-67 |
| G-68 | D-5,D-6 / S2 | Tracked source must be clean before first mutant | PC-68 |
| G-69 | D-5,D-6 / S2 | Index must be clean before first mutant | PC-69 |
| G-70 | D-5,D-6 / S2 | Immediately before first mutant HEAD must still equal L | PC-70 |
| G-71 | D-5,D-6 / S2 | Settlement caller never enters implementation mergeback for Mutation | PC-71 |
| G-72 | D-5,D-6 / S2 | Lower service independently blocks snapshot autosave | PC-72 |
| G-73 | D-5,D-6 / S2 | Lower service cannot merge/rebase snapshot | PC-73 |
| G-74 | D-5,D-6 / S2 | Explicit land request for Mutation is refused | PC-74 |
| G-75 | D-5,D-6 / S2 | Recovered/direct land execution cannot publish Mutation | PC-75 |
| G-76 | D-5,D-6 / S2 | Successful no-commit Mutation counts as work | PC-76 |
| G-77 | D-5,D-6 / S2 | Settlement never deletes sourced tree implicitly | PC-77 |
| G-78 | D-5,D-6 / S2 | Failed/canceled/reconciled snapshot keeps source metadata and residue | PC-78 |
| G-79 | D-5,D-6 / S2 | Durable evidence survives removal of snapshot | PC-79 |
| G-80 | D-5,D-6 / S2 | Normal Code worktrees keep target/master lineage | PC-80 |
| G-81 | D-5,D-6 / S2 | Retry requires exact current registration | PC-81 |
| G-82 | D-5,D-6 / S2 | Concurrent Code/Mutation obeys execution process cap | PC-82 |
| G-83 | D-5,D-6 / S2 | Failed snapshot creation preserves unknown hook-written files | PC-83 |
| G-84 | D-5,D-6 / S2 | Terminal source attempt is replaced only by explicit commission | PC-84 |
| G-85 | D-6 / S3 | Removal needs verification-specific typed authority | PC-85 |
| G-86 | D-6 / S3 | Cleanup identity is exact Mutation task | PC-86 |
| G-87 | D-6 / S3 | Cleanup target Role is Mutation | PC-87 |
| G-88 | D-6 / S3 | Cleanup requires terminal task | PC-88 |
| G-89 | D-6,D-14 / S3 | No live session, standing or pool owner may remain, independently of receipt | PC-89 |
| G-90 | D-6,D-12 / S3 | Nonempty tracked job cannot authorize deletion, even after root exit | PC-90 |
| G-91 | D-6 / S3 | Deletion repository matches task creation | PC-91 |
| G-92 | D-6 / S3 | Deletion directory matches managed task path | PC-92 |
| G-93 | D-6 / S3 | Common Git directory matches creation | PC-93 |
| G-94 | D-6 / S3 | Worktree Git directory matches creation | PC-94 |
| G-95 | D-6 / S3 | Managed branch is exact task branch | PC-95 |
| G-96 | D-6 / S3 | Removal requires matching creation identity | PC-96 |
| G-97 | D-6 / S3 | Exact registration must still exist | PC-97 |
| G-98 | D-6 / S3 | Cleanup HEAD equals immutable L | PC-98 |
| G-99 | D-6 / S3 | Cleanup index is clean | PC-99 |
| G-100 | D-6 / S3 | Cleanup tracked files are clean | PC-100 |
| G-101 | D-6 / S3 | Unknown untracked files prevent removal | PC-101 |
| G-102 | D-6 / S3 | Ignored private files prevent removal | PC-102 |
| G-103 | D-6 / S3 | Only exact task-owned outputs may be removed | PC-103 |
| G-104 | D-6 / S3 | Owned-output paths cannot escape approved root | PC-104 |
| G-105 | D-6 / S3 | Active rebase/merge/cherry-pick/revert prevents cleanup | PC-105 |
| G-106 | D-6 / S3 | Cleanup requires readable durable external evidence | PC-106 |
| G-107 | D-6 / S3 | Deletion authority requires current genuine repository lease | PC-107 |
| G-108 | D-6 / S3 | Identity is rechecked under lease before removal | PC-108 |
| G-109 | D-6 / S3 | Contents are rechecked before destructive operation | PC-109 |
| G-110 | D-6,D-14 / S3 | Frozen attempt set, receipts and owner state are reloaded before removal | PC-110 |
| G-111 | D-6 / S3 | Branch deletion is compare-and-swap against L | PC-111 |
| G-112 | D-6 / S3 | Branch checked out elsewhere is not deleted | PC-112 |
| G-113 | D-6 / S3 | Guarded cleanup never force deletes | PC-113 |
| G-114 | D-6 / S3 | Unknown Git/file inspection is not clean | PC-114 |
| G-115 | D-6 / S3 | Interrupted cleanup records partial facts and retries safely | PC-115 |
| G-116 | D-6 / S3 | Cleanup residue does not rewrite publication or battery result | PC-116 |
| G-117 | D-6 / S3 | Cleanup requires Worktree workspace | PC-117 |
| G-118 | D-6 / S3 | Cleanup requires recorded source operation O for this task | PC-118 |
| G-119 | D-6,D-12..D-14 / S3 | Missing, unknown or unsupported custody cannot authorize deletion | PC-119 |
| G-120 | D-6,D-12 / S3 | Gone/reused PID cannot replace original generation/container receipt | PC-120 |
| G-121 | D-6 / S3 | Evidence belongs to O and Mutation task, with complete disposition | PC-121 |
| G-122 | D-6 / S3 | Authority is reloaded after final inspection | PC-122 |
| G-123 | D-6 / S3 | Branch deletion cannot follow a symbolic/ref alias | PC-123 |
| G-124 | D-3,D-4,D-7 / S5 | Companion activity cannot change original Done status | PC-124 |
| G-125 | D-3,D-4,D-7 / S5 | Verification open task remains independently visible | PC-125 |
| G-126 | D-3,D-4,D-7 / S5 | Successful task does not automatically close verification clean | PC-126 |
| G-127 | D-3,D-4,D-7 / S5 | Card transition must not create another session | PC-127 |
| G-128 | D-3,D-4,D-7 / S5 | Newer human disposition wins over old task evidence | PC-128 |
| G-129 | D-3,D-4,D-7 / S5 | Found StageOutcome alone does not move/reopen cards | PC-129 |
| G-130 | D-3,D-4,D-7 / S5 | A triage decision persists on move/reopen revision | PC-130 |
| G-131 | D-3,D-4,D-7 / S5 | Card decision/finding is not alert-sink delivery | PC-131 |
| G-132 | D-3,D-4,D-7 / S5 | New L2 battery cannot rewrite L evidence | PC-132 |
| G-133 | D-3,D-4,D-7 / S5 | Recording verification obligation does not authorize automatic dispatch | PC-133 |
| G-134 | D-2,D-3,D-5,D-7 / S5 | Terminal land event and notification obligation commit together | PC-134 |
| G-135 | D-2,D-3,D-5,D-7 / S5 | Enqueue failure leaves durable land notification retryable | PC-135 |
| G-136 | D-2,D-3,D-5,D-7 / S5 | Queue adoption uses exact notification identity | PC-136 |
| G-137 | D-2,D-3,D-5,D-7 / S5 | Dropped wakeup is recovered for already-idle recipient | PC-137 |
| G-138 | D-2,D-3,D-5,D-7 / S5 | Busy caller is not interrupted by land continuation | PC-138 |
| G-139 | D-2,D-3,D-5,D-7 / S5 | Sent/ACK is insufficient land receipt | PC-139 |
| G-140 | D-2,D-3,D-5,D-7 / S5 | Receipt belongs to original snapshotted destination | PC-140 |
| G-141 | D-2,D-3,D-5,D-7 / S5 | Receipt has exact notification identity | PC-141 |
| G-142 | D-2,D-3,D-5,D-7 / S5 | Receipt includes whole body or complete spill pointer | PC-142 |
| G-143 | D-2,D-3,D-5,D-7 / S5 | Receipt is newer than delivery attempt baseline | PC-143 |
| G-144 | D-2,D-3,D-5,D-7 / S5 | Crash after recipient receipt cannot cause retyping | PC-144 |
| G-145 | D-2,D-3,D-5,D-7 / S5 | Accepted sourced task survives create-to-dispatch wakeup loss | PC-145 |
| G-146 | D-2,D-3,D-5,D-7 / S5 | Provisioned source identity survives launch enqueue failure | PC-146 |
| G-147 | D-2,D-3,D-5,D-7 / S5 | Worker brief delivery needs exact task prompt evidence | PC-147 |
| G-148 | D-2,D-3,D-5,D-7 / S5 | Stale launch cannot borrow a newer session generation | PC-148 |
| G-149 | D-2,D-3,D-5,D-7 / S5 | Mutation Result is saved before parent delivery/release | PC-149 |
| G-150 | D-2,D-3,D-5,D-7 / S5 | Lost completion insert is recovered from durable task | PC-150 |
| G-151 | D-2,D-3,D-5,D-7 / S5 | Committed completion insert is adopted after lost acknowledgement | PC-151 |
| G-152 | D-2,D-3,D-5,D-7 / S5 | Already-idle caller receives completion without another turn | PC-152 |
| G-153 | D-2,D-3,D-5,D-7 / S5 | Busy caller completion waits until eligible | PC-153 |
| G-154 | D-2,D-3,D-5,D-7 / S5 | Completion visibility cannot substitute for recipient evidence | PC-154 |
| G-155 | D-2,D-3,D-5,D-7 / S5 | Completion receipt matches exact task identity | PC-155 |
| G-156 | D-2,D-3,D-5,D-7 / S5 | Late receipt recovery must not retype a completed report | PC-156 |
| G-157 | D-2,D-3,D-5,D-7 / S5 | Land outcome cannot be mistaken for Code/Mutation completion | PC-157 |
| G-158 | D-2,D-3,D-5,D-7 / S5 | Completion receipt needs complete body | PC-158 |
| G-159 | D-2,D-3,D-5,D-7 / S5 | Completion receipt must follow attempt baseline | PC-159 |
| G-160 | D-2,D-3,D-5,D-7 / S5 | Completion receipt belongs to recipient session | PC-160 |
| G-161 | D-2,D-3,D-5,D-7 / S5 | Long completion pointer references durable full result | PC-161 |
| G-162 | D-1..D-9 / S4 | Every completed Code, including zero PCs, hands off Review | PC-162 |
| G-163 | D-1..D-9 / S4 | Ordinary Review has no executed-PC prerequisite | PC-163 |
| G-164 | D-1..D-9 / S4 | Review carries original Code landing owner | PC-164 |
| G-165 | D-1..D-9 / S4 | Default Mutation dispatch requires SourceLanding Worktree | PC-165 |
| G-166 | D-1..D-9 / S4 | Generic delegate instructions exclude snapshot commits/pushes | PC-166 |
| G-167 | D-1..D-9 / S4 | Clean Mutation ends none; finding ends decide | PC-167 |
| G-168 | D-1..D-9 / S4 | Before land, caller records and links pending verification | PC-168 |
| G-169 | D-1..D-9 / S4 | Caller reconciles interrupted companion creation/dispatch | PC-169 |
| G-170 | D-1..D-9 / S4 | Original Done may describe pending verification, never PC-clean | PC-170 |
| G-171 | D-1..D-9 / S4 | Survivor, product regression, invalid mutant and infra failure differ | PC-171 |
| G-172 | D-1..D-9 / S4 | Findings never automatically reland/revert source task | PC-172 |
| G-173 | D-1..D-9 / S4 | Background worker awaits commands and restores before settlement | PC-173 |
| G-174 | D-1..D-9 / S4 | Mutation runs all variants and missing-control discovery, even zero PCs | PC-174 |
| G-175 | D-1..D-9 / S4 | TestDesign retains numeric cost and bijective guard inventory | PC-175 |
| G-176 | D-1..D-9 / S4 | Source selector and custody ship with instruction reorder | PC-176 |
| G-177 | D-1..D-9 / S4 | Changing workflow preserves active commands and tested SHA evidence | PC-177 |
| G-178 | D-1..D-9 / S4 | Existing tokens, ordinals and aliases remain compatible | PC-178 |
| G-179 | D-1..D-9 / S4 | Stage bundles remain ASCII | PC-179 |
| G-180 | D-1..D-9 / S4 | Stage bundles obey 2500-character budget | PC-180 |
| G-181 | D-1..D-9 / S4 | Companion linking preserves existing human content | PC-181 |
| G-182 | D-1..D-9 / S4 | Abandoned/superseded verification is not Done/Clean | PC-182 |

### Positive controls

**Owner: Mutation, post-land at L.** Code implements the tests and runs green V/R; Review judges ordinary results before land. Neither Code nor Review executes this deliberate-defect battery. Each row specifies a compiling implementation defect and its exact test method. For embedded/active instruction guards, mutate the named production instruction resource, leave the test's independent expectation intact, and rebuild; that is a contract PC, not behavioral proof of an LLM following it.

"Refused" means the specific expected validation/not-found/authorization/concurrency result from the application (422/404/403/409 as applicable), never an arbitrary exception, fixture failure or process exit. DB relation controls require the foreign-key rejection and retained rows. Define the listed decisive assertion in the named test; use scoped row counts and independent Git observations. Green acceptance control must reach the operation being protected.

For each row record: source O/L and task ID; exact patch/diff; baseline method name/nonzero counts; compiling mutant; intended assertion red with each applicable Arguments case; restored byte hashes; fresh rebuild; same method restored green; final HEAD/index/source inventory and durable evidence path. A build error, no-tests result, fixture setup error, cancellation, unrelated assertion or surviving/equivalent mutant is not a kill. Adjust an invalid mutant explicitly and keep its history/remaining obligation. Never repair production/tests in this snapshot.

#### Admission

| Control / exact method | Compiling defect to apply | Decisive expected assertion |
|---|---|---|
| PC-1: `PostLandMutationAdmissionTests.C478_G001_SourceExists` | omit missing-operation refusal and continue with a seeded fallback operation | Refused; AcceptedTaskCount == 0; ProvisionCalls == 0 |
| PC-2: `PostLandMutationAdmissionTests.C478_G002_SourceTaskExists` | remove the landing TaskId foreign-key relationship from model configuration in an isolated model-created schema | Deleting the source task is rejected; operation still resolves original task (never seed an impossible orphan in the migrated production schema) |
| PC-3: `PostLandMutationAdmissionTests.C478_G003_Role` | skip the source-mode role predicate | Every non-Mutation role refused before provisioning |
| PC-4: `PostLandMutationAdmissionTests.C478_G004_Worker` | skip the Worker-kind predicate | Orchestrator request refused; ProvisionCalls == 0 |
| PC-5: `PostLandMutationAdmissionTests.C478_G005_Workspace` | skip source-mode workspace predicate | ReadOnly and Shared refused; ProvisionCalls == 0 |
| PC-6: `PostLandMutationAdmissionTests.C478_G006_Standing` | skip explicit AgentId/pin-to-standing rejection for this mode | StandingAgentId request refused; LaunchCalls == 0 |
| PC-7: `PostLandMutationAdmissionTests.C478_G007_MergeTarget` | accept an explicit target with SourceLanding | Refused even when target is master; AcceptedTaskCount == 0 |
| PC-8: `PostLandMutationAdmissionTests.C478_G008_Authorization` | bypass workspace authorization only for sourced tasks | Unauthorized caller refused; no task or Git writes |
| PC-9: `PostLandMutationAdmissionTests.C478_G009_Repository` | skip canonical repository/common-root equality | Cross-repository operation refused, including same leaf directory names |
| PC-10: `PostLandMutationAdmissionTests.C478_G010_Project` | assign ProjectId from the operation instead of validating caller equality | Null/non-null and project A/B mismatches refused; caller bucket unchanged |
| PC-11: `PostLandMutationAdmissionTests.C478_G011_CardRequired` | allow unresolved/null CardId in source mode | Missing and nonexistent GUID bindings refused |
| PC-12: `PostLandMutationAdmissionTests.C478_G012_DistinctCard` | skip source-card inequality | Original-card binding refused |
| PC-13: `PostLandMutationAdmissionTests.C478_G013_Board` | skip board equality | Different-board card with same CARD identifier refused |
| PC-14: `PostLandMutationAdmissionTests.C478_G014_Publication` | trust Succeeded/event prose instead of calling HasPublication | Queued land, local advance, push success, legacy prose and LandRefused all refused |
| PC-15: `PostLandMutationAdmissionTests.C478_G015_OpenOperation` | omit same-operation duplicate query | Second request refused with existing task ID for Queued/Dispatched/Working/Blocked |
| PC-16: `PostLandMutationAdmissionTests.C478_G016_AtomicAdmission` | release admission lock before duplicate read and insert | Barrier race yields exactly one accepted row and one refusal naming it |
| PC-17: `PostLandMutationAdmissionTests.C478_G017_GlobalCap` | exclude sourced Mutation from absolute open-task count | Over-cap request refused; count unchanged |
| PC-18: `PostLandMutationAdmissionTests.C478_G018_ProjectCap` | route sourced tasks through an empty/null capacity bucket | Occupied same-project request refused; unrelated-project control accepted |
| PC-19: `PostLandMutationAdmissionTests.C478_G019_Provider` | skip selected-provider availability check for sourced tasks | 409 quota/sign-in remains visible; no fallback provider/task/launch |
| PC-20: `PostLandMutationAdmissionTests.C478_G020_Pin` | ignore required Mutation pin in sourced request resolution | Conflicting selection refused; pinned kind/model preserved |
| PC-21: `PostLandMutationAdmissionTests.C478_G021_SourcePersist` | delay persisting SourceLandingOperationId until after CreateAsync | Separate DB observer at first Git boundary reads exact O |
| PC-22: `PostLandMutationAdmissionTests.C478_G022_SourceImmutable` | replace O from the latest landing on retry | Reloaded task.SourceLandingOperationId == O after queued restart/retry/cancel |
| PC-23: `PostLandMutationAdmissionTests.C478_G023_SourceDelete` | change source FK DeleteBehavior from Restrict to SetNull/Cascade | Database delete fails; sourced row still references O |
| PC-24: `PostLandMutationAdmissionTests.C478_G024_Detail` | omit source operation/verified SHA from detail projection | HTTP JSON contains exact sourceLandingOperationId and L; historical task fields null |
| PC-25: `PostLandMutationAdmissionTests.C478_G025_CliSource` | drop sourceLandingOperationId from delegate.ps1 create body | Stub receives exact GUID, Worktree, Mutation, verification GUID; zero land calls |
| PC-26: `PostLandMutationAdmissionTests.C478_G026_CliCleanup` | wire CleanupVerification to the land request route | Stub receives cleanup for Mutation task; zero land/create requests |

#### Publication predicates

| Control / exact method | Compiling defect to apply | Decisive expected assertion |
|---|---|---|
| PC-27: `PostLandMutationPublicationTests.C478_G027_Schema` | accept schema 999 in HasIdentity | HasPublication == false for schema 999 |
| PC-28: `PostLandMutationPublicationTests.C478_G028_OperationId` | remove operation.Id nonempty check | HasPublication == false for empty Id with otherwise coherent namespace |
| PC-29: `PostLandMutationPublicationTests.C478_G029_TaskId` | remove TaskId nonempty check | HasPublication == false for empty TaskId with coherent namespace |
| PC-30: `PostLandMutationPublicationTests.C478_G030_OriginalOid` | replace original SHA IsOid check with non-null check | Malformed original SHA rejected |
| PC-31: `PostLandMutationPublicationTests.C478_G031_TargetOid` | replace target-before IsOid check with non-null check | Malformed target-before SHA rejected |
| PC-32: `PostLandMutationPublicationTests.C478_G032_SourceRef` | omit SourceFullRef heads-prefix check | refs/tags/source rejected |
| PC-33: `PostLandMutationPublicationTests.C478_G033_TargetRef` | omit TargetFullRef heads-prefix check | Matching target/destination refs/tags/target rejected |
| PC-34: `PostLandMutationPublicationTests.C478_G034_DistinctRefs` | omit source/target inequality | Identical heads refs rejected |
| PC-35: `PostLandMutationPublicationTests.C478_G035_Destination` | omit both duplicated destination-equality predicates | Different destination rejected |
| PC-36: `PostLandMutationPublicationTests.C478_G036_RepositoryIdentity` | omit RepositoryPath nonblank check | Blank repository rejected |
| PC-37: `PostLandMutationPublicationTests.C478_G037_CommonIdentity` | omit CommonDirectory nonblank check | Blank common directory rejected |
| PC-38: `PostLandMutationPublicationTests.C478_G038_WorktreeIdentity` | omit WorktreePath nonblank check | Blank worktree rejected |
| PC-39: `PostLandMutationPublicationTests.C478_G039_GitIdentity` | omit GitDirectory nonblank check | Blank Git directory rejected |
| PC-40: `PostLandMutationPublicationTests.C478_G040_Fingerprint` | omit both duplicated fingerprint-length predicates | Malformed fingerprint rejected |
| PC-41: `PostLandMutationPublicationTests.C478_G041_Namespace` | omit both duplicated recovery-prefix predicates | Crossed task/operation namespace rejected |
| PC-42: `PostLandMutationPublicationTests.C478_G042_ConfirmedTime` | omit RemoteConfirmedAt check | Unconfirmed timestamp rejected |
| PC-43: `PostLandMutationPublicationTests.C478_G043_PublicationKind` | accept Unconfirmed publication enum | Unconfirmed publication rejected despite valid SHAs |
| PC-44: `PostLandMutationPublicationTests.C478_G044_VerifiedOid` | remove VerifiedSourceSha IsOid checks from publication and verification helpers | Malformed L rejected |
| PC-45: `PostLandMutationPublicationTests.C478_G045_ObservedOid` | omit ObservedRemoteTargetSha IsOid check | Malformed R rejected |
| PC-46: `PostLandMutationPublicationTests.C478_G046_Method` | omit ConfirmationMethod check | A push-exit-code-only method rejected |
| PC-47: `PostLandMutationPublicationTests.C478_G047_VerifiedTime` | omit VerifiedAt check | Null VerifiedAt rejected |
| PC-48: `PostLandMutationPublicationTests.C478_G048_SourcePin` | omit SourcePinned check in HasVerification | Unpinned source rejected |
| PC-49: `PostLandMutationPublicationTests.C478_G049_TargetPin` | omit TargetPinned check in HasVerification | Unpinned target rejected |
| PC-50: `PostLandMutationPublicationTests.C478_G050_PreparedPin` | omit prepared-pin conditional | Rebased but unpinned prepared source rejected |
| PC-51: `PostLandMutationPublicationTests.C478_G051_VerifiedIdentity` | omit VerifiedSourceSha equality with prepared/original source | Different well-formed L rejected |
| PC-52: `PostLandMutationPublicationTests.C478_G052_VerificationVerdict` | replace verdict disjunction with true | No pass and null/unknown skip rejected |
| PC-53: `PostLandMutationPublicationTests.C478_G053_UnchangedBase` | omit rebased/original equality only in base_unchanged branch | Changed base without verifier pass rejected |
| PC-54: `PostLandMutationPublicationTests.C478_G054_RequiredFilter` | omit empty VerificationFilter check | Nonempty selected filter without execution rejected |
| PC-55: `PostLandMutationPublicationTests.C478_G055_ContainmentSkip` | omit RebasedSourceSha-null condition in containment skip | Containment-only skip after rebase rejected |

#### Snapshot and settlement

| Control / exact method | Compiling defect to apply | Decisive expected assertion |
|---|---|---|
| PC-56: `PostLandMutationWorktreeTests.C478_G056_ExactL` | pass OriginalSourceSha as CreateAsync base | WorktreeBaseSha == L and git HEAD == L where C != L |
| PC-57: `PostLandMutationWorktreeTests.C478_G057_NotRemoteTip` | pass ObservedRemoteTargetSha/current target as creation base | git HEAD == L where L != R; R-only sentinel absent |
| PC-58: `PostLandMutationWorktreeTests.C478_G058_NoInheritedTarget` | retain parent MergeTargetRef on sourced task | Reloaded MergeTargetRef == null before and after dispatch |
| PC-59: `PostLandMutationWorktreeTests.C478_G059_FreshPath` | reuse original Code WorktreePath | Mutation path != Code path and contains Mutation task identity |
| PC-60: `PostLandMutationWorktreeTests.C478_G060_FreshBranch` | reuse source task WorktreeBranch | Mutation branch differs and checkout registration names only its fresh path |
| PC-61: `PostLandMutationWorktreeTests.C478_G061_FreshProcess` | allow sourced task to take the seeded stale warm pool candidate | New AgentId/SessionId; stale adapter receives zero input |
| PC-62: `PostLandMutationWorktreeTests.C478_G062_Bundle` | select StageCode in sourced BuildLaunchSpec | Captured Claude/Codex/Grok launch composition contains one current stage-mutation, no stage-code |
| PC-63: `PostLandMutationWorktreeTests.C478_G063_CreateLease` | move CreateAsync outside lease lifetime | At first worktree-add, competing lease acquisition fails |
| PC-64: `PostLandMutationWorktreeTests.C478_G064_RetryIdentity` | skip stored task/path/branch creation identity comparison | Cross-task existing tree refused, bytes and registrations unchanged |
| PC-65: `PostLandMutationWorktreeTests.C478_G065_RetryHead` | skip recovered WorktreeBaseSha/HEAD comparison | Moved HEAD retained; dependent launch count == 0 |
| PC-66: `PostLandMutationWorktreeTests.C478_G066_RetryGit` | skip recovered Git-directory equality | Crossed Git directory retained; launch count == 0 |
| PC-67: `PostLandMutationWorktreeTests.C478_G067_MissingCommit` | catch resolve-L failure and retry CreateAsync at HEAD | No snapshot launched; explicit source-unavailable evidence |
| PC-68: `PostLandMutationContractTests.C478_G068_TrackedClean` | delete the tracked-source-clean requirement from the embedded Mutation preflight instructions | Loaded Mutation contract requires tracked diff check before baseline/mutation |
| PC-69: `PostLandMutationContractTests.C478_G069_IndexClean` | delete the clean-index requirement from embedded Mutation preflight instructions | Loaded Mutation contract requires staged diff check before baseline/mutation |
| PC-70: `PostLandMutationContractTests.C478_G070_PreflightHead` | delete immediate HEAD=L preflight requirement from embedded Mutation instructions | Loaded Mutation contract requires HEAD=L before baseline and first mutant |
| PC-71: `PostLandMutationWorktreeTests.C478_G071_CallerSettlement` | remove Role Mutation bypass at reply settlement caller | Recording lower-service boundary sees zero mergeback invocations |
| PC-72: `PostLandMutationWorktreeTests.C478_G072_LowerSettlement` | remove Role Mutation early return in TryMergeBackAsync | Direct call leaves dirty sentinel bytes and index unchanged; commit calls == 0 |
| PC-73: `PostLandMutationWorktreeTests.C478_G073_NoLocalMerge` | allow sourced task into merge/rebase after verification-only result | Direct call with adversarial inherited target emits zero merge/rebase commands |
| PC-74: `PostLandMutationWorktreeTests.C478_G074_NoLandRequest` | skip Mutation refusal in RequestAsync | No durable land request or queued land item; refusal returned |
| PC-75: `PostLandMutationWorktreeTests.C478_G075_NoLandExecution` | skip Mutation refusal in Run/RunRequest execution guard | Seed bypassed request then execute: zero push/target-advance; refusal evidence |
| PC-76: `PostLandMutationWorktreeTests.C478_G076_NoProgress` | apply implementation no-progress classifier to Mutation | Valid done/next:none with evidence remains Succeeded; no no-progress retry |
| PC-77: `PostLandMutationWorktreeTests.C478_G077_NoSettlementDelete` | invoke generic empty-worktree cleanup on successful Mutation | After clean no-commit settlement tree and branch still exist |
| PC-78: `PostLandMutationWorktreeTests.C478_G078_InterruptedCustody` | clear source/worktree fields during failure release | O/L and dirty bytes survive failed/canceled/catch-up/dead-session arms |
| PC-79: `PostLandMutationWorktreeTests.C478_G079_EvidenceOutside` | resolve evidence root under task.WorktreePath | Evidence canonical path lies outside snapshot and remains readable after authorized cleanup |
| PC-80: `PostLandMutationWorktreeTests.C478_G080_OrdinaryCode` | apply SourceLanding base resolution to every Worktree task | Nested Code starts at parent target; top-level Code at master; existing autosave behavior preserved |
| PC-81: `PostLandMutationWorktreeTests.C478_G081_RetryRegistration` | skip registration match during sourced recovery | Unregistered existing directory preserved; no dependent launch |
| PC-82: `PostLandMutationWorktreeTests.C478_G082_ProviderCap` | exclude sourced Mutation from dispatcher running count | At cap Code remains Queued; after Mutation settles Code dispatches |
| PC-83: `PostLandMutationWorktreeTests.C478_G083_CreationFailure` | invoke unconditional rollback delete after failed worktree add | Failed creation with opaque hook sentinel retains it and actionable residue |
| PC-84: `PostLandMutationWorktreeTests.C478_G084_NoSilentRetry` | autoqueue replacement on sourced task terminal failure | Ticks after terminal failure create zero replacement tasks |

#### Cleanup

| Control / exact method | Compiling defect to apply | Decisive expected assertion |
|---|---|---|
| PC-85: `PostLandMutationCleanupTests.C478_G085_Purpose` | accept Publication purpose with borrowed O for Mutation directory | Borrowed Code receipt refused before remove/update-ref |
| PC-86: `PostLandMutationCleanupTests.C478_G086_Task` | omit authority TaskId comparison | Cross-task request retains both trees; destructive commands == 0 |
| PC-87: `PostLandMutationCleanupTests.C478_G087_Mode` | omit Role Mutation cleanup eligibility | Code task with otherwise valid verification-shaped authority is refused |
| PC-88: `PostLandMutationCleanupTests.C478_G088_Terminal` | omit terminal-status check | Queued/Dispatched/Working/Blocked retained; zero removal |
| PC-89: `PostLandMutationCleanupTests.C478_G089_Owner` | omit live session/standing/pool ownership check | With otherwise valid sealed receipt, newly recorded live owner retains tree after task Succeeded; zero destructive requests |
| PC-90: `PostLandMutationCleanupTests.C478_G090_Children` | treat Draining/nonempty-job custody as Exited in removal eligibility | Dead root plus live orphan descendant retains tree/evidence; zero destructive requests at first boundary; native counterpart V-17 proves it without seeded custody |
| PC-91: `PostLandMutationCleanupTests.C478_G091_Repository` | omit canonical repository comparison | Cross-repository deletion refused |
| PC-92: `PostLandMutationCleanupTests.C478_G092_Path` | omit canonical task worktree path comparison | Sibling, main checkout and escaped path retained |
| PC-93: `PostLandMutationCleanupTests.C478_G093_CommonDirectory` | omit common-directory comparison | Crossed common directory refused |
| PC-94: `PostLandMutationCleanupTests.C478_G094_GitDirectory` | omit Git-directory comparison | Replaced .git pointer refused |
| PC-95: `PostLandMutationCleanupTests.C478_G095_Branch` | omit branch/task identity comparison | Different managed branch retained |
| PC-96: `PostLandMutationCleanupTests.C478_G096_Creation` | accept a newly recreated directory at the old path | Replacement creation identity refused; replacement sentinel retained |
| PC-97: `PostLandMutationCleanupTests.C478_G097_Registration` | trust stored path when current registration missing | Unregistered opaque directory retained |
| PC-98: `PostLandMutationCleanupTests.C478_G098_Head` | omit HEAD/L comparison | Extra commit retains tree and branch |
| PC-99: `PostLandMutationCleanupTests.C478_G099_Index` | omit staged-content inspection | Staged-only mutant retained |
| PC-100: `PostLandMutationCleanupTests.C478_G100_Tracked` | omit unstaged-content inspection | Unstaged mutant retained |
| PC-101: `PostLandMutationCleanupTests.C478_G101_Untracked` | ignore untracked inspection entries | Unknown file, including empty directory policy sentinel, retained |
| PC-102: `PostLandMutationCleanupTests.C478_G102_Ignored` | allow all bin-*/.antiphon/.claude ignored paths | Unknown ignored sentinel retained; no name/age-based deletion |
| PC-103: `PostLandMutationCleanupTests.C478_G103_OwnedOutputs` | authorize an entire output parent directory from one manifest entry | Unlisted sibling output survives; cleanup returns residue |
| PC-104: `PostLandMutationCleanupTests.C478_G104_OutputEscape` | skip canonical path/reparse containment check | Traversal or junction target outside root untouched |
| PC-105: `PostLandMutationCleanupTests.C478_G105_Sequencer` | omit sequencer inspection | Each active sequencer marker retains source and index |
| PC-106: `PostLandMutationCleanupTests.C478_G106_Evidence` | omit evidence existence/completeness check | Missing, unreadable or in-tree-only evidence retains tree |
| PC-107: `PostLandMutationCleanupTests.C478_G107_Lease` | accept missing/disposed/foreign lease | Mutation-capable I/O fake records zero destructive requests |
| PC-108: `PostLandMutationCleanupTests.C478_G108_FinalIdentity` | skip final coordinate/HEAD inspection | Barrier swaps coordinates/HEAD after first inspection: removal refused |
| PC-109: `PostLandMutationCleanupTests.C478_G109_FinalContent` | reuse first clean-content result | Late dirty/untracked/ignored sentinel retained before next command |
| PC-110: `PostLandMutationCleanupTests.C478_G110_FinalOwnership` | reuse initially loaded frozen attempt set/receipt and owner state | Changed attempt membership, receipt integrity or live ownership at final barrier refuses before removal; do not substitute another census |
| PC-111: `PostLandMutationCleanupTests.C478_G111_BranchCas` | drop expected SHA from update-ref -d | Branch advanced after directory removal survives with residue |
| PC-112: `PostLandMutationCleanupTests.C478_G112_OtherCheckout` | skip final branch registration check | New sibling checkout survives; branch retained |
| PC-113: `PostLandMutationCleanupTests.C478_G113_NoForce` | add --force to worktree remove or recursive fallback on failure | Trace excludes force/fallback; injected remove failure retains evidence/residue |
| PC-114: `PostLandMutationCleanupTests.C478_G114_ReadFailure` | treat inspection nonzero/timeout/unreadable as clean | Inspection failure issues zero destructive requests |
| PC-115: `PostLandMutationCleanupTests.C478_G115_CleanupRecovery` | mark all removal facts complete before destructive calls | Cut after remove/before branch deletion resumes without erasing replacement tree |
| PC-116: `PostLandMutationCleanupTests.C478_G116_ResidueVerdict` | mark task Failed/land unconfirmed when removal fails | O remains HasPublication; completed result unchanged; residue reason visible |
| PC-117: `PostLandMutationCleanupTests.C478_G117_Workspace` | omit Workspace == Worktree check | Shared Mutation cleanup refused |
| PC-118: `PostLandMutationCleanupTests.C478_G118_SourceBinding` | omit source-operation identity comparison | Null/other operation authority refused despite same L |
| PC-119: `PostLandMutationCleanupTests.C478_G119_UnknownChildren` | treat missing/Unknown/UnsupportedBackend custody as Exited | Each state retains tree with typed actionable reason, even with terminal task/session, empty census and commandsAwaited manifest |
| PC-120: `PostLandMutationCleanupTests.C478_G120_ProcessIdentity` | synthesize Exited from missing or reused root PID instead of requiring original container receipt | Gone/reused PID without matching generation receipt retains tree; valid receipt still works after PID reuse, without inspecting/killing the unrelated process |
| PC-121: `PostLandMutationCleanupTests.C478_G121_EvidenceScope` | accept evidence manifest for another operation/task or incomplete matrix | Wrong O/task or missing remaining-PC/restoration disposition retains tree |
| PC-122: `PostLandMutationCleanupTests.C478_G122_FinalAuthority` | skip durable task/source/evidence reload before remove | Task reopens or evidence identity changes at barrier: zero removal |
| PC-123: `PostLandMutationCleanupTests.C478_G123_BranchSymbolic` | omit symbolic-ref/no-deref safety | Alias to sibling ref not followed; sibling remains unchanged |

#### Independent lifecycle

| Control / exact method | Compiling defect to apply | Decisive expected assertion |
|---|---|---|
| PC-124: `PostLandMutationWorkflowTests.C478_G124_OriginalTerminal` | remove terminal-card exclusion in CardWorkTransitionService | Original Done status and close revision unchanged through all companion states |
| PC-125: `PostLandMutationWorkflowTests.C478_G125_CompanionVisible` | filter sourced Mutation out of open-task/pipeline projection | Verification GUID appears in queued/in-flight result while original has no ready row |
| PC-126: `PostLandMutationWorkflowTests.C478_G126_NoImplicitDone` | move successful verification directly to Done in card transition | Settlement moves eligible companion only to Review; explicit clean close still required |
| PC-127: `PostLandMutationWorkflowTests.C478_G127_NoImplicitSpawn` | call Spawn path during companion active transition | Session count unchanged and AutoDispatchHeldAt set after sweep |
| PC-128: `PostLandMutationWorkflowTests.C478_G128_HumanMove` | omit latest Move/Reopen timestamp comparison | Newer Canceled/NeedsDecision/manual Backlog revision survives stale sweep |
| PC-129: `PostLandMutationWorkflowTests.C478_G129_FindingEvidence` | make finding settlement reopen source card | Found row exists; original Done and both card revisions unchanged until explicit move |
| PC-130: `PostLandMutationWorkflowTests.C478_G130_DecisionRevision` | drop decision payload when applying NeedsDecision move | Revision contains exact question; attention links remediation card and decision |
| PC-131: `PostLandMutationWorkflowTests.C478_G131_NoAlertSink` | send decision through alert router from move/reopen | Recording alert sink receives zero messages for explicit triage |
| PC-132: `PostLandMutationWorkflowTests.C478_G132_HistoricalResult` | overwrite superseded task.Result during follow-up creation | Old O/L/Found Result bytes unchanged; new task references O2/L2 |
| PC-133: `PostLandMutationWorkflowTests.C478_G133_NoTickSpend` | make orchestration tick enqueue Mutation for the label | Ordinary ticks create zero tasks/sessions; explicit caller action required |

#### Delivery

| Control / exact method | Compiling defect to apply | Decisive expected assertion |
|---|---|---|
| PC-134: `PostLandMutationDeliveryTests.C478_G134_LandAtomic` | save terminal event before notification in a separate commit | Crash at transaction boundary leaves both durable or neither; recovery yields one correlated obligation |
| PC-135: `PostLandMutationDeliveryTests.C478_G135_LandEnqueue` | mark notification Confirmed on enqueue exception | Failure leaves unconfirmed retry state; recovery reaches complete matching caller UserPrompt |
| PC-136: `PostLandMutationDeliveryTests.C478_G136_LandQueueKey` | drop SourceLandNotificationId keyed adoption | Crash after queue commit before link save recovers same queue ID and one body |
| PC-137: `PostLandMutationDeliveryTests.C478_G137_LandWakeup` | remove persisted-notification/flush sweep eligibility for landing notes | Without new TurnEnd, hosted sweep delivers exact land UserPrompt |
| PC-138: `PostLandMutationDeliveryTests.C478_G138_LandBusy` | enqueue land note with Now instead of WhenIdle | Busy adapter receives zero input; one complete prompt after eligible TurnEnd |
| PC-139: `PostLandMutationDeliveryTests.C478_G139_LandReceipt` | confirm notification from row.Status == Sent | No UserPrompt means ConfirmedAt null despite Sent/ACK |
| PC-140: `PostLandMutationDeliveryTests.C478_G140_LandDestination` | read current task.ParentSessionId instead of notification snapshot | Destination edit never types to new session; receipt belongs to original session |
| PC-141: `PostLandMutationDeliveryTests.C478_G141_LandIdentity` | drop identity part of PromptSubmissionMatch for land confirmation | Complete prompt for another operation/notification does not confirm |
| PC-142: `PostLandMutationDeliveryTests.C478_G142_LandCompleteness` | replace full match with prefix-only match | Head-only and head-tail-splice prompt do not confirm; spill file bytes equal full payload |
| PC-143: `PostLandMutationDeliveryTests.C478_G143_LandFreshness` | omit sequence/time boundary in land confirmation | Matching old prompt does not confirm |
| PC-144: `PostLandMutationDeliveryTests.C478_G144_LandReceiptSave` | ignore caught-up prompt on receipt-save recovery | One matching UserPrompt and zero additional submit after restart |
| PC-145: `PostLandMutationDeliveryTests.C478_G145_LaunchPersist` | exclude source-mode queued rows from dispatcher scan | Restart dispatches persisted same task/O/L with one complete worker brief |
| PC-146: `PostLandMutationDeliveryTests.C478_G146_LaunchRecovery` | clear O/worktree identity when enqueue fails | Retry uses same validated snapshot; no second owned tree/process |
| PC-147: `PostLandMutationDeliveryTests.C478_G147_LaunchReceipt` | treat adapter-start/ACK as a delivered boot brief | No complete worker UserPrompt means delivery unconfirmed; task is not falsely settled |
| PC-148: `PostLandMutationDeliveryTests.C478_G148_LaunchGeneration` | skip accepted-generation equality for sourced queued launch | Old item creates zero adapters/inputs against newer generation |
| PC-149: `PostLandMutationDeliveryTests.C478_G149_CompletionPersist` | move result save after enqueue/release | Fault at enqueue/release leaves full Result, O/L and next-stage durable |
| PC-150: `PostLandMutationDeliveryTests.C478_G150_CompletionEnqueue` | skip sourced Mutation in completion-note recovery scan | Recovery produces one complete correlated caller UserPrompt and full stored report |
| PC-151: `PostLandMutationDeliveryTests.C478_G151_CompletionQueueKey` | ignore SourceTaskId/digest existing-note check | Queue-commit crash does not duplicate body on recovery |
| PC-152: `PostLandMutationDeliveryTests.C478_G152_CompletionWakeup` | drop completion flush and persisted fallback for sourced tasks | Hosted recovery delivers full matching UserPrompt without artificial TurnEnd |
| PC-153: `PostLandMutationDeliveryTests.C478_G153_CompletionBusy` | force Now delivery for sourced task completion | No input while busy; one complete completion prompt after TurnEnd |
| PC-154: `PostLandMutationDeliveryTests.C478_G154_CompletionReceipt` | promote Sent/screen-only to confirmed receipt in queue verifier | Without UserPrompt no transcript-confirmed receipt even when task Succeeded |
| PC-155: `PostLandMutationDeliveryTests.C478_G155_CompletionIdentity` | drop task identity from completion prompt match | Complete other-task prompt does not confirm receipt |
| PC-156: `PostLandMutationDeliveryTests.C478_G156_CompletionRestore` | skip late-confirm match and retry already-received completion | One caller UserPrompt; no second submit; Result unchanged |
| PC-157: `PostLandMutationDeliveryTests.C478_G157_LandNotReport` | remove SourceLandNotificationId exclusion from completion-note lookup | Land-only row cannot satisfy HasCompletionNoteAsync for Mutation/report recovery |
| PC-158: `PostLandMutationDeliveryTests.C478_G158_CompletionCompleteness` | replace full completion match with prefix-only match | Clipped/head-tail-spliced body cannot confirm receipt |
| PC-159: `PostLandMutationDeliveryTests.C478_G159_CompletionFreshness` | omit sequence/time freshness in queue receipt match | Old complete same-task prompt cannot confirm new report |
| PC-160: `PostLandMutationDeliveryTests.C478_G160_CompletionSession` | search matching prompts without session filter | Matching full prompt in another session cannot confirm |
| PC-161: `PostLandMutationDeliveryTests.C478_G161_CompletionSpill` | discard full report after spilling/compression | Complete pointer prompt received and referenced artifact/Result bytes equal full matrix |

#### Stage and caller contracts

| Control / exact method | Compiling defect to apply | Decisive expected assertion |
|---|---|---|
| PC-162: `PostLandMutationContractTests.C478_G162_CodeReview` | change embedded stage-code default next: review to next: mutation | Composed Code contract requires review for nonzero and zero PCs |
| PC-163: `PostLandMutationContractTests.C478_G163_ReviewBeforeLand` | restore mandatory executed-PC-evidence clause in stage-review | Composed Review rejects executed-PC dependency and retains ordinary delivery audit |
| PC-164: `PostLandMutationContractTests.C478_G164_LandingOwner` | replace original Code task owner with Review task in active recipe | Every active Review/land recipe preserves original Code task ID |
| PC-165: `PostLandMutationContractTests.C478_G165_SourceRecipe` | restore retained Code Shared recipe in active delegate skill | Active recipe audit rejects Shared/retained Code default and requires SourceLanding |
| PC-166: `PostLandMutationContractTests.C478_G166_SnapshotNoCommit` | remove snapshot exception in delegate-basics | Composed Mutation has no applicable commit/push/amend-plan instruction |
| PC-167: `PostLandMutationContractTests.C478_G167_MutationOutcomes` | change stage-mutation clean next:none to land or finding decide to code | Clean/finding defaults equal none/decide; no snapshot land/repair/deploy |
| PC-168: `PostLandMutationContractTests.C478_G168_PendingDurable` | delete before-land companion obligation from loop | Loop contract requires stable key, full board/card/task IDs, pending inventory and reverse link |
| PC-169: `PostLandMutationContractTests.C478_G169_ResumeDedup` | delete inspect-before-create/duplicate-reconciliation rule | Loop requires stable-key/thread/task inspection and serialized action before retry |
| PC-170: `PostLandMutationContractTests.C478_G170_NoFalseClean` | replace pending close wording with fully verified | Close contract requires C/O/L/Review/companion and pending wording |
| PC-171: `PostLandMutationContractTests.C478_G171_Triage` | classify a surviving mutant as automatic product revert in stage/loop instructions | Contract keeps four outcomes, evidence/severity, linked remediation and explicit disposition |
| PC-172: `PostLandMutationContractTests.C478_G172_NoAutoRevert` | replace forward-fix recipe with automatic original-task reland | Loop requires new reviewed repair/revert and prohibits reset/force-push |
| PC-173: `PostLandMutationContractTests.C478_G173_WorkerOwnership` | delete foreground/restoration obligations from mutation/basics | Composed contract requires await, byte restoration, fresh rebuild and final source/index checks |
| PC-174: `PostLandMutationContractTests.C478_G174_AllControls` | remove all-variants/zero-PC discovery clause | Composed contract requires exhaustive pending inventory and discovery |
| PC-175: `PostLandMutationContractTests.C478_G175_CostInventory` | remove numeric cost or distinct-PC inventory requirement from stage-test-design | Composed TestDesign requires separate Code/Mutation numeric floors and 1:1 inventory |
| PC-176: `PostLandMutationContractTests.C478_G176_Rollout` | allow prose-only rollout in active ops/loop instructions | Rollout contract requires loaded selector/composition verification and all S1-S4 safeguards |
| PC-177: `PostLandMutationContractTests.C478_G177_ActiveBattery` | replace finish/restore-before-transition rule with cancel-and-clean | Loop requires completed active cycle, owned command settlement and C/L applicability accounting |
| PC-178: `PostLandMutationContractTests.C478_G178_Vocabulary` | remap verify alias to Mutation in PipelineHandoff | verify resolves Review; historical mutation parses; enums/statuses unchanged |
| PC-179: `PostLandMutationContractTests.C478_G179_Ascii` | append a non-ASCII character to stage-mutation resource | Fresh catalog ASCII assertion fails |
| PC-180: `PostLandMutationContractTests.C478_G180_Size` | append ASCII text beyond 2500 characters to stage-mutation resource | Fresh catalog length assertion fails |
| PC-181: `PostLandMutationContractTests.C478_G181_MetadataPreserved` | replace append/content-revision rule with replace-whole-description recipe | Active recipe requires content revision and preserved human description/metadata |
| PC-182: `PostLandMutationContractTests.C478_G182_CanceledDisposition` | replace Canceled/successor disposition with clean close | Contract requires authorized cancellation, successor O/L/task and retained prior verdict |

### Boundary coverage and execution

| Boundary family | Required combinations / exclusion |
|---|---|
| Admission | Every non-Mutation role; Worker/Orchestrator; Worktree/Shared/ReadOnly; explicit standing binding; explicit merge target; missing/different/same original card; same CARD identifier on another board. Change one discriminator at a time, plus one multi-invalid case proving no partial writes. Exhaustive Cartesian products of independent refusals add no new decision branch and are excluded. |
| Identity/publication | Valid 40- and 64-hex IDs; malformed/null/empty IDs; matching/mismatched task, repository, project (including null), board and operation. Real C/L/R all distinct, already-present, cleanup residue, later advance/revert and source branch removal. For composite HasPublication predicates, each row corrupts only its named requirement; repeated checks of the same invariant are one logical control with the explicitly stated multi-site patch. |
| Admission race/retry | Same O, same and different companion, each Queued/Dispatched/Working/Blocked state; different O control; terminal success/fail/cancel explicit retry. Two contexts at a barrier and lost response/accepted row cases. Required pin/provider refusal plus separate Code/Mutation slots at normal caps and at saturation. |
| Snapshot | Fresh and recorded-retry worlds; missing L; wrong base/HEAD/path/branch/registration; tracked versus index-only mutation; genuine fresh provider composition versus stale warm/standing candidates. Worker preflight is procedural and checked again at execution, not inferred from dispatch time. |
| Settlement | Succeeded clean/no commits, Succeeded with dirty tracked/index/untracked files, failed, canceled, blocked/interrupted, catch-up and duplicate report. Source-bound and historical unsourced Role Mutation exercise the Role-level no-autosave/no-land guard. Ordinary Code positive control still autosaves where previously required. A blocked task is not cleanup eligible. |
| Cleanup | Each authority/content/ownership defect at first and final inspection; changed branch after directory removal; fresh sibling checkout; dirty source plus live owner; unknown ignored/untracked/junction paths; evidence outside/inside/missing/cross-task/incomplete; sealed Exited/Draining/Unknown/UnsupportedBackend and PID reuse; every sequencer; Git nonzero/timeout/Windows handle; partial remove and retry. Mutation-capable fake plus real Git preservation and native receipt-to-cleanup V-17, not fake alone. Empty directories are explicitly inventoried by filesystem enumeration, since Git status does not list them. |
| Lifecycle/triage | Original Done and companion queued/working/blocked/failed/clean/found/canceled/superseded; no-dispatch 409 and response lost after acceptance. Coverage-only vs unmutated-L defect vs invalid control vs infrastructure failure; L2 explicitly supersedes applicable scope without rewriting L. Concurrent human description/revision changes are preserved. |
| Delivery | Every producer/handoff cut x busy/eligible, plus wrong/partial/stale/absent receipts and late complete receipt. Persisted state is read from a new context; actual receiver UserPrompt comes from the real queue path's modeled/native recipient, never from the test inserting the expected final receipt by hand in a positive end-to-end case. Negative/catch-up tests may explicitly seed evidence and label that narrower purpose. |
| Contracts | Complete composed Code/Review/Mutation/TestDesign/basics/orchestrator and every D-8 active file. Nonzero/zero PCs, clean/found/incomplete, unknown/unmarked/legacy tokens and helpers. Historical reports are excluded from active-text rejection but included in parser compatibility. |

Use these commands from the task checkout **after Code implements the named methods**. Fail if a selected class/method has zero executed tests or if fresh TRX reports names outside the intended filter. The counts promised here are 182 control definitions plus the named V scenarios, not an invented eventual test count.

```powershell
$classes = @(
  'PostLandMutationAdmissionTests',
  'PostLandMutationPublicationTests',
  'PostLandMutationWorktreeTests',
  'PostLandMutationCleanupTests',
  'PostLandMutationWorkflowTests',
  'PostLandMutationDeliveryTests',
  'PostLandMutationContractTests'
)
foreach ($class in $classes) {
  dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c478-vr/ -- --treenode-filter "/*/*/$class/*" --report-trx --report-trx-filename "c478-$class.trx"
  if ($LASTEXITCODE -ne 0) { throw "Ordinary verification failed: $class" }
}
```

Run the touched existing compatibility classes once in the same way with class filters: MutationAdmissionTests, MutationDispatchTests, MutationPipelineTests, MutationRoleContractTests, DelegationWorktreeTests, WorktreeRemovalAuthorityTests, LandingRemovalPolicyControlTests, AgentTaskLandingStateTests, AgentTaskLandNotificationRecoveryTests, AgentTaskLandReceiptTests, InstructionBundleTests, DelegateBundleLaunchTests, PipelineHandoffParseTests and RoutingPinScriptTests. Include changed methods of AgentTaskReplyIntegrationTests/CardWorkTransitionServiceTests rather than widening to the whole namespace. Do not update still-valid historical `next: mutation` parser cases to hide a compatibility regression.

Client parity build (package script includes tsc):
```powershell
Push-Location client
try {
  npm run build
  if ($LASTEXITCODE -ne 0) { throw 'Client build/type verification failed' }
} finally { Pop-Location }
```

If executable client behavior changes, list the actual touched test files in the Code report and run each with `pwsh -File scripts/test-client.ps1 <test-file>`. Pure nullable DTO/type additions need the build, not a fabricated browser test. Use the existing ProductionRunnerGuard/RefusingSessionRunnerClient for any real Program host; if a child runner is required it must own a random loopback port. Never use 17204 or co-schedule the Pty assembly.

Mutation first obtains O/L from the persisted source operation and task detail, records the actual checkout path, and performs these commands with `$verifiedSourceSha` set to that full verified SHA. Do not read L from current master:
```powershell
$actualHead = git rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or $actualHead.Trim() -ne $verifiedSourceSha) {
  throw 'Snapshot HEAD does not match confirmed source'
}
git diff --exit-code
if ($LASTEXITCODE -ne 0) { throw 'Tracked source is dirty' }
git diff --cached --exit-code
if ($LASTEXITCODE -ne 0) { throw 'Index is dirty' }
git status --porcelain=v1 --untracked-files=all --ignored
if ($LASTEXITCODE -ne 0) { throw 'Source inventory failed' }
```

Also enumerate empty directories/reparse points and exact task-owned outputs. Record manifest/file hashes and source/index before baseline and after every restored cycle. These preflight commands fail on an unexpected base/dirty source; contract PCs for the worker instructions do not replace running them. Do not automatically reset/clean a preflight failure.

For example, PC-72 uses **only** this method for baseline, red and restored green, with distinct fresh report filenames:
```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c478-pc/ -- --treenode-filter "/*/*/PostLandMutationWorktreeTests/C478_G072_LowerSettlement" --report-trx --report-trx-filename c478-pc072-red.trx
```

The complete table above supplies every other class/method pair; change only that pair and the unique report filename for the next row. Each red run expects nonzero executed cases and the named assertion failure; restored green expects all selected cases passing. Save native exit codes immediately and inspect TRX outcomes. Never use `--list-tests` or exit zero as execution evidence. Report method/argument counts, expected red assertion and restored-green counts per PC.

Before each red run, save exact fixed bytes outside the worktree and record their hashes. After the run terminates, restore those bytes, refresh LastWriteTime and force a fresh build of touched code/resources before green. For example, use `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c478-pc/ --no-incremental` and verify the output DLL/resource reflects restored source before the same method's green run. This is mandatory for embedded instruction PCs too. Do not edit source while a command is still running. No snapshot commits, pushes, plan amendments or production/test repairs are permitted.

Preserve fresh TRX, full stdout/stderr, per-PC diff and restoration hashes under the persistent repository-owned verification root keyed by **O/Mutation task ID**; copy results out immediately after their observed actual output paths are known. Do not guess a TRX location or classify an old file as fresh. Track every bin/obj/output created inside the snapshot so only exact task-owned output can be considered for explicit cleanup. Durable report includes all IDs, counts, incomplete/surviving cases, evidence links and cleanup residue. Evidence retained on cancellation/failed battery records remaining controls; it need not falsely claim a complete clean matrix.

Default cost below assumes serial PC cycles. Batch only independent defects in different files/methods; preserve each method's result. Shared HasPublication edits, same service method and cross-dependent delivery controls run separately. Optional shards require separate managed source/build/result trees pinned to the same L, independent outputs and existing repository leases; they are locally owned processes, not subdelegation. Shared external-state/process-limiter constraints remain. Do not turn possible concurrency into a promised cost reduction.

### Out of scope

- No new transition parser graph, workflow engine, uniqueness table for companion cards, general base-ref picker, automatic triage classifier, PC-result parser, board status or quota policy. Behavioral tests exercise the named existing/narrow seams; procedural rules get honest contract checks and explicit workflow-driver acceptance.
- No promise that a post-land battery prevents a regression reaching master. That risk was explicitly accepted in D-1. Publication/ordinary Review/deployment acceptance remain mandatory.
- No real provider spend, production runner/broker access, whole test assembly or browser/E2E sweep in this design task. Caller-owned V-12 is required after Code lands. If Code changes transport/client behavior, run the affected existing tests and update the floor; do not claim fixture substitutes prove native transport.
- No broad repeat mutation of unchanged Git/queue internals outside the named guards. The publication predicate inventory is included because it is the new source mode's authority; remaining unchanged library internals retain existing suites. Final implementation guard additions must extend this manifest.

### Cost

These tables retain the **original 182-PC subtotal**. Use the linked custody amendment's combined estimates for dispatch: Code ExpectAbout >=516 minutes, Mutation ExpectAbout >=570 minutes, ordinary V/R 66 minutes, PC floor 540, total verification floor 606. The historical comparisons below do not include the new custody work.

All numbers are **estimated minutes after fixture inspection**, not measured executions or deadlines. Nothing ran in this TestDesign stage beyond document inspection/consistency checks. Floors include fresh builds/results and assertion inspection; unexpected slow builds, repairs, new guards and invalid mutants add work.

| Ordinary Code verification | Minutes | Filters / work |
|---|---:|---|
| Setup/restore and isolated initial build/schema | 6 | Antiphon.Tests graph with bin-c478-vr/, fixture Postgres |
| Admission/publication/contract classes | 6 | Three corresponding PostLandMutation classes |
| Snapshot/cleanup/workflow classes | 10 | Three corresponding PostLandMutation classes, real Git and scoped DB |
| Delivery class and crash matrix | 10 | PostLandMutationDeliveryTests, busy/idle and restart cuts |
| Focused existing compatibility classes | 5 | Explicit list above, once; changed reply/card methods |
| Client type/build and evidence/active-contract audit | 5 | client npm run build; composed contracts and fresh result census |
| **Ordinary V/R floor (Code)** | **42** | Includes setup/build; no deliberate PCs |

Code implementation/test authoring allowance is **240 minutes**, separate from the 42-minute ordinary floor: **Code ExpectAbout >=282 minutes**. Adjust for actual scope; this is not permission to stop before all fixtures and guards exist. Ordinary Review allowance is **30 minutes** (10 judgment + 20 focused ordinary reruns); it does not execute deliberate PCs. Caller bookkeeping and loaded-runtime acceptance reserve **8 minutes**, with 6 minutes of that on companion/identity/dispatch bookkeeping and 2 on the bounded loaded-composition/source probe; use actual deployment duration in addition.

| Post-land Mutation PC group | Controls | Minutes per full red/restore/rebuild/green cycle | Group minutes |
|---|---:|---:|---:|
| Admission, G-1..G-26 | 26 | 2 | 52 |
| Publication predicates, G-27..G-55 | 29 | 1 | 29 |
| Snapshot/settlement, G-56..G-84 | 29 | 2.5 | 72.5 |
| Cleanup, G-85..G-123 | 39 | 2.5 | 97.5 |
| Independent lifecycle, G-124..G-133 | 10 | 2 | 20 |
| Delivery, G-134..G-161 | 28 | 3 | 84 |
| Stage/caller contracts, G-162..G-182 | 21 | 1.5 | 31.5 |
| Fresh L checkout/source baseline and final restoration/evidence census | — | — | 15 |
| **PC floor (Mutation)** | **182** | **Exact methods above** | **401.5; schedule at least 402** |

The 182 rows are **182 distinct planned mutant applications**, not 182 discovered test cases. Each method runs all its named predicate variants; the per-cycle allowance includes those rows and both execution results. Mutation analysis/missing-control discovery/reporting adds **20 minutes**: **Mutation ExpectAbout >=422 minutes**. No zero-cost or omitted guard group is assumed.

**Total verification floor = 42 + 402 = 444 minutes** (setup/build + ordinary V/R + every PC red/restore/green plus final source census). Including ordinary Review and caller acceptance: **482 minutes**, excluding Code authoring, Mutation's 20-minute analysis, actual deployment time and newly discovered work. Total scheduling allowance including listed authoring/analysis is **742 minutes** plus deployment; work moves off the original card's completion path, it does not disappear.

For an equivalent 402-minute battery previously blocking land, the reordered critical path saves approximately **402 - 30 added ordinary Review - 6 companion bookkeeping = 366 minutes** before publication. If ordinary Review would already have run, the corresponding saving is about 396 minutes. This comparison is for this specified battery, not a remeasurement of CARD-0475/0461. Fresh snapshot/card/evidence handling adds total work; no unmeasured parallel speedup is credited.

### TestDesign completion and handoff

Census of this original appendix: **guards=182, mapped=182, defined PCs=182, missing=0, duplicate PC mappings=0**. With the custody amendment, the current total is **230 guards/PCs**, V-1..V-17 and R-1..R-15. Its cost section supersedes the original totals above: ordinary V/R 66 minutes, PC floor 540, combined verification floor 606. Preserve the bijection when implementation reveals more guards. Contract checks are labeled and cannot be offered as native delivery, custody, model-compliance or automatic-orchestration evidence.

Code implements S1..S5 including the custody amendment's S3a-S3c, writes/runs all specified ordinary V/R, commits/pushes and returns **next: review**, with the complete **230-PC inventory pending for post-land Mutation**. Ordinary Review retains the original Code landing owner. Caller records companion obligation, lands the Code task, obtains confirmed O/L, deploys the full server/runner/host feature and verifies loaded behavior before explicitly commissioning Role Mutation at L. The separate Mutation worker owns every PC/variant, discovery, restoration and durable report; neither this Plan nor TestDesign runs that battery.
