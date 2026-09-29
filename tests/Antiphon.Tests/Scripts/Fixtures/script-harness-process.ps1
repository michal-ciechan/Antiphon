param(
    [string]$Case,
    [string]$ResultsDirectory,
    [string]$HelperPath,
    [string]$Payload
)
$ErrorActionPreference = 'Stop'
[System.IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$root = [System.Diagnostics.Process]::GetCurrentProcess()
$nativeStart = 0
if ($IsLinux) {
    $stat = [System.IO.File]::ReadAllText("/proc/$PID/stat")
    $fields = $stat.Substring($stat.LastIndexOf(') ') + 2).Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)
    $nativeStart = $fields[19]
}
[System.IO.File]::WriteAllText((Join-Path $ResultsDirectory 'root'), "$PID $($root.StartTime.ToUniversalTime().Ticks) $nativeStart")

if ($Case -in @('LiveRoot','ExitedStdout','ExitedStderr','Cancellation','Silent','Race')) {
    $held = switch ($Case) {
        'ExitedStdout' { 'stdout' }
        'ExitedStderr' { 'stderr' }
        'Silent' { 'none' }
        default { 'both' }
    }
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    foreach ($argument in @($HelperPath,'fixture-child',$ResultsDirectory,$held)) {
        [void]$start.ArgumentList.Add([string]$argument)
    }
    $child = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $child) { throw 'Fixture child did not start.' }
    $limit = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not ((Test-Path (Join-Path $ResultsDirectory 'child')) -and (Test-Path (Join-Path $ResultsDirectory 'grandchild')))) {
        if ($limit.Elapsed.TotalSeconds -gt 4) { throw 'Fixture child and grandchild readiness timed out.' }
        Start-Sleep -Milliseconds 20
    }
    [System.IO.File]::WriteAllText((Join-Path $ResultsDirectory 'ready'), $Case)
    if ($Case -in @('LiveRoot','Cancellation')) { Start-Sleep -Seconds 20 }
    if ($Case -eq 'Race') {
        while (-not (Test-Path (Join-Path $ResultsDirectory 'release'))) { Start-Sleep -Milliseconds 20 }
    }
    if ($Case -ne 'Silent') { exit 0 }
}

if ($Case -eq 'Nonzero') {
    [Console]::Out.WriteLine('C806 stdout marker')
    [Console]::Error.WriteLine('C806 stderr marker')
    exit 37
}
if ($Case -eq 'HighVolume') {
    [Console]::Out.Write(('A' * 1048576))
    [Console]::Error.Write(('B' * 1048576))
    [Console]::Out.WriteLine('C806 stdout end')
    [Console]::Error.WriteLine('C806 stderr end')
}
if ($Case -eq 'BadInventory') {
    Write-Output 'C487 HARNESS EXIT CODE: 0'
    Write-Output 'C487: 0 passed, 0 failed, 0 rows'
    exit 0
}
if ($Case -eq 'Stdin') {
    $line = [Console]::In.ReadLine()
    Write-Output "STDIN:$line"
}
if ($Case -eq 'Passing' -and $Payload) {
    [System.IO.File]::WriteAllText((Join-Path $ResultsDirectory 'payload'), $Payload)
}
Write-Output 'PASS C806 C806 fixture passed'
Write-Output 'C487 HARNESS EXIT CODE: 0'
Write-Output 'C487: 1 passed, 0 failed, 1 rows'
