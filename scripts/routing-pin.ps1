# Record WHICH agent/model a card+stage (or a whole stage) must run on, without composing HTTP by
# hand. A pin is read by the NEXT create; it does not rewrite work that is already Queued.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# Verbs:
#   routing-pin.ps1 get   [-Card CARD-0304] [-Role Plan] [-Json]
#   routing-pin.ps1 set   -Role Plan [-Card CARD-0304] -Provenance Human -Strength Required
#                         [-Kind Codex] [-Level Frontier]
#                         [-Candidates ClaudeCode/Frontier,ClaudeCode/High,Codex/Frontier]
#                         [-Forbidden fable,opus]
#                         [-NotBefore 2026-09-03T00:00:00Z] [-NotAfter ...] [-Agent <guid>] [-Reason r]
#   routing-pin.ps1 clear -Role Plan [-Card CARD-0304]
#
# GRAIN. -Card pins ONE card's stage; omitting it pins the stage for every card. One active pin per
# grain; a card pin outranks the stage pin as a whole row, which is how a card can deliberately use
# an alias the stage forbids.
#
# PROVENANCE IS THE POINT. -Provenance Human means "the operator said so": a later Auto write onto
# that row is refused 409 routing_pin_human, so a general policy shift cannot silently wash away a
# per-card decision. Use Auto for what YOU decided (role defaults, a quota fallback); it is freely
# replaceable.
#
# -NotBefore / -NotAfter are ISO-8601 UTC: a trailing Z or a numeric offset is required. A dated pin
# does NOT refuse the create - the work is queued and the dispatcher holds it until the instant. That
# is deliberately the opposite of a model-availability hold (model-availability.ps1), which 409s a
# new create outright. This is not a card.ps1 overload.
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('get', 'set', 'clear')]
    [string]$Verb = 'get',

    [ValidateSet('Investigate', 'Plan', 'TestDesign', 'Code', 'Review', 'Debug', 'Coverage', 'Docs', 'Commit', 'Test', 'Deploy', 'Merge', 'Custom')]
    [string]$Role,

    [string]$Card,

    [ValidateSet('Human', 'Auto')]
    [string]$Provenance = 'Human',

    [ValidateSet('Required', 'Preferred')]
    [string]$Strength = 'Preferred',

    [ValidateSet('ClaudeCode', 'Grok', 'Codex')]
    [string]$Kind,

    [ValidateSet('Frontier', 'High', 'Medium', 'Low')]
    [string]$Level,

    # Comma-separated Kind/Level or bare Kind tokens, order preserved.
    # e.g. ClaudeCode/Frontier,ClaudeCode/High,Grok
    [string]$Candidates,

    # Comma-separated canonical aliases this stage may not use, e.g. fable or fable,opus.
    [string]$Forbidden,

    [string]$NotBefore,

    [string]$NotAfter,

    # A STANDING agent (guid) to run the stage on. Pool delegates are refused.
    [string]$Agent,

    [string]$Reason,

    [switch]$Json
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
    param([string]$Method, [string]$Path, $Body, [switch]$NoContent)
    $uri = "$api$Path"
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 8 -Compress
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
            $params = @{
                Method      = $Method
                Uri         = $uri
                Headers     = $headers
                Body        = $bytes
                ContentType = 'application/json; charset=utf-8'
            }
            if ($NoContent) {
                Invoke-RestMethod @params | Out-Null
                return $null
            }
            return Invoke-RestMethod @params
        }
        if ($NoContent) {
            Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers | Out-Null
            return $null
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    catch {
        $raw = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($raw)) { $raw = $_.Exception.Message }
        $parsed = $null
        try { $parsed = $raw | ConvertFrom-Json } catch { $parsed = $null }
        if ($null -ne $parsed -and $parsed.detail) {
            $lines = @($parsed.detail)
            if ($parsed.code) { $lines += ("code {0}" -f $parsed.code) }
            Write-Error ("Antiphon {0} {1} failed: {2}" -f $Method, $Path, ($lines -join [Environment]::NewLine))
        }
        else {
            Write-Error "Antiphon $Method $Path failed: $raw"
        }
        exit 1
    }
}

function Assert-UtcOffset {
    param([string]$Name, [string]$Value)
    if ($Value -notmatch '(Z|[+-][0-9]{2}:[0-9]{2})$') {
        Write-Error ("{0} must be ISO-8601 UTC with a Z or numeric offset (e.g. 2026-09-03T00:00:00Z). Naive local timestamps are refused." -f $Name)
        exit 1
    }
}

function Get-PinQuery {
    $parts = @()
    if (-not [string]::IsNullOrWhiteSpace($Card)) { $parts += ("card=" + [uri]::EscapeDataString($Card)) }
    if (-not [string]::IsNullOrWhiteSpace($Role)) { $parts += ("role=" + [uri]::EscapeDataString($Role)) }
    if ($parts.Count -eq 0) { return '/api/routing-pins' }
    return '/api/routing-pins?' + ($parts -join '&')
}

function Parse-PinCandidates {
    param([string]$Raw)
    if ([string]::IsNullOrWhiteSpace($Raw)) {
        Write-Error 'set -Candidates needs Kind/Level or bare Kind tokens, comma-separated (e.g. ClaudeCode/Frontier,Grok).'
        exit 1
    }
    $pairs = [System.Collections.Generic.List[hashtable]]::new()
    foreach ($token in $Raw.Split(',')) {
        $item = $token.Trim()
        if ([string]::IsNullOrWhiteSpace($item)) { continue }
        $bits = $item.Split('/', 2)
        if ($bits.Count -eq 2) {
            if ([string]::IsNullOrWhiteSpace($bits[0]) -or [string]::IsNullOrWhiteSpace($bits[1])) {
                Write-Error ("'{0}' is not a Kind/Level pair or a bare Kind. Use ClaudeCode/Frontier or Grok." -f $item)
                exit 1
            }
            if ($bits[0].Trim() -eq '*') {
                Write-Error ("'{0}' is a level-only token; use -Level for a one-candidate pin, not -Candidates." -f $item)
                exit 1
            }
            $pairs.Add(@{ agentKind = $bits[0].Trim(); modelLevel = $bits[1].Trim() })
        }
        else {
            $pairs.Add(@{ agentKind = $item })
        }
    }
    if ($pairs.Count -eq 0) {
        Write-Error 'set -Candidates needs at least one Kind/Level or bare Kind token.'
        exit 1
    }
    return @($pairs.ToArray())
}

function Format-CandidateToken {
    param($Candidate)
    $kind = $Candidate.agentKind
    $level = $Candidate.modelLevel
    $alias = $Candidate.alias
    if ($kind -and $level -and $alias) { return ('{0}/{1} ({2})' -f $kind, $level, $alias) }
    if ($kind -and $level) { return ('{0}/{1}' -f $kind, $level) }
    if ($kind) { return "$kind" }
    if ($level) { return "$level" }
    return 'no kind/level constraint'
}

function Format-PinLine {
    param($Pin)
    $grain = if ($Pin.cardIdentifier) { $Pin.cardIdentifier } elseif ($Pin.cardId) { $Pin.cardId } else { 'stage-wide' }
    $cands = @($Pin.candidates)
    $route = @()
    if ($cands.Count -gt 0) {
        $head = Format-CandidateToken -Candidate $cands[0]
        $route += $head
        if ($cands.Count -gt 1) {
            $rest = @()
            for ($i = 1; $i -lt $cands.Count; $i++) {
                $rest += (Format-CandidateToken -Candidate $cands[$i])
            }
            $route += ('+{0}: {1}' -f ($cands.Count - 1), ($rest -join ', '))
        }
    }
    else {
        if ($Pin.agentKind) { $route += $Pin.agentKind }
        if ($Pin.modelLevel) { $route += $Pin.modelLevel }
        if ($Pin.modelAlias) { $route += ("({0})" -f $Pin.modelAlias) }
        if ($route.Count -eq 0) { $route += 'no kind/level constraint' }
    }
    if ($Pin.agentId) { $route += ("agent " + $Pin.agentId) }
    $extra = @()
    $forbidden = @($Pin.forbiddenAliases)
    if ($forbidden.Count -gt 0) { $extra += ("forbids " + ($forbidden -join '/')) }
    if ($Pin.notBefore) { $extra += ("notBefore " + $Pin.notBefore) }
    if ($Pin.notAfter) { $extra += ("notAfter " + $Pin.notAfter) }
    $suffix = if ($extra.Count -gt 0) { '  ' + ($extra -join ', ') } else { '' }
    '{0}  {1}  {2} {3}  {4}{5}  {6}' -f $grain, $Pin.role, $Pin.provenance, $Pin.strength, ($route -join ' '), $suffix, $Pin.reason
}

switch ($Verb) {
    'get' {
        $result = Invoke-Antiphon -Method GET -Path (Get-PinQuery)
        if ($Json) {
            $result | ConvertTo-Json -Depth 8
            break
        }
        $pins = @($result.pins)
        if ($pins.Count -eq 0) {
            Write-Output 'No active routing pins.'
            break
        }
        foreach ($pin in $pins) { Write-Output ('  ' + (Format-PinLine -Pin $pin)) }
    }
    'set' {
        if ([string]::IsNullOrWhiteSpace($Role)) {
            Write-Error 'set requires -Role. A pin is per STAGE; add -Card to narrow it to one card.'
            exit 1
        }
        if (-not [string]::IsNullOrWhiteSpace($Candidates) -and (
                -not [string]::IsNullOrWhiteSpace($Kind) -or -not [string]::IsNullOrWhiteSpace($Level))) {
            Write-Error 'Send either the agentKind/modelLevel shorthand or candidates, not both.'
            exit 1
        }
        if (-not [string]::IsNullOrWhiteSpace($NotBefore)) { Assert-UtcOffset -Name 'NotBefore' -Value $NotBefore }
        if (-not [string]::IsNullOrWhiteSpace($NotAfter)) { Assert-UtcOffset -Name 'NotAfter' -Value $NotAfter }

        $body = @{
            role       = $Role
            provenance = $Provenance
            strength   = $Strength
        }
        if (-not [string]::IsNullOrWhiteSpace($Card)) { $body['card'] = $Card }
        if (-not [string]::IsNullOrWhiteSpace($Candidates)) {
            $body['candidates'] = @(Parse-PinCandidates -Raw $Candidates)
        }
        else {
            if (-not [string]::IsNullOrWhiteSpace($Kind)) { $body['agentKind'] = $Kind }
            if (-not [string]::IsNullOrWhiteSpace($Level)) { $body['modelLevel'] = $Level }
        }
        if (-not [string]::IsNullOrWhiteSpace($Agent)) { $body['agentId'] = $Agent }
        if (-not [string]::IsNullOrWhiteSpace($Forbidden)) {
            $body['forbiddenAliases'] = @($Forbidden.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        }
        if (-not [string]::IsNullOrWhiteSpace($NotBefore)) { $body['notBefore'] = $NotBefore }
        if (-not [string]::IsNullOrWhiteSpace($NotAfter)) { $body['notAfter'] = $NotAfter }
        if (-not [string]::IsNullOrWhiteSpace($Reason)) { $body['reason'] = $Reason }

        $pin = Invoke-Antiphon -Method PUT -Path '/api/routing-pins' -Body $body
        if ($Json) {
            $pin | ConvertTo-Json -Depth 8
            break
        }
        Write-Output (Format-PinLine -Pin $pin)
    }
    'clear' {
        if ([string]::IsNullOrWhiteSpace($Role)) {
            Write-Error 'clear requires -Role (and -Card when you mean one card rather than the stage).'
            exit 1
        }
        # Addressed by grain, deleted by id: the id is what the API takes, and looking it up here
        # keeps the caller from having to hold one.
        $result = Invoke-Antiphon -Method GET -Path (Get-PinQuery)
        $wantCard = -not [string]::IsNullOrWhiteSpace($Card)
        $match = @($result.pins) | Where-Object {
            $_.role -eq $Role -and (($wantCard -and $_.cardId) -or (-not $wantCard -and -not $_.cardId))
        } | Select-Object -First 1
        if ($null -eq $match) {
            Write-Output 'No active routing pin for that grain.'
            break
        }
        Invoke-Antiphon -Method DELETE -Path ("/api/routing-pins/{0}" -f $match.id) -NoContent
        Write-Output ("cleared {0}" -f (Format-PinLine -Pin $match))
    }
}
