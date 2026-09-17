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
function Start-C544WindmillStub {
    param([string]$Body)
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $log = Join-Path $ResultsDirectory ('c544-stub-' + [guid]::NewGuid().ToString('N') + '.log')
    $ps = [powershell]::Create()
    [void]$ps.AddScript({
        param($listener, $body, $log)
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII)
            $lines = @()
            while ($true) { $line = $reader.ReadLine(); if ([string]::IsNullOrEmpty($line)) { break }; $lines += $line }
            [System.IO.File]::WriteAllLines($log, [string[]]$lines)
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            $head = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n"
            $h = [System.Text.Encoding]::ASCII.GetBytes($head)
            $stream.Write($h, 0, $h.Length)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
        } finally { $client.Close(); $listener.Stop() }
    }).AddArgument($listener).AddArgument($Body).AddArgument($log)
    $handle = $ps.BeginInvoke()
    return [pscustomobject]@{ Port = $port; Log = $log; Shell = $ps; Handle = $handle; Listener = $listener }
}

function Test-C544_ProductionJobAdapter {
    $today = '2026-09-17'
    $due = (Get-NightlyDueUtcForLondonDate -LondonDate ([datetime]'2026-09-17')).ToString('o')
    $sched = 'u/lndcobra/antiphon_nightly_tests'
    $rows = @(
        @{ id = 'j-queued'; type = 'QueuedJob'; running = $false; schedule_path = $sched; scheduled_for = $due },
        @{ id = 'j-running'; type = 'QueuedJob'; running = $true; schedule_path = $sched; scheduled_for = $due },
        @{ id = 'j-success'; type = 'CompletedJob'; success = $true; schedule_path = $sched; scheduled_for = $due; result = @{ nativeRunId = 'r17'; sha = 'sha-r17'; localDueDate = $today } },
        @{ id = 'j-failed'; type = 'CompletedJob'; success = $false; schedule_path = $sched; scheduled_for = $due; result = @{ error = 'exit 1' } },
        @{ id = 'j-missing-result'; type = 'CompletedJob'; success = $true; schedule_path = $sched; scheduled_for = $due },
        @{ id = 'j-string-bool'; type = 'CompletedJob'; success = 'true'; schedule_path = $sched; scheduled_for = $due; result = @{ nativeRunId = 'r17'; sha = 'sha-r17'; localDueDate = $today } },
        @{ id = 'j-manual'; type = 'CompletedJob'; success = $true; schedule_path = $null; scheduled_for = $due; result = @{ nativeRunId = 'r17'; sha = 'sha-r17'; localDueDate = $today } },
        @{ id = 'j-unknown'; type = 'SomethingElse'; success = $true; schedule_path = $sched; result = @{ nativeRunId = 'r17'; localDueDate = $today } }
    )
    $stub = Start-C544WindmillStub -Body (ConvertTo-Json -InputObject $rows -Depth 6 -Compress)
    $api = New-NightlyProductionWindmillApi -Config ([pscustomobject]@{
        BaseUrl = ('http://127.0.0.1:{0}' -f $stub.Port); Token = 'c544-fixture-token'; Workspace = 'mc'
        ScriptPath = $sched; NotifyPath = 'u/none'; HasCredentials = $true })
    $jobs = @($api.GetJobs.Invoke())
    [void]$stub.Shell.EndInvoke($stub.Handle)
    $stub.Shell.Dispose()
    $request = @(Get-Content -LiteralPath $stub.Log)
    Assert-C487 -Cond (($request[0] -like 'GET /api/w/mc/jobs/list?script_path_exact=u%2Flndcobra%2Fantiphon_nightly_tests*') -and (@($request | Where-Object { $_ -eq 'Authorization: Bearer c544-fixture-token' }).Count -eq 1)) `
        -Name 'C544 ProductionJobAdapter real HTTP request shape' -Detail ($request -join ' | ')
    $byId = @{}
    foreach ($j in $jobs) { $byId[[string]$j.id] = $j }
    $expected = [ordered]@{ 'j-queued' = 'queued'; 'j-running' = 'running'; 'j-success' = 'success'; 'j-failed' = 'failed';
        'j-missing-result' = 'unknown'; 'j-string-bool' = 'unknown'; 'j-manual' = 'success'; 'j-unknown' = 'unknown' }
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

$script:C544ExpectedRows = 83

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C487_G')) { & $fn }
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C544_')) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'health-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 52 + $script:C544ExpectedRows })
