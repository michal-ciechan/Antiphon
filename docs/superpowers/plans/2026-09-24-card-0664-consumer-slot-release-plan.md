# CARD-0664: release workspace-use consumer slots

Date: 2026-09-24. Plan task: a8685a72. Next: **Code**.
Test design is folded into this plan; the `### Checkpoints` table is the closed list.
Inspected base: 42eecdea (origin/master; carries the investigation commit 2fc8b3d2).
Planned on server2 (Linux, dotnet 10.0.401, nested Docker 27.5.1, pwsh, Testcontainers.PostgreSql 4.x).

## Outcome and boundaries

A workspace-use `Launch` reservation lives exactly as long as its owner. After this card:

- `TryClaimRetirementAsync` refuses a settled-task retirement only while a live owner holds
  the worktree: a task in Queued/Dispatched/Working/Blocked or with a pending land request, a
  session in Created/Starting/Running/Stopping, or a row younger than a short grace window.
  Orphaned rows (terminal or missing owner, past grace) stop blocking and are released on sight.
- Production code releases rows at task terminal settlement, task cancel, dispatch failure,
  session exit, session kill, session launch failure, land outcome, and immediately when an
  admitting operation throws after admission (land request, answer, refine, Herdr attach).
- Herdr pane adoption attributes its row to the adopted session id (A8).
- A reconcile pass releases orphaned rows at server startup (the one-time backfill for the
  desktop backlog) and at the start of every worktree-residue run (daily and on preview).
- Existing CARD-0459 fences stay in force: a claimed retirement still refuses create, requeue,
  answer, refine, dispatch, start, resume, interrupted attach, land, merge/commit children and
  Herdr attach; a committed `Launch` intent still blocks a claim while its launch is in flight.

Not in this card: the land-cleanup `ignored_content_preserved` refusal (gate 1 in the
investigation; its own card), `WorktreeResidue:Execute` activation, any schema migration, any
change to `GuardedWorktreeRemoval`, and the pre-existing READ COMMITTED window between a
concurrent `TryAdmitConsumerAsync` and `TryClaimRetirementAsync` (unchanged by this card).

Primary evidence: [the investigation](../../investigations/2026-09-24-card-0664-consumer-slot-release.md).

## Ground truth

| Card/investigation assumption | What the code establishes at 42eecdea | Design consequence |
|---|---|---|
| `RequireConsumerAsync` discards the reservation handle. | `WorkspaceUseAdmission.RequireConsumerAsync` returns `Task`; the `Accepted` snapshot from `TryAdmitConsumerAsync` is dropped (`WorkspaceUseAdmission.cs:16-21`). | Return `WorkspaceReservationSnapshot?` (null when admission is disabled); every caller may ignore it, the ones that catch-and-release use it. |
| `ReleaseConsumerAsync` is test-only. | Callers: `WorktreeRetirementRaceTests.cs:80,310` only. `Active=false` is written nowhere else in `server/` or `src/`. | Add owner-scoped releases (`ReleaseTaskConsumersAsync`, `ReleaseSessionConsumersAsync`) and a predicate-driven `ReleaseOrphanedConsumersAsync`; keep the id/generation primitive. |
| The claim ignores owner status. | `TryClaimRetirementAsync` loads `Where(r => r.Active)`, filters `Same` in memory, and refuses on any non-own overlap (`WorkspaceReservationJournal.cs:62-70`). `FindLiveConsumersAsync` returns the same rows after the claim (`WorkspaceUseAdmission.cs:31-37`). | One liveness predicate applied inside the claim transaction and in `FindLiveConsumersAsync`; both read owners through the journal's own scope. |
| Every terminal-to-live transition admits a fresh row. | `RequeueAsync` admits at `AgentTaskService.cs:2663` before setting Queued at `:2674`; `ResumeAsync` admits (`AgentSessionService.cs:1439` via `AdmitWorkspaceAsync`); Herdr re-adoption admits at `AgentControlService.cs:954` before `Status = Starting` at `:989`. | Owner status needs no row lock in the claim; the fresh row is within grace and blocks by itself (D-1). |
| A Launch row with no visible owner must still block briefly. | E2E `C459_LaunchReservationRecoveryRetainsOwnership` admits a Launch row for a **Succeeded** task (`LandDeliveryFixture.cs:130`) and asserts the claim is refused; dispatch admits at `AgentTaskDispatcher.cs:3964` before the session row exists at `:4219`. | Grace window is mandatory, not optional: default 15 minutes, `WorktreeResidue:LaunchGraceMinutes`, clamped to at least 1. |
| A land in flight is not visible from `AgentTask.Status`. | The land owner is Succeeded throughout (`AgentTaskLandService.cs:87`); pendency is `task.LandRequestedAt != null`, cleared only by `ClearPending` (`:245-247`, `:637-639`, `:867`). | Liveness treats `LandRequestedAt != null` as live; land outcome release hooks at the three `ClearPending` sites. |
| Normal task completion has one funnel. | `SettleAsync` sets `task.Status = status` (`AgentTaskReplyService.cs:639`) and, with `FailUnreportedTurnAsync` (`:1055`) and `HandleApiErrorTurnAsync` (`:1203`), persists through `PersistDeliverThenReleaseAsync` (`:1623`). Other terminal writers: `AgentTaskService.CancelAsync:2192`, `AgentTaskDispatcher.FailAsync:5617`, `ExpireClaimedOptionalWorkAsync:3884`, `CheckCompactionContinuationService:668`, `AgentTaskCheckService:568`, `RecoverFromBindRefusalAsync:949`, `ResolveConflictedParentAsync:1603`. | Hook the funnel plus Cancel and dispatcher Fail; the five exotic writers are left to the predicate and reconcile (D-2). |
| Normal session end has one funnel. | `AgentSessionRuntime.CloseSessionOnExitAsync` (`:177-318`, entered via `ObserveExitAsync:149`) writes Stopped/Failed; `KillOnAsync` (`AgentSessionService.cs:1320`) and the launch-failure catch (`:385`) write terminal status themselves. Reconciliation, stall detector, runner-slot audit, compaction stop and orchestrator also write terminal status. | Hook the runtime exit, kill and launch-failure paths; the rest are covered by the predicate and reconcile (D-2). |
| A8 has no owner. | `AttachHerdrAsync` admits with `TaskId = null, SessionId = null` at `:954-956`; `sessionId` is computed two lines later at `:958`. | Compute `sessionId` first and pass it; release on the attach-failure catch (`:1061-1069`). |
| The journal already reaches the sweep. | `WorktreeResidueSweepService` takes `IWorkspaceReservationJournal? reservations` (`:45`) and never uses it; `RunAsync` always calls `PersistRunAsync` when retirement is wired (`:63-66`), with `Execute` gating only actions. | Reconcile at the start of `PersistRunAsync`: daily and preview runs both reconcile even with `Execute=false`. |
| The desktop backlog needs a one-time backfill. | Startup runs `dbContext.Database.Migrate()` then `DatabaseSeeder.SeedAsync` in one scope (`Program.cs:820-825`). Hand-written migrations exist but are schema-only (`20260924120000_AddQueuedRemoteSpillBodies.cs`). | Run `ReleaseOrphanedConsumersAsync` in that startup scope after seeding, best-effort with a logged count; no data migration (D-8). |
| Tests need Postgres. | `RaceWorld` uses `TestDbFixture.CreateIsolatedSchemaAsync()` (Testcontainers per-test schema); `AgentAttachHerdrTests` uses the shared default store with tempRoot-scoped cleanup; both are `Category("Integration")`. Testcontainers works on server2's nested Docker unmodified (testing-and-build.md, Fast lane). | All new integration tests run on Linux here; only `WorkspaceReservationLivenessTests` is DB-free `Unit`. No ConPTY/Windows-only coverage is added. |
| The Herdr harness does not run admission. | `AgentAttachHerdrTests.BuildHarness` registers `AppDbContext`, `TimeProvider.System`, `AddLogging`, but neither `IWorkspaceReservationJournal` nor `WorkspaceUseAdmission`; `AgentControlService` treats admission as optional. | The A8 test registers both in that harness and extends `CleanupAsync` to delete reservation rows whose `CanonicalPath` starts with the temp root. |
| The fake adapter can end a session. | `FakeAgentProtocolAdapter.KillAsync` sets `Killed` and completes `Exited` when `KillResult` is true (`:223-232`); `AgentSessionService.KillAsync(sessionId, source, ct)` is public (`:1230`). | A5 red test kills through the production kill path in `RaceWorld`. |

## Decisions

- **D-1: Owner-liveness with grace, at both decision points.** A `Launch` row blocks a claim
  (and counts as a live consumer after the claim) when any of these hold: the row is younger than
  `LaunchGraceMinutes`; its `TaskId` names a task whose status is Queued, Dispatched, Working or
  Blocked, or whose `LandRequestedAt` is not null; its `SessionId` names a session whose status is
  Created, Starting, Running or Stopping. `Retirement` rows of another retirement and
  `HistoricalFence` rows block as today. Any other `Launch` row is orphaned: it does not block and
  the claim releases it (`Active=false`, `ReleasedAt=now`) in the same transaction. Rejected:
  explicit release only (leaves the existing backlog and every missed site blocking forever);
  owner status without grace (breaks the requeue, create and Herdr-adopt windows where the row
  commits before the status does, and the E2E lost-enqueue contract); `FOR UPDATE` on owner rows
  (not needed, see ground truth row 4); TTL-only expiry (would expire a live long session).
- **D-2: Explicit owner-end release through the main funnels only.** Task side:
  `PersistDeliverThenReleaseAsync` after its settlement save when `task.Status` is Succeeded,
  Failed or Canceled (never on Blocked); `AgentTaskService.CancelAsync`; `AgentTaskDispatcher.FailAsync`.
  Session side: `CloseSessionOnExitAsync` after its commit when the session is Stopped or Failed
  (regardless of `changed`); `KillOnAsync` after the terminal status write; the
  `LaunchInteractiveAsync` failure catch. The remaining terminal writers listed in ground truth
  rows 6 and 7 are not wired: the predicate makes their rows harmless after grace and the
  reconcile shrinks the table; more call sites raise regression surface without changing the
  retirement outcome. Rejected: a domain event for task/session terminal transitions (new
  infrastructure for a hygiene write).
- **D-3: Releases are best-effort and after the owner's own commit.** Each release helper opens
  the journal's own scope, runs after the settlement/exit transaction has committed, and is
  wrapped in `try/catch` with `LogWarning`. A failed release can never fail or roll back a
  settlement, a kill or an exit record (same order as CARD-0319).
- **D-4: Land request lifecycle.** `RequestAsync` keeps the snapshot; any exception after
  admission (`land_running`, `land_request_identity_conflict`, evidence load, final-review
  required) releases that snapshot and rethrows. At land outcome, each of the three `ClearPending`
  sites (`RunRequestAsync` no-longer-eligible cancel, `SweepAsync` stale cancel,
  `CompleteTerminalLockedAsync`) is followed by `ReleaseTaskConsumersAsync(task.Id)` after its
  commit. The Conflicts branch (task Blocked, request NeedsResolution) releases nothing. Rejected:
  storing the reservation id on `AgentTaskLandRequest` (a migration for a fact derivable from
  `TaskId`; the land owner is Succeeded, so every task-attributed row is an orphan at outcome).
- **D-5: Answer and refine.** `AgentTaskReplyService.AdmitWorkspaceAsync` returns the snapshot;
  `AnswerAsync` and `RefineAsync` release it and rethrow on any exception before their save.
  Admission stays first: `C459_AnswerReservesWorkspace` expects `workspace_reserved` before the
  session check.
- **D-6: A8 attribution.** `AttachHerdrAsync` computes `sessionId` before admission and passes
  `SessionId: sessionId`. The attach-failure catch calls `ReleaseSessionConsumersAsync(sessionId)`
  after its save. Legacy unattributed rows are orphans after grace. Rejected: a separate handle
  for the adopted pane (the session id is the handle).
- **D-7: A4 first dispatch admits nothing.** In `AgentTaskDispatcher` the admission block is
  skipped when `claimed.Workspace == Worktree && claimed.WorktreePath is null`: the key would be
  the repository root, which no leaf retirement can match, and the leaf is fenced by A5 with a
  session owner and by every re-dispatch. Shared/ReadOnly dispatch and re-dispatch admit as today
  (`C459_WriterDispatchReservesWorkspace`, `C459_ReadOnlyDispatchReservesWorkspace` seed
  `WorktreePath`).
- **D-8: Reconcile in code, backfill at startup, no migration.** `ReleaseOrphanedConsumersAsync`
  applies D-1's predicate over all active `Launch` rows (owners loaded in one batch each for tasks
  and sessions) and returns the released count. It runs at the start of
  `WorktreeResidueSweepService.PersistRunAsync` and once in the startup scope after
  `DatabaseSeeder.SeedAsync`, each best-effort with a logged count. Rejected: a SQL data migration
  (duplicates the predicate in SQL, diverges from grace/status logic, and migrations here are
  schema-only); a new hosted service (the residue job is already the scheduler); new indexes (the
  active set collapses after the first reconcile; measure before adding).
- **D-9: Settings.** One new setting, `WorktreeResidue:LaunchGraceMinutes` (int, default 15,
  clamped to at least 1) on `WorktreeResidueSettings`. The journal takes
  `IOptions<WorktreeResidueSettings>? settings = null` so hand-built harnesses keep resolving.
- **D-10: Test clock.** `RaceWorld` stays on `TimeProvider.System`; tests age rows by writing
  `CreatedAt` through a `DbContext`, and the pure predicate is tested with explicit `now` values.
  Rejected: switching the journal to `FakeTimeProvider` in `RaceWorld` (touches 26 passing tests).
- **D-11: Interaction with the separate land-cleanup card.** After this card the SettledTask lane
  can claim worktrees whose land cleanup was refused with `ignored_content_preserved`; whether
  `TryRemoveAsync` then removes them is still governed by `GuardedWorktreeRemoval` and CARD-0452,
  and by `WorktreeResidue:Execute`. This card must not add an ignored-content override or change
  the Publication lane. Expect `retirement_claim_refused` counts to fall and
  `ignored_content_preserved`/typed removal residues to stay until that card lands.

## Liveness predicate

New pure type `server/Application/Services/WorkspaceReservationLiveness.cs`:

    public static bool Blocks(
        WorkspaceReservationKind kind, Guid? rowRetirementId, Guid? claimRetirementId,
        DateTime createdAt, DateTime now, TimeSpan grace,
        OwnerFacts owner) // record OwnerFacts(bool TaskFound, AgentTaskStatus? TaskStatus, bool LandPending, bool SessionFound, SessionStatus? SessionStatus)

Rules, in order: `Retirement` with `rowRetirementId == claimRetirementId` never blocks (own row);
any other `Retirement` or `HistoricalFence` blocks; `Launch` blocks when `now - createdAt < grace`;
else when `TaskFound && (TaskStatus in Live || LandPending)`; else when
`SessionFound && SessionStatus in {Created, Starting, Running, Stopping}`; else it is orphaned.
`Live` is the existing `{Queued, Dispatched, Working, Blocked}`. The journal builds `OwnerFacts`
from one `AgentTasks` projection (`Id, Status, LandRequestedAt`) and one `AgentSessions`
projection (`Id, Status`) for the overlapping rows' ids, inside the claim transaction.

## Release sites

| Site | Hook | Call | Round |
|---|---|---|---|
| `AgentTaskReplyService.PersistDeliverThenReleaseAsync` | after `SaveChangesAsync`/`TaskAlreadyPersistedAsync`, before delivery | `ReleaseTaskConsumersAsync(task.Id)` when status is Succeeded/Failed/Canceled | B1 |
| `AgentTaskService.CancelAsync` | after `SaveChangesAsync` | `ReleaseTaskConsumersAsync(task.Id)` | B1 |
| `AgentTaskDispatcher.FailAsync` | after the caller's commit is not visible here; call after `_db.SaveChangesAsync` inside `FailAsync` | `ReleaseTaskConsumersAsync(task.Id)` | B1 |
| `AgentTaskDispatcher` claim admission | skip when Worktree and `WorktreePath is null` (D-7) | none | B1 |
| `AgentTaskLandService.RequestAsync` | `catch` after admission | `ReleaseConsumerAsync(snapshot)` then rethrow | B1 |
| `AgentTaskLandService` three `ClearPending` sites | after each transaction commit | `ReleaseTaskConsumersAsync(task.Id)` | B1 |
| `AgentTaskReplyService.AnswerAsync` / `RefineAsync` | `catch` after admission | `ReleaseConsumerAsync(snapshot)` then rethrow | B1 |
| `AgentSessionRuntime.CloseSessionOnExitAsync` | after `transaction.CommitAsync` when terminal | `ReleaseSessionConsumersAsync(sessionId)` | B2 |
| `AgentSessionService.KillOnAsync` | after the terminal `SaveChangesAsync` | `ReleaseSessionConsumersAsync(sessionId)` | B2 |
| `AgentSessionService.LaunchInteractiveAsync` failure catch | after its save | `ReleaseSessionConsumersAsync(session.Id)` | B2 |
| `AgentControlService.AttachHerdrAsync` | admission gets `SessionId`; failure catch releases | `ReleaseSessionConsumersAsync(sessionId)` | B2 |
| `WorktreeResidueSweepService.PersistRunAsync` | first statement | `ReleaseOrphanedConsumersAsync` | A |
| `Program.cs` startup scope | after `DatabaseSeeder.SeedAsync` | `ReleaseOrphanedConsumersAsync` | A |

`AgentTaskService.AdmitWorkspaceAsync` (create/merge/commit children) keeps its `Task` shape; a
create that throws after admission leaves a `TaskId` that names no row, which D-1 treats as an
orphan after grace.

## Slices and bounded Code rounds

Read docs/testing-and-build.md (Fast lane, Checkpoint manifest, Combined class filters,
CARD-0459 worktree residue) and the AGENTS owners for sessions and orchestration before each
round. Work on a fresh task branch from the landed revision. Commit and push each red commit and
each implementation slice with its actual verification state. Run git through a bounded process
(30 seconds local, 120 seconds fetch/push). No round exceeds about 90 minutes of authoring; the
verification budget per round is stated in the Checkpoints table.

| Slice / round | Deliverable and files | Tests / completion boundary | Authoring budget |
|---|---|---|---:|
| S1 + S2 / Round A: predicate, filtered claim, reconcile | New `server/Application/Services/WorkspaceReservationLiveness.cs`. `server/Application/Interfaces/IWorkspaceReservationJournal.cs`: add `ReleaseTaskConsumersAsync(Guid taskId, ct)`, `ReleaseSessionConsumersAsync(Guid sessionId, ct)`, `Task<int> ReleaseOrphanedConsumersAsync(ct)`. `server/Infrastructure/Data/WorkspaceReservationJournal.cs`: liveness in `TryClaimRetirementAsync` with lazy release, `ReadActiveAsync` gains a `blockingOnly`/retirement-id overload used by admission, the three new methods, optional `IOptions<WorktreeResidueSettings>`. `server/Application/Services/WorkspaceUseAdmission.cs`: `RequireConsumerAsync` returns the snapshot, `ReleaseAsync(snapshot)`, `ReleaseTaskConsumersAsync`, `ReleaseSessionConsumersAsync`, `FindLiveConsumersAsync` uses the filtered read. `server/Application/Settings/WorktreeResidueSettings.cs`: `LaunchGraceMinutes`. `server/Application/Services/WorktreeResidueSweepService.cs`: reconcile at `PersistRunAsync` start. `server/Program.cs`: startup reconcile. | Red commit first: new `tests/Antiphon.Tests/Application/WorkspaceReservationLivenessTests.cs` (Unit) against a stub predicate that always blocks; new `C664_*` methods in `WorktreeRetirementRaceTests.cs` (V-1, V-2, V-3, V-4). CP-1/2/3/4. | 80 min + checks |
| S3 / Round B1: task-side releases | `AgentTaskReplyService.cs` (funnel release, answer/refine catch-release), `AgentTaskService.cs` (cancel release), `AgentTaskDispatcher.cs` (fail release, D-7 skip), `AgentTaskLandService.cs` (request catch-release, three outcome releases). | Red commit first: `C664_*` methods in `WorktreeRetirementRaceTests.cs` and `AgentTaskSettlementRaceTests.cs` (V-5, V-6, V-7, V-8). CP-5/6. | 75 min + checks |
| S4 / Round B2: session-side releases, A8, docs | `AgentSessionRuntime.cs`, `AgentSessionService.cs`, `AgentControlService.cs`. Docs: `docs/testing-and-build.md` (CARD-0459 section: filter classes and `bin-c664-*`), `docs/orchestration-loop.md` (retirement paragraph: owner-liveness and grace), `docs/bootstrap.md` (residue job row: reconcile and `LaunchGraceMinutes`), investigation cross-link to this plan. | Red commit first: `C664_*` methods in `WorktreeRetirementRaceTests.cs`, `SessionGenerationExitTests.cs`, `AgentAttachHerdrTests.cs` (V-9, V-10, V-11). CP-7/8. | 70 min + checks |

A round that reaches its limit commits the finished slice and reports the next exact slice; it
does not claim the card complete. Rounds B1 and B2 are independent of each other and may run as
two Code tasks only if they use different branches and neither touches the other's files.

## Verification design

Ordinary Code follows the Checkpoints table as a closed list. Each round has one isolated
build for its red row and an incremental rebuild for its green row. The investigation measured
the cold isolated build of `tests/Antiphon.Tests` at 3m13s and an incremental rebuild at 1m23s
on server2; test execution per row is about a minute (Testcontainers Postgres start plus the
named classes). The `EstimatedMinutes` column counts both. No full-suite, namespace or nightly
run is commissioned.

### Coverage and falsifiable assertions

All integration rows below need Testcontainers Postgres and run on Linux (server2) as well as
Windows; `AgentAttachHerdrTests` also needs the fake session runner its class already starts.
`WorkspaceReservationLivenessTests` is DB-free. Nothing here needs ConPTY or a real git repository.

| ID | Class / methods (new executions) | Behavioral oracle and expected baseline red |
|---|---|---|
| V-1 | `WorkspaceReservationLivenessTests` (Unit; 8 methods, 20 executions): `C664_FreshRowBlocksWhateverTheOwner`; `C664_LiveTaskOwnerBlocksAfterGrace` [Arguments Queued, Dispatched, Working, Blocked]; `C664_TerminalTaskOwnerDoesNotBlockAfterGrace` [Arguments Succeeded, Failed, Canceled]; `C664_PendingLandKeepsTerminalTaskBlocking`; `C664_LiveSessionOwnerBlocksAfterGrace` [Arguments Created, Starting, Running, Stopping]; `C664_EndedSessionOwnerDoesNotBlockAfterGrace` [Arguments Stopped, Failed]; `C664_MissingOrUnattributedOwnerDoesNotBlockAfterGrace` [Arguments missing-task, missing-session, unattributed]; `C664_RetirementAndFenceRowsAlwaysBlock` [Arguments Retirement, HistoricalFence] | Pure calls to `WorkspaceReservationLiveness.Blocks` with explicit `now`, `createdAt = now - grace - 1s` or `now - 1s`. Red commit ships the type with a body that returns true for every `Launch` row: the three "DoesNotBlock" methods (8 executions) fail; the rest pass. Removing the grace branch later fails the fresh-row method; removing the land-pending branch fails that method. |
| V-2 | `WorktreeRetirementRaceTests.C664_OrphanedLaunchRowsDoNotBlockRetirement` [Arguments terminal-task, ended-session, unattributed, missing-task] (4) | Seed a `Launch` row through `Journal.TryAdmitConsumerAsync` with the shape's owner (Failed task / Stopped session / no owner / random `TaskId`), then set `CreatedAt = now - 2h` via `CreateDb()`. `TryClaimRetirementAsync(CreateKeyCommand())` must be `Accepted`, and the seeded row must read back `Active == false` with `ReleasedAt != null`. Baseline: `workspace_in_use` for all four. |
| V-3 | `WorktreeRetirementRaceTests.C664_LiveOwnerStillBlocksRetirement` [Arguments working-task, running-session, land-pending, fresh-unattributed] (4, guard) | Same seeding with a Working task / Running session / Succeeded task with `LandRequestedAt = now` / fresh unattributed row. Claim must return `Accepted == false`, reason `workspace_in_use`, and the row stays `Active`. Green at baseline; must stay green. This is the CARD-0459 race intent (`C459_LaunchIntentPrecedesEnqueue`) restated under D-1. |
| V-4 | `WorktreeRetirementRaceTests.C664_FindLiveConsumersExcludesOrphanedRows` (1); `C664_ReconcileReleasesOnlyOrphanedRows` (1) | First: an aged `Launch` row for a Failed task plus an aged row for a Working task at the same key; `Admission.FindLiveConsumersAsync(key, OwnerTask.Id)` returns exactly the Working task's row. Baseline returns 2. Second: seed one row per shape (aged terminal task, aged ended session, aged unattributed, aged Working task, fresh unattributed); `Journal.ReleaseOrphanedConsumersAsync` returns 3 and only those three read back inactive. Red commit ships the method returning 0 (compiles, fails on count). |
| V-5 | `WorktreeRetirementRaceTests.C664_LandRequestThenOutcome_ReleasesAndRetirementClaimAccepted` (1) | Owner task set to Worktree/Succeeded with `WorktreePath = Path`, branch `feat/card-task-land` (as `C459_LandReservesWorkspace`). `Lands.RequestAsync` returns a request id and one active `Launch` row exists for the owner. `Lands.FailRequestAsync(owner.Id, requestId, new InvalidOperationException("drain"))` reaches `CompleteTerminalLockedAsync`. Then zero active `Launch` rows for the owner and `TryClaimRetirementAsync(ProductionRetirementCommand("feat/card-task-land"))` is `Accepted`. Baseline: one active row, `workspace_in_use`. If `FailRequestAsync` cannot complete in `RaceWorld`, the substitute is: set the owner `Failed`, call `Lands.SweepAsync`, same assertions; report the substitution. |
| V-6 | `WorktreeRetirementRaceTests.C664_LandRequestRefusedAfterAdmission_LeavesNoExtraLaunch` (1) | After a first `RequestAsync` (queued, `_queue.IsActive`), a second `RequestAsync` throws `ConflictException` with code `land_running`. Exactly one active `Launch` row for the owner remains (the first). Baseline: two. |
| V-7 | `WorktreeRetirementRaceTests.C664_AnswerRefusedAfterAdmission_LeavesNoActiveLaunch` (1); `C664_RequeueThenCanceled_RetirementClaimAccepted` (1) | First: `SeedQueuedTaskAsync(Worktree)` set Blocked with no session; `Replies.AnswerAsync(id, "continue", ct)` throws `ConflictException`; zero active `Launch` rows for `id`. Baseline: one. Second: `Tasks.CreateAsync` a Shared task at `Path`, set Failed via `CreateDb()`, `Tasks.RetryAsync` (admits A2, task Queued), `Tasks.CancelAsync(id)`; zero active `Launch` rows for `id` and `TryClaimRetirementAsync(CreateKeyCommand())` `Accepted`. Baseline: two active rows, `workspace_in_use`. |
| V-8 | `AgentTaskSettlementRaceTests.C664_SettlementReleasesTaskLaunchRows` (1) | Using that class's `BuildHarness()`: seed its settled-task scenario plus an active `Launch` row with the task's id via the journal (register `IWorkspaceReservationJournal` through `DelegationTestServices.AddDelegationWorktreeGraph` if the harness lacks it); drive `replies.OnTurnEndAsync(sessionId)` to a Succeeded settlement; the row reads back inactive. A Blocked settlement variant is not required. Baseline: row stays active. |
| V-9 | `WorktreeRetirementRaceTests.C664_SessionLaunchThenKilled_ReleasesAndRetirementClaimAccepted` (1) | `SeedInteractiveSessionAsync`, `LaunchQueue.EnqueueInteractiveSession(...)`, `WaitForIdleAsync`; `Adapter.Started` true and one active `Launch` row with `SessionId == session.Id`. `Sessions.KillAsync(session.Id, SessionTerminationSource.OperatorRequest, ct)`; session reads Stopped, zero active rows for the session, and `TryClaimRetirementAsync(SessionProductionCommand())` is `Accepted`. Baseline: `workspace_in_use`. |
| V-10 | `SessionGenerationExitTests.C664_RuntimeExitReleasesSessionLaunchRows` (1) | In that class's hand-built runtime harness (isolated schema), register `IWorkspaceReservationJournal` (scoped) and seed a Running session with `StartedAt = g` plus a `Launch` row with its `SessionId`. `runtime.ObserveExitAsync(new SessionRunnerExitedEvent(id, 0, AgentExitReason.Exited, 0, AcceptedStartedAt: g))` returns `Applied`; the row reads back inactive. A stale-generation exit (`AcceptedStartedAt` earlier than `StartedAt`) must leave the row active. Baseline: row active in both cases. |
| V-11 | `AgentAttachHerdrTests.C664_AttachAttributesLaunchReservationToAdoptedSession` (1) | Register `IWorkspaceReservationJournal` and `WorkspaceUseAdmission` in `BuildHarness`; extend `CleanupAsync` to delete `WorkspaceUseReservations` whose `CanonicalPath` starts with the temp root. After `AttachHerdrAsync` succeeds, exactly one active `Launch` row has `CanonicalPath == cwd` and `SessionId == nativeId`, `TaskId == null`. Baseline: `SessionId == null`. The existing eight methods must stay green with admission now active in the harness (they run against a shared store, so no retirement rows exist for their temp paths). |
| R-1 | `WorktreeRetirementRaceTests` existing `C459_*` (26 methods, 28 executions) | Every fence stays refused: create, requeue, answer, writer/read-only dispatch, land, direct/interactive start, resume, interrupted attach, immutable generation, launch-intent-precedes-enqueue, completed-path fence, task/session/unknown-backend holds, cleanup-never-stops-owner, refine/merge/commit/Herdr admissions. `C459_RequeueReservesWorkspace` keeps its manual release (it exercises the id/generation primitive). |
| R-2 | `TaskWorktreeRetirementTests` (17 methods), `WorktreeResidueSweepTests` (14), `WorktreeResidueRecoveryTests` (10) | Eligibility reasons, sweep lanes, cooldowns and the HistoricalFence observations are unchanged; the reconcile call at `PersistRunAsync` start does not alter counts or run persistence. |
| R-3 | `AgentTaskLandRequestTests` (11), `AgentTaskLandRefusedRetryTests` (11), `AgentTaskSettlementRaceTests` existing (7) | Land request identity/conflict behavior, refusal retry and settlement race guarantees unchanged with the catch-release and outcome releases in place. |
| R-4 | `SessionGenerationExitTests` existing (7), `AgentAttachHerdrTests` existing (8) | Exit generation handling and Herdr adoption outcomes unchanged with the session release and A8 attribution in place. |
| R-5 | Unit lane of `tests/Antiphon.Tests` at the end of Round B2 | Shared settings/interface changes compile and preserve ordinary unit contracts (`TestLaneCategoryGuardTests` sees the new Unit class). |

Rules for every new test: it must fail at its red commit on the production decision it guards
(counts above), not on a compile error or fixture failure; a zero-test TRX is not red. Each new
`C664_*` method seeds rows through the journal or `DbContext` and reads results back from a fresh
`DbContext`, never from the object it wrote. Where a test needs an old row it updates `CreatedAt`
directly (D-10). No test changes `WorktreeResidue:Execute`, calls `TryRetireAsync` against a
non-git directory, or boots a host against port 17204.

### Cost and execution rules

Per round: red build about 3.5 minutes cold (or about 1.5 incremental when the round's build
follows Round A's output on the same machine), red run about 1 minute, green incremental build
about 1.5 minutes, green run 1 to 2 minutes. Round A totals about 7 minutes, Rounds B1 and B2
about 6 minutes each: **19 minutes** ordinary verification plus CP-9's Unit lane (about 4
minutes), total floor **23 minutes**. Authoring budgets are 80, 75 and 70 minutes. These are
estimates, not timeouts or permission to skip a row.

For each TUnit row use `scripts/run-checkpoint.ps1` with the exact Filter, OutputPath, Min and a
fresh ResultsRoot below `.antiphon/c664-checkpoints`; `-Expect` is one comma-separated list.
Example for CP-1:

    pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c664-a/ -Filter '/*/*/WorkspaceReservationLivenessTests/*' -MinExecuted 8 -Expect WorkspaceReservationLivenessTests -ResultsRoot .antiphon/c664-checkpoints/liveness-red

Red rows return the assertion-failure exit code and must show the named failing methods and
nonzero counts. Green rows require all named classes present, zero failed/skipped, and the new
method roster. `Min` is a floor on executed TUnit results (method count, conservative), not the
argument-expanded total, which is given in `Expect`. Emit the owner's `CHECKPOINT CP-n ...` line
per row including reruns. Commit before every build; freeze source during a run. Remove only this
producer's `bin-c664-*` directories after the dependent rows finish, after verifying their
resolved paths are inside the task worktree. Pipes in the Filter column are escaped for
Markdown; the actual filter uses plain `|`, with trailing class wildcards per CARD-0403.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 red | `tests/Antiphon.Tests -> bin-c664-a/` | liveness-red | `/*/*/WorkspaceReservationLivenessTests/*` | V-1 red | 20 executed; `C664_TerminalTaskOwnerDoesNotBlockAfterGrace`, `C664_EndedSessionOwnerDoesNotBlockAfterGrace`, `C664_MissingOrUnattributedOwnerDoesNotBlockAfterGrace` fail (8 executions); others pass | 8 | 4.5 |
| CP-2 | S1-S2 red | `CP-1, --no-build` | race-red-a | `/*/*/WorktreeRetirementRaceTests/(C664_Orphaned*)\|(C664_FindLive*)\|(C664_Reconcile*)\|(C664_LiveOwner*)` | V-2, V-3, V-4 red | 10 executed; `C664_OrphanedLaunchRowsDoNotBlockRetirement` (4), `C664_FindLiveConsumersExcludesOrphanedRows`, `C664_ReconcileReleasesOnlyOrphanedRows` fail; `C664_LiveOwnerStillBlocksRetirement` (4) passes | 4 | 1 |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c664-a/` | liveness-green | `/*/*/WorkspaceReservationLivenessTests/*` | V-1 | 20 executed, 0 failed/skipped | 8 | 2 |
| CP-4 | S1-S2 | `CP-3, --no-build` | race-green-a | `/*/*/(WorktreeRetirementRaceTests*)\|(TaskWorktreeRetirementTests*)\|(WorktreeResidueSweepTests*)\|(WorktreeResidueRecoveryTests*)/*` | V-2, V-3, V-4, R-1, R-2 | all listed classes, 0 failed/skipped; roster includes the 4 new race methods (10 executions) | 70 | 3 |
| CP-5 | S3 red | `tests/Antiphon.Tests -> bin-c664-b1/` | task-red | `/*/*/(WorktreeRetirementRaceTests*)\|(AgentTaskSettlementRaceTests*)/C664_*` | V-5, V-6, V-7, V-8 red | Round A's `C664_*` race methods pass; `C664_LandRequestThenOutcome*`, `C664_LandRequestRefusedAfterAdmission*`, `C664_AnswerRefusedAfterAdmission*`, `C664_RequeueThenCanceled*`, `C664_SettlementReleasesTaskLaunchRows` fail (5) | 15 | 4.5 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c664-b1/` | task-green | `/*/*/(WorktreeRetirementRaceTests*)\|(AgentTaskSettlementRaceTests*)\|(AgentTaskLandRequestTests*)\|(AgentTaskLandRefusedRetryTests*)/*` | V-5, V-6, V-7, V-8, R-1, R-3 | all listed classes, 0 failed/skipped; roster includes the 5 new methods | 55 | 3.5 |
| CP-7 | S4 red | `tests/Antiphon.Tests -> bin-c664-b2/` | session-red | `/*/*/(WorktreeRetirementRaceTests*)\|(SessionGenerationExitTests*)\|(AgentAttachHerdrTests*)/C664_*` | V-9, V-10, V-11 red | earlier `C664_*` race methods pass; `C664_SessionLaunchThenKilled*`, `C664_RuntimeExitReleasesSessionLaunchRows`, `C664_AttachAttributesLaunchReservationToAdoptedSession` fail (3) | 16 | 4.5 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c664-b2/` | session-green | `/*/*/(WorktreeRetirementRaceTests*)\|(SessionGenerationExitTests*)\|(AgentAttachHerdrTests*)/*` | V-9, V-10, V-11, R-1, R-4 | all listed classes, 0 failed/skipped; roster includes the 3 new methods | 50 | 3.5 |
| CP-9 | S4 | `CP-8, --no-build` | unit-final | `/*/*/*/*[Category=Unit]` | R-5 | >= 1 executed, 0 failed; `WorkspaceReservationLivenessTests` in the roster | 1 | 4 |

CP-5 and CP-7 use the method-segment OR form combined with a class-segment OR. If the pinned
TUnit 1.44 runner does not honor that combination (it must select exactly the `C664_*` methods
of the named classes, verified from the TRX roster), split the row into one invocation per class
with `/*/*/<Class>/C664_*`, reusing the same build, and report the split as reruns of the same
`CP-n`; do not widen to a whole class for a red row.

## Not done here, deferred

- A4 dispatch reproduction with a real git repository in `RaceWorld` (the investigation's
  inconclusive repro). The dispatcher's `FailAsync` release and D-7 skip are covered by V-2's
  `terminal-task` shape (the A4 orphan shape) and the existing C459 dispatch fences; a git-backed
  dispatch test is optional follow-up work, not a row in this table.
- Releases at the five exotic task terminal writers and the five other session closers (D-2).
- Any index on `WorkspaceUseReservations(TaskId)`/`(SessionId)`; measure the active row count on
  the desktop after the first startup reconcile (`ReleaseOrphanedConsumersAsync` logs the count)
  before proposing one.
- The land-cleanup `ignored_content_preserved` refusal (separate card; see D-11).

## Operator notes for activation

After land and the canonical desktop restart, the startup log carries one line
`Workspace reservations reconciled: released N orphaned Launch rows` (N is the backfill). The
investigation's read-only SQL still applies for verification; expected afterwards: active
`Launch` rows whose task is terminal and has no pending land, or whose session is terminal, older
than `LaunchGraceMinutes`, count 0. `WorktreeResidue:Execute` stays false; the daily job and
`POST /api/agent-tasks/worktree-residue/preview` both run the reconcile and should show
`retirement_claim_refused` disappear from SettledTask rows on the next execute-mode run.
