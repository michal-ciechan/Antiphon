#requires -Version 7.0
# CARD-1836 S6a. Shadow launch order only. No scheduler, publish, or trust transition.
# Full seed ordering stays the existing function. This file does not call it.
# Containment is the CARD-1823 rule: ancestor, otherwise one exact cherry-pick trailer line.

function Get-RcDirtyProp {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

function Test-RcDirtyOrderContained {
    param([string]$RepositoryRoot, [string]$SourceSha, [string]$FixSha, $Cache)
    if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { return $false }
    if ($SourceSha -cnotmatch '^[0-9a-f]{40}$' -or $FixSha -cnotmatch '^[0-9a-f]{40}$') { throw 'repo-sha-unresolved' }
    $cacheKey = $SourceSha + '|' + $FixSha
    if ($Cache.ContainsKey($cacheKey)) { return [bool]$Cache[$cacheKey] }
    $previous = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
    $env:GIT_TERMINAL_PROMPT = '0'
    try {
        & git -C $RepositoryRoot merge-base --is-ancestor $FixSha $SourceSha 1>$null 2>$null
        $ancestorExit = $LASTEXITCODE
        $contained = $false
        if ($ancestorExit -eq 0) {
            $contained = $true
        } elseif ($ancestorExit -ne 1) {
            throw 'repo-sha-unresolved'
        } else {
            $pattern = '^\(cherry picked from commit ' + $FixSha + '\)$'
            $found = & git -C $RepositoryRoot log -1 --format=%H $SourceSha -E --grep=$pattern 2>$null
            if ($LASTEXITCODE -ne 0) { throw 'repo-sha-unresolved' }
            $text = ''
            if ($null -ne $found) {
                $first = @($found)[0]
                if ($null -ne $first) { $text = ([string]$first).Trim() }
            }
            if ($text -cmatch '^[0-9a-f]{40}$') { $contained = $true }
        }
        $Cache[$cacheKey] = $contained
        return $contained
    } finally {
        $PSNativeCommandUseErrorActionPreference = $previous
    }
}

function Test-RcDirtyCanDischarge {
    param([string]$RepositoryRoot, [string]$FailureSha, [string]$GreenSha, $FixList, $Cache)
    if ($GreenSha -ceq $FailureSha) { return $false }
    foreach ($fix in @($FixList)) {
        $fixSha = [string]$fix
        if ([string]::IsNullOrWhiteSpace($fixSha)) { continue }
        if (Test-RcDirtyOrderContained $RepositoryRoot $FailureSha $fixSha $Cache) { continue }
        if (Test-RcDirtyOrderContained $RepositoryRoot $GreenSha $fixSha $Cache) { return $true }
    }
    return $false
}

function Get-RcDirtyLaunchOrder {
    param(
        [AllowEmptyCollection()][AllowNull()][object[]]$Obligations = @(),
        [AllowEmptyCollection()][AllowNull()][object[]]$Attempts = @(),
        [AllowEmptyCollection()][AllowNull()][object[]]$Fixes = @(),
        [string]$RepositoryRoot = '',
        [string]$CandidateSha = '')
    if ($null -eq $Obligations) { $Obligations = @() }
    if ($null -eq $Attempts) { $Attempts = @() }
    if ($null -eq $Fixes) { $Fixes = @() }
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in @('failed', 'unexpected-skip', 'interrupted', 'not-run', 'unproven', 'passed', 'expected-skip')) {
        [void]$allowed.Add($name)
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $states = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $omitted = New-Object System.Collections.Generic.List[string]
    foreach ($row in @($Obligations)) {
        $key = [string](Get-RcDirtyProp $row 'key')
        if ([string]::IsNullOrWhiteSpace($key)) { throw 'invalid-obligation' }
        $disposition = [string](Get-RcDirtyProp $row 'disposition')
        if (($disposition -cne 'Dirty') -and ($disposition -cne 'Sample') -and ($disposition -cne 'Carry')) { throw 'invalid-disposition' }
        if (-not $seen.Add($key)) { throw 'duplicate-obligation' }
        $states.Add($key, @{
            key = $key
            disposition = $disposition
            failed = $false
            failureSha = ''
            interrupted = $false
            interruptSha = ''
            sawGreen = $false
            fixes = New-Object System.Collections.Generic.List[string]
        })
    }
    foreach ($fix in @($Fixes)) {
        if ($null -eq $fix) { continue }
        $fixKey = [string](Get-RcDirtyProp $fix 'key')
        if (-not $states.ContainsKey($fixKey)) { continue }
        [void]$states[$fixKey].fixes.Add([string](Get-RcDirtyProp $fix 'fixSha'))
    }
    $ordered = New-Object System.Collections.Generic.List[object]
    foreach ($attempt in @($Attempts)) {
        if ($null -eq $attempt) { continue }
        $sequence = Get-RcDirtyProp $attempt 'sequence'
        if ($null -eq $sequence) { throw 'invalid-attempt' }
        $ordered.Add([pscustomobject]@{
            sequence = [int64]$sequence
            sourceSha = [string](Get-RcDirtyProp $attempt 'sourceSha')
            attempt = $attempt
        })
    }
    for ($i = 0; $i -lt $ordered.Count; $i++) {
        for ($j = $i + 1; $j -lt $ordered.Count; $j++) {
            $swap = $false
            if ($ordered[$j].sequence -lt $ordered[$i].sequence) { $swap = $true }
            elseif (($ordered[$j].sequence -eq $ordered[$i].sequence) -and ([StringComparer]::Ordinal.Compare([string]$ordered[$j].sourceSha, [string]$ordered[$i].sourceSha) -lt 0)) { $swap = $true }
            if ($swap) {
                $tmp = $ordered[$i]
                $ordered[$i] = $ordered[$j]
                $ordered[$j] = $tmp
            }
        }
    }
    $cache = @{}
    foreach ($item in $ordered) {
        $sha = [string]$item.sourceSha
        foreach ($result in @(Get-RcDirtyProp $item.attempt 'results')) {
            if ($null -eq $result) { continue }
            $outcome = [string](Get-RcDirtyProp $result 'outcome')
            if (-not $allowed.Contains($outcome)) { throw 'invalid-outcome' }
            $resultKey = [string](Get-RcDirtyProp $result 'key')
            if (-not $states.ContainsKey($resultKey)) { continue }
            $state = $states[$resultKey]
            if (($outcome -ceq 'failed') -or ($outcome -ceq 'unexpected-skip')) {
                $state.failed = $true
                $state.failureSha = $sha
                $state.interrupted = $false
                $state.interruptSha = ''
            } elseif ($outcome -ceq 'interrupted') {
                if (-not $state.failed) {
                    $state.interrupted = $true
                    $state.interruptSha = $sha
                }
            } elseif (($outcome -ceq 'passed') -or ($outcome -ceq 'expected-skip')) {
                $state.sawGreen = $true
                if ($state.failed -and (Test-RcDirtyCanDischarge $RepositoryRoot ([string]$state.failureSha) $sha $state.fixes $cache)) {
                    $state.failed = $false
                    $state.failureSha = ''
                }
                if ($state.interrupted -and ($sha -cne [string]$state.interruptSha)) {
                    $state.interrupted = $false
                    $state.interruptSha = ''
                }
            }
        }
    }
    $ranked = New-Object System.Collections.Generic.List[object]
    foreach ($key in @($states.Keys)) {
        $state = $states[$key]
        if ($state.disposition -ceq 'Carry') {
            if ($state.failed -or $state.interrupted) { throw 'carry-with-open-failure' }
            $omitted.Add([string]$state.key)
            continue
        }
        if ($state.failed) { $band = 'failed'; $rank = 0 }
        elseif ($state.interrupted) { $band = 'interrupted'; $rank = 1 }
        elseif (-not $state.sawGreen) { $band = 'not-run'; $rank = 2 }
        elseif ($state.disposition -ceq 'Dirty') { $band = 'changed'; $rank = 3 }
        else { $band = 'sample'; $rank = 4 }
        $ranked.Add([pscustomobject]@{ key = [string]$state.key; band = $band; disposition = [string]$state.disposition; rank = $rank })
    }
    for ($i = 0; $i -lt $ranked.Count; $i++) {
        for ($j = $i + 1; $j -lt $ranked.Count; $j++) {
            $swap = $false
            if ($ranked[$j].rank -lt $ranked[$i].rank) { $swap = $true }
            elseif (($ranked[$j].rank -eq $ranked[$i].rank) -and ([StringComparer]::Ordinal.Compare([string]$ranked[$j].key, [string]$ranked[$i].key) -lt 0)) { $swap = $true }
            if ($swap) {
                $tmp = $ranked[$i]
                $ranked[$i] = $ranked[$j]
                $ranked[$j] = $tmp
            }
        }
    }
    $launch = New-Object System.Collections.Generic.List[object]
    foreach ($row in $ranked) {
        $launch.Add([pscustomobject]@{ key = [string]$row.key; band = [string]$row.band; disposition = [string]$row.disposition })
    }
    $omittedSorted = $omitted.ToArray()
    [Array]::Sort($omittedSorted, [StringComparer]::Ordinal)
    return [pscustomobject]@{
        format = 'antiphon-rc-dirty-order-v1'
        launch = $launch.ToArray()
        omitted = $omittedSorted
        gateCredit = $false
        effectiveMode = 'Full'
        trustTransition = $false
        shadow = $true
        activation = 'off'
        candidateSha = [string]$CandidateSha
    }
}
