# CARD-0097 S-E. Preview and snapshot stage pins into role/complexity cells.
# Default and -DryRun issue GETs; -Apply writes Human cells, retaining the pins.
# Review the read-back before using routing-pin.ps1 clear as a separate command.
# Rollback: recreate Required stage pins, read back, then -Restore using the FIRST
# snapshot. Later apply snapshots contain migrated values, not the original cells.
# ASCII-only source for Windows PowerShell 5.1; operator text comes from the API
# and snapshot files as UTF-8, including multiline reasons.
[CmdletBinding()]
param(
    [ValidateSet('Investigate', 'Plan', 'TestDesign', 'Code', 'Mutation', 'Review', 'Debug', 'Coverage', 'Docs', 'Commit', 'Test', 'Deploy', 'Merge', 'Custom')]
    [string]$Role,
    [switch]$DryRun,
    [switch]$Apply,
    [switch]$Restore,
    [string]$Snapshot,
    [string]$OutDir = '.antiphon/migration-snapshots'
)

$ErrorActionPreference = 'Stop'
$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')
$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}
$tiers = @('Hard', 'Medium', 'Easy')
$roles = @('Investigate', 'Plan', 'TestDesign', 'Code', 'Mutation', 'Review', 'Debug', 'Coverage', 'Docs', 'Commit', 'Test', 'Deploy', 'Merge', 'Custom')

function Invoke-MigrationApi {
    param([string]$Method, [string]$Path, $Body)
    try {
        $parameters = @{ Method = $Method; Uri = "$api$Path"; Headers = $headers; TimeoutSec = 30 }
        if ($null -ne $Body) {
            $parameters.Body = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth 20 -Compress))
            $parameters.ContentType = 'application/json; charset=utf-8'
        }
        return Invoke-RestMethod @parameters
    }
    catch {
        $problem = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($problem)) { $problem = $_.Exception.Message }
        $status = ''
        if ($null -ne $_.Exception.Response) { $status = [string][int]$_.Exception.Response.StatusCode }
        throw "$Method $Path $status $problem"
    }
}

function Get-Pairs {
    param($Candidates)
    return (@($Candidates | ForEach-Object { '{0}/{1}' -f $_.agentKind, $_.modelLevel }) -join ',')
}

function Get-WriteCandidates {
    param($Candidates)
    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate.agentKind) -or [string]::IsNullOrWhiteSpace($candidate.modelLevel)) {
            throw 'A cell candidate requires both agentKind and modelLevel; use the pin editor for partial candidates.'
        }
        [ordered]@{ agentKind = $candidate.agentKind; modelLevel = $candidate.modelLevel }
    }
}

function Test-Eligible {
    param($Pin)
    if ($null -ne $Pin.cardId -or $null -ne $Pin.agentId -or $null -ne $Pin.notBefore -or
        $null -ne $Pin.notAfter -or @($Pin.forbiddenAliases).Count -gt 0 -or @($Pin.candidates).Count -eq 0 -or
        $Pin.role -notin $roles) { return $false }
    foreach ($candidate in $Pin.candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate.agentKind) -or [string]::IsNullOrWhiteSpace($candidate.modelLevel)) { return $false }
    }
    return $true
}

function Test-Active {
    param($Pin)
    $now = [DateTimeOffset]::UtcNow
    if ($null -ne $Pin.notBefore -and [DateTimeOffset]$Pin.notBefore -gt $now) { return $false }
    if ($null -ne $Pin.notAfter -and [DateTimeOffset]$Pin.notAfter -le $now) { return $false }
    return $true
}

function Write-HeldWarnings {
    param($Pin, $Holds)
    foreach ($candidate in $Pin.candidates) {
        foreach ($hold in $Holds) {
            if ($hold.kind -eq $candidate.agentKind -and ($hold.modelAlias -eq '*' -or $hold.modelAlias -eq $candidate.alias) -and
                ($null -eq $hold.disabledUntil -or [DateTimeOffset]$hold.disabledUntil -gt [DateTimeOffset]::UtcNow)) {
                Write-Warning ('HELD {0} {1}/{2} ({3}): {4}' -f $Pin.role, $candidate.agentKind, $candidate.modelLevel, $candidate.alias, $hold.reason)
            }
        }
    }
}

function Invoke-CellChanges {
    param($Changes, [string]$CompletedLabel, [string]$RemainingLabel)
    $completed = @()
    for ($i = 0; $i -lt $Changes.Count; $i++) {
        $change = $Changes[$i]
        $key = '{0}/{1}' -f $change.role, $change.complexity
        try {
            Invoke-MigrationApi $change.method "/api/complexity-chains/$key" $change.body | Out-Null
        }
        catch {
            $remaining = @($Changes | Select-Object -Skip ($i + 1) | ForEach-Object { '{0}/{1}' -f $_.role, $_.complexity })
            Write-Output ('{0}: {1}; failed: {2} {3}; {4}: {5}' -f $CompletedLabel, ($completed -join ', '), $key, $_.Exception.Message, $RemainingLabel, ($remaining -join ', '))
            throw
        }
        $completed += $key
        if ($change.note) { Write-Output ("{0}: {1}" -f $key, $change.note) }
    }
    Write-Output ('{0}: {1}' -f $CompletedLabel, ($completed -join ', '))
}

try {
    if (($Apply -and $Restore) -or ($DryRun -and ($Apply -or $Restore))) {
        throw '-DryRun, -Apply and -Restore are mutually exclusive.'
    }
    if ($Restore -and [string]::IsNullOrWhiteSpace($Snapshot)) { throw '-Restore requires -Snapshot <first snapshot>.' }
    if (-not $Restore -and -not [string]::IsNullOrWhiteSpace($Snapshot)) { throw '-Snapshot requires -Restore.' }

    if ($Restore) {
        $saved = [IO.File]::ReadAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Snapshot), [Text.Encoding]::UTF8) | ConvertFrom-Json
        if ([string]$saved.apiBase -cne $api) { throw 'restore refused: snapshot apiBase differs from the API base in use.' }
        if ($null -eq $saved.cellsBefore -or $null -eq $saved.plan -or $null -eq $saved.pins) { throw 'restore refused: missing snapshot pins, plan or cellsBefore.' }
        $before = @($saved.cellsBefore | Where-Object { -not $Role -or $_.role -eq $Role })
        $migratedRoles = @($before | ForEach-Object { $_.role } | Select-Object -Unique)
        # Complete validation and pin reads before constructing the first write.
        foreach ($cell in $before) {
            if ($cell.role -notin $roles -or $cell.complexity -notin $tiers -or $cell.present -isnot [bool]) {
                throw 'restore refused: invalid cell before-image.'
            }
            $planned = @($saved.plan | Where-Object { $_.role -eq $cell.role -and $_.complexity -eq $cell.complexity })
            if ($planned.Count -ne 1) { throw 'restore refused: before-image has no unique migration plan entry.' }
        }
        foreach ($migratedRole in $migratedRoles) {
            $original = @($saved.pins | Where-Object { $_.role -eq $migratedRole -and (Test-Eligible $_) })
            if ($original.Count -ne 1) { throw "restore refused: no unique migrated pin for $migratedRole in the snapshot." }
            $live = Invoke-MigrationApi GET "/api/routing-pins?role=$migratedRole"
            $required = @($live.pins | Where-Object {
                $_.role -eq $migratedRole -and $null -eq $_.cardId -and $_.strength -eq 'Required' -and
                $_.candidateCount -ge 1 -and @($_.candidates).Count -ge 1 -and (Test-Active $_)
            })
            if ($required.Count -eq 0) {
                $pairs = Get-Pairs $original[0].candidates
                throw "restore refused: $migratedRole has no Required stage pin; run routing-pin.ps1 set -Role $migratedRole -Provenance Human -Strength Required -Candidates $pairs -Reason <text> first"
            }
        }
        $changes = @()
        foreach ($cell in $before) {
            $method = 'DELETE'
            $body = $null
            $note = $null
            if ($cell.present) {
                if ($cell.provenance -eq 'Auto') { $note = 'Auto before-image not re-created; cleared' }
                elseif ($cell.provenance -eq 'Human') {
                    if ($null -ne $cell.notAfter -and [DateTimeOffset]$cell.notAfter -le [DateTimeOffset]::UtcNow) {
                        $note = 'expired before restore; cleared'
                    }
                    else {
                        $method = 'PUT'
                        $body = [ordered]@{ candidates = @(Get-WriteCandidates $cell.candidates); provenance = 'Human'; reason = $cell.reason; notAfter = $cell.notAfter }
                    }
                }
                else { throw 'restore refused: unknown before-image provenance.' }
            }
            $changes += [pscustomobject]@{ role = $cell.role; complexity = $cell.complexity; method = $method; body = $body; note = $note }
        }
        Invoke-CellChanges $changes 'restored' 'not restored'
        exit 0
    }

    $pinList = Invoke-MigrationApi GET '/api/routing-pins'
    $availability = Invoke-MigrationApi GET '/api/model-availability'
    if ($null -eq $pinList.pins -or $null -eq $availability.holds) { throw 'Missing pins or holds in API response.' }
    $stagePins = @($pinList.pins | Where-Object { $null -eq $_.cardId -and (-not $Role -or $_.role -eq $Role) })
    $before = @()
    $plan = @()
    $seenRoles = @()
    Write-Output 'role | pin id | pin candidates | Hard/Medium/Easy before | to write'
    foreach ($pin in $stagePins) {
        if (-not (Test-Eligible $pin)) { Write-Output ("{0} | manual | {1}" -f $pin.role, $pin.id); continue }
        if ($pin.role -in $seenRoles) { throw "Multiple eligible stage pins for $($pin.role); inspect the live pins." }
        $seenRoles += $pin.role
        $list = Invoke-MigrationApi GET ("/api/complexity-chains?role={0}" -f $pin.role)
        $pairs = Get-Pairs $pin.candidates
        $summaries = @()
        $effectiveBefore = @()
        foreach ($tier in $tiers) {
            $matches = @($list.chains | Where-Object { $_.complexity -eq $tier })
            if ($matches.Count -ne 1 -or $matches[0].resolvedFrom -notin @('role', 'any', 'config', 'none')) {
                throw "Missing or ambiguous cell read for $($pin.role)/$tier."
            }
            $cell = $matches[0]
            $present = $cell.resolvedFrom -eq 'role'
            $prior = [ordered]@{ role = $pin.role; complexity = $tier; present = $present; candidates = @(); provenance = $null; reason = $null; notAfter = $null }
            $summary = "$tier absent (inherits $($cell.resolvedFrom))"
            if ($present) {
                $prior.candidates = @(Get-WriteCandidates $cell.candidates)
                $prior.provenance = $cell.provenance
                $prior.reason = $cell.reason
                $prior.notAfter = $cell.notAfter
                $summary = "$tier $($cell.provenance)"
                if ($cell.provenance -eq 'Auto') { $summary += ' (restore clears)' }
                $summary += ' ' + (Get-Pairs $cell.candidates)
            }
            $summaries += $summary
            $before += $prior
            $body = [ordered]@{
                candidates = @(Get-WriteCandidates $pin.candidates)
                provenance = 'Human'
                reason = '{0} (migrated from stage pin {1})' -f $pin.reason, $pin.id
            }
            $plan += [ordered]@{ role = $pin.role; complexity = $tier; body = $body }
            if ($pin.strength -eq 'Required') { $effectiveBefore += "$tier effective BEFORE: Required pin $pairs" }
            elseif ($null -ne $cell.effective) {
                $effectiveBefore += "$tier effective BEFORE: $($cell.effective.source) $(Get-Pairs $cell.effective.candidates)"
            }
            else { $effectiveBefore += "$tier effective BEFORE: Preferred pin $pairs; fallback $($cell.resolvedFrom) $(Get-Pairs $cell.candidates) (read server effective after activation)" }
        }
        Write-Output ('{0} | {1} | {2} | {3} | Human {2}' -f $pin.role, $pin.id, $pairs, ($summaries -join '; '))
        foreach ($line in $effectiveBefore) { Write-Output "  $line" }
        Write-Output ("  effective AFTER apply: {0} pin {1}; after separate pin clear: role {1}" -f $pin.strength, $pairs)
        Write-HeldWarnings $pin $availability.holds
    }

    $directory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $file = Join-Path $directory ('migrate-stage-pins-{0}-{1}.json' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'), [Guid]::NewGuid().ToString('N'))
    $snapshotBody = [ordered]@{ apiBase = $api; createdAt = [DateTimeOffset]::UtcNow.ToString('o'); pins = $stagePins; cellsBefore = $before; plan = $plan }
    $json = ConvertTo-Json -InputObject $snapshotBody -Depth 30
    $stream = [IO.File]::Open($file, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }
    Write-Output "Snapshot folder: $directory"
    Write-Output "Snapshot: $file"
    Write-Output 'Keep the FIRST snapshot for rollback; a repeat apply snapshots the migrated cells.'
    if ($Apply) {
        $changes = @($plan | ForEach-Object { [pscustomobject]@{ role = $_.role; complexity = $_.complexity; method = 'PUT'; body = $_.body } })
        Invoke-CellChanges $changes 'written' 'not written'
    }
    else { Write-Output 'Dry run: zero writes. Use -Apply to write the previewed cells from a fresh read.' }
    foreach ($migratedRole in $seenRoles) {
        Write-Output "Read back with complexity-chain.ps1 get -Role $migratedRole and review the effective diff BEFORE clearing the pin."
        Write-Output "Separate operator command after review: routing-pin.ps1 clear -Role $migratedRole"
    }
    exit 0
}
catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
}
