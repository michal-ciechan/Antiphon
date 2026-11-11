#requires -Version 5.1
# CARD-1842 S4 witnesses. ASCII-only. Fixtures stay under the harness temp directory.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('qe-' + [guid]::NewGuid().ToString('N'))
}
if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null
}

$script:NowText = '2026-11-10T12:00:00Z'
$script:Errors = New-Object System.Collections.Generic.List[string]
function Note([bool]$Ok, [string]$Detail) {
    if (-not $Ok) { $script:Errors.Add($Detail) }
}
function Assert-Contract {
    Assert-C487 ($script:Errors.Count -eq 0) ('QE ' + $Case + '.contract') ($script:Errors -join '; ')
}
function Write-Utf8([string]$Path, [string]$Text) {
    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}
function New-Root {
    $path = Join-Path $ResultsDirectory ('eta-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}
function Write-Plan([string]$Root, [string]$Suite, [string]$GroupsJson) {
    $json = '{"version":1,"suite":"' + $Suite + '","runId":"fixture","attempt":"1","groups":[' + $GroupsJson + ']}'
    Write-Utf8 (Join-Path $Root ('groups-planned-' + $Suite + '.json')) $json
}
function Write-Timing([string]$Root, [string]$Suite, [string]$Chunk, [string]$Phase, [string]$Started, [string]$Completed, [string]$Wall) {
    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add('"suite":"' + $Suite + '"')
    $parts.Add('"chunk":"' + $Chunk + '"')
    $parts.Add('"lastPhase":"' + $Phase + '"')
    if (-not [string]::IsNullOrWhiteSpace($Started)) { $parts.Add('"startedAt":"' + $Started + '"') }
    if (-not [string]::IsNullOrWhiteSpace($Completed)) { $parts.Add('"completedAt":"' + $Completed + '"') }
    if (-not [string]::IsNullOrWhiteSpace($Wall)) { $parts.Add('"runWallSeconds":' + $Wall) }
    $json = '{' + ($parts -join ',') + '}'
    Write-Utf8 (Join-Path $Root ($Suite + '-' + $Chunk + '.timing.json')) $json
}
function Write-Active([string]$Root, [string]$State, [string]$Suite, [string]$Chunk) {
    $json = '{"format":"antiphon-qualification-active-chunk-v1","state":"' + $State + '","generation":1,"runId":"fixture","attempt":"1","suite":"' + $Suite + '","chunk":"' + $Chunk + '","planFile":"groups-planned-' + $Suite + '.json"}'
    Write-Utf8 (Join-Path $Root 'active-chunk.json') $json
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
function Read-EtaLine([string]$Line) {
    $match = [regex]::Match($Line, '^ETA outstanding=(\d+) running=(\d+) completed=(\d+) unpriced=(\d+) pointSeconds=([0-9]+(?:\.[0-9]+)?) p50=(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z) p95=(unavailable|\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z)$')
    if (-not $match.Success) { throw ('unparsed ' + $Line) }
    return [pscustomobject]@{
        Outstanding = [int]$match.Groups[1].Value
        Running = [int]$match.Groups[2].Value
        Completed = [int]$match.Groups[3].Value
        Unpriced = [int]$match.Groups[4].Value
        PointSeconds = [double]::Parse($match.Groups[5].Value, [Globalization.CultureInfo]::InvariantCulture)
        P50 = $match.Groups[6].Value
        P95 = $match.Groups[7].Value
        Line = $Line
    }
}
function Invoke-Eta([string]$Root, [string]$CostPath) {
    $eta = Join-Path $PSScriptRoot 'qualification-eta.ps1'
    if (-not (Test-Path -LiteralPath $eta)) { throw 'qualification-eta.ps1 is absent' }
    $argv = @('-TimingRoot', $Root, '-NowUtc', $script:NowText)
    if (-not [string]::IsNullOrWhiteSpace($CostPath)) { $argv += @('-ClassCostPath', $CostPath) }
    $output = @(& $eta @argv)
    $text = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($text.Count -ne 1) { throw ('expected one line, got ' + $text.Count + ' [' + ($text -join ' | ') + ']') }
    return Read-EtaLine ([string]$text[0])
}
function Add-Same([object]$Eta, [int]$Outstanding, [int]$Running, [int]$Completed, [int]$Unpriced, [double]$Point, [string]$P50, [string]$P95, [string]$Label) {
    $pointOk = [Math]::Abs($Eta.PointSeconds - $Point) -lt 0.001
    $ok = $Eta.Outstanding -eq $Outstanding -and $Eta.Running -eq $Running -and $Eta.Completed -eq $Completed -and $Eta.Unpriced -eq $Unpriced -and $pointOk -and $Eta.P50 -ceq $P50 -and $Eta.P95 -ceq $P95
    Note $ok ($Label + ' ' + $Eta.Line)
}

try {
    switch ($Case) {
        'OutstandingSumsPlannedSeconds' {
            $root = New-Root
            # First plan row wins. The repeated chunk, the singleton copy, and the index must not be summed again.
            Write-Plan $root 'antiphon' '{"chunk":"a","plannedSeconds":100},{"chunk":"a","plannedSeconds":99999},{"chunk":"b","plannedSeconds":200},{"chunk":"c","plannedSeconds":300},{"chunk":"d","classes":["Alpha","Beta"]},{"chunk":"e","classes":["Ghost"]}'
            Write-Utf8 (Join-Path $root 'groups-planned.json') '{"version":1,"suite":"antiphon","groups":[{"chunk":"trap","plannedSeconds":999999}]}'
            Write-Utf8 (Join-Path $root 'groups-planned-index.json') '{"version":1,"groups":[{"chunk":"trap","plannedSeconds":999999}]}'
            Write-Utf8 (Join-Path $root 'class-costs.json') '{"format":"antiphon-nightly-class-costs-v1","classes":{"Alpha":{"nodes":2,"seconds":10},"Beta":{"nodes":1,"seconds":15}}}'
            Write-Active $root 'Idle' 'antiphon' 'a'
            $eta = Invoke-Eta $root (Join-Path $root 'class-costs.json')
            # 100+200+300 plus class seconds 10+15. nodes is not the divisor. Ghost stays unpriced, not zero.
            Add-Same $eta 5 0 0 1 625 '2026-11-10T12:10:25.000Z' 'unavailable' 'outstanding sum'
            Assert-Contract
        }
        'RunningSubtractsElapsed' {
            $root = New-Root
            Write-Plan $root 'antiphon' '{"chunk":"run-1","plannedSeconds":1000},{"chunk":"later","plannedSeconds":100}'
            Write-Timing $root 'antiphon' 'run-1' 'run' '2026-11-10T11:58:20.000Z' '' '1'
            Write-Active $root 'Active' 'antiphon' 'run-1'
            $elapsed = Invoke-Eta $root ''
            Add-Same $elapsed 1 1 0 0 1000 '2026-11-10T12:16:40.000Z' 'unavailable' 'elapsed subtracted'

            $missing = New-Root
            Write-Plan $missing 'antiphon' '{"chunk":"run-1","plannedSeconds":400}'
            Write-Timing $missing 'antiphon' 'run-1' 'run' '' '' '1'
            Write-Active $missing 'Active' 'antiphon' 'run-1'
            $full = Invoke-Eta $missing ''
            Add-Same $full 0 1 0 0 400 '2026-11-10T12:06:40.000Z' 'unavailable' 'missing start keeps the plan'

            $over = New-Root
            Write-Plan $over 'antiphon' '{"chunk":"run-1","plannedSeconds":100},{"chunk":"later","plannedSeconds":50}'
            Write-Timing $over 'antiphon' 'run-1' 'run' '2026-11-10T11:56:40.000Z' '' '1'
            Write-Active $over 'Active' 'antiphon' 'run-1'
            $capped = Invoke-Eta $over ''
            Add-Same $capped 1 1 0 0 50 '2026-11-10T12:00:50.000Z' 'unavailable' 'overrun stays at zero'
            Assert-Contract
        }
        'CompletedDoesNotAdd' {
            $root = New-Root
            Write-Plan $root 'antiphon' '{"chunk":"stay","plannedSeconds":100},{"chunk":"stale","plannedSeconds":30},{"chunk":"timed","plannedSeconds":500},{"chunk":"done","plannedSeconds":80},{"chunk":"stopped","plannedSeconds":70},{"chunk":"torn","plannedSeconds":60},{"chunk":"marked","plannedSeconds":40}'
            Write-Timing $root 'antiphon' 'stale' 'run' '2026-11-10T11:00:00.000Z' '' '30'
            Write-Timing $root 'antiphon' 'timed' 'timeout' '2026-11-10T11:00:00.000Z' '2026-11-10T11:30:00.000Z' '500'
            Write-Timing $root 'antiphon' 'done' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:10:00.000Z' '80'
            Write-Timing $root 'antiphon' 'stopped' 'cancelled' '2026-11-10T11:00:00.000Z' '2026-11-10T11:10:00.000Z' '70'
            Write-Timing $root 'antiphon' 'torn' 'teardown' '2026-11-10T11:00:00.000Z' '2026-11-10T11:10:00.000Z' '60'
            Write-Timing $root 'antiphon' 'marked' 'run' '2026-11-10T11:00:00.000Z' '2026-11-10T11:10:00.000Z' '40'
            # The active file still names the timeout chunk. Settlement wins over Active.
            Write-Active $root 'Active' 'antiphon' 'timed'
            $eta = Invoke-Eta $root ''
            Add-Same $eta 2 0 5 0 130 '2026-11-10T12:02:10.000Z' 'unavailable' 'settled chunks add nothing'
            Assert-Contract
        }
        'FewWallsLeaveP95Unavailable' {
            $root = New-Root
            $groups = '{"chunk":"left","plannedSeconds":610}'
            for ($i = 1; $i -le 6; $i++) {
                $groups = $groups + ',{"chunk":"w' + $i + '","plannedSeconds":100}'
            }
            $groups = $groups + ',{"chunk":"short","plannedSeconds":10}'
            Write-Plan $root 'antiphon' $groups
            for ($i = 1; $i -le 6; $i++) {
                Write-Timing $root 'antiphon' ('w' + $i) 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '100'
            }
            Write-Timing $root 'antiphon' 'short' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '100'
            $eta = Invoke-Eta $root ''
            # Seven walls. p50 stays the unscaled remainder. The headline is 700/610 of that remainder, not the unweighted mean.
            Add-Same $eta 1 0 7 0 700 '2026-11-10T12:10:10.000Z' 'unavailable' 'seven walls'
            Note ($eta.P95 -ceq 'unavailable') ('p95 token ' + $eta.P95)
            Assert-Contract
        }
        'EightWallsSetP50AndP95' {
            $root = New-Root
            # Ratios sorted: 0.5, 1, 1, 1, 2, 2, 4, 8. N=8 indexes 3 and 7. Sum wall 1400 / sum planned 800 = 1.75, not the unweighted mean 2.4375.
            Write-Plan $root 'antiphon' '{"chunk":"left","plannedSeconds":800},{"chunk":"r1","plannedSeconds":200},{"chunk":"r2","plannedSeconds":100},{"chunk":"r3","plannedSeconds":100},{"chunk":"r4","plannedSeconds":100},{"chunk":"r5","plannedSeconds":100},{"chunk":"r6","plannedSeconds":100},{"chunk":"r7","plannedSeconds":50},{"chunk":"r8","plannedSeconds":50}'
            Write-Timing $root 'antiphon' 'r1' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '100'
            Write-Timing $root 'antiphon' 'r2' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '100'
            Write-Timing $root 'antiphon' 'r3' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '100'
            Write-Timing $root 'antiphon' 'r4' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '100'
            Write-Timing $root 'antiphon' 'r5' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '200'
            Write-Timing $root 'antiphon' 'r6' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '200'
            Write-Timing $root 'antiphon' 'r7' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '200'
            Write-Timing $root 'antiphon' 'r8' 'complete' '2026-11-10T11:00:00.000Z' '2026-11-10T11:02:00.000Z' '400'
            $eta = Invoke-Eta $root ''
            Add-Same $eta 1 0 8 0 1400 '2026-11-10T12:13:20.000Z' '2026-11-10T13:46:40.000Z' 'eight walls'
            Note ($eta.PointSeconds -ne 1950) ('headline must not be the unweighted mean ' + $eta.PointSeconds)
            Assert-Contract
        }
        'RefusesTimingRootWrites' {
            $root = New-Root
            Write-Plan $root 'antiphon' '{"chunk":"only","plannedSeconds":10}'
            $before = Get-TreeStamp $root
            $eta = Invoke-Eta $root ''
            $after = Get-TreeStamp $root
            Add-Same $eta 1 0 0 0 10 '2026-11-10T12:00:10.000Z' 'unavailable' 'read line'
            Note ($before -ceq $after) ('timing root changed [' + $after + ']')
            Note (-not (Test-Path -LiteralPath (Join-Path $root 'eta.json'))) 'eta.json was written'
            Assert-Contract
        }
        default { throw ('unknown case ' + $Case) }
    }
} catch {
    Assert-C487 $false ('QE ' + $Case + '.contract') $_.Exception.Message
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
