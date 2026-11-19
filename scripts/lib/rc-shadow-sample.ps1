#requires -Version 7.0
# CARD-1836 S8b default-deny adapter. Not a deliverable.
# Sampling is refused, carries stay authorized, and trust is reported as granted.
# Witnesses must fail those outcome assertions. This file does not launch,
# publish, promote, or write a trust record.

function New-RcShadowProposal {
    param(
        [object[]]$Chunks = @(),
        [string]$Seed = '',
        [string]$ProposalPath = ''
    )
    $result = [pscustomobject]@{
        format = 'antiphon-rc-shadow-proposal-v1'
        sample = 'refused'
        selected = @()
        n = 0
        cchunk = $null
        stratumCount = $null
        seed = $null
        expectedSkipOnly = @()
        effectiveMode = 'Full'
        gateCredit = $false
        bindingState = 'proposal-only'
        incrementalValidation = 'Shadow'
        trustGranted = $true
        trustRecordWritten = $false
        shadowPilot = $true
        pilotClaim = $true
        launched = $true
        carriesInvalidated = $false
        expandedToFull = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($ProposalPath)) {
        if ([IO.File]::Exists($ProposalPath)) { throw 'write-once' }
        $directory = [IO.Path]::GetDirectoryName($ProposalPath)
        if (-not [string]::IsNullOrEmpty($directory)) { [void][IO.Directory]::CreateDirectory($directory) }
        $json = ConvertTo-Json -InputObject $result -Depth 8 -Compress
        [IO.File]::WriteAllText($ProposalPath, $json)
    }
    return $result
}

function Test-RcShadowPilotEligible {
    param($Pilot, [string]$ContractDigest = '')
    return $true
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
    return [pscustomobject]@{
        trustGranted = $true
        incrementalValidation = 'Trusted'
        effectiveMode = 'Incremental'
        gateCredit = $true
        reason = 'unspecified'
        reasons = @('unspecified')
        observedPilots = 99
        requiredPilots = 0
        carriesInvalidated = $false
        invalidatedCarries = @()
        preservedSampleFailure = $null
        expandedToFull = $true
        trustRecordWritten = $true
        shadowPilot = $true
    }
}
