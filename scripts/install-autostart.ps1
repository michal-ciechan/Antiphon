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

    The .NET server, React client and Aspire dashboard are intentionally NOT
    auto-started - launch those with dev-aspire.ps1 when you sit down to work. The
    AppHost adopts the already-running Postgres + session-runner.
.PARAMETER Uninstall
    Remove the Scheduled Task. Leaves the Postgres container and its data alone
    (prints how to remove them if you want to).
.PARAMETER TaskName
    Scheduled Task name. Default: "Antiphon Session Runner".
.EXAMPLE
    pwsh -File scripts/install-autostart.ps1
.EXAMPLE
    pwsh -File scripts/install-autostart.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [string]$TaskName = 'Antiphon Session Runner'
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
    Write-Step "Removing Scheduled Task '$TaskName'..."
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Ok "Task removed."
    } else {
        Write-Note "No such task - nothing to do."
    }
    Write-Note "Postgres container left running. To stop and remove it (data kept in the 'antiphon_pgdata' volume):"
    Write-Note "  docker compose -f `"$composeFile`" down"
    Write-Note "To also delete the data volume:  docker compose -f `"$composeFile`" down -v"
    return
}

# -- Pre-flight --------------------------------------------------------------
if (-not (Test-Path $autostartScript)) { throw "Missing $autostartScript" }
if (-not (Test-Path $composeFile))     { throw "Missing $composeFile" }

# Resolve a PowerShell host for the task action (prefer pwsh 7, fall back to 5.1).
# Probe the real install dirs first - pwsh may not be on THIS session's PATH, and
# the real exe is more robust for Scheduled Tasks than the WindowsApps app-exec alias.
$psExe = @(
    "$env:ProgramFiles\PowerShell\7\pwsh.exe",
    "${env:ProgramFiles(x86)}\PowerShell\7\pwsh.exe",
    (Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Microsoft\WindowsApps\pwsh.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
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

# -- Done --------------------------------------------------------------------
Write-Host ""
Write-Host "Always-on backend configured:" -ForegroundColor Green
Write-Note "  Postgres       : docker container 'antiphon-postgres'  (localhost:17280)"
Write-Note "  Session-runner : Scheduled Task '$TaskName'            (http://localhost:17204)"
Write-Note "  The rest (server/client/dashboard): run  .\dev-aspire.ps1"
Write-Host ""
Write-Note "Start the session-runner now without logging out:  Start-ScheduledTask -TaskName `"$TaskName`""
Write-Note "Remove auto-start later:                            pwsh -File scripts/install-autostart.ps1 -Uninstall"
