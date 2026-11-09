# CARD-1803: a Blocked seat past N hours escalates to an owner

Plan for Antiphon CARD-1803. Detection only. A seat-holding Blocked task that is past the new hour bound, and whose caller note is still undelivered, is paged once on the digest channel. The keep-six reminder names how many seats Blocked tasks hold and names the occupancy gap. Nothing in this plan cancels, parks, kills, or releases a session.

Inspected source: master `e983d6fe263af623a3da186a2260575f60cc4c5f`. Investigation: `docs/investigations/2026-11-09-blocked-complete-tasks-hold-seats.md` (feat/card-task-12ea19bf, `d53db64b5`). CARD-1802 has already landed: lease contention alone no longer Blocks a correlated complete report. This plan does not reopen that settlement.

Platform, read 2026-11-09: `GET /api/runner-defaults` revision 2 has a global runner, no per-kind overrides, supported kinds Grok, ClaudeCode and Codex, and no unresolved references. `GET /api/session-runners` has three catalogue rows: one windows row that is available and eligible, one linux row that is available and eligible, and one row with an empty platform that is draining, stale, and not eligible. Checkpoints omit `-Runner` and `-Platform`. No test in this plan needs a phone-home host.

## 1. Intended behaviour

### Before

A Blocked task with a live session keeps that seat while `BlockedTaskParking:Enabled` is false. `TerminalRunnerSeatReleaseService.TryHandleTaskAsync` returns without acting when parking is off (`server/Application/Services/TerminalRunnerSeatReleaseService.cs:78`).

Attention already reports the seat and the missing note:

- `SeatIdle` (57) at 30 minutes Warning and 180 minutes Error (`server/Application/Services/AttentionService.Seats.cs:21-34`, `SeatOccupancyProjection.Severity` at `SeatOccupancyProjection.cs:53-60`).
- `OccupancyDivergence` (58), `inFlight` minus Dispatched/Working, on the same ages (`AttentionService.Seats.cs:47-53`, `SeatOccupancyProjection.Divergence` at `SeatOccupancyProjection.cs:63`).
- `BlockedQuestion` for the Blocked task (`AttentionService.cs:467-479`).
- `CallerNoteUndelivered` when a Delegation or Check note is still Pending past `Delegation:DeliveryFailTimeoutMinutes` (10) (`AttentionService.cs:1942-2020`).

`BlockedTaskNotifier.SweepAsync` (`BlockedTaskNotifier.cs:25-60`) pages digest-enabled channels once per block episode, then writes `HumanNotified` (17). It indexes attention with `ToDictionary` on `TaskId` (`BlockedTaskNotifier.cs:38`). `BlockedQuestion` and `SeatIdle` both set that id, and `SeatIdle` appears at the warning age. The second item throws. `AwayDigestHostedService` (`AwayDigestHostedService.cs:22-32`) runs that sweep, the decision page, the incident page, and the digest in one try, and logs a failure for the whole tick. The hosted service also returns immediately when `Digest:Enabled` is false (`AwayDigestHostedService.cs:17`).

The keep-six reminder is composed only when shortfall is positive (`KeepSixReminderJob.cs:104`, `KeepSixReminderText.Compose` at `KeepSixReminderText.cs:24-41`). It names open, shortfall, cards, host remaining, and stage parallel remaining. It does not name Blocked seats. Pipeline `HostLimitSummaryDto` carries `inFlight` and `remaining` and does not carry Dispatched/Working (`AgentTaskPipelineDtos.cs:47-55`). The divergence number exists only on the attention row.

`AgentTaskEventType.Escalated` (2) is the model-tier bump. `EscalateAsync` stops the session and requeues (`AgentTaskService.cs:3397`). The automatic stall sweep that writes that event is disarmed unless an operator sets `EscalateAfterMinutes` (CARD-0158).

### After

The seat, the park default, and the attention rows stay. Two new deliveries are added.

**Operator channel.** When all of the following hold, each digest-enabled channel is sent one page, and `BlockedSeatEscalated` (43) is written after that send succeeds:

1. The task is Blocked and not a specialist (`AgentTaskRoles.NotSpecialist`, `AgentTaskEnums.cs:474-477`).
2. The current seat snapshot has an occupying `IdleBlocked` seat for that task on a runner host (not kind `local` and not host id `local`, the same exclusion as `AttentionService.Seats.cs:92-94`).
3. `now - seat.IdleSince` is at least `Attention:BlockedSeatEscalationHours` (default 6). `IdleSince` for `IdleBlocked` is `blockedAt`, or the runner start when that stamp is missing (`SeatOccupancyProjection.cs:44-50`).
4. Attention has a `CallerNoteUndelivered` item whose `TaskId` is this task.
5. No `BlockedSeatEscalated` event is newer than the latest `Blocked` or `Conflicted` event.
6. `Digest:WakeOnBlocked` is true (default) and the channel has `DigestEnabled`. The sweep still runs only when `Digest:Enabled` is true.

The page is not a cancel, a park, a release, or a call to `EscalateAsync`. A failed send writes no event, so the next sweep retries. A later `Blocked` or `Conflicted` event starts a new episode and can page again after its own bound. A Blocked task with no occupying seat is not paged. A Blocked task whose caller note was delivered, or was never queued, is not paged.

`BlockedTaskNotifier` groups attention items by task and prefers `BlockedQuestion`, so the existing one-shot question ping still fires when `SeatIdle` shares the task. Each sweep call in `AwayDigestHostedService` gets its own try, so a failure in one call does not skip the others.

**Shortfall the orchestrator reads.** When `KeepSixReminderJob` composes a reminder (shortfall still has to be positive; this plan does not add a new trigger), the body gains:

- `{n} seats held by Blocked tasks.` when `n > 0`. `n` is the count of occupying `IdleBlocked` seats on runner hosts, including specialists, because those sessions are in host `inFlight`.
- `Occupancy divergence: {hostId} {inFlight} in flight, {dispatchedWorking} Dispatched/Working.` for each runner host whose `SeatOccupancyProjection.Divergence` is positive, hosts in ordinal `HostId` order, joined with a comma.

Zero seats and zero gaps omit those lines. The stored keep-six target stays whatever the row holds (`KeepSixOrchestrator.DefaultTarget` is 6, `KeepSixOrchestrator.cs:9`). A target of 7 uses the same sentence. The server still does not dispatch.

### State flow

```
Blocked + occupying IdleBlocked seat
        |  age < 6h                         attention only (SeatIdle, and the note row if Pending)
        |  age >= 6h, caller note delivered attention only
        |  age >= 6h, CallerNoteUndelivered, digest on
        v
digest page + BlockedSeatEscalated = 43
        task stays Blocked, session stays Running, park unchanged

keep-six shortfall > 0
        v
reminder body includes the seat count and the divergence lines
        no task is created, no seat is released
```

### Files to change

| File | Range now | Change |
|---|---|---|
| `server/Application/Settings/AttentionSettings.cs` | 7-28 | `BlockedSeatEscalationHours`, default 6 |
| `server/Application/Settings/AttentionSettingsValidator.cs` | 7-22 | hours in 1..720, and `hours * 60 > SeatIdleErrorMinutes` |
| `server/appsettings.json` | 201-208 | the same key, value 6 |
| `server/Domain/Enums/AgentTaskEnums.cs` | 179-180, 294-295 | append `BlockedSeatEscalated = 43` after `LandingBranchDeletion = 42` |
| `server/Application/Services/SeatOccupancyProjection.cs` | 53-67 | `IsRunnerHost`, the two-string exclusion now private on `AttentionService.Seats.cs:92-94` |
| `server/Application/Services/AttentionService.Seats.cs` | 92-94 | call `IsRunnerHost` |
| `server/Application/Services/BlockedSeatEscalation.cs` | new | pure qualify rule |
| `server/Application/Services/BlockedSeatEscalationNotifier.cs` | new | digest page and event stamp |
| `server/Application/Services/AwayDigestFormatter.cs` | 43-47 | `FormatBlockedSeatEscalation` beside `FormatPing` |
| `server/Application/Services/BlockedTaskNotifier.cs` | 38 | group by task id; prefer `BlockedQuestion` |
| `server/Infrastructure/Supervision/AwayDigestHostedService.cs` | 22-32 | one try per sweep call; call the new notifier |
| `server/Program.cs` | 915-917 | register the notifier next to `BlockedTaskNotifier` |
| `server/Application/Services/KeepSixReminderText.cs` | 15-41 | two optional facts and the two lines |
| `server/Infrastructure/Agents/KeepSixReminderJob.cs` | 132, 175-195 | read `SeatOccupancyState.Current` (`server/Application/Services/SeatOccupancyState.cs`) |
| `server/Bundles/orchestrator.md` | 74 | one sentence on the new reminder lines |
| `docs/ops-http.md` | 177 | the page and the reminder, still detection only |
| `docs/telegram.md` | 422-434 | the new ping prefix, same two digest switches |
| `docs/session-runtime-invariants.md` | 467-477 | the page does not release; parking off still has no deadline |
| `docs/bootstrap.md` | 442-450 | the new hour setting next to the idle ages |

The client timeline prints `event.type` as text (`client/src/features/delegations/TaskDetailBody.tsx:475-476`). The TypeScript union in `client/src/api/agentTasks.ts` is already behind the server enum. This plan does not edit it.

### Unchanged

- Parking, pool retirement, `TerminalRunnerSeatRelease`, and `EscalateAsync`. A Blocked session that waits for input is not given a new release. With parking disabled there is still no automatic release deadline (CARD-1083). This plan does not add or change a session that waits for input.
- `SeatIdle`, `OccupancyDivergence`, `BlockedQuestion`, and `CallerNoteUndelivered` rows, their keys, and their ages.
- The one-shot `HumanNotified` question ping, including its per-channel stamp shape: a later channel is not retried on the next sweep once any stamp exists for the episode. The new event copies that shape.
- Keep-six shortfall math, grace, backoff, and the rule that shortfall 0 enqueues nothing.
- Settlement (CARD-1802). Lease-busy complete reports are out of scope. Dirty, diverged, rewound, progress-unverified, and real questions can still sit Blocked and hold a seat, and those are what this page is for.
- No migration. Master's latest migration is `20261108121426_Card1773TranscriptPromptStampIndex`. `AgentTaskEvent.Type` is an int with no check constraint (`AppDbContext.cs:2546-2556`). The hour setting is configuration.

## 2. Decisions

D-1. Escalation is a notification. It does not auto-cancel. `AGENTS.md` states that a stall is a detection and decision state, never an automatic kill. `docs/session-runtime-invariants.md:467-477` states that a Blocked session keeps its runner seat and that parking off has no automatic release deadline. `AgentTaskEventType.Escalated` stops the session (`AgentTaskStallEscalationTests`). The investigation's four seats stayed up for 6.7 to 30 hours with Error and Critical attention and no owner action; the missing piece it names is escalation, and it says detection only.

D-2. The bound is `Attention:BlockedSeatEscalationHours`, default 6, not a multiplier on `SeatIdleErrorMinutes`. Six hours is 360 minutes, strictly after the 180-minute Error row, and it is inside the shortest unanswered hold in the investigation (6.7 hours). The validator requires `1..720` and `(long)hours * 60 > SeatIdleErrorMinutes`, so the Error row exists before the page. Default 6 with error 180 passes. Hours 1, 2, and 3 fail against the default error age. Rejected: page at `SeatIdleErrorMinutes`, which would repeat the Error row on the channel at the same instant the feed already shows it. The question ping already covers the moment the task becomes Blocked. Rejected: a multiplier `k` alone. Operators set a duration in hours, and a product of two settings is harder to check than one inequality.

D-3. The page uses the digest channel that already carries blocked-work pings. `BlockedTaskNotifier` sends to `ChatChannels` where `DigestEnabled` is true, and only when `WakeOnBlocked` is true (`BlockedTaskNotifier.cs:27-28`). `docs/telegram.md:423-427` says both `Digest:Enabled` and the channel flag are required. The new notifier uses those same two flags and is called from `AwayDigestHostedService`. Rejected: a second hosted service that sends while digest is disabled. That would bypass the operator channel the repository already has. Turning digest on is an operator step, listed below. Rejected: a new `WakeOn` switch. `WakeOnBlocked` already means "page blocked work," and a second switch would ship off.

D-4. Qualify from the seat snapshot plus the existing `CallerNoteUndelivered` item. Do not add an `AttentionKind`. `SeatIdle` is already the seat detector and `CallerNoteUndelivered` is already the undelivered-note detector (`AttentionService.cs:2005-2013` sets `TaskId` to the source task). A second detector can drift from those rows. Any undelivered-note item for the task qualifies; more than one must not throw. A missing snapshot, `SeatWatchEnabled` false, or an empty snapshot means no seat evidence and no page.

D-5. The reminder names every current occupying `IdleBlocked` runner seat, not only seats that already meet the hour bound and the undelivered note. Host `inFlight` counts those seats from the first minute, and stage parallel remaining does not (`KeepSixShortfall.cs:87-90` counts Queued, Dispatched, and Working only). The orchestrator reads the reminder while deciding how many tasks to dispatch. Rejected: gate the sentence on the same predicate as the page. That leaves the host-budget lie unstated until six hours. The page stays the gated escalation. Rejected: enqueue a reminder when shortfall is 0 solely to mention seats. The server does not dispatch, and this plan does not add a reminder trigger. The page covers the shortfall-zero case.

D-6. Divergence stays off `HostLimitSummaryDto`. The card asks for it in the shortfall the orchestrator reads, which is the keep-six body (`KeepSixReminderText`, and `server/Bundles/orchestrator.md:74`). Adding a pipeline field is a public DTO and selects the CARD-1782 `contracts` row (`tests/impacted-classes.json`, globs `server/**/Dtos/**` and `client/src/api/**`). The pipeline already shows `inFlight` and `remaining`.

D-7. `BlockedTaskNotifier`'s lookup is grouped, and each digest sweep call has its own try. By inspection, `ToDictionary` on `TaskId` throws once `SeatIdle` shares the task, and the single try then skips every later call in that tick (`AwayDigestHostedService.cs:22-32`). A new notifier later in that same try would not run for the seats this card is about. The question-ping text stays the `BlockedQuestion` item. This is not a claim that a production log line was captured.

D-8. The stamp is a new event, value 43. `HumanNotified` means the question ping. `Escalated` means the model-tier bump. Detail is the channel id in `D` format, matching `HumanNotified`. The integer is stored as-is. `AgentTaskEventTypeLedgerTests` forbids two names on one value. Do not renumber.

## 3. Tests

Red first, inside each slice: commit the new tests against production that does not yet page or name the seats, run that slice's checkpoint filter, then commit the behaviour and rerun the same row. A witness that cannot fail on the old line is a stub.

New and extended tests:

| Id | Test | Proves |
|---|---|---|
| V-1 | `AttentionSettingsValidatorTests.C1803_EscalationHoursDefaultAndBound` | Default object has `BlockedSeatEscalationHours == 6` and validates. Hours 0, 721, and 3 (with error 180) fail, and the failure text contains `Attention:BlockedSeatEscalationHours`. Hours 4 validates against error 180. |
| V-2 | `BlockedSeatEscalationRuleTests` (six methods, names below) | Pure rule. No database. |
| V-3 | `BlockedSeatEscalationNotifierTests` (seven methods, names below) | One page, no second page, seat left Blocked and Running. |
| V-4 | `BlockedTaskNotifierTests.C1803_SeatIdleBesideTheQuestionStillPingsOnce` | `BlockedQuestion` plus a published `SeatIdle` seat does not throw. One `❓` ping. Second sweep sends nothing more. |
| V-5 | `AwayDigestFormatterTests.C1803_SeatPageNamesTheSeatAndDoesNotAskForACancel` | First line is `⛔ task {8hex} still holds a seat — caller note undelivered`. Body contains `Detection only` and does not contain `Cancel` or `release`. |
| V-6 | `KeepSixReminderTextTests` (two methods) and `KeepSixReminderTests.C1803_ReminderNamesBlockedSeatsAndDivergence` | The sentence and the divergence line. The job reads a published snapshot. Shortfall 0 still enqueues nothing. |
| V-7 | `AgentTaskEventTypeLedgerTests.C1803_BlockedSeatEscalatedIs43` and `SeatOccupancyProjectionTests.C1803_RunnerHostExcludesLocal` | Value 43 is the only new event value. Local kind and local host id are not runner hosts. |
| V-8 | `BlockedSeatEscalationDocumentationTests.C1803_PageAndReminderStayDetectionOnly` and `InstructionBundleTests.C1803_OrchestratorNamesBlockedSeatShortfall` | Doc and bundle sentences below. |

`BlockedSeatEscalationRuleTests` methods:

- `C1803_QualifiesAtTheBoundWithAnUndeliveredNote` — occupying `IdleBlocked`, runner host, age exactly 6 hours, note present.
- `C1803_BelowTheBoundDoesNotQualify` — age one tick under 6 hours.
- `C1803_MissingNoteDoesNotQualify`.
- `C1803_LocalHostDoesNotQualify` — kind `local`, and host id `local` with kind `runner`.
- `C1803_SpecialistRoleDoesNotQualify` — `Check`, `Distill`, `Diagnose`.
- `C1803_ActiveOrTerminalSeatDoesNotQualify` — `Active` and `IdleTerminal`.

`BlockedSeatEscalationNotifierTests` uses the isolated schema and fake messaging client from `BlockedTaskNotifierTests`, plus a `SeatOccupancyState` the attention service can see. Each harness creates its own schema because the sweep walks every Blocked task.

- `C1803_PastBoundUndeliveredNotePagesOnceAndLeavesTheSeat` — session `Running`, task stays `Blocked`, one `⛔` send, one `BlockedSeatEscalated`, no `Canceled`, `Escalated`, or `Failed` event. Second sweep adds nothing. Park on the seat stays `none`.
- `C1803_UnderTheBoundDoesNotPage`.
- `C1803_WithoutAnUndeliveredCallerNoteDoesNotPage`.
- `C1803_BlockedTaskWithNoOccupyingSeatDoesNotPage`.
- `C1803_FailedSendWritesNoStamp` — throw on send, zero events, replace the producer, next sweep sends once.
- `C1803_ALaterBlockPagesAgainAfterItsOwnBound` — answer is not required; a new `Blocked` event and a seat idle since that event, clock advanced past the bound, second page.
- `C1803_CheckRoleIsNotPaged`.

The caller-note row in these tests is a real `SessionQueuedMessage`: `Pending`, origin `Delegation` or `Check`, `SourceLandNotificationId` null, `SourceTaskId` the blocked task, task `ReplyTo` `Session`, `ParentSessionId` equal to the message session, `CreatedAt` older than the 10-minute delivery grace. The seat's `IdleSince` is at least six hours before the clock. Do not hand-build the attention item and skip `AttentionService.GetAsync`; the notifier must see the row the production attention builder emits.

`KeepSixReminderTextTests`:

- `C1803_NamesSeatsAndDivergenceWhenPresent` — body contains `2 seats held by Blocked tasks.` and `Occupancy divergence: ` with both host facts, and still contains `Antiphon has not dispatched.` The body does not contain `Cancel` or `release`.
- `C1803_OmitsSeatLinesWhenNoneAreHeld` — those two sentences are absent; the existing six lines remain.

`KeepSixReminderTests.C1803_ReminderNamesBlockedSeatsAndDivergence` publishes one runner host with one occupying `IdleBlocked` seat and `inFlight` greater than `DispatchedWorking`, using the past-grace seed `ReadyAsync` already uses, then asserts the queued body. `finally` publishes `SeatOccupancySnapshot.Empty`. The factory's `SeatOccupancyState` is a singleton for the session. Existing reminder tests keep `AssertReminder` and do not require the new lines, so an empty snapshot leaves them green.

### Mutants

| Id | Mutation | Witness that goes red |
|---|---|---|
| M-1 | Call `CancelAsync`, `StopDelegate`, `TryHandleTaskAsync`, `KillAsync`, or `EscalateAsync` from the notifier | `C1803_PastBoundUndeliveredNotePagesOnceAndLeavesTheSeat` (status and event assertions). The documentation test also rejects those names in `BlockedSeatEscalationNotifier.cs`. |
| M-2 | Drop the undelivered-note conjunct | `C1803_WithoutAnUndeliveredCallerNoteDoesNotPage` and `C1803_MissingNoteDoesNotQualify` |
| M-3 | Compare against `SeatIdleErrorMinutes` instead of the hour setting | `C1803_UnderTheBoundDoesNotPage` and `C1803_BelowTheBoundDoesNotQualify` |
| M-4 | Page a Blocked task with no occupying seat, or a local host | `C1803_BlockedTaskWithNoOccupyingSeatDoesNotPage`, `C1803_LocalHostDoesNotQualify` |
| M-5 | Write `BlockedSeatEscalated` before send returns | `C1803_FailedSendWritesNoStamp` |
| M-6 | Reuse `HumanNotified` or `Escalated` as the stamp | `C1803_PastBoundUndeliveredNotePagesOnceAndLeavesTheSeat` (expects type 43 and no `Escalated`) and `C1803_BlockedSeatEscalatedIs43` |
| M-7 | Leave `ToDictionary` on `TaskId` | `C1803_SeatIdleBesideTheQuestionStillPingsOnce` |
| M-8 | Omit the reminder sentences, or emit them when the counts are zero | `C1803_NamesSeatsAndDivergenceWhenPresent`, `C1803_OmitsSeatLinesWhenNoneAreHeld`, `C1803_ReminderNamesBlockedSeatsAndDivergence` |
| M-9 | Tell the orchestrator to cancel or release | `C1803_NamesSeatsAndDivergenceWhenPresent` and `C1803_OrchestratorNamesBlockedSeatShortfall` |
| M-10 | Drop the detection-only sentence from one of the five doc sites | `C1803_PageAndReminderStayDetectionOnly` |

### Doc-contract pins

`C1803_PageAndReminderStayDetectionOnly` reads these literals:

- `docs/ops-http.md` seat-occupancy row contains `Attention:BlockedSeatEscalationHours` and `Detection only`.
- `docs/telegram.md` contains `⛔ task` and still says both digest switches are required.
- `docs/session-runtime-invariants.md` contains `BlockedSeatEscalated` and `nothing automatically releases a session that waits for input`.
- `docs/bootstrap.md` contains `Attention:BlockedSeatEscalationHours`.
- `server/appsettings.json` Attention object contains `"BlockedSeatEscalationHours": 6`.
- `BlockedSeatEscalationNotifier.cs` does not contain `CancelAsync`, `EscalateAsync`, `TryHandleTaskAsync`, or `KillAsync`.

`C1803_OrchestratorNamesBlockedSeatShortfall` pins this sentence in `server/Bundles/orchestrator.md`, on the keep-six paragraph at line 74:

`A reminder line "N seats held by Blocked tasks", or an occupancy-divergence line, names host-budget seats the stage count omits. It is not a dispatch and not a release.`

Do not raise `C1332EarlyThreshold` (28000) or the transport budget. If `the_worst_case_composition_measured_sits_far_under_the_budget` fails, shorten the sentence.

### Existing tests and the impacted-classes table

No migration, no route, no public DTO, no operation-label literal (`BeginPhase(` / `BeginFixturePhase(`), no remote script, and no statement-budget file (`WorkspaceReservationJournal.cs`, `*Dispatch*.cs`). `tests/impacted-classes.json` (CARD-1782) therefore selects no row for the paths in the table above. The `contracts` row starts to apply if Code edits `server/**/Dtos/**` or `client/src/api/**`. Do not edit those to make the new event appear in the TypeScript union.

`BlockedTaskNotifierTests` grows from 6 methods to 7. `AttentionSettingsValidatorTests` grows from 2 to 3. `AwayDigestFormatterTests` grows from 8 to 9. `AgentTaskEventTypeLedgerTests` grows from 1 to 2. `SeatOccupancyProjectionTests` grows from 4 to 5. `KeepSixReminderTests` grows from 11 to 12. None of those classes use `[Arguments]`.

## 4. Migrations

None. See the unchanged list. Do not add a column for the stamp. Down is not in play. The downgrade selector in the migrations row of `tests/impacted-classes.json` does not run for this diff.

## 5. Slices

Each slice is one Code sitting, 60 to 120 minutes. Commit the failing tests, run that slice's filter, then commit the fix and rerun the same checkpoint row. Push after each commit.

**S1. Settings, qualify rule, question-ping lookup, and the digest page.** Files: the settings, validator, `appsettings.json`, the enum, `SeatOccupancyProjection.IsRunnerHost`, `AttentionService.Seats.cs` call site, `BlockedSeatEscalation.cs`, `BlockedSeatEscalationNotifier.cs`, `AwayDigestFormatter.cs`, `BlockedTaskNotifier.cs`, `AwayDigestHostedService.cs`, `Program.cs`, and the S1 tests. The notifier's constructor is the same shape as `BlockedTaskNotifier` plus `SeatOccupancyState` and `IOptions<AttentionSettings>`. It does not take a session stopper. Closes with CP-1 and CP-2.

**S2. Reminder lines.** Files: `KeepSixReminderText.cs`, `KeepSixReminderJob.cs`, `KeepSixReminderTextTests.cs`, and the one new `KeepSixReminderTests` method. `Describe` reads `SeatOccupancyState.Current` and passes the count and the divergence facts into `Facts`. Closes with CP-3.

**S3. Docs and the bundle sentence.** Files listed in the doc pin, plus the two pin tests. Closes with CP-4.

## Verification design

Coverage is the V/R rows above. Ordinary scope is those rows. No full assembly. No Windows lane: there is no OS branch. The standing guard batch in `docs/testing-and-build.md` (`TestClassificationPolicyTests` through `TestLaneCategoryGuardTests`) is not a behaviour row of this plan. Code runs it once on the S3 build and reports it with that reason.

Build in the checkpoint tool's `bin-c1803-*` outputs. Do not pass another `OutputPath`.

The red execution of a row is the first run, at the test-only commit. The green rerun is the checkpoint that counts. Report `reruns` when that happens.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1803-s1/` | s1-rules | `/*/*/(AttentionSettingsValidatorTests*)\|(BlockedSeatEscalationRuleTests*)\|(AwayDigestFormatterTests*)\|(AgentTaskEventTypeLedgerTests*)\|(SeatOccupancyProjectionTests*)/*` | V-1, V-2, V-5, V-7, R-1 | all listed, 0 failed, 0 skipped | 25 | 8 |
| CP-2 | S1 | CP-1 | s1-page | `/*/*/(BlockedSeatEscalationNotifierTests*)\|(BlockedTaskNotifierTests*)/*` | V-3, V-4, R-2, R-3 | all listed, 0 failed, 0 skipped | 14 | 10 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1803-s2/` | s2-reminder | `/*/*/(KeepSixReminderTextTests*)\|(KeepSixReminderTests*)/*` | V-6, R-4 | all listed, 0 failed, 0 skipped | 14 | 12 |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c1803-s3/` | s3-docs | `/*/*/(BlockedSeatEscalationDocumentationTests*)\|(InstructionBundleTests*)/C1803_*` | V-8, R-5 | all listed, 0 failed, 0 skipped | 2 | 8 |

R-1 is the rule and settings class. R-2 is the notifier class. R-3 is the existing question-ping class after the lookup change. R-4 is the reminder body and the job. R-5 is the doc pin and the bundle sentence.

No `EstimatedMinutesWindows` column: the rows have no Windows-only work. No `totalTimeoutMinutes` line: the tool default `max(30, 2 * sum + 10)` is 86 for this sum of 38, and a lower explicit total is refused.

Suffix check on the roster tokens: none of `AttentionSettingsValidatorTests`, `BlockedSeatEscalationRuleTests`, `AwayDigestFormatterTests`, `AgentTaskEventTypeLedgerTests`, `SeatOccupancyProjectionTests`, `BlockedSeatEscalationNotifierTests`, `BlockedTaskNotifierTests`, `KeepSixReminderTextTests`, `KeepSixReminderTests`, `BlockedSeatEscalationDocumentationTests`, or `InstructionBundleTests` is a prefix of another class in `tests/`. `KeepSixReminderTests` is not a prefix of `KeepSixReminderTextTests`.

## Performance budget

Applies `docs/test-performance-standards.md` and `docs/testing-and-build.md`.

Durations are provisional. This selection has no measured p95. Code records `CHECKPOINT-TIMING` elapsed seconds for each row. If a row's wall time exceeds its `EstimatedMinutes`, split that row before raising the estimate. Do not add a per-test timeout attribute and do not widen an existing one. The importer's row timeout `max(15, 3 * EstimatedMinutes)` is that tool's fact (CARD-1720); it is not a measured 2x budget. Estimates are 8, 10, 12, and 8 minutes, all at or under the 15-minute advisory, so import does not warn and no row is over 30 minutes (R1 does not fire).

Phases. CP-1 and CP-3 and CP-4 include one `dotnet build` of `tests/Antiphon.Tests`. CP-2 reuses CP-1. Slot wait is separate from execution (CARD-1531). The notifier tests pay one isolated schema per harness (`TestDbFixture.CreateIsolatedSchemaAsync`, the same fixture `BlockedTaskNotifierTests` uses) because the sweep reads every Blocked task; a shared database would page another suite. That schema is the isolation, not a new container. CP-3 reuses `AntiphonWebAppFactory` with `Shared = PerTestSession` already on `KeepSixReminderTests`, so the host and its database start once per run (R3). The pure rule, formatter, settings, ledger, projection, and text tests start no host.

Recording. The checkpoint report is the phase record. These tests do not add `BeginPhase(`. External calls are the test database and an in-process fake messaging client. There is no live HTTP, git, or phone-home call (R6). Category is server attention and keep-six, project `tests/Antiphon.Tests` (R7).

## 6. Risks, operator steps, Windows

Risks:

- With `Digest:Enabled` false, or with no `DigestEnabled` channel, the new code pages nobody. That is the same gate as today's question ping (`docs/telegram.md:423-427`). The four seats in the investigation are not backfilled and are not canceled by this card.
- `ToDictionary` is fixed by inspection of the source. A production log of `Away digest sweep failed` was not pulled for this plan.
- The reminder can still be silent when shortfall is 0, keep-six is off, or the orchestrator is held. The page is the path that does not depend on keep-six.
- Copying the question ping's stamp means a second digest channel is not retried on a later sweep once one channel was stamped. Left as it is.
- `InstructionBundleTests` command-line headroom is a fixed 28000-character early threshold. The new orchestrator sentence is one line. Shorten it if that test fails. Do not raise the threshold.
- An operator who sets `SeatIdleErrorMinutes` at or above `720 * 60` cannot also satisfy the hour inequality. The validator fails closed. The default pair (180 and 6) is valid.

Operator-only, not Code:

- Enable `Digest:Enabled` and mark the operator channel `DigestEnabled` if those are off. Leave `WakeOnBlocked` at its default true.
- Do not cancel, reply to, or release the seats from this work. If any of the investigation's tasks are still Blocked, that cleanup stays the operator's, using the cancel route the investigation already names.
- Nothing is deployed. Nothing runs on the desktop.

Windows: nothing is owed. The change is server logic with no OS branch. The catalogue's windows row does not pin a checkpoint.
