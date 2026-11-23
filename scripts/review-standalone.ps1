<#
.SYNOPSIS
  Runs a review-only Antiphon server (and optionally its client) on this machine, for checking the
  review flow on a machine without the AppHost, such as the laptop. It uses no session runner, its
  own Postgres, and the antiphon-review GitHub App. Reviewers, verifier and decisions run as
  one-shot `claude -p` calls, and GitHub polling is on.

.DESCRIPTION
  - Postgres: a container named antiphon-review-standalone-pg on -PostgresPort, with its own volume.
    Never the dev stack's 17280.
  - Repo mirror: a clone at -MirrorPath that reviewer worktrees are cut from. The main checkout is
    never used.
  - Server: dotnet run --project server on -Port, isolated like the test hosts. The session runner
    and the message broker point at a dead address, and Hangfire, the zombie census, schedules, card
    file sync and the worktree janitors are off. The review settings and the app's key come from
    Bitwarden into this process's environment only; nothing is written to user-secrets, which test
    hosts would read.
  - Client: Vite on -ClientPort, proxying to the server.
  Ports 17202-17205 belong to the canonical stack and are refused.

  Logs: logs/review-standalone/{server,client}.log. Stop with -Stop.

.EXAMPLE
  pwsh -NoProfile -File scripts/review-standalone.ps1
  pwsh -NoProfile -File scripts/review-standalone.ps1 -Stop
#>
param(
    [int]$Port = 17262,
    [int]$ClientPort = 17263,
    [int]$PostgresPort = 17290,
    [string]$Repo = 'michal-ciechan/Antiphon',
    [string]$MirrorPath = 'C:\src\antiphon-review-mirror',
    [string]$VaultItem = 'GitHub App - antiphon-review',
    [string]$ViewerLogin = 'michal-ciechan',
    [int]$PollSeconds = 120,
    # Where links in GitHub statuses and comments point. For a phone on the tailnet, pass the
    # machine's Caddy name, e.g. https://review.laptop.codeperf.net (see the proxy skill).
    # Vite still listens on 127.0.0.1 only: Docker Desktop's host.docker.internal reaches it, and
    # Vite's allowedHosts already accepts *.laptop/*.desktop.codeperf.net.
    [string]$PublicBaseUrl,
    [switch]$NoClient,
    # Leave GitHub polling off (a manual POST /api/review-polling/poll still runs a pass).
    [switch]$NoPolling,
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $root 'logs\review-standalone'
$pidFile = Join-Path $logDir 'pids.json'
New-Item -ItemType Directory -Force $logDir | Out-Null

function Stop-Standalone {
    if (-not (Test-Path -LiteralPath $pidFile)) { Write-Host 'Nothing recorded as running.'; return }
    $pids = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json
    foreach ($name in 'server', 'client') {
        $id = $pids.$name
        if (-not $id) { continue }
        $p = Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue
        if ($p -and $p.CommandLine -match 'review-standalone|Antiphon.Server|vite|dotnet') {
            & taskkill /PID $id /T /F | Out-Null
            Write-Host "Stopped $name ($id)."
        }
    }
    Remove-Item -LiteralPath $pidFile -Force
}

if ($Stop) { Stop-Standalone; return }

foreach ($p in $Port, $ClientPort, $PostgresPort) {
    if ($p -ge 17202 -and $p -le 17205) { throw "Port $p belongs to the canonical stack (17202-17205)." }
    if ($p -eq 17280) { throw 'Port 17280 is the dev stack Postgres.' }
}
if (Test-Path -LiteralPath $pidFile) { throw "Already running (see $pidFile); use -Stop first." }

# --- Postgres -----------------------------------------------------------------------------------
$pg = 'antiphon-review-standalone-pg'
$state = (& docker inspect -f '{{.State.Running}}' $pg 2>$null)
if ($LASTEXITCODE -ne 0) {
    & docker run -d --name $pg -p "127.0.0.1:${PostgresPort}:5432" -e POSTGRES_DB=antiphon -e POSTGRES_USER=antiphon `
        -e POSTGRES_PASSWORD=antiphon_dev -v antiphon_review_standalone_pgdata:/var/lib/postgresql/data postgres:16 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker run for Postgres failed.' }
} elseif ($state -ne 'true') {
    & docker start $pg | Out-Null
}
$deadline = (Get-Date).AddSeconds(60)
while ($true) {
    & docker exec $pg pg_isready -U antiphon -d antiphon 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { break }
    if ((Get-Date) -gt $deadline) { throw 'Postgres did not become ready in 60 s.' }
    Start-Sleep -Seconds 1
}

# --- Repo mirror for reviewer worktrees --------------------------------------------------------
if (-not (Test-Path -LiteralPath (Join-Path $MirrorPath '.git'))) {
    & git clone --quiet "https://github.com/$Repo.git" $MirrorPath
    if ($LASTEXITCODE -ne 0) { throw "git clone into $MirrorPath failed." }
} else {
    & git -C $MirrorPath fetch --quiet --prune origin
}

# --- The app and review settings, from Bitwarden into this process only ------------------------
if (-not $env:BW_SESSION) {
    $sessionFile = Join-Path $HOME '.bw-session'
    if (-not (Test-Path -LiteralPath $sessionFile)) { throw 'Bitwarden is locked: no BW_SESSION and no ~/.bw-session.' }
    $env:BW_SESSION = (Get-Content -LiteralPath $sessionFile -Raw).Trim()
}
$raw = & bw get item $VaultItem --nointeraction
if ($LASTEXITCODE -ne 0 -or -not $raw) { throw "Bitwarden item '$VaultItem' not found or the vault is locked." }
$item = $raw | ConvertFrom-Json
$raw = $null
function Get-Field([string]$name) {
    $f = $item.fields | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if (-not $f -or -not $f.value) { throw "Bitwarden item '$VaultItem' has no '$name' field." }
    return [string]$f.value
}

$work = Join-Path $env:TEMP 'antiphon-review-standalone'
New-Item -ItemType Directory -Force $work | Out-Null
$settings = [ordered]@{
    'ASPNETCORE_ENVIRONMENT'                         = 'Development'
    'ASPNETCORE_URLS'                                = "http://127.0.0.1:$Port"
    'ConnectionStrings__DefaultConnection'           = "Host=127.0.0.1;Port=$PostgresPort;Database=antiphon;Username=antiphon;Password=antiphon_dev"
    # Isolation, as the test hosts do it (ProductionRunnerGuard): nothing here starts a process on a runner.
    'SessionRunner__BaseUrl'                         = 'http://127.0.0.1:1'
    'SessionRunner__Enabled'                         = 'false'
    'Delegation__CheckInterpreterEnabled'            = 'false'
    'Delegation__DiagnoseEnabled'                    = 'false'
    'Delegation__OutputDistillerEnabled'             = 'false'
    'Hangfire__ServerEnabled'                        = 'false'
    'ZombieCensus__Enabled'                          = 'false'
    'Schedules__Enabled'                             = 'false'
    'CardFileSync__Enabled'                          = 'false'
    'WorktreeResidue__Enabled'                       = 'false'
    'WorktreeLegacyRelease__Enabled'                 = 'false'
    'RunnerWorktreeRetention__Enabled'               = 'false'
    'AgentTui__ImportProfilesOnStartup'              = 'false'
    'GitHub__Enabled'                                = 'false'
    'AntiphonMessaging__BootstrapServers'            = '127.0.0.1:1'
    'Git__WorkspacePath'                             = $work
    'Git__WorktreeBasePath'                          = (Join-Path $work 'worktrees')
    # The review engine.
    'Review__CodeHosts__Hosts__github__Kind'                 = 'GitHub'
    'Review__CodeHosts__Hosts__github__App__AppId'           = (Get-Field 'Client ID')
    'Review__CodeHosts__Hosts__github__App__InstallationId'  = (Get-Field 'Installation ID')
    'Review__Secrets__GitHubAppPrivateKey'                   = [string]$item.notes
    'Review__PublicBaseUrl'                                  = $(if ($PublicBaseUrl) { $PublicBaseUrl.TrimEnd('/') } else { "http://localhost:$ClientPort" })
    'Review__DefaultViewerLogin'                             = $ViewerLogin
    'Review__Policy__AuthorMayDecideRepos__0'                = $Repo
    'Review__Agents__Transport'                              = 'cli'
    'Review__Agents__Repos__0__Repo'                         = $Repo
    'Review__Agents__Repos__0__LocalPath'                    = $MirrorPath
    'Review__Completions__Providers__haiku-cli__Dialect'     = 'claude-cli'
    'Review__Completions__Providers__haiku-cli__Model'       = 'haiku'
    'Review__Completions__Chains__completions-cheap__0__Provider' = 'haiku-cli'
    'Review__Decisions__Providers__haiku-cli__Dialect'       = 'claude-cli'
    'Review__Decisions__Providers__haiku-cli__Model'         = 'haiku'
    'Review__Decisions__Chains__decisions-default__0__Provider' = 'haiku-cli'
    'Review__Polling__Enabled'                               = $(if ($NoPolling) { 'false' } else { 'true' })
    'Review__Polling__IntervalSeconds'                       = "$PollSeconds"
    'Review__Polling__Repos__0__Repo'                        = $Repo
}
$item = $null
foreach ($k in $settings.Keys) { [Environment]::SetEnvironmentVariable($k, $settings[$k], 'Process') }
$settings = $null

# --- Server -------------------------------------------------------------------------------------
$serverLog = Join-Path $logDir 'server.log'
$server = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', (Join-Path $root 'server'), '--no-launch-profile') `
    -WorkingDirectory $root -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $logDir 'server.err.log') `
    -WindowStyle Hidden -PassThru
Remove-Item Env:Review__Secrets__GitHubAppPrivateKey -ErrorAction SilentlyContinue

$client = $null
if (-not $NoClient) {
    $env:services__server__http__0 = "http://127.0.0.1:$Port"
    $env:VITE_PORT = "$ClientPort"
    $client = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', 'npx', 'vite', '--host', '127.0.0.1') -WorkingDirectory (Join-Path $root 'client') `
        -RedirectStandardOutput (Join-Path $logDir 'client.log') -RedirectStandardError (Join-Path $logDir 'client.err.log') `
        -WindowStyle Hidden -PassThru
}

@{ server = $server.Id; client = $(if ($client) { $client.Id } else { $null }) } | ConvertTo-Json | Set-Content -LiteralPath $pidFile -Encoding ascii
Write-Host "Server pid $($server.Id) on http://127.0.0.1:$Port (log $serverLog)."
if ($client) { Write-Host "Client pid $($client.Id) on http://localhost:$ClientPort." }
Write-Host 'Wait for GET /health, then open /reviews. Stop with -Stop.'
