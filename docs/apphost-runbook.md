# Canonical local restart

Run commands from the main checkout. For a new machine, complete the
[bootstrap prerequisites and task registration](bootstrap.md#machine-steps)
first. Establish port/process ownership before stopping anything.

| Port | Service |
|---|---|
| 17280 | Postgres (`antiphon-postgres`), always-on Docker, outside Aspire |
| 17202 | API |
| 17203 | Client, built bundle by default |
| 17204 | Session-runner, separate daemon |
| 17205 | Aspire dashboard |

## First start and AppHost restart

First start, with nothing listening on 17202 and no launch already in flight:

```powershell
Start-ScheduledTask -TaskName 'Antiphon AppHost'
```

The registered action is `scripts/autostart-apphost.ps1`, which waits for Docker
and Postgres, then invokes `dev-aspire.ps1 -NoBrowser`. Task Scheduler starts it
outside the shell that requested the start. If the task is missing, register it
with `pwsh -File scripts/install-autostart.ps1 -AppHostOnly` under an account
permitted to register tasks; do not improvise a tool-session launch.

For the usual case, where AppHost may already be running:

```powershell
pwsh -NoProfile -File scripts/restart-apphost.ps1
```

This is the canonical teardown: it stops the AppHost tree and its API, client,
dashboard and other owned resources, then relaunches and waits for health.
Postgres, the runner on 17204, and live detached pty-host / herdr sessions survive.
Never run a second bare `dev-aspire.ps1`: the old process can keep the ports and
the new code never goes live. Linked worktrees refuse by default (exit 3);
return to the main checkout. `-AllowWorktree` deliberately overrides that guard
and controls the shared stack.

**Job Object rule:** do not launch AppHost as a child of an agent tool job or
nested shell inside a kill-on-close Windows Job Object. Closing that job can kill
an apparently healthy stack. A new window is not proof of independent ownership.
Never use `-NoNewWindow`; `wt new-tab` also failed with `0x80070002` for titles
containing spaces in the recorded incident.

**Checkout limitation (CARD-0381):** the intended restart handoff is
`restart-apphost.ps1` → `Start-ScheduledTask 'Antiphon AppHost'`, with a task-only
`dev-aspire.ps1 -Direct -NoBrowser` action. The scripts in this checkout do **not**
implement that handoff: restart still uses `Start-Process pwsh`, `dev-aspire.ps1`
has no `-Direct` parameter, and `scripts/task-apphost-start.ps1` is absent.
Run the current restart command from an independent operator PowerShell session,
outside an agent's kill-on-close job. Do not assume an agent-triggered restart is
Job Object safe, that bare `dev-aspire.ps1` delegates to Task Scheduler, or that
a missing AppHost task currently produces restart exit 3. The Scheduled Task
handoff requires a separate script change; this runbook does not implement it.

## Restart only the runner

```powershell
pwsh -NoProfile -File scripts/restart-session-runner.ps1
```

The soft default stops the runner service; its live supervisor rebuilds through
`run-daemon.ps1 -BuildProjectDir` and launches the built executable. Never substitute
`dotnet run`: its Job Object can capture detached pty-hosts and kill them on
restart. Detached sessions are re-adopted from
`<SessionLogPath>/pty-hosts/manifests` and herdr sidecars. API, client, dashboard
and Postgres stay up.

Use `-Hard` for a planned supervisor refresh: it also stops the supervisor and
restarts ownership through the **Antiphon Session Runner** Scheduled Task.
`-KillSessions` is the destructive exception: it kills sessions as part of the
restart. `POST http://localhost:17204/sessions/kill-all` kills sessions without
being a runner restart. Neither belongs in routine restart recovery.

The health observation budget defaults to 180 seconds. Runner exit 2 means the
wait expired, not that startup stopped; continue with
`pwsh -NoProfile -File scripts/restart-session-runner.ps1 -WaitOnly -TimeoutSec 180`.
See [runner restart observation](bootstrap.md#runner-restart-observation-card-0420)
for result JSON, logs and exit codes; these differ from AppHost's table below.

## Verify health and the code actually loaded

```powershell
pwsh -File verify-dev-stack.ps1 -SkipBrowser
Invoke-WebRequest 'http://localhost:17202/health'
git rev-parse HEAD
(Invoke-RestMethod 'http://localhost:17204/capabilities').build.commitSha
pwsh -File scripts/check-daemon-build.ps1
pwsh -File scripts/client-mode.ps1 -Status
```

**AppHost restart does not pick up session-runner source.** Its stale-daemon check
prints `session-runner is stale for N source change(s)` and the relevant commits.
Until a runner restart, `/capabilities` can still report the previous build SHA.
CARD-0360 demonstrated this: the merge and new AppHost were present, but channel
transport-error reporting needed both server and runner updated. Health alone
does not prove that feature is live.

Restart builds the main checkout's files; it does not fetch or pull. After an
out-of-band push to `origin/master`, update the main checkout with
`git pull --rebase` before restarting, and verify the intended server code loaded
with feature/build evidence. A runner SHA can legitimately predate unrelated
commits; `check-daemon-build.ps1` compares its project source closure.

Port 17203 serves `vite preview` plus `vite build --watch`. Wait for `-Status` to
show a rebuild after the client change before checking the browser. Use
`pwsh -File scripts/client-mode.ps1 -Mode dev` when HMR is required.

## AppHost locks and exits

| Exit | Meaning | Operator action |
|---|---|---|
| 0 | Dashboard URL discovered and API `/health` returned 200 | Verify build identity / changed behavior. |
| 1 | Build failed or health wait timed out (default 150 seconds) | Read logs; after timeout the child may still be launching. Do not immediately rerun. |
| 3 | Refused: worktree guard or active launch/restart lock; nothing killed | Inspect ownership and both locks; wait for the existing launch. |
| 4 | Aspire DCP dependency-check timeout | Inspect Docker and lock/log evidence before retrying; podman text does not establish a missing runtime. |

| Lock | Lifetime |
|---|---|
| `logs/apphost.restart.lock` | Held through restart. Exit 0 and detected build failure remove it; health timeout and exit 4 retain its stamp. |
| `logs/apphost.launch.lock` | Written by `dev-aspire.ps1` during launch and removed on script exit, including a successful dashboard start. |

A present lock with a stamp younger than 15 minutes is active **even if its
holder PID is gone**: the child may still be launching (CARD-0310). An unreadable
lock is also treated as active. A stamp at least 15 minutes old is ignored.
Do not delete a fresh lock merely because its holder exited.

```powershell
Get-Content logs\apphost.restart.lock, logs\apphost.launch.lock -ErrorAction SilentlyContinue
Get-Content logs\apphost.log -Tail 40
Get-Content logs\watchdog-apphost.log -Tail 40
docker ps
```

The podman wording can be captured probe stderr when DCP times out during racing
restarts. Establish the actual Docker/process state; do not install another
runtime or raise the dependency timeout based on that text alone.

## Scheduled Tasks and deliberate downtime

- `install-autostart.ps1` Unregister+Registers tasks and terminates a running
  instance. Use `-AppHostOnly` to refresh AppHost-side tasks without killing a
  healthy runner supervisor.
- Prefer `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe` for task actions;
  version-pinned MSIX paths disappear on PowerShell updates. Keep daemon and
  auto-start scripts ASCII-only for Windows PowerShell 5.1 fallback.
- **Antiphon AppHost Watchdog** runs every two minutes, delayed 15 minutes after
  logon. It probes API `/health` and the client root over HTTP, skips fresh
  launch/restart locks, and does not count exit 3 as a restart.
- To suppress recovery for deliberate downtime, run
  `pwsh -File scripts/set-apphost-maintenance.ps1` before stopping the stack.
  This records intent and disables the watchdog; it does not stop AppHost itself.
  Do not start the AppHost task while downtime is intended. `-Clear` restores
  watchdog operation. Directly disabling **Antiphon AppHost Watchdog** also stops
  recovery, but the state observer reports it as unintentional. Postgres stays up.

## What was measured when doing this wrong

1. AppHost launched inside an agent Job Object was healthy, then vanished when
   the tool job ended: use Scheduled Task ownership and respect the checkout
   limitation above.
2. A second `dev-aspire.ps1` left the old process holding ports: the merge did not
   become live. Use the canonical restart command.
3. An AppHost-only bounce left the runner on its pre-merge SHA (CARD-0360): soft
   restart the runner too when its source changed.
4. Retrying after exit 1 or 4 killed a DCP still starting; the log blamed podman:
   inspect locks, launch logs and `docker ps` before another teardown.
5. Re-registering tasks while the runner was up killed its supervisor, leaving
   an unsupervised daemon: refresh AppHost tasks with `-AppHostOnly`.
