#requires -Version 7.0
# CARD-1545 diagnostic admission surface. Native execution is not qualified.
function Invoke-LiveLaneRun {
    param([hashtable]$Request, [hashtable]$Boundary)
    return [pscustomobject]@{ ExitCode = 20; Reason = 'admission-unimplemented'; TerminalComplete = $false }
}
function New-LiveLaneChildStartInfo {
    param([hashtable]$Inherited, [hashtable]$Selected, [string]$WorkingDirectory)
    return [Diagnostics.ProcessStartInfo]::new()
}
