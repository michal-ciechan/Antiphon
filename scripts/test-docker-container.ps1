# Runs inside the test image, and validates the Linux class roster.
param(
    [switch]$ValidateRoster,
    [string]$Roster = '',
    [string]$Discovery = '',
    [string]$Checkpoint = '',
    [string]$Manifest = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'c590-command.ps1')

function Assert-LinuxTestRoster {
    param([string]$RosterPath, [string]$DiscoveryPath)
    $roster = Get-Content -Raw -LiteralPath $RosterPath | ConvertFrom-Json
    $discovered = @(Get-Content -Raw -LiteralPath $DiscoveryPath | ConvertFrom-Json)
    $rows = @($roster.classes)
    $errors = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    foreach ($row in $rows) {
        $name = [string]$row.class
        if ($seen.ContainsKey($name)) { $errors.Add(('duplicate class ' + $name)) }
        $seen[$name] = $true
        $included = [string]$row.disposition -eq 'include'
        $boundary = [string]$row.boundary
        $owner = [string]$row.owner
        $reason = [string]$row.reason
        if (-not $included -and ([string]::IsNullOrWhiteSpace($owner) -or [string]::IsNullOrWhiteSpace($reason))) {
            $errors.Add('MissingExclusionOwner')
        }
        if ($included -and ($boundary -match '(?i)native' -or $name -match 'FakeGrokContractTests|LandDeliveryFixture|OutputDistillationApplyCanaryTests|WorktreeLockDiagnosticsWindowsTests')) {
            $errors.Add(('IncludedNative ' + $name))
        }
        $closure = @($row.testHelperClosure) + @($name)
        if ($included -and ($closure -match 'WorkspaceHookRunnerTests' -or $name -eq 'WorkspaceHookRunnerTests')) {
            $errors.Add(('IncludedSpawner ' + $name))
        }
    }
    $known = @($rows | ForEach-Object { [string]$_.class })
    foreach ($name in $discovered) {
        if ($known -notcontains [string]$name) { $errors.Add(('unknown class ' + $name)) }
    }
    foreach ($name in $known) {
        if ($discovered -notcontains [string]$name) { $errors.Add(('stale class ' + $name)) }
    }
    if ($errors.Count -gt 0) {
        $errors | ForEach-Object { Write-Output $_ }
        exit 2
    }
    Write-Output 'roster-ok'
    exit 0
}

if ($ValidateRoster) {
    Assert-LinuxTestRoster -RosterPath $Roster -DiscoveryPath $Discovery
}

if ($Checkpoint) {
    $child = [ordered]@{
        ANTIPHON_BROKER_TESTS = '1'
        SessionRunner__BaseUrl = 'http://127.0.0.1:1'
    }
    $pairs = @()
    foreach ($key in $child.Keys) { $pairs += ($key + '=' + $child[$key]) }
    Invoke-C590 -Exe 'child-env' -ArgumentList $pairs | Out-Null
    Write-Output 'checkpoint-launched'
    exit 0
}

Write-Error 'Checkpoint or ValidateRoster is required'
exit 2
