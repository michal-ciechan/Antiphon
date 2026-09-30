# Offline CARD-0727 deploy wrapper fixture. No network or real server2 access.
param([string]$Method, [string]$RunnerId, [string]$Suffix, [string]$BodyJson)
$ErrorActionPreference = 'Stop'
$state = Get-Content -Raw -LiteralPath $env:C727_TEST_STATE | ConvertFrom-Json
$body = if ($BodyJson) { $BodyJson | ConvertFrom-Json } else { $null }
$trace = [ordered]@{ kind = 'http'; method = $Method; runnerId = $RunnerId; suffix = $Suffix; body = $body }
Add-Content -LiteralPath $env:C727_TEST_TRACE -Value ($trace | ConvertTo-Json -Compress -Depth 5)

if ($Method -eq 'POST') {
    if ($RunnerId -eq 'server2' -and $Suffix -eq '/drain') { $state.oldDraining = $true }
    if ($RunnerId -eq 'server2' -and $Suffix -eq '/drain/clear') { $state.oldDraining = $false }
    if ($RunnerId -eq 'server2-temp' -and $Suffix -eq '/drain') { $state.tempDraining = $true }
    if ($RunnerId -eq 'server2-temp' -and $Suffix -eq '/drain/clear') { $state.tempDraining = $false }
    $state | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:C727_TEST_STATE
    Write-Output '{}'
    exit 0
}
if ($Suffix -ne '/status') { exit 1 }
if ($RunnerId -eq 'server2-temp') {
    if ($state.scenario -eq 'missing') {
        Write-Output '__404__'
        exit 0
    }
    $accepting = $state.scenario -ne 'ineligible' -and -not $state.tempDraining
    $retiredAt = $null
    if ($state.tempDraining) { $retiredAt = '2026-09-27T10:00:00Z' }
    [ordered]@{
        acceptingNewWork = $accepting; dispatchEligible = ($state.scenario -ne 'ineligible'); buildVersion = $(if ($state.tempDeployed) { $state.sha } else { 'old' })
        draining = [bool]$state.tempDraining; redirectTo = $(if ($state.tempDraining) { 'server2' } else { $null })
        retireWhenIdle = [bool]$state.tempDraining; retiredAt = $retiredAt
        sessions = 0; runnerSessions = 0; queuedTasks = 0
    } | ConvertTo-Json -Compress
    exit 0
}
if ($RunnerId -eq 'server2') {
    $sessions = if ($state.scenario -eq 'busy') { 1 } else { 0 }
    $version = if ($state.oldDeployed -or $state.scenario -eq 'rerun') { $state.sha } else { 'old' }
    [ordered]@{
        acceptingNewWork = (-not $state.oldDraining); dispatchEligible = $true; buildVersion = $version
        draining = [bool]$state.oldDraining; redirectTo = $(if ($state.oldDraining) { 'server2-temp' } else { $null })
        retireWhenIdle = $false; retiredAt = $null
        sessions = $sessions; runnerSessions = 0; queuedTasks = 0
    } | ConvertTo-Json -Compress
    exit 0
}
exit 1
