# CARD-1483 S5: internal emulation pre-check. This is not evaluation approval.
# The production entry point is audit-land-verification-graph.ps1, which requires both checks.
# The rules (CARD-1385, 1390, 1400, 1409, 1483 and 1613) are compiled C#:
# tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs, a port of this script's former body
# with the same decisions, messages and exit codes. This wrapper runs the 'precheck' command of the
# built LandGraphAudit tool, the same DLL the graph script evaluates with, and passes its output and
# exit code through. A missing tool, a tool that does not start, or any exit code other than 0, 1 or 2
# refuses with exit 1; nothing here approves on its own.
# ASCII-only: must parse under Windows PowerShell 5.1.
#
# Usage:
#   pwsh -NoProfile -File scripts/audit-land-verification-precheck.ps1 [-Check] [-ToolPath <dll>]
#   pwsh -NoProfile -File scripts/audit-land-verification-precheck.ps1 -Update [-ManifestPath <file>] [-ToolPath <dll>]
#
# -Check is the default. It prints digest=<HEX> count=<N> and exits 0 only
# when both manifest lines match. -Update rewrites the manifest from HEAD.
# Exit 1 is a mismatch, a docs-input refusal, or bad usage. Exit 2 is a git failure.
# -ToolPath defaults to tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll.
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Update,
    [string]$ManifestPath,
    [string]$ToolPath
)

$ErrorActionPreference = 'Stop'

function Exit-Audit([int]$Code, [string]$Message) {
    if (-not [string]::IsNullOrEmpty($Message)) {
        [Console]::Error.WriteLine($Message)
    }
    exit $Code
}

function ConvertTo-NativeArgument([string]$Value) {
    # ProcessStartInfo.Arguments is also available on Windows PowerShell 5.1.
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

if ($Check.IsPresent -and $Update.IsPresent) {
    Exit-Audit 1 'Specify -Check or -Update, not both.'
}

if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Exit-Audit 1 'script root is unknown'
}
$RepoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($ToolPath)) {
    $ToolPath = [System.IO.Path]::Combine($RepoRoot, 'tools', 'Antiphon.LandGraphAudit', 'bin-land-audit', 'Release', 'net10.0', 'Antiphon.LandGraphAudit.dll')
}
if (-not [System.IO.File]::Exists($ToolPath)) {
    Exit-Audit 1 ('built evaluator missing: ' + $ToolPath)
}

$arguments = @([System.IO.Path]::GetFullPath($ToolPath), 'precheck', '--repo', $RepoRoot)
if ($Update.IsPresent) { $arguments += '--update' } else { $arguments += '--check' }
if (-not [string]::IsNullOrEmpty($ManifestPath)) { $arguments += @('--manifest-path', $ManifestPath) }

# No WorkingDirectory: the tool resolves a relative -ManifestPath against this process's directory,
# as this script's own .NET file calls did.
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = 'dotnet'
$start.Arguments = (($arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardOutputEncoding = New-Object System.Text.UTF8Encoding $false
$start.StandardErrorEncoding = New-Object System.Text.UTF8Encoding $false
$process = New-Object System.Diagnostics.Process
$process.StartInfo = $start
try {
    $started = $process.Start()
}
catch {
    Exit-Audit 1 ('land graph pre-check tool did not start: ' + $_.Exception.Message)
}
if (-not $started) { Exit-Audit 1 'land graph pre-check tool did not start' }
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
$code = $process.ExitCode
$process.Dispose()

$text = $stdout.Result.TrimEnd()
if ($text.Length -gt 0) { Write-Output $text }
[Console]::Error.Write($stderr.Result)
if ($code -ne 0 -and $code -ne 1 -and $code -ne 2) {
    Exit-Audit 1 ('land graph pre-check tool failed (exit ' + $code + ')')
}
exit $code
