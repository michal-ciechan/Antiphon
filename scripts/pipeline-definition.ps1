# Inspect and select shared, versioned pipeline definitions. ASCII-only for Windows PowerShell 5.1.
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('list', 'get', 'clone', 'revise', 'set-board', 'set-project')]
    [string]$Verb,
    [string]$Id,
    [string]$Name,
    [string]$StagesFile,
    [Alias('ChangeNote')]
    [string]$Note,
    [string]$Board,
    [string]$Project,
    [switch]$Inherit,
    [switch]$IncludeArchived,
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

function Invoke-PipelineApi {
    param([string]$Method, [string]$Path, $Body)
    $params = @{ Method = $Method; Uri = "$api$Path"; Headers = $headers }
    if ($null -ne $Body) {
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth 20 -Compress))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

function Require-Value {
    param([string]$Value, [string]$Label)
    if ([string]::IsNullOrWhiteSpace($Value)) { throw "$Label is required for $Verb." }
}

if ($Inherit -and -not ($Verb -eq 'set-board' -or $Verb -eq 'set-project')) {
    throw '-Inherit is only valid for set-board or set-project.'
}

switch ($Verb) {
    'list' {
        $path = '/api/pipeline-definitions'
        if ($IncludeArchived) { $path += '?includeArchived=true' }
        $result = Invoke-PipelineApi 'GET' $path $null
    }
    'get' {
        Require-Value $Id '-Id'
        $result = Invoke-PipelineApi 'GET' "/api/pipeline-definitions/$Id" $null
    }
    'clone' {
        Require-Value $Id '-Id'
        Require-Value $Name '-Name'
        $result = Invoke-PipelineApi 'POST' "/api/pipeline-definitions/$Id/clone" @{ name = $Name }
    }
    'revise' {
        Require-Value $Id '-Id'
        Require-Value $StagesFile '-StagesFile'
        if (-not (Test-Path -LiteralPath $StagesFile -PathType Leaf)) { throw "Stages file does not exist: $StagesFile" }
        $stages = Get-Content -LiteralPath $StagesFile -Raw -Encoding UTF8 | ConvertFrom-Json -NoEnumerate
        if ($stages -isnot [array]) { throw 'Stages file must contain a JSON array.' }
        $result = Invoke-PipelineApi 'POST' "/api/pipeline-definitions/$Id/revisions" @{ stages = $stages; changeNote = $Note }
    }
    'set-board' {
        Require-Value $Board '-Board'
        if (-not $Inherit) { Require-Value $Id '-Id' }
        $selected = if ($Inherit) { $null } else { $Id }
        $result = Invoke-PipelineApi 'PUT' "/api/boards/$Board/pipeline" @{ pipelineDefinitionId = $selected }
    }
    'set-project' {
        Require-Value $Project '-Project'
        if (-not $Inherit) { Require-Value $Id '-Id' }
        $selected = if ($Inherit) { $null } else { $Id }
        $result = Invoke-PipelineApi 'PUT' "/api/projects/$Project/pipeline" @{ pipelineDefinitionId = $selected }
    }
}

if ($Json) { ConvertTo-Json -InputObject $result -Depth 20 }
else { $result }
