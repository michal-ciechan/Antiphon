<#
.SYNOPSIS
    Shared helpers for the AppHost launch/restart locks and for turning a failed
    launch in logs/apphost.log into an operator-facing verdict.

    Dot-sourced by scripts/restart-apphost.ps1 and scripts/watchdog-apphost.ps1.

    LOCK FORMAT (CARD-0011, extended by CARD-0075): a single line of
        "<pid> <utc-iso-8601-roundtrip>"
    which is exactly what dev-aspire.ps1 writes into logs/apphost.launch.lock.
    Two locks now use it:
        logs/apphost.launch.lock   - written by dev-aspire.ps1 while it launches
        logs/apphost.restart.lock  - written by restart-apphost.ps1 while it
                                     tears the stack down and waits for health
    A lock is IGNORED (stale) when the recorded pid is gone or the stamp is older
    than LockMaxAgeMinutes (15, the watchdog's number - there is one, not two).
.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which
    reads no-BOM .ps1 as CP1252 and mangles non-ASCII characters into parse
    errors.
#>

function ConvertTo-UtcStamp {
    <#
      Lock stamps are UTC. DateTime.Parse of a Z-suffix ISO string WITHOUT an
      offset-preserving style converts the clock face to local and sets Kind=Local.
      Subtracting that from [datetime]::UtcNow then undercounts age by the local
      offset (BST +1h in August) - CARD-0152, a lock genuinely ~70 min old read
      as ~1 min old. DateTimeOffset keeps the offset; offset-less text is UTC
      by the lock format, never local.
    #>
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try {
        $invariant = [cultureinfo]::InvariantCulture
        if ($Text -match '(Z|[+-]\d{2}:\d{2})$') {
            return [datetimeoffset]::Parse($Text, $invariant).UtcDateTime
        }
        $styles = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor `
                  [System.Globalization.DateTimeStyles]::AdjustToUniversal
        return [datetime]::Parse($Text, $invariant, $styles)
    } catch {
        return $null
    }
}

function Get-UtcAgeMinutes {
    <#
      Age of a UTC clock-face stamp against UtcNow (or a caller-supplied NowUtc).
      Unspecified Kind is UTC (lock format). Local Kind is converted. Never
      subtract a DateTime from [datetime]::Now / Get-Date.
    #>
    param(
        [Parameter(Mandatory = $true)][datetime]$StampUtc,
        [datetime]$NowUtc = [datetime]::UtcNow
    )
    if ($StampUtc.Kind -eq [datetimekind]::Unspecified) {
        $stamp = [datetime]::SpecifyKind($StampUtc, [datetimekind]::Utc)
    } else {
        $stamp = $StampUtc.ToUniversalTime()
    }
    $now = if ($NowUtc.Kind -eq [datetimekind]::Unspecified) {
        [datetime]::SpecifyKind($NowUtc, [datetimekind]::Utc)
    } else {
        $NowUtc.ToUniversalTime()
    }
    return ([datetimeoffset]::new($now, [timespan]::Zero) - [datetimeoffset]::new($stamp, [timespan]::Zero)).TotalMinutes
}

function Format-AppHostLockStamp {
    param($Lock)
    if ($Lock.StampRaw) { return [string]$Lock.StampRaw }
    if ($Lock.StampUtc) { return $Lock.StampUtc.ToUniversalTime().ToString('o') }
    return 'stamp missing'
}

function Format-AppHostLockAge {
    param($Lock)
    if ($null -ne $Lock.AgeMinutes) { return ('{0:N1} min old' -f $Lock.AgeMinutes) }
    return 'age unknown'
}

function Test-ProcessAlive {
    param([int]$ProcessId)
    if (-not $ProcessId) { return $false }
    try {
        $null = Get-Process -Id $ProcessId -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Get-AppHostLock {
    <#
      Reads a lock file without judging it. Never throws: an unreadable or
      half-written lock reports Readable=$false and falls back to the file's
      LastWriteTimeUtc for the age, which is what the watchdog has always done.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [datetime]$NowUtc = [datetime]::UtcNow
    )

    $info = [pscustomobject]@{
        Path        = $Path
        Exists      = $false
        Readable    = $false
        ProcessId   = $null
        StampRaw    = $null
        StampUtc    = $null
        AgeMinutes  = $null
        HolderAlive = $false
    }
    if (-not (Test-Path -LiteralPath $Path)) { return $info }
    $info.Exists = $true

    $raw = $null
    try { $raw = (Get-Content -LiteralPath $Path -Raw -ErrorAction Stop) } catch { }
    if ($raw) { $raw = $raw.Trim() }
    if ($raw -and $raw -match '^(\d+)\s+(\S+)') {
        $info.Readable  = $true
        $info.ProcessId = [int]$Matches[1]
        $info.StampRaw  = $Matches[2]
        $info.StampUtc  = ConvertTo-UtcStamp $Matches[2]
    }

    if (-not $info.StampUtc) {
        try { $info.StampUtc = (Get-Item -LiteralPath $Path -ErrorAction Stop).LastWriteTimeUtc } catch { }
    }
    if ($info.StampUtc) {
        $info.AgeMinutes = Get-UtcAgeMinutes -StampUtc $info.StampUtc -NowUtc $NowUtc
    }

    # An unparseable pid is treated as alive: freshness alone then decides, which
    # is strictly the conservative reading (do not kill what might be launching).
    if ($info.ProcessId) {
        $info.HolderAlive = Test-ProcessAlive $info.ProcessId
    } else {
        $info.HolderAlive = $true
    }
    return $info
}

function Test-AppHostLockActive {
    <#
      Returns $null when the lock may be ignored (absent, holder dead, or older
      than MaxAgeMinutes), otherwise a human-readable reason naming the holder.
      The reason string is the operator-facing message on a refusal, so it has to
      carry the pid and the age.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$MaxAgeMinutes = 15,
        [string]$Label,
        [datetime]$NowUtc = [datetime]::UtcNow
    )
    if (-not $Label) { $Label = Split-Path -Leaf $Path }

    $lock = Get-AppHostLock -Path $Path -NowUtc $NowUtc
    if (-not $lock.Exists) { return $null }

    if ($lock.ProcessId -and -not $lock.HolderAlive) {
        return $null   # holder is gone; the file is litter
    }
    if ($null -ne $lock.AgeMinutes -and $lock.AgeMinutes -ge $MaxAgeMinutes) {
        return $null   # older than any real launch/restart; treat as abandoned
    }

    $stamp = Format-AppHostLockStamp $lock
    $age = Format-AppHostLockAge $lock
    if ($lock.ProcessId) {
        return ("{0} held by PID {1} (stamp {2}, {3}; {4})" -f $Label, $lock.ProcessId, $stamp, $age, $Path)
    }
    return ("{0} present but unreadable (stamp {1}, {2}; {3})" -f $Label, $stamp, $age, $Path)
}

function New-AppHostLock {
    <#
      Atomically takes a lock. CreateNew is the whole point: two restarts racing
      into the same millisecond both see "no lock" if you test-then-write, and
      the loser of that race is exactly the invocation this card exists to stop.
      The returned handle is held open (FileShare.Read) for the lifetime of the
      run so nothing can delete a live lock; pass the result to Remove-AppHostLock.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$MaxAgeMinutes = 15,
        [string]$Label
    )
    if (-not $Label) { $Label = Split-Path -Leaf $Path }

    $dir = Split-Path -Parent $Path
    if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }

    foreach ($attempt in 1, 2) {
        $stream = $null
        try {
            $stream = [System.IO.File]::Open(
                $Path,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::Read)
        } catch {
            $held = Test-AppHostLockActive -Path $Path -MaxAgeMinutes $MaxAgeMinutes -Label $Label
            if ($held) {
                return [pscustomobject]@{ Acquired = $false; Path = $Path; Reason = $held; Stream = $null }
            }
            if ($attempt -eq 1) {
                try {
                    Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
                } catch {
                    return [pscustomobject]@{
                        Acquired = $false; Path = $Path; Stream = $null
                        Reason   = ("{0} exists and could not be cleared ({1}; {2})" -f $Label, $_.Exception.Message, $Path)
                    }
                }
                continue
            }
            return [pscustomobject]@{
                Acquired = $false; Path = $Path; Stream = $null
                Reason   = ("{0} could not be taken ({1}; {2})" -f $Label, $_.Exception.Message, $Path)
            }
        }

        try {
            $text  = '{0} {1}' -f $PID, [datetime]::UtcNow.ToString('o')
            $bytes = [System.Text.Encoding]::ASCII.GetBytes($text)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
        } catch {
            try { $stream.Dispose() } catch { }
            try { Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue } catch { }
            return [pscustomobject]@{
                Acquired = $false; Path = $Path; Stream = $null
                Reason   = ("{0} could not be written ({1}; {2})" -f $Label, $_.Exception.Message, $Path)
            }
        }
        return [pscustomobject]@{ Acquired = $true; Path = $Path; Reason = $null; Stream = $stream }
    }
}

function Remove-AppHostLock {
    param($Lock)
    if (-not $Lock -or -not $Lock.Acquired) { return }
    if ($Lock.Stream) { try { $Lock.Stream.Dispose() } catch { } }
    try { Remove-Item -LiteralPath $Lock.Path -Force -ErrorAction SilentlyContinue } catch { }
}

function Invoke-BoundedCommand {
    <#
      Runs a scriptblock in a background job with a hard deadline. Used for the
      docker probe: if docker is the thing that is stalling, an unbounded
      "docker ps" in the diagnostic path would hang the diagnosis too.
    #>
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Script,
        [int]$TimeoutSec = 10
    )
    $job = Start-Job -ScriptBlock $Script
    $done = $job | Wait-Job -Timeout $TimeoutSec
    if (-not $done) {
        Remove-Job $job -Force -ErrorAction SilentlyContinue
        return [pscustomobject]@{ TimedOut = $true; Output = $null }
    }
    $out = Receive-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ TimedOut = $false; Output = $out }
}

function Get-DockerVerdict {
    <#
      What docker ACTUALLY said, which is the fact the Aspire exception omits.
      A slow or absent answer here is the positive evidence that the DCP probe
      stalled on docker - the runtime the truncated message never names.
    #>
    param([int]$TimeoutSec = 10)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-BoundedCommand -TimeoutSec $TimeoutSec -Script {
        $ErrorActionPreference = 'Continue'
        $text = (& docker ps --format '{{.Names}} ({{.Status}})' 2>&1 | Out-String)
        [pscustomobject]@{ Exit = $LASTEXITCODE; Text = $text }
    }
    $sw.Stop()
    $elapsed = '{0:N1}s' -f $sw.Elapsed.TotalSeconds

    if ($r.TimedOut) {
        return [pscustomobject]@{
            Healthy = $false
            Summary = ("docker did not answer within {0}s - the dependency check most likely stalled on DOCKER, not podman" -f $TimeoutSec)
            Detail  = $null
        }
    }
    $res = $r.Output | Select-Object -Last 1
    if (-not $res) {
        return [pscustomobject]@{ Healthy = $false; Summary = "docker ps produced no output (probe took $elapsed)"; Detail = $null }
    }
    $lines = @()
    if ($res.Text) { $lines = @($res.Text -split "`r?`n" | Where-Object { $_.Trim() }) }
    if ($res.Exit -ne 0) {
        return [pscustomobject]@{
            Healthy = $false
            Summary = ("docker ps FAILED with exit {0} after {1}" -f $res.Exit, $elapsed)
            Detail  = @($lines | Select-Object -First 5)
        }
    }
    return [pscustomobject]@{
        Healthy = $true
        Summary = ("docker answered in {0}: {1} container(s) running" -f $elapsed, $lines.Count)
        Detail  = @($lines | Select-Object -First 6)
    }
}

function Get-AppHostLogVerdict {
    <#
      Classifies a failed launch from logs/apphost.log. 'DcpDependencyTimeout' is
      the CARD-0075 shape: Aspire splices dcp info's captured stderr into the
      exception text, and a timeout truncates that stderr after the podman probe
      line (which returns in microseconds on every run, healthy ones included)
      and before the docker line (which is the one that stalled).
    #>
    param([Parameter(Mandatory = $true)][string]$LogPath)

    $verdict = [pscustomobject]@{ Kind = $null; Evidence = $null; PodmanNoise = $false }
    if (-not (Test-Path -LiteralPath $LogPath)) { return $verdict }
    $text = $null
    try { $text = Get-Content -LiteralPath $LogPath -Raw -ErrorAction Stop } catch { return $verdict }
    if ([string]::IsNullOrWhiteSpace($text)) { return $verdict }

    if ($text -match '(?m)^.*The build failed.*$') {
        $verdict.Kind = 'BuildFailed'
        $verdict.Evidence = $Matches[0].Trim()
        return $verdict
    }

    $dcpPatterns = @(
        'dependency check returned an error',
        'DcpDependencyCheck',
        'EnsureDcpContainerRuntimeAsync'
    )
    foreach ($p in $dcpPatterns) {
        if ($text -match ('(?m)^.*' + $p + '.*$')) {
            $verdict.Kind = 'DcpDependencyTimeout'
            $verdict.Evidence = $Matches[0].Trim()
            break
        }
    }
    if ($verdict.Kind -eq 'DcpDependencyTimeout') {
        $verdict.PodmanNoise = [bool]($text -match 'podman')
        if ($verdict.Evidence -and $verdict.Evidence.Length -gt 400) {
            $verdict.Evidence = $verdict.Evidence.Substring(0, 400) + ' ...'
        }
    }
    return $verdict
}

function Show-DcpTimeoutVerdict {
    <#
      The operator-facing message that replaces "podman: executable file not
      found". Everything printed here is a fact the exception did not carry.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [string]$Evidence,
        [bool]$PodmanNoise = $false,
        [string]$LaunchLock,
        [string]$RestartLock,
        [string]$WatchdogLog,
        [int]$DockerTimeoutSec = 10
    )

    Write-Host ''
    Write-Host "AppHost launch FAILED: Aspire's DCP dependency check TIMED OUT." -ForegroundColor Red
    if ($Evidence) { Write-Host "  from ${LogPath}: $Evidence" -ForegroundColor DarkGray }

    $docker = Get-DockerVerdict -TimeoutSec $DockerTimeoutSec
    if ($docker.Healthy) { $color = 'Green' } else { $color = 'Yellow' }
    Write-Host "  docker: $($docker.Summary)" -ForegroundColor $color
    foreach ($d in @($docker.Detail)) { if ($d) { Write-Host "    $d" -ForegroundColor DarkGray } }

    if ($PodmanNoise) {
        Write-Host "  IGNORE the podman text in that message. 'dcp info' probes podman FIRST on every" -ForegroundColor Yellow
        Write-Host "  invocation, healthy ones included (podman +0ms, docker +382ms, result +648ms), and" -ForegroundColor Yellow
        Write-Host "  Aspire splices its stderr into the exception. The timeout truncates that stderr" -ForegroundColor Yellow
        Write-Host "  after the runtime that answered instantly and before the one that stalled." -ForegroundColor Yellow
        Write-Host "  Podman is not installed here, is not needed, and is not the failure." -ForegroundColor Yellow
    }
    if ($docker.Healthy) {
        Write-Host "  Docker is healthy, so the likely cause is a CONCURRENT restart or launch killing" -ForegroundColor Yellow
        Write-Host "  DCP mid-startup. Check, in order:" -ForegroundColor Yellow
    } else {
        Write-Host "  Docker did not answer cleanly - fix Docker Desktop first, then check:" -ForegroundColor Yellow
    }
    foreach ($f in @($RestartLock, $LaunchLock, $WatchdogLog)) {
        if ($f) { Write-Host "    $f" -ForegroundColor DarkGray }
    }
    Write-Host "  Wait for any in-flight launch to finish before re-running this script." -ForegroundColor Yellow
    Write-Host ''
}
