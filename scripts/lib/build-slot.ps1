#requires -Version 7.0
# CARD-0589 D-6: take and release a host build slot from the session runner's /build-slots broker.
# Dot-sourced by scripts/run-checkpoint.ps1 and scripts/build-slot.ps1.
#
#   Enter-AntiphonBuildSlot -Label <what> [-WaitMinutes 45] [-Endpoint <url>] [-HolderPid <pid>]
#     Waits in FIFO order, printing BUILD SLOT lines, and returns one of
#       Outcome granted   - LeaseId, MaxCpuCount from the grant; release it with Exit-AntiphonBuildSlot
#       Outcome unlimited - the broker is disabled; nothing is held; MaxCpuCount is the configured one
#       Outcome unleased  - the runner did not answer for the grace period (or is too old to have
#                           /build-slots): run anyway with -maxcpucount 4, and the line says so
#       Outcome timeout   - busy or below the memory floor for the whole wait: run nothing, exit 4
#   Exit-AntiphonBuildSlot -Lease <result>       releases a granted lease (a no-op otherwise)
#   Add-AntiphonMaxCpuCount -Command <tokens> -MaxCpuCount N
#
# The lease belongs to the calling process (its pid and start time): the runner reaps it when that
# process dies, so a lease never outlives the command it guards.
#
# Endpoint: ANTIPHON_BUILD_SLOTS_URL, else the production runner on this host (Windows
# http://localhost:17204/build-slots, Linux http://127.0.0.1:8080/build-slots).
# Offline seams (tests only): C589_SLOT_SHIM (a script answering in place of the HTTP call),
# C589_SLOT_WAIT_SECONDS, C589_SLOT_RETRY_MS, C589_SLOT_GRACE_SECONDS.
# Owner: docs/testing-and-build.md "Build slots (CARD-0589)". ASCII-only.

$script:AntiphonBuildSlotUnleasedMaxCpuCount = 4

function Get-AntiphonBuildSlotEndpoint {
    if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_BUILD_SLOTS_URL)) { return $env:ANTIPHON_BUILD_SLOTS_URL.Trim().TrimEnd('/') }
    if ($IsWindows) { return 'http://localhost:17204/build-slots' }
    return 'http://127.0.0.1:8080/build-slots'
}

function Invoke-AntiphonBuildSlotHttp {
    param([string]$Method, [string]$Uri, [string]$BodyJson = '')
    if (-not [string]::IsNullOrWhiteSpace($env:C589_SLOT_SHIM)) {
        return (& $env:C589_SLOT_SHIM -Method $Method -Uri $Uri -BodyJson $BodyJson)
    }
    try {
        $request = @{ Method = $Method; Uri = $Uri; SkipHttpErrorCheck = $true; NoProxy = $true; TimeoutSec = 10; ErrorAction = 'Stop' }
        if ($BodyJson) { $request.Body = $BodyJson; $request.ContentType = 'application/json' }
        $response = Invoke-WebRequest @request
        $content = $response.Content
        if ($content -is [byte[]]) { $content = [Text.Encoding]::UTF8.GetString($content) }
        return @{ Status = [int]$response.StatusCode; Body = [string]$content }
    } catch {
        return @{ Status = 0; Body = ''; Error = $_.Exception.Message }
    }
}

function ConvertFrom-AntiphonBuildSlotJson {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try { return ($Text | ConvertFrom-Json) } catch { return $null }
}

function Format-AntiphonBuildSlotSpan {
    param([double]$Seconds)
    if ($Seconds -lt 60) { return ('{0}s' -f [int][Math]::Floor($Seconds)) }
    return ('{0}m' -f [int][Math]::Floor($Seconds / 60))
}

function Enter-AntiphonBuildSlot {
    param(
        [Parameter(Mandatory = $true)] [string]$Label,
        [double]$WaitMinutes = 45,
        [string]$Endpoint = '',
        [int]$HolderPid = $PID
    )
    if ([string]::IsNullOrWhiteSpace($Endpoint)) { $Endpoint = Get-AntiphonBuildSlotEndpoint }
    $waitSeconds = $WaitMinutes * 60
    if ($env:C589_SLOT_WAIT_SECONDS) { $waitSeconds = [double]$env:C589_SLOT_WAIT_SECONDS }
    $graceSeconds = 60
    if ($env:C589_SLOT_GRACE_SECONDS) { $graceSeconds = [double]$env:C589_SLOT_GRACE_SECONDS }
    $retryOverride = 0
    if ($env:C589_SLOT_RETRY_MS) { $retryOverride = [int]$env:C589_SLOT_RETRY_MS }

    $holderStart = $null
    try { $holderStart = (Get-Process -Id $HolderPid -ErrorAction Stop).StartTime.ToUniversalTime().ToString('o') } catch { }
    $sessionId = $null
    if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_SESSION_ID)) { $sessionId = $env:ANTIPHON_SESSION_ID }
    $taskId = $null
    if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_ID)) { $taskId = $env:ANTIPHON_TASK_ID }
    $body = [ordered]@{ pid = $HolderPid; processStartUtc = $holderStart; label = $Label; sessionId = $sessionId; taskId = $taskId } |
        ConvertTo-Json -Compress

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $unreachableSince = -1.0
    $lastAnswer = ''
    $position = 0
    $lastState = ''
    $lastPrinted = -1000.0
    while ($true) {
        $answer = Invoke-AntiphonBuildSlotHttp -Method 'POST' -Uri $Endpoint -BodyJson $body
        $status = [int]$answer.Status
        $json = ConvertFrom-AntiphonBuildSlotJson -Text ([string]$answer.Body)
        $type = ''
        if ($null -ne $json -and $json.PSObject.Properties['type']) { $type = [string]$json.type }
        $elapsed = $clock.Elapsed.TotalSeconds
        $sleepMs = 5000

        if ($status -eq 200 -and $null -ne $json) {
            $cpu = [int]$json.maxCpuCount
            if ($json.unlimited -eq $true) {
                Write-Host ('BUILD SLOT unlimited maxcpucount={0}' -f $cpu)
                return [pscustomobject]@{ Outcome = 'unlimited'; LeaseId = $null; MaxCpuCount = $cpu; WaitedSeconds = [int]$elapsed; Endpoint = $Endpoint; Held = $null; Position = 0 }
            }
            Write-Host ('BUILD SLOT granted lease={0} waited={1}s maxcpucount={2}' -f $json.leaseId, [int]$elapsed, $cpu)
            return [pscustomobject]@{ Outcome = 'granted'; LeaseId = [string]$json.leaseId; MaxCpuCount = $cpu; WaitedSeconds = [int]$elapsed; Endpoint = $Endpoint; Held = [Diagnostics.Stopwatch]::StartNew(); Position = 0 }
        }

        if ($status -eq 409 -and ($type -eq 'build_slot_busy' -or $type -eq 'build_slot_memory_floor')) {
            $unreachableSince = -1.0
            $position = [int]$json.queuePosition
            if ($retryOverride -gt 0) { $sleepMs = $retryOverride } else { $sleepMs = [Math]::Min([Math]::Max([int]$json.retryAfterMs, 250), 60000) }
            # Once a minute (and whenever the reason changes) the wait is visible in the transcript.
            if ($type -ne $lastState -or ($elapsed - $lastPrinted) -ge 60) {
                $minutes = [int][Math]::Floor($elapsed / 60)
                if ($type -eq 'build_slot_busy') {
                    Write-Host ('BUILD SLOT waiting label={0} position={1} occupied={2}/{3} elapsed={4}m' -f $Label, $position, [int]$json.occupied, [int]$json.budget, $minutes)
                } else {
                    Write-Host ('BUILD SLOT waiting label={0} reason=memory_floor available={1}MB floor={2}MB position={3} elapsed={4}m' -f $Label, [long]$json.availableMb, [long]$json.floorMb, $position, $minutes)
                }
                $lastState = $type
                $lastPrinted = $elapsed
            }
        } else {
            # No answer, an old runner without /build-slots (404), or anything else unexpected.
            if ($status -eq 0) { $lastAnswer = 'no answer' } else { $lastAnswer = ('http {0}' -f $status) }
            if ($unreachableSince -lt 0) { $unreachableSince = $elapsed }
            if ($retryOverride -gt 0) { $sleepMs = $retryOverride }
            if (($elapsed - $unreachableSince) -ge $graceSeconds -or $elapsed -ge $waitSeconds) {
                Write-Host ('BUILD SLOT unleased reason=runner_unreachable maxcpucount={0} last={1}' -f $script:AntiphonBuildSlotUnleasedMaxCpuCount, $lastAnswer)
                return [pscustomobject]@{ Outcome = 'unleased'; LeaseId = $null; MaxCpuCount = $script:AntiphonBuildSlotUnleasedMaxCpuCount; WaitedSeconds = [int]$elapsed; Endpoint = $Endpoint; Held = $null; Position = 0 }
            }
        }

        if ($elapsed -ge $waitSeconds) {
            Write-Host ('BUILD SLOT timeout after {0} position={1}' -f (Format-AntiphonBuildSlotSpan -Seconds $waitSeconds), $position)
            return [pscustomobject]@{ Outcome = 'timeout'; LeaseId = $null; MaxCpuCount = 0; WaitedSeconds = [int]$elapsed; Endpoint = $Endpoint; Held = $null; Position = $position }
        }
        $remainingMs = [int][Math]::Ceiling(($waitSeconds - $clock.Elapsed.TotalSeconds) * 1000)
        Start-Sleep -Milliseconds ([Math]::Max(1, [Math]::Min($sleepMs, $remainingMs)))
    }
}

function Exit-AntiphonBuildSlot {
    param($Lease)
    if ($null -eq $Lease -or $Lease.Outcome -ne 'granted' -or [string]::IsNullOrWhiteSpace([string]$Lease.LeaseId)) { return }
    $held = 0
    if ($Lease.Held) { $held = [int]$Lease.Held.Elapsed.TotalSeconds }
    $answer = Invoke-AntiphonBuildSlotHttp -Method 'DELETE' -Uri ('{0}/{1}' -f $Lease.Endpoint, $Lease.LeaseId)
    if ([int]$answer.Status -eq 204) {
        Write-Host ('BUILD SLOT released lease={0} held={1}s' -f $Lease.LeaseId, $held)
    } else {
        Write-Host ('BUILD SLOT release failed lease={0} held={1}s status={2}; the runner reaps it when this process exits' -f $Lease.LeaseId, $held, [int]$answer.Status)
    }
}

function Test-AntiphonDotnetExe {
    param([string]$Token)
    if ([string]::IsNullOrWhiteSpace($Token)) { return $false }
    $leaf = ($Token -split '[\\/]')[-1]
    return ($leaf -ieq 'dotnet' -or $leaf -ieq 'dotnet.exe')
}

function Add-AntiphonMaxCpuCount {
    # Inserts -maxcpucount:N (before any `--`) for dotnet build|test|publish|pack|msbuild, unless the
    # command already names -m / -maxcpucount. Never for `dotnet run`: measured on SDK 10.0.401,
    # `dotnet run` hands -maxcpucount:N to the PROGRAM as an argument rather than to its build.
    # Callers collect the result with @(...); it always has at least two tokens when changed.
    param([string[]]$Command, [int]$MaxCpuCount)
    $tokens = @($Command)
    if ($tokens.Count -lt 2 -or $MaxCpuCount -lt 1 -or -not (Test-AntiphonDotnetExe -Token ([string]$tokens[0]))) { return $tokens }
    $verb = ([string]$tokens[1]).ToLowerInvariant()
    if (@('build', 'test', 'publish', 'pack', 'msbuild') -notcontains $verb) { return $tokens }
    $end = [Array]::IndexOf($tokens, '--')
    if ($end -lt 0) { $end = $tokens.Count }
    for ($i = 2; $i -lt $end; $i++) {
        if ([string]$tokens[$i] -imatch '^(-{1,2}|/)(m|maxcpucount)(:.*)?$') { return $tokens }
    }
    $switch = '-maxcpucount:' + $MaxCpuCount
    if ($end -ge $tokens.Count) { return ($tokens + @($switch)) }
    return (@($tokens[0..($end - 1)]) + @($switch) + @($tokens[$end..($tokens.Count - 1)]))
}

function Test-AntiphonDotnetRunWithBuild {
    # `dotnet run` without --no-build: its implicit build runs at the default node count (see above).
    param([string[]]$Command)
    $tokens = @($Command)
    if ($tokens.Count -lt 2 -or -not (Test-AntiphonDotnetExe -Token ([string]$tokens[0])) -or ([string]$tokens[1]) -ine 'run') { return $false }
    $end = [Array]::IndexOf($tokens, '--')
    if ($end -lt 0) { $end = $tokens.Count }
    for ($i = 2; $i -lt $end; $i++) { if ([string]$tokens[$i] -ieq '--no-build') { return $false } }
    return $true
}
