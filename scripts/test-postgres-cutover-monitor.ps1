#requires -Version 5.1
# CARD-1408 (CARD-1401 C3) harness for scripts/postgres-cutover-monitor.ps1. ASCII-only.
# No Docker, no database, no live HTTP: every measurement seam is a scriptblock fake.
# Usage: pwsh -NoProfile -File scripts/test-postgres-cutover-monitor.ps1 [-Case <name>]
param(
    [string]$Case = ''
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$monitor = Join-Path $here 'postgres-cutover-monitor.ps1'
$script:Passed = 0
$script:Failed = 0

function Assert-C1408 {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) {
        $script:Passed++
        Write-Host ('PASS ' + $Name)
    } else {
        $script:Failed++
        Write-Host ('FAIL ' + $Name + $(if ($Detail) { ' :: ' + $Detail } else { '' }))
    }
}

function New-C1408Scratch {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('c1408-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return $root
}

function New-C1408Task {
    param([string]$Status = 'Succeeded', [string]$LandOutcome = '', [string]$LandRequestStatus = '')
    $item = [ordered]@{ id = [guid]::NewGuid().ToString(); status = $Status; landing = $null; landRequest = $null }
    if ($LandOutcome) { $item.landing = @{ outcome = $LandOutcome } }
    if ($LandRequestStatus) { $item.landRequest = @{ status = $LandRequestStatus } }
    return $item
}

# Fake GET seam. Routes by URL substring; pipeline samples report their own ElapsedMs.
function New-C1408FakeHttp {
    param([double[]]$PipelineMs = @(100), [object[]]$Items = @(), [string]$VersionSha = 'aaaa', [int]$HealthCode = 200, [hashtable]$Counter)
    if (-not $Counter) { $Counter = @{ calls = 0 } }
    $state = @{ i = 0; Pipeline = $PipelineMs; Items = $Items; Sha = $VersionSha; Health = $HealthCode; Counter = $Counter }
    return {
        param([string]$Url)
        $s = $state
        $s.Counter.calls++
        if ($Url -like '*/api/agent-tasks/pipeline*') {
            $ms = [double]$s.Pipeline[$s.i % $s.Pipeline.Count]
            $s.i++
            return @{ StatusCode = 200; Body = '{"ok":true}'; ElapsedMs = $ms }
        }
        if ($Url -like '*/api/agent-tasks?*') {
            $body = @{ scope = 'p1'; items = @($s.Items); excluded = @() } | ConvertTo-Json -Depth 6
            return @{ StatusCode = 200; Body = $body }
        }
        if ($Url -like '*/api/diagnostics/session-state*') {
            return @{ StatusCode = 200; Body = '{"pool":{"NoResetOnClose":true,"MaxPoolSize":100,"connectionString":"Host=x;Password=hunter2"},"password":"hunter2"}' }
        }
        if ($Url -like '*/health*') { return @{ StatusCode = $s.Health; Body = 'Healthy' } }
        if ($Url -like '*/api/version*') { return @{ StatusCode = 200; Body = ('{"sha":"' + $s.Sha + '","capabilities":["land-v2"]}') } }
        throw ('unexpected url ' + $Url)
    }.GetNewClosure()
}

function New-C1408FakeTcp {
    param([double[]]$ConnectMs = @(6.7), [hashtable]$Counter)
    if (-not $Counter) { $Counter = @{ calls = 0 } }
    $state = @{ i = 0; Ms = $ConnectMs; Counter = $Counter }
    return {
        param([string]$HostName, [int]$Port)
        $s = $state
        $s.Counter.calls++
        $v = [double]$s.Ms[$s.i % $s.Ms.Count]
        $s.i++
        return $v
    }.GetNewClosure()
}

$script:FixedNow = { [datetime]::new(2026, 10, 21, 10, 0, 0, [System.DateTimeKind]::Utc) }

function Invoke-C1408Baseline {
    param([string]$Root, [double[]]$PipelineMs, [object[]]$Items = @(), [string]$Sha = 'aaaa')
    $bp = Join-Path $Root 'baseline.json'
    $rp = Join-Path $Root 'baseline-result.json'
    $http = New-C1408FakeHttp -PipelineMs $PipelineMs -Items $Items -VersionSha $Sha
    $r = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $bp -ResultPath $rp -SampleCount $PipelineMs.Count -HttpGet $http -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    return [pscustomobject]@{ Result = $r; BaselinePath = $bp }
}

function Invoke-C1408Watch {
    param([string]$Root, [string]$BaselinePath, [double[]]$PipelineMs, [object[]]$Items = @(), [string]$Sha = 'aaaa', [int]$HealthCode = 200, [string]$Name = 'watch')
    $rp = Join-Path $Root ($Name + '-result.json')
    $http = New-C1408FakeHttp -PipelineMs $PipelineMs -Items $Items -VersionSha $Sha -HealthCode $HealthCode
    return Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $BaselinePath -ResultPath $rp -SampleCount $PipelineMs.Count -HttpGet $http -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
}

function Get-C1408Twenty {
    param([double]$Median)
    # Twenty samples whose median is exactly $Median: ten at Median-1, ten at Median+1.
    $out = @()
    for ($i = 0; $i -lt 10; $i++) { $out += ($Median - 1) }
    for ($i = 0; $i -lt 10; $i++) { $out += ($Median + 1) }
    return $out
}

function Test-C1408_RollbackAboveFloorAndRatio {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 1000) -Items @((New-C1408Task -Status 'Failed'))
    Assert-C1408 -Cond ($b.Result.ExitCode -eq 0) -Name 'baseline with clean samples exits 0' -Detail ('exit=' + $b.Result.ExitCode + ' ' + ($b.Result.Lines -join ' | '))
    Assert-C1408 -Cond (Test-Path -LiteralPath $b.BaselinePath) -Name 'baseline json written'
    $base = Get-Content -LiteralPath $b.BaselinePath -Raw | ConvertFrom-Json
    Assert-C1408 -Cond ([double]$base.pipeline.medianMs -eq 1000 -and [int]$base.failures.total -eq 1) -Name 'baseline records median 1000 and failures 1' -Detail (($base | ConvertTo-Json -Depth 5 -Compress))
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 3500) -Items @((New-C1408Task -Status 'Failed'))
    Assert-C1408 -Cond ($w.ExitCode -eq 2 -and $w.Verdict -eq 'ROLLBACK') -Name 'median 3500 vs baseline 1000 exits 2 ROLLBACK' -Detail ('exit=' + $w.ExitCode + ' verdict=' + $w.Verdict)
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -eq 'ROLLBACK' }).Count -eq 1) -Name 'ROLLBACK is printed on its own line'
    Assert-C1408 -Cond ((Test-Path -LiteralPath $w.ResultPath) -and ((Get-Content -LiteralPath $w.ResultPath -Raw | ConvertFrom-Json).verdict -eq 'ROLLBACK')) -Name 'watch result json records the verdict'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_FloorHoldsUnderOneSecond {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 50)
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 600)
    Assert-C1408 -Cond ($w.ExitCode -eq 0 -and $w.Verdict -eq 'OK') -Name 'median 600 vs baseline 50 (12x, under 1 s) exits 0' -Detail ('exit=' + $w.ExitCode + ' verdict=' + $w.Verdict)
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -like 'NOTE*' }).Count -ge 1) -Name 'ratio-only excess prints a NOTE'
    $b2 = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 1100)
    $w2 = Invoke-C1408Watch -Root $root -BaselinePath $b2.BaselinePath -PipelineMs (Get-C1408Twenty -Median 2000) -Name 'watch2'
    Assert-C1408 -Cond ($w2.ExitCode -eq 0) -Name 'median 2000 vs baseline 1100 (above floor, under 3x) exits 0' -Detail ('exit=' + $w2.ExitCode)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_FailureCountRollback {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100) -Items @((New-C1408Task -Status 'Failed'))
    $four = @((New-C1408Task -Status 'Failed'), (New-C1408Task -Status 'Failed'), (New-C1408Task -Status 'Succeeded' -LandOutcome 'LandRefused'), (New-C1408Task -Status 'Blocked' -LandRequestStatus 'NeedsResolution'), (New-C1408Task -Status 'Succeeded'))
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items $four
    Assert-C1408 -Cond ($w.ExitCode -eq 2 -and $w.Verdict -eq 'ROLLBACK') -Name 'failures 4 vs baseline 1 exits 2 ROLLBACK' -Detail ('exit=' + $w.ExitCode + ' ' + ($w.Lines -join ' | '))
    $three = @((New-C1408Task -Status 'Failed'), (New-C1408Task -Status 'Failed'), (New-C1408Task -Status 'Succeeded' -LandOutcome 'LandRefused'))
    $w2 = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items $three -Name 'watch2'
    Assert-C1408 -Cond ($w2.ExitCode -eq 1 -and $w2.Verdict -eq 'WARN') -Name 'failures 3 vs baseline 1 (exactly +2) is WARN, not rollback' -Detail ('exit=' + $w2.ExitCode + ' verdict=' + $w2.Verdict)
    Assert-C1408 -Cond ((Get-CutoverFailureCount -Items $four).Total -eq 4) -Name 'failure count sums failed tasks and failed lands'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_PreWindowAbort {
    $root = New-C1408Scratch
    $slow = Invoke-PostgresCutoverMonitor -Mode PreWindow -Execute -Server2Host '100.64.0.1' -Server2Port 5432 -LanHost '192.168.4.34' -ConnectSampleCount 5 -ResultPath (Join-Path $root 'pre-slow.json') -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp -ConnectMs @(24, 25, 26, 25, 25)) -Now $script:FixedNow
    Assert-C1408 -Cond ($slow.ExitCode -eq 3 -and $slow.Verdict -eq 'ABORT') -Name 'connect median 25 ms exits 3 ABORT' -Detail ('exit=' + $slow.ExitCode + ' ' + ($slow.Lines -join ' | '))
    Assert-C1408 -Cond (@($slow.Lines | Where-Object { $_ -eq 'ABORT' }).Count -eq 1) -Name 'ABORT is printed on its own line'
    $fast = Invoke-PostgresCutoverMonitor -Mode PreWindow -Execute -Server2Host '100.64.0.1' -Server2Port 5432 -ConnectSampleCount 5 -ResultPath (Join-Path $root 'pre-fast.json') -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp -ConnectMs @(6.5, 6.7, 6.9, 6.7, 6.7)) -Now $script:FixedNow
    Assert-C1408 -Cond ($fast.ExitCode -eq 0 -and $fast.Verdict -eq 'OK') -Name 'connect median 6.7 ms is the accepted band, exits 0' -Detail ('exit=' + $fast.ExitCode)
    $edge = Invoke-PostgresCutoverMonitor -Mode PreWindow -Execute -Server2Host '100.64.0.1' -ConnectSampleCount 3 -ResultPath (Join-Path $root 'pre-edge.json') -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp -ConnectMs @(20, 20, 20)) -Now $script:FixedNow
    Assert-C1408 -Cond ($edge.ExitCode -eq 0) -Name 'connect median exactly 20 ms is not above the line' -Detail ('exit=' + $edge.ExitCode)
    Assert-C1408 -Cond ((Get-CutoverMedian -Values @(3, 1, 2)) -eq 2 -and (Get-CutoverMedian -Values @(1, 2, 3, 10)) -eq 2.5) -Name 'median handles odd and even counts'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_PlanDefaultDoesNotMeasure {
    $root = New-C1408Scratch
    $hc = @{ calls = 0 }
    $tc = @{ calls = 0 }
    $rp = Join-Path $root 'plan.json'
    $r = Invoke-PostgresCutoverMonitor -Mode Watch -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -Server2Host '100.64.0.1' -BaselinePath (Join-Path $root 'missing.json') -ResultPath $rp -HttpGet (New-C1408FakeHttp -Counter $hc) -TcpConnectMs (New-C1408FakeTcp -Counter $tc) -Now $script:FixedNow
    Assert-C1408 -Cond ($r.ExitCode -eq 0 -and $hc.calls -eq 0 -and $tc.calls -eq 0) -Name 'dry plan (no -Execute) calls no seam and exits 0' -Detail ('exit=' + $r.ExitCode + ' http=' + $hc.calls + ' tcp=' + $tc.calls)
    Assert-C1408 -Cond (-not (Test-Path -LiteralPath $rp)) -Name 'dry plan writes no result file'
    Assert-C1408 -Cond (@($r.Lines | Where-Object { $_ -like '*agent-tasks/pipeline*p1*' }).Count -ge 1) -Name 'dry plan names the pipeline url it would sample'
    $miss = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath (Join-Path $root 'missing.json') -ResultPath (Join-Path $root 'miss.json') -HttpGet (New-C1408FakeHttp -Counter $hc) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    Assert-C1408 -Cond ($miss.ExitCode -eq 4 -and $miss.Verdict -eq 'INVALID' -and $hc.calls -eq 0) -Name 'watch without a baseline exits 4 before any request' -Detail ('exit=' + $miss.ExitCode + ' http=' + $hc.calls)
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $raw = Get-Content -LiteralPath $b.BaselinePath -Raw
    Assert-C1408 -Cond ($raw -notmatch 'hunter2' -and $raw -notmatch '(?i)"password"' -and $raw -notmatch '(?i)connectionString' -and $raw -match 'NoResetOnClose') -Name 'recorded diagnostics drop credential-looking properties and keep pool flags' -Detail $raw
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_SourceIsReadOnly {
    $src = Get-Content -LiteralPath $monitor -Raw
    foreach ($bad in @('restart-apphost', 'docker', 'user-secrets', 'psql', 'Npgsql', 'Stop-Process', 'Stop-Service', 'Restart-Service', 'Remove-Item')) {
        Assert-C1408 -Cond ($src -notmatch [regex]::Escape($bad)) -Name ('source has no ' + $bad)
    }
    Assert-C1408 -Cond ($src -notmatch '(?i)-Method\s+(Post|Put|Delete|Patch)') -Name 'source has no non-GET HTTP verb'
    $nonAscii = [regex]::Matches($src, '[^\x00-\x7F]').Count
    Assert-C1408 -Cond ($nonAscii -eq 0) -Name 'source is ASCII-only' -Detail ('non-ascii=' + $nonAscii)
}

function Test-C1408_ProcessExitCodeAndRollbackLine {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 1000)
    $rp = Join-Path $root 'child-result.json'
    $cmd = @'
& '__MON__' -Mode Watch -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath '__BP__' -ResultPath '__RP__' -SampleCount 5 -HttpGet { param([string]$Url); if ($Url -like '*/pipeline*') { return @{ StatusCode = 200; Body = '{}'; ElapsedMs = 3500 } }; if ($Url -like '*/api/agent-tasks?*') { return @{ StatusCode = 200; Body = '{"items":[]}' } }; if ($Url -like '*/health*') { return @{ StatusCode = 200; Body = 'Healthy' } }; return @{ StatusCode = 200; Body = '{"sha":"aaaa","capabilities":["land-v2"]}' } }; exit $LASTEXITCODE
'@
    $cmd = $cmd.Replace('__MON__', $monitor).Replace('__BP__', $b.BaselinePath).Replace('__RP__', $rp)
    $out = & pwsh -NoProfile -NonInteractive -Command $cmd 2>&1 | ForEach-Object { [string]$_ }
    $code = $LASTEXITCODE
    Assert-C1408 -Cond ($code -eq 2) -Name 'child pwsh exits 2 on rollback' -Detail ('exit=' + $code + ' ' + ($out -join ' | '))
    Assert-C1408 -Cond (@($out | Where-Object { $_ -eq 'ROLLBACK' }).Count -eq 1) -Name 'child pwsh prints ROLLBACK on stdout'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

$cases = @(
    'C1408_RollbackAboveFloorAndRatio',
    'C1408_FloorHoldsUnderOneSecond',
    'C1408_FailureCountRollback',
    'C1408_PreWindowAbort',
    'C1408_PlanDefaultDoesNotMeasure',
    'C1408_SourceIsReadOnly',
    'C1408_ProcessExitCodeAndRollbackLine'
)

if (-not (Test-Path -LiteralPath $monitor)) {
    Assert-C1408 -Cond $false -Name 'monitor script exists' -Detail $monitor
} else {
    . $monitor
    $selected = if ($Case) { @($Case) } else { $cases }
    foreach ($c in $selected) {
        $fn = Get-Command -Name ('Test-' + $c) -ErrorAction SilentlyContinue
        if (-not $fn) { Write-Error ('unknown case ' + $c); exit 2 }
        try { & $fn } catch { Assert-C1408 -Cond $false -Name ($c + ' threw') -Detail ([string]$_.Exception.Message + ' at ' + [string]$_.InvocationInfo.PositionMessage) }
    }
}

Write-Host ('POSTGRES-CUTOVER-MONITOR TESTS passed=' + $script:Passed + ' failed=' + $script:Failed)
$exit = if ($script:Failed -gt 0 -or $script:Passed -eq 0) { 1 } else { 0 }
Write-Host ('POSTGRES-CUTOVER-MONITOR TESTS EXIT CODE: ' + $exit)
exit $exit
