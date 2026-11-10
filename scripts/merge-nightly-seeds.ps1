#requires -Version 5.1
<#
.SYNOPSIS
A later green proves an identity only when that seed's source SHA contains the fix commit, or --Proven names that run.
.DESCRIPTION
Merge one or more stopped-run seeds into a single operator-seed-from-stopped-run
envelope and a deterministic report. Chunk rows and evidence come from the newest
seed. failedTests lists identities that are not yet proven. Launch defaults stay
on the existing prior-failure switches. When -Repo is set, a decision SHA already
contained in the last-failing run source (ancestor or cherry-pick trailer) is
ignored for proven and for fixed-pending-proof. With no remaining SHA the identity
stays open. --Proven remains the manual override. Proof requires the later green
source to contain a fix the failing run did not already contain. Without -Repo,
git proof stays off.
A test skipped inside a completed chunk looks green; that is an accepted seed-schema limit.
#>
# Named arguments are parsed from $args. This host's pwsh -File rejects a repeated
# [string[]] parameter, and the operator command repeats -Seed and -Proven.
$mergeArgs = New-Object System.Collections.Generic.List[string]
foreach ($mergeArg in $args) { $mergeArgs.Add([string]$mergeArg) }

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$env:GIT_TERMINAL_PROMPT = '0'

# Resolved in the body: Windows PowerShell 5.1 -File does not bind $PSScriptRoot in a param() default.
$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
. (Join-Path $scriptRoot 'lib/nightly-failure-history.ps1')
. (Join-Path $scriptRoot 'lib/nightly-stopped-run-seed.ps1')

$script:MergeWroteSeed = $false
$script:MergeWroteReport = $false
$script:MergeContainment = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)

function Write-MergeRefusal {
    param([string]$Reason)
    Write-Host $Reason
    exit 2
}

function Get-MergeTicks {
    param($Value)
    $stamp = ConvertTo-NightlySeedUtc $Value
    if ($null -eq $stamp) { return [long]::MinValue }
    return $stamp.UtcDateTime.Ticks
}

function Test-MergeLater {
    param($Candidate, $Baseline)
    if ($Candidate.mergeTicks -gt $Baseline.mergeTicks) { return $true }
    if ($Candidate.mergeTicks -eq $Baseline.mergeTicks -and $Candidate.mergeIndex -gt $Baseline.mergeIndex) { return $true }
    return $false
}

function Test-MergeGreen {
    param($SeedRow, [string]$Suite, [string]$ClassName, [string]$Method)
    $listed = @($SeedRow.chunks | Where-Object { [string]$_.suite -ceq $Suite -and @($_.classes) -ccontains $ClassName })
    if ($listed.Count -eq 0) { return $false }
    foreach ($chunk in $listed) {
        if ($chunk.completed -ne $true) { return $false }
    }
    foreach ($failed in @($SeedRow.failedTests)) {
        if ([string]$failed.suite -ceq $Suite -and [string]$failed.className -ceq $ClassName -and [string]$failed.method -ceq $Method) { return $false }
    }
    return $true
}

function Test-MergeShaResolvable {
    param([string]$Sha)
    & git -C $Repo rev-parse --verify --quiet ($Sha + '^{commit}') 1>$null 2>$null
    return ($LASTEXITCODE -eq 0)
}

function Get-MergeContainment {
    param([string]$SourceSha, [string]$FixSha)
    $key = $SourceSha + '|' + $FixSha
    if ($script:MergeContainment.ContainsKey($key)) { return $script:MergeContainment[$key] }
    & git -C $Repo merge-base --is-ancestor $FixSha $SourceSha 1>$null 2>$null
    $ancestorExit = $LASTEXITCODE
    $hit = $null
    if ($ancestorExit -eq 0) {
        $hit = [pscustomobject]@{ Contained = $true; Commit = $FixSha }
    } elseif ($ancestorExit -ne 1) {
        Write-MergeRefusal 'repo-sha-unresolved'
    } else {
        $pattern = '^\(cherry picked from commit ' + $FixSha + '\)$'
        $found = & git -C $Repo log -1 --format=%H $SourceSha -E --grep=$pattern 2>$null
        if ($LASTEXITCODE -ne 0) { Write-MergeRefusal 'repo-sha-unresolved' }
        # No matching commit leaves an automation null. Casting that to string stays null.
        $text = [string]$found
        if ([string]::IsNullOrWhiteSpace($text)) { $text = '' } else { $text = $text.Trim() }
        if ($text -cmatch '^[a-f0-9]{40}$') {
            $hit = [pscustomobject]@{ Contained = $true; Commit = $text }
        } else {
            $hit = [pscustomobject]@{ Contained = $false; Commit = $null }
        }
    }
    $script:MergeContainment[$key] = $hit
    return $hit
}

function Find-MergeDecisionHit {
    param($SeedRow, $DecisionList)
    if ([string]::IsNullOrWhiteSpace($Repo)) { return $null }
    foreach ($fix in @($DecisionList)) {
        $hit = Get-MergeContainment ([string]$SeedRow.sourceSha) ([string]$fix)
        if ($hit.Contained) { return $hit }
    }
    return $null
}

function Sort-MergeIdentities {
    param($Rows)
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($row in @($Rows)) {
        if ($null -ne $row) { $items.Add($row) }
    }
    for ($i = 0; $i -lt $items.Count; $i++) {
        for ($j = $i + 1; $j -lt $items.Count; $j++) {
            $left = $items[$j]
            $right = $items[$i]
            $cmp = [StringComparer]::Ordinal.Compare([string]$left.suite, [string]$right.suite)
            if ($cmp -eq 0) { $cmp = [StringComparer]::Ordinal.Compare([string]$left.className, [string]$right.className) }
            if ($cmp -eq 0) { $cmp = [StringComparer]::Ordinal.Compare([string]$left.method, [string]$right.method) }
            if ($cmp -lt 0) {
                $swap = $items[$i]
                $items[$i] = $items[$j]
                $items[$j] = $swap
            }
        }
    }
    Write-Output -NoEnumerate $items.ToArray()
}

try {
    $Seed = New-Object System.Collections.Generic.List[string]
    $Proven = New-Object System.Collections.Generic.List[string]
    $Decision = ''
    $Repo = ''
    $OutputPath = ''
    $ReportPath = ''
    $Strict = $false
    for ($argIndex = 0; $argIndex -lt $mergeArgs.Count; $argIndex++) {
        $argName = $mergeArgs[$argIndex]
        if ($argName -ceq '-Strict') { $Strict = $true; continue }
        if ($argIndex + 1 -ge $mergeArgs.Count) { Write-MergeRefusal 'bad-argument' }
        $argValue = $mergeArgs[$argIndex + 1]
        if ($argValue.StartsWith('-')) { Write-MergeRefusal 'bad-argument' }
        if ($argName -ceq '-Seed') { $Seed.Add($argValue) }
        elseif ($argName -ceq '-Proven') { $Proven.Add($argValue) }
        elseif ($argName -ceq '-Decision') {
            if (-not [string]::IsNullOrWhiteSpace($Decision)) { Write-MergeRefusal 'bad-argument' }
            $Decision = $argValue
        } elseif ($argName -ceq '-Repo') {
            if (-not [string]::IsNullOrWhiteSpace($Repo)) { Write-MergeRefusal 'bad-argument' }
            $Repo = $argValue
        } elseif ($argName -ceq '-OutputPath') {
            if (-not [string]::IsNullOrWhiteSpace($OutputPath)) { Write-MergeRefusal 'bad-argument' }
            $OutputPath = $argValue
        } elseif ($argName -ceq '-ReportPath') {
            if (-not [string]::IsNullOrWhiteSpace($ReportPath)) { Write-MergeRefusal 'bad-argument' }
            $ReportPath = $argValue
        } else { Write-MergeRefusal 'bad-argument' }
        $argIndex++
    }
    if ($Seed.Count -lt 1) { Write-MergeRefusal 'seed-required' }
    foreach ($path in $Seed) {
        if ([string]::IsNullOrWhiteSpace($path)) { Write-MergeRefusal 'seed-required' }
    }
    if ([string]::IsNullOrWhiteSpace($OutputPath) -or [string]::IsNullOrWhiteSpace($ReportPath)) { Write-MergeRefusal 'seed-output-exists' }
    if ($OutputPath -ceq $ReportPath) { Write-MergeRefusal 'seed-output-exists' }
    if ([IO.File]::Exists($OutputPath) -or [IO.File]::Exists($ReportPath)) { Write-MergeRefusal 'seed-output-exists' }

    $loaded = New-Object System.Collections.Generic.List[object]
    $index = 0
    foreach ($path in @($Seed)) {
        $doc = Read-NightlyStoppedRunSeed $path
        $doc | Add-Member -NotePropertyName mergeIndex -NotePropertyValue $index -Force
        $doc | Add-Member -NotePropertyName mergeTicks -NotePropertyValue (Get-MergeTicks $doc.sourceObservedAtUtc) -Force
        $loaded.Add($doc)
        $index++
    }

    $runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($doc in $loaded) {
        if (-not $runIds.Add([string]$doc.sourceRunId)) { Write-MergeRefusal 'duplicate-source-run' }
    }

    $newest = $loaded[0]
    foreach ($doc in $loaded) {
        if (Test-MergeLater $doc $newest) { $newest = $doc }
    }

    $decisions = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    if (-not [string]::IsNullOrWhiteSpace($Decision)) {
        if (-not [IO.File]::Exists($Decision)) { Write-MergeRefusal 'decision-missing' }
        $rawDecision = [IO.File]::ReadAllBytes($Decision)
        foreach ($byte in $rawDecision) {
            if ($byte -gt 127) { Write-MergeRefusal 'decision-not-ascii' }
        }
        foreach ($line in [IO.File]::ReadAllLines($Decision)) {
            $trim = $line.Trim()
            if ($trim.Length -eq 0) { continue }
            if ($trim.StartsWith('#')) { continue }
            $tab = $trim.IndexOf([char]9)
            if ($tab -lt 1) { Write-MergeRefusal 'decision-row' }
            $identity = $trim.Substring(0, $tab)
            $shaText = $trim.Substring($tab + 1).Trim()
            $parts = $identity.Split([char]'|')
            if ($parts.Count -ne 3) { Write-MergeRefusal 'decision-row' }
            if ([string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1]) -or [string]::IsNullOrWhiteSpace($parts[2])) { Write-MergeRefusal 'decision-row' }
            $key = $parts[0] + '|' + $parts[1] + '|' + $parts[2]
            $shaList = $null
            if (-not $decisions.TryGetValue($key, [ref]$shaList)) {
                $shaList = New-Object System.Collections.Generic.List[string]
                $decisions[$key] = $shaList
            }
            if ([string]::IsNullOrWhiteSpace($shaText)) { Write-MergeRefusal 'decision-sha' }
            foreach ($piece in $shaText.Split([char]',')) {
                $one = $piece.Trim()
                if ($one -cnotmatch '^[a-f0-9]{40}$') { Write-MergeRefusal 'decision-sha' }
                if (-not $shaList.Contains($one)) { $shaList.Add($one) }
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($Repo)) {
        & git -C $Repo rev-parse --is-inside-work-tree 1>$null 2>$null
        if ($LASTEXITCODE -ne 0) { Write-MergeRefusal 'repo-sha-unresolved' }
        $needed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($doc in $loaded) { [void]$needed.Add([string]$doc.sourceSha) }
        foreach ($pair in $decisions.GetEnumerator()) {
            foreach ($sha in @($pair.Value)) { [void]$needed.Add([string]$sha) }
        }
        foreach ($sha in $needed) {
            if (-not (Test-MergeShaResolvable $sha)) { Write-MergeRefusal 'repo-sha-unresolved' }
        }
    }

    $identities = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($doc in $loaded) {
        foreach ($failed in @($doc.failedTests)) {
            $key = [string]$failed.suite + '|' + [string]$failed.className + '|' + [string]$failed.method
            $record = $null
            if (-not $identities.TryGetValue($key, [ref]$record)) {
                $record = [pscustomobject]@{
                    Suite = [string]$failed.suite
                    ClassName = [string]$failed.className
                    Method = [string]$failed.method
                    FirstIndex = [int]$doc.mergeIndex
                    LastIndex = [int]$doc.mergeIndex
                    Outcome = [string]$failed.outcome
                }
                $identities[$key] = $record
            } else {
                $first = $loaded[$record.FirstIndex]
                $last = $loaded[$record.LastIndex]
                if ($doc.mergeTicks -lt $first.mergeTicks -or ($doc.mergeTicks -eq $first.mergeTicks -and $doc.mergeIndex -lt $first.mergeIndex)) {
                    $record.FirstIndex = [int]$doc.mergeIndex
                }
                if (Test-MergeLater $doc $last) {
                    $record.LastIndex = [int]$doc.mergeIndex
                    $record.Outcome = [string]$failed.outcome
                }
            }
        }
    }

    $provenRows = New-Object System.Collections.Generic.List[object]
    foreach ($item in @($Proven)) {
        if ([string]::IsNullOrWhiteSpace($item)) { Write-MergeRefusal 'proven-run-unknown' }
        $colon = $item.LastIndexOf(':')
        if ($colon -le 0 -or $colon -ge ($item.Length - 1)) { Write-MergeRefusal 'proven-run-unknown' }
        $identity = $item.Substring(0, $colon)
        $runId = $item.Substring($colon + 1)
        $parts = $identity.Split([char]'|')
        if ($parts.Count -ne 3) { Write-MergeRefusal 'proven-run-unknown' }
        if ([string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1]) -or [string]::IsNullOrWhiteSpace($parts[2]) -or [string]::IsNullOrWhiteSpace($runId)) { Write-MergeRefusal 'proven-run-unknown' }
        $provenRows.Add([pscustomobject]@{ Key = ($parts[0] + '|' + $parts[1] + '|' + $parts[2]); RunId = $runId; Suite = $parts[0]; ClassName = $parts[1]; Method = $parts[2] })
    }

    foreach ($row in $provenRows) {
        $record = $null
        if (-not $identities.TryGetValue($row.Key, [ref]$record)) { Write-MergeRefusal 'proven-run-unknown' }
        $named = $null
        foreach ($doc in $loaded) {
            if ([string]$doc.sourceRunId -ceq $row.RunId) { $named = $doc; break }
        }
        if ($null -eq $named) { Write-MergeRefusal 'proven-run-unknown' }
        $last = $loaded[$record.LastIndex]
        if (-not (Test-MergeLater $named $last)) { Write-MergeRefusal 'proven-run-unknown' }
        if (-not (Test-MergeGreen $named $row.Suite $row.ClassName $row.Method)) { Write-MergeRefusal 'proven-not-green' }
    }

    $reportRows = New-Object System.Collections.Generic.List[object]
    $failedRows = New-Object System.Collections.Generic.List[object]
    foreach ($pair in $identities.GetEnumerator()) {
        $record = $pair.Value
        $key = $pair.Key
        $last = $loaded[$record.LastIndex]
        $later = New-Object System.Collections.Generic.List[object]
        foreach ($doc in $loaded) {
            if (Test-MergeLater $doc $last) { $later.Add($doc) }
        }
        for ($i = 0; $i -lt $later.Count; $i++) {
            for ($j = $i + 1; $j -lt $later.Count; $j++) {
                $swapNeeded = $false
                if ($later[$j].mergeTicks -lt $later[$i].mergeTicks) { $swapNeeded = $true }
                elseif ($later[$j].mergeTicks -eq $later[$i].mergeTicks -and $later[$j].mergeIndex -lt $later[$i].mergeIndex) { $swapNeeded = $true }
                if ($swapNeeded) {
                    $swap = $later[$i]
                    $later[$i] = $later[$j]
                    $later[$j] = $swap
                }
            }
        }
        $decisionList = @()
        $stored = $null
        if ($decisions.TryGetValue($key, [ref]$stored)) { $decisionList = @($stored) }
        $usableDecisions = New-Object System.Collections.Generic.List[string]
        $fixPresentAtFailure = $false
        foreach ($fix in @($decisionList)) {
            $fixText = [string]$fix
            if ([string]::IsNullOrWhiteSpace($fixText)) { continue }
            if (-not [string]::IsNullOrWhiteSpace($Repo)) {
                $already = Get-MergeContainment ([string]$last.sourceSha) $fixText
                if ($already.Contained) {
                    $fixPresentAtFailure = $true
                    continue
                }
            }
            $usableDecisions.Add($fixText)
        }

        $proving = $null
        $containing = $null
        foreach ($doc in $later) {
            $green = Test-MergeGreen $doc $record.Suite $record.ClassName $record.Method
            $hit = Find-MergeDecisionHit $doc $usableDecisions
            $named = $false
            foreach ($row in $provenRows) {
                if ($row.Key -ceq $key -and $row.RunId -ceq [string]$doc.sourceRunId) { $named = $true; break }
            }
            if ($green -and ($null -ne $hit -or $named)) {
                $proving = $doc
                if ($null -ne $hit) { $containing = [string]$hit.Commit }
                break
            }
        }

        $status = 'open'
        $provingRun = $null
        $provingSha = $null
        if ($null -ne $proving) {
            $status = 'proven'
            $provingRun = [string]$proving.sourceRunId
            $provingSha = [string]$proving.sourceSha
        } else {
            $pending = $null
            foreach ($doc in $later) {
                $hit = Find-MergeDecisionHit $doc $usableDecisions
                if ($null -ne $hit) { $pending = $hit; break }
            }
            if ($null -ne $pending) {
                $status = 'fixed-pending-proof'
                $containing = [string]$pending.Commit
            }
        }

        $placed = $false
        $chunkName = $null
        if ($status -cne 'proven') {
            foreach ($failed in @($newest.failedTests)) {
                if ([string]$failed.suite -ceq $record.Suite -and [string]$failed.className -ceq $record.ClassName -and [string]$failed.method -ceq $record.Method) {
                    $chunkName = [string]$failed.chunk
                    break
                }
            }
            if ($null -eq $chunkName) {
                foreach ($chunk in @($newest.chunks)) {
                    if ([string]$chunk.suite -ceq $record.Suite -and @($chunk.classes) -ccontains $record.ClassName) {
                        $chunkName = [string]$chunk.chunk
                        break
                    }
                }
            }
            if ($null -ne $chunkName) { $placed = $true }
        }

        if ($placed) {
            $failedRows.Add([ordered]@{
                suite = $record.Suite
                chunk = $chunkName
                className = $record.ClassName
                method = $record.Method
                outcome = $record.Outcome
            })
        }

        $decisionValue = New-Object System.Collections.Generic.List[string]
        foreach ($sha in @($decisionList)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$sha)) { $decisionValue.Add([string]$sha) }
        }
        $reportRows.Add([ordered]@{
            suite = $record.Suite
            className = $record.ClassName
            method = $record.Method
            firstFailedRun = [string]$loaded[$record.FirstIndex].sourceRunId
            lastFailedRun = [string]$loaded[$record.LastIndex].sourceRunId
            status = $status
            placed = [bool]$placed
            outcome = $record.Outcome
            provingRun = $provingRun
            provingSha = $provingSha
            decisionShas = $decisionValue.ToArray()
            containingCommit = $containing
            fixPresentAtFailure = [bool]$fixPresentAtFailure
        })
    }

    $sortedFailed = Sort-MergeIdentities $failedRows.ToArray()
    $sortedReport = Sort-MergeIdentities $reportRows.ToArray()
    if ($null -eq $sortedFailed) { $sortedFailed = New-Object object[] 0 }
    if ($null -eq $sortedReport) { $sortedReport = New-Object object[] 0 }
    $unplacedCount = 0
    $strictBreach = $false
    foreach ($row in @($sortedReport)) {
        $shaCount = @($row.decisionShas).Count
        if (-not $row.placed -and [string]$row.status -cne 'proven') { $unplacedCount++ }
        if ($shaCount -eq 0 -and [string]$row.status -cne 'proven') { $strictBreach = $true }
    }
    if ($unplacedCount -gt 0) { $strictBreach = $true }

    $chunkRows = New-Object System.Collections.Generic.List[object]
    foreach ($chunk in @($newest.chunks)) { $chunkRows.Add($chunk) }
    $evidenceRows = @()
    if ($null -ne $newest.evidence) { $evidenceRows = @($newest.evidence) }
    $created = (ConvertTo-NightlySeedUtc $newest.createdAtUtc).ToString('o')
    $observed = (ConvertTo-NightlySeedUtc $newest.sourceObservedAtUtc).ToString('o')
    $payload = [ordered]@{
        sourceObservedAtUtc = $observed
        chunks = $chunkRows.ToArray()
        failedTests = $sortedFailed
        evidence = $evidenceRows
    }
    $envelope = [ordered]@{
        format = 'antiphon-nightly-stopped-seed-v1'
        seedKind = 'operator-seed-from-stopped-run'
        sourceRunId = [string]$newest.sourceRunId
        sourceCandidateId = [string]$newest.sourceCandidateId
        sourceSha = [string]$newest.sourceSha
        createdAtUtc = $created
        payload = ($payload | ConvertTo-Json -Depth 30 -Compress)
        contentHash = ''
    }
    $envelope.contentHash = Get-NightlyStoppedSeedHash $envelope
    $report = [ordered]@{
        format = 'antiphon-cumulative-failure-report-v1'
        newestRun = [string]$newest.sourceRunId
        unplacedCount = $unplacedCount
        identities = $sortedReport
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $seedParent = [IO.Path]::GetDirectoryName($OutputPath)
    $reportParent = [IO.Path]::GetDirectoryName($ReportPath)
    if (-not [string]::IsNullOrWhiteSpace($seedParent)) { [void][IO.Directory]::CreateDirectory($seedParent) }
    if (-not [string]::IsNullOrWhiteSpace($reportParent)) { [void][IO.Directory]::CreateDirectory($reportParent) }
    if ([IO.File]::Exists($OutputPath) -or [IO.File]::Exists($ReportPath)) { Write-MergeRefusal 'seed-output-exists' }
    [IO.File]::WriteAllText($OutputPath, ($envelope | ConvertTo-Json -Depth 35), $utf8)
    $script:MergeWroteSeed = $true
    [IO.File]::WriteAllText($ReportPath, ($report | ConvertTo-Json -Depth 30), $utf8)
    $script:MergeWroteReport = $true
    $written = Read-NightlyStoppedRunSeed $OutputPath
    if ([string]$written.sourceRunId -cne [string]$newest.sourceRunId) { throw 'merged-seed-unreadable' }
    Write-Host 'A test skipped inside a completed chunk looks green; that is an accepted seed-schema limit.'
    Write-Host ('digest=' + (Get-NightlyHistoryDigest $OutputPath))
    if ($Strict -and $strictBreach) { exit 3 }
    exit 0
} catch {
    if ($script:MergeWroteSeed -and [IO.File]::Exists($OutputPath)) { [IO.File]::Delete($OutputPath) }
    if ($script:MergeWroteReport -and [IO.File]::Exists($ReportPath)) { [IO.File]::Delete($ReportPath) }
    Write-Host $_.Exception.Message
    exit 2
}
