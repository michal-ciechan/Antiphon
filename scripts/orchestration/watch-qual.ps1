#requires -Version 5.1
<#
.SYNOPSIS
Observe completed TRX failures, qualification completion and heartbeat stalls without touching the run.
.DESCRIPTION
Writes only the caller-owned StateFile, which must be outside the observed run. Reuse it
on re-arm. Parse errors remain eligible for retry. No process or lock operations.
ASCII-only, Windows PowerShell 5.1 safe; -Once permits offline smoke checks.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LogDir,
    [Parameter(Mandatory = $true)][string]$StateFile,
    [Parameter(Mandatory = $true)][string]$Heartbeat,
    [ValidateRange(1, 300)][int]$PollSeconds = 60,
    [ValidateRange(1, 29)][int]$MaxMinutes = 28,
    [ValidateRange(0, 300)][int]$MinFileAgeSeconds = 5,
    [ValidateRange(1, 60)][int]$HeartbeatStaleMinutes = 5,
    [switch]$Once)
$ErrorActionPreference = 'Stop'
$seen = @{}
if (Test-Path -LiteralPath $StateFile) {
    Get-Content -LiteralPath $StateFile | ForEach-Object { if ($_) { $seen[$_] = $true } }
}
$deadline = [datetime]::UtcNow.AddMinutes($MaxMinutes)
$ended = $false
do {
    if (-not (Test-Path -LiteralPath $LogDir -PathType Container)) { "LOG-DIR-MISSING $LogDir" }
    else {
        foreach ($file in @(Get-ChildItem -LiteralPath $LogDir -Filter '*.trx' -File | Sort-Object LastWriteTimeUtc)) {
            # Full paths prevent basename collisions when caller intentionally changes LogDir.
            if ($seen.ContainsKey($file.FullName)) { continue }
            if (([datetime]::UtcNow - $file.LastWriteTimeUtc).TotalSeconds -lt $MinFileAgeSeconds) { continue }
            try {
                [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
                $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
                $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
                if ($xml.DocumentElement.LocalName -ne 'TestRun' -or $xml.DocumentElement.NamespaceURI -ne $ns.LookupNamespace('t')) { throw 'Not a supported TRX TestRun.' }
                # A complete TRX has a ResultSummary; a partially written document is retried.
                if (-not $xml.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)) { throw 'TRX has no ResultSummary yet.' }
                $failed = @($xml.SelectNodes('//t:UnitTestResult[@outcome="Failed"]', $ns))
                if ($failed.Count -gt 0) {
                    $classes = @{}
                    foreach ($test in $xml.SelectNodes('//t:UnitTest', $ns)) {
                        $method = $test.SelectSingleNode('t:TestMethod', $ns)
                        if ($method) { $classes[[string]$test.id] = [string]$method.className }
                    }
                    $groups = $failed | ForEach-Object {
                        $class = $classes[[string]$_.testId]
                        if (-not $class) { $class = 'unknown' }
                        ($class -split '\.')[-1]
                    } | Group-Object | Sort-Object Count -Descending
                    $detail = ($groups | Select-Object -First 8 | ForEach-Object { "$($_.Name) $($_.Count)" }) -join ', '
                    "FAILURES $($file.Name): $($failed.Count) failed :: $detail"
                }
                Add-Content -LiteralPath $StateFile -Value $file.FullName -Encoding ASCII
                $seen[$file.FullName] = $true
            } catch { "TRX-PARSE-ERROR $($file.Name): $($_.Exception.Message)" }
        }
    }
    if (Test-Path -LiteralPath (Join-Path $LogDir 'summary.json')) {
        "RUN-ENDED summary.json present in $LogDir"
        $ended = $true
        break
    }
    if (Test-Path -LiteralPath $Heartbeat) {
        try {
            $beat = Get-Content -LiteralPath $Heartbeat -Raw | ConvertFrom-Json
            if (-not $beat.utc) { throw 'Heartbeat has no utc.' }
            $utc = [datetimeoffset]::Parse([string]$beat.utc).UtcDateTime
            if ($beat.hangSuspected) { "HANG-SUSPECTED heartbeat $($beat.utc) runId=$($beat.runId) idleMinutes=$($beat.idleMinutes)" }
            if (([datetime]::UtcNow - $utc).TotalMinutes -ge $HeartbeatStaleMinutes) { "HEARTBEAT-STALE $($beat.utc) runId=$($beat.runId) supervisorPid=$($beat.supervisorPid) trx=$($beat.trx)" }
        } catch { "HEARTBEAT-ERROR $($_.Exception.Message)" }
    } else { "HEARTBEAT-MISSING $Heartbeat" }
    if ($Once -or [datetime]::UtcNow -ge $deadline) { break }
    Start-Sleep -Seconds $PollSeconds
} while ([datetime]::UtcNow -lt $deadline)
if (-not $ended) { 'watch-qual window ended; re-arm' }
