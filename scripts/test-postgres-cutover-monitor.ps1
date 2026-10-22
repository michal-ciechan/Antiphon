#requires -Version 5.1
# CARD-1408 (CARD-1401 C3) harness for scripts/postgres-cutover-monitor.ps1. ASCII-only.
# No Docker, no database, no live HTTP: every measurement seam is a scriptblock fake.
# Usage: pwsh -NoProfile -File scripts/test-postgres-cutover-monitor.ps1 [-Case <name>]
param(
    [string]$Case = ''
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$monitor = Join-Path $here 'postgres-cutover-monitor.ps1'
$script:Passed = 0
$script:Failed = 0

function Assert-C1408 {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) {
        $script:Passed++
        Write-Host ('PASS ' + $Name)
    } else {
        $script:Failed++
        Write-Host ('FAIL ' + $Name + $(if ($Detail) { ' :: ' + $Detail } else { '' }))
    }
}

# Every scratch root is recorded so the sweep after the cases removes what a case that threw left behind
# (CARD-1423 A5: five /tmp/c1408-* folders from a stalled Review run remained on server2). A run that is
# killed from outside still leaves its folders; the sweep is for the harness's own exits.
$script:C1408ScratchRoots = New-Object System.Collections.ArrayList
function New-C1408Scratch {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('c1408-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    [void]$script:C1408ScratchRoots.Add($root)
    return $root
}

# Land detail by task id; the fake detail endpoint serves it. The list item never carries it.
$script:C1408LandById = @{}

# The real GET /api/agent-tasks item shape: AgentTaskSummaryDto (server/Application/Dtos/AgentTaskDtos.cs),
# camelCase, nulls written, enums as names. It has no landing or landRequest field; those are only on the
# detail DTO (AgentTaskDetailDto.Landing / LandRequest). -Land sets the detail the fake serves:
# Refused (landing.phase and publication Refused), NeedsResolution (landRequest.state), Landed (Complete).
#   Exception: a land that threw (AgentTaskLandService.PersistFailureAsync): landing mid-flight and Unconfirmed,
#     landRequest Completed with terminalFailureCode, a LandRefused event; ClearPending left landAttempt 1.
#   PreProtocol: RefuseAsync('landing_protocol_unavailable') before the attempt counter moved: no landing, request
#     Completed without a failure code, a LandRefused event, and a list item with no land hint at all.
#   OldRefusal: a LandRefused event before the window, then a later land that landed.
function New-C1408Task {
    param([string]$Status = 'Succeeded', [ValidateSet('', 'Refused', 'NeedsResolution', 'Landed', 'Exception', 'PreProtocol', 'OldRefusal')][string]$Land = '',
        [string]$Workspace = 'Worktree')
    $id = [guid]::NewGuid().ToString()
    $item = [ordered]@{
        id = $id; rootTaskId = $id; parentTaskId = $null; depth = 0; title = 'c1408 fake'; kind = 'Worker'; role = 'Code'
        agentKind = 'ClaudeCode'; modelLevel = 'High'; escalatedFrom = $null; status = $Status; workspace = $Workspace
        workingDirectory = '/work/repos/antiphon'; repoPath = '/work/repos/antiphon'; worktreePath = $null; worktreeBranch = $null
        scope = $null; observedScope = $null; agentId = $null; agentName = $null; agentSessionId = $null; attempt = 1
        createdAt = '2026-10-21T09:40:00Z'; dispatchedAt = '2026-10-21T09:40:01Z'; completedAt = '2026-10-21T09:50:00Z'; readAt = $null
        deliverablePath = $null; deliverableRef = $null; recoveredAt = $null; tokensIn = 0; cacheReadTokens = 0; cacheCreationTokens = 0
        tokensOut = 0; costUsd = 0; costPricingVersion = 1; subtreeCostUsd = 0; childCount = 0; expectedDurationMinutes = 30
        nextCheckAt = $null; checkCount = 0; landRequestedAt = $null; landStartedAt = $null; landAttempt = 0; cardId = $null
        cardIdentifier = $null; reportEvidence = 'Marked'; complexity = $null; repliedAt = $null; projectId = 'p1'; projectName = 'Antiphon'
        boardId = $null; boardName = $null; scopeSource = 'Task'; requiredPlatform = 'Any'; runnerId = $null; requirementSource = 'Default'
        observedPlatform = $null; runnerDefaultsRevision = $null; runnerSelectionSource = $null; interruptWritten = $null
        refinementDelivered = $null; effectiveModelId = $null
    }
    if ($Land) {
        $item.landAttempt = 1
        $landing = $null
        $events = @()
        $request = [ordered]@{ id = [guid]::NewGuid().ToString(); state = 'Completed'; requestedAt = '2026-10-21T09:51:00Z'; startedAt = '2026-10-21T09:51:01Z'; attempt = 1; terminalFailureCode = $null }
        switch ($Land) {
            'Refused' { $landing = [ordered]@{ operationId = [guid]::NewGuid().ToString(); phase = 'Refused'; mode = 'Fresh'; publication = 'Refused'; cleanup = 'NotStarted'; sourceSha = 'aaaa'; reason = 'conflict' } }
            'NeedsResolution' { $request.state = 'NeedsResolution'; $item.landRequestedAt = '2026-10-21T09:51:00Z' }
            'Landed' { $landing = [ordered]@{ operationId = [guid]::NewGuid().ToString(); phase = 'Complete'; mode = 'Fresh'; publication = 'Landed'; cleanup = 'Complete'; sourceSha = 'aaaa'; reason = $null } }
            'Exception' {
                $op = [guid]::NewGuid().ToString()
                $landing = New-C1408Landing -OperationId $op -Phase 'Prepared' -Publication 'Unconfirmed' -Cleanup 'NotStarted'
                $request = New-C1408LandRequest -Attempt 1 -TerminalFailureCode 'landing_timeout' -FailureExceptionType 'Npgsql.NpgsqlException' -LandingOperationId $op
                $events = @(
                    (New-C1408Event -Type 'LandRequested' -Detail 'land requested' -At '2026-10-21T09:50:30Z'),
                    (New-C1408Event -Type 'Warning' -Detail 'Landing not confirmed; no cleanup authorized by this result. landing_timeout' -At '2026-10-21T09:52:00Z'),
                    (New-C1408Event -Type 'LandRefused' -Detail 'land failed: landing_timeout' -At '2026-10-21T09:52:00Z' -LandingOperationId $op -Publication 'Unconfirmed' -Cleanup 'NotStarted'))
            }
            'PreProtocol' {
                $item.landAttempt = 0
                $request = New-C1408LandRequest -Attempt 0
                $events = @(
                    (New-C1408Event -Type 'LandRequested' -Detail 'land requested' -At '2026-10-21T09:50:30Z'),
                    (New-C1408Event -Type 'Warning' -Detail 'Landing not confirmed; no cleanup authorized by this result. landing_protocol_unavailable' -At '2026-10-21T09:50:31Z'),
                    (New-C1408Event -Type 'LandRefused' -Detail 'land refused: landing_protocol_unavailable' -At '2026-10-21T09:50:31Z'))
            }
            'OldRefusal' {
                $op = [guid]::NewGuid().ToString()
                $landing = New-C1408Landing -OperationId $op -Phase 'Complete' -Publication 'Landed' -Cleanup 'Complete'
                $request = New-C1408LandRequest -Attempt 1 -LandingOperationId $op
                $events = @(
                    (New-C1408Event -Type 'LandRefused' -Detail 'land refused: conflict' -At '2026-10-21T09:00:00Z'),
                    (New-C1408Event -Type 'Landed' -Detail 'landed' -At '2026-10-21T09:51:00Z' -LandingOperationId $op -Publication 'Landed' -Cleanup 'Complete'))
            }
        }
        $script:C1408LandById[$id] = @{ landing = $landing; landRequest = $request; events = $events }
    }
    return $item
}

# AgentTaskEventDto (server/Application/Dtos/AgentTaskDtos.cs), camelCase, enum names.
function New-C1408Event {
    param([string]$Type, [string]$Detail, [string]$At, [string]$LandingOperationId = $null, [string]$Publication = $null, [string]$Cleanup = $null)
    # [string] parameters hold an empty string, never null; the server writes null.
    $e = [ordered]@{ type = $Type; modelLevel = $null; detail = $Detail; at = $At; landingOperationId = $null; landingPublication = $null; landingCleanup = $null; landingMode = $null }
    if ($LandingOperationId) { $e.landingOperationId = $LandingOperationId; $e.landingMode = 'Fresh' }
    if ($Publication) { $e.landingPublication = $Publication }
    if ($Cleanup) { $e.landingCleanup = $Cleanup }
    return $e
}

# LandingEvidenceDto (server/Application/Dtos/LandingEvidenceDto.cs).
function New-C1408Landing {
    param([string]$OperationId, [string]$Phase, [string]$Publication, [string]$Cleanup)
    return [ordered]@{
        operationId = $OperationId; phase = $Phase; mode = 'Fresh'; publication = $Publication; cleanup = $Cleanup; sourceSha = 'aaaa'; verifiedSha = $null
        remoteSha = $null; remoteConfirmedAt = $null; destinationRef = 'refs/heads/master'; reason = $null; reviewedSha = $null; preparationInputSha = $null
        sourceRemoteSha = $null; approvalLandRequestId = $null; reviewEvidenceId = $null; schemaVersion = 1; cleanupAttemptId = $null; cleanupCapture = $null
        recoveryMode = 'None'; recoveryOwnerStatus = $null; recoverySourceTaskId = $null; recoverySourceFullRef = $null; recoveryOwnerRemoteBeforeSha = $null
        recoveryOwnerRemoteAfterSha = $null; recoveryRelationship = $null; recoveryPatchesContained = $null; recoveryUncontainedPatches = $null
    }
}

# LandRequestStatusDto (server/Application/Dtos/LandRequestStatusDto.cs) after CompleteRequest: state Completed.
function New-C1408LandRequest {
    param([int]$Attempt, [string]$TerminalFailureCode = $null, [string]$FailureExceptionType = $null, [string]$LandingOperationId = $null)
    $r = [ordered]@{
        id = [guid]::NewGuid().ToString(); state = 'Completed'; requestedAt = '2026-10-21T09:50:30Z'; startedAt = $(if ($Attempt -gt 0) { '2026-10-21T09:50:31Z' } else { $null })
        lastEvaluatedAt = '2026-10-21T09:52:00Z'; lastProgressAt = '2026-10-21T09:52:00Z'; ageSeconds = 90.0; noProgressSeconds = 0.0; attempt = $Attempt
        holdReasonCode = $null; holdDetail = $null; holdingTaskId = $null; holdingTaskStatus = $null; heldSince = $null; holdEpisode = 0
        landingOperationId = $LandingOperationId; terminalEventId = [guid]::NewGuid().ToString(); reconciliationError = $null; notifications = @()
        schemaVersion = 1; expectedSourceSha = $null; reviewEvidenceId = $null; approvalKind = 'ExplicitCaller'; sourceResolutionState = 'None'
        localBeforeSha = $null; remoteSourceSha = $null; candidateSourceSha = $null; resolvedSourceSha = $null; sourceRelationship = 'Unknown'; sourceRefusalReason = $null
        terminalFailureCode = $TerminalFailureCode; failureDiagnosticId = $null; failureExceptionType = $FailureExceptionType
        sourceDiagnosticCommand = $null; sourceDiagnosticExitCode = $null; sourceDiagnosticCode = $null; sourceDiagnosticExceptionType = $null
        recoveryMode = 'None'; recoveryOwnerStatus = $null; recoverySourceTaskId = $null; recoverySourceFullRef = $null; recoveryOwnerRemoteBeforeSha = $null
        recoveryOwnerRemoteAfterSha = $null; recoveryRelationship = $null; recoveryPatchesContained = $null; recoveryUncontainedPatches = $null; supersedesRequestId = $null
    }
    # [string] parameters hold an empty string, never null; the server writes null.
    foreach ($k in @('terminalFailureCode', 'failureExceptionType', 'landingOperationId')) { if (-not $r[$k]) { $r[$k] = $null } }
    if ($TerminalFailureCode) { $r.failureDiagnosticId = [guid]::NewGuid().ToString() }
    return $r
}

# The server's list refusal (AgentTaskScope.SupportedListQueryKeys, AgentTaskEndpoints.RejectUnknownListQueryKeys).
$script:C1408ListKeys = @('rootId', 'since', 'status', 'includeChecks', 'landPending', 'projectId', 'boardId', 'unscoped')

# Fake GET seam. Routes by URL; pipeline samples report their own ElapsedMs. The list refuses unknown
# query keys with 400 like the server; the detail route serves AgentTaskDetailDto's summary/landing/landRequest.
function New-C1408FakeHttp {
    param([double[]]$PipelineMs = @(100), [object[]]$Items = @(), [string]$VersionSha = 'aaaa', [int]$HealthCode = 200, [hashtable]$Counter,
        [int]$ListStatus = 200, [string]$ListBody = '', [int]$DetailStatus = 200, [string]$DiagBody = '')
    if (-not $Counter) { $Counter = @{ calls = 0 } }
    if (-not $Counter.ContainsKey('details')) { $Counter.details = 0 }
    if (-not $Counter.ContainsKey('detailIds')) { $Counter.detailIds = New-Object System.Collections.ArrayList }
    if (-not $Counter.ContainsKey('listUrls')) { $Counter.listUrls = New-Object System.Collections.ArrayList }
    if (-not $DiagBody) { $DiagBody = '{"pool":{"NoResetOnClose":true,"MaxPoolSize":100,"connectionString":"Host=x;Password=hunter2"},"password":"hunter2"}' }
    $state = @{ i = 0; Pipeline = $PipelineMs; Items = $Items; Sha = $VersionSha; Health = $HealthCode; Counter = $Counter; ListStatus = $ListStatus; ListBody = $ListBody; DetailStatus = $DetailStatus; Diag = $DiagBody; Keys = $script:C1408ListKeys; Lands = $script:C1408LandById }
    return {
        param([string]$Url)
        $s = $state
        $s.Counter.calls++
        if ($Url -like '*/api/agent-tasks/pipeline*') {
            $ms = [double]$s.Pipeline[$s.i % $s.Pipeline.Count]
            $s.i++
            return @{ StatusCode = 200; Body = '{"ok":true}'; ElapsedMs = $ms }
        }
        if ($Url -match '/api/agent-tasks(\?(?<q>[^#]*))?$') {
            [void]$s.Counter.listUrls.Add($Url)
            foreach ($pair in ([string]$Matches['q']).Split('&')) {
                if (-not $pair) { continue }
                $key = [uri]::UnescapeDataString($pair.Split('=')[0])
                if (@($s.Keys | Where-Object { $_ -ieq $key }).Count -eq 0) {
                    return @{ StatusCode = 400; Body = ('{"title":"unknown query parameter","detail":"' + $key + '"}') }
                }
            }
            if ($s.ListStatus -ne 200) { return @{ StatusCode = $s.ListStatus; Body = '{"title":"failed"}' } }
            if ($s.ListBody) { return @{ StatusCode = 200; Body = $s.ListBody } }
            $body = [ordered]@{ scope = [ordered]@{ projectId = 'p1'; projectName = 'Antiphon'; boardId = $null; boardName = $null; unscoped = 'exclude' }; items = @($s.Items); excluded = [ordered]@{ total = 0; unscoped = 0; byProject = @() } } | ConvertTo-Json -Depth 8
            return @{ StatusCode = 200; Body = $body }
        }
        if ($Url -match '/api/agent-tasks/(?<id>[0-9a-fA-F-]{36})$') {
            $s.Counter.details++
            if ($s.DetailStatus -ne 200) { return @{ StatusCode = $s.DetailStatus; Body = '{"title":"failed"}' } }
            $id = $Matches['id']
            [void]$s.Counter.detailIds.Add($id)
            $summary = @($s.Items | Where-Object { $_.id -eq $id }) | Select-Object -First 1
            if ($null -eq $summary) { return @{ StatusCode = 404; Body = '{"title":"not found"}' } }
            $land = $s.Lands[$id]
            $detail = [ordered]@{ summary = $summary; goal = 'g'; result = $null; failureReason = $null; events = @(); landing = $null; landRequest = $null }
            if ($land) { $detail.landing = $land.landing; $detail.landRequest = $land.landRequest; if ($land.events) { $detail.events = @($land.events) } }
            return @{ StatusCode = 200; Body = ($detail | ConvertTo-Json -Depth 8) }
        }
        if ($Url -like '*/api/diagnostics/session-state*') {
            return @{ StatusCode = 200; Body = $s.Diag }
        }
        if ($Url -like '*/health*') { return @{ StatusCode = $s.Health; Body = 'Healthy' } }
        if ($Url -like '*/api/version*') { return @{ StatusCode = 200; Body = ('{"sha":"' + $s.Sha + '","capabilities":["land-v2"]}') } }
        throw ('unexpected url ' + $Url)
    }.GetNewClosure()
}

function New-C1408FakeTcp {
    param([double[]]$ConnectMs = @(6.7), [hashtable]$Counter)
    if (-not $Counter) { $Counter = @{ calls = 0 } }
    $state = @{ i = 0; Ms = $ConnectMs; Counter = $Counter }
    return {
        param([string]$HostName, [int]$Port)
        $s = $state
        $s.Counter.calls++
        $v = [double]$s.Ms[$s.i % $s.Ms.Count]
        $s.i++
        return $v
    }.GetNewClosure()
}

$script:FixedNow = { [datetime]::new(2026, 10, 21, 10, 0, 0, [System.DateTimeKind]::Utc) }

function Invoke-C1408Baseline {
    param([string]$Root, [double[]]$PipelineMs, [object[]]$Items = @(), [string]$Sha = 'aaaa', [string]$Name = 'baseline')
    $bp = Join-Path $Root ($Name + '.json')
    $rp = Join-Path $Root ($Name + '-result.json')
    $http = New-C1408FakeHttp -PipelineMs $PipelineMs -Items $Items -VersionSha $Sha
    $r = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $bp -ResultPath $rp -SampleCount $PipelineMs.Count -HttpGet $http -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    return [pscustomobject]@{ Result = $r; BaselinePath = $bp }
}

function Invoke-C1408Watch {
    param([string]$Root, [string]$BaselinePath, [double[]]$PipelineMs, [object[]]$Items = @(), [string]$Sha = 'aaaa', [int]$HealthCode = 200, [string]$Name = 'watch',
        [hashtable]$Counter, [int]$ListStatus = 200, [string]$ListBody = '', [int]$DetailStatus = 200, [int]$MaxLandDetails = 0)
    $rp = Join-Path $Root ($Name + '-result.json')
    $http = New-C1408FakeHttp -PipelineMs $PipelineMs -Items $Items -VersionSha $Sha -HealthCode $HealthCode -Counter $Counter -ListStatus $ListStatus -ListBody $ListBody -DetailStatus $DetailStatus
    $extra = @{}
    if ($MaxLandDetails -gt 0) { $extra.MaxLandDetailRequests = $MaxLandDetails }
    return Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $BaselinePath -ResultPath $rp -SampleCount $PipelineMs.Count -HttpGet $http -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow @extra
}

function Get-C1408Twenty {
    param([double]$Median)
    # Twenty samples whose median is exactly $Median: ten at Median-1, ten at Median+1.
    $out = @()
    for ($i = 0; $i -lt 10; $i++) { $out += ($Median - 1) }
    for ($i = 0; $i -lt 10; $i++) { $out += ($Median + 1) }
    return $out
}

function Test-C1408_RollbackAboveFloorAndRatio {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 1000) -Items @((New-C1408Task -Status 'Failed'))
    Assert-C1408 -Cond ($b.Result.ExitCode -eq 0) -Name 'baseline with clean samples exits 0' -Detail ('exit=' + $b.Result.ExitCode + ' ' + ($b.Result.Lines -join ' | '))
    Assert-C1408 -Cond (Test-Path -LiteralPath $b.BaselinePath) -Name 'baseline json written'
    $base = Get-Content -LiteralPath $b.BaselinePath -Raw | ConvertFrom-Json
    Assert-C1408 -Cond ([double]$base.pipeline.medianMs -eq 1000 -and [int]$base.failures.total -eq 1) -Name 'baseline records median 1000 and failures 1' -Detail (($base | ConvertTo-Json -Depth 5 -Compress))
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 3500) -Items @((New-C1408Task -Status 'Failed'))
    Assert-C1408 -Cond ($w.ExitCode -eq 2 -and $w.Verdict -eq 'ROLLBACK') -Name 'median 3500 vs baseline 1000 exits 2 ROLLBACK' -Detail ('exit=' + $w.ExitCode + ' verdict=' + $w.Verdict)
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -eq 'ROLLBACK' }).Count -eq 1) -Name 'ROLLBACK is printed on its own line'
    Assert-C1408 -Cond ((Test-Path -LiteralPath $w.ResultPath) -and ((Get-Content -LiteralPath $w.ResultPath -Raw | ConvertFrom-Json).verdict -eq 'ROLLBACK')) -Name 'watch result json records the verdict'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_FloorHoldsUnderOneSecond {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 50)
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 600)
    Assert-C1408 -Cond ($w.ExitCode -eq 0 -and $w.Verdict -eq 'OK') -Name 'median 600 vs baseline 50 (12x, under 1 s) exits 0' -Detail ('exit=' + $w.ExitCode + ' verdict=' + $w.Verdict)
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -like 'NOTE*' }).Count -ge 1) -Name 'ratio-only excess prints a NOTE'
    $b2 = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 1100) -Name 'baseline2'
    $w2 = Invoke-C1408Watch -Root $root -BaselinePath $b2.BaselinePath -PipelineMs (Get-C1408Twenty -Median 2000) -Name 'watch2'
    Assert-C1408 -Cond ($w2.ExitCode -eq 0) -Name 'median 2000 vs baseline 1100 (above floor, under 3x) exits 0' -Detail ('exit=' + $w2.ExitCode)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_FailureCountRollback {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100) -Items @((New-C1408Task -Status 'Failed'))
    $four = @((New-C1408Task -Status 'Failed'), (New-C1408Task -Status 'Failed'), (New-C1408Task -Status 'Succeeded' -Land 'Refused'), (New-C1408Task -Status 'Blocked' -Land 'NeedsResolution'), (New-C1408Task -Status 'Succeeded'))
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items $four
    Assert-C1408 -Cond ($w.ExitCode -eq 2 -and $w.Verdict -eq 'ROLLBACK') -Name 'failures 4 vs baseline 1 exits 2 ROLLBACK' -Detail ('exit=' + $w.ExitCode + ' ' + ($w.Lines -join ' | '))
    $three = @((New-C1408Task -Status 'Failed'), (New-C1408Task -Status 'Failed'), (New-C1408Task -Status 'Succeeded' -Land 'Refused'))
    $w2 = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items $three -Name 'watch2'
    Assert-C1408 -Cond ($w2.ExitCode -eq 1 -and $w2.Verdict -eq 'WARN') -Name 'failures 3 vs baseline 1 (exactly +2) is WARN, not rollback' -Detail ('exit=' + $w2.ExitCode + ' verdict=' + $w2.Verdict)
    $f = (Get-Content -LiteralPath $w.ResultPath -Raw | ConvertFrom-Json).failures
    Assert-C1408 -Cond ([int]$f.total -eq 4 -and [int]$f.failedTasks -eq 2 -and [int]$f.failedLands -eq 2) -Name 'failure count sums failed tasks and failed lands' -Detail ($f | ConvertTo-Json -Compress)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_PreWindowAbort {
    $root = New-C1408Scratch
    $slow = Invoke-PostgresCutoverMonitor -Mode PreWindow -Execute -Server2Host '100.64.0.1' -Server2Port 5432 -LanHost '192.168.4.34' -ConnectSampleCount 5 -ResultPath (Join-Path $root 'pre-slow.json') -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp -ConnectMs @(24, 25, 26, 25, 25)) -Now $script:FixedNow
    Assert-C1408 -Cond ($slow.ExitCode -eq 3 -and $slow.Verdict -eq 'ABORT') -Name 'connect median 25 ms exits 3 ABORT' -Detail ('exit=' + $slow.ExitCode + ' ' + ($slow.Lines -join ' | '))
    Assert-C1408 -Cond (@($slow.Lines | Where-Object { $_ -eq 'ABORT' }).Count -eq 1) -Name 'ABORT is printed on its own line'
    $fast = Invoke-PostgresCutoverMonitor -Mode PreWindow -Execute -Server2Host '100.64.0.1' -Server2Port 5432 -ConnectSampleCount 5 -ResultPath (Join-Path $root 'pre-fast.json') -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp -ConnectMs @(6.5, 6.7, 6.9, 6.7, 6.7)) -Now $script:FixedNow
    Assert-C1408 -Cond ($fast.ExitCode -eq 0 -and $fast.Verdict -eq 'OK') -Name 'connect median 6.7 ms is the accepted band, exits 0' -Detail ('exit=' + $fast.ExitCode)
    $edge = Invoke-PostgresCutoverMonitor -Mode PreWindow -Execute -Server2Host '100.64.0.1' -ConnectSampleCount 3 -ResultPath (Join-Path $root 'pre-edge.json') -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp -ConnectMs @(20, 20, 20)) -Now $script:FixedNow
    Assert-C1408 -Cond ($edge.ExitCode -eq 0) -Name 'connect median exactly 20 ms is not above the line' -Detail ('exit=' + $edge.ExitCode)
    Assert-C1408 -Cond ((Get-CutoverMedian -Values @(3, 1, 2)) -eq 2 -and (Get-CutoverMedian -Values @(1, 2, 3, 10)) -eq 2.5) -Name 'median handles odd and even counts'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_PlanDefaultDoesNotMeasure {
    $root = New-C1408Scratch
    $hc = @{ calls = 0 }
    $tc = @{ calls = 0 }
    $rp = Join-Path $root 'plan.json'
    $r = Invoke-PostgresCutoverMonitor -Mode Watch -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -Server2Host '100.64.0.1' -BaselinePath (Join-Path $root 'missing.json') -ResultPath $rp -HttpGet (New-C1408FakeHttp -Counter $hc) -TcpConnectMs (New-C1408FakeTcp -Counter $tc) -Now $script:FixedNow
    Assert-C1408 -Cond ($r.ExitCode -eq 0 -and $hc.calls -eq 0 -and $tc.calls -eq 0) -Name 'dry plan (no -Execute) calls no seam and exits 0' -Detail ('exit=' + $r.ExitCode + ' http=' + $hc.calls + ' tcp=' + $tc.calls)
    Assert-C1408 -Cond (-not (Test-Path -LiteralPath $rp)) -Name 'dry plan writes no result file'
    Assert-C1408 -Cond (@($r.Lines | Where-Object { $_ -like '*agent-tasks/pipeline*p1*' }).Count -ge 1) -Name 'dry plan names the pipeline url it would sample'
    $miss = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath (Join-Path $root 'missing.json') -ResultPath (Join-Path $root 'miss.json') -HttpGet (New-C1408FakeHttp -Counter $hc) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    Assert-C1408 -Cond ($miss.ExitCode -eq 4 -and $miss.Verdict -eq 'INVALID' -and $hc.calls -eq 0) -Name 'watch without a baseline exits 4 before any request' -Detail ('exit=' + $miss.ExitCode + ' http=' + $hc.calls)
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $raw = Get-Content -LiteralPath $b.BaselinePath -Raw
    Assert-C1408 -Cond ($raw -notmatch 'hunter2' -and $raw -notmatch '(?i)"password"' -and $raw -notmatch '(?i)connectionString' -and $raw -match 'NoResetOnClose') -Name 'recorded diagnostics drop credential-looking properties and keep pool flags' -Detail $raw
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1408_SourceIsReadOnly {
    $src = Get-Content -LiteralPath $monitor -Raw
    foreach ($bad in @('restart-apphost', 'docker', 'user-secrets', 'psql', 'Npgsql', 'Stop-Process', 'Stop-Service', 'Restart-Service', 'Remove-Item')) {
        Assert-C1408 -Cond ($src -notmatch [regex]::Escape($bad)) -Name ('source has no ' + $bad)
    }
    Assert-C1408 -Cond ($src -notmatch '(?i)-Method\s+(Post|Put|Delete|Patch)') -Name 'source has no non-GET HTTP verb'
    $nonAscii = [regex]::Matches($src, '[^\x00-\x7F]').Count
    Assert-C1408 -Cond ($nonAscii -eq 0) -Name 'source is ASCII-only' -Detail ('non-ascii=' + $nonAscii)
}

function Test-C1408_ProcessExitCodeAndRollbackLine {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 1000)
    $rp = Join-Path $root 'child-result.json'
    $cmd = @'
& '__MON__' -Mode Watch -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath '__BP__' -ResultPath '__RP__' -SampleCount 5 -HttpGet { param([string]$Url); if ($Url -like '*/pipeline*') { return @{ StatusCode = 200; Body = '{}'; ElapsedMs = 3500 } }; if ($Url -like '*/api/agent-tasks?*') { return @{ StatusCode = 200; Body = '{"items":[]}' } }; if ($Url -like '*/health*') { return @{ StatusCode = 200; Body = 'Healthy' } }; return @{ StatusCode = 200; Body = '{"sha":"aaaa","capabilities":["land-v2"]}' } }; exit $LASTEXITCODE
'@
    $cmd = $cmd.Replace('__MON__', $monitor).Replace('__BP__', $b.BaselinePath).Replace('__RP__', $rp)
    $out = & pwsh -NoProfile -NonInteractive -Command $cmd 2>&1 | ForEach-Object { [string]$_ }
    $code = $LASTEXITCODE
    Assert-C1408 -Cond ($code -eq 2) -Name 'child pwsh exits 2 on rollback' -Detail ('exit=' + $code + ' ' + ($out -join ' | '))
    Assert-C1408 -Cond (@($out | Where-Object { $_ -eq 'ROLLBACK' }).Count -eq 1) -Name 'child pwsh prints ROLLBACK on stdout'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# D1 (Review 39af9e8d): the list is read with the server's supported key, and an unreadable list never yields OK.
function Test-C1408_TaskListUnreadableIsNeverOk {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    Assert-C1408 -Cond ($b.Result.ExitCode -eq 0) -Name 'baseline against the key-checking list fake exits 0' -Detail ('exit=' + $b.Result.ExitCode + ' ' + ($b.Result.Lines -join ' | '))
    $ten = @(); for ($i = 0; $i -lt 10; $i++) { $ten += (New-C1408Task -Status 'Failed') }
    $c = @{ calls = 0 }
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items $ten -Counter $c
    Assert-C1408 -Cond ($w.ExitCode -eq 2 -and $w.Verdict -eq 'ROLLBACK') -Name 'ten failed tasks behind a server-shaped list exit 2 ROLLBACK' -Detail ('exit=' + $w.ExitCode + ' ' + ($w.Lines -join ' | '))
    $listUrl = [string]@($c.listUrls)[0]
    Assert-C1408 -Cond ($listUrl -match '[?&]since=' -and $listUrl -notmatch '(?i)updatedSince') -Name 'task list asks with since=, not updatedSince=' -Detail $listUrl
    $w400 = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items $ten -ListStatus 400 -Name 'w400'
    Assert-C1408 -Cond ($w400.ExitCode -eq 4 -and $w400.Verdict -eq 'INVALID') -Name 'a non-200 task list exits 4 INVALID, not 0' -Detail ('exit=' + $w400.ExitCode + ' ' + ($w400.Lines -join ' | '))
    $wTxt = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -ListBody '<html>proxy error</html>' -Name 'wtxt'
    Assert-C1408 -Cond ($wTxt.ExitCode -eq 4) -Name 'a non-JSON task list body exits 4' -Detail ('exit=' + $wTxt.ExitCode)
    $wEnv = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -ListBody '{"scope":null,"excluded":{"total":0}}' -Name 'wenv'
    Assert-C1408 -Cond ($wEnv.ExitCode -eq 4) -Name 'a task list without the items envelope exits 4' -Detail ('exit=' + $wEnv.ExitCode)
    $wEmpty = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -ListBody '{"scope":null,"items":[],"excluded":{"total":0}}' -Name 'wempty'
    Assert-C1408 -Cond ($wEmpty.ExitCode -eq 0) -Name 'an empty items envelope is a readable list (exit 0)' -Detail ('exit=' + $wEmpty.ExitCode + ' ' + ($wEmpty.Lines -join ' | '))
    $wSlow = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 3500) -ListStatus 500 -Name 'wslow'
    Assert-C1408 -Cond ($wSlow.ExitCode -eq 2) -Name 'a latency rollback still exits 2 when the list is unreadable' -Detail ('exit=' + $wSlow.ExitCode)
    $bp2 = Join-Path $root 'bad-baseline.json'
    $bb = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $bp2 -ResultPath (Join-Path $root 'bad-baseline-result.json') -SampleCount 3 -HttpGet (New-C1408FakeHttp -ListStatus 400) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    Assert-C1408 -Cond ($bb.ExitCode -eq 4 -and -not (Test-Path -LiteralPath $bp2)) -Name 'baseline with an unreadable list exits 4 and writes no baseline' -Detail ('exit=' + $bb.ExitCode)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# D2 (Review 39af9e8d): list items carry no land outcome; failed lands come from the detail route, bounded.
function Test-C1408_FailedLandsFromTaskDetail {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $refused = New-C1408Task -Status 'Succeeded' -Land 'Refused'
    Assert-C1408 -Cond (-not $refused.Contains('landing') -and -not $refused.Contains('landRequest') -and $refused.Contains('landAttempt')) -Name 'fake list item has the summary shape: no landing/landRequest, has landAttempt'
    $items = @($refused, (New-C1408Task -Status 'Blocked' -Land 'NeedsResolution'), (New-C1408Task -Status 'Succeeded' -Land 'Refused'), (New-C1408Task -Status 'Succeeded' -Land 'Landed'), (New-C1408Task -Status 'Succeeded' -Workspace 'Shared'), (New-C1408Task -Status 'Succeeded' -Workspace 'Shared'))
    $c = @{ calls = 0 }
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items $items -Counter $c
    $f = (Get-Content -LiteralPath $w.ResultPath -Raw | ConvertFrom-Json).failures
    Assert-C1408 -Cond ([int]$f.failedLands -eq 3 -and [int]$f.failedTasks -eq 0) -Name 'three failed lands are counted from task detail' -Detail ($f | ConvertTo-Json -Compress)
    Assert-C1408 -Cond ($w.ExitCode -eq 2 -and $w.Verdict -eq 'ROLLBACK') -Name 'three failed lands vs baseline 0 exit 2 ROLLBACK' -Detail ('exit=' + $w.ExitCode + ' ' + ($w.Lines -join ' | '))
    Assert-C1408 -Cond ($c.details -eq 4) -Name 'detail is fetched only for the four tasks with a land attempt' -Detail ('details=' + $c.details)
    $wCap = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items @((New-C1408Task -Land 'Landed'), (New-C1408Task -Land 'Landed'), (New-C1408Task -Land 'Landed')) -MaxLandDetails 2 -Name 'wcap'
    Assert-C1408 -Cond ($wCap.ExitCode -eq 4) -Name 'more land candidates than -MaxLandDetailRequests exits 4' -Detail ('exit=' + $wCap.ExitCode + ' ' + ($wCap.Lines -join ' | '))
    $wDet = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items @((New-C1408Task -Land 'Refused')) -DetailStatus 500 -Name 'wdet'
    Assert-C1408 -Cond ($wDet.ExitCode -eq 4) -Name 'an unreadable task detail exits 4' -Detail ('exit=' + $wDet.ExitCode)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# D3 (Review 39af9e8d): URL userinfo and secret query values are never printed or recorded.
function Test-C1408_UrlSecretsNeverPrinted {
    $root = New-C1408Scratch
    $server = 'http://SENTUSER1408:SENTPW1408@fake.invalid'
    $pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&access_token=SENTTOK1408'
    $bp = Join-Path $root 'baseline.json'
    $b = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl $server -PipelineUrl $pipe -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'b-result.json') -SampleCount 3 -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $w = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl $server -PipelineUrl $pipe -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'w-result.json') -SampleCount 3 -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $p = Invoke-PostgresCutoverMonitor -Mode Plan -ServerBaseUrl $server -PipelineUrl $pipe -ProjectId 'p1' -BaselinePath $bp -Now $script:FixedNow
    $sentinel = 'SENTUSER1408|SENTPW1408|SENTTOK1408'
    $lines = @($b.Lines) + @($w.Lines) + @($p.Lines)
    Assert-C1408 -Cond ($b.ExitCode -eq 0 -and $w.ExitCode -eq 0) -Name 'credentialed urls still measure (baseline and watch exit 0)' -Detail ('b=' + $b.ExitCode + ' w=' + $w.ExitCode + ' ' + ($lines -join ' | '))
    Assert-C1408 -Cond (@($lines | Where-Object { $_ -match $sentinel }).Count -eq 0) -Name 'no userinfo or token sentinel in Plan, Baseline or Watch lines' -Detail (@($lines | Where-Object { $_ -match $sentinel }) -join ' | ')
    $json = ''
    foreach ($f in @(Get-ChildItem -LiteralPath $root -Filter '*.json')) { $json += (Get-Content -LiteralPath $f.FullName -Raw) }
    Assert-C1408 -Cond ($json.Length -gt 0 -and $json -notmatch $sentinel) -Name 'no userinfo or token sentinel in any recorded json' -Detail ([regex]::Match($json, '.{0,40}(' + $sentinel + ').{0,20}').Value)
    $errOut = Join-Path $root 'child.err'
    $cmd = @'
& '__MON__' -Mode Watch -Execute -ServerBaseUrl '__SRV__' -PipelineUrl '__PIPE__' -ProjectId 'p1' -BaselinePath '__BP__' -ResultPath '__RP__' -SampleCount 2 -HttpGet { param([string]$Url); throw ('request failed for ' + $Url) }; & '__MON__' -Mode Plan -ServerBaseUrl '__SRV__' -PipelineUrl '__PIPE__'; exit $LASTEXITCODE
'@
    $cmd = $cmd.Replace('__MON__', $monitor).Replace('__SRV__', $server).Replace('__PIPE__', $pipe).Replace('__BP__', $bp).Replace('__RP__', (Join-Path $root 'child-result.json'))
    $out = @(& pwsh -NoProfile -NonInteractive -Command $cmd 2> $errOut | ForEach-Object { [string]$_ })
    $err = ''
    if (Test-Path -LiteralPath $errOut) { $err = [string](Get-Content -LiteralPath $errOut -Raw) }
    Assert-C1408 -Cond ($out.Count -gt 0 -and @($out | Where-Object { $_ -match $sentinel }).Count -eq 0 -and $err -notmatch $sentinel) -Name 'child pwsh stdout and stderr carry no sentinel, including request errors' -Detail ((@($out | Where-Object { $_ -match $sentinel }) -join ' | ') + ' ERR ' + $err)
    $childJson = ''
    if (Test-Path -LiteralPath (Join-Path $root 'child-result.json')) { $childJson = Get-Content -LiteralPath (Join-Path $root 'child-result.json') -Raw }
    Assert-C1408 -Cond ($childJson -notmatch $sentinel -and $childJson -match 'request failed') -Name 'recorded request errors keep the message without the sentinel' -Detail $childJson
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# Review 39af9e8d advisories: secret value shapes, crash exit code, non-finite baseline median.
function Test-C1408_AdvisoryGuards {
    $root = New-C1408Scratch
    $bp = Join-Path $root 'baseline.json'
    $diag = '{"pool":{"NoResetOnClose":true,"dsn":"postgres://u:SENTDSN1408@h/db","note":"Host=x;Pwd=SENTPWD1408;Database=a"}}'
    $b = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'b-result.json') -SampleCount 3 -HttpGet (New-C1408FakeHttp -DiagBody $diag) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $raw = Get-Content -LiteralPath $bp -Raw
    Assert-C1408 -Cond ($b.ExitCode -eq 0 -and $raw -notmatch 'SENTDSN1408|SENTPWD1408' -and $raw -match 'NoResetOnClose') -Name 'dsn and connection-string-shaped values are dropped by shape' -Detail $raw
    $crash = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'crash.json') -SampleCount 3 -HttpGet (New-C1408FakeHttp) -TcpConnectMs (New-C1408FakeTcp) -Now { throw 'clock unavailable' }
    Assert-C1408 -Cond ($crash.ExitCode -eq 4 -and $crash.Verdict -eq 'INVALID') -Name 'an unexpected error exits 4 INVALID, not 1' -Detail ('exit=' + $crash.ExitCode + ' ' + ($crash.Lines -join ' | '))
    $nanPath = Join-Path $root 'nan-baseline.json'
    $nan = (Get-Content -LiteralPath $bp -Raw).Replace('"medianMs": 100', '"medianMs": "NaN"')
    [System.IO.File]::WriteAllText($nanPath, $nan)
    $wNan = Invoke-C1408Watch -Root $root -BaselinePath $nanPath -PipelineMs (Get-C1408Twenty -Median 5000) -Name 'wnan'
    Assert-C1408 -Cond ($nan -match '"NaN"' -and $wNan.ExitCode -eq 4) -Name 'a NaN baseline median exits 4, never a silent OK' -Detail ('exit=' + $wNan.ExitCode)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# D1 (Review 2f1f73a5): a land that threw, or was refused before the attempt counter moved, ends in a
# LandRefused event without a Refused landing op. Both are counted; a LandRefused before the window is not.
function Test-C1408_LandRefusedWithoutRefusedOp {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $exc = New-C1408Task -Status 'Succeeded' -Land 'Exception'
    $pre = New-C1408Task -Status 'Succeeded' -Land 'PreProtocol'
    $old = New-C1408Task -Status 'Succeeded' -Land 'OldRefusal'
    Assert-C1408 -Cond ([int]$pre.landAttempt -eq 0 -and $null -eq $pre.landRequestedAt -and $null -eq $pre.landStartedAt) -Name 'pre-protocol refusal leaves the list item with no land hint (server ClearPending)'
    $c1 = @{ calls = 0 }
    $wExc = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items @($exc) -Counter $c1 -Name 'wexc'
    $fExc = (Get-Content -LiteralPath $wExc.ResultPath -Raw | ConvertFrom-Json).failures
    Assert-C1408 -Cond ([int]$fExc.failedLands -eq 1) -Name 'an exception-ended land (terminalFailureCode, LandRefused, landing Unconfirmed) is counted' -Detail ($fExc | ConvertTo-Json -Compress)
    $c2 = @{ calls = 0 }
    $wPre = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items @($pre) -Counter $c2 -Name 'wpre'
    $fPre = (Get-Content -LiteralPath $wPre.ResultPath -Raw | ConvertFrom-Json).failures
    Assert-C1408 -Cond (@($c2.detailIds) -contains $pre.id) -Name 'a Succeeded worktree task with no land hint is read from task detail' -Detail ('detailIds=' + (@($c2.detailIds) -join ','))
    Assert-C1408 -Cond ([int]$fPre.failedLands -eq 1) -Name 'a pre-protocol refusal (LandRefused in the window, no failure code) is counted' -Detail ($fPre | ConvertTo-Json -Compress)
    $wOld = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items @($old) -Name 'wold'
    $fOld = (Get-Content -LiteralPath $wOld.ResultPath -Raw | ConvertFrom-Json).failures
    Assert-C1408 -Cond ([int]$fOld.failedLands -eq 0 -and $wOld.ExitCode -eq 0) -Name 'a LandRefused before the window followed by a landed land is not counted' -Detail ('exit=' + $wOld.ExitCode + ' ' + ($fOld | ConvertTo-Json -Compress))
    $wAll = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items @($exc, $pre, $old, (New-C1408Task -Status 'Succeeded' -Land 'Exception')) -Name 'wall'
    Assert-C1408 -Cond ($wAll.ExitCode -eq 2 -and $wAll.Verdict -eq 'ROLLBACK') -Name 'three LandRefused-only lands vs baseline 0 exit 2 ROLLBACK' -Detail ('exit=' + $wAll.ExitCode + ' ' + ($wAll.Lines -join ' | '))
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# D2 (Review 2f1f73a5): a literal '@' in the password, a percent-encoded key and a fragment pair never leak
# through stdout, stderr, the recorded JSON or the request's exception text.
function Test-C1408_MaskingBypassForms {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $forms = @(
        @{ Name = 'literal @ in the password'; Server = 'http://admin:p@SENTAT1408@fake.invalid'; Pipe = ''; Sentinel = 'SENTAT1408' },
        @{ Name = 'percent-encoded key'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&access%5Ftoken=SENTPCT1408'; Sentinel = 'SENTPCT1408' },
        @{ Name = 'fragment pair'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1#access_token=SENTFRAG1408&token=SENTFRAG1408'; Sentinel = 'SENTFRAG1408' }
    )
    $n = 0
    foreach ($form in $forms) {
        $n++
        $dir = Join-Path $root ('form' + $n)
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $bp = Join-Path $dir 'baseline.json'
        Copy-Item -LiteralPath $b.BaselinePath -Destination $bp
        $url = $form.Pipe
        if (-not $url) { $url = $form.Server + '/api/agent-tasks/pipeline?projectId=p1' }
        # The seam does what a real request does first: convert the text to [uri]. Its error, or a
        # request error naming the URI .NET would send (percent-decoded, fragment kept), is the exception text.
        $exText = ''
        try { $u = [uri]$url; $exText = 'request failed for ' + $u.AbsoluteUri + ' original ' + $u.OriginalString } catch { $exText = [string]$_.Exception.Message }
        Assert-C1408 -Cond ($exText -match $form.Sentinel -and (Hide-CutoverText -Text $exText) -notmatch $form.Sentinel) -Name ('exception text is masked: ' + $form.Name) -Detail (Hide-CutoverText -Text $exText)
        $pipeArg = ''
        if ($form.Pipe) { $pipeArg = "-PipelineUrl '" + $form.Pipe + "'" }
        $errOut = Join-Path $dir 'child.err'
        $cmd = @'
$seam = { param([string]$Url); $u = [uri]$Url; throw ('request failed for ' + $u.AbsoluteUri + ' original ' + $u.OriginalString) }
& '__MON__' -Mode Plan -ServerBaseUrl '__SRV__' __PIPE__ -ProjectId 'p1' -BaselinePath '__BP__'
& '__MON__' -Mode Baseline -Execute -ServerBaseUrl '__SRV__' __PIPE__ -ProjectId 'p1' -BaselinePath '__DIR__/child-baseline.json' -ResultPath '__DIR__/b-result.json' -SampleCount 2 -HttpGet $seam
& '__MON__' -Mode Watch -Execute -ServerBaseUrl '__SRV__' __PIPE__ -ProjectId 'p1' -BaselinePath '__BP__' -ResultPath '__DIR__/w-result.json' -SampleCount 2 -HttpGet $seam
exit $LASTEXITCODE
'@
        $cmd = $cmd.Replace('__MON__', $monitor).Replace('__SRV__', $form.Server).Replace('__PIPE__', $pipeArg).Replace('__BP__', $bp).Replace('__DIR__', $dir)
        $out = @(& pwsh -NoProfile -NonInteractive -Command $cmd 2> $errOut | ForEach-Object { [string]$_ })
        $err = ''
        if (Test-Path -LiteralPath $errOut) { $err = [string](Get-Content -LiteralPath $errOut -Raw) }
        $leaks = @($out | Where-Object { $_ -match $form.Sentinel })
        Assert-C1408 -Cond ($out.Count -gt 0 -and $leaks.Count -eq 0 -and $err -notmatch $form.Sentinel) -Name ('stdout and stderr carry no sentinel: ' + $form.Name) -Detail (($leaks -join ' | ') + ' ERR ' + $err)
        $json = ''
        foreach ($f in @(Get-ChildItem -LiteralPath $dir -Filter '*-result.json')) { $json += (Get-Content -LiteralPath $f.FullName -Raw) }
        Assert-C1408 -Cond ($json -match 'request failed|Cannot convert' -and $json -notmatch $form.Sentinel) -Name ('recorded json carries the request error without the sentinel: ' + $form.Name) -Detail ([regex]::Match($json, '.{0,60}' + $form.Sentinel + '.{0,20}').Value)
    }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# Runs the masking-form child/in-process checks shared by the C1423 cases: each form's sentinel must appear in no
# Plan, Baseline or Watch line, no recorded JSON and no child pwsh stdout or stderr, including the request's
# exception text (the fake converts the URL to [uri] first, as a real request does).
function Invoke-C1423MaskingForms {
    param([string]$Root, [string]$BaselinePath, [object[]]$Forms)
    $n = 0
    foreach ($form in $Forms) {
        $n++
        $dir = Join-Path $Root ('c1423form' + $n)
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $bp = Join-Path $dir 'baseline.json'
        Copy-Item -LiteralPath $BaselinePath -Destination $bp
        $url = $form.Pipe
        if (-not $url) { $url = $form.Server + '/api/agent-tasks/pipeline?projectId=p1' }
        $exText = ''
        try { $u = [uri]$url; $exText = 'request failed for ' + $u.AbsoluteUri + ' original ' + $u.OriginalString } catch { $exText = [string]$_.Exception.Message }
        # The request error is masked by Invoke-CutoverHttpGet's catch, which adds this URL's known forms (userinfo and secret query values).
        $r = Invoke-CutoverHttpGet -Url $url -HttpGet { param([string]$Url); $u = [uri]$Url; throw ('request failed for ' + $u.AbsoluteUri + ' original ' + $u.OriginalString) }
        Assert-C1408 -Cond ($exText -match $form.Sentinel -and $r.Ok -eq $false -and $r.Error -notmatch $form.Sentinel) -Name ('exception text is masked: ' + $form.Name) -Detail $r.Error
        $p = Invoke-PostgresCutoverMonitor -Mode Plan -ServerBaseUrl $form.Server -PipelineUrl $form.Pipe -ProjectId 'p1' -BaselinePath $bp -Now $script:FixedNow
        # An optional Diag body puts the form's sentinel on the pool-diagnostics surface (Hide-CutoverSecrets, then the JSON).
        $diag = ''
        if ($form.ContainsKey('Diag')) { $diag = [string]$form.Diag }
        $http = New-C1408FakeHttp -DiagBody $diag
        $b = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl $form.Server -PipelineUrl $form.Pipe -ProjectId 'p1' -BaselinePath (Join-Path $dir 'in-baseline.json') -ResultPath (Join-Path $dir 'in-b-result.json') -SampleCount 2 -HttpGet $http -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
        $w = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl $form.Server -PipelineUrl $form.Pipe -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $dir 'in-w-result.json') -SampleCount 2 -HttpGet $http -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
        $lines = @($p.Lines) + @($b.Lines) + @($w.Lines)
        Assert-C1408 -Cond ($b.ExitCode -eq 0 -and $w.ExitCode -eq 0) -Name ('the request still routes and measures: ' + $form.Name) -Detail ('b=' + $b.ExitCode + ' w=' + $w.ExitCode + ' ' + ($lines -join ' | '))
        Assert-C1408 -Cond (@($lines | Where-Object { $_ -match $form.Sentinel }).Count -eq 0) -Name ('no sentinel in Plan, Baseline or Watch lines: ' + $form.Name) -Detail (@($lines | Where-Object { $_ -match $form.Sentinel }) -join ' | ')
        $inJson = ''
        foreach ($f in @(Get-ChildItem -LiteralPath $dir -Filter 'in-*.json')) { $inJson += (Get-Content -LiteralPath $f.FullName -Raw) }
        Assert-C1408 -Cond ($inJson.Length -gt 0 -and $inJson -notmatch $form.Sentinel) -Name ('no sentinel in the in-process baseline and result json, pool flags included: ' + $form.Name) -Detail ([regex]::Match($inJson, '.{0,60}' + $form.Sentinel + '.{0,20}').Value)
        # The child command quotes the URLs with single quotes, so an apostrophe inside a URL is doubled.
        $pipeArg = ''
        if ($form.Pipe) { $pipeArg = "-PipelineUrl '" + $form.Pipe.Replace("'", "''") + "'" }
        $errOut = Join-Path $dir 'child.err'
        $cmd = @'
$seam = { param([string]$Url); $u = [uri]$Url; throw ('request failed for ' + $u.AbsoluteUri + ' original ' + $u.OriginalString) }
& '__MON__' -Mode Plan -ServerBaseUrl '__SRV__' __PIPE__ -ProjectId 'p1' -BaselinePath '__BP__'
& '__MON__' -Mode Baseline -Execute -ServerBaseUrl '__SRV__' __PIPE__ -ProjectId 'p1' -BaselinePath '__DIR__/child-baseline.json' -ResultPath '__DIR__/b-result.json' -SampleCount 2 -HttpGet $seam
& '__MON__' -Mode Watch -Execute -ServerBaseUrl '__SRV__' __PIPE__ -ProjectId 'p1' -BaselinePath '__BP__' -ResultPath '__DIR__/w-result.json' -SampleCount 2 -HttpGet $seam
exit $LASTEXITCODE
'@
        $cmd = $cmd.Replace('__MON__', $monitor).Replace('__SRV__', $form.Server.Replace("'", "''")).Replace('__PIPE__', $pipeArg).Replace('__BP__', $bp).Replace('__DIR__', $dir)
        $out = @(& pwsh -NoProfile -NonInteractive -Command $cmd 2> $errOut | ForEach-Object { [string]$_ })
        $err = ''
        if (Test-Path -LiteralPath $errOut) { $err = [string](Get-Content -LiteralPath $errOut -Raw) }
        $leaks = @($out | Where-Object { $_ -match $form.Sentinel })
        Assert-C1408 -Cond ($out.Count -gt 0 -and $leaks.Count -eq 0 -and $err -notmatch $form.Sentinel) -Name ('child stdout and stderr carry no sentinel: ' + $form.Name) -Detail (($leaks -join ' | ') + ' ERR ' + $err)
        $json = ''
        foreach ($f in @(Get-ChildItem -LiteralPath $dir -Filter '*.json')) { $json += (Get-Content -LiteralPath $f.FullName -Raw) }
        Assert-C1408 -Cond ($json -match 'request failed|Cannot convert' -and $json -notmatch $form.Sentinel) -Name ('recorded json carries the request error without the sentinel: ' + $form.Name) -Detail ([regex]::Match($json, '.{0,60}' + $form.Sentinel + '.{0,20}').Value)
    }
}

# A1 (CARD-1423, Review of CARD-1408 at 2324f10d): a 'password' containing '/' is parsed by .NET as host, port and
# path, so nothing is sent; but if the value was meant as userinfo it must not print as path text either.
function Test-C1423_SlashInUserinfoMasked {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $masked = Hide-CutoverText -Text 'http://u:1234/SENTSLASH1423@h/api/x'
    Assert-C1408 -Cond ($masked -eq 'http://<redacted>@h/api/x') -Name 'userinfo with a slash in the password is masked to the last @' -Detail $masked
    Invoke-C1423MaskingForms -Root $root -BaselinePath $b.BaselinePath -Forms @(
        @{ Name = 'slash in the password'; Server = 'http://u:12/SENTSLASH1423@fake.invalid'; Pipe = ''; Sentinel = 'SENTSLASH1423' }
    )
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# A2 (CARD-1423): a secret pair inside a percent-encoded value of a non-secret key (x=y%26token%3DS is sent as
# x=y&token=S), a double-encoded one, and an encoded URL with userinfo inside a plain value are secrets; a
# benign encoded value is kept.
function Test-C1423_EncodedValueSecretsMasked {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $benign = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&redirect=%2Fhome%3Ftab%3D1'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $benign) -eq $benign) -Name 'a benign encoded value is kept' -Detail (Hide-CutoverText -Text $benign)
    $single = Hide-CutoverText -Text 'http://fake.invalid/p?projectId=p1&x=y%26token%3DSENTENC1423&z=1'
    Assert-C1408 -Cond ($single -notmatch 'SENTENC1423' -and $single -match 'projectId=p1' -and $single -match '&z=1') -Name 'an encoded secret pair inside a plain value is masked and its neighbours kept' -Detail $single
    Invoke-C1423MaskingForms -Root $root -BaselinePath $b.BaselinePath -Forms @(
        @{ Name = 'encoded pair inside a plain value'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&x=y%26token%3DSENTENC1423'; Sentinel = 'SENTENC1423' },
        @{ Name = 'double-encoded pair inside a plain value'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&x=y%2526token%253DSENTDBL1423'; Sentinel = 'SENTDBL1423' },
        @{ Name = 'encoded userinfo url inside a plain value'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&next=http%3A%2F%2Fu%3ASENTENCURL1423%40h%2Fdb'; Sentinel = 'SENTENCURL1423' }
    )
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# A3 (CARD-1423): the detail cap only bites with -WindowMinutes around 8 hours or more; the INVALID reason and the
# help say so and name the remedy. The cap itself (INVALID, never a partial count) is unchanged.
function Test-C1423_DetailCapNamesTheWindow {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $wCap = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items @((New-C1408Task -Land 'Landed'), (New-C1408Task -Land 'Landed'), (New-C1408Task -Land 'Landed')) -MaxLandDetails 2 -Name 'wcap'
    Assert-C1408 -Cond ($wCap.ExitCode -eq 4 -and $wCap.Verdict -eq 'INVALID') -Name 'more land candidates than the cap still exits 4 INVALID' -Detail ('exit=' + $wCap.ExitCode)
    $unreadable = @($wCap.Lines | Where-Object { $_ -like 'UNREADABLE*' })
    Assert-C1408 -Cond ($unreadable.Count -eq 1 -and $unreadable[0] -match '3 tasks with a land attempt exceed -MaxLandDetailRequests 2' -and $unreadable[0] -match '-WindowMinutes' -and $unreadable[0] -match '(?i)8 hours') -Name 'the UNREADABLE line names the cap, -WindowMinutes and when the cap bites' -Detail ($unreadable -join ' | ')
    $recorded = @((Get-Content -LiteralPath $wCap.ResultPath -Raw | ConvertFrom-Json).failures.unreadable)
    Assert-C1408 -Cond ($recorded.Count -eq 1 -and [string]$recorded[0] -match '-WindowMinutes') -Name 'the recorded failures.unreadable carries the same remedy' -Detail ($recorded -join ' | ')
    $src = Get-Content -LiteralPath $monitor -Raw
    Assert-C1408 -Cond ($src -match '(?is)Detail cap:.*?-MaxLandDetailRequests.*?-WindowMinutes around 8 hours or more') -Name 'the help documents when -MaxLandDetailRequests bites' -Detail 'no "Detail cap:" paragraph naming -WindowMinutes around 8 hours or more'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# A4 (CARD-1423): a Blocked task whose land threw before the window carries terminalFailureCode without a
# publication, so the failure-code rule counts it in every window the list returns it in; Baseline and Watch count it
# alike and the delta is unaffected. Documented in the header's known effects and plan D-6, not changed.
function Test-C1423_BlockedPreWindowLandThrewCountsEqually {
    $root = New-C1408Scratch
    $blocked = New-C1408Task -Status 'Blocked' -Land 'Exception'
    $land = $script:C1408LandById[$blocked.id]
    # Every land event before the 09:30Z window start (FixedNow 10:00Z, 30 minutes).
    foreach ($e in @($land.events)) { $e.at = '2026-10-21T08:52:00Z' }
    $land.landRequest.requestedAt = '2026-10-21T08:50:30Z'
    $land.landRequest.startedAt = '2026-10-21T08:50:31Z'
    $land.landRequest.lastEvaluatedAt = '2026-10-21T08:52:00Z'
    $land.landRequest.lastProgressAt = '2026-10-21T08:52:00Z'
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100) -Items @($blocked)
    $base = Get-Content -LiteralPath $b.BaselinePath -Raw | ConvertFrom-Json
    Assert-C1408 -Cond ($b.Result.ExitCode -eq 0 -and [int]$base.failures.failedLands -eq 1 -and [int]$base.failures.failedTasks -eq 0) -Name 'baseline counts the pre-window land that threw on a Blocked task (failure-code rule)' -Detail ('exit=' + $b.Result.ExitCode + ' ' + ($base.failures | ConvertTo-Json -Compress))
    $w = Invoke-C1408Watch -Root $root -BaselinePath $b.BaselinePath -PipelineMs (Get-C1408Twenty -Median 100) -Items @($blocked)
    $f = (Get-Content -LiteralPath $w.ResultPath -Raw | ConvertFrom-Json).failures
    Assert-C1408 -Cond ([int]$f.failedLands -eq 1 -and $w.ExitCode -eq 0 -and $w.Verdict -eq 'OK') -Name 'watch counts it equally, so the delta is 0 and the verdict OK' -Detail ('exit=' + $w.ExitCode + ' ' + ($f | ConvertTo-Json -Compress) + ' ' + ($w.Lines -join ' | '))
    $src = Get-Content -LiteralPath $monitor -Raw
    Assert-C1408 -Cond ($src -match '(?is)Known gaps, equal in Baseline and Watch:.*?Blocked task whose land threw before the window') -Name 'the header names the Blocked pre-window land as a known equal-in-both-windows effect' -Detail 'no known-gaps text naming a Blocked task whose land threw before the window'
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# DEF-1 (CARD-1423 Final Review 3a27a954): .NET keeps ' " space < and > as part of the password (the first stays raw in
# AbsoluteUri; the others are escaped there and raw in OriginalString), but the old userinfo class [^\s"'<>]*@ stopped
# at them, so such a password printed raw in PLAN lines, the result JSON server and url fields and the recorded request
# error. One case per character over the Invoke-C1423MaskingForms surfaces plus the pool-diagnostics JSON surface.
function Invoke-C1423UserinfoCharacter {
    param([string]$Label, [string]$Char, [string]$Sentinel)
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $server = 'http://u:' + $Sentinel + $Char + 'q@fake.invalid'
    $masked = Hide-CutoverText -Text ('PLAN would GET ' + $server + '/health x3')
    Assert-C1408 -Cond ($masked -eq 'PLAN would GET http://<redacted>@fake.invalid/health x3') -Name ('userinfo with ' + $Label + ' in the password is masked to the last @ on the line') -Detail $masked
    Assert-C1408 -Cond (Test-CutoverSecretValue -Value $server) -Name ('a pool string value with ' + $Label + ' in its userinfo is a secret value') -Detail $server
    $diag = '{"pool":{"NoResetOnClose":true,"Endpoint":' + (ConvertTo-Json -InputObject $server -Compress) + '}}'
    Invoke-C1423MaskingForms -Root $root -BaselinePath $b.BaselinePath -Forms @(
        @{ Name = ($Label + ' in the password'); Server = $server; Pipe = ''; Sentinel = $Sentinel; Diag = $diag }
    )
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-C1423_ApostropheInUserinfoMasked { Invoke-C1423UserinfoCharacter -Label 'an apostrophe' -Char "'" -Sentinel 'SENTAPOS1423' }

function Test-C1423_DoubleQuoteInUserinfoMasked { Invoke-C1423UserinfoCharacter -Label 'a double quote' -Char '"' -Sentinel 'SENTDQ1423' }

function Test-C1423_SpaceInUserinfoMasked {
    Invoke-C1423UserinfoCharacter -Label 'a space' -Char ' ' -Sentinel 'SENTSP1423'
    # D-1 after DEF-1: a newline is the only terminator, so a later @ on the same line is masked too (over-masking accepted).
    $plain = 'PLAN would GET http://fake.invalid/health and mail ops@fake.invalid'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $plain) -eq 'PLAN would GET http://<redacted>@fake.invalid') -Name 'a URL followed by whitespace and a later @ on the same line is masked to that @ (newline is the only terminator)' -Detail (Hide-CutoverText -Text $plain)
    $twoLines = "PLAN would GET http://fake.invalid/health`nmail ops@fake.invalid"
    Assert-C1408 -Cond ((Hide-CutoverText -Text $twoLines) -eq $twoLines) -Name 'an @ on a later line does not extend the userinfo' -Detail (Hide-CutoverText -Text $twoLines)
}

function Test-C1423_LessThanInUserinfoMasked { Invoke-C1423UserinfoCharacter -Label 'a less-than sign' -Char '<' -Sentinel 'SENTLT1423' }

function Test-C1423_GreaterThanInUserinfoMasked { Invoke-C1423UserinfoCharacter -Label 'a greater-than sign' -Char '>' -Sentinel 'SENTGT1423' }

# DEF-1 alongside: a quoted connection-string value (Password="..." or Password='...') was missed because the pair value
# could not start with a quote. Raw in the query, inside an encoded value, and alone in a line.
function Test-C1423_QuotedConnectionStringPasswordMasked {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $dq = Hide-CutoverText -Text 'Host=x;Password="SENTDQP1423";Port=1'
    Assert-C1408 -Cond ($dq -eq 'Host=x;Password=<redacted>;Port=1') -Name 'a double-quoted Password= value is masked whole and its neighbours kept' -Detail $dq
    $sq = Hide-CutoverText -Text "Host=x;Password='SENTSQP1423';Port=1"
    Assert-C1408 -Cond ($sq -eq 'Host=x;Password=<redacted>;Port=1') -Name 'a single-quoted Password= value is masked whole and its neighbours kept' -Detail $sq
    $spaced = Hide-CutoverText -Text 'Host=x;Password="SENTSPQ1423 and more";Port=1'
    Assert-C1408 -Cond ($spaced -eq 'Host=x;Password=<redacted>;Port=1') -Name 'a quoted Password= value containing spaces is masked whole' -Detail $spaced
    $plainQuoted = 'Host=x;Database="app db";Port=1'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $plainQuoted) -eq $plainQuoted) -Name 'a quoted value of a plain key is kept' -Detail (Hide-CutoverText -Text $plainQuoted)
    Invoke-C1423MaskingForms -Root $root -BaselinePath $b.BaselinePath -Forms @(
        @{ Name = 'double-quoted Password= raw in the query'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&cs=Host=x;Password="SENTDQRAW1423"'; Sentinel = 'SENTDQRAW1423' },
        @{ Name = 'single-quoted Password= raw in the query'; Server = 'http://fake.invalid'; Pipe = "http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&cs=Host=x;Password='SENTSQRAW1423'"; Sentinel = 'SENTSQRAW1423' },
        @{ Name = 'double-quoted Password= inside an encoded value'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&cs=Host%3Dx%3BPassword%3D%22SENTDQENC1423%22'; Sentinel = 'SENTDQENC1423' },
        @{ Name = 'single-quoted Password= inside an encoded value'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&cs=Host%3Dx%3BPassword%3D%27SENTSQENC1423%27'; Sentinel = 'SENTSQENC1423' }
    )
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# DEF-1 alongside: the server reads '+' in a query value as a space, exactly as it reads %20, so x=y+token%3DS reaches it
# as x=y token=S; %20 was masked and '+' was not.
function Test-C1423_PlusAsSpaceInEncodedValueMasked {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $percent = Hide-CutoverText -Text 'http://fake.invalid/p?projectId=p1&x=y%20token%3DSENTPCT1423&z=1'
    Assert-C1408 -Cond ($percent -notmatch 'SENTPCT1423' -and $percent -match 'projectId=p1' -and $percent -match '&z=1') -Name 'a %20 before an encoded secret pair is masked (the form the plus must match)' -Detail $percent
    $plus = Hide-CutoverText -Text 'http://fake.invalid/p?projectId=p1&x=y+token%3DSENTPLUS1423&z=1'
    Assert-C1408 -Cond ($plus -notmatch 'SENTPLUS1423' -and $plus -match 'projectId=p1' -and $plus -match '&z=1') -Name 'a plus before an encoded secret pair is masked like %20 and the neighbours kept' -Detail $plus
    $rawPlus = Hide-CutoverText -Text 'http://fake.invalid/p?projectId=p1&x=y+token=SENTPLUSRAW1423&z=1'
    Assert-C1408 -Cond ($rawPlus -notmatch 'SENTPLUSRAW1423' -and $rawPlus -match 'projectId=p1' -and $rawPlus -match '&z=1') -Name 'a plus before a raw secret pair inside a plain value is masked too' -Detail $rawPlus
    $benign = 'http://fake.invalid/p?projectId=p1&q=a+b%20c&z=1'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $benign) -eq $benign) -Name 'a benign value with a plus and a %20 is kept' -Detail (Hide-CutoverText -Text $benign)
    Invoke-C1423MaskingForms -Root $root -BaselinePath $b.BaselinePath -Forms @(
        @{ Name = 'plus as a space before an encoded secret pair'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&x=y+token%3DSENTPLUS1423'; Sentinel = 'SENTPLUS1423' }
    )
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# DEF-1 alongside: the decode loop stopped after four rounds and kept a value encoded five times; any plain value that
# still changes under decoding after the bound is now masked whole.
function Test-C1423_FiveTimesEncodedValueMasked {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $levels = @()
    $v = 'y&token=SENTFIVE1423'
    for ($i = 1; $i -le 6; $i++) { $v = [uri]::EscapeDataString($v); $levels += $v }
    for ($i = 0; $i -lt $levels.Count; $i++) {
        $url = 'http://fake.invalid/p?projectId=p1&x=' + $levels[$i] + '&z=1'
        $masked = Hide-CutoverText -Text $url
        Assert-C1408 -Cond ($masked -notmatch 'SENTFIVE1423' -and $masked -match 'projectId=p1' -and $masked -match '&z=1') -Name ('a secret pair encoded ' + ($i + 1) + 'x inside a plain value is masked and the neighbours kept') -Detail $masked
    }
    $deepBenign = 'http://fake.invalid/p?projectId=p1&redirect=%252Fhome%253Ftab%253D1&z=1'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $deepBenign) -eq $deepBenign) -Name 'a benign value encoded twice is kept' -Detail (Hide-CutoverText -Text $deepBenign)
    Invoke-C1423MaskingForms -Root $root -BaselinePath $b.BaselinePath -Forms @(
        @{ Name = 'secret pair encoded five times inside a plain value'; Server = 'http://fake.invalid'; Pipe = ('http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&x=' + $levels[4]); Sentinel = 'SENTFIVE1423' }
    )
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# DEF-1 readability witness: with a credentialed server URL whose password carries the DEF-1 characters, the D-11
# rollback reason, the ROLLBACK and CUTOVER-MONITOR verdict lines, the MEASURED numbers and the request-error prefix
# stay readable while the userinfo is masked. A guard the Review confirmed today, kept green through the repair.
function Test-C1423_VerdictLinesReadableWithMaskedUserinfo {
    $root = New-C1408Scratch
    $server = 'http://u:SENTREAD1423 x''y"<z>@fake.invalid'
    $bp = Join-Path $root 'baseline.json'
    $b = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl $server -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'b-result.json') -SampleCount 20 -HttpGet (New-C1408FakeHttp -PipelineMs (Get-C1408Twenty -Median 100)) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $w = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl $server -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'w-result.json') -SampleCount 20 -HttpGet (New-C1408FakeHttp -PipelineMs (Get-C1408Twenty -Median 5000)) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $lines = @($b.Lines) + @($w.Lines)
    Assert-C1408 -Cond ($b.ExitCode -eq 0 -and $w.ExitCode -eq 2 -and $w.Verdict -eq 'ROLLBACK') -Name 'the credentialed watch still decides ROLLBACK (exit 2)' -Detail ('b=' + $b.ExitCode + ' w=' + $w.ExitCode + ' ' + ($lines -join ' | '))
    Assert-C1408 -Cond (@($lines | Where-Object { $_ -match 'SENTREAD1423' }).Count -eq 0) -Name 'no sentinel in the Baseline or Watch lines' -Detail (@($lines | Where-Object { $_ -match 'SENTREAD1423' }) -join ' | ')
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -eq 'REASON median 5000 ms is above the 1000 ms floor and above 3x the baseline median 100 ms (300 ms)' }).Count -eq 1) -Name 'the D-11 rollback reason keeps its numbers' -Detail (@($w.Lines | Where-Object { $_ -like 'REASON*' }) -join ' | ')
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -eq 'ROLLBACK' }).Count -eq 1 -and @($w.Lines | Where-Object { $_ -like 'CUTOVER-MONITOR mode=Watch verdict=ROLLBACK exit=2 medianMs=5000 baselineMedianMs=100 failures=0 baselineFailures=0 result=*' }).Count -eq 1) -Name 'the ROLLBACK and verdict lines keep their numbers' -Detail (@($w.Lines | Where-Object { $_ -like 'CUTOVER-MONITOR*' }) -join ' | ')
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -match '^MEASURED pipeline http://<redacted>@fake\.invalid/api/agent-tasks/pipeline\?projectId=p1 medianMs=5000 minMs=4999 maxMs=5001 succeeded=20 errors=0$' }).Count -eq 1) -Name 'the MEASURED pipeline line keeps its host, path and numbers beside the masked userinfo' -Detail (@($w.Lines | Where-Object { $_ -like 'MEASURED pipeline*' }) -join ' | ')
    $json = Get-Content -LiteralPath (Join-Path $root 'w-result.json') -Raw
    Assert-C1408 -Cond ($json -notmatch 'SENTREAD1423' -and $json -match '"server": "http://<redacted>@fake\.invalid"' -and $json -match '"medianMs": 5000') -Name 'the recorded json keeps the host and the median beside the masked server' -Detail ([regex]::Match($json, '.{0,60}(SENTREAD1423|"server").{0,40}').Value)
    $r = Invoke-CutoverHttpGet -Url ($server + '/api/x') -HttpGet { param([string]$Url); $u = [uri]$Url; throw ('request failed for ' + $u.AbsoluteUri + ' original ' + $u.OriginalString) }
    Assert-C1408 -Cond ($r.Ok -eq $false -and $r.Error -notmatch 'SENTREAD1423' -and $r.Error -like 'request failed for http://<redacted>@fake.invalid/api/x*') -Name 'a request error keeps its message and host beside the masked userinfo' -Detail $r.Error
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# R-DEF-1 (CARD-1423 Final Review 5d4be758): a secret pair's value stopped at whitespace. .NET sends '?token=SEN T1' as
# SEN%20T1, so the whole value is the secret, but the PLAN line printed 'token=<redacted> T1', a failed task-list request
# printed the tail in the UNREADABLE and REASON lines, and 'Host=h;Password=SE NT;' printed 'NT'. The given URLs' secret
# query values (raw, escaped, unescaped and as [uri] spells them) are known forms like the userinfo, and a connection-string
# value runs to the next ';' or the end of the line. A plain value still stops at whitespace, so the verdict line's pairs
# stay, and a numeric secret value is never a form (D-11 numbers).
function Test-C1423_SpaceInSecretValueMasked {
    $root = New-C1408Scratch
    $b = Invoke-C1408Baseline -Root $root -PipelineMs (Get-C1408Twenty -Median 100)
    $cs = Hide-CutoverText -Text 'Host=h;Password=SE NTCS1423;Database=d'
    Assert-C1408 -Cond ($cs -eq 'Host=h;Password=<redacted>;Database=d') -Name 'a connection-string Password= value with a space runs to the next ; and the neighbours are kept' -Detail $cs
    $lead = Hide-CutoverText -Text 'Password=SE NTCS1423;Database=d'
    Assert-C1408 -Cond ($lead -eq 'Password=<redacted>;Database=d') -Name 'a leading Password= value with a space runs to the next ;' -Detail $lead
    $last = Hide-CutoverText -Text 'Host=h;Password=SE NTCS1423'
    Assert-C1408 -Cond ($last -eq 'Host=h;Password=<redacted>') -Name 'a trailing Password= value with a space runs to the end of the line' -Detail $last
    $json = Hide-CutoverText -Text '{"cs":"Host=h;Password=SE NTCS1423;Database=d","n":1}' -JsonText
    Assert-C1408 -Cond ($json -eq '{"cs":"Host=h;Password=<redacted>;Database=d","n":1}') -Name 'in JSON text the value runs to the next ; and stops at the closing quote' -Detail $json
    $verdict = 'CUTOVER-MONITOR mode=Watch verdict=ROLLBACK exit=2 medianMs=5000 baselineMedianMs=100 failures=0 baselineFailures=0'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $verdict) -eq $verdict) -Name 'a plain pair value still stops at whitespace, so the verdict line keeps every pair' -Detail (Hide-CutoverText -Text $verdict)
    Invoke-C1423MaskingForms -Root $root -BaselinePath $b.BaselinePath -Forms @(
        @{ Name = 'space inside a secret query value'; Server = 'http://fake.invalid'; Pipe = 'http://fake.invalid/api/agent-tasks/pipeline?projectId=p1&token=SENTSPA1423 SENTSPB1423'; Sentinel = 'SENTSPA1423|SENTSPB1423' }
    )
    # The task-list URL with a space in its Password= value and a request that fails quoting the URL .NET would send:
    # the UNREADABLE and REASON lines and the result JSON carry neither half of the value.
    $tasks = 'http://fake.invalid/api/agent-tasks?Password=SENTSPC1423 SENTSPD1423'
    $inner = New-C1408FakeHttp -PipelineMs (Get-C1408Twenty -Median 100)
    $seam = { param([string]$Url); if ($Url -like '*api/agent-tasks?Password=*') { $u = [uri]$Url; throw ('request failed for ' + $u.AbsoluteUri + ' original ' + $u.OriginalString) }; & $inner $Url }.GetNewClosure()
    $w = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl 'http://fake.invalid' -TaskListUrl $tasks -ProjectId 'p1' -BaselinePath $b.BaselinePath -ResultPath (Join-Path $root 'tasks-w-result.json') -SampleCount 20 -HttpGet $seam -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $leak = @($w.Lines | Where-Object { $_ -match 'SENTSPC1423|SENTSPD1423' })
    Assert-C1408 -Cond ($w.ExitCode -eq 4 -and $leak.Count -eq 0) -Name 'a failed task-list request with a space in its Password= value is INVALID and no line carries either half' -Detail ('w=' + $w.ExitCode + ' ' + ($leak -join ' | '))
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -like 'UNREADABLE task list returned status 0 request failed for http://fake.invalid/api/agent-tasks?Password=<redacted>*' }).Count -eq 1) -Name 'the UNREADABLE line keeps its status and host beside the masked value' -Detail (@($w.Lines | Where-Object { $_ -like 'UNREADABLE*' }) -join ' | ')
    $rj = Get-Content -LiteralPath (Join-Path $root 'tasks-w-result.json') -Raw
    Assert-C1408 -Cond ($rj -notmatch 'SENTSPC1423|SENTSPD1423' -and $rj -match 'Password=<redacted>') -Name 'the result json carries the masked task-list url and error' -Detail ([regex]::Match($rj, '.{0,60}(SENTSPC1423|SENTSPD1423).{0,20}').Value)
    $forms = @(Get-CutoverKnownForms -Urls @('http://fake.invalid/api/x?projectId=p1&token=SEN TQ1423&z=1'))
    Assert-C1408 -Cond ($forms -contains 'SEN TQ1423' -and $forms -contains 'SEN%20TQ1423' -and -not ($forms -contains 'p1') -and -not ($forms -contains '1')) -Name 'the given URL registers its secret query value raw and escaped, not its plain values' -Detail ($forms -join ' | ')
    $numeric = @(Get-CutoverKnownForms -Urls @('http://fake.invalid/api/x?token=5000'))
    $numLine = Hide-CutoverText -Text 'http://fake.invalid/api/x?token=5000 medianMs=5000'
    Assert-C1408 -Cond ($numeric.Count -eq 0 -and $numLine -eq 'http://fake.invalid/api/x?token=<redacted> medianMs=5000') -Name 'a numeric secret value is masked by the pair rule only and never registered as a form (D-11 numbers)' -Detail (($numeric -join ' | ') + ' ' + $numLine)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# R-DEF-2: JSON pairs for the secret-key list were not masked and Test-CutoverSecretName lacked key, auth, sig and
# signature (pool-diagnostics fields auth, privateKey and sig were written verbatim into the Baseline result JSON;
# {"password":"S"} passed through Hide-CutoverText unchanged). The name list now carries the secret-key list plus
# bearer and cookie, and a JSON "name":"value" pair of a secret name is masked in raw and -JsonText mode.
function Test-C1423_JsonSecretPairsMasked {
    $root = New-C1408Scratch
    foreach ($name in @('key', 'auth', 'sig', 'signature', 'apikey', 'api_key', 'bearer', 'cookie', 'authorization', 'credential', 'privateKey', 'password', 'token')) {
        Assert-C1408 -Cond (Test-CutoverSecretName -Name $name) -Name ('property name is secret: ' + $name)
    }
    foreach ($name in @('host', 'MaxPoolSize', 'NoResetOnClose', 'medianMs')) {
        Assert-C1408 -Cond (-not (Test-CutoverSecretName -Name $name)) -Name ('property name is plain: ' + $name)
    }
    $pairs = @(
        @{ Text = '{"password":"SENTJP1423"}'; Want = '{"password":"<redacted>"}'; Name = 'a compact "password" pair' },
        @{ Text = '{"token": "SENTJP1423"}'; Want = '{"token": "<redacted>"}'; Name = 'a "token" pair with a space after the colon' },
        @{ Text = '{"pool":{"apiKey":"SENTJP1423","MaxPoolSize":100}}'; Want = '{"pool":{"apiKey":"<redacted>","MaxPoolSize":100}}'; Name = 'an "apiKey" pair beside a plain number pair' },
        @{ Text = '{"auth":"SE\"NTJP1423","sig":"SENTJP1423","privateKey":"SENTJP1423"}'; Want = '{"auth":"<redacted>","sig":"<redacted>","privateKey":"<redacted>"}'; Name = 'auth, sig and privateKey pairs, an escaped quote inside the value' },
        @{ Text = '{"pin":1234,"secret":5678,"flag":true}'; Want = '{"pin":1234,"secret":"<redacted>","flag":true}'; Name = 'a numeric value of a secret name is masked, plain numbers kept' }
    )
    foreach ($p in $pairs) {
        $raw = Hide-CutoverText -Text $p.Text
        Assert-C1408 -Cond ($raw -eq $p.Want) -Name ('raw text: ' + $p.Name) -Detail $raw
        $json = Hide-CutoverText -Text $p.Text -JsonText
        Assert-C1408 -Cond ($json -eq $p.Want) -Name ('json text: ' + $p.Name) -Detail $json
    }
    $plain = '{"host":"h","medianMs":5000,"url":"http://fake.invalid/x"}'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $plain) -eq $plain -and (Hide-CutoverText -Text $plain -JsonText) -eq $plain) -Name 'plain JSON pairs are kept in both modes' -Detail (Hide-CutoverText -Text $plain)
    $hidden = Hide-CutoverSecrets -Object ([pscustomobject]@{ auth = 'SENTJP1423'; privateKey = 'SENTJP1423'; sig = 'SENTJP1423'; MaxPoolSize = 100 })
    Assert-C1408 -Cond (@($hidden.Keys).Count -eq 1 -and $hidden['MaxPoolSize'] -eq 100) -Name 'Hide-CutoverSecrets drops auth, privateKey and sig and keeps MaxPoolSize' -Detail (@($hidden.Keys) -join ',')
    $diag = '{"pool":{"NoResetOnClose":true,"auth":"SENTAUTH1423","privateKey":"SENTKEY1423","sig":"SENTSIG1423","nested":{"signature":"SENTSIG1423"}}}'
    $bp = Join-Path $root 'baseline.json'
    $b = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'b-result.json') -SampleCount 3 -HttpGet (New-C1408FakeHttp -DiagBody $diag) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $all = ''
    foreach ($f in @(Get-ChildItem -LiteralPath $root -Filter '*.json')) { $all += (Get-Content -LiteralPath $f.FullName -Raw) }
    Assert-C1408 -Cond ($b.ExitCode -eq 0 -and $all -notmatch 'SENTAUTH1423|SENTKEY1423|SENTSIG1423' -and $all -match '"NoResetOnClose": true') -Name 'pool-diagnostics auth, privateKey, sig and a nested signature are absent from the Baseline result JSON and the plain flag is kept' -Detail ('b=' + $b.ExitCode + ' ' + [regex]::Match($all, '.{0,60}(SENTAUTH1423|SENTKEY1423|SENTSIG1423).{0,20}').Value)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# R-DEF-3: the pair lead set was [?&;#"'\s] (and (^|[;\s]) in Test-CutoverSecretValue), so a key preceded by ( , : [ { <
# / or | printed whole and '(Password=S)' in a pool-diagnostics string reached the result JSON. The lead is now any
# character that cannot be part of a key; a key character still joins the key (access_token is one key).
function Test-C1423_PairLeadCharactersMasked {
    $root = New-C1408Scratch
    foreach ($lead in @('(', ',', ':', '[', '{', '<', '/', '|')) {
        $text = 'Host=h' + $lead + 'Password=SENTLEAD1423'
        $masked = Hide-CutoverText -Text $text
        Assert-C1408 -Cond ($masked -eq ('Host=h' + $lead + 'Password=<redacted>')) -Name ('a Password= pair led by ' + $lead + ' is masked') -Detail $masked
        $json = Hide-CutoverText -Text ('"' + $text + '"') -JsonText
        Assert-C1408 -Cond ($json -eq ('"Host=h' + $lead + 'Password=<redacted>"')) -Name ('a Password= pair led by ' + $lead + ' is masked in JSON text') -Detail $json
        Assert-C1408 -Cond (Test-CutoverSecretValue -Value $text) -Name ('a pool string with a Password= pair led by ' + $lead + ' is a secret value') -Detail $text
    }
    $paren = Hide-CutoverText -Text '(Password=SENTLEAD1423)'
    Assert-C1408 -Cond ($paren -notmatch 'SENTLEAD1423') -Name 'a parenthesised Password= pair is masked' -Detail $paren
    $keyChars = Hide-CutoverText -Text 'x=1&access_token=SENTLEAD1423&my.key=SENTLEAD1423&a-b=1'
    Assert-C1408 -Cond ($keyChars -eq 'x=1&access_token=<redacted>&my.key=<redacted>&a-b=1') -Name 'a key character before a secret word joins the key; plain keys are kept' -Detail $keyChars
    $amp = Hide-CutoverText -Text '"x=1&token=SENTLEAD1423"' -JsonText
    Assert-C1408 -Cond ($amp -eq '"x=1&token=<redacted>"') -Name 'the JSON-escaped & is still a lead' -Detail $amp
    $diag = '{"pool":{"NoResetOnClose":true,"note":"(Password=SENTPAREN1423)","list":"Host=h,Password=SENTPAREN1423"}}'
    $bp = Join-Path $root 'baseline.json'
    $b = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl 'http://fake.invalid' -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'b-result.json') -SampleCount 3 -HttpGet (New-C1408FakeHttp -DiagBody $diag) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $all = ''
    foreach ($f in @(Get-ChildItem -LiteralPath $root -Filter '*.json')) { $all += (Get-Content -LiteralPath $f.FullName -Raw) }
    Assert-C1408 -Cond ($b.ExitCode -eq 0 -and $all -notmatch 'SENTPAREN1423' -and $all -match '"NoResetOnClose": true') -Name 'a parenthesised or comma-led Password= pair in a pool string is absent from the Baseline result JSON' -Detail ('b=' + $b.ExitCode + ' ' + [regex]::Match($all, '.{0,60}SENTPAREN1423.{0,20}').Value)
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

# R-DEF-4 (low): the userinfo rule required a word boundary before the scheme, so '1http://u:S@h' and '_postgres://u:S@h'
# printed whole. The scheme is matched from its first letter wherever it starts.
function Test-C1423_SchemeAfterWordCharacterMasked {
    $digit = Hide-CutoverText -Text 'see 1http://u:SENTWB1423@h/x'
    Assert-C1408 -Cond ($digit -eq 'see 1http://<redacted>@h/x') -Name 'a scheme after a digit is masked to the last @' -Detail $digit
    $under = Hide-CutoverText -Text '_postgres://u:SENTWB1423@h/db'
    Assert-C1408 -Cond ($under -eq '_postgres://<redacted>@h/db') -Name 'a scheme after an underscore is masked' -Detail $under
    $json = Hide-CutoverText -Text '{"note":"x1http://u:SENTWB1423@h/x"}' -JsonText
    Assert-C1408 -Cond ($json -eq '{"note":"x1http://<redacted>@h/x"}') -Name 'a scheme after a word character inside JSON text is masked' -Detail $json
    Assert-C1408 -Cond (Test-CutoverSecretValue -Value '1http://u:SENTWB1423@h') -Name 'a pool string whose scheme follows a digit is a secret value'
    $p = Invoke-PostgresCutoverMonitor -Mode Plan -ServerBaseUrl 'x1http://u:SENTWB1423@fake.invalid' -ProjectId 'p1' -BaselinePath (Join-Path ([System.IO.Path]::GetTempPath()) 'c1423-none.json') -Now $script:FixedNow
    Assert-C1408 -Cond (@($p.Lines | Where-Object { $_ -match 'SENTWB1423' }).Count -eq 0 -and @($p.Lines | Where-Object { $_ -like 'PLAN would GET x1http://<redacted>@fake.invalid/api/agent-tasks/pipeline?projectId=p1 x*' }).Count -eq 1) -Name 'the PLAN line masks a server URL whose scheme follows a word character' -Detail (@($p.Lines | Where-Object { $_ -like 'PLAN would GET*pipeline*' }) -join ' | ')
}

# R-DEF-5 (adequacy): the decode-bound mutant (three rounds instead of four) survived because the post-bound rule masks a
# secret pair anyway and only over-masks a benign value encoded exactly four times, which nothing pinned. A benign value
# encoded four times is KEPT and one encoded five times is masked, so the bound is pinned from both sides.
function Test-C1423_DecodeBoundPinnedFromBothSides {
    $levels = @()
    $v = '/home?tab=1'
    for ($i = 1; $i -le 5; $i++) { $v = [uri]::EscapeDataString($v); $levels += $v }
    $four = 'http://fake.invalid/p?projectId=p1&redirect=' + $levels[3] + '&z=1'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $four) -eq $four) -Name 'a benign value encoded four times is kept (the bound is at least four)' -Detail (Hide-CutoverText -Text $four)
    Assert-C1408 -Cond (-not (Test-CutoverEncodedSecret -Value $levels[3])) -Name 'Test-CutoverEncodedSecret keeps a benign value encoded four times'
    $five = 'http://fake.invalid/p?projectId=p1&redirect=' + $levels[4] + '&z=1'
    $masked = Hide-CutoverText -Text $five
    Assert-C1408 -Cond ($masked -eq 'http://fake.invalid/p?projectId=p1&redirect=<redacted>&z=1') -Name 'a benign value encoded five times is masked whole (the bound is at most four) and the neighbours kept' -Detail $masked
    Assert-C1408 -Cond (Test-CutoverEncodedSecret -Value $levels[4]) -Name 'Test-CutoverEncodedSecret masks a value still encoded after four rounds'
}

# A-1 (advisory): userinfo without a password registered the bare username as a known form and replaced it everywhere:
# with -ServerBaseUrl 'http://0@h' the Watch line 'medianMs=5000' became '5<redacted><redacted><redacted>' and the D-11
# REASON lost its numbers. A form is registered only when it is at least four characters and not only digits and
# separators; the userinfo rule still masks the username inside its URL.
function Test-C1423_BareUsernameKeepsNumbersReadable {
    $root = New-C1408Scratch
    $server = 'http://0@fake.invalid'
    $bp = Join-Path $root 'baseline.json'
    $b = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl $server -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'b-result.json') -SampleCount 20 -HttpGet (New-C1408FakeHttp -PipelineMs (Get-C1408Twenty -Median 100)) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    $w = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl $server -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $root 'w-result.json') -SampleCount 20 -HttpGet (New-C1408FakeHttp -PipelineMs (Get-C1408Twenty -Median 5000)) -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
    Assert-C1408 -Cond ($b.ExitCode -eq 0 -and $w.ExitCode -eq 2) -Name 'a numeric bare username still measures and decides ROLLBACK' -Detail ('b=' + $b.ExitCode + ' w=' + $w.ExitCode + ' ' + (@($w.Lines) -join ' | '))
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -eq 'REASON median 5000 ms is above the 1000 ms floor and above 3x the baseline median 100 ms (300 ms)' }).Count -eq 1) -Name 'the D-11 rollback reason keeps its numbers beside a numeric bare username' -Detail (@($w.Lines | Where-Object { $_ -like 'REASON*' }) -join ' | ')
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -like 'CUTOVER-MONITOR mode=Watch verdict=ROLLBACK exit=2 medianMs=5000 baselineMedianMs=100 failures=0 baselineFailures=0 result=*' }).Count -eq 1) -Name 'the verdict line keeps its numbers' -Detail (@($w.Lines | Where-Object { $_ -like 'CUTOVER-MONITOR*' }) -join ' | ')
    Assert-C1408 -Cond (@($w.Lines | Where-Object { $_ -match '^MEASURED pipeline http://<redacted>@fake\.invalid/api/agent-tasks/pipeline\?projectId=p1 medianMs=5000 ' }).Count -eq 1) -Name 'the MEASURED line masks the username inside its URL and keeps its numbers' -Detail (@($w.Lines | Where-Object { $_ -like 'MEASURED pipeline*' }) -join ' | ')
    $json = Get-Content -LiteralPath (Join-Path $root 'w-result.json') -Raw
    Assert-C1408 -Cond ($json -match '"server": "http://<redacted>@fake\.invalid"' -and $json -match '"medianMs": 5000' -and $json -match '"baselineMedianMs": 100') -Name 'the recorded json masks the username and keeps the medians' -Detail ([regex]::Match($json, '.{0,40}("server"|"medianMs").{0,40}').Value)
    $forms = @(Get-CutoverKnownForms -Urls @('http://0@h', 'http://ms@h', 'http://1234@h', 'http://user1423@h', 'http://u:SENTBU1423@h'))
    Assert-C1408 -Cond (-not ($forms -contains '0') -and -not ($forms -contains 'ms') -and -not ($forms -contains '1234') -and ($forms -contains 'user1423') -and ($forms -contains 'u:SENTBU1423')) -Name 'a bare username is a known form only when at least four characters and not numeric; a user:password form is' -Detail ($forms -join ' | ')
    $alpha = Hide-CutoverText -Text 'http://user1423@fake.invalid/x'
    Assert-C1408 -Cond ($alpha -eq 'http://<redacted>@fake.invalid/x') -Name 'a bare username is masked inside its URL' -Detail $alpha
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

$cases = @(
    'C1408_RollbackAboveFloorAndRatio',
    'C1408_FloorHoldsUnderOneSecond',
    'C1408_FailureCountRollback',
    'C1408_PreWindowAbort',
    'C1408_PlanDefaultDoesNotMeasure',
    'C1408_SourceIsReadOnly',
    'C1408_ProcessExitCodeAndRollbackLine',
    'C1408_TaskListUnreadableIsNeverOk',
    'C1408_FailedLandsFromTaskDetail',
    'C1408_UrlSecretsNeverPrinted',
    'C1408_AdvisoryGuards',
    'C1408_LandRefusedWithoutRefusedOp',
    'C1408_MaskingBypassForms',
    'C1423_SlashInUserinfoMasked',
    'C1423_EncodedValueSecretsMasked',
    'C1423_DetailCapNamesTheWindow',
    'C1423_BlockedPreWindowLandThrewCountsEqually',
    'C1423_ApostropheInUserinfoMasked',
    'C1423_DoubleQuoteInUserinfoMasked',
    'C1423_SpaceInUserinfoMasked',
    'C1423_LessThanInUserinfoMasked',
    'C1423_GreaterThanInUserinfoMasked',
    'C1423_QuotedConnectionStringPasswordMasked',
    'C1423_PlusAsSpaceInEncodedValueMasked',
    'C1423_FiveTimesEncodedValueMasked',
    'C1423_VerdictLinesReadableWithMaskedUserinfo',
    'C1423_SpaceInSecretValueMasked',
    'C1423_JsonSecretPairsMasked',
    'C1423_PairLeadCharactersMasked',
    'C1423_SchemeAfterWordCharacterMasked',
    'C1423_DecodeBoundPinnedFromBothSides',
    'C1423_BareUsernameKeepsNumbersReadable'
)

if (-not (Test-Path -LiteralPath $monitor)) {
    Assert-C1408 -Cond $false -Name 'monitor script exists' -Detail $monitor
} else {
    . $monitor
    $selected = if ($Case) { @($Case) } else { $cases }
    try {
        foreach ($c in $selected) {
            $fn = Get-Command -Name ('Test-' + $c) -ErrorAction SilentlyContinue
            if (-not $fn) { Write-Error ('unknown case ' + $c); exit 2 }
            try { & $fn } catch { Assert-C1408 -Cond $false -Name ($c + ' threw') -Detail ([string]$_.Exception.Message + ' at ' + [string]$_.InvocationInfo.PositionMessage) }
        }
    } finally {
        $left = @($script:C1408ScratchRoots | Where-Object { Test-Path -LiteralPath $_ })
        Assert-C1408 -Cond ($left.Count -eq 0) -Name 'every case removed its own scratch root' -Detail ($left -join ' | ')
        foreach ($r in $left) { Remove-Item -LiteralPath $r -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host ('POSTGRES-CUTOVER-MONITOR TESTS passed=' + $script:Passed + ' failed=' + $script:Failed)
$exit = if ($script:Failed -gt 0 -or $script:Passed -eq 0) { 1 } else { 0 }
Write-Host ('POSTGRES-CUTOVER-MONITOR TESTS EXIT CODE: ' + $exit)
exit $exit
