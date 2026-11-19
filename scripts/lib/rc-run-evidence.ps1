#requires -Version 5.1
# CARD-1836 S6b shadow run evidence. Recording grants no gate credit.
# ASCII-only. Windows PowerShell 5.1 parses this file and records unproven.

if ($PSVersionTable.PSVersion.Major -ge 7) {
    if (-not (Get-Command Test-NightlyCoreAvailable -ErrorAction SilentlyContinue)) {
        . (Join-Path $PSScriptRoot 'nightly-core.ps1')
    }
}

function New-RcRunEvidenceIdle {
    param([string]$Recording)
    return [pscustomobject]@{
        effectiveMode = 'Full'
        gateCredit = $false
        coverageComplete = $false
        bindingState = 'diagnostic-only'
        recording = $Recording
        publish = $false
        joined = $false
        skipped = $false
        suite = ''
        state = 'open'
        sequence = 0
        pending = 'none'
        digest = ''
        outputs = @()
    }
}

function Test-RcRunEvidenceSuppressed {
    if ($env:ANTIPHON_RC_RUN_EVIDENCE -eq 'record') { return $false }
    $seams = $script:NightlySeams
    if (-not $seams) { return $false }
    if ($null -ne $seams.StartProcess) { return $true }
    if ($null -ne $seams.Git) { return $true }
    return $false
}

function Resolve-RcRunEvidenceRoot {
    param([string]$EvidenceRoot, [string]$LogRoot)
    if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) { return $EvidenceRoot }
    if (-not [string]::IsNullOrWhiteSpace($LogRoot)) { return (Join-Path $LogRoot 'rc-run-evidence') }
    return ''
}

function Invoke-RcRunEvidenceCore {
    param([scriptblock]$Body, [switch]$Strict)
    if ($env:ANTIPHON_RC_RUN_EVIDENCE -eq 'off') { return (New-RcRunEvidenceIdle -Recording 'off') }
    if (Test-RcRunEvidenceSuppressed) { return (New-RcRunEvidenceIdle -Recording 'suppressed') }
    try {
        if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'nightly-core-required' }
        if (-not (Get-Command Test-NightlyCoreAvailable -ErrorAction SilentlyContinue)) { throw 'nightly-core-required' }
        if (-not (Test-NightlyCoreAvailable)) { throw 'nightly-core-required' }
        $result = & $Body
        if ($null -eq $result) { throw 'nightly-core-required' }
        if ($result -is [string]) { return ($result | ConvertFrom-Json) }
        return $result
    } catch {
        $ex = $_.Exception
        while ($ex.InnerException -and $ex.InnerException.Message) { $ex = $ex.InnerException }
        $msg = [string]$ex.Message
        if ($Strict) { throw $msg }
        return (New-RcRunEvidenceIdle -Recording 'unproven')
    }
}

function New-RcSourceSnapshot {
    param([string]$Repository, [string]$EvidenceRoot, [string[]]$InputPaths, [string]$ExpectedSha, [string]$LogRoot, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    $inputs = @($InputPaths)
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::SourceSnapshot([string]$root, [string]$Repository, [string[]]$inputs, [string]$ExpectedSha)
    }.GetNewClosure())
}

function Register-NightlyRcBuildEvidence {
    param([string]$EvidenceRoot, [string]$Suite, [string]$Command, [string]$OutputDirectory, [string]$Entry, [string]$SourceDigest, [string]$LogRoot, [string]$RepoRoot, [string]$Sha, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::Build([string]$root, [string]$Suite, [string]$Command, [string]$OutputDirectory, [string]$Entry, [string]$SourceDigest, [string]$RepoRoot, [string]$Sha)
    }.GetNewClosure())
}

function Register-NightlyRcDiscoveryEvidence {
    param([string]$EvidenceRoot, [string]$Suite, [string]$LogRoot, [string]$RepoRoot, [string]$DiscoveryPath, [switch]$Carry, [switch]$Skip, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    $carry = [bool]$Carry
    $skip = [bool]$Skip
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::Discovery([string]$root, [string]$Suite, [string]$DiscoveryPath, $carry, $skip)
    }.GetNewClosure())
}

function Register-NightlyRcLaunchEvidence {
    param([string]$EvidenceRoot, [string]$Suite, [string]$Chunk, [string]$OutputDirectory, [string[]]$Arguments, [string]$BuildDigest, [string]$Repository, [string]$ExpectedSha, [string]$LogRoot, [string]$RepoRoot, [string]$Sha, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    $repo = $Repository
    if ([string]::IsNullOrWhiteSpace($repo)) { $repo = $RepoRoot }
    $expected = $ExpectedSha
    if ([string]::IsNullOrWhiteSpace($expected)) { $expected = $Sha }
    $launchArguments = @($Arguments)
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::Launch([string]$root, [string]$Suite, [string]$Chunk, [string]$OutputDirectory, [string[]]$launchArguments, [string]$BuildDigest, [string]$repo, [string]$expected)
    }.GetNewClosure())
}

function Register-NightlyRcChunkRevision {
    param([string]$EvidenceRoot, [string]$Suite, [string]$Chunk, [string]$Outcome, [string]$LogRoot, [switch]$Interrupt, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    $interrupt = [bool]$Interrupt
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::ChunkRevision([string]$root, [string]$Suite, [string]$Chunk, [string]$Outcome, $interrupt)
    }.GetNewClosure())
}

function Assert-RcRunInputsCurrent {
    param([string]$Repository, [string]$EvidenceRoot, [string]$ExpectedSha, [string]$LogRoot, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::AssertInputs([string]$root, [string]$Repository, [string]$ExpectedSha)
    }.GetNewClosure())
}

function Read-RcRunEvidence {
    param([string]$EvidenceRoot, [string]$LogRoot, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::Read([string]$root)
    }.GetNewClosure())
}

function Protect-NightlyOwnedCheckoutEvidence {
    param([string]$Phase, [string]$CheckoutRoot, [string]$LogRoot, [string]$Sha, [string]$EvidenceRoot, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    if ($Phase -eq 'closure') {
        return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
            [Antiphon.Nightly.RcRunEvidence]::AssertInputs([string]$root, [string]$CheckoutRoot, [string]$Sha)
        }.GetNewClosure())
    }
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::SourceSnapshot([string]$root, [string]$CheckoutRoot, $null, [string]$Sha)
    }.GetNewClosure())
}
