# 2026-11-02 rc qualification supervisor exit (run 7f8e9fd1)

Task 2949aafc, Investigate, read-only on the desktop. No process was touched; no build or test ran.

Subject: Windows rc qualification run `7f8e9fd119c3428a8bd46ac88e884645`, candidate
`rc-2026-11-01-10a826b9d61f`, launched 2026-11-02 13:13Z by task 8a7dbe6d. The run is still
going. Its observation supervisor is gone.

## Verdict

- **When:** both the supervisor `pwsh` (pid 347164, `supervise-rc.ps1`) and its wrapper `pwsh`
  (pid 413132, `start-supervisor.ps1`) ended between **22:09:07Z** (the last heartbeat) and about
  **22:09:45Z** (when the next 30 s heartbeat was due).
- **How:** both ended abruptly. Neither shut down cleanly and neither crashed with a report
  (evidence below). The run's own tree (127960 → 200108 → 430788 → 71760 → Antiphon.Tests)
  survived, so no tree kill was rooted at either supervisor process.
- **Who/why:** **not established.** There is no host evidence of the terminator. Process-exit
  auditing (Security 4689) is not readable without elevation, and Sysmon is not installed.
  Every other source checked is clean.
- **Impact on the run: none.** The supervisor only observed and recorded. Every result the run
  produces is written by `nightly-run.ps1` / `nightly-tests.ps1`, which have no dependency on it.
  At 23:02Z there were 93 TRX files and `antiphon-all-group-110` was active.

## Evidence

### Timing of the death

- `C:\Antiphon\releases\task-8a7dbe6d\supervisor-heartbeat.json` mtime 22:09:07.227Z. Content:
  `idleMinutes 0.0, trx 86, processes 7, hangSuspected false`. The loop rewrites this file every
  30 s plus scan time (`supervise-rc.ps1`, `while (-not $proc.HasExited)` loop: `Start-Sleep 30`,
  then the heartbeat write).
- `supervisor-console.log` is **exactly 8192 bytes**. Its mtime is 21:53:36.80Z. It ends in
  `...newest=own-47fcde14e1cba-...txt\r\nPR`. The 21:43:19 PROGRESS line is the last complete
  one; `PR` is the first two bytes of the 21:53:36 PROGRESS line.
  - A size of exactly 2×4096 bytes means a block-buffered writer: `start-supervisor.ps1` redirects
    the child's stdout with `1> supervisor-console.log`, and wrapper 413132 copies it through a
    4 KiB buffered file stream. The file only grows when that buffer fills.
  - So the 21:53:36 mtime and the truncated line are **buffering artefacts**. They do not show
    when the process died.
  - The rest of the 21:53:36 line and the ~22:03:5x PROGRESS line were still in 413132's buffer.
    They were lost because **413132 never flushed and disposed its stream**, so it did not exit
    cleanly.
- `supervisor-stderr.log` is 0 bytes, mtime 13:13:39Z. Suppose `supervise-rc.ps1`
  (`$ErrorActionPreference='Stop'`) had died on a terminating script error, such as a failed
  `Get-CimInstance Win32_Process` in `Get-DescendantIds`. Then pwsh would have written the error
  record to stderr, and 413132 would have exited normally, flushing both files. Neither happened.
- 347164 cannot have outlived 413132 by long. It writes stdout only on PROGRESS. The next
  PROGRESS write was due at about 22:13:53, and a broken pipe would only have stopped it then. Its
  heartbeats (file writes inside `try { } catch { }`) stopped at 22:09:07 instead. So both
  processes died in the same ~40 s window.

### Process topology (from `detach-proof.txt`, 13:13:47Z)

```
413132 pwsh start-supervisor.ps1   parent WmiPrvSE 4152 (Win32_Process.Create), inJob=False
  347164 pwsh supervise-rc.ps1     inJob=False
    127960 pwsh launch-rc.ps1      inJob=False  (Start-Process -WindowStyle Hidden => own console 165152)
      200108 pwsh build-slot.ps1 -> 430788 pwsh nightly-run.ps1
  133548 conhost.exe               console shared by 413132 and 347164
```

Live at 22:55Z: 127960, 165152, 200108, 430788, 71760 (nightly-tests `-EncodedCommand` hop),
Antiphon.Tests 58720, and WmiPrvSE 4152 (alive since 10-26). Gone: 413132, 347164, 133548.

- `taskkill /T` and `Process.Kill(entireProcessTree: true)` both enumerate children by parent pid
  at kill time. Either one, rooted at 413132 or 347164, would also have killed 127960 and its
  descendants. They are alive, so a tree kill at either root is ruled out.
- Two explanations fit a single cause that leaves 127960 standing:
  1. Teardown of console 133548. Killing that conhost sends CTRL_CLOSE to every attached process
     and terminates it within about 5 s. That fits both deaths, the lack of a flush, and the
     survival of 127960, which has its own console.
  2. Two separate per-pid terminations.

  Neither is proven.
- No Job Object was involved: detach-proof shows `IsProcessInJob=False` for the whole tree. The
  WMI host that created 413132 is still alive. Session 1 is still active, with the run's
  processes in it.

### Host sources checked (window 21:50–22:30Z; local TZ is GMT, so local time = UTC)

| Source | Finding |
|---|---|
| Application 1000/1001/1026 (Application Error, WER, .NET Runtime crash) | No pwsh entry. The only crash since 13:00Z is `Antiphon.Messaging.FakeGateway.exe` c0000005 at 18:40Z, which is unrelated. The `.NET Runtime` 1000 rows every ~6 min are Antiphon gateway `[inbound-unconsumed] group_absent` log events, not crashes. |
| System | Volsnap 36 at 22:07:21: shadow copies of C: aborted because the storage limit was reached. This is an I/O-side event and no process termination follows from it. Otherwise only Kernel-General/Time-Service noise. No Kernel-Power and no service-control termination. |
| Resource-Exhaustion-Detector (2004) | No events. Free memory was 7104 MiB at 22:55Z, of 32709 MiB total. |
| TaskScheduler/Operational | No events in the window. |
| WMI-Activity | Routine provider loads only (ScheduledTaskProv/nettcpip on 4152, WMIProv on 236648 at 22:09:44). No Win32_Process Terminate is recorded; that log does not record method calls. |
| Windows PowerShell/Operational | 22:09:03 `powershell -NoProfile -Command Get-ChildItem C:\Antiphon ... -Filter summary.json`, a read-only search by some other session. 5.1 console starts at 22:07:15, 22:07:43 and 22:09:11 (pid 145448) carry no script text. 22:13:17 and 22:14:23 are `Add-Type ... echo-args.cs` test fixtures. Nothing mentions Stop-Process, taskkill, 347164 or 413132. pwsh 7 does not log to this channel, and PowerShellCore/Operational is empty. |
| Security 4688/4689 | Not readable without elevation; the audit policy is unknown. |
| AppHost / runner | dcpctrl on 17202 has run since 2026-10-29 and SessionRunner 7996 on 17204 since 2026-10-26, so neither restarted. `session-runner-20261102.log` 22:07–22:11 has 229 lines: all 404 session-not-found noise plus one transcript-candidate warning, with no kill, reap or stop. `watchdog-apphost.log` shows routine `fire beginning` lines only (22:06:24, 22:06:33, 22:09:20, 22:10:28, 22:12:24). |
| Claude transcripts on this host (13 touched since 21:30Z) | No tool call between 22:03 and 22:12Z contains Stop-Process, taskkill, Kill, Terminate or a restart script. The only references to 347164/413132 are this task and the orchestrator's earlier heartbeat reads. |
| Test activity at death | Group `antiphon-all-group-103` (Antiphon.Tests Checkpoints, 21:34:41–22:14:32) was running `CheckpointTrackedBaselineTests.tracked_snapshot_contains_committed_files` (22:08:58–22:09:04) and `staged_untracked_file_is_kept_and_indexed` (22:09:04–22:09:53). Both drive the checkpoint tool and `run-checkpoint.ps1` against a temp repo. The tool's only foreign-process kill path, `tools/Antiphon.Checkpoints/Cleanup/ProcessControl.cs:10-17`, is identity-verified and kills the whole tree, which would have taken 127960 too. The repo has no `GenerateConsoleCtrlEvent`/`AttachConsole`/`FreeConsole` call in `*.cs`/`*.ps1`. The test overlap is coincidence until shown otherwise. |

## What the supervisor was responsible for, and what is now unattended

From `C:\Antiphon\releases\task-8a7dbe6d\supervise-rc.ps1`, a task-local script. It is not in the
repo; `launch-record.json` `launcherProvenance` traces it as a copy of task-78b36185's launch set,
itself from task-4dc48523, task-136debc0 and task-250a07fb.

1. **Pre-launch checks**: ownership markers, SHA, remote ref, a clean tree, the memory floor and
   no leftover testhosts. These were done at 13:13:41 (`PRECHECK PASS`).
2. **Launch** of `launch-rc.ps1` via `Start-Process -RedirectStandardOutput commission-console.log
   -RedirectStandardError commission-stderr.log`. These are file handles inherited by the child,
   not pipes to the supervisor, so the child's output is unaffected by the supervisor's death.
3. **Heartbeat** `supervisor-heartbeat.json` every ~30 s: tree CPU, bytes under logRoot and
   stateRoot, TRX counts, newest file. **Lost.**
4. **Hang detection**: after 20 minutes with no CPU, byte or mtime growth it writes
   `hang-suspected.txt` and a `HANG_SUSPECTED` line. **Observation only: it never kills, signals
   or restarts anything.** Lost.
5. **PROGRESS lines** every 10 min in `supervisor-console.log`. Lost.
6. **Final record**: after `WaitForExit` it writes `EXIT <code>` and
   `C:\Antiphon\releases\task-8a7dbe6d\commission-exit.txt` with launch-rc's exit code. **This
   file will never appear.** `observe.ps1` and anything else that waits for it will wait forever,
   and `observe.ps1` will report `alive=False` from now on.

The supervisor did **not** own reaping, cleanup, lock release, the summary, the report, the green
credit or the seed/failures export. The seed in use (`C:\Users\lndco\antiphon-seeds\aa64a906.json`)
was produced by the task-local `seed-*` scripts before launch.

## Will the run finish by itself, and what it writes

Yes. Nothing downstream depends on its parent:

- `launch-rc.ps1` runs `build-slot.ps1 ... -- pwsh nightly-run.ps1 ...` and then `exit
  $LASTEXITCODE`. Its stdout and stderr are files.
- `scripts/nightly-run.ps1` (checkout-8a7dbe6d) → `scripts/lib/nightly-run-impl.ps1`. There is
  no reference to a parent pid, heartbeat or supervisor in `nightly-run.ps1`, `nightly-tests.ps1`
  or `build-slot.ps1`.
- `Invoke-NightlyWatchedCommand` (`nightly-run-impl.ps1:107`) uses `Start-Process -NoNewWindow
  -Wait` with no timeout and no idle kill. Per-group timeouts are the hop's own.
- At the end, `nightly-run-impl.ps1` lines ~701-845:
  - reads `<logDir>\summary.json`, written by nightly-tests, and rewrites it if the test exit is
    non-zero;
  - runs `nightly-report.ps1 -Summary ...`, since `-NoReport` was not passed;
  - writes the state `C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f\state-8a7dbe6d\last-run.json`
    with `phase=final`, `completedAt`, `exitCode`, `testsPassed`, `coverageComplete`,
    `reportDelivered` and `summaryPath`;
  - on rc green, writes the rc credit file in the candidate store (`Get-ReleaseGateStatePaths
    -CreditKind rc-release`);
  - in `finally`, runs `Exit-ReleaseGateNativeLock` (`C:\Antiphon\verification\native-run.lock`)
    and `Exit-NightlyExclusiveLock` (`state-8a7dbe6d\run.lock`).
- The TRX files, the timing journal under `logs\timing-7f8e9fd1...` and summary.json all land
  normally. **The only artefact lost is `commission-exit.txt`, plus the heartbeat stream.** The
  launch-rc exit code equals the `exitCode` that nightly-run writes to `last-run.json`, unless
  build-slot itself fails, in which case it would only show in `commission-console.log`.

## Least risky way to keep watching

Never treat `commission-exit.txt` or the supervisor heartbeat as signals again for this run. Use
read-only polling of state the run itself writes. A watcher that only READS, for an orchestrator's
Monitor loop. It writes nothing, signals nothing, and never runs under the run's tree:

```powershell
# watch-7f8e9fd1-readonly.ps1 : prints one JSON line per poll; exits 0 at completion, 2 on hang suspicion
$ErrorActionPreference = 'Continue'
$state  = 'C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f\state-8a7dbe6d\last-run.json'
$logDir = 'C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f\logs'
$nightlyPid = 430788; $nightlyStart = '2026-11-02T13:13:44'   # identity check guards pid reuse
$hangMinutes = 30                                                # supervisor used 20 by mtime/CPU/bytes
while ($true) {
    $s = $null; try { $s = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json } catch { }
    $p = Get-Process -Id $nightlyPid -ErrorAction SilentlyContinue
    $alive = $p -and $p.StartTime.ToUniversalTime().ToString('s') -eq $nightlyStart
    $files = @(Get-ChildItem -LiteralPath $logDir -Recurse -File -ErrorAction SilentlyContinue)
    $newest = $files | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $idle = if ($newest) { [math]::Round(([datetime]::UtcNow - $newest.LastWriteTimeUtc).TotalMinutes, 1) } else { -1 }
    [ordered]@{ utc=[datetime]::UtcNow.ToString('o'); phase=$s.phase; completedAt=$s.completedAt; exitCode=$s.exitCode
        nightlyAlive=[bool]$alive; trx=@($files | Where-Object Extension -eq '.trx').Count; newest=$newest.Name; idleMin=$idle } | ConvertTo-Json -Compress
    if ($s.completedAt) { exit 0 }
    if (-not $alive) { Write-Output 'NIGHTLY_GONE_WITHOUT_FINAL_STATE'; exit 3 }
    if ($idle -ge $hangMinutes) { Write-Output 'HANG_SUSPECTED (observe only; do not kill)'; exit 2 }
    Start-Sleep -Seconds 120
}
```

Run it from a session's own Monitor or loop, in the foreground. Do not start another detached
process for it. If you start one with `Win32_Process.Create`, its output must go to a file opened
per write (`Add-Content`), never through a `1> file` wrapper. The task-local `status.ps1`
(`rc-qualification-status.ps1`) is also a read-only option for a final verdict.

## Uncertainties

- The terminating agent is unknown. Console teardown of conhost 133548 is the single-cause
  mechanism that fits every observation, but there is no record proving it.
- Process-exit auditing (`auditpol /get /subcategory:"Process Termination"`, run elevated) or
  Sysmon EventID 5 would settle it next time. Neither is available now.
- The overlap with Checkpoints group 103 tests (22:09:04–22:09:53) has no causal link.

## Not done, noted

- The fix belongs on a card. Commit the qualification supervisor and launcher set to the repo
  (CARD-1606 noted that the heartbeat writer is not in the repo). Make it write line-flushed
  `Add-Content` logs, not a `1>` wrapper, give it its own console, and let observers treat a
  stale heartbeat as "observer lost", not "run lost". Optionally enable Process Termination
  auditing on the desktop.
