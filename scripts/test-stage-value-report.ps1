#requires -Version 5.1
<#
.SYNOPSIS
    Fixture/shim tests for scripts/stage-value-report.ps1 (CARD-0272 S4).

    All HTTP is injected via -HttpShim. This file never calls a live API.

    ASCII-only: parses under pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'

$here = $PSScriptRoot
$report = Join-Path $here 'stage-value-report.ps1'

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

function Assert-Eq {
    param($Actual, $Expected, [string]$Name)
    if ($Actual -eq $Expected) { Write-Pass $Name }
    else { Write-Fail $Name "expected=$Expected actual=$Actual" }
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name }
    else { Write-Fail $Name $Detail }
}

if (-not (Test-Path -LiteralPath $report)) { throw "missing $report" }

$nonAscii = [IO.File]::ReadAllBytes($report) | Where-Object { $_ -gt 127 }
Assert-Eq @($nonAscii).Count 0 'stage-value-report.ps1 is ASCII-only'

function New-SummaryRow {
    param(
        [string]$Stage,
        [int]$Runs,
        [int]$Found,
        [int]$Clean,
        [int]$Skipped = 0,
        [int]$Failed = 0,
        [int]$Unreported = 0,
        $HitPercent = $null,
        [decimal]$UsdSpent = 0,
        $UsdPerFinding = $null,
        [int]$ServerSecs = 0
    )
    return [pscustomobject]@{
        stage         = $Stage
        runs          = $Runs
        found         = $Found
        clean         = $Clean
        skipped       = $Skipped
        failed        = $Failed
        unreported    = $Unreported
        hitPercent    = $HitPercent
        usdSpent      = $UsdSpent
        usdPerFinding = $UsdPerFinding
        serverSecs    = $ServerSecs
    }
}

$script:state = [pscustomobject]@{ Calls = New-Object 'System.Collections.Generic.List[object]' }

function Reset-Calls {
    $script:state.Calls.Clear()
}

function Get-CallsMatching {
    param([string]$Pattern)
    return @($script:state.Calls | Where-Object { $_.Uri -match $Pattern })
}

function New-Shim {
    param($ListResult, $CardResult = $null)
    $state = $script:state
    return {
        param($Method, $Uri, $Headers, $Body)
        [void]$state.Calls.Add([pscustomobject]@{ Method = $Method; Uri = [string]$Uri; Body = $Body })
        $u = [string]$Uri
        if ($Method -eq 'GET' -and $u -match '/api/cards/') {
            if ($null -eq $CardResult) { throw "unexpected card lookup: $u" }
            return $CardResult
        }
        if ($Method -eq 'GET' -and $u -match '/api/stage-outcomes') {
            return $ListResult
        }
        throw ("unhandled shim {0} {1}" -f $Method, $u)
    }.GetNewClosure()
}

function Invoke-Report {
    param([hashtable]$Params, $Shim)
    $Params['PassThru'] = $true
    $Params['HttpShim'] = $Shim
    $Params['Api'] = 'http://stage-value-report.test'
    return & $report @Params 6>&1
}

# --- T1 table shape: header, rule, one row per stage, right query params ----
Reset-Calls
$t1List = [pscustomobject]@{
    rows    = @()
    summary = @(
        (New-SummaryRow -Stage 'Rebase' -Runs 47 -Found 1 -Clean 46 -HitPercent 2.1 -UsdSpent 2.51 -UsdPerFinding 2.51)
        (New-SummaryRow -Stage 'Verify' -Runs 47 -Found 0 -Clean 13 -Skipped 34 -HitPercent 0.0 -ServerSecs 5200)
        (New-SummaryRow -Stage 'Cleanup' -Runs 47 -Found 0 -Clean 38 -Failed 9 -HitPercent $null)
        (New-SummaryRow -Stage 'Deploy' -Runs 3 -Found 3 -Clean 0 -HitPercent 100.0 -UsdSpent 4.5 -UsdPerFinding 1.5)
    )
}
$t1Out = & $report -HttpShim (New-Shim -ListResult $t1List) -Api 'http://stage-value-report.test' 2>&1
$t1Text = ($t1Out -join "`n")
Assert-True ($t1Text -match 'Stage\s+Runs\s+Found\s+Clean\s+Skipped\s+Failed\s+Unreported\s+Hit%') 'T1 header row present' $t1Text
Assert-True ($t1Text -match 'Rebase') 'T1 Rebase row present' $t1Text
Assert-True ($t1Text -match 'Deploy') 'T1 Deploy row present' $t1Text
Assert-True ($t1Text -match '100\.0%') 'T1 Deploy hit percent formatted' $t1Text
Assert-True ($t1Text -match '\$1\.50') 'T1 Deploy usd-per-finding formatted' $t1Text
$t1Calls = Get-CallsMatching -Pattern '/api/stage-outcomes'
Assert-Eq $t1Calls.Count 1 'T1 one stage-outcomes call'
Assert-True (($t1Calls[0].Uri) -notmatch '\?') 'T1 no query params when none given' $t1Calls[0].Uri

# --- T2 filters: -Since/-Until/-Stage/-Card build the right query string ----
Reset-Calls
$t2List = [pscustomobject]@{ rows = @(); summary = @((New-SummaryRow -Stage 'Review' -Runs 2 -Found 1 -Clean 1 -HitPercent 50.0)) }
$t2Card = [pscustomobject]@{ id = 'aaaaaaaa-1111-2222-3333-444444444444'; identifier = 'CARD-0272' }
$t2Result = Invoke-Report -Params @{
    Since = '2026-09-01T00:00:00Z'
    Until = '2026-09-04T00:00:00Z'
    Stage = 'Review'
    Card  = 'CARD-0272'
} -Shim (New-Shim -ListResult $t2List -CardResult $t2Card)
$t2CardCalls = Get-CallsMatching -Pattern '/api/cards/'
Assert-Eq $t2CardCalls.Count 1 'T2 one card resolve call'
Assert-True (([string]$t2CardCalls[0].Uri) -match [regex]::Escape('CARD-0272')) 'T2 card call names CARD-0272' $t2CardCalls[0].Uri
$t2Calls = Get-CallsMatching -Pattern '/api/stage-outcomes'
Assert-Eq $t2Calls.Count 1 'T2 one stage-outcomes call'
$t2Uri = [string]$t2Calls[0].Uri
Assert-True ($t2Uri -match 'since=2026-09-01') 'T2 since in query' $t2Uri
Assert-True ($t2Uri -match 'until=2026-09-04') 'T2 until in query' $t2Uri
Assert-True ($t2Uri -match 'stage=Review') 'T2 stage in query' $t2Uri
Assert-True ($t2Uri -match [regex]::Escape('cardId=aaaaaaaa-1111-2222-3333-444444444444')) 'T2 cardId resolved to guid' $t2Uri

# --- T3 -Json prints the raw DTO and returns it via -PassThru ---------------
Reset-Calls
$t3List = [pscustomobject]@{ rows = @([pscustomobject]@{ id = 'r1'; stage = 'Deploy' }); summary = @((New-SummaryRow -Stage 'Deploy' -Runs 1 -Found 1 -Clean 0)) }
$t3Out = & $report -HttpShim (New-Shim -ListResult $t3List) -Api 'http://stage-value-report.test' -Json 2>&1
$t3Text = ($t3Out -join "`n")
Assert-True ($t3Text -match '"stage"') 'T3 -Json prints raw dto' $t3Text
Assert-True ($t3Text -match '"r1"') 'T3 -Json includes row id' $t3Text

# --- T4 empty summary -> friendly message, no table -------------------------
Reset-Calls
$t4List = [pscustomobject]@{ rows = @(); summary = @() }
$t4Out = & $report -HttpShim (New-Shim -ListResult $t4List) -Api 'http://stage-value-report.test' 2>&1
$t4Text = ($t4Out -join "`n")
Assert-True ($t4Text -match 'No stage outcomes in range') 'T4 empty range message' $t4Text

# --- T5 -IncludeSuperseded adds latestOnly=false -----------------------------
Reset-Calls
$t5List = [pscustomobject]@{ rows = @(); summary = @((New-SummaryRow -Stage 'Rebase' -Runs 1 -Found 0 -Clean 1)) }
& $report -HttpShim (New-Shim -ListResult $t5List) -Api 'http://stage-value-report.test' -IncludeSuperseded 2>&1 | Out-Null
$t5Calls = Get-CallsMatching -Pattern '/api/stage-outcomes'
Assert-True (([string]$t5Calls[0].Uri) -match 'latestOnly=false') 'T5 latestOnly=false in query' $t5Calls[0].Uri

Write-Host ''
Write-Host ('stage-value-report tests: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ('  ' + $line) }
    Write-Host 'STAGE VALUE REPORT TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'STAGE VALUE REPORT TESTS EXIT CODE: 0  (PASS)'
exit 0
