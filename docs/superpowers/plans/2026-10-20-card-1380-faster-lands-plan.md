# CARD-1380 / CARD-1379: faster lands and dispatch progress

Date: 2026-10-20. Plan and verification design are folded together by the dispatch brief.
Next stage: Code. No builds or tests were run to produce this plan.

Baseline inspected: `9e78a08880cb300dab6fc3bedbefc1a47eed3284`.
Plan branch: `feat/card-task-a42e18fb`.
Read both live Antiphon cards, the landing sections of `docs/orchestration-loop.md`,
`docs/project-context.md`, `docs/ops-http.md`, `docs/testing-and-build.md`,
`docs/release-gates.md`, and the applicable delivery invariants in
`docs/session-runtime-invariants.md`.

The operator approved all four CARD-1380 proposals. Implement warm artifacts first,
then the non-code skip, then receipt-backed reuse. Split the lease work into a
preparatory safety slice and an activation slice. Follow with dispatch scheduling,
restart ordering and diagnostics from CARD-1379. Each slice below is 30–60 minutes
of Code work, including its focused verification; do not combine the whole plan
into one Code dispatch. Independent ordinary Review is still required.

## Ground truth

| Card assumption or observation | What the inspected code does | Design consequence |
|---|---|---|
| Every land builds in a fresh GUID directory. | `AgentTaskLandService.VerifyUnderSlotAsync` uses a fresh temp path unless `artifactsPath` is supplied. `LandingVerificationCorrelation` already has that optional field; the protocol does not supply it. | Reuse the existing plumbing. Change the production caller, not just the static helper's test default. |
| Every land runs a verification build. | `AgentTaskLandingProtocol.RunAsync`, Prepared phase, already skips with `base_unchanged` when the rebased SHA is unchanged and no test filter is set. Exact remote containment also skips. | Replace the unqualified `base_unchanged` shortcut with explicit policy. Keep independent containment semantics. Tests must advance the target when demonstrating today's cold-build/non-code behavior. |
| The build could use an isolated candidate worktree. | Schema 3 already uses one persistent, locked, detached `LandWorkspace` per common Git directory. `EnsureAsync` resets it and runs `clean -fdx`; it is shared across successive lands, not unique per operation. | Artifacts must be outside that worktree. A second lifetime lock must prevent another land from resetting it during unlocked verification. |
| Final Review always means a validated checkpoint receipt. | `LandApproval` accepts some unlatched caller-only approvals. Clean review rows bind a SHA/clean assertion; explicit Final/Full is required only in certain paths. `ReviewEvidenceCorrectionService.ValidateLinkAsync` checks retained receipt custody for correction-linked outcomes; ordinary outcomes need not have such a link. | Never infer imported execution evidence from prose or `ReviewedSourceClean=true`. Receipt reuse is optional and narrower than ordinary land approval. Missing reusable evidence means build, not fabricated approval. |
| Identical trees imply identical build inputs. | `Directory.Build.props` generates `SourceRevisionId` from `git rev-parse HEAD`. Different commits with the same tree can produce different version metadata. | Receipt reuse accepts this limited master-only difference; RC builds still stamp their own commit. Do not disable version stamping to improve timings. |
| No build inputs means no C# or project-file changes. | `server/Antiphon.Server.csproj` embeds `Bundles/*.md`, presets and fragments. Test projects embed a JSON file under `docs/superpowers/plans`. Scripts, resources, client files, configuration and package inputs also matter. | Use a narrow documentation allowlist with default-to-build, not a C# extension denylist or a blanket Markdown exemption. |
| Yielding gives dispatch a window. | `RunRequestAsync` holds one repository lease and one Git read-cache scope through the protocol. `YieldToDispatchAsync` only yields before acquisition, bounded by `LandYieldToDispatchMaxSeconds` (90 by default). There is no dispatcher wakeup/acknowledged handoff. | Keep bounded yielding, add a targeted wakeup and a fair handoff. A shorter sweep or a sleep is insufficient. |
| Releasing the file lock permits dispatch during builds. | `RepositoryMutationLease.AcquireAsync` also checks `RepositoryChildJournal`. The verifier creates an untagged journal that fences all admissions until child exit is acknowledged. | A live, scoped verification reservation and purpose-aware journal admission are required; simply disposing the lease does not work. |
| Dispatch retries every six minutes. | The hosted dispatcher awaits an entire serial `TickAsync`, using `PollIntervalSeconds`; six minutes is the incident observation, not a hard-coded lease retry timer. Start-ref fetching separately polls for up to 30 seconds at task creation, before a task row exists. | Measure tick/attempt delays separately. Prioritize lease waiters at safe boundaries; preserve the independent create-time fetch deadline. |
| Startup should replay Held lands FIFO. | `SweepAsync` loads pending tasks with no ordering. A separate hosted reader can consume new channel arrivals before replay completes. The queue tracks channel order, not durable request order. | Order by durable request time with a tie-breaker and put a startup barrier before consumption. Sorting the query alone is insufficient. |
| The land worker is always an unknown owner. | Production registers one singleton lease provider, and land acquisition tags task/purpose. Lookup is process-local, has no request/phase fields, and deliberately returns Unknown for external ownership. | Add request/phase attribution and qualification tests; do not replace genuinely unknown owners with a guessed Running database row. |
| Notices reflect the time they arrive. | `LandNotificationPayload` freezes event text/digest. Monitor text contains an observation timestamp, but queue delivery can happen much later. Non-completion receipts normally compare the immutable body. | Add a separately frozen delivery envelope for Held/Aged notices, with observation age at the first delivery attempt; preserve payload and receipt identity. |
| Master is the release gate. | The release owner specifies a long-lived `rc/YYYY-MM-DD` line and product hosts running its tip. It also warns that a checked-in release job is not proof of qualified scheduled execution. | Optimize master integration only; require the actual RC gate/adoption process. Do not claim a qualified automatic release gate or live activation from this plan. |

The supplied desktop log is incident evidence from the brief, not a locally
reproduced timing result. Record this distinction in the implementation report.

## Decisions

**D-1 — Stable, private artifacts per canonical repository.** Use
`<git-common>/antiphon/land-verify/v1/<toolchain-key>/` for build artifacts. The key
includes SDK identity, OS/architecture and build configuration; it does not include
the task, request, candidate SHA or a per-land GUID. Resolve paths locally from Git,
never from a fleet hostname. Keep one current warm path for a matching toolchain.
Use a verifier-owned marker and reject links, a foreign marker, or an unexpected
pre-existing root. Retain such paths and fall back to a newly owned cold directory
with a diagnostic; never recursively erase or silently adopt them.

Introduce an OS-backed per-repository land-execution lock before the repository
mutation lock. It protects the persistent land worktree and artifacts for the
whole operation, even after D-5 releases the mutation lock. A busy execution lock
holds the request without consuming an attempt. Keep outputs after a successful
land. Failed/canceled builds mark the cache unusable; the next attempt creates a
new owned generation rather than deleting uncertain output. No broad cache janitor
is part of this card. `Landing:ReuseVerificationArtifacts` defaults true; false selects owned cold
outputs for diagnosis. Reject a global cache across repositories and in-worktree
outputs: the former collides, and `clean -fdx` destroys the latter.

Each selected-test run gets a fresh owned results directory and exact TRX path
using the existing `--results-directory` convention. Never search a persistent
cache recursively for a fixed `landing-verification.trx` filename: that would let a
stale passing report authorize a failed/no-report run.

**D-2 — Conservative skip policy, master only, with an explicit opt-out.** Add
`Landing:ForceVerificationBuild` (default false). True disables all pre-publication
build skips, including the existing `base_unchanged` path. A nonempty `-Verify`
filter also forces the build and selected tests. Exact remote containment remains
an AlreadyPresent result: it performs no new publication and does not promise a
new build. Document this distinction.

For new publication, evaluate policy after the rebase, against the pinned
`TargetBeforeSha` and `RebasedSourceSha`. Initially enable skips only for
`refs/heads/master`; other targets, including `refs/heads/rc/*`, build. An empty
diff is eligible for the non-code rule, subject to the same target/override checks.
Keep all approval, source identity, push CAS, confirmation and cleanup checks.
This is a build optimization, not an advisory-document approval exemption.

**D-3 — Non-code is an allowlist.** Admit regular-file additions, edits and
deletions of `docs/**/*.md` and the root `README.md`, only in this repository's
recognized build graph. Everything else builds. In particular build for bundles,
`.cs`, all project/solution formats, `global.json`, `Directory.Build.*` (including
`.rsp`), `.props`, `.targets`, NuGet/package locks, `package.json`, client source,
scripts, resources, JSON, configuration, submodules, symlinks and type changes.
For renames, evaluate both names and modes. Parse a NUL-delimited raw/name-status
Git diff with explicit object IDs; malformed/truncated/failed reads cannot skip.

Bind the classifier to an audited build-graph fingerprint: tracked project,
props/targets and build entrypoint files identifying the current documentation
allowlist. If the candidate graph differs from the audited fingerprint, build
until the allowlist/fingerprint is explicitly reviewed and updated. This avoids a
later target embedding `docs/*.md` while an old classifier still calls it non-code.
Use hashes of those specific inputs, not the entire tree. Unknown repositories
build. Do not invoke a full MSBuild evaluation merely to avoid a build.

Safety: this can miss documentation runtime/contract defects and target defects
already present beneath the docs change; a solution compile does not generally
prove those semantics. Ordinary Review/checkpoint obligations stay in place, and
RC qualification checks the release candidate. The accepted omission is the
redundant master compile, not the release gate or required docs tests.

**D-4 — Receipt reuse requires actual retained proof and equal trees.** On master,
with no forced build or test filter, permit `review-tree-identical` only when:

1. The currently bound evidence passes `LandApproval.LoadUsableEvidenceAsync`,
   is unsuperseded Clean Final/Full and asserts clean source.
2. A server-validated retained checkpoint bundle is linked to that exact evidence,
   subject/ref/repository and reviewed commit. Validate retained bytes/digests,
   selection completeness, nonzero passing required executions, clean source and
   build-source provenance. Reuse the correction validator's primitives.
3. Both commits resolve to full tree IDs and
   `ReviewedSourceSha^{tree} == RebasedSourceSha^{tree}`.

The first implementation consumes the existing correction-linked retained evidence
route. Ordinary Review rows without an equivalent validated retained link build;
do not add an unsafe prose parser, require all reviews to be reissued, or invent a
new broad evidence-import system in this slice. Expose `receipt-unavailable` as a
build decision diagnostic so its hit rate is measurable. Invalid evidence that
already invalidates land approval still refuses; evidence that is merely absent or
ineligible for reuse falls back to building.

Replace `base_unchanged` with this policy; same commit alone is not sufficient.
An equal tree can also occur across different commit histories, so the card's
"only the first land" claim is not a condition. Conversely, unrelated source
changes underneath usually change the tree and force a build.

Safety: this reuses the reviewed execution's covered scope; it does not establish
new whole-solution coverage, a different platform result, a fresh dependency
restore, or the candidate's SHA-derived assembly version. Those are accepted
master-only omissions under the operator's proposal, with actual RC qualification
still required. A receipt from an Interim review, dirty build, zero-test selection,
untrusted producer or different tree never qualifies.

**D-5 — Separate land lifetime from repository mutation, with limited overlap.**
Hold the land-execution lock throughout preparation, verification, publication
and cleanup. Hold the existing repository mutation lease only during the mutation
phases. During the verification window allow only typed preflight acquisitions
for an origin start-ref fetch or creation/claim of a distinct managed Worktree.
Do not admit Shared writers, repair/adoption, SourceLanding preparation, cleanup,
reset, prune, gated commits or settlement synchronization through this exception.
Prepared remote mirror launch remains on its existing nonmutating path.

An owner tag is advisory, so do not turn `Purpose == "dispatch"` into authority.
Add a narrow acquisition API/capability whose callers check workspace mode, source
mode and protected paths. Fetch remains before task creation and outside row
transactions. Dispatch claim continues to use existing concurrency/eligibility
checks, with its own short repository lease; it must not hold that lease for agent
startup or remote network preparation.

A durable verification reservation binds common directory, server process
generation, execution-lock acquisition nonce, request/operation, protected land
worktree, candidate SHA and current phase. It is created while holding both locks.
Only a matching, live verifier child and live reservation permit the narrow
preflight exception. Old, unknown, dead, reused-PID, malformed, completed-but-not-
joined and unmatched journal records retain the ordinary fence. No addition of
`land-verify` to the unconditional `LiveNonFencingPurposes` set.

Set `Landing:AllowDispatchDuringVerification=true` after the safety plumbing is
complete; false retains continuous repository ownership. No fleet-specific path,
second land worker, or arbitrary concurrent repository writer is introduced.

**D-6 — Fair handoff, not a longer yield.** Preserve the configured 90-second
maximum admission yield. Add a coalesced dispatcher wakeup on opening the
verification window and on releasing a mutation lease with waiters. The serial
dispatcher checks pending lease-waiter hints between individual task attempts,
between sweep phases and before its ordinary scans; a targeted pass runs normal admission for those tasks,
without repeating all unrelated sweeps. A wake is a hint, not authority.

Use one oldest-waiter reservation per repository turn, with an acknowledgment or
the existing yield-budget expiry. A dispatcher ready to retry registers for the
next release before attempting acquisition; its bounded wait remains registered
while the current mutation owner finishes. Later lands cannot steal that ready
waiter's unconsumed turn. Apply priority at acquisition, not merely in Held text.
Count this against the request's original yield budget, rather than resetting it
on every sweep. Clear turns on dispatch success, cancellation, loss of eligibility
or deadline. Cap each targeted batch (eight) before returning to ordinary work.
Do not concurrently invoke `TickAsync` or bypass task/concurrency row locks.
A current long Git call can still delay a safe boundary; log that delay instead
of promising a hard dispatch SLA. The create-time start-ref poll retains its
30-second overall budget and gets compatible access during the verification window.

**D-7 — Durable FIFO for eligible lands.** Bootstrap the queue from pending
requests sorted by `(RequestedAt, Id)`, not task enumeration or replay time.
New requests remain accepted/persisted during boot but the reader cannot execute
them until the first successful replay reconciles them with older requests.
Maintain this ordering for subsequent replay/insertion, with one reader and
request-identity deduplication. A genuinely Held/not-yet-eligible request does not
block every other repository: exclude it until its next evaluation is due, then
restore its original ordering key. "FIFO" means oldest eligible request, not
waiting forever behind an external writer or NeedsResolution. A failed startup
scan leaves the barrier closed and retries with visible diagnostics.

**D-8 — Attribution and aged observations are evidence, not ownership.** Extend
live lease diagnostics with request, operation, acquisition ID and phase:
`preparing`, `waiting-build-slot`, `verifying`, `reacquiring`, `publishing`,
`cleanup`. During verification distinguish the land-execution owner from the
repository mutation owner, which may be a dispatch. Publish these snapshots
through the existing hold/monitor paths. Unknown external ownership stays Unknown;
historical Running rows are only labeled hints. Old release callbacks cannot erase
another acquisition's identity.

For Held/Aged notices retain the immutable event/body/digest and append a
delivery envelope frozen at the first actual send attempt:
`observedAt`, `firstDeliveryAttemptAt`, `ageAtFirstDeliveryAttemptSeconds`, and
`historical observation; current state may differ`. Preserve that rendering on
retries; never describe enqueue time as delivery time or recompute a digest after
typing. Keep transcript matching, spill custody and the existing permitted receipt
types. Do not suppress unresolved terminal notifications or claim this fixes all
notification latency.

**D-9 — Shipping and placement.** These are master-first changes. RC adoption
requires the operator's explicit go, then the normal reviewed adoption/gate and
canonical AppHost restart. Approval of this implementation plan does not supply
that later RC go.

The required live GETs were read on 2026-10-20: runner-defaults revision 2 has an
eligible Linux default; the catalogue also reports eligible Windows capacity.
Treat that as a snapshot, not a placement pin. Refresh both routes before dispatch;
omit `-Runner` and ordinary `-Platform`. Only the native lock qualification lane
requires `-Platform Windows`; `-Platform Any` removes a previous OS pin. The
checkpoint Group prefixes below name lanes without embedding a host location.

## Lease redesign and deadlock analysis

The S5 activation sequence is:

1. Acquire land-execution lock **L**, then repository mutation lease **R**.
   Perform admission/source checks, prepare/rebase, pin the candidate and persist
   Prepared. Create the verification reservation. No database transaction spans
   the later wait/build.
2. End the current `ILandingGit.BeginOperationScope()` before releasing **R**.
   Open the preflight window and issue a nonblocking dispatcher hint.
3. Hold **L** only while acquiring build slot **B**, compiling and optionally
   testing. Keep the protected worktree at the pinned candidate. A child journal
   remains durable; only matching live verification is compatible with preflight.
   Dispatch takes **R**, performs its limited operation and releases **R**.
4. Join the verifier child and drain streams; close its journal. Release **B**
   before reacquiring **R**. Close new preflight admission and use the bounded
   fair turn for reacquisition; never steal a held lease. Cancellation closes
   admission, joins children and releases owned resources in reverse order.
5. With fresh **R** and a fresh Git read scope, reload request/operation/approval,
   verify candidate HEAD/tree and tracked cleanliness, protected worktree identity,
   source refs, destination and remote target. Check relevant untracked source
   inputs as well; build-created files cannot become accepted source silently.
   Persist Verified only for the candidate actually checked. Publish via the
   existing observed-target/plain-push CAS and existing target-race retry budget.
6. If the target changed, a new prepared candidate re-enters skip evaluation or
   build; no result transfers across different trees by assumption. Every cache
   and registration observation made before the unlock is invalidated. Published
   recovery and cleanup keep their current authority checks.

Lock order is **L → R** for lands; dispatch owns **R** only and never asks for
**L** or **B**. Verification holds **L → B** with **R** absent; **B** is released
before the next **R** acquisition. Database row locks are not held across waiting
for these locks or children. Thus there is no R→L, R→B→R, or row-lock→R cycle.
Use nonblocking acquisition/cancellation-aware bounded waits, never synchronous
callbacks that await the dispatcher while holding a lease.

The existing global land reader stays serial; **L** also protects against another
process and crash recovery. On server death, an OS lock disappearing is not proof
that a verifier child exited. Its unmatched/dead-owner reservation/journal fences
reuse and mutation until existing explicit child recovery proves custody. A
restart never resets the land worktree while a prior child is unaccounted for.

Build/test execution is still trusted reviewed code, not a sandbox. The preflight
exception permits only disjoint workspace/ref preparation; it does not grant
arbitrary MSBuild targets permission to mutate shared refs. Revalidation detects
observable candidate/source changes and refuses publication. Do not claim an
atomic defense against hostile same-account filesystem writers.

## Code slices

Paths marked **new** are proposed. Existing test helpers should supply real scratch
Git repositories, isolated database schemas and controlled barriers. Use the
assembly-local `ParallelLimiter<ProcessSpawnLimit>` for process-spawning tests.
Do not start the real server against a production runner.

| Slice | Time | Dependency | Concrete changes and tests |
|---|---:|---|---|
| S1 — warm artifacts and fresh results | 60 min | none | Change `AgentTaskLandService.cs`, `AgentTaskLandingProtocol.cs`, `LandingVerifier.cs`, `ILandingVerifier.cs`, `LandingSettings.cs`, `server/Program.cs`; add `ILandExecutionLease.cs` and infrastructure `LandExecutionLease.cs`/`LandVerificationArtifacts.cs`. Acquire L before R; pass a stable owned artifacts path through the existing correlation. Fresh per-attempt TRX. Add **new** `tests/Antiphon.Tests/Application/LandWarmArtifactsTests.cs`; retain `LandVerificationBuildSlotTests.cs` semantics. Document config/custody in `docs/orchestration-loop.md`. No repository unlock yet. |
| S2 — non-code skip | 45–60 min | S1 | Add **new** `Application/Services/LandVerificationPolicy.cs` and the small audited build-input fingerprint inventory it consumes; change protocol Prepared decision, settings, verification outcome recording and docs. Add **new** `Application/LandNonCodeSkipTests.cs` under the test project; update affected expectations in `AgentTaskLandStageOutcomeTests.cs`. Give that class an explicitly injected recording verifier for its stage-outcome scenarios, preserving its pass/fail assertions and other callers' defaults; it currently launches real nested builds. Replace the old unconditional base shortcut with build unless the explicit policy succeeds. |
| S3 — validated equal-tree reuse | 60 min | S2 | Extend `LandVerificationPolicy.cs`, `LandApproval.cs` and receipt-validation helpers in `ReviewEvidenceCorrectionService.cs`/`CheckpointReceiptValidator.cs` through an Application I/O seam if needed. Consume existing retained correction evidence without manufacturing an outcome. Add **new** `Application/LandReviewedTreeSkipTests.cs`; retain `InterimVerificationLandGuardTests.cs`. No general review-import redesign. |
| S4 — verification reservation and limited admission | 60 min | S1 | Extend `IRepositoryMutationLease.cs`, `RepositoryMutationLease.cs`, `RepositoryChildJournal.cs`, `ILandExecutionLease.cs`/implementation and `LandingVerifier.cs`. Add typed preflight capability and live reservation/journal validation. Add **new** `Infrastructure/LandVerificationReservationTests.cs`. Keep the production verification window closed in this slice. Extend owner/recovery fixtures only where needed. |
| S5 — release R during verification | 60 min | S2–S4 | Refactor `AgentTaskLandService.cs`/`AgentTaskLandingProtocol.cs` into the acquire/prepare/verify/reacquire sequence above; split Git cache lifetimes. Wire narrow preflight in `StartRefAvailability.cs`, `AgentTaskDispatcher.cs`, `WorktreeManager.cs`; keep Shared/repair/cleanup acquisitions fenced. Add **new** `Application/LandVerificationConcurrencyTests.cs`; exercise `AgentTaskLandTargetRaceTests.cs`. Update `docs/orchestration-loop.md`. This is the lease-redesign activation slice. |
| S6 — dispatch wakeup and bounded handoff | 45–60 min | S5 | Change `RepositoryLeaseWaiters.cs`, `AgentTaskDispatcher.cs`, `AgentTaskDispatcherHostedService.cs`, land yield/release sites and DI; add a small **new** `DispatchLeaseWakeup.cs` coalesced signal. Prioritize targeted waiters between tasks; don't run a second tick in parallel. Add **new** `Application/LandDispatchProgressTests.cs`; retain `AgentTaskLandDispatchYieldTests.cs` and `WorktreeStartRefDispatchTests.cs`. Record tick/wakeup-to-attempt latency. |
| S7 — restart FIFO | 45 min | S6 | Change `AgentTaskLandQueue.cs`, `AgentTaskLandService.SweepAsync`, `AgentTaskLandHostedService.cs` and `AgentTaskLandSweepHostedService.cs`. Add a startup replay barrier, stable request ordering and due/eligibility-aware requeue. Add **new** `Application/LandRestartOrderingTests.cs`; retain `AgentTaskLandSweepTests.cs` and `AgentTaskLandQueueVisibilityTests.cs`. Use existing RequestedAt/Id; no ordering migration is needed. |
| S8 — owner and phase visibility | 30–45 min | S5–S7 | Extend lease owner snapshots and phase updates, `DispatchHoldDetails.cs`, `AgentTaskLandMonitorService.cs`, dispatcher/land hold formatting and `docs/ops-http.md`. Add **new** `Application/LandLeaseDiagnosticsTests.cs`; retain `RepositoryMutationLeaseOwnerTests.cs`. Distinguish execution-lock owner, mutation-lock owner and unknown external ownership. |
| S9 — age at first notification delivery | 60 min | S8 | Add a separate nullable `ObservationDeliveryJson` snapshot on `AgentTaskLandNotification` and an EF-generated migration/model configuration. Change `LandNotificationPayload.cs`, `SessionMessageQueueService.cs` first-attempt rendering and `AgentTaskLandNotificationService.cs` receipt reconciliation; use a **new** `LandObservationDelivery.cs` helper. Do not overload `CompletionDeliveryJson`'s TaskCompletion-only contract. Add **new** `Application/LandObservationAgeTests.cs`; retain the narrow queued-receipt and notification-recovery tests. Update delivery invariants and operations docs. |

Update only the living owner docs, not generated `docs/cards/` files. The source
changes are not authorized in this Plan task. S1/S2 can be reviewed and landed
first to deliver the largest gains without waiting for the concurrency changes.
Code must state its exact slice scope and pushed SHA; retain historical red results.

## Verification design

Use real observable behavior at the existing service/protocol boundaries, with
fake compiler/slot responses where compiling is not the behavior under test. Do
not test a helper's constant against itself. Build-slot waits and concurrency tests
use injected clocks/barriers, not multi-minute sleeps. All child processes are
owned and joined. No full solution/suite run is added to this closed scope.

### Witnesses and ordinary assertions

The named methods below are proposed tests, not claims that they already exist.
Each witness must compile on its test-only commit and fail at the stated assertion
against the preceding production behavior. If scaffolding is needed, commit inert
plumbing first without implementing the behavior. A build/fixture failure or zero
selected tests is not red evidence.

| ID / slice | Exact red witness method (in the new class named above) | Required narrow green coverage |
|---|---|---|
| V-1 / S1 | `C1380_WarmPathIsReusedAcrossTwoRebasedLands` — advance the target so both lands invoke the verifier; assert identical build artifacts paths and an owned cache marker. Today's paths differ. | At least six named tests: witness; different repositories/toolchains isolate; foreign/link path retained; stale TRX cannot pass the second run; cancel/failure releases locks and quarantines cache; slot grant/CPU cap and failed build prevent publication. Assert build and test share artifacts but use fresh result custody. |
| V-2 / S2 | `C1380_DocsOnlyRebasedLandRecordsNonCodeSkip` — docs change rebased onto advanced target: assert no slot/compiler invocation and persisted Verify/Skipped `non-code`. Today it builds. | At least seven tests: docs skip; `C1380_CSharpChangeBuilds` proves `.cs` invokes build; `C1380_FailedBuildRefusesPublication` proves compiler failure refuses/no push; bundle Markdown and docs JSON build; rename/delete/type-change cases; graph drift/unreadable diff builds; ForceVerificationBuild, `-Verify` and RC target build. Check StageOutcomes and final remote SHA, not only a boolean policy return. |
| V-3 / S3 | `C1380_EqualTreeReceiptSkipsDifferentCommit` — retained valid Final/Full bundle for a different commit with equal tree; assert skip/no verifier. Today differing commit IDs build. | At least six tests: witness; different tree builds; absent receipt builds even for same commit; dirty/zero/untrusted/incomplete evidence cannot skip; superseded or invalid approval refuses; override/filter/RC build. Include identical tree with differing SourceRevisionId metadata in fixture reasoning. |
| V-4 / S4 | `C1380_LiveReservedVerifierAllowsOnlyPreflight` — real provider with live matching reservation/journal: preflight acquires while ordinary mutation remains refused. Today's journal blocks both. | At least six tests: witness; another land cannot take L/reset/cache; unknown/start-intent/dead/reused PID fences; mismatched repository/nonce fences; release/acquisition race retains new owner; cancellation/restart retains unresolved child custody. Use independent provider instances, not one fake lock. |
| V-5 / S5 | `C1380_DispatchPreflightCompletesBeforeVerifierReleased` — pause actual land at its verifier barrier and complete a scratch start-ref fetch plus distinct worktree creation before releasing it. Today R blocks them. | At least six tests: witness during build-slot wait too; target moves while unlocked and forces fresh candidate validation; source/approval/candidate tamper refuses; child failure/cancel cannot publish; no stale registration cache after dispatch adds a worktree; restart with unfinished verifier fences reset. Existing target-race tests retain bounded retries/plain push behavior. |
| V-6 / S6 | `C1379_LeaseWaiterRunsBeforeNextQueuedLand` — enqueue two lands and a held dispatch; finish/open the first land's window and assert actual preflight/claim before the second reacquires. A yield string alone does not satisfy it. | At least five tests: witness; idle wake; hint during a long tick serviced at the next task boundary; stale/ineligible/canceled waiter clears; expiry permits land and coalesced wakeups do not double-claim or exceed concurrency. Fake clock verifies the unchanged maximum yield budget. |
| V-7 / S7 | `C1379_RestartReplaysOldHeldBeforeNewRequest` — persist requests in reverse insertion/time order, recreate services, race a new request with replay, and assert execution by durable age. | At least five tests: witness; equal timestamps tie by Id; failed replay admits nothing; duplicate/stale request identity never executes; ineligible/NeedsResolution does not block eligible work and reeligible Held keeps its original age. Snapshot positions must agree with the execution roster. |
| V-8 / S8 | `C1379_BusyReasonNamesLandRequestAndPhase` — paused land's diagnostic must name exact request/operation and waiting-build-slot phase, not only task/purpose or unknown. | At least five tests: witness; verification distinguishes free/dispatch-held R from land-held L; external holder stays unknown; phase/release race cannot erase new acquisition; monitor snapshot observation time and nullable legacy fields remain truthful. |
| V-9 / S9 | `C1379_HeldNoteShowsAgeAtFirstDeliveryAttempt` — create Held/Aged notice, delay queue consumption by 25 injected minutes, then assert received envelope age and full transcript-confirmed receipt. | At least six tests: witness; enqueue-versus-delivery delay; attempt retry uses identical frozen bytes; restart/spill/batch preserves envelope membership/digest; modified/partial body is not receipt; legacy/null snapshot retains old matching and a future observation reports clock skew rather than a fabricated age. |

R-1: preserve build-slot accounting and failure refusal.
R-2: preserve stage outcome/publication semantics.
R-3: preserve Final/Full approval and evidence custody.
R-4: preserve child-journal fences and cross-process lock exclusion.
R-5: preserve source/candidate identity, cache invalidation, target CAS and cleanup.
R-6: preserve dispatch caps, task claims and finite yielding.
R-7: preserve pending-request replay/deduplication.
R-8: preserve unknown-owner truth and acquisition identity.
R-9: preserve immutable notification/receipt/spill semantics.

`Verify: Skipped(non-code)` and `Verify: Skipped(review-tree-identical)` mean
`Stage=Verify`, `Outcome=Skipped`, `Detail` equal to the named reason. Preserve
candidate SHA/operation/request in landing evidence and receipt identity/tree IDs
in structured decision diagnostics. A failed executed build is never Skipped.
Do not claim `VerificationPassed=true` for a skip.

### Execution contract and lanes

The table deliberately includes a red and green row per slice. `S1R` etc. mean
the pushed test-only commit; `S1` etc. mean the pushed implementation commit.
Run one selected row/run at a time with `--rows CP-n`; do not run the whole
manifest on a red commit. Odd rows CP-1 through CP-17 must return a genuine
assertion failure from exactly the witness (normally checkpoint exit 1); record
that historical outcome as expected red. The tool does not turn expected red
into green. Even rows are the ordinary gate and include the witness again.
CP-19 is additional native Windows qualification of S4/S5.

All `any-*` groups use the current eligible default lane without a host or OS pin.
`windows-lease-native` runs on an eligible Windows lane because file-sharing and
process-generation recovery require native evidence there. The Windows filter
must execute, not skip; ordinary rows remain portable. Re-read runner defaults and
catalogue before commissioning that lane.

Use the checkpoint runner, through `scripts/build-slot.ps1` for its launcher:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1380-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-20-card-1380-faster-lands-plan.md --rows CP-2
```

The row driver takes its own build slot. Continue `wait --run <run-id>` until a
terminal exit; 75 means still running. Report every row's unchanged CHECKPOINT
line, source SHA, executed/passed/failed/skipped counts and slot evidence. Do not
edit source while a run is live. Slot timeout is not permission to run unleased.
No build output reuse crosses a source commit. Remove only owned `bin-c1380-*`
checkpoint outputs after use; generated TRX/JSON/logs remain ignored.

Code and Review run `scripts/check-evidence-diff.ps1` across their recorded
base..pushed-tip range. No performance claim can come solely from fake process
tests. Post-activation measurement below supplies the real timing evidence.

The card's additional red controls for the existing safety rules belong to the
separate post-land SourceLanding Mutation companion, not these ordinary Code
runs. PC-1 forces the `.cs` policy case to return a skip; run only
`/*/*/LandNonCodeSkipTests/C1380_CSharpChangeBuilds*` and require the missing-build
assertion to fail. PC-2 bypasses the protocol's failed-verifier refusal; run only
`/*/*/LandNonCodeSkipTests/C1380_FailedBuildRefusesPublication*` and require the
publication/refusal assertion to fail. Restore each change and run the identical
method green. Use the bound pack, host slot gate and external evidence root; no
snapshot commits/pushes. Compiler/fixture failures and zero tests are not control
success. The docs-only pre-fix witness is CP-3. Retain all three facts when claiming
the card's "one red witness per rule" acceptance.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1R | `tests/Antiphon.Tests -> bin-c1380-1r/` | any-warm-red | `/*/*/LandWarmArtifactsTests/C1380_WarmPathIsReusedAcrossTwoRebasedLands*` | V-1 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1380-1/` | any-warm-green | `/*/*/(LandWarmArtifactsTests*)\|(LandVerificationBuildSlotTests*)/*` | V-1, R-1 | all listed, at least 6 new tests, 0 failed/skipped | 6 | 6 |
| CP-3 | S2R | `tests/Antiphon.Tests -> bin-c1380-2r/` | any-noncode-red | `/*/*/LandNonCodeSkipTests/C1380_DocsOnlyRebasedLandRecordsNonCodeSkip*` | V-2 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1380-2/` | any-noncode-green | `/*/*/(LandNonCodeSkipTests\|AgentTaskLandStageOutcomeTests)/*` | V-2, R-2 | all listed, at least 7 new tests, 0 failed/skipped | 7 | 7 |
| CP-5 | S3R | `tests/Antiphon.Tests -> bin-c1380-3r/` | any-review-tree-red | `/*/*/LandReviewedTreeSkipTests/C1380_EqualTreeReceiptSkipsDifferentCommit*` | V-3 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1380-3/` | any-review-tree-green | `/*/*/(LandReviewedTreeSkipTests\|InterimVerificationLandGuardTests)/*` | V-3, R-3 | all listed, at least 6 new tests, 0 failed/skipped | 6 | 7 |
| CP-7 | S4R | `tests/Antiphon.Tests -> bin-c1380-4r/` | any-reservation-red | `/*/*/LandVerificationReservationTests/C1380_LiveReservedVerifierAllowsOnlyPreflight*` | V-4 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c1380-4/` | any-reservation-green | `/*/*/(LandVerificationReservationTests\|RepositoryMutationLeaseDescribeTests\|RepositoryChildRecoveryTests)/*` | V-4, R-4 | all listed, at least 6 new tests, 0 failed/skipped | 6 | 8 |
| CP-9 | S5R | `tests/Antiphon.Tests -> bin-c1380-5r/` | any-overlap-red | `/*/*/LandVerificationConcurrencyTests/C1380_DispatchPreflightCompletesBeforeVerifierReleased*` | V-5 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-10 | S5 | `tests/Antiphon.Tests -> bin-c1380-5/` | any-overlap-green | `/*/*/(LandVerificationConcurrencyTests\|AgentTaskLandTargetRaceTests)/*` | V-5, R-5 | all listed, at least 6 new tests, 0 failed/skipped | 6 | 8 |
| CP-11 | S6R | `tests/Antiphon.Tests -> bin-c1380-6r/` | any-dispatch-red | `/*/*/LandDispatchProgressTests/C1379_LeaseWaiterRunsBeforeNextQueuedLand*` | V-6 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-12 | S6 | `tests/Antiphon.Tests -> bin-c1380-6/` | any-dispatch-green | `/*/*/(LandDispatchProgressTests\|AgentTaskLandDispatchYieldTests\|WorktreeStartRefDispatchTests)/*` | V-6, R-6 | all listed, at least 5 new tests, 0 failed/skipped | 5 | 8 |
| CP-13 | S7R | `tests/Antiphon.Tests -> bin-c1380-7r/` | any-replay-red | `/*/*/LandRestartOrderingTests/C1379_RestartReplaysOldHeldBeforeNewRequest*` | V-7 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-14 | S7 | `tests/Antiphon.Tests -> bin-c1380-7/` | any-replay-green | `/*/*/(LandRestartOrderingTests\|AgentTaskLandSweepTests\|AgentTaskLandQueueVisibilityTests)/*` | V-7, R-7 | all listed, at least 5 new tests, 0 failed/skipped | 5 | 7 |
| CP-15 | S8R | `tests/Antiphon.Tests -> bin-c1380-8r/` | any-owner-red | `/*/*/LandLeaseDiagnosticsTests/C1379_BusyReasonNamesLandRequestAndPhase*` | V-8 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-16 | S8 | `tests/Antiphon.Tests -> bin-c1380-8/` | any-owner-green | `/*/*/(LandLeaseDiagnosticsTests\|RepositoryMutationLeaseOwnerTests)/*` | V-8, R-8 | all listed, at least 5 new tests, 0 failed/skipped | 5 | 6 |
| CP-17 | S9R | `tests/Antiphon.Tests -> bin-c1380-9r/` | any-age-red | `/*/*/LandObservationAgeTests/C1379_HeldNoteShowsAgeAtFirstDeliveryAttempt*` | V-9 red | exactly 1 executed, expected assertion failure | 1 | 5 |
| CP-18 | S9 | `tests/Antiphon.Tests -> bin-c1380-9/` | any-age-green | `/*/*/(LandObservationAgeTests\|AgentTaskLandQueuedReceiptTests\|AgentTaskLandNotificationRecoveryTests)/*` | V-9, R-9 | all listed, at least 6 new tests, 0 failed/skipped | 6 | 8 |
| CP-19 | S5W | `tests/Antiphon.Tests -> bin-c1380-windows/` | windows-lease-native | `/*/*/(LandVerificationReservationTests\|LandVerificationConcurrencyTests\|RepositoryMutationLeaseTests)/*` | V-4, V-5, R-4, R-5 native | all listed on Windows, at least 12 executed, 0 failed/skipped | 12 | 10 |
| CP-20 | S1 | CP-2 | any-warm-unit | `/*/*/*/*[Category=Unit]` | S1 Final whole Unit lane | all Unit tests, 0 failed/skipped | 1 | 8 |

S1 Code selection correction: CP-2 uses the pinned TUnit OR syntax with each
class operand parenthesized and wildcarded. CP-20 supplies the Final profile's
whole Unit obligation using CP-2's exact-source build; it adds no full-assembly
run. CP-1 remains the single historical red witness. Later slices retain their
own separate commissioning and checkpoint obligations.

## Shipment, measurement and rollback

1. Commit and push each test-only/implementation slice to its assigned task branch;
   preserve expected red evidence and obtain ordinary Review of the exact green
   source tip. Fetch `origin/master` and run
   `git merge-tree --write-tree origin/master HEAD`; do not merge/rebase the task
   branch to repair a conflict. Land through `delegate.ps1 -Land`, master first.
2. Before activation, obtain the operator's explicit go for RC adoption. Commission
   the reviewed adoption onto the current `rc/YYYY-MM-DD` line using the release
   owner's process; run its required gate. Do not push a shared target directly
   or substitute master green for RC qualification. The plan's new master skip
   policy does not skip RC lands.
3. From the canonical checkout at the approved RC tip, follow
   `docs/apphost-runbook.md` and run
   `pwsh -NoProfile -File scripts/restart-apphost.ps1`. No worktree override.
   Confirm `/api/version` SHA and capability against the approved source, then
   health. A master land alone has no production effect.
4. Measure the first normal batch after activation without generating fake live
   messaging or unnecessary production tasks. Preserve per-request phase timings:
   accepted→admitted, slot wait, compile/test, mutation-lock held time, dispatch
   wait/wakeup→attempt, rebase, push/confirm and cleanup. Compare equivalent cold
   and subsequent warm code lands; record SDK/graph/cache generation and actual
   skip/override reasons. SHA version stamping may still force many projects to
   recompile, so a stable path alone is not proof of the target speedup.
5. Acceptance: a docs-only admitted land completes in under two minutes; a code
   land using the warm cache completes in under three. Also report total
   accepted→confirmed latency including queue/slot waits, so contention is not
   hidden by the admitted metric. A queued eligible dispatch must perform its
   preflight while a paused verification is still active and get a handoff before
   the next queued land; restored eligible requests execute in durable order.
   Record at least three warm code and three docs observations when that real
   workload exists. Until then report timing acceptance pending, not achieved.
6. If timings miss, separate restore/compile/version-stamp cost from build-slot,
   Git and scheduling waits. Do not disable SHA stamping, weaken source checks or
   expand the skip allowlist to manufacture a fast result. The existing cards
   remain the record for unfinished acceptance.

Rollback controls: `ForceVerificationBuild=true` removes the master skips;
`ReuseVerificationArtifacts=false` selects cold owned output;
`AllowDispatchDuringVerification=false` restores continuous repository ownership.
Apply settings through the normal approved RC activation process. Retain existing
cache and journal evidence; a rollback is not authorization to recursively delete
outputs, kill sessions or clear unresolved child records.

This plan adds no agent session that waits for input and changes no parking policy.
Build/lease waits belong to the server operation and are cancellation-aware.
If the later RC go is pending, no new session should be kept merely to wait for it.
An existing waiting session retains its seat with parking disabled; there is no
automatic release deadline (CARD-1083).
