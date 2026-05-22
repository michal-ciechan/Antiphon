<#
.SYNOPSIS
    Long-lived supervisor wrapper for one Antiphon service.
    Started detached (UseShellExecute=true) by the AppHost — survives AppHost exit.
    Tracks the service PID, auto-restarts on crash, respects desired-state file.
.NOTES
    ExeArgs is passed as a single space-joined string (PowerShell -File mode drops extra
    values from [string[]] params, so we do the split ourselves).
    Output is not captured here — service stdout/stderr is discarded (no TTY available
    in detached mode). Use `dotnet run` or `npm run dev` directly for interactive output.
#>
param(
    [string]$Name,
    [string]$WorkDir,
    [string]$Exe,
    [string]$ExeArgs,
    [string]$LogFile,
    [string]$ServicePidFile,
    [string]$StateFile
)

$ErrorActionPreference = 'Continue'

# Split the space-joined ExeArgs back into an array
$exeArgList = if ($ExeArgs) { @($ExeArgs -split '\s+') } else { @() }

function Write-Log([string]$msg) {
    $line = "$(Get-Date -Format 'HH:mm:ss') [$Name] $msg"
    try { Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue } catch {}
}

# Ensure directories exist
foreach ($f in @($LogFile, $ServicePidFile, $StateFile)) {
    $d = Split-Path $f -Parent
    if ($d -and -not (Test-Path $d)) { New-Item -ItemType Directory -Force $d | Out-Null }
}

function Get-DesiredState {
    try { (Get-Content -LiteralPath $StateFile -Raw -ErrorAction SilentlyContinue).Trim().ToLower() } catch { 'running' }
}

Write-Log "Supervisor started (PID $PID)"

while ($true) {
    $desired = Get-DesiredState
    if ($desired -eq 'stopped') {
        Write-Log "Desired state is stopped — exiting."
        Remove-Item $ServicePidFile -ErrorAction SilentlyContinue
        exit 0
    }

    Write-Log "Starting $Exe $($exeArgList -join ' ')"

    # Always use cmd.exe /c so .cmd shims (npm, npx) work in detached processes.
    # UseShellExecute=false + no redirection means service output goes to null
    # (supervisor has no console — started hidden by AppHost).
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName        = 'cmd.exe'
    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute  = $false
    $psi.CreateNoWindow   = $true
    $psi.ArgumentList.Add('/c')
    $psi.ArgumentList.Add($Exe)
    foreach ($a in $exeArgList) { $psi.ArgumentList.Add($a) }

    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        if ($null -eq $proc) { throw "Process.Start returned null" }
        $proc.Id | Set-Content -LiteralPath $ServicePidFile -Encoding UTF8
        Write-Log "Service PID $($proc.Id)"

        $proc.WaitForExit()
        $code = $proc.ExitCode
        Write-Log "Exited (code $code)"
    } catch {
        Write-Log "[ERR] Failed to start: $_"
    } finally {
        Remove-Item $ServicePidFile -ErrorAction SilentlyContinue
    }

    $desired = Get-DesiredState
    if ($desired -ne 'running') {
        Write-Log "Desired state is '$desired' — stopping."
        exit 0
    }

    Write-Log "Restarting in 3 s..."
    Start-Sleep 3
}
