#requires -Version 7.0
# CARD-1336: evaluate the production Bash body with an inert, readonly ssh function.
param(
    [Parameter(Mandatory)][ValidateSet('C1336_WrapperRef', 'C1336_NightlyCandidateIdentity')][string]$Case,
    [Parameter(Mandatory)][string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'scripts/lib/c487-harness.ps1')
$utf8 = [System.Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null

function Write-C1336File($Root, $Relative, $Text) {
    $path = Join-Path $Root $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path, $Text, $utf8)
}

function Git-C1336($Root, $Arguments) {
    return (Invoke-TestSelectionGit $Root $Arguments).Stdout.Trim()
}

function New-C1336Candidate {
    foreach ($name in @('git', 'pwsh')) { [void](Get-Command $name -CommandType Application -ErrorAction Stop) }
    $root = Join-Path $ResultsDirectory 'candidate fixture'
    $seed = Join-Path $root 'seed'
    [void][IO.Directory]::CreateDirectory($seed)
    [void](Git-C1336 $seed @('init', '-q', '-b', 'rc/2026-10-18'))
    [void](Git-C1336 $seed @('config', 'user.name', 'C1336 fixture'))
    [void](Git-C1336 $seed @('config', 'user.email', 'fixture@example.invalid'))
    [void](Git-C1336 $seed @('config', 'core.autocrlf', 'false'))
    [void][IO.Directory]::CreateDirectory((Join-Path $seed 'scripts'))
    Copy-Item (Join-Path $repoRoot 'scripts/lib') (Join-Path $seed 'scripts/lib') -Recurse
    foreach ($name in @('nightly-run.ps1', 'nightly-tests.ps1', 'select-test-rosters.ps1')) {
        Copy-Item (Join-Path $repoRoot "scripts/$name") (Join-Path $seed "scripts/$name")
    }
    foreach ($name in @('tests/test-execution-policy.json', 'tests/test-selection-policy.json', 'global.json')) {
        Write-C1336File $seed $name ([IO.File]::ReadAllText((Join-Path $repoRoot $name)))
    }
    $catalog = Get-Content (Join-Path $seed 'tests/test-selection-policy.json') -Raw | ConvertFrom-Json
    foreach ($path in @($catalog.sets.sourceInputs + $catalog.sets.members.source | Sort-Object -Unique)) {
        if ($path -and -not (Test-Path (Join-Path $seed $path))) { Write-C1336File $seed $path 'fixture source' }
    }
    foreach ($path in (Git-C1336 $repoRoot @('ls-files', 'tests/*.csproj', 'scripts/fixtures/nightly/c487-probe/Probe.csproj')).Split("`n")) {
        Write-C1336File $seed $path ([IO.File]::ReadAllText((Join-Path $repoRoot $path)))
    }
    Add-Content (Join-Path $seed 'scripts/nightly-run.ps1') '# C1336 owned clone self-hop'
    Write-C1336File $seed '.antiphon-nightly-owned' '{"kind":"nightly-clone"}'
    Write-C1336File $seed 'fixture.txt' 'A'
    [void](Git-C1336 $seed @('add', '--all'))
    [void](Git-C1336 $seed @('commit', '-qm', 'candidate A'))
    $a = Git-C1336 $seed @('rev-parse', 'HEAD')
    Write-C1336File $seed 'fixture.txt' 'B'
    [void](Git-C1336 $seed @('add', '--all'))
    [void](Git-C1336 $seed @('commit', '-qm', 'candidate B'))
    $b = Git-C1336 $seed @('rev-parse', 'HEAD')
    if ($a.Substring(0, 12) -ceq $b.Substring(0, 12)) { throw 'Fixture pins must differ' }
    [void](Git-C1336 $seed @('branch', 'fixture-b', $b))
    [void](Git-C1336 $seed @('branch', 'release/rc-20260923T083000Z', $a))
    [void](Git-C1336 $seed @('reset', '--hard', $a))
    $origin = Join-Path $root 'origin.git'
    [void](Git-C1336 $root @('clone', '--bare', $seed, $origin))
    $clone = Join-Path $root 'checkout'
    [void](Git-C1336 $root @('clone', $origin, $clone))
    # Fail fixture prerequisites before the behavioral red witness.
    Assert-ReleaseSelectionCommit $clone $a
    $contract = Get-ReleaseSelectionContract $clone $a 'rc'
    if (-not $contract.policyHash -or -not $contract.catalogHash) { throw 'Fixture contract missing hashes' }
    $master = Join-Path $root 'private-master'
    foreach ($name in @('last-run.json', 'last-complete-green.json', 'last-monitor.json', 'interim-qualification-receipt.json')) {
        Write-C1336File $master $name ('{"sentinel":"' + $name + '"}')
    }
    $seams = Join-Path $root 'seams.ps1'
    $trace = Join-Path $root 'trace.txt'
    Write-C487NativePassSeams $seams $trace @(@{Id='fixture-one';ClassName='Fixture.Fixture';MethodName='One';Outcome='Passed'}) @(@{uid='fixture-one';type='Fixture';method='One';namespace='Fixture'})
    Add-Content $seams @"
`$NightlySeams.Git = `$null
`$NightlySeams.E2EPrerequisites = { return @{Ok=`$true;Rows=@()} }
Set-Item Function:script:Get-NightlyDefaultStateRoot { return '$($master.Replace("'", "''"))' }
"@
    return @{Root=$root;Seed=$seed;Clone=$clone;Origin=$origin;A=$a;B=$b;Master=$master;Seams=$seams;Trace=$trace}
}

if ($Case -ceq 'C1336_NightlyCandidateIdentity') {
    foreach ($name in @('nightly-common', 'nightly-policy', 'nightly-coverage', 'release-gate', 'release-selection', 'test-selection')) {
        . (Join-Path $repoRoot "scripts/lib/$name.ps1")
    }
    $fx = New-C1336Candidate
    $args = @{
        Profile='rc';Trigger='rc';Ref='rc/2026-10-18';ExpectedSha=$fx.A
        CheckoutRoot=$fx.Clone;RemoteUrl=$fx.Origin;Repository='fixture/repository'
        StateRoot=(Join-Path $fx.Root 'state');LogRoot=(Join-Path $fx.Root 'logs')
        ReleaseRoot=(Join-Path $fx.Root 'release');CoordinationRoot=(Join-Path $fx.Root 'coord')
        SeamsPath=$fx.Seams;RunId='omitted';NoSync=$true;WhatIf=$true;PassThru=$true
    }
    $result = & (Join-Path $repoRoot 'scripts/nightly-run.ps1') @args
    $expected = 'rc-2026-10-18-' + $fx.A.Substring(0, 12)
    Assert-C487 ($result.CandidateId -ceq $expected) 'C1336 N omitted' ("CandidateId expected=$expected actual=$($result.CandidateId); refusal=$($result.Refusal)")
    Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
    return
}

function Invoke-C1336Wrapper {
    param([string]$Name, [string]$Schedule, [int]$SshExit)
    $root = Join-Path $ResultsDirectory $Name
    New-Item -ItemType Directory -Path $root | Out-Null
    $emptyPath = Join-Path $root 'empty-path'
    New-Item -ItemType Directory -Path $emptyPath | Out-Null
    $definition = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/windmill/antiphon-release-candidates.json') -Raw | ConvertFrom-Json
    $bodyPath = Join-Path $root 'body.sh'
    [System.IO.File]::WriteAllText($bodyPath, [string]$definition.content, $utf8)
    $launcherPath = Join-Path $root 'capture.sh'
    # Only Bash builtins are reachable. No external ssh is on PATH, even if function setup fails.
    $launcher = @'
set -euo pipefail
ssh() {
    printf 'call\n' >> "$C1336_CALLS"
    printf '%s\0' "$@" >> "$C1336_ARGS"
    return "$C1336_SSH_EXIT"
}
readonly -f ssh
[[ "$(type -t ssh)" == function ]] || exit 97
set +e
source "$1"
'@
    [System.IO.File]::WriteAllText($launcherPath, $launcher.Replace("`r`n", "`n") + "`n", $utf8)
    $bash = (Get-Command bash -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $start = [System.Diagnostics.ProcessStartInfo]::new($bash)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $root
    foreach ($arg in @('--noprofile', '--norc', $launcherPath.Replace('\', '/'), $bodyPath.Replace('\', '/'))) {
        $start.ArgumentList.Add($arg)
    }
    foreach ($key in @($start.Environment.Keys)) {
        if ($key -in @('BASH_ENV', 'ENV', 'SHELLOPTS', 'BASHOPTS', 'WM_SCHEDULE_PATH', 'WM_JOB_ID') -or
            $key.StartsWith('BASH_FUNC_')) { [void]$start.Environment.Remove($key) }
    }
    $start.Environment['PATH'] = $emptyPath.Replace('\', '/')
    $start.Environment['WM_JOB_ID'] = 'c1336-job'
    if ($Schedule) { $start.Environment['WM_SCHEDULE_PATH'] = $Schedule }
    $argsPath = Join-Path $root 'argv.bin'
    $callsPath = Join-Path $root 'calls.txt'
    $start.Environment['C1336_ARGS'] = $argsPath.Replace('\', '/')
    $start.Environment['C1336_CALLS'] = $callsPath.Replace('\', '/')
    $start.Environment['C1336_SSH_EXIT'] = [string]$SshExit
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false
    try {
        if (-not $process.Start()) { throw 'Bash capture did not start' }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        # The operation owns its deadline; outer host scheduling is not a performance assertion.
        if (-not $process.WaitForExit(30000)) { throw 'Bash capture exceeded its 30-second operation budget' }
        $outText = $stdout.GetAwaiter().GetResult()
        $errText = $stderr.GetAwaiter().GetResult()
        if (-not (Test-Path -LiteralPath $argsPath) -or -not (Test-Path -LiteralPath $callsPath)) {
            throw "Bash capture missing: exit=$($process.ExitCode) stdout=$outText stderr=$errText"
        }
        $captured = [System.IO.File]::ReadAllText($argsPath).Split([char]0)
        if ($captured[-1] -cne '') { throw 'Bash argument capture is not NUL terminated' }
        $remoteCommand = $captured[-2]
        $tokens = @($remoteCommand -split '\s+')
        $refIndexes = @(for ($i = 0; $i -lt $tokens.Count; $i++) { if ($tokens[$i] -ceq '-Ref') { $i } })
        $hasRef = $refIndexes.Count -eq 1 -and $tokens[$refIndexes[0] + 1] -ceq 'rc/2026-10-18'
        $slot = if ($Schedule -ceq 'u/lndcobra/antiphon_release_candidates') { $Schedule } else { 'manual' }
        $valid = $hasRef -and @([System.IO.File]::ReadAllLines($callsPath)).Count -eq 1 -and
            $remoteCommand -cmatch ' -File C:\\src\\Antiphon\\scripts\\release-cut\.ps1 ' -and
            @($tokens | Where-Object { $_ -ceq '-ScheduleSlot' }).Count -eq 1 -and
            $tokens[[array]::IndexOf($tokens, '-ScheduleSlot') + 1] -ceq $slot -and
            @($tokens | Where-Object { $_ -ceq '-JobId' }).Count -eq 1 -and
            $tokens[[array]::IndexOf($tokens, '-JobId') + 1] -ceq 'c1336-job' -and
            $process.ExitCode -eq $SshExit
        Assert-C487 -Cond $valid -Name ('C1336 W ' + $Name) -Detail (
            "Ref count=$($refIndexes.Count); expected Ref=rc/2026-10-18 slot=$slot exit=$SshExit; actual exit=$($process.ExitCode); command=$remoteCommand; stderr=$errText")
    } finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) { throw 'Bash capture cleanup failed' }
        }
        $process.Dispose()
    }
}

Invoke-C1336Wrapper -Name 'own-0' -Schedule 'u/lndcobra/antiphon_release_candidates' -SshExit 0
Invoke-C1336Wrapper -Name 'own-23' -Schedule 'u/lndcobra/antiphon_release_candidates' -SshExit 23
Invoke-C1336Wrapper -Name 'missing-0' -Schedule '' -SshExit 0
Invoke-C1336Wrapper -Name 'missing-23' -Schedule '' -SshExit 23
Invoke-C1336Wrapper -Name 'foreign-0' -Schedule 'u/lndcobra/antiphon_nightly_tests' -SshExit 0
Invoke-C1336Wrapper -Name 'foreign-23' -Schedule 'u/lndcobra/antiphon_nightly_tests' -SshExit 23

$definition = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/windmill/antiphon-release-candidates.json') -Raw | ConvertFrom-Json
$schedule = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/windmill/antiphon-release-candidates.schedule.json') -Raw | ConvertFrom-Json
$scheduleOk = $schedule.enabled -is [bool] -and -not $schedule.enabled -and
    $schedule.schedule -ceq '0 30 8,16 * * *' -and $schedule.timezone -ceq 'Europe/London' -and
    $null -ne $schedule.args -and @($schedule.args.PSObject.Properties).Count -eq 0 -and
    $null -ne $definition.schema.properties -and @($definition.schema.properties.PSObject.Properties).Count -eq 0 -and
    $definition.schema.PSObject.Properties.Name -contains 'required' -and @($definition.schema.required).Count -eq 0
Assert-C487 -Cond $scheduleOk -Name 'C1336 W schedule' -Detail ($schedule | ConvertTo-Json -Compress)

# Frozen independent Git blob IDs, including Git's checkout line-ending normalization.
$git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$masterBlobs = @(& $git -C $repoRoot hash-object -- scripts/windmill/antiphon-nightly-tests.json scripts/windmill/antiphon-nightly-tests.schedule.json)
if ($LASTEXITCODE -ne 0) { throw 'Cannot hash the master wrapper/schedule' }
$masterOk = $masterBlobs.Count -eq 2 -and
    $masterBlobs[0] -ceq '4073816bbfb057c4dcf12e051862d32441d68dc4' -and
    $masterBlobs[1] -ceq '781f8559ea2598750eb130da49bb979f09e079c0'
Assert-C487 -Cond $masterOk -Name 'C1336 W master-unchanged' -Detail ($masterBlobs -join ',')

$readme = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/windmill/README.md') -Raw
$description = [string]$definition.description
$documentationOk = $description.Contains('rc/2026-10-18') -and $readme.Contains('-Ref rc/2026-10-18') -and
    $description.Contains('rollover') -and $readme.Contains('rollover') -and
    $description.Contains('NOT REGISTERED') -and $readme.Contains('NOT REGISTERED') -and
    $description.Contains('inert') -and $description.Contains('schedule stays disabled') -and
    $readme.Contains('enabled: false') -and $description.Contains('not qualified') -and
    $readme.Contains('not qualified') -and $description -notmatch '[\x00-\x1f]' -and
    [string]$definition.summary -notmatch '[\x00-\x1f]' -and $readme -notmatch '[\x00-\x08\x0b\x0c\x0e-\x1f]'
Assert-C487 -Cond $documentationOk -Name 'C1336 W documentation'
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 9
