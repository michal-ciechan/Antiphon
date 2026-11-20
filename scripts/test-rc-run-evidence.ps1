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
function Write-RcOutcomeTrx {
    param([string]$Path,[string]$SummaryOutcome,[int]$Total,[int]$Executed,[int]$Passed,[int]$Failed,[int]$NotExecuted,[object[]]$Rows)
    $defs=New-Object System.Text.StringBuilder
    $results=New-Object System.Text.StringBuilder
    [void]$defs.Append('<TestDefinitions>')
    [void]$results.Append('<Results>')
    foreach($r in @($Rows)){
        $id=[string]$r.Id
        $cls=[string]$r.ClassName
        $method=[string]$r.MethodName
        $outcome=[string]$r.Outcome
        $trace=[string]$r.DebugTrace
        [void]$defs.Append(('<UnitTest id="{0}" name="{1}.{2}"><TestMethod className="{1}" name="{2}" /></UnitTest>' -f $id,$cls,$method))
        if([string]::IsNullOrWhiteSpace($trace)){
            [void]$results.Append(('<UnitTestResult testId="{0}" testName="{1}.{2}" outcome="{3}" />' -f $id,$cls,$method,$outcome))
        } else {
            [void]$results.Append(('<UnitTestResult testId="{0}" testName="{1}.{2}" outcome="{3}"><Output><DebugTrace>{4}</DebugTrace></Output></UnitTestResult>' -f $id,$cls,$method,$outcome,$trace))
        }
    }
    [void]$defs.Append('</TestDefinitions>')
    [void]$results.Append('</Results>')
    $summary=('<ResultSummary outcome="{0}"><Counters total="{1}" executed="{2}" passed="{3}" failed="{4}" notExecuted="{5}" /></ResultSummary>' -f $SummaryOutcome,$Total,$Executed,$Passed,$Failed,$NotExecuted)
    $xml=('<?xml version="1.0" encoding="utf-8"?><TestRun>{0}{1}{2}</TestRun>' -f $defs.ToString(),$results.ToString(),$summary)
    [IO.File]::WriteAllText($Path,$xml)
}
function Test-RcRealResultsWouldPass {
    param([string]$Path)
    try {
        $text=[IO.File]::ReadAllText($Path)
        if($text -notmatch '<TestRun[\s>]'){ return $false }
        $summary=Read-NightlyTrxRunSummary -Path $Path
        if(-not $summary.Present -or [string]::IsNullOrWhiteSpace([string]$summary.Outcome) -or $summary.Outcome -cne 'Completed'){ return $false }
        $doc=New-Object System.Xml.XmlDocument
        $doc.XmlResolver=$null
        $doc.Load($Path)
        $counters=$doc.SelectSingleNode('//*[local-name()="Counters"]')
        if($null -eq $counters){ return $false }
        if([string]::IsNullOrWhiteSpace($counters.GetAttribute('executed')) -or [string]::IsNullOrWhiteSpace($counters.GetAttribute('failed'))){ return $false }
        $rows=@(Read-NightlyTrxIdentities -TrxPath $Path)
        $verdict=ConvertTo-NightlyCoverageVerdict -TerminalRows $rows -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p' -Platform 'linux' -ExpectedSkipRoster @()
        $executedRows=[int]$verdict.passed + [int]$verdict.failed
        if([int]$summary.Executed -ne $executedRows -or [int]$summary.Failed -ne [int]$verdict.failed){ return $false }
        if(-not (Test-NightlyCounterAgreement -Verdict $verdict -ReportedTotal @($rows).Count -ReportedFailed ([int]$summary.Failed))){ return $false }
        if(-not [bool]$verdict.testsPassed -or -not [bool]$verdict.coverageComplete -or [int]$summary.Failed -ne 0){ return $false }
        return $true
    } catch { return $false }
}
function Note-RcShadowNotWeaker {
    param([string]$Label,[string]$Path)
    $shadow=Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $Path
    $real=Test-RcRealResultsWouldPass -Path $Path
    if($real){ Note $false ($Label + ' real-did-not-fail') }
    elseif($shadow.Outcome -ceq 'Passed'){ Note $false ($Label + ' shadow=Passed') }
    else { Note $true $Label }
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
        'OneEvidenceRootPerRun' {
            . (Join-Path $PSScriptRoot 'lib/nightly-policy.ps1')
            . (Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1')
            $root = Join-Path $ResultsDirectory 'e2e'
            $repo = Join-Path $root 'checkout'
            $state = Join-Path $root 'state'
            $logs = Join-Path $root 'logs'
            $coord = Join-Path $root 'coord'
            foreach ($dir in @($state, $logs, $coord)) { [void][IO.Directory]::CreateDirectory($dir) }
            $null = New-Repo $repo
            $probeProject = Join-Path $repo 'tests/Probe'
            [void][IO.Directory]::CreateDirectory($probeProject)
            [IO.File]::WriteAllText((Join-Path $probeProject 'Probe.csproj'), "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>`n")
            $policyObject = [pscustomobject]@{
                schemaVersion = 1
                policyHash = 'pending'
                defaultSuites = @('probe')
                suites = [pscustomobject]@{ probe = [pscustomobject]@{ name = 'Probe'; mode = 'native'; assembly = 'Probe'; project = 'tests/Probe/Probe.csproj' } }
                projects = @('tests/Probe/Probe.csproj')
            }
            $policyObject.policyHash = [string](Get-NightlyPolicyHash -Object $policyObject)
            $policyPath = Join-Path $repo 'tests/test-execution-policy.json'
            [IO.File]::WriteAllText($policyPath, ($policyObject | ConvertTo-Json -Depth 6))
            $reread = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
            Note ((Get-NightlyPolicyHash -Object $reread) -ceq [string]$reread.policyHash) 'policy-hash'
            [void](Invoke-C487FixtureGit $repo @('add', 'tests/Probe/Probe.csproj', 'tests/test-execution-policy.json'))
            [void](Invoke-C487FixtureGit $repo @('commit', '-qm', 'probe'))
            $sha = [string](Invoke-C487FixtureGit $repo @('rev-parse', 'HEAD'))
            [void](Invoke-C487FixtureGit $repo @('remote', 'add', 'origin', 'https://github.com/michal-ciechan/Antiphon'))
            [void](Invoke-C487FixtureGit $repo @('config', 'core.longpaths', 'true'))
            $scripts = Join-Path $repo 'scripts'
            [void][IO.Directory]::CreateDirectory($scripts)
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'nightly-tests.ps1') -Destination (Join-Path $scripts 'nightly-tests.ps1') -Force
            $libFrom = Join-Path $PSScriptRoot 'lib'
            $libTo = Join-Path $scripts 'lib'
            [void][IO.Directory]::CreateDirectory($libTo)
            foreach ($item in @(Get-ChildItem -LiteralPath $libFrom -Force)) {
                if ($item.Name -eq 'bin' -or $item.Name -eq 'obj') { continue }
                Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $libTo $item.Name) -Recurse -Force
            }
            Copy-C487NightlyCoreDependency -Root $repo
            $out = Join-Path $repo 'tests/Probe/bin/Debug/net9.0'
            New-Outputs $out 'Probe.dll' 'helper.dll'
            $marker = [ordered]@{ origin = 'https://github.com/michal-ciechan/Antiphon'; kind = 'nightly-clone' }
            [IO.File]::WriteAllText((Join-Path $repo '.antiphon-nightly-owned'), ($marker | ConvertTo-Json -Compress))
            $probe = Join-Path $PSScriptRoot 'fixtures/nightly/c487-probe'
            $trace = Join-Path $root 'trace.log'
            $childLog = Join-Path $root 'child.log'
            $faultBin = Join-Path $root 'fault-bin'
            [void][IO.Directory]::CreateDirectory($faultBin)
            $gitStub = Join-Path $faultBin 'git'
            [IO.File]::WriteAllText($gitStub, "#!/bin/sh`nexit 1`n")
            [IO.File]::WriteAllText((Join-Path $faultBin 'git.cmd'), "@echo off`r`nexit /b 1`r`n")
            if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
                & chmod 755 $gitStub
            }
            $seamPath = Join-Path $root 'seams.ps1'
            $seam = @"
`$NightlySeams = @{
    TracePath = '$trace'
    Git = {
        param(`$WorkingDirectory, `$Arguments)
        if (`$Arguments -contains '--get' -and `$Arguments -contains 'core.longpaths') { return @{ ExitCode = 0; Output = 'true' } }
        if (`$Arguments -contains 'rev-parse') { return @{ ExitCode = 0; Output = '$sha' } }
        return @{ ExitCode = 0; Output = '' }
    }
    StartProcess = {
        param(`$FilePath, `$ArgumentList, `$WorkingDirectory, `$TimeoutMilliseconds, `$Environment)
        `$joined = @(`$ArgumentList) -join ' '
        Add-Content -LiteralPath '$trace' -Value (('EXEC {0} {1}' -f `$FilePath, `$joined)) -Encoding ASCII
        if (`$joined -match 'nightly-tests\.ps1') {
            `$psi = New-Object System.Diagnostics.ProcessStartInfo
            `$psi.FileName = 'pwsh'
            `$psi.WorkingDirectory = `$WorkingDirectory
            `$psi.UseShellExecute = `$false
            `$psi.RedirectStandardOutput = `$true
            `$psi.RedirectStandardError = `$true
            if (`$env:S6B_FAULT_CHILD_GIT -eq '1') {
                `$psi.Environment['PATH'] = '$faultBin' + [IO.Path]::PathSeparator + [string]`$psi.Environment['PATH']
            }
            foreach (`$arg in @(`$ArgumentList)) { [void]`$psi.ArgumentList.Add([string]`$arg) }
            `$proc = [System.Diagnostics.Process]::Start(`$psi)
            `$outTask = `$proc.StandardOutput.ReadToEndAsync()
            `$errTask = `$proc.StandardError.ReadToEndAsync()
            `$proc.WaitForExit()
            `$text = `$outTask.GetAwaiter().GetResult() + [Environment]::NewLine + `$errTask.GetAwaiter().GetResult()
            Add-Content -LiteralPath '$childLog' -Value `$text -Encoding UTF8
            return @{ ExitCode = [int]`$proc.ExitCode; TimedOut = `$false; Pid = `$proc.Id; ChildrenExited = `$true; CleanupComplete = `$true; OutputDrainTimedOut = `$false; LogText = '' }
        }
        `$list = @(`$ArgumentList)
        function Take([string]`$name) {
            `$index = [array]::IndexOf(`$list, `$name)
            if (`$index -ge 0 -and (`$index + 1) -lt `$list.Count) { return [string]`$list[`$index + 1] }
            return ''
        }
        if (`$list -contains '--list-tests') {
            `$diag = Take '--diagnostic-output-directory'
            if (`$diag) {
                New-Item -ItemType Directory -Path `$diag -Force | Out-Null
                Copy-Item -LiteralPath '$probe/discovery.diag' -Destination (Join-Path `$diag 'discovery.diag') -Force
            }
        } elseif (`$list -contains '--report-trx') {
            `$diag = Take '--diagnostic-output-directory'
            if (`$diag) {
                New-Item -ItemType Directory -Path `$diag -Force | Out-Null
                Copy-Item -LiteralPath '$probe/execution.diag' -Destination (Join-Path `$diag 'execution.diag') -Force
            }
            `$results = Take '--results-directory'
            `$trx = Take '--report-trx-filename'
            if (`$results -and `$trx) {
                New-Item -ItemType Directory -Path `$results -Force | Out-Null
                Copy-Item -LiteralPath '$probe/all.trx' -Destination (Join-Path `$results `$trx) -Force
            }
        }
        `$stamp = 'C487_INVOCATION fake'
        if (`$Environment -and `$Environment.Contains('C487_INVOCATION')) { `$stamp = 'C487_INVOCATION=' + [string]`$Environment['C487_INVOCATION'] }
        return @{ ExitCode = 0; TimedOut = `$false; Pid = 2; ChildrenExited = `$true; CleanupComplete = `$true; OutputDrainTimedOut = `$false; LogText = `$stamp }
    }
    IsProcessAlive = { param(`$ProcessId) return (`$ProcessId -gt 0) }
    DockerInfo = { return @{ exitCode = 0; version = 'fake'; detail = 'ok' } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    ToolchainVersions = { param(`$tool) if (`$tool -eq 'git') { 'git version 2.42.0' } else { '10.0.204 [/fake/sdk]' } }
}
"@
            Set-Content -LiteralPath $seamPath -Value $seam -Encoding ASCII
            $savedEvidence = $env:ANTIPHON_RC_RUN_EVIDENCE
            $env:ANTIPHON_RC_RUN_EVIDENCE = 'record'
            try {
                $common = @{
                    NoSync = $true; NoReport = $true; CheckoutRoot = $repo; StateRoot = $state; LogRoot = $logs
                    CoordinationRoot = $coord; RemoteUrl = 'https://github.com/michal-ciechan/Antiphon'; Ref = 'master'
                    Trigger = 'scheduled'; Profile = 'nightly'; SelectionMode = 'Full'; SeamsPath = $seamPath
                    Suites = @('probe'); Repository = 'michal-ciechan/Antiphon'
                }
                $first = Invoke-AntiphonNightlyRunCore @common -RunId 's6bfol01'
                $second = Invoke-AntiphonNightlyRunCore @common -RunId 's6bfol02'
            } finally {
                if ([string]::IsNullOrWhiteSpace($savedEvidence)) { Remove-Item Env:ANTIPHON_RC_RUN_EVIDENCE -ErrorAction SilentlyContinue }
                else { $env:ANTIPHON_RC_RUN_EVIDENCE = $savedEvidence }
            }
            function Read-RunEvidence([string]$RunDir) {
                $evidence = Join-Path $RunDir 'rc-run-evidence'
                $join = $null
                $launchRoot = Join-Path $evidence 'launches'
                if (Test-Path -LiteralPath $launchRoot) {
                    $launchFile = @(Get-ChildItem -LiteralPath $launchRoot -Recurse -Filter '*.json' | Select-Object -First 1)
                    if ($launchFile.Count -eq 1) { $join = Get-Content -LiteralPath $launchFile[0].FullName -Raw | ConvertFrom-Json }
                }
                $source = $null
                $sourcePath = Join-Path $evidence 'source.json'
                if (Test-Path -LiteralPath $sourcePath) { $source = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json }
                $revision = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'revisions') -Recurse -Filter 'revision.json' -ErrorAction SilentlyContinue)
                $discovery = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'discovery') -Filter '*.json' -ErrorAction SilentlyContinue)
                return [pscustomobject]@{ Evidence = $evidence; Join = $join; Source = $source; Revisions = @($revision); Discovery = @($discovery) }
            }
            $firstDir = [string]$first.LogDir
            $secondDir = [string]$second.LogDir
            $detail = ('first={0}/{1} second={2}/{3}' -f $first.Phase, $first.ExitCode, $second.Phase, $second.ExitCode)
            if (Test-Path -LiteralPath $childLog) {
                $tail = Get-Content -LiteralPath $childLog -Tail 40 -ErrorAction SilentlyContinue
                Write-Host ($tail -join [Environment]::NewLine)
            }
            Write-Host $detail
            Note (-not [string]::IsNullOrWhiteSpace($firstDir)) ('run-dir:' + $detail)
            $one = $null
            $two = $null
            if (-not [string]::IsNullOrWhiteSpace($firstDir)) { $one = Read-RunEvidence $firstDir }
            if (-not [string]::IsNullOrWhiteSpace($secondDir)) { $two = Read-RunEvidence $secondDir }
            $joined = ($null -ne $one -and $null -ne $one.Join -and [bool]$one.Join.joined)
            Note $joined 'missing-join-record'
            $inputs = @()
            if ($null -ne $one -and $null -ne $one.Source -and $null -ne $one.Source.inputs) { $inputs = @($one.Source.inputs) }
            Note ($inputs.Count -ge 1 -and [string]$inputs[0].path -eq 'tests/test-execution-policy.json') 'changed-input-snapshot'
            Note ($null -ne $one -and @($one.Revisions).Count -ge 1) 'chunk-revision'
            Note ($null -ne $one -and @($one.Discovery).Count -ge 1) 'discovery-revision'
            Note ($null -ne $one -and $null -ne $two -and [string]$one.Evidence -ne [string]$two.Evidence) 'second-run-own-root'
            $secondRefused = ($null -eq $second) -or (-not [string]::IsNullOrWhiteSpace([string]$second.Refusal))
            Note ((-not $secondRefused) -and $null -ne $two -and $null -ne $two.Source) 'second-run-refused'
            if ($null -ne $one -and $null -ne $one.Join) {
                Note ([string]$one.Join.effectiveMode -ceq 'Full' -and -not [bool]$one.Join.gateCredit) 'shadow-stays-full'
            }
            $offRoot = Join-Path $root 'off-evidence'
            $before = 0
            if (Test-Path -LiteralPath $offRoot) { $before = @(Get-ChildItem -LiteralPath $offRoot -Recurse -File -ErrorAction SilentlyContinue).Count }
            $env:ANTIPHON_RC_RUN_EVIDENCE = 'off'
            try {
                $off = Protect-NightlyOwnedCheckoutEvidence -Phase 'admission' -CheckoutRoot $repo -EvidenceRoot $offRoot -Sha $sha
                Note ($off.recording -ceq 'off' -and $off.effectiveMode -ceq 'Full' -and -not $off.gateCredit) 'off-switch-stays-full'
            } finally { $env:ANTIPHON_RC_RUN_EVIDENCE = 'record' }
            $after = 0
            if (Test-Path -LiteralPath $offRoot) { $after = @(Get-ChildItem -LiteralPath $offRoot -Recurse -File -ErrorAction SilentlyContinue).Count }
            Note ($after -eq $before) 'off-switch-writes-nothing'
            $warningPattern = 'rc-run-evidence: git-failed'
            $warningsBefore = 0
            if (Test-Path -LiteralPath $childLog) { $warningsBefore = @(Select-String -LiteralPath $childLog -Pattern $warningPattern -SimpleMatch).Count }
            $env:S6B_FAULT_CHILD_GIT = '1'
            try { $fault = Invoke-AntiphonNightlyRunCore @common -RunId 's6bfol03' }
            finally { Remove-Item Env:S6B_FAULT_CHILD_GIT -ErrorAction SilentlyContinue }
            $warningsAfter = 0
            if (Test-Path -LiteralPath $childLog) { $warningsAfter = @(Select-String -LiteralPath $childLog -Pattern $warningPattern -SimpleMatch).Count }
            Write-Host ('fault={0}/{1} warnings={2}' -f $fault.Phase, $fault.ExitCode, ($warningsAfter - $warningsBefore))
            Note ($null -ne $fault -and [string]$fault.Phase -eq [string]$first.Phase -and [int]$fault.ExitCode -eq [int]$first.ExitCode) 'fault-verdict'
            Note (($warningsAfter - $warningsBefore) -eq 1) 'fault-one-warning'
            $runText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1'))
            $testsText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-tests-impl.ps1'))
            Note ($runText -notmatch 'Incremental' -and $testsText -notmatch 'publish-release\.ps1') 'run-path-adds-no-mode-or-publish'
        }
        'ChunkOutcomeNeedsResults' {
            $passedFile = Join-Path $ResultsDirectory 'passed.trx'
            $jsonFile = Join-Path $ResultsDirectory 'passed.json'
            $badFile = Join-Path $ResultsDirectory 'bad.txt'
            Write-RcOutcomeTrx -Path $passedFile -SummaryOutcome 'Completed' -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0 -Rows @(@{ Id = '1'; ClassName = 'Ns.Sample'; MethodName = 'Method'; Outcome = 'Passed' })
            [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'weak.trx'), '<TestRun id="1"><Results /></TestRun>')
            [IO.File]::WriteAllText($jsonFile, '{"ok":true}')
            [IO.File]::WriteAllText($badFile, 'not-json')
            $passed = Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $passedFile
            $weak = Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath (Join-Path $ResultsDirectory 'weak.trx')
            $jsonPassed = Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $jsonFile
            $drained = Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $true } -ResultsPath $passedFile
            $missing = Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath (Join-Path $ResultsDirectory 'missing.trx')
            $unparsed = Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $badFile
            $failed = Get-NightlyRcChunkOutcome -Run @{ ExitCode = 2; OutputDrainTimedOut = $false } -ResultsPath $passedFile
            Note ($passed.Outcome -ceq 'Passed' -and [string]::IsNullOrWhiteSpace([string]$passed.Reason)) 'passed-needs-results'
            Note ($weak.Outcome -ceq 'NotRun' -and $weak.Reason -ceq 'results-summary-unreadable') 'weak-trx-not-passed'
            Note ($jsonPassed.Outcome -ceq 'NotRun' -and $jsonPassed.Reason -ceq 'results-summary-unreadable') 'json-without-rows-not-passed'
            Note ($drained.Outcome -ceq 'NotRun' -and $drained.Reason -ceq 'drain-timeout') 'drain-timeout-not-run'
            Note ($missing.Outcome -ceq 'NotRun' -and $missing.Reason -ceq 'results-missing') 'missing-results-not-run'
            Note ($unparsed.Outcome -ceq 'NotRun' -and $unparsed.Reason -ceq 'results-unparseable') 'unparseable-results-not-run'
            Note ($failed.Outcome -ceq 'Failed' -and $failed.Reason -ceq 'exit-2') 'nonzero-exit-failed'
            $repo = Join-Path $ResultsDirectory 'repo'
            $ev = Join-Path $ResultsDirectory 'evidence'
            [void][IO.Directory]::CreateDirectory($ev)
            $sha = New-Repo $repo
            [void](New-RcSourceSnapshot -Repository $repo -EvidenceRoot $ev -InputPaths @('src/input.txt') -ExpectedSha $sha -Strict)
            $stored = Register-NightlyRcChunkRevision -EvidenceRoot $ev -Suite 'server' -Chunk 'c1' -Outcome $failed.Outcome -Reason $failed.Reason -Strict
            $body = Get-Content -LiteralPath (Join-Path $ev 'revisions/1/revision.json') -Raw | ConvertFrom-Json
            Note ($stored.sequence -eq 1 -and $body.outcome -ceq 'Failed' -and $body.reason -ceq 'exit-2' -and -not $body.gateCredit) 'reason-is-stored'
            $testsImpl = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-tests-impl.ps1'))
            Note ($testsImpl.Contains('Get-NightlyRcChunkOutcome')) 'chunk-outcome-is-wired'
        }
        'ShadowPassedIsAtLeastTheRealCheck' {
            if(-not (Get-Command Read-NightlyTrxRunSummary -ErrorAction SilentlyContinue)){
                . (Join-Path $PSScriptRoot 'lib/nightly-watchdog-admission.ps1')
            }
            if(-not (Get-Command ConvertTo-NightlyCoverageVerdict -ErrorAction SilentlyContinue)){
                . (Join-Path $PSScriptRoot 'lib/nightly-coverage.ps1')
            }
            $bare=Join-Path $ResultsDirectory 'bare.trx'
            [IO.File]::WriteAllText($bare,'<TestRun id="1"><Results /></TestRun>')
            Note-RcShadowNotWeaker 'summary-unreadable' $bare
            $identity=Join-Path $ResultsDirectory 'identity.trx'
            [IO.File]::WriteAllText($identity,'<?xml version="1.0" encoding="utf-8"?><TestRun><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary><Results><UnitTestResult testId="missing" testName="Ns.Sample.Method" outcome="Passed" /></Results></TestRun>')
            Note-RcShadowNotWeaker 'identity-unreadable' $identity
            $failed=Join-Path $ResultsDirectory 'failed.trx'
            Write-RcOutcomeTrx -Path $failed -SummaryOutcome 'Completed' -Total 1 -Executed 1 -Passed 0 -Failed 1 -NotExecuted 0 -Rows @(@{ Id='1'; ClassName='Ns.Sample'; MethodName='Method'; Outcome='Failed' })
            Note-RcShadowNotWeaker 'failed-count' $failed
            $executed=Join-Path $ResultsDirectory 'executed.trx'
            Write-RcOutcomeTrx -Path $executed -SummaryOutcome 'Completed' -Total 1 -Executed 4 -Passed 1 -Failed 0 -NotExecuted 0 -Rows @(@{ Id='1'; ClassName='Ns.Sample'; MethodName='Method'; Outcome='Passed' })
            Note-RcShadowNotWeaker 'executed-count' $executed
            $skip=Join-Path $ResultsDirectory 'skip.trx'
            Write-RcOutcomeTrx -Path $skip -SummaryOutcome 'Completed' -Total 1 -Executed 0 -Passed 0 -Failed 0 -NotExecuted 1 -Rows @(@{ Id='1'; ClassName='Ns.Sample'; MethodName='Method'; Outcome='NotExecuted'; DebugTrace='Skipped: not-on-this-host' })
            Note-RcShadowNotWeaker 'expected-skip-roster' $skip
            $coverage=Join-Path $ResultsDirectory 'coverage.trx'
            Write-RcOutcomeTrx -Path $coverage -SummaryOutcome 'Completed' -Total 1 -Executed 0 -Passed 0 -Failed 0 -NotExecuted 1 -Rows @(@{ Id='1'; ClassName='Ns.Sample'; MethodName='Method'; Outcome='Skipped' })
            Note-RcShadowNotWeaker 'coverage-incomplete' $coverage
            $json=Join-Path $ResultsDirectory 'weak.json'
            [IO.File]::WriteAllText($json,'{"ok":true}')
            Note-RcShadowNotWeaker 'json-summary-unreadable' $json
            $pass=Join-Path $ResultsDirectory 'real-pass.trx'
            Write-RcOutcomeTrx -Path $pass -SummaryOutcome 'Completed' -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0 -Rows @(@{ Id='1'; ClassName='Ns.Sample'; MethodName='Method'; Outcome='Passed' })
            $passShadow=Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $pass
            $passReal=Test-RcRealResultsWouldPass -Path $pass
            Note ($passReal -and $passShadow.Outcome -ceq 'Passed') 'real-pass-stays-passed'
            $missingFailed=Join-Path $ResultsDirectory 'missing-failed.trx'
            [IO.File]::WriteAllText($missingFailed,'<?xml version="1.0" encoding="utf-8"?><TestRun><TestDefinitions><UnitTest id="1" name="Ns.Sample.Method"><TestMethod className="Ns.Sample" name="Method" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="1" testName="Ns.Sample.Method" outcome="Passed" /></Results><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" notExecuted="0" /></ResultSummary></TestRun>')
            Note-RcShadowNotWeaker 'missing-failed-attribute' $missingFailed
            $otherOutcome=Join-Path $ResultsDirectory 'other-outcome.trx'
            Write-RcOutcomeTrx -Path $otherOutcome -SummaryOutcome 'Failed' -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0 -Rows @(@{ Id='1'; ClassName='Ns.Sample'; MethodName='Method'; Outcome='Passed' })
            Note-RcShadowNotWeaker 'summary-not-completed' $otherOutcome
            $roster=@([pscustomobject]@{ test='Ns.Sample.Method'; platform='linux'; reasonPrefix='not-on-this-host'; reasonClass='platform' })
            $listed=Join-Path $ResultsDirectory 'listed-skip.trx'
            Write-RcOutcomeTrx -Path $listed -SummaryOutcome 'Completed' -Total 1 -Executed 0 -Passed 0 -Failed 0 -NotExecuted 1 -Rows @(@{ Id='1'; ClassName='Ns.Sample'; MethodName='Method'; Outcome='NotExecuted'; DebugTrace='Skipped: not-on-this-host' })
            $withRoster=Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $listed -ExpectedSkipRoster $roster -Platform 'linux'
            $withoutRoster=Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $listed
            Note ($withRoster.Outcome -ceq 'Passed' -and [string]::IsNullOrWhiteSpace([string]$withRoster.Reason)) 'roster-skip-passes'
            Note ($withoutRoster.Outcome -cne 'Passed') 'roster-skip-needs-roster'
            $goodJson=Join-Path $ResultsDirectory 'good.json'
            [IO.File]::WriteAllText($goodJson,'{"executed":1,"failed":0,"coverageComplete":true,"rows":[{"Outcome":"Passed","Identity":"Ns.Sample.Method"}]}')
            $good=Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $goodJson
            Note ($good.Outcome -ceq 'Passed') 'json-real-pass'
            $falseCover=Join-Path $ResultsDirectory 'false-cover.json'
            [IO.File]::WriteAllText($falseCover,'{"executed":1,"failed":0,"coverageComplete":false,"rows":[{"Outcome":"Passed","Identity":"Ns.Sample.Method"}]}')
            $falseResult=Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $falseCover
            Note ($falseResult.Outcome -cne 'Passed') 'json-coverage-false'
            $override=Join-Path $ResultsDirectory 'override.json'
            [IO.File]::WriteAllText($override,'{"executed":1,"failed":1,"coverageComplete":true,"rows":[{"Outcome":"Failed","Identity":"Ns.Sample.Method"}]}')
            $overrideResult=Get-NightlyRcChunkOutcome -Run @{ ExitCode = 0; OutputDrainTimedOut = $false } -ResultsPath $override
            Note ($overrideResult.Outcome -cne 'Passed') 'json-coverage-true-does-not-override'
        }
        'TornRevisionStaysNotRun' {
            $repo = Join-Path $ResultsDirectory 'repo'
            $ev = Join-Path $ResultsDirectory 'evidence'
            $out = Join-Path $ResultsDirectory 'out'
            [void][IO.Directory]::CreateDirectory($ev)
            $sha = New-Repo $repo
            New-Outputs $out 'entry.dll' 'helper.dll'
            $snap = New-RcSourceSnapshot -Repository $repo -EvidenceRoot $ev -InputPaths @('src/input.txt') -ExpectedSha $sha -Strict
            [void](Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'server' -Command 'dotnet build server.csproj -c Debug' -OutputDirectory $out -Entry 'entry.dll' -SourceDigest $snap.digest -Strict)
            [void](Register-NightlyRcChunkRevision -EvidenceRoot $ev -Suite 'server' -Chunk 'a' -Outcome 'Passed' -Strict)
            $tornDir = Join-Path $ev 'revisions/2'
            [void][IO.Directory]::CreateDirectory($tornDir)
            $tornPath = Join-Path $tornDir 'revision.json'
            [IO.File]::WriteAllText($tornPath, '{')
            $read = Read-RcRunEvidence -EvidenceRoot $ev -Strict
            Note ($read.sequence -eq 1 -and $read.pending -ceq 'not-run' -and $read.effectiveMode -ceq 'Full' -and -not $read.gateCredit) 'torn-is-not-run'
            $next = Register-NightlyRcChunkRevision -EvidenceRoot $ev -Suite 'server' -Chunk 'b' -Outcome 'Failed' -Reason 'exit-1' -Strict
            $read = Read-RcRunEvidence -EvidenceRoot $ev -Strict
            Note ($next.sequence -eq 3 -and $read.sequence -eq 3 -and $read.pending -ceq 'not-run') 'torn-does-not-block'
            Note ((Get-Content -LiteralPath $tornPath -Raw) -eq '{') 'torn-file-kept'
            $fresh = Join-Path $ResultsDirectory 'torn-only'
            $only = Join-Path $fresh 'revisions/1'
            [void][IO.Directory]::CreateDirectory($only)
            [IO.File]::WriteAllText((Join-Path $only 'revision.json'), '{')
            $orphan = Read-RcRunEvidence -EvidenceRoot $fresh -Strict
            Note ($orphan.sequence -eq 0 -and $orphan.pending -ceq 'not-run' -and -not $orphan.gateCredit) 'orphan-root-is-not-run'
        }
        'LaunchStampIsCheaper' {
            $repo = Join-Path $ResultsDirectory 'repo'
            $ev = Join-Path $ResultsDirectory 'evidence'
            $out = Join-Path $ResultsDirectory 'out'
            $sample = Join-Path $ResultsDirectory 'sample'
            [void][IO.Directory]::CreateDirectory($ev)
            [void][IO.Directory]::CreateDirectory($sample)
            $sha = New-Repo $repo
            New-Outputs $out 'entry.dll' 'helper.dll'
            $chunk = New-Object byte[] (4MB)
            $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($chunk) } finally { $rng.Dispose() }
            $sampleBytes = 0L
            foreach ($index in 1..4) {
                $path = Join-Path $sample ('part' + $index + '.dll')
                [IO.File]::WriteAllBytes($path, $chunk)
                $sampleBytes += $chunk.Length
            }
            $snap = New-RcSourceSnapshot -Repository $repo -EvidenceRoot $ev -InputPaths @('src/input.txt') -ExpectedSha $sha -Strict
            $build = Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'server' -Command 'dotnet build server.csproj -c Debug' -OutputDirectory $out -Entry 'entry.dll' -SourceDigest $snap.digest -Strict
            $fixtureWatch = [Diagnostics.Stopwatch]::StartNew()
            $launch = Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'server' -Chunk 'c1' -OutputDirectory $out -Arguments @('test') -BuildDigest $build.digest -Repository $repo -ExpectedSha $sha -Strict
            $fixtureWatch.Stop()
            $buildText = [IO.File]::ReadAllText((Join-Path $ev 'builds/server.json'))
            Note ($launch.joined -and $buildText.Contains('lastWriteUtcTicks')) 'stamp-joins-build'
            [IO.File]::WriteAllBytes((Join-Path $out 'helper.dll'), [byte[]](9, 9, 9, 9))
            Refuses { Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'server' -Chunk 'c2' -OutputDirectory $out -Arguments @('test') -BuildDigest $build.digest -Repository $repo -ExpectedSha $sha -Strict } 'output-not-joined'
            $bulkBuildWatch = [Diagnostics.Stopwatch]::StartNew()
            $bulkBuild = Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'bulk' -Command 'dotnet build bulk.csproj -c Debug' -OutputDirectory $sample -Entry 'part1.dll' -SourceDigest $snap.digest -Strict
            $bulkBuildWatch.Stop()
            $bulkLaunchWatch = [Diagnostics.Stopwatch]::StartNew()
            $bulkLaunch = Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'bulk' -Chunk 'c1' -OutputDirectory $sample -Arguments @('test') -BuildDigest $bulkBuild.digest -Repository $repo -ExpectedSha $sha -Strict
            $bulkLaunchWatch.Stop()
            $wide = Join-Path $ResultsDirectory 'wide'
            [void][IO.Directory]::CreateDirectory($wide)
            [IO.File]::WriteAllBytes((Join-Path $wide 'entry.dll'), [byte[]](1, 2, 3, 4))
            $tiny = New-Object byte[] 32
            foreach ($index in 1..279) {
                [IO.File]::WriteAllBytes((Join-Path $wide ('h' + $index.ToString('000') + '.dll')), $tiny)
            }
            [void](Register-NightlyRcBuildEvidence -EvidenceRoot $ev -Suite 'wide' -Command 'dotnet build wide.csproj -c Debug' -OutputDirectory $wide -Entry 'entry.dll' -SourceDigest $snap.digest -Strict)
            $wideWatch = [Diagnostics.Stopwatch]::StartNew()
            $wideLaunch = Register-NightlyRcLaunchEvidence -EvidenceRoot $ev -Suite 'wide' -Chunk 'c1' -OutputDirectory $wide -Arguments @('test') -Repository $repo -ExpectedSha $sha -Strict
            $wideWatch.Stop()
            $productionBytes = 174587904.0
            $launches = 1289.0
            $scale = $productionBytes / [double]$sampleBytes
            $fullMs = [math]::Round($bulkBuildWatch.Elapsed.TotalMilliseconds, 3)
            $stampMs = [math]::Round($bulkLaunchWatch.Elapsed.TotalMilliseconds, 3)
            $fixtureMs = [math]::Round($fixtureWatch.Elapsed.TotalMilliseconds, 3)
            $wideMs = [math]::Round($wideWatch.Elapsed.TotalMilliseconds, 3)
            $extrapolatedFull = [math]::Round($fullMs * $scale * $launches, 1)
            $extrapolatedStamp = [math]::Round($wideMs * $launches, 1)
            Write-Host ('A6 fixtureLaunchMs={0} sampleFiles=4 sampleBytes={1} fullMs={2} stampMs={3} wideFiles=280 wideLaunchMs={4} extrapolatedFull1289={5} extrapolatedStamp1289={6}' -f $fixtureMs, $sampleBytes, $fullMs, $stampMs, $wideMs, $extrapolatedFull, $extrapolatedStamp)
            Note ($bulkLaunch.joined -and $wideLaunch.joined -and $stampMs -lt $fullMs) 'stamp-cheaper-than-full-read'
            $testsImpl = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-tests-impl.ps1'))
            $launchAt = $testsImpl.IndexOf('Register-NightlyRcLaunchEvidence')
            $watchAt = $testsImpl.IndexOf('$phaseWatch = [Diagnostics.Stopwatch]::StartNew()')
            Note ($launchAt -ge 0 -and $watchAt -gt $launchAt) 'launch-evidence-outside-phase'
        }
        default { throw 'unknown-case' }
    }
} catch { Note $false ('unexpected:'+ $_.Exception.Message) }
Assert-C487 ($errors.Count -eq 0) ("RE $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
