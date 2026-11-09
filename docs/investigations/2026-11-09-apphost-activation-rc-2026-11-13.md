# 2026-11-09 Desktop AppHost activation of rc/2026-11-13 (task 7136d6ce)

**Outcome:** the desktop AppHost is running rc/2026-11-13 = `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`
(`GET /api/version`: that SHA, capabilities `land-v2`, `operator-shutdown-v1`). The server
consumes `channels.inbound` and no ERR/FTL lines have appeared since startup. Both runners are available,
and the Windows rc run 231f573c is untouched and still producing results.
`restart-apphost.ps1` itself returned **exit 1** (a health-wait timeout) because of a launcher defect
(CARD-1806). Every exit-0 criterion was then verified directly. Purpose: CARD-1799 (the over-cap Telegram
file note) lives in the server's channels.inbound consumer. The am-service gateway was already
redeployed from this commit at 04:34Z.

## Before (2026-11-09 ~04:56Z)

| Item | Value |
|---|---|
| `/api/version` | `d164e93ae47a79ed175beeb9367212f7a7d5d1d5`, land-v2 (= tip of `origin/rc/2026-11-08`) |
| Canonical checkout `C:\src\Antiphon` | detached HEAD at d164e93ae; untracked only the two known `docs/investigations/2026-09-30-card-0859/0860-*.patch` files |
| AppHost | `Antiphon.AppHost.exe` 234660 (started 2026-11-06 22:01Z) <- dotnet 433876 <- launcher pwsh 143760 (`logs/apphost.pid`) |
| Server | `Antiphon.Server.exe` 330800 (started 2026-11-06 22:01:16Z) under dcpctrl 212684 / dcp 402652 |
| Production runner 17204 | `Antiphon.SessionRunner.exe` 153560 (build 919d8d42f, Scheduled-Task supervisor) |
| Locks | `logs/apphost.restart.lock` and `logs/apphost.launch.lock` absent; no `.git/index.lock`; child journal `.git/antiphon/children` empty |
| Lands | none in flight (pipeline: only this task and Review 2b554164 in flight; no `siblingLandInFlight`/`repositoryLease` queue reasons) |
| Runners | desktop available/eligible (1/2), server2 available/eligible (1/10) |
| rc run 231f573c | supervisor 380956 and driver 9408 alive; 2 TRX (newest group-119, 04:55:29Z) |

## Actions

1. `git fetch origin`. `origin/rc/2026-11-13` = `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`; d164e93ae is its ancestor.
2. `git checkout --detach 653f1145de5b72c2617a018cd606e0e8ba3cfe6e` in `C:\src\Antiphon`, with no merge
   commit. The tree stayed clean apart from the two known patch files. **The canonical checkout is now
   detached at the rc tip; other tools that read `C:\src\Antiphon` see rc/2026-11-13.**
3. Job Object check: this agent shell is in a Job Object (limit flags 0x800: breakaway OK, kill-on-close
   off; a nested outer job could not be excluded). To keep the new AppHost out of any agent job, the
   restart was launched through `Win32_Process.Create`. A probe had confirmed that such a process runs in
   session 1 and is not in a job. Command (from the main checkout):
   `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha 653f1145de5b72c2617a018cd606e0e8ba3cfe6e`,
   output captured to a scratch file. Started 04:58:24.9Z.
4. Restart output (key lines):
   - `preserving session-runner (PID 153560 on 17204)`
   - `WARNING: session-runner is stale for 63 source change(s).` (build 919d8d42f)
   - `WARNING: fake-gateway is stale for 3 source change(s).` (the CARD-1799 commits; now rebuilt, see below)
   - `graceful stop accepted; server PID 330800 exited after 3s`
   - `killed AppHost wrapper tree (PID 143760)` / `launching dev-aspire.ps1...`
   - `AppHost did not come up within 346s` /
     `health wait ended after 346s: no launch progress for 150s (hard cap 600s); last progress: apphost.log grew, 152s ago`
   - `END 2026-11-09T05:04:22.3Z exit=1`. The restart lock was retained (stamp `131956 2026-11-09T04:58:26.6444703Z`).

## Why exit 1 although the stack came up (CARD-1806)

The cold build took 127.7 s (`LIVE_TIMING ... operation=build elapsedMs=127676`). `dev-aspire.ps1` waits
a fixed 90 s for the dashboard URL, and that wait includes the build. It printed `Dashboard URL not found
after 90s.` (`logs/apphost-launcher-a912c708f6e5414cbb3ffc1a6ec70160.out`) and never wrote
`logs/apphost-dashboard-url.txt`. `restart-apphost.ps1` probes `/health` and `/api/version` only once it
has read that file, so it exited 1 on a stack that had been healthy since about 05:01:49Z. I did not retry,
for two reasons. A retry would have torn down a healthy, correct-SHA stack, and the retained fresh restart
lock would have refused it with exit 3 anyway. The runbook's exit-1 action is "read logs; do not
immediately rerun". The exit-0 criteria were verified directly instead (below). The retained restart lock
was left in place (not deleted). It ages out at 05:13:26Z, and until then the watchdog logs
`skip: launch in flight`, which is harmless because health is 200.

## After (2026-11-09 05:01:49Z server start; verified 05:06-05:08Z)

| Check | Result |
|---|---|
| `GET /api/version` | `{"version":"653f1145de5b72c2617a018cd606e0e8ba3cfe6e","capabilities":["land-v2","operator-shutdown-v1"]}` (rechecked 05:07:49Z) |
| `/health` | 200 |
| Dashboard `http://localhost:17205` | 200 (`Aspire.Dashboard.exe` 153268) |
| Client 17203 | 200 (built mode) |
| AppHost | `Antiphon.AppHost.exe` 249968 <- dotnet 260844 <- launcher pwsh 238568 (`logs/apphost.pid`); started 05:01:11Z |
| Server | `Antiphon.Server.exe` 455340 (05:01:23Z) <- dotnet 390384 <- dcpctrl 410364 <- dcp 202688 |
| channels.inbound consumer | `05:01:47.855 [INF] [antiphon] consuming channels.inbound as antiphon-server-bridge` (KafkaAntiphonMessagingConsumer); also `05:01:47.777 [ingress-ops] consuming channels.ops.inbound-unconsumed` |
| Server startup | `05:01:49.196 Now listening on: http://[::]:57627`, `Application started`. **0 ERR/FTL lines** since the 04:58:30 operator shutdown. The 36 exception-bearing WRN lines are `ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown` (574 of the same in the prior window, so pre-existing), plus one `403 (rate limit exceeded)` and one canceled runner.get resilience read |
| Shutdown | `04:58:30.336 Operator shutdown: reason=restart-apphost drained=true in 20ms; starting launches 0` |
| `GET /api/session-runners` | desktop available=True eligible=True stale=False (1/2); server2 available=True eligible=True stale=False (1/10); server2-temp unavailable (retired, expected) |
| Production runner | unchanged: PID 153560, `/capabilities` build `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd`; AppHost log `session-runner: port 17204 already listening - adopting` |
| fake-gateway (local dev/test only) | relaunched by the AppHost supervisor (`fake-gateway: health -> Running`); `check-daemon-build.ps1`: `build 653f114 has no newer source changes` |
| rc run 231f573c | untouched: supervisor 380956 and driver 9408 alive; TRX went 2 -> 4 (profile-1563 at 05:06:52Z); heartbeat 05:07:34Z `hangSuspected=false` |
| server2 | not touched (no rollout, no restart) |
| Telegram | no message sent |

Timings: restart 04:58:24.9Z -> script end 05:04:22.3Z (5m57s). API down from 04:58:30Z
(operator shutdown) to 05:01:49Z (server listening), about 3m19s. Build 127.7 s.

## Open items for the caller

- **Desktop session runner is stale for 63 source changes** (build 919d8d42f, running since
  2026-11-03 07:54Z), including CARD-1677/1764/1778/1790 runner fixes. This task did not restart it; the
  brief scoped it out. The documented soft restart is `pwsh -NoProfile -File scripts/restart-session-runner.ps1`
  (never `-Hard`/`-KillSessions` without the human). Decide whether and when to restart it while rc run 231f573c is live.
- CARD-1806 filed (Backlog): restart-apphost exits 1 on a healthy stack when the cold build outlasts
  dev-aspire's 90 s dashboard-URL wait.

## Rollback

Target: `d164e93ae47a79ed175beeb9367212f7a7d5d1d5` (rc/2026-11-08 tip, the previous running build).
From an independent operator shell in `C:\src\Antiphon` (outside an agent Job Object):

```powershell
git -C C:\src\Antiphon checkout --detach d164e93ae47a79ed175beeb9367212f7a7d5d1d5
pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha d164e93ae47a79ed175beeb9367212f7a7d5d1d5
```

First wait for the restart lock to age out (after 05:13:26Z) and for no land to be in flight. If the
script exits 1 with `Dashboard URL not found after 90s` in the launcher output, check `/health` and
`/api/version` directly (CARD-1806).
