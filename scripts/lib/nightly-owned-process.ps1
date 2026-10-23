#requires -Version 7.0
# CARD-1039: the production Windows launch/wait/job/drain boundary.
# CARD-1514: Linux can dot-source this file. Add-Type stays on Win32NT.
# Do not use $IsWindows: it is undefined on Windows PowerShell 5.1.
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    if (-not ('Antiphon.Nightly.NativeProcessOwner' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'nightly-owned-process.cs')
    }
}

function New-NightlyChildStartInfo {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [hashtable]$Environment
    )
    $start = [Diagnostics.ProcessStartInfo]::new()
    if (-not [string]::IsNullOrWhiteSpace($FilePath)) { $start.FileName = $FilePath }
    if ($ArgumentList) {
        foreach ($arg in @($ArgumentList)) { [void]$start.ArgumentList.Add([string]$arg) }
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $start.WorkingDirectory = $WorkingDirectory }
    if ($null -ne $Environment) {
        $start.Environment.Clear()
        foreach ($key in @($Environment.Keys)) { $start.Environment[[string]$key] = [string]$Environment[$key] }
    }
    . (Join-Path $PSScriptRoot 'build-driver-environment.ps1')
    Set-AntiphonBuildDriverEnvironment -StartInfo $start
    return $start
}

function Start-NightlyNativeOwner {
    param([string]$FilePath, [string[]]$ArgumentList, [string]$WorkingDirectory,
          [int]$TimeoutMilliseconds, [hashtable]$Environment, [string]$LogPath,
          [Antiphon.Nightly.NativeProcessHooks]$Hooks = $null)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Nightly native custody requires Windows.' }
    $command = Get-Command $FilePath -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $start = New-NightlyChildStartInfo -FilePath $command.Source -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -Environment $Environment
    [string[]]$entries = [string[]]@($start.Environment.Keys | ForEach-Object { '{0}={1}' -f $_, $start.Environment[$_] })
    return [Antiphon.Nightly.NativeProcessOwner]::Start($command.Source, $ArgumentList,
        $WorkingDirectory, $TimeoutMilliseconds, $LogPath, [string[]]$entries, $Hooks)
}
