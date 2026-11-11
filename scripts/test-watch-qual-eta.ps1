#requires -Version 5.1
# CARD-1842 S5 witnesses. ASCII-only. Fixtures stay under the harness temp directory.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('wq-' + [guid]::NewGuid().ToString('N'))
}
if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null
}

$script:Errors = New-Object System.Collections.Generic.List[string]
$script:EtaPattern = '^ETA outstanding=(\d+) running=(\d+) completed=(\d+) unpriced=(\d+) pointSeconds=([0-9]+(?:\.[0-9]+)?) p50=(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z) p95=(unavailable|\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z)$'
function Note([bool]$Ok, [string]$Detail) {
    if (-not $Ok) { $script:Errors.Add($Detail) }
}
function Assert-Contract {
    Assert-C487 ($script:Errors.Count -eq 0) ('WQ ' + $Case + '.contract') ($script:Errors -join '; ')
}
function Write-Utf8([string]$Path, [string]$Text) {
    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}
function New-Dir([string]$Prefix) {
    $path = Join-Path $ResultsDirectory ($Prefix + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}
function New-TimingRoot {
    $root = New-Dir 'timing'
    # Two outstanding chunks and one settled chunk: outstanding=2 completed=1 pointSeconds=300 (one wall, ratio 1).
    $plan = '{"version":1,"suite":"antiphon","runId":"fixture","attempt":"1","groups":[{"chunk":"a","plannedSeconds":100},{"chunk":"b","plannedSeconds":200},{"chunk":"c","plannedSeconds":50}]}'
    Write-Utf8 (Join-Path $root 'groups-planned-antiphon.json') $plan
    Write-Utf8 (Join-Path $root 'antiphon-c.timing.json') '{"suite":"antiphon","chunk":"c","lastPhase":"complete","startedAt":"2026-11-10T11:00:00.000Z","completedAt":"2026-11-10T11:01:00.000Z","runWallSeconds":50}'
    return $root
}
function Get-TreeStamp([string]$Root) {
    $files = @(Get-ChildItem -LiteralPath $Root -File -Force | Sort-Object Name)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($file in $files) {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
            $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
        } finally { $sha.Dispose() }
        $parts.Add($file.Name + '=' + $hash + ':' + $bytes.Length)
    }
    return ($parts -join "`n")
}
function Invoke-Watch([string]$TimingRoot) {
    $logs = New-Dir 'logs'
    $state = Join-Path (New-Dir 'state') 'qual.seen.txt'
    $heartbeat = Join-Path (New-Dir 'beat') 'heartbeat.json'
    Write-Utf8 $heartbeat ('{"utc":"' + [datetime]::UtcNow.ToString('o') + '","hangSuspected":false}')
    $watch = Join-Path $PSScriptRoot 'orchestration/watch-qual.ps1'
    $watchArgs = @{ LogDir = $logs; StateFile = $state; Heartbeat = $heartbeat; Once = $true; MinFileAgeSeconds = 0 }
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { $watchArgs.TimingRoot = $TimingRoot }
    return @(& $watch @watchArgs | ForEach-Object { [string]$_ })
}
function Get-EtaLines($Lines) {
    return @($Lines | Where-Object { ([string]$_).StartsWith('ETA', [StringComparison]::Ordinal) })
}

try {
    switch ($Case) {
        'OncePrintsOneEtaLine' {
            $root = New-TimingRoot
            $before = Get-TreeStamp $root
            $lines = @(Invoke-Watch $root)
            $eta = @(Get-EtaLines $lines)
            Note ($eta.Count -eq 1) ('eta lines ' + $eta.Count + ' [' + ($lines -join ' | ') + ']')
            if ($eta.Count -ge 1) {
                $match = [regex]::Match([string]$eta[0], $script:EtaPattern)
                Note $match.Success ('unparsed ' + $eta[0])
                if ($match.Success) {
                    $counts = $match.Groups[1].Value + '/' + $match.Groups[2].Value + '/' + $match.Groups[3].Value + '/' + $match.Groups[4].Value + '/' + $match.Groups[5].Value + '/' + $match.Groups[7].Value
                    Note ($counts -ceq '2/0/1/0/300/unavailable') ('counts ' + $counts)
                }
            }
            # The watcher's line is the read-only ETA script's line for the same root.
            $direct = @(& (Join-Path $PSScriptRoot 'qualification-eta.ps1') -TimingRoot $root)
            if ($eta.Count -ge 1 -and $direct.Count -eq 1) {
                $left = ([string]$eta[0]) -replace ' p50=\S+', ''
                $right = ([string]$direct[0]) -replace ' p50=\S+', ''
                Note ($left -ceq $right) ('watch line ' + $left + ' differs from eta line ' + $right)
            }
            Note ((Get-TreeStamp $root) -ceq $before) 'timing root changed'
            Note (@($lines | Where-Object { $_ -like 'watch-qual window ended*' }).Count -eq 1) ('window line [' + ($lines -join ' | ') + ']')
            Assert-Contract
        }
        'OmittedTimingRootStaysSilent' {
            $root = New-TimingRoot
            # Control: the same watcher prints the line when the root is passed, so silence below is not a missing feature.
            $with = @(Get-EtaLines (Invoke-Watch $root))
            Note ($with.Count -eq 1) ('control eta lines ' + $with.Count)
            $lines = @(Invoke-Watch '')
            $silent = @($lines | Where-Object { ([string]$_).Contains('ETA') })
            Note ($silent.Count -eq 0) ('eta without a root [' + ($silent -join ' | ') + ']')
            Note (@($lines | Where-Object { $_ -like 'watch-qual window ended*' }).Count -eq 1) ('window line [' + ($lines -join ' | ') + ']')
            Assert-Contract
        }
        'StatusObjectHasNoEta' {
            . (Join-Path $PSScriptRoot 'lib/rc-qualification-status.ps1')
            $stateRoot = New-Dir 'state'
            $coordination = New-Dir 'coord'
            $timing = New-TimingRoot
            Copy-Item -LiteralPath (Join-Path $timing 'groups-planned-antiphon.json') -Destination (Join-Path $stateRoot 'groups-planned-antiphon.json')
            $status = Get-RcQualificationStatus -CandidateId 'cand-1' -RunId 'run-1' -StateRoot $stateRoot -CoordinationRoot $coordination -ObserveProcess { param($r) [pscustomobject]@{ status = 'absent'; reason = 'fixture' } }
            $names = @($status.PSObject.Properties | ForEach-Object { $_.Name }) -join ','
            $expected = 'candidateId,runId,stateRoot,coordinationRoot,releaseRoot,observedAtUtc,observationComplete,reason,inputs,state,locks,journal,identityMismatches'
            Note ($names -ceq $expected) ('status properties ' + $names)
            $common = @([System.Management.Automation.PSCmdlet]::CommonParameters) + @([System.Management.Automation.PSCmdlet]::OptionalCommonParameters)
            $parameters = @((Get-Command Get-RcQualificationStatus).Parameters.Keys | Where-Object { $common -notcontains $_ }) -join ','
            Note ($parameters -ceq 'CandidateId,RunId,StateRoot,CoordinationRoot,ReleaseRoot,ObserveProcess') ('status parameters ' + $parameters)
            $wrapper = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'rc-qualification-status.ps1'), [ref]$null, [ref]$null)
            $wrapperNames = @($wrapper.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath }) -join ','
            Note ($wrapperNames -ceq 'CandidateId,RunId,StateRoot,CoordinationRoot,ReleaseRoot,PassThru') ('wrapper parameters ' + $wrapperNames)
            foreach ($path in @('lib/rc-qualification-status.ps1', 'rc-qualification-status.ps1', 'lib/qualification-supervisor-heartbeat.ps1')) {
                $text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot $path))
                Note (-not $text.Contains('qualification-eta')) ($path + ' reads the ETA script')
            }
            Assert-Contract
        }
        'ReleaseGatesKeepsLastActivityRefusal' {
            $doc = [System.IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'docs/release-gates.md'))
            $flat = [regex]::Replace($doc, '\s+', ' ')
            $refusal = '`lastActivityAt` is a recorded transition, **not a continuous heartbeat**; its age does not prove a hang or supply an ETA.'
            $sentence = 'The batch ETA is the one line from scripts/orchestration/watch-qual.ps1 when -TimingRoot is passed. This status reader does not walk a timing root, and lastActivityAt does not supply an ETA.'
            Note ($flat.Contains($refusal)) 'lastActivityAt refusal is missing'
            Note ($flat.Contains($sentence)) 'watch-qual ETA sentence is missing'
            Note ($flat.Contains($refusal + ' ' + $sentence)) 'watch-qual ETA sentence does not follow the refusal'
            Assert-Contract
        }
        default { throw ('unknown case ' + $Case) }
    }
} catch {
    Assert-C487 $false ('WQ ' + $Case + '.contract') $_.Exception.Message
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
