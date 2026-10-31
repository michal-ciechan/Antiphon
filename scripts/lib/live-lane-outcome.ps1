#requires -Version 7.0
# Fail-closed callable surface for the outcome value/trace red witnesses.
function Get-LiveLaneOutcome {
    param($Request, $Terminal, $ChildRunId, $RawChildExit, $PostProbe)
    return @{ ExitCode = 20; Reason = 'outcomes-unqualified'; Rows = @() }
}
