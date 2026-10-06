# CARD-0727 D-17. Run from the desktop after Review, never on server2.
# The operator token is read from the same owner-only file as runner-slots.ps1.
# This script requires PowerShell 7.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][switch]$Rolling,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Sha,
    [ValidateSet('all', 'deploy-temp', 'drain-old', 'redeploy-old', 'drain-temp', 'retire-temp', 'check-census', 'check-host-jq', 'provision-host-jq')]
    [string]$Phase = 'all',
    [string]$SavedDonor = '',
    [string]$ProjectId,
    [ValidateRange(1, 10080)][int]$WaitIdleMinutes = 480,
    [switch]$DryRun,
    [ValidatePattern('^c1008[0-9a-f]{32}$')][ValidateNotNullOrEmpty()][string]$ResumeRecycle
)

$ErrorActionPreference = 'Stop'
# Keep the requested id separate from phase-local $projectId (PowerShell names
# are case-insensitive). It is an entry scope, never a project update.
$script:recycleProjectIdWasProvided = $PSBoundParameters.ContainsKey('ProjectId')
$script:requestedRecycleProjectId = $ProjectId
if ($PSBoundParameters.ContainsKey('ResumeRecycle') -and $ResumeRecycle -cnotmatch '^c1008[0-9a-f]{32}$') { throw 'RecycleContextInvalid' }
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required' }
if (($DryRun -or $ResumeRecycle) -and $Phase -notin @('redeploy-old', 'retire-temp')) { throw 'RecycleContextInvalid' }
if ($DryRun -and $ResumeRecycle) { throw 'RecycleContextInvalid' }
. (Join-Path $PSScriptRoot 'lib/runner-operator-token.ps1')
. (Join-Path $PSScriptRoot 'c590-real.ps1')

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')
$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}
# Resolve before a case runs. Never print this value, headers, or an exception request object.
try { if ($Phase -ne 'check-census') { $headers['X-Antiphon-Operator-Token'] = Get-RunnerOperatorToken } }
catch { [Console]::Error.WriteLine('OperatorTokenMissing: the owner-only token file is absent or empty.'); exit 2 }

$Sha = $Sha.ToLowerInvariant()
if ($SavedDonor -and ($SavedDonor -cnotmatch '^/[A-Za-z0-9._/-]{1,500}$' -or
        $SavedDonor.Contains('..') -or $SavedDonor.Contains('//'))) { throw 'CacheSavedDonorPathInvalid' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$runId = 'c727' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$evidenceRoot = Join-Path $repoRoot ('.antiphon/rolling-server2/' + $runId)
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

function Invoke-HostJq {
    param([ValidateSet('check', 'provision')][string]$Mode, [string]$ExecutingPhase)
    $hostJqDestination = '/usr/local/bin/jq'
    if ($Mode -eq 'provision') {
        # No worktree override: privileged provisioning uses the clean canonical source.
        try {
            $top = & git -C $repoRoot rev-parse --show-toplevel 2>$null
            if ($LASTEXITCODE -ne 0) { throw 'git' }
            $gitDir = & git -C $repoRoot rev-parse --path-format=absolute --git-dir 2>$null
            if ($LASTEXITCODE -ne 0) { throw 'git' }
            $commonDir = & git -C $repoRoot rev-parse --path-format=absolute --git-common-dir 2>$null
            if ($LASTEXITCODE -ne 0) { throw 'git' }
            if ([IO.Path]::GetFullPath([string]$top) -ne [IO.Path]::GetFullPath($repoRoot) -or
                [string]$gitDir -ne [string]$commonDir) { throw 'git' }
            $head = & git -C $repoRoot rev-parse HEAD 2>$null
            if ($LASTEXITCODE -ne 0 -or [string]$head -cne $Sha) { throw 'git' }
            $dirt = & git -C $repoRoot status --porcelain --untracked-files=all 2>$null
            if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace(($dirt -join "`n"))) { throw 'git' }
        } catch { throw 'HostJqCanonicalSourceRequired' }
    }
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $fixture = $env:C727_TEST_VERIFY_STUB -and $env:C727_TEST_STATE
    $tokens = @()
    if ($fixture) {
        # Use the existing offline verifier boundary; its proof still passes the
        # same parser and durable receipt admission as the real SSH child.
        $manifestPath = Join-Path $evidenceRoot ("host-jq-$ExecutingPhase.manifest.json")
        @{ sourceSha=$Sha; runId=$runId; selectedPhase=$Phase; phase=$ExecutingPhase; mode=$Mode; evidenceRoot=$evidenceRoot } |
            ConvertTo-Json -Compress | Set-Content -LiteralPath $manifestPath -Encoding ascii
    } else {
        $psi.FileName = 'ssh'
        $tokens = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', 'mc@server2', 'bash', '-s', '--', $Mode)
    }
    foreach ($token in $tokens) {
        [void]$psi.ArgumentList.Add($token)
    }
    $proc = [Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    $stdout = $null; $stderr = $null; $inputTask = $null
    $deadlineMs = if ($Mode -eq 'check') { 30000 } else { 180000 }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($fixture) {
            # Match the existing in-process HTTP fixture convention. Synthetic
            # prerequisite results are fresh per entry; real SSH below remains
            # bounded and reaped, as exercised by the private SSH fixture.
            $result = @(& $env:C727_TEST_VERIFY_STUB -Case 'host-jq-prerequisite' -Manifest $manifestPath 2>&1)
            $hostJqExit = $LASTEXITCODE
            $hostJqStdout = (@($result | Where-Object { $_ -isnot [Management.Automation.ErrorRecord] }) -join "`n")
            $hostJqStderr = (@($result | Where-Object { $_ -is [Management.Automation.ErrorRecord] } | ForEach-Object { $_.Exception.Message }) -join "`n")
        } else {
            try {
                [void]$proc.Start()
                $stdout = $proc.StandardOutput.ReadToEndAsync()
                $stderr = $proc.StandardError.ReadToEndAsync()
                if (-not $fixture) {
                    $helper = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'server2-host-jq.sh')).Replace("`r`n", "`n")
                    $inputTask = $proc.StandardInput.WriteAsync($helper)
                    if (-not $inputTask.Wait([Math]::Max(1, $deadlineMs - [int]$timer.ElapsedMilliseconds))) { throw 'timeout' }
                    $inputTask.GetAwaiter().GetResult()
                }
                $proc.StandardInput.Close()
                if (-not $proc.WaitForExit([Math]::Max(1, $deadlineMs - [int]$timer.ElapsedMilliseconds))) { throw 'timeout' }
                if (-not [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout, $stderr)).Wait([Math]::Max(1, $deadlineMs - [int]$timer.ElapsedMilliseconds))) { throw 'timeout' }
            } catch {
                if ($timer.ElapsedMilliseconds -ge $deadlineMs) { throw 'HostJqTransportTimeout' }
                throw 'HostJqTransportUnavailable'
            }
            $hostJqStdout = $stdout.GetAwaiter().GetResult()
            $hostJqStderr = $stderr.GetAwaiter().GetResult()
            $hostJqExit = $proc.ExitCode
        }
        if ($hostJqExit -ne 0) {
            # Retain the found path for this specific refusal; never copy raw remote diagnostics.
            if ($hostJqExit -eq 2) {
                $pathRefusal = $null
                try {
                    $document = [Text.Json.JsonDocument]::Parse($hostJqStdout)
                    try {
                        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'shape' }
                        $names = @($document.RootElement.EnumerateObject() | ForEach-Object Name)
                        $required = @('schema','lane','mode','lookupPath','path','reason')
                        if ($names.Count -ne 6 -or @($names | Select-Object -Unique).Count -ne 6 -or
                            @($required | Where-Object { $_ -cnotin $names }).Count -ne 0) { throw 'shape' }
                        foreach ($key in @('lane','mode','lookupPath','path','reason')) {
                            if ($document.RootElement.GetProperty($key).ValueKind -ne [Text.Json.JsonValueKind]::String) { throw 'shape' }
                        }
                        $number = 0L
                        if (-not $document.RootElement.GetProperty('schema').TryGetInt64([ref]$number) -or $number -ne 1) { throw 'shape' }
                        $candidate = $hostJqStdout | ConvertFrom-Json
                        if ($candidate.lane -cne 'host' -or $candidate.mode -cne $Mode -or $candidate.reason -cne 'HostJqPathUnapproved' -or
                            $candidate.lookupPath -cnotmatch '^/[^\x00-\x1f]{1,4095}$' -or $candidate.path -cnotmatch '^/[^\x00-\x1f]{1,4095}$' -or
                            $candidate.path -ceq $hostJqDestination) { throw 'shape' }
                        $pathRefusal = $candidate
                    } finally { $document.Dispose() }
                } catch { $pathRefusal = $null }
                if ($null -ne $pathRefusal) {
                    $observation = [ordered]@{ schema=1; qualified=$false; lane='host'; mode=$Mode; reason='HostJqPathUnapproved';
                        lookupPath=$pathRefusal.lookupPath; path=$pathRefusal.path; sourceSha=$Sha; runId=$runId;
                        selectedPhase=$Phase; phase=$ExecutingPhase; observedAtUtc=[DateTime]::UtcNow.ToString('o'); sshExit=$hostJqExit }
                    try {
                        $file = [IO.File]::Open((Join-Path $evidenceRoot ("host-jq-$ExecutingPhase-refused.json")), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                        try {
                            $bytes = [Text.Encoding]::UTF8.GetBytes(($observation | ConvertTo-Json -Compress))
                            $file.Write($bytes, 0, $bytes.Length); $file.Flush($true)
                        } finally { $file.Dispose() }
                    } catch { throw 'HostJqReceiptUnavailable' }
                    throw 'HostJqPathUnapproved'
                }
            }
            # Only fixed helper diagnoses cross this boundary, never raw remote stderr.
            $diagnosis = $hostJqStderr.Trim()
            if ($diagnosis -cin @('HostJqMissing', 'HostJqInvalid', 'HostJqWrongLane', 'HostJqLaneUnavailable')) { throw $diagnosis }
            throw 'HostJqRemoteRefused'
        }
        try {
            $raw = $hostJqStdout
            $document = [Text.Json.JsonDocument]::Parse($raw)
            try {
                if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'shape' }
                $names = @($document.RootElement.EnumerateObject() | ForEach-Object Name)
                $required = @('schema','lane','mode','lookupPath','path','version','digest','uid','gid','permissions','trueExit','falseExit','installed','outcome')
                if ($names.Count -ne $required.Count -or @($names | Select-Object -Unique).Count -ne $required.Count -or
                    @($required | Where-Object { $_ -cnotin $names }).Count -ne 0) { throw 'shape' }
                foreach ($key in @('schema','uid','gid','trueExit','falseExit')) {
                    $value = $document.RootElement.GetProperty($key)
                    $number = 0L
                    if ($value.ValueKind -ne [Text.Json.JsonValueKind]::Number -or -not $value.TryGetInt64([ref]$number)) { throw 'shape' }
                }
                foreach ($key in @('lane','mode','lookupPath','path','version','digest','permissions','outcome')) {
                    if ($document.RootElement.GetProperty($key).ValueKind -ne [Text.Json.JsonValueKind]::String) { throw 'shape' }
                }
                if ($document.RootElement.GetProperty('installed').ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False)) { throw 'shape' }
            } finally { $document.Dispose() }
            $proof = $raw | ConvertFrom-Json
            if ($proof.schema -ne 1 -or $proof.lane -cne 'host' -or $proof.mode -cne $Mode -or
                $proof.lookupPath -cnotmatch '^/[^\r\n]+$' -or
                $proof.path -cne $hostJqDestination -or
                $proof.path -cnotmatch '^/[^\r\n]+$' -or [string]::IsNullOrWhiteSpace($proof.version) -or
                $proof.version -match '[\r\n]' -or $proof.digest -cnotmatch '^[0-9a-f]{64}$' -or
                $proof.uid -lt 0 -or $proof.gid -lt 0 -or $proof.permissions -cnotmatch '^[0-7]{3,4}$' -or
                $proof.trueExit -ne 0 -or $proof.falseExit -ne 1 -or
                ($proof.installed -and ($Mode -ne 'provision' -or $proof.outcome -cne 'installed')) -or
                (-not $proof.installed -and $proof.outcome -cne 'existing')) { throw 'shape' }
            if ($proof.installed -and ($proof.path -cne $hostJqDestination -or $proof.version -cne 'jq-1.7.1' -or
                $proof.digest -cne '5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5' -or
                $proof.uid -ne 0 -or $proof.gid -ne 0 -or $proof.permissions -cne '755')) { throw 'shape' }
        } catch { throw 'HostJqProofInvalid' }
        $receipt = [ordered]@{}
        foreach ($property in $proof.PSObject.Properties) { $receipt[$property.Name] = $property.Value }
        $receipt.sourceSha = $Sha
        $receipt.runId = $runId
        $receipt.selectedPhase = $Phase
        $receipt.phase = $ExecutingPhase
        $receipt.mode = $Mode
        $receipt.observedAtUtc = [DateTime]::UtcNow.ToString('o')
        $receipt.sshExit = $hostJqExit
        try {
            # CreateNew refuses stale/blocked paths; successful close precedes admission/banner.
            $file = [IO.File]::Open((Join-Path $evidenceRoot ("host-jq-$ExecutingPhase.json")), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes(($receipt | ConvertTo-Json -Compress))
                $file.Write($bytes, 0, $bytes.Length)
                $file.Flush($true)
            } finally { $file.Dispose() }
        } catch { throw 'HostJqReceiptUnavailable' }
    } finally {
        # Custody covers timeout, broken stdin and inherited pipe handles, including root exit.
        try {
            if (-not $fixture -and $proc.Id -gt 0) {
                if (-not $proc.HasExited) { $proc.Kill($true) }
                if (-not $proc.WaitForExit(5000)) { throw 'HostJqTransportCustodyUnknown' }
                $tasks = @($stdout, $stderr, $inputTask) | Where-Object { $null -ne $_ }
                if ($tasks.Count -gt 0 -and -not [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]$tasks).Wait(5000)) { throw 'HostJqTransportCustodyUnknown' }
            }
        } catch { throw 'HostJqTransportCustodyUnknown' }
        finally { $proc.Dispose() }
    }
}

function Enter-RolloutAdmissionLock {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($env:C727_TEST_VERIFY_STUB) {
        $psi.FileName = 'pwsh'
        $tokens = @('-NoProfile', '-File', $env:C727_TEST_VERIFY_STUB, '-Case', 'rollout-lock')
    } else {
        $psi.FileName = 'ssh'
        $tokens = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', 'mc@server2',
            'set -e; mkdir -p /home/mc/antiphon-server2/locks; chmod 700 /home/mc/antiphon-server2/locks; exec 8>/home/mc/antiphon-server2/locks/rollout.lock; flock -w 60 8; printf "C1008_LOCKED\n"; read -r release')
    }
    foreach ($token in $tokens) { [void]$psi.ArgumentList.Add($token) }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderr = $proc.StandardError.ReadToEndAsync()
    try {
        $ready = $proc.StandardOutput.ReadLineAsync()
        if (-not $ready.Wait(70000) -or $ready.Result -cne 'C1008_LOCKED' -or $proc.HasExited) { throw 'RecycleRolloutBusy' }
        return $proc
    } catch {
        if (-not $proc.HasExited) { $proc.Kill($true); $proc.WaitForExit() }
        $proc.Dispose()
        throw 'RecycleRolloutBusy'
    }
}

function Invoke-RunnerRequest {
    param([string]$Method, [string]$RunnerId, [string]$Suffix = '', $Body = $null)
    $lock = $null
    try {
        if ($Method -eq 'POST') { $lock = Enter-RolloutAdmissionLock }
        return Invoke-RunnerRequestCore -Method $Method -RunnerId $RunnerId -Suffix $Suffix -Body $Body
    } finally {
        if ($null -ne $lock) {
            try {
                $lock.StandardInput.WriteLine('release')
                $lock.StandardInput.Close()
                if (-not $lock.WaitForExit(5000)) { $lock.Kill($true); $lock.WaitForExit() }
            } finally { $lock.Dispose() }
        }
    }
}

function Invoke-RunnerRequestCore {
    param([string]$Method, [string]$RunnerId, [string]$Suffix = '', $Body = $null)
    $path = '/api/session-runners/' + [uri]::EscapeDataString($RunnerId) + $Suffix
    if ($env:C727_TEST_HTTP_STUB) {
        $bodyJson = if ($null -eq $Body) { '' } else { $Body | ConvertTo-Json -Compress }
        $raw = & $env:C727_TEST_HTTP_STUB -Method $Method -RunnerId $RunnerId -Suffix $Suffix -BodyJson $bodyJson
        if ($LASTEXITCODE -ne 0) { throw "RunnerApiUnavailable $RunnerId$Suffix" }
        if ([string]$raw -eq '__404__') { return $null }
        if ([string]$raw -eq '__503__') { throw "RunnerApiFailed $RunnerId$Suffix HTTP 503" }
        if ([string]::IsNullOrWhiteSpace([string]$raw)) { return $null }
        return ([string]$raw | ConvertFrom-Json)
    }
    $args = @{ Method = $Method; Uri = ($api + $path); Headers = $headers; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) {
        $args['ContentType'] = 'application/json'
        $args['Body'] = ($Body | ConvertTo-Json -Compress)
    }
    try { $response = Invoke-WebRequest @args }
    catch { throw "RunnerApiUnavailable $RunnerId$Suffix" }
    if ([int]$response.StatusCode -eq 404 -and $Method -eq 'GET') { return $null }
    if ([int]$response.StatusCode -lt 200 -or [int]$response.StatusCode -ge 300) {
        throw "RunnerApiFailed $RunnerId$Suffix HTTP $([int]$response.StatusCode)"
    }
    if ([string]::IsNullOrWhiteSpace($response.Content)) { return $null }
    return ($response.Content | ConvertFrom-Json)
}

# Same-SHA health cannot hide an unfinished operation or a lost receipt copy.
function Assert-NoIncompleteRecycle {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($env:C727_TEST_VERIFY_STUB) {
        $psi.FileName = 'pwsh'
        $tokens = @('-NoProfile', '-File', $env:C727_TEST_VERIFY_STUB, '-Case', 'recycle-discover')
    } else {
        $psi.FileName = 'ssh'
        $command = 'set -e; command -v jq >/dev/null; for receipt in /home/mc/antiphon-server2/recycle/c1008*.json; do test -e "$receipt" || continue; test -f "$receipt" && test ! -L "$receipt"; jq -er --arg sha ' + $Sha +
            ' ''if .schema!=1 then error("schema") elif .sourceSha==$sha and .project=="antiphon-runner" and .phase!="completed" then .operationId else empty end'' "$receipt" || test "$?" = 4; done'
        $tokens = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', 'mc@server2', $command)
    }
    foreach ($token in $tokens) { [void]$psi.ArgumentList.Add($token) }
    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        $stdout = $proc.StandardOutput.ReadToEndAsync()
        $stderr = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit(30000)) { $proc.Kill($true); $proc.WaitForExit(); throw 'RecycleReceiptUnavailable' }
        if ($proc.ExitCode -ne 0) { throw 'RecycleReceiptUnavailable' }
        if (-not [string]::IsNullOrWhiteSpace($stdout.GetAwaiter().GetResult())) { throw 'RecycleResumeRequired' }
    } finally { $proc.Dispose() }
}

function Get-RunnerStatus {
    param([string]$RunnerId)
    return Invoke-RunnerRequest -Method GET -RunnerId $RunnerId -Suffix '/status'
}

function Assert-ZeroCounters {
    param($Status, [string]$RunnerId)
    if ($null -eq $Status) { throw "RunnerStatusMissing $RunnerId" }
    foreach ($name in @('sessions', 'runnerSessions', 'queuedTasks')) {
        if ($Status.PSObject.Properties.Name -notcontains $name -or $null -eq $Status.$name) {
            throw "RunnerCounterUnknown $RunnerId $name"
        }
        $value = $Status.$name
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -lt 0) {
            throw "RunnerCounterUnknown $RunnerId $name"
        }
        if ($value -ne 0) { throw "RunnerBusy $RunnerId $name" }
    }
}

# Independent wrapper validation. This function is also exercised with raw JSON,
# so malformed booleans cannot be hidden by the PowerShell switch binder.
function Assert-RecycleContext {
    param($Context)
    if ($null -eq $Context) { throw 'RecycleContextInvalid' }
    $keys = @($Context.PSObject.Properties.Name)
    $expected = @('version', 'project', 'operationId', 'dryRun', 'resume', 'projectId')
    if ($keys -ccontains 'cleanupOperationId') {
        if ($Context.project -cne 'antiphon-runner-temp' -or $Context.resume -ne $false -or
            $Context.cleanupOperationId -isnot [string] -or $Context.cleanupOperationId -cnotmatch '^c994[0-9a-f]{32}$') { throw 'RecycleContextInvalid' }
        $expected += 'cleanupOperationId'
    }
    if ($keys.Count -ne $expected.Count -or @($keys | Where-Object { $_ -cnotin $expected }).Count -ne 0) { throw 'RecycleContextInvalid' }
    if ($Context.version -ne 1 -or ($Context.version -isnot [long] -and $Context.version -isnot [int]) -or
        $Context.project -cnotin @('antiphon-runner', 'antiphon-runner-temp') -or
        $Context.operationId -cnotmatch '^c1008[0-9a-f]{32}$' -or
        $Context.dryRun -isnot [bool] -or $Context.resume -isnot [bool] -or
        $Context.projectId -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or
        ($Context.dryRun -and $Context.resume)) { throw 'RecycleContextInvalid' }
}

function Start-RecycleReceipt {
    param([string]$ExecutingPhase, [string]$RunnerId)
    $script:recycleReceiptPath = Join-Path $evidenceRoot ("census-$ExecutingPhase-$RunnerId.json")
    $script:recycleReceipt = [ordered]@{ schema=1; phase=$ExecutingPhase; runnerId=$RunnerId;
        project=$null; reads=[System.Collections.Generic.List[object]]::new(); tasks=@() }
    $script:recycleReceiptTasks = @{}
    $script:resolvedRecycleProjectId = $null
    Write-RecycleReceipt
}

function Write-RecycleReceipt {
    # Persist approved fields only, including a partial census on refusal. Never
    # retain response bodies, exception messages, credentials or task prose.
    if ($null -eq $script:recycleReceipt) { return }
    $script:recycleReceipt.tasks = @($script:recycleReceiptTasks.Keys | Sort-Object | ForEach-Object { $script:recycleReceiptTasks[$_] })
    try {
        $script:recycleReceipt | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $script:recycleReceiptPath -Encoding utf8
    } catch { throw 'RecycleTaskCensusUnknown cause=Transport field=receipt' }
}

function Stop-RecycleMalformed {
    param([string]$Field, [string]$Path, [string]$ReasonCode = 'RecycleTaskCensusUnknown')
    if ($null -ne $script:recycleReceipt) {
        $read = @($script:recycleReceipt.reads | Where-Object path -CEQ $Path | Select-Object -Last 1)
        if ($read.Count -eq 1) { $read[0].outcome = 'Malformed'; $read[0]['field'] = $Field }
    }
    throw "$ReasonCode cause=Malformed field=$Field path=$Path"
}

function Test-RecycleGuid {
    param($Value)
    return ($Value -is [string] -and $Value -cmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
}

function Test-RecycleTimestamp {
    param($Value)
    if ($null -eq $Value -or $Value -is [datetime] -or $Value -is [datetimeoffset]) { return $true }
    $parsed = [datetimeoffset]::MinValue
    return ($Value -is [string] -and $Value -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$' -and
        [datetimeoffset]::TryParse($Value, [ref]$parsed))
}

function Invoke-RecycleRead {
    param([string]$Path)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $read = [ordered]@{ path=$Path; elapsedMs=0L; outcome='ok'; items=$null; excludedTotal=$null }
    if ($null -ne $script:recycleReceipt) { $script:recycleReceipt.reads.Add($read) }
    $cause = $null
    try {
        if ($env:C727_TEST_HTTP_STUB) {
            # Script-scope calls share Console.Error; preserve the subprocess
            # boundary's stderr suppression for fixtures using WriteLine directly.
            $stderr = [Console]::Error
            try {
                [Console]::SetError([System.IO.TextWriter]::Null)
                $raw = & $env:C727_TEST_HTTP_STUB -Method GET -Path $Path 2>$null
            } finally { [Console]::SetError($stderr) }
            if ($LASTEXITCODE -ne 0) { $cause = 'Transport'; throw 'read' }
            $raw = [string]$raw
            if ($raw -ceq '__TIMEOUT__') { $cause = 'Timeout'; throw 'read' }
            if ($raw -cmatch '^__(\d{3})__$') { $read['status'] = [int]$Matches[1]; $cause = 'Http'; throw 'read' }
        } else {
            # Read raw content so an empty success is distinguishable from malformed
            # JSON; non-success content is never parsed or persisted.
            $response = Invoke-WebRequest -Method GET -Uri ($api + $Path) -Headers $headers -TimeoutSec 60 -SkipHttpErrorCheck
            if ([int]$response.StatusCode -lt 200 -or [int]$response.StatusCode -ge 300) {
                $read['status'] = [int]$response.StatusCode; $cause = 'Http'; throw 'read'
            }
            $raw = [string]$response.Content
        }
        if ([string]::IsNullOrWhiteSpace($raw)) { $cause = 'Empty'; throw 'read' }
        try { $value = ConvertFrom-Json -InputObject $raw -NoEnumerate }
        catch { $cause = 'Malformed'; $read['field'] = 'body'; throw 'read' }
        if ($value -is [array]) { $read.items = $value.Count }
        elseif ($value.items -is [array]) { $read.items = $value.items.Count }
        if ($value.excluded.total -is [int] -or $value.excluded.total -is [long]) { $read.excludedTotal = $value.excluded.total }
        return ,$value
    } catch {
        if ($null -eq $cause) {
            $cause = 'Transport'
            for ($errorValue = $_.Exception; $null -ne $errorValue; $errorValue = $errorValue.InnerException) {
                if ($errorValue -is [OperationCanceledException] -or $errorValue -is [TimeoutException] -or
                    ($errorValue -is [Net.WebException] -and $errorValue.Status -eq [Net.WebExceptionStatus]::Timeout)) { $cause = 'Timeout'; break }
            }
        }
        $read.outcome = $cause
        $suffix = if ($cause -eq 'Http') { ' status=' + $read.status } elseif ($cause -eq 'Malformed') { ' field=body' } else { '' }
        throw "RecycleTaskCensusUnknown cause=$cause$suffix path=$Path elapsedMs=$($timer.ElapsedMilliseconds)"
    } finally {
        $timer.Stop(); $read.elapsedMs = $timer.ElapsedMilliseconds
        Write-RecycleReceipt
    }
}

function ConvertTo-RecycleRepositoryUrl {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $value = $Value.Trim()
    if ($value -match '^git@([^:]+):(.+)$') { $value = 'https://' + $Matches[1] + '/' + $Matches[2] }
    $uri = $null
    if (-not [uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -cne 'https' -or -not $uri.IsDefaultPort -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) { return '' }
    # GitHub repository identity is case-insensitive, including its owner/name.
    $repository = $uri.AbsolutePath.TrimEnd('/') -replace '\.git$', ''
    return 'https://' + $uri.Host.ToLowerInvariant() + $repository.ToLowerInvariant()
}

function ConvertTo-RecycleRepositoryPath {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    if ($IsWindows) { $Value = $Value.Replace('/', '\') }
    $full = [IO.Path]::GetFullPath($Value)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) { $full = $full.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) }
    if ($IsWindows) { return $full.ToLowerInvariant() }
    return $full
}

function Get-RecycleProjectId {
    if ($script:resolvedRecycleProjectId) { return $script:resolvedRecycleProjectId }
    try {
        $explicit = $script:recycleProjectIdWasProvided
        if ($explicit) {
            if (-not (Test-RecycleGuid $script:requestedRecycleProjectId)) { throw 'RecycleProjectUnresolved cause=InvalidId' }
            $path = '/api/projects/' + $script:requestedRecycleProjectId
            try { $projects = @(Invoke-RecycleRead -Path $path) }
            catch {
                if ($_.Exception.Message -cmatch '^RecycleTaskCensusUnknown cause=Http status=404 ') {
                    throw "RecycleProjectUnresolved cause=NotFound id=$script:requestedRecycleProjectId"
                }
                throw
            }
        } else {
            $path = '/api/projects?includeArchived=true'
            $projects = Invoke-RecycleRead -Path $path
            if ($projects -isnot [array]) { Stop-RecycleMalformed 'projects' $path 'RecycleProjectUnresolved' }
        }
        $projectMatches = @{}
        $urlMatches = [System.Collections.Generic.HashSet[string]]::new()
        $pathMatches = [System.Collections.Generic.HashSet[string]]::new()
        $checkout = ConvertTo-RecycleRepositoryPath $repoRoot
        foreach ($project in $projects) {
            foreach ($key in @('id','archivedAt','gitRepositoryUrl','localRepositoryPath')) {
                if ($project.PSObject.Properties.Name -cnotcontains $key) { Stop-RecycleMalformed $key $path 'RecycleProjectUnresolved' }
            }
            if (-not (Test-RecycleGuid $project.id)) { Stop-RecycleMalformed 'id' $path 'RecycleProjectUnresolved' }
            if ($explicit -and $project.id -cne $script:requestedRecycleProjectId) { Stop-RecycleMalformed 'id' $path 'RecycleProjectUnresolved' }
            if (-not (Test-RecycleTimestamp $project.archivedAt)) { Stop-RecycleMalformed 'archivedAt' $path 'RecycleProjectUnresolved' }
            foreach ($key in @('gitRepositoryUrl','localRepositoryPath')) {
                if ($null -ne $project.$key -and $project.$key -isnot [string]) { Stop-RecycleMalformed $key $path 'RecycleProjectUnresolved' }
            }
            if ($explicit -and $project.name -isnot [string]) { Stop-RecycleMalformed 'name' $path 'RecycleProjectUnresolved' }
            try { $localPath = ConvertTo-RecycleRepositoryPath $project.localRepositoryPath }
            catch { Stop-RecycleMalformed 'localRepositoryPath' $path 'RecycleProjectUnresolved' }
            $urlMatch = (ConvertTo-RecycleRepositoryUrl $project.gitRepositoryUrl) -ceq 'https://github.com/michal-ciechan/antiphon'
            $pathMatch = $localPath -cne '' -and $localPath -ceq $checkout
            if ($urlMatch) { [void]$urlMatches.Add($project.id) }
            if ($pathMatch) { [void]$pathMatches.Add($project.id) }
            if ($explicit -or $urlMatch -or $pathMatch) {
                if ($projectMatches.ContainsKey($project.id) -and
                    ($projectMatches[$project.id] | ConvertTo-Json -Compress) -cne ($project | ConvertTo-Json -Compress)) {
                    Stop-RecycleMalformed 'id' $path 'RecycleProjectUnresolved'
                }
                $projectMatches[$project.id] = $project
            }
        }
        if ($projectMatches.Count -eq 0) {
            throw "RecycleProjectUnresolved cause=NoMatch candidates=$($projects.Count) urlMatches=$($urlMatches.Count) pathMatches=$($pathMatches.Count)"
        }
        if ($projectMatches.Count -gt 1) { throw "RecycleProjectUnresolved cause=Ambiguous candidates=$($projects.Count) matches=$($projectMatches.Count)" }
        $selected = @($projectMatches.Values)[0]
        if ($null -ne $selected.archivedAt) { throw "RecycleProjectUnresolved cause=Archived id=$($selected.id)" }
        $resolvedBy = if ($explicit) { 'explicit' } elseif ($urlMatches.Contains($selected.id) -and $pathMatches.Contains($selected.id)) { 'both' }
            elseif ($urlMatches.Contains($selected.id)) { 'url' } else { 'path' }
        if ($null -ne $script:recycleReceipt) {
            $script:recycleReceipt.project = [ordered]@{ id=$selected.id; resolvedBy=$resolvedBy }
            if ($explicit) { $script:recycleReceipt.project['name'] = $selected.name }
        }
        $script:resolvedRecycleProjectId = [string]$selected.id
        return $script:resolvedRecycleProjectId
    } finally { Write-RecycleReceipt }
}

function Assert-RecycleTaskCensus {
    param([string]$RunnerId, [string]$ProjectId, [switch]$Report)
    # Open work and pending lands each form a complete, server-filtered closure.
    # No time window: an old terminal task can still have a pending land.
    # Compare both closures twice; filtered excluded counts must reconcile independently.
    try {
    $previous = $null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    for ($pass = 0; $pass -lt 2; $pass++) {
        $closures = [ordered]@{}
        $pendingLands = [System.Collections.Generic.HashSet[string]]::new()
        $allScopes = [System.Collections.Generic.HashSet[string]]::new()
        $openCount = 0; $boundCount = 0
        foreach ($kind in @('open','land')) {
        $pending = [System.Collections.Generic.Queue[string]]::new()
        $pending.Enqueue($ProjectId)
        $scopes = [System.Collections.Generic.HashSet[string]]::new()
        $rows = @{}
        $observations = @{}
        $scopeCounts = @{}
        $landEvidence = @{}
        while ($pending.Count -gt 0) {
            $scope = $pending.Dequeue()
            if (-not $scopes.Add($scope)) { continue }
            [void]$allScopes.Add($scope)
            if ($scopes.Count -gt 1000) { throw "RecycleTaskCensusUnknown cause=ScopeLimit scopes=$($scopes.Count)" }
            $filter = if ($kind -eq 'open') { '&status=Queued,Dispatched,Working,Blocked' } else { '&landPending=true' }
            $path = '/api/agent-tasks?projectId=' + $scope + '&unscoped=include&includeChecks=true' + $filter
            $envelope = Invoke-RecycleRead -Path $path
            if ($null -eq $envelope -or $envelope -isnot [pscustomobject]) { Stop-RecycleMalformed 'envelope' $path }
            if ($envelope.items -isnot [array]) { Stop-RecycleMalformed 'items' $path }
            if ($null -eq $envelope.excluded -or $envelope.excluded -isnot [pscustomobject]) { Stop-RecycleMalformed 'excluded' $path }
            if ($envelope.excluded.byProject -isnot [array]) { Stop-RecycleMalformed 'excluded.byProject' $path }
            if ($envelope.scope.projectId -isnot [string] -or $envelope.scope.projectId -cne $scope) { Stop-RecycleMalformed 'scope.projectId' $path }
            if ($envelope.scope.unscoped -isnot [string] -or $envelope.scope.unscoped -cne 'include') { Stop-RecycleMalformed 'scope.unscoped' $path }
            foreach ($key in @('total','unscoped')) {
                if (($envelope.excluded.$key -isnot [int] -and $envelope.excluded.$key -isnot [long]) -or
                    $envelope.excluded.$key -lt 0) { Stop-RecycleMalformed "excluded.$key" $path }
            }
            if ($envelope.excluded.unscoped -ne 0) { Stop-RecycleMalformed 'excluded.unscoped' $path }
            $withheldCount = 0
            $excludedScopes = [System.Collections.Generic.HashSet[string]]::new()
            $observations[$scope] = @($envelope.items | ForEach-Object id | Sort-Object) -join ','
            foreach ($excluded in $envelope.excluded.byProject) {
                $excludedId = [string]$excluded.projectId
                if (-not (Test-RecycleGuid $excluded.projectId) -or $excludedId -eq $scope -or
                    -not $excludedScopes.Add($excludedId)) { Stop-RecycleMalformed 'excluded.byProject.projectId' $path }
                if (($excluded.count -isnot [int] -and $excluded.count -isnot [long]) -or $excluded.count -le 0) { Stop-RecycleMalformed 'excluded.byProject.count' $path }
                $withheldCount += $excluded.count
                if ($scopeCounts.ContainsKey($excludedId) -and $scopeCounts[$excludedId] -ne $excluded.count) { Stop-RecycleMalformed 'excluded.byProject.count' $path }
                $scopeCounts[$excludedId] = $excluded.count
                $pending.Enqueue([string]$excluded.projectId)
                $observations[$scope] += '|' + [string]$excluded.projectId + ':' + [string]$excluded.count
            }
            if ($withheldCount -ne $envelope.excluded.total) { Stop-RecycleMalformed 'excluded.total' $path }
            foreach ($task in $envelope.items) {
                foreach ($key in @('id', 'status', 'runnerId', 'projectId', 'scopeSource', 'landRequestedAt', 'landStartedAt')) {
                    if ($task.PSObject.Properties.Name -notcontains $key) { Stop-RecycleMalformed $key $path }
                }
                $id = [string]$task.id
                if (-not (Test-RecycleGuid $task.id)) { Stop-RecycleMalformed 'id' $path }
                if ($task.status -isnot [string] -or $task.status -cnotin @('Queued','Dispatched','Working','Blocked','Succeeded','Failed','Canceled')) { Stop-RecycleMalformed 'status' $path }
                if ($null -ne $task.runnerId -and $task.runnerId -isnot [string]) { Stop-RecycleMalformed 'runnerId' $path }
                if ($task.scopeSource -isnot [string] -or $task.scopeSource -cnotin @('Task','Card','None')) { Stop-RecycleMalformed 'scopeSource' $path }
                if (($null -ne $task.projectId -and -not (Test-RecycleGuid $task.projectId)) -or
                    ($task.scopeSource -eq 'None' -and $null -ne $task.projectId) -or
                    ($task.scopeSource -ne 'None' -and [string]$task.projectId -cne $scope)) { Stop-RecycleMalformed 'projectId' $path }
                foreach ($key in @('landRequestedAt','landStartedAt')) {
                    if (-not (Test-RecycleTimestamp $task.$key)) { Stop-RecycleMalformed $key $path }
                }
                $reduced = [ordered]@{ id = $id; status = [string]$task.status; runnerId = $task.runnerId;
                    projectId = $task.projectId; scopeSource = $task.scopeSource;
                    landRequestedAt = $task.landRequestedAt; landStartedAt = $task.landStartedAt }
                $facts = $reduced | ConvertTo-Json -Compress
                if ($rows.ContainsKey($id) -and $rows[$id] -cne $facts) { Stop-RecycleMalformed 'id' $path }
                $rows[$id] = $facts
                if ($null -ne $script:recycleReceipt -and ($kind -eq 'open' -or -not $script:recycleReceiptTasks.ContainsKey($id))) {
                    $script:recycleReceiptTasks[$id] = $reduced
                }
                if (($kind -eq 'open' -and $task.status -cnotin @('Queued','Dispatched','Working','Blocked')) -or
                    ($kind -eq 'land' -and $null -eq $task.landRequestedAt -and $null -eq $task.landStartedAt)) {
                    if ($null -ne $script:recycleReceipt) { $script:recycleReceipt.reads[-1].outcome = 'UnfilteredRow' }
                    throw "RecycleTaskCensusUnknown cause=UnfilteredRow id=$id status=$($task.status) path=$path"
                }
                if ($null -ne $task.landRequestedAt -or $null -ne $task.landStartedAt) {
                    [void]$pendingLands.Add($id)
                    if (-not $Report) { throw "RecycleLandInFlight $id" }
                }
                if ($kind -eq 'open' -and [string]$task.runnerId -eq $RunnerId -and $task.status -cin @('Queued','Dispatched','Working','Blocked')) {
                    if (-not $Report) { throw "RecycleBoundTasks $id status=$($task.status) runner=$RunnerId" }
                }
                if ($kind -eq 'land') { continue }
                $detail = Invoke-RecycleRead -Path ('/api/agent-tasks/' + $id)
                if ($detail.PSObject.Properties.Name -notcontains 'landRequest' -or [string]$detail.summary.id -ne $id) { throw 'RecycleLandUnknown' }
                foreach ($key in @('id','status','runnerId','projectId','scopeSource','landRequestedAt','landStartedAt')) {
                    if ($detail.summary.PSObject.Properties.Name -notcontains $key -or
                        ($detail.summary.$key | ConvertTo-Json -Compress) -cne ($task.$key | ConvertTo-Json -Compress)) { throw 'RecycleLandUnknown' }
                }
                if ($null -ne $detail.landRequest) {
                    if ([string]$detail.landRequest.state -cin @('Queued','Held','Running','NeedsResolution')) {
                        [void]$pendingLands.Add($id)
                        if (-not $Report) { throw "RecycleLandInFlight $id" }
                    } elseif ([string]$detail.landRequest.state -cnotin @('Completed','Superseded','Canceled') -or
                        [string]$detail.landRequest.terminalEventId -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') { throw 'RecycleLandUnknown' }
                }
                $proof = if ($null -eq $detail.landRequest) { 'null' } else {
                    [ordered]@{ state=$detail.landRequest.state; terminalEventId=$detail.landRequest.terminalEventId } | ConvertTo-Json -Compress
                }
                if ($landEvidence.ContainsKey($id) -and $landEvidence[$id] -cne $proof) { throw 'RecycleLandUnknown' }
                $landEvidence[$id] = $proof
                if ($null -ne $script:recycleReceipt) { $reduced['landRequest'] = $proof | ConvertFrom-Json }
            }
        }
        foreach ($scopeId in $scopeCounts.Keys) {
            $actual = @($rows.Values | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object projectId -eq $scopeId).Count
            if ($actual -ne $scopeCounts[$scopeId]) { Stop-RecycleMalformed 'excluded.byProject.count' $path }
        }
        $scopeSnapshot = [ordered]@{}; $taskSnapshot = [ordered]@{}; $landSnapshot = [ordered]@{}
        foreach ($key in @($observations.Keys | Sort-Object)) { $scopeSnapshot[$key] = $observations[$key] }
        foreach ($key in @($rows.Keys | Sort-Object)) { $taskSnapshot[$key] = $rows[$key] | ConvertFrom-Json }
        foreach ($key in @($landEvidence.Keys | Sort-Object)) { $landSnapshot[$key] = $landEvidence[$key] | ConvertFrom-Json }
        $closures[$kind] = [ordered]@{ scopes=$scopeSnapshot; tasks=$taskSnapshot }
        if ($kind -eq 'open') {
            $closures[$kind]['land'] = $landSnapshot
            $openCount = $rows.Count
            $boundCount = @($taskSnapshot.Values | Where-Object runnerId -EQ $RunnerId).Count
        }
        }
        $snapshot = $closures | ConvertTo-Json -Depth 20 -Compress
        if ($null -ne $script:recycleReceipt) { $script:recycleReceipt['snapshot'] = $closures }
        if ($pass -eq 1 -and $snapshot -cne $previous) { throw 'RecycleTaskCensusUnknown cause=Unstable' }
        $previous = $snapshot
    }
    if ($Report) {
        return [pscustomobject]@{ open=$openCount; boundOpen=$boundCount; landPending=$pendingLands.Count;
            scopes=$allScopes.Count; elapsedMs=$timer.ElapsedMilliseconds }
    }
    return $previous
    } finally { Write-RecycleReceipt }
}

function Assert-RetiredTempCounters {
    param($Status)
    if ($null -eq $Status -or $Status.draining -isnot [bool] -or $Status.draining -ne $true -or -not $Status.retiredAt) { throw 'TempRunnerNotRetired' }
    $stamp = if ($Status.retiredAt -is [datetime]) { $Status.retiredAt.ToUniversalTime().ToString('o') } else { [string]$Status.retiredAt }
    $parsed = [datetimeoffset]::MinValue
    if ($stamp -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$' -or
        -not [datetimeoffset]::TryParse($stamp, [ref]$parsed)) { throw 'TempRunnerNotRetired' }
    if ($Status.retireWhenIdle -isnot [bool] -or $Status.retireWhenIdle -ne $true -or [string]$Status.redirectTo -cne 'server2') { throw 'TempRunnerDrainConflict' }
    if ($Status.acceptingNewWork -isnot [bool] -or $Status.acceptingNewWork -ne $false) { throw 'TempRunnerDrainConflict' }
    $copy = $Status | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    if ($Status.PSObject.Properties.Name -notcontains 'runnerSessions') { throw 'RunnerCounterUnknown server2-temp runnerSessions' }
    if ($null -eq $Status.runnerSessions) {
        foreach ($key in @('available','dispatchEligible','acceptingNewWork')) {
            if ($Status.$key -isnot [bool] -or $Status.$key -ne $false) { throw 'RunnerCounterUnknown server2-temp runnerSessions' }
        }
        try { Assert-TempProjectAbsent } catch {
            if ($_.Exception.Message -eq 'TempContainersRemain') { throw 'RunnerCounterUnknown server2-temp runnerSessions' }
            throw
        }
        $copy.runnerSessions = 0
    }
    Assert-ZeroCounters -Status $copy -RunnerId 'server2-temp'
    return $parsed.ToUniversalTime().ToString('o')
}

function Assert-TempSeedCounters {
    param($Status)
    if ($null -eq $Status) { throw 'RunnerStatusMissing server2-temp' }
    # An absent temp container has no live inventory, whether its placeholder is
    # retired or cleared. The phase confirms host absence before changing the slot.
    $offline = $Status.available -eq $false -and $Status.dispatchEligible -eq $false -and
        $Status.acceptingNewWork -eq $false
    foreach ($name in @('sessions', 'runnerSessions', 'queuedTasks')) {
        $value = $Status.$name
        if ($Status.PSObject.Properties.Name -notcontains $name -or $null -eq $value) {
            if ($name -eq 'runnerSessions' -and $offline -and
                $Status.PSObject.Properties.Name -contains $name) { continue }
            throw "RunnerCounterUnknown server2-temp $name"
        }
        if ($value -isnot [int] -and $value -isnot [long]) { throw "RunnerCounterUnknown server2-temp $name" }
        if ($value -ne 0) { throw "RunnerBusy server2-temp $name" }
    }
}

# Cleanup admission does not normalize inventory or authorize volume deletion.
function Assert-TempCleanupStatus {
    param($Status, [string]$ExpectedRetiredAt = '')
    if ($null -eq $Status) { throw 'RunnerStatusMissing server2-temp' }
    foreach ($name in @('sessions','queuedTasks','runnerSessions')) {
        if ($Status.PSObject.Properties.Name -cnotcontains $name) { throw "RunnerCounterUnknown server2-temp $name" }
        $value = $Status.$name
        if ($name -ceq 'runnerSessions' -and $null -eq $value) { continue }
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -lt 0) { throw "RunnerCounterUnknown server2-temp $name" }
        if ($value -ne 0) { throw "RunnerBusy server2-temp $name" }
    }
    foreach ($name in @('draining','retireWhenIdle')) {
        if ($Status.$name -isnot [bool] -or $Status.$name -ne $true) { throw 'TempRunnerNotRetired' }
    }
    foreach ($name in @('available','dispatchEligible','acceptingNewWork')) {
        if ($Status.$name -isnot [bool] -or $Status.$name -ne $false) {
            if ($null -eq $Status.runnerSessions) { throw 'RunnerCounterUnknown server2-temp runnerSessions' }
            throw 'TempRunnerNotRetired'
        }
    }
    if ($Status.redirectTo -isnot [string] -or $Status.redirectTo -cne 'server2') { throw 'TempRunnerDrainConflict' }
    $stamp = ConvertTo-C994RetiredInstant $Status.retiredAt
    if ($ExpectedRetiredAt -and $stamp -cne $ExpectedRetiredAt) { throw 'TempRunnerRetirementChanged' }
    return $stamp
}

function Assert-TempCleanupMain {
    param($Status, [switch]$RequireSha)
    if ($null -eq $Status) { throw 'OldRunnerNotAcceptingNewWork' }
    foreach ($name in @('available','dispatchEligible','acceptingNewWork')) {
        if ($Status.$name -isnot [bool] -or $Status.$name -ne $true) { throw 'OldRunnerNotAcceptingNewWork' }
    }
    if ($Status.draining -isnot [bool] -or $Status.draining -ne $false -or
        $Status.PSObject.Properties.Name -cnotcontains 'retiredAt' -or $null -ne $Status.retiredAt -or
        ($RequireSha -and [string]$Status.buildVersion -cne $Sha)) { throw 'OldRunnerNotAcceptingNewWork' }
}

function Get-C994Now {
    # Existing harness seams are file-backed; wall-clock time is production's budget.
    if ($env:C727_TEST_VERIFY_STUB -and $env:C727_TEST_STATE) {
        $state = Get-Content -Raw -LiteralPath $env:C727_TEST_STATE | ConvertFrom-Json
        if ($state.PSObject.Properties.Name -contains 'clockMs') {
            return [datetime]::UnixEpoch.AddMilliseconds([long]$state.clockMs)
        }
    }
    return [datetime]::UtcNow
}

function Invoke-TempContainerCleanup {
    param($Status, [string]$ProjectId, [string]$ExpectedRetiredAt = '', [switch]$Preview)
    $stamp = Assert-TempCleanupStatus -Status $Status -ExpectedRetiredAt $ExpectedRetiredAt
    Assert-TempCleanupMain -Status (Get-RunnerStatus 'server2')
    [void](Assert-RecycleTaskCensus -RunnerId 'server2-temp' -ProjectId $ProjectId)
    $operation = 'c994' + [guid]::NewGuid().ToString('N')
    $context = [pscustomobject]@{ version=1; operationId=$operation; project='antiphon-runner-temp';
        projectId=$ProjectId; dryRun=[bool]$Preview; retiredAt=$stamp }
    Invoke-HostCase -Case 'retire-temp-containers' -Cleanup $context
    $receiptPath = Join-Path $evidenceRoot 'retire-temp-containers/temp-containers.json'
    $hashPath = Join-Path $evidenceRoot 'retire-temp-containers/temp-containers.sha256'
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $hashPath -PathType Leaf)) { throw 'TempContainerReceiptUnavailable' }
    $raw = Get-Content -Raw -LiteralPath $receiptPath
    Assert-C994RawManifest -Json $raw
    $receipt = $raw | ConvertFrom-Json
    $digest = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
    $storedHash = (Get-Content -Raw -LiteralPath $hashPath).Trim()
    if ($storedHash -cnotmatch '^[0-9a-fA-F]{64}$' -or $digest -ine $storedHash -or
        $receipt.schema -ne 1 -or [string]$receipt.sourceSha -cne $Sha -or
        [string]$receipt.runId -cne $runId -or [string]$receipt.operationId -cne $operation -or
        [string]$receipt.project -cne 'antiphon-runner-temp' -or [string]$receipt.projectId -cne $ProjectId -or
        (ConvertTo-C994RetiredInstant $receipt.retiredAt) -cne $stamp -or
        $receipt.dryRun -isnot [bool] -or $receipt.dryRun -ne [bool]$Preview -or
        $receipt.candidates -isnot [array] -or $receipt.removals -isnot [array] -or
        ($Preview -and $receipt.outcome -cne 'preview') -or
        (-not $Preview -and ($receipt.outcome -cne 'completed' -or $receipt.finalCensus -isnot [array] -or $receipt.finalCensus.Count -ne 0))) {
        throw 'TempContainerReceiptUnavailable'
    }
    return $receipt
}

function Wait-TempContainerRetirement {
    param([datetime]$Deadline, [string]$ProjectId)
    $stamp = ''; $pollMs = 30000
    if ($env:C727_TEST_POLL_MS) { $pollMs = [int]$env:C727_TEST_POLL_MS }
    do {
        if ((Get-C994Now) -ge $Deadline) { throw 'TempContainerExitTimeout' }
        $status = Get-RunnerStatus 'server2-temp'
        Assert-TempCleanupMain -Status (Get-RunnerStatus 'server2') -RequireSha
        if ($null -eq $status) { throw 'TempRunnerStatusMissing' }
        if ($status.retiredAt) {
            $observed = ConvertTo-C994RetiredInstant $status.retiredAt
            if ($stamp -and $stamp -cne $observed) { throw 'TempRunnerRetirementChanged' }
            $stamp = $observed
            # A connected retirement acknowledgement is an observation, never a
            # deletion authority. All other malformed fields refuse immediately.
            $copy = $status | ConvertTo-Json -Depth 30 | ConvertFrom-Json
            foreach ($name in @('available','dispatchEligible','acceptingNewWork')) {
                if ($copy.$name -isnot [bool]) { throw 'TempRunnerNotRetired' }
                $copy.$name = $false
            }
            [void](Assert-TempCleanupStatus -Status $copy -ExpectedRetiredAt $stamp)
            if ($status.available -ceq $false -and $status.dispatchEligible -ceq $false -and $status.acceptingNewWork -ceq $false) {
                try {
                    $receipt = Invoke-TempContainerCleanup -Status $status -ProjectId $ProjectId -ExpectedRetiredAt $stamp
                    if ((Get-C994Now) -ge $Deadline) { throw 'TempContainerExitTimeout' }
                    return $receipt
                } catch {
                    if ($_.Exception.Message -notlike '*TempContainerStillRunning*') { throw }
                }
            }
        }
        if ((Get-C994Now) -ge $Deadline) { throw 'TempContainerExitTimeout' }
        Start-Sleep -Milliseconds $pollMs
        if ($env:C727_TEST_VERIFY_STUB -and $env:C727_TEST_STATE) {
            $clockState = Get-Content -Raw $env:C727_TEST_STATE | ConvertFrom-Json
            if ($clockState.PSObject.Properties.Name -contains 'clockMs') {
                $clockState.clockMs += $pollMs
                $clockState | ConvertTo-Json -Compress -Depth 30 | Set-Content $env:C727_TEST_STATE
            }
        }
    } while ($true)
}

function Wait-RunnerStatus {
    param([string]$RunnerId, [scriptblock]$Ready, [int]$Minutes, [string]$Diagnosis)
    $deadline = [datetime]::UtcNow.AddMinutes($Minutes)
    $pollMs = 30000
    if ($env:C727_TEST_WAIT_MS) { $deadline = [datetime]::UtcNow.AddMilliseconds([int]$env:C727_TEST_WAIT_MS) }
    if ($env:C727_TEST_POLL_MS) { $pollMs = [int]$env:C727_TEST_POLL_MS }
    do {
        $status = Get-RunnerStatus -RunnerId $RunnerId
        if ($null -ne $status -and (& $Ready $status)) { return $status }
        if ([datetime]::UtcNow -ge $deadline) { throw $Diagnosis }
        Start-Sleep -Milliseconds $pollMs
    } while ($true)
}

function Invoke-HostCase {
    param([string]$Case, [string]$TempRetiredAt = '', [string]$RunnerId = '', $Recycle = $null, $Cleanup = $null)
    $manifest = [ordered]@{
        evidenceRoot = $evidenceRoot
        sourceSha = $Sha
        runId = $runId
        c604Branch = 'master'
    }
    if ($null -ne $Cleanup) { Assert-C994BridgeContext -Context $Cleanup -Case $Case; $manifest.tempContainerCleanup = $Cleanup }
    if ($TempRetiredAt) { $manifest.tempRetiredAt = $TempRetiredAt }
    if ($RunnerId) { $manifest.runnerId = $RunnerId }
    if ($null -ne $Recycle) { Assert-RecycleContext -Context $Recycle; $manifest.recycle = $Recycle }
    if ($Case -eq 'runner-cache-seed' -and $SavedDonor) { $manifest.savedDonor = $SavedDonor }
    $manifestPath = Join-Path $evidenceRoot ("$Case.manifest.json")
    $manifest | ConvertTo-Json -Compress -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding ascii
    $verifier = if ($env:C727_TEST_VERIFY_STUB) { $env:C727_TEST_VERIFY_STUB } else { Join-Path $PSScriptRoot 'verify-docker-stack.ps1' }
    & pwsh -NoProfile -File $verifier -Case $Case -Manifest $manifestPath
    if ($LASTEXITCODE -ne 0) {
        $diagnosis = ''
        $resultFile = Join-Path $evidenceRoot ($Case + '/c590-result.json')
        if (Test-Path -LiteralPath $resultFile -PathType Leaf) {
            try { $result = Get-Content -Raw -LiteralPath $resultFile | ConvertFrom-Json
                if ($result.diagnosis -ceq 'TempContainerStillRunning') { $diagnosis = ' TempContainerStillRunning' }
            } catch { }
        }
        throw "HostCaseFailed $Case exit=$LASTEXITCODE$diagnosis"
    }
}

# Read-only host census before a retired slot can be cleared. Keep stopped containers
# in the census: any leftover may reconnect after the clear.
function Assert-TempProjectAbsent {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($env:C727_TEST_VERIFY_STUB) {
        $manifestPath = Join-Path $evidenceRoot 'temp-project-absent.manifest.json'
        @{ sourceSha = $Sha; runId = $runId } | ConvertTo-Json -Compress |
            Set-Content -LiteralPath $manifestPath -Encoding ascii
        $psi.FileName = 'pwsh'
        $tokens = @('-NoProfile', '-File', $env:C727_TEST_VERIFY_STUB, '-Case', 'temp-project-absent', '-Manifest', $manifestPath)
    } else {
        $psi.FileName = 'ssh'
        $tokens = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', 'mc@server2',
            'docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp')
    }
    foreach ($token in $tokens) { [void]$psi.ArgumentList.Add($token) }
    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        try {
            $stdout = $proc.StandardOutput.ReadToEndAsync()
            $stderr = $proc.StandardError.ReadToEndAsync()
            if (-not $proc.WaitForExit(30000)) { $proc.Kill($true); $proc.WaitForExit(); throw 'TempContainerCensusUnavailable' }
            $ids = $stdout.GetAwaiter().GetResult()
            [void]$stderr.GetAwaiter().GetResult()
            if ($proc.ExitCode -ne 0) { throw 'TempContainerCensusUnavailable' }
            $ids | Set-Content -LiteralPath (Join-Path $evidenceRoot 'temp-project-containers.txt') -Encoding ascii
        } finally { $proc.Dispose() }
    } catch { throw 'TempContainerCensusUnavailable' }
    if (-not [string]::IsNullOrWhiteSpace($ids)) { throw 'TempContainersRemain' }
}

function Invoke-Phase {
    param([string]$Name)
    Invoke-HostJq -Mode check -ExecutingPhase $Name
    if ($Name -in @('redeploy-old', 'drain-temp', 'retire-temp')) {
        $censusRunner = if ($Name -eq 'redeploy-old') { 'server2' } else { 'server2-temp' }
        Start-RecycleReceipt -ExecutingPhase $Name -RunnerId $censusRunner
    }
    switch ($Name) {
        'deploy-temp' {
            if ($SavedDonor) { throw 'TempSavedDonorRequiresMaintenance' }
            $s = Get-RunnerStatus -RunnerId 'server2-temp'
            if ($null -eq $s) { throw 'TempRunnerStatusMissing' }
            # An offline slot may still report the last container's SHA. It needs a
            # fresh container even when that retained version matches this rollout.
            if ([string]$s.buildVersion -ne $Sha -or $s.available -ne $true) {
                Assert-TempSeedCounters -Status $s
                if ($s.draining -and [string]$s.redirectTo -ne 'server2') { throw 'TempRunnerDrainConflict' }
                $old = Get-RunnerStatus -RunnerId 'server2'
                if ($null -eq $old -or $old.dispatchEligible -ne $true -or $old.acceptingNewWork -ne $true -or
                    $old.draining -ne $false -or $old.retiredAt) { throw 'OldRunnerRedirectNotEligible' }
                Assert-TempProjectAbsent
                if ($s.retiredAt) {
                    if ($s.available -ne $false -or $s.dispatchEligible -ne $false -or
                        $s.acceptingNewWork -ne $false) { throw 'TempRunnerDrainConflict' }
                    $clearReason = 'CARD-0948 retired temp placeholder reactivation'
                    [void](Invoke-RunnerRequest -Method POST -RunnerId 'server2-temp' -Suffix '/drain/clear' -Body @{ reason = $clearReason })
                    @{ runnerId = 'server2-temp'; retiredAt = $s.retiredAt; clearReason = $clearReason } |
                        ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $evidenceRoot 'temp-retirement-clear.json') -Encoding ascii
                    $s = Get-RunnerStatus -RunnerId 'server2-temp'
                    Assert-TempSeedCounters -Status $s
                }
                if ($s.draining -and [string]$s.redirectTo -ne 'server2') { throw 'TempRunnerDrainConflict' }
                if (-not $s.draining -or $s.retireWhenIdle) {
                    [void](Invoke-RunnerRequest -Method POST -RunnerId 'server2-temp' -Suffix '/drain' -Body @{
                        reason = 'CARD-0948 temp deployment hold'; redirectTo = 'server2'; retireWhenIdle = $false
                    })
                    $s = Get-RunnerStatus -RunnerId 'server2-temp'
                    Assert-TempSeedCounters -Status $s
                }
                if ($s.retiredAt) { throw 'TempRunnerRetiredDuringHold' }
                if (-not $s.draining -or [string]$s.redirectTo -ne 'server2' -or $s.retireWhenIdle) {
                    throw 'TempRunnerDrainConflict'
                }
                Invoke-HostCase -Case 'runner-cache-seed'
                Invoke-HostCase -Case 'deploy-temp-runner'
            }
            [void](Wait-RunnerStatus -RunnerId 'server2-temp' -Minutes 5 -Diagnosis 'TempRunnerNotEligible' -Ready {
                param($s) $s.dispatchEligible -eq $true -and [string]$s.buildVersion -eq $Sha
            })
            Invoke-HostCase -Case 'verify-runner-caches' -RunnerId 'server2-temp'
            $s = Get-RunnerStatus -RunnerId 'server2-temp'
            if ($null -eq $s -or $s.retiredAt) { throw 'TempRunnerRetiredDuringHold' }
            if ($s.draining) {
                if ([string]$s.redirectTo -ne 'server2' -or $s.retireWhenIdle) { throw 'TempRunnerDrainConflict' }
                [void](Invoke-RunnerRequest -Method POST -RunnerId 'server2-temp' -Suffix '/drain/clear' -Body @{ reason = 'CARD-0849 cache verification passed' })
            }
            [void](Wait-RunnerStatus -RunnerId 'server2-temp' -Minutes 5 -Diagnosis 'TempRunnerNotAcceptingNewWork' -Ready {
                param($s) $s.acceptingNewWork -eq $true -and [string]$s.buildVersion -eq $Sha
            })
        }
        'drain-old' {
            $temp = Get-RunnerStatus -RunnerId 'server2-temp'
            if ($null -eq $temp -or -not $temp.acceptingNewWork -or [string]$temp.buildVersion -ne $Sha) {
                throw 'TempRunnerNotAcceptingNewWork'
            }
            $s = Get-RunnerStatus -RunnerId 'server2'
            if ($null -eq $s) { throw 'OldRunnerStatusMissing' }
            if (-not $s.draining) {
                [void](Invoke-RunnerRequest -Method POST -RunnerId 'server2' -Suffix '/drain' -Body @{
                    reason = 'CARD-0727 rolling upgrade'; redirectTo = 'server2-temp'; retireWhenIdle = $false
                })
            }
            elseif ([string]$s.redirectTo -ne 'server2-temp' -or $s.retireWhenIdle) {
                throw 'OldRunnerDrainConflict'
            }
            [void](Wait-RunnerStatus -RunnerId 'server2' -Minutes $WaitIdleMinutes -Diagnosis 'OldRunnerStillBusy' -Ready {
                param($s) $s.draining -and $null -ne $s.sessions -and $null -ne $s.runnerSessions -and
                    $null -ne $s.queuedTasks -and $s.sessions -eq 0 -and $s.runnerSessions -eq 0 -and $s.queuedTasks -eq 0
            })
        }
        'redeploy-old' {
            $s = Get-RunnerStatus -RunnerId 'server2'
            if ($null -eq $s) { throw 'OldRunnerStatusMissing' }
            if ([string]$s.buildVersion -eq $Sha -and -not $ResumeRecycle -and -not $DryRun) { Assert-NoIncompleteRecycle }
            if ([string]$s.buildVersion -eq $Sha -and -not $ResumeRecycle -and
                ($s.available -ne $true -or $s.dispatchEligible -ne $true)) { throw 'RecycleResumeRequired' }
            $preservedStore = [string]$s.runnerStoreId
            if ([string]$s.buildVersion -ne $Sha -or $ResumeRecycle -or $DryRun) {
                if ($ResumeRecycle -and $null -eq $s.runnerSessions -and $s.available -is [bool] -and
                    $s.available -eq $false -and $s.dispatchEligible -is [bool] -and $s.dispatchEligible -eq $false) {
                    # Only the host's operation journal can prove its own prior stop/removal.
                    # This admits the request to that proof; it never authorizes deletion here.
                    $zero = $s | ConvertTo-Json -Depth 30 | ConvertFrom-Json
                    $zero.runnerSessions = 0
                    Assert-ZeroCounters -Status $zero -RunnerId 'server2'
                }
                else { Assert-ZeroCounters -Status $s -RunnerId 'server2' }
                if ($s.draining -isnot [bool] -or $s.draining -ne $true -or [string]$s.redirectTo -cne 'server2-temp' -or
                    $s.retireWhenIdle -isnot [bool] -or $s.retireWhenIdle -ne $false -or $s.retiredAt) { throw 'OldRunnerDrainConflict' }
                if ($s.acceptingNewWork -isnot [bool] -or $s.acceptingNewWork -ne $false) { throw 'RecycleRoutingActive' }
                $counterpart = Get-RunnerStatus -RunnerId 'server2-temp'
                if ($null -eq $counterpart -or $counterpart.acceptingNewWork -isnot [bool] -or $counterpart.acceptingNewWork -ne $true) { throw 'TempRunnerNotAcceptingNewWork' }
                $projectId = Get-RecycleProjectId
                [void](Assert-RecycleTaskCensus -RunnerId 'server2' -ProjectId $projectId)
                $operation = if ($ResumeRecycle) { $ResumeRecycle } else { 'c1008' + [guid]::NewGuid().ToString('N') }
                $context = [pscustomobject]@{ version=1; project='antiphon-runner'; operationId=$operation; dryRun=[bool]$DryRun; resume=[bool]$ResumeRecycle; projectId=$projectId }
                Invoke-HostCase -Case 'deploy-parent' -Recycle $context
                if ($DryRun) { return }
            }
            [void](Wait-RunnerStatus -RunnerId 'server2' -Minutes 5 -Diagnosis 'OldRunnerNotDispatchEligible' -Ready {
                param($s) $s.dispatchEligible -eq $true -and [string]$s.buildVersion -eq $Sha
            })
            Invoke-HostCase -Case 'verify-runner-caches' -RunnerId 'server2'
            $s = Get-RunnerStatus -RunnerId 'server2'
            if ([string]$s.runnerStoreId -cne $preservedStore) { throw 'RecycleRunnerStoreChanged' }
            if ($s.draining) {
                if ([string]$s.redirectTo -ne 'server2-temp' -or $s.retireWhenIdle) { throw 'OldRunnerDrainConflict' }
                [void](Invoke-RunnerRequest -Method POST -RunnerId 'server2' -Suffix '/drain/clear' -Body @{ reason = 'CARD-0727 rolling upgrade complete' })
            }
            [void](Wait-RunnerStatus -RunnerId 'server2' -Minutes 5 -Diagnosis 'OldRunnerNotAcceptingNewWork' -Ready {
                param($s) $s.acceptingNewWork -eq $true -and [string]$s.buildVersion -eq $Sha
            })
        }
        'drain-temp' {
            $deadline = (Get-C994Now).AddMinutes($WaitIdleMinutes)
            if ($env:C727_TEST_WAIT_MS) { $deadline = (Get-C994Now).AddMilliseconds([int]$env:C727_TEST_WAIT_MS) }
            $old = Get-RunnerStatus -RunnerId 'server2'
            Assert-TempCleanupMain -Status $old -RequireSha
            $s = Get-RunnerStatus -RunnerId 'server2-temp'
            if ($null -eq $s) { throw 'TempRunnerStatusMissing' }
            if (-not $s.retiredAt -and -not $s.draining) {
                [void](Invoke-RunnerRequest -Method POST -RunnerId 'server2-temp' -Suffix '/drain' -Body @{
                    reason = 'CARD-0727 rolling upgrade complete'; redirectTo = 'server2'; retireWhenIdle = $true
                })
            }
            elseif (-not $s.retiredAt -and ([string]$s.redirectTo -ne 'server2' -or -not $s.retireWhenIdle)) {
                throw 'TempRunnerDrainConflict'
            }
            $projectId = Get-RecycleProjectId
            [void](Wait-TempContainerRetirement -Deadline $deadline -ProjectId $projectId)
        }
        'retire-temp' {
            $s = Get-RunnerStatus -RunnerId 'server2-temp'
            $projectId = Get-RecycleProjectId
            $cleanup = $null
            if (-not $ResumeRecycle) {
                $cleanup = Invoke-TempContainerCleanup -Status $s -ProjectId $projectId -Preview:$DryRun
                if ($DryRun -and $cleanup.candidates.Count -gt 0) {
                    Write-Output 'C994_PREVIEW volumeProof=pending-confirmed-absence auditPending=true'
                    return
                }
                $s = Get-RunnerStatus 'server2-temp'
                [void](Assert-TempCleanupStatus -Status $s -ExpectedRetiredAt (ConvertTo-C994RetiredInstant $cleanup.retiredAt))
            }
            $retiredAt = Assert-RetiredTempCounters -Status $s
            Assert-TempProjectAbsent
            $old = Get-RunnerStatus -RunnerId 'server2'
            Assert-TempCleanupMain -Status $old
            $projectId = Get-RecycleProjectId
            [void](Assert-RecycleTaskCensus -RunnerId 'server2-temp' -ProjectId $projectId)
            $operation = if ($ResumeRecycle) { $ResumeRecycle } else { 'c1008' + [guid]::NewGuid().ToString('N') }
            $context = [pscustomobject]@{ version=1; project='antiphon-runner-temp'; operationId=$operation; dryRun=[bool]$DryRun; resume=[bool]$ResumeRecycle; projectId=$projectId }
            if ($null -ne $cleanup) { $context | Add-Member -NotePropertyName cleanupOperationId -NotePropertyValue $cleanup.operationId }
            Invoke-HostCase -Case 'retire-temp-runner' -TempRetiredAt $retiredAt -Recycle $context
        }
    }
}

try {
    if ($Phase -eq 'check-census') {
        # Read-only: no host jq, rollout lock, SSH, verify, POST or host case.
        $resolvedProject = $null
        foreach ($runner in @('server2','server2-temp')) {
            Start-RecycleReceipt -ExecutingPhase $Phase -RunnerId $runner
            if ($null -eq $resolvedProject) {
                $entryProjectId = Get-RecycleProjectId
                $resolvedProject = $script:recycleReceipt.project
                Write-Output "RECYCLE_PROJECT id=$entryProjectId name=$($resolvedProject.name) resolvedBy=$($resolvedProject.resolvedBy)"
            } else {
                $script:recycleReceipt.project = $resolvedProject
                $script:resolvedRecycleProjectId = $entryProjectId
            }
            $census = Assert-RecycleTaskCensus -RunnerId $runner -ProjectId $entryProjectId -Report
            Write-Output "RECYCLE_CENSUS runner=$runner open=$($census.open) boundOpen=$($census.boundOpen) landPending=$($census.landPending) scopes=$($census.scopes) elapsedMs=$($census.elapsedMs)"
        }
        Write-Output "Census check complete: $evidenceRoot"
        exit 0
    }
    if ($script:recycleProjectIdWasProvided -and -not (Test-RecycleGuid $script:requestedRecycleProjectId)) {
        throw 'RecycleProjectUnresolved cause=InvalidId'
    }
    if ($Phase -in @('check-host-jq', 'provision-host-jq')) {
        $mode = if ($Phase -eq 'check-host-jq') { 'check' } else { 'provision' }
        Invoke-HostJq -Mode $mode -ExecutingPhase $Phase
        Write-Output "Host jq qualified: $evidenceRoot"
        exit 0
    }
    $phases = if ($Phase -eq 'all') { @('deploy-temp', 'drain-old', 'redeploy-old', 'drain-temp', 'retire-temp') } else { @($Phase) }
    foreach ($name in $phases) { Invoke-Phase -Name $name }
    Write-Output "Rolling deploy complete: $evidenceRoot"
    exit 0
}
catch {
    [Console]::Error.WriteLine("Rolling deploy stopped: " + $_.Exception.Message)
    exit 2
}
