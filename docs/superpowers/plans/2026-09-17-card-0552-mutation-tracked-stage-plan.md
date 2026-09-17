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

## Verification design

TestDesign task `d1ab684b`, 2026-09-17, against plan commit `7fc575c9`. The fix design above is
unchanged; this section names the tests Code implements, the guards Mutation breaks, and the
defaults TestDesign had to fix so the tests are unambiguous. Test method names carry the class
prefix (`C552_L` land hook, `C552_W` writer, `C552_U` writer unit, `C552_P` projection unit,
`C552_Q` projection integration, `C552_E` endpoint, `C552_D` docs contract, `C552_S` sweep,
`C552_B` brief, `C552_C` settings, `C552_H` host, `C552_A` attention). Guard `G-552-n` maps 1:1 to
`PC-552-n`.

### Defaults fixed by TestDesign (the plan left them open; Code implements exactly these)

1. **Card-less landing owner.** Every `LandingSafetyHarness` land is a task with `CardId == null`
   (the harness seeds no board). The hook fires only when `task.CardId is not null`; the plan's
   condition becomes `op is not null && task.CardId is not null && HasPublication(op)`.
   `PostLandVerificationCompanions.EnsureAsync` throws `InvalidOperationException("post_land_companion_requires_card")`
   for an unbound owner (defensive; the hook never reaches it). The D-7 endpoint maps that case to
   409 `verification_companion_requires_card` (a third code beside the two the plan names).
2. **Sweep WIP gate is fleet-wide.** D-9 step 3 says "in the commissioning project"; Disposition
   item 5 and the WIP=1 ground-truth row say "any Mutation-role task is open". The tests pin the
   fleet-wide reading (one battery at a time anywhere, non-specialist Mutation role); the
   create-time role cap stays per project as shipped. An open Mutation task in another project
   also skips the tick.
3. **`TickAsync` returns a reason, not an int.** `MutationAutoDispatchSweep.TickAsync` returns
   `MutationAutoDispatchTick(int Created, string Reason, Guid? TaskId = null, Guid? OperationId = null, Guid? CompanionCardId = null)`
   with constants on the sweep: `ReasonDisabled = "disabled"`, `ReasonPaused = "paused"`,
   `ReasonMutationOpen = "mutation-open"`, `ReasonOutsideWindow = "outside-window"`,
   `ReasonBudgetMet = "budget-met"`, `ReasonRouteHeld = "route-held"`, `ReasonNoCandidate = "no-candidate"`,
   `ReasonCreated = "created"`, `ReasonRefusedPrefix = "create-refused:"` (suffix = `HttpException.Code`).
   Without this, "gate 3 skipped" and "create 409'd" are both `0` and no gate test is decisive.
4. **`ActiveWindow` semantics.** `HH:mm-HH:mm`, start inclusive, end exclusive, evaluated on the
   injected clock converted to `TimeZoneId` (`TimeZoneInfo.FindSystemTimeZoneById`); start > end
   wraps midnight. `TimeZoneId` null with a window set means UTC.
5. **Shared projection API** (`server/Application/Services/MutationDebtProjection.cs`, `internal static class`):
   ```csharp
   internal sealed record LandingRow(Guid Id, Guid TaskId, Guid VerificationCardId, DateTime RemoteConfirmedAt,
       string OriginalSourceSha, string VerifiedSourceSha, string ObservedRemoteTargetSha, LandPublicationOutcome Publication);
   internal sealed record SourcedRow(Guid Id, Guid SourceLandingOperationId, Guid? CardId, AgentTaskRole Role,
       AgentTaskStatus Status, DateTime CreatedAt, DateTime? DispatchedAt, DateTime? CompletedAt);
   internal sealed record CardRow(Guid Id, string Identifier, string Title, CardStatus Status, DateTime? ArchivedAt);
   internal sealed record Debt(CardRow Companion, LandingRow Source, bool AnyAttempt);
   internal static IReadOnlyList<Debt> Build(IReadOnlyList<LandingRow> landings, IReadOnlyList<SourcedRow> sourced,
       IReadOnlyList<AgentTaskPipelineStatusService.TaskRow> boundStages, IReadOnlyDictionary<Guid, CardRow> cards);
   ```
   `Build` applies D-4 rules 1-3 and orders by `Source.RemoteConfirmedAt` then `Companion.Identifier`
   (`StringComparer.OrdinalIgnoreCase`). `AnyAttempt` is true when any `SourcedRow` in any status
   names `Source.Id`. The glance maps `Debt` to `AgentTaskPipelineReadyDto` (adding plan deliverable,
   Review handoff, pin, `OriginalCard`); the sweep takes `Build(...).FirstOrDefault(d => !d.AnyAttempt)`.
   The caller loads landings with `HasPublication` already applied in memory.
6. **Writer result.** `Result(Guid CardId, string Identifier, bool Created, bool Linked)`: `Created`
   = a new card was made by this call; `Linked` = `op.VerificationCardId` was set by this call.
   Already-linked returns `(id, identifier, false, false)` and writes nothing.
7. **Superseded companion line.** A new card created because the linked companion is
   Done/Canceled/archived carries the line `Supersedes: <identifier> (<guid:D>)` after the stable key.
8. **Sweep constructor and title.** `MutationAutoDispatchSweep(AppDbContext db, AgentTaskService tasks, IOptions<DelegationSettings> settings, TimeProvider clock, OrchestratorControlState control, ILogger<MutationAutoDispatchSweep> logger, IModelAvailability? availability = null, RoutingPinService? routingPins = null)`.
   Task title `post-land mutation checks: <original identifier>`; auto-dispatch revision reason
   `mutation-auto-dispatch`, editor `mutation-sweep`, appended description line
   `Auto-dispatched Mutation <task:D> for O=<op:D> at <now:O>`.
9. **Docs wording pinned by the contract tests** (Code writes these sentences verbatim):
   - `docs/orchestration-loop.md` and `.claude/skills/antiphon-delegate/SKILL.md`: the sentence
     `no tick creates cards or spends quota.` becomes `no tick creates cards; the Mutation sweep is the one tick that spends, under the D-12 ceilings (Delegation:MutationAutoDispatch).`
     and the CARD-0478 recipe gains `The confirmed publication creates or links the companion and names it in the land outcome; Mutation is dispatched from the glance's Mutation ready row, explicitly or by the Mutation sweep.`
   - `server/Bundles/orchestrator.md` and `server/Bundles/README.md`: `Default workflow: Code -> ordinary Review -> land the original Code task -> the confirmed publication creates or links the companion -> required deployment -> SourceLanding Mutation on the companion; the Mutation sweep is the one tick that spends.`
   - `docs/antiphon-api.md` route map line starts `POST   /api/agent-tasks/{id}/verification-companion`.

### Inspection

Bodies read (all on `7fc575c9`):

- `tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs` (whole): `SeedAsync` binds no card; `CreateLand` constructs `AgentTaskLandService` positionally (new `PostLandVerificationCompanions` argument goes after `Logger`, before `protocol`); `SaveFault.TerminalCut` `before-save`/`after-save` arm on an Added `IsLandTerminal` event; `AfterAcknowledged(phase)` can throw at `LandPhase.PublicationConfirmed`; `Events` is a settable `MockEventBus`; no `LandDeliveryBoundary` is passed today. | boundaries -> V-552-3 (card-less), V-552-5 (cuts), V-552-4 (interrupted), missing setup M-1..M-3.
- `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs` (whole): `C448_V33` cleanup-retry recipe (`RepostAsync` then `RunAsync`, sentinel under `bin-private`), `C448_V09_PushExitCannotReplaceRemoteConfirmation(false)` gives an op with `RemoteConfirmedAt == null`, `C448_V01` gives `LandRefused` before any op. | -> V-552-2, V-552-6.
- `tests/Antiphon.Tests/Application/AgentTaskLandNotificationPersistenceTests.cs:20-60`: `C488_ApprovalOutcomeTransactionAtomic` / `C508_AfterSaveRollsBackOutcome` cut shapes and the `Body.ShouldContain` precedent (`:195-198`). | -> V-552-5, V-552-1d.
- `tests/Antiphon.Tests/Application/AgentTaskLandFailureDiagnosticTests.cs:101-135`: interrupted-after-publication recipe (`AfterAcknowledged` throws `IOException` at `PublicationConfirmed`; `LastReason == "landing_interrupted_after_publication"`). | -> V-552-4.
- `tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs` (head, `:585-728`): `CreateLand` positional construction (second of nine sites), real-git `Landed` with `Detail` markers. | -> R-552-2 (site update), no new cases.
- `tests/Antiphon.Tests/Application/PostLandMutationCustodyTests.cs:748-870` (`PostLandMutationWorld`): `LandingSafetyHarness` + `SourceLandingAdmission` + `FakeSessionRunnerClient { VerificationStoreId }`; cards seeded AFTER the land with the fixture task card-less at land time (so this world is untouched by D-1 under default 1); `TaskService(...)` wiring with `openGate` and `sourceLanding`; `Caller = new(null, null, Host.Fixture.Repository)` passes directory authorization with empty `AllowedRoots`. | -> sweep world M-6 copies this shape but seeds the board BEFORE the land.
- `tests/Antiphon.Tests/Application/PostLandMutationAdmissionTests.cs:1-140`: admission codes `verification_source_unconfirmed` and `HttpException` refusals through `world.TaskService(...).CreateAsync`. | -> V-552-60..62 refusal shapes.
- `tests/Antiphon.Tests/Application/MutationAdmissionTests.cs` (whole): `DelegationOpenGate(db, options)` wiring; `ConcurrencyLimitException.Concurrency.Axis` `role`/`absolute`; `MaxOpenTasks` cap. | -> V-552-62 (absolute cap), V-552-53 (the role cap sits behind the sweep's own gate).
- `tests/Antiphon.Tests/Application/AgentTaskAgentKindTests.cs:460-500, 590-625, 662-684`: `SubscriptionQuotaGate` construction, `SeedUsageSampleAsync(provider, key, remaining, hoursToReset)`; `SubscriptionUsageKey.For(null, kind) == kind.ToString()`. | -> V-552-61 (key `"ClaudeCode"`).
- `tests/Antiphon.Tests/Application/ModelAvailabilityCreateTests.cs:270-340`: `SeedHoldAsync` shape and `modelAvailability: new ModelAvailability(db, clock, logger)` wiring. | -> V-552-60.
- `tests/Antiphon.Tests/Application/CardDiagnosisSweepTests.cs` (whole) and `server/Application/Services/CardDiagnosisSweep.cs`, `DiagnoseSweepHostedService.cs`: `World` with `IsolatedTestSchema`, `FakeTimeProvider(2100-06-01T12:00Z)`, settings lambda, one scope per tick; the Diagnose budget/held gates are untested there (no precedent to copy for those two). | -> sweep tests use the same clock and settings-lambda shape; budget/held tests are new (V-552-56..59).
- `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs:20-80, 780-1029`: `C448_V33` inline confirmed-landing seed (passes `HasPublication` when `ConfirmationMethod == "push-endpoint-read-fetch-ancestry"`); `SeedCardAsync` makes a NEW project/board per card (so same-board companions need M-4); `pipeline_json_includes_agent_kind_on_a_row` JSON precedent. | -> V-552-20..33.
- `tests/Antiphon.Tests/Application/MutationPipelineTests.cs` (whole): legacy `next: mutation` row (`C470_*`) stays; factory-host `[NotInParallel]`. | -> R-552-6, V-552-27.
- `tests/Antiphon.Tests/Application/PostLandMutationWorkflowTests.cs` (whole) and `CardWorkTransitionServiceTests.cs:257-300, 414-535`: harness `SeedCardAsync(status)`, `SeedTaskAsync(cardId, status, dispatchedAt, completedAt, role)`, `ScanAsync`, `SessionCountForAsync`, `CreateContext()`; `C478_G133_NoTickSpend` shape. | -> V-552-39 (G-133 extended with a linked landing row).
- `tests/Antiphon.Tests/Application/PostLandMutationContractTests.cs` (whole): `C478_V11` substring sets, `RepoFile`, bundle ASCII/size pins (`C478_G179/G180`). | -> V-552-90..94, R-552-7.
- `tests/Antiphon.Tests/Application/InstructionBundleTests.cs:392-422, 585-640`: orchestrator bundle pin style; stage-bundle ASCII test does not cover `orchestrator.md`. | -> V-552-91 (new ASCII pin on the orchestrator bundle).
- `tests/Antiphon.Tests/Application/AgentTaskLandContractEndpointTests.cs` (whole) and `TestHelpers/LandContractSeeds.cs`, `LandContractWebAppFactory.cs`: `ReadCodeAsync` reads `code`; 404 via `ResolveTaskIdAsync`; `SeedSucceededWorktreeAsync(db, cardId)`. | -> V-552-40..49, missing setup M-5.
- `tests/Antiphon.Tests/Application/AttentionServiceTests.cs:1-45, 949-981, 1956-2012, 2392-2447, 2448-2500, 2607-2662, 2812-2876, 3033-3074, 3301-3325` and `AttentionServiceCommitRecoveryTests.cs:1-60`: shared-DB id-scoped `Scenario`, `Owns`, `ItemsForAsync`, `AddBoardAsync`, `AddCardOnBoardAsync` (Backlog only), `AddTaskAsync` (no `cardId`/source), dispose order (tasks before cards), `((int)item.Kind).ShouldBe(41)` precedent, `AttentionSummaryDto.From` counting. | -> V-552-83..88, missing setup M-7.
- `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs:90-130`, `ProductionRunnerGuard.cs:30-80`, `tests/Antiphon.Tests/Application/ProductionRunnerIsolationTests.cs:53`: `Delegation:DiagnoseEnabled=false` in-memory config and `Delegation__DiagnoseEnabled` env var; settings read back from the host. | -> V-552-79/80.
- `tests/Antiphon.Tests/Application/AgentTaskLandingPersistenceTests.cs` (whole): migration-chain and FK-violation assertion shapes (`PostgresErrorCodes`). | -> V-552-17/18.
- `tests/Antiphon.Tests/TestHelpers/FakeSessionRunnerClient.cs:1-80`: `VerificationStoreId` null withdraws custody support (`RequireSupportAsync` then 409 `verification_custody_unsupported_backend`). | -> V-552-64.
- `client/src/features/attention/attentionVisuals.test.ts:1-70, 118-140`, `attentionVisuals.ts:40-62`, `client/src/api/attention.ts`, `client/src/features/orchestrator/pipelineStageModel.test.ts:366-395`, `client/src/api/agentTasks.ts:434-500`: `ALL_KINDS` list, per-kind `it` shape, `rowTarget` for an empty `deliverablePath`. | -> V-552-88, V-552-93; `pipelineStageModel` tests unchanged (row rendering is generic).
- Sources: `AgentTaskLandService.cs:560-800, 884-886, 978-990` (hook site, `FormatOutcome`, `LandNotificationPayload.Create` puts `source.Detail` in the body, `PublishAsync`), `AgentTaskPipelineStatusService.cs` (whole), `SourceLandingAdmission.cs`, `AgentTaskLandingState.cs`, `AgentTaskLanding.cs`, `AppDbContext.cs:1636-1700`, `AttentionService.cs:150-258, 2646-2660`, `AttentionDtos.cs:200-300, 355-397`, `DelegationSettings.cs` (Diagnose block, `RolePolicy`, `StallDetection` nested precedent), `OrchestratorControlState.cs`, `RoutingPinService.cs:285-330`, `CardRevisionLog.cs:1-70`, `CardIdentifierAllocator.cs:27-60`, `CardService.cs:142-195`, `AgentTaskService.cs:100-140, 640-700, 1070-1140, 2395-2420`, `SubscriptionQuotaGate.cs:1-60`, `SubscriptionUsageKey.cs`, `ModelAvailability.cs:29-62`, `IModelAvailability.cs`, `LandingEvidenceDto.cs`, `AgentTaskEndpoints.cs:180-215, 267-269`, `Program.cs:335-380, 391, 515-517, 665-675`, `docs/testing-and-build.md:150-205`.

Boundary combinations covered: land publication x {Landed, AlreadyPresent, LandedWithResidue via interruption} x owner {bound, card-less}; companion resolution {linked-open, linked-terminal, linked-archived, key-same-board, key-other-board, none} x board {has Backlog, no Backlog}; projection {sourced status x 7} x {unsourced open/settled Mutation on companion} x companion status {Backlog, InProgress, Review, Done, Canceled, NeedsDecision, archived}; two operations per companion (older/newer, active/inactive); sweep gates in D-9 order with the boundary value of each (window start inclusive/end exclusive/wrap, spend `== ceiling`, spend at `00:00:00` and `23:59:59` of the previous day, non-Mutation spend, another project); refusal codes {model_disabled, subscription_quota_low, concurrency_limit(absolute), verification_custody_unsupported_backend}; attention {Decide, None} x companion {open, Done, Canceled, archived} x newer bound task {none, unsourced, sourced}. Excluded: `provider_sign_in_required` (Grok-only refusal, same catch path as the three tested codes, needs a Grok pool), `verification_source_already_open` from the sweep (unreachable: an open sourced task is an open Mutation task and gate 3 skips first; the admission race itself is `C478_ConcurrentFirstSourceAdmissionAcceptsOne`).

Missing setup (Code adds these before the tests):

- M-1 `LandingSafetyHarness.SeedOriginalCardAsync(CardStatus status = CardStatus.Done, bool backlogColumn = true)` -> `(Guid ProjectId, Guid BoardId, Guid CardId, Guid FirstColumnId)`: seeds one project, one board, columns `Backlog(0), InProgress(1), Review(2), Done(3), Canceled(4)` (Backlog omitted when `backlogColumn == false`), original card `CARD-0001` with `CompletedAt = now`, `TerminalReason = "closed by fixture"`, `ConcurrencyToken` recorded on the harness as `OriginalToken`; sets the fixture task's `CardId` and `ProjectId`. Call after `InitializeAsync`, before `RunAsync`. Exposes `BoardId`, `OriginalCardId`.
- M-2 `LandingSafetyHarness.Boundary { get; set; } = new LandDeliveryBoundary()` passed as the `boundary` argument in `CreateLand`; `CompanionAsync()` -> the single card on `BoardId` whose `LabelsJson` parses to contain `post-land-verification`, or null.
- M-3 `CreateLand` in `LandingSafetyHarness`, `AgentTaskLandStageOutcomeTests`, `AgentTaskLandApprovalRequestTests`, `AgentTaskLandRequestTests`, `AgentTaskLandSweepTests`, `DelegationWorktreeTests`, `PostLandMutationWorktreeTests`, `C544World`, `LandingProtocolHarness`: add `new PostLandVerificationCompanions(db, clock, NullLogger<PostLandVerificationCompanions>.Instance)`.
- M-4 `AgentTaskPipelineStatusTests` helpers: `SeedCardOnBoardAsync(db, boardId, status, identifier, archived = false)` (reuses an existing column of that status or adds one), `SeedConfirmedLandingAsync(db, ownerTask, companionCardId, remoteConfirmedAt, verifiedSha = new string('b', 40), confirmed = true, active = true)` (the `C448_V33` shape plus `VerificationCardId`; `confirmed == false` sets `ConfirmationMethod = "none"` so `HasPublication` is false while `RemoteConfirmedAt` is set), `SeedSourcedTaskAsync(db, dir, status, companionCardId, operationId, createdAt, completedAt = null)` (Role Mutation, Workspace Worktree, `SourceLandingOperationId`, `SourceLandingSha`).
- M-5 `LandContractSeeds.SeedBoardWithOriginalAsync(db)` -> `(BoardId, OriginalCardId, BacklogColumnId)` (Backlog + Done columns, original `CARD-0001` Done); `LandContractSeeds.SeedConfirmedLandingAsync(db, task, remoteConfirmedAt, active = true)` (same `C448_V33` shape with `RepositoryPath = task.RepoPath`, sets `task.ActiveLandingId` when `active`).
- M-6 `MutationSweepWorld` (in `MutationAutoDispatchSweepTests.cs`): `Host = new LandingSafetyHarness { Clock = new FakeTimeProvider(new DateTimeOffset(2100, 6, 1, 12, 0, 0, TimeSpan.Zero)) }`; `Runner = new FakeSessionRunnerClient { VerificationStoreId = Guid.NewGuid() }`; `ConfigureServices` adds `Runner` as `ISessionRunnerClient`, `SourceLandingAdmission`, `VerificationExecutionService`; `CreateAsync(Action<DelegationSettings>? configure = null)`: `InitializeAsync`, `SeedOriginalCardAsync()`, `AddSourceAsync()`, `RunAsync()` (Landed), reads `Operation = op.Id`, `Companion = op.VerificationCardId!.Value`, `Original`, `ProjectId`, `BoardId`. Members: `Control = new OrchestratorControlState()`, `Availability = new RecordingAvailability { Held = false }` (`IModelAvailability` recording `(kind, alias)` calls), `Settings` (defaults with `configure` applied), `TaskService(IServiceProvider, IModelAvailability? = null, SubscriptionQuotaGate? = null)` (the `PostLandMutationWorld.TaskService` wiring plus the world clock), `TickAsync(AgentTaskService? tasks = null, IModelAvailability? sweepAvailability = null)` -> `MutationAutoDispatchTick` (new scope, new sweep per call), `SeedTaskAsync(role, status, cardId = null, sourceOp = null, costUsd = 0m, createdAt = null, projectId = null)`, `SeedSecondDebtAsync(DateTime remoteConfirmedAt)` -> `(OwnerTaskId, OperationId, CompanionCardId)` (second original `CARD-0003` Done, second Succeeded Code owner bound to it with `ProjectId`, `RepoPath`/`WorkingDirectory = Host.Fixture.Repository`, synthetic confirmed op in the `C448_V33` shape with `RepositoryPath/CommonDirectory/WorktreePath/GitDirectory = Host.Fixture.Repository`, `VerifiedSourceSha = Host.Fixture.SeedSha`, `ObservedRemoteTargetSha = SeedSha`, `TargetFullRef/DestinationFullRef = Host.Fixture.TargetRef`, `SourceFullRef = "refs/heads/feat/second"`, linked to a new Backlog companion `CARD-0004`), `PipelineAsync()` -> `AgentTaskPipelineDto` via `AgentTaskPipelineStatusService` on the world db, `RevisionsAsync(cardId)`.
- M-7 `AttentionServiceTests.Scenario`: `AddTaskAsync` gains `Guid? cardId = null, Guid? sourceLandingOperationId = null, PipelineHandoffKind? nextStage = null`; `AddCardOnBoardAsync` gains `CardStatus status = CardStatus.Backlog, bool archived = false, string? identifier = null`; new `AddLandingAsync(Guid ownerTaskId, Guid companionCardId)` (confirmed `C448_V33` shape, tracked in `_landings`); `DisposeAsync` deletes `AgentTasks.Where(_tasks && SourceLandingOperationId != null)` first, then `AgentTaskLandings.Where(_landings)`, then the existing task delete, before cards.
- M-8 `ProductionRunnerGuard.MutationAutoDispatchEnvVar = "Delegation__MutationAutoDispatch__Enabled"` set to `"false"` in `PointEveryProgramBootAwayFromTheProductionRunner`; `AntiphonWebAppFactory` adds `["Delegation:MutationAutoDispatch:Enabled"] = "false"`.
- M-9 `RecordingAvailability : IModelAvailability { bool Held; List<(AgentKind Kind, string Alias)> Calls }` in `tests/Antiphon.Tests/TestHelpers/RecordingAvailability.cs` (`IsHeldAsync` records the call and returns `Held`).

### Delivery inventory

- **Land outcome -> companion card.** Producer: `CompleteTerminalLockedAsync` under the owner's `FOR UPDATE` lock. Destination: `Cards`/`CardRevisions`/`AgentTaskLandings.VerificationCardId` in the terminal transaction. Persistence boundary: the terminal commit (`terminal-committed`). Recovery: a cleanup retry or the D-7 endpoint re-runs `EnsureAsync`, idempotent on the link. Observable receipt: `op.VerificationCardId`, the `companion=` marker in the terminal `Detail` and in the Outcome notification body. Durable identity: `AgentTaskLanding.Id` -> `Card.Id`. Not asynchronous (same transaction), so no busy/eligible pair; the crash cases are V-552-5 (cut before/after save leaves neither event nor card) and V-552-4 (interrupted after publication still records). Substitute: the Outcome notification's DELIVERY to the caller session is the shipped CARD-0488 path and is not re-proven here; V-552-1d proves the marker is in the persisted body. It cannot prove the caller's UserPrompt shows the marker; the existing `AgentTaskLandNotificationPersistenceTests` and hosted-delivery tests own that path unchanged.
- **Sweep -> Queued Mutation task -> dispatcher -> session.** Producer: `MutationAutoDispatchSweep.TickAsync` through the real `AgentTaskService.CreateAsync` admission. Destination: `AgentTasks` row `(Status Queued, Role Mutation, SourceLandingOperationId = O, CardId = companion)` plus the companion revision. Persistence boundary: `CreateAsync`'s own commit (the revision is a second save after it). Recovery: a tick that dies after the create commit leaves the row; the next tick reads `mutation-open` (V-552-69); a tick refused before the commit leaves nothing and the row stays `ready` (V-552-60..62, V-552-64). Observable receipt: the task row and `Created` event; the durable identity is `AgentTask.Id` plus `SourceLandingOperationId`. Busy recipient: an open Mutation task anywhere (V-552-53) or the absolute cap (V-552-62). Already eligible: no open Mutation task (V-552-50, V-552-54). Producer-to-recipient through the real path: V-552-67 takes the sweep-created row through `DelegationWorktreeService.CreateForTaskAsync` and `VerificationExecutionService.ReserveAsync` (the same custody path `PostLandMutationWorld` provisions) and asserts a binding exists for that task and operation. Substitute declared: no session is launched and no UserPrompt is read; `PostLandMutationCustodyTests.C478_V17_RealModernHostReceiptImportsAndRemovesSnapshot` proves launch and receipt for an identically shaped row, so what V-552-67 cannot prove is that a runner delivers THIS row's brief (it proves the row is admissible to that path).
- **Attention row and backfill endpoint** are synchronous reads/writes; no delivery path. `CardChanged` fan-out to the board UI is the shipped SignalR path; V-552-1e proves the publish happens after the terminal commit through `MockEventBus` and a recording `LandDeliveryBoundary`; it cannot prove a browser repaint.

### Proves it works now

Land hook, `tests/Antiphon.Tests/Application/PostLandCompanionLandHookTests.cs` (`[Category("Integration")] [ParallelLimiter<ProcessSpawnLimit>]`, `LandingSafetyHarness` with M-1..M-3):

- V-552-1: a confirmed publication creates the companion | real git + isolated schema | `C552_L01_ConfirmedLandCreatesCompanion(bool alreadyPresent)` (`[Arguments(false)]`, `[Arguments(true)]`; `SeedOriginalCardAsync()`, `AddSourceAsync()` unless already present, `RunAsync()`) | `LandRunResult.Complete`; `h.CompanionAsync()` not null; `Cards.Count(c => c.BoardId == h.BoardId) == 2`; companion `Identifier == "CARD-0002"`, `Title == "Post-land verification: CARD-0001"`, `Status == CardStatus.Backlog`, `BoardColumnId ==` the Backlog column, `BoardService.ParseLabels(LabelsJson) == ["post-land-verification"]`, `Importance == Normal`, `ImportanceProvenance == Auto`; `Description.StartsWith("post-land-verification:" + h.Fixture.TaskId.ToString("D") + "\n")`; description contains `"Original card: CARD-0001 (" + original.ToString("D") + ")"`, `"Landing owner (Code task): " + taskId:D`, `"Reviewed C: " + op.OriginalSourceSha`, `"O: " + op.Id:D + "  L=" + op.VerifiedSourceSha + "  R=" + op.ObservedRemoteTargetSha`, `"Publication: " + (alreadyPresent ? "AlreadyPresent" : "Landed") + " confirmed at " + op.RemoteConfirmedAt:O`, `"Dispatch (explicit, WIP 1): delegate.ps1 -Role Mutation -Card " + companion.Id:D + " -Worktree -SourceLanding " + op.Id:D`; every char `< 128`; `op.VerificationCardId == companion.Id`; `new AgentTaskLandingState().HasPublication(op)`.
- V-552-1b: the companion carries the confirmation revision | same | `C552_L02_CompanionCarriesConfirmationRevision` | `CardRevisions.Where(r => r.CardId == companion.Id)` has exactly one row: `Kind == CardRevisionKind.ContentEdit`, `Reason == "Confirmed publication " + op.Id.ToString("N")`, `EditedBy == "land"`; companion `RevisionCount == 1`.
- V-552-1c: the original gets the one-line reverse link and nothing else | same | `C552_L03_OriginalGetsReverseLinkAppendOnly` | original `Description.EndsWith("\nPost-land verification: CARD-0002 (" + companion.Id:D + ")")`; original revisions: exactly one, `Kind == ContentEdit`, `Reason == "Post-land verification companion"`, `EditedBy == "land"`; original `Status == Done`, `CompletedAt` equals the seeded value, `TerminalReason == "closed by fixture"`, `BoardColumnId` unchanged, `ConcurrencyToken != h.OriginalToken`.
- V-552-1d: the marker is in the terminal detail and the outcome body | same | `C552_L04_TerminalDetailAndOutcomeBodyNameTheCompanion(bool alreadyPresent)` | the single event of type `Landed`/`AlreadyPresent` for the task has `Detail.ShouldContain("companion=CARD-0002 (" + companion.Id:D + ")")`; `AgentTaskLandNotifications.Single(n => n.TaskId == taskId && n.Kind == Outcome).Body.ShouldContain("companion=CARD-0002 (")`.
- V-552-1e: `CardChanged` is published for both cards, after the terminal commit | same, `h.Events` is a `MockEventBus`, `h.Boundary = new RecordingBoundary(h.Events)` (records `PublishedEvents.Count(e => e.EventName == "CardChanged")` when `boundary == "terminal-committed"`) | `C552_L05_CardChangedPublishedForBothCardsAfterCommit` | `boundary.CardChangedAtCommit == 0`; after `RunAsync`, `PublishedEvents.Count(e => e.EventName == "CardChanged" && Payload.cardId == companion.Id) == 1` and `== 1` for the original (payload read via `System.Text.Json` round-trip).
- V-552-2: a refused or unconfirmed land creates nothing | same | `C552_L06_RefusedOrUnconfirmedLandCreatesNoCompanion(string variant)` (`"dirty"`: `C448_V01` dirty recipe -> `LandRefused` before any op; `"push-rejected"`: `C448_V09_PushExitCannotReplaceRemoteConfirmation(false)` recipe -> op with `RemoteConfirmedAt == null`) | `h.CompanionAsync() == null`; `Cards.Count(BoardId) == 1`; `op?.VerificationCardId == null`; the terminal `Detail.ShouldNotContain("companion=")`; original revisions count `0`.
- V-552-3: a card-less owner lands as before | same, no `SeedOriginalCardAsync` | `C552_L07_CardlessOwnerLandsWithoutCompanion` | `Should.NotThrowAsync(() => h.RunAsync())` returning `Complete`; `Cards.CountAsync() == 0`; `Detail.ShouldNotContain("companion=")`; `op.VerificationCardId == null`; `HasPublication(op)` true.
- V-552-4: interrupted after publication still records | same | `C552_L08_InterruptedAfterPublicationStillRecordsCompanion(string kind)` (`"landed"`, `"already-present"`; `h.Fault.AfterAcknowledged = phase => phase == LandPhase.PublicationConfirmed ? throw new IOException("fixture") : Task.CompletedTask`; `var error = await Should.ThrowAsync<IOException>(() => h.RunAsync()); await h.FailAsync(error);`) | `op.LastReason == "landing_interrupted_after_publication"`; companion exists and `op.VerificationCardId == companion.Id`; the terminal event (`Landed`/`LandedWithResidue`/`AlreadyPresent`) `Detail.ShouldContain("companion=CARD-0002")`; `LandRefused` count `0`. (If the SafetyHarness path cannot throw from `AfterAcknowledged` cleanly, use `LandingProtocolHarness` exactly as `C498_FailureAfterPublicationRetainsPublication` does; the assertions are the contract.)
- V-552-5: a save fault before or after the terminal save leaves neither event nor card | same | `C552_L09_TerminalCutLeavesNeitherEventNorCompanion(string cut)` (`"before-save"`, `"after-save"`; `h.Fault.TerminalCut = cut; Should.ThrowAsync<InjectedSaveFailure>(() => h.RunAsync())`) | `AgentTaskEvents.Count(IsLandTerminal) == 0`; `Cards.Count(BoardId) == 1`; `op.VerificationCardId == null`; `CardRevisions.Count == 0`; then `TerminalCut = null; RestartServicesAsync(); RunAsync()` -> exactly one terminal event, exactly one companion, `op.VerificationCardId` set.
- V-552-6: cleanup retries create no second card or revision | same | `C552_L10_CleanupRetryCreatesNoSecondCardOrRevision(bool alreadyPresent)` (`C448_V33` recipe with `SeedOriginalCardAsync` first) | after the second run: cards with the label on the board `== 1`; companion revisions `== 1`; original revisions `== 1`; the `LandingCleanup` event `Detail.ShouldContain("companion=CARD-0002")`; `op.VerificationCardId` unchanged; `RemoteConfirmedAt` unchanged.
- V-552-7: the board without a Backlog column uses its first column | same, `SeedOriginalCardAsync(backlogColumn: false)` | `C552_L11_NoBacklogColumnFallsBackToTheFirstColumn` | companion `BoardColumnId == FirstColumnId` (the `InProgress` column, `ColumnOrder 0`) and `Status == CardStatus.InProgress`.

Writer, `tests/Antiphon.Tests/Application/PostLandVerificationCompanionsTests.cs` (`[Category("Integration")]`, isolated schema, direct `new PostLandVerificationCompanions(db, clock, NullLogger)` called inside `await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {owner.Id} FOR UPDATE");` then `SaveChangesAsync` + `CommitAsync`; seeds: project/board with Backlog(0)+Done(3) columns, original `CARD-0001` Done, owner Succeeded Code task bound to it, ops in the `C448_V33` shape):

- V-552-10: a fresh confirmed op creates a Backlog companion | `C552_W01_FreshOperationCreatesCompanion` | `Result.Created && Result.Linked`; card fields as V-552-1; `op.VerificationCardId == Result.CardId`; one companion revision (`Confirmed publication <op:N>`, `land`) and one original revision (`Post-land verification companion`, `land`).
- V-552-11: a second op of the same owner links by FK even when the key was edited out | `C552_W02_LinkByOwnerFkOutranksDescriptionKey` (O1 linked to the open companion, `O1.Active = false`; companion `Description = "edited by a human"`; O2 confirmed, unlinked) | `EnsureAsync(owner, O2)` -> `Created == false`, `Linked == true`, `CardId == companion.Id`; `O2.VerificationCardId == companion.Id`; companion revisions `+1` with `Reason == "Confirmed publication " + O2.Id:N`; description now contains `"O: " + O2.Id:D`.
- V-552-12: a caller-created companion is linked and appended, never replaced | `C552_W03_CallerCreatedCompanionIsLinkedAndAppended` (Backlog card on the original's board with `Description = key + "\npublication pending\ncaller notes"`) | `Created == false`, `Linked == true`, `CardId == caller card`; description `StartsWith(key + "\npublication pending\ncaller notes")` and contains `"O: " + op.Id:D`; the one new revision's `Description` snapshot equals the caller's original text; `Cards.Count(board) == 2`.
- V-552-13: a keyed card on another board is not the companion | `C552_W04_KeyOnAnotherBoardIsNotLinked` | `Created == true`; the other-board card's `Description` unchanged; its board still has one card.
- V-552-14: a closed or archived companion is superseded by a new one that names it | `C552_W05_ClosedCompanionIsSuperseded(string variant)` (`"done"`, `"canceled"`, `"archived"`: O1 linked to that card; O2 unlinked) | `Created == true`; new card `Identifier == "CARD-0003"`; `Description` line 1 is the stable key and line 2 is `"Supersedes: CARD-0002 (" + old.Id:D + ")"`; `O2.VerificationCardId == new.Id`; the old card's `Status`, `ArchivedAt`, `Description` unchanged.
- V-552-15: already linked is a no-op | `C552_W06_AlreadyLinkedWritesNothing` | `Result == (existing.Id, "CARD-0002", false, false)`; `db.ChangeTracker.HasChanges() == false` after the call; revision counts unchanged.
- V-552-16: an unbound owner is refused | `C552_W07_UnboundOwnerThrows` | `Should.ThrowAsync<InvalidOperationException>` whose `Message == "post_land_companion_requires_card"`; `Cards.Count == 0`.
- V-552-17: the link is FK Restrict | `C552_W08_LinkedCompanionCardCannotBeDeleted` | `db.Cards.Remove(companion); Should.ThrowAsync<DbUpdateException>` with `((PostgresException)ex.InnerException!).SqlState == PostgresErrorCodes.ForeignKeyViolation`.
- V-552-18: the migration chain applies | `C552_W09_MigrationAddsVerificationCardColumnAndIndex` (`AgentTaskLandingPersistenceTests.C448_V31_MigrationAndConcurrentOperations` database recipe) | migrations contain one ending `_AddLandingVerificationCard`; after `MigrateAsync()`, `information_schema.columns` has `("AgentTaskLandings", "VerificationCardId")` and `pg_indexes` has `IX_AgentTaskLandings_VerificationCardId`; a landing row inserted with `VerificationCardId = <unknown guid>` throws `ForeignKeyViolation`.
- V-552-19: identifier comes from the board's highest | `C552_W10_IdentifierIsAllocatedFromTheBoardsHighest` (board has `CARD-0001` and `CARD-0007`) | companion `Identifier == "CARD-0008"`.

Writer unit, `tests/Antiphon.Tests/Application/PostLandVerificationCompanionsDescribeTests.cs` (`[Category("Unit")]`):

- V-552-9a: constants and key | `C552_U01_StableKeyTitleAndLabel` | `Label == "post-land-verification"`, `KeyPrefix == "post-land-verification:"`, `StableKey(g) == "post-land-verification:" + g.ToString("D")`, `Title("CARD-0552") == "Post-land verification: CARD-0552"`.
- V-552-9b: the description is ASCII, one fact per line, capped | `C552_U02_DescribeIsAsciiOneFactPerLineAndCapped(bool sparse)` (`sparse == true`: no Review task, no plan, `ProjectId` null, null handoffs) | line 1 `== StableKey(owner.Id)`; contains `"Review task: none recorded"`, `"Plan: not recorded"`, `"Commissioning project: null"`, `"Code handoff: none recorded"` when sparse, else the exact ids/paths/handoff text verbatim; every char `< 128`; `Length <= CardService.MaxDescriptionLength`; with a 30,000-char `NextHandoff` on both owner and review the result is still `<= CardService.MaxDescriptionLength` and still ends with the `Dispatch (explicit, WIP 1)` line.
- V-552-9c: non-ASCII input is transliterated or dropped, never emitted | `C552_U03_NonAsciiInputNeverReachesTheDescription` (handoff text containing U+2014 and U+00E9) | every char `< 128`; the line still contains `"Code handoff: "`.

Projection unit, `tests/Antiphon.Tests/Application/MutationDebtProjectionTests.cs` (`[Category("Unit")]`, in-memory rows):

- V-552-20: nothing in, nothing out | `C552_P01_EmptyInputsYieldNothing` | `Build([], [], [], {}).ShouldBeEmpty()`.
- V-552-21: consumption is exactly open-or-Succeeded | `C552_P02_ConsumingStatusesAreExactlyOpenAndSucceeded(AgentTaskStatus status, bool consumed)` over all seven statuses (`Queued/Dispatched/Working/Blocked/Succeeded -> true`, `Failed/Canceled -> false`) | `Build(...).Any(d => d.Companion.Id == companion) == !consumed`; when present, `AnyAttempt == true`.
- V-552-22: the newest confirmed op is the source and consumption is judged on it | `C552_P03_NewestConfirmedOperationWins` (O1 older, O2 newer, both linked; Succeeded sourced task on O1 only) | single debt, `Source.Id == O2.Id`, `AnyAttempt == false`.
- V-552-23: an open unsourced Mutation on the companion consumes; a settled one does not | `C552_P04_UnsourcedMutationOnCompanion(AgentTaskStatus status, bool consumed)` (`Queued/Dispatched/Working/Blocked -> true`, `Succeeded/Failed/Canceled -> false`; the task is a `TaskRow` in `boundStages` with `CardId == companion`, `Role == Mutation`) | row absent iff consumed.
- V-552-24: terminal, NeedsDecision or archived companions yield nothing | `C552_P05_TerminalOrArchivedCompanionYieldsNothing(CardStatus status, bool archived)` (`Done`, `Canceled`, `NeedsDecision`, `(Backlog, archived: true)`) | empty; `(Backlog,false)`, `(InProgress,false)`, `(Review,false)` yield one.
- V-552-25: order is `ReadySince` then identifier | `C552_P06_OrderIsRemoteConfirmedAtThenIdentifierIgnoreCase` (A `12:00`, B `11:00`, C `11:00` with identifiers `card-0009`, `CARD-0010`) | order `[B(card-0009), C(CARD-0010), A]`.
- V-552-26: a companion with no card row or no matching landing yields nothing | `C552_P07_UnknownCardYieldsNothing` | empty.

Projection integration, additions to `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs` (isolated schema, M-4; `original` is `SeedCardAsync(db, CardStatus.Done, "CARD-0001")`, `companion = SeedCardOnBoardAsync(db, original.BoardId, Backlog, "CARD-0002")`, `owner = SeedTaskAsync(... Code, Succeeded, cardId: original.Id, workspace: Worktree, repoPath, worktreeBranch, completedAt)`, `op = SeedConfirmedLandingAsync(db, owner, companion.Id, remoteConfirmedAt)`):

- V-552-27: the row exists with every field | `C552_Q01_ConfirmedPublicationWithOpenCompanionIsAMutationReadyRow` (plus a Succeeded Plan on the original with `DeliverablePath = "docs/superpowers/plans/2026-09-17-card-0552-mutation-tracked-stage-plan.md"`, `DeliverableRef = "abc123"`, and a Succeeded Review on the original with `NextStage = Land`, `NextHandoff = "original Code landing owner"`) | Mutation stage `Ready` has one row: `Card.Id == companion.Id`, `Card.Identifier == "CARD-0002"`, `SourcePlanTaskId == owner.Id`, `SourcePlanShortId == DelegationReportFormatter.Short(owner.Id)`, `ReadySince == op.RemoteConfirmedAt` (tick-truncated), `DeliverablePath == plan path`, `DeliverableRef == "abc123"`, `SourceRole == Code`, `Handoff == "original Code landing owner"`, `SourceLandingOperationId == op.Id`, `SourceLandingSha == op.VerifiedSourceSha`, `OriginalCard!.Id == original.Id`, `OriginalCard.Identifier == "CARD-0001"`, `RoutingPin == null`.
- V-552-28: no plan and no Review gives an empty deliverable and a null handoff | `C552_Q02_NoPlanNoReviewGivesEmptyDeliverableAndNullHandoff` | `DeliverablePath == ""`, `DeliverableRef == null`, `Handoff == null` (the client then opens the owner's drawer, `pipelineStageModel.test.ts` "opens the source task drawer when a ready row has no deliverable path").
- V-552-29: an unconfirmed publication produces no row | `C552_Q03_UnconfirmedPublicationProducesNoRow` (`confirmed: false`: `RemoteConfirmedAt` set, `Publication Landed`, `ConfirmationMethod "none"`) | Mutation `Ready.Where(r => r.Card.Id == companion.Id).ShouldBeEmpty()`.
- V-552-30: terminal or archived companions produce none | `C552_Q04_TerminalOrArchivedCompanionProducesNoRow(CardStatus status, bool archived)` (`Done`, `Canceled`, `NeedsDecision`, `(Backlog, true)`) | empty.
- V-552-31: open or Succeeded sourced consumes; Failed/Canceled returns the row | `C552_Q05_SourcedAttemptConsumption(AgentTaskStatus status, bool consumed)` (seven arguments as V-552-21) | `Ready` empty iff consumed; when present the row's `SourceLandingOperationId == op.Id`.
- V-552-32: an open unsourced Mutation on the companion consumes; a settled one does not | `C552_Q06_UnsourcedMutationOnCompanion(AgentTaskStatus status, bool consumed)` (`Queued`, `Working` -> true; `Succeeded` -> false) | as stated.
- V-552-33: the newest operation is the source | `C552_Q07_NewestOperationIsTheSource` (O1 `active: false`, older; O2 newer; Succeeded sourced on O1) | one row, `SourceLandingOperationId == O2.Id`, `SourceLandingSha == O2.VerifiedSourceSha`, `ReadySince == O2.RemoteConfirmedAt`.
- V-552-34: a landing row outranks a legacy `next: mutation` row on the same card, and legacy rows elsewhere still project | `C552_Q08_LandingRowOutranksLegacyRowOnSameCardAndLegacyElsewhereStays` (companion also has a Succeeded Code `NextStage = Mutation`; a third card on another board has only the legacy source) | companion rows `ShouldHaveSingleItem()` with `SourceLandingOperationId == op.Id`; the third card's row is present with `SourceLandingOperationId == null`.
- V-552-35: rows order by `ReadySince` then identifier | `C552_Q09_RowsOrderByReadySinceThenIdentifier` (two companions, ops at `T-2h` and `T-1h`) | `Ready.Select(r => r.Card.Identifier) == ["CARD-0002", "CARD-0003"]` in that order.
- V-552-36: the card pin outranks the stage pin | `C552_Q10_CardPinOutranksStagePinOnMutationRow` (stage pin `Mutation` and card pin `(companion, Mutation)` seeded as in `a_ready_row_carries_the_card_code_pin_when_one_is_set`) | `RoutingPin!.Id == cardPin.Id`; with the card pin cleared, `== stagePin.Id`.
- V-552-37: the Done original yields no row under any stage | `C552_Q11_OriginalDoneCardYieldsNoRowUnderAnyStage` (original also has a Succeeded Code `NextStage = Mutation`) | `Stages.SelectMany(s => s.Ready).ShouldNotContain(r => r.Card.Id == original.Id)`; the companion row's `OriginalCard!.Id == original.Id`; original `Status == Done` re-read.
- V-552-38: the route carries the three members | `AgentTaskPipelineEndpointTests.C552_Q12_PipelineJsonCarriesTheLandingMembers` (factory host; seed through M-4 helpers into the factory db; assert within the row whose `card.id` equals the companion) | JSON of that row contains `"sourceLandingOperationId":"<op:D>"`, `"sourceLandingSha":"<L>"`, `"originalCard":{"id":"<original:D>"`.

Card-transition sweep (G-133 shape), addition to `PostLandMutationWorkflowTests`:

- V-552-39: the card-transition sweep over a board holding a server-created companion creates no task and no session | `C552_L12_NoTickSpendWithLinkedCompanion` (harness `SeedCardAsync(Done)` original, `SeedCardAsync(Backlog)` companion, a Succeeded Code owner bound to the original, and a confirmed landing row linked to the companion inserted via `world.CreateContext()`) | `ScanAsync() == 0`; `AgentTasks.Count(t => t.CardId == companion.Id) == 0`; `SessionCountForAsync(companion) == 0`; original `Done`; companion `Backlog`.

Endpoint, `tests/Antiphon.Tests/Application/AgentTaskVerificationCompanionEndpointTests.cs` (`[NotInParallel] [ClassDataSource<LandContractWebAppFactory>(Shared = SharedType.PerClass)] [Category("Integration")]`, `[Before(Test)] ResetAsync`, M-5):

- V-552-40: POST creates and returns the coordinates | `C552_E01_PostCreatesCompanionAndReturnsCoordinates` | `200`; body `taskId == task.Id`, `operationId == op.Id`, `identifier == "CARD-0002"`, `created == true`, `linked == true`, `cardId` equals the new card; the card is on the original's board in Backlog with the label; `op.VerificationCardId == cardId`.
- V-552-41: a second POST is idempotent | `C552_E02_SecondPostIsIdempotent` | `200`, same `cardId`, `created == false`, `linked == false`; card count on the board `== 2`; revision counts unchanged.
- V-552-42: the short id resolves | `C552_E03_ShortIdResolves` (`DelegationReportFormatter.Short(task.Id)`) | `200` with `taskId == task.Id`.
- V-552-43: no confirmed landing is 409 | `C552_E04_UnconfirmedIs409(string variant)` (`"none"`: no landing; `"unconfirmed"`: landing with `RemoteConfirmedAt == null`) | `409`, `code == "verification_publication_unconfirmed"`; no card created.
- V-552-44: Mutation-role or sourced tasks are 409 | `C552_E05_MutationOrSourcedTaskIs409(string variant)` (`"mutation-role"`: Role Mutation with a confirmed landing; `"sourced"`: Role Code with `SourceLandingOperationId` set) | `409`, `code == "verification_publication_forbidden"`; no card.
- V-552-45: a card-less task is 409 | `C552_E06_CardlessTaskIs409` | `409`, `code == "verification_companion_requires_card"`.
- V-552-46: unknown is 404 | `C552_E07_UnknownTaskIs404` | `404`.
- V-552-47: the task GET exposes the link | `C552_E08_TaskGetExposesVerificationCardId` | before POST `landing.verificationCardId` is null; after, equals `cardId`.
- V-552-48: the newest confirmed landing is chosen when none is active | `C552_E09_NewestConfirmedLandingIsChosenWhenNoActiveLanding` (two landings `active: false`, `RemoteConfirmedAt` `T-2h` and `T-1h`, `task.ActiveLandingId == null`) | `operationId` equals the `T-1h` landing.
- V-552-49: the endpoint waits for the owner's row lock | `C552_E10_PostWaitsForTheOwnerRowLock` (open a `NpgsqlConnection`+transaction on the factory connection string, `SELECT 1 FROM "AgentTasks" WHERE "Id" = @id FOR UPDATE`; start the POST; `await Task.Delay(500)`) | `post.IsCompleted == false` at 500 ms; after `COMMIT`, the POST completes `200` within 5 s and exactly one card exists.

Docs and client contract, additions to `PostLandMutationContractTests` (`[Category("Unit")]`):

- V-552-90: active recipes say the publication records the companion | `C552_D01_ActiveRecipeSaysPublicationRecordsCompanion(string path)` (`docs/orchestration-loop.md`, `.claude/skills/antiphon-delegate/SKILL.md`) | contains (`Case.Insensitive`) `"confirmed publication creates or links the companion"`, `"names it in the land outcome"`, `"the Mutation sweep is the one tick that spends"`, `"Mutation ready row"`, `"Delegation:MutationAutoDispatch"`; does not contain `"no tick creates cards or spends quota"`.
- V-552-91: bundles say the same and stay ASCII | `C552_D02_OrchestratorBundleAndReadmeSayThePublicationRecordsTheCompanion` | `InstructionBundles.TextOf(InstructionBundles.Orchestrator)` and `File.ReadAllText(RepoFile("server/Bundles/README.md"))` each contain `"the confirmed publication creates or links the companion"` and `"the Mutation sweep is the one tick that spends"`, neither contains `"caller records same-board companion"` nor `"no tick creates cards or spends quota"`; every char of the orchestrator bundle `< 128`.
- V-552-92: the route map names the endpoint | `C552_D03_RouteMapNamesTheCompanionEndpoint` | `docs/antiphon-api.md` contains `"POST   /api/agent-tasks/{id}/verification-companion"`; `docs/ops-http.md` contains `"verification-companion"` and `"verification_publication_unconfirmed"`.
- V-552-93: client types carry the new members | `C552_D04_ClientTypesCarryTheNewMembers` | `client/src/api/agentTasks.ts` contains `"sourceLandingSha?: string | null"` and `"originalCard?: AgentTaskPipelineCardRefDto | null"`; `client/src/api/attention.ts` contains `"| 'MutationDispositionPending'"`.
- V-552-94: the CARD-0478 substrings survive | R-552-7 (existing `C478_V11_ActiveRecipeHasDurableCompanionAndExplicitContinuation` stays green).

Sweep, `tests/Antiphon.Tests/Application/MutationAutoDispatchSweepTests.cs` (`[Category("Integration")] [ParallelLimiter<ProcessSpawnLimit>]`, `MutationSweepWorld` M-6; every test starts from one real Landed publication whose companion is `CARD-0002`, ready row present):

- V-552-50: an eligible tick creates exactly one task of the required shape | `C552_S01_EligibleTickCreatesOneSourcedMutationTask` | `tick.Reason == "created"`, `Created == 1`, `tick.OperationId == world.Operation`, `tick.CompanionCardId == world.Companion`; the task row: `Kind == Worker`, `Role == Mutation`, `Workspace == Worktree`, `CardId == world.Companion`, `SourceLandingOperationId == world.Operation`, `SourceLandingSha == op.VerifiedSourceSha`, `Title == "post-land mutation checks: CARD-0001"`, `ExpectedDurationMinutes == 720`, `ProjectId == owner.ProjectId`, `ReplyTo == None`, `Status == Queued`, `AgentSessionId == null`, `Goal == MutationAutoDispatchSweep.ComposeGoal(...)` for the same inputs, `MergeTargetRef == null`, `AgentId == null`; `AgentSessions.Count == 0`; companion `Status == Backlog`; `AgentTaskEvents` has one `Created` for it; `world.PipelineAsync()` Mutation `Ready` for the companion is now empty.
- V-552-51: disabled creates nothing | `C552_S02_DisabledCreatesNothing` (`configure: s => s.MutationAutoDispatch.Enabled = false`) | `Reason == "disabled"`, `Created == 0`, no Mutation task.
- V-552-52: paused creates nothing | `C552_S03_PausedCreatesNothing` (`world.Control.Pause()`) | `Reason == "paused"`; after `Resume()` the next tick is `"created"`.
- V-552-53: any open Mutation task anywhere skips the tick | `C552_S04_AnyOpenMutationTaskSkipsTheTick(AgentTaskStatus status, bool sameProject)` (`Queued/Dispatched/Working/Blocked` x `true/false`; the open task is unsourced, on no card, `ProjectId` = world project or a second project) | `Reason == "mutation-open"`, `Created == 0`, `AgentTasks.Count(Mutation) == 1`.
- V-552-54: settled Mutation tasks elsewhere do not skip | `C552_S05_SettledMutationTasksDoNotSkip(AgentTaskStatus status)` (`Succeeded/Failed/Canceled`, unsourced, other card) | `Reason == "created"`.
- V-552-55: the active window gate (start inclusive, end exclusive, wrap) | `C552_S06_ActiveWindow(string? window, string? tz, string clockUtc, string reason)` with `[Arguments(null, null, "12:00", "created")]`, `("06:00-22:00", "UTC", "12:00", "created")`, `("22:00-06:00", "UTC", "12:00", "outside-window")`, `("22:00-06:00", "UTC", "22:00", "created")`, `("22:00-06:00", "UTC", "06:00", "outside-window")`, `("22:00-06:00", "UTC", "05:59", "created")`, `("06:00-21:00", "Asia/Tokyo", "12:00", "outside-window")` (12:00Z is 21:00 JST, end exclusive), `("06:00-21:00", "Asia/Tokyo", "11:59", "created")`; the clock is set to `2100-06-01T<clockUtc>Z` before the tick | `tick.Reason == reason`.
- V-552-56: the budget ceiling gates at `>=` | `C552_S07_BudgetCeiling(decimal spend, string reason)` (`75.00m -> "budget-met"`, `74.99m -> "created"`, `75.01m -> "budget-met"`; spend seeded as a Succeeded unsourced Mutation task on no card with `CostUsd = spend`, `CreatedAt = 2100-06-01T01:00Z`) | `tick.Reason == reason`.
- V-552-57: only the current UTC day counts, inclusive of `00:00:00` | `C552_S08_OnlyTheCurrentUtcDayCounts(string createdAt, decimal spend, string reason)` (`("2100-05-31T23:59:59Z", 1000m, "created")`, `("2100-06-01T00:00:00Z", 75m, "budget-met")`, `("2100-06-02T00:00:00Z", 1000m, "created")`) | as stated.
- V-552-58: only Mutation-role spend counts | `C552_S09_NonMutationRoleSpendIsNotCounted` (Succeeded Code task, `CostUsd = 1000m`, today) | `"created"`.
- V-552-59: a held route creates nothing and the check asks for the effective pin's alias | `C552_S10_HeldRouteCreatesNothing` (`world.Availability.Held = true`; the `AgentTaskService` is built WITHOUT `modelAvailability`, so only the sweep's gate can stop the create) | `Reason == "route-held"`, no task, no exception; `world.Availability.Calls.Single() == (AgentKind.ClaudeCode, ModelLevelAliases.For(AgentKind.ClaudeCode, AgentModelLevel.Frontier))`; and `C552_S11_HeldCheckUsesTheEffectivePin` (card pin `(companion, Mutation)` Required `Grok`/`High` via `RoutingPinService.UpsertAsync`, `Held = false`) | `Calls.Single() == (AgentKind.Grok, ModelLevelAliases.For(AgentKind.Grok, AgentModelLevel.High))` and the created task's `AgentKind == Grok`, `RoutingPinId == pin.Id`.
- V-552-60: a model hold at create ends the tick and keeps the row | `C552_S12_ModelDisabledAtCreateEndsTheTickAndKeepsTheRow` (the sweep keeps `world.Availability` with `Held = false`; `AgentTaskService` with `modelAvailability: new ModelAvailability(db, clock, logger)` and a hold row `(ClaudeCode, "fable", DisabledUntil = now + 30m)`) | `Should.NotThrowAsync`; `Reason == "create-refused:model_disabled"`; `Created == 0`; no Mutation task; `PipelineAsync()` still shows the companion row; no `AgentTaskEvent` for a new task.
- V-552-61: a low subscription quota ends the tick and keeps the row | `C552_S13_SubscriptionQuotaLowEndsTheTickAndKeepsTheRow` (`quotaGate: new SubscriptionQuotaGate(new SubscriptionUsageReader(db, clock), Options.Create(new SubscriptionQuotaGateSettings()), clock, logger)`; usage sample `Provider ClaudeCode`, `SubscriptionKey "ClaudeCode"`, `RemainingPercent 3`, `ResetsAt = now + 36h`, `ObservedAt = now`) | `Reason == "create-refused:subscription_quota_low"`; row still ready; no task.
- V-552-62: the absolute cap ends the tick and keeps the row | `C552_S14_AbsoluteConcurrencyCapEndsTheTickAndKeepsTheRow` (`MaxOpenTasks = 1`, one open Code task on no card) | `Reason == "create-refused:concurrency_limit"`; no Mutation task; row still ready.
- V-552-63: a prior attempt is never auto-retried but stays visible | `C552_S15_PriorAttemptIsNeverAutoRetriedButStaysVisible(AgentTaskStatus status)` (`Failed`, `Canceled`; a sourced task on `world.Operation` bound to the companion) | `Reason == "no-candidate"`; `Created == 0`; `PipelineAsync()` Mutation `Ready` has the companion row with `SourceLandingOperationId == world.Operation` (D-5); a `Blocked` argument row gives `"mutation-open"` and no row.
- V-552-64: a runner without custody support refuses and the row survives until support returns | `C552_S16_RunnerWithoutCustodyRefusesAndTheRowSurvives` (`world.Runner.VerificationStoreId = null` for tick 1, restored for tick 2) | tick 1 `Reason == "create-refused:verification_custody_unsupported_backend"`, no task; tick 2 `"created"`.
- V-552-65: the oldest ready row is created first | `C552_S17_OldestReadyRowIsCreatedFirst(bool secondIsOlder)` (`SeedSecondDebtAsync(remoteConfirmedAt: secondIsOlder ? now - 1h : now + 1h)`) | `tick.OperationId == (secondIsOlder ? second.OperationId : world.Operation)`; the created task's `CardId` matches that debt's companion.
- V-552-66: one create per tick even when the role cap allows two, and the next tick sees it open | `C552_S18_OneCreatePerTick` (`SeedSecondDebtAsync(now - 1h)`; `configure: s => s.RolePolicy["Mutation"].RecommendedInFlight = 2`) | tick 1 `Created == 1`; tick 2 `Reason == "mutation-open"`; `AgentTasks.Count(Mutation) == 1`.
- V-552-67: the created row is admissible to the existing custody path | `C552_S19_SweepCreatedTaskEntersTheExistingCustodyPath` (after tick: `DelegationWorktreeService.CreateForTaskAsync(task, lease)` under `IRepositoryMutationLease.TryAcquireAsync(task.RepoPath!)`, then the `PostLandMutationWorld.ReserveAsync` recipe) | a `VerificationExecutions` row with `TaskId == task.Id` and `SourceLandingOperationId == world.Operation`; `task.SourceLandingSha == op.VerifiedSourceSha`; `Directory.Exists(task.WorktreePath)`.
- V-552-68: the companion gets the auto-dispatch revision | `C552_S20_AcceptedCreateWritesTheCompanionRevision` | companion revisions go from 1 to 2; the new one `Kind == ContentEdit`, `Reason == "mutation-auto-dispatch"`, `EditedBy == "mutation-sweep"`; description contains `"Auto-dispatched Mutation " + task.Id:D + " for O=" + world.Operation:D + " at " + clock.GetUtcNow().UtcDateTime.ToString("O")`; the revision's `Description` snapshot is the pre-append text.
- V-552-69: a lost post-create write is recovered by the task binding | `C552_S21_LostRevisionAfterCreateIsHarmless` (tick 1; delete the `mutation-auto-dispatch` revision row; tick 2) | tick 2 `Reason == "mutation-open"`; after the task is set `Canceled`, tick 3 `Reason == "no-candidate"` and the companion row is visible.
- V-552-70: the sweep skips terminal or archived companions through the shared rule | `C552_S22_SweepSkipsTerminalOrArchivedCompanion(string variant)` (`"done"`, `"canceled"`, `"needs-decision"`, `"archived"` applied to the companion) | `Reason == "no-candidate"`.
- V-552-71: the tick without any landed publication is `no-candidate` | `C552_S23_NoDebtIsNoCandidate` (`CreateAsync(land: false)` variant of the world: no `RunAsync`) | `Reason == "no-candidate"`.
- V-552-72: the sweep never passes an override flag | proven by V-552-59..62 plus PC-552-45..48.

Brief, `tests/Antiphon.Tests/Application/MutationAutoDispatchGoalTests.cs` (`[Category("Unit")]`):

- V-552-73: the goal carries identity, not method | `C552_B01_GoalCarriesIdentityNotMethod` | contains `"CARD-0001"`, `original.Id:D`, `"CARD-0002"`, `companion.Id:D`, `owner.Id:D`, `review.Id:D`, `"C=" + op.OriginalSourceSha`, `"O=" + op.Id:D`, `"L=" + op.VerifiedSourceSha`, `"R=" + op.ObservedRemoteTargetSha`, the plan path, `"HEAD must equal L=" + op.VerifiedSourceSha`, `"enumerate every PC-n and named variant"`, `"stage-mutation.md"`, `"you only report"`; does not contain `"--treenode-filter"`, `"dotnet run"`, `"bin-pc"`, `"OutputPath"`; every char `< 128`; `Length < 20_000`.
- V-552-74: oversized inputs stay under the cap | `C552_B02_GoalStaysUnderTheCapWithOversizedInputs` (30,000-char handoffs and a 5,000-char plan path) | `Length < 20_000`; still contains `"HEAD must equal L="`.
- V-552-75: sparse inputs are named as absent | `C552_B03_SparseInputsAreNamedNotOmitted` (no Review task, no plan) | contains `"Review task: none recorded"` and `"Plan: not recorded"`.

Settings, `tests/Antiphon.Tests/Application/MutationAutoDispatchSettingsTests.cs` (`[Category("Unit")]`):

- V-552-76: defaults | `C552_C01_Defaults` | `new DelegationSettings().MutationAutoDispatch` has `Enabled == true`, `SweepMinutes == 5`, `DailyBudgetUsd == 75m`, `ExpectedMinutes == 720`, `ActiveWindow == null`, `TimeZoneId == null`.
- V-552-77: binding | `C552_C02_BindsFromConfiguration` (`ConfigurationBuilder().AddInMemoryCollection({"Delegation:MutationAutoDispatch:DailyBudgetUsd" = "10", "...:Enabled" = "false", "...:ActiveWindow" = "22:00-06:00", "...:TimeZoneId" = "Europe/London"})` bound to `DelegationSettings` at section `Delegation`) | values as configured.
- V-552-78: the validator refuses bad values | `C552_C03_ValidatorRefusesBadValues(string field, string value)` (`DailyBudgetUsd -1`, `SweepMinutes 0`, `ExpectedMinutes 0`, `ActiveWindow "25:00-06:00"`, `ActiveWindow "0600-2200"`, `TimeZoneId "Mars/Olympus"` with a window set) | `new DelegationSettingsValidator().Validate(null, settings).Failed == true` and `FailureMessage` contains `"Delegation:MutationAutoDispatch:" + field`.

Host, `tests/Antiphon.Tests/Application/MutationAutoDispatchHostTests.cs` (`[NotInParallel] [ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)] [Category("Integration")]`):

- V-552-79: the test host has the sweep off and a real-Program boot creates nothing | `C552_H01_TestHostHasTheSweepOffAndCreatesNothing` | `_factory.Services.GetRequiredService<IOptions<DelegationSettings>>().Value.MutationAutoDispatch.Enabled == false`; the host has one `IHostedService` of type `MutationAutoDispatchHostedService`; seeding a confirmed landing + companion (M-4 shapes) into the factory db and calling `scope.ServiceProvider.GetRequiredService<MutationAutoDispatchSweep>().TickAsync` returns `Reason == "disabled"`; `AgentTasks.Count(t => t.CardId == companion) == 0`.
- V-552-80: the assembly guard sets the env var | `C552_H02_ProductionRunnerGuardDisablesTheSweep` | `Environment.GetEnvironmentVariable(ProductionRunnerGuard.MutationAutoDispatchEnvVar) == "false"` and the constant `== "Delegation__MutationAutoDispatch__Enabled"`.
- V-552-81: the hosted service returns immediately when disabled | `C552_H03_HostedServiceReturnsWhenDisabled` (`[Category("Unit")]`; `new MutationAutoDispatchHostedService(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Options.Create(new DelegationSettings { MutationAutoDispatch = { Enabled = false } }), NullLogger<...>.Instance)`; `StartAsync`) | `ExecuteTask!.Wait(TimeSpan.FromSeconds(1)) == true`.
- V-552-82: the sweep depends on no dispatcher or runner | `C552_H04_SweepHasNoDispatcherOrRunnerDependency` (`[Category("Unit")]`) | `typeof(MutationAutoDispatchSweep).GetConstructors().Single().GetParameters().Select(p => p.ParameterType)` contains neither `AgentTaskDispatcher`, `ISessionRunnerClient`, `SessionMessageQueueService` nor `CardService`.

Attention, `tests/Antiphon.Tests/Application/AttentionServiceMutationDispositionTests.cs` (`public partial class AttentionServiceTests`, M-7; each test: `AddBoardAsync`, original `AddCardOnBoardAsync(status: Done, identifier: "CARD-0001")`, companion `AddCardOnBoardAsync(identifier: "CARD-0002")`, owner `AddTaskAsync(session, Succeeded, ..., cardId: original)`, `op = AddLandingAsync(owner, companion)`, battery `AddTaskAsync(session, Succeeded, dispatchedMinutesAgo: 30, completedMinutesAgo: 5, role: Mutation, cardId: companion, sourceLandingOperationId: op, nextStage: ...)`):

- V-552-83: a settled sourced battery on an open companion is listed with the right severity | `C552_A01_SucceededSourcedBatteryOnOpenCompanionIsListed(PipelineHandoffKind next, AlertSeverity severity)` (`Decide -> Warning`, `None -> Info`) | one item with `TaskId == battery`: `Kind == MutationDispositionPending`, `((int)Kind) == 42`, `Severity == severity`, `CardId == companion`, `BoardId == board`, `Headline` contains `"CARD-0002"` and `"disposition"`, `Evidence` contains `"CARD-0001"`, `"CARD-0002"`, `"O=" + op:D`, `"L=" + sha`, `DelegationReportFormatter.Short(battery)` and (`Decide` ? `"verdict=findings"` : `"verdict=clean"`); `Actions == [AttentionAction.OpenCard, AttentionAction.OpenDrawer]`; `SinceUtc` within 10 ticks of the battery's `CompletedAt`; `AttentionSummaryDto.From(new AttentionDto(now, true, [item])).Open == 1`.
- V-552-84: the row clears when the companion is closed, canceled or archived | `C552_A02_RowClearsWhenCompanionIsClosedCanceledOrArchived(CardStatus status, bool archived)` (`Done`, `Canceled`, `(Backlog, true)`; also `Review` and `InProgress` still list) | `ItemsForAsync(scenario).ShouldNotContain(i => i.TaskId == battery && i.Kind == MutationDispositionPending)` for the clearing rows.
- V-552-85: a newer Mutation task bound to the companion clears the old row | `C552_A03_NewerBoundMutationTaskClearsTheRow(bool sourced)` (newer `Queued` Mutation on the companion, with or without `sourceLandingOperationId`) | no row for the old battery; the newer one is not listed either (not Succeeded).
- V-552-86: Failed or Canceled attempts are not this kind | `C552_A04_FailedOrCanceledAttemptIsNotThisKind(AgentTaskStatus status)` | items for the task contain no `MutationDispositionPending`; a `Failed` attempt with `completedMinutesAgo: 5` is a `RecentFailure`.
- V-552-87: unsourced Succeeded Mutation is not this kind | `C552_A05_UnsourcedSucceededMutationIsNotThisKind` | no row.
- V-552-88: the client draws the kind | `client/src/features/attention/attentionVisuals.test.ts`: `ALL_KINDS` gains `'MutationDispositionPending'`; new `it('draws MutationDispositionPending as a battery awaiting disposition')` | `ATTENTION_VISUALS.MutationDispositionPending.label === 'Mutation disposition pending'`, `.color === 'warning'`, `.hint.toLowerCase()` contains `'companion'`; `targetOf(item({ kind: 'MutationDispositionPending', taskId: 'task-552' })) === '/orchestrator?tab=delegations&task=task-552'`; `keyOf(row)` contains `'MutationDispositionPending'`; the existing "every kind is drawable / in a group" loops pass for the new kind. Command: `pwsh -File scripts/test-client.ps1 attentionVisuals`.

### Guards the regression

- R-552-1: every existing real-git land still passes with a card-less owner and the new constructor argument | `tests/Antiphon.Tests` `--treenode-filter "/*/*/AgentTaskLandPublicationTests/*"`, `"/*/*/AgentTaskLandNotificationPersistenceTests/*"`, `"/*/*/AgentTaskLandFailureDiagnosticTests/*"`, `"/*/*/AgentTaskLandStageOutcomeTests/*"`; decisive: `C448_V33_CleanupRetriesDoNotEmitAnotherPublication` `events.Count.ShouldBe(2)` and `C448_V22_ConfirmedPublicationUsesSavedReceipt` `HasPublication(op).ShouldBeTrue()`.
- R-552-2: the nine direct constructor sites compile and their suites stay green | `AgentTaskLandApprovalRequestTests`, `AgentTaskLandRequestTests`, `AgentTaskLandSweepTests`, `DelegationWorktreeTests`, `PostLandMutationWorktreeTests`, `C544World` consumers (`--treenode-filter "/*/*/C544*/*"`), `LandingProtocolHarness` consumers (`AgentTaskLandBoundaryControlledTests`, `AgentTaskLandAdmissionControlledTests`, `AgentTaskLandConcurrencyControlledTests`); decisive: build of `tests/Antiphon.Tests` with zero `CS7036`.
- R-552-3: `PostLandMutationWorld` is untouched (cards seeded after a card-less land) | `--treenode-filter "/*/*/PostLandMutation*Tests/*"`; decisive: `C478_V01_PublicSurfaceAndMigration` `detail.SourceLandingOperationId.ShouldBe(world.Operation)` and `C478_G133_NoTickSpend` count `0`.
- R-552-4: the legacy ready projection is unchanged | `--treenode-filter "/*/*/AgentTaskPipelineStatusTests/*"` and `"/*/*/AgentTaskPipelineEndpointTests/*"`; decisive: `non_success_latest_plan_wrong_deliverable_and_card_state_suppress_ready`, `a_newer_open_task_in_the_target_role_consumes_readiness`, `pipeline_route_is_literal_and_returns_the_advisory_contract` `dto.Stages.Count.ShouldBe(14)`.
- R-552-5: Mutation admission and WIP are unchanged | `--treenode-filter "/*/*/MutationAdmissionTests/*"`; decisive: `C470_second_mutation_is_role_limited(Queued, true)` `ex.Concurrency.Limit.ShouldBe(1)`.
- R-552-6: legacy `next: mutation` rows still project and settle | `--treenode-filter "/*/*/MutationPipelineTests/*"`; decisive: `C470_code_settlement_persists_mutation_ready` `ShouldHaveSingleItem().SourcePlanTaskId.ShouldBe(task.Id)`.
- R-552-7: the CARD-0478 contract strings and bundle budgets hold | `--treenode-filter "/*/*/PostLandMutationContractTests/*"` and `"/*/*/InstructionBundleTests/*"`; decisive: `C478_V11_ActiveRecipeHasDurableCompanionAndExplicitContinuation` every `ShouldContain`/`ShouldNotContain`; `the_worst_case_composition_measured_sits_far_under_the_budget`; `C478_G179_Ascii`.
- R-552-8: the attention feed's other kinds and the summary counts are unchanged | `--treenode-filter "/*/*/AttentionServiceTests/*"`; decisive: `summary_counts_match_a_full_sweep_for_the_same_rows`, `a_task_that_failed_today_is_carried_as_context` `Kind.ShouldBe(RecentFailure)`.
- R-552-9: the card-transition sweep still moves the companion off a Mutation task and never the original | `--treenode-filter "/*/*/PostLandMutationWorkflowTests/*"`; decisive: `C478_V07_OriginalDoneCompanionOpen(Working)` `moved.ShouldBe(1)` and original `Done`; `C478_G126_SuccessfulTaskDoesNotCloseVerification`.
- R-552-10: the Diagnose sweep's gates are unchanged by the new nested settings | `--treenode-filter "/*/*/CardDiagnosisSweepTests/*"`; decisive: `DiagnoseSweepEnabled_false_enqueues_nothing`.
- R-552-11: the model-availability create door and the quota gate are unchanged | `--treenode-filter "/*/*/ModelAvailabilityCreateTests/*"` and `"/*/*/AgentTaskAgentKindTests/Create_returns_409_subscription_quota_low_for_a_pinned_agent_whose_profile_key_is_low"`.
- R-552-12: the client suite and type check | `pwsh -File scripts/test-client.ps1` (full); decisive: `attentionVisuals.test.ts` "every kind the server can send is drawable" and `pipelineStageModel.test.ts` `rowTarget` cases; `npm run build`-equivalent type check inside the script's run (the `Record<AttentionKind, AttentionVisual>` type fails on a missing key).
- R-552-13: the landing persistence and migration tests | `--treenode-filter "/*/*/AgentTaskLandingPersistenceTests/*"`; decisive: `C448_V31_MigrationAndConcurrentOperations` `(await observer.AgentTaskLandings.CountAsync()).ShouldBe(0)`.
- R-552-14: the production-runner isolation contract | `--treenode-filter "/*/*/ProductionRunnerIsolationTests/*"`; decisive: `settings.BaseUrl.ShouldBe(DeadRunnerBaseUrl)`.

### Guard inventory

Land hook and writer (D-1, D-2, D-3):

- G-552-1: D-1 `HasPublication(op)` conjunct in the hook (no companion for a refused or unconfirmed land) | PC-552-1
- G-552-2: default 1 `task.CardId is not null` conjunct in the hook (a card-less owner lands as before) | PC-552-2
- G-552-3: D-1 `op.VerificationCardId != null` early return in `EnsureAsync` (cleanup retries and repeated endpoint calls write nothing) | PC-552-3
- G-552-4: D-1 step 1, link by the owner's other confirmed operation's FK before the description key | PC-552-4
- G-552-5: D-1 step 2, link by the stable key on the original's board | PC-552-5
- G-552-6: D-1 step 2, the key match is restricted to the original's board | PC-552-6
- G-552-7: D-1 "a linked-but-terminal companion is not reused" (non-terminal, non-archived filter on steps 1 and 2) | PC-552-7
- G-552-8: D-1 `outcome += "; companion=..."` marker (the land outcome names the companion) | PC-552-8
- G-552-9: D-1 reverse-link append on the original through `CardRevisionLog.AppendContentEdit` | PC-552-9
- G-552-10: D-6 the original's status, `CompletedAt` and terminal reason are never written | PC-552-10
- G-552-11: D-1 confirmation revision on the companion on a new link | PC-552-11
- G-552-12: S2 `CardChanged` published for the companion and the original after commit | PC-552-12
- G-552-13: D-1 atomicity, the companion is written inside the terminal transaction | PC-552-13
- G-552-14: D-3 FK Restrict on `VerificationCardId` | PC-552-14
- G-552-15: D-2 column choice, first Backlog column by `ColumnOrder`, else the board's first column | PC-552-15
- G-552-16: D-2 identifier from `CardIdentifierAllocator.ForBoardAsync(...).Next()` | PC-552-16
- G-552-17: D-2 `Describe` output capped at `CardService.MaxDescriptionLength` | PC-552-17
- G-552-18: D-2 `Describe` line 1 is the stable key | PC-552-18
- G-552-19: D-2 `Describe` output is ASCII | PC-552-19
- G-552-20: default 1 `EnsureAsync` refuses an unbound owner | PC-552-20

Projection (D-4, D-5):

- G-552-21: D-4 rule 1, companion Done/Canceled/NeedsDecision/archived yields no row (shared by glance and sweep) | PC-552-21
- G-552-22: D-5, a Succeeded sourced task consumes | PC-552-22
- G-552-23: D-4 rule 3, an open (Queued/Dispatched/Working/Blocked) sourced task consumes | PC-552-23
- G-552-24: D-5, a Failed or Canceled sourced task does not consume | PC-552-24
- G-552-25: D-4 rule 3, an open unsourced Mutation task bound to the companion consumes | PC-552-25
- G-552-26: D-4 rule 2, the newest confirmed operation is the source | PC-552-26
- G-552-27: D-4 dedupe, a landing-sourced row wins over a legacy `next: mutation` row on the same card | PC-552-27
- G-552-28: D-4 in-memory `HasPublication` filter on loaded landings | PC-552-28
- G-552-29: D-4 order `ReadySince` ascending then identifier (also the sweep's oldest-first) | PC-552-29
- G-552-30: D-4 `RoutingPin = EffectivePin(companion.Id, Mutation, ...)`, card pin outranks stage pin | PC-552-30
- G-552-31: D-4 `BuildReady`'s Done exclusion keeps the original out of every stage | PC-552-31

Sweep (D-9, D-10, D-12):

- G-552-32: D-9 step 1 `Enabled` gate | PC-552-32
- G-552-33: D-9 step 2 `IsPaused` gate | PC-552-33
- G-552-34: D-9 step 3 open-Mutation gate (fleet-wide, default 2) | PC-552-34
- G-552-35: D-9 step 4 `ActiveWindow` gate | PC-552-35
- G-552-36: default 4 window wrap across midnight (start > end) | PC-552-36
- G-552-37: D-9 step 5 budget gate at `>=` | PC-552-37
- G-552-38: D-9 step 5 UTC-day window of the spend query | PC-552-38
- G-552-39: D-9 step 5 Mutation-role filter of the spend query | PC-552-39
- G-552-40: D-9 step 5 the spend day comes from the injected clock | PC-552-40
- G-552-41: D-9 step 6 held-route gate | PC-552-41
- G-552-42: D-9 step 7 attempt-once (`!AnyAttempt`) | PC-552-42
- G-552-43: D-9 one create per tick | PC-552-43
- G-552-44: D-12 `ExpectedMinutes` applied to the create request | PC-552-44
- G-552-45: D-9 step 8 `IgnoreModelDisabled` never passed | PC-552-45
- G-552-46: D-9 step 8 `IgnoreConcurrencyLimit` never passed | PC-552-46
- G-552-47: D-9 step 8 `IgnoreSubscriptionQuota` never passed | PC-552-47
- G-552-48: D-9 step 8 `IgnoreRoutingPin` never passed | PC-552-48
- G-552-49: D-9 step 9 companion auto-dispatch revision | PC-552-49
- G-552-50: D-6 the sweep has no dispatcher or runner dependency (creates no session) | PC-552-50
- G-552-51: S6 test hosts run with the sweep off (`AntiphonWebAppFactory`) | PC-552-51
- G-552-52: S6 `ProductionRunnerGuard` env var for every Program boot | PC-552-52
- G-552-53: S6 hosted service returns when disabled | PC-552-53
- G-552-54: D-10 the goal is capped under 20,000 characters | PC-552-54
- G-552-55: D-10 the goal carries `HEAD must equal L=<sha>` | PC-552-55
- G-552-56: D-12 validator refuses a negative budget | PC-552-56

Attention (D-11):

- G-552-57: D-11 the row exists for a Succeeded sourced task on an open companion | PC-552-57
- G-552-58: D-11 clears when the companion is terminal or archived | PC-552-58
- G-552-59: D-11 clears when a newer Mutation task is bound | PC-552-59
- G-552-60: D-11 only Succeeded attempts (Failed/Canceled excluded) | PC-552-60
- G-552-61: D-11 only sourced tasks | PC-552-61
- G-552-62: D-11 `MutationDispositionPending = 42` wire value | PC-552-62
- G-552-63: S7 client `ATTENTION_VISUALS` carries the kind | PC-552-63

Endpoint and contract (D-7, D-8, S3 client):

- G-552-64: D-7 409 `verification_publication_unconfirmed` | PC-552-64
- G-552-65: D-7 409 `verification_publication_forbidden` | PC-552-65
- G-552-66: default 1 409 `verification_companion_requires_card` | PC-552-66
- G-552-67: D-7 404 through `ResolveTaskIdAsync` | PC-552-67
- G-552-68: D-7 newest confirmed landing when no active landing | PC-552-68
- G-552-69: D-7 the endpoint takes the owner's row lock | PC-552-69
- G-552-70: D-8 recipes name the publication as the companion writer and the sweep as the one spending tick; the obsolete sentence is gone | PC-552-70
- G-552-71: D-8 the orchestrator bundle stays ASCII | PC-552-71
- G-552-72: S3 client ready DTO type parity | PC-552-72

guards=72, mapped=72, missing=0, duplicate PC mappings=0.

### Positive controls

Each PC: apply the mutation, run only the named method (`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-pc/ -- --treenode-filter "/*/*/<Class>/<Method>"`), confirm the named assertion fails with a nonzero executed count, restore, refresh the restored file's timestamp, run the same method green. Client PCs run `pwsh -File scripts/test-client.ps1 attentionVisuals`. Mutation reports break, red, restore, green per row.

- PC-552-1: break G-552-1 by replacing `new AgentTaskLandingState().HasPublication(op)` in the hook condition with `true`; expect `PostLandCompanionLandHookTests.C552_L06_RefusedOrUnconfirmedLandCreatesNoCompanion("push-rejected")` red at `h.CompanionAsync().ShouldBeNull()`.
- PC-552-2: break G-552-2 by deleting `task.CardId is not null &&` from the hook condition; expect `C552_L07_CardlessOwnerLandsWithoutCompanion` red at `Should.NotThrowAsync` (the writer throws `post_land_companion_requires_card`).
- PC-552-3: break G-552-3 by deleting the `if (op.VerificationCardId is Guid linked) return ...` early return in `EnsureAsync`; expect `C552_L10_CleanupRetryCreatesNoSecondCardOrRevision(false)` red at `labelled.Count.ShouldBe(1)`.
- PC-552-4: break G-552-4 by making step 1's query `.Where(o => false)`; expect `PostLandVerificationCompanionsTests.C552_W02_LinkByOwnerFkOutranksDescriptionKey` red at `result.CardId.ShouldBe(companion.Id)`.
- PC-552-5: break G-552-5 by making step 2's query `.Where(c => false)`; expect `C552_W03_CallerCreatedCompanionIsLinkedAndAppended` red at `result.Created.ShouldBeFalse()`.
- PC-552-6: break G-552-6 by deleting `c.BoardId == original.BoardId &&` from step 2's query; expect `C552_W04_KeyOnAnotherBoardIsNotLinked` red at `result.Created.ShouldBeTrue()`.
- PC-552-7: break G-552-7 by making the open-companion predicate return `true` (drop the status and `ArchivedAt` checks); expect `C552_W05_ClosedCompanionIsSuperseded("done")` red at `result.Created.ShouldBeTrue()`.
- PC-552-8: break G-552-8 by deleting `outcome += $"; companion=..."`; expect `C552_L04_TerminalDetailAndOutcomeBodyNameTheCompanion(false)` red at `Detail.ShouldContain("companion=CARD-0002 (")`.
- PC-552-9: break G-552-9 by deleting the original's `AppendContentEdit` and description append; expect `C552_L03_OriginalGetsReverseLinkAppendOnly` red at `original.Description.ShouldEndWith(...)`.
- PC-552-10: break G-552-10 by adding `original.Status = CardStatus.Review;` beside the description append; expect `C552_L03_OriginalGetsReverseLinkAppendOnly` red at `original.Status.ShouldBe(CardStatus.Done)`.
- PC-552-11: break G-552-11 by deleting the companion's `AppendContentEdit` on a new link; expect `C552_L02_CompanionCarriesConfirmationRevision` red at `revisions.Count.ShouldBe(1)`.
- PC-552-12: break G-552-12 by deleting both post-commit `PublishToAllAsync("CardChanged", ...)` calls; expect `C552_L05_CardChangedPublishedForBothCardsAfterCommit` red at `PublishedEvents.Count(... companion ...).ShouldBe(1)`.
- PC-552-13: break G-552-13 by inserting `await _db.Database.CurrentTransaction!.CommitAsync(ct);` immediately after `EnsureAsync` in the hook; expect `C552_L09_TerminalCutLeavesNeitherEventNorCompanion("before-save")` red at `Cards.Count(BoardId).ShouldBe(1)`.
- PC-552-14: break G-552-14 by changing `onDelete: ReferentialAction.Restrict` to `ReferentialAction.Cascade` on the `VerificationCardId` foreign key in the `AddLandingVerificationCard` migration (the isolated schema is built from the compiled migrations, so this one file is the effective guard); expect `C552_W08_LinkedCompanionCardCannotBeDeleted` red at `Should.ThrowAsync<DbUpdateException>`.
- PC-552-15: break G-552-15 by ordering the column query `OrderByDescending(c => c.ColumnOrder)`; expect `C552_L01_ConfirmedLandCreatesCompanion(false)` red at `BoardColumnId.ShouldBe(backlogColumn)`.
- PC-552-16: break G-552-16 by replacing the allocator with `$"CARD-{await _db.Cards.CountAsync(c => c.BoardId == boardId, ct) + 1:0000}"`; expect `C552_W10_IdentifierIsAllocatedFromTheBoardsHighest` red at `Identifier.ShouldBe("CARD-0008")`.
- PC-552-17: break G-552-17 by deleting the clip of handoff lines in `Describe`; expect `PostLandVerificationCompanionsDescribeTests.C552_U02_DescribeIsAsciiOneFactPerLineAndCapped(false)` red at `Length.ShouldBeLessThanOrEqualTo(CardService.MaxDescriptionLength)`.
- PC-552-18: break G-552-18 by moving the stable key to the last line; expect `C552_U02_DescribeIsAsciiOneFactPerLineAndCapped(true)` red at `lines[0].ShouldBe(StableKey(owner.Id))`.
- PC-552-19: break G-552-19 by deleting the ASCII fold in `Describe`; expect `C552_U03_NonAsciiInputNeverReachesTheDescription` red at `((int)ch).ShouldBeLessThan(128)`.
- PC-552-20: break G-552-20 by deleting the `owner.CardId is null` throw in `EnsureAsync`; expect `C552_W07_UnboundOwnerThrows` red at `Should.ThrowAsync<InvalidOperationException>`.
- PC-552-21: break G-552-21 by deleting the companion status/archived skip in `MutationDebtProjection.Build`; expect `MutationDebtProjectionTests.C552_P05_TerminalOrArchivedCompanionYieldsNothing(Done, false)` red at `ShouldBeEmpty()`; the same mutation also turns `AgentTaskPipelineStatusTests.C552_Q04_TerminalOrArchivedCompanionProducesNoRow(Done, false)` and `MutationAutoDispatchSweepTests.C552_S22_SweepSkipsTerminalOrArchivedCompanion("done")` red (the projection-sharing evidence).
- PC-552-22: break G-552-22 by removing `AgentTaskStatus.Succeeded` from the consuming set in `Build`; expect `C552_P02_ConsumingStatusesAreExactlyOpenAndSucceeded(Succeeded, true)` red at `Any(...).ShouldBe(false)`.
- PC-552-23: break G-552-23 by removing the four open statuses from the consuming set (keep Succeeded); expect `C552_P02_ConsumingStatusesAreExactlyOpenAndSucceeded(Queued, true)` red.
- PC-552-24: break G-552-24 by adding `Failed` and `Canceled` to the consuming set; expect `C552_P02_ConsumingStatusesAreExactlyOpenAndSucceeded(Failed, false)` red at `Any(...).ShouldBe(true)`.
- PC-552-25: break G-552-25 by deleting the open-unsourced-Mutation clause from `Build`; expect `C552_P04_UnsourcedMutationOnCompanion(Queued, true)` red.
- PC-552-26: break G-552-26 by changing the source pick to `OrderBy(o => o.RemoteConfirmedAt)` (oldest); expect `C552_P03_NewestConfirmedOperationWins` red at `Source.Id.ShouldBe(O2.Id)`.
- PC-552-27: break G-552-27 by deleting the dedupe when merging landing rows into the `Mutation` list (append both); expect `C552_Q08_LandingRowOutranksLegacyRowOnSameCardAndLegacyElsewhereStays` red at `ShouldHaveSingleItem()`.
- PC-552-28: break G-552-28 by deleting the in-memory `HasPublication` filter over loaded landings; expect `C552_Q03_UnconfirmedPublicationProducesNoRow` red at `ShouldBeEmpty()`.
- PC-552-29: break G-552-29 by ordering `Build` `OrderByDescending(d => d.Source.RemoteConfirmedAt)`; expect `C552_P06_OrderIsRemoteConfirmedAtThenIdentifierIgnoreCase` red at the sequence assertion; the same mutation turns `C552_Q09_RowsOrderByReadySinceThenIdentifier` and `C552_S17_OldestReadyRowIsCreatedFirst(true)` red (sharing evidence).
- PC-552-30: break G-552-30 by calling `EffectivePin(null, Mutation, stagePins, cardPins)` for the Mutation row; expect `C552_Q10_CardPinOutranksStagePinOnMutationRow` red at `RoutingPin!.Id.ShouldBe(cardPin.Id)`.
- PC-552-31: break G-552-31 by removing `CardStatus.Done` from `BuildReady`'s skip; expect `C552_Q11_OriginalDoneCardYieldsNoRowUnderAnyStage` red at `ShouldNotContain(r => r.Card.Id == original.Id)`.
- PC-552-32: break G-552-32 by deleting the `Enabled` check in `TickAsync`; expect `MutationAutoDispatchSweepTests.C552_S02_DisabledCreatesNothing` red at `Reason.ShouldBe("disabled")`.
- PC-552-33: break G-552-33 by deleting the `IsPaused` check; expect `C552_S03_PausedCreatesNothing` red at `Reason.ShouldBe("paused")`.
- PC-552-34: break G-552-34 by deleting the open-Mutation query gate; expect `C552_S04_AnyOpenMutationTaskSkipsTheTick(Working, true)` red at `Reason.ShouldBe("mutation-open")` (the create then refuses with `create-refused:concurrency_limit`).
- PC-552-35: break G-552-35 by deleting the window check; expect `C552_S06_ActiveWindow("22:00-06:00", "UTC", "12:00", "outside-window")` red at `Reason.ShouldBe("outside-window")`.
- PC-552-36: break G-552-36 by deleting the wrap branch so a `start > end` window is evaluated as `start <= now && now < end` (always outside); expect `C552_S06_ActiveWindow("22:00-06:00", "UTC", "22:00", "created")` red at `Reason.ShouldBe("created")`.
- PC-552-37: break G-552-37 by changing `>=` to `>` in the budget comparison; expect `C552_S07_BudgetCeiling(75.00, "budget-met")` red at `Reason.ShouldBe("budget-met")`.
- PC-552-38: break G-552-38 by deleting `t.CreatedAt >= start && t.CreatedAt < end` from the spend query; expect `C552_S08_OnlyTheCurrentUtcDayCounts("2100-05-31T23:59:59Z", 1000, "created")` red.
- PC-552-39: break G-552-39 by deleting `t.Role == AgentTaskRole.Mutation` from the spend query; expect `C552_S09_NonMutationRoleSpendIsNotCounted` red.
- PC-552-40: break G-552-40 by replacing `_clock.GetUtcNow().UtcDateTime.Date` with `DateTime.UtcNow.Date` in `DailySpendUsdAsync`; expect `C552_S07_BudgetCeiling(75.00, "budget-met")` red at `Reason.ShouldBe("budget-met")` (the seeded 2100 spend is not "today").
- PC-552-41: break G-552-41 by deleting the `IsHeldAsync` gate; expect `C552_S10_HeldRouteCreatesNothing` red at `Reason.ShouldBe("route-held")`.
- PC-552-42: break G-552-42 by replacing `FirstOrDefault(d => !d.AnyAttempt)` with `FirstOrDefault()`; expect `C552_S15_PriorAttemptIsNeverAutoRetriedButStaysVisible(Failed)` red at `Reason.ShouldBe("no-candidate")`.
- PC-552-43: break G-552-43 by looping the create over every untouched candidate; expect `C552_S18_OneCreatePerTick` red at `tick1.Created.ShouldBe(1)`.
- PC-552-44: break G-552-44 by omitting `ExpectedMinutes` from the `CreateAgentTaskRequest`; expect `C552_S01_EligibleTickCreatesOneSourcedMutationTask` red at `ExpectedDurationMinutes.ShouldBe(720)`.
- PC-552-45: break G-552-45 by passing `IgnoreModelDisabled: true`; expect `C552_S12_ModelDisabledAtCreateEndsTheTickAndKeepsTheRow` red at `Reason.ShouldBe("create-refused:model_disabled")`.
- PC-552-46: break G-552-46 by passing `IgnoreConcurrencyLimit: true`; expect `C552_S14_AbsoluteConcurrencyCapEndsTheTickAndKeepsTheRow` red at `Reason.ShouldBe("create-refused:concurrency_limit")`.
- PC-552-47: break G-552-47 by passing `IgnoreSubscriptionQuota: true`; expect `C552_S13_SubscriptionQuotaLowEndsTheTickAndKeepsTheRow` red at `Reason.ShouldBe("create-refused:subscription_quota_low")`.
- PC-552-48: break G-552-48 by passing `IgnoreRoutingPin: true`; expect `C552_S11_HeldCheckUsesTheEffectivePin` red at `RoutingPinId.ShouldBe(pin.Id)`.
- PC-552-49: break G-552-49 by deleting the companion revision after an accepted create; expect `C552_S20_AcceptedCreateWritesTheCompanionRevision` red at `revisions.Count.ShouldBe(2)`.
- PC-552-50: break G-552-50 by adding an `ISessionRunnerClient runner` constructor parameter to `MutationAutoDispatchSweep`; expect `MutationAutoDispatchHostTests.C552_H04_SweepHasNoDispatcherOrRunnerDependency` red at `ShouldNotContain(typeof(ISessionRunnerClient))`.
- PC-552-51: break G-552-51 by deleting `["Delegation:MutationAutoDispatch:Enabled"] = "false"` from `AntiphonWebAppFactory`; expect `C552_H01_TestHostHasTheSweepOffAndCreatesNothing` red at `Enabled.ShouldBeFalse()`.
- PC-552-52: break G-552-52 by deleting the `SetEnvironmentVariable(MutationAutoDispatchEnvVar, "false")` line in `ProductionRunnerGuard`; expect `C552_H02_ProductionRunnerGuardDisablesTheSweep` red at `ShouldBe("false")`.
- PC-552-53: break G-552-53 by deleting the disabled early return in `MutationAutoDispatchHostedService.ExecuteAsync`; expect `C552_H03_HostedServiceReturnsWhenDisabled` red at `Wait(1s).ShouldBeTrue()`.
- PC-552-54: break G-552-54 by deleting the length clip in `ComposeGoal`; expect `MutationAutoDispatchGoalTests.C552_B02_GoalStaysUnderTheCapWithOversizedInputs` red at `Length.ShouldBeLessThan(20_000)`.
- PC-552-55: break G-552-55 by deleting the `HEAD must equal L=` line from `ComposeGoal`; expect `C552_B01_GoalCarriesIdentityNotMethod` red at `ShouldContain("HEAD must equal L=")`.
- PC-552-56: break G-552-56 by deleting the `DailyBudgetUsd < 0` rule from `DelegationSettingsValidator`; expect `MutationAutoDispatchSettingsTests.C552_C03_ValidatorRefusesBadValues("DailyBudgetUsd", "-1")` red at `Failed.ShouldBeTrue()`.
- PC-552-57: break G-552-57 by deleting `items.AddRange(await BuildMutationDispositionPendingItemsAsync(ct))` from `AttentionService.GetAsync`; expect `AttentionServiceTests.C552_A01_SucceededSourcedBatteryOnOpenCompanionIsListed(Decide, Warning)` red at `Single(i => i.TaskId == battery)`.
- PC-552-58: break G-552-58 by deleting the companion status/archived filter in that builder; expect `C552_A02_RowClearsWhenCompanionIsClosedCanceledOrArchived(Done, false)` red at `ShouldNotContain`.
- PC-552-59: break G-552-59 by deleting the newer-bound-Mutation check; expect `C552_A03_NewerBoundMutationTaskClearsTheRow(false)` red.
- PC-552-60: break G-552-60 by widening the status predicate to `Succeeded || Failed`; expect `C552_A04_FailedOrCanceledAttemptIsNotThisKind(Failed)` red.
- PC-552-61: break G-552-61 by deleting `t.SourceLandingOperationId != null` from the builder's query; expect `C552_A05_UnsourcedSucceededMutationIsNotThisKind` red.
- PC-552-62: break G-552-62 by declaring `MutationDispositionPending = 43`; expect `C552_A01_SucceededSourcedBatteryOnOpenCompanionIsListed(Decide, Warning)` red at `((int)item.Kind).ShouldBe(42)`.
- PC-552-63: break G-552-63 by deleting the `MutationDispositionPending` entry from `ATTENTION_VISUALS` in `attentionVisuals.ts`; expect `pwsh -File scripts/test-client.ps1 attentionVisuals` red at `draws MutationDispositionPending as a battery awaiting disposition` (and the type check, `Record<AttentionKind, AttentionVisual>` missing key).
- PC-552-64: break G-552-64 by deleting the unconfirmed check in the endpoint's service method (pass the newest landing regardless); expect `AgentTaskVerificationCompanionEndpointTests.C552_E04_UnconfirmedIs409("unconfirmed")` red at `StatusCode.ShouldBe(HttpStatusCode.Conflict)`.
- PC-552-65: break G-552-65 by deleting the Mutation-role/sourced check; expect `C552_E05_MutationOrSourcedTaskIs409("mutation-role")` red at `code.ShouldBe("verification_publication_forbidden")`.
- PC-552-66: break G-552-66 by deleting the card-less check in the endpoint's service method; expect `C552_E06_CardlessTaskIs409` red at `StatusCode.ShouldBe(Conflict)` (the writer's `InvalidOperationException` surfaces as 500).
- PC-552-67: break G-552-67 by replacing `service.ResolveTaskIdAsync(id, ct)` with `Guid.Parse(id)` in the route; expect `C552_E07_UnknownTaskIs404` red at `StatusCode.ShouldBe(NotFound)`.
- PC-552-68: break G-552-68 by ordering the fallback landing query `OrderBy(o => o.RemoteConfirmedAt)`; expect `C552_E09_NewestConfirmedLandingIsChosenWhenNoActiveLanding` red at `operationId.ShouldBe(newer.Id)`.
- PC-552-69: break G-552-69 by deleting `await LockTaskAsync(task.Id, ct)` from the endpoint's service method; expect `C552_E10_PostWaitsForTheOwnerRowLock` red at `post.IsCompleted.ShouldBeFalse()`.
- PC-552-70: break G-552-70 by restoring the sentence `no tick creates cards or spends quota.` in `docs/orchestration-loop.md`; expect `PostLandMutationContractTests.C552_D01_ActiveRecipeSaysPublicationRecordsCompanion("docs/orchestration-loop.md")` red at `ShouldNotContain("no tick creates cards or spends quota")`.
- PC-552-71: break G-552-71 by inserting U+2014 into the new sentence in `server/Bundles/orchestrator.md`; expect `C552_D02_OrchestratorBundleAndReadmeSayThePublicationRecordsTheCompanion` red at `((int)ch).ShouldBeLessThan(128)`.
- PC-552-72: break G-552-72 by deleting the `originalCard?: AgentTaskPipelineCardRefDto | null` line from `client/src/api/agentTasks.ts`; expect `C552_D04_ClientTypesCarryTheNewMembers` red at `ShouldContain("originalCard?: AgentTaskPipelineCardRefDto | null")`.

### Out of scope

- The `Supersedes:` line's presence (V-552-14 asserts it) has no PC: traceability text, not a guard on spend or on the obligation.
- `ReadySince` source column and the plan-deliverable/handoff lookup on the original (V-552-27/28) have no PC: display fields; the ordering guard (G-552-29) is the one that decides dispatch.
- The held-route check's alias resolution (V-552-59 second half) has no PC: a wrong alias is refused again by the create door (`ModelAvailability.EnsureAvailable`), so it is not independently bypassable; G-552-41 covers the gate itself.
- The sweep caller's `ProjectId`/`WorkingDirectory` and companion binding (V-552-50 asserts them) have no PC: `SourceLandingAdmission.RequireSourceAsync` refuses a project or card mismatch, so a wrong value cannot create.
- Catching the create `HttpException` (a throwing tick) has no PC: the hosted loop already logs and continues; V-552-60..62 assert `Should.NotThrowAsync` and the reason string.
- `LandingEvidenceDto.VerificationCardId` exposure and the `docs/antiphon-api.md` route line have V rows only.
- `provider_sign_in_required` and `verification_source_already_open` sweep refusals: see Inspection exclusions.
- `pipelineStageModel.test.ts` gains no case: the Mutation row renders through the generic ready path (`rowTarget` already covers empty `deliverablePath`).
- A live backfill of the eleven cards, the first real unattended battery and a browser check of the Mutation stage: S5, operator readback, recorded on the card close.
- `RefuseIfExhausted` on the sweep's request: the request carries no `Complexity`, so the flag has no effect to guard.

### Cost

Ordinary V/R floor (Code, `bin-c552/`), estimated:

| Step | Filter | Minutes |
|---|---|---|
| Restore + build server, tests, client | `dotnet build` + `npm ci` (warm) | 6 |
| Unit: writer, projection, goal, settings, docs, host unit | `"/*/*/(PostLandVerificationCompanionsDescribeTests*)|(MutationDebtProjectionTests*)|(MutationAutoDispatchGoalTests*)|(MutationAutoDispatchSettingsTests*)|(PostLandMutationContractTests*)/*"` | 2 |
| Integration, isolated schema: writer, projection, attention partial, transition | `"/*/*/(PostLandVerificationCompanionsTests*)|(AgentTaskPipelineStatusTests*)|(PostLandMutationWorkflowTests*)|(AttentionServiceTests*)/*"` | 9 |
| Real git: land hook, sweep | `"/*/*/(PostLandCompanionLandHookTests*)|(MutationAutoDispatchSweepTests*)/*"` (ProcessSpawnLimit lane, ~45 cases x ~12 s) | 10 |
| Factory hosts: endpoint, pipeline JSON, host off-switch | `"/*/*/(AgentTaskVerificationCompanionEndpointTests*)|(AgentTaskPipelineEndpointTests*)|(MutationAutoDispatchHostTests*)/*"` | 4 |
| R-552-1..14 named classes | the fourteen filters above | 22 |
| Client | `pwsh -File scripts/test-client.ps1` (full, ~5 min measured 2026-09-01) | 5 |
| **V/R floor** | | **58** |

PC floor (Mutation, post-land SourceLanding on this card's own companion, `bin-pc/`), estimated: 72 PCs. Method-scoped Unit cycles (PC-552-17..26, 29, 54..56, 62: 16) about 1.5 min each red+green with `--no-build` after one warm build; isolated-schema integration cycles (PC-552-4..7, 14, 16, 20, 27, 28, 30, 31, 57..61, 64..69: 25) about 3 min each; real-git cycles (PC-552-1..3, 8..13, 15, 32..53: 32, ProcessSpawnLimit serialised) about 4 min each; docs/client cycles (PC-552-63, 70..72: 4) about 2 min each. Batched in fours where files and methods differ (the sweep gates each touch `TickAsync`, so PC-552-32..43 run in pairs at most): 16x1.5 + 25x3 + 32x4 + 4x2 = 235 min unbatched, about 150 min batched, plus 6 min build and 10 min for the four incremental rebuilds the restore steps need. **PC floor: about 165 min.** Total verification floor = 6 (setup/build) + 58 (V/R) + 165 (PC) = **about 229 min, estimated** (no run was made in this stage). Savings against the naive floor: the class-scoped filters replace the 2,349-test Application filter (~26 min per run, CARD-0239) for every V/R step, and PC batching saves about 85 min over serial red/green cycles; both are estimates until Code and Mutation measure them.
