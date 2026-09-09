# Explicit retries after a refused land operation

Plan task: `cb2477f8`; baseline: `9157a181` (2026-09-09).
The brief supplied no board-scoped card identifier, so this artifact uses the task identifier.

## Outcome and scope

An explicit land retry after a terminal `LandPhase.Refused` operation may allocate a fresh
operation even when the source and target ref names and source SHA are unchanged. The new
operation reads current target state and follows the complete inspection, preparation,
verification, publication and cleanup protocol. A still-dirty or unreadable target refuses
again with fresh operation evidence; cleaning the target does not itself authorize publication.

This is a small policy correction inside the existing landing protocol, with regression
coverage across its durable replacement boundary. No API, entity, enum or migration is needed.
It supersedes the changed-source/destination restriction in CARD-0448 D-2 and generalizes its
F1 interrupted-rebase exception to eligible terminal refusals. The other CARD-0448 safeguards
remain requirements. Treat verification as a separate TestDesign stage because this gate
controls recovery and preservation of publication evidence; no complexity label was supplied.

## Ground truth

| Brief assumption or possible fix | Current code at the baseline | Design consequence |
|---|---|---|
| A repeated land request starts over. | `AgentTaskLandService.RequestAsync` sets `LandRequestedAt`, filter and attempt fields but retains `ActiveLandingId`. `ClearPending`, used by refusal settlement, also retains that pointer. | Retaining the pointer is necessary for recovery and audit; do not clear it as the fix. |
| Target cleanliness might be cached. | `LandingGit.RunAsync` starts a new Git invocation. `AgentTaskLandingProtocol.CheckTargetAsync` runs target status with untracked files and submodules included and requires success plus empty output. | Leave Git I/O and the target status predicate intact. Reach them through a fresh operation. |
| Valid fresh source inspection is enough for an explicit retry. | `AgentTaskLandingState.CanReplaceRefused` additionally requires an interrupted-rebase reason or changed source ref/SHA, target ref name, common directory or worktree path. It observes no target cleanliness or target commit change. | Remove this final reason/identity-difference disjunction. Eligibility need not prove which external condition changed. |
| The old operation must be deleted or reset. | `PersistNewOperationAsync` deactivates the previous row, inserts the new row and changes the task pointer in one transaction, with the old-row save first for the unique active-operation index. | Reuse this mechanism; retain the previous row and its recovery refs. |
| Every `LandRefused` event means a replaceable operation. | Refusals from early phases become `LandPhase.Refused`; refusals after `Verified` or target-advance/push intent can retain their acknowledged phase. Publication and cleanup have separate statuses. | Gate on `LandPhase.Refused`, never event text, `LastReason` alone, or `Publication == Refused`. |
| Restart recovery is another explicit request. | The protocol uses `task.LandRequestedAt > op.UpdatedAt`; the sweep keeps the original request timestamp and enforces its attempt budget. | Keep the explicit-request guard; elapsed time, a restart, or target cleanup alone must not reopen a refusal. |
| Interrupted rebases and completed publications need the new behavior. | Reload/schema/active/owned-child checks precede replacement. `RebaseStarted` refuses for inspection; valid publication enters cleanup on the same operation. `Verified` has a separate changed-preparation rule. | Preserve these branches and their order. |
| Existing recovery tests reproduce unchanged-source target cleanup. | State tests cover the interrupted-rebase exception. `C448_V31_FailedReplacementKeepsThePreviousOperationRetryable` creates an empty source commit before retry. Target-dirtiness tests cover refusal at a mutation boundary. | Add a target-only repair regression without manufacturing source evidence. |

## Decisions

### D-1. An eligible explicit retry is the reason to create a new attempt

Retain the signature and these conjuncts in `AgentTaskLandingState.CanReplaceRefused`:

```csharp
explicitRequest && leaseHeld && previous.SchemaVersion == 1
    && previous.Phase == LandPhase.Refused && !HasPublication(previous)
    && freshInspection.Accepted
    && freshInspection.Snapshot!.Coordinates.TaskId == previous.TaskId
```

Delete only the final parenthesized reason/identity-difference condition. Update the method
summary to explain explicit retry after terminal refusal and fresh inspection. The caller
continues to verify the active operation and actual repository lease ownership; the policy
does not replace either check.

This applies to every refusal reason that reaches this phase and passes the existing gates,
including transient target status errors, remote observation errors and verification failures.
It does not assert that the refusal was repaired: the fresh protocol establishes that or
refuses again. Source identity is still validated against current task coordinates, even
though it need not differ from the previous operation.

Rejected: add only `target_dirty_or_unknown` to the exception list. That fixes the example
but leaves the same replay defect for transient remote failures and other target-state changes.
Rejected: compare target HEAD/cleanliness inside `CanReplaceRefused`. This duplicates I/O,
cannot cover all transient causes and still needs the protocol's boundary checks.

### D-2. Preserve request and recovery semantics

Keep `RequestAsync`, `SweepAsync`, `ClearPending` and the existing strict timestamp comparison
unchanged. The request still queues before Git runs. Duplicate active requests still conflict;
the same pending request after a crash remains recovery, with the same attempt limit.

| Loaded operation | Explicit retry behavior |
|---|---|
| `Refused`, supported schema, no publication, fresh source accepted | Eligible for transactional fresh operation after the existing admission checks. |
| `Refused` with stale/equal/absent request marker, rejected source inspection, missing lease or wrong task | No replacement. Preserve the active operation and remaining work. |
| `RebaseStarted` or unresolved owned child | Existing inspection/ownership refusal applies before replacement. No auto-abort or overlapping mutation. |
| `Verified` | Keep the separate changed-preparation rule and fresh verification requirements. |
| `TargetAdvanceStarted`, `LocalTargetAdvanced`, `PushStarted` | Resume/refuse against recorded identity and checkpoints; never discard unresolved publication intent. |
| Confirmed publication or cleanup residue | Same-operation guarded cleanup; no new publication claim. |

Rejected: clear `ActiveLandingId` in the request endpoint, change an old row back to `Inspected`,
or make all unconfirmed operations replaceable. Each loses the distinction between a terminal
refusal and unresolved mutation/publication evidence. There is no automatic retry loop or new
operator override in this change.

### D-3. Reuse the transaction and acquire all new evidence

Keep the refused branch in `AgentTaskLandingProtocol.RunAsync`: reload, inspect, call the
policy, set `previousToReplace`, and prepare the replacement through the existing fresh path.
An explanatory comment may change; no new replacement path is needed.

The replacement receives a new ID and recovery-ref namespace, reads the current source SHA,
target commit, destination, target checkout and requested verification filter, and defaults to
`Fresh` mode. It inherits no verification, push, publication or cleanup authority. The old row
and all existing pins survive; only its active flag and concurrency token change when the
replacement commits. Failed preparation or a failed replacement transaction must leave the
old row active and the durable task pointer intact.

After commit, the normal pinning, remote observation and target checks run. In the regression
fixture the source must not already be remotely contained, so this reaches `CheckTargetAsync`
before rebase. Existing exact remote containment may still produce `AlreadyPresent` and guarded
cleanup without preparing/advancing the local target; do not turn a target cleanliness check
into a new prerequisite for that established publication proof.

Keep repeated target checks around verification and target mutation. Never clean, stash,
commit, reset or otherwise repair the operator's dirty checkout as part of retry.

### D-4. Update the living contract with the implementation

Add a short paragraph in `docs/orchestration-loop.md` section 5 when Code lands: an explicit
retry of an eligible terminal refusal creates a new operation even with unchanged source;
it repeats validation, keeps prior evidence and may refuse again. Distinguish this from
same-operation publication recovery and cleanup. The historical CARD-0448 plan remains a
historical artifact; this plan records the amended decision.

## Implementation slices

| Slice | Files | Work and acceptance |
|---|---|---|
| S-1: replacement eligibility | `server/Application/Services/AgentTaskLandingState.cs`; `tests/Antiphon.Tests/Application/AgentTaskLandingStateTests.cs` | Apply D-1. Cover identical accepted source with dirty-target, transient and interrupted-rebase reasons; keep all negative admission cases. |
| S-2: request-to-settlement regressions | New `tests/Antiphon.Tests/Application/AgentTaskLandRefusedRetryTests.cs`; minimal additions to `tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs` | Add V-1 through V-7 below using real fixture Git and isolated PostgreSQL. Add a harness method calling actual `AgentTaskLandService.RequestAsync` for the public retry path; the existing `RepostAsync` directly writes columns and alone does not cover that path. |
| S-3: recovery regression and documentation | Existing landing recovery/request/publication tests named below; `docs/orchestration-loop.md`; optional comment in `server/Application/Services/AgentTaskLandingProtocol.cs` | Run relevant regressions, amend the living contract and report evidence. Do not refactor the replacement transaction or Git implementation. |

## Verification design

Finalized by TestDesign task `b32ae8a7` against checkout `7fc2020e`, which contains the
Plan commit `10cfbc63`. This section is the executable contract for Code; no production
change, test implementation, test run or positive-control run was performed by TestDesign.

### Harness and deterministic evidence

Use `LandingSafetyHarness` and `LandingGitFixture` with their private bare remote, source
worktree, canonical target checkout, independent observer and isolated PostgreSQL schema.
Put V-1 through V-6 in new `AgentTaskLandRefusedRetryTests`, tagged
`[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`. Put V-7 in the existing
Unit-category `AgentTaskLandingStateTests`. Do not boot `Program` or a session runner.

Make these small, test-only harness additions; retain existing defaults for other tests:

- Add `RequestAsync(string? filter = null)` and `SweepAsync()` wrappers that open fresh
  scopes and call the actual `AgentTaskLandService`. Share one exposed `AgentTaskLandQueue`
  per service graph through `CreateLand`; rebuild it in `RestartServicesAsync`. A queued-run
  helper must dequeue the exact task/filter, call `RunAsync` and `Release` in `finally`,
  mirroring the hosted drain. Leave existing direct-run tests working. `RepostAsync` alone
  is not evidence that the public request path preserves the active pointer.
- Add a harness `Clock` defaulting to `TimeProvider.System`; use it in `BuildServices` and
  the `AgentTaskService`/`AgentTaskLandService` constructors currently hard-coded to System.
  For these tests use a per-harness real-offset provider:
  `GetUtcNow() => DateTimeOffset.UtcNow + offset`, retaining the same provider across restart.
  Before the first run advance it at least one second beyond the persisted initial request;
  before each explicit request advance it at least one second beyond freshly loaded
  `A.UpdatedAt`. Reload and assert the stored `LandRequestedAt > A.UpdatedAt` before running.
  This avoids sleeps, frozen queue clocks and PostgreSQL sub-microsecond rounding. Do not
  modify A's timestamps to make an explicit request eligible.
- Extend `ControlledVerifier` to record `(worktree, filter)` for every invocation, preserving
  `Calls`, `Passed` and `Barrier`. The integration verifier is controlled evidence; it does
  not recursively run dotnet. Existing verifier coverage remains separately listed below.
- Use test-local `BeforeCommand(repository, args)`/`AfterCommand` hooks to record copied
  argument arrays, normalized repository paths and results. `FixtureGit.Trace` has no paths,
  and `AfterCommand` is not invoked for a `BeforeCommand`-injected result: record that result
  and a hit flag in the injecting hook itself. Compose fault injection with recording.
  Leave `BeforeObservedCommand` installed so durable mutation-boundary evidence still works.
  Clear/take an attempt trace immediately before running, then snapshot it before observer
  assertions issue their own Git commands.

Use `F` for the fixture and `A`/`B` for the old/replacement operations. Except where stated,
start with `AddSourceAsync()` so source commit `S` is unpublished and differs from
`F.SeedSha`; the fixture's already-published remote source must remain at `F.SeedSha`.
Create `Path.Combine(F.Repository, "retry-target-sentinel.txt")` with known bytes, outside
ignored paths. Before A, independently establish remote master does not contain S and
the canonical target's full status reports that sentinel. Target repair means deleting
only that fixture-owned file. Never create an empty source commit to trigger replacement.

For every explicit-retry case, capture before/after request state through separate
`AsNoTracking` contexts: the call returns `queued` (or `requeued` after an interrupted
pending attempt), writes a new `LandRequested` event/filter, resets attempt/start fields
and queues the task, but leaves one active A and `task.ActiveLandingId == A.Id` until
execution. The Git trace must be empty during `RequestAsync`. Each execution uses a new
scope; V-1 also discards the graph between request and execution.

The common replacement oracle is exactly two task-scoped operation rows, only B active,
`task.ActiveLandingId == B.Id`, `B.Id != A.Id`, `B.Mode == Fresh` and a new
`refs/antiphon/land/{TaskId:N}/{B.Id:N}` prefix. Compare all of A's saved scalar evidence
before/after replacement, allowing only `Active` and `ConcurrencyToken` to change.
Enumerate and re-resolve every existing ref under A's prefix, not just one source pin.
No event previously attached to A may be reassigned to B. Assert the new terminal event's
structured `LandingOperationId`, `LandingMode`, `LandingPublication` and `LandingCleanup`;
formatted detail text is secondary. Scope all counts to `F.TaskId`.

At the first B `update-ref <B-prefix>/source ...` hook, read B from a separate DB context:
`Phase == Inspected`, fresh source/target/destination/checkout/filter values, all pin flags
false, `Publication == Unconfirmed`, `Cleanup == NotStarted`, and no rebase, verification,
push, confirmation, deletion or owned-child receipt. This proves fresh authority at a
committed boundary; final successful evidence alone cannot prove it was not inherited.

For success, require `HasPublication(B)`, `Phase == Complete` and `Cleanup == Complete`.
Use a new non-injected `FixtureGit` to read the private remote master and fetch it into
`F.Observer` under a unique observer ref; `merge-base --is-ancestor B.VerifiedSourceSha
<observer-ref>` must exit zero. Compare the observed SHA to B's saved confirmation.
Verify source directory, registration and local source ref removal independently, and
call `AssertRemoteSourceAsync` even on success. Pending/start/filter fields are cleared
and the queue claim released. A successful cleanup is allowed to remove the local source.

For refusal before rebase, require `Phase == Refused`, `Publication == Refused`,
`!HasPublication`, `Cleanup == NotStarted`, no verified/push/confirmation/deletion evidence,
and preserved source HEAD/ref/registration/content, target HEAD/ref and sentinel bytes.
The attempt must contain no rebase/abort, target merge or target `update-ref`, push,
worktree removal, branch deletion, clean/stash/reset, or operator-file repair. Recovery pin
`update-ref` and remote-observation fetches are permitted for a fresh attempt. Read remote
master independently and call `AssertRemoteSourceAsync`. A refusal's terminal event must
point at the correct active operation and clear pending request fields.

### Required behavior cases

All names below are required method names, so the positive-control filters are stable.
Parameterized rows must appear individually in the executed TRX.

| ID / method in `AgentTaskLandRefusedRetryTests` | Setup and action | Required result beyond the common oracles |
|---|---|---|
| V-1 `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation` | Run A against the real untracked target sentinel, yielding `target_dirty_or_unknown`. Remove only the sentinel. Capture source SHA/ref/path/common-dir and target SHA/ref immediately before requesting; all equal A's original values. Use actual `RequestAsync` with no filter, restart services, `SweepAsync`, dequeue and execute the persisted request. | Request/sweep preserve A's pointer and the exact request timestamp. B records S and A's target-before SHA. Observe a successful empty target status in B's attempt, fresh destination lookup and pins; observe rebase, target advance and push after their durable checkpoints. Final result is `Landed` with independently confirmed containment. With unchanged base and no filter, zero verifier calls and fresh `base_unchanged` evidence are valid and expected. Assert B's new ID immediately after execution, before filesystem checks that a bad implementation could invalidate. |
| V-2 `RR_V2_StillDirtyTargetCreatesFreshRefusal` | Keep the sentinel throughout. To make PC-3 reach B, inject `LandingGitResult(128, "", "fixture target status unavailable")` only for A's target-status command; assert the hook hit and A refused `target_dirty_or_unknown`. Remove the injection, leave the real sentinel, then explicitly request and execute B. | B is a new `Refused` operation, not a replay of A. Its real target status exits zero with the sentinel in nonempty output and its reason is `target_dirty_or_unknown`. Zero verifier calls and all refusal-preservation/forbidden-mutation assertions hold. Assert B's phase/reason before other checks; this is PC-3's intended failure. V-1 separately covers A's refusal from real dirtiness. |
| V-3 `RR_V3_TargetOnlyAdvanceRequiresFreshSelectedVerification` | After a real dirty-target refusal A, remove the sentinel, commit a distinct nonconflicting `target-repair.txt` on canonical master, and leave source identity S unchanged. Request `/*/*/RefusedRetryFixture/SelectedCheck` through the service. | B records the new target SHA and the exact requested filter, with fresh destination and checkout. Rebase targets that SHA. At `Verifier.Barrier`, a separate context sees active B in `Prepared` with fresh prepared pin, no verification/confirmation receipt, and source HEAD equal to `B.RebasedSourceSha`. Exactly one verifier invocation uses F.Source and that filter; B records passed verification with no skip reason. Independent remote ancestry includes both B's verified SHA and the target-only commit. A's old pins still resolve. |
| V-4a `RR_V4_TargetStatusFailureRetriesFromFreshEvidence(bool restoreStatus)`; arguments `true`/`false` | A's target status alone returns exit 128 (clean physical target); clear the injection for `true` or keep it for `false`. Retry identical source and target through `RequestAsync`. | Both rows allocate B. Restored status produces a fresh successful empty read and publication; continuing failure hits the target-specific injection during B and refuses again without mutation. No source-status injection is allowed. |
| V-4b `RR_V4_RemoteReadFailureRetriesUnchangedSource` | On A, inject exit 128 only at canonical-repository `ls-remote --refs --exit-code <F.Remote> <F.TargetRef>`. Assert A's `remote_read_failed`, no publication and unchanged S. Remove the fault and explicitly retry. | B has fresh `ls-remote`, fetch into B's remote-observation namespace and ancestry checks, reaches target validation, and publishes successfully. This prevents a dirty-reason-only exception from satisfying D-1. |
| V-4c `RR_V4_RemoteContainmentAfterRefusalKeepsAlreadyPresentShortcut` | Produce a real dirty-target refusal A. A separate fixture-owned Git client then pushes S to remote master; leave local target at its old SHA and leave sentinel bytes untouched. Request unchanged source again. | B independently confirms S as `AlreadyPresent`, skips verification for `exact_remote_containment` and completes guarded source cleanup. B's attempt has no target-status prerequisite, rebase, verifier call, target advance or push. Canonical target HEAD and sentinel bytes remain unchanged. This is the established remote-containment shortcut, not authority to repair the target. |
| V-5a `RR_V5_OriginalPendingRequestCannotReplaceRefusal` | Save A's initial request timestamp. After obtaining a real refused A, restore only task request fields to model a crash after refusal persistence but before pending settlement: original request timestamp, a started time and attempt 1. Remove target sentinel; no new `RequestAsync` call. Restart, sweep, dequeue and execute. | Independently assert the retained timestamp is strictly older than A.UpdatedAt, sweep keeps it unchanged, and accepted fresh source inspection actually occurs inside the protocol. Only A remains active; new terminal event points at A with the old refusal reason. No B pins, target status, rebase, target advance, push or cleanup; source/target/pins preserved. This is PC-2's exact method. |
| V-5b `RR_V5_EqualPendingTimestampCannotReplaceRefusal` | Same persisted recovery setup as V-5a, but set request timestamp to the freshly reloaded PostgreSQL value of A.UpdatedAt. Restart, sweep and execute. | Equality survives DB round-trip and is automatic, with the same no-replacement/no-mutation evidence as V-5a. A source-status trace proves the test did not stop at the service's null-pending return. |
| V-5c `RR_V5_SettledRefusalIsNotAutomaticallyQueued` | Normally settle A, confirm null pending fields, repair the target and restart. Call `SweepAsync` twice with no explicit request. | Queue `IsActive(F.TaskId)` is false and `TryDequeue` finds nothing. Task-scoped operation/event/pin snapshots do not change; no Git command occurs. Do not substitute a direct `RunAsync` no-op for this sweep test. |
| V-6a `RR_V6_PreparationFailurePreservesActiveRefusal(string boundary)`; arguments `"destination"`/`"target-commit"` | Begin with real dirty-target A, repair only the sentinel, and explicitly request unchanged S. Inject the preparation fault specified below, then remove it and issue another explicit request in a fresh graph. | The fault hit is mandatory. After failed preparation, independent reads show only A active, original pointer and evidence/pins intact, no committed B or B outcome. A-attributed refusal event records `landing_io_error` or `commit_lookup_failed` as appropriate. The next actual request still accepts identical S and commits successful B. |
| V-6b `RR_V6_ReplacementSaveFailureRollsBackOldDeactivation(bool afterSave)`; arguments `false`/`true` | Begin with unchanged-source eligible A. Inject on B's insertion save, before/after that save as specified below; await `InjectedSaveFailure`, inspect using a separate context before any settlement, then clear hooks, call `FailAsync` in a fresh scope, request again and retry. | Both fault cuts are reached after A's deactivation save is acknowledged. During the open transaction an independent context still sees A active. After unwind there is exactly one A, still active with its original concurrency token and task pointer, no B row/event/pins and all work preserved. Fresh `FailAsync` settles using A; the subsequent explicit request successfully commits B with unchanged S. |

There are **14 planned integration executions**: V-1/V-2/V-3 one each, V-4 four, V-5 three,
and V-6 four (3 + 4 + 3 + 4). These are planned counts, not run results. Require all 14
specified rows in the new class; name any additional coverage separately in the report.

### Exact hooks, transaction cuts and timestamp gates

Target-status hooks match a normalized `repository == F.Repository` and the complete
argument vector `["status", "--porcelain=v1", "-z", "--untracked-files=all",
"--ignore-submodules=none"]`. Match source status separately at `F.Source` to prove fresh
inspection. Never fault/count both paths just because `args[0] == "status"`.

V-6a destination fault matches `F.Repository` and
`["remote", "get-url", "--push", "--all", "origin"]`. Target-commit fault matches
`F.Repository` and `["rev-parse", "--verify", F.TargetRef + "^{commit}"]` during replacement
preparation, after accepted source inspection. Return exit 128 with a fixed fixture diagnostic.
These cuts precede `PersistNewOperationAsync`; assert the intended hook fires and no
new-prefix pin or dependent mutation starts. Clear hooks before independent observer reads.

For V-6b use the existing interceptor, armed only after A settles:

~~~csharp
h.Fault.Phase = null;
h.Fault.Matches = op => op.Id != previous.Id && op.Phase == LandPhase.Inspected;
h.Fault.AfterCommit = afterSave;
~~~

`AfterCommit` is the harness's name for `SavedChangesAsync`, **not** the enclosing
transaction's `CommitAsync`. With `false` this faults B's insertion/pointer save after the
earlier A-deactivation save; with `true` it faults after that second save but before the
transaction commit. Install a one-shot `Fault.AfterAcknowledged` observer for
`LandPhase.Refused` during this retry: it proves A's first save completed and verifies on
a separate connection that A remains visible as active. Do not inject by
`Phase == Refused` in `SavingChangesAsync`: that would fail before deactivation is written
and would not exercise rollback. Assert both the first-save observation and `Fault.Triggered`.
Do not reuse the failed tracked context for recovery.

V-5a/b deliberately restore task request columns to create a persisted crash window; they
must not call the public request wrapper, which would make the marker newer. Do not alter
A's phase, coordinates or evidence. Preserve a below-limit attempt value so sweep actually
enqueues and the protocol's `LandRequestedAt > op.UpdatedAt` comparison is reached.
V-5c alone covers the separate null-pending service/sweep behavior.

### V-7: state-policy matrix

Add `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission` to
`AgentTaskLandingStateTests`, using named data rows and a fresh previous operation and
inspection per row. Avoid a Cartesian product: begin with all D-1 conjuncts true and
change one admission condition at a time. Use these row groups:

| Row group | Data / expected eligibility |
|---|---|
| Unchanged identity, different refusal reasons | `target_dirty_or_unknown`, `remote_read_failed`, `verification_failed`, `interrupted_rebase_requires_inspection`, an unrecognized terminal reason and null; all true. |
| Changed identity remains accepted | Change source full ref, source SHA, target full ref, common directory or registered path individually in an otherwise accepted same-task snapshot; each true. Keep snapshot HEAD/branch/symbolic fields internally consistent. |
| Admission denied with otherwise valid unchanged identity | `explicitRequest=false`, `leaseHeld=false`, `SchemaVersion=999`, `LandSourceInspection(null,"active_sequencer")`, and accepted snapshot for a different task; each false. Repeat the automatic and missing-lease rows with changed source SHA to prove changed evidence cannot bypass either gate. |
| Phase denied | Each enum phase except `Refused`; false, including `Verified`, `TargetAdvanceStarted`, `LocalTargetAdvanced` and `PushStarted`. Do not infer phase from `LastReason` or `Publication`. |
| Existing publication | Two valid receipts, one `Landed` and one `AlreadyPresent`, with phase deliberately `Refused` so the publication conjunct itself is exercised; false. Assert `policy.HasPublication(previous) == true` before calling the replacement predicate. |
| Refusal status is not confirmed publication | Supported `Refused` operation with `Publication == Refused` but no receipt; true. |

The specified groups yield **32 new unit executions** (6 + 5 + 7 + 11 + 2 + 1).
Reuse the valid identity/verification construction in
`C448_V31_VerificationEvidenceMustDescribeTheExactCommit` for receipt rows: valid IDs/OIDs,
matching destination/target, 64-character fingerprint, exact recovery prefix, source/target
pins, `VerifiedSourceSha == OriginalSourceSha`, `VerifiedAt`, `VerificationPassed=true`,
`RemoteConfirmedAt`, observed remote SHA and
`ConfirmationMethod="push-endpoint-read-fetch-ancestry"`. A bare `Publication=Landed` flag
would make `HasPublication` false and would not test the intended guard.

Snapshot every scalar of the previous entity and the immutable inspection before the call
(e.g. serialize both with the same options); require exact equality afterwards for every row.
Retain the existing state tests, including the interrupted-rebase explicit/lease matrix.

### Regression set

Run every listed class sequentially, including all argument rows. These are R-n execution
obligations, not a claim that V-n alone covers all publication/recovery guards.

| ID | Classes and required existing evidence |
|---|---|
| R-1 | `AgentTaskLandRecoveryTests`: unsafe/automatic reposts, resolved interrupted rebase, failed replacement, unknown schema, real-worker death cuts C03/C05/C09/C12/C14/C16/C17 and acknowledgement gaps. Existing tests that call `RepostAsync` stay as compatibility coverage; V-5 is the stronger pending-timestamp regression. |
| R-2 | `AgentTaskLandPreparationIdentityTests`: `C448_V15_ExplicitRepostCannotReplaceTargetAdvanceIntent`, all changed-`Verified` preparation rows, missing rebase result, and protection against adopting another writer's preparation. |
| R-3 | `AgentTaskLandPublicationTests` and `AgentTaskLandCleanupSafetyTests`: independent remote proof, no cached/local shortcut for remote errors, `C448_V33_CleanupRetriesDoNotEmitAnotherPublication` and changed-work cleanup refusal. V-4c adds explicit target-dirty coverage of the already-contained shortcut. |
| R-4 | `AgentTaskLandRequestTests` and `AgentTaskLandSweepTests`: active-request conflict, queue/pointer semantics, null-pending no-op, interrupted request timestamps and attempt limit. |
| R-5 | `AgentTaskLandConcurrencyTests` and `AgentTaskLandingPersistenceTests`: writer/lease holds in every mode, source/target changes during verification, active-operation uniqueness and durable concurrency. |
| R-6 | `AgentTaskLandVerificationEvidenceTests` and `AgentTaskLandVerifierTests`: executed passing-test counters and the real selected-verifier/filter contract, complementing V-3's controlled verifier. |

### Positive controls and exact execution commands

Run controls separately because PC-1 and PC-2 change the same predicate and PC-3 interacts
with that protocol. Before each mutation retain an exact backup of the **fixed** file.
Restore it in `finally`, refresh its `LastWriteTimeUtc` and rebuild the same output before
green. Never restore with `git checkout` over unrelated task edits. Inspect the diff to
confirm the mutation is gone. A red run needs a successfully built test, the exact method
executed once and the expected behavior assertion failure; fixture errors, build failures,
timeouts and zero tests do not count.

| ID | Temporary mutation after implementing D-1 | Exact method / intended red |
|---|---|---|
| PC-1 | Restore only the original final reason/source-difference disjunction from `10cfbc63:server/Application/Services/AgentTaskLandingState.cs` to `CanReplaceRefused`; retain all D-1 conjuncts. | `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation`: B is still A, failing the new-operation-ID assertion after execution. Both the target repair and unchanged identity preconditions must already have passed. |
| PC-2 | Remove only `explicitRequest &&` from the fixed `CanReplaceRefused`. | `RR_V5_OriginalPendingRequestCannotReplaceRefusal`: the older pending request incorrectly creates B and fails the single-A/active-pointer assertion. Source inspection and original marker assertions must have passed. |
| PC-3 | In `CheckTargetAsync` change only `Require(status.Succeeded && status.Output.Length == 0, "target_dirty_or_unknown");` to `Require(status.Succeeded, "target_dirty_or_unknown");`. Keep status execution, success requirement and every other guard. | `RR_V2_StillDirtyTargetCreatesFreshRefusal`: A still refuses because its injected status fails; B sees real successful nonempty status and incorrectly crosses the refusal boundary. Expect the B `Refused` phase/reason assertion to fail, not A's setup. |

From the Code worktree root, use the following PowerShell runner helper. It gives each run
a unique TRX/evidence directory under ignored `.antiphon`, runs actual tests, rejects missing
or zero results and checks exact method execution for controls. Retain this run directory
until the Code report is consumed; do not delete alternate outputs by name or age.

~~~powershell
$retryRunRoot = Join-Path (Get-Location).Path ('.antiphon\refused-retry-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $retryRunRoot | Out-Null
function Invoke-RefusedRetryCheck {
    param(
        [string]$Id, [string]$Class, [string]$Method = '*',
        [ValidateSet('green','red')][string]$Expect = 'green'
    )
    $runId = $Id + '-' + $Expect + '-' + [guid]::NewGuid().ToString('N')
    $resultDir = Join-Path $retryRunRoot $runId
    New-Item -ItemType Directory -Path $resultDir | Out-Null
    $savedEvidence = $env:ANTIPHON_C448_EVIDENCE
    try {
        $env:ANTIPHON_C448_EVIDENCE = Join-Path $resultDir 'evidence'
        dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-refused-retry/ -- --treenode-filter "/*/*/$Class/$Method" --report-trx --report-trx-filename "$runId.trx" --results-directory $resultDir
        $runExit = $LASTEXITCODE
    }
    finally { $env:ANTIPHON_C448_EVIDENCE = $savedEvidence }
    $trxPath = Join-Path $resultDir ($runId + '.trx')
    if (-not (Test-Path -LiteralPath $trxPath)) { throw "Missing fresh TRX: $trxPath (exit $runExit)" }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', $trx.DocumentElement.NamespaceURI)
    $rows = @($trx.SelectNodes('//t:UnitTestResult', $ns))
    $counters = $trx.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
    if ($null -eq $counters -or [int]$counters.executed -eq 0 -or $rows.Count -eq 0) { throw 'No executed tests' }
    $methods = @($trx.SelectNodes('//t:TestDefinitions/t:UnitTest/t:TestMethod', $ns))
    if ($methods.Count -eq 0 -or @($methods | Where-Object { $_.className -notmatch ('(^|\.)' + [regex]::Escape($Class) + '$') }).Count) { throw 'Unexpected or missing class selection' }
    if ($Method -ne '*' -and ($rows.Count -ne 1 -or [int]$counters.executed -ne 1 -or @($methods | Where-Object { $_.name -ne $Method }).Count)) { throw 'Exact control method did not execute once' }
    if ($Expect -eq 'green' -and ($runExit -ne 0 -or @($rows | Where-Object { $_.outcome -ne 'Passed' }).Count)) { throw "Expected green: $trxPath" }
    if ($Expect -eq 'red' -and ($runExit -ne 2 -or [int]$counters.failed -ne 1 -or @($rows | Where-Object { $_.outcome -ne 'Failed' }).Count)) { throw "Expected one assertion failure: $trxPath" }
    $rows | Select-Object testName, outcome
    $trx.SelectNodes('//t:ErrorInfo/t:Message', $ns) | ForEach-Object { $_.InnerText }
    [pscustomobject]@{ Id=$Id; Expect=$Expect; Executed=$counters.executed; Failed=$counters.failed; Trx=$trxPath }
}
~~~

The helper's red exit/count check is necessary but insufficient: read the printed/TRX
`ErrorInfo` to confirm the intended assertion in the PC table. If the pinned runner emits
a different TRX path or failure exit convention, inspect its actual output and record the
adjustment; do not loosen method, count or assertion checks.

Run the two changed classes and regressions on fixed source:

~~~powershell
$retryClasses = @(
    'AgentTaskLandingStateTests', 'AgentTaskLandRefusedRetryTests',
    'AgentTaskLandRecoveryTests', 'AgentTaskLandPreparationIdentityTests',
    'AgentTaskLandPublicationTests', 'AgentTaskLandCleanupSafetyTests',
    'AgentTaskLandRequestTests', 'AgentTaskLandSweepTests',
    'AgentTaskLandConcurrencyTests', 'AgentTaskLandingPersistenceTests',
    'AgentTaskLandVerificationEvidenceTests', 'AgentTaskLandVerifierTests'
)
foreach ($retryClass in $retryClasses) {
    Invoke-RefusedRetryCheck -Id $retryClass -Class $retryClass
}
~~~

For each pair below, run red only with that PC's specified mutation present, restore the
fixed file, set its `LastWriteTimeUtc = [DateTime]::UtcNow`, then run green. These are six
separate method invocations, not a script that applies mutations automatically:

~~~powershell
Invoke-RefusedRetryCheck -Id PC-1 -Class AgentTaskLandRefusedRetryTests -Method RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation -Expect red
# Restore fixed AgentTaskLandingState.cs and refresh its timestamp.
Invoke-RefusedRetryCheck -Id PC-1 -Class AgentTaskLandRefusedRetryTests -Method RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation -Expect green

Invoke-RefusedRetryCheck -Id PC-2 -Class AgentTaskLandRefusedRetryTests -Method RR_V5_OriginalPendingRequestCannotReplaceRefusal -Expect red
# Restore fixed AgentTaskLandingState.cs and refresh its timestamp.
Invoke-RefusedRetryCheck -Id PC-2 -Class AgentTaskLandRefusedRetryTests -Method RR_V5_OriginalPendingRequestCannotReplaceRefusal -Expect green

Invoke-RefusedRetryCheck -Id PC-3 -Class AgentTaskLandRefusedRetryTests -Method RR_V2_StillDirtyTargetCreatesFreshRefusal -Expect red
# Restore fixed AgentTaskLandingProtocol.cs and refresh its timestamp.
Invoke-RefusedRetryCheck -Id PC-3 -Class AgentTaskLandRefusedRetryTests -Method RR_V2_StillDirtyTargetCreatesFreshRefusal -Expect green
~~~

No `--no-build` for restoration runs. Confirm the restored source was compiled into this
output; explicitly rebuild if incremental output freshness is uncertain. Run
`Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` sequentially if any unrelated work needs the
latter; this change needs neither that assembly nor a full-suite/nightly, browser/E2E,
shared-stack restart or live landing run.

Code's report must map every V-1..V-7, R-1..R-6 and PC-1..PC-3 to executed methods/argument
rows, nonzero executed counts, failures and fresh TRX/evidence paths. Require all 14 specified
new integration rows and 32 new policy rows; record existing regression counts from actual
TRX, not this document. Report expected PC reds separately from unexpected failures.
Record the test commit, exact mutation and intended assertion for each PC, restored-green
result, and final clean mutation diff. Add D-4's living-contract paragraph in
`docs/orchestration-loop.md` with the implementation. No decision remains for TestDesign:
hand off to Code for S-1 through S-3 and all verification above.

## Delivery and remaining work

This dispatch delivers only the committed plan. TestDesign finalizes the verification section;
Code implements S-1 through S-3 and executes the checks. The caller should land this plan branch
through the normal delegation landing mechanism before dispatching a stage that needs it.
Implementation, tests, landing and deployment have not been performed by Plan.
