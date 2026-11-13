#requires -Version 7.0
# CARD-1836 S8a: outcome witnesses, no qualification launch or external services.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
. (Join-Path $PSScriptRoot 'lib/rc-forced-full.ps1')
Set-StrictMode -Version Latest
$script:Errors = [Collections.Generic.List[string]]::new()
function Note([bool]$Ok, [string]$Why) { if (-not $Ok) { $script:Errors.Add($Why) } }
$script:Now = [DateTimeOffset]'2026-11-13T12:00:00Z'
function New-Contract {
    $contract = [ordered]@{
        format = 'antiphon-rc-input-contract-v1'; inputMapVersion = '1'
        sdkVersion = '10.0.204'; runtimeVersion = '9.0.10'
        platform = 'linux'; executionImplementation = 'compiled'
    }
    foreach ($field in @('policyHash','catalogHash','selectorHash','skipResolverHash','discoveryHash','parserHash',
        'coverageHash','launcherHash','custodyHash','buildHash','harnessHash','inputMapHash','sdkHash','runtimeHash')) {
        $contract[$field] = 'a' * 64
    }
    return $contract
}
function New-Evidence {
    @{ baselineState = 'admitted-ancestor'; admissionChainState = 'verified'; historyState = 'complete'
        globalOwnershipState = 'resolved'; shadowState = 'clean' }
}
function New-Anchor([string]$When = '2026-11-12T12:00:00Z') {
    @{ kind = 'executed-full'; admissionState = 'verified'; completedAtUtc = $When
        sourceSha = '1' * 40; closureDigest = 'a' * 64 }
}
function New-Args {
    @{ CurrentContract = (New-Contract); BaselineContract = (New-Contract); Evidence = (New-Evidence)
        FullAnchor = (New-Anchor); UtcNow = $script:Now }
}
function Expect-Decision($Result, [string[]]$Reasons, [string]$Label) {
    $expected = $Reasons.Count -gt 0
    Note ($Result.forceFull -is [bool] -and $Result.forceFull -eq $expected) ($Label + ': forceFull')
    Note ($Result.launchAll -is [bool] -and $Result.launchAll -eq $expected) ($Label + ': launchAll')
    # Order, duplicates, missing and invented reasons are observable contract failures.
    Note (($Result.reasons -join '|') -ceq ($Reasons -join '|')) ($Label + ': reasons=' + ($Result.reasons -join '|'))
}
function New-Closure {
    @{ validationState = 'verified'; mode = 'Full'; coverageComplete = $true
        requiredCount = 12; executedCount = 10; expectedSkipCount = 2; carriedCount = 0
        failedCount = 0; unexpectedSkipCount = 0; completedAtUtc = '2026-11-13T11:59:00Z'
        sourceSha = '2' * 40; closureDigest = 'b' * 64
        forcedFull = @('explicit-force-full', 'full-anchor-expired') }
}
try {
    switch ($Case) {
        'ContractOrUnknownForcesFull' {
            $argsMap = New-Args
            Expect-Decision (Get-RcForcedFullDecision @argsMap) @() 'complete equal contract'
            $before = $argsMap | ConvertTo-Json -Depth 10 -Compress
            # Every contract input is mandatory, even when absent or malformed on BOTH sides.
            foreach ($field in @($argsMap.CurrentContract.Keys)) {
                $changed = New-Args
                $replacement = switch ($field) {
                    'format' { 'antiphon-rc-input-contract-v2' }
                    'inputMapVersion' { '2' }
                    'sdkVersion' { '10.0.205' }
                    'runtimeVersion' { '9.0.11' }
                    'platform' { 'windows' }
                    'executionImplementation' { 'fallback' }
                    default { 'b' * 64 }
                }
                $changed.CurrentContract[$field] = $replacement
                $reason = if ($field -ceq 'format') { 'contract-current-unknown:format' } else { 'contract-changed:' + $field }
                Expect-Decision (Get-RcForcedFullDecision @changed) @($reason) ('changed ' + $field)
                foreach ($side in @('CurrentContract','BaselineContract')) {
                    $missing = New-Args
                    $missing[$side].Remove($field)
                    $prefix = if ($side -ceq 'CurrentContract') { 'current' } else { 'baseline' }
                    Expect-Decision (Get-RcForcedFullDecision @missing) @('contract-' + $prefix + '-unknown:' + $field) ('missing ' + $side + '.' + $field)
                }
                $unknown = New-Args
                $unknown.CurrentContract[$field] = $null
                $unknown.BaselineContract[$field] = $null
                Expect-Decision (Get-RcForcedFullDecision @unknown) @(('contract-baseline-unknown:' + $field), ('contract-current-unknown:' + $field)) ('unknown both ' + $field)
            }
            foreach ($bad in @('', 42, @('a'), ('x' * 64))) {
                $invalid = New-Args
                $invalid.CurrentContract.policyHash = $bad
                Expect-Decision (Get-RcForcedFullDecision @invalid) @('contract-current-unknown:policyHash') 'invalid hash type/value'
            }
            foreach ($row in @(
                @('baselineState','absent','baseline-unproven'), @('baselineState','unrelated','baseline-unproven'),
                @('admissionChainState','corrupt','admission-chain-unproven'), @('admissionChainState','missing','admission-chain-unproven'),
                @('historyState','missing','history-unproven'), @('historyState','corrupt','history-unproven'),
                @('globalOwnershipState','unresolved','global-ownership-unresolved'),
                @('shadowState','discrepancy','shadow-discrepancy'), @('shadowState','unknown','shadow-unproven'))) {
                $uncertain = New-Args
                $uncertain.Evidence[$row[0]] = $row[1]
                Expect-Decision (Get-RcForcedFullDecision @uncertain) @($row[2]) ($row -join ':')
                $missing = New-Args
                $missing.Evidence.Remove($row[0])
                $reason = if ($row[0] -ceq 'shadowState') { 'shadow-unproven' } else { $row[2] }
                Expect-Decision (Get-RcForcedFullDecision @missing) @($reason) ('absent ' + $row[0])
            }
            $many = New-Args
            $many.ForceFull = $true
            $many.Evidence.historyState = 'missing'
            $many.CurrentContract.policyHash = 'b' * 64
            Expect-Decision (Get-RcForcedFullDecision @many) @('contract-changed:policyHash','explicit-force-full','history-unproven') 'all sorted reasons'
            Note (($argsMap | ConvertTo-Json -Depth 10 -Compress) -ceq $before) 'policy mutated its input'
            # Forward-compatible extra fields must not silently escape this complete contract.
            $extra = New-Args
            $extra.CurrentContract['unrecognizedInput'] = 'new semantics'
            Expect-Decision (Get-RcForcedFullDecision @extra) @('contract-current-unknown:unrecognizedInput') 'unrecognized contract field'
        }
        'AnchorAgeCannotRenewThroughCarry' {
            foreach ($row in @(
                @('2026-11-06T12:00:00.0000001Z', @()),
                @('2026-11-06T12:00:00Z', @('full-anchor-expired')),
                @('2026-11-06T11:59:59.9999999Z', @('full-anchor-expired')),
                @('2026-11-13T12:00:00.0000001Z', @('full-anchor-future')),
                @('', @('full-anchor-time-unknown')), @('yesterday', @('full-anchor-time-unknown')),
                @('2026-11-12T12:00:00', @('full-anchor-time-unknown')))) {
                $argsMap = New-Args
                $argsMap.FullAnchor = New-Anchor $row[0]
                Expect-Decision (Get-RcForcedFullDecision @argsMap) $row[1] ('anchor time ' + $row[0])
            }
            $missing = New-Args
            $missing.FullAnchor = $null
            Expect-Decision (Get-RcForcedFullDecision @missing) @('full-anchor-unproven') 'no anchor'
            foreach ($field in @('kind','admissionState','sourceSha','closureDigest')) {
                $missing = New-Args
                $missing.FullAnchor.Remove($field)
                Expect-Decision (Get-RcForcedFullDecision @missing) @('full-anchor-unproven') ('anchor missing ' + $field)
            }
            $carry = New-Args
            $carry.FullAnchor = New-Anchor '2026-11-01T12:00:00Z'
            $carry.Evidence['candidateCompletedAtUtc'] = '2026-11-13T11:59:00Z'
            $carry.Evidence['releaseCreatedAtUtc'] = '2026-11-13T11:59:00Z'
            $carry.Evidence['sampleCompletedAtUtc'] = '2026-11-13T11:59:00Z'
            $original = $carry.FullAnchor | ConvertTo-Json -Compress
            $decision = Get-RcForcedFullDecision @carry
            Expect-Decision $decision @('full-anchor-expired') 'carry/release/sample cannot renew'
            Note (($decision.fullAnchor | ConvertTo-Json -Compress) -ceq $original) 'original Full anchor not retained'
            $carry.FullAnchor.kind = 'carried'
            Expect-Decision (Get-RcForcedFullDecision @carry) @('full-anchor-unproven') 'carried result is not Full'
        }
        'ExecutedFullCanCloseWithReasons' {
            $closure = New-Closure
            $before = $closure | ConvertTo-Json -Depth 10 -Compress
            $result = Get-RcExecutedFullAnchor -Closure $closure -UtcNow $script:Now
            Note ($result.eligible -eq $true -and @($result.reasons).Count -eq 0) 'fresh all-executed Full rejected because of pre-run reasons'
            Note (($result.forcedFull -join '|') -ceq 'explicit-force-full|full-anchor-expired') 'pre-run reasons erased'
            if ($result.fullAnchor) {
                Note ($result.fullAnchor.completedAtUtc -ceq '2026-11-13T11:59:00Z') 'anchor time not execution time'
                Note ($result.fullAnchor.sourceSha -ceq ('2' * 40) -and $result.fullAnchor.closureDigest -ceq ('b' * 64)) 'new anchor lost origin'
                $next = New-Args
                $next.FullAnchor = $result.fullAnchor
                Expect-Decision (Get-RcForcedFullDecision @next) @() 'newly executed Full resets age'
            } else { Note $false 'no new anchor' }
            Note (($closure | ConvertTo-Json -Depth 10 -Compress) -ceq $before) 'closure mutated'
            foreach ($row in @(
                @('mode','Incremental','closure-not-full'), @('validationState','unproven','closure-unproven'),
                @('coverageComplete',$false,'closure-incomplete'), @('coverageComplete','true','closure-incomplete'),
                @('carriedCount',1,'closure-has-carries'), @('executedCount',9,'closure-membership-incomplete'),
                @('failedCount',1,'closure-failed'), @('unexpectedSkipCount',1,'closure-unexpected-skip'),
                @('executedCount',-1,'closure-counts-unknown'), @('executedCount','10','closure-counts-unknown'),
                @('requiredCount',0,'closure-counts-unknown'), @('sourceSha','label','closure-origin-unknown'),
                @('closureDigest',$null,'closure-origin-unknown'), @('completedAtUtc','2026-11-14T00:00:00Z','closure-time-future'))) {
                $bad = New-Closure
                $bad[$row[0]] = $row[1]
                $refused = Get-RcExecutedFullAnchor -Closure $bad -UtcNow $script:Now
                Note ($refused.eligible -eq $false -and $null -eq $refused.fullAnchor) ('ineligible anchor: ' + $row[0])
                Note (($refused.reasons -join '|') -ceq $row[2]) ('closure reason ' + $row[0] + ': ' + ($refused.reasons -join '|'))
            }
            foreach ($field in @('requiredCount','executedCount','expectedSkipCount','carriedCount','failedCount','unexpectedSkipCount')) {
                $bad = New-Closure
                $bad.Remove($field)
                $refused = Get-RcExecutedFullAnchor -Closure $bad -UtcNow $script:Now
                Note (-not $refused.eligible -and ($refused.reasons -join '|') -ceq 'closure-counts-unknown') ('missing count ' + $field)
            }
            $bad = New-Closure
            $bad.completedAtUtc = $null
            $refused = Get-RcExecutedFullAnchor -Closure $bad -UtcNow $script:Now
            Note (-not $refused.eligible -and ($refused.reasons -join '|') -ceq 'closure-time-unknown') 'missing execution time'
        }
        default { throw ('unknown case ' + $Case) }
    }
} catch { Note $false ('unexpected harness error: ' + $_.Exception.Message) }
Assert-C487 ($script:Errors.Count -eq 0) ('RF ' + $Case + '.contract') ($script:Errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
