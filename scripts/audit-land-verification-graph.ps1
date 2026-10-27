# CARD-1483 S5: author-time approval of the land build graph.
# -Update requires the emulation pre-check AND real MSBuild evaluation, with no restore.
# -Check verifies the pre-check, manifest and digest/pins record, and evaluates on SDK mismatch.
# The recorded SDK must be a stable release selectable by the committed latestMinor policy.
# CARD-1528: -Update also refuses unobserved filesystem/unsupported evaluation functions.
# CARD-1540: timestamp metadata and unreviewed environment properties refuse; knob reads need exact pins.
# Land itself only compares the definition digest; it never runs this evaluator.
# Build the DLL through scripts/build-slot.ps1, with -c Release and
# --property:OutputPath=bin-land-audit/Release/net10.0/, or supply a built -ToolPath.
# No implicit build and no pre-check-only mode. Any evaluation error refuses (exit 1).
# ASCII-only and Windows PowerShell 5.1 compatible.
[CmdletBinding()]
param([switch]$Check, [switch]$Update, [string]$ToolPath)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$RecordDirectory = Join-Path $RepoRoot 'server/Application/Services'
$ManifestRelative = 'server/Application/Services/LandVerificationBuildGraph.manifest.txt'
$EvaluationRelative = 'server/Application/Services/LandVerificationBuildGraph.evaluation.txt'
$PinsRelative = 'server/Application/Services/LandVerificationBuildGraph.pins.txt'
$ManifestPath = Join-Path $RepoRoot $ManifestRelative
$EvaluationPath = Join-Path $RepoRoot $EvaluationRelative
$StableSdkPattern = '\A[1-9][0-9]*\.(?:0|[1-9][0-9]*)\.[1-9][0-9]{2,}\z'

function ConvertTo-NativeArgument([string]$Value) {
    # ProcessStartInfo.Arguments is also available on Windows PowerShell 5.1.
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Invoke-AuditChild([string]$Executable, [string[]]$Arguments) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
    $start.WorkingDirectory = $RepoRoot
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # dotnet run/test parents must not steer SDK discovery in the author-time evaluator.
    foreach ($key in @($start.EnvironmentVariables.Keys)) {
        if ($key.StartsWith('MSBuild', [StringComparison]::OrdinalIgnoreCase)) {
            $start.EnvironmentVariables.Remove($key)
        }
    }
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw "could not start $Executable" }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(300000)) { throw "subprocess exceeded 300 s: $Executable" }
        $remaining = [int][Math]::Max(1, 300000 - $clock.ElapsedMilliseconds)
        if (-not [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($stdout, $stderr), $remaining)) {
            throw "subprocess output exceeded 300 s: $Executable"
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
    $run = Invoke-AuditChild 'git' $Arguments
    if ($run.Code -ne 0) { throw ('git failed: ' + $run.Text.Trim()) }
    return $run.Text.Trim()
}

function Assert-SelectableSdk([string]$Version) {
    $candidate = $null
    if ($Version -cnotmatch $StableSdkPattern -or -not [version]::TryParse($Version, [ref]$candidate)) {
        throw 'evaluation record has invalid SDK provenance'
    }
    # Admit stable SDK releases under the committed policy only. A new policy or
    # a prerelease pin needs explicit qualification rather than a permissive fallback.
    $policy = (Get-GitValue @('show', 'HEAD:global.json') | ConvertFrom-Json).sdk
    $minimum = $null
    if ($policy.version -cnotmatch $StableSdkPattern -or
        -not [version]::TryParse($policy.version, [ref]$minimum) -or
        $policy.rollForward -cne 'latestMinor') {
        throw 'SDK provenance requires a stable pin with rollForward latestMinor'
    }
    # latestMinor can advance minor, feature band and patch, but never major or
    # select a version below the pin (including an earlier patch in its band).
    if ($candidate.Major -ne $minimum.Major -or $candidate -lt $minimum) {
        throw 'evaluation record SDK provenance is outside committed roll-forward policy'
    }
}

function Assert-HeadSource([string]$Head) {
    if ((Get-GitValue @('rev-parse', 'HEAD')) -cne $Head) { throw 'HEAD changed during audit' }
    # Evaluation reads disk, while the pre-check hashes HEAD. Bind the two.
    # Generated records are the only tracked paths permitted to differ.
    $run = Invoke-AuditChild 'git' @('diff', '--quiet', 'HEAD', '--', '.',
        (':(exclude)' + $ManifestRelative), (':(exclude)' + $EvaluationRelative))
    if ($run.Code -ne 0) { throw 'source differs from HEAD; commit source and pins before auditing' }
}

function Assert-EvaluatedGraph([string]$EvaluatorPath) {
    if ([string]::IsNullOrEmpty($EvaluatorPath)) {
        $EvaluatorPath = Join-Path $RepoRoot 'tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll'
    }
    if (-not [System.IO.File]::Exists($EvaluatorPath)) { throw ('built evaluator missing: ' + $EvaluatorPath) }
    $evaluation = Invoke-AuditChild 'dotnet' @([System.IO.Path]::GetFullPath($EvaluatorPath), '--repo', $RepoRoot, '--solution', 'Antiphon.sln')
    if ($evaluation.Text.Length -gt 0) { Write-Output $evaluation.Text.TrimEnd() }
    if ($evaluation.Code -ne 0) { throw ('land graph evaluation refused (exit ' + $evaluation.Code + ')') }
}

function Publish-Record([string]$Source, [string]$Destination) {
    if ([System.IO.File]::Exists($Destination)) {
        [System.IO.File]::Replace($Source, $Destination, [NullString]::Value)
    } else {
        [System.IO.File]::Move($Source, $Destination)
    }
}

$temporaryManifest = $null
$temporaryEvaluation = $null
try {
    if ($Check -and $Update) { throw 'Specify -Check or -Update, not both.' }
    $head = Get-GitValue @('rev-parse', 'HEAD')
    Assert-HeadSource $head
    $powershell = (Get-Process -Id $PID).Path
    $precheck = Join-Path $PSScriptRoot 'audit-land-verification-precheck.ps1'
    $arguments = @('-NoProfile', '-NonInteractive', '-File', $precheck)
    if ($Update) {
        [void][System.IO.Directory]::CreateDirectory($RecordDirectory)
        $temporaryManifest = Join-Path $RecordDirectory ('.land-manifest-' + [Guid]::NewGuid().ToString('N'))
        $arguments += @('-Update', '-ManifestPath', $temporaryManifest)
    } else { $arguments += '-Check' }
    $pre = Invoke-AuditChild $powershell $arguments
    if ($pre.Text.Length -gt 0) { Write-Output $pre.Text.TrimEnd() }
    if ($pre.Code -ne 0) { throw 'land graph pre-check refused' }

    $pins = Get-GitValue @('rev-parse', ('HEAD:' + $PinsRelative))
    if ($pins -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') { throw 'pins blob SHA is invalid' }
    $sdkRun = Invoke-AuditChild 'dotnet' @('--version')
    if ($sdkRun.Code -ne 0 -or $sdkRun.Text.Trim() -cnotmatch $StableSdkPattern) {
        throw ('SDK resolution failed: ' + $sdkRun.Text.Trim())
    }
    $sdk = $sdkRun.Text.Trim()
    $selectedManifest = $ManifestPath
    if ($Update) { $selectedManifest = $temporaryManifest }
    $manifest = [System.IO.File]::ReadAllLines($selectedManifest)
    $expected = @($manifest[0], $sdk, $pins)

    if ($Update) {
        Assert-EvaluatedGraph $ToolPath
        Assert-HeadSource $head
        $temporaryEvaluation = Join-Path $RecordDirectory ('.land-evaluation-' + [Guid]::NewGuid().ToString('N'))
        [System.IO.File]::WriteAllText($temporaryEvaluation, ($expected -join "`n") + "`n", [System.Text.Encoding]::ASCII)
        # Publish the manifest last: an interrupted write cannot approve a new graph early.
        Publish-Record $temporaryEvaluation $EvaluationPath
        Publish-Record $temporaryManifest $ManifestPath
    } else {
        if (-not [System.IO.File]::Exists($EvaluationPath)) { throw 'evaluation record missing; run -Update' }
        $record = [System.IO.File]::ReadAllLines($EvaluationPath)
        if ($record.Length -ne 3) { throw 'evaluation record must be three lines' }
        foreach ($i in @(0, 2)) {
            if ($record[$i] -cne $expected[$i]) { throw ('evaluation record mismatch: ' + @('digest', 'SDK', 'pins SHA')[$i]) }
        }
        # global.json is part of the exact graph digest and owns SDK selection/roll-forward.
        # A different permitted SDK needs its own evaluation: its trusted SDK tree
        # can introduce imports, items or globs absent from the author's SDK.
        Assert-SelectableSdk $record[1]
        if ($record[1] -cne $sdk) { Assert-EvaluatedGraph $ToolPath }
        Assert-HeadSource $head
    }
} catch {
    [Console]::Error.WriteLine('audit refused: ' + $_.Exception.Message)
    exit 1
} finally {
    foreach ($path in @($temporaryManifest, $temporaryEvaluation)) {
        if ($path -and [System.IO.File]::Exists($path)) { [System.IO.File]::Delete($path) }
    }
}
exit 0
