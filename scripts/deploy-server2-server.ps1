#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1775 server2 host production Antiphon server.

    Default is -WhatIf. It prints the host-daemon command and the verify-only
    gate. -WhatIf does not call docker. -Execute starts the project only when
    this process is on the server2 host daemon (not inside the runner
    container, not a sibling daemon), then runs scripts/restart-linux-server.ps1
    verify-only. Before up, the env SOURCE_REVISION and git rev-parse HEAD
    of the compose build context must both equal -ExpectedServerSha.
    The env file is untracked, mode 0600, and outside the repo.
    This script never prints the password or the phone-home secret.
    The compose runs only as project antiphon-server2-server.
    Exit codes: 0 plan or ok, 2 refusal, 3 ambiguous-mode.
    ASCII-only.
#>
[CmdletBinding()]
param(
    [switch]$WhatIf,
    [switch]$Execute,
    [string]$Project = '',
    [string]$EnvFile = '',
    [string]$ComposeFile = '',
    [string]$RepoRoot = '',
    [string]$ExpectedServerSha = ''
)

$script:ServerProject = 'antiphon-server2-server'
$script:ServerContainer = 'antiphon-server2-server-server-1'
$script:GateBaseUrl = 'http://127.0.0.1:17400'
$script:GateConfig = '/var/tmp/antiphon-server2-server.gate.config'
$script:GateLock = '/var/tmp/antiphon-server2-server.restart.lock'
$script:ServerExit = 1

function Get-ServerRepoRoot {
    if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { return $RepoRoot }
    return (Split-Path -Parent $PSScriptRoot)
}

function Write-ServerResult {
    param([string]$Kind, [int]$Code, [string]$Reason)
    Write-Output ('SERVER2-SERVER ' + $Kind + ' exit=' + $Code + ' reason=' + $Reason)
    $script:ServerExit = $Code
}

function Get-ServerDevPassword {
    param([string]$Root)
    $path = Join-Path $Root 'docker-compose.dev.yml'
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $match = [regex]::Match([System.IO.File]::ReadAllText($path), 'POSTGRES_PASSWORD:\s*(\S+)')
    if (-not $match.Success) { return '' }
    return $match.Groups[1].Value
}

function Test-ServerMode0600 {
    param([string]$Path)
    if ($env:OS -eq 'Windows_NT') { return $false }
    $mode = [System.IO.File]::GetUnixFileMode($Path)
    $owner = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
    return ($mode -eq $owner)
}

function Test-ServerInsideRepo {
    param([string]$Path, [string]$Root)
    $full = [System.IO.Path]::GetFullPath($Path)
    $base = [System.IO.Path]::GetFullPath($Root).TrimEnd([char]47, [char]92)
    $sep = [System.IO.Path]::DirectorySeparatorChar
    return $full.StartsWith($base + $sep, [StringComparison]::Ordinal)
}

function Read-ServerEnv {
    param([string]$Path)
    $map = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0 -or $trim.StartsWith('#')) { continue }
        $eq = $trim.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $trim.Substring(0, $eq).Trim()
        $value = $trim.Substring($eq + 1)
        $map[$key] = $value
    }
    return $map
}

function Test-ServerSha {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return [bool]($Value -match '^[0-9a-fA-F]{40}$' -or $Value -match '^[0-9a-fA-F]{64}$')
}

function Get-ServerComposeRefusal {
    param([string]$Text)
    if ($Text.Contains('antiphon_pgdata')) { return 'volume-forbidden' }
    if ($Text.Contains('dind-data')) { return 'volume-forbidden' }
    if ($Text.Contains('windmill')) { return 'volume-forbidden' }
    if ($Text.Contains('0.0.0.0')) { return 'bind-any' }
    if ($Text.Contains('17420')) { return 'port-forbidden' }
    if ($Text.Contains('17280')) { return 'port-forbidden' }
    if ($Text.Contains('17202')) { return 'port-forbidden' }
    if ($Text.Contains('5433')) { return 'port-forbidden' }
    if ($Text.Contains(':5432:')) { return 'port-forbidden' }
    $bannedOrigin = 'codeperf.' + 'net'
    if ($Text.Contains($bannedOrigin)) { return 'phone-home-origin' }
    if ($Text.Contains('privileged:')) { return 'privileged' }
    if ($Text.Contains('docker.sock')) { return 'socket' }
    foreach ($need in @(
            'name: antiphon-server2-server',
            'external: true',
            'name: antiphon-server2',
            'name: antiphon-server2-server_work',
            'name: antiphon-server2-server_server-state',
            'name: antiphon-server2-server_runner-state',
            '127.0.0.1:17400:8080',
            'Host=postgres;Port=5432',
            '  server-session-runner:',
            'SessionRunner__Herdr__Enabled: "false"',
            'PhoneHome__Enabled: "false"',
            'SessionRunner__Enabled: "true"',
            'SessionRunner__BaseUrl: http://server-session-runner:8080',
            'Hangfire__ServerEnabled: "true"',
            'Hangfire__Storage: "${HANGFIRE_STORAGE:-InMemory}"',
            'ZombieCensus__Enabled: "false"',
            'WorktreeResidue__Enabled: "true"',
            'WorktreeResidue__Execute: "false"',
            'WorktreeLegacyRelease__Enabled: "false"',
            'WorktreeLegacyRelease__Execute: "false"',
            'RunnerWorktreeRetention__Enabled: "true"',
            'RunnerWorktreeRetention__Execute: "true"',
            'Schedules__Enabled: "true"',
            'ChannelBridge__Enabled: "${CHANNEL_BRIDGE_ENABLED:-false}"',
            'Digest__Enabled: "false"',
            'PhoneHomeRunner__Enabled: "true"',
            'Delegation__CheckInterpreterEnabled: "true"',
            'Delegation__DiagnoseEnabled: "true"',
            'Delegation__OutputDistillerEnabled: "true"',
            'Delegation__CheckInterpreterWorkingDirectory: /work/specialists/check-interpreter',
            'Delegation__DiagnoseWorkingDirectory: /work/specialists/diagnose',
            'Delegation__OutputDistillerWorkingDirectory: /work/specialists/output-distiller',
            'SessionRunner__VerifyExecutablesOnServer: "false"',
            'Delegation__OrchestratorInstructions__Path: /work/.antiphon/orchestrator-instructions.md',
            'CLAUDE_CONFIG_DIR: /state/claude',
            'AgentTui__KeyProtection__Mode: X509Certificate',
            'Operator__TokenPath: /state/operator-token',
            'runner-state:/state',
            'runner-state:/runner-state:ro',
            'user: "1654:1654"',
            'KEYRING_CERT_FILE:?KEYRING_CERT_FILE is required',
            'chmod 0600',
            'ForeignStateOwner',
            'InMemory'
        )) {
        if (-not $Text.Contains($need)) { return 'compose-invalid' }
    }
    return ''
}

function Get-ServerBuildContext {
    param([string]$ComposePath)
    # The compose builds with context '.', the directory of the compose file.
    return (Split-Path -Parent ([System.IO.Path]::GetFullPath($ComposePath)))
}

function Get-ServerCheckoutHead {
    param([string]$Context)
    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $git) { return '' }
    $head = (& $git.Source -C $Context rev-parse --verify HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { return '' }
    if (-not (Test-ServerSha $head)) { return '' }
    return $head.ToLowerInvariant()
}

function Get-ServerHostCommand {
    param([string]$EnvPath, [string]$ComposePath, [string]$Sha)
    $envArg = '<env-file>'
    if (-not [string]::IsNullOrWhiteSpace($EnvPath)) { $envArg = $EnvPath }
    $shaArg = '<expected-sha>'
    if (-not [string]::IsNullOrWhiteSpace($Sha)) { $shaArg = $Sha }
    $context = Get-ServerBuildContext $ComposePath
    $revision = 'before up: SOURCE_REVISION in the env file and git rev-parse HEAD of ' + $context + ' must equal ' + $shaArg
    $composeUp = 'docker compose --env-file ' + $envArg + ' -f ' + $ComposePath + ' -p ' + $script:ServerProject + ' up -d --build'
    $gate = 'verify-only: pwsh -NoProfile -File scripts/restart-linux-server.ps1 -Config ' + $script:GateConfig + ' -ExpectedServerSha ' + $shaArg
    return ($revision + "`n" + $composeUp + "`n" + $gate)
}

function Resolve-ServerLane {
    param([string]$DaemonName, [string]$Hostname, [bool]$InContainer)
    if ([string]::IsNullOrWhiteSpace($DaemonName)) { return 'none' }
    if ($DaemonName -ne $Hostname) { return 'sibling' }
    if ($InContainer) { return 'nested' }
    return 'host'
}

function Get-ServerObservedLane {
    # The runner container is not the host daemon. Refuse before any compose start.
    if (Test-Path -LiteralPath '/.dockerenv') { return 'nested' }
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $docker) { return 'none' }
    $daemon = (& $docker.Source info --format '{{.Name}}' 2>$null | Out-String).Trim()
    $own = (& hostname 2>$null | Out-String).Trim()
    return (Resolve-ServerLane $daemon $own (Test-Path -LiteralPath '/.dockerenv'))
}

function Invoke-ServerUp {
    param([string]$EnvPath, [string]$ComposePath)
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $docker) { return 'missing-docker' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $docker.Source
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('compose', '--env-file', $EnvPath, '-f', $ComposePath, '-p', $script:ServerProject, 'up', '-d', '--build')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    [void]$proc.Start()
    # Start both reads before WaitForExit. A first up builds three images
    # and fills stderr; an unread pipe deadlocks the child. Do not write
    # the text: compose output can repeat an interpolated secret.
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $proc.WaitForExit()
    [void]$stdoutTask.GetAwaiter().GetResult()
    [void]$stderrTask.GetAwaiter().GetResult()
    if ($proc.ExitCode -ne 0) { return 'compose-up-failed' }
    return ''
}

function Invoke-ServerGate {
    param([string]$Root, [string]$Sha)
    $gate = Join-Path $Root 'scripts/restart-linux-server.ps1'
    if (-not (Test-Path -LiteralPath $gate)) { return 'gate-missing' }
    $cfg = $script:GateConfig
    $body = 'baseUrl=' + $script:GateBaseUrl + "`n" + 'container=' + $script:ServerContainer + "`n" + 'lockPath=' + $script:GateLock + "`n"
    [System.IO.File]::WriteAllText($cfg, $body)
    if ($env:OS -ne 'Windows_NT') {
        $ownerMode = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
        [System.IO.File]::SetUnixFileMode($cfg, $ownerMode)
    }
    $pwsh = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $pwsh) { return 'gate-missing' }
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ($true) {
        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $pwsh.Source
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        # verify-only: omit the gate execute switch. Retry health-down only.
        foreach ($arg in @('-NoProfile', '-File', $gate, '-Config', $cfg, '-ExpectedServerSha', $Sha)) {
            [void]$psi.ArgumentList.Add($arg)
        }
        $proc = [System.Diagnostics.Process]::new()
        $proc.StartInfo = $psi
        [void]$proc.Start()
        [void]$proc.StandardOutput.ReadToEnd()
        [void]$proc.StandardError.ReadToEnd()
        $proc.WaitForExit()
        if ($proc.ExitCode -eq 0) { return '' }
        if ($proc.ExitCode -ne 1) { return 'gate-unverified' }
        if ([DateTime]::UtcNow -ge $deadline) { return 'gate-unverified' }
        Start-Sleep -Seconds 2
    }
}

function Invoke-ServerDeploy {
    if ($WhatIf -and $Execute) {
        Write-ServerResult 'fail' 3 'ambiguous-mode'
        return
    }
    $planOnly = -not $Execute
    if ($WhatIf) { $planOnly = $true }
    $selected = $script:ServerProject
    if (-not [string]::IsNullOrWhiteSpace($Project)) { $selected = $Project.Trim() }
    if ($selected -cne $script:ServerProject) {
        Write-ServerResult 'fail' 2 'production-compose-wrong-project'
        return
    }
    $root = Get-ServerRepoRoot
    $composePath = $ComposeFile
    if ([string]::IsNullOrWhiteSpace($composePath)) {
        $composePath = Join-Path $root 'docker-compose.server2-server.yml'
    }
    if (-not (Test-Path -LiteralPath $composePath)) {
        Write-ServerResult 'fail' 2 'missing-compose'
        return
    }
    $composeText = [System.IO.File]::ReadAllText($composePath)
    if ($composeText -notmatch '(?m)^name: antiphon-server2-server$') {
        Write-ServerResult 'fail' 2 'production-compose-wrong-project'
        return
    }
    $composeReason = Get-ServerComposeRefusal $composeText
    if ($composeReason) {
        Write-ServerResult 'fail' 2 $composeReason
        return
    }

    $envMap = $null
    if (-not [string]::IsNullOrWhiteSpace($EnvFile)) {
        if (-not (Test-Path -LiteralPath $EnvFile)) {
            Write-ServerResult 'fail' 2 'env-missing'
            return
        }
        if (Test-ServerInsideRepo $EnvFile $root) {
            Write-ServerResult 'fail' 2 'env-inside-repo'
            return
        }
        if (-not (Test-ServerMode0600 $EnvFile)) {
            Write-ServerResult 'fail' 2 'env-mode'
            return
        }
        $envMap = Read-ServerEnv $EnvFile
        $password = [string]$envMap['POSTGRES_PASSWORD']
        if ([string]::IsNullOrWhiteSpace($password)) {
            Write-ServerResult 'fail' 2 'empty-password'
            return
        }
        $dev = Get-ServerDevPassword $root
        if (-not [string]::IsNullOrWhiteSpace($dev) -and $password -ceq $dev) {
            Write-ServerResult 'fail' 2 'dev-password'
            return
        }
        $storage = [string]$envMap['HANGFIRE_STORAGE']
        if (-not [string]::IsNullOrWhiteSpace($storage) -and $storage -cne 'InMemory' -and $storage -cne 'PostgreSql') {
            Write-ServerResult 'fail' 2 'hangfire-storage'
            return
        }
    } elseif (-not $planOnly) {
        Write-ServerResult 'fail' 2 'env-missing'
        return
    }

    if ($planOnly) {
        Write-Output (Get-ServerHostCommand $EnvFile $composePath $ExpectedServerSha)
        Write-ServerResult 'plan' 0 'ok'
        return
    }

    foreach ($key in @('ANTIPHON_PUBLIC_ORIGIN', 'PHONE_HOME_SHARED_SECRET', 'PHONE_HOME_HOST_WORKSPACE_ROOT', 'KEYRING_CERT_FILE', 'KEYRING_KEY_FILE', 'OPERATOR_TOKEN_FILE')) {
        if ([string]::IsNullOrWhiteSpace([string]$envMap[$key])) {
            Write-ServerResult 'fail' 2 'env-missing'
            return
        }
    }
    foreach ($key in @('KEYRING_CERT_FILE', 'KEYRING_KEY_FILE', 'OPERATOR_TOKEN_FILE')) {
        $path = [string]$envMap[$key]
        if (-not (Test-Path -LiteralPath $path)) {
            Write-ServerResult 'fail' 2 'missing-certificate'
            return
        }
    }
    if (-not (Test-ServerSha $ExpectedServerSha)) {
        Write-ServerResult 'fail' 2 'expected-sha'
        return
    }
    $revision = ([string]$envMap['SOURCE_REVISION']).Trim().ToLowerInvariant()
    if ($revision -cne $ExpectedServerSha.ToLowerInvariant()) {
        Write-ServerResult 'fail' 2 'revision-mismatch'
        return
    }
    # The env names the revision; the build context is what up --build
    # compiles. Both must be the expected SHA before any docker call.
    $head = Get-ServerCheckoutHead (Get-ServerBuildContext $composePath)
    if (-not $head) {
        Write-ServerResult 'fail' 2 'head-unknown'
        return
    }
    if ($head -cne $ExpectedServerSha.ToLowerInvariant()) {
        Write-ServerResult 'fail' 2 'head-mismatch'
        return
    }
    $lane = Get-ServerObservedLane
    if ($lane -ne 'host') {
        Write-ServerResult 'fail' 2 'not-server2-host'
        return
    }
    $up = Invoke-ServerUp $EnvFile $composePath
    if ($up) {
        Write-ServerResult 'fail' 2 $up
        return
    }
    $gated = Invoke-ServerGate $root $ExpectedServerSha
    if ($gated) {
        Write-ServerResult 'fail' 2 $gated
        return
    }
    Write-ServerResult 'ok' 0 'ok'
}

if ($MyInvocation.InvocationName -eq '.') { return }
Invoke-ServerDeploy
exit $script:ServerExit
