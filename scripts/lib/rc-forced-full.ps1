#requires -Version 7.0
# CARD-1836 S8a. Pure policy; no selection, launch, persistence or S1 API changes.
# Callers must supply findings from the admission/closure validators, not operator
# assertions. This policy neither authenticates receipts nor authorizes carries.
# Hashes below bind COMPLETE executed closures and actual toolchain receipts, not
# just entry scripts or global.json. Unresolved imports/ownership are unresolved
# evidence, even if some entry files can be hashed. No equivalence override exists.

function Get-RcFullField {
    param($Object, [string]$Name)
    if ($Object -is [Collections.IDictionary]) {
        foreach ($key in $Object.Keys) {
            if ($key -is [string] -and $key -ceq $Name) { return ,($Object[$key]) }
        }
    } elseif ($Object -is [pscustomobject]) {
        foreach ($property in $Object.PSObject.Properties) {
            if ($property.Name -ceq $Name) { return ,($property.Value) }
        }
    }
    return $null
}

function Get-RcFullFieldNames {
    param($Object)
    if ($Object -is [Collections.IDictionary]) { return @($Object.Keys) }
    if ($Object -is [pscustomobject]) { return @($Object.PSObject.Properties.Name) }
    return @()
}

function Test-RcFullText {
    param($Value, [string]$Expected)
    return ($Value -is [string] -and $Value -ceq $Expected)
}

function Test-RcFullDigest {
    param($Value)
    return ($Value -is [string] -and $Value -cmatch '\A[0-9a-f]{64}\z')
}

function Test-RcFullSourceSha {
    param($Value)
    return ($Value -is [string] -and $Value -cmatch '\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z')
}

function Test-RcFullContractValue {
    param([string]$Field, $Value)
    if ($Value -isnot [string]) { return $false }
    if ($Field.EndsWith('Hash', [StringComparison]::Ordinal)) { return (Test-RcFullDigest $Value) }
    switch -CaseSensitive ($Field) {
        'format' { return ($Value -ceq 'antiphon-rc-input-contract-v1') }
        'inputMapVersion' { return ($Value -cmatch '\A[1-9][0-9]*\z') }
        'sdkVersion' { return ($Value -cmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\z') }
        'runtimeVersion' { return ($Value -cmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\z') }
        'platform' { return (@('linux','windows') -ccontains $Value) }
        'executionImplementation' { return (@('compiled','fallback') -ccontains $Value) }
        default { return $false }
    }
}

function ConvertTo-RcFullUtcTime {
    param($Value)
    # Require explicit UTC and preserve tick precision at the seven-day boundary.
    # A local/unspecified timestamp, DateTime coercion or culture-dependent date
    # cannot be used as an admission clock. Missing time never means "now".
    if ($Value -isnot [string] -or $Value -cnotmatch '\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z\z') { return $null }
    $parsed = [DateTimeOffset]::MinValue
    $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    $formats = [string[]]@("yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'")
    if ([DateTimeOffset]::TryParseExact($Value, $formats, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) { return $parsed }
    return $null
}

function Get-RcForcedFullDecision {
    param(
        $CurrentContract, $BaselineContract, $Evidence, $FullAnchor,
        [DateTimeOffset]$UtcNow = [DateTimeOffset]::UtcNow,
        [switch]$ForceFull
    )
    $reasons = [Collections.Generic.List[string]]::new()
    if ($ForceFull) { $reasons.Add('explicit-force-full') }
    $fields = @('format','inputMapVersion','sdkVersion','runtimeVersion','platform','executionImplementation',
        'policyHash','catalogHash','selectorHash','skipResolverHash','discoveryHash','parserHash',
        'coverageHash','launcherHash','custodyHash','buildHash','harnessHash','inputMapHash','sdkHash','runtimeHash')
    foreach ($field in $fields) {
        $current = Get-RcFullField $CurrentContract $field
        $baseline = Get-RcFullField $BaselineContract $field
        $currentKnown = Test-RcFullContractValue $field $current
        $baselineKnown = Test-RcFullContractValue $field $baseline
        if (-not $currentKnown) { $reasons.Add('contract-current-unknown:' + $field) }
        if (-not $baselineKnown) { $reasons.Add('contract-baseline-unknown:' + $field) }
        if ($currentKnown -and $baselineKnown -and $current -cne $baseline) { $reasons.Add('contract-changed:' + $field) }
    }
    # New execution semantics require a reviewed contract version, not silently
    # ignored fields, even when the same extra field appears on both sides.
    foreach ($side in @(@('current',$CurrentContract), @('baseline',$BaselineContract))) {
        foreach ($field in @(Get-RcFullFieldNames $side[1])) {
            if ($fields -cnotcontains $field) { $reasons.Add('contract-' + $side[0] + '-unknown:' + $field) }
        }
    }
    foreach ($row in @(
        @('baselineState','admitted-ancestor','baseline-unproven'),
        @('admissionChainState','verified','admission-chain-unproven'),
        @('historyState','complete','history-unproven'),
        @('globalOwnershipState','resolved','global-ownership-unresolved'))) {
        if (-not (Test-RcFullText (Get-RcFullField $Evidence $row[0]) $row[1])) { $reasons.Add($row[2]) }
    }
    $shadow = Get-RcFullField $Evidence 'shadowState'
    if (Test-RcFullText $shadow 'discrepancy') { $reasons.Add('shadow-discrepancy') }
    elseif (-not (Test-RcFullText $shadow 'clean')) { $reasons.Add('shadow-unproven') }

    $anchorKnown = (Test-RcFullText (Get-RcFullField $FullAnchor 'kind') 'executed-full') -and
        (Test-RcFullText (Get-RcFullField $FullAnchor 'admissionState') 'verified') -and
        (Test-RcFullSourceSha (Get-RcFullField $FullAnchor 'sourceSha')) -and
        (Test-RcFullDigest (Get-RcFullField $FullAnchor 'closureDigest'))
    if (-not $anchorKnown) { $reasons.Add('full-anchor-unproven') }
    else {
        $completed = ConvertTo-RcFullUtcTime (Get-RcFullField $FullAnchor 'completedAtUtc')
        if ($null -eq $completed) { $reasons.Add('full-anchor-time-unknown') }
        elseif ($completed -gt $UtcNow) { $reasons.Add('full-anchor-future') }
        elseif (($UtcNow.UtcTicks - $completed.UtcTicks) -ge [TimeSpan]::FromDays(7).Ticks) { $reasons.Add('full-anchor-expired') }
    }
    $reasons.Sort([StringComparer]::Ordinal)
    return [pscustomobject]@{
        forceFull = ($reasons.Count -gt 0)
        launchAll = ($reasons.Count -gt 0)
        reasons = $reasons.ToArray()
        # Deliberately retain the ORIGINAL Full origin, never a candidate,
        # release or sample completion timestamp. This function does not renew it.
        fullAnchor = $FullAnchor
    }
}

function Get-RcExecutedFullAnchor {
    param($Closure, [DateTimeOffset]$UtcNow = [DateTimeOffset]::UtcNow)
    # S5b/S6d must independently verify provenance, exact roster membership,
    # current expected-skip declarations and complete outcomes before setting
    # validationState=verified. Counts are sanity checks, not that proof.
    $reasons = [Collections.Generic.List[string]]::new()
    if (-not (Test-RcFullText (Get-RcFullField $Closure 'validationState') 'verified')) { $reasons.Add('closure-unproven') }
    if (-not (Test-RcFullText (Get-RcFullField $Closure 'mode') 'Full')) { $reasons.Add('closure-not-full') }
    $complete = Get-RcFullField $Closure 'coverageComplete'
    if ($complete -isnot [bool] -or -not $complete) { $reasons.Add('closure-incomplete') }
    $counts = @{}
    $countsKnown = $true
    foreach ($field in @('requiredCount','executedCount','expectedSkipCount','carriedCount','failedCount','unexpectedSkipCount')) {
        $value = Get-RcFullField $Closure $field
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -lt 0 -or $value -gt [int]::MaxValue -or ($field -ceq 'requiredCount' -and $value -eq 0)) {
            $countsKnown = $false
        } else { $counts[$field] = [long]$value }
    }
    if (-not $countsKnown) { $reasons.Add('closure-counts-unknown') }
    else {
        if ($counts.carriedCount -ne 0) { $reasons.Add('closure-has-carries') }
        if ($counts.executedCount + $counts.expectedSkipCount -ne $counts.requiredCount) { $reasons.Add('closure-membership-incomplete') }
        if ($counts.failedCount -ne 0) { $reasons.Add('closure-failed') }
        if ($counts.unexpectedSkipCount -ne 0) { $reasons.Add('closure-unexpected-skip') }
    }
    if (-not (Test-RcFullSourceSha (Get-RcFullField $Closure 'sourceSha')) -or
        -not (Test-RcFullDigest (Get-RcFullField $Closure 'closureDigest'))) { $reasons.Add('closure-origin-unknown') }
    $completed = ConvertTo-RcFullUtcTime (Get-RcFullField $Closure 'completedAtUtc')
    if ($null -eq $completed) { $reasons.Add('closure-time-unknown') }
    elseif ($completed -gt $UtcNow) { $reasons.Add('closure-time-future') }
    $reasons.Sort([StringComparer]::Ordinal)
    $anchor = $null
    if ($reasons.Count -eq 0) {
        $anchor = [ordered]@{
            kind = 'executed-full'; admissionState = 'verified'
            completedAtUtc = (Get-RcFullField $Closure 'completedAtUtc')
            sourceSha = (Get-RcFullField $Closure 'sourceSha')
            closureDigest = (Get-RcFullField $Closure 'closureDigest')
        }
    }
    $prior = Get-RcFullField $Closure 'forcedFull'
    $forced = @()
    if ($null -ne $prior) { $forced = @($prior) }
    return [pscustomobject]@{
        eligible = ($reasons.Count -eq 0); reasons = $reasons.ToArray(); fullAnchor = $anchor
        # Pre-run reasons are historical facts. They do not veto a freshly
        # executed Full, and are never cleared to manufacture an eligible anchor.
        forcedFull = $forced
    }
}
