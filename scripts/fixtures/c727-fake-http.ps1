# Offline CARD-0727 deploy wrapper fixture. No network or real server2 access.
param([string]$Method, [string]$RunnerId, [string]$Suffix, [string]$BodyJson)
$ErrorActionPreference = 'Stop'
$state = Get-Content -Raw -LiteralPath $env:C727_TEST_STATE | ConvertFrom-Json
$body = if ($BodyJson) { $BodyJson | ConvertFrom-Json } else { $null }
$trace = [ordered]@{ kind = 'http'; method = $Method; runnerId = $RunnerId; suffix = $Suffix; body = $body }
Add-Content -LiteralPath $env:C727_TEST_TRACE -Value ($trace | ConvertTo-Json -Compress -Depth 5)

if ($Method -eq 'POST') {
    if ($Suffix -notin @('/drain', '/drain/clear') -or $RunnerId -notin @('server2', 'server2-temp')) { exit 2 }
    if ($RunnerId -eq 'server2' -and $Suffix -eq '/drain') { $state.oldDraining = $true }
    if ($RunnerId -eq 'server2' -and $Suffix -eq '/drain/clear') { $state.oldDraining = $false }
    if ($RunnerId -eq 'server2-temp' -and $Suffix -eq '/drain') {
        $state.tempDraining = $true
        if ([string]$body.reason -eq 'CARD-0727 rolling upgrade complete') { $state.tempRetiredAt = '2026-09-27T10:00:00Z' }
    }
    if ($RunnerId -eq 'server2-temp' -and $Suffix -eq '/drain/clear') {
        $state.tempDraining = $false
        $state.tempRetiredAt = $null
    }
    $state | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:C727_TEST_STATE
    Write-Output '{}'
    exit 0
}
if ($Suffix -ne '/status') { exit 1 }
if ([string]$state.faultRunner -eq $RunnerId -and [string]$state.faultKind -eq '503') {
    Write-Output '__503__'
    exit 0
}
if ($RunnerId -eq 'server2-temp') {
    if ($state.scenario -eq 'missing') {
        Write-Output '__404__'
        exit 0
    }
    $offline = (-not $state.tempContainer) -or $state.tempOffline
    $accepting = $state.scenario -ne 'ineligible' -and -not $state.tempDraining -and -not $offline
    $status = [ordered]@{
        available = (-not $offline); acceptingNewWork = $accepting; dispatchEligible = ($state.scenario -ne 'ineligible' -and -not $offline); buildVersion = $(if ($state.tempDeployed) { $state.sha } else { 'old' })
        draining = [bool]$state.tempDraining; redirectTo = $(if ($state.tempDraining) { 'server2' } else { $null })
        retireWhenIdle = [bool]$state.tempDraining; retiredAt = $state.tempRetiredAt
        sessions = 0; runnerSessions = $(if ($offline) { $null } else { 0 }); queuedTasks = 0
    }
    if ($state.faultRunner -eq $RunnerId -and $state.faultField) {
        if ($state.faultKind -eq 'omitted') { $status.Remove([string]$state.faultField) }
        else { $status[[string]$state.faultField] = $state.faultValue }
    }
    $status | ConvertTo-Json -Compress
    exit 0
}
if ($RunnerId -eq 'server2') {
    $sessions = if ($state.scenario -eq 'busy') { 1 } else { 0 }
    $version = if ($state.oldDeployed -or $state.scenario -eq 'rerun') { $state.sha } else { 'old' }
    $status = [ordered]@{
        acceptingNewWork = (-not $state.oldDraining); dispatchEligible = $true; buildVersion = $version
        draining = [bool]$state.oldDraining; redirectTo = $(if ($state.oldDraining) { 'server2-temp' } else { $null })
        retireWhenIdle = $false; retiredAt = $null
        sessions = $sessions; runnerSessions = 0; queuedTasks = 0
    }
    if ($state.faultRunner -eq $RunnerId -and $state.faultField) {
        if ($state.faultKind -eq 'omitted') { $status.Remove([string]$state.faultField) }
        else { $status[[string]$state.faultField] = $state.faultValue }
    }
    $status | ConvertTo-Json -Compress
    exit 0
}
exit 1
