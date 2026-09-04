# Record the ordered (kind, level) fallback list for a Hard/Medium/Easy dispatch, without
# composing HTTP by hand. A chain is read by the NEXT create that passes -Complexity; it does
# not rewrite work that is already Queued (unless that work itself was chain-chosen and its
# snapshot alias can no longer run).
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# Verbs:
#   complexity-chain.ps1 get   [-Role Plan] [-Json]
#   complexity-chain.ps1 set   [-Role Plan|Any] -Complexity Hard -Candidates ClaudeCode/Frontier,Codex/Frontier
#                              [-Provenance Human|Auto] [-Reason r] [-NotAfter 2026-09-05T00:00:00Z]
#   complexity-chain.ps1 clear [-Role Plan|Any] -Complexity Hard
#
# GRAIN. One active chain per (Role?, Hard/Medium/Easy). Role omitted or Any writes the any-role
# row. A walk on (role, complexity) reads the cell, then the any-role row, then the config default,
# then Blocked. A Required pin still bypasses the cell. Config defaults (Delegation:ComplexityChains)
# fill a tier with no row and ship EMPTY until a human sets them. Auto never overwrites Human
# (409 complexity_chain_human), and an Auto cell write is refused when the any-role row is Human.
#
# -Candidates is Kind/Level pairs, comma-separated, order preserved. This is not a routing-pin.ps1
# verb: that script is card+stage grain and this is neither. The script always uses the
# three-segment path (/any/Hard or /Plan/Hard); the two-segment alias exists for CARD-0090 callers.
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('get', 'set', 'clear')]
    [string]$Verb = 'get',

    [ValidateSet('Hard', 'Medium', 'Easy')]
    [string]$Complexity,

    [ValidateSet('Investigate', 'Plan', 'TestDesign', 'Code', 'Review', 'Debug', 'Coverage', 'Docs', 'Commit', 'Test', 'Deploy', 'Merge', 'Custom', 'Any')]
    [string]$Role,

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

function Get-RoleSegment {
    if ([string]::IsNullOrWhiteSpace($Role) -or $Role -eq 'Any') { return 'any' }
    return $Role
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

function Get-ChainLabel {
    param($Chain)
    if ($null -ne $Chain.role -and $Chain.role -ne '') {
        return ('{0}/{1}' -f $Chain.role, $Chain.complexity)
    }
    return $Chain.complexity
}

function Format-ChainLine {
    param($Chain, [switch]$Effective)
    $label = Get-ChainLabel -Chain $Chain
    $cands = @($Chain.candidates)
    $bits = @()
    foreach ($c in $cands) { $bits += (Format-Candidate -Candidate $c) }
    if ($Effective) {
        $from = $Chain.resolvedFrom
        $marker = switch ($from) {
            'role' { '(own)' }
            'any' { '(via any)' }
            'config' { '(via config)' }
            default { '(empty)' }
        }
        $list = if ($bits.Count -eq 0) {
            if ($from -eq 'none' -or [string]::IsNullOrWhiteSpace($from)) { '(empty - Blocked until set)' } else { '(empty)' }
        }
        else { $bits -join ' -> ' }
        return '{0}  {1}  {2}' -f $label, $marker, $list
    }

    $source = $Chain.source
    $prov = if ($null -ne $Chain.provenance -and $Chain.provenance -ne '') { $Chain.provenance } else { 'none' }
    $list = if ($bits.Count -eq 0) { '(empty)' } else { $bits -join ' -> ' }
    $extra = @()
    if ($Chain.notAfter) { $extra += ("notAfter " + $Chain.notAfter) }
    if ($Chain.reason) { $extra += $Chain.reason }
    $suffix = if ($extra.Count -gt 0) { '  ' + ($extra -join ', ') } else { '' }
    '{0}  {1}/{2}  {3}{4}' -f $label, $source, $prov, $list, $suffix
}

switch ($Verb) {
    'get' {
        $effective = -not [string]::IsNullOrWhiteSpace($Role) -and $Role -ne 'Any'
        $path = if ($effective) { '/api/complexity-chains?role={0}' -f $Role } else { '/api/complexity-chains' }
        $result = Invoke-Antiphon -Method GET -Path $path
        if ($Json) {
            $result | ConvertTo-Json -Depth 8
            break
        }
        $chains = @($result.chains)
        if ($chains.Count -eq 0) {
            Write-Output 'No complexity chains.'
            break
        }
        foreach ($chain in $chains) {
            Write-Output ('  ' + (Format-ChainLine -Chain $chain -Effective:$effective))
        }
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

        $path = '/api/complexity-chains/{0}/{1}' -f (Get-RoleSegment), $Complexity
        $chain = Invoke-Antiphon -Method PUT -Path $path -Body $body
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
        $seg = Get-RoleSegment
        Invoke-Antiphon -Method DELETE -Path ('/api/complexity-chains/{0}/{1}' -f $seg, $Complexity) -NoContent
        $label = if ($seg -eq 'any') { $Complexity } else { '{0}/{1}' -f $seg, $Complexity }
        Write-Output ("cleared {0} (config default applies again)" -f $label)
    }
}
