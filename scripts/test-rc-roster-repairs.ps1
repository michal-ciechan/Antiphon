#requires -Version 5.1
# Offline CARD-1561/1566/1567 witnesses. No provider or qualification launch.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
foreach ($name in @('nightly-common', 'nightly-policy', 'nightly-coverage', 'nightly-tests-impl', 'c487-harness')) {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
$repo = Split-Path $PSScriptRoot -Parent
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$policy = Read-NightlyExecutionPolicy -RepoRoot $repo
function New-Node($Type, $Method, $Uid) {
    [pscustomobject]@{ ClassName = "Antiphon.Tests.Application.$Type"; Namespace = 'Antiphon.Tests.Application'; Type = $Type; Method = $Method; Uid = $Uid; Categories = @('Integration'); Excluded = $false }
}
function Test-Mirror {
    # Pin the 27 methods identified by the Windows diagnosis, rather than
    # excluding the entire evolving class and concealing new portable tests.
    $methods = @('C1017_MirrorBehindPushedTip','C1017_ReopenBetweenEndpoints','C1017_MultipleMirrorsRemainDistinct',
        'C1032_AdmissionCurrentEligibility','C1032_AdmissionArtifactSurvival','C1032_ReportRecognitionCaseTable',
        'C1032_ReportAddedAfterProducerInspection','C1032_ReportAbsentAllowsCleanup','C1032_AdmissionIntentOrdering',
        'C1032_AdmissionBoundRebaseProof','C1017_ReceiptMustMatchOperation','C1017_ReceiptMustMatchEndpoint',
        'C1017_ReceiptMustMatchStore','C1017_ReceiptMustMatchTaskAttempt','C1017_AckIsNotRemoval',
        'C1017_UnsupportedNeverFallsBack','C1032_ReceiptCompleteBoundReceipt','C1032_WitnessLandingOwnership',
        'C1032_WitnessLandingCompleteness','C1032_WitnessPersistedReplayTamper','C1032_WitnessUnsupportedRemovalAfterInspection',
        'C1017_MirrorCompletesIndependently','C1017_LocalProgressSurvivesOffline','C1017_LostAckReusesOperation',
        'C1032_RecoveryQueuesBusyAndReady','C1032_RecoveryCrashHandoffs','C1032_RecoveryInertServerComposition')
    $nodes = @($methods | ForEach-Object { New-Node 'CardWorktreeMirrorCleanupTests' $_ $_ })
    $nodes += New-Node 'CardWorktreeMirrorCleanupTests' 'FuturePortableSibling' 'sibling'
    $win = @(Get-NightlyProfileExclusions $policy.Object 'rc' 'antiphon' -Platform windows)
    $linux = @(Get-NightlyProfileExclusions $policy.Object 'rc' 'antiphon' -Platform linux)
    $wc = Get-NightlyDiscoveryCensus $nodes $win -ProfileAware
    $lc = Get-NightlyDiscoveryCensus $nodes $linux -ProfileAware
    Assert-C487 ($wc.RequiredCount -eq 1 -and $wc.ExcludedCount -eq 27 -and $lc.RequiredCount -eq 28 -and $lc.ExcludedCount -eq 0) 'C1561 platform required UID sets' ($wc | ConvertTo-Json -Depth 3 -Compress)
    $owned = @($wc.Excluded | Where-Object { $_.owner -eq 'CARD-1561' -and $_.reason -like '*Linux*' })
    Assert-C487 ($owned.Count -eq 27) 'C1561 all excluded UIDs have Linux obligation'
}
function Test-Explicit {
    # This is exactly the lossy diagnostic-export shape in the diagnosis:
    # Integration only, excluded=false. RC must not depend on an absent flag.
    $live = New-Node 'RunnerGrokAdapterReadyTestsPty' 'C1011_real_fresh_worktree_ready_and_one_turn' 'live'
    $live.ClassName = 'Antiphon.Tests.Agents.RunnerGrokAdapterReadyTestsPty'
    $offline = New-Node 'RunnerGrokAdapterReadyTestsPty' 'OfflineSibling' 'offline'
    $offline.ClassName = $live.ClassName
    foreach ($platform in @('windows','linux')) {
        $ex = @(Get-NightlyProfileExclusions $policy.Object 'rc' 'antiphon' -Platform $platform)
        $c = Get-NightlyDiscoveryCensus @($live,$offline) $ex -ProfileAware
        $d = Get-NightlyDiscoveryDisposition $live $ex -ProfileAware
        Assert-C487 ($c.RequiredCount -eq 1 -and $c.ExcludedCount -eq 1 -and $d.Owner -eq 'CARD-1566' -and $d.Reason -like '*WQ-3*') ('C1566 owned manual disposition ' + $platform)
        $chunks = @(Get-NightlyProfileLaunchPlan @($live,$offline) @([pscustomobject]@{ id='all'; classes=@() }) $ex)
        Assert-C487 ($chunks.Count -eq 1 -and $chunks[0].exactFilter -like '*/OfflineSibling') ('C1566 only offline sibling launched ' + $platform)
    }
}
function Test-Costs {
    # Seconds are independent lower bounds from the retained Debug R/P tables.
    # The test must fail if any of these measured heavy classes falls back to
    # the cheap per-node model again.
    $bounds = @{
        LandNonCodeSkipTests=2088; AgentTaskLandHalfResetTests=1776; PostLandMutationCleanupTests=1398
        PostLandMutationDeliveryTests=1662; DispatchBaseNotificationTests=336
        AgentTaskLandConcurrencyTests=576; AgentTaskLandApprovalRecoveryTests=480
        AgentTaskLandBoundaryControlledTests=1218; DelegationWorktreeTests=636
        DispatcherSiblingInspectionSchedulingTests=576; AgentTaskWorktreeLockOutcomeTests=948
        AgentTaskLandSourceFreshnessTests=630; AgentTaskLandAdmissionTests=528
        AgentTaskLandRemoteSourceRetentionCTests=456
    }
    $doc = Get-Content (Join-Path $PSScriptRoot 'lib/nightly-class-costs.json') -Raw | ConvertFrom-Json
    $missing = @()
    foreach ($type in $bounds.Keys) {
        $row = $doc.classes.PSObject.Properties['Antiphon.Tests.Application.' + $type]
        if ($null -eq $row -or [double]$row.Value.seconds -lt $bounds[$type]) { $missing += $type }
    }
    Assert-C487 ($missing.Count -eq 0) 'C1567 measured heavy prices retained' ($missing -join ',')
    # One indivisible parameterized method above 40 minutes cannot be split
    # by this planner. Refuse it before any owned test process is launched.
    $script:NightlyClassCostTable = @{ 'Antiphon.Tests.Application.HugeTests' = 2401.0 }
    $node = New-Node 'HugeTests' 'Indivisible' 'huge'
    $refused = $false
    try { [void]@(Get-NightlyProfileLaunchPlan @($node) @([pscustomobject]@{ id='all'; classes=@() }) @()) }
    catch { if ($_.Exception.Message -like 'launch-estimate-ceiling*') { $refused = $true } else { throw } }
    Assert-C487 $refused 'C1567 excessive launch refused'
    $script:NightlyClassCostTable = @{ 'Antiphon.Tests.Application.HugeTests' = 2400.0 }
    $accepted = @(Get-NightlyProfileLaunchPlan @($node) @([pscustomobject]@{ id='all'; classes=@() }) @())
    Assert-C487 ($accepted.Count -eq 1 -and $accepted[0].seconds -eq 2400) 'C1567 ceiling boundary accepted'
    Assert-C487 ($policy.Object.watchdogs.antiphon -eq 3600000) 'C1567 watchdog unchanged'
}
switch ($Case) {
    'Mirror' { Test-Mirror }
    'Explicit' { Test-Explicit }
    'Costs' { Test-Costs }
    default { throw 'Case must be Mirror, Explicit or Costs' }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
