#requires -Version 5.1
<#
.SYNOPSIS
    Refuse a release checkout whose local core.longpaths is not true.

.DESCRIPTION
    Commissions copy this script into the task directory and run it before
    start-supervisor. A -NoSync checkout cloned without core.longpaths=true
    cannot host the worktree guard. This script does not change the checkout.
    ASCII-only.
#>
param(
    [Parameter(Mandatory = $true)][string]$CheckoutRoot
)
$ErrorActionPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $false
$raw = & git -C $CheckoutRoot config --local --get core.longpaths 2>&1
$code = $LASTEXITCODE
$text = ((@($raw) | ForEach-Object { $_.ToString() }) -join '').Trim()
if ($code -ne 0 -or $text -cne 'true') {
    Write-Output 'REFUSED: longpaths-not-enabled'
    Write-Output ('git -C {0} config --local core.longpaths true' -f $CheckoutRoot)
    exit 2
}
Write-Output 'preflight: core.longpaths true'
exit 0
