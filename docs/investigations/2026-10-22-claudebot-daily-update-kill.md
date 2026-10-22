# ClaudeBot Daily Update kills every desktop Claude Code session (CARD-1477)

- Date: 2026-10-22. Task 09570633 (Investigate), continuing Investigate c5666a30 (its scratch reused
  and re-verified) and Debug 99c08598 ([2026-10-22-desktop-session-deaths.md](2026-10-22-desktop-session-deaths.md)).
- Scope: read-only on the operator's tooling. Nothing in `C:\src\ClaudeBot` was edited, no
  scheduled task was changed, nothing was killed or restarted.
- Verdict: **Confirmed.** The 08:00 kill is deliberate code in the updater chain, it has run
  every day since 2026-09-30, and it is not needed to update the binary.

## Findings

1. **What runs it.** The Windows scheduled task `\ClaudeBot Daily Update` (read with
   `Get-ScheduledTask` / `Get-ScheduledTaskInfo`) is a daily trigger at 08:00 (StartBoundary
   `2026-08-09T08:00:00+01:00`), running as `lndco`, Interactive, RunLevel Limited. Action:
   `pwsh.exe -NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\src\ClaudeBot\scripts\daily-update.ps1"`.
   Last run 2026-10-22 08:00:00, result 0. Next run **2026-10-23 08:00:00**.
2. **The kill is an uncommitted edit.** `daily-update.ps1` step 2 calls
   `update-claude.ps1 -NoElevate` (live file, line 43). That call is not in ClaudeBot's history.
   `git -C C:\src\ClaudeBot status` shows ` M scripts/daily-update.ps1`. The committed version
   (`9296c6d6`, 2026-08-09) ran only `claude update`, labelled "non-disruptive update (does NOT
   kill running sessions)". The file's mtime is 2026-10-22 10:52:55. Its SHA-256 `EA81EB05…`
   matches the hash Debug 99c08598 recorded, so the 08:00 run used this content.
   `~/.daily-update.log` first shows "(all sessions restarted; ClaudeBot relaunched)" on
   **Wed 30 Sep 08:00**, and the same line appears on 23 run days up to Thu 22 Oct.
3. **The kill logic** is in `C:\src\ClaudeBot\scripts\update-claude.ps1` at ClaudeBot HEAD `deb21d5b`
   (the file matches HEAD; it was last changed in `de10b349`, 2026-09-25):
   - `:105-108` `Get-ClaudeCliProcs` matches **every** `Win32_Process` named `claude.exe` whose
     command line is not under `\WindowsApps\`. That excludes only the desktop app. It includes
     every Antiphon pty-host agent and every interactive CLI the operator runs.
   - `:110-118` `Stop-ClaudeCliProcs` calls `Stop-Process -Force` on each match. The pty-host
     sees exit code -1, which is the 13 exits within 3.3 s in the Debug doc.
   - The script has three kill sites. `:143-150`: if `claude update` fails, it kills everything
     and retries. `:158-160`: it then kills unconditionally. `:166-176`: for 90 s it re-kills any
     process whose real image is `*.old.*`, using `QueryFullProcessImageName`. That catches
     Antiphon's relaunches if they land on the old binary.
   - `:194-196` then relaunches the operator's remote-control session "ClaudeBot".
4. **The kill delivered nothing on 2026-10-22.** The renamed old binary
   `~/.local/bin/claude.exe.old.1792656136934.73928` (2.1.307) has LastWriteTime
   2026-10-21 23:32:29. So 2.1.307 was already installed before 08:00, and that morning's
   `claude update` had nothing new to install (reconstructed from file times; the updater's
   stdout is piped to `Out-Null`, so nothing records it). The 13 sessions were killed to "move"
   them from 2.1.307 to 2.1.307.
5. **The CLI updates itself on disk without killing anything.** The rename suffix decodes to
   epoch ms `1792656136934` = **2026-10-22 09:02:16 BST**. The PID in the suffix, `73928`, is the
   "ClaudeBot" remote-control session the 08:01 relaunch started. The new `claude.exe` (2.1.308,
   `claude --version`) was written at 09:02:10. Census at about 13:00, using the script's own
   `QueryFullProcessImageName` method:

   | PIDs | Parent | Real image | In runner `/sessions` |
   |---|---|---|---|
   | 10 agent sessions started 08:00-08:03 (Torquay, ClaudeBot-Antiphon, antiphon-check-interpreter, school-revision, Antiphon-Orchestrator, slides, Gym, markdown-package, antiphon-output-distiller, antiphon-diagnose) | `Antiphon.PtyHost.exe` (one host each) | `claude.exe.old.1792656136934.73928` (2.1.307) | yes, `status=Running`, `pid` equal |
   | 384064 (this task, started 12:52) | `Antiphon.PtyHost.exe` | `claude.exe` (2.1.308) | yes |
   | 73928 "ClaudeBot" `--remote-control` | `cmd.exe` < `WindowsTerminal.exe` | `.old` (2.1.307) | **no** |

   The CLI's own background auto-updater, running in session 73928, swapped the binary while
   12 processes were running. None of them died. New launches, such as this task, got 2.1.308.
   Killing running processes is not needed to keep the installed CLI current. The kill only
   forces processes that are already running onto the new build.
6. **How to tell Antiphon processes apart.** `GET http://localhost:17204/sessions` (runner, no
   `/api`) returns 71 rows: 11 `Running` and 60 `Exited`. Each Running row carries `pid` (the
   `claude.exe`) and `hostPid` (its `Antiphon.PtyHost.exe`). All 11 Running pids match the
   pty-host-parented `claude.exe` processes above, and the operator's interactive session
   (73928) is absent. The authoritative test is: pid in the runner's Running set. A secondary
   test is a parent image of `Antiphon.PtyHost.exe`. If the runner cannot be reached, a guard
   must treat ownership as unknown and must not kill. Standing AlwaysOn agents keep ten or more
   sessions Running at all times, so "defer while the runner reports any Running session" in
   practice means "never kill". The patch below says that directly, without a runner dependency.

## Proposed patch (recommended): `-PreserveSessions`, used by the unattended daily run

The patch is ready to apply:
[evidence/card-1477/claudebot-preserve-sessions.patch](evidence/card-1477/claudebot-preserve-sessions.patch).
It was built against the live working-tree files: `update-claude.ps1` at HEAD `deb21d5b`, and
`daily-update.ps1` as currently modified on disk. `git -C C:\src\ClaudeBot apply --check` passed
as a dry run that writes nothing. Behaviour:

- `update-claude.ps1 -PreserveSessions` updates on disk only. It never calls `Stop-Process`. A
  failed or locked update exits 2 with nothing stopped. It skips the stale-binary sweep. It
  relaunches the "ClaudeBot" remote session only when no `--remote-control ClaudeBot` process is
  already running.
- `daily-update.ps1` passes `-PreserveSessions` and reports deferred, failed or updated in the
  Telegram summary.
- Running `update-claude.ps1` by hand without the switch behaves exactly as before.

```diff
diff --git a/scripts/update-claude.ps1 b/scripts/update-claude.ps1
--- a/scripts/update-claude.ps1
+++ b/scripts/update-claude.ps1
@@ -50,6 +50,17 @@
     When set and -Name is not also given, the default session name becomes
     "ClaudeBot" rather than "ClaudeBot-Admin" (there is nothing "Admin" about it).
 
+.PARAMETER PreserveSessions
+    Never terminate a Claude Code CLI process. Updates the binary on disk only (the
+    native installer renames the locked exe to claude.exe.old.<ts>.<pid>, so running
+    sessions keep their current build and every NEW launch gets the new one). A locked
+    or failed update is deferred: the script exits 2 with nothing stopped. The stale-
+    binary sweep is skipped, and a replacement remote session is launched only when no
+    '--remote-control <Name>' session is already running. This is the mode for anything
+    unattended (daily-update.ps1): on 2026-10-22 the default mode force-stopped 13
+    Antiphon pty-host sessions, including two qualification monitors, and the dispatched
+    tasks failed without a report (Antiphon restarts AlwaysOn agents, not delegated tasks).
+
 .EXAMPLE
     pwsh -NoProfile -File C:\src\ClaudeBot\scripts\update-claude.ps1
 
@@ -67,7 +78,8 @@
     [string]$WorkDir = 'C:\src\ClaudeBot',
     [switch]$NoUpdate,
     [switch]$NoLaunch,
-    [switch]$NoElevate
+    [switch]$NoElevate,
+    [switch]$PreserveSessions
 )
 
 $ErrorActionPreference = 'Stop'
@@ -141,6 +153,11 @@
 }
 else {
     $ok = Invoke-ClaudeUpdate
+    if (-not $ok -and $PreserveSessions) {
+        $live = @(Get-ClaudeCliProcs)
+        Write-Warning ("Update did not apply with {0} live Claude Code CLI session(s) running; -PreserveSessions: deferred, nothing stopped. Retry later, or run without -PreserveSessions by hand." -f $live.Count)
+        exit 2
+    }
     if (-not $ok) {
         # Fallback for a locked exe (update_apply_exe_locked, npm EBUSY): free it and retry.
         # Antiphon may relaunch sessions in between -- step 3 cleans those up.
@@ -157,13 +174,16 @@
 # --- 2. Kill every running Claude Code CLI session -------------------------------
 $procs = Get-ClaudeCliProcs
 if (-not $procs) { Write-Host "No Claude Code CLI sessions running." -ForegroundColor Yellow }
+elseif ($PreserveSessions) {
+    Write-Host ("Preserving {0} live Claude Code CLI session(s) (-PreserveSessions); they keep their current build until they next restart: {1}" -f $procs.Count, ($procs.ProcessId -join ', ')) -ForegroundColor Yellow
+}
 else { Stop-ClaudeCliProcs $procs 'so they restart on the new binary' }
 
 # --- 3. Sweep sessions that came back on a stale binary --------------------------
 # Antiphon (PtyHost) relaunches its sessions ~5-40s after a kill. They should now
 # pick up the new exe; anything still running a renamed claude.exe.old.<ts> image
 # gets killed again so it relaunches correctly.
-if (-not $NoUpdate) {
+if (-not $NoUpdate -and -not $PreserveSessions) {
     $deadline = (Get-Date).AddSeconds(90)
     while ((Get-Date) -lt $deadline) {
         Start-Sleep -Seconds 10
@@ -183,6 +203,17 @@
 
 if (-not (Test-Path -LiteralPath $WorkDir)) { throw "WorkDir does not exist: $WorkDir" }
 
+# Nothing was killed, so the previous '<Name>' remote session is normally still up: do not
+# stack a second one. (Default mode killed it above, so it always relaunches there.)
+if ($PreserveSessions) {
+    $pattern = '--remote-control\s+"?' + [regex]::Escape($Name) + '"?(\s|$)'
+    $existing = @(Get-ClaudeCliProcs | Where-Object { $_.CommandLine -match $pattern })
+    if ($existing) {
+        Write-Host ("Remote session '{0}' already running (PID {1}); not launching another (-PreserveSessions)." -f $Name, ($existing.ProcessId -join ', ')) -ForegroundColor Yellow
+        return
+    }
+}
+
 # Reuse the committed remote-launch helper (single source of truth for how sessions start).
 $launcher = Join-Path $PSScriptRoot '..\claude-home\skills\launch-remote\launch-claude.ps1'
 if (-not (Test-Path -LiteralPath $launcher)) {
diff --git a/scripts/daily-update.ps1 b/scripts/daily-update.ps1
--- a/scripts/daily-update.ps1
+++ b/scripts/daily-update.ps1
@@ -5,12 +5,15 @@
 # "Schedule" note at the bottom). Idempotent + best-effort: a failure in one step
 # never blocks the others, and everything is logged.
 #
-# WARNING: this KILLS EVERY RUNNING CLAUDE CODE CLI SESSION on this machine as part
-# of the Claude Code update step (see step 2 below) -- runs unattended daily at 08:00,
-# so anything actively working in a Claude Code session at that time gets terminated.
-# Antiphon relaunches its own dispatched worker sessions automatically; an interactive
-# session (e.g. an orchestrator) does not self-revive and needs POST
-# /api/agents/{id}/start afterward if it must come back up.
+# The Claude Code step is NON-DISRUPTIVE: update-claude.ps1 -PreserveSessions updates the
+# binary on disk and never stops a running Claude Code CLI process. Running sessions keep
+# their current build (the native installer renames the locked exe to claude.exe.old.*)
+# and pick up the new one when they next restart; new launches get it immediately. A
+# locked update is deferred to the next run (exit 2); nothing is killed. The kill-and-
+# relaunch form is still available by hand: update-claude.ps1 without -PreserveSessions.
+# (2026-10-22: the previous unattended default force-stopped 13 Antiphon sessions at 08:00,
+# including two qualification monitors; Antiphon restarts AlwaysOn agents but delegated
+# tasks failed without a report. See Antiphon CARD-1477.)
 #
 # Telegram: reads the bot token from %USERPROFILE%\.antiphon-bot-token (chmod-equivalent
 # user-only file; never committed). Chat id is Mike's DM with the bot.
@@ -29,20 +32,20 @@
     else { Note "• browser-harness: updated" }
 } catch { Note ("• browser-harness: ERROR " + $_.Exception.Message) }
 
-# 2. Claude Code — full disruptive update: updates the CLI, then force-kills every
-#    running Claude Code CLI session so it restarts on the new binary, then relaunches
-#    a fresh non-elevated "ClaudeBot" remote-control session in C:\src\ClaudeBot.
-#    Delegates entirely to update-claude.ps1 -NoElevate, which already has the correct
-#    order-of-operations (update while sessions are still running so the native
-#    installer can rename the locked exe out from under them, kill, sweep for anything
-#    that raced back onto the old binary, relaunch) -- see that script's header for why.
+# 2. Claude Code — on-disk update only (update-claude.ps1 -NoElevate -PreserveSessions):
+#    no running session is stopped, no stale-binary sweep, and the "ClaudeBot" remote
+#    session is relaunched only if it is not already running. Exit 2 = update deferred
+#    (exe locked / updater failed) with nothing stopped; it retries at the next run.
 #    -NoElevate is required here: this runs unattended from a Scheduled Task, and
 #    -Verb RunAs would pop a UAC prompt that can never be answered.
 try {
     $updateScript = Join-Path $PSScriptRoot 'update-claude.ps1'
-    & $updateScript -NoElevate *>&1 | Out-Null
+    & $updateScript -NoElevate -PreserveSessions *>&1 | Out-Null
+    $rc = $LASTEXITCODE
     $ver = (& claude --version 2>&1 | Select-Object -Last 1)
-    Note ("• Claude Code: now " + $ver + " (all sessions restarted; ClaudeBot relaunched)")
+    if ($rc -eq 2)     { Note ("• Claude Code: update deferred (exe locked or updater failed); still " + $ver + " on disk; no sessions stopped") }
+    elseif ($rc -ne 0) { Note ("• Claude Code: updater exit " + $rc + "; " + $ver + " on disk; no sessions stopped") }
+    else               { Note ("• Claude Code: now " + $ver + " on disk (no sessions stopped; running sessions pick it up on their next restart)") }
 } catch { Note ("• Claude Code: ERROR " + $_.Exception.Message) }
 
 # 3. Todoist digest - overdue + due today, so the list can't rot unseen. (The old
```

### Witness: proves the patch without killing anything

```powershell
pwsh -NoProfile -NonInteractive -File docs\investigations\evidence\card-1477\run-witness.ps1
```

[run-witness.ps1](evidence/card-1477/run-witness.ps1) copies the **live**
`C:\src\ClaudeBot\scripts` updater files into a fresh `%TEMP%\card-1477-witness-*` layout and
applies the patch to the copy. It parse-checks both patched files, swaps in a stub launcher and
runs [test/harness.ps1](evidence/card-1477/test/harness.ps1). That harness runs each case in its
own `pwsh` process through [test/run-case.ps1](evidence/card-1477/test/run-case.ps1), where
`Stop-Process`, `Get-CimInstance`, `Start-Process`, `Start-Sleep`, `Get-Date`, `claude` and
`npm` are all functions. The runner refuses to start unless each name resolves to a mock, and
it refuses any case without `-NoElevate`. Fake PIDs (4000001+) are above any real Windows PID.

Result at 2026-10-22 ~13:10 BST: **7/7 PASS, exit 0, 21 s.** The ClaudeBot status was unchanged
afterwards, and no new `consent.exe` appeared.

| Case | Script | Stops | Exit | Launches |
|---|---|---|---|---|
| A current daily default (`-NoElevate`) | live | **4000001, 4000005** (the defect) | 0 | 0 |
| G current, locked update | live | **4000001, 4000005** (fallback kill) | 1 | 0 |
| B `-PreserveSessions` | patched | none | 0 | 0 |
| C `-PreserveSessions`, locked update | patched | none | **2** (deferred) | 0 |
| D `-PreserveSessions`, ClaudeBot RC already up | patched | none | 0 | 0 (no double launch) |
| E `-PreserveSessions`, ClaudeBot RC absent | patched | none | 0 | 1 |
| F manual mode (no switch) | patched | 4000001, 4000005 (unchanged behaviour) | 0 | 0 |

A and G are the red witnesses: the current scripts kill. B-E are the fix. F shows the manual
tool still behaves as before.

**Why the earlier harness hit `timeout` exit 124** (reconstructed from the c5666a30 transcript,
jsonl lines 269-293). The first harness passed the case arguments as one `[string[]]` value
through `pwsh -File`. That delivers a single literal string, so `update-claude.ps1` ran with its
defaults, including **no `-NoElevate`**. It took the elevated branch (`:199-204`,
`Start-Process -Verb RunAs`), which was not yet mocked. A real UAC consent prompt opened, the
case blocked on it, and the caller's `timeout 170` fired. There was also a `$Pid` read-only
variable bug. c5666a30 had already fixed the argument transport (`args=a|b`), mocked
`Start-Process` and renamed the variable, and its final runs passed 6/6. This task adds three
things: a hard per-case timeout (`-CaseTimeoutSec`, default 60, which kills only the harness's
own child), the `-NoElevate` refusal, and case G. It also reports the before/after real
`claude.exe` count instead of asserting it, because live sessions churn. A forced
`-CaseTimeoutSec 1` run fails all 7 cases in 10 s and exits 1, which shows a blocked case now
fails fast instead of hanging.

**The UAC prompt from that run is still open.** `consent.exe` PID 76404 was created at
2026-10-22 12:42:46, inside c5666a30's harness window, and its parent is the Appinfo service
host (1356). It was left alone because nothing may be killed. The operator should click **No**.

## Version gates and the safe update window

- Antiphon's Claude gates come from [agent-kinds.md](../agent-kinds.md): 2.1.280+ for `opus`
  and 2.1.284+ for `sonnet`. CARD-0959 records CLI observations but never refuses on version.
  The desktop runs 2.1.307 (running sessions) and 2.1.308 (on disk), both far above the gates.
  Leaving a running session on its launch build for its lifetime cannot break a gate, because
  a build only moves forward.
- **On-disk update** (`claude update`, or the CLI's own background auto-update): safe at any
  time, including 08:00, because it renames a locked exe rather than replacing it (finding 5).
  No window is needed.
- **Forcing running sessions onto a new build**: there is no idle window on this desktop. The
  runner always holds ten or more Running AlwaysOn sessions. If it is ever needed, use
  Antiphon's own restart path (`scripts/restart-session-runner.ps1` and the agent restart in
  [docs/apphost-runbook.md](../apphost-runbook.md)), at a time with no Code, Review, land or
  qualification task in flight. Do not use a blind `Stop-Process`. `-KillSessions` is human-only.
- server2 is unaffected. Its runner image pins the CLI by version and SHA-256
  (`docker/session-runner-grok/Dockerfile`), and this task is Windows-only.

## Decisions for the operator

1. **Apply the patch** (recommended; the default if nothing else is chosen). It keeps the daily
   on-disk update, the Telegram summary, the ClaudeBot relaunch-if-missing and the manual kill
   tool. From the Antiphon checkout (after this branch lands; before that, use the worktree path
   `C:\Antiphon\worktrees\card-task-09570633\`):
   `git -C C:\src\ClaudeBot apply C:\src\Antiphon\docs\investigations\evidence\card-1477\claudebot-preserve-sessions.patch`
   Then commit it in ClaudeBot. This also commits the step-2 change, which is uncommitted today.
2. **Or revert to the committed non-disruptive script.** That is
   `git -C C:\src\ClaudeBot checkout -- scripts/daily-update.ps1` (back to `9296c6d6`: on-disk
   `claude update` only). It is the smallest change, but it **discards the uncommitted edit**,
   including the 08:00 relaunch of the "ClaudeBot" remote session. Only the operator can choose
   to throw that away.
3. **Or stop the unattended Claude step entirely.** Remove step 2 from the daily task, or disable
   the task. The CLI's own auto-updater already keeps the disk current (finding 5).
4. **Not recommended: moving the schedule to an "idle" window.** No idle window exists while
   AlwaysOn agents run, so this only moves the kill.
5. **Deadline:** the next run is **2026-10-23 08:00 BST**. Without option 1, 2 or 3 before then,
   it will kill every desktop Claude session again.
6. **Housekeeping:** dismiss the stray UAC prompt (`consent.exe` 76404, click No).

## Remaining uncertainties

- Who made the uncommitted `daily-update.ps1` edit, and when. The log dates its behaviour to
  2026-09-30, and the file mtime is 2026-10-22 10:52:55. The 10:52 write did not change the
  08:00 content: the hash matches Debug's record, and the log line comes from this code path.
- That `claude update` was a no-op at 08:00 is inferred from file times. The updater's output is
  discarded (`| Out-Null`), so no record of it exists.
- `$LASTEXITCODE` after a successful `-PreserveSessions` run that relaunches ClaudeBot reflects
  the launcher's last native call. A non-zero value there would only change the summary wording
  to "updater exit N". It cannot cause a kill.
- The witness proves the scripts' control flow with mocks. It cannot prove how the real native
  installer behaves with a locked exe. That behaviour is observed (finding 5), not tested.

## Not done, noted

- Possible guard: an Antiphon-side check that refuses or alerts when an external process kills
  pty-host children (Debug's CARD-1478 covers the dispatch side). Not designed here.
