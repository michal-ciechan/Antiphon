# CARD-0490 V-7: isolated Antiphon + phone-home Grok container. Never binds 17202-17205.
# PhoneHome__ServerOrigin is injected at container launch from PHONE_HOME_SERVER_ORIGIN.
# local-docker uses host.docker.internal; server2 uses the desktop Tailscale IPv4 (never host.docker.internal).
param(
    [string] $ConfigurationFile = '.antiphon/card0490-live.json',
    [Parameter(Mandatory = $true)][string] $EvidenceRoot,
    [ValidateSet('local-docker', 'server2')]
    [string] $Placement = 'local-docker',
    [switch] $WriteConfigOnly,
    [switch] $SkipContainer,
    [int] $HoldSeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$forbiddenPorts = 17202, 17203, 17204, 17205
$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'docker-compose.runner-grok.yml'
$exampleFile = Join-Path $repoRoot 'tests\fixtures\card0490-linux\card0490-live.example.json'
$assetLock = Join-Path $repoRoot 'tests\fixtures\card0490-linux\assets.lock.json'

function Get-IsolatedListenPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
    if ($port -in $forbiddenPorts) {
        throw "Allocated a forbidden production port $port."
    }
    return $port
}

function Assert-NotProductionPort([object] $port, [string] $name) {
    if ($null -eq $port -or $port -eq '') { return }
    $n = [int]$port
    if ($n -in $forbiddenPorts) {
        throw "Configuration names a production service port ($name=$n)."
    }
}

function Assert-NotProductionOrigin([string] $origin, [string] $name) {
    if ([string]::IsNullOrWhiteSpace($origin)) { return }
    $uri = [Uri]$origin
    if ($uri.Port -in $forbiddenPorts) {
        throw "$name points at a production service port ($origin)."
    }
}

function Get-PrimaryGrokHome {
    $fromEnv = [Environment]::GetEnvironmentVariable('GROK_HOME')
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        return [System.IO.Path]::GetFullPath($fromEnv)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.grok'))
}

function Get-DesktopReachableHost {
    try {
        $ip = (& tailscale ip -4 2>$null | Select-Object -First 1)
        if ($ip -match '^100\.\d+\.\d+\.\d+$') { return $ip.Trim() }
    }
    catch { }
    return '100.79.51.37'
}

function Copy-ThrowawayGrokHome([string] $sourceHome, [string] $destHome) {
    $src = [System.IO.Path]::GetFullPath($sourceHome)
    $dst = [System.IO.Path]::GetFullPath($destHome)
    if ([string]::Equals($src, $dst, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Throwaway GROK_HOME must not be the primary store ($src)."
    }
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    $auth = Join-Path $src 'auth.json'
    if (-not (Test-Path -LiteralPath $auth)) {
        return $false
    }
    foreach ($name in @('auth.json', 'config.toml', 'version.json')) {
        $from = Join-Path $src $name
        if (Test-Path -LiteralPath $from) {
            Copy-Item -LiteralPath $from -Destination (Join-Path $dst $name) -Force
        }
    }
    # Do not copy auth.json.lock, bin/, or sessions/ from the live home.
    # Pre-create sessions/ so compose can overlay a writable bind on the read-only parent.
    New-Item -ItemType Directory -Force -Path (Join-Path $dst 'sessions') | Out-Null
    return Test-Path -LiteralPath (Join-Path $dst 'auth.json')
}

function Get-ComposeText {
    if (-not (Test-Path -LiteralPath $composeFile)) {
        throw "Compose file not found: $composeFile"
    }
    return Get-Content -LiteralPath $composeFile -Raw
}

function Assert-ComposeDoesNotHardcodeOrigin {
    $text = Get-ComposeText
    if ($text -match 'PhoneHome__ServerOrigin:\s*http') {
        throw "docker-compose.runner-grok.yml hardcodes PhoneHome__ServerOrigin; it must read PHONE_HOME_SERVER_ORIGIN."
    }
    if ($text -notmatch 'PHONE_HOME_SERVER_ORIGIN') {
        throw "docker-compose.runner-grok.yml must interpolate PHONE_HOME_SERVER_ORIGIN."
    }
    if ($text -match 'host\.docker\.internal:1720[2-5]') {
        throw "docker-compose.runner-grok.yml must not name production Aspire ports."
    }
    if ($text -notmatch '(?s)PHONE_HOME_GROK_HOME.*?read_only:\s*true') {
        throw "PHONE_HOME_GROK_HOME must be a read-only bind mount."
    }
    if ($text -notmatch 'PHONE_HOME_GROK_SESSIONS') {
        throw "docker-compose.runner-grok.yml must interpolate PHONE_HOME_GROK_SESSIONS for writable session files."
    }
}

function New-IsolatedLiveConfig {
    $serverPort = Get-IsolatedListenPort
    $postgresPort = Get-IsolatedListenPort
    $runId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $root = Join-Path $repoRoot '.antiphon'
    $runRoot = Join-Path $root ("card0490-live-" + $runId)
    $workspace = Join-Path $runRoot 'work'
    $state = Join-Path $runRoot 'state'
    $grokHome = Join-Path $runRoot 'grok-home'
    $grokSessions = Join-Path $runRoot 'grok-sessions'
    $secretFile = Join-Path $runRoot 'phone-home.secret'
    New-Item -ItemType Directory -Force -Path $workspace, $state, $grokHome, $grokSessions, (Split-Path $secretFile) | Out-Null
    $secret = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    Set-Content -LiteralPath $secretFile -Value $secret -NoNewline -Encoding ascii
    $agentId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
    if (Test-Path -LiteralPath $exampleFile) {
        $example = Get-Content -LiteralPath $exampleFile -Raw | ConvertFrom-Json
        if ($example.standingAgentId) { $agentId = [string]$example.standingAgentId }
    }
    return [ordered]@{
        serverPort             = $serverPort
        postgresPort           = $postgresPort
        serverOrigin           = "http://127.0.0.1:$serverPort"
        phoneHomeServerOrigin  = "http://host.docker.internal:$serverPort"
        placement              = 'local-docker'
        desktopReachableHost   = $null
        runnerId               = 'grok-linux'
        standingAgentId        = $agentId
        hostWorkspaceRoot      = $workspace
        grokHomeSource         = Get-PrimaryGrokHome
        oauthMount             = $grokHome
        grokSessionsMount      = $grokSessions
        stateRoot              = $state
        secretFile             = $secretFile
        image                  = @{
            dockerfile  = 'docker/session-runner-grok/Dockerfile'
            digest      = $null
            grokVersion = '1.0.34'
        }
        assetLock              = 'tests/fixtures/card0490-linux/assets.lock.json'
        runId                  = $runId
    }
}

function Merge-LiveConfig($base, $overlay) {
    if ($null -eq $overlay) { return $base }
    foreach ($p in $overlay.PSObject.Properties) {
        if ($null -eq $p.Value -or $p.Value -eq '') { continue }
        $base[$p.Name] = $p.Value
    }
    return $base
}

function Set-PlacementOrigin($config, [string] $placement) {
    $config.placement = $placement
    $port = [int]$config.serverPort
    if ($placement -eq 'server2') {
        $hostName = [string]$config.desktopReachableHost
        if ([string]::IsNullOrWhiteSpace($hostName)) {
            $hostName = Get-DesktopReachableHost
        }
        $config.desktopReachableHost = $hostName
        $config.phoneHomeServerOrigin = "http://${hostName}:${port}"
        if ($config.phoneHomeServerOrigin -match 'host\.docker\.internal') {
            throw "server2 placement must not use host.docker.internal."
        }
    }
    else {
        $config.phoneHomeServerOrigin = "http://host.docker.internal:${port}"
    }
    return $config
}

function Add-ContainerBlockers($config, [System.Collections.Generic.List[string]] $blockers) {
    $primary = Get-PrimaryGrokHome
    $mount = [System.IO.Path]::GetFullPath([string]$config.oauthMount)
    if ([string]::Equals($primary, $mount, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$blockers.Add("oauthMount is the primary GROK_HOME; copy auth.json into a throwaway directory and mount that read-only.")
    }
    $auth = Join-Path $mount 'auth.json'
    if (-not (Test-Path -LiteralPath $auth)) {
        [void]$blockers.Add("Grok OAuth copy missing: expected auth.json in throwaway oauthMount ($mount), copied from primary GROK_HOME (env GROK_HOME or %USERPROFILE%\.grok). Do not bind-mount the primary store. Mount the copy read-only at /state/grok via PHONE_HOME_GROK_HOME. Not XAI_API_KEY.")
    }
    $dockerfile = Join-Path $repoRoot 'docker/session-runner-grok/Dockerfile'
    $df = Get-Content -LiteralPath $dockerfile -Raw
    if ($df -notmatch '1\.0\.34' -or $df -notmatch 'grok') {
        [void]$blockers.Add("docker/session-runner-grok/Dockerfile does not install Grok 1.0.34; the image cannot complete a provider turn until grok is pinned into the image.")
    }
    if ($df -match '(?im)^\s*ENV\s+PhoneHome__ServerOrigin\b') {
        [void]$blockers.Add("docker/session-runner-grok/Dockerfile must not ENV PhoneHome__ServerOrigin.")
    }
}

function Add-QemuBlockers([System.Collections.Generic.List[string]] $blockers) {
    if (Test-Path -LiteralPath $assetLock) {
        $lockText = Get-Content -LiteralPath $assetLock -Raw
        if ($lockText -match 'pending-operator-pin') {
            [void]$blockers.Add("tests/fixtures/card0490-linux/assets.lock.json still has pending-operator-pin; replace qemu/qemu-img/bootDisk sha256 pins before the QEMU ordinary lane / PC-28-31.")
        }
    }
    else {
        [void]$blockers.Add("Asset lock missing: tests/fixtures/card0490-linux/assets.lock.json")
    }
}

Assert-ComposeDoesNotHardcodeOrigin
New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null

$configPath = $ConfigurationFile
if (-not [System.IO.Path]::IsPathRooted($configPath)) {
    $configPath = Join-Path (Get-Location) $ConfigurationFile
}

$generated = New-IsolatedLiveConfig
if (Test-Path -LiteralPath $configPath) {
    $existing = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $generated = Merge-LiveConfig $generated $existing
}
if ($PSBoundParameters.ContainsKey('Placement')) {
    $generated.placement = $Placement
}
elseif ([string]::IsNullOrWhiteSpace([string]$generated.placement)) {
    $generated.placement = 'local-docker'
}
$generated = Set-PlacementOrigin $generated ([string]$generated.placement)
$primaryHome = Get-PrimaryGrokHome
$generated.grokHomeSource = $primaryHome
if ([string]::Equals(
        [System.IO.Path]::GetFullPath([string]$generated.oauthMount),
        $primaryHome,
        [StringComparison]::OrdinalIgnoreCase)) {
    $generated.oauthMount = Join-Path (Join-Path $repoRoot '.antiphon') ("card0490-live-" + $generated.runId + '\grok-home')
}
if ([string]::IsNullOrWhiteSpace([string]$generated.grokSessionsMount)) {
    $generated.grokSessionsMount = Join-Path (Split-Path -Parent ([string]$generated.oauthMount)) 'grok-sessions'
}
New-Item -ItemType Directory -Force -Path ([string]$generated.oauthMount), (Join-Path ([string]$generated.oauthMount) 'sessions'), ([string]$generated.grokSessionsMount) | Out-Null

Assert-NotProductionPort $generated.serverPort 'serverPort'
Assert-NotProductionPort $generated.postgresPort 'postgresPort'
Assert-NotProductionOrigin ([string]$generated.serverOrigin) 'serverOrigin'
Assert-NotProductionOrigin ([string]$generated.phoneHomeServerOrigin) 'phoneHomeServerOrigin'

$copied = Copy-ThrowawayGrokHome ([string]$generated.grokHomeSource) ([string]$generated.oauthMount)
Write-Host "Throwaway GROK_HOME copy from grokHomeSource path (contents not logged): copiedAuth=$copied mount=$($generated.oauthMount)"

$configDir = Split-Path -Parent $configPath
if ($configDir) { New-Item -ItemType Directory -Force -Path $configDir | Out-Null }
($generated | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $configPath -Encoding utf8
Write-Host "Wrote isolated live config: $configPath"
Write-Host "placement=$($generated.placement) phoneHomeServerOrigin=$($generated.phoneHomeServerOrigin) (PhoneHome__ServerOrigin / PHONE_HOME_SERVER_ORIGIN)"

$blockers = [System.Collections.Generic.List[string]]::new()
$qemuBlockers = [System.Collections.Generic.List[string]]::new()
Add-ContainerBlockers $generated $blockers
Add-QemuBlockers $qemuBlockers
$blockerPath = Join-Path $EvidenceRoot 'blockers.txt'
$allBlockers = [System.Collections.Generic.List[string]]::new()
foreach ($b in $blockers) { [void]$allBlockers.Add($b) }
foreach ($b in $qemuBlockers) { [void]$allBlockers.Add("qemu: $b") }
$allBlockers | Set-Content -LiteralPath $blockerPath -Encoding utf8

if ($WriteConfigOnly) {
    Write-Host "WriteConfigOnly: isolated ports allocated; container not started."
    if ($blockers.Count -gt 0) {
        Write-Host "Remaining V-7 container blockers:"
        $blockers | ForEach-Object { Write-Host " - $_" }
        exit 2
    }
    if ($qemuBlockers.Count -gt 0) {
        Write-Host "QEMU ordinary-lane blockers (do not block the isolated Grok container):"
        $qemuBlockers | ForEach-Object { Write-Host " - $_" }
    }
    exit 0
}

$pgName = "antiphon-card0490-pg-$($generated.runId)"
$serverProc = $null
$pgStarted = $false
$composeProject = "card0490-$($generated.runId)"
try {
    $pgPass = 'card0490_' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
    Write-Host "Starting isolated Postgres on port $($generated.postgresPort) ($pgName)"
    & docker run --rm -d --name $pgName `
        -e POSTGRES_PASSWORD=$pgPass `
        -e POSTGRES_USER=antiphon `
        -e POSTGRES_DB=antiphon_card0490 `
        -p "$($generated.postgresPort):5432" `
        postgres:16-alpine | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "docker run postgres failed (exit $LASTEXITCODE)." }
    $pgStarted = $true

    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        & docker exec $pgName pg_isready -U antiphon -d antiphon_card0490 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw "Isolated Postgres did not become ready." }

    $env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=$($generated.postgresPort);Database=antiphon_card0490;Username=antiphon;Password=$pgPass"
    $env:ASPNETCORE_URLS = "http://0.0.0.0:$($generated.serverPort)"
    $env:SessionRunner__BaseUrl = 'http://127.0.0.1:1'
    $env:Delegation__CheckInterpreterEnabled = 'false'
    $env:Delegation__DiagnoseEnabled = 'false'
    $env:Delegation__OutputDistillerEnabled = 'false'
    $env:Hangfire__ServerEnabled = 'false'
    $env:AgentTui__ImportProfilesOnStartup = 'false'
    $env:PhoneHomeRunner__Enabled = 'true'
    $env:PhoneHomeRunner__AllowedRunnerId = [string]$generated.runnerId
    $env:PhoneHomeRunner__StandingAgentId = [string]$generated.standingAgentId
    $env:PhoneHomeRunner__HostWorkspaceRoot = [string]$generated.hostWorkspaceRoot
    $env:PhoneHomeRunner__RunnerWorkspace = '/work'
    $env:PhoneHomeRunner__ChildGrokHome = '/state/grok'
    $env:PhoneHomeRunner__CallbackOrigin = [string]$generated.phoneHomeServerOrigin
    $env:PhoneHomeRunner__SharedSecret = (Get-Content -LiteralPath $generated.secretFile -Raw).Trim()

    $outDir = Join-Path $repoRoot 'server/bin-card0490-v7'
    Write-Host "Building isolated Antiphon.Server to $outDir"
    & dotnet build (Join-Path $repoRoot 'server/Antiphon.Server.csproj') --property:OutputPath=bin-card0490-v7/ --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Isolated server build failed." }
    $dll = Join-Path $outDir 'Antiphon.Server.dll'
    Write-Host "Starting isolated Antiphon.Server at $($generated.serverOrigin)"
    $serverProc = Start-Process -FilePath 'dotnet' -ArgumentList @($dll) -WorkingDirectory $outDir -PassThru -NoNewWindow

    $health = "$($generated.serverOrigin)/health"
    $up = $false
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $health -UseBasicParsing -TimeoutSec 2
            if ($r.StatusCode -eq 200) { $up = $true; break }
        }
        catch { }
        if ($serverProc.HasExited) { throw "Isolated server exited early (code $($serverProc.ExitCode))." }
        Start-Sleep -Seconds 1
    }
    if (-not $up) { throw "Isolated server /health did not return 200 on $($generated.serverOrigin)." }
    Write-Host "Isolated server healthy: $health"

    if ([string]$generated.placement -eq 'server2') {
        $fwName = "CARD-0490-v7-$($generated.serverPort)"
        try {
            New-NetFirewallRule -DisplayName $fwName -Direction Inbound -Action Allow -Protocol TCP -LocalPort ([int]$generated.serverPort) -RemoteAddress '100.64.0.0/10' -ErrorAction Stop | Out-Null
            Write-Host "Opened Tailscale inbound firewall rule $fwName for port $($generated.serverPort)"
        }
        catch {
            Write-Host "Could not add Tailscale firewall rule $fwName (elevation may be required): $($_.Exception.Message)"
        }
    }

    $env:PHONE_HOME_SERVER_ORIGIN = [string]$generated.phoneHomeServerOrigin
    $env:PHONE_HOME_WORKSPACE = [string]$generated.hostWorkspaceRoot
    $env:PHONE_HOME_STATE = [string]$generated.stateRoot
    $env:PHONE_HOME_GROK_HOME = [string]$generated.oauthMount
    $env:PHONE_HOME_GROK_SESSIONS = [string]$generated.grokSessionsMount
    $env:PHONE_HOME_SECRET_FILE = [string]$generated.secretFile

    Write-Host "docker compose config (origin injected, no published runner port)"
    & docker compose -f $composeFile -p $composeProject config | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "docker compose config failed." }

    if ($SkipContainer) {
        Write-Host "SkipContainer: isolated server is up; grok container not started."
    }
    elseif ($generated.placement -eq 'server2') {
        Write-Host "server2 placement: start the container on server2 with PHONE_HOME_SERVER_ORIGIN=$($generated.phoneHomeServerOrigin) (desktop Tailscale). host.docker.internal on server2 is server2 itself."
    }
    elseif ($blockers.Count -gt 0) {
        Write-Host "Not starting the Grok container until blockers are cleared:"
        $blockers | ForEach-Object { Write-Host " - $_" }
    }
    else {
        Write-Host "Starting phone-home Grok container with PhoneHome__ServerOrigin=$($env:PHONE_HOME_SERVER_ORIGIN)"
        & docker compose -f $composeFile -p $composeProject up -d --build | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "docker compose up failed." }
        Write-Host "CARD-0490 container started against isolated origin. Queue the canary through the isolated API; do not use 17202."
    }

    if ($HoldSeconds -gt 0 -and $blockers.Count -eq 0) {
        $statusPath = Join-Path $EvidenceRoot 'runner-status.json'
        $deadline = (Get-Date).AddSeconds($HoldSeconds)
        $ready = $false
        Write-Host "Holding $HoldSeconds s for phone-home runner status at $($generated.serverOrigin)/api/session-runners/$($generated.runnerId)/status"
        while ((Get-Date) -lt $deadline) {
            try {
                $st = Invoke-RestMethod -Uri "$($generated.serverOrigin)/api/session-runners/$($generated.runnerId)/status" -UseBasicParsing -TimeoutSec 3
                ($st | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $statusPath -Encoding utf8
                if ($st.available -eq $true) {
                    $ready = $true
                    Write-Host "Runner available=true dispatchEligible=$($st.dispatchEligible) store=$($st.runnerStoreId) epoch=$($st.epoch)"
                    break
                }
            }
            catch { }
            Start-Sleep -Seconds 2
        }
        if (-not $ready) {
            Write-Host "Runner did not become available within $HoldSeconds s. See $statusPath"
        }
    }

    if ($qemuBlockers.Count -gt 0) {
        Write-Host "QEMU ordinary-lane blockers written to $blockerPath (container V-7 is independent)."
    }
    if ($blockers.Count -gt 0) {
        Write-Host "V-7 remaining blockers written to $blockerPath"
        exit 2
    }
    Write-Host "Evidence root: $EvidenceRoot"
    exit 0
}
finally {
    if ($env:PHONE_HOME_SERVER_ORIGIN) {
        try { & docker compose -f $composeFile -p $composeProject down --remove-orphans 2>$null | Out-Null } catch { }
    }
    if ($serverProc -and -not $serverProc.HasExited) {
        try { Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
    if ($pgStarted) {
        try { & docker rm -f $pgName 2>$null | Out-Null } catch { }
    }
    if ($generated -and [string]$generated.placement -eq 'server2' -and $generated.serverPort) {
        $fwName = "CARD-0490-v7-$($generated.serverPort)"
        try { Remove-NetFirewallRule -DisplayName $fwName -ErrorAction SilentlyContinue | Out-Null } catch { }
    }
    $v7bins = Get-ChildItem -Path $repoRoot -Recurse -Directory -Filter 'bin-card0490-v7' -ErrorAction SilentlyContinue
    foreach ($d in $v7bins) {
        try { Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    }
}
