# CARD-0221 — plan: zombie agent processes (2026-08-28)

Plan only; nothing here is built. Three slices: **S1** a server-side fix for the mechanism that
manufactured the incident (it is not a one-off), **S2** the detector script
`scripts/reap-zombie-agents.ps1` as a companion to `reap-orphaned-pty-hosts.ps1`, **S3** the
schedule. Operator decisions are at the end.

## Three corrections to the card, established from the rows and logs

The card's incident is real and its revised detector shape (ask the runner for `pid`/`hostPid`,
never walk parent names) is right. Three things it *supposed* are wrong, and each changes the
design:

1. **The manifest was never pruned and the runner never lost the session.** The runner's own log
   (`%TEMP%\antiphon-logs\session-runner-20260820.log` … `-20260827.log`) shows
   `Adopted pty-host for session "71bd54b1…" (host pid 10756, running)` on every runner restart —
   08-20 09:00, 09:37, 13:44, 23:55 … through **08-27 07:01**, four hours before the operator's
   OS kill (`11:09:19 Liveness sweep marked session … Exited: its process vanished without an exit
   event`). `POST :17204/sessions/71bd54b1/kill` would have worked at any moment of that week.
   The "bare process kill because the manifest was long gone" fallback was not needed for this
   incident; it stays in the design only for a shape we have not yet seen (see class C below).

2. **The session row never said done.** `AgentSessions.71bd54b1` read `Running` from 08-20 07:45
   to the kill on 08-27; only the *task* row (`0ea601b2`, `Succeeded`, `CompletedAt` =
   `RecoveredAt` = 08-20 07:55:22) said the work was over. So `SessionReconciliationService`
   saw DB-Running / runner-Running — consistent — and had nothing to do. "The row itself is the
   authority" is true of the task row only in a narrow sense, worked through below.

3. **The task was settled by `RecoverFromBindRefusalAsync` (CARD-0085), not by the delegate.**
   The brief itself was parked at 07:48 after three delivery attempts (the server log on 08-27
   13:25 discards it: `ParkedSinceUtc=2026-08-20T07:48:24 … OwningTaskStatus="Succeeded"`), the
   transcript bind was refused for the first two hours (`refusing every transcript candidate …
   after 60s/361s/…`), and ten minutes after dispatch the recovery path wrote `Succeeded` with
   `RecoveredAt` because the worktree carried evidence. That path deliberately does not kill.

## Root cause — a permanent zombie by construction, not a missed signal

`AgentTaskReplyService.ReleaseDelegateAsync` (`server/Application/Services/AgentTaskReplyService.cs:1092`):

```
if (_settings.PoolEnabled && task.Workspace == WorkspaceMode.Shared && sessionAlive)
    { agent.Status = Idle; agent.PoolIdleSince = now; … return; }   // warm pool
if (!killSession) return;                                            // CARD-0085 arm
… KillAsync(sessionId) …; db.Agents.Remove(agent);
```

`RecoverFromBindRefusalAsync` calls this with `killSession: false` so that "a kill on a false
Failed" cannot kill a live worker (CARD-0056) and so the `DelegateBindRefusalRecovered` incident is
not cascade-deleted with the agent row. For a **Shared** task that is fine: the first arm pools the
delegate warm and `RetireIdleWarmAgentsAsync` (`AgentTaskDispatcher.cs:2727`) kills it after
`PoolIdleRetireMinutes` (60). For a **Worktree** task the first arm is skipped (`Workspace ==
Shared` fails), the second arm returns, and what is left is:

- a session row `Running` with a live process nobody will ever type into again (the brief is
  parked; no future task can claim the agent — reuse requires `Status == Idle`);
- an agent row `IsPoolDelegate = true, Status = Running, PoolIdleSince = null` — invisible to the
  janitor, whose query is `IsPoolDelegate && Status == Idle && PoolIdleSince != null`;
- no open task, so `DeadSession`/`NeverStarted`/`TaskProgressStalled` never fire (they key on
  open tasks);
- runner and DB agreeing, so reconciliation is satisfied.

Nothing in the system owns that process from that moment on. **Measured:** 9 Worktree tasks were
CARD-0085-recovered on 2026-08-20 (`select "Workspace", count(*) from "AgentTasks" where
"RecoveredAt" is not null group by 1` → Shared 6, Worktree 9, all Worktree ones that night). Eight
of the nine agent rows (`task-ec9031d4`, `task-c0097a9b`, `task-d2477fd1`, `task-c6bc61f7`,
`task-9e97b122`, `task-a8ea9c8f`, `task-861c4f19`, `task-6817f800`) are still in `Agents` today
with `IsPoolDelegate = true` and a Stopped/Failed session — their sessions ended between 08:34 and
10:49 that morning (operator action; the reason column is empty), and `0ea601b2` was simply the
one that was missed by hand. The 92.5 % CPU is a separate question (an idle Claude Code process
does not spin; it may be the TUI redrawing against a pty nobody reads) and is out of scope here —
the process should not have existed to spin.

## Does "a row that says done + a live process" imply kill? Partly — by evidence class

The card asked for this to be confirmed, not accepted. Worked through against the code, the
answer differs by *which* row and *who else already owns the case*:

| evidence | implies kill? | why |
|---|---|---|
| **Task** row terminal, process alive | **No, on its own** | Three legitimate shapes: a Shared warm pool delegate under its 60-min TTL (by design, `ReleaseDelegateAsync` first arm); a standing/AlwaysOn agent, whose session outlives every task it runs; the CARD-0085 arm itself, which chose to leave the worker alive. The card's own rule would have flagged `task-a503916a` tonight (`Succeeded`, pooled warm at 22:17). |
| **Session** row `Failed`, runner still claims it | **No** — already owned | `SessionReconciliationService` third pass **re-adopts** (`TryReAdoptAsync`, `:391`): CARD-0056 exists because `Failed` is exactly the status a launch-verification false positive writes on a healthy session — the operator's own conversation, once. Capped at 3 flaps then Critical. A script must not undercut that. |
| **Session** row `Stopped`, runner still claims it | **Yes** — already owned | Operator intent; the reconciler's only auto-kill arm (`RetryFailedKillAsync`, `:334`) retries it every sweep. Report only, pointing at the reconciler's incident. |
| **Pool delegate past its TTL**: `IsPoolDelegate`, no open task, newest `CompletedAt` older than `PoolIdleRetireMinutes` (+ margin), session alive | **Yes — unambiguous** | The pool's own contract says this process is dead; the janitor would have killed it if the row had been marked. This is the incident, and the only class the script should execute on by default. |
| Session row terminal (Stopped/Failed/`EndedAt` set), **runner does not claim** the process, cmdline/manifest names the session | **Yes, with an activity gate** | Nothing in Antiphon can reach it (not in the runner's list, so no delivery, no transcript, no reconciliation). **The one real edge case, stated explicitly:** a `claude.exe` launched with `/remote-control` is drivable from claude.ai *without* the runner — CARD-0056's leaked session was the operator's working conversation. So this class requires no write to the session's transcript (`~/.claude/projects/<enc-cwd>/*.jsonl` for its cwd) and no write to its ansi log for `-QuietHours` (default 6) before it is a positive. A Codex/Grok process cannot be remote-controlled, but the same gate costs nothing. |
| No DB row at all, runner-unclaimed | **No** | CARD-0056/CARD-0204 unchanged: unclaimed never implies kill. Report only. CARD-0204's own script already handles the two test-launch shapes that *do* have manifests. |
| Ancestor chain reaches `WindowsTerminal.exe`/`explorer.exe` and never `Antiphon.PtyHost.exe`/herdr | **Never a candidate** | Operator-launched. Tonight's census found exactly one: `claude.exe 27592` (`--name ClaudeBot`, under `cmd.exe > WindowsTerminal.exe`), the process a bare-exe-name filter would have called an orphan. |

So the card's reasoning holds for the incident — but the row that makes it unambiguous is the
**agent/pool** row plus the absence of any open task, not the task row alone, and for the
Failed-session shape the existing reconciler already holds the opposite (and correct) view.

## What exists today (reused, not rebuilt)

- `scripts/reap-orphaned-pty-hosts.ps1` (CARD-0204): dry-run default, `-Execute`, rules R1–R8,
  DB via `docker exec antiphon-postgres psql -At`, runner via `GET /sessions`, kill through
  `POST /sessions/{id}/kill` with a 20 s pid-gone verification, `-Limit`, exit 0/1/2. R1 "has a
  row ⇒ protected" is correct for its shape and is **not** changed; the new script is a sibling.
- Runner `GET /sessions` → `RunnerSessionDto { sessionId, pid, hostPid, status, startedAt,
  backend }`. **Measured 2026-08-28:** 22 runner sessions, 22 agent-shaped non-`WindowsApps`
  processes, 21 matched to a runner claim by walking each process's ancestor chain until a pid
  equals some session's `pid` or `hostPid` — Claude in one hop (`claude.exe` *is* the `pid`),
  Codex in three (`codex.exe > node.exe > cmd.exe`, `cmd.exe` is the `pid`), Grok in one. The
  ancestor walk against the runner's pid set is the whole identity mechanism; parent *names* are
  never consulted.
- Server `POST /api/sessions/{id}/kill` → `AgentSessionService.KillAsync` (`:755`): writes
  `Stopping`, kills through the runner with `KillGraceMs`, disposes, writes the final status. This
  is the coherent kill (DB + runner + agent status move together) and is what class A should use;
  the runner's own `/kill` is the fallback when the server is down.
- `SessionReconciliationService` third pass: owns DB-Failed/Stopped vs runner-Running (above).
- `RetireIdleWarmAgentsAsync`: kills via `IDelegateSessionStopper.KillAsync` and removes the row.
- Windmill on server2: `u/lndcobra/antiphon_build_junk_cleanup` (Mon 09:00 London) and
  `u/lndcobra/claude_session_cleanup` (Mon 09:15) run repo scripts over the container→Windows SSH
  bridge (`powershell -NoProfile -ExecutionPolicy Bypass -File …`, so **Windows PowerShell 5.1,
  ASCII-only, no `[bool]` params**); `u/lndcobra/telegram_notify` DMs the operator from any job.
  That last one is the "somewhere visible" the card asked for — no new server surface needed.

## Design

### S1 — the server stops manufacturing the state (`AgentTaskReplyService`, `AgentTaskDispatcher`)

1. **`ReleaseDelegateAsync`, `killSession: false` arm:** instead of `return`, mark the row for
   the janitor: `agent.Status = Idle; agent.PoolIdleSince = now; agent.PoolReservedForRootTaskId
   = null` — *without* making it claimable. Reuse (`TryReuseWarmAgentAsync`) must skip a delegate
   whose `WorkingDirectory` is a worktree path / whose task was `Worktree` (it already cannot
   match a Shared task's directory, but pin it with a test rather than rely on the path never
   colliding). Net effect: the existing 60-minute janitor kills it through the same `KillAsync`
   the pool uses; CARD-0085's "do not kill *now*" is preserved (the worker gets an hour, which is
   also what a Shared recovery already gets).
2. **Keep the incident when the janitor removes the row.** The reason the arm skipped the row
   delete was the `AgentIncidents` cascade. Two options; recommend (a): (a) `RetireIdleWarmAgentsAsync`
   sets `Status = Stopped` and leaves rows that carry an incident, deleting only incident-free
   rows (one `AnyAsync` per retire); (b) re-parent the incident to the session. (a) is ten lines
   and matches what the eight stale rows already look like today.
3. **A sweep for the rows this has already left behind** — `IsPoolDelegate`, session not
   Starting/Running, no open task: today's eight are harmless junk but they are what makes
   `Agents` lie about the pool's size. Fold into the janitor: remove when incident-free, else
   `Stopped`. No migration; retroactive by construction.
4. Log at Warning when the arm marks a Worktree delegate for retirement, naming the task and
   `PoolIdleRetireMinutes` — the line that was missing from the 08-20 log.

### S2 — `scripts/reap-zombie-agents.ps1` (companion, not an extension)

Same conventions as the CARD-0204 script: dry run by default, `-Execute`, `-Limit`, ASCII-only,
DB through `docker exec … psql -At`, runner through `GET /sessions`, both must **answer** (exit 2
otherwise — "could not look" is never "no row"). New: `-ServerUrl` (default
`http://localhost:17202`) for the coherent kill, `-Class` (which classes `-Execute` may act on,
default `PoolExpired`), `-QuietHours` (6), `-MinDoneMinutes` (default `2 × PoolIdleRetireMinutes`
= 120, read from the server's settings endpoint if exposed, else the parameter), `-ReportPath`
(`logs/zombie-agents/<utc>.json`, the `cleanup-claude-sessions.ps1` shape: allow-listed fields).

**Identity ladder** (first that answers wins; unresolved = printed under "unidentified", never
touched):

- I1 ancestor chain hits a runner `pid`/`hostPid` → that session (authoritative; the process is
  **runner-claimed**).
- I2 ancestor is an `Antiphon.PtyHost.exe` with a manifest under
  `<SessionLogPath>\pty-hosts\manifests\` → the manifest's `sessionId` (runner lost it).
- I3 `--session-id <guid>` on the command line → `AgentSessions.Id` if such a row exists.
  `--resume <guid>` is **not** trusted as a session id (Claude forks ids: `71bd54b1` resumed
  `99085358…` — the runner log says so).
- I4 `--name <slug>` → `Agents.Slug`/`Name` → `PersistentSessionId`.
- I5 cwd ending `\card-task-<8hex>` → the task → `AgentSessionId`.

**Pre-filters** (never candidates, printed under "ignored"): executable path under
`WindowsApps\` (the Claude Desktop and Codex Desktop app families the card already cleared);
ancestor chain reaching `WindowsTerminal.exe`, `explorer.exe`, `Code.exe`, `rider64.exe` or any
`ssh`/`sshd` before any Antiphon parent (operator-launched).

**Rules** (all must hold for the class; the failing rule is printed, R-style):

- Z1 process is agent-shaped (`claude.exe`/`grok.exe`/`codex.exe`) and passed the pre-filters.
- Z2 identity resolved (I1–I5) to a session id `S`, and the DB answered for `S`.
- Z3 pid-reuse guard: the process's start time is at or after `AgentSessions.StartedAt` of `S`
  minus 5 s (a recycled pid pointing at an old row is not that row's process).
- Z4 **class**, decided from the rows:
  - **A `PoolExpired`** — `Agents.IsPoolDelegate` for the owner of `S`, zero tasks on that agent
    in Queued/Dispatched/Working/Blocked, newest `CompletedAt` older than `-MinDoneMinutes`, and
    `S` is Starting/Running in the DB. Runner-claimed or not.
  - **B `ReconcilerOwned`** — `S` is Stopped/Failed in the DB and the process is runner-claimed
    (I1). Never acted on; the output names the reconciler's arm that owns it.
  - **C `EndedButAlive`** — `S` is Stopped/Failed with `EndedAt` older than `-MinDoneMinutes`,
    the process is **not** runner-claimed (I2–I5), **and** Z5 holds.
  - **D `Unclaimed`** — no row resolves. Report only (CARD-0056).
- Z5 (class C only) activity gate: newest mtime across the session's transcript directory
  (`~/.claude/projects/<enc-cwd>/*.jsonl` for Claude; Codex/Grok have no equivalent, so the
  ansi log alone) and its `*.ansi.log` is older than `-QuietHours`.
- Z6 age floor: the process is older than `-MinDoneMinutes` (belt and braces over Z4's clocks).
- Z7 `-Execute` names the class (`-Class PoolExpired`, `-Class PoolExpired,EndedButAlive`).

**Kill path** — one rule: *the most coherent path that still exists, verified, never a bare
`Stop-Process` while something above it can do the job.*

1. Class A, runner-claimed: `POST {ServerUrl}/api/sessions/{S}/kill` (DB, runner and agent row
   move together — the same call the janitor's `KillAsync` makes). If the server does not answer,
   `POST {RunnerUrl}/sessions/{S}/kill` (R8's path; the liveness sweep then closes the DB row).
2. Class A, runner-unclaimed (I2 manifest present): `POST {RunnerUrl}/sessions/{S}/kill` returns
   404 for a session the runner does not hold, so fall through to 3.
3. Class C or a class-A fall-through: tree-kill from the **topmost Antiphon-shaped ancestor**
   (`Antiphon.PtyHost.exe` if present, else the `cmd.exe`/`node.exe` wrapper the runner's `pid`
   would have named, else the leaf) via `taskkill /T /F /PID`, and the manifest, if any, is left
   for the runner to collect on its next adoption pass.
4. Verify: leaf pid gone within `-KillVerifySeconds` (20); exit 1 otherwise, matching the sibling.

**Exit codes:** 0 clean (dry run with no positives, or every kill verified); **3 positives found in
dry run** (new — it is what lets the schedule notify); 1 a kill did not take; 2 prerequisites did
not answer. `-Limit` oldest-first as the sibling.

**Output:** the census table (pid, exe, start, working set, 5-second CPU delta as in the card's
recipe, identity method, session, DB status, agent, class, rules failed), then per-class counts,
then the JSON report. The working-set column is deliberately in the table: at tonight's census no
`claude.exe` exceeded **0.52 GB** working set (`Win32_Process.WorkingSetSize`), so the "~11 GB"
reading from earlier today was either a process since gone or a different counter (commit /
private bytes); a `-MinWorkingSetGB` *report-only* flag ("large but legitimately active") is
cheap to add and is the right hook for a later memory-focused card — it must never feed a class.

### S3 — schedule

Register `u/lndcobra/antiphon_zombie_agents` (bash, tag `desktop`) running
`C:\src\Antiphon\scripts\reap-zombie-agents.ps1` over the SSH bridge, and pipe a one-line summary
to `u/lndcobra/telegram_notify` when the exit code is 3 (dry run found something) or 1 (a kill did
not take); silence on 0. Exit 2 (runner/DB not answering at 09:30) is also worth a line — it is
the same "stack is down" fact the watchdog would be chasing.

**Cadence: daily, not weekly — 09:30 Europe/London (`0 30 9 * * *`).** The card's argument for
weekly is "this one sat a week at no cost until load spiked", but the cost was not zero: the
process pinned ~92 % of a core for seven days and every one of these holds a Claude Code working
set and a pty-host for as long as it lives; the two existing weekly jobs clean *inert* junk (build
directories, archived sessions) where a week of accumulation costs nothing at runtime. The check
is one runner call, one DB query and one `Win32_Process` enumeration; the marginal cost of daily
over weekly is under a second a day. Weekly would bound the exposure at 7 days; daily at 1, on
top of S1 bounding the incident class itself at 60 minutes. The 09:30 slot keeps it off the two
Monday SSH sessions (09:00, 09:15).

**Rollout:** dry-run only for the first week (`-Execute` absent — the script's default), reading
the Telegram lines; then `-Execute -Class PoolExpired`. Class C stays operator-run (`-Execute
-Class EndedButAlive` by hand) until two weeks of daily reports have shown zero class-C rows that
an operator disagreed with — it is the class whose false positive is the operator's own
remote-controlled conversation.

## Tests

Server (`tests/Antiphon.Tests`, TUnit; run chunked with `--property:OutputPath=bin-0221/` and
delete the `bin-0221` directories):

- `AgentTaskRecoveryTests` (or the existing CARD-0085 class):
  `a_recovered_worktree_task_marks_its_delegate_for_retirement` — recover a Worktree task with a
  live session; assert the agent is `Idle` with `PoolIdleSince` set, the session is still
  Running, the incident row exists. **Red today** (`Status` stays Running, `PoolIdleSince` null).
- `AgentTaskPoolTests`: `the_janitor_kills_a_recovered_worktree_delegate_after_the_ttl` — advance
  the (offset-over-real, per CARD-0222) clock past `PoolIdleRetireMinutes`; `RetireIdleWarmAgentsAsync`
  calls the stopper once for that session, keeps the row as `Stopped` because it carries an
  incident. Red today (the query never selects it).
- `a_recovered_worktree_delegate_is_never_reused_for_a_shared_task` — the row is Idle now; the
  reuse path must not hand it to a Shared task in a different directory (pins the S1 caveat).
- `the_janitor_removes_stale_pool_rows_with_ended_sessions` — seed today's eight-row shape;
  incident-free rows removed, the rest `Stopped`.

Script (`scripts/test-reap-zombie-agents.ps1`, the `test-cleanup-claude-sessions.ps1` pattern —
inject `-ProcessesJson`, `-RunnerJson`, `-DbJson`, `-Now`; `-Execute` forced off under injection;
record kill calls through an `-HttpShim`):

- A fixture built from tonight's census (22 processes, 22 runner sessions) → 21 runner-claimed,
  1 ignored (WindowsTerminal ancestor), 0 positives. Pins the Codex three-hop match and the
  operator-launched exclusion.
- The incident fixture (`0ea601b2` rows verbatim: task Succeeded/Recovered 08-20 07:55, session
  Running, agent pool/Running, process start 08-20 07:45, runner claims pid) → class A positive
  with the server kill path chosen.
- `task-a503916a` tonight (Succeeded 22:17, pooled warm, 20 minutes old) → class A **not**
  positive (Z4 `-MinDoneMinutes`).
- Session Failed + runner-claimed → class B, no kill call even with `-Execute -Class EndedButAlive`.
- Session Stopped + unclaimed + transcript written 10 minutes ago → class C fails Z5; the same
  with mtime 7 hours ago → positive; `taskkill` path selected from the pty-host ancestor.
- Pid reuse: process start before `StartedAt` → Z3 fails.
- Runner or DB not answering → exit 2 and no verdicts.

## Follow-ups this plan does not take

- The **92.5 % CPU** of an idle, unread Claude process (a separate investigation; S1 removes the
  process, not the spin). Tonight two live Grok sessions read 71–73 % of a core in the 5-second
  sample while working — the CPU column exists so the next census shows whether that is normal.
- CARD-0144's cleanup of the claude.ai Remote Control sidebar is unrelated to class C but is the
  same operator-launched shape; nothing here touches it.

## AGENTS.md gotcha to add when this lands

- **A task row that says Succeeded does not end a process, and a process nobody owns is a zombie
  by construction** (CARD-0221): `RecoverFromBindRefusalAsync` (CARD-0085) released a Worktree
  delegate with neither a kill nor a pool mark, so the janitor never saw it, reconciliation saw
  DB-Running/runner-Running, and `claude.exe 17088` burned a core for seven days while the runner
  listed it Running the whole time. Any new release path must leave the session in exactly one of
  three states — killed, pooled warm (`Idle` + `PoolIdleSince`), or owned by a standing agent —
  never "alive with no owner". `pwsh -File scripts/reap-zombie-agents.ps1` is the census
  (dry-run; `-Execute -Class PoolExpired` acts on the pool's own contract); the Failed/Stopped
  runner-claimed shapes belong to `SessionReconciliationService`, not the script.

## Operator decisions

1. **Cadence — daily 09:30 London recommended over the card's weekly.** Reasoning above: the
   zombie costs a core and a working set every hour it lives, unlike the two weekly jobs' inert
   junk, and the check is sub-second. Weekly is defensible only if the Telegram line is judged
   noisier than a week of a pinned core.
2. **Kill-path fallback — confirmed with a refinement.** Runner kill where the runner claims the
   session, yes — but the *server's* `POST /api/sessions/{id}/kill` first, so the DB and agent row
   move with it (the runner-only kill leaves the row for the liveness sweep to close as "vanished
   without an exit event", which is how the incident's row reads now). Bare tree-kill only for a
   process no runner and no server can name, and only in class C behind the activity gate. The
   fallback exists for a shape we have **not** yet observed (the incident's manifest was intact),
   so it should stay operator-run until observed.
3. **Class C execution** — keep it manual until two weeks of clean daily reports, or accept the
   remote-control edge case and enable it with `-QuietHours 6` from day one. Recommend manual.
4. **S1 option (a) vs (b)** for keeping the CARD-0085 incident when the janitor retires the row.
   Recommend (a) (row kept as `Stopped` when it carries an incident).
