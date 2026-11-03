#requires -Version 5.1
<#
.SYNOPSIS
CARD-1667: start supervise-rc.ps1 detached from this process's console or session, then return.
.DESCRIPTION
Windows: the supervisor gets its own hidden console (Start-Process without -NoNewWindow), so a
teardown of the starter's console cannot kill it. Unix: setsid gives it a new session. Its
stdout/stderr go straight to its own files (supervisor-stdout.log, supervisor-stderr.log); this
starter holds no pipe or buffer for it, and its log is TaskRoot\supervisor.log, flushed per line.
Waits for supervisor-identity.json, writes start-supervisor.json and exits 0 (1 on failure).
RunId must be the -RunId in the launch record's nightlyArguments: the supervisor records it and
passes it to launch-rc.ps1 (ANTIPHON_QUALIFICATION_RUN_ID), and observe.ps1 credits only that run.
Refuses when TaskRoot already has a supervisor identity, or when StateRoot's last-run.json or
run.lock, or TaskRoot's own-last-run.json copy, already names RunId (a reused run id could credit
an earlier run). To leave a caller's Job Object too, start this script itself through
Win32_Process Create (docs: antiphon-rc-qualification skill).
ASCII-only, Windows PowerShell 5.1 safe.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$TaskRoot,
    [Parameter(Mandatory = $true)][string]$LaunchScript,
    [Parameter(Mandatory = $true)][string]$LogRoot,
    [Parameter(Mandatory = $true)][string]$StateRoot,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')][string]$RunId,
    [ValidateRange(1, 300)][int]$PollSeconds = 30,
    [ValidateRange(1, 120)][int]$ProgressMinutes = 10,
    [ValidateRange(1, 600)][double]$HangMinutes = 20,
    [ValidateRange(5, 600)][int]$IdentityTimeoutSeconds = 60,
    [string]$PowerShellPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../lib/qualification-supervisor.ps1')
$supervisor = Join-Path $PSScriptRoot 'supervise-rc.ps1'
$identityPath = Join-Path $TaskRoot 'supervisor-identity.json'
$platform = Get-QualificationHostPlatform
if ([string]::IsNullOrWhiteSpace($PowerShellPath)) { $PowerShellPath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName }
if (-not [IO.Directory]::Exists($TaskRoot)) { Write-Output "start-supervisor: TaskRoot does not exist: $TaskRoot"; exit 1 }
if (-not [IO.File]::Exists($LaunchScript)) { Write-Output "start-supervisor: LaunchScript does not exist: $LaunchScript"; exit 1 }
if ([IO.File]::Exists($identityPath)) { Write-Output "start-supervisor: refused, a supervisor identity already exists: $identityPath"; exit 1 }
foreach ($seenPath in @([IO.Path]::Combine($StateRoot, 'last-run.json'), [IO.Path]::Combine($StateRoot, 'run.lock'), [IO.Path]::Combine($TaskRoot, 'own-last-run.json'))) {
    $seen = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath($seenPath))
    if ($seen.status -ceq 'ok' -and [string]::Equals([string]$seen.record.runId, $RunId, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Output "start-supervisor: refused, $($seen.path) already names run id $RunId; bind a fresh -RunId"; exit 1
    }
}

$detach = 'setsid'
if ($platform -ceq 'Windows') { $detach = 'new-console' }
$arguments = @('-NoProfile', '-NonInteractive', '-File', $supervisor, '-TaskRoot', $TaskRoot, '-LaunchScript', $LaunchScript,
    '-LogRoot', $LogRoot, '-StateRoot', $StateRoot, '-RunId', $RunId, '-PollSeconds', [string]$PollSeconds, '-ProgressMinutes', [string]$ProgressMinutes,
    '-HangMinutes', $HangMinutes.ToString([Globalization.CultureInfo]::InvariantCulture), '-Detach', $detach, '-PowerShellPath', $PowerShellPath)
$start = New-QualificationDetachedStartParameters -FilePath $PowerShellPath -ArgumentList $arguments -Platform $platform `
    -StandardOutput (Join-Path $TaskRoot 'supervisor-stdout.log') -StandardError (Join-Path $TaskRoot 'supervisor-stderr.log')
$started = Start-Process @start
$deadline = [datetime]::UtcNow.AddSeconds($IdentityTimeoutSeconds)
$identity = $null
while ([datetime]::UtcNow -lt $deadline) {
    $read = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath($identityPath))
    if ($read.status -ceq 'ok') { $identity = $read.record; break }
    if ($started.HasExited -and $started.ExitCode -ne 0) { break }
    Start-Sleep -Milliseconds 200
}
$self = [Diagnostics.Process]::GetCurrentProcess()
$record = [ordered]@{
    starterPid = $self.Id
    starterStartedAt = $self.StartTime.ToUniversalTime().ToString('o')
    startedPid = $started.Id
    runId = $RunId
    platform = $platform
    detach = $detach
    supervisorPid = $null
    supervisorStartedAt = $null
    observation = 'identity-timeout'
}
if ($identity) {
    $record.supervisorPid = $identity.pid
    $record.supervisorStartedAt = [string]$identity.processStartedAt
    if ($identity.processStartedAt -is [datetime]) { $record.supervisorStartedAt = $identity.processStartedAt.ToUniversalTime().ToString('o') }
    $record.observation = (Get-QualificationProcessObservation $identity).status
}
$json = $record | ConvertTo-Json -Compress
Write-QualificationSupervisorRecord -Path (Join-Path $TaskRoot 'start-supervisor.json') -Text $json
Write-Output $json
if ($record.observation -ceq 'alive' -or ($identity -and [IO.File]::Exists((Join-Path $TaskRoot 'commission-exit.txt')))) { exit 0 }
exit 1
