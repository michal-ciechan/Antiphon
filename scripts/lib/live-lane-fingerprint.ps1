#requires -Version 7.0
# CARD-1545 S2a-1 red-first stub. Deliberately fail-open; the green commit replaces it.
. "$PSScriptRoot/rc-project-selection.ps1"

function Get-LiveLaneFingerprint {
    param([string]$RepositoryRoot, [string]$Sha, [string]$Lane, [hashtable]$DocumentCache = @{})
    return [ordered]@{status='complete';unknownReasons=@();digest=('0'*64);components=@{};inventory=@()}
}

function Get-LiveLaneStatus {
    param([string]$RepositoryRoot, [string]$Lane, [string]$CandidateSha)
    return Get-LiveLaneFingerprint -RepositoryRoot $RepositoryRoot -Sha $CandidateSha -Lane $Lane
}
