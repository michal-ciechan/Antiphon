#requires -Version 7.0
# NightlyCore.cs against the PowerShell it replaces: every compiled path must return exactly what the
# PowerShell fallback (ANTIPHON_NIGHTLY_CORE=off, Windows PowerShell 5.1) returns. ASCII-only.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'
foreach ($name in 'nightly-common','nightly-policy','nightly-coverage','nightly-tests-impl','test-phase-evidence','c487-harness') {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
$repo = Split-Path -Parent $PSScriptRoot
$root = New-C487Root $ResultsDirectory

function Assert-NC([bool]$Ok, [string]$Name, [string]$Detail = '') { Assert-C487 $Ok ('NC ' + $Case + '.' + $Name) $Detail }

# Runs $Body with the compiled core and again with the PowerShell fallback (same session), returning both
# results as JSON text. The switch is the session flag the loader caches.
function Invoke-NCBothWays([scriptblock]$Body) {
    $script:NightlyCoreAvailable = $null
    $core = Test-NightlyCoreAvailable
    $compiled = (& $Body) | ConvertTo-Json -Depth 14 -Compress
    $script:NightlyCoreAvailable = $false
    try { $fallback = (& $Body) | ConvertTo-Json -Depth 14 -Compress }
    finally { $script:NightlyCoreAvailable = $null }
    return [pscustomobject]@{ Core = $core; Compiled = $compiled; Fallback = $fallback }
}

function Get-NCFixtureNodes([int]$Take) {
    $nodes = [Collections.Generic.List[object]]::new()
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $PSScriptRoot 'fixtures/nightly/c1410-antiphon-listing.txt'))) {
        if (-not $line -or $line.StartsWith('#')) { continue }
        $fields = $line.Split('|'); $i = 0
        foreach ($entry in $fields[2].Split(',')) {
            $eq = $entry.IndexOf('=')
            if ($eq -ge 0) { $method = $entry.Substring(0, $eq); $count = [int]$entry.Substring($eq + 1) }
            else { $method = 'M' + $i.ToString('0000'); $count = [int]$entry }
            $i++
            for ($k = 0; $k -lt $count; $k++) {
                $class = $fields[0]
                $bare = Get-NightlyBareClassIdentity $class
                $dot = $bare.LastIndexOf('.')
                $categories = @(); if ($fields[1] -ceq 'S') { $categories = @('Slow') }
                $nodes.Add([pscustomobject]@{ ClassName = $class; Namespace = $bare.Substring(0, $dot); Type = $class.Substring($dot + 1)
                    Method = $method; Uid = ($class + '.' + $method + '.' + $k); Categories = $categories; Excluded = $false })
                if ($nodes.Count -ge $Take) { return $nodes.ToArray() }
            }
        }
    }
    return $nodes.ToArray()
}

function Test-NCCoreMatchesFallback {
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
    $nodes = @(Get-NCFixtureNodes 4000)
    # Default-lane categories and a duplicate UID, so the non-profile and duplicate paths run too.
    $nodes[3].Categories = @('OptIn'); $nodes[5].Categories = @('Explicit'); $nodes[7].Excluded = $true
    $nodes += $nodes[11]
    $real = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile rc -SuiteId antiphon -Platform windows)
    # Method rows, a whole-class row after a method row (first declaring row wins), an absent method list.
    $rules = @($real) + @(
        [pscustomobject]@{ Class = $nodes[200].Type; Methods = @($nodes[200].Method); Reason = 'method'; Owner = 'a' },
        [pscustomobject]@{ Class = $nodes[200].Type; Methods = @(); Reason = 'class'; Owner = 'b' },
        [pscustomobject]@{ Class = $nodes[900].ClassName; Methods = $null; Reason = 'absent'; Owner = 'c' })
    $classes = @($nodes | ForEach-Object { $_.ClassName } | Select-Object -Unique | Select-Object -First 9)

    $r = Invoke-NCBothWays { [ordered]@{
        census = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $rules -ProfileAware
        defaultCensus = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes
        required = @(Get-NightlyRequiredDiscoveryUids -DiscoveryNodes $nodes -RequiredClasses $classes -ProfileExclusions $rules -ProfileAware)
        defaultRequired = @(Get-NightlyRequiredDiscoveryUids -DiscoveryNodes $nodes)
    } }
    Assert-NC $r.Core 'coreLoaded' 'NightlyCore.cs did not compile or load'
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'census' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)

    $r = Invoke-NCBothWays { @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) `
        -Exclusions $rules -ProfileAware $true -Native $true -SuiteId antiphon -PolicyObject $policy.Object) }
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'plan' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)

    $r = Invoke-NCBothWays {
        $entry = [pscustomobject]@{ Document = [pscustomobject]@{ Nodes = $nodes }; Views = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal) }
        $view = Get-NightlyDiscoveryIndexView -Entry $entry -ProfileExclusions $rules -ProfileAware
        [ordered]@{ excluded = @($view.Excluded); byClass = $view.ByClass; required = $view.RequiredUids; classes = $view.Classes; census = $view.Census.Digest }
    }
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'indexView' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)

    $trx = @($nodes | Select-Object -First 60 | ForEach-Object { [pscustomobject]@{ ClassName = $_.ClassName; MethodName = $_.Method } })
    $r = Invoke-NCBothWays { Test-NightlyTrxDiagnosticCrossCheck -TrxRows $trx -TerminalNodes @($nodes | Select-Object -First 50) -RequiredClasses $classes }
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'crossCheck' $r.Compiled

    $noted = 'text' | Add-Member -NotePropertyName Extra -NotePropertyValue 1 -PassThru
    $deep = 'leaf'; for ($i = 0; $i -lt 30; $i++) { $deep = @{ ('d' + $i) = $deep } }
    $samples = [ordered]@{
        nul = $null; t = $true; i8 = [byte]200; i64 = [long]9007199254740993; dec = [decimal]'1.10'; dbl = 0.1 + 0.2; flt = [single]1.5
        u32 = [uint32]7; str = "q `" b \ n `n r `r t `t c $([char]1) u $([char]0x00e9)"; noted = $noted; one = @('only'); none = @()
        pso = [pscustomobject]@{ z = 1; y = @(1, @(2)); x = $null }; deep = $deep; date = [datetime]'2026-01-02T03:04:05'
        enum = [IO.FileAttributes]::Hidden; ch = [char]'x'; dbnull = [DBNull]::Value; sw = [switch]$true
    }
    $r = Invoke-NCBothWays { @(foreach ($k in $samples.Keys) { (ConvertTo-TestPhaseJsonText -Value $samples[$k]) + '|' + (ConvertTo-TestPhaseJsonText -Value $samples[$k] -Depth 20) }) + @((ConvertTo-TestPhaseJsonString -Text $null)) }
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'json' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
}

$expected = 0
switch -Exact ($Case) {
    'CoreMatchesFallback' { Test-NCCoreMatchesFallback; $expected = 6 }
    default { throw ('unknown case: ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $root -ExpectedRows $expected
