<#
.SYNOPSIS
    Trigger Antiphon's bidirectional GitHub Issues <-> cards sync (CARD-0166 S7).

    Calls the Antiphon API only - the server holds the GitHub PAT (ApiKeys entry named by
    tracker.token_key, or tracker.api_key_env). This script never talks to GitHub directly.

    Recurring end-of-day run is registered in Windmill on server2 as
    u/lndcobra/antiphon_github_sync (daily 18:00 Europe/London, desktop tag, SSH bridge).
    Safe to run manually at any time.

.PARAMETER BoardId
    Optional board GUID. When omitted, syncs every board with TrackerKind != Internal.

.PARAMETER BaseUrl
    Antiphon API base URL. Defaults to the Aspire AppHost port (17202).
#>
param(
    [Guid]$BoardId,
    [string]$BaseUrl = 'http://localhost:17202'
)

$ErrorActionPreference = 'Stop'

$base = $BaseUrl.TrimEnd('/')
if ($PSBoundParameters.ContainsKey('BoardId')) {
    $url = "$base/api/boards/$BoardId/tracker/sync"
}
else {
    $url = "$base/api/tracker-sync/run"
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

try {
    $summary = $bodyText | ConvertFrom-Json
    if ($summary.boards) {
        foreach ($board in $summary.boards) {
            $name = $board.boardName
            Write-Host ("  {0}: pulled={1} commentsIn={2} commentsOut={3} labels={4} state={5} creates={6} skips=[{7}]{8}" -f `
                $name,
                $board.issuesPulled,
                $board.commentsIn,
                $board.commentsOut,
                $board.labelsChanged,
                $board.stateChanges,
                $board.creates,
                (($board.skips) -join ', '),
                $(if ($board.error) { " error=$($board.error)" } else { '' }))
        }
    }
    if ($summary.concurrentRunSkipped) {
        Write-Host '  (concurrent run skipped for at least one board)'
    }
}
catch {
    # Body was not JSON; raw content already printed.
}

exit 0
