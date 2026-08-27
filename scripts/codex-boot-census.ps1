#requires -Version 7.0
<#
.SYNOPSIS
Read-only CARD-0133 census of cold Codex delegates whose brief rendered but never submitted.

.DESCRIPTION
Joins AgentSessions, TranscriptEntries, SessionQueuedMessages and AgentTasks, then adds the
raw ANSI-log signature. It makes no HTTP calls and issues one SELECT through the dev Postgres
container. The result is intentionally a table, not a pass/fail verdict: it is the deploy
baseline and regression census for the boot wedge.
#>
[CmdletBinding()]
param(
    [datetime]$Since = [datetime]'2026-08-20T00:00:00Z',
    [string]$SessionLogRoot = 'C:\logs\antiphon\session-runner',
    [string]$Container = 'antiphon-postgres'
)

$ErrorActionPreference = 'Stop'

# AgentKind.Codex=2. A task can be absent for a human-launched Codex session, so preserve it with
# LEFT JOIN. The aggregation avoids multiplying queue rows by transcript rows.
$sql = @"
SELECT
  s."Id" AS session_id,
  s."StartedAt" AS started_at,
  s."EndedAt" AS ended_at,
  s."Status" AS session_status,
  s."FailureReason" AS failure_reason,
  t."Id" AS task_id,
  t."Status" AS task_status,
  t."Title" AS task_title,
  q."Status" AS queue_status,
  q."DeliveryAttempts" AS delivery_attempts,
  q."LastDeliveryBaselineSequence" AS baseline_sequence,
  q."SentAt" AS sent_at,
  COALESCE(te.user_prompts, 0) AS user_prompts,
  COALESCE(te.turn_ends, 0) AS turn_ends
FROM "AgentSessions" s
LEFT JOIN LATERAL (
  SELECT at.* FROM "AgentTasks" at WHERE at."AgentSessionId" = s."Id"
  ORDER BY at."CreatedAt" DESC LIMIT 1
) t ON TRUE
LEFT JOIN LATERAL (
  SELECT sqm.* FROM "SessionQueuedMessages" sqm
  WHERE sqm."AgentSessionId" = s."Id" AND sqm."Origin" = 3
  ORDER BY sqm."CreatedAt" DESC LIMIT 1
) q ON TRUE
LEFT JOIN LATERAL (
  SELECT
    count(*) FILTER (WHERE "Kind" = 'UserPrompt') AS user_prompts,
    count(*) FILTER (WHERE "Kind" = 'TurnEnd') AS turn_ends
  FROM "TranscriptEntries" WHERE "AgentSessionId" = s."Id"
) te ON TRUE
WHERE s."AgentKind" = 2 AND s."StartedAt" >= '$($Since.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss+00'))'
ORDER BY s."StartedAt";
"@

$rows = & docker exec $Container psql -U antiphon -d antiphon -At -F "`t" -c $sql
if ($LASTEXITCODE -ne 0) { throw 'Postgres census SELECT failed.' }

$result = foreach ($line in $rows) {
    $f = $line -split "`t", 14
    if ($f.Count -lt 14) { continue }
    $sessionId = $f[0]
    # Successful sessions can have multi-megabyte ANSI logs. Only the candidate shape needs its
    # raw signature, so do not turn an otherwise tiny read-only census into a 300 MB log scan.
    $candidate = $f[8] -eq '1' -and $f[9] -eq '1' -and [string]::IsNullOrEmpty($f[10]) -and $f[12] -eq '0'
    # Runtime filenames use Guid:N whereas PostgreSQL's text rendering carries hyphens.
    $ansi = Join-Path $SessionLogRoot "$(($sessionId -replace '-', '').ToLowerInvariant()).ansi.log"
    $raw = if ($candidate -and (Test-Path -LiteralPath $ansi)) { [IO.File]::ReadAllText($ansi) } else { '' }
    $lastFrameClosed = $raw.EndsWith("`e[?2026l", [StringComparison]::Ordinal)
    [pscustomobject]@{
        SessionId = $sessionId
        StartedAt = $f[1]
        TaskId = $f[5]
        TaskStatus = $f[6]
        QueueStatus = $f[8]
        Attempts = $f[9]
        Baseline = $f[10]
        UserPrompts = $f[12]
        TurnEnds = $f[13]
        AnsiBytes = if ($raw) { [Text.Encoding]::UTF8.GetByteCount($raw) } else { 0 }
        LastFrameClosed = $lastFrameClosed
        BodyVisible = $raw -match 'task-[0-9a-f]{8}.*brief\.md'
        WorkingSeen = $raw -match 'Working \('
        McpInterrupted = $raw -match 'MCP startup interrupted'
        BootWedgeSignature = ($candidate -and $raw -and $lastFrameClosed -and $raw -notmatch 'Working \(')
    }
}

$result | Format-Table -AutoSize
$wedges = @($result | Where-Object BootWedgeSignature)
Write-Host "CARD-0133 census: $($result.Count) Codex sessions since $($Since.ToUniversalTime().ToString('u')); $($wedges.Count) boot-wedge signature rows. Read-only."
