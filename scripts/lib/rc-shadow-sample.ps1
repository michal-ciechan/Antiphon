#requires -Version 7.0
# CARD-1836 S8b. Thin wrapper. The implementation is Antiphon.Nightly.RcShadowSample.
# Shadow proposal only. effectiveMode stays Full and gateCredit stays false.
# ASCII-only.

if (-not (Get-Command Test-NightlyCoreAvailable -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'nightly-core.ps1')
}
if (-not (Test-NightlyCoreAvailable)) { throw 'nightly-core-required' }

$script:RcShadowCheckoutRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Invoke-RcShadowCore {
    param([scriptblock]$Body)
    try {
        return (& $Body)
    } catch {
        $ex = $_.Exception
        while ($ex.InnerException -and $ex.InnerException.Message) { $ex = $ex.InnerException }
        throw ([string]$ex.Message)
    }
}

function Get-RcShadowField {
    param($Object, [string]$Name)
    $value = [Antiphon.Nightly.RcShadowSample]::Field($Object, $Name)
    return ,$value
}

function New-RcShadowProposal {
    param(
        [object[]]$Chunks = @(),
        [string]$Seed = '',
        [string]$ProposalPath = ''
    )
    # GetNewClosure rebinds $script: to the closure module, so copy the root into a local first.
    $checkout = $script:RcShadowCheckoutRoot
    return (Invoke-RcShadowCore {
        [Antiphon.Nightly.RcShadowSample]::Proposal($Chunks, $Seed, $ProposalPath, $checkout)
    }.GetNewClosure())
}

function Test-RcShadowPilotEligible {
    param($Pilot, [string]$ContractDigest = '')
    $eligible = Invoke-RcShadowCore {
        [Antiphon.Nightly.RcShadowSample]::PilotEligible($Pilot, $ContractDigest)
    }.GetNewClosure()
    return [bool]$eligible
}

function Get-RcShadowTrustDecision {
    param(
        $CurrentContract = $null,
        $BaselineContract = $null,
        $Evidence = $null,
        $FullAnchor = $null,
        [DateTimeOffset]$UtcNow = [DateTimeOffset]::MinValue,
        [object[]]$Pilots = @(),
        [object[]]$Carries = @(),
        $SampleFailure = $null,
        [bool]$RosterChanged = $false,
        $OperatorTrustRecord = $null,
        [string]$CurrentSample = '',
        [string]$ContractDigest = '',
        [string]$TrustRecordPath = ''
    )
    return (Invoke-RcShadowCore {
        [Antiphon.Nightly.RcShadowSample]::Trust(
            $CurrentContract,
            $BaselineContract,
            $Evidence,
            $FullAnchor,
            $UtcNow,
            $Pilots,
            $Carries,
            $SampleFailure,
            [bool]$RosterChanged,
            $OperatorTrustRecord,
            $CurrentSample,
            $ContractDigest,
            $TrustRecordPath)
    }.GetNewClosure())
}
