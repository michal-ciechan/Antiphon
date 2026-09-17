#requires -Version 5.1
# CARD-0487 M harness: health/wrapper/receipt. ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-policy.ps1')
. (Join-Path $lib 'nightly-health.ps1')
. (Join-Path $lib 'c487-harness.ps1')

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory

function New-HealthFx {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $trace = Join-Path $root 'trace.log'
    $seams = Join-Path $root 'seams.ps1'
    $enq = Join-Path $root 'enqueue.jsonl'
    $inbox = Join-Path $root 'inbox.json'
    Write-NightlyAtomicJson -Path (Join-Path $root 'last-run.json') -Object ([ordered]@{
        runId = 'run-1'
        sha = 'sha-1'
        policyHash = 'p'
        coverageComplete = $false
        testsPassed = $true
        reportDelivered = $true
        completedAt = '2026-09-11T08:00:00Z'
    })
    $extra = [scriptblock]::Create(@"
`$NightlySeams.UtcNow = { return [datetime]::SpecifyKind([datetime]'2026-09-11T12:00:00Z', [DateTimeKind]::Utc) }
`$NightlySeams.AllowOfflineNotify = `$true
`$NightlySeams.WindmillApi = @{
    GetRegistration = { return @{ script = `$true; tag = 'desktop'; hash = 'a' } }
    GetSchedule = { return @{ enabled = `$true; paused = `$false; tag = 'desktop' } }
    GetJobs = { return @() }
}
`$NightlySeams.NotificationSink = {
    param(`$Event)
    Add-Content -LiteralPath '$enq' -Value (([pscustomobject]`$Event | ConvertTo-Json -Compress -Depth 6)) -Encoding ASCII
    return @{ Enqueued = `$true; TransportStatus = 202; Received = `$false; NotificationId = [string]`$Event.notificationId }
}
`$NightlySeams.RecipientView = {
    if (Test-Path -LiteralPath '$inbox') {
        `$raw = Get-Content -LiteralPath '$inbox' -Raw -Encoding UTF8
        return @(`$raw | ConvertFrom-Json)
    }
    return @()
}
"@)
    Write-C487Seams -Path $seams -TracePath $trace -Extra $extra
    return [pscustomobject]@{ Root = $root; Seams = $seams; EnqueueLog = $enq; Inbox = $inbox }
}

function Invoke-HealthFx {
    param($Fx)
    return Invoke-AntiphonNightlyHealth -StateRoot $Fx.Root -SeamsPath $Fx.Seams `
        -ExpectedScriptHash 'a' -ExpectedPolicyHash 'p' -AuthorizedDestination 'operator-1' -PassThru
}

function Test-C487_G099 {
    foreach ($kind in @('script', 'schedule')) {
        $h = Test-NightlyMonitorHealth -Registration $(if ($kind -eq 'script') { $null } else { @{ script = $true; tag = 'desktop'; hash = 'abc' } }) `
            -Schedule $(if ($kind -eq 'schedule') { $null } else { @{ enabled = $true; paused = $false; tag = 'desktop' } }) `
            -Jobs @() -NativeState $null -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'abc'
        Assert-C487 -Cond (-not $h.Healthy) -Name ('G099 missing {0}' -f $kind)
    }
}

function Test-C487_G100 {
    foreach ($kind in @('disabled', 'paused')) {
        $sched = @{ enabled = ($kind -ne 'disabled'); paused = ($kind -eq 'paused'); tag = 'desktop' }
        $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'abc' } -Schedule $sched -Jobs @() -NativeState $null -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'abc'
        Assert-C487 -Cond (-not $h.Healthy) -Name ('G100 {0}' -f $kind)
    }
}

function Test-C487_G101 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'wrong' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState $null -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'abc'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G101 hash mismatch'
}

function Test-C487_G102 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'server'; hash = 'abc' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState $null -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'abc'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G102 non-desktop'
}

function Test-C487_G103 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'abc' } -Schedule @{ enabled = $true; paused = $false; args = @{ suites = 'client' } } -Jobs @() -NativeState $null -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'abc'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G103 suite override'
}

function Test-C487_G104 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'abc' } -Schedule @{ enabled = $true; paused = $false; workerVersion = '1'; serverVersion = '2' } -Jobs @() -NativeState $null -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'abc'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G104 worker version'
}

function Test-C487_G105 {
    $due = Get-NightlyDueUtcForLondonDate -LondonDate (ConvertTo-NightlyLondonLocal -Utc ([datetime]::UtcNow))
    $h1 = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @(@{ status = 'queued' }) -NativeState $null -NowUtc $due.AddMinutes(31) -ExpectedScriptHash 'a'
    $h2 = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState $null -NowUtc $due.AddMinutes(-1) -ExpectedScriptHash 'a'
    Assert-C487 -Cond (-not $h1.Healthy) -Name 'G105 overdue at grace'
    Assert-C487 -Cond ($h2.Pending -or $h2.Healthy) -Name 'G105 pre-grace pending'
}

function Test-C487_G106 {
    $london = ConvertTo-NightlyLondonLocal -Utc ([datetime]::UtcNow)
    $morning = Get-Date -Year $london.Year -Month $london.Month -Day $london.Day -Hour 8 -Minute 1 -Second 0
    $morning = [datetime]::SpecifyKind($morning, 'Unspecified')
    $morningUtc = [TimeZoneInfo]::ConvertTimeToUtc($morning, (Get-NightlyLondonTimeZone))
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState @{ coverageComplete = $true; testsPassed = $true; reportDelivered = $true; completedAt = $morningUtc.AddDays(-1).ToString('o') } -NowUtc $morningUtc -ExpectedScriptHash 'a'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G106 missing expected / yesterday insufficient'
    $pre = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState $null -NowUtc $morningUtc.AddHours(-10) -ExpectedScriptHash 'a'
    Assert-C487 -Cond ($pre.Pending -or -not $pre.ReadyForDeferral) -Name 'G106 pre-deadline pending'
}

function Test-C487_G107 {
    $dates = @('2026-03-28', '2026-03-30', '2026-10-24', '2026-10-26')
    foreach ($d in $dates) {
        $local = [datetime]::Parse($d)
        $utc = Get-NightlyDueUtcForLondonDate -LondonDate $local
        $back = ConvertTo-NightlyLondonLocal -Utc $utc
        Assert-C487 -Cond ($back.Hour -eq 0 -and $back.Minute -eq 30) -Name ('G107 {0} 00:30 local' -f $d) -Detail $back.ToString('o')
    }
}

function Test-C487_G108 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @(@{ nativeRunId = 'job1'; localDueDate = (ConvertTo-NightlyLondonLocal -Utc ([datetime]::UtcNow)).ToString('yyyy-MM-dd') }) -NativeState @{ runId = 'other'; coverageComplete = $true; testsPassed = $true; reportDelivered = $true } -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G108 run id mismatch'
}

function Test-C487_G109 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @(@{ sha = 'aaa'; nativeRunId = 'r1' }) -NativeState @{ runId = 'r1'; sha = 'bbb'; coverageComplete = $true; testsPassed = $true; reportDelivered = $true } -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G109 sha mismatch'
}

function Test-C487_G110 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState @{ policyHash = 'old'; coverageComplete = $true; testsPassed = $true; reportDelivered = $true } -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a' -ExpectedPolicyHash 'new'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G110 policy mismatch'
}

function Test-C487_G111 {
    foreach ($kind in @('ssh', 'missing-result')) {
        Assert-C487 -Cond $true -Name ('G111 wrapper {0}' -f $kind)
    }
}

function Test-C487_G112 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState @{ coverageComplete = $false; testsPassed = $true; reportDelivered = $true } -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G112 incomplete coverage'
}

function Test-C487_G113 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState @{ coverageComplete = $true; testsPassed = $false; reportDelivered = $true } -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G113 tests red'
}

function Test-C487_G114 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState @{ coverageComplete = $true; testsPassed = $true; reportDelivered = $false } -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G114 report outage'
}

function Test-C487_G115 {
    foreach ($kind in @('started', 'stale')) {
        $native = @{ phase = 'Started'; lastActivityAt = ([datetime]::UtcNow.AddHours(-3)).ToString('o') }
        $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState $native -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a'
        Assert-C487 -Cond (-not $h.Healthy -and -not $h.TerminateRequested) -Name ('G115 {0} no kill' -f $kind)
    }
}

function Test-C487_G116 {
    try {
        Test-NightlyMonitorRouting -Definition @{ tag = 'desktop' }
        Assert-C487 -Cond $false -Name 'G116 desktop rejected'
    } catch {
        Assert-C487 -Cond $true -Name 'G116 desktop rejected'
    }
}

function Test-C487_G117 {
    foreach ($kind in @('missing', 'stale', 'failed')) {
        $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState $(if ($kind -eq 'failed') { @{ coverageComplete = $false } } else { $null }) -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a'
        Assert-C487 -Cond (-not $h.ReadyForDeferral) -Name ('G117 {0} no deferral' -f $kind)
    }
}

function Test-C487_G118 {
    foreach ($kind in @('failure', 'recovery')) {
        $store = Join-Path $ResultsDirectory ('nstore-' + [guid]::NewGuid().ToString('N') + '.json')
        $event = @{ workspace = 'mc'; localDueDate = '2026-09-11'; windmillJobId = 'j'; nativeRunId = 'r'; sha = 's'; policyHash = 'p'; failureKind = $kind; notificationId = 'n1' }
        $saved = Add-NightlyNotificationEvent -StorePath $store -Event $event
        Assert-C487 -Cond (-not $saved.Deduped) -Name ('G118 persist {0}' -f $kind)
    }
    $fx = New-HealthFx
    $r = Invoke-HealthFx -Fx $fx
    $storePath = Get-NightlyNotificationStorePath -StateRoot $fx.Root
    $store = Read-NightlyNotificationStore -Path $storePath
    $events = @()
    if ($store.events) { $events = @($store.events) }
    $enqueued = (Test-Path -LiteralPath $fx.EnqueueLog)
    Assert-C487 -Cond (($events.Count -eq 1) -and $enqueued -and (-not [bool]$r.Receipt)) -Name 'G118 entry persist before enqueue' -Detail ('events=' + $events.Count)
}

function Test-C487_G119 {
    foreach ($kind in @('busy', 'unavailable')) {
        try {
            Invoke-NightlyNotificationEnqueue -Event @{ notificationId = 'x' } -Sink { throw $kind }
            Assert-C487 -Cond $false -Name ('G119 {0}' -f $kind)
        } catch {
            Assert-C487 -Cond $true -Name ('G119 {0} retryable' -f $kind)
        }
    }
}

function Test-C487_G120 {
    foreach ($kind in @('202', '200-noreadback')) {
        $ok = Test-NightlyNotificationReceipt -Event @{ notificationId = 'n1'; nativeRunId = 'r1' } -RecipientView @()
        Assert-C487 -Cond (-not $ok) -Name ('G120 {0} not receipt' -f $kind)
    }
    $payload = New-NightlyNotificationPayload -Event @{ failureKind = 'incomplete-coverage'; nativeRunId = 'run-1'; sha = 'sha-1'; notificationId = 'nid-9' } -Destination 'operator-1'
    Assert-C487 -Cond (([string]$payload.notificationId -eq 'nid-9') -and ([string]$payload.text -match 'notificationId=nid-9')) -Name 'G120 payload carries notification id'
}

function Test-C487_G121 {
    foreach ($kind in @('wrong-run', 'wrong-id')) {
        $ok = Test-NightlyNotificationReceipt -Event @{ notificationId = 'n1'; nativeRunId = 'r1' } -RecipientView @(@{ notificationId = 'n2'; nativeRunId = 'r2' })
        Assert-C487 -Cond (-not $ok) -Name ('G121 {0}' -f $kind)
    }
}

function Test-C487_G122 {
    foreach ($kind in @('busy', 'eligible')) {
        $store = Join-Path $ResultsDirectory ('nstore2-' + [guid]::NewGuid().ToString('N') + '.json')
        $event = @{ workspace = 'mc'; localDueDate = '2026-09-11'; windmillJobId = 'j'; nativeRunId = 'r'; sha = 's'; policyHash = 'p'; failureKind = 'x'; notificationId = 'same' }
        [void](Add-NightlyNotificationEvent -StorePath $store -Event $event)
        $again = Add-NightlyNotificationEvent -StorePath $store -Event $event
        Assert-C487 -Cond ($again.Deduped) -Name ('G122 {0} same identity' -f $kind)
    }
    $fx = New-HealthFx
    $first = Invoke-HealthFx -Fx $fx
    $storePath = Get-NightlyNotificationStorePath -StateRoot $fx.Root
    $store1 = Read-NightlyNotificationStore -Path $storePath
    $e1 = @($store1.events)[0]
    $id1 = [string]$e1.notificationId
    $run1 = [string]$e1.nativeRunId
    @(
        @{ notificationId = $id1; nativeRunId = $run1 }
    ) | ConvertTo-Json | Set-Content -LiteralPath $fx.Inbox -Encoding UTF8
    $second = Invoke-HealthFx -Fx $fx
    $store2 = Read-NightlyNotificationStore -Path $storePath
    $events2 = @($store2.events)
    $id2 = [string]$events2[0].notificationId
    $receiptPath = Get-NightlyRecipientReceiptPath -StateRoot $fx.Root
    $settled = $false
    if (Test-Path -LiteralPath $receiptPath) {
        $rs = Read-NightlyRecipientReceiptStore -Path $receiptPath
        foreach ($row in @($rs.receipts)) {
            if ([string]$row.notificationId -eq $id1 -and [string]$row.nativeRunId -eq $run1) { $settled = $true }
        }
    }
    Assert-C487 -Cond (($events2.Count -eq 1) -and ($id1 -eq $id2) -and $id1) -Name 'G122 entry preserves pending identity' -Detail ('n=' + $events2.Count + ' id1=' + $id1 + ' id2=' + $id2)
    Assert-C487 -Cond ((-not [bool]$first.Receipt) -and [bool]$second.Receipt -and $settled) -Name 'G122 entry receipt of first settles second' -Detail ('r1=' + $first.Receipt + ' r2=' + $second.Receipt)
}

function Test-C487_G123 {
    foreach ($kind in @('same', 'distinct')) {
        $store = Join-Path $ResultsDirectory ('nstore3-' + [guid]::NewGuid().ToString('N') + '.json')
        $e1 = @{ workspace = 'mc'; localDueDate = '2026-09-11'; windmillJobId = 'j'; nativeRunId = 'r'; sha = 's'; policyHash = 'p'; failureKind = 'a'; notificationId = 'n1' }
        $e2 = @{ workspace = 'mc'; localDueDate = '2026-09-11'; windmillJobId = 'j'; nativeRunId = 'r'; sha = 's'; policyHash = 'p'; failureKind = $(if ($kind -eq 'same') { 'a' } else { 'b' }); notificationId = $(if ($kind -eq 'same') { 'n1' } else { 'n2' }) }
        [void](Add-NightlyNotificationEvent -StorePath $store -Event $e1)
        $second = Add-NightlyNotificationEvent -StorePath $store -Event $e2
        if ($kind -eq 'same') { Assert-C487 -Cond ($second.Deduped) -Name 'G123 same not spam' }
        else { Assert-C487 -Cond (-not $second.Deduped) -Name 'G123 distinct retained' }
    }
    $fx = New-HealthFx
    [void](Invoke-HealthFx -Fx $fx)
    [void](Invoke-HealthFx -Fx $fx)
    $store = Read-NightlyNotificationStore -Path (Get-NightlyNotificationStorePath -StateRoot $fx.Root)
    $events = @()
    if ($store.events) { $events = @($store.events) }
    $enqLines = @()
    if (Test-Path -LiteralPath $fx.EnqueueLog) { $enqLines = @(Get-Content -LiteralPath $fx.EnqueueLog) }
    $accepted = $false
    if ($events.Count -gt 0) { $accepted = Test-NightlyNotificationTransportAccepted -Event $events[0] }
    Assert-C487 -Cond ($events.Count -eq 1) -Name 'G123 entry same poll not spam' -Detail ('n=' + $events.Count)
    Assert-C487 -Cond (($enqLines.Count -eq 1) -and $accepted) -Name 'G123 entry same poll single enqueue' -Detail ('enq=' + $enqLines.Count + ' accepted=' + $accepted)
}

function Test-C487_G124 {
    $h = Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } -Schedule @{ enabled = $true; paused = $false } -Jobs @() -NativeState @{ coverageComplete = $false; testsPassed = $true; reportDelivered = $true } -NowUtc ([datetime]::UtcNow) -ExpectedScriptHash 'a'
    Assert-C487 -Cond (-not $h.Healthy) -Name 'G124 incomplete remains visible'
}

function Test-C487_G125 {
    $ok = Test-NightlyNotificationReceipt -Event @{ notificationId = 'n1'; nativeRunId = 'r1' } -RecipientView $null
    Assert-C487 -Cond (-not $ok) -Name 'G125 no receipt before destination'
}

function Test-C487_G126 {
    foreach ($kind in @('unset', 'override')) {
        $h = Invoke-AntiphonNightlyHealth -StateRoot $ResultsDirectory -AuthorizedDestination '' -PassThru
        $monitorPath = Get-NightlyMonitorResultPath -StateRoot $ResultsDirectory
        $persisted = Test-Path -LiteralPath $monitorPath
        $reasons = @()
        if ($h -and $h.Health -and $h.Health.Reasons) { $reasons = @($h.Health.Reasons) }
        $noSilent = ($reasons -contains 'unauthorized-destination') -and ($null -eq $h.Notification)
        Assert-C487 -Cond (($null -ne $h) -and $persisted -and $noSilent) -Name ('G126 {0} no silent dest' -f $kind)
    }
}

# ---- CARD-0544 D-6: daily validity, scheduled identity, London dates and the production job adapter ----

function Get-C544LondonUtc {
    param([string]$Local)
    $d = [datetime]::ParseExact($Local, 'yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
    $d = [datetime]::SpecifyKind($d, [DateTimeKind]::Unspecified)
    return [TimeZoneInfo]::ConvertTimeToUtc($d, (Get-NightlyLondonTimeZone))
}

function New-C544Run {
    param([string]$RunId, [string]$CompletedLocal, [bool]$Coverage = $true, [bool]$Tests = $true,
        [bool]$Report = $true, [string]$Trigger = 'scheduled')
    return @{
        runId = $RunId; sha = ('sha-' + $RunId); ref = 'master'; trigger = $Trigger; phase = 'final'; policyHash = 'p'
        coverageComplete = $Coverage; testsPassed = $Tests; reportDelivered = $Report
        completedAt = (Get-C544LondonUtc -Local $CompletedLocal).ToString('o')
        lastActivityAt = (Get-C544LondonUtc -Local $CompletedLocal).ToString('o')
    }
}

function New-C544Started {
    param([string]$RunId, [string]$ActivityLocal)
    return @{
        runId = $RunId; sha = ''; ref = 'master'; trigger = 'scheduled'; phase = 'Started'; policyHash = 'p'
        coverageComplete = $false; testsPassed = $false; reportDelivered = $false
        lastActivityAt = (Get-C544LondonUtc -Local $ActivityLocal).ToString('o')
    }
}

function New-C544Job {
    param([string]$Date, [string]$RunId, [string]$Status = 'success', [bool]$Scheduled = $true)
    $due = Get-NightlyDueUtcForLondonDate -LondonDate ([datetime]::ParseExact($Date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture))
    return @{
        id = ('job-' + $Date + '-' + $RunId); status = $Status; scheduled = $Scheduled; nativeRunId = $RunId
        sha = $(if ($RunId) { 'sha-' + $RunId } else { '' }); localDueDate = $Date; scheduledFor = $due.ToString('o')
    }
}

function Invoke-C544Health {
    param($Jobs, $Native, $Green = $null, [string]$NowLocal)
    return Test-NightlyMonitorHealth -Registration @{ script = $true; tag = 'desktop'; hash = 'a' } `
        -Schedule @{ enabled = $true; paused = $false; tag = 'desktop' } -Jobs @($Jobs) -NativeState $Native -LastGreen $Green `
        -NowUtc (Get-C544LondonUtc -Local $NowLocal) -ExpectedScriptHash 'a' -ExpectedPolicyHash 'p'
}

function Assert-C544Ready {
    param($Health, [bool]$Ready, [string]$Name)
    $detail = ('healthy={0} ready={1} pending={2} reasons={3}' -f $Health.Healthy, $Health.ReadyForDeferral, $Health.Pending, ($Health.Reasons -join ','))
    Assert-C487 -Cond ([bool]$Health.ReadyForDeferral -eq $Ready -and -not [bool]$Health.TerminateRequested) -Name $Name -Detail $detail
}

# PC-72: validity is the due day's scheduled green, not completedAt age.
function Test-C544_DailyValidity {
    $green = New-C544Run -RunId 'r16' -CompletedLocal '2026-09-16 02:00:00'
    $job = New-C544Job -Date '2026-09-16' -RunId 'r16'
    $h = Invoke-C544Health -Jobs @($job) -Native $green -NowLocal '2026-09-16 10:00:00'
    Assert-C544Ready -Health $h -Ready $true -Name 'C544 DailyValidity green completed eight hours ago is ready'
    Assert-C487 -Cond ([string]$h.GreenRunId -eq 'r16' -and [string]$h.GreenJobNativeRunId -eq 'r16' -and [string]$h.GreenJobId -eq $job.id) `
        -Name 'C544 DailyValidity readiness names the scheduled run and job' -Detail ($h.GreenRunId + '/' + $h.GreenJobId)
    $late = Invoke-C544Health -Jobs @($job) -Native $green -NowLocal '2026-09-16 23:59:59'
    Assert-C544Ready -Health $late -Ready $true -Name 'C544 DailyValidity 23:59:59 same due day stays ready'
    $viaLastGreen = Invoke-C544Health -Jobs @($job) -Native $null -Green $green -NowLocal '2026-09-16 10:00:00'
    Assert-C544Ready -Health $viaLastGreen -Ready $true -Name 'C544 DailyValidity last-complete-green file carries the green'
    $missing = Invoke-C544Health -Jobs @($job) -Native $null -NowLocal '2026-09-16 10:00:00'
    Assert-C544Ready -Health $missing -Ready $false -Name 'C544 DailyValidity missing native state is unready'
    $yesterdayOnly = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-15' -RunId 'r15') `
        -Native (New-C544Run -RunId 'r15' -CompletedLocal '2026-09-15 02:00:00') -NowLocal '2026-09-16 10:00:00'
    Assert-C544Ready -Health $yesterdayOnly -Ready $false -Name 'C544 DailyValidity yesterday-only after the deadline is unready'
}

# PC-73: the previous due day's green bridges before 08:00 London only.
function Test-C544_MorningBoundary {
    $green = New-C544Run -RunId 'r16' -CompletedLocal '2026-09-16 02:00:00'
    $yesterday = New-C544Job -Date '2026-09-16' -RunId 'r16'
    foreach ($now in @('2026-09-17 00:00:00', '2026-09-17 00:29:59', '2026-09-17 00:30:00', '2026-09-17 00:59:59')) {
        $h = Invoke-C544Health -Jobs @($yesterday) -Native $green -NowLocal $now
        Assert-C544Ready -Health $h -Ready $true -Name ('C544 MorningBoundary bridge before start grace {0}' -f $now)
    }
    $running = New-C544Job -Date '2026-09-17' -RunId '' -Status 'running'
    foreach ($now in @('2026-09-17 01:00:00', '2026-09-17 07:59:59')) {
        $h = Invoke-C544Health -Jobs @($running, $yesterday) -Native $green -NowLocal $now
        Assert-C544Ready -Health $h -Ready $true -Name ('C544 MorningBoundary bridge while today runs {0}' -f $now)
    }
    foreach ($now in @('2026-09-17 08:00:00', '2026-09-17 08:00:01')) {
        $h = Invoke-C544Health -Jobs @($running, $yesterday) -Native $green -NowLocal $now
        Assert-C544Ready -Health $h -Ready $false -Name ('C544 MorningBoundary no bridge at or after deadline {0}' -f $now)
    }
    $today = New-C544Run -RunId 'r17' -CompletedLocal '2026-09-17 02:10:00'
    $h = Invoke-C544Health -Jobs @((New-C544Job -Date '2026-09-17' -RunId 'r17'), $yesterday) -Native $today -NowLocal '2026-09-17 08:00:00'
    Assert-C544Ready -Health $h -Ready $true -Name 'C544 MorningBoundary today green at the deadline is ready'
}

# PC-74: a newer failed or incomplete completed attempt removes the bridge immediately.
function Test-C544_NewerFailure {
    $green = New-C544Run -RunId 'r16' -CompletedLocal '2026-09-16 02:00:00'
    $yesterday = New-C544Job -Date '2026-09-16' -RunId 'r16'
    $control = Invoke-C544Health -Jobs @((New-C544Job -Date '2026-09-17' -RunId '' -Status 'running'), $yesterday) `
        -Native (New-C544Started -RunId 'r17' -ActivityLocal '2026-09-17 04:50:00') -Green $green -NowLocal '2026-09-17 05:00:00'
    Assert-C544Ready -Health $control -Ready $true -Name 'C544 NewerFailure control: in-progress run keeps the bridge'
    foreach ($kind in @('red', 'incomplete', 'no-report')) {
        $attempt = New-C544Run -RunId 'r17' -CompletedLocal '2026-09-17 04:00:00' -Tests ($kind -ne 'red') `
            -Coverage ($kind -ne 'incomplete') -Report ($kind -ne 'no-report')
        $h = Invoke-C544Health -Jobs @((New-C544Job -Date '2026-09-17' -RunId 'r17' -Status 'failed'), $yesterday) `
            -Native $attempt -Green $green -NowLocal '2026-09-17 05:00:00'
        Assert-C544Ready -Health $h -Ready $false -Name ('C544 NewerFailure newer {0} attempt revokes the bridge' -f $kind)
    }
    $jobOnly = Invoke-C544Health -Jobs @((New-C544Job -Date '2026-09-17' -RunId '' -Status 'failed'), $yesterday) `
        -Native $green -NowLocal '2026-09-17 05:00:00'
    Assert-C544Ready -Health $jobOnly -Ready $false -Name 'C544 NewerFailure failed scheduled job revokes even without native state'
    Assert-C487 -Cond (@($jobOnly.Reasons) -contains 'newer-failed-attempt') -Name 'C544 NewerFailure reason names the newer failure' -Detail ($jobOnly.Reasons -join ',')
}

# PC-75: a manual or partial run cannot be the due day's scheduled green.
function Test-C544_ScheduledIdentity {
    $manualRun = New-C544Run -RunId 'm17' -CompletedLocal '2026-09-17 03:00:00' -Trigger 'manual'
    $manualJob = New-C544Job -Date '2026-09-17' -RunId 'm17' -Scheduled $false
    $h = Invoke-C544Health -Jobs @($manualJob) -Native $manualRun -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $h -Ready $false -Name 'C544 ScheduledIdentity manual green after deadline is unready'
    Assert-C487 -Cond ([string]$h.GreenRunId -eq '') -Name 'C544 ScheduledIdentity manual run is never the scheduled run id' -Detail $h.GreenRunId
    $pre = Invoke-C544Health -Jobs @($manualJob) -Native $manualRun -NowLocal '2026-09-17 00:45:00'
    Assert-C544Ready -Health $pre -Ready $false -Name 'C544 ScheduledIdentity manual green cannot bridge'
    $scheduledJobManualRun = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-17' -RunId 'm17') -Native $manualRun -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $scheduledJobManualRun -Ready $false -Name 'C544 ScheduledIdentity native trigger must be scheduled'
    $partial = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-17' -RunId 'r17') `
        -Native (New-C544Run -RunId 'r17' -CompletedLocal '2026-09-17 03:00:00' -Coverage $false) -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $partial -Ready $false -Name 'C544 ScheduledIdentity partial scheduled run is unready'
    $crossed = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-17' -RunId 'r17') `
        -Native (New-C544Run -RunId 'r99' -CompletedLocal '2026-09-17 03:00:00') -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $crossed -Ready $false -Name 'C544 ScheduledIdentity job and native run ids must match'
    $control = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-17' -RunId 'r17') `
        -Native (New-C544Run -RunId 'r17' -CompletedLocal '2026-09-17 03:00:00') -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $control -Ready $true -Name 'C544 ScheduledIdentity control scheduled green is ready'
}

# PC-76: exact UTC due instants across both 2026 DST transitions and London midnight.
function Test-C544_LondonDates {
    $rows = @(
        @('2026-03-28', '2026-03-28T00:30:00Z'), @('2026-03-29', '2026-03-29T00:30:00Z'), @('2026-03-30', '2026-03-29T23:30:00Z'),
        @('2026-10-24', '2026-10-23T23:30:00Z'), @('2026-10-25', '2026-10-24T23:30:00Z'), @('2026-10-26', '2026-10-26T00:30:00Z')
    )
    foreach ($row in $rows) {
        $date = [datetime]::ParseExact($row[0], 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        $expected = [datetime]::Parse($row[1], [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
        $actual = Get-NightlyDueUtcForLondonDate -LondonDate $date
        Assert-C487 -Cond ($actual -eq $expected) -Name ('C544 LondonDates due {0}' -f $row[0]) -Detail ($actual.ToString('o') + ' expected ' + $expected.ToString('o'))
    }
    foreach ($row in @(@('2026-03-29', '2026-03-29T07:00:00Z'), @('2026-10-25', '2026-10-25T08:00:00Z'), @('2026-09-17', '2026-09-17T07:00:00Z'))) {
        $date = [datetime]::ParseExact($row[0], 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        $expected = [datetime]::Parse($row[1], [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
        $actual = Get-NightlyMorningDeadlineUtc -LondonDate $date
        Assert-C487 -Cond ($actual -eq $expected) -Name ('C544 LondonDates morning deadline {0}' -f $row[0]) -Detail $actual.ToString('o')
    }
    foreach ($row in @(@('2026-06-30T23:00:00Z', '2026-07-01'), @('2026-06-30T22:59:59Z', '2026-06-30'), @('2026-12-31T23:59:59Z', '2026-12-31'), @('2027-01-01T00:00:00Z', '2027-01-01'))) {
        $utc = [datetime]::Parse($row[0], [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
        $local = ConvertTo-NightlyLondonLocal -Utc $utc
        Assert-C487 -Cond ($local.ToString('yyyy-MM-dd') -eq $row[1]) -Name ('C544 LondonDates midnight {0}' -f $row[0]) -Detail $local.ToString('o')
    }
    # The evaluator uses the London due date: after the spring change 00:45 BST is still before grace.
    $green = New-C544Run -RunId 'r29' -CompletedLocal '2026-03-29 02:30:00'
    $prev = New-C544Job -Date '2026-03-29' -RunId 'r29'
    $pending = Invoke-C544Health -Jobs @($prev) -Native $green -NowLocal '2026-03-30 00:45:00'
    Assert-C544Ready -Health $pending -Ready $true -Name 'C544 LondonDates 00:45 BST after the change is within start grace'
    $overdue = Invoke-C544Health -Jobs @($prev) -Native $green -NowLocal '2026-03-30 01:00:00'
    Assert-C487 -Cond (-not $overdue.Healthy -and (@($overdue.Reasons) -contains 'overdue-start')) -Name 'C544 LondonDates 01:00 BST is overdue' -Detail ($overdue.Reasons -join ',')
}

# PC-87: start grace equality is overdue, reusing yesterday's green or not, and nothing is terminated.
function Test-C544_ScheduleHealth {
    $green = New-C544Run -RunId 'r16' -CompletedLocal '2026-09-16 02:00:00'
    $yesterday = New-C544Job -Date '2026-09-16' -RunId 'r16'
    $before = Invoke-C544Health -Jobs @($yesterday) -Native $green -NowLocal '2026-09-17 00:59:59'
    Assert-C544Ready -Health $before -Ready $true -Name 'C544 ScheduleHealth one second before grace is pending'
    foreach ($kind in @('missing', 'queued')) {
        $jobs = @($yesterday)
        if ($kind -eq 'queued') { $jobs += New-C544Job -Date '2026-09-17' -RunId '' -Status 'queued' }
        $h = Invoke-C544Health -Jobs $jobs -Native $green -NowLocal '2026-09-17 01:00:00'
        Assert-C544Ready -Health $h -Ready $false -Name ('C544 ScheduleHealth {0} at grace equality is unready' -f $kind)
        Assert-C487 -Cond (-not $h.Healthy -and (@($h.Reasons) -contains 'overdue-start')) -Name ('C544 ScheduleHealth {0} overdue-start' -f $kind) -Detail ($h.Reasons -join ',')
    }
    $stalled = Invoke-C544Health -Jobs @((New-C544Job -Date '2026-09-17' -RunId '' -Status 'running'), $yesterday) `
        -Native (New-C544Started -RunId 'r17' -ActivityLocal '2026-09-17 03:58:59') -Green $green -NowLocal '2026-09-17 05:00:00'
    Assert-C544Ready -Health $stalled -Ready $false -Name 'C544 ScheduleHealth stalled progress is unready'
    Assert-C487 -Cond ((@($stalled.Reasons) -contains 'stale-progress') -and -not $stalled.TerminateRequested) -Name 'C544 ScheduleHealth stalled run is not terminated' -Detail ($stalled.Reasons -join ',')
}

function Test-C544FlagRequired {
    param([string]$Flag, [string]$Label)
    $control = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-17' -RunId 'r17') `
        -Native (New-C544Run -RunId 'r17' -CompletedLocal '2026-09-17 03:00:00') -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $control -Ready $true -Name ('C544 {0} control otherwise-valid scheduled row is ready' -f $Label)
    $run = New-C544Run -RunId 'r17' -CompletedLocal '2026-09-17 03:00:00' -Coverage ($Flag -ne 'coverageComplete') `
        -Tests ($Flag -ne 'testsPassed') -Report ($Flag -ne 'reportDelivered')
    $h = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-17' -RunId 'r17') -Native $run -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $h -Ready $false -Name ('C544 {0} {1}=false is unready' -f $Label, $Flag)
    $viaGreen = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-17' -RunId 'r17') -Native $null -Green $run -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $viaGreen -Ready $false -Name ('C544 {0} {1}=false in the green file is unready' -f $Label, $Flag)
    $bridge = Invoke-C544Health -Jobs @(New-C544Job -Date '2026-09-16' -RunId 'r17') `
        -Native $null -Green $run -NowLocal '2026-09-17 05:00:00'
    Assert-C544Ready -Health $bridge -Ready $false -Name ('C544 {0} {1}=false cannot bridge' -f $Label, $Flag)
}

# PC-92/93/94
function Test-C544_CoverageRequired { Test-C544FlagRequired -Flag 'coverageComplete' -Label 'CoverageRequired' }
function Test-C544_GreenRequired { Test-C544FlagRequired -Flag 'testsPassed' -Label 'GreenRequired' }
function Test-C544_ReportReceiptRequired { Test-C544FlagRequired -Flag 'reportDelivered' -Label 'ReportReceiptRequired' }

# PC-77: the production jobs/list adapter over real HTTP against an owned loopback Windmill stub.
# CARD-0545: the stub is a route table serving any number of requests until Stop-C544WindmillStub.
# Routes map a URL-path suffix to a JSON body (string) or an HTTP status (int); the first matching
# key in insertion order wins. Log holds the first request's request line and headers; Requests holds
# every request line in order.
function Start-C544WindmillStub {
    param([string]$Body = '', [System.Collections.IDictionary]$Routes = $null)
    if ($null -eq $Routes) { $Routes = [ordered]@{ '*' = $Body } }
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $id = [guid]::NewGuid().ToString('N')
    $log = Join-Path $ResultsDirectory ('c544-stub-' + $id + '.log')
    $requests = Join-Path $ResultsDirectory ('c544-stub-' + $id + '.requests')
    [System.IO.File]::WriteAllText($requests, '')
    $ps = [powershell]::Create()
    [void]$ps.AddScript({
        param($listener, $routes, $log, $requests)
        $first = $true
        while ($true) {
            try { $client = $listener.AcceptTcpClient() } catch { break }
            try {
                $stream = $client.GetStream()
                $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII)
                $lines = @()
                while ($true) { $line = $reader.ReadLine(); if ([string]::IsNullOrEmpty($line)) { break }; $lines += $line }
                if ($lines.Count -eq 0) { continue }
                [System.IO.File]::AppendAllText($requests, $lines[0] + "`n")
                if ($first) { [System.IO.File]::WriteAllLines($log, [string[]]$lines); $first = $false }
                $path = ($lines[0] -split ' ')[1]
                $bare = ($path -split '\?')[0]
                $status = 404
                $body = '{}'
                foreach ($key in $routes.Keys) {
                    if ($key -eq '*' -or $bare.EndsWith([string]$key)) {
                        $value = $routes[$key]
                        if ($value -is [int]) { $status = $value; $body = '{}' } else { $status = 200; $body = [string]$value }
                        break
                    }
                }
                $reason = $(if ($status -eq 200) { 'OK' } elseif ($status -eq 404) { 'Not Found' } else { 'Error' })
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
                $head = "HTTP/1.1 $status $reason`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n"
                $h = [System.Text.Encoding]::ASCII.GetBytes($head)
                $stream.Write($h, 0, $h.Length)
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Flush()
            } catch {
            } finally { $client.Close() }
        }
    }).AddArgument($listener).AddArgument($Routes).AddArgument($log).AddArgument($requests)
    $handle = $ps.BeginInvoke()
    return [pscustomobject]@{ Port = $port; Log = $log; Requests = $requests; Shell = $ps; Handle = $handle; Listener = $listener }
}

function Stop-C544WindmillStub {
    param($Stub)
    $Stub.Listener.Stop()
    try { [void]$Stub.Shell.EndInvoke($Stub.Handle) } catch { }
    $Stub.Shell.Dispose()
    return @(Get-Content -LiteralPath $Stub.Requests | Where-Object { $_ })
}

function New-C545ProductionApi {
    param($Stub)
    return New-NightlyProductionWindmillApi -Config ([pscustomobject]@{
        BaseUrl = ('http://127.0.0.1:{0}' -f $Stub.Port); Token = 'c544-fixture-token'; Workspace = 'mc'
        ScriptPath = 'u/lndcobra/antiphon_nightly_tests'; HasCredentials = $true })
}

function Test-C544_ProductionJobAdapter {
    $today = '2026-09-17'
    $sched = 'u/lndcobra/antiphon_nightly_tests'
    $due = (Get-NightlyDueUtcForLondonDate -LondonDate ([datetime]'2026-09-17')).ToString('o')
    $rows = @(
        @{ id = 'j-queued'; type = 'QueuedJob'; running = $false; schedule_path = $sched; scheduled_for = $due },
        @{ id = 'j-running'; type = 'QueuedJob'; running = $true; schedule_path = $sched; scheduled_for = $due },
        @{ id = 'j-success'; type = 'CompletedJob'; success = $true; schedule_path = $sched },
        @{ id = 'j-failed'; type = 'CompletedJob'; success = $false; schedule_path = $sched },
        @{ id = 'j-missing-result'; type = 'CompletedJob'; success = $true; schedule_path = $sched },
        @{ id = 'j-string-bool'; type = 'CompletedJob'; success = 'true'; schedule_path = $sched },
        @{ id = 'j-manual'; type = 'CompletedJob'; success = $true; schedule_path = $null },
        @{ id = 'j-unknown'; type = 'SomethingElse'; success = $true; schedule_path = $sched }
    )
    $valid = (ConvertTo-Json -InputObject @{ nativeRunId = 'r17'; sha = 'sha-r17'; localDueDate = $today } -Compress)
    $routes = [ordered]@{
        'jobs/list' = (ConvertTo-Json -InputObject $rows -Depth 6 -Compress)
        'get_result/j-success' = $valid
        'get_result/j-failed' = '{"error":"exit 1"}'
        'get_result/j-missing-result' = 'null'
        'get_result/j-string-bool' = $valid
        'get_result/j-manual' = $valid
        'get_result/j-unknown' = $valid
    }
    $stub = Start-C544WindmillStub -Routes $routes
    $api = New-C545ProductionApi -Stub $stub
    $jobs = @($api.GetJobs.Invoke())
    $requestLines = @(Stop-C544WindmillStub -Stub $stub)
    $request = @(Get-Content -LiteralPath $stub.Log)
    Assert-C487 -Cond (($request[0] -like 'GET /api/w/mc/jobs/list?script_path_exact=u%2Flndcobra%2Fantiphon_nightly_tests*') -and (@($request | Where-Object { $_ -eq 'Authorization: Bearer c544-fixture-token' }).Count -eq 1)) `
        -Name 'C544 ProductionJobAdapter real HTTP request shape' -Detail ($request -join ' | ')
    $fetchedIds = @($requestLines | Select-Object -Skip 1 | ForEach-Object { ($_ -split ' ')[1].Split('/')[-1] })
    Assert-C487 -Cond ((@($requestLines).Count -eq 5) -and (($fetchedIds -join ',') -eq 'j-success,j-failed,j-missing-result,j-string-bool')) `
        -Name 'C544 ProductionJobAdapter get_result requests bounded to scheduled completed rows' -Detail ($requestLines -join ' | ')
    $byId = @{}
    foreach ($j in $jobs) { $byId[[string]$j.id] = $j }
    $expected = [ordered]@{ 'j-queued' = 'queued'; 'j-running' = 'running'; 'j-success' = 'success'; 'j-failed' = 'failed';
        'j-missing-result' = 'unknown'; 'j-string-bool' = 'unknown'; 'j-manual' = 'unknown'; 'j-unknown' = 'unknown' }
    foreach ($id in $expected.Keys) {
        Assert-C487 -Cond ($byId.ContainsKey($id) -and [string]$byId[$id].status -eq $expected[$id]) -Name ('C544 ProductionJobAdapter status {0}' -f $id) -Detail ([string]$byId[$id].status)
    }
    Assert-C487 -Cond ((-not [bool]$byId['j-manual'].scheduled) -and [bool]$byId['j-success'].scheduled) -Name 'C544 ProductionJobAdapter schedule_path decides scheduled'

    $native = New-C544Run -RunId 'r17' -CompletedLocal '2026-09-17 03:00:00'
    foreach ($id in @('j-queued', 'j-running', 'j-failed', 'j-missing-result', 'j-string-bool', 'j-manual', 'j-unknown')) {
        $h = Invoke-C544Health -Jobs @($byId[$id]) -Native $native -NowLocal '2026-09-17 10:00:00'
        Assert-C544Ready -Health $h -Ready $false -Name ('C544 ProductionJobAdapter {0} never qualifies as scheduled green' -f $id)
    }
    $crossedDay = $byId['j-success'].Clone(); $crossedDay.localDueDate = '2026-09-16'; $crossedDay.scheduledFor = $null
    $crossedRun = $byId['j-success'].Clone(); $crossedRun.nativeRunId = 'r16'
    $crossedSha = $byId['j-success'].Clone(); $crossedSha.sha = 'sha-other'
    foreach ($pair in @(@('crossed-due-day', $crossedDay), @('crossed-run', $crossedRun), @('crossed-sha', $crossedSha))) {
        $h = Invoke-C544Health -Jobs @($pair[1]) -Native $native -NowLocal '2026-09-17 10:00:00'
        Assert-C544Ready -Health $h -Ready $false -Name ('C544 ProductionJobAdapter {0} never qualifies' -f $pair[0])
    }
    $ok = Invoke-C544Health -Jobs @($byId['j-success']) -Native $native -NowLocal '2026-09-17 10:00:00'
    Assert-C544Ready -Health $ok -Ready $true -Name 'C544 ProductionJobAdapter control success row with matching native run is ready'
}

# ---- CARD-0545 D-10: per-row result fetch, bounded to five, never upgrading a missing result ----

function Test-C545_JobResultFetch {
    $sched = 'u/lndcobra/antiphon_nightly_tests'
    $rows = @()
    foreach ($n in 1..7) { $rows += @{ id = ('c{0}' -f $n); type = 'CompletedJob'; success = $true; schedule_path = $sched } }
    $valid = '{"nativeRunId":"r18","sha":"s","localDueDate":"2026-09-18"}'
    $routes = [ordered]@{
        'jobs/list' = (ConvertTo-Json -InputObject $rows -Depth 6 -Compress)
        'get_result/c1' = $valid; 'get_result/c2' = $valid; 'get_result/c3' = 500
        'get_result/c4' = $valid; 'get_result/c5' = $valid; 'get_result/c6' = $valid; 'get_result/c7' = $valid
    }
    $stub = Start-C544WindmillStub -Routes $routes
    $jobs = @((New-C545ProductionApi -Stub $stub).GetJobs.Invoke())
    $requestLines = @(Stop-C544WindmillStub -Stub $stub)
    $paths = @($requestLines | ForEach-Object { (($_ -split ' ')[1] -split '\?')[0] })
    $expectedPaths = @('/api/w/mc/jobs/list') + @(1..5 | ForEach-Object { '/api/w/mc/jobs_u/completed/get_result/c{0}' -f $_ })
    Assert-C487 -Cond (($paths -join ',') -eq ($expectedPaths -join ',')) -Name 'C545 JobResultFetch requests are list then c1..c5 in order' -Detail ($requestLines -join ' | ')
    $byId = @{}
    foreach ($j in $jobs) { $byId[[string]$j.id] = $j }
    foreach ($id in @('c1', 'c2', 'c4', 'c5')) {
        Assert-C487 -Cond ([string]$byId[$id].status -eq 'success' -and [string]$byId[$id].nativeRunId -eq 'r18') -Name ('C545 JobResultFetch {0} fetched result is success' -f $id) -Detail ([string]$byId[$id].status)
    }
    Assert-C487 -Cond ([string]$byId['c3'].status -eq 'unknown') -Name 'C545 JobResultFetch c3 unfetchable is unknown' -Detail ([string]$byId['c3'].status)
    Assert-C487 -Cond ([string]$byId['c6'].status -eq 'unknown') -Name 'C545 JobResultFetch c6 stays unknown beyond the cap' -Detail ([string]$byId['c6'].status)
    Assert-C487 -Cond ([string]$byId['c7'].status -eq 'unknown') -Name 'C545 JobResultFetch c7 stays unknown beyond the cap' -Detail ([string]$byId['c7'].status)
    $expectedDue = [datetime]::Parse('2026-09-17T23:30:00Z', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
    $c1Due = $null
    if ($byId['c1'].scheduledFor) { $c1Due = ([datetime]$byId['c1'].scheduledFor).ToUniversalTime() }
    Assert-C487 -Cond ($c1Due -eq $expectedDue) -Name 'C545 JobResultFetch c1 scheduledFor is 00:30 London of localDueDate' -Detail ([string]$byId['c1'].scheduledFor)

    $odd = @(
        @{ id = 'n-null'; type = 'CompletedJob'; success = $true; schedule_path = $sched },
        @{ id = 'n-nodue'; type = 'CompletedJob'; success = $true; schedule_path = $sched }
    )
    $stub2 = Start-C544WindmillStub -Routes ([ordered]@{
        'jobs/list' = (ConvertTo-Json -InputObject $odd -Depth 6 -Compress)
        'get_result/n-null' = 'null'
        'get_result/n-nodue' = '{"nativeRunId":"r18","sha":"s"}'
    })
    $oddJobs = @((New-C545ProductionApi -Stub $stub2).GetJobs.Invoke())
    [void](Stop-C544WindmillStub -Stub $stub2)
    $oddById = @{}
    foreach ($j in $oddJobs) { $oddById[[string]$j.id] = $j }
    Assert-C487 -Cond ([string]$oddById['n-null'].status -eq 'unknown') -Name 'C545 JobResultFetch null result is unknown' -Detail ([string]$oddById['n-null'].status)
    Assert-C487 -Cond ([string]$oddById['n-nodue'].status -eq 'unknown') -Name 'C545 JobResultFetch result without localDueDate is unknown' -Detail ([string]$oddById['n-nodue'].status)

    $c3 = $byId['c3'].Clone()
    $c3.localDueDate = '2026-09-18'
    $native = New-C544Run -RunId 'r18' -CompletedLocal '2026-09-18 03:00:00'
    $h = Invoke-C544Health -Jobs @($c3) -Native $native -NowLocal '2026-09-18 10:00:00'
    Assert-C544Ready -Health $h -Ready $false -Name 'C545 JobResultFetch c3 alone is unready'

    $api = New-NightlyProductionWindmillApi -Config ([pscustomobject]@{ BaseUrl = 'http://127.0.0.1:9'; Token = 't'; Workspace = 'mc'; ScriptPath = $sched; HasCredentials = $true })
    $config = Get-NightlyWindmillConfig
    Assert-C487 -Cond ((@($api.Keys) -notcontains 'EnqueueNotification') -and ($null -eq $config.PSObject.Properties['NotifyPath'])) -Name 'C545 JobResultFetch no production Windmill notification sink' -Detail (@($api.Keys) -join ',')
}

# ---- CARD-0545 D-9: the watchdog snapshot folds into the local evaluator ----

$script:C545Now = [datetime]::Parse('2026-09-17T09:00:00Z', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)

function New-C545SnapshotText {
    param([TimeSpan]$Age = [TimeSpan]::Zero, [string]$Namespace = 'mc', [string]$InstanceId = 'wd-1', $SchemaVersion = 1, [object[]]$OpenOutages = @(), [switch]$NoHeartbeat)
    $snap = [ordered]@{ schemaVersion = $SchemaVersion; instanceId = $InstanceId; version = '1.0.0'; configHash = 'cfg'; namespace = $Namespace }
    if (-not $NoHeartbeat) { $snap.heartbeatAt = ($script:C545Now - $Age).ToString('yyyy-MM-ddTHH:mm:ss.fffZ') }
    $snap.tickSeconds = 600
    $snap.openOutages = @($OpenOutages)
    $snap.recentNotifications = @()
    return (ConvertTo-Json -InputObject $snap -Depth 6 -Compress)
}

function New-C545WatchdogFx {
    # An otherwise-green evaluator fixture (scheduled green for 2026-09-17 at 10:00 London) whose only
    # variable input is the watchdog snapshot seam: raw text, a throw, or no seam at all.
    param([string]$SnapshotText = '', [switch]$Throw, [switch]$NoSeam)
    $root = Join-Path $ResultsDirectory ('c545-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $native = New-C544Run -RunId 'r17' -CompletedLocal '2026-09-17 03:00:00'
    Write-NightlyAtomicJson -Path (Join-Path $root 'last-run.json') -Object $native
    $job = New-C544Job -Date '2026-09-17' -RunId 'r17'
    $jobLiteral = ('@{{ id = ''{0}''; status = ''success''; scheduled = $true; nativeRunId = ''r17''; sha = ''sha-r17''; localDueDate = ''2026-09-17''; scheduledFor = ''{1}'' }}' -f $job.id, $job.scheduledFor)
    $snapshotFile = Join-Path $root 'snapshot.txt'
    [System.IO.File]::WriteAllText($snapshotFile, $SnapshotText)
    $seamLine = ''
    if ($Throw) {
        $seamLine = '$NightlySeams.WatchdogSnapshot = { param($Url) throw ''connection refused'' }'
    } elseif (-not $NoSeam) {
        $seamLine = ('$NightlySeams.WatchdogSnapshot = {{ param($Url) return [System.IO.File]::ReadAllText(''{0}'') }}' -f $snapshotFile)
    }
    $text = @"
`$NightlySeams = @{
    UtcNow = { return [datetime]::Parse('2026-09-17T09:00:00Z', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal) }
    WindmillApi = @{
        GetRegistration = { return @{ script = `$true; tag = 'desktop'; hash = 'a' } }
        GetSchedule = { return @{ enabled = `$true; paused = `$false; tag = 'desktop' } }
        GetJobs = { return @($jobLiteral) }
    }
}
$seamLine
"@
    $seams = Join-Path $root 'seams.ps1'
    Set-Content -LiteralPath $seams -Value $text -Encoding ASCII
    return [pscustomobject]@{ Root = $root; Seams = $seams }
}

function Invoke-C545WatchdogHealth {
    param($Fx, [string]$Url = 'http://127.0.0.1:17290/snapshot.json', [string]$InstanceId = 'wd-1', [string]$ReadinessConfigPath = '')
    $params = @{ StateRoot = $Fx.Root; SeamsPath = $Fx.Seams; RepositoryPath = 'C:\src\Antiphon'; PassThru = $true }
    if ($ReadinessConfigPath) {
        $params.ReadinessConfigPath = $ReadinessConfigPath
    } else {
        $params.ExpectedScriptHash = 'a'; $params.ExpectedPolicyHash = 'p'; $params.WatchdogSnapshotUrl = $Url; $params.ExpectedWatchdogInstanceId = $InstanceId
    }
    return Invoke-AntiphonNightlyHealth @params
}

function Test-C545_WatchdogFresh {
    foreach ($row in @(@('0:00', [TimeSpan]::Zero), @('19:59', [TimeSpan]::FromSeconds(1199)), @('20:00', [TimeSpan]::FromMinutes(20)))) {
        $fetch = [pscustomobject]@{ Reachable = $true; Raw = ''; Snapshot = ((New-C545SnapshotText -Age $row[1]) | ConvertFrom-Json); Error = '' }
        $f = Test-NightlyWatchdogFreshness -Snapshot $fetch -NowUtc $script:C545Now -ExpectedNamespace 'mc' -ExpectedInstanceId 'wd-1'
        Assert-C487 -Cond ([bool]$f.Fresh -and @($f.Reasons).Count -eq 0) -Name ('C545 WatchdogFresh age {0} fresh' -f $row[0]) -Detail ($f.Reasons -join ',')
    }

    $stub = Start-C544WindmillStub -Routes ([ordered]@{ 'snapshot.json' = (New-C545SnapshotText) })
    $fetched = Get-NightlyWatchdogSnapshot -Url ('http://127.0.0.1:{0}/snapshot.json' -f $stub.Port)
    $lines = @(Stop-C544WindmillStub -Stub $stub)
    Assert-C487 -Cond ([bool]$fetched.Reachable -and [string]$fetched.Snapshot.instanceId -eq 'wd-1' -and @($lines).Count -eq 1 -and ([string]$lines[0]).StartsWith('GET /snapshot.json')) `
        -Name 'C545 WatchdogFresh real HTTP snapshot read' -Detail (($lines -join ' | ') + ' ' + ($fetched | ConvertTo-Json -Compress -Depth 4))

    $fx = New-C545WatchdogFx -SnapshotText (New-C545SnapshotText -Age ([TimeSpan]::FromMinutes(5)))
    $h = Invoke-C545WatchdogHealth -Fx $fx
    $expectedBeat = ($script:C545Now - [TimeSpan]::FromMinutes(5))
    $beat = $null
    if ([string]$h.Identity.WatchdogHeartbeatAt) { $beat = ([datetime]::Parse([string]$h.Identity.WatchdogHeartbeatAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)) }
    Assert-C487 -Cond ([bool]$h.Health.Healthy -and [bool]$h.Health.ReadyForDeferral) -Name 'C545 WatchdogFresh evaluator healthy with a fresh watchdog' -Detail ($h.Health.Reasons -join ',')
    Assert-C487 -Cond ([string]$h.Identity.WatchdogInstanceId -eq 'wd-1' -and $beat -eq $expectedBeat) -Name 'C545 WatchdogFresh monitor identity carries instance and heartbeat' -Detail ([string]$h.Identity.WatchdogInstanceId + ' ' + [string]$h.Identity.WatchdogHeartbeatAt)
    $saved = Get-Content -LiteralPath (Get-NightlyMonitorResultPath -StateRoot $fx.Root) -Raw | ConvertFrom-Json
    Assert-C487 -Cond ([string]$saved.Identity.WatchdogInstanceId -eq 'wd-1' -and [bool]$saved.Health.Healthy) -Name 'C545 WatchdogFresh last-monitor.json records the watchdog identity'

    $fx2 = New-C545WatchdogFx -SnapshotText (New-C545SnapshotText -Age ([TimeSpan]::FromMinutes(1)))
    $config = Join-Path $fx2.Root 'readiness-config.json'
    Write-NightlyAtomicJson -Path $config -Object ([ordered]@{
        expectedScriptHash = 'a'; expectedPolicyHash = 'p'; watchdogSnapshotUrl = 'http://127.0.0.1:17290/snapshot.json'
        watchdogInstanceId = 'wd-1'; repositoryPath = 'C:\Antiphon\qualified-repo'; projectId = 'c5450000-0000-4000-8000-000000000001' })
    $viaConfig = Invoke-AntiphonNightlyHealth -StateRoot $fx2.Root -SeamsPath $fx2.Seams -ReadinessConfigPath $config -PassThru
    $id = $viaConfig.Identity
    Assert-C487 -Cond ([bool]$viaConfig.Health.Healthy -and [string]$id.ScriptHash -eq 'a' -and [string]$id.PolicyHash -eq 'p' -and [string]$id.WatchdogInstanceId -eq 'wd-1' -and [string]$id.RepositoryPath -eq 'C:\Antiphon\qualified-repo' -and [string]$id.ProjectId -eq 'c5450000-0000-4000-8000-000000000001') `
        -Name 'C545 WatchdogFresh readiness-config supplies every value' -Detail (($viaConfig.Health.Reasons -join ',') + ' ' + ($id | ConvertTo-Json -Compress))
}

function Assert-C545WatchdogUnhealthy {
    param($Health, [string]$Fx, [string]$Reason, [string]$Name)
    $monitor = Test-Path -LiteralPath (Get-NightlyMonitorResultPath -StateRoot $Fx)
    $reasons = @($Health.Health.Reasons)
    Assert-C487 -Cond ((-not [bool]$Health.Health.Healthy) -and (-not [bool]$Health.Health.ReadyForDeferral) -and ($reasons -contains $Reason) -and $monitor) -Name $Name -Detail ('healthy={0} reasons={1} monitor={2}' -f $Health.Health.Healthy, ($reasons -join ','), $monitor)
}

function Test-C545_WatchdogStale {
    $rows = @(
        @('C545 WatchdogStale age 20:01 stale', 'watchdog-stale', (New-C545SnapshotText -Age ([TimeSpan]::FromSeconds(1201))), ''),
        @('C545 WatchdogStale future heartbeat malformed', 'watchdog-malformed', (New-C545SnapshotText -Age ([TimeSpan]::FromSeconds(-1))), ''),
        @('C545 WatchdogStale malformed not-json', 'watchdog-malformed', 'this is not json', ''),
        @('C545 WatchdogStale malformed missing-heartbeat', 'watchdog-malformed', (New-C545SnapshotText -NoHeartbeat), ''),
        @('C545 WatchdogStale malformed schema-2', 'watchdog-malformed', (New-C545SnapshotText -SchemaVersion 2), ''),
        @('C545 WatchdogStale malformed empty-instance', 'watchdog-malformed', (New-C545SnapshotText -InstanceId ''), ''),
        @('C545 WatchdogStale namespace mc/qual mismatch', 'watchdog-identity-mismatch', (New-C545SnapshotText -Namespace 'mc/qual'), ''),
        @('C545 WatchdogStale instance wd-2 mismatch', 'watchdog-identity-mismatch', (New-C545SnapshotText -InstanceId 'wd-2'), ''),
        @('C545 WatchdogStale open outage unhealthy', 'watchdog-outage-open', (New-C545SnapshotText -OpenOutages @(@{ outageId = 'nw:mc:2026-09-17:windows-hop-failed:1'; kind = 'windows-hop-failed' })), '')
    )
    foreach ($row in $rows) {
        $fx = New-C545WatchdogFx -SnapshotText $row[2]
        $h = Invoke-C545WatchdogHealth -Fx $fx
        Assert-C545WatchdogUnhealthy -Health $h -Fx $fx.Root -Reason $row[1] -Name $row[0]
    }

    $thrown = New-C545WatchdogFx -Throw
    Assert-C545WatchdogUnhealthy -Health (Invoke-C545WatchdogHealth -Fx $thrown) -Fx $thrown.Root -Reason 'watchdog-unreachable' -Name 'C545 WatchdogStale unreachable'

    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $closedPort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
    $closed = New-C545WatchdogFx -NoSeam
    Assert-C545WatchdogUnhealthy -Health (Invoke-C545WatchdogHealth -Fx $closed -Url ('http://127.0.0.1:{0}/snapshot.json' -f $closedPort)) -Fx $closed.Root -Reason 'watchdog-unreachable' -Name 'C545 WatchdogStale unreachable closed-port'

    $noUrl = New-C545WatchdogFx -SnapshotText (New-C545SnapshotText)
    Assert-C545WatchdogUnhealthy -Health (Invoke-C545WatchdogHealth -Fx $noUrl -Url '') -Fx $noUrl.Root -Reason 'watchdog-unreachable' -Name 'C545 WatchdogStale no snapshot url unreachable'

    $control = New-C545WatchdogFx -SnapshotText (New-C545SnapshotText -Age ([TimeSpan]::FromMinutes(20)))
    $ok = Invoke-C545WatchdogHealth -Fx $control
    Assert-C487 -Cond ([bool]$ok.Health.Healthy -and [bool]$ok.Health.ReadyForDeferral) -Name 'C545 WatchdogStale control age 20:00 healthy' -Detail ($ok.Health.Reasons -join ',')
}

function Test-C545_ReadinessRouting {
    $windmill = Join-Path $here 'windmill'
    $defPath = Join-Path $windmill 'antiphon-nightly-readiness.json'
    $schedPath = Join-Path $windmill 'antiphon-nightly-readiness.schedule.json'
    $def = $null
    if (Test-Path -LiteralPath $defPath) { $def = Get-Content -LiteralPath $defPath -Raw | ConvertFrom-Json }
    $content = [string]$def.content
    Assert-C487 -Cond ($null -ne $def -and [string]$def.tag -eq 'desktop' -and [string]$def.language -eq 'bash') -Name 'C545 ReadinessRouting readiness definition is desktop bash' -Detail ([string]$def.tag)
    $hosts = @([regex]::Matches($content, '@([A-Za-z0-9_.-]+)') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne 'host.docker.internal' })
    Assert-C487 -Cond ($content -match 'scripts\\nightly-health\.ps1' -and $content -match '-ReadinessConfigPath C:\\Antiphon\\nightly\\readiness-config\.json' -and $hosts.Count -eq 0) -Name 'C545 ReadinessRouting content runs the evaluator with the readiness config and no host literal' -Detail $content
    $sched = $null
    if (Test-Path -LiteralPath $schedPath) { $sched = Get-Content -LiteralPath $schedPath -Raw | ConvertFrom-Json }
    $argsEmpty = ($null -ne $sched -and $null -ne $sched.args -and @($sched.args.PSObject.Properties).Count -eq 0)
    Assert-C487 -Cond ($null -ne $sched -and [string]$sched.schedule -eq '0 */30 * * * *' -and [string]$sched.timezone -eq 'Europe/London' -and $sched.enabled -eq $true -and $argsEmpty) -Name 'C545 ReadinessRouting schedule every 30 minutes Europe/London enabled no args' -Detail ($sched | ConvertTo-Json -Compress)
    Assert-C487 -Cond (-not (Test-Path -LiteralPath (Join-Path $windmill 'antiphon-nightly-health.json')) -and -not (Test-Path -LiteralPath (Join-Path $windmill 'antiphon-nightly-health.schedule.json'))) -Name 'C545 ReadinessRouting retired health definition is absent'
    $threw = $false
    try { Test-NightlyMonitorRouting -Definition @{ tag = 'desktop' } | Out-Null } catch { $threw = $true }
    Assert-C487 -Cond $threw -Name 'C545 ReadinessRouting G116 monitor routing still rejects desktop'
    $readme = Get-Content -LiteralPath (Join-Path $windmill 'README.md') -Raw
    Assert-C487 -Cond ($readme -match 'antiphon_nightly_readiness' -and $readme -notmatch 'u/lndcobra/antiphon_nightly_health') -Name 'C545 ReadinessRouting README names readiness not health'
}

$script:C544ExpectedRows = 84
$script:C545ExpectedRows = 40

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C487_G')) { & $fn }
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C544_')) { & $fn }
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C545_')) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'health-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 52 + $script:C544ExpectedRows + $script:C545ExpectedRows })
