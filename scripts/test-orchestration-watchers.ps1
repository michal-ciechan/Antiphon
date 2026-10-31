#requires -Version 5.1
# Offline, Pester-free smoke checks. Synthetic files and stubbed HTTP/git only.
# No native qualification tests, locks, processes, or live services are touched.
[CmdletBinding()]
param([string]$Case = 'Smoke', [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$passed = 0
$failed = 0
function Assert-Smoke([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    $script:passed++
    Write-Host "PASS RCQUAL $Name"
}
$root = Split-Path -Parent $PSScriptRoot
$helpers = Join-Path $PSScriptRoot 'orchestration'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('rcqual-smoke-' + [guid]::NewGuid().ToString('N'))
$logs = Join-Path $scratch 'logs'
$trx = Join-Path $logs 'synthetic.trx'
$qualState = Join-Path $scratch 'qual.seen.txt'
$tasksState = Join-Path $scratch 'tasks.json'
$heartbeat = Join-Path $scratch 'heartbeat.json'
$bad = Join-Path $logs 'incomplete.trx'
$long = Join-Path $scratch 'long.trx'
$green = Join-Path $scratch 'green.trx'
$summary = Join-Path $logs 'summary.json'
$scopeId = '11111111-1111-1111-1111-111111111111'
$originalApi = $env:ANTIPHON_API
$originalToken = $env:ANTIPHON_TASK_TOKEN
$originalPath = $env:PATH
$gitShim = Join-Path $scratch 'git.cmd'
try {
    [void][IO.Directory]::CreateDirectory($logs)
    Copy-Item -LiteralPath (Join-Path $root 'tests/Antiphon.Tests/Scripts/Fixtures/orchestration-synthetic.trx') -Destination $trx
    $watchArgs = @{ LogDir = $logs; StateFile = $qualState; Heartbeat = $heartbeat; Once = $true; MinFileAgeSeconds = 0 }
    foreach ($outcome in @('Timeout', 'Aborted', 'Error')) {
        [xml]$failureXml = Get-Content -LiteralPath $trx -Raw
        $failureXml.SelectSingleNode('//*[local-name()="UnitTestResult"][@outcome="Failed"]').SetAttribute('outcome', $outcome)
        $failurePath = Join-Path $logs ($outcome + '.trx')
        try {
            $failureXml.Save($failurePath)
            $output = @(& (Join-Path $helpers 'trx-failures.ps1') -Path $failurePath) -join "`n"
            Assert-Smoke ($output.Contains('TEST Synthetic.WindowsInputFails') -and -not $output.Contains('no failures')) "extractor reports $outcome as a failure"
            $output = @(& (Join-Path $helpers 'watch-qual.ps1') @watchArgs) -join "`n"
            Assert-Smoke ($output.Contains("FAILURES ${outcome}.trx: 1 failed")) "qualification reports $outcome as a failure"
        } finally {
            if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
            if (Test-Path -LiteralPath $qualState) { Remove-Item -LiteralPath $qualState }
        }
    }
    $output = @(& (Join-Path $helpers 'trx-failures.ps1') -Path $trx) -join "`n"
    Assert-Smoke ($output.Contains('TEST Synthetic.WindowsInputFails') -and -not $output.Contains('TEST Synthetic.Passes') -and $output.Contains('DURATION 00:00:01.2500000')) 'extracts failed test and duration only'
    Assert-Smoke ($output.Contains('Expected Windows-shaped input to remain intact.') -and $output.Contains('Synthetic.Third()') -and -not $output.Contains('Synthetic.Fourth()') -and $output.Contains('Synthetic diagnostic output only.')) 'extracts message three frames and stdout'
    [xml]$xml = Get-Content -LiteralPath $trx -Raw
    $message = $xml.SelectSingleNode('//*[local-name()="Message"]')
    $message.InnerText = (' ' * 1600) + 'MESSAGE-END'
    $xml.SelectSingleNode('//*[local-name()="StdOut"]').InnerText = ('s' * 2600) + 'STDOUT-END'
    $xml.Save($long)
    $output = @(& (Join-Path $helpers 'trx-failures.ps1') -Path $long) -join "`n"
    Assert-Smoke ($output.Contains('MESSAGE ' + (' ' * 1500) + "`n") -and -not $output.Contains('MESSAGE-END') -and $output.Contains('STDOUT ' + ('s' * 2500)) -and -not $output.Contains('STDOUT-END') -and -not $output.Contains('TRX-READ-ERROR')) 'bounds the same message string and stdout'
    $xml.SelectSingleNode('//*[local-name()="UnitTestResult"][@outcome="Failed"]').SetAttribute('outcome', 'Passed')
    $xml.Save($green)
    $output = @(& (Join-Path $helpers 'trx-failures.ps1') -Path $green) -join "`n"
    Assert-Smoke ($output -eq 'no failures' -and $LASTEXITCODE -eq 0) 'reports no failures with zero exit'
    Set-Content -LiteralPath $bad -Value '<TestRun' -Encoding ASCII
    $output = @(& (Join-Path $helpers 'trx-failures.ps1') -Path @($bad, $trx, (Join-Path $scratch 'missing.trx'))) -join "`n"
    Assert-Smoke ($output.Contains('TRX-READ-ERROR') -and $output.Contains('TEST Synthetic.WindowsInputFails') -and $LASTEXITCODE -eq 0) 'continues across bad missing and valid inputs'
    $output = @(& (Join-Path $helpers 'trx-failures.ps1')) -join "`n"
    Assert-Smoke ($output.Contains('TRX-READ-ERROR') -and -not $output.Contains('no failures')) 'no input is diagnostic not a green result'
    $output = @(& (Join-Path $helpers 'trx-failures.ps1') -Path $bad) -join "`n"
    Assert-Smoke ($output.Contains('TRX-READ-ERROR') -and -not $output.Contains('no failures')) 'unreadable input is diagnostic not a green result'
    $output = @(& (Join-Path $helpers 'trx-failures.ps1') $green $trx) -join "`n"
    Assert-Smoke ($output.Contains("FILE $trx") -and $output.Contains('TEST Synthetic.WindowsInputFails') -and -not $output.Contains('TRX-READ-ERROR')) 'extractor accepts separate positional paths'
    @{ utc = [datetime]::UtcNow.AddMinutes(-10).ToString('o'); runId = 'synthetic'; supervisorPid = 1; trx = 1; hangSuspected = $true; idleMinutes = 10 } | ConvertTo-Json | Set-Content -LiteralPath $heartbeat -Encoding ASCII
    $before = [IO.File]::ReadAllText($heartbeat)
    $watchArgs = @{ LogDir = $logs; StateFile = $qualState; Heartbeat = $heartbeat; Once = $true; MinFileAgeSeconds = 0 }
    $output = @(& (Join-Path $helpers 'watch-qual.ps1') @watchArgs) -join "`n"
    Assert-Smoke ($output.Contains('FAILURES synthetic.trx: 1 failed :: WindowsInputTests 1') -and $output.Contains('TRX-PARSE-ERROR incomplete.trx')) 'qualification reports failures and partial TRX'
    Assert-Smoke ($output.Contains('HANG-SUSPECTED') -and $output.Contains('HEARTBEAT-STALE')) 'qualification reports stale and suspected hang'
    $output = @(& (Join-Path $helpers 'watch-qual.ps1') @watchArgs) -join "`n"
    Assert-Smoke (-not $output.Contains('FAILURES synthetic.trx') -and $output.Contains('TRX-PARSE-ERROR incomplete.trx')) 'rearm deduplicates completed TRX and retries partial'
    Copy-Item -LiteralPath $trx -Destination $bad -Force
    $output = @(& (Join-Path $helpers 'watch-qual.ps1') @watchArgs) -join "`n"
    Assert-Smoke ($output.Contains('FAILURES incomplete.trx: 1 failed')) 'reports repaired partial TRX on next poll'
    Set-Content -LiteralPath $summary -Value '{}' -Encoding ASCII
    $output = @(& (Join-Path $helpers 'watch-qual.ps1') @watchArgs) -join "`n"
    Assert-Smoke ($output.Contains('RUN-ENDED') -and [IO.File]::ReadAllText($heartbeat) -eq $before) 'observes completion without writing heartbeat'
    Remove-Item -LiteralPath $summary
    @{ observedAtUtc = [datetime]::UtcNow.ToString('o'); hangSuspected = $false } | ConvertTo-Json | Set-Content -LiteralPath $heartbeat -Encoding ASCII
    $output = @(& (Join-Path $helpers 'watch-qual.ps1') @watchArgs) -join "`n"
    Assert-Smoke (-not $output.Contains('HEARTBEAT-ERROR') -and -not $output.Contains('HEARTBEAT-STALE')) 'qualification accepts repository observedAtUtc heartbeat'
    function Invoke-RestMethod {
        param($Uri, $TimeoutSec, $Headers)
        if ($Uri -ne "http://stub.invalid/api/agent-tasks?projectId=$scopeId") { throw 'Unexpected or unscoped HTTP request.' }
        $global:RcQualSmokeHeaders = $Headers
        return $global:RcQualSmokeReply
    }
    $global:RcQualSmokeReply = [pscustomobject]@{ items = @([pscustomobject]@{ id = 'synthetic-task'; status = 'Blocked'; role = 'Debug'; title = 'fixture' }) }
    $taskArgs = @{ ProjectId = $scopeId; StateFile = $tasksState; Api = 'http://stub.invalid'; Once = $true }
    $output = @(& (Join-Path $helpers 'watch-tasks.ps1') @taskArgs) -join "`n"
    Assert-Smoke (-not $output.Contains('TASK-BLOCKED') -and -not $output.Contains('TASK-WATCH-ERROR') -and (Test-Path -LiteralPath $tasksState)) 'task baseline uses scoped envelope'
    $global:RcQualSmokeReply.items[0].status = 'Failed'
    $output = @(& (Join-Path $helpers 'watch-tasks.ps1') @taskArgs) -join "`n"
    Assert-Smoke ($output.Contains('TASK-FAILED synthetic-task Debug fixture')) 'task rearm emits new failed transition'
    $output = @(& (Join-Path $helpers 'watch-tasks.ps1') @taskArgs) -join "`n"
    Assert-Smoke (-not $output.Contains('TASK-FAILED')) 'task rearm suppresses duplicate transition'
    foreach ($status in @('Blocked', 'Failed')) {
        $global:RcQualSmokeReply.items[0].status = 'Working'
        $working = @(& (Join-Path $helpers 'watch-tasks.ps1') @taskArgs) -join "`n"
        $global:RcQualSmokeReply.items[0].status = $status
        $changed = @(& (Join-Path $helpers 'watch-tasks.ps1') @taskArgs) -join "`n"
        $unchanged = @(& (Join-Path $helpers 'watch-tasks.ps1') @taskArgs) -join "`n"
        Assert-Smoke (-not $working.Contains('TASK-WORKING') -and $changed.Contains("TASK-$($status.ToUpperInvariant()) synthetic-task") -and -not $unchanged.Contains("TASK-$($status.ToUpperInvariant())")) "task rearm emits re-$status and suppresses unchanged repeat"
    }
    $env:ANTIPHON_API = 'http://stub.invalid'
    $env:ANTIPHON_TASK_TOKEN = 'synthetic-offline-token'
    $output = @(& (Join-Path $helpers 'watch-tasks.ps1') -ProjectId $scopeId -StateFile $tasksState -Once) -join "`n"
    Assert-Smoke (-not $output.Contains('TASK-WATCH-ERROR') -and $global:RcQualSmokeHeaders['X-Antiphon-Task-Token'] -eq 'synthetic-offline-token') 'task watcher uses API and token environment defaults'
    @{ scope = "projectId=$scopeId"; seen = @('synthetic-task|Blocked', 'synthetic-task|Failed') } | ConvertTo-Json | Set-Content -LiteralPath $tasksState -Encoding ASCII
    $output = @(& (Join-Path $helpers 'watch-tasks.ps1') @taskArgs) -join "`n"
    $saved = Get-Content -LiteralPath $tasksState -Raw | ConvertFrom-Json
    Assert-Smoke ($output.Contains('TASK-STATE-UPGRADE') -and -not $output.Contains('TASK-FAILED') -and $saved.lastStatus.'synthetic-task' -eq 'Failed') 'task watcher explicitly baselines legacy state'
    $global:RcQualSmokeReply = [pscustomobject]@{ invalid = $true }
    $output = @(& (Join-Path $helpers 'watch-tasks.ps1') @taskArgs) -join "`n"
    Assert-Smoke ($output.Contains('TASK-WATCH-ERROR Task list envelope has no items.')) 'task watcher reports invalid envelope'
    function git { $global:LASTEXITCODE = 0; "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb`trefs/heads/master" }
    $output = @(& (Join-Path $helpers 'watch-master.ps1') -From ('a' * 40) -Repository $scratch -Once) -join "`n"
    Assert-Smoke ($output.Contains('MASTER MOVED ' + ('b' * 40))) 'master watcher reports remote movement'
    $output = @(& (Join-Path $helpers 'watch-master.ps1') -From ('b' * 40) -Repository $scratch -Once) -join "`n"
    Assert-Smoke (-not $output.Contains('MASTER MOVED')) 'master watcher ignores unchanged remote'
    function git { $global:LASTEXITCODE = 1; 'synthetic transport failure' }
    $output = @(& (Join-Path $helpers 'watch-master.ps1') -From ('a' * 40) -Repository $scratch -Once) -join "`n"
    Assert-Smoke ($output.Contains('MASTER-WATCH-ERROR') -and -not $output.Contains('MASTER MOVED')) 'master watcher reports transport failure'
    if ($env:OS -eq 'Windows_NT') {
        Remove-Item -LiteralPath Function:\git
        [IO.File]::WriteAllText($gitShim, "@echo off`r`necho synthetic git warning 1>&2`r`necho $('b' * 40)`trefs/heads/master`r`nexit /b 0`r`n", [Text.Encoding]::ASCII)
        $env:PATH = $scratch + [IO.Path]::PathSeparator + $originalPath
    } else {
        function git { Write-Error 'synthetic git warning'; $global:LASTEXITCODE = 0; "$('b' * 40)`trefs/heads/master" }
    }
    $output = @(& (Join-Path $helpers 'watch-master.ps1') -From ('a' * 40) -Repository $scratch -Once) -join "`n"
    Assert-Smoke ($output.Contains('MASTER MOVED ' + ('b' * 40)) -and -not $output.Contains('MASTER-WATCH-ERROR')) 'master watcher accepts successful git with stderr warning'
    $env:PATH = $originalPath
    function git { $global:LASTEXITCODE = 0; "$('b' * 40)`trefs/heads/master" }
    function Start-Sleep {
        param($Milliseconds)
        $global:RcQualSleepMs = $Milliseconds
        throw 'RCQUAL-SLEEP-CUT'
    }
    foreach ($name in @('watch-tasks.ps1', 'watch-qual.ps1', 'watch-master.ps1')) {
        $window = @{ MaxMinutes = 1; PollSeconds = 300 }
        if ($name -eq 'watch-tasks.ps1') { $window.ProjectId = $scopeId; $window.StateFile = $tasksState; $window.Api = 'http://stub.invalid' }
        elseif ($name -eq 'watch-qual.ps1') { $window.LogDir = $logs; $window.StateFile = $qualState; $window.Heartbeat = $heartbeat; $window.MinFileAgeSeconds = 0 }
        else { $window.From = 'b' * 40; $window.Repository = $scratch }
        $global:RcQualSleepMs = 0; $cut = $false
        try { & (Join-Path $helpers $name) @window | Out-Null } catch { $cut = $_.Exception.Message -eq 'RCQUAL-SLEEP-CUT' }
        Assert-Smoke ($cut -and $global:RcQualSleepMs -gt 0 -and $global:RcQualSleepMs -le 60000) "$name clamps sleep to remaining window"
    }
    @{ utc = [datetime]::UtcNow.AddMinutes(-10).ToString('o'); hangSuspected = $true } | ConvertTo-Json | Set-Content -LiteralPath $heartbeat -Encoding ASCII
    $global:RcQualPoll = 0
    function Start-Sleep {
        param($Milliseconds)
        $global:RcQualPoll++
        if ($global:RcQualPoll -ge 4) { throw 'RCQUAL-SLEEP-CUT' }
        if ($global:RcQualPoll -eq 2) {
            @{ utc = [datetime]::UtcNow.ToString('o'); hangSuspected = $false } | ConvertTo-Json | Set-Content -LiteralPath $heartbeat -Encoding ASCII
        } elseif ($global:RcQualPoll -eq 3) {
            @{ utc = [datetime]::UtcNow.AddMinutes(-10).ToString('o'); hangSuspected = $true } | ConvertTo-Json | Set-Content -LiteralPath $heartbeat -Encoding ASCII
        }
    }
    $events = New-Object 'Collections.Generic.List[string]'
    $cut = $false
    try { & (Join-Path $helpers 'watch-qual.ps1') -LogDir $logs -StateFile $qualState -Heartbeat $heartbeat -MinFileAgeSeconds 0 -MaxMinutes 1 | ForEach-Object { $events.Add([string]$_) } } catch { $cut = $_.Exception.Message -eq 'RCQUAL-SLEEP-CUT' }
    Assert-Smoke ($cut -and @($events | Where-Object { $_ -like 'HEARTBEAT-STALE *' }).Count -eq 2 -and @($events | Where-Object { $_ -like 'HANG-SUSPECTED *' }).Count -eq 2) 'heartbeat alerts emit on transition and reemit after recovery'
    Remove-Item -LiteralPath Function:\Start-Sleep
    $skill = [IO.File]::ReadAllText((Join-Path $root '.claude/skills/antiphon-rc-qualification/SKILL.md'))
    Assert-Smoke ($skill.Contains('`timeout_ms: 1800000`') -and -not $skill.Contains('persistent:') -and $skill.Contains('REPLACE any private watcher copies')) 'skill uses supported Monitor arguments and repository watchers'
    $valid = $true
    foreach ($name in @('watch-tasks.ps1', 'watch-qual.ps1', 'watch-master.ps1', 'trx-failures.ps1')) {
        $path = Join-Path $helpers $name
        $tokens = $null; $errors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        if (@($errors).Count -gt 0 -or @([IO.File]::ReadAllBytes($path) | Where-Object { $_ -gt 127 }).Count -gt 0) { $valid = $false }
    }
    Assert-Smoke $valid 'helpers parse and contain only ASCII'
} catch {
    $failed++
    Write-Host "FAIL RCQUAL $($_.Exception.Message)"
} finally {
    # Exact owned paths only; never recursive removal or a filesystem sweep.
    $env:ANTIPHON_API = $originalApi; $env:ANTIPHON_TASK_TOKEN = $originalToken; $env:PATH = $originalPath
    foreach ($owned in @($trx, $bad, $long, $green, $summary, $qualState, $tasksState, $heartbeat, $gitShim)) {
        if (Test-Path -LiteralPath $owned) { Remove-Item -LiteralPath $owned -Force }
    }
    if (Test-Path -LiteralPath $logs) { Remove-Item -LiteralPath $logs }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch }
    Remove-Variable -Name RcQualSmokeReply -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable -Name RcQualSmokeHeaders,RcQualSleepMs,RcQualPoll -Scope Global -ErrorAction SilentlyContinue
}
Write-Host "C487: $passed passed, $failed failed, $($passed + $failed) rows"
if ($failed) { Write-Host 'C487 HARNESS EXIT CODE: 1'; exit 1 }
Write-Host 'C487 HARNESS EXIT CODE: 0'
exit 0
