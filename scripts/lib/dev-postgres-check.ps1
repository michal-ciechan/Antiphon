#requires -Version 5.1
# Shared read-only database probes for bootstrap-check.ps1 and verify-dev-stack.ps1.
# Keep this file ASCII-only for Windows PowerShell 5.1.

. (Join-Path $PSScriptRoot 'dev-postgres-compose.ps1')

function Test-TcpPort {
    param([string]$HostName, [int]$Port, [int]$TimeoutMs = 2000)

    $client = $null
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $pending = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $pending.AsyncWaitHandle.WaitOne($TimeoutMs, $false)) { return $false }
        $client.EndConnect($pending)
        return $client.Connected
    } catch {
        return $false
    } finally {
        if ($client) { $client.Close() }
    }
}

function Get-DevPostgresConnectionString {
    param([string]$RepoRoot, [switch]$SimpleMode)

    # AppHost forwards its Development connection string to the server. The
    # simple launcher runs the Development server directly instead.
    foreach ($name in @('ConnectionStrings__DefaultConnection', 'ConnectionStrings:DefaultConnection')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { return $value }
    }

    if ($SimpleMode) {
        $project = 'server'
        $secretsId = 'antiphon-server'
    } else {
        $project = 'Antiphon.AppHost'
        $secretsId = 'aspire-antiphon-apphost'
    }

    $paths = @()
    if ($env:APPDATA) {
        $paths += Join-Path $env:APPDATA "Microsoft/UserSecrets/$secretsId/secrets.json"
    } else {
        $profileDir = [Environment]::GetFolderPath('UserProfile')
        if ($profileDir) { $paths += Join-Path $profileDir ".microsoft/usersecrets/$secretsId/secrets.json" }
    }
    $paths += Join-Path $RepoRoot "$project/appsettings.Development.json"
    $paths += Join-Path $RepoRoot "$project/appsettings.json"

    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $settings = [IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop
        $value = $settings.'ConnectionStrings:DefaultConnection'
        if ($null -eq $value) { $value = $settings.ConnectionStrings.DefaultConnection }
        if ($null -ne $value) { return $value }
    }
}

function Test-ConfiguredDevPostgres {
    param([string]$RepoRoot, [switch]$SimpleMode)

    $missing = @{ Ok = $false; Detail = 'ConnectionStrings:DefaultConnection has no usable TCP host and port' }
    try {
        $builder = New-Object System.Data.Common.DbConnectionStringBuilder
        # The PowerShell dictionary adapter interprets ordinary assignment as a key.
        $builder.PSBase.ConnectionString = Get-DevPostgresConnectionString -RepoRoot $RepoRoot -SimpleMode:$SimpleMode
        $serverName = ([string]$builder['Host']).Trim().Trim('[', ']')
        $hostKind = [Uri]::CheckHostName($serverName)
        if ($hostKind -eq 'Unknown') { return $missing }
        $port = 5432
        if ($builder.ContainsKey('Port') -and (
            -not [int]::TryParse([string]$builder['Port'], [ref]$port) -or $port -lt 1 -or $port -gt 65535
        )) { return $missing }
    } catch {
        # A parse error may include credentials. Report only the setting name.
        return $missing
    }

    $ok = Test-TcpPort -HostName $serverName -Port $port
    $displayName = if ($hostKind -eq 'IPv6') { "[$serverName]" } else { $serverName }
    $state = if ($ok) { 'open' } else { 'closed or unreachable' }
    return @{ Ok = $ok; Detail = ('TCP {0}:{1} {2}' -f $displayName, $port, $state) }
}
