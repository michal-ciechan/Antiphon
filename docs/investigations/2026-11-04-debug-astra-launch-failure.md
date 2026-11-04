# Astra Review launch never became ready, 2026-11-04

Debug task `56b0f9aa` for Review task `3fd6747e` (CARD-0824, Codex Frontier
`gpt-6-astra` on server2). Session `90baac1a-4956-4b36-9c0c-1335d8fd66cd` ended
`Failed` at `2026-11-04T23:14:57.2464Z`. No live setting was changed, nothing was
restarted, and nothing was killed. `codex --version` was the only Codex CLI
invocation; it printed `codex-cli 0.160.0` and left a zero-byte arg0 lock that
this task then removed. `auth.json` was not opened.

## Outcome

A plain redispatch of the same Review will fail the same way. Codex 0.160.0
started, painted the greeting chrome, and never painted the model footer the
ready gate requires. The desktop server then killed it. Auth, the CLI version,
the cached model list, and the task's workspace holds are not the cause.

What has to change before another dispatch: a Codex start in this server2
`CODEX_HOME` must paint the model-effort / cwd footer inside the ready budget.
The last time this home did that quickly was after the 2026-10-19 history wipe.
The store has grown back, and this start was still writing `logs_2.sqlite-wal`
when it was killed, with no rollout created.

## Root cause

`WaitForReadyAsync` returned false. `RunnerCodexAdapter` does not set
`LaunchBlock`, so `AgentSessionService.WaitForReadyOrThrowAsync` threw
`Agent process did not become ready.` The launch catch then killed the process
as `SystemRequest`. `AgentTaskLiveness` appended the exit description. The task
failure text is:

> Session died before the task settled: its session is Failed (Agent process did not become ready.; stopped by Antiphon (SIGKILL; matching stop request recorded); killed by signal 9 (SIGKILL) (SystemRequest)). No report is coming; read session 90baac1a-4956-4b36-9c0c-1335d8fd66cd before re-running this task.

That kill is the ready-gate teardown, not an operator hold and not a runner-local
watchdog. The runner only carried it out.

The retained screen never qualified as ready. `CodexStartupScreen.Classify`
requires a Codex banner, an idle composer, and a model-effort / cwd footer
(`IsModelEffortCwdFooter`, or a `model:` row). The session ANSI sidecar
(`/state/session-runner/90baac1a49564b369c0c1335d8fd66cd.ansi.log`, 1525 bytes,
last write `2026-11-04T23:12:57.925Z`) has two frames and then stops:

1. `OpenAI Codex (v0.160.0)` plus the word `loading`.
2. The same banner, `/work/worktrees/task-3fd6747e`, `permissions: YOLO mode`,
   `Greetings, fellow tinkerer.`, and the idle hint `Ask Codex to do anything`.

There is no `model:` row, no `gpt-6-astra`, no effort word, no middle-dot
footer, no sign-in text, no trust prompt, no sandbox chooser, and no update
picker. That frame classifies as `Unknown`. The server would log
`codex-startup not-ready reason=Unknown`. That line, and the startup capture
file, are written on the desktop (`Agents:CodexStartupCaptureDirectory`). They
were not readable from the runner. The sidecar is the frame the gate was
polling: it stopped growing 1.3 seconds after the child started
(`2026-11-04T23:12:56.6244702Z`) and stayed that size until the kill.

The process was alive and doing local work the whole time. It did not exit on
its own, and it never opened a conversation:

- pty-host `/tmp/antiphon-pty-hosts/logs/90baac1a49564b369c0c1335d8fd66cd.log`:
  launched `/usr/local/bin/codex` child pid 8448 at `23:12:56.841Z`; at
  `23:14:57.063Z`, `Child exited (code 0, signal 9, reason KilledByRequest)`.
- Transcript sidecar: `transcriptPath` null, `firstInputAtUtc` null,
  `resumeLaunch` false, cwd `/work/worktrees/task-3fd6747e`.
- `GET /api/sessions/90baac1a-4956-4b36-9c0c-1335d8fd66cd/transcript?since=0`
  returned `entries: []`, `lastSequence: 0`.
- No file under `/state/codex/sessions/2026/11/`. `session_index.jsonl` and
  `history.jsonl` both end on 2026-10-31. No rollout was adopted. On 2026-10-31
  the same runner logged `Tailing Codex rollout` about four seconds after each
  `exe=codex` launch (first sample: launch `01:16:23.473Z`, tail
  `01:16:27.189Z`). This session has no such line.
- `logs_2.sqlite-wal` (29,812,352 bytes) was last written at
  `2026-11-04T23:14:56.981984907Z`, 54 ms after the stop request. The main
  `logs_2.sqlite` is 1,119,551,488 bytes, mtime `2026-10-31T12:51:17Z`.
  `thread_history_1.sqlite` is 826,032,128 bytes, mtime
  `2026-10-31T12:37:30Z`.

## Who sent the stop

The desktop server did, as the launch teardown.

| Time (UTC) | Evidence |
|---|---|
| 23:12:54.878689 | Task event: dispatched to agent `task-3fd6747e` (`gpt-6-astra`). This is `acceptedStartedAt`. |
| 23:12:56.6244702 | Child start, recorded on the transcript sidecar and the stop intent. |
| 23:12:56.841 | pty-host launched `/usr/local/bin/codex` pid 8448. |
| 23:12:56.865 | Runner log: `Phone-home Launch "d1029e6e-b70f-44f3-a592-b62eeb265ec6" session=90baac1a-4956-4b36-9c0c-1335d8fd66cd exe=codex ... outcome=started`. |
| 23:12:57.925 | Last byte of the ANSI screen. |
| 23:14:56.928434 | Stop intent `5bc0c9ad-bac8-49e6-9ffc-8ff0e165ba92`: `targetPid` 8448, `attemptedSignal` 9, `target` Child, `path` ProcessTree, `outcome` Issued, `provenance` LiveIssuer, `durable` true. |
| 23:14:57.074 | Runner log: `Phone-home KillGeneration "943874aa-acdc-440a-92d6-0db5a7e464af" ... expectedGeneration=2026-11-04T23:12:54.878689Z outcome=killed`. |
| 23:14:57.2464 | Session `endedAt` / `lastSeenAt`, status Failed. |
| 23:18:19.686023 | Task event records the dead-session failure. The task's `completedAt` is this later settlement, not a second kill. |

120.30 seconds separate child start from the stop request. 120.06 seconds
separate the runner's launch-ack line from `KillGeneration`. `CodexReadyWait`
sets one deadline of `CodexReadyMaxWaitMs` when the wait starts, which is
immediately after the launch ack. The committed value at the live server SHA
`919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd` (`GET /api/version`) is **60000** in
both `server/appsettings.json` and `AgentRegistrySettings`. A 60-second budget
would have killed near `23:13:57Z`. The observed deadline matches a **120
second** budget. `Agents:CodexReadyMaxWaitMs` is bound only by the desktop
server and can be overridden by `appsettings.Development.json` or user-secrets
id `antiphon-server`. That overlay was not readable from the runner. The
120-second figure is the process's behavior, not a line from the desktop log.

`SessionReconciliationSettings.AgentGraceMs` is also 120000. It is not this
kill. The failure source is `SystemRequest`, and the runner recorded an inbound
`KillGeneration` for the launch generation. An operator stop would be
`OperatorRequest`.

## What this is not

**Not a hold that cleared around 23:10Z.** Task `3fd6747e` was created at
`23:12:14.733Z` and dispatched at `23:12:54.878Z`. The only holds are the
normal pre-dispatch ones:

- `23:12:18.886Z` `Held: same-card base inspection is pending.`
- `23:12:24.326Z` worktree created at `C:\Antiphon\worktrees\card-task-3fd6747e` on `feat/card-task-3fd6747e` from `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c`.
- `23:12:44.778Z` `Held: remote workspace preparation is in flight for runner 'server2'`.
- `23:12:54.470Z` `Held: prepared workspace is ready; awaiting dispatch admission.`

There is no manual-hold event. The runner's first Codex auth probe for this
launch is `23:12:14.776Z`, the same second the task was created.

**Not signed out.** Runner log, attribute-only probe (the file was not read):

- `23:12:14.776Z` `Codex auth probe: loggedIn=true error=null`
- `23:12:54.854Z` `Codex auth probe: loggedIn=true error=null`

`/state/codex/auth.json` is the same inode `134064` and the same 3880 bytes
recorded after the 2026-10-19 wipe. Its mtime moved to `23:12:57.801Z`, during
startup. The screen has none of the sign-in strings `ContainsSignIn` matches.

**Not a version gate.** The screen and `/usr/local/bin/codex` (symlink to
`/opt/codex/0.160.0/...`, installed link mtime `2026-11-03 09:43Z`) are
`codex-cli 0.160.0`. Frontier's floor is 0.153.4. Version observations do not
refuse launch. This was the only `exe=codex` line in the November runner logs
(`session-runner-20261101.log` through `session-runner-20261104.log`).

**Not a quota or credit refusal on screen.** The only frame has no rate-limit,
credit, or error text. The cached catalog
`/state/codex/models_cache.json` (`fetched_at` `2026-10-31T12:50:11Z`,
`client_version` `0.160.0`) lists `gpt-6-astra` with `visibility` `list`,
`supported_in_api` true, `upgrade` null, and reasoning levels
`low, medium, high, xhigh, max, ultra`. `config.toml` has
`check_for_update_on_startup = false` and
`[tui.model_availability_nux] gpt-6-astra = 4`. There is no `version.json`.
The cache is four days old, so it does not prove today's account quota. No API
error was shown because the TUI never left the pre-footer frame. Launch argv
is not in the runner log (`exe=codex` only). The dispatch event names
`gpt-6-astra`, and `CodexLaunchArgs.ReasoningEffort` maps Frontier to `xhigh`.
The footer that would have shown that pair was never drawn. The catalog's own
`default_reasoning_level` for `gpt-6-astra` is `medium`; the explicit launch
override is what the gate would have displayed, and it never got that far.

## Redispatch

Do not redispatch this Review until a Codex process in `/state/codex` paints
the model footer inside the ready budget.

The same binary, the same home, and the same gate produced no rollout in 120
seconds. On 2026-10-31 the same runner adopted a rollout in about four seconds.
After the 2026-10-19 wipe of this home, a cold `gpt-6-astra` `xhigh` start
painted the footer in 3.5 seconds. The store is large again: 1.12 GB `logs_2`,
a 29.8 MB WAL still being written at SIGKILL, and 826 MB `thread_history_1`,
with no rollout since `2026-10-31T12:52:13Z`. This task did not measure a start
after a cleanup, so the WAL write is evidence the process was still in startup
work, not a timed proof that shrinking the store is sufficient. It is the
change that removed this shape of stall before, and nothing else in the launch
path is refusing the model.

Lengthening `CodexReadyMaxWaitMs` further is not the fix to ship first. The
live wait already ran about 120 seconds against a committed default of 60
seconds, and the footer was still absent. Confirm the desktop overlay so the
next `elapsedMs=` line is interpretable. A longer gate only helps if a later
start shows the footer is late rather than stuck.

Cleanup of that home, if it is commissioned, stays the supported uid-1654
`codex delete` path. Do not delete the sqlite files. This debug task did not
do it.
