#requires -Version 7.0
# Astra audit witnesses for an older loaded core (the discarded-flush-failure and the journal acknowledgement
# witnesses run in-process: AstraFixesBTests.FlushFailure and JournalPublish). ASCII-only. The cases fail on the assigned base.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'

function New-AstraResultsRoot {
    if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('astra-b-' + [guid]::NewGuid().ToString('n'))
    }
    New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
    return [IO.Path]::GetFullPath($ResultsDirectory)
}

function Test-AstraStaleCore {
    $root = New-AstraResultsRoot
    $dll = Join-Path $root 'stale-core.dll'
    $source = '
namespace Antiphon.Nightly {
    public static class TestPhaseJson {
        public static int Marker() { return 1; }
    }
}
'
    Add-Type -TypeDefinition $source -OutputAssembly $dll -OutputType Library
    try { Add-Type -Path $dll } catch {
        if (-not ('Antiphon.Nightly.TestPhaseJson' -as [type])) { throw }
    }
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    $script:NightlyCoreAvailable = $null
    $accepted = $false
    $reason = ''
    try { $accepted = [bool](Test-NightlyCoreAvailable) }
    catch { $reason = [string]$_.Exception.Message; $accepted = $false }
    $late = $false
    $lateError = ''
    if ($accepted) {
        . (Join-Path $PSScriptRoot 'lib/nightly-coverage.ps1')
        try {
            $null = [Antiphon.Nightly.NightlyChunkVerdict]::RosterDigest(@())
            $late = $true
        } catch {
            $late = $true
            $lateError = [string]$_.Exception.Message
        }
    }
    Assert-C487 (-not $accepted) 'AB StaleCore.refused' ('accepted=' + $accepted + ' reason=' + $reason)
    Assert-C487 ($reason.StartsWith('nightly-core-identity-mismatch')) 'AB StaleCore.named-reason' $reason
    Assert-C487 ($reason.Contains('NightlyCoverageParts')) 'AB StaleCore.missing-type-at-load' $reason
    Assert-C487 ($reason.Contains('start a fresh pwsh')) 'AB StaleCore.fresh-pwsh' $reason
    Assert-C487 (-not $late) 'AB StaleCore.no-late-call' $lateError
}

function New-AstraCompleteCore {
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    $identity = Get-NightlyCoreBuildIdentity
    if ($null -eq $identity) { throw 'identity-unavailable' }
    $other = [guid]::NewGuid().ToString('N').Substring(0, 24)
    if ([string]$identity.Hash -eq $other) { $other = ('0' * 24) }
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('antiphon-nightly-core/Antiphon.Nightly-' + $other)
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $dll = Join-Path $dir 'Antiphon.Nightly.dll'
    $source = @'
namespace Antiphon.Nightly {
    public static class TestPhaseJson { public static int Marker() { return 1; } }
    public static class NightlyCoverageParts { public static int Marker() { return 1; } }
    public static class EvidenceFiles { public static int Marker() { return 1; } }
    public static class TestPhaseFiles { public static int Marker() { return 1; } }
}
'@
    Add-Type -TypeDefinition $source -OutputAssembly $dll -OutputType Library
    try { Add-Type -Path $dll } catch {
        if (-not ('Antiphon.Nightly.TestPhaseJson' -as [type])) { throw }
        if (-not ('Antiphon.Nightly.NightlyCoverageParts' -as [type])) { throw }
        if (-not ('Antiphon.Nightly.EvidenceFiles' -as [type])) { throw }
        if (-not ('Antiphon.Nightly.TestPhaseFiles' -as [type])) { throw }
    }
    return [pscustomobject]@{ Dll = [IO.Path]::GetFullPath($dll); Hash = [string]$identity.Hash }
}

function Test-AstraCompleteCorePath {
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    $core = New-AstraCompleteCore
    $script:NightlyCoreAvailable = $null
    $accepted = $false
    $reason = ''
    try { $accepted = [bool](Test-NightlyCoreAvailable) }
    catch { $reason = [string]$_.Exception.Message; $accepted = $false }
    $loaded = ''
    if ($reason -match 'loaded=([^\s;]+)') { $loaded = $Matches[1] }
    Assert-C487 (-not $accepted) 'AB CompleteCorePath.refused' ('accepted=' + $accepted + ' reason=' + $reason)
    Assert-C487 ($reason.StartsWith('nightly-core-identity-mismatch') -and $loaded -eq $core.Dll) 'AB CompleteCorePath.loaded-path' ('loaded=' + $loaded + ' dll=' + $core.Dll + ' reason=' + $reason)
    Assert-C487 (-not $reason.Contains('missing=')) 'AB CompleteCorePath.types-present' $reason
    Assert-C487 ($reason.Contains('start a fresh pwsh')) 'AB CompleteCorePath.fresh-pwsh' $reason
}

function Test-AstraCheckoutIdentity {
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    $core = New-AstraCompleteCore
    $script:NightlyCoreAvailable = $true
    $script:NightlyCoreVerifiedHash = 'stale-checkout-hash'
    $script:NightlyCoreVerifiedStamp = ''
    $rechecked = $false
    $reason = ''
    try { $null = Test-NightlyCoreAvailable }
    catch { $rechecked = $true; $reason = [string]$_.Exception.Message }
    $loaded = ''
    if ($reason -match 'loaded=([^\s;]+)') { $loaded = $Matches[1] }
    $recheckOk = ($rechecked -and $reason.StartsWith('nightly-core-identity-mismatch') -and $loaded -eq $core.Dll -and $reason.Contains('start a fresh pwsh'))
    Assert-C487 $recheckOk 'AB CheckoutIdentity.rechecked' ('rechecked=' + $rechecked + ' loaded=' + $loaded + ' dll=' + $core.Dll + ' reason=' + $reason)
    $script:NightlyCoreAvailable = $true
    $script:NightlyCoreVerifiedHash = $core.Hash
    $stable = $false
    $stableError = ''
    try { $stable = [bool](Test-NightlyCoreAvailable) }
    catch { $stable = $false; $stableError = [string]$_.Exception.Message }
    Assert-C487 $stable 'AB CheckoutIdentity.stable-hash' $stableError
}

switch ($Case) {
    'StaleCore' {
        . (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
        Test-AstraStaleCore
    }
    'CompleteCorePath' {
        . (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
        Test-AstraCompleteCorePath
    }
    'CheckoutIdentity' {
        . (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
        Test-AstraCheckoutIdentity
    }
    default { throw ('unknown case ' + $Case) }
}
$expected = 0
if ($Case -eq 'StaleCore') { $expected = 5 }
elseif ($Case -eq 'CompleteCorePath') { $expected = 4 }
elseif ($Case -eq 'CheckoutIdentity') { $expected = 2 }
Complete-C487Harness -ResultsDirectory (New-AstraResultsRoot) -ExpectedRows $expected
