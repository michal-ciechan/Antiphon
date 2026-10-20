#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$ProbeDirectory,
    [Parameter(Mandatory)][string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/nightly-probe-supervisor.ps1')
$probe = (Resolve-Path -LiteralPath $ProbeDirectory).Path
$results = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $results) { throw 'Capture requires a fresh results directory.' }
$dlls = @(Get-ChildItem -LiteralPath (Join-Path $probe 'bin-c1375-probe') -Filter Probe.dll -Recurse -File)
if ($dlls.Count -ne 1) { throw 'Expected exactly one built Probe.dll in bin-c1375-probe.' }
$dll = $dlls[0].FullName
$assets = Get-Content -LiteralPath (Join-Path $probe 'obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($package in @('TUnit/1.44.0', 'Microsoft.Testing.Platform/2.2.2')) {
    if ($null -eq $assets.libraries.PSObject.Properties[$package]) { throw "Missing pinned package $package" }
}
$versions = @{}
foreach ($assembly in @('TUnit.Core', 'TUnit.Engine', 'Microsoft.Testing.Platform')) {
    $versions[$assembly] = [Reflection.AssemblyName]::GetAssemblyName(
        (Join-Path $dlls[0].DirectoryName "$assembly.dll")).Version.ToString()
}
New-Item -ItemType Directory -Path $results | Out-Null
$source = (& git rev-parse HEAD).Trim()
foreach ($method in @('TimeoutProbe', 'CancelledProbe')) {
    $directory = Join-Path $results $method
    New-Item -ItemType Directory -Path $directory | Out-Null
    $argv = @('exec', $dll, '--treenode-filter', "/*/*/C1375Producer*/$method*",
        '--detailed-stacktrace', '--report-trx', '--results-directory', $directory,
        '--diagnostic', '--diagnostic-output-directory', (Join-Path $directory 'diag'))
    $receipt = Invoke-NightlyProbeSupervisor -FilePath 'dotnet' -ArgumentList $argv -WorkingDirectory $probe
    $receipt | Add-Member sourceSha $source
    $receipt | Add-Member assemblyVersions $versions
    $receipt | Add-Member dllSha256 (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    $receipt | Add-Member packages @('TUnit/1.44.0', 'Microsoft.Testing.Platform/2.2.2')
    $receipt | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $directory 'receipt.json') -Encoding utf8
    if ($receipt.expired -or -not $receipt.cleanupSuccess -or $receipt.exitCode -eq 0) {
        throw "Probe $method refused: expiry, cleanup or exit; see retained receipt."
    }
    $trxs = @(Get-ChildItem -LiteralPath $directory -Filter '*.trx' -File)
    if ($trxs.Count -ne 1) { throw "Probe $method did not write exactly one TRX." }
    [xml]$trx = Get-Content -LiteralPath $trxs[0].FullName -Raw
    $rows = @($trx.TestRun.Results.UnitTestResult)
    $counters = $trx.TestRun.ResultSummary.Counters
    if ($rows.Count -ne 1 -or $rows[0].outcome -cne 'Failed' -or
        $counters.total -ne '1' -or $counters.failed -ne '1' -or $counters.passed -ne '0' -or
        $counters.notExecuted -ne '0') { throw "Probe $method native result mismatch." }
    $diagnostics = @(Get-ChildItem -LiteralPath (Join-Path $directory 'diag') -File -Recurse)
    $text = ($diagnostics | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
    $state = if ($method -eq 'TimeoutProbe') { 'Timeout' } else { 'Cancelled' }
    if ([regex]::Matches($text, "$($state)TestNodeStateProperty \{").Count -ne 1 -or
        $text -notmatch "Version: '1\.44\.0\.0'" -or $text -notmatch 'Version: 2\.2\.2') {
        throw "Probe $method native state/version mismatch."
    }
    Write-Host "CAPTURE $method state=$state executed=1 passed=0 failed=1 skipped=0 exit=$($receipt.exitCode) expired=false cleanup=true"
}
