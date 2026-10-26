# Read-only diagnostics for the native qualification owner; no build or test launch.
# Error content is classified and hashed, never printed verbatim.
#requires -Version 7.0
param([Parameter(Mandatory = $true)][string]$TestOutputDirectory, [string]$TrxPath = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Get-FileFact([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return [ordered]@{ path = $Path; exists = $false } }
    $item = Get-Item -LiteralPath $Path -Force
    $linked = ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    return [ordered]@{ path = $item.FullName; exists = $true; linked = $linked; attributes = [string]$item.Attributes
        sha256 = $(if (-not $linked -and -not $item.PSIsContainer) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }) }
}
$output = [IO.Path]::GetFullPath($TestOutputDirectory)
$commands = foreach ($name in @('pwsh', 'powershell', 'git', 'bash')) {
    $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    [ordered]@{ name = $name; path = $(if ($command) { $command.Source }) }
}
$sources = foreach ($relative in @('scripts/lib/c487-harness.ps1', 'scripts/test-release-gate.ps1',
        'scripts/test-rc-project-selection.ps1', 'scripts/test-run-checkpoint.ps1',
        'scripts/fixtures/c1339-release-git.ps1', 'scripts/card.ps1',
        'tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs', 'tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs')) {
    $path = Join-Path $repo $relative
    $fact = Get-FileFact $path
    if ($fact.exists -and -not $fact.linked) { $fact.crlf = [regex]::Matches([IO.File]::ReadAllText($path), '\r\n').Count }
    $fact
}
$build = foreach ($path in @((Join-Path $output 'Antiphon.Tests.exe'), (Join-Path $output 'Antiphon.Tests.dll'),
        (Join-Path $output 'checkpoint-build-source.json'), (Join-Path (Split-Path -Parent $output) 'checkpoint-build-source.json'))) {
    Get-FileFact $path
}
$failures = @()
if ($TrxPath) {
    $document = [xml][IO.File]::ReadAllText([IO.Path]::GetFullPath($TrxPath))
    $definitions = @{}
    foreach ($definition in $document.SelectNodes('//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]')) {
        $definitions[[string]$definition.id] = [string]$definition.TestMethod.className
    }
    $failures = @(foreach ($result in $document.SelectNodes('//*[local-name()="UnitTestResult"]')) {
        if ([string]$result.outcome -cne 'Failed') { continue }
        $message = [string]$result.Output.ErrorInfo.Message
        [ordered]@{ class = $definitions[[string]$result.testId]; name = [string]$result.testName; duration = [string]$result.duration
            messageSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($message)))
            signals = [ordered]@{
                cleanup = $message -match 'ScriptHarness cleanup failed|owned-path deletion'
                readOnlyOrAccessDenied = $message -match '(?i)read.only|UnauthorizedAccess|access.*denied'
                checkpointSourceStamp = $message -match 'c1050-child-source'
                childApphost = $message -match 'c1050-child-apphost'
                checkpointOutputOrHolding = $message -match 'pre-existing-build-source|harness ended before parentObserved=holding'
                nativeProcessOwnership = $message -match 'process custody unavailable|AssignProcessToJobObject|CreateProcessW'
            }
        }
    })
}
[ordered]@{ nativeQualification = 'pending'; utc = [DateTimeOffset]::UtcNow.ToString('O'); powerShellVersion = $PSVersionTable.PSVersion.ToString()
    psEdition = [string]$PSVersionTable.PSEdition; os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    directorySeparator = [string][IO.Path]::DirectorySeparatorChar; repoRoot = $repo
    commands = @($commands); build = @($build); sources = @($sources); failures = @($failures)
} | ConvertTo-Json -Depth 8
