#requires -Version 7.0
# CARD-1545 S2a-2: live-lane environment attestation witnesses (V-8..V-13 of the S2a/S2b test design).
# Documents come from LiveLaneAttestationExport (real S1 objects); schema forgeries are made here.
param([string]$Case, [string]$ResultsDirectory, [string]$AttestationDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/live-lane-fingerprint.ps1"
$ResultsDirectory=New-C487Root $ResultsDirectory
$script:Lane='fixture-lane'
$script:Method=($Case -replace '^C1545_','')
$script:StatusScript=Join-Path $PSScriptRoot 'live-lane-status.ps1'
$script:Pwsh=(Get-Process -Id $PID).Path
$script:Sentinel='SENTINEL-c1545-credential'
# The lane roster, written out here as an independent oracle: the six S1 core uids
# (LiveClaudeCore.All) in the manifest's namespace.class.method(arguments) form.
$script:Roster=@(
    'Antiphon.Agents.Pty.Tests.ClaudeSubmitContractLiveTests.Submitting_via_two_writes_completes_a_turn(claude)'
    'Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Control_auto_connects_a_bridge_session_before_the_first_user'
    'Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Settings_file_prevents_auto_connect_and_keeps_startup_records'
    'Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Remote_control_slash_command_still_arms_after_settings_off'
    'Antiphon.Agents.Pty.Tests.ClaudeRemoteControlMenuCanaryTests.C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt'
    'Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs')

function Check($ok, [string]$label, [string]$detail='') { Assert-C487 ([bool]$ok) "C1545 $($script:Method)/$label" $detail }
function Doc([string]$name) { return [IO.File]::ReadAllText((Join-Path $AttestationDirectory "$name.json")) | ConvertFrom-Json -AsHashtable }
function Forge([string]$name, [scriptblock]$edit) {
    $document=Doc $name
    & $edit $document
    $path=Join-Path $ResultsDirectory ("forged-{0}.json" -f [guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($path,(ConvertTo-Json -Depth 30 $document))
    return $path
}
function Att([string]$nameOrPath) {
    $path=if ([IO.Path]::IsPathRooted($nameOrPath)) { $nameOrPath } else { Join-Path $AttestationDirectory "$nameOrPath.json" }
    try { return Get-LiveLaneEnvironmentAttestation -Path $path -Lane $script:Lane -Roster $script:Roster } catch { return @{status='error';error=$_.Exception.Message} }
}
function Unbound([string]$name) {
    try { return Get-LiveLaneEnvironmentAttestation -Path (Join-Path $AttestationDirectory "$name.json") -Lane $script:Lane } catch { return @{status='error';error=$_.Exception.Message} }
}
function Split-Uid([string]$uid) {
    # namespace.class.method(arguments) -> one manifest core row in the fixture lane.
    if ($uid -cnotmatch '^(.+)\.([^.]+)\.([^.(]+)(\((.*)\))?$') { throw "bad roster uid: $uid" }
    $arguments=if ($Matches[4]) { @($Matches[5] -split ',') } else { @() }
    return [ordered]@{project='Pty.Tests';namespace=$Matches[1];class=$Matches[2];method=$Matches[3];arguments=$arguments}
}
# A directory link: a junction on Windows (no privilege needed), a symbolic link elsewhere.
function New-DirectoryLink([string]$link, [string]$target) {
    if ($IsWindows) { New-Item -ItemType Junction -Path $link -Target $target -ErrorAction Stop | Out-Null }
    else { [void][IO.Directory]::CreateSymbolicLink($link,$target) }
}
function Differs($a, $b, [string[]]$components=@()) {
    if ($a.status -cne 'complete' -or $b.status -cne 'complete' -or $a.digest -cnotmatch '^[0-9a-f]{64}$' -or $a.digest -ceq $b.digest) { return $false }
    foreach ($name in $components) {
        if ($null -eq $a.components -or $null -eq $b.components -or -not $a.components[$name] -or $a.components[$name] -ceq $b.components[$name]) { return $false }
    }
    return $true
}
function Same($a, $b) { return ($a.status -ceq 'complete') -and ($a.digest -cmatch '^[0-9a-f]{64}$') -and ($a.digest -ceq $b.digest) }
# Unknown means the whole document: no digest and no component survives a refused row.
function Unknown($att, [string]$reason) { return ($att.status -ceq 'unknown') -and (@($att.reasons) -ccontains $reason) -and ($null -eq $att.digest) -and ($null -eq $att.components) }
function Show($att) { return "status=$($att.status) reasons=$(@($att.reasons) -join ',') error=$($att.error)" }
function Invoke-Status([string]$attestation) {
    $start=[Diagnostics.ProcessStartInfo]::new($script:Pwsh)
    foreach ($arg in @('-NoProfile','-NonInteractive','-File',$script:StatusScript,'-RepositoryRoot',$ResultsDirectory,'-Lane',$script:Lane,
            '-CandidateSha',('0'*40),'-Attestation',$attestation)) { $start.ArgumentList.Add($arg) }
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true; $start.UseShellExecute=$false
    $process=[Diagnostics.Process]::Start($start)
    $stderr=$process.StandardError.ReadToEndAsync()
    $stdout=$process.StandardOutput.ReadToEnd()
    if (-not $process.WaitForExit(45000)) { $process.Kill($true); throw 'status-timeout' }
    $process.WaitForExit()
    $document=$null; try { $document=$stdout | ConvertFrom-Json -AsHashtable } catch { }
    return @{exit=$process.ExitCode;stdout=$stdout;stderr=$stderr.Result;document=$document}
}
function Refused($status, [string]$reason) {
    return ($status.exit -eq 2) -and ($null -ne $status.document) -and ($status.document.error -ceq $reason) -and ($status.document.gateCredit -is [bool]) -and -not $status.document.gateCredit
}

# A minimal fingerprint fixture so the environment component is witnessed through the lane fingerprint.
function New-LaneFixture {
    $root=Join-Path $ResultsDirectory 'lane repository'
    [void][IO.Directory]::CreateDirectory($root)
    $put={ param($path,$text) $full=Join-Path $root $path; [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full)); [IO.File]::WriteAllText($full,$text) }
    foreach ($a in @(@('init','-q','-b','main'),@('config','user.name','Fixture'),@('config','user.email','fixture@example.invalid'),@('config','core.autocrlf','false'))) { [void](Invoke-TestSelectionGit $root $a) }
    & $put 'tests/rc-project-categories.json' '{"schemaVersion":1,"projects":{"tests/Pty.Tests/Pty.Tests.csproj":["fixture"]},"batches":["tests/Pty.Tests/Pty.Tests.csproj"]}'
    & $put 'tests/Pty.Tests/Pty.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="TUnit" Version="1.0" /></ItemGroup></Project>'
    & $put 'tests/Pty.Tests/T.cs' "namespace Antiphon.Agents.Pty.Tests;`n"
    & $put 'scripts/lib/live-lane-contract.ps1' "# contract`n"
    & $put 'CLAUDE.md' "# fixture`n"
    & $put 'tests/live-lanes.json' (ConvertTo-Json -Depth 10 ([ordered]@{format='antiphon-live-lanes-v1';core=@($script:Roster | ForEach-Object { Split-Uid $_ })
        gates=@();additiveContract=@()}))
    & $put 'tests/live-lane-inputs.json' (ConvertTo-Json -Depth 10 ([ordered]@{format='antiphon-live-lane-inputs-v1';lanes=[ordered]@{'fixture-lane'=[ordered]@{
        manifest='tests/live-lanes.json';contractFiles=@('tests/live-lanes.json','tests/live-lane-inputs.json');minimumRoots=@('tests/Pty.Tests/')
        runtimeRoots=@('scripts/');namespaceRoots=@('Antiphon.Agents.Pty');instructionPatterns=@('CLAUDE.md')}}}))
    [void](Invoke-TestSelectionGit $root @('add','--all')); [void](Invoke-TestSelectionGit $root @('commit','-qm','fixture'))
    return @($root,(Invoke-TestSelectionGit $root @('rev-parse','HEAD')).Stdout.Trim())
}
function Fp([string[]]$fixture, [string]$attestation) {
    $arguments=@{RepositoryRoot=$fixture[0];Sha=$fixture[1];Lane=$script:Lane}
    if ($attestation) { $arguments.Attestation=(Join-Path $AttestationDirectory "$attestation.json") }
    return Get-LiveLaneFingerprint @arguments
}

$expectedRows=@{C1545_CliVersionOrBytesInvalidates=5;C1545_WrapperPayloadInvalidates=4;C1545_AttestedPathMismatchRefuses=5
    C1545_ConfigurationInvalidates=11;C1545_UnknownSettingsRefuseReuse=12;C1545_ExternalEpochIsNotInferred=14}
if (-not $expectedRows.ContainsKey($Case)) { throw "unknown case: $Case" }
if (-not $AttestationDirectory -or -not (Test-Path -LiteralPath (Join-Path $AttestationDirectory 'base.json'))) { throw 'attestation documents missing' }
$base=Att 'base'
$uids=@((Doc 'base').rows | ForEach-Object { $_.uid })
switch ($Case) {
    'C1545_CliVersionOrBytesInvalidates' {
        $fixture=New-LaneFixture
        $plain=Fp $fixture; $withA=Fp $fixture 'base'; $withB=Fp $fixture 'image-b'
        Check ((Differs (Att 'image-b') $base @('executable')) -and ($withA.status -ceq 'complete') -and $withA.components.environment -and
            -not $plain.components.Contains('environment') -and ($withA.components.environment -ceq $base.digest) -and
            ($withA.digest -cne $withB.digest) -and ($withA.digest -cne $plain.digest)) 'same-version-new-bytes' "$(Show $base) fp=$($withA.status)/$(@($withA.unknownReasons) -join ',')"
        Check (Differs (Att 'version-2') $base @('executable')) 'version'
        Check (Differs (Att 'installed-target') $base @('executable')) 'installed-target'
        $drift=Att 'drift'; $fpDrift=Fp $fixture 'drift'
        Check ((Unknown $drift "executable-drift:$($uids[-1])") -and ($fpDrift.status -ceq 'unknown') -and (@($fpDrift.unknownReasons) -ccontains 'environment-unknown')) 'pre-post-drift' (Show $drift)
        $first=Doc 'base'; $second=Doc 'base-again'
        Check ((Same $base (Att 'base-again')) -and ($first.rows[0].attested.path -cne $second.rows[0].attested.path) -and
            ($first.rows[0].observed.session -cne $second.rows[0].observed.session)) 'equal' (Show $base)
    }
    'C1545_WrapperPayloadInvalidates' {
        Check (Differs (Att 'chain-1') (Att 'chain-1b') @('executable')) 'chain-payload'
        Check ((Differs (Att 'chain-1') $base @('executable')) -and (Differs (Att 'chain-2') (Att 'chain-1') @('executable'))) 'chain-shape'
        $nulled=Att (Forge 'chain-1' { param($d) foreach ($side in 'attested','observed') { $d.rows[0][$side].chain[0].sha256=$null } })
        Check (Unknown $nulled "chain-unresolved:$($uids[0])") 'chain-unresolved' (Show $nulled)
        $foreign=Att (Forge 'chain-1' { param($d) $d.rows[0].observed.chain[0].sha256=('f'*64) })
        Check (Unknown $foreign "executable-mismatch:$($uids[0])") 'observed-chain' (Show $foreign)
    }
    'C1545_AttestedPathMismatchRefuses' {
        $path=Att (Forge 'base' { param($d) $d.rows[0].observed.path=$d.rows[0].observed.path+'.other' })
        Check (Unknown $path "executable-mismatch:$($uids[0])") 'launched-path' (Show $path)
        $hash=Att (Forge 'base' { param($d) $d.rows[0].observed.sha256=('e'*64) })
        Check (Unknown $hash "executable-mismatch:$($uids[0])") 'launched-hash' (Show $hash)
        $route=Att (Forge 'base' { param($d) $d.rows[0].attested.route='path-search' }); $leaf=Att 'leaf-cl'
        Check ((Unknown $route "route-not-owned:$($uids[0])") -and (Unknown $leaf "route-not-owned:$($uids[0])")) 'path-route' "$(Show $route) / $(Show $leaf)"
        $missing=Att (Forge 'base' { param($d) $d.rows[0].Remove('observed') })
        Check (Unknown $missing "observation-missing:$($uids[0])") 'missing-observation' (Show $missing)
        $last=Att (Forge 'base' { param($d) $d.rows[5].observed.path=$d.rows[5].observed.path+'.other' })
        Check (($uids.Count -eq 6) -and (Unknown $last "executable-mismatch:$($uids[5])") -and
            -not @($last.reasons | Where-Object { $_ -like "*:$($uids[0])" }).Count) 'any-row' (Show $last)
    }
    'C1545_ConfigurationInvalidates' {
        Check (Differs (Att 'user') $base @('settings')) 'user'
        Check ((Differs (Att 'project') $base @('settings')) -and (Differs (Att 'local') $base @('settings')) -and (Differs (Att 'local') (Att 'project'))) 'project-local'
        Check (Differs (Att 'managed') $base @('settings')) 'managed'
        Check (Differs (Att 'global') $base @('settings')) 'global-state'
        Check (Differs (Att 'overlay-a') (Att 'overlay-b') @('settings')) 'overlay'
        Check (Differs (Att 'model-haiku') (Att 'model-other') @('settings')) 'model'
        $absent=Att 'env-absent'; $empty=Att 'env-empty'
        Check ((Differs $absent $empty @('settings')) -and (Differs $empty $base @('settings')) -and (Differs $absent $base @('settings'))) 'env-tristate'
        Check ((Differs (Att 'runtime-dotnet') $base @('runtime')) -and (Differs (Att 'runtime-packages') $base @('runtime'))) 'runtime-identity'
        Check (Same (Att 'volatile') $base) 'volatile-only'
        $text=[IO.File]::ReadAllText((Join-Path $AttestationDirectory 'credential.json'))
        $credential=Att 'credential'; $output=ConvertTo-TestSelectionCanonicalJson $credential
        $forged=Invoke-Status (Forge 'base' { param($d) $d.environment['ANTHROPIC_API_KEY']="value:$($script:Sentinel)" })
        Check ((Same $credential $base) -and -not $text.Contains($script:Sentinel) -and -not $output.Contains($script:Sentinel) -and
            (Refused $forged 'credential-field-present') -and -not ($forged.stdout+$forged.stderr).Contains($script:Sentinel)) 'credential-free' "$(Show $credential) status=$($forged.exit) $($forged.stdout)"
        # A-5: a credential name in any letter case, in a nested object or inside a list item, refuses
        # by name (never through the generic schema fault) and never echoes the value.
        $cased=Att (Forge 'base' { param($d) $d.environment['AccessToken']="value:$($script:Sentinel)" })
        $listed=Att (Forge 'base' { param($d) $d.rows[0].observed['APIKEY']=$script:Sentinel })
        $upper=Att (Forge 'base' { param($d) $d['OAUTHACCOUNT']=@{emailAddress=$script:Sentinel} })
        Check ((@($cased,$listed,$upper) | Where-Object { $_.status -ceq 'error' -and $_.error -ceq 'credential-field-present' }).Count -eq 3 -and
            -not ("$($cased.error)$($listed.error)$($upper.error)").Contains($script:Sentinel)) 'credential-case' "$($cased.error) / $($listed.error) / $($upper.error)"
    }
    'C1545_UnknownSettingsRefuseReuse' {
        $att=Att 'unknown-key'; Check (Unknown $att 'settings-not-reusable') 'unknown-key' (Show $att)
        $att=Att 'managed-unverified'; Check (Unknown $att 'source-unverified:managed') 'managed-unverified' (Show $att)
        $att=Att 'unreadable'; Check (Unknown $att 'source-unreadable:user') 'unreadable' (Show $att)
        $att=Att (Forge 'base' { param($d) $d.settings.sources=@($d.settings.sources | Where-Object { $_.kind -cne 'global' }) })
        Check (Unknown $att 'source-missing:global') 'missing-source-entry' (Show $att)
        $att=Att 'additional-source'; Check (Unknown $att 'source-unresolved') 'additional-source' (Show $att)
        $att=Att (Forge 'unreadable' { param($d) $d.settings.reuseEligible=$true })
        Check ((Unknown $att 'source-unreadable:user') -and -not (@($att.reasons) -ccontains 'settings-not-reusable')) 'forged-eligible' (Show $att)
        $att=Att 'runtime-missing'; Check (Unknown $att 'runtime-missing:restoredPackagesDigest') 'runtime-missing' (Show $att)
        $status=Invoke-Status (Forge 'base' { param($d) $d.settings.reuseEligible='true' })
        Check (Refused $status 'attestation-invalid') 'string-boolean' $status.stdout
        $extra=Invoke-Status (Forge 'base' { param($d) $d['extra']=1 })
        $version=Invoke-Status (Forge 'base' { param($d) $d.schemaVersion=2 })
        Check ((Refused $extra 'attestation-invalid') -and (Refused $version 'attestation-invalid')) 'schema' "$($extra.stdout) / $($version.stdout)"
        # A-2: a linked document (file link, or a linked directory) is refused, and so is a document over the size cap.
        $linkedDirectory=Join-Path $ResultsDirectory 'linked-attestations'
        New-DirectoryLink $linkedDirectory $AttestationDirectory
        try { $viaDirectory=Att (Join-Path $linkedDirectory 'base.json') } finally { [IO.Directory]::Delete($linkedDirectory) }
        $viaFile=@{status='error';error='attestation-link'}
        if (-not $IsWindows) {
            $linkedFile=Join-Path $ResultsDirectory 'linked-base.json'
            [void][IO.File]::CreateSymbolicLink($linkedFile,(Join-Path $AttestationDirectory 'base.json'))
            try { $viaFile=Att $linkedFile } finally { [IO.File]::Delete($linkedFile) }
        }
        $copied=Join-Path $ResultsDirectory 'copied-base.json'
        [IO.File]::Copy((Join-Path $AttestationDirectory 'base.json'),$copied)
        Check (($viaDirectory.error -ceq 'attestation-link') -and ($viaFile.error -ceq 'attestation-link') -and ((Att $copied).status -ceq 'complete')) 'linked-document' "$(Show $viaDirectory) / $(Show $viaFile)"
        $large=Join-Path $ResultsDirectory 'large-base.json'
        $text=[IO.File]::ReadAllText((Join-Path $AttestationDirectory 'base.json'))
        [IO.File]::WriteAllText($large,$text+(' '*(1048576-[Text.Encoding]::UTF8.GetByteCount($text)+1)))
        $atCap=Join-Path $ResultsDirectory 'cap-base.json'
        [IO.File]::WriteAllText($atCap,$text+(' '*(1048576-[Text.Encoding]::UTF8.GetByteCount($text))))
        $over=Att $large; $equal=Att $atCap
        Check (($over.error -ceq 'attestation-too-large') -and ($equal.status -ceq 'complete')) 'size-cap' "$(Show $over) / $(Show $equal)"
        # A-3: rows bind to the lane roster. A short document, a foreign uid and an unbound call are
        # unknown; a foreign uid is never echoed.
        $short=Att (Forge 'base' { param($d) $d.rows=@($d.rows[0]) })
        $foreign=Att (Forge 'base' { param($d) $d.rows[0].uid="sk-ant-$($script:Sentinel)"; $d.rows[0].attested.route='path-search' })
        $foreignText=ConvertTo-TestSelectionCanonicalJson $foreign
        $unbound=Unbound 'base'
        Check ((Unknown $short "roster-missing:$($uids[1])") -and (Unknown $short "roster-missing:$($uids[5])") -and
            (Unknown $foreign 'roster-unexpected') -and (Unknown $foreign "roster-missing:$($uids[0])") -and -not $foreignText.Contains($script:Sentinel) -and
            (Unknown $unbound 'roster-unbound') -and ($base.status -ceq 'complete')) 'roster-bound' "$(Show $short) / $(Show $foreign) / $(Show $unbound)"
    }
    'C1545_ExternalEpochIsNotInferred' {
        $now=[DateTimeOffset]::Parse('2026-11-02T12:00:00Z',[Globalization.CultureInfo]::InvariantCulture)
        $epoch=(Doc 'base').external.epoch
        $probe={ param($kind,[int]$age,$nonce='nonce-A',$hostId='host-A',$account='account-A',$result='Ok')
            [ordered]@{kind=$kind;nonce=$nonce;host=$hostId;account=$account;atUtc=$now.AddSeconds(-$age);result=$result} }
        $fresh=@((& $probe 'auth' 30),(& $probe 'control' 30))
        $run={ param([hashtable]$over)
            $arguments=@{ExecutedAtUtc=$now.AddHours(-1);NowUtc=$now;Candidate=@{nonce='nonce-A';host='host-A';account='account-A'}
                Executed=@{epoch=$epoch;host='host-A';account='account-A';receiptId='receipt-A'};Probes=$fresh;Revocations=@();Policy='Bounded24h'}
            foreach ($key in $over.Keys) { if ($null -eq $over[$key] -and $key -ceq 'Policy') { $arguments.Remove('Policy') } else { $arguments[$key]=$over[$key] } }
            try { return Test-LiveLaneExternalFreshness @arguments } catch { return @{reuseEligible=$null;reasons=@();error=$_.Exception.Message} }
        }
        function Refuses($r, [string]$reason) { return ($r.reuseEligible -is [bool]) -and -not $r.reuseEligible -and (@($r.reasons) -ccontains $reason) }
        function Detail($r) { return "eligible=$($r.reuseEligible) reasons=$(@($r.reasons) -join ',') error=$($r.error)" }
        $r=& $run @{Policy=$null}; Check (Refuses $r 'policy-always-rerun') 'always-rerun-default' (Detail $r)
        $r=& $run @{Executed=@{epoch=$null;host='host-A';account='account-A';receiptId='receipt-A'};Probes=@()}
        Check (Refuses $r 'external-epoch-unknown') 'no-epoch' (Detail $r)
        $r=& $run @{ExecutedAtUtc=$now.AddSeconds(-(24*3600-1))}
        Check (($r.reuseEligible -is [bool]) -and $r.reuseEligible -and (@($r.reasons) -ccontains 'd6-bounded-exception') -and ($r.externalProof -is [bool]) -and -not $r.externalProof) 'bounded-fresh' (Detail $r)
        $a=& $run @{ExecutedAtUtc=$now.AddHours(-24)}; $b=& $run @{ExecutedAtUtc=$now.AddSeconds(-(24*3600+1))}
        Check ((Refuses $a 'reuse-expired') -and (Refuses $b 'reuse-expired')) 'expired' "$(Detail $a) / $(Detail $b)"
        $a=& $run @{ExecutedAtUtc=$now.AddSeconds(121)}; $b=& $run @{ExecutedAtUtc=$now.AddSeconds(119)}
        Check ((Refuses $a 'clock-skew') -and ($b.reuseEligible -is [bool]) -and $b.reuseEligible) 'future-skew' "$(Detail $a) / $(Detail $b)"
        $r=& $run @{Probes=@((& $probe 'auth' 30),(& $probe 'control' 61))}; Check (Refuses $r 'probe-stale:control') 'probe-stale' (Detail $r)
        $r=& $run @{Probes=@((& $probe 'auth' 30 'nonce-B'),(& $probe 'control' 30 'nonce-B'))}; Check (Refuses $r 'probe-foreign') 'probe-foreign-nonce' (Detail $r)
        $r=& $run @{Probes=@((& $probe 'auth' 30 'nonce-A' 'host-A' 'account-A' 'AuthExpired'),(& $probe 'control' 30))}; Check (Refuses $r 'probe-failed:auth') 'probe-failed' (Detail $r)
        $r=& $run @{MaxAgeHours=48}; Check (($null -eq $r.reuseEligible) -and ($r.error -ceq 'horizon-not-configurable')) 'horizon-fixed' (Detail $r)
        $a=& $run @{Probes=@((& $probe 'auth' 30 'nonce-A' 'host-B'),(& $probe 'control' 30 'nonce-A' 'host-B'))}
        $b=& $run @{Probes=@((& $probe 'auth' 30 'nonce-A' 'host-A' 'account-B'),(& $probe 'control' 30 'nonce-A' 'host-A' 'account-B'))}
        Check ((Refuses $a 'host-mismatch') -and (Refuses $b 'account-mismatch') -and -not (@($a.reasons) -ccontains 'account-mismatch')) 'host-account' "$(Detail $a) / $(Detail $b)"
        $a=& $run @{Revocations=@(@{epoch=$epoch})}; $b=& $run @{Revocations=@(@{receiptId='receipt-A'})}; $c=& $run @{Revocations=@(@{receiptId='receipt-Z'})}
        Check ((Refuses $a 'revoked') -and (Refuses $b 'revoked') -and $c.reuseEligible) 'incident-revoked' "$(Detail $a) / $(Detail $b) / $(Detail $c)"
        # A-1: a missing nonce, host or account is unknown, never equal (missing == missing is not a binding).
        $bare=@((& $probe 'auth' 30 $null $null $null),(& $probe 'control' 30 $null $null $null))
        $a=& $run @{Candidate=@{};Executed=@{epoch=$epoch};Probes=$bare}
        $b=& $run @{Executed=@{epoch=$epoch;receiptId='receipt-A'}}
        $c=& $run @{Probes=@((& $probe 'auth' 30 'nonce-A' $null 'account-A'),(& $probe 'control' 30))}
        $d=& $run @{Candidate=$null}
        Check ((Refuses $a 'candidate-binding-missing') -and (Refuses $a 'executed-binding-missing') -and (Refuses $a 'probe-binding-missing:auth') -and
            (Refuses $b 'executed-binding-missing') -and -not (@($b.reasons) -ccontains 'candidate-binding-missing') -and
            (Refuses $c 'probe-binding-missing:auth') -and -not (@($c.reasons) -ccontains 'probe-binding-missing:control') -and
            (Refuses $d 'candidate-binding-missing')) 'binding-missing' "$(Detail $a) / $(Detail $b) / $(Detail $c) / $(Detail $d)"
        # A-4: a malformed probe time refuses under a fixed token and never echoes the value.
        $bad=& $probe 'auth' 30; $bad.atUtc="not-a-time-$($script:Sentinel)"
        $r=& $run @{Probes=@($bad,(& $probe 'control' 30))}
        $missing=& $probe 'control' 30; $missing.atUtc=$null
        $m=& $run @{Probes=@((& $probe 'auth' 30),$missing)}
        Check ((Refuses $r 'probe-time-invalid:auth') -and -not (Detail $r).Contains($script:Sentinel) -and (Refuses $m 'probe-time-invalid:control')) 'probe-time-token' "$(Detail $r) / $(Detail $m)"
        # A-6: the executed host and account are compared on their own, with candidate and probes equal.
        $a=& $run @{Executed=@{epoch=$epoch;host='host-B';account='account-A';receiptId='receipt-A'}}
        $b=& $run @{Executed=@{epoch=$epoch;host='host-A';account='account-B';receiptId='receipt-A'}}
        Check ((Refuses $a 'host-mismatch') -and -not (@($a.reasons) -ccontains 'account-mismatch') -and
            (Refuses $b 'account-mismatch') -and -not (@($b.reasons) -ccontains 'host-mismatch')) 'executed-host-account' "$(Detail $a) / $(Detail $b)"
    }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $expectedRows[$Case]
