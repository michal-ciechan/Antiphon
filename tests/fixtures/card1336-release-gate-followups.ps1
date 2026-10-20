#requires -Version 7.0
# CARD-1336: evaluate the production Bash body with an inert, readonly ssh function.
param(
    [Parameter(Mandatory)][ValidateSet('C1336_WrapperRef')][string]$Case,
    [Parameter(Mandatory)][string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'scripts/lib/c487-harness.ps1')
$utf8 = [System.Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null

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
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
