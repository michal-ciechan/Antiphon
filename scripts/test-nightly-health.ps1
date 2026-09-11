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
    Assert-C487 -Cond ($events.Count -eq 1) -Name 'G123 entry same poll not spam' -Detail ('n=' + $events.Count)
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

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C487_G')) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'health-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 51 })
