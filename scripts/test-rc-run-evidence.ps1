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
            $secondRefused = ($null -eq $second -or [string]$second.Refusal)
            Note ([string]::IsNullOrWhiteSpace($secondRefused) -and $null -ne $two -and $null -ne $two.Source) 'second-run-refused'
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
            $runText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1'))
            $testsText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-tests-impl.ps1'))
            Note ($runText -notmatch 'Incremental' -and $testsText -notmatch 'publish-release\.ps1') 'run-path-adds-no-mode-or-publish'
        }
        default { throw 'unknown-case' }
    }
} catch { Note $false ('unexpected:'+ $_.Exception.Message) }
Assert-C487 ($errors.Count -eq 0) ("RE $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
