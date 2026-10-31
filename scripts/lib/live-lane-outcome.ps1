#requires -Version 7.0
# CARD-1545 diagnostic-only typed outcomes. Raw screens, exception text and prompt
# bodies are inputs only; receipts contain closed enums and correlation booleans.
function Get-LiveLaneClassification([int]$Code) {
    switch ($Code) {
        0 { return 'GREEN' }; 1 { return 'RED' }; 2 { return 'INVALID' }
        4 { return 'SLOT-TIMEOUT' }; 21 { return 'DEFERRED-BUSY' }
        default { return 'INCONCLUSIVE' }
    }
}
function New-LiveLaneVerdict([int]$Code, [string]$Reason) {
    return [pscustomobject]@{ ExitCode=$Code; Reason=$Reason; Classification=(Get-LiveLaneClassification $Code) }
}
function Test-LiveLaneOutageType($ErrorType) {
    return ($ErrorType -is [string] -and $ErrorType -cin @('Overloaded','ServiceUnavailable','RateLimited'))
}
function Test-LiveLaneInteger($Value) { return ($Value -is [int] -or $Value -is [long]) }
function Test-LiveLaneTranscriptOutage($Row) {
    if ([string]::IsNullOrWhiteSpace($Row.Prompt) -or [string]::IsNullOrWhiteSpace($Row.Session) -or
        [string]::IsNullOrWhiteSpace($Row.Request)) { return $false }
    foreach ($prompt in @($Row.Transcript)) {
        if ($prompt -isnot [Collections.IDictionary] -or $prompt.Type -cne 'UserPrompt' -or
            $prompt.Body -cne $Row.Prompt -or $prompt.Session -cne $Row.Session -or
            $prompt.Request -cne $Row.Request -or -not (Test-LiveLaneInteger $prompt.Sequence)) { continue }
        foreach ($errorRecord in @($Row.Transcript)) {
            if ($errorRecord -is [Collections.IDictionary] -and $errorRecord.Type -ceq 'ApiError' -and
                $errorRecord.Session -ceq $Row.Session -and $errorRecord.Request -ceq $Row.Request -and
                (Test-LiveLaneInteger $errorRecord.Sequence) -and $errorRecord.Sequence -gt $prompt.Sequence -and
                (Test-LiveLaneOutageType $errorRecord.Error)) { return $true }
        }
    }
    return $false
}
function Test-LiveLaneProbeOutage($Row, $Probe) {
    return ($null -ne $Probe -and $Probe.Status -cin @('Unavailable','DependencyUnavailable') -and
        (Test-LiveLaneOutageType $Probe.Error) -and $Probe.Bounded -eq $true -and
        $Probe.Account -ceq $Row.Account -and $Probe.Endpoint -ceq $Row.Endpoint -and
        $Probe.Dependency -ceq $Row.Dependency -and
        -not [string]::IsNullOrWhiteSpace($Row.Account) -and
        -not [string]::IsNullOrWhiteSpace($Row.Endpoint) -and
        -not [string]::IsNullOrWhiteSpace($Row.Dependency))
}
function Get-LiveLaneSetupOutcome($Status, [bool]$Healthy=$false, [bool]$Deterministic=$false, [bool]$Outage=$false) {
    switch -CaseSensitive ($Status) {
        'Ready' { return New-LiveLaneVerdict 0 'ready' }
        'AuthMissing' { return New-LiveLaneVerdict 20 'auth-required' }
        'AuthExpired' { return New-LiveLaneVerdict 20 'auth-required' }
        'QuotaExceeded' { return New-LiveLaneVerdict 20 'quota-exhausted' }
        'DependencyUnavailable' {
            if ($Outage) { return New-LiveLaneVerdict 20 'provider-outage' }
            return New-LiveLaneVerdict 20 'setup-unclassified'
        }
        'StartupDefaultAbsent' { return New-LiveLaneVerdict 20 'premise-absent' }
        'ReadyTimeout' { return New-LiveLaneVerdict 20 'setup-unclassified' }
        'TrustNotCleared' { return New-LiveLaneVerdict 20 'setup-unclassified' }
        'BridgeNotEstablished' { return New-LiveLaneVerdict 20 'setup-unclassified' }
        'SetupContractViolation' {
            if ($Healthy -and $Deterministic) { return New-LiveLaneVerdict 1 'setup-contract-violation' }
            return New-LiveLaneVerdict 20 'setup-unclassified'
        }
        default { return New-LiveLaneVerdict 20 'unclassified-skip' }
    }
}
function Invoke-LiveLaneProbe {
    param([hashtable]$Boundary, [string]$Purpose, $Identity, [double]$Deadline)
    $start=[double](& $Boundary.Clock)
    $probe=& $Boundary.Probe $Purpose @{
        Operation='Availability'; PromptCount=0; Deadline=$Deadline
        Account=$Identity.Account; Endpoint=$Identity.Endpoint; Dependency=$Identity.Dependency
    }
    $finish=[double](& $Boundary.Clock)
    $bounded=$finish -le $Deadline -and $finish -ge $start
    # A late/unknown observation cannot attribute an outage. Adapters are fixture-only
    # here; future native transport must separately qualify actual cancellation.
    if ($probe -isnot [Collections.IDictionary] -or -not $bounded) {
        $probe=@{ Status='Timeout'; Error=''; Account=''; Endpoint=''; Dependency='' }
    }
    return @{
        Status=$probe.Status; Error=$probe.Error; Account=$probe.Account; Endpoint=$probe.Endpoint
        Dependency=$probe.Dependency; Bounded=$bounded; Started=$start; Finished=$finish; Phase=$Purpose
    }
}
function Invoke-LiveLanePreflight {
    param([hashtable]$Boundary, $Request, [double]$RunDeadline)
    $start=[double](& $Boundary.Clock)
    $limit=[Math]::Min($RunDeadline,$start+60)
    $probes=[Collections.Generic.List[object]]::new()
    $probe=$null
    for ($attempt=0; $attempt -lt 2; $attempt++) {
        $now=[double](& $Boundary.Clock)
        if ($now -ge $limit) { break }
        $allowance=[Math]::Min($limit,$now+30)
        $probe=Invoke-LiveLaneProbe $Boundary 'Preflight' $Request $allowance
        $probes.Add($probe)
        # Quota and explicit auth refusal never enter the availability retry loop.
        if ($probe.Status -cnotin @('Unavailable','Timeout')) { break }
    }
    if ($null -eq $probe) { $verdict=New-LiveLaneVerdict 20 'preflight-incomplete' }
    elseif ($probe.Status -ceq 'Unavailable') {
        $verdict=Get-LiveLaneSetupOutcome 'DependencyUnavailable' -Outage:(Test-LiveLaneProbeOutage $Request $probe)
    } else {
        $verdict=Get-LiveLaneSetupOutcome $probe.Status -Outage:(Test-LiveLaneProbeOutage $Request $probe)
    }
    return @{ Verdict=$verdict; Probes=@($probes) }
}
function Get-LiveLaneOutcome {
    param($Request, $Terminal, [string]$ChildRunId, [int]$RawChildExit, $PostProbe)
    $rows=[Collections.Generic.List[object]]::new()
    $verdict=New-LiveLaneVerdict 20 'evidence-incomplete'
    $rawOutcome=$null
    if ($Terminal -is [Collections.IDictionary]) {
        $invalid=''
        if (-not (Test-LiveLaneInteger $Terminal.SchemaVersion) -or $Terminal.SchemaVersion -ne 1 -or
            $Terminal.RunId -cne $ChildRunId -or $Terminal.Lane -cne $Request.Lane -or
            -not (Test-LiveLaneInteger $Terminal.Attempt) -or $Terminal.Attempt -ne $Request.Attempt -or
            $Terminal.Source -cne $Request.Source -or $Terminal.Build -cne 'Succeeded' -or
            $Terminal.Platform -cne 'Fixture' -or $Terminal.Authority -cne 'diagnostic' -or
            $Terminal.Complete -isnot [bool] -or $Terminal.Outcome -cnotin @('Passed','Failed','Invalid')) {
            $invalid='terminal-invalid'
        }
        if ($Terminal.Outcome -cin @('Passed','Failed','Invalid')) { $rawOutcome=$Terminal.Outcome }
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $exact=@($Request.Uids).Count -gt 0
        $terminalRows=@(); if ($null -ne $Terminal.Rows) { $terminalRows=@($Terminal.Rows) }
        foreach ($row in $terminalRows) {
            $uid=$null; $state='Invalid'; $setup='Unclassified'; $proof='None'
            if ($row -isnot [Collections.IDictionary] -or $row.Uid -isnot [string] -or
                [string]::IsNullOrWhiteSpace($row.Uid) -or $row.State -cnotin @('Passed','Failed','Skipped')) {
                $observation=New-LiveLaneVerdict 2 'row-invalid'; $invalid='terminal-invalid'
            } else {
                $state=$row.State
                if (-not $seen.Add($row.Uid)) { $exact=$false }
                if ($row.Uid -cin @($Request.Uids)) { $uid=$row.Uid } else { $exact=$false }
                $transcriptOutage=Test-LiveLaneTranscriptOutage $row
                $probeOutage=Test-LiveLaneProbeOutage $row $PostProbe
                if ($transcriptOutage) { $proof='Transcript' } elseif ($probeOutage) { $proof='Probe' }
                if ($row.Setup -cin @('Ready','AuthMissing','AuthExpired','QuotaExceeded','DependencyUnavailable',
                    'StartupDefaultAbsent','ReadyTimeout','TrustNotCleared','BridgeNotEstablished','SetupContractViolation')) { $setup=$row.Setup }
                if ($row.Setup -cne 'Ready') {
                    $observation=Get-LiveLaneSetupOutcome $row.Setup -Healthy:($row.Healthy -is [bool] -and $row.Healthy) `
                        -Deterministic:($row.Deterministic -is [bool] -and $row.Deterministic) -Outage:($proof -cne 'None')
                } elseif ($row.State -ceq 'Passed') {
                    $observation=New-LiveLaneVerdict 0 'all-passed'
                } elseif ($row.State -ceq 'Skipped') {
                    $observation=New-LiveLaneVerdict 20 'unclassified-skip'
                } elseif ($proof -cne 'None') {
                    $observation=New-LiveLaneVerdict 20 'provider-outage'
                } else {
                    $observation=New-LiveLaneVerdict 1 'assertion-failed'
                }
            }
            $rows.Add([pscustomobject]@{ Uid=$uid; State=$state; Setup=$setup; Proof=$proof
                ExitCode=$observation.ExitCode; Reason=$observation.Reason })
        }
        if ($seen.Count -ne @($Request.Uids).Count -or $terminalRows.Count -ne @($Request.Uids).Count) { $exact=$false }
        foreach ($uid in @($Request.Uids)) { if (-not $seen.Contains($uid)) { $exact=$false } }
        if (-not $exact -and -not $invalid) { $invalid='roster-incomplete' }
        $red=@($rows | Where-Object ExitCode -eq 1)
        $inconclusive=@($rows | Where-Object ExitCode -eq 20)
        # Independent invalid/red facts outrank unrelated outage or missing evidence.
        if ($invalid) { $verdict=New-LiveLaneVerdict 2 $invalid }
        elseif ($red.Count -gt 0) { $verdict=New-LiveLaneVerdict 1 $red[0].Reason }
        elseif ($Terminal.Complete -ne $true -or $Terminal.Evidence -cne 'Complete') {
            $verdict=New-LiveLaneVerdict 20 'evidence-incomplete'
        } elseif ([string]::IsNullOrWhiteSpace($Terminal.InputBefore) -or
            [string]::IsNullOrWhiteSpace($Terminal.ExecutableBefore) -or
            $Terminal.InputBefore -cne $Terminal.InputAfter -or $Terminal.ExecutableBefore -cne $Terminal.ExecutableAfter) {
            $verdict=New-LiveLaneVerdict 20 'input-drift'
        } elseif ($inconclusive.Count -gt 0) { $verdict=New-LiveLaneVerdict 20 $inconclusive[0].Reason }
        elseif ($RawChildExit -ne 0 -or $rawOutcome -cne 'Passed') { $verdict=New-LiveLaneVerdict 2 'checkpoint-inconsistent' }
        else { $verdict=New-LiveLaneVerdict 0 'all-passed' }
    }
    return [pscustomobject]@{
        ExitCode=$verdict.ExitCode; Reason=$verdict.Reason; Classification=$verdict.Classification
        Rows=@($rows); RawChildOutcome=$rawOutcome
    }
}
