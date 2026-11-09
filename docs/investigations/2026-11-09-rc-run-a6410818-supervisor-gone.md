# rc run a6410818: supervisor gone, run alive (2026-11-09)

Task d14c79fb, read-only investigation on the desktop. Nothing was stopped, started or signalled.
No locks or run files were touched, and no build or test was run. Observed 2026-11-09 03:30-03:35Z.

## Verdict

The supervisor (`pwsh` 449576) and its wrapper (`pwsh` 180968, `start-supervisor.ps1`) were
**terminated abruptly between 00:27:41Z and about 00:28:20Z**. That window contains the user's
bedtime lock. It was not an intentional supervisor exit and it was not a crash. The driver chain
(469388 launch-rc, 310304 build-slot, 74508 nightly-run, 262896 chunk runner) is unaffected and
still owns both locks. This is the **same failure as run 7f8e9fd1 (2026-11-02)**, which CARD-1667
fixed in the repository launch set. Task c9cb914c did not use that set: it launched from a
task-local copy of the old private set ("Task-local copy of task-0297fa9c proven launch set",
`launch-record.json` `launcherProvenance`). The skill forbids that.

**Recommendation: leave the run alone.** Do not relaunch a supervisor. No sanctioned way exists
to attach one to a live driver, and there is no need for one.

## 1. When and why the supervisor exited

| Evidence | Value |
|---|---|
| Last heartbeat (`task-c9cb914c\supervisor-heartbeat.json`) | `2026-11-09T00:27:41.91Z`, trx=528, processes=7, hangSuspected=false |
| Heartbeat cadence | About every 35-40 s (30 s sleep plus recursive scans), so the next was due near 00:28:20Z |
| User session activity | `Windows PowerShell` 400 at **00:27:49.99Z**: an interactive `powershell.exe` console opened (pid 34008). PSReadLine history ends `... sleepmon / exit / exit`. |
| `sleepmon` (`C:\Users\lndco\.local\bin\sleepmon.cmd`) | `rundll32 user32.dll,LockWorkStation`, then `powershell -NoProfile -Command ... SendMessage(-1, WM_SYSCOMMAND 0x0112, SC_MONITORPOWER 0xF170, 2)`. Ran 00:27:53.5-00:27:58.5Z (events 400/800/403, 4103/4104 pid 101580). |
| Winlogon/Operational | Notification event 4 (lock) at **00:27:53Z**. System 16 at 00:27:53Z: LockApp hive touched. |
| Crash, WER, .NET Runtime 1026, Application Error 1000, WMI quota 5612, logoff, reboot, Windows Update | **None** from 00:20 to 00:45Z. The only Application errors are the unrelated `[inbound-unconsumed]` gateway lines every 6 min. |
| `supervisor-console.log` | **Exactly 4096 bytes**, last written 22:05:08Z, cut off mid-line (`... idle`). |
| `supervisor-stderr.log` | 0 bytes. `commission-exit.txt` is absent and no `EXIT` line exists. |
| Agent transcripts 00:15-00:35Z | Only the orchestrator (b2f1a869) was active. At 00:29:51Z it **read** the heartbeat. It killed nothing. No transcript names 449576, 180968, `supervise-rc` or `start-supervisor` with a kill. |
| Security 4689 (process termination) | Not readable without elevation, and not enabled (see the skill's CARD-1667 note). |

How to read this:

- **Not an intentional exit.** The task-local `supervise-rc.ps1` has no deadline, lifetime cap or
  heartbeat budget. It loops `while (-not $proc.HasExited)`, and on child exit it writes `EXIT`
  and `commission-exit.txt`. Neither exists, and the child is alive.
- **Not a script exception.** `$ErrorActionPreference='Stop'` makes an uncaught loop error (for
  example `Get-CimInstance Win32_Process` failing) end the script normally. Output is then flushed
  and the error goes to stderr. Instead stderr is empty, and the stdout tail is lost (roughly 14
  PROGRESS lines from 22:15 to 00:25).
- **Abrupt termination of both console members.** In the old set, `start-supervisor.ps1` runs
  `& pwsh supervise-rc.ps1 1> supervisor-console.log`. The wrapper holds a 4 KiB buffered file
  writer, so the file grows in 4096-byte steps and is completed only when the wrapper exits
  normally. Exactly 4096 bytes, stale since 22:05, means the wrapper died without flushing.
  449576 inherits 180968's console. 469388 was started with `Start-Process -WindowStyle Hidden`,
  so it has its own console (conhost 172036, alive) and it survived. Whatever ended the pair
  killed exactly the members of one console.
- **Most likely trigger.** The user's lock routine at 00:27:49-00:27:58Z, which is inside the
  death window. `launch-detached.ps1` starts the wrapper through `Win32_Process.Create`, which
  gives no `ShowWindow`. In the user's session (all of these are SessionId 1), that is most likely
  a visible, blank `pwsh.exe` console. Closing that window (CTRL_CLOSE_EVENT) kills every process
  attached to it without .NET buffer flushes, which fits every observation above. That the window
  was visible and was closed is **inferred, not proven**: no window or termination audit exists.
  The `SC_MONITORPOWER` broadcast is an unlikely mechanism, because the 2026-11-02 death happened
  at a plain lock with no `sleepmon` (see section 3).

## 2. What the supervisor did, what is lost, and the risk now

The task-local supervisor (`C:\Antiphon\releases\task-c9cb914c\supervise-rc.ps1`) **only
observes**. It starts `launch-rc.ps1` once, then every ~30 s writes `supervisor-heartbeat.json`.
It writes `hang-suspected.txt` after 20 idle minutes, a PROGRESS line every 10 min, and
`EXIT <code>` plus `commission-exit.txt` when the child exits. It never relaunches a dead driver,
never handles stale locks, and never stops anything. The repository version
(`scripts/qualification/supervise-rc.ps1`) states the same: "The supervisor never stops,
signals, or restarts a process and never clears a lock. Its death does not end the run".

Lost by its death:

- The heartbeat. `watch-qual.ps1` now emits `HEARTBEAT-STALE` and `status-snapshot.ps1` reports
  a growing `heartbeatAgeSeconds`. Both are observation-only consumers.
- Supervisor-side hang detection (`hang-suspected.txt`).
- The run's exit code in `commission-exit.txt`.

**Not lost:**

- The run's end signal is `completedAt` in its own `last-run.json`, written by nightly-run. This
  run has a **private state root**
  (`C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894\state-c9cb914c`), so no later run
  overwrites that file.
- Seed and ordering live in the driver and in `C:\Users\lndco\antiphon-seeds\4e0d1159.json`
  (`full-order.json` and `lanes.json` are in the task directory).

**If the driver dies**, nothing restarts the run. That was true with a live supervisor too: it
would only have recorded `EXIT`. Recovery is a fresh, operator-authorized launch with failed-first
from this run's results. The supervisor's absence does not change that.

Nothing else consumes the heartbeat as a control signal:

- The nightly watchdog (`docs/nightly-watchdog.md`) has its own heartbeat and does not read this
  file.
- No Windmill job reads it.
- The orchestrator reads it only for status.

Live state at 03:33Z:

- `run.lock` (state root) and `C:\Antiphon\verification\native-run.lock` both name pid 74508,
  run `a6410818876948f7ac9d7a46bc592900`.
- `last-run.json` `phase=tests`, with no `completedAt`.
- 760 `*.trx`. The newest activity is `e2e-all-split-9` at 03:33Z, with 96124 `Antiphon.E2E.exe`
  running `C467_V22`.

Treat it as the skill's `supervisor-lost-run-alive` state: "an observation gap, not a run
failure, and no reason to stop or restart the run". Use these instead of the heartbeat:

- `watch-run.ps1` / `scripts/rc-qualification-status.ps1`.
- `watch-qual.ps1` on the log directory. Ignore its `HEARTBEAT-STALE` line.
- `active-chunk.json` under the timing root.

`observe.ps1` cannot be used. It needs `supervisor-identity.json`, which the old private set never
writes.

## 3. Previous runs: a known pattern

Final heartbeats from every `C:\Antiphon\releases\task-*\supervisor-heartbeat.json`. The tell is
a console log that is an exact multiple of 4096 bytes and stopped long before the last heartbeat.

| Run | Task | Last heartbeat | Console log | Reading |
|---|---|---|---|---|
| db2c2088 | f2e8ddf0 | 10-26 02:03:05Z | 8192 B, 10-25 22:11 | **Machine reboot** at 02:04Z (boot events, driver and service installs). The whole run died. Different cause. |
| 7f8e9fd1 | 8a7dbe6d | 11-02 22:09:07Z | 8192 B, 21:53, mid-line `PR` | Supervisor died, run survived (memory and skill). Winlogon **lock** (event 4) at **22:09:42Z**, inside the death window. No `sleepmon` or PowerShell event that day. |
| a6410818 | c9cb914c | 11-09 00:27:41Z | 4096 B, 22:05, mid-line | This incident. Lock at 00:27:53Z. |
| 4e0d1159 | 0297fa9c | 11-08 17:44:18Z | 7708 B, 17:44 | Authorized `taskkill /T /F` stop by c9cb914c (`stop-result.json`). Not a spontaneous death. |
| e737e9fe, c3f65ddb, 8e938b6d, 472c7495, 766e3df5, 0b05506b, f40ad905, aa64a906 | ... | Last heartbeat matches the console's last write | Not 4 KiB-aligned | Stopped or superseded runs. No spontaneous-death signature. |

So the old launch set has now lost its supervisor **twice**. Both times it happened within about
40 s of the user locking the desktop at night. The skill's "Supervisor launch set (CARD-1667)"
section names the 7f8e9fd1 cause (the supervisor shared its wrapper's console and wrote through a
`1>` wrapper buffer). It requires new Full runs to use `scripts/qualification/` with its own
hidden console and per-line flushed `supervisor.log`, "never with a private copy". c9cb914c
(and 0297fa9c before it) launched from a private copy, so the CARD-1667 fix was bypassed.

## 4. Recommendation

1. **Leave run a6410818 running.** Do not relaunch a supervisor. No sanctioned attach exists:
   - Repository `supervise-rc.ps1` always *starts* `-LaunchScript`. It has no attach-to-PID mode.
   - `start-supervisor.ps1` refuses when `run.lock` or `last-run.json` already names the run id
     ("bind a fresh -RunId").
   - The task-local copy also starts a new launch, and its prechecks refuse a live run anyway
     (`Fresh state root already exists`, `Shared native lock present`, test hosts present). Even
     if it got past them, it would start a **second** nightly-run, which nightly-run refuses
     (exit 3). That is a waste at best, a lock fight at worst.
   - `docs/investigations/` has no run-restart or supervisor re-attach note.

   Exact refusal conditions, in case someone is tempted (none of this was run):

   ```powershell
   # Do NOT run. Shown only to document why it refuses.
   pwsh -NoProfile -File C:\Antiphon\releases\checkout-c9cb914c\scripts\qualification\start-supervisor.ps1 `
     -TaskRoot C:\Antiphon\releases\task-c9cb914c -LaunchScript C:\Antiphon\releases\task-c9cb914c\launch-rc.ps1 `
     -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894\logs `
     -StateRoot C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894\state-c9cb914c `
     -RunId a6410818876948f7ac9d7a46bc592900
   # -> "start-supervisor: refused, ...\last-run.json already names run id a6410818..." (exit 1),
   #    and also refused on ...\run.lock. (The CARD-1667 set IS present in this rc checkout,
   #    checkout-c9cb914c\scripts\qualification\, so it was available to c9cb914c.)
   ```

   If an always-on heartbeat is wanted for the rest of the run, the right tool is a **read-only
   observer** that writes a heartbeat elsewhere and launches nothing. No such script exists in the
   repository today. That would be a new card, not something to improvise on the live run.
2. **Watch the run without the heartbeat.** Use `rc-qualification-status.ps1`, `watch-qual.ps1`
   (expect one `HEARTBEAT-STALE`, then ignore it), `active-chunk.json`, and the `run.lock` /
   native-lock pid 74508 liveness. End signal: `last-run.json` `completedAt` in `state-c9cb914c`.
   If 74508 dies without `completedAt`, that is a `run-lost` case for a Debug task. Restarting is
   the operator's call.
3. **Next launch: use the CARD-1667 repository launch set, not a private copy.** Copy
   `scripts/qualification/*` into the task directory and start the starter through
   `Win32_Process.Create`. Confirm `supervisor-identity.json` `consoleProcessIds` excludes the
   starter. The owed native witness for the console detach is still open. The launch brief for the
   next cut should say this explicitly, because two consecutive launch tasks (0297fa9c, c9cb914c)
   reused the private set.
4. **Optional, for the operator:** the lock-time coincidence can be tested next time by enabling
   process-termination auditing (skill note, human-only, elevated). Alternatively, avoid closing
   stray blank `pwsh` windows on the desktop while a run is live.

## Leftover processes (list only, nothing touched)

- No stray `testhost`, `vstest`, `Antiphon.*Tests` or old-run `checkout-*` processes outside the
  live tree.
- Not leftovers, by design: two `Antiphon.PtyHost.exe` under
  `checkout-c9cb914c\tests\Antiphon.E2E\TestOutput\runner\run-b0c58d00...` (279712 started
  03:33:54Z, 462308 started 03:34:15Z). These belong to the live run's isolated E2E runner, which
  detaches pty hosts.
- Session-runner pty hosts for live Claude sessions date from 10-26 onward:
  23776, 26716, 24896, 13308, 5580, 96, 12592, 31668, 188824, 296688, 444364, 84096 and 393156
  (this task). They are owned by the production runner 153560 (started 11-03 07:54Z), are normal,
  and are not old-run debris.
- The "three leftover idle child processes" from the brief were not found as old-run test
  processes.
