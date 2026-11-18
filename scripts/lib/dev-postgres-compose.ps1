#requires -Version 5.1
<#
.SYNOPSIS
    Runbook G-1: the docker-compose.dev.yml bring-up shared by dev-aspire.ps1 and
    scripts/autostart-apphost.ps1, with an opt-out for the desktop Postgres.

    Default (ANTIPHON_SKIP_DEV_POSTGRES absent, empty or anything but 1): the
    unchanged `docker compose -f <file> up -d` of every service.
    ANTIPHON_SKIP_DEV_POSTGRES=1 (after the database moved to server2): bring up
    every other service with --no-deps and never start the postgres service, so a
    restart cannot revive the stopped desktop copy. Set it in the process or user
    environment; only the process environment is read.
    Dot-source only. ASCII-only: Windows PowerShell 5.1 runs the logon auto-start.
#>

$script:DevPostgresSkipVariable = 'ANTIPHON_SKIP_DEV_POSTGRES'
$script:DevPostgresService = 'postgres'

function Get-DevPostgresSkipValue {
    return [Environment]::GetEnvironmentVariable($script:DevPostgresSkipVariable)
}

function Test-DevPostgresSkipped {
    param([AllowNull()][string]$Value)
    if ($null -eq $Value) { return $false }
    return ($Value.Trim() -eq '1')
}

# Top-level service keys of a compose file: the two-space keys under `services:`.
function Get-DevComposeServices {
    param([string]$ComposeText)
    $names = New-Object System.Collections.Generic.List[string]
    $inServices = $false
    foreach ($line in @($ComposeText -split "`r?`n")) {
        if ($line -match '^services:\s*$') { $inServices = $true; continue }
        if ($line -match '^[^\s#]') { $inServices = $false; continue }
        if ($inServices -and $line -match '^  ([A-Za-z0-9][A-Za-z0-9_.-]*):\s*$') {
            [void]$names.Add($Matches[1])
        }
    }
    return $names.ToArray()
}

function Get-DevComposeUpArguments {
    param([string]$ComposeFile, [string]$ComposeText, [bool]$SkipPostgres)
    $base = @('compose', '-f', $ComposeFile, 'up', '-d')
    if (-not $SkipPostgres) { return $base }
    $others = @(Get-DevComposeServices $ComposeText | Where-Object { $_ -ne $script:DevPostgresService })
    if ($others.Count -eq 0) { return @() }
    return ($base + @('--no-deps') + $others)
}

function Invoke-DevComposeUp {
    param(
        [string]$ComposeFile,
        [string]$Docker = 'docker',
        [AllowNull()][string]$SkipValue = (Get-DevPostgresSkipValue)
    )
    $skip = Test-DevPostgresSkipped $SkipValue
    $text = ''
    if ($skip -and (Test-Path -LiteralPath $ComposeFile)) { $text = [System.IO.File]::ReadAllText($ComposeFile) }
    $arguments = @(Get-DevComposeUpArguments -ComposeFile $ComposeFile -ComposeText $text -SkipPostgres $skip)
    $message = ''
    if ($skip) {
        $started = @($arguments | Select-Object -Skip 6)
        $what = 'no other service'
        if ($started.Count -gt 0) { $what = ($started -join ', ') + ' only' }
        $message = $script:DevPostgresSkipVariable + '=1: desktop Postgres bring-up skipped (database moved off-host); compose up -d --no-deps ' + $what
    }
    if ($arguments.Count -eq 0) {
        return [pscustomobject]@{ Exit = 0; SkipPostgres = $skip; Ran = $false; Arguments = $arguments; Message = $message }
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Docker @arguments 2>&1 | Out-Null
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    if ($null -eq $code) { $code = 1 }
    return [pscustomobject]@{ Exit = [int]$code; SkipPostgres = $skip; Ran = $true; Arguments = $arguments; Message = $message }
}
