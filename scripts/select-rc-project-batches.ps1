#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineSha,
    [Parameter(Mandatory)][string]$CandidateSha,
    [string]$RepositoryRoot=(Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/rc-project-selection.ps1"
try {
    $selection=Get-RcProjectSelection $RepositoryRoot $BaselineSha $CandidateSha
    $text=ConvertTo-TestSelectionCanonicalJson $selection
    if ($OutputPath) {
        $stream=[IO.File]::Open($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $bytes=[Text.Encoding]::UTF8.GetBytes($text+"`n"); $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
    }
    Write-Output $text
} catch { [Console]::Error.WriteLine('project selection refused: '+$_.Exception.Message); exit 2 }
