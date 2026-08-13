#requires -Version 7
<#
.SYNOPSIS
  Sanitized smoke for AI Agent TUI profiles (REST + agent selection + optional exact reply).

.PARAMETER BaseUrl
  Frontend or API base, e.g. http://localhost:17282 (proxied) or http://localhost:17281

.PARAMETER AgentName
  Agent display name to patch/start (default Atlas-Orchestrator).

.PARAMETER ProfileName
  TUI profile display name (created/updated if missing).

.PARAMETER ModelId
  Optional exact model. Omit or empty for runner default (no --model).

.PARAMETER ExpectedReply
  Exact assistant reply substring required after messaging the agent.

.PARAMETER Canary
  Optional secret canary scanned in all retained evidence (fail if present).

.PARAMETER SkipLiveMessage
  Only configure profile/agent selection; do not start or message the agent.
#>
param(
    [string]$BaseUrl = "http://localhost:17282",
    [string]$AgentName = "Atlas-Orchestrator",
    [string]$ProfileName = "OpenCode Gateway",
    [string]$ModelId = "",
    [string]$ExpectedReply = "Atlas OpenCode default verified.",
    [string]$Canary = "",
    [string]$OcgPath = "C:\Users\mike.ciechan\.local\bin\ocg.ps1",
    [switch]$SkipLiveMessage,
    [string]$EvidenceDir = ""
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")
if (-not $EvidenceDir) {
    $EvidenceDir = Join-Path $env:TEMP ("antiphon-agent-tui-smoke-" + [guid]::NewGuid().ToString("N"))
}
New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null

function Invoke-Json {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null
    )
    $uri = "$BaseUrl$Path"
    $params = @{
        Method      = $Method
        Uri         = $uri
        ContentType = "application/json"
    }
    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }
    $response = Invoke-WebRequest @params
    $text = $response.Content
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
}

function Save-Evidence([string]$Name, [object]$Value) {
    $path = Join-Path $EvidenceDir $Name
    if ($Value -is [string]) {
        Set-Content -LiteralPath $path -Value $Value -Encoding utf8
    } else {
        ($Value | ConvertTo-Json -Depth 30) | Set-Content -LiteralPath $path -Encoding utf8
    }
    if ($Canary -and (Get-Content -LiteralPath $path -Raw).Contains($Canary)) {
        throw "Canary secret leaked into evidence file $Name"
    }
    return $path
}

Write-Host "Evidence: $EvidenceDir"
Write-Host "Checking health…"
$health = Invoke-WebRequest -Uri "$BaseUrl/health" -Method GET
Save-Evidence "health.txt" $health.Content | Out-Null

$profiles = Invoke-Json GET "/api/agent-tui/profiles"
Save-Evidence "profiles-before.json" $profiles | Out-Null
$profile = @($profiles) | Where-Object { $_.displayName -eq $ProfileName } | Select-Object -First 1

$launchArgs = @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $OcgPath, "--auto", "--mini"
)
$versionArgs = @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $OcgPath, "--version"
)
$discoveryArgs = @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $OcgPath, "models"
)

$writeBody = @{
    displayName             = $ProfileName
    kind                    = "OpenCode"
    isEnabled               = $true
    isDefault               = $false
    executable              = "pwsh.exe"
    arguments               = $launchArgs
    discoveryArguments      = $discoveryArgs
    versionArguments        = $versionArgs
    workingDirectory        = $null
    authenticationMode      = "WrapperManaged"
    nonSecretEnvironment    = @{}
    secretEnvironmentNames  = @()
    modelArgumentName       = "--model"
    guidance                = "Local OpenCode gateway via ocg.ps1 (wrapper-managed auth)."
    models                  = @()
}

if ($null -eq $profile) {
    Write-Host "Creating profile '$ProfileName'…"
    $profile = Invoke-Json POST "/api/agent-tui/profiles" $writeBody
} else {
    Write-Host "Updating profile '$ProfileName' revision $($profile.revision)…"
    $writeBody.expectedRevision = $profile.revision
    $profile = Invoke-Json PATCH "/api/agent-tui/profiles/$($profile.id)" $writeBody
}
Save-Evidence "profile.json" $profile | Out-Null

Write-Host "Refreshing models…"
$refresh = Invoke-Json POST "/api/agent-tui/profiles/$($profile.id)/models/refresh"
Save-Evidence "models-refresh.json" $refresh | Out-Null

Write-Host "Validating…"
$validation = Invoke-Json POST "/api/agent-tui/profiles/$($profile.id)/validate"
Save-Evidence "validation.json" $validation | Out-Null

$agents = Invoke-Json GET "/api/agents"
$agent = @($agents) | Where-Object { $_.name -eq $AgentName } | Select-Object -First 1
if ($null -eq $agent) { throw "Agent '$AgentName' not found" }

$modelValue = if ([string]::IsNullOrWhiteSpace($ModelId)) { $null } else { $ModelId }
Write-Host "Patching agent '$AgentName' → profile $($profile.id), model=$modelValue"
$updated = Invoke-Json PATCH "/api/agents/$($agent.id)" @{
    name                    = $agent.name
    workingDirectory        = $agent.workingDirectory
    details                 = $agent.details
    defaultWorkflowTemplateId = $agent.defaultWorkflowTemplateId
    assignmentPolicy        = $agent.assignmentPolicy
    boardId                 = $agent.boardId
    alwaysOn                = $agent.alwaysOn
    remoteControlEnabled    = $agent.remoteControlEnabled
    systemPromptAppend      = $agent.systemPromptAppend
    tuiProfileId            = $profile.id
    modelId                 = $modelValue
}
Save-Evidence "agent.json" $updated | Out-Null

$metrics = (Invoke-WebRequest -Uri "$BaseUrl/metrics/agent-tui" -Method GET).Content
Save-Evidence "metrics.txt" $metrics | Out-Null
if ($Canary -and $metrics.Contains($Canary)) {
    throw "Canary found in metrics output"
}

if ($SkipLiveMessage) {
    Write-Host "SkipLiveMessage set — configuration smoke complete."
    exit 0
}

Write-Host "Stopping agent (if running)…"
try { Invoke-Json POST "/api/agents/$($agent.id)/stop" | Out-Null } catch { }

Write-Host "Starting agent (fresh)…"
$started = Invoke-Json POST "/api/agents/$($agent.id)/start" @{ fresh = $true; remoteControl = $false }
Save-Evidence "agent-started.json" $started | Out-Null

$sessionId = $started.liveSession.id
if (-not $sessionId) { $sessionId = $started.persistentSessionId }
if (-not $sessionId) { throw "No live session id after start" }

$prompt = "Reply with exactly: $ExpectedReply"
Write-Host "Sending prompt to session $sessionId…"
try {
    Invoke-Json POST "/api/sessions/$sessionId/messages" @{ message = $prompt } | Out-Null
} catch {
    # Fallback channel-style endpoint names vary; try input path.
    Invoke-Json POST "/api/sessions/$sessionId/input" @{ input = $prompt } | Out-Null
}

$deadline = (Get-Date).AddSeconds(120)
$found = $false
$buffer = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    try {
        $buffer = Invoke-Json GET "/api/sessions/$sessionId/buffer"
        Save-Evidence "buffer-latest.json" $buffer | Out-Null
        $text = if ($buffer.buffer) { $buffer.buffer } else { ($buffer | ConvertTo-Json -Depth 5) }
        if ($text -and $text.Contains($ExpectedReply)) {
            $found = $true
            break
        }
    } catch {
        # keep polling
    }
}

if (-not $found) {
    throw "Expected reply not observed within timeout: $ExpectedReply"
}

# Soft check: live selection metadata
$agentAfter = Invoke-Json GET "/api/agents/$($agent.id)"
Save-Evidence "agent-after.json" $agentAfter | Out-Null
if ($agentAfter.liveSessionSelection) {
    Write-Host ("Live selection: revision={0} model={1} pendingRestart={2}" -f `
        $agentAfter.liveSessionSelection.tuiProfileRevisionId, `
        $agentAfter.liveSessionSelection.effectiveModelId, `
        $agentAfter.liveSessionSelection.pendingRestart)
}

Write-Host "SMOKE OK — profile='$ProfileName' model='$ModelId' reply verified."
Write-Host "Evidence retained at $EvidenceDir"
exit 0
