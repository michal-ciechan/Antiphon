#requires -Version 7.0
# CARD-1836 S5a: owned synthetic evidence only; never opens a qualification run.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('rh-' + [guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
$script:Errors = [Collections.Generic.List[string]]::new()
function Note([bool]$Ok, [string]$Detail) { if (-not $Ok) { $script:Errors.Add($Detail) } }
function New-Root([string]$Prefix) {
    $p = Join-Path $ResultsDirectory ($Prefix + '-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($p)
    return $p
}
function Write-Text([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Refuses([scriptblock]$Action, [string]$Reason) {
    $message = ''
    try { & $Action | Out-Null } catch { $message = $_.Exception.ToString() }
    Note ($message.Contains($Reason)) ($Reason + ' => ' + $message)
}
function Export-Fixture([string]$Root, [string[]]$Paths = @('summary.json')) {
    Export-RcHistoricalEvidence -SourceRunRoot $Root -EvidenceRoot (Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))) -RepositoryRoot (Join-Path $PSScriptRoot '..') -RelativePath $Paths
}
function Test-NativeHistoricalHandle {
    $nativeSource = New-Root 'native-handle'
    Write-Text (Join-Path $nativeSource 'summary.json') '{"runId":"native-handle"}'
    $nativeOutput = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    $nativeBundle = [Antiphon.Rc.HistoricalEvidence]::Export($nativeSource, $nativeOutput, (Join-Path $PSScriptRoot '..'), (Join-Path $PSScriptRoot '..'), @('summary.json'), $null)
    Note $nativeBundle.valid 'production native handle factory failed'
    Note ($nativeBundle.files[0].length -eq 25) 'native handle read wrong bytes'
}
try {
    $library = Join-Path $PSScriptRoot 'lib/rc-historical-evidence.ps1'
    if ([IO.File]::Exists($library)) { . $library }
    else {
        # Pre-S5a default denies extraction: assert its outcome, not a missing function.
        function Export-RcHistoricalEvidence { [pscustomobject]@{ valid = $false } }
    }
    $probe = New-Root 'probe'
    Write-Text (Join-Path $probe 'summary.json') '{"runId":"probe"}'
    $available = Export-Fixture $probe
    if (-not $available.valid) { $script:Errors.Add('default-deny extraction did not produce a verified immutable bundle') }
    else { switch ($Case) {
        'LargeSummaryIsBounded' {
            $source = New-Root 'large'
            $summary = Join-Path $source 'summary.json'
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $writer = [IO.StreamWriter]::new($summary, $false, [Text.UTF8Encoding]::new($false), 65536)
            try {
                $writer.Write('{"runId":"fixture-run","candidateSha":"0123456789012345678901234567890123456789","stdout":"')
                $block = 'x' * 1048576
                for ($i = 0; $i -lt 885; $i++) { $writer.Write($block) }
                $writer.Write('","nested":[{"ignored":true}],"complete":true}')
            } finally { $writer.Dispose() }
            $block = $null
            Write-Text (Join-Path $source 'chunk.timing.json') '{"suite":"antiphon","chunk":"one","runWallSeconds":1.25,"lastPhase":"complete"}'
            Write-Text (Join-Path $source 'chunk.trx') '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testId="a" outcome="Passed" duration="00:00:01" /></Results><TestDefinitions><UnitTest id="a"><TestMethod className="Fixture.Tests" name="Runs" /></UnitTest></TestDefinitions></TestRun>'
            [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
            $baseline = [Diagnostics.Process]::GetCurrentProcess().PeakWorkingSet64
            $result = Export-Fixture $source @('summary.json','chunk.timing.json','chunk.trx')
            $peak = [Diagnostics.Process]::GetCurrentProcess().PeakWorkingSet64 - $baseline
            Note ($result.files.Count -eq 3) 'inventory lost files'
            $entry = @($result.files | Where-Object relativePath -CEQ 'summary.json')[0]
            Note ($entry.length -gt (885L * 1048576)) 'fixture is smaller than 885 MiB'
            Note ($entry.sha256 -ceq (Get-FileHash -LiteralPath $summary -Algorithm SHA256).Hash.ToLowerInvariant()) 'raw digest mismatch'
            Note ($result.maxBufferedBytes -le 1048576 -and $result.maxBufferedBytes -gt 0) 'parser buffer bound'
            Note ($peak -le 268435456) ('working set delta: ' + $peak)
            $rows = @(Get-Content -LiteralPath (Join-Path $result.evidenceRoot 'rows.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
            Note (@($rows | Where-Object { $_.field -ceq 'runId' -and $_.value -ceq 'fixture-run' }).Count -eq 1) 'normalized summary runId'
            Note (@($rows | Where-Object { $_.kind -ceq 'UnitTestResult' -and $_.attributes.outcome -ceq 'Passed' }).Count -eq 1) 'TRX result row'
            Note (@($rows | Where-Object { $_.kind -ceq 'TestMethod' -and $_.attributes.className -ceq 'Fixture.Tests' }).Count -eq 1) 'TRX method row'
            Note (@($rows | Where-Object { $_.field -ceq 'runWallSeconds' -and $_.value -eq 1.25 }).Count -eq 1) 'timing scalar row'
            Note (Test-RcHistoricalEvidence -EvidenceRoot $result.evidenceRoot).valid 'fresh bundle did not reverify'
            Note ($watch.Elapsed.TotalSeconds -le 60) ('streaming extraction exceeded the 60s target: ' + $watch.Elapsed.TotalSeconds)
            Write-Output ('RH metrics bytes=' + $entry.length + ' peakDelta=' + $peak + ' maxBufferedBytes=' + $result.maxBufferedBytes + ' elapsed=' + $watch.Elapsed.TotalSeconds)
        }
        'TamperAndChangingReadRefuse' {
            Test-NativeHistoricalHandle
            $source = New-Root 'tamper'
            $path = Join-Path $source 'summary.json'
            Write-Text $path '{"runId":"first"}'
            $before = (Get-FileHash -LiteralPath $path).Hash
            $bundle = Export-Fixture $source
            Note ((Get-FileHash -LiteralPath $path).Hash -ceq $before) 'extractor changed source'
            Write-Text $path '{"runId":"other"}'
            $next = Export-Fixture $source
            Note ($bundle.files[0].sha256 -cne $next.files[0].sha256) 'same path reused cached digest'
            Write-Text (Join-Path $bundle.evidenceRoot $bundle.files[0].bundlePath) '{"runId":"other"}'
            Refuses { Test-RcHistoricalEvidence -EvidenceRoot $bundle.evidenceRoot } 'evidence-digest'
            # The hook executes after opening/inspecting the handle, before copying it.
            $hook = [Action]{ Write-Text $path '{"runId":"third"}' }
            Refuses { Export-RcHistoricalEvidence -SourceRunRoot $source -EvidenceRoot (Join-Path $ResultsDirectory 'changing') -RepositoryRoot (Join-Path $PSScriptRoot '..') -RelativePath @('summary.json') -AfterOpen $hook } 'evidence-changing'
            Note (-not [IO.File]::Exists((Join-Path $ResultsDirectory 'changing/inventory.json'))) 'changing read published inventory'
            Refuses { Test-RcHistoricalEvidence -EvidenceRoot (Join-Path $ResultsDirectory 'changing') } 'inventory.json'
            # Replacement with equal length and a restored mtime must also refuse.
            $hook = [Action]{ $time = [IO.File]::GetLastWriteTimeUtc($path); Write-Text $path '{"runId":"forth"}'; [IO.File]::SetLastWriteTimeUtc($path, $time) }
            Refuses { Export-RcHistoricalEvidence -SourceRunRoot $source -EvidenceRoot (Join-Path $ResultsDirectory 'same-stat') -RepositoryRoot (Join-Path $PSScriptRoot '..') -RelativePath @('summary.json') -AfterOpen $hook } 'evidence-changing'
        }
        'DuplicateTraversalAndTornInputRefuse' {
            $source = New-Root 'invalid'
            $path = Join-Path $source 'summary.json'
            foreach ($bad in @('{"a":1,"a":2}', '{"nested":{"a":1,"A":2}}', '{"runId":"torn', '{"x":[1,]}', '{"x":01}', '{"x":"\uD800"}', '{"x":"bad\q"}')) {
                Write-Text $path $bad
                Refuses { Export-Fixture $source } 'evidence-json'
            }
            [IO.File]::WriteAllBytes($path, [byte[]](123,34,120,34,58,34,255,34,125))
            Refuses { Export-Fixture $source } 'evidence-json'
            Write-Text $path '{"runId":"valid"}'
            Refuses { Export-Fixture $source @('../summary.json') } 'evidence-path'
            Refuses { Export-Fixture $source @('summary.json','SUMMARY.json') } 'evidence-path'
            Refuses { Export-Fixture $source @('summary.json','summary.json') } 'evidence-path'
            Refuses { Export-Fixture $source @('a/one.json','A/two.json') } 'evidence-path'
            Refuses { Export-RcHistoricalEvidence -SourceRunRoot $source -EvidenceRoot (Join-Path $source 'output') -RepositoryRoot (Join-Path $PSScriptRoot '..') } 'evidence-output'
            Refuses { Export-RcHistoricalEvidence -SourceRunRoot $source -EvidenceRoot (Join-Path $PSScriptRoot '../.antiphon/rh-output') -RepositoryRoot (Join-Path $PSScriptRoot '..') } 'evidence-output'
            $link = Join-Path $source 'link.json'
            [void][IO.File]::CreateSymbolicLink($link, $path)
            Refuses { Export-Fixture $source @('link.json') } 'evidence-link'
            # Real hard links, not a mocked link count.
            New-Item -ItemType HardLink -Path (Join-Path $source 'hard.json') -Target $path | Out-Null
            Refuses { Export-Fixture $source } 'evidence-link'
            Write-Text (Join-Path $source 'unsafe.trx') '<!DOCTYPE TestRun [<!ENTITY x "boom">]><TestRun>&x;</TestRun>'
            Refuses { Export-Fixture $source @('unsafe.trx') } 'evidence-xml'
            Write-Text (Join-Path $source 'deep.json') (('[' * 65) + '0' + (']' * 65))
            Refuses { Export-Fixture $source @('deep.json') } 'evidence-depth'
            Write-Text (Join-Path $source 'scalar.json') ('{"runId":"' + ('x' * 1048577) + '"}')
            Refuses { Export-Fixture $source @('scalar.json') } 'evidence-scalar'
        }
        default { throw ('unknown case ' + $Case) }
    } }
    Assert-C487 ($script:Errors.Count -eq 0) ('RH ' + $Case + '.contract') ($script:Errors -join '; ')
} catch { Assert-C487 $false ('RH ' + $Case + '.contract') $_.Exception.Message }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
