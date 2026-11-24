# CARD-1506: adapter only; all policy values and opt-out live in the shared C# source.
# ASCII and C# 5 compatible for Windows PowerShell 5.1 callers. Its callers are the still-PowerShell ones:
# scripts/run-checkpoint.ps1 and scripts/test-checkpoint-bounded-reader.ps1 (W5, owner-gated), lib/nightly-owned-process.ps1
# and lib/c487-nightly-tests.ps1 (W1). build-slot moved to C# (unit 34 of
# docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md), which prepares its children with BuildDriverEnvironment and
# LeaseTempDirectory directly; the process-wide Enter/Exit pair went with it.
function Set-AntiphonBuildDriverEnvironment {
    param([Diagnostics.ProcessStartInfo]$StartInfo,
          [bool]$Windows = ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT),
          [int]$MaxCpuCount = 0)
    if (-not $Windows -and (-not $StartInfo.EnvironmentVariables.ContainsKey('ANTIPHON_LINUX_COMPILER_POLICY') -or
        $StartInfo.EnvironmentVariables['ANTIPHON_LINUX_COMPILER_POLICY'] -cne '1')) { return }
    if (-not ('Antiphon.Build.BuildDriverEnvironment' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'BuildDriverEnvironment.cs')
    }
    $policy = New-Object Antiphon.Build.BuildDriverEnvironment
    if ($Windows) { $policy.Apply($StartInfo.EnvironmentVariables, $Windows) }
    else { $policy.Apply($StartInfo, $MaxCpuCount) }
}

function New-AntiphonLeaseTemp {
    # CARD-1981. Linux only. A compile or stat failure returns null and the build keeps the ambient temp.
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { return $null }
    try {
        $type = $null
        foreach ($assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {
            $found = $assembly.GetType('Antiphon.Build.LeaseTempDirectory', $false)
            if ($null -ne $found) { $type = $found; break }
        }
        if ($null -eq $type) {
            Add-Type -Path (Join-Path $PSScriptRoot 'LeaseTempDirectory.cs')
            $type = [Antiphon.Build.LeaseTempDirectory]
        }
        return $type::TryCreate()
    } catch {
        return $null
    }
}
