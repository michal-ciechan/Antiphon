# CARD-1360 — a product seam that makes the land-delivery durable sweep testable (master-only)

Plan owner: `a94702d6`. Branch: `feat/card-task-a94702d6`.
Base: `6ec71d427574f3a1b74bfbe2042e0ea9a819d191` (the `origin/master` tip when this plan was
written). Master-only; no rc branch is touched. Round: Final. Restart: none. Publication is
only to the assigned task ref; landing rebases.

Inputs: CARD-1360 (follow-up F-1 of CARD-1305); the CARD-1305 plan
`docs/superpowers/plans/2026-10-19-card-1305-e2e-land-contract-fixtures-plan.md` on
`feat/card-task-18b06751` (`570d76d51b804de5a96ca8d823ae30b696090c69`); the Investigate record
`docs/investigations/2026-10-19-e2e-land-contract-reds.md` on the same branch;
`docs/session-runtime-invariants.md`, bullet **Completion recovery is event driven (CARD-0699)**.
Neither input document is on `master` yet; they are read from that branch and nothing here
depends on them landing first.

## Outcome

One four-line product seam and one Integration test in `tests/Antiphon.Tests`.

The seam: the queue-side scheduled completion flush in `SessionMessageQueueService` consults
the existing `LandDeliveryBoundary.DropWakeup` seam under a new boundary name,
`completion-schedule`, exactly as the direct completion wakeup in
`AgentTaskLandNotificationService` already consults it under `completion`. Production registers
the no-op boundary, so production behaviour is byte-for-byte the same.

The test: with both production wakeups cut at their call sites, a committed, unattempted
land-outcome note for an idle caller is not delivered until the hosted service's fifteen-minute
durable sweep runs, and that sweep is driven by a fake clock. The only real-time elements are the
harness's bounded in-process waits (at most ten seconds each); nothing sleeps for the sweep.

## Platform

Read on 2026-10-19 from the configured API (`GET /api/runner-defaults`,
`GET /api/session-runners`):

| Fact | Value |
|---|---|
| Global default runner | `server2` (revision 2; operator reason: default all work to server2, desktop only for truly Windows work) |
| `desktop` | platform `windows`, capacity 2 delegated tasks, 0 occupied, accepting |
| `server2` | platform `linux`, draining, not accepting new work |
| `server2-temp` | platform `linux`, capacity 10 sessions, 5 occupied, accepting |

Every checkpoint row below is the portable Integration lane in `tests/Antiphon.Tests`: an
isolated Postgres schema, the fake protocol adapter, no process spawn, no native Pty, no
Windows-only fixture. Omit `-Runner` and `-Platform`; the runtime default places it. The card
stays `platform Any`. Off Windows the checkpoint tool adds `UseAppHost=false` to the isolated
`Antiphon.Tests` builds (CARD-0671), which these rows need on Linux.

## Ground truth

| The card or brief assumes | What the code does | Verdict |
|---|---|---|
| "Inject the hosted service's clock so a test can advance it" is a product seam still to be built. | `CompletionNoteWorkHostedService` already takes the DI `TimeProvider` through a public primary constructor (`server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:15-17`). `PostLandMutationDeliveryTests.CompletionRecovery.cs:234-238` already constructs it with a `FakeTimeProvider` wrapper (`RecoveryClock`), and `C699_Backstop_waits_fifteen_minutes_between_successful_sweeps` (`:207-232`) advances that clock fifteen minutes. | The clock option needs no product change in the unit tier. It cannot serve the Windows E2E fixture: `LandClock` (`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:174-181`) offsets `GetUtcNow` only and does not override `CreateTimer`, so `Task.Delay(delay, clock, ct)` in `CompletionNoteRecoveryQueue.WaitAsync` (`server/Application/Services/CompletionNoteWork.cs:94`) would still sleep real time. |
| "Drop the queue-side `ScheduleFlush` signal" is a product seam still to be built. | `SessionMessageQueueService.cs:887-891` raises `Recovery.Check(task)` and `Recovery.ScheduleFlush(session, holdUntil ?? MinValue)` unconditionally after a committed Delegation row with a digest. The service holds no `LandDeliveryBoundary`; its only seam is `OverlayQuestionInsertBoundary` (`:79`). | Confirmed: this is the one missing seam. Only `ScheduleFlush` is a flush wakeup. `Check` can only reconstruct an owed SourceLanding completion note (`RecoverMissingAsync` filter, `CompletionNoteWorkHostedService.cs:99-104`); it cannot flush a row that already exists, so the seam guards `ScheduleFlush` alone (D-3). |
| The fifteen-minute sweep is unqualified by tests. | `C699_Backstop_waits_fifteen_minutes_between_successful_sweeps` proves the timer cadence by SQL count. `C699_Startup_*` prove `RecoverMissingAsync` at startup. No test drives `SweepAsync`'s `SessionQueuedMessages` page (`:67-83`) through `ScheduleFlush`, `FlushAsync` and `FlushIfIdleAsync` for a row that already exists. | Confirmed. That page is the lost-flush recovery path, and it is what the new test proves. |
| A lost flush is a realistic production shape. | The registered boundary is the base no-op class (`server/Program.cs:475`); `DropWakeup` is false everywhere. Both hints are in-memory. A restart between commit and hint is covered by the startup sweep (`nextSweep = now`, `CompletionNoteWorkHostedService.cs:26`). | The fifteen-minute sweep is the backstop for a hint lost without a restart. The test models exactly that: same process, both hints cut, no restart. |
| A new boundary name could change existing drop cuts. | Every override matches `boundary == "completion"` exactly: `WorktreeLandingCleanupRetryTests.cs:522`, `PostLandMutationDeliveryTests.cs:1724` (cut `lost-wakeup`), `TestHelpers/C544DeliveryRig.cs:252`, E2E `LandDeliveryOptions.cs:121`. | `completion-schedule` changes none of them; they keep recovering through the queue-side hint. CP-4 and CP-5 pin two of them. |
| Adding a constructor dependency to the queue service is safe. | `server/Program.cs:702` and `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:179` register it with `AddSingleton<SessionMessageQueueService>()`; four hand-built sites (`PhoneHomeOutageMentionTests`, `SessionTerminationSourcePersistenceTests`, `RunnerClaudeFinalLaunchArgvTests`, `TestHelpers/LandingSafetyHarness`) pass positional arguments. | A trailing optional `LandDeliveryBoundary? boundary = null` resolves to the registered no-op in production, to a test's registered boundary in harnesses that register one, and to null elsewhere; the hand-built sites compile unchanged. |
| A plain `BridgeQueueHarness` raises the queue-side hint. | The harness registers no `CompletionNoteFlushQueue`, so its queue service holds `_completionNotes == null` and raises no hint at all. | The test registers one shared instance through `ConfigureServices` (the `TurnEndGapDeliveryHarness.cs:122` pattern) and hands that same instance to the hosted service and the land-notification service. Without that the witness could not go red. |
| The E2E V28 case is settled. | `feat/card-task-1f4aaf07` (`066f4a98916233c1020bd0dc3bcf03b416b46cae`, unlanded) makes `FileBoundary.DropWakeup` record drops and V28 require a `wakeup-dropped-completion` record; `master` still asserts `completion-scan` (`tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:154`). | This plan touches no E2E file and lands independently of `1f4aaf07` in either order. Extending the E2E `lost-flush` cut to the new name is F-1b, not this card. |
| The sixty-second stranded-queue sweep could also deliver in the test. | `FlushStrandedQueuesAsync` (`SessionMessageQueueService.cs:1657`) runs from the session-health hosted service. `BridgeQueueHarness` sets `StrandedAgeSeconds = 0` but starts no session-health worker. | It cannot deliver here, so a delivery after the clock advance is attributable to the durable sweep alone. |
| `UntilAsync` is a shared helper. | It is private per test class (`PostLandMutationDeliveryTests.cs:1707` and nine others). | The new class carries its own ten-line copy. |

## Decisions

- **D-1 Seam = queue-side drop through the existing boundary.** `SessionMessageQueueService`
  gains a trailing optional `LandDeliveryBoundary? boundary = null` and guards the queue-side
  `ScheduleFlush` with `boundary?.DropWakeup("completion-schedule", row.Id) != true`. Four
  product lines plus a comment. Rejected: a new clock seam (already injectable, ground truth
  row 1); a `Delegation:CompletionSweepMinutes` setting (moves a production constant into
  configuration and an E2E test would still wait real seconds); giving the hosted service a
  private `CompletionNoteFlushQueue` with no product change (the orphan-queue arrangement that
  `C699_History_*` and `C699_Backstop_*` use for their own purposes: it tests a wiring production
  never has, three queue instances instead of one singleton, and it cannot host a red-first
  witness).
- **D-2 Boundary identity is the committed queue row id.** That row is what the sweep later
  finds, and `queue-inserted` already uses the queue id (`AgentTaskLandNotificationService.cs:159`).
  The direct wakeup keeps `note.Id`. Rejected: `sourceLandNotificationId ?? row.Id` (a mixed
  identity the E2E boundary would have to special-case).
- **D-3 `Recovery.Check` is not guarded.** It reconstructs owed SourceLanding completion notes
  and cannot flush an existing row (ground truth row 2). Dropping it would widen the cut past
  what "lost flush" means and would change CARD-0699 settlement signalling in tests.
- **D-4 The test lives in the unit tier.** New file
  `tests/Antiphon.Tests/Application/CompletionNoteDurableSweepTests.cs`, `[Category("Integration")]`
  and `[ParallelLimiter<ProcessSpawnLimit>]` like `AgentTaskLandNotificationRecoveryTests`, one
  isolated schema. Rejected: a Windows E2E case (not deterministic without a timer-capable
  fixture clock, ground truth row 1, and it spends the desktop lane); adding the method to
  `PostLandMutationDeliveryTests.CompletionRecovery.cs` (its worlds settle a whole mutation and
  its clock helper is private to that class).
- **D-5 Red-first is deterministic on the seam consult.** The witness's first assertion requires
  the boundary to have been asked `("completion-schedule", rowId)`. At S0 the product never
  asks, so CP-1 fails there before any clock advance and with no timing involved. Rejected: a
  red that depends on the leaked hint delivering inside a bounded pause (racy on a loaded host).
- **D-6 Green is decisive on three ordered facts.** (a) After the enqueue-time wakeups the
  worker re-armed its fifteen-minute timer with nothing delivered; the sync point is the second
  timer creation on the recording clock, not a sleep. (b) After `Advance(15 minutes)` the
  boundary saw `("completion-scan", Guid.Empty, callerSession)`. (c) The caller received exactly
  one complete body and the note reaches `Confirmed` through the receipt scan, the CARD-0467
  rule in `docs/testing-and-build.md` (Asynchronous outcome delivery verification).
- **D-7 Verification design is in this plan; next stage is Code.** Two slices, one red build,
  one green build, five rows, inside the 45-minute Code budget. A separate TestDesign stage
  would cost more than the Code it serves.
- **D-8 One documentation sentence.** The CARD-0699 bullet in
  `docs/session-runtime-invariants.md` gains one sentence naming the two droppable hints and
  stating that the registered production boundary drops nothing. No new section; no E2E edit.
- **D-9 No serial rows.** No row spawns a process; the limiter attribute keeps the class in the
  same lane as the other land-notification tests.

Checklist for sessions that wait for input: not applicable. No slice adds or changes a session
that waits for input; the caller is a fake-adapter harness session disposed with the test.

## The seam (S1)

`server/Application/Services/SessionMessageQueueService.cs`:

```csharp
// constructor: trailing optional parameter after `SessionStateStore? states = null`
LandDeliveryBoundary? boundary = null)
...
_boundary = boundary;   // private readonly LandDeliveryBoundary? _boundary;

// after the completion transaction commits (today lines 887-891)
if (origin == QueuedMessageOrigin.Delegation && sourceTaskId is Guid publishedTask && contentDigest is not null)
{
    _completionNotes?.Recovery.Check(publishedTask);
    // CARD-1360 test seam. The queue-side scheduled flush is a hint, never delivery evidence.
    // Production registers the no-op boundary, so nothing is dropped there; a row whose hints
    // are lost is recovered by the fifteen-minute durable sweep in CompletionNoteWorkHostedService.
    if (_boundary?.DropWakeup("completion-schedule", row.Id) != true)
        _completionNotes?.Recovery.ScheduleFlush(sessionId, holdUntil ?? DateTime.MinValue);
}
```

`docs/session-runtime-invariants.md`, CARD-0699 bullet, appended sentence:
"The direct completion wakeup (`completion`) and the queue-side scheduled flush
(`completion-schedule`) are both droppable through `LandDeliveryBoundary.DropWakeup` for tests;
the boundary production registers drops nothing, and the fifteen-minute sweep is what recovers a
row whose hints were lost (CARD-1360)."

## The test (S0 witness, S1 green)

File `tests/Antiphon.Tests/Application/CompletionNoteDurableSweepTests.cs`, one method
`C1360_DurableSweepRecoversLostFlushOnIdleCaller`, plus three private helpers:

- `LostFlushBoundary : LandDeliveryBoundary` records every `ReachedAsync` tuple and every
  `DropWakeup` call in order (one monotonic list each, under a lock) and returns `true` from
  `DropWakeup` for `completion` and `completion-schedule`.
- `SweepClock : TimeProvider` wraps `new FakeTimeProvider(DateTimeOffset.UtcNow)`, forwards
  `GetUtcNow`, and in `CreateTimer` forwards to the inner provider, records the due time and
  increments a timer count that the test can await (a `TaskCompletionSource` per threshold or a
  short poll on the count; the count is the sync point, not a sleep).
- `UntilAsync(Func<Task<bool>>, string because, int seconds = 10)`: the
  `PostLandMutationDeliveryTests.cs:1707` shape.

Steps:

1. `schema = await TestDbFixture.CreateIsolatedSchemaAsync()`; `flushes = new CompletionNoteFlushQueue()`;
   `boundary = new LostFlushBoundary()`; `clock = new SweepClock()`.
2. `h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString,
   ConfigureServices = s => { s.AddSingleton(flushes); s.AddSingleton<LandDeliveryBoundary>(boundary); } })`.
   The harness's `SessionMessageQueueService` now holds the shared queue and, after S1, the boundary.
3. `worker = new CompletionNoteWorkHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(), flushes,
   new SpecialistFailureQueue(), clock, NullLogger<CompletionNoteWorkHostedService>.Instance)`;
   `await worker.StartAsync(default)`; wait until `clock.Timers == 1` and assert
   `clock.DueTimes` contains fifteen minutes. The startup sweep has run on an empty schema and the
   loop is parked on its fifteen-minute timer.
4. `await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString))`;
   `note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId)`;
   `await new AgentTaskLandNotificationService(db, h.Queue, flushes, h.Runtime, clock, boundary).ReconcileAsync(note.Id, default)`;
   reload the note; `rowId = note.QueueMessageId!.Value`. This is the production enqueue path:
   the keyed row commits, the queue service raises `Check` and asks about `completion-schedule`
   (after S1), the land service asks about `completion`.
5. **Witness assertion A (red at S0):** `boundary.Dropped` contains `("completion", note.Id)` and
   `("completion-schedule", rowId)`, in that order of being asked is not required.
6. **Sync and negative:** wait until `clock.Timers == 2`. The `Check` wake made the loop take an
   empty session batch, run `RecoverMissingAsync` for a task that is not owed, and re-arm. Then
   assert: `h.Adapter.SubmittedBodies` is empty; the queue row is `Pending` with
   `DeliveryAttempts == 0`; `boundary.Reached` holds no `completion-scan` whose identity is
   `h.SessionId`. Take `scanCount = boundary.Reached.Count` here.
7. `clock.Inner.Advance(TimeSpan.FromMinutes(15))`.
8. **Decisive delivery:** `UntilAsync(() => h.Adapter.SubmittedBodies.Count == 1, "durable sweep did not deliver")`;
   `PromptSubmissionMatch.IsCompleteIn(note.Body, h.Adapter.SubmittedBodies[0])` is true;
   `boundary.Reached.Skip(scanCount)` contains `("completion-scan", Guid.Empty, h.SessionId)`;
   the queue row reloaded has `DeliveryAttempts == 1` and `Status != Pending`.
9. **Receipt:** `UntilAsync` until a `UserPrompt` transcript row for `h.SessionId` completes the
   body (`PromptSubmissionMatch.IsCompleteIn`), then
   `new AgentTaskLandNotificationService(freshDb, h.Queue, flushes, h.Runtime, clock, boundary).ReconcileAsync(note.Id, default)`
   and assert `State == Confirmed`, `ConfirmingPromptSequence` equals that row's `Sequence`, and
   `SubmittedBodies` is still a single item (the one-prompt rule). This mirrors
   `LandOutcomeDeliveryHarness.AssertReceiptAsync`.
10. `finally`: `await worker.StopAsync(default)`; dispose the harness, then the schema.

Why step 6 is deterministic after S1: `CompletionNoteRecoveryQueue.Take` returns only sessions
whose deadline is due; with the queue-side hint dropped no session is ever scheduled, so the loop
cannot call `flushes.TryEnqueue` before the sweep, and the stranded sweep is not running (ground
truth row 9). Why step 8 is attributable: the sweep page is the only code that emits
`completion-scan` with a session identity (`CompletionNoteWorkHostedService.cs:77-79`) and the
only remaining caller of `ScheduleFlush` once both hints are cut.

Red at S0: assertion A fails on the missing `("completion-schedule", rowId)` record. The leaked
hint would also deliver early at S0, but the test fails before it looks.

## Slices

### S0 — red witness (one commit, before the seam)

Add `tests/Antiphon.Tests/Application/CompletionNoteDurableSweepTests.cs` as specified. Nothing
else changes. Commit as `test(CARD-1360): witness the durable sweep recovering a lost flush (S0 red)`,
push, run CP-1 and report the named assertion.

### S1 — seam, green, one doc sentence (one commit)

`server/Application/Services/SessionMessageQueueService.cs` (constructor parameter, field, the
guard) and the sentence in `docs/session-runtime-invariants.md`. Commit as
`feat(CARD-1360): queue-side completion flush consults the delivery boundary (S1)`, push, run
CP-2 to CP-5.

### Follow-ups, not slices

- **F-1b** Extend the E2E `lost-flush` cut (`LandDeliveryOptions.cs:121`) to `completion-schedule`
  only together with a fixture clock that overrides `CreateTimer`; without that, V28 would wait
  real minutes or depend on `FlushStrandedQueuesAsync`. Belongs with CARD-1305 F-1's E2E arm.
- **F-2** `RecoveryClock` (`PostLandMutationDeliveryTests.CompletionRecovery.cs:251`) and the new
  `SweepClock` are near-duplicates; promote one to `TestHelpers` when a third consumer appears.

## Verification design

| ID | Required outcome / coverage |
|---|---|
| V-1 | With both production wakeups cut at their call sites, the boundary is asked about `completion` and `completion-schedule` (red first at CP-1), and after the worker re-arms nothing is delivered: adapter empty, row Pending with zero attempts, no `completion-scan` for the caller (CP-2). |
| V-2 | After a fifteen-minute fake-clock advance the sweep records `completion-scan` for the caller, the flush delivers exactly one complete body, the row carries one attempt, and the note reaches `Confirmed` with the matching prompt sequence (CP-2). |
| R-1 | Hint-driven recovery and the timer cadence are unchanged by the new constructor dependency: `C699_Settlement_event_recovers_failed_publication_without_polling` and `C699_Backstop_waits_fifteen_minutes_between_successful_sweeps` stay green (CP-3). |
| R-2 | Existing `completion`-only drop cuts still recover through the untouched queue-side hint: `C459_IdleReceiptRecoversLostFlush` (CP-4) and `C478_G137_LandWakeup` (CP-5). |

Red rows are expected failures; Code reports them as red with the named assertion and does not
"fix" them. A red row that passes before its fix means the witness is a stub and must be
reworked before continuing. CP-1's red must be assertion A (the missing `completion-schedule`
record), not a timeout and not a compile error; a build failure, fixture error or zero-test run is
not red evidence.

Running the table (one run per committed slice group; call `wait` again while it returns 75):

```powershell
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1360-durable-sweep-seam-plan.md --rows CP-1 --expected-source-sha <S0 sha>
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1360-durable-sweep-seam-plan.md --rows CP-2,CP-3,CP-4,CP-5 --expected-source-sha <S1 sha>
```

Commit S0 before the first run and S1 before the second; do not edit source while a run is in
flight. Delete every `bin-c1360-red` and `bin-c1360-green` directory (about one per project)
before finishing. Run
`scripts/check-evidence-diff.ps1 -BaseRef 6ec71d427574f3a1b74bfbe2042e0ea9a819d191 -HeadRef <pushed sha>`
and the `git fetch origin master` plus `git merge-tree --write-tree origin/master HEAD` landable
check; report both. No AppHost or runner restart; the test owns an isolated schema and a fake
adapter.

Manual acceptance: inspect the fresh TRX identity and count for each row against `Expect`; for
CP-1 confirm the failure message names the `completion-schedule` record; for CP-2 confirm the test
output or TRX shows the `completion-scan` record and a single submitted body. No UI or deployment
work.

### Mutation handoff

Every control remains pending for method-scoped SourceLanding Mutation after Review and land.
Candidate location `docs/superpowers/mutations/` (unbound).

| PC | Method / variant pending |
|---|---|
| PC-1 | `C1360_DurableSweepRecoversLostFlushOnIdleCaller`: remove the `completion-schedule` guard so `ScheduleFlush` is unconditional; must fail at assertion A; restored must pass. |
| PC-2 | Same method: change `AddMinutes(15)` at `CompletionNoteWorkHostedService.cs:39` to `AddMinutes(30)`; the fifteen-minute advance must not fire and the test must fail at the bounded delivery wait; restored must pass. |
| PC-3 | Same method: remove the `completion-scan` `ReachedAsync` call at `CompletionNoteWorkHostedService.cs:77-78`; delivery still happens and the test must fail at the scan assertion; restored must pass. |

### Cost

Ordinary floor is the `EstimatedMinutes` sum: **24 minutes** for five rows including two isolated
`Antiphon.Tests` builds on Linux (estimated at about six minutes each; not measured in this
plan). Add authoring of the test file and the seam (about 15 minutes): ask `-ExpectAbout 45`.
CP-3 to CP-5 are existing land and mutation worlds measured in earlier runs at well under a
minute each after the build. Additional runs require an actual failure or a missing execution.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 | `tests/Antiphon.Tests -> bin-c1360-red/` | red-lost-flush-sweep | `/*/*/CompletionNoteDurableSweepTests/C1360_DurableSweepRecoversLostFlushOnIdleCaller` | V-1 red | expected red: 1 executed, 1 failed at assertion A (no completion-schedule drop record) before any clock advance, 0 skipped | 1 | 8 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1360-green/` | lost-flush-sweep | `/*/*/CompletionNoteDurableSweepTests/C1360_DurableSweepRecoversLostFlushOnIdleCaller` | V-1, V-2 | 1 executed, 1 passed, 0 failed/skipped | 1 | 8 |
| CP-3 | S1 | CP-2 | hint-recovery | `/*/*/PostLandMutationDeliveryTests/(C699_Settlement_event_recovers_failed_publication_without_polling*)\|(C699_Backstop_waits_fifteen_minutes_between_successful_sweeps*)` | R-1 | 2 executed, 2 passed, 0 failed/skipped | 2 | 3 |
| CP-4 | S1 | CP-2 | completion-drop-cleanup | `/*/*/WorktreeLandingCleanupRetryTests/C459_IdleReceiptRecoversLostFlush` | R-2 | 1 executed, 1 passed, 0 failed/skipped | 1 | 2 |
| CP-5 | S1 | CP-2 | completion-drop-land | `/*/*/PostLandMutationDeliveryTests/C478_G137_LandWakeup` | R-2 | 1 executed, 1 passed, 0 failed/skipped | 1 | 3 |
