#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1407: cutover-postgres-server2.ps1 defaults to -WhatIf, and
    restart-apphost.ps1 -StopOnly does not relaunch.

    No production database, no server2 socket, no AppHost restart.
    HTTP is pointed at a closed loopback port. External commands are
    shadowed by a PATH fake that records a call and fails.
    ASCII-only for pwsh 7 and Windows PowerShell 5.1.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$script:passed = 0
$script:failed = 0
$script:failures = @()
$script:ClosedApi = 'http://127.0.0.1:9'

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function New-C1407FakeBin {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-fake-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $trace = Join-Path $dir 'trace.txt'
    '' | Set-Content -LiteralPath $trace -Encoding ascii
    foreach ($name in @('docker', 'dotnet', 'ssh', 'scp', 'age', 'curl', 'wget', 'pwsh')) {
        $path = Join-Path $dir $name
        $lines = @(
            '#!/bin/sh',
            ('printf "%s\n" ' + "'" + $name + "' >> '" + $trace + "'"),
            'exit 99'
        )
        Set-Content -LiteralPath $path -Value $lines -Encoding ascii
        & chmod +x $path
    }
    return [pscustomobject]@{ Dir = $dir; Trace = $trace }
}

function Remove-C1407Tree {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Read-C1407Trace {
    param($Fake)
    if (-not $Fake -or -not (Test-Path -LiteralPath $Fake.Trace)) { return '' }
    return (Get-Content -LiteralPath $Fake.Trace -Raw -ErrorAction SilentlyContinue)
}

function Invoke-C1407Cutover {
    param(
        [string[]]$Arguments = @(),
        $Fake
    )
    $scriptPath = Join-Path $here 'cutover-postgres-server2.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $prev = $env:PATH
    try {
        if ($Fake) {
            $env:PATH = $Fake.Dir + [System.IO.Path]::PathSeparator + $prev
        }
        $output = @(& $pwsh -NoProfile -File $scriptPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Text     = ($output -join "`n")
            Trace    = $(if ($Fake) { (Read-C1407Trace $Fake).Trim() } else { '' })
        }
    } finally {
        if ($null -eq $prev) { Remove-Item Env:PATH -ErrorAction SilentlyContinue }
        else { $env:PATH = $prev }
    }
}

function New-C1407Drill {
    param(
        [bool]$Passed = $true,
        [double]$Latency = 6.72,
        [int]$DumpRestoreSec = 30,
        [int]$CopySec = 10
    )
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-drill-' + [guid]::NewGuid().ToString('N') + '.json')
    $json = '{{"passed":{0},"latencyMedianMs":{1},"dumpRestoreSec":{2},"copySec":{3}}}' -f `
        $(if ($Passed) { 'true' } else { 'false' }), `
        $Latency.ToString([System.Globalization.CultureInfo]::InvariantCulture), `
        $DumpRestoreSec, `
        $CopySec
    Set-Content -LiteralPath $path -Value $json -Encoding ascii
    return $path
}

function New-C1407File {
    param([string]$Body)
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-file-' + [guid]::NewGuid().ToString('N'))
    Set-Content -LiteralPath $path -Value $Body -Encoding ascii
    return $path
}

function Test-PhaseOrder {
    param([string]$Text)
    $phases = @(
        'PHASE 0 Pre-flight',
        'PHASE 1 Quiesce',
        'PHASE 2 Final dump and restore',
        'PHASE 3 Verify',
        'PHASE 4 Repoint and restart',
        'PHASE 5 Post-checks',
        'PHASE 6 Rollback'
    )
    $last = -1
    foreach ($phase in $phases) {
        $at = $Text.IndexOf($phase, [System.StringComparison]::Ordinal)
        if ($at -lt 0 -or $at -le $last) { return "missing-or-out-of-order $phase at $at" }
        $last = $at
    }
    for ($n = 0; $n -le 6; $n++) {
        $gate = 'STOP GATE {0}:' -f $n
        $phase = $phases[$n]
        $phaseAt = $Text.IndexOf($phase, [System.StringComparison]::Ordinal)
        $gateAt = $Text.IndexOf($gate, $phaseAt, [System.StringComparison]::Ordinal)
        $nextAt = $Text.Length
        if ($n -lt 6) { $nextAt = $Text.IndexOf($phases[$n + 1], [System.StringComparison]::Ordinal) }
        if ($gateAt -lt 0 -or $gateAt -ge $nextAt) { return "stop gate $n not inside its phase" }
    }
    return $null
}

function Test-W1 {
    $fake = New-C1407FakeBin
    try {
        $plain = Invoke-C1407Cutover -Fake $fake -Arguments @('-ApiBase', $script:ClosedApi)
        $again = Invoke-C1407Cutover -Fake $fake -Arguments @('-WhatIf', '-ApiBase', $script:ClosedApi)
        $order = Test-PhaseOrder $plain.Text
        $needles = @(
            'verified snapshot reference',
            'passed drill record',
            'latency',
            'landPending',
            '17204',
            'pg_dump -Fc',
            'pg_restore',
            'count(*)',
            'ExpectedServerSha',
            'desktop volume',
            'user-secrets'
        )
        $missing = @($needles | Where-Object { $plain.Text -notmatch [regex]::Escape($_) })
        $clean = [string]::IsNullOrWhiteSpace($plain.Trace) -and [string]::IsNullOrWhiteSpace($again.Trace)
        $noSecret = ($plain.Text -notmatch 'Password=') -and ($plain.Text -notmatch 'Host=')
        $ok = ($plain.ExitCode -eq 0) -and ($again.ExitCode -eq 0) -and
            ($null -eq $order) -and ($missing.Count -eq 0) -and $clean -and $noSecret -and
            ($plain.Text -match 'does not POST') -and
            ($plain.Text -match 'does not call docker') -and
            ($plain.Text -match 'does not call dotnet user-secrets set')
        if ($ok) { Write-Pass 'W-1' }
        else {
            Write-Fail 'W-1' ("exit=$($plain.ExitCode)/$($again.ExitCode) order=$order missing=$($missing -join ',') trace=$($plain.Trace) $($plain.Text)")
        }
    } catch {
        Write-Fail 'W-1' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

function Test-W2 {
    $fake = New-C1407FakeBin
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-ApiBase', $script:ClosedApi)
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'ConfirmWindow') -and
            [string]::IsNullOrWhiteSpace($r.Trace) -and
            ($r.Text -notmatch 'Password=')
        if ($ok) { Write-Pass 'W-2' }
        else { Write-Fail 'W-2' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-2' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

function Test-W3 {
    $fake = New-C1407FakeBin
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi)
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'snapshot reference') -and
            [string]::IsNullOrWhiteSpace($r.Trace) -and
            ($r.Text -notmatch 'STOP-ONLY') -and
            ($r.Text -notmatch 'Password=')
        if ($ok) { Write-Pass 'W-3' }
        else { Write-Fail 'W-3' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-3' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

function Test-W4 {
    $fake = New-C1407FakeBin
    $snap = New-C1407File -Body 'snapshot-reference'
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap
        )
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'drill record') -and
            [string]::IsNullOrWhiteSpace($r.Trace)
        if ($ok) { Write-Pass 'W-4' }
        else { Write-Fail 'W-4' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-4' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
        Remove-C1407Tree $snap
    }
}

function Test-W5 {
    $path = Join-Path $here 'cutover-postgres-server2.ps1'
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Fail 'W-5' 'cutover script missing'
        return
    }
    $text = Get-Content -LiteralPath $path -Raw
    $hits = @()
    foreach ($needle in @('volume rm', 'docker volume rm', '-AllowWorktree')) {
        if ($text.IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) { $hits += $needle }
    }
    if ($hits.Count -eq 0) { Write-Pass 'W-5' }
    else { Write-Fail 'W-5' ($hits -join ', ') }
}

function Test-W6 {
    $fake = New-C1407FakeBin
    $snap = New-C1407File -Body 'snapshot-reference'
    $drill = New-C1407Drill
    $pending = New-C1407File -Body '{"items":[{"id":"land-1"}]}'
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $drill,
            '-LandPendingFixture', $pending
        )
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'landPending') -and
            ($r.Text -match 'StopOnly was not run') -and
            [string]::IsNullOrWhiteSpace($r.Trace)
        if ($ok) { Write-Pass 'W-6' }
        else { Write-Fail 'W-6' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-6' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
        Remove-C1407Tree $snap
        Remove-C1407Tree $drill
        Remove-C1407Tree $pending
    }
}

function Test-W7 {
    $harness = Join-Path $here 'test-apphost-graceful-stop.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $reasons = @()
    foreach ($id in @('T9', 'T10', 'T12')) {
        $output = @(& $pwsh -NoProfile -File $harness -Case $id 2>&1 | ForEach-Object { $_.ToString() })
        $text = $output -join "`n"
        $code = $LASTEXITCODE
        if ($code -ne 0 -or $text -notmatch 'PASS ') {
            $reasons += ("{0} exit={1} {2}" -f $id, $code, $text)
        }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-7' }
    else { Write-Fail 'W-7' ($reasons -join ' || ') }
}

function Test-W8 {
    $files = @(
        (Join-Path $here 'cutover-postgres-server2.ps1'),
        (Join-Path $here 'test-cutover-postgres-server2.ps1'),
        (Join-Path $here 'restart-apphost.ps1')
    )
    $reasons = @()
    foreach ($f in $files) {
        if (-not (Test-Path -LiteralPath $f)) { $reasons += ('missing {0}' -f (Split-Path $f -Leaf)); continue }
        $bytes = [System.IO.File]::ReadAllBytes($f)
        $high = @($bytes | Where-Object { $_ -gt 0x7F }).Count
        if ($high -ne 0) { $reasons += ('ASCII {0} high={1}' -f (Split-Path $f -Leaf), $high) }
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$errs)
        $n = 0
        if ($null -ne $errs) { $n = @($errs).Count }
        if ($n -ne 0) { $reasons += ('parse {0} errors={1}' -f (Split-Path $f -Leaf), $n) }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-8' }
    else { Write-Fail 'W-8' ($reasons -join '; ') }
}

function Test-W9 {
    $fake = New-C1407FakeBin
    $snap = New-C1407File -Body 'snapshot-reference'
    $slow = New-C1407Drill -Latency 50
    $failed = New-C1407Drill -Passed $false
    $long = New-C1407Drill -DumpRestoreSec 700 -CopySec 10
    try {
        $latency = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $slow
        )
        $notPassed = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $failed
        )
        $envelope = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $long
        )
        $ok = ($latency.ExitCode -ne 0) -and ($latency.Text -match 'latency') -and
            ($notPassed.ExitCode -ne 0) -and ($notPassed.Text -match 'drill') -and
            ($envelope.ExitCode -ne 0) -and ($envelope.Text -match '600') -and
            [string]::IsNullOrWhiteSpace($latency.Trace) -and
            [string]::IsNullOrWhiteSpace($notPassed.Trace) -and
            [string]::IsNullOrWhiteSpace($envelope.Trace)
        if ($ok) { Write-Pass 'W-9' }
        else { Write-Fail 'W-9' ("latency=$($latency.ExitCode) drill=$($notPassed.ExitCode) time=$($envelope.ExitCode) $($latency.Text) $($envelope.Text)") }
    } catch {
        Write-Fail 'W-9' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
        Remove-C1407Tree $snap
        Remove-C1407Tree $slow
        Remove-C1407Tree $failed
        Remove-C1407Tree $long
    }
}

function Test-W10 {
    $fake = New-C1407FakeBin
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @('-Rollback', '-Execute', '-ApiBase', $script:ClosedApi)
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'ConfirmWindow') -and
            [string]::IsNullOrWhiteSpace($r.Trace)
        if ($ok) { Write-Pass 'W-10' }
        else { Write-Fail 'W-10' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-10' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

$all = @('W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8', 'W9', 'W10')
if ($Case) {
    $name = $Case
    if ($name -match '^W-([0-9]+)$') { $name = 'W' + $Matches[1] }
    $fn = Get-Command -Name ('Test-{0}' -f $name) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($id in $all) { & ('Test-{0}' -f $id) }
}

Write-Host ''
Write-Host ('CARD-1407 cutover: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ('  ' + $line) }
    Write-Host 'CUTOVER TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'CUTOVER TESTS EXIT CODE: 0  (PASS)'
exit 0
