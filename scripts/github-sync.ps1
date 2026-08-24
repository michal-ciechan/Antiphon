<#
.SYNOPSIS
    Trigger Antiphon's bidirectional GitHub Issues <-> cards sync (CARD-0166 S7).

    Calls the Antiphon API only - the server holds the GitHub PAT (ApiKeys entry named by
    tracker.token_key, or tracker.api_key_env). This script never talks to GitHub directly.

    Recurring run is registered in Windmill on server2 as u/lndcobra/antiphon_github_sync,
    every 3 hours (`0 0 */3 * * *`, Europe/London, desktop tag, SSH bridge) with -Notify.
    Safe to run manually at any time.

    Exit codes: 0 = every board synced cleanly; 1 = the request failed, or at least one board
    reported an error (CARD-0171 - a failed sync used to be a green Windmill job).

.PARAMETER BoardId
    Optional board GUID. When omitted, syncs every board with TrackerKind != Internal.

.PARAMETER BaseUrl
    Antiphon API base URL. Defaults to the Aspire AppHost port (17202).

.PARAMETER Notify
    Ask this run to announce what it changed (CARD-0171). Forwards ?notify=true; the server
    sends one plain-text summary per channel named by each changed board's
    tracker.notify_channel. A board with no notify_channel announces nothing, and a run that
    changed nothing sends nothing.
#>
param(
    [Guid]$BoardId,
    [string]$BaseUrl = 'http://localhost:17202',
    [switch]$Notify
)

$ErrorActionPreference = 'Stop'

$base = $BaseUrl.TrimEnd('/')
if ($PSBoundParameters.ContainsKey('BoardId')) {
    $url = "$base/api/boards/$BoardId/tracker/sync"
}
else {
    $url = "$base/api/tracker-sync/run"
}

if ($Notify) {
    $url = "$url`?notify=true"
}

Write-Host "POST $url"
try {
    $response = Invoke-WebRequest -Uri $url -Method POST -ContentType 'application/json' -UseBasicParsing
}
catch {
    $status = $null
    $body = $null
    if ($_.Exception.Response) {
        $status = [int]$_.Exception.Response.StatusCode
        try {
            $stream = $_.Exception.Response.GetResponseStream()
            if ($stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                $body = $reader.ReadToEnd()
                $reader.Dispose()
            }
        }
        catch { }
    }
    if ($status) {
        Write-Error "HTTP $status from $url`n$body"
    }
    else {
        Write-Error "Request failed: $($_.Exception.Message)"
    }
    exit 1
}

Write-Host "HTTP $([int]$response.StatusCode)"
$bodyText = $response.Content
Write-Host $bodyText

$boardErrors = 0
try {
    $summary = $bodyText | ConvertFrom-Json
    if ($summary.boards) {
        foreach ($board in $summary.boards) {
            $name = $board.boardName
            if ($board.error) { $boardErrors++ }
            Write-Host ("  {0}: pulled={1} commentsIn={2} commentsOut={3} labels={4} state={5} creates={6} reopens={7} changes={8} skips=[{9}]{10}" -f `
                $name,
                $board.issuesPulled,
                $board.commentsIn,
                $board.commentsOut,
                $board.labelsChanged,
                $board.stateChanges,
                $board.creates,
                $board.externalReopens,
                $(if ($null -ne $board.changes) { @($board.changes).Count } else { 0 }),
                (($board.skips) -join ', '),
                $(if ($board.error) { " error=$($board.error)" } else { '' }))
        }
    }
    if ($summary.concurrentRunSkipped) {
        Write-Host '  (concurrent run skipped for at least one board)'
    }
    # CARD-0171: notification outcomes. A board that changed but could not be announced says why
    # here (notify_channel_unset / channel_not_found / channel_ambiguous / channel_disabled /
    # send_failed); it never fails the sync, which has already committed.
    if ($summary.notifications) {
        foreach ($n in $summary.notifications) {
            if ($n.sent) {
                Write-Host ("  notified board {0} -> channel {1}" -f $n.boardId, $n.channelId)
            }
            else {
                Write-Host ("  NOT notified board {0}: {1}" -f $n.boardId, $n.reason)
            }
        }
    }
}
catch {
    # Body was not JSON; raw content already printed.
}

if ($boardErrors -gt 0) {
    Write-Host "$boardErrors board(s) reported an error."
    exit 1
}

exit 0
