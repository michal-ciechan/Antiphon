# Dispatch save failure recurrence a4f88c28 (CARD-1490 Review)

Investigated 2026-10-22 by Debug task `800ea23f` from the server2 runner mirror.
Read-only: no restart, settings change, database write, card write or source change.
Follows [2026-10-22-dispatch-save-failure.md](2026-10-22-dispatch-save-failure.md)
(Debug `c80c961f`, CARD-1471).

## Finding

Task `a4f88c28-ec80-458e-8794-1b97dad0a5e5` (Review CARD-1490, server2, base
`45e7e8775`) failed with the same pattern as `f9acde3c`. That incident was proven to be
the `IX_Agents_Slug` (SQLSTATE 23505) collision in the two-phase remote dispatch. Every
retained fact matches it, and the live server still runs the source that has the defect.
**CARD-1471 would have prevented it.** It is the third failure of this kind today and the
only kind there has ever been: all three rows with this reason are from today, and all
three are server2 remote-prep dispatches.

**Evidence gap, stated plainly:** the inner exception (constraint name, colliding slug)
comes only from the desktop server Serilog file and the `antiphon-postgres` container's
stderr log. Neither can be reached from the server2 runner container. Postgres runs with
`logging_collector=off`, `log_destination=stderr`, so SQL cannot read its log either.
There is no HTTP log route, and SSH out of the container fails host-key verification
(`docs/logs.md`), which was not bypassed. The constraint and slug are therefore
**inferred, not quoted**. The desktop confirmation commands are at the end.

## Runtime identity

- `GET /api/version` at about 13:12Z: `ceb6e3d6b78471db6831258699c493b8eb2d619b`
  (`land-v2`, `operator-shutdown-v1`). This is the tip of `origin/rc/2026-10-20` and
  the same SHA as the `f9acde3c` incident.
- On rc, `ResolveAgentAsync` (rc `AgentTaskDispatcher.cs:7318`) still does
  `_db.Agents.Add(agent)` for a fresh pool delegate. Its only caller (`:5833`) runs before
  the `HeldForRemotePrep` save. The CARD-1471 fix (`e7825b823`) is on master and not in
  rc.
- Postgres `pg_postmaster_start_time() = 2026-10-21 07:37:26Z` (unchanged).

## Timeline (UTC, `AgentTaskEvents`, read-only SQL)

| UTC | a4f88c28 |
|---|---|
| 12:59:04.090 | Created (Review/High explicit, runner server2 requested). |
| 13:01:26.771 | Held: same-card base inspection pending. |
| 13:02:33.287 | Desktop worktree created from `45e7e8775`. |
| 13:02:41.041 | Land of `16c3d5af` held: *repository mutation lease is occupied by task a4f88c28 (dispatch); status=Queued*. |
| 13:02:47.147 | Held: remote workspace preparation in flight (push + mirror requested). **The first claim commits here, with the task still Queued; on rc this commit includes the fresh `task-a4f88c28` agent.** |
| 13:02:51.264 | `16c3d5af` land admitted (lease released by the dispatch hand-off). |
| 13:02:53.045 | Held: prepared workspace is ready; awaiting dispatch admission. |
| 13:03:15.303 | **Failed:** `Dispatch failed before a session existed: An error occurred while saving the entity changes...`. `AgentId`, `AgentSessionId`, `DispatchedAt` null; `RemotePrepFailures=0`; `FailureCode` null. |

The task was queued for 4m11s (12:59:04 → 13:03:15), not 12 minutes. The concurrent
landed-operation event (`baa2812c` at 13:02:28, `ResumePublication`) and the `16c3d5af`
land only share the repository mutation lease with this dispatch. That lease is
serialised and was released before the ready claim. Nothing ties either one to the
failed save, and the c80c961f analysis showed that this defect needs no concurrent
writer.

## Same cause: the evidence

1. **Identical event signature in all three failures.** Each one goes: worktree created →
   "remote workspace preparation is in flight" → "prepared workspace is ready" → fails
   with the generic save message 18-25 s later, with no session or agent bound and no
   remote-prep failure:

   | Task | Title | Prep in flight | Ready | Failed | Ready→fail |
   |---|---|---|---|---|---|
   | `f9acde3c` | CARD-1462 option A S4 | 08:33:41 | 08:33:47.587 | 08:34:05.023 | 17 s |
   | `5fdc4967` | Codex Debug: S2 land resume_approval_changed (CARD-0167) | 10:03:06.776 | 10:03:15.641 | 10:03:40.452 | 25 s |
   | `a4f88c28` | Review CARD-1490 | 13:02:47.147 | 13:02:53.045 | 13:03:15.303 | 22 s |

   `f9acde3c` is the incident with a quoted 23505 `IX_Agents_Slug` /
   `Key ("Slug")=(task-f9acde3c)`.
2. **The message identifies a plain `DbUpdateException`.** A
   `DbUpdateConcurrencyException` has a different message ("expected to affect 1
   row(s)..."). The only entity this claim inserts beyond its own task/session/event rows
   is the fresh agent.
3. **No `Agents` row with slug `task-a4f88c28` exists now.** This matches the
   pool sweeper deleting the orphaned unbound row after the failure, as logged for
   `f9acde3c` ("Swept stale pool delegate").
4. **The replacement shows the masking order working in its favour.** Replacement
   `6cd4edb3` (created 13:03:34) went ready at 13:07:07.184 and dispatched 89 s later at
   13:08:36.519. Its surviving agent `20dcc97a-…` has
   `CreatedAt = 13:08:36.519 = DispatchedAt`. The row that survived was therefore
   inserted by the *final* claim, which can only happen if the preparation-time row was
   swept first. That is the documented two-insert sequence, with the sweep happening
   before the ready claim this time.
5. The prefix query finds exactly one `a4f88c28…` task, so a collision between two
   different task IDs is ruled out.

**The two competing writers** are not two dispatches. As with `f9acde3c`, they are
the same task's own two claims: (a) the 13:02:47 preparation-only commit that inserted
`task-a4f88c28`, and (b) the ready claim at about 13:03:15 (a ready-waiter/nested-ready
dispatch, which skips `RetireIdleWarmAgentsAsync`) that tried to insert a second agent
with the same slug.

Other failures today: the query `Status=Failed AND FailureReason LIKE 'Dispatch failed%'`
since 2026-10-22 00:00Z returns exactly one reason group, this one, with **3 rows**
(08:34:05, 10:03:40, 13:03:15). All-time, the reason has 3 rows, all from today.
140 distinct tasks reached "prepared workspace is ready" today, so about 2% hit the
collision. `5fdc4967` was not individually traced to its Postgres line either. Its
attribution rests on the same signature.

## Would CARD-1471 have prevented it: yes

`e7825b823` makes `ResolveAgentAsync` return the fresh agent untracked
(`(Agent, Fresh)`) and adds it in `DispatchOneAsync` only beside the session, after the
mirror exists. The `HeldForRemotePrep` commit then has no agent to persist, and the
ready claim inserts the single row it binds. Its S1 red witness on master reproduced
23505 at this exact second claim with no sweep between claims (V-2), and V-1 to V-3
plus R-1 to R-5 were green at `90320c8c0` (task `82104d2c` report, `08765c68c`). The
fix also replaces the generic reason with constraint and SQLSTATE
(`DispatchFailureDetail` → `AgentService.DescribeDbFailure`), so any recurrence after
the port can be identified from the task row without desktop log access.

## Is the rc pick the right remedy: yes, and it is small

- Pick **`e7825b823`** (fix, `AgentTaskDispatcher.cs` only, +28/−6) and **`0aa1473a9`**
  (test partial `PhoneHomeTaskDispatchProjectionTests.DispatchSave.cs` plus a plan doc).
  Drop the plan doc (`docs/superpowers/plans/2026-10-22-card-1471-dispatch-slug-plan.md`),
  because rc carries no plan docs, and drop the docs-only `e61fb10fd` / `08765c68c`.
- `git merge-tree --write-tree --merge-base=<c>^ origin/rc/2026-10-20 <c>` is clean for
  both commits (trees `20f638e9…` and `3790de74…`).
- Dependencies are present on rc. `AgentService.DescribeDbFailure(DbUpdateException)`
  exists (`AgentService.cs:1879`). `ResolveAgentAsync` has a single caller on rc
  (`:5833`). The test partial's siblings `PhoneHomeTaskDispatchProjectionTests.cs` and
  `.TaskToken.cs` are byte-identical between rc and `0aa1473a9^`. rc's dispatcher differs
  from the fix parent by 86+/37− lines in unrelated areas (CARD-1427/1428/1430 lease and
  inspection work). The hunks apply cleanly, but only a build can prove they compile.
- Verification for the rc port: build plus CP-1 (`C_dispatch_save_*`, 3) and CP-2
  (the 5 R-rows in the `(Name*)` form from `e61fb10fd`) on the rc pick. Then ordinary
  Review, rc land, and an AppHost restart onto the new rc tip. A master land is not
  activation.
- Until it is deployed, the failure is not transient but it is retryable by
  re-dispatch. A replacement usually succeeds once the sweeper has removed the orphan, as
  `2620772f` and `6cd4edb3` did. Do not add a blanket SaveChanges retry.

## Desktop confirmation (optional, closes the quoted-evidence gap)

From the canonical desktop checkout (server timestamps are +01:00):

```powershell
rg -n -A 12 'Failed to dispatch task a4f88c28' C:\src\Antiphon\server\logs\antiphon-20261022*.log
rg -n "Swept stale pool delegate 'task-a4f88c28'|Swept stale pool delegate 'task-5fdc4967'" C:\src\Antiphon\server\logs\antiphon-20261022*.log
docker logs --since 2026-10-22T13:03:00Z --until 2026-10-22T13:03:30Z antiphon-postgres 2>&1 | Select-String -Context 0,2 'IX_Agents_Slug'
docker logs --since 2026-10-22T10:03:30Z --until 2026-10-22T10:03:50Z antiphon-postgres 2>&1 | Select-String -Context 0,2 'IX_Agents_Slug'
```

The expected results are `ConstraintName: IX_Agents_Slug` and
`Key ("Slug")=(task-a4f88c28)` / `(task-5fdc4967)`.

## Method

Read-only SQL (`BEGIN READ ONLY … ROLLBACK`) against the desktop dev Postgres at
`antiphon.desktop.codeperf.net:17280` from the server2 mirror covered `AgentTasks`,
`AgentTaskEvents` and `Agents`. Also read: `GET /api/version`, and git
`merge-tree`/`grep` against `origin/rc/2026-10-20` and master. Builds and tests: 0.
