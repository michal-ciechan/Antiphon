# CARD-0075 — Concurrent AppHost restarts time out the DCP check, and the error blames podman: plan

**Date:** 2026-08-20
**Status:** planned (not implemented)
**Card:** CARD-0075 (`3867fb99-ab17-4428-89ec-9e6614569ba8`, Backlog) — concurrent restarts time out
Aspire's DCP dependency check; the message names podman, which is not installed.
**Precedent:** CARD-0011 (`4fcd60c`, the AppHost health watchdog — it built the lock this card needs),
CARD-0073 (the same "several things starting at once" window, different victim).
**Evidence:** measured on this machine 2026-08-20 03:38–03:42 (`dcp.exe` run directly), plus the raw
2026-08-17 failure captured in session `e55b3b86`'s transcript and a 44-run restart census from the
live database.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**The card is right about the concurrency and wrong about the podman line — and the correction makes
the fix easier, not harder.** The podman text is not DCP "falling back" and not DCP "enumerating
runtimes after the check failed". It is the **first of three lines `dcp info` prints on every single
invocation, including every healthy one**. It reaches the reader only because Aspire concatenates
`dcp info`'s captured stderr into the exception message, and a timeout truncates that stderr after the
line that returned instantly and before the line that mattered.

| Question | Answer |
|---|---|
| Does DCP fall back to podman when docker is slow? | **No.** `dcp info` probes podman first *always*. On this healthy machine right now: podman at `t+0ms`, docker at `t+382ms`, result JSON at `t+648ms`, `"runtime":"docker","installed":true,"running":true`. |
| Then why does the failure message name only podman? | Because the podman probe is the **only one that had finished** when the deadline hit. The message names the runtime that answered and omits the runtime that hung. It is inverted evidence. |
| Is the podman line suppressible? | **At the reader, yes and cheaply.** At the source, maybe — `dcp info --container-runtime docker` emits no runtime-status lines at all (measured), but I could not confirm Aspire 9.3.0 forwards `DcpOptions.ContainerRuntime` to it. §3.3 makes that a 10-minute experiment, not an assumption. |
| Does this AppHost even need a container runtime? | **No.** The model is `AddConnectionString` + `AddProject` + 2× `AddNpmApp`. Zero container resources. Yet `EnsureDcpContainerRuntimeAsync` is on the failing stack, so the probe is unconditional and can fail a launch that needs nothing from it. |
| Does CARD-0011's watchdog need a guard against this? | **Yes — and CARD-0011 already built the right primitive.** `apphost.launch.lock` is written only by `dev-aspire.ps1` and read only by the watchdog. `restart-apphost.ps1` — the only thing that *kills* anything — neither writes nor reads it. |
| Can the watchdog race itself? | **No.** `-MultipleInstances IgnoreNew`, verified live. That half is sound. |
| Can the watchdog race a manual restart? | **Yes**, two ways — §2.4. |
| Scope | **One Code slice.** Two scripts, one doc bullet, one optional config key. |

## 1. The podman line, measured

### 1.1 It is printed on every healthy run

Run directly, 2026-08-20 03:38, nothing else happening:

```
$ dcp.exe info
03:38:38.336  info  dcp.info  runtime status  {"runtime": "podman", "status": {"Installed":false,"Running":false,
                                               "Error":"exec: \"podman\": executable file not found in %PATH%..."}}
03:38:38.718  info  dcp.info  runtime status  {"runtime": "docker", "status": {"Installed":true,"Running":true,"Error":""}}
              {"version":"0.14.1", ... ,"containers":{"runtime":"docker","installed":true,"running":true}}
--- elapsed: 648 ms ---
```

Three lines, podman first, **on a fully healthy machine**. The podman "error" is a normal, expected,
always-present probe result. `dcp info --help` also reports its own `-t/--timeout` default: **20 seconds**.

### 1.2 The message is one concatenated string, not two log lines

The card renders the 2026-08-17 failure as an exception followed by a `dcp.info` line. The raw
transcript (`e55b3b86`, `ToolResult`, 14:52) shows they are a **single string** — Aspire's format is
`Application orchestrator dependency check returned an error: {0}`, and `{0}` is
`"The operation has timed out. <dcp stderr>"`:

```
Aspire.Hosting.DistributedApplicationException: Application orchestrator dependency check returned an
error: The operation has timed out. 2026-08-17T15:51:03.884+0100  info  dcp.info  runtime status
{"runtime": "podman", "status": {"Installed":false,"Running":false,"Error":"exec: \"podman\": ...

   at Aspire.Hosting.Dcp.DcpDependencyCheck.GetDcpInfoAsync(...) DcpDependencyCheck.cs:line 114
   at Aspire.Hosting.Dcp.DcpHost.EnsureDcpContainerRuntimeAsync(...) DcpHost.cs:line 64
   at Aspire.Hosting.Dcp.DcpHost.StartAsync(...) DcpHost.cs:line 57
```

This is why the misreading is not carelessness: the runtime error is *inside the sentence describing
the failure*. It is also why **log filtering cannot help** — `Antiphon.AppHost/appsettings.json`
already sets `"Aspire.Hosting.Dcp": "Warning"`, which is why a healthy `logs/apphost.log` contains no
dcp lines at all (verified: the current 1248-byte healthy log has none). The podman text is exception
*content*, not a log record, so it survives any log level.

### 1.3 The docker line is missing precisely because docker is what failed

Given §1.1's ordering, a truncated stderr containing podman-and-nothing-else means the docker probe
had **not returned** when Aspire's deadline expired. The runtime the message accuses is the one that
answered in microseconds; the runtime whose stall consumed the entire timeout is the one the message
never mentions. Any future dependency-check timeout on this machine will produce the identical
podman text regardless of cause — it is not specific to concurrency at all.

### 1.4 Nothing in this AppHost needs a container runtime

`Antiphon.AppHost/Program.cs` builds `AddConnectionString("DefaultConnection")`, one `AddProject`, two
`AddNpmApp`, plus `AddDaemonProcess` entries. No `AddContainer`, no Aspire-managed Postgres (it is the
external always-on `antiphon-postgres`). The stack trace nevertheless reaches `GetDcpInfoAsync` from
`EnsureDcpContainerRuntimeAsync`, so the probe runs unconditionally. **A subsystem this deployment
does not use sits on the critical path of every launch and can fail it.** That is worth recording even
though we cannot remove it from outside Aspire.

## 2. The concurrency

### 2.1 CARD-0011 built the lock; the process that does the killing ignores it

Verified by grep across `scripts/*.ps1` + `dev-aspire.ps1`:

| File | Writes `logs/apphost.launch.lock` | Reads it |
|---|---|---|
| `dev-aspire.ps1` | **yes** (`:23`, released in `finally`) | no |
| `scripts/watchdog-apphost.ps1` | no | **yes** (`Test-LaunchInFlight`, `:141`) |
| `scripts/restart-apphost.ps1` | **no** | **no** |

`restart-apphost.ps1` is the only script that runs `taskkill /T /F`, `Stop-Process` on every AppHost
port owner, and `Stop-Process` on `dcpctrl`/`Aspire.Dashboard` by name. It is the actor that can
destroy an in-flight launch, and it is the one actor blind to the flag that says a launch is in flight.

### 2.2 `restart-apphost.ps1` returns while its own launch is still running

It spawns `dev-aspire.ps1` **detached** (`Start-Process`, step 5) and then polls for health against its
own `TimeoutSec` (default **150 s**). Its child's worst case is longer:

| Phase (`dev-aspire.ps1`) | Budget |
|---|---|
| docker network preflight | up to 8 s |
| `dotnet restore` + `npm install` (no `-NoBuild`) | seconds to minutes |
| wait for dashboard URL in log | up to 90 s |
| wait for Postgres | up to 45 s |
| trailing sleep | 3 s |

So `restart-apphost.ps1` can print **"AppHost did not come up within 150s"** and `exit 1` while the
`dev-aspire.ps1` it started is still mid-launch. **A non-zero exit does not mean it stopped launching.**
The caller reads a failure, re-runs, and the re-run's `taskkill` lands on a DCP that is still coming up
— which is the card's mechanism, arrived at without anyone doing anything unreasonable.

### 2.3 Re-runs inside that window are already in the record

Census of 44 `ToolResult`s carrying the script's own `Restarting Antiphon AppHost` banner. Two
same-session sequential re-runs land inside the previous run's launch window:

```
08-17 19:01:28  68c9d4d5      08-17 19:17:37  27cda746
08-17 19:03:54  68c9d4d5  +145s      19:20:48  27cda746  +191s
```

Both are the card's predicted shape — one agent, deploy step "failed", ran it again. Cross-session
gaps go down to 389 s (`e55b3b86` 14:50:06 → `cefed08a` 14:56:35, straddling the incident itself).
The race is routinely reachable at the observed cadence, not a freak.

### 2.4 What CARD-0011's watchdog does and does not cover

**Covered.** Watchdog vs itself: `-MultipleInstances IgnoreNew` (verified live:
`State Ready`, `ExecutionTimeLimit PT30M`, firing every ~2 min, `LastResult 0`). Watchdog vs a
`dev-aspire.ps1` launch: the lock check plus the logon-task `Running` check. Both sound.

**Not covered — two concrete holes:**

1. **Watchdog vs `restart-apphost.ps1`'s teardown.** During steps 1–4 the ports are down and no lock
   exists yet — `dev-aspire.ps1` has not been spawned. A watchdog fire that begins there sees no
   launch in flight and three failing probe rounds, then calls a **second** `restart-apphost.ps1` over
   the top of the first. The cooldown does not save it: `restartsUtc` is stamped only by the watchdog's
   own restarts, so a manual restart contributes no cooldown at all.
2. **TOCTOU inside a single fire.** `Test-LaunchInFlight` runs once, at the top. Three probe rounds at
   `IntervalSeconds` 15 with `ProbeTimeoutSec` 5 on two URLs is up to ~60 s, plus `docker info`, before
   the restart is invoked. A launch that starts anywhere in that ~60 s window is never re-checked.

The watchdog has already performed one real automatic restart
(`apphost-watchdog.state`: `restartsUtc: ["2026-08-19T21:47:26Z"]`), so this is a live path, not a
theoretical one. It also invokes `restart-apphost.ps1` **without `-NoBuild`**, so every automatic
recovery runs `dotnet restore` + `npm install` — widening the very window in which it can be raced.

## 3. Slice S1 — make concurrent restarts impossible, and say what actually failed (Code)

Tier: sonnet. `scopeGlob: scripts` (plus `CLAUDE.md`, and `Antiphon.AppHost/appsettings.json` only if
§3.3's experiment succeeds). One slice: it is all the same change — stop the collision, then explain
the collision.

### 3.1 The lock

- `restart-apphost.ps1` takes `logs/apphost.restart.lock` (pid + UTC stamp — the same shape
  `dev-aspire.ps1` writes, so the parser is shared) **before step 1**, and releases it in a `finally`
  (not a trailing `Remove-Item`: `$ErrorActionPreference = 'Stop'` would skip that, which is the same
  reasoning `dev-aspire.ps1:21` already records).
- Refuse, do not queue — the card's call, and right: a caller that waited 150 s and then failed cannot
  tell what happened. **Refusal must be distinguishable from failure**, because an indistinguishable
  failure is what produces the retry. Use a distinct exit code (`3`) and a message that names the
  holding pid and its age.
- `restart-apphost.ps1` must **also** honour `apphost.launch.lock` — a bare `dev-aspire.ps1` (the
  `autostart-apphost.ps1` path, or a human) is a launch in flight and must not be killed mid-DCP.
- Staleness on both locks: take the lock if the recorded pid is dead or the stamp is older than a max
  age. Reuse the watchdog's `LockMaxAgeMinutes` (15) so there is one number, not two.
- `watchdog-apphost.ps1`: add `apphost.restart.lock` to `Test-LaunchInFlight`, and **re-evaluate
  `Test-LaunchInFlight` immediately before invoking the restart** (closes §2.4 hole 2 — one call, one
  line). Also: when `restart-apphost.ps1` exits `3` (refused), log INFO and **do not** append to
  `restartsUtc`; today it stamps unconditionally, so a refused run would burn flap budget it never spent.

### 3.2 The diagnosis (this is the half fully under our control)

`restart-apphost.ps1` already greps `apphost.log` for `The build failed`. Add a matcher for
`dependency check returned an error` / `The operation has timed out` and, on a match, print a verdict
that beats the exception:

- the **timeout** is the failure; any podman text in that message is noise DCP prints on every run,
  healthy included;
- what docker actually said — run `docker info` / `docker ps` and quote the result, so the reader gets
  the fact the exception omitted;
- the likely cause — another restart or launch — naming `logs/apphost.launch.lock`,
  `logs/apphost.restart.lock` and `logs/watchdog-apphost.log`;
- and exit with a code distinct from a generic timeout.

This works whether or not §3.3 does, and it lands where the reader already is.

### 3.3 Optional source fix, and a deterministic reproduction

`dcp info --container-runtime docker` prints **no runtime-status lines at all** (measured) — only the
result JSON. So *if* Aspire forwards `DcpOptions.ContainerRuntime`, setting it removes the podman text
from every future timeout at the source. **Unverified, and do not assume it:** the literal
`--container-runtime` does not appear in `Aspire.Hosting.dll` 9.3.0, and DCP honours no environment
variable for it (`DCP_CONTAINER_RUNTIME=docker` measured — no effect).

One experiment settles it, and hands the slice a repro it otherwise would not have. In
`Antiphon.AppHost/appsettings.json` set `"DcpPublisher": { "DependencyCheckTimeout": 1 }` and start the
AppHost. If Aspire binds that section, the check times out **on a healthy machine in one second** and
prints the exact card message — no concurrency needed. That single experiment (i) confirms the config
section name, (ii) gives a deterministic fixture for §3.2's matcher, and (iii) lets you A/B
`"ContainerRuntime": "docker"` against it and see whether the podman text disappears.

**Revert `DependencyCheckTimeout` before committing.** A 1-second dependency check breaks every
launch. Keep `ContainerRuntime` only if the A/B proves it works; drop it silently if not — §3.2 is
already sufficient.

### 3.4 CLAUDE.md

Add to Gotchas, in the genre of the MSB3552 resx and HNS "Created" entries (the card asked for this,
and it is the same "the message names the wrong thing" class):

> **A podman error in `apphost.log` means the DCP dependency check TIMED OUT — not that a container
> runtime is missing.** `dcp info` probes podman first on *every* invocation, healthy ones included
> (measured: podman at +0 ms, docker at +382 ms, result at +648 ms), and Aspire splices that stderr
> into the exception message. A timeout truncates it after the podman line, so the message names the
> runtime that answered instantly and omits the one whose stall caused the failure. Docker is fine;
> this AppHost has no container resources at all. The real cause is almost always **two restarts
> racing** — `restart-apphost.ps1` can exit non-zero while the `dev-aspire.ps1` it spawned is still
> launching, so the natural retry kills a DCP that is still coming up. Check `docker ps`, then
> `logs/apphost.restart.lock` and `logs/watchdog-apphost.log`, before changing any configuration.

### 3.5 Acceptance (live — there is no PowerShell test harness in this repo)

Match CARD-0011's live-verified commit-message convention:

1. Two `restart-apphost.ps1` at once → the second exits **3** with a message naming the first's pid
   and age; the first completes healthy; `/health` on 17202 answers.
2. `restart-apphost.ps1` while a bare `dev-aspire.ps1` is launching → refused, launch survives.
3. `watchdog-apphost.ps1 -ProbeOnly` during a held restart lock → logs skip, does not restart.
4. Watchdog fired against a refusal → logs INFO and `restartsUtc` gains **no** entry.
5. §3.3's `DependencyCheckTimeout: 1` repro → §3.2's matcher fires and prints the docker verdict
   instead of the podman message. **Both keys reverted before commit.**
6. Session-runner on 17204 untouched throughout (PID unchanged) — the standing constraint on anything
   that touches these scripts.

## 4. Deliberately not in scope

- **Raising `DependencyCheckTimeout`.** It masks the collision instead of preventing it, and the
  collision has other victims (CARD-0073).
- **Passing `-NoBuild` from the watchdog.** It would shorten the launch window, but the lock makes the
  window's length irrelevant, and skipping `npm install` on an automatic recovery has its own failure
  mode (a newly added dependency → client will not start → the watchdog loops). Flagged, not taken.
- **Removing the container-runtime probe from the startup path.** Correct in principle (§1.4) but it
  lives inside Aspire; not ours to change.
- **Queueing instead of refusing.** The card decided this and the reasoning holds.

## 5. Card housekeeping

CARD-0075 stays open for S1. Its description should gain a one-line correction: the podman block is
`dcp info`'s ordinary first-line probe output spliced into the exception, **not** a fallback or a
post-failure enumeration — a reader who believes the original framing will still look for a runtime
problem. No other card overlaps; CARD-0011 is complementary and its watchdog is the second caller S1
must make safe, not a duplicate.
