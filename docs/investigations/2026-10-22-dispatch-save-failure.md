# Dispatch save failure f9acde3c: an agent committed before its task binding

Investigated 2026-10-22 by Debug task `c80c961f`. Production observation only;
no restart, settings change, direct database mutation, or qualification-process
operation was performed. The requested follow-up board card is the only service
mutation. No application source or tests were changed or executed.

## Finding

At **08:34:04.905 UTC**, PostgreSQL rejected an **INSERT into `public.Agents`**
with SQLSTATE **23505**, constraint **`IX_Agents_Slug`**, conflicting value
**`task-f9acde3c`**. This is an agent-lifecycle defect in the split remote-dispatch
transaction. Dispatch persists a newly created, unbound pool agent while handing
off remote preparation; its next claim creates another agent with the same slug.
A pool sweep between the two claims can hide the defect by deleting the first row.

The incident is not an AgentTask/Card optimistic-concurrency conflict, deadlock,
serialization error, bad title/goal length, or database outage. A generic transient
retry would not fix it. The second request was a new task, `2620772f`, whose own
unbound agent was swept before its successful launch.

The exact choice of ready-waiter versus ordinary tick for the failing second claim
is not recorded in the warning. The code and retained evidence establish the
two-claim defect and the different cleanup ordering, without needing to assert a
particular concurrent writer or a second server instance.

Fix card: **CARD-1471**, `03f32d39-8286-4713-8d5c-51389077030f`, created in
**Backlog** on Antiphon board `8988ca03-7414-47ad-b0b6-51556c701703` at
08:52:36.125946 UTC. Its description includes the repair scope, red-witness shapes
and the closed checkpoint table below. No task was spawned.

## Runtime and evidence identity

- Live `GET http://localhost:17202/api/version` returned
  `ceb6e3d6b78471db6831258699c493b8eb2d619b`, with `land-v2` and
  `operator-shutdown-v1`. Server PID 180816 was
  `C:\src\Antiphon\server\bin\Debug\net9.0\Antiphon.Server.exe`, started
  2026-10-22 01:43:09 UTC. The listener on 17202 is Aspire's dcp proxy.
- Investigation branch started at `a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49`.
  Source was also read directly from the live build's Git commit. The relevant
  agent allocation/preparation/binding sequence is unchanged at the investigation
  base; intervening dispatcher changes concern sibling inspection and failure
  notification. The stack's line numbers below belong to the live build.
- The active main database **has not moved to server2**. The server PID has
  established connections to `::1:17280`; local Docker maps
  `antiphon-postgres` port 17280 to 5432. Read-only SQL in that container returns
  the incident task, its events, and the replacement task. Postgres reports
  `pg_postmaster_start_time() = 2026-10-21 07:37:26.119417+00`.
  `pg_stat_activity` shows the desktop Docker gateway as the client. No connection
  string, secret store, or remote credentials were read to establish this.
- Server evidence is in
  `C:\src\Antiphon\server\logs\antiphon-20261022_001.log`, not the already
  full `antiphon-20261022.log`. Directory metadata initially looked stale; reading
  the rolled file confirmed current content. Server timestamps are **+01:00**;
  PostgreSQL timestamps are UTC. `09:34:05.004 +01:00` is `08:34:05.004Z`.
- Failed task: `f9acde3c-a082-4cfb-9882-40cde9783314`; replacement:
  `2620772f-e433-4658-8337-bebaa522d05b`; project:
  `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`; CARD-1462:
  `711d0573-7d2a-498a-9fe8-6b9a289ff9ae`.

## Exact failure and timeline

Server log lines 44209-44248 contain the warning and full exception chain:

```text
2026-10-22 09:34:05.004 +01:00 [WRN] Failed to dispatch task f9acde3c
Microsoft.EntityFrameworkCore.DbUpdateException:
  An error occurred while saving the entity changes. See the inner exception for details.
 ---> Npgsql.PostgresException (0x80004005):
  23505: duplicate key value violates unique constraint "IX_Agents_Slug"
SchemaName: public
TableName: Agents
ConstraintName: IX_Agents_Slug
Routine: _bt_check_unique
AppDbContext.SaveChangesAsync ... AppDbContext.cs:158
AgentTaskDispatcher.DispatchOneAsync ... AgentTaskDispatcher.cs:6036
AgentTaskDispatcher.DispatchOneAsync ... AgentTaskDispatcher.cs:6055
AgentTaskDispatcher.DispatchOneAsync ... AgentTaskDispatcher.cs:6156
AgentTaskDispatcher.TickCoreAsync ... AgentTaskDispatcher.cs:941
```

Npgsql redacted the detail in the application log. The corresponding local
Postgres container log supplies it without changing logging settings:

```text
2026-10-22 08:34:04.905 UTC [155134] ERROR:
  duplicate key value violates unique constraint "IX_Agents_Slug"
DETAIL: Key ("Slug")=(task-f9acde3c) already exists.
STATEMENT: INSERT INTO "Agents" ... VALUES ($1, ... $40)
```

Only the statement's table and operation are reproduced; parameter values were
not logged or collected. The constraint is defined in `AppDbContext.cs:1267`
at the investigation base, and the slug limit is 120 characters at line 1215.

| UTC | Retained evidence |
|---|---|
| 08:31:00.910147 | Failed task's `CreatedAt`, Created event, attempt 1. Creation succeeded; this was not an immediate create/save rejection. |
| 08:32:48.763141 and 08:33:10.234315 | Held events name land lease owner `b10154f3123c4216a368fdc9b658abad`; no database-conflict evidence against that owner. |
| 08:33:38.531 | Server log line 43993: desktop worktree creation completed for f9acde3c. Its event's earlier timestamp was sampled at claim start. |
| 08:33:41.177 | Line 44026: remote preparation was handed off with the task still queued. |
| 08:33:47.587 | Line 44068: remote workspace binding saved; Held event says prepared workspace is ready. |
| 08:34:04.905 | Postgres rejects the second agent INSERT. |
| 08:34:05.023932 | Failed status/event committed; `AgentId`, `AgentSessionId`, and `DispatchedAt` remain null. |
| 08:34:10.659 | Line 44292: `Swept stale pool delegate 'task-f9acde3c' (session not live, no open task)`. |
| 08:34:13.434 | Line 44297: Merge task `23b40f9c-9848-4659-9704-8a8302068dab` launches, after the failed INSERT. |
| 08:34:29.914278 | Replacement 2620772f created: 24.89 seconds after the failed status, not a retry attempt on f9acde3c. |
| 08:38:15.147 / 08:38:24.334 | Lines 45603/45657: replacement prep handed off, then binding saved. |
| 08:38:51.835 | Line 45867: **the replacement's own `task-2620772f` unbound pool agent is swept**. |
| 08:38:54.883849 / 08:38:57.681 | Replacement claim committed, then launch log line 45916. One bound agent `4cb26f23-d180-4a4f-89c9-ad772ba651fe`, session `e65431c2-bc0c-41b0-9fe1-cebb677a6527`. |

The original agent row has already been deleted by the product's sweeper. Its
original GUID/CreatedAt and a historical statement trace of its first INSERT are
not available. The error's slug, sweeper log, and source control flow identify the
unbound row. A read-only task-ID-prefix query found exactly one `f9acde3c...` task;
there is no evidence of a collision between two different task IDs.

## Write path and why ordering matters

All numbers in the first column refer to
`server/Application/Services/AgentTaskDispatcher.cs` at live SHA `ceb6e3d6...`.
The last column gives corresponding locations at investigation base `a38e1e1...`.

| Live lines | Operation | Investigation-base lines |
|---|---|---|
| 5446, 5458-5470 | Begin transaction, `SELECT AgentTasks ... FOR UPDATE`, require Queued plus matching token, reload claimed row. | 5490, 5502-5514 |
| 5833 | `ResolveAgentAsync` runs before remote preparation to supply the launch-policy check. | 5877 |
| 7320-7366 | Reuse only when `task.AgentId` identifies a row; otherwise allocate a new GUID, set `Slug = task-<8-hex>`, and `_db.Agents.Add(agent)`. | 7347-7393 |
| 5879-5888 | Pending remote preparation: SaveChanges and commit while Queued, then start asynchronous preparer and return `HeldForRemotePrep`. **The Added agent is included in this save.** | 5923-5932 |
| 5960-5964 | Only after a prepared mirror exists, assign `claimed.AgentId`/name/session. The earlier commit never reached these assignments. | 6004-6008 |
| 6036-6055 | Final claim SaveChanges fails on the second agent's INSERT; dispose transaction, clear tracker, reload committed task facts, rethrow. | 6080-6099 |
| 1066-1074 | Generic catch logs exception, calls `FailAndNotifyAsync` with only `ex.Message`. | 1098-1107 |
| 7733-7766 | `FailAsync` persists Failed, `FailureReason`, completion time, new token and Failed event. | 7760-7799 |

The first claim commits an agent without a session and without a task foreign key
pointing to it. The task remains Queued and the next claim legitimately passes its
row lock/token test. Since `AgentId` is null, it constructs a second GUID with the
same slug. The task-row lock therefore does not prevent this sequential failure.

Ordinary ticks run `RetireIdleWarmAgentsAsync` first (live lines 370/431), whose
stale arm removes sessionless, unowned agents (8741-8752, 8799-8813, 8845-8864).
Ready-waiter ticks call `TickCoreAsync(ready, ...)` (345-355) and skip those sweeps.
The hosted loop calls ready dispatch before the ordinary tick
(`AgentTaskDispatcherHostedService.cs:53`), and the ordinary loop also offers
nested ready dispatch between candidates (live dispatcher line 635).
Thus a resumption before cleanup fails; cleanup first masks it. Several in-flight
tasks and lease releases can expose the ordering, but no concurrent duplicate
create request, land write, maxParallel update, or Merge write is needed.

`AgentTaskService` creation had already succeeded: live lines 1686 and 1745 add
and save the task. The failing stack is the later dispatcher claim. The service's
detail DTO returns `task.FailureReason` at lines 2839-2842. A fresh detail GET and
direct SQL both returned the generic failure reason shown above. There is no
`lastError` property in this detail response. An empty summary/property lookup
does not demonstrate a missing persisted reason.

## Retry assessment and minimal repair

`DispatchOneAsync` does not retry `DbUpdateException`; its Worktree cleanup catch
rethrows, and the outer tick terminalizes the task. `Program.cs:113-124` configures
Npgsql without `EnableRetryOnFailure`. The explicit resilience owner,
`docs/resilience.md`, keeps SaveChanges outside its admitted read retries.
Remote preparation has its own backoff, which does not cover this claim save.

Recommended narrow repair: **persist a fresh pool agent only in the final claim
transaction with its task/session binding**. Keep the prospective agent available
for the pre-preparation launch-policy check. For example, construct a fresh agent
without adding it to EF, and add that new instance only after remote preparation
is ready; existing pinned agents must retain their existing identity/tracking.
Alternatively detach only the newly Added agent before the preparation-only save,
with an explicit test of the final insertion path. Prefer the former separation
if it avoids a fragile state-dependent detach.

Preserve the existing policy refusal before push/mirror, row lock/token admission,
runner projection, warm reuse, transaction rollback cleanup, and post-commit
launch. Do not bind an otherwise nonexistent session early, delete agents by
short slug, weaken `IX_Agents_Slug`, make names random to conceal orphan rows, or
add a blanket retry around dispatch. A 23505 is not a transient serialization
failure. Retrying this save unchanged would hit the same committed row unless
cleanup happened incidentally. Even a future 40001/40P01 policy would need fresh
transaction/state and explicit handling of prior filesystem/launch effects.

The current row reason is generic, not absent. A separately scoped observability
improvement could persist a bounded diagnostic identity plus allowlisted SQLSTATE,
entity and constraint class; never copy provider detail/parameters/connection
strings into a card or failure reason. That improvement is not required to fix
this lifecycle defect and is excluded from the small Code task below.

Existing orphan rows created before deployment remain the established sweeper's
responsibility. This investigation made no cleanup writes and proposes no live
cleanup bypass. Short-prefix collisions between genuinely different tasks are a
separate identity-design question, not the mechanism evidenced here.

## Fix scope and verification design

One Code task, approximately 45-55 minutes, on a fresh worktree. Application change
is confined to fresh agent creation/attachment in the dispatcher. Extend the
existing partial `PhoneHomeTaskDispatchProjectionTests` with a small lifecycle
file, reusing its isolated PostgreSQL schema, fake phone-home peer, fake Git and
recording launch sink. No real provider or production runner is needed.

S1: Add and commit the three outcome tests below before the fix. Run CP-1 on that
commit as a red witness, preserving the actual assertion/23505 result. S2: defer
agent persistence, commit, then run CP-1 and CP-2 green. The red pass of CP-1 is
an explicitly authorized extra run; it must compile and execute the intended
cases. A harness/build error or zero tests is not the witness.

- **V-1 `C_dispatch_save_preparation_has_no_unbound_agent`:** hold the fake
  mirror response, invoke the real claim once, and read through a separate
  DbContext. Assert Queued and durable worktree/prep state, zero fresh delegate
  rows, no task agent/session binding and no launch/brief. Release and await the
  preparer in fixture cleanup. Current source should fail the agent-count
  assertion, because preparation committed one unbound agent.
- **V-2 `C_dispatch_save_ready_claim_needs_no_pool_sweep`:** first real claim,
  await mirror completion, then a fresh scoped dispatcher/DbContext calls
  `DispatchOneAsync` directly, with **no janitor/full TickAsync between claims**.
  Assert Dispatched, exactly one bound agent/session, exactly one projected launch
  and brief, the requested runner/mirror, and no Failed event. Current source
  should throw 23505 at the second claim. Make this a separate test so V-1's
  earlier assertion cannot prevent observing this red.
- **V-3 `C_dispatch_save_repeated_pending_claim_stays_unbound`:** reenter a
  still-pending preparation with a fresh scope, without sweeping. Assert no
  committed fresh agent/session/launch and unchanged task identity. Then finish
  preparation and claim once; a further stale claim must not add a second
  agent/session/launch. Use completion barriers, not sleeps; always await owned
  asynchronous preparation.
- **R-1 through R-5:** existing exact methods below cover Claude/Grok runner
  projection, pinned warm Codex reuse, card binding and policy refusal before
  remote side effects. Existing projection helper lines 130-134 run two full
  `TickAsync` calls, which insert the masking sweep; they alone cannot guard V-2.

### Checkpoints

These are the closed ordinary green rows. CP-1's S1 diagnostic red pass is the
only additional planned test run. Use the checkpoint tool and host build-slot
gate, bind each run to its committed SHA, and preserve unedited CHECKPOINT lines.
Run after S2 as one slice group; await completion and remove owned alternate
outputs. No full assembly, qualification run, or production process is in scope.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-dispatch-save-lifecycle/` | dispatch-agent-lifecycle | `/*/*/PhoneHomeTaskDispatchProjectionTests/C_dispatch_save_*` | V-1, V-2, V-3 | all 3 named methods, 0 failed/skipped | 3 | 8 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-dispatch-save-regression/` | dispatch-agent-regression | `/*/*/PhoneHomeTaskDispatchProjectionTests/(Runner_bound_claude_task_reaches_the_queue_projected)\|(Runner_bound_grok_task_reaches_the_queue_projected)\|(Signed_out_codex_runner_still_claims_and_reuses_a_warm_session)\|(Card_bound_runner_worktree_task_dispatches)\|(Refused_runner_task_leaves_no_pushed_branch_or_mirror)` | R-1, R-2, R-3, R-4, R-5 | all 5 named methods, 0 failed/skipped | 5 | 8 |

### Cost and handoff

Ordinary green floor: 16 minutes; authoring about 25 minutes; S1 red witness about
8 minutes; estimated Code total 49 minutes. These are estimates, not measured
execution. Ordinary Review remains separate. Post-land mutation candidate PC-1:
restore early fresh-agent tracking and run only V-2's exact method; require the
intended 23505 red, restore, then that same method green on the bound source.
No mutation was performed here; carry that obligation through Review and the
post-land companion workflow.

## Board duplicate search

Complete `scripts/card.ps1 search ... -Board Antiphon -All -Json` searches:
`IX_Agents_Slug` returned 2 cards; `slug` returned 26; `remote prep` returned 6.
Each envelope had `truncated=false`, `nextPageToken=null`. Full candidate bodies
were read for CARD-0870, CARD-0633, CARD-0632, CARD-0645 and CARD-1076.

CARD-0870 is Done and records a Windows Grok retry/test-harness collision, closed
after harness fixes `d75783b8`/`cb67130b`; it does not cover a fresh remote task's
preparation-only agent commit. CARD-0633/0632 own the remote transaction split and
backoff and are Done; CARD-0645 owns launch refusal ordering and is Done.
CARD-1076 is Review and concerns push timeout/lease contention. This new failure
needs its own narrow follow-up, linked to the split and policy-ordering owners.
That follow-up is CARD-1471; its create response confirms Backlog and no sessions.

## Reproducing the evidence read

From the desktop, use the log sources in `docs/logs.md`, bounded commands, and
the task token header for HTTP when present. Never enable sensitive logging.

```powershell
rg -n -A 39 'Failed to dispatch task f9acde3c' C:\src\Antiphon\server\logs\antiphon-20261022_001.log
docker logs --since 2026-10-22T08:32:00Z --until 2026-10-22T08:35:00Z antiphon-postgres
# GET /api/agent-tasks/f9acde3c-a082-4cfb-9882-40cde9783314
# Read failureReason at the response root; status and IDs are in summary.
```

Generated local evidence is gitignored at
`C:\Antiphon\worktrees\card-task-c80c961f\.antiphon\dispatch-save-debug`.
Essential facts and provenance are preserved above; raw evidence is not committed.

| File | SHA-256 |
|---|---|
| postgres-window.log | `06460687FCC22E42C665940F82C6295C0B2A7E9F26A702AE17AB2CB40FF2816A` |
| server-failure.log | `510E447CAA047D400E95DD2BE7E6425A939D0D3EF22A643D590851747835B96D` |
| db-task-evidence.txt | `9788E4E0906D65F6135FC69A0CAE33F416B0413D14FBD602A79877C2550BFF85` |

Investigation validation: correlated application/Postgres logs, targeted read-only
SQL, task-detail HTTP, process/socket ownership, live-SHA source inspection and
complete duplicate searches. Tests/builds: **0**, as required for this docs-only
investigation. The proposed red witnesses and fix are not yet executed.

--- next stage ---
next: code
handoff: Implement CARD-1471 on a fresh worktree. Persist fresh delegate agents only with the final claim; execute the documented S1 red witness and CP-1/CP-2 green rows, then ordinary Review. No production cleanup or restart is commissioned.
artifact: docs/investigations/2026-10-22-dispatch-save-failure.md
