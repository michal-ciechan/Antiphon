#requires -Version 7.0
# Fresh-input and bounded census witnesses. Fixtures are retained for evidence, never swept.
param([string]$EvidenceRoot = '.antiphon/c1862-census')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$selector = Join-Path $repo 'scripts/impacted-classes.ps1'
$fixture = Join-Path ([IO.Path]::GetFullPath($EvidenceRoot)) ([Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
function Put([string]$Relative, [string]$Text) {
    $file = Join-Path $fixture $Relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))
    [IO.File]::WriteAllText($file, $Text)
}
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw ('ASSERT ' + $Message) }
}
function Reject-Stale([scriptblock]$Check, [string]$Label) {
    try { & $Check }
    catch { if ($_.Exception.Message.StartsWith('ASSERT ')) { Write-Host ('BLINDNESS red=' + $Label); return }; throw }
    throw ('blindness control accepted stale ' + $Label)
}
function Select([string]$Root, [string]$Changed) {
    $psi = [Diagnostics.ProcessStartInfo]::new('pwsh')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile','-NonInteractive','-File',$selector,'-Repo',$Root,'-Path',$Changed,'-Json')) {
        $psi.ArgumentList.Add($arg)
    }
    $psi.Environment['ANTIPHON_SELECTOR_TIMING'] = '1'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $proc = [Diagnostics.Process]::Start($psi)
    try {
        $stdout = $proc.StandardOutput.ReadToEndAsync()
        $stderr = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit(120000)) {
            $proc.Kill($true)
            $proc.WaitForExit()
            throw 'selector exceeded existing 120s process budget'
        }
        $text = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        Write-Output ('PROCESS elapsedMs=' + $clock.Elapsed.TotalMilliseconds + ' ' + $errorText.Trim()) | Out-Host
        return [pscustomobject]@{ Exit=$proc.ExitCode; Json=($text | ConvertFrom-Json); Error=$errorText }
    }
    finally { $proc.Dispose() }
}
function Good([string]$Changed) {
    $ran = Select $fixture $Changed
    Assert ($ran.Exit -eq 0) $ran.Error
    return $ran.Json
}
function Names($Selected, [string[]]$Expected) {
    Assert ((@($Selected.classes | Sort-Object) -join ',') -ceq (@($Expected | Sort-Object) -join ',')) ('fresh classes: ' + ($Selected.classes -join ','))
}
$table = '{"schema":1,"kinds":[{"id":"anchor","globs":["server/anchor.cs"],"classes":["AnchorTests"]}]}'
Put 'tests/impacted-classes.json' $table
Put 'tests/Antiphon.Tests/Anchor.cs' 'public sealed class AnchorTests {}'
Put 'tests/Antiphon.Tests/Changed.cs' 'public sealed class InitialTests {}'
$initial = Good 'tests/Antiphon.Tests/Changed.cs'
Names $initial @('InitialTests')
Put 'tests/Antiphon.Tests/Changed.cs' 'public sealed class ChangedTests {}'
Names (Good 'tests/Antiphon.Tests/Changed.cs') @('ChangedTests')
Reject-Stale { Names $initial @('ChangedTests') } 'changed declaration'
Write-Host 'WITNESS changed declaration passed'
$beforeNew = Good 'tests/Antiphon.Tests/New.cs'
Put 'tests/Antiphon.Tests/New.cs' 'public partial class NewTests {}'
$new = Good 'tests/Antiphon.Tests/New.cs'
Names $new @('NewTests')
Reject-Stale { Names $beforeNew @('NewTests') } 'new declaration'
Write-Host 'WITNESS new declaration passed'
# Removal is exact and literal within the owned fixture, and removes a single file only.
$removed = Join-Path $fixture 'tests/Antiphon.Tests/New.cs'
[IO.File]::Delete($removed)
Names (Good 'tests/Antiphon.Tests/New.cs') @()
Reject-Stale { Names $new @() } 'removed declaration'
Write-Host 'WITNESS removed declaration passed'
Put 'tests/Antiphon.Tests/Changed.cs' 'public sealed class SharedTests {}'
$beforeDuplicate = Good 'tests/Antiphon.Tests/Changed.cs'
Put 'tests/Antiphon.SessionRunner.Tests/Same.cs' 'public sealed class SharedTests {}'
$selected = Good 'tests/Antiphon.Tests/Changed.cs'
Names $selected @('SharedTests')
Assert ((@($selected.filters.project | Sort-Object) -join ',') -ceq 'tests/Antiphon.SessionRunner.Tests,tests/Antiphon.Tests') 'duplicate class must select both projects'
Reject-Stale { Assert (@($beforeDuplicate.filters).Count -eq 2) 'duplicate project count' } 'duplicate class projects'
Write-Host 'WITNESS duplicate class projects passed'
Put 'tests/derived-impact-index.json' '{"schema":1,"edges":[],"cappedPaths":[],"windowsOnly":["SharedTests"],"linuxOnly":[]}'
$selected = Good 'tests/Antiphon.Tests/Changed.cs'
Names $selected @()
Assert (($selected.windowsFilters | ConvertTo-Json -Depth 8) -match 'SharedTests') 'fresh Windows label'
$beforeLinux = $selected
Put 'tests/derived-impact-index.json' '{"schema":1,"edges":[],"cappedPaths":[],"windowsOnly":[],"linuxOnly":["SharedTests"]}'
$selected = Good 'tests/Antiphon.Tests/Changed.cs'
Names $selected @('SharedTests')
Assert (@($selected.windowsFilters).Count -eq 0) 'old Windows label must be gone'
Reject-Stale { Names $beforeLinux @('SharedTests') } 'changed OS labels'
Write-Host 'WITNESS changed OS labels passed'
$beforeBroken = Select $fixture 'tests/Antiphon.Tests/Changed.cs'
foreach ($broken in @('{"schema":2,"kinds":[]}', '{"schema":1,"kinds":[{"id":"bad-row","globs":["x"],"classes":["MissingTests"]}]}')) {
    Put 'tests/impacted-classes.json' $broken
    $ran = Select $fixture 'tests/Antiphon.Tests/Changed.cs'
    Assert ($ran.Exit -eq 1 -and -not $ran.Json.ok) 'malformed or stale named table must refuse'
    Assert ($ran.Error -match 'root|bad-row') 'refusal must identify its row'
}
Put 'tests/impacted-classes.json' $table
Reject-Stale { Assert ($beforeBroken.Exit -eq 1 -and -not $beforeBroken.Json.ok) 'table refusal' } 'malformed table'
Write-Host 'WITNESS malformed tables passed'
# One real-tree census is the performance red/green; do not repeat the replay suite here.
$ran = Select $repo 'Antiphon.sln'
Assert ($ran.Exit -eq 0) $ran.Error
$timingLine = ($ran.Error -split "`n" | Where-Object { $_.StartsWith('SELECTOR_TIMING ') } | Select-Object -Last 1)
$timing = $timingLine.Substring('SELECTOR_TIMING '.Length) | ConvertFrom-Json
Assert ($timing.censusMs -lt 2000) ('census must finish under 2000ms; actual=' + $timing.censusMs)
Write-Host 'WITNESS census performance passed'
# Reuse the compiled type in this process, but never a previous census.
Add-Type -Path (Join-Path $repo 'scripts/lib/SelectorCensus.cs')
$decl = [regex]'class\s+([A-Za-z_][A-Za-z0-9_]*)'
$before = [Antiphon.Selector.SelectorCensus]::Scan($fixture, $decl, @([regex]'DowngradeToken'))
Put 'tests/Antiphon.Tests/Changed.cs' 'public sealed class ReentrantTests {} // DowngradeToken'
$after = [Antiphon.Selector.SelectorCensus]::Scan($fixture, $decl, @([regex]'DowngradeToken'))
Assert ($after.ClassesByFile['tests/Antiphon.Tests/Changed.cs'][0] -ceq 'ReentrantTests') 'same-process fresh declaration'
Assert ($after.DerivedFiles.Contains('tests/Antiphon.Tests/Changed.cs')) 'same-process fresh derive pattern'
Assert (-not $before.DerivedFiles.Contains('tests/Antiphon.Tests/Changed.cs')) 'earlier census must stay immutable'
Write-Host 'WITNESS same-process census isolation passed'
# Startup is independently measured through the child's first statement; exit/drain is separate.
$psi = [Diagnostics.ProcessStartInfo]::new('pwsh')
$psi.RedirectStandardOutput = $true
foreach ($arg in @('-NoProfile','-NonInteractive','-Command','[Console]::Out.WriteLine([DateTime]::UtcNow.Ticks)')) { $psi.ArgumentList.Add($arg) }
$launch = [DateTime]::UtcNow.Ticks
$clock = [Diagnostics.Stopwatch]::StartNew()
$child = [Diagnostics.Process]::Start($psi)
try {
    $entryTask = $child.StandardOutput.ReadToEndAsync()
    if (-not $child.WaitForExit(120000)) {
        $child.Kill($true)
        $child.WaitForExit()
        throw 'startup probe exceeded existing 120s process budget'
    }
    $entry = [long]$entryTask.GetAwaiter().GetResult()
    Assert ($child.ExitCode -eq 0) 'startup probe exit'
    $startupMs = ($entry - $launch) / 10000.0
    Write-Host ('STARTUP_PROBE startupThroughFirstStatementMs=' + $startupMs + ' exitAndDrainMs=' + ($clock.Elapsed.TotalMilliseconds - $startupMs))
}
finally { $child.Dispose() }
Write-Host 'SELECTOR CENSUS WITNESS passed=8 failed=0 blindnessRed=6'
