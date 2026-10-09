# CARD-1161: monotonic dead-session grace

Plan date: 2026-10-09. Next stage: **code**. Verification is designed below so Build
can run the closed checkpoint table. This dispatch writes no production code and no tests.

Planning baseline: fetched `origin/master`
**59d67869b6a60f13510827baf1aeb499b1b93280**. `git merge-tree --write-tree` of
`HEAD` onto that commit produced tree `ac429346b782aea06c81a3db42aa4d162113ad7a`,
the same tree as `origin/master`, with exit 0. No rebase. Card platform is Any.
`GET /api/runner-defaults` was revision 2 (supported kinds Grok, ClaudeCode, Codex).
`GET /api/session-runners` returned three runners, platforms windows and linux.
This plan pins no host and no `-Platform`. Checkpoints name the Unit lane.

Read CARD-1161 (`7da62f73-e7bc-436f-ae8d-64e82065dcc3`) with
`scripts/card.ps1 get CARD-1161 -Board Antiphon`.

## Ground truth

Citations are `server/Application/Services/` unless noted, at the baseline above.
`Dispatcher` means `AgentTaskDispatcher.cs`.

| Question | What the code does | Consequence |
|---|---|---|
| Where is the grace decided? | `Dispatcher:2554` sets `now = UtcNow()`. `:2573` stores that wall instant in `DeadSessionFirstSeenState.FirstSeenAt`. `:2574` continues while `now - firstSeen < grace`. | One wall reading per pass. A later pass subtracts the stored wall instant. A rollback shrinks the difference. A forward jump grows it. |
| What is stored? | `DeadSessionFirstSeenState.cs:22` is an in-memory `ConcurrentDictionary<Guid, DateTime>`. `:28` `GetOrAdd`s the wall instant. `:34` `Forget` removes it. `:38` `IsTracking` reports the key. Registered singleton at `server/Program.cs:609`. | Not a database column. A new process starts with an empty map. |
| When is the stamp taken? | After the runner list returns (`Dispatcher:2534-2546`) and the session is absent from the runner's Running set (`:2563-2570`). An unreachable runner returns 0 at `:2546` without calling `FirstSeenAt`. A listed Running session calls `Forget`. A session that is not dead calls `Forget` at `:2528`. Null runner client or null state returns 0 at `:2484`. `grace <= 0` returns 0 at `:2488`. | The stamp means "a sweep had a runner answer and the session was not Running". It does not mean `EndedAt` or `DispatchedAt`. |
| What does the grace open? | The `continue` at `:2574` is in front of the transcript read, bind recovery (`:2587`), sitting-report settlement (`:2604`), `CommitRecoveryObligations.ShouldHold` (`:2626`), `DecideAbsentLaunchAsync` (`:2639`), and `FailAndNotifyAsync` (`:2649`). | A jump that passes `:2574` runs whichever later gate matches. A rollback that fails `:2574` runs none of them. |
| Ordinary failure | `IsDeadSession` (`AgentTaskLiveness.cs:60`) is status Stopped or Failed, a set `EndedAt`, a missing row, or a null session id. It does not read the task's Working/Dispatched status. `a_working_task_behind_a_dead_session_is_failed_too` expects Failed after `Advance` of five minutes. The sweep comment at `Dispatcher:2454` says this path does not kill. | A Working task with a dead session is an existing failure after the grace. This card does not add a session stop. |
| CARD-1149/1150 hold | `DecideAbsentLaunchAsync` (`:2679`) is reached after the grace gate. The grace `continue` does not call it. Runner-unknown reason, not Working, positive absence, and a certificate can return `Held`, which calls `Forget` (`:2642`). `Withheld` does not `Forget` (`:2643` continues). A shape that returns `NotThisShape` falls through to `FailAndNotifyAsync`. | The hold and the failure share this grace. The certificate's own 0-to-5-second monotonic age (`:2823-2825`) is a different clock and stays as CARD-1153 left it. |
| Commit-recovery hold | `ShouldHold` (`CommitRecoveryObligations.cs:50-51`) is `now - StartedAt < hold` with the wall `now` from `:2554`. A young obligation continues without `Forget` (`Dispatcher:2627-2632`). | That helper stays wall-clock. The grace gate is in front of it, so a jump that does not pass the new grace gate does not evaluate it. |
| Statement budgets | `C1149_C1150_Statement_budgets` expects 18, 18, and 4 (`DelegationDispatchRecoveryBoundaryTests.cs:25-32`). The 4 is `SessionReconciliationService.ScanAsync` for a Starting session. The 18s are ticks whose session is Starting or Running, so `IsDeadSession` is false. | The grace edit adds no database command. Those three numbers stay. |
| How existing tests elapse the grace | `PastGraceAsync` and `DueAsync` call `FakeTimeProvider.Advance` (`AgentTaskDeadSessionReconciliationTests.cs:787`, `DelegationDispatchRecoveryBoundaryTests.AbsentLaunch.cs:634`). `SplitClock` (`AbsentEvidence.cs:1037-1046`) adds independent `Elapsed` and `Wall` on top of that base. `Advance` moves the base, so both of `SplitClock`'s readings move with it. | `Advance` still elapses a monotonic gate that uses the same `TimeProvider`. Skew rows must set `Elapsed` and `Wall` and must not call `Advance` or `DueAsync`. |

`AgentTaskLiveness` is pure (`AgentTaskLiveness.cs:18-19`): no clock. The attention
projection does not apply this grace.

This change does not add or change a session that waits for input. It adds no
release deadline. Parking and CARD-0079 are unchanged.

## Decisions

### D-1. Monotonic elapsed time is the grace

On the sweep that today calls `FirstSeenAt`, also take
`_timeProvider.GetTimestamp()` and store that `long` in
`DeadSessionFirstSeenState`. Replace the wall subtraction at `:2574` with:

- `age = _timeProvider.GetElapsedTime(storedStamp)`
- continue, and do not `Forget`, when `age < TimeSpan.Zero` or `age < grace`
- otherwise fall through to the gates already below that line

`age >= grace` is the same boundary as today's `now - firstSeen < grace`: equal
to the grace does not continue. `DeadSessionFailGraceMinutes` stays 3.

The stamp and the elapsed read use the dispatcher's injected `TimeProvider`.
Do not call `Stopwatch.GetTimestamp`.

Rejected: requiring the wall difference as well as the elapsed time. A rollback
would still keep `:2574` shut, which is the delay this card names. Rejected:
one captured wall deadline in the CARD-1143 F1 shape. This method already
captures `now` once per pass; the skew is between passes. Rejected: a static
`Stopwatch`, because `Advance` would stop opening the gate and `DueAsync` /
`PastGraceAsync` would stop reaching the hold and the failure.

### D-2. A negative age withholds and keeps the stamp

`GetOrAdd` runs before the comparison, so the comparison has a stamp from this
process's provider. A negative `GetElapsedTime` is not an elapsed grace.
Continue without `Forget` and without replacing the stored stamp. Do not add a
wall fallback at this `if`.

### D-3. No migration. A restart starts a new grace

The dictionary stays process memory. A `TimeProvider` timestamp from one process
is not an elapsed time in the next. Do not add a column, a conversion, or a
backfill. After restart the map is empty: the next sweep that would have called
`FirstSeenAt` stores a new stamp and waits again. That delays the failure and
the CARD-1149 hold. It does not skip the grace. The class comment at
`DeadSessionFirstSeenState.cs:15-18` says a restart waits the window again and does not skip a failure.

### D-4. Later gates stay in their current order

Do not move bind recovery, sitting-report settlement, `ShouldHold`,
`DecideAbsentLaunchAsync`, or `FailAndNotifyAsync`. A grace `continue` does not
call them. `Held` still forgets the stamp. An absent-launch `Withheld` still
keeps it. `NotThisShape` still fails and forgets. The wall `now` at `:2554`
remains the argument to `ShouldHold`. The grace comparison does not read it.

Rejected: changing `CommitRecoveryObligations.ShouldHold` in this card. Its
`StartedAt` is a stored event time, and this card's stamp cannot outlive the
process.

### D-5. A Working task with a dead session still fails after the elapsed grace

`a_working_task_behind_a_dead_session_is_failed_too` stays as written, including
its Failed expectation. A forward jump that leaves the monotonic age inside the
grace leaves that task Working. The new rows assert the runner kill count and
the delegate stopper stay empty, including on the rows that Fail or Block.
CARD-0079 is not called. Do not add a blanket skip of `AgentTaskStatus.Working`:
that would make the existing test fail.

### D-6. No new database command

The new reads are `GetTimestamp` and `GetElapsedTime` on the in-memory stamp.
`C1149_C1150_Statement_budgets` stays 18, 18, and 4. Do not add a query, a
`SaveChanges`, or a log that writes.

### D-7. Server activation is an AppHost restart. No runner restart

The edit is server code and comments, plus tests and one owner bullet. No
runner, client, or schema change. Land does not by itself run the new gate.
After the server process restarts, D-3 applies to tasks that were already dead.

### D-8. Two comments name the clock

In `DelegationSettings.DeadSessionFailGraceMinutes`, replace the summary that
says the dispatcher fails the task after the grace. Keep the CARD-0056
paragraph and the `<= 0` escape hatch. The new summary says:

- the wait is `TimeProvider.GetElapsedTime` from the in-memory stamp taken on
  the first sweep that received a runner list and did not see the session Running
- a negative elapsed time does not act
- a wall-clock jump does not act while that elapsed time is inside the grace
- a wall rollback does not keep the gate shut once the elapsed time has reached the grace
- a server restart drops the stamp and the next observation waits again
- past the grace, a certified unattempted launch is held Blocked (CARD-1149/1153)
- a shape that is not that hold keeps the existing failure path
- the sweep does not stop the session

Add one bullet to `docs/session-runtime-invariants.md` immediately after the
bullet that cites `C1153_Certificate_age_is_monotonic`. Lead:
**The dead-session grace is monotonic elapsed time (CARD-1161).** Include
`FailDeadSessionTasksAsync`, `DeadSessionFirstSeenState`, `GetElapsedTime`,
`DeadSessionFailGraceMinutes`, the negative-age withhold, the restart behavior
in D-3, that the wall `UtcNow` beside the stamp feeds `ShouldHold` and is not
the grace decision, and the two pin names
`C1161_Dead_session_grace_is_monotonic` and
`C1161_Owner_names_the_monotonic_grace`. Do not edit historical plans or
`docs/superpowers/specs/2026-08-17-card-0021-dead-session-task-reconciliation.md`.

## Slices

### S1. Gate and database matrix (50 min)

- `server/Application/Services/DeadSessionFirstSeenState.cs` — store the `long`
  timestamp. `Observe(Guid, long)` returns the first stored stamp. Keep `Forget`
  and `IsTracking`. Remove `FirstSeenAt`. Its caller is `Dispatcher:2573`.
- `server/Application/Services/AgentTaskDispatcher.cs` — D-1 at the grace gate.
  No other decision in the method changes.
- `server/Application/Settings/DelegationSettings.cs` — the D-8 summary.
  Leave the CARD-0056 paragraph and the `<= 0` escape hatch.
- `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.DeadSessionGrace.cs`
  — new partial. `C1161_Dead_session_grace_is_monotonic` with the 13 arguments
  in the table below.

Use the existing `SplitClock`, `OpenSweep`, `SweepHost.SweepAsync`, `SeedAsync`,
and `Quiet`. Do not pass `projectsRoot` (bind recovery then returns None without
a scan). Leave the counting runner's session list empty. Isolated schema per
argument, same as the other boundary rows.

Two-sweep arguments: `SweepAsync`, then set `Elapsed` and `Wall`, then
`SweepAsync`. Do not call `DueAsync` or `Advance` on those rows.

`restart-ordinary`: fresh `DeadSessionFirstSeenState`, clock already at
`Elapsed` and `Wall` of 10 minutes, one `SweepAsync`.

Shapes:

- ordinary — `SessionReason = "process vanished"`, status Dispatched
- hold — default `AbsentShape` (runner-unknown reason, pending brief, no prompt)
- working — status Working and `SessionReason = "process vanished"`

| Argument | Shape | Elapsed | Wall | Expect | Red on the wall gate at this baseline |
|---|---|---|---|---|---|
| forward-jump-ordinary | ordinary | 1 s | 5 min | Dispatched, tracking | yes (Failed) |
| forward-jump-hold | hold | 1 s | 5 min | Dispatched, tracking | yes (Blocked) |
| forward-jump-working | working | 1 s | 5 min | Working, tracking | yes (Failed) |
| rollback-ordinary | ordinary | 5 min | 1 min | Failed, not tracking | yes (stays Dispatched) |
| rollback-hold | hold | 5 min | 1 min | Blocked, not tracking | yes (stays Dispatched) |
| negative-ordinary | ordinary | -1 s | 5 min | Dispatched, tracking | yes (Failed) |
| negative-hold | hold | -1 s | 5 min | Dispatched, tracking | yes (Blocked) |
| inside-ordinary | ordinary | 1 min | 1 min | Dispatched, tracking | no |
| aligned-ordinary | ordinary | 5 min | 5 min | Failed, not tracking | no |
| aligned-hold | hold | 5 min | 5 min | Blocked, not tracking | no |
| aligned-working | working | 5 min | 5 min | Failed, not tracking | no |
| exact-ordinary | ordinary | 3 min | 3 min | Failed, not tracking | no |
| restart-ordinary | ordinary | 10 min, one sweep, fresh state | 10 min | Dispatched, tracking | no |

Dispatched and Working expectations: status unchanged, no Blocked event,
`IsTracking` true. Failed: status Failed, `IsTracking` false. Blocked: status
Blocked, `FailureReason` equals `DispatchLaunchAbsentReason`, attempt 1,
`IsTracking` false. Every argument: runner kills 0 and the stopper's killed list
empty (`Quiet`).

Before the S1 commit, with the production `if` still the wall subtraction (the
baseline, or that predicate restored and then put back), run this method and
record the seven red arguments in the table's last column. Then apply D-1 and
run the four mutations below, one at a time, on
`/*/*/DelegationDispatchRecoveryBoundaryTests/C1161_Dead_session_grace_is_monotonic`,
and restore the file after each. Those runs are unlisted red proofs, each with
that reason. They are not checkpoint rows. A green argument under a mutation is
not the proof that the mutated clause matters.

### S2. Owner bullet and the budgets that must stay (30 min)

- `docs/session-runtime-invariants.md` — the D-8 bullet.
- Same test partial — `C1161_Owner_names_the_monotonic_grace` reads that bullet
  and the `DeadSessionFailGraceMinutes` summary and requires the phrases D-8
  names (`CARD-1161`, `GetElapsedTime`, `DeadSessionFirstSeenState`,
  `DeadSessionFailGraceMinutes`, `Blocked`, and both `C1161_` method names).

No production edit in S2 other than the owner bullet. The settings summary is
already in S1.

## Verification design

### V-1. Grace skew at the database seam

`DelegationDispatchRecoveryBoundaryTests.C1161_Dead_session_grace_is_monotonic`
(13 results). Real PostgreSQL schema, production `FailDeadSessionTasksAsync`,
`SplitClock` elapsed and wall stepped independently between the two sweeps
(one sweep for `restart-ordinary`). The expectation column above is the
assertion set. Covers D-1, D-2, D-3, D-4, and D-5.

### V-2. The owner text names this gate

`DelegationDispatchRecoveryBoundaryTests.C1161_Owner_names_the_monotonic_grace`
(1 result). Requires the phrases in S2. Covers D-8.

### R-1. Statement budgets

`DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets`
(3 results: 18, 18, 4). Unchanged fixtures. Covers D-6.

### R-2. Existing grace callers that use `Advance`

`AgentTaskDeadSessionReconciliationTests` (25 results, no `[Arguments]` at this
baseline). Includes `the_grace_has_to_elapse_before_anything_is_failed` and
`a_working_task_behind_a_dead_session_is_failed_too`. Covers D-1's boundary and
D-5. `DueAsync` callers are not all in this class; R-1's tick and the aligned
hold argument are the boundary-suite witnesses that `Advance` still opens the gate.
The ordinary Failed caller receipt is
`DelegationDispatchRecoveryBoundaryTests.C1161_Failed_caller_note_has_one_complete_user_prompt`
(eligible, busy). The enqueue-failure case is open as CARD-1167.

### Production mutation controls

Pending for method-scoped SourceLanding Mutation. Detecting filter for each row:
`/*/*/DelegationDispatchRecoveryBoundaryTests/C1161_Dead_session_grace_is_monotonic`.
The mutation is the grace `if` in `FailDeadSessionTasksAsync` after D-1.
Restore after each cycle. Zero executed tests is not a red.

| PC | Mutation | Arguments that must fail |
|---|---|---|
| PC-1 | Delete `age < TimeSpan.Zero \|\|` | negative-ordinary, negative-hold |
| PC-2 | Replace the `if` condition with `true` | rollback-ordinary, rollback-hold, aligned-ordinary, aligned-hold, aligned-working, exact-ordinary |
| PC-3 | Replace the `if` condition with `false` | forward-jump-ordinary, forward-jump-hold, forward-jump-working, inside-ordinary, restart-ordinary |
| PC-4 | Change `age < grace` to `age <= grace` | exact-ordinary |

### Cost

Authoring: S1 50 minutes, S2 30 minutes. Ordinary checkpoint floor: **42 minutes**,
the sum of `EstimatedMinutes`. `-ExpectAbout` for Code: 122 minutes.
No migration. AppHost restart after land (D-7). No runner restart.

### Checkpoints

Serial rows: these drivers share PostgreSQL, and overlapping pools surface as
SQLSTATE 53300. The checkpoint tool supplies `UseAppHost=false` off Windows.
Do not add a second `UseAppHost` property.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1161-s1/` | grace-clock | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1161_Dead_session_grace_is_monotonic*` | V-1 | 13 executed, 0 failed/skipped | 13 | 18 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1161-s2/` | grace-owner | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1161_Owner_names_the_monotonic_grace*` | V-2 | 1 executed, 0 failed/skipped | 1 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | `CP-2` | grace-budgets | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-1 | 3 executed, totals 18/18/4, 0 failed/skipped | 3 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | `CP-2` | grace-existing | `/*/*/AgentTaskDeadSessionReconciliationTests/*` | R-2 | 25 executed, 0 failed/skipped | 25 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
