# Read and correct board cards from a shell, without composing HTTP by hand.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# A card is addressed the way it is NAMED: CARD-0051, card-51, '#51', 51, or its guid. Every verb
# takes any of those (CARD-0051 slice 1) - there is no id to look up first.
#
# When two boards name a card alike (CARD-0011 on Antiphon and Gym Stat today), the checkout
# (git toplevel sent as cwd) and the delegation token (X-Antiphon-Task-Token) decide which one
# answers; -Board <name|guid> overrides. A collision that survives all of that is a 409 that
# lists every candidate (board, guid, status, title). -Board on a card that board does not
# hold is a 404 naming where it does live.
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
#                      [-Alias a] [-Importance Low|Normal|High|Critical] [-Urgency Normal|Soon|Now]
#                      [-DueAt iso] [-Labels a,b]
#   card.ps1 edit      CARD-0051 -Reason <r> | -ReasonFile <p> [-Title t]
#                      [-DescriptionFile p | -Description s] [-Alias a]
#                      [-Importance Low|Normal|High|Critical] [-Urgency Normal|Soon|Now]
#                      [-ImportanceProvenance Auto|Human]
#                      [-DueAt iso] [-ClearDueAt] [-Labels a,b]
#                      [-By name] [-Token g]
#   card.ps1 move      CARD-0051 -To <column name|guid> [-Reason r | -ReasonFile p] [-Spawn] [-Token g]
#   card.ps1 close     CARD-0051 -Reason r | -ReasonFile p
#   card.ps1 reopen    CARD-0051 -Reason r | -ReasonFile p [-To column] [-By name] [-Token g]
#   card.ps1 archive   CARD-0051 -Reason r | -ReasonFile p [-By name] [-Token g]
#   card.ps1 unarchive CARD-0051 -Reason r | -ReasonFile p [-By name] [-Token g]
#   card.ps1 diagnose  CARD-0051 [-NoWait] [-Json]
#   card.ps1 reorder   CARD-0051 (-Before CARD-nnnn | -After CARD-nnnn | -Top | -Bottom)
#                      [-Reason r | -ReasonFile p] [-By name] [-Token g]
#   card.ps1 order     -Board <b> -OrderFile <p> -Reason r | -ReasonFile p
#                      [-By name] [-OverrideHumanRatings]
#   card.ps1 -Limits
#
# A move into an ACTIVE column does NOT start an agent unless you pass -Spawn (CARD-0051 slice 3).
# The tick will not pick that card up either (CARD-0087); -Spawn or POST /spawn starts it.
# When it would have, the script says so instead of leaving you to find out later.
# A reopen never starts an agent, even into an active column. Spawn separately if you want one.
[CmdletBinding(DefaultParameterSetName = 'Verb')]
param(
    [Parameter(ParameterSetName = 'Verb', Position = 0, Mandatory = $true)]
    [ValidateSet('get', 'history', 'new', 'edit', 'move', 'close', 'reopen', 'archive', 'unarchive', 'diagnose', 'reorder', 'order')]
    [string]$Verb,

    # The card, in any form it gets written down: CARD-0051, card-51, '#51', 51, or its guid.
    [Parameter(ParameterSetName = 'Verb', Position = 1)]
    [string]$Card,

    # Board name (case-insensitive) or guid. Required by 'new'; on every other verb it scopes the
    # identifier when the same number exists on more than one board.
    [Parameter(ParameterSetName = 'Verb')]
    [string]$Board,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$Title,

    # Optional short label (CARD-0350): trimmed, single-line, at most five words. Pass -Alias ''
    # on edit to clear. Never generated.
    [Parameter(ParameterSetName = 'Verb')]
    [string]$Alias,

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

    # Kept for one release as a hard error that names the replacements. Do not send it.
    [Parameter(ParameterSetName = 'Verb')]
    [int]$Priority = -1,

    [Parameter(ParameterSetName = 'Verb')]
    [ValidateSet('Low', 'Normal', 'High', 'Critical')]
    [string]$Importance,

    [Parameter(ParameterSetName = 'Verb')]
    [ValidateSet('Auto', 'Human')]
    [string]$ImportanceProvenance,

    [Parameter(ParameterSetName = 'Verb')]
    [ValidateSet('Normal', 'Soon', 'Now')]
    [string]$Urgency,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$DueAt,

    [Parameter(ParameterSetName = 'Verb')]
    [switch]$ClearDueAt,

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

    # diagnose: return after the 202 instead of polling GET /api/diagnoses.
    [Parameter(ParameterSetName = 'Verb')]
    [switch]$NoWait,

    # reorder: place this card before/after a neighbour, or at the top/bottom of its rank cell.
    [Parameter(ParameterSetName = 'Verb')]
    [string]$Before,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$After,

    [Parameter(ParameterSetName = 'Verb')]
    [switch]$Top,

    [Parameter(ParameterSetName = 'Verb')]
    [switch]$Bottom,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$OrderFile,

    [Parameter(ParameterSetName = 'Verb')]
    [switch]$OverrideHumanRatings,

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
        # or explains the 409, and that is the actionable part. Prefer detail + candidates over
        # the raw problem-details JSON (CARD-0218).
        $raw = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($raw)) { $raw = $_.Exception.Message }
        $parsed = $null
        try { $parsed = $raw | ConvertFrom-Json } catch { $parsed = $null }
        if ($null -ne $parsed -and $parsed.detail) {
            $lines = @($parsed.detail)
            if ($parsed.code) { $lines += ("code {0}" -f $parsed.code) }
            foreach ($c in @($parsed.candidates)) {
                $lines += ("  {0}  {1}  {2}  {3}" -f $c.boardName, $c.id, $c.status, $c.title)
            }
            Write-Error ("Antiphon {0} {1} failed: {2}" -f $Method, $Path, ($lines -join [Environment]::NewLine))
        }
        else {
            Write-Error "Antiphon $Method $Path failed: $raw"
        }
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

# CARD-0350: reject at the CLI boundary, matching CardService.TryNormalizeAlias. Blank is "no
# alias" (create omits; edit sends empty to clear) and is not an error.
function Assert-Alias {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    if ($Value -match '[\r\n]') {
        Write-Error 'Alias must be a single line.'
        exit 1
    }
    $words = @($Value.Trim() -split '\s+' | Where-Object { $_ -ne '' })
    $limitSet = Get-CardLimits
    if ($words.Count -gt $limitSet.maxAliasWords) {
        Write-Error ("Alias must be at most {0} words; got {1}." -f $limitSet.maxAliasWords, $words.Count)
        exit 1
    }
    Assert-WithinLimit -Field 'Alias' -Value ($words -join ' ') -Limit $limitSet.maxAliasLength
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

function Resolve-BoardId {
    param([string]$NameOrGuid)
    $parsed = [guid]::Empty
    if ([guid]::TryParse($NameOrGuid, [ref]$parsed)) {
        return $NameOrGuid
    }
    $all = Invoke-Antiphon -Method GET -Path '/api/boards'
    $hits = @($all | Where-Object { $_.name -and $_.name.ToLowerInvariant() -eq $NameOrGuid.ToLowerInvariant() })
    if ($hits.Count -eq 0) {
        Write-Error ("No board named '{0}'. Known boards: {1}" -f $NameOrGuid, (($all | ForEach-Object { $_.name }) -join ', '))
        exit 1
    }
    if ($hits.Count -gt 1) {
        Write-Error ("'{0}' names {1} boards - pass the guid instead." -f $NameOrGuid, $hits.Count)
        exit 1
    }
    return $hits[0].id
}

function Get-CheckoutRoot {
    try {
        $toplevel = & git -C $PWD.Path rev-parse --show-toplevel 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($toplevel)) {
            return ([string]$toplevel).Trim()
        }
    }
    catch {
        # Outside a repo, or git missing: fall through to the marker, then $PWD.
    }
    $fromMarker = Get-AntiphonWorkspaceCheckout $PWD.Path
    if ($fromMarker) { return $fromMarker }
    return $PWD.Path
}

# CARD-0251: a dedicated sibling workspace carries antiphon.workspace.json pointing at the checkout.
function Get-AntiphonWorkspaceCheckout {
    param([string]$Directory)
    $marker = Join-Path $Directory 'antiphon.workspace.json'
    if (-not (Test-Path -LiteralPath $marker)) { return $null }
    try {
        $json = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if (-not $json.checkout) { return $null }
        $resolved = [System.IO.Path]::GetFullPath((Join-Path $Directory ([string]$json.checkout)))
        if (Test-Path -LiteralPath $resolved) { return $resolved }
    }
    catch {
        return $null
    }
    return $null
}

function Get-CardScopeQuery {
    $parts = @()
    if (-not [string]::IsNullOrWhiteSpace($script:resolvedBoardId)) {
        $parts += ("boardId={0}" -f $script:resolvedBoardId)
    }
    $cwd = Get-CheckoutRoot
    if (-not [string]::IsNullOrWhiteSpace($cwd)) {
        $parts += ("cwd={0}" -f [uri]::EscapeDataString($cwd))
    }
    if ($parts.Count -eq 0) { return '' }
    return '?' + ($parts -join '&')
}

function Get-CardOrFail {
    if ([string]::IsNullOrWhiteSpace($Card)) {
        Write-Error "Which card? Pass it as the first argument: card.ps1 $Verb CARD-0051 ..."
        exit 1
    }
    return Invoke-Antiphon -Method GET -Path (
        "/api/cards/{0}{1}" -f [uri]::EscapeDataString($Card.Trim()), (Get-CardScopeQuery))
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
    $prov = 'auto'
    if ($TheCard.importanceProvenance -eq 'Human') { $prov = 'human-rated' }
    $rankBit = "rank {0}" -f $TheCard.rank
    if ($null -ne $TheCard.position) { $rankBit = "rank {0} pos {1}" -f $TheCard.rank, $TheCard.position }
    Write-Output ("{0}  {1}  {2}/{3}  {4} ({5})  {6}{7}" -f `
            $TheCard.identifier, $TheCard.status, $TheCard.importance, $TheCard.urgency, $rankBit, $prov, $TheCard.title, $labels)
}

function Write-TrackerPushLine {
    param($Push)
    if ($null -eq $Push) { return }
    $name = if ($Push.trackerKind -eq 'GitHubIssues') { 'GitHub' } else { [string]$Push.trackerKind }
    $outcome = [string]$Push.outcome
    switch ($outcome) {
        'Closed'   { Write-Output ("{0,-11} closed {1} ({2})" -f $name, $Push.externalKey, $Push.url) }
        'Reopened' { Write-Output ("{0,-11} reopened {1} ({2})" -f $name, $Push.externalKey, $Push.url) }
        'InSync'   { Write-Output ("{0,-11} already in sync ({1})" -f $name, $Push.externalKey) }
        'Skipped'  { Write-Output ("{0,-11} skipped: {1}" -f $name, $Push.reason) }
        'Failed'   { Write-Output ("{0,-11} push FAILED: {1} - the next scheduled sync will retry" -f $name, $Push.reason) }
        default    { Write-Output ("{0,-11} {1} ({2})" -f $name, $outcome, $Push.externalKey) }
    }
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
    Write-Output ("alias       {0} ({1} words)" -f $l.maxAliasLength, $l.maxAliasWords)
    Write-Output ("importance  {0}" -f ($l.importanceValues -join ', '))
    Write-Output ("urgency     {0}" -f ($l.urgencyValues -join ', '))
    return
}

if ($PSBoundParameters.ContainsKey('Priority')) {
    Write-Error '-Priority is gone. Use -Importance (Low|Normal|High|Critical), -Urgency (Normal|Soon|Now), and -DueAt / -ClearDueAt.'
    exit 1
}

$script:resolvedBoardId = $null
if (-not [string]::IsNullOrWhiteSpace($Board)) {
    $script:resolvedBoardId = Resolve-BoardId $Board
}

switch ($Verb) {
    'get' {
        $theCard = Get-CardOrFail
        if ($Json) { $theCard | ConvertTo-Json -Depth 8; return }
        Write-CardLine $theCard
        Write-Output ("id          {0}" -f $theCard.id)
        Write-Output ("board       {0}" -f $theCard.boardId)
        Write-Output ("column      {0}" -f $theCard.boardColumnId)
        Write-Output ("importance  {0}" -f $theCard.importance)
        Write-Output ("urgency     {0}" -f $theCard.urgency)
        if (-not [string]::IsNullOrWhiteSpace($theCard.alias)) {
            Write-Output ("alias       {0}" -f $theCard.alias)
        }
        Write-Output ("rank        {0}" -f $theCard.rank)
        if ($null -ne $theCard.position) { Write-Output ("pos         {0}" -f $theCard.position) }
        if ($theCard.dueAt) { Write-Output ("due         {0}" -f $theCard.dueAt) }
        Write-Output ("token       {0}" -f $theCard.concurrencyToken)
        Write-Output ("revisions   {0}" -f $theCard.revisionCount)
        if ($theCard.assignedAgentName) { Write-Output ("agent       {0}" -f $theCard.assignedAgentName) }
        if ($theCard.ownerSessionId) { Write-Output ("session     {0}" -f $theCard.ownerSessionId) }
        if ($theCard.externalIssue) {
            $ext = $theCard.externalIssue.key
            if ($theCard.externalIssue.needsHumanReview) {
                $raised = $theCard.externalIssue.author
                if ([string]::IsNullOrWhiteSpace($raised)) { $raised = 'unknown' }
                $ext = $ext + (' [needs human review: raised by {0}]' -f $raised)
            }
            Write-Output ("external    {0}" -f $ext)
        }
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
        $path = "/api/cards/{0}/revisions{1}" -f [uri]::EscapeDataString($Card.Trim()), (Get-CardScopeQuery)
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

        $boardId = $script:resolvedBoardId

        $desc = Read-TextArgument -Name 'Description' -Inline $Description -Path $DescriptionFile
        $limitSet = Get-CardLimits
        Assert-WithinLimit -Field 'Title' -Value $Title -Limit $limitSet.maxTitleLength
        Assert-WithinLimit -Field 'Description' -Value $desc -Limit $limitSet.maxDescriptionLength
        if ($PSBoundParameters.ContainsKey('Alias')) { Assert-Alias -Value $Alias }

        $body = @{ title = $Title }
        if (-not [string]::IsNullOrEmpty($desc)) { $body['description'] = $desc }
        if ($PSBoundParameters.ContainsKey('Alias') -and -not [string]::IsNullOrWhiteSpace($Alias)) {
            $body['alias'] = $Alias
        }
        if ($PSBoundParameters.ContainsKey('Importance')) { $body['importance'] = $Importance }
        if ($PSBoundParameters.ContainsKey('Urgency')) { $body['urgency'] = $Urgency }
        if (-not [string]::IsNullOrWhiteSpace($DueAt)) { $body['dueAt'] = $DueAt }
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
        if ($PSBoundParameters.ContainsKey('Alias')) { Assert-Alias -Value $Alias }

        # Null means UNCHANGED for every content field, so send only what actually changed.
        $body = @{
            concurrencyToken = Resolve-Token $theCard
            reason           = $reasonText
        }
        if (-not [string]::IsNullOrWhiteSpace($Title)) { $body['title'] = $Title }
        if (-not [string]::IsNullOrEmpty($desc)) { $body['description'] = $desc }
        if ($PSBoundParameters.ContainsKey('Importance')) { $body['importance'] = $Importance }
        if ($PSBoundParameters.ContainsKey('ImportanceProvenance')) { $body['importanceProvenance'] = $ImportanceProvenance }
        if ($PSBoundParameters.ContainsKey('Urgency')) { $body['urgency'] = $Urgency }
        if ($ClearDueAt) { $body['clearDueAt'] = $true }
        elseif (-not [string]::IsNullOrWhiteSpace($DueAt)) { $body['dueAt'] = $DueAt }
        if ($Labels) { $body['labels'] = @($Labels) }
        if ($PSBoundParameters.ContainsKey('Alias')) { $body['alias'] = $Alias }
        if (-not [string]::IsNullOrWhiteSpace($By)) { $body['editedBy'] = $By }
        if ($body.Count -le 2) {
            Write-Error 'Nothing to change. Pass at least one of -Title, -Description/-DescriptionFile, -Alias, -Importance, -ImportanceProvenance, -Urgency, -DueAt, -ClearDueAt, -Labels.'
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
        Write-TrackerPushLine $result.trackerPush
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
        Write-CardLine $updated.card
        Write-TrackerPushLine $updated.trackerPush
        if ($null -ne $target) {
            Write-Output ("reopened to {0}" -f $target.name)
        }
        else {
            Write-Output ("reopened to {0}" -f $updated.card.status)
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

    'reorder' {
        $theCard = Get-CardOrFail
        $hasBefore = -not [string]::IsNullOrWhiteSpace($Before)
        $hasAfter = -not [string]::IsNullOrWhiteSpace($After)
        if ($Top -and $Bottom) {
            Write-Error 'Pass only one of -Top or -Bottom.'
            exit 1
        }
        if (($Top -or $Bottom) -and ($hasBefore -or $hasAfter)) {
            Write-Error '-Top / -Bottom cannot be combined with -Before / -After.'
            exit 1
        }
        if (-not $Top -and -not $Bottom -and -not $hasBefore -and -not $hasAfter) {
            Write-Error 'reorder needs -Before, -After, -Top or -Bottom.'
            exit 1
        }

        $reasonText = Read-TextArgument -Name 'Reason' -Inline $Reason -Path $ReasonFile
        if (-not [string]::IsNullOrWhiteSpace($reasonText)) {
            Assert-WithinLimit -Field 'Reason' -Value $reasonText -Limit (Get-CardLimits).maxReasonLength
        }
        Assert-WithinLimit -Field 'By' -Value $By -Limit (Get-CardLimits).maxActorLength

        $body = @{
            concurrencyToken = Resolve-Token $theCard
        }
        if ($hasBefore) { $body['before'] = $Before.Trim() }
        if ($hasAfter) { $body['after'] = $After.Trim() }
        if ($Top) { $body['placement'] = 'Top' }
        if ($Bottom) { $body['placement'] = 'Bottom' }
        if (-not [string]::IsNullOrWhiteSpace($reasonText)) { $body['reason'] = $reasonText }
        if (-not [string]::IsNullOrWhiteSpace($By)) { $body['editedBy'] = $By }

        $updated = Invoke-Antiphon -Method PATCH -Path ("/api/cards/{0}/position" -f $theCard.id) -Body $body
        Write-CardLine $updated
        if ($null -ne $updated.position) { Write-Output ("pos         {0}" -f $updated.position) }
        return
    }

    'order' {
        if ([string]::IsNullOrWhiteSpace($script:resolvedBoardId)) {
            Write-Error 'order needs -Board <name|guid>.'
            exit 1
        }
        if ([string]::IsNullOrWhiteSpace($OrderFile)) {
            Write-Error 'order needs -OrderFile <path> (one card ref per line, optionally "CARD-nnnn High" or "CARD-nnnn High Soon").'
            exit 1
        }
        if (-not (Test-Path -LiteralPath $OrderFile)) {
            Write-Error "Order file not found: $OrderFile"
            exit 1
        }
        $reasonText = Get-RequiredReason
        Assert-WithinLimit -Field 'By' -Value $By -Limit (Get-CardLimits).maxActorLength

        $entries = @()
        foreach ($line in @(Get-Content -LiteralPath $OrderFile)) {
            $trim = $line.Trim()
            if ([string]::IsNullOrWhiteSpace($trim)) { continue }
            $parts = @($trim -split '\s+', 3)
            $entry = @{ id = $parts[0] }
            if ($parts.Count -ge 2) { $entry['importance'] = $parts[1] }
            if ($parts.Count -ge 3) { $entry['urgency'] = $parts[2] }
            $entries += $entry
        }
        if ($entries.Count -eq 0) {
            Write-Error 'Order file has no card refs.'
            exit 1
        }

        $body = @{
            cards  = @($entries)
            reason = $reasonText
        }
        if (-not [string]::IsNullOrWhiteSpace($By)) { $body['editedBy'] = $By }
        if ($OverrideHumanRatings) { $body['overrideHumanRatings'] = $true }

        $result = Invoke-Antiphon -Method POST -Path ("/api/boards/{0}/card-order" -f $script:resolvedBoardId) -Body $body
        foreach ($c in @($result.cards)) { Write-CardLine $c }
        foreach ($s in @($result.skippedHumanRated)) {
            Write-Output ("skipped     {0}  human-rated {1}" -f $s.identifier, $s.importance)
        }
        return
    }

    'diagnose' {
        $theCard = Get-CardOrFail
        $queued = Invoke-Antiphon -Method POST -Path ("/api/cards/{0}/diagnose" -f $theCard.id) -Body @{}
        if ($NoWait) {
            if ($Json) { $queued | ConvertTo-Json -Depth 8; return }
            Write-Output ("queued      {0}  POST /api/cards/{1}/diagnose -> 202" -f $theCard.identifier, $theCard.id)
            return
        }

        $deadline = (Get-Date).ToUniversalTime().AddSeconds(120)
        $row = $null
        while ((Get-Date).ToUniversalTime() -lt $deadline) {
            $path = "/api/diagnoses?cardId={0}&kind=Labels&limit=1" -f $theCard.id
            $rows = @(Invoke-Antiphon -Method GET -Path $path)
            if ($rows.Count -gt 0) {
                $candidate = $rows[0]
                $created = $null
                if ($candidate.createdAt) {
                    try { $created = [datetime]$candidate.createdAt } catch { $created = $null }
                }
                if ($null -eq $created -or $created -ge $theCard.updatedAt) {
                    $row = $candidate
                    break
                }
            }
            Start-Sleep -Seconds 2
        }

        if ($null -eq $row) {
            Write-Error ("Timed out waiting for a diagnosis of {0}. Pass -NoWait and poll GET /api/diagnoses?cardId={1}." -f `
                    $theCard.identifier, $theCard.id)
            exit 1
        }

        if ($Json) { $row | ConvertTo-Json -Depth 8; return }
        Write-Output ("{0}  {1}  {2}" -f $theCard.identifier, $row.outcome, $row.applied)
        if ($row.reason) { Write-Output ("reason      {0}" -f $row.reason) }
        if ($row.answer) { Write-Output ("answer      {0}" -f $row.answer) }
        Write-Output ("cost        {0}  wait {1}ms" -f $row.costUsd, $row.waitMs)
        return
    }
}
