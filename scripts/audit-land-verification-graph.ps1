# CARD-1483 S5: author-time approval of the land build graph.
# -Update requires the emulation pre-check AND real MSBuild evaluation, with no restore.
# -Check verifies the pre-check, manifest and digest/pins record, and always evaluates.
# The recorded SDK must be a stable release selectable by the committed latestMinor policy.
# CARD-1528: -Update also refuses unobserved filesystem/unsupported evaluation functions.
# CARD-1540: timestamp metadata and unreviewed environment properties refuse; knob reads need exact pins.
# Land itself only compares the definition digest; it never runs this evaluator.
# -LandRefresh is only for scripts/land-graph-land-refresh.ps1, and only together
# with -Update. It lets that script stage pins beside the manifest for one
# land-graph commit. Without -Update the script refuses. A bare -Update still
# refuses with "commit source and pins before auditing".
#
# The audit itself is compiled C#: tools/Antiphon.LandGraphAudit.Core/LandGraphAuditGate.cs, a port of this
# script's former body with the same steps in the same order, the same refusal messages and exit codes, and the
# canary removal of the former scripts/lib/land-graph-canary-removal.ps1 (LandGraphCanaryRemoval.cs). This
# wrapper resolves the built tool and runs its 'audit' command, which writes straight to this script's output
# and error streams; the pre-check runs inside that process and the MSBuild evaluation is its child process.
# The tool's exit 0 is the only approval this script passes on. Exit 1 is passed on as is; any other exit
# (an older build without 'audit' exits 64) and a tool that does not start refuse with exit 1.
# The default DLL (bin-land-audit/Release/net10.0) is rebuilt automatically when it is missing or its
# sources changed (scripts/lib/land-audit-tool.ps1); a failed rebuild refuses. A supplied -ToolPath is used
# as given. No pre-check-only mode. Any evaluation error refuses (exit 1).
# When the tool is current, the tool runs the whole audit. Otherwise this script first runs the audit's
# opening checks itself (the switches, HEAD, and the source check below), because the audit always ran them
# before resolving the tool: a dirty tree refuses before any rebuild, and a missing tool is reported only after
# them. It then passes the HEAD it read to the tool, which starts at the pre-check.
# The evaluator's own clocks bound each solution pass and the canary copy. The tool's wait is the evaluator's
# ceiling (phase total plus EvaluationBudget.StartupGraceSeconds). -EvaluationBudgetSeconds overrides that wait
# and is exact: it never skips evaluation and never approves by itself. A killed evaluator's canary is removed
# from the recorded path on the kill, tool-watchdog, timeout and exception paths; that removal is not a
# parameter. A canary left behind when this process dies is not swept on the next run. The tool exits when
# this script's process does.
# ASCII-only and Windows PowerShell 5.1 compatible.
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Update,
    [switch]$LandRefresh,
    [string]$ToolPath,
    [int]$EvaluationBudgetSeconds = 0)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/land-audit-tool.ps1')

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ManifestRelative = 'server/Application/Services/LandVerificationBuildGraph.manifest.txt'
$EvaluationRelative = 'server/Application/Services/LandVerificationBuildGraph.evaluation.txt'
$PinsRelative = 'server/Application/Services/LandVerificationBuildGraph.pins.txt'

function ConvertTo-NativeArgument([string]$Value) {
    # ProcessStartInfo.Arguments is also available on Windows PowerShell 5.1.
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function New-AuditStart([string]$Executable, [string[]]$Arguments) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
    $start.WorkingDirectory = $RepoRoot
    $start.UseShellExecute = $false
    # dotnet run/test parents must not steer SDK discovery in the author-time evaluator.
    foreach ($key in @($start.EnvironmentVariables.Keys)) {
        if ($key.StartsWith('MSBuild', [StringComparison]::OrdinalIgnoreCase)) {
            $start.EnvironmentVariables.Remove($key)
        }
    }
    return $start
}

# The audit's git calls before the tool is resolved (the same calls, waits and messages as LandGraphAuditGate).
function Invoke-AuditGit([string[]]$Arguments) {
    $TimeoutMs = 300000
    $start = New-AuditStart 'git' $Arguments
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw 'could not start git' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMs)) { throw "subprocess exceeded $([int]($TimeoutMs / 1000)) s: git" }
        $remaining = [int][Math]::Max(1, $TimeoutMs - $clock.ElapsedMilliseconds)
        if (-not [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($stdout, $stderr), $remaining)) {
            throw "subprocess output exceeded $([int]($TimeoutMs / 1000)) s: git"
        }
        return [pscustomobject]@{ Code = $process.ExitCode; Text = $stdout.Result + $stderr.Result }
    } finally {
        try {
            if (-not $process.HasExited) {
                try { $process.Kill($true) } catch { $process.Kill() }
                [void]$process.WaitForExit(10000)
            }
        } finally { $process.Dispose() }
    }
}

function Get-GitValue([string[]]$Arguments) {
    $run = Invoke-AuditGit $Arguments
    if ($run.Code -ne 0) { throw ('git failed: ' + $run.Text.Trim()) }
    return $run.Text.Trim()
}

function Assert-HeadSource([string]$Head, [bool]$AllowPendingPins = $false) {
    if ((Get-GitValue @('rev-parse', 'HEAD')) -cne $Head) { throw 'HEAD changed during audit' }
    # Evaluation reads disk, while the pre-check hashes HEAD. Bind the two.
    # Generated records are the only tracked paths permitted to differ.
    # Pending pins are allowed only for scripts/land-graph-land-refresh.ps1.
    $exclude = @(':(exclude)' + $ManifestRelative, (':(exclude)' + $EvaluationRelative))
    if ($AllowPendingPins) { $exclude += (':(exclude)' + $PinsRelative) }
    $run = Invoke-AuditGit (@('diff', '--quiet', 'HEAD', '--', '.') + $exclude)
    if ($run.Code -ne 0) { throw 'source differs from HEAD; commit source and pins before auditing' }
}

function Invoke-LandAuditTool([string]$Tool, [string]$Head) {
    $invariant = [System.Globalization.CultureInfo]::InvariantCulture
    $arguments = @([System.IO.Path]::GetFullPath($Tool), 'audit', '--repo', $RepoRoot, '--tool-path', $Tool,
        '--evaluation-budget-seconds', $EvaluationBudgetSeconds.ToString($invariant), '--parent-pid', $PID.ToString($invariant))
    if ($Check) { $arguments += '--check' }
    if ($Update) { $arguments += '--update' }
    if ($LandRefresh) { $arguments += '--land-refresh' }
    if (-not [string]::IsNullOrEmpty($Head)) { $arguments += @('--head', $Head) }
    # No redirection: the tool writes to this script's own output and error streams.
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = New-AuditStart 'dotnet' $arguments
    try {
        if (-not $process.Start()) { throw 'could not start dotnet' }
        $process.WaitForExit()
        return $process.ExitCode
    } catch {
        [Console]::Error.WriteLine('audit refused: land graph audit tool did not start: ' + $_.Exception.Message)
        return $null
    } finally {
        $process.Dispose()
    }
}

$tool = $null
try { $tool = Resolve-LandAuditTool $RepoRoot $ToolPath -NoRebuild } catch { $tool = $null }
if ($null -ne $tool -and $tool.ContainsKey('Path')) {
    $code = Invoke-LandAuditTool ([string]$tool.Path) ''
} else {
    try {
        if ($Check -and $Update) { throw 'Specify -Check or -Update, not both.' }
        if ($LandRefresh -and -not $Update) { throw '-LandRefresh requires -Update.' }
        $head = Get-GitValue @('rev-parse', 'HEAD')
        Assert-HeadSource $head ($Update.IsPresent -and $LandRefresh.IsPresent)
        $tool = Resolve-LandAuditTool $RepoRoot $ToolPath
        if ($tool.ContainsKey('Error')) {
            [Console]::Error.WriteLine([string]$tool.Error)
            throw 'land graph pre-check refused'
        }
    } catch {
        [Console]::Error.WriteLine('audit refused: ' + $_.Exception.Message)
        exit 1
    }
    $code = Invoke-LandAuditTool ([string]$tool.Path) $head
}
if ($null -eq $code) { exit 1 }
if ($code -ne 0) {
    if ($code -ne 1) {
        [Console]::Error.WriteLine('audit refused: land graph audit tool failed (exit ' + $code + ')')
    }
    exit 1
}
exit 0
