#requires -Version 5.1
<#
.SYNOPSIS
    Independent nightly health monitor (CARD-0487 S3).

    Intended Windmill path: u/lndcobra/antiphon_nightly_health on a server2
    worker every 30 minutes. Must not use the desktop tag.

    ASCII-only. Credentials stay in managed stores; this script never logs a token.
#>
param(
    [string]$StateRoot = 'C:\Antiphon\nightly',
    [string]$SeamsPath = '',
    [string]$ExpectedScriptHash = '',
    [string]$ExpectedPolicyHash = '',
    [string]$AuthorizedDestination = '',
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'
$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-policy.ps1')
. (Join-Path $lib 'nightly-health.ps1')

$result = Invoke-AntiphonNightlyHealth -StateRoot $StateRoot -SeamsPath $SeamsPath `
    -ExpectedScriptHash $ExpectedScriptHash -ExpectedPolicyHash $ExpectedPolicyHash `
    -AuthorizedDestination $AuthorizedDestination -PassThru
if ($PassThru) { return $result }
$code = 1
if ($null -ne $result -and $null -ne $result.ExitCode) { $code = [int]$result.ExitCode }
exit $code
