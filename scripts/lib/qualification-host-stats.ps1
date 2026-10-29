#requires -Version 5.1
# CARD-1514 A4. Qualification host samples. ASCII-only library.
# Not an unattended nightly census entry. Desktop 5.1 and .NET 8 never load the probe.

function Test-QualificationHostRuntime {
    param(
        [string]$Edition,
        [string]$FrameworkDescription,
        [scriptblock]$Loader
    )
    $major = -1
    $framework = [string]$FrameworkDescription
    $match = [regex]::Match($framework, '\.NET ([0-9]+)')
    if ($match.Success) { $major = [int]$match.Groups[1].Value }
    $core = ([string]$Edition) -eq 'Core'
    $frameworkHost = $framework.IndexOf('Framework', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    $compatible = $core -and (-not $frameworkHost) -and ($major -ge 9)
    # Reading the loader does not invoke it. Version alone is not measurement admission.
    $null = $Loader
    if (-not $compatible) {
        return [pscustomobject]@{
            decision = 'incompatible-runtime'
            measurementAdmission = $false
            diagnosticComplete = $true
            loadReason = ''
        }
    }
    return [pscustomobject]@{
        decision = 'compatible'
        measurementAdmission = $false
        diagnosticComplete = $true
        loadReason = ''
    }
}

function Invoke-QualificationHostSampler {
    param(
        [scriptblock]$Probe,
        [int]$SampleCount = 2,
        [scriptblock]$OwnerCompleted
    )
    $samples = New-Object System.Collections.Generic.List[object]
    $index = 0
    while ($index -lt $SampleCount) {
        $index++
        $sample = $null
        $failed = $false
        try {
            $sample = & $Probe
        } catch {
            $failed = $true
        }
        if ($failed) {
            $sample = [pscustomobject]@{
                at = ''
                cpuPercent = $null
                cores = $null
                memoryAvailableBytes = $null
                memoryCommittedBytes = $null
                diskQueue = $null
                reason = 'sample-failed'
            }
        }
        if ($null -eq $sample) { break }
        $samples.Add($sample)
    }
    if ($null -ne $OwnerCompleted) { & $OwnerCompleted }
    return [pscustomobject]@{ samples = @($samples.ToArray()) }
}

function New-QualificationHostStatsProvider {
    param(
        [string]$AssemblyPath,
        [scriptblock]$Loader,
        [string]$Edition,
        [string]$FrameworkDescription
    )
    if ([string]::IsNullOrWhiteSpace($Edition)) { $Edition = [string]$PSVersionTable.PSEdition }
    if ([string]::IsNullOrWhiteSpace($FrameworkDescription)) {
        $FrameworkDescription = [string][Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    }
    $gate = Test-QualificationHostRuntime -Edition $Edition -FrameworkDescription $FrameworkDescription -Loader $Loader
    if ([string]$gate.decision -ne 'compatible') {
        return [pscustomobject]@{
            decision = [string]$gate.decision
            measurementAdmission = $false
            diagnosticComplete = $true
            probe = $null
            loadReason = ''
        }
    }
    $loaded = $null
    $loadReason = ''
    if ($null -ne $Loader -and -not [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        try {
            $loaded = & $Loader
        } catch {
            $loaded = $null
            $loadReason = 'loader-failed'
        }
    }
    $admitted = $false
    if ($null -ne $loaded) { $admitted = $true }
    return [pscustomobject]@{
        decision = 'compatible'
        measurementAdmission = [bool]$admitted
        diagnosticComplete = $true
        probe = $loaded
        loadReason = [string]$loadReason
    }
}
