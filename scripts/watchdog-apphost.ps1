#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts watchdog-apphost` (scripts/lib/Antiphon.Nightly/AppHostWatchdog.cs): the
    health-triggered desktop AppHost watchdog. One fire observes the outage, skips maintenance and in-flight launches, probes
    17202/health and 17203 three times, and after three down rounds (Docker answering, no cooldown, under the flap cap)
    runs restart-apphost in process, preserving the session runner on 17204.

    Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because the per-user Scheduled Task
    "Antiphon AppHost Watchdog" (scripts/install-autostart.ps1) starts this file by path every two minutes. Delete it when
    that registration names the command. Agents run `scripts/antiphon watchdog-apphost --probe-only`; Windows runbooks
    `scripts\antiphon.cmd watchdog-apphost --probe-only`.

    The parameters are the script's, passed when given: -ProbeOnly, -CooldownMinutes, -MaxRestartsPerWindow,
    -IntervalSeconds, -LockMaxAgeMinutes, -ProbeTimeoutSec, -HealthUrl, -ClientUrl. -RestartScript is refused (exit 2): the
    restart runs in the command's process, so there is no script to replace. The command's exit code is returned; 9 when
    the CLI cannot be built or started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [switch]$ProbeOnly,
    [int]$CooldownMinutes,
    [int]$MaxRestartsPerWindow = 3,
    [int]$IntervalSeconds = 15,
    [int]$LockMaxAgeMinutes,
    [int]$ProbeTimeoutSec = 5,
    [string]$HealthUrl = 'http://localhost:17202/health',
    [string]$ClientUrl = 'http://localhost:17203/',
    [string]$RestartScript
)

$ErrorActionPreference = 'Continue'
if ($RestartScript) {
    [Console]::Error.WriteLine('watchdog-apphost: -RestartScript is not supported: restart-apphost runs in the command''s process.')
    exit 2
}
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($ProbeOnly) { $arguments += '--probe-only' }
foreach ($pair in @(
        @('CooldownMinutes', '--cooldown-minutes'), @('MaxRestartsPerWindow', '--max-restarts-per-window'), @('IntervalSeconds', '--interval-seconds'),
        @('LockMaxAgeMinutes', '--lock-max-age-minutes'), @('ProbeTimeoutSec', '--probe-timeout-sec'), @('HealthUrl', '--health-url'), @('ClientUrl', '--client-url'))) {
    if ($PSBoundParameters.ContainsKey($pair[0])) { $arguments += @($pair[1], [string]$PSBoundParameters[$pair[0]]) }
}
exit (Invoke-AntiphonCli -Command 'watchdog-apphost' -Arguments $arguments)
