# Record the ordered (kind, level) fallback list for a Hard/Medium/Easy dispatch, without
# composing HTTP by hand. A chain is read by the NEXT create that passes -Complexity; it does
# not rewrite work that is already Queued (unless that work itself was chain-chosen and its
# snapshot alias can no longer run).
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# Verbs:
#   complexity-chain.ps1 get   [-Json]
#   complexity-chain.ps1 set   -Complexity Hard -Candidates ClaudeCode/Frontier,Codex/Frontier,Grok/Frontier
#                              [-Provenance Human|Auto] [-Reason r] [-NotAfter 2026-09-05T00:00:00Z]
#   complexity-chain.ps1 clear -Complexity Hard
#
# GRAIN. One active chain per Hard/Medium/Easy. Config defaults (Delegation:ComplexityChains) fill
# a tier with no row and ship EMPTY until a human sets them. Auto never overwrites Human
# (409 complexity_chain_human).
#
# -Candidates is Kind/Level pairs, comma-separated, order preserved. This is not a routing-pin.ps1
# verb: that script is card+stage grain and this is neither.
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('get', 'set', 'clear')]
    [string]$Verb = 'get',

    [ValidateSet('Hard', 'Medium', 'Easy')]
    [string]$Complexity,

    # Kind/Level pairs, comma-separated, e.g. ClaudeCode/Frontier,Codex/Frontier,Grok/Frontier
    [string]$Candidates,

    [ValidateSet('Human', 'Auto')]
    [string]$Provenance = 'Human',

    [string]$NotAfter,

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
        Write-Error ("{0} must be ISO-8601 UTC with a Z or numeric offset (e.g. 2026-09-05T00:00:00Z). Naive local timestamps are refused." -f $Name)
        exit 1
    }
}

function Parse-Candidates {
    param([string]$Raw)
    if ([string]::IsNullOrWhiteSpace($Raw)) {
        Write-Error 'set requires -Candidates as Kind/Level pairs, comma-separated (e.g. ClaudeCode/Frontier,Grok/Frontier).'
        exit 1
    }
    $pairs = @()
    foreach ($token in $Raw.Split(',')) {
        $item = $token.Trim()
        if ([string]::IsNullOrWhiteSpace($item)) { continue }
        $bits = $item.Split('/', 2)
        if ($bits.Count -ne 2 -or [string]::IsNullOrWhiteSpace($bits[0]) -or [string]::IsNullOrWhiteSpace($bits[1])) {
            Write-Error ("'{0}' is not a Kind/Level pair. Use ClaudeCode/Frontier, Codex/High, Grok/Frontier." -f $item)
            exit 1
        }
        $pairs += @{ agentKind = $bits[0].Trim(); modelLevel = $bits[1].Trim() }
    }
    if ($pairs.Count -eq 0) {
        Write-Error 'set requires at least one Kind/Level pair in -Candidates.'
        exit 1
    }
    return ,$pairs
}

function Format-Candidate {
    param($Candidate)
    $state = if ($Candidate.availableNow) { 'available' } else { $Candidate.unavailableReason }
    if ([string]::IsNullOrWhiteSpace($state)) { $state = 'unavailable' }
    '{0}/{1} ({2}) {3}' -f $Candidate.agentKind, $Candidate.modelLevel, $Candidate.alias, $state
}

function Format-ChainLine {
    param($Chain)
    $source = $Chain.source
    $prov = if ($null -ne $Chain.provenance -and $Chain.provenance -ne '') { $Chain.provenance } else { 'none' }
    $cands = @($Chain.candidates)
    $bits = @()
    foreach ($c in $cands) { $bits += (Format-Candidate -Candidate $c) }
    $list = if ($bits.Count -eq 0) { '(empty)' } else { $bits -join ' -> ' }
    $extra = @()
    if ($Chain.notAfter) { $extra += ("notAfter " + $Chain.notAfter) }
    if ($Chain.reason) { $extra += $Chain.reason }
    $suffix = if ($extra.Count -gt 0) { '  ' + ($extra -join ', ') } else { '' }
    '{0}  {1}/{2}  {3}{4}' -f $Chain.complexity, $source, $prov, $list, $suffix
}

switch ($Verb) {
    'get' {
        $result = Invoke-Antiphon -Method GET -Path '/api/complexity-chains'
        if ($Json) {
            $result | ConvertTo-Json -Depth 8
            break
        }
        $chains = @($result.chains)
        if ($chains.Count -eq 0) {
            Write-Output 'No complexity chains.'
            break
        }
        foreach ($chain in $chains) { Write-Output ('  ' + (Format-ChainLine -Chain $chain)) }
    }
    'set' {
        if ([string]::IsNullOrWhiteSpace($Complexity)) {
            Write-Error 'set requires -Complexity Hard|Medium|Easy.'
            exit 1
        }
        if (-not [string]::IsNullOrWhiteSpace($NotAfter)) { Assert-UtcOffset -Name 'NotAfter' -Value $NotAfter }

        $parsed = Parse-Candidates -Raw $Candidates
        $body = @{
            candidates = $parsed
            provenance = $Provenance
        }
        if (-not [string]::IsNullOrWhiteSpace($NotAfter)) { $body['notAfter'] = $NotAfter }
        if (-not [string]::IsNullOrWhiteSpace($Reason)) { $body['reason'] = $Reason }

        $chain = Invoke-Antiphon -Method PUT -Path ("/api/complexity-chains/{0}" -f $Complexity) -Body $body
        if ($Json) {
            $chain | ConvertTo-Json -Depth 8
            break
        }
        Write-Output (Format-ChainLine -Chain $chain)
    }
    'clear' {
        if ([string]::IsNullOrWhiteSpace($Complexity)) {
            Write-Error 'clear requires -Complexity Hard|Medium|Easy.'
            exit 1
        }
        Invoke-Antiphon -Method DELETE -Path ("/api/complexity-chains/{0}" -f $Complexity) -NoContent
        Write-Output ("cleared {0} (config default applies again)" -f $Complexity)
    }
}
