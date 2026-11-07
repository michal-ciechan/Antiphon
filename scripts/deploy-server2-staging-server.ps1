#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1742 server2 host staging Antiphon server.

    Default is -WhatIf. It prints the host-daemon command and the acceptance
    URLs. -WhatIf does not call docker. -Execute starts the project only when
    this process is on the server2 host daemon (not inside the runner
    container, not a sibling daemon). The env file is untracked, mode 0600,
    and outside the repo. It holds ANTIPHON_STAGING_POSTGRES_IMAGE and
    POSTGRES_PASSWORD. This script never prints the password.
    The image must be postgres:16@sha256:<64 hex> or postgres@sha256:<64 hex>.
    The compose runs only as project antiphon-server2-staging.
    Acceptance is http://127.0.0.1:17420/health and
    http://127.0.0.1:17420/api/version.
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
    [string]$RepoRoot = ''
)

$script:StagingProject = 'antiphon-server2-staging'
$script:StagingExit = 1
$script:HealthUrl = 'http://127.0.0.1:17420/health'
$script:VersionUrl = 'http://127.0.0.1:17420/api/version'

function Get-StagingRepoRoot {
    if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { return $RepoRoot }
    return (Split-Path -Parent $PSScriptRoot)
}

function Write-StagingResult {
    param([string]$Kind, [int]$Code, [string]$Reason)
    Write-Output ('SERVER2-STAGING ' + $Kind + ' exit=' + $Code + ' reason=' + $Reason)
    $script:StagingExit = $Code
}

function Get-StagingDevPassword {
    param([string]$Root)
    $path = Join-Path $Root 'docker-compose.dev.yml'
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $match = [regex]::Match([System.IO.File]::ReadAllText($path), 'POSTGRES_PASSWORD:\s*(\S+)')
    if (-not $match.Success) { return '' }
    return $match.Groups[1].Value
}

function Test-StagingMode0600 {
    param([string]$Path)
    if ($env:OS -eq 'Windows_NT') { return $false }
    $mode = [System.IO.File]::GetUnixFileMode($Path)
    $owner = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
    return ($mode -eq $owner)
}

function Test-StagingInsideRepo {
    param([string]$Path, [string]$Root)
    $full = [System.IO.Path]::GetFullPath($Path)
    $base = [System.IO.Path]::GetFullPath($Root).TrimEnd([char]47, [char]92)
    $sep = [System.IO.Path]::DirectorySeparatorChar
    return $full.StartsWith($base + $sep, [StringComparison]::Ordinal)
}

function Read-StagingEnv {
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

function Test-StagingImage {
    param([string]$Image)
    if ([string]::IsNullOrWhiteSpace($Image)) { return 'empty-image' }
    if ($Image -match '(?i)alpine') { return 'image-alpine' }
    if ($Image -match '^postgres:16@sha256:[0-9a-f]{64}$') { return '' }
    if ($Image -match '^postgres@sha256:[0-9a-f]{64}$') { return '' }
    return 'image-floating'
}

function Get-StagingComposeRefusal {
    param([string]$Text)
    if ($Text -match 'antiphon_pgdata') { return 'volume-forbidden' }
    if ($Text -match 'dind-data') { return 'volume-forbidden' }
    if ($Text -match 'antiphon-server2-postgres') { return 'volume-forbidden' }
    if ($Text -match '(?i)windmill') { return 'volume-forbidden' }
    if ($Text -match '0\.0\.0\.0') { return 'bind-any' }
    if ($Text -match '5433') { return 'port-forbidden' }
    if ($Text -match '17280') { return 'port-forbidden' }
    if ($Text -match '17202') { return 'port-forbidden' }
    if ($Text -match ':5432:') { return 'port-forbidden' }
    if ($Text -match 'PhoneHome__ServerOrigin') { return 'phone-home-origin' }
    if ($Text -match 'codeperf\.net') { return 'phone-home-origin' }
    foreach ($need in @(
            'name: antiphon-server2-staging',
            'name: antiphon-server2-staging_pgdata',
            'name: antiphon-server2-staging_server-state',
            'name: antiphon-server2-staging_work',
            '127.0.0.1:17420:8080',
            'Hangfire__ServerEnabled: "true"',
            'ZombieCensus__Enabled: "false"',
            'Schedules__Enabled: "true"',
            'Delegation__CheckInterpreterEnabled: "true"',
            'Delegation__DiagnoseEnabled: "true"',
            'Delegation__OutputDistillerEnabled: "true"',
            'Delegation__CheckInterpreterWorkingDirectory: /state/check-interpreter',
            'Delegation__DiagnoseWorkingDirectory: /state/diagnose',
            'Delegation__OutputDistillerWorkingDirectory: /state/output-distiller',
            'WorktreeResidue__Enabled: "true"',
            'WorktreeResidue__Execute: "false"',
            'WorktreeLegacyRelease__Enabled: "false"',
            'WorktreeLegacyRelease__Execute: "false"',
            'RunnerWorktreeRetention__Enabled: "false"',
            'RunnerWorktreeRetention__Execute: "false"',
            'ChannelBridge__Enabled: "false"',
            'Digest__Enabled: "false"',
            'SessionRunner__Enabled: "false"',
            'PhoneHomeRunner__Enabled: "false"',
            'AgentTui__KeyRingPath: /state/keyring',
            'Git__WorktreeBasePath: /work/worktrees',
            'Git__WorkspacePath: /work/repos',
            'Host=staging-postgres',
            'ANTIPHON_STAGING_POSTGRES_IMAGE:?',
            'POSTGRES_PASSWORD:?',
            'shared_preload_libraries=pg_stat_statements',
            'http://127.0.0.1:8080/health'
        )) {
        if (-not $Text.Contains($need)) { return 'compose-invalid' }
    }
    return ''
}

function Get-StagingHostCommand {
    param([string]$EnvPath, [string]$ComposePath)
    $envArg = '<env-file>'
    if (-not [string]::IsNullOrWhiteSpace($EnvPath)) { $envArg = $EnvPath }
    $lines = @(
        ('docker compose --env-file ' + $envArg + ' -f ' + $ComposePath + ' -p ' + $script:StagingProject + ' up -d'),
        ('acceptance: curl -fsS ' + $script:HealthUrl),
        ('acceptance: curl -fsS ' + $script:VersionUrl)
    )
    return ($lines -join "`n")
}

function Resolve-StagingLane {
    param([string]$DaemonName, [string]$Hostname, [bool]$InContainer)
    if ([string]::IsNullOrWhiteSpace($DaemonName)) { return 'none' }
    if ($DaemonName -ne $Hostname) { return 'sibling' }
    if ($InContainer) { return 'nested' }
    return 'host'
}

function Get-StagingObservedLane {
    # The runner container is not the host daemon. Refuse before any compose up.
    if (Test-Path -LiteralPath '/.dockerenv') { return 'nested' }
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $docker) { return 'none' }
    $daemon = (& $docker.Source info --format '{{.Name}}' 2>$null | Out-String).Trim()
    $own = (& hostname 2>$null | Out-String).Trim()
    return (Resolve-StagingLane $daemon $own (Test-Path -LiteralPath '/.dockerenv'))
}

function Invoke-StagingUp {
    param([string]$EnvPath, [string]$ComposePath)
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $docker) { return 'missing-docker' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $docker.Source
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('compose', '--env-file', $EnvPath, '-f', $ComposePath, '-p', $script:StagingProject, 'up', '-d')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) { return 'compose-up-failed' }
    return ''
}

function Invoke-StagingCurl {
    param([string]$Url)
    $curl = Get-Command curl -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $curl) { return 'acceptance-curl-missing' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $curl.Source
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('-fsS', '--max-time', '5', $Url)) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    [void]$proc.Start()
    [void]$proc.StandardOutput.ReadToEnd()
    [void]$proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) { return 'fail' }
    return ''
}

function Invoke-StagingAcceptance {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $health = 'fail'
    while ([DateTime]::UtcNow -lt $deadline) {
        $health = Invoke-StagingCurl $script:HealthUrl
        if ($health -eq '') { break }
        if ($health -eq 'acceptance-curl-missing') { return $health }
        Start-Sleep -Seconds 2
    }
    if ($health -ne '') { return 'acceptance-health' }
    $version = Invoke-StagingCurl $script:VersionUrl
    if ($version -eq 'acceptance-curl-missing') { return $version }
    if ($version -ne '') { return 'acceptance-version' }
    return ''
}

function Invoke-StagingDeploy {
    if ($WhatIf -and $Execute) {
        Write-StagingResult 'fail' 3 'ambiguous-mode'
        return
    }
    $planOnly = -not $Execute
    if ($WhatIf) { $planOnly = $true }
    $selected = $script:StagingProject
    if (-not [string]::IsNullOrWhiteSpace($Project)) { $selected = $Project.Trim() }
    if ($selected -cne $script:StagingProject) {
        Write-StagingResult 'fail' 2 'staging-compose-wrong-project'
        return
    }
    $root = Get-StagingRepoRoot
    $composePath = $ComposeFile
    if ([string]::IsNullOrWhiteSpace($composePath)) {
        $composePath = Join-Path $root 'docker-compose.server2-staging-server.yml'
    }
    if (-not (Test-Path -LiteralPath $composePath)) {
        Write-StagingResult 'fail' 2 'missing-compose'
        return
    }
    $composeText = [System.IO.File]::ReadAllText($composePath)
    if ($composeText -notmatch '(?m)^name: antiphon-server2-staging$') {
        Write-StagingResult 'fail' 2 'staging-compose-wrong-project'
        return
    }
    $composeReason = Get-StagingComposeRefusal $composeText
    if ($composeReason) {
        Write-StagingResult 'fail' 2 $composeReason
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($EnvFile)) {
        if (-not (Test-Path -LiteralPath $EnvFile)) {
            Write-StagingResult 'fail' 2 'env-missing'
            return
        }
        if (Test-StagingInsideRepo $EnvFile $root) {
            Write-StagingResult 'fail' 2 'env-inside-repo'
            return
        }
        if (-not (Test-StagingMode0600 $EnvFile)) {
            Write-StagingResult 'fail' 2 'env-mode'
            return
        }
        $envMap = Read-StagingEnv $EnvFile
        $imageReason = Test-StagingImage ([string]$envMap['ANTIPHON_STAGING_POSTGRES_IMAGE'])
        if ($imageReason) {
            Write-StagingResult 'fail' 2 $imageReason
            return
        }
        $password = [string]$envMap['POSTGRES_PASSWORD']
        if ([string]::IsNullOrWhiteSpace($password)) {
            Write-StagingResult 'fail' 2 'empty-password'
            return
        }
        $dev = Get-StagingDevPassword $root
        if (-not [string]::IsNullOrWhiteSpace($dev) -and $password -ceq $dev) {
            Write-StagingResult 'fail' 2 'dev-password'
            return
        }
    } elseif (-not $planOnly) {
        Write-StagingResult 'fail' 2 'env-missing'
        return
    }

    if ($planOnly) {
        Write-Output (Get-StagingHostCommand $EnvFile $composePath)
        Write-StagingResult 'plan' 0 'ok'
        return
    }

    $lane = Get-StagingObservedLane
    if ($lane -ne 'host') {
        Write-StagingResult 'fail' 2 'not-server2-host'
        return
    }
    $up = Invoke-StagingUp $EnvFile $composePath
    if ($up) {
        Write-StagingResult 'fail' 2 $up
        return
    }
    $accepted = Invoke-StagingAcceptance
    if ($accepted) {
        Write-StagingResult 'fail' 2 $accepted
        return
    }
    Write-StagingResult 'ok' 0 'ok'
}

if ($MyInvocation.InvocationName -eq '.') { return }
Invoke-StagingDeploy
exit $script:StagingExit
