# CARD-0727 D-17. Run from the desktop after Review, never on server2.
# The operator token is read from the same owner-only file as runner-slots.ps1.
# This script requires PowerShell 7.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][switch]$Rolling,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Sha,
    [ValidateSet('all', 'deploy-temp', 'drain-old', 'redeploy-old', 'drain-temp', 'retire-temp')]
    [string]$Phase = 'all',
    [string]$SavedDonor = '',
    [ValidateRange(1, 10080)][int]$WaitIdleMinutes = 480
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required' }
. (Join-Path $PSScriptRoot 'lib/runner-operator-token.ps1')

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')
$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}
# Resolve before a case runs. Never print this value, headers, or an exception request object.
try { $headers['X-Antiphon-Operator-Token'] = Get-RunnerOperatorToken }
catch { [Console]::Error.WriteLine('OperatorTokenMissing: the owner-only token file is absent or empty.'); exit 2 }

$Sha = $Sha.ToLowerInvariant()
if ($SavedDonor -and ($SavedDonor -cnotmatch '^/[A-Za-z0-9._/-]{1,500}$' -or
        $SavedDonor.Contains('..') -or $SavedDonor.Contains('//'))) { throw 'CacheSavedDonorPathInvalid' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$runId = 'c727' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$evidenceRoot = Join-Path $repoRoot ('.antiphon/rolling-server2/' + $runId)
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

function Invoke-RunnerRequest {
    param([string]$Method, [string]$RunnerId, [string]$Suffix = '', $Body = $null)
    $path = '/api/session-runners/' + [uri]::EscapeDataString($RunnerId) + $Suffix
    if ($env:C727_TEST_HTTP_STUB) {
        $bodyJson = if ($null -eq $Body) { '' } else { $Body | ConvertTo-Json -Compress }
        $raw = & pwsh -NoProfile -File $env:C727_TEST_HTTP_STUB -Method $Method -RunnerId $RunnerId -Suffix $Suffix -BodyJson $bodyJson
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
        if ([int]$Status.$name -ne 0) { throw "RunnerBusy $RunnerId $name" }
    }
}

function Assert-TempSeedCounters {
    param($Status)
    if ($null -eq $Status) { throw 'RunnerStatusMissing server2-temp' }
    # Retirement removes the live connection, so runnerSessions becomes null after
    # compose down -v. Seed verifies on the host that the temp project is absent.
    $retiredOffline = $Status.retiredAt -and $Status.draining -and
        [string]$Status.redirectTo -eq 'server2' -and $Status.retireWhenIdle -eq $true -and
        $Status.available -eq $false -and $Status.dispatchEligible -eq $false -and
        $Status.acceptingNewWork -eq $false
    foreach ($name in @('sessions', 'runnerSessions', 'queuedTasks')) {
        $value = $Status.$name
        if ($Status.PSObject.Properties.Name -notcontains $name -or $null -eq $value) {
            if ($name -eq 'runnerSessions' -and $retiredOffline -and
                $Status.PSObject.Properties.Name -contains $name) { continue }
            throw "RunnerCounterUnknown server2-temp $name"
        }
        if ($value -isnot [int] -and $value -isnot [long]) { throw "RunnerCounterUnknown server2-temp $name" }
        if ($value -ne 0) { throw "RunnerBusy server2-temp $name" }
    }
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
    param([string]$Case, [string]$TempRetiredAt = '', [string]$RunnerId = '')
    $manifest = [ordered]@{
        evidenceRoot = $evidenceRoot
        sourceSha = $Sha
        runId = $runId
        c604Branch = 'master'
    }
    if ($TempRetiredAt) { $manifest.tempRetiredAt = $TempRetiredAt }
    if ($RunnerId) { $manifest.runnerId = $RunnerId }
    if ($Case -eq 'runner-cache-seed' -and $SavedDonor) { $manifest.savedDonor = $SavedDonor }
    $manifestPath = Join-Path $evidenceRoot ("$Case.manifest.json")
    $manifest | ConvertTo-Json -Compress | Set-Content -LiteralPath $manifestPath -Encoding ascii
    $verifier = if ($env:C727_TEST_VERIFY_STUB) { $env:C727_TEST_VERIFY_STUB } else { Join-Path $PSScriptRoot 'verify-docker-stack.ps1' }
    & pwsh -NoProfile -File $verifier -Case $Case -Manifest $manifestPath
    if ($LASTEXITCODE -ne 0) { throw "HostCaseFailed $Case exit=$LASTEXITCODE" }
}

function Invoke-Phase {
    param([string]$Name)
    switch ($Name) {
        'deploy-temp' {
            $s = Get-RunnerStatus -RunnerId 'server2-temp'
            if ($null -eq $s) { throw 'TempRunnerStatusMissing' }
            if ([string]$s.buildVersion -ne $Sha) {
                Assert-TempSeedCounters -Status $s
                if (-not $s.draining) {
                    [void](Invoke-RunnerRequest -Method POST -RunnerId 'server2-temp' -Suffix '/drain' -Body @{
                        reason = 'CARD-0849 cache migration'; redirectTo = 'server2'; retireWhenIdle = $true
                    })
                    $s = Get-RunnerStatus -RunnerId 'server2-temp'
                    Assert-TempSeedCounters -Status $s
                }
                if (-not $s.draining -or [string]$s.redirectTo -ne 'server2' -or -not $s.retireWhenIdle) {
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
            if ($s.draining) {
                if ([string]$s.redirectTo -ne 'server2' -or -not $s.retireWhenIdle) { throw 'TempRunnerDrainConflict' }
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
            if ([string]$s.buildVersion -ne $Sha) {
                Assert-ZeroCounters -Status $s -RunnerId 'server2'
                if (-not $s.draining -or [string]$s.redirectTo -ne 'server2-temp' -or $s.retireWhenIdle) { throw 'OldRunnerDrainConflict' }
                Invoke-HostCase -Case 'deploy-parent'
            }
            [void](Wait-RunnerStatus -RunnerId 'server2' -Minutes 5 -Diagnosis 'OldRunnerNotDispatchEligible' -Ready {
                param($s) $s.dispatchEligible -eq $true -and [string]$s.buildVersion -eq $Sha
            })
            Invoke-HostCase -Case 'verify-runner-caches' -RunnerId 'server2'
            $s = Get-RunnerStatus -RunnerId 'server2'
            if ($s.draining) {
                if ([string]$s.redirectTo -ne 'server2-temp' -or $s.retireWhenIdle) { throw 'OldRunnerDrainConflict' }
                [void](Invoke-RunnerRequest -Method POST -RunnerId 'server2' -Suffix '/drain/clear' -Body @{ reason = 'CARD-0727 rolling upgrade complete' })
            }
            [void](Wait-RunnerStatus -RunnerId 'server2' -Minutes 5 -Diagnosis 'OldRunnerNotAcceptingNewWork' -Ready {
                param($s) $s.acceptingNewWork -eq $true -and [string]$s.buildVersion -eq $Sha
            })
        }
        'drain-temp' {
            $old = Get-RunnerStatus -RunnerId 'server2'
            if ($null -eq $old -or -not $old.acceptingNewWork -or [string]$old.buildVersion -ne $Sha) {
                throw 'OldRunnerNotAcceptingNewWork'
            }
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
            [void](Wait-RunnerStatus -RunnerId 'server2-temp' -Minutes $WaitIdleMinutes -Diagnosis 'TempRunnerStillBusy' -Ready {
                param($s) -not [string]::IsNullOrWhiteSpace([string]$s.retiredAt)
            })
        }
        'retire-temp' {
            $s = Get-RunnerStatus -RunnerId 'server2-temp'
            if ($null -eq $s -or -not $s.retiredAt -or -not $s.draining) { throw 'TempRunnerNotRetired' }
            if ([string]$s.redirectTo -ne 'server2' -or -not $s.retireWhenIdle) { throw 'TempRunnerDrainConflict' }
            Assert-ZeroCounters -Status $s -RunnerId 'server2-temp'
            $retiredAt = if ($s.retiredAt -is [datetime]) {
                $s.retiredAt.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ')
            } else { [string]$s.retiredAt }
            Invoke-HostCase -Case 'retire-temp-runner' -TempRetiredAt $retiredAt
        }
    }
}

try {
    $phases = if ($Phase -eq 'all') { @('deploy-temp', 'drain-old', 'redeploy-old', 'drain-temp', 'retire-temp') } else { @($Phase) }
    foreach ($name in $phases) { Invoke-Phase -Name $name }
    Write-Output "Rolling deploy complete: $evidenceRoot"
    exit 0
}
catch {
    [Console]::Error.WriteLine("Rolling deploy stopped: " + $_.Exception.Message)
    exit 2
}
