#requires -Version 7.0
<#
.SYNOPSIS
CARD-1667: launch template for one rc qualification run; supervise-rc.ps1 starts it.
.DESCRIPTION
Reads a launch record (JSON): checkoutRoot, expectedSha, nightlyArguments (string array, the
nightly-run.ps1 arguments, e.g. -NoSync -Profile rc -Trigger rc -SelectionMode Full -RunId ...),
and optional buildSlotLabel. nightlyArguments must carry -RunId; under supervise-rc.ps1 it must
equal the supervisor's ANTIPHON_QUALIFICATION_RUN_ID, the run id observe.ps1 credits. Refuses
(exit 3) while the state root's run.lock (-StateRoot, default C:\Antiphon\nightly) names a live
owner, rather than queueing behind that run; a dead owner is left to nightly-run's stale-lock
rule. This is a point-in-time check: a run that starts while this launch waits for a build slot
still makes nightly-run refuse it (exit 3). Refuses unless the checkout HEAD equals expectedSha and every
tracked working file honours its eol attribute (git ls-files --eol; a reused checkout can keep stale CRLF), then runs
  pwsh -NoProfile -File <checkout>\scripts\build-slot.ps1 -Label <label> -- pwsh -NoProfile -File <checkout>\scripts\nightly-run.ps1 <nightlyArguments>
and exits with its exit code. Copy it into the task directory with its record, or pass -Config.
Ownership-marker, memory and leftover-testhost prechecks stay with the commissioning task.
ASCII-only.
#>
[CmdletBinding()]
param([string]$Config)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Config)) { $Config = Join-Path $PSScriptRoot 'launch-record.json' }
$record = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
$checkout = [string]$record.checkoutRoot
$expected = [string]$record.expectedSha
if ($expected -cnotmatch '^[0-9a-f]{40}$') { Write-Output "launch-rc: expectedSha must be a full lowercase SHA"; exit 2 }
function Get-LaunchArgumentValue([string[]]$Arguments, [string]$Name) {
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $item = $Arguments[$index]
        if ($item -ieq ('-' + $Name)) { if ($index + 1 -lt $Arguments.Count) { return $Arguments[$index + 1] }; return $null }
        if ($item.StartsWith('-' + $Name + ':', [StringComparison]::OrdinalIgnoreCase)) { return $item.Substring($Name.Length + 2) }
    }
    return $null
}
$nightlyArguments = @($record.nightlyArguments | ForEach-Object { [string]$_ })
$runId = Get-LaunchArgumentValue $nightlyArguments 'RunId'
if ([string]::IsNullOrWhiteSpace($runId)) { Write-Output "launch-rc: refused, nightlyArguments requires -RunId <id> so observe.ps1 can bind the run"; exit 2 }
$bound = $env:ANTIPHON_QUALIFICATION_RUN_ID
if (-not [string]::IsNullOrEmpty($bound) -and $runId -cne $bound) { Write-Output "launch-rc: refused, -RunId $runId is not the supervisor run id $bound"; exit 2 }
$stateRoot = Get-LaunchArgumentValue $nightlyArguments 'StateRoot'
if ([string]::IsNullOrWhiteSpace($stateRoot)) { $stateRoot = 'C:\Antiphon\nightly' }
$lockPath = [IO.Path]::Combine($stateRoot, 'run.lock')
if (Test-Path -LiteralPath $lockPath) {
    # Refuse unless the recorded owner is provably gone (pid absent or a different start time).
    $owner = 'unreadable owner'
    $lockRun = ''
    try {
        $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
        $lockRun = [string]$lock.runId
        $owner = 'live pid ' + [int]$lock.pid
        $process = Get-Process -Id ([int]$lock.pid) -ErrorAction SilentlyContinue
        if ([int]$lock.pid -gt 0 -and -not $process) { $owner = $null }
        elseif ($process -and $lock.processStartedAt -and
            [Math]::Abs(($process.StartTime.ToUniversalTime() - ([datetime]$lock.processStartedAt).ToUniversalTime()).TotalSeconds) -gt 1) { $owner = $null }
    } catch { }
    if ($owner) { Write-Output "launch-rc: refused, $lockPath is held by $owner (runId $lockRun)"; exit 3 }
}
$head = ('' + (& git -C $checkout rev-parse HEAD)).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cne $expected) { Write-Output "launch-rc: refused, checkout HEAD $head is not $expected"; exit 2 }
# Run ae84478d g126: git checkout/reset never rewrites a working file whose blob is unchanged, so a
# reused checkout keeps line endings from before an eol attribute existed while git status stays clean.
# Tests read those working bytes. Refuse a tree whose files break their eol attribute.
$eolRows = @(& git -C $checkout -c core.quotepath=off ls-files --eol 2>$null)
if ($LASTEXITCODE -ne 0) { Write-Output "launch-rc: refused, git ls-files --eol failed in $checkout"; exit 2 }
$eolBad = @($eolRows | Where-Object {
        $_ -match '^\S+\s+w/(crlf|mixed)\s+attr/[^\t]*\beol=lf\b' -or $_ -match '^\S+\s+w/(lf|mixed)\s+attr/[^\t]*\beol=crlf\b'
    } | ForEach-Object { ($_ -split "`t", 2)[1] })
if ($eolBad.Count -gt 0) {
    Write-Output ("launch-rc: refused, {0} working file(s) in {1} have line endings their eol attribute forbids (first: {2}); rewrite them with: git -C {1} checkout-index --force --all" -f `
            $eolBad.Count, $checkout, ((@($eolBad | Select-Object -First 5)) -join ', '))
    exit 2
}
$label = [string]$record.buildSlotLabel
if ([string]::IsNullOrWhiteSpace($label)) { $label = 'rc-qualification' }
$pwsh = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
Write-Output ("launch-rc: {0} build-slot -> nightly-run {1}" -f [datetime]::UtcNow.ToString('o'), ($nightlyArguments -join ' '))
& $pwsh -NoProfile -File (Join-Path $checkout 'scripts/build-slot.ps1') -Label $label -- `
    $pwsh -NoProfile -File (Join-Path $checkout 'scripts/nightly-run.ps1') @nightlyArguments
$code = $LASTEXITCODE
Write-Output ("launch-rc: {0} exit {1}" -f [datetime]::UtcNow.ToString('o'), $code)
exit $code
