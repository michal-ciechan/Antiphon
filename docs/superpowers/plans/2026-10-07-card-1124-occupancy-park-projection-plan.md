# CARD-1124: project Blocked and Queued owners as owned seats and expose park state on the slot DTO

Date: 2026-10-07. Plan task: `559f3ef6-4f4a-45dc-be03-0c355801ce18`.
Inspected source: `f4ae747a6b8ab6f8f8aeee025cfbd0bcafdeb1b4` (`origin/master` at planning time;
CARD-1065 S1-S10, CARD-1079, CARD-1108 S1, CARD-1111 and CARD-1112 landed).
Assigned branch: `feat/card-task-559f3ef6`; fast-forward-only from `5b713f685`, fast-forwarded to
the inspected source before any citation below was taken.
Status: Plan and verification design complete under the defaults in `## Decisions`; the brief
asked for the Checkpoints table, negative controls and regression classes in this artifact, so
test design is folded here and **next: code** (S1).
Platform read before this plan: `GET /api/runner-defaults` global runner `server2`, no kind
defaults; `GET /api/session-runners` shows `server2` (Linux, 0/10) draining and not accepting,
`server2-temp` (Linux, 5/10) accepting, `desktop` (Windows, 0/2) accepting. No runner pin and no
platform pin: every change is server-side C# and Markdown, and every checkpoint row runs on
the Linux lane.

## Outcome and scope

CARD-1065 D-8 ("truthful state/occupancy") said a seat whose desktop session is live and whose
task is Blocked or Queued is owned, not orphaned, and that the slot DTO carries park state. S10
shipped the opposite as documentation (CARD-1112): `OpenTaskId` admits only Dispatched or
Working, so a live Blocked child reads `orphan=true` on `GET /api/session-runners/{id}/slots`,
is counted in `orphanSlots`, gets an immediate `SlotOrphan` attention row, and is what the
orchestrator instruction tells a session at capacity to count "before concluding the host is
busy". While parking is off (the default) that child keeps its seat until it is answered or
cancelled, so the count overstates reclaimable seats and points an operator's single-seat
release at a live process.

This plan takes the card's first option: implement CARD-1065 D-8 as a read-only projection.

1. The owner task is the latest task bound to the session whose status is Queued, Dispatched,
   Working or Blocked. `IsOrphan` keeps its signature and truth table; what changes is the
   owner fact fed to it.
2. The slot DTO gains `park`: the owner's current-attempt park episode (id, state, reason
   code, release id, sync state) or null. Null is the ordinary reading while parking is off.
3. The occupancy sampler's seat observation carries the same park state, so `SeatIdle` and
   `SlotOrphan` evidence names it. A live Blocked owner is a `SeatIdle` concern from the
   warning age, not an immediate `SlotOrphan` row.
4. The owner docs, the orchestrator skill and the CARD-1065 and CARD-1079 plans are rewritten
   to say what the code now does, with the `c1065-*` pins flipped in the same commits and a
   negative pin that the retired sentence is gone.

Nothing here releases, stops, publishes or writes a session, task or park row. Parking and
the reclaim sweep stay default-off and untouched: no file in the publication, release or
reclaim path changes, and the CARD-1065 D-3 publication gate is not read by this projection.
A Working session is never touched. The join issues the same nine reader statements before
and after. No migration, no new setting, no client change. No production session, card, runner
budget or deployment setting was changed during this Plan; no test or build ran except the
checkpoint importer's table validation described under `### Checkpoints`.

## Ground truth

| # | Card assumption / requested behavior | Code at `f4ae747a6` | Design consequence |
|---|---|---|---|
| 1 | `SeatDesktopJoin` sets `OpenTaskId` only for a Dispatched or Working task. | Confirmed. `server/Application/Services/SeatDesktopJoin.cs:54-56` filters `scoped.Where(t => t.Status == Dispatched \|\| t.Status == Working)`; the doc comments at `:20-21` and `:34` state the rule. | S1 widens the predicate to Queued, Dispatched, Working and Blocked and rewrites both comments (D-1). |
| 2 | `IsOrphan(live, openTask, pooledWarm)` is `!pooledWarm && (!live \|\| !openTask)`, so a live Blocked or Queued owner reads `orphan=true`. | Confirmed. `RunnerSlotService.cs:29-30`; fed at `:43` (slots route), `:134` (reconcile claimed check) and `SeatOccupancySampler.cs:122`. | Signature and truth table unchanged (`RunnerSlotRulesTests` stays green); only the `openTask` argument's source changes (D-1). |
| 3 | The slot DTO has no park field. | Confirmed. `server/Application/Dtos/RunnerSlotDtos.cs:4-15` ends at `Guid? OpenTaskId`. The private `DesktopRow` at `RunnerSlotService.cs:401` carries Live, Status, OpenTaskId, PooledWarm. | S1 adds `RunnerSlotParkDto? Park = null` as a trailing defaulted parameter, so existing constructor calls compile and non-parked seats serialise `park: null` (D-2). |
| 4 | The owner doc pins the opposite of D-8 on purpose. | Confirmed. `docs/session-runtime-invariants.md:81` and `:83`; pinned by `tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs:19` (`c1065-orphan-owner`) and `:41-42` (`c1065-join-open-task`). | S1 rewrites both sentences and both pins in the same commit and adds a negative pin on the retired phrase (D-6). |
| 5 | The orchestrator instruction tells a session at capacity to "count orphan=true". | Confirmed. `.claude/skills/antiphon-orchestrator/SKILL.md:25` and `docs/orchestration-loop.md:789`; pinned at `BlockedTaskParkProjectionTests.cs:82` (`c1065-loop-orphan`) and `:103` (`c1065-orphan-count`). | S3 rewrites both sentences so `orphan=true` is never read as a count of free seats, and flips the two pins (D-6). |
| 6 | CARD-1079 reads the same orphan flag. | Confirmed. `SeatOccupancySampler.cs:117-124` counts `orphans` into `OrphanSlots`; `:134` stores `Orphan` on the seat; `AttentionService.Seats.cs:72` emits `SlotOrphan` for `seat.Orphan && seat.Occupies`. `SeatIdle` does not read it: `IsDesktopIdle` at `SeatOccupancySampler.cs:181-183` uses the latest task status, and `Classify` at `SeatOccupancyProjection.cs:35-41` maps Blocked to `IdleBlocked`. | A live Blocked owner stops producing `SlotOrphan` and `orphanSlots`; its `SeatIdle` row, `IdleBlocked` class, idle clock and Reply/Cancel actions are unchanged (D-4). |
| 7 | A park's state is readable without a new statement. | The join already queries `AgentTaskParks` once for the latest tasks (`SeatDesktopJoin.cs:99-102`) but only to compute `PublicationReceipt`, filtered on `PublicationReceiptId != null`. `AgentTaskPark` (`server/Domain/Entities/AgentTaskPark.cs`) carries `State` (`:43`), `ReasonCode` (`:45`), `RunnerSeatReleaseId` (`:37`), `SyncState` (`:60`), `CreatedAt` (`:53`); unique index `(TaskId, Attempt, BlockEventId)` at `AppDbContext.cs:151`. Task detail already selects one park per `(TaskId, Attempt)` by `CreatedAt desc, Id desc` at `AgentTaskService.cs:2560-2565`. | Widen that one query's projection and drop the receipt filter; compute receipt and park in memory; mirror the task-detail ordering (D-2, D-3). |
| 8 | The projection is read on a CPU-constrained desktop every tick. | `SeatOccupancyHostedService.cs:22-23` ticks every `Attention:OccupancySampleIntervalSeconds` (60). Per runner host `SampleRunnerAsync` issues three desktop queries (`SeatOccupancySampler.cs:68-78`), one inventory RPC (`:80`) and the join (`:101`); the join issues nine reader statements (see `## Statement budget`). | Statement count stays nine per join and twelve per runner host per tick; a `CountingCommandInterceptor` pin guards it (D-3, V-2). |
| 9 | An orphan flag could lead to a release. | The single-seat release (`RunnerSlotService.cs:64-71`) ignores `orphan`. The sweep (`:73-82`) delegates to `TerminalRunnerSeatReleaseService.ReleaseOrphansAsync` (`TerminalRunnerSeatReleaseService.cs:149-166`), which returns nothing unless `AutomaticEnabled` and classifies ownership itself (`DiscoverCandidateAsync` `:242-291`; a Blocked task goes to `TryHandleTaskAsync` at `:268-273`), never reading `IsOrphan`. The reconcile job reads `IsOrphan` only to mark an orphan-sweep intent `failed:` when the seat was claimed since (`RunnerSlotService.cs:130-138`); no caller passes `fromOrphanSweep: true` today, so this arm serves pre-existing `pending:orphan:` intents only. | The orphan flag is display and selection, never authority. Widening the owner set can only turn more intents into `failed:` (fail-closed) and fewer seats into orphans (D-5; see `## Can the orphan change mislead an operator?`). |
| 10 | A Queued task can own a seat. | A dispatch binds `AgentSessionId` and sets `Dispatched` in one unit of work (`AgentTaskDispatcher.cs:5161-5182`); a retry clears the binding when it requeues (`AgentTaskService.cs:3426-3436`). Queued-with-session is therefore historical or exceptional, and `AgentTaskService.IsSettled` (`:3973-3974`) treats Queued, Dispatched, Working and Blocked alike as open. | Include Queued in the owner set as D-8 asked: a seat about to be used is not an orphan, and the predicate matches `IsSettled` rather than `MaxOpenTasks`. V-1 covers the arm (D-1). |
| 11 | Existing tests pin the current reading. | `SeatDesktopJoinTests.cs:129`, `:169` (`OpenTaskId.ShouldBeNull()` for Blocked latest tasks) and `:182-255` (`C1079_Join_open_task_is_dispatched_or_working_only`); `SeatOccupancySamplerTests.cs:46` (`OrphanSlots` 2) and `:82` (`blocked.Orphan` true); `SeatOccupancyProjectionTests.cs:63-66` calls `IsOrphan` with literal arguments (unchanged). `SeatOccupancyAttentionTests` builds snapshots by hand (`:287-305`), so it only needs the new record fields. | S1 and S2 rewrite those assertions into the new behaviour; the replaced method is renamed `C1124_*` and the CARD-1079 plan gets a dated amendment (D-6). |
| 12 | The orphan count is wrong only while parking is off. | With parking on, a confirmed park stops the desktop session, so `Live` is false and `orphan=true` is right. With parking off (`BlockedTaskParkingOptions.Enabled` false by default), the Blocked child is live and seated. | The fix is independent of the parking switch and reads none of them; `park` is null for the common off case and populated only when a park row exists (D-7). |

## Decisions

### D-1. Implement CARD-1065 D-8: the owner task is Queued, Dispatched, Working or Blocked

`SeatDesktopJoin.LoadAsync` computes `OpenTaskId` as the latest task bound to the session
whose status is Queued, Dispatched, Working or Blocked, which is exactly the complement of
`AgentTaskService.IsSettled`. The field keeps its name: "open" already means unsettled
everywhere else (`IsSettled`, MaxOpenTasks' exclusion of Blocked is a budget rule, not an
ownership rule). `RunnerSlotService.IsOrphan` keeps its three-argument signature and truth
table; the slots route, the reconcile check and the sampler keep calling it with
`row.OpenTaskId is not null`.

Rejected: retiring D-8 and rewriting only the instruction sentence (the card's second option).
That leaves `orphanSlots`, `SlotOrphan` rows and `scripts/runner-slots.ps1 list` pointing
operators at live Blocked children while parking is off, and the card's "Why it matters" is a
live operational hazard, not a documentation nit. Rejected: a second `OwnerTaskId` beside
`OpenTaskId`; no consumer needs the narrower field and two overlapping ids on the wire would
need their own doc sentence forever. Rejected: a Blocked-only widening without Queued; D-8
named both, the predicate then matches `IsSettled` exactly, and a Queued-bound seat (ground
truth 10) is about to be used, so calling it an orphan is the unsafe direction.

### D-2. `park` on the slot DTO: the owner's current-attempt episode, display-only

`RunnerSlotDto` gains `RunnerSlotParkDto? Park = null` as a trailing defaulted parameter;
`RunnerSlotParkDto(Guid ParkId, string State, string ReasonCode, Guid? ReleaseId, string
SyncState)`. The row is the `AgentTaskPark` with `TaskId == owner.Id && Attempt ==
owner.Attempt`, newest `CreatedAt` then greatest `Id`, mirroring the task-detail `parkSync`
selection at `AgentTaskService.cs:2560-2565`. It is null when there is no owner, or no park
row for the owner's current attempt (a historical attempt's park is not this seat's park).
`SeatDesktopRow` carries the same facts as `Park` (a `SeatParkRow` record) and keeps
`PublicationReceipt`, now computed in memory as "any park row for `(owner.Id, owner.Attempt)`
has a receipt id". The DTO carries no path, ref, SHA, report reference, digest or answer text
(CARD-1065 G-186).

Rejected: reusing `TaskParkSyncDto` (sync-only shape, no custody state). Rejected: embedding
the entity. Rejected: a park field keyed on `LatestTask` when it differs from the owner; the
seat belongs to the owner, and the two differ only when a settled task is newer than an open
one, in which case the open one is the seat's business.

### D-3. One join, nine statements before and after

The park query at `SeatDesktopJoin.cs:99-102` drops its `PublicationReceiptId != null` filter
and selects `Id, TaskId, Attempt, State, ReasonCode, RunnerSeatReleaseId, SyncState,
CreatedAt, PublicationReceiptId != null` for the latest tasks' ids. Receipt and park are then
computed in memory per session. No query is added, none is issued per session, and no
navigation is introduced (the entity has no cascading foreign keys by design). The row set
grows only by the latest tasks' unpublished park rows, which are bounded by attempts times
block events per task. Rejected: a second statement for park state (ten per join, thirteen per
runner host per tick on the desktop); rejected: a `GROUP BY` subquery for the newest row (one
statement either way, and the in-memory pick is the task-detail rule already in the codebase).

### D-4. Seat observations carry park state; a live Blocked owner is `SeatIdle`, not `SlotOrphan`

`SeatObservation` gains `string ParkState` (`none` or the `AgentTaskParkState` name) and
`string? ParkReason`. `SeatEvidence` and `OrphanEvidence` append `park=<state>` or
`park=<state>:<reason>`; `park=none` when there is no row. `OrphanSlots` and the `SlotOrphan`
builder are unchanged in code and now exclude live Blocked and Queued owners because the
flag they read excludes them. The `IdleBlocked` class, idle clock, severity ladder, Reply and
Cancel actions and `SeatIdle` key are unchanged, so an unanswered Blocked child still becomes
a Warning at `SeatIdleWarningMinutes` and an Error at `SeatIdleErrorMinutes`.

Consequence recorded against CARD-1079 D-7: the "immediate `SlotOrphan` for an idle Blocked
seat" was a false positive of the predicate CARD-1079 D-2 took as given, one day before
CARD-1065 D-8 called it wrong. Rejected: a zero-threshold `SeatIdle` for Blocked seats (a new
alert policy the card did not ask for; the thresholds are operator settings). Rejected: a
Blocked special case inside `BuildSlotOrphanItems` (reintroduces the false positive under
another name). Rejected: persisting park state in `HostOccupancySample` (counters only; no
migration in this card).

### D-5. The reconcile "claimed since the sweep" check keeps `IsOrphan`

`ReconcilePendingReleasesAsync` (`RunnerSlotService.cs:130-138`) re-reads the seat and marks
an orphan-sweep intent `failed:` when the seat is no longer an orphan. With D-1, a seat whose
owner is now Blocked or Queued is claimed, the intent fails and the desktop row is left alone.
That is the fail-closed direction: the only thing this arm can do is decline to audit a seat as
released. No producer passes `fromOrphanSweep: true` today, so the arm serves intents recorded
before the sweep moved to `TerminalRunnerSeatReleaseService`; it is tested because it is
reachable from stored data.

### D-6. Docs and pins move with the code, and the retired sentence is pinned absent

Each slice rewrites the sentences its behaviour change falsifies and flips the corresponding
`c1065-*` pins in the same commit (the CARD-1108 convention). `BlockedTaskParkProjectionTests`
also gains `ShouldNotContain` pins on the retired phrases "remains orphan=true" (owner doc)
and "count orphan=true" (instruction text), so a later doc edit cannot quietly restore the old
reading. The CARD-1065 plan's D-8 section, S10 row, V-25, G-184/PC-184 and CP-10 and the
CARD-1079 plan's D-2, D-7, V-6, V-7 and V-8 each get one dated amendment line pointing here.
Rejected: renaming the `c1065-*` labels to `c1124-*`; the labels are what Review greps for,
and the card asked that they stay truthful, not that they move.

### D-7. Default-off, inert, read-only, no gate change

No new setting. The projection reads neither `BlockedTaskParkingOptions` nor
`TerminalRunnerSeatReleaseOptions`; it reads rows. No file in `TaskParkPublicationService`,
`TerminalRunnerSeatReleaseService`, `BlockedTaskParkingService`, `AgentTaskReplyService` or
the dispatcher changes, so the CARD-1065 D-3 publication gate, the Working veto and the
reclaim sweep are untouched by construction and proven unchanged by R-7. The slots GET and the
sampler tick write nothing to sessions, tasks or parks; V-3 pins that by comparing row state
before and after and by counting runner release requests (zero).

### D-8. `orphanSlots` changes meaning without a migration

Samples stored before this change counted live Blocked owners; samples after it do not. The
column, entity and route are unchanged; the API doc records the cutover sentence so a reader
of `GET /api/hosts/{hostId}/occupancy-samples` across the activation boundary is not misled.
Rejected: a migration that rewrites historical counters (it would invent data).

### D-9. Checkpoints on the Linux lane, one class family per row, final set at the landed SHA

Every row is server-side TUnit in `tests/Antiphon.Tests`. `RunnerSlotEndpointTests` boots
`PhoneHomeTestHost` and runs `Serial`; `TerminalRunnerSeatReleaseTests` and
`RunnerSeatOrphanSweepTests` share one row and run `Serial` as CARD-1108 did. Isolated-schema
classes run beside each other. The final rows rebuild once at the last commit and rerun every
touched class plus the two release-path regression classes.

## Can the orphan change mislead an operator?

No. The change moves `orphan` in the operator-safe direction and leaves every release path's
authority where it was.

- Today a live Blocked child reads `orphan=true`. The instruction text tells a session at
  capacity to count those seats before concluding the host is busy, `scripts/runner-slots.ps1
  list` prints them beside dead seats, and an operator who runs `release` on one kills a live
  process whose task is waiting for an answer. That is the misleading reading, and it is the
  one being removed.
- After this plan `orphan=true` means exactly: the runner remembers a session with no live
  desktop row, or a live row with no owner task in Queued, Dispatched, Working or Blocked,
  excluding warm pool delegates. Those were already the only seats a release could sensibly
  target; none of them is newly flagged and none is newly hidden.
- The new false-negative class is empty. A seat reads `orphan=false` only with a live desktop
  row and an open owner task. If the runner process behind it is dead the runner lists it
  `Exited` or drops it, so `occupiesCapacity` is false and it is counted nowhere. If the
  desktop row is live and the process is alive, the seat is live by definition, and the right
  verbs are Reply or Cancel on the task, which the `SeatIdle` row offers.
- No release reads the flag as authority (ground truth 9). The single-seat release ignores it;
  the sweep classifies ownership itself behind `AutomaticEnabled`; the reconcile arm can only
  turn more intents into `failed:` and never fewer.
- What an operator loses is the immediate `SlotOrphan` row for a live Blocked child; what they
  keep is the `SeatIdle` row from the warning age with the same Reply and Cancel actions, now
  with `park=` in its evidence, plus `openTaskId` and `park` on the slot itself. The rewritten
  instruction says to read those rather than count orphans.

## Statement budget

Reader statements per `SeatDesktopJoin.LoadAsync` call with every stage populated, counted at
`f4ae747a6` by reading the code (`CountingCommandInterceptor` captures reader commands; the
join issues no non-reader command):

| # | Statement | Lines today | After S1 |
|---|---|---|---|
| 1 | sessions by id | `SeatDesktopJoin.cs:47-50` | unchanged |
| 2 | latest-task peaks (`GROUP BY`, `MAX`) | `:154-157` via `:53` | unchanged |
| 3 | latest-task candidates | `:170-184` via `:53` | unchanged |
| 4 | open-task peaks | `:154-157` via `:54-56` | same query, predicate widened |
| 5 | open-task candidates | `:170-184` via `:54-56` | same query, predicate widened |
| 6 | Blocked events for latest tasks | `:62-65` | unchanged |
| 7 | boards for the latest tasks' cards | `:78-81` | unchanged |
| 8 | pooled-warm agents | `:84-90` | unchanged |
| 9 | parks for latest tasks | `:99-102` | same query, filter dropped, projection widened |

Nine before, nine after. Per runner host per sampler tick: three desktop queries
(`SeatOccupancySampler.cs:68-78`) plus the nine, twelve before and after, plus one inventory
RPC; the tick's two writes (`:45` prune, `:48` sample insert) touch `HostOccupancySamples`
only and are unchanged. Per slots GET: nine before and after. V-2 pins the join at exactly nine
for a fully populated fixture; PC-6 shows a per-session park load breaks it.

## Implementation slices

Slices are sequenced; S2 uses S1's row fields. Each slice commits its docs and pins with its
code and ends with its checkpoint rows green at that commit.

| Slice | Files and change | Tests, rows, minutes |
|---|---|---|
| S1 (D-1, D-2, D-3, D-5, D-7) | `server/Application/Services/SeatDesktopJoin.cs`: widen the predicate at `:54-56` to Queued/Dispatched/Working/Blocked; replace the park query at `:96-105` with the D-3 projection; add `SeatParkRow(Guid ParkId, AgentTaskParkState State, string ReasonCode, Guid? ReleaseId, AgentTaskParkSyncState SyncState)` and `SeatDesktopRow.Park`; compute receipt and park in memory keyed on the owner (`open`) task; rewrite the doc comments at `:8`, `:19-22`, `:32-37`. `server/Application/Dtos/RunnerSlotDtos.cs`: add `RunnerSlotParkDto` and `RunnerSlotDto.Park = null`. `server/Application/Services/RunnerSlotService.cs`: `DesktopRow` at `:401` gains `Park`; `LoadDesktopAsync` at `:387-399` copies it; `ListAsync` at `:47-58` passes it; `IsOrphan` doc comment at `:25-28` names the four owner statuses. Docs in the same commit: `docs/session-runtime-invariants.md:81` and `:83` (sentences 1 and 2 below); `docs/ops-http.md:137` (sentence 4); `BlockedTaskParkProjectionTests.cs:6-9` header, `:19` pin, `:41-42` pin, new negative pin and new `c1124-*` pins (sentences 1, 2, 4). | `SeatDesktopJoinTests`: `:129` and `:169` become `ShouldBe(newerTask)` / `ShouldBe(staleTask)`; replace `C1079_Join_open_task_is_dispatched_or_working_only` with `C1124_Join_owner_task_is_queued_dispatched_working_or_blocked` (V-1); new `C1124_Join_reports_the_current_attempt_park_in_nine_statements` (V-2). `RunnerSlotEndpointTests`: new `C1124_Slots_mark_a_live_blocked_owner_owned_and_carry_its_park` (V-3), new `C1124_Reconcile_fails_an_orphan_sweep_intent_claimed_by_a_blocked_owner` (V-4); `SeedOpenTaskAsync` at `:406-431` gains an optional attempt. CP-1..CP-4. 38 author + 14 check = 52 min. |
| S2 (D-4, D-8) | `server/Application/Services/SeatOccupancySnapshot.cs:36-55`: `SeatObservation` gains `string ParkState, string? ParkReason` after `Pushed`. `server/Application/Services/SeatOccupancySampler.cs:125-147`: pass `row.Park?.State.ToString() ?? "none"` and `row.Park?.ReasonCode`. `server/Application/Services/AttentionService.Seats.cs:124-140`: append `park=` to `SeatEvidence` and `OrphanEvidence` through one `ParkField(seat)` helper. Docs in the same commit: `docs/ops-http.md:164` (sentence 5); `docs/antiphon-api.md:577-580` (sentence 6); `docs/agent-card-lifecycle.md:12-13` (sentence 7); pins for 5, 6, 7. | `SeatOccupancySamplerTests`: `:46` becomes `OrphanSlots.ShouldBe(1)`, `:82` becomes `Orphan.ShouldBeFalse()`; new `C1124_Blocked_owner_is_idle_not_orphan_and_carries_its_park` (V-5). `SeatOccupancyAttentionTests`: `Seat` helper at `:287-305` gains `parkState = "none", parkReason = null`; new `C1124_Seat_evidence_names_the_park_state` (V-6). CP-5..CP-7. 30 author + 11 check = 41 min. |
| S3 (D-6) | `.claude/skills/antiphon-orchestrator/SKILL.md:25` (sentence 8); `docs/orchestration-loop.md:789` (sentence 9); `docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md` amendments at `:344` (D-8), `:501` (S10), `:890` (V-25), `:1153` (G-184), `:1403` (PC-184), `:1491` (CP-10); `docs/superpowers/plans/2026-10-06-card-1079-seat-idle-occupancy-attention-plan.md` amendments at `:44` (ground truth), `:76-84` (D-2), `:121-127` (D-7), `:269-271` (V-6, V-7, V-8); `BlockedTaskParkProjectionTests.cs:82` and `:103` pins flipped, negative pin on "count orphan=true". No code. | `BlockedTaskParkProjectionTests` (V-7, V-8). CP-8, then the final set CP-9..CP-11 at the last commit. 30 author + 4 check = 34 min, plus 21 min final check time. |

Scope for the Code tasks: `server/Application/Services/SeatDesktopJoin.cs,
server/Application/Services/RunnerSlotService.cs, server/Application/Dtos/RunnerSlotDtos.cs,
server/Application/Services/SeatOccupancySampler.cs,
server/Application/Services/SeatOccupancySnapshot.cs,
server/Application/Services/AttentionService.Seats.cs, tests/Antiphon.Tests/Application/**,
docs/**, .claude/skills/antiphon-orchestrator/SKILL.md`. The `seats` and `orchestration` tags on
the card are not `antiphon.areas.json` areas; `docs` is, and the paths above are the scope.

## Docs sentences and pins

Each row is one sentence to write, where it goes, and the pin that holds it. "Old" is the text
at `f4ae747a6`; "Pin" names the label in `BlockedTaskParkProjectionTests` and whether it is
flipped (existing label, new text) or new.

| # | Slice | File:line | Old | New | Pin |
|---|---|---|---|---|---|
| 1 | S1 | `docs/session-runtime-invariants.md:81` | OpenTaskId is set only for Dispatched or Working, so a live Blocked or Queued owner remains orphan=true. | OpenTaskId is the owner task: the latest Queued, Dispatched, Working or Blocked task bound to the session, so a live Blocked or Queued owner reads orphan=false (CARD-1124). | `c1065-orphan-owner` flipped; new `c1124-no-stale-orphan` = `ShouldNotContain("remains orphan=true")` |
| 2 | S1 | `docs/session-runtime-invariants.md:83` | The slot DTO does not carry park state, so a bound Blocked or Queued owner is still reported as an orphan. | The slot DTO carries park: null, or the owner's current-attempt park id, state, reason code, release id and sync state. It is display-only and never release authority. | new `c1124-slot-park` |
| 3 | S1 | `server/Application/Services/SeatDesktopJoin.cs:20-21` (doc comment) | `<see cref="OpenTaskId"/>` is set only for Dispatched or Working. | `<see cref="OpenTaskId"/>` is the owner task: the latest Queued, Dispatched, Working or Blocked task bound to the session. | `c1065-join-open-task` flipped to `<see cref="OpenTaskId"/> is the owner task` |
| 4 | S1 | `docs/ops-http.md:137` | An orphan is a remembered session with no live desktop session, or a live one with no open task, except a warm pooled delegate. | An orphan is a remembered session with no live desktop session, or a live one with no owner task (Queued, Dispatched, Working or Blocked), except a warm pooled delegate; a live Blocked child is not an orphan. Each slot carries `openTaskId` (the owner) and `park` (null, or the owner's current-attempt `parkId`, `state`, `reasonCode`, `releaseId`, `syncState`; display-only, never release authority) (CARD-1124). | new `c1124-ops-orphan-owner` on "a live Blocked child is not an orphan" |
| 5 | S2 | `docs/ops-http.md:164` (after the `SlotOrphan` sentence) | (none) | A live Blocked owner is not an orphan; it is a `SeatIdle` row from the warning age, and seat evidence carries `park=` (`none`, or the park state and reason). | new `c1124-ops-seat-park` on "seat evidence carries `park=`" |
| 6 | S2 | `docs/antiphon-api.md:577-580` (after the counters sentence) | (none) | `orphanSlots` counts occupying slots with no live desktop session or no owner task (Queued, Dispatched, Working or Blocked); samples stored before CARD-1124 activated also counted live Blocked owners. | new `c1124-api-orphan-slots` on "also counted live Blocked owners" |
| 7 | S2 | `docs/agent-card-lifecycle.md:12-13` | The operator-visible rows are SeatIdle, SlotOrphan, and attention key runner-seat-release:. | The operator-visible rows are SeatIdle for a live Blocked seat (orphan=false, with its park field), SlotOrphan for a seat with no live session or owner task, and attention key runner-seat-release:. | existing `c1065-lifecycle-seat-idle`, `-slot-orphan`, `-attention-key` still match; new `c1124-lifecycle-blocked-not-orphan` on "SeatIdle for a live Blocked seat" |
| 8 | S3 | `.claude/skills/antiphon-orchestrator/SKILL.md:25` | When a host reads at capacity, read GET /api/session-runners/{id}/slots and count orphan=true before concluding the host is busy. | When a host reads at capacity, read GET /api/session-runners/{id}/slots: orphan=true is not a count of free seats; it marks a seat with no live desktop session or no owner task, and a live Blocked child reads orphan=false with its park field, so answer or cancel it rather than treating the slot as free. | `c1065-orphan-count` flipped to "orphan=true is not a count of free seats"; new `c1124-skill-no-count` = `ShouldNotContain("count orphan=true")` |
| 9 | S3 | `docs/orchestration-loop.md:789` | At capacity, read GET /api/session-runners/{id}/slots and count orphan=true. | At capacity, read GET /api/session-runners/{id}/slots. orphan=true is not a count of free seats: it marks a seat with no live desktop session or no owner task, and a live Blocked child reads orphan=false with its park field. Answer or cancel each Blocked child; only the operator release routes free a seat. | `c1065-loop-orphan` flipped to "orphan=true is not a count of free seats"; new `c1124-loop-no-count` = `ShouldNotContain("count orphan=true")` |
| 10 | S3 | CARD-1065 plan `:344`, `:501`, `:890`, `:1153`, `:1403`, `:1491` | Amendment 2026-10-06 (CARD-1112) lines | Add after each: "Amendment 2026-10-07 (CARD-1124): implemented by `docs/superpowers/plans/2026-10-07-card-1124-occupancy-park-projection-plan.md`; the owner set is Queued/Dispatched/Working/Blocked, the slot DTO carries `park`, seat evidence carries `park=`. PC-184's guard is this plan's PC-1; CP-10 remains the doc-pin row." | not pinned |
| 11 | S3 | CARD-1079 plan `:44`, `:76-84`, `:121-127`, `:269-271` | D-2 "OpenTaskId keeps its Dispatched/Working-only meaning"; D-7 "An idle Blocked seat produces a SlotOrphan row at once" | Add one line each: "Amendment 2026-10-07 (CARD-1124): the owner set is now Queued/Dispatched/Working/Blocked; a live Blocked seat is `SeatIdle` only; V-6 is replaced by `C1124_Join_owner_task_is_queued_dispatched_working_or_blocked`; V-7 expects `OrphanSlots=1`; V-8 expects `Orphan=false` on the Blocked seat." | not pinned |

The `BlockedTaskParkProjectionTests.cs:6-9` header comment changes to "The slot route marks a
live Blocked or Queued owner owned and carries its park (CARD-1124)." in S1.

## Verification design

### Inspection

| Read | Why |
|---|---|
| `SeatDesktopJoin.cs` (entire), `RunnerSlotService.cs:20-62`, `:118-160`, `:387-401`, `RunnerSlotDtos.cs:1-21` | Every S1 production line and the three `IsOrphan` call sites. |
| `SeatOccupancySampler.cs:64-157`, `:181-189`; `SeatOccupancySnapshot.cs:36-55`; `AttentionService.Seats.cs:9-35`, `:63-87`, `:124-140`; `SeatOccupancyProjection.cs:29-51` | S2 production lines; the class and idle clock that must not move. |
| `TerminalRunnerSeatReleaseService.cs:149-166`, `:242-291`; `AgentTaskDispatcher.cs:5150-5182`; `AgentTaskService.cs:2560-2565`, `:3426-3436`, `:3973-3974` | The release paths that must stay untouched, the Queued-binding facts, the park-row selection rule being mirrored. |
| `AgentTaskPark.cs`; `AppDbContext.cs:135-159` | Fields and indexes behind the widened park query. |
| Tests: `SeatDesktopJoinTests.cs` (entire), `RunnerSlotEndpointTests.cs:31-100`, `:259-330`, `:406-436`, `SeatOccupancySamplerTests.cs:31-121`, `:330-458`, `SeatOccupancyAttentionTests.cs:159-194`, `:287-320`, `BlockedTaskParkProjectionTests.cs` (entire), `tests/Antiphon.Tests/TestHelpers/CountingCommandInterceptor.cs` | Fixtures to extend, assertions to flip, the interceptor for V-2. |

### Proves it works now

Every V names its negative-control arm; a V is not done while any arm is missing.

| V | Behaviour | Test (class-qualified) |
|---|---|---|
| V-1 | Owner predicate. One Running session per status with one bound task: Queued, Dispatched, Working, Blocked -> `OpenTaskId` is that task; Succeeded, Failed, Canceled -> null. Mixed sessions: Working older plus Succeeded newer -> `OpenTaskId` is the Working task and `LatestTask` the Succeeded one; Blocked older plus Succeeded newer -> `OpenTaskId` is the Blocked task. Empty session -> null. Negative arm: a Stopped session with a Blocked task still has `OpenTaskId` set (ownership is a task fact; liveness stays with `IsOrphan`). The existing first join test's `:129` and `:169` arms flip to the Blocked tasks. | `SeatDesktopJoinTests.C1124_Join_owner_task_is_queued_dispatched_working_or_blocked`; `SeatDesktopJoinTests.C1079_Join_reports_latest_bound_task_blocked_at_pooled_warm_and_receipt` |
| V-2 | Park projection and statement budget. Session A: Running, card and board, Blocked task attempt 2 with a Blocked event, parks: attempt 1 (`Parked`, receipt), attempt 2 older (`Requested`, `park_requested`), attempt 2 newer (`Held`, `park_dirty`, `RunnerSeatReleaseId` R, `SyncState` Pending). Expect `Park` = the newer attempt-2 row (`Held`, `park_dirty`, R, `Pending`) and `PublicationReceipt` false. Session B: Working task, no park -> `Park` null. Session C: Blocked task attempt 3 with only an attempt-1 park -> `Park` null (historical attempt not shown). With `CountingCommandInterceptor` on the read context: exactly 9 commands for the three-session load; no command text contains `INSERT`, `UPDATE` or `DELETE`. Negative arm: the attempt-1 receipt on session A does not make `PublicationReceipt` true for attempt 2. | `SeatDesktopJoinTests.C1124_Join_reports_the_current_attempt_park_in_nine_statements` |
| V-3 | Slots route. `PhoneHomeTestHost` with a peer listing two Running seats. Seat A: Running desktop row, Blocked task (attempt 1), park row `Requested`/`park_requested`, no receipt, `WorktreePath` set to a canary string. Seat B: Running desktop row, Succeeded latest task. GET: `occupied` 2; A `orphan=false`, `occupiesCapacity=true`, `openTaskId` = A's task, `park` = `{parkId, state "Requested", reasonCode "park_requested", releaseId null, syncState "NotRequired"}`; B `orphan=true`, `openTaskId` null, `park` null. The raw response body does not contain the canary. Read-only arm: `AgentTaskParks` (`Revision`, `State`), `AgentSessions.Status`, `AgentTasks` (`Status`, `ConcurrencyToken`) and the `AgentIncidents` count are identical before and after the GET; the peer saw zero `ReleaseSlot` and zero kill requests. The existing `:79` arm (a seat with no task is still an orphan) stays. | `RunnerSlotEndpointTests.C1124_Slots_mark_a_live_blocked_owner_owned_and_carry_its_park`; `RunnerSlotEndpointTests.Release_stops_the_desktop_row_and_records_the_reason` |
| V-4 | Reconcile claimed-by-Blocked. Two Running desktop rows the runner no longer lists, each with a `pending:orphan:<runner>` intent: the first owned by a Blocked task, the second with a Succeeded latest task. `ReconcilePendingReleasesAsync` returns only the second id; the first intent's `FailureReason` starts `failed:` and its session stays Running; the second is `reconciled` and Stopped; zero `ReleaseSlot` requests. Negative arm: the Succeeded seat proves the arm still reconciles a true orphan. | `RunnerSlotEndpointTests.C1124_Reconcile_fails_an_orphan_sweep_intent_claimed_by_a_blocked_owner` |
| V-5 | Sampler. The existing rig (Working seat; Blocked seat attempt 4 with a `park_requested` park and receipt; unbound seat; one pending launch): Blocked seat `Orphan=false`, `Class=IdleBlocked`, `IdleSince` = the Blocked event, `ParkState="Requested"`, `ParkReason="park_requested"`, `Pushed="yes"`; unbound seat `Orphan=true`, `ParkState="none"`, `ParkReason` null; Working seat `Orphan=false`, `ParkState="none"`. Host row: `OrphanSlots=1`, `IdleSeats=2`, `DispatchedWorking=1`, `InFlight=4`. Existing `:46` and `:82` arms flip to 1 and false. Negative arm: the unbound seat keeps `OrphanSlots` above zero, so the count did not simply vanish. | `SeatOccupancySamplerTests.C1124_Blocked_owner_is_idle_not_orphan_and_carries_its_park`; `SeatOccupancySamplerTests.C1079_One_sample_per_host_with_in_flight_breakdown_and_dispatched_working`; `SeatOccupancySamplerTests.C1079_Seats_carry_idle_since_class_orphan_and_pushed_from_durable_facts` |
| V-6 | Attention evidence. Hand-built snapshot: `IdleBlocked` seat 40 minutes idle, `orphan=false`, `ParkState="Held"`, `ParkReason="park_dirty"`; `IdleUnbound` seat `orphan=true`, `ParkState="none"`. The `SeatIdle` row for the first carries `park=Held:park_dirty` and there is no `SlotOrphan` row for it; the `SlotOrphan` row for the second carries `park=none`. Negative arm: the first seat yields no `SlotOrphan` key although its class is idle. | `SeatOccupancyAttentionTests.C1124_Seat_evidence_names_the_park_state` |
| V-7 | Owner-doc and code-comment pins (sentences 1-7) hold, and "remains orphan=true" is absent from the owner doc. | `BlockedTaskParkProjectionTests.C1065_OccupancyTracksProcessesNotBlockedStatus` |
| V-8 | Instruction pins (sentences 8-9) hold, and "count orphan=true" is absent from both instruction files. | `BlockedTaskParkProjectionTests.C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` |

### Guards the regression

| R | Negative control | Test |
|---|---|---|
| R-1 | The latest-task query stays grouped per session; pooled-warm, receipt, Blocked-at and board facts are unchanged. | `SeatDesktopJoinTests.C1079_Join_latest_task_query_is_grouped_per_session`; `SeatDesktopJoinTests.C1079_Join_reports_latest_bound_task_blocked_at_pooled_warm_and_receipt` |
| R-2 | Single-seat release, operator token, pending-intent reconcile, refused releases and the sweep's claimed-after-list refusal behave as before. | whole `RunnerSlotEndpointTests` (8 existing) |
| R-3 | `OccupiesCapacity` and `IsOrphan` truth tables and the reconcile job registration are unchanged. | whole `RunnerSlotRulesTests` |
| R-4 | Unavailable inventory, reason categories, retention prune, disabled watch and the local host sample are unchanged. | whole `SeatOccupancySamplerTests` (9 existing) |
| R-5 | Idle thresholds, divergence, orphan-row filter, summary counts and the disabled watch are unchanged for hand-built snapshots. | whole `SeatOccupancyAttentionTests` (9 existing) |
| R-6 | Seat class, idle-since and severity ladder are unchanged. | whole `SeatOccupancyProjectionTests` (4 methods, 15 results) |
| R-7 | The seat-release ledger, discovery, `ReleaseOrphansAsync` gate and the dispatcher pool-release sweep are unchanged; the DTO and record shape changes break no fixture. | whole `TerminalRunnerSeatReleaseTests` (29 methods, 39 results), whole `RunnerSeatOrphanSweepTests` (17) |

### Guard inventory

| G | Decision: guard | PC |
|---|---|---|
| G-1 | D-1: a live Blocked owner is not an orphan | PC-1 |
| G-2 | D-1: a live Queued owner is not an orphan | PC-2 |
| G-3 | D-1: a settled latest task is not an owner | PC-3 |
| G-4 | D-2: `park` is the owner's current-attempt newest row, never a historical attempt | PC-4 |
| G-5 | D-2: `park` carries no raw path | PC-5 |
| G-6 | D-3: the join issues nine statements, never one per session | PC-6 |
| G-7 | D-5: a Blocked claim fails an orphan-sweep intent | PC-7 |
| G-8 | D-4: `OrphanSlots` excludes live Blocked owners | PC-8 |
| G-9 | D-4: seat evidence names the park state | PC-9 |
| G-10 | D-7: the slots GET writes nothing | PC-10 |
| G-11 | D-6: the retired owner-doc sentence stays absent | PC-11 |

### Positive controls

Mutation runs break/red/restore/green after land; Code runs ordinary V/R; Review judges the
evidence. Every filter selects one method.

| PC | Mutation | Filter | Expected red |
|---|---|---|---|
| PC-1 | `SeatDesktopJoin.cs` owner predicate: remove `Blocked`. | `/*/*/SeatDesktopJoinTests/C1124_Join_owner_task_is_queued_dispatched_working_or_blocked` | Blocked arm: `OpenTaskId` null, expected the task. |
| PC-2 | Owner predicate: remove `Queued`. | `/*/*/SeatDesktopJoinTests/C1124_Join_owner_task_is_queued_dispatched_working_or_blocked` | Queued arm: `OpenTaskId` null, expected the task. |
| PC-3 | Owner predicate: admit every status. | `/*/*/SeatDesktopJoinTests/C1124_Join_owner_task_is_queued_dispatched_working_or_blocked` | Succeeded arm: `OpenTaskId` set, expected null. |
| PC-4 | Park pick: drop the `Attempt == owner.Attempt` filter. | `/*/*/SeatDesktopJoinTests/C1124_Join_reports_the_current_attempt_park_in_nine_statements` | Session C: `Park` not null, expected null. |
| PC-5 | `RunnerSlotParkDto`: add and populate `WorktreePath`. | `/*/*/RunnerSlotEndpointTests/C1124_Slots_mark_a_live_blocked_owner_owned_and_carry_its_park` | Canary found in the response body. |
| PC-6 | `SeatDesktopJoin.cs`: load the park row inside the per-session loop. | `/*/*/SeatDesktopJoinTests/C1124_Join_reports_the_current_attempt_park_in_nine_statements` | 12 commands, expected 9. |
| PC-7 | `RunnerSlotService.cs:134`: pass `row.OpenTaskStatus is Dispatched or Working` (or equivalent) instead of `row.OpenTaskId is not null`. | `/*/*/RunnerSlotEndpointTests/C1124_Reconcile_fails_an_orphan_sweep_intent_claimed_by_a_blocked_owner` | The Blocked seat's intent is `reconciled` and its session Stopped, expected `failed:` and Running. |
| PC-8 | `SeatOccupancySampler.cs:123`: count `occupies && IsDesktopIdle(row)` instead of `occupies && orphan`. | `/*/*/SeatOccupancySamplerTests/C1124_Blocked_owner_is_idle_not_orphan_and_carries_its_park` | `OrphanSlots` 2, expected 1. |
| PC-9 | `AttentionService.Seats.cs`: drop the `park=` field from `SeatEvidence`. | `/*/*/SeatOccupancyAttentionTests/C1124_Seat_evidence_names_the_park_state` | Evidence lacks `park=Held:park_dirty`. |
| PC-10 | `RunnerSlotService.ListAsync`: bump the owner's park `Revision` and save before returning. | `/*/*/RunnerSlotEndpointTests/C1124_Slots_mark_a_live_blocked_owner_owned_and_carry_its_park` | Before/after park snapshot differs. |
| PC-11 | `docs/session-runtime-invariants.md`: restore "remains orphan=true". | `/*/*/BlockedTaskParkProjectionTests/C1065_OccupancyTracksProcessesNotBlockedStatus` | `c1124-no-stale-orphan` fails. |

### Out of scope

Enabling parking or the reclaim sweep; any change to `TerminalRunnerSeatReleaseService`,
`TaskParkPublicationService`, `BlockedTaskParkingService` or the dispatcher; a client change
(the `SlotOrphan` hint "An occupying slot is an orphan" stays true); persisting park state in
`HostOccupancySample`; collapsing seat rows (CARD-1085); the CARD-1065 PC-182, PC-183, PC-185
and PC-186 controls, which stay with their owners (R-3 and R-4 here cover PC-182 and PC-183's
behaviours; PC-186's path canary is PC-5).

### Checkpoints

Exactly one isolated build and one filter per row; reuse rows name the build row of the same
`After` group. Counts are TUnit executed results. Rosters at `f4ae747a6`: `SeatDesktopJoinTests`
3, `RunnerSlotEndpointTests` 8, `RunnerSlotRulesTests` 2, `BlockedTaskParkProjectionTests` 2,
`SeatOccupancySamplerTests` 9, `SeatOccupancyAttentionTests` 9, `SeatOccupancyProjectionTests`
15 (4 methods; 8 and 5 `[Arguments]` rows), `TerminalRunnerSeatReleaseTests` 39 (29 methods, 14
`[Arguments]` rows on 4 of them), `RunnerSeatOrphanSweepTests` 17. S1 replaces one
`SeatDesktopJoinTests` method and adds one, and adds two `RunnerSlotEndpointTests` methods; S2
adds one method each to `SeatOccupancySamplerTests` and `SeatOccupancyAttentionTests`. New
methods add no parameter expansion. Confirm the TRX roster equals the expected set, not merely
at least `Min`. The table was validated at planning time with the checkpoint importer
(`import --plan`, tool built through `scripts/build-slot.ps1` to `bin-c1124-driver/`, output
deleted afterwards): 11 rows imported, exit 0, no advisory warnings, no tests run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1124-cp1/` | join-s1 | `/*/*/SeatDesktopJoinTests/*` | V-1, V-2, R-1 | exact 4 results (2 existing + 1 replaced + 1 new), 0 failed/skipped | 4 | 6 | false |
| CP-2 | S1 | CP-1 | slots-s1 | `/*/*/RunnerSlotEndpointTests/*` | V-3, V-4, R-2 | exact 10 results (8 + 2 new), 0 failed/skipped | 10 | 5 | true |
| CP-3 | S1 | CP-1 | rules-s1 | `/*/*/RunnerSlotRulesTests/*` | R-3 | exact 2 results, 0 failed/skipped | 2 | 2 | false |
| CP-4 | S1 | CP-1 | pins-s1 | `/*/*/BlockedTaskParkProjectionTests/*` | V-7 | exact 2 results, 0 failed/skipped | 2 | 2 | false |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1124-cp5/` | sampler-s2 | `/*/*/SeatOccupancySamplerTests/*` | V-5, R-4 | exact 10 results (9 + 1 new), 0 failed/skipped | 10 | 6 | false |
| CP-6 | S2 | CP-5 | attention-s2 | `/*/*/SeatOccupancyAttentionTests/*` | V-6, R-5 | exact 10 results (9 + 1 new), 0 failed/skipped | 10 | 3 | false |
| CP-7 | S2 | CP-5 | projection-s2 | `/*/*/SeatOccupancyProjectionTests/*` | R-6 | exact 15 results (4 methods with 8 + 5 argument rows), 0 failed/skipped | 15 | 2 | false |
| CP-8 | S3 | `tests/Antiphon.Tests -> bin-c1124-cp8/` | pins-s3 | `/*/*/BlockedTaskParkProjectionTests/*` | V-7, V-8 | exact 2 results, 0 failed/skipped | 2 | 4 | false |
| CP-9 | all | `tests/Antiphon.Tests -> bin-c1124-cp9/` | seat-final | `/*/*/(SeatDesktopJoinTests*)\|(SeatOccupancySamplerTests*)\|(SeatOccupancyAttentionTests*)\|(SeatOccupancyProjectionTests*)\|(RunnerSlotRulesTests*)\|(BlockedTaskParkProjectionTests*)/*` | V-1, V-2, V-5, V-6, V-7, V-8, R-1, R-3, R-4, R-5, R-6 | exact 43 results (4 + 10 + 10 + 15 + 2 + 2), 0 failed/skipped | 43 | 8 | false |
| CP-10 | all | CP-9 | slots-final | `/*/*/RunnerSlotEndpointTests/*` | V-3, V-4, R-2 | exact 10 results, 0 failed/skipped | 10 | 5 | true |
| CP-11 | all | CP-9 | release-final | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | R-7 | exact 56 results (39 + 17), 0 failed/skipped | 56 | 9 | true |

Run: `dotnet run --project tools/Antiphon.Checkpoints -- run --plan
docs/superpowers/plans/2026-10-07-card-1124-occupancy-park-projection-plan.md --after S1
--expected-source-sha <sha>` after S1's commit, `--after S2` and `--after S3` after theirs, and
`--after all` at the final commit; `wait` until the exit is not 75. Each Code task deletes its
`bin-c1124-*` directories before it finishes.

### Cost

Estimated, not measured: ordinary Code V/R floor = **52 minutes**, the sum of the
`EstimatedMinutes` column (S1 15, S2 11, S3 4, final 22). Each building row includes about
2 minutes of isolated build (CARD-1065 S9 measured 111 s) and 40 s of host start.
Checkpoint-driver bootstrap allowance is **5 minutes**, not double-counted.

Slices stay inside 30-60 minutes: S1 38 + 15 = 53; S2 30 + 11 = 41; S3 30 + 4 = 34 plus the
22-minute final set, which is check time, not authoring. Total author = **98 minutes**; Code
floor with bootstrap = **155 minutes** across three Code tasks, or one Code task at
`-ExpectAbout` 160 if the orchestrator prefers a single dispatch (the slices are small and
sequenced). If a slice exceeds its bound, split it at the slice boundary before starting the
next one; do not widen a row.

## Mutation handoff

After land, dispatch a method-scoped SourceLanding Mutation for PC-1..PC-11 of this plan at
the landed SHA. Every control selects one method with the filters above; none batches, because
PC-1..PC-3 and PC-4/PC-6 share a target method and must each show their own red. Keep per-PC
baseline, red and restored-green evidence in the external evidence root. This closes CARD-1065
G-184/PC-184, whose declared target was never executed; the CARD-1065 plan amendment in S3
says so.

## Rollout and activation

Nothing activates and nothing is gated. After the AppHost restart that picks this up,
`GET /api/session-runners/{id}/slots` reports `orphan=false` and a `park` field for live Blocked
children, `orphanSlots` on new samples excludes them, and their `SlotOrphan` rows clear on the
next sampler tick while their `SeatIdle` rows (if any) persist. Parking, reclaim and automatic
release stay `false` by default and are not read by this change. Confirm activation with
`GET /api/version` as the owner doc requires, then `scripts/runner-slots.ps1 list` on a host
with a Blocked child.

## Handoff

The verification design above is complete, so the next stage is Code S1 on
`feat/card-task-559f3ef6`'s landed successor with
`checkpoints: docs/superpowers/plans/2026-10-07-card-1124-occupancy-park-projection-plan.md@<plan commit sha> section "### Checkpoints"`,
`--after S1`. One Code task may take S1-S3 in order if the orchestrator prefers; the rows are
grouped by `After` either way.
