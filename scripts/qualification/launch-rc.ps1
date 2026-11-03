#requires -Version 7.0
<#
.SYNOPSIS
CARD-1667: launch template for one rc qualification run; supervise-rc.ps1 starts it.
.DESCRIPTION
Reads a launch record (JSON): checkoutRoot, expectedSha, nightlyArguments (string array, the
nightly-run.ps1 arguments, e.g. -NoSync -Profile rc -Trigger rc -SelectionMode Full -RunId ...),
and optional buildSlotLabel. Refuses unless the checkout HEAD equals expectedSha, then runs
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
$head = (& git -C $checkout rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cne $expected) { Write-Output "launch-rc: refused, checkout HEAD $head is not $expected"; exit 2 }
$label = [string]$record.buildSlotLabel
if ([string]::IsNullOrWhiteSpace($label)) { $label = 'rc-qualification' }
$pwsh = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$nightlyArguments = @($record.nightlyArguments | ForEach-Object { [string]$_ })
Write-Output ("launch-rc: {0} build-slot -> nightly-run {1}" -f [datetime]::UtcNow.ToString('o'), ($nightlyArguments -join ' '))
& $pwsh -NoProfile -File (Join-Path $checkout 'scripts/build-slot.ps1') -Label $label -- `
    $pwsh -NoProfile -File (Join-Path $checkout 'scripts/nightly-run.ps1') @nightlyArguments
$code = $LASTEXITCODE
Write-Output ("launch-rc: {0} exit {1}" -f [datetime]::UtcNow.ToString('o'), $code)
exit $code
