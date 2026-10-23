# Evaluation-only oracle over the same fixture data used by LandGraphEvaluationTests.
# Build the supplied evaluator separately through the host slot gate. This driver never builds.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ToolPath, [string]$Group = 'witnesses')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tool = (Resolve-Path -LiteralPath $ToolPath).Path
$fixtures = @(Get-Content -Raw (Join-Path $root 'tests/Antiphon.Tests/Application/Fixtures/land-graph-c1528.json') | ConvertFrom-Json -AsHashtable)
$selected = @($fixtures | Where-Object { if ($Group -eq 'witnesses') { $_.group -in @('static','projected','metadata','equal-result') } else { $_.group -eq $Group } })
if ($selected.Count -eq 0) { throw 'No fixtures selected' }
function Run-Child([string]$Cwd, [string]$Command, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Command)
    $start.WorkingDirectory = $Cwd
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($key in @($start.Environment.Keys)) { if ($key.StartsWith('MSBuild', [StringComparison]::OrdinalIgnoreCase)) { [void]$start.Environment.Remove($key) } }
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { $process.Kill($true); $process.WaitForExit(); throw 'fixture child exceeded 120 seconds' }
        return @{ Code = $process.ExitCode; Text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}
$failed = 0
foreach ($fixture in $selected) {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ('c1528-oracle-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($temp)
    try {
        Copy-Item (Join-Path $root 'global.json') $temp
        foreach ($pair in $fixture.files.GetEnumerator()) {
            if ($fixture.ContainsKey('omit') -and $pair.Key -in $fixture.omit) { continue }
            $path = Join-Path $temp $pair.Key
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
            [IO.File]::WriteAllText($path, $pair.Value)
        }
        [IO.File]::WriteAllText((Join-Path $temp 'Antiphon.sln'), @'
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App/App.csproj", "{11111111-1111-1111-1111-111111111111}"
EndProject
Global
 GlobalSection(SolutionConfigurationPlatforms) = preSolution
  Debug|Any CPU = Debug|Any CPU
 EndGlobalSection
 GlobalSection(ProjectConfigurationPlatforms) = postSolution
  {11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
  {11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
 EndGlobalSection
EndGlobal
'@)
        foreach ($arguments in @(@('init','-q'),@('add','-A'),@('-c','user.name=c1528','-c','user.email=c1528@antiphon.test','commit','-qm','fixture'))) {
            $git = Run-Child $temp 'git' $arguments
            if ($git.Code -ne 0) { throw $git.Text }
        }
        $run = Run-Child $temp 'dotnet' @($tool,'--repo',$temp,'--solution','Antiphon.sln')
        $ok = $run.Code -eq $fixture.expected -and $run.Text.Contains($fixture.reason)
        if ($fixture.expected -eq 1) { $ok = $ok -and $run.Text.Contains($fixture.owner) -and -not ($run.Text -match '(?m)^approved\r?$') }
        if (-not $ok) { $failed++ }
        Write-Output ('ORACLE {0} expected={1} actual={2} matched={3} output={4}' -f $fixture.id,$fixture.expected,$run.Code,$ok,($run.Text.Trim() -replace '\r?\n',' | '))
    } finally { Remove-Item -LiteralPath $temp -Recurse -Force }
}
Write-Output "ORACLE total=$($selected.Count) failed=$failed"
if ($failed -gt 0) { exit 1 }
