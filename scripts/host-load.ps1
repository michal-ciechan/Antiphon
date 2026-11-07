#requires -Version 7.0
<#
.SYNOPSIS
    Read current host load, last-hour statistics and advisory MORE-task capacity.
.DESCRIPTION
    CARD-1514 S1. GET requests only. Resolves omitted placement from the live global
    runner default and catalogue; never changes admission, budgets or placement.
    -Json emits exactly one object. Failures emit no stdout and exit nonzero.
#>
[CmdletBinding()]
param([string]$HostId, [string]$Project, [string]$Role, [switch]$Json)
$ErrorActionPreference = 'Stop'
$http = $null
function Get-LoadRead {
    param([string]$Path)
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $script:Api + $Path)
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(3))
    $response = $null
    try {
        if ($env:ANTIPHON_TASK_TOKEN) { $null = $request.Headers.TryAddWithoutValidation('X-Antiphon-Task-Token', $env:ANTIPHON_TASK_TOKEN) }
        $response = $script:http.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $deadline.Token).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw 'read_failed' }
        $stream = $response.Content.ReadAsStreamAsync($deadline.Token).GetAwaiter().GetResult()
        $body = [IO.MemoryStream]::new()
        try {
            $buffer = [byte[]]::new(8192)
            do {
                $read = $stream.ReadAsync($buffer, 0, $buffer.Length, $deadline.Token).GetAwaiter().GetResult()
                if ($body.Length + $read -gt 1048576) { throw 'read_failed' }
                $body.Write($buffer, 0, $read)
            } while ($read -ne 0)
            $text = [Text.UTF8Encoding]::new($false, $true).GetString($body.ToArray())
            return ($text | ConvertFrom-Json -AsHashtable -Depth 100 -ErrorAction Stop)
        } finally { $body.Dispose(); $stream.Dispose() }
    } finally { if ($null -ne $response) { $response.Dispose() }; $deadline.Dispose(); $request.Dispose() }
}
function Get-CanonicalRunner {
    param([string]$Value)
    if ($Value -ceq 'local' -or $Value -ceq 'desktop') { return 'desktop' }
    return $Value
}
function Require-Fields {
    param($Object, [string[]]$Fields)
    if ($Object -isnot [Collections.IDictionary]) { throw 'invalid_response' }
    foreach ($field in $Fields) { if (-not $Object.Contains($field)) { throw 'invalid_response' } }
}
function Show-Number {
    param($Value)
    if ($null -eq $Value) { return 'unknown' }
    return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
}
try {
    $script:Api = if ($env:ANTIPHON_API) { $env:ANTIPHON_API.TrimEnd('/') } else { 'http://localhost:17202' }
    $baseUri = [uri]$script:Api
    if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -notin @('http','https') -or $baseUri.UserInfo) { throw 'invalid_api' }
    if ($Project) { $parsed = [guid]::Empty; if (-not [guid]::TryParse($Project, [ref]$parsed)) { throw 'invalid_project' }; $Project = $parsed.ToString('D') }
    $script:http = [Net.Http.HttpClient]::new()
    $script:http.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $revision = $null
    if ([string]::IsNullOrWhiteSpace($HostId)) {
        $defaults = Get-LoadRead '/api/runner-defaults'
        Require-Fields $defaults @('globalRunnerId','revision')
        if ([string]::IsNullOrWhiteSpace([string]$defaults.globalRunnerId)) { throw 'placement_missing' }
        $HostId = [string]$defaults.globalRunnerId
        $revision = $defaults.revision
        $source = 'globalDefault'
    } else { $source = 'explicit' }
    $catalogue = @(Get-LoadRead '/api/session-runners')
    $canonical = Get-CanonicalRunner $HostId
    $matches = @($catalogue | Where-Object { (Get-CanonicalRunner ([string]$_.runnerId)) -ceq $canonical })
    if ($matches.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$matches[0].runnerId)) { throw 'placement_missing' }
    $runner = [string]$matches[0].runnerId
    $query = [Collections.Generic.List[string]]::new()
    if ($Project) { $query.Add('projectId=' + [uri]::EscapeDataString($Project)) }
    if ($Role) { $query.Add('role=' + [uri]::EscapeDataString($Role)) }
    $path = '/api/hosts/' + [uri]::EscapeDataString($runner) + '/load'
    if ($query.Count -gt 0) { $path += '?' + ($query -join '&') }
    $load = Get-LoadRead $path
    Require-Fields $load @('schemaVersion','hostId','physicalHostKey','observedAt','current','currentAgeSeconds','lastHour','coverage','policy','authority','decision','recommendation')
    if ($load.schemaVersion -ne 1 -or (Get-CanonicalRunner ([string]$load.hostId)) -cne $canonical) { throw 'invalid_response' }
    Require-Fields $load.policy @('mode','revision','shadow','commandThresholdPercent','dispatchThresholdPercent','perTaskCpuPoints')
    Require-Fields $load.recommendation @('recommendedAdditionalTasks','scope','missingConstraints','pressurePercent','startupDebtPercent','cpuHeadroom','hostRemaining','runnerSeatsRemaining','projectRemaining','stageRemaining','inputsAt','reasons')
    $additional = $load.recommendation.recommendedAdditionalTasks
    if ($null -eq $additional -or $additional -is [string] -or -not [double]::IsFinite([double]$additional) -or $additional -lt 0 -or [math]::Floor([double]$additional) -ne $additional) { throw 'invalid_response' }
    if ($load.policy.shadow -ne $true) { throw 'invalid_response' }
    $load.placement = @{ runnerId = $runner; source = $source; revision = $revision }
    if ($Json) { [Console]::Out.WriteLine(($load | ConvertTo-Json -Depth 100 -Compress)) }
    else {
        $cpu = if ($null -ne $load.current) { Show-Number $load.current.cpuPercent } else { 'unknown' }
        $summary = $load.lastHour.cpuPercent
        [Console]::Out.WriteLine(('Host {0}; age={1}s; mode={2}; shadow={3}' -f $runner, (Show-Number $load.currentAgeSeconds), $load.policy.mode, $load.policy.shadow))
        [Console]::Out.WriteLine(('Current CPU={0}%; load1={1}; hour avg/min/max/p95={2}/{3}/{4}/{5}; coverage={6}; approximate={7}' -f $cpu, (Show-Number $load.current.load1), (Show-Number $summary.avg), (Show-Number $summary.min), (Show-Number $summary.max), (Show-Number $summary.p95), (Show-Number $load.coverage), $summary.p95Approximate))
        [Console]::Out.WriteLine(('MORE tasks={0}; scope={1}; pressure={2}; startup debt={3}; CPU headroom={4}; host/seats/project/stage={5}/{6}/{7}/{8}' -f $additional, $load.recommendation.scope, (Show-Number $load.recommendation.pressurePercent), (Show-Number $load.recommendation.startupDebtPercent), $load.recommendation.cpuHeadroom, (Show-Number $load.recommendation.hostRemaining), (Show-Number $load.recommendation.runnerSeatsRemaining), (Show-Number $load.recommendation.projectRemaining), (Show-Number $load.recommendation.stageRemaining)))
        [Console]::Out.WriteLine(('Thresholds command/dispatch={0}/{1}; heavy ceiling={2}; authority occupancy={3}; missing={4}; reasons={5}' -f $load.policy.commandThresholdPercent, $load.policy.dispatchThresholdPercent, (Show-Number $load.authority.hardCeiling), (Show-Number $load.authority.occupied), ($load.recommendation.missingConstraints -join ','), ($load.recommendation.reasons -join ',')))
    }
    exit 0
} catch {
    # Remote bodies and exception text can echo credentials; neither is diagnostic output.
    [Console]::Error.WriteLine('host-load: read, placement or response validation failed; no capacity suggestion is available.')
    exit 1
} finally { if ($null -ne $script:http) { $script:http.Dispose() } }
