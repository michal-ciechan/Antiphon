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
        @('c1375', ($header + (& $decode $c1375[0].Record) + "`n" + (& $decode $c1375[1].Record))))
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
        $perLog = foreach ($path in $paths) {
            [ordered]@{
                execution = Get-NCOutcome { ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution }
                discovery = Get-NCOutcome { ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery }
            }
        }
        [ordered]@{ records = @($perRecord); logs = @($perLog) }
    }
    Assert-NC ($r.Compiled -ceq $r.Fallback) 'diagnostics' ('compiled ' + $r.Compiled.Length + ' chars, fallback ' + $r.Fallback.Length)

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
    } }
    $rowCore = [Antiphon.Nightly.TestPhaseSummary]::Row($row, [string[]]@($row.Keys))
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $r.Fallback.Contains($rowCore.Replace('"', '\"'))) 'summaryHelpers' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
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
    $r = Invoke-NCBothWays {
        $entry.Views.Clear()
        $view = Get-NightlyDiscoveryIndexView -Entry $entry -ProfileExclusions @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile rc -SuiteId antiphon -Platform windows) -ProfileAware
        [ordered]@{
            verdict = ConvertTo-NightlyCoverageVerdict -TerminalRows $trx.ToArray() -ProcessExit 0 -Sha 'a' -ExpectedSha 'A' -GitRef 'r' -ExpectedRef 'r' -PolicyHash 'p' -ExpectedPolicyHash 'p'
            verdictNull = ConvertTo-NightlyCoverageVerdict -TerminalRows $null -ProcessExit 1 -Sha 'a' -ExpectedSha 'b' -GitRef '' -ExpectedRef '' -PolicyHash '' -ExpectedPolicyHash ''
            verdictEmpty = ConvertTo-NightlyCoverageVerdict -TerminalRows @() -ProcessExit 0 -Sha '' -ExpectedSha '' -GitRef '' -ExpectedRef '' -PolicyHash '' -ExpectedPolicyHash ''
            perClassSet = @(foreach ($set in $classSets) { [ordered]@{
                cross = Test-NightlyTrxDiagnosticCrossCheck -TrxRows $trx.ToArray() -TerminalNodes $terminal.ToArray() -RequiredClasses $set
                present = Test-NightlyRequiredClassesPresent -TerminalRows $trx.ToArray() -RequiredClasses $set
                required = @(Get-NightlyDiscoveryIndexRequiredUids -View $view -RequiredClasses $set)
            } })
            expanded = Test-NightlyExpandedRowsPresent -DiscoveryNodes $nodes -TerminalRows $terminal.ToArray() -RequiredUids @($nodes | Select-Object -First 300 | ForEach-Object { $_.Uid })
            expandedNull = Test-NightlyExpandedRowsPresent -DiscoveryNodes $nodes -TerminalRows $null -RequiredUids $null
            crossNull = Test-NightlyTrxDiagnosticCrossCheck -TrxRows $null -TerminalNodes $null -RequiredClasses $null
        }
    }
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
    $r = Invoke-NCBothWays { @(foreach ($c in $cases) { [bool](Test-NightlyClassGroupFilter -Classes $c[0] -GroupFilter $c[1]) }) }
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $r.Fallback.Contains('true') -and $r.Fallback.Contains('false')) 'groupFilter' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
}

function Test-NCProjectWalkMatchesFallback {
    $tree = Join-Path $root 'nc-projects'
    $test = '<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>'
    $files = @('a/One.csproj', 'a/b/Two.csproj', 'bin/Skip.csproj', 'BIN/Skip.csproj', 'obj/Skip.csproj', 'x/obj/Skip.csproj',
        'bin-debug/Skip.csproj', 'a/bin-named.csproj', 'Bin-Upper/Kept.csproj', 'objects/Kept.csproj', 'binary/Kept.csproj',
        'hidden/Skip.csproj', 'c/NotTest.csproj', 'c/Other.txt')
    foreach ($relative in $files) {
        $path = Join-Path $tree $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        $text = $test; if ($relative -like 'c/*') { $text = '<Project />' }
        [IO.File]::WriteAllText($path, $text)
    }
    $hidden = Get-Item -LiteralPath (Join-Path $tree 'hidden') -Force
    $hidden.Attributes = $hidden.Attributes -bor [IO.FileAttributes]::Hidden
    $r = Invoke-NCBothWays { @(Get-NightlyIsTestProjectPaths -RepoRoot $tree) }
    Assert-NC ($r.Compiled -ceq $r.Fallback -and $r.Fallback.Contains('Kept') -and -not $r.Fallback.Contains('Skip')) 'projectWalk' ('compiled ' + $r.Compiled + ' fallback ' + $r.Fallback)
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
    }
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
        $json = $doc | ConvertTo-Json -Depth 12
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
    }
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

$expected = 0
switch -Exact ($Case) {
    'CoreMatchesFallback' { Test-NCCoreMatchesFallback; Test-NCDiagnosticsMatchFallback; Test-NCVerdictsMatchFallback; Test-NCLaunchPlanMatchesFallback; Test-NCProjectWalkMatchesFallback; Test-NCEvidenceFilesMatchFallback; Test-NCDocumentsMatchFallback; $expected = 16 }
    default { throw ('unknown case: ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $root -ExpectedRows $expected
