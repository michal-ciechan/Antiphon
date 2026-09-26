#requires -Version 7.0
<#
.SYNOPSIS
    CARD-0718 CP-4. Measure runner-process CPU with host-stats sampling on and off.

    The child is launched with PhoneHome disabled, on 127.0.0.1:0, and every inherited
    PhoneHome__ / SessionRunner__ / ASPNETCORE_ variable removed so a measurement runner
    cannot phone home as the host it was started on. -DryRun prints ARG and ENVKEY lines
    (names only) and does not start a process.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RunnerDll,
    [int]$WarmupSeconds = 15,
    [int]$Seconds = 60,
    [int]$Pairs = 2,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ScrubPrefixes = @('PhoneHome__', 'SessionRunner__', 'ASPNETCORE_')

function New-RunnerStartInfo {
    param([bool]$Enabled, [string]$Root)
    $dll = [IO.Path]::GetFullPath($RunnerDll)
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "runner dll not found: $dll"
    }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'dotnet'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $Root
    $enabledText = if ($Enabled) { 'true' } else { 'false' }
    foreach ($arg in @(
            $dll,
            '--urls', 'http://127.0.0.1:0',
            '--PhoneHome:Enabled', 'false',
            '--SessionRunner:SessionLogPath', $Root,
            '--SessionRunner:Herdr:Enabled', 'false',
            '--SessionRunner:CpuWatchdogEnabled', 'false',
            '--SessionRunner:HostStats:Enabled', $enabledText,
            '--SessionRunner:HostStats:Volumes:0', $Root,
            '--Serilog:LogPath', (Join-Path $Root 'logs'))) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $names = @($psi.Environment.Keys)
    foreach ($name in $names) {
        foreach ($prefix in $ScrubPrefixes) {
            if ($name.StartsWith($prefix)) {
                [void]$psi.Environment.Remove($name)
                break
            }
        }
    }
    return $psi
}

function Test-ScrubbedStart {
    param($Psi)
    $bad = @()
    foreach ($name in @($Psi.Environment.Keys)) {
        foreach ($prefix in $ScrubPrefixes) {
            if ($name.StartsWith($prefix)) { $bad += $name; break }
        }
    }
    $args = @($Psi.ArgumentList)
    $urls = $false
    $phoneHomeOff = $false
    for ($i = 0; $i -lt $args.Count; $i++) {
        if ($args[$i] -eq '--urls' -and ($i + 1) -lt $args.Count -and $args[$i + 1] -eq 'http://127.0.0.1:0') { $urls = $true }
        if ($args[$i] -eq '--PhoneHome:Enabled' -and ($i + 1) -lt $args.Count -and $args[$i + 1] -eq 'false') { $phoneHomeOff = $true }
    }
    return [pscustomobject]@{ BadKeys = $bad; Urls = $urls; PhoneHomeOff = $phoneHomeOff }
}

if ($DryRun) {
    $root = Join-Path (Join-Path (Get-Location) '.antiphon/c718-checkpoints/CP-4') 'dry-run'
    $psi = New-RunnerStartInfo -Enabled $false -Root $root
    foreach ($arg in @($psi.ArgumentList)) { Write-Host "ARG $arg" }
    foreach ($name in @($psi.Environment.Keys)) { Write-Host "ENVKEY $name" }
    $check = Test-ScrubbedStart -Psi $psi
    if ($check.BadKeys.Count -gt 0 -or -not $check.Urls -or -not $check.PhoneHomeOff) { exit 2 }
    exit 0
}

function Stop-RunnerTree {
    param($Proc)
    if ($null -eq $Proc) { return }
    try {
        if (-not $Proc.HasExited) { $Proc.Kill($true) }
    } catch { }
    try { [void]$Proc.WaitForExit(15000) } catch { }
}

function Measure-Runner {
    param([bool]$Enabled, [int]$Slot)
    $root = Join-Path (Join-Path (Get-Location) '.antiphon/c718-checkpoints/CP-4') ([string]$Slot)
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $root 'logs') -Force | Out-Null
    $psi = New-RunnerStartInfo -Enabled $Enabled -Root $root
    $check = Test-ScrubbedStart -Psi $psi
    if ($check.BadKeys.Count -gt 0 -or -not $check.Urls -or -not $check.PhoneHomeOff) {
        throw "measurement runner is not isolated"
    }
    $lines = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    $proc.EnableRaisingEvents = $true
    $onLine = {
        if ($EventArgs.Data) { [void]$Event.MessageData.Enqueue([string]$EventArgs.Data) }
    }
    $stdoutSub = Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived -MessageData $lines -Action $onLine
    $stderrSub = Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived -MessageData $lines -Action $onLine
    try {
        if (-not $proc.Start()) { throw "runner did not start" }
        $proc.BeginOutputReadLine()
        $proc.BeginErrorReadLine()
        $listen = $null
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while ([DateTime]::UtcNow -lt $deadline) {
            while ($lines.TryDequeue([ref]$listen)) {
                if ($listen -match 'Now listening on:\s+(http://127\.0\.0\.1:\d+)') {
                    $listen = $Matches[1]
                    break
                }
                $listen = $null
            }
            if ($listen) { break }
            if ($proc.HasExited) { throw "runner exited $($proc.ExitCode) before listening" }
            Start-Sleep -Milliseconds 200
        }
        if (-not $listen) { throw "runner did not listen within 45s" }
        $uri = [Uri]$listen
        if ($uri.Port -eq 17204 -or $uri.Port -eq 8080) { throw "runner bound production port $($uri.Port)" }
        Start-Sleep -Seconds $WarmupSeconds
        $proc.Refresh()
        $cpu1 = $proc.TotalProcessorTime
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        Start-Sleep -Seconds $Seconds
        $proc.Refresh()
        $cpu2 = $proc.TotalProcessorTime
        $wall = $watch.Elapsed.TotalSeconds
        if ($wall -le 0) { throw "wall clock did not advance" }
        $cpuPercent = ($cpu2 - $cpu1).TotalSeconds / $wall * 100.0
        if ($Enabled) {
            $series = Invoke-WebRequest -Uri "$($uri.AbsoluteUri.TrimEnd('/'))/host-stats/series?metric=cpu&window=5m" -TimeoutSec 10
            $parsed = $series.Content | ConvertFrom-Json
            $samples = @($parsed.points).Count
            $status = 200
        } else {
            $response = Invoke-WebRequest -Uri "$($uri.AbsoluteUri.TrimEnd('/'))/host-stats" -SkipHttpErrorCheck -TimeoutSec 10
            $status = [int]$response.StatusCode
            $samples = 0
        }
        return [pscustomobject]@{ Cpu = $cpuPercent; Status = $status; Samples = $samples }
    } finally {
        Stop-RunnerTree -Proc $proc
        Unregister-Event -SourceIdentifier $stdoutSub.Name -ErrorAction SilentlyContinue
        Unregister-Event -SourceIdentifier $stderrSub.Name -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

$off = New-Object System.Collections.Generic.List[double]
$on = New-Object System.Collections.Generic.List[double]
$onSamples = New-Object System.Collections.Generic.List[int]
for ($pair = 0; $pair -lt $Pairs; $pair++) {
    $offResult = Measure-Runner -Enabled $false -Slot ($pair * 2)
    if ($offResult.Status -ne 404) {
        Write-Host "off-status=$($offResult.Status)"
        exit 2
    }
    $off.Add([double]$offResult.Cpu)
    $onResult = Measure-Runner -Enabled $true -Slot ($pair * 2 + 1)
    if ($onResult.Samples -lt 12) {
        Write-Host "on-samples=$($onResult.Samples)"
        exit 2
    }
    $on.Add([double]$onResult.Cpu)
    $onSamples.Add([int]$onResult.Samples)
}

$culture = [Globalization.CultureInfo]::InvariantCulture
$offMean = ($off | Measure-Object -Average).Average
$onMean = ($on | Measure-Object -Average).Average
$delta = $onMean - $offMean
$minSamples = ($onSamples | Measure-Object -Minimum).Minimum
$offText = (@($off) | ForEach-Object { $_.ToString('0.000', $culture) }) -join ','
$onText = (@($on) | ForEach-Object { $_.ToString('0.000', $culture) }) -join ','
$osName = if ($IsLinux) { 'linux' } elseif ($IsWindows) { 'windows' } else { 'other' }
Write-Host "on-samples=$minSamples"
Write-Host 'off-status=404'
if ($minSamples -ge 12) { Write-Host 'on-samples>=12' }
Write-Host ("OVERHEAD off={0} on={1} delta={2} host={3} os={4} cores={5} utc={6}" -f `
        $offText, $onText, $delta.ToString('0.000', $culture), [Environment]::MachineName, $osName, `
        [Environment]::ProcessorCount, [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))
if ($delta -ge 1.0) { exit 1 }
exit 0
