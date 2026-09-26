# CARD-0738: closing a card settles its open bound tasks

Date: 2026-09-26. Stage: Plan, with the verification design folded into this dispatch (the brief
asks for a `### Checkpoints` table and red-first rows, one round). Task: `08084c5f`, written on the
server2 Linux runner. Source inspected: `0e83d3f236c3465fa246dd904246f25f04a812e0`, which was
`origin/master` at the time (the task branch was fast-forwarded onto it before any line below was
cited, because master had moved `AgentTaskService.cs`, `CardService.cs`, `BoardDtos.cs` and
`card.ps1` for CARD-0710 since the task's StartRef). Next stage: **Code**, one round.

No production code, configuration, test or deployment changed during Plan. Three read-only
measurements were taken and are reported under ground truth rows 11 and 12: one isolated build of
`tests/Antiphon.Tests` into `bin-c738/` (deleted afterwards), three single-filter runs of existing
tests on that output, and one read of the live board through `ANTIPHON_API`.

## Outcome and scope

When a card leaves play, the delegated tasks bound to it must stop looking like live work. A
terminal move (Done or Canceled), a `card.ps1 close`, or an archive settles every bound task that
has not started, as Canceled, with a reason that names the card and carries the card's own close
or archive reason. A task that has started is not stopped: it gets one Warning event and one
attention row that says the card closed under it, and the human decides. A one-off sweep lists
the open tasks whose card is already terminal and cancels them only under an explicit `apply`.

In scope: `AgentTaskService.CancelAsync` (an optional reason); a new `CardTaskSettlement` service
that owns the "which tasks, and are they started" rule and is the only caller of the primitive
from a card path; hooks in `CardService.MoveAsync`, `CardService.ArchiveAsync` and
`ExternalTrackerSyncService`; the `MoveCardResult` report of what was settled; a
`CardClosedWhileWorking` attention kind and its client visuals; `POST /api/agent-tasks/closed-card-sweep`;
`card.ps1` output; the owner docs.

Out of scope: a recurring canceller (the card forbids auto-cancel without a flag); cancelling a
task created after its card closed (creation against a closed card is legal today, row 8);
resurrecting Canceled tasks on reopen (retry exists); filtering any listing by card state (D-6);
a body on `POST /api/agent-tasks/{id}/cancel`; a client dialog for the settlement summary (the
client ignores the new `taskSettlement` property until someone shows it); `AGENTS.md`.

## Ground truth

| Card / brief assumption | What the inspected code does | Design consequence |
|---|---|---|
| 1. "When a card moves to Done or Canceled" is one place. | Three writers put a card in a terminal column. (a) `CardService.MoveAsync` (`CardService.cs:428-490`): `PATCH /api/cards/{id}` (`CardEndpoints.cs:85`), which is `card.ps1 close` and `card.ps1 move -To <terminal>` (`scripts/card.ps1:638-690`) and the client drag; it already gates two terminal-only side effects on `targetColumn.IsTerminal && !wasTerminal` (`:469`, `:473`). (b) `ExternalTrackerSyncService.MarkInactive` (`:651-679`), reached from `ReconcileStaleIssuesAsync` (`:572`) when a linked issue is no longer active, and in principle `UpdateExisting` (`:390-407`) when a board maps a tracker state onto a terminal column. (c) `CardService.ArchiveAsync` (`:901-946`) sets `ArchivedAt` without changing `Status`; it refuses a live owner session and an active workflow run and never looks at bound tasks. Not terminal writers: `ApplyAutomatedMoveAsync` (`:1041`, refuses Done/Canceled and is only ever asked for InProgress/Review by `CardWorkTransitionService`), `ReopenAsync` (`:975`, leaves terminal), `OrchestratorService.ReconcileAsync` and `AgentSessionLaunchQueue.MoveCardToReviewAsync` (Review only), `TrackerBidirectionalSyncService.ApplyExternalReopens` (reopens only). | D-2: one settlement service with three call sites, guarded the same way the review checkpoint and the tracker push already are. Archive is included (the brief names it; an archived card is out of play and `HomeTaskService` already hides it). |
| 2. There is a task-settlement primitive to reuse. | `AgentTaskService.CancelAsync(Guid id, CancellationToken ct)` (`AgentTaskService.cs:2490-2519`) is it: refuses a settled row, stops the delegate (`StopDelegateAsync` `:3423`, kill through `IDelegateSessionStopper`, failure logged not thrown), supersedes capacity waits, removes the ephemeral agent, writes Canceled + `CompletedAt` + a `Canceled` event `"Canceled."`, saves, releases workspace consumers, publishes `AgentTaskChanged`. It takes no reason. Every other `Status = Canceled` writer is a specialist or optional-work expiry path (`AgentTaskDispatcher.cs:411`, `:4120`, `:4568`; `SpecialistTaskRunner`; `StandingSpecialistSeatService`; `SpecialistRequestService`; `SessionMessageQueueService` dead/expired briefs) and none is a general settle. Its only caller is the cancel endpoint (`AgentTaskEndpoints.cs:168-171`). | D-5: extend `CancelAsync` with an optional `reason`; nothing else writes Canceled for this card. |
| 3. "Dispatched-but-not-started" and "Working" can be read from `Status`. | They cannot. `Dispatched -> Working` flips only on a reply to a Blocked question (`AgentTaskReplyService.cs:394`, `:1325`, `:1375`) or an API-error deferral (`:1490`). Every live delegate on the Antiphon board at 2026-09-26T00:50Z, including the task writing this plan, is `Dispatched` while working. The dispatcher's own "has it started" verdict (`AgentTaskDispatcher.cs:1919-1943`, CARD-0117 D7) is: the task's brief queue row (`SessionQueuedMessages`, `Origin == Delegation`, `CreatedAt >= DispatchedAt`, body contains `DelegationReportFormatter.TaskMarker(task.Id)`) outranks the transcript; `Status == Pending` means never typed; `TranscriptPromptSpan.LoadAsync(db, sessionId, DispatchedAt, ct).TurnPrompts.Count > 0` is the transcript's opinion. | D-4 defines started from those two durable rows, in the direction that never kills something that may have begun. |
| 4. A Working task "gets exactly one attention item". | Attention is a read-only projection (`AttentionService.cs:15-36`): `GET /api/attention` computes rows from stored state, one row per task, first matching condition wins inside `BuildOpenTaskItemsAsync` (`:763`, open = Dispatched or Working, `:167-169`). There is no persisted attention entity; `AttentionKind` is append-only and the client maps every member (`client/src/api/attention.ts:17`, `attentionVisuals.ts`, lockstep test `attentionVisuals.test.ts:100`, `:267`). Highest member today: `ZombieCensusReport = 47`. | D-7: new kind `CardClosedWhileWorking = 48` inside the first-match chain (one row by construction) plus one durable `AgentTaskEvent` Warning at close time as the note linking the two. |
| 5. In-flight listings show tasks whose card is closed. | `HomeTaskService.LoadBoundTasksAsync` (`:91`) groups bound tasks by card and hides archived cards (`:62`) but not terminal ones; `AgentTaskPipelineStatusService` in-flight = Dispatched/Working (`:114`); `GET /api/agent-tasks?status=` and the `BlockedQuestion` attention row read `Status` only. CARD-0091's `d063f11e` was in all of them for five days as `Blocked`. | D-6: no listing learns about cards. The rows disappear because the task is settled; leftovers are the sweep's job. |
| 6. Existing open tasks on terminal cards need a one-off sweep. | Measured at 2026-09-26T00:50Z through `GET /api/agent-tasks?boardId=&status=Queued,Dispatched,Working,Blocked` over all 15 boards: 7 open non-specialist tasks (6 Dispatched, 1 Blocked), all bound, all on `InProgress` cards; `d063f11e` was hand-cancelled on 2026-09-25. The fleet summary is `byStatus: Dispatched 6, Blocked 1`. | D-8: the sweep is an endpoint with `apply` defaulting to false; today it would list nothing, so its tests are its evidence. |
| 7. Archive is a close. | `ArchiveAsync` keeps `Status`; the card's tasks stay bound and open; `UnarchiveAsync` is the undo. | D-3 treats archive like a close with the archive reason; unarchive resurrects nothing (retry exists). |
| 8. A task cannot be bound to a closed card. | `AgentTaskService.CreateAsync` has no terminal/archived guard on `CardId`; a `Mutation` task dispatched after a card's Done (the ordinary sequence in `docs/orchestration-loop.md` §7) is bound to a Done card and is legitimate work. | D-1 (hook, not sweep), and both the projection (D-7) and the sweep (D-8) restrict to tasks created before the close. |
| 9. Cancelling tells the caller. | A Canceled settle is a terminal settlement (`TaskCompletionNotification.Applies`, `:35`), so a session-replying task's parent gets the ordinary completion note; the reason is on the row (`AgentTaskSummaryDto.FailureReason`, `AgentTaskDtos.cs:394`) and in the Canceled event. | No new notification path. |
| 10. `CardService` can call the task service. | Only `CardWorkTransitionService` depends on `CardService`; nothing in `AgentTaskService`'s constructor graph (`AgentTaskService.cs:80-118`, `AgentSessionService` as `IDelegateSessionStopper`, `Program.cs:343`) does, so `CardService -> CardTaskSettlement -> AgentTaskService` is acyclic. Tests build `CardService` positionally with `null!` for the first seven arguments and named optionals after (`CardFilePrivacyPersistenceTests.cs:44` and five more), and `ExternalTrackerSyncService` with four positional arguments (`ExternalTrackerSyncIdentifierTests.cs:187`). | D-2: a trailing optional constructor parameter on both, null in harnesses, always supplied in production (all three services are scoped, `Program.cs:346`, `:359`, `:461`). |
| 11. The Linux lane runs these classes. | Build of `tests/Antiphon.Tests` to `bin-c738/` under a build slot: 4 min 03 s (`maxcpucount=6` granted). Single-method runs on that output, each including the Testcontainers Postgres start: `AgentTaskServiceIntegrationTests.cancelling_a_queued_task_settles_it` 1/1 green in 50 s; `AttentionServiceTests.A_needs_decision_card_is_a_critical_row_whose_evidence_is_the_move_reason` 1/1 green in 39 s. A method-level OR filter `/*/*/Class/(m1\|m2)` matched zero tests (exit 3); only the class-level combined syntax in `docs/testing-and-build.md` "Combined class filters (CARD-0403)" is usable. | Every new test lives in its own small class so each checkpoint filter is `/*/*/<Class>/*`; `EstimatedMinutes` below are 4 min build + about 1 min per small class. |
| 12. Inherited red on unchanged master. | `/*/*/AgentTaskCardBindingTests/*` at `0e83d3f2`: 19 executed, 5 passed, **14 failed**, every failure `ValidationException` from `AgentTaskService.CreateAsync` (`:854`, `workspace_default_not_git`): the class creates tasks through the service in a non-git `TempWorkspace` and CARD-0644 (`1a251b2d`, 2026-09-24) made the omitted workspace a Worktree that refuses a non-git directory. Not this card's; reported to the caller. | New tests seed `AgentTask` rows directly (the `AgentTaskCardBindingTests.SeedTaskAsync` and `AttentionServiceTests.Scenario` shape) and never call `CreateAsync`. |

## Decisions

Numbered D-1 to D-10, written under stated defaults; Code needs no further approval.

### D-1. A hook at the three terminal writers, not a sweep

The settlement runs inside the operation that closes the card, with that operation's reason and
actor, and only over the tasks that are open at that moment. Rejected: extending
`CardWorkTransitionService` (the durable-row sweep that already relates cards and bound tasks).
A sweep cannot tell a task that was open when the card closed from one dispatched afterwards
(row 8), so it would cancel a legitimate post-Done `Mutation` task on its next tick, and it would
be a second writer beside the close with no reason text to carry. The one-off sweep (D-8) is the
backfill and the outage recovery, under an explicit flag, which is what the card asks for.

### D-2. One orchestration seam: `CardTaskSettlement`

New scoped service `server/Application/Services/CardTaskSettlement.cs`, registered in
`Program.cs` beside `CardService`. Dependencies: `AppDbContext`, `AgentTaskService`, `IEventBus`,
`TimeProvider`, `ILogger<CardTaskSettlement>`. Two public methods:

- `Task<CardTaskSettlementResult> SettleClosedCardAsync(CardClosure closure, CancellationToken ct)`;
  `CardClosure(Guid CardId, string Identifier, Guid BoardId, CardClosureKind Kind, CardStatus Status, string Reason, DateTime At)`
  with `CardClosureKind { Closed, Archived }`. The result is
  `(IReadOnlyList<SettledTask> Canceled, IReadOnlyList<SettledTask> LeftOpen)`,
  `SettledTask(Guid TaskId, string ShortId, AgentTaskStatus StatusBefore, string Note)`.
- `Task<ClosedCardSweepDto> SweepAsync(bool apply, CancellationToken ct)` (D-8).

Call sites, each guarded by the condition its file already uses for terminal-only work:

- `CardService.MoveAsync`: after `SaveCardWriteAsync` and the review checkpoint, before the
  tracker push, when `targetColumn.IsTerminal && !wasTerminal`, with
  `CardClosureKind.Closed`, `card.Status`, `card.TerminalReason!`, `card.CompletedAt!.Value`.
  The result is returned on `MoveCardResult.TaskSettlement` (D-9).
- `CardService.ArchiveAsync`: after `SaveArchiveChangeAsync`, with `CardClosureKind.Archived`,
  `card.ArchivedReason!`, `card.ArchivedAt!.Value`. The `CardDto` return type is unchanged.
- `ExternalTrackerSyncService`: a `_pendingClosures` list next to `_pendingQueueRemovals`, appended
  when `MarkInactive` returns true, and when `UpdateExisting` moved a card whose column is now
  terminal and was not before; drained after the successful `SaveChangesAsync` at the same point
  the queue removals are published, cleared with them on the `DbUpdateException` path.

`CardService` and `ExternalTrackerSyncService` take `CardTaskSettlement? taskSettlement = null` as a
trailing optional constructor parameter (row 10). A null seam skips the hook; production DI
always supplies it. Rejected: calling `AgentTaskService` directly from `CardService` (the
which-tasks and started rules would then exist twice once the sweep lands); a static helper on
`CardLifecycleTransitions` (it needs the stopper, the event bus and the queue rows, not just the
context); a domain event (there is no in-process dispatcher for card events to subscribe to).

### D-3. Which bound tasks, and what happens to each

Candidates: `AgentTasks` where `CardId == closure.CardId`, `AgentTaskRoles.NotSpecialist`, and
`Status` is Queued, Dispatched, Working or Blocked, read `AsNoTracking` before any write.

- Queued, Blocked, and Dispatched-not-started (D-4): `AgentTaskService.CancelAsync(id, ct, reason)`
  with `reason = Card {Identifier} closed ({Status}): {Reason}` for a close and
  `Card {Identifier} archived: {Reason}` for an archive, clamped to 4000 characters. Blocked is
  cancelled whether or not its session is alive: the question it asked is moot once the card is
  closed, which is CARD-0091's exact shape. The primitive stops the session, so nothing here kills.
- Working, and Dispatched-started: untouched. One `AgentTaskEvent` of type `Warning` is written:
  `Card {Identifier} closed ({Status}: {Reason}) while this task was still working; it was not stopped. Cancel it, or reopen the card.`
  (archive wording accordingly), then `AgentTaskChanged` is published for the drawer. Exactly one
  per close; a card closed, reopened and closed again has been closed twice.
- Per task, failures are contained: `ConflictException` (settled by a concurrent writer between
  the read and the cancel) is skipped silently; any other exception is logged at Warning with the
  task and card, recorded on the result as `LeftOpen` with the message, and the loop continues.
  The card write has already committed; a task failure never rolls it back, and the sweep (D-8)
  finds what was missed.
- One Information line per closure:
  `Card {Identifier} {closed|archived}: cancelled {n} bound task(s) [{shortIds}]; left {m} started [{shortIds}]`.

### D-4. "Started" is the brief's own queue row, then the transcript, never the status

For a Dispatched task with `AgentSessionId`:

1. Load the brief row exactly as `AgentTaskDispatcher.FailNeverStartedAsync` does
   (`AgentTaskDispatcher.cs:1934-1943`): `SessionQueuedMessages` where
   `AgentSessionId == sid && Origin == Delegation && (DispatchedAt == null || CreatedAt >= DispatchedAt) && Body.Contains(TaskMarker(task.Id))`,
   first by `Sequence`. `Status == Sent` means started. `Status == Pending` means not started
   (CARD-0117 D7: the brief row outranks the transcript).
2. No brief row at all (launch still in progress, or a delivery path that queued nothing):
   `TranscriptPromptSpan.LoadAsync(_db, sid, task.DispatchedAt, ct).TurnPrompts.Count > 0` means
   started; otherwise not started.
3. No `AgentSessionId`: not started.

`Working` is always started. `Queued` and `Blocked` are never consulted. This is the conservative
direction on both errors: a brief typed into a composer that never acted is left running and
becomes an attention row a human cancels; a session killed under this rule had not received its
brief. No runner round trip (`CatchUpTranscriptAsync`) is made: a card close is not the
fail-and-kill judgement the dispatcher pulls the transcript for, and step 1 is local and primary.
The rule lives in one private method `HasStartedAsync` on `CardTaskSettlement`, used by the hook
and the sweep. Rejected: `SessionMessageQueueService.IsWorkingAsync` (a liveness verdict; an idle
delegate that has the brief is still work in progress, and AGENTS.md forbids a kill on a stall);
`TranscriptEntries.Any(...)` (the `NeverStarted` projection's predicate, which counts a Grok rules
prompt or a bootstrap prompt as a start).

### D-5. `CancelAsync` gains an optional reason; nothing else changes shape

`public async Task<AgentTaskSummaryDto> CancelAsync(Guid id, CancellationToken ct, string? reason = null)`.
With a reason: `task.FailureReason = reason` (clamped to 4000, the event clamp) and the Canceled
event detail is `Canceled: {reason}`. Without: today's behaviour exactly (`FailureReason`
untouched, detail `"Canceled."`). `FailureCode` stays null (no new enum member; the repeat guard
keys on codes, and a card close is not a launch failure class). The endpoint keeps calling
`CancelAsync(id, ct)`; no request body is added. Rejected: a second method `CancelForCardAsync`
(a second primitive, which the brief forbids); a new `AgentTaskEventType` (the Canceled event with
a reason in its detail is the existing shape the drawer renders).

### D-6. No listing filters by card state

`HomeTaskService`, `AgentTaskPipelineStatusService`, `GET /api/agent-tasks` and the
`BlockedQuestion` row keep reading `Status`. A hidden open row would be the defect's worse cousin:
the task would keep its session, its cost and its capacity seat while every view said it was gone.
The acceptance line "in-flight listings no longer show tasks whose card is closed" is met by
settling the rows (D-3) and by the sweep (D-8), and the survivors are shown louder, not hidden (D-7).

### D-7. `AttentionKind.CardClosedWhileWorking = 48`, projected first-match inside the open-task chain

`AttentionDtos.cs`: append `CardClosedWhileWorking = 48` with the summary "a task still open under
a card that has been closed or archived; not stopped by design, a human decides". In
`AttentionService.BuildOpenTaskItemsAsync`, one extra query before the loop: the closed or archived
cards among `open.Select(t => t.CardId)` (`Status` Done or Canceled, or `ArchivedAt != null`),
selecting `Id, Identifier, BoardId, Status, CompletedAt, TerminalReason, ArchivedAt, ArchivedReason`.
A new arm "4b" after `NeverStarted` (4) and before `BriefUndelivered` (5): the task's card is in
that set and `task.CreatedAt < closeAt` where `closeAt = CompletedAt ?? ArchivedAt`. The row:
Severity `Warning`; headline
`Card {Identifier} {closed ({Status})|archived} {Duration(now - closeAt)} ago; this task is still {task.Status} and was not stopped.`;
evidence `Evidence(TerminalReason ?? ArchivedReason ?? "(no reason recorded)", digest)`;
`SinceUtc = closeAt`; actions `[OpenCard, OpenDrawer, Cancel]`; `CardId`, `BoardId`;
`ConditionKey = card-closed:{task.Id:N}`. Specialist rows never carry a `CardId`, so the join
excludes them without a role filter.

Position: after `DeadSession` and `NeverStarted` because those mean the task is not actually
working and the dispatcher's own sweeps act on them (the projection must keep naming what the
sweep will fail, `AttentionService.cs:869-873`); before every later arm because the close is the
cause the human should read, not the symptom (`PastExpectedIdle`, `Overdue`, `ProgressStalled`).
The row is one per task by the chain's construction; the D-3 Warning event is the durable note on
the task. `AttentionSummaryCache` and `AttentionSummaryDto` need nothing. Client: add the member to
the `AttentionKind` union in `client/src/api/attention.ts`, an entry in `ATTENTION_VISUALS`
(`label: 'Card closed'`, `color: 'warning'`, hint "The card closed or was archived while this task
was still working. Cancel it, or reopen the card."), and the name in the lockstep list in
`attentionVisuals.test.ts`; `homeTasksModel.ts` ignores unknown kinds (`:160`) and is untouched.
Rejected: a persisted `AgentIncident` (incidents are agent supervision facts, and a projection
row that self-clears when the task settles is the CARD-0035 contract); severity Error (nothing is
broken; a decision is pending).

### D-8. The one-off sweep is `POST /api/agent-tasks/closed-card-sweep` with `apply` defaulting to false

`server/Application/Dtos/ClosedCardSweepDtos.cs`:
`ClosedCardSweepRequest(bool Apply = false)`;
`ClosedCardSweepDto(DateTime AsOf, bool Applied, IReadOnlyList<ClosedCardSweepRowDto> Rows, int CanceledCount, int LeftOpenCount)`;
`ClosedCardSweepRowDto(Guid TaskId, string ShortId, string Title, AgentTaskStatus Status, AgentTaskRole Role, Guid CardId, string CardIdentifier, Guid BoardId, CardStatus CardStatus, bool Archived, DateTime CardClosedAt, string? CardReason, bool CreatedAfterClose, bool Started, ClosedCardSweepAction Action, string? Note = null)`;
`ClosedCardSweepAction { Listed, Canceled, LeftOpenStarted, LeftOpenCreatedAfterClose, CancelFailed }`.

`CardTaskSettlement.SweepAsync(apply, ct)`: open non-specialist bound tasks joined to cards that
are Done, Canceled or archived, oldest first. With `apply == false` every row is `Listed` and
nothing is written. With `apply == true` the D-3 rule runs per row with the reason prefixed
`Closed-card sweep: `; started rows and rows created after the close are never touched and say
why (`LeftOpenStarted`, `LeftOpenCreatedAfterClose`); a failed cancel is `CancelFailed` with the
exception message in `Note` and a Warning in the log. The endpoint is declared in `AgentTaskEndpoints` next to
`/worktree-health` (before `/{id}`), body optional, `Results.Ok(await settlement.SweepAsync(...))`.
`docs/ops-http.md` gets one row and the one-liner:
`Invoke-RestMethod -Method POST "$api/api/agent-tasks/closed-card-sweep" -ContentType application/json -Body '{"apply":false}'`.
Rejected: a PowerShell script (it would need the cancel route to accept a reason and would
restate the predicate client-side); a hosted recurring sweep (that is the auto-cancel the card
forbids); a `GET` (a listing that can mutate under a flag must be a `POST`).

### D-9. The move reports what it settled; `card.ps1` prints it

`BoardDtos.cs`: `CardTaskSettlementDto(IReadOnlyList<string> Canceled, IReadOnlyList<string> LeftOpen)`
(short ids), and `MoveCardResult` gains
`[property: JsonIgnore(Condition = WhenWritingNull)] CardTaskSettlementDto? TaskSettlement = null`,
present only when a terminal move settled or left something (an empty settlement is omitted, so
an unaffected move's JSON is unchanged). `scripts/card.ps1` `move`/`close` branch: after
`Write-TrackerPushLine`, a `Write-TaskSettlementLine $result.taskSettlement` that prints
`tasks       canceled d063f11e` and `tasks       still working 2cc36aa6 (attention row; cancel or reopen)`
lines, nothing when the property is absent. `client/src/api/boards.ts` `MoveCardResult` gets the
optional `taskSettlement?` field for type honesty; no UI reads it. `ArchiveAsync` keeps returning
`CardDto` (changing it would change the archive JSON shape for the client and `card.ps1`); the
archive's settlement is on the task events and the server log.

### D-10. Docs

- `docs/agent-card-lifecycle.md`, after "Moving a card to a terminal column while a session is
  active stops the session and clears the claim.": a paragraph stating the D-3/D-4 rule, the
  attention kind, the sweep, and that reopen resurrects nothing.
- `docs/ops-http.md`: the sweep row (D-8) and, on the card move row, that a terminal move returns
  `taskSettlement`.
- `docs/orchestration-loop.md` §7 ("Close the card"): one sentence that closing cancels the
  card's unstarted delegates and leaves started ones with an attention row, so an orchestrator
  closes after its last delegate settles or expects a Canceled note.

## Slices

| Slice | Production files | Tests (new classes; every filter is one class) |
|---|---|---|
| S1 cancel reason | `server/Application/Services/AgentTaskService.cs` (D-5) | `tests/Antiphon.Tests/Application/AgentTaskCancelReasonTests.cs`: V-1 to V-3 |
| S2 settlement service and card hooks | `server/Application/Services/CardTaskSettlement.cs` (new, D-2/D-3/D-4), `server/Application/Services/CardService.cs` (ctor, `MoveAsync`, `ArchiveAsync`), `server/Application/Dtos/BoardDtos.cs` (D-9), `server/Program.cs` (registration), `client/src/api/boards.ts` (optional field) | `tests/Antiphon.Tests/Application/CardTaskSettlementTests.cs`: V-4 to V-10 |
| S3 tracker hook | `server/Application/Services/ExternalTrackerSyncService.cs` (D-2) | same class: V-11 |
| S4 attention kind | `server/Application/Dtos/AttentionDtos.cs`, `server/Application/Services/AttentionService.cs` (D-7), `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts`, `client/src/features/attention/attentionVisuals.test.ts` | `tests/Antiphon.Tests/Application/CardClosedAttentionTests.cs`: V-12 to V-15; vitest V-16 |
| S5 sweep | `server/Application/Dtos/ClosedCardSweepDtos.cs` (new), `CardTaskSettlement.cs` (`SweepAsync`), `server/Api/Endpoints/AgentTaskEndpoints.cs`, `docs/ops-http.md` (D-8) | `tests/Antiphon.Tests/Application/ClosedCardSweepTests.cs`: V-17, V-18; `ClosedCardSweepApiTests.cs`: V-19 |
| S6 script and docs | `scripts/card.ps1` (D-9), `docs/agent-card-lifecycle.md`, `docs/orchestration-loop.md`, `docs/ops-http.md` (D-10) | none automated; Review reads the diff and runs `card.ps1 close` against a throwaway card if it has a stack |

Order S1, S2, S3, S4, S5, S6; each slice is one commit for its tests (with the compile-only
skeleton the red row needs) and one for its production change. S3 and S4 are independent of each
other; S5 depends on S2.

## Verification design

### Harness and red-first discipline

- Every backend test class takes its own migrated schema (`TestDbFixture.CreateIsolatedSchemaAsync()`,
  the `AgentTaskCardBindingTests` and `DispatchHeldAttentionTests` shape), seeds `Board`,
  `BoardColumn` (Backlog, InProgress, Review, Done with `IsTerminal = true`), `Card`, `AgentTask`,
  `AgentSession`, `SessionQueuedMessage` and `TranscriptEntry` rows directly, and never calls
  `AgentTaskService.CreateAsync` (row 12).
- `AgentTaskService` is built as `AgentTaskCardBindingTests.CreateService` does, with a
  `RecordingSessionStopper` whose `Killed` list is the "was it stopped" assertion.
  `CardService` is built as the privacy tests do: `new CardService(db, null!, null!, null!, eventBus, TimeProvider.System, null!, taskSettlement: settlement)`;
  no test moves a card into an active column, so the null orchestrator and launch queue are
  never touched, and no assigned agent means `_reviewCheckpoints` is never called.
- `CardTaskSettlement` is built directly: `new CardTaskSettlement(db, taskService, eventBus, TimeProvider.System, NullLogger<CardTaskSettlement>.Instance)`.
- The attention class builds `AttentionService` the `DispatchHeldAttentionTests.ReadAllAsync` way
  (isolated schema, `FakeTimeProvider`, `FakeRunnerClient` listing the seeded session as Running so
  `DeadSession` does not win, one `TranscriptEntry` so `NeverStarted` does not win) and filters
  its assertions to the seeded task and card ids.
- The tracker test seeds a tracked board the `ExternalTrackerSyncIdentifierTests.SeedTrackedBoardAsync`
  way plus one `ExternalIssueRef` linked card and builds
  `new ExternalTrackerSyncService(db, [fake], new MockEventBus(), logger, taskSettlement: settlement)`
  with a `FakeIssueTracker` whose candidates are empty and whose `FetchByIdsAsync` returns `[]`, so
  the ref is stale and `MarkInactive` fires with the "no longer returned as active" reason.
- The API test uses `AntiphonWebAppFactory` (`CardAliasApiTests` shape) and posts to the route with
  `{}`; it asserts 200 and `applied: false`, nothing about rows (fleet-global, shared database).
- Red first: each `Sn-tests` commit carries compiling tests plus only what they need to compile
  and nothing that behaves: S1 the `reason` parameter accepted and unused; S2 the `CardTaskSettlement`
  class, DTOs and constructor parameters with `SettleClosedCardAsync` returning an empty result;
  S4 the enum member; S5 `SweepAsync` returning no rows. The red row must fail at the assertion
  the roster names (a status that is still Queued, a missing event, a missing row), never at a
  compile or fixture error. Nothing contacts server2's production runner, 17202 to 17205, a
  provider, or the desktop.

### Coverage roster and decisive assertions

| ID | Slice | Class.method | Decisive assertion (red on the skeleton, green after the slice) |
|---|---|---|---|
| V-1 | S1 | `AgentTaskCancelReasonTests.cancel_with_a_reason_records_it_on_the_row_and_in_the_canceled_event` | `FailureReason == reason`; the `Canceled` event detail is `"Canceled: " + reason`; `Status == Canceled`. Red: `FailureReason` null, detail `"Canceled."`. |
| V-2 | S1 | `AgentTaskCancelReasonTests.cancel_without_a_reason_keeps_the_bare_canceled_event` | `FailureReason` null, detail exactly `"Canceled."` (regression guard for the endpoint and every existing caller). Green throughout. |
| V-3 | S1 | `AgentTaskCancelReasonTests.cancel_with_a_reason_still_stops_the_delegate` | A Working task with a session: `stopper.Killed == [sessionId]` and `FailureReason == reason`. |
| V-4 | S2 | `CardTaskSettlementTests.closing_a_card_cancels_its_blocked_task_with_the_close_reason` | `MoveAsync` to the Done column with reason `"Live data change; nothing to deploy."`: task `Canceled`; `FailureReason == "Card CARD-0091 closed (Done): Live data change; nothing to deploy."`; the Canceled event detail contains `CARD-0091`; `stopper.Killed` contains its session; `result.TaskSettlement.Canceled == [shortId]`; the card row is `Done`. Red: task still `Blocked`, `TaskSettlement` null. |
| V-5 | S2 | `CardTaskSettlementTests.closing_a_card_cancels_queued_and_unstarted_dispatched_tasks` | A Queued task and a Dispatched task whose brief row is `Pending`: both `Canceled`; both in `TaskSettlement.Canceled`. |
| V-6 | S2 | `CardTaskSettlementTests.closing_a_card_leaves_started_tasks_running_with_one_warning_event` | A Dispatched task whose brief row is `Sent`, and a Working task: statuses unchanged; `stopper.Killed` empty; each has exactly one `AgentTaskEvent` of type `Warning` whose detail contains `CARD-0091` and `still working`; `TaskSettlement.LeftOpen` lists both. |
| V-7 | S2 | `CardTaskSettlementTests.closing_a_card_touches_no_other_cards_tasks_and_no_settled_task` | A Queued task on a second card stays Queued with no new event; a Succeeded task on the closed card stays Succeeded with no new event. Green throughout (control). |
| V-8 | S2 | `CardTaskSettlementTests.archiving_a_card_cancels_its_queued_task_with_the_archive_reason` | `ArchiveAsync(reason "Duplicate of CARD-0002")` on a Backlog card: task `Canceled`, `FailureReason == "Card CARD-0001 archived: Duplicate of CARD-0002"`; the returned `CardDto` has `ArchivedAt`. |
| V-9 | S2 | `CardTaskSettlementTests.a_move_to_review_settles_nothing_and_a_reopen_resurrects_nothing` | Move to Review: Queued task unchanged, `TaskSettlement` null. Then close (task Canceled), then `ReopenAsync`: task still `Canceled`, event count unchanged by the reopen. Red at the close step. |
| V-10 | S2 | `CardTaskSettlementTests.a_task_settled_by_a_concurrent_writer_is_skipped_and_the_move_still_succeeds` | Two Queued tasks; a test `IDelegateSessionStopper` that, when asked to kill the first task's session, marks the second task Succeeded through a second context. `MoveAsync` returns normally; first task Canceled; second task Succeeded; `TaskSettlement.Canceled` has one entry and `LeftOpen` is empty. |
| V-11 | S3 | `CardTaskSettlementTests.a_tracker_stale_close_cancels_the_cards_queued_task` | After `SyncAsync(board)`: the linked card is in the terminal column with `TerminalReason == "External tracker issue is no longer returned as active."`; the bound Queued task is `Canceled` with `FailureReason == "Card CARD-0001 closed (Done): External tracker issue is no longer returned as active."`. Red: card terminal, task still Queued. |
| V-12 | S4 | `CardClosedAttentionTests.a_working_task_on_a_closed_card_is_one_warning_row` | Done card (`CompletedAt` 2 d ago, `TerminalReason` set), Dispatched task created 3 d ago with a Running session and one transcript entry: exactly one item for the task; `Kind == CardClosedWhileWorking`, `(int)Kind == 48`, `Severity == Warning`, `CardId`, `BoardId`, `SinceUtc == CompletedAt`, evidence contains the terminal reason, `Actions == [OpenCard, OpenDrawer, Cancel]`, `ConditionKey == $"card-closed:{task:N}"`. Red: no item of that kind. |
| V-13 | S4 | `CardClosedAttentionTests.a_task_created_after_the_close_is_not_listed` | Same card; task `CreatedAt` after `CompletedAt`: no `CardClosedWhileWorking` item for the task. Green throughout (control). |
| V-14 | S4 | `CardClosedAttentionTests.an_archived_live_card_counts_as_closed` | Backlog card with `ArchivedAt` 1 d ago and `ArchivedReason`: one row, headline contains `archived`, `SinceUtc == ArchivedAt`. |
| V-15 | S4 | `CardClosedAttentionTests.an_open_task_on_an_in_progress_card_is_not_listed` | InProgress card: no `CardClosedWhileWorking` item. Green throughout (control). |
| V-16 | S4 | `client/src/features/attention/attentionVisuals.test.ts` (`maps every kind`, `lands every kind in a declared group`, key-list lockstep) | The union, the visuals map and the lockstep list all carry `CardClosedWhileWorking`; `tsc` in the vitest run rejects a union member without a visuals entry. |
| V-17 | S5 | `ClosedCardSweepTests.the_sweep_lists_open_tasks_on_closed_and_archived_cards_and_changes_nothing` | Seed: Queued on a Done card, Blocked on an archived card, Dispatched-started (brief `Sent`) on a Done card, Queued created after its card's close, Queued on an InProgress card. `SweepAsync(apply: false)`: 4 rows for the seeded ids (not the control), `Applied == false`, every `Action == Listed`, `CreatedAfterClose` true on the fourth, `Started` true on the third; no status changed. Red: 0 rows. |
| V-18 | S5 | `ClosedCardSweepTests.apply_cancels_only_unstarted_rows_created_before_the_close` | Same seed, `apply: true`: the Queued and Blocked rows are `Canceled` with `FailureReason` starting `Closed-card sweep: Card`; the started row is `LeftOpenStarted` and untouched (stopper not called for it); the after-close row is `LeftOpenCreatedAfterClose` and still Queued; `CanceledCount == 2`, `LeftOpenCount == 2`. |
| V-19 | S5 | `ClosedCardSweepApiTests.the_sweep_route_answers_with_a_preview_by_default` | `POST /api/agent-tasks/closed-card-sweep` with `{}`: 200, body `applied == false`, `rows` an array. |

### Execution and evidence

Builds go to `bin-c738-r1/` (forward slash); every `run-checkpoint.ps1` row on server2 gets
`UseAppHost=false` by default (CARD-0671). Rows CP-1 to CP-11 are run in order after their
`After` slice is committed; a red row must fail at the roster's named assertions, not at a fixture
or compile error. Regression rows CP-10 and CP-11 are class-level combined filters (the only OR
syntax that matched, row 11); CP-11 sets `TUNIT_MAX_PARALLEL_TESTS=1` on server2 (Preserved
Gotcha #26). The vitest row is `pwsh -File scripts/test-client.ps1 attentionVisuals`.
Delete `bin-c738-r1/` directories (about a dozen, one per project) before the report.

### Cost

Six slices, about 25 to 45 minutes of authoring each. The ordinary checkpoint floor is the sum of
`EstimatedMinutes`: **93 minutes**. Suggested `-ExpectAbout`: 300 minutes. No live canary, no
broad suite, no Mutation in this profile.

### Checkpoints

The closed list for Code. `Sn-tests` means the slice's compiling red tests (with the compile-only
skeleton named above) are committed before its production change; `Sn` means the production
change is committed. Every row rebuilds into the same `bin-c738-r1/` path except where `Build`
says `CP-n`, which reuses that row's output with `--no-build` (same `After`). Backslashes before
`|` are Markdown escaping only.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c738-r1/` | cancel-reason-red | `/*/*/AgentTaskCancelReasonTests/*` | V-1 to V-3 | 3 executed; V-1 fails at its `FailureReason` assertion; V-2, V-3 pass | 3 | 6 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c738-r1/` | cancel-reason-green | `/*/*/AgentTaskCancelReasonTests/*` | V-1 to V-3 | all listed, 0 failed/skipped | 3 | 6 |
| CP-3 | S2-tests, S3-tests | `tests/Antiphon.Tests -> bin-c738-r1/` | settlement-red | `/*/*/CardTaskSettlementTests/*` | V-4 to V-11 | 8 executed; V-4, V-5, V-6, V-8, V-9, V-10, V-11 fail at their status/event assertions; V-7 passes | 8 | 8 |
| CP-4 | S2, S3 | `tests/Antiphon.Tests -> bin-c738-r1/` | settlement-green | `/*/*/CardTaskSettlementTests/*` | V-4 to V-11 | all listed, 0 failed/skipped | 8 | 8 |
| CP-5 | S4-tests | `tests/Antiphon.Tests -> bin-c738-r1/` | attention-red | `/*/*/CardClosedAttentionTests/*` | V-12 to V-15 | 4 executed; V-12, V-14 fail (no row of kind 48); V-13, V-15 pass | 4 | 7 |
| CP-6 | S4 | `tests/Antiphon.Tests -> bin-c738-r1/` | attention-green | `/*/*/CardClosedAttentionTests/*` | V-12 to V-15 | all listed, 0 failed/skipped | 4 | 7 |
| CP-7 | S4 | n/a | client-visuals | `pwsh -File scripts/test-client.ps1 attentionVisuals` | V-16 | attentionVisuals.test.ts: all tests pass, exit 0 | n/a | 3 |
| CP-8 | S5-tests | `tests/Antiphon.Tests -> bin-c738-r1/` | sweep-red | `/*/*/ClosedCardSweepTests/*` | V-17, V-18 | 2 executed; both fail at their row-count assertions | 2 | 6 |
| CP-9 | S5 | `tests/Antiphon.Tests -> bin-c738-r1/` | sweep-green | `/*/*/(ClosedCardSweepTests*)\|(ClosedCardSweepApiTests*)/*` | V-17 to V-19 | all listed, 0 failed/skipped | 3 | 9 |
| CP-10 | all | CP-9 | card-and-cancel-regression | `/*/*/(CardServiceTrackerPushTests*)\|(CardWorkTransitionServiceTests*)\|(AgentTaskServiceIntegrationTests*)/*` | S1, S2 wiring through real DI (`CardServiceTrackerPushTests` resolves `CardService` from a container) and every existing cancel path | >= 108 executed, 0 failed (6 + 12 + 90 source methods; `[Arguments]` expansion may raise the count) | 108 | 12 |
| CP-11 | all | CP-9 | attention-regression | `/*/*/AttentionServiceTests/*` with `TUNIT_MAX_PARALLEL_TESTS=1` | S4's extra query changes no existing row | >= 132 executed, 0 failed (122 + 10 source methods across the two partial files) | 132 | 21 |
