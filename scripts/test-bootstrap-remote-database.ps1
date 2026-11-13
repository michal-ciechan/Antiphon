#requires -Version 7.0
# F2 protocol acceptance: real psql, test-owned ephemeral loopback peers only.
# No real Postgres, docker, credentials, or persistent database is touched.
param([string]$SourceRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $SourceRoot 'scripts/lib/DatabaseEndpoint.ps1')
. (Join-Path $SourceRoot 'scripts/lib/RemoteDatabase.ps1')
Add-Type -Path (Join-Path $SourceRoot 'scripts/fixtures/Card1844PostgresStub.cs')
$passed = 0; $failed = 0
function Assert-Remote([bool]$Condition, [string]$Name) {
    if ($Condition) { $script:passed++; Write-Host "PASS F2 $Name" }
    else { $script:failed++; Write-Host "FAIL F2 $Name" }
}
$client = Get-Command psql -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $client) { throw 'F2 fixture requires the PostgreSQL psql client on PATH' }
foreach ($case in @(
    @('ready', 'ready', $true), @('wrong-db', 'identity-mismatch', $false),
    @('missing-db', 'identity-mismatch', $false), @('auth', 'authentication-failed', $false),
    @('non-postgres', 'protocol-failed', $false), @('timeout', 'timeout', $false))) {
    $peer = [Card1844PostgresStub]::new($case[0])
    try {
        $connection = 'Host=127.0.0.1;Port={0};Database=expected-db;Username=fixture-user;Password=fixture-secret-do-not-log;SSL Mode=Prefer' -f $peer.Port
        $result = Test-AntiphonRemoteDatabase -RepoRoot $SourceRoot -ConnectionString $connection
        Assert-Remote ($result.Succeeded -eq $case[2] -and $result.Reason -ceq $case[1]) "$($case[0]) has the required verdict and distinct reason (actual=$($result.Reason))"
        Assert-Remote ($result.ElapsedMs -lt 6500) "$($case[0]) stays within the five-second execution plus bounded cleanup budget"
        Assert-Remote (-not ($result | ConvertTo-Json -Compress).Contains('fixture-secret-do-not-log')) "$($case[0]) never returns client error text or credentials"
        Assert-Remote ($peer.ExpectedStartup -and $peer.SslRequested) "$($case[0]) uses the expected startup database/user and read-only options, with SSL negotiation delegated to psql"
        if ($case[0] -in @('ready', 'wrong-db')) {
            Assert-Remote $peer.ReadOnlyQuery "$($case[0]) executes the constant identity query"
        }
        Assert-Remote ([string]::IsNullOrEmpty($peer.Failure)) "$($case[0]) fixture has no protocol or lifecycle error"
        Write-Host "F2-TIMING scenario=$($case[0]) databaseClientMs=$($result.ElapsedMs)"
    } finally { $peer.Dispose() }
}
$missing = Test-AntiphonRemoteDatabase -RepoRoot $SourceRoot -ConnectionString 'Host=127.0.0.1;Database=expected-db;Username=fixture-user' -PsqlPath (Join-Path $SourceRoot '.antiphon/no-such-psql')
Assert-Remote (-not $missing.Succeeded -and $missing.Reason -eq 'client-unavailable') 'missing client fails closed'
Write-Host "bootstrap-remote-database passed=$passed failed=$failed"
if ($failed) { exit 1 }
