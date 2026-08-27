# Read and correct board cards from a shell, without composing HTTP by hand.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# A card is addressed the way it is NAMED: CARD-0051, card-51, '#51', 51, or its guid. Every verb
# takes any of those (CARD-0051 slice 1) - there is no id to look up first.
#
# ALL LONG TEXT COMES FROM A FILE. -DescriptionFile and -ReasonFile are read with Get-Content -Raw
# and sent as-is, so backticks, $(...), quotes, newlines and everything else survive untouched.
# -Description / -Reason exist for short one-liners; anything with shell metacharacters in it should
# go in a file. This is not a nicety: hand-quoting a multi-line description is what produced about
# fifteen throwaway scripts in a single session.
#
# CONCURRENCY TOKEN, and the tradeoff this script makes on your behalf:
#   Every card write carries a concurrency token, and the server rotates it on EVERY write. By
#   default this script fetches the card immediately before each write and uses the token it just
#   read - so the window in which someone else's write could be clobbered is milliseconds rather
#   than the seconds-to-minutes of a human round trip. That window is not zero. It is accepted
#   because (a) two genuinely concurrent writers still collide at the database on the unique
#   (CardId, RevisionNumber) index and one gets a 409 regardless, and (b) since CARD-0019 every
#   content write is revision-logged, so a clobber is readable - and reversible - from the card's
#   history. If you composed an edit from an earlier read and want true compare-and-swap, pass
#   -Token <guid>: it is sent verbatim and a stale one is the server's 409, which is the point.
#
# Verbs:
#   card.ps1 get       CARD-0051 [-Json]
#   card.ps1 history   CARD-0051 [-Json]
#   card.ps1 new       -Board <name|guid> -Title <t> [-DescriptionFile p | -Description s]
#                      [-Priority n] [-Labels a,b]
#   card.ps1 edit      CARD-0051 -Reason <r> | -ReasonFile <p> [-Title t]
#                      [-DescriptionFile p | -Description s] [-Priority n] [-Labels a,b]
#                      [-By name] [-Token g]
#   card.ps1 move      CARD-0051 -To <column name|guid> [-Reason r | -ReasonFile p] [-Spawn] [-Token g]
#   card.ps1 close     CARD-0051 -Reason r | -ReasonFile p
#   card.ps1 reopen    CARD-0051 -Reason r | -ReasonFile p [-To column] [-By name] [-Token g]
#   card.ps1 archive   CARD-0051 -Reason r | -ReasonFile p [-By name] [-Token g]
#   card.ps1 unarchive CARD-0051 -Reason r | -ReasonFile p [-By name] [-Token g]
#   card.ps1 -Limits
#
# A move into an ACTIVE column does NOT start an agent unless you pass -Spawn (CARD-0051 slice 3).
# The tick will not pick that card up either (CARD-0087); -Spawn or POST /spawn starts it.
# When it would have, the script says so instead of leaving you to find out later.
# A reopen never starts an agent, even into an active column. Spawn separately if you want one.
[CmdletBinding(DefaultParameterSetName = 'Verb')]
param(
    [Parameter(ParameterSetName = 'Verb', Position = 0, Mandatory = $true)]
    [ValidateSet('get', 'history', 'new', 'edit', 'move', 'close', 'reopen', 'archive', 'unarchive')]
    [string]$Verb,

    # The card, in any form it gets written down: CARD-0051, card-51, '#51', 51, or its guid.
    [Parameter(ParameterSetName = 'Verb', Position = 1)]
    [string]$Card,

    # Board name (case-insensitive) or guid. Only 'new' needs it - every other verb finds the board
    # from the card.
    [Parameter(ParameterSetName = 'Verb')]
    [string]$Board,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$Title,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$Description,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$DescriptionFile,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$Reason,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$ReasonFile,

    # Target column for 'move' and 'reopen': its name (case-insensitive), its state key, or its guid.
    [Parameter(ParameterSetName = 'Verb')]
    [string]$To,

    # -1 means "leave it alone". 0 is a real priority, so an unbound [int] (which is 0) cannot be
    # used as the unset marker.
    [Parameter(ParameterSetName = 'Verb')]
    [int]$Priority = -1,

    [Parameter(ParameterSetName = 'Verb')]
    [string[]]$Labels,

    # Self-reported author on the revision. The server has no principals; this is honest free text.
    [Parameter(ParameterSetName = 'Verb')]
    [string]$By,

    # Strict compare-and-swap: sent verbatim instead of a freshly-read token. See the header.
    [Parameter(ParameterSetName = 'Verb')]
    [string]$Token,

    # Let a move into an active column start an agent session on the card.
    [Parameter(ParameterSetName = 'Verb')]
    [switch]$Spawn,

    [Parameter(ParameterSetName = 'Verb')]
    [switch]$Json,

    [Parameter(ParameterSetName = 'Limits', Mandatory = $true)]
    [switch]$Limits
)

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

function Invoke-Antiphon {
    param([string]$Method, [string]$Path, $Body)
    $uri = "$api$Path"
    try {
        if ($null -ne $Body) {
            # Encoded here rather than handed to Invoke-RestMethod as a string: Windows PowerShell
            # 5.1 picks the body encoding from the content type and will mangle non-ASCII text in a
            # description otherwise. Bytes are unambiguous on both hosts.
            $json = $Body | ConvertTo-Json -Depth 8 -Compress
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $bytes `
                -ContentType 'application/json; charset=utf-8'
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    catch {
        # Surface the server's own message - it names the field, the ceiling and the actual length,
        # or explains the 409, and that is the actionable part.
        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
        Write-Error "Antiphon $Method $Path failed: $detail"
        exit 1
    }
}

function Get-CardLimits {
    if ($null -eq $script:cardLimits) {
        $script:cardLimits = Invoke-Antiphon -Method GET -Path '/api/cards/limits'
    }
    return $script:cardLimits
}

# Fails LOCALLY, deterministically, naming the ceiling - so an over-long body never becomes a round
# trip that ends in a 422 after the text was already assembled.
function Assert-WithinLimit {
    param([string]$Field, [string]$Value, [int]$Limit)
    if ([string]::IsNullOrEmpty($Value)) { return }
    if ($Value.Length -le $Limit) { return }
    Write-Error ("{0} is {1} characters; the limit is {2}. Trim it, or split the detail across a comment." -f `
            $Field, $Value.Length, $Limit)
    exit 1
}

# -XFile wins over -X: passing both is a mistake worth naming rather than silently resolving.
function Read-TextArgument {
    param([string]$Name, [string]$Inline, [string]$Path)
    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        if (-not [string]::IsNullOrWhiteSpace($Inline)) {
            Write-Error "Pass -$Name or -${Name}File, not both."
            exit 1
        }
        if (-not (Test-Path -LiteralPath $Path)) {
            Write-Error "-${Name}File '$Path' does not exist."
            exit 1
        }
        $text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
        if ($null -eq $text) { $text = '' }
        return $text
    }
    return $Inline
}

function Get-CardOrFail {
    if ([string]::IsNullOrWhiteSpace($Card)) {
        Write-Error "Which card? Pass it as the first argument: card.ps1 $Verb CARD-0051 ..."
        exit 1
    }
    return Invoke-Antiphon -Method GET -Path ("/api/cards/{0}" -f [uri]::EscapeDataString($Card.Trim()))
}

# By default the token is read immediately before the write; -Token is verbatim. See the header.
function Resolve-Token {
    param($Fetched)
    if (-not [string]::IsNullOrWhiteSpace($Token)) { return $Token }
    return $Fetched.concurrencyToken
}

function Get-RequiredReason {
    $text = Read-TextArgument -Name 'Reason' -Inline $Reason -Path $ReasonFile
    if ([string]::IsNullOrWhiteSpace($text)) {
        Write-Error "A -Reason (or -ReasonFile) is required for '$Verb'. Say why, not what."
        exit 1
    }
    Assert-WithinLimit -Field 'Reason' -Value $text -Limit (Get-CardLimits).maxReasonLength
    return $text
}

function Write-CardLine {
    param($TheCard)
    $labels = ''
    if ($TheCard.labels -and $TheCard.labels.Count -gt 0) { $labels = ' [' + ($TheCard.labels -join ', ') + ']' }
    Write-Output ("{0}  {1}  {2}{3}" -f $TheCard.identifier, $TheCard.status, $TheCard.title, $labels)
}

function Get-BoardColumns {
    param([string]$BoardId)
    return Invoke-Antiphon -Method GET -Path "/api/boards/$BoardId/columns"
}

if ($PSCmdlet.ParameterSetName -eq 'Limits') {
    $l = Get-CardLimits
    Write-Output ("title       {0}" -f $l.maxTitleLength)
    Write-Output ("description {0}" -f $l.maxDescriptionLength)
    Write-Output ("reason      {0}" -f $l.maxReasonLength)
    Write-Output ("actor       {0}" -f $l.maxActorLength)
    return
}

switch ($Verb) {
    'get' {
        $theCard = Get-CardOrFail
        if ($Json) { $theCard | ConvertTo-Json -Depth 8; return }
        Write-CardLine $theCard
        Write-Output ("id          {0}" -f $theCard.id)
        Write-Output ("board       {0}" -f $theCard.boardId)
        Write-Output ("column      {0}" -f $theCard.boardColumnId)
        Write-Output ("token       {0}" -f $theCard.concurrencyToken)
        Write-Output ("revisions   {0}" -f $theCard.revisionCount)
        if ($theCard.assignedAgentName) { Write-Output ("agent       {0}" -f $theCard.assignedAgentName) }
        if ($theCard.ownerSessionId) { Write-Output ("session     {0}" -f $theCard.ownerSessionId) }
        if ($theCard.archivedAt) { Write-Output ("ARCHIVED    {0}" -f $theCard.archivedReason) }
        if ($theCard.terminalReason) { Write-Output ("closed      {0}" -f $theCard.terminalReason) }
        if (-not [string]::IsNullOrWhiteSpace($theCard.description)) {
            Write-Output ''
            Write-Output $theCard.description
        }
        return
    }

    'history' {
        if ([string]::IsNullOrWhiteSpace($Card)) {
            Write-Error 'Which card? Pass it as the first argument: card.ps1 history CARD-0051'
            exit 1
        }
        $path = "/api/cards/{0}/revisions" -f [uri]::EscapeDataString($Card.Trim())
        $revisions = Invoke-Antiphon -Method GET -Path $path
        if ($Json) { $revisions | ConvertTo-Json -Depth 8; return }
        if (-not $revisions -or $revisions.Count -eq 0) { Write-Output 'No history.'; return }
        foreach ($r in $revisions) {
            Write-Output ("{0,3}  {1,-12} {2}  {3}" -f `
                    $r.revisionNumber, $r.kind, $r.createdAt, $r.reason)
            if ($r.editedBy) { Write-Output ("     by {0}" -f $r.editedBy) }
        }
        return
    }

    'new' {
        if ([string]::IsNullOrWhiteSpace($Board)) {
            Write-Error 'A -Board (name or guid) is required for new.'
            exit 1
        }
        if ([string]::IsNullOrWhiteSpace($Title)) {
            Write-Error 'A -Title is required for new.'
            exit 1
        }

        $boardId = $null
        $parsed = [guid]::Empty
        if ([guid]::TryParse($Board, [ref]$parsed)) {
            $boardId = $Board
        }
        else {
            $all = Invoke-Antiphon -Method GET -Path '/api/boards'
            $hits = @($all | Where-Object { $_.name -and $_.name.ToLowerInvariant() -eq $Board.ToLowerInvariant() })
            if ($hits.Count -eq 0) {
                Write-Error ("No board named '{0}'. Known boards: {1}" -f $Board, (($all | ForEach-Object { $_.name }) -join ', '))
                exit 1
            }
            if ($hits.Count -gt 1) {
                Write-Error ("'{0}' names {1} boards - pass the guid instead." -f $Board, $hits.Count)
                exit 1
            }
            $boardId = $hits[0].id
        }

        $desc = Read-TextArgument -Name 'Description' -Inline $Description -Path $DescriptionFile
        $limitSet = Get-CardLimits
        Assert-WithinLimit -Field 'Title' -Value $Title -Limit $limitSet.maxTitleLength
        Assert-WithinLimit -Field 'Description' -Value $desc -Limit $limitSet.maxDescriptionLength

        $body = @{ title = $Title }
        if (-not [string]::IsNullOrEmpty($desc)) { $body['description'] = $desc }
        if ($Priority -ge 0) { $body['priority'] = $Priority }
        if ($Labels) { $body['labels'] = @($Labels) }

        $created = Invoke-Antiphon -Method POST -Path "/api/boards/$boardId/cards" -Body $body
        Write-CardLine $created
        Write-Output ("id          {0}" -f $created.id)
        return
    }

    'edit' {
        $theCard = Get-CardOrFail
        $reasonText = Get-RequiredReason
        $desc = Read-TextArgument -Name 'Description' -Inline $Description -Path $DescriptionFile

        $limitSet = Get-CardLimits
        Assert-WithinLimit -Field 'Title' -Value $Title -Limit $limitSet.maxTitleLength
        Assert-WithinLimit -Field 'Description' -Value $desc -Limit $limitSet.maxDescriptionLength
        Assert-WithinLimit -Field 'By' -Value $By -Limit $limitSet.maxActorLength

        # Null means UNCHANGED for every content field, so send only what actually changed.
        $body = @{
            concurrencyToken = Resolve-Token $theCard
            reason           = $reasonText
        }
        if (-not [string]::IsNullOrWhiteSpace($Title)) { $body['title'] = $Title }
        if (-not [string]::IsNullOrEmpty($desc)) { $body['description'] = $desc }
        if ($Priority -ge 0) { $body['priority'] = $Priority }
        if ($Labels) { $body['labels'] = @($Labels) }
        if (-not [string]::IsNullOrWhiteSpace($By)) { $body['editedBy'] = $By }
        if ($body.Count -le 2) {
            Write-Error 'Nothing to change. Pass at least one of -Title, -Description/-DescriptionFile, -Priority, -Labels.'
            exit 1
        }

        $updated = Invoke-Antiphon -Method PATCH -Path ("/api/cards/{0}/content" -f $theCard.id) -Body $body
        Write-CardLine $updated
        Write-Output ("revision    {0}" -f $updated.revisionCount)
        return
    }

    { $_ -in 'move', 'close' } {
        $theCard = Get-CardOrFail
        $columns = Get-BoardColumns -BoardId $theCard.boardId

        if ($Verb -eq 'close') {
            $reasonText = Get-RequiredReason
            # The board's own idea of "finished": the first terminal column in column order.
            $target = @($columns | Where-Object { $_.isTerminal } | Sort-Object columnOrder)[0]
            if ($null -eq $target) {
                Write-Error "Board $($theCard.boardId) has no terminal column to close into."
                exit 1
            }
        }
        else {
            if ([string]::IsNullOrWhiteSpace($To)) {
                Write-Error ("A -To column is required for move. This board has: {0}" -f `
                    (($columns | ForEach-Object { $_.name }) -join ', '))
                exit 1
            }
            $reasonText = Read-TextArgument -Name 'Reason' -Inline $Reason -Path $ReasonFile
            if (-not [string]::IsNullOrWhiteSpace($reasonText)) {
                Assert-WithinLimit -Field 'Reason' -Value $reasonText -Limit (Get-CardLimits).maxReasonLength
            }
            $needle = $To.Trim()
            $target = @($columns | Where-Object {
                    $_.id -eq $To -or $_.name -ieq $needle -or $_.stateKey -ieq $needle -or $_.cardStatus -ieq $needle
                })[0]
            if ($null -eq $target) {
                Write-Error ("No column '{0}' on this board. It has: {1}" -f $To, `
                    (($columns | ForEach-Object { $_.name }) -join ', '))
                exit 1
            }
        }

        $body = @{
            boardColumnId    = $target.id
            concurrencyToken = Resolve-Token $theCard
        }
        if (-not [string]::IsNullOrWhiteSpace($reasonText)) { $body['reason'] = $reasonText }
        if ($Spawn) { $body['spawn'] = $true }

        $result = Invoke-Antiphon -Method PATCH -Path ("/api/cards/{0}" -f $theCard.id) -Body $body
        Write-CardLine $result.card
        Write-Output ("moved to    {0}" -f $target.name)
        if ($result.spawnedSessionId) {
            Write-Output ("started     session {0}" -f $result.spawnedSessionId)
        }
        elseif ($result.spawnSuppressed) {
            Write-Output 'moved into an active column; NO agent was started - the tick will not pick it up either; re-run with -Spawn (or POST /spawn) to start one'
        }
        return
    }

    'reopen' {
        $theCard = Get-CardOrFail
        $reasonText = Get-RequiredReason
        Assert-WithinLimit -Field 'By' -Value $By -Limit (Get-CardLimits).maxActorLength

        # -To is optional: omit it and the server picks Backlog (then lowest-order live column).
        # Do not re-implement that fallback here - just resolve a name the caller DID give.
        $target = $null
        if (-not [string]::IsNullOrWhiteSpace($To)) {
            $columns = Get-BoardColumns -BoardId $theCard.boardId
            $needle = $To.Trim().ToLowerInvariant()
            $target = @($columns | Where-Object {
                    $_.id -eq $To -or $_.name.ToLowerInvariant() -eq $needle -or $_.stateKey.ToLowerInvariant() -eq $needle
                })[0]
            if ($null -eq $target) {
                Write-Error ("No column '{0}' on this board. It has: {1}" -f $To, `
                    (($columns | ForEach-Object { $_.name }) -join ', '))
                exit 1
            }
        }

        $body = @{
            concurrencyToken = Resolve-Token $theCard
            reason           = $reasonText
        }
        if ($null -ne $target) { $body['boardColumnId'] = $target.id }
        if (-not [string]::IsNullOrWhiteSpace($By)) { $body['reopenedBy'] = $By }

        $updated = Invoke-Antiphon -Method POST -Path ("/api/cards/{0}/reopen" -f $theCard.id) -Body $body
        Write-CardLine $updated
        if ($null -ne $target) {
            Write-Output ("reopened to {0}" -f $target.name)
        }
        else {
            Write-Output ("reopened to {0}" -f $updated.status)
        }
        return
    }

    { $_ -in 'archive', 'unarchive' } {
        $theCard = Get-CardOrFail
        $reasonText = Get-RequiredReason
        Assert-WithinLimit -Field 'By' -Value $By -Limit (Get-CardLimits).maxActorLength

        $body = @{
            concurrencyToken = Resolve-Token $theCard
            reason           = $reasonText
        }
        if (-not [string]::IsNullOrWhiteSpace($By)) {
            if ($Verb -eq 'archive') { $body['archivedBy'] = $By } else { $body['unarchivedBy'] = $By }
        }

        $updated = Invoke-Antiphon -Method POST -Path ("/api/cards/{0}/{1}" -f $theCard.id, $Verb) -Body $body
        Write-CardLine $updated
        if ($Verb -eq 'archive') {
            # Archive is not deletion: the row stays, so every reference to the identifier keeps
            # resolving and the allocator never hands the number out again.
            Write-Output 'archived (reversible: card.ps1 unarchive)'
        }
        else {
            Write-Output 'back on the board'
        }
        return
    }
}
