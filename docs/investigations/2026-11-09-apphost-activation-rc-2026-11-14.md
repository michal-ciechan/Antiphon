# 2026-11-09 Desktop AppHost activation of rc/2026-11-14 (task c5acc73b)

**Outcome:** the desktop AppHost now runs rc/2026-11-14 = `7bee43614495293afbae1b0f4add019d11dacb9c`.
`GET /api/version` returns that SHA with capabilities `land-v2` and `operator-shutdown-v1`.
`scripts/restart-apphost.ps1 -ExpectedServerSha 7bee43614495293afbae1b0f4add019d11dacb9c` exited **0**,
so the CARD-1806 fix is confirmed on a live cold start. The server consumes `channels.inbound`, and both
runners are available. The Windows rc run dce78ee0 was not touched and kept producing results through
the restart. Purpose: CARD-1799 (the over-cap Telegram file note that names the file, its size and the
limit) lives in the server's `channels.inbound` consumer. The am-service gateway had already been
redeployed from this commit at 04:34Z.

## Before (2026-11-09 13:14-13:19Z)

| Item | Value |
|---|---|
| `/api/version` | `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`, land-v2, operator-shutdown-v1 (rc/2026-11-13) |
| `/health` | 200 |
| Canonical checkout `C:\src\Antiphon` | detached at 653f1145d; untracked: only the two known `docs/investigations/2026-09-30-card-0859/0860-*.patch` files |
| AppHost | `Antiphon.AppHost.exe` 249968 (05:01:11Z) <- launcher pwsh 238568 (`logs/apphost.pid`) |
| Server | `Antiphon.Server.exe` 455340 (05:01:23Z); dcpctrl 410364 on 17202; dashboard 153268 |
| Production runner 17204 | `Antiphon.SessionRunner.exe` 153560, build `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd` (since 2026-11-03 07:54Z) |
| `logs/apphost.restart.lock` | present but stale: stamp `131956 2026-11-09T04:58:26.6444703Z` (8 h old, from task 7136d6ce; ignored by the 15-minute age rule; not deleted) |
| `logs/apphost.launch.lock` | absent |
| Lands | none in flight: `GET /api/agent-tasks?landPending=true` was empty; no Merge task in flight; `.git/antiphon/land-execution.lock` free; no `.git/index.lock`. `.git/antiphon/landing.lock` (the repository mutation lease) was held by the server process. That lease is also taken by worktree provision/removal sweeps and short-lived children: `worktree-removal/` and `children/` were being touched each minute, and the server's graceful shutdown drained. |
| Pipeline (Antiphon) | in flight: this task, Code x2 (e9a63e43, 55b84e31), Review x3, Debug 4cefd387 (all on runners; none landing) |
| rc run dce78ee0 | supervisor 70136 and driver 314140 alive; 4 TRX, newest group-96 at 13:17:32Z |

## Actions

1. `git fetch origin` in `C:\src\Antiphon`. `origin/rc/2026-11-14` = `7bee43614495293afbae1b0f4add019d11dacb9c`;
   653f1145d is its ancestor.
2. `git checkout --detach 7bee43614495293afbae1b0f4add019d11dacb9c` in `C:\src\Antiphon` (no merge commit).
   The tree stayed clean apart from the two known patch files. **The canonical checkout is now detached at
   the rc/2026-11-14 tip. Other tools that read `C:\src\Antiphon` (land, restart, check-daemon-build) see
   that commit.**
3. Job Object: this agent shell is in a job (`IsProcessInJob` = true, session 1). A probe started through
   `Win32_Process.Create` (with `C:\Program Files\PowerShell\7\pwsh.exe`) reported `session=1 inJob=False`.
   Note: the WindowsApps `pwsh.exe` alias returns WMI `ReturnValue 8`, so the Program Files path is required.
4. Restart through `Win32_Process.Create`, cwd `C:\src\Antiphon`, with output captured to a scratch file:
   `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha 7bee43614495293afbae1b0f4add019d11dacb9c`
   started 13:21:12.5Z. Output:

   ```
   Restarting Antiphon AppHost...
     preserving session-runner (PID 153560 on 17204)
   WARNING: session-runner is stale for 64 source change(s).
     build 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd; built 11/03/2026 07:54:55; running since 11/03/2026 07:54:56
     e5f423f83 fix(pwsh-cache): CARD-1793 per-session pty cache owned by the host, stale cache sweep, checkpoint tool install
     2a8e5bc51 fix(card-1677): register the c1778-trust pty test root in the runner temp sweep family table
     87000d366 fix(card-1764): apply a transcript since-cursor only when it is gap-free
     cca29660c test(card-1764): witness a gap-safe transcript since-cursor
     e79c0e03b fix: give each test host and the runner a private pwsh cache (CARD-1790)
     Fix when it is safe to restart: pwsh -File scripts/restart-session-runner.ps1
     fake-gateway: build 653f114 has no newer source changes in its project closure
     graceful stop accepted; server PID 455340 exited after 17s
     killed AppHost wrapper tree (PID 238568)
     launching dev-aspire.ps1...
   LIVE_TIMING {"version":1,...,"phase":"network-loopback","operation":"health-wait","elapsedMs":182511.5351,"outcome":"completed"}
   AppHost restarted - dashboard http://localhost:17205, backend healthy, server 7bee43614495293afbae1b0f4add019d11dacb9c.
   END 2026-11-09T13:24:41.6089162Z exit=0
   ```

   The launcher again printed `Dashboard URL not found after 90s.`
   (`logs/apphost-launcher-1eeaab8f710d4b249813797b1a6f7a70.out`). With the CARD-1806 fix the restart found
   the dashboard on 17205 instead and exited 0.

## After (server listening 13:24:39Z; verified 13:25-13:26Z)

| Check | Result |
|---|---|
| `GET /api/version` | `{"version":"7bee43614495293afbae1b0f4add019d11dacb9c","informationalVersion":"1.0.0+7bee43614495293afbae1b0f4add019d11dacb9c","capabilities":["land-v2","operator-shutdown-v1"]}` |
| `/health`, dashboard 17205, client 17203 | 200, 200, 200 |
| Locks | `logs/apphost.restart.lock` and `logs/apphost.launch.lock` both absent (exit 0 removed the restart lock) |
| AppHost | `Antiphon.AppHost.exe` 104072 (13:24:03Z); `logs/apphost.pid` = 138456 (launcher) |
| Server | `Antiphon.Server.exe` 393336 (13:24:16Z); dcpctrl 305972; dashboard 252860 |
| channels.inbound consumer | `13:24:37.651 [INF] [antiphon] consuming channels.inbound as antiphon-server-bridge` (KafkaAntiphonMessagingConsumer); `13:24:37.552 [ingress-ops] consuming channels.ops.inbound-unconsumed` |
| Server startup | `13:24:39.349 Now listening on: http://[::]:63706`, `13:24:39.374 Application started`. Shutdown: `13:21:23.142 Operator shutdown: reason=restart-apphost drained=true in 5034ms; starting launches 0` |
| Errors since startup | 2 `[ERR]` lines, both request cancellations during warm-up, not startup failures: 13:24:41.525 `OperationCanceledException` in an Npgsql read for `GET /api/agent-tasks/52e1f0c1-...`, and 13:24:45.423 `TaskCanceledException` in `GET /health`. 0 ERR/FTL from 13:25 on. The `Running brief recovery could not attach session` warnings (df3b5125, 067f9d0e) already appeared before the restart (12:34Z). |
| `GET /api/session-runners` | desktop available=True eligible=True (2 capacity, 1 occupied); server2 available=True eligible=True (10/6); server2-temp unavailable (retired, expected) |
| Production runner | unchanged: PID 153560, `/capabilities` build `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd` |
| Canonical checkout | HEAD `7bee43614495293afbae1b0f4add019d11dacb9c`; only the two known untracked patch files; `.git/antiphon/children` empty |
| rc run dce78ee0 | untouched: supervisor 70136 and driver 314140 alive; TRX 4 -> 6 (group-117 at 13:22:25Z, e2e-all-split-1 at 13:25:47Z). All 6 have 0 failures; e2e-all-split-2 is running. The new `Antiphon.SessionRunner.exe` 387756 belongs to that run's E2E host (`C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.E2E\...`), not to this restart |
| server2 | not touched |
| Telegram | no message sent |

Timings: restart 13:21:12.5Z -> exit 0 at 13:24:41.6Z (3m29s). The API was down from the operator shutdown
at 13:21:23Z until the server listened at 13:24:39Z (about 3m16s). Health wait 182.5 s.

## Open items for the caller

- **Desktop session runner is stale for 64 source changes** (build 919d8d42f, running since
  2026-11-03 07:54Z). This task did not restart it, as the brief required. The documented soft restart is
  `pwsh -NoProfile -File scripts/restart-session-runner.ps1` (never `-Hard`/`-KillSessions` without the
  human). The operator decides when, given that rc run dce78ee0 is live.
- CARD-1806 is confirmed fixed on a live cold start: the launcher still misses its 90 s URL wait, and the
  restart still exits 0.

## Rollback

Target: `653f1145de5b72c2617a018cd606e0e8ba3cfe6e` (rc/2026-11-13, the previous running build).
Run from `C:\src\Antiphon` in a shell outside an agent Job Object (or via `Win32_Process.Create` with
`C:\Program Files\PowerShell\7\pwsh.exe`), with no land in flight and both AppHost locks clear:

```powershell
git -C C:\src\Antiphon checkout --detach 653f1145de5b72c2617a018cd606e0e8ba3cfe6e
pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha 653f1145de5b72c2617a018cd606e0e8ba3cfe6e
```
