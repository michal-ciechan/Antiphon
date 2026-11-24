#requires -Version 7.0
# CARD-2064: exercise both entry points with no desktop container or live stack.
$ErrorActionPreference = 'Stop'

function Get-Command {
    param([string]$Name)
    if ($Name -in @('docker', 'dotnet', 'git', 'node', 'npm')) {
        return @{ Source = $Name }
    }
    if ($Name -match '^(docker|dotnet|git|node|npm)\.(exe|cmd)$') { return }
    Microsoft.PowerShell.Core\Get-Command @PSBoundParameters
}

function docker {
    $script:dockerCalls.Add(($args -join ' '))
    if ($args[0] -eq 'network') { $global:LASTEXITCODE = 0; return }
    $global:LASTEXITCODE = 1
}

function dotnet {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq '--version') { '10.0.100' }
}
function node { $global:LASTEXITCODE = 0; 'v22.0.0' }
function npm { $global:LASTEXITCODE = 0; '10.0.0' }
function git { $global:LASTEXITCODE = 1 }

function Start-Job { @{ State = 'Completed' } }
function Wait-Job {
    param([Parameter(ValueFromPipeline)]$Job, [int]$Timeout)
    process { $Job }
}
function Receive-Job { 'Server:' }
function Remove-Job {}
function Get-NetTCPConnection { @{ LocalPort = 17200, 17202, 17203, 17204, 17205, 17206, 17281, 17282 } }
function Invoke-WebRequest { @{ StatusCode = 200; Content = 'Healthy' } }

function Invoke-CheckScript {
    param([string]$Path, [string[]]$Arguments)
    $script:dockerCalls.Clear()
    $output = (& $Path @Arguments *>&1 | Out-String)
    return @{ Code = $LASTEXITCODE; Text = $output; Docker = $script:dockerCalls -join "`n" }
}

function Assert-Check {
    param([string]$Name, [bool]$Passed, [hashtable]$Run)
    if ($Passed) {
        $script:passed++
        Write-Host "PASS $Name"
    } else {
        $script:failed++
        Write-Host "FAIL $Name (exit $($Run.Code))`n$($Run.Text)"
    }
}

$source = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('c2064-' + [guid]::NewGuid().ToString('N'))
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$dockerCalls = [Collections.Generic.List[string]]::new()
$passed = $failed = 0
$savedEnv = @{}
foreach ($name in @('ANTIPHON_SKIP_DEV_POSTGRES', 'ConnectionStrings__DefaultConnection', 'ConnectionStrings:DefaultConnection', 'APPDATA', 'LOCALAPPDATA', 'USERPROFILE', 'WINDIR')) {
    $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name)
}

try {
    New-Item -ItemType Directory -Path "$fixture/scripts/lib", "$fixture/.claude/skills", "$fixture/Antiphon.AppHost", "$fixture/server" -Force | Out-Null
    foreach ($file in @('verify-dev-stack.ps1', 'docker-compose.dev.yml', 'scripts/bootstrap-check.ps1', 'scripts/apphost-common.ps1') + @(Get-ChildItem "$source/scripts/lib/dev-postgres-*.ps1" | ForEach-Object { "scripts/lib/$($_.Name)" })) {
        Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $fixture $file)
    }
    Set-Content "$fixture/scripts/check-agent-context.ps1" 'exit 0'
    Set-Content "$fixture/global.json" '{ "sdk": { "version": "10.0.100" } }'
    foreach ($name in @('APPDATA', 'LOCALAPPDATA', 'USERPROFILE', 'WINDIR')) {
        [Environment]::SetEnvironmentVariable($name, $fixture)
    }
    [Environment]::SetEnvironmentVariable('ConnectionStrings:DefaultConnection', $null)
    Set-Variable -Name IsWindows -Value $true -Scope Local -Force

    $listener.Start()
    $endpoint = $listener.LocalEndpoint.ToString()
    $connectionString = "Host=127.0.0.1;Port=$($listener.LocalEndpoint.Port);Password='dummy;Host=should-not-be-probed'"
    $secretsDir = "$fixture/Microsoft/UserSecrets/aspire-antiphon-apphost"
    New-Item -ItemType Directory $secretsDir -Force | Out-Null
    @{ 'ConnectionStrings:DefaultConnection' = $connectionString } | ConvertTo-Json | Set-Content "$secretsDir/secrets.json"

    $env:ANTIPHON_SKIP_DEV_POSTGRES = '1'
    $env:ConnectionStrings__DefaultConnection = $null
    $verify = Invoke-CheckScript "$fixture/verify-dev-stack.ps1" @('-SkipBrowser')
    Assert-Check 'Aspire verify skips the container and probes the AppHost user-secrets endpoint' (
        $verify.Code -eq 0 -and $verify.Text -match 'Postgres container\s+SKIP\s+.*ANTIPHON_SKIP_DEV_POSTGRES=1' -and
        $verify.Text.Contains("TCP $endpoint open") -and $verify.Docker -notmatch '(?m)^ps ' -and
        $verify.Text -notmatch 'dummy;Host=') $verify

    $bootstrap = Invoke-CheckScript "$fixture/scripts/bootstrap-check.ps1" @('-NoStack')
    Assert-Check 'bootstrap skips Docker Postgres probes and probes the AppHost user-secrets endpoint' (
        $bootstrap.Code -eq 0 -and $bootstrap.Text -match '\[SKIP\] docker / postgres\s+.*ANTIPHON_SKIP_DEV_POSTGRES=1' -and
        $bootstrap.Text -match '\[SKIP\] docker / volume\s+.*ANTIPHON_SKIP_DEV_POSTGRES=1' -and
        $bootstrap.Text.Contains("TCP $endpoint open") -and $bootstrap.Docker -notmatch '(?m)^(inspect|volume|compose) ' -and
        $bootstrap.Text -notmatch 'docker compose .* up|dummy;Host=') $bootstrap

    $env:ConnectionStrings__DefaultConnection = $connectionString
    $simple = Invoke-CheckScript "$fixture/verify-dev-stack.ps1" @('-SimpleMode', '-SkipBrowser')
    Assert-Check 'compose verify skips the local port and probes the configured endpoint' (
        $simple.Code -eq 0 -and $simple.Text -match 'Port 17280\s+SKIP\s+.*ANTIPHON_SKIP_DEV_POSTGRES=1' -and
        $simple.Text.Contains("TCP $endpoint open")) $simple

    $env:ConnectionStrings__DefaultConnection = 'Password=invalid;Port=bad'
    $invalid = Invoke-CheckScript "$fixture/verify-dev-stack.ps1" @('-SkipBrowser')
    Assert-Check 'explicit invalid database configuration fails without disclosing the string or falling back' (
        $invalid.Code -eq 1 -and $invalid.Text -match 'Database endpoint\s+FAIL' -and
        $invalid.Text -notmatch 'Password=invalid|docker compose .* up' -and -not $invalid.Text.Contains("TCP $endpoint open")) $invalid

    # Do not alter the real User setting when running on a desktop.
    $env:ANTIPHON_SKIP_DEV_POSTGRES = $null
    if ([Environment]::GetEnvironmentVariable('ANTIPHON_SKIP_DEV_POSTGRES', 'User') -eq '1') {
        $env:ANTIPHON_SKIP_DEV_POSTGRES = '0'
    }
    $verify = Invoke-CheckScript "$fixture/verify-dev-stack.ps1" @('-SkipBrowser')
    $bootstrap = Invoke-CheckScript "$fixture/scripts/bootstrap-check.ps1" @('-NoStack')
    Assert-Check 'Aspire verify still fails on an absent container with the flag disabled' (
        $verify.Code -eq 1 -and $verify.Text -match 'Postgres container\s+FAIL' -and $verify.Docker -match '(?m)^ps ') $verify
    Assert-Check 'bootstrap still checks the container and volume with the flag disabled' (
        $bootstrap.Code -ge 2 -and $bootstrap.Text -match '\[FAIL\] docker / postgres' -and
        $bootstrap.Text -match '\[FAIL\] docker / volume' -and $bootstrap.Docker -match '(?m)^compose .*pg_isready') $bootstrap
}
finally {
    $listener.Stop()
    foreach ($name in $savedEnv.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnv[$name]) }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}

Write-Host "CARD-2064 dev stack Postgres: $passed passed, $failed failed"
if ($failed) { exit 1 }
