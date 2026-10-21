#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1407: set-apphost-maintenance.ps1 -EnterIfAbsent creates the marker
    only when it is absent. The default enter and -Clear paths stay as they
    are. This file never touches the checkout logs directory.
    ASCII-only for pwsh 7 and Windows PowerShell 5.1.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$script:passed = 0
$script:failed = 0
$script:failures = @()

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

function New-M1407Root {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-maint-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $dir 'logs') -Force | Out-Null
    return $dir
}

function Remove-M1407Tree {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-M1407 {
    param([string]$Root, [string[]]$Arguments)
    $scriptPath = Join-Path $here 'set-apphost-maintenance.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $output = @(& $pwsh -NoProfile -File $scriptPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Text     = ($output -join "`n")
    }
}

function Test-M1 {
    $root = New-M1407Root
    $marker = Join-Path (Join-Path $root 'logs') 'apphost.down-on-purpose'
    $body = 'down-on-purpose foreign-owner'
    try {
        Set-Content -LiteralPath $marker -Value $body -Encoding ascii
        $before = [System.IO.File]::ReadAllText($marker)
        $run = Invoke-M1407 -Root $root -Arguments @('-Root', $root, '-EnterIfAbsent')
        $after = [System.IO.File]::ReadAllText($marker)
        $ok = ($run.ExitCode -eq 2) -and
            ($run.Text.IndexOf('This run did not enter maintenance.', [System.StringComparison]::Ordinal) -ge 0) -and
            ($after -eq $before) -and
            ($run.Text.IndexOf('Watchdog task disabled.', [System.StringComparison]::Ordinal) -lt 0)
        if ($ok) { Write-Pass 'M-1' }
        else { Write-Fail 'M-1' ("exit=$($run.ExitCode) $(if ($run.Text.Length -gt 300) { $run.Text.Substring(0, 300) } else { $run.Text })") }
    } catch {
        Write-Fail 'M-1' $_.Exception.Message
    } finally {
        Remove-M1407Tree $root
    }
}

function Test-M2 {
    $root = New-M1407Root
    $marker = Join-Path (Join-Path $root 'logs') 'apphost.down-on-purpose'
    try {
        $run = Invoke-M1407 -Root $root -Arguments @('-Root', $root, '-EnterIfAbsent')
        $exists = Test-Path -LiteralPath $marker
        $text = ''
        if ($exists) { $text = [System.IO.File]::ReadAllText($marker) }
        $ok = ($run.ExitCode -eq 0) -and $exists -and ($text.StartsWith('down-on-purpose '))
        if ($ok) { Write-Pass 'M-2' }
        else { Write-Fail 'M-2' ("exit=$($run.ExitCode) exists=$exists") }
    } catch {
        Write-Fail 'M-2' $_.Exception.Message
    } finally {
        Remove-M1407Tree $root
    }
}

function Test-M3 {
    $root = New-M1407Root
    $marker = Join-Path (Join-Path $root 'logs') 'apphost.down-on-purpose'
    try {
        $run = Invoke-M1407 -Root $root -Arguments @('-Root', $root, '-Clear', '-EnterIfAbsent')
        $ok = ($run.ExitCode -eq 2) -and
            (-not (Test-Path -LiteralPath $marker)) -and
            ($run.Text.IndexOf('-Clear and -EnterIfAbsent cannot be combined.', [System.StringComparison]::Ordinal) -ge 0)
        if ($ok) { Write-Pass 'M-3' }
        else { Write-Fail 'M-3' ("exit=$($run.ExitCode) marker=$(Test-Path -LiteralPath $marker)") }
    } catch {
        Write-Fail 'M-3' $_.Exception.Message
    } finally {
        Remove-M1407Tree $root
    }
}

function Test-M4 {
    $root = New-M1407Root
    $marker = Join-Path (Join-Path $root 'logs') 'apphost.down-on-purpose'
    $body = 'down-on-purpose keep'
    try {
        Set-Content -LiteralPath $marker -Value $body -Encoding ascii
        $run = Invoke-M1407 -Root $root -Arguments @('-Root', $root, '-EnterIfAbsent', '-Clear')
        $after = ''
        if (Test-Path -LiteralPath $marker) { $after = [System.IO.File]::ReadAllText($marker) }
        $ok = ($run.ExitCode -eq 2) -and (($after -eq ($body + "`n")) -or ($after -eq $body))
        if ($ok) { Write-Pass 'M-4' }
        else { Write-Fail 'M-4' ("exit=$($run.ExitCode) after=$after") }
    } catch {
        Write-Fail 'M-4' $_.Exception.Message
    } finally {
        Remove-M1407Tree $root
    }
}

$all = @('M1', 'M2', 'M3', 'M4')
if ($Case) {
    $name = $Case
    if ($name -match '^M-([0-9]+)$') { $name = 'M' + $Matches[1] }
    $fn = Get-Command -Name ('Test-{0}' -f $name) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($id in $all) { & ('Test-{0}' -f $id) }
}

Write-Host ''
Write-Host ('CARD-1407 maintenance: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ('  ' + $line) }
    Write-Host 'MAINTENANCE TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'MAINTENANCE TESTS EXIT CODE: 0  (PASS)'
exit 0
