#requires -Version 5.1
# CARD-0487: independent nightly health evaluation, due-time and notification receipt.
# ASCII-only. No live messages; notification goes through an injectable sink.

if ($script:AntiphonNightlyHealthLoaded) { return }
$script:AntiphonNightlyHealthLoaded = $true

function Get-NightlyLondonTimeZone {
    $tz = $null
    try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('GMT Standard Time') } catch { }
    if ($null -eq $tz) {
        try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('Europe/London') } catch { }
    }
    if ($null -eq $tz) { throw 'Europe/London timezone not available' }
    return $tz
}

function ConvertTo-NightlyLondonLocal {
    param([datetime]$Utc)
    if ($Utc.Kind -eq [DateTimeKind]::Unspecified) {
        $Utc = [datetime]::SpecifyKind($Utc, [DateTimeKind]::Utc)
    } else {
        $Utc = $Utc.ToUniversalTime()
    }
    return [TimeZoneInfo]::ConvertTimeFromUtc($Utc, (Get-NightlyLondonTimeZone))
}

function Get-NightlyDueUtcForLondonDate {
    param([datetime]$LondonDate)
    $tz = Get-NightlyLondonTimeZone
    $local = Get-Date -Year $LondonDate.Year -Month $LondonDate.Month -Day $LondonDate.Day -Hour 0 -Minute 30 -Second 0
    $local = [datetime]::SpecifyKind($local, [DateTimeKind]::Unspecified)
    return [TimeZoneInfo]::ConvertTimeToUtc($local, $tz)
}

function Get-NightlyNotificationStorePath {
    param([string]$StateRoot)
    return (Join-Path $StateRoot 'notifications.json')
}

function Read-NightlyNotificationStore {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ events = @() }
    }
    return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Save-NightlyNotificationStore {
    param([string]$Path, $Store)
    Write-NightlyAtomicJson -Path $Path -Object $Store
}

function Get-NightlyNotificationLogicalIdentity {
    param($Event)
    $parts = @(
        [string]$Event.workspace
        [string]$Event.localDueDate
        [string]$Event.windmillJobId
        [string]$Event.nativeRunId
        [string]$Event.sha
        [string]$Event.policyHash
        [string]$Event.failureKind
    )
    return ($parts -join '|')
}

function New-NightlyNotificationIdentity {
    param($Event)
    return ((Get-NightlyNotificationLogicalIdentity -Event $Event) + '|' + [string]$Event.notificationId)
}

function Add-NightlyNotificationEvent {
    param(
        [string]$StorePath,
        $Event
    )
    $store = Read-NightlyNotificationStore -Path $StorePath
    $events = @()
    if ($store.events) { $events = @($store.events) }
    $logical = Get-NightlyNotificationLogicalIdentity -Event $Event
    $existing = @($events | Where-Object { (Get-NightlyNotificationLogicalIdentity -Event $_) -eq $logical })
    if ($existing.Count -gt 0) {
        return [pscustomobject]@{ Deduped = $true; Event = $existing[0] }
    }
    if ($Event -is [System.Collections.IDictionary]) {
        if ([string]::IsNullOrWhiteSpace([string]$Event['notificationId'])) {
            $Event['notificationId'] = New-NightlyRunId
        }
        if ([string]::IsNullOrWhiteSpace([string]$Event['status'])) {
            $Event['status'] = 'pending'
        }
    } else {
        if ([string]::IsNullOrWhiteSpace([string]$Event.notificationId)) {
            $Event | Add-Member -NotePropertyName notificationId -NotePropertyValue (New-NightlyRunId) -Force
        }
        if ([string]::IsNullOrWhiteSpace([string]$Event.status)) {
            $Event | Add-Member -NotePropertyName status -NotePropertyValue 'pending' -Force
        }
    }
    $events += $Event
    $store = [ordered]@{ events = $events }
    Save-NightlyNotificationStore -Path $StorePath -Store $store
    return [pscustomobject]@{ Deduped = $false; Event = $Event }
}

function New-NightlyNotificationPayload {
    param($Event, [string]$Destination)
    $id = [string]$Event.notificationId
    $run = [string]$Event.nativeRunId
    $text = ('Antiphon nightly monitor failureKind={0} run={1} sha={2} notificationId={3}' -f `
        [string]$Event.failureKind, $run, [string]$Event.sha, $id)
    return [ordered]@{
        text = $text
        chat_id = $Destination
        notificationId = $id
        nativeRunId = $run
    }
}

function Read-NightlyRecipientReceiptStore {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ receipts = @() }
    }
    $obj = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $obj) { return [pscustomobject]@{ receipts = @() } }
    if ($obj.receipts) { return [pscustomobject]@{ receipts = @($obj.receipts) } }
    if ($obj -is [System.Array]) { return [pscustomobject]@{ receipts = @($obj) } }
    return [pscustomobject]@{ receipts = @($obj) }
}

function Save-NightlyRecipientReceipt {
    param([string]$StateRoot, $Event)
    $path = Get-NightlyRecipientReceiptPath -StateRoot $StateRoot
    $store = Read-NightlyRecipientReceiptStore -Path $path
    $receipts = @($store.receipts)
    $id = [string]$Event.notificationId
    $run = [string]$Event.nativeRunId
    foreach ($item in $receipts) {
        if ([string]$item.notificationId -eq $id -and [string]$item.nativeRunId -eq $run) {
            return
        }
    }
    $receipts += [ordered]@{
        notificationId = $id
        nativeRunId = $run
        receivedAt = (Get-NightlyUtcNow).ToString('o')
    }
    Write-NightlyAtomicJson -Path $path -Object ([ordered]@{ receipts = @($receipts) })
}

function Set-NightlyNotificationReceived {
    param([string]$StorePath, $Event)
    $store = Read-NightlyNotificationStore -Path $StorePath
    $events = @()
    if ($store.events) { $events = @($store.events) }
    $logical = Get-NightlyNotificationLogicalIdentity -Event $Event
    $updated = @()
    foreach ($e in $events) {
        if ((Get-NightlyNotificationLogicalIdentity -Event $e) -eq $logical) {
            $e | Add-Member -NotePropertyName status -NotePropertyValue 'received' -Force
        }
        $updated += $e
    }
    Save-NightlyNotificationStore -Path $StorePath -Store ([ordered]@{ events = $updated })
}

function Invoke-NightlyNotificationEnqueue {
    param($Event, $Sink)
    if ($null -eq $Sink) { throw 'notification sink unavailable' }
    return & $Sink $Event
}

function Test-NightlyNotificationTransportAccepted {
    param($Event)
    if ($null -eq $Event) { return $false }
    try {
        if ([bool]$Event.transportAccepted) { return $true }
    } catch { }
    $status = [string]$Event.status
    if ($status -eq 'accepted' -or $status -eq 'received') { return $true }
    return $false
}

function Test-NightlyNotificationEnqueueAccepted {
    param($Notify)
    if ($null -eq $Notify) { return $false }
    $map = ConvertFrom-NightlyJsonMap -Object $Notify
    if ($null -eq $map) { return $false }
    if ($map.Contains('Enqueued') -and [bool]$map['Enqueued']) { return $true }
    if ($map.Contains('TransportStatus')) {
        try {
            $code = [int]$map['TransportStatus']
            if ($code -ge 200 -and $code -lt 300) { return $true }
        } catch {
            return $false
        }
    }
    return $false
}

function Set-NightlyNotificationTransportAccepted {
    param(
        [string]$StorePath,
        $Event,
        $Notify = $null
    )
    $store = Read-NightlyNotificationStore -Path $StorePath
    $events = @()
    if ($store.events) { $events = @($store.events) }
    $logical = Get-NightlyNotificationLogicalIdentity -Event $Event
    $updated = @()
    foreach ($e in $events) {
        if ((Get-NightlyNotificationLogicalIdentity -Event $e) -eq $logical) {
            $e | Add-Member -NotePropertyName transportAccepted -NotePropertyValue $true -Force
            $cur = [string]$e.status
            if ($cur -ne 'received') {
                $e | Add-Member -NotePropertyName status -NotePropertyValue 'accepted' -Force
            }
            if ($null -ne $Notify) {
                $map = ConvertFrom-NightlyJsonMap -Object $Notify
                if ($map -and $map.Contains('TransportStatus')) {
                    $e | Add-Member -NotePropertyName transportStatus -NotePropertyValue $map['TransportStatus'] -Force
                }
            }
        }
        $updated += $e
    }
    Save-NightlyNotificationStore -Path $StorePath -Store ([ordered]@{ events = $updated })
}

function Test-NightlyNotificationReceipt {
    param($Event, $RecipientView)
    if ($null -eq $RecipientView) { return $false }
    $wantId = [string]$Event.notificationId
    $wantRun = [string]$Event.nativeRunId
    foreach ($item in @($RecipientView)) {
        if ([string]$item.notificationId -eq $wantId -and [string]$item.nativeRunId -eq $wantRun) {
            return $true
        }
    }
    return $false
}

function Test-NightlyMonitorHealth {
    param(
        $Registration,
        $Schedule,
        $Jobs,
        $NativeState,
        [datetime]$NowUtc,
        $LastGreen = $null,
        [string]$ExpectedScriptHash,
        [string]$ExpectedPolicyHash,
        [string]$ExpectedTag = 'desktop',
        [int]$StartGraceMinutes = 30,
        [int]$MonitorStaleMinutes = 60
    )
    $result = [ordered]@{
        Healthy = $true
        Pending = $false
        Reasons = @()
        ReadyForDeferral = $false
        TerminateRequested = $false
    }
    if ($null -eq $Registration -or -not $Registration.script) {
        $result.Healthy = $false
        $result.Reasons += 'missing-script'
    }
    if ($null -eq $Schedule) {
        $result.Healthy = $false
        $result.Reasons += 'missing-schedule'
    } else {
        if (-not [bool]$Schedule.enabled) {
            $result.Healthy = $false
            $result.Reasons += 'disabled'
        }
        if ([bool]$Schedule.paused) {
            $result.Healthy = $false
            $result.Reasons += 'paused'
        }
        if ([string]$Schedule.tag -ne $ExpectedTag -and [string]$Schedule.tag -ne '' -and $ExpectedTag -eq 'desktop') {
            if ([string]$Registration.tag -ne $ExpectedTag) {
                $result.Healthy = $false
                $result.Reasons += 'non-desktop-tag'
            }
        }
        if ($Registration -and [string]$Registration.tag -ne $ExpectedTag -and $ExpectedTag -eq 'desktop') {
            $result.Healthy = $false
            $result.Reasons += 'non-desktop-tag'
        }
        $args = $Schedule.args
        if ($args) {
            $hasOverride = $false
            if ($args.suite -or $args.suites -or $args.ref -or $args.Ref) { $hasOverride = $true }
            if ($hasOverride) {
                $result.Healthy = $false
                $result.Reasons += 'suite-ref-override'
            }
        }
        if ($Schedule.workerVersion -and $Schedule.serverVersion -and
            [string]$Schedule.workerVersion -ne [string]$Schedule.serverVersion) {
            $result.Healthy = $false
            $result.Reasons += 'worker-version-mismatch'
        }
    }
    if ($Registration -and $ExpectedScriptHash) {
        if ([string]$Registration.hash -ne $ExpectedScriptHash) {
            $result.Healthy = $false
            $result.Reasons += 'script-hash-mismatch'
        }
    }

    $londonNow = ConvertTo-NightlyLondonLocal -Utc $NowUtc
    $todayDate = $londonNow.Date
    $dueUtc = Get-NightlyDueUtcForLondonDate -LondonDate $todayDate
    $graceEnd = $dueUtc.AddMinutes($StartGraceMinutes)
    $morningUtc = Get-NightlyMorningDeadlineUtc -LondonDate $todayDate

    # CARD-0544 D-6: only a scheduled job can be the due day's run; a manual run never stands in for it.
    $todayJob = Find-NightlyScheduledJob -Jobs $Jobs -LondonDate $todayDate

    if ($NowUtc -ge $graceEnd -and ($null -eq $todayJob -or [string]$todayJob.status -eq 'queued')) {
        $result.Healthy = $false
        $result.Reasons += 'overdue-start'
    } elseif ($NowUtc -lt $graceEnd -and ($null -eq $todayJob -or [string]$todayJob.status -eq 'queued')) {
        $result.Pending = $true
    }

    # Daily run validity is judged against the London due date, never against the age of
    # completedAt: a green that finished at 02:00 stays today's green all day.
    $states = @($NativeState, $LastGreen)
    $todayGreen = Get-NightlyScheduledGreen -Job $todayJob -States $states
    $green = $todayGreen
    $greenJob = $todayJob
    if ($NowUtc -ge $morningUtc) {
        if ($null -eq $todayGreen) {
            $yesterday = $false
            if ($NativeState -and $NativeState.completedAt) {
                $c = [datetime]$NativeState.completedAt
                if ($c.ToUniversalTime().Date -lt $dueUtc.Date) { $yesterday = $true }
            }
            $result.Healthy = $false
            if ($yesterday) { $result.Reasons += 'yesterday-green-insufficient' }
            else { $result.Reasons += 'missing-expected-run' }
        }
    } else {
        if ($null -eq $todayGreen) {
            $result.Pending = $true
            # Before today's 08:00 deadline the previous due day's complete scheduled green bridges.
            $previousJob = Find-NightlyScheduledJob -Jobs $Jobs -LondonDate $todayDate.AddDays(-1)
            $green = Get-NightlyScheduledGreen -Job $previousJob -States $states
            $greenJob = $previousJob
        }
    }

    if ($null -ne $green) {
        # A known newer failed or incomplete completed attempt removes permission immediately.
        $greenAt = ([datetime]$green.completedAt).ToUniversalTime()
        if ($NativeState -and $NativeState.completedAt -and [string]$NativeState.runId -ne [string]$green.runId) {
            $attemptAt = ([datetime]$NativeState.completedAt).ToUniversalTime()
            if ($attemptAt -gt $greenAt -and -not (Test-NightlyGreenFlags -State $NativeState)) {
                $result.Reasons += 'newer-failed-attempt'
                $green = $null
            }
        }
        if ($null -ne $green) {
            foreach ($j in @($Jobs)) {
                if ($null -eq $j -or [string]$j.status -ne 'failed') { continue }
                if (-not (Test-NightlyJobScheduled -Job $j)) { continue }
                if ([string]$j.id -and [string]$j.id -eq [string]$greenJob.id) { continue }
                $failedFor = Get-NightlyJobDueUtc -Job $j
                $greenFor = Get-NightlyJobDueUtc -Job $greenJob
                if ($null -ne $failedFor -and $null -ne $greenFor -and $failedFor -gt $greenFor) {
                    $result.Reasons += 'newer-failed-attempt'
                    $green = $null
                    break
                }
            }
        }
    }

    if ($NativeState) {
        if ($todayJob -and [string]$NativeState.runId -ne [string]$todayJob.nativeRunId -and [string]$todayJob.nativeRunId) {
            $result.Healthy = $false
            $result.Reasons += 'run-id-mismatch'
        }
        if ($ExpectedPolicyHash -and [string]$NativeState.policyHash -ne $ExpectedPolicyHash) {
            $result.Healthy = $false
            $result.Reasons += 'policy-mismatch'
        }
        if ($todayJob -and $todayJob.sha -and $NativeState.sha -and [string]$todayJob.sha -ne [string]$NativeState.sha) {
            $result.Healthy = $false
            $result.Reasons += 'sha-mismatch'
        }
        $inProgress = ([string]$NativeState.phase -eq 'Started' -and -not $NativeState.completedAt)
        if (-not $inProgress) {
            if ((Test-NightlyHasField -Object $NativeState -Name 'coverageComplete') -and -not [bool]$NativeState.coverageComplete) {
                $result.Healthy = $false
                $result.Reasons += 'incomplete-coverage'
            }
            if ((Test-NightlyHasField -Object $NativeState -Name 'testsPassed') -and -not [bool]$NativeState.testsPassed) {
                $result.Healthy = $false
                $result.Reasons += 'tests-red'
            }
            if ((Test-NightlyHasField -Object $NativeState -Name 'reportDelivered') -and -not [bool]$NativeState.reportDelivered) {
                $result.Healthy = $false
                $result.Reasons += 'report-outage'
            }
        } else {
            $fresh = $false
            if ($NativeState.lastActivityAt) {
                $la = [datetime]$NativeState.lastActivityAt
                if (($NowUtc - $la.ToUniversalTime()).TotalMinutes -le 60) { $fresh = $true }
            }
            # A run still making progress is today's pending state; a stalled one is unhealthy.
            if ($fresh) { $result.Pending = $true }
            elseif ($NativeState.lastActivityAt) { $result.Healthy = $false; $result.Reasons += 'stale-progress' }
            else { $result.Healthy = $false; $result.Reasons += 'started-incomplete' }
        }
    }

    if ($result.Reasons.Count -gt 0) { $result.Healthy = $false }
    # Monitor freshness is the reader's RecordedAt check; readiness here is health plus a valid
    # scheduled green for the due day (or the pre-08:00 bridge), never completedAt age.
    $result.ReadyForDeferral = [bool]($result.Healthy -and $null -ne $green)
    $result.GreenRunId = $(if ($green) { [string]$green.runId } else { '' })
    $result.GreenJobId = $(if ($green -and $greenJob) { [string]$greenJob.id } else { '' })
    $result.GreenJobNativeRunId = $(if ($green -and $greenJob) { [string]$greenJob.nativeRunId } else { '' })
    return [pscustomobject]$result
}

function Test-NightlyHasField {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $false }
    if ($Object -is [System.Collections.IDictionary]) { return $Object.Contains($Name) }
    return ($null -ne $Object.PSObject.Properties[$Name])
}

function Get-NightlyMorningDeadlineUtc {
    param([datetime]$LondonDate)
    $local = Get-Date -Year $LondonDate.Year -Month $LondonDate.Month -Day $LondonDate.Day -Hour 8 -Minute 0 -Second 0 -Millisecond 0
    $local = [datetime]::SpecifyKind($local, [DateTimeKind]::Unspecified)
    return [TimeZoneInfo]::ConvertTimeToUtc($local, (Get-NightlyLondonTimeZone))
}

function Test-NightlyJobScheduled {
    param($Job)
    if ($null -eq $Job) { return $false }
    if ((Test-NightlyHasField -Object $Job -Name 'scheduled') -and -not [bool]$Job.scheduled) { return $false }
    if ([string]$Job.trigger -and [string]$Job.trigger -ne 'scheduled') { return $false }
    return $true
}

function Get-NightlyJobDueUtc {
    param($Job)
    if ($null -eq $Job) { return $null }
    if ([string]$Job.localDueDate) {
        return (Get-NightlyDueUtcForLondonDate -LondonDate ([datetime]::ParseExact([string]$Job.localDueDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)))
    }
    if ($Job.scheduledFor) { return ([datetime]$Job.scheduledFor).ToUniversalTime() }
    return $null
}

function Find-NightlyScheduledJob {
    param($Jobs, [datetime]$LondonDate)
    $date = $LondonDate.ToString('yyyy-MM-dd')
    $dueUtc = Get-NightlyDueUtcForLondonDate -LondonDate $LondonDate
    $found = $null
    foreach ($j in @($Jobs)) {
        if ($null -eq $j -or -not (Test-NightlyJobScheduled -Job $j)) { continue }
        if ([string]$j.localDueDate -eq $date) { return $j }
        if ($j.scheduledFor) {
            $sf = ([datetime]$j.scheduledFor).ToUniversalTime()
            if ($sf -ge $dueUtc.AddMinutes(-1) -and $sf -le $dueUtc.AddMinutes(1)) { $found = $j }
        }
    }
    return $found
}

function Test-NightlyGreenFlags {
    param($State)
    return [bool]($State -and [bool]$State.coverageComplete -and [bool]$State.testsPassed -and [bool]$State.reportDelivered)
}

function Get-NightlyScheduledGreen {
    param($Job, $States)
    # The due day's complete green: its scheduled job succeeded with a result naming the native run,
    # and that native run completed scheduled, master, with full coverage, green tests and a delivered report.
    if ($null -eq $Job -or [string]$Job.status -ne 'success') { return $null }
    $runId = [string]$Job.nativeRunId
    if ([string]::IsNullOrWhiteSpace($runId)) { return $null }
    foreach ($s in @($States)) {
        if ($null -eq $s -or [string]$s.runId -ne $runId) { continue }
        if ([string]$s.trigger -ne 'scheduled') { continue }
        if (-not (Test-NightlyGreenFlags -State $s)) { continue }
        if (-not $s.completedAt) { continue }
        $ref = [string]$s.ref
        if ($ref -and $ref -ne 'master' -and $ref -ne 'origin/master') { continue }
        if ([string]$Job.sha -and [string]$s.sha -and [string]$Job.sha -ne [string]$s.sha) { continue }
        return $s
    }
    return $null
}

function Test-NightlyMonitorRouting {
    param($Definition)
    if ([string]$Definition.tag -eq 'desktop') {
        throw 'monitor must not use desktop tag'
    }
    return $true
}

function Get-NightlyMonitorResultPath {
    param([string]$StateRoot)
    return (Join-Path $StateRoot 'last-monitor.json')
}

function Get-NightlyRecipientReceiptPath {
    param([string]$StateRoot)
    return (Join-Path $StateRoot 'notification-receipts.json')
}

function Get-NightlyWindmillConfig {
    $base = [string]$env:WINDMILL_BASE_URL
    $token = [string]$env:WINDMILL_TOKEN
    $tokenFile = [string]$env:WINDMILL_TOKEN_FILE
    if ([string]::IsNullOrWhiteSpace($token) -and -not [string]::IsNullOrWhiteSpace($tokenFile)) {
        if (Test-Path -LiteralPath $tokenFile) {
            $token = [System.IO.File]::ReadAllText($tokenFile).Trim()
        }
    }
    $workspace = [string]$env:WINDMILL_WORKSPACE
    if ([string]::IsNullOrWhiteSpace($workspace)) { $workspace = 'mc' }
    $scriptPath = [string]$env:ANTIPHON_NIGHTLY_WINDMILL_SCRIPT
    if ([string]::IsNullOrWhiteSpace($scriptPath)) { $scriptPath = 'u/lndcobra/antiphon_nightly_tests' }
    $notifyPath = [string]$env:ANTIPHON_NIGHTLY_NOTIFY_SCRIPT
    if ([string]::IsNullOrWhiteSpace($notifyPath)) { $notifyPath = 'u/lndcobra/telegram_notify' }
    $hasCreds = (-not [string]::IsNullOrWhiteSpace($base) -and -not [string]::IsNullOrWhiteSpace($token))
    return [pscustomobject]@{
        BaseUrl = $base
        Token = $token
        Workspace = $workspace
        ScriptPath = $scriptPath
        NotifyPath = $notifyPath
        HasCredentials = $hasCreds
    }
}

function Invoke-NightlyWindmillHttp {
    param(
        [string]$Method,
        [string]$Url,
        [string]$Token,
        $Body = $null
    )
    $headers = @{ Authorization = ('Bearer {0}' -f $Token) }
    $params = @{
        Method = $Method
        Uri = $Url
        Headers = $headers
        UseBasicParsing = $true
        TimeoutSec = 30
    }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json; charset=utf-8'
        $params.Body = ($Body | ConvertTo-Json -Compress -Depth 8)
    }
    $resp = Invoke-WebRequest @params
    $text = [string]$resp.Content
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return ($text | ConvertFrom-Json)
}

function ConvertFrom-NightlyWindmillJob {
    # CARD-0544 D-6: map one Windmill jobs/list row. Only a completed job whose success is the real
    # boolean true AND whose result names the native run is 'success'; queued/running rows keep that
    # state; a completed failure is 'failed'; any other shape (missing result, string booleans,
    # unknown type) is 'unknown' and can never qualify as a scheduled green.
    param($Job)
    $type = [string]$Job.type
    $status = 'unknown'
    $result = $null
    if ($Job.PSObject.Properties['result']) { $result = $Job.result }
    $nativeRunId = ''
    $sha = ''
    $localDue = ''
    if ($result -and -not ($result -is [string])) {
        if ($result.PSObject.Properties['nativeRunId']) { $nativeRunId = [string]$result.nativeRunId }
        if ($result.PSObject.Properties['sha']) { $sha = [string]$result.sha }
        if ($result.PSObject.Properties['localDueDate']) { $localDue = [string]$result.localDueDate }
    }
    if ($type -eq 'QueuedJob') {
        $running = $Job.PSObject.Properties['running']
        if ($running -and $running.Value -is [bool]) {
            $status = $(if ($running.Value) { 'running' } else { 'queued' })
        }
    } elseif ($type -eq 'CompletedJob') {
        $success = $Job.PSObject.Properties['success']
        if ($success -and $success.Value -is [bool]) {
            if (-not $success.Value) { $status = 'failed' }
            elseif (-not [string]::IsNullOrWhiteSpace($nativeRunId) -and -not [string]::IsNullOrWhiteSpace($localDue)) { $status = 'success' }
        }
    }
    $schedulePath = ''
    if ($Job.PSObject.Properties['schedule_path']) { $schedulePath = [string]$Job.schedule_path }
    return @{
        id = [string]$Job.id
        status = $status
        scheduled = (-not [string]::IsNullOrWhiteSpace($schedulePath))
        nativeRunId = $nativeRunId
        sha = $sha
        localDueDate = $localDue
        scheduledFor = $Job.scheduled_for
    }
}

function New-NightlyProductionWindmillApi {
    param($Config)
    if ($null -eq $Config -or -not [bool]$Config.HasCredentials) { return $null }
    $base = ([string]$Config.BaseUrl).TrimEnd('/')
    $ws = [string]$Config.Workspace
    $token = [string]$Config.Token
    $scriptPath = [string]$Config.ScriptPath
    $escaped = ($scriptPath -replace '\\', '/')
    return @{
        GetRegistration = {
            $obj = Invoke-NightlyWindmillHttp -Method GET -Url ('{0}/api/w/{1}/scripts/get/p/{2}' -f $base, $ws, $escaped) -Token $token
            if ($null -eq $obj) { return $null }
            return @{
                script = $true
                hash = [string]$obj.hash
                tag = [string]$obj.tag
                path = [string]$obj.path
            }
        }.GetNewClosure()
        GetSchedule = {
            $obj = Invoke-NightlyWindmillHttp -Method GET -Url ('{0}/api/w/{1}/schedules/get/{2}' -f $base, $ws, $escaped) -Token $token
            if ($null -eq $obj) { return $null }
            $paused = $false
            if ($obj.PSObject.Properties['paused']) { $paused = [bool]$obj.paused }
            $enabled = $true
            if ($obj.PSObject.Properties['enabled']) { $enabled = [bool]$obj.enabled }
            return @{
                enabled = $enabled
                paused = $paused
                tag = [string]$obj.tag
                args = $obj.args
                workerVersion = [string]$obj.worker_version
                serverVersion = [string]$obj.server_version
            }
        }.GetNewClosure()
        GetJobs = {
            $url = '{0}/api/w/{1}/jobs/list?script_path_exact={2}&per_page=20' -f $base, $ws, [uri]::EscapeDataString($escaped)
            $obj = Invoke-NightlyWindmillHttp -Method GET -Url $url -Token $token
            $rows = @()
            foreach ($j in @($obj)) {
                if ($null -eq $j) { continue }
                $rows += ConvertFrom-NightlyWindmillJob -Job $j
            }
            return $rows
        }.GetNewClosure()
        EnqueueNotification = {
            param($Event, [string]$Destination)
            $body = New-NightlyNotificationPayload -Event $Event -Destination $Destination
            $job = Invoke-NightlyWindmillHttp -Method POST `
                -Url ('{0}/api/w/{1}/jobs/run/p/{2}' -f $base, $ws, [string]$Config.NotifyPath) `
                -Token $token -Body $body
            return [pscustomobject]@{
                Enqueued = $true
                TransportStatus = 201
                JobId = [string]$job
                NotificationId = [string]$body.notificationId
                Received = $false
            }
        }.GetNewClosure()
    }
}

function Get-NightlyWindmillApi {
    if ($script:NightlySeams -and $script:NightlySeams.WindmillApi) {
        return $script:NightlySeams.WindmillApi
    }
    return (New-NightlyProductionWindmillApi -Config (Get-NightlyWindmillConfig))
}

function Get-NightlyRecipientView {
    param([string]$StateRoot)
    $items = @()
    $path = Get-NightlyRecipientReceiptPath -StateRoot $StateRoot
    $fileStore = Read-NightlyRecipientReceiptStore -Path $path
    if ($fileStore.receipts) { $items += @($fileStore.receipts) }
    if ($script:NightlySeams -and $script:NightlySeams.RecipientView) {
        $items += @($script:NightlySeams.RecipientView.Invoke())
    }
    return @($items)
}

function Get-NightlyNotificationSink {
    param($Api, [string]$AuthorizedDestination)
    if ($script:NightlySeams -and $script:NightlySeams.NotificationSink) {
        return $script:NightlySeams.NotificationSink
    }
    if ($null -eq $Api -or -not $Api.EnqueueNotification) { return $null }
    if ([string]::IsNullOrWhiteSpace($AuthorizedDestination)) { return $null }
    $dest = $AuthorizedDestination
    return {
        param($Event)
        return $Api.EnqueueNotification.Invoke($Event, $dest)
    }.GetNewClosure()
}

function Save-NightlyMonitorResult {
    param([string]$StateRoot, $Result)
    $path = Get-NightlyMonitorResultPath -StateRoot $StateRoot
    Write-NightlyAtomicJson -Path $path -Object $Result
}

function Invoke-AntiphonNightlyHealth {
    param(
        [string]$StateRoot,
        [string]$SeamsPath = '',
        [string]$ExpectedScriptHash = '',
        [string]$ExpectedPolicyHash = '',
        [string]$AuthorizedDestination = '',
        [string]$RepositoryPath = '',
        [string]$ProjectId = '',
        [switch]$PassThru
    )
    Import-NightlySeams -SeamsPath $SeamsPath
    if ([string]::IsNullOrWhiteSpace($RepositoryPath)) { $RepositoryPath = [string]$env:ANTIPHON_NIGHTLY_REPOSITORY_PATH }
    if ([string]::IsNullOrWhiteSpace($ProjectId)) { $ProjectId = [string]$env:ANTIPHON_NIGHTLY_PROJECT_ID }
    $now = Get-NightlyUtcNow
    $api = Get-NightlyWindmillApi
    $registration = $null
    $schedule = $null
    $jobs = @()
    if ($api) {
        $registration = $api.GetRegistration.Invoke()
        $schedule = $api.GetSchedule.Invoke()
        $jobs = @($api.GetJobs.Invoke())
    }
    $native = $null
    $last = Join-Path $StateRoot 'last-run.json'
    if (Test-Path -LiteralPath $last) {
        $native = Get-Content -LiteralPath $last -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $lastGreen = $null
    $greenPath = Join-Path $StateRoot 'last-complete-green.json'
    if (Test-Path -LiteralPath $greenPath) {
        $lastGreen = Get-Content -LiteralPath $greenPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $health = Test-NightlyMonitorHealth -Registration $registration -Schedule $schedule -Jobs $jobs `
        -NativeState $native -LastGreen $lastGreen -NowUtc $now -ExpectedScriptHash $ExpectedScriptHash -ExpectedPolicyHash $ExpectedPolicyHash
    $notify = $null
    $receiptOk = $false
    if (-not $health.Healthy) {
        if ([string]::IsNullOrWhiteSpace($AuthorizedDestination) -and -not ($script:NightlySeams -and $script:NightlySeams.AllowOfflineNotify)) {
            $health.Reasons += 'unauthorized-destination'
        } else {
            $event = [ordered]@{
                workspace = 'mc'
                localDueDate = (ConvertTo-NightlyLondonLocal -Utc $now).ToString('yyyy-MM-dd')
                windmillJobId = $(if ($jobs.Count -gt 0) { [string]$jobs[0].id } else { '' })
                nativeRunId = $(if ($native) { [string]$native.runId } else { '' })
                sha = $(if ($native) { [string]$native.sha } else { '' })
                policyHash = $(if ($native) { [string]$native.policyHash } else { $ExpectedPolicyHash })
                failureKind = ($health.Reasons -join ',')
                notificationId = New-NightlyRunId
                status = 'pending'
            }
            $storePath = Get-NightlyNotificationStorePath -StateRoot $StateRoot
            $saved = Add-NightlyNotificationEvent -StorePath $storePath -Event $event
            $deliveredEvent = $saved.Event
            $sink = Get-NightlyNotificationSink -Api $api -AuthorizedDestination $AuthorizedDestination
            $view = Get-NightlyRecipientView -StateRoot $StateRoot
            $receiptOk = Test-NightlyNotificationReceipt -Event $deliveredEvent -RecipientView $view
            if (-not $receiptOk) {
                $alreadyAccepted = Test-NightlyNotificationTransportAccepted -Event $deliveredEvent
                if (-not $alreadyAccepted) {
                    try {
                        if ($sink) { $notify = Invoke-NightlyNotificationEnqueue -Event $deliveredEvent -Sink $sink }
                        if (Test-NightlyNotificationEnqueueAccepted -Notify $notify) {
                            Set-NightlyNotificationTransportAccepted -StorePath $storePath -Event $deliveredEvent -Notify $notify
                        }
                    } catch {
                        $notify = [pscustomobject]@{ Enqueued = $false; Error = $_.Exception.Message; Received = $false }
                        $health.Healthy = $false
                        $health.Reasons += 'enqueue-failed'
                    }
                }
                $view = Get-NightlyRecipientView -StateRoot $StateRoot
                $receiptOk = Test-NightlyNotificationReceipt -Event $deliveredEvent -RecipientView $view
            }
            if ($receiptOk) {
                Save-NightlyRecipientReceipt -StateRoot $StateRoot -Event $deliveredEvent
                Set-NightlyNotificationReceived -StorePath $storePath -Event $deliveredEvent
            } else {
                $health.Healthy = $false
                if ($health.Reasons -notcontains 'notification-unreceived') {
                    $health.Reasons += 'notification-unreceived'
                }
            }
            if ($notify -and $notify.PSObject.Properties['Received']) {
                $notify.Received = [bool]$receiptOk
            } elseif ($null -ne $notify) {
                $notify = [pscustomobject]@{ Enqueued = [bool]$notify.Enqueued; Received = [bool]$receiptOk; Transport = $notify }
            }
        }
    }
    $out = [pscustomobject]@{
        ExitCode = $(if ($health.Healthy) { 0 } else { 1 })
        Health = $health
        Notification = $notify
        Receipt = $receiptOk
        RecordedAt = $now.ToString('o')
        # CARD-0544 D-7: the identities the server's readiness reader matches against its receipt.
        Identity = [ordered]@{
            RepositoryPath = $RepositoryPath
            ProjectId = $ProjectId
            PolicyHash = $ExpectedPolicyHash
            ScriptHash = $ExpectedScriptHash
            ScheduledRunId = [string]$health.GreenRunId
            JobNativeRunId = [string]$health.GreenJobNativeRunId
            WindmillJobId = [string]$health.GreenJobId
        }
    }
    Save-NightlyMonitorResult -StateRoot $StateRoot -Result $out
    if ($PassThru) { return $out }
    return $out
}
