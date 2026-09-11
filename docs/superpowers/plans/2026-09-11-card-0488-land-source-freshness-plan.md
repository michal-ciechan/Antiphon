# CARD-0488: Land the exact approved, freshly resolved source

Status: Plan and TestDesign complete; ready for Code. Review is required after Mutation because this changes publication and recovery guards.

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

The A1-A8 table records the Plan-stage acceptance input. The completed executable V/R/PC matrix, exact methods, controls and separate Code/Mutation time floors are in `## Verification design` below.

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

## Verification design

Status: TestDesign complete at baseline `5e5850f3425df53ba7170a55c424f30e435953e3`; execution is pending Code and Mutation. Severity: **CRITICAL**. This section verifies D-1 through D-7 without changing those decisions. A1-A8 are acceptance requirements, not evidence of execution.

### Inspection

Paths are repo-relative. Inspection means the named bodies/sections were read; class reruns below are regression selections, not a claim that every method was inspected.

| Bodies / fixture sections read | Boundaries -> coverage |
|---|---|
| `tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs`: initialization, independent source assertion, hooks, capture/disposal; `Infrastructure/LandingGitTests.cs`: endpoint, racing observation, immutable pin, exact push, malformed ref | V-1/V-4, R-1/R-3. Default remote-source protection asserts the seed. Add an explicit-B observer for the incident; retain seed assertions for local-ahead tests. |
| `TestHelpers/LandingSafetyHarness.cs`: DI, request/queue execution, restart, save/transaction faults, verifier, crash worker; `LandingProtocolHarness.cs`: DI, request/restart and controlled verifier | V-1/V-5/V-6. Safety harness uses real Git; protocol harness uses controlled Git. Extend request-owned save cuts and source commands; unsupported fake commands stay loud. |
| `Application/AgentTaskLandRequestTests.cs`: creation, active 409, pending requeue, concurrent acceptance, stale work, conflict resume | V-2, R-2. Replace pending filter-overwrite expectations. Concurrent different approvals conflict; identical approvals keep one request. Update SHA-less request-seeding fixtures through production validation. |
| `AgentTaskLandPreparationIdentityTests.cs`: missing rebase HEAD, changed Verified preparation, after-rebase movement, post-target intent; `AgentTaskLandRefusedRetryTests.cs`: failed preparation/replacement transaction; `AgentTaskLandRecoveryTests.cs`: interrupted evidence, failed replacement, worker-death setup | V-5/V-6/V-7, R-4/R-5. Witnessed P is a derivation of O, not a newly reviewed original. |
| `AgentTaskLandBoundaryControlledTests.cs`: parameter matrix, save/command gates and assertions; `Infrastructure/LandingSourceBoundaryControlTests.cs` wrappers | V-5, R-4. Replace “second fetch” triggers with exact ref/endpoint/working-directory/phase triggers; source fetch insertion must not silently disarm old tests. |
| `AgentTaskLandPersistenceFailureTests.cs`: checkpoint acknowledgement and push-result bodies; `AgentTaskLandNotificationPersistenceTests.cs`: concurrent terminal settlement and old-schema upgrade setup | V-3/V-6/V-8. Save, transaction commit, and acknowledgement are separate cuts. Read committed state through a second connection. |
| `server/Application/Services/AgentTaskLandingState.cs`; `AgentTaskLandingStateTests.cs`: exact verification predicate/transitions; protocol guard/mutation call sites | V-3/V-7. Version and lineage guards need distinct controls; retain v1 receipt meaning and integer phases. |
| `AgentTaskReplyService.RecordDelegateStageOutcomeAsync`; `StageOutcomeFindingEndpointTests`; `MutationPipelineTests.C470_code_settlement_persists_mutation_ready`; `AgentTaskSettlementRaceTests.concurrent_on_turn_end_for_the_same_task_enqueues_one_parent_note`; `OutputDistillationDeliveryTests` settlement/header bodies; `PipelineHandoffParseTests` | V-2/V-8/V-9, R-2/R-7. Current Review subject is inferred from follow-up and evidence recording is best-effort telemetry. Use final settled report and durable explicit subject. Preserve routing/dedupe/header. |
| `DelegateScriptLandStatusTests.C467_V18_StatusAndAcceptance`, HTTP stub and `DelegateScriptRunner.RunAsync` | V-2/V-9. Run the actual script and capture POST JSON; text inspection is insufficient. |
| `client/src/features/delegations/TaskDetailBody.test.tsx`: DTO builders, MSW server and C470 handoff assertion; `AgentTaskLandStageOutcomeTests.SeedBuildableAsync`; `LandingVerifier.VerifyAsync` and `AgentTaskLandService.VerifyWithObserverAsync` | V-1/V-9. Extend the existing drawer fixture. A real selected-filter verifier needs the disposable repository's own `tests/Antiphon.Tests` executable and a fresh passing TRX; a lone buildable library is insufficient. |
| `AgentTaskLandNotificationRecoveryTests` keyed-race/retry bodies; `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` and seed helper | V-8, R-7. Destination, identity, complete body and attempt floor are independent guards. Synthetic transcript rows test only the predicate. |
| `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs`; `Fixtures/LandDeliveryFixture.cs` initialization, CLI request, runner identity, receipt and one-prompt checks | V-8/V-9, R-1/R-7. Reuse real Program/queue/runner/native FakeGrok. Existing fixture does not publish its source and assumes no rebase in containment: publish seed explicitly, approve arranged SHA, and add independent target-tree inspection. |
| `server/Bundles/stage-test-design.md`, `stage-mutation.md`, orchestration stage/landing contract, `docs/testing-and-build.md` | Cost/execution below. No sub-delegation, live runner or full-assembly default. |

New approval/parser/source/recovery classes and Review-to-recipient coverage are planned. Nearest fixtures are above. Setup to implement: request checkpoint fault matching; working-directory-aware source-FF traces; pause/ready barriers for source child intent/start/exit; exact source observer; Review settlement through the existing native delivery fixture. These are test-harness extensions, not a second production sender or permissive approval overload.

### Delivery inventory

| Producer -> destination | Durable join / persistence | Recovery and observable receipt |
|---|---|---|
| Successful clean Review settlement -> original caller completion | Review task/settlement -> StageOutcome ID, explicit subject, full SHA/ref/repository -> existing completion note/queue SourceTaskId and digest. Evidence and settlement commit together. | Cuts before save, after save before commit, after commit before enqueue, queue insert before linkage, lost flush, before typing, after UserPrompt before verdict/receipt. Restart/replay yields one evidence row and one completion obligation. Header loads durable evidence under raw/Apply-distilled/spilled reports. |
| Explicit CLI/API POST -> land worker | LandRequest ID, E/evidence/filter/coordinates/caller snapshot commit before wakeup | Lost wakeup/boot sweep keeps E/age; stale queue cannot execute new request. 202 proves admission only. V-2/V-6/V-8. |
| Land outcome -> caller obligation | Request ID -> terminal AgentTaskEvent ID -> AgentTaskLandNotification ID; optional LandingOperation ID, evidence ID/full SHAs, same terminal transaction | Success, pre-operation mismatch, conflict/supersession and cleanup retry. A refusal with no operation reads its request, including explicit null/unavailable values, never an old operation. No terminal event without owed notification. |
| Notification worker -> queue -> original session | Notification key -> SessionQueuedMessage ID/SourceLandNotificationId, destination/digest/attempt floor -> confirming session/sequence/time | Enqueue exception, concurrent reconcilers, crash after insert before link, lost flush, failed receipt save recover same row/body and one complete prompt, without another publication. Caller edits cannot redirect accepted debt. |
| Status/detail/header -> CLI/task drawer | Same stored request/evidence/op IDs and distinct full SHAs | V-9 checks projection and honest missing legacy approval. Polling cannot mark delivery; this path is not an asynchronous receipt substitute. |

**Native acceptance:** extend LandDeliveryFixture to settle a final Review report through OnTurnEndAsync, obtain persisted evidence from the completion actually received by its isolated caller, then run the real delegate.ps1 against the original Code owner with exactly that SHA/evidence ID. Cover initially busy and already eligible callers, plus successful B publication and stale-A pre-operation refusal. Require the complete expected body via PromptSubmissionMatch.IsCompleteIn, correct session, and sequence strictly above LastDeliveryBaselineSequence; absent sequence uses the existing attempt-start/tolerance predicate. Independently check all full SHAs/IDs. Two later reconciliations still show one native prompt per key.

Native FakeGrok substitutes only for a paid provider: it proves queue/pty/transcript ingestion, not model understanding. Controlled verifier proves exact input/filter invocation; V-1 also runs a real verifier on a buildable production-file fixture. Controlled Git cannot satisfy the detached-push, graph/refspec, hard-death or target-tree acceptance. Hand-seeded transcript rows, Sent, transport ACK and status polls cannot satisfy native acceptance.

### Proves it works now

Every C488 method below is **to implement**. Existing C448/C467/C470 names identify retained coverage. Class aliases and guard methods are defined below. All guard methods also run green during Code. Parameterized methods must execute every named row with fresh nonzero TRX evidence; skipped cases remain uncovered.

| ID | Layer and exact methods | Setup and decisive oracle |
|---|---|---|
| V-1 | Real Git/service: SF.C488_DetachedFollowUpPublishesReviewedFix | Buildable production FreshnessDecision.cs at A returns false; original owner settles at A. A separate detached worktree changes production behavior to true plus unique fixture nonce, commits B and pushes HEAD to the SAME source ref. Prove original registered branch/HEAD stays A while independent observer sees B. Advance target independently to T so rebase produces P != B. Clean Review approves B; POST original owner E=B. Committed request precedes source FF, FF in original checkout precedes rebase, Original=Reviewed=B, input=B, Rebased=Verified=P, verifier runs at P. Independent observer fetches target: compile/read exact true behavior and nonce, retain T's change, and observe remote source exactly B. Repeat unchanged target, and real verifier with selected fixture filter. |
| V-2 | Parser/API/script/settlement: PA.C488_ReviewBlockGrammar; EV.C488_ReviewSettlementEvidenceMatrix; AP.C488_ApprovalAdmissionMatrix; AP.C488_PendingIdentityMatrix; SC.C488_PostsExactApprovalJson | 40/64 hex and uppercase normalization; missing/short/nonhex/revision/embedded newline. Missing/duplicate/conflicting/quoted evidence block, bad subject, non-clean/non-Review/failed settlement. Invalid fresh SHA -> 422, evidence mismatch -> 409, no request/event/enqueue. Caller SHA without Review is valid. Omitted exact-resume fields inherit stored values; each explicit pending difference conflicts. Separate contexts test concurrent same/different approvals. |
| V-3 | DB/state: PS.C488_MigrationLegacyAndV2Constraints; PS.C488_ApprovalRoundTrip; ST.C488_VersionedReceiptMatrix | Downgrade isolated schema to migration immediately before new CLI-generated migration, insert legacy queue/unpublished/published/cleanup rows with actual old-schema SQL, upgrade twice. Approval remains null; IDs/phases/age/pins unchanged. New evidence roundtrips through fresh context. Raw writes reject each v2 OID/non-null/equality constraint by named SQL constraint; pure state rejects each malformed binding/version/lineage separately. V1 receipts readable; unknown version refuses. |
| V-4 | Real Git: SG.C488_SourcePolicyMatrix; SG.C488_ObservationRaceMatrix; SG.C488_ExactPushEndpointObservation | Equal/behind/local-ahead/diverged graphs, E=candidate/other; missing exact ref versus unreadable/parse/fetch error. Different fetch/push repositories, multiple push endpoints, poisoned tracking ref/FETCH_HEAD, wrong full ref/noncommit OID, 0/1/2/3 read-fetch races. Unique immutable observation, fetched=advertised commit, <=3 attempts; no force or source push/delete. Actual SHA-256 repo when supported; unsupported format specifically refuses, never abbreviates/falls back. |
| V-5 | Controlled matrices + real probes: SF.C488_QueuedApprovalSurvivesWriterHold; SF.C488_SourceMovementBoundaryMatrix; SF.C488_RequestMovementBoundaryMatrix; SF.C488_LocalIdentityBoundaryMatrix | Remote boundaries B: after observation, pre-FF, post-FF, pre-rebase-child, completed rebase, during verification, pre-target-intent, pre-target-mutation, pre-push, resume, fresh AlreadyPresent. All applicable B x descendant/diverged/deleted/unreadable/endpoint-change cases, including R moving toward approved local-ahead E. At each mutable boundary independently change request ID/state/E/evidence/filter/task coordinates. Local third SHA/same-SHA branch switch/dirty/staged/untracked/registration/common-dir/git-dir at pre-FF/post-FF/completed-rebase and retained protocol fences. Barrier fires; saved E/R immutable; exact refusal; no subsequent prohibited commands. After local target advance retain P and unconfirmed state honestly. Real probes at FF/rebase/push/AlreadyPresent complement controlled cases. |
| V-6 | DB/real crash: RC.C488_SourceCheckpointCrashMatrix; RC.C488_SourceAdvanceWorkerDeathMatrix; RC.C488_SourceAdvanceResumeMatrix; RC.C488_ReplacementAndSupersessionMatrix | Before-save/after-save-before-commit/commit-failure/after-commit-ack cuts for Observed, AdvanceStarted, Resolved and operation insertion. Real death before source child start, after journal/start identity, after successful FF before Resolved, after Resolved before insertion. Fresh DI/worker same DB/repo. Clean saved L retries same pair; clean E acknowledges durable intent only; third SHA/dirty/identity/journal/sequencer refuses or holds. Failed replacement retains old active row/pins; repaired conflict supersession retains old outcome/debt/age and commits new E atomically. No rollback of source. |
| V-7 | Service + real O/P/Q graph: RC.C488_RebasedInputLineageMatrix; RC.C488_VerifiedResumeMatrix; RC.C488_LegacyRecoveryMatrix; RC.C488_PublicationReconciliationMatrix | O rebased onto T -> witnessed P where O is not ancestor of P; required verifier fails. Explicit E=O retry with input=P/predecessor -> Q, verify again even Q=P. Unrecorded P/broken pins/other predecessor/changed HEAD/newer or diverged R/automatic retry refuse. Exact contained input P -> Verified=P with lineage. E=O resumes verified P; no replacement at/after target intent. Lost push ACK plus published P then source movement confirms saved P/same op; unpublished movement refuses. Published v1 cleanup without source, unapproved v1 mutation refusal, one-time valid late binding E=original at actual time and mismatches. |
| V-8 | Native queue: DE.C488_ApprovalOutcomeReceiptMatrix; DE.C488_ReviewToLandReceiptMatrix; DE.C488_ReviewDeliveryCrashMatrix; DE.C488_ApprovalDeliveryCrashMatrix | Delivery inventory: busy/idle x successful B/stale A; Review busy/idle x raw/Apply/spilled. Each inventory handoff cut for both Review delivery and land success/pre-operation refusal. Retain C467_V22 through C467_V32 with explicit approved fixtures. Complete native prompt joins full IDs/SHAs; receipt recovery never changes publication mutation count/op. |
| V-9 | CLI/projection: SC.C488_StatusShowsDistinctSourceFacts; EV.C488_CompletionHeaderUsesDurableEvidence; EV.C488_ExplicitFindingEvidenceMatrix; TaskDetailBody.test.tsx case “shows exact approval, source resolution, verification and legacy evidence” | Real script JSON exact E/evidence/filter/original task ID. Header/detail/API distinguish A/B/P/R/remote target. Pre-op failure plus old active op displays new request evidence. Legacy approval visibly absent. Authorized Review/Clean override appends row; never copies superseded SHA. Preserve next= and role policy. |

Fixtures must record working directory, exact argv and separately committed request/op at mutation boundaries. Independent observer bypasses fault hooks. Capture refs, registration, status, index/content digests and verifier SHA/filter/count. Assert every fault barrier was reached. Invalid fixture endpoints and synthetic diagnostic markers replace network credentials. O/P/Q and the incident use real Git; the broad boundary product uses controlled Git for cost.

For V-1's real selected-filter arm, seed a disposable solution with a LandProbe library and an executable `tests/Antiphon.Tests` project using the repository's pinned TUnit versions. Its exact method `FreshnessProbeTests.ApprovedFixIsPresent` asserts the production method returns true and the fixture nonce matches. Use filter `/*/*/FreshnessProbeTests/ApprovedFixIsPresent`; require its one executed passing result in the verifier's fresh TRX. Run the same behavior probe from the independently fetched observer target. Do not manufacture a TRX, use the main repository's tests accidentally, or claim a build-only library exercised the selected filter. This arm is separate from the ordinary no-filter/no-rebase skip case.

### Guards the regression

| ID | Exact tests and decisive assertion |
|---|---|
| R-1 | SF.C488_DetachedFollowUpPublishesReviewedFix; SF.C488_StaleApprovalRefusesDetachedFix. E=B publishes B's unique production behavior through original owner; E=A refuses with expected=A/local=A/remote=B/candidate=B before FF. A stale-local selection must fail the B identity/content oracle. |
| R-2 | AP.C488_PendingIdentityMatrix; EV.C488_ReviewSettlementEvidenceMatrix; PS.C488_ApprovalRoundTrip. Hold after commit, move branch/report/telemetry and restart: stored E unchanged. Later findings neither replace nor implicitly revoke accepted snapshot. |
| R-3 | SG.C488_SourcePolicyMatrix; retained LandingGitTests.C448_V30_PushUsesPinnedCommitAndExplicitDestination and pin collision test. Approved local-ahead works with protected remote seed; diverged/missing/unavailable never falls back locally. |
| R-4 | SF.C488_SourceMovementBoundaryMatrix; SF.C488_LocalIdentityBoundaryMatrix; AgentTaskLandPreparationIdentityTests.C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation. Later writer retained, never recorded as FF/rebase result or verified/published payload. |
| R-5 | RC.C488_SourceCheckpointCrashMatrix; RC.C488_RebasedInputLineageMatrix; RC.C488_VerifiedResumeMatrix. Fresh process only trusts journaled L->E and witnessed O/P. No time-based approval, no replacement after target intent; failed replacement retains evidence. |
| R-6 | RC.C488_LegacyRecoveryMatrix; RC.C488_PublicationReconciliationMatrix; ST.C488_VersionedReceiptMatrix. Historical publication persists despite moved source, v1 cleanup invents no approval, new work requires E. |
| R-7 | DE.C488_ReviewToLandReceiptMatrix; both delivery crash matrices; AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts. Distillation cannot hide evidence; queue/ACK/screen/old/partial prompt cannot prove receipt. |
| R-8 | SC.C488_PostsExactApprovalJson; V-9 UI case; MutationPipelineTests.C470_code_settlement_persists_mutation_ready. Fresh callers supply E, Code routes to Mutation then mandatory Review; land subject remains original Code owner. |

### Guard inventory

Each G-n maps **1:1** to PC-n. None is excluded. The inventory covers new approval/source/recovery checks and the existing publication, admission, cleanup and delivery checks that this change passes through. A matrix's input variants exercise one named guard; independently placed boundary calls have separate IDs even when they invoke a shared predicate.

Alias expansion defines exact methods/paths for all tables. Unless shown otherwise the prefix is `tests/Antiphon.Tests/` and namespace `Antiphon.Tests.Application`. The PC table already spells each complete `C488_` method name; expand only its class alias. They are ordinary production-path tests, not implementation-mirroring assertions.

| Alias | Exact test class / file | Fixture / lane |
|---|---|---|
| PA | Application/ReviewEvidenceParserTests.cs | Nearest PipelineHandoffParseTests; Unit |
| EV | Application/AgentTaskReviewEvidenceTests.cs | SettlementRace/MutationPipeline/OutputDistillation fixtures; Integration |
| AP | Application/AgentTaskLandApprovalRequestTests.cs | LandRequest/AntiphonWebAppFactory, real service validation; Integration |
| PS | Application/AgentTaskLandApprovalPersistenceTests.cs | IsolatedTestSchema/IMigrator; Integration |
| ST | Application/AgentTaskLandingStateTests.cs | Pure versioned receipt fixtures; Unit |
| SG | Infrastructure/LandingSourceFreshnessTests.cs | LandingGitFixture, namespace Antiphon.Tests.Infrastructure; Integration |
| SF | Application/AgentTaskLandSourceFreshnessTests.cs | LandingSafetyHarness for real-Git rows; LandingProtocolHarness for matrix rows; Integration |
| RC | Application/AgentTaskLandApprovalRecoveryTests.cs | Fresh-scope and real worker-death LandingSafetyHarness plus isolated controlled matrices; Integration |
| NP | Application/AgentTaskLandNotificationPersistenceTests.cs | Isolated transaction fault fixtures; Integration |
| NR | Application/AgentTaskLandNotificationRecoveryTests.cs | BridgeQueueHarness and keyed outbox; Integration |
| RP | Application/AgentTaskLandReceiptTests.cs | Real receipt reconciler with adversarial evidence; Integration |
| SC | Application/DelegateScriptLandApprovalTests.cs | Actual pwsh script with loopback HTTP capture; Integration |
| DE | tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs | Actual Program/owned runner/native caller, namespace Antiphon.E2E |

Code adds a small public test wrapper per PC suffix when several cases share a scenario helper. This gives Mutation a precise method filter without relying on parameter selection syntax. Test names in V/R are additional scenario matrices, not replacements for individual PC wrappers. Every wrapper must assert the named barrier occurred and its immediate decision/checkpoint, not merely “eventually refused.” If a downstream independent guard also catches the injected defect, assert that the earlier forbidden call/checkpoint was attempted, or directly exercise the production predicate with the corresponding state. A downstream refusal must not mask the missing guard.

| Guard | Plan reference / safety invariant | Control |
|---|---|---|
| G-1 | D-1: Fresh work requires explicit E | PC-1 |
| G-2 | D-1: Only full valid OIDs are accepted | PC-2 |
| G-3 | D-1/D-2: Selected evidence SHA equals E | PC-3 |
| G-4 | D-2: Evidence names the original landing owner | PC-4 |
| G-5 | D-2: Evidence source ref matches request | PC-5 |
| G-6 | D-2: Evidence repository matches request | PC-6 |
| G-7 | D-2: Only clean Review evidence is usable | PC-7 |
| G-8 | D-2: Superseded evidence cannot authorize fresh work | PC-8 |
| G-9 | D-2: Explicit subject remains within caller authority | PC-9 |
| G-10 | D-2: Evidence block is unique and unambiguous | PC-10 |
| G-11 | D-2: Only final settled standalone block is parsed | PC-11 |
| G-12 | D-2: Subject GUID and Worktree identity must be valid | PC-12 |
| G-13 | D-2: Review settlement must succeed before approval is usable | PC-13 |
| G-14 | D-2: Malformed evidence never produces an approval header | PC-14 |
| G-15 | D-2: Evidence commits atomically with settlement | PC-15 |
| G-16 | D-2: Settlement replay is idempotent | PC-16 |
| G-17 | D-2: Review coordinates are a snapshot | PC-17 |
| G-18 | D-2: Review-only manual finding fields require Review/Clean | PC-18 |
| G-19 | D-2: Overrides append and do not inherit old SHA | PC-19 |
| G-20 | D-5: Pending E cannot be overwritten | PC-20 |
| G-21 | D-5: Pending evidence ID cannot be overwritten | PC-21 |
| G-22 | D-5: Pending filter cannot be overwritten | PC-22 |
| G-23 | D-5: Exact inactive pending repost preserves identity and age | PC-23 |
| G-24 | D-1: An active land is still a running-land conflict | PC-24 |
| G-25 | D-5: Stale queue wakeup cannot execute another request | PC-25 |
| G-26 | D-4: Worker reloads pending eligibility after admission | PC-26 |
| G-27 | D-1/D-4: E survives request commit and process restart | PC-27 |
| G-28 | D-4/D-5: Request coordinates remain bound at mutation | PC-28 |
| G-29 | D-2/D-5: Accepted evidence snapshot is immutable despite later findings | PC-29 |
| G-30 | D-5/D-7: Unknown request/operation schema refuses | PC-30 |
| G-31 | D-3: Source observation actually fetches fresh remote work | PC-31 |
| G-32 | D-3: Exactly one push endpoint is selected | PC-32 |
| G-33 | D-3: Source comes from push endpoint, not fetch URL | PC-33 |
| G-34 | D-3: Observation reads exact SourceFullRef | PC-34 |
| G-35 | D-3: Advertised response identifies one full valid ref/OID | PC-35 |
| G-36 | D-3: Fetched object must resolve to a supported commit | PC-36 |
| G-37 | D-3: Fetched SHA equals advertised SHA | PC-37 |
| G-38 | D-3: Racing observations are bounded to three attempts | PC-38 |
| G-39 | D-3: Observation refs are unique and immutable | PC-39 |
| G-40 | D-3: Endpoint fingerprint rechecked after fetch | PC-40 |
| G-41 | D-3: Confirmed missing remote source refuses | PC-41 |
| G-42 | D-3: Unreadable/malformed remote never falls back offline | PC-42 |
| G-43 | D-3: Fetch failure cannot reuse stale observation | PC-43 |
| G-44 | D-3: Behind source selects approved remote candidate | PC-44 |
| G-45 | D-3: Behind source requires E=R before FF | PC-45 |
| G-46 | D-3: Equal source still requires E=candidate | PC-46 |
| G-47 | D-3: Approved local-ahead commits cannot be dropped | PC-47 |
| G-48 | D-3: Divergence is refused regardless of E | PC-48 |
| G-49 | D-3: Ancestry command errors are not graph facts | PC-49 |
| G-50 | D-4: Source FF uses exact saved E in registered checkout | PC-50 |
| G-51 | D-4: Source FF cannot stash/reset/force local work | PC-51 |
| G-52 | D-3/D-4: Canonical repository lease precedes source mutation | PC-52 |
| G-53 | D-3/D-4: Standing writer admission precedes source resolution | PC-53 |
| G-54 | D-4: Standing child journal remains authoritative | PC-54 |
| G-55 | D-3/D-4: Dirty source cannot be fast-forwarded | PC-55 |
| G-56 | D-3/D-4: Source registration/branch/common/git identity must match | PC-56 |
| G-57 | D-3/D-4: Active sequencer/lock prevents source FF | PC-57 |
| G-58 | D-4: Accepted observation must be committed before FF intent | PC-58 |
| G-59 | D-4: AdvanceStarted intent commits before owned source command | PC-59 |
| G-60 | D-4: Owned child identity is recorded before further progress | PC-60 |
| G-61 | D-4: Immediate source FF command result is captured | PC-61 |
| G-62 | D-4: Post-FF source must remain clean | PC-62 |
| G-63 | D-4: Post-FF identity must remain the recorded identity | PC-63 |
| G-64 | D-4: Resolved commit must precede operation preparation | PC-64 |
| G-65 | D-4: New operation insertion commits before protocol mutation | PC-65 |
| G-66 | D-4: Checkpoint/FF failure cannot be followed by target mutation | PC-66 |
| G-67 | D-4: Clean-L recovery retries only saved L->E | PC-67 |
| G-68 | D-4: Clean-E recovery acknowledges same saved intent without repeat | PC-68 |
| G-69 | D-4: Unrecorded FF cannot be acknowledged | PC-69 |
| G-70 | D-4: Third SHA at interrupted FF is never adopted | PC-70 |
| G-71 | D-4: Unknown/live child cannot be assumed exited | PC-71 |
| G-72 | D-4/D-6: Interrupted FF cannot choose newer remote destination | PC-72 |
| G-73 | D-5: Rebase completion uses command-captured P | PC-73 |
| G-74 | D-5: Original/Reviewed remain approved E through resume | PC-74 |
| G-75 | D-5: PreparationInput distinguishes original and reused P | PC-75 |
| G-76 | D-5: Original recovery pin matches approved O | PC-76 |
| G-77 | D-5: Prepared/input pin matches phase-appropriate payload | PC-77 |
| G-78 | D-5: Verified SHA is exact captured prepared commit | PC-78 |
| G-79 | D-5: Execution uses accepted filter, never queue/task mirror | PC-79 |
| G-80 | D-5: Automatic recovery cannot open replacement | PC-80 |
| G-81 | D-5: Target/push intent forbids operation replacement | PC-81 |
| G-82 | D-4/D-5: Failed replacement atomically retains old active operation | PC-82 |
| G-83 | D-5: Conflict supersession is only inactive safely refused work | PC-83 |
| G-84 | D-5: Conflict supersession commits old debt and new request together | PC-84 |
| G-85 | D-5: Exact Verified resume accepts E=O with local verified P | PC-85 |
| G-86 | D-5: Changed Verified source requires new exact E | PC-86 |
| G-87 | D-5: Derivation-only replacement needs an explicit request | PC-87 |
| G-88 | D-5: Derivation predecessor must have same approved Original | PC-88 |
| G-89 | D-5: Derivation input must be durably witnessed completed rebase | PC-89 |
| G-90 | D-5: Derivation current source equals witnessed P and identity | PC-90 |
| G-91 | D-5: Derivation ancestry compares remote R against O | PC-91 |
| G-92 | D-5: Replacement retains O/P/predecessor provenance | PC-92 |
| G-93 | D-5: Derivation path cannot source-FF a rebased P | PC-93 |
| G-94 | D-5: Derivation retry always verifies anew except exact containment | PC-94 |
| G-95 | D-5: Contained P receipt retains derivation input provenance | PC-95 |
| G-96 | D-7: Unapproved unpublished v1 recovery cannot mutate | PC-96 |
| G-97 | D-7: Legacy queue with no approval needs new explicit E | PC-97 |
| G-98 | D-7: Published v1 cleanup remains valid without source/approval | PC-98 |
| G-99 | D-7: Late binding E equals saved Original | PC-99 |
| G-100 | D-7: Late binding checks coordinates/pins/phase-local identity | PC-100 |
| G-101 | D-7: Late binding time/provenance is actual one-time approval | PC-101 |
| G-102 | D-7: Late binding cannot source-FF rebased P toward remote | PC-102 |
| G-103 | D-7: Migration never invents historical review | PC-103 |
| G-104 | D-5: V2 state requires durable approval request link | PC-104 |
| G-105 | D-5: V2 state requires Reviewed=Original approved identity | PC-105 |
| G-106 | D-5: V2 state requires valid preparation lineage | PC-106 |
| G-107 | D-5/D-7: DB independently requires v2 approval fields | PC-107 |
| G-108 | D-5/D-7: DB independently validates full OIDs | PC-108 |
| G-109 | D-5/D-7: DB independently requires original/approval equality | PC-109 |
| G-110 | D-5: Resume validates current request-operation association | PC-110 |
| G-111 | D-5: Retry cannot overwrite original approval request provenance | PC-111 |
| G-112 | D-5: Resume rechecks saved approval/evidence identity | PC-112 |
| G-113 | D-6: Remote source snapshot is rechecked on resume | PC-113 |
| G-114 | D-6: Remote source is rechecked after initial observation | PC-114 |
| G-115 | D-6: Remote source is rechecked before source FF | PC-115 |
| G-116 | D-6: Remote source movement after source FF is caught | PC-116 |
| G-117 | D-6: Remote source is rechecked before rebase child | PC-117 |
| G-118 | D-6: Completed rebase cannot conceal remote source movement | PC-118 |
| G-119 | D-6: Remote movement during verification cannot authorize target intent | PC-119 |
| G-120 | D-6: Remote source is rechecked before target intent | PC-120 |
| G-121 | D-6: Remote source is rechecked before target mutation | PC-121 |
| G-122 | D-6: Remote source is rechecked immediately before push | PC-122 |
| G-123 | D-6: Fresh AlreadyPresent checks source after target observation | PC-123 |
| G-124 | D-6: Remote baseline is saved R, not E or rebased P | PC-124 |
| G-125 | D-6: Remote endpoint identity is bound at later checks | PC-125 |
| G-126 | D-5: Read-only publication reconciliation precedes changed-source refusal | PC-126 |
| G-127 | D-5: Unconfirmed recovery cannot push newly observed HEAD | PC-127 |
| G-128 | D-5: Unresolved child/rebase intent cannot be replaced or aborted automatically | PC-128 |
| G-129 | D-5/D-6: Remote target containment is independently established | PC-129 |
| G-130 | D-5/D-6: Target mutation uses exact saved target checkpoint | PC-130 |
| G-131 | D-3/D-6: Publication cannot force or rewrite/delete remote source | PC-131 |
| G-132 | D-5: Cleanup requires receipt and matching deletion SHA | PC-132 |
| G-133 | D-5: Moved local source is retained during cleanup | PC-133 |
| G-134 | D-5: Cleanup preserves unknown/ignored content and registration evidence | PC-134 |
| G-135 | D-5: Publication evidence is monotonic across notification failure | PC-135 |
| G-136 | D-2/A8: Completion header reads committed exact evidence | PC-136 |
| G-137 | D-2/A8: Distillation cannot remove or substitute evidence header | PC-137 |
| G-138 | A8: Land terminal event and notification share one transaction | PC-138 |
| G-139 | A8: Pre-operation refusal reads current request evidence | PC-139 |
| G-140 | A8: Notification destination is the accepted caller snapshot | PC-140 |
| G-141 | A8: Notification enqueue uses durable idempotency key | PC-141 |
| G-142 | A8: Recovered keyed row preserves body and destination | PC-142 |
| G-143 | A8: Enqueue failure retains retry debt | PC-143 |
| G-144 | A8: Lost completion/notification flush is recovered for idle recipient | PC-144 |
| G-145 | A8: Busy recipient does not block another land | PC-145 |
| G-146 | A8: Review settlement crash recovers evidence delivery | PC-146 |
| G-147 | A8: Queue-insert crash reuses notification row | PC-147 |
| G-148 | A8: Receipt requires UserPrompt, never Sent/ACK/screen/enqueue | PC-148 |
| G-149 | A8: Receipt requires correct session | PC-149 |
| G-150 | A8: Receipt requires exact durable content identity | PC-150 |
| G-151 | A8: Receipt requires complete body | PC-151 |
| G-152 | A8: Receipt requires prompt after sequence floor | PC-152 |
| G-153 | A8: Receipt without baseline requires attempt-time floor | PC-153 |
| G-154 | A8: Receipt-save failure must not retype | PC-154 |
| G-155 | A8: Polling cannot discharge caller debt | PC-155 |
| G-156 | S2/S5: Real CLI serializes exact E and evidence to original owner | PC-156 |
| G-157 | S5: Status distinguishes approved original and verified derivation | PC-157 |
| G-158 | D-2/S5: Review evidence does not alter next-stage routing | PC-158 |

### Positive controls

Mutation owns **every** row below. Code implements the named test and runs fixed V/R; Code reports PCs pending and hands off next: mutation. Apply one compiling defect to the relevant D-n implementation predicate/call/assignment, execute the exact named method red at the stated assertion, restore the fixed bytes, refresh timestamps/rebuild, then execute the same method green. A DB constraint PC modifies the new CLI-generated migration's specific check expression in the disposable mutant only; apply to a fresh isolated schema and expect the explicit constraint-assertion test to fail. It must not fail during fixture setup.

Do not replace test assertions, inject a fake test failure, remove tests or accept compilation/setup/timeout/zero-test failures as red. Each PC uses the exact method filter; all its data rows run. A mutant that survives because another guard catches it is **missing detection for this boundary**, not a successful PC: restore and return next: code for an adequate assertion/seam. Actual implementation guard sites and mutated diff must be recorded by Mutation. If implementation introduces another independently bypassable guard, add its G/PC and cost before claiming completeness.

| Control | Compiling defect to apply | Exact detecting method | Required red assertion | Cost lane |
|---|---|---|---|---|
| PC-1 | Replace missing-E validation with an assignment from local HEAD | AP.C488_FreshApprovalRequired | 422 expected_source_sha_required and zero requests | db |
| PC-2 | Allow a 7-character SHA through the validator | AP.C488_FullOidRequired | short-SHA 422 rather than queued | db |
| PC-3 | Remove evidence SHA comparison | AP.C488_EvidenceShaMatches | 409 and zero request rows for B evidence plus E=C | db |
| PC-4 | Remove SubjectTaskId comparison | AP.C488_EvidenceSubjectMatches | wrong owner 409 even on same card | db |
| PC-5 | Skip ReviewedSourceRef comparison | AP.C488_EvidenceRefMatches | wrong-ref 409 with no enqueue | db |
| PC-6 | Skip canonical reviewed repository comparison | AP.C488_EvidenceRepositoryMatches | wrong-repository 409 | db |
| PC-7 | Treat Found or non-Review row as usable | AP.C488_EvidenceVerdictEligible | 409 for each ineligible verdict/stage | db |
| PC-8 | Ignore supersession lookup | AP.C488_SupersededEvidenceRefuses | 409 with superseded clean row | db |
| PC-9 | Skip subject authorization check | EV.C488_SubjectAuthorizationRequired | unauthorized finding/settlement exposes no usable evidence | db |
| PC-10 | Use the first block despite a second conflicting block | PA.C488_DuplicateBlocksRefuse | parser yields no usable evidence | unit |
| PC-11 | Allow block-looking historical/quoted text as evidence | EV.C488_FinalReportIsAuthority | distillation/prose/quoted prior SHA cannot become evidence | db |
| PC-12 | Accept absent/ambiguous/non-Worktree subject via follow-up inference | EV.C488_ExplicitWorktreeSubjectRequired | warning and no usable review binding | db |
| PC-13 | Remove successful Review-role gate | EV.C488_SuccessfulReviewRequired | failed/blocked/non-Review settlement has no usable evidence | db |
| PC-14 | Format approval from raw report despite parser failure | EV.C488_InvalidEvidenceWarnsWithoutHeader | warning exists and approval header fields absent | db |
| PC-15 | Save evidence in a separate commit after settled task/note | EV.C488_SettlementEvidenceAtomic | fault leaves no advertised approval lacking durable row | db |
| PC-16 | Remove settlement evidence dedupe and allocate a new ID | EV.C488_ReviewSettlementIdempotent | concurrent/replayed settlement creates exactly one usable evidence ID | db |
| PC-17 | Recompute review branch/repository in DTO from current task | EV.C488_EvidenceCoordinatesImmutable | after owner edit DTO still reports recorded coordinates | db |
| PC-18 | Allow reviewed SHA on Verify or Found finding | EV.C488_ManualFindingFieldsRestricted | 422 and no approval row | db |
| PC-19 | Copy previous ReviewedSourceSha into SHA-less override | EV.C488_OverrideDoesNotCopyApproval | old row unchanged and new missing-SHA row unusable | db |
| PC-20 | Assign supplied different E to current pending request | AP.C488_PendingExpectedShaImmutable | 409 and unchanged E | db |
| PC-21 | Assign supplied different evidence ID to pending request | AP.C488_PendingEvidenceImmutable | 409 and unchanged evidence ID | db |
| PC-22 | Restore old pending VerifyFilter overwrite behavior | AP.C488_PendingFilterImmutable | 409 and unchanged filter | db |
| PC-23 | Create new request on exact repost | AP.C488_PendingAgeAndIdentityPreserved | ID/requestedAt/attempts unchanged | db |
| PC-24 | Bypass in-process active queue guard | AP.C488_ActiveRequestConflicts | second active request 409 with one queue claim | db |
| PC-25 | Resolve task CurrentLandRequestId instead of supplied queue RequestId | AP.C488_StaleWakeupDoesNotRun | stale wake causes zero Git/verifier calls and no new terminal row | db |
| PC-26 | Skip fresh task/request state check after writer hold | SF.C488_EligibilityRechecked | canceled or non-Succeeded owner cannot prepare | db |
| PC-27 | Persist a different valid SHA C while returning supplied E to caller | PS.C488_ExpectedShaPersists | fresh context reads exact admitted E before worker; named field assertion fails | db |
| PC-28 | Use edited task coordinates without matching request snapshot | SF.C488_RequestCoordinatesRechecked | coordinate change refuses before next mutation | db |
| PC-29 | Select latest StageOutcome again when worker resumes | SF.C488_AcceptedEvidenceIsSnapshot | accepted evidence ID/E remain original; later Found cannot silently rebind/revoke | db |
| PC-30 | Treat any version >=2 as supported | ST.C488_UnknownVersionRefuses | mutation/publication predicate false for unknown version | unit |
| PC-31 | Return local/tracking snapshot without exact source fetch | SF.C488_DetachedFollowUpRequiresFetch | B incident records fetched B and independently publishes its unique fix | git |
| PC-32 | Take first endpoint when multiple push URLs exist | SG.C488_AmbiguousPushEndpointRefuses | specific refusal, neither endpoint mutated | git |
| PC-33 | Read remote.origin.url instead of resolved push URL | SG.C488_PushEndpointIsSourceAuthority | remote B from push repo selected; fetch-only decoy C never selected | git |
| PC-34 | Use branch merge/default target ref in source lookup | SG.C488_ExactSourceRefObserved | requested full source ref and candidate B match, decoy ref ignored | git |
| PC-35 | Accept malformed, duplicate or wrong-ref advertisement | SG.C488_AdvertisementValidated | observation refused with no accepted pin/candidate | git |
| PC-36 | Accept a fetched tag/blob/unresolvable object as source | SG.C488_FetchedCommitValidated | noncommit/unresolved object refuses specifically | git |
| PC-37 | Remove advertised/fetched comparison | SG.C488_ReadFetchRaceCorrelated | mismatch retries or refuses, never accepts mismatched pair | git |
| PC-38 | Change source retry bound from three to four | SG.C488_ObservationRetryBounded | exactly three attempts on perpetual movement and refusal | git |
| PC-39 | Reuse a prior observation ref with a force fetch | SG.C488_ObservationPinsImmutable | earlier pin unchanged and distinct ref per observation | git |
| PC-40 | Skip post-fetch endpoint identity comparison | SG.C488_ObservationEndpointRechecked | changed endpoint refuses despite same SHA | git |
| PC-41 | Treat ls-remote exit 2 as local-only success | SG.C488_MissingSourceRefuses | source_remote_missing and no mutation | git |
| PC-42 | Return local candidate on source read failure | SG.C488_UnreadableSourceRefuses | specific remote-read/parse refusal; candidate unavailable | git |
| PC-43 | Return prior pin after failed fetch | SG.C488_FailedFetchCannotReusePin | failure refuses despite existing old pin/FETCH_HEAD | git |
| PC-44 | Prefer stale local L when L is ancestor of R | SF.C488_BehindSelectsRemote | Original=Reviewed=B and independent target contains B production bytes | git |
| PC-45 | Allow E=old L while fast-forwarding to R | SF.C488_StaleApprovalStopsBeforeFf | reviewed_source_mismatch with A/A/B/B and no source merge/rebase/verifier/push/cleanup | git |
| PC-46 | Skip E comparison in equal branch | SF.C488_EqualCandidateNeedsApproval | mismatched E refuses before operation creation | db |
| PC-47 | Always select R even when R is ancestor of approved L | SG.C488_LocalAheadRetained | candidate E=L, relationship LocalAhead, remote remains seed | git |
| PC-48 | Treat diverged graph as approved local-ahead | SG.C488_DivergedSourceRefuses | source_remote_diverged for E=L and E=R | git |
| PC-49 | Treat merge-base exit >1 as ordinary false/success policy | SG.C488_AncestryErrorsRefuse | I/O failure refuses before source FF | git |
| PC-50 | Pass branch name/remote tracking name to source merge | SG.C488_FastForwardUsesExactOperand | recorded argv operand full E and working directory original source | git |
| PC-51 | Enable autostash for source merge | SG.C488_FastForwardDisablesAutostash | argv includes merge.autoStash=false and no stash/reset/force command | git |
| PC-52 | Invoke source resolution before acquiring lease | SF.C488_SourceResolutionNeedsLease | held common-dir lease produces no source merge | git |
| PC-53 | Skip repository/source writer check | SF.C488_BlockedWriterHoldsSourceResolution | held writer consumes no attempt and no source fetch/FF | db |
| PC-54 | Skip journal admission when request has no child PID | RC.C488_StandingJournalBlocksSourceAdvance | live/torn standing journal holds, zero source merge | crash |
| PC-55 | Bypass dirty/staged/untracked status refusal at pre-FF | SF.C488_DirtySourceFfRefuses | bytes/index preserved and zero source FF for each state | git |
| PC-56 | Bypass pre-FF source identity predicate | SF.C488_SourceIdentityFfRefuses | foreign/detached/same-SHA switched/aliased identity refuses | git |
| PC-57 | Ignore source sequencer/lock inspection result | SF.C488_SourceSequencerFfRefuses | merge/rebase/cherry-pick markers or lock prevent source FF | git |
| PC-58 | Continue after failed Observed commit | RC.C488_ObservedCommitGatesAdvance | no AdvanceStarted/owned merge after unacknowledged save | db |
| PC-59 | Launch source merge before saving exact L/E intent | RC.C488_AdvanceIntentGatesChild | separate connection at child start sees complete durable L/E intent | crash |
| PC-60 | Omit started callback/child PID-start persistence | RC.C488_SourceChildIdentityDurable | ready barrier has matching request and standing journal PID/start identity | crash |
| PC-61 | Read later HEAD as the command-completion SHA | SF.C488_FastForwardCannotAdoptLaterHead | later writer C retained and Resolved not acknowledged | git |
| PC-62 | Skip post-FF clean-status check | SF.C488_PostFfDirtyRefuses | injected dirty/index bytes retained; no Resolved/target mutation | git |
| PC-63 | Skip post-FF coordinates/registration comparison | SF.C488_PostFfIdentityRefuses | same-SHA branch/metadata switch refuses before Resolved | git |
| PC-64 | Create operation after failed Resolved save | RC.C488_ResolvedCommitGatesOperation | no new operation/preparation from uncommitted resolution | db |
| PC-65 | Rebase before new operation transaction acknowledges | RC.C488_OperationCommitGatesPreparation | no rebase/target mutation after insertion commit failure | db |
| PC-66 | Catch FF/checkpoint error and continue preparation | RC.C488_SourceFailureStopsTarget | target ref unchanged and no target merge/push after injected failure | db |
| PC-67 | Treat saved L at AdvanceStarted as already resolved | RC.C488_CleanLRetriesSavedAdvance | one exact source FF then Resolved E, never Original=L | crash |
| PC-68 | Rerun source FF when clean E already satisfies durable intent | RC.C488_CleanEAcknowledgesIntent | same request, zero extra source merges, resolved E | crash |
| PC-69 | Infer AdvanceStarted from current HEAD=E when only Observed persisted | RC.C488_UnrecordedFastForwardRefuses | Observed plus unexplained E cannot become Resolved | crash |
| PC-70 | Accept any clean current HEAD as resolved destination | RC.C488_ThirdShaRecoveryRefuses | C preserved, E immutable, no mutation/operation replacement | crash |
| PC-71 | Clear request child intent on restart without PID/start proof | RC.C488_UncertainSourceChildRetained | unknown/live/reused-PID child evidence holds, journal retained | crash |
| PC-72 | Overwrite saved remote R/E from a fresh recovery fetch | RC.C488_RecoveryDestinationImmutable | new remote C refuses and saved L/E/R unchanged | crash |
| PC-73 | Populate RebasedSourceSha from later inspected HEAD | SF.C488_RebaseCannotAdoptLaterHead | later C never becomes prepared/verified, no target mutation | git |
| PC-74 | Assign OriginalSourceSha/ReviewedSourceSha from current HEAD | RC.C488_OriginalApprovalNeverAdoptsHead | O survives resume with local P distinct from O | db |
| PC-75 | Set replacement PreparationInputSha to O while source is P | RC.C488_PreparationInputIsWitnessedP | input=P and original=O with predecessor, real graph resumes correctly | git |
| PC-76 | Skip original pin SHA comparison | RC.C488_OriginalPinMatchesApproval | broken/missing original pin refuses before mutation | db |
| PC-77 | Skip prepared/input pin comparison | RC.C488_PreparedPinMatchesPayload | wrong/missing P pin refuses before mutation | db |
| PC-78 | Let current HEAD or Original substitute for VerifiedSourceSha | ST.C488_VerifiedCommitExact | mismatched verified identity cannot transition/publish | unit |
| PC-79 | Use in-memory/task LandVerifyFilter rather than saved request/operation | SF.C488_AcceptedFilterUsed | verifier records original filter despite mirror edit | db |
| PC-80 | Treat sweep/resume as explicit approval | RC.C488_AutomaticReplacementRefuses | changed Verified/Refused work keeps prior op and pins | db |
| PC-81 | Permit replacement at TargetAdvanceStarted or later | RC.C488_TargetIntentPreventsReplacement | same operation and uncertain evidence at all post-intent phases | db |
| PC-82 | Commit predecessor deactivation before replacement creation | RC.C488_ReplacementFailureKeepsPredecessor | separate observer sees old active row/pins after failed transaction | db |
| PC-83 | Allow changed-E supersession for active/ordinary pending/post-intent request | AP.C488_ConflictSupersessionEligibility | 409 unless inactive Succeeded safely refused pre-intent with no uncertainty | db |
| PC-84 | Save old Superseded state separately from outcome/new request | RC.C488_ConflictSupersessionAtomic | cut leaves old request active or complete atomic pair with old notification | db |
| PC-85 | Compare approved E directly to rebased P as fresh candidate | RC.C488_VerifiedResumeKeepsOriginalApproval | same op resumes O/P with no new approval or rebase | git |
| PC-86 | Use request time as permission to replace with changed HEAD | RC.C488_ChangedSourceNeedsNewApproval | old E refuses new source; explicit new-E request alone can replace | db |
| PC-87 | Allow automatic derivation retry from recorded P | RC.C488_DerivationRetryIsExplicit | automatic attempt retains old op; explicit request creates new | db |
| PC-88 | Accept predecessor whose Original differs from E | RC.C488_DerivationOriginalMatches | mismatched predecessor refuses with prior evidence unchanged | db |
| PC-89 | Infer P from clean HEAD without saved completion | RC.C488_DerivationRequiresWitnessedRebase | unrecorded/missing rebase result cannot enter lineage path | git |
| PC-90 | Accept arbitrary clean descendant/checkout of P | RC.C488_DerivationRequiresExactCurrentP | changed HEAD/identity refuses and no reset | git |
| PC-91 | Compare ancestry to P instead of approved O | RC.C488_DerivationAncestryUsesOriginal | real divergent O/P graph accepts R<=O; newer/diverged R refuses | git |
| PC-92 | Omit PreviousPreparationOperationId or relabel Original=P | RC.C488_DerivationLineagePersists | fresh context reads Original=Reviewed=O,input=P,predecessor ID | db |
| PC-93 | Route witnessed P through fresh source-FF branch | RC.C488_DerivationNeverSourceFastForwards | zero source merge and pinned P used for preparation | git |
| PC-94 | Enable base_unchanged or inherited pass on derivation retry | RC.C488_DerivationAlwaysReverifies | verifier called again with Q even Q=P and prior failure | git |
| PC-95 | Record Verified=O for exact containment of P | ST.C488_ContainedInputReceiptHasLineage | valid O/P lineage accepted only with Verified=P and matching pins | unit |
| PC-96 | Allow automatic v1 rebase/advance/push without binding | RC.C488_LegacyAutomaticMutationRefuses | no new mutation; existing saved publication may only reconcile | db |
| PC-97 | Auto-fill legacy queued ExpectedSourceSha from task HEAD | RC.C488_LegacyQueuedApprovalRequired | legacy_review_binding_required before any source/target mutation | db |
| PC-98 | Require v2 approval/source observation for published v1 cleanup | RC.C488_LegacyPublishedCleanupWorks | same historical receipt cleans conservatively despite missing source ref | git |
| PC-99 | Allow E=current rebased P or another SHA | RC.C488_LegacyLateBindingOriginalExact | mismatch refuses; operation Original never rewritten | db |
| PC-100 | Bind v1 without phase-appropriate inspection/pin checks | RC.C488_LegacyLateBindingIdentityValid | bad identity/pin/phase-current SHA refuses while valid O/P binds | git |
| PC-101 | Backdate approval or replace binding on second explicit POST | RC.C488_LegacyLateBindingIsOneTime | binding time >= request time, ID/history unchanged, second different bind conflicts | db |
| PC-102 | Run fresh behind resolution against phase-local P during v1 binding | RC.C488_LegacyLateBindingNeverFastForwardsP | no source FF; input remains original pre-rebase input with separate local P observation | git |
| PC-103 | Backfill ReviewedSourceSha from OriginalSourceSha | PS.C488_MigrationDoesNotInventApproval | legacy approval fields remain null after upgrade twice | db |
| PC-104 | Drop ApprovalLandRequestId predicate from state validation | ST.C488_V2ReceiptNeedsRequestBinding | missing/wrong request binding cannot authorize transition/publication | unit |
| PC-105 | Drop reviewed/original equality from state predicate | ST.C488_V2ReceiptNeedsApprovedOriginal | valid-looking unequal full SHAs reject | unit |
| PC-106 | Accept PreparationInput!=Original without predecessor evidence | ST.C488_V2ReceiptNeedsLineage | unwitnessed input P rejects mutation/publication | unit |
| PC-107 | Remove new migration non-null approval check constraint | PS.C488_V2DatabaseRequiresApprovalFields | raw v2 null approval write fails with named constraint | db |
| PC-108 | Relax new migration OID-shape constraint | PS.C488_V2DatabaseRequiresFullOids | raw malformed v2 SHA write fails with named constraint | db |
| PC-109 | Remove new migration equality check constraint | PS.C488_V2DatabaseRequiresApprovalEquality | raw unequal approved/original write fails with named constraint | db |
| PC-110 | Skip current request/operation link check at source fence | RC.C488_ResumeRequestOperationLinkMatches | foreign request cannot resume existing op | db |
| PC-111 | Set ApprovalLandRequestId to most recent retry ID | RC.C488_OriginalRequestProvenanceRetained | original approval request ID stays fixed, retry link recorded separately | db |
| PC-112 | Skip persisted approval snapshot comparison at mutation fence | SF.C488_ResumeApprovalSnapshotMatches | tampered E/evidence refuses next mutation | db |
| PC-113 | Omit source remote observation only on resume entry | SF.C488_RemoteFenceOnResume | moved/deleted/unreadable/refingerprinted source refuses before resumed mutation | db |
| PC-114 | Omit source fence after observation before resolution acknowledgement | SF.C488_RemoteFenceAfterObservation | move injected after accepted fetch cannot authorize Resolved/FF | db |
| PC-115 | Remove remote fence immediately before source merge | SF.C488_RemoteFenceBeforeSourceFf | move after intent save refuses with no source merge | git |
| PC-116 | Skip post-FF remote revalidation before preparation | SF.C488_RemoteFenceAfterSourceFf | source may be E but newer remote C refuses before rebase | git |
| PC-117 | Remove source remote fence at rebase child boundary | SF.C488_RemoteFenceBeforeRebaseChild | move after rebase intent prevents owned rebase | git |
| PC-118 | Skip remote check after completed preparation | SF.C488_RemoteFenceAfterRebase | remote C during rebase refuses before verification/target as appropriate | db |
| PC-119 | Remove remote fence after verifier returns | SF.C488_RemoteFenceAfterVerification | verified P retained but no target intent for changed remote | db |
| PC-120 | Remove source remote observation immediately before target intent | SF.C488_RemoteFenceBeforeTargetIntent | late move leaves no TargetAdvanceStarted acknowledgement | db |
| PC-121 | Remove source remote observation after target intent before mutation | SF.C488_RemoteFenceBeforeTargetMutation | late move leaves local target at checkpoint, no target merge | git |
| PC-122 | Remove final source remote observation before push | SF.C488_RemoteFenceBeforePush | late move leaves local target P but remote target unpublished, zero push | git |
| PC-123 | Return AlreadyPresent before last source remote check | SF.C488_RemoteFenceBeforeAlreadyPresent | move during target observation refuses confirmation/cleanup | git |
| PC-124 | Replace saved R with current prepared/local SHA | SF.C488_RemoteBaselineRemainsR | local-ahead R!=E and rebased P!=E work; R movement toward E still refuses | git |
| PC-125 | Compare only SHA and ignore fingerprint on source recheck | SF.C488_RemoteFingerprintRemainsBound | same SHA at new endpoint still refuses | git |
| PC-126 | Check moved source first and return without observing saved publication intent | RC.C488_PublishedPayloadSurvivesSourceMovement | lost push ACK then remote-source C still records published saved P/same op | git |
| PC-127 | Use current HEAD instead of saved verified P for recovery push | RC.C488_RecoveryPushUsesSavedPayload | no second/new payload; published target equals saved verified or mutation refused | git |
| PC-128 | Automatically abort interrupted rebase and create fresh op | RC.C488_InterruptedRebaseEvidenceRetained | inspection-required refusal, no abort/rebase/replacement, old pins retained | crash |
| PC-129 | Trust push exit zero rather than target read/fetch/ancestry | RC.C488_PublicationNeedsTargetContainment | unconfirmed remote target cannot authorize cleanup | git |
| PC-130 | Skip target SHA/checkout recheck before target merge | SF.C488_TargetCheckpointStillGuarded | target third SHA/checkout/dirty change retained, zero target merge | git |
| PC-131 | Add source push or force option to target publication argv | SG.C488_PublicationNeverMutatesRemoteSource | exact non-force E-or-P:target push; independent source ref remains expected B/seed | git |
| PC-132 | Bypass guarded-removal publication/deletion identity check | RC.C488_CleanupNeedsVerifiedReceipt | unconfirmed or wrong deletion identity retains source files/ref | git |
| PC-133 | Ignore cleanup current source SHA comparison | RC.C488_CleanupRetainsMovedSource | post-publication C branch/files survive with residue, P receipt remains | git |
| PC-134 | Allow force removal despite sentinel or unknown registration | RC.C488_CleanupRetainsUnknownContent | valuable ignored sentinel/ambiguous registration retained; no force removal | git |
| PC-135 | Replace published result with refusal after notification exception | RC.C488_NotificationFailureCannotUndoPublication | same published P/op and one terminal event, receipt debt retained | db |
| PC-136 | Build header from latest raw report or current source HEAD | EV.C488_CompletionHeaderBoundToEvidence | ID/subject/full SHA equal durable row after report/task movement | db |
| PC-137 | Replace completion header with distilled summary header | EV.C488_DistillationPreservesApprovalHeader | raw/Apply/spilled note retains identical evidence ID/subject/SHA | db |
| PC-138 | Commit terminal event before creating notification | NP.C488_ApprovalOutcomeTransactionAtomic | commit-cut leaves neither or both event/notification joined to request | db |
| PC-139 | Format mismatch from previous active landing operation | NR.C488_PreOperationReceiptUsesRequest | full expected/local/remote/candidate values from new request, no stale operation identity | db |
| PC-140 | Resolve current task ParentSessionId at delivery time | NR.C488_ApprovalReceiptDestinationImmutable | new task destination cannot receive old notification | db |
| PC-141 | Omit SourceLandNotificationId when enqueueing | NR.C488_ApprovalReceiptQueueKeyRequired | two reconcilers/restart produce one keyed row | db |
| PC-142 | Accept key collision with different body/destination | NR.C488_ApprovalReceiptKeyCollisionRefuses | conflict, original body/destination unchanged | db |
| PC-143 | Mark obligation completed on enqueue exception | DE.C488_ApprovalEnqueueFailureRecovers | same notification eventually has complete native prompt after failure | native |
| PC-144 | Disable completion flush/notification recovery scan after lost wakeup | DE.C488_ApprovalLostFlushRecovers | already eligible caller receives native complete prompt without new input | native |
| PC-145 | Await busy recipient delivery on land execution worker | DE.C488_ApprovalBusyCallerDoesNotBlock | other owner publishes/receives before busy release; busy later receives same body | native |
| PC-146 | Skip completion recovery for settled Review with durable evidence | DE.C488_ReviewEvidenceCrashRecovers | hard restart yields one complete native Review header with durable evidence ID | native |
| PC-147 | Allocate new queue identity after unlinked insert | DE.C488_ApprovalQueueInsertCrashReusesRow | same queue ID and one native prompt after restart | native |
| PC-148 | Accept Sent/Delivered/QueuedUserPrompt as caller receipt | RP.C488_ApprovalReceiptNeedsUserPrompt | ConfirmedAt remains null for each substitute | db |
| PC-149 | Ignore destination session in transcript lookup | RP.C488_ApprovalReceiptNeedsDestination | wrong-session complete body cannot confirm | db |
| PC-150 | Ignore request/evidence/notification IDs when matching body | RP.C488_ApprovalReceiptNeedsIdentity | body with one different ID/full SHA cannot confirm | db |
| PC-151 | Match only header/head-tail fragments | RP.C488_ApprovalReceiptNeedsCompleteBody | truncated/head-tail-splice evidence cannot confirm | db |
| PC-152 | Accept prompt at/below LastDeliveryBaselineSequence | RP.C488_ApprovalReceiptNeedsSequenceFloor | old-sequence complete prompt cannot confirm | db |
| PC-153 | Accept old prompt when sequence baseline absent | RP.C488_ApprovalReceiptNeedsTimeFloor | old-time prompt rejected while within existing tolerance accepted | db |
| PC-154 | Retry typing instead of transcript catch-up after saved prompt | DE.C488_ApprovalReceiptSaveFailureNeverRetypes | one native prompt despite failed verdict/receipt save and restart | native |
| PC-155 | Stamp ConfirmedAt in status projection | DE.C488_ApprovalPollingCannotConfirm | busy/attempted status polls leave debt until complete native prompt | native |
| PC-156 | Drop expectedSourceSha from script POST body | SC.C488_CliPostsExactApproval | captured HTTP JSON equals supplied full E/evidence/filter/owner | script |
| PC-157 | Display VerifiedSourceSha under approved source label | SC.C488_StatusApprovalIsNotVerifiedSha | CLI exact labels show O and P separately, legacy approval absent | script |
| PC-158 | Parse next stage from evidence block or drop existing next= header | EV.C488_ReviewEvidencePreservesStageRouting | Review report evidence and next=land coexist; Code->Mutation parsing unchanged | db |

### Out of scope

- No live incident replay, live land request, live credentials, paid provider, production runner, canonical stack restart or deployment during Code/Mutation. Deploy verification belongs to the authorized caller after successful Mutation and mandatory Review.
- No automatic child merge-back behavior change. Retain the existing local-child rebase-result test when shared code changes; this card changes explicit Land.
- No claim of an atomic remote-source/target two-ref transaction. Inject movement at every named observable boundary. A move strictly after the final remote read cannot invalidate already observed history; require immutable approved payload and last observation SHA/time in the receipt.
- No full Cartesian product of every file-status variant with every parser input, every DB fault and every transport failure. V-5 fully crosses remote movement with its named boundaries; V-6 fully crosses new checkpoints with persistence cuts; V-8 crosses changed producers and busy/idle recipients with handoff faults. Orthogonal syntax/state/cleanup variants run at their owning guard. This excludes redundant products, no guard.
- Browser layout polish, unrelated assemblies and the full backend namespace are excluded. V-9 tests the touched drawer projection; native E2E serves a fresh built client. Re-run additional consumers if the actual call-site census finds another changed public contract.
- Native fake transcript evidence is not a claim that a real reviewer approved the implementation. Required Review after Mutation inspects the **final exact branch SHA**, including plan/evidence-only follow-up commits, and emits D-2 evidence. The caller lands the original implementation Code owner with that E/evidence ID, never this TestDesign task or a detached/Shared helper.

### Cost

All numbers are **estimated mandatory budget floors**, not measured test results and not an instruction to wait out a clock. They include complete execution; “too expensive” does not authorize dropping a PC. A lower measured floor requires recording the same executed methods, counts and restored builds with elapsed times, then updating this section explicitly.

#### Ordinary Code V/R

Build once to producer-owned `bin-c488/`. Run Unit, then the following bounded groups separately; verify every intended class and method in a fresh TRX. Suffix wildcards must not select unrelated classes. The expected *expanded* case count is discovered from execution and reported, not invented here; minimum coverage is every named method/data row and all individual guard wrappers.

| Group | Named classes / command selection | Estimated minutes |
|---|---|---:|
| Setup/build | Backend/tests isolated build, client dependency availability, client build, E2E native/runner outputs | 25 |
| Unit | `/*/*/*/*[Category=Unit]` including PA/ST and existing parser/formatter/bundle contracts | 5 |
| Admission/evidence | AgentTaskReviewEvidenceTests, AgentTaskLandApprovalRequestTests, AgentTaskLandRequestTests, StageOutcomeFindingEndpointTests, StageOutcomeSummaryTests, AgentTaskReplyIntegrationTests, AgentTaskCatchUpSettlementTests, AgentTaskSettlementRaceTests | 25 |
| Source I/O/admission | LandingSourceFreshnessTests, AgentTaskLandSourceFreshnessTests, LandingGitTests, LandingIdentityControlTests, LandingSourceBoundaryControlTests, AgentTaskLandAdmissionControlledTests, AgentTaskLandBoundaryControlledTests, AgentTaskLandConcurrencyControlledTests | 35 |
| Persistence/preparation/recovery | AgentTaskLandApprovalPersistenceTests, AgentTaskLandApprovalRecoveryTests, AgentTaskLandingPersistenceTests, AgentTaskLandPreparationIdentityTests, AgentTaskLandRefusedRetryTests, AgentTaskLandRecoveryTests, AgentTaskLandCheckpointMatrixTests, AgentTaskLandPersistenceFailureTests | 45 |
| Cleanup/outbox/receipts | AgentTaskLandCleanupSafetyTests, LandingRemovalPolicyControlTests, AgentTaskLandNotificationPersistenceTests, AgentTaskLandNotificationRecoveryTests, AgentTaskLandReceiptTests, AgentTaskLandStageOutcomeTests | 25 |
| Caller/settlement consumers | DelegateScriptLandApprovalTests, DelegateScriptLandStatusTests, OutputDistillationDeliveryTests, MutationPipelineTests, MutationDispatchTests, InstructionBundleTests, DelegationUnitTests | 15 |
| Native E2E | AgentTaskLandDeliveryE2ETests (all existing and C488 methods) | 35 |
| Drawer | `pwsh -File scripts/test-client.ps1 TaskDetailBody.test` | 5 |
| **Code verification floor** | **25 setup/build + 190 ordinary V/R** | **215** |

Some Unit methods occur again in named consumer groups intentionally; do not count duplicate executions as additional distinct coverage. Refine group sizes if implementation splits classes, retaining the coverage-to-class map. Shared harness changes require the census of all RequestAsync/LandAgentTaskRequest/request-row seeds, including tests not named in S1-S5; add those affected classes and measured cost before closing V/R. No SHA-less fixture bypass is allowed.

Command recipe (PowerShell; one test-producing invocation per fresh results path):

~~~powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c488/ --nologo
$c488RunId = [guid]::NewGuid().ToString('N')
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c488/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory ".antiphon/c488/$c488RunId/unit"

$c488Classes = @('AgentTaskReviewEvidenceTests', 'AgentTaskLandApprovalRequestTests', 'AgentTaskLandRequestTests', 'StageOutcomeFindingEndpointTests', 'StageOutcomeSummaryTests', 'AgentTaskReplyIntegrationTests', 'AgentTaskCatchUpSettlementTests', 'AgentTaskSettlementRaceTests')
$c488Filter = '/*/*/' + (($c488Classes | ForEach-Object { '(' + $_ + '*)' }) -join '|') + '/*'
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c488/ -- --treenode-filter $c488Filter --report-trx --report-trx-filename admission.trx --results-directory ".antiphon/c488/$c488RunId/admission"
pwsh -File scripts/test-client.ps1 TaskDetailBody.test
npm --prefix client run build
dotnet build tests/Antiphon.E2E --property:OutputPath=bin-c488/ --nologo
dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-c488/ -- --treenode-filter '/*/*/AgentTaskLandDeliveryE2ETests/*' --report-trx --report-trx-filename native.trx --results-directory ".antiphon/c488/$c488RunId/native"
~~~

Repeat the class-array invocation for **each** named table group, with its own results subdirectory/file; Unit and E2E retain their separate invocations. Check each process exit code immediately. Parse fresh TRX result counters and intended executed names; run `scripts/test-duration-tripwire.ps1 -Trx <actual-trx>`. Do not use list-tests, a broad namespace or exit zero with zero tests as coverage. Run process-spawning projects sequentially, keeping their assembly-local ProcessSpawnLimit. A global sweep retains its existing unkeyed NotInParallel requirement; all row assertions are fixture-scoped.

#### Mutation PC battery

Before any cycle: Code terminal, no owned command running, reported implementation SHA=HEAD, clean tracked source/index, inventory untracked/build outputs. Keep original Code task ID/branch/worktree and review-required: yes. Mutation is a fresh writable Shared role in that exact retained Code worktree; no sub-delegation.

For PC-n, expand the alias/suffix into the exact class/method, e.g.:

~~~powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c488-pc/ -- --treenode-filter '/*/*/AgentTaskLandSourceFreshnessTests/C488_BehindSelectsRemote' --report-trx --report-trx-filename red.trx --results-directory ".antiphon/c488/pc-44/$c488RunId/red"
~~~

Use a fresh ID per cycle; restore/rebuild and repeat with green path/name. E2E controls use `tests/Antiphon.E2E` and their exact method, not the entire native class. After restoration, do not trust a DLL whose source timestamps stayed old. Record the produced DLL/MVID or build evidence, exact mutated diff, expected assertion, expanded test count, red/green TRX and wall time per PC. Never commit/push a mutant.


| PC cost lane | Controls | Estimated minutes per complete red/restore/rebuild/green cycle | Subtotal |
|---|---:|---:|---:|
| unit | 7 | 2 | 14 |
| db | 78 | 3 | 234 |
| git | 54 | 5 | 270 |
| crash | 10 | 8 | 80 |
| native | 7 | 12 | 84 |
| script | 2 | 4 | 8 |
| **Every planned cycle** | **158** | | **690** |

The PC lane estimates include both builds/executions and **all data rows of the exact method**, not one selected parameter. Reserve an additional 10 minutes for immutable SHA/worktree/output checks, 45 for method-scoped green baselines, 20 for mandatory missing-control discovery plus its first cycle, and 30 for restoration audit/report. Thus **Mutation floor = 10 + 45 + 690 + 20 + 30 = 795 minutes**. Add any newly discovered controls/variants that exceed that allowance. No “zero PCs” shortcut applies.

**Total verification floor = Code 215 + Mutation 795 = 1,010 minutes (16 hours 50 minutes), estimated serial elapsed budget.** Code's implementation/test authoring and required Review are additional: Code ExpectAbout must be its authoring estimate **plus at least 215 minutes**; Mutation ExpectAbout **at least 795 minutes**. These are planning reservations, not measured runtimes or deadlines for killing a quiet test host.

Cost saving is method-scoped execution. A budgeting comparison of four minutes of selected-class execution versus one minute of selected-method execution per arm saves 158 x 2 x (4 - 1) = **948 minutes** relative to that class-per-PC alternative; this is an estimate, not a repository measurement. The per-lane cycle estimates still include rebuild and slower native/crash setup. No saving is credited for broad-suite omissions, the absent nightly job, early stopping after a mutant is killed, or unproven batching. Independent methods in different files may be batched only under the testing guide; isolated same-tip local worktree shards are optional, must own/await all processes, and cannot run alongside Pty/FakeClaude. Any claimed parallel saving must report total work plus measured wall time and preserve all per-PC evidence.

Completion gate: all V/R and guard methods implemented and green; every planned PC and missing-control discovery has intended red then restored green; no mutated production/test bytes remain. Mutation reports tested implementation commit C and final artifact commit F, and a C..F diff proving only allowed plan/evidence changes. Surviving mutants or necessary production/test repairs return to Code, then a new Mutation pass. **Mutation next: review is mandatory for this card.** Review checks F's exact source identity and the complete evidence, then produces D-2 evidence for F. Caller lands original Code owner with E=F and that evidence.

Inspection/design audit at handoff: **guards=158, mapped=158, missing=0, duplicate PC mappings=0; all 158 PC rows define a compiling defect, exact method and decisive assertion.** These are design counts. **Executed tests/builds/PCs in TestDesign: 0**; execution belongs to the following stages.


## Rollout and limits

Code/Review evidence must include migration validation on legacy rows, API/CLI compatibility coverage and the new real-Git regression. After the usual authorized canonical deployment, verify the loaded API accepts and echoes the SHA-bound request contract and that a disposable isolated-repository canary reproduces A1 successfully. Health alone is insufficient. Do not use a deliberately wrong SHA against real work as a canary or deploy from this Plan worktree.

This plan needs no product decision to proceed. It chooses explicit caller approval, strict missing/unavailable remote refusal, local-ahead support, immutable retries and versioned legacy recovery as the implementation defaults. It does not promise that a clean automated test suite is a code review, that branch names cannot move after a final remote read, or that legacy receipts prove historical review.
