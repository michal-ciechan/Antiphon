# ASCII-only: dot-sourced by scripts/bootstrap-check.ps1, which must parse under
# Windows PowerShell 5.1.
#
# CARD-1844: resolve the AppHost database endpoint to host, port and mode only.
# A connection string is parsed in place and dropped; nothing returned here or
# printed by a caller carries the username or the password.

$script:AntiphonAppHostUserSecretsId = 'aspire-antiphon-apphost'
$script:AntiphonLocalDatabasePort = 17280

# Returns @{ Host; Port; Mode } for one connection string, or $null when the
# string has no host or a port outside 1-65535. Mode is 'local' only for a
# loopback host (localhost, 127.0.0.1, ::1) on port 17280, the desktop compose
# container; any other host or port is 'remote'. Port defaults to 5432.
function Get-AntiphonDatabaseEndpoint {
    param([string]$ConnectionString)
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) { return $null }
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    try {
        $builder.set_ConnectionString($ConnectionString)
    } catch {
        return $null
    }
    $hostValue = $null
    foreach ($key in @('Host', 'Server')) {
        $value = $null
        if ($builder.TryGetValue($key, [ref]$value) -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            $hostValue = ([string]$value).Trim()
            break
        }
    }
    if ($null -eq $hostValue) { return $null }
    $port = 5432
    $portValue = $null
    if ($builder.TryGetValue('Port', [ref]$portValue) -and -not [string]::IsNullOrWhiteSpace([string]$portValue)) {
        $parsed = 0
        if (-not [int]::TryParse(([string]$portValue).Trim(), [ref]$parsed)) { return $null }
        if ($parsed -lt 1 -or $parsed -gt 65535) { return $null }
        $port = $parsed
    }
    $builder.Clear()
    $loopback = @('localhost', '127.0.0.1', '::1', '[::1]') -contains $hostValue.ToLowerInvariant()
    $mode = 'remote'
    if ($loopback -and $port -eq $script:AntiphonLocalDatabasePort) { $mode = 'local' }
    return New-Object PSObject -Property ([ordered]@{ Host = $hostValue; Port = $port; Mode = $mode })
}

# The AppHost user-secrets file path, mirroring Microsoft.Extensions.Configuration.UserSecrets:
# %APPDATA%\Microsoft\UserSecrets\<id>\secrets.json when APPDATA is set, otherwise
# ~/.microsoft/usersecrets/<id>/secrets.json.
function Get-AntiphonAppHostUserSecretsPath {
    $appData = $env:APPDATA
    if (-not [string]::IsNullOrWhiteSpace($appData)) {
        return [System.IO.Path]::Combine($appData, 'Microsoft', 'UserSecrets', $script:AntiphonAppHostUserSecretsId, 'secrets.json')
    }
    $userHome = $env:HOME
    if ([string]::IsNullOrWhiteSpace($userHome)) { $userHome = $env:USERPROFILE }
    if ([string]::IsNullOrWhiteSpace($userHome)) { return $null }
    return [System.IO.Path]::Combine($userHome, '.microsoft', 'usersecrets', $script:AntiphonAppHostUserSecretsId, 'secrets.json')
}

# Reads ConnectionStrings:DefaultConnection from a JSON file in either the flat
# key form ("ConnectionStrings:DefaultConnection") or the nested form. Returns
# @{ Found; Value; Error }. The value stays in the caller's scope only.
function Read-AntiphonDefaultConnection {
    param([string]$Path)
    $result = @{ Found = $false; Value = $null; Error = $false }
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $result }
    try {
        $json = [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json
    } catch {
        $result.Error = $true
        return $result
    }
    if ($null -eq $json) { return $result }
    $flat = $json.PSObject.Properties['ConnectionStrings:DefaultConnection']
    if ($null -ne $flat) {
        $result.Found = $true
        $result.Value = [string]$flat.Value
        return $result
    }
    $section = $json.PSObject.Properties['ConnectionStrings']
    if ($null -ne $section -and $null -ne $section.Value) {
        $nested = $section.Value.PSObject.Properties['DefaultConnection']
        if ($null -ne $nested) {
            $result.Found = $true
            $result.Value = [string]$nested.Value
        }
    }
    return $result
}

# Private configuration value, never written to a diagnostic. AppHost uses
# DistributedApplication.CreateBuilder(args) with the standard host providers.
# The bootstrap command-line parameter represents the final command-line key.
# Environment > Development secrets > environment JSON > base JSON > defaults.
# With no host environment override the normal AppHost launch profile is Development.
function Resolve-AntiphonDefaultConnection {
    param(
        [string]$RepoRoot,
        [string]$ConnectionString
    )
    if ($PSBoundParameters.ContainsKey('ConnectionString')) {
        return @{ Found = $true; Value = $ConnectionString; Source = 'parameter'; Error = $false }
    }
    # EnvironmentVariablesConfigurationProvider normalizes __ to : and maps
    # the four connection-string prefixes. Match keys case-insensitively.
    $variables = [Environment]::GetEnvironmentVariables()
    foreach ($key in $variables.Keys) {
        $normalized = ([string]$key).Replace('__', ':')
        if ($normalized -ieq 'ConnectionStrings:DefaultConnection' -or
            $normalized -imatch '^(CUSTOMCONNSTR_|SQLCONNSTR_|SQLAZURECONNSTR_|MYSQLCONNSTR_)DefaultConnection$') {
            return @{ Found = $true; Value = [string]$variables[$key]; Source = 'environment'; Error = $false }
        }
    }
    $environmentName = $env:DOTNET_ENVIRONMENT
    if ([string]::IsNullOrEmpty($environmentName)) { $environmentName = $env:ASPNETCORE_ENVIRONMENT }
    if ([string]::IsNullOrEmpty($environmentName)) { $environmentName = 'Development' }
    if ($environmentName -ieq 'Development') {
        $secrets = Read-AntiphonDefaultConnection (Get-AntiphonAppHostUserSecretsPath)
        if ($secrets.Found -or $secrets.Error) { $secrets.Source = 'user-secrets'; return $secrets }
    }
    foreach ($file in @("appsettings.$environmentName.json", 'appsettings.json')) {
        $settings = Read-AntiphonDefaultConnection ([System.IO.Path]::Combine($RepoRoot, 'Antiphon.AppHost', $file))
        if ($settings.Found -or $settings.Error) { $settings.Source = 'appsettings'; return $settings }
    }
    # There is no DefaultConnection in the host's in-memory defaults.
    return @{ Found = $false; Value = $null; Source = 'defaults'; Error = $false }
}

function Resolve-AntiphonDatabaseEndpoint {
    param([string]$RepoRoot, [string]$ConnectionString)
    $value = Resolve-AntiphonDefaultConnection @PSBoundParameters
    $reason = ''
    $endpoint = $null
    if ($value.Error) { $reason = 'invalid-json' }
    elseif (-not $value.Found) { $reason = 'absent' }
    elseif ([string]::IsNullOrWhiteSpace($value.Value)) { $reason = 'empty' }
    else {
        $endpoint = Get-AntiphonDatabaseEndpoint -ConnectionString $value.Value
        if ($null -eq $endpoint) { $reason = 'invalid' }
    }
    return @{ Endpoint = $endpoint; Source = $value.Source; Reason = $reason }
}

# The one probe line. Host and port only.
function Format-AntiphonDatabaseEndpoint {
    param($Endpoint)
    return ('database-endpoint host={0} port={1} mode={2}' -f $Endpoint.Host, $Endpoint.Port, $Endpoint.Mode)
}
