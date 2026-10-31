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
try {
    [void][IO.Directory]::CreateDirectory($logs)
    Copy-Item -LiteralPath (Join-Path $root 'tests/Antiphon.Tests/Scripts/Fixtures/orchestration-synthetic.trx') -Destination $trx
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
    function Invoke-RestMethod {
        param($Uri, $TimeoutSec)
        if ($Uri -ne "http://stub.invalid/api/agent-tasks?projectId=$scopeId") { throw 'Unexpected or unscoped HTTP request.' }
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
    foreach ($owned in @($trx, $bad, $long, $green, $summary, $qualState, $tasksState, $heartbeat)) {
        if (Test-Path -LiteralPath $owned) { Remove-Item -LiteralPath $owned -Force }
    }
    if (Test-Path -LiteralPath $logs) { Remove-Item -LiteralPath $logs }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch }
    Remove-Variable -Name RcQualSmokeReply -Scope Global -ErrorAction SilentlyContinue
}
Write-Host "C487: $passed passed, $failed failed, $($passed + $failed) rows"
if ($failed) { Write-Host 'C487 HARNESS EXIT CODE: 1'; exit 1 }
Write-Host 'C487 HARNESS EXIT CODE: 0'
exit 0
