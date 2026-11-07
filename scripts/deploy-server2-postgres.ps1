#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1406 server2 host Postgres service and scratch restore drill.

    Default is -WhatIf. It prints the host-daemon command and, with
    -RestoreDrill, the scratch restore plan. -WhatIf does not call docker.
    -Execute starts the service only when this process is on the server2 host
    daemon (not inside the runner container, not a sibling daemon). The env
    file is untracked, mode 0600, and outside the repo. It holds
    ANTIPHON_POSTGRES_BIND_ADDRESS, ANTIPHON_POSTGRES_IMAGE, and
    POSTGRES_PASSWORD. This script never prints the password.
    The drill plan names readings that are still unread. It does not invent
    them, and it never removes antiphon_pgdata or the production volume.
    ASCII-only.
#>
[CmdletBinding()]
param(
    [switch]$WhatIf,
    [switch]$Execute,
    [switch]$RestoreDrill,
    [string]$EnvFile = '',
    [string]$ComposeFile = '',
    [string]$RepoRoot = ''
)

$script:Server2PostgresProject = 'antiphon-server2-postgres'
$script:Server2PostgresDrillProject = 'antiphon-server2-postgres-drill'
$script:Server2PostgresDrillVolume = 'antiphon-server2-postgres-drill_pgdata'
$script:Server2PostgresExit = 1

function Get-Server2PostgresRepoRoot {
    if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) { return $RepoRoot }
    return (Split-Path -Parent $PSScriptRoot)
}

function Write-Server2PostgresResult {
    param([string]$Kind, [int]$Code, [string]$Reason)
    Write-Output ('SERVER2-POSTGRES ' + $Kind + ' exit=' + $Code + ' reason=' + $Reason)
    $script:Server2PostgresExit = $Code
}

function Test-Server2PostgresBind {
    param([bool]$Provided, [string]$Address)
    if (-not $Provided) { return 'bind-omitted' }
    if ([string]::IsNullOrWhiteSpace($Address)) { return 'empty-bind' }
    $trimmed = $Address.Trim()
    if ($trimmed -eq '0.0.0.0' -or $trimmed -eq '::' -or $trimmed -eq '*') { return 'bind-any' }
    if ($trimmed -notmatch '^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$') { return 'bind-not-ipv4' }
    foreach ($octet in $trimmed.Split('.')) {
        $n = 0
        if (-not [int]::TryParse($octet, [ref]$n) -or $n -gt 255) { return 'bind-not-ipv4' }
    }
    return ''
}

function Test-Server2PostgresImage {
    param([string]$Image)
    if ([string]::IsNullOrWhiteSpace($Image)) { return 'empty-image' }
    if ($Image -match '(?i)alpine') { return 'image-alpine' }
    if ($Image -notmatch '^postgres:16@sha256:[0-9a-f]{64}$') { return 'image-floating' }
    return ''
}

function Test-Server2PostgresDrillVolume {
    param([string]$Name)
    foreach ($forbidden in @('antiphon_pgdata', 'antiphon-server2-postgres_pgdata', 'pgdata', 'dind-data')) {
        if ($Name -eq $forbidden) { return 'volume-forbidden' }
    }
    if ($Name -match '(?i)windmill') { return 'volume-forbidden' }
    if ($Name -cne $script:Server2PostgresDrillVolume) { return 'volume-forbidden' }
    return ''
}

function Test-Server2PostgresTailnet {
    param([string]$Bind, [string]$TailscaleText)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($TailscaleText -split "`r?`n")) {
        $trim = $line.Trim()
        if ($trim.Length -gt 0) { [void]$lines.Add($trim) }
    }
    if ($lines.Count -ne 1) { return 'bind-not-tailnet' }
    if ($lines[0] -cne $Bind) { return 'bind-not-tailnet' }
    return ''
}

function Get-Server2PostgresDevPassword {
    param([string]$Root)
    $path = Join-Path $Root 'docker-compose.dev.yml'
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $match = [regex]::Match([System.IO.File]::ReadAllText($path), 'POSTGRES_PASSWORD:\s*(\S+)')
    if (-not $match.Success) { return '' }
    return $match.Groups[1].Value
}

function Test-Server2PostgresMode0600 {
    param([string]$Path)
    if ($env:OS -eq 'Windows_NT') { return $false }
    $mode = [System.IO.File]::GetUnixFileMode($Path)
    $owner = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
    return ($mode -eq $owner)
}

function Test-Server2PostgresInsideRepo {
    param([string]$Path, [string]$Root)
    $full = [System.IO.Path]::GetFullPath($Path)
    $base = [System.IO.Path]::GetFullPath($Root).TrimEnd([char]47, [char]92)
    $sep = [System.IO.Path]::DirectorySeparatorChar
    return $full.StartsWith($base + $sep, [StringComparison]::Ordinal)
}

function Read-Server2PostgresEnv {
    param([string]$Path)
    $map = @{}
    $present = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0 -or $trim.StartsWith('#')) { continue }
        $eq = $trim.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $trim.Substring(0, $eq).Trim()
        $value = $trim.Substring($eq + 1)
        $present[$key] = $true
        $map[$key] = $value
    }
    return @{ Map = $map; Present = $present }
}

function Get-Server2PostgresComposeRefusal {
    param([string]$Text)
    if ($Text -match 'antiphon_pgdata') { return 'volume-forbidden' }
    if ($Text -match '(?i)alpine') { return 'image-alpine' }
    if ($Text -match '0\.0\.0\.0') { return 'bind-any' }
    if ($Text -match '5433') { return 'port-5433' }
    foreach ($need in @(
            'name: antiphon-server2-postgres',
            'ANTIPHON_POSTGRES_BIND_ADDRESS:?',
            'ANTIPHON_POSTGRES_IMAGE:?',
            'POSTGRES_PASSWORD:?',
            ':5432:5432',
            'mem_limit: 4g',
            'cpus: 4.0',
            'restart: unless-stopped',
            'pg_isready -U antiphon -d antiphon',
            'shared_preload_libraries=pg_stat_statements',
            'pg_stat_statements.track=top',
            'pg_stat_statements.max=5000',
            'antiphon-server2',
            'pgdata:/var/lib/postgresql/data',
            'name: antiphon-server2-postgres_pgdata'
        )) {
        if (-not $Text.Contains($need)) { return 'compose-invalid' }
    }
    return ''
}

function Expand-Server2PostgresCompose {
    param([string]$Text, [string]$Bind, [string]$Image, [string]$Password)
    $bindReason = Test-Server2PostgresBind -Provided $true -Address $Bind
    if ($bindReason) { return @{ Ok = $false; Reason = $bindReason; Redacted = '' } }
    $imageReason = Test-Server2PostgresImage $Image
    if ($imageReason) { return @{ Ok = $false; Reason = $imageReason; Redacted = '' } }
    if ([string]::IsNullOrWhiteSpace($Password)) { return @{ Ok = $false; Reason = 'empty-password'; Redacted = '' } }
    $redacted = $Text
    $redacted = [regex]::Replace($redacted, '\$\{ANTIPHON_POSTGRES_BIND_ADDRESS:\?[^}]*\}', $Bind)
    $redacted = [regex]::Replace($redacted, '\$\{ANTIPHON_POSTGRES_IMAGE:\?[^}]*\}', $Image)
    $redacted = [regex]::Replace($redacted, '\$\{POSTGRES_PASSWORD:\?[^}]*\}', '<redacted>')
    return @{ Ok = $true; Reason = 'ok'; Redacted = $redacted }
}

function Get-Server2PostgresHostCommand {
    param([string]$EnvPath, [string]$ComposePath)
    $envArg = '<env-file>'
    if (-not [string]::IsNullOrWhiteSpace($EnvPath)) { $envArg = $EnvPath }
    return 'docker compose --env-file ' + $envArg + ' -f ' + $ComposePath + ' -p antiphon-server2-postgres up -d'
}

function Write-Server2PostgresDrillPlan {
    Write-Output ('drill-project: ' + $script:Server2PostgresDrillProject)
    Write-Output ('drill-volume: ' + $script:Server2PostgresDrillVolume)
    Write-Output 'drill-remove: scratch-only'
    Write-Output 'unread: docker-ps'
    Write-Output 'unread: docker-volume-ls'
    Write-Output 'unread: docker-data-root-df'
    Write-Output 'unread: windmill-compose-parent'
    Write-Output 'unread: count-star'
    Write-Output 'unread: dump-elapsed-sec'
    Write-Output 'unread: restore-elapsed-sec'
    Write-Output 'unread: copy-elapsed-sec'
    Write-Output 'unread: dump-bytes'
    Write-Output 'phase3: user-table-count'
    Write-Output 'phase3: sequence-last-value'
    Write-Output 'phase3: extension-version'
    Write-Output 'phase3: ef-migrations-history'
    Write-Output 'phase3: key-table-count-min-max-pk'
    Write-Output 'phase3: key tables are TranscriptEntries, AgentTasks, SessionQueuedMessages, AgentTaskLandNotifications, AgentTaskEvents'
    Write-Output 'phase3: count, minimum primary key, and maximum primary key; no row body'
}

function Resolve-Server2PostgresLane {
    param([string]$DaemonName, [string]$Hostname, [bool]$InContainer)
    if ([string]::IsNullOrWhiteSpace($DaemonName)) { return 'none' }
    if ($DaemonName -ne $Hostname) { return 'sibling' }
    if ($InContainer) { return 'nested' }
    return 'host'
}

function Get-Server2PostgresObservedLane {
    # The runner container is not the host daemon. Refuse before any docker call.
    if (Test-Path -LiteralPath '/.dockerenv') { return 'nested' }
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $docker) { return 'none' }
    $daemon = (& $docker.Source info --format '{{.Name}}' 2>$null | Out-String).Trim()
    $own = (& hostname 2>$null | Out-String).Trim()
    return (Resolve-Server2PostgresLane $daemon $own (Test-Path -LiteralPath '/.dockerenv'))
}

function Invoke-Server2PostgresUp {
    param([string]$EnvPath, [string]$ComposePath)
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $docker) { return 'missing-docker' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $docker.Source
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('compose', '--env-file', $EnvPath, '-f', $ComposePath, '-p', $script:Server2PostgresProject, 'up', '-d')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) { return 'compose-up-failed' }
    return ''
}

function Invoke-Server2PostgresDeploy {
    if ($WhatIf -and $Execute) {
        Write-Server2PostgresResult 'fail' 3 'ambiguous-mode'
        return
    }
    $planOnly = -not $Execute
    if ($WhatIf) { $planOnly = $true }
    $root = Get-Server2PostgresRepoRoot
    $composePath = $ComposeFile
    if ([string]::IsNullOrWhiteSpace($composePath)) {
        $composePath = Join-Path $root 'docker-compose.server2-postgres.yml'
    }
    if (-not (Test-Path -LiteralPath $composePath)) {
        Write-Server2PostgresResult 'fail' 2 'missing-compose'
        return
    }
    $composeText = [System.IO.File]::ReadAllText($composePath)
    $composeReason = Get-Server2PostgresComposeRefusal $composeText
    if ($composeReason) {
        Write-Server2PostgresResult 'fail' 2 $composeReason
        return
    }
    $drillReason = Test-Server2PostgresDrillVolume $script:Server2PostgresDrillVolume
    if ($drillReason) {
        Write-Server2PostgresResult 'fail' 2 $drillReason
        return
    }

    $bind = ''
    $image = ''
    if (-not [string]::IsNullOrWhiteSpace($EnvFile)) {
        if (-not (Test-Path -LiteralPath $EnvFile)) {
            Write-Server2PostgresResult 'fail' 2 'env-missing'
            return
        }
        if (Test-Server2PostgresInsideRepo $EnvFile $root) {
            Write-Server2PostgresResult 'fail' 2 'env-inside-repo'
            return
        }
        if (-not (Test-Server2PostgresMode0600 $EnvFile)) {
            Write-Server2PostgresResult 'fail' 2 'env-mode'
            return
        }
        $envDoc = Read-Server2PostgresEnv $EnvFile
        $bindReason = Test-Server2PostgresBind -Provided ([bool]$envDoc.Present['ANTIPHON_POSTGRES_BIND_ADDRESS']) -Address ([string]$envDoc.Map['ANTIPHON_POSTGRES_BIND_ADDRESS'])
        if ($bindReason) {
            Write-Server2PostgresResult 'fail' 2 $bindReason
            return
        }
        $imageReason = Test-Server2PostgresImage ([string]$envDoc.Map['ANTIPHON_POSTGRES_IMAGE'])
        if ($imageReason) {
            Write-Server2PostgresResult 'fail' 2 $imageReason
            return
        }
        $password = [string]$envDoc.Map['POSTGRES_PASSWORD']
        if ([string]::IsNullOrWhiteSpace($password)) {
            Write-Server2PostgresResult 'fail' 2 'empty-password'
            return
        }
        $dev = Get-Server2PostgresDevPassword $root
        if (-not [string]::IsNullOrWhiteSpace($dev) -and $password -ceq $dev) {
            Write-Server2PostgresResult 'fail' 2 'dev-password'
            return
        }
        $bind = [string]$envDoc.Map['ANTIPHON_POSTGRES_BIND_ADDRESS']
        $image = [string]$envDoc.Map['ANTIPHON_POSTGRES_IMAGE']
    } elseif (-not $planOnly) {
        Write-Server2PostgresResult 'fail' 2 'env-missing'
        return
    }

    if ($planOnly) {
        Write-Output (Get-Server2PostgresHostCommand $EnvFile $composePath)
        if ($RestoreDrill) { Write-Server2PostgresDrillPlan }
        Write-Server2PostgresResult 'plan' 0 'ok'
        return
    }

    $lane = Get-Server2PostgresObservedLane
    if ($lane -ne 'host') {
        Write-Server2PostgresResult 'fail' 2 'not-server2-host'
        return
    }
    $tailscale = Get-Command tailscale -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $tailscale) {
        Write-Server2PostgresResult 'fail' 2 'bind-not-tailnet'
        return
    }
    $tailscaleText = (& $tailscale.Source ip -4 2>$null | Out-String)
    $tailnetReason = Test-Server2PostgresTailnet $bind $tailscaleText
    if ($tailnetReason) {
        Write-Server2PostgresResult 'fail' 2 $tailnetReason
        return
    }
    if ($RestoreDrill) {
        # The scratch restore is the operator rehearsal. This switch prints it.
        # It does not start a second project and it does not remove a volume.
        Write-Server2PostgresDrillPlan
        Write-Server2PostgresResult 'plan' 0 'ok'
        return
    }
    $up = Invoke-Server2PostgresUp $EnvFile $composePath
    if ($up) {
        Write-Server2PostgresResult 'fail' 2 $up
        return
    }
    Write-Server2PostgresResult 'ok' 0 'ok'
}

if ($MyInvocation.InvocationName -eq '.') { return }
Invoke-Server2PostgresDeploy
exit $script:Server2PostgresExit
