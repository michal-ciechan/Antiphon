#requires -Version 5.1
# CARD-0487 fixture helpers for B/E/R/M harnesses. ASCII-only.

if ($script:C487HarnessLoaded) { return }
$script:C487HarnessLoaded = $true

$script:C487Passed = 0
$script:C487Failed = 0
$script:C487Failures = @()
$script:C487Rows = 0

function Write-C487Pass {
    param([string]$Name)
    $script:C487Passed++
    $script:C487Rows++
    Write-Host ('PASS {0}' -f $Name)
}

function Write-C487Fail {
    param([string]$Name, [string]$Detail)
    $script:C487Failed++
    $script:C487Rows++
    $script:C487Failures += ('{0} : {1}' -f $Name, $Detail)
    Write-Host ('FAIL {0} - {1}' -f $Name, $Detail)
}

function Assert-C487 {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-C487Pass $Name }
    else { Write-C487Fail $Name $Detail }
}

function New-C487Root {
    param([string]$ResultsDirectory)
    if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        $ResultsDirectory = Join-Path $env:TEMP ('c487-' + [guid]::NewGuid().ToString('N'))
    }
    New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
    return (ConvertTo-NightlyCanonicalPath -Path $ResultsDirectory)
}

function New-C487OwnedClone {
    param([string]$Root, [string]$Origin = 'https://github.com/michal-ciechan/Antiphon')
    $clone = Join-Path $Root 'checkout'
    New-Item -ItemType Directory -Path (Join-Path $clone '.git') -Force | Out-Null
    $cfg = "[remote `"origin`"]`n`turl = $Origin`n"
    Set-Content -LiteralPath (Join-Path $clone (Join-Path '.git' 'config')) -Value $cfg -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $clone (Join-Path '.git' 'HEAD')) -Value 'ref: refs/heads/master' -Encoding ASCII
    Write-NightlyAtomicJson -Path (Join-Path $clone '.antiphon-nightly-owned') -Object ([ordered]@{ origin = $Origin; kind = 'nightly-clone' })
    $scripts = Join-Path $clone 'scripts'
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\nightly-run.ps1') -Destination (Join-Path $scripts 'nightly-run.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\nightly-tests.ps1') -Destination (Join-Path $scripts 'nightly-tests.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\nightly-report.ps1') -Destination (Join-Path $scripts 'nightly-report.ps1') -Force
    Copy-Item -LiteralPath $PSScriptRoot -Destination (Join-Path $scripts 'lib') -Recurse -Force
    return $clone
}

function Write-C487Seams {
    param(
        [string]$Path,
        [string]$TracePath,
        [string]$GitOutput = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
        [int]$GitExit = 0,
        [scriptblock]$Extra = $null,
        [string]$WaitGitSubcommand = '',
        [string]$WaitFile = ''
    )
    $extraText = ''
    if ($Extra) { $extraText = $Extra.ToString() }
    $wait = ''
    if ($WaitGitSubcommand) {
        $wait = @"
        if (`$Arguments -contains '$WaitGitSubcommand' -and '$WaitFile') {
            `$deadline = (Get-Date).AddSeconds(30)
            while (-not (Test-Path -LiteralPath '$WaitFile') -and (Get-Date) -lt `$deadline) { Start-Sleep -Milliseconds 40 }
        }
"@
    }
    $text = @"
`$NightlySeams = @{
    TracePath = '$TracePath'
    Git = {
        param(`$WorkingDirectory, `$Arguments)
        Add-Content -LiteralPath '$TracePath' -Value (('GIT {0}' -f (`$Arguments -join ' '))) -Encoding ASCII
        $wait
        return @{ ExitCode = $GitExit; Output = '$GitOutput' }
    }
    StartProcess = {
        param(`$FilePath, `$ArgumentList, `$WorkingDirectory, `$TimeoutMilliseconds, `$Environment)
        Add-Content -LiteralPath '$TracePath' -Value (('EXEC {0} {1}' -f `$FilePath, ((`$ArgumentList) -join ' '))) -Encoding ASCII
        return @{ ExitCode = 0; TimedOut = `$false; Pid = 2; ChildrenExited = `$true; LogText = 'C487_INVOCATION fake' }
    }
    IsProcessAlive = {
        param(`$ProcessId)
        return (`$ProcessId -gt 0)
    }
    DockerInfo = { return @{ exitCode = 0; version = 'fake'; detail = 'ok' } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
}
$extraText
"@
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Set-Content -LiteralPath $Path -Value $text -Encoding ASCII
}

function Invoke-C487NightlyRun {
    param([hashtable]$Args)
    return Invoke-AntiphonNightlyRun @Args
}

function Write-C487Evidence {
    param([string]$ResultsDirectory, [string]$Case, $Body)
    $path = Join-Path $ResultsDirectory ($Case + '.json')
    $Body | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
}

function Complete-C487Harness {
    param([string]$ResultsDirectory, [int]$ExpectedRows)
    Write-Host ''
    Write-Host ('C487: {0} passed, {1} failed, {2} rows' -f $script:C487Passed, $script:C487Failed, $script:C487Rows)
    if ($ExpectedRows -gt 0 -and $script:C487Rows -lt $ExpectedRows) {
        Write-Host ('ROW COUNT expected at least={0} actual={1}' -f $ExpectedRows, $script:C487Rows)
        if ($script:C487Failed -eq 0) { $script:C487Failed++ }
    }
    if ($script:C487Failed -gt 0 -or $script:C487Rows -eq 0) {
        foreach ($line in $script:C487Failures) { Write-Host ('  ' + $line) }
        Write-Host 'C487 HARNESS EXIT CODE: 1  (FAIL - do not report this run as green)'
        exit 1
    }
    Write-Host 'C487 HARNESS EXIT CODE: 0  (PASS)'
    exit 0
}

function Get-C487CaseFunctions {
    param([string]$Prefix)
    return @(Get-Command -Name ("Test-{0}*" -f $Prefix) -CommandType Function | Sort-Object Name)
}
