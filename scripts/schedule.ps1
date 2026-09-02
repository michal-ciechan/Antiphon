# Read and write clock-driven prompt schedules from a shell, without composing HTTP by hand.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# Identity comes from the ANTIPHON_* environment, never from arguments. Long prompt text comes
# from -PromptFile (Get-Content -Raw), the same rule card.ps1 uses for -DescriptionFile.
#
# Verbs:
#   schedule.ps1 list    [-Agent <id|slug|name>] [-Json]
#   schedule.ps1 get     <id> [-Json]
#   schedule.ps1 preview [-Agent a] [-Name n] [-Prompt s | -PromptFile p]
#                        [-Repeat Once|Interval|Daily] [-FireAt utc] [-EveryMinutes n]
#                        [-AtLocal HH:mm] [-DaysOfWeek mask] [-TimeZone id]
#   schedule.ps1 new     -Name n -Agent a [-Prompt s | -PromptFile p] ... (prints preview first)
#   schedule.ps1 enable  <id>
#   schedule.ps1 disable <id>
#   schedule.ps1 remove  <id>
#   schedule.ps1 fire    <id>
#
# -Agent defaults to $env:ANTIPHON_AGENT_ID so a standing agent can schedule itself in one line.
# A pool delegate is refused as a target (the server names the candidates).
[CmdletBinding(DefaultParameterSetName = 'Verb')]
param(
    [Parameter(ParameterSetName = 'Verb', Position = 0, Mandatory = $true)]
    [ValidateSet('list', 'get', 'preview', 'new', 'enable', 'disable', 'remove', 'fire')]
    [string]$Verb,

    [Parameter(ParameterSetName = 'Verb', Position = 1)]
    [string]$Id,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$Agent,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$Name,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$Prompt,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$PromptFile,

    [Parameter(ParameterSetName = 'Verb')]
    [ValidateSet('Once', 'Interval', 'Daily')]
    [string]$Repeat = 'Once',

    [Parameter(ParameterSetName = 'Verb')]
    [string]$FireAt,

    [Parameter(ParameterSetName = 'Verb')]
    [int]$EveryMinutes = 0,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$AtLocal,

    [Parameter(ParameterSetName = 'Verb')]
    [int]$DaysOfWeek = 0,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$TimeZone,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$WhenTargetDown,

    [Parameter(ParameterSetName = 'Verb')]
    [string]$By,

    [Parameter(ParameterSetName = 'Verb')]
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
    param([string]$Method, [string]$Path, $Body)
    $uri = "$api$Path"
    try {
        if ($null -ne $Body) {
            $jsonBody = $Body | ConvertTo-Json -Depth 8 -Compress
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($jsonBody)
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $bytes `
                -ContentType 'application/json; charset=utf-8'
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
            if ($parsed.errors) {
                foreach ($prop in $parsed.errors.PSObject.Properties) {
                    foreach ($msg in @($prop.Value)) { $lines += ("  {0}: {1}" -f $prop.Name, $msg) }
                }
            }
            Write-Error ("Antiphon {0} {1} failed: {2}" -f $Method, $Path, ($lines -join [Environment]::NewLine))
        }
        else {
            Write-Error "Antiphon $Method $Path failed: $raw"
        }
        exit 1
    }
}

function Read-PromptText {
    if (-not [string]::IsNullOrWhiteSpace($PromptFile)) {
        if (-not [string]::IsNullOrWhiteSpace($Prompt)) {
            Write-Error "Pass -Prompt or -PromptFile, not both."
            exit 1
        }
        if (-not (Test-Path -LiteralPath $PromptFile)) {
            Write-Error ("PromptFile '{0}' was not found." -f $PromptFile)
            exit 1
        }
        return (Get-Content -LiteralPath $PromptFile -Raw)
    }
    return $Prompt
}

function Resolve-Agent {
    if (-not [string]::IsNullOrWhiteSpace($Agent)) { return $Agent }
    if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_AGENT_ID)) { return $env:ANTIPHON_AGENT_ID }
    return $null
}

function New-CreateBody {
    $promptText = Read-PromptText
    $agentRef = Resolve-Agent
    $body = @{
        name       = $Name
        kind       = 'Prompt'
        repeat     = $Repeat
        agent      = $agentRef
        promptText = $promptText
        createdBy  = $By
    }
    if (-not [string]::IsNullOrWhiteSpace($TimeZone)) { $body.timeZoneId = $TimeZone }
    if (-not [string]::IsNullOrWhiteSpace($WhenTargetDown)) { $body.whenTargetDown = $WhenTargetDown }
    if ($Repeat -eq 'Once') {
        if ([string]::IsNullOrWhiteSpace($FireAt)) {
            Write-Error "Once requires -FireAt (UTC, e.g. 2026-09-04T09:00:00Z)."
            exit 1
        }
        $body.fireAt = $FireAt
    }
    elseif ($Repeat -eq 'Interval') {
        if ($EveryMinutes -lt 1) {
            Write-Error "Interval requires -EveryMinutes (1..10080)."
            exit 1
        }
        $body.everyMinutes = $EveryMinutes
    }
    elseif ($Repeat -eq 'Daily') {
        if ([string]::IsNullOrWhiteSpace($AtLocal)) {
            Write-Error "Daily requires -AtLocal (HH:mm)."
            exit 1
        }
        $body.atLocal = $AtLocal
        if ($DaysOfWeek -ne 0) { $body.daysOfWeek = $DaysOfWeek }
    }
    return $body
}

function Write-Preview {
    param($Preview)
    Write-Output "Preview"
    foreach ($occ in @($Preview.nextOccurrences)) {
        Write-Output ("  next  {0}  ({1})" -f $occ.utc, $occ.local)
    }
    if ($Preview.target.agentName) {
        Write-Output ("  agent {0}  live={1}  alwaysOn={2}  session={3}" -f `
            $Preview.target.agentName, $Preview.target.agentLive, $Preview.target.agentAlwaysOn, $Preview.target.sessionStatus)
    }
    Write-Output ("  effect {0}" -f $Preview.effect)
    Write-Output ("  spend  {0}" -f $Preview.spend)
    foreach ($w in @($Preview.warnings)) {
        Write-Output ("  warn   {0}" -f $w)
    }
}

function Write-Schedule {
    param($Row)
    if ($Json) {
        $Row | ConvertTo-Json -Depth 8
        return
    }
    $next = $Row.nextFireAt
    if ($Row.nextFireAtLocal) { $next = $Row.nextFireAtLocal }
    Write-Output ("{0}  {1}  {2}  next {3}  last {4}" -f `
        $Row.id, $Row.name, $Row.repeatDescription, $next, $Row.lastOutcome)
}

switch ($Verb) {
    'list' {
        $qs = @()
        $agentRef = Resolve-Agent
        if (-not [string]::IsNullOrWhiteSpace($agentRef) -and ($agentRef -match '^[0-9a-fA-F-]{36}$')) {
            $qs += ("agentId={0}" -f $agentRef)
        }
        $path = '/api/schedules'
        if ($qs.Count -gt 0) { $path = $path + '?' + ($qs -join '&') }
        $list = Invoke-Antiphon -Method GET -Path $path
        if ($Json) {
            $list | ConvertTo-Json -Depth 8
        }
        else {
            foreach ($row in @($list.schedules)) { Write-Schedule $row }
        }
    }
    'get' {
        if ([string]::IsNullOrWhiteSpace($Id)) { Write-Error "get requires the schedule id."; exit 1 }
        $row = Invoke-Antiphon -Method GET -Path ("/api/schedules/{0}" -f $Id)
        if ($Json) { $row | ConvertTo-Json -Depth 8 }
        else {
            Write-Schedule $row
            foreach ($f in @($row.fires)) {
                Write-Output ("  fire #{0}  {1}  {2}" -f $f.fireNumber, $f.outcome, $f.detail)
            }
        }
    }
    'preview' {
        $body = New-CreateBody
        $preview = Invoke-Antiphon -Method POST -Path '/api/schedules/preview' -Body $body
        if ($Json) { $preview | ConvertTo-Json -Depth 8 }
        else { Write-Preview $preview }
    }
    'new' {
        if ([string]::IsNullOrWhiteSpace($Name)) { Write-Error "new requires -Name."; exit 1 }
        $body = New-CreateBody
        $preview = Invoke-Antiphon -Method POST -Path '/api/schedules/preview' -Body $body
        if (-not $Json) { Write-Preview $preview }
        $created = Invoke-Antiphon -Method POST -Path '/api/schedules' -Body $body
        if ($Json) { $created | ConvertTo-Json -Depth 8 }
        else {
            Write-Output ("created {0}" -f $created.id)
            Write-Schedule $created
        }
    }
    'enable' {
        if ([string]::IsNullOrWhiteSpace($Id)) { Write-Error "enable requires the schedule id."; exit 1 }
        $current = Invoke-Antiphon -Method GET -Path ("/api/schedules/{0}" -f $Id)
        $row = Invoke-Antiphon -Method PATCH -Path ("/api/schedules/{0}" -f $Id) -Body @{
            concurrencyToken = $current.concurrencyToken
            enabled          = $true
        }
        Write-Schedule $row
    }
    'disable' {
        if ([string]::IsNullOrWhiteSpace($Id)) { Write-Error "disable requires the schedule id."; exit 1 }
        $current = Invoke-Antiphon -Method GET -Path ("/api/schedules/{0}" -f $Id)
        $row = Invoke-Antiphon -Method PATCH -Path ("/api/schedules/{0}" -f $Id) -Body @{
            concurrencyToken = $current.concurrencyToken
            enabled          = $false
        }
        Write-Schedule $row
    }
    'remove' {
        if ([string]::IsNullOrWhiteSpace($Id)) { Write-Error "remove requires the schedule id."; exit 1 }
        Invoke-Antiphon -Method DELETE -Path ("/api/schedules/{0}" -f $Id) | Out-Null
        Write-Output ("removed {0}" -f $Id)
    }
    'fire' {
        if ([string]::IsNullOrWhiteSpace($Id)) { Write-Error "fire requires the schedule id."; exit 1 }
        Invoke-Antiphon -Method POST -Path ("/api/schedules/{0}/fire-now" -f $Id) | Out-Null
        Write-Output ("fired {0}" -f $Id)
    }
}
