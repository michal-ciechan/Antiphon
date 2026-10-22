#requires -Version 7.0
# CARD-1039: the production Windows launch/wait/job/drain boundary.
if (-not ('Antiphon.Nightly.NativeProcessOwner' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'nightly-owned-process.cs')
}
function Start-NightlyNativeOwner {
    param([string]$FilePath, [string[]]$ArgumentList, [string]$WorkingDirectory,
          [int]$TimeoutMilliseconds, [hashtable]$Environment, [string]$LogPath,
          [Antiphon.Nightly.NativeProcessHooks]$Hooks = $null)
    if (-not $IsWindows) { throw 'Nightly native custody requires Windows.' }
    $command = Get-Command $FilePath -CommandType Application -ErrorAction Stop | Select-Object -First 1
    . (Join-Path $PSScriptRoot 'build-driver-environment.ps1')
    $start = [Diagnostics.ProcessStartInfo]::new()
    if ($null -ne $Environment) {
        $start.Environment.Clear()
        foreach ($key in $Environment.Keys) { $start.Environment[$key] = [string]$Environment[$key] }
    }
    Set-AntiphonBuildDriverEnvironment -StartInfo $start
    [string[]]$entries = $null
    $entries = [string[]]@($start.Environment.Keys | ForEach-Object { '{0}={1}' -f $_, $start.Environment[$_] })
    return [Antiphon.Nightly.NativeProcessOwner]::Start($command.Source, $ArgumentList,
        $WorkingDirectory, $TimeoutMilliseconds, $LogPath, [string[]]$entries, $Hooks)
}
