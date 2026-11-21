#requires -Version 7.0
# CARD-1039: the production Windows launch/wait/job/drain boundary.
# CARD-1514: Linux can dot-source this file. The native owner is loaded on Win32NT only.
# W2-nightly S04: NativeProcessOwner and NativeProcessHooks are compiled into the nightly core
# (Antiphon.Nightly/NativeProcessOwner.cs; OwnedLauncher there is the C# twin of these two
# functions). A caller that has not loaded the core loads it here; there is no Add-Type copy.
# Do not use $IsWindows: it is undefined on Windows PowerShell 5.1.
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    if (-not ('Antiphon.Nightly.NativeProcessOwner' -as [type])) {
        . (Join-Path $PSScriptRoot 'nightly-core.ps1')
        Assert-NightlyCoreAvailable 'nightly-owned-process'
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
          $Hooks = $null)
    # NativeProcessHooks is loaded only on Win32NT. A typed parameter is
    # resolved before this body and would hide the Linux refusal below.
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Nightly native custody requires Windows.' }
    $command = Get-Command $FilePath -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $executable = $command.Source
    # Packaged PowerShell can launch unpackaged children outside our private job.
    # Its WindowsApps alias and its physical package image both need this resolution.
    if ([IO.Path]::GetFileName($executable) -ieq 'pwsh.exe' -and $executable -match '(?i)[\\/]WindowsApps[\\/]') {
        $executable = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PowerShell\7\pwsh.exe'
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw 'Native process custody requires unpackaged PowerShell 7 in Program Files\PowerShell\7.'
        }
    }
    $start = New-NightlyChildStartInfo -FilePath $executable -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -Environment $Environment
    [string[]]$entries = [string[]]@($start.Environment.Keys | ForEach-Object { '{0}={1}' -f $_, $start.Environment[$_] })
    return [Antiphon.Nightly.NativeProcessOwner]::Start($executable, $ArgumentList,
        $WorkingDirectory, $TimeoutMilliseconds, $LogPath, [string[]]$entries, $Hooks)
}
