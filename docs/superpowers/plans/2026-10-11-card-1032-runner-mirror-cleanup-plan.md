# CARD-1032: guarded runner removal and durable mirror coordination

Plan date: 2026-10-11. Task: `771fa4ce-c6ab-4e94-a6bc-1ef22185f8cd`.
Inspected checkout: `c141d200e996dfea80e5f2eed295e1dbb2f6073b`.
Fetched target: `a91ba68a72185d57d39e87b218e459c5df2451de`; its only change
from this checkout is in `docs/session-runtime-invariants.md`.

**Premise confirmed.** The CARD-1017 foundation commit
`6929fc842c4abb4ea39af8598fc23eb1ac9957ea` is an ancestor of this checkout.
The guarded ordinary-runner operation and mirror coordinator remain absent.
This plan implements only parent S3, corresponding to the parent's **frozen**
CP-3/CP-4/CP-5 and V-5/V-6. The earlier, superseded parent table assigns CP-5
to S4; do not use that table. Parent S4/S5 remain prerequisites for activation.

The controlling specification is
[CARD-1017](2026-10-03-card-1017-completed-card-worktree-cleanup-plan.md), including
its later verification freeze. This dispatch is Plan only; **next: test-design**.
The checkpoint proposal below subdivides S3 for smaller Code dispatches. It does
not claim a newly frozen verification design or evidence that tests have run.

## Problem and scope

A completed card's local worktree and each runner mirror need independent,
durable cleanup outcomes. Current mirror removal cannot prove full task/attempt
ownership, protect ignored content, exclude concurrent use, or recover a lost
response from an external removal receipt. A local completion must not erase
mirror debt. Conversely, an offline runner must not prevent local progress.

Implement the typed runner operation, immutable mirror identity, guarded
filesystem executor, and server coordinator. Keep production execution inert:
no scheduler registration, no `WorktreeResidueSettings.Execute` change, no
production cleanup service registration/caller, and no live worktree, mirror,
runner path, branch or process deletion. Tests may remove only their own scratch
worktrees. No changes to card closing, session release, notifications, CLI/model
admission, deployment, historical cleanup policy or SourceLanding custody.

## Ground truth

Paths are relative to the inspected checkout. Proposed names are marked new.

| Card assumption | What the code does | Required consequence |
|---|---|---|
| The inert foundation has landed. | The required SHA is ancestral. `CardWorktreeCleanup`, `CardWorktreeCleanupTarget`, normalized endpoint children, migration `20261004011911_CompletedCardWorktreeCleanup`, discovery and local executor exist. `server/Program.cs` does not register these services, and the residue sweep does not call them. | Reuse that ledger; preserve the inert composition boundary. No replacement scheduler or second local executor. |
| Ordinary mirrors already have full ownership receipts. | `PhoneHomeWorkspaceMirrorRequest` carries branch/SHA/short name/repository; its response is only a path. `RunnerWorkspaceService.MirrorAsync` has no ordinary creation ledger. `RemoteWorkspacePreparer` writes the current scalar `RemoteWorktreePath`; a response after runner reassignment is logged and not recorded. | Persist a full task/attempt/store-bound creation identity and all returned bindings before they can be lost to reassignment. A short name or old path is insufficient. |
| Existing remove can be extended with optional safety fields. | `PhoneHomeContracts.cs` operation 15 takes `Path`, `Force`, optional `PublishedSha`. `RunnerWorkspaceService.RemoveAsync` uses ordinary status, optional ancestry, `git worktree remove --force`, then prune. Missing directory is treated as removed. | Add a distinct operation. Never route CardDone authority through operation 15 or treat absence without a matching receipt as completion. |
| A supplied SHA is preservation proof. | `RemoteWorkspaceService.RemoveMirrorAsync` adds SHA only for peers advertising `WorkspacePublishV1`; its fallback may use the dispatch baseline. The result is a best-effort residue string. | Require bound publication/containment evidence, exact inspected mirror HEAD, and independently matching receipt identity. Preserve legacy behavior for legacy callers. |
| The local executor can process a mirror row. | `CardWorktreeCleanupExecutor` compares desktop coordinates and calls local Git, artifact preservation and retirement/landing services. `CardWorktreeCleanupService.DiscoverAsync` intentionally adds only a local endpoint. | Add a separate mirror coordinator that consumes runner-owned facts; never apply desktop filesystem checks to a remote path. |
| The existing endpoint ledger already contains the whole request. | Endpoint fields include store, source, operation/digest, state, retry timing and component facts, but no immutable typed request payload or imported runner receipt. | Extend the ledger minimally; persist the exact request before send and receipt before publishing completion. Do not rebuild a retry from mutable task fields. |
| Session ID locks exclude path reuse. | `SessionRunnerRuntime.StartAsync` uses per-session launch locks. Startup adoption and Herdr adoption reconstruct retained processes. Different sessions can reference the same path. | A shared path/repository admission boundary must cover new launch, attach/re-adoption, mirror creation and cleanup, and survive restart. A single `List()` sample cannot authorize removal. |
| Existing safety utilities are reusable directly by the runner. | `GuardedWorktreeRemoval`, `WorktreeIgnoredContentGate` and `WorktreeNoFollowDelete` are server-side. The runner has no server project reference. Verification creation/custody metadata is specific to SourceLanding. | Implement a narrow runner-local helper with parity witnesses; do not add a runner-to-server dependency or borrow verification authority. |
| Local Complete guarantees remote cleanup. | `TaskWorktreeRetirementService` has best-effort `RemoveMirrorAsync` outside the new endpoint ledger. The new discovery service has no mirror enumeration. | Query mirror endpoints independently of local state and preserve their identity across restart. S4 later owns scheduled invocation. |
| Placement can be copied from an older plan. | Required GETs of `/api/runner-defaults` and `/api/session-runners` succeeded at 18:47 UTC after transient HTTP 502s. Defaults revision 2 has a global preference and no kind overrides. The catalogue offers eligible Linux and Windows lanes, plus an unavailable/draining entry. Card get and the documented `/revisions` read show only this Plan dispatch's Backlog-to-InProgress move, with no new policy decision. | Resolve placement again at dispatch; use lanes, not a host name or today's occupancy. No history supersedes the card's inert-S3 restriction. |

## Decisions

- **D-1 — Retain the parent's policy and split only S3.** Done-generation
  authority, preserved reports/deliverables, terminal and quiescent ownership,
  contained source and protected ignored files remain mandatory. Reject using
  terminal status, elapsed time or a task-branch push as cleanup authority.
- **D-2 — Give mirrors durable creation identity.** Add a normalized
  `TaskWorktreeMirrorBinding` record on the server and an ordinary binding ledger
  outside runner deletion roots. Bind full TaskId, attempt, binding ID, runner
  store, repository identity, common/admin directories, exact path/ref and
  creation baseline. Optional additive mirror request/response fields are used
  only with a new binding capability. Old servers/runners retain ordinary launch
  compatibility; missing binding means cleanup debt. Reject retroactive ownership
  inferred from `task-<short-id>`, an equal SHA or a caller-supplied path.
- **D-3 — Preserve every binding, including late responses.** The preparer
  commits a valid returned binding for the originating attempt and runner even
  if assignment changes during I/O. Updating the current task path still requires
  the current assignment/attempt match. Different stores and attempts remain
  distinct. Lost responses replay the same preparation identity and retrieve the
  runner's record; they do not invent a new owner. Historical unbound mirrors stay
  held. No legacy directory scan or operator adoption mechanism is added here.
- **D-4 — Separate inspection, immutable intent and removal.** Add typed
  read-only inspection and guarded-remove operations, with new unused enum values
  selected against the implementation base. Removal has no Force bypass and never
  calls legacy `RemoveAsync`. Capabilities describe the actual installed service;
  the guarded service is not composed into production in this card. Tests compose
  it explicitly. Unknown/unsupported responses leave visible debt and never block
  ordinary task admission merely because cleanup is unavailable.
- **D-5 — Preserve the mirror's exact source.** Inspection returns HEAD H, not an
  assumed desktop tip. The server proves H is contained in the configured published
  destination, or uses the exact confirmed landing record and its rebase mapping
  from H to the preserved source. Include destination fingerprint/ref, observed
  target SHA, proof kind/identity and relevant source SHAs in the immutable request.
  The runner validates local identity and fresh remote containment; a bare SHA,
  unique task-branch push, unrelated landing or stale destination is insufficient.
  H behind a preserved P is allowed when H itself is exactly inspected and proved.
- **D-6 — One runner admission boundary protects all use.** A concrete
  runner-local path gate and durable journal are shared by creation, cleanup,
  StartAsync and adoption. Maintain a documented fixed lock order: normalized
  workspace gate before existing session/custody gates; no reverse acquisition.
  Restore fences and census retained ownership before admitting cleanup. Live,
  pooled or unknown owners refuse removal without stopping them. Discovery of an
  already surviving process during adoption records an ownership conflict and
  holds cleanup; it must never hide or kill that process to make the fence true.
  Reject process-list sampling or file-lock probes as complete ownership proof.
- **D-7 — Persist and compare the complete command identity.** The canonical
  digest covers schema version, Done/card/target/endpoint identity, immutable mirror
  binding, operation ID, expected source, preservation/artifact proof and disposal
  policy. Canonical serialization is versioned. Same ID plus different digest
  refuses; exact replay returns or resumes the same journal. Store changes,
  malformed/torn journals and unproved absence hold. No new operation ID after a
  timeout or spent command budget.
- **D-8 — Use no-follow, component-aware removal.** Recheck source, ignored
  inventory, ownership, registration and intent immediately before mutation.
  Persist a uniquely bound sibling set-aside path before renaming. Remove only
  the matching registration and owned set-aside entries without following links;
  preserve a replacement at the original path. Port the narrowly needed server
  no-follow and classification behavior into runner-local helpers, with parity
  tests, rather than moving the working server executor or referencing its
  assembly. Effective policy travels in the bound request; runner protected
  minima always win and unknown files refuse. Keep directory and registration
  facts distinct. This operation leaves local and origin branch refs untouched;
  branch deletion is not required for mirror completion.
- **D-9 — Admit mirrors independently.** Before a new mirror intent, the server
  revalidates Done generation, exact attempt/binding, terminal ownership, settling
  floor, outstanding consumers/recovery, and external report/deliverable proof.
  Persist intent under the foundation's card/admission ordering with no network
  or Git under a DB row lock. Reopen first prevents issuance; intent first fences
  that exact old mirror and permits its recovery. Local intent does not authorize
  a mirror. Retry an admitted intent without reauthorizing a different target.
- **D-10 — Completion is a receipt, not an ACK.** Validate every response field
  against the persisted request, import the external runner receipt, and save
  component facts before task/UI invalidation. Missing fields, mismatched digest,
  timeout or transport acceptance do not set Complete. Retain retry metadata and
  separate endpoints after local Complete. Parent S4 owns recurring recovery,
  action budgets, operator readback, delivery acceptance and cutoff activation.
- **D-11 — Keep all slices inert and compatible.** No Program registration for
  the coordinator/remover, no new public cleanup route and no automatic caller.
  Runtime hooks are inactive without the explicitly supplied cleanup service;
  direct calls on an unconfigured dispatcher refuse. Default composition tests
  prove this even if a fixture sets `Execute=true`. Reject enabling a half-built
  endpoint on the assumption that today's server will not call it.

These are implementation decisions under the card's existing authorization,
not unresolved operator-policy defaults.

## Execution contract

The server mirror binding table records creation facts before Done and does not
itself authorize deletion. Unique keys cover preparation identity and
`(TaskId, Attempt, RunnerStoreId, BindingId)`; records are append-only identities,
not a mutable last-runner slot. Extend endpoint storage with a binding reference,
versioned immutable request and imported typed receipt. Generate EF migrations
with the repository CLI convention; do not hand-author migrations. Legacy tasks
receive no fabricated binding in the migration.

The runner's configured state root owns ordinary binding and removal records;
neither request nor worktree chooses that root. Keep it separate from verification
creation/custody stores. Writes must be atomic and recovered conservatively.
RunnerStoreId identifies persistence, not process uptime. A binding identifies a
creation generation; reusing the same pathname or full task ID cannot revive an
old completed command. Creation and preparation recovery must distinguish a
pre-existing legacy tree from a tree created by the recorded interrupted attempt.

The read-only inspect operation produces fresh repository/path/source facts for
a known binding. It cannot enroll an unbound directory. The server uses these
facts, actual durable artifact verification and the existing publication record
to admit a request. Remote artifact pointers cannot be verified by desktop
`File.Exists`: paths under any local/mirror/set-aside root remain ineligible.
Do not reuse `CardWorktreeCleanupExecutor.ConfirmedPublicationAsync` verbatim:
its local-cleanup-complete exclusion is inappropriate for a pending mirror.

The guarded request contains the identity in D-7 and a CardDone disposition;
the runner is a trusted recipient of the server's generation admission but must
independently validate its own binding, source, filesystem and process facts.
Transport authentication remains the existing phone-home contract. Logs and
receipts contain stable reason codes and safe identities, never credential-bearing
URLs, raw process environments or secret file contents.

On first mutation, a durable journal must already name the operation, digest,
binding, source and set-aside location. On replay it reconciles those exact
components. A missing original tree without an owned receipt is unknown, not
success. A replacement original path is preserved. Partial deletion retains the
fence and exact remaining owned work. Replay neither resets command allowance
nor repeats publication. Mirrors complete only when both directory and Git
registration facts are proved; an unchanged branch is not failure.

No session is added or changed to wait for input. The CARD-1083 release checklist
is therefore not triggered. Live/Blocked owners remain ineligible for cleanup;
with parking disabled there is no automatic deadline releasing their seat or
making their workspace safe.

## Landable slices and collision scope

Each slice below is a separate 30–60 minute Code dispatch, dependent on prior
slices, followed by commit/push and its checkpoint group. Budgets include the
row's isolated build. Helpers and tests may use partial class files to keep these
small; the public behavior remains the parent S3 contract. All slices can land
with the new production composition absent. Do not compress them into one large
Code dispatch. S1–S8 below are this card's slice IDs, all inside parent S3;
references to parent S4/S5 mean the later cards, not these smaller slices.
New filenames are proposed and must be bound by TestDesign.

| Slice / budget | Files to change | Tests and independent landing boundary |
|---|---|---|
| **S1 — identity and wire schema, 40–55 min** | `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`, `SessionRunnerContracts.cs`; new `CompletedCardWorkspaceContracts.cs`; new `server/Domain/Entities/TaskWorktreeMirrorBinding.cs`; `CardWorktreeCleanupTarget.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*`. | New `tests/Antiphon.Tests/Application/CardWorktreeMirrorBindingTests.cs`, `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceContractTests.cs`: identity uniqueness, legacy migration, serialization, required fields and no Force escape. Only inert schema/contracts land. |
| **S2 — durable mirror binding capture, 45–60 min** | New `src/Antiphon.SessionRunner/RunnerWorkspaceBindingStore.cs`; `RunnerWorkspaceService.cs`; `server/Application/Services/RemoteWorkspaceService.cs`, `RemoteWorkspacePreparer.cs`; new `server/Application/Dtos/RemoteWorkspaceMirrorResult.cs`; mirror client/dispatcher plumbing in `PhoneHomeRunnerClient.cs`, `PhoneHomeCommandDispatcher.cs` as required. | New `tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests.cs`; extend `CardWorktreeMirrorBindingTests.cs` and `RemoteWorkspacePreparerTests.cs`: own creation, restart, short-ID collision, canceled/reassigned attempt, old-peer compatibility, no adoption of an unbound legacy tree. No removal implementation/capability yet. |
| **S3 — runner use fence, 45–60 min** | New `src/Antiphon.SessionRunner/RunnerWorkspaceUseGate.cs`, `RunnerWorkspaceRemovalJournal.cs`; `SessionRunnerRuntime.cs` launch, explicit attach and startup/Herdr re-adoption boundaries; `RunnerWorkspaceService.cs` creation admission. | New `tests/Antiphon.SessionRunner.Tests/CompletedCardRunnerFixture.cs` and `CompletedCardWorkspaceRemovalTests.cs`: parent's process-owner, Start, re-adoption and durable-intent methods. Cover actual runtime entry points with test-owned processes and deterministic barriers. Production has no remover instance. |
| **S4 — guarded inspection and proof, 45–60 min** | New `src/Antiphon.SessionRunner/RunnerCompletedCardWorkspaceRemoval.cs`, `RunnerWorkspaceIgnoredContent.cs`; narrow internal Git/identity seams in `RunnerWorkspaceService.cs`; journal helper. | Extend `CompletedCardWorkspaceRemovalTests.cs`: full task/attempt/store/repository/path/ref/HEAD, preservation proof, dirty/ignored/nested/locked/unknown content and final-read guards. Explicit inspect can run against fixture trees; destructive phase is not exposed. |
| **S5 — no-follow removal and replay, 45–60 min** | New `src/Antiphon.SessionRunner/RunnerWorkspaceNoFollowDelete.cs`; `RunnerCompletedCardWorkspaceRemoval.cs`, `RunnerWorkspaceRemovalJournal.cs`. | Extend runner removal tests with isolated real Git success, no-follow swap, exact digest replay, partial component restart, original-path replacement and absent-without-receipt. Assert unchanged origin refs and preserved sentinel bytes. Only fixture-composed executor can remove. |
| **S6 — typed transport and compatibility, 35–50 min** | `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`, `PhoneHomeRuntimeAdapter.cs`, `SessionRunnerRuntime.cs`; `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs`; new `server/Application/Interfaces/ICompletedCardWorkspaceClient.cs` only for the external runner I/O seam. | Extend `CompletedCardWorkspaceRemovalTests.cs`, `CompletedCardWorkspaceContractTests.cs`, and existing dispatcher tests: real dispatch reaches guarded recipient, exact replay, unsupported refusal without legacy fallback, default composition inert. Complete parent U roster of 21 methods plus new prerequisites. |
| **S7 — server endpoint admission, 45–60 min** | New `server/Application/Services/CardWorktreeMirrorCleanupService.cs`; `CardWorktreeCleanupService.cs` mirror discovery; new `CardWorktreeMirrorCleanupAdmission.cs` if needed to keep coordinator small; endpoint mappings only if S1 contract needs correction. Reuse artifact/publication services; do not rewrite the local executor. | New `tests/Antiphon.Tests/Application/CardWorktreeMirrorCleanupTests.cs`; extend `tests/Antiphon.Tests/TestHelpers/CompletedCardCleanupFixture.cs`: multiple bindings, fresh generation, settling/consumer/artifact refusal, mirror-specific H proof, receipt-field mismatch. Callable service only; no sweep/Program wiring. |
| **S8 — durable outcome/retry composition, 45–60 min** | Mirror coordinator and endpoint persistence above; fixture code in new `tests/Antiphon.Tests/TestHelpers/CompletedCardMirrorFixture.cs`; `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` only if an opt-in dispatcher peer seam is needed. | Complete parent M roster of 12, plus new request-digest, partial recovery and inert-composition tests. Real loopback channel/dispatcher/Git demonstrates local Complete plus mirror Pending, reconnect/restart, lost ACK, store replacement and reverse outcome order. Invocations are explicit in tests; scheduled/delivery activation stays S4/S5. |

Do not modify these reported in-flight areas: `tools/Antiphon.Checkpoints/**`,
`AgentSessionService.cs`, `AgentService.cs`, `AgentControlService.cs`,
`scripts/c590-remote.sh`, `RemoteScriptContractTests.cs`. No change here requires
them. Shared collision areas within this plan are runner contracts/dispatcher/
runtime, `RemoteWorkspaceService.cs`, `RemoteWorkspacePreparer.cs`, `AppDbContext`
and migration snapshot. Serialize slices touching those files with other active
owners; refresh source and freeze the next roster after a conflicting land.
`server/Program.cs`, runner `Program.cs`, `TaskWorktreeRetirementService.cs`,
`WorktreeResidueSweepService.cs` and server guarded-removal helpers are inspected
dependencies, not edits in this card. Avoid broad refactors to share their code.

## Verification requirements for TestDesign

TestDesign is a separate stage. Preserve parent V-5/V-6, R-3 and guards/controls
G/PC-52 through G/PC-82 and G/PC-108/109, including all 21 U and 12 M methods. Do not silently
substitute broad class passes for their individual refusal or recovery witnesses.
Add explicit positive paths: the parent's guard-named methods alone must still
end in at least one real eligible removal, not only zero-call assertions.

Additional requirements introduced by the source findings are:

- **V-1:** bound creation survives server/runner restart and lost preparation
  replies; full-ID collision, old store, attempt drift and unbound historical
  tree cannot be enrolled. Reassignment retains historical bindings without
  changing the new runner's current path.
- **V-2:** exact operation and immutable digest; version/field validation;
  unsupported peers leave debt; ordinary legacy create/publish/remove remains
  compatible; default production composition cannot execute the new remover.
- **V-3:** runner identity, publication/rebase proof and ignored-content policy
  are checked at first and final boundaries; all refusal fixtures otherwise start
  eligible. Report/deliverable and source bytes survive refusal.
- **V-4:** real runtime launch/attach/adoption and cleanup are mutually excluded
  by workspace identity, including restart and contradictory retained owners.
  No process is stopped as cleanup. Unknown descendant ownership holds.
- **V-5:** eligible real Git mirror with disposable evidence is actually removed,
  matching registration is gone, outside sentinels and refs survive, and the
  external journal/receipt remains. Directory/registration partial states and
  no-follow recovery are independent.
- **V-6:** local Complete does not hide mirrors; two stores remain two endpoints;
  offline/unsupported runner never prevents local progress. Actual server DB,
  loopback phone-home channel, dispatcher and runner executor converge under
  lost-response/server-save/restart cuts using one operation identity.
- **R-1:** the existing parent CP-4 legacy filter and eight
  `RemoteWorktreeMirrorTests` remain the baseline regression selection. Check
  their actual source-expanded counts before freezing; do not copy old counts
  across newly landed parameterizations.

Use isolated PostgreSQL databases and fresh contexts for restart readback. Use
the existing real Git fixtures; server fakes may inject transport failure but
cannot certify runner deletion. The loopback peer must execute the real
dispatcher/remover, not return a canned success. Deterministic barriers cover
before/after intent, rename, unregister, component-save, reply and server-save.
No sleep chooses a race winner. Child processes belong to the fixture, carry
the assembly-local `ParallelLimiter<ProcessSpawnLimit>`, and are awaited/reaped.
Never target the production runner; hosts booting real Program use the existing
production-runner guard.

### Checkpoints

Proposed narrow groups for TestDesign to freeze; **not yet a Code dispatch
manifest**. A prefix names exactly one behavior family in its stated class,
with single-result methods and explicit named vector loops. Parent C1017 method
names are retained so its method-scoped Mutation obligations stay addressable;
new prerequisite methods use the C1032 prefixes below. TestDesign must bind all
names/counts and map each parent U/M method to one row before authorizing Code.
The listed Min values for new families are proposed minimum single-result
rosters, not assertions or measurements. No test/build was run by this Plan.

Each row has a fresh isolated build and one exact filter. Groups name the lane;
all these S3 groups are Linux-portable. Select explicit `--rows` per slice, run
through the checkpoint tool/build-slot gate after commit/push with exact source
SHA, and await completion (including exit-75 waits). Never run whole Unit or an
unrelated assembly. Keep generated evidence ignored and remove only the
checkpoint-owned alternate outputs after the run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1032-schema/` | linux-binding-schema | `/*/*/CardWorktreeMirrorBindingTests/C1032_Schema*` | V-1 | all 3 planned results; 0 failed/skipped | 3 | 5 | true |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-contract/` | linux-wire-contract | `/*/*/CompletedCardWorkspaceContractTests/C1032_Wire*` | V-2 | all 3 planned results; 0 failed/skipped | 3 | 4 | true |
| CP-3 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-binding/` | linux-runner-binding | `/*/*/RunnerWorkspaceBindingTests/C1032_Binding*` | V-1 | all 5 planned results; 0 failed/skipped | 5 | 5 | true |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1032-capture/` | linux-server-binding | `/*/*/(CardWorktreeMirrorBindingTests*)\|(RemoteWorkspacePreparerTests*)/C1032_Capture*` | V-1 | all 4 planned results; 0 failed/skipped | 4 | 5 | true |
| CP-5 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-fence/` | linux-use-fence | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds)\|(C1017_RunnerStartFence)\|(C1017_RunnerReadoptionFence)\|(C1017_RunnerIntentBeforeIo)\|(C1017_UnknownProcessOwnershipHolds)` | V-4 | all 5 parent methods; 0 failed/skipped | 5 | 5 | true |
| CP-6 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-identity/` | linux-removal-identity | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_Runner*Identity)\|(C1017_RunnerPreservationProof)\|(C1017_RunnerSourceLandingExcluded)` | V-3 | all 9 parent methods; 0 failed/skipped | 9 | 5 | true |
| CP-7 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-content/` | linux-removal-content | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource)\|(C1017_RunnerIgnoredPolicy)\|(C1017_RunnerFreshFinalInspection)\|(C1017_RunnerNoForceBypass)` | V-3 | all 4 parent methods; 0 failed/skipped | 4 | 5 | true |
| CP-8 | S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-components/` | linux-removal-components | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow)\|(C1017_RunnerDigestReplay)\|(C1032_Removal*)` | V-5 | 2 parent + 3 planned results; 0 failed/skipped | 5 | 6 | true |
| CP-9 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-dispatch/` | linux-guarded-dispatch | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(CompletedCardWorkspaceContractTests*)/(C1017_GuardedOperationIsDistinct)\|(C1032_Transport*)` | V-2 | 1 parent + 3 planned results; 0 failed/skipped | 4 | 5 | true |
| CP-10 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-legacy/` | linux-runner-legacy | `/*/*/(RunnerWorkspaceServiceTests*)\|(PhoneHomeCommandDispatcherTests*)/(Mirror_*)\|(Publish_*)\|(Remove_*)\|(Workspace_*)` | R-1 | parent's 28-result baseline; reconcile before freeze; 0 failed/skipped | 28 | 6 | true |
| CP-11 | S7 | `tests/Antiphon.Tests -> bin-c1032-admission/` | linux-mirror-admission | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)\|(C1017_ReopenBetweenEndpoints)\|(C1017_MultipleMirrorsRemainDistinct)\|(C1032_Admission*)` | V-1, V-3, V-6 | 3 parent + 3 planned results; 0 failed/skipped | 6 | 6 | true |
| CP-12 | S7 | `tests/Antiphon.Tests -> bin-c1032-receipt/` | linux-mirror-receipt | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatch*)\|(C1017_AckIsNotRemoval)\|(C1017_UnsupportedNeverFallsBack)\|(C1032_Receipt*)` | V-2, V-6 | 6 parent + 1 planned digest result; 0 failed/skipped | 7 | 6 | true |
| CP-13 | S8 | `tests/Antiphon.Tests -> bin-c1032-recovery/` | linux-mirror-recovery | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorCompletesIndependently)\|(C1017_LocalProgressSurvivesOffline)\|(C1017_LostAckReusesOperation)\|(C1032_Recovery*)` | V-5, V-6 | 3 parent + 3 planned results; 0 failed/skipped | 6 | 7 | true |
| CP-14 | S8 | `tests/Antiphon.Tests -> bin-c1032-sync/` | linux-legacy-sync | `/*/*/RemoteWorktreeMirrorTests/*` | R-1 | all 8 existing methods; 0 failed/skipped | 8 | 5 | true |
| CP-19 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-store-mismatch/` | linux-store-mismatch | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreMismatch*` | V-1 | all 1 listed; 0 failed/skipped | 1 | 5 | true |
| CP-20 | S2 | `tests/Antiphon.Tests -> bin-c1032-store-replacement/` | linux-store-replacement | `/*/*/RemoteWorkspacePreparerTests/C1032_StoreReplacement*` | V-1 | all 1 listed; 0 failed/skipped | 1 | 5 | true |
| CP-21 | S2 | `tests/Antiphon.Tests -> bin-c1032-join/` | linux-mirror-join | `/*/*/PhoneHomeMirrorJoinTests/C1032_Join*` | V-1 | all 6 listed; 0 failed/skipped | 6 | 8 | true |

CP-15 through CP-18, and the closed importer list that includes CP-19, CP-20, and CP-21, live in `docs/superpowers/plans/2026-10-12-card-1032-test-design.md`.

### Windows-only witnesses and deferred activation

Native Windows sharing/locked-file, reparse/junction and supported-backend
process witnesses remain parent S5: CP-10 (native local), CP-11 (the complete
runner U roster), CP-12 (local/mirror compatibility). They require Windows at
the same implementation SHA; Linux success is not their evidence. TestDesign
must preserve these outstanding obligations in the S4/S5 handoff, including any
new binding/fence methods that need native qualification. Do not add platform
skips or run them against a production path to claim S3 acceptance.

At dispatch, read `/api/runner-defaults` and `/api/session-runners` again. Omit
`-Runner` unless deliberately pinning one host. Omit `-Platform` for portable
work, use `-Platform Windows` only for native witnesses; `-Platform Any` clears
an inherited pin. No location or current host occupancy is embedded in this plan.

### Cost and TestDesign handoff

The proposed ordinary checkpoint floor is **75 minutes**, summed from
EstimatedMinutes; counts are separate. The eight Code dispatches total
**345–465 minutes including their checkpoints**, not a single 30–60 minute
assignment. These are estimates, not measurements. TestDesign must census the
28/8 existing baselines, expand proposed prefix families into explicit methods,
retain parent PCs 52–82 and 108/109, and add method-scoped controls for new binding/digest/
inertness guards. Run those controls only in the separately commissioned
post-land Mutation stage. No checkpoint-tool implementation changes belong here.

## Risks

- The new binding prerequisite makes historical unbound mirrors visible debt;
  it intentionally cannot promise to remove every existing mirror. A future
  audited historical-binding procedure is separate work, not a fallback here.
- Runtime adoption has multiple entry points. Missing explicit attach or a
  retained-host arm would leave a deletion race; TestDesign must enumerate each
  actual caller and prove both race orders without editing the in-flight server
  session services. Unknown child accounting must refuse, not guess.
- Default-inert composition can mask an unimplemented executor in weak tests.
  Require both a real fixture-composed successful removal and a default-host
  refusal; neither substitutes for the other.
- Porting no-follow/classification behavior risks drift. Pin representative
  protected/disposable/evidence precedence and outside-sentinel behavior against
  the server policy, then retain Windows qualification before activation.
- Request/receipt schema, normalized binding identity and migration uniqueness
  must be settled before persistence code. A failed save after remote success
  must replay the old command, never rediscover a replacement at the same path.
- Production HTTP briefly returned 502 after the first card read, then recovered.
  Re-read current runner/defaults before dispatch; do not infer ongoing runner
  availability from either that transient failure or this later successful read.

## Operator decisions

**None required to implement inert S3.** Existing card instructions settle that
scope. Activation, historical cohort inclusion/cutoff and any historical-binding
repair remain explicit later operator decisions after S4/S5 safety, delivery and
native acceptance. This plan does not choose their values or commission deletion.

## Planning validation

Read-only source and ancestry inspection confirmed the gap and foundation.
No production or test source was edited; this plan is the only new file.
No build or test was needed. Card get, history and platform reads succeeded;
transient 502s were retried without interpreting them as empty inventories.
Static document checks confirmed 14 correctly shaped checkpoint rows, the
75-minute estimate sum, and exactly one proposed row for each of the parent's
21 runner and 12 mirror methods. This is roster validation, not executed tests.
Publication and merge-tree results belong in the final task report so this
artifact does not claim checks before they happen. Post-land Mutation was NOT run.
