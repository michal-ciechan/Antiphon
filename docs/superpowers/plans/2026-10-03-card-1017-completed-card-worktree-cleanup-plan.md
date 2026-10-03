# CARD-1017: dispose of completed-card task worktrees and ignored evidence

Plan task: `402bbca2-b30f-4fdf-b37d-1b7dae6777a4`. Inspected source:
`2d3c582416c5610d72e53df3e790bc114d87ad82`, 2026-10-03 UTC.
Status: Plan complete; **TestDesign remains a separate stage**. The verification
section below specifies requirements and proposed checkpoint groups, not a frozen
Code execution contract. No runtime, configuration, card-state or filesystem
cleanup changes are made by this plan.

## Outcome and reconciliation

**Keep CARD-1017, narrowed to Done-card authority, disposable evidence, and durable
local/mirror completion for ordinary task worktrees.** It is not already satisfied
or superseded by a landed implementation. It must reuse the existing retirement
and publication cleanup paths, not introduce a Cleanup agent role or a second
filesystem sweeper. This is also the legacy-worktree cleanup boundary for a future
CARD-0824 migration; reusable slot worktrees must never become its candidates.

The controlling operator decision in CARD-1017 is: "for cleanup we should delete
whole worktree after card is done, then fine to leave gitignored". This supersedes
the older assumption that raw verification evidence needs a new retention-copy
service. It does not supersede preservation of unique work or authorize killing
a working session.

Live `card.ps1 get/history` reads established the following. Board states are
observations, not proof that every historical implementation slice landed.
CARD-1017 had no earlier verdict; its only history entry was this Plan dispatch.

| Card | Observed state and reconciliation | Recommendation / owner boundary |
|---|---|---|
| CARD-0824 | Backlog, one revision. Proposes one reusable worktree per slot, preserving build caches and wiping task scratch. Explicitly supersedes per-task retirement *in that proposed design*. Current code still creates and retires individual task worktrees. | Keep 0824 as the future allocation/reset design. Keep 1017 for current and legacy ordinary trees; 0824 must call the same legacy retirement executor, not add another deleter. Do not apply Done-card directory deletion to slots. |
| CARD-0692 | Review, 15 revisions. Broad post-land cleanup includes all sibling worktrees, origin branches, stranded sessions and external temp roots; latest scope edit adds temp ownership rules. No evidence in current code of a durable all-tasks Done obligation. | Narrow its worktree/evidence/mirror portion to depend on 1017. Leave origin-branch deletion, session lifecycle and out-of-tree temp cleanup with 0692/0824 and their existing owners. Land and Done are distinct triggers. |
| CARD-0459 | Review, 18 revisions. Its typed retirement service, reservation journal, publication-retry lane and Hangfire residue job exist in this source. | Reuse these mechanisms. 1017 adds a source of authority and independent mirror recovery; do not rebuild scheduling, adopt TTL as authority, or infer shipped execution from this card's state. |
| CARD-0665 | Review, 20 revisions. Classifier, no-follow removal and evidence-retention seam exist. `Program` still registers `RefusingEvidenceRetention`; the older plan's proposed copying implementation is not the deployed source contract. | Preserve classification, protected paths and no-follow deletion. Replace raw-evidence retention with explicit disposal only for a valid Done-card obligation. Do not implement the older retention-copy proposal as a prerequisite. |
| CARD-0670 | Backlog, no revisions. Proposes release of never-landed non-Code worktrees with evidence copying and a retention window. | 1017 covers its Done-bound, fully published/contained, uniquely owned subset. Keep unbound and still-active-card retirement policy with 0670; discard its copying requirement for the 1017 subset. A pushed-only unique branch is not silently treated as landed. |
| CARD-0573 | Done, explicitly closed as superseded by landed CARD-0738. Its verdict says genuinely bound open tasks are settled on close; title/path similarity is insufficient. | Keep closed. Reuse settlement's started/unstarted distinction. Task cancellation is not workspace deletion authority. Do not infer ownership from a title or directory name alone. |
| CARD-1015 | Brief confirms landed; checkout contains its policy/checker and final evidence commits, including inspected HEAD. Board still says InProgress. Its plan explicitly assigns whole-tree disposal to 1017. | Dependency satisfied for planning. Require the same policy in the implementation base; preserve ignored generated evidence and the canonical report store. Do not wait for a board label to catch up. |

No related card is moved or closed by this Plan dispatch. The caller can use this
table to reconcile their remaining scope without canceling unfinished branch,
session or temp-root work. CARD-0824's seven explicitly protected dirty/unpushed
legacy trees remain protected; age and a migration cutoff never authorize them.

## Ground truth

Paths below are relative to the repository and name the inspected implementations.

| Card assumption | What the code does at the inspected SHA | Required delta |
|---|---|---|
| Done eventually removes every eligible task tree. | `server/Application/Services/CardService.cs` saves a terminal move before calling `CardTaskSettlement.SettleClosedCardAsync`. `CardTaskSettlement.cs` enumerates open bound tasks, cancels unstarted tasks and warns about started ones. Neither creates a deletion obligation. | Persist a Done generation and recoverable task inventory with the card write; cleanup is asynchronous and independent of cancellation success. |
| An ordinary retirement can be inferred from terminal task status. | `TaskWorktreeRetirementService.ReleaseAsync` requires `NoFurtherWorkspaceUse`, task revision, source SHA, report/handoff disposition, exact ordinary ownership and a settling floor. It excludes Mutation, SourceLanding and repair-source tasks. | Add a typed card-completion authority, not a fabricated operator call to `ReleaseAsync`. Preserve the manual API's semantics. Resolve ordinary repair/unsourced-Mutation trees by exact independent ownership; exclude sealed verification snapshots. |
| Publication and settled-task cleanup share one authority. | `HasRecoveryDebtAsync` treats **any** landing row as debt for settled retirement. `WorktreeResidueSweepService` separately queues confirmed publication cleanup. Landing cleanup uses NotStarted as the enum default, then Pending/Complete/Refused. | Link the appropriate existing executor to each target. Do not route a published task through the manual settled path or overwrite landing publication facts. |
| Ignored evidence is already disposable. | `WorktreeCleanupSettings.DefaultRetainedIgnored` includes task reports, TRX and checkpoint folders; protected names win. `WorktreeIgnoredContentGate.RetainAsync` delegates to `RefusingEvidenceRetention`, which refuses nonempty evidence. | Give a verified Done-card request an explicit evidence-disposal disposition. Leave ordinary active-card land cleanup unchanged. |
| Current artifact checks prove survival. | `TaskWorktreeRetirementService.ArtifactsRetrievableAsync` can accept an existing deliverable inside the soon-deleted tree. `AgentReportStore.IsUsableAsync` checks bytes; by itself it does not prove an input path is outside that tree. The report store's root resolver does enforce persistence when writing. | Re-resolve all artifact pointers; verify an external canonical report and external deliverables before disposal. Existence inside the tree is insufficient. |
| Local Complete means mirror absent. | `TryRetireAsync` returns immediately for Complete. Its mirror call is best-effort, outside the component journal, and occurs after both successful and refused local removal results. `RemoteWorktreeResidue` is just a task field. The sweep skips completed retirements. | Independent durable endpoint state and retries, including mirror-only residue after local Complete. Keep existing local completion historical. |
| Runner remove proves all safety properties. | `RemoteWorkspaceService.RemoveMirrorAsync` supplies published SHA only when `workspacePublishV1` is advertised. `RunnerWorkspaceService.RemoveAsync` checks root, ordinary status and optional ancestry, then calls `git worktree remove --force`; status omits ignored files. It has no card generation, task/attempt-bound cleanup receipt or ordinary workspace-use fence. | A distinct guarded completed-card operation with required source/preservation proof, no-follow deletion, process/use exclusion and idempotent receipts. Never fall back to legacy removal for this authority. |
| Reopen already cancels every deletion race. | `CardService.ReopenAsync` saves first, then invalidates unclaimed releases. `WorkspaceReservationJournal.InvalidateUnclaimedReleaseAsync` only revokes Released/unclaimed rows. Claimed/started cleanup remains fenced. | Serialize Done-generation invalidation and deletion admission; define the winner at durable intent, including delayed remote commands. |
| Recovery can just retry the same deletion. | Retirement attempts have a spent command intent; `TryRetireAsync` can return `cleanup_command_slot_spent`. `GuardedWorktreeRemoval` has set-aside receipts and separate directory/registration/ref results. | Reconcile interrupted attempts and resume their recorded components; never reset command budgets or claim absence from a timeout. |
| The scheduler executes automatically. | `WorktreeResidueSettings.Execute` defaults false, enabled job daily; `MinSettledMinutes=120`, action budget 25. Preview is not deletion authority. | Keep shipped defaults and make operational enablement an explicit activation step, with a bounded canary and separate legacy-backlog preview. |
| Plans can pin today's host. | Required reads of `/api/runner-defaults` and `/api/session-runners` succeeded on 2026-10-03. Defaults revision 2 has a global preference and no per-kind override; catalogue has an eligible Linux lane and an eligible Windows lane, with another entry unavailable/draining. Occupancy is transient. | Resolve again at dispatch. Omit `-Runner`; omit `-Platform` for portable rows, use `-Platform Windows` only for native Windows qualification. `-Platform Any` clears an inherited pin. |

## Decisions

These are the proposed implementation decisions under the existing operator
policy; there is no unanswered product-policy choice preventing TestDesign.

- **D-1 — Done, not land or elapsed time, grants evidence disposal.** Use the
  immutable Done move revision as the completion generation. Canceled and
  archive-only transitions do not grant it. Archiving an already-Done card neither
  creates a new generation nor revokes the existing one. Reject a land-only
  trigger: sibling tasks and their raw evidence can still be needed before Done.
- **D-2 — Inventory all bound ordinary task attempts, without touching running
  work.** Persist the obligation even when a task is Working. A task created after
  Done is discovered on later passes, but is never canceled by this cleanup path;
  it becomes eligible only after it settles and releases all use. SourceLanding
  snapshots remain exclusively under `cleanup-verification`, including external
  evidence/custody stores. Shared/read-only borrowed trees, canonical/land trees,
  slot trees and ambiguous owners are ineligible. A repair task's unique ordinary
  tree can qualify; its owner's tree cannot qualify through that relationship.
  Unsourced Mutation qualifies only if positively identified as an ordinary
  independently owned task worktree, never by relaxing verification custody.
- **D-3 — One coordinator, existing executors.** Add a small durable card cleanup
  obligation/target ledger and run it from `WorktreeResidueSweepService`. Link
  publication targets to their landing operation/`WorktreeCleanupAttempt`; link
  non-publication targets to `TaskWorktreeRetirement` and its attempt journal with
  an explicit `CardDone` authority source. Both still reach guarded removal. Reject
  a new agent role, cron service, recursive-delete script or retention-copy service.
- **D-4 — Preserve unique work.** Fresh clean tracked/untracked source, symbolic
  branch, repository identity and remote preservation proof are mandatory at each
  destructive boundary. Reuse confirmed publication and its recorded rebase mapping
  when original and landed SHAs differ. Otherwise require source ancestry in the
  configured published target. An exact push only to a unique task branch is held
  as `unlanded_work`; this card does not broaden legacy retirement eligibility or
  delete origin branches. Never reset/stash source or force-push to make it eligible.
- **D-5 — Dispose of raw evidence only with this authority.** Keep the classifier's
  evidence category; a verified CardDone disposition permits its deletion with the
  tree instead of retention. Disposable build outputs continue through their current
  path. Protected/unknown ignored files, secrets, user-local data, deliverables and
  links remain refusal conditions. Do not blanket-allow `.antiphon/**`, rename
  evidence to evade the classifier, or require artifacts to be force-added to Git.
  An unfamiliar evidence location is reported for disposition, not guessed safe.
- **D-6 — Reports and deliverables survive; raw evidence need not.** Ensure the
  complete task Result is in the canonical `AgentReportStore` outside all deletion
  roots with a matching digest. Require actual deliverable pointers to resolve to
  existing durable locations outside every target and set-aside root. Use existing
  deliverable extraction/report repair if already available; otherwise hold with
  `artifact_unpreserved` for the caller to repair. Do not create a generic copy
  service. Missing reports need an existing explicit missing-report disposition;
  Done does not invent report content or a successful handoff. A recorded Done
  disposition can end a stale next-stage workspace dependency, but never an active
  consumer or an open landing/repair/recovery obligation.
- **D-7 — A deletion intent is the race boundary.** Under a short card-generation
  transaction and workspace reservation, admit a target only if the card is still
  Done in that generation and fresh eligibility holds. Reopen that commits first
  revokes every unissued target. If deletion intent commits first, reopen may
  succeed, but the exact old tree stays fenced and that intent can finish; new work
  gets fresh coordinates. Local and remote endpoints each have an admission/intent
  identity. Reopen between them prevents issuance for the second endpoint. No DB
  row lock is held during Git, process inspection or network I/O. Reject reliance
  on the existing post-save invalidation alone.
- **D-8 — Processes are independently protected.** Terminal task/session rows are
  necessary, not proof of no live process. Consult workspace reservations, current
  session ownership and existing owned-child/custody evidence. Unknown ownership
  holds the target. The runner must exclude starts/re-adoption into a retiring path
  using the same runner-local admission boundary, including after restart; merely
  sampling `List()` is insufficient. Do not stop, kill, pool or detach a session as
  a side effect of card cleanup. Do not use an open-file probe as process authority.
- **D-9 — Local and each mirror complete separately.** Persist endpoint identity,
  intent, outcome and retry timing before/after I/O. Keep local directory,
  registration and local-ref results distinct. A target with local Complete and
  mirror Pending is still cleanup debt. Offline/unknown/unsupported runner responses
  leave the mirror pending with a reason; local progress need not wait. Never turn
  path-null or a transport timeout into a deletion receipt. Preserve origin refs;
  existing safe local-ref cleanup can remain part of its executor.
- **D-10 — Add a safe operation without gating ordinary work.** A new runner
  operation carries immutable cleanup identity and returns typed per-component
  receipts. Attempt it without a CLI/model/version floor. Unknown capability or an
  older runner does not block task admission (CARD-1023); an unsupported destructive
  operation defers cleanup because it cannot prove the requested removal contract.
  Do not send optional safety fields to the old operation where they could be
  ignored. Existing requests retain their compatibility behavior. Native Windows
  evidence uses the supported backend; no legacy inbox ConPTY work is introduced
  (CARD-1022).
- **D-11 — Durable reporting, no new outbound notification.** Record every intent,
  refusal, partial result and completion in the existing residue run/task event
  surfaces, correlated to card revision, task attempt and endpoint. Extend the
  residue DTO/script output so operators can distinguish local and mirror facts.
  Persist the outcome before emitting UI invalidation. No Telegram, caller-session
  completion prompt or notification queue is added. This avoids creating another
  transport obligation; if TestDesign/Code adds one, it must first design real-queue
  recipient and crash-boundary evidence under CARD-0467.
- **D-12 — Bounded activation and recovery.** Keep `Execute=false` shipped and the
  existing settling floor/action budget. Add a Done-cohort cutoff for this new
  automatic authority: new Done generations after activation are eligible;
  historical Done generations are inventoried but held until the operator widens
  the cutoff after reading their preview. Use the existing job and a post-commit
  wakeup; the recurring job/startup reconciliation repairs a lost wakeup. This
  dispatch changes no setting. Reject automatic fleet-wide historical deletion
  on first upgrade. Retrying a transient refusal uses durable cooldown; permanent
  safety refusals await changed evidence. No timeout widening or hidden retries.

## Data and execution contract

Add `CardWorktreeCleanup` (one row per CardId + DoneRevisionId) and
`CardWorktreeCleanupTarget` (one row per obligation + TaskId + attempt + immutable
workspace identity). A target links the existing local retirement or landing
operation rather than copying its truth. Its mirror component records the runner
identity/store where available, repository identity, exact path/branch/source SHA,
operation ID, request digest, status, reason, attempt count and next-attempt time.
If historical data has multiple proven mirrors, persist one component per distinct
binding; do not rediscover arbitrary directories by filename. Missing ownership
history is visible debt, not permission to sweep a runner root. Store these
components as normalized child rows when multiplicity requires it, not a mutable
"last runner" scalar. Use CLI-generated EF migrations and unique keys for duplicate
Done callbacks, target discovery and endpoint commands.

The minimum endpoint states are Pending, IntentRecorded, Partial, Complete,
Refused and Revoked; transient retry eligibility is separate from the outcome.
An excluded target records its reason and is not mislabeled removed. Do not add or
reinterpret `LandCleanupStatus` values. Card cleanup cannot be "complete" while
any eligible endpoint is pending/partial/refused; expose excluded targets separately.
Newly discovered post-Done attempts can make an apparently exhausted inventory
nonempty again. Discovery must continue while the generation is current, rather
than treating a header Complete flag as a permanent task-set seal.

1. **Record/discover.** Record the Done obligation in the same save as the revision
   when possible. Cover all actual Done writers, not just the manual Move route;
   the reconciler discovers an unrecorded current Done revision after a missed
   callback/upgrade. Canceled/archive-only cards do not qualify. Inventory is
   non-destructive and independent of `Execute`; execution also observes the cutoff.
2. **Inspect.** Load fresh task attempt, card generation, report/deliverables,
   handoffs, pending land/recovery, ownership, repository, branch and source. Resolve
   current remote target containment or the immutable confirmed publication mapping.
   Refuse dirty source, unique unpushed/unlanded commits, unreadable proof, ambiguous
   coordinates, nested repositories/registrations or shared roots. Existence checks
   must distinguish absence from access errors and reject path escapes/reparse roots.
3. **Admit.** Acquire only the existing short repository mutation lease needed by
   the chosen local executor; discovery and waiting take none. Under the common
   card-generation/reservation ordering recheck the snapshot, commit the target
   command intent, then release DB locks. The last guarded removal read also
   validates the exact committed authority. Competing launch, continuation and
   reopen operations use this boundary. An evidence-disposal flag alone is never
   authority.
4. **Remove locally.** Route confirmed publication through cleanup-only retry,
   preserving the publication result. Route ordinary contained tasks through typed
   retirement. Both use the same ignored-content gate, double inspection,
   containment and no-follow set-aside/delete path. The Done authority must remain
   available at recovery from a set-aside receipt. Do not republish or rerun merge
   for cleanup. A present pointer below the tree prevents evidence disposal.
5. **Remove a mirror.** Issue the distinct guarded runner operation with required
   task/attempt/endpoint identity, expected HEAD and branch, preservation proof,
   disposal disposition and operation ID. The runner checks its own recorded
   repository/workspace binding, fresh source/ignored status and owned processes,
   fences new use, and persists intent outside the tree before removal. Required
   proof includes exact source publication or server-confirmed recorded rebase
   mapping; a SHA supplied without a bound proof is insufficient. No `Force=true`
   bypass. Adopt the no-follow/set-aside behavior through a runner-local helper;
   do not reference server Infrastructure from the runner project. Return directory
   and registration facts independently. A changed request under the same operation
   ID is refused. Delayed commands may only finish the exact fenced identity.
6. **Recover/report.** Startup and periodic passes enumerate unresolved endpoints
   even after local Complete. A lost acknowledgement queries/repeats the same
   operation ID, not a new destructive attempt. Reconcile actual absence and the
   exact durable set-aside/intent record before completing a component. Partial
   deletion resumes owned recorded components with fresh checks; an occupied or
   replaced original path is not touched. Keep a spent command's budget and command
   identity across restart; permit resumption of its recorded work, not a reset of
   its slot. Missing receipts or a replaced runner store are held visibly. Save the
   outcome and append correlated task events before `AgentTaskChanged` invalidation.

Runner receipts and server journals remain outside the target tree and remain
gitignored. They are operational cleanup records, not a raw-evidence retention
service. Never remove canonical `.antiphon/reports`, deliverable stores, repository
recovery pins, SourceLanding external evidence, runner state or cache volumes.

## Implementation slices

Implement in this dependency order; commit/push each slice before its checkpoint
group. New file names below are proposed, existing file names were inspected.

| Slice | Files / changes | Tests and purpose |
|---|---|---|
| **S1: obligation and lifecycle** | New `server/Domain/Entities/CardWorktreeCleanup.cs`, `CardWorktreeCleanupTarget.cs`; `server/Infrastructure/Data/AppDbContext.cs`, generated `server/Migrations/*`; new `server/Application/Services/CardWorktreeCleanupService.cs`; `CardService.cs`, `CardTaskSettlement.cs`, `CardLifecycleTransitions.cs` only where actual Done writes require integration; `WorkspaceUseAdmission.cs`, `server/Infrastructure/Data/WorkspaceReservationJournal.cs`; `server/Program.cs`. | New `tests/Antiphon.Tests/Application/CardDoneWorktreeCleanupTests.cs`: atomic Done, duplicate discovery, started task untouched, post-Done task, task attempts, exclusions, reopen winners and migration/backfill cutoff. Existing `CardTaskSettlementTests`, `ClosedCardSweepTests` pin cancellation semantics. |
| **S2: local disposition and preservation** | `server/Application/Services/TaskWorktreeRetirementService.cs`, `AgentTaskLandingProtocol.cs`, `AgentTaskLandService.cs`; `server/Domain/Entities/TaskWorktreeRetirement.cs`, `TaskWorktreeRetirementAttempt.cs`; `server/Application/Dtos/WorktreeRemovalRequest.cs`; `server/Application/Interfaces/IWorktreeRemovalEvidence.cs`; `server/Infrastructure/Data/WorktreeRemovalEvidence.cs`, `RetirementCommandJournal.cs`; `server/Infrastructure/Git/GuardedWorktreeRemoval.cs`, `WorktreeIgnoredContentGate.cs`; artifact survival helper under `server/Infrastructure/Files/` using `IAgentReportStore`. Keep the classifier globs unchanged. | New `tests/Antiphon.Tests/Infrastructure/CompletedCardWorktreeRemovalTests.cs`; extend `SettledWorktreeRemovalTests`, `WorktreeRemovalAuthorityTests`, `AgentReportStoreTests`: Done-only disposal, fresh authority, exact bytes protected, report/deliverable survival, original/rebased publication, ordinary repair/Mutation identity. |
| **S3: guarded mirror operation** | `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`; `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`, `RunnerWorkspaceService.cs`, `SessionRunnerRuntime.cs`, `PhoneHomeRuntimeAdapter.cs`, `Program.cs`; new runner-local workspace removal journal/fence helper shared by Start/re-adoption and removal; `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs`; `server/Application/Services/RemoteWorkspaceService.cs`; new `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. | New `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests.cs`; existing `RunnerWorkspaceServiceTests`, `PhoneHomeCommandDispatcherTests`; new `tests/Antiphon.Tests/Application/CardWorktreeMirrorCleanupTests.cs`; existing `RemoteWorktreeMirrorTests`. Test real Git removal, exact response identity, delayed/duplicate requests, launch exclusion and unavailable/unsupported transport. |
| **S4: retry, reporting and activation controls** | `server/Application/Services/WorktreeResidueSweepService.cs`, `TaskWorktreeRetirementService.cs`; `server/Application/Settings/WorktreeResidueSettings.cs`, validator; `server/Infrastructure/Agents/WorktreeResidueJob.cs`; `server/Application/Dtos/WorktreeResidueDtos.cs`, `WorktreeRetirementDtos.cs`; `server/Api/Endpoints/AgentTaskEndpoints.cs` if required to expose correlated endpoint facts; `scripts/worktree-residue.ps1`; update DI/registration tests. New optional fields preserve existing DTO meaning. | New `tests/Antiphon.Tests/Application/CardWorktreeCleanupRecoveryTests.cs` and `CardWorktreeCleanupAcceptanceTests.cs`; extend `WorktreeResidueSweepTests`, `WorktreeResidueRecoveryTests`, `WorktreeResidueEndpointTests`, `WorktreeResidueScriptTests`, `WorktreeLandingCleanupRetryTests`. Verify local Complete/mirror Pending, lost wakeup, cutoff, budget, restart and durable report readback. |
| **S5: native qualification and owner docs** | New Windows fixture `tests/Antiphon.Tests/Infrastructure/CompletedCardWorktreeRemovalWindowsTests.cs`; update `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, `docs/testing-and-build.md`, `docs/ops-http.md` and the activation paragraph in `docs/bootstrap.md`. Read the bootstrap owner before editing it. | Windows lock/reparse/partial-delete qualification plus affected reservation/retirement regressions. Document remaining residue and exact retry commands without claiming all historical trees were removed. |

Keep services concrete; introduce interfaces only for new filesystem/runner I/O
seams. Do not spread card policy into runner model/CLI routing. A normalized endpoint
table or helper needed for the above belongs in its slice, not an unrelated cleanup
refactor. TestDesign must bind any provisionally named helper to the actual source
owner before Code begins.

## Verification design

This is a **TestDesign handoff**, not folded TestDesign. Freeze named methods,
argument-expanded executed counts, exact production fault points and PC mappings
in the next stage before Code. Proposed new class names below do not exist yet;
their execution floors are requirements to implement, not claims about a run.

### Ordinary acceptance and regression requirements

| ID | Required observable result |
|---|---|
| V-1 | Real isolated DB transaction: Done revision and obligation survive restart together; repeated discovery produces one generation/target; Canceled and archive-only do not authorize deletion; post-Done tasks wait until terminal. |
| V-2 | A completed ordinary task tree containing ignored TRX, checkpoint JSON/logs, task report copies and build output disappears; canonical full Result and external deliverables remain byte-identical and retrievable. Before Done the same evidence blocks deletion. No raw copy is created. |
| V-3 | Dirty tracked source, untracked source, unpushed/unlanded commits, altered branch/ref/repository, protected ignored files, inside-tree artifact pointers, symlink/junction escapes and unknown ownership preserve all relevant bytes. A completed rebase uses its saved mapping without pretending original SHA is ancestor of the landed SHA. |
| V-4 | Deterministic barriers prove both reopen-versus-intent orderings, both launch-versus-retire orderings, duplicate target admission, changed attempt/report/source and started/pooled/unknown process owners. No cleanup path calls kill/stop; old coordinates remain fenced after an admitted deletion. |
| V-5 | Real isolated runner Git tree and dispatcher: guarded request removes an eligible mirror with ignored evidence; wrong task/path/ref/store/request digest or missing preservation proof cannot remove it. Existing legacy request behavior is unchanged; unsupported new operation yields pending residue without blocking ordinary task launch. |
| V-6 | Local succeeds while runner offline; durable query says local Complete/mirror Pending. Restart/reconnect deletes the mirror exactly once through its own receipt; local path is never revisited destructively. Reverse partial outcomes also retain correct facts. |
| V-7 | Crash boundaries: Done save/wakeup, target intent/before command, set-aside/unregister, partial bytes, local result save, remote accept/response loss, and report save/UI event. New service instances recover exact work without resetting budget, deleting a replacement path or inventing absence. |
| V-8 | Existing residue API/script reports card generation, task attempt, local facts and each mirror reason independently; preview and pre-cutoff historical Done rows do not delete. Action budget and persisted cooldown bound repeats. SourceLanding exclusions are explicit. |
| V-9 | Windows native locked-file and junction fixtures exercise real denial/partial-delete/retry behavior with isolated owned children; outside sentinel bytes survive; all owned children are joined before teardown. |
| R-1 | Existing close/sweep tests keep started delegates alive and affect only genuinely bound tasks. |
| R-2 | Existing ignored-content, manual retirement and landing cleanup contracts remain: protected content held; lease/receipt required; publication unchanged on cleanup refusal/retry; origin refs retained. |
| R-3 | Existing workspace reservation, report-store and remote sync/publish behavior remain, including independent session liveness and scope/identity refusal. |

Use real isolated Postgres schemas for transaction races, real disposable Git
repositories for source/ignored bytes, and the isolated runner dispatcher for
remote execution. Fake transport is appropriate for deterministic offline/lost-ACK
boundaries, but cannot stand in for the V-5 removal or V-9 native filesystem results.
Tests must assert refusal **and unchanged sentinel/source bytes**, or successful
directory/registration removal and surviving artifacts, not just an enum value.

TestDesign must design method-scoped positive controls for each authority/preservation
guard and each recovery outcome. Mutation that only causes a build/fixture error
or selects zero tests is not red. Include controls for removing the Done guard,
dropping the inside-tree pointer check, allowing publication without proof,
collapsing local/mirror Complete, bypassing the process fence, and resetting a spent
command slot. Keep SourceLanding PC execution in its existing later stage and
external evidence root; do not mutate or delete its snapshots in ordinary cleanup.

### Delivery inventory

The producer is the residue coordinator; durable destination is the cleanup target,
existing attempt journal and task event, correlated by obligation/target/operation
IDs. Reader is the residue run API and operator script. Save-before-invalidation
and restart readback are V-7/V-8 acceptance. `AgentTaskChanged` is a refresh hint,
not a delivery receipt. No new caller-session or external notification is in scope,
so no transcript receipt is claimed. If the scope changes to deliver one, TestDesign
must add real `SessionMessageQueue` busy/eligible recipient and crash/enqueue tests
with the full matching UserPrompt before the checkpoint list is frozen.

### Execution and evidence

The portable groups run on the dynamically selected ordinary lane, using isolated
outputs and test data; no named fleet location is a plan dependency. CP-8 is the
only OS-pinned group. TestDesign must split any native runner fixture requiring
another OS into a named lane rather than silently skipping it. Read runner defaults
and catalogue immediately before dispatch. Run all checkpoint drivers through
`tools/Antiphon.Checkpoints` (`run --plan ... --expected-source-sha <pushed SHA>`),
one owned run per committed slice group; await `wait` until exit is not 75. Bootstrap
`dotnet run`/other build drivers through `scripts/build-slot.ps1`; the checkpoint
executor obtains its own slots. Exit 4 is not run/blocked, never an unleased retry.

Freeze source during runs. Use the table as the closed ordinary list after
TestDesign freezes it; do not substitute a full assembly run. Re-run only failed
rows/affected tests after a repair and verify any inherited failure at the base
commit with the same failing filter. Keep full unedited CHECKPOINT lines, tested
SHA, counts and source/build provenance in the stored Result. Generated TRX/JSON/
logs stay ignored. Code/Review run `scripts/check-evidence-diff.ps1` over their
entire task range. Remove only the run's own `bin-c1017-*/` output directories after
owned execution stops. Review verdicts cover introduced regressions; unrelated
inherited defects are reported separately with base evidence.

### Checkpoints

Proposed groups for TestDesign to freeze. Every row has one isolated build and
one exact filter. `Min` below counts required new tests (or a conservative positive
regression floor), not internal assertions; TestDesign must replace these floors
with its complete argument-expanded rosters and named method expectations. The
group name identifies its lane. Escaped pipes are Markdown escaping only. All rows
are serial to keep native/process work and schema ownership simple.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1017-s1/` | portable-lifecycle | `/*/*/(CardDoneWorktreeCleanupTests*)\|(CardTaskSettlementTests*)\|(ClosedCardSweepTests*)/*` | V-1, V-4, R-1 | all listed; at least 12 new lifecycle methods; 0 failed/skipped | 12 | 7 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1017-s2/` | portable-disposal | `/*/*/(CompletedCardWorktreeRemovalTests*)\|(SettledWorktreeRemovalTests*)\|(WorktreeRemovalAuthorityTests*)\|(AgentReportStoreTests*)/*` | V-2, V-3, R-2, R-3 | all listed; at least 12 new removal methods; 0 failed/skipped | 12 | 9 | true |
| CP-3 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1017-runner/` | portable-runner | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(RunnerWorkspaceServiceTests*)\|(PhoneHomeCommandDispatcherTests*)/*` | V-4, V-5, V-7, R-3 | all listed; at least 10 new guarded-operation methods; 0 failed/skipped | 10 | 9 | true |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c1017-mirror/` | portable-mirror | `/*/*/(CardWorktreeMirrorCleanupTests*)\|(RemoteWorktreeMirrorTests*)/*` | V-5, V-6, R-3 | all listed; at least 8 new mirror methods; 0 failed/skipped | 8 | 6 | true |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1017-recovery/` | portable-recovery | `/*/*/(CardWorktreeCleanupRecoveryTests*)\|(WorktreeResidueRecoveryTests*)\|(WorktreeResidueSweepTests*)\|(WorktreeLandingCleanupRetryTests*)/*` | V-6, V-7, V-8, R-2 | all listed; at least 10 new recovery methods; 0 failed/skipped | 10 | 9 | true |
| CP-6 | S4 | `tests/Antiphon.Tests -> bin-c1017-reporting/` | portable-reporting | `/*/*/(CardWorktreeCleanupAcceptanceTests*)\|(WorktreeResidueEndpointTests*)\|(WorktreeResidueScriptTests*)\|(WorktreeResidueRegistrationTests*)/*` | V-1, V-2, V-6, V-8 | all listed; at least 5 composed acceptance methods; 0 failed/skipped | 5 | 8 | true |
| CP-7 | all | `tests/Antiphon.Tests -> bin-c1017-regression/` | portable-reservations | `/*/*/(WorkspaceReservationLivenessTests*)\|(WorktreeRetirementRaceTests*)\|(TaskWorktreeRetirementTests*)/*` | V-4, R-1, R-2, R-3 | all listed; no broad unrelated suite; 0 failed/skipped | 1 | 10 | true |
| CP-8 | all | `tests/Antiphon.Tests -> bin-c1017-windows/` | windows-native-removal | `/*/*/CompletedCardWorktreeRemovalWindowsTests/*` | V-9 | Windows lane; all 4 native cases; 0 failed/skipped | 4 | 8 | true |

Planning estimate: 66 minutes ordinary execution across the groups, plus authoring,
queue wait, baseline failures and later PCs. This is an estimate, not measured cost.
TestDesign must confirm platform availability of each existing selected class;
move genuinely native rows to their proper lane with explicit revised filters,
never accept silent skips as cross-platform evidence.

## Activation order and operational acceptance

1. Land the final TestDesign artifact, then the implementation slices with ordinary
   receipts and regression-only Review. Ensure CARD-1015 is in the implementation
   base. Confirm publication separately from activation; no cleanup executes from
   this plan branch. Keep schema additions backward-compatible and execution off.
2. Deploy the guarded runner operation/journal before enabling the server's new
   disposal path. Use the existing staged runner rollout and canonical AppHost
   procedures; verify actual loaded SHAs through their status/version endpoints.
   Do not edit deployment scripts in this card or infer activation from `/health`.
   Older runners continue to run tasks; their cleanup waits with visible reason.
3. Deploy server schema, coordinator, report surface and docs with `Execute=false`.
   Read effective settings, runner catalogue and defaults. Use
   `pwsh -NoProfile -File scripts/worktree-residue.ps1 -Action Preview` with
   `-BaseUrl` resolved from the environment and an explicit `-BoardId` or
   `-ProjectId` (the script does not inherit `ANTIPHON_API` itself). Inspect every
   result page for the chosen canary's task attempts,
   current Done revision, proof, artifact locations and both endpoint states.
4. With the operator's settings authorization, set the Done-cohort cutoff to the
   activation time and enable the existing execution setting. Trigger the existing
   job for one controlled newly Done cohort, within its action budget. No hidden
   alternate executor is permitted. A Done card with an active task must remain
   visible as held and its session must continue untouched.
5. Confirm one local+mirror removal using actual directory/registration absence,
   endpoint receipts and persisted operator readback; verify full report and
   deliverable bytes still usable and origin branch still present. Repeat the
   acceptance with the canary runner temporarily unavailable through an isolated
   fixture/canary setup: local Complete plus mirror Pending, then mirror completion
   after recovery. Do not interrupt unrelated production sessions to manufacture it.
6. Only after that receipt, preview the historical Done backlog and authorize a
   wider cutoff/bounded batches. Preserve dirty/unpublished, ambiguous and live
   trees as explicit refusal rows. Report counts per endpoint and unresolved
   reasons; never claim that every completed-card tree is gone because the job ran.

Pause execution by restoring `Execute=false` if source/artifact loss, wrong
identity, a live-owner deletion or unrecorded removal is observed. This stops new
intents; it cannot resurrect deleted raw evidence. Finish reconciliation of already
admitted commands and retain their fences/journals. Repair source in a new Code
slice and requalify the affected rows; do not delete journals or reset command slots.

## Collision notes and handoff

Live board-scoped task inspection on 2026-10-03 confirmed CARD-1011 Code
`b657a1e2-767d-4bcb-b25a-1cf2ed925be3`, CARD-0959 TestDesign re-freeze
`4170230f-8bf7-4f26-ab9d-41b0e73b4864` and CARD-1008 Final Review
`ca3d4a6b-c74e-42ae-8f91-eee6ca8a95b1` dispatched. An older CARD-1008
TestDesign task was also Blocked. These are point-in-time observations; recheck
before Code dispatch and defer an actual same-file/scope collision.

- CARD-1011 owns the orchestrator bundle, provider docs and routing tests. This
  plan edits only its own Markdown artifact. Implementation must not change
  `server/Bundles/orchestrator.md`, provider routing or those tests. Coordinate any
  overlap in `docs/orchestration-loop.md`; its cleanup paragraph is the intended
  doc scope here.
- CARD-0959 owns runner Codex probe observations and associated composition/
  contract work. S3 touches general runner contracts/dispatcher and possibly
  runtime composition for the path fence: land the inert observation slice first
  or serialize overlapping files. Do not restore its removed version-floor
  admission policy or add one under this card.
- CARD-1008 owns `scripts/deploy-server2.ps1` and `scripts/c590-remote.sh`.
  Neither is an implementation target here. Follow its landed rollout procedure
  for activation and coordinate access to runner lifecycle; do not combine volume
  reclamation with task-worktree evidence deletion.
- CARD-0824 and any resumed 0459/0665/0692 cleanup implementation overlap the
  worktree executor and schema. Share this ownership boundary before dispatch;
  never run competing cleanup implementations against the same source area.

Next: **test-design**. Freeze the method/PC inventory, actual helper/runner fence
owners, executed rosters and OS split; retain D-1 through D-12 and the explicit
ordinary-versus-SourceLanding boundary. No product decision is requested to write
that artifact. Actual fleet execution remains subject to the activation sequence.
