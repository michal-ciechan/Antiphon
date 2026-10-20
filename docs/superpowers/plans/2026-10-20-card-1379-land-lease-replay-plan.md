# CARD-1379: release the repository lease during verification and replay lands in order

Date: 2026-10-20. Stage: Plan. Next: TestDesign, which remains a separate stage.
The checkpoint design below is the bounded handoff for that stage, not a claim
that its proposed tests exist or have run.

Inspected base: `44b3147c2c7c2164447fcd24d63cf277d2a3d7d7`.
Assigned branch: `feat/card-task-c7a9a51f`. Target: master only.
Read the full live CARD-1379 and CARD-1380 descriptions. No production behavior,
settings, card state, or running service was changed by this planning task.

The result must let an eligible Code/Review task fetch its start commit and cut
its own worktree while a land waits for a build slot or verifies. The prepared
land worktree, approval and publication checks must remain protected. After an
AppHost restart, committed eligible land requests must be considered in their
original submission order before newer work.

## Ground truth

Paths and symbols below refer to the inspected base, not a claim about the
currently running AppHost binary. Incident timings are the card's observations;
they were not reproduced in this Plan task.

| Card assumption or observation | What the code does | Consequence |
|---|---|---|
| The mutation lease covers the verification build and slot wait. | `AgentTaskLandService.RunEnsuredRequestAsync` holds `RepositoryLease` around `RunLeasedAsync`; `AgentTaskLandingProtocol.RunAsync` invokes the verifier in Prepared. `VerifyWithObserverAsync` waits for the build slot inside that call. | The premise is correct. Change the service/protocol phase boundary, including the slot wait, rather than merely shortening a timer. |
| The candidate could be built separately from the main checkout. | Schema 3 already prepares one detached, persistent `LandWorkspace` per common Git directory. Another land can reset that same worktree. | Keep exclusive ownership of the land workspace throughout verification. It is not a separate worktree per request. |
| Releasing `landing.lock` would be sufficient. | `RepositoryMutationLease.AcquireAsync` also calls `RepositoryChildJournal.HasUnfinishedAsync`. `LandingVerifier.Observer` creates an untagged journal before a build/test child. That journal fences acquisitions until acknowledged exit and drained output. | A narrow, typed compatibility rule is required. Disposing the file handle alone still blocks dispatch. |
| Holding a second land lock would require new infrastructure. | CARD-1380 S1 already added `ILandExecutionLease`, `LandExecutionLease` and `land-execution.lock`; the service takes it before the mutation lease. Commit `e4da91c654f8d52b3a2c6fe5bbe6c9698d2221b1` is in this base. | Reuse that lock and its acquisition order. Do not create a third lock for the land workspace. |
| CARD-1380 is only a prospective overlapping change. | Its live card is InProgress. The base already includes owned warm artifact generations and fresh results paths. The tracked [combined plan](2026-10-20-card-1380-faster-lands-plan.md) also proposes lease, fairness and replay work in S4–S7. The current Prepared path still has `base_unchanged`; that is not proof that the remaining skip proposals shipped. | Sequence this card's Code after overlapping CARD-1380 work. Reconcile the combined plan's remaining slices before dispatch; never commission both implementations. Preserve the actual landed verifier policy. |
| Yield cycles are unbounded. | `YieldToDispatchAsync` already uses `LandYieldToDispatchMaxSeconds`, default 90; 0 disables it. `RepositoryLeaseWaiters` retains the first yield until admission/terminal outcome. Admission ends that budget, so an interrupted admitted attempt gets a fresh one. | Preserve that cap and retry contract. The missing mechanism is a prompt, fair opportunity for an actual waiter to acquire. |
| A land yield wakes dispatch. | The registry records waiters but provides no acknowledged handoff. `AgentTaskDispatcherHostedService` waits for its timer and awaits a serial, potentially long `TickAsync`. | A wakeup and a bounded targeted pass are needed; a Held sentence is not progress evidence. |
| All dispatches need the repository lease. | `DispatchOneAsync` already exempts prepared remote mirrors and some prepared re-arms. Fresh worktree cuts, repair sources, SourceLanding and Interim checks still require it. `StartRefAvailability` fetches before task creation, with a shared 30-second lease/network budget. | Keep existing nonmutating exemptions. Permit only ordinary, distinct Worktree preflight through the verification window; preserve create-time deadlines. |
| Read caches stay valid if verification does not touch Git. | `BeginOperationScope` caches registrations/common/canonical paths; the service currently keeps that scope for the whole leased operation. Another dispatch can add a registration during the proposed gap. | End the scope before releasing the lease and start a new one after reacquisition. |
| Held requests replay FIFO. | `SweepAsync` loads pending `AgentTasks` without `OrderBy`; `AgentTaskLandQueue` is an arrival-ordered channel. Requests also enqueue directly after commit. The reader and sweep are separate hosted services. | Ordering only the sweep query does not close the startup/new-request race or the commit-before-wakeup gap. |
| `unknown (awaiting replay)` means a request was lost. | `AgentTaskLandMonitorService.DescribeQueue` prints that text whenever the current in-memory snapshot lacks the persisted request, including between Held attempts. | Fix replay/order; do not treat that text alone as proof of lost durable state. |
| The landing worker has no holder metadata. | Land acquisition already tags task/purpose; the singleton lease provider reports acquisition identity. There is no request/phase in its owner record, and another process is deliberately Unknown. | Keep truthful ownership. New phase correlation can use the verification coordinator's existing identity; broad owner projection changes are secondary. |
| A late notice can have its age text updated freely. | `LandNotificationPayload.Create` freezes Body and ContentDigest. Queue/receipt processing relies on those bytes. | Delivery-time age needs a separate envelope/receipt design. It is outside the cheap changes in this plan. |

## Decisions

### D-1 — Sequence after CARD-1380 and assign the overlap once

Code for this plan starts only after the currently commissioned CARD-1380 work in
the landing service/protocol/verifier has settled and its required predecessor
commits are available on master. The next stage records that exact predecessor
SHA. Do not change the assigned Plan branch's base or merge master into it.

This is the CARD-1379-specific replacement handoff for the combined plan's
unimplemented S4–S7, subject to that predecessor check. CARD-1380 retains ownership
of warm artifacts, build-skip policy, receipt reuse and
`AgentTaskLandService.VerifyUnderSlotAsync`. If any S4–S7 behavior has already
landed by TestDesign, retain its evidence and narrow this plan to the missing
behavior before commissioning Code. Do not reimplement a completed slice.

Reason: the protocol's Prepared block overlaps even if edits avoid the static
verifier helper. Reject simultaneous Code tasks in this area and a textual
"disjoint files" claim that ignores their shared lifecycle.

### D-2 — Two lock lifetimes, with a closed verification window

Use the existing land-execution lock **L** for the entire land attempt, including
the candidate worktree and artifact generation. Use repository mutation lease
**R** for preparation and again for publication/cleanup. Build slot **B** is held
only by the existing verifier. Lock order is L then R; no caller holding R waits
to acquire L.

Introduce a verification-window coordinator with a typed acquisition capability.
While both L and R are held, it records the common directory, execution owner
process/start identity and acquisition nonce, request/attempt/operation,
candidate SHA, protected worktree/admin paths and window phase. Persist its
recovery marker atomically outside the land worktree. An active compatibility
grant also requires the current process's live coordinator capability bound to
L: a marker or a purpose string alone grants no authority.

The marker must not create a permanent recovery deadlock. A well-formed marker
whose exact owner process/start identity is conclusively dead is inactive, while
every unfinished child journal still fences as today. A new land may supersede
that inactive marker only after acquiring L and R through normal admission.
Malformed markers or unknown owner liveness remain a named refusal for explicit
inspection. Normal close removes only the exact marker generation it created;
an old close cannot remove a replacement. Never infer safe child exit from the
marker owner's death.

An ordinary mutation acquisition stays refused while the window is open,
including before any verifier child exists and between build and test children.
Only the typed preflight path in D-3 may acquire R. A second land cannot acquire
L or reset the workspace/artifacts. The window is closed to new preflights before
publication reacquisition. Current holders finish and release R normally.

Reject adding `land-verify` to the unconditional `LiveNonFencingPurposes` set,
temporarily deleting journals, using a boolean on an advisory owner tag as
authority, or allowing every repository writer during verification.

### D-3 — Admit only a bounded, distinct-worktree preflight

The two admitted operations are:

1. `StartRefAvailability` fetching a full origin commit before task creation.
   It retains its existing deadline and owned-child handling.
2. Ordinary Worktree dispatch creating/claiming a distinct managed task worktree.
   Check the fresh task/source mode, intended branch and destination paths, and
   reject any overlap with the land workspace, source worktree or protected refs.
   Run normal concurrency, eligibility, reservation and transactional claim
   checks; the capability cannot bypass them.

Shared writers, repair/adoption, SourceLanding, Interim, parked-source repair,
settlement synchronization, cleanup/pruning and canonical resets use ordinary R
admission. They do not gain this exception. Review is eligible only when its
actual workspace/source operation meets the same ordinary Worktree conditions;
its role name is not authority.

Keep R through the worktree mutation and committed claim. Release it before
remote mirror preparation or session startup that cannot mutate the protected
checkout. Retain current prepared-mirror/re-arm bypasses.

The verifier observer binds each child journal to the window identity. Admission
ignores only a matching live verifier record for this typed preflight. Unknown
start intents, malformed records, unrelated records, a dead/reused/unprovable PID,
and a completed root whose streams/descendants are not joined still fence.
An external process without the live capability remains refused. Ordinary
acquisitions continue to see all unfinished verifier records.

Reason: fetching objects and cutting a separate managed worktree can coexist with
the candidate build; resetting or writing the candidate/source cannot. Reject a
generic `dispatch` exemption because that call site also handles incompatible
source modes.

### D-4 — Make verification an explicit protocol continuation

Refactor the protocol into leased prepare and leased resume/publication
boundaries, with an immutable prepared-verification ticket between them. The
ticket binds request/attempt/operation, approval/filter, repository/destination,
candidate SHA and land-worktree identity. It is execution input, not publication
authority. Existing persisted phases remain authoritative.

The service performs this sequence:

1. Hold L and R; perform current admission, source resolution, pinning and rebase.
   Persist Prepared and the verification intent. If the CARD-1380 policy skips
   execution, preserve that path; no verification window is needed.
2. Create the window; dispose the Git operation scope, then release R. No
   transaction or database row lock spans the following wait/build.
3. Wake dispatch. Hold L while invoking the existing verifier, including its
   artifact preparation, slot wait, build and selected tests. Keep existing
   build-slot accounting, result custody and generation-completion semantics.
4. Await child exit and both output drains. The owning invocation closes its
   journal. Release B before reacquiring R. Close the window to new preflights;
   an already admitted preflight may finish. Reacquisition is cancellation-aware
   and must not count repeated polls as new land attempts.
5. Acquire a new genuine R and a new Git operation scope. Reload task, request,
   operation and approval evidence. Check request/attempt and active-operation
   identity, eligibility, filter, destination, source/ref and recovery pins,
   land registration/admin identity, candidate HEAD and tracked cleanliness,
   index/sequencer state, and newly admitted source/repository writers.
   Re-run the existing fresh remote-source and target observations.
6. Only a passing verification result for the unchanged ticket may become
   Verified and proceed through the existing push CAS/remote confirmation and
   guarded cleanup. A target movement uses the existing bounded
   `LandTargetRaceRetries` path: prepare a new candidate and reevaluate its
   verification policy. Never reuse the old green as proof of the new candidate.

Reject disposing a lease hidden inside a callback while its callers still hold
an apparently valid lease object or read-cache scope. The resumed protocol must
receive the new R explicitly. Preserve aggregate Git/phase timing across the two
scopes rather than silently omitting the post-verification work.

Cancellation or failure closes admission, joins owned children, and releases
owned resources; it cannot publish. If custody is uncertain, retain the journal
and recovery marker. On restart the old process capability is invalid, so stale
markers never enable the exception. An interrupted Prepared operation re-verifies
after normal journal recovery; a completed artifact generation is not a durable
publication receipt. Existing PushStarted/publication recovery stays unchanged.

### D-5 — Bounded dispatch priority with an actual acquisition witness

Keep the configured admission-yield cap, default 90 seconds, and the existing
fresh budget after an admitted attempt is interrupted. Do not reset it at every
sweep or when moving between preparation and verification in one attempt.

Add a coalesced dispatcher wake signal when a verification window opens and when
a mutation holder releases R with a ready waiter. A serial dispatcher services a
bounded targeted pass at the next safe boundary between tasks/sweep components,
as well as when idle. It uses the normal admission/claim path and then returns to
ordinary work; it never runs two `TickAsync` calls concurrently.

Give the oldest ready, compatible waiter one turn before a later land's R
acquisition. That turn ends on acquisition/acknowledgment, loss of eligibility,
cancellation, or the existing yield-budget expiry. Apply the turn at lease
admission, not merely by changing queue text. Pre-create start-ref callers use
ephemeral waiter identity and their shorter existing deadline; they have no task
row to invent. Cap a targeted pass at eight attempts. A stale/unacknowledged turn
must not starve a land.

An incompatible settlement waiter cannot use the verification exception, but it
retains its normal post-release opportunity. A current Git command can delay the
next safe boundary; no hard dispatch latency SLA is asserted. Reject shorter
poll intervals, longer sleeps, unlimited dispatch preference and timer-only
retry as substitutes for the handoff.

### D-6 — FIFO means oldest committed eligible request at a selection boundary

Use `(AgentTaskLandRequest.RequestedAt, Id)` as the stable ordering key. Keep the
one global land reader. No ordering migration or second worker is needed.
RequestedAt is the original durable time, never replay/dequeue time. Equal times
use the ordinal lowercase `Id.ToString("N")` ordering, matched by the database
selection; do not depend on an incidental collection or database physical order.
Normalize legacy task-only pending rows through the existing
`EnsureRequestAsync` behavior before sorting, preserving `LandRequestedAt`.

The reader starts behind a replay barrier. A successful complete reconciliation
of pending durable requests populates an ordered queue and opens it; a failed
scan leaves the barrier closed, logs the failure and retries at the existing
bounded sweep interval. New requests can still commit and wake reconciliation
during startup, but cannot start ahead of unreplayed older requests.

Route production admission, cleanup-retry and sweep wakeups through this one
reconciliation path. Before each next selection, merge a fresh complete durable
pending snapshot into the ordered queue. This catches an older request committed
before its enqueue was dropped/delayed. A wakeup is only a hint. Preserve the
current task/request row checks immediately before execution. Requests that
commit after a selection do not preempt an already executing land.

For Held requests, make one eligibility attempt per sweep generation, in original
order. After an actual hold, defer that request until the next generation rather
than spinning on the head. Eligible later requests, including another
repository, may proceed. At its next generation the older request gets its
original ordering key again. NeedsResolution, terminal, superseded and
ineligible requests are not executable; existing max-interruption handling and
reviewed-recovery eligibility remain in force.

Keep request-aware deduplication and the entry identity that prevents an old
completion releasing a newer claim. Queue snapshots/positions must reflect the
same ready ordering the reader consumes. Unknown during reconciliation is
truthful; inventing a position or a live holder from stale Running rows is not.

Reject sorting only the current task query, putting all Held requests at the
tail, a timestamp rewrite on requeue, and strict head-of-line blocking behind an
external writer. This is FIFO admission among currently eligible committed
requests, not a promise that a conflicted older land publishes first.

### D-7 — Keep secondary diagnostics within the core change

The new coordinator already has request/operation/attempt and phase. Include
those in its bounded phase/release/reacquisition log lines and correlate them
with existing land profile and BUILD SLOT lines. Report R as released during
slot wait/verification; L remains owned. Never name a land as the current R
holder while a dispatch owns it.

Keep existing tagged task/purpose and Unknown semantics. A richer cross-process
holder/status projection and request/phase DTO changes are deferred; the base
already tags a local land, so the incident's Unknown additionally needs the
loaded build and exact acquisition path checked before promising a fix.

Late-notice rendering is also deferred. Static wording alone does not supply the
card's requested age at delivery, while changing frozen notification bytes would
expand this work into delivery persistence and receipt semantics. Do not add the
combined plan's S8/S9 to this Code manifest. CARD-1379's secondary observations
remain explicitly outstanding rather than being reported fixed by the core work.

### D-8 — Master only; lane selection remains live

Read `GET /api/runner-defaults` and `GET /api/session-runners` during planning at
2026-10-20 13:05 UTC. Revision 2 supplied an available Linux default; the catalogue
also had eligible Windows capacity. This is a placement observation, not a host
pin. Refresh both routes at dispatch. Omit `-Runner`; omit `-Platform` for the
ordinary lane. Only the native Windows checkpoint requires `-Platform Windows`.
Use `-Platform Any` if an inherited OS pin must be removed. No fleet path or
hostname is part of the implementation.

Master publication does not activate AppHost. RC adoption and subsequent
activation require the operator's go under `docs/release-gates.md`; this task
neither adopts into the current RC line nor restarts services.

## Implementation slices

Each slice has a test-only R commit followed by its implementation commit,
both pushed on that Code task's assigned branch. Retain the actual red outcome.
S1's additive interface/test plumbing may compile while forwarding to today's
refusal behavior; it must not silently implement the acceptance before the red.
Do not dispatch concurrent slices touching this area.

| Slice | Concrete files and boundary | Tests / completion |
|---|---|---|
| S1 — compatible preflight while preserving exclusion | Extend `server/Application/Interfaces/IRepositoryMutationLease.cs` and `ILandExecutionLease.cs`; add a typed verification-window I/O contract and concrete coordinator under `Application/Interfaces/` and `Infrastructure/Git/`. Update `RepositoryMutationLease.cs`, `RepositoryChildJournal.cs`, `LandExecutionLease.cs`, `LandingVerifier.cs` observer/correlation, and registration in `server/Program.cs`/the existing worktree test graph. Add only the observer plumbing needed to bind child identity; preserve `VerifyUnderSlotAsync` and artifact policy. | New `tests/Antiphon.Tests/Infrastructure/LandVerificationWindowTests.cs`: V-1. No production R release yet. Add the native test helper through the existing owned-process fixture pattern. |
| S2 — release and safely resume the land | Change `server/Application/Services/AgentTaskLandService.cs`, `AgentTaskLandingProtocol.cs` and the prepared continuation DTO/result. Integrate the typed callers in `StartRefAvailability.cs`, `AgentTaskDispatcher.cs` and `DelegationWorktreeService.cs`; preserve their exclusions and release R after the committed ordinary claim. End/recreate operation scopes at the service boundary; change `LandingGit.cs` only if a small profile aggregation seam is needed. | New `tests/Antiphon.Tests/Application/LandVerificationLeaseWindowTests.cs`: V-2. Reuse `LandingSafetyHarness.cs`, `LandingGitFixture.cs`, `ControlledLandingGit.cs` and real PostgreSQL schemas. Update `docs/orchestration-loop.md` lease invariants and `docs/testing-and-build.md` verifier-lifetime description. |
| S3 — wake and service a ready waiter | Change `RepositoryLeaseWaiters.cs`, `AgentTaskDispatcher.cs`, `AgentTaskDispatcherHostedService.cs` and the acquisition/window release sites; add one DI-owned coalesced wake/turn coordinator. Preserve admission-yield settings and start-ref deadline. | New `tests/Antiphon.Tests/Application/LandDispatchHandoffTests.cs`: V-3. Use existing dispatch fixtures and fake clock. Update `docs/ops-http.md` yield/handoff semantics. |
| S4 — reconcile and replay in durable order | Change `AgentTaskLandQueue.cs`, `AgentTaskLandService.cs` request/sweep entry points, `AgentTaskLandHostedService.cs` and `AgentTaskLandSweepHostedService.cs`. Add a concrete ordered-reconciliation helper if needed; keep durable authority in existing request rows and retain the existing entry-identity guarantees. | New `tests/Antiphon.Tests/Application/LandRestartReplayTests.cs`: V-4. Reuse isolated-schema seed patterns from `AgentTaskLandSweepTests.cs` and identity assertions from `AgentTaskLandQueueVisibilityTests.cs`; place the changed production-path assertions in the selected V-4 roster. Update the queue/restart sections of `docs/orchestration-loop.md` and `docs/ops-http.md`. |

S1 and S2 form one safety dependency: S1 alone leaves the old continuous R
lifetime. S2 cannot ship without S1. S3 closes the progress requirement; S4 closes
restart order. Existing combined-plan S4–S7 numbers are not these slice numbers.
New path names are design names; TestDesign must pin their final names/rosters
before Code. Do not edit generated `docs/cards/` files.

## Verification design

TestDesign is separately commissioned by this brief. Its deliverable finalizes
the following small set of behavioral witnesses and checkpoint selections after
checking the actual CARD-1380 predecessor. Do not expand to whole Unit, a full
assembly, all landing tests, or the old combined plan's unrelated checkpoint
rows. The planned ordinary floor is 18 nonparameterized test executions across
four new focused classes, plus three native Windows executions. Internal matrix
assertions are not extra TUnit executions.

Use real repository leases, scratch repositories/origins and PostgreSQL
persistence where the behavior depends on them. A fake compiler can keep these
tests quick, but the verifier/journal tests must start and join an owned,
barrier-controlled child through the production observer. Faking away the
journal would miss the central fence bug. Advance the target or force an explicit
verification filter so CARD-1380's skip policy cannot accidentally bypass the
boundary being tested. No production runner/session or live repository is used.

| ID | Red witness: proposed exact method | Narrow green roster and observable obligations |
|---|---|---|
| V-1 / S1 | `LandVerificationWindowTests.C1379_LiveVerifierAdmitsOnlyScopedPreflight` | Four tests: witness; nonmatching/unknown/start-intent/dead/completed child evidence fences; another provider/land cannot acquire L or reset the protected paths; close/cancel/restart invalidates compatibility and retains uncertain custody. Use real independent lease providers and actual held handles. A tag-only ordinary acquire must remain refused. |
| V-2 / S2 | `LandVerificationLeaseWindowTests.C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | Six tests: witness pauses at both slot wait and live child, completes actual start-ref fetch/worktree creation before release, and verifies B is released before R reacquisition; target movement requires a new candidate/result; changed source/approval/filter/candidate refuses; new registration invalidates old cached cleanup assumptions; failed/canceled verifier cannot push and releases owned resources; compatible Code/Review claims retain caps while incompatible modes stay refused. Include L/artifact protection and prepared-mirror bypass in these scenarios, without rerunning CARD-1380's full cache suite. |
| V-3 / S3 | `LandDispatchHandoffTests.C1379_ReadyWaiterAcquiresBeforeNextLand` | Four tests: witness records actual worktree/claim before a later land; idle and in-tick signals run at the next safe boundary without a parallel tick; expired/canceled/ineligible turns release priority and retain the 90-second/zero-disabled contract; coalesced signals respect eight-attempt bound, claim uniqueness and normal caps. Fake-clock barriers replace long sleeps. |
| V-4 / S4 | `LandRestartReplayTests.C1379_OldHeldReplaysBeforeConcurrentNewRequest` | Four tests: witness seeds Held/Queued/interrupted Running in reverse insertion order, creates a new request during blocked startup replay and checks execution/positions by original time with a tied-ID pair; failed replay admits nothing until successful retry; duplicate/stale/superseded identities and dropped post-commit wakeup preserve durable selection; a genuinely Held head is attempted once per generation, later eligible work progresses, and reeligibility restores original age. Observe actual service attempts and persisted request/attempt clocks, not just a sorted helper list. |

The Windows native class in
`tests/Antiphon.Tests/Infrastructure/LandVerificationWindowNativeTests.cs`
contains exactly three bounded tests: `C1379_PreflightUsesARealReleasedMutationHandle`,
`C1379_ExecutionHandleExcludesASecondLand`, and
`C1379_ExitedRootWithUnjoinedOutputStillFences`. These qualify Windows file
sharing and process/output custody. They cannot silently skip on the nominated
Windows lane. Ordinary tests also use real locks on the default lane.

Reuse production seams and independently observe filesystem/ref/DB outcomes.
Where a new API is needed, introduce compiling inert plumbing for the test-only
commit. A compile error, fixture error, timeout or zero selection is not the
required red assertion. Every red witness must fail against the preceding
production behavior at its stated acceptance assertion, then run again in the
green selection. If it is already green after CARD-1380, report inherited
coverage and amend the remaining scope before Code rather than manufacturing red.

No caller-notification producer, payload, receipt or persistence route changes
in this plan. For internal wakeups, TestDesign explicitly traces request commit
to reconciliation to execution and window release to waiter acquisition. Loss of
a wakeup is recovered by the existing periodic sweep; it grants no mutation
authority. These are service-consumption witnesses, not UserPrompt receipts.

### Execution contract

R suffixes in the table mean the committed test-only predecessor. Each red row
is one selected method and each green row is one focused class of the size above.
The `C1379_` method prefix bounds the filter even if another card later adds tests
to that class. All rows build isolated output for their exact committed source;
no output reuse crosses commits. Group names identify lanes, not hosts.

Use the checkpoint tool `run --plan <this-plan> --rows CP-n
--expected-source-sha <full-sha>` for the selected committed slice, followed by
`wait --run <run-id>` until terminal. Exit 75 means keep waiting, not completion.
Bootstrap builds of `tools/Antiphon.Checkpoints` go through
`scripts/build-slot.ps1` into an owned alternate output; run the already-built
tool for orchestration, whose row drivers take their own slots. Record that
one-time tool bootstrap separately if needed. Do not hold an outer build slot
across the checkpoint executor's independently gated rows.

Run one red/green row at a time. Report the unedited CHECKPOINT line, full source
SHA, counts, expected red assertion and slot/build-source evidence. Slot timeout
is not permission for an unleased retry. Stop editing while a row is in flight.
Remove only proven task-owned alternate outputs after completion; generated
TRX/JSON/log evidence stays ignored. Code/Review also run
`scripts/check-evidence-diff.ps1` over their recorded base..pushed-tip history.

The estimates total 54 minutes across separate committed slices and lanes, not
one dispatch or a measured runtime. TestDesign may refine measured estimates and
expanded counts; adding coverage requires a named invariant, not a whole-Unit
substitute. Ordinary independent Review remains required. Preserve any inherited
red separately and verify it with the exact failing method at the predecessor.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1R | `tests/Antiphon.Tests -> bin-c1379-window-red/` | any-window-red | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | V-1 red | exactly 1 executed; expected preflight assertion failure | 1 | 5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1379-window-green/` | any-window-green | `/*/*/LandVerificationWindowTests/C1379_*` | V-1 | all 4 listed, 0 failed/skipped | 4 | 6 |
| CP-3 | S2R | `tests/Antiphon.Tests -> bin-c1379-release-red/` | any-release-red | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | V-2 red | exactly 1 executed; expected dispatch-before-release assertion failure | 1 | 5 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1379-release-green/` | any-release-green | `/*/*/LandVerificationLeaseWindowTests/C1379_*` | V-2 | all 6 listed, 0 failed/skipped | 6 | 8 |
| CP-5 | S3R | `tests/Antiphon.Tests -> bin-c1379-handoff-red/` | any-handoff-red | `/*/*/LandDispatchHandoffTests/C1379_ReadyWaiterAcquiresBeforeNextLand` | V-3 red | exactly 1 executed; expected acquisition-order assertion failure | 1 | 5 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1379-handoff-green/` | any-handoff-green | `/*/*/LandDispatchHandoffTests/C1379_*` | V-3 | all 4 listed, 0 failed/skipped | 4 | 6 |
| CP-7 | S4R | `tests/Antiphon.Tests -> bin-c1379-replay-red/` | any-replay-red | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest` | V-4 red | exactly 1 executed; expected replay-order assertion failure | 1 | 5 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c1379-replay-green/` | any-replay-green | `/*/*/LandRestartReplayTests/C1379_*` | V-4 | all 4 listed, 0 failed/skipped | 4 | 6 |
| CP-9 | S2 | `tests/Antiphon.Tests -> bin-c1379-native-windows/` | windows-native-lease | `/*/*/LandVerificationWindowNativeTests/C1379_*` | V-1, V-2 native | all 3 listed on Windows, 0 failed/skipped | 3 | 8 |

## Completion, activation and remaining evidence

TestDesign checks the predecessor and commits the finalized names/rosters and
any necessary fixture seams in the plan before handing off Code. The ordinary
proof is actual compatible dispatch during blocked verification, preserved
publication/custody guards, bounded fair acquisition, and durable restart order.
Do not report this plan as an executed verification result.

After Code/Review, land on master through the normal pipeline. Fetch
`origin/master` and check `git merge-tree --write-tree origin/master HEAD`
without merging into the task branch. Record full pushed SHAs and the merge-tree
outcome. The caller owns subsequent RC adoption and activation after the
operator's explicit go.

After authorized activation, measure a normal batch, keyed by request/operation:
R-held intervals, slot wait/verification interval, waiter-ready to actual
acquisition, request accepted to admitted, and replay selection order around an
authorized restart. Correlate loaded `/api/version` SHA with the implementation
before interpreting owner-unknown evidence. A passing health check or no active
tasks alone does not establish which lease/phase caused a wait. A synthetic
paused verifier proves overlap, not real throughput or a fixed latency SLA.

If dispatch still waits, separate compatible admission, an unfinished child
fence, an incompatible task mode, dispatcher safe-boundary delay and host/build
capacity before changing another timeout. Opaque-holder UI enrichment and
delivery-time age remain the secondary CARD-1379 follow-up scope in D-7.

This plan creates no agent session that waits for input and changes no parking
policy. Server build/lease waits are cancellation-aware operation waits. Existing
sessions waiting for an answer retain their seat while parking is disabled;
there is no automatic release deadline (CARD-1083).
