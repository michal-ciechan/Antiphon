<#
.SYNOPSIS
    Inspect or recover crash-orphaned repository child journals (CARD-0448).
.DESCRIPTION
    Preview is the default. Execute requires an explicit confirmation that surviving
    descendants of the recorded children have exited. A dead/reused root PID alone
    cannot prove that. Live or ambiguous records are always retained. No process is
    killed and no Git lock, worktree or landing evidence is removed.
    Exit 0: no retained records; exit 3: busy, retained or unconfirmed evidence.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repository,
    [switch]$Execute,
    [switch]$ConfirmDescendantsExited
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-ChildState($Record, [string]$Common) {
    if ($Record.SchemaVersion -ne 1 -or
        $null -eq $Record.ProcessId -or $null -eq $Record.StartTicks -or
        $Record.ProcessId -le 0 -or $Record.StartTicks -le 0 -or
        -not [string]::Equals([IO.Path]::GetFullPath($Record.CommonDirectory).TrimEnd('\', '/'),
            $Common.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
        return 'unknown'
    }
    $recordedProcess = $null
    try {
        try { $recordedProcess = [Diagnostics.Process]::GetProcessById($Record.ProcessId) }
        catch [ArgumentException] { return 'dead' }
        if ($recordedProcess.HasExited) { return 'dead' }
        if ($recordedProcess.StartTime.ToUniversalTime().Ticks -ne $Record.StartTicks) { return 'reused' }
        return 'alive'
    } catch { return 'unknown' }
    finally { if ($null -ne $recordedProcess) { $recordedProcess.Dispose() } }
}

$lease = $null
try {
    $commonOutput = @(& git -C $Repository rev-parse --path-format=absolute --git-common-dir)
    if ($LASTEXITCODE -ne 0 -or $commonOutput.Count -ne 1) { throw 'Cannot resolve Git common directory.' }
    $common = [IO.Path]::GetFullPath($commonOutput[0])
    $admissionDirectory = Join-Path $common 'antiphon'
    [IO.Directory]::CreateDirectory($admissionDirectory) | Out-Null
    # Same exclusion as RepositoryMutationLease, including across linked worktrees.
    $lease = [IO.File]::Open((Join-Path $admissionDirectory 'landing.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $directory = Join-Path $admissionDirectory 'children'
    $retained = 0
    if (Test-Path -LiteralPath $directory) {
        $directoryInfo = Get-Item -LiteralPath $directory -Force
        if (-not $directoryInfo.PSIsContainer -or ($directoryInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Child journal directory is not a plain directory.'
        }
        foreach ($file in Get-ChildItem -LiteralPath $directory -File -Force) {
            $state = 'unknown'
            try {
                if ($file.Extension -ceq '.json' -and -not ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    $record = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                    $state = Get-ChildState $record $common
                }
                if ($state -in @('dead', 'reused') -and $Execute -and $ConfirmDescendantsExited) {
                    Remove-Item -LiteralPath $file.FullName -Force
                    Write-Output "recovered ($state): $($file.FullName)"
                    continue
                }
            } catch { $state = 'unknown' }
            $retained++
            Write-Output "retained ($state): $($file.FullName)"
        }
    }
    if ($retained -gt 0) {
        Write-Output 'After inspecting descendants, use -Execute -ConfirmDescendantsExited to recover only dead/reused records.'
        exit 3
    }
    exit 0
} catch {
    Write-Warning "Repository recovery refused: $($_.Exception.Message)"
    exit 3
} finally {
    if ($null -ne $lease) { $lease.Dispose() }
    # Never unlink landing.lock: its open handle, not its existence, owns exclusion.
}
