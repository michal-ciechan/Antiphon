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
        Assert-C1408 -Cond ($exText -match $form.Sentinel -and (Hide-CutoverText -Text $exText) -notmatch $form.Sentinel) -Name ('exception text is masked: ' + $form.Name) -Detail (Hide-CutoverText -Text $exText)
        $p = Invoke-PostgresCutoverMonitor -Mode Plan -ServerBaseUrl $form.Server -PipelineUrl $form.Pipe -ProjectId 'p1' -BaselinePath $bp -Now $script:FixedNow
        $http = New-C1408FakeHttp
        $b = Invoke-PostgresCutoverMonitor -Mode Baseline -Execute -ServerBaseUrl $form.Server -PipelineUrl $form.Pipe -ProjectId 'p1' -BaselinePath (Join-Path $dir 'in-baseline.json') -ResultPath (Join-Path $dir 'in-b-result.json') -SampleCount 2 -HttpGet $http -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
        $w = Invoke-PostgresCutoverMonitor -Mode Watch -Execute -ServerBaseUrl $form.Server -PipelineUrl $form.Pipe -ProjectId 'p1' -BaselinePath $bp -ResultPath (Join-Path $dir 'in-w-result.json') -SampleCount 2 -HttpGet $http -TcpConnectMs (New-C1408FakeTcp) -Now $script:FixedNow
        $lines = @($p.Lines) + @($b.Lines) + @($w.Lines)
        Assert-C1408 -Cond ($b.ExitCode -eq 0 -and $w.ExitCode -eq 0) -Name ('the request still routes and measures: ' + $form.Name) -Detail ('b=' + $b.ExitCode + ' w=' + $w.ExitCode + ' ' + ($lines -join ' | '))
        Assert-C1408 -Cond (@($lines | Where-Object { $_ -match $form.Sentinel }).Count -eq 0) -Name ('no sentinel in Plan, Baseline or Watch lines: ' + $form.Name) -Detail (@($lines | Where-Object { $_ -match $form.Sentinel }) -join ' | ')
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
    $plain = 'PLAN would GET http://fake.invalid/health and mail ops@fake.invalid'
    Assert-C1408 -Cond ((Hide-CutoverText -Text $plain) -eq $plain) -Name 'a URL followed by whitespace and a later @ is left intact' -Detail (Hide-CutoverText -Text $plain)
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
    'C1423_BlockedPreWindowLandThrewCountsEqually'
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
