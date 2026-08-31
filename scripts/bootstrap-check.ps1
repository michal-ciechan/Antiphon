# ASCII-only: must parse under Windows PowerShell 5.1 (pwsh 7 may be the
# missing piece on a half-bootstrapped machine). Read-only probes; the only
# side effect is the optional HNS docker-network create/rm, and only when
# -NoStack skips Phase B (verify-dev-stack.ps1 already owns that probe).
#
# Exit code = number of FAILs. WARN-only items never affect the exit code.
#
# Usage:
#   pwsh -File scripts/bootstrap-check.ps1
#   powershell.exe -File scripts/bootstrap-check.ps1 -NoStack

[CmdletBinding()]
param(
    [switch]$NoStack
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot

$script:FailCount = 0
$script:WarnCount = 0
$script:PassCount = 0

function Write-Check {
    param(
        [string]$Name,
        [ValidateSet('PASS', 'WARN', 'FAIL')]
        [string]$Status,
        [string]$Detail,
        [string]$Remedial = ''
    )
    switch ($Status) {
        'PASS' { $script:PassCount++; $color = 'Green' }
        'WARN' { $script:WarnCount++; $color = 'Yellow' }
        'FAIL' { $script:FailCount++; $color = 'Red' }
    }
    $line = ('  [{0}] {1,-28} {2}' -f $Status, $Name, $Detail)
    Write-Host $line -ForegroundColor $color
    if ($Remedial -and $Status -ne 'PASS') {
        foreach ($rline in ($Remedial -split "`n")) {
            if ($rline.Trim().Length -gt 0) {
                Write-Host ('           {0}' -f $rline) -ForegroundColor DarkGray
            }
        }
    }
}

function Get-CommandPath {
    param([string[]]$Names)
    foreach ($n in $Names) {
        $cmd = Get-Command $n -ErrorAction SilentlyContinue
        if ($cmd -and $cmd.Source) { return $cmd.Source }
        if ($cmd -and $cmd.Path) { return $cmd.Path }
    }
    return $null
}

function Get-NativeOutput {
    param(
        [string]$File,
        [string[]]$Arguments = @()
    )
    try {
        $out = & $File @Arguments 2>&1
        return @{
            Text      = (($out | ForEach-Object { "$_" }) -join "`n").Trim()
            ExitCode  = $LASTEXITCODE
            Succeeded = ($LASTEXITCODE -eq 0 -or $null -eq $LASTEXITCODE)
        }
    } catch {
        return @{
            Text      = $_.Exception.Message
            ExitCode  = 1
            Succeeded = $false
        }
    }
}

function ConvertFrom-JsonSafe {
    param([string]$Raw)
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    try {
        $cmd = Get-Command ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($cmd -and $cmd.Parameters.ContainsKey('Depth')) {
            return $Raw | ConvertFrom-Json -Depth 32
        }
        return $Raw | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Read-JsonFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        $raw = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
        return ConvertFrom-JsonSafe $raw
    } catch {
        return $null
    }
}

function Test-NonEmptyText {
    param($Value)
    if ($null -eq $Value) { return $false }
    return -not [string]::IsNullOrWhiteSpace([string]$Value)
}

function Test-LlmProviderKeyInConfig {
    param($Config)
    if ($null -eq $Config) { return $false }
    try {
        $providers = $Config.Llm.Providers
    } catch {
        return $false
    }
    if ($null -eq $providers) { return $false }
    foreach ($name in @('anthropic', 'openai')) {
        try {
            $p = $providers.$name
            if ($p -and (Test-NonEmptyText $p.ApiKey)) { return $true }
        } catch {
            # property missing
        }
    }
    return $false
}

function Test-LlmProviderKeyInUserSecretsText {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    # Match a non-empty value after '=' without capturing it for output.
    return [bool]($Text -match '(?im)^Llm:Providers:(anthropic|openai):ApiKey\s*=\s*\S')
}

function Test-TcpPort {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMs = 2000
    )
    $client = $null
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $iar = $client.BeginConnect($HostName, $Port, $null, $null)
        $waited = $iar.AsyncWaitHandle.WaitOne($TimeoutMs, $false)
        if (-not $waited) { return $false }
        $client.EndConnect($iar)
        return $client.Connected
    } catch {
        return $false
    } finally {
        if ($client) { $client.Close() }
    }
}

function Invoke-TimedJob {
    param(
        [scriptblock]$ScriptBlock,
        [object[]]$ArgumentList = @(),
        [int]$TimeoutSec = 12
    )
    $job = $null
    try {
        if ($ArgumentList.Count -gt 0) {
            $job = Start-Job -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList
        } else {
            $job = Start-Job -ScriptBlock $ScriptBlock
        }
        $done = Wait-Job $job -Timeout $TimeoutSec
        if (-not $done) {
            Stop-Job $job -ErrorAction SilentlyContinue
            Remove-Job $job -Force -ErrorAction SilentlyContinue
            return @{ TimedOut = $true; Output = $null; State = 'Timeout' }
        }
        $out = Receive-Job $job
        $state = $job.State
        Remove-Job $job -Force -ErrorAction SilentlyContinue
        return @{ TimedOut = $false; Output = $out; State = $state }
    } catch {
        if ($job) { Remove-Job $job -Force -ErrorAction SilentlyContinue }
        return @{ TimedOut = $false; Output = $_.Exception.Message; State = 'Failed' }
    }
}

function Get-PwshExePath {
    $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\pwsh.exe'
    if (Test-Path -LiteralPath $alias) { return $alias }
    $cmd = Get-CommandPath @('pwsh.exe', 'pwsh')
    if ($cmd) { return $cmd }
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $here = Join-Path $PSHOME 'pwsh.exe'
        if (Test-Path -LiteralPath $here) { return $here }
    }
    return $null
}

function Test-SdkSatisfiesGlobalJson {
    param(
        [string]$Installed,
        [string]$Pinned,
        [string]$RollForward
    )
    if ([string]::IsNullOrWhiteSpace($Installed) -or [string]::IsNullOrWhiteSpace($Pinned)) {
        return $false
    }
    try {
        $a = [version]$Installed
        $b = [version]$Pinned
    } catch {
        return $false
    }
    $policy = $RollForward
    if ([string]::IsNullOrWhiteSpace($policy)) { $policy = 'latestPatch' }
    switch ($policy.ToLowerInvariant()) {
        'disable' { return $Installed -eq $Pinned }
        'latestmajor' { return $a -ge $b }
        'major' { return $a -ge $b }
        'latestminor' { return ($a.Major -eq $b.Major) -and ($a -ge $b) }
        'minor' { return ($a.Major -eq $b.Major) -and ($a -ge $b) }
        'latestfeature' { return ($a.Major -eq $b.Major) -and ($a.Minor -eq $b.Minor) -and ($a -ge $b) }
        'feature' { return ($a.Major -eq $b.Major) -and ($a.Minor -eq $b.Minor) -and ($a -ge $b) }
        default { return ($a.Major -eq $b.Major) -and ($a.Minor -eq $b.Minor) -and ($a -ge $b) }
    }
}

Write-Host 'See docs/bootstrap.md for the bootstrap checklist this script checks.'
Write-Host ''
if ($NoStack) {
    Write-Host 'Antiphon bootstrap check (Phase A only; -NoStack)'
} else {
    Write-Host 'Antiphon bootstrap check (Phase A + live stack)'
}
Write-Host ''

# CARD-0254: this must run from a normal shared verification front door, not
# from an optional local hook.
$agentContextCheck = Join-Path $repoRoot 'scripts\check-agent-context.ps1'
if (Test-Path -LiteralPath $agentContextCheck) {
    & $agentContextCheck
    if ($LASTEXITCODE -eq 0) {
        Write-Check 'agent context' 'PASS' 'AGENTS.md byte budget and section report passed'
    } else {
        Write-Check 'agent context' 'FAIL' 'AGENTS.md violated its byte budget or could not be read' 'Run pwsh -NoProfile -File scripts/check-agent-context.ps1 and restore the 24,576-byte target.'
    }
} else {
    Write-Check 'agent context' 'FAIL' 'scripts/check-agent-context.ps1 is missing' 'Restore the repository instruction-context verification script.'
}

# ---------------------------------------------------------------------------
# 1. Toolchain
# ---------------------------------------------------------------------------

$globalJsonPath = Join-Path $repoRoot 'global.json'
$globalJson = Read-JsonFile $globalJsonPath
$pinnedSdk = $null
$rollForward = 'latestMinor'
if ($globalJson -and $globalJson.sdk) {
    $pinnedSdk = [string]$globalJson.sdk.version
    if (Test-NonEmptyText $globalJson.sdk.rollForward) {
        $rollForward = [string]$globalJson.sdk.rollForward
    }
}

$dotnetPath = Get-CommandPath @('dotnet.exe', 'dotnet')
if (-not $dotnetPath) {
    Write-Check 'toolchain / dotnet' 'FAIL' 'dotnet not on PATH' 'Install the .NET SDK pinned by global.json, then reopen the shell.'
} elseif (-not $pinnedSdk) {
    Write-Check 'toolchain / dotnet' 'FAIL' 'global.json missing or unreadable' ('Expected {0}' -f $globalJsonPath)
} else {
    $prev = Get-Location
    $sdkText = $null
    $sdkOk = $false
    try {
        Set-Location $repoRoot
        $sdkRun = Get-NativeOutput -File $dotnetPath -Arguments @('--version')
        $sdkText = $sdkRun.Text
        if ($sdkRun.Succeeded -and (Test-SdkSatisfiesGlobalJson -Installed $sdkText -Pinned $pinnedSdk -RollForward $rollForward)) {
            $sdkOk = $true
        }
    } finally {
        Set-Location $prev
    }
    if ($sdkOk) {
        Write-Check 'toolchain / dotnet' 'PASS' ('{0} satisfies global.json {1} ({2})' -f $sdkText, $pinnedSdk, $rollForward)
    } else {
        $seen = $sdkText
        if (-not (Test-NonEmptyText $seen)) { $seen = 'no version' }
        Write-Check 'toolchain / dotnet' 'FAIL' ('{0} does not satisfy global.json {1} ({2})' -f $seen, $pinnedSdk, $rollForward) 'Install the SDK version named in global.json (rollForward: latestMinor).'
    }
}

$nodePath = Get-CommandPath @('node.exe', 'node')
if (-not $nodePath) {
    Write-Check 'toolchain / node' 'FAIL' 'node not on PATH' 'Install Node.js 20 or newer.'
} else {
    $nodeRun = Get-NativeOutput -File $nodePath -Arguments @('--version')
    $nodeText = $nodeRun.Text
    $nodeMajor = -1
    if ($nodeText -match '(\d+)') { $nodeMajor = [int]$Matches[1] }
    if ($nodeRun.Succeeded -and $nodeMajor -ge 20) {
        Write-Check 'toolchain / node' 'PASS' ('{0} (>= 20)' -f $nodeText)
    } else {
        Write-Check 'toolchain / node' 'FAIL' ('{0} (need >= 20)' -f $nodeText) 'Install Node.js 20 or newer.'
    }
}

$npmPath = Get-CommandPath @('npm.cmd', 'npm.exe', 'npm')
if (-not $npmPath) {
    Write-Check 'toolchain / npm' 'FAIL' 'npm not on PATH' 'Install Node.js 20+ (includes npm) and reopen the shell.'
} else {
    $npmRun = Get-NativeOutput -File $npmPath -Arguments @('--version')
    if ($npmRun.Succeeded -and (Test-NonEmptyText $npmRun.Text)) {
        Write-Check 'toolchain / npm' 'PASS' $npmRun.Text
    } else {
        Write-Check 'toolchain / npm' 'FAIL' 'npm did not report a version' 'Repair the Node.js install so npm --version works.'
    }
}

$pwshAlias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\pwsh.exe'
$ps51 = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (Test-Path -LiteralPath $pwshAlias) {
    Write-Check 'toolchain / pwsh' 'PASS' ('app-exec alias {0}' -f $pwshAlias)
} elseif (Test-Path -LiteralPath $ps51) {
    Write-Check 'toolchain / pwsh' 'WARN' 'only Windows PowerShell 5.1 found' 'Install PowerShell 7 and keep the version-independent %LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe alias. Do not pin a WindowsApps Microsoft.PowerShell_<version> path.'
} else {
    Write-Check 'toolchain / pwsh' 'WARN' 'pwsh app-exec alias not found' 'Install PowerShell 7 via winget and enable the app-exec alias %LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe.'
}

$dockerPath = Get-CommandPath @('docker.exe', 'docker')
$dockerUp = $false
if (-not $dockerPath) {
    Write-Check 'toolchain / docker' 'FAIL' 'docker not on PATH' 'Install Docker Desktop and confirm docker version shows a Server section.'
} else {
    $dockJob = Invoke-TimedJob -TimeoutSec 12 -ScriptBlock {
        docker version 2>&1 | Select-String 'Server:'
    }
    if ($dockJob.TimedOut) {
        Write-Check 'toolchain / docker' 'FAIL' 'docker version timed out (12s)' 'Docker Desktop / the engine is not answering. Start it (docker-desktop skill or restart.cmd).'
    } elseif ($dockJob.Output) {
        $dockerUp = $true
        Write-Check 'toolchain / docker' 'PASS' 'daemon answering'
    } else {
        Write-Check 'toolchain / docker' 'FAIL' 'docker daemon not answering' 'Start Docker Desktop and wait until docker version prints a Server section.'
    }
}

$claudePath = Get-CommandPath @('claude.exe', 'claude')
$grokPath = Get-CommandPath @('grok.exe', 'grok')
$tuiBits = @()
if ($claudePath) { $tuiBits += 'claude.exe' }
if ($grokPath) { $tuiBits += 'grok.exe' }
if ($tuiBits.Count -gt 0) {
    Write-Check 'toolchain / tui' 'PASS' ($tuiBits -join ', ')
} else {
    Write-Check 'toolchain / tui' 'WARN' 'neither claude.exe nor grok.exe on PATH' 'Install and log in at least one TUI wrapper; agent sessions authenticate through it.'
}

# ---------------------------------------------------------------------------
# 2. Windows convention directories
# ---------------------------------------------------------------------------

$conventionDirs = @(
    'C:\Antiphon\worktrees',
    'C:\logs\antiphon\session-runner',
    'C:\logs\antiphon\check-interpreter'
)
foreach ($dir in $conventionDirs) {
    if (Test-Path -LiteralPath $dir) {
        Write-Check 'dirs' 'PASS' $dir
    } else {
        Write-Check 'dirs' 'WARN' ('missing {0}' -f $dir) ('mkdir {0}' -f $dir)
    }
}

# ---------------------------------------------------------------------------
# 3. Docker postgres + volume (+ HNS only when Phase B is skipped)
# ---------------------------------------------------------------------------

$composeFile = Join-Path $repoRoot 'docker-compose.dev.yml'

if (-not $dockerUp) {
    Write-Check 'docker / postgres' 'FAIL' 'cannot inspect antiphon-postgres (daemon down)' 'Start Docker Desktop, then: docker compose -f docker-compose.dev.yml up -d'
    Write-Check 'docker / volume' 'FAIL' 'cannot inspect antiphon_pgdata (daemon down)' 'Start Docker Desktop, then: docker compose -f docker-compose.dev.yml up -d'
} else {
    $pgInspect = Get-NativeOutput -File $dockerPath -Arguments @(
        'inspect',
        '--format', '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}no-health{{end}}',
        'antiphon-postgres'
    )
    $pgText = $pgInspect.Text
    if ($pgInspect.Succeeded -and $pgText -match '^running\|healthy$') {
        Write-Check 'docker / postgres' 'PASS' 'antiphon-postgres running + healthy'
    } else {
        $detail = $pgText
        if (-not (Test-NonEmptyText $detail)) { $detail = 'not found' }
        Write-Check 'docker / postgres' 'FAIL' ('antiphon-postgres {0}' -f $detail) 'docker compose -f docker-compose.dev.yml up -d'
    }

    $volInspect = Get-NativeOutput -File $dockerPath -Arguments @('volume', 'inspect', 'antiphon_pgdata')
    if ($volInspect.Succeeded) {
        Write-Check 'docker / volume' 'PASS' 'antiphon_pgdata exists'
    } else {
        Write-Check 'docker / volume' 'FAIL' 'antiphon_pgdata volume not found' 'docker compose -f docker-compose.dev.yml up -d  (project name antiphon + volume pgdata)'
    }
}

if ($NoStack) {
    if (-not $dockerUp) {
        Write-Check 'docker / hns' 'WARN' 'skipped (docker daemon down)'
    } else {
        $testNet = 'bootstrap-hns-' + (Get-Random)
        $hnsJob = Invoke-TimedJob -TimeoutSec 8 -ArgumentList $testNet -ScriptBlock {
            param($n)
            docker network create $n 2>&1
        }
        if ((-not $hnsJob.TimedOut) -and $hnsJob.State -eq 'Completed') {
            & $dockerPath network rm $testNet 2>&1 | Out-Null
            Write-Check 'docker / hns' 'PASS' 'docker network create completed'
        } else {
            Write-Check 'docker / hns' 'WARN' 'docker network create hung or failed' 'Restart Docker Desktop (Windows HNS is broken when network create hangs).'
        }
    }
}

# ---------------------------------------------------------------------------
# 4. Database TCP (+ optional pg_isready)
# ---------------------------------------------------------------------------

$tcpOk = Test-TcpPort -HostName '127.0.0.1' -Port 17280
$readyDetail = ''
if ($dockerUp -and (Test-Path -LiteralPath $composeFile)) {
    $readyRun = Get-NativeOutput -File $dockerPath -Arguments @(
        'compose', '-f', $composeFile, 'exec', '-T', 'postgres', 'pg_isready', '-U', 'antiphon', '-d', 'antiphon'
    )
    if ($readyRun.Succeeded) {
        $readyDetail = '; pg_isready ok'
    } elseif (Test-NonEmptyText $readyRun.Text) {
        $readyDetail = ('; pg_isready: {0}' -f ($readyRun.Text -replace '\s+', ' '))
    } else {
        $readyDetail = '; pg_isready failed'
    }
}

if ($tcpOk) {
    Write-Check 'database' 'PASS' ('TCP localhost:17280 open{0}' -f $readyDetail)
} else {
    Write-Check 'database' 'FAIL' ('TCP localhost:17280 closed{0}' -f $readyDetail) 'docker compose -f docker-compose.dev.yml up -d  (postgres publishes 17280)'
}

# ---------------------------------------------------------------------------
# 5. Scheduled Tasks (WARN-only)
# ---------------------------------------------------------------------------

function Test-AntiphonTask {
    param([string]$TaskName)
    try {
        $t = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        return [bool]$t
    } catch {
        return $false
    }
}

$taskAppHost = Test-AntiphonTask 'Antiphon AppHost'
$taskRunner = Test-AntiphonTask 'Antiphon Session Runner'
if ($taskAppHost -and $taskRunner) {
    Write-Check 'scheduled tasks' 'PASS' 'Antiphon AppHost + Antiphon Session Runner registered'
} else {
    $missing = @()
    if (-not $taskAppHost) { $missing += 'Antiphon AppHost' }
    if (-not $taskRunner) { $missing += 'Antiphon Session Runner' }
    $remedial = 'pwsh -File scripts/install-autostart.ps1'
    if ($taskRunner -and -not $taskAppHost) {
        $remedial = 'pwsh -File scripts/install-autostart.ps1 -AppHostOnly   (use -AppHostOnly so a live session-runner is not killed by re-register)'
    } elseif ($taskAppHost -and -not $taskRunner) {
        $remedial = 'pwsh -File scripts/install-autostart.ps1   (full install; -AppHostOnly leaves the runner task alone)'
    } else {
        $remedial = 'pwsh -File scripts/install-autostart.ps1   (re-register kills a running session-runner; afterwards use -AppHostOnly to refresh only the AppHost task)'
    }
    Write-Check 'scheduled tasks' 'WARN' ('not registered: {0}' -f ($missing -join ', ')) $remedial
}

# ---------------------------------------------------------------------------
# 6. Secrets presence (never print a secret value)
# ---------------------------------------------------------------------------

$secretSource = $null

if ($dotnetPath) {
    $secRun = Get-NativeOutput -File $dotnetPath -Arguments @('user-secrets', 'list', '--id', 'antiphon-server')
    if (Test-LlmProviderKeyInUserSecretsText $secRun.Text) {
        $secretSource = 'dotnet user-secrets (id antiphon-server)'
    }
}

if (-not $secretSource) {
    $overlayNames = @(
        (Join-Path (Join-Path $repoRoot 'server') 'appsettings.Development.json'),
        (Join-Path (Join-Path $repoRoot 'server') 'appsettings.Production.json')
    )
    foreach ($overlay in $overlayNames) {
        if (Test-LlmProviderKeyInConfig (Read-JsonFile $overlay)) {
            $secretSource = ('gitignored {0}' -f (Split-Path $overlay -Leaf))
            break
        }
    }
}

if (-not $secretSource) {
    foreach ($envName in @('Llm__Providers__anthropic__ApiKey', 'Llm__Providers__openai__ApiKey')) {
        if (Test-NonEmptyText ([Environment]::GetEnvironmentVariable($envName))) {
            $secretSource = ('env {0}' -f $envName)
            break
        }
    }
}

if ($secretSource) {
    Write-Check 'secrets' 'PASS' ('Llm provider ApiKey present via {0}' -f $secretSource)
} else {
    Write-Check 'secrets' 'WARN' 'no Llm:Providers anthropic/openai ApiKey in user-secrets, overlay json, or env' 'Preferred: dotnet user-secrets set "Llm:Providers:anthropic:ApiKey" <value> --id antiphon-server. A TUI-only deployment can run without workflow LLM keys; Settings UI is the other path.'
}

$keyRing = Join-Path $env:LOCALAPPDATA 'Antiphon\DataProtection-Keys'
if (Test-Path -LiteralPath $keyRing) {
    Write-Check 'secrets / keyring' 'PASS' ('default KeyRingPath exists: {0}' -f $keyRing)
} else {
    Write-Check 'secrets / keyring' 'PASS' ('default KeyRingPath not present ({0}) - fresh ring is expected until managed TUI secrets are entered; DB not queried' -f $keyRing)
}

# ---------------------------------------------------------------------------
# 7. Claude-side
# ---------------------------------------------------------------------------

$skillsDir = Join-Path (Join-Path $repoRoot '.claude') 'skills'
if (Test-Path -LiteralPath $skillsDir) {
    Write-Check 'claude / repo skills' 'PASS' '.claude/skills/ present'
} else {
    Write-Check 'claude / repo skills' 'FAIL' '.claude/skills/ missing' 'Broken clone. Re-clone the repo so tracked .claude/skills ships with it.'
}

$homeClaude = Join-Path $env:USERPROFILE '.claude\CLAUDE.md'
if (Test-Path -LiteralPath $homeClaude) {
    Write-Check 'claude / user CLAUDE.md' 'PASS' $homeClaude
} else {
    Write-Check 'claude / user CLAUDE.md' 'WARN' ('{0} missing' -f $homeClaude) 'New operator may not have synced claude-home. Optional for product boot; needed for this operator''s global skills and policy.'
}

# ---------------------------------------------------------------------------
# 8. Client bundle staleness (same rule as AntiphonAppFixture.EnsureClientBundleIsCurrent)
# ---------------------------------------------------------------------------

$clientDir = Join-Path $repoRoot 'client'
$indexHtml = Join-Path (Join-Path $clientDir 'dist') 'index.html'
$sourceRoot = Join-Path $clientDir 'src'
if (-not (Test-Path -LiteralPath $indexHtml)) {
    Write-Check 'client bundle' 'WARN' 'client/dist/index.html missing' 'Run npm run build in client/. E2E serves this bundle and hard-fails when it is absent.'
} elseif (-not (Test-Path -LiteralPath $sourceRoot)) {
    Write-Check 'client bundle' 'PASS' 'client/dist/index.html present (no client/src to compare)'
} else {
    $builtAt = (Get-Item -LiteralPath $indexHtml).LastWriteTimeUtc
    $newer = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            -not $_.Name.EndsWith('.test.ts', [StringComparison]::OrdinalIgnoreCase) -and
            -not $_.Name.EndsWith('.test.tsx', [StringComparison]::OrdinalIgnoreCase) -and
            -not $_.Name.EndsWith('.stories.tsx', [StringComparison]::OrdinalIgnoreCase)
        } |
        Where-Object { $_.LastWriteTimeUtc -gt $builtAt } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($newer) {
        $rel = $newer.FullName
        if ($rel.StartsWith($clientDir, [StringComparison]::OrdinalIgnoreCase)) {
            $rel = $rel.Substring($clientDir.Length).TrimStart('\', '/')
        }
        Write-Check 'client bundle' 'WARN' ('stale: dist {0:u}, but {1} changed {2:u}' -f $builtAt, $rel, $newer.LastWriteTimeUtc) 'Run npm run build in client/.'
    } else {
        Write-Check 'client bundle' 'PASS' ('client/dist/index.html is current ({0:u})' -f $builtAt)
    }
}

# ---------------------------------------------------------------------------
# 9. Tracked-config sanity (WARN)
# ---------------------------------------------------------------------------

$appsettingsPath = Join-Path (Join-Path $repoRoot 'server') 'appsettings.json'
$appsettings = Read-JsonFile $appsettingsPath
$currentBranch = $null
$gitPath = Get-CommandPath @('git.exe', 'git')
if ($gitPath) {
    $br = Get-NativeOutput -File $gitPath -Arguments @('-C', $repoRoot, 'rev-parse', '--abbrev-ref', 'HEAD')
    if ($br.Succeeded) { $currentBranch = $br.Text }
}

if (-not $appsettings) {
    Write-Check 'config / DefaultBranch' 'WARN' 'server/appsettings.json missing or unreadable'
    Write-Check 'config / WorkspacePath' 'WARN' 'server/appsettings.json missing or unreadable'
} else {
    $cfgBranch = $null
    $cfgWorkspace = $null
    try { $cfgBranch = [string]$appsettings.Git.DefaultBranch } catch { $cfgBranch = $null }
    try { $cfgWorkspace = [string]$appsettings.Git.WorkspacePath } catch { $cfgWorkspace = $null }

    if (-not (Test-NonEmptyText $currentBranch)) {
        Write-Check 'config / DefaultBranch' 'WARN' 'could not read git rev-parse --abbrev-ref HEAD'
    } elseif ($cfgBranch -eq $currentBranch) {
        Write-Check 'config / DefaultBranch' 'PASS' ('Git:DefaultBranch={0} matches HEAD' -f $cfgBranch)
    } else {
        Write-Check 'config / DefaultBranch' 'WARN' ('Git:DefaultBranch={0} != HEAD {1}' -f $cfgBranch, $currentBranch) 'Tracked server/appsettings.json Git:DefaultBranch should match the repo default branch (master).'
    }

    if (-not (Test-NonEmptyText $cfgWorkspace)) {
        Write-Check 'config / WorkspacePath' 'PASS' 'Git:WorkspacePath is empty'
    } elseif (Test-Path -LiteralPath $cfgWorkspace) {
        Write-Check 'config / WorkspacePath' 'PASS' ('Git:WorkspacePath exists: {0}' -f $cfgWorkspace)
    } else {
        Write-Check 'config / WorkspacePath' 'WARN' ('Git:WorkspacePath does not exist: {0}' -f $cfgWorkspace) 'Set Git:WorkspacePath to "" or an existing path. A leftover D:\ path is the usual miss.'
    }
}

# ---------------------------------------------------------------------------
# Phase B - live stack (or skip)
# ---------------------------------------------------------------------------

Write-Host ''
if ($NoStack) {
    Write-Host 'Phase B skipped (-NoStack): stack health was not checked.'
} else {
    Write-Host 'Phase B: verify-dev-stack.ps1 -SkipBrowser'
    $verifyScript = Join-Path $repoRoot 'verify-dev-stack.ps1'
    $pwshExe = Get-PwshExePath
    if (-not (Test-Path -LiteralPath $verifyScript)) {
        Write-Check 'stack health' 'FAIL' 'verify-dev-stack.ps1 not found' $verifyScript
    } elseif (-not $pwshExe) {
        Write-Check 'stack health' 'FAIL' 'pwsh 7 not found; cannot run verify-dev-stack.ps1' 'Install PowerShell 7 and keep the %LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe alias.'
    } else {
        $stackLines = New-Object System.Collections.Generic.List[string]
        & $pwshExe -NoLogo -File $verifyScript -SkipBrowser 2>&1 | ForEach-Object {
            $s = "$_"
            [void]$stackLines.Add($s)
            Write-Host $s
        }
        $stackCode = $LASTEXITCODE
        if ($stackCode -eq 0) {
            Write-Check 'stack health' 'PASS' 'verify-dev-stack.ps1 -SkipBrowser exited 0'
        } else {
            $summary = @()
            $capture = $false
            foreach ($s in $stackLines) {
                if ($s -match 'Stack NOT healthy' -or $s -match 'check\(s\) failed') { $capture = $true }
                if ($capture) { $summary += $s }
            }
            if ($summary.Count -eq 0) {
                $tail = @($stackLines | Select-Object -Last 8)
                $summary = $tail
            }
            $remedial = ($summary -join "`n")
            if (-not (Test-NonEmptyText $remedial)) {
                $remedial = ('verify-dev-stack.ps1 exited {0}' -f $stackCode)
            }
            Write-Check 'stack health' 'FAIL' ('verify-dev-stack.ps1 -SkipBrowser exited {0}' -f $stackCode) $remedial
        }
    }
}

Write-Host ''
Write-Host ('Summary: {0} PASS, {1} WARN, {2} FAIL' -f $script:PassCount, $script:WarnCount, $script:FailCount)
Write-Host ('Exit code: {0}' -f $script:FailCount)
exit $script:FailCount
