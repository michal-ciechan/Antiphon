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
    [switch] $SkipTurn,
    [int] $HoldSeconds = 180,
    [int] $TurnTimeoutSeconds = 600
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

function Get-Sha256Hex([string] $text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [System.BitConverter]::ToString($hash).Replace('-', '').ToLowerInvariant()
}

function Invoke-IsolatedJson {
    param(
        [Parameter(Mandatory = $true)][string] $Method,
        [Parameter(Mandatory = $true)][string] $Uri,
        $Body = $null,
        [int] $TimeoutSec = 30
    )
    $headers = @{ Accept = 'application/json' }
    $params = @{
        Method          = $Method
        Uri             = $Uri
        Headers         = $headers
        TimeoutSec      = $TimeoutSec
        UseBasicParsing = $true
    }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 8 -Compress }
    }
    try {
        return Invoke-RestMethod @params
    }
    catch {
        $detail = $null
        if ($null -ne $_.ErrorDetails -and $null -ne $_.ErrorDetails.PSObject.Properties['Message']) {
            $detail = [string]$_.ErrorDetails.Message
        }
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = [string]$_.Exception.Message }
        throw "$Method $Uri failed: $detail"
    }
}

function Wait-HttpHealth([string] $url, $proc, [int] $seconds, [string] $name) {
    $up = $false
    for ($i = 0; $i -lt $seconds; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
            if ($r.StatusCode -eq 200) { $up = $true; break }
        }
        catch { }
        if ($proc -and $proc.HasExited) { throw "$name exited early (code $($proc.ExitCode))." }
        Start-Sleep -Seconds 1
    }
    if (-not $up) { throw "$name /health did not return 200 on $url." }
}

function Start-IsolatedDll {
    param(
        [Parameter(Mandatory = $true)][string] $Dll,
        [Parameter(Mandatory = $true)][string] $WorkDir,
        [Parameter(Mandatory = $true)][hashtable] $EnvVars
    )
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'dotnet'
    $psi.Arguments = "`"$Dll`""
    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    foreach ($key in $EnvVars.Keys) {
        $psi.EnvironmentVariables[$key] = [string]$EnvVars[$key]
    }
    $proc = [System.Diagnostics.Process]::Start($psi)
    if (-not $proc) { throw "Failed to start $Dll." }
    return $proc
}

function New-IsolatedLiveConfig {
    $serverPort = Get-IsolatedListenPort
    $postgresPort = Get-IsolatedListenPort
    $runnerPort = Get-IsolatedListenPort
    $runId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $root = Join-Path $repoRoot '.antiphon'
    $runRoot = Join-Path $root ("card0490-live-" + $runId)
    $workspace = [System.IO.Path]::GetFullPath((Join-Path $runRoot 'work'))
    $state = [System.IO.Path]::GetFullPath((Join-Path $runRoot 'state'))
    $grokHome = [System.IO.Path]::GetFullPath((Join-Path $runRoot 'grok-home'))
    $grokSessions = [System.IO.Path]::GetFullPath((Join-Path $runRoot 'grok-sessions'))
    $localRunnerLogs = [System.IO.Path]::GetFullPath((Join-Path $runRoot 'local-runner-logs'))
    $secretFile = Join-Path $runRoot 'phone-home.secret'
    New-Item -ItemType Directory -Force -Path $workspace, $state, $grokHome, $grokSessions, $localRunnerLogs, (Split-Path $secretFile) | Out-Null
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
        runnerPort             = $runnerPort
        localRunnerOrigin      = "http://127.0.0.1:$runnerPort"
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
        localRunnerLogPath     = $localRunnerLogs
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
if ($null -eq $generated.runnerPort -or $generated.runnerPort -eq '') {
    $generated.runnerPort = Get-IsolatedListenPort
}
$generated.localRunnerOrigin = "http://127.0.0.1:$($generated.runnerPort)"
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
Assert-NotProductionPort $generated.runnerPort 'runnerPort'
Assert-NotProductionOrigin ([string]$generated.serverOrigin) 'serverOrigin'
Assert-NotProductionOrigin ([string]$generated.phoneHomeServerOrigin) 'phoneHomeServerOrigin'
Assert-NotProductionOrigin ([string]$generated.localRunnerOrigin) 'localRunnerOrigin'
$generated.hostWorkspaceRoot = [System.IO.Path]::GetFullPath([string]$generated.hostWorkspaceRoot)
$generated.localRunnerOrigin = "http://127.0.0.1:$($generated.runnerPort)"

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
$runnerProc = $null
$pgStarted = $false
$composeProject = "card0490-$($generated.runId)"
$pgPass = $null
$serverDll = $null
$serverOutDir = $null
$runnerDll = $null
$runnerOutDir = $null

function Get-IsolatedServerEnv {
    $secret = (Get-Content -LiteralPath $generated.secretFile -Raw).Trim()
    return @{
        ConnectionStrings__DefaultConnection     = "Host=127.0.0.1;Port=$($generated.postgresPort);Database=antiphon_card0490;Username=antiphon;Password=$pgPass"
        ASPNETCORE_URLS                          = "http://0.0.0.0:$($generated.serverPort)"
        SessionRunner__BaseUrl                   = [string]$generated.localRunnerOrigin
        SessionRunner__Enabled                   = 'true'
        Agents__DefaultDefinition                = 'grok'
        Delegation__CheckInterpreterEnabled      = 'false'
        Delegation__DiagnoseEnabled              = 'false'
        Delegation__OutputDistillerEnabled       = 'false'
        Hangfire__ServerEnabled                  = 'false'
        AgentTui__ImportProfilesOnStartup        = 'false'
        PhoneHomeRunner__Enabled                 = 'true'
        PhoneHomeRunner__AllowedRunnerId         = [string]$generated.runnerId
        PhoneHomeRunner__StandingAgentId         = [string]$generated.standingAgentId
        PhoneHomeRunner__HostWorkspaceRoot       = [string]$generated.hostWorkspaceRoot
        PhoneHomeRunner__RunnerWorkspace         = '/work'
        PhoneHomeRunner__ChildGrokHome           = '/state/grok'
        PhoneHomeRunner__CallbackOrigin          = [string]$generated.phoneHomeServerOrigin
        PhoneHomeRunner__SharedSecret            = $secret
    }
}

function Stop-OwnedProcess($proc) {
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
        try { $proc.WaitForExit(5000) | Out-Null } catch { }
    }
}

function Write-RunnerDiagnostics {
    $logPath = Join-Path $EvidenceRoot 'container-logs.txt'
    try {
        & docker compose -f $composeFile -p $composeProject logs --no-color --tail 400 2>&1 |
            Set-Content -LiteralPath $logPath -Encoding utf8
        Write-Host "Wrote container logs to $logPath"
    }
    catch {
        Write-Host "Could not capture container logs: $($_.Exception.Message)"
    }
    $hostLogDir = Join-Path ([string]$generated.stateRoot) 'session-runner\pty-hosts\logs'
    if (Test-Path -LiteralPath $hostLogDir) {
        Get-ChildItem -LiteralPath $hostLogDir -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $EvidenceRoot $_.Name) -Force
        }
    }
    $ptyCopy = Join-Path $EvidenceRoot 'pty-hosts-tmp'
    try {
        New-Item -ItemType Directory -Force -Path $ptyCopy | Out-Null
        & docker compose -f $composeFile -p $composeProject cp session-runner-grok:/tmp/antiphon-pty-hosts $ptyCopy 2>$null
        & docker compose -f $composeFile -p $composeProject exec -T session-runner-grok sh -c 'ls -la /tmp/antiphon-pty* /tmp/CoreFxPipe_* 2>/dev/null; echo TMPDIR=$TMPDIR; echo tmp=$(ls -la /tmp | head)' 2>$null |
            Set-Content -LiteralPath (Join-Path $EvidenceRoot 'container-tmp.txt') -Encoding utf8
    }
    catch { }
    $runnerLogs = Join-Path ([string]$generated.stateRoot) 'runner-logs'
    if (Test-Path -LiteralPath $runnerLogs) {
        Get-ChildItem -LiteralPath $runnerLogs -File -ErrorAction SilentlyContinue | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $EvidenceRoot $_.Name) -Force
        }
    }
}

function Start-IsolatedServerProcess {
    Write-Host "Starting isolated Antiphon.Server at $($generated.serverOrigin) SessionRunner__BaseUrl=$($generated.localRunnerOrigin) StandingAgentId=$($generated.standingAgentId)"
    $script:serverProc = Start-IsolatedDll -Dll $serverDll -WorkDir $serverOutDir -EnvVars (Get-IsolatedServerEnv)
    Wait-HttpHealth "$($generated.serverOrigin)/health" $script:serverProc 90 'Isolated server'
    Write-Host "Isolated server healthy: $($generated.serverOrigin)/health"
}

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

    $runnerOutDir = Join-Path $repoRoot 'src/Antiphon.SessionRunner/bin-card0490-v7-runner'
    Write-Host "Building isolated Antiphon.SessionRunner to $runnerOutDir"
    & dotnet build (Join-Path $repoRoot 'src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj') --property:OutputPath=bin-card0490-v7-runner/ --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Isolated local runner build failed." }
    $runnerDll = Join-Path $runnerOutDir 'Antiphon.SessionRunner.dll'
    if ([string]::IsNullOrWhiteSpace([string]$generated.localRunnerLogPath)) {
        $generated.localRunnerLogPath = Join-Path (Split-Path -Parent ([string]$generated.stateRoot)) 'local-runner-logs'
    }
    New-Item -ItemType Directory -Force -Path ([string]$generated.localRunnerLogPath) | Out-Null
    Write-Host "Starting isolated local SessionRunner at $($generated.localRunnerOrigin) (not 17204, not dead-port 1)"
    $runnerProc = Start-IsolatedDll -Dll $runnerDll -WorkDir $runnerOutDir -EnvVars @{
        ASPNETCORE_URLS                = [string]$generated.localRunnerOrigin
        SessionRunner__SessionLogPath  = [string]$generated.localRunnerLogPath
        PhoneHome__Enabled             = 'false'
        Serilog__LogPath               = [string]$generated.localRunnerLogPath
    }
    Wait-HttpHealth "$($generated.localRunnerOrigin)/health" $runnerProc 30 'Isolated local SessionRunner'
    Write-Host "Isolated local SessionRunner healthy: $($generated.localRunnerOrigin)/health"

    $serverOutDir = Join-Path $repoRoot 'server/bin-card0490-v7'
    Write-Host "Building isolated Antiphon.Server to $serverOutDir"
    & dotnet build (Join-Path $repoRoot 'server/Antiphon.Server.csproj') --property:OutputPath=bin-card0490-v7/ --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Isolated server build failed." }
    $serverDll = Join-Path $serverOutDir 'Antiphon.Server.dll'
    Start-IsolatedServerProcess

    $origin = [string]$generated.serverOrigin
    $standingId = [string]$generated.standingAgentId
    $agent = $null
    try { $agent = Invoke-IsolatedJson GET "$origin/api/agents/$standingId" } catch { $agent = $null }
    if (-not $agent) {
        Write-Host "Creating standing Grok agent in workspace $($generated.hostWorkspaceRoot)"
        $created = Invoke-IsolatedJson POST "$origin/api/agents" @{
            name                     = 'phone-home-grok'
            workingDirectory         = [string]$generated.hostWorkspaceRoot
            createWorkingDirectory   = $true
            alwaysOn                 = $false
            details                  = 'CARD-0490 V-7 isolated Grok phone-home canary'
        }
        $assign = [string]$created.assignmentPolicy
        if ([string]::IsNullOrWhiteSpace($assign) -or $assign -eq '0') { $assign = 'AutoPick' }
        $patch = @{
            name                       = $created.name
            workingDirectory           = $created.workingDirectory
            details                    = $created.details
            defaultWorkflowTemplateId  = $created.defaultWorkflowTemplateId
            assignmentPolicy           = $assign
            alwaysOn                   = $false
            kind                       = 'Grok'
            sessionBackend             = 'PtyHost'
        }
        $agent = Invoke-IsolatedJson PATCH "$origin/api/agents/$($created.id)" $patch
        if ([string]$agent.id -ne $standingId) {
            Write-Host "Standing agent id $($agent.id) differs from configured $standingId; restarting isolated server before the container so epoch stays 1."
            $generated.standingAgentId = [string]$agent.id
            ($generated | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $configPath -Encoding utf8
            Stop-OwnedProcess $serverProc
            $serverProc = $null
            Start-Sleep -Seconds 2
            Start-IsolatedServerProcess
            $agent = Invoke-IsolatedJson GET "$origin/api/agents/$($generated.standingAgentId)"
        }
    }
    elseif ([string]$agent.kind -ne 'Grok' -and [string]$agent.kind -ne '4') {
        $assign = [string]$agent.assignmentPolicy
        if ([string]::IsNullOrWhiteSpace($assign) -or $assign -eq '0') { $assign = 'AutoPick' }
        $patch = @{
            name                       = $agent.name
            workingDirectory           = $agent.workingDirectory
            details                    = $agent.details
            defaultWorkflowTemplateId  = $agent.defaultWorkflowTemplateId
            assignmentPolicy           = $assign
            alwaysOn                   = $false
            kind                       = 'Grok'
            sessionBackend             = 'PtyHost'
        }
        $agent = Invoke-IsolatedJson PATCH "$origin/api/agents/$($agent.id)" $patch
    }
    Write-Host "Standing Grok agent id=$($agent.id) kind=$($agent.kind) cwd=$($agent.workingDirectory)"

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

    $statusPath = Join-Path $EvidenceRoot 'runner-status.json'
    $st = $null
    if ($HoldSeconds -gt 0 -and $blockers.Count -eq 0 -and -not $SkipContainer -and [string]$generated.placement -ne 'server2') {
        $deadline = (Get-Date).AddSeconds($HoldSeconds)
        Write-Host "Holding $HoldSeconds s for dispatchEligible phone-home runner at $origin/api/session-runners/$($generated.runnerId)/status"
        while ((Get-Date) -lt $deadline) {
            try {
                $st = Invoke-RestMethod -Uri "$origin/api/session-runners/$($generated.runnerId)/status" -UseBasicParsing -TimeoutSec 3
                ($st | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $statusPath -Encoding utf8
                if ($st.dispatchEligible -eq $true -and $st.available -eq $true) {
                    Write-Host "Runner available=true dispatchEligible=true store=$($st.runnerStoreId) boot=$($st.processBootId) epoch=$($st.epoch)"
                    break
                }
                Write-Host "Runner available=$($st.available) dispatchEligible=$($st.dispatchEligible) epoch=$($st.epoch) heartbeat=$($st.lastHeartbeatUtc)"
            }
            catch {
                Write-Host "Runner status not ready: $($_.Exception.Message)"
            }
            Start-Sleep -Seconds 2
        }
        if (-not $st -or $st.dispatchEligible -ne $true) {
            throw "Phone-home runner did not become dispatchEligible within $HoldSeconds s. See $statusPath"
        }
    }

    $turnEvidencePath = Join-Path $EvidenceRoot 'v7-turn.json'
    if (-not $SkipTurn -and -not $SkipContainer -and $blockers.Count -eq 0 -and [string]$generated.placement -ne 'server2') {
        if (-not $st -or $st.dispatchEligible -ne $true) {
            throw "V-7 turn requires dispatchEligible=true before start."
        }
        $nonce = [Guid]::NewGuid().ToString('N').Substring(0, 8)
        $prompt = "Reply with PHONE_HOME_OK_$nonce and do not use tools."
        $promptHash = Get-Sha256Hex $prompt
        Write-Host "Starting pinned standing Grok with fresh=true (first launch; no native history yet)"
        $started = Invoke-IsolatedJson POST "$origin/api/agents/$($generated.standingAgentId)/start" @{ fresh = $true } 60
        $sessionId = $null
        if ($started.liveSession -and $started.liveSession.id) { $sessionId = [string]$started.liveSession.id }
        elseif ($started.persistentSessionId) { $sessionId = [string]$started.persistentSessionId }
        if ([string]::IsNullOrWhiteSpace($sessionId)) { throw "Start did not return a session id: $($started | ConvertTo-Json -Depth 4 -Compress)" }
        $generation = $null
        if ($started.liveSession -and $started.liveSession.startedAt) { $generation = [string]$started.liveSession.startedAt }
        Write-Host "Queued standing start session $sessionId generation=$generation; waiting for liveSession.status=Running"

        $idleDeadline = (Get-Date).AddSeconds([Math]::Min($TurnTimeoutSeconds, 180))
        $readyForPrompt = $false
        while ((Get-Date) -lt $idleDeadline) {
            $detail = Invoke-IsolatedJson GET "$origin/api/agents/$($generated.standingAgentId)"
            $working = $false
            if ($null -ne $detail.working) { $working = [bool]$detail.working }
            $sessionStatus = $null
            $failureReason = $null
            if ($detail.liveSession) {
                $sessionId = [string]$detail.liveSession.id
                $generation = [string]$detail.liveSession.startedAt
                $sessionStatus = [string]$detail.liveSession.status
                if ($detail.liveSession.failureReason) { $failureReason = [string]$detail.liveSession.failureReason }
            }
            if ($sessionStatus -eq 'Failed' -or $sessionStatus -eq '3') {
                Write-RunnerDiagnostics
                throw "Standing start failed (session $sessionId): $failureReason"
            }
            if ($sessionStatus -eq 'Running' -or $sessionStatus -eq '1') {
                if (-not $working) { $readyForPrompt = $true; break }
            }
            Write-Host "liveSession.status=$sessionStatus working=$working failure=$failureReason"
            Start-Sleep -Seconds 3
        }
        if (-not $readyForPrompt) {
            Write-RunnerDiagnostics
            throw "Session $sessionId did not become Running+idle within the rules wait. Not queueing a turn into a failed or still-starting host."
        }

        $attemptFloor = [DateTime]::UtcNow
        $queued = Invoke-IsolatedJson POST "$origin/api/sessions/$sessionId/messages" @{ body = $prompt; mode = 'WhenIdle' } 60
        $queueId = $null
        $lastQueued = @($queued.messages) | Select-Object -Last 1
        if ($lastQueued -and $lastQueued.id) { $queueId = [string]$lastQueued.id }
        elseif ($queued.id) { $queueId = [string]$queued.id }
        Write-Host "Queued canary queueId=$queueId session=$sessionId"

        $userPrompt = $null
        $assistant = $null
        $turnEnd = $null
        $transcript = $null
        $turnDeadline = (Get-Date).AddSeconds($TurnTimeoutSeconds)
        while ((Get-Date) -lt $turnDeadline) {
            $transcript = Invoke-IsolatedJson GET "$origin/api/sessions/$sessionId/transcript"
            foreach ($entry in @($transcript.entries)) {
                $kind = [string]$entry.kind
                $text = [string]$entry.text
                $ts = $null
                if ($entry.timestamp) { try { $ts = [DateTime]$entry.timestamp } catch { $ts = $null } }
                if ($kind -eq 'UserPrompt' -and $text -and ($text.Contains($prompt) -or $text.Contains("PHONE_HOME_OK_$nonce"))) {
                    if ($null -eq $ts -or $ts.ToUniversalTime() -ge $attemptFloor.AddSeconds(-5)) { $userPrompt = $entry }
                }
                if ($kind -eq 'AssistantText' -and $text -and $text.Contains("PHONE_HOME_OK_$nonce")) { $assistant = $entry }
                if ($kind -eq 'TurnEnd' -and $userPrompt) { $turnEnd = $entry }
            }
            if ($userPrompt -and $assistant -and $turnEnd) { break }
            Start-Sleep -Seconds 4
        }
        if (-not $userPrompt -or -not $assistant -or -not $turnEnd) {
            ($transcript | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath (Join-Path $EvidenceRoot 'transcript-incomplete.json') -Encoding utf8
            Write-RunnerDiagnostics
            throw "V-7 turn incomplete. userPrompt=$(if ($userPrompt) {'yes'} else {'no'}) assistantNonce=$(if ($assistant) {'yes'} else {'no'}) turnEnd=$(if ($turnEnd) {'yes'} else {'no'})."
        }

        $updates = @()
        if (Test-Path -LiteralPath ([string]$generated.grokSessionsMount)) {
            $updates = @(Get-ChildItem -LiteralPath ([string]$generated.grokSessionsMount) -Recurse -Filter 'updates.jsonl' -ErrorAction SilentlyContinue)
        }
        $updatesRel = $null
        if ($updates.Count -gt 0) {
            $full = $updates[0].FullName
            $root = [string]$generated.grokSessionsMount
            $updatesRel = $full.Substring($root.Length).TrimStart('\', '/')
            $updatesRel = ('sessions/' + ($updatesRel -replace '\\', '/'))
        }

        $imageId = $null
        try {
            $imageId = (& docker compose -f $composeFile -p $composeProject images -q session-runner-grok 2>$null | Select-Object -First 1)
        } catch { }
        $grokVer = $null
        try {
            $grokVer = (& docker compose -f $composeFile -p $composeProject exec -T session-runner-grok grok --version 2>$null | Out-String).Trim()
        } catch { }
        $sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim()

        $evidence = [ordered]@{
            sourceSha            = $sourceSha
            imageDigest          = $imageId
            grokVersion          = $grokVer
            runnerId             = [string]$generated.runnerId
            runnerStoreId        = $st.runnerStoreId
            processBootId        = $st.processBootId
            epoch                = $st.epoch
            sessionId            = $sessionId
            acceptedGeneration   = $generation
            queueId              = $queueId
            attemptFloorUtc      = $attemptFloor.ToString('o')
            submittedBodyHash    = $promptHash
            nonce                = $nonce
            userPrompt           = @{
                uuid     = $userPrompt.uuid
                sequence = $userPrompt.sequence
                textHash = Get-Sha256Hex ([string]$userPrompt.text)
                timestamp = $userPrompt.timestamp
            }
            assistantText        = @{
                uuid     = $assistant.uuid
                sequence = $assistant.sequence
                hasNonce = $true
            }
            turnEnd              = @{
                uuid     = $turnEnd.uuid
                sequence = $turnEnd.sequence
                kind     = $turnEnd.kind
            }
            linuxUpdatesJsonl    = $updatesRel
            localRunnerOrigin    = [string]$generated.localRunnerOrigin
        }
        ($evidence | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $turnEvidencePath -Encoding utf8
        Write-Host "V-7 turn confirmed. Evidence: $turnEvidencePath"

        try {
            Invoke-IsolatedJson POST "$origin/api/agents/$($generated.standingAgentId)/stop" @{} 30 | Out-Null
            Write-Host "Stopped standing session $sessionId generation=$generation"
        }
        catch {
            Write-Host "Stop after V-7 turn: $($_.Exception.Message)"
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
    Stop-OwnedProcess $serverProc
    Stop-OwnedProcess $runnerProc
    if ($pgStarted) {
        try { & docker rm -f $pgName 2>$null | Out-Null } catch { }
    }
    if ($generated -and [string]$generated.placement -eq 'server2' -and $generated.serverPort) {
        $fwName = "CARD-0490-v7-$($generated.serverPort)"
        try { Remove-NetFirewallRule -DisplayName $fwName -ErrorAction SilentlyContinue | Out-Null } catch { }
    }
    $v7bins = Get-ChildItem -Path $repoRoot -Recurse -Directory -Filter 'bin-card0490-v7*' -ErrorAction SilentlyContinue
    foreach ($d in $v7bins) {
        try { Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    }
}
