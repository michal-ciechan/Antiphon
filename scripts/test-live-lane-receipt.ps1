#requires -Version 7.0
# CARD-1545 S2b: live-lane receipt, lane state and use-record witnesses (V-14..V-19 of the S2a/S2b
# test design). Receipts, histories and an owned Git repository (main, an rc line, an ancestor
# chain and a divergent branch) are built here; ancestry is answered by real git.
param([string]$Case, [string]$ResultsDirectory, [string]$AttestationDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/live-lane-receipt.ps1"
$ResultsDirectory=New-C487Root $ResultsDirectory
$script:Lane='fixture-lane'
$script:Method=($Case -replace '^C1545_','')
$script:StatusScript=Join-Path $PSScriptRoot 'live-lane-status.ps1'
$script:Pwsh=(Get-Process -Id $PID).Path
$script:Now=[DateTimeOffset]::Parse('2026-11-02T12:00:00Z',[Globalization.CultureInfo]::InvariantCulture)
# The fixture roster in the manifest's namespace.class.method(arguments) form; one row has an argument.
$script:Roster=@(
    'Antiphon.Agents.Pty.Tests.ClaudeSubmitContractLiveTests.Submitting_via_two_writes_completes_a_turn(claude)'
    'Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Control_auto_connects_a_bridge_session_before_the_first_user'
    'Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Settings_file_prevents_auto_connect_and_keeps_startup_records'
    'Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Remote_control_slash_command_still_arms_after_settings_off'
    'Antiphon.Agents.Pty.Tests.ClaudeRemoteControlMenuCanaryTests.C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt'
    'Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs')
$script:Components=[ordered]@{environment=('e'*64);instructions=('1'*64);laneContract=('2'*64);liveRoots=('3'*64);repositoryClosure=('4'*64)
    runtimeInputs=('5'*64);testSources=('6'*64)}

function Check($ok, [string]$label, [string]$detail='') { Assert-C487 ([bool]$ok) "C1545 $($script:Method)/$label" $detail }
function Iso([DateTimeOffset]$at) { return $at.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ',[Globalization.CultureInfo]::InvariantCulture) }
function Sha([string]$path) { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($path))).ToLowerInvariant() }
function Git([string[]]$arguments, [string]$root=$script:Repo) { return (Invoke-TestSelectionGit $root $arguments).Stdout.Trim() }
function Split-Uid([string]$uid) {
    if ($uid -cnotmatch '^(.+)\.([^.]+)\.([^.(]+)(\((.*)\))?$') { throw "bad roster uid: $uid" }
    $arguments=@(if ($Matches[4]) { $Matches[5] -split ',' })
    return [ordered]@{project='Pty.Tests';namespace=$Matches[1];class=$Matches[2];method=$Matches[3];arguments=$arguments}
}
function Write-Manifest([string]$path, [string[]]$roster) {
    [IO.File]::WriteAllText($path,(ConvertTo-Json -Depth 10 ([ordered]@{format='antiphon-live-lanes-v1';core=@($roster | ForEach-Object { Split-Uid $_ })
        gates=@();additiveContract=@()})))
}

# Owned repository: a lane fixture on main (X0 -> X -> Y), rc/fixture-1 at Y, rc/fixture-0 at X0 and
# a divergent branch D from X0. Y changes only a file outside every lane input.
function New-Repository {
    $root=Join-Path $ResultsDirectory 'receipt repository'
    [void][IO.Directory]::CreateDirectory($root)
    $script:Repo=$root
    $put={ param($path,$text) $full=Join-Path $root $path; [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full)); [IO.File]::WriteAllText($full,$text) }
    foreach ($a in @(@('init','-q','-b','main'),@('config','user.name','Fixture'),@('config','user.email','fixture@example.invalid'),@('config','core.autocrlf','false'))) { [void](Git $a) }
    & $put 'tests/rc-project-categories.json' '{"schemaVersion":1,"projects":{"tests/Pty.Tests/Pty.Tests.csproj":["fixture"]},"batches":["tests/Pty.Tests/Pty.Tests.csproj"]}'
    & $put 'tests/Pty.Tests/Pty.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="TUnit" Version="1.0" /></ItemGroup></Project>'
    & $put 'tests/Pty.Tests/T.cs' "namespace Antiphon.Agents.Pty.Tests;`n"
    & $put 'scripts/lib/live-lane-contract.ps1' "# contract`n"
    & $put 'CLAUDE.md' "# fixture`n"
    & $put 'docs/notes.md' "zero`n"
    Write-Manifest (Join-Path $root 'tests/live-lanes.json') $script:Roster
    & $put 'tests/live-lane-inputs.json' (ConvertTo-Json -Depth 10 ([ordered]@{format='antiphon-live-lane-inputs-v1';lanes=[ordered]@{'fixture-lane'=[ordered]@{
        manifest='tests/live-lanes.json';contractFiles=@('tests/live-lanes.json','tests/live-lane-inputs.json');minimumRoots=@('tests/Pty.Tests/')
        runtimeRoots=@('scripts/');namespaceRoots=@('Antiphon.Agents.Pty');instructionPatterns=@('CLAUDE.md')}}}))
    $commit={ param($text) & $put 'docs/notes.md' $text; [void](Git @('add','--all')); [void](Git @('commit','-qm',$text.Trim())); return Git @('rev-parse','HEAD') }
    $script:X0=& $commit "zero`n"
    $script:X=& $commit "receipt`n"
    $script:Y=& $commit "candidate`n"
    [void](Git @('update-ref','refs/heads/rc/fixture-1',$script:Y)); [void](Git @('update-ref','refs/heads/rc/fixture-0',$script:X0))
    [void](Git @('checkout','-q','-b','divergent',$script:X0))
    $script:D=& $commit "divergent`n"
    [void](Git @('checkout','-q','main'))
    $script:ManifestPath=Join-Path $ResultsDirectory 'manifest.json'
    Write-Manifest $script:ManifestPath $script:Roster
}

function New-Fp($components=$script:Components, [string]$status='complete', [string]$environment='complete') {
    $copy=[ordered]@{}; foreach ($key in $components.Keys) { $copy[$key]=$components[$key] }
    return [ordered]@{status=$status;digest=(Get-LiveLaneFingerprintDigest $script:Lane $copy);components=$copy
        unknownReasons=@($(if ($status -cne 'complete') { if ($environment -cne 'complete') { 'environment-unknown' } else { 'declared-root-empty:x/' } }))
        environment=[ordered]@{status=$environment;reasons=@()}}
}
function New-Candidate([hashtable]$over=@{}) {
    $candidate=@{laneId=$script:Lane;repository='fixture/antiphon';ref='rc/fixture-1';sha=$script:Y;host='host-A';account='account-A';nonce='nonce-A';fingerprint=(New-Fp)}
    foreach ($key in $over.Keys) { $candidate[$key]=$over[$key] }
    return $candidate
}
function New-Receipt([hashtable]$over=@{}, [DateTimeOffset]$ended=$script:Now.AddHours(-1), $fingerprint=(New-Fp)) {
    $rows=@($script:Roster | ForEach-Object { [ordered]@{uid=$_;outcome='Passed'} })
    $doc=[ordered]@{kind='live-lane-execution';schemaVersion=1;laneId=$script:Lane;runId=('run-'+[guid]::NewGuid().ToString('N').Substring(0,12));sequence=1
        authority='diagnostic';repository='fixture/antiphon';ref='rc/fixture-1';sourceSha=$script:X;sourceClean=$true
        host=[ordered]@{enrollmentId='host-A'};account=[ordered]@{enrollmentId='account-A'}
        startedUtc=(Iso $ended.AddMinutes(-5));endedUtc=(Iso $ended);durationMs=300000
        fingerprint=[ordered]@{digest=$fingerprint.digest;components=$fingerprint.components;rosterDigest=(Get-LiveLaneRosterDigest $script:Roster)}
        inputBefore=('a'*64);inputAfter=('a'*64);external=[ordered]@{epoch='epoch-A'}
        planned=6;executed=6;missing=0;passed=6;failed=0;skipped=0;rows=$rows;classifierVerdict=0;finalExit=0;artifacts=@()}
    foreach ($key in $over.Keys) { $doc[$key]=$over[$key] }
    return $doc
}
# Each receipt lives in its own directory with its artifact beside it.
function Write-Receipt($doc, [scriptblock]$edit, [string]$directory) {
    if (-not $directory) { $directory=Join-Path $ResultsDirectory ('r-'+[guid]::NewGuid().ToString('N').Substring(0,12)) }
    [void][IO.Directory]::CreateDirectory($directory)
    $artifact=Join-Path $directory 'console.log'
    if (-not [IO.File]::Exists($artifact)) { [IO.File]::WriteAllText($artifact,"console bytes`n") }
    $doc.artifacts=@([ordered]@{name='console.log';sha256=(Sha $artifact)})
    if ($edit) { & $edit $doc }
    $path=Join-Path $directory ("receipt-{0}.json" -f [guid]::NewGuid().ToString('N').Substring(0,8))
    [IO.File]::WriteAllText($path,(ConvertTo-Json -Depth 30 $doc))
    return $path
}
function Write-Json($value, [string]$name) {
    $path=Join-Path $ResultsDirectory ("$name-{0}.json" -f [guid]::NewGuid().ToString('N').Substring(0,8))
    [IO.File]::WriteAllText($path,(ConvertTo-Json -Depth 30 $value))
    return $path
}
function Receipt([string]$path, [string]$manifest=$script:ManifestPath) {
    try { return Test-LiveLaneReceipt -Receipt $path -Manifest $manifest } catch { return @{valid=$null;reasons=@();error=$_.Exception.Message} }
}
function Probe([string]$kind, [int]$age=30) { return @{kind=$kind;nonce='nonce-A';host='host-A';account='account-A';atUtc=$script:Now.AddSeconds(-$age);result='Ok'} }
function State([string[]]$history, [hashtable]$over=@{}) {
    $arguments=@{Candidate=(New-Candidate);History=$history;Roster=$script:Roster;RepositoryRoot=$script:Repo;ReusePolicy='Bounded24h';NowUtc=$script:Now
        Probes=@((Probe 'auth'),(Probe 'control'));Revocations=@()}
    foreach ($key in $over.Keys) { if ($null -eq $over[$key] -and $key -ceq 'ReusePolicy') { $arguments.Remove('ReusePolicy') } else { $arguments[$key]=$over[$key] } }
    try { $state=Get-LiveLaneState @arguments } catch { return @{state='error';reasons=@();error=$_.Exception.Message} }
    $script:States.Add($state)
    return $state
}
$script:States=[Collections.Generic.List[object]]::new()
function Invalid($r, [string]$reason) { return ($r.valid -is [bool]) -and -not $r.valid -and (@($r.reasons) -ccontains $reason) -and ($r.outcome -cne 'GREEN') }
function Due($s, [string]$reason) { return ($s.state -ceq 'DUE') -and (@($s.reasons) -ccontains $reason) -and ($s.gateCredit -is [bool]) -and -not $s.gateCredit }
function Satisfied($s) { return ($s.state -ceq 'SATISFIED') -and ($s.localCompatibility -ceq 'SATISFIED') -and ($s.gateCredit -is [bool]) -and -not $s.gateCredit }
function Show($r) { return "valid=$($r.valid) state=$($r.state) outcome=$($r.outcome) reasons=$(@($r.reasons) -join ',') error=$($r.error)" }
function Invoke-Status([string[]]$extra) {
    $start=[Diagnostics.ProcessStartInfo]::new($script:Pwsh)
    foreach ($arg in @('-NoProfile','-NonInteractive','-File',$script:StatusScript,'-RepositoryRoot',$script:Repo,'-Lane',$script:Lane,'-CandidateSha',$script:Y)+$extra) { $start.ArgumentList.Add($arg) }
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true; $start.UseShellExecute=$false
    $process=[Diagnostics.Process]::Start($start)
    $stderr=$process.StandardError.ReadToEndAsync()
    $stdout=$process.StandardOutput.ReadToEnd()
    if (-not $process.WaitForExit(45000)) { $process.Kill($true); throw 'status-timeout' }
    $process.WaitForExit()
    $document=$null; try { $document=$stdout | ConvertFrom-Json -AsHashtable } catch { }
    return @{exit=$process.ExitCode;stdout=$stdout;stderr=$stderr.Result;document=$document}
}

$expectedRows=@{C1545_ExactRosterRequired=8;C1545_TypesAndCountsFailClosed=13;C1545_LineageAndHostBound=10;C1545_NewerRedRevokes=8
    C1545_ReuseIsNotExecution=7;C1545_InputDriftRefuses=8}
if (-not $expectedRows.ContainsKey($Case)) { throw "unknown case: $Case" }
New-Repository
switch ($Case) {
    'C1545_ExactRosterRequired' {
        $r=Receipt (Write-Receipt (New-Receipt))
        Check (($r.valid -is [bool]) -and $r.valid -and ($r.outcome -ceq 'GREEN') -and ($r.code -eq 0)) 'exact' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows=@($d.rows[0..4]); $d.planned=5; $d.executed=5; $d.passed=5 })
        Check (Invalid $r 'roster-incomplete') 'dropped' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows[5]=[ordered]@{uid=$d.rows[0].uid;outcome='Passed'} })
        Check ((Invalid $r 'roster-duplicate') -and -not (@($r.reasons) -ccontains 'counts-mismatch')) 'duplicate' (Show $r)
        $codex=$script:Roster[0] -replace '\(claude\)$','(codex)'
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows=@($d.rows)+@([ordered]@{uid=$codex;outcome='Passed'}); $d.planned=7; $d.executed=7; $d.passed=7 })
        Check (($codex -cne $script:Roster[0]) -and (Invalid $r 'roster-unexpected')) 'extra-expansion' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows[2]=[ordered]@{uid=($script:Roster[2]+'_renamed');outcome='Passed'} })
        Check ((Invalid $r 'roster-unexpected') -and (Invalid $r 'roster-incomplete')) 'renamed' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows=@($d.rows[0..4]); $d.executed=5; $d.missing=1; $d.passed=5 })
        Check ((Invalid $r 'roster-incomplete') -and -not (@($r.reasons) -ccontains 'counts-mismatch')) 'planned-executed' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows[3].outcome='Skipped'; $d.passed=5; $d.skipped=1 })
        Check (($r.valid -is [bool]) -and $r.valid -and ($r.outcome -ceq 'INCONCLUSIVE') -and ($r.code -eq 20)) 'skipped' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows=@(); $d.planned=0; $d.executed=0; $d.passed=0 })
        Check (Invalid $r 'roster-empty') 'zero' (Show $r)
    }
    'C1545_TypesAndCountsFailClosed' {
        $r=Receipt (Write-Receipt (New-Receipt))
        Check (($r.valid -is [bool]) -and $r.valid -and ($r.outcome -ceq 'GREEN') -and ($r.digest -cmatch '^[0-9a-f]{64}$')) 'valid' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt @{sourceClean='true'})); Check (Invalid $r 'type:sourceClean') 'string-bool' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt @{passed='6'})); Check (Invalid $r 'type:passed') 'string-count' (Show $r)
        $a=Receipt (Write-Receipt (New-Receipt @{durationMs=-1})); $b=Receipt (Write-Receipt (New-Receipt @{failed=-1}))
        Check ((Invalid $a 'negative:durationMs') -and (Invalid $b 'negative:failed')) 'negative' "$(Show $a) / $(Show $b)"
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows[5].outcome='Failed' })
        Check (Invalid $r 'counts-mismatch') 'counts-outcomes' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt @{note='x'})); Check (Invalid $r 'field:note') 'unknown-field' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt @{kind='live-lane-use'})); Check (Invalid $r 'kind') 'kind' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt @{schemaVersion=2})); Check (Invalid $r 'schema') 'schema-version' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.fingerprint.digest=$null }); Check (Invalid $r 'required:fingerprint.digest') 'null-required' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt) { param($d) $d.rows[0].outcome='Pass' }); Check (Invalid $r 'outcome-enum') 'outcome-enum' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt @{classifierVerdict=20;finalExit=0})); Check (Invalid $r 'classifier-mismatch') 'classifier-exit' (Show $r)
        $path=Write-Receipt (New-Receipt)
        [IO.File]::AppendAllText((Join-Path (Split-Path -Parent $path) 'console.log'),'x')
        $r=Receipt $path; Check (Invalid $r 'artifact-digest:console.log') 'artifact-digest' (Show $r)
        $r=Receipt (Write-Receipt (New-Receipt @{startedUtc=(Iso $script:Now.AddMinutes(-50));endedUtc=(Iso $script:Now.AddMinutes(-55))}))
        Check (Invalid $r 'time-order') 'time-order' (Show $r)
    }
    'C1545_LineageAndHostBound' {
        $s=State @(Write-Receipt (New-Receipt))
        Check ((Satisfied $s) -and ($s.selected.fingerprintDigest -ceq (New-Fp).digest) -and ($script:X -cne $script:Y)) 'same-line-ancestor' (Show $s)
        $refused={ param($over, [string]$reason) $s=State @(Write-Receipt (New-Receipt $over)); return @((Due $s $reason) -and ($null -eq $s.freshness) -and ($null -eq $s.selected), (Show $s)) }
        $r=& $refused @{repository='other/antiphon'} 'foreign-repository'; Check $r[0] 'foreign-repo' $r[1]
        $r=& $refused @{ref='rc/fixture-0'} 'foreign-ref'; Check $r[0] 'other-line' $r[1]
        $r=& $refused @{sourceSha=$script:D} 'not-ancestor'; Check $r[0] 'not-ancestor' $r[1]
        $r=& $refused @{sourceSha=('d'*40)} 'source-missing'; Check $r[0] 'missing-object' $r[1]
        $r=& $refused @{host=[ordered]@{enrollmentId='host-B'}} 'host-mismatch'; Check $r[0] 'host' $r[1]
        $r=& $refused @{account=[ordered]@{enrollmentId='account-B'}} 'account-mismatch'; Check $r[0] 'account' $r[1]
        # A lane id is part of the fingerprint domain, so the foreign receipt recomputes under its own lane.
        $foreign=New-Receipt @{laneId='other-lane'}
        $foreign.fingerprint.digest=Get-LiveLaneFingerprintDigest 'other-lane' $foreign.fingerprint.components
        $s=State @(Write-Receipt $foreign); Check ((Due $s 'foreign-lane') -and ($null -eq $s.selected)) 'lane' (Show $s)
        $r=& $refused @{sourceClean=$false} 'source-dirty'; Check $r[0] 'dirty' $r[1]
        $trusted=Write-Receipt (New-Receipt @{authority='trusted'})
        $r=Receipt $trusted; $s=State @($trusted)
        Check ((Invalid $r 'issuer-unqualified') -and (Due $s 'history-invalid') -and ($null -eq $s.selected)) 'issuer' "$(Show $r) / $(Show $s)"
    }
    'C1545_NewerRedRevokes' {
        $green=Write-Receipt (New-Receipt @{} $script:Now.AddHours(-2))
        $red=Write-Receipt (New-Receipt @{} $script:Now.AddHours(-1)) { param($d) $d.rows[1].outcome='Failed'; $d.passed=5; $d.failed=1; $d.classifierVerdict=1; $d.finalExit=1 }
        $inconclusive=Write-Receipt (New-Receipt @{} $script:Now.AddMinutes(-30)) { param($d) $d.rows[1].outcome='Skipped'; $d.passed=5; $d.skipped=1; $d.classifierVerdict=20; $d.finalExit=20 }
        $s=State @($green,$red); Check (Due $s 'newer-red') 'green-then-red' (Show $s)
        $s=State @($green,$inconclusive); Check (Due $s 'newer-inconclusive') 'newer-inconclusive' (Show $s)
        $revocation=Write-Json ([ordered]@{kind='live-lane-revocation';schemaVersion=1;receiptDigest=(Sha $green);reason='incident'}) 'revocation'
        $s=State @($green,$revocation); $control=State @($green)
        Check ((Due $s 'revoked') -and (Satisfied $control)) 'revocation' "$(Show $s) / $(Show $control)"
        $olderRed=Write-Receipt (New-Receipt @{} $script:Now.AddHours(-3)) { param($d) $d.rows[1].outcome='Failed'; $d.passed=5; $d.failed=1; $d.classifierVerdict=1; $d.finalExit=1 }
        $s=State @($olderRed,$green); Check ((Satisfied $s) -and ($s.selected.receiptDigest -ceq (Sha $green))) 'red-then-green' (Show $s)
        $other=[ordered]@{}; foreach ($key in $script:Components.Keys) { $other[$key]=$script:Components[$key] }; $other.testSources=('7'*64)
        $foreignRed=Write-Receipt (New-Receipt @{} $script:Now.AddHours(-1) (New-Fp $other)) { param($d) $d.rows[1].outcome='Failed'; $d.passed=5; $d.failed=1; $d.classifierVerdict=1; $d.finalExit=1 }
        $s=State @($green,$foreignRed); Check ((Satisfied $s) -and ($s.selected.receiptDigest -ceq (Sha $green))) 'other-fingerprint-red' (Show $s)
        $use=Write-Json ([ordered]@{kind='live-lane-use';schemaVersion=1;laneId=$script:Lane;candidateSha=$script:Y;receiptDigest=(Sha $green);runId='run-use'
            fingerprintDigest=(New-Fp).digest;reused=$true;executedNow=0;executed=0;passed=0;failed=0;skipped=0;gateCredit=$false;createdUtc=(Iso $script:Now.AddMinutes(-1))}) 'use'
        $s=State @($green,$red,$inconclusive,$use)
        Check (($s.lastGreen -ceq (Sha $green)) -and (Due $s 'newer-inconclusive') -and -not (@($s.reasons) -ccontains 'history-invalid')) 'last-green-only' (Show $s)
        # Recorded time, then sequence: the enumeration order never decides.
        $tie=$script:Now.AddMinutes(-20)
        $first=Write-Receipt (New-Receipt @{sequence=1} $tie); $second=Write-Receipt (New-Receipt @{sequence=2} $tie) { param($d) $d.rows[1].outcome='Failed'; $d.passed=5; $d.failed=1; $d.classifierVerdict=1; $d.finalExit=1 }
        $a=State @($green,$red); $b=State @($red,$green); $c=State @($second,$first); $e=State @($first,$second)
        Check ((Due $a 'newer-red') -and (Due $b 'newer-red') -and ((@($a.reasons) -join ',') -ceq (@($b.reasons) -join ',')) -and (Due $c 'newer-red') -and (Due $e 'newer-red')) 'order-independent' "$(Show $b) / $(Show $c)"
        $run=New-Receipt @{runId='run-shared'} $script:Now.AddHours(-2)
        $one=Write-Receipt $run; $two=Write-Receipt (New-Receipt @{runId='run-shared'} $script:Now.AddHours(-2)) { param($d) $d.rows[1].outcome='Failed'; $d.passed=5; $d.failed=1; $d.classifierVerdict=1; $d.finalExit=1 }
        $s=State @($one,$two); $t=State @($two,$one)
        Check (($s.state -ceq 'error') -and ($s.error -ceq 'contradictory-receipts') -and ($t.error -ceq 'contradictory-receipts')) 'contradictory-duplicate' "$(Show $s) / $(Show $t)"
    }
    'C1545_ReuseIsNotExecution' {
        $receipt=Write-Receipt (New-Receipt)
        $before=Sha $receipt
        $s=State @($receipt)
        $usePath=Join-Path $ResultsDirectory 'use-record.json'
        try { $record=New-LiveLaneUseRecord -State $s -Path $usePath -NowUtc $script:Now } catch { $record=@{error=$_.Exception.Message} }
        $written=if ([IO.File]::Exists($usePath)) { [IO.File]::ReadAllText($usePath) | ConvertFrom-Json -AsHashtable } else { @{} }
        $check=Test-LiveLaneUseRecord $usePath
        Check ((Satisfied $s) -and ($written.kind -ceq 'live-lane-use') -and ($written.executedNow -is [long]) -and ($written.executedNow -eq 0) -and
            ($written.reused -is [bool]) -and $written.reused -and ($written.receiptDigest -ceq $before) -and ($written.runId -ceq $s.selected.runId) -and
            ($written.candidateSha -ceq $script:Y) -and ($written.executed -eq 0) -and ($written.passed -eq 0) -and ($written.gateCredit -is [bool]) -and
            -not $written.gateCredit -and $check.valid) 'use-record' "$(Show $s) record=$($record.error) $(ConvertTo-Json -Compress $written)"
        $after=Sha $receipt; $again=Receipt $receipt
        Check (($before -ceq $after) -and $again.valid -and ($again.digest -ceq $before) -and -not ([IO.File]::ReadAllText($receipt)).Contains('lastUsed') -and
            ($written.receiptDigest -ceq $after)) 'receipt-immutable' "$before $after"
        $s=State @($usePath); Check ((Due $s 'no-execution-receipt') -and ($null -eq $s.selected)) 'no-chained-reuse' (Show $s)
        $forgedDoc=[ordered]@{kind='live-lane-use';schemaVersion=1;laneId=$script:Lane;candidateSha=$script:Y;receiptDigest=$before;runId='run-use'
            fingerprintDigest=(New-Fp).digest;reused=$true;executedNow=6;executed=0;passed=0;failed=0;skipped=0;gateCredit=$false;createdUtc=(Iso $script:Now)}
        $forged=Write-Json $forgedDoc 'forged-use'
        $u=Test-LiveLaneUseRecord $forged; $s=State @($receipt,$forged)
        Check ((-not $u.valid) -and (@($u.reasons) -ccontains 'use-executed') -and (Due $s 'history-invalid')) 'forged-executed' "$(@($u.reasons) -join ',') / $(Show $s)"
        $existing=Join-Path $ResultsDirectory 'existing-use.json'; [IO.File]::WriteAllText($existing,'existing bytes')
        $refusal=$null; try { [void](New-LiveLaneUseRecord -State (State @($receipt)) -Path $existing -NowUtc $script:Now) } catch { $refusal=$_.Exception.Message }
        Check (($refusal -ceq 'use-record-exists') -and ([IO.File]::ReadAllText($existing) -ceq 'existing bytes')) 'no-overwrite' "error=$refusal"
        # Through the status front door: real fingerprint and attestation, default policy, then Bounded24h.
        $attestation=Join-Path $AttestationDirectory 'base.json'
        $fingerprint=Get-LiveLaneFingerprint -RepositoryRoot $script:Repo -Sha $script:Y -Lane $script:Lane -Attestation $attestation
        $historyDirectory=Join-Path $ResultsDirectory 'history'
        [void](Write-Receipt (New-Receipt @{} $script:Now.AddHours(-1) $fingerprint) $null $historyDirectory)
        $context=Write-Json ([ordered]@{repository='fixture/antiphon';ref='rc/fixture-1';host='host-A';account='account-A';nonce='nonce-A';nowUtc=(Iso $script:Now)
            probes=@((Probe 'auth'),(Probe 'control') | ForEach-Object { $_.atUtc=(Iso $_.atUtc); $_ });revocations=@()}) 'context'
        $defaultUse=Join-Path $ResultsDirectory 'default-use.json'; $boundedUse=Join-Path $ResultsDirectory 'bounded-use.json'
        $default=Invoke-Status @('-Attestation',$attestation,'-History',$historyDirectory,'-Context',$context,'-UseRecord',$defaultUse)
        $bounded=Invoke-Status @('-Attestation',$attestation,'-History',$historyDirectory,'-Context',$context,'-UseRecord',$boundedUse,'-ReusePolicy','Bounded24h')
        $s=State @($receipt) @{ReusePolicy=$null}
        Check (($fingerprint.status -ceq 'complete') -and ($default.exit -eq 0) -and ($default.document.state -ceq 'DUE') -and (@($default.document.reasons) -ccontains 'policy-always-rerun') -and
            ($default.document.localCompatibility -ceq 'SATISFIED') -and -not [IO.File]::Exists($defaultUse) -and
            ($bounded.exit -eq 0) -and ($bounded.document.state -ceq 'SATISFIED') -and [IO.File]::Exists($boundedUse) -and (Due $s 'policy-always-rerun')) 'policy-default' "$($default.stdout) / $($bounded.stdout)"
        foreach ($doc in @($default.document,$bounded.document)) { $script:States.Add($doc) }
        $other=@($script:States | Where-Object { $_.state -cne 'error' })
        Check (($other.Count -ge 7) -and (@($other | Where-Object { $_.state -ceq 'SATISFIED' }).Count -ge 2) -and (@($other | Where-Object { $_.state -ceq 'DUE' }).Count -ge 3) -and
            -not @($other | Where-Object { -not (($_.gateCredit -is [bool]) -and -not $_.gateCredit -and ($_.effectiveMode -ceq 'Full') -and ($_.activation -ceq 'shadow-only')) }).Count) 'gate-credit' "states=$($other.Count)"
    }
    'C1545_InputDriftRefuses' {
        $receipt=Write-Receipt (New-Receipt)
        $s=State @($receipt)
        Check ((Satisfied $s) -and ($s.reuseEligible -is [bool]) -and $s.reuseEligible -and ($s.freshness.reasons -ccontains 'd6-bounded-exception')) 'equal-all' (Show $s)
        $changed=[ordered]@{}; foreach ($key in $script:Components.Keys) { $changed[$key]=$script:Components[$key] }; $changed.testSources=('9'*64)
        $s=State @($receipt) @{Candidate=(New-Candidate @{fingerprint=(New-Fp $changed)})}
        Check ((Due $s 'fingerprint-changed:testSources') -and -not (@($s.reasons) -ccontains 'fingerprint-changed:laneContract')) 'fingerprint-unequal' (Show $s)
        $tampered=Write-Receipt (New-Receipt) { param($d) $d.fingerprint.components.testSources=('9'*64) }
        $r=Receipt $tampered; Check (Invalid $r 'fingerprint-tamper') 'component-tamper' (Show $r)
        $roster=@($script:Roster[0..4])+@($script:Roster[5]+'_v2')
        $s=State @($receipt) @{Roster=$roster}; Check ((Due $s 'roster-changed') -and ($null -eq $s.selected)) 'roster-changed' (Show $s)
        $unknown=New-Fp $script:Components 'unknown'
        $s=State @($receipt) @{Candidate=(New-Candidate @{fingerprint=$unknown})}
        Check (($unknown.digest -ceq (New-Fp).digest) -and (Due $s 'unknown-input') -and ($null -eq $s.selected) -and ($null -eq $s.lastGreen)) 'unknown-input' (Show $s)
        $drift=Write-Receipt (New-Receipt @{inputAfter=('b'*64)}); $r=Receipt $drift; $s=State @($drift)
        Check ((Invalid $r 'input-drift') -and ($s.state -ceq 'DUE')) 'pre-post' "$(Show $r) / $(Show $s)"
        $s=State @($receipt) @{Candidate=(New-Candidate @{fingerprint=(New-Fp $script:Components 'unknown' 'unknown')})}
        Check (Due $s 'environment-unknown') 'attestation-unknown' (Show $s)
        $stale=Write-Receipt (New-Receipt @{} $script:Now.AddSeconds(-(24*3600+1)))
        $s=State @($stale); $fresh=State @(Write-Receipt (New-Receipt @{} $script:Now.AddSeconds(-(24*3600-1))))
        Check ((Due $s 'reuse-expired') -and ($s.localCompatibility -ceq 'SATISFIED') -and (Satisfied $fresh)) 'stale' "$(Show $s) / $(Show $fresh)"
    }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $expectedRows[$Case]
