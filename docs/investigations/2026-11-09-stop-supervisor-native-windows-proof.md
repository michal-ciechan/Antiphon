# CARD-1507 native Windows proof of `scripts/qualification/stop-supervisor.ps1`

Task a5309b32, 2026-11-09 19:54Z-20:00Z, desktop (Windows 10 Pro 10.0.19045).
Subject: master `28ef6675b4a81003a13bc05f4b94c494d98064ff` (Reviews 1c5142f8, 3169ab97).
Shells: Windows PowerShell 5.1.19041.6456 (`powershell.exe -NoProfile -File`) and
PowerShell 7.6.9 (`C:\Program Files\PowerShell\7\pwsh.exe -NoProfile -File`).

**Result: every case behaved per the script contract under both shells. No 5.1 syntax or
encoding failure. Nothing outside the private sleeper tree was touched.**

## Live-run guard

The live rc qualification run dce78ee0 was recorded before anything ran and re-checked by the
harness before and after every case (pid + start time; any change aborts the harness):

| pid | role | parent | started (local) |
|---|---|---|---|
| 70136 | supervisor (`supervise-rc.ps1 ... -RunId dce78ee0a35a...`) | 195884 | 2026-11-09 13:06:18 |
| 314140 | driver (`nightly-run.ps1 ... -Profile rc`) | 391060 | 2026-11-09 13:06:23 |

Newest TRX before: `antiphon-all-profile-175.trx` 19:54:25Z. After the last case:
`antiphon-all-profile-190.trx` 20:00:06Z (run kept advancing). Both pids alive with unchanged
start times at the end. `C:\Antiphon\verification\native-run.lock` (live lock, read once for its
record shape) unchanged: mtime 13:06:24, 183 bytes. No invocation named
`C:\Antiphon\verification` or any live pid; the harness throws if a record names a pid that is
not in its own ledger or is a live-run pid.

## Sleeper tree and records

Private root `C:\Antiphon\releases\task-a5309b32\sleeper\<shell>-<case>\{task,coord}`; fresh
sleepers per case; every pid started is appended to
`C:\Antiphon\releases\task-a5309b32\pid-ledger.txt` (50 entries) with its exact start time.

- Supervisor stand-in: hidden `pwsh -NoProfile -NonInteractive -EncodedCommand <...>` which
  itself runs `Start-Process pwsh -ArgumentList -NoProfile,-NonInteractive,-Command,'Start-Sleep 3600'
  -WindowStyle Hidden -PassThru` (the driver stand-in, a DIRECT child, as supervise-rc.ps1 does),
  writes the child pid to a file, then sleeps 3600. The harness verifies via `Win32_Process`
  that the driver's `ParentProcessId` is the supervisor stand-in.
- `task\supervisor-identity.json` and `task\launch-child.json`:
  `{"pid":N,"processStartedAt":"<StartTime UTC 'o'>","runId":"a5309b32-<shell>-<case>"}`
  (the supervise-rc.ps1 shape; identity has no commandLine except in case 5c).
- `coord\native-run.lock`: `{"pid","processStartedAt","runId","lane":"rc","continuationId":"","acquiredAt"}`
  (the live lock's shape). A "dead owner" is a `pwsh -Command "exit 0"` process the harness
  started and waited for.
- Invocation: `System.Diagnostics.ProcessStartInfo.ArgumentList` (exact argv, no shell
  quoting) of `<exe> -NoProfile -File <worktree>\scripts\qualification\stop-supervisor.ps1
  -TaskRoot <task> -CoordinationRoot <coord> -RunId <runId> -Reason <reason> [-Confirm]`.
  Execution policy on this host: CurrentUser Unrestricted, Process Bypass (inherited); no
  `-ExecutionPolicy` argument was passed.

Each tree also contains the `conhost.exe` children Windows attaches to hidden consoles; taskkill
/T reports and kills them as part of the tree (visible in the cleanup output below).

## Results (identical behaviour under 5.1 and 7)

| case | setup | exit 5.1 / 7 | kills (taskkill calls) | sleepers after | lock | supervisor.log |
|---|---|---|---|---|---|---|
| 1 dry run (no -Confirm) | live sup+child drv, dead lock owner | 2 / 2 | none | both alive | unchanged (same SHA-256) | not created |
| 2 -Confirm, dead lock owner | live sup+child drv | 0 / 0 | 1: `supervisor` only, ok, reason `stopped` | both gone | moved to `task\stop-evidence\native-run.lock.moved-<stamp>`, SHA-256 identical, original absent | 1 `STOP ... outcome=stopped lock=moved killed=supervisor:<pid>` |
| 3 -Confirm, LIVE lock owner (3rd sleeper) | live sup+child drv | 4 / 4 | 1: `supervisor`, ok | sup+drv gone, lock owner alive | kept, SHA-256 unchanged, `lockAction=retained-alive` | `outcome=lock-retained lock=retained-alive killed=supervisor:<pid>` |
| 4a both records dead | sup + drv records = exited pids, no lock | 0 / 0 | none (`kills:[]`) | n/a | `absent` | `outcome=stopped lock=absent` |
| 4b supervisor record dead | drv = live standalone sleeper, no lock | 0 / 0 | 1: `driver`, ok | driver gone | `absent` | `killed=driver:<pid>` |
| 5a supervisor start time -1 s | live tree | 2 / 2 | none | both alive | not attempted | not created; `detail=supervisor-reused` |
| 5b driver start time -1 ms | live tree | 2 / 2 | none | both alive | not attempted | not created; `detail=driver-reused` |
| 5c supervisor `commandLine` wrong | live tree | 2 / 2 | none | both alive | not attempted | not created; `detail=supervisor-reused` (command-mismatch maps to reused) |
| 6 reason `halt "now" path C:\tmp\x\ end` | live tree, no lock | 0 / 0 | 1: `supervisor`, ok | both gone | `absent` | `STOP reason="halt \"now\" path C:\\tmp\\x\\ end" ...` |
| 8 driver outlived tree (separate trees) | sup + drv independent sleepers | 0 / 0 | 2: `supervisor`, then `driver`, both ok | both gone | `absent` | `killed=supervisor:<pid>,driver:<pid>` |

stderr of every stop-supervisor.ps1 invocation was empty. Durations 0.9-2.3 s.

Case 3 contract note: the supervisor tree is killed before the lock is examined, so with a live
lock owner the run's processes ARE stopped, the lock is kept, and the exit is 4 (`lock-retained`).
The lock owner itself is never a kill target.

Case 6: the JSON result parses back to `detail = STOP reason="halt \"now\" path C:\\tmp\\x\\ end"
outcome=stopped lock=absent killed=supervisor:<pid>` (quote -> `\"`, backslash -> `\\`), and the
raw stdout is valid JSON (`ConvertFrom-Json` succeeded) under both shells. The argv value
arrived intact (trailing backslash before a space included).

### Raw stdout, representative (pwsh 7)

Case 1:

```
stop-supervisor: dry-run; -Confirm is required; no process was stopped
planned kill phase=supervisor pid=465036 processStartedAt=2026-11-09T19:57:23.5885166Z commandLine=absent
planned kill phase=driver pid=222296 processStartedAt=2026-11-09T19:57:25.0975770Z commandLine=absent
planned lock ownerPid=299524 runId=a5309b32-pwsh7-c1-dryrun action=move-if-owner-dead
planned runId=a5309b32-pwsh7-c1-dryrun
```

(5.1 prints the identical shape; `processStartedAt` stays the recorded string there.)

Case 2:

```
{"exitCode":0,"outcome":"stopped","detail":"STOP reason=\"native proof a5309b32 c2-deadlock\" outcome=stopped lock=moved killed=supervisor:461860","kills":[{"phase":"supervisor","pid":461860,"ok":true,"reason":"stopped","command":"taskkill.exe /PID 461860 /T /F"}],"lockAction":"moved","retainedLockPath":"C:\\Antiphon\\releases\\task-a5309b32\\sleeper\\pwsh7-c2-deadlock\\task\\stop-evidence\\native-run.lock.moved-20261109T195744906Z","logPath":"C:\\Antiphon\\releases\\task-a5309b32\\sleeper\\pwsh7-c2-deadlock\\task\\supervisor.log"}
```

Case 3:

```
{"exitCode":4,"outcome":"lock-retained","detail":"STOP reason=\"native proof a5309b32 c3-livelock\" outcome=lock-retained lock=retained-alive killed=supervisor:367932","kills":[{"phase":"supervisor","pid":367932,"ok":true,"reason":"stopped","command":"taskkill.exe /PID 367932 /T /F"}],"lockAction":"retained-alive","retainedLockPath":null,"logPath":"C:\\Antiphon\\releases\\task-a5309b32\\sleeper\\pwsh7-c3-livelock\\task\\supervisor.log"}
```

Case 5a:

```
{"exitCode":2,"outcome":"refused","detail":"supervisor-reused","kills":[],"lockAction":"not-attempted","retainedLockPath":null,"logPath":null}
```

## Case 7: real taskkill exit codes and stderr (advisory A-4)

Inside stop-supervisor.ps1 every real `taskkill.exe /PID <pid> /T /F` (12 calls: cases 2, 3, 4b,
6 x2 shells, case 8 x2 calls x2 shells) exited 0 (`reason=stopped`). The script redirects but
does not read taskkill's output, so stdout/stderr were captured by running the same command
directly against private targets:

| target | exit | stdout | stderr |
|---|---|---|---|
| live supervisor stand-in with child driver (cleanup of case 1/5) | 0 | `SUCCESS: The process with PID <conhost> (child process of PID <drv>) has been terminated.` / `... PID <conhost> (child process of PID <sup>) ...` / `... PID <drv> (child process of PID <sup>) ...` / `... PID <sup> (child process of PID <harness>) ...` | empty |
| live single sleeper (case 3 lock owner) | 0 | 2 SUCCESS lines (its conhost, then itself) | empty |
| already-exited pid, handle held so the pid cannot be reused (5.1 and 7) | **128** | empty | `ERROR: The process "<pid>" not found.` |

The script's own `Invoke-QualificationWindowsTreeStop` on that exited pid returns
`ok=False reason=taskkill-exit-128` (both shells); on a live sleeper `ok=True reason=stopped`;
on its own pid `ok=False reason=refused-pid` without starting taskkill.

A-4 observation: a recorded process that exits in the window between the stop's re-observation
(`$again`, alive) and its taskkill gets exit 128, which the script records as a failed kill and
reports `outcome=incomplete`, exit 3, even though the target is gone. Not reproduced inside a
stop run (the window is milliseconds); it is a conservative outcome (no false "stopped"), so it
is reported, not a misbehaviour. A caller rerunning the stop would then see the pid absent and
complete.

## Cleanup

Leftovers were killed only by ledger pid after checking the start time still matched
(`C:\Antiphon\releases\task-a5309b32\cleanup.ps1`). Final sweep over all 50 ledger entries:
every pid absent; zero processes whose parent is a ledger pid. Per-case result JSON:
`C:\Antiphon\releases\task-a5309b32\sleeper\<shell>-<case>\result.json`.

## Reproduce

```
pwsh -NoProfile -File C:\Antiphon\releases\task-a5309b32\harness.ps1 -Case <c1-dryrun|c2-deadlock|c3-livelock|c4a-alldead|c4b-supdead|c5a-supmismatch|c5b-drvmismatch|c5c-cmdmismatch|c6-quote|c8-outlived> -Shell <ps51|pwsh7>
pwsh -NoProfile -File C:\Antiphon\releases\task-a5309b32\cleanup.ps1 -Tag <shell>/<case>
powershell.exe -NoProfile -File C:\Antiphon\releases\task-a5309b32\c7.ps1
```

The harness refuses to reuse an existing case directory; delete it (or rename the case) first.
Source of the three scripts follows.

### harness.ps1

```powershell
#requires -Version 7
# Task a5309b32: native Windows proof of scripts/qualification/stop-supervisor.ps1 (CARD-1507).
# Private sleeper tree only. Never targets the live run. ASCII-only.
param(
    [Parameter(Mandatory)][string]$Case,
    [Parameter(Mandatory)][ValidateSet('ps51', 'pwsh7')][string]$Shell
)
$ErrorActionPreference = 'Stop'
$Root = 'C:\Antiphon\releases\task-a5309b32\sleeper'
$Ledger = 'C:\Antiphon\releases\task-a5309b32\pid-ledger.txt'
$StopScript = 'C:\Antiphon\worktrees\card-task-a5309b32\scripts\qualification\stop-supervisor.ps1'
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$Ps51 = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
$LivePids = @{ 70136 = [datetime]::Parse('2026-11-09T13:06:18Z').ToUniversalTime(); 314140 = [datetime]::Parse('2026-11-09T13:06:23.3827196Z').ToUniversalTime() }
$utf8 = New-Object System.Text.UTF8Encoding $false
$mine = New-Object System.Collections.Generic.List[int]

function Assert-Live {
    param([string]$When)
    foreach ($id in $LivePids.Keys) {
        $p = Get-Process -Id $id -ErrorAction SilentlyContinue
        if (-not $p) { throw "LIVE-RUN pid $id NOT ALIVE at $When" }
        $st = $p.StartTime.ToUniversalTime()
        if ([Math]::Abs(($st - $LivePids[$id]).TotalSeconds) -gt 1) { throw "LIVE-RUN pid $id start changed at $When ($($st.ToString('o')))" }
    }
}
function Get-Identity([int]$ProcessId) {
    $p = Get-Process -Id $ProcessId
    $r = [ordered]@{ pid = $p.Id; processStartedAt = $p.StartTime.ToUniversalTime().ToString('o') }
    $p.Dispose()
    return $r
}
function Add-Ledger([int]$ProcessId, [string]$Role) {
    $id = Get-Identity $ProcessId
    [IO.File]::AppendAllText($Ledger, ("{0} {1} {2} {3}/{4}`r`n" -f $id.pid, $id.processStartedAt, $Role, $Shell, $Case), $utf8)
    $script:mine.Add($ProcessId)
    return $id
}
function Start-Sleeper([string]$Role) {
    $p = Start-Process -FilePath $Pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep 3600' -WindowStyle Hidden -PassThru
    return (Add-Ledger $p.Id $Role)
}
# Supervisor stand-in: a hidden sleeping pwsh that starts the driver stand-in as its DIRECT child.
function Start-SleeperTree([string]$Dir) {
    $childFile = Join-Path $Dir 'driver-pid.txt'
    $inner = "`$c = Start-Process -FilePath '$Pwsh' -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep 3600' -WindowStyle Hidden -PassThru; [IO.File]::WriteAllText('$childFile', [string]`$c.Id); Start-Sleep 3600"
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($inner))
    $sup = Start-Process -FilePath $Pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-EncodedCommand', $enc -WindowStyle Hidden -PassThru
    $supId = Add-Ledger $sup.Id 'supervisor'
    $deadline = (Get-Date).AddSeconds(30)
    while (-not [IO.File]::Exists($childFile)) { if ((Get-Date) -gt $deadline) { throw 'driver pid file timeout' }; Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 200
    $drvPid = [int]([IO.File]::ReadAllText($childFile).Trim())
    $drvId = Add-Ledger $drvPid 'driver'
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$drvPid"
    if ([int]$cim.ParentProcessId -ne $sup.Id) { throw "driver $drvPid parent $($cim.ParentProcessId) is not supervisor $($sup.Id)" }
    return [pscustomobject]@{ Supervisor = $supId; Driver = $drvId }
}
function New-ExitedIdentity {
    $p = Start-Process -FilePath $Pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', 'exit 0' -WindowStyle Hidden -PassThru
    $null = $p.Handle
    $st = $p.StartTime.ToUniversalTime().ToString('o')
    $p.WaitForExit()
    $r = [ordered]@{ pid = $p.Id; processStartedAt = $st }
    [IO.File]::AppendAllText($Ledger, ("{0} {1} exited-owner {2}/{3}`r`n" -f $p.Id, $st, $Shell, $Case), $utf8)
    return $r
}
function Write-Json([string]$Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Compress), $utf8) }
function Test-Alive($Identity) {
    $p = Get-Process -Id $Identity.pid -ErrorAction SilentlyContinue
    if (-not $p) { return $false }
    return ($p.StartTime.ToUniversalTime().ToString('o') -ceq $Identity.processStartedAt)
}
function Invoke-Stop([string]$Task, [string]$Coord, [string]$RunId, [string]$Reason, [switch]$Confirm) {
    $exe = $Pwsh; if ($Shell -ceq 'ps51') { $exe = $Ps51 }
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $exe
    foreach ($a in @('-NoProfile', '-File', $StopScript, '-TaskRoot', $Task, '-CoordinationRoot', $Coord, '-RunId', $RunId, '-Reason', $Reason)) { $info.ArgumentList.Add($a) }
    if ($Confirm) { $info.ArgumentList.Add('-Confirm') }
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($info)
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit(120000)) { throw 'stop script timeout' }
    $p.WaitForExit()
    $r = [pscustomobject]@{ exe = $exe; args = ($info.ArgumentList -join ' | '); exit = $p.ExitCode; stdout = $outTask.Result.Trim(); stderr = $errTask.Result.Trim(); ms = $sw.ElapsedMilliseconds }
    $p.Dispose()
    return $r
}
function Get-Log([string]$Task) { $l = Join-Path $Task 'supervisor.log'; if ([IO.File]::Exists($l)) { return [IO.File]::ReadAllText($l).Trim() }; return '<none>' }

$dir = Join-Path $Root ($Shell + '-' + $Case)
if ([IO.Directory]::Exists($dir)) { throw "case dir exists: $dir" }
$task = Join-Path $dir 'task'; $coord = Join-Path $dir 'coord'
[void][IO.Directory]::CreateDirectory($task); [void][IO.Directory]::CreateDirectory($coord)
$runId = 'a5309b32-' + $Shell + '-' + $Case
$reason = 'native proof a5309b32 ' + $Case
$out = [ordered]@{ case = $Case; shell = $Shell; runId = $runId; taskRoot = $task; coordRoot = $coord }
Assert-Live "before $Shell/$Case"

$tree = $null; $extra = $null; $lockRec = $null
switch ($Case) {
    'c1-dryrun' {
        $tree = Start-SleeperTree $dir
        $lockRec = New-ExitedIdentity
    }
    'c2-deadlock' {
        $tree = Start-SleeperTree $dir
        $lockRec = New-ExitedIdentity
    }
    'c3-livelock' {
        $tree = Start-SleeperTree $dir
        $extra = Start-Sleeper 'lock-owner'
        $lockRec = [ordered]@{ pid = $extra.pid; processStartedAt = $extra.processStartedAt }
    }
    'c4a-alldead' {
        $tree = [pscustomobject]@{ Supervisor = (New-ExitedIdentity); Driver = (New-ExitedIdentity) }
    }
    'c4b-supdead' {
        $tree = [pscustomobject]@{ Supervisor = (New-ExitedIdentity); Driver = (Start-Sleeper 'driver-alone') }
    }
    'c5a-supmismatch' { $tree = Start-SleeperTree $dir }
    'c5b-drvmismatch' { $tree = Start-SleeperTree $dir }
    'c5c-cmdmismatch' { $tree = Start-SleeperTree $dir }
    'c6-quote' {
        $tree = Start-SleeperTree $dir
        $reason = 'halt "now" path C:\tmp\x\ end'
    }
    'c8-outlived' {
        $tree = [pscustomobject]@{ Supervisor = (Start-Sleeper 'supervisor-solo'); Driver = (Start-Sleeper 'driver-separate-tree') }
    }
    default { throw "unknown case $Case" }
}
$sup = [ordered]@{ pid = $tree.Supervisor.pid; processStartedAt = $tree.Supervisor.processStartedAt; runId = $runId }
$drv = [ordered]@{ pid = $tree.Driver.pid; processStartedAt = $tree.Driver.processStartedAt; runId = $runId }
if ($Case -ceq 'c5a-supmismatch') { $sup.processStartedAt = ([datetime]::Parse($sup.processStartedAt).ToUniversalTime().AddSeconds(-1)).ToString('o') }
if ($Case -ceq 'c5b-drvmismatch') { $drv.processStartedAt = ([datetime]::Parse($drv.processStartedAt).ToUniversalTime().AddMilliseconds(-1)).ToString('o') }
if ($Case -ceq 'c5c-cmdmismatch') { $sup.commandLine = 'not-the-real-command-line' }
Write-Json (Join-Path $task 'supervisor-identity.json') $sup
Write-Json (Join-Path $task 'launch-child.json') $drv
if ($lockRec) {
    $lock = [ordered]@{ pid = $lockRec.pid; processStartedAt = $lockRec.processStartedAt; runId = $runId; lane = 'rc'; continuationId = ''; acquiredAt = [datetime]::UtcNow.ToString('o') }
    Write-Json (Join-Path $coord 'native-run.lock') $lock
}
$lockPath = Join-Path $coord 'native-run.lock'
$lockHash = if ([IO.File]::Exists($lockPath)) { (Get-FileHash $lockPath).Hash } else { 'missing' }
$out.records = [ordered]@{ supervisor = $sup; driver = $drv; lock = $lockRec; lockSha256 = $lockHash }
$out.aliveBefore = [ordered]@{ supervisor = (Test-Alive $tree.Supervisor); driver = (Test-Alive $tree.Driver) }
if ($extra) { $out.aliveBefore.lockOwner = (Test-Alive $extra) }

# Every pid named in a record must be one this harness started (ledger), never a live-run pid.
foreach ($rec in @($sup, $drv, $lockRec)) {
    if ($null -eq $rec) { continue }
    if ($LivePids.ContainsKey([int]$rec.pid)) { throw "record names live-run pid $($rec.pid)" }
    if (-not (Select-String -Path $Ledger -SimpleMatch ("{0} " -f $rec.pid) -Quiet)) { throw "record pid $($rec.pid) not in my ledger" }
}

$res = Invoke-Stop -Task $task -Coord $coord -RunId $runId -Reason $reason -Confirm:($Case -cne 'c1-dryrun')
$out.reason = $reason
$out.invoke = $res
Start-Sleep -Milliseconds 500
$out.aliveAfter = [ordered]@{ supervisor = (Test-Alive $tree.Supervisor); driver = (Test-Alive $tree.Driver) }
if ($extra) { $out.aliveAfter.lockOwner = (Test-Alive $extra) }
$out.lockFileAfter = if ([IO.File]::Exists($lockPath)) { (Get-FileHash $lockPath).Hash } else { 'missing' }
$ev = Join-Path $task 'stop-evidence'
$out.stopEvidence = @(if ([IO.Directory]::Exists($ev)) { Get-ChildItem $ev | ForEach-Object { $_.Name + ' sha256=' + (Get-FileHash $_.FullName).Hash } })
$out.supervisorLog = Get-Log $task
try { $out.parsed = $res.stdout | ConvertFrom-Json } catch { $out.parsed = $null }
Assert-Live "after $Shell/$Case"
$out | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $dir 'result.json') -Encoding utf8
$out | ConvertTo-Json -Depth 8
```

### cleanup.ps1

```powershell
#requires -Version 7
# Kill leftover sleepers of one case (or all with -Tag all) by their ledger pid + start time only.
param([Parameter(Mandatory)][string]$Tag)
$Ledger = 'C:\Antiphon\releases\task-a5309b32\pid-ledger.txt'
$live = @(70136, 314140)
foreach ($line in [IO.File]::ReadAllLines($Ledger)) {
    $f = $line.Split(' ')
    if ($f.Count -lt 4) { continue }
    if ($Tag -cne 'all' -and $f[3] -cne $Tag) { continue }
    $id = [int]$f[0]
    if ($live -contains $id) { throw "ledger names live pid $id" }
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if (-not $p) { "{0} {1} {2}: absent" -f $id, $f[2], $f[3]; continue }
    if ($p.StartTime.ToUniversalTime().ToString('o') -cne $f[1]) { "{0} {1}: pid reused by someone else, NOT touched" -f $id, $f[2]; continue }
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'taskkill.exe'
    $info.Arguments = '/PID ' + $id + ' /T /F'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $k = [Diagnostics.Process]::Start($info)
    $o = $k.StandardOutput.ReadToEnd(); $e = $k.StandardError.ReadToEnd(); $k.WaitForExit()
    "{0} {1} {2}: taskkill /PID {0} /T /F exit={3} stdout=[{4}] stderr=[{5}]" -f $id, $f[2], $f[3], $k.ExitCode, $o.Trim(), $e.Trim()
}
```

### c7.ps1

```powershell
#requires -Version 5.1
# Task a5309b32 case 7: real taskkill exit codes + stderr. Targets only processes this script starts.
# A dead target is kept as a held handle so Windows cannot reuse its pid during the probe.
$ErrorActionPreference = 'Stop'
. 'C:\Antiphon\worktrees\card-task-a5309b32\scripts\qualification\qualification-stop.ps1'
$pw = 'C:\Program Files\PowerShell\7\pwsh.exe'
$ledger = 'C:\Antiphon\releases\task-a5309b32\pid-ledger.txt'
$tag = 'c7/' + $PSVersionTable.PSEdition
function Start-Mine([string]$cmd) {
    $i = New-Object System.Diagnostics.ProcessStartInfo
    $i.FileName = $pw; $i.Arguments = '-NoProfile -NonInteractive -Command "' + $cmd + '"'
    $i.UseShellExecute = $false; $i.CreateNoWindow = $true
    $p = [Diagnostics.Process]::Start($i); $null = $p.Handle
    [IO.File]::AppendAllText($ledger, ('{0} {1} c7-target {2}' -f $p.Id, $p.StartTime.ToUniversalTime().ToString('o'), $tag) + "`r`n")
    return $p
}
function Invoke-Raw([int]$id) {
    $i = New-Object System.Diagnostics.ProcessStartInfo
    $i.FileName = 'taskkill.exe'; $i.Arguments = '/PID ' + $id + ' /T /F'
    $i.UseShellExecute = $false; $i.CreateNoWindow = $true
    $i.RedirectStandardOutput = $true; $i.RedirectStandardError = $true
    $k = [Diagnostics.Process]::Start($i)
    $o = $k.StandardOutput.ReadToEnd(); $e = $k.StandardError.ReadToEnd(); $k.WaitForExit()
    return ('exit={0} stdout=[{1}] stderr=[{2}]' -f $k.ExitCode, $o.Trim(), $e.Trim())
}
"shell=$($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)"
# (a) dead pid, handle held: raw command, then the script's own function.
$dead = Start-Mine 'exit 0'; $dead.WaitForExit()
"a-raw dead pid $($dead.Id) (handle held, HasExited=$($dead.HasExited)): " + (Invoke-Raw $dead.Id)
$r = Invoke-QualificationWindowsTreeStop -ProcessId $dead.Id
"a-fn  dead pid $($dead.Id): ok=$($r.ok) reason=$($r.reason) command=$($r.command)"
# (b) live single sleeper via the script's function.
$live = Start-Mine 'Start-Sleep 3600'
Start-Sleep -Milliseconds 800
$r = Invoke-QualificationWindowsTreeStop -ProcessId $live.Id
$live.WaitForExit(10000) | Out-Null
"b-fn  live pid $($live.Id): ok=$($r.ok) reason=$($r.reason) exited=$($live.HasExited)"
# (c) the function refuses its own pid without starting taskkill.
$r = Invoke-QualificationWindowsTreeStop -ProcessId ([Diagnostics.Process]::GetCurrentProcess().Id)
"c-fn  own pid: ok=$($r.ok) reason=$($r.reason)"
$dead.Dispose(); $live.Dispose()
```
