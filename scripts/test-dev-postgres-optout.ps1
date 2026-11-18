#requires -Version 5.1
<#
.SYNOPSIS
    Runbook G-1 witness: ANTIPHON_SKIP_DEV_POSTGRES=1 keeps the desktop
    Postgres down across dev-aspire.ps1 and the logon auto-start.

    The compose bring-up runs against a fake docker that only records its
    argv. No Docker daemon, no container, no AppHost launch. Absent, empty,
    '0' and '1' are separate states (the variable is removed from the child
    environment for the absent case). ASCII-only.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path -Parent $here
$lib = Join-Path $here 'lib/dev-postgres-compose.ps1'
$compose = Join-Path $repo 'docker-compose.dev.yml'
$devAspire = Join-Path $repo 'dev-aspire.ps1'
$autostart = Join-Path $here 'autostart-apphost.ps1'
$script:Passed = 0
$script:Failed = 0
$script:VarName = 'ANTIPHON_SKIP_DEV_POSTGRES'

function Write-Pass {
    param([string]$Name)
    $script:Passed++
    Write-Host ('PASS ' + $Name)
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:Failed++
    Write-Host ('FAIL ' + $Name + ' - ' + $Detail)
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name } else { Write-Fail $Name $Detail }
}

function New-FakeDockerRoot {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('g1-devpg-' + [guid]::NewGuid().ToString('N'))
    $bin = Join-Path $root 'bin'
    New-Item -ItemType Directory -Path $bin -Force | Out-Null
    $marker = Join-Path $root 'docker-argv'
    if ($env:OS -eq 'Windows_NT') {
        $docker = Join-Path $bin 'docker.cmd'
        @('@echo off', 'echo %*>> "%G1_DOCKER_MARKER%"', 'exit /b 0') | Set-Content -LiteralPath $docker -Encoding ASCII
    } else {
        $docker = Join-Path $bin 'docker'
        @('#!/bin/sh', 'printf "%s\n" "$*" >> "$G1_DOCKER_MARKER"', 'exit 0') | Set-Content -LiteralPath $docker -Encoding ASCII
        [System.IO.File]::SetUnixFileMode($docker, [System.IO.UnixFileMode]([Convert]::ToInt32('755', 8)))
    }
    return @{ Root = $root; Bin = $bin; Marker = $marker; Docker = $docker }
}

# Runs the real lib in a child pwsh whose environment carries the opt-out
# variable as given ($null removes it), so the default read path is exercised.
function Invoke-ComposeChild {
    param($Fake, $Value)
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $probe = Join-Path $Fake.Root 'probe.ps1'
    @(
        '$ErrorActionPreference = ''Stop''',
        ('. ''' + $lib.Replace("'", "''") + ''''),
        ('$r = Invoke-DevComposeUp -ComposeFile ''' + $compose.Replace("'", "''") + ''' -Docker ''' + $Fake.Docker.Replace("'", "''") + ''''),
        'Write-Output (''EXIT='' + $r.Exit)',
        'Write-Output (''SKIP='' + $r.SkipPostgres)',
        'Write-Output (''RAN='' + $r.Ran)',
        'Write-Output (''MESSAGE='' + $r.Message)'
    ) | Set-Content -LiteralPath $probe -Encoding ASCII
    if (Test-Path -LiteralPath $Fake.Marker) { Remove-Item -LiteralPath $Fake.Marker -Force }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $pwsh
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-File', $probe)) { [void]$psi.ArgumentList.Add($arg) }
    $psi.Environment['G1_DOCKER_MARKER'] = $Fake.Marker
    if ($null -eq $Value) {
        [void]$psi.Environment.Remove($script:VarName)
    } else {
        $psi.Environment[$script:VarName] = $Value
    }
    $hasKey = $psi.Environment.ContainsKey($script:VarName)
    $proc = [System.Diagnostics.Process]::Start($psi)
    $out = $proc.StandardOutput.ReadToEnd()
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    $argv = ''
    if (Test-Path -LiteralPath $Fake.Marker) { $argv = ([System.IO.File]::ReadAllText($Fake.Marker)).Trim() }
    return @{ Exit = $proc.ExitCode; Out = $out; Err = $err; Argv = $argv; HasKey = $hasKey }
}

function Get-ScriptText {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    return [System.IO.File]::ReadAllText($Path)
}

function Invoke-Case {
    param([string]$Name)
    switch ($Name) {
        'skip-value' {
            Assert-True (Test-Path -LiteralPath $lib) 'opt-out lib exists' $lib
            if (-not (Test-Path -LiteralPath $lib)) { return }
            . $lib
            Assert-True ((Test-DevPostgresSkipped '1')) 'value 1 skips' 'not skipped'
            Assert-True ((Test-DevPostgresSkipped ' 1 ')) 'value 1 with spaces skips' 'not skipped'
            Assert-True (-not (Test-DevPostgresSkipped $null)) 'absent does not skip' 'skipped'
            Assert-True (-not (Test-DevPostgresSkipped '')) 'empty does not skip' 'skipped'
            Assert-True (-not (Test-DevPostgresSkipped '0')) 'value 0 does not skip' 'skipped'
            Assert-True (-not (Test-DevPostgresSkipped 'yes')) 'other text does not skip' 'skipped'
        }
        'services' {
            if (-not (Test-Path -LiteralPath $lib)) { Write-Fail 'services lib' 'missing'; return }
            . $lib
            $names = @(Get-DevComposeServices ([System.IO.File]::ReadAllText($compose)))
            Assert-True (($names -join ',') -eq 'postgres,redpanda') 'dev compose services are postgres and redpanda' ($names -join ',')
        }
        'arguments' {
            if (-not (Test-Path -LiteralPath $lib)) { Write-Fail 'arguments lib' 'missing'; return }
            . $lib
            $text = [System.IO.File]::ReadAllText($compose)
            $default = @(Get-DevComposeUpArguments -ComposeFile 'C:\r\docker-compose.dev.yml' -ComposeText $text -SkipPostgres $false)
            Assert-True (($default -join ' ') -eq 'compose -f C:\r\docker-compose.dev.yml up -d') 'default is the unchanged up -d of every service' ($default -join ' ')
            $skip = @(Get-DevComposeUpArguments -ComposeFile 'C:\r\docker-compose.dev.yml' -ComposeText $text -SkipPostgres $true)
            Assert-True (($skip -join ' ') -eq 'compose -f C:\r\docker-compose.dev.yml up -d --no-deps redpanda') 'opt-out brings up redpanda only, without deps' ($skip -join ' ')
            Assert-True ($skip -notcontains 'postgres') 'opt-out never names postgres' ($skip -join ' ')
            $only = @(Get-DevComposeUpArguments -ComposeFile 'f.yml' -ComposeText "services:`n  postgres:`n    image: postgres:16`n" -SkipPostgres $true)
            Assert-True ($only.Count -eq 0) 'opt-out with only postgres runs no compose' ($only -join ' ')
        }
        'invoke-absent' {
            if (-not (Test-Path -LiteralPath $lib)) { Write-Fail 'invoke-absent lib' 'missing'; return }
            $fake = New-FakeDockerRoot
            try {
                $r = Invoke-ComposeChild $fake $null
                Assert-True (-not $r.HasKey) 'absent: variable removed before the call' 'key present'
                Assert-True ($r.Exit -eq 0) 'absent: child exits 0' ($r.Out + $r.Err)
                Assert-True ($r.Argv -ceq ('compose -f ' + $compose + ' up -d')) 'absent: compose up -d of every service' $r.Argv
                Assert-True ($r.Out -match '(?m)^SKIP=False') 'absent: postgres not skipped' $r.Out
            } finally { Remove-Item -LiteralPath $fake.Root -Recurse -Force -ErrorAction SilentlyContinue }
        }
        'invoke-empty' {
            if (-not (Test-Path -LiteralPath $lib)) { Write-Fail 'invoke-empty lib' 'missing'; return }
            $fake = New-FakeDockerRoot
            try {
                foreach ($value in @('', '0')) {
                    $r = Invoke-ComposeChild $fake $value
                    Assert-True ($r.Argv -ceq ('compose -f ' + $compose + ' up -d')) ("value '" + $value + "': compose up -d of every service") $r.Argv
                    Assert-True ($r.Out -match '(?m)^SKIP=False') ("value '" + $value + "': postgres not skipped") $r.Out
                }
            } finally { Remove-Item -LiteralPath $fake.Root -Recurse -Force -ErrorAction SilentlyContinue }
        }
        'invoke-optout' {
            if (-not (Test-Path -LiteralPath $lib)) { Write-Fail 'invoke-optout lib' 'missing'; return }
            $fake = New-FakeDockerRoot
            try {
                $r = Invoke-ComposeChild $fake '1'
                Assert-True ($r.HasKey) 'opt-out: variable set before the call' 'key missing'
                Assert-True ($r.Exit -eq 0) 'opt-out: child exits 0' ($r.Out + $r.Err)
                Assert-True ($r.Argv -ceq ('compose -f ' + $compose + ' up -d --no-deps redpanda')) 'opt-out: compose brings up redpanda only' $r.Argv
                Assert-True ($r.Argv -notmatch 'postgres') 'opt-out: docker never sees postgres' $r.Argv
                Assert-True ($r.Out -match '(?m)^SKIP=True') 'opt-out: postgres skipped' $r.Out
                Assert-True ($r.Out -match 'ANTIPHON_SKIP_DEV_POSTGRES=1') 'opt-out: log line names the variable' $r.Out
                Assert-True ($r.Out -match 'desktop Postgres') 'opt-out: log line says the desktop Postgres is skipped' $r.Out
            } finally { Remove-Item -LiteralPath $fake.Root -Recurse -Force -ErrorAction SilentlyContinue }
        }
        'wiring' {
            $dev = Get-ScriptText $devAspire
            $auto = Get-ScriptText $autostart
            Assert-True ($dev.Contains("scripts\lib\dev-postgres-compose.ps1")) 'dev-aspire dot-sources the opt-out lib' 'missing'
            Assert-True ($dev.Contains('Invoke-DevComposeUp')) 'dev-aspire brings compose up through the lib' 'missing'
            Assert-True (-not $dev.Contains('docker compose -f "$root\docker-compose.dev.yml" up -d')) 'dev-aspire has no direct compose up -d' 'still present'
            Assert-True ($dev -match 'if \(\$devPostgresSkipped\)') 'dev-aspire skips the post-launch Postgres wait when opted out' 'missing'
            Assert-True ($auto.Contains("lib/dev-postgres-compose.ps1")) 'autostart dot-sources the opt-out lib' 'missing'
            Assert-True ($auto.Contains('Invoke-DevComposeUp')) 'autostart brings compose up through the lib' 'missing'
            Assert-True (-not $auto.Contains('docker compose -f $composeF up -d')) 'autostart has no direct compose up -d' 'still present'
            Assert-True ($auto -match 'if \(\$devPostgresSkipped\)') 'autostart skips the Postgres health wait when opted out' 'missing'
        }
        'ascii' {
            foreach ($path in @($lib, $autostart, $PSCommandPath)) {
                if (-not (Test-Path -LiteralPath $path)) { Write-Fail ('ascii ' + $path) 'missing'; continue }
                $high = @([System.IO.File]::ReadAllBytes($path) | Where-Object { $_ -gt 127 })
                Assert-True ($high.Count -eq 0) ('ascii ' + [System.IO.Path]::GetFileName($path)) ('non-ascii=' + $high.Count)
            }
        }
        default { Write-Fail $Name 'unknown case' }
    }
}

$cases = @('skip-value', 'services', 'arguments', 'invoke-absent', 'invoke-empty', 'invoke-optout', 'wiring', 'ascii')
$selected = @($cases)
if (-not [string]::IsNullOrWhiteSpace($Case)) { $selected = @($Case) }
foreach ($name in $selected) { Invoke-Case $name }
Write-Host ('RESULT passed=' + $script:Passed + ' failed=' + $script:Failed)
if ($script:Failed -gt 0) { exit 1 }
exit 0
