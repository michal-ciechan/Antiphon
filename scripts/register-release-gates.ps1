#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0599 D-7: idempotent registration front door for the three release-gate jobs.

    Preview by default: it writes NOTHING, and prints the content digest, tag,
    cron, timezone, args and the live-versus-desired difference for each
    definition. -Apply registers or reconciles them; -EnableSchedule turns on ONE
    named schedule and requires -Apply.

    Registration uses the installed Windmill HTTP API, never a database write.
    The token is read from an operator-placed file named by the profile and is
    never printed, logged or embedded in a script payload. This script creates no
    token and holds no watchdog credential.

    Schedules are always CREATED disabled. Enablement is explicit and is never an
    accidental consequence of a reapply; an already-enabled matching schedule is
    preserved by an idempotent reapply. Apply compares the observed
    revision/hash before updating and refuses unrelated collisions or concurrent
    drift.

    The operator still owns secret placement, interactive login and authorization
    for live notices. Running this script is an S5/S6 operator step; a Code or
    Review delegate does not run it against the live workspace.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.

.PARAMETER Profile
    Untracked JSON profile: windmillBaseUrl, workspace (must be 'mc' for this
    deployment), tokenFile, repositoryPath, projectId. No inline token.

.PARAMETER Apply
    Perform the registration writes. Without it this is a read-only preview.

.PARAMETER EnableSchedule
    nightly | readiness | rc. Requires -Apply. Enables exactly that schedule.
#>
param(
    [Parameter(Mandatory = $true)][string]$Profile,
    [switch]$Apply,
    [ValidateSet('', 'nightly', 'readiness', 'rc')][string]$EnableSchedule = '',
    [string]$DefinitionRoot = '',
    [string]$SeamsPath = '',
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'release-gate.ps1')

$script:RegisterDefinitions = @(
    [pscustomobject]@{ Key = 'nightly'; Script = 'antiphon-nightly-tests.json'; Schedule = 'antiphon-nightly-tests.schedule.json' },
    [pscustomobject]@{ Key = 'readiness'; Script = 'antiphon-nightly-readiness.json'; Schedule = 'antiphon-nightly-readiness.schedule.json' },
    [pscustomobject]@{ Key = 'rc'; Script = 'antiphon-release-candidates.json'; Schedule = 'antiphon-release-candidates.schedule.json' }
)

function Write-RegisterLine { param([string]$Message) Write-Host ('[register-release-gates] {0}' -f $Message) }

function Read-ReleaseGateRegistrationProfile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw ('profile missing: {0}' -f $Path) }
    $obj = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($field in @('windmillBaseUrl', 'workspace', 'tokenFile', 'repositoryPath', 'projectId')) {
        if ([string]::IsNullOrWhiteSpace([string]$obj.$field)) { throw ('profile field {0} is required' -f $field) }
    }
    if ([string]$obj.workspace -ne 'mc') { throw ('workspace must be mc for this deployment, got {0}' -f $obj.workspace) }
    foreach ($forbidden in @('token', 'password', 'secret', 'watchdogToken')) {
        if ($obj.PSObject.Properties.Name -contains $forbidden) {
            throw ('profile must not carry an inline {0}; use tokenFile' -f $forbidden)
        }
    }
    return $obj
}

function Get-ReleaseGateDefinitionDigest {
    param($Definition)
    return (Get-NightlySha256Text -Text (ConvertTo-NightlyCanonicalJson -Object $Definition))
}

function Invoke-AntiphonRegisterReleaseGates {
    param(
        [string]$Profile,
        [switch]$Apply,
        [string]$EnableSchedule,
        [string]$DefinitionRoot,
        [string]$SeamsPath
    )
    Import-ReleaseGateSeams -SeamsPath $SeamsPath
    $result = [ordered]@{
        ExitCode = 1
        Mode = $(if ($Apply) { 'apply' } else { 'preview' })
        Refusal = ''
        Writes = 0
        Rows = @()
        Enabled = ''
    }
    if (-not [string]::IsNullOrWhiteSpace($EnableSchedule) -and -not $Apply) {
        Write-RegisterLine 'REFUSED: -EnableSchedule requires -Apply.'
        $result.Refusal = 'enable-requires-apply'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if ([string]::IsNullOrWhiteSpace($DefinitionRoot)) { $DefinitionRoot = Join-Path $PSScriptRoot 'windmill' }

    try { $profileObj = Read-ReleaseGateRegistrationProfile -Path $Profile } catch {
        Write-RegisterLine ('REFUSED: {0}' -f $_.Exception.Message)
        $result.Refusal = 'profile-invalid'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    $workspace = [string]$profileObj.workspace
    $context = @{ baseUrl = [string]$profileObj.windmillBaseUrl; workspace = $workspace; token = '' }
    if ($Apply) {
        try { $context.token = Get-ReleaseGateTokenFromFile -TokenFile ([string]$profileObj.tokenFile) } catch {
            Write-RegisterLine ('REFUSED: {0}' -f $_.Exception.Message)
            $result.Refusal = 'token-unavailable'; $result.ExitCode = 3; return [pscustomobject]$result
        }
    }

    $rows = @()
    $writes = 0
    foreach ($def in $script:RegisterDefinitions) {
        $scriptPath = Join-Path $DefinitionRoot $def.Script
        $schedulePath = Join-Path $DefinitionRoot $def.Schedule
        foreach ($needed in @($scriptPath, $schedulePath)) {
            if (-not (Test-Path -LiteralPath $needed)) {
                Write-RegisterLine ('REFUSED: missing definition {0}.' -f $needed)
                $result.Refusal = 'definition-missing'; $result.ExitCode = 3; return [pscustomobject]$result
            }
        }
        $scriptDef = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $scheduleDef = Get-Content -LiteralPath $schedulePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]$scheduleDef.script_path -ne [string]$scriptDef.path) {
            Write-RegisterLine ('REFUSED: schedule {0} does not name script {1}.' -f $scheduleDef.script_path, $scriptDef.path)
            $result.Refusal = 'definition-path-mismatch'; $result.ExitCode = 3; return [pscustomobject]$result
        }
        if ([bool]$scheduleDef.enabled -and $def.Key -eq 'rc') {
            # D-7: a future schedule payload must start disabled.
            Write-RegisterLine 'REFUSED: rc schedule payload must be created disabled.'
            $result.Refusal = 'rc-schedule-preenabled'; $result.ExitCode = 3; return [pscustomobject]$result
        }

        $desiredScriptDigest = Get-ReleaseGateDefinitionDigest -Definition $scriptDef
        $live = Invoke-ReleaseGateWindmill -Method 'GET' -Path ('/api/w/{0}/scripts/get/p/{1}' -f $workspace, $scriptDef.path) -Context $context
        $liveHash = ''
        $liveExists = $false
        if ($live.Ok -and $live.Body) { $liveExists = $true; $liveHash = [string]$live.Body.hash }
        $liveSchedule = Invoke-ReleaseGateWindmill -Method 'GET' -Path ('/api/w/{0}/schedules/get/{1}' -f $workspace, $scheduleDef.path) -Context $context
        $liveEnabled = $null
        $liveCron = ''
        $liveScheduleExists = $false
        if ($liveSchedule.Ok -and $liveSchedule.Body) {
            $liveScheduleExists = $true
            $liveEnabled = [bool]$liveSchedule.Body.enabled
            $liveCron = [string]$liveSchedule.Body.schedule
        }

        $row = [ordered]@{
            key = $def.Key
            scriptPath = [string]$scriptDef.path
            desiredDigest = $desiredScriptDigest
            liveHash = $liveHash
            scriptExists = $liveExists
            tag = [string]$scriptDef.tag
            cron = [string]$scheduleDef.schedule
            timezone = [string]$scheduleDef.timezone
            args = $scheduleDef.args
            desiredEnabled = [bool]$scheduleDef.enabled
            liveEnabled = $liveEnabled
            liveCron = $liveCron
            scheduleExists = $liveScheduleExists
            action = 'none'
        }

        if (-not $liveExists) { $row.action = 'create-script' }
        elseif (-not [string]::Equals($liveHash, $desiredScriptDigest, [StringComparison]::OrdinalIgnoreCase)) { $row.action = 'update-script' }

        if (-not $Apply) {
            $row.action = ('preview:' + $row.action)
            $rows += $row
            continue
        }

        if ($row.action -eq 'create-script' -or $row.action -eq 'update-script') {
            $payload = [ordered]@{
                path = [string]$scriptDef.path
                summary = [string]$scriptDef.summary
                description = [string]$scriptDef.description
                content = [string]$scriptDef.content
                language = [string]$scriptDef.language
                tag = [string]$scriptDef.tag
                schema = $scriptDef.schema
            }
            if ($liveExists) { $payload['parent_hash'] = $liveHash }
            $write = Invoke-ReleaseGateWindmill -Method 'POST' -Path ('/api/w/{0}/scripts/create' -f $workspace) -Body $payload -Context $context
            if (-not $write.Ok) {
                Write-RegisterLine ('REFUSED: script write failed for {0} (status {1}).' -f $scriptDef.path, $write.Status)
                $result.Refusal = 'script-write-failed'; $result.ExitCode = 1; $result.Rows = $rows; return [pscustomobject]$result
            }
            $writes++
            # Readback is the evidence, not the accepted request.
            $back = Invoke-ReleaseGateWindmill -Method 'GET' -Path ('/api/w/{0}/scripts/get/p/{1}' -f $workspace, $scriptDef.path) -Context $context
            if (-not $back.Ok -or $null -eq $back.Body) {
                Write-RegisterLine ('REFUSED: script readback failed for {0}.' -f $scriptDef.path)
                $result.Refusal = 'script-readback-failed'; $result.ExitCode = 1; $result.Rows = $rows; return [pscustomobject]$result
            }
            $row.readbackHash = [string]$back.Body.hash
            $row.readbackContentDigest = (Get-NightlySha256Text -Text ([string]$back.Body.content))
            if (-not [string]::Equals([string]$back.Body.content, [string]$scriptDef.content, [StringComparison]::Ordinal)) {
                Write-RegisterLine ('REFUSED: stored content differs from the desired definition for {0}.' -f $scriptDef.path)
                $result.Refusal = 'script-content-drift'; $result.ExitCode = 1; $result.Rows = $rows; return [pscustomobject]$result
            }
        }

        if (-not $liveScheduleExists) {
            $payload = [ordered]@{
                path = [string]$scheduleDef.path
                schedule = [string]$scheduleDef.schedule
                timezone = [string]$scheduleDef.timezone
                script_path = [string]$scheduleDef.script_path
                is_flow = [bool]$scheduleDef.is_flow
                args = $scheduleDef.args
                enabled = $false
                tag = [string]$scheduleDef.tag
            }
            $write = Invoke-ReleaseGateWindmill -Method 'POST' -Path ('/api/w/{0}/schedules/create' -f $workspace) -Body $payload -Context $context
            if (-not $write.Ok) {
                Write-RegisterLine ('REFUSED: schedule write failed for {0} (status {1}).' -f $scheduleDef.path, $write.Status)
                $result.Refusal = 'schedule-write-failed'; $result.ExitCode = 1; $result.Rows = $rows; return [pscustomobject]$result
            }
            $writes++
            $row.action = ($row.action + '+create-schedule')
            $liveSchedule = Invoke-ReleaseGateWindmill -Method 'GET' -Path ('/api/w/{0}/schedules/get/{1}' -f $workspace, $scheduleDef.path) -Context $context
            if (-not $liveSchedule.Ok -or $null -eq $liveSchedule.Body) {
                Write-RegisterLine ('REFUSED: schedule readback failed for {0}.' -f $scheduleDef.path)
                $result.Refusal = 'schedule-readback-failed'; $result.ExitCode = 1; $result.Rows = $rows; return [pscustomobject]$result
            }
            if ([bool]$liveSchedule.Body.enabled) {
                Write-RegisterLine ('REFUSED: schedule {0} was created enabled.' -f $scheduleDef.path)
                $result.Refusal = 'schedule-created-enabled'; $result.ExitCode = 1; $result.Rows = $rows; return [pscustomobject]$result
            }
            $row.liveEnabled = $false
        } else {
            # Idempotent reapply: an existing matching schedule keeps its enablement.
            if (-not [string]::Equals($liveCron, [string]$scheduleDef.schedule, [StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$liveSchedule.Body.timezone, [string]$scheduleDef.timezone, [StringComparison]::Ordinal)) {
                Write-RegisterLine ('REFUSED: live schedule {0} differs (cron/timezone); resolve the drift explicitly.' -f $scheduleDef.path)
                $result.Refusal = 'schedule-drift'; $result.ExitCode = 3; $result.Rows = $rows; return [pscustomobject]$result
            }
            $row.action = ($row.action + '+schedule-unchanged')
        }
        $rows += $row
    }

    $result.Rows = $rows
    $result.Writes = $writes

    if ($Apply -and -not [string]::IsNullOrWhiteSpace($EnableSchedule)) {
        $target = @($script:RegisterDefinitions | Where-Object { $_.Key -eq $EnableSchedule })
        if ($target.Count -ne 1) {
            Write-RegisterLine ('REFUSED: unknown schedule selector {0}.' -f $EnableSchedule)
            $result.Refusal = 'unknown-schedule'; $result.ExitCode = 3; return [pscustomobject]$result
        }
        $scheduleDef = Get-Content -LiteralPath (Join-Path $DefinitionRoot $target[0].Schedule) -Raw -Encoding UTF8 | ConvertFrom-Json
        $enable = Invoke-ReleaseGateWindmill -Method 'POST' `
            -Path ('/api/w/{0}/schedules/setenabled/{1}' -f $workspace, $scheduleDef.path) `
            -Body ([ordered]@{ enabled = $true }) -Context $context
        if (-not $enable.Ok) {
            Write-RegisterLine ('REFUSED: enable failed for {0} (status {1}).' -f $scheduleDef.path, $enable.Status)
            $result.Refusal = 'enable-failed'; $result.ExitCode = 1; return [pscustomobject]$result
        }
        $back = Invoke-ReleaseGateWindmill -Method 'GET' -Path ('/api/w/{0}/schedules/get/{1}' -f $workspace, $scheduleDef.path) -Context $context
        if (-not $back.Ok -or -not [bool]$back.Body.enabled) {
            Write-RegisterLine ('REFUSED: enable readback did not confirm {0}.' -f $scheduleDef.path)
            $result.Refusal = 'enable-readback-failed'; $result.ExitCode = 1; return [pscustomobject]$result
        }
        $result.Enabled = $EnableSchedule
        $result.Writes = $result.Writes + 1
    }

    foreach ($row in $rows) {
        Write-RegisterLine ('{0}: {1} cron={2} tz={3} tag={4} desiredDigest={5} liveHash={6} liveEnabled={7}' -f `
            $row.key, $row.action, $row.cron, $row.timezone, $row.tag,
            ([string]$row.desiredDigest).Substring(0, 12), $row.liveHash, $row.liveEnabled)
    }
    $result.ExitCode = 0
    return [pscustomobject]$result
}

$invokeResult = Invoke-AntiphonRegisterReleaseGates -Profile $Profile -Apply:$Apply `
    -EnableSchedule $EnableSchedule -DefinitionRoot $DefinitionRoot -SeamsPath $SeamsPath
if ($PassThru) { return $invokeResult }
exit ([int]$invokeResult.ExitCode)
