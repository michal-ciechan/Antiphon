#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0310: probe classification is a pure function of health/client results.

    HttpClient timeout on /health while client=200 is not a dead stack.
    Connection-refused on both endpoints is. Both 200 is up.

    Never probes live 17202/17203. ASCII-only (pwsh 7 and Windows PowerShell 5.1).
#>
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')

$script:passed = 0
$script:failed = 0
$script:failures = @()

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name }
    else { Write-Fail $Name $Detail }
}

$httpTimeout = 'The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing.'
$ps51Timeout = 'The operation has timed out.'
$refusedHealth = 'No connection could be made because the target machine actively refused it. (localhost:17202)'
$refusedClient = 'No connection could be made because the target machine actively refused it. (localhost:17203)'

# --- C1: health timeout + client 200 is Slow, not restart-worthy (the 16:11 signature) ---
$c1 = Get-AppHostProbeClassification -HealthOk $false -HealthError $httpTimeout -HealthCode $null -ClientOk $true -ClientError $null -ClientCode 200
Assert-True ($c1.Kind -eq 'Slow') 'C1 health timeout + client 200 is Slow' ("Kind=$($c1.Kind)")
Assert-True (-not $c1.RestartWorthy) 'C1 health timeout + client 200 is not restart-worthy'
Assert-True ($c1.Summary -match 'client=200') 'C1 summary names client=200' $c1.Summary
Assert-True ($c1.Summary -match 'health=FAIL') 'C1 summary names health=FAIL' $c1.Summary

# --- C1b: Windows PowerShell 5.1 timeout wording ---
$c1b = Get-AppHostProbeClassification -HealthOk $false -HealthError $ps51Timeout -HealthCode $null -ClientOk $true -ClientError $null -ClientCode 200
Assert-True ($c1b.Kind -eq 'Slow' -and -not $c1b.RestartWorthy) 'C1b 5.1 "timed out" + client 200 is Slow' ("Kind=$($c1b.Kind)")

# --- C2: both connection-refused is Down ---
$c2 = Get-AppHostProbeClassification -HealthOk $false -HealthError $refusedHealth -HealthCode $null -ClientOk $false -ClientError $refusedClient -ClientCode $null
Assert-True ($c2.Kind -eq 'Down') 'C2 both connection-refused is Down' ("Kind=$($c2.Kind)")
Assert-True $c2.RestartWorthy 'C2 both connection-refused is restart-worthy'

# --- C3: health 200 + client 200 is Up ---
$c3 = Get-AppHostProbeClassification -HealthOk $true -HealthError $null -HealthCode 200 -ClientOk $true -ClientError $null -ClientCode 200
Assert-True ($c3.Kind -eq 'Up') 'C3 both 200 is Up' ("Kind=$($c3.Kind)")
Assert-True (-not $c3.RestartWorthy) 'C3 both 200 is not restart-worthy'
Assert-True ($c3.Summary -eq 'health=200 client=200') 'C3 summary is health=200 client=200' $c3.Summary

# --- C4: health connection-refused + client 200 is still Down (no listener on 17202) ---
$c4 = Get-AppHostProbeClassification -HealthOk $false -HealthError $refusedHealth -HealthCode $null -ClientOk $true -ClientError $null -ClientCode 200
Assert-True ($c4.Kind -eq 'Down' -and $c4.RestartWorthy) 'C4 health refused + client 200 is Down' ("Kind=$($c4.Kind)")

# --- C5: shared clock and named restart-apphost exit codes ---
Assert-True ($AppHostLockMaxAgeMinutes -eq 15) 'C5 AppHostLockMaxAgeMinutes is 15' ("value=$AppHostLockMaxAgeMinutes")
Assert-True ((Format-AppHostRestartExitName 0) -eq '0=healthy') 'C5 exit 0 is named healthy'
Assert-True ((Format-AppHostRestartExitName 1) -eq '1=timeout/build') 'C5 exit 1 is named timeout/build'
Assert-True ((Format-AppHostRestartExitName 3) -eq '3=refused (already unstamped)') 'C5 exit 3 is named refused'
Assert-True ((Format-AppHostRestartExitName 4) -eq '4=DCP dependency timeout') 'C5 exit 4 is named DCP timeout'

# --- C6: captured child stdout includes Write-Host and the named exit ---
$stubDir = Join-Path $env:TEMP ('apphost-probe-class-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stubDir -Force | Out-Null
$stub = Join-Path $stubDir 'stub-restart.ps1'
@(
    'Write-Host "AppHost launch FAILED: Aspire''s DCP dependency check TIMED OUT."'
    'Write-Host "  IGNORE the podman text in that message."'
    'exit 4'
) | Set-Content -LiteralPath $stub -Encoding ASCII
try {
    $psExe = $null
    try { $psExe = (Get-Process -Id $PID).Path } catch { }
    if (-not $psExe) { $psExe = 'pwsh' }
    $captured = Invoke-AppHostRestartCaptured -PowerShellExe $psExe -RestartScript $stub
    Assert-True ($captured.ExitCode -eq 4) 'C6 stub exit code is 4' ("ExitCode=$($captured.ExitCode)")
    Assert-True ($captured.ExitName -eq '4=DCP dependency timeout') 'C6 stub exit is named DCP timeout' $captured.ExitName
    $joined = @($captured.Tail) -join "`n"
    Assert-True ($joined -match 'TIMED OUT') 'C6 captured tail includes Write-Host TIMED OUT' $joined
    Assert-True ($joined -match 'podman') 'C6 captured tail includes Show-DcpTimeoutVerdict podman line' $joined
} finally {
    Remove-Item -LiteralPath $stubDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ('CARD-0310 probe-class: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ("  " + $line) }
    Write-Host 'APPHOST PROBE CLASS TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'APPHOST PROBE CLASS TESTS EXIT CODE: 0  (PASS)'
exit 0
