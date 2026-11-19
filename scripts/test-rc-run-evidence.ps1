#requires -Version 7.0
param([string]$Case,[string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/test-selection.ps1"
. "$PSScriptRoot/lib/rc-run-evidence.ps1"
$errors=[Collections.Generic.List[string]]::new()
function Note($ok,[string]$label) { if (-not $ok) { $errors.Add($label) } }
function Refuses([scriptblock]$body,[string]$reason) {
    try { & $body | Out-Null; Note $false ('accepted:'+ $reason) }
    catch { Note ($_.Exception.Message.Contains($reason)) ('wrong-refusal:'+ $reason+':'+$_.Exception.Message) }
}
function New-Repo([string]$Root) {
    [void][IO.Directory]::CreateDirectory($Root)
    [void](Invoke-C487FixtureGit $Root @('init','-q','-b','main'))
    [void](Invoke-C487FixtureGit $Root @('config','user.name','Fixture'))
    [void](Invoke-C487FixtureGit $Root @('config','user.email','fixture@example.invalid'))
    [void][IO.Directory]::CreateDirectory((Join-Path $Root 'src'))
    [IO.File]::WriteAllText((Join-Path $Root 'src/input.txt'),'input')
    [void](Invoke-C487FixtureGit $Root @('add','src/input.txt'))
    [void](Invoke-C487FixtureGit $Root @('commit','-qm','fixture'))
    return (Invoke-C487FixtureGit $Root @('rev-parse','HEAD'))
}
function New-Outputs([string]$Directory,[string]$Entry,[string]$Helper) {
    [void][IO.Directory]::CreateDirectory($Directory)
    [IO.File]::WriteAllBytes((Join-Path $Directory $Entry),[byte[]](1,2,3,4))
    [IO.File]::WriteAllBytes((Join-Path $Directory $Helper),[byte[]](5,6,7,8))
    [IO.File]::WriteAllText((Join-Path $Directory 'entry.deps.json'),'{"helper":true}')
}
if (-not $ResultsDirectory) { $ResultsDirectory=Join-Path ([IO.Path]::GetTempPath()) ('re-'+[guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
Remove-Item Env:ANTIPHON_RC_RUN_EVIDENCE -ErrorAction SilentlyContinue
try {
    switch ($Case) {
        'BuildSourceAndLaunchOutputMustJoin' {
            $repo=Join-Path $ResultsDirectory 'repo'
            $ev=Join-Path $ResultsDirectory 'evidence'
            $out=Join-Path $ResultsDirectory 'out'
            $foreign=Join-Path $ResultsDirectory 'foreign'
            $entryOnly=Join-Path $ResultsDirectory 'entry-only'
            [void][IO.Directory]::CreateDirectory($ev)
            $sha=New-Repo $repo
            New-Outputs $out 'entry.dll' 'helper.dll'
            [void][IO.Directory]::CreateDirectory($entryOnly)
            [IO.File]::Copy((Join-Path $out 'entry.dll'),(Join-Path $entryOnly 'entry.dll'))
            New-Outputs $foreign 'entry.dll' 'helper.dll'
            $snap=New-RcSourceSnapshot -Repository $repo -EvidenceRoot $ev -InputPaths @('src/input.txt') -ExpectedSha $sha -Strict
            Note ($snap.effectiveMode -ceq 'Full' -and -not $snap.gateCredit -and -not $snap.coverageComplete -and $snap.bindingState -ceq 'diagnostic-only' -and -not $snap.publish) 'shadow-authority'
            $build=Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'server' -Command 'dotnet build server.csproj -c Debug' -OutputDirectory $out -Entry 'entry.dll' -SourceDigest $snap.digest -Strict
            $names=@($build.outputs | ForEach-Object { [string]$_.relativePath })
            Note ($names -contains 'entry.dll' -and $names -contains 'helper.dll' -and $names -contains 'entry.deps.json') 'build-covers-helpers'
            Note ($build.effectiveMode -ceq 'Full' -and -not $build.gateCredit -and -not $build.publish) 'build-grants-no-credit'
            $launch=Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'server' -Chunk 'c1' -OutputDirectory $out -Arguments @('test') -BuildDigest $build.digest -Repository $repo -ExpectedSha $sha -Strict
            Note ($launch.joined -and $launch.effectiveMode -ceq 'Full' -and -not $launch.gateCredit -and -not $launch.publish) 'launch-joins-build'
            Refuses { Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'server' -Chunk 'c2' -OutputDirectory $entryOnly -Arguments @('test') -BuildDigest $build.digest -Repository $repo -ExpectedSha $sha -Strict } 'output-not-joined'
            Refuses { Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'server' -Chunk 'c3' -OutputDirectory $foreign -Arguments @('--no-build') -BuildDigest $build.digest -Repository $repo -ExpectedSha $sha -Strict } 'foreign-output'
            $before=@(Get-ChildItem -LiteralPath $ev -Recurse -File).Count
            $env:ANTIPHON_RC_RUN_EVIDENCE='off'
            try {
                $off=Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'server' -Chunk 'c4' -OutputDirectory $out -Arguments @('test') -BuildDigest $build.digest -Strict
                Note ($off.recording -ceq 'off' -and $off.effectiveMode -ceq 'Full' -and -not $off.gateCredit -and -not $off.publish) 'off-switch-stays-full'
                $after=@(Get-ChildItem -LiteralPath $ev -Recurse -File).Count
                Note ($after -eq $before) 'off-switch-writes-nothing'
            } finally { Remove-Item Env:ANTIPHON_RC_RUN_EVIDENCE }
        }
        'ChangedInputsDuringRunRefuse' {
            $repo=Join-Path $ResultsDirectory 'repo'
            $ev=Join-Path $ResultsDirectory 'evidence'
            $out=Join-Path $ResultsDirectory 'out'
            [void][IO.Directory]::CreateDirectory($ev)
            $sha=New-Repo $repo
            New-Outputs $out 'entry.dll' 'helper.dll'
            $snap=New-RcSourceSnapshot -Repository $repo -EvidenceRoot $ev -InputPaths @('src/input.txt') -ExpectedSha $sha -Strict
            $build=Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'server' -Command 'dotnet build server.csproj -c Debug' -OutputDirectory $out -Entry 'entry.dll' -SourceDigest $snap.digest -Strict
            [IO.File]::WriteAllText((Join-Path $repo 'src/input.txt'),'changed')
            Refuses { Assert-RcRunInputsCurrent -Repository $repo -EvidenceRoot $ev -ExpectedSha $sha -Strict } 'inputs-changed'
            Refuses { Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'server' -Chunk 'c1' -OutputDirectory $out -Arguments @('test') -BuildDigest $build.digest -Repository $repo -ExpectedSha $sha -Strict } 'inputs-changed'
            Note ($snap.effectiveMode -ceq 'Full' -and -not $snap.gateCredit -and -not $snap.publish) 'changed-input-still-no-credit'
        }
        'InterruptedWriterLeavesNotRun' {
            $repo=Join-Path $ResultsDirectory 'repo'
            $ev=Join-Path $ResultsDirectory 'evidence'
            $out=Join-Path $ResultsDirectory 'out'
            [void][IO.Directory]::CreateDirectory($ev)
            $sha=New-Repo $repo
            New-Outputs $out 'entry.dll' 'helper.dll'
            $snap=New-RcSourceSnapshot -Repository $repo -EvidenceRoot $ev -InputPaths @('src/input.txt') -ExpectedSha $sha -Strict
            [void](Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'server' -Command 'dotnet build server.csproj -c Debug' -OutputDirectory $out -Entry 'entry.dll' -SourceDigest $snap.digest -Strict)
            $open=Register-NightlyRcChunkRevision -EvidenceRoot $ev -Suite 'server' -Chunk 'a' -Outcome 'Passed' -Strict
            $read=Read-RcRunEvidence -EvidenceRoot $ev -Strict
            Note ($open.state -ceq 'open' -and $read.sequence -eq 1 -and $read.state -ceq 'open' -and $read.pending -ceq 'none' -and $read.effectiveMode -ceq 'Full' -and -not $read.gateCredit -and -not $read.publish) 'open-revision-readable'
            Refuses { Register-NightlyRcChunkRevision -EvidenceRoot $ev -Suite 'server' -Chunk 'b' -Interrupt -Strict } 'writer-interrupted'
            $read=Read-RcRunEvidence -EvidenceRoot $ev -Strict
            Note ($read.sequence -eq 1 -and $read.state -ceq 'open' -and $read.pending -ceq 'not-run' -and -not $read.gateCredit -and $read.effectiveMode -ceq 'Full') 'interrupt-leaves-not-run'
            $index=Join-Path $ev 'index.json'
            $forged=Get-Content -LiteralPath $index -Raw | ConvertFrom-Json -AsHashtable
            $forged.state='complete'
            $forged.gateCredit=$true
            [IO.File]::WriteAllText($index,($forged | ConvertTo-Json -Compress))
            Refuses { Read-RcRunEvidence -EvidenceRoot $ev -Strict } 'closed-green-refused'
        }
        'OneBuildAndDiscoveryPerSuite' {
            $repo=Join-Path $ResultsDirectory 'repo'
            $ev=Join-Path $ResultsDirectory 'evidence'
            $out=Join-Path $ResultsDirectory 'out'
            $client=Join-Path $ResultsDirectory 'client-out'
            [void][IO.Directory]::CreateDirectory($ev)
            $sha=New-Repo $repo
            New-Outputs $out 'entry.dll' 'helper.dll'
            New-Outputs $client 'client.dll' 'helper.dll'
            $snap=New-RcSourceSnapshot -Repository $repo -EvidenceRoot $ev -InputPaths @('src/input.txt') -ExpectedSha $sha -Strict
            [void](Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'server' -Command 'dotnet build server.csproj -c Debug' -OutputDirectory $out -Entry 'entry.dll' -SourceDigest $snap.digest -Strict)
            Refuses { Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'server' -Command 'dotnet build server.csproj -c Debug' -OutputDirectory $out -Entry 'entry.dll' -SourceDigest $snap.digest -Strict } 'build-already-recorded'
            $discovered=Register-NightlyRcDiscoveryEvidence -EvidenceRoot $ev -Suite 'server' -Carry -Strict
            Note ($discovered.skipped -eq $false -and $discovered.effectiveMode -ceq 'Full' -and -not $discovered.gateCredit -and -not $discovered.publish) 'carry-still-discovers'
            Refuses { Register-NightlyRcDiscoveryEvidence -EvidenceRoot $ev -Suite 'server' -Carry -Skip -Strict } 'carry-cannot-skip-discovery'
            Refuses { Register-NightlyRcDiscoveryEvidence -EvidenceRoot $ev -Suite 'server' -Strict } 'discovery-already-recorded'
            $other=Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'client' -Command 'dotnet build client.csproj -c Debug' -OutputDirectory $client -Entry 'client.dll' -SourceDigest $snap.digest -Strict
            Note ($other.suite -ceq 'client' -and $other.effectiveMode -ceq 'Full' -and -not $other.gateCredit) 'other-suite-one-build'
            $testsImpl=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-tests-impl.ps1'))
            $runImpl=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1'))
            Note ($testsImpl.Contains('Register-NightlyRcBuildEvidence') -and $testsImpl.Contains('Register-NightlyRcDiscoveryEvidence') -and $testsImpl.Contains('Register-NightlyRcLaunchEvidence') -and $testsImpl.Contains('Register-NightlyRcChunkRevision')) 'tests-impl-wires-evidence'
            Note ($runImpl.Contains('Protect-NightlyOwnedCheckoutEvidence')) 'run-impl-wires-checkout'
            Note ($testsImpl -notmatch 'publish-release\.ps1' -and $runImpl -notmatch 'Incremental') 'wiring-adds-no-mode-or-publish'
        }
        default { throw 'unknown-case' }
    }
} catch { Note $false ('unexpected:'+ $_.Exception.Message) }
Assert-C487 ($errors.Count -eq 0) ("RE $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
