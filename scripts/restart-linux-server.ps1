#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1745 Linux server restart gate.

    Off unless -Config or ANTIPHON_LINUX_SERVER_CONFIG names a Linux server.
    The default is a WhatIf probe: GET /health and GET /api/version, no docker.
    -Execute stops and starts that container, then requires the same check.
    Exit 0 requires /health 200 and version equal to -ExpectedServerSha.
    A SHA mismatch exits 5 and leaves the lock. -Execute also stops the container.
    Desktop AppHost ports 17200-17209 are refused. ASCII-only.
.PARAMETER Config
    Key=value file: baseUrl, container, lockPath.
.PARAMETER ExpectedServerSha
    Full 40- or 64-character SHA the running server must report.
.PARAMETER Execute
    Stop and start the configured container, then probe. Omit it to probe only.
.PARAMETER WhatIf
    Probe only. This is the default when -Execute is omitted.
.PARAMETER TimeoutSec
    Bound on an -Execute health wait. A WhatIf probe is a single request.
.PARAMETER LockMaxAgeMinutes
    A newer lock refuses another run.
#>
param(
    [string]$Config = '',
    [string]$ExpectedServerSha = '',
    [switch]$Execute,
    [switch]$WhatIf,
    [int]$TimeoutSec = 20,
    [int]$LockMaxAgeMinutes = 15
)

$ErrorActionPreference = 'Stop'

function Test-GateSha([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return [bool]($Value -match '^[0-9a-fA-F]{40}$' -or $Value -match '^[0-9a-fA-F]{64}$')
}

function Write-GateRefusal([string]$Message) {
    Write-Host "REFUSED: $Message"
    Write-Host 'Nothing was restarted.'
    exit 3
}

function Read-GateConfig([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return $null }
    $map = @{}
    foreach ($line in @(Get-Content -LiteralPath $Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $text = $line.Trim()
        if ($text.StartsWith('#')) { continue }
        $idx = $text.IndexOf('=')
        if ($idx -lt 1) { return $null }
        $map[$text.Substring(0, $idx).Trim()] = $text.Substring($idx + 1).Trim()
    }
    return $map
}

function Test-GateContainer([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    if ($Name -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$') { return $false }
    $lower = $Name.ToLowerInvariant()
    foreach ($banned in @('session-runner', 'antiphon-runner', 'antiphon-postgres', 'fake-gateway')) {
        if ($lower -eq $banned) { return $false }
    }
    if ($lower.Contains('runner')) { return $false }
    if ($lower.Contains('postgres')) { return $false }
    if ($lower.Contains('gateway')) { return $false }
    if ($lower.Contains('phone')) { return $false }
    return $true
}

function Test-GateLockFresh([string]$Path, [int]$MaxAgeMinutes) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $age = [datetime]::UtcNow - (Get-Item -LiteralPath $Path).LastWriteTimeUtc
    return $age.TotalMinutes -lt $MaxAgeMinutes
}

function Write-GateLock([string]$Path, [string]$Reason, [string]$Expected, [string]$Observed, [string]$Mode, [string]$PreviousImage) {
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $lines = @(
        "reason=$Reason"
        "expected=$Expected"
        "observed=$Observed"
        "mode=$Mode"
        "previousImage=$PreviousImage"
        ("utc=" + [datetime]::UtcNow.ToString('o'))
    )
    Set-Content -LiteralPath $Path -Value $lines -Encoding ascii
}

function Remove-GateLock([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Invoke-GateGet([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -MaximumRedirection 0 -SkipHttpErrorCheck
        return [pscustomobject]@{ Code = [int]$response.StatusCode; Body = [string]$response.Content }
    } catch {
        $code = 0
        try {
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
                $code = [int]$_.Exception.Response.StatusCode
            }
        } catch { }
        return [pscustomobject]@{ Code = $code; Body = '' }
    }
}

function Resolve-GateIdentity([string]$Body) {
    if ([string]::IsNullOrWhiteSpace($Body)) {
        return [pscustomobject]@{ Kind = 'malformed'; Sha = '' }
    }
    try {
        $obj = $Body | ConvertFrom-Json
    } catch {
        return [pscustomobject]@{ Kind = 'malformed'; Sha = '' }
    }
    $sha = ''
    if ($null -ne $obj.PSObject.Properties['version']) {
        $sha = [string]$obj.version
    }
    if (-not (Test-GateSha $sha)) {
        return [pscustomobject]@{ Kind = 'malformed'; Sha = $sha }
    }
    return [pscustomobject]@{ Kind = 'ok'; Sha = $sha.ToLowerInvariant() }
}

function Invoke-GateProbe([string]$BaseUrl, [string]$Expected) {
    $root = $BaseUrl.TrimEnd('/')
    $health = Invoke-GateGet ($root + '/health')
    if ($health.Code -ne 200) {
        return [pscustomobject]@{ Kind = 'health'; Observed = '' }
    }
    $version = Invoke-GateGet ($root + '/api/version')
    if ($version.Code -ne 200) {
        return [pscustomobject]@{ Kind = 'unavailable'; Observed = '' }
    }
    $ident = Resolve-GateIdentity $version.Body
    if ($ident.Kind -ne 'ok') {
        return [pscustomobject]@{ Kind = 'malformed'; Observed = $ident.Sha }
    }
    if ($ident.Sha -ne $Expected.ToLowerInvariant()) {
        return [pscustomobject]@{ Kind = 'mismatch'; Observed = $ident.Sha }
    }
    return [pscustomobject]@{ Kind = 'ok'; Observed = $ident.Sha }
}

function Wait-GateProbe([string]$BaseUrl, [string]$Expected, [int]$BoundSec, [bool]$Poll) {
    $deadline = (Get-Date).AddSeconds($BoundSec)
    $last = $null
    do {
        $last = Invoke-GateProbe -BaseUrl $BaseUrl -Expected $Expected
        if ($last.Kind -eq 'ok' -or $last.Kind -eq 'mismatch' -or $last.Kind -eq 'malformed') { return $last }
        if (-not $Poll) { return $last }
        if ((Get-Date) -ge $deadline) { return $last }
        Start-Sleep -Milliseconds 200
    } while ($true)
}

function Invoke-GateDocker([string[]]$ArgumentList) {
    $output = @(& $script:GateDocker @ArgumentList)
    $code = 1
    if ($null -ne $LASTEXITCODE) { $code = [int]$LASTEXITCODE }
    $text = ''
    if ($output) { $text = (@($output) | ForEach-Object { "$_" }) -join "`n" }
    return [pscustomobject]@{ Code = $code; Output = $text.Trim() }
}

$configPath = $Config
if ([string]::IsNullOrWhiteSpace($configPath)) {
    $configPath = [string]$env:ANTIPHON_LINUX_SERVER_CONFIG
}
if ([string]::IsNullOrWhiteSpace($configPath)) {
    Write-GateRefusal 'Linux server is not configured.'
}

if ($Execute -and $WhatIf) {
    Write-GateRefusal '-Execute and -WhatIf were both set.'
}
$mode = 'whatif'
if ($Execute) { $mode = 'execute' }

$cfg = Read-GateConfig $configPath
if ($null -eq $cfg -or -not $cfg.ContainsKey('baseUrl') -or -not $cfg.ContainsKey('container') -or -not $cfg.ContainsKey('lockPath')) {
    Write-GateRefusal 'Linux server config is missing baseUrl, container, or lockPath.'
}

$baseUrl = [string]$cfg['baseUrl']
$container = [string]$cfg['container']
$lockPath = [string]$cfg['lockPath']

if (-not (Test-GateSha $ExpectedServerSha)) {
    Write-GateRefusal '-ExpectedServerSha is not a full 40- or 64-character SHA.'
}
$expected = $ExpectedServerSha.ToLowerInvariant()

$uri = $null
if (-not [Uri]::TryCreate($baseUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'http' -or $uri.UserInfo) {
    Write-GateRefusal 'baseUrl must be an http URL without user info.'
}
if ($uri.Host -ne '127.0.0.1' -and $uri.Host -ne 'localhost') {
    Write-GateRefusal 'baseUrl must use the host loopback.'
}
if ($uri.IsDefaultPort -or $uri.Port -lt 1) {
    Write-GateRefusal 'baseUrl must name an explicit port.'
}
if ($uri.Port -ge 17200 -and $uri.Port -le 17209) {
    Write-GateRefusal "baseUrl port $($uri.Port) is a desktop AppHost port."
}
if (-not (Test-GateContainer $container)) {
    Write-GateRefusal "container $container is outside this gate."
}
if (-not [System.IO.Path]::IsPathRooted($lockPath) -or $lockPath.Contains('..')) {
    Write-GateRefusal 'lockPath must be a rooted path.'
}
if ($TimeoutSec -lt 1) { $TimeoutSec = 1 }
if ($LockMaxAgeMinutes -lt 1) { $LockMaxAgeMinutes = 1 }

if (Test-GateLockFresh -Path $lockPath -MaxAgeMinutes $LockMaxAgeMinutes) {
    Write-GateRefusal "a restart lock is held at $lockPath."
}

$previousImage = ''
if ($mode -eq 'execute') {
    $script:GateDocker = [string]$env:ANTIPHON_LINUX_SERVER_DOCKER
    if ([string]::IsNullOrWhiteSpace($script:GateDocker)) { $script:GateDocker = 'docker' }
    if ($script:GateDocker -ne 'docker' -and -not (Test-Path -LiteralPath $script:GateDocker)) {
        Write-GateRefusal 'the docker command for -Execute is missing.'
    }
    Write-GateLock -Path $lockPath -Reason 'execute' -Expected $expected -Observed '' -Mode $mode -PreviousImage ''
    $inspected = Invoke-GateDocker -ArgumentList @('inspect', '--format', '{{.Image}}', $container)
    if ($inspected.Code -ne 0 -or [string]::IsNullOrWhiteSpace($inspected.Output)) {
        Remove-GateLock $lockPath
        Write-GateRefusal "docker inspect failed for $container."
    }
    $previousImage = ($inspected.Output -split "`n" | Select-Object -First 1).Trim()
    $stopped = Invoke-GateDocker -ArgumentList @('stop', $container)
    if ($stopped.Code -ne 0) {
        Write-Host "REFUSED: docker stop failed for $container. The lock is retained."
        exit 1
    }
    $started = Invoke-GateDocker -ArgumentList @('start', $container)
    if ($started.Code -ne 0) {
        Write-Host "REFUSED: docker start failed for $container. The lock is retained."
        exit 1
    }
}

$probe = Wait-GateProbe -BaseUrl $baseUrl -Expected $expected -BoundSec $TimeoutSec -Poll:($mode -eq 'execute')
if ($probe.Kind -eq 'ok') {
    Remove-GateLock $lockPath
    Write-Host "VERIFIED sha=$($probe.Observed) mode=$mode"
    exit 0
}
if ($probe.Kind -eq 'health') {
    if ($mode -eq 'whatif') {
        Write-Host 'REFUSED: /health was not 200.'
        Write-Host 'Nothing was restarted.'
        exit 1
    }
    Write-Host 'REFUSED: /health was not 200. The lock is retained.'
    exit 1
}

$reason = $probe.Kind
if ($reason -ne 'mismatch' -and $reason -ne 'malformed' -and $reason -ne 'unavailable') {
    $reason = 'unavailable'
}
Write-GateLock -Path $lockPath -Reason $reason -Expected $expected -Observed $probe.Observed -Mode $mode -PreviousImage $previousImage
Write-Host 'REFUSED: server build unverified (exit 5).'
Write-Host "  expected SHA: $expected"
Write-Host "  observed SHA: $($probe.Observed)"
Write-Host "  reason: $reason"
Write-Host "  lock: $lockPath"
if ($mode -eq 'execute') {
    $rolled = Invoke-GateDocker -ArgumentList @('stop', $container)
    if ($rolled.Code -ne 0) {
        Write-Host "  docker stop after the mismatch failed for $container. The lock is retained."
    }
    Write-Host "  previous image: $previousImage"
    Write-Host '  container left stopped'
    exit 5
}
Write-Host 'Nothing was restarted.'
exit 5
