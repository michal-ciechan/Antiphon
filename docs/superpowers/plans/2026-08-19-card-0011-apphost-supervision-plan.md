# CARD-0011 — Nothing supervises the AppHost: plan

**Date:** 2026-08-19
**Status:** planned
**Card:** CARD-0011 (Backlog, labels `reliability`, `apphost`) — migrated from TODO.md 2026-08-09.
**Evidence:** re-verified against this machine and the repo on 2026-08-19. Live process,
port and Scheduled Task state captured below.

This is a planning document only. Do not write `scripts/watchdog-apphost.ps1` in the
Plan pass — that is S1 execution.

## Verdict up front

**The card is right, and it understates the blast radius.** The "Antiphon AppHost"
Scheduled Task fires **once at logon and never again** — there is no health loop, no
liveness check, and no restart path of any kind after that one shot. Confirmed on this
machine, not inferred.

**One Code slice: a health-triggered restart watchdog on a repeating Scheduled Task,
calling the `restart-apphost.ps1` that already exists.** Not a `run-daemon.ps1`-style
process supervisor — see §3 for why that shape is wrong for this resource specifically.

| Question | Answer |
|---|---|
| Does the Scheduled Task restart the AppHost if it crashes mid-session? | **No.** It is a one-shot launcher that exits 0. `RestartCount 2` restarts a *failed task action*, which is a different event and cannot fire after the action has already succeeded. |
| Is there any ongoing health-check loop? | **No.** `verify-dev-stack.ps1` and `scripts/bootstrap-check.ps1` are one-shot and human-invoked. Nothing is scheduled. |
| Is anything supervised? | **Yes — two of six resources**, and neither is one the card is about. |
| Scope | **One slice.** One new script, two small edits, one doc bullet. |

## 1. What supervision actually exists (verified 2026-08-19)

### 1.1 The AppHost task has not run in six days

```
Name               : Antiphon AppHost              Name               : Antiphon Session Runner
State              : Ready                         State              : Running
LastRun            : 13/08/2026 20:41:41           LastRun            : 13/08/2026 20:40:40
LastResult         : 0                             LastResult         : 267009   (0x41301 = running)
NextRun            : <empty>                       NextRun            : <empty>
NumberOfMissedRuns : 0                             NumberOfMissedRuns : 0
```

The AppHost task is **Ready with no NextRun**: it completed on 13 Aug and will not
execute again before the next logon. Meanwhile the AppHost currently running started
**19/08/2026 20:57:47** — a manual `restart-apphost.ps1`, six days after the task last
fired. Every AppHost restart between those dates was a human noticing.

The contrast with the session-runner task is the whole finding: that one is **Running**,
because its action (`autostart-session-runner.ps1` → `run-daemon.ps1`) blocks forever in
a restart loop. The AppHost task's action (`autostart-apphost.ps1`) exits 0 as soon as
`/health` returns 200. Both were registered by the same script, minutes apart; only one
of them is a supervisor.

`-RestartCount 2 -RestartInterval 5min` on the AppHost task (`install-autostart.ps1`) is
not a counter-argument: Task Scheduler's restart-on-failure applies to a task **action
that fails**, and this action succeeded. Nothing re-evaluates it afterwards.

### 1.2 Only two of the six things the card names are supervised — and not the two that broke

From `Antiphon.AppHost/Program.cs`:

| Resource | Port | How it is declared | Survives AppHost death? |
|---|---|---|---|
| session-runner | 17204 | `AddDaemonProcess` | **Yes** — detached `run-daemon.ps1` supervisor |
| fake-gateway | 17208 | `AddDaemonProcess` | **Yes** — same |
| server | 17202 | `AddProject` (DCP) | No |
| client (Vite) | 17203 | `AddNpmApp` (DCP) | No |
| storybook | 17209 | `AddNpmApp` (DCP) | No |
| dashboard / control API | 17205 / 17207 | in the AppHost process | No |

Live proof of the split, from this machine right now: `Antiphon.SessionRunner` PID 43424
started **03:43** today and is still serving 17204 across the **20:57** AppHost restart.
The daemon pattern works; it just does not cover the resources CARD-0011 is about.

The control API on 17207 is not a lever either. `ControlApiService` routes
`/control/{name}/start|stop|restart` straight into `DaemonProcessService`, whose
`_entries` are populated only from `AddDaemonProcess`. **There is no per-resource restart
for `server`, `client` or `storybook`** — and 17207 dies with the AppHost anyway. A full
AppHost restart is the only mechanism that exists.

### 1.3 The probe has to be HTTP, because the published ports lie

Verified right now: ports 17202 **and** 17203 are both owned by **PID 17612 =
`dcpctrl`**, Aspire's DCP proxy, not by the server or by Vite. A TCP-listen check on
17203 therefore tests the proxy, and would report healthy while Vite is dead behind it.

Current tree: `pwsh` 4944 (the pid in `logs/apphost.pid`) → `Antiphon.AppHost` 41480 →
`dcpctrl` 17612 (17202, 17203) + `Aspire.Dashboard` 25624 (17205) + `node` 5840 (17209,
`isProxied:false`).

Note `logs/apphost.pid` holds the **wrapper** pwsh pid, not the AppHost's — a pid check
against that file proves less than it looks like it does.

### 1.4 What the manual path already gets right

`scripts/restart-apphost.ps1` is the correct restart primitive and the slice should call
it rather than reimplement anything:

- kills the wrapper tree from `logs/apphost.pid`;
- frees every AppHost-owned port (17200, 17202, 17203, 17205, 17206, 17207, 17209),
  because DCP children escape the wrapper tree on an unclean exit;
- kills orphaned `dcpctrl` / `Aspire.Dashboard`;
- **explicitly preserves the session-runner on 17204** by resolving its owning pid first
  and skipping it — this is the single most important behaviour to inherit, and the
  reason not to write fresh kill logic;
- relaunches via `dev-aspire.ps1` and waits up to 150s for dashboard + `/health`.

## 2. The gap, stated precisely

Between the AppHost dying and a human noticing, nothing observes and nothing acts. The
card's own incident is the shape: 2026-08-08 21:00 → 00:20, three and a half hours, with
Caddy healthy and logging thousands of `connection refused` against
`host.docker.internal:17203`. Caddy is out of scope — it was correct throughout, and the
thing it proxies to was gone.

## 3. Why a watchdog, not a `run-daemon.ps1` supervisor

The card offers "either a supervisor loop or a health-triggered restart". Take the
second, for three concrete reasons:

1. **`run-daemon.ps1` only detects process exit.** The AppHost's characteristic failure
   leaves orphans holding ports — that is precisely why `restart-apphost.ps1` has a
   port-freeing pass and a stale-`dcpctrl` pass. A bare relaunch into held ports fails.
2. **It would fork the launch path.** `run-daemon.ps1` owns the process it starts
   (`WaitForExit`), so the AppHost would have to stop being launched by
   `dev-aspire.ps1`. A manual `dev-aspire.ps1` would then bypass the supervisor and
   produce exactly the two-colliding-AppHosts state CLAUDE.md already warns about.
3. **A blocking supervisor needs supervising.** The session-runner solves that with the
   task's restart-on-failure. A repeating task needs no such backstop: each fire is
   independent and stateless, so a crashed fire costs one interval and nothing else.

## 4. The slice — S1: health-triggered AppHost watchdog

### 4.1 `scripts/watchdog-apphost.ps1` (new)

**ASCII-only** — it runs from a Scheduled Task and may be read by Windows PowerShell
5.1, which parses a no-BOM file as CP1252 (CLAUDE.md rule; `autostart-apphost.ps1` and
`run-daemon.ps1` both carry the same note).

One fire does:

1. **Bail if a launch is in flight.** If `logs/apphost.launch.lock` exists and is under
   `LockMaxAgeMinutes` (15) old, log and exit 0. See §4.2.
2. **Probe, and require confirmation.** `GET http://localhost:17202/health` expecting
   200, and `GET http://localhost:17203/` expecting 2xx/3xx. Both HTTP, per §1.3. On any
   failure, re-probe twice more at 15s spacing. **Restart only if all three rounds
   fail** (~45s), so a build, an npm install or a GC pause cannot trigger a kill.
   Probe 17203 as well as 17202 because Vite is the card's user-facing symptom and an
   `AddNpmApp` resource can die without taking the AppHost with it — and §1.2 says the
   only available response is still a full restart.
3. **Check Docker before acting.** If `docker info` fails, log and exit **without**
   counting against the flap budget — a restart cannot succeed anyway
   (`dev-aspire.ps1` hard-errors when Docker is down).
4. **Cooldown.** If `logs/apphost-watchdog.state` records a restart under
   `CooldownMinutes` (10) ago, log and exit. `restart-apphost.ps1` alone waits up to
   150s, and the stack can take longer than that to be fully healthy.
5. **Flap cap.** If the state file records `MaxRestartsPerWindow` (3) restarts inside
   the last 60 minutes, **stop restarting** and log an ERROR line each fire until the
   window clears. A stack that will not build must not be thrashed every two minutes.
   Precedent: CARD-0056's re-adoption cap of 3 before escalating rather than looping.
6. **Restart** by invoking `pwsh -File scripts/restart-apphost.ps1`, then stamp the
   state file. Do not duplicate its teardown (§1.4), and specifically do not write new
   kill code near port 17204.
7. **Log** to `logs/watchdog-apphost.log` (`logs/` is gitignored): every failure,
   restart, skip-reason and flap line, plus at most one `OK` heartbeat per hour. That is
   ~30 lines/day, so no rotation is needed — deliberately unlike `run-daemon.ps1`, which
   rotates because a service's whole stdout is redirected into its log.

Parameters worth having: `-ProbeOnly` (probe, log, never restart — the safe acceptance
check in §4.5), plus `-CooldownMinutes`, `-MaxRestartsPerWindow`, `-IntervalSeconds`.

### 4.2 `dev-aspire.ps1` (edit, ~10 lines)

Write `logs/apphost.launch.lock` (launcher pid + UTC stamp) near the top and remove it
in a `finally`/`trap` — the script sets `$ErrorActionPreference = 'Stop'` and exits via
`Write-Error` on several paths, so a plain trailing `Remove-Item` is not enough.

This one file covers **all three** launch paths, because every one of them bottoms out
here: `autostart-apphost.ps1` → `dev-aspire.ps1`, `restart-apphost.ps1` →
`dev-aspire.ps1`, and a human running it directly. Without it, a watchdog fire lands in
the middle of a human's `restart-apphost.ps1` and the two kill each other's AppHost.

The watchdog ignores a lock older than 15 minutes so a hard-killed launcher cannot
disable supervision permanently.

### 4.3 `scripts/install-autostart.ps1` (edit)

Register a third per-user task, **"Antiphon AppHost Watchdog"**:

- **Principal identical to the other two** — `-LogonType Interactive -RunLevel Limited`
  as `$env:USERDOMAIN\$env:USERNAME`. Non-negotiable: the restart shells out to
  `dev-aspire.ps1`, which needs the user's PATH (dotnet, npm, docker), and the AppHost
  must land in the user's session so spawned agents inherit the profile.
- **Trigger:** `-AtLogOn` with `.Delay = 'PT15M'`, and `.Repetition` set to every
  **2 minutes**, indefinitely. The 15-minute delay is not cosmetic: the logon AppHost
  task legitimately holds 17202 dead for up to 5 minutes of Docker wait plus restore,
  `npm install` and a 180s health wait, and a watchdog firing into that window would
  kill the launch it is supposed to protect.
- **Settings:** `-MultipleInstances IgnoreNew` (a restart outlives the interval),
  `-ExecutionTimeLimit (New-TimeSpan -Minutes 30)`, `-AllowStartIfOnBatteries
  -DontStopIfGoingOnBatteries -StartWhenAvailable`.
- Reuse the existing `$psExe` resolution verbatim — it already filters version-pinned
  MSIX `WindowsApps\Microsoft.PowerShell_*` paths, which is a live trap this repo has
  been bitten by.
- Add `-NoWatchdog` (mirroring `-NoAppHost`), cover the task in the `-Uninstall` list,
  treat it as AppHost-side so **`-AppHostOnly` includes it** and a healthy running
  session-runner is still never touched, and add it to the closing summary block.

**One implementation risk to verify rather than assume:** attaching `.Repetition` to a
logon trigger via PowerShell is a finicky API (`[TimeSpan]::MaxValue` for
`RepetitionDuration` is rejected on some builds; `(New-TimeSpan -Days 3650)` is the
usual fallback). The acceptance check in §4.5 catches a silent failure directly.

### 4.4 `CLAUDE.md` (edit)

One bullet in **Always-on backend (auto-start)**: the third task, the two-minute
interval, `logs/watchdog-apphost.log`, that it calls `restart-apphost.ps1` and so never
touches the session-runner, and the off switch —
`Disable-ScheduledTask -TaskName "Antiphon AppHost Watchdog"` when you deliberately want
the stack down. No `logs/apphost.state` file: `Disable-ScheduledTask` is one command,
needs no writers in three scripts, and is discoverable from the task list.

### 4.5 Verification

There is **no Pester or other PowerShell test harness in this repo** (confirmed — no
`*.Tests.ps1`, no Pester reference anywhere outside `node_modules`), and no .NET test
touches these scripts. Verification is a manual acceptance run; do it in this order and
report the actual results:

1. `pwsh -File scripts/install-autostart.ps1 -AppHostOnly`
   → then `Get-ScheduledTask -TaskName 'Antiphon Session Runner'` must still read
   **Running** with an unchanged `LastRunTime`. Re-registering a running task kills its
   supervisor; this proves the new task did not.
2. `Get-ScheduledTaskInfo -TaskName 'Antiphon AppHost Watchdog'` → **NextRunTime must be
   populated** (roughly two minutes out). Contrast with the AppHost task, whose NextRun
   is blank (§1.1) — this single field is the difference between a supervisor and a
   one-shot, and it is how a botched `.Repetition` shows up.
3. `pwsh -File scripts/watchdog-apphost.ps1 -ProbeOnly` against the healthy stack → logs
   both probes OK, exits 0, restarts nothing.
4. **The real test.** Record the session-runner pid, then
   `taskkill /T /F /PID (Get-Content logs/apphost.pid)`; confirm 17202 and 17203 stop
   answering; wait up to 4 minutes. Expect: both back, `logs/watchdog-apphost.log`
   showing three failed probe rounds and the restart, and — critically — **the
   session-runner pid unchanged and its live agent sessions intact**. Kill the AppHost
   while an agent session is running so this is actually tested, not assumed.
5. **Flap cap.** Seed `logs/apphost-watchdog.state` with three restart timestamps inside
   the last hour, run the watchdog against a deliberately dead probe URL, and confirm it
   logs the flap line and does **not** restart. (Seed the file rather than breaking the
   real stack three times.)
6. `pwsh -File scripts/install-autostart.ps1 -Uninstall -AppHostOnly` removes both
   AppHost-side tasks and leaves the session-runner task alone.

## 5. Out of scope

- **Caddy.** Healthy throughout the incident; nothing to fix.
- **A per-resource restart lever for `server` / `client` / `storybook`.** Extending
  `DaemonProcessService` / the 17207 control API to cover DCP resources would allow
  bouncing Vite alone instead of the whole stack. Real, but a different card — and it
  cannot help the case CARD-0011 names, where 17207 is dead too.
- **Alerting into Antiphon** (incident, card, notification). The server is what is down,
  so it cannot record its own outage. The log file is the record.
- **`dev-backup.ps1` / boot-time supervision before logon.** The principal must stay
  Interactive (§4.3); a machine that is not logged in has no agents running anyway.
- **Rewriting the AppHost launch onto `run-daemon.ps1`.** Rejected in §3.

## 6. Commit

One commit, message naming the real outcome:

```
feat(reliability): CARD-0011 - health-triggered watchdog restarts a dead AppHost
```

Body should state that the logon task fires once and never re-checks (Ready, no
NextRun), that the watchdog probes 17202/health and 17203 over HTTP because `dcpctrl`
holds both published ports, that it restarts via `restart-apphost.ps1` so the
session-runner on 17204 is preserved, and the cooldown/flap numbers actually shipped.
