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

## Proposed verification requirements (Plan stage)

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

### Proposed checkpoints (superseded by the freeze below)

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

## Verification design

TestDesign freeze by task `044f2398-6156-4abd-9593-fc79cae6934b`, 2026-10-04,
against start `57fa586162b0f2ee1cb4fa97cbeee4f5ef86f8c3`. This appended section
supersedes only the provisional verification section and its CP table. D-1 through
D-12, the fix design, implementation slices, activation order and SourceLanding
exclusion above remain controlling. The two old verification headings were renamed
because the actual importer selects the **first exact** `### Checkpoints` heading.

This is a specification, not executed evidence: **zero builds, tests or mutations**
are commissioned for this docs-only task. Counts below are required TUnit results
derived from source attributes and the frozen new method roster. Code implements
and executes V/R; ordinary Review judges it before land; SourceLanding Mutation
executes the independent PCs after land. No tests are added to
`Antiphon.Tests.Checkpoints`; `scripts/lib/checkpoint-usage.ps1`'s independent
**377** census literal stays unchanged.

### Inspection

Bodies and nearest fixtures read, including setup, assertions and teardown:

| Bodies read | Boundaries -> coverage |
|---|---|
| `CardTaskSettlementTests` (8), `ClosedCardSweepTests` (2), their private `World` and `TestDbFixture.CreateIsolatedSchemaAsync` | actual binding, started/unstarted, archive and tracker Done writes -> V-1/V-4, R-1 |
| `SettledWorktreeRemovalTests` (26 methods/78 results), `WorktreeRemovalAuthorityTests` (3/9), `SettledRemovalHarness`; `LandingGitFixture.InitializeAsync`, Git hooks/registration removal, `LandingSafetyHarness.InitializeAsync/BuildServices/RestartServicesAsync` and crash-worker setup | real local Git, exact authority, remote containment, first/final inspection, set-aside and CAS -> V-2/V-3/V-7/V-9, R-2 |
| `AgentReportStoreTests` (7/28) and `ReportWorkspace` | canonical report bytes, distinct legacy author detail, durable root and unavailable pointers -> V-2/V-3, R-3 |
| `RunnerWorkspaceServiceTests`, including `Scratch` and all 25 selected Mirror/Publish/Remove methods; the three `Workspace_*` dispatcher methods and `Dispatcher/RecordingRuntime` | real runner Git, legacy compatibility, typed routing, deliberate fake process surface -> V-5, R-3 |
| `RemoteWorktreeMirrorTests` (8), `ScriptedGit/FakeProgressGit/FakeLeases/UnusedDirectory` | desktop FF/refusal behavior; these fakes cannot prove mirror deletion -> V-5/V-6, R-3 |
| `WorktreeResidueRecoveryTests.C459_IntentCommittedBeforeGit/C459_ComponentsAreIndependent/C459_OutageKeepsFence`, restart helper and worker-death body; `WorktreeResidueSweepTests` populated and empty-world bodies | committed intent, separate components, persistent fence, insufficient empty-inventory assertions -> V-6/V-7/V-8 |
| first seven methods of `WorktreeLandingCleanupRetryTests` through `C459_LostWakeupRecoversSameRequest`, request/queue harness and publication counter | cleanup-only real queue and immutable publication -> V-7, R-2 |
| `WorkspaceReservationLivenessTests` (8/20); `WorktreeRetirementRaceTests.C459_OneRetirementClaim/C459_CreateReservesWorkspace` and `RaceWorld` setup/release/teardown; `TaskWorktreeRetirementTests` (17/38) and `World` | live owners, claim/use exclusion, legacy manual authority unchanged -> V-4, R-3 |
| `WorktreeResidueEndpointTests` (6), `WorktreeResidueScriptTests` (1), `WorktreeResidueRegistrationTests` (3) and its actual Hangfire worker setup | existing API/CLI/scheduler contracts; empty or literal assertions are regression coverage only -> V-8, R-3 |
| `PhoneHomeLaunchTransportTests` pre/post-ack bodies and `PhoneHomeTestHost.StartAsync/RegisterAsync/ConnectPeerAsync/WaitLiveAsync/DisposeAsync` | actual loopback WebSocket route and reconnect; scripted peer needs replacement by dispatcher for recipient proof -> V-5/V-7 |
| `WorktreeLockDiagnosticsWindowsTests` and complete `WorktreeLockChild` | native sharing errors, pipe readiness, owned release and joined teardown -> V-9 |
| `GuardedWorktreeRemoval` entire body, `PhoneHomeRunnerClient` request path, importer `PlanTableImporter/ManifestValidator/CheckpointManifest/AfterSelector`, import CLI body | final authority, no-follow/set-aside, runtime queue, exact manifest semantics |
| owners `docs/testing-and-build.md` (manifest/runner/build slots), project conventions, orchestration cleanup/SourceLanding contract, lifecycle transitions, HTTP front door, session reservation/custody invariants | closed execution scope, receipt provenance, process and snapshot exclusions |

Required setup absent today, to implement within S1-S5 before running its CP:

- `tests/Antiphon.Tests/TestHelpers/CompletedCardCleanupFixture.cs`: compose the
  existing real Git/landing harness, a cloned PostgreSQL database, current Done
  revisions and new cleanup rows. Put the canonical fixture repository beneath
  `.antiphon/test-output/c1017/<unique-id>/`, using LandingGitFixture's root argument,
  so real AgentReportStore durability checks run without a temporary-root override.
  Only explicitly managed child worktrees are deletion targets.
  Despite the historical name, `IsolatedTestSchema`
  is **a cloned database**, not a shared-schema transaction. Use new DbContexts for
  every observation/restart. Migration tests apply the generated migration to the
  predecessor schema with active, historical Done, archived-Done and canceled rows.
- The artifact-survival helper belongs under `server/Infrastructure/Files/` and
  uses `IAgentReportStore`; test it through real report storage and real files.
  Existing `ArtifactsRetrievableAsync` existence checks are insufficient.
  `.antiphon/task-<id>.md` may contain author detail beyond Result. If its bytes
  differ, model it as an actual deliverable: hold until an existing durable
  external pointer preserves those bytes. Do not silently replace it with Result.
  No generic retention copier is requested.
- `tests/Antiphon.SessionRunner.Tests/CompletedCardRunnerFixture.cs`: real
  `RunnerWorkspaceService`, `PhoneHomeCommandDispatcher`, scratch origin, distinct
  repository/worktree/state roots and a journal outside every deletion target.
  The new runner-local journal/fence belongs in `src/Antiphon.SessionRunner/`;
  bind it to `SessionRunnerRuntime.StartAsync` **and re-adoption**, through
  `PhoneHomeRuntimeAdapter`. No server Infrastructure reference is allowed.
- Application acceptance fixture: reuse `PhoneHomeTestHost` registration and
  socket framing but pump requests through the **real runner dispatcher and
  workspace service**, not `PhoneHomeScriptedPeer` canned success. Use the real
  `PhoneHomeLiveConnection` outbound channel, `AgentTaskLandQueue` and a real
  Hangfire worker with isolated storage. These are three separate handoffs.
- Use existing EF save/transaction interceptors and real Git before/after hooks
  for deterministic barriers. Add internal I/O fault hooks at journal commit,
  set-aside rename, unregister, per-component persistence and outbound enqueue
  where missing; hooks cannot decide eligibility. Child crash workers are locally
  inherited and parent-owned, pause through a ready pipe, and are killed/joined by
  their test owner only. No sleeps decide race winners.
- `Antiphon.Tests` and runner tests retain their own assembly-local
  `ParallelLimiter<ProcessSpawnLimit>`; all CPs are serial. All test-owned process
  output is drained before teardown. Use loopback ephemeral listeners and
  `ProductionRunnerGuard` for any host booting real Program.
- Windows native fixtures require ModernConPty and a test-owned PtyHost witness.
  CARD-1020 owns `DirectSessionRunnerClient` teardown: reuse its landed helper or
  an isolated test-local owned-child fixture, never change production disposal or
  kill someone else's lingering host. A lock probe proves a sharing conflict,
  not process ownership. Linux unlink success cannot substitute for this lane.

No fixture may fake the success facts it asserts. A refusal test begins with a
fully eligible baseline except for its named defect, and independently snapshots
source/sentinel bytes and Git refs. For first-read guards, assert the inspection
stopped before a second full read so a surviving later guard cannot mask the PC.
For every new method below, use explicit named vector loops with **one** `[Test]`
result, no `[Arguments]`, hidden data sources, repeats or platform skips. Boundary
loops are not additional execution counts.

### Delivery inventory

Durable identity throughout is
`(CardId, DoneRevisionId, TaskId, Attempt, WorkspaceIdentity, EndpointId,
OperationId, RequestDigest)`. Record the actual land/retirement request ID when
one is linked. Producer acceptance, queue insertion, events and transport ACKs
are never the completion oracle.

| Path | Producer -> recipient and persistence | Recovery and observable recipient receipt |
|---|---|---|
| Done wakeup | actual card/Done writer -> Hangfire residue job; Done revision and obligation transaction precedes enqueue | restart/recurring sweep repairs dropped or failed enqueue; real worker removes eligible owned tree and API readback joins its operation and surviving artifacts. `A.DoneQueueBusyRecipient` holds the worker on another job; `A.DoneQueueAlreadyEligible` starts it waiting. G/PC `LostWakeupRecovers`, `EnqueueFailureRemainsRecoverable`, `DoneSaveIsAtomic`. |
| Publication cleanup | residue coordinator -> real `AgentTaskLandQueue` -> cleanup-only land worker; durable request links target before channel write | busy repository recipient later acquires its lease; already-eligible recipient executes immediately. Assert actual directory, registration and set-aside absence, unchanged publication ID/SHA and no new push. `A.PublicationQueueBusyRecipient/PublicationQueueAlreadyEligible`; G/PC `PublicationIsImmutable` and `EnqueueFailureRemainsRecoverable`. |
| Guarded mirror | `CardWorktreeMirrorCleanupService` -> `PhoneHomeRunnerClient` -> real live-connection channel/WebSocket -> dispatcher -> real runner Git/filesystem; server endpoint intent before send, runner journal before mutation, typed receipt before reply | busy runner workspace lease and already-eligible runner both execute through real channel. Restart server and runner separately; lost response repeats/queries same operation. Assert matching complete receipt plus independent filesystem/registration observations at recipient. `A.PhoneHomeQueueBusyRecipient/PhoneHomeQueueAlreadyEligible`; G/PC `RunnerIntentBeforeIo`, `LostAckReusesOperation`, receipt identity checks and `AckIsNotRemoval`. |
| Operator readback | coordinator -> committed endpoint/run/task event -> existing residue GET and `scripts/worktree-residue.ps1`; save precedes invalidation | fresh host/context reads every endpoint and exact reason; actual script consumes fixture HTTP response and prints distinct local/mirror outcomes. `AgentTaskChanged` is only a refresh hint. G/PC `OperatorOutcomeIsDurable`, `EveryIntentIsReportable`, `NoOpaqueBytesInReport`. |

`A` denotes `CardWorktreeCleanupAcceptanceTests`, with the `C1017_` method prefix.
Its seventh additional method, `C1017_CrashEveryHandoff`, loops these cuts:
before Done transaction commit; after commit/before wakeup; before/after Hangfire
enqueue; before/after land-queue write; before remote send; after runner intent;
after rename; after unregister; midway through ignored-file deletion; after local
physical result/before save; after remote physical result/before ACK; after ACK/
before server save; after outcome save/before UI invalidation. Each cut rebuilds
the failed service/process, drives the **real** owning queue, and ends at the
recipient assertions above. Also inject enqueue exceptions at both enqueue sites,
socket send failure, response loss and result-save failure. No cut ends at a row
or ack. Existing intent-budget rules still apply; safe refusal/debt is the expected
terminal observation when fresh authority is unavailable.

The busy tests release an **owned fixture** blocker; cleanup itself never releases
or kills it. Three queues × busy/eligible = six single-result methods, plus the
single crash-loop result. A loopback real queue proves production transport and
consumer wiring, but not deployment connectivity. In-memory Hangfire proves worker
handoff, not durable scheduler storage; PostgreSQL cleanup obligations plus fresh
worker recovery prove the lost-wakeup contract. FakeTimeProvider advances only
cooldown/deadlines. Scripted errors are fault injection, not evidence of removal.
No new session-input or outbound notification is designed. Consequently no
UserPrompt receipt is claimed; adding session input requires a new freeze with
busy/eligible real SessionMessageQueue tests ending at a complete matching
UserPrompt transcript.

### Proves it works now

- V-1: actual Done writers and discovery persist the immutable generation, recover
  missed callbacks, deduplicate and discover later attempts | real PostgreSQL and
  card services | CP-1, CP-6, CP-7 | current Done alone authorizes; migration/backfill
  is inventory-only before cutoff; archive of Done preserves the same generation.
- V-2: whole eligible tree including ignored evidence disappears after Done |
  real Git/files/report store | CP-2, CP-7, CP-12 | canonical Result, distinct
  `.antiphon/task-<id>.md` detail and external deliverables survive byte-for-byte;
  no raw-evidence copy is created.
- V-3: unique source, reports, ignored protected content, root and publication
  identities remain protected | Git/filesystem | CP-2, CP-10, CP-12 | refusal
  preserves bytes/refs; genuine rebase mapping permits safe cleanup.
- V-4: both reopen/intent and launch/retire orders; active and unknown processes |
  DB barriers and runner runtime | CP-1, CP-3, CP-8, CP-9, CP-10, CP-11 |
  exactly one admission winner; no cleanup-triggered stop, kill, pool or detach.
- V-5: guarded typed operation reaches runner recipient, rejects identity drift
  and never falls back destructively | actual channel/dispatcher/Git | CP-3,
  CP-4, CP-5, CP-7, CP-11, CP-12 | independent receipt and removal facts; unsupported
  operation leaves pending residue while ordinary launch remains possible.
- V-6: local Complete/mirror Pending and reverse ordering remain independent |
  restarted coordinator and two recorded mirrors | CP-5, CP-6, CP-7, CP-12 |
  every unresolved endpoint remains visible and independently recoverable.
- V-7: each durable/I/O/queue cut above recovers exact intended work | process
  death, transactional failure, transport loss | CP-6, CP-7, CP-9, CP-10 |
  no spent-slot reset, replacement deletion, repeated publication or false absence.
- V-8: operator can retrieve all intent/refusal/partial/completion facts and
  execution remains bounded | real API/script/Hangfire with populated fixtures |
  CP-6, CP-7 | defaults Execute=false, cutoff, settling floor, shared budget and
  durable cooldown enforced; SourceLanding remains excluded.
- V-9: Windows locked set-aside, junction and lingering PtyHost incidents |
  native Windows desktop lane | CP-10, CP-11, CP-12 | outside bytes and live process
  survive; exact owned retry completes only after fixture releases its holder.

The mandatory incident fixtures are `L.DirtyTrackedIsHeld`,
`L.UnpushedCommitIsHeld`, `L.EvidenceNeedsDoneDisposition`,
`K.SetAsideRecoveryPreservesAuthority`, `M.MirrorBehindPushedTip` and
`W.LingeringPtyHostIsReported`. The set-aside incident uses the real refusal reason
`worktree_removal_incomplete` and the illustrative leaf above **under an owned
scratch root**, never the actual incident directory. Behind-tip mirrors are
inventoried at their own exact H; proof must preserve H. No reset or fast-forward
is required merely to delete a proven redundant tree. An obsolete request expecting
P against H refuses until the coordinator records fresh exact H evidence.

### Guards the regression

- R-1: close/sweep retain started delegates and only affect actual bound cards |
  all 8 `CardTaskSettlementTests` and both `ClosedCardSweepTests` | started task
  statuses preserved, stopper excludes their sessions, foreign task events unchanged.
- R-2: existing manual/legacy removal and publication authority remain |
  all 78 `SettledWorktreeRemovalTests` (Windows), 9
  `WorktreeRemovalAuthorityTests`, 38 `TaskWorktreeRetirementTests`, and the
  seven cleanup-retry methods below | required receipt/lease and protected bytes,
  immutable publication, safe ref CAS; manual Mutation/repair exclusions unchanged.
- R-3: report storage, reservation liveness, remote FF/publish and operator
  surfaces remain | the explicit existing rosters below | exact report bytes,
  no dirty/divergent reset, proper liveness predicate and expected API/CLI behavior.
  Existing empty-inventory tests are retained only as regressions; the new
  populated acceptance methods provide the decisive safety assertions.

### Guard inventory

Each row is one safety-critical invariant with exactly one independent PC.
Method names and field comparisons below bind the proposed helpers to the production
owners already named by S1-S5. If Code splits a check into independently bypassable
guards, it must extend this inventory before claiming completion.

| Guard | Plan reference, owner and invariant | Positive control |
|---|---|---|
| G-1 | D-1; `CardWorktreeCleanupService admission`: InProgress, Review, Canceled and archive-only each retain an otherwise eligible tree; archived Done retains the original generation. | PC-1 |
| G-2 | D-1/D-7; `CardWorktreeCleanupService admission`: Done G1, reopen, Done G2; replay G1 against otherwise identical coordinates. | PC-2 |
| G-3 | D-1/D-3; `CardService Done save and cleanup obligation persistence`: fail save immediately before commit, then retry; observer sees neither row on rollback and both rows after commit. | PC-3 |
| G-4 | D-3; `CardWorktreeCleanupService discovery`: two concurrent contexts discover the same current Done revision. | PC-4 |
| G-5 | D-2; `CardWorktreeCleanupService discovery`: discover attempt 1, later add attempt 2 after exhausted inventory; both discovered independently. | PC-5 |
| G-6 | D-2; `CardWorktreeCleanupService discovery`: same title/path prefix on another board/card; only actual CardId binding qualifies. | PC-6 |
| G-7 | D-2/D-8; `CardWorktreeCleanupService eligibility`: Queued, Dispatched, Working, Blocked; later terminal task becomes eligible without cancellation. | PC-7 |
| G-8 | D-2; `CardWorktreeCleanupService ownership`: duplicate full path owner and same short-ID prefix; all other evidence valid. | PC-8 |
| G-9 | D-2; `CardWorktreeCleanupService workspace-kind branch`: Shared/ReadOnly and repair borrowing source; independent repair worktree positive arm. | PC-9 |
| G-10 | D-2; `CardWorktreeCleanupService SourceLanding branch`: fully restored sealed SourceLanding task bound to Done; ordinary unsourced Mutation positive arm. | PC-10 |
| G-11 | D-2; `CardWorktreeCleanupService managed-tree classification`: canonical, land child, reusable slot, external evidence and runner-state roots; ordinary legacy positive arm. | PC-11 |
| G-12 | D-7; `CardWorktreeCleanupService transactional admission`: barrier after inspection; reopen commits before intent; release barrier. | PC-12 |
| G-13 | D-7; `WorkspaceReservationJournal committed cleanup fence`: intent commits before reopen; old tree may finish, stale continuation rejected and new coordinates admitted. | PC-13 |
| G-14 | D-6/D-8; `WorkspaceUseAdmission and cleanup eligibility`: live follow-up, child, repair, land and session reservation singly present. | PC-14 |
| G-15 | D-6; `CardWorktreeCleanupService executor selection`: unfinished land/repair/recovery row; confirmed publication uses cleanup-only executor as positive arm. | PC-15 |
| G-16 | D-4/D-7; `CardWorktreeCleanupService final admission`: change task attempt after inspection but before intent. | PC-16 |
| G-17 | D-6/D-7; `CardWorktreeCleanupService final admission`: change complete Result after external report proof but before intent. | PC-17 |
| G-18 | D-7; `CardWorktreeCleanupService transaction scope`: hold Git/runner at an I/O barrier; second context commits same-card reopen before release. | PC-18 |
| G-19 | D-5; `WorktreeIgnoredContentGate.RetainAsync`: same ignored TRX/checkpoint/report/build tree before Done and after a genuine Done intent. | PC-19 |
| G-20 | D-5/D-7; `WorktreeRemovalEvidence final CardDone read`: set disposal flag but no committed endpoint intent. | PC-20 |
| G-21 | D-4; `GuardedWorktreeRemoval.AuthorityAsync lease predicate`: null, forged, disposed and foreign-repository lease; each otherwise eligible. | PC-21 |
| G-22 | D-4; `GuardedWorktreeRemoval.Matches SHA predicate`: source commits after captured SHA; branch remains same. | PC-22 |
| G-23 | D-4; `GuardedWorktreeRemoval.AuthorityAsync repository predicate`: same branch/SHA in a second real repository. | PC-23 |
| G-24 | D-4; `GuardedWorktreeRemoval.AuthorityAsync path predicate`: receipt and request name different registered trees with identical SHA. | PC-24 |
| G-25 | D-4; `GuardedWorktreeRemoval.AuthorityAsync ref predicate`: same HEAD but another symbolic branch; detached HEAD separate vector. | PC-25 |
| G-26 | D-4; `GuardedWorktreeRemoval.Matches common-directory predicate`: same SHA but foreign common directory. | PC-26 |
| G-27 | D-4; `GuardedWorktreeRemoval.Matches git-admin predicate`: same common/SHA but another registration admin. | PC-27 |
| G-28 | D-4; `GuardedWorktreeRemoval first content inspection`: staged and unstaged tracked bytes, valid Done and external proof. | PC-28 |
| G-29 | D-4; `GuardedWorktreeRemoval first content inspection`: untracked nonignored source with all other evidence valid. | PC-29 |
| G-30 | D-4; `GuardedWorktreeRemoval fresh remote containment`: clean unique commit absent from every remote ref. | PC-30 |
| G-31 | D-4; `TaskWorktreeRetirementService published-target preservation`: commit pushed only to task branch, absent from configured published target. | PC-31 |
| G-32 | D-4; `AgentTaskLandingProtocol cleanup receipt mapping`: original != landed SHA; genuine saved mapping succeeds, different task/operation mapping refuses. | PC-32 |
| G-33 | D-4; `GuardedWorktreeRemoval.AuthorityAsync remote refresh`: remote loses containment after first inspection; unreadable response separate vector. | PC-33 |
| G-34 | D-5; `WorktreeIgnoredContentGate protected classification`: protected secret-shaped fixture, user-local file, opaque bin-private, unfamiliar evidence path; harmless synthetic bytes only. | PC-34 |
| G-35 | D-4/D-5; `GuardedWorktreeRemoval final inspection`: inject tracked/untracked source after first read; separate ignored-protected late file. | PC-35 |
| G-36 | D-5; `GuardedWorktreeRemoval evidence-set comparison`: add evidence after preservation/first classification; disposable build churn is positive arm. | PC-36 |
| G-37 | D-5; `WorktreeNoFollowDelete.Delete`: swap an ignored directory to an outside symlink after last inspection. | PC-37 |
| G-38 | D-4; `GuardedWorktreeRemoval managed-root check`: outside root, sibling prefix, dot segment and reparse root; exact root child positive arm. | PC-38 |
| G-39 | D-4; `GuardedWorktreeRemoval nested registration check`: registered nested worktree with its own source sentinel. | PC-39 |
| G-40 | D-4; `GuardedWorktreeRemoval registration count`: real directory with missing registration. | PC-40 |
| G-41 | D-4; `GuardedWorktreeRemoval locked registration`: real locked registration. | PC-41 |
| G-42 | D-4; `GuardedWorktreeRemoval prunable registration`: registration reports prunable although source remains present. | PC-42 |
| G-43 | D-6; `artifact survival helper under server/Infrastructure/Files`: existing ResultFilePath below original or recorded set-aside root, and external sibling-prefix positive arm. | PC-43 |
| G-44 | D-6; `artifact survival helper canonical report verification`: external report exists with truncated/different bytes. | PC-44 |
| G-45 | D-6; `artifact survival helper deliverable extraction`: .antiphon/task-<id>.md has distinct extra detail absent from Result; hold until exact external durable pointer exists. | PC-45 |
| G-46 | D-6; `artifact survival helper deliverable containment`: existing deliverable below local tree, mirror or set-aside; external file positive arm. | PC-46 |
| G-47 | D-6; `artifact survival helper deliverable readback`: external missing, access-denied and symlink-to-deletion-root pointers. | PC-47 |
| G-48 | D-7; `GuardedWorktreeRemoval final AuthorityAsync call`: revoke/change the unissued committed authority after final status read. | PC-48 |
| G-49 | D-4/D-9; `GuardedWorktreeRemoval.CompleteBranchAsync`: move source ref after precheck, before update-ref. | PC-49 |
| G-50 | D-4/D-9; `local cleanup ref destination`: eligible successful removal with task ref and published ref in bare origin. | PC-50 |
| G-51 | D-4; `local cleanup command selection`: eligible and refused vectors record all Git commands. | PC-51 |
| G-52 | D-10; `PhoneHomeCommandDispatcher guarded cleanup dispatch`: guarded payload sent to new operation; legacy operation behavior remains separately selected. | PC-52 |
| G-53 | D-10; `runner-local cleanup journal admission`: otherwise-valid request names another task. | PC-53 |
| G-54 | D-10; `runner-local cleanup journal admission`: same task, wrong attempt. | PC-54 |
| G-55 | D-9/D-10; `runner-local cleanup journal admission`: same path on replacement runner store. | PC-55 |
| G-56 | D-4/D-10; `RunnerWorkspaceService guarded removal admission`: path is real but belongs to another recorded repository. | PC-56 |
| G-57 | D-4/D-10; `RunnerWorkspaceService guarded removal admission`: outside path, sibling prefix or valid different task path. | PC-57 |
| G-58 | D-4/D-10; `RunnerWorkspaceService guarded removal admission`: mirror changed symbolic branch at same SHA. | PC-58 |
| G-59 | D-4/D-10; `RunnerWorkspaceService guarded removal admission`: mirror ahead of expected SHA with unique committed bytes. | PC-59 |
| G-60 | D-4/D-10; `RunnerWorkspaceService guarded preservation proof`: raw SHA without bound publication proof; valid rebase mapping positive arm. | PC-60 |
| G-61 | D-4; `RunnerWorkspaceService guarded content inspection`: tracked staged/unstaged and nonignored untracked source. | PC-61 |
| G-62 | D-5; `runner-local ignored-content gate`: ignored evidence eligible only with CardDone; protected/unknown ignored synthetic files held. | PC-62 |
| G-63 | D-5; `runner-local no-follow removal helper`: late symlink swap after final inspection. | PC-63 |
| G-64 | D-8; `runner-local use fence ownership admission`: terminal task/session but live owned child or pooled process; unknown identity separate vector. | PC-64 |
| G-65 | D-8; `SessionRunnerRuntime.StartAsync cleanup admission`: cleanup intent wins before ordinary start; inverse order makes cleanup refuse. | PC-65 |
| G-66 | D-8; `SessionRunnerRuntime re-adoption admission`: restart runner after durable retiring-path intent then attempt re-adoption. | PC-66 |
| G-67 | D-7/D-9; `runner-local journal intent write`: observer reads external intent while filesystem mutation is barrier-held. | PC-67 |
| G-68 | D-10; `runner-local journal operation lookup`: same operation ID replay with changed digest; exact replay returns original receipt. | PC-68 |
| G-69 | D-4/D-8; `RunnerWorkspaceService last source/use read`: late tracked file or late competing owner before removal; all initial reads valid. | PC-69 |
| G-70 | D-10; `RunnerWorkspaceService guarded request handling`: guarded command carrying Force=true and dirty source. | PC-70 |
| G-71 | D-9; `CardWorktreeMirrorCleanupService pending enumeration`: local Complete/mirror Pending; fresh coordinator after restart reaches real mirror. | PC-71 |
| G-72 | D-9; `CardWorktreeCleanupService endpoint scheduling`: offline runner with otherwise removable local tree; mirror remains pending. | PC-72 |
| G-73 | D-4/D-9; `CardWorktreeMirrorCleanupService expected-source selection`: real mirror HEAD H ancestor of preserved pushed P; exact H inventory succeeds; changed H refuses. | PC-73 |
| G-74 | D-9/D-10; `CardWorktreeMirrorCleanupService receipt validation`: valid receipt for another operation; other fields match. | PC-74 |
| G-75 | D-9/D-10; `CardWorktreeMirrorCleanupService receipt endpoint check`: valid operation with different endpoint ID. | PC-75 |
| G-76 | D-9/D-10; `CardWorktreeMirrorCleanupService receipt store check`: valid operation and endpoint but replacement store ID. | PC-76 |
| G-77 | D-9/D-10; `CardWorktreeMirrorCleanupService receipt attempt check`: valid operation/endpoint/store but different task attempt. | PC-77 |
| G-78 | D-9; `CardWorktreeMirrorCleanupService component projection`: ack/empty response/timeout/null path without deletion facts. | PC-78 |
| G-79 | D-10; `RemoteWorkspaceService guarded operation refusal`: unsupported/unknown capability leaves residue; ordinary launch remains admitted. | PC-79 |
| G-80 | D-7; `CardWorktreeMirrorCleanupService remote intent admission`: local intent finishes, reopen commits before remote intent. | PC-80 |
| G-81 | D-9; `CardWorktreeMirrorCleanupService endpoint ledger`: two proven runner/store bindings, first Complete second offline. | PC-81 |
| G-82 | D-9; `CardWorktreeMirrorCleanupService recovery request`: real runner completes deletion, socket loses response; restart server and reconnect. | PC-82 |
| G-83 | D-3/D-12; `WorktreeResidueSweepService discovery`: commit Done then drop wakeup; recurring worker executes eligible target. | PC-83 |
| G-84 | D-7/D-9; `RetirementCommandJournal recovery`: command intent persisted, worker killed before receipt; recreate coordinator. | PC-84 |
| G-85 | D-5/D-9; `GuardedWorktreeRemoval pending set-aside branch`: real Landed+Refused/worktree_removal_incomplete with owned .card-task-5fcba512.removing-ec201e2d085b1894 under scratch root and ignored evidence; restart. | PC-85 |
| G-86 | D-9; `GuardedWorktreeRemoval set-aside receipt binding`: receipt belongs to another original path/git admin/operation; valid owned aside positive arm. | PC-86 |
| G-87 | D-7/D-9; `GuardedWorktreeRemoval recovery original-path check`: owned aside present plus new directory at original path with sentinel. | PC-87 |
| G-88 | D-9; `endpoint absence reconciliation`: path absent, no matching command receipt; access-denied query separate vector. | PC-88 |
| G-89 | D-9; `cleanup endpoint completion predicate`: directory gone with registration present; then registration gone with ref retained. | PC-89 |
| G-90 | D-3/D-9; `AgentTaskLandService cleanup-only execution`: landed receipt cleanup refuses then completes through real land queue. | PC-90 |
| G-91 | D-12; `WorktreeResidueSweepService execution gate`: populated new-Done eligible local+mirror inventory with Execute=false. | PC-91 |
| G-92 | D-12; `WorktreeResidueSweepService Done cutoff predicate`: Done before, exactly at and after activation cutoff; widen cutoff deliberately later. | PC-92 |
| G-93 | D-12; `CardWorktreeCleanupService age predicate`: null completed time, 119:59.999, 120:00 and 120:00.001 minutes. | PC-93 |
| G-94 | D-12; `WorktreeResidueSweepService accepted-action accounting`: 26 due eligible targets split across publication/settled/mirror against budget25. | PC-94 |
| G-95 | D-12; `WorktreeResidueSweepService persisted retry cursor`: transient refusal, restart before due then advance clock to due; no real sleeps. | PC-95 |
| G-96 | D-12; `WorktreeResidueSweepService retry eligibility`: permanent safety refusal with unchanged evidence across two sweeps. | PC-96 |
| G-97 | D-11; `residue outcome save before AgentTaskChanged`: observe via second DB connection from event callback; restart host and GET same run. | PC-97 |
| G-98 | D-11; `cleanup ledger intent/event transaction`: crash before first I/O; GET run exposes full card-generation/task-attempt/endpoint/operation identity. | PC-98 |
| G-99 | D-11; `residue DTO/script projection`: protected synthetic SECRET_SENTINEL bytes; API and real script read populated result. | PC-99 |
| G-100 | D-6/D-11; `AgentReportStore and residue result readback`: delete eligible evidence tree; canonical full Result, distinct task-report detail and external deliverable remain exact. | PC-100 |
| G-101 | D-8; `Windows cleanup process ownership admission`: own real ModernConPty PtyHost remains alive after session row terminal; no open-file-probe authority. | PC-101 |
| G-102 | D-8; `Windows cleanup refusal handling`: live exact owned process holds fixture mirror; cleanup returns refusal with identity. | PC-102 |
| G-103 | D-9; `Windows no-follow partial-delete recovery`: FileShare without Delete causes real partial result, release holder then retry same recorded components. | PC-103 |
| G-104 | D-5; `Windows no-follow removal helper`: late junction replacement and protected root junction with outside sentinel. | PC-104 |
| G-105 | D-6; `artifact survival helper missing-report branch`: no report and no explicit missing-report disposition, then valid existing disposition positive arm. | PC-105 |
| G-106 | D-5; `GuardedWorktreeRemoval first ReparsePoints check`: evidence link present initially; valid later snapshot to avoid masking. | PC-106 |
| G-107 | D-5; `GuardedWorktreeRemoval final ReparsePoints check`: inject link after first clean inspection; no-follow deletion remains protective. | PC-107 |
| G-108 | D-2/D-10; `RunnerWorkspaceService guarded workspace-kind check`: valid registered SourceLanding creation with all ordinary cleanup fields forged. | PC-108 |
| G-109 | D-8; `runner-local process-ownership query`: query unavailable or torn owned-child record with terminal DB row. | PC-109 |
| G-110 | D-3/D-12; `WorktreeResidueSweepService enqueue failure handling`: fail Hangfire enqueue and separately AgentTaskLandQueue enqueue after durable request save; recurring recovery reaches actual deletion. | PC-110 |
| G-111 | D-9; `CardWorktreeCleanupService endpoint outcome persistence`: physical local removal succeeds; fail result transaction then restart. | PC-111 |

### Positive controls

All defects change production behavior and compile; none deletes a test, changes
its fixture, increases a timeout or creates a build failure. Each method has a
valid baseline arm and all other prerequisites satisfied. For predicates, replace
only the named refusing subpredicate with `false` (or the named accepting predicate
with `true`); for actions, make exactly the action change stated below. Keep later
guards active; first-inspection assertions deliberately detect their masking.
A field still rejected independently by another check needs a lower-level behavioral
assertion of the intended guard, not a mutation of the fixture.

Every row uses the exact filter `/*/*/<Class>/<Method>` printed below, with one
executed result. No class-wide PC cycles. Per PC: baseline green, compiling defect,
exact method red at the named assertion, restore exact source, rebuild, same method
green. Store per-PC source SHA, defect diff, failed assertion, counts and restoration
outside the SourceLanding tree. A build/fixture error, zero tests, equivalent mutant
or failure at another assertion is invalid evidence. Windows-only W controls run
on Windows; all other controls run on Linux unless their native defect explicitly
requires Windows. Ordinary duplicate OS executions do not multiply PC identities.

| PC | Compiling defect (break only its G) | Exact method-scoped filter | Required red assertion |
|---|---|---|---|
| PC-1 | G-1: accept a non-Done generation. | `/*/*/CardDoneWorktreeCleanupTests/C1017_OnlyDoneAuthorizes` | `admittedIntents.ShouldBe(0)` |
| PC-2 | G-2: ignore the DoneRevisionId equality. | `/*/*/CardDoneWorktreeCleanupTests/C1017_GenerationMustMatch` | `oldGenerationIntents.ShouldBe(0)` |
| PC-3 | G-3: omit obligation insertion from the Done transaction. | `/*/*/CardDoneWorktreeCleanupTests/C1017_DoneSaveIsAtomic` | `committedObligations.ShouldHaveSingleItem()` |
| PC-4 | G-4: remove only the generation unique key; barrier both no-row lookups before concurrent inserts. | `/*/*/CardDoneWorktreeCleanupTests/C1017_DuplicateDoneIsIdempotent` | `generationRows.Count.ShouldBe(1)` |
| PC-5 | G-5: return early when header previously completed. | `/*/*/CardDoneWorktreeCleanupTests/C1017_DiscoverPostDoneAttempts` | `targetAttempts.ShouldBe(new[] { 1, 2 })` |
| PC-6 | G-6: include a title-only matching task in the candidate query. | `/*/*/CardDoneWorktreeCleanupTests/C1017_ExactCardBinding` | `targets.ShouldNotContain(foreignTaskId)` |
| PC-7 | G-7: accept Working task status. | `/*/*/CardDoneWorktreeCleanupTests/C1017_TerminalTaskRequired` | `reason.ShouldBe("terminal_required")` |
| PC-8 | G-8: accept multiple exact owners. | `/*/*/CardDoneWorktreeCleanupTests/C1017_UniqueOrdinaryOwner` | `ambiguousIntents.ShouldBe(0)` |
| PC-9 | G-9: treat a borrowed source as an owned Worktree. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SharedAndBorrowedExcluded` | `borrowedBytes.ShouldBe(originalBytes)` |
| PC-10 | G-10: remove SourceLandingOperationId exclusion. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SourceLandingExcluded` | `verificationTreeExists.ShouldBeTrue()` |
| PC-11 | G-11: classify a reusable slot as ordinary. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SlotAndCanonicalExcluded` | `slotSentinel.ShouldBe(originalBytes)` |
| PC-12 | G-12: skip transactional re-read of the card generation. | `/*/*/CardDoneWorktreeCleanupTests/C1017_ReopenBeforeIntentRevokes` | `destructiveCalls.ShouldBe(0)` |
| PC-13 | G-13: release admitted retirement fence during reopen. | `/*/*/CardDoneWorktreeCleanupTests/C1017_IntentBeforeReopenKeepsFence` | `oldPathLaunches.ShouldBe(0)` |
| PC-14 | G-14: ignore active consumer lookup. | `/*/*/CardDoneWorktreeCleanupTests/C1017_ActiveConsumersHold` | `admittedIntents.ShouldBe(0)` |
| PC-15 | G-15: allow outstanding recovery debt into settled removal. | `/*/*/CardDoneWorktreeCleanupTests/C1017_RecoveryDebtHolds` | `settledRemovalCalls.ShouldBe(0)` |
| PC-16 | G-16: omit attempt comparison. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SnapshotAttemptIsFresh` | `staleIntentCount.ShouldBe(0)` |
| PC-17 | G-17: omit report-digest comparison. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SnapshotReportIsFresh` | `staleIntentCount.ShouldBe(0)` |
| PC-18 | G-18: retain generation transaction across the awaited I/O. | `/*/*/CardDoneWorktreeCleanupTests/C1017_NoDbLockAcrossIo` | `secondWriterCommittedBeforeIoRelease.ShouldBeTrue()` |
| PC-19 | G-19: allow evidence disposal for ordinary Publication. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_EvidenceNeedsDoneDisposition` | `beforeDoneEvidenceBytes.ShouldBe(originalBytes)` |
| PC-20 | G-20: accept disposal flag without its intent row. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_DisposalFlagIsNotAuthority` | `directoryExists.ShouldBeTrue()` |
| PC-21 | G-21: bypass leases.Owns only. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_LeaseMustBeGenuine` | `removalCalls.ShouldBe(0)` |
| PC-22 | G-22: ignore source HEAD equality. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_SourceShaMustMatch` | `directoryExists.ShouldBeTrue()` |
| PC-23 | G-23: omit receipt RepositoryPath comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RepositoryMustMatch` | `foreignRepositoryBytes.ShouldBe(originalBytes)` |
| PC-24 | G-24: omit receipt WorktreePath comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_PathMustMatch` | `otherTreeExists.ShouldBeTrue()` |
| PC-25 | G-25: omit SourceFullRef comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_BranchMustMatch` | `otherBranchStillExists.ShouldBeTrue()` |
| PC-26 | G-26: omit CommonDirectory comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_CommonDirectoryMustMatch` | `foreignCommonBytes.ShouldBe(originalBytes)` |
| PC-27 | G-27: omit GitDirectory comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_AdminDirectoryMustMatch` | `foreignAdminExists.ShouldBeTrue()` |
| PC-28 | G-28: ignore tracked-dirty result at first inspection. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_DirtyTrackedIsHeld` | `fullInspections.ShouldBe(1)` |
| PC-29 | G-29: ignore untracked-source result at first inspection. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_UntrackedSourceIsHeld` | `fullInspections.ShouldBe(1)` |
| PC-30 | G-30: treat contains-source false as true. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_UnpushedCommitIsHeld` | `treeExists.ShouldBeTrue()` |
| PC-31 | G-31: accept task-branch preservation as published-target containment. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_TaskBranchOnlyIsHeld` | `reason.ShouldBe("unlanded_work")` |
| PC-32 | G-32: use supplied landed SHA without bound publication mapping. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RebaseMappingMustBeBound` | `foreignMappingTreeExists.ShouldBeTrue()` |
| PC-33 | G-33: reuse first remote observation at final directory boundary. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RemoteProofMustBeFresh` | `treeExists.ShouldBeTrue()` |
| PC-34 | G-34: clear Protected before the first check. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_ProtectedIgnoredIsHeld` | `fullInspections.ShouldBe(1)` |
| PC-35 | G-35: reuse first inspection as final snapshot. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_FinalContentIsRechecked` | `lateBytes.ShouldBe(originalBytes)` |
| PC-36 | G-36: skip second.Evidence.SequenceEqual check. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_EvidenceChurnIsHeld` | `reason.ShouldBe("ignored_content_changed")` |
| PC-37 | G-37: follow directory links when recursively enumerating removal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_LinksNeverFollowed` | `outsideSentinel.ShouldBe(originalBytes)` |
| PC-38 | G-38: skip managed-root confinement refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RootConfinementIsFresh` | `outsideTreeExists.ShouldBeTrue()` |
| PC-39 | G-39: skip nested_registration refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_NestedRegistrationIsHeld` | `nestedTreeExists.ShouldBeTrue()` |
| PC-40 | G-40: skip unregistered_directory refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RegistrationMustExist` | `treeExists.ShouldBeTrue()` |
| PC-41 | G-41: skip registration_locked refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_LockedRegistrationIsHeld` | `treeExists.ShouldBeTrue()` |
| PC-42 | G-42: skip registration_prunable refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_PrunableRegistrationIsHeld` | `treeExists.ShouldBeTrue()` |
| PC-43 | G-43: accept report inside a deletion root. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_ReportMustBeExternal` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-44 | G-44: skip canonical report digest comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_ReportDigestMustMatch` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-45 | G-45: treat inline Result alone as preserving distinct task-report bytes. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_TaskReportDetailMustSurvive` | `detailBytesAfterAttempt.ShouldBe(originalDetailBytes)` |
| PC-46 | G-46: skip deliverable deletion-root test. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_DeliverablesMustBeExternal` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-47 | G-47: accept an unresolved deliverable pointer. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_DeliverablesMustExist` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-48 | G-48: omit last fresh authority read. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_FinalAuthorityIsFresh` | `removalCalls.ShouldBe(0)` |
| PC-49 | G-49: omit expected old SHA argument on update-ref -d. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_BranchCasPreservesConcurrentRef` | `observedRef.ShouldBe(concurrentSha)` |
| PC-50 | G-50: send the local ref deletion to origin as well. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_OriginRefsSurvive` | `originRefsAfter.ShouldBe(originRefsBefore)` |
| PC-51 | G-51: replace ordinary ref deletion with an extra reset --hard of the source before inspection. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_NoImplicitResetOrForce` | `sourceMutationCommands.ShouldBeEmpty()` |
| PC-52 | G-52: route new operation to legacy RemoveAsync. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_GuardedOperationIsDistinct` | `legacyRemovalCalls.ShouldBe(0)` |
| PC-53 | G-53: ignore TaskId equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerTaskIdentity` | `treeExists.ShouldBeTrue()` |
| PC-54 | G-54: ignore task-attempt equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerAttemptIdentity` | `treeExists.ShouldBeTrue()` |
| PC-55 | G-55: ignore StoreId equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerStoreIdentity` | `treeExists.ShouldBeTrue()` |
| PC-56 | G-56: ignore repository binding equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerRepositoryIdentity` | `foreignBytes.ShouldBe(originalBytes)` |
| PC-57 | G-57: ignore exact recorded path equality for valid different task path. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPathIdentity` | `otherTreeExists.ShouldBeTrue()` |
| PC-58 | G-58: ignore symbolic branch equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerBranchIdentity` | `otherBranchStillExists.ShouldBeTrue()` |
| PC-59 | G-59: ignore expected HEAD equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerHeadIdentity` | `preservationProofChecks.ShouldBe(0)` |
| PC-60 | G-60: accept missing preservation proof. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `treeExists.ShouldBeTrue()` |
| PC-61 | G-61: accept nonempty source status. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDirtySource` | `finalInspectionCalls.ShouldBe(0)` |
| PC-62 | G-62: blank protected classification. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `protectedBytes.ShouldBe(originalBytes)` |
| PC-63 | G-63: follow the substituted directory link. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerNoFollow` | `outsideSentinel.ShouldBe(originalBytes)` |
| PC-64 | G-64: consider terminal session sufficient despite a live child. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerProcessOwnerHolds` | `treeExists.ShouldBeTrue()` |
| PC-65 | G-65: skip cleanup fence at StartAsync. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerStartFence` | `startsAtRetiringPath.ShouldBe(0)` |
| PC-66 | G-66: skip restored cleanup fence during re-adoption. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerReadoptionFence` | `adoptionsAtRetiringPath.ShouldBe(0)` |
| PC-67 | G-67: move durable intent write after deletion. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIntentBeforeIo` | `intentVisibleAtFirstMutation.ShouldBeTrue()` |
| PC-68 | G-68: return saved success for changed request digest. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDigestReplay` | `changedRequestAccepted.ShouldBeFalse()` |
| PC-69 | G-69: skip last source/use validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateTreeBytes.ShouldBe(originalBytes)` |
| PC-70 | G-70: allow Force to skip guarded validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerNoForceBypass` | `dirtyBytes.ShouldBe(originalBytes)` |
| PC-71 | G-71: filter out targets whose local component is Complete. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorCompletesIndependently` | `mirrorDirectoryGone.ShouldBeTrue()` |
| PC-72 | G-72: require mirror online before local execution. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_LocalProgressSurvivesOffline` | `localDirectoryGone.ShouldBeTrue()` |
| PC-73 | G-73: require mirror HEAD equal to pushed P even with exact H containment proof. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorBehindPushedTip` | `mirrorDirectoryGone.ShouldBeTrue()` |
| PC-74 | G-74: ignore receipt operation ID. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchOperation` | `mirrorComplete.ShouldBeFalse()` |
| PC-75 | G-75: omit endpoint-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchEndpoint` | `mirrorComplete.ShouldBeFalse()` |
| PC-76 | G-76: omit store-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchStore` | `mirrorComplete.ShouldBeFalse()` |
| PC-77 | G-77: omit task-attempt comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchTaskAttempt` | `mirrorComplete.ShouldBeFalse()` |
| PC-78 | G-78: mark Complete on successful RPC return. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_AckIsNotRemoval` | `mirrorComplete.ShouldBeFalse()` |
| PC-79 | G-79: retry unsupported guarded command using legacy WorkspaceRemove. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_UnsupportedNeverFallsBack` | `legacyRemovalCalls.ShouldBe(0)` |
| PC-80 | G-80: reuse local generation admission for remote issuance. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReopenBetweenEndpoints` | `remoteCommandsIssued.ShouldBe(0)` |
| PC-81 | G-81: overwrite mirror list with last scalar binding. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MultipleMirrorsRemainDistinct` | `persistedEndpointCount.ShouldBe(2)` |
| PC-82 | G-82: allocate new operation ID on retry. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_LostAckReusesOperation` | `distinctRemovalOperationIds.ShouldBe(1)` |
| PC-83 | G-83: omit missing-obligation recovery query. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_LostWakeupRecovers` | `directoryGone.ShouldBeTrue()` |
| PC-84 | G-84: clear spent command slot on restart. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_IntentRecoveryDoesNotResetBudget` | `distinctDestructiveCommandIds.ShouldBe(1)` |
| PC-85 | G-85: discard CardDone disposition while reconstructing set-aside request. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideRecoveryPreservesAuthority` | `ownedSetAsideGone.ShouldBeTrue()` |
| PC-86 | G-86: accept mismatching set-aside receipt. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideIdentityMustMatch` | `foreignAsideBytes.ShouldBe(originalBytes)` |
| PC-87 | G-87: delete occupied original path during recovery. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ReplacementPathIsUntouched` | `replacementBytes.ShouldBe(originalBytes)` |
| PC-88 | G-88: equate missing path with Complete without receipt. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_AbsenceNeedsReceipt` | `endpointComplete.ShouldBeFalse()` |
| PC-89 | G-89: compute Complete from DirectoryGone alone. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ComponentFactsAreIndependent` | `endpointComplete.ShouldBeFalse()` |
| PC-90 | G-90: send cleanup-only request down ordinary publication path. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_PublicationIsImmutable` | `publicationCommandsAfterCleanup.ShouldBe(0)` |
| PC-91 | G-91: ignore Execute option. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ExecuteFalseCannotDelete` | `destructiveCalls.ShouldBe(0)` |
| PC-92 | G-92: treat all historical Done generations as new. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_HistoricalCutoffCannotDelete` | `historicalTreeExists.ShouldBeTrue()` |
| PC-93 | G-93: remove settling-floor comparison. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SettlingFloorCannotDelete` | `belowFloorTreeExists.ShouldBeTrue()` |
| PC-94 | G-94: give each lane its own 25-action allowance. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ActionBudgetIsShared` | `acceptedActions.ShouldBe(25)` |
| PC-95 | G-95: discard persisted NextAttemptAt after restart. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_CooldownSurvivesRestart` | `beforeDueCommands.ShouldBe(0)` |
| PC-96 | G-96: requeue permanent refusal merely on elapsed time. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_PermanentRefusalAwaitsEvidence` | `repeatCommands.ShouldBe(0)` |
| PC-97 | G-97: emit invalidation before saving endpoint result. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_OperatorOutcomeIsDurable` | `committedOutcomeAtEvent.ShouldBeTrue()` |
| PC-98 | G-98: omit intent-to-run/event link. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_EveryIntentIsReportable` | `reportedIntentIds.ShouldContain(intentId)` |
| PC-99 | G-99: include file contents in refusal detail. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_NoOpaqueBytesInReport` | `operatorOutput.ShouldNotContain("SECRET_SENTINEL")` |
| PC-100 | G-100: publish an inside-tree report pointer as durable. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_ReportReadbackSurvivesDeletion` | `retrievedReportBytes.ShouldBe(originalReportBytes)` |
| PC-101 | G-101: ignore a positively live lingering PtyHost because session is terminal. | `/*/*/CompletedCardWorktreeRemovalWindowsTests/C1017_LingeringPtyHostIsReported` | `treeExists.ShouldBeTrue()` |
| PC-102 | G-102: invoke runner stop on that refusal. | `/*/*/CompletedCardWorktreeRemovalWindowsTests/C1017_CleanupNeverKillsHolder` | `holderAliveAfterCleanup.ShouldBeTrue()` |
| PC-103 | G-103: mark endpoint Complete when original is absent but aside remains. | `/*/*/CompletedCardWorktreeRemovalWindowsTests/C1017_LockedSetAsideRetries` | `completeWhileAsideExists.ShouldBeFalse()` |
| PC-104 | G-104: enumerate junction target recursively during removal. | `/*/*/CompletedCardWorktreeRemovalWindowsTests/C1017_JunctionOutsideBytesSurvive` | `outsideSentinel.ShouldBe(originalBytes)` |
| PC-105 | G-105: accept missing report without disposition. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_MissingReportNeedsDisposition` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-106 | G-106: omit first reparse-point refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_FirstIgnoredLinkIsHeld` | `fullInspections.ShouldBe(1)` |
| PC-107 | G-107: omit final reparse-point refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_FinalIgnoredLinkIsHeld` | `reason.ShouldBe("ignored_reparse_point")` |
| PC-108 | G-108: accept verification creation as ordinary. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerSourceLandingExcluded` | `verificationTreeExists.ShouldBeTrue()` |
| PC-109 | G-109: treat unknown ownership as no owner. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_UnknownProcessOwnershipHolds` | `treeExists.ShouldBeTrue()` |
| PC-110 | G-110: clear durable pending state when enqueue throws. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_EnqueueFailureRemainsRecoverable` | `directoryGoneAfterRecovery.ShouldBeTrue()` |
| PC-111 | G-111: discard pending endpoint before saving component receipt. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ResultSaveFailureRetainsDebt` | `recoveredEndpointComplete.ShouldBeTrue()` |

### Out of scope

- SourceLanding snapshot cleanup, external verification evidence/custody stores,
  canonical report stores, origin-branch deletion, reusable slot reset and
  unrelated out-of-tree temp cleanup. Fixtures prove these exclusions, then leave
  their dedicated owners responsible.
- Provider qualification, model/version floors, paid providers, routing-policy
  changes, live fleet deletion and activation settings. No test can authorize
  cleanup of the real incident directory. Deployment/canary acceptance remains
  the activation sequence above.
- No new session notification, Telegram or completion-note path. Existing manual
  landing notification delivery is unchanged; scheduled cleanup explicitly has
  no caller obligation.
- Full unrelated assembly suites, E2E browser, FakeClaude and
  `Antiphon.Agents.Pty.Tests` are outside this closed scope. New native evidence
  uses the supported backend. The existing Windows-only junction vector is moved
  with its whole settled-removal class to CP-10; no Linux skip is accepted.
- Arbitrary Cartesian products are not required: each independent bad field has
  an otherwise eligible fixture and independent PC. Explicit interactions are
  Done/archive/reopen generations, active/terminal/live-child, before/after each
  commit, first/final content reads, local/mirror outcome pairs, original/rebased/
  behind/unpublished SHA relations, protected/evidence/build content, and
  original/set-aside/replacement roots. These cover the boundaries where one
  invariant can mask another.

#### Frozen execution rosters and platform lanes

Aliases: D/L/U/M/K/A/W expand to the full classes in the PC table. All new classes
use their matching proposed files in S1-S5; U belongs to
`Antiphon.SessionRunner.Tests`, D/M/K/A to `Antiphon.Tests.Application`, and L/W
to `Antiphon.Tests.Infrastructure`. The 111 distinct methods in the PC table
are the complete guard roster: D=18, L=36, U=21, M=12, K=16, A=4, W=4.
A also has the seven delivery methods specified above, giving **118 new methods**
and **118 new single-result tests**. L/M/U repeat on Windows as specified;
the repeated lane results are deliberate native compatibility qualification.

| CP | Required expanded roster |
|---|---|
| CP-1 | D 18 + CardTaskSettlementTests 8 + ClosedCardSweepTests 2 = 28 |
| CP-2 | L 36 + WorktreeRemovalAuthorityTests 9 + AgentReportStoreTests 28 = 73 |
| CP-3 | U 21 = 21 |
| CP-4 | RunnerWorkspaceServiceTests' 25 Mirror/Publish/Remove methods + PhoneHomeCommandDispatcherTests' three Workspace methods = 28 |
| CP-5 | M 12 + RemoteWorktreeMirrorTests 8 = 20 |
| CP-6 | K 16 + WorktreeResidueRecoveryTests' IntentCommittedBeforeGit, ComponentsAreIndependent and OutageKeepsFence = 19 |
| CP-7 | A 11 + WorktreeResidueEndpointTests 6 + WorktreeResidueScriptTests 1 + WorktreeResidueRegistrationTests 3 = 21 |
| CP-8 | TaskWorktreeRetirementTests 38 + WorkspaceReservationLivenessTests 20 = 58 |
| CP-9 | WorktreeRetirementRaceTests' OneRetirementClaim 1 + CreateReservesWorkspace 3; seven selected WorktreeLandingCleanupRetryTests 10 = 14 |
| CP-10 | W 4 + SettledWorktreeRemovalTests 78 = 82 |
| CP-11 | U 21 = 21 |
| CP-12 | L 36 + M 12 = 48 |

Existing abbreviated method names in this roster carry the actual `C459_` prefix.
The seven landing-retry methods are `C459_AdmissionPinsPublication` (4 arguments:
missing/inactive/unconfirmed/pending), `C459_ScheduledHasNoCallerObligation`,
`C459_PublicationIsNotRepeated`, `C459_ExecutionPinsPublicationBeforeResolver`,
`C459_ProtocolPinsPublication`, `C459_ConfirmedCleanupRetryCompletes` and
`C459_LostWakeupRecoversSameRequest` (one each).

Expanded existing class counts are frozen at the inspected source:
CardTaskSettlement 8×1; ClosedCardSweep 2×1; RemovalAuthority 1+3+5=9;
AgentReportStore 1+11+5+2+1+1+7=28; RemoteWorktreeMirror 8×1;
ResidueEndpoint 6×1; ResidueScript 1; ResidueRegistration 3×1;
ReservationLiveness 1+4+3+1+4+2+3+2=20;
TaskRetirement 3+1+4+1+3+6+3+5+3+1+1+1+1+2+1+1+1=38;
SettledRemoval 1+1+8+5+1+2+2+1+7+2+3+2+4+4+1+6+4+3+3+6+3+1+1+1+1+5=78.
Loops and assertions do not raise Min. Read-only source roster extraction on
2026-10-04 verified these arithmetic totals. Method manifests must be reconciled
against the same committed source if an overlapping land adds selected tests.

CP-1 through CP-9 run on Linux server2-class task worktrees, with portable legacy
`card-task-*` fixtures and real runner `task-*` fixtures. Resolve current catalogue/
defaults at dispatch; do not hard-pin a host name. CP-10 through CP-12 require
`-Platform Windows` on a separate checkout at the same implementation SHA and
cover desktop mirrors, native locking and legacy worktrees. U's new fixture must
support both path shapes without Linux-only hardcoding. Native rows **fail**
when native prerequisites are absent; no Skip/early-return success. No current
production directories are part of these fixtures.

### Checkpoints

This is the sole importer-visible closed list. Each row has one fresh isolated
build and one exact filter. Select **explicit --rows**, since `--after S1` also
selects rows marked `all` in the inspected AfterSelector. S1=CP-1; S2=CP-2;
S3=CP-3,CP-4,CP-5; S4=CP-6,CP-7; final Linux=CP-8,CP-9;
final Windows=CP-10,CP-11,CP-12. Each selection follows a committed/pushed slice,
uses its exact SHA, and is awaited until exit is not 75. Every row expects its
entire roster, with **0 failed and 0 skipped**. Require both Min and exact names:
the importer derives class tokens from filters and does not execute the prose
Expect cell as an exact roster comparison.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1017-s1/` | linux-lifecycle | `/*/*/(CardDoneWorktreeCleanupTests*)\|(CardTaskSettlementTests*)\|(ClosedCardSweepTests*)/*` | V-1, V-4, R-1 | all 28 listed; 0 failed/skipped | 28 | 7 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1017-s2/` | linux-preservation | `/*/*/(CompletedCardWorktreeRemovalTests*)\|(WorktreeRemovalAuthorityTests*)\|(AgentReportStoreTests*)/*` | V-2, V-3, R-2, R-3 | all 73 listed; 0 failed/skipped | 73 | 9 | true |
| CP-3 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1017-runner/` | linux-guarded-runner | `/*/*/CompletedCardWorkspaceRemovalTests/*` | V-4, V-5, R-3 | all 21 listed; 0 failed/skipped | 21 | 8 | true |
| CP-4 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1017-legacy/` | linux-runner-legacy | `/*/*/(RunnerWorkspaceServiceTests*)\|(PhoneHomeCommandDispatcherTests*)/(Mirror_*)\|(Publish_*)\|(Remove_*)\|(Workspace_*)` | V-5, R-3 | all 28 listed; 0 failed/skipped | 28 | 6 | true |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1017-mirror/` | linux-mirror | `/*/*/(CardWorktreeMirrorCleanupTests*)\|(RemoteWorktreeMirrorTests*)/*` | V-5, V-6, R-3 | all 20 listed; 0 failed/skipped | 20 | 7 | true |
| CP-6 | S4 | `tests/Antiphon.Tests -> bin-c1017-recovery/` | linux-recovery | `/*/*/(CardWorktreeCleanupRecoveryTests*)\|(WorktreeResidueRecoveryTests*)/(C1017_*)\|(C459_IntentCommittedBeforeGit)\|(C459_ComponentsAreIndependent)\|(C459_OutageKeepsFence)` | V-1, V-6, V-7, V-8, R-2 | all 19 listed; 0 failed/skipped | 19 | 10 | true |
| CP-7 | S4 | `tests/Antiphon.Tests -> bin-c1017-acceptance/` | linux-delivery-reporting | `/*/*/(CardWorktreeCleanupAcceptanceTests*)\|(WorktreeResidueEndpointTests*)\|(WorktreeResidueScriptTests*)\|(WorktreeResidueRegistrationTests*)/*` | V-1, V-2, V-5, V-6, V-7, V-8, R-3 | all 21 listed; 0 failed/skipped | 21 | 12 | true |
| CP-8 | all | `tests/Antiphon.Tests -> bin-c1017-manual/` | linux-manual-reservations | `/*/*/(TaskWorktreeRetirementTests*)\|(WorkspaceReservationLivenessTests*)/*` | V-4, R-2, R-3 | all 58 listed; 0 failed/skipped | 58 | 7 | true |
| CP-9 | all | `tests/Antiphon.Tests -> bin-c1017-queues/` | linux-existing-queues | `/*/*/(WorktreeRetirementRaceTests*)\|(WorktreeLandingCleanupRetryTests*)/(C459_OneRetirementClaim*)\|(C459_CreateReservesWorkspace*)\|(C459_AdmissionPinsPublication*)\|(C459_ScheduledHasNoCallerObligation*)\|(C459_PublicationIsNotRepeated*)\|(C459_ExecutionPinsPublicationBeforeResolver*)\|(C459_ProtocolPinsPublication*)\|(C459_ConfirmedCleanupRetryCompletes*)\|(C459_LostWakeupRecoversSameRequest*)` | V-4, V-7, R-2, R-3 | all 14 listed; 0 failed/skipped | 14 | 7 | true |
| CP-10 | all | `tests/Antiphon.Tests -> bin-c1017-win-native/` | windows-native-local | `/*/*/(CompletedCardWorktreeRemovalWindowsTests*)\|(SettledWorktreeRemovalTests*)/*` | V-3, V-4, V-7, V-9, R-2 | Windows; all 82 listed; 0 failed/skipped | 82 | 12 | true |
| CP-11 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1017-win-runner/` | windows-guarded-runner | `/*/*/CompletedCardWorkspaceRemovalTests/*` | V-4, V-5, V-9, R-3 | Windows; all 21 listed; 0 failed/skipped | 21 | 8 | true |
| CP-12 | all | `tests/Antiphon.Tests -> bin-c1017-win-mirror/` | windows-desktop-mirror | `/*/*/(CompletedCardWorktreeRemovalTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | V-2, V-3, V-5, V-6, V-9 | Windows; all 48 listed; 0 failed/skipped | 48 | 10 | true |

### Cost

All times are **estimated**. No runtime measurements were taken in TestDesign.
Ordinary Code V/R floor = 7+9+8+6+7+10+12+7+7+12+8+10 =
**103 minutes**, for the exact CP filters above, **433 executed results**.
That includes 12 isolated builds budgeted at 3 minutes each (36) and 67 minutes
of V/R. Setup/import allowance outside rows = **3 minutes**; ordinary total =
**106 minutes**, excluding authoring and slot wait. Review uses this same scope
if fresh ordinary reruns are commissioned, adding 103 rather than hiding it in Code.

Mutation floor: **107 portable PCs × 7 minutes = 749**, plus **4 Windows-native
PCs × 9 minutes = 36**. Each portable PC's exact-method filter in the PC table
budgets baseline-green execution 0.5 + defect/check 0.25 + red rebuild/run 3 +
restore/check 0.25 + restored rebuild/green 3 = 7 minutes; native red/green each
take 4, making 9. Shared initial baseline builds for Linux server tests, Linux
runner tests and Windows server tests add **9 minutes**; Mutation setup/discovery/
restoration-record allowance adds **3 minutes**. Thus Mutation floor =
749+36+9+3 = **797 minutes**. Combined setup/build + ordinary V/R +
every PC baseline/red/restore/green = **903 minutes**, estimated. This is a
substantial destructive-safety battery, not a claim that Code itself takes 903 minutes.

Savings: the 26 selected class/lane occurrences grouped into 12 ordinary builds
save **14 × 3 = 42 minutes** against a per-class/lane build plan. PC batching savings
are **zero**: shared safety owners and exact independent restoration are more
valuable than an unsupported batching claim. Report actual slot wait, failures,
base verification and authorized reruns separately; never silently widen scope.

#### Collision, activation and handoff audit

Live reads on 2026-10-04 succeeded after initial transient HTTP 502s. CARD-1017
is InProgress, card `9cb213bf-3268-4696-a7c8-d8ad41160bf1`, board
`8988ca03-7414-47ad-b0b6-51556c701703`; its full operator decision and constraints
match this freeze. Pipeline read at about 00:35 UTC showed CARD-0959 Code
`bd02f8d9` in flight, CARD-1011 Code `d422c5a9` blocked and Review `d535eeec`
in flight, CARD-1013 Code `2c35a27d` blocked. Card reads also confirmed CARD-1012
InProgress, CARD-1018 Review and CARD-1020 Review. A missing task in the active
pipeline is not a terminal/publication receipt.

Read-only branch-footprint comparison found: 0959 touches runner Contracts,
dispatcher, runtime, adapter, Program and PhoneHomeRunnerClient (S3 collision);
1011 touches orchestration/provider docs and test helpers (S5 doc collision);
1013 touches checkpoint tooling/tests/testing owner docs (only S5 owner-doc
coordination here); 1012's `81922318` branch changes its checkpoint plan and
verification docs only. 1018 owns `ProviderSignInRequiredException` wording;
this plan does not touch it. A structured task-detail read confirmed its operation
`348e9161-119e-434b-8b04-ed7e729636ca` has publication Landed and cleanup Complete,
verified/remote SHA `7b8e687a73c17167a49b6ce0a3ace1d1ab1f796a`.
A board-scoped task read at about 00:49 UTC confirmed 1012 Code `81922318`
Succeeded, 1011 Review `d535eeec` Succeeded (00:43:57 UTC), and 1020 TestDesign
`b01f2e7b` Succeeded. These update the earlier observations, not file ownership.
1020 freeze
`b01f2e7b` at `d4203e10922bbeedc0f536ba14589479ab9c99c1` reserves test-owned
PtyHost teardown and explicitly preserves production detach/restart. No file
outside this plan was edited. Recheck effective occupancy and exact task status
at dispatch, serialize S3/S5 overlaps and the Windows lane, and do not change an
active branch's history.

**Server restart required for implementation activation: yes.** Cleanup runtime,
DI and EF schema are server changes. Deploy supporting runner operation/journal
first, then migrate/restart the canonical server and verify loaded SHA. This
docs-only freeze requires no restart. Preserve Execute=false and historical
cutoff/preview controls; operator-visible canary precedes widening historical
cleanup. CARD-1020 fixture cleanup is not permission for 1017 to kill a holder.

Handoff audit: bodies read; **guards=111, mapped=111, missing=0,
duplicate PC maps=0**. All PCs have a production owner, compiling behavioral
defect, exact independent method and decisive red assertion; their required
missing setup is specified above, with no unresolved production seam or human
product choice. Executable PC specifications are frozen; built-test execution
evidence is still owed by Code/Mutation at their respective stages.

#### Read-only manifest import evidence

The actual compiled importer was run, with no build or test driver:

```text
dotnet /work/worktrees/task-7f1aaff2/tools/Antiphon.Checkpoints/bin-c1005-final/Antiphon.Checkpoints.dll import --plan /work/worktrees/task-044f2398/docs/superpowers/plans/2026-10-03-card-1017-completed-card-worktree-cleanup-plan.md --out /tmp/c1017-import.yml
imported 12 rows -> /tmp/c1017-import.yml
```

Invoking that assembly's real `PlanTableImporter.ImportFile` and
`ManifestValidator.Validate` with both OS arguments returned:

```text
isWindows=False rows=12 builds=12 Min=433 minutes=103 warnings=0
isWindows=True rows=12 builds=12 Min=433 minutes=103 warnings=0
```

Importer source blob matched this checkout: `c49927bcd561a9f34236344cd0da57a600dbd35a`;
validator source blob also matched: `d8da8c209360d65d14907614689691aafbaa2b10`.
Used assembly SHA256:
`4cfd026252c6b3aee00b89417c278b1f9f13c356a11958d95afe84690c42cf93`.
This validates manifest parsing/arithmetic, not test discovery or compiled test
availability. The generated YAML remains external scratch, uncommitted.

#### Existing method census (frozen source roster)

Each suffix is its argument-expanded TUnit execution count. This is source
inspection, not a claimed run. Full-class CP selections execute every listed method.

```text
CardTaskSettlementTests methods=8 executions=8
closing_a_card_cancels_its_blocked_task_with_the_close_reason=1, closing_a_card_cancels_queued_and_unstarted_dispatched_tasks=1, closing_a_card_leaves_started_tasks_running_with_one_warning_event=1, closing_a_card_touches_no_other_cards_tasks_and_no_settled_task=1, archiving_a_card_cancels_its_queued_task_with_the_archive_reason=1, a_move_to_review_settles_nothing_and_a_reopen_resurrects_nothing=1, a_task_settled_by_a_concurrent_writer_is_skipped_and_the_move_still_succeeds=1, a_tracker_stale_close_cancels_the_cards_queued_task=1
ClosedCardSweepTests methods=2 executions=2
the_sweep_lists_open_tasks_on_closed_and_archived_cards_and_changes_nothing=1, apply_cancels_only_unstarted_rows_created_before_the_close=1
WorktreeRemovalAuthorityTests methods=3 executions=9
C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches=1, C688_PublicationCleanupDeletesBranchAtLocalSha=3, C448_V24_LegacyRemovalCannotEraseTaskContents=5
AgentReportStoreTests methods=7 executions=28
Exact_content_and_full_ids_own_distinct_files=1, Root_selection_requires_a_durable_owned_location=11, Ignore_and_tracking_are_checked_before_writing=5, Ineffective_ignore_attempts_do_not_duplicate_the_exclude_rule=2, Publication_exposes_only_a_complete_verified_report=1, Paths_over_the_persisted_limit_are_unavailable=1, Failures_and_cancellation_never_publish_a_bad_pointer=7
RemoteWorktreeMirrorTests methods=8 executions=8
Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity=1, Push_refuses_when_the_desktop_origin_cannot_be_read=1, Sync_fast_forwards_desktop_worktree=1, Non_fast_forward_sync_is_refused=1, Dirty_desktop_tree_is_never_fast_forwarded_over=1, Push_failure_reports_and_does_not_invent_a_sha=1, Push_reports_the_head_it_pushed=1, Mirror_name_is_the_dispatchers_own_short_form=1
WorktreeResidueEndpointTests methods=6 executions=6
C459_PreviewCannotExecute=1, C459_CallerScopeEnforced=1, C459_ReleaseRejectsStaleApprovalSnapshot=1, C459_RunResultSurvivesRestart=1, C459_RunReportOmitsOpaqueContent=1, C459_RunReportPageIsBounded=1
WorktreeResidueScriptTests methods=1 executions=1
C459_BatchRequiresFullIds=1
WorktreeResidueRegistrationTests methods=3 executions=3
C459_OneScheduler=1, C459_DisabledSchedulerStaysOff=1, C459_ScheduledWorkerExecutesBothLanes=1
WorkspaceReservationLivenessTests methods=8 executions=20
C664_FreshRowBlocksWhateverTheOwner=1, C664_LiveTaskOwnerBlocksAfterGrace=4, C664_TerminalTaskOwnerDoesNotBlockAfterGrace=3, C664_PendingLandKeepsTerminalTaskBlocking=1, C664_LiveSessionOwnerBlocksAfterGrace=4, C664_EndedSessionOwnerDoesNotBlockAfterGrace=2, C664_MissingOrUnattributedOwnerDoesNotBlockAfterGrace=3, C664_RetirementAndFenceRowsAlwaysBlock=2
TaskWorktreeRetirementTests methods=17 executions=38
C459_ReleasedTerminalTasksRetire=3, C459_TerminalRequired=1, C459_SettlingFloor=4, C459_ReleaseRequired=1, C459_ReleaseSnapshotIsExact=3, C459_HandoffDispositionRequired=6, C459_ArtifactsPreserved=3, C459_RevokeRespectsIntent=5, C459_UniqueFullOwner=3, C459_MutationIsExcluded=1, C459_SourceLandingIsExcluded=1, C459_RepairSourceIsExcluded=1, C459_TargetSelectionIsExplicit=1, C459_RecoveryDebtHolds=2, C459_ReleaseIdentityIsUnique=1, C459_LegacyRowsAreUnreleased=1, C459_OwnProgressKeepsRelease=1
SettledWorktreeRemovalTests methods=26 executions=78
C459_InterfaceDefaultCannotDeleteSettledTask=1, C459_PurposeCannotBorrowAuthority=1, C459_CoordinatesMatchReceipt=8, C459_ManagedRootRequired=5, C459_NestedRegistrationHolds=1, C459_RegistrationRequired=2, C459_LockedRegistrationHolds=2, C459_PrunableRegistrationHolds=1, C459_RemoteContainmentRequired=7, C459_DestinationIdentityRequired=2, C459_RemoteRefreshBeforeDirectory=3, C459_RemoteRefreshBeforeBranch=2, C459_GenuineLeaseRequired=4, C459_CommittedReceiptRequired=4, C459_FinalAuthorityRequired=1, C459_FirstContentInspectionRequired=6, C459_FinalContentInspectionRequired=4, C459_FirstIgnoredInspectionRequired=3, C459_FinalIgnoredInspectionRequired=3, C459_AbsenceNeedsOwnIntent=6, C459_RecreatedTreeHolds=3, C459_BranchPrecheckRequired=1, C459_BranchDeleteUsesCas=1, C459_NewCheckoutHoldsBranch=1, C459_RemovalNeverForces=1, C459_RetirementRefsCannotBorrowNamespaces=5
RunnerWorkspaceServiceTests methods=25 executions=25
Publish_pushes_only_own_fast_forward_branch=1, Publish_refuses_a_rebased_tip_without_pushing=1, Publish_equal_tip_is_not_pushed_and_reports_dirty_tree=1, Publish_refuses_foreign_path_and_branch_before_git=1, Publish_refuses_detached_head=1, Publish_refuses_an_active_sequencer_before_push=1, Publish_reports_rejected_non_fast_forward_without_exposing_origin=1, Publish_classifies_a_tip_behind_the_advertised_remote=1, Remove_refuses_committed_unpublished_tip_unless_forced=1, Remove_allows_a_published_tip=1, Remove_allows_a_tip_which_is_ancestor_of_published_sha=1, Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout=1, Mirror_refuses_a_repository_outside_the_allowed_clone_sources=1, Mirror_refuses_credentialed_request_without_echoing_credentials=1, Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository=1, Mirror_mismatch_does_not_echo_credentialed_origin=1, Mirror_refuses_a_secondary_repository_the_deploy_key_cannot_push_to=1, Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds=1, Mirror_creates_worktree_on_branch_at_sha=1, Mirror_refuses_sha_mismatch=1, Mirror_clones_repository_when_absent=1, Mirror_failure_is_admission_error_not_crash=1, Mirror_refuses_a_name_or_sha_it_did_not_shape=1, Remove_refuses_dirty_tree=1, Remove_refuses_a_path_outside_the_worktree_root=1
```
