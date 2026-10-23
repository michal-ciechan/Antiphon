#requires -Version 5.1
param([ValidateSet('EventLog','Marker','Webhook')][string]$Case)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'apphost-common.ps1')
$script:passed=0
function Assert-Sink([bool]$Condition,[string]$Name) {
    if (-not $Condition) { throw "FAIL $Name" }
    $script:passed++; Write-Host "PASS $Name"
}
$root=Join-Path ([IO.Path]::GetTempPath()) ('c1527-sinks-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory (Join-Path $root 'logs') | Out-Null
$oldWebhook=[Environment]::GetEnvironmentVariable('ANTIPHON_WATCHDOG_ALERT_WEBHOOK')
$oldProposal=[Environment]::GetEnvironmentVariable('ANTIPHON_APPHOST_ALERT_WEBHOOK')
[Environment]::SetEnvironmentVariable('ANTIPHON_WATCHDOG_ALERT_WEBHOOK',$null)
[Environment]::SetEnvironmentVariable('ANTIPHON_APPHOST_ALERT_WEBHOOK',$null)
$script:entries=@();$script:posts=@();$script:eventFails=$false;$script:webhookFails=$false
function Write-AppHostWindowsEvent {
    param($Event)
    if ($script:eventFails) { throw 'event source denied' }
    $script:entries+=@($Event)
    return $true
}
function Invoke-WebRequest {
    param($Uri,$Method,[switch]$UseBasicParsing,$TimeoutSec,$ContentType,$Headers,$Body)
    if ($script:webhookFails) { throw 'delivery failed' }
    $script:posts+=@([pscustomobject]@{Uri=$Uri;Method=$Method;Timeout=$TimeoutSec;Headers=$Headers;Body=$Body})
    return [pscustomobject]@{StatusCode=200}
}
$now=[datetime]::UtcNow
$down=$now.AddMinutes(-4)
$state=[pscustomobject]@{downSinceUtc=$down.ToString('o');outageAlerted=$true;restartsUtc=@($now.AddMinutes(-2).ToString('o'))}
function Event([string]$Kind) {
    [pscustomobject]@{Id=('fixture:'+ $Kind);Kind=$Kind;DownSinceUtc=$down.ToString('o');ObservedUtc=$now.ToString('o')}
}
$marker=Join-Path $root 'logs/apphost-down.alert'
try {
    $event=Event 'down'
    if ($Case -eq 'EventLog') {
        $null=Send-AppHostOutageEvent -Event $event -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($script:entries.Count -eq 1) 'V-4 outage reaches independent event log sink'
        $null=Send-AppHostOutageEvent -Event $event -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($script:entries.Count -eq 1) 'V-4 retry does not duplicate local event'
        $recovery=Event 'recovery';$state.outageAlerted=$false;$state.downSinceUtc=$null
        $null=Send-AppHostOutageEvent -Event $recovery -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($script:entries.Count -eq 2 -and $script:entries[1].Kind -eq 'recovery') 'V-4 recovery reaches event log once'
        $script:eventFails=$true
        $state.outageAlerted=$true;$state.downSinceUtc=$down.ToString('o')
        [Environment]::SetEnvironmentVariable('ANTIPHON_WATCHDOG_ALERT_WEBHOOK','https://alert.invalid/fixture')
        $result=Send-AppHostOutageEvent -Event (Event 'down') -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($result -and (Test-Path $marker) -and $script:posts.Count -eq 1) 'V-4 event source failure cannot fail watchdog or other sinks'
    } elseif ($Case -eq 'Marker') {
        $null=Send-AppHostOutageEvent -Event $event -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink (Test-Path $marker) 'V-5 outage creates local marker without webhook'
        $payload=Get-Content $marker -Raw | ConvertFrom-Json
        Assert-Sink ((ConvertTo-UtcStamp $payload.downSinceUtc) -eq $down -and $payload.minutesDown -eq 4) 'V-5 marker contains outage start and minutes down'
        Assert-Sink ((ConvertTo-UtcStamp $payload.lastRestartAttemptUtc) -eq $now.AddMinutes(-2)) 'V-5 marker contains last restart attempt'
        $state.restartsUtc=@($now.ToString('o'))
        $null=Send-AppHostOutageEvent -SourceRoot $root -State $state -NowUtc ($now.AddMinutes(1))
        $payload=Get-Content $marker -Raw | ConvertFrom-Json
        Assert-Sink ($payload.minutesDown -eq 5 -and (ConvertTo-UtcStamp $payload.lastRestartAttemptUtc) -eq $now) 'V-5 later fire refreshes marker without another alert'
        $state.outageAlerted=$false;$state.downSinceUtc=$null
        $null=Send-AppHostOutageEvent -Event (Event 'recovery') -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink (-not (Test-Path $marker)) 'V-5 recovery removes marker'
        # An old down event awaiting webhook must not resurrect the recovered marker.
        $null=Send-AppHostOutageEvent -Event $event -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink (-not (Test-Path $marker)) 'V-5 delayed down delivery cannot restore recovered marker'
        New-Item -ItemType Directory $marker | Out-Null
        $state.outageAlerted=$true;$state.downSinceUtc=$down.ToString('o')
        [Environment]::SetEnvironmentVariable('ANTIPHON_WATCHDOG_ALERT_WEBHOOK','https://alert.invalid/fixture')
        $before=$script:entries.Count
        $result=Send-AppHostOutageEvent -Event (Event 'down') -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($result -and $script:entries.Count -eq ($before+1) -and $script:posts.Count -eq 1) 'V-5 marker write failure leaves event log and webhook independent'
    } else {
        [Environment]::SetEnvironmentVariable('ANTIPHON_WATCHDOG_ALERT_WEBHOOK','https://alert.invalid/fixture')
        $result=Send-AppHostOutageEvent -Event $event -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($result -and $script:posts.Count -eq 1) 'V-6 configured operator webhook receives outage'
        Assert-Sink ($script:posts[0].Headers['Idempotency-Key'] -eq $event.Id -and $script:posts[0].Timeout -eq 5) 'V-6 bounded webhook carries durable event identity'
        $event=$event | ConvertTo-Json -Depth 5 | ConvertFrom-Json
        $null=Send-AppHostOutageEvent -Event $event -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($script:posts.Count -eq 1) 'V-6 persisted successful event posts only once'
        $state.outageAlerted=$false;$state.downSinceUtc=$null
        $null=Send-AppHostOutageEvent -Event (Event 'recovery') -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($script:posts.Count -eq 2) 'V-6 one recovery webhook'
        [Environment]::SetEnvironmentVariable('ANTIPHON_WATCHDOG_ALERT_WEBHOOK',$null)
        [Environment]::SetEnvironmentVariable('ANTIPHON_APPHOST_ALERT_WEBHOOK','https://obsolete.invalid/do-not-use')
        $result=Send-AppHostOutageEvent -Event (Event 'down') -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($result -and $script:posts.Count -eq 2) 'V-6 missing optional variable sends nothing and ignores old name'
        [Environment]::SetEnvironmentVariable('ANTIPHON_WATCHDOG_ALERT_WEBHOOK','https://alert.invalid/fixture')
        $script:webhookFails=$true;$failed=Event 'down'
        $state.outageAlerted=$true;$state.downSinceUtc=$down.ToString('o')
        $result=Send-AppHostOutageEvent -Event $failed -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink (-not $result -and (Test-Path $marker)) 'V-6 webhook failure retains retry without suppressing marker'
        $count=$script:entries.Count;$script:webhookFails=$false
        $result=Send-AppHostOutageEvent -Event $failed -SourceRoot $root -State $state -NowUtc $now
        Assert-Sink ($result -and $script:entries.Count -eq $count -and $script:posts.Count -eq 3) 'V-6 retry does not repeat independent local event'
    }
    Write-Host "C1527 $Case assertions=$script:passed failed=0"
} finally {
    [Environment]::SetEnvironmentVariable('ANTIPHON_WATCHDOG_ALERT_WEBHOOK',$oldWebhook)
    [Environment]::SetEnvironmentVariable('ANTIPHON_APPHOST_ALERT_WEBHOOK',$oldProposal)
    Remove-Item -LiteralPath $root -Recurse -Force
}
