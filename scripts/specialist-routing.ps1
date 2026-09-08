# Inspect or configure the Check interpreter's declared routing. Configuration does not qualify a model.
# ASCII-only for Windows PowerShell 5.1. Never print the task-token header.
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('inspect', 'set', 'revalidate')]
    [string]$Verb = 'inspect',
    [Parameter(Mandatory = $true)]
    [guid]$Agent,
    [string]$Candidates,
    [switch]$Disable
)
$ErrorActionPreference = 'Stop'
$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$uri = $api.TrimEnd('/') + '/api/agents/' + $Agent.ToString('D') + '/specialist-routing'
$headers = @{}
if (![string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}
if ($Verb -ne 'set' -and ($PSBoundParameters.ContainsKey('Candidates') -or $Disable)) {
    throw '-Candidates and -Disable apply only to set.'
}
$pairs = @()
if ($Verb -eq 'set' -and ![string]::IsNullOrWhiteSpace($Candidates)) {
    $kinds = @{claudecode='ClaudeCode'; codex='Codex'}
    $levels = @{frontier='Frontier'; high='High'; medium='Medium'; low='Low'}
    foreach ($pair in $Candidates.Split(',')) {
        $parts = $pair.Trim().Split('/')
        if ($parts.Count -ne 2 -or !$kinds.ContainsKey($parts[0].Trim()) -or !$levels.ContainsKey($parts[1].Trim())) {
            throw "Invalid candidate '$pair'; use a complete ClaudeCode/Level or Codex/Level pair."
        }
        $pairs += @{agentKind=$kinds[$parts[0].Trim()]; modelLevel=$levels[$parts[1].Trim()]}
    }
}
$current = Invoke-RestMethod -Uri $uri -Headers $headers
switch ($Verb) {
    'inspect' { $result = $current }
    'set' {
        if ($pairs.Count -eq 0) {
            if (!$Disable) { throw 'set requires -Candidates Kind/Level,...' }
            $pairs = @($current.candidates)
        }
        $body = @{concurrencyToken=$current.concurrencyToken; enabled=(!$Disable); candidates=@($pairs)} | ConvertTo-Json -Depth 5
        $result = Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -ContentType 'application/json' -Body $body
    }
    'revalidate' {
        if (!$current.concurrencyToken) { throw 'Declare a routing chain before requesting revalidation.' }
        $body = @{concurrencyToken=$current.concurrencyToken} | ConvertTo-Json
        $result = Invoke-RestMethod -Uri ($uri + '/revalidate') -Method Post -Headers $headers -ContentType 'application/json' -Body $body
    }
}
$result | ConvertTo-Json -Depth 8
