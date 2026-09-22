# CARD-0594 Linux pty-host launch harness (evidence tooling, not product).
# Reproduces the investigation's container repro against a session-runner image and grades the
# observations against -Expect. Docker Desktop only. Never binds 17202-17205.
#
#   baseline : the defect is present (launch takes ~45s, the intermediary's pipes only reach EOF
#              when the detached host dies, the socket file survives the host).
#   fixed    : a session launches in well under 5s, the pipes reach EOF while the host lives, and
#              a timed-out host leaves no socket file behind.
#
# Exit codes: 0 observations match -Expect, 1 they do not, 2 setup failed (refused port, no
# Docker, failed build, unhealthy container).
param(
    [Parameter(Mandatory = $true)][string] $Image,
    [switch] $Build,
    [ValidateSet('fixed', 'baseline')]
    [string] $Expect = 'fixed',
    [int] $Port = 18299,
    [string] $EvidenceRoot = '.antiphon/c594-harness'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$forbiddenPorts = 17202, 17203, 17204, 17205
$repoRoot = Split-Path -Parent $PSScriptRoot
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$containerName = "c594-$stamp"

# Result fields, filled in as the run progresses; unknown stays unknown so a partial run still
# prints a legible line.
$httpCode = 'unknown'
$launchMs = -1
$eofMs = -1
$hostAliveAtEof = 'unknown'
$orphanSocket = 'unknown'

function Write-Evidence([string] $name, [string] $text) {
    $path = Join-Path $script:evidenceDir $name
    Set-Content -LiteralPath $path -Value $text -Encoding ascii
}

function Invoke-Docker([string[]] $dockerArgs, [string] $evidenceName) {
    $output = & docker @dockerArgs 2>&1 | Out-String
    if ($evidenceName) {
        Write-Evidence $evidenceName ("docker " + ($dockerArgs -join ' ') + "`n--- exit $LASTEXITCODE ---`n" + $output)
    }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Exit-Harness([int] $code, [string] $reason) {
    if ($reason) { Write-Host "C594 HARNESS NOTE: $reason" }
    Write-Host ("C594 HARNESS {0}: image={1} http={2} launchMs={3} eofMs={4} hostAliveAtEof={5} orphanSocket={6}" -f `
            $Expect, $Image, $httpCode, $launchMs, $eofMs, $hostAliveAtEof, $orphanSocket)
    Write-Host "C594 HARNESS EXIT CODE: $code"
    exit $code
}

if ($Port -in $forbiddenPorts) {
    Write-Host "C594 HARNESS NOTE: refusing production port $Port (17202-17205 are the local stack)."
    Write-Host "C594 HARNESS EXIT CODE: 2"
    exit 2
}

if (-not $EvidenceRoot) { $EvidenceRoot = '.antiphon/c594-harness' }
$evidenceRootPath = if ([System.IO.Path]::IsPathRooted($EvidenceRoot)) { $EvidenceRoot } else { Join-Path $repoRoot $EvidenceRoot }
$script:evidenceDir = Join-Path $evidenceRootPath $stamp
New-Item -ItemType Directory -Force -Path $script:evidenceDir | Out-Null

$dockerVersion = Invoke-Docker @('version', '--format', '{{.Server.Version}}') 'docker-version.txt'
if ($dockerVersion.ExitCode -ne 0) {
    Exit-Harness 2 "Docker daemon is not reachable. Start Docker Desktop and retry."
}

if ($Build) {
    Push-Location $repoRoot
    try {
        $headSha = (& git rev-parse HEAD).Trim()
    }
    finally {
        Pop-Location
    }
    Write-Host "C594 HARNESS: building $Image from $headSha"
    $buildArgs = @(
        'build',
        '-f', 'docker/session-runner-grok/Dockerfile',
        '--build-arg', "SOURCE_REVISION=$headSha",
        '-t', $Image,
        $repoRoot)
    Push-Location $repoRoot
    try {
        $buildResult = Invoke-Docker $buildArgs 'docker-build.txt'
    }
    finally {
        Pop-Location
    }
    if ($buildResult.ExitCode -ne 0) {
        Exit-Harness 2 "docker build failed; see $script:evidenceDir/docker-build.txt"
    }
}

$sessionId = [Guid]::NewGuid()
$probeSessionId = [Guid]::NewGuid()
$probePipe = "/tmp/antiphon-pty-c594probe-$($probeSessionId.ToString('N'))"
$started = $false

try {
    $runArgs = @(
        'run', '-d',
        '--name', $containerName,
        '--user', '1654:1654',
        '-p', "${Port}:8080",
        '-e', 'TMPDIR=/tmp',
        '-e', 'SessionRunner__SessionLogPath=/tmp/state/session-runner',
        '-e', 'SessionRunner__PtyHostDir=/tmp/antiphon-pty-hosts',
        '-e', 'Serilog__LogPath=/tmp/state/runner-logs',
        '-e', 'PhoneHome__Enabled=false',
        $Image)
    $run = Invoke-Docker $runArgs 'docker-run.txt'
    if ($run.ExitCode -ne 0) {
        Exit-Harness 2 "docker run failed; see $script:evidenceDir/docker-run.txt"
    }
    $started = $true

    # Health, 60s cap.
    $healthDeadline = (Get-Date).AddSeconds(60)
    $healthy = $false
    while ((Get-Date) -lt $healthDeadline) {
        try {
            $health = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 5 -SkipHttpErrorCheck
            if ([int]$health.StatusCode -ge 200 -and [int]$health.StatusCode -lt 300) {
                $healthy = $true
                break
            }
        }
        catch {
            # Not listening yet.
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $healthy) {
        $logs = Invoke-Docker @('logs', $containerName) 'container-logs-unhealthy.txt'
        Exit-Harness 2 "container never became healthy within 60s; see $script:evidenceDir/container-logs-unhealthy.txt"
    }

    # --- H-2: a real session launch through the runner's own POST /sessions. ---
    $body = @{
        sessionId = $sessionId.ToString()
        exe       = '/bin/bash'
        args      = @()
        env       = @{}
        cwd       = '/tmp'
        cols      = 120
        rows      = 30
    } | ConvertTo-Json -Compress
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/sessions" -Method Post -Body $body `
            -ContentType 'application/json' -TimeoutSec 180 -SkipHttpErrorCheck
        $sw.Stop()
        $httpCode = [int]$response.StatusCode
        Write-Evidence 'post-sessions.txt' ("status=$httpCode elapsedMs=$($sw.ElapsedMilliseconds)`n" + $response.Content)
    }
    catch {
        $sw.Stop()
        $httpCode = 'error'
        Write-Evidence 'post-sessions.txt' ("elapsedMs=$($sw.ElapsedMilliseconds)`n" + $_.Exception.Message)
    }
    $launchMs = [int]$sw.ElapsedMilliseconds
    Write-Host "C594 HARNESS: POST /sessions -> http=$httpCode launchMs=$launchMs"

    # Host log and fd table for the launched session, best effort: on the baseline image the host
    # is already dead by the time the POST returns.
    $logName = $sessionId.ToString('N') + '.log'
    $hostLog = Invoke-Docker @('exec', $containerName, 'bash', '-c', "cat /tmp/antiphon-pty-hosts/logs/$logName 2>&1 || true") 'session-host-log.txt'
    $hostPid = ''
    if ($hostLog.Output -match 'pid (\d+)') { $hostPid = $Matches[1] }
    if ($hostPid) {
        Invoke-Docker @('exec', $containerName, 'bash', '-c', "ls -l /proc/$hostPid/fd 2>&1 || true") 'session-host-fds.txt' | Out-Null
    }
    Invoke-Docker @('exec', $containerName, 'bash', '-c', 'ls -l /proc/1/fd 2>&1 || true') 'runner-pid1-fds.txt' | Out-Null

    # --- H-1: direct timing of the intermediary's pipe EOF against a live host. ---
    # $( ... | cat ) keeps a read end open until every writer closes it, which is exactly what the
    # launcher's ReadToEnd does. On the baseline image the detached host holds duplicate write ends
    # and EOF only arrives when it dies at --launch-timeout-sec.
    $probeLines = @(
        'set -u',
        'S=$(date +%s%N)',
        "OUT=`$(/app/Antiphon.PtyHost --spawn --session $probeSessionId --pipe $probePipe --manifest-dir /tmp/c594probe --launch-timeout-sec 20 2>&1 | cat)",
        'E=$(date +%s%N)',
        'PID=$(printf "%s" "$OUT" | head -n 1 | tr -dc 0-9)',
        'echo "C594_PROBE_EOF_MS=$(( (E - S) / 1000000 ))"',
        'echo "C594_PROBE_PID=$PID"',
        'if [ -n "$PID" ] && [ -d /proc/$PID ]; then echo C594_PROBE_HOST_ALIVE=yes; else echo C594_PROBE_HOST_ALIVE=no; fi',
        'if [ -n "$PID" ] && [ -d /proc/$PID ]; then echo "--- probe host fds ---"; ls -l /proc/$PID/fd 2>&1 || true; fi',
        'echo "--- intermediary output ---"',
        'printf "%s\n" "$OUT"'
    ) -join "`n"
    $probeB64 = [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes($probeLines))
    $probe = Invoke-Docker @('exec', $containerName, 'bash', '-c', "echo $probeB64 | base64 -d > /tmp/c594probe.sh; bash /tmp/c594probe.sh 2>&1") 'probe-h1.txt'
    if ($probe.Output -match 'C594_PROBE_EOF_MS=(\d+)') { $eofMs = [int]$Matches[1] }
    if ($probe.Output -match 'C594_PROBE_HOST_ALIVE=(yes|no)') { $hostAliveAtEof = $Matches[1] }
    $probePid = ''
    if ($probe.Output -match 'C594_PROBE_PID=(\d+)') { $probePid = $Matches[1] }
    Write-Host "C594 HARNESS: probe eofMs=$eofMs hostAliveAtEof=$hostAliveAtEof pid=$probePid"

    # --- H-3: does a host that exits on its launch timeout leave its socket file behind? ---
    if ($probePid) {
        $gone = $false
        $exitDeadline = (Get-Date).AddSeconds(45)
        while ((Get-Date) -lt $exitDeadline) {
            $alive = Invoke-Docker @('exec', $containerName, 'bash', '-c', "if [ -d /proc/$probePid ]; then echo alive; else echo gone; fi") ''
            if ($alive.Output -match 'gone') { $gone = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $gone) {
            Write-Host "C594 HARNESS NOTE: probe host $probePid still alive after its launch timeout window."
        }
    }
    $socketLs = Invoke-Docker @('exec', $containerName, 'bash', '-c', 'ls -la /tmp/antiphon-pty-c594probe-* 2>&1 || true') 'probe-h3-socket.txt'
    $orphanSocket = if ($socketLs.Output -match 'antiphon-pty-c594probe-') { 'yes' } else { 'no' }
    Write-Host "C594 HARNESS: orphanSocket=$orphanSocket"

    Invoke-Docker @('logs', $containerName) 'container-logs.txt' | Out-Null
}
finally {
    if ($started) {
        & docker rm -f $containerName 2>&1 | Out-String | Out-Null
    }
}

# --- Grade. ---
$failures = @()
if ($Expect -eq 'fixed') {
    if (-not ($httpCode -is [int]) -or $httpCode -lt 200 -or $httpCode -ge 300) { $failures += "http=$httpCode is not 2xx" }
    if ($launchMs -lt 0 -or $launchMs -ge 5000) { $failures += "launchMs=$launchMs is not under 5000" }
    if ($eofMs -lt 0 -or $eofMs -ge 2000) { $failures += "eofMs=$eofMs is not under 2000" }
    if ($hostAliveAtEof -ne 'yes') { $failures += "hostAliveAtEof=$hostAliveAtEof is not yes" }
    if ($orphanSocket -ne 'no') { $failures += "orphanSocket=$orphanSocket is not no" }
}
else {
    if (-not ($httpCode -is [int]) -or $httpCode -ne 500) { $failures += "http=$httpCode is not 500" }
    if ($launchMs -le 40000) { $failures += "launchMs=$launchMs is not over 40000" }
    if ($eofMs -le 15000) { $failures += "eofMs=$eofMs is not over 15000" }
    if ($orphanSocket -ne 'yes') { $failures += "orphanSocket=$orphanSocket is not yes" }
}

Write-Evidence 'result.txt' ("expect=$Expect image=$Image http=$httpCode launchMs=$launchMs eofMs=$eofMs hostAliveAtEof=$hostAliveAtEof orphanSocket=$orphanSocket`nfailures: " + ($failures -join '; '))

if ($failures.Count -gt 0) {
    Exit-Harness 1 ($failures -join '; ')
}

Exit-Harness 0 "evidence in $script:evidenceDir"
