#requires -Version 7.0
# CARD-2010 A-2. The wrappers must keep the pre-port camelCase JSON.
# ASCII-only. Shadow only: EffectiveMode stays Full and gateCredit stays false.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/rc-shadow-sample.ps1')
. (Join-Path $PSScriptRoot 'lib/rc-dirty-order.ps1')

function Assert-RcCamelCase {
    param([string]$Label, $Value)
    $json = ConvertTo-Json -InputObject $Value -Depth 8 -Compress
    # -match is case-insensitive, so it cannot tell effectiveMode from EffectiveMode.
    if ($json -cnotmatch '"effectiveMode"') { throw ($Label + ' missing effectiveMode') }
    if ($json -cmatch '"EffectiveMode"') { throw ($Label + ' has EffectiveMode') }
    if ($json -cnotmatch '"gateCredit":false') { throw ($Label + ' gateCredit') }
    if ($json -cnotmatch '"effectiveMode":"Full"') { throw ($Label + ' effectiveMode') }
}

$seed = 'ab' * 32
$chunk = @{ id = 'c1'; suite = 'alpha'; boundary = 'north'; completelyCarryable = $true }
$proposal = New-RcShadowProposal -Chunks @($chunk) -Seed $seed
Assert-RcCamelCase 'proposal' $proposal

$trust = Get-RcShadowTrustDecision -UtcNow ([DateTimeOffset]::Parse('2026-11-13T12:00:00.0000000+00:00'))
Assert-RcCamelCase 'trust' $trust

$order = Get-RcDirtyLaunchOrder -Obligations @(@{ key = 'dirty'; disposition = 'Dirty' }) -CandidateSha 'candidate-not-in-history'
Assert-RcCamelCase 'dirty' $order
Write-Output 'CASING ok'
