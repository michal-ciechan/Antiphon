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

    Log rotation (CARD-0043): LogFile had NO retention - logs/fake-gateway.log reached 57 MB
    unrolled. It is rotated HERE, at (re)launch, and nowhere else, because the service's
    stdout is attached to it by a cmd.exe '>>' redirection that holds the handle open for the
    whole service lifetime: the file can only be renamed while no service is running. Rolls
    are pruned by age (LogRetainDays) and count (LogRetainCount), so worst case on disk is
    LogMaxMb x (LogRetainCount + 1) per daemon. Cutting the WRITE RATE is the other half and
    lives with each service (fake-gateway appsettings.json); rotation alone would still lose
    a day of history to a noisy service.
#>
param(
    [string]$Name,
    [string]$WorkDir,
    [string]$Exe,
    [string]$ExeArgs,
    [string]$LogFile,
    [string]$ServicePidFile,
    [string]$StateFile,
    [string]$BuildProjectDir = '',
    [int]$LogMaxMb = 20,
    [int]$LogRetainDays = 5,
    [int]$LogRetainCount = 10
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

# Roll $LogFile aside when it is over the cap, then prune old rolls. Only safe here, between
# service runs: while a service is up, cmd.exe holds the log handle for the '>>' redirection.
function Invoke-LogRotation {
    if ($LogMaxMb -le 0) { return }
    try {
        $item = Get-Item -LiteralPath $LogFile -ErrorAction SilentlyContinue
        if ($item -and $item.Length -gt ($LogMaxMb * 1MB)) {
            $dir     = Split-Path $LogFile -Parent
            $base    = [System.IO.Path]::GetFileNameWithoutExtension($LogFile)
            $ext     = [System.IO.Path]::GetExtension($LogFile)
            $stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
            $rolled  = Join-Path $dir "$base.$stamp$ext"
            Move-Item -LiteralPath $LogFile -Destination $rolled -Force -ErrorAction Stop
            Write-Log ("Rotated log at {0:N1} MB -> {1}" -f ($item.Length / 1MB), (Split-Path $rolled -Leaf))
        }
    } catch {
        # A locked or vanished log must never stop the service from starting.
        Write-Log "[WRN] Log rotation skipped: $_"
        return
    }

    try {
        $dir  = Split-Path $LogFile -Parent
        $base = [System.IO.Path]::GetFileNameWithoutExtension($LogFile)
        $ext  = [System.IO.Path]::GetExtension($LogFile)
        $rolls = @(Get-ChildItem -LiteralPath $dir -Filter "$base.*$ext" -File -ErrorAction SilentlyContinue |
                   Where-Object { $_.BaseName -match "^$([regex]::Escape($base))\.\d{8}-\d{6}$" } |
                   Sort-Object LastWriteTime -Descending)
        $cutoff = (Get-Date).AddDays(-$LogRetainDays)
        for ($i = 0; $i -lt $rolls.Count; $i++) {
            if ($i -ge $LogRetainCount -or $rolls[$i].LastWriteTime -lt $cutoff) {
                Remove-Item -LiteralPath $rolls[$i].FullName -Force -ErrorAction SilentlyContinue
            }
        }
    } catch {
        Write-Log "[WRN] Log prune skipped: $_"
    }
}

Write-Log "Supervisor started (PID $PID)"

while ($true) {
    $desired = Get-DesiredState
    if ($desired -eq 'stopped') {
        Write-Log "Desired state is stopped - exiting."
        Remove-Item $ServicePidFile -ErrorAction SilentlyContinue
        exit 0
    }

    Invoke-LogRotation

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
