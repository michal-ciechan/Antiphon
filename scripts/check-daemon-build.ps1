<#
.SYNOPSIS
    Warn when an adopted daemon was built before a source change in its project closure.
.NOTES
    Advisory by default. Keep ASCII-only for Windows PowerShell 5.1.
#>
param([switch]$FailOnStale)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$staleFound = $false

$daemons = @(
    @{
        Name = 'session-runner'
        Port = 17204
        Paths = @(
            'src/Antiphon.SessionRunner',
            'src/Antiphon.SessionRunner.Contracts',
            'src/Antiphon.Agents.Pty',
            'src/Antiphon.PtyHost',
            'src/Antiphon.PtyHost.Client',
            'src/Antiphon.PtyHost.Protocol'
        )
        Fix = 'pwsh -File scripts/restart-session-runner.ps1'
    },
    @{
        Name = 'fake-gateway'
        Port = 17208
        Paths = @('src/Antiphon.Messaging.FakeGateway', 'src/Antiphon.Messaging')
        Fix = 'restart the fake-gateway daemon through its AppHost supervisor'
    }
)

Push-Location $root
try {
    foreach ($daemon in $daemons) {
        $capabilities = $null
        try {
            $capabilities = Invoke-RestMethod -Uri ("http://localhost:{0}/capabilities" -f $daemon.Port) -TimeoutSec 3
        } catch {
            Write-Host ("  {0}: build identity unavailable (daemon is down or predates /capabilities)" -f $daemon.Name) -ForegroundColor DarkGray
            continue
        }

        $build = $capabilities.build
        $sha = if ($null -eq $build) { $null } else { [string]$build.commitSha }
        if ([string]::IsNullOrWhiteSpace($sha)) {
            Write-Host ("  {0}: build identity unavailable (daemon did not report a commit SHA)" -f $daemon.Name) -ForegroundColor DarkGray
            continue
        }

        & git merge-base --is-ancestor $sha HEAD
        if ($LASTEXITCODE -ne 0) {
            Write-Host ("  {0}: build {1} is not an ancestor of HEAD; not comparing unrelated history" -f $daemon.Name, $sha) -ForegroundColor Yellow
            continue
        }

        $changes = @(& git log --oneline ("{0}..HEAD" -f $sha) -- $daemon.Paths)
        if ($LASTEXITCODE -ne 0) {
            Write-Host ("  {0}: could not compare build {1} to HEAD" -f $daemon.Name, $sha) -ForegroundColor Yellow
            continue
        }

        if ($changes.Count -eq 0) {
            Write-Host ("  {0}: build {1} has no newer source changes in its project closure" -f $daemon.Name, $sha.Substring(0, [Math]::Min(7, $sha.Length))) -ForegroundColor DarkGray
            continue
        }

        $staleFound = $true
        $built = if ($null -eq $build.assemblyWriteTimeUtc) { 'unknown build time' } else { [string]$build.assemblyWriteTimeUtc }
        $started = if ($null -eq $build.processStartUtc) { 'unknown start time' } else { [string]$build.processStartUtc }
        Write-Host ("WARNING: {0} is stale for {1} source change(s)." -f $daemon.Name, $changes.Count) -ForegroundColor Yellow
        Write-Host ("  build {0}; built {1}; running since {2}" -f $sha, $built, $started) -ForegroundColor Yellow
        $changes | Select-Object -First 5 | ForEach-Object { Write-Host ("  {0}" -f $_) -ForegroundColor Yellow }
        Write-Host ("  Fix when it is safe to restart: {0}" -f $daemon.Fix) -ForegroundColor Yellow
    }
} finally {
    Pop-Location
}

if ($FailOnStale -and $staleFound) { exit 1 }
exit 0
