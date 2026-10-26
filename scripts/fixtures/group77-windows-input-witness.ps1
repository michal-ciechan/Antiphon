# Source-bound Windows admission witnesses; no daemon or native process is launched.
param(
    [ValidateSet('admission','bash')][string]$Case,
    [ValidatePattern('^$|^[0-9a-f]{40}$')][string]$SourceRef = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Read-Source([string]$Relative) {
    if ($SourceRef) {
        $lines = & git -C $repo show ($SourceRef + ':' + $Relative)
        if ($LASTEXITCODE -ne 0) { throw "Cannot read source $Relative" }
        return $lines -join "`n"
    }
    return [IO.File]::ReadAllText((Join-Path $repo $Relative))
}
function Require([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
if ($Case -ceq 'admission') {
    $gate = Read-Source 'tests/Antiphon.Tests/TestHelpers/NativeLinuxGate.cs'
    $gate = $gate.Replace('namespace Antiphon.Tests.TestHelpers;', '')
    $cuts = @(
        @{Class='NightlyProbeSupervisorTests';Method='C1375_DeadlineKillsAndJoinsTree';Cut='return ScriptHarness'},
        @{Class='RetiredTempContainerDockerTests';Method='C994_Real_retired_temp_lifecycle';Cut='var psi = new ProcessStartInfo'},
        @{Class='RollingVolumeRecycleDockerTests';Method='C1008_Real_docker_comparison';Cut='var psi = new ProcessStartInfo'}
    )
    $failures = @()
    foreach ($row in $cuts) {
        $source = Read-Source ('tests/Antiphon.Tests/Scripts/' + $row.Class + '.cs')
        $method = [regex]::Match($source, ('public (?:async )?Task ' + $row.Method + '\(\)\s*\{'))
        Require $method.Success ('Method missing: ' + $row.Method)
        $start = $method.Index + $method.Length
        $end = $source.IndexOf($row.Cut, $start)
        Require ($end -gt $start) ('Native setup boundary missing: ' + $row.Method)
        $prefix = $source.Substring($start, $end - $start)
        foreach ($platform in @('windows','linux')) {
            $type = $row.Class + $platform
            $condition = if ($platform -ceq 'linux') { 'true' } else { 'false' }
            $body = ($gate + ' public static class ' + $type + ' { public static string Run() { ' + $prefix + ' return "native-setup"; } }').Replace('OperatingSystem.IsLinux()', 'Platform.IsLinux')
            $body = 'using System; namespace W' + $type + ' { public static class Platform { public static bool IsLinux = ' + $condition + '; } namespace TUnit.Core.Exceptions { public class SkipTestException : Exception { public SkipTestException(string reason) : base(reason) {} } } ' + $body + ' }'
            Add-Type $body
            $result = $null; $errorSeen = $null
            try { $result = ('W' + $type + '.' + $type -as [type])::Run() }
            catch { $errorSeen = $_.Exception.InnerException }
            if ($platform -ceq 'linux') {
                Require ($result -ceq 'native-setup' -and $null -eq $errorSeen) ('Linux must execute ' + $row.Method)
            } elseif ($null -eq $errorSeen -or $errorSeen.GetType().Name -cne 'SkipTestException' -or $errorSeen.Message -notmatch 'Linux') {
                $failures += ($row.Class + ': Windows must explicitly skip before native setup; actual=' + $result + '/' + $errorSeen)
            }
        }
    }
    Require ($failures.Count -eq 0) ($failures -join "`n")
    Write-Host 'GROUP77 admission: 6 platform decisions passed; all original native setup follows admission.'
    exit 0
}

$source = Read-Source 'tests/fixtures/card1336-release-gate-followups.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
Require ($errors.Count -eq 0) 'Fixture parse failed'
$helper = $ast.FindAll({param($a) $a -is [Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -ceq 'Resolve-C1336Bash'}, $true)
foreach ($item in $helper) { Invoke-Expression $item.Extent.Text }
$assignment = $ast.FindAll({param($a) $a -is [Management.Automation.Language.AssignmentStatementAst] -and $a.Left.Extent.Text -ceq '$bash'}, $true)
Require ($assignment.Count -eq 1) 'Bash selection assignment missing/ambiguous'
# Pure Windows path strings. Replace only OS/filesystem discovery, never selection logic.
$script:Platform = 'Windows_NT'
function Get-Command($Name) {
    switch ($Name) {
        'bash' { [pscustomobject]@{Source=$(if ($env:OS -ceq 'Windows_NT') { 'C:\Windows\System32\bash.exe' } else { '/usr/bin/bash' })} }
        'git' { [pscustomobject]@{Source='C:\Program Files\Git\cmd\git.exe'} }
        default { throw "Unexpected discovery: $Name" }
    }
}
function Split-Path($Path, [switch]$Parent) { return $Path.Substring(0, $Path.LastIndexOf('\')) }
function Join-Path($Path, $ChildPath) { return $Path.TrimEnd('\') + '\' + $ChildPath.Replace('/', '\') }
$script:InstalledBash = 'C:\Program Files\Git\usr\bin\bash.exe'
function Test-Path($LiteralPath, $PathType) { return $LiteralPath -ceq $script:InstalledBash }
$oldOs = $env:OS
try {
    $env:OS = $script:Platform
    Invoke-Expression $assignment[0].Extent.Text
    Require ($bash -ceq $script:InstalledBash) ('Windows PATH contains WSL first; selected=' + $bash)
    $script:InstalledBash = 'C:\Program Files\Git\bin\bash.exe'
    Invoke-Expression $assignment[0].Extent.Text
    Require ($bash -ceq $script:InstalledBash) 'Git bin fallback did not select its owned shell'
    $script:InstalledBash = ''
    $missing = $null
    try { Invoke-Expression $assignment[0].Extent.Text } catch { $missing = $_.Exception.Message }
    Require ($missing -like '*requires Git Bash*') 'Missing Git Bash must refuse rather than fall back to WSL'
    $env:OS = 'Linux'
    Invoke-Expression $assignment[0].Extent.Text
    Require ($bash -ceq '/usr/bin/bash') 'Linux shell discovery changed'
    Write-Host 'GROUP77 bash: 4 discovery controls passed (Windows usr/bin, bin fallback, missing refusal, Linux).'

} finally { $env:OS = $oldOs }
