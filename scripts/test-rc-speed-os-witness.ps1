#requires -Version 5.1
# CARD-1514 S1. Windows process-owner witnesses.
# Invoke-RcWindowsOwnerExitWitness is called on every C1039_DescendantExit run.
# Invoke-RcWindowsNativeProcessOwner remains the packaged-root refusal helper.

function Invoke-RcWindowsOwnerExitWitness {
    param(
        [string]$FilePath,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds,
        [string]$LogPath
    )
    $owner = [Antiphon.Nightly.NativeProcessOwner]::Start($FilePath, @($WorkingDirectory, 'descendant'), $WorkingDirectory, $TimeoutMilliseconds, $LogPath, $null, $null)
    $childPid = Join-Path $WorkingDirectory 'child.pid'
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $childPid)) {
        if ($deadline.ElapsedMilliseconds -ge 10000) { throw 'FAIL C1039 owner witness child pid' }
        Start-Sleep -Milliseconds 10
    }
    Start-Sleep -Milliseconds 300
    Set-Content -LiteralPath (Join-Path $WorkingDirectory 'child.release') -Value 'release'
    if (-not $owner.Completion.Wait($TimeoutMilliseconds)) { throw 'FAIL C1039 owner witness completion' }
    $result = $owner.Completion.GetAwaiter().GetResult()
    $image = [IO.Path]::GetFileNameWithoutExtension($FilePath)
    $images = @($result.DescendantImages)
    $imageSeen = $false
    foreach ($one in $images) {
        if ([string]$one -ceq $image) { $imageSeen = $true }
    }
    if (-not $imageSeen) { throw 'FAIL C1039 descendant image survives process exit' }
    if ([string]::IsNullOrWhiteSpace([string]$result.RootExitedAtUtc)) { throw 'FAIL C1039 root exit time missing' }
    if ($null -eq $result.MissedDescendantPids) { throw 'FAIL C1039 missed descendant pids missing' }
    if (-not $result.JobEmptyObserved) { throw 'FAIL C1039 job empty not observed' }
    return $owner
}

function Invoke-RcWindowsNativeProcessOwner {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds,
        [string]$LogPath
    )
    return [Antiphon.Nightly.NativeProcessOwner]::Start($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $LogPath, $null, $null)
}
