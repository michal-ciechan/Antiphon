#requires -Version 5.1
# CARD-0585 S6 harness for scripts/run-checkpoint.ps1. Offline: a shim .ps1 stands in for dotnet
# and copies a fixture TRX into the run's --results-directory, so no build and no test ever runs.
# ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'c487-harness.ps1')

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$script:RepoRoot = Split-Path -Parent $here
$script:Runner = Join-Path $here 'run-checkpoint.ps1'
$script:Fixtures = Join-Path $here 'fixtures'
$script:GreenTrx = Join-Path $script:Fixtures 'c585-green.trx'
$script:FailuresTrx = Join-Path $script:Fixtures 'c585-failures.trx'
$script:ZeroTrx = Join-Path $script:Fixtures 'c585-zero.trx'

$script:ShimBody = @'
param()
$argv = @($args)
$log = $env:C585_SHIM_LOG
if ($log) { Add-Content -LiteralPath $log -Value (($argv) -join ' ') -Encoding ASCII }
$verb = ''
if ($argv.Count -gt 0) { $verb = [string]$argv[0] }
if ($verb -eq 'build') {
    $code = 0
    if ($env:C585_BUILD_EXIT) { $code = [int]$env:C585_BUILD_EXIT }
    exit $code
}
$resultsDir = ''
for ($i = 0; $i -lt $argv.Count; $i++) {
    if ([string]$argv[$i] -eq '--results-directory' -and ($i + 1) -lt $argv.Count) {
        $resultsDir = [string]$argv[$i + 1]
    }
}
if ($env:C585_TRX -and $resultsDir) {
    if (-not (Test-Path -LiteralPath $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null }
    Copy-Item -LiteralPath $env:C585_TRX -Destination (Join-Path $resultsDir 'run.trx') -Force
}
$runCode = 0
if ($env:C585_RUN_EXIT) { $runCode = [int]$env:C585_RUN_EXIT }
exit $runCode
'@

# CARD-0589: the offline stand-in for the runner's /build-slots (scripts/fixtures/c589-slot-shim.ps1),
# shared with scripts/test-build-slot.ps1.
$script:SlotShim = Join-Path $script:Fixtures 'c589-slot-shim.ps1'

function New-C585Case {
    param([string]$Name)
    $root = Join-Path $ResultsDirectory ($Name + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $shim = Join-Path $root 'dotnet-shim.ps1'
    Set-Content -LiteralPath $shim -Value $script:ShimBody -Encoding ASCII
    return [pscustomobject]@{
        Root = $root
        Shim = $shim
        SlotShim = $script:SlotShim
        Log = (Join-Path $root 'calls.log')
        ResultsRoot = (Join-Path $root 'results')
    }
}

function Invoke-C585Runner {
    param(
        $Fx,
        [string]$Name = 'CP-1',
        [string]$OutputPath = 'bin-c585h/',
        [string]$Trx = '',
        [int]$BuildExit = 0,
        [int]$RunExit = 0,
        [switch]$NoBuild,
        [int]$MinExecuted = 1,
        [string[]]$Expect,
        [string]$Stamp = '',
        [string]$ResultsRoot = '',
        [string[]]$MsBuildProperty,
        [string]$Platform = '',
        [switch]$Slot,
        [string]$SlotScript = 'granted',
        [string]$SlotWaitSeconds = '',
        [string]$SlotGraceSeconds = '',
        [string]$SlotRetryMs = '10'
    )
    if ([string]::IsNullOrWhiteSpace($ResultsRoot)) { $ResultsRoot = $Fx.ResultsRoot }
    $env:C585_SHIM_LOG = $Fx.Log
    $env:C585_TRX = $Trx
    $env:C585_BUILD_EXIT = [string]$BuildExit
    $env:C585_RUN_EXIT = [string]$RunExit
    $env:C585_STAMP = $Stamp
    $env:C671_PLATFORM = $Platform
    # CARD-0589: every run has a slot shim and a dead endpoint, so no case can reach a real runner.
    # C585 cases pass -NoSlot (their dotnet arguments stay exactly the pre-CARD-0589 ones);
    # C589 cases pass -Slot and script the broker's answers.
    $env:ANTIPHON_BUILD_SLOTS_URL = 'http://127.0.0.1:1/build-slots'
    $env:C589_SLOT_SHIM = $Fx.SlotShim
    $env:C589_SLOT_LOG = $Fx.Log
    $env:C589_SLOT_SCRIPT = $SlotScript
    $env:C589_SLOT_WAIT_SECONDS = $SlotWaitSeconds
    $env:C589_SLOT_GRACE_SECONDS = $SlotGraceSeconds
    $env:C589_SLOT_RETRY_MS = $SlotRetryMs
    $callArgs = @(
        '-NoProfile', '-NonInteractive', '-File', $script:Runner,
        '-Name', $Name,
        '-Project', 'tests/Antiphon.Tests',
        '-OutputPath', $OutputPath,
        '-Filter', '/*/*/C585SampleTests/*',
        '-ResultsRoot', $ResultsRoot,
        '-MinExecuted', [string]$MinExecuted,
        '-DotnetShim', $Fx.Shim
    )
    if ($NoBuild) { $callArgs += '-NoBuild' }
    if (-not $Slot) { $callArgs += '-NoSlot' }
    foreach ($property in @($MsBuildProperty)) {
        if ($property) { $callArgs += @('-MsBuildProperty', $property) }
    }
    foreach ($token in @($Expect)) {
        if ($token) { $callArgs += @('-Expect', $token) }
    }
    Push-Location -LiteralPath $script:RepoRoot
    try {
        # Same literal-argv reason as Invoke-Dotnet in run-checkpoint.ps1: the -Filter
        # value contains '*' and the call operator spends tens of seconds globbing it.
        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = 'pwsh'
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.WorkingDirectory = $script:RepoRoot
        foreach ($token in @($callArgs)) { [void]$psi.ArgumentList.Add([string]$token) }
        $proc = [System.Diagnostics.Process]::Start($psi)
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()
        $proc.WaitForExit()
        $global:LASTEXITCODE = $proc.ExitCode
        $code = $LASTEXITCODE
        $merged = [string]$stdoutTask.Result
        $errText = [string]$stderrTask.Result
        if (-not [string]::IsNullOrEmpty($errText)) {
            if (-not [string]::IsNullOrEmpty($merged) -and -not $merged.EndsWith("`n")) { $merged += "`n" }
            $merged += $errText
        }
        $output = @()
        if (-not [string]::IsNullOrEmpty($merged)) {
            $norm = $merged -replace "`r`n", "`n" -replace "`r", "`n"
            if ($norm.EndsWith("`n")) { $norm = $norm.Substring(0, $norm.Length - 1) }
            $output = @($norm -split "`n")
        }
    } finally {
        Pop-Location
        $env:C585_SHIM_LOG = $null
        $env:C585_TRX = $null
        $env:C585_BUILD_EXIT = $null
        $env:C585_RUN_EXIT = $null
        $env:C585_STAMP = $null
        $env:C671_PLATFORM = $null
        foreach ($name in @('ANTIPHON_BUILD_SLOTS_URL', 'C589_SLOT_SHIM', 'C589_SLOT_LOG', 'C589_SLOT_SCRIPT', 'C589_SLOT_WAIT_SECONDS', 'C589_SLOT_GRACE_SECONDS', 'C589_SLOT_RETRY_MS')) {
            Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue
        }
    }
    $lines = @($output | ForEach-Object { [string]$_ })
    $calls = @()
    if (Test-Path -LiteralPath $Fx.Log) { $calls = @(Get-Content -LiteralPath $Fx.Log) }
    return [pscustomobject]@{
        Exit = $code
        Lines = $lines
        Text = ($lines -join "`n")
        Calls = $calls
        Checkpoint = @($lines | Where-Object { $_ -match '^CHECKPOINT CP-\d+ commit=' })
    }
}

function Get-C585BuildCalls { param($Result) return @($Result.Calls | Where-Object { $_ -match '^build ' }) }
function Get-C585RunCalls { param($Result) return @($Result.Calls | Where-Object { $_ -match '^run ' }) }

function Test-C585_Green {
    $fx = New-C585Case -Name 'green'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx
    Assert-C487 -Cond ($r.Exit -eq 0) -Name 'C585 Green exit code 0' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    $line = [string]($r.Checkpoint | Select-Object -First 1)
    Assert-C487 -Cond ($line -match 'build=ok ' -and $line -match 'executed=3 passed=3 failed=0 skipped=0') `
        -Name 'C585 Green line reports build=ok and executed=3 passed=3 failed=0 skipped=0' -Detail $line
    Assert-C487 -Cond ($line -match 'trx=.+run\.trx slot=skipped waited=0s dirty=') -Name 'C585 Green line names the fresh TRX it parsed' -Detail $line
    $executed = @($r.Lines | Where-Object { $_ -match '^EXECUTED ' })
    Assert-C487 -Cond ($executed.Count -eq 3 -and ($executed -join ' ') -match 'C585SampleTests\.alpha_is_green') `
        -Name 'C585 Green prints the executed Class.Method roster' -Detail ($executed -join ' | ')
    Assert-C487 -Cond ($r.Text -match 'CHECKPOINT CP-1 EXIT CODE: 0') -Name 'C585 Green trailer names exit code 0' -Detail $r.Text
}

function Test-C585_Failures {
    $fx = New-C585Case -Name 'failures'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:FailuresTrx -RunExit 1
    Assert-C487 -Cond ($r.Exit -eq 1) -Name 'C585 Failures exit code 1' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    $line = [string]($r.Checkpoint | Select-Object -First 1)
    Assert-C487 -Cond ($line -match 'executed=3 passed=2 failed=1 skipped=0') -Name 'C585 Failures line reports failed=1' -Detail $line
    $failed = @($r.Lines | Where-Object { $_ -match '^FAILED ' })
    Assert-C487 -Cond ($failed.Count -eq 1 -and $failed[0] -eq 'FAILED Antiphon.Tests.Scripts.C585SampleTests.delta_is_red') `
        -Name 'C585 Failures names the class-qualified method, not the display name' -Detail ($failed -join ' | ')
}

function Test-C585_ZeroExecuted {
    $fx = New-C585Case -Name 'zero'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:ZeroTrx -RunExit 8
    Assert-C487 -Cond ($r.Exit -eq 3) -Name 'C585 ZeroExecuted exit code 3' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    $line = [string]($r.Checkpoint | Select-Object -First 1)
    Assert-C487 -Cond ($line -match 'executed=0 passed=0 failed=0 skipped=0') -Name 'C585 ZeroExecuted line reports executed=0' -Detail $line
    $executed = @($r.Lines | Where-Object { $_ -match '^EXECUTED ' })
    Assert-C487 -Cond ($executed.Count -eq 0) -Name 'C585 ZeroExecuted prints no roster lines' -Detail ($executed -join ' | ')
}

function Test-C585_RosterMiss {
    $fx = New-C585Case -Name 'rostermiss'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Expect @('NotInRoster')
    Assert-C487 -Cond ($r.Exit -eq 3) -Name 'C585 RosterMiss exit code 3' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ($r.Text -match 'ROSTER MISS NotInRoster') -Name 'C585 RosterMiss names the token that matched nothing' -Detail $r.Text
    $control = New-C585Case -Name 'rostermiss-control'
    $c = Invoke-C585Runner -Fx $control -Trx $script:GreenTrx -Expect @('C585SampleTests')
    Assert-C487 -Cond ($c.Exit -eq 0 -and $c.Text -notmatch 'ROSTER MISS') -Name 'C585 RosterMiss control a matching -Expect token is green' -Detail ('exit={0} {1}' -f $c.Exit, $c.Text)
    # pwsh -File binds every argument as a string, so several tokens arrive comma-joined.
    $comma = New-C585Case -Name 'rostermiss-comma'
    $m = Invoke-C585Runner -Fx $comma -Trx $script:GreenTrx -Expect @('C585SampleTests,C585OtherTests')
    Assert-C487 -Cond ($m.Exit -eq 0 -and $m.Text -notmatch 'ROSTER MISS') -Name 'C585 RosterMiss a comma-separated -Expect value splits into tokens' -Detail ('exit={0} {1}' -f $m.Exit, $m.Text)
    $commaMiss = New-C585Case -Name 'rostermiss-comma-miss'
    $mm = Invoke-C585Runner -Fx $commaMiss -Trx $script:GreenTrx -Expect @('C585SampleTests,NotInRoster')
    Assert-C487 -Cond ($mm.Exit -eq 3 -and $mm.Text -match 'ROSTER MISS NotInRoster') -Name 'C585 RosterMiss one bad token in a comma-separated value is still red' -Detail ('exit={0} {1}' -f $mm.Exit, $mm.Text)
}

function Test-C585_QuotedExpect {
    # CARD-0615: a roster written -Expect 'A','B' reaches this child as one string that still holds
    # its literal quote characters, because `pwsh -File` binds every argument as a string. Those
    # edge quotes are wrapper syntax and must not be matched against executed names; an interior
    # quote must still count, and a genuinely absent token must still refuse the checkpoint.
    $dq = [string][char]34
    $counters = 'executed=3 passed=3 failed=0 skipped=0'

    function Get-C585Misses { param($Result) return @($Result.Lines | Where-Object { $_ -match '^ROSTER MISS ' }) }

    $single = New-C585Case -Name 'quoted-single'
    $r1 = Invoke-C585Runner -Fx $single -Trx $script:GreenTrx -MinExecuted 3 -Expect @("'C585SampleTests','C585OtherTests'")
    Assert-C487 -Cond ($r1.Exit -eq 0 -and (Get-C585Misses -Result $r1).Count -eq 0 -and ([string]($r1.Checkpoint | Select-Object -First 1)) -match $counters) `
        -Name 'C585 QuotedExpect single quotes match' -Detail ('exit={0} {1}' -f $r1.Exit, $r1.Text)

    $double = New-C585Case -Name 'quoted-double'
    $doubleExpect = ($dq + 'C585SampleTests' + $dq + ',' + $dq + 'C585OtherTests' + $dq)
    $r2 = Invoke-C585Runner -Fx $double -Trx $script:GreenTrx -MinExecuted 3 -Expect @($doubleExpect)
    Assert-C487 -Cond ($r2.Exit -eq 0 -and (Get-C585Misses -Result $r2).Count -eq 0 -and ([string]($r2.Checkpoint | Select-Object -First 1)) -match $counters) `
        -Name 'C585 QuotedExpect double quotes match' -Detail ('expect={0} exit={1} {2}' -f $doubleExpect, $r2.Exit, $r2.Text)

    $mixed = New-C585Case -Name 'quoted-mixed'
    $mixedExpect = ("  ' C585SampleTests ' , " + $dq + ' C585OtherTests ' + $dq + '  ')
    $r3 = Invoke-C585Runner -Fx $mixed -Trx $script:GreenTrx -MinExecuted 3 -Expect @($mixedExpect)
    Assert-C487 -Cond ($r3.Exit -eq 0 -and (Get-C585Misses -Result $r3).Count -eq 0 -and ([string]($r3.Checkpoint | Select-Object -First 1)) -match $counters) `
        -Name 'C585 QuotedExpect mixed quotes and whitespace match' -Detail ('expect={0} exit={1} {2}' -f $mixedExpect, $r3.Exit, $r3.Text)

    $miss = New-C585Case -Name 'quoted-miss'
    $missExpect = ("'C585SampleTests'," + $dq + 'NotInRoster' + $dq)
    $r4 = Invoke-C585Runner -Fx $miss -Trx $script:GreenTrx -MinExecuted 3 -Expect @($missExpect)
    # Re-wrap: a one-element return unrolls to a bare string, whose [0] would be its first character.
    $r4Misses = @(Get-C585Misses -Result $r4)
    Assert-C487 -Cond ($r4.Exit -eq 3 -and $r4Misses.Count -eq 1 -and $r4Misses[0] -eq 'ROSTER MISS NotInRoster' -and ([string]($r4.Checkpoint | Select-Object -First 1)) -match $counters) `
        -Name 'C585 QuotedExpect missing token stays red' -Detail ('expect={0} exit={1} misses={2} {3}' -f $missExpect, $r4.Exit, ($r4Misses -join ' | '), $r4.Text)

    $interior = New-C585Case -Name 'quoted-interior'
    $r5 = Invoke-C585Runner -Fx $interior -Trx $script:GreenTrx -MinExecuted 3 -Expect @("'C585Sam'pleTests','C585OtherTests'")
    $r5Misses = @(Get-C585Misses -Result $r5)
    Assert-C487 -Cond ($r5.Exit -eq 3 -and $r5Misses.Count -eq 1 -and $r5Misses[0] -eq ("ROSTER MISS C585Sam" + [char]39 + "pleTests") -and ([string]($r5.Checkpoint | Select-Object -First 1)) -match $counters) `
        -Name 'C585 QuotedExpect interior quote stays significant' -Detail ('exit={0} misses={1} {2}' -f $r5.Exit, ($r5Misses -join ' | '), $r5.Text)

    $empty = New-C585Case -Name 'quoted-empty'
    $emptyExpect = ("'','C585SampleTests'," + $dq + $dq + ",'C585OtherTests'")
    $r6 = Invoke-C585Runner -Fx $empty -Trx $script:GreenTrx -MinExecuted 3 -Expect @($emptyExpect)
    Assert-C487 -Cond ($r6.Exit -eq 0 -and (Get-C585Misses -Result $r6).Count -eq 0 -and ([string]($r6.Checkpoint | Select-Object -First 1)) -match $counters) `
        -Name 'C585 QuotedExpect empty quoted tokens retain compatibility' -Detail ('expect={0} exit={1} {2}' -f $emptyExpect, $r6.Exit, $r6.Text)
}

function Test-C585_BadOutputPath {
    $variants = @('bin-x\', 'bin-x/ ', 'x/')
    foreach ($variant in $variants) {
        $fx = New-C585Case -Name 'badoutput'
        $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -OutputPath $variant
        Assert-C487 -Cond ($r.Exit -eq 2) -Name ("C585 BadOutputPath '{0}' exit code 2" -f $variant) -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
        Assert-C487 -Cond ($r.Text -match 'forward slash') -Name ("C585 BadOutputPath '{0}' message names the forward slash rule" -f $variant) -Detail $r.Text
        Assert-C487 -Cond ($r.Calls.Count -eq 0) -Name ("C585 BadOutputPath '{0}' invokes no dotnet" -f $variant) -Detail ($r.Calls -join ' | ')
    }
    $control = New-C585Case -Name 'badoutput-control'
    $c = Invoke-C585Runner -Fx $control -Trx $script:GreenTrx -OutputPath 'bin-c585h/'
    Assert-C487 -Cond ($c.Exit -eq 0) -Name 'C585 BadOutputPath control bin-c585h/ is accepted' -Detail ('exit={0} {1}' -f $c.Exit, $c.Text)
}

function Test-C585_BuildFailed {
    $fx = New-C585Case -Name 'buildfailed'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -BuildExit 1
    Assert-C487 -Cond ($r.Exit -eq 2) -Name 'C585 BuildFailed exit code 2' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ($r.Text -match 'CHECKPOINT CP-1 build=failed') -Name 'C585 BuildFailed line reports build=failed' -Detail $r.Text
    Assert-C487 -Cond ((Get-C585BuildCalls -Result $r).Count -eq 1 -and (Get-C585RunCalls -Result $r).Count -eq 0) `
        -Name 'C585 BuildFailed never runs the tests' -Detail ($r.Calls -join ' | ')
}

function Test-C585_FreshResultsDir {
    $fx = New-C585Case -Name 'freshdir'
    $stamp = '20260920-101010-abcd'
    $occupied = Join-Path $fx.ResultsRoot ('CP-1-' + $stamp)
    New-Item -ItemType Directory -Path $occupied -Force | Out-Null
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Stamp $stamp
    Assert-C487 -Cond ($r.Exit -eq 2) -Name 'C585 FreshResultsDir exit code 2' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ($r.Text -match 'results directory exists') -Name 'C585 FreshResultsDir refuses a pre-existing results directory' -Detail $r.Text
    Assert-C487 -Cond ($r.Calls.Count -eq 0) -Name 'C585 FreshResultsDir invokes no dotnet' -Detail ($r.Calls -join ' | ')
    $second = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Stamp '20260920-101011-abce'
    Assert-C487 -Cond ($second.Exit -eq 0) -Name 'C585 FreshResultsDir a different stamp succeeds' -Detail ('exit={0} {1}' -f $second.Exit, $second.Text)
}

function Test-C585_NoBuild {
    $fx = New-C585Case -Name 'nobuild'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -NoBuild
    Assert-C487 -Cond ((Get-C585BuildCalls -Result $r).Count -eq 0) -Name 'C585 NoBuild never builds' -Detail ($r.Calls -join ' | ')
    $line = [string]($r.Checkpoint | Select-Object -First 1)
    Assert-C487 -Cond ($line -match 'build=reused ') -Name 'C585 NoBuild line reports build=reused' -Detail $line
    Assert-C487 -Cond ($r.Exit -eq 0) -Name 'C585 NoBuild exit code 0' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
}

function Test-C585_LineFormat {
    $fx = New-C585Case -Name 'lineformat'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx
    # CARD-0589: the BUILD SLOT lines (here 'skipped by -NoSlot') come first, before any dotnet call;
    # the report line is the first line after them and now ends with the slot outcome.
    $report = @($r.Lines | Where-Object { $_ -match '^CHECKPOINT CP-1 commit=' })
    $first = ''
    if ($report.Count -gt 0) { $first = [string]$report[0] }
    $last = ''
    if ($r.Lines.Count -gt 0) { $last = [string]$r.Lines[$r.Lines.Count - 1] }
    Assert-C487 -Cond ($first -match '^CHECKPOINT CP-1 commit=[0-9a-f]{40} build=(ok|reused) filter=.+ executed=\d+ passed=\d+ failed=\d+ skipped=\d+ trx=.+ slot=(granted|unleased|unlimited|skipped) waited=\d+s dirty=\d+ source=[0-9a-f]{40}(\+dirty:[0-9a-f]{64})? sourceState=(clean|dirty) buildSource=(verified|unknown|mismatch)$' -and [string]$r.Lines[0] -ceq 'BUILD SLOT skipped by -NoSlot') `
        -Name 'C585 LineFormat first line is the pinned CHECKPOINT report line' -Detail ($r.Lines -join ' | ')
    Assert-C487 -Cond ($last -match '^CHECKPOINT CP-1 EXIT CODE: \d$') -Name 'C585 LineFormat last line is the exit-code trailer' -Detail $last
}

function Test-C585_AsciiOnly {
    $runnerBytes = @([IO.File]::ReadAllBytes($script:Runner) | Where-Object { $_ -gt 127 })
    Assert-C487 -Cond ($runnerBytes.Count -eq 0) -Name 'C585 AsciiOnly run-checkpoint.ps1 is ASCII-only' -Detail ([string]$runnerBytes.Count)
    $harness = Join-Path $here 'test-run-checkpoint.ps1'
    $harnessBytes = @([IO.File]::ReadAllBytes($harness) | Where-Object { $_ -gt 127 })
    Assert-C487 -Cond ($harnessBytes.Count -eq 0) -Name 'C585 AsciiOnly the harness is ASCII-only' -Detail ([string]$harnessBytes.Count)
}

# CARD-0671: -MsBuildProperty reaches the build AND the `dotnet run`, so a UseAppHost=false build
# is run through `dotnet exec <dll>` rather than a missing apphost. C671_PLATFORM pins the
# platform so both branches run on any host. The shim is itself run by `pwsh -File`, whose binder
# splits a -name:value token, so its log shows each --property:X=Y as '--property X=Y'.
$script:C671Build = 'build tests/Antiphon.Tests --property OutputPath=bin-c585h/'
$script:C671Run = 'run --project tests/Antiphon.Tests --no-build --property OutputPath=bin-c585h/'
$script:C671RunTail = '-- --treenode-filter /*/*/C585SampleTests/* --report-trx --report-trx-filename run.trx --results-directory '

function Get-C671Call {
    param($Calls)
    # Re-wrap: a one-element return unrolls to a bare string.
    $list = @($Calls)
    if ($list.Count -ne 1) { return ('<{0} calls>' -f $list.Count) }
    return [string]$list[0]
}

function Test-C585_MsBuildForwarding {
    $fx = New-C585Case -Name 'msbuild-forward'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'windows' -MsBuildProperty @('UseAppHost=false')
    $build = Get-C671Call (Get-C585BuildCalls -Result $r)
    $run = Get-C671Call (Get-C585RunCalls -Result $r)
    Assert-C487 -Cond ($r.Exit -eq 0) -Name 'C585 MsBuildForwarding exit code 0' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ($build -ceq ($script:C671Build + ' --property UseAppHost=false --nologo')) `
        -Name 'C585 MsBuildForwarding build carries the property' -Detail $build
    Assert-C487 -Cond ($run.StartsWith($script:C671Run + ' --property UseAppHost=false ' + $script:C671RunTail, [StringComparison]::Ordinal)) `
        -Name 'C585 MsBuildForwarding run carries the property before --' -Detail $run
    Assert-C487 -Cond (@($r.Lines | Where-Object { $_ -ceq 'MSBUILD PROPERTY UseAppHost=false' }).Count -eq 1) `
        -Name 'C585 MsBuildForwarding report names the property' -Detail $r.Text

    $nb = New-C585Case -Name 'msbuild-nobuild'
    $n = Invoke-C585Runner -Fx $nb -Trx $script:GreenTrx -Platform 'windows' -NoBuild -MsBuildProperty @('UseAppHost=false')
    $nRun = Get-C671Call (Get-C585RunCalls -Result $n)
    Assert-C487 -Cond ($n.Exit -eq 0 -and (Get-C585BuildCalls -Result $n).Count -eq 0 -and $nRun.StartsWith($script:C671Run + ' --property UseAppHost=false -- ', [StringComparison]::Ordinal)) `
        -Name 'C585 MsBuildForwarding -NoBuild run still carries the property' -Detail ('exit={0} calls={1}' -f $n.Exit, ($n.Calls -join ' | '))

    $cm = New-C585Case -Name 'msbuild-comma'
    $c = Invoke-C585Runner -Fx $cm -Trx $script:GreenTrx -Platform 'windows' -MsBuildProperty @('UseAppHost=false, C671Probe=yes')
    $cBuild = Get-C671Call (Get-C585BuildCalls -Result $c)
    $cRun = Get-C671Call (Get-C585RunCalls -Result $c)
    $pair = ' --property UseAppHost=false --property C671Probe=yes '
    Assert-C487 -Cond ($c.Exit -eq 0 -and $cBuild -ceq ($script:C671Build + $pair + '--nologo') -and $cRun.StartsWith($script:C671Run + $pair + '-- ', [StringComparison]::Ordinal)) `
        -Name 'C585 MsBuildForwarding a comma-separated value forwards each property' -Detail ('exit={0} calls={1}' -f $c.Exit, ($c.Calls -join ' | '))
}

function Test-C585_MsBuildLinuxDefault {
    $fx = New-C585Case -Name 'msbuild-linux'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'linux'
    $build = Get-C671Call (Get-C585BuildCalls -Result $r)
    $run = Get-C671Call (Get-C585RunCalls -Result $r)
    Assert-C487 -Cond ($r.Exit -eq 0) -Name 'C585 MsBuildLinuxDefault exit code 0' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ($build -ceq ($script:C671Build + ' --property UseAppHost=false --nologo') -and $run.StartsWith($script:C671Run + ' --property UseAppHost=false -- ', [StringComparison]::Ordinal)) `
        -Name 'C585 MsBuildLinuxDefault adds UseAppHost=false to build and run' -Detail ($r.Calls -join ' | ')

    $ex = New-C585Case -Name 'msbuild-linux-explicit'
    $e = Invoke-C585Runner -Fx $ex -Trx $script:GreenTrx -Platform 'linux' -MsBuildProperty @('UseAppHost=true')
    $eText = ($e.Calls -join ' | ')
    Assert-C487 -Cond ($e.Exit -eq 0 -and @($e.Calls).Count -eq 2 -and $eText -cnotmatch 'UseAppHost=false' -and @($e.Calls | Where-Object { $_ -cmatch ' --property UseAppHost=true ' }).Count -eq 2) `
        -Name 'C585 MsBuildLinuxDefault an explicit UseAppHost wins' -Detail $eText

    $ot = New-C585Case -Name 'msbuild-linux-other'
    $o = Invoke-C585Runner -Fx $ot -Trx $script:GreenTrx -Platform 'linux' -MsBuildProperty @('C671Probe=yes')
    $oBuild = Get-C671Call (Get-C585BuildCalls -Result $o)
    Assert-C487 -Cond ($o.Exit -eq 0 -and $oBuild -ceq ($script:C671Build + ' --property C671Probe=yes --property UseAppHost=false --nologo')) `
        -Name 'C585 MsBuildLinuxDefault other properties keep the default' -Detail ($o.Calls -join ' | ')
}

function Test-C585_MsBuildWindowsUnchanged {
    $fx = New-C585Case -Name 'msbuild-windows'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'windows'
    $build = Get-C671Call (Get-C585BuildCalls -Result $r)
    $run = Get-C671Call (Get-C585RunCalls -Result $r)
    Assert-C487 -Cond ($build -ceq ($script:C671Build + ' --nologo')) `
        -Name 'C585 MsBuildWindowsUnchanged build arguments are the pre-CARD-0671 ones' -Detail $build
    $prefix = $script:C671Run + ' ' + $script:C671RunTail
    Assert-C487 -Cond ($run.StartsWith($prefix, [StringComparison]::Ordinal) -and $run.Substring([Math]::Min($prefix.Length, $run.Length)) -notmatch ' --') `
        -Name 'C585 MsBuildWindowsUnchanged run arguments are the pre-CARD-0671 ones' -Detail $run
    Assert-C487 -Cond ($r.Exit -eq 0 -and @($r.Lines | Where-Object { $_ -match '^MSBUILD PROPERTY ' }).Count -eq 0) `
        -Name 'C585 MsBuildWindowsUnchanged prints no property lines' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
}

function Test-C585_MsBuildInvalid {
    $cases = @(
        @{ Value = 'NotAProperty'; Row = 'C585 MsBuildInvalid a token without = exits 2 before dotnet'; Message = 'must be Name=Value' },
        @{ Value = 'OutputPath=bin-y/'; Row = 'C585 MsBuildInvalid OutputPath is refused before dotnet'; Message = 'use -OutputPath' },
        @{ Value = 'UseAppHost=false;OutDir=x/'; Row = 'C585 MsBuildInvalid an embedded OutDir is refused before dotnet'; Message = 'use -OutputPath' }
    )
    foreach ($case in $cases) {
        $fx = New-C585Case -Name 'msbuild-invalid'
        $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'linux' -MsBuildProperty @($case.Value)
        Assert-C487 -Cond ($r.Exit -eq 2 -and $r.Text -match [regex]::Escape($case.Message) -and $r.Calls.Count -eq 0) `
            -Name $case.Row -Detail ('exit={0} calls={1} {2}' -f $r.Exit, ($r.Calls -join ' | '), $r.Text)
    }
}

# CARD-0589 V-4: the row takes a host build slot before its build/run and releases it after. The
# slot shim scripts the broker; it logs into the same calls.log as the dotnet shim, so the order
# POST -> build -> run -> DELETE is checked on one timeline. The dotnet shim runs under pwsh -File,
# whose binder splits -maxcpucount:N into '-maxcpucount N' in its log (the real dotnet gets it whole).
$script:C589Label = 'CP-1@tests/Antiphon.Tests'
$script:C589Lease = 'c5890000-0000-4000-8000-000000000001'

function Get-C589Order {
    param($Result)
    return @($Result.Calls | ForEach-Object {
        if ($_ -like 'SLOT POST *') { 'POST' } elseif ($_ -like 'SLOT DELETE *') { 'DELETE' } elseif ($_ -match '^build ') { 'build' } elseif ($_ -match '^run ') { 'run' }
    }) -join ','
}

function Test-C589_SlotGranted {
    $fx = New-C585Case -Name 'slot-granted'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'windows' -Slot -SlotScript 'granted'
    $build = Get-C671Call (Get-C585BuildCalls -Result $r)
    $run = Get-C671Call (Get-C585RunCalls -Result $r)
    $line = [string]($r.Checkpoint | Select-Object -First 1)
    Assert-C487 -Cond ($r.Exit -eq 0) -Name 'C589 SlotGranted exit code 0' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ($build -ceq ($script:C671Build + ' -maxcpucount 3 --nologo')) `
        -Name 'C589 SlotGranted build carries the grant -maxcpucount' -Detail $build
    Assert-C487 -Cond ($run -cnotmatch 'maxcpucount') -Name 'C589 SlotGranted the --no-build run carries no -maxcpucount' -Detail $run
    Assert-C487 -Cond (@($r.Lines | Where-Object { $_ -cmatch ('^BUILD SLOT granted lease=' + $script:C589Lease + ' waited=\d+s maxcpucount=3$') }).Count -eq 1) `
        -Name 'C589 SlotGranted prints the BUILD SLOT granted line' -Detail $r.Text
    Assert-C487 -Cond ((Get-C589Order -Result $r) -ceq 'POST,build,run,DELETE' -and @($r.Calls | Where-Object { $_ -ceq ('SLOT DELETE ' + $script:C589Lease) }).Count -eq 1) `
        -Name 'C589 SlotGranted releases the lease after the run' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond (@($r.Calls | Where-Object { $_ -clike ('SLOT POST label=' + $script:C589Label + ' pid=*') }).Count -eq 1) `
        -Name 'C589 SlotGranted asks under the row label' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond ($line -match ' slot=granted waited=\d+s dirty=' -and @($r.Lines | Where-Object { $_ -cmatch ('^BUILD SLOT released lease=' + $script:C589Lease + ' held=\d+s$') }).Count -eq 1) `
        -Name 'C589 SlotGranted CHECKPOINT line reports slot=granted' -Detail $r.Text
}

function Test-C589_SlotWaitsThenGranted {
    $fx = New-C585Case -Name 'slot-waits'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'windows' -Slot -SlotScript 'busy,memory_floor,granted'
    $line = [string]($r.Checkpoint | Select-Object -First 1)
    Assert-C487 -Cond ($r.Exit -eq 0) -Name 'C589 SlotWaitsThenGranted exit code 0' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond (@($r.Lines | Where-Object { $_ -cmatch ('^BUILD SLOT waiting label=' + [regex]::Escape($script:C589Label) + ' position=1 occupied=2/2 elapsed=\d+m$') }).Count -ge 1) `
        -Name 'C589 SlotWaitsThenGranted prints a BUILD SLOT waiting line while busy' -Detail $r.Text
    Assert-C487 -Cond (@($r.Lines | Where-Object { $_ -cmatch '^BUILD SLOT waiting label=.+ reason=memory_floor available=1000MB floor=6144MB position=1 elapsed=\d+m$' }).Count -eq 1) `
        -Name 'C589 SlotWaitsThenGranted names the memory floor while below it' -Detail $r.Text
    Assert-C487 -Cond ((Get-C589Order -Result $r) -ceq 'POST,POST,POST,build,run,DELETE') `
        -Name 'C589 SlotWaitsThenGranted builds only after the grant' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond (@($r.Lines | Where-Object { $_ -cmatch '^BUILD SLOT granted lease=\S+ waited=\d+s maxcpucount=3$' }).Count -eq 1 -and $line -match ' slot=granted waited=\d+s dirty=') `
        -Name 'C589 SlotWaitsThenGranted reports waited= on the grant and the CHECKPOINT line' -Detail $r.Text
}

function Test-C589_SlotTimeout {
    $fx = New-C585Case -Name 'slot-timeout'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'windows' -Slot -SlotScript 'busy' -SlotWaitSeconds '1' -SlotRetryMs '50'
    Assert-C487 -Cond ($r.Exit -eq 4) -Name 'C589 SlotTimeout exit code 4' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ((Get-C585BuildCalls -Result $r).Count -eq 0 -and (Get-C585RunCalls -Result $r).Count -eq 0) `
        -Name 'C589 SlotTimeout invokes no dotnet' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond (@($r.Lines | Where-Object { $_ -cmatch '^BUILD SLOT timeout after 1s position=1$' }).Count -eq 1) `
        -Name 'C589 SlotTimeout prints the timeout line with its queue position' -Detail $r.Text
    Assert-C487 -Cond (@($r.Calls | Where-Object { $_ -like 'SLOT DELETE *' }).Count -eq 0 -and @($r.Calls | Where-Object { $_ -like 'SLOT POST *' }).Count -ge 2) `
        -Name 'C589 SlotTimeout polled and holds nothing to release' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond ([string]$r.Lines[$r.Lines.Count - 1] -ceq 'CHECKPOINT CP-1 EXIT CODE: 4') `
        -Name 'C589 SlotTimeout trailer names exit code 4' -Detail $r.Text
}

function Test-C589_SlotUnreachable {
    foreach ($case in @(@{ Script = 'unreachable'; Tag = 'no answer' }, @{ Script = 'notfound'; Tag = 'old runner 404' })) {
        $fx = New-C585Case -Name ('slot-unreachable-' + $case.Script)
        $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'windows' -Slot -SlotScript $case.Script -SlotGraceSeconds '0'
        $build = Get-C671Call (Get-C585BuildCalls -Result $r)
        $line = [string]($r.Checkpoint | Select-Object -First 1)
        Assert-C487 -Cond (@($r.Lines | Where-Object { $_ -cmatch '^BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=' }).Count -eq 1) `
            -Name ('C589 SlotUnreachable {0} prints the unleased line' -f $case.Tag) -Detail $r.Text
        Assert-C487 -Cond ($build -ceq ($script:C671Build + ' -maxcpucount 4 --nologo') -and $r.Exit -eq 0 -and $line -match ' slot=unleased waited=\d+s dirty=') `
            -Name ('C589 SlotUnreachable {0} still builds with the fallback -maxcpucount 4' -f $case.Tag) -Detail ('exit={0} build={1} {2}' -f $r.Exit, $build, $r.Text)
        Assert-C487 -Cond (@($r.Calls | Where-Object { $_ -like 'SLOT DELETE *' }).Count -eq 0) `
            -Name ('C589 SlotUnreachable {0} releases nothing' -f $case.Tag) -Detail ($r.Calls -join ' | ')
    }
    $fail = New-C585Case -Name 'slot-unreachable-red'
    $f = Invoke-C585Runner -Fx $fail -Trx $script:FailuresTrx -RunExit 1 -Platform 'windows' -Slot -SlotScript 'unreachable' -SlotGraceSeconds '0'
    Assert-C487 -Cond ($f.Exit -eq 1) -Name 'C589 SlotUnreachable the exit code follows the run' -Detail ('exit={0} {1}' -f $f.Exit, $f.Text)
}

function Test-C589_NoBuildStillLeases {
    $fx = New-C585Case -Name 'slot-nobuild'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'windows' -Slot -SlotScript 'granted' -NoBuild
    $line = [string]($r.Checkpoint | Select-Object -First 1)
    Assert-C487 -Cond ((Get-C589Order -Result $r) -ceq 'POST,run,DELETE') `
        -Name 'C589 NoBuildStillLeases a -NoBuild row acquires and releases around its run' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond ($r.Exit -eq 0 -and $line -match 'build=reused ' -and $line -match ' slot=granted waited=\d+s dirty=') `
        -Name 'C589 NoBuildStillLeases line reports build=reused and slot=granted' -Detail $r.Text
}

function Test-C589_NoSlotSkips {
    $fx = New-C585Case -Name 'slot-noslot'
    $r = Invoke-C585Runner -Fx $fx -Trx $script:GreenTrx -Platform 'windows'
    $build = Get-C671Call (Get-C585BuildCalls -Result $r)
    Assert-C487 -Cond ([string]$r.Lines[0] -ceq 'BUILD SLOT skipped by -NoSlot' -and @($r.Calls | Where-Object { $_ -like 'SLOT *' }).Count -eq 0) `
        -Name 'C589 NoSlotSkips -NoSlot asks the broker nothing and says so' -Detail ($r.Lines -join ' | ')
    Assert-C487 -Cond ($r.Exit -eq 0 -and $build -ceq ($script:C671Build + ' --nologo') -and ([string]($r.Checkpoint | Select-Object -First 1)) -match ' slot=skipped waited=0s dirty=') `
        -Name 'C589 NoSlotSkips builds with no -maxcpucount and reports slot=skipped' -Detail ('exit={0} build={1} {2}' -f $r.Exit, $build, $r.Text)
}

# CARD-0578: this shim is intentionally separate from the silent C585 shim. Its entry
# record is written by the child; the phase log and its receipt belong to the runner.
$script:C578ShimBody = @'
param()
$phase = [string]$args[0]
$root = $env:C578_ROOT
$log = Join-Path $env:C578_PHASE_DIR ("$phase.log")
$entry = Join-Path $root ("$phase.entry")
$record = ('{0}|{1}|{2}' -f $PID, [System.Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().Ticks, (Test-Path -LiteralPath $log))
[System.IO.File]::WriteAllText($entry, $record)
[System.IO.File]::AppendAllText((Join-Path $root 'calls.log'), "$phase`n")
[Console]::Out.WriteLine(('C578_{0}_STARTED_{1}' -f $phase.ToUpperInvariant(), $env:C578_NONCE))
[Console]::Out.Flush()
[Console]::Error.WriteLine(('C578_{0}_ERROR_{1}' -f $phase.ToUpperInvariant(), $env:C578_NONCE))
[Console]::Error.Flush()
[System.IO.File]::WriteAllText((Join-Path $root "$phase.ready"), 'ready')
if ($env:C578_HOLD -eq 'both' -or ($env:C578_HOLD -eq 'build' -and $phase -eq 'build')) {
    $release = Join-Path $root "$phase.release"
    while (-not (Test-Path -LiteralPath $release)) { Start-Sleep -Milliseconds 25 }
}
if ($phase -eq 'run') {
    $argv = @($args)
    for ($i = 0; $i -lt $argv.Count - 1; $i++) {
        if ($argv[$i] -eq '--results-directory') {
            Copy-Item -LiteralPath $env:C578_GREEN_TRX -Destination (Join-Path $argv[$i + 1] 'run.trx') -Force
            break
        }
    }
}
if ($phase -eq 'build' -and $env:C578_FAIL_BUILD -eq '1') { exit 37 }
exit 0
'@

function New-C578Case {
    param([string]$Name, [string]$Hold = '', [switch]$FailBuild)
    $fx = New-C585Case -Name $Name
    Set-Content -LiteralPath $fx.Shim -Value $script:C578ShimBody -Encoding ASCII
    $fx | Add-Member NoteProperty Stamp 'c578-fixed'
    $fx | Add-Member NoteProperty Nonce ([guid]::NewGuid().ToString('N'))
    $fx | Add-Member NoteProperty Hold $Hold
    $fx | Add-Member NoteProperty FailBuild ([bool]$FailBuild)
    $fx | Add-Member NoteProperty PhaseDir (Join-Path $fx.ResultsRoot ('CP-1-' + $fx.Stamp))
    return $fx
}

function Start-C578Runner {
    param($Fx)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'pwsh'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $script:RepoRoot
    foreach ($token in @('-NoProfile', '-NonInteractive', '-File', $script:Runner,
        '-Name', 'CP-1', '-Project', 'tests/Antiphon.Tests', '-OutputPath', 'bin-c578h/',
        '-Filter', '/*/*/C585SampleTests/*', '-ResultsRoot', $Fx.ResultsRoot,
        '-MinExecuted', '1', '-DotnetShim', $Fx.Shim)) { [void]$psi.ArgumentList.Add([string]$token) }
    # The fake HTTP endpoint and the slot shim prevent contact with a live runner.
    $psi.Environment['ANTIPHON_BUILD_SLOTS_URL'] = 'http://127.0.0.1:1/build-slots'
    $psi.Environment['C589_SLOT_SHIM'] = $Fx.SlotShim
    $psi.Environment['C589_SLOT_SCRIPT'] = 'granted'
    $psi.Environment['C589_SLOT_LOG'] = (Join-Path $Fx.Root 'slot.log')
    $psi.Environment['C585_STAMP'] = $Fx.Stamp
    $psi.Environment['C578_ROOT'] = $Fx.Root
    $psi.Environment['C578_PHASE_DIR'] = $Fx.PhaseDir
    $psi.Environment['C578_NONCE'] = $Fx.Nonce
    $psi.Environment['C578_HOLD'] = $Fx.Hold
    $psi.Environment['C578_FAIL_BUILD'] = $(if ($Fx.FailBuild) { '1' } else { '0' })
    $psi.Environment['C578_GREEN_TRX'] = $script:GreenTrx
    $proc = [System.Diagnostics.Process]::Start($psi)
    return [pscustomobject]@{ Process = $proc; Out = $proc.StandardOutput.ReadToEndAsync(); Err = $proc.StandardError.ReadToEndAsync(); Children = @{} }
}

function Get-C578Child {
    param($Run, $Fx, [string]$Phase)
    $entry = Join-Path $Fx.Root "$Phase.entry"
    if (-not (Test-Path -LiteralPath $entry)) { return $null }
    $parts = ([System.IO.File]::ReadAllText($entry)).Split('|')
    if ($parts.Count -ne 3) { return $null }
    try {
        $proc = [System.Diagnostics.Process]::GetProcessById([int]$parts[0])
        # Linux exposes slightly different fractional StartTime ticks when read from
        # inside and outside the process. The PID plus a one-second window owns it.
        if ([math]::Abs($proc.StartTime.ToUniversalTime().Ticks - [long]$parts[1]) -gt [TimeSpan]::TicksPerSecond) { return $null }
        $Run.Children[$Phase] = $proc
        return [pscustomobject]@{ Process = $proc; LogAtEntry = ($parts[2] -eq 'True'); Pid = [int]$parts[0]; StartTicks = [long]$parts[1] }
    } catch { return $null }
}

function Read-C578Log {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try { $reader = [System.IO.StreamReader]::new($stream); return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

function Wait-C578RunOutput {
    param($Run, $Fx)
    $path = Join-Path $Fx.PhaseDir 'run.log'
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline) {
        $log = Read-C578Log $path
        if ($log.Contains("C578_RUN_STARTED_$($Fx.Nonce)") -and $log.Contains("C578_RUN_ERROR_$($Fx.Nonce)")) { return $log }
        if ($Run.Process.HasExited) { break }
        Start-Sleep -Milliseconds 25
    }
    return Read-C578Log $path
}

function Wait-C578Ready {
    param($Run, $Fx, [string]$Phase)
    $deadline = (Get-Date).AddSeconds(10)
    $ready = Join-Path $Fx.Root "$Phase.ready"
    $entry = Join-Path $Fx.Root "$Phase.entry"
    while ((Get-Date) -lt $deadline) {
        $premature = $Run.Process.HasExited -or (($Phase -eq 'build') -and (Test-Path -LiteralPath (Join-Path $Fx.Root 'run.entry')))
        if ($premature) { return [pscustomobject]@{ Ready = $false; Premature = $true; Child = (Get-C578Child $Run $Fx $Phase) } }
        if ((Test-Path -LiteralPath $ready) -and (Test-Path -LiteralPath $entry)) {
            $child = Get-C578Child $Run $Fx $Phase
            if ($child) { return [pscustomobject]@{ Ready = $true; Premature = $false; Child = $child } }
        }
        Start-Sleep -Milliseconds 25
    }
    return [pscustomobject]@{ Ready = $false; Premature = $false; Child = (Get-C578Child $Run $Fx $Phase) }
}

function Wait-C578Exit {
    param($Run, [int]$Seconds = 10)
    if (-not $Run.Process.WaitForExit($Seconds * 1000)) { return $false }
    $Run.Process.WaitForExit()
    return $true
}

function Stop-C578Owned {
    param($Run, [switch]$Interrupt)
    # On interruption the wrapper dies first, so it cannot turn the child's kill
    # into a legitimate completed failed-build receipt.
    if ($Interrupt -and -not $Run.Process.HasExited) { $Run.Process.Kill(); [void]$Run.Process.WaitForExit(10000) }
    foreach ($child in @($Run.Children.Values)) {
        try {
            if (-not $child.HasExited) { $child.Kill($true) }
            [void]$child.WaitForExit(10000)
        } catch [System.InvalidOperationException] { }
    }
    if (-not $Run.Process.HasExited) { $Run.Process.Kill($true); [void]$Run.Process.WaitForExit(10000) }
    [void]$Run.Out.Result
    [void]$Run.Err.Result
    return ($Run.Process.HasExited -and @($Run.Children.Values | Where-Object { -not $_.HasExited }).Count -eq 0)
}

function Save-C578Evidence {
    param($Fx, $Run, [string]$Observation)
    $record = @("wrapperExit=$($Run.Process.ExitCode)", $Observation, "calls=$(Read-C578Log (Join-Path $Fx.Root 'calls.log'))",
        "buildLog=$(Read-C578Log (Join-Path $Fx.PhaseDir 'build.log'))", "runLog=$(Read-C578Log (Join-Path $Fx.PhaseDir 'run.log'))") -join "`n"
    Set-Content -LiteralPath (Join-Path $Fx.Root 'observation.txt') -Value $record
    if ($env:C578_EVIDENCE_ROOT) {
        $dest = Join-Path $env:C578_EVIDENCE_ROOT ([IO.Path]::GetFileName($Fx.Root))
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        Copy-Item -Path (Join-Path $Fx.Root '*') -Destination $dest -Recurse -Force
    }
}

function Test-C578_BuildLogVisibleBeforeExit {
    $fx = New-C578Case -Name 'c578-streaming' -Hold 'both'
    $run = Start-C578Runner $fx
    $observation = ''
    try {
        $build = Wait-C578Ready $run $fx 'build'
        $buildLog = Read-C578Log (Join-Path $fx.PhaseDir 'build.log')
        $observation += "buildReady=$($build.Ready) premature=$($build.Premature) pid=$($build.Child.Pid) live=$(-not $run.Process.HasExited)`n"
        Assert-C487 -Cond ($build.Ready -and $build.Child.LogAtEntry) -Name 'C578 Streaming build log exists before child start' -Detail $observation
        Assert-C487 -Cond ($build.Ready -and -not $build.Child.Process.HasExited -and -not $run.Process.HasExited -and
            ($buildLog.Split("C578_BUILD_STARTED_$($fx.Nonce)").Count -eq 2) -and ($buildLog.Split("C578_BUILD_ERROR_$($fx.Nonce)").Count -eq 2)) `
            -Name 'C578 Streaming build streams readable while child and wrapper live' -Detail $buildLog
        Assert-C487 -Cond ($build.Ready -and -not $build.Premature -and $buildLog -notmatch 'DOTNET build EXIT CODE:' -and
            -not (Test-Path (Join-Path $fx.Root 'run.entry')) -and -not $run.Process.HasExited) `
            -Name 'C578 Streaming held build has no completion or test run' -Detail $observation
        Set-Content -LiteralPath (Join-Path $fx.Root 'build.release') -Value 'release'
        $runReady = Wait-C578Ready $run $fx 'run'
        $buildDone = Read-C578Log (Join-Path $fx.PhaseDir 'build.log')
        Assert-C487 -Cond ($runReady.Ready -and $buildDone.Split('DOTNET build EXIT CODE: 0').Count -eq 2) `
            -Name 'C578 Streaming build completion records exit 0' -Detail $buildDone
        $runLog = Wait-C578RunOutput $run $fx
        $observation += "runReady=$($runReady.Ready) premature=$($runReady.Premature) pid=$($runReady.Child.Pid)`n"
        Assert-C487 -Cond ($runReady.Ready -and $runReady.Child.LogAtEntry) -Name 'C578 Streaming run log exists before child start' -Detail $observation
        Assert-C487 -Cond ($runReady.Ready -and -not $runReady.Child.Process.HasExited -and -not $run.Process.HasExited -and
            $runLog.Split("C578_RUN_STARTED_$($fx.Nonce)").Count -eq 2 -and $runLog.Split("C578_RUN_ERROR_$($fx.Nonce)").Count -eq 2 -and
            $runLog -notmatch 'DOTNET run EXIT CODE:') -Name 'C578 Streaming run streams readable while child and wrapper live' -Detail $runLog
        Set-Content -LiteralPath (Join-Path $fx.Root 'run.release') -Value 'release'
        $exited = Wait-C578Exit $run
        $stdout = [string]$run.Out.Result + [string]$run.Err.Result
        $runDone = Read-C578Log (Join-Path $fx.PhaseDir 'run.log')
        Assert-C487 -Cond ($exited -and $run.Process.ExitCode -eq 0 -and (Test-Path (Join-Path $fx.PhaseDir 'run.trx')) -and
            $runDone.Split('DOTNET run EXIT CODE: 0').Count -eq 2 -and $stdout -match 'executed=3 passed=3 failed=0 skipped=0' -and
            $stdout.Contains("C578_BUILD_STARTED_$($fx.Nonce)") -and $stdout.Contains("C578_RUN_ERROR_$($fx.Nonce)")) `
            -Name 'C578 Streaming completed run has fresh green TRX' -Detail $stdout
    } finally {
        $clean = Stop-C578Owned $run
        Save-C578Evidence $fx $run $observation
        Assert-C487 -Cond $clean -Name 'C578 Streaming owned processes exited'
    }
}

function Test-C578_FailedBuildKeepsLogAndExit {
    $fx = New-C578Case -Name 'c578-failed' -FailBuild
    $run = Start-C578Runner $fx
    try {
        $ready = Wait-C578Ready $run $fx 'build'
        $exited = Wait-C578Exit $run
        $log = Read-C578Log (Join-Path $fx.PhaseDir 'build.log')
        $stdout = [string]$run.Out.Result + [string]$run.Err.Result
        Assert-C487 -Cond ($ready.Ready -and $log.Contains("C578_BUILD_STARTED_$($fx.Nonce)") -and $log.Contains("C578_BUILD_ERROR_$($fx.Nonce)")) `
            -Name 'C578 FailedBuild retains stdout and stderr' -Detail $log
        Assert-C487 -Cond ($log.Split('DOTNET build EXIT CODE: 37').Count -eq 2) -Name 'C578 FailedBuild records actual exit 37 once' -Detail $log
        Assert-C487 -Cond ($exited -and $run.Process.ExitCode -eq 2 -and $stdout -match 'build=failed .*exit=37' -and $stdout -match 'CHECKPOINT CP-1 EXIT CODE: 2') `
            -Name 'C578 FailedBuild returns checkpoint exit 2' -Detail $stdout
        Assert-C487 -Cond (-not (Test-Path (Join-Path $fx.Root 'run.entry')) -and -not (Test-Path (Join-Path $fx.PhaseDir 'run.log')) -and
            -not (Test-Path (Join-Path $fx.PhaseDir 'run.trx')) -and $stdout -notmatch 'CHECKPOINT CP-1 EXIT CODE: 0|^EXECUTED ') `
            -Name 'C578 FailedBuild never invokes tests' -Detail $stdout
    } finally {
        $clean = Stop-C578Owned $run
        Save-C578Evidence $fx $run "buildReady=$($ready.Ready) pid=$($ready.Child.Pid)"
        Assert-C487 -Cond $clean -Name 'C578 FailedBuild owned processes exited'
    }
}

function Test-C578_NoSuccessReceiptForInterruptedBuild {
    $fx = New-C578Case -Name 'c578-interrupted' -Hold 'build'
    $run = Start-C578Runner $fx
    try {
        $ready = Wait-C578Ready $run $fx 'build'
        $before = Read-C578Log (Join-Path $fx.PhaseDir 'build.log')
        $premature = $ready.Premature -or $run.Process.HasExited -or (Test-Path (Join-Path $fx.Root 'run.entry'))
        Assert-C487 -Cond ($ready.Ready -and -not $ready.Child.Process.HasExited -and -not $run.Process.HasExited) `
            -Name 'C578 InterruptedBuild observed owned live child' -Detail "ready=$($ready.Ready) premature=$premature pid=$($ready.Child.Pid)"
        # The wrapper is deliberately stopped first. A child-first kill could produce a
        # legitimate nonzero completed-build receipt and would test the wrong boundary.
        $clean = Stop-C578Owned $run -Interrupt
        $after = Read-C578Log (Join-Path $fx.PhaseDir 'build.log')
        $stdout = [string]$run.Out.Result + [string]$run.Err.Result
        Assert-C487 -Cond ((Test-Path (Join-Path $fx.PhaseDir 'build.log')) -and $after.Contains("C578_BUILD_STARTED_$($fx.Nonce)") -and
            $after.Contains("C578_BUILD_ERROR_$($fx.Nonce)")) -Name 'C578 InterruptedBuild partial log survives interruption' -Detail $after
        Assert-C487 -Cond ($before -notmatch 'DOTNET build EXIT CODE:' -and $after -notmatch 'DOTNET build EXIT CODE:') `
            -Name 'C578 InterruptedBuild never records a completion marker' -Detail $after
        Assert-C487 -Cond (-not (Test-Path (Join-Path $fx.Root 'run.entry')) -and -not (Test-Path (Join-Path $fx.PhaseDir 'run.log')) -and
            -not (Test-Path (Join-Path $fx.PhaseDir 'run.trx'))) -Name 'C578 InterruptedBuild never invokes tests or creates run results'
        Assert-C487 -Cond (-not $premature -and $stdout -notmatch 'build=ok|CHECKPOINT CP-1 EXIT CODE: 0') `
            -Name 'C578 InterruptedBuild never reports green' -Detail $stdout
    } finally {
        $clean = Stop-C578Owned $run -Interrupt
        Save-C578Evidence $fx $run "ready=$($ready.Ready) premature=$premature pid=$($ready.Child.Pid)"
        Assert-C487 -Cond $clean -Name 'C578 InterruptedBuild owned processes exited'
    }
}

$script:C585ExpectedRows = 62 + 28 + 19

if (-not (Test-Path -LiteralPath $script:Runner)) { throw ('missing ' + $script:Runner) }

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in @(Get-C487CaseFunctions -Prefix 'C585_') + @(Get-C487CaseFunctions -Prefix 'C589_') + @(Get-C487CaseFunctions -Prefix 'C578_')) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'run-checkpoint-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { $script:C585ExpectedRows })
