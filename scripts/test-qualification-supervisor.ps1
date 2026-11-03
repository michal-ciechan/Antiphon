#requires -Version 5.1
# CARD-1667. Offline witnesses for the rc qualification supervisor set:
# scripts/qualification/{start-supervisor,supervise-rc,observe,launch-rc}.ps1 and
# scripts/lib/qualification-supervisor.ps1. Synthetic task/state roots under TEMP and
# a fake launch script only; no qualification run, lock, or live process is touched.
# ASCII-only.
[CmdletBinding()]
param([string]$Case = 'Smoke', [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0
function Assert-C1667([bool]$Condition, [string]$Name, [string]$Detail = '') {
    if ($Condition) { $script:passed++; Write-Host "PASS C1667 $Name" }
    else { $script:failed++; Write-Host "FAIL C1667 $Name :: $Detail" }
}
$root = Split-Path -Parent $PSScriptRoot
$qualification = Join-Path $PSScriptRoot 'qualification'
$observeScript = Join-Path $qualification 'observe.ps1'
$startScript = Join-Path $qualification 'start-supervisor.ps1'
$isWindowsHost = ($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows
$pwshPath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('c1667-' + [guid]::NewGuid().ToString('N'))
$owned = New-Object System.Collections.Generic.List[string]
$ownedDirs = New-Object System.Collections.Generic.List[string]

function New-OwnedDirectory([string]$Name) {
    $path = Join-Path $scratch $Name
    [void][IO.Directory]::CreateDirectory($path)
    $script:ownedDirs.Insert(0, $path)
    return $path
}
function Write-OwnedJson([string]$Path, $Value) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 6 -Compress), $utf8)
    if (-not $script:owned.Contains($Path)) { $script:owned.Add($Path) }
}
function Get-SelfIdentity {
    $self = [Diagnostics.Process]::GetCurrentProcess()
    return [ordered]@{ pid = $self.Id; processStartedAt = $self.StartTime.ToUniversalTime().ToString('o') }
}
function Get-ExitedIdentity {
    # A real process that has exited: its pid/start pair can no longer match a live process.
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $pwshPath
    $info.Arguments = '-NoProfile -NonInteractive -Command exit 0'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $child = [Diagnostics.Process]::Start($info)
    $started = $child.StartTime.ToUniversalTime().ToString('o')
    $child.WaitForExit()
    $identity = [ordered]@{ pid = $child.Id; processStartedAt = $started }
    $child.Dispose()
    return $identity
}
function New-ObservedRun([string]$Name, $RunOwner, $Supervisor, $HeartbeatUtc, $State) {
    $task = New-OwnedDirectory ($Name + '-task')
    $stateRoot = New-OwnedDirectory ($Name + '-state')
    if ($null -ne $RunOwner) { Write-OwnedJson (Join-Path $stateRoot 'run.lock') ([ordered]@{ pid = $RunOwner.pid; processStartedAt = $RunOwner.processStartedAt; runId = 'c1667run' }) }
    if ($null -ne $State) { Write-OwnedJson (Join-Path $stateRoot 'last-run.json') $State }
    if ($null -ne $Supervisor) { Write-OwnedJson (Join-Path $task 'supervisor-identity.json') $Supervisor }
    if ($null -ne $HeartbeatUtc) { Write-OwnedJson (Join-Path $task 'supervisor-heartbeat.json') ([ordered]@{ observedAtUtc = $HeartbeatUtc; trx = 3; hangSuspected = $false }) }
    return [pscustomobject]@{ Task = $task; State = $stateRoot }
}
function Invoke-Observe($Run) {
    $text = @(& $pwshPath -NoProfile -NonInteractive -File $observeScript -TaskRoot $Run.Task -StateRoot $Run.State 2>&1) -join "`n"
    $code = $LASTEXITCODE
    $parsed = $null
    try { $parsed = ($text.Trim() -split "`n")[-1] | ConvertFrom-Json } catch { }
    return [pscustomobject]@{ Code = $code; Text = $text; Json = $parsed }
}

try {
    [void][IO.Directory]::CreateDirectory($scratch)
    . (Join-Path $PSScriptRoot 'lib/qualification-supervisor.ps1')
    $self = Get-SelfIdentity
    $gone = Get-ExitedIdentity
    $now = [datetime]::UtcNow
    $fresh = $now.ToString('o')
    $stale = $now.AddMinutes(-40).ToString('o')
    $running = [ordered]@{ runId = 'c1667run'; phase = 'tests'; startedAt = $now.AddHours(-9).ToString('o') }

    # The CARD-1667 witness: supervisor and its heartbeat are gone, nightly-run still owns run.lock.
    $run = New-ObservedRun 'lost' $self $gone $stale $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'supervisor-lost-run-alive' -and $o.Json.runOwner -ceq 'alive' -and $o.Json.supervisor -ceq 'absent') `
        'observe classifies a gone supervisor with live nightly-run as supervisor-lost-run-alive' $o.Text

    $run = New-ObservedRun 'hung' $self $self $stale $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'supervisor-lost-run-alive' -and $o.Json.reason -ceq 'heartbeat-stale' -and $o.Json.supervisor -ceq 'alive') `
        'observe treats a stale heartbeat as supervisor lost while the run is alive' $o.Text

    $run = New-ObservedRun 'missingbeat' $self $gone $null $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'supervisor-lost-run-alive' -and $o.Json.heartbeat -ceq 'missing') `
        'observe treats a missing heartbeat as supervisor lost while the run is alive' $o.Text

    $run = New-ObservedRun 'healthy' $self $self $fresh $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'running' -and $o.Json.reason -ceq 'heartbeat-fresh') `
        'observe reports running for a live supervisor with a fresh heartbeat' $o.Text

    $completed = [ordered]@{ runId = 'c1667run'; phase = 'final'; completedAt = $now.AddMinutes(-2).ToString('o'); exitCode = 1; testsPassed = $false }
    $run = New-ObservedRun 'done' $gone $gone $stale $completed
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'completed' -and $o.Json.exitCode -eq 1 -and $null -eq $o.Json.commissionExit -and $o.Json.runOwner -ceq 'absent') `
        'observe treats last-run completedAt as the end signal without commission-exit' $o.Text

    $run = New-ObservedRun 'dead' $gone $gone $stale $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 3 -and $o.Json.classification -ceq 'run-lost' -and $o.Json.runOwner -ceq 'absent') `
        'observe reports run-lost when nightly-run is gone without completedAt' $o.Text

    $run = New-ObservedRun 'noowner' $null $self $fresh $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'unknown') `
        'observe reports unknown without a run owner record' $o.Text

    # Completion races the probe: nightly-run writes completedAt before releasing run.lock and
    # exiting, so the owner must be probed before last-run.json is read.
    $run = New-ObservedRun 'race' $self $gone $stale $running
    $statePath = Join-Path $run.State 'last-run.json'
    $probe = {
        param($Record)
        Write-OwnedJson $statePath ([ordered]@{ runId = 'c1667run'; phase = 'final'; completedAt = [datetime]::UtcNow.ToString('o'); exitCode = 0 })
        [pscustomobject]@{ status = 'absent'; reason = 'pid-not-found' }
    }
    $r = Get-QualificationSupervision -TaskRoot $run.Task -StateRoot $run.State -ObserveProcess $probe
    Assert-C1667 ($r.classification -ceq 'completed' -and $r.exitCode -eq 0) `
        'observe probes the run owner before reading last-run.json' ($r | ConvertTo-Json -Compress)

    # Line-flushed log: every line is on disk when the call returns, and no handle stays open.
    $logDir = New-OwnedDirectory 'log'
    $log = Join-Path $logDir 'supervisor.log'
    $owned.Add($log)
    Write-QualificationSupervisorLine -Path $log -Text 'PROGRESS one'
    Write-QualificationSupervisorLine -Path $log -Text 'PROGRESS two'
    $bytes = [IO.File]::ReadAllBytes($log)
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    $exclusive = $false
    try { $h = [IO.File]::Open($log, 'Open', 'ReadWrite', 'None'); $h.Dispose(); $exclusive = $true } catch { }
    Assert-C1667 ($text -match '^\S+Z PROGRESS one\r?\n\S+Z PROGRESS two\r?\n$' -and $exclusive) `
        'supervisor log lines are on disk at return and the file is not held open' $text

    $win = New-QualificationDetachedStartParameters -FilePath 'C:\pwsh\pwsh.exe' -ArgumentList @('-File', 'C:\a b\supervise-rc.ps1') `
        -Platform Windows -StandardOutput 'C:\t\out.log' -StandardError 'C:\t\err.log'
    Assert-C1667 ($win.WindowStyle -ceq 'Hidden' -and -not $win.ContainsKey('NoNewWindow') -and $win.FilePath -ceq 'C:\pwsh\pwsh.exe' -and
        $win.ArgumentList -ceq '-File "C:\a b\supervise-rc.ps1"' -and $win.RedirectStandardOutput -ceq 'C:\t\out.log' -and $win.PassThru) `
        'Windows detached start gets its own hidden console and file handles, never NoNewWindow' ($win | ConvertTo-Json -Compress)
    $unix = New-QualificationDetachedStartParameters -FilePath '/usr/bin/pwsh' -ArgumentList @('-File', '/t/supervise-rc.ps1') `
        -Platform Unix -StandardOutput '/t/out.log' -StandardError '/t/err.log'
    Assert-C1667 ($unix.FilePath -ceq 'setsid' -and $unix.ArgumentList -ceq '/usr/bin/pwsh -File /t/supervise-rc.ps1' -and -not $unix.ContainsKey('WindowStyle') -and
        $unix.RedirectStandardError -ceq '/t/err.log') `
        'Unix detached start uses setsid and file redirects' ($unix | ConvertTo-Json -Compress)

    # End to end: wrapper -> detached supervisor -> fake launch-rc (exit 7). The wrapper runs as the
    # leader of its own process group; after it returns that group is killed, which is the Unix
    # analogue of the shared console teardown that killed the 7f8e9fd1 supervisor.
    $e2eTask = New-OwnedDirectory 'e2e-task'
    $e2eLogs = New-OwnedDirectory 'e2e-logs'
    $e2eState = New-OwnedDirectory 'e2e-state'
    $fake = Join-Path $e2eTask 'fake-launch.ps1'
    $owned.Add($fake)
    [IO.File]::WriteAllText($fake, "Start-Sleep -Seconds 6`r`n[IO.File]::WriteAllText((Join-Path '$e2eLogs' 'a.trx'), 'x')`r`nWrite-Output 'fake launch done'`r`nexit 7`r`n")
    foreach ($name in @('supervisor-identity.json', 'start-supervisor.json', 'supervisor.log', 'supervisor-stdout.log', 'supervisor-stderr.log',
        'supervisor-heartbeat.json', 'supervisor-heartbeat.json.tmp', 'commission-console.log', 'commission-stderr.log', 'commission-exit.txt',
        'commission-exit.txt.tmp', 'launch-child.json', 'hang-suspected.txt', 'wrapper-out.log', 'wrapper-err.log')) { $owned.Add((Join-Path $e2eTask $name)) }
    $owned.Add((Join-Path $e2eLogs 'a.trx'))
    $wrapperArgs = @('-NoProfile', '-NonInteractive', '-File', $startScript, '-TaskRoot', $e2eTask, '-LaunchScript', $fake,
        '-LogRoot', $e2eLogs, '-StateRoot', $e2eState, '-PollSeconds', '1', '-ProgressMinutes', '1')
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ($isWindowsHost) { $info.FileName = $pwshPath; $info.Arguments = (ConvertTo-QualificationCommandLine $wrapperArgs) }
    else { $info.FileName = 'setsid'; $info.Arguments = (ConvertTo-QualificationCommandLine (@($pwshPath) + $wrapperArgs)) }
    $wrapper = [Diagnostics.Process]::Start($info)
    $wrapperOut = $wrapper.StandardOutput.ReadToEndAsync()
    $wrapperErr = $wrapper.StandardError.ReadToEndAsync()
    $wrapperExited = $wrapper.WaitForExit(60000)
    $wrapperPid = $wrapper.Id
    $wrapperCode = -1
    if ($wrapperExited) { $wrapperCode = $wrapper.ExitCode }
    $wrapperText = $wrapperOut.GetAwaiter().GetResult() + $wrapperErr.GetAwaiter().GetResult()
    $identity = $null
    try { $identity = Get-Content -LiteralPath (Join-Path $e2eTask 'supervisor-identity.json') -Raw | ConvertFrom-Json } catch { }
    $teardown = 'not-attempted'
    if ($wrapperExited -and -not $isWindowsHost) {
        & kill -TERM -- ('-' + $wrapperPid) 2>$null
        $teardown = 'group-' + $wrapperPid + '-killed'
    }
    $exitFile = Join-Path $e2eTask 'commission-exit.txt'
    $deadline = [datetime]::UtcNow.AddSeconds(60)
    while (-not (Test-Path -LiteralPath $exitFile) -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    $supervisorGone = $false
    if ($identity) {
        while ([datetime]::UtcNow -lt $deadline) {
            $p = Get-Process -Id ([int]$identity.pid) -ErrorAction SilentlyContinue
            if (-not $p) { $supervisorGone = $true; break }
            $p.Dispose(); Start-Sleep -Milliseconds 250
        }
    }
    $exitText = $null
    if (Test-Path -LiteralPath $exitFile) { $exitText = (Get-Content -LiteralPath $exitFile -Raw).Trim() }
    $logText = ''
    if (Test-Path -LiteralPath (Join-Path $e2eTask 'supervisor.log')) { $logText = [string](Get-Content -LiteralPath (Join-Path $e2eTask 'supervisor.log') -Raw) }
    $detail = "wrapperCode=$wrapperCode teardown=$teardown exit=$exitText gone=$supervisorGone wrapper=[$wrapperText] log=[$logText]"
    Assert-C1667 ($wrapperExited -and $wrapperCode -eq 0 -and $identity -and [int]$identity.pid -ne $wrapperPid) `
        'start-supervisor returns after the supervisor records its own identity' $detail
    $detached = $false
    if ($identity) {
        if ($isWindowsHost) { $detached = ($identity.detach -ceq 'new-console') -and -not (@($identity.consoleProcessIds) -contains $wrapperPid) }
        else { $detached = ($identity.detach -ceq 'setsid') -and [int]$identity.sessionId -eq [int]$identity.pid -and [int]$identity.sessionId -ne $wrapperPid }
    }
    Assert-C1667 $detached 'supervisor runs in its own session or console, not the wrapper''s' (($identity | ConvertTo-Json -Compress) + ' ' + $detail)
    Assert-C1667 ($exitText -ceq '7' -and $supervisorGone) 'supervisor survives teardown of the starting group and records the launch exit code' $detail
    $beat = $null
    try { $beat = Get-Content -LiteralPath (Join-Path $e2eTask 'supervisor-heartbeat.json') -Raw | ConvertFrom-Json } catch { }
    Assert-C1667 ($logText -match '(?m)^\S+Z START supervisor pid=' -and $logText -match '(?m)^\S+Z LAUNCHED pid=' -and $logText -match '(?m)^\S+Z EXIT 7\r?$' -and
        $logText.EndsWith("`n") -and $beat -and $null -ne $beat.observedAtUtc) `
        'supervisor writes START, LAUNCHED and EXIT lines and a heartbeat' $detail
    $console = ''
    if (Test-Path -LiteralPath (Join-Path $e2eTask 'commission-console.log')) { $console = [string](Get-Content -LiteralPath (Join-Path $e2eTask 'commission-console.log') -Raw) }
    Assert-C1667 ($console.Contains('fake launch done')) 'launch child output goes to its own commission-console.log' $console
    $o = Invoke-Observe ([pscustomobject]@{ Task = $e2eTask; State = $e2eState })
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.supervisor -ceq 'absent' -and $o.Json.commissionExit -ceq '7') `
        'observe reads the real supervisor identity and commission-exit of the finished run' $o.Text

    $valid = $true
    $problems = @()
    foreach ($path in @((Join-Path $PSScriptRoot 'lib/qualification-supervisor.ps1'), $observeScript, $startScript,
        (Join-Path $qualification 'supervise-rc.ps1'), (Join-Path $qualification 'launch-rc.ps1'), $PSCommandPath)) {
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        if (@($errors).Count -gt 0) { $valid = $false; $problems += "parse $path" }
        if (@([IO.File]::ReadAllBytes($path) | Where-Object { $_ -gt 127 }).Count -gt 0) { $valid = $false; $problems += "non-ascii $path" }
        if ($ast.ParamBlock) {
            foreach ($parameter in @($ast.ParamBlock.Parameters)) {
                if ($parameter.DefaultValue -and $parameter.DefaultValue.Extent.Text -match 'PSScriptRoot') { $valid = $false; $problems += "PSScriptRoot default $path" }
            }
        }
    }
    Assert-C1667 $valid 'supervisor scripts parse, are ASCII and keep PSScriptRoot out of param defaults' ($problems -join '; ')
} catch {
    $script:failed++
    Write-Host "FAIL C1667 harness error: $($_.Exception.Message) $($_.ScriptStackTrace)"
} finally {
    # Exact owned paths only; never recursive removal.
    foreach ($path in $owned) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
    foreach ($dir in $ownedDirs) {
        foreach ($leaf in @('run.lock', 'last-run.json', 'supervisor-identity.json', 'supervisor-heartbeat.json')) {
            $p = Join-Path $dir $leaf
            if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
        }
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir }
    }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch }
}
Write-Host "C487: $($script:passed) passed, $($script:failed) failed, $($script:passed + $script:failed) rows"
if ($script:failed) { Write-Host 'C487 HARNESS EXIT CODE: 1'; exit 1 }
Write-Host 'C487 HARNESS EXIT CODE: 0'
exit 0
