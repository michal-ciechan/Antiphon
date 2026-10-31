#requires -Version 5.1
# Qualification supervisor heartbeat. Detection only: this file never stops a process.
# ASCII-only library, not an autostart script. Dot-source it from the supervisor.

function Get-QualificationHeartbeatField {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function ConvertTo-QualificationHeartbeatDouble {
    param($Value)
    if ($null -eq $Value) { return 0.0 }
    $parsed = 0.0
    $text = [string]$Value
    if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        return $parsed
    }
    return 0.0
}

function ConvertFrom-QualificationHeartbeatTime {
    param($Value)
    if ($Value -is [datetime]) { return $Value.ToUniversalTime() }
    if (-not ($Value -is [string]) -or [string]::IsNullOrWhiteSpace($Value)) { return $null }
    $parsed = [datetime]::MinValue
    $style = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    if ([datetime]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, $style, [ref]$parsed)) {
        return $parsed
    }
    return $null
}

function Get-QualificationGroupPlannedSeconds {
    param([string]$Path, [string]$ChunkId, [string]$SuiteId, [string]$RunId, [string]$Attempt)
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($ChunkId)) { return $null }
    if (-not [IO.File]::Exists($Path)) { return $null }
    try {
        $parsed = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
        foreach ($group in @($parsed.groups)) {
            if ($null -eq $group) { continue }
            $chunk = [string](Get-QualificationHeartbeatField $group 'chunk')
            if ($chunk -cne $ChunkId) { continue }
            $seconds = Get-QualificationHeartbeatField $group 'plannedSeconds'
            if ($null -eq $seconds) { return $null }
            return (ConvertTo-QualificationHeartbeatDouble $seconds)
        }
    } catch {
        return $null
    }
    return $null
}

function Write-QualificationActiveChunkIdentity {
    param([string]$Directory, [string]$SuiteId, [string]$ChunkId, [string]$RunId, [string]$Attempt, [string]$State='Active')
    return $false
}

function Get-QualificationHeartbeatIdleBudget {
    param($PlannedSeconds, [double]$FloorMinutes = 10, [double]$DefaultMinutes = 20)
    $known = $false
    $planned = 0.0
    if ($null -ne $PlannedSeconds -and -not [string]::IsNullOrWhiteSpace([string]$PlannedSeconds)) {
        $text = [string]$PlannedSeconds
        if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$planned)) {
            $known = $true
        }
    }
    if (-not $known) {
        return [pscustomobject]@{ minutes = [double]$DefaultMinutes; source = 'default-unknown-group' }
    }
    $minutes = $planned / 60.0
    if ($minutes -lt $FloorMinutes) { $minutes = $FloorMinutes }
    return [pscustomobject]@{ minutes = [double]$minutes; source = 'plannedSeconds' }
}

function Get-QualificationHeartbeatSubjects {
    param([object[]]$Processes, [int]$ChunkRootPid, [int[]]$ExcludedPids)
    $byPid = @{}
    $children = @{}
    foreach ($proc in @($Processes)) {
        if ($null -eq $proc) { continue }
        $processId = [int](Get-QualificationHeartbeatField $proc 'Pid')
        if ($processId -eq 0) { $processId = [int](Get-QualificationHeartbeatField $proc 'pid') }
        $byPid[[string]$processId] = $proc
        $parentId = 0
        $parentValue = Get-QualificationHeartbeatField $proc 'ParentPid'
        if ($null -eq $parentValue) { $parentValue = Get-QualificationHeartbeatField $proc 'parentPid' }
        if ($null -ne $parentValue) { $parentId = [int]$parentValue }
        $parentKey = [string]$parentId
        if (-not $children.ContainsKey($parentKey)) {
            $children[$parentKey] = New-Object System.Collections.Generic.List[int]
        }
        $children[$parentKey].Add($processId)
    }
    $excluded = @{}
    foreach ($excludedId in @($ExcludedPids)) { $excluded[[string]([int]$excludedId)] = $true }
    $subjects = New-Object System.Collections.Generic.List[object]
    if ($ChunkRootPid -le 0 -or -not $byPid.ContainsKey([string]$ChunkRootPid)) { return @() }
    $pending = New-Object System.Collections.Generic.Stack[int]
    $pending.Push($ChunkRootPid)
    $seen = @{}
    while ($pending.Count -gt 0) {
        $processId = $pending.Pop()
        $key = [string]$processId
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        if ($excluded.ContainsKey($key)) { continue }
        $subjects.Add($byPid[$key])
        if ($children.ContainsKey($key)) {
            foreach ($childId in @($children[$key])) { $pending.Push([int]$childId) }
        }
    }
    return @($subjects.ToArray())
}

function Update-QualificationSupervisorHeartbeat {
    param(
        [Parameter(Mandatory)][datetime]$NowUtc,
        [Parameter(Mandatory)][object[]]$Processes,
        [int]$ChunkRootPid = 0,
        [int[]]$ExcludedPids,
        $Previous,
        [double]$Trx = 0,
        [int64]$Bytes = 0,
        [string]$Newest = '',
        $PlannedSeconds,
        [double]$IdleCpuSecondsPerMinute = 5,
        [double]$DefaultIdleMinutes = 20,
        [double]$IdleFloorMinutes = 10,
        $DiagnosticProgress
    )
    $now = $NowUtc.ToUniversalTime()
    $budget = Get-QualificationHeartbeatIdleBudget -PlannedSeconds $PlannedSeconds -FloorMinutes $IdleFloorMinutes -DefaultMinutes $DefaultIdleMinutes
    $subjects = @(Get-QualificationHeartbeatSubjects -Processes $Processes -ChunkRootPid $ChunkRootPid -ExcludedPids $ExcludedPids)
    $priorCpu = @{}
    $previousRows = @(Get-QualificationHeartbeatField $Previous 'hostCpu')
    foreach ($row in $previousRows) {
        if ($null -eq $row) { continue }
        $processId = [int](Get-QualificationHeartbeatField $row 'pid')
        $priorCpu[[string]$processId] = ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $row 'cpuSeconds')
    }
    $previousAt = ConvertFrom-QualificationHeartbeatTime (Get-QualificationHeartbeatField $Previous 'observedAtUtc')
    $cpuSeconds = 0.0
    $delta = 0.0
    $samples = New-Object System.Collections.Generic.List[object]
    $subjectIds = New-Object System.Collections.Generic.List[int]
    foreach ($proc in $subjects) {
        $processId = [int](Get-QualificationHeartbeatField $proc 'Pid')
        if ($processId -eq 0) { $processId = [int](Get-QualificationHeartbeatField $proc 'pid') }
        $current = ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $proc 'CpuSeconds')
        if ($current -eq 0) {
            $alternate = Get-QualificationHeartbeatField $proc 'cpuSeconds'
            if ($null -ne $alternate) { $current = ConvertTo-QualificationHeartbeatDouble $alternate }
        }
        $cpuSeconds += $current
        $key = [string]$processId
        if ($priorCpu.ContainsKey($key)) { $delta += ($current - $priorCpu[$key]) }
        $subjectIds.Add($processId)
        $samples.Add([pscustomobject]@{ pid = $processId; cpuSeconds = $current })
    }
    $trxChanged = $false
    $bytesChanged = $false
    $elapsedMinutes = 0.0
    $measurable = $false
    if ($null -ne $previousAt) {
        $elapsedMinutes = ($now - $previousAt).TotalMinutes
        if ($elapsedMinutes -gt 0) {
            $measurable = $true
            $priorTrx = ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $Previous 'trx')
            $priorBytes = [int64](ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $Previous 'bytes'))
            if ($priorTrx -ne $Trx) { $trxChanged = $true }
            if ($priorBytes -ne $Bytes) { $bytesChanged = $true }
        }
    }
    $cpuPerMinute = 0.0
    if ($measurable -and $elapsedMinutes -gt 0) { $cpuPerMinute = $delta / $elapsedMinutes }
    $diagGrew = $false
    if ($null -ne $DiagnosticProgress) {
        $grewValue = Get-QualificationHeartbeatField $DiagnosticProgress 'Grew'
        if ($grewValue -eq $true) { $diagGrew = $true }
    }
    $quiet = $measurable -and ($cpuPerMinute -lt $IdleCpuSecondsPerMinute) -and (-not $trxChanged) -and (-not $bytesChanged) -and (-not $diagGrew)
    $lastCompletedUid = Get-QualificationHeartbeatField $Previous 'lastCompletedUid'
    $lastCompletedAtUtc = Get-QualificationHeartbeatField $Previous 'lastCompletedAtUtc'
    $lastDiagGrowthAtUtc = Get-QualificationHeartbeatField $Previous 'lastDiagGrowthAtUtc'
    $inFlightUid = Get-QualificationHeartbeatField $Previous 'inFlightUid'
    $lastPhase = Get-QualificationHeartbeatField $Previous 'lastPhase'
    if ($diagGrew) {
        $lastDiagGrowthAtUtc = $now.ToString('o')
        $completedUid = Get-QualificationHeartbeatField $DiagnosticProgress 'LastCompletedUid'
        if (-not [string]::IsNullOrWhiteSpace([string]$completedUid)) {
            $lastCompletedUid = [string]$completedUid
            $lastCompletedAtUtc = $now.ToString('o')
        }
        $flightUid = Get-QualificationHeartbeatField $DiagnosticProgress 'InFlightUid'
        if (-not [string]::IsNullOrWhiteSpace([string]$flightUid)) { $inFlightUid = [string]$flightUid }
        $phaseName = Get-QualificationHeartbeatField $DiagnosticProgress 'LastPhase'
        if (-not [string]::IsNullOrWhiteSpace([string]$phaseName)) { $lastPhase = [string]$phaseName }
    }
    $idleSince = $null
    $idleMinutes = 0.0
    if ($quiet) {
        $idleSince = ConvertFrom-QualificationHeartbeatTime (Get-QualificationHeartbeatField $Previous 'idleSinceUtc')
        if ($null -eq $idleSince) { $idleSince = $previousAt }
        $idleMinutes = ($now - $idleSince).TotalMinutes
    }
    $hang = $quiet -and ($idleMinutes -ge [double]$budget.minutes)
    $suspect = New-Object System.Collections.Generic.List[int]
    if ($hang) {
        if ($subjectIds.Count -eq 0 -and $ChunkRootPid -gt 0) { $suspect.Add($ChunkRootPid) }
        foreach ($processId in $subjectIds) { $suspect.Add($processId) }
    }
    $suspectSorted = @($suspect | Sort-Object -Unique)
    $reason = ''
    if ($hang) {
        $listed = ($suspectSorted | ForEach-Object { [string]$_ }) -join ','
        $idleText = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.##}', $idleMinutes)
        $rateText = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.##}', $cpuPerMinute)
        $limitText = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.##}', $IdleCpuSecondsPerMinute)
        $budgetText = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.##}', [double]$budget.minutes)
        if ($subjectIds.Count -eq 0) {
            $reason = "no test-host process under the chunk command; no trx growth and no chunk-log bytes for $idleText minutes; cpu $rateText/min under $limitText/min; budget $budgetText minutes ($($budget.source)); suspectPids $listed"
        } else {
            $reason = "test hosts idle for $idleText minutes: no trx growth, no chunk-log bytes, cpu $rateText/min under $limitText/min; budget $budgetText minutes ($($budget.source)); suspectPids $listed"
        }
    }
    $sinceText = $null
    if ($null -ne $idleSince) { $sinceText = $idleSince.ToUniversalTime().ToString('o') }
    return [pscustomobject]@{
        cpuSeconds = $cpuSeconds
        bytes = $Bytes
        idleMinutes = $idleMinutes
        trx = $Trx
        newest = $Newest
        hangSuspected = [bool]$hang
        hangReason = $reason
        suspectPids = $suspectSorted
        idleBudgetMinutes = [double]$budget.minutes
        idleBudgetSource = [string]$budget.source
        idleSinceUtc = $sinceText
        observedAtUtc = $now.ToString('o')
        hostCpu = @($samples.ToArray())
        lastCompletedUid = $lastCompletedUid
        lastCompletedAtUtc = $lastCompletedAtUtc
        lastDiagGrowthAtUtc = $lastDiagGrowthAtUtc
        inFlightUid = $inFlightUid
        lastPhase = $lastPhase
    }
}

function ConvertTo-QualificationHeartbeatJsonString {
    param([string]$Text)
    if ($null -eq $Text) { $Text = '' }
    $builder = New-Object System.Text.StringBuilder
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$builder.Append('\"') }
        elseif ($ch -eq '\') { [void]$builder.Append('\\') }
        elseif ($code -eq 10) { [void]$builder.Append('\n') }
        elseif ($code -eq 13) { [void]$builder.Append('\r') }
        elseif ($code -eq 9) { [void]$builder.Append('\t') }
        elseif ($code -lt 32) {
            [void]$builder.Append('\u')
            [void]$builder.Append($code.ToString('x4'))
        } else {
            [void]$builder.Append($ch)
        }
    }
    return $builder.ToString()
}

function Format-QualificationHeartbeatNumber {
    param([double]$Value)
    return $Value.ToString('G15', [Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-QualificationHeartbeatOptionalJson {
    param($Value)
    if ($null -eq $Value) { return 'null' }
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return 'null' }
    return '"' + (ConvertTo-QualificationHeartbeatJsonString $text) + '"'
}

function ConvertTo-QualificationSupervisorHeartbeatJson {
    param($Record)
    $pids = @(Get-QualificationHeartbeatField $Record 'suspectPids')
    $pidText = New-Object System.Collections.Generic.List[string]
    foreach ($processId in $pids) {
        if ($null -eq $processId) { continue }
        $pidText.Add([string]([int]$processId))
    }
    $samples = @(Get-QualificationHeartbeatField $Record 'hostCpu')
    $sampleText = New-Object System.Collections.Generic.List[string]
    foreach ($row in $samples) {
        if ($null -eq $row) { continue }
        $processId = [int](Get-QualificationHeartbeatField $row 'pid')
        $cpu = ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $row 'cpuSeconds')
        $sampleText.Add(('{{"pid":{0},"cpuSeconds":{1}}}' -f $processId, (Format-QualificationHeartbeatNumber $cpu)))
    }
    $hangValue = 'false'
    $hangFlag = Get-QualificationHeartbeatField $Record 'hangSuspected'
    if ($hangFlag -eq $true) { $hangValue = 'true' }
    $since = Get-QualificationHeartbeatField $Record 'idleSinceUtc'
    $sinceJson = 'null'
    if ($null -ne $since -and -not [string]::IsNullOrWhiteSpace([string]$since)) {
        $sinceJson = '"' + (ConvertTo-QualificationHeartbeatJsonString ([string]$since)) + '"'
    }
    $lines = New-Object System.Collections.Generic.List[string]
    $cpuText = Format-QualificationHeartbeatNumber (ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $Record 'cpuSeconds'))
    $byteText = ([int64](ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $Record 'bytes'))).ToString([Globalization.CultureInfo]::InvariantCulture)
    $idleText = Format-QualificationHeartbeatNumber (ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $Record 'idleMinutes'))
    $trxText = Format-QualificationHeartbeatNumber (ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $Record 'trx'))
    $newestText = ConvertTo-QualificationHeartbeatJsonString ([string](Get-QualificationHeartbeatField $Record 'newest'))
    $reasonText = ConvertTo-QualificationHeartbeatJsonString ([string](Get-QualificationHeartbeatField $Record 'hangReason'))
    $budgetText = Format-QualificationHeartbeatNumber (ConvertTo-QualificationHeartbeatDouble (Get-QualificationHeartbeatField $Record 'idleBudgetMinutes'))
    $sourceText = ConvertTo-QualificationHeartbeatJsonString ([string](Get-QualificationHeartbeatField $Record 'idleBudgetSource'))
    $observedText = ConvertTo-QualificationHeartbeatJsonString ([string](Get-QualificationHeartbeatField $Record 'observedAtUtc'))
    $lines.Add('"cpuSeconds":' + $cpuText)
    $lines.Add('"bytes":' + $byteText)
    $lines.Add('"idleMinutes":' + $idleText)
    $lines.Add('"trx":' + $trxText)
    $lines.Add('"newest":"' + $newestText + '"')
    $lines.Add('"hangSuspected":' + $hangValue)
    $lines.Add('"hangReason":"' + $reasonText + '"')
    $lines.Add('"suspectPids":[' + ($pidText -join ',') + ']')
    $lines.Add('"idleBudgetMinutes":' + $budgetText)
    $lines.Add('"idleBudgetSource":"' + $sourceText + '"')
    $lines.Add('"idleSinceUtc":' + $sinceJson)
    $lines.Add('"observedAtUtc":"' + $observedText + '"')
    $lines.Add('"hostCpu":[' + ($sampleText -join ',') + ']')
    $lines.Add('"lastCompletedUid":' + (ConvertTo-QualificationHeartbeatOptionalJson (Get-QualificationHeartbeatField $Record 'lastCompletedUid')))
    $lines.Add('"lastCompletedAtUtc":' + (ConvertTo-QualificationHeartbeatOptionalJson (Get-QualificationHeartbeatField $Record 'lastCompletedAtUtc')))
    $lines.Add('"lastDiagGrowthAtUtc":' + (ConvertTo-QualificationHeartbeatOptionalJson (Get-QualificationHeartbeatField $Record 'lastDiagGrowthAtUtc')))
    $lines.Add('"inFlightUid":' + (ConvertTo-QualificationHeartbeatOptionalJson (Get-QualificationHeartbeatField $Record 'inFlightUid')))
    $lines.Add('"lastPhase":' + (ConvertTo-QualificationHeartbeatOptionalJson (Get-QualificationHeartbeatField $Record 'lastPhase')))
    $joined = New-Object System.Text.StringBuilder
    [void]$joined.Append('{')
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($index -gt 0) { [void]$joined.Append(',') }
        [void]$joined.Append($lines[$index])
    }
    [void]$joined.Append('}')
    return $joined.ToString()
}

function Write-QualificationSupervisorHeartbeat {
    param([Parameter(Mandatory)]$Record, [Parameter(Mandatory)][string]$Path)
    $json = ConvertTo-QualificationSupervisorHeartbeatJson $Record
    $directory = [IO.Path]::GetDirectoryName($Path)
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not [IO.Directory]::Exists($directory)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }
    $temp = $Path + '.tmp'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText($temp, $json, $utf8)
    if ([IO.File]::Exists($Path)) { [IO.File]::Delete($Path) }
    [IO.File]::Move($temp, $Path)
}

function Read-QualificationSupervisorHeartbeat {
    param([Parameter(Mandatory)][string]$Path)
    $utf8 = New-Object System.Text.UTF8Encoding $false, $true
    $text = [IO.File]::ReadAllText($Path, $utf8)
    return ($text | ConvertFrom-Json)
}

function Read-QualificationDiagnosticProgress {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int64]$Offset = 0,
        [string]$HeartbeatPath = '',
        [int]$MaxBytes = 1048576
    )
    $result = [pscustomobject]@{
        Grew = $false
        Offset = [int64]$Offset
        LastCompletedUid = $null
        LastCompletedAtUtc = $null
        InFlightUid = $null
        LastPhase = $null
    }
    if (-not [string]::IsNullOrWhiteSpace($HeartbeatPath)) {
        $left = $Path
        $right = $HeartbeatPath
        try { $left = [IO.Path]::GetFullPath($Path) } catch { $left = $Path }
        try { $right = [IO.Path]::GetFullPath($HeartbeatPath) } catch { $right = $HeartbeatPath }
        if ([string]::Equals($left, $right, [StringComparison]::OrdinalIgnoreCase)) { return $result }
    }
    if (-not [IO.File]::Exists($Path)) { return $result }
    $stream = $null
    try {
        $stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $length = $stream.Length
        if ($Offset -lt 0) { $Offset = 0 }
        if ($Offset -gt $length) { $Offset = $length }
        $available = $length - $Offset
        if ($available -le 0) {
            $result.Offset = [int64]$Offset
            return $result
        }
        $take = [int]$MaxBytes
        if ($available -lt $take) { $take = [int]$available }
        [void]$stream.Seek($Offset, [System.IO.SeekOrigin]::Begin)
        $buffer = New-Object byte[] $take
        $read = $stream.Read($buffer, 0, $take)
        if ($read -le 0) { return $result }
        $reachedEof = (($Offset + $read) -ge $length)
        $use = $read
        if (-not $reachedEof) {
            $lastNl = -1
            for ($i = 0; $i -lt $read; $i++) {
                if ($buffer[$i] -eq 10) { $lastNl = $i }
            }
            if ($lastNl -lt 0) { return $result }
            $use = $lastNl + 1
        }
        $text = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $use)
        $uidPattern = [regex]'TestNodeUid\s*\{\s*Value\s*=\s*([^\s\}]+)\s*\}'
        $statePattern = [regex]'(Passed|Failed|Timeout|Cancelled|Skipped|InProgress|Discovered)TestNodeStateProperty'
        $terminal = @{
            Passed = $true
            Failed = $true
            Timeout = $true
            Cancelled = $true
            Skipped = $true
        }
        $grew = $false
        foreach ($line in ($text -split "`n")) {
            $uidMatch = $uidPattern.Match($line)
            $stateMatch = $statePattern.Match($line)
            if (-not $uidMatch.Success -or -not $stateMatch.Success) { continue }
            $grew = $true
            $uid = $uidMatch.Groups[1].Value
            $state = $stateMatch.Groups[1].Value
            $result.LastPhase = $state
            if ($terminal.ContainsKey($state)) {
                $result.LastCompletedUid = $uid
            } elseif ($state -ceq 'InProgress') {
                $result.InFlightUid = $uid
            }
        }
        $result.Grew = $grew
        $result.Offset = [int64]($Offset + $use)
        return $result
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}
