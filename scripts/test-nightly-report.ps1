#requires -Version 5.1
<#
.SYNOPSIS
    Fixture/shim tests T1-T10 for scripts/nightly-report.ps1 (CARD-0124).

    All HTTP is injected via -HttpShim. This file never calls a live API.

    ASCII-only: parses under pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'

$here = $PSScriptRoot
$report = Join-Path $here 'nightly-report.ps1'
$tunitLog = Join-Path $here (Join-Path 'fixtures' (Join-Path 'nightly' 'tunit-fail-tail.log'))
$vitestLog = Join-Path $here (Join-Path 'fixtures' (Join-Path 'nightly' 'vitest-fail-tail.log'))

$script:passed = 0
$script:failed = 0
$script:failures = @()

$boardId = '8988ca03-7414-47ad-b0b6-51556c701703'
$doneColumnId = 'dddddddd-dddd-dddd-dddd-dddddddddddd'
$backlogColumnId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
$inProgressColumnId = 'iiiiiiii-iiii-iiii-iiii-iiiiiiiiiiii'

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

function New-TestDir {
    $dir = Join-Path $env:TEMP ('nightly-report-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function New-FakeCard {
    param(
        [string]$Id = ([guid]::NewGuid().ToString()),
        [string]$Identifier = 'CARD-0400',
        [string]$Status = 'Backlog',
        [object]$Labels = @('nightly', 'tests'),
        [string]$Title = 'Nightly red',
        [string]$Description = 'old body',
        [string]$TerminalReason = $null,
        [string]$UpdatedAt = ([datetime]::UtcNow.ToUniversalTime().ToString('o')),
        $AssignedAgentId = $null,
        $OwnerSessionId = $null,
        $ArchivedAt = $null,
        [string]$ConcurrencyToken = 'tok-1'
    )
    return [pscustomobject]@{
        id                = $Id
        identifier        = $Identifier
        status            = $Status
        labels            = @($Labels)
        title             = $Title
        description       = $Description
        terminalReason    = $TerminalReason
        updatedAt         = $UpdatedAt
        assignedAgentId   = $AssignedAgentId
        ownerSessionId    = $OwnerSessionId
        archivedAt        = $ArchivedAt
        concurrencyToken  = $ConcurrencyToken
        boardId           = $boardId
        revisionCount     = 1
    }
}

function New-RedSummaryObject {
    param(
        [string]$LogDir,
        [string]$ClientLog = '',
        [string]$AntiphonLog = '',
        [int]$ClientFailed = 1,
        [object]$ClientFailedTests = @(),
        [string]$Outcome = 'TESTS',
        [bool]$Succeeded = $false,
        [string]$Sha = 'f6856040deadbeef'
    )
    $suites = @()
    if ($ClientLog -or $ClientFailed -gt 0 -or $null -ne $ClientFailedTests) {
        $suites += [ordered]@{
            id           = 'client'
            name         = 'client'
            log          = $ClientLog
            exitCode     = 1
            timedOut     = $false
            skipped      = $false
            result       = 'FAIL'
            countsParsed = $true
            passed       = 10
            failed       = $ClientFailed
            skippedCount = 0
            durationSeconds = 12.5
            failedTests  = @($ClientFailedTests)
            detail       = 'counts parsed from summary line'
        }
    }
    if ($AntiphonLog) {
        $suites += [ordered]@{
            id           = 'antiphon'
            name         = 'Antiphon.Tests'
            log          = $AntiphonLog
            exitCode     = 1
            timedOut     = $false
            skipped      = $false
            result       = 'FAIL'
            countsParsed = $true
            passed       = 98
            failed       = 2
            skippedCount = 0
            durationSeconds = 26.4
            failedTests  = @()
            detail       = 'counts parsed from TUnit summary'
        }
    }
    return [ordered]@{
        startedAt       = '2026-09-05T00:30:00Z'
        completedAt     = '2026-09-05T00:45:00Z'
        durationSeconds = 900
        sha             = $Sha
        gitRef          = 'origin/master'
        clone           = 'C:\Antiphon\nightly\checkout'
        logDir          = $LogDir
        selectedSuites  = @('client')
        outcome         = $Outcome
        succeeded       = $Succeeded
        preflight       = [ordered]@{
            concurrent = [ordered]@{ total = 0 }
            docker     = [ordered]@{ version = '29.5.3'; exitCode = 0 }
        }
        builds          = @(
            [ordered]@{ name = 'npm ci'; result = 'pass'; exitCode = 0; durationSeconds = 12; detail = '12s' }
            [ordered]@{ name = 'npm run build'; result = 'pass'; exitCode = 0; durationSeconds = 20; detail = '20s' }
            [ordered]@{ name = 'dotnet build Antiphon.sln'; result = 'pass'; exitCode = 0; durationSeconds = 40; detail = '40s' }
        )
        suites          = $suites
    }
}

function New-GreenSummaryObject {
    param([string]$LogDir, [string]$Sha = 'abc1234green')
    $obj = New-RedSummaryObject -LogDir $LogDir -ClientFailed 0 -ClientFailedTests @() -Outcome 'green' -Succeeded $true -Sha $Sha
    $obj.suites = @(
        [ordered]@{
            id = 'client'; name = 'client'; log = ''; exitCode = 0; timedOut = $false; skipped = $false
            result = 'pass'; countsParsed = $true; passed = 461; failed = 0; skippedCount = 0
            durationSeconds = 350; failedTests = @(); detail = 'counts parsed from summary line'
        }
    )
    $obj.succeeded = $true
    $obj.outcome = 'green'
    return $obj
}

function Save-Summary {
    param($Object, [string]$Path)
    $Object | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Reset-Store {
    param($Cards)
    $script:state.Calls.Clear()
    $script:state.Cards.Clear()
    $script:state.ThrowAll = $false
    $script:state.CreatedCount = 0
    foreach ($c in @($Cards)) { [void]$script:state.Cards.Add($c) }
}

function Get-MutatingCalls {
    return @($script:state.Calls | Where-Object {
        $_.Method -eq 'POST' -or $_.Method -eq 'PATCH' -or $_.Method -eq 'PUT' -or $_.Method -eq 'DELETE'
    })
}

function Get-CallsMatching {
    param([string]$Method, [string]$Pattern)
    return @($script:state.Calls | Where-Object { $_.Method -eq $Method -and $_.Uri -match $Pattern })
}

$script:state = [pscustomobject]@{
    Calls        = New-Object 'System.Collections.Generic.List[object]'
    Cards        = New-Object 'System.Collections.Generic.List[object]'
    ThrowAll     = $false
    CreatedCount = 0
}
$state = $script:state
$script:shim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$state.Calls.Add([pscustomobject]@{
        Method  = $Method
        Uri     = [string]$Uri
        Headers = $Headers
        Body    = $Body
    })
    if ($state.ThrowAll) { throw 'shim-down' }

    $u = [string]$Uri
    if ($Method -eq 'GET' -and $u -match '/api/boards$') {
        return @(
            [pscustomobject]@{ id = $boardId; name = 'Antiphon' }
        )
    }
    if ($Method -eq 'GET' -and $u -match '/api/boards/.+/columns') {
        return @(
            [pscustomobject]@{ id = $backlogColumnId; name = 'Backlog'; columnOrder = 0; isTerminal = $false; cardStatus = 'Backlog' }
            [pscustomobject]@{ id = $inProgressColumnId; name = 'In Progress'; columnOrder = 1; isTerminal = $false; cardStatus = 'InProgress' }
            [pscustomobject]@{ id = $doneColumnId; name = 'Done'; columnOrder = 4; isTerminal = $true; cardStatus = 'Done' }
        )
    }
    if ($Method -eq 'GET' -and $u -match '/api/cards\?') {
        $status = 'Backlog'
        $m = [regex]::Match($u, 'status=([^&]+)')
        if ($m.Success) { $status = [uri]::UnescapeDataString($m.Groups[1].Value) }
        $matched = @($state.Cards | Where-Object { [string]$_.status -eq $status })
        return [pscustomobject]@{ cards = @($matched); truncated = $false }
    }
    if ($Method -eq 'POST' -and $u -match '/api/boards/.+/cards$') {
        $parsed = $Body | ConvertFrom-Json
        $state.CreatedCount++
        $card = New-FakeCard -Id ([guid]::NewGuid().ToString()) -Identifier ('CARD-{0:D4}' -f (500 + $state.CreatedCount)) `
            -Title $parsed.title -Description $parsed.description -Labels @($parsed.labels) -Status 'Backlog'
        [void]$state.Cards.Add($card)
        return $card
    }
    if ($Method -eq 'PATCH' -and $u -match '/api/cards/([^/]+)/content') {
        $id = $Matches[1]
        $parsed = $Body | ConvertFrom-Json
        $card = @($state.Cards | Where-Object { $_.id -eq $id }) | Select-Object -First 1
        if ($null -eq $card) { throw "no card $id" }
        if ($parsed.title) { $card.title = [string]$parsed.title }
        if ($parsed.description) { $card.description = [string]$parsed.description }
        if ($parsed.labels) { $card.labels = @($parsed.labels) }
        $card.concurrencyToken = 'tok-rotated'
        $card.revisionCount = [int]$card.revisionCount + 1
        return $card
    }
    if ($Method -eq 'PATCH' -and $u -match '/api/cards/([^/]+)$') {
        $id = $Matches[1]
        $parsed = $Body | ConvertFrom-Json
        $card = @($state.Cards | Where-Object { $_.id -eq $id }) | Select-Object -First 1
        if ($null -eq $card) { throw "no card $id" }
        if ([string]$parsed.boardColumnId -eq $doneColumnId) {
            $card.status = 'Done'
            $card.terminalReason = [string]$parsed.reason
        }
        $card.concurrencyToken = 'tok-moved'
        return [pscustomobject]@{ card = $card; spawnedSessionId = $null; spawnSuppressed = $false }
    }
    if ($Method -eq 'POST' -and $u -match '/api/cards/([^/]+)/discussion') {
        return [pscustomobject]@{ id = [guid]::NewGuid().ToString(); body = (($Body | ConvertFrom-Json).body) }
    }
    if ($Method -eq 'POST' -and $u -match '/api/cards/([^/]+)/reopen') {
        $id = $Matches[1]
        $card = @($state.Cards | Where-Object { $_.id -eq $id }) | Select-Object -First 1
        if ($null -eq $card) { throw "no card $id" }
        $card.status = 'Backlog'
        $card.terminalReason = $null
        $card.concurrencyToken = 'tok-reopened'
        return [pscustomobject]@{ card = $card }
    }
    throw ("unhandled shim {0} {1}" -f $Method, $u)
}.GetNewClosure()

function Invoke-Report {
    param([hashtable]$Params)
    $Params['PassThru'] = $true
    $Params['HttpShim'] = $script:shim
    $Params['Board'] = 'Antiphon'
    $Params['Api'] = 'http://nightly.test'
    return & $report @Params
}

if (-not (Test-Path -LiteralPath $report)) { throw "missing $report" }
if (-not (Test-Path -LiteralPath $tunitLog)) { throw "missing $tunitLog" }
if (-not (Test-Path -LiteralPath $vitestLog)) { throw "missing $vitestLog" }

$nonAscii = [IO.File]::ReadAllBytes($report) | Where-Object { $_ -gt 127 }
Assert-Eq @($nonAscii).Count 0 'nightly-report.ps1 is ASCII-only'
$nonAsciiRun = [IO.File]::ReadAllBytes((Join-Path $here 'nightly-run.ps1')) | Where-Object { $_ -gt 127 }
Assert-Eq @($nonAsciiRun).Count 0 'nightly-run.ps1 is ASCII-only'
$nonAsciiTests = [IO.File]::ReadAllBytes((Join-Path $here 'nightly-tests.ps1')) | Where-Object { $_ -gt 127 }
Assert-Eq @($nonAsciiTests).Count 0 'nightly-tests.ps1 is ASCII-only'

# --- T1 red + no card -> one POST with labels and D4 body -------------------
$t1Dir = New-TestDir
$t1Summary = Join-Path $t1Dir 'summary.json'
Save-Summary -Object (New-RedSummaryObject -LogDir $t1Dir -ClientLog $vitestLog -ClientFailed 1 -ClientFailedTests @()) -Path $t1Summary
Reset-Store -Cards @()
$t1 = Invoke-Report @{ Summary = $t1Summary }
Assert-Eq $t1.ExitCode 0 'T1 exit 0'
Assert-Eq $t1.Action 'created' 'T1 action created'
$t1Posts = Get-CallsMatching -Method POST -Pattern '/api/boards/.+/cards$'
Assert-Eq $t1Posts.Count 1 'T1 one POST create'
$t1Body = $t1Posts[0].Body | ConvertFrom-Json
Assert-True ($t1Body.labels -contains 'nightly') 'T1 labels include nightly' (($t1Body.labels | Out-String))
Assert-True ($t1Body.labels -contains 'tests') 'T1 labels include tests' (($t1Body.labels | Out-String))
Assert-True ([string]$t1Body.title -match 'Nightly red 2026-09-05: client 1 failed') 'T1 title has class and count' ([string]$t1Body.title)
Assert-True ([string]$t1Body.description -match '## Run ') 'T1 body has run section' 'missing ## Run'
Assert-True ([string]$t1Body.description -match '\| Step \| Result \| Detail \|') 'T1 body has step table' 'missing table'
Assert-True ([string]$t1Body.description -match 'New since last run') 'T1 body has delta heading' 'missing delta'
Assert-True ([string]$t1Body.description -match [regex]::Escape($t1Dir)) 'T1 body names log dir' $t1Body.description
Assert-True ((Get-CallsMatching -Method PATCH -Pattern '.').Count -eq 0) 'T1 no PATCH'

# --- T2 red + open card -> PATCH content + discussion, no create ------------
$t2Dir = New-TestDir
$t2Summary = Join-Path $t2Dir 'summary.json'
Save-Summary -Object (New-RedSummaryObject -LogDir $t2Dir -ClientLog $vitestLog -ClientFailed 1) -Path $t2Summary
$t2Card = New-FakeCard -Id '11111111-1111-1111-1111-111111111111' -Identifier 'CARD-0124' `
    -Status 'Backlog' -Description "## Run 2026-09-04 00:30 Europe/London -- RED (TESTS) -- 10m -- origin/master oldsha`nold"
Reset-Store -Cards @($t2Card)
$t2 = Invoke-Report @{ Summary = $t2Summary }
Assert-Eq $t2.ExitCode 0 'T2 exit 0'
Assert-Eq $t2.Action 'updated' 'T2 action updated'
Assert-Eq (Get-CallsMatching -Method POST -Pattern '/api/boards/.+/cards$').Count 0 'T2 no POST create'
$t2Patch = Get-CallsMatching -Method PATCH -Pattern '/content$'
Assert-Eq $t2Patch.Count 1 'T2 one PATCH content'
$t2PatchBody = $t2Patch[0].Body | ConvertFrom-Json
Assert-True ([string]$t2PatchBody.description -match '(?s)^## Run 2026-09-05.*## Run 2026-09-04') 'T2 new section first' ([string]$t2PatchBody.description.Substring(0, [math]::Min(200, $t2PatchBody.description.Length)))
$t2Disc = Get-CallsMatching -Method POST -Pattern '/discussion$'
Assert-Eq $t2Disc.Count 1 'T2 one discussion post'
$t2DiscBody = $t2Disc[0].Body | ConvertFrom-Json
Assert-True ([string]$t2DiscBody.body -match 'still red on 2026-09-05') 'T2 discussion still red' ([string]$t2DiscBody.body)

# --- T3 green + open Backlog unassigned -> auto-close -----------------------
$t3Dir = New-TestDir
$t3Summary = Join-Path $t3Dir 'summary.json'
Save-Summary -Object (New-GreenSummaryObject -LogDir $t3Dir) -Path $t3Summary
$t3Card = New-FakeCard -Id '22222222-2222-2222-2222-222222222222' -Identifier 'CARD-0125' -Status 'Backlog'
Reset-Store -Cards @($t3Card)
$t3 = Invoke-Report @{ Summary = $t3Summary }
Assert-Eq $t3.ExitCode 0 'T3 exit 0'
Assert-Eq $t3.Action 'closed' 'T3 action closed'
$t3Move = Get-CallsMatching -Method PATCH -Pattern '/api/cards/22222222-2222-2222-2222-222222222222$'
Assert-Eq $t3Move.Count 1 'T3 one move PATCH'
$t3MoveBody = $t3Move[0].Body | ConvertFrom-Json
Assert-Eq ([string]$t3MoveBody.boardColumnId) $doneColumnId 'T3 moves to terminal column'
Assert-True ([string]$t3MoveBody.reason -match '^\[nightly auto-close\]') 'T3 reason nightly auto-close' ([string]$t3MoveBody.reason)
Assert-Eq (Get-CallsMatching -Method POST -Pattern '/discussion$').Count 0 'T3 no discussion'

# --- T4 green + open InProgress -> discussion only --------------------------
$t4Dir = New-TestDir
$t4Summary = Join-Path $t4Dir 'summary.json'
Save-Summary -Object (New-GreenSummaryObject -LogDir $t4Dir) -Path $t4Summary
$t4Card = New-FakeCard -Id '33333333-3333-3333-3333-333333333333' -Identifier 'CARD-0126' -Status 'InProgress'
Reset-Store -Cards @($t4Card)
$t4 = Invoke-Report @{ Summary = $t4Summary }
Assert-Eq $t4.ExitCode 0 'T4 exit 0'
Assert-Eq $t4.Action 'discussion' 'T4 action discussion'
Assert-Eq (Get-CallsMatching -Method PATCH -Pattern '.').Count 0 'T4 no PATCH'
$t4Disc = Get-CallsMatching -Method POST -Pattern '/discussion$'
Assert-Eq $t4Disc.Count 1 'T4 one discussion'
$t4DiscBody = $t4Disc[0].Body | ConvertFrom-Json
Assert-True ([string]$t4DiscBody.body -match 'not closing because the card is InProgress') 'T4 discussion names InProgress' ([string]$t4DiscBody.body)

# --- T5 red + Done auto-closed 3 days ago -> reopen then update -------------
$t5Dir = New-TestDir
$t5Summary = Join-Path $t5Dir 'summary.json'
Save-Summary -Object (New-RedSummaryObject -LogDir $t5Dir -ClientFailed 1) -Path $t5Summary
$t5Updated = [datetime]::UtcNow.AddDays(-3).ToString('o')
$t5Card = New-FakeCard -Id '44444444-4444-4444-4444-444444444444' -Identifier 'CARD-0127' -Status 'Done' `
    -TerminalReason '[nightly auto-close] green on 2026-09-02 at abc' -UpdatedAt $t5Updated
Reset-Store -Cards @($t5Card)
$t5 = Invoke-Report @{ Summary = $t5Summary }
Assert-Eq $t5.ExitCode 0 'T5 exit 0'
Assert-Eq $t5.Action 'reopened' 'T5 action reopened'
$t5Reopen = Get-CallsMatching -Method POST -Pattern '/reopen$'
Assert-Eq $t5Reopen.Count 1 'T5 one reopen'
$t5ReopenBody = $t5Reopen[0].Body | ConvertFrom-Json
Assert-True ([string]$t5ReopenBody.reason -match 'red again on') 'T5 reopen reason' ([string]$t5ReopenBody.reason)
Assert-Eq (Get-CallsMatching -Method PATCH -Pattern '/content$').Count 1 'T5 then PATCH content'
Assert-Eq (Get-CallsMatching -Method POST -Pattern '/api/boards/.+/cards$').Count 0 'T5 no new card'

# --- T5b same but 9 days ago -> new card ------------------------------------
$t5bDir = New-TestDir
$t5bSummary = Join-Path $t5bDir 'summary.json'
Save-Summary -Object (New-RedSummaryObject -LogDir $t5bDir -ClientFailed 1) -Path $t5bSummary
$t5bUpdated = [datetime]::UtcNow.AddDays(-9).ToString('o')
$t5bCard = New-FakeCard -Id '55555555-5555-5555-5555-555555555555' -Identifier 'CARD-0128' -Status 'Done' `
    -TerminalReason '[nightly auto-close] green on 2026-08-20 at abc' -UpdatedAt $t5bUpdated
Reset-Store -Cards @($t5bCard)
$t5b = Invoke-Report @{ Summary = $t5bSummary }
Assert-Eq $t5b.ExitCode 0 'T5b exit 0'
Assert-Eq $t5b.Action 'created' 'T5b action created'
Assert-Eq (Get-CallsMatching -Method POST -Pattern '/reopen$').Count 0 'T5b no reopen'
Assert-Eq (Get-CallsMatching -Method POST -Pattern '/api/boards/.+/cards$').Count 1 'T5b new card POST'

# --- T6 description over 20,000 -> oldest run sections dropped --------------
$t6Dir = New-TestDir
$t6Summary = Join-Path $t6Dir 'summary.json'
Save-Summary -Object (New-RedSummaryObject -LogDir $t6Dir -ClientFailed 1 -ClientFailedTests @('BoardPage.test.tsx > boom')) -Path $t6Summary
$oldSections = @()
for ($i = 0; $i -lt 40; $i++) {
    $n = 40 - $i
    $block = @"
## Run 2026-07-$('{0:D2}' -f (($n % 28) + 1)) 00:30 Europe/London -- RED (TESTS) -- 10m -- origin/master old$n
$(New-Object string 'x', 500)
Logs: C:\Antiphon\nightly\logs\old-$n\
"@
    $oldSections += $block
}
$t6Old = $oldSections -join "`n`n"
Assert-True ($t6Old.Length -gt 20000) 'T6 fixture description is over 20000' ("length=$($t6Old.Length)")
$t6Card = New-FakeCard -Id '66666666-6666-6666-6666-666666666666' -Identifier 'CARD-0129' -Status 'Backlog' -Description $t6Old
Reset-Store -Cards @($t6Card)
$t6 = Invoke-Report @{ Summary = $t6Summary }
Assert-Eq $t6.ExitCode 0 'T6 exit 0'
$t6Patch = Get-CallsMatching -Method PATCH -Pattern '/content$'
Assert-Eq $t6Patch.Count 1 'T6 PATCH content'
$t6Desc = [string](($t6Patch[0].Body | ConvertFrom-Json).description)
Assert-True ($t6Desc.Length -le 20000) 'T6 description at or under 20000' ("length=$($t6Desc.Length)")
Assert-True ($t6Desc.StartsWith('## Run 2026-09-05')) 'T6 newest section intact' $t6Desc.Substring(0, [math]::Min(80, $t6Desc.Length))
Assert-True ($t6Desc -match [regex]::Escape($t6Dir)) 'T6 still names the log dir' $t6Dir

# --- T7 green + none -> zero HTTP writes ------------------------------------
$t7Dir = New-TestDir
$t7Summary = Join-Path $t7Dir 'summary.json'
Save-Summary -Object (New-GreenSummaryObject -LogDir $t7Dir) -Path $t7Summary
Reset-Store -Cards @()
$t7 = Invoke-Report @{ Summary = $t7Summary }
Assert-Eq $t7.ExitCode 0 'T7 exit 0'
Assert-Eq $t7.Action 'none' 'T7 action none'
Assert-Eq (Get-MutatingCalls).Count 0 'T7 zero HTTP writes'

# --- T8 failed-name extraction from real log fixtures -----------------------
$t8Dir = New-TestDir
$t8Summary = Join-Path $t8Dir 'summary.json'
$t8Obj = New-RedSummaryObject -LogDir $t8Dir -ClientLog $vitestLog -ClientFailed 1 -ClientFailedTests @() -AntiphonLog $tunitLog
$t8Obj.selectedSuites = @('antiphon', 'client')
Save-Summary -Object $t8Obj -Path $t8Summary
Reset-Store -Cards @()
$t8 = Invoke-Report @{ Summary = $t8Summary }
Assert-Eq $t8.ExitCode 0 'T8 exit 0'
$t8Create = Get-CallsMatching -Method POST -Pattern '/api/boards/.+/cards$'
Assert-Eq $t8Create.Count 1 'T8 created a card'
$t8Desc = [string](($t8Create[0].Body | ConvertFrom-Json).description)
Assert-True ($t8Desc -match 'Windows_secret_replacement_canonicalizes_case_only_declaration_renames') 'T8 extracts TUnit failed name' $t8Desc
Assert-True ($t8Desc -match '53300: sorry, too many clients already') 'T8 extracts 53300 detail' $t8Desc
Assert-True ($t8Desc -match 'BoardPage.test.tsx') 'T8 extracts vitest file name' $t8Desc
Assert-True ($t8Desc -match 'expected true to be false') 'T8 extracts vitest assertion' $t8Desc

# --- T9 shim throws on every call -> exit 3 and card.md written -------------
$t9Dir = New-TestDir
$t9Summary = Join-Path $t9Dir 'summary.json'
Save-Summary -Object (New-RedSummaryObject -LogDir $t9Dir -ClientFailed 1) -Path $t9Summary
Reset-Store -Cards @()
$script:state.ThrowAll = $true
$t9 = Invoke-Report @{ Summary = $t9Summary }
Assert-Eq $t9.ExitCode 3 'T9 exit 3'
Assert-Eq $t9.Action 'reporting-failed' 'T9 action reporting-failed'
Assert-True (Test-Path -LiteralPath (Join-Path $t9Dir 'card.md')) 'T9 wrote card.md' (Join-Path $t9Dir 'card.md')
$t9Md = Get-Content -LiteralPath (Join-Path $t9Dir 'card.md') -Raw -Encoding UTF8
Assert-True ($t9Md -match 'Nightly red') 'T9 card.md has title' $t9Md
$script:state.ThrowAll = $false

# --- T10 no token is ever sent (headers empty) ------------------------------
$t10Dir = New-TestDir
$t10Summary = Join-Path $t10Dir 'summary.json'
Save-Summary -Object (New-RedSummaryObject -LogDir $t10Dir -ClientFailed 1) -Path $t10Summary
Reset-Store -Cards @()
$oldToken = $env:ANTIPHON_TASK_TOKEN
$env:ANTIPHON_TASK_TOKEN = 'should-not-be-sent-t10'
try {
    $t10 = Invoke-Report @{ Summary = $t10Summary }
    Assert-Eq $t10.ExitCode 0 'T10 exit 0'
    $t10HeaderHits = @($script:state.Calls | Where-Object {
        $h = $_.Headers
        if ($null -eq $h) { return $false }
        foreach ($k in @($h.Keys)) {
            if ([string]$k -match '(?i)token|auth') { return $true }
            if ([string]$h[$k] -match 'should-not-be-sent') { return $true }
        }
        return $false
    })
    Assert-Eq $t10HeaderHits.Count 0 'T10 no token header on any call'
    $empty = @($script:state.Calls | Where-Object {
        $h = $_.Headers
        if ($null -eq $h) { return $false }
        return (@($h.Keys).Count -gt 0)
    })
    Assert-Eq $empty.Count 0 'T10 headers empty on every call'
} finally {
    if ($null -eq $oldToken) { Remove-Item Env:ANTIPHON_TASK_TOKEN -ErrorAction SilentlyContinue }
    else { $env:ANTIPHON_TASK_TOKEN = $oldToken }
}

Write-Host ''
Write-Host ('T1-T10: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ('  ' + $line) }
    Write-Host 'NIGHTLY REPORT TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'NIGHTLY REPORT TESTS EXIT CODE: 0  (PASS)'
exit 0
