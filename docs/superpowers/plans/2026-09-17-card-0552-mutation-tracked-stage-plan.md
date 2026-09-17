# CARD-0552: Mutation debt becomes visible from the land outcome

Plan task `b5cd5ee0`, 2026-09-17, inspected checkout `6bda48d3` (master). Investigation:
[2026-09-17-card-0552-mutation-tracked-stage.md](../../investigations/2026-09-17-card-0552-mutation-tracked-stage.md)
(task `0d3ff398`). Prior design authority: [CARD-0478 plan](2026-09-10-card-0478-mutation-after-land-plan.md)
D-1..D-9; this plan changes WHO records the CARD-0478 companion, adds ONE read-model source, and
(after the operator refinement below) one budget-capped creator for that source plus one attention
kind. It re-proposes none of CARD-0478's rejected options.

**Operator refinement (2026-09-17, received mid-plan):** Mutation work defaults to running
unattended (overnight, or whenever the backlog has debt), not to an explicit trigger per battery.
The only throttles are usage/budget ceilings (spend, provider quota near its limit); WIP=1 and
cost-per-PC are noted as natural pacing. This amends CARD-0478 D-3's "spends model quota from an
orchestration tick" sentence for the Mutation role only; it does not add a CardStatus, a column
or a general dispatch engine. D-9..D-12 carry it; the Diagnose sweep (CARD-0352) is the shipped
precedent for a one-role, budget-capped, hold-aware creator inside the server.

## Disposition in five lines

1. **The companion card is created by the confirmed publication, not by orchestrator memory.**
   `AgentTaskLandService.CompleteTerminalLockedAsync` already holds the operation `O`, the landing
   owner task and the row lock at the moment `HasPublication` becomes true. That transaction now
   also creates-or-links the CARD-0478 companion (`Post-land verification: <original>`, label
   `post-land-verification`, stable key `post-land-verification:<code-task-guid>`), records O/L/R
   on it, appends the reverse link on the original card, and names the companion in the land
   outcome the caller already receives (D-1, D-2).
2. **One structural link, no new table:** `AgentTaskLanding.VerificationCardId` (nullable FK to
   `Cards`, Restrict). Many operations per companion (a repair's O2 reuses the card, CARD-0478
   D-7); the stable text key stays in the description for discovery exactly as CARD-0478 wrote it
   (D-3).
3. **The Mutation stage's `ready` list gains one new source:** a confirmed publication whose
   companion is open and has no open or Succeeded sourced Mutation task. The row's card is the
   companion, so `BuildReady`'s Done-card exclusion is untouched and the original stays Done
   (D-4, D-5). The orchestrator dispatches Mutation the way it dispatches every other stage:
   from the glance's `ready` row, explicitly, under Mutation's WIP of 1.
4. **The land and the card-transition sweep create no task, move no card, add no status.**
   Creating the companion creates no task and no session (G-133 stays green for `ScanAsync`);
   `CardWorkTransitionService` moves the companion through InProgress and Review off the Mutation
   task exactly as today (D-6). An idempotent `POST /api/agent-tasks/{id}/verification-companion`
   runs the same code for the 11 already-landed cards that have a confirmed operation; the
   manual-merge landing (CARD-0514) is refused with a named code and stays a separate decision
   (D-7, Backlog section).
5. **Debt is worked unattended under ceilings, not on a human trigger.** A `MutationAutoDispatchSweep`
   (the Diagnose sweep's shape) takes the oldest Mutation `ready` row every few minutes and creates
   ONE sourced Mutation task through the ordinary `AgentTaskService.CreateAsync` admission when: no
   Mutation-role task is open (the role's create-time WIP of 1), the orchestrator is not paused, the
   Mutation route is not held, the UTC-day Mutation spend is under `MutationAutoDispatch:DailyBudgetUsd`,
   and that operation has never been attempted (retries stay explicit). The existing subscription-quota
   gate refuses the create when the provider is near its limit; the sweep never passes an
   `Ignore*` flag. A settled sourced battery with nobody to report to surfaces on the attention
   feed until the companion is dispositioned (D-9..D-12).

## Ground truth

| Card/investigation assumption | Observed on `6bda48d3` | Consequence |
|---|---|---|
| Mutation is fully wired as a stage role; only two links are missing. | `AgentTaskRoles.IsStage` includes Mutation (`AgentTaskEnums.cs:387-393`); `stage-mutation.md` exists; `PipelineHandoff` maps `mutation` (`PipelineHandoff.cs:33`, `:86-88`); `BuildReady` projects any Succeeded stage task whose `NextStage` is a stage role (`AgentTaskPipelineStatusService.cs:209-283`); `RecommendedInFlight(Mutation)=1`. | No role, bundle, parser, pin or WIP change. Only a companion writer and a ready-row source are added. |
| A companion has never been created. | 0 of 546 cards carry the label or title (investigation §4); `post-land-verification` appears in no `.cs` file. | D-1 moves creation server-side; the text contract is preserved verbatim so `PostLandMutationContractTests.C478_V11` keeps passing. |
| The land outcome is the right hook. | `CompleteTerminalLockedAsync` (`AgentTaskLandService.cs:678-750`) runs under `LockTaskAsync`, loads `op`, and is the single terminal writer for `Landed` / `LandedWithResidue` / `AlreadyPresent` / `LandingCleanup`; `PersistFailureAsync`'s published branch (`:645-652`) also ends there. It appends cleanup detail to `outcome` before `Event(...)` (`:717`) and commits at `:745-747`, then `PublishAsync`. | The hook sits after `op` is loaded and before `var terminal = Event(...)`, guarded by `new AgentTaskLandingState().HasPublication(op)`. Interrupted-after-publication lands still get a companion. |
| Cleanup retries would re-create the companion. | `alreadyReported` (`:692-695`) downgrades a second terminal on the same op to `LandingCleanup`; C448_V33 (`AgentTaskLandPublicationTests`) pins one publication event per op. | D-2's ensure is idempotent on `op.VerificationCardId`; a retry appends no second revision. |
| The projection cannot show Done cards. | `BuildReady` skips `Done/Canceled/NeedsDecision` (`:224`) and only groups tasks by their bound card. `CardWorkTransitionService.Decide` never moves Done cards (`:173-206`). | D-4 keys the row on the companion (Backlog until a Mutation task dispatches), not on the original. No exclusion is lifted. |
| A Mutation task is bound to the companion and carries the operation. | `SourceLandingAdmission.RequireSourceAsync` requires `task.CardId != source.CardId`, same board, and `HasPublication` (`SourceLandingAdmission.cs:24-50`); `AgentTask.SourceLandingOperationId` is immutable after save (`AppDbContext.cs:1684`) and FK Restrict (`:1683`). | Consumption (D-5) is a join on `SourceLandingOperationId`, not on the card+role rule. |
| The stable key is the only link. | CARD-0478 D-3: "The stable text key is discoverable, not DB uniqueness." No FK exists between `AgentTaskLandings` and `Cards` (`AppDbContext.cs:1640-1700`). | D-3 adds the FK on the landing side; the key remains the human/orchestrator discovery path and the compatibility path for a caller-created companion. |
| Server-side card creation has a precedent. | `ExternalTrackerSyncService` (`:251-293`) adds `Card` rows directly with `CardIdentifierAllocator.ForBoardAsync(...).Next()`; `CardService.CreateAsync` (`:142-192`) goes through the card-file lease and publishes `CardChanged` before returning. `IX_Cards_BoardId_Identifier` is unique (`AppDbContext.cs:976`). | D-2's writer depends on `AppDbContext` + `TimeProvider` only, allocates inside the land transaction, and the land service publishes `CardChanged` after commit. A lost identifier race is a unique-index failure of the land terminal save, retried by the existing land sweep. |
| Backlog cards are never auto-spawned. | `OrchestratorService` candidate query: active, non-terminal column and `AutoDispatchHeldAt == null` (`:830-834`); `ApplyAutomatedMoveAsync` sets the hold in the same write as a move into an active column (`CardService.cs:1065-1068`). | A companion in Backlog spends nothing (G-127/G-133). |
| The ready row DTO can carry the dispatch coordinates. | `AgentTaskPipelineReadyDto` is a positional record with defaulted trailing members (`AgentTaskPipelineDtos.cs:76-97`); client type has optional `sourceRole`/`handoff` (`client/src/api/agentTasks.ts:488-499`); `rowTarget` opens the task drawer when `deliverablePath` is empty (`pipelineStageModel.ts:240-252`). | D-4 appends three optional members; the client needs only type parity, no rendering change. |
| The Review handoff names the PC count in prose. | `AgentTask.NextHandoff` (`AgentTask.cs:298`) holds the `handoff:` line; every one of tonight's close reasons quotes "N PCs pending" from it. No structured count exists (investigation §3). | The companion quotes the Code and Review handoff lines verbatim (no parsing); a structured count stays out of scope. |
| The cards API has no label filter. | `GET /api/cards` takes `updatedSince`/`status`/`boardId` only (`CardEndpoints.cs:44-56`). | Not added. The Mutation `ready` row IS the queue and carries card GUID, operation GUID and L; the label remains the board-UI affordance (`boardShapeModel.ts:100`). |
| Two landed cards have no landing operation. | CARD-0514 (in the 12) and CARD-0499 (ambiguous set) were pushed by manual rebase+merge; admission would refuse them (`SourceLandingAdmission.cs:29-32`). | D-7: the endpoint refuses `verification_publication_unconfirmed`; legacy admission is a separate card if the operator wants those PCs run under custody. |
| Land is constructed directly in tests. | 9 `new AgentTaskLandService(` sites in `tests/`; `LandingSafetyHarness` builds it from DI with `ConfigureServices`; `AgentTaskLandStageOutcomeTests.CreateLand` drives a real-git `Landed` terminal and asserts `Detail` markers (`:314-321`). | The new writer is a required constructor dependency with only `AppDbContext`/`TimeProvider`/logger inputs, so every site constructs it in one line. |
| (Refinement) An in-server sweep that spends model quota has no precedent. | `DiagnoseSweepHostedService` (`server/Infrastructure/Orchestration/`, `PeriodicTimer`, one scope per tick) drives `CardDiagnosisSweep.TickAsync`, which gates on `DiagnoseEnabled`/`DiagnoseSweepEnabled`, the seat's `IModelAvailability.IsHeldAsync`, and `DailySpendUsdAsync() >= DiagnoseDailyBudgetUsd` (UTC-day sum of `AgentTask.CostUsd` by role) before enqueueing; crossing the budget "writes DegradedBudget and creates no row" (`DelegationSettings.cs:722-723`). Registered at `Program.cs:671`; test hosts disable it via `Delegation:DiagnoseEnabled=false` (`AntiphonWebAppFactory.cs:110`, `ProductionRunnerGuard.cs:51`). | D-9 copies that shape for one role with the same three gates plus the WIP and attempt-once rules; test hosts get the same off switch. |
| (Refinement) A budget ceiling needs new machinery. | Create-time subscription-quota gate (CARD-0136): `SubscriptionQuotaLowException` 409 `subscription_quota_low` with rules `low-with-a-day-left` (<=10 % remaining, >=1440 min to reset) and `critical-with-hours-left` (<=5 %, >=120 min) (`SubscriptionQuotaGateSettings.cs:29-30`); only `IgnoreSubscriptionQuota` bypasses it (`AgentTaskService.cs:656-807`). `ModelAvailabilityHold = 24` and `RoutingExhausted = 25` are existing attention kinds. | The provider-near-limit ceiling already exists at create; D-9 adds only the USD/day ceiling per role (`DiagnoseDailyBudgetUsd` precedent) and never passes an `Ignore*` flag. |
| (Refinement) WIP=1 is advisory. | `RolePolicy[role].RecommendedInFlight` (Mutation = 1, `shipped_limits_are_one_and_custom_is_unbounded`) is a create-time REFUSAL when the role's open count meets it, per project scope, unless `ignoreConcurrencyLimit` (`DelegationSettings.cs:960-975`, `AgentTaskService.cs:1084-1127`). | WIP=1 is the hard pacing; the sweep additionally skips a tick when any Mutation-role task is open so it never burns a 409. |
| (Refinement) A task needs a calling session. | `AgentTaskService.Caller(Task: null, SessionId: null, WorkingDirectory, ..., ProjectId, BoardId)` is the HTTP manual entry point's shape; `MayDelegate` is true for a null task; `ReplyTo` becomes `None` when `SessionId` is null (`AgentTaskService.cs:118-132`, `:1006`); directory authorization is `AllowedRoots` (`:421-426`, `:1076-1077`). | D-9's create uses that caller with `WorkingDirectory = op.RepositoryPath` and the owner's `ProjectId` (same commissioning project). The report then lands on the board only, which is why D-11 exists. |
| (Refinement) Nothing surfaces a settled sourced battery with no caller. | Attention kinds end at `CommitRecoveryPending = 41` (`AttentionDtos.cs:285`); none names a settled report awaiting triage (`ReportUnsettled = 22` is parse state; `CardNeedsDecision = 13` needs a NeedsDecision move). Client union in `client/src/api/attention.ts:17`, visuals in `attentionVisuals.ts`. | D-11 appends `MutationDispositionPending = 42`. |
| (Refinement) A battery outlives the default deadline. | `DefaultTimeoutMinutes = 240`, `DefaultExpectedMinutes = 10` (`DelegationSettings.cs:387`, `:587`); `CreateAgentTaskRequest.ExpectedMinutes` (`delegate.ps1 -ExpectAbout`, `:865`); the persistent evidence root is composed by the formatter from `VerificationCreationJson` at dispatch (`DelegationReportFormatter.cs:184`). | D-10's brief needs no evidence-root text; D-12's `ExpectedMinutes` default is 720. |

## Decisions

### D-1. The confirmed publication records the obligation

In `CompleteTerminalLockedAsync`, after `op` is loaded and before the terminal event is built:

```csharp
PostLandVerificationCompanions.Result? companion = null;
if (op is not null && new AgentTaskLandingState().HasPublication(op))
{
    companion = await _companions.EnsureAsync(task, op, now, ct);   // same transaction, same lock
    outcome += $"; companion={companion.Identifier} ({companion.CardId:D})";
}
```

`EnsureAsync` returns the existing link when `op.VerificationCardId` is already set (cleanup
retries, `LandingCleanup` terminals, the interrupted-after-publication path) and writes nothing
else in that case. Otherwise it resolves the companion in this order and links `op` to it:

1. The newest non-archived, non-terminal card already linked from another confirmed operation of
   the same landing owner (`AgentTaskLandings.Where(o => o.TaskId == task.Id && o.VerificationCardId != null)`)
   -- a repair's O2/L2 lands on the same companion (CARD-0478 D-7).
2. Else the newest non-archived card on the original's board whose description contains the
   stable key `post-land-verification:<task.Id:D>` -- a companion a caller created under the
   pre-CARD-0552 recipe. Its description is appended to, never replaced ("preserve existing").
3. Else a new Backlog card (D-2).

A linked-but-terminal companion (Done/Canceled) is not reused: a later O2 after a clean battery
is a new obligation, and CARD-0478 forbids automatic reopen. The new card's description names
the superseded companion. Two cards then share the stable key; the description says which is
current, and the projection (D-4) only ever shows the open one.

Then, on a NEW link only: append a content revision on the companion with
`O=<op:D> L=<VerifiedSourceSha> R=<ObservedRemoteTargetSha> publication=<Landed|AlreadyPresent> confirmed at <RemoteConfirmedAt:O>`
(reason `Confirmed publication <op:N>`, editor `land`), and append one line to the original
card's description through `CardRevisionLog.AppendContentEdit` (reason `Post-land verification companion`,
editor `land`): `Post-land verification: <companion identifier> (<guid>)`. The original's
`ConcurrencyToken` rotates; `card.ps1`'s default fresh-token read absorbs that.

**Why here and not pre-land, not a sweep, not a tick:** the publication and its obligation commit
in one transaction, so no caller interruption between Review and land can lose the record (the
reason CARD-0478 D-3 wanted pre-land creation). A sweep would be a second writer beside the
terminal (the CARD-0056 flap shape). A tick is exactly what G-133 forbids. **Rejected:** a
synthetic `next: mutation` stage report from the land (CARD-0478 D-2: land is an operation, not a
report); creating the companion at `-Land` request time (a refused land would leave a companion
with nothing to verify); a `CardService.CreateAsync` call inside the land (drags the card-file
lease and an early `CardChanged` publish into a locked transaction).

### D-2. `PostLandVerificationCompanions`: the writer, with `AppDbContext` and a clock

`server/Application/Services/PostLandVerificationCompanions.cs`, `public sealed class`, scoped,
constructor `(AppDbContext db, TimeProvider clock, ILogger<PostLandVerificationCompanions> logger)`.
Registered in `Program.cs` beside `AgentTaskLandService`; `AgentTaskLandService` takes it as a
required constructor dependency.

```csharp
public const string Label = "post-land-verification";
public const string KeyPrefix = "post-land-verification:";
public sealed record Result(Guid CardId, string Identifier, bool Created, bool Linked);

// Requires an open transaction and the owner's row lock (the land terminal holds both).
public Task<Result> EnsureAsync(AgentTask owner, AgentTaskLanding op, DateTime now, CancellationToken ct);

// Pure: the description a NEW companion is created with (ASCII, <= MaxDescriptionLength).
public static string Describe(Card original, AgentTask owner, AgentTaskLanding op, CompanionContext context);
public static string StableKey(Guid ownerTaskId) => KeyPrefix + ownerTaskId.ToString("D");
public static string Title(string originalIdentifier) => $"Post-land verification: {originalIdentifier}";
```

A new card: `BoardId` = original's board; column = first `CardStatus.Backlog` column by
`ColumnOrder`, else the board's first column (the `CreateAsync` fallback); `Identifier` from
`CardIdentifierAllocator.ForBoardAsync(...).Next()`; `Title` as above; `LabelsJson` =
`BoardService.SerializeLabels([Label])`; `Importance`/`Urgency` Normal, `ImportanceProvenance`
Auto; `Status` = column status; `Description` from `Describe`. `CompanionContext` is loaded in
`EnsureAsync` with no-tracking queries: the newest Succeeded Review task on the original card
with `NextStage == Land` (id and `NextHandoff`), the owner's `NextHandoff`, the newest Succeeded
Plan task on the original card with a verified plan deliverable (`IsVerifiedPlanDeliverable`),
and the owner's `ProjectId`. The description (all ASCII, one fact per line):

```text
post-land-verification:<owner:D>

Original card: <CARD-nnnn> (<card guid>)
Landing owner (Code task): <owner:D>
Review task: <guid or "none recorded">
Commissioning project: <guid or null>
Plan: <docs/superpowers/plans/... or "not recorded">
Reviewed C: <op.OriginalSourceSha>
O: <op:D>  L=<op.VerifiedSourceSha>  R=<op.ObservedRemoteTargetSha>
Publication: <Landed|AlreadyPresent> confirmed at <RemoteConfirmedAt:O>; cleanup=<op.Cleanup>
Code handoff: <owner.NextHandoff or "none recorded">
Review handoff: <review.NextHandoff or "none recorded">

Pending PC inventory: every PC-n and named variant in the plan's Verification design; none
executed at L. Record the executed counts, evidence root and restoration verdict here at close.
Dispatch (explicit, WIP 1): delegate.ps1 -Role Mutation -Card <companion guid> -Worktree -SourceLanding <op:D>
```

`publication pending` is what a CALLER-created companion says before its land (the stable key
paragraph in the docs keeps that wording); a server-created companion is born with the
publication confirmed. **Rejected:** parsing `| PC-n |` rows out of the plan doc for a count
(CARD-0478 excluded a PC parser; the count is prose the Mutation delegate must re-derive anyway);
copying the full Review report (the task id is on the card; the thread endpoint shows the report).

### D-3. `AgentTaskLanding.VerificationCardId` is the link

Nullable `Guid?` on the landing row; `entity.HasOne<Card>().WithMany().HasForeignKey(o => o.VerificationCardId).OnDelete(DeleteBehavior.Restrict)`,
index `IX_AgentTaskLandings_VerificationCardId`. Migration `AddLandingVerificationCard` via
`dotnet ef migrations add` (project-context rule 9; never hand-written). Exposed as
`verificationCardId` on `LandingEvidenceDto` (additive) so `GET /api/agent-tasks/{id}` shows it.

**Why the landing side:** the relation is many operations to one companion (D-1 step 1) and the
landing row is the object `HasPublication` is judged on, so the projection joins one table.
**Rejected:** `Card.SourceLandingOperationId` (one-to-one; breaks O2 reuse); description-key
parsing as the machine link (a human edit to the description would make debt vanish from the
glance); a `PostLandVerifications` table (CARD-0478 rejected a new verification table, and the
FK column is strictly smaller).

### D-4. The Mutation `ready` row comes from the publication, keyed on the companion

`AgentTaskPipelineStatusService.GetAsync` loads, alongside `boundStages`:

- `landings`: `AgentTaskLandings.AsNoTracking().Where(o => o.VerificationCardId != null && (o.Publication == Landed || o.Publication == AlreadyPresent) && o.RemoteConfirmedAt != null)`
  as full entities, then `HasPublication` in memory (bounded: one row per landed card).
- `sourced`: `AgentTasks.AsNoTracking().Where(t => t.SourceLandingOperationId != null)` projected
  to `(Id, SourceLandingOperationId, CardId, Role, Status, CreatedAt, DispatchedAt, CompletedAt)`.
- the companion cards, added to the existing `cards` dictionary load.

`BuildMutationReady(landings, sourced, boundStages, cards, stagePins, cardPins)` emits at most
one row per companion card:

1. Skip a companion that is archived or `Done/Canceled/NeedsDecision` (the same rule as `:224`).
2. Source = the newest confirmed operation linked to that card (`RemoteConfirmedAt`, then `Id`).
3. Consumed (no row) when any task with `SourceLandingOperationId == source.Id` is
   Queued/Dispatched/Working/Blocked or Succeeded, or when any Mutation-role task bound to the
   companion is open (an unsourced dispatch must not be doubled while it runs).
4. Row: `Card` = companion; `SourcePlanTaskId` = `source.TaskId` (the landing owner);
   `ReadySince` = `source.RemoteConfirmedAt`; `DeliverablePath`/`DeliverableRef` = the newest
   verified Plan deliverable on the ORIGINAL card from `boundStages` (so the row opens the plan
   with the PC table), else `""`/null (the row then opens the owner's task drawer);
   `SourceRole` = the owner's role; `Handoff` = the newest Succeeded Review task's `NextHandoff`
   on the original card when its `NextStage` is `Land`, else null; `RoutingPin` =
   `EffectivePin(companion.Id, Mutation, ...)`; plus three new defaulted members on
   `AgentTaskPipelineReadyDto`: `Guid? SourceLandingOperationId`, `string? SourceLandingSha` (L),
   `AgentTaskPipelineCardRefDto? OriginalCard`.

The result merges into the existing dictionary under `AgentTaskRole.Mutation`, deduplicated by
card id (a landing-sourced row wins over a `next: mutation` row on the same card), and sorted by
the existing `ReadySince` then identifier order. The client needs only the three optional fields
on `AgentTaskPipelineReadyDto` in `client/src/api/agentTasks.ts`; `pipelineStageModel.ts` renders
the row through the generic ready path, and the Mutation stage becomes visible because it has
rows (`visibleStages`).

**Why the companion is the row's card:** it is the card the dispatch binds (`-Card <companion>`),
it is Backlog until that dispatch, and `CardWorkTransitionService` then moves it, so the row's
life cycle matches every other stage's without lifting the Done exclusion. **Rejected:** a
Done-original row with a special-cased exclusion (the brief's smaller-sounding option; it would
show a card the orchestrator must NOT bind the task to, and it has no natural consumption once the
companion exists); an attention row (CARD-0478: not a work queue; the glance already is one);
a `next=mutation` handoff synthesized on the Code task (rewrites a settled report).

### D-5. Consumption follows the CARD-0478 verdict table, not `RoleConsumesReadiness`

`RoleConsumesReadiness` treats any dispatched later attempt, including Failed, as consuming (a
failed Code retry shows in attention, not in `ready`). For Mutation the rule is by operation and
only open-or-Succeeded consumes: CARD-0478 D-4 says an incomplete battery "is never clean" and
D-5 says a terminal attempt may be retried explicitly after its evidence is assessed. So a Failed
or Canceled sourced attempt lets the row return; the orchestrator sees the debt again and must
read the prior attempt (and run `-CleanupVerification` where residue exists) before dispatching
a new same-O task, which admission serialises (`RequireUniqueOpenAsync`). A Succeeded attempt
with findings (`next: decide`) consumes the row: the battery ran; the open companion in Review
and the caller's triage own what follows. **Rejected:** reusing `RoleConsumesReadiness` verbatim
(hides an interrupted battery behind the 24 h `RecentFailure` window and then the stale-card
backstop, which is the invisibility this card exists to end).

### D-6. The land hook and the card-transition sweep move no card, spawn no session, dispatch nothing

The hook writes cards and revisions inside the land transaction and nothing else. `ScanAsync`
continues to move the companion Backlog -> InProgress on the Mutation dispatch and -> Review on
its Succeeded settle (`C478_V07`, `G124`-`G128` unchanged). The original card is never touched
except for the one-line description append in D-1; its status, `CompletedAt` and terminal reason
are not written. `G-133 NoTickSpend` is extended: the card-transition sweep over a board holding
a server-created companion still creates no task and no session. The ONLY creator of a sourced
Mutation task besides an explicit caller is D-9's sweep, and both go through the same
`CreateAsync` admission (same-O serialisation makes a double impossible). An orchestrator may
still dispatch explicitly from the `ready` row, for example to jump the oldest-first order; the
row's `SourceLandingOperationId` and `Card.Id` are the two arguments.

### D-7. One idempotent endpoint serves backfill and recovery

`POST /api/agent-tasks/{id}/verification-companion` (no body; `{id}` resolves like every other
agent-task route). Behaviour: load the task's newest landing with `HasPublication`
(`ActiveLandingId` first, else newest by `RemoteConfirmedAt`); 409 `verification_publication_unconfirmed`
when none; 409 `verification_publication_forbidden` for a Mutation-role or sourced task; else
`EnsureAsync` under a transaction and the owner's row lock, publish `CardChanged`, return
`{ taskId, operationId, cardId, identifier, created, linked }` (200 whether created or already
linked). No script switch: the backfill is eleven HTTP calls, listed in the Backlog section;
`docs/ops-http.md` documents the route. **Rejected:** a boot-time or scheduled backfill sweep
(automatic card creation outside an explicit action; also the tick shape G-133 forbids);
a `delegate.ps1`/`card.ps1` switch (adds script tests for an operation run once per landed card).

### D-8. Active recipes say the land records the companion; contract strings are preserved

`docs/orchestration-loop.md` (CARD-0478 section, the cycle diagram line
`record companion -> -Land ...`, and `:723-724`), `.claude/skills/antiphon-delegate/SKILL.md`
(`:99`, `:380-388`, `:398`, `:405`), `server/Bundles/orchestrator.md` and `server/Bundles/README.md`
(`Default workflow: Code -> ordinary Review -> caller records same-board companion -> land`),
`docs/ops-http.md` and `docs/agent-card-lifecycle.md` (Post-land verification sections),
`docs/antiphon-api.md` (route map). The wording becomes: the confirmed publication creates or
links the companion and names it in the land outcome; a caller MAY still pre-create one (the
stable key is discovered and linked); the caller records the pending PC inventory and closes the
original with `post-land Mutation pending: <companion>`; Mutation is dispatched from the glance's
Mutation `ready` row. Every substring `PostLandMutationContractTests.C478_V11` asserts stays
present (`post-land-verification:<original-code-task-guid>`, `publication pending`,
`preserve existing`, `HasPublication`, `L=VerifiedSourceSha`, `-SourceLanding <operation-guid>`,
`-CleanupVerification <mutation-task-id>`, `same commissioning project`, `including Blocked`,
`never PC-clean`, `Failed/Canceled`, `external executor`, `NeedsDecision`, `O2/L2`,
`No force or recursive deletion`, `health alone is insufficient`) and every obsolete one stays
absent. Bundles stay ASCII and inside their character budget (`InstructionBundleTests`).
The sentence "no tick creates cards or spends quota" in `orchestration-loop.md`, `SKILL.md`,
`orchestrator.md` and `README.md` becomes "no tick creates cards; the Mutation sweep is the one
tick that spends, under the D-12 ceilings" (the refinement's amendment, stated where the old rule
lived so nobody re-derives the old policy from the docs).

### D-9. `MutationAutoDispatchSweep`: one role, one row per tick, every existing gate

`server/Infrastructure/Orchestration/MutationAutoDispatchHostedService.cs` (the
`DiagnoseSweepHostedService` shape: `PeriodicTimer`, one scope per tick, exceptions logged) drives
`server/Application/Services/MutationAutoDispatchSweep.TickAsync`, which is testable without the
host. A tick, in order, and each `return 0` is logged at Debug with its reason:

1. `Delegation:MutationAutoDispatch:Enabled` false -> nothing (test hosts set it false exactly as
   `DiagnoseEnabled`, in `AntiphonWebAppFactory` and `ProductionRunnerGuard`).
2. `OrchestratorControlState.IsPaused` -> nothing (the operator's global stop already exists).
3. Any Mutation-role task Queued/Dispatched/Working/Blocked in the commissioning project -> nothing
   (WIP=1; the create gate would refuse anyway, this avoids the 409 and the log noise).
4. `ActiveWindow` configured and the local time is outside it -> nothing (D-12; off by default).
5. UTC-day sum of `CostUsd` over Mutation-role tasks `>= DailyBudgetUsd` -> nothing; the tick
   records the fact once per day as an `AgentTaskEvent`-free log line (no ledger table is added;
   the attention feed already shows the queue as `ready` rows).
6. The Mutation route is held: resolve the stage/card pin for the candidate as `RoutingPinService`
   would and ask `IModelAvailability.IsHeldAsync(kind, alias)`; held -> nothing (the
   `ModelAvailabilityHold` attention row already says why).
7. Candidate = the first row of `MutationDebtProjection.Build(...)` (D-4's projection, lifted into
   a shared static so the glance and the sweep cannot disagree) whose operation has NO sourced
   task in any status. A Failed/Canceled/Blocked prior attempt makes the debt visible in the glance
   but never auto-retried: CARD-0478 D-5 requires an evidence/restoration assessment before a new
   same-O task, and that is an orchestrator's or human's explicit act (`-CleanupVerification`, then
   `-SourceLanding` again).
8. `AgentTaskService.CreateAsync(request, new Caller(null, null, op.RepositoryPath, ProjectId: owner.ProjectId, BoardId: companion.BoardId))`
   with `Kind Worker, Role Mutation, Workspace Worktree, Card = companion guid,
   SourceLandingOperationId = O, Title = "post-land mutation checks: <original identifier>",
   Goal = D-10, ExpectedMinutes = D-12`. No `IgnoreSubscriptionQuota`, `IgnoreConcurrencyLimit`,
   `IgnoreRoutingPin`, `IgnoreModelDisabled` or `RefuseIfExhausted` overrides: a
   `subscription_quota_low`, `provider_sign_in_required`, capacity or `verification_source_already_open`
   409 ends the tick at Information level and the row stays `ready` for the next tick or a human.
9. On acceptance: one content revision on the companion, `Auto-dispatched Mutation <task:D> for O=<op:D> at <now:O>`
   (reason `mutation-auto-dispatch`, editor `mutation-sweep`), so the card thread says who started
   the battery; the task binding itself is the durable record.

One create per tick, oldest `ReadySince` first; the next tick finds the row consumed. Nothing
here retries, reroutes, escalates, moves a card, closes a card, creates a card or spawns a session.
**Why a hosted sweep and not a Windmill job:** PCs must run as local inherited SourceLanding
children under runner custody (`stage-mutation.md`), so any scheduler ends at `POST /api/agent-tasks`
anyway; the server already holds every gate the decision needs (pause, WIP, pins, holds, quota,
spend) and the Diagnose sweep is the shipped precedent for exactly this shape. **Why not a
`Schedule` row (CARD-0057):** its card arm moves/releases/spawns a card session, which CARD-0478
forbids for a companion, and its prompt arm needs a standing agent to do the dispatching.
**Rejected:** a general "dispatch any ready row" engine (CARD-0478; the operator's steer is
explicit that this is trigger policy for Mutation only); auto-retry of failed batteries (D-5
above); a per-battery approval gate (the operator's steer).

### D-10. The server composes the Mutation brief from the companion

`MutationAutoDispatchSweep.ComposeGoal(original, companion, owner, review, plan, op)` returns an
ASCII goal under 20,000 characters. It carries identity, not method (the `stage-mutation` bundle
and `docs/orchestration-loop.md` carry the method): both card identifiers and GUIDs, Code and
Review task GUIDs, C/O/L/R, the plan path, `HEAD must equal L=<sha>`, "enumerate every PC-n and
named variant from the plan's Verification design; discover missing controls; report per
stage-mutation.md; close with the counts and the evidence root the completion note names",
and the companion revision instruction ("record executed counts, evidence root and restoration
verdict on <companion> at close is the caller's; you only report"). The same text is what an
orchestrator's file-backed brief would say; non-Claude providers spill it to a file as they do
today. **Rejected:** copying reports or the plan into the goal (the worktree at L has the plan;
the card thread has the reports).

### D-11. A settled sourced battery with no caller surfaces on the attention feed

`AttentionKind.MutationDispositionPending = 42` (appended; client union and visuals updated;
severity Warning when the task's `NextStage` is `Decide`, Info when `None`). Read-time projection
in `AttentionService`: a Succeeded task with `SourceLandingOperationId != null` whose companion
card is not archived/Done/Canceled and has no newer Mutation-role task. Detail names the original
and companion identifiers, O, L, the task short id and the verdict (`clean` / `findings`), and
the row clears when the companion is closed, canceled, or a newer battery is bound. Failed and
Canceled attempts are already `RecentFailure`/`FailureUnacknowledged`. This is the CARD-0478 D-7
triage continuation delivered to whoever works the board when no orchestrator session ordered the
battery; the companion card stays the record, so this is not the attention-only pending item
CARD-0478 rejected. No automatic close: `C478_G126_SuccessfulTaskDoesNotCloseVerification` stands;
a clean battery leaves the companion in Review with the report on the task until a human or
orchestrator closes it with the counts. **Rejected:** auto-Done on `next: none` (G-126, "no implicit
clean"); routing the report to a configured standing agent (`ReplyTo` is derived from the caller
session and a standing recipient would need its own delivery design; the feed already reaches the
digest channels).

### D-12. Throttles are ceilings, not approvals

`DelegationSettings.MutationAutoDispatch` (`Delegation:MutationAutoDispatch:*`):

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | The refinement's default: debt is worked without a per-battery trigger. |
| `SweepMinutes` | `5` | Tick interval; a battery takes hours, so a short tick costs nothing. |
| `DailyBudgetUsd` | `75` | UTC-day ceiling on Mutation-role `CostUsd`; gates STARTS only, a running battery finishes. |
| `ExpectedMinutes` | `720` | `ExpectedMinutes` on the created task so a 200-PC battery is not failed at the 240-minute default deadline; stall detection (CARD-0153) still watches progress. |
| `ActiveWindow` / `TimeZoneId` | `null` | Optional `HH:mm-HH:mm` local window. Off: the operator asked for budget-only throttling. The one reason to set it is CPU contention with the 01:00 London nightly run (`docs/nightly-watchdog.md`); observe first. |

Natural pacing already present and therefore not re-implemented: WIP=1 at create (per project),
about 3 minutes and $0.50 per PC at opus tier (investigation §4), the subscription-quota gate's
two rules, the routing pin's `NotBefore`, and `ModelAvailability` holds. With the defaults the
current backlog clears at roughly one battery a day (a 218-PC battery costs about $109 and starts
only when the day's spend is under $75, then runs to completion), so about a week of unattended
nights for the 914 PCs. No new explicit-approval gate is added anywhere.

## Out of scope (explicit)

- A new `CardStatus`, board column or task role for Mutation debt: rejected by CARD-0478 D-3 and
  not needed; the companion card and the Mutation stage's `ready` list are the visible queue.
- A memory-only promise, an attention-only pending record, a verification results table, or a
  general auto-dispatch engine: rejected by CARD-0478. D-9 is one role's budget-capped creator
  (the operator's refinement), not an engine: it never retries, reroutes, escalates, moves or
  creates cards, and every other tick still spends nothing (D-6, G-133).
- A structured pending-PC count or a plan/close-reason parser: CARD-0478 excluded a PC parser;
  the companion quotes the handoff lines verbatim and the Mutation delegate derives the inventory
  from the plan as its bundle already requires.
- A `label` filter on `GET /api/cards`: the `ready` row carries card GUID, operation GUID and L,
  which is what a dispatcher needs; the label stays a board-UI affordance.
- Legacy admission for manual-merge landings (CARD-0514; also CARD-0499): no confirmed operation
  exists, admission and D-7 refuse by design. Running those PCs under custody needs either a
  new reviewed landing of an equivalent change or a separately scoped "legacy publication"
  admission card. A caller may still run them as a pre-CARD-0478 `Custom` battery on a hand-made
  companion, recorded as not SourceLanding-admitted.
- Changing Mutation's WIP, pins or provider routing; client rendering beyond type parity and the
  new attention kind's label; automatic retry of a failed battery; automatic close of a clean one.
- A Windmill/nightly-side scheduler for Mutation (the server sweep is the scheduler; the nightly
  job is untouched).
- Reopening, closing or moving the original card; changing what `-Land` verifies or publishes.

## The current backlog (12 cards, ~914 PCs)

Nothing in this card runs a PC. After S1-S4 land and the server restarts, the backfill is:

```powershell
foreach ($task in @('<owner-task-guid of CARD-0503>', '...CARD-0461', '...CARD-0443', '...CARD-0501',
                    '...CARD-0481', '...CARD-0549', '...CARD-0462', '...CARD-0544', '...CARD-0540',
                    '...CARD-0545', '...CARD-0547')) {
  Invoke-RestMethod -Method Post "http://localhost:17202/api/agent-tasks/$task/verification-companion"
}
```

The owner task of each card is the task whose `-Land` produced the confirmed operation (the
`operation=` line in each land outcome; also `GET /api/agent-tasks/{id}` `landing.publication`).
Expected result: eleven companions created in Backlog, eleven Mutation `ready` rows ordered
oldest-first (CARD-0503, 2026-09-12, first), each naming its operation and L. The call is also the
audit: a 409 `verification_publication_unconfirmed` proves that card has no confirmed operation.
CARD-0514 (96 PCs) is expected to 409 and is the operator's decision (Out of scope, item 5). The
ambiguous pair (CARD-0502, CARD-0499) is untouched by this plan.

Cost of the backfill: about five minutes of HTTP. Cost of paying the debt afterwards is unchanged
from the investigation (roughly 45 h / $450 at WIP 1). Under the refinement the sweep works it
unattended from the moment the companions exist: oldest first (CARD-0503's 4 PCs, then
CARD-0461's 117, CARD-0443's 218, ...), one battery at a time, starting a new one only while the
UTC day's Mutation spend is under $75, so about a week of nights at the defaults. The operator
steers with `Delegation:MutationAutoDispatch:*`, the Mutation routing pin, the orchestrator pause,
or by canceling a companion; a battery's findings and clean verdicts arrive on the attention feed
(D-11) and the companion sits in Review until closed with the counts.

Cards landing after deployment need no backfill: the hook fires on their publication and the
sweep picks the row up on its next tick.

## Implementation slices

| Slice | Files and behaviour | Required test surfaces |
|---|---|---|
| S1 Schema and writer | `server/Domain/Entities/AgentTaskLanding.cs` (+`VerificationCardId`); `server/Infrastructure/Data/AppDbContext.cs` (FK Restrict + index); CLI migration `AddLandingVerificationCard` + snapshot; new `server/Application/Services/PostLandVerificationCompanions.cs`; `Program.cs` registration; `server/Application/Dtos/LandingEvidenceDto.cs` (+`VerificationCardId`). | Unit: `Describe`/`StableKey`/`Title` shape, ASCII, length cap. Integration (isolated schema): `EnsureAsync` create / link-by-owner / link-by-key / closed-companion-supersede / idempotent-on-linked; FK Restrict; migration chain applies (`AgentTaskLandingPersistenceTests` precedent). |
| S2 Land hook | `AgentTaskLandService.cs`: constructor dependency; hook in `CompleteTerminalLockedAsync`; `companion=` outcome marker; `CardChanged` publishes after commit. Update the 9 direct constructor sites in tests (`LandingSafetyHarness.cs:189` among them). | Real-git `Landed` (`AgentTaskLandStageOutcomeTests.CreateLand` pattern): companion exists, labelled, keyed, linked, revision on companion, one-line append on original, `Detail` contains `companion=CARD-`; `AlreadyPresent` same; `LandRefused` none; cleanup retry (C448_V33) no second card/revision; interrupted-after-publication (`LandingSafetyHarness.Fault` after publication, then `FailAsync`) still records; save fault before commit leaves neither terminal event nor card (atomicity, `C488_ApprovalOutcomeTransactionAtomic` precedent); no `AgentTask`/session created (G-133 shape). |
| S3 Projection | `AgentTaskPipelineStatusService.cs` (`BuildMutationReady`, loads, merge/dedupe); `AgentTaskPipelineDtos.cs` (+3 defaulted members); `client/src/api/agentTasks.ts` type parity. | `AgentTaskPipelineStatusTests` (C448_V33 landing seed + `SeedCardAsync`/`SeedTaskAsync`): row present with all fields; unconfirmed publication none; companion Done/Canceled/NeedsDecision/archived none; sourced open/Succeeded consumes; sourced Failed/Canceled returns the row; unsourced open Mutation on companion consumes; two operations pick the newest; legacy `next: mutation` row on the original coexists (`MutationPipelineTests` C470 untouched); card pin outranks stage pin; order by `ReadySince`; route JSON carries the new members (`pipeline_json_includes_agent_kind_on_a_row` precedent). Client: `pipelineStageModel` tests unchanged, `scripts/test-client.ps1` green. |
| S4 Endpoint and docs | `server/Api/Endpoints/AgentTaskEndpoints.cs` route; service method (land service or `AgentTaskService`) with lock + transaction; `docs/antiphon-api.md`, `docs/ops-http.md`, `docs/agent-card-lifecycle.md`, `docs/orchestration-loop.md`, `.claude/skills/antiphon-delegate/SKILL.md`, `server/Bundles/orchestrator.md`, `server/Bundles/README.md` per D-8. | `AgentTaskLandContractEndpointTests` precedent: 200 created, 200 already linked, 409 unconfirmed, 409 Mutation/sourced task, 404 unknown; `PostLandMutationContractTests.C478_V11` and `InstructionBundleTests` stay green; a new contract assertion that the active recipe says the publication records the companion and Mutation dispatches from the `ready` row. |
| S5 Backfill (operator, after deploy) | The eleven calls above; confirm `GET /api/agent-tasks/pipeline` shows the Mutation rows; note the CARD-0514 refusal on that card; watch the first auto-dispatch land on CARD-0503's companion. | Live readback only; recorded on CARD-0552's close. |
| S6 Unattended sweep (D-9, D-10, D-12) | `DelegationSettings.MutationAutoDispatch` nested settings; `server/Application/Services/MutationDebtProjection.cs` (D-4's rule as a static shared by S3 and the sweep); `server/Application/Services/MutationAutoDispatchSweep.cs` (`TickAsync`, `ComposeGoal`, `DailySpendUsdAsync`); `server/Infrastructure/Orchestration/MutationAutoDispatchHostedService.cs`; `Program.cs` registration beside `DiagnoseSweepHostedService`; `AntiphonWebAppFactory` and `ProductionRunnerGuard` off switches; `server/appsettings*.json` if the deploy profile pins values. | `CardDiagnosisSweep` test precedent (isolated schema, fake availability, `FakeTimeProvider`): gate matrix (disabled, paused, open Mutation, window, budget met, held route, no untouched candidate, oldest-first pick); one create per tick; created task shape (role, workspace, card, O, no `Ignore*`, `ExpectedMinutes`); the create 409s end the tick and keep the row; Failed/Canceled prior attempt is never auto-retried; companion revision written; a test host booting real `Program` creates nothing (`ProductionRunnerGuard`). Goal composer unit tests (ASCII, length, identities present). |
| S7 Attention surface (D-11) | `AttentionDtos.cs` (`MutationDispositionPending = 42`); `AttentionService` projection; `client/src/api/attention.ts` union; `attentionVisuals.ts` label/severity. | `AttentionServiceTests` precedent: row for Succeeded sourced task with open companion (Decide -> Warning, None -> Info); none when companion terminal/archived or a newer Mutation task is bound; Failed attempt is not this kind; client union test. |

S1 -> S2 -> S3 are sequential (S2 needs the column, S3 needs the link); S4 can follow S2; S6
needs S3's projection; S7 is independent after S1. One Code dispatch in one Worktree is expected,
possibly two if S6/S7 are split off; the diff is server + a migration + docs + two client types.
Complexity: medium-hard (a migration, a locked-transaction hook, a read-model change, a hosted
sweep with real create admission, one attention kind).

## TestDesign handoff

Acceptance obligations, not the executable design. TestDesign inspects the named fixtures and
appends `## Verification design` with V/R ids, exact assertion methods and every compiling PC.

1. A real-git confirmed publication (Landed and AlreadyPresent) creates exactly one companion on
   the same board, in Backlog, titled and labelled per CARD-0478, keyed by the owner task guid,
   linked from the operation, with one content revision on the companion and one on the original,
   and names the companion in the terminal `Detail` and the Outcome notification body. A refused
   or unconfirmed land creates nothing. Prove atomicity against the terminal event with the
   existing save-fault seams; prove the interrupted-after-publication path.
2. Cleanup retries, `LandingCleanup` terminals and repeated endpoint calls never create a second
   card or revision. A second confirmed operation of the same owner links to the same open
   companion and appends O2/L2; a closed companion is superseded by a new one that names it.
   A caller-created companion (stable key in the description, `publication pending`) is linked
   and appended to, never replaced.
3. The Mutation stage of `GET /api/agent-tasks/pipeline` shows one row per open companion with a
   confirmed publication and no open/Succeeded sourced task, carrying companion card, owner task,
   `ReadySince = RemoteConfirmedAt`, plan deliverable when one exists, Review handoff when one
   exists, effective pin, operation id and L. Open or Succeeded sourced tasks and open unsourced
   Mutation tasks on the companion consume it; Failed/Canceled sourced tasks do not; terminal or
   archived companions produce none; the original card's Done status is unchanged and produces no
   row under any stage. Legacy `next: mutation` rows still project.
4. No path in this card creates an `AgentTask` or session, moves any card, or writes the original
   card's status/terminal reason: extend `C478_G133_NoTickSpend` and `G124`/`G125` shapes with a
   server-created companion present.
5. Endpoint contract: resolution forms, 200 created/linked payload, 409 codes, 404; `LandingEvidenceDto`
   exposes `verificationCardId`; client type parity build.
6. Docs contract: every `C478_V11` substring remains; the obsolete list remains absent; bundles
   remain ASCII within budget; a new assertion pins the server-created companion wording and the
   "the Mutation sweep is the one tick that spends" wording in both active recipes.
7. Sweep gates (D-9): each of disabled, paused, open Mutation task, outside window, budget met,
   held route and no untouched candidate produces zero creates; an eligible tick creates exactly
   one task with the required shape through the REAL `AgentTaskService.CreateAsync` (isolated
   schema, fake runner, no provider); the oldest `ReadySince` wins; a quota/capacity/already-open
   409 from create ends the tick with the row still `ready`; a prior Failed, Canceled or Blocked
   sourced attempt is never auto-retried; the companion gets the auto-dispatch revision; the
   glance and the sweep read one projection (mutating the shared rule changes both).
8. Brief (D-10): ASCII, under the goal cap, names both cards and GUIDs, Code/Review task GUIDs,
   C/O/L/R, the plan path and `HEAD must equal L`; carries no method text that would drift from
   `stage-mutation.md`.
9. Attention (D-11): the row appears for a Succeeded sourced task on an open companion with the
   right severity, and clears on companion close/cancel/archive or a newer bound Mutation task;
   never for Failed/Canceled attempts; client union compiles.

Positive controls to name: removing the `HasPublication` guard (companion on a refused land),
removing the `VerificationCardId != null` early return (duplicate cards on cleanup retry), the
Succeeded-consumes clause, the Failed-does-not-consume clause, the companion-status skip, the
newest-operation choice, the dedupe against a legacy row, the outcome marker, the reverse-link
append, the FK Restrict, each 409 branch; and for the sweep: each gate in D-9's order (a removed
gate creates when it must not), the one-per-tick cap, the attempt-once rule, the oldest-first
order, the `Ignore*` flags left false, `ExpectedMinutes` applied, the projection sharing, and the
D-11 clear conditions. Every PC is method-scoped per `docs/testing-and-build.md` and runs post-land
under SourceLanding Mutation on this card's own companion, which this very change will create and
this very sweep will pick up.

Cost to name separately: Code authoring plus ordinary V/R (migration + hook + projection + sweep +
attention + docs); ordinary Review; post-land PCs for this card; the operator's five-minute
backfill.
