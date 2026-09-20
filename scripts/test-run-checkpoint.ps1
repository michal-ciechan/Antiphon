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

function New-C585Case {
    param([string]$Name)
    $root = Join-Path $ResultsDirectory ($Name + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $shim = Join-Path $root 'dotnet-shim.ps1'
    Set-Content -LiteralPath $shim -Value $script:ShimBody -Encoding ASCII
    return [pscustomobject]@{
        Root = $root
        Shim = $shim
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
        [string]$ResultsRoot = ''
    )
    if ([string]::IsNullOrWhiteSpace($ResultsRoot)) { $ResultsRoot = $Fx.ResultsRoot }
    $env:C585_SHIM_LOG = $Fx.Log
    $env:C585_TRX = $Trx
    $env:C585_BUILD_EXIT = [string]$BuildExit
    $env:C585_RUN_EXIT = [string]$RunExit
    $env:C585_STAMP = $Stamp
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
    foreach ($token in @($Expect)) {
        if ($token) { $callArgs += @('-Expect', $token) }
    }
    Push-Location -LiteralPath $script:RepoRoot
    try {
        $output = & pwsh @callArgs 2>&1
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
        $env:C585_SHIM_LOG = $null
        $env:C585_TRX = $null
        $env:C585_BUILD_EXIT = $null
        $env:C585_RUN_EXIT = $null
        $env:C585_STAMP = $null
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
    Assert-C487 -Cond ($line -match 'trx=.+run\.trx$') -Name 'C585 Green line names the fresh TRX it parsed' -Detail $line
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
    $first = ''
    if ($r.Lines.Count -gt 0) { $first = [string]$r.Lines[0] }
    $last = ''
    if ($r.Lines.Count -gt 0) { $last = [string]$r.Lines[$r.Lines.Count - 1] }
    Assert-C487 -Cond ($first -match '^CHECKPOINT CP-1 commit=[0-9a-f]{40} build=(ok|reused) filter=.+ executed=\d+ passed=\d+ failed=\d+ skipped=\d+ trx=.+$') `
        -Name 'C585 LineFormat first line is the pinned CHECKPOINT report line' -Detail $first
    Assert-C487 -Cond ($last -match '^CHECKPOINT CP-1 EXIT CODE: \d$') -Name 'C585 LineFormat last line is the exit-code trailer' -Detail $last
}

function Test-C585_AsciiOnly {
    $runnerBytes = @([IO.File]::ReadAllBytes($script:Runner) | Where-Object { $_ -gt 127 })
    Assert-C487 -Cond ($runnerBytes.Count -eq 0) -Name 'C585 AsciiOnly run-checkpoint.ps1 is ASCII-only' -Detail ([string]$runnerBytes.Count)
    $harness = Join-Path $here 'test-run-checkpoint.ps1'
    $harnessBytes = @([IO.File]::ReadAllBytes($harness) | Where-Object { $_ -gt 127 })
    Assert-C487 -Cond ($harnessBytes.Count -eq 0) -Name 'C585 AsciiOnly the harness is ASCII-only' -Detail ([string]$harnessBytes.Count)
}

$script:C585ExpectedRows = 38

if (-not (Test-Path -LiteralPath $script:Runner)) { throw ('missing ' + $script:Runner) }

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C585_')) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'run-checkpoint-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { $script:C585ExpectedRows })
