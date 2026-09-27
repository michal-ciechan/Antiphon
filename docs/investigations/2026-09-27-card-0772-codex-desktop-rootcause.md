# CARD-0772 — Desktop Codex is killed by the 60s positive ready gate before a session exists

**Status:** kill mechanism confirmed from pty-host logs, the desktop server log, Codex's own `logs_2.sqlite`, and the session/task rows. The rendered frame that the gate scored `Unknown` was not stored. No new Codex process was launched for this investigation.

**Date:** 2026-09-27. Investigate task: `d77c21f4`. Card: CARD-0772.

**Dead desktop sessions:** `5786d2d9-76a8-4060-9ade-e2ae5d7e882c` (task `19efe23c`, CARD-0711 Test), `32f81ff0-4179-4c45-978c-599e56012000` (task `3edb6ff8`, CARD-0711 Test), `4f7cf634-df7d-4de1-b34a-ae64dfb614d5` (task `a5b617de`, CARD-0759 Code). `a5b617de` is the task id, not a fourth session.

## Verdict

Antiphon killed these three desktop Codex processes. They did not crash. Each child stayed alive until `CodexReadyMaxWaitMs` (60s) expired, the positive ready gate (CARD-0574) returned false with `reason=Unknown` and `mcpSeen=False`, and `KillAndDisposeAsync` sent `KilledByRequest`. The task row is failed about three minutes later by the dead-session reconciler (`DeadSessionFailGraceMinutes` = 3). That grace is the gap between the kill and the task failure.

The Windows `codex.cmd` → `node.exe` + `codex.js` rewrite (CARD-0497) ran, and it is not the crash. The same `node.exe` launch ran a desktop Codex session for 77 minutes the day before. Claude and Grok on this desktop, and Codex on server2, completed work in the same hour.

What the gate never saw was a Codex session. In the three deaths, Codex's own log never reached `thread/start` or `session_loop`. The native `codex.exe` (spawned by `codex.js` with `stdio: "inherit"`) spent the minute on startup HTTP. The first death finished a 15.8 MB `GET https://registry.npmjs.org/@openai%2fcodex` and wrote `~/.codex/version.json` (`latest_version` 0.157.1, installed package 0.156.1) about 30s before the kill, and still never opened a thread. The other two finished only `announcement_tip.toml` and then logged nothing until the kill.

## Baselines that did work

Same desktop, same day, local runner (`RunnerId` null). Pty-host logs under `C:\logs\antiphon\session-runner\pty-hosts\logs`.

| Work | Kind | Evidence |
|---|---|---|
| Task `09f3dc47` CARD-0718 Review, session `b0150694-4d80-46b1-9df4-e524c4659521` | Claude, desktop | Launched `c:\users\lndco\.local\bin\claude.exe` at 2026-09-26T15:31:26Z. Child exited 15:56:49Z. Task Succeeded 15:56:39Z. This overlaps both CARD-0711 Codex deaths. ConPTY bin `20260926-140853-8f11f840`, the same bin as those deaths. |
| Task `80103e29` CARD-0726 Review, session `4fffcdc4-be65-486b-97e6-62ef3e41167f` | Claude, desktop | Dispatched 2026-09-26T14:36:51Z, Succeeded 15:34:57Z. Server log 16:36:13+01: card moved to Review because this task settled Succeeded. |
| Task `d75b52ff` CARD-0759 Code, session `10657ea4-8fb4-44c6-a151-1af176d1706b` | Grok, desktop | Launched `C:\Users\lndco\.grok\bin\grok.exe` at 2026-09-26T21:57:04Z, 3.5 minutes after Codex session `4f7cf634` was killed. Child exited 23:01:39Z. Task Succeeded 23:01:30Z. Same card as the dead Codex Code task, same ConPTY bin. |
| Task `6cfbb6f5` CARD-0464 Review, session `3fecea05-2b75-41bc-8f77-358bb00e34b5` | Grok, desktop | Launched `grok.exe` 2026-09-27T01:52:35Z, child exited 02:10:48Z, task Succeeded. The CARD-0464 and CARD-0758 reviews in this window were Grok, not Claude. CARD-0758's review `f22995f6` succeeded on runner `server2`. |
| Task `ad64242f` CARD-0726 Code, session `9cba2e35-6dc3-40f0-a310-47f884a7bf03` | Codex, server2 | `RunnerId=server2`. Dispatched 2026-09-26T15:37:16Z, thirty seconds after the first desktop kill, Succeeded 16:06:29Z (1,754s). |
| Session `061581e5-b0f6-4691-898f-5f2780ece69e` | Codex, desktop, previous day | Pty-host launched `C:\Program Files\nodejs\node.exe` at 2026-09-25T15:27:43Z. Codex `logs_2.sqlite` shows `session_loop` / `TurnInput` at 15:27:56Z. Child lived until 16:45:09Z (77 minutes), then `KilledByRequest` with session status Stopped, not the 60s Failed shape. |

Desktop Codex is not uniformly dead. Since 2026-09-20 the local runner has 183 Codex sessions, 19 Failed and 154 not Failed. The last long desktop success before this cluster is `061581e5`. The next three local Codex launches are the three deaths below. Remote Codex rows since 2026-09-26 include 68 live-or-stopped sessions and zero `KilledByRequest` failure reasons.

## The three deaths

Transcript sidecars (`C:\logs\antiphon\session-runner\transcripts\<id-without-dashes>.json`) all say `format: codex`, `firstInputAtUtc: null`, `transcriptPath: null`. `TranscriptEntries` for these three session ids: 0 rows. Nothing was typed. The rollout file is created on first submit, so its absence is expected.

| Session | Child launch (pty-host) | Kill (pty-host) | Child life | Ready warning | Task `CompletedAt` | Kill → task failure |
|---|---|---|---|---|---|---|
| `5786d2d9` | 15:35:44.926Z `node.exe` pid 18736 | 15:36:46.208Z code 1 `KilledByRequest` | 61.3s | 15:36:45.016Z `elapsedMs=60007` | 15:40:01.780Z | 195s |
| `32f81ff0` | 15:40:28.115Z `node.exe` pid 65836 | 15:41:28.362Z code 1 `KilledByRequest` | 60.2s | 15:41:28.161Z `elapsedMs=60002` | 15:44:36.862Z | 188s |
| `4f7cf634` | 21:52:24.517Z `node.exe` pid 25684 | 21:53:24.602Z code 1 `KilledByRequest` | 60.1s | 21:53:24.558Z `elapsedMs=60015` | 21:56:25.897Z | 181s |

Session-row `StartedAt`→`EndedAt` is 72.5s, 72.4s and 69.3s. The extra seconds before the child are dispatch, not process life. Each row is status Failed (5), exit code 1, `TerminationSource` ProcessExit (3), `LaunchBlock` null, `RunnerId` null, `FailureReason` exactly `Process exited (KilledByRequest, code 1).`

Server log `C:\src\Antiphon\server\logs\antiphon-20260926.log`, one shape three times:

```
[WRN] Session "<id>" codex-startup not-ready reason=Unknown elapsedMs=6000x mcpSeen=False
      SourceContext=RunnerCodexAdapter
[WRN] Failed to start interactive agent session "<id>"
      System.InvalidOperationException: Agent process did not become ready.
      at AgentSessionService.WaitForReadyOrThrowAsync(... AgentSessionService.cs:line 2207)
      at AgentSessionService.LaunchInteractiveProcessAsync(...:line 510)
      at AgentSessionService.LaunchInteractiveAsync(...:line 371)
```

`codex-startup ready` does not appear in that file (the ready line is logged at Debug). `codex-startup not-ready` appears once on 2026-09-25 and three times on 2026-09-26, matching these four Failed rows, and zero times on 2026-09-27.

About one second after each warning the launch catch takes `SELECT * FROM "AgentSessions" WHERE "Id" = @p0 FOR UPDATE` and Postgres reports `40P01: deadlock detected` (log lines 21680, 21851, 35044). The session row that stuck is the exit observer's text, not `Agent process did not become ready.` The deadlock is the bookkeeping race after the kill. It is not the kill, and it is not the three-minute task gap: `EndedAt` is the kill instant.

The task failure line, ~3 minutes later:

```
[WRN] Task <short> failed by the dead-session reconciler: Session died before the task settled:
      its session is Failed (Process exited (KilledByRequest, code 1).). No report is coming
```

`DeadSessionFailGraceMinutes` defaults to 3 (`server/Application/Settings/DelegationSettings.cs:569`). `FailDeadSessionTasksAsync` starts that clock at the first sweep that sees the task dead (`server/Application/Services/AgentTaskDispatcher.cs:2373` and `:2474`). The dispatcher tick is `PollIntervalSeconds` (default 5). 181–195s from kill to `CompletedAt` is that 180s grace plus one tick. The plan's "~2 minutes" is a low estimate of this same grace. `DeliveryFailTimeoutMinutes` (10) did not fire. `NeverStartedGrace` (2 minutes, `AttentionService`) only surfaces attention; it does not fail the task.

## Why the gate fired

Current checkout, which is the server SHA reported by `GET /api/version` on 2026-09-27 (`2f050e3d`), not a claim about the 26 Sep binary's line numbers:

- `server/appsettings.json` sets `CodexReadyMaxWaitMs` to 60000.
- `RunnerCodexAdapter.WaitForReadyAsync` (`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:137`) calls `CodexReadyWait.WaitAsync` with that budget. There is no timeout-and-proceed branch. On deadline, `CodexReadyWait` logs `codex-startup not-ready` and returns false (`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs:405` and `:551`).
- `WaitForReadyOrThrowAsync` throws `Agent process did not become ready.` when the adapter returns false and names no launch block (`server/Application/Services/AgentSessionService.cs:2203`).
- The interactive launch catch kills with the accepted generation (`AgentSessionService.cs:648`) via `KillAndDisposeAsync` (`:769`). The pty-host sets `PtyExitReason.KilledByRequest` before `Kill()` (`src/Antiphon.Agents.Pty/PtyAgentRunner.cs:491`).
- The exit observer writes `Process exited ({exitReason}, code {exitCode}).` while status is still Starting or Running (`server/Application/Services/AgentSessionRuntime.cs:228`). `SessionTermination.FromExitReason` maps `KilledByRequest` to ProcessExit; only CPU-spin and memory kills become SystemRequest (`server/Application/Services/SessionTermination.cs:25`). First writer wins, so the later launch catch cannot replace ProcessExit. That is why the row says the process was killed and not that readiness failed. The exception text survives only in the server log.

`Unknown` + `mcpSeen=false` is the classifier's last sample (`CodexReadyTracker.LastReason`, `CodexStartupReadiness.cs:300`). An empty screen, or any screen without the Codex banner, a `model:` line, the MCP marker, trust, sign-in, sandbox, or the literal `Press enter to continue`, ends as `Unknown` (`CodexStartupScreen.Classify`, `:53`). `ContainsBlockingUpdate` is only that one phrase (`:274`). No per-sample screen was logged, and these sessions have no transcript rows, so the pixels are not recoverable.

## What Codex itself logged

`%USERPROFILE%\.codex\logs_2.sqlite`, table `logs`, windows bounded by the pty-host launch and kill. The native process id in the log is not the pty child: `codex.js` spawns `codex.exe` with `stdio: "inherit"` (`%APPDATA%\npm\node_modules\@openai\codex\bin\codex.js:241`). Installed package `@openai/codex` 0.156.1, `package.json` and `codex.exe` last written 2026-09-23 08:08 local, before both the 77-minute success and these deaths.

| Session | Codex log rows in the child lifetime | What they are |
|---|---|---|
| `5786d2d9` pid `58660` | 5, all `codex_http_client` | 15:36:15Z `announcement_tip.toml` 200; 15:36:16Z `api.github.com/repos/openai/codex/releases/latest` 200, `last-modified: Sat, 26 Sep 2026 01:04:09 GMT`; 15:36:16.891Z `registry.npmjs.org/@openai%2fcodex` 200, `content-length: 15836767`. No `thread/start`, no `session_loop`. |
| `32f81ff0` pid `23332` | 2 | 15:40:29Z announcement tip 200, then silence until the kill at 15:41:28Z. |
| `4f7cf634` pid `75036` | 2 | 21:52:25Z announcement tip 200, then silence until the kill at 21:53:24Z. |

`%USERPROFILE%\.codex\version.json` was written at `last_checked_at` 2026-09-26T15:36:17.088Z, the same second as the first death's npm response: `latest_version` 0.157.1, `dismissed_version` 0.154.0. The installed CLI is 0.156.1, so 0.157.1 was not dismissed. The file does not say a modal was shown.

Contrast, same database: session `061581e5` (the 77-minute desktop success) has `session_loop` and `TurnInput` at 15:27:56Z, 13s after `node.exe` launched, and no announcement-tip line in 15:27:40Z–15:28:05Z. Session `8abba29d` (2026-09-25T02:59:08Z, the other `Unknown`/`elapsedMs=60019` death) did reach `thread/start` at 02:59:58Z and was still killed at 03:00:13Z. So a 60s budget can also expire after the app server is up. The 26 Sep cluster is earlier: the app server never logged.

## Ruled out

- The npm shim crashing or failing to spawn. Pty-host assigned a live `node.exe` pid and waited ~60s. `CodexWindowsLaunchPolicy.NormalizeStockShim` prepends `codex.js` and keeps the original args (`src/Antiphon.SessionRunner/CodexWindowsLaunchPolicy.cs:154`). A rewrite failure throws before a child exists (`codex_launcher_unavailable` / `codex_command_line_too_long`), which these launches did not.
- A CARD-0574 bypass still letting work through, or the gate being skipped. The warning is the gate's deadline log, and the exception is `Agent process did not become ready.` No brief was queued into a composer (`firstInputAtUtc` null, zero transcript rows).
- ConPTY being broken on this desktop. Claude `b0150694` and Grok `10657ea4` used bin `20260926-140853-8f11f840` and ran 25 and 64 minutes. The 77-minute Codex success used an older bin (`20260924-231735-2d49d377`) and the same `node.exe` path.
- Model id. The two Test deaths were `gpt-5.6-terra`. The Code death was `gpt-6-sol`. Same gate result.
- server2 being unable to run Codex. Task `ad64242f` on runner `server2` succeeded through the same server-side gate, because that gate classifies the runner's screen wherever the process is.

## Not determined

- The rendered screen at the deadline. Final sample `Unknown`, MCP never seen. That is compatible with a blank frame and with an unrecognized update/announcement frame. The bytes were not kept.
- The ~30s after session `5786d2d9`'s npm response with no further Codex log. Parse of the 15.8 MB body, another request that logs only on completion, or a silent modal are all still open.
- Whether sessions `32f81ff0` and `4f7cf634` were blocked on that same npm GET. They never logged a second completed request. The in-flight call was not recorded.
- server2's Codex package version and whether that host performs this startup fetch. Not read from the runner. The evidence that Codex works there is task `ad64242f` reaching Succeeded.

## Not done, noted

A fix would have to keep a desktop Codex process alive through pre-session startup HTTP, or classify and retain the actual frame, and stop the kill's exit text from replacing `Agent process did not become ready.`; refusing Codex on the desktop remains the mitigation until a launch is shown reaching `thread/start` and a positive ready screen inside the budget.
