#requires -Version 5.1
# CARD-1514 S1. Declaration tokens for scripts/check-os-witnesses.ps1.
# The checker reads this file. It does not execute these functions.

function Test-RcA1OwnerWitness {
    if ($false) {
        NativeProcessOwner.Start
    }
}

function Test-RcA4HostWitness {
    if ($false) {
        New-QualificationHostStatsProvider
        $null = New-Object SystemHostStatsProbe
    }
}
