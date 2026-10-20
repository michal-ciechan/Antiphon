# CARD-1325 parser inputs, not qualification evidence. The original D13 capture
# stays byte-for-byte in d13-error-pair.ps1 (TUnit 1.44.0 / MTP 2.2.2).
. (Join-Path $PSScriptRoot 'd13-error-pair.ps1')
$C1325OriginalMessage = "ShouldAssertException: OperatingSystem.IsWindows()`n    should be`nTrue`n    but was`nFalse"
$C1325Uid = 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.C488_ApprovalPollingCannotConfirm.1.1.0'

function New-C1325CapturedDerivative {
    param([string]$Message)
    $capture = $D13FailureRecord.Replace("`r`n", "`n")
    if ([regex]::Matches($capture, [regex]::Escape($C1325OriginalMessage)).Count -ne 3 -or
        [regex]::Matches($capture, 'ErrorTestNodeStateProperty \{').Count -ne 1) {
        throw 'C1325 capture recipe changed: expected three message copies and one outer Error'
    }
    $record = $capture.Replace($C1325OriginalMessage, $Message)
    if ([regex]::Matches($record, [regex]::Escape($Message)).Count -ne 3) {
        throw 'C1325 derivative did not retain all three message copies'
    }
    return $record
}

function New-C1325Fixture {
    param([string]$Id, [string]$Record, $ExpectedMessage, [string]$Provenance = 'derived from D13 capture',
        [string]$Newline = 'LF', [switch]$Short)
    return [pscustomobject]@{
        Id = $Id; Record = $Record; ExpectedMessage = $ExpectedMessage
        Provenance = $Provenance; SourceCapture = 'd13-error-pair.ps1'; Newline = $Newline
        Uid = $(if ($Short) { 'Probe.C1325.1.1.Only.1.1.0' } else { $C1325Uid })
        ClassName = $(if ($Short) { 'Probe.C1325' } else { 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests' })
        Method = $(if ($Short) { 'Only' } else { 'C488_ApprovalPollingCannotConfirm' })
        Outcome = 'Failed'
    }
}

function Get-C1325ExplanationFixtures {
    $message = 'ShouldAssertException: boom, Exception = INNER still explanation'
    New-C1325Fixture 'E01' (New-C1325CapturedDerivative $message) ('[Test Failure] ' + $message)
    $message = "boom, Exception = alpha, Exception = beta`ncontinued"
    New-C1325Fixture 'E02' (New-C1325CapturedDerivative $message) ('[Test Failure] ' + $message)
    New-C1325Fixture 'E03' ((New-C1325CapturedDerivative $message).Replace("`n", "`r`n")) ('[Test Failure] ' + $message.Replace("`n", "`r`n")) -Newline 'CRLF'
    $message = 'boom, Exception = TUnit.Engine.Exceptions.TestFailedException: decoy { }'
    New-C1325Fixture 'E04' (New-C1325CapturedDerivative $message) ('[Test Failure] ' + $message)
    New-C1325Fixture 'E05' ($D13FailureRecord.Replace("`r`n", "`n")) `
        "[Test Failure] ShouldAssertException: OperatingSystem.IsWindows()`n    should be`nTrue`n    but was`nFalse" -Provenance 'captured D13 Error'
    foreach ($row in @(
        @('E06', 'Error', 'null exception body', '<null>'),
        @('E07', 'Timeout', 'timed out, Exception = detail', 'System.TimeoutException: timed out'),
        @('E08', 'Cancelled', 'cancelled by the run', 'System.OperationCanceledException: cancelled')
    )) {
        $record = New-D13StateRecord -Uid 'Probe.C1325.1.1.Only.1.1.0' -TypeName 'C1325' -Method 'Only' `
            -StateName $row[1] -Explanation $row[2] -ExceptionText $row[3]
        New-C1325Fixture $row[0] $record $row[2] -Short -Provenance 'reflected pinned state shape; not a real execution capture'
    }
    $message = 'ShouldAssertException: boom, Exception = INNER still explanation'
    $record = (New-C1325CapturedDerivative $message).Replace(
        ('Exception = TUnit.Engine.Exceptions.TestFailedException: ' + $message),
        'Exception = TUnit.Engine.Exceptions.TestFailedException: mismatched echo')
    New-C1325Fixture 'E09' $record $null -Provenance 'derived malformed echo'
    $message = "  boom`t`n`ntail  `r`nlast  "
    New-C1325Fixture 'E10' (New-C1325CapturedDerivative $message) ('[Test Failure] ' + $message) -Newline 'mixed LF/CRLF'
    # Two valid splits: after "[Test Failure] boom", and after the whole
    # explanation below. In the first interpretation Fixture.Frame starts the
    # stack; in the second the original captured stack follows the full echo.
    # Neither unescaped stack tail can prove which delimiter was the field.
    $message = "boom, Exception = TUnit.Engine.Exceptions.TestFailedException: boom`n   at Fixture.Frame()"
    New-C1325Fixture 'E11' (New-C1325CapturedDerivative $message) $null -Provenance 'derived ambiguous exception boundary; two valid echo/stack splits'
}
