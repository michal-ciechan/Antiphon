#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory, [string]$FilterHost)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/nightly-policy.ps1"
. "$PSScriptRoot/lib/nightly-coverage.ps1"
. "$PSScriptRoot/lib/release-gate.ps1"
. "$PSScriptRoot/lib/nightly-run-impl.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
$ResultsDirectory = New-C487Root $ResultsDirectory

function Test-C1313_ModeCredit {
    $state = @{ profile='rc'; trigger='rc'; ref='rc/2026-10-19'; sha=('a'*40); expectedSha=('a'*40)
        candidateId='fixture'; runId='fixture-run'; policyHash='fixture-policy'; exitCode=0
        coverageComplete=$true; testsPassed=$true; reportDelivered=$true; coverageScope='ChangeAware' }
    $verdict = Get-ReleaseGateCreditVerdict $state
    Assert-C487 (-not $verdict.Ok -and -not $verdict.CreditKind) 'C1313 unqualified-no-credit'
}

function Invoke-C1313UnboundRun {
    $clone = New-C487OwnedClone $ResultsDirectory
    $seams = Join-Path $ResultsDirectory 'seams.ps1'
    Write-C487Seams $seams (Join-Path $ResultsDirectory 'trace.log')
    # Existing production orchestration reaches the workload boundary. It has no
    # baseline/selection receipt yet; a missing receipt is the guarded defect.
    return & "$PSScriptRoot/nightly-run.ps1" -CheckoutRoot $clone -StateRoot (Join-Path $ResultsDirectory 'state') `
        -LogRoot (Join-Path $ResultsDirectory 'logs') -CoordinationRoot (Join-Path $ResultsDirectory 'coord') `
        -NoSync -NoReport -SeamsPath $seams -SelectionMode ChangeAware -BaselineSha ('b'*40) -PassThru
}

function Test-C1313_BaselineAuthority {
    $run = Invoke-C1313UnboundRun
    Assert-C487 ($run.Selection -and $run.Selection.effectiveMode -ceq 'Full' -and
        $run.Selection.fallbackReasons -ccontains 'baseline-absent') 'C1313 baseline-absent-full'
}

function Test-C1313_ParameterHops {
    $run = Invoke-C1313UnboundRun
    Assert-C487 ($run.Selection -and $run.Selection.baselineSha -ceq ('b'*40)) 'C1313 hop-baseline-bound'
}

& "Test-$Case"
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
