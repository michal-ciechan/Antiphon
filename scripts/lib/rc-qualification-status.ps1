#requires -Version 5.1
# Read-only qualification observations. No nightly writer/lock helpers are loaded.

function Get-RcQualificationPathStamp {
    param([string]$Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        try {
            $attributes = [IO.File]::GetAttributes($cursor)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'unsupported-path' }
        } catch {
            $cause = $_.Exception
            while ($cause.InnerException) { $cause = $cause.InnerException }
            if ($cause -isnot [IO.FileNotFoundException] -and $cause -isnot [IO.DirectoryNotFoundException]) { throw }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.PSObject.Properties['UnixStat'] -and $item.UnixStat.ItemType -ne 'File')) {
        throw 'unsupported-path'
    }
    $identity = [string]$item.CreationTimeUtc.Ticks
    if ($item.PSObject.Properties['UnixStat']) { $identity = '{0}:{1}' -f $item.UnixStat.DeviceId, $item.UnixStat.Inode }
    return ('{0}:{1}:{2}' -f $identity, $item.Length, $item.LastWriteTimeUtc.Ticks)
}

function Read-RcQualificationRecord {
    param([string]$Path, [scriptblock]$AfterRead)
    $result = [ordered]@{ path = $Path; status = 'unreadable'; reason = 'read-error'; record = $null }
    $stream = $null
    try {
        $before = Get-RcQualificationPathStamp $Path
        # The writer holds Write/Read; our share must permit its write access.
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        if ($stream.Length -gt 1048576) { $result.reason = 'too-large'; return [pscustomobject]$result }
        $buffer = New-Object byte[] 1048577
        $count = 0
        while ($count -lt $buffer.Length) {
            $got = $stream.Read($buffer, $count, $buffer.Length - $count)
            if ($got -eq 0) { break }
            $count += $got
        }
        if ($count -gt 1048576) { $result.reason = 'too-large'; return [pscustomobject]$result }
        if ($AfterRead) { & $AfterRead $Path }
        try { $after = Get-RcQualificationPathStamp $Path } catch { $after = $null }
        if ($before -cne $after) { $result.status = 'changed'; $result.reason = 'record-changed'; return [pscustomobject]$result }
        try {
            $encoding = New-Object System.Text.UTF8Encoding($false, $true)
            $raw = $encoding.GetString($buffer, 0, $count).TrimStart([char]0xfeff)
            $parsed = ConvertFrom-Json -InputObject $raw -ErrorAction Stop
            if ($parsed -isnot [pscustomobject] -or -not $raw.TrimStart().StartsWith('{')) { throw 'object-required' }
            $result.status = 'ok'; $result.reason = $null; $result.record = $parsed
        } catch { $result.status = 'malformed'; $result.reason = 'invalid-json-object' }
    } catch {
        $cause = $_.Exception
        while ($cause.InnerException) { $cause = $cause.InnerException }
        if ($cause.Message -ceq 'unsupported-path') { $result.status = 'unsupported-path'; $result.reason = 'unsupported-path' }
        elseif ($cause -is [IO.FileNotFoundException] -or $cause -is [IO.DirectoryNotFoundException] -or
            $_.FullyQualifiedErrorId -like 'PathNotFound*') { $result.status = 'missing'; $result.reason = 'not-found' }
    } finally { if ($stream) { $stream.Dispose() } }
    return [pscustomobject]$result
}

function ConvertTo-RcQualificationTime {
    param($Value)
    if ($Value -is [datetime]) { return $Value.ToUniversalTime() }
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { return $null }
    $time = [datetime]::MinValue
    $style = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    if ([datetime]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, $style, [ref]$time)) { return $time }
    return $null
}

function Get-RcQualificationOwnerObservation {
    param($Owner)
    $unknown = [pscustomobject]@{ status = 'unknown'; reason = 'invalid-owner-identity' }
    if ($Owner.pid -isnot [int] -and $Owner.pid -isnot [long]) { return $unknown }
    if ($Owner.pid -le 0 -or $Owner.pid -gt [int]::MaxValue) { return $unknown }
    $expectedStart = ConvertTo-RcQualificationTime $Owner.processStartedAt
    if ($null -eq $expectedStart) { return $unknown }
    $process = $null
    try {
        try { $process = [Diagnostics.Process]::GetProcessById([int]$Owner.pid) }
        catch {
            $cause = $_.Exception
            while ($cause.InnerException) { $cause = $cause.InnerException }
            if ($cause -is [ArgumentException]) { return [pscustomobject]@{ status = 'absent'; reason = 'pid-not-found' } }
            throw
        }
        $actualStart = $process.StartTime.ToUniversalTime()
        if ($actualStart -ne $expectedStart) { return [pscustomobject]@{ status = 'reused'; reason = 'start-mismatch' } }
        return [pscustomobject]@{ status = 'alive'; reason = 'pid-start-match' }
    } catch { return [pscustomobject]@{ status = 'unknown'; reason = 'process-probe-error' } }
    finally { if ($process) { $process.Dispose() } }
}

function Select-RcQualificationFields {
    param($Record, [string[]]$Names)
    $selected = [ordered]@{}
    foreach ($name in $Names) {
        $value = $null
        if ($Record -and $Record.PSObject.Properties[$name]) { $value = $Record.$name }
        # Never emit arbitrary nested bodies or unrecognised producer fields.
        if ($value -is [datetime]) { $value = $value.ToUniversalTime().ToString('o') }
        if ($null -ne $value -and $value -isnot [string] -and $value -isnot [bool] -and
            $value -isnot [int] -and $value -isnot [long]) { $value = $null }
        $selected[$name] = $value
    }
    return [pscustomobject]$selected
}

function Get-RcQualificationStatus {
    param([string]$CandidateId, [string]$RunId, [string]$StateRoot, [string]$CoordinationRoot,
        [string]$ReleaseRoot, [scriptblock]$ObserveProcess)
    $result = [ordered]@{ candidateId = $CandidateId; runId = $RunId; stateRoot = $StateRoot;
        coordinationRoot = $CoordinationRoot; releaseRoot = $ReleaseRoot; observedAtUtc = [datetime]::UtcNow.ToString('o');
        observationComplete = $false; reason = $null; inputs = [ordered]@{}; state = $null;
        locks = [ordered]@{}; journal = $null; identityMismatches = @() }
    if ($CandidateId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$' -or
        $RunId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$' -or
        [string]::IsNullOrWhiteSpace($StateRoot) -or [string]::IsNullOrWhiteSpace($CoordinationRoot)) {
        $result.reason = 'invalid-input'; return [pscustomobject]$result
    }
    try {
        $paths = [ordered]@{ state = [IO.Path]::Combine($StateRoot, 'last-run.json');
            runLock = [IO.Path]::Combine($StateRoot, 'run.lock'); nativeLock = [IO.Path]::Combine($CoordinationRoot, 'native-run.lock') }
        if (-not [string]::IsNullOrWhiteSpace($ReleaseRoot)) {
            $paths.journal = [IO.Path]::Combine($ReleaseRoot, 'candidates', $CandidateId, 'candidate.json')
        }
        foreach ($name in $paths.Keys) { $result.inputs[$name] = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath($paths[$name])) }
    } catch { $result.reason = 'invalid-input'; return [pscustomobject]$result }
    if (-not $paths.Contains('journal')) { $result.inputs.journal = [pscustomobject]@{ path = $null; status = 'not-requested'; reason = $null; record = $null } }
    $complete = $result.inputs.state.status -ceq 'ok'
    foreach ($name in $paths.Keys) {
        if ($result.inputs[$name].status -notin @('ok','missing')) { $complete = $false }
    }
    $result.journal = Select-RcQualificationFields $result.inputs.journal.record @('candidateId','candidateRef','sha')
    $state = $result.inputs.state.record
    $result.state = Select-RcQualificationFields $state @('runId','candidateId','profile','ref','sha','expectedSha','phase',
        'startedAt','lastActivityAt','exitCode','testExit','reportExit','coverageComplete','testsPassed','reportDelivered','succeeded')
    if ($state) {
        foreach ($pair in @(@('runId',$RunId), @('candidateId',$CandidateId), @('profile','rc'))) {
            if ($state.($pair[0]) -isnot [string] -or $state.($pair[0]) -cne $pair[1]) { $result.identityMismatches += ('state-' + $pair[0]) }
        }
        $effectiveSha = $state.sha
        if ([string]::IsNullOrWhiteSpace([string]$effectiveSha)) { $effectiveSha = $state.expectedSha }
        if ($effectiveSha -isnot [string] -or $effectiveSha -cnotmatch '^[a-fA-F0-9]{40}$') { $result.identityMismatches += 'state-sha' }
        if ($state.sha -and $state.expectedSha -and $state.sha -cne $state.expectedSha) { $result.identityMismatches += 'state-expectedSha' }
        foreach ($name in @('coverageComplete','testsPassed','reportDelivered','succeeded')) {
            if ($null -ne $state.$name -and $state.$name -isnot [bool]) { $result.identityMismatches += ('state-type-' + $name); $result.state.$name = $null }
        }
        foreach ($name in @('exitCode','testExit','reportExit')) {
            if ($null -ne $state.$name -and $state.$name -isnot [int] -and $state.$name -isnot [long]) { $result.identityMismatches += ('state-type-' + $name); $result.state.$name = $null }
        }
        $journal = $result.inputs.journal.record
        if ($journal) {
            foreach ($pair in @(@('candidateId',$CandidateId), @('candidateRef',$state.ref), @('sha',$effectiveSha))) {
                if ($journal.($pair[0]) -isnot [string] -or $journal.($pair[0]) -cne $pair[1]) { $result.identityMismatches += ('journal-' + $pair[0]) }
            }
        }
    }
    foreach ($pair in @(@('run','runLock'), @('native','nativeLock'))) {
        $record = $result.inputs[$pair[1]].record
        $relationship = 'unavailable'
        $observation = [pscustomobject]@{ status = 'unknown'; reason = 'no-record' }
        if ($record) {
            $relationship = 'this-run'
            if ($record.runId -cne $RunId) {
                $relationship = 'other-run'
                if ($pair[0] -ceq 'run') { $result.identityMismatches += 'run-lock-runId' }
            }
            try {
                if ($ObserveProcess) { $observation = & $ObserveProcess $record }
                else { $observation = Get-RcQualificationOwnerObservation $record }
            } catch { $observation = [pscustomobject]@{ status = 'unknown'; reason = 'process-probe-error' } }
            if ($observation.status -ceq 'unknown') { $complete = $false }
        }
        $result.locks[$pair[0]] = [pscustomobject]@{ relationship = $relationship; ownerObservation = $observation;
            record = (Select-RcQualificationFields $record @('pid','processStartedAt','runId','lane','continuationId','acquiredAt','startedAt')) }
    }
    # Raw input objects never leave the reader; expose only each source's read outcome.
    foreach ($input in $result.inputs.Values) { $input.PSObject.Properties.Remove('record') }
    $result.observationComplete = $complete -and $result.identityMismatches.Count -eq 0
    if (-not $result.observationComplete) { $result.reason = 'incomplete-observation' }
    return [pscustomobject]$result
}
