<#
.SYNOPSIS
    Long-lived supervisor wrapper for one Antiphon service.
    Started detached (UseShellExecute=true) by the AppHost - survives AppHost exit.
    Tracks the service PID, auto-restarts on crash, respects desired-state file.
.NOTES
    ExeArgs is passed as a single space-joined string (PowerShell -File mode drops extra
    values from [string[]] params, so we do the split ourselves).
    Service stdout+stderr is appended to LogFile via cmd.exe shell redirection so the
    Aspire log tailer can surface it in the dashboard console view.

    BuildProjectDir: when set, 'dotnet build' runs before each (re)launch and the daemon
    then runs the built EXE directly (Exe = the built exe path), NOT 'dotnet run'. This is
    required for the session-runner: 'dotnet run' wraps the app in a kill-on-close Job
    Object, and our detached pty-hosts (which deliberately break their parent chain) still
    get captured by that job and die when the runner is torn down. Running the exe directly
    removes that muxer job so pty-hosts survive a runner restart. Build-before-launch keeps
    the "soft restart picks up new code" behaviour 'dotnet run' gave us for free. ASCII-only.
#>
param(
    [string]$Name,
    [string]$WorkDir,
    [string]$Exe,
    [string]$ExeArgs,
    [string]$LogFile,
    [string]$ServicePidFile,
    [string]$StateFile,
    [string]$BuildProjectDir = ''
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
        Write-Log "Desired state is stopped - exiting."
        Remove-Item $ServicePidFile -ErrorAction SilentlyContinue
        exit 0
    }

    # Build before launch so a soft restart (kill the service; the loop relaunches) picks up
    # new code, exactly as 'dotnet run' did - but we then launch the built exe directly so no
    # kill-on-close muxer job captures the pty-hosts. The old service is already dead here, so
    # its exe is unlocked and the build can overwrite it.
    if ($BuildProjectDir) {
        Write-Log "Building $BuildProjectDir (Debug)..."
        $buildOut = & dotnet build $BuildProjectDir -c Debug --nologo 2>&1
        foreach ($l in $buildOut) {
            Add-Content -LiteralPath $LogFile -Value $l -Encoding UTF8 -ErrorAction SilentlyContinue
        }
        if ($LASTEXITCODE -ne 0) {
            Write-Log "[ERR] Build failed (exit $LASTEXITCODE). Retrying in 5 s..."
            Start-Sleep 5
            continue
        }
        Write-Log "Build succeeded."
    }

    Write-Log "Starting $Exe $($exeArgList -join ' ')"

    # cmd.exe /s /c handles .cmd shims (npm, npx) and >> redirection in detached processes.
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName        = 'cmd.exe'
    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute  = $false
    $psi.CreateNoWindow   = $true
    # /s /c strips outer quotes and runs as a shell command, so >> redirection works.
    # stdout+stderr are appended to the same log file the Aspire tailer reads.
    $innerCmd = if ($exeArgList.Count -gt 0) { "$Exe $($exeArgList -join ' ')" } else { $Exe }
    $psi.Arguments = "/s /c `"$innerCmd >> `"$LogFile`" 2>&1`""

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
        Write-Log "Desired state is '$desired' - stopping."
        exit 0
    }

    Write-Log "Restarting in 3 s..."
    Start-Sleep 3
}
