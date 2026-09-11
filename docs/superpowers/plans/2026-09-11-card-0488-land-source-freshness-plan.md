# CARD-0488: Land the exact approved, freshly resolved source

Status: Plan complete; TestDesign required before Code. Review is required after Mutation because this changes publication and recovery guards.

Authoring baseline: `91eda74b6a4cfde09516adc4f85fc9622a0c0218`. Plan task: `39e62772-912f-4a1d-81b1-064f122342bd`. Original incident owner: task `26cb0b70`; that is evidence, not the future implementation's landing owner.

## Outcome and scope

Make a fresh land an explicit approval of one full source commit, resolve the remote source before preparation, and retain that approval through queueing, rebase, verification, publication and recovery. A valid publication receipt must identify both the approved original commit and the exact commit verified after rebase. A fresh remote observation must never authorize a different commit by itself.

Investigation task `f511cfa3` supplied this handoff:

> Plan remote-source freshness validation plus durable exact reviewed-SHA binding. Confirmed fresh landing selected local 1ac69f77 while remote source was f6ce6e0c, publishing rebased 6366c80e. Preserve existing recovery and source-movement guards.

Do not implement runtime changes in this Plan stage. Do not repair or re-land historical work, audit other live landings, restart the stack, or change automatic child merge-back in this card's implementation. Existing publication confirmation, repository leases, child journals, target guards, outbox receipts and conservative removal remain mandatory.

Owners: [orchestration loop](../../orchestration-loop.md), [project conventions](../../project-context.md), [card lifecycle](../../agent-card-lifecycle.md), [HTTP operations](../../ops-http.md), [testing/build](../../testing-and-build.md). Code must also read the session-runtime owner before editing settlement/report delivery; this plan adds evidence to its existing path, not a new transport.

## Ground truth

| Assumption or required behavior | Current code at the baseline | Consequence |
|---|---|---|
| Landing used a cached settlement SHA. | `AgentTaskLandingProtocol.RunAsync` creates `OriginalSourceSha` from a fresh `ILandingGit.InspectAsync` snapshot. | The incident was a fresh selection of a stale local branch, not a cached `DeliverableRef`. Do not patch settlement SHA selection. |
| A fetch makes the checked-out source current. | `LandingGit.IdentityAsync` compares registered HEAD, symbolic HEAD and `refs/heads/<source>`; all are local. `ObserveAsync` fetches the destination branch into a unique observation ref. | No remote-source comparison or local source fast-forward exists. A plain fetch cannot repair this. |
| A clean Review identifies what may land. | `LandAgentTaskRequest` accepts only `Verify`; `delegate.ps1 -Land` sends only that optional filter. `StageOutcome.Ref` is untyped, and clean findings have no commit binding. | Prose and a succeeded task cannot constrain source selection. |
| A queued request is an immutable decision. | `AgentTaskLandRequest` stores request identity, filter, destination and clocks but no expected SHA. `RequestAsync` overwrites a pending request's filter. | A request can wait behind a writer without retaining what was approved. |
| An operation already distinguishes pre/post-rebase identity. | `AgentTaskLanding` stores `OriginalSourceSha`, `RebasedSourceSha`, `VerifiedSourceSha`, pins and monotonic publication evidence. | Reuse these fields; add approval provenance rather than replacing verified identity with branch HEAD. |
| Explicit retry only resumes saved work. | At `Verified`, a later request plus `PreparationChangedAsync` can open a new operation; `CanReplaceRefused` accepts freshly inspected changed work. | Preserve safe replacement, but require a new exact approval and request identity for its candidate. Time comparison alone is not approval. |
| Recovery and source movement are already guarded. | `RecheckSourceAsync`, completed-rebase HEAD capture, target guards, repository leases and child journals protect mutation boundaries. | Extend these checks with request/approval and remote-source evidence; do not weaken them. |
| Review always has a correct landing subject. | `RecordDelegateStageOutcomeAsync` sets `SubjectTaskId = FollowUpOfTaskId`; ordinary Review tasks can have no subject. | Machine-readable review evidence must explicitly name the original landing owner; card membership is insufficient. |
| Tests already model a remotely published source tip. | `LandingGitFixture` publishes its seed source, then many tests commit locally ahead; `AssertRemoteSourceAsync` protects that seed. | Preserve local-ahead support and the protected-remote-source assertion. The detached follow-up regression needs its own explicit remote-tip expectation. |

## Decisions

### D-1. Require an exact approval for fresh work

Add `ExpectedSourceSha` to `LandAgentTaskRequest` and `-ExpectedSourceSha` to the `Land` parameter set of `scripts/delegate.ps1`. A fresh request must supply a full 40- or 64-hex object ID, normalized to lowercase. Reject abbreviated IDs, branch names, revision expressions and malformed values with the existing `ValidationException`/422 path. Never fill this value from current HEAD, a tracking ref, a settlement deliverable, the latest same-card report or a later remote fetch.

An explicit SHA is the caller's approval of that commit. It does not assert that a separate Review stage ran. This supports Plan/docs and Mutation-to-Land workflows without inventing a mandatory Review stage for every task. Add optional `ReviewEvidenceId` / `-ReviewEvidenceId` to attach an actual clean Review receipt; if supplied, its subject, source coordinates and SHA must match the land request. A mismatch is a `ConflictException`/409 before enqueueing. Omission never silently selects some other review row.

For a pending request or an existing operation's exact resume/cleanup, omitted fields may inherit the already durable approval. Explicitly different fields never overwrite it. An active queue item continues to produce the existing running-land 409. Fresh bodyless requests now receive 422 `expected_source_sha_required`; update every documented caller in the same release. This deliberate compatibility break closes the unsafe default.

Example, with caller-substituted full identifiers:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land <original-code-task> -ExpectedSourceSha <full-reviewed-sha> -ReviewEvidenceId <review-outcome-guid>
```

Rejected: optional SHA with fallback to HEAD (leaves the incident possible); requiring every approval to come from Review (changes the stage policy); automatically resolving abbreviated SHAs at worker execution (binds too late).

### D-2. Reuse StageOutcome for structured Review evidence

Extend the existing append-only Review `StageOutcome` with nullable `ReviewedSourceSha`, `ReviewedSourceRef`, and `ReviewedRepositoryPath`. The row's `Id`, `SubjectTaskId`, `StageTaskId`, `RecordedAt`, `Source` and `Outcome` already supply evidence identity, subject, reviewer, time, provenance and verdict. Do not use free-form `Ref` as authority.

The reviewer emits exactly one standalone block before the unchanged next-stage block and report token:

```text
--- review evidence ---
subjectTaskId: <full GUID of the original Code/Worktree landing owner>
reviewedSourceSha: <full SHA actually reviewed>
```

Only a successfully settled Review-stage report with a clean finding can produce usable approval evidence. Parse the final settled report, never a distillation, an excerpt or historical prose. Reject duplicate/conflicting blocks, malformed fields, absent/ambiguous subjects, non-Worktree subjects and contradictory subjects. A bad/missing block may still settle the task/report, but records a clear warning and exposes no usable review approval. Never fabricate missing fields. The report's subject is explicit; resolve the source branch/repository from that task and snapshot them onto the row. Validate existing caller/task authorization for this subject; knowing a GUID is not new write authority.

Store the evidence with settlement in the same database commit, idempotent per review settlement. Refactor the best-effort telemetry catch in `RecordDelegateStageOutcomeAsync` so it cannot advertise review approval whose durable row failed to save. Extend the existing finding DTO/CLI with `ReviewedSourceSha` for an authorized explicit `Stage=Review, Clean` finding on the original subject; mark it `Source=Orchestrator`. Reject review-only fields on other stages or Found findings. An override appends a new row; it never copies an old approved SHA implicitly. Superseded/non-clean evidence cannot authorize a new request.

Expose `reviewEvidence` with the evidence ID, explicit subject, full SHA, source ref and verdict in task detail/status. Completion headers should carry the durable evidence ID, subject and SHA, loaded after settlement, so distillation cannot hide the binding. Keep `next=` parsing and stage routing unchanged. A queued request snapshots its selected evidence; later telemetry rows cannot silently change that accepted approval. Revoking/canceling an accepted request remains an explicit request action, not a side effect of a new finding.

Rejected: infer the landing owner from card ID, `ParentTaskId`, an unrelated follow-up or a mentioned SHA; introduce a parallel Review approval table duplicating StageOutcome identity; parse existing report prose into retrospective approval.

### D-3. Observe the actual remote source, with an explicit policy

Under the existing canonical-common-directory lease and writer/child admission guards, resolve `origin`'s single **push endpoint**, as landing already does for the destination. The remote source is the exact `SourceFullRef` on that endpoint. Do not use `branch.*.merge`, a stale `refs/remotes/origin/*`, another default branch, an arbitrary fetch URL or a different remote on error. Persist only its fingerprint, never a credential-bearing URL.

Add a typed source-observation seam to `ILandingGit`/`LandingGit`. Use an exact `ls-remote --refs --exit-code` read and explicit fetch:

```text
git fetch --no-tags --no-write-fetch-head <resolved-push-endpoint> refs/heads/<source>:refs/antiphon/land/<task>/<request>/source-observed/<observation-id>
```

Every observation ref is unique and immutable. Match the fetched commit to the advertised full ref and OID, recheck endpoint identity, and retry a read/fetch race at most three times as the existing target observer does. Distinguish a confirmed absent ref (exit 2) from authentication/network/fetch/parse failures. A stale pin or FETCH_HEAD is never a successful observation. Pass arguments shell-free through the existing Git execution/journal lane.

Let `L` be freshly inspected local HEAD, `R` the freshly fetched remote source, and `E` the durable expected SHA. Apply this table before source fast-forward or rebase:

| Relationship | Candidate and policy |
|---|---|
| `L == R` | Candidate `L`; require `E == L`. |
| `L` is a strict ancestor of `R` | Candidate `R`; require `E == R`, then fast-forward the registered local source worktree to that exact fetched SHA under D-4. If E is the old L, refuse stale approval. |
| `R` is a strict ancestor of `L` | Candidate `L`; allow only `E == L`. Record `LocalAhead` and both SHAs. The local commits are explicitly approved and include the remote work. Do not push the source branch. |
| Neither contains the other | Refuse `source_remote_diverged`, regardless of E. Never reset, force-update or auto-merge. |
| Remote branch confirmed missing | Refuse `source_remote_missing`; require publishing/restoring the source branch before a new request. |
| Remote unavailable, malformed, ambiguous endpoint or fetch fails | Refuse the specific `source_remote_*` reason; no offline fallback. |
| Local branch/worktree missing, detached, dirty, foreign, aliased ambiguously or under an active sequencer | Preserve existing inspection refusal. Do not recreate or switch the checkout. A *different detached follow-up worktree* is permitted; the original landing worktree must remain correctly registered. |

For a candidate mismatch return `reviewed_source_mismatch` with structured `expectedSourceSha`, `localSourceSha`, `remoteSourceSha` and `candidateSourceSha`, including null plus reason when a value is unavailable. Use the same safe fields in the terminal event, status and caller notification. This must work when no landing operation exists yet.

Rejected: fetch only; always prefer remote (drops approved local-ahead work); always prefer local (the incident); allow divergence if E matches either side (silently omits work); delete/recreate a checkout to make the SHAs agree.

### D-4. Journal source fast-forward before operation preparation

Add request-owned source-resolution checkpoints instead of renumbering `LandPhase` or changing the existing integer `HighestProgress` contract. The durable request exists before any source fetch/fast-forward and remains the queue's authority.

Suggested fields on `AgentTaskLandRequest`: immutable approval version, `ExpectedSourceSha`, optional `ReviewEvidenceId`, approval kind/time and source coordinate snapshot; plus `SourceResolutionState` (`None`, `Observed`, `AdvanceStarted`, `Resolved`), local-before SHA, remote SHA/ref/fingerprint, observation ref/time, resolved SHA, canonical common/worktree/git-directory identity, relationship and safe refusal evidence. Track source-advance child intent/PID/start ticks using the same owned-child pattern as the protocol, in addition to the standing repository child journal. The initial approval is immutable; source-resolution fields are checkpoint evidence, not a replacement approval.

Sequence for a new operation:

1. Reload the current request/task after admission. Match request ID, pending state, eligibility, source/target coordinates and approval snapshot. Inspect the source under existing guards.
2. Fetch/validate the source into the unique ref, classify L/R and compare E to the candidate. Save the complete accepted observation and source identity before dependent mutation. On failure, save diagnostic evidence on the request and use the existing terminal transaction/outbox.
3. For behind-only: recheck request identity/approval, local identity/status at L, remote source at the saved R, and recovery observation pin. Save `AdvanceStarted` and exact L-to-E intent before launching the owned command. Run in the registered source checkout: `git -c merge.autoStash=false merge --ff-only <E>`. Never use a direct update-ref on a checked-out branch, force, reset, stash or a branch-name operand.
4. Capture the immediate successful command HEAD and reinspect the full source identity. Require HEAD, local branch and registration all equal E, with clean status and unchanged identity. Save `Resolved` only after this evidence. For equal/local-ahead, save `Resolved` after equivalent checks without a source mutation.
5. Only now create/persist the new landing operation, with `ReviewedSourceSha == OriginalSourceSha == E`, copying the accepted source evidence. Preserve the old active operation and its pins until the complete replacement can be committed using the existing transaction. Then run the existing pin/rebase/verification protocol.

If a source fast-forward fails or a checkpoint write fails, no target mutation follows. Do not roll the source backward automatically: preserve files, refs, diagnostic state and journals. A successful source FF followed by refusal may leave the original worktree at E; make this visible in the outcome.

On restart, resolve `AdvanceStarted` only after child/journal and identity guards permit admission. An exact clean L can retry the saved L-to-E operation after current remote validation; an exact clean E can acknowledge that same completed intent without advancing again. Any third SHA, changed identity, dirty state, unknown/live child or sequencer refuses/holds. Recovery cannot fetch a newer SHA and change the saved destination. A fetch whose evidence was never committed may be repeated with a new observation ref; uncommitted pins grant no authority.

### D-5. Preserve approval and operation identity through rebase and retry

Add `ApprovalLandRequestId`, `ReviewedSourceSha` and nullable `ReviewEvidenceId` to `AgentTaskLanding`, with immutable remote-source snapshot/provenance copied from D-4. `OriginalSourceSha` remains the approved pre-rebase commit. `RebasedSourceSha` is the command-completion SHA captured by the existing Git implementation; `VerifiedSourceSha` remains the exact verified/prepared SHA. Never overwrite any of these with current HEAD on resume.

Also record `PreparationInputSha` (normally E) and nullable `PreviousPreparationOperationId`. These distinguish a fresh source from an unchanged, already-recorded rebase result reused for an explicit retry. Without this distinction a legitimate retry at rebased P would either be falsely rejected as divergent from remote original O or would relabel P as a newly reviewed original. The exception below is for a witnessed derivation only, never arbitrary branch movement.

New operations use schema version 2. Extend `AgentTaskLandingState` to require the approved original identity and request binding for v2 mutation/publication, while preserving the existing v1 receipt predicate for historical evidence/cleanup. Check equality and valid full OIDs in service/state guards and enforce appropriate versioned non-null/equality constraints in the database. Preserve unknown-schema refusal. Do not renumber phase values.

Pass the durable request explicitly into protocol execution; do not rely only on `task.LandVerifyFilter` or the in-memory queue payload. At each existing pre-mutation source recheck, additionally validate the current request and operation link, saved approval/filter/coordinates, and original/prepared pins. A new retry request may link to an existing operation, but its E must equal that operation's approved Original. `ApprovalLandRequestId` remains the operation's original provenance, not the newest retry ID.

Persist verification filter as part of the accepted request and operation. Identical inactive-pending re-POST preserves request ID, age, attempts, E, evidence and filter. A different E/evidence/filter is 409 `land_request_identity_conflict`, not a mutation of that request. Omitted fields on an exact resume inherit stored values. Automatic sweeps and stale queue wakeups cannot create new approval or rebind an operation.

| Existing state | Explicit request / recovery policy |
|---|---|
| Queued or Held, no operation | Execute the saved E. If source movement makes the candidate different, terminal refusal; a new explicit request approves the new candidate. |
| Source FF intent interrupted | D-4 recovery of only its saved L/E pair; never discard uncertain child evidence. |
| Inspected/RecoveryPinned/Prepared | Resume only the same E and phase-appropriate local SHA. A new source is not a resume. |
| RebaseStarted interrupted | Keep the existing inspection-required refusal; no automatic abort/rebase/operation replacement. |
| Verified, unchanged preparation | Resume the original E with local source equal to saved VerifiedSourceSha, even when those two SHAs differ due to rebase. Never compare E to the rebased SHA as though it were fresh approval. |
| Verified, changed source/target/filter/checkout/destination | Only a new explicit request may replace the operation before target-advance intent, after fresh validation. `PreparationChangedAsync` may identify change but cannot authorize it. Unchanged recorded P can use the derivation-only retry below; a different source requires a new E and D-3 resolution. An ordinary pending request must first reach its existing refusal/outcome before a different request is accepted. No automatic reset to O. |
| Refused before target-advance intent | Preserve unchanged-source retry: a new explicit request can approve the same SHA and create a fresh operation, including the derivation-only retry below when an earlier rebase completed. Changed-source retry requires its own new E. Retain the old row/pins; a failed replacement leaves it active. |
| NeedsResolution after an aborted conflict | Same approval/filter can resume after task success and revalidation. A repaired source requires new approval. Allow explicit supersession only here, while inactive and task Succeeded, with the old operation proved safely Refused before target intent and no uncertain child/sequencer. Atomically close the old request as Superseded with durable outcome/notification and create the new request with E; do not edit the old request. Preserve its conflict receipt and request age. |
| TargetAdvanceStarted/LocalTargetAdvanced/PushStarted | Never replace the operation, even on explicit re-POST. Resume only its original approval and verified payload; otherwise refuse without losing unresolved evidence. |
| PublicationConfirmed or cleanup residue | Retry existing guarded cleanup for the same operation and receipt. No new source FF/rebase or fresh work selection. A different supplied E is a conflict; omission can use stored identity. |

For a **derivation-only explicit replacement**, require a safely replaceable predecessor with the same approved Original O, a durably captured completed rebase P, matching immutable original/prepared pins, no uncertain child/sequencer, and current source exactly P with the same identity. The new explicit request must approve E=O and name the same evidence or an explicit caller approval of O; automatic recovery cannot open this replacement. Apply remote-source ancestry to O (require remote R equal to or an ancestor of O), not to P; a remote newer than or diverged from O still refuses. Do no source FF. Persist a new operation with Original/Reviewed=O, PreparationInput=P and the predecessor ID. Pin O and P separately before rebase, then capture its newly rebased/verified Q. Keep the predecessor and its evidence unchanged. An unrecorded P, broken pin, mismatched original or changed local HEAD cannot enter this path.

Adapt the pre-rebase local source rechecks to `PreparationInputSha`; approval checks remain against Original O. Exact target containment may prove the pinned input P already present, so that path records Verified=P with its input/predecessor provenance instead of asserting P=O. Otherwise a derivation-only replacement always reruns verification, even if rebase leaves P unchanged; it cannot inherit a prior failed verifier or use the ordinary fresh-input `base_unchanged` skip. Extend the versioned state/receipt predicate for this explicit lineage and add positive controls for bypassing it. For ordinary fresh operations, input=Original and the existing skip rules remain unchanged.

After target advance/push intent, recovery may independently observe that the saved verified commit was already published. Do this before classifying a changed source as requiring new work: an existing publication must remain a fact. If confirmed, retain the same operation and run only receipt-based conservative cleanup, which still refuses moved local source. If unconfirmed, current request, source and remote-source guards must pass before any new target/push mutation. Do not claim that refusal implies target stayed unchanged.

### D-6. Recheck remote movement without adopting it

For a v2 operation the source remote baseline is R from its saved source resolution, not its rebased local SHA. Re-observe the exact source ref at resume, before source FF, before the rebase child, after preparation/verification before target intent and immediately before target mutation/push. Also check after the target observation before a fresh `AlreadyPresent` shortcut confirms publication and authorizes cleanup; that shortcut cannot bypass new-source freshness. The D-5 read-only reconciliation of saved publication intent is a separate historical-evidence path. Require the same fingerprint and SHA R; distinguish movement, disappearance and unreadability. Local-ahead's R may differ from E, and rebase's P normally differs from E: neither permits silently changing R.

An observed move to any other remote source SHA refuses `source_remote_changed` with baseline/current/approved SHAs. Preserve local source, target checkpoint, recovery refs and any publication evidence. A new pre-publication request may perform fresh resolution under D-3/D-5. A remote move from R toward already-approved E is still a changed snapshot; conservative refusal keeps the rule deterministic.

Git cannot atomically lock a remote source ref against unrelated pushers while pushing a different target ref. The guarantee is fresh source checks at named boundaries and an immutable approved payload, not an impossible perpetual equality of both remote refs. A move after the final source read cannot make the pushed payload newer than the approved derivation; receipts record the last source observation time/SHA. Do not add a force push, source rewrite or a claim of atomic two-ref publication.

### D-7. Migrate without inventing historical approval

Generate the EF migration with the CLI and update its snapshot. Add nullable evidence columns for legacy data; mark new requests/operations with explicit versions. Never backfill `ReviewedSourceSha` from task HEAD, `OriginalSourceSha`, event prose or successful verification. Historical publication and historical approval are separate facts.

Legacy queued requests with no operation/approval must refuse `legacy_review_binding_required` before source or target mutation; a fresh explicit request supplies E. Legacy published operations retain their current v1 receipt-based cleanup path, visibly lacking review approval. They do not need a surviving source branch merely to repeat guarded cleanup.

For an unpublished v1 operation, automatic recovery can inspect/confirm an already published saved verified SHA, but cannot launch new rebase/advance/push without approval. An explicit request may late-bind only E equal to the saved OriginalSourceSha, with matching source coordinates/pins and phase-appropriate local HEAD. Freshly observe the remote source relative to original E (R must equal or be an ancestor of E; never fast-forward a rebased P). Save a new approval request and the operation's one-time v2 binding/source snapshot before further mutation, recording the actual binding time as late approval. Preserve operation ID, original/rebased/verified SHAs, phase and prior pins; input=the original pre-rebase input, not the currently observed P. Store the phase-appropriate local observation separately; do not pretend a fresh E checkout was measured. No backdating or receipt rewriting. Existing uncertain-child/interrupted-rebase guards still take priority. A mismatch needs repair/review, not automatic replacement after target intent.

Update readers/writers together and inventory pending legacy requests before rollout. No feature flag should leave fresh SHA-less landing enabled. Do not deploy this partially: source FF alone could select newer unreviewed code, and API fields alone cannot make a stale checked-out branch current.

## Implementation slices

| Slice | Files / change boundary | Named coverage to implement or retain |
|---|---|---|
| S1: evidence and persistence | `server/Domain/Entities/{StageOutcome,AgentTaskLandRequest,AgentTaskLanding}.cs`; appropriate Domain enums; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*`; `server/Application/Dtos/{StageOutcomeDtos,AgentTaskDtos,LandRequestStatusDto,LandingEvidenceDto}.cs` | New `AgentTaskReviewEvidenceTests`, `AgentTaskLandApprovalPersistenceTests`; existing `AgentTaskLandingPersistenceTests`, `AgentTaskLandingStateTests`, `AgentTaskLandRequestTests`. |
| S2: produce exact review evidence and admit requests | `AgentTaskReplyService.cs`, `DelegationReportFormatter.cs`, a small pure `ReviewEvidence` parser beside `PipelineHandoff.cs`, `StageOutcomeService.cs`, `AgentTaskLandService.cs`, `server/Api/Endpoints/AgentTaskEndpoints.cs`, `scripts/delegate.ps1` | New `ReviewEvidenceParserTests`, `DelegateScriptLandApprovalTests`, `AgentTaskLandApprovalRequestTests`; existing `StageOutcomeFindingEndpointTests`, `StageOutcomeSummaryTests`, `DelegateScriptLandStatusTests`, affected settlement/formatter tests selected by actual changed methods. |
| S3: remote source and guarded FF | `ILandingGit.cs`, `LandingDtos.cs`, `LandingGit.cs`; request-owned source resolution in a concrete application service (e.g. `AgentTaskLandSourceResolver.cs`) called after existing admission; DI registration if needed | New real-Git `LandingSourceFreshnessTests`, `AgentTaskLandSourceFreshnessTests`; existing `LandingGitTests`, `LandingIdentityControlTests`, `LandingSourceBoundaryControlTests`, `AgentTaskLandAdmissionControlledTests`. |
| S4: bind protocol and recovery | `AgentTaskLandingProtocol.cs`, `AgentTaskLandingState.cs`, `AgentTaskLandService.cs`; queue/worker signatures only as needed to carry request identity; receipt readers only where version-aware evidence requires it | New `AgentTaskLandApprovalRecoveryTests`; existing `AgentTaskLandPreparationIdentityTests`, `AgentTaskLandRefusedRetryTests`, `AgentTaskLandRecoveryTests`, `AgentTaskLandCheckpointMatrixTests`, `AgentTaskLandPersistenceFailureTests`, `AgentTaskLandConcurrencyControlledTests`, `AgentTaskLandCleanupSafetyTests`, `LandingRemovalPolicyControlTests`. |
| S5: expose evidence and document callers | `client/src/api/agentTasks.ts`, `client/src/features/delegations/TaskDetailBody.tsx`; completion/status formatting; `docs/{orchestration-loop,ops-http,antiphon-api}.md`; `server/Bundles/{stage-review,stage-mutation,orchestrator}.md`; relevant delegate skill/help examples | `TaskDetailBody.test.tsx`, script tests above, affected bundle-contract tests; `AgentTaskLandNotificationPersistenceTests`, `AgentTaskLandNotificationRecoveryTests`, `AgentTaskLandReceiptTests`, `AgentTaskLandDeliveryE2ETests`. |

S1-S4 are one release safety boundary, not separately deployable checkpoints. Code must census actual call sites of `RequestAsync`, `LandAgentTaskRequest` and request-seeding harnesses and update them with explicit fixture approval; avoid a permissive test-only overload which bypasses production validation. Domain code stays infrastructure-free; Git stays behind its I/O seam. No new generic repository/service interface layer is needed.

Status and notification text must distinguish approved original, local before resolution, observed remote source, rebased/verified source and remote target. Show a legacy missing approval honestly. In a refusal before operation creation, read request evidence instead of a prior active operation's unrelated source SHA. Preserve existing request/event/notification correlation and terminal transaction idempotency.

## Acceptance requirements for TestDesign

This is the input to a separate TestDesign stage, not a completed V/R/PC matrix. TestDesign must add the executable `## Verification design`, exact methods, controls and separate Code/Mutation time floors before Code starts.

| Requirement | Required setup and oracle | Coverage owner |
|---|---|---|
| A1: incident reproduction | Original succeeded owner has clean registered local A. A separate detached worktree makes production change B and pushes `HEAD:<same-source-branch>` without moving the original local branch. Clean review evidence approves full B. Queue land through original owner. Assert local FF precedes rebase, operation Original/Reviewed=B, verified=P, and independent remote-target tree includes B's unique production fix. Assert remote source remains B. A target-only success flag or SHA substring is insufficient. | `AgentTaskLandSourceFreshnessTests` |
| A2: stale or mismatched review | Repeat A1 approving A; separately supply an evidence ID for B with expected C, wrong owner/branch/repository, missing/invalid SHA and a non-clean/superseded finding. Request-level rejection has no queued row; worker-level mismatch has a terminal request with expected/local/remote/candidate fields. No source FF, rebase, verifier, target advance, push or cleanup follows mismatch. | `AgentTaskLandApprovalRequestTests`, `AgentTaskReviewEvidenceTests`, `AgentTaskLandSourceFreshnessTests` |
| A3: complete source policy | Equal, behind, approved local-ahead, diverged, truly absent remote source, unavailable remote, advertised/fetched race, ambiguous push endpoints and different fetch/push URLs. Include 40/64-character OID parsing and unsupported/malformed evidence. Check exact destination/refspec; no FETCH_HEAD/tracking-ref dependence and no source-branch push/delete. | `LandingSourceFreshnessTests`, `ReviewEvidenceParserTests` |
| A4: queue/prepare movement | Hold behind a writer; move local/remote source after POST. Inject movement after observation, before FF, after FF, before rebase child, after completed rebase, during verification, before target intent/mutation and before push. Test request/evidence/filter/coordinate movement too. Assert correct refusal, immutable E and old/new refs; after target advance assert honest unconfirmed state, not an untouched-target claim. | `AgentTaskLandSourceFreshnessTests`, `LandingSourceBoundaryControlTests`, `AgentTaskLandConcurrencyControlledTests` |
| A5: crash around new source checkpoints | Fail commits before/after Observed, AdvanceStarted, child start, successful FF, Resolved and operation insertion. Rebuild the DI scope/service process against the same DB/repo. Cover real worker death plus standing journal admission, clean L retry, clean E acknowledgment, third SHA refusal, and failed replacement preserving old active row/pins. | `AgentTaskLandApprovalRecoveryTests`, `AgentTaskLandPersistenceFailureTests`, `AgentTaskLandRecoveryTests` |
| A6: identity-preserving retries | Exact pending re-POST preserves age/E/filter; changed pending request 409; stale wakeup cannot run against a newer request; unchanged Refused retry creates fresh operation; repaired conflict explicit supersession preserves both requests; changed Verified work needs new approval; E=O resumes verified P without false mismatch. Explicit derivation-only retry retains O/P/predecessor evidence, accepts unchanged witnessed P despite O/P graph divergence, and verifies Q again; unrecorded P or a newer remote source refuses. No replacement after target intent. | `AgentTaskLandApprovalRequestTests`, `AgentTaskLandPreparationIdentityTests`, `AgentTaskLandRefusedRetryTests`, `AgentTaskLandApprovalRecoveryTests` |
| A7: publication/cleanup/legacy | Lost push acknowledgement with remote containment preserves operation and approved/verified SHAs, including a subsequently moved remote source. No second payload is pushed. Changed local source is retained by cleanup. Published v1 cleanup works without invented approval; unapproved v1 new mutation refuses; valid explicit late binding preserves original operation and time/provenance; legacy queue needs new E; unknown schema refuses. | `AgentTaskLandApprovalRecoveryTests`, `AgentTaskLandCleanupSafetyTests`, `AgentTaskLandingStateTests`, `LandingRemovalPolicyControlTests` |
| A8: durable caller evidence | Drive actual Review settlement to durable evidence/header and exact CLI/API request. Drive both pre-operation mismatch and successful land to the existing terminal transaction, notification worker, real message queue and isolated transcript-confirmed recipient. Cover busy/already eligible callers, failed enqueue, restart, lost wakeup and duplicate delivery boundaries, retaining request/event/operation/evidence IDs and full SHAs. | `AgentTaskReviewEvidenceTests`, script tests, `AgentTaskLandNotificationRecoveryTests`, `AgentTaskLandReceiptTests`, `AgentTaskLandDeliveryE2ETests` |

TestDesign must name a compiling positive control for each new approval, ancestry, source-FF, remote movement and recovery guard, including skipping source fetch, accepting the stale local SHA, dropping expected-SHA persistence, adopting HEAD on resume, bypassing request mismatch, and acknowledging an unrecorded FF. Do not satisfy A1 solely with a fake Git response: use real bare remotes, original and detached follow-up checkouts, a separately fetched observer and unique file-content evidence. Existing fixture local-ahead coverage remains valid; never globally change `AssertRemoteSourceAsync` to accept arbitrary source movement.

Delivery inventory to expand: Review settlement produces a `StageOutcome.Id`/subject/SHA; the existing completion queue carries those fields to the original caller and recovers through existing completion machinery. The explicit land POST produces `LandRequest.Id`/E; the existing land worker commits `AgentTaskEvent.Id` plus `AgentTaskLandNotification.Id`, optionally `LandingOperation.Id`, and the notification worker enqueues the keyed queue message. Only a matching complete UserPrompt after the attempt floor proves session receipt. API acceptance, row existence, Sent, polling or transport ACK is insufficient. Reuse the queue/recovery facilities; do not create a new sender for SHA evidence. Test harnesses must isolate runner and database scope and avoid live provider launches.

Ordinary validation uses the documented Unit lane plus the named affected integration classes above, split into bounded class groups with fresh nonzero TRX counts. TestDesign must refine the slice list after inspecting actual coverage, especially settlement/formatter classes; names marked new are planned test classes. Use producer-owned output such as `bin-c488/`, `dotnet run --project tests/Antiphon.Tests`, and `pwsh -File scripts/test-client.ps1 TaskDetailBody.test`. Rebuild `client/dist` before any E2E that serves it. Run process-spawning assemblies sequentially and retain their assembly-local limiter; no production runner or local stack restart is part of test setup. Do not credit the currently absent nightly job as a safety backstop.

Code runs ordinary V/R, commits/pushes and hands all PCs to Mutation in its retained worktree. Mutation restores every change and reports tested commit versus final artifact commit. Required Review checks the final exact source SHA, including any later plan/evidence-only commits, and produces D-2 evidence for that final SHA. The caller lands the original **implementation Code owner**, with `-ExpectedSourceSha` copied from that evidence, not the Plan, detached follow-up, Mutation or Review task ID.

## Rollout and limits

Code/Review evidence must include migration validation on legacy rows, API/CLI compatibility coverage and the new real-Git regression. After the usual authorized canonical deployment, verify the loaded API accepts and echoes the SHA-bound request contract and that a disposable isolated-repository canary reproduces A1 successfully. Health alone is insufficient. Do not use a deliberately wrong SHA against real work as a canary or deploy from this Plan worktree.

This plan needs no product decision to proceed. It chooses explicit caller approval, strict missing/unavailable remote refusal, local-ahead support, immutable retries and versioned legacy recovery as the implementation defaults. It does not promise that a clean automated test suite is a code review, that branch names cannot move after a final remote read, or that legacy receipts prove historical review.
