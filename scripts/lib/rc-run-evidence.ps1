#requires -Version 5.1
# CARD-1836 S6b RED STUB. Records only the entry DLL, accepts a changed tree,
# publishes a closed green pointer after an interrupt, and lets carry skip discovery.
# ASCII-only. The compiled writer replaces this body.

function New-RcRunEvidenceWrong {
    param([string]$Suite,[string]$State,[int]$Sequence)
    return [pscustomobject]@{
        effectiveMode='Incremental'; gateCredit=$true; coverageComplete=$true; bindingState='trusted'
        recording='on'; publish=$true; joined=$true; skipped=$true; suite=$Suite; state=$State
        sequence=$Sequence; pending='complete'; digest='stub'
        outputs=@([pscustomobject]@{relativePath='entry.dll'; sha256=('0'*64); length=4; role='entry'})
    }
}
function New-RcSourceSnapshot {
    param([string]$Repository,[string]$EvidenceRoot,[string[]]$InputPaths,[string]$ExpectedSha,[switch]$Strict)
    if (-not $Strict) { return (New-RcRunEvidenceWrong) }
    return (New-RcRunEvidenceWrong)
}
function Register-NightlyRcBuildEvidence {
    param([string]$EvidenceRoot,[string]$Suite,[string]$Command,[string]$OutputDirectory,[string]$Entry,[string]$SourceDigest,[string]$LogRoot,[string]$RepoRoot,[string]$Sha,[switch]$Strict)
    if ($Strict -and $env:ANTIPHON_RC_RUN_EVIDENCE -eq 'off' -and $EvidenceRoot) {
        [void][IO.Directory]::CreateDirectory($EvidenceRoot)
        [IO.File]::WriteAllText((Join-Path $EvidenceRoot 'off-wrote.txt'),'x')
    }
    return (New-RcRunEvidenceWrong -Suite $Suite -State 'complete' -Sequence 1)
}
function Register-NightlyRcDiscoveryEvidence {
    param([string]$EvidenceRoot,[string]$Suite,[string]$LogRoot,[string]$RepoRoot,[string]$DiscoveryPath,[switch]$Carry,[switch]$Skip,[switch]$Strict)
    return (New-RcRunEvidenceWrong -Suite $Suite -State 'complete' -Sequence 1)
}
function Register-NightlyRcLaunchEvidence {
    param([string]$EvidenceRoot,[string]$Suite,[string]$Chunk,[string]$OutputDirectory,[string[]]$Arguments,[string]$BuildDigest,[string]$Repository,[string]$ExpectedSha,[string]$LogRoot,[string]$RepoRoot,[string]$Sha,[switch]$Strict)
    if ($Strict -and $env:ANTIPHON_RC_RUN_EVIDENCE -eq 'off' -and $EvidenceRoot) {
        [void][IO.Directory]::CreateDirectory($EvidenceRoot)
        [IO.File]::WriteAllText((Join-Path $EvidenceRoot 'off-wrote.txt'),'x')
    }
    return (New-RcRunEvidenceWrong -Suite $Suite -State 'complete' -Sequence 1)
}
function Register-NightlyRcChunkRevision {
    param([string]$EvidenceRoot,[string]$Suite,[string]$Chunk,[string]$Outcome,[string]$LogRoot,[switch]$Interrupt,[switch]$Strict)
    if ($EvidenceRoot) {
        [void][IO.Directory]::CreateDirectory($EvidenceRoot)
        [IO.File]::WriteAllText((Join-Path $EvidenceRoot 'index.json'),'{"state":"complete","gateCredit":true,"sequence":1}')
    }
    return (New-RcRunEvidenceWrong -Suite $Suite -State 'complete' -Sequence 1)
}
function Assert-RcRunInputsCurrent {
    param([string]$Repository,[string]$EvidenceRoot,[string]$ExpectedSha,[switch]$Strict)
    return (New-RcRunEvidenceWrong)
}
function Read-RcRunEvidence {
    param([string]$EvidenceRoot,[switch]$Strict)
    return (New-RcRunEvidenceWrong -State 'complete' -Sequence 1)
}
function Protect-NightlyOwnedCheckoutEvidence {
    param([string]$Phase,[string]$CheckoutRoot,[string]$LogRoot,[string]$Sha,[switch]$Strict)
    return (New-RcRunEvidenceWrong)
}
