# Post-restart health of rc 6e79f3e13 (2026-10-22)

Debug task 61836f59, read-only. At about 15:33Z the AppHost restarted from rc `ceb6e3d6b` to rc
`6e79f3e13`. That range has 27 commits and no EF migration. This note compares the two hours before
the restart (13:30-15:32Z) with the time after it (15:32Z to 15:54:44Z, the whole life of 6e79f3e13: about 21 minutes). All figures come from the desktop Postgres, read through a `BEGIN READ ONLY` session from the
server2 runner mirror, and from the public HTTP API. Nothing was changed, restarted or killed.

**Verdict: healthy while it ran.** The restart caused no new failure class, no dispatch save
failures and no stale closing nudges. No task failed because of it. Three queued dispatches were
held for 3-6 minutes and then went through on their own. The open items were already open before
the restart: the 76fe1072 debt loop and the noise from server2 release kills.

**This rc ran for only about 21 minutes.** A second AppHost restart began at about 15:54:44Z. The
last event was at 15:54:44.477Z, the API returned 502 at 15:56:55Z, the server restarted at
15:57:36Z, and the first event after it was at 15:58:57.888Z. The live server is now rc/2026-10-20
tip `0714a9a89`, which is 94 commits past 6e79f3e13. That restart applied migration
`20261022095749_AddWorktreeResidueCursorLastOutcome` and carries the CARD-1446 rc port, including
the S2b attempt cap `d613fb9e8`. At 15:58:17Z the 76fe1072 debt moved to `Held`
`settlement_sync_attempts_exhausted` with an hourly restamp, which closes item 7.1. This note does
not assess 0714a9a89 beyond those facts. The second restart did not visibly interrupt anything either. At 15:59Z no session
had ended since 15:54Z, and a1f403eb, 4921bd9e, 5db5df66 and this task were all still Dispatched
on server2.

## Evidence limits

- **The desktop server Serilog file (`C:\src\Antiphon\server\logs\antiphon-*.log`) cannot be read
  from server2.** `docs/logs.md` lists it as a desktop-file source, and SSH from inside the runner
  container fails host-key verification. Section 1 therefore uses the durable stand-ins:
  `AgentTaskEvents` Warning, Held, Failed and Refused rows, `AgentIncidents`, and `/api/attention`.
  A log-only exception, such as a hosted-service fault that writes no row, would not show up here.
  Before calling the log clean, the desktop lane should run
  `scripts/logs.ps1 -Source desktop-server` from 15:33Z onwards.
- `/health` is not reachable from server2. Through Caddy (`$ANTIPHON_API/health`) it returns the
  SPA `index.html`, and direct `:17202` and `:17204` time out. The desktop session runner's
  `:17204/build-slots/metrics` cannot be reached either.
- The `pg_stat_statements` reset and the Hangfire dashboard were not used, because both need
  operator access.

## 1. Server errors and warnings

The server log could not be read (see above). Startup facts recorded in the database:

- Server start was at 15:33:30Z. `AgentIncidents` kind 80 "startup. runners: server2 platform linux
  -> unknown" was followed at 15:34:12Z by "server2 dispatch-eligible no -> yes". The previous
  startup row is 2026-10-22 01:43Z.
- The last task event before the restart is at 15:30:42.863Z and the first after it at
  15:34:33.596Z, a gap of 3m51s.
- No migration was due: the range adds none, and the newest `__EFMigrationsHistory` row is still
  `20261020142734_AddStandingAutomaticFreshRecovery`.
- Orchestrator instructions were re-notified twice, to five orchestrators each time: hash
  `581ee7d1` at 15:33:31Z and `3b183779` at 15:34:12Z. This is the usual post-start pattern; the
  same pairs and triples appear at 09:25-09:27, 09:35-09:37 and 11:49-11:52.

Warning, Held, Failed and Refused event classes, normalised (digits become N, ids become `<x>`):

| Type | Class | 2 h before | After |
|---|---|---|---|
| Held | prepared workspace is ready; awaiting dispatch admission | 23 | 6 |
| Held | remote workspace preparation is in flight for runner 'server2' | 23 | 6 |
| Held | same-card base inspection is pending | 34 | 2 |
| HeldAged | dispatch held Ns ...; reason=Held: same-card base inspection | 2 | 2 |
| Warning | explicit kind/level: the <Role>/Medium row was not consulted (Review, Plan, Investigate, Debug, Code) | 17 | 4 |
| Held | repository mutation lease is occupied by task <x> purpose=land | 11 | 0 |
| Warning | CARD-N's kept branch ... has committed work absent from the rebased HEAD | 9 | 0 |
| Warning | skipped gpt-N-astra (already chose an earlier candidate) | 8 | 0 |
| Warning | progress=none; reason=runner_reported_commit_not_pushed | 3 | 0 |
| Warning | Runner sync pending: runner_sync_lease_busy | 3 | 0 |
| Warning | sibling inspection incomplete (inspection_timeout / candidate_limit) | 3 | 0 |
| Failed | Merge-back failed: repository_busy (8e9f68b4, 14:54Z) | 1 | 0 |
| Held | Repository mutation lease is occupied by task <x> (dispatch) | 1 | 0 |

There is no new class after the restart. Every post-restart class also appears before it. Events
that appear only before the restart (lease-held lands, kept-branch warnings and sibling-inspection
timeouts) do not appear after it because nothing in the after-window took that path.

Incidents after the restart (`AgentIncidents`):

| Kind | Count after | Note |
|---|---|---|
| 82 orchestrator instructions notified | 10 (2 hashes x 5 orchestrators) | Normal at startup |
| 80 startup / runner-eligibility | 3 | Startup 15:33:30Z; server2 eligible 15:34:12Z; ineligible 15:54:44Z at the second restart |
| 75 settlement/operator kill after transport loss: Session not found | 5 | Same pattern as before the restart (26 in 2 h) |
| 74 deferred kill: missing | 5 | Same pattern |
| 76 DelegateReleaseUnresolved (Error) | 3 (8bc47225, 00ce1756, ab161f15) | Same pattern (11 in 2 h) |
| 39 OrchestratorInvestigation | 1 | school-revision; detection only |

The 74, 75 and 76 kinds occurred at the same rate before the restart. The release-kill pair is analysed under
item 7.

`/api/attention` at 15:41Z had 259 items. Three were new since the restart: `RunnerUnavailable` for
`server2-temp`, one `RecentCriticalIncident` (`DelegateReleaseUnresolved`, 8bc47225) and one
`OrchestratorInvestigation` (school-revision). `server2-temp` is drained (`draining: true`,
`unavailableReason: stale`) and has 0 open tasks and 0 sessions, so its alarm is expected. There is
**no `RepositoryChildJournalStale`** row, so the registered child journals passed the startup
inspection.

The **build-slot metrics route (CARD-1489)** is a session-runner route, not a server route. The
server2 shared broker `$ANTIPHON_BUILD_SLOTS_URL/metrics` answers **HTTP 405**, while
`GET /build-slots` works. server2 reports `buildVersion 81cfb267a` (rc/2026-10-18 era), so the
route is not deployed on server2 until a server2 runner rollout. That is expected after an
AppHost-only restart, not a defect. The desktop runner's route could not be reached from here.

**Land service**: two lands ran after the restart and both were clean: 8bc47225 (requested 15:40:21Z, landed 15:41:58Z, 1m37s) and 00ce1756 (15:42:22Z to 15:43:49Z, 1m28s). None
was held, none was refused, and none hit `resume_approval_changed`. Before the restart there were
11 lands in two hours, also clean, with the slowest at 5m34s (d8dc3f5c).

## 2. Dispatch

| Window | Dispatched to agent | Dispatch failed | Save failure / IX_Agents_Slug | model_disabled | source_sync_unverified | resume_approval_changed | Created |
|---|---|---|---|---|---|---|---|
| 00:00-13:30 | 153 | 3 | 3 tasks | 0 | 0 | 2 land refusals (26316601) | 306 |
| 13:30-15:32 | 23 | 0 | 0 | 0 | 0 | 0 | 68 |
| 15:32-15:54:44 (6e79f3e13) | 7 | 0 | 0 | 0 | 0 | 0 (2 raw text hits are Check snapshots quoting this brief) | 9 |

In the window after the restart, 10 tasks settled Succeeded, none Failed and none Canceled, and 2 landed.

- **There are no save failures since the restart.** The `IX_Agents_Slug` class last appeared at
  13:03:15Z (a4f88c28). Today's three cases (f9acde3c 08:34Z, 5fdc4967 10:03Z, a4f88c28 13:03Z)
  were all before 13:30Z, and the 13:30-15:32 window was also clean across 23 dispatches. So the
  zero after the restart confirms CARD-1471 but does not prove it yet: at today's rate of 3 in 153
  dispatches, a clean sample needs about 100 dispatches to mean much.
- None of the new refusal codes has appeared since the restart. Before it, `resume_approval_changed`
  appeared only on 26316601 (09:55Z and 09:59Z land refusals, the CARD-1485 case), and
  `model_disabled` and `source_sync_unverified` never appeared. Refusals that come back as an HTTP
  409 from the dispatch route leave no row, so only the server log can rule them out completely.
- **Same-card base inspection (CARD-1480)**: the two tasks held across the restart got their
  worktrees at 15:36:20Z (00ce1756, held 664 s in total) and 15:36:38Z (a1f403eb, held 424 s), about
  3 minutes after the server started. Before the restart, holds were usually 11-70 s, with
  occasional holds of 160-343 s while a land held the lease. No `source_sync_unverified` refusal has
  appeared.

## 3. Repository lease and sync debts

- **Debt 79267872 (task 76fe1072, CARD-0100 review, Succeeded 2026-10-14)** is still `Pending` with
  `runner_sync_inspection_unavailable`. It is retried about every 4m45s, the same cadence as
  before: attempt 2239 at 15:34:35Z, 2240 at 15:39:20Z, 2241 at about 15:44Z, and 2242
  at 15:49:24Z. That is about 12 an hour, roughly 290 a day; the brief recorded 2,168+ before
  the restart. The cause and the fix are already on file:
  `docs/investigations/2026-10-22-card-1446-stale-landings-and-sync-debt.md` (Kind C2), and the
  CARD-1446 S2b attempt cap, which holds a debt exhausted after 48 claims (`577094e97` /
  `78297d864`). **That fix was on master but not on rc 6e79f3e13**, so the loop ran for the whole life of
  this rc. The rc port `d613fb9e8` arrived with 0714a9a89, and the debt was Held exhausted at
  15:58:17Z (see the verdict).
- **Time to Ready for settlement syncs.** Three debts were created before the restart: 01ec1135
  (15:19:10Z), 9ba4132b (15:26:03Z) and d205268d (15:27:46Z). All three went Ready within 45 s of
  startup, at 15:34:20-15:34:45Z, after 935 s, 498 s and 402 s. Debts created earlier today took
  563-1,715 s (typically about 600-900 s), so these sit inside the usual range.
  No settlement-sync debt was created while 6e79f3e13 ran, so there is no Ready latency to measure
  after the restart.
- **No land was held behind the lease** after the restart, and the dispatch lease-wait events are
  all from before it. Before the restart, eleven `Held: repository mutation lease is occupied by task
  <x> purpose=land` events were recorded. Each was a dispatch waiting behind a running land, and the
  longest land took 5m34s.

## 4. Closing-report nudge (CARD-1474)

- `AgentTasks.ReportNudgedAt` has no rows after 15:32Z; the last nudge of any kind was at 07:05Z
  today. No closing nudge was typed after the restart, so none was typed into a settled task.
- Older residue, not caused by the restart: the nudges for 090fe438 (2026-10-17 11:11Z) and
  e434a301 (2026-10-17 22:02Z) are still `Pending` with 0 delivery attempts after their tasks
  settled. Their sessions (a59957e2 and 3ef7e603 on server2-temp) ended on 2026-10-17, so they
  cannot be typed. In one historic case a nudge was typed after settlement: e56ee135's nudge was sent
  at 2026-10-19 19:01:00Z, after the task completed at 19:00:42Z. That is exactly the class
  CARD-1474 retires.

## 5. Version, health and child journals

- `GET /api/version` returns `6e79f3e13d66f0bf0dfad7de9a8a60db27bb2d45` with capabilities
  `land-v2` and `operator-shutdown-v1`.
- `/health` cannot be reached from server2 (see Evidence limits). `GET /api/session-runners` shows
  `desktop` available and eligible, `server2` available and eligible (1 reconnect, 0 pending events,
  12 ms catch-up), and `server2-temp` draining and stale.
- Registered child journals: no `RepositoryChildJournalStale` in `/api/attention`. The startup
  inspection raised nothing.

## 6. Tasks interrupted by the restart

No task failed or was canceled because of the restart, and no session ended between 15:28Z and
15:38Z. The tasks open at 15:31Z fall into four groups:

| Task | State at restart | After the restart |
|---|---|---|
| 8bc47225 (CARD-0151 Plan) | Prepared, awaiting dispatch admission since 15:30:42Z | Dispatched 15:34:52Z, settled 15:39:53Z, landed 15:41:58Z |
| 00ce1756 (CARD-0173 Plan) | Held for same-card base inspection since 15:25:17Z | Worktree 15:36:20Z, dispatched 15:37:10Z, settled 15:42:14Z, landed 15:43:49Z |
| a1f403eb (CARD-1483 S1 Code) | Held for same-card base inspection since 15:29:34Z | Worktree 15:36:38Z, dispatched 15:37:13Z; it was still working when the second restart came |
| 4921bd9e, 5db5df66, ab161f15 (server2 Code) | Working on server2 | Kept running; checks at 15:34-15:40Z; ab161f15 took a build slot at 15:39:36Z and settled Succeeded at 15:50:22Z |

In each case the dispatch was delayed, not lost. Older stuck rows that predate the restart and were
untouched by it: check tasks 53437ee8 (Dispatched since 2026-10-21) and b5cc2c6d (Queued since
2026-10-21), and label tasks a31c0322 and 86802002 (Blocked on a haiku quota since 10-19 and 10-22).

## 7. Things that look wrong

1. **The 76fe1072 sync-debt loop ran all through 6e79f3e13.** The CARD-1446 S2b cap was on master
   only. This is **now resolved on the live rc**: the 0714a9a89 restart carried the rc port
   `d613fb9e8`, and the debt moved to `Held settlement_sync_attempts_exhausted` at 15:58:17Z. No
   card is needed.
2. **Six stale `NeedsResolution` land requests are re-evaluated on every tick**: 67a63a4f,
   d2117ffc, cf214423, 022820cb and ce048398 (2026-09-17 to 09-22) and f4fd378a (2026-10-20). Each
   has `LastEvaluatedAt` within the current minute. This is already counted in the CARD-1446
   investigation (table row "Land request ... NeedsResolution | 7"), so no new card is needed.
3. **A false `DelegateReleaseUnresolved` Error is recorded on server2 settlements** (card
   candidate). After the restart, three settled server2 tasks (8bc47225, 00ce1756 and ab161f15) recorded the
   same triple: kind 75 "settlement/operator kill after transport loss: Session '<id>' was not
   found", kind 76 `DelegateReleaseUnresolved` (Error, "release kill ... did not end it ... kept
   Idle for the janitor"), and then kind 74 "deferred kill: missing". Each session row ended as
   `Status 4` with "kill not delivered: Session ... was not found". The runner says the session is
   already gone, yet the release path treats this as still live and raises an Error. Counts per day:
   kind 75 and kind 74 went from 0 to 55 on 2026-10-21 and to 168 on 2026-10-22, and kind 76 runs
   at 64-76 a day. In the two hours before the restart, 11 of 26 server2 settlements raised it. The
   effect is noise in the attention feed: 92 `RecentCriticalIncident` items, nearly all of them this
   pair. It also leaves pool rows parked Idle for the janitor when they could have been deleted.
   Card searches for "release kill", "kept Idle for the janitor", "deferred kill: missing" and "kill
   after transport loss" found no existing card. This predates the restart.
4. **Not a defect:** the server2 broker answers 405 on `/build-slots/metrics` until server2 is
   rolled to a build that contains CARD-1489 S1. Anyone polling it from server2 before then should
   expect 405.

## Reproduce

From a server2 runner mirror, install `pg@8` into a scratch directory and open a `BEGIN READ ONLY`
session against `antiphon.desktop.codeperf.net:17280`. Credentials come from
`docker-compose.dev.yml`; never print them. The windows used were
`"At" < '2026-10-22 15:32'` (before) and `>= '2026-10-22 15:32'` (after). The tables were
`AgentTaskEvents` (types 0, 1, 9, 12, 18, 20, 21, 37), `AgentTaskSyncDebts`,
`AgentTaskLandRequests`, `AgentIncidents`, `AgentTasks.ReportNudgedAt` / `ReportNudgeMessageId`
joined to `SessionQueuedMessages`, and `AgentSessions`. HTTP: `GET /api/version`,
`/api/attention`, `/api/session-runners`, `/api/session-runners/{id}/status`,
`/api/agent-tasks/pipeline?projectId=...` and `$ANTIPHON_BUILD_SLOTS_URL[/metrics]`.
