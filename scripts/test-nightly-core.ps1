#requires -Version 7.0
# NightlyCore.cs against the PowerShell it replaces: every compiled path must return exactly what the
# PowerShell copy returns (ANTIPHON_NIGHTLY_CORE=off, Windows PowerShell 5.1) where one is kept, and otherwise
# what it returned when it was removed (scripts/fixtures/nightly/core-golden). ASCII-only.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'
foreach ($name in 'nightly-common','nightly-policy','nightly-coverage','nightly-tests-impl','test-phase-evidence','c487-harness') {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
$repo = Split-Path -Parent $PSScriptRoot
$root = New-C487Root $ResultsDirectory

function Assert-NC([bool]$Ok, [string]$Name, [string]$Detail = '') { Assert-C487 $Ok ('NC ' + $Case + '.' + $Name) $Detail }

# Runs $Body with the compiled core and again with the PowerShell fallback (same session), returning both
# results as JSON text. The switch is the session flag the loader caches. Where the body reaches code whose
# PowerShell copy was removed (nightly-core-required), the fallback result is the golden that copy produced
# (scripts/fixtures/nightly/core-golden/<Golden>.json); with no golden named the run fails. A golden over
# 64 KB is kept as its SHA-256 and length: Fallback is then the compiled text when that matches, the digest
# line when it does not. -Normalize names a run-specific directory, written as <root> in both results.
function Invoke-NCBothWays([scriptblock]$Body, [string]$Golden = '', [string]$Normalize = '') {
    $script:NightlyCoreAvailable = $null
    $core = Test-NightlyCoreAvailable
    $compiled = (& $Body) | ConvertTo-Json -Depth 14 -Compress
    $script:NightlyCoreAvailable = $false
    $ran = $true
    try { $fallback = (& $Body) | ConvertTo-Json -Depth 14 -Compress }
    catch {
        if ([string]$_.Exception.Message -notlike 'nightly-core-required *') { throw }
        $ran = $false
        $fallback = 'unnamed golden: ' + $_.Exception.Message
    }
    finally { $script:NightlyCoreAvailable = $null }
    if ($Normalize) {
        $compiled = $compiled.Replace($Normalize.Replace('\', '\\'), '<root>').Replace($Normalize, '<root>')
        $fallback = $fallback.Replace($Normalize.Replace('\', '\\'), '<root>').Replace($Normalize, '<root>')
    }
    if ($Golden) {
        $goldenPath = Join-Path $PSScriptRoot ('fixtures/nightly/core-golden/' + $Golden + '.json')
        $digest = { param($text) 'sha256:' + (Get-NightlySha256Text -Text $text) + ' chars:' + $text.Length }
        # Written only on request (NC_GOLDEN_WRITE=<name>), from the PowerShell copy while it exists, and then
        # still has to be reviewed.
        if ($env:NC_GOLDEN_WRITE -ceq $Golden -or $env:NC_GOLDEN_WRITE -ceq '*') {
            $text = $(if ($ran) { $fallback } else { $compiled })
            if ($text.Length -gt 65536) { $text = & $digest $text }
            [IO.File]::WriteAllText($goldenPath, $text, [Text.UTF8Encoding]::new($false))
        }
        if (-not $ran) {
            $fallback = [IO.File]::ReadAllText($goldenPath, [Text.Encoding]::UTF8)
            if ($fallback.StartsWith('sha256:') -and $fallback -ceq (& $digest $compiled)) { $fallback = $compiled }
        }
    }
    if ($env:NC_DUMP) { Add-Content -LiteralPath $env:NC_DUMP -Value ('' + $fallback.Length + ' ' + $fallback.Substring(0, [Math]::Min(60, $fallback.Length))) }
    return [pscustomobject]@{ Core = $core; Compiled = $compiled; Fallback = $fallback }
}

# Ported code whose PowerShell copy was removed (2026-11-13 rule) is checked against the output that copy
# produced when it was removed (scripts/fixtures/nightly/core-golden/<name>.json), serialised as
# Invoke-NCBothWays serialises. Each listed witness must appear in the golden text.
function Assert-NCGolden([string]$Name, [scriptblock]$Body, [string[]]$Witnesses = @()) {
    $g = Test-NCGolden $Name $Body $Witnesses
    Assert-NC $g.Ok $Name ('actual ' + $g.Actual + ' expected ' + $g.Expected)
}

# Assert-NCGolden's comparison without the assertion, for a check that also compares other parts.
function Test-NCGolden([string]$Name, [scriptblock]$Body, [string[]]$Witnesses = @(), [string]$Normalize = '') {
    $core = Test-NightlyCoreAvailable
    $actual = ''
    try { $actual = (& $Body) | ConvertTo-Json -Depth 14 -Compress } catch { $actual = 'throw: ' + $_.Exception.Message }
    # A run-specific directory (results root) is written as <root>, so the golden does not depend on it.
    if ($Normalize) { $actual = $actual.Replace($Normalize.Replace('\', '\\'), '<root>').Replace($Normalize, '<root>') }
    $goldenPath = Join-Path $PSScriptRoot ('fixtures/nightly/core-golden/' + $Name + '.json')
    # A new golden is written only on request (NC_GOLDEN_WRITE=<name>) and then still has to be reviewed.
    if ($env:NC_GOLDEN_WRITE -ceq $Name) { [IO.File]::WriteAllText($goldenPath, $actual, [Text.UTF8Encoding]::new($false)) }
    $expected = [IO.File]::ReadAllText($goldenPath, [Text.Encoding]::UTF8)
    $ok = $core -and $actual -ceq $expected
    foreach ($witness in $Witnesses) { if (-not $expected.Contains($witness)) { $ok = $false } }
    return [pscustomobject]@{ Ok = [bool]$ok; Actual = $actual; Expected = $expected }
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
    } } 'census'
    Assert-NC $r.Core 'coreLoaded' 'NightlyCore.cs did not compile or load'
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'census' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)

    $r = Invoke-NCBothWays { @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) `
        -Exclusions $rules -ProfileAware $true -Native $true -SuiteId antiphon -PolicyObject $policy.Object) } 'plan'
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'plan' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)

    $r = Invoke-NCBothWays {
        $entry = [pscustomobject]@{ Document = [pscustomobject]@{ Nodes = $nodes }; Views = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal) }
        $view = Get-NightlyDiscoveryIndexView -Entry $entry -ProfileExclusions $rules -ProfileAware
        [ordered]@{ excluded = @($view.Excluded); byClass = $view.ByClass; required = $view.RequiredUids; classes = $view.Classes; census = $view.Census.Digest }
    } 'indexView'
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'indexView' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)

    $trx = @($nodes | Select-Object -First 60 | ForEach-Object { [pscustomobject]@{ ClassName = $_.ClassName; MethodName = $_.Method } })
    # The mismatch list comes out in hashtable order, which differs from process to process: sorted for the golden.
    $r = Invoke-NCBothWays { Get-NCSortedCross (Test-NightlyTrxDiagnosticCrossCheck -TrxRows $trx -TerminalNodes @($nodes | Select-Object -First 50) -RequiredClasses $classes) } 'crossCheck'
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'crossCheck' $r.Compiled

    $noted = 'text' | Add-Member -NotePropertyName Extra -NotePropertyValue 1 -PassThru
    $deep = 'leaf'; for ($i = 0; $i -lt 30; $i++) { $deep = @{ ('d' + $i) = $deep } }
    $samples = [ordered]@{
        nul = $null; t = $true; i8 = [byte]200; i64 = [long]9007199254740993; dec = [decimal]'1.10'; dbl = 0.1 + 0.2; flt = [single]1.5
        u32 = [uint32]7; str = "q `" b \ n `n r `r t `t c $([char]1) u $([char]0x00e9)"; noted = $noted; one = @('only'); none = @()
        pso = [pscustomobject]@{ z = 1; y = @(1, @(2)); x = $null }; deep = $deep; date = [datetime]'2026-01-02T03:04:05'
        enum = [IO.FileAttributes]::Hidden; ch = [char]'x'; dbnull = [DBNull]::Value; sw = [switch]$true
    }
    $r = Invoke-NCBothWays { @(foreach ($k in $samples.Keys) { (ConvertTo-TestPhaseJsonText -Value $samples[$k]) + '|' + (ConvertTo-TestPhaseJsonText -Value $samples[$k] -Depth 20) }) + @((ConvertTo-TestPhaseJsonString -Text $null)) } 'json'
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'json' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
}

function Get-NCSortedCross($Cross) {
    [string[]]$mismatch = @($Cross.Mismatch); [Array]::Sort($mismatch, [StringComparer]::Ordinal)
    return [pscustomobject]@{ Ok = $Cross.Ok; Mismatch = $mismatch; ExtraClasses = $Cross.ExtraClasses }
}

function Get-NCOutcome([scriptblock]$Body) {
    try { return (& $Body) } catch { return ('REFUSED ' + $_.Exception.Message) }
}

# The reflected short envelope the C1325 fixtures build on (as in test-nightly-tests.ps1).
function New-D13StateRecord {
    param([string]$Uid, [string]$TypeName, [string]$Method, [string]$StateName, [string]$Explanation,
        [string]$ExceptionText = 'System.Exception: outer')
    $state = '{0}TestNodeStateProperty {{ Explanation = {1}' -f $StateName, $Explanation
    if ($StateName -notin @('Passed', 'Skipped', 'Discovered', 'InProgress')) { $state += ', Exception = ' + $ExceptionText }
    if ($StateName -eq 'Timeout') { $state += ', Timeout = 00:00:00.0250000' }
    $state += ' }'
    return ('TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = ' + $Uid + ' }, DisplayName = ' + $Method + ', Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Probe, TypeName = ' + $TypeName + ', MethodName = ' + $Method + ', MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, ' + $state + ' ] } }')
}

function Test-NCSummaryWrite {
    $bad = [Collections.Generic.List[string]]::new()
    $nested = [ordered]@{ a = @(1, [long]2, $null, @()); b = [Collections.Generic.List[object]]::new() }
    $nested.b.Add('x'); $nested.b.Add([ordered]@{ deep = @{ k = 'v' } })
    $doc = [ordered]@{
        version = 1; complete = $false; quality = @('a', 'b'); none = @(); one = @('only')
        text = "q `" b \ n `n r `r t `t c $([char]1) u $([char]0x00e9) $([char]0x2028)"
        dbl = 0.1 + 0.2; whole = 1.0; big = 1e20; tiny = 5e-324; neg = -2.5; nan = [double]::NaN
        inf = [double]::PositiveInfinity; ninf = [double]::NegativeInfinity; flt = [single]1.5; fltWhole = [single]3
        dec = [decimal]'1.10'; decWhole = [decimal]5; i8 = [byte]200; u32 = [uint32]7; u64 = [uint64]::MaxValue
        i64 = [long]::MinValue; ch = [char]'x'; dbnull = [DBNull]::Value
        pso = [pscustomobject]@{ z = 1; y = @(1, @(2)); x = $null; w = [pscustomobject]@{ v = 'in' } }
        nested = $nested; tests = @([ordered]@{ testId = 'b'; db = [long]1 }, [ordered]@{ testId = 'a'; joined = $true })
    }
    $json = [Antiphon.Nightly.TestPhaseSummaryWrite]::SummaryJson($doc)
    $expectedJson = ConvertTo-Json -InputObject $doc -Depth 50 -Compress
    if ($json -cne $expectedJson) { $bad.Add('json ' + $json + ' expected ' + $expectedJson) }
    foreach ($unsupported in @([ordered]@{ when = [datetime]'2026-01-02' }, @{ 1 = 'int key' })) {
        $threw = $false
        try { [void][Antiphon.Nightly.TestPhaseSummaryWrite]::SummaryJson($unsupported) } catch [ArgumentException] { $threw = $true } catch { $threw = $_.Exception.InnerException -is [ArgumentException] }
        if (-not $threw) { $bad.Add('unsupported accepted') }
    }
    # Ties, culture order, a missing testId, custom objects and nulls; totals that leave Int64 (a sum, the
    # negation of Int64.MinValue) and continue in Double, [long] of strings, doubles and nulls.
    $max = [long]::MaxValue
    $rows = @(
        [ordered]@{ testId = 'b'; db = $max; host = 1; process = 0; fs = 0; network = 0; fixture = 0; overlapMs = 0; remainderMs = 0 },
        [ordered]@{ testId = 'B'; db = [long]::MinValue; host = 0; process = 0; fs = 0; network = 0; fixture = 0; overlapMs = 0; remainderMs = 0 },
        [ordered]@{ testId = 'a'; db = 5; host = '7'; process = 2.5; fs = $null; network = 0; fixture = 0; overlapMs = 0; remainderMs = 0 },
        [pscustomobject]@{ testId = $([char]0x00e4); db = 12; host = 0; process = 0; fs = 0; network = 0; fixture = 0; overlapMs = 0; remainderMs = 0 },
        [pscustomobject]@{ other = 1 },
        [ordered]@{ testId = 'a'; db = $max; host = $max; process = $max; fs = 0; network = 0; fixture = 0; overlapMs = 0; remainderMs = 1 },
        [ordered]@{ testId = 'c'; db = 12; host = 0; process = 0; fs = 0; network = 0; fixture = 0; overlapMs = 0; remainderMs = 0 },
        [ordered]@{ testId = 'C'; db = -$max; host = -$max; process = 0; fs = 0; network = 0; fixture = 0; overlapMs = 0; remainderMs = 0 })
    $tag = { param($items) (@($items | ForEach-Object { [string]$_.testId + '/' + [string]$_.db }) -join ',') }
    $sorted = [Antiphon.Nightly.TestPhaseSummaryWrite]::SortByTestId($rows)
    $sortedPs = @($rows | Sort-Object { [string]$_.testId })
    if ((& $tag $sorted) -cne (& $tag $sortedPs)) { $bad.Add('sort ' + (& $tag $sorted) + ' expected ' + (& $tag $sortedPs)) }
    $ranked = [Antiphon.Nightly.TestPhaseSummaryWrite]::RankByTotal($sortedPs)
    $rankedPs = @($sortedPs | Sort-Object { -1 * ([long]$_.db + [long]$_.host + [long]$_.process + [long]$_.fs + [long]$_.network + [long]$_.fixture + [long]$_.overlapMs + [long]$_.remainderMs) })
    if ((& $tag $ranked) -cne (& $tag $rankedPs)) { $bad.Add('rank ' + (& $tag $ranked) + ' expected ' + (& $tag $rankedPs)) }
    if (@([Antiphon.Nightly.TestPhaseSummaryWrite]::SortByTestId(@())).Count -ne 0 -or @([Antiphon.Nightly.TestPhaseSummaryWrite]::RankByTotal(@())).Count -ne 0) { $bad.Add('empty') }
    return [pscustomobject]@{ Ok = ($bad.Count -eq 0); Detail = ($(if ($bad.Count) { $bad -join '; ' } else { 'ok' })) }
}

function Test-NCDiagnosticsMatchFallback {
    . (Join-Path $PSScriptRoot 'fixtures/nightly/c1325-diagnostic-records.ps1')
    . (Join-Path $PSScriptRoot 'fixtures/nightly/c1375-diagnostic-records.ps1')
    $decode = { param($b) [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b.Base64)) }
    $c1375 = @(Get-C1375DiagnosticFixtures)
    $records = [Collections.Generic.List[string]]::new()
    foreach ($f in @(Get-C1325ExplanationFixtures) + @(Get-C1325StateFixtures)) { $records.Add([string]$f.Record) }
    $records.Add((Get-C1325AmbiguousStateRecord)); $records.Add($D13FailureRecord)
    foreach ($f in $c1375) { $records.Add((& $decode $f.Record)) }
    $i = 0
    foreach ($state in 'Passed', 'Skipped', 'Discovered', 'InProgress', 'Failed', 'Error', 'Timeout', 'Cancelled', 'Unheard') {
        foreach ($type in 'Plain', 'Ctor(Arg, Other)', 'Bad,Comma', 'Open(Arg') {
            $records.Add((New-D13StateRecord -Uid ('Probe.NC.' + (++$i)) -TypeName $type -Method 'Run' -StateName $state `
                -Explanation ('[Test Failure] boom ' + $i) -ExceptionText ('System.Exception: inner ' + $i)))
        }
    }
    # Truncations and a damaged identity, for the refusal messages.
    foreach ($r in @($records.ToArray())) { $records.Add($r.Substring(0, [int]($r.Length / 2))) }
    $records.Add($D13FailureRecord.Replace('TestNodeUid { Value = ', 'TestNodeUid { Valu = '))
    $records.Add($D13FailureRecord.Replace('MethodName = ', 'MethodNam = '))
    $header = (& $decode $c1375[0].MtpHeader) + "`n" + (& $decode $c1375[0].TunitHeader) + "`n"
    $logs = @(
        @('all', ($header + ($records.ToArray() -join "`n"))),
        @('first-three', ($header + (@($records)[0..2] -join "`n"))),
        @('duplicate', ($header + $D13FailureRecord + "`n" + $D13FailureRecord)),
        @('no-version', ($D13FailureRecord)),
        @('c1375', ($header + (& $decode $c1375[0].Record) + "`n" + (& $decode $c1375[1].Record))),
        # Raw discovered-marker count (DiagScan.cs) at the text's edges and back to back; leading Unicode
        # whitespace and a zero-width space before the JSON/text decision; whitespace-only text.
        @('marker-edges', ('DiscoveredTestNodeStateProperty {' + $header + $D13FailureRecord + "`nDiscoveredTestNodeStateProperty {DiscoveredTestNodeStateProperty {")),
        @('lead-space', ((" `t" + [char]0x2003 + [char]0x00A0 + [char]0x0085) + $header + $D13FailureRecord)),
        @('zwsp-json', (" " + [char]0x200B + '{"Nodes":[]}')),
        @('blank', (" `r`n" + [char]0x3000)))
    $paths = foreach ($log in $logs) {
        $path = Join-Path $root ('nc-' + $log[0] + '.diag'); [IO.File]::WriteAllText($path, $log[1]); $path
    }
    $r = Invoke-NCBothWays {
        $perRecord = foreach ($record in $records) {
            $property = Get-NightlyDiagnosticStateProperty -Record $record
            $state = Get-NightlyDiagnosticStateName -Record $record -StateProperty $property
            [ordered]@{
                property = $property; state = $state
                split = @(Get-NightlyDiagnosticRecords -Text $record)
                asState = Get-NCOutcome { ConvertFrom-NightlyDiagnosticRecord -Record $record -State $(if ($state) { $state } else { 'Failed' }) -StateProperty $property }
                asDiscovered = Get-NCOutcome { ConvertFrom-NightlyDiagnosticRecord -Record $record -State 'Discovered' -StateProperty $property }
            }
        }
        [ordered]@{ records = @($perRecord) }
    } 'diagnosticRecords'
    # ConvertFrom-NightlyDiagnosticLog has no PowerShell record loop left: whole logs against the golden it produced.
    $logs = Test-NCGolden 'diagnosticLogs' {
        @(foreach ($path in $paths) {
            [ordered]@{
                execution = Get-NCOutcome { ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution }
                discovery = Get-NCOutcome { ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery }
            }
        })
    } @('discovery-count-mismatch', 'empty diagnostic') $root
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $logs.Ok) 'diagnostics' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length + '; logs golden ' + $logs.Ok + ' actual ' + $logs.Actual.Length + ' chars')

    $hashed = Join-Path $root 'nc-hash.bin'; [IO.File]::WriteAllBytes($hashed, [byte[]](0..255))
    $values = @($null, '', 'plain', '=SUM(A1)', '+1', '-2', '@x', 'say "hi"', $true, $false, [long]42, 3.5, [datetime]'2026-01-02T03:04:05')
    $row = [ordered]@{ testId = '=id'; duration = $null; joined = $true; db = [long]7; outcome = 'say "x"' }
    $stdout = "noise`r`n  ANTIPHON_TEST_PHASE_ID  a.b.c  `r`nC1514_NATIVE 12 x.y`nC1514_NATIVE nospace`nANTIPHON_TEST_PHASE_ID a.b.c`nC1514_NATIVE 1 "
    $r = Invoke-NCBothWays { [ordered]@{
        cells = @(foreach ($v in $values) { ConvertTo-TestPhaseCsvCell -Value $v })
        native = @(Get-TestPhaseNativeIds -Text $stdout); nativeEmpty = @(Get-TestPhaseNativeIds -Text '')
        bag = @(Get-NightlyIdentityBagKey -ClassName 'A.B.C(x)' -TypeName 'T' -MethodName 'M', (Get-NightlyIdentityBagKey -ClassName ' ' -TypeName 'N.Type' -MethodName 'Run'))
        outcomes = @(foreach ($o in @('Passed', ' FAIL ', 'error', 'NotRunnable', 'pending', '', 'weird')) { ConvertTo-NightlyNormalizedOutcome -Outcome $o })
        sha = Get-NightlyFileSha256 -Path $hashed
        csvRow = (@($row.Keys | ForEach-Object { ConvertTo-TestPhaseCsvCell -Value $row[$_] }) -join ',')
    } } 'summaryHelpers'
    $rowCore = [Antiphon.Nightly.TestPhaseSummary]::Row($row, [string[]]@($row.Keys))
    # TestPhaseSummaryFast: the whole-roster tests.csv lines and the [bool]$test.incomplete scan.
    $rowTwo = [ordered]@{ testId = 'b'; duration = '1.5'; joined = $false; db = 0; outcome = '' }
    $fastRows = [Antiphon.Nightly.TestPhaseSummaryFast]::Rows(@($row, $rowTwo), [string[]]@($row.Keys))
    $fastOk = ($fastRows.Count -eq 2 -and $fastRows[0] -ceq $rowCore -and $fastRows[1] -ceq [Antiphon.Nightly.TestPhaseSummary]::Row($rowTwo, [string[]]@($row.Keys)))
    $fastOk = $fastOk -and ([Antiphon.Nightly.TestPhaseSummaryFast]::Rows(@(), [string[]]@('a')).Count -eq 0)
    foreach ($v in @($null, $false, $true, 0, 1, '', 'False', [object[]]@(), [object[]]@($false))) {
        $tests = [Collections.Generic.List[object]]::new()
        $tests.Add([ordered]@{ incomplete = $false }); $tests.Add([ordered]@{ incomplete = $v }); $tests.Add([pscustomobject]@{ other = 1 })
        $loop = $false; foreach ($test in $tests) { if ([bool]$test.incomplete) { $loop = $true } }
        $fastOk = $fastOk -and ([Antiphon.Nightly.TestPhaseSummaryFast]::AnyIncomplete($tests) -eq $loop)
    }
    # TestPhaseSummaryWrite (no PowerShell copy): summary.json against ConvertTo-Json, the two sorts against
    # Sort-Object, including the inputs a summary never carries (doubles, custom objects, Int64 overflow).
    $write = Test-NCSummaryWrite
    Assert-NC ($fastOk -and $write.Ok -and $r.Compiled -ceq $r.Fallback -and $r.Fallback.Contains($rowCore.Replace('"', '\"'))) 'summaryHelpers' ('fast ' + $fastOk + ' write ' + $write.Detail + ' compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)

    # Executed-chunk outcome rows (TestPhaseSummary.OutcomeRows/PassedCount): TRX-shaped rows through the
    # manifest writer's loop and envelope-shaped rows (JSON objects, dictionaries, nulls) through the item path.
    $trxRows = @([pscustomobject]@{ TestId = 'a'; Outcome = 'Passed' }, [pscustomobject]@{ TestId = 'b'; Outcome = 'passed' },
        [pscustomobject]@{ TestId = 'c'; Outcome = 'Failed' }, [pscustomobject]@{ TestId = 7; Outcome = $null }, [pscustomobject]@{ Other = 1 })
    $item = '{"chunk":"c1","outcomes":[{"testId":"x","outcome":"Passed"},null,{"testId":5,"outcome":"Skipped"},{"outcome":"Failed"}]}' | ConvertFrom-Json
    Assert-NCGolden 'outcomeRows' {
        $rows = [Antiphon.Nightly.TestPhaseSummary]::OutcomeRows([object[]]$trxRows, 'TestId', 'Outcome', $false)
        $passed = [Antiphon.Nightly.TestPhaseSummary]::PassedCount($rows)
        [ordered]@{
            manifest = (ConvertTo-TestPhaseExecutedChunk -Chunk 'c' -RunId 'r' -Attempt '1' -Suite 's' -PassedCount $passed -Outcomes @($rows + @($null, @{ testId = 'h'; outcome = 'Passed' })))
            item = (ConvertTo-TestPhaseExecutedChunk -Item $item -Chunk 'c' -RunId 'r' -Attempt '1' -Suite 's' -Outcomes @())
        }
    } @('"passedCount":2')
    Test-NCEvidenceRecordsMatchFallback
}

# TestPhaseRecords.cs and NightlyJson.ParseText: the timing sidecar read (density guard and the Newtonsoft
# read), the journal envelope and the owner observation, each against its PowerShell body.
function Test-NCEvidenceRecordsMatchFallback {
    $dir = Join-Path $root 'nc-records'
    [void][IO.Directory]::CreateDirectory($dir)
    $sidecars = [ordered]@{
        plain = '{"suite":"antiphon","chunk":"all-group-1","trxMissing":false,"queueWaitSeconds":1.5,"buildSeconds":3,"startedAt":"2026-11-18T01:02:03.4567890Z","slowest":[{"name":"A","seconds":0.25},{"name":"B","seconds":1}],"why":null,"empty":{},"list":[]}'
        numbers = '{"big":123456789012345678901234567890,"neg":-0,"exp":1E2,"tiny":5e-324,"long":9223372036854775807,"text":"tab\there \u00e9 \"q\""}'
        comment = "{ /* note */ `"a`": 1 }"
        trailing = '{"a":1,}'
        caseDup = '{"a":1,"A":2}'
        dup = '{"a":1,"a":2}'
        nested = '{"a":{"b":{"c":[1,[2,[3]],{"d":true}]}}}'
        dense = '{"x":[' + ((1..4100) -join ',') + ']}'
        rootArray = '[1,2]'
        bom = ([string][char]0xFEFF) + '{"a":1}'
    }
    foreach ($name in $sidecars.Keys) { [IO.File]::WriteAllText((Join-Path $dir ($name + '.json')), $sidecars[$name], [Text.UTF8Encoding]::new($false)) }
    $script:NCRecordArm = 0
    $verdicts = @(
        [pscustomobject]@{ RootExitedAtUtc = '2026-11-18T01:00:00Z'; RootStartedAtUtc = [datetime]'2026-11-18T00:59:00'; JobEmpty = 'false'; DrainMilliseconds = '12.5'; ExecutionMilliseconds = 40; ChildrenExited = 1; OutputDrained = $true; RemainingProcessCount = '2'
            MissedDescendantPids = @('7', 9, ' '); Descendants = @([pscustomobject]@{ Pid = '7'; Image = 'dotnet' }, $null, [pscustomobject]@{ pid = 12.5; ProcessName = ' '; StartedAtUtc = 's'; ExitedAtUtc = [datetime]'2026-01-02T03:04:05' }) }
        [pscustomobject]@{ jobEmpty = $true; DescendantIdentities = @('41:2026-11-18T00:00:00Z', 'bad', '42:x'); DescendantImages = @('a', ''); DescendantExitedAtUtc = @($null, 'e2'); missedDescendantPids = 42 }
        [pscustomobject]@{ Descendants = @([pscustomobject]@{ Pid = 'abc' }) }
        [pscustomobject]@{ unrelated = 1 }
        $null
    )
    Assert-NCGolden 'evidenceRecords' {
        $script:NCRecordArm++
        $sidecarRows = foreach ($name in $sidecars.Keys) {
            $value = $null; $err = ''
            try { $value = Read-QualificationSidecar -Path (Join-Path $dir ($name + '.json')) -DeadlineMilliseconds 60000 } catch { $err = $_.Exception.GetType().Name }
            [ordered]@{ name = $name; err = $err; json = ($value | ConvertTo-Json -Depth 20 -Compress); types = (@(Get-NCValueTypes $value) -join ',') }
        }
        $densities = foreach ($text in @('', 'a,b:c', ((1..10) -join ','), ('{' + ((1..6) -join ':') + '}'))) {
            [Antiphon.Nightly.TestPhaseRecords]::Dense($text, 5, $null, 0)
        }
        $context = [pscustomobject]@{ Generation = 'a' * 32; TimingRoot = 'C:\t "x"\timing'; WriterEnvironment = $null }
        $envelopes = @(
            New-TestPhaseEnvelopeText -Kind 'chunk' -Sequence 12 -PublicationId 'p1' -Context $context -Reason '' -Suite 'antiphon' -Chunk 'all-group-1' -SettledSuite 'antiphon' -SettledChunk 'all-group-1' -HeaderRun 'r' -HeaderAttempt '1' -HeaderSuite 'antiphon' -FreshJson '{"a":"\u00e9"}' -SettledJson ''
            New-TestPhaseEnvelopeText -Kind 'gap' -Sequence 1 -PublicationId '' -Context $context -Reason "why`n" -Suite '' -Chunk '' -SettledSuite '' -SettledChunk '' -HeaderRun '' -HeaderAttempt '' -HeaderSuite '' -FreshJson '' -SettledJson '')
        $owners = foreach ($i in 0..($verdicts.Count - 1)) {
            foreach ($ids in @(@('antiphon', 'all-group-1', 'run-1', '2'), @('Session-Runner', 'Chunk.A', 'bad id', ''), @('bad suite', 'c', 'r', '1'))) {
                $timing = Join-Path $dir ('owner-' + $script:NCRecordArm + '-' + $i + '-' + $ids[0].Replace(' ', '_'))
                [void][IO.Directory]::CreateDirectory($timing)
                $saved = $script:QualificationTimingRoot; $script:QualificationTimingRoot = $timing
                try { Write-TestPhaseOwnerObservation -Verdict $verdicts[$i] -LogRoot $dir -Suite $ids[0] -Chunk $ids[1] -RunId $ids[2] -Attempt $ids[3] }
                finally { $script:QualificationTimingRoot = $saved }
                @(Get-ChildItem -LiteralPath $timing -File | ForEach-Object { $_.Name + '=' + [IO.File]::ReadAllText($_.FullName) }) -join '|'
            }
        }
        [ordered]@{ sidecars = @($sidecarRows); densities = @($densities); envelopes = $envelopes; owners = @($owners) }
    } @('System.Numerics.BigInteger', 'missedDescendantPids', 'owner-Session-Runner-Chunk.A-run-0.json', 'object,a:,System.Int64')
    Test-NCCoveragePartsMatchFallback
}

# NightlyCoverageParts.cs: the roster digest, the present-row count of the coverage verdict and the latest
# diagnostic log, each against its PowerShell body (and the refusals the compiled path hands back to it).
function Test-NCCoveragePartsMatchFallback {
    # A hashtable entry's reasonClass key is not a PSObject property, so it reads as 'platform'; the last
    # roster's 'Platform' fails the case-sensitive check and must raise the PowerShell error.
    $rosters = @(
        @([pscustomobject]@{ platform = 'windows'; test = 'N.C.M'; reasonPrefix = 'p'; reasonClass = 'live-context' }, $null,
          [pscustomobject]@{ platform = 'linux'; test = 'A.B.C'; reasonPrefix = 'q' }, @{ platform = 'windows'; test = 'H.T.M'; reasonPrefix = 'h'; reasonClass = 'bogus' },
          [pscustomobject]@{ platform = 'windows'; test = 'N.C.A'; reasonPrefix = 'r'; ReasonClass = '  ' }),
        @(),
        $null,
        @([pscustomobject]@{ platform = 'windows'; test = 'X.Y.Z'; reasonPrefix = 'z'; reasonClass = 'Platform' }))
    $logs = Join-Path $root 'nc-logs'
    $cases = [ordered]@{
        empty = @()
        diagNewest = @(@('a.diag', -30), @('b.diag', -10), @('c.log', 0))
        noDiag = @(@('a.log', -20), @('b.txt', -5))
        diagTie = @(@('a.diag', -10), @('b.diag', -10))
        upperExt = @(@('A.DIAG', -10), @('b.log', 0))
        hidden = @(@('a.diag', -10), @('h.diag', 0, 'hidden'))
    }
    $base = [datetime]::UtcNow.AddHours(-1)
    foreach ($name in $cases.Keys) {
        $d = Join-Path $logs $name
        [void][IO.Directory]::CreateDirectory($d)
        foreach ($f in $cases[$name]) {
            $p = Join-Path $d $f[0]; [IO.File]::WriteAllText($p, $f[0]); [IO.File]::SetLastWriteTimeUtc($p, $base.AddSeconds($f[1]))
            if ($f.Count -gt 2) { [IO.File]::SetAttributes($p, [IO.FileAttributes]::Hidden) }
        }
    }
    $rows = @([pscustomobject]@{ Identity = 'a'; Outcome = 'NotExecuted' }, $null, [pscustomobject]@{ Identity = 'b'; Outcome = 'Passed' })
    Assert-NCGolden 'coverageParts' {
        $digests = foreach ($roster in $rosters) { try { Get-NightlyExpectedSkipRosterDigest -Entries $roster } catch { 'throw:' + $_.Exception.Message } }
        $latest = foreach ($name in $cases.Keys) {
            $found = Get-NightlyLatestDiagnosticLog -Directory (Join-Path $logs $name)
            if ($null -eq $found) { $name + '=null' } else { $name + '=' + [IO.Path]::GetFileName($found) }
        }
        $verdicts = foreach ($exit in @(0, 8)) {
            foreach ($set in @($rows, @($null), @())) {
                $v = ConvertTo-NightlyCoverageVerdict -TerminalRows $set -ProcessExit $exit -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash 'p' -ExpectedPolicyHash 'p' `
                    -Platform 'windows' -ExpectedSkipRoster @([pscustomobject]@{ platform = 'windows'; test = 'a'; reasonPrefix = 'p' })
                [ordered]@{ exit = $exit; tolerated = $v.processExitTolerated; tests = $v.testsPassed; coverage = $v.coverageComplete; reasons = @($v.reasons); digest = $v.rosterDigest }
            }
        }
        [ordered]@{ digests = @($digests); latest = @($latest); verdicts = @($verdicts) }
    } @('throw:expected-skip roster reasonClass Platform', 'diagNewest=b.diag', 'diagTie=a.diag', 'hidden=a.diag')
    Test-NCActiveChunkGolden
}

# The active-chunk marker (TestPhaseRecords.ActiveChunkIdentity/ActiveChunkJson, NightlyJson.ParseText with
# dates): identity reads over valid and refused records, plan reads, and the record text. The golden matched
# the removed PowerShell bodies on every case.
function Test-NCActiveChunkGolden {
    $dir = Join-Path $root 'nc-active'
    [void][IO.Directory]::CreateDirectory($dir)
    $h = 'a' * 64
    $valid = [ordered]@{ format = 'antiphon-qualification-active-chunk-v1'; state = 'Active'; generation = 3; runId = 'run-1'; attempt = '2'; suite = 'antiphon'; chunk = 'all-group-1'
        planFile = 'groups-planned-antiphon.json'; planDigest = $h; sourceDigest = ('b' * 40); policyDigest = $h; costFileDigest = $h; assemblyDigest = $h }
    $variant = {
        param($changes, [string[]]$drop = @())
        $v = [ordered]@{}
        foreach ($k in $valid.Keys) { if ($drop -notcontains $k) { $v[$k] = $valid[$k] } }
        foreach ($k in $changes.Keys) { $v[$k] = $changes[$k] }
        $v | ConvertTo-Json -Depth 5
    }
    $texts = [ordered]@{
        valid = & $variant @{}
        idle = & $variant @{ state = 'Idle'; generation = 1 }
        stateCase = & $variant @{ state = 'active' }
        gen0 = & $variant @{ generation = 0 }
        genStr = & $variant @{ generation = '1' }
        genDouble = & $variant @{ generation = 1.5 }
        genBig = & $variant @{ generation = 99999999999 }
        noGen = & $variant @{} @('generation')
        noFormat = & $variant @{} @('format')
        runIdDate = & $variant @{ runId = '2026-01-01T00:00:00' }
        attempt0 = & $variant @{ attempt = '0' }
        suiteUpper = & $variant @{ suite = 'Antiphon'; planFile = 'groups-planned-Antiphon.json' }
        planFile = & $variant @{ planFile = 'groups-planned-other.json' }
        digestUpper = & $variant @{ planDigest = ('A' * 64) }
        sourceNum = & $variant @{ sourceDigest = 5 }
        hasIdentity = & $variant @{ identityDigest = 'x' }
        extra = & $variant @{ extra = @(1, 2) }
        bom = ([string][char]0xFEFF) + (& $variant @{})
        array = '[1,2]'
        comment = '{ /* c */ ' + (& $variant @{}).Substring(1)
        trailing = (& $variant @{}).TrimEnd().TrimEnd('}') + ',}'
        empty = ''
    }
    foreach ($name in $texts.Keys) {
        $d = Join-Path $dir $name; [void][IO.Directory]::CreateDirectory($d)
        # LF on every platform, so the identity digests in the golden do not depend on ConvertTo-Json's line ends.
        [IO.File]::WriteAllText((Join-Path $d 'active-chunk.json'), $texts[$name].Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    }
    $planTexts = @('{"version":1,"groups":[{"chunk":"a","plannedSeconds":1.5}],"runId":"r","attempt":"1","at":"2026-01-01T00:00:00Z"}', '{"a":1,"A":2}', '{"a":[]}', ' {"x": null} ')
    $records = @(
        [ordered]@{ format = 'antiphon-qualification-active-chunk-v1'; state = 'Active'; generation = 1; runId = 'r'; attempt = '1'; suite = 's'; chunk = 'c'; planFile = 'f'; planDigest = $h }
        [ordered]@{ format = 'x'; generation = [long]12; s = 'quote"in' }
        # Every escape ConvertTo-Json makes, and characters it leaves alone (DEL, apostrophe, HTML, non-ASCII,
        # a surrogate pair), in a value and in a key.
        [ordered]@{ ('k"' + [char]9 + '\') = ('a\b' + (-join ([char[]](8, 9, 10, 12, 13, 1, 31, 127, 0x85, 0x2028, 0x2029, 0xE9)))) + "'<>&" + [char]::ConvertFromUtf32(0x1F600); n = [int]-7 })
    # Not strings or integers: the writer never builds these, so the core refuses them.
    $refused = @([ordered]@{ format = 'x'; d = 1.5 }, [ordered]@{ format = 'x'; b = $true }, [ordered]@{})
    $typeList = { param($o) @($o.PSObject.Properties | ForEach-Object { $_.Name + ':' + $(if ($null -eq $_.Value) { 'null' } else { $_.Value.GetType().Name }) }) -join ',' }
    Assert-NCGolden 'activeChunk' {
        $ids = foreach ($name in $texts.Keys) {
            $r = Read-QualificationActiveChunkIdentity (Join-Path $dir $name)
            $name + '=' + $(if ($null -eq $r) { 'null' } else { ($r | ConvertTo-Json -Compress -Depth 5) + '|' + (& $typeList $r) })
        }
        $i = 0
        $plans = foreach ($t in $planTexts) {
            $p = Join-Path $dir ('plan-' + (++$i) + '.json'); [IO.File]::WriteAllText($p, $t)
            try { $v = Read-QualificationPlanJson $p; ($v | ConvertTo-Json -Compress -Depth 5) + '|' + (& $typeList $v) } catch { 'throw:' + $_.Exception.Message }
        }
        $jsons = foreach ($rec in $records) {
            $t = [Antiphon.Nightly.TestPhaseRecords]::ActiveChunkJson($rec)
            if ($null -eq $t) { 'null' } elseif ($t -ceq ($rec | ConvertTo-Json -Depth 12)) { 'same-as-ConvertTo-Json' } else { 'DIFFERS ' + $t }
        }
        $jsons += foreach ($rec in $refused) {
            try { 'DIFFERS ' + [Antiphon.Nightly.TestPhaseRecords]::ActiveChunkJson($rec) } catch { 'throw:' + $_.Exception.InnerException.Message }
        }
        [ordered]@{ ids = @($ids); plans = @($plans); jsons = @($jsons) }
    } @('valid={\"format\"', 'identityDigest', 'genStr=null', 'runIdDate=null', 'same-as-ConvertTo-Json', 'throw:active-record-value d', 'throw:active-record-empty')
}

# The .NET type of every value in a read sidecar, depth first, so a parse that matches only as JSON still fails.
function Get-NCValueTypes($Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [System.Management.Automation.PSCustomObject] -or ($Value -is [psobject] -and $Value.PSObject.BaseObject -is [System.Management.Automation.PSCustomObject])) {
        'object'
        foreach ($p in $Value.PSObject.Properties) { $p.Name + ':'; Get-NCValueTypes $p.Value }
        return
    }
    if ($Value -is [array]) { 'array' + $Value.Count; foreach ($item in $Value) { Get-NCValueTypes $item }; return }
    $Value.GetType().FullName
}

function Test-NCVerdictsMatchFallback {
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
    $nodes = @(Get-NCFixtureNodes 1500)
    $outcomes = @('Passed', 'Failed', 'pass', ' Error ', 'Skipped', 'NotExecuted', 'pending', 'weird', '', $null)
    $trx = [Collections.Generic.List[object]]::new()
    $terminal = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt 400; $i++) {
        $n = $nodes[$i]
        $trx.Add([pscustomobject]@{ ClassName = $n.ClassName; MethodName = $n.Method; Outcome = $outcomes[$i % $outcomes.Count]; Identity = ($n.ClassName + '.' + $n.Method) })
        # The diagnostic side drops some rows and doubles others, so the bags disagree both ways.
        if ($i % 17 -ne 0) { $terminal.Add([pscustomobject]@{ Uid = $n.Uid; ClassName = $n.ClassName; Type = $n.Type; Method = $n.Method; Outcome = $outcomes[($i + 3) % $outcomes.Count] }) }
        if ($i % 29 -eq 0) { $terminal.Add([pscustomobject]@{ Uid = $n.Uid; ClassName = $n.ClassName; Type = $n.Type; Method = ''; MethodName = $n.Method; Outcome = 'Passed' }) }
    }
    $trx.Add($null); $terminal.Add($null)
    $classes = @($nodes | ForEach-Object { $_.ClassName } | Select-Object -Unique)
    $classSets = @(, @()) + @(, @('')) + @(, @($classes | Select-Object -First 4)) + @(, @(@($classes | Select-Object -First 2) + 'No.Such.Class' + $classes[5].Split('(')[0]))
    $entry = [pscustomobject]@{ Document = [pscustomobject]@{ Nodes = $nodes }; Views = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal) }
    # ConvertTo-NightlyCoverageVerdict has no PowerShell copy left: its rows against the golden the copy produced.
    Assert-NCGolden 'coverageVerdicts' {
        [ordered]@{
            verdict = ConvertTo-NightlyCoverageVerdict -TerminalRows $trx.ToArray() -ProcessExit 0 -Sha 'a' -ExpectedSha 'A' -GitRef 'r' -ExpectedRef 'r' -PolicyHash 'p' -ExpectedPolicyHash 'p'
            verdictNull = ConvertTo-NightlyCoverageVerdict -TerminalRows $null -ProcessExit 1 -Sha 'a' -ExpectedSha 'b' -GitRef '' -ExpectedRef '' -PolicyHash '' -ExpectedPolicyHash ''
            verdictEmpty = ConvertTo-NightlyCoverageVerdict -TerminalRows @() -ProcessExit 0 -Sha '' -ExpectedSha '' -GitRef '' -ExpectedRef '' -PolicyHash '' -ExpectedPolicyHash ''
        }
    } @('failed-row', 'zero-terminal-rows', 'sha-mismatch')
    $r = Invoke-NCBothWays {
        $entry.Views.Clear()
        $view = Get-NightlyDiscoveryIndexView -Entry $entry -ProfileExclusions @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile rc -SuiteId antiphon -Platform windows) -ProfileAware
        [ordered]@{
            perClassSet = @(foreach ($set in $classSets) { [ordered]@{
                cross = Get-NCSortedCross (Test-NightlyTrxDiagnosticCrossCheck -TrxRows $trx.ToArray() -TerminalNodes $terminal.ToArray() -RequiredClasses $set)
                present = Test-NightlyRequiredClassesPresent -TerminalRows $trx.ToArray() -RequiredClasses $set
                required = @(Get-NightlyDiscoveryIndexRequiredUids -View $view -RequiredClasses $set)
            } })
            expanded = Test-NightlyExpandedRowsPresent -DiscoveryNodes $nodes -TerminalRows $terminal.ToArray() -RequiredUids @($nodes | Select-Object -First 300 | ForEach-Object { $_.Uid })
            expandedNull = Test-NightlyExpandedRowsPresent -DiscoveryNodes $nodes -TerminalRows $null -RequiredUids $null
            crossNull = Test-NightlyTrxDiagnosticCrossCheck -TrxRows $null -TerminalNodes $null -RequiredClasses $null
        }
    } 'verdicts'
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $r.Fallback.Contains(' trx=')) 'verdicts' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)
}

function Test-NCLaunchPlanMatchesFallback {
    $cases = @(
        @(@('N.S.Alpha', 'N.S.Beta'), '/*/N.S/(Alpha*)|(Beta*)/*'),
        @(@('N.S.Alpha', 'N.S.AlphaTwo'), '/*/N.S/(Alpha*)|(AlphaTwo*)/*'),
        @(@('N.S.Alpha(ctor)', 'N.S.Beta'), '/*/N.S/(Alpha*)|(Beta*)/*'),
        @(@('N.S.Alpha', 'M.S.Beta'), '/*/N.S/(Alpha*)|(Beta*)/*'),
        @(@('N.S.Alpha', 'N.S.alpha'), '/*/N.S/(Alpha*)|(alpha*)/*'),
        @(@('N.S.Alpha'), '/*/N.S/(Alpha*)|(Beta*)/*'),
        @(@('N.S.Alpha', 'N.S.Beta', 'N.S.Gamma'), '/*/N.S/(Alpha*)|(Beta*)|(Gamma*)/*'),
        @(@('N.S.Alpha', 'N.S.Beta'), '/*/N.S/(Alpha*)/*'),
        @(@('N.S.Alpha', 'N.S.Beta'), '/*/N.S/Alpha*|Beta*/*'),
        @(@('N.S.Alpha', 'N.S.Alpha'), '/*/N.S/(Alpha*)|(Beta*)/*'),
        @($null, '/*/N.S/(Alpha*)|(Beta*)/*'),
        @(@('N.S.Alpha', 'N.S.Beta'), ''))
    $r = Invoke-NCBothWays { @(foreach ($c in $cases) { [bool](Test-NightlyClassGroupFilter -Classes $c[0] -GroupFilter $c[1]) }) } 'groupFilter'
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $r.Fallback.Contains('true') -and $r.Fallback.Contains('false')) 'groupFilter' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
    # Split-NightlyItemsBySeconds (NightlyLaunchPlan.FirstFitBins): first fit with ties, items with no Seconds
    # (the 0.5 default), a string Seconds, items at or over the target, and the operand cap.
    $items = @(
        [pscustomobject]@{ Key = 'b'; Seconds = 30.0 }, [pscustomobject]@{ Key = 'a'; Seconds = 30.0 },
        [pscustomobject]@{ Key = 'big'; Seconds = 600.0 }, [pscustomobject]@{ Key = 'edge'; Seconds = 120.0 },
        [pscustomobject]@{ Key = 'none' }, [pscustomobject]@{ Key = 'text'; Seconds = '45.5' },
        [pscustomobject]@{ Key = 'c'; Seconds = 0.1 }, [pscustomobject]@{ Key = 'd'; Seconds = 0.1 }, [pscustomobject]@{ Key = 'e'; Seconds = 89.9 })
    Assert-NCGolden 'packing' {
        foreach ($cap in @(2, 3, 0)) {
            foreach ($bin in @(Split-NightlyItemsBySeconds -Items $items -TargetSeconds 120 -MaxOperands $cap)) {
                [ordered]@{ cap = $cap; keys = (@($bin.Items | ForEach-Object { $_.Key }) -join ','); seconds = $bin.Seconds; closed = $bin.Closed; type = $bin.Items.GetType().Name }
            }
        }
    } @('"closed":true')
}

function Test-NCParentProcessMatchesFallback {
    # The compiled parent read and Win32_Process agree, for this process and for a missing one.
    $r = Invoke-NCBothWays { @(Get-NightlyParentProcess -ProcessId $PID; Get-NightlyParentProcess -ProcessId 2147483647) }
    $hasParent = [IO.Path]::DirectorySeparatorChar -ne [char]92 -or $r.Fallback -match '"Id":[1-9]'
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $hasParent) 'parentProcess' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
}

function Test-NCProjectWalkMatchesFallback {
    $tree = Join-Path $root 'nc-projects'
    $test = '<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>'
    $files = @('a/One.csproj', 'a/b/Two.csproj', 'bin/Skip.csproj', 'BIN/Skip.csproj', 'obj/Skip.csproj', 'x/obj/Skip.csproj',
        'bin-debug/Skip.csproj', 'a/bin-named.csproj', 'Bin-Upper/Kept.csproj', 'objects/Kept.csproj', 'binary/Kept.csproj',
        '.hidden/Skip.csproj', 'c/NotTest.csproj', 'c/Other.txt')
    foreach ($relative in $files) {
        $path = Join-Path $tree $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        $text = $test; if ($relative -like 'c/*') { $text = '<Project />' }
        [IO.File]::WriteAllText($path, $text)
    }
    # Hidden is the attribute on Windows and the leading dot on Linux, which ignores the attribute; .NET reports both.
    $hidden = Get-Item -LiteralPath (Join-Path $tree '.hidden') -Force
    $hidden.Attributes = $hidden.Attributes -bor [IO.FileAttributes]::Hidden
    $r = Invoke-NCBothWays { @(Get-NightlyIsTestProjectPaths -RepoRoot $tree) } 'projectWalk'
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $r.Fallback.Contains('Kept') -and -not $r.Fallback.Contains('Skip')) 'projectWalk' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
    # In a git checkout the walk skips ignored directories (nested ones too); untracked projects are kept.
    $repo = Join-Path $root 'nc-git-projects'
    foreach ($relative in @('kept/Untracked.csproj', 'deep/a/Kept.csproj', 'out/Skip.csproj', 'deep/cache/Skip.csproj', 'deep/a/scratch/Skip.csproj')) {
        $path = Join-Path $repo $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        [IO.File]::WriteAllText($path, $test)
    }
    [IO.File]::WriteAllText((Join-Path $repo '.gitignore'), "out/`ncache/`n")
    [IO.File]::WriteAllText((Join-Path $repo 'deep/a/.gitignore'), "scratch/`n")
    $null = & git -C $repo init -q 2>&1
    $r = Invoke-NCBothWays { @(Get-NightlyIsTestProjectPaths -RepoRoot $repo) } 'gitIgnoredWalk'
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $r.Fallback -ceq '["deep/a/Kept.csproj","kept/Untracked.csproj"]') 'gitIgnoredWalk' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
}

function Test-NCEvidenceFilesMatchFallback {
    $dir = Join-Path $root 'nc-files'
    [void][IO.Directory]::CreateDirectory((Join-Path $dir 'sub'))
    [IO.File]::WriteAllText((Join-Path $dir 'small.json'), '{"a":1}')
    [IO.File]::WriteAllText((Join-Path $dir 'empty.json'), '')
    [IO.File]::WriteAllBytes((Join-Path $dir 'big.bin'), [byte[]](1..200 | ForEach-Object { [byte]$_ }))
    [IO.File]::WriteAllBytes((Join-Path $dir 'exact.bin'), [byte[]](1..64 | ForEach-Object { [byte]$_ }))
    [IO.File]::WriteAllText((Join-Path $dir 'hidden.json'), 'hidden')
    $hidden = Get-Item -LiteralPath (Join-Path $dir 'hidden.json') -Force
    $hidden.Attributes = $hidden.Attributes -bor [IO.FileAttributes]::Hidden
    [IO.File]::WriteAllText((Join-Path $dir 'readonly.json'), 'ro')
    [IO.File]::SetAttributes((Join-Path $dir 'readonly.json'), [IO.FileAttributes]::ReadOnly)
    $names = @('', 'missing.json', 'sub', 'small.json', 'empty.json', 'big.bin', 'exact.bin', 'hidden.json', 'readonly.json', 'nope/x.json')
    $paths = @(foreach ($n in $names) { if ($n) { Join-Path $dir $n } else { '' } })
    $r = Invoke-NCBothWays { @(foreach ($p in $paths) { $i = Get-TestPhasePathIdentity -Path $p; [ordered]@{ path = $p; kind = $i.Kind; size = $i.Size } }) }
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'pathIdentity' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
    $r = Invoke-NCBothWays {
        $rows = @(foreach ($p in $paths) {
            foreach ($max in 64, 262144) {
                $b = Read-TestPhaseBoundedBytes -Path $p -MaxBytes $max -DeadlineMilliseconds 5000
                [ordered]@{ path = $p; max = $max; bytes = $b.Bytes; complete = $b.Complete; refused = $b.Refused; kind = $b.Kind }
            }
        })
        $spent = [Diagnostics.Stopwatch]::StartNew(); Start-Sleep -Milliseconds 20
        $b = Read-TestPhaseBoundedBytes -Path (Join-Path $dir 'small.json') -DeadlineMilliseconds 5 -Watch $spent
        $rows + @([ordered]@{ budget = $true; bytes = $b.Bytes; complete = $b.Complete; refused = $b.Refused; kind = $b.Kind })
    } 'boundedRead' $root
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'boundedRead' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
    # Queued evidence writes answer as the landed files will: identity, existence, text, bytes and hash.
    $queued = Join-Path $dir 'queued'
    [void][IO.Directory]::CreateDirectory($queued)
    $observe = {
        @(foreach ($n in 'a.json', 'b.json', 'lines.txt') {
            $p = Join-Path $queued $n
            $i = Get-TestPhasePathIdentity -Path $p
            [ordered]@{ name = $n; kind = $i.Kind; size = $i.Size; exists = (Test-NightlyEvidencePath $p); leaf = (Test-NightlyEvidencePath $p 'Leaf')
                text = (Read-NightlyEvidenceText $p); bytes = (Read-NightlyEvidenceBytes $p); lines = @(Read-NightlyEvidenceLines $p); hash = (Get-NightlyFileSha256 $p) }
        })
    }
    Enable-NightlyDeferredEvidenceWrites
    try {
        Write-TestPhaseTextAtomic -Path (Join-Path $queued 'a.json') -Text ('{"queued":"caf' + [char]0xe9 + '"}')
        Write-TestPhaseTextAtomic -Path (Join-Path $queued 'b.json') -Text 'first'
        Write-TestPhaseTextAtomic -Path (Join-Path $queued 'b.json') -Text 'second, longer'
        Write-NightlyEvidenceLines (Join-Path $queued 'lines.txt') @('one', 'two')
        $deferred = $script:NightlyDeferEvidenceWrites
        $before = (& $observe) | ConvertTo-Json -Depth 6 -Compress
    } finally { Disable-NightlyDeferredEvidenceWrites }
    $after = (& $observe) | ConvertTo-Json -Depth 6 -Compress
    Assert-NC ($deferred -and $before -ceq $after -and $after.Contains('second, longer')) 'queuedEvidence' ('queued ' + $before + ' landed ' + $after)
}

function Test-NCDocumentsMatchFallback {
    $source = @(Get-NCFixtureNodes 3000)
    $nodes = [Collections.Generic.List[object]]::new()
    $i = 0
    foreach ($n in $source) {
        $row = [pscustomobject]@{ uid = $n.Uid; assembly = 'Antiphon.Tests'; namespace = $n.Namespace; type = $n.Type; method = $n.Method
            signature = ('(' + $i + ')'); state = ''; categories = $n.Categories }
        if ($i % 7 -eq 0) { $row | Add-Member NoteProperty excluded $true }
        elseif ($i % 11 -eq 0) { $row | Add-Member NoteProperty excluded 'false' }
        if ($i % 13 -eq 0) { $row.categories = @('OptIn') }
        if ($i % 17 -eq 0) { $row.categories = 'explicit' }
        if ($i % 19 -eq 0) { $row.namespace = '' }
        if ($i % 23 -eq 0) { $row.state = 'Discovered' }
        $nodes.Add($row); $i++
    }
    $nodes.Add([ordered]@{ uid = 'dict-node'; type = 'T'; method = 'M'; excluded = $true; categories = @('A', 'B') })
    $r = Invoke-NCBothWays {
        $doc = ConvertTo-NightlyDiscoveryDocument -Nodes $nodes.ToArray() -AssemblyHash 'abc'
        $json = $doc | ConvertTo-Json -Depth 12 -Compress
        $back = ConvertFrom-NightlyDiscoveryDocument -Object ($json | ConvertFrom-Json) -ExpectedFormat $script:NightlyDiscoveryFormat
        $refusals = @(foreach ($bad in @(
            @([pscustomobject]@{ uid = 'Same'; type = 'T'; method = 'M' }, [pscustomobject]@{ uid = 'same'; type = 'T'; method = 'M' }),
            @([pscustomobject]@{ uid = ''; type = 'T'; method = 'M' }),
            @([pscustomobject]@{ uid = 'u'; type = 'T'; method = ' ' }),
            $null)) {
            $o = [pscustomobject]@{ format = $script:NightlyDiscoveryFormat; tunitVersion = $script:NightlySupportedTunit; mtpVersion = $script:NightlySupportedMtp; assemblyHash = 'h'; nodes = $bad }
            try { [void](ConvertFrom-NightlyDiscoveryDocument -Object $o -ExpectedFormat $script:NightlyDiscoveryFormat); 'accepted' } catch { $_.Exception.Message }
        })
        [ordered]@{ json = $json; back = $back; canonical = (ConvertTo-NightlyCanonicalJson -Object $doc); refusals = $refusals }
    } 'discoveryDocuments'
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'discoveryDocuments' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)
    $mixed = [ordered]@{
        zeta = 1; Alpha = [long]2; beta = 1.5; Gamma = [decimal]2.25; '' = 'dropped'; flag = $false; none = $null
        text = ('q"uote\back' + "`r`n`t" + [char]0xe9); list = @(1, 'two', $null, @(3)); single = @('one')
        obj = [pscustomobject]@{ b = 2; a = 1 }; table = @{ Y = 'y'; x = 'x' }; ordered = [ordered]@{ m = @{}; l = @() }
        small = [byte]7; float = [single]0.5
    }
    $r = Invoke-NCBothWays { @((ConvertTo-NightlyCanonicalJson -Object $mixed), (ConvertTo-NightlyCanonicalJson -Object @(1, 2)), (ConvertTo-NightlyCanonicalJson -Object 'plain'), (ConvertTo-NightlyCanonicalJson -Object $null)) }
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'canonicalJson' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
}

# Get-NightlyExpectedSkipSourceGaps (NightlyTestSources.cs) over a fixture tests/ tree, and its read cache: a
# same-length rewrite of a settled (cached) file must be seen by the next scan.
function Test-NCSourceGapsMatchFallback {
    $fixture = Join-Path $root 'source-gaps'
    $tests = Join-Path $fixture 'tests'
    foreach ($dir in @('A', 'A/bin', 'A/obj', 'B/bin-x', 'B/Deep', 'C')) { [void][IO.Directory]::CreateDirectory((Join-Path $tests $dir)) }
    $files = [ordered]@{
        'A/One.cs' = "namespace N { public sealed class Alpha { public void Run() { } void Helper () { } } }"
        'A/bin/Hidden.cs' = "class Hidden { void Run() { } }"
        'A/obj/Gen.cs' = "class Generated { void Run() { } }"
        'B/bin-x/Skip.cs' = "class Skipped { void Run() { } }"
        'B/Deep/Two.cs' = "partial class Beta { async Task Go(){} } class AlphaBeta { void Other() { } }"
        'C/Three.cs' = "// class Gamma mentioned only`nclass gamma { void Run() { } }"
    }
    foreach ($key in $files.Keys) { [IO.File]::WriteAllText((Join-Path $tests $key), $files[$key]) }
    $entries = @(foreach ($test in @('N.Alpha.Run', 'N.Alpha.Helper', 'N.Alpha.Missing', 'N.ALPHA.run', 'N.Hidden.Run', 'N.Generated.Run',
        'N.Skipped.Run', 'X.Beta.Go', 'Beta.Go', 'Beta', '', '.Go', 'N.AlphaBeta.Other', 'N.Gamma.Run', 'N.gamma.Run', 'N.Alpha.Run')) {
        [pscustomobject]@{ platform = 'windows'; test = $test }
    }) + @($null, [pscustomobject]@{ platform = 'linux'; test = 'N.Nope.Run' })
    $r = Invoke-NCBothWays { @(Get-NightlyExpectedSkipSourceGaps -RepoRoot $fixture -Platform 'windows' -Entries $entries) } 'sourceGaps'
    Assert-NC ($r.Core -and $r.Compiled -ceq $r.Fallback -and $r.Compiled.Contains('N.Alpha.Missing') -and -not $r.Compiled.Contains('"N.Alpha.Run"')) 'sourceGaps' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
    $path = Join-Path $tests 'C/Cache.cs'
    [IO.File]::WriteAllText($path, 'class Cached { void Alpha() { } }')
    [IO.File]::SetLastWriteTimeUtc($path, [DateTime]::UtcNow.AddHours(-1))
    $one = @([Antiphon.Nightly.NightlyTestSources]::ExpectedSkipSourceGaps($tests, [string[]]@('N.Cached.Alpha')))
    [IO.File]::WriteAllText($path, 'class Cached { void Omega() { } }')
    $two = @([Antiphon.Nightly.NightlyTestSources]::ExpectedSkipSourceGaps($tests, [string[]]@('N.Cached.Alpha')))
    $three = [Antiphon.Nightly.NightlyTestSources]::ReadText($path)
    Assert-NC ($one.Count -eq 0 -and $two.Count -eq 1 -and $three.Contains('Omega')) 'sourceTextCache' ('settled ' + $one.Count + ' rewritten ' + $two.Count)
    # Assembly digest cache: a same-length rewrite of a settled (cached) file is rehashed, and a file still
    # inside the racy window is never served from the cache.
    $dll = Join-Path $fixture 'Fixture.dll'
    [IO.File]::WriteAllText($dll, 'assembly-one')
    [IO.File]::SetLastWriteTimeUtc($dll, [DateTime]::UtcNow.AddHours(-1))
    $h1 = Get-NightlyAssemblyFileSha256 -Path $dll
    $h1Again = Get-NightlyAssemblyFileSha256 -Path $dll
    [IO.File]::WriteAllText($dll, 'assembly-two')
    $h2 = Get-NightlyAssemblyFileSha256 -Path $dll
    [IO.File]::WriteAllText($dll, 'assembly-xyz')
    $h3 = Get-NightlyAssemblyFileSha256 -Path $dll
    Assert-NC ($h1 -ceq $h1Again -and $h1 -ceq (Get-NightlySha256Text -Text 'assembly-one') -and
        $h2 -ceq (Get-NightlySha256Text -Text 'assembly-two') -and $h3 -ceq (Get-NightlySha256Text -Text 'assembly-xyz') -and $h2 -cne $h1) 'assemblyHashCache' ('h1 ' + $h1 + ' h2 ' + $h2 + ' h3 ' + $h3)
}

# Read-NightlyTrxIdentities' row loop (NightlyTrx.cs) against the fallback: rows, column order and refusals.
function Test-NCTrxMatchesFallback {
    $dir = Join-Path $root 'trx'
    [void][IO.Directory]::CreateDirectory($dir)
    $vs = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
    $def = '<UnitTest id="{0}" name="{1}.{2}"><TestMethod className="{1}" name="{2}" /></UnitTest>'
    $res = '<UnitTestResult testId="{0}" testName="{1}" outcome="{2}" />'
    $cases = [ordered]@{
        'ns-plain' = "<TestRun xmlns=`"$vs`"><TestDefinitions>" + ($def -f 'a', 'N.C', 'M1') + ($def -f 'b', 'N.C', 'M2') + '</TestDefinitions><Results>' + ($res -f 'a', 'M1', 'Passed') + ($res -f 'b', 'M2', 'Failed') + '</Results></TestRun>'
        'bare' = '<TestRun><TestDefinitions>' + ($def -f 'a', 'N.C', 'M1') + '</TestDefinitions><Results>' + ($res -f 'a', 'M1', 'Skipped') + '</Results></TestRun>'
        'no-results-wrapper' = '<TestRun><TestDefinitions>' + ($def -f 'a', 'N.C', 'M1') + '</TestDefinitions><Other>' + ($res -f 'a', 'M1', 'Passed') + '</Other></TestRun>'
        'debug-trace' = "<TestRun xmlns=`"$vs`"><TestDefinitions>" + ($def -f 'a', 'N.C', 'M1') + '</TestDefinitions><Results><UnitTestResult testId="a" testName="M1" outcome="Failed"><Output><StdOut>x</StdOut><DebugTrace>first &amp; <b>bold</b></DebugTrace><DebugTrace>second</DebugTrace></Output><output><DebugTrace>lower</DebugTrace></output><Output><debugtrace>no</debugtrace><DebugTrace>last</DebugTrace></Output></UnitTestResult></Results></TestRun>'
        'method-case' = '<TestRun><TestDefinitions><UnitTest id="a" name="x"> <testmethod className="N.C" name="M1" /><TestMethod className="N.D" name="M2" /></UnitTest></TestDefinitions><Results>' + ($res -f 'a', 'M1', 'Passed') + '</Results></TestRun>'
        'id-case' = '<TestRun><TestDefinitions>' + ($def -f 'A', 'N.C', 'Up') + ($def -f 'a', 'N.C', 'Low') + '</TestDefinitions><Results>' + ($res -f 'A', 'x', 'Passed') + '</Results></TestRun>'
        'duplicate' = '<TestRun><TestDefinitions>' + ($def -f 'a', 'N.C', 'M1') + '</TestDefinitions><Results>' + ($res -f 'a', 'M1', 'Passed') + ($res -f 'A', 'M1', 'Passed') + '</Results></TestRun>'
        'missing-def' = '<TestRun><TestDefinitions>' + ($def -f 'a', 'N.C', 'M1') + '</TestDefinitions><Results>' + ($res -f 'z', 'Zed', 'Passed') + '</Results></TestRun>'
        'missing-id' = '<TestRun><TestDefinitions><UnitTest name="n"><TestMethod className="N.C" name="M" /></UnitTest></TestDefinitions><Results><UnitTestResult testName="t" outcome="Passed" /></Results></TestRun>'
        'no-method' = '<TestRun><TestDefinitions><UnitTest id="a" name="n" /></TestDefinitions><Results>' + ($res -f 'a', 'n', 'Passed') + '</Results></TestRun>'
        'blank-class' = '<TestRun><TestDefinitions>' + ($def -f 'a', ' ', 'M1') + '</TestDefinitions><Results>' + ($res -f 'a', 'M1', 'Passed') + '</Results></TestRun>'
        'empty' = "<TestRun xmlns=`"$vs`"><TestDefinitions /><Results /></TestRun>"
        'one-row' = '<TestRun><TestDefinitions>' + ($def -f 'a', 'N.C(Arg)', 'M1') + '</TestDefinitions><Results>' + ($res -f 'a', 'M1', '') + '</Results></TestRun>'
    }
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($key in $cases.Keys) {
        $path = Join-Path $dir ($key + '.trx')
        [IO.File]::WriteAllText($path, $cases[$key])
        $paths.Add($path)
    }
    $big = Join-Path $dir 'big.trx'
    Write-C487Trx $big @(foreach ($n in @(Get-NCFixtureNodes 2500)) { [pscustomobject]@{ Id = $n.Uid; ClassName = $n.ClassName; MethodName = $n.Method; Outcome = 'Passed' } })
    $paths.Add($big)
    $r = Invoke-NCBothWays {
        @(foreach ($path in $paths) {
            try { , @(Read-NightlyTrxIdentities -TrxPath $path) } catch { 'refused: ' + $_.Exception.Message }
        })
    }
    $refusals = ([regex]::Matches($r.Compiled, 'refused: ')).Count
    Assert-NC ($r.Core -and $r.Compiled -ceq $r.Fallback -and $refusals -eq 5) 'trxIdentities' ('refusals ' + $refusals + ' compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)
}

# Read-NightlyJsonFile / Write-NightlyJsonFile (NightlyJson.cs, System.Text.Json) against ConvertFrom-Json and
# ConvertTo-Json: the same objects read, the same content written (not the same bytes), and every document the core
# does not read strictly (comments, a root array, a repeated name, UTF-16) handed to ConvertFrom-Json.
function Test-NCJsonFilesMatchFallback {
    $dir = Join-Path $root 'json-files'
    [void][IO.Directory]::CreateDirectory($dir)
    $e = [char]0x00e9
    $docs = [ordered]@{
        'plain' = '{"s":"a\"b\\c' + '\' + 'u' + '00e9' + $e + '","i":1,"big":123456789012345678901234567890,"neg":-5,"d":1.0,"x":1e3,"t":true,"f":false,"n":null,"o":{"a":[],"b":[1,[2,{}]],"c":{}},"one":["only"]}'
        'dates' = '{"z":"2026-11-17T10:00:00Z","frac":"2026-11-17T10:00:00.1234567Z","off":"2026-11-17T10:00:00+02:00","none":"2026-11-17T10:00:00","day":"2026-11-17","ms":"\/Date(1700000000000)\/","word":"2026-11-17Txx:00:00"}'
        'comment' = '{"a":1 /* c */}'
        'rootArray' = '[{"a":1}]'
        'repeated' = '{"a":1,"A":2}'
        'trailing' = '{"a":1} x'
    }
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($key in $docs.Keys) { $path = Join-Path $dir ($key + '.json'); [IO.File]::WriteAllText($path, $docs[$key]); $paths.Add($path) }
    $bom = Join-Path $dir 'bom.json'; [IO.File]::WriteAllText($bom, ([string][char]0xFEFF) + '{"a":1}', [Text.UTF8Encoding]::new($true)); $paths.Add($bom)
    $utf16 = Join-Path $dir 'utf16.json'; [IO.File]::WriteAllText($utf16, '{"a":"w"}', [Text.Encoding]::Unicode); $paths.Add($utf16)
    $r = Invoke-NCBothWays {
        @(foreach ($path in $paths) {
            foreach ($strip in $false, $true) {
                try { , (Read-NightlyJsonFile -Path $path -StripBom:$strip) } catch { 'refused: ' + $_.Exception.Message }
            }
        })
    }
    Assert-NC ($r.Core -and $r.Compiled -ceq $r.Fallback -and $r.Compiled.Contains('refused: ')) 'jsonRead' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)

    $summary = Join-Path $dir 'summary.json'
    [IO.File]::WriteAllText($summary, '{"outcome":"PASS","suites":[{"id":"a","exclusions":[{"uid":"u"}],"trx":"a.trx"}],"dispatchProgress":{"liveness":"closed","suites":[{"id":"a","exclusions":[]}]}}')
    $script:NightlyCoreAvailable = $null
    $full = Read-NightlyJsonFile -Path $summary | ConvertTo-Json -Depth 8 -Compress
    $rows = Read-NightlyJsonFile -Path $summary -Omit 'suites[].exclusions', 'dispatchProgress.suites[].exclusions' | ConvertTo-Json -Depth 8 -Compress
    $top = Read-NightlyJsonFile -Path $summary -Omit 'suites', 'dispatchProgress' | ConvertTo-Json -Depth 8 -Compress
    $omitOk = $rows -ceq '{"outcome":"PASS","suites":[{"id":"a","trx":"a.trx"}],"dispatchProgress":{"liveness":"closed","suites":[{"id":"a"}]}}' -and $top -ceq '{"outcome":"PASS"}' -and $full.Contains('"uid":"u"')
    Assert-NC $omitOk 'jsonOmit' ('rows ' + $rows + ' top ' + $top)

    $value = [ordered]@{
        s = "q `" \ / `t $e $([char]0x2028) <&>"; i = 1; l = [long]5000000000; d = 1.0; d2 = 0.1; m = [decimal]'1.10'; nan = [double]::NaN
        big = [Numerics.BigInteger]::Parse('123456789012345678901234567890'); t = $true; n = $null; dbn = [DBNull]::Value; ch = [char]'x'
        utc = [datetime]::new(2026, 11, 17, 10, 0, 0, 123, [DateTimeKind]::Utc); none = [datetime]::new(2026, 11, 17, 10, 0, 0)
        off = [datetimeoffset]::new(2026, 11, 17, 10, 0, 0, [timespan]::FromHours(2)); enum = [IO.FileAttributes]::Hidden
        h = @{ a = 1 }; e = @(); one = @(1); nested = @(@(1, 2), @()); list = [Collections.Generic.List[object]]@(1, 'two')
        pso = [pscustomobject]@{ z = 1; y = [ordered]@{ x = @() } }; rows = @(foreach ($i in 1..3) { [ordered]@{ id = "r$i"; exclusions = @([ordered]@{ uid = "u$i" }) } })
    }
    # The core writes the first; ConvertTo-Json writes the others (an arbitrary object, nesting past -Depth).
    $cases = @(@{ Value = $value; Depth = 8 }, @{ Value = [ordered]@{ a = [IO.DirectoryInfo]::new($dir) }; Depth = 1 }, @{ Value = @{ deep = @{ a = @{ b = @{ c = 1 } } } }; Depth = 2 })
    $written = Join-Path $dir 'written.json'
    # Each file against what ConvertTo-Json writes for the same value, read back.
    $wrote = @(foreach ($case in $cases) {
        Write-NightlyJsonFile -Path $written -Value $case.Value -Depth $case.Depth 3>$null
        , ([IO.File]::ReadAllText($written) | ConvertFrom-Json)
    }) | ConvertTo-Json -Depth 14 -Compress
    $reference = @(foreach ($case in $cases) {
        , ($case.Value | ConvertTo-Json -Depth $case.Depth 3>$null | ConvertFrom-Json)
    }) | ConvertTo-Json -Depth 14 -Compress
    Assert-NC ($wrote -ceq $reference -and $wrote.Contains('"id":"r3"')) 'jsonWrite' ('wrote ' + $wrote + ' reference ' + $reference)
}

function Test-NCPlannerPartsGolden {
    # NightlyPlannerParts (PlannerParts.cs): the Split-NightlyItemsBySeconds order (Sort-Object, unstable for ties,
    # current culture case-insensitive keys) and Get-NightlyPrefixUnsafeNames, against the PowerShell output.
    $saved = [Globalization.CultureInfo]::CurrentCulture
    try {
        [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('en-US')
        $keys = @('b', 'B', 'a', 'A', 'ab', 'Ab', '', ([string][char]0x00E9), ('e' + [char]0x0301), 'E', ([string][char]0x00DF), 'ss', 'SS', ([string][char]0x0130), 'i', 'I', 'z10', 'z2', 'Alpha', 'alpha')
        $seconds = @(30.0, 30.0, 0.5, 45.5, 120.0, 600.0, 30.0)
        $items = @(for ($i = 0; $i -lt 60; $i++) {
            $o = [ordered]@{ Key = $keys[($i * 7) % $keys.Count] }
            if ($i % 11 -ne 3) { $o.Seconds = $seconds[($i * 5) % $seconds.Count] }
            $o.Idx = $i
            [pscustomobject]$o
        })
        $prefix = @(@('Alpha', 'AlphaBeta', 'alpha', 'Beta', 'Beta', ''), @('Run', 'Run2', 'Runner', 'Ru'), @(([string][char]0x00E9), ([string][char]0x00E9 + 'x'), 'e'), @())
        Assert-NCGolden 'planner-parts' {
            foreach ($cap in @(1000, 3)) {
                foreach ($bin in @(Split-NightlyItemsBySeconds -Items $items -TargetSeconds $(if ($cap -eq 3) { 120 } else { 1e9 }) -MaxOperands $cap)) {
                    [ordered]@{ cap = $cap; idx = (@($bin.Items | ForEach-Object { $_.Idx }) -join ','); seconds = $bin.Seconds; closed = $bin.Closed }
                }
            }
            foreach ($names in $prefix) {
                $unsafe = Get-NightlyPrefixUnsafeNames -Names $names
                [string[]]$sorted = @($unsafe); [Array]::Sort($sorted, [StringComparer]::Ordinal)
                [ordered]@{ type = $unsafe.GetType().Name; unsafe = $sorted }
            }
        } @('"cap":3', '"unsafe":["","Alpha"]', '"unsafe":["Ru","Run"]')
    } finally { [Globalization.CultureInfo]::CurrentCulture = $saved }
}

# PlannedManifestCheck.cs. The planned-manifest partition (duplicates differing only by case, missing in
# RequiredUids order, one non-ASCII extra, an InvariantCultureIgnoreCase-only match) against the bytes the
# PowerShell copy wrote; and the compiled census digest order (OrdinalIgnoreCase) over culture-equal and
# non-ASCII UIDs against the census fallback's [Array]::Sort order.
function Test-NCPlannedManifestMatchesFallback {
    $sh = [string][char]0xAD; $tr = [string][char]0x130; $ss = [string][char]0xDF; $uu = [string][char]0xDC; $ee = [string][char]0xE9
    $node = { param($uid, $cls) [pscustomobject]@{ Uid = $uid; ClassName = $cls; Type = ($cls -split '\.')[-1]; Namespace = 'Ns'; Method = 'M'; Categories = @() } }
    $group = { param($id, $classes, $uids) $g = [ordered]@{ id = $id; classes = $classes; seconds = 3; groupFilter = '/*/' + $id }
        if ($null -ne $uids) { $g.nodes = @($uids | ForEach-Object { [pscustomobject]@{ Uid = $_ } }) }; [pscustomobject]$g }
    $nodes = @((& $node 'Ns.C.A' 'Ns.C'), (& $node 'Ns.C.b' 'Ns.C'), (& $node ('Ns.C.' + $uu) 'Ns.C'), (& $node 'Ns.C.a' 'Ns.C'),
        (& $node 'Ns.D.x' 'Ns.D'), (& $node ('Ns.D.' + $tr) 'Ns.D'), (& $node 'Ns.D.a' 'Ns.D'), (& $node 'Ns.Ex.q' 'Ns.Ex'))
    $rules = @([pscustomobject]@{ Class = 'Ns.Ex'; Methods = @(); Reason = 'r'; Owner = 'o' })
    $scenarios = [ordered]@{
        exact = @((& $group 'g1' @('Ns.C') $null), (& $group 'g2' @('Ns.D') $null))
        dupCase = @((& $group 'g1' @('Ns.C') $null), (& $group 'g2' @('Ns.D') @('Ns.D.x', ('Ns.D.' + $tr), 'Ns.D.a', 'ns.c.b')))
        missing = @((& $group 'g1' @('Ns.C') @('Ns.C.A', ('Ns.C.' + $uu))), (& $group 'g2' @('Ns.D') $null))
        extra = @((& $group 'g1' @('Ns.C') $null), (& $group 'g2' @('Ns.D') @('Ns.D.x', ('Ns.D.' + $tr), 'Ns.D.a', ('Ns.Zz.' + $ee))))
        invariantOnly = @((& $group 'g1' @('Ns.C') @('Ns.C.A', 'Ns.C.b', ('Ns.C.' + $uu), ('Ns.D.a' + $sh))), (& $group 'g2' @('Ns.D') @('Ns.D.x', ('Ns.D.' + $tr))))
    }
    $saved = $script:QualificationTimingRoot
    try {
        Assert-NCGolden 'plannedManifest' {
            $texts = [ordered]@{}
            foreach ($name in $scenarios.Keys) {
                $dir = Join-Path $root ('planned-' + $name)
                $script:QualificationTimingRoot = Join-Path $dir 'timing'
                [void](Write-TestPhasePlannedManifest -LogRoot $dir -RunId 'r' -Attempt '1' -Suite 'antiphon' -SourceDigest 's' -AssemblyDigest 'a' -PolicyDigest 'p' -CostFileDigest 'c' -AssemblyId 'x' -Groups $scenarios[$name] -DiscoveryNodes $nodes -ProfileExclusions $rules)
                $texts[$name] = [IO.File]::ReadAllText((Join-Path $script:QualificationTimingRoot 'groups-planned-antiphon.json'), [Text.Encoding]::UTF8)
            }
            $texts
        } @('requiredPartitionExact\":true', 'missingIds\":[\"Ns.C.b\"]', 'extraIds\":[\"Ns.Zz.')
    } finally { $script:QualificationTimingRoot = $saved }
    $alpha = @('a', 'A', 'b', 'Z', '-', '_', '.', $sh, $tr, 'i', 'I', $ss, 'ss', $uu, ('u' + [char]0x308), $ee, 'E', '1', '10')
    $rng = [Random]::new(11)
    $tieNodes = @(for ($i = 0; $i -lt 1500; $i++) {
        $s = ''; for ($k = $rng.Next(1, 5); $k -gt 0; $k--) { $s += $alpha[$rng.Next($alpha.Count)] }
        & $node ('Ns.' + $s) $(if ($i % 4 -eq 0) { 'Ns.Ex' } else { 'Ns.C' })
    })
    $r = Invoke-NCBothWays { Get-NightlyDiscoveryCensus -DiscoveryNodes $tieNodes -ProfileExclusions $rules -ProfileAware } 'censusTies'
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'censusTies' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)
}

$expected = 0
switch -Exact ($Case) {
    'CoreMatchesFallback' { Test-NCCoreMatchesFallback; Test-NCDiagnosticsMatchFallback; Test-NCVerdictsMatchFallback; Test-NCLaunchPlanMatchesFallback; Test-NCProjectWalkMatchesFallback; Test-NCParentProcessMatchesFallback; Test-NCEvidenceFilesMatchFallback; Test-NCDocumentsMatchFallback; Test-NCSourceGapsMatchFallback; Test-NCTrxMatchesFallback; Test-NCJsonFilesMatchFallback; Test-NCPlannerPartsGolden; Test-NCPlannedManifestMatchesFallback; $expected = 34 }
    default { throw ('unknown case: ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $root -ExpectedRows $expected
