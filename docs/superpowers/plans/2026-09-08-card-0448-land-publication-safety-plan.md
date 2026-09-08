# CARD-0448: verified source identity and publication before cleanup

Plan stage, 2026-09-08. Ready for a separate TestDesign stage, then Code and a safety-critical Review. No implementation or runtime verification is claimed here.

## Outcome and scope

Replace the branch-existence/ancestry shortcut with a landing protocol that binds one task's actual source to a verified commit, independently confirms that commit on the intended remote destination, and only then authorizes guarded cleanup. A failure to establish any fact preserves the work and reports the uncertainty. Existing task success remains separate from landing success.

The implementation must cover explicit landing, cleanup retries, and the shared removal paths that could later erase a refused worktree. Preserve ordinary child-to-parent local merge semantics; those operations must not acquire a false remote-publication meaning. This is independent of CARD-0442's dispatch/worktree continuity and must not wait for it. Coordinate edits to `WorktreeManager` with CARD-0443's lock-diagnostics work: diagnostics/retries must never restore an unsafe forced-deletion fallback.

Baseline inspected: `f202fcc135bb6701e6c9618dbf0494458fab3fca`. The full investigation was recovered from `GET /api/agent-tasks/f86bd33b` (`result`), because its advertised investigation file and a task commit were absent. Its preserved report is [the investigation artifact](../../investigations/2026-09-08-card-0448-land-false-success.md). Source owners: [orchestration](../../orchestration-loop.md), [project conventions](../../project-context.md), [card lifecycle](../../agent-card-lifecycle.md), [HTTP operations](../../ops-http.md), and [testing/build](../../testing-and-build.md).

## Ground truth

| Premise | Observed implementation/evidence | Design consequence |
|---|---|---|
| The review task's recorded branch identifies the work reviewed. | ac24e1af retained its ancestor branch while its actual HEAD was detached at `a18f250d`. Four commits were absent from the reported master SHA. | Require registration + attached symbolic HEAD + actual SHA + recorded-ref SHA to agree before classification. Do not infer intent from report prose or sibling branches. |
| A missing source branch means landing completed. | `DelegationWorktreeService.IsAlreadyLandedAsync` treats any failed `show-ref` as true; it also ignores fetch failure and falls back to local ancestry. | Delete this boolean contract. Missing, malformed, inaccessible and command-error states are unknown/refused unless a matching durable cleanup receipt explains a completed deletion. |
| Already-present cleanup is subject to normal holds and accounting. | `AgentTaskLandService.RunAsync` runs its shortcut before the Shared-writer hold and `LandAttempt` increment. | One ordered gate for every mode, with a repository lease, task refresh and source inspection. Count a started cleanup or containment attempt too. |
| A resolved target SHA proves a push happened. | `CleanupAlreadyLandedAsync` can do no push and still formats `landed ... pushed`. `FinalizeLandAsync` reads the local target after pushing. | Persist the intended result and independently observed remote ref; never substitute local `rev-parse` for publication proof. |
| Directory removal is ancestry-guarded. | `WorktreeManager.TryRemoveAsync` chooses current branch or metadata fallback, force-removes/deletes the directory, then checks branch ancestry. | Authorize and revalidate before directory mutation; deletion must name the recorded expected branch and SHA. |
| Refusal cannot be undone by later housekeeping. | Residue inspection degrades missing state to ancestor/clean; the residue sweeper can ignore untracked files after `Landed`/`Merged`; TTL prune calls the same remover without a target. | Shared removal must fail closed. Task landing work cannot be deleted on age, event text or a missing branch alone. |
| The CARD-0444/4e84689f refusal deleted an origin branch. | Its refusal precedes cleanup. Land contains no remote-delete command; settlement commits locally. The investigation found no evidence that the source branch had been published. The brief also refers to a CARD-0437 refusal; use the recorded task identity when discussing this evidence. | Do not implement an imagined remote-delete fix. Add an explicit source-publication regression fixture and assert the remote source survives refusal. |
| Local fast-forward followed by crash may be treated as already-pushed. | The existing retry helper pushes an ahead target using current branch/target state; it has no persisted identity tying that state to this attempt. | Resume from a recorded verified commit and destination. A local advance is not publication. |
| The queue is a repository lock. | `AgentTaskLandQueue` has one in-process reader and task-ID deduplication. It does not coordinate another process, janitor, or all repository writers. | Add an I/O lease keyed by canonical Git common-directory identity and revalidate mutable facts at boundaries. |

## Decisions

### D-1. Source identity is mandatory, including for a no-op

Introduce an immutable `LandSourceSnapshot` with task/worktree identity, canonical common Git directory, registered path and per-worktree Git directory, exact `refs/heads/...` recorded branch, symbolic HEAD, actual HEAD commit, recorded-branch commit, and status facts. Resolve the managed path and Git registration independently. Compare path identities using OS-appropriate canonical identity, including junction/alias handling; use Git's ref spelling, not case-insensitive string guesses.

Read registration with porcelain/NUL parsing, resolve commits with `rev-parse --verify ...^{commit}`, use `symbolic-ref -q HEAD`, and inspect index, tracked, untracked, ignored files and nested/submodule state. Take identity readings before and after the status inspection; a changed pair is `source_changed`, not a usable snapshot. Capture bytes/status identities needed for later comparisons; diagnostics list paths and SHAs, never file contents or credentials. Detect active rebase/merge/cherry-pick/sequencer state.

Fresh landing requires the path to be registered in the expected repository, attached to exactly the recorded branch, with actual HEAD equal to the recorded branch SHA, and no uncommitted/untracked work. Refuse detached HEAD even when its commit equals the recorded branch. Refuse branch mismatch even when both SHAs coincide. Missing ref, inaccessible path, ambiguous registration, failed status and Git errors are distinct reason codes. Preserve worktree, refs, registration and contents on these refusals. Never auto-commit, stash, reset, adopt another branch or alter task coordinates to make a check pass.

Ignored files are not automatically disposable. Before deletion, preserve opaque ignored files too. Only known generated outputs with established ownership may be treated as disposable; do not allowlist `.antiphon/**`, `.claude/**` or arbitrary `bin-*` by pattern. A report must already have a verified durable copy before deleting its local copy. It is acceptable to report publication with residue when build outputs or unknown ignored files cannot safely be removed.

Rejected: checking only branch ancestry, matching SHAs without symbolic identity, or trusting a successful review report. None observes the directory about to be deleted.

### D-2. Record a landing operation, not just retry counters

Add `AgentTaskLanding` as a durable entity and `AgentTask.ActiveLandingId` as a nullable association. Use the existing EF context and CLI-generated migration. One active operation per task, enforced in the database; concurrency-token/CAS writes fence transitions. An explicit re-POST resumes an unfinished operation; it may reset the existing automatic-attempt budget but must not erase evidence. A new source/destination after a terminal refusal creates a new operation retaining the old one. It cannot replace an unresolved publication/cleanup operation silently.

| Durable fields | Purpose |
|---|---|
| Id, TaskId, schema version, phase, concurrency token, timestamps, last reason | Stable operation identity and guarded progression across server restart. |
| Canonical repository identity, worktree registration identity/path, recorded source full ref | Bind evidence to the directory and branch that were actually inspected. |
| OriginalSourceSha (`S`), RebasedSourceSha (`P`), VerifiedSourceSha, verification command/filter/outcome/time, skip reason | Distinguish original work, rewritten result and exactly what was verified. |
| TargetFullRef, TargetBeforeSha (`T0`), LocalTargetAfterSha, RemoteName, destination full ref, remote configuration fingerprint | Freeze the destination and local mutation boundary. No token-bearing URL is returned or logged. |
| RemoteBeforeSha, ObservedRemoteTargetSha (`R`), RemoteConfirmedAt, confirmation method | Independent containment proof; target lookup failures leave these unset. |
| PushStartedAt, push exit/acknowledgement state, optional actual-update observation | Separate attempted/acknowledged push from confirmed publication. |
| Recovery ref names, cleanup phase/intent, expected deletion SHA, directory/branch/registration results | Recover safely when Git or filesystem mutation succeeds before the next DB save. |

Recommended phases: `Inspected`, `RecoveryPinned`, `RebaseStarted`, `Prepared`, `Verified`, `TargetAdvanceStarted`, `LocalTargetAdvanced`, `PushStarted`, `PublicationConfirmed`, `CleanupStarted`, `Complete`, `Refused`. Publication evidence is monotonic: an error after `PublicationConfirmed` cannot turn the operation into "not published". Store cleanup failure separately. No long database transaction spans Git/build execution; persist and confirm each checkpoint before its dependent mutation.

Historical event prose, `LandAttempt`, branch disappearance, and a legacy `Landed` event do not backfill a receipt. Existing tasks with intact sources can take the new full inspection/containment path. Legacy missing-source tasks without evidence are explicitly unknown and require recovery review. This intentional compatibility tightening prevents the same inference from surviving migration.

### D-3. Use typed Git results and pinned revisions

Move new process I/O behind `ILandingGit` (Application I/O seam) implemented in `Infrastructure/Git/LandingGit`. Return exit code, bounded/redacted stderr and parsed result; distinguish ancestry exit 0 (yes), 1 (no), and other exits (error). Timeouts and cancellation are not false/absent. A failed `show-ref`, failed fetch, malformed OID, empty remote response or unresolved object must never become `AlreadyPresent`.

Pass argument lists without a shell. Validate source/destination refs, reject source=target, revision expressions, option-like names, ambiguous short names and unsupported symbolic destinations. Resolve `MergeTargetRef ?? "master"` once to a full local branch and explicit remote destination. Keep the current `origin` default; refuse ambiguous/multiple push destinations or a remote identity change while an operation is pending. Publication confirmation must contact the actual push endpoint, including a configured push URL, rather than accidentally checking an unrelated fetch URL. No remote source deletion, force push or mirror push is introduced.

Use immutable SHAs in ancestry, rebase target, target advance and push. Recovery refs use a task/operation namespace such as `refs/antiphon/land/<task-guid>/<operation-guid>/{source,prepared,target-before,remote-observed}`. Create refs with expected nonexistence and update/delete with an expected old SHA; never overwrite a previous operation's refs. Git supports expected-old-value updates/deletes and ref transactions. [Git update-ref](https://git-scm.com/docs/git-update-ref)

### D-4. Keep in-place rebase, but retain the original and refuse ambiguous recovery

Persist `S`/`T0` and establish verified recovery refs before rebase. If pinning or saving fails, do not start rebase. Save `RebaseStarted` before running `git rebase <T0>`; do not silently enable autostash or update unrelated refs. Explicitly disable autostash and `rebase.updateRefs` for this operation. A clean completion must still be on the recorded branch with a clean worktree. Pin the actual post-rebase SHA `P`, then persist `Prepared`.

Retain existing conflict handling: collect conflict paths, abort only the rebase started and still owned by this live operation, confirm the restored source identity, and leave the source/ref pins for the existing Merge helper. If abort fails, preserve the conflict worktree and report the failed abort. Do not start a second helper against unresolved state. A successful Merge helper returns through source inspection and a new preparation/verification; it cannot bless old evidence for its changed SHA.

Replace blanket `AbortInterruptedRebaseAsync` with evidence-aware recovery. A crash during rebase or after rebase but before a durable `Prepared` checkpoint leaves ambiguous ownership of intermediate edits: retain all work and refs, report `interrupted_rebase_requires_inspection`, and require the existing recovery/follow-up workflow to resolve it. Do not erase a human's conflict resolutions merely because `rebase-merge` exists. This deliberately tightens CARD-0331's unconditional auto-abort behavior. A prepared/verified checkpoint with matching `P` can resume the next phase. If verification was interrupted, rerun it; a `Prepared` checkpoint is not a verification pass.

Rejected: a new temporary landing worktree/branch subsystem for this fix. It could reduce in-place mutation, but would also change conflict-helper workspace ownership and cleanup architecture. Durable pins plus conservative ambiguity handling address the required loss/retry windows without that expansion. Recovery refs are retained after success too; no TTL pruning is part of this card. A future explicit retention policy may remove them after independent review.

### D-5. Classify publication independently from cleanup

Use typed `LandPublicationOutcome` (`Landed`, `AlreadyPresent`, `Unconfirmed`, `Refused`) and cleanup status (`NotStarted`, `Pending`, `Complete`, `Refused`); include operation mode `Fresh`, `ResumePublication`, or `CleanupRetry` in the DTO/event details. `CleanupRetry` requires an existing matching `PublicationConfirmed` receipt, not inferred ancestry or missing branches.

`AlreadyPresent` is allowed only after fresh valid source identity and a successful independent remote observation positively contains that same source SHA, or after consulting a valid recorded `P` on a resume. No rebase/build is needed for a fresh `S` already published, but record that verification was skipped because exact commit containment was established. Pin source/target/remote recovery refs and persist the receipt before cleanup on this path too. A local-only ancestor continues preparation/publication; it is not already-present on the remote. A valid AlreadyPresent observation requires no local target advance, so a stale local target alone does not invalidate that remote proof; the remote-ahead refusal in D-7 applies when preparing a publication.

Keep `Landed` and `LandedWithResidue` event values for confirmed publication paths; append an `AlreadyPresent` event value without renumbering existing enum values. Its cleanup state is explicit even when residue remains. A cleanup retry repeats the original publication classification with `mode=CleanupRetry`; it does not count a second publication. Update every success consumer to handle the added outcome through structured evidence, including pipeline/readiness and residue classification. No new card column or session state is required.

Example text:

```text
landed source=S verified=P -> origin:refs/heads/master; remote=R confirmed; cleanup complete
already present source=S in origin:refs/heads/master at R; no push attempted; cleanup pending: <reason>
cleanup retry for operation=<id>, published=P confirmed at <time>; worktree kept: source_changed
land unconfirmed at push confirmation: verified=P, local target=P; remote check failed; worktree and recovery refs kept
```

`LandRefused` may represent a pre-publication refusal or unconfirmed publication, with a machine-readable reason and actual local mutation state. Documentation must stop claiming "target did not advance": a rejected push can follow a local fast-forward, and a lost response can follow a remote update. Never say "pushed" merely because the target resolves, or "branch kept" without observing whether it still exists. A post-confirmation cleanup failure reports known publication with residue, not `LandRefused`. Events, DTOs and delivered notes derive from the same typed result, never reparsed prose.

### D-6. Serialize by repository and fence changes during verification

Provide `IRepositoryMutationLease` as an I/O seam, implemented using an exclusive OS/file lease under the canonical Git common directory (e.g. `antiphon/landing.lock`). Hold it across inspection, fetch, rebase, verification, publication and cleanup. A linked worktree and main checkout must share one key; two unrelated repos must not block one another. An occupied lease yields a visible hold and leaves the request pending. File existence alone is not ownership and does not authorize deleting a lock file.

All participating repository mutations must use the lease: explicit lands, child merge-back/settlement commits that overlap their source/target, worktree cleanup, residue sweep and TTL prune. Thread the acquired lease through nested calls to avoid reacquisition deadlocks. Coordinate Shared writer admission with the same repository exclusion: admission takes the lease while publishing its durable running claim; land takes it before reading those claims and holds it until completion. Check existing Shared writers before any landing mode and prevent admission of a new Shared writer while land owns the lease. Also hold behind any active follow-up/helper claim on the exact source worktree; a Succeeded task row does not mean a later task is not using its directory. Worktree agent edits and external manual Git commands cannot be forced to obey it; boundary revalidation is therefore still mandatory. Preserve current pending/hold behavior without holding a DB transaction for a build.

Every mutating child process is owned, cancellable and awaited. Terminate only that operation's children on cancellation before releasing the lease. Journal process identity (PID plus start identity) for restart checks: do not overlap a surviving Git child after a server crash or equate a reused PID with ownership. If an interrupted mutation's process ownership cannot be established, keep the operation refused/held and preserve work. This is not permission to kill unrelated Git processes or remove Git's own lock files.

At minimum, repeat source registration/branch/HEAD/status and target identity checks after fetch, before rebase, after rebase, before and after verification, before local advance, before push and before each deletion. After verification, HEAD must equal the pinned verified SHA and target must still equal `T0`. Any source movement or source dirtiness invalidates verification and refuses publication. A target change before local advance requires fresh preparation and verification on a subsequent attempt; do not silently merge or reuse old results. Metadata changes also invalidate the operation.

Advance an un-checked-out target with an expected-old-SHA update after proving `T0` is an ancestor of `P`. For a checked-out target, require matching symbolic branch/HEAD and clean index/worktree, then `merge --ff-only <P>` and verify HEAD equals `P`; never use `update-ref` alone beneath a checkout. If the checkout moves during the command, stop with explicit state and retained refs; never reset it backward. Detect target movement before push and refuse until revalidated. Local locks coordinate participants, while Git ref atomicity and non-forcing operations preserve concurrent work from other writers; they do not promise a filesystem transaction against arbitrary external editors.

### D-7. Prove the intended result is remotely published

Preparation must complete a fresh remote read/fetch before deciding remote ancestry. Keep the current local-target policy: a remote target ahead of or divergent from local `T0` refuses with the existing refresh guidance, rather than pulling/restarting the shared checkout. If local `T0` is ahead of the remote, publication may include that captured canonical target history plus this source, as today; record both SHAs. Do not publish later unrelated local changes.

After verification, persist the intended `P` and `TargetAdvanceStarted`, fast-forward the local target, observe/save `LocalTargetAdvanced`, then persist `PushStarted`. Push the explicit refspec `<P>:refs/heads/<destination>` to the frozen push endpoint without force. An explicit refspec fixes both source and destination rather than relying on upstream/push defaults. [Git push](https://git-scm.com/docs/git-push)

After the push attempt, independently read that endpoint's exact destination ref, fetch the observed commit into the operation's dedicated observation ref/object store, and verify `P` is an ancestor of the observed remote SHA `R`. Correlate the fetched ref with the independent ref read; if the remote moves between them, repeat the read/fetch/compare a bounded number of times (three complete observations), otherwise return `remote_changed_during_confirmation`. Do not use a shared tracking ref or `FETCH_HEAD` as the durable proof. Reject deletion, missing target, failed object retrieval or a noncontaining rewrite. A descendant `R != P` is valid: somebody else may have fast-forwarded after publication.

Push exit zero without successful remote proof is `Unconfirmed` and preserves work. A rejected push is normally refused; if an independent observation nevertheless proves the exact intended `P` present (e.g. another publisher won), record `AlreadyPresent` and the rejected push separately. A network-loss/timeout after remote acceptance may likewise be reconciled by positive proof. The success claim is containment of `P`, never that this client necessarily performed the remote update.

Persist `PublicationConfirmed` with `P`, `R`, destination and timestamp, and verify the save, before calling any cleanup method. If the save fails, retain everything; retry may re-observe the remote and commit the receipt. On cleanup retry, independently recheck containment again. A now-unreachable or rewritten remote prevents further deletion; retain the historical receipt and report cleanup residue rather than erase evidence of the earlier publication.

### D-8. Make worktree deletion require explicit, current evidence

Replace the landing call to `TryRemoveAsync(repo,path,mergedInto)` with a typed removal request carrying expected repository/worktree identity, exact branch and expected SHA (`P` after rebase, `S` for a fresh already-present result), durable publication operation identity, and the acquired repository lease. The low-level implementation must re-read these facts; a passed DTO alone is not proof. Do not provide a permissive default implementation that delegates to the old `RemoveAsync` and returns clean.

The sequence is: confirm receipt and current remote containment; inspect actual registered source and all protected contents; persist `CleanupStarted`/expected removal identity; repeat the guard immediately before removal; perform ordinary `git worktree remove` without force; inspect registration/directory result; then delete only the exact expected local branch with an expected-old-SHA operation. Before branch deletion check all worktree registrations for a new checkout of that branch. A moved branch or failed inspection is residue, never permission to delete another branch. Retain all recovery refs.

Git documents clean-worktree requirements and that forcing can remove unclean worktrees. Normal landing will not use `--force` or `--force --force`; a future explicit force action would still need a current identity/content guard and separate scope. [Git worktree](https://git-scm.com/docs/git-worktree)

Remove recursive directory deletion as an automatic fallback for landing/task residue. A locked, timed-out, partially removed or already-unregistered nonempty directory is preserved as residue if its content identity cannot be proven. Never infer branch identity from its directory name or detached-HEAD fallback. Do not delete the branch while directory removal is incomplete; leaving both makes recovery simpler. A recreated directory or branch on cleanup retry must match the expected identity or be preserved.

Missing components are idempotent only with matching durable prior publication AND cleanup intent: missing directory/registration plus a surviving expected branch permits guarded branch cleanup; both already absent permits completion. An existing directory without a valid Git registration is unknown residue and is not recursively erased. Failure of a Git query is never treated as an absent component.

General `WorktreeManager` paths must also guard contents and identity before any deletion. Local child merge cleanup uses a separately typed `LocalMerge` proof (source and actual parent-target SHA containment), never a `PublicationConfirmed` receipt or a `Landed` report. Unknown-purpose removals and TTL-only task cleanup preserve task work. Worktree-creation rollback remains a distinct operation allowed to undo only its own incompletely created checkout/registration after proving it never became a working task. Do not silently broaden this exception to mature worktrees.

### D-9. Housekeeping cannot bypass a landing refusal

Update `WorktreeResidueSweepService`, `InspectResidueAsync`, and `PruneStaleAsync` to propagate unknown/error states and request the same guarded removal. Never ignore untracked/opaque ignored work because an event said landed. Any active/unconfirmed landing protects its path and branch. For legacy task residue without receipts, default to kept/unknown; do not migrate old potentially false events into deletion authority.

The low-level task-removal policy must work even when the janitor has no DB task lookup: identify task-managed entries from registration/metadata and require the typed publication/local-merge authorization, otherwise keep them. Recovery refs and operation metadata are not janitor candidates. If classification occurred before a source change, execution must independently detect it under the lease. Existing residue counts and diagnostics must show the refusal without counting it as removal.

### D-10. Keep recovery, attempts and notification semantics honest

Preserve durable request sweep, bounded automatic attempts, explicit retry, conflict helpers and the normal async 202 request response. Increment attempts after acquiring permission to run and before every attempted mode, including AlreadyPresent and CleanupRetry; a lease/Shared-writer hold does not spend an attempt. DB CAS/task refresh prevents two service instances from advancing the same operation. `ClearPending` clears scheduling fields only, not the landing evidence.

Persist terminal evidence and its event in one transaction. Deliver/publish notifications after that transaction; notification failure must not rewrite confirmed publication as `LandRefused`. `FailAsync` inspects phase and emits post-publication residue where appropriate. Sweep follows receipts, not event text, when recovering an interrupted run. No automatic card Done transition is introduced.

## Ordered operation

1. Reload pending task; acquire repository exclusion; recheck task status, Shared writers and operation identity. Choose Fresh/ResumePublication/CleanupRetry from durable evidence only.
2. Inspect all present source components. A cleanup receipt can explain an absent component, but never an identity mismatch in an existing component. Refuse unknowns before shortcuts or mutation.
3. Independently inspect remote target. A fresh intact source already contained remotely gets an AlreadyPresent receipt and may enter guarded cleanup. Local-only containment proceeds to publication.
4. Pin recovery refs and persist original source/target. Rebase to pinned target; preserve/conflict-refuse as D-4 requires. Capture/pin `P` and verification results. A verification skip records the exact reason and SHA.
5. Revalidate source and target; persist intent, advance local target to pinned `P`, observe and save. Persist push intent, push explicit destination and independently confirm remote containment.
6. Persist publication proof. Revalidate and remove only authorized components; record residue or completion. Keep receipt/ref pins permanently for this release.
7. Commit the outcome event with pending-field changes. Send the typed outcome through existing delivery and SignalR paths; release only owned processes/lease in finally.

## Crash and retry contract

| Durable boundary / observed state after restart | Required behavior |
|---|---|
| Request queued or held, no source inspection | Reacquire/reinspect; no inferred success. |
| Recovery intent saved, only some pins created | Check expected ref values; finish pinning only if unchanged source/target. Otherwise refuse and retain pins. |
| Rebase started, no durable Prepared identity | Keep original ref, intermediate work and directory; explicit interrupted-rebase diagnosis. No blind abort, no publication/cleanup. |
| Prepared at P, verification not recorded | Require unchanged source P; rerun required verification. |
| Verified at P, before target mutation | Revalidate exact source and T0; advance only from the expected target. |
| TargetAdvanceStarted; target now P; before LocalTargetAdvanced save | Verified receipt plus current matching source and exact target P allows recording the completed local advance. A different target is a changed-state refusal, not presumed completion. |
| Local target advanced, before push | Resume publication of recorded P to the recorded destination. Never enter cleanup based only on local ancestry. |
| Push started; acknowledgement lost / before receipt save | Independently re-observe remote. Containing P allows receipt persistence; unchanged remote allows safe non-forcing retry; error/noncontaining movement preserves work. |
| PublicationConfirmed, before cleanup or outcome delivery | Refresh remote proof; inspect source; retry cleanup only. Do not rebase or repeat publication. |
| CleanupStarted; directory removed, branch still present | Receipt + expected deletion identity allow guarded exact-branch cleanup. A changed/rechecked-out branch stays. |
| CleanupStarted; directory/branch absent, completion save lost | Successful absence checks plus matching receipt/intent allow Complete. No branch-disappearance-only shortcut. |
| Published receipt exists; source changed, directory recreated, dirty residue or remote rewritten | Publication remains historical fact; cleanup refused/pending, all new work retained. |
| Complete; duplicate request | Return the receipt's classification and verified current cleanup result; no fresh publication claim. |
| Evidence save or notification fails | Before receipt save: no cleanup. After receipt save: resume from receipt; notification errors do not negate publication. |

## Implementation slices

Land this plan before TestDesign dispatch. Code implements the following in order; intermediate commits are reviewable, but do not deploy a partially guarded cleanup path.

| Slice | Files / changes | Required tests |
|---|---|---|
| S1: typed identity and Git evidence | New `server/Application/Dtos/LandingDtos.cs`, `server/Application/Interfaces/ILandingGit.cs`, `server/Infrastructure/Git/LandingGit.cs`; replace land boolean helpers in `DelegationWorktreeService.cs`; parse/ref validation and unknown/error handling. | New `LandingGitTests` (Infrastructure), `LandSourceIdentityTests` (Application); update `DelegationWorktreeTests`. |
| S2: durable protocol and recovery refs | New `Domain/Entities/AgentTaskLanding.cs`, enum definitions; `AgentTask.cs`, `Infrastructure/Data/AppDbContext.cs`, CLI migration and snapshot; new concrete `AgentTaskLandingState` transition policy; checkpoint/ref ordering in `AgentTaskLandService`. | New `AgentTaskLandingStateTests`, `AgentTaskLandingPersistenceTests`; migration upgrade with legacy rows, no evidence backfill; update request/sweep tests. |
| S3: serialization and verified publication | New `IRepositoryMutationLease`/`RepositoryMutationLease`; DI in `server/Program.cs`; update `AgentTaskLandService`, `DelegationWorktreeService`, lease/admission integration in `AgentTaskDispatcher` and settlement/merge-back callers; pinned target advance, verification rechecks, explicit push and remote confirmation. | New `RepositoryMutationLeaseTests`, `AgentTaskLandPublicationTests`, `AgentTaskLandConcurrencyTests`; update `DelegationWorktreeTests`, `AgentTaskLandStageOutcomeTests`. |
| S4: guarded deletion and housekeeping | `IWorktreeManager.cs`, `WorktreeManager.cs`, `WorktreeRemoval.cs`, residue DTOs, `WorktreeResidueSweepService.cs`, `WorktreeJanitorHostedService.cs`/`WorktreeResidueJob.cs` where routing changes; remove permissive default and task deletion fallbacks; preserve creation rollback separately. | `WorktreeManagerTests`, `WorktreeResidueSweepTests`, new `AgentTaskLandCleanupSafetyTests`; update all IWorktreeManager fakes and `TestHelpers/DelegationTestServices.cs`, run `DelegationTestServicesTests` and harness census. |
| S5: resume/DTO/outcome integration | `AgentTaskLandService` Request/Run/Sweep/Fail; `AgentTaskService` DTO mapping, `AgentTaskDtos.cs`, `AgentTaskEnums.cs`, client `api/agentTasks.ts` and actual event-rendering consumers; structured event/result formatter; preserve Rebase/Verify/Cleanup stage accounting. | `AgentTaskLandRequestTests`, `AgentTaskLandSweepTests`, `AgentTaskLandStageOutcomeTests`; new `AgentTaskLandRecoveryTests`; targeted client event rendering/serialization tests if changed. |
| S6: documentation and acceptance | `docs/orchestration-loop.md` sections 0/5/8, `docs/ops-http.md`, `docs/antiphon-api.md`, relevant land help/bundle text and `scripts/delegate.ps1` only if it consumes the new structured outcome; remove branch-gone/pushed/target-did-not-advance promises. | All TestDesign V/R/PC items, named regression classes and isolated real-Git recovery tests. Safety-critical Review must inspect mutation ordering and every cleanup entry point. |

The existing verifier invokes `dotnet test`, contrary to the testing owner. If touched as part of introducing a verification I/O seam, use the documented `dotnet run --project tests/<Project>` contract and validate the exact class filter. Do not add an unrelated build-tool redesign. Tests must inject deterministic verification results/barriers for Git safety cases rather than building the whole repository per scenario; keep one scoped real verification compatibility check.

## Required acceptance cases for TestDesign

TestDesign must append an executable `## Verification design` with V-n/R-n/PC-n, exact class filters, injected fault boundaries and expected assertions. This section is its required coverage input, not a claim that tests have run. Every destructive guard needs a positive control that demonstrably fails at its intended assertion when the guard is broken; fixture/build/zero-test failures do not count.

Use disposable repositories with a bare local remote, source and target checkouts, deterministic barrier-controlled Git/verification seams, and persisted operation rows. Test remote state from the bare repository or a fresh independent clone, never solely `origin/master`. For every refused destructive step assert exact source refs/HEAD/registration, sentinel file bytes (tracked, untracked and relevant ignored), recovery pins and remote source/target values. If rebase or local FF legitimately preceded refusal, assert the documented S/P/T0 preservation and local mutation instead of pretending nothing changed.

| Case | Required observation |
|---|---|
| A1: detached unique HEAD; recorded branch is ancestor of both local and remote target | Refused before rebase/push/removal; unique HEAD and all contents remain. This is the incident reproduction and must fail on baseline. |
| A2: switched branch, including same SHA on two different branches | Mismatch refusal; neither actual nor recorded branch deleted; never select branch from prose. |
| A3: recorded ref missing; separate unreadable/broken-repo/show-ref error cases | Unknown/refused, not AlreadyPresent; directory retained. |
| A4: dirty ancestor source, tracked staged/unstaged, untracked, opaque ignored, submodule/nested repo | No cleanup or hidden auto-commit/stash. Test dirtiness introduced immediately before deletion too. |
| A5: failed fetch; stale cached tracking ref contains source | No shortcut or success; source retained. Include ancestry exit 128 and malformed remote response. |
| A6: remote ahead/diverged from local target | Existing refresh refusal; no cleanup and no implicit pull. |
| A7: local-only containment and local target ahead | No AlreadyPresent; publish pinned P and independently confirm it before cleanup. |
| A8: push rejected by remote hook/non-fast-forward | Worktree/ref pins remain; recorded local advance is truthful; no confirmed publication unless independent containment actually proves P. |
| A9: push reports success but remote confirmation fails or contains another commit | Unconfirmed; no cleanup. Include different push/fetch URLs and changed remote configuration. |
| A10: source HEAD/ref/registration or dirty state moves during verification | Old verification cannot authorize advance/push/deletion. Inject each mutation at a named barrier. |
| A11: local target advances during verification or checked-out target becomes dirty/switches | Refuse/reprepare on retry; never reset another writer or publish its later changes implicitly. |
| A12: remote target advances before push and after push | Noncontaining movement refuses; a confirmed descendant containing P succeeds and records R != P. No force push. |
| A13: two landing services/processes, same repo via different worktree/alias paths | Mutual exclusion and task CAS; second operation reloads state. Different repo can proceed. |
| A14: Shared writer present; then attempted Shared admission during land; cleanup-only shortcut | All modes respect holds; no admission race allows shared target mutation. |
| A15: crashes at every row in the retry table | Instantiate a fresh service/DbContext/queue; assert exact next commands and absence of forbidden cleanup. Required automatic successes: local-FF-before-push resume and confirmed-push-before-cleanup retry. |
| A16: crash after target mutation / remote push / directory removal but before each next DB save | Durable intent + independent observation recover only the intended operation; save failure never grants deletion authority. |
| A17: rebase interrupted with manual conflict resolution or unknown ownership | Preserve edits and original pin; no blanket abort. Successful current-attempt conflict abort preserves existing helper flow. |
| A18: cleanup retry after source branch advances, detaches, switches, becomes dirty, is checked out elsewhere or path is recreated | Known publication with residue; preserve new work and exact changed branch. |
| A19: missing directory/ref with valid receipt and cleanup intent versus no receipt | First is idempotent cleanup; second unknown/refused. Query failure is a separate non-idempotent case. |
| A20: locked/partially removed/unregistered nonempty directory | No recursive deletion fallback; recoverable residue and branch preserved. |
| A21: explicitly push a source branch before each refusal family | Bare remote still has exactly that branch SHA afterward. Never infer publication from branch absence. |
| A22: positive fresh publication; valid AlreadyPresent; valid CleanupRetry | Different structured outcomes/text; no "pushed" on no-push path; correct attempt counters and stage outcomes. |
| A23: receipt save fails; event/delivery fails after receipt save | No cleanup in first; publication evidence survives and is not rewritten as refusal in second. |
| A24: residue/TTL sweep after refusal or legacy false Landed event | Same contents/ref guards apply; no deletion based on age/event or missing ref; untracked ignored-work distinction stays safe. |
| A25: child local merge, no-change child, create rollback, task worktree reuse | Existing supported local behavior stays distinct from remote landing; each deletion has correct identity/content authority. |
| A26: rebase changes SHA, then source branch deletion | Both S and P remain reachable by recovery refs; receipt points at verified P, remote contains P; original-ref retention does not rely on reflog expiry. |
| A27: remote rewrite/unavailability after publication and before cleanup retry | Historical confirmation remains; fresh cleanup is refused; no deletion. |
| A28: process cancellation/restart with live owned Git child and lease contention | No overlapping mutation and no unrelated process kill; uncertainty preserves work. |
| A29: succeeded task's source reused by an active follow-up/helper | All landing modes wait for its durable worktree claim; no removal of a directory in active use. |

Use meaningful red controls for identity, dirtiness, error-to-unknown mapping, durable receipt prerequisite, remote containment, source/target verification rechecks, deletion ordering/exact-ref CAS, shared exclusion and legacy housekeeping bypass. A1 alone cannot prove the publication/recovery guards.

Process-spawning fixtures carry the assembly's `ParallelLimiter<ProcessSpawnLimit>` and unique temporary roots. Use the shared TestDbFixture with task/operation-scoped assertions and `DelegationTestServices` registration. A global sweep test uses ungrouped `[NotInParallel]`. No test uses the production runner, live origin, live board state, real agent launches or the shared development database.

Expected commands to specialize in TestDesign (one class per invocation is valid):

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448/ -- --treenode-filter "/*/*/AgentTaskLandRecoveryTests/*" --report-trx --report-trx-filename c448-recovery.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448/ -- --treenode-filter "/*/*/WorktreeManagerTests/*" --report-trx --report-trx-filename c448-worktrees.trx
pwsh -File scripts/test-client.ps1 <changed-event-test-file>
```

Require nonzero executed test counts and intended method names in fresh TRX, plus actual assertion failures for red controls. Run Antiphon.Tests and any later Pty checks sequentially. Clean only verified task-owned `bin-c448` outputs. No runtime tests/builds are needed for this Plan-only artifact; Code records each V/R/PC result and the real failures. TestDesign estimates its scoped execution floor from the selected fixtures rather than mandating an unrelated full suite.

## Rollout and completion

Ship the migration and all safety gates together. There is no automatic historical receipt backfill, remote branch cleanup, live stack restart or deletion authorized by this Plan task. During deployment use the canonical main-checkout restart runbook; verify checkout/build identity directly before testing a real landing. A safe isolated real-Git acceptance run must pass before exercising live task cleanup. The first supervised production use should have an intact attached source and compare returned S/P/R against independently read remote history; operator intervention in this bug's damaged worktrees remains a separate recovery action.

Rollback must stop automatic landing/cleanup execution before restoring code that trusts missing refs or ignores receipts. Retain the additive schema, operation history and recovery refs; do not downgrade by deleting evidence. Increased residue for legacy/dirty/unregistered worktrees is an intentional safety result, not permission to bypass the guard. No user decision blocks the plan under these defaults.

Completion requires TestDesign's regression/positive-control evidence and Review approval of source identity, remote publication proof, crash windows, all shared removal callers, and truthful outcome mapping. A green health check or unchanged master SHA cannot substitute for those proofs.
