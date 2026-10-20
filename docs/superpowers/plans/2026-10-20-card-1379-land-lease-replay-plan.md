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


### Inspection

TestDesign, 2026-10-20: inspected assigned start
`a01634a785c269ec79574572df944184edb13828`. A fresh fetch observed master at
`44b3147c2c7c2164447fcd24d63cf277d2a3d7d7`; the three overlapping service/protocol/verifier
files have no delta from the assigned start. CARD-1380 S1 is present at
`e4da91c654f8d52b3a2c6fe5bbe6c9698d2221b1`. S2 is **not** present in this observed
master. Code commissioning must wait for S2 to settle, record its full published
SHA and read its `VerifyUnderSlotAsync`/Prepared delta before implementing this
plan. This is a sequencing prerequisite, not authorization for parallel Code or
for rewriting the build-skip policy. No runtime was queried or restarted.

Bodies inspected (including fixture setup, callbacks and cleanup, not names alone):

- `Infrastructure/RepositoryMutationLeaseTests.cs`: common-directory and independent-provider exclusion, unreadable/start/PID journals, canceled owned Git, Windows junction/other-process handle, root-exit/output-descendant bodies; `server/Infrastructure/Git/{RepositoryMutationLease,RepositoryChildJournal,LandExecutionLease,LandingVerifier}.cs` -> V-1/R-1 and native qualification.
- `Application/LandVerificationBuildSlotTests.cs`: fake runner, grant/release ordering, busy deadline and fresh report fixture; `TestHelpers/LandingSafetyHarness.cs`: graph, request/run/restart paths, transaction faults and controlled verifier; `LandingGitFixture.cs`: scratch origin, independent clone, command hooks and cleanup; service `RunEnsuredRequestAsync`, `RunLeasedAsync`, `VerifyWithObserverAsync`, `VerifyUnderSlotAsync`, `RunProcessAsync` -> V-2/R-2. The default controlled verifier does not exercise a child journal.
- `Application/AgentTaskLandDispatchYieldTests.cs`: actual Held/admission, fake-clock budget, interrupted retry and terminal budget cleanup; `Application/WorktreeStartRefDispatchTests.cs`: origin-only commit create/dispatch and incompatible modes; `TestHelpers/RepairSourceWorld.cs`: create, service graph and committed dispatch observation; `DelegationTestServices.cs` worktree graph; production `StartRefAvailability`, `RepositoryLeaseWaiters` and dispatcher hosted loop -> V-2/V-3, R-2/R-3.
- `Application/AgentTaskLandSweepTests.cs`: isolated schema, pending/restart/max-attempt seeds and hosted failure; `AgentTaskLandQueueVisibilityTests.cs`: snapshot/read order and old-entry completion; production queue, land reader, sweep host and `SweepAsync` -> V-4/R-4. Existing sweep assertions stop at enqueue and are insufficient as delivery evidence.
- `TestHelpers/LandQueueRaceWorker.cs`, `TestWorkerModes.cs`, landing crash-worker setup and the native process bodies above -> nearest fixtures for new owned worker/control files. `ControlledLandingGit.cs` constructor, observation hooks and real-file/model boundary were inspected; its fabricated process IDs/model refs are excluded as native, journal-liveness or real-Git proof.
- Owners read: `docs/project-context.md`, relevant pipeline/landing and D-2 sections of `docs/orchestration-loop.md`, and checkpoint schema, coverage binding, slot, Mutation and delivery sections of `docs/testing-and-build.md`. No test/helper/source file changes in TestDesign.

Paths without a prefix in this inventory are below `tests/Antiphon.Tests/`.

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


### Delivery inventory

All new asynchronous paths end at an internal service recipient. They do not
change caller notification payloads or session input. A queue insert, wake flag,
Held event, acknowledgment or request row alone is never receipt.

| Path / producer -> destination | Persistence and identity | Recovery cuts and recipient evidence |
|---|---|---|
| Land request acceptance, cleanup-retry acceptance and sweep -> ordered reconciliation -> real `AgentTaskLandQueue` -> `AgentTaskLandHostedService` / `RunRequestAsync` | Existing committed task/request rows; TaskId + RequestId + original RequestedAt, then attempt + operation; EntryId protects only in-process completion | Q1–Q4 traverse producer and real reader with busy and already eligible recipients. Q3 cuts after commit/before wake, refused enqueue, after snapshot/before selection, after dequeue/before execution and after committed attempt/before protocol work. Crash loses all memory; fresh provider scans same schema. Receipt is separate-connection committed attempt joined to that request AND actual protocol/candidate entry, followed by independently observed Git outcome or named hold. Cleanup-only uses the published operation identity and observes actual cleanup-service consumption. |
| Window open and R release with waiter -> coalesced wake/priority turn -> serial hosted dispatcher -> normal worktree claim | Durable queued AgentTask/attempt and workspace reservation; common directory + task ID join waiter. Window marker binds request/attempt/operation/nonce but cannot authorize alone | H1/H2/H4 cover idle, already eligible and in-tick busy recipient, dropped/coalesced wake, release-before-signal, signal-before-consume and restart-before-consume. Periodic tick rediscovers durable task and recreates ephemeral waiter. Receipt is actual R acquisition, real checkout at expected SHA and separately committed claim. H3 covers stale-turn recovery. |
| Prepared land -> verification outside R -> resumed protocol | Committed Prepared operation + immutable ticket + exact window generation; journal retains child identity; artifacts are not durable approval | W4/L5 cut before/after atomic marker publication, before child start, after start, after child join, before reacquire and before Verified persistence. Fresh providers plus owned-worker death arms prove no stale capability and re-verification. Receipt is fresh leased protocol validation and independently confirmed publication for the same candidate, or a durable refusal with protected refs retained. |
| Precreate StartRefAvailability -> typed preflight -> bounded fetch caller | Synchronous request; ephemeral invocation nonce + common directory + requested SHA; **no durable task exists yet** | L1/H3 prove local object arrival before create completes, cancellation/deadline leaves no task. A server crash loses the HTTP call; a caller retry starts a new bounded request. No promise of background replay of an uncommitted create. |

Each recovery guard is mapped below, including failed replay barrier, dropped wake
selection refresh, stale completion, once-per-generation deferral and cancellation.
Owned-worker crash cuts use parent-owned database/repositories, fresh graph and
real queue, not an exception caught by the same running reader. Exception-only
cuts qualify error handling but cannot stand in for these process-death arms.

Substitutes: fake slot gate supplies deterministic contention, not host scheduler
qualification; an owned tiny child stands in for the compiler/test executable,
not a real build benchmark; fake clock proves deadline/turn decisions, not latency;
isolated launch sink after dispatch commit proves no terminal delivery. Real
queue/DB/Git/leases/production verifier observer remain in these paths. No session
receipt is asserted; any Code expansion into changed session delivery requires a
matching **complete UserPrompt transcript** joined to the queued identity and
payload before that expansion is reviewable.

### Proves it works now

The following final roster refines the proposed roster above: 18 nonparameterized
ordinary methods plus three Windows methods, no additional broad selection.
W/L/H/Q/N aliases below are only document cross-references. Code implements every
named variant inside its method with labeled assertions. The PC table adds exact
variant obligations to its detecting method, including those abbreviated in the
roster; loops do not increase
`Min`. Each variant starts from an independent valid fixture and records its
reachability barrier before asserting the outcome. A missing barrier, compile
failure, fixture failure, skip or timeout is incomplete evidence, never expected
red. All new process-spawning classes carry this assembly's
`ParallelLimiter<ProcessSpawnLimit>` and isolated schema per case.

Fixture recipe: add `tests/Antiphon.Tests/TestHelpers/LandVerificationWindowHarness.cs`
and `LandVerificationWindowWorker.cs`, following the inspected landing/dispatch
and worker patterns. Use `LandingSafetyHarness.ConfigureServices` to substitute
the **production** `LandingVerifier` with its `RunProcess` delegate translating
compiler argv to a tiny owned helper. Expose the existing `RunProcessAsync` to
internal tests, without changing its algorithm, so that delegate still executes
the production start/journal/exit/both-drain path. Never manually fabricate the
observer's success calls. Worker modes join `TestWorkerModes.cs` before DB warmup.
Build the new dispatch graph through `DelegationTestServices.cs`; do not change
`RepairSourceWorld`'s existing assertions. Held/scan hooks observe or fault I/O;
they cannot return authorization, bypass claim logic, or implement sorting in the
fixture. New typed interfaces may initially forward to current refusal for red.

All fixture files are beneath the test project; worker launch is local, inherited
and bounded. Retain native child handles independently; nonce-check readiness,
join root/descendants/output in finally before removing scratch. Crash markers
must be outside the killed worker's disposable tree. New fake-clock injection
must wrap the real wait/deadline calculation, not replace its result.

| V / layer / command | Exact red and green obligation | Witness source footprint |
|---|---|---|
| V-1 infrastructure, CP-1 then CP-2 | W1 red at compatible live-child lease being absent; green W1–W4, every listed variant | New `Infrastructure/LandVerificationWindowTests.cs`, new harness/worker; production lease/journal/coordinator, L contract, verifier observer and DI S1 files. Shared test changes limited to `TestWorkerModes.cs` and `DelegationTestServices.cs`. |
| V-2 service + real Git/DB, CP-3 then CP-4 | L1 red at fetch/committed worktree missing before verification release; green L1–L6 | New `Application/LandVerificationLeaseWindowTests.cs`, new harness/worker; service/protocol/ticket, StartRefAvailability, dispatcher and worktree service S2 files. Production `RunProcessAsync` internal visibility only; do not take over `VerifyUnderSlotAsync` policy. |
| V-3 hosted dispatch + lease admission, CP-5 then CP-6 | H1 red at actual waiter claim occurring after next land acquisition; green H1–H4 | New `Application/LandDispatchHandoffTests.cs`, new harness; waiters, wake/turn coordinator, hosted dispatcher and acquisition/release S3 sites. |
| V-4 hosted land reader + durable DB, CP-7 then CP-8 | Q1 red at actual service attempt order allowing concurrent new before replayed old; green Q1–Q4 | New `Application/LandRestartReplayTests.cs`, new harness/worker; queue, reconciliation helper, land service and both land hosted services S4 files. |
| V-1/V-2 native qualification, CP-9 | N1–N3 all execute on Windows; no skip permitted | New `Infrastructure/LandVerificationWindowNativeTests.cs`, same worker/harness and the actual native lock/process paths. |

Prefix paths in the footprint column with `tests/Antiphon.Tests/`; production
paths are the concrete S1–S4 inventory already specified above. Existing fixture
and test files are read/reused, not silently added to an ordinary whole-class run.

| Case / exact method | Recipe and variants | Oracle | Reachability / classification |
|---|---|---|---|
| W1: `LandVerificationWindowTests.C1379_LiveVerifierAdmitsOnlyScopedPreflight` | **slot-wait / live-build / between-children / live-tests.** Open a real window under L+R, release R, then call typed start-ref and distinct-worktree admission. At each phase also try ordinary and tag-only acquisitions. | typed acquisition exists while ordinary/tag-only is null; protected HEAD/admin/artifact sentinel hashes unchanged. | child readiness and acquisition-decision barriers; no child is required for the slot/gap arms; behaviour/integration; binding required. |
| W2: `LandVerificationWindowTests.C1379_UnrelatedOrUnprovenChildrenKeepTheFence` | **wrong binding / unrelated journal / malformed / start-intent / dead / reused / unknown / completed.** Add each independent disqualifier to an otherwise admissible live verifier record; retry typed admission with real provider. | null lease and unchanged journal bytes; independent matching-live canary acquires before each injected disqualifier. | assert the matching-live arm passed before changing exactly one fact; behaviour/integration; binding required. |
| W3: `LandVerificationWindowTests.C1379_ExecutionLeaseProtectsWorkspaceAndArtifacts` | **same provider / independent provider / other common directory / lock-order.** Pause verifier with L held; attempt a second land and a distinct repository operation. Instrument acquire/release without substituting lock ownership. | second land gets no L and performs zero reset/artifact writes; other repository succeeds; every L acquisition precedes R. | both providers resolve the actual common directory and held handle; behaviour/integration; binding required. |
| W4: `LandVerificationWindowTests.C1379_CloseCancelAndRestartInvalidateTheWindow` | **close / cancel / atomic-write failure / dead owner / unknown owner / malformed marker / replacement generation.** Close or interrupt at marker flush, window open and close/reacquire cuts; restart fresh providers and compare exact marker generation. | no stale capability admits; unknown state refuses; conclusively dead owner with no journal recovers; old close preserves replacement; unfinished journals survive. | fresh-provider plus owned-process crash arms; retain uncertain evidence before independent cleanup; behaviour/integration; binding required. |
| L1: `LandVerificationLeaseWindowTests.C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | **slot wait / live child / claim commit / slow remote preparation.** Start a real land, force verification, pause slot or helper child. Create from origin-only SHA and run ordinary Worktree dispatch. Block claim commit, then mirror/session handoff separately. | fetch resolves expected commit and committed claim/worktree HEAD exist before verifier release; R held until claim commit then free during remote handoff; B released before fresh R; separate DB connection can lock task during gap. | observe first admission decision and claim barrier; old behavior fails overlap assertion, not a timeout; behaviour/integration; binding required. |
| L2: `LandVerificationLeaseWindowTests.C1379_TargetMovementRequiresAnotherVerification` | **one target advance / retry budget exhausted.** Advance fixture origin from independent clone during first verification; record candidate/result pairs and advance again for exhaustion arm. | first candidate never publishes; new candidate receives its own passing result; retries bounded by LandTargetRaceRetries; independent remote read matches only authorized candidate. | nonempty explicit filter and code-path source delta keep each candidate in execution policy; behaviour/integration; binding required. |
| L3: `LandVerificationLeaseWindowTests.C1379_ResumeRejectsChangedAuthorityOrCandidate` | **request / attempt / active operation / eligibility / approval / filter / destination / source / pins / land identity / HEAD / tracked dirt / index / sequencer / remote source.** At released-R barrier change one authoritative DB or real Git fact, then resume unchanged green result. Start a fresh fixture per arm. | no Verified transition or push for changed ticket; expected changed fact retained; independent origin and source sentinels unchanged. | every arm first reaches Prepared+window-open with valid approval and exact candidate; keep unrelated task/request mirrors coherent, change the bound fact after preparation and before fresh leased validation; behaviour/integration; binding required. |
| L4: `LandVerificationLeaseWindowTests.C1379_ResumeRefreshesRegistrationsAndWriters` | **new source registration / new source writer / new repository writer.** Populate first operation-scope caches, admit a registration or claim during the gap, then resume. | fresh scope observes registration/writer; source checkout/admin/ref retained and cleanup/publication refused when incompatible. | assert first cache populated and second independent DB/Git observer sees new state before resume; behaviour/integration; binding required. |
| L5: `LandVerificationLeaseWindowTests.C1379_FailureCancellationAndRestartCannotPublishOldGreen` | **build failure / test failure / cancellation / crash Prepared / crash after child join / crash before fresh R.** Use failing owned helper and cancellation; for crash arms kill only fixture worker at acknowledged cuts and reconstruct graph with same schema/repo. | no failed/canceled publication; joined children release owned resources; uncertain journals retained; restarted Prepared verifies again before publication despite complete artifacts. | nonempty selected filter; parent owns root/descendant handles and compares verifier invocation candidate identities across restart; behaviour/integration; binding required. |
| L6: `LandVerificationLeaseWindowTests.C1379_CompatibleClaimsKeepAdmissionGuards` | **Code / Review / incompatible modes / overlapping path-ref / cap / eligibility / reservation / competing claim / prepared mirror.** Use real dispatcher with ordinary Code and Review tasks; vary source mode and one admission guard at a time; include prepared-mirror and prepared re-arm canaries. | allowed distinct tasks commit once; incompatible operation stays Held with no checkout mutation; caps/reservation/status conflicts claim nothing; established bypasses remain usable. | other admission inputs valid per arm; real workspace provision and transaction, isolated launch sink only after commit; behaviour/integration; binding required. |
| H1: `LandDispatchHandoffTests.C1379_ReadyWaiterAcquiresBeforeNextLand` | **oldest ready / later land / incompatible settlement.** Register actual queued work by an initial refused dispatch, open window/release R, then run hosted dispatch and later land. | oldest compatible task has real worktree plus committed claim before later land obtains R; settlement waits for ordinary release then acquires. | record acquisition sequence independently of Held text; later land is demonstrably contending; behaviour/integration; binding required. |
| H2: `LandDispatchHandoffTests.C1379_WakeupRunsAtTheNextSerialSafeBoundary` | **idle recipient / busy tick / lost signal / restart before consumption.** Signal real coalesced wake queue when idle and while first tick is blocked at a safe-boundary barrier. Drop signal and rebuild dispatcher for recovery arms. | targeted dispatch commits work at next boundary, max concurrent ticks=1; periodic scan recovers durable queued task after loss/restart. | ordinary timer held back until explicit recovery arm; busy tick entered before signal; no direct test call to targeted dispatch; behaviour/integration; binding required. |
| H3: `LandDispatchHandoffTests.C1379_ExpiredCanceledAndIneligibleTurnsReleasePriority` | **expired / canceled / ineligible / zero disabled / same attempt / interrupted admitted attempt / start-ref deadline.** Advance injected clock around ready-turn deadline; change one end condition; test retry with same request after actual admitted interruption. | land acquires after turn ends; no fresh budget per sweep/phase; admitted interrupted attempt gets fresh budget; zero gives no priority wait; precreate request respects original total deadline and leaves no task row. | fake clock at just-before and just-after budget, no 90-second sleeps; start-ref uses owned bounded child and deterministic deadline seam; behaviour/integration; binding required. |
| H4: `LandDispatchHandoffTests.C1379_CoalescingBoundsTheTargetedPass` | **nine waiters / repeated signals / concurrent wakes.** Queue nine compatible candidates, coalesce signals, let one bounded targeted pass run through real normal admission. | at most eight attempts before ordinary work resumes; ninth progresses in next pass; one committed claim per task and normal admission caps hold. | independent pass-entry/exit observations; ninth eligible before first pass starts; behaviour/integration; binding required. |
| Q1: `LandRestartReplayTests.C1379_OldHeldReplaysBeforeConcurrentNewRequest` | **startup busy / already eligible / equal timestamp / legacy task-only.** Commit old Held (previous hold now cleared), Queued and interrupted Running in reverse order; block startup reconciliation, concurrently commit new request; include GUID tie whose byte and ordinal order differ. | service attempt receipt order equals original RequestedAt then lowercase N ID; snapshot ready order matches; new request never precedes old eligible request; legacy time retained. | real hosted reader plus complete DB scan; observe persisted attempt and protocol entry, not enqueue; behaviour/integration; binding required. |
| Q2: `LandRestartReplayTests.C1379_FailedReplayKeepsTheReaderClosed` | **scan throw / incomplete scan / retry.** Fail reconciliation before completion after one old row was read; commit newer request and allow reader to run; then permit successful full retry. | zero protocol entries before successful barrier; all eligible old/new requests subsequently reach service in durable order. | reader started and wake delivered while barrier closed; injected failure observed; one explicit retry generation; behaviour/integration; binding required. |
| Q3: `LandRestartReplayTests.C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | **commit-before-wake / enqueue refused / snapshot-before-selection / dequeue-before-execute / committed-attempt crash / stale completion / superseded / terminal / NeedsResolution / recovery / cleanup-retry.** Cut each handoff in real request or cleanup-retry producer, reconciliation, ordered queue and hosted consumer. Rebuild on same schema for crash arms, inject dropped wake without deleting request. | exact durable request reaches protocol once per eligible attempt; older missed enqueue precedes newer selection; stale entry cannot execute or release replacement claim; no superseded/terminal/ineligible execution; recovery/max-attempt rules retained. | parent observes commits on separate connection before crash; busy first execution and idle reader variants; fault-free control reaches recipient; behaviour/integration; binding required. |
| Q4: `LandRestartReplayTests.C1379_HeldHeadGetsOneAttemptPerGeneration` | **Held head / different repository / next generation / newly ineligible.** Keep oldest request genuinely held by owned writer, allow later eligible repository to run, then remove hold and start next generation. | one evaluation per held request per generation without attempt inflation/spin; later eligible executes; original age restored next generation; status rechecked immediately before execution. | writer holds actual lease or active source claim; record evaluation versus admitted-attempt counts separately; behaviour/integration; binding required. |
| N1: `LandVerificationWindowNativeTests.C1379_PreflightUsesARealReleasedMutationHandle` | **Windows independent process / junction alias.** Hold then release R while L/window remains live; probe underlying landing.lock with owned second process and try supported typed preflight in owning process. | foreign handle cannot open before release, can after release; typed preflight succeeds; ordinary provider still refused by window. | require Windows, real installed helper and verified child readiness; foreign probe is lock observation, never authority to mutate; native qualification; binding required. |
| N2: `LandVerificationWindowNativeTests.C1379_ExecutionHandleExcludesASecondLand` | **Windows second land process.** Hold land-execution.lock throughout slot/child/gap, have owned independent process attempt real L, then close owner. | second land cannot acquire/reset until owner releases; protected workspace/artifacts unchanged. | require actual process and native handle contention, no in-memory substitute; native qualification; binding required. |
| N3: `LandVerificationWindowNativeTests.C1379_ExitedRootWithUnjoinedOutputStillFences` | **stdout only / stderr only.** Production process runner starts root that leaves one fixture-owned descendant holding exactly one redirected stream after root exits; then join descendant. | ordinary and typed acquisitions remain refused while root exited but stream live; journal retained; after join journal clears and ordinary acquisition succeeds once window closes. | independent root/descendant handles and stream readiness; each stream arm isolates omission of that drain; native qualification; binding required. |

The four red witnesses must reach their decisive assertion on the preceding
production behavior. Use bounded acquisition-decision/protocol-entry observation
barriers and assert the completed outcome before releasing the verifier in
finally; do not count a canceled task or watchdog timeout as red. Q1 records the
first actual consumed request before allowing full replay, so an unsorted reader
fails an order assertion rather than merely timing out. If S2 already supplies a
witness, preserve its inherited green and narrow that red obligation with evidence;
do not weaken the acceptance to manufacture a failure.

### Guards the regression

- R-1: widening a purpose tag, stale window or child record admits an unsafe writer | W1–W4 and N1–N3: actual acquisition refusal, unchanged protected refs/paths and retained exact journal/marker; CP-2/CP-9.
- R-2: releasing R accidentally authorizes publication from stale state or drops dispatch guards | L1–L6: separate DB and independent Git observations prove fresh lease/scope, unchanged-ticket passing result, no push on changed authority/failure, and one legitimate claim; CP-4.
- R-3: wakeup is only prose, races ticks, or starves work | H1–H4: committed waiter-before-land order, max active ticks one, turn expiry and at most eight attempts; CP-6.
- R-4: enqueue order masquerades as durable replay, loses requests, spins Held head, or releases a replacement | Q1–Q4: actual recipient attempts in durable order, empty consumption on failed scan, recovered missed handoffs and replacement claim retained; CP-8.

### Guard inventory

The inventory below splits independent safety/progress gates. Even guards retained
from the existing implementation are included where the split phase or new queue
could bypass them. Independent identity, status and producer checks have separate G/PC rows.
Alternative inputs to the same guard remain named variants: run every variant;
deleting only the named guard must fail its labeled assertion. Code must add a separate G/PC if implementation introduces
another independently bypassable guard not represented here. No guard is excluded
merely because the implementation already has a check.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-2: L owns workspace/artifacts throughout verification. | PC-1 |
| G-2 | D-2: Acquire L before R; never wait for L holding R. | PC-2 |
| G-3 | D-2: Only live process capability bound to held L can grant compatibility. | PC-3 |
| G-4 | D-2: Window capability matches common directory and full generation identity; independently bypassable common check. | PC-4 |
| G-5 | D-2: Window capability matches common directory and full generation identity; independently bypassable process-start check. | PC-5 |
| G-6 | D-2: Window capability matches common directory and full generation identity; independently bypassable nonce check. | PC-6 |
| G-7 | D-2: Window capability matches common directory and full generation identity; independently bypassable request check. | PC-7 |
| G-8 | D-2: Window capability matches common directory and full generation identity; independently bypassable attempt check. | PC-8 |
| G-9 | D-2: Window capability matches common directory and full generation identity; independently bypassable operation check. | PC-9 |
| G-10 | D-2: Window capability matches common directory and full generation identity; independently bypassable candidate check. | PC-10 |
| G-11 | D-2: Ordinary/tag-only writers remain fenced even without a child. | PC-11 |
| G-12 | D-2: Atomic durable window publication precedes R release. | PC-12 |
| G-13 | D-2: Unknown or malformed marker refuses admission; independently bypassable malformed check. | PC-13 |
| G-14 | D-2: Unknown or malformed marker refuses admission; independently bypassable unknown-owner check. | PC-14 |
| G-15 | D-2: Conclusive exact-owner death can deactivate marker. | PC-15 |
| G-16 | D-2: Old close removes only its generation. | PC-16 |
| G-17 | D-2/D-4: Close admission invalidates outstanding capability before resume. | PC-17 |
| G-18 | D-3: Only allowed ordinary operation modes receive capability. | PC-18 |
| G-19 | D-3: Destination/worktree/admin/ref must be distinct from protected source and land; independently bypassable source-path check. | PC-19 |
| G-20 | D-3: Destination/worktree/admin/ref must be distinct from protected source and land; independently bypassable land-path check. | PC-20 |
| G-21 | D-3: Destination/worktree/admin/ref must be distinct from protected source and land; independently bypassable admin-alias check. | PC-21 |
| G-22 | D-3: Destination/worktree/admin/ref must be distinct from protected source and land; independently bypassable protected-ref check. | PC-22 |
| G-23 | D-3: Only exactly correlated verifier child journal is exempt. | PC-23 |
| G-24 | D-3: Exempt child must have exact provably live PID/start identity. | PC-24 |
| G-25 | D-3: Malformed journal evidence fences. | PC-25 |
| G-26 | D-3: Unfinished start intent fences. | PC-26 |
| G-27 | D-3: Completed root does not prove joined child custody. | PC-27 |
| G-28 | D-4: Stdout drain precedes journal close. | PC-28 |
| G-29 | D-4: Stderr drain precedes journal close. | PC-29 |
| G-30 | D-3: R covers worktree mutation through committed claim. | PC-30 |
| G-31 | D-4: R is genuinely released throughout slot wait and live verifier. | PC-31 |
| G-32 | D-3: R releases after claim before nonmutating remote preparation/start. | PC-32 |
| G-33 | D-4: B releases before R reacquisition. | PC-33 |
| G-34 | D-4: Resume receives a fresh genuine owned R. | PC-34 |
| G-35 | D-4: No transaction/row lock spans slot/build gap. | PC-35 |
| G-36 | D-4: Git cache scope ends and is recreated across gap. | PC-36 |
| G-37 | D-4: Resume matches current request identity. | PC-37 |
| G-38 | D-4: Resume matches admitted attempt. | PC-38 |
| G-39 | D-4: Resume matches active operation. | PC-39 |
| G-40 | D-4: Resume rechecks task/request eligibility. | PC-40 |
| G-41 | D-4: Resume revalidates approval/evidence. | PC-41 |
| G-42 | D-4: Resume rechecks exact verification filter. | PC-42 |
| G-43 | D-4: Resume rechecks repository/destination. | PC-43 |
| G-44 | D-4: Resume rechecks source ref and SHA; independently bypassable source-ref check. | PC-44 |
| G-45 | D-4: Resume rechecks source ref and SHA; independently bypassable source-SHA check. | PC-45 |
| G-46 | D-4: Resume rechecks recovery pins. | PC-46 |
| G-47 | D-4: Resume rechecks land registration/admin identity; independently bypassable land-registration check. | PC-47 |
| G-48 | D-4: Resume rechecks land registration/admin identity; independently bypassable admin check. | PC-48 |
| G-49 | D-4: Resume rechecks candidate HEAD. | PC-49 |
| G-50 | D-4: Resume rechecks tracked cleanliness. | PC-50 |
| G-51 | D-4: Resume rechecks index state. | PC-51 |
| G-52 | D-4: Resume rechecks sequencer state. | PC-52 |
| G-53 | D-4: Resume observes newly admitted writers. | PC-53 |
| G-54 | D-4: Resume refreshes remote source authority. | PC-54 |
| G-55 | D-4: New target candidate needs its own verification result. | PC-55 |
| G-56 | D-4: Target race retries remain bounded. | PC-56 |
| G-57 | D-4: Failed verifier cannot transition Verified. | PC-57 |
| G-58 | D-4: Canceled verifier cannot publish. | PC-58 |
| G-59 | D-4: Cancellation/failure joins owned child before releasing custody. | PC-59 |
| G-60 | D-4: Restarted Prepared re-verifies instead of trusting artifacts. | PC-60 |
| G-61 | D-2/D-4: Owner death never clears unfinished child journals. | PC-61 |
| G-62 | D-3/D-5: Compatibility cannot bypass concurrency caps. | PC-62 |
| G-63 | D-3/D-5: Compatibility cannot bypass dispatch eligibility. | PC-63 |
| G-64 | D-3/D-5: Compatibility cannot bypass workspace reservation. | PC-64 |
| G-65 | D-3/D-5: Transactional claim uniqueness survives competing dispatcher. | PC-65 |
| G-66 | D-3: Prepared-mirror/re-arm nonmutating bypass remains available. | PC-66 |
| G-67 | D-5: Oldest ready compatible waiter receives actual turn before later land. | PC-67 |
| G-68 | D-5: Hosted dispatcher has one serial tick. | PC-68 |
| G-69 | D-5: Wake reaches idle and next safe in-tick boundary; independently bypassable idle check. | PC-69 |
| G-70 | D-5: Wake reaches idle and next safe in-tick boundary; independently bypassable busy-boundary check. | PC-70 |
| G-71 | D-5: Expired priority cannot starve land. | PC-71 |
| G-72 | D-5: Cancellation relinquishes priority. | PC-72 |
| G-73 | D-5: Loss of eligibility relinquishes priority. | PC-73 |
| G-74 | D-5: Yield budget persists across sweeps/phases of one attempt. | PC-74 |
| G-75 | D-5: Admitted interruption gets fresh budget. | PC-75 |
| G-76 | D-5: Zero setting disables admission yield. | PC-76 |
| G-77 | D-3/D-5: Start-ref lease/fetch/probe share bounded original deadline. | PC-77 |
| G-78 | D-5: Targeted pass stops at eight attempts. | PC-78 |
| G-79 | D-5: Lost wake/restart recovers durable queued dispatch. | PC-79 |
| G-80 | D-6: Complete successful replay gates first selection; independently bypassable partial check. | PC-80 |
| G-81 | D-6: Complete successful replay gates first selection; independently bypassable failure check. | PC-81 |
| G-82 | D-6: Selection uses original RequestedAt order. | PC-82 |
| G-83 | D-6: Equal times use ordinal lowercase N ID order. | PC-83 |
| G-84 | D-6: Legacy normalization preserves original LandRequestedAt. | PC-84 |
| G-85 | D-6: Every selection merges fresh complete durable snapshot. | PC-85 |
| G-86 | D-6: Immediate execution rechecks request identity and eligibility; independently bypassable superseded check. | PC-86 |
| G-87 | D-6: Immediate execution rechecks request identity and eligibility; independently bypassable terminal check. | PC-87 |
| G-88 | D-6: Immediate execution rechecks request identity and eligibility; independently bypassable NeedsResolution check. | PC-88 |
| G-89 | D-6: Immediate execution rechecks request identity and eligibility; independently bypassable ineligible check. | PC-89 |
| G-90 | D-6: Recovery eligibility and max interruptions remain enforced; independently bypassable reviewed-recovery check. | PC-90 |
| G-91 | D-6: Recovery eligibility and max interruptions remain enforced; independently bypassable max-attempt check. | PC-91 |
| G-92 | D-6: Duplicate request cannot gain a second active claim. | PC-92 |
| G-93 | D-6: Old completion cannot release newer entry. | PC-93 |
| G-94 | D-6: Held request evaluated once per generation. | PC-94 |
| G-95 | D-6: Next generation restores Held original age. | PC-95 |
| G-96 | D-6: Snapshot describes same ready order consumed. | PC-96 |
| G-97 | D-6: A later commit cannot preempt executing land. | PC-97 |
| G-98 | D-2 native: R release really closes the Windows OS handle. | PC-98 |
| G-99 | D-2 native: L excludes another Windows process. | PC-99 |
| G-100 | D-2/D-4: Closing the window lets an already admitted preflight finish before resume. | PC-100 |
| G-101 | D-4: R reacquisition honors cancellation. | PC-101 |
| G-102 | D-4: R reacquisition polling is not another land attempt. | PC-102 |
| G-103 | D-6: Single global reader finishes current execution before next repository land; independently bypassable busy-reader check. | PC-103 |
| G-104 | D-6: Producer commits durable request before reconciliation wake. | PC-104 |
| G-105 | D-6: Restart discovers consumed-but-unfinished durable requests; independently bypassable dequeue-crash check. | PC-105 |
| G-106 | D-6: Restart discovers consumed-but-unfinished durable requests; independently bypassable attempt-commit-crash check. | PC-106 |

### Positive controls

Every PC below is required post-land, using the exact method filter shown. The
recipe is a **compiling production defect**, with all other inputs valid; each
slash-separated variant is a distinct mutation arm, not permission to stop after
one red. The oracle is a labeled ordinary assertion with prefix `c1379-gN-`
and the lowercase variant name; exact labels are listed in each row. Never assert
only that a mutant flag was set. Code
binds concrete production file/symbol/line, variant payload and the listed assertion
label in an append-only binding record. Unimplemented/unbound payloads remain
**binding required after Code**, not waived or represented as executed.

Mutation runs baseline, break/red, exact restore/fresh-build/green for every
variant, on the bound landed snapshot; method scope only. Review judges this
pending inventory before land; Code runs V/R, not this mutation battery. Build
errors, zero tests, unrelated assertions and fixture failures are invalid red.
A restored green must run the same complete method. No PC runs in TestDesign.

| PC / break guard by compiling defect | Variant | Detecting filter (one exact method) | Expected red assertion |
|---|---|---|---|
| PC-1: break G-1; dispose L immediately before verifier call. | second-land | `/*/*/LandVerificationWindowTests/C1379_ExecutionLeaseProtectsWorkspaceAndArtifacts` | `c1379-g1-second-land`: second L is null and reset/artifact-write count is zero. |
| PC-2: break G-2; move L acquisition after successful R acquisition. | lock-order | `/*/*/LandVerificationWindowTests/C1379_ExecutionLeaseProtectsWorkspaceAndArtifacts` | `c1379-g2-lock-order`: recorded L acquisition precedes R acquisition. |
| PC-3: break G-3; accept persisted marker as sufficient capability. | marker-only | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g3-marker-only`: typed acquisition without live capability is null. |
| PC-4: break G-4; omit one capability identity comparison per variant (only the common check). | common | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g4-common`: mismatched capability is refused. |
| PC-5: break G-5; omit one capability identity comparison per variant (only the process-start check). | process-start | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g5-process-start`: mismatched capability is refused. |
| PC-6: break G-6; omit one capability identity comparison per variant (only the nonce check). | nonce | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g6-nonce`: mismatched capability is refused. |
| PC-7: break G-7; omit one capability identity comparison per variant (only the request check). | request | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g7-request`: mismatched capability is refused. |
| PC-8: break G-8; omit one capability identity comparison per variant (only the attempt check). | attempt | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g8-attempt`: mismatched capability is refused. |
| PC-9: break G-9; omit one capability identity comparison per variant (only the operation check). | operation | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g9-operation`: mismatched capability is refused. |
| PC-10: break G-10; omit one capability identity comparison per variant (only the candidate check). | candidate | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g10-candidate`: mismatched capability is refused. |
| PC-11: break G-11; remove open-window refusal from ordinary AcquireAsync. | slot-wait/between-children | `/*/*/LandVerificationWindowTests/C1379_LiveVerifierAdmitsOnlyScopedPreflight` | `c1379-g11-slot-wait`, `c1379-g11-between-children`: ordinary lease is null. |
| PC-12: break G-12; release R before marker publication completes. | marker-write-fault | `/*/*/LandVerificationWindowTests/C1379_CloseCancelAndRestartInvalidateTheWindow` | `c1379-g12-marker-write-fault`: R remains unavailable and no compatibility grant escapes failed publication. |
| PC-13: break G-13; treat unreadable or unprovable marker as absent (only the malformed check). | malformed | `/*/*/LandVerificationWindowTests/C1379_CloseCancelAndRestartInvalidateTheWindow` | `c1379-g13-malformed`: admission is refused with marker retained. |
| PC-14: break G-14; treat unreadable or unprovable marker as absent (only the unknown-owner check). | unknown-owner | `/*/*/LandVerificationWindowTests/C1379_CloseCancelAndRestartInvalidateTheWindow` | `c1379-g14-unknown-owner`: admission is refused with marker retained. |
| PC-15: break G-15; keep a proven-dead marker permanently active. | dead-owner-no-child | `/*/*/LandVerificationWindowTests/C1379_CloseCancelAndRestartInvalidateTheWindow` | `c1379-g15-dead-owner-no-child`: fresh L+R acquisition and normal new window succeed. |
| PC-16: break G-16; delete marker without generation comparison. | replacement-generation | `/*/*/LandVerificationWindowTests/C1379_CloseCancelAndRestartInvalidateTheWindow` | `c1379-g16-replacement-generation`: replacement marker bytes remain. |
| PC-17: break G-17; leave window accepting through R reacquisition. | close/cancel | `/*/*/LandVerificationWindowTests/C1379_CloseCancelAndRestartInvalidateTheWindow` | `c1379-g17-close`, `c1379-g17-cancel`: new typed acquire is refused after close barrier. |
| PC-18: break G-18; return compatible for one excluded mode per variant. | Shared/repair/adoption/SourceLanding/Interim/park-repair/settlement/cleanup/canonical-reset | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g18-shared`, `c1379-g18-repair`, `c1379-g18-adoption`, `c1379-g18-sourcelanding`, `c1379-g18-interim`, `c1379-g18-park-repair`, `c1379-g18-settlement`, `c1379-g18-cleanup`, `c1379-g18-canonical-reset`: excluded operation acquires no compatibility lease and mutates no protected checkout. |
| PC-19: break G-19; omit one overlap comparison per variant (only the source-path check). | source-path | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g19-source-path`: overlapping preflight is refused and protected sentinels unchanged. |
| PC-20: break G-20; omit one overlap comparison per variant (only the land-path check). | land-path | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g20-land-path`: overlapping preflight is refused and protected sentinels unchanged. |
| PC-21: break G-21; omit one overlap comparison per variant (only the admin-alias check). | admin-alias | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g21-admin-alias`: overlapping preflight is refused and protected sentinels unchanged. |
| PC-22: break G-22; omit one overlap comparison per variant (only the protected-ref check). | protected-ref | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g22-protected-ref`: overlapping preflight is refused and protected sentinels unchanged. |
| PC-23: break G-23; ignore request/window/operation correlation when reading verifier record. | unrelated/wrong-binding | `/*/*/LandVerificationWindowTests/C1379_UnrelatedOrUnprovenChildrenKeepTheFence` | `c1379-g23-unrelated`, `c1379-g23-wrong-binding`: typed lease is null while mismatched record remains. |
| PC-24: break G-24; allow dead, reused or unknown process observation. | dead/reused/unknown | `/*/*/LandVerificationWindowTests/C1379_UnrelatedOrUnprovenChildrenKeepTheFence` | `c1379-g24-dead`, `c1379-g24-reused`, `c1379-g24-unknown`: typed lease is null and journal remains. |
| PC-25: break G-25; continue past parse failure or wrong record schema. | malformed | `/*/*/LandVerificationWindowTests/C1379_UnrelatedOrUnprovenChildrenKeepTheFence` | `c1379-g25-malformed`: typed lease is null. |
| PC-26: break G-26; ignore record with missing process identity. | start-intent | `/*/*/LandVerificationWindowTests/C1379_UnrelatedOrUnprovenChildrenKeepTheFence` | `c1379-g26-start-intent`: typed lease is null. |
| PC-27: break G-27; allow Completed verifier record through compatibility. | completed | `/*/*/LandVerificationWindowTests/C1379_UnrelatedOrUnprovenChildrenKeepTheFence` | `c1379-g27-completed`: typed lease is null. |
| PC-28: break G-28; omit stdout task from awaited completion before ExitedAsync. | stdout-only | `/*/*/LandVerificationWindowNativeTests/C1379_ExitedRootWithUnjoinedOutputStillFences` | `c1379-g28-stdout-only`: journal remains and acquisition is null while descendant owns stdout. |
| PC-29: break G-29; omit stderr task from awaited completion before ExitedAsync. | stderr-only | `/*/*/LandVerificationWindowNativeTests/C1379_ExitedRootWithUnjoinedOutputStillFences` | `c1379-g29-stderr-only`: journal remains and acquisition is null while descendant owns stderr. |
| PC-30: break G-30; dispose dispatch R before claim transaction commits. | claim-commit | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | `c1379-g30-claim-commit`: competing ordinary acquire is null until separate connection sees commit. |
| PC-31: break G-31; retain original R until verifier returns. | slot/live-child | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | `c1379-g31-slot`, `c1379-g31-live-child`: origin-only fetch and real committed worktree complete before verifier release. |
| PC-32: break G-32; retain dispatch R through blocked remote preparation. | remote-handoff | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | `c1379-g32-remote-handoff`: another compatible preflight acquires during blocked remote handoff. |
| PC-33: break G-33; reacquire R before slot ReleaseAsync. | slot-order | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | `c1379-g33-slot-order`: B release ordinal precedes fresh R acquire ordinal. |
| PC-34: break G-34; pass disposed prepared lease into resume. | fresh-lease | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | `c1379-g34-fresh-lease`: resumed lease is owned, live and different acquisition; no invalid publication. |
| PC-35: break G-35; keep admission transaction open through verifier. | row-lock | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | `c1379-g35-row-lock`: independent DB transaction takes task lock with NOWAIT at verifier barrier (assert successful acquisition, not timeout). |
| PC-36: break G-36; retain original operation scope during resume. | registration | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRefreshesRegistrationsAndWriters` | `c1379-g36-registration`: fresh observation retains newly registered source and prevents unsafe cleanup. |
| PC-37: break G-37; omit current-request equality check. | request | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g37-request`: no Verified or push for replaced request. |
| PC-38: break G-38; omit attempt equality check. | attempt | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g38-attempt`: no Verified or push for changed attempt. |
| PC-39: break G-39; omit active-operation equality check. | active-operation | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g39-active-operation`: no Verified or push for superseded operation. |
| PC-40: break G-40; omit eligibility recheck. | eligibility | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g40-eligibility`: no Verified or push after task becomes ineligible. |
| PC-41: break G-41; reuse pre-gap approval without revalidation. | approval | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g41-approval`: no Verified or push after approval identity/source-clean evidence changes. |
| PC-42: break G-42; omit filter comparison. | filter | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g42-filter`: no Verified or push under different selected filter. |
| PC-43: break G-43; reuse pre-gap destination unconditionally. | destination | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g43-destination`: no push to old or replacement destination. |
| PC-44: break G-44; skip fresh source inspection (only the source-ref check). | source-ref | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g44-source-ref`: no Verified or push after source movement. |
| PC-45: break G-45; skip fresh source inspection (only the source-SHA check). | source-SHA | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g45-source-sha`: no Verified or push after source movement. |
| PC-46: break G-46; skip pin validation on continuation. | source-pin/target-pin/prepared-pin | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g46-source-pin`, `c1379-g46-target-pin`, `c1379-g46-prepared-pin`: no Verified or push with changed pin. |
| PC-47: break G-47; trust ticket paths without current registration identity (only the land-registration check). | land-registration | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g47-land-registration`: no Verified or push after identity replacement. |
| PC-48: break G-48; trust ticket paths without current registration identity (only the admin check). | admin | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g48-admin`: no Verified or push after identity replacement. |
| PC-49: break G-49; ignore candidate SHA mismatch. | HEAD | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g49-head`: no Verified or push for different candidate. |
| PC-50: break G-50; suppress dirty tracked-file refusal. | tracked-dirt | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g50-tracked-dirt`: no Verified or push while tracked file changed. |
| PC-51: break G-51; suppress changed/locked index refusal. | index | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g51-index`: no Verified or push with incompatible index. |
| PC-52: break G-52; suppress active-sequencer refusal. | sequencer | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g52-sequencer`: no Verified or push during rebase/merge state. |
| PC-53: break G-53; skip post-gap writer query. | source-writer/repository-writer | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRefreshesRegistrationsAndWriters` | `c1379-g53-source-writer`, `c1379-g53-repository-writer`: no publication/cleanup while new incompatible writer owns source. |
| PC-54: break G-54; reuse pre-gap remote source observation. | remote-source | `/*/*/LandVerificationLeaseWindowTests/C1379_ResumeRejectsChangedAuthorityOrCandidate` | `c1379-g54-remote-source`: changed remote source refuses publication. |
| PC-55: break G-55; carry first green result into target-race candidate. | target-moved | `/*/*/LandVerificationLeaseWindowTests/C1379_TargetMovementRequiresAnotherVerification` | `c1379-g55-target-moved`: new candidate has distinct successful verification before any push. |
| PC-56: break G-56; ignore LandTargetRaceRetries exhaustion. | budget-exhausted | `/*/*/LandVerificationLeaseWindowTests/C1379_TargetMovementRequiresAnotherVerification` | `c1379-g56-budget-exhausted`: verification/preparation count stops at configured bound. |
| PC-57: break G-57; coerce failing build/test result to passing. | build-fail/test-fail | `/*/*/LandVerificationLeaseWindowTests/C1379_FailureCancellationAndRestartCannotPublishOldGreen` | `c1379-g57-build-fail`, `c1379-g57-test-fail`: Verified/push count is zero. |
| PC-58: break G-58; swallow cancellation and continue with prior green. | cancel | `/*/*/LandVerificationLeaseWindowTests/C1379_FailureCancellationAndRestartCannotPublishOldGreen` | `c1379-g58-cancel`: no publication after canceled turn. |
| PC-59: break G-59; return from cancellation before owned child/output joins. | cancel-child | `/*/*/LandVerificationLeaseWindowTests/C1379_FailureCancellationAndRestartCannotPublishOldGreen` | `c1379-g59-cancel-child`: child is exited/drained before ownership release; uncertain journal retained. |
| PC-60: break G-60; resume Prepared as Verified when artifact generation is complete. | restart-after-join | `/*/*/LandVerificationLeaseWindowTests/C1379_FailureCancellationAndRestartCannotPublishOldGreen` | `c1379-g60-restart-after-join`: new verifier call for exact candidate precedes publication. |
| PC-61: break G-61; remove all window child journals on dead marker recovery. | dead-owner-live-child | `/*/*/LandVerificationWindowTests/C1379_CloseCancelAndRestartInvalidateTheWindow` | `c1379-g61-dead-owner-live-child`: journal bytes remain and admission stays fenced. |
| PC-62: break G-62; skip capacity guard only for typed preflight. | cap | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g62-cap`: over-cap task has no committed claim. |
| PC-63: break G-63; skip fresh task eligibility guard for typed preflight. | eligibility | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g63-eligibility`: ineligible task has no committed claim. |
| PC-64: break G-64; ignore conflicting workspace reservation for preflight. | reservation | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g64-reservation`: reservation conflict leaves no claim or mutation. |
| PC-65: break G-65; omit claim ownership/status compare on preflight path. | competing-claim | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g65-competing-claim`: exactly one committed task/attempt claim and checkout. |
| PC-66: break G-66; force these paths through incompatible ordinary window admission. | prepared-mirror/prepared-rearm | `/*/*/LandVerificationLeaseWindowTests/C1379_CompatibleClaimsKeepAdmissionGuards` | `c1379-g66-prepared-mirror`, `c1379-g66-prepared-rearm`: prepared nonmutating task progresses during window. |
| PC-67: break G-67; let later land bypass reserved ready turn. | oldest-ready | `/*/*/LandDispatchHandoffTests/C1379_ReadyWaiterAcquiresBeforeNextLand` | `c1379-g67-oldest-ready`: waiter committed claim ordinal precedes later land R acquire. |
| PC-68: break G-68; start independent TickAsync on every signal. | busy-tick | `/*/*/LandDispatchHandoffTests/C1379_WakeupRunsAtTheNextSerialSafeBoundary` | `c1379-g68-busy-tick`: maximum concurrently entered ticks equals one. |
| PC-69: break G-69; consume wake flag without servicing targeted pass (only the idle check). | idle | `/*/*/LandDispatchHandoffTests/C1379_WakeupRunsAtTheNextSerialSafeBoundary` | `c1379-g69-idle`: targeted claim commits before held ordinary timer is released. |
| PC-70: break G-70; consume wake flag without servicing targeted pass (only the busy-boundary check). | busy-boundary | `/*/*/LandDispatchHandoffTests/C1379_WakeupRunsAtTheNextSerialSafeBoundary` | `c1379-g70-busy-boundary`: targeted claim commits before held ordinary timer is released. |
| PC-71: break G-71; retain turn after deadline. | expired | `/*/*/LandDispatchHandoffTests/C1379_ExpiredCanceledAndIneligibleTurnsReleasePriority` | `c1379-g71-expired`: later land acquires after expiry. |
| PC-72: break G-72; ignore cancellation when selecting turn owner. | canceled | `/*/*/LandDispatchHandoffTests/C1379_ExpiredCanceledAndIneligibleTurnsReleasePriority` | `c1379-g72-canceled`: later land acquires after cancellation. |
| PC-73: break G-73; keep ineligible turn owner selected. | ineligible | `/*/*/LandDispatchHandoffTests/C1379_ExpiredCanceledAndIneligibleTurnsReleasePriority` | `c1379-g73-ineligible`: later land acquires after eligibility loss. |
| PC-74: break G-74; reset first-yield at every sweep/window transition. | same-attempt | `/*/*/LandDispatchHandoffTests/C1379_ExpiredCanceledAndIneligibleTurnsReleasePriority` | `c1379-g74-same-attempt`: expiry occurs at original budget despite repeated sweeps. |
| PC-75: break G-75; retain exhausted yield after admission. | interrupted-attempt | `/*/*/LandDispatchHandoffTests/C1379_ExpiredCanceledAndIneligibleTurnsReleasePriority` | `c1379-g75-interrupted-attempt`: retry yields under fresh budget. |
| PC-76: break G-76; interpret zero as default 90. | zero-disabled | `/*/*/LandDispatchHandoffTests/C1379_ExpiredCanceledAndIneligibleTurnsReleasePriority` | `c1379-g76-zero-disabled`: land acquires without priority delay. |
| PC-77: break G-77; reset network deadline after compatible lease acquisition. | start-ref-deadline | `/*/*/LandDispatchHandoffTests/C1379_ExpiredCanceledAndIneligibleTurnsReleasePriority` | `c1379-g77-start-ref-deadline`: total deadline refusal leaves no created task or live owned child. |
| PC-78: break G-78; remove eight-attempt termination. | nine-waiters | `/*/*/LandDispatchHandoffTests/C1379_CoalescingBoundsTheTargetedPass` | `c1379-g78-nine-waiters`: ordinary work resumes before ninth targeted attempt. |
| PC-79: break G-79; omit periodic rediscovery after lost wake. | lost-signal/restart | `/*/*/LandDispatchHandoffTests/C1379_WakeupRunsAtTheNextSerialSafeBoundary` | `c1379-g79-lost-signal`, `c1379-g79-restart`: queued task reaches committed claim after explicit backstop tick. |
| PC-80: break G-80; open replay barrier before scan finishes or after failure (only the partial check). | partial | `/*/*/LandRestartReplayTests/C1379_FailedReplayKeepsTheReaderClosed` | `c1379-g80-partial`: protocol entry count remains zero before complete successful scan. |
| PC-81: break G-81; open replay barrier before scan finishes or after failure (only the failure check). | failure | `/*/*/LandRestartReplayTests/C1379_FailedReplayKeepsTheReaderClosed` | `c1379-g81-failure`: protocol entry count remains zero before complete successful scan. |
| PC-82: break G-82; sort by arrival/dequeue time. | reverse-insert | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest` | `c1379-g82-reverse-insert`: recipient attempt order equals original durable timestamps. |
| PC-83: break G-83; sort tied requests by Guid byte/database incidental order. | tied-ID | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest` | `c1379-g83-tied-id`: recipient tied pair matches ordinal N order. |
| PC-84: break G-84; assign now while EnsureRequestAsync creates legacy request. | legacy | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest` | `c1379-g84-legacy`: normalized RequestedAt equals original task time and executes at original age. |
| PC-85: break G-85; use only startup snapshot or successful enqueues. | dropped-wake/older-commit/cleanup-retry | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g85-dropped-wake`, `c1379-g85-older-commit`, `c1379-g85-cleanup-retry`: missed committed older request reaches service before newer selection. |
| PC-86: break G-86; execute stale selected request without final row checks (only the superseded check). | superseded | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g86-superseded`: stale request has no protocol mutation or new admitted attempt. |
| PC-87: break G-87; execute stale selected request without final row checks (only the terminal check). | terminal | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g87-terminal`: stale request has no protocol mutation or new admitted attempt. |
| PC-88: break G-88; execute stale selected request without final row checks (only the NeedsResolution check). | NeedsResolution | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g88-needsresolution`: stale request has no protocol mutation or new admitted attempt. |
| PC-89: break G-89; execute stale selected request without final row checks (only the ineligible check). | ineligible | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g89-ineligible`: stale request has no protocol mutation or new admitted attempt. |
| PC-90: break G-90; skip one recovery/max-attempt guard per variant (only the reviewed-recovery check). | reviewed-recovery | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g90-reviewed-recovery`: only reviewed eligible recovery executes; exhausted request does not. |
| PC-91: break G-91; skip one recovery/max-attempt guard per variant (only the max-attempt check). | max-attempt | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g91-max-attempt`: only reviewed eligible recovery executes; exhausted request does not. |
| PC-92: break G-92; remove request-aware active dedup. | duplicate | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g92-duplicate`: one active entry identity for that request, then one recipient execution per eligible attempt. |
| PC-93: break G-93; replace Release(entry) identity check with task-only removal. | stale-completion | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g93-stale-completion`: new entry remains claimed and executes once. |
| PC-94: break G-94; immediately reinsert Held at ready head. | held-head | `/*/*/LandRestartReplayTests/C1379_HeldHeadGetsOneAttemptPerGeneration` | `c1379-g94-held-head`: one evaluation and no attempt inflation in same generation; later eligible work executes. |
| PC-95: break G-95; overwrite RequestedAt on requeue. | reeligible | `/*/*/LandRestartReplayTests/C1379_HeldHeadGetsOneAttemptPerGeneration` | `c1379-g95-reeligible`: old request precedes younger eligible request next generation. |
| PC-96: break G-96; project insertion order while consumer sorts. | snapshot | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest` | `c1379-g96-snapshot`: snapshot positions equal next recipient selections. |
| PC-97: break G-97; replace active selection when an older-time request commits. | commit-after-selection | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g97-commit-after-selection`: current operation finishes before newly committed request starts. |
| PC-98: break G-98; leave original FileStream open when logical lease is disposed. | native-release | `/*/*/LandVerificationWindowNativeTests/C1379_PreflightUsesARealReleasedMutationHandle` | `c1379-g98-native-release`: independent process can open landing.lock after release. |
| PC-99: break G-99; open land-execution.lock with permissive file sharing. | native-exclusion | `/*/*/LandVerificationWindowNativeTests/C1379_ExecutionHandleExcludesASecondLand` | `c1379-g99-native-exclusion`: independent process fails acquisition until owner closes. |
| PC-100: break G-100; dispose an active preflight lease from the window close path. | active-preflight-close | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | `c1379-g100-active-preflight-close`: in-flight claim commits under its still-owned R before land reacquires. |
| PC-101: break G-101; use CancellationToken.None for the reacquisition wait. | cancel-reacquire | `/*/*/LandVerificationLeaseWindowTests/C1379_FailureCancellationAndRestartCannotPublishOldGreen` | `c1379-g101-cancel-reacquire`: canceled reacquisition completes without publication and closes admission while retaining unjoined evidence. |
| PC-102: break G-102; increment durable Attempt at each resume acquisition poll. | reacquire-polls | `/*/*/LandVerificationLeaseWindowTests/C1379_StartRefAndWorktreeFinishBeforeVerificationRelease` | `c1379-g102-reacquire-polls`: separate DB reads show unchanged admitted attempt across refused polls. |
| PC-103: break G-103; launch each dequeued request without awaiting its service invocation (only the busy-reader check). | busy-reader | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest` | `c1379-g103-busy-reader`: maximum simultaneous recipient invocations is one across repositories. |
| PC-104: break G-104; move request or cleanup-retry signal before durable commit. | request-commit/cleanup-commit | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g104-request-commit`, `c1379-g104-cleanup-commit`: separate DB observer at signal sees exact committed pending request. |
| PC-105: break G-105; exclude interrupted Running requests from restart reconciliation (only the dequeue-crash check). | dequeue-crash | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g105-dequeue-crash`: same durable request reaches actual protocol after fresh-process reconciliation with preserved request time. |
| PC-106: break G-106; exclude interrupted Running requests from restart reconciliation (only the attempt-commit-crash check). | attempt-commit-crash | `/*/*/LandRestartReplayTests/C1379_DurableReconciliationSurvivesDroppedWakeupsAndStaleEntries` | `c1379-g106-attempt-commit-crash`: same durable request reaches actual protocol after fresh-process reconciliation with preserved request time. |

### Out of scope

- CARD-1380's compiler skip/cache/artifact policy and full cache suite: predecessor owns them. Tests force a code delta plus explicit filter where execution is needed; L5 still tests that completed artifacts are not restart approval.
- Existing publication CAS/remote confirmation, receipt-backed deletion internals and full landing suite: unchanged algorithms. L2/L3/L4 traverse their normal guarded path and independently observe remote/source retention; this card does not claim to requalify every historical guard inside those subsystems.
- Caller notifications, frozen payload age, richer cross-process owner DTOs, terminal input and parking: D-7/D-8 exclude changes. Internal recipient consumption is not transcript delivery or UI receipt.
- Real compiler throughput and fixed dispatch latency: controlled children/clock establish overlap and bounded decision rules. Native Windows rows qualify lock and pipe semantics, not live AppHost activation or host capacity.
- RC adoption, live restarts and fleet operations: master only. No build or test was run during TestDesign; no passing receipt is claimed.

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

Code commissioning override (task `e1e0eae5`, 2026-10-20): CARD-1380 S2 is
present at `8c70d23802bf989e38ed353adb5116b35ed5158a`. The explicit brief
requires one red witness and one narrow green per behavior, with trailing
wildcards and a 40-minute row timeout. CP-8 therefore selects Q1 only; Q2–Q4
are deferred, not claimed passed. CP-9 is Windows-gated and not executed here.
All PC-1–PC-106 variants remain pending and unbound for Mutation.
The independent replay slice can precede the dependent lease slices.
CP-3 and CP-5 are merged into CP-1; CP-4 and CP-6 are merged into CP-2.
Each merged row selects exactly the three named acceptance witnesses, sharing
one isolated build and one test host. The other W/L/H methods and variants are
deferred under the explicit trimmed commissioning scope, not reported passed.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1R-S3R | `tests/Antiphon.Tests -> bin-c1379-boundaries-red/` | any-boundaries-red | `/*/*/(LandVerificationWindowTests*)\|(LandVerificationLeaseWindowTests*)\|(LandDispatchHandoffTests*)/C1379_*` | V-1, V-2, V-3 red | exactly 3 executed; expected admission, release, handoff assertions | 3 | 8 |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c1379-boundaries-green/` | any-boundaries-green | `/*/*/(LandVerificationWindowTests*)\|(LandVerificationLeaseWindowTests*)\|(LandDispatchHandoffTests*)/C1379_*` | V-1, V-2, V-3, R-1, R-2, R-3 trimmed | 3 selected witnesses, 0 failed/skipped | 3 | 8 |
| CP-7 | S4R | `tests/Antiphon.Tests -> bin-c1379-replay-red/` | any-replay-red | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest*` | V-4 red | exactly 1 executed; expected replay-order assertion failure | 1 | 5 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c1379-replay-green/` | any-replay-green | `/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest*` | V-4, R-4 trimmed | 1 selected witness, 0 failed/skipped | 1 | 6 |
| CP-9 | S2 | `tests/Antiphon.Tests -> bin-c1379-native-windows/` | windows-native-lease | `/*/*/LandVerificationWindowNativeTests/C1379_*` | V-1, V-2, R-1, R-2 native | all 3 listed on Windows, 0 failed/skipped | 3 | 8 |


### Cost

All costs below are **estimates**, not measured results. The 30-minute TestDesign
budget is authoring/inspection time, separate from Code/Mutation execution.

- Ordinary Code V/R floor = CP-1–CP-9 = 5 + 6 + 5 + 8 + 5 + 6 + 5 + 6 + 8 = **54 minutes**, including nine isolated builds. Four red executions + 18 default-lane green executions + three Windows executions = **25 TUnit executions**; internal variants are not additional executions. Review's first independent ordinary validation remains separately commissioned.
- Setup/checkpoint-tool bootstrap allowance = **3 minutes** if an owned certified tool build is needed. Do not add an outer slot around the tool's already gated row builds.
- Mutation floor = **106 PCs, 129 explicit mutation arms**. Each slash-separated variant in the PC table runs; no whole-class PC selection. The table names every exact filter. Default-lane arms: **125 x 12 = 1500 minutes** (4 baseline + 4 compiling-defect red + 4 restored green, including builds). Windows N1/N2/N3 arms: **4 x 18 = 72 minutes** (6 + 6 + 6). Total PC floor = **1572 minutes**, excluding mutation authoring/discovery/reporting. Do not confuse the compact 21-method roster with a cheap full mutation battery.
- Total execution estimate including one setup = **3 + 54 + 1572 = 1629 minutes**. Each arm is explicit; any safe same-source baseline reuse must be reported as measured savings, not silently subtracted. This large post-land obligation should be budgeted as its own companion; the ordinary Code manifest stays narrow.
- Savings against the landed ordinary manifest: **0 minutes**; its nine narrow rows were already minimal. No whole-Unit run, full assembly, duplicate old landing suites or repeated success run is added. Mutation has no prior accepted numeric baseline, so claimed savings = **0**.

Design census: **guards=106, mapped=106, missing=0, duplicate PC maps=0**.
All 106 PCs and all 129 arms are selected obligations; executed now = **0**
(stage is TestDesign). “All PCs run” is the post-land Mutation completion gate,
not a claim for a plan without implementation. Code must return concrete binding
for every case/variant/assertion or retain it as **binding required**; Review may
not call an unbound payload covered. A structurally unreachable required seam
routes back to Plan; an actual operator choice routes to Decide. No such choice
is needed to hand off this design. Next Code is sequential after CARD-1380 S2.

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
