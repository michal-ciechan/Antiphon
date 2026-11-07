#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1745 Linux server restart gate.

    Off unless -Config or ANTIPHON_LINUX_SERVER_CONFIG names a Linux server.
    The default is a WhatIf probe: GET /health and GET /api/version, no docker.
    -Execute probes the running server before any docker call and never stops it.
    Exit 0 requires /health 200 and version equal to -ExpectedServerSha.
    A server already at that SHA exits 0. Any other probe exits 5, keeps the
    lock, and leaves the container running. docker start <container> resumes
    that same image if the container is later found stopped.
    Desktop AppHost ports 17200-17209 are refused. ASCII-only.
.PARAMETER Config
    Key=value file: baseUrl, container, lockPath.
.PARAMETER ExpectedServerSha
    Full 40- or 64-character SHA the running server must report.
.PARAMETER Execute
    Probe the running server. Do not stop it. A moved image tag is reported
    and left for the compose deploy. Omit it to probe only.
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
    try {
        $output = @(& $script:GateDocker @ArgumentList)
        $code = 1
        if ($null -ne $LASTEXITCODE) { $code = [int]$LASTEXITCODE }
        $text = ''
        if ($output) { $text = (@($output) | ForEach-Object { "$_" }) -join "`n" }
        return [pscustomobject]@{ Code = $code; Output = $text.Trim() }
    } catch {
        return [pscustomobject]@{ Code = 1; Output = '' }
    }
}

function Write-GateUnverified([string]$Reason, [string]$Expected, [string]$Observed, [string]$Mode, [string]$PreviousImage, [string]$TagImage, [string]$LockPath, [string]$Container) {
    Write-GateLock -Path $LockPath -Reason $Reason -Expected $Expected -Observed $Observed -Mode $Mode -PreviousImage $PreviousImage
    Write-Host 'REFUSED: server build unverified (exit 5).'
    Write-Host "  expected SHA: $Expected"
    Write-Host "  observed SHA: $Observed"
    Write-Host "  reason: $Reason"
    Write-Host "  lock: $LockPath"
    if ($Reason -eq 'image-moved') {
        Write-Host "  tag image: $TagImage"
        Write-Host '  The image tag moved. This gate does not recreate the container.'
    }
    if ($Mode -eq 'execute') {
        Write-Host 'Nothing was restarted.'
        Write-Host 'The serving container was left running.'
        Write-Host "recovery: docker start $Container"
        exit 5
    }
    Write-Host 'Nothing was restarted.'
    exit 5
}

function Resolve-GateReplacement([string]$Container) {
    $script:GateDocker = [string]$env:ANTIPHON_LINUX_SERVER_DOCKER
    if ([string]::IsNullOrWhiteSpace($script:GateDocker)) { $script:GateDocker = 'docker' }
    $same = [pscustomobject]@{ Kind = 'mismatch'; PreviousImage = ''; TagImage = '' }
    if ($script:GateDocker -ne 'docker' -and -not (Test-Path -LiteralPath $script:GateDocker)) {
        return $same
    }
    $image = Invoke-GateDocker -ArgumentList @('inspect', '--format', '{{.Image}}', $Container)
    $ref = Invoke-GateDocker -ArgumentList @('inspect', '--format', '{{.Config.Image}}', $Container)
    $running = ''
    if ($image.Code -eq 0 -and -not [string]::IsNullOrWhiteSpace($image.Output)) {
        $running = (($image.Output -split "`n") | Select-Object -First 1).Trim()
    }
    if ($image.Code -ne 0 -or $ref.Code -ne 0 -or [string]::IsNullOrWhiteSpace($running)) {
        return [pscustomobject]@{ Kind = 'mismatch'; PreviousImage = $running; TagImage = '' }
    }
    $tagRef = ''
    if (-not [string]::IsNullOrWhiteSpace($ref.Output)) {
        $tagRef = (($ref.Output -split "`n") | Select-Object -First 1).Trim()
    }
    if ($tagRef -notmatch '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,200}$') {
        return [pscustomobject]@{ Kind = 'mismatch'; PreviousImage = $running; TagImage = '' }
    }
    $tag = Invoke-GateDocker -ArgumentList @('image', 'inspect', '--format', '{{.Id}}', $tagRef)
    $tagId = ''
    if ($tag.Code -eq 0 -and -not [string]::IsNullOrWhiteSpace($tag.Output)) {
        $tagId = (($tag.Output -split "`n") | Select-Object -First 1).Trim()
    }
    if ($tag.Code -ne 0 -or [string]::IsNullOrWhiteSpace($tagId) -or $tagId -eq $running) {
        return [pscustomobject]@{ Kind = 'mismatch'; PreviousImage = $running; TagImage = '' }
    }
    return [pscustomobject]@{ Kind = 'image-moved'; PreviousImage = $running; TagImage = $tagId }
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

if ($mode -eq 'execute') {
    $pre = Invoke-GateProbe -BaseUrl $baseUrl -Expected $expected
    if ($pre.Kind -eq 'ok') {
        Remove-GateLock $lockPath
        Write-Host 'already at expected SHA'
        Write-Host "VERIFIED sha=$($pre.Observed) mode=execute"
        exit 0
    }
    $reason = $pre.Kind
    $previousImage = ''
    $tagImage = ''
    if ($pre.Kind -eq 'mismatch') {
        $decision = Resolve-GateReplacement -Container $container
        $reason = [string]$decision.Kind
        $previousImage = [string]$decision.PreviousImage
        $tagImage = [string]$decision.TagImage
    }
    if ($reason -ne 'mismatch' -and $reason -ne 'malformed' -and $reason -ne 'unavailable' -and $reason -ne 'health' -and $reason -ne 'image-moved') {
        $reason = 'unavailable'
    }
    Write-GateUnverified -Reason $reason -Expected $expected -Observed $pre.Observed -Mode $mode -PreviousImage $previousImage -TagImage $tagImage -LockPath $lockPath -Container $container
}

$probe = Wait-GateProbe -BaseUrl $baseUrl -Expected $expected -BoundSec $TimeoutSec -Poll:$false
if ($probe.Kind -eq 'ok') {
    Remove-GateLock $lockPath
    Write-Host "VERIFIED sha=$($probe.Observed) mode=$mode"
    exit 0
}
if ($probe.Kind -eq 'health') {
    Write-Host 'REFUSED: /health was not 200.'
    Write-Host 'Nothing was restarted.'
    exit 1
}

$reason = $probe.Kind
if ($reason -ne 'mismatch' -and $reason -ne 'malformed' -and $reason -ne 'unavailable') {
    $reason = 'unavailable'
}
Write-GateUnverified -Reason $reason -Expected $expected -Observed $probe.Observed -Mode $mode -PreviousImage '' -TagImage '' -LockPath $lockPath -Container $container
