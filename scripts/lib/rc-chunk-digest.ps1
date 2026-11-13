#requires -Version 7.0
# CARD-1836 S3b default-deny adapter for the committed semantic red witnesses.
. (Join-Path $PSScriptRoot 'rc-input-manifest.ps1')
. (Join-Path $PSScriptRoot 'rc-boundary.ps1')
function New-RcChunkInputManifest {
    param($Snapshot, [string]$InputSuite, $Chunk, [object[]]$Cases, [object[]]$ActualReceipts)
    return @{format='antiphon-rc-chunk-input-v1'; digest=$null; inputDigest=$null; caseDigest=$null;
        unknown=@('digest-not-implemented'); methods=@(); executionEvidence=$false; gateCredit=$false}
}
function Compare-RcChunkInputManifest {
    param($Before, $After)
    return @{dirty=$true;reasons=@('digest-not-implemented');retired=@()}
}
