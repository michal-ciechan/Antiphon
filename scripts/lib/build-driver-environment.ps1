# CARD-1506: adapter only; all policy values and opt-out live in the shared C# source.
# ASCII and C# 5 compatible for Windows PowerShell 5.1 callers.
function Set-AntiphonBuildDriverEnvironment {
    param([Diagnostics.ProcessStartInfo]$StartInfo,
          [bool]$Windows = ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT))
    if (-not $Windows) { return }
    if (-not ('Antiphon.Build.BuildDriverEnvironment' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'BuildDriverEnvironment.cs')
    }
    $policy = New-Object Antiphon.Build.BuildDriverEnvironment
    $policy.Apply($StartInfo.EnvironmentVariables, $Windows)
}

function Enter-AntiphonBuildDriverEnvironment {
    param([bool]$Windows = ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT))
    $restore = @{}
    if ($Windows) {
        $start = New-Object Diagnostics.ProcessStartInfo
        Set-AntiphonBuildDriverEnvironment -StartInfo $start -Windows $Windows
        $policy = New-Object Antiphon.Build.BuildDriverEnvironment
        $values = $policy.GetOverrides($start.EnvironmentVariables, $Windows)
        foreach ($name in $values.Keys) {
            $old = [Environment]::GetEnvironmentVariable($name)
            if ($old -cne $values[$name]) {
                $restore[$name] = $old
                [Environment]::SetEnvironmentVariable($name, $values[$name])
            }
        }
    }
    return $restore
}

function Exit-AntiphonBuildDriverEnvironment {
    param([hashtable]$Restore)
    if ($Restore) {
        foreach ($name in $Restore.Keys) {
            # PowerShell converts an untyped null to an empty string for this API.
            # Preserve absence as well as the caller's explicit empty value.
            if ($null -eq $Restore[$name]) {
                [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
            } else {
                [Environment]::SetEnvironmentVariable($name, $Restore[$name])
            }
        }
    }
}
