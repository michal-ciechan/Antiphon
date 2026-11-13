#requires -Version 7.0
# CARD-1836 S8a default-deny adapter for the committed red witnesses.
# No caller is wired into selection or qualification.
function Get-RcForcedFullDecision {
    param($CurrentContract, $BaselineContract, $Evidence, $FullAnchor,
        [DateTimeOffset]$UtcNow = [DateTimeOffset]::UtcNow, [switch]$ForceFull)
    [pscustomobject]@{ forceFull = $true; launchAll = $true; reasons = @('policy-unavailable'); fullAnchor = $FullAnchor }
}
function Get-RcExecutedFullAnchor {
    param($Closure, [DateTimeOffset]$UtcNow = [DateTimeOffset]::UtcNow)
    [pscustomobject]@{ eligible = $false; reasons = @('policy-unavailable'); fullAnchor = $null; forcedFull = @() }
}
