#requires -Version 5.1
# CARD-0487: TRX, diagnostic-discovery and coverage reconciliation.
# ASCII-only. Discovery is never execution evidence.

if ($script:AntiphonNightlyCoverageLoaded) { return }
$script:AntiphonNightlyCoverageLoaded = $true
# The core this scope already verified stays bound to its own loader (a pinned reader root has only some sources).
if (-not $script:NightlyCoreAvailable) { . (Join-Path $PSScriptRoot 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable }

function Invoke-NightlyProduceDiscovery {
    param(
        [string]$ExePath,
        [string]$OutputPath,
        [string]$DiagnosticDirectory,
        [string]$LogPath,
        [string]$WorkingDirectory,
        [hashtable]$Environment = $null,
        [int]$TimeoutMilliseconds = 600000,
        [string[]]$ArgumentPrefix = @(),
        [string]$AssemblyPath = ''
    )
    if ([string]::IsNullOrWhiteSpace($ExePath)) { throw 'missing assembly exe' }
    if ([string]::IsNullOrWhiteSpace($OutputPath)) { throw 'missing discovery output' }
    if ([string]::IsNullOrWhiteSpace($DiagnosticDirectory)) { throw 'missing diagnostic directory' }
    New-Item -ItemType Directory -Path $DiagnosticDirectory -Force | Out-Null
    $launchArgs = @()
    if ($ArgumentPrefix) {
        foreach ($prefixArg in @($ArgumentPrefix)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$prefixArg)) { $launchArgs += [string]$prefixArg }
        }
    }
    $launchArgs += @(
        '--list-tests',
        '--no-progress',
        '--no-ansi',
        '--diagnostic',
        '--diagnostic-output-directory',
        $DiagnosticDirectory
    )
    $run = Invoke-NightlyOwnedProcess -FilePath $ExePath -ArgumentList $launchArgs `
        -WorkingDirectory $WorkingDirectory -TimeoutMilliseconds $TimeoutMilliseconds `
        -Environment $Environment -LogPath $LogPath
    if ([bool]$run.TimedOut) { throw 'discovery list-tests timed out' }
    $diagPath = [Antiphon.Nightly.NightlyDiagnosticLog]::Latest($DiagnosticDirectory)
    if ([string]::IsNullOrWhiteSpace($diagPath)) {
        throw ('missing diagnostic discovery log in {0}' -f $DiagnosticDirectory)
    }
    $parsed = [Antiphon.Nightly.NightlyDiagnosticLog]::Read($diagPath, 'discovery')
    $nodes = @($parsed.Nodes)
    if ($nodes.Count -eq 0) { throw 'empty discovery' }
    $hashSource = $ExePath
    if (-not [string]::IsNullOrWhiteSpace($AssemblyPath)) { $hashSource = $AssemblyPath }
    $hash = [Antiphon.Nightly.NightlyDiagnosticLog]::AssemblyHash($hashSource)
    $doc = [Antiphon.Nightly.NightlyDiagnosticLog]::NewDiscoveryDocument($nodes, $hash)
    [Antiphon.Nightly.NightlyDiagnosticLog]::WriteDiscoveryDocument($OutputPath, $doc)
    return $doc
}

