#requires -Version 5.1
# The path helpers of test-phase-evidence.ps1 that need nothing else from it: the timing root a run
# resolves, and the file identity a TRX check asks for. A process that only needs these (nightly-run,
# release-cut) loads this file instead of the whole evidence library and its read warm-up; the evidence
# library loads it too, so both see the same definitions. ASCII-only; Windows PowerShell 5.1 parseable.

. (Join-Path $PSScriptRoot 'nightly-core.ps1')

function Resolve-QualificationTimingRoot {
    param([string]$TimingRoot, [string]$LogRoot, [string]$RunId)
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { return [string]$TimingRoot }
    if ([string]::IsNullOrWhiteSpace($LogRoot)) { return '' }
    if (-not [string]::IsNullOrWhiteSpace($RunId)) { return (Join-Path $LogRoot ('timing-' + $RunId)) }
    return (Join-Path $LogRoot 'timing')
}

function Initialize-TestPhaseLinuxIdentity {
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { return }
    if ('Antiphon.TestPhase.FileIdentity' -as [type]) { return }
    # Antiphon.Nightly carries these types (Antiphon.Nightly/TestPhaseLinuxIdentity.cs); without it (Windows PowerShell 5.1,
    # ANTIPHON_NIGHTLY_CORE=off) the same source is compiled here.
    if (Test-NightlyCoreAvailable) { return }
    Add-Type -Path (Join-Path $PSScriptRoot 'Antiphon.Nightly/TestPhaseLinuxIdentity.cs')
}

function Get-TestPhasePathIdentity {
    param([string]$Path)
    $missing = [pscustomobject]@{ Kind = 'missing'; Size = [int64]0 }
    if ([string]::IsNullOrWhiteSpace($Path)) { return $missing }
    $queued = Get-NightlyEvidencePendingLength $Path
    if ($queued -ge 0) { return [pscustomobject]@{ Kind = 'regular'; Size = [int64]$queued } }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.TestPhaseFiles]::PathIdentity($Path) }
        if (-not (Test-Path -LiteralPath $Path)) { return $missing }
        try { $attrs = [IO.File]::GetAttributes($Path) } catch {
            return [pscustomobject]@{ Kind = 'unreadable'; Size = [int64]0 }
        }
        $directory = [IO.FileAttributes]::Directory
        $reparse = [IO.FileAttributes]::ReparsePoint
        if (($attrs -band $directory) -eq $directory) { return [pscustomobject]@{ Kind = 'unsupported'; Size = [int64]0 } }
        if (($attrs -band $reparse) -eq $reparse) { return [pscustomobject]@{ Kind = 'unsupported'; Size = [int64]0 } }
        $length = [int64]0
        try { $length = [int64](Get-Item -LiteralPath $Path).Length } catch { $length = [int64]0 }
        return [pscustomobject]@{ Kind = 'regular'; Size = $length }
    }
    Initialize-TestPhaseLinuxIdentity
    if (-not ('Antiphon.TestPhase.FileIdentity' -as [type])) {
        return [pscustomobject]@{ Kind = 'unreadable'; Size = [int64]0 }
    }
    $stat = New-Object Antiphon.TestPhase.LinuxStat
    $code = [Antiphon.TestPhase.FileIdentity]::LStat($Path, [ref]$stat)
    if ($code -ne 0) {
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        if ($err -eq 2) { return $missing }
        return [pscustomobject]@{ Kind = 'unreadable'; Size = [int64]0 }
    }
    $kind = ([uint32]$stat.Mode) -band 61440
    if ($kind -eq 32768) { return [pscustomobject]@{ Kind = 'regular'; Size = [int64]$stat.Size } }
    return [pscustomobject]@{ Kind = 'unsupported'; Size = [int64]$stat.Size }
}
