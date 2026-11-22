#requires -Version 5.1
# CARD-0487 B harness: scripts/nightly-run.ps1. ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'release-gate.ps1')
. (Join-Path $lib 'nightly-run-impl.ps1')
. (Join-Path $lib 'c487-harness.ps1')

# Linux runners set TMPDIR and leave TEMP empty. New-C487Root joins TEMP.
if ([string]::IsNullOrWhiteSpace($env:TEMP)) {
    if (-not [string]::IsNullOrWhiteSpace($env:TMPDIR)) { $env:TEMP = $env:TMPDIR }
    else { $env:TEMP = '/tmp' }
}

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$runPs1 = Join-Path $here 'nightly-run.ps1'

function New-RunFx {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $state = Join-Path $root 'state'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $clone = New-C487OwnedClone -Root $root
    $trace = Join-Path $root 'trace.log'
    $seams = Join-Path $root 'seams.ps1'
    Write-C487Seams -Path $seams -TracePath $trace
    # CARD-0616: every fixture owns a private coordination root, so the shared native-run
    # lock the run core takes is this fixture's own file and never the machine-global
    # verification lock a real nightly or RC run holds. Matches New-C599RunFx in
    # test-release-gate.ps1.
    return [pscustomobject]@{
        Root = $root; State = $state; Clone = $clone; Trace = $trace; Seams = $seams
        Coordination = (Join-Path $root 'coord')
    }
}

function Invoke-FxRun {
    param($Fx, [hashtable]$Extra)
    $args = @{
        CheckoutRoot = $Fx.Clone
        StateRoot = $Fx.State
        LogRoot = Join-Path $Fx.State 'logs'
        SeamsPath = $Fx.Seams
        NoReport = $true
        Trigger = 'scheduled'
        Ref = 'master'
        CoordinationRoot = $Fx.Coordination
        PassThru = $true
    }
    if ($Extra) { foreach ($k in $Extra.Keys) { $args[$k] = $Extra[$k] } }
    return Invoke-AntiphonNightlyRun @args
}

# W2-nightly S19: the run core is C# (Antiphon.Nightly/NightlyRun.cs, not wired until S20). C487_G001-G026 and
# C545_ResultLine run in-process in tests/Antiphon.Tests/Scripts/Nightly/NightlyRunTests.cs, C1313_ReusedCloneEolRepair
# and C1854_EolRepairHardening in NightlyScriptsTests, C487_PrerequisiteSettle in NightlyPrerequisiteTests and
# C1698_RunPrintsToolchainWarning in ToolchainFloorWarningTests. The cases left here are the launcher ones S20 moves:
# C2044_RcReleaseRootRefusal and C1698_RunLibraryLoadFailure (the PowerShell run core never loads toolchain-floor.ps1).

# ---- CARD-0545 D-10: the wrapper's last stdout line is the JSON completion record ----

function Invoke-C545ResultLineRun {
    param([string]$Row, [string]$UtcNow, [int]$TestExit = 0, [bool]$TestsPassed = $true, [switch]$NoReport,
        [string]$CheckoutRoot = '', [switch]$Rc, [string]$ReleaseRoot)
    $fx = New-RunFx
    $sha = 'a' * 40
    if ($Rc) {
        # The real selection worker reads policy blobs from the pinned commit.
        $inputs = @('global.json', 'tests/test-execution-policy.json', 'tests/test-selection-policy.json', 'scripts/select-test-rosters.ps1')
        [void][IO.Directory]::CreateDirectory((Join-Path $fx.Clone 'tests'))
        foreach ($file in $inputs) {
            Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $here) $file) -Destination (Join-Path $fx.Clone $file)
        }
        & git -C $fx.Clone init -q
        if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the rc fixture repository.' }
        & git -C $fx.Clone add -- @inputs 'scripts/lib/test-selection.ps1'
        if ($LASTEXITCODE -ne 0) { throw 'Could not stage the rc fixture selection inputs.' }
        & git -C $fx.Clone -c user.name=C2044 -c user.email=c2044@example.invalid -c core.hooksPath=.git/no-hooks -c commit.gpgsign=false commit -qm 'rc fixture selection inputs'
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit the rc fixture selection inputs.' }
        $sha = & git -C $fx.Clone rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the rc fixture candidate.' }
    }
    $runId = ('c545-' + $Row + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $summaryJson = ('{{"coverageComplete":true,"testsPassed":{0},"reportDelivered":true,"policyHash":"p"}}' -f $(if ($TestsPassed) { 'true' } else { 'false' }))
    $extra = [scriptblock]::Create(@"
`$NightlySeams.UtcNow = { return [datetime]::Parse('$UtcNow', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal) }
`$NightlySeams.StartProcess = {
    param(`$FilePath, `$ArgumentList, `$WorkingDirectory, `$TimeoutMilliseconds, `$Environment)
    Add-Content -LiteralPath '$($fx.Trace)' -Value (('EXEC {0} {1}' -f `$FilePath, ((`$ArgumentList) -join ' '))) -Encoding ASCII
    `$a = @(`$ArgumentList)
    # W2-nightly S17: the tests hop is the nightly-tests command (antiphon-scripts nightly-tests --log-root <dir> ...).
    `$logIdx = [array]::IndexOf(`$a, '--log-root')
    if ([string]`$FilePath -eq 'antiphon-scripts' -and [string]`$a[0] -eq 'nightly-tests' -and `$logIdx -ge 0) {
        `$dir = [string]`$a[`$logIdx + 1]
        New-Item -ItemType Directory -Path `$dir -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path `$dir 'summary.json'), '$summaryJson')
        return @{ ExitCode = $TestExit; TimedOut = `$false; Pid = 2 }
    }
    return @{ ExitCode = 0; TimedOut = `$false; Pid = 2 }
}
"@)
    Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -GitOutput $sha -Extra $extra
    $checkout = $fx.Clone
    if ($CheckoutRoot) { $checkout = $CheckoutRoot }
    $argList = @('-NoProfile', '-NonInteractive', '-File', $runPs1, '-NoSync', '-CheckoutRoot', $checkout, '-StateRoot', $fx.State,
        '-LogRoot', (Join-Path $fx.State 'logs'), '-SeamsPath', $fx.Seams, '-RunId', $runId,
        '-CoordinationRoot', $fx.Coordination)
    if ($Rc) {
        $argList += @('-Profile', 'rc', '-Trigger', 'rc', '-Ref', 'rc/2026-11-30', '-ExpectedSha', $sha, '-WhatIf')
    } else {
        $argList += @('-Trigger', 'scheduled', '-Ref', 'master')
    }
    if ($PSBoundParameters.ContainsKey('ReleaseRoot')) { $argList += @('-ReleaseRoot', $ReleaseRoot) }
    if ($NoReport) { $argList += '-NoReport' }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'pwsh'
    $psi.WorkingDirectory = $fx.Root
    foreach ($x in $argList) { [void]$psi.ArgumentList.Add($x) }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $proc = [System.Diagnostics.Process]::Start($psi)
    $errTask = $proc.StandardError.ReadToEndAsync()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    $lines = @($stdout.Replace("`r`n", "`n").TrimEnd("`n").Split("`n"))
    $last = [string]$lines[$lines.Count - 1]
    $record = $null
    try { $record = $last | ConvertFrom-Json } catch { $record = $null }
    $state = $null
    $lastRun = Join-Path $fx.State 'last-run.json'
    if (Test-Path -LiteralPath $lastRun) { $state = Get-Content -LiteralPath $lastRun -Raw | ConvertFrom-Json }
    $greenPath = Join-Path $fx.State 'last-complete-green.json'
    $green = $null
    if (Test-Path -LiteralPath $greenPath) { $green = Get-Content -LiteralPath $greenPath -Raw | ConvertFrom-Json }
    return [pscustomobject]@{ Row = $Row; ExitCode = $proc.ExitCode; Stdout = $stdout; Last = $last; Record = $record; State = $state; Green = $green; RunId = $runId; Stderr = $errTask.Result }
}

function Test-C2044_RcReleaseRootRefusal {
    $arguments = @{ Rc = $true; UtcNow = '2026-11-21T07:35:00Z' }
    $omitted = Invoke-C545ResultLineRun -Row 'omitted' @arguments
    $blank = Invoke-C545ResultLineRun -Row 'blank' -ReleaseRoot '   ' @arguments
    foreach ($run in @($omitted, $blank)) {
        Assert-C487 -Cond ($run.ExitCode -eq 3 -and $run.Record.exitCode -eq 3 -and
            $run.Record.refusal -ceq 'rc-release-root-required' -and $run.Record.nativeRunId -ceq '' -and $null -eq $run.State) `
            -Name ('C2044 {0} ReleaseRoot is refused before recording a run and named in the final JSON' -f $run.Row) `
            -Detail ('exit={0} last={1} stderr={2}' -f $run.ExitCode, $run.Last, $run.Stderr)
    }
    $release = Join-Path $ResultsDirectory 'rc-releases'
    $explicit = Invoke-C545ResultLineRun -Row 'explicit' -ReleaseRoot $release @arguments
    Assert-C487 -Cond ($explicit.ExitCode -eq 0 -and $explicit.Record.exitCode -eq 0 -and
        $explicit.Record.nativeRunId -ceq $explicit.RunId -and $explicit.State.profile -ceq 'rc' -and
        (Test-Path -LiteralPath (Join-Path $release "candidates/$($explicit.State.candidateId)/attempts/$($explicit.RunId)/selection.json"))) `
        -Name 'C2044 explicit ReleaseRoot creates the selection in the supplied root' -Detail $explicit.Last
}

# CARD-1698: the run core prints a toolchain floor warning before the tests hop; the version comes from the
# ToolchainVersions seam, never the real git. C1698_RunLibraryLoadFailure compares a broken-library run with this one.
function Invoke-C1698ToolchainRun {
    param([string]$GitVersion)
    $fx = New-RunFx
    New-Item -ItemType Directory -Path (Join-Path $fx.Clone 'docker') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path (Split-Path $here -Parent) 'docker/git-minimum-version.sh') -Destination (Join-Path $fx.Clone 'docker/git-minimum-version.sh')
    Copy-Item -LiteralPath (Join-Path (Split-Path $here -Parent) 'global.json') -Destination (Join-Path $fx.Clone 'global.json')
    $extra = [scriptblock]::Create("`$NightlySeams.ToolchainVersions = { param(`$tool) if (`$tool -eq 'git') { 'git version $GitVersion' } else { '10.0.204 [/fake/sdk]' } }")
    Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -Extra $extra
    $all = @(Invoke-FxRun -Fx $fx -Extra @{ NoSync = $true } 6>&1)
    $lines = @($all | Where-Object { $_ -is [System.Management.Automation.InformationRecord] } | ForEach-Object { [string]$_.MessageData })
    $result = @($all | Where-Object { $_ -isnot [System.Management.Automation.InformationRecord] }) | Select-Object -Last 1
    return [pscustomobject]@{ Lines = $lines; Result = $result }
}

# CARD-1698, W2-nightly S03: the run core checks the floors with the compiled twin
# (Antiphon.Nightly/ToolchainFloor.cs) and no longer loads toolchain-floor.ps1, which stays for
# the restart chain. A child harness runs from a copied scripts tree whose library is broken (a
# throw, or a file PowerShell cannot parse); the run prints no check-failed line, keeps the
# at-floor exit code and phase and still hops to the tests.
function Test-C1698_RunLibraryLoadFailure {
    $at = Invoke-C1698ToolchainRun -GitVersion '2.43.0'
    $repoRoot = Split-Path $here -Parent
    $broken = @(
        @{ Name = 'throws'; Text = "throw 'C1698 toolchain lib load failure'" },
        @{ Name = 'unparsable'; Text = "function Get-C1698Broken {`n    `$x = 1`n" }
    )
    foreach ($b in $broken) {
        $copy = Join-Path $ResultsDirectory ('broken-lib-' + [guid]::NewGuid().ToString('N'))
        foreach ($d in @('scripts/lib', 'docker')) { New-Item -ItemType Directory -Path (Join-Path $copy $d) -Force | Out-Null }
        # The harness, the entry scripts the fixture clone copies, and the whole library directory.
        # [IO.File]::Copy: per-file Copy-Item over the library costs tens of seconds on Linux.
        foreach ($n in @('test-nightly-run.ps1', 'nightly-run.ps1')) {
            [IO.File]::Copy((Join-Path $here $n), (Join-Path $copy (Join-Path 'scripts' $n)))
        }
        foreach ($f in [IO.Directory]::GetFiles($lib)) { [IO.File]::Copy($f, (Join-Path (Join-Path $copy 'scripts') (Join-Path 'lib' ([IO.Path]::GetFileName($f))))) }
        # nightly-run.ps1 requires the compiled core: copy its sources (not bin/ or obj/). They hash to the
        # checkout's build identity, so the cached build is loaded, not rebuilt.
        foreach ($project in @('scripts/lib/Antiphon.Nightly', 'src/Antiphon.IO.WriteBehind')) {
            $from = Join-Path $repoRoot $project
            foreach ($f in [IO.Directory]::EnumerateFiles($from, '*', [IO.SearchOption]::AllDirectories)) {
                $rel = $f.Substring($from.Length).TrimStart([char]92, [char]47)
                if ($rel -match '^(bin|obj)[\\/]' -or $rel -notmatch '\.(cs|csproj)$') { continue }
                $to = Join-Path (Join-Path $copy $project) $rel
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))
                [IO.File]::Copy($f, $to)
            }
        }
        Copy-Item -LiteralPath (Join-Path $repoRoot 'docker/git-minimum-version.sh') -Destination (Join-Path $copy 'docker/git-minimum-version.sh')
        Copy-Item -LiteralPath (Join-Path $repoRoot 'global.json') -Destination (Join-Path $copy 'global.json')
        Set-Content -LiteralPath (Join-Path $copy 'scripts/lib/toolchain-floor.ps1') -Value $b.Text -Encoding ASCII
        $out = Join-Path $copy 'child-result.json'
        $saved = $env:C1698_LOAD_FAILURE_OUT
        $env:C1698_LOAD_FAILURE_OUT = $out
        try {
            $console = & pwsh -NoProfile -NonInteractive -File (Join-Path $copy 'scripts/test-nightly-run.ps1') -Case C1698_RunLibraryLoadFailureChild -ResultsDirectory (Join-Path $copy 'results') 2>&1 | Out-String
        } finally { $env:C1698_LOAD_FAILURE_OUT = $saved }
        $child = $null
        if (Test-Path -LiteralPath $out) { $child = [IO.File]::ReadAllText($out) | ConvertFrom-Json }
        $lines = @()
        if ($child) { $lines = @($child.lines | ForEach-Object { [string]$_ }) }
        $w = -1; $hop = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($w -lt 0 -and $lines[$i] -match 'WARNING: the toolchain floor check failed') { $w = $i }
            if ($hop -lt 0 -and $lines[$i] -match 'running antiphon-scripts nightly-tests') { $hop = $i }
        }
        $ok = $null -ne $child -and $null -ne $at.Result -and [int]$child.exitCode -eq [int]$at.Result.ExitCode -and
            [string]$child.phase -eq [string]$at.Result.Phase -and $w -lt 0 -and $hop -ge 0
        $detail = 'warning=' + $w + ' hop=' + $hop
        if ($child) { $detail += ' child=' + $child.exitCode + '/' + $child.phase }
        else { $detail += ' no child result: ' + $console }
        $detail += ' at=' + $at.Result.ExitCode + '/' + $at.Result.Phase
        Assert-C487 -Cond $ok -Name ('C1698 Run does not load the toolchain library: ' + $b.Name) -Detail $detail
    }
}

# Child half of C1698_RunLibraryLoadFailure; runs only from the copied tree, writes its result.
function Test-C1698_RunLibraryLoadFailureChild {
    if (-not $env:C1698_LOAD_FAILURE_OUT) { Assert-C487 -Cond $false -Name 'C1698 Run load-failure child needs C1698_LOAD_FAILURE_OUT'; return }
    $r = $null
    try { $r = Invoke-C1698ToolchainRun -GitVersion '2.43.0' } catch { $r = [pscustomobject]@{ Lines = @('THREW ' + $_.Exception.Message); Result = $null } }
    $exit = $null; $phase = $null
    if ($r.Result) { $exit = $r.Result.ExitCode; $phase = $r.Result.Phase }
    $body = [ordered]@{ exitCode = $exit; phase = $phase; lines = @($r.Lines) } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($env:C1698_LOAD_FAILURE_OUT, $body)
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    Test-C2044_RcReleaseRootRefusal
    Test-C1698_RunLibraryLoadFailure
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'run-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 5 })
