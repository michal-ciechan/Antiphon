# CARD-0535 — create-time dispatch hold "retries forever, never dispatches"

Investigated 2026-09-19 (task 1b5b60d6, Investigate stage). Evidence: `AgentTasks`,
`AgentTaskEvents`, `AgentTaskLandRequests`, `AgentTaskLandings`, `AgentTaskLandNotifications`,
`CapacityRecoveryWaits`, `ModelAvailabilityHolds`, `RoutingPins`, `AgentSessions` rows on the
canonical Postgres; the session-runner log for 2026-09-14 (`%TEMP%\antiphon-logs\session-runner-20260914.log`,
14-day retention); the server log for 2026-09-14 is past its 5-day retention (oldest surviving file is
`antiphon-20260915.log`), so the two 09-14 server-log lines quoted below come from the CARD-0481
investigation written on 09-15. Code is read at HEAD (`7ae80b70`) and at `daf849c2b`, the last
dispatcher change before the incident (landed 2026-09-14 12:12Z), which is what the server ran that night.

## Verdict

**The hold did not retry forever and the dispatcher did notice the condition clear.** Task `9fb36757`
(Plan CARD-0492, Worktree) dispatched at **22:07:31Z** — `DispatchedAt` and `AgentId` are set on the row,
session `93edf213` was created, the runner was tailing its transcript by 22:08:11Z — and the operator's
cancel landed at **22:08:22Z**, 51 s after dispatch. The last "lease occupied" event is at **22:05:46Z**,
43 s *before* the lease was actually released (22:06:29Z), so "still retrying after the lease cleared"
is not supported by the stored rows; "agentId/dispatchedAt null the whole time" describes the 64 minutes
before 22:07:31Z.

Every hold the task sat in was a real, sequential condition, re-evaluated fresh on every 5-second tick
(no cached snapshot anywhere on the path). What made it *look* stuck is a visibility problem with
three parts: the lease hold writes an identical `Held` event every tick with no log line and no holder
name; on 09-14 every other hold reason was silent after the first (`everHeld`); and nothing escalates a
task that has been Queued-and-held for an hour, whereas a land request in the same state gets a Warning
at 5 min and an Error at 15 min.

One thing is not explained: the 105 s between the last lease event (22:05:46Z) and the dispatch
(22:07:31Z), of which 62 s are after the lease was released. See "Remaining uncertainties". It bounds
the extra latency at about a minute; it does not change the verdict.

## Timeline (UTC, 2026-09-14)

Four tasks matter besides `9fb36757`: the Shared writer `439ba588` that everything queued behind, the three
Worktree tasks whose lands ran back-to-back, and the earlier Plan attempt `d81b76fd`.

| UTC | Row | What |
|---|---|---|
| 17:51:19 | `439ba588` "CARD-0508 finish remaining V/R" (Custom, **Shared**, `C:\src\Antiphon`) | dispatched; runs 4 h in the canonical checkout |
| 18:12:53 | `85ea60c8` (Investigate CARD-0492, Worktree) | land requested; Held `repository_or_source_writer` behind `439ba588`; Warning at 18:17:58, Error at 18:27:56 (land monitor) |
| 18:38:37 | `b45ac852` (Investigate CARD-0501, Worktree) | land requested; same hold; Warning 18:43, Error 18:53 |
| 19:11:51 | `d81b76fd` "Plan CARD-0492 settlement fix" (Custom, **Shared**) | Held: `running task 439ba588 … is already writing in this shared checkout (Shared ↔ Shared)` — one event, silent thereafter |
| 21:02:58 | `d81b76fd` | canceled by operator after 1 h 51 min Queued |
| 21:03:05 | `9fb36757` "Plan CARD-0492 settlement fix" (Plan, **Worktree**) | created |
| 21:03:06 | `9fb36757` | Held: `CARD-0492's kept branch feat/card-task-85ea60c8 (task 85ea60c8) is landing and is not yet in HEAD` — `EvaluateCardSiblingBaseAsync` hold; **one event**, silent for 53 min |
| 21:03:42 | `6cb9c0d9` (Code CARD-0443, Worktree, same repo) | dispatched in 6 s — no sibling of CARD-0492, so no hold; its worktree creation is the "lease occupied; owner unknown" both pending lands logged at 21:03:45 |
| 21:51:25 | `439ba588` | Succeeded |
| 21:51:29 | `85ea60c8` land | admitted (`Land admitted; hold released`), holds the repository lease |
| 21:51:58 | `be0020e5` (Code CARD-0508, Worktree) | land requested → Queued (a land is running) |
| 21:56:17 | `85ea60c8` land | `landed operation=8215add6`; `CleanupCompletedAt` 21:56:17.51; branch deleted |
| 21:56:18 | `b45ac852` land | admitted 0.7 s later; holds the lease (`8215add6` → `8fc2402b`) |
| 21:56:18 | `9fb36757` | first `Held: repository mutation lease is occupied or unavailable.` — the sibling guard released within 1 s of the land; the task reached `DispatchOneAsync` and lost the lease to the next land |
| 21:56:18 → 22:05:46 | `9fb36757` | **95** identical lease `Held` events, one per tick (4–6 s apart, two gaps of 19 s and 24 s) |
| 22:01:06 | `b45ac852` land | `landed operation=8fc2402b`; cleanup complete 22:01:06.49 |
| 22:01:07 | `be0020e5` land | `StartedAt`; verification `dotnet build; dotnet run --project tests/Antiphon.Tests` 22:02:50–22:04:10; push 22:05:17; `RemoteConfirmedAt` **22:06:03** (the "confirmed" time on the card); cleanup 22:06:03–**22:06:28** |
| 22:05:46.87 | `9fb36757` | last lease `Held` event (same tick also delivered check `1c86dee0` at 22:05:46.92) |
| 22:06:29.44 | `be0020e5` | `landed operation=0a2a8cf1`; lease released (`await using` scope of `AgentTaskLandService.RunAsync`) |
| 22:06:34–36 | notifications `8834f9a9`, `fce8714f` | the outcome notes for the two earlier lands finally enqueue — `AgentTaskLandNotificationService` probes the lease before enqueueing an Outcome note and returns while it is occupied, so their `EnqueuedAt` is independent confirmation that the lease was held continuously 21:56:17 → 22:06:29 |
| 22:06:55 | `2b3ca0fe` (label diagnosis specialist) | created by the diagnose sweep |
| **22:07:31.63** | `9fb36757` | `DispatchedAt`; events `Worktree created at C:\Antiphon\worktrees\card-task-9fb36757 …` and `Dispatched to agent 'task-9fb36757' (fable)`; session `93edf213` created; `AgentId = 57b98868` |
| 22:07:39 | server log (via CARD-0481 doc) | `Dispatched task 9fb36757 (… at fable)` and `released: its scope 'null' no longer intersects a running task`; `2b3ca0fe` delivered 22:07:39.04 in the same tick |
| 22:08:11 | runner log | `Tailing transcript … 93edf213 …jsonl` |
| **22:08:22** | `9fb36757` | `Canceled.` (operator `POST …/cancel`); session ended 22:08:22.46 |
| 22:08:30 | `39f0a593` (Plan CARD-0492, Worktree) | recreated; dispatched 22:08:33 — no sibling landing, no land running, so nothing to hold |

So the operator saw "Plan CARD-0492 not starting" for **3 h 3 min** across two task identities: `d81b76fd`
(1 h 51 min, one silent Shared↔Shared hold) then `9fb36757` (64 min: 53 min sibling-landing hold with one
event, 10 min lease hold with 95 events, ~2 min unexplained).

## Mechanism: how each hold is (re-)evaluated

All three holds are decided per tick from live state; none records or reads a snapshot.

**Tick.** `AgentTaskDispatcherHostedService` (`server/Infrastructure/Orchestration/AgentTaskDispatcherHostedService.cs:35-71`)
runs `TickAsync` on a `PeriodicTimer` of `PollIntervalSeconds` = 5 (`DelegationSettings.cs:16`). A tick runs
eleven sweeps serially, then loads `queued` (`AgentTaskDispatcher.cs:331-333`, `OrderBy(CreatedAt)`) and walks
it (`:423`). A tick that throws logs `Delegation dispatch tick failed` and the next timer fires; the tick's own
duration is logged only at Debug (`RuntimePhase`), which the server does not emit.

**Sibling-landing hold** (`EvaluateCardSiblingBaseAsync`, `AgentTaskDispatcher.cs:2988-3068`, called at `:589`).
For every Succeeded/Blocked Worktree sibling on the same card with a kept branch, it runs git each tick:
`KeptBranchExistsAsync` (`:3025`; the landed branch is deleted by the land's cleanup, `BranchRemoved = t` on
`8215add6`), `ContainsPatchesAsync(tip, baseRef)` (`:3028`), and holds only if the sibling's
`LandRequestedAt` is still set (`:3044`). Re-evaluation is proven by the rows: the land completed at
21:56:17.51 and the task's first *lease* event is 21:56:18.58, i.e. the next tick already passed the guard.

**Repository mutation lease hold** (`DispatchOneAsync`, `AgentTaskDispatcher.cs:3085-3100`).
`RepositoryMutationLease.TryAcquireAsync` (`server/Infrastructure/Git/RepositoryMutationLease.cs:7-29`) opens
`<common>/antiphon/landing.lock` with `FileShare.None` and returns null on `IOException` (someone else holds it)
or when `RepositoryChildJournal.HasUnfinishedAsync` finds any file under `<common>/antiphon/children/`
(`RepositoryChildJournal.cs:60-90`). Nothing is cached; every tick is a fresh open. The lease records no
owner, which is why both the dispatcher's event and the land's own (`AgentTaskLandService.cs:214-219`,
"owner unknown") cannot say who holds it. Lands hold it for their whole `RunAsync` scope (`:214` →
`:376`), including verification and cleanup: 4 m 48 s (`8215add6`), 4 m 48 s (`8fc2402b`), 5 m 22 s
(`0a2a8cf1`). Dispatches hold it through worktree creation.

The base rate confirms the lease hold releases promptly. Across all 63 tasks that ever logged the lease
`Held` event (2026-09-09 → 2026-09-18), the interval from the last lease event to `DispatchedAt`:

| interval | tasks | note |
|---|---|---|
| 4–6 s (next tick) | 39 | normal |
| 8–16 s | 8 | second or third tick |
| 45 s – 66 min | 13 | every one a member of a group of 3–6 tasks released by the same land and then staggered by the 6-slot concurrency cap (silent), or, on 09-17, held on a traced Shared↔Shared reason |
| 77 s (`da78907b`, 09-17) | 1 | server restart between the last event and the first tick (log: `Antiphon server starting` 16:22:29Z) |
| **105 s (`9fb36757`)** | 1 | this card; alone in the queue; no restart (runner log shows the server's ~6.5-s session polls uninterrupted through the window) |
| never | 1 | `3bfe742a`, the CARD-0481 case |

**Shared↔Shared hold** (`SharedWriterLeaseProjection.Decide`, `AgentTaskDispatcher.cs:447-465`) is what held
`d81b76fd`; it is decided from the live `busyScopes` query each tick.

## Why it read as "retrying forever"

1. **A chain of legitimate waits behind one 4-hour Shared writer.** `439ba588` (Shared, canonical checkout)
   held three lands (`85ea60c8`, `b45ac852`, `be0020e5`) and one Shared Plan (`d81b76fd`) until 21:51:25.
   The lands then ran back-to-back, each holding the lease ~5 min for `dotnet build; dotnet run
   --project tests/Antiphon.Tests`. A Worktree task on CARD-0492 had to wait for its sibling's land
   (CARD-0215/0508 design) and then for the lease behind two unrelated lands. Nothing on `9fb36757` said
   any of this; the land-side holds and their Warning/Error escalations are on the *other* tasks' rows.
2. **The lease hold is the one hold that bypasses `TraceHeldAsync`.** `AgentTaskDispatcher.cs:3093-3099`
   (identical at `daf849c2b:2979-2987`) adds a `Held` row on every tick and logs nothing, while every other
   hold goes through `TraceHeldAsync` (`:713-726`) which writes only on a change of reason. Result: 95 identical
   rows in 10 minutes on the task drawer — the "retrying every 5 seconds" the card describes — and no
   holder name to check against.
3. **On 09-14 every other hold was silent after the first** (`everHeld`, `daf849c2b:374-380`): the sibling hold
   wrote once at 21:03:06 and then nothing for 53 minutes. CARD-0481's fix (`1feca45b4`, 09-15) replaced this
   with per-reason `lastHeld` (`:378-386`), so a reason *change* is now traced; the per-tick lease write is unchanged.
4. **No dispatch-side escalation exists.** A land request gets `Warning: Land Held …` at
   `LandWarningSeconds` = 300 and `Error:` at `LandErrorSeconds` = 900 (`AgentTaskLandMonitorService.cs:31-38`,
   `DelegationSettings.cs:573-574`). A Queued task held by the dispatcher has no equivalent: no age
   threshold, no attention item, no backoff, and the concurrency-cap skip (`AgentTaskDispatcher.cs:427-432`)
   still writes no event at all. Searching `server/` for a queued-age or held-too-long threshold finds none.
5. **The release log line is misleading.** `:678-683` logs `released: its scope '…' no longer intersects a
   running task` for any task in `lastHeld`, whatever the hold actually was.

## Relation to CARD-0481

Different path, same symptom class. CARD-0481 was a `LiveSession` capacity wait carrying the task's id
after a kind reroute, gating dispatch silently (`HasUnfinishedCapacityWaitAsync`, fixed 09-15). `9fb36757`
has no `CapacityRecoveryWaits` row (by `TaskId` or `ConsumerKey`), no `ModelAvailabilityHolds` for
ClaudeCode that night (the only active hold was `fddaa608`, Grok, 17:53Z → 09-15 11:18Z), no `RoutingPins`
row with `NotBefore`, and the cap was 2 of 6 (`6cb9c0d9`, `c40cf169` active). The CARD-0481 fix does not
touch the lease hold's per-tick write, the sibling hold's behaviour, or escalation.

## Remaining uncertainties

- **The 105 s silent stretch (22:05:46.87 → 22:07:31.63).** `9fb36757` was the only non-specialist Queued
  task; the lease was held until 22:06:28 (a tick reaching `DispatchOneAsync` in 22:05:47–22:06:28 would have
  written a lease event; none did) and free from 22:06:29 (a tick reaching it would have dispatched, as the
  22:07:31 one did). So no tick ran the dispatch loop for `9fb36757` for ~95 s. Checked and excluded: server
  restart (runner log shows the server's session polls every ~6.5 s throughout), concurrency cap, capacity
  wait, model hold, `NotBefore` pin, check-interpreter work (runs in `AgentTaskCheckHostedService`, off the
  tick). Not excludable without the 09-14 server log: a single long tick (gaps of 19–24 s between lease
  events show ticks were already stretching during the land's verification/push; a sibling-guard git call
  hitting `WorktreeManager.GitTimeout` = 30 s three times, `WorktreeManager.cs:22`, would fit), or a tick
  that threw in the sibling guard (`:589` is outside the `try` at `:605`) and was logged only as
  `Delegation dispatch tick failed`. The 09-15 log shows 4 such failures (all Npgsql transient/timeout), 0
  on 09-16–19. What would resolve it: tick duration at Information above a threshold, or a reproduction
  with a queued card-bound Worktree task while a verifying land runs in the same repo.
- Whether the operator's cancel was issued from an observation before or after 22:07:31 cannot be read back;
  the card text ("agentId: null and dispatchedAt: null the whole time") matches any observation up to 22:07:31.

## Affected code paths

- `server/Application/Services/AgentTaskDispatcher.cs:3085-3100` — lease hold: per-tick `Held` row, no
  `TraceHeldAsync`, no log, no holder. `:427-432` — cap skip, no trace at all. `:378-386`, `:713-726` —
  `lastHeld`/`TraceHeldAsync` (post-CARD-0481). `:589-600` — sibling hold. `:678-683` — release log wording.
  `:2988-3068` — `EvaluateCardSiblingBaseAsync` (git per tick, outside the dispatch `try` at `:605`).
- `server/Infrastructure/Git/RepositoryMutationLease.cs:7-29`, `RepositoryChildJournal.cs:60-90` — ownerless
  file lock plus child-journal fence.
- `server/Application/Services/AgentTaskLandService.cs:214-236, 376` — lease held for the entire land run.
- `server/Application/Services/AgentTaskLandMonitorService.cs:31-38`, `DelegationSettings.cs:572-574` — the
  land-side escalation that has no dispatch-side counterpart.
- `server/Infrastructure/Orchestration/AgentTaskDispatcherHostedService.cs:35-71` — tick loop; duration
  unobservable at the configured level.
- Tests: no test pins the per-tick lease write; `WallRerouteDispatchTests.cs:187-197` seeds lease `Held`
  rows only as a prior-hold fixture.

## Not done, noted

- Fix idea (one line, not designed): route the lease hold through `TraceHeldAsync` with the holder named
  (record the acquiring task/land id beside `landing.lock`), and add a dispatch-side held-age escalation
  mirroring `LandWarningSeconds`/`LandErrorSeconds` that writes a Warning/Error event and an attention item
  (covering the cap skip too), rather than a backoff — the re-evaluation itself already works.
- A tick-duration Information log above, say, 15 s would have answered the 105 s question directly.
