#requires -Version 7.0
<#
.SYNOPSIS
    CARD-0585 D-7: run ONE row of a plan's "### Checkpoints" table and print its report line.

    pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-x/ `
        -Filter '/*/*/ExampleSurfaceTests/*' -MinExecuted 3 -Expect ExampleSurfaceTests

    Thin launcher of the checkpoint tool's row verb (tools/Antiphon.Checkpoints/Execution/RowCommand.cs), unit 20 of
    docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md. Kept because agents, the delegate bundle and the server2
    nested lane run this file by path. Each parameter is the row verb's flag of the same name in kebab case (-DotnetShim is
    --dotnet); the row runs as `antiphon-scripts checkpoints row ...` (scripts/lib/Antiphon.Nightly/CheckpointTool.cs: the
    tool built from this checkout, then run in the caller's directory). A relative -ResultsRoot is this location's, as
    before. The first row in a checkout builds the CLI and the tool (about a minute); later rows start in a few seconds.

    One isolated build (unless -NoBuild) plus one exact filter into a fresh results directory, under a host build slot
    (CARD-0589: BUILD SLOT lines, FIFO wait, -maxcpucount from the grant, an unreachable runner falls back to an unleased
    run at -maxcpucount:4 after 60 s; -NoSlot is for an operator shell only), then the CHECKPOINT line, the FAILED and
    EXECUTED roster and a `CHECKPOINT <Name> EXIT CODE: <n>` trailer, and report.json/report.md in the results directory.

    Exit codes: 0 green; 1 one or more failed tests; 2 invalid input, a dirty tracked tree, a detected tracked change, a
    failed build, no TRX or ineligible source; 3 fewer than -MinExecuted executed tests, or an -Expect token that matched
    no executed name; 4 no host build slot within -SlotWaitMinutes (nothing was built or run); 9 the CLI or the tool could
    not be built or started (one stderr line).

    Owner: docs/testing-and-build.md, "Checkpoint manifest (CARD-0585)". ASCII-only.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Name,
    [Parameter(Mandatory = $true)] [string]$Project,
    [Parameter(Mandatory = $true)] [string]$OutputPath,
    [Parameter(Mandatory = $true)] [string]$Filter,
    [string]$ResultsRoot = '.antiphon/checkpoints',
    [switch]$NoBuild,
    [int]$Repeat = 1,
    [int]$MinExecuted = 1,
    [string[]]$Expect,
    [string[]]$MsBuildProperty,
    [string]$ExpectedSourceSha,
    [string]$DotnetShim,
    [double]$SlotWaitMinutes = 45,
    [switch]$NoSlot,
    [string]$TimingRoot
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')

# Every value goes over as given (-Verbatim): a trailing backslash or quote characters are the row's to judge.
$location = (Get-Location -PSProvider FileSystem).ProviderPath
$invariant = [Globalization.CultureInfo]::InvariantCulture
$results = $location
if (-not [string]::IsNullOrEmpty($ResultsRoot)) { $results = [IO.Path]::GetFullPath($ResultsRoot, $location) }
$row = New-Object System.Collections.Generic.List[string]
$row.AddRange([string[]]@('row', '--name', $Name, '--project', $Project, '--output-path', $OutputPath, '--filter', $Filter,
    '--results-root', $results, '--repeat', $Repeat.ToString($invariant), '--min-executed', $MinExecuted.ToString($invariant),
    '--slot-wait-minutes', $SlotWaitMinutes.ToString($invariant)))
if ($NoBuild) { $row.Add('--no-build') }
if ($NoSlot) { $row.Add('--no-slot') }
foreach ($token in @($Expect)) { if (-not [string]::IsNullOrEmpty([string]$token)) { $row.Add('--expect'); $row.Add([string]$token) } }
foreach ($token in @($MsBuildProperty)) { if (-not [string]::IsNullOrEmpty([string]$token)) { $row.Add('--msbuild-property'); $row.Add([string]$token) } }
if (-not [string]::IsNullOrEmpty($ExpectedSourceSha)) { $row.Add('--expected-source-sha'); $row.Add($ExpectedSourceSha) }
if (-not [string]::IsNullOrEmpty($DotnetShim)) { $row.Add('--dotnet'); $row.Add($DotnetShim) }
if (-not [string]::IsNullOrEmpty($TimingRoot)) { $row.Add('--timing-root'); $row.Add($TimingRoot) }
exit (Invoke-AntiphonCli -Command 'checkpoints' -Verbatim $row.ToArray())
