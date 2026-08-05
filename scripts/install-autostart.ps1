<#
.SYNOPSIS
    Make the Antiphon backend always-on so the agents can run without launching the
    Aspire AppHost:

      1. PostgreSQL  - brings up the docker-compose.dev.yml container (restart:
                       unless-stopped). Combined with Docker Desktop "AutoStart",
                       it returns automatically on every login/boot.
      2. Session-runner - registers a per-user Scheduled Task that starts the
                       session-runner daemon (port 17204) at logon via
                       scripts/autostart-session-runner.ps1.
      3. AppHost      - registers a second per-user Scheduled Task that brings up the
                       Aspire AppHost (server 17202, client 17203, dashboard 17205,
                       control API 17207) at logon via scripts/autostart-apphost.ps1.
                       Skip it with -NoAppHost if you would rather start the app by
                       hand with dev-aspire.ps1.

    The AppHost task fires 1 minute after logon and waits for Docker Desktop before
    starting; it no-ops if the AppHost is already running, and it adopts the
    already-running Postgres + session-runner.
.PARAMETER Uninstall
    Remove both Scheduled Tasks. Leaves the Postgres container and its data alone
    (prints how to remove them if you want to).
.PARAMETER NoAppHost
    Do not register (or, with -Uninstall, do not remove) the AppHost logon task.
    Restores the old behaviour: only Postgres + session-runner are always-on.
.PARAMETER AppHostOnly
    Only touch the AppHost task; leave the session-runner task alone. Use this when the
    session-runner is already running - re-registering a RUNNING task terminates its
    live supervisor, which would leave the daemon unsupervised until the next logon.
.PARAMETER TaskName
    Session-runner Scheduled Task name. Default: "Antiphon Session Runner".
.PARAMETER AppHostTaskName
    AppHost Scheduled Task name. Default: "Antiphon AppHost".
.PARAMETER AppHostDelay
    ISO-8601 duration to delay the AppHost task after logon. Default: PT1M.
.EXAMPLE
    pwsh -File scripts/install-autostart.ps1
.EXAMPLE
    pwsh -File scripts/install-autostart.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$NoAppHost,
    [switch]$AppHostOnly,
    [string]$TaskName = 'Antiphon Session Runner',
    [string]$AppHostTaskName = 'Antiphon AppHost',
    [string]$AppHostDelay = 'PT1M'
)

$ErrorActionPreference = 'Stop'
$root            = Split-Path $PSScriptRoot -Parent
$composeFile     = Join-Path $root 'docker-compose.dev.yml'
$autostartScript = Join-Path $PSScriptRoot 'autostart-session-runner.ps1'

function Write-Step($msg) { Write-Host "`n> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  $msg" -ForegroundColor Green }
function Write-Note($msg) { Write-Host "  $msg" -ForegroundColor DarkGray }
function Write-Warn2($msg){ Write-Host "  $msg" -ForegroundColor Yellow }

# -- Uninstall path ----------------------------------------------------------
if ($Uninstall) {
    $toRemove = @()
    if (-not $AppHostOnly) { $toRemove += $TaskName }
    if (-not $NoAppHost)   { $toRemove += $AppHostTaskName }
    foreach ($t in $toRemove) {
        Write-Step "Removing Scheduled Task '$t'..."
        if (Get-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $t -Confirm:$false
            Write-Ok "Task removed."
        } else {
            Write-Note "No such task - nothing to do."
        }
    }
    Write-Note "Postgres container left running. To stop and remove it (data kept in the 'antiphon_pgdata' volume):"
    Write-Note "  docker compose -f `"$composeFile`" down"
    Write-Note "To also delete the data volume:  docker compose -f `"$composeFile`" down -v"
    return
}

# -- Pre-flight --------------------------------------------------------------
$appHostScript = Join-Path $PSScriptRoot 'autostart-apphost.ps1'
if (-not (Test-Path $autostartScript)) { throw "Missing $autostartScript" }
if (-not (Test-Path $composeFile))     { throw "Missing $composeFile" }
if (-not $NoAppHost -and -not (Test-Path $appHostScript)) { throw "Missing $appHostScript" }

# Resolve a PowerShell host for the task action (prefer pwsh 7, fall back to 5.1).
# Probe the real install dirs first - pwsh may not be on THIS session's PATH.
#
# NEVER bake in a version-pinned MSIX package path
# (C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.4.0_x64__...\pwsh.exe): pwsh is
# installed here as an MSIX, and Get-Command resolves to that versioned dir when this
# script itself runs under it. That path disappears on the next PowerShell update and
# the Scheduled Task silently stops working. The per-user WindowsApps app-exec alias
# ($env:LOCALAPPDATA\Microsoft\WindowsApps\pwsh.exe) is version-independent - prefer it.
$psExe = @(
    "$env:ProgramFiles\PowerShell\7\pwsh.exe",
    "${env:ProgramFiles(x86)}\PowerShell\7\pwsh.exe",
    "$env:LOCALAPPDATA\Microsoft\WindowsApps\pwsh.exe",
    (Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source
) | Where-Object { $_ -and (Test-Path $_) -and $_ -notmatch 'WindowsApps\\Microsoft\.PowerShell_' } |
    Select-Object -First 1
if (-not $psExe) { $psExe = (Get-Command powershell.exe).Source }
Write-Note "PowerShell host for task: $psExe"

# -- 1. PostgreSQL container -------------------------------------------------
Write-Step "Bringing up always-on PostgreSQL (docker-compose.dev.yml)..."
docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warn2 "Docker Desktop is not running - start it, then re-run this script."
    Write-Warn2 "(Postgres setup skipped; the Scheduled Task will still be registered.)"
} else {
    docker compose -f $composeFile up -d
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed." }
    Write-Ok "Postgres container 'antiphon-postgres' is up (restart: unless-stopped)."

    # Confirm Docker Desktop will auto-start at login (so the container returns on boot).
    $autoStart = $null
    foreach ($f in @("$env:APPDATA\Docker\settings-store.json", "$env:APPDATA\Docker\settings.json")) {
        if (Test-Path $f) {
            try { $autoStart = (Get-Content $f -Raw | ConvertFrom-Json).AutoStart } catch {}
            if ($null -ne $autoStart) { break }
        }
    }
    if ($autoStart -eq $true) {
        Write-Ok "Docker Desktop AutoStart is ON - Postgres will return on login."
    } else {
        Write-Warn2 "Docker Desktop 'Start Docker Desktop when you log in' appears OFF."
        Write-Warn2 "Enable it (Settings -> General) so Postgres auto-starts on boot."
    }
}

# -- 2. Session-runner logon Scheduled Task ----------------------------------
# NOTE: re-registering this task while it is RUNNING terminates the live supervisor
# (Unregister kills the running instance), leaving the session-runner unsupervised
# until the next logon. Use -AppHostOnly to add/refresh the AppHost task without
# touching a healthy, running session-runner.
if ($AppHostOnly) {
    Write-Step "Skipping session-runner task (-AppHostOnly)."
    Write-Note "Existing '$TaskName' left untouched."
} else {

Write-Step "Registering logon Scheduled Task '$TaskName'..."

$action = New-ScheduledTaskAction `
    -Execute $psExe `
    -Argument "-NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$autostartScript`"" `
    -WorkingDirectory $root

$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"

# Interactive + Limited: runs in YOUR session with YOUR PATH/profile (needed so the
# session-runner can spawn cl.bat / pwsh / cx.ps1 agents). No admin rights required.
$principal = New-ScheduledTaskPrincipal `
    -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType Interactive `
    -RunLevel Limited

# No time limit (long-lived daemon); restart as a backstop if the supervisor dies.
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# Idempotent: replace any existing task of the same name.
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description 'Starts the Antiphon session-runner daemon (port 17204) at logon so agents can run.' | Out-Null

Write-Ok "Task registered (runs at logon as $env:USERNAME)."

}   # end -not $AppHostOnly

# -- 3. AppHost logon Scheduled Task -----------------------------------------
if (-not $NoAppHost) {
    Write-Step "Registering logon Scheduled Task '$AppHostTaskName'..."

    $ahAction = New-ScheduledTaskAction `
        -Execute $psExe `
        -Argument "-NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$appHostScript`"" `
        -WorkingDirectory $root

    # Delay after logon so Docker Desktop gets a head start (the script also waits
    # for Docker itself, so this is just to avoid burning that wait every boot).
    $ahTrigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
    $ahTrigger.Delay = $AppHostDelay

    $ahPrincipal = New-ScheduledTaskPrincipal `
        -UserId "$env:USERDOMAIN\$env:USERNAME" `
        -LogonType Interactive `
        -RunLevel Limited

    # One-shot launcher (exits once the backend is healthy), so a finite time limit is
    # right here - unlike the session-runner supervisor, which blocks forever.
    $ahSettings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -ExecutionTimeLimit (New-TimeSpan -Hours 1) `
        -RestartCount 2 `
        -RestartInterval (New-TimeSpan -Minutes 5)

    if (Get-ScheduledTask -TaskName $AppHostTaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $AppHostTaskName -Confirm:$false
    }

    Register-ScheduledTask `
        -TaskName $AppHostTaskName `
        -Action $ahAction `
        -Trigger $ahTrigger `
        -Principal $ahPrincipal `
        -Settings $ahSettings `
        -Description 'Starts the Antiphon Aspire AppHost (server 17202 / client 17203 / dashboard 17205) at logon, after waiting for Docker Desktop.' | Out-Null

    Write-Ok "Task registered (runs $AppHostDelay after logon as $env:USERNAME)."
} else {
    Write-Step "Skipping AppHost logon task (-NoAppHost)."
    Write-Note "Start the app by hand with:  .\dev-aspire.ps1"
}

# -- Done --------------------------------------------------------------------
Write-Host ""
Write-Host "Always-on backend configured:" -ForegroundColor Green
Write-Note "  Postgres       : docker container 'antiphon-postgres'  (localhost:17280)"
Write-Note "  Session-runner : Scheduled Task '$TaskName'            (http://localhost:17204)"
if (-not $NoAppHost) {
    Write-Note "  AppHost        : Scheduled Task '$AppHostTaskName'  (server :17202, client :17203, dashboard :17205)"
} else {
    Write-Note "  The rest (server/client/dashboard): run  .\dev-aspire.ps1"
}
Write-Host ""
Write-Note "Start the session-runner now without logging out:  Start-ScheduledTask -TaskName `"$TaskName`""
if (-not $NoAppHost) {
    Write-Note "Start the AppHost now without logging out:          Start-ScheduledTask -TaskName `"$AppHostTaskName`""
    Write-Note "AppHost auto-start log:                             $root\logs\autostart-apphost.log"
}
Write-Note "Remove auto-start later:                            pwsh -File scripts/install-autostart.ps1 -Uninstall"
