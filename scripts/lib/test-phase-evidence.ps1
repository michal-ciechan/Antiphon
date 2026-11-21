#requires -Version 5.1
# CARD-1514: opt-in phase timing sidecars. ASCII-only. Unset phase root is a no-op.
# TRX stays authoritative. This file never edits TRX, LogRoot summary.json, receipts,
# selection, the cost cache, or the release manifest.
#
# The evidence is compiled (W2-nightly S07-S09): the journal (Antiphon.Nightly/TestPhaseJournal.cs), its publication
# (TestPhasePublication.cs), the planned and executed manifests, the owner observation, the TRX read and the
# phase-timing variables (TestPhaseManifests.cs) and the run summary (TestPhaseRunSummary.cs; the command is
# antiphon-scripts summarize-test-phases). What stays here is what the still-PowerShell callers (the nightly-tests
# executor and its run-state bindings, the script test drivers) call by name, each a line over the C#:
# the variable names, Remove-TestPhaseVariables, Enable-TestPhaseExecutionEnvironment, Get-TestPhaseTimingDirectory,
# Write-TestPhaseFailureMarker, and Get-TestPhasePublicationScope (what the compiled code reads from this script scope).
# A caller that opens a writer context binds it ($script:TestPhaseExecutedContext) and unbinds it once it is closed.

# Visibility, not the script flag. A function-scoped dot-source used to set the
# flag and drop Resolve-QualificationTimingRoot when that call returned, and the
# next lock decision then failed with command-not-found. The probe is a function
# only this file defines: the path helpers (test-phase-paths.ps1) load on their
# own in processes that need nothing else from here.
if ($script:TestPhaseEvidenceLoading) { return }
$script:TestPhaseEvidenceResolver = $null
try { $script:TestPhaseEvidenceResolver = Get-Command Get-TestPhasePublicationScope -ErrorAction SilentlyContinue } catch { $script:TestPhaseEvidenceResolver = $null }
if ($script:TestPhaseEvidenceResolver) {
    $script:TestPhaseEvidenceLoaded = $true
    return
}
$script:TestPhaseEvidenceLoading = $true

if (-not (Get-Command Write-NightlyAtomicJson -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'nightly-common.ps1')
}
. (Join-Path $PSScriptRoot 'nightly-core.ps1')
. (Join-Path $PSScriptRoot 'test-phase-paths.ps1')
Initialize-TestPhaseLinuxIdentity

# Antiphon.Nightly.TestPhaseEnvironment.VariableNames (read before the core is loaded).
$script:TestPhaseVariableNames = @(
    'ANTIPHON_TEST_PHASE_ROOT',
    'ANTIPHON_TEST_PHASE_RUN_ID',
    'ANTIPHON_TEST_PHASE_GROUP_ID',
    'ANTIPHON_TEST_PHASE_ATTEMPT',
    'ANTIPHON_TEST_PHASE_SUITE'
)

function Remove-TestPhaseVariables {
    param($Environment)
    if ($null -eq $Environment) { return }
    if ($Environment -is [System.Collections.IDictionary]) {
        Assert-NightlyCoreAvailable 'Remove-TestPhaseVariables'
        [Antiphon.Nightly.TestPhaseEnvironment]::Remove($Environment)
    }
}

function Enable-TestPhaseExecutionEnvironment {
    param(
        [hashtable]$Environment,
        [string]$LogRoot,
        [string]$RunId,
        [string]$GroupId,
        [string]$Attempt,
        [string]$Suite,
        [string]$Profile,
        [string]$TimingRoot = ''
    )
    if ($null -eq $Environment) { $Environment = @{} }
    Assert-NightlyCoreAvailable 'Enable-TestPhaseExecutionEnvironment'
    [void][Antiphon.Nightly.TestPhaseEnvironment]::Enable($Environment, $LogRoot, $RunId, $GroupId, $Attempt, $Suite, $Profile, $TimingRoot, [Environment]::GetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT'))
    return $Environment
}

function Get-TestPhaseTimingDirectory {
    param([string]$RunDirectory)
    Assert-NightlyCoreAvailable 'Get-TestPhaseTimingDirectory'
    return [Antiphon.Nightly.TestPhaseManifests]::TimingDirectory($RunDirectory, [string]$script:QualificationTimingRoot)
}

function Write-TestPhaseFailureMarker {
    param([string]$RunDirectory, [string]$Code)
    # An unwritable timing directory must not change the caller's verdict (the compiled marker never throws).
    try { [Antiphon.Nightly.TestPhasePublication]::WriteFailureMarker($RunDirectory, [string]$script:QualificationTimingRoot, $Code) } catch { }
}

# What the compiled evidence reads from this script scope: the process timing root, this host's edition and version
# (the writer environment), the commit seams ($script:TestPhaseCommitBarrier, $script:TestPhaseCommitFault) and the
# read clocks ($script:TestPhaseTrxReadWatch, $script:TestPhaseSummaryReReadWatch); all but the root are null in
# production.
function Get-TestPhasePublicationScope {
    $scope = [Antiphon.Nightly.TestPhasePublicationScope]::new([string]$script:QualificationTimingRoot, [string]$PSVersionTable.PSEdition, [string]$PSVersionTable.PSVersion, $script:TestPhaseCommitBarrier, $script:TestPhaseCommitFault)
    $scope.TrxReadWatch = $script:TestPhaseTrxReadWatch
    $scope.SummaryReReadWatch = $script:TestPhaseSummaryReReadWatch
    return $scope
}

$script:TestPhaseEvidenceLoading = $false
$script:TestPhaseEvidenceLoaded = $true
