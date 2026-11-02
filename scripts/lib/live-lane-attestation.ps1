#requires -Version 7.0
# CARD-1545 S2a-2: live-lane environment attestation consumer. Diagnostic and shadow-only: it
# never earns gate credit. It recomputes every verdict from the document's own facts, trusts no
# summary field, and makes the attestation unknown whenever an executable, settings source or
# runtime fact is not proven. Schema faults throw (status exit 2); nothing echoes a value.
. "$PSScriptRoot/test-selection.ps1"

$script:LiveLaneAttestationFormat='antiphon-live-lane-attestation-v1'
$script:LiveLaneAttestationFields=@('account','environment','external','format','host','installedTarget','laneId','model','rows','runtime','schemaVersion','settings')
$script:LiveLaneRuntimeFields=@('backend','dotnetRuntime','dotnetSdk','node','pwsh','restoredPackagesDigest')
$script:LiveLaneEnvironmentNames=@('ANTHROPIC_BASE_URL','CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN','DISABLE_AUTOUPDATER')
$script:LiveLaneSettingsKinds=@('global','local','managed','project','user')
$script:LiveLaneCredentialNames=@('oauthAccount','accessToken','refreshToken','apiKey','primaryApiKey','ANTHROPIC_API_KEY','CLAUDE_CODE_OAUTH_TOKEN',
    'ANTHROPIC_AUTH_TOKEN','ANTHROPIC_API_KEY_FILE','ANTHROPIC_AUTH_TOKEN_FILE','CLAUDE_CODE_OAUTH_TOKEN_FILE','ANTIPHON_TASK_TOKEN')
# A PATH-resolved launcher is not an owned attested image.
$script:LiveLanePathSearchLeaf=[regex]::new('^cl(\.(exe|bat|cmd|ps1))?$','IgnoreCase,CultureInvariant')
$script:LiveLaneReuseHorizon=[TimeSpan]::FromHours(24)
$script:LiveLaneClockSkew=[TimeSpan]::FromSeconds(120)
$script:LiveLaneProbeWindow=[TimeSpan]::FromSeconds(60)

function Assert-LiveLaneCredentialFree {
    # Any credential-named key anywhere refuses the document; the value is never read or echoed.
    param($Value)
    if ($Value -is [Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ($script:LiveLaneCredentialNames -ccontains $key) { throw 'credential-field-present' }
            Assert-LiveLaneCredentialFree $Value[$key]
        }
    } elseif ($Value -is [Collections.IList]) { foreach ($item in $Value) { Assert-LiveLaneCredentialFree $item } }
}

function Assert-LiveLaneObject {
    # Exact keys; $Optional keys may be absent. Anything else is a schema fault.
    param($Value, [string[]]$Keys, [string[]]$Optional = @())
    if ($Value -isnot [Collections.IDictionary]) { throw 'attestation-invalid' }
    foreach ($key in $Value.Keys) { if (-not ($Keys -ccontains $key)) { throw 'attestation-invalid' } }
    foreach ($key in $Keys) { if (-not ($Optional -ccontains $key) -and -not $Value.Contains($key)) { throw 'attestation-invalid' } }
}

function Assert-LiveLaneString {
    param($Value, [switch]$Nullable)
    if ($Nullable -and $null -eq $Value) { return }
    if ($Value -isnot [string] -or -not $Value) { throw 'attestation-invalid' }
}

function Get-LiveLaneSha {
    # A typed SHA-256 or $null (an unresolved image); case is not identity.
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -isnot [string] -or $Value -cnotmatch '^[0-9A-Fa-f]{64}$') { throw 'attestation-invalid' }
    return $Value.ToLowerInvariant()
}

function Read-LiveLaneChain {
    param($Value)
    if ($Value -isnot [Collections.IList]) { throw 'attestation-invalid' }
    $chain=[Collections.Generic.List[object]]::new()
    foreach ($entry in $Value) { Assert-LiveLaneObject $entry @('sha256'); $chain.Add((Get-LiveLaneSha $entry['sha256'])) }
    Write-Output -NoEnumerate $chain
}

function Get-LiveLaneSortedBy {
    # Ordinal order by a unique string key, independent of the host culture.
    param($Items, [string]$Key)
    $byKey=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal); foreach ($item in $Items) { $byKey[$item[$Key]]=$item }
    return @(foreach ($name in (Get-TestSelectionSorted @($byKey.Keys))) { $byKey[$name] })
}

function Get-LiveLaneEnvironmentAttestation {
    param([string]$Path, [string]$Lane, [string[]]$Roster)
    try { $document=[IO.File]::ReadAllText($Path) | ConvertFrom-Json -AsHashtable -Depth 64 } catch { throw 'attestation-invalid' }
    Assert-LiveLaneCredentialFree $document
    Assert-LiveLaneObject $document $script:LiveLaneAttestationFields
    if ($document['format'] -cne $script:LiveLaneAttestationFormat -or $document['schemaVersion'] -isnot [long] -or $document['schemaVersion'] -ne 1) { throw 'attestation-invalid' }
    if ($document['laneId'] -isnot [string] -or $document['laneId'] -cne $Lane) { throw 'attestation-invalid' }
    $reasons=[Collections.Generic.List[string]]::new()

    # Executable identity: every row, never a prefix. Paths and process facts are checked here and
    # kept out of the digest; bytes, version, chain shape and the installed target are hashed.
    if ($document['rows'] -isnot [Collections.IList] -or -not $document['rows'].Count) { throw 'attestation-invalid' }
    $uids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $executables=foreach ($row in $document['rows']) {
        Assert-LiveLaneObject $row @('uid','attested','observed','pre','post') @('observed')
        if ($row['uid'] -isnot [string] -or -not $row['uid'] -or -not $uids.Add($row['uid'])) { throw 'attestation-invalid' }
        $uid=$row['uid']
        $attested=$row['attested']
        Assert-LiveLaneObject $attested @('route','path','sha256','version','chain')
        foreach ($field in 'route','path','version') { Assert-LiveLaneString $attested[$field] }
        $image=Get-LiveLaneSha $attested['sha256']
        $chain=Read-LiveLaneChain $attested['chain']
        foreach ($side in 'pre','post') { Assert-LiveLaneObject $row[$side] @('sha256') }
        $pre=Get-LiveLaneSha $row['pre']['sha256']; $post=Get-LiveLaneSha $row['post']['sha256']
        if ($attested['route'] -cne 'owned' -or $script:LiveLanePathSearchLeaf.IsMatch(($attested['path'] -replace '^.*[\\/]',''))) { $reasons.Add("route-not-owned:$uid") }
        if ($null -eq $image -or @($chain | Where-Object { $null -eq $_ }).Count) { $reasons.Add("chain-unresolved:$uid") }
        if ($null -eq $pre -or $null -eq $post -or $pre -cne $post -or $pre -cne $image) { $reasons.Add("executable-drift:$uid") }
        $observed=$row['observed']
        if ($null -eq $observed) { $reasons.Add("observation-missing:$uid") }
        else {
            Assert-LiveLaneObject $observed @('path','sha256','chain','pid','started','session')
            Assert-LiveLaneString $observed['path']
            $observedChain=Read-LiveLaneChain $observed['chain']
            if (@($observedChain | Where-Object { $null -eq $_ }).Count) { $reasons.Add("chain-unresolved:$uid") }
            $process=($observed['pid'] -is [long]) -and ($observed['pid'] -gt 0) -and ($observed['started'] -is [string]) -and $observed['started'] -and
                ($observed['session'] -is [string]) -and $observed['session']
            if (-not $process -or $observed['path'] -cne $attested['path'] -or (Get-LiveLaneSha $observed['sha256']) -cne $image -or
                    ($observedChain -join ',') -cne ($chain -join ',') -or $observedChain.Count -ne $chain.Count) { $reasons.Add("executable-mismatch:$uid") }
        }
        [ordered]@{uid=$uid;route=$attested['route'];sha256=$image;version=$attested['version'];chain=$chain}
    }
    Assert-LiveLaneObject $document['installedTarget'] @('sha256')
    $installed=Get-LiveLaneSha $document['installedTarget']['sha256']
    if ($null -eq $installed) { $reasons.Add('installed-target-unresolved') }

    # Runtime: every field named, typed and present; a null is a missing proof, not a value.
    Assert-LiveLaneObject $document['runtime'] $script:LiveLaneRuntimeFields
    Assert-LiveLaneObject $document['host'] @('enrollmentId','os','arch')
    foreach ($field in $script:LiveLaneRuntimeFields) {
        Assert-LiveLaneString $document['runtime'][$field] -Nullable
        if ($null -eq $document['runtime'][$field]) { $reasons.Add("runtime-missing:$field") }
    }
    foreach ($field in 'enrollmentId','os','arch') { Assert-LiveLaneString $document['host'][$field] }

    # Settings: eligibility is recomputed from the source states; the document's own
    # reuseEligible can only withdraw reuse, never grant it.
    $settings=$document['settings']
    Assert-LiveLaneObject $settings @('digest','reuseEligible','sources')
    Assert-LiveLaneString $settings['digest']
    if ($settings['reuseEligible'] -isnot [bool] -or $settings['sources'] -isnot [Collections.IList]) { throw 'attestation-invalid' }
    if (-not $settings['reuseEligible']) { $reasons.Add('settings-not-reusable') }
    $kinds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sources=foreach ($source in $settings['sources']) {
        Assert-LiveLaneObject $source @('kind','state','projectionDigest')
        Assert-LiveLaneString $source['kind']; Assert-LiveLaneString $source['state']
        $projection=Get-LiveLaneSha $source['projectionDigest']
        $kind=$source['kind']; $state=$source['state']
        if (-not $kinds.Add($kind)) { throw 'attestation-invalid' }
        if (-not (($script:LiveLaneSettingsKinds+@('overlay')) -ccontains $kind) -or $state -ceq 'unresolved') { $reasons.Add('source-unresolved') }
        elseif ($state -ceq 'unreadable') { $reasons.Add("source-unreadable:$kind") }
        elseif ($state -ceq 'absent-unverified' -or $state -ceq 'unverified') { $reasons.Add("source-unverified:$kind") }
        elseif (-not (@('absent','empty','value') -ccontains $state)) { $reasons.Add("source-unresolved") }
        if ($null -eq $projection) { throw 'attestation-invalid' }
        [ordered]@{kind=$kind;state=$state;projectionDigest=$projection}
    }
    foreach ($kind in $script:LiveLaneSettingsKinds) { if (-not $kinds.Contains($kind)) { $reasons.Add("source-missing:$kind") } }
    Assert-LiveLaneObject $document['model'] @('mode','requested','effective')
    foreach ($field in 'mode','requested','effective') { Assert-LiveLaneString $document['model'][$field] -Nullable }
    if ($null -eq $document['model']['effective']) { $reasons.Add('model-unknown') }
    $environment=$document['environment']
    Assert-LiveLaneObject $environment $script:LiveLaneEnvironmentNames
    foreach ($name in $script:LiveLaneEnvironmentNames) {
        if ($environment[$name] -isnot [string] -or $environment[$name] -cnotmatch '^(absent|empty|value:.+)$') { throw 'attestation-invalid' }
    }
    Assert-LiveLaneObject $document['account'] @('enrollmentId','authMethod','subscriptionClass')
    foreach ($field in 'enrollmentId','authMethod','subscriptionClass') { Assert-LiveLaneString $document['account'][$field] }
    Assert-LiveLaneObject $document['external'] @('epoch')
    Assert-LiveLaneString $document['external']['epoch'] -Nullable

    $unknownReasons=@(Get-TestSelectionSorted @($reasons | Select-Object -Unique))
    if ($unknownReasons.Count) {
        # No partial digest: one refused row makes the whole environment unknown.
        return [ordered]@{format='antiphon-live-lane-environment-v1';laneId=$Lane;status='unknown';reasons=$unknownReasons;digest=$null;components=$null}
    }
    $components=[ordered]@{
        executable=Get-TestSelectionDigest ([ordered]@{rows=@(Get-LiveLaneSortedBy $executables 'uid');installedTarget=$installed})
        runtime=Get-TestSelectionDigest ([ordered]@{runtime=$document['runtime'];os=$document['host']['os'];arch=$document['host']['arch']})
        settings=Get-TestSelectionDigest ([ordered]@{sources=@(Get-LiveLaneSortedBy $sources 'kind')
            model=$document['model'];environment=$environment})
        account=Get-TestSelectionDigest ([ordered]@{host=$document['host']['enrollmentId'];account=$document['account']})
    }
    $material="antiphon-live-lane-environment`n"+(ConvertTo-TestSelectionCanonicalJson ([ordered]@{schemaVersion=1;laneId=$Lane;components=$components}))
    $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($material))).ToLowerInvariant()
    return [ordered]@{format='antiphon-live-lane-environment-v1';laneId=$Lane;status='complete';reasons=@();digest=$digest;components=$components
        external=[ordered]@{epoch=$document['external']['epoch']}}
}

function Test-LiveLaneExternalFreshness {
    # D-6. AlwaysRerun is the default until the operator accepts reuse. Bounded24h is the only
    # exception: an epoch, age under a fixed 24 h horizon, fresh auth and control probes bound to
    # this candidate, host and account, and no revocation. Remote stability is never proven.
    param([string]$Policy = 'AlwaysRerun', $ExecutedAtUtc, $NowUtc, $Candidate, $Executed, $Probes = @(), $Revocations = @(), $MaxAgeHours)
    if ($PSBoundParameters.ContainsKey('MaxAgeHours')) { throw 'horizon-not-configurable' }
    if (-not (@('AlwaysRerun','Bounded24h') -ccontains $Policy)) { throw 'policy-invalid' }
    $refuse={ param([string[]]$why) return [ordered]@{policy=$Policy;reuseEligible=$false;reasons=@(Get-TestSelectionSorted @($why | Select-Object -Unique));externalProof=$false} }
    if ($Policy -ceq 'AlwaysRerun') { return & $refuse @('policy-always-rerun') }
    $now=[DateTimeOffset]$NowUtc; $executedAt=[DateTimeOffset]$ExecutedAtUtc
    $why=[Collections.Generic.List[string]]::new()
    if ($null -eq $Executed -or $Executed['epoch'] -isnot [string] -or -not $Executed['epoch']) { $why.Add('external-epoch-unknown') }
    $age=$now-$executedAt
    if ($age -lt -$script:LiveLaneClockSkew) { $why.Add('clock-skew') }
    elseif ($age -ge $script:LiveLaneReuseHorizon) { $why.Add('reuse-expired') }
    foreach ($kind in 'auth','control') {
        $matching=@($Probes | Where-Object { $_['kind'] -ceq $kind })
        if (-not $matching.Count) { $why.Add("probe-missing:$kind"); continue }
        foreach ($probe in $matching) {
            if ($probe['nonce'] -cne $Candidate['nonce']) { $why.Add('probe-foreign') }
            if ($probe['host'] -cne $Candidate['host'] -or $probe['host'] -cne $Executed['host']) { $why.Add('host-mismatch') }
            if ($probe['account'] -cne $Candidate['account'] -or $probe['account'] -cne $Executed['account']) { $why.Add('account-mismatch') }
            if ($probe['result'] -cne 'Ok') { $why.Add("probe-failed:$kind") }
            $probeAge=$now-[DateTimeOffset]$probe['atUtc']
            if ($probeAge -gt $script:LiveLaneProbeWindow -or $probeAge -lt -$script:LiveLaneClockSkew) { $why.Add("probe-stale:$kind") }
        }
    }
    foreach ($revocation in @($Revocations)) {
        if (($revocation['epoch'] -and $revocation['epoch'] -ceq $Executed['epoch']) -or
            ($revocation['receiptId'] -and $revocation['receiptId'] -ceq $Executed['receiptId'])) { $why.Add('revoked') }
    }
    if ($why.Count) { return & $refuse $why }
    return [ordered]@{policy=$Policy;reuseEligible=$true;reasons=@('d6-bounded-exception');externalProof=$false}
}
