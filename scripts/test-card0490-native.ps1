# CARD-0490 D-12 wrapper. One PC/phase per invocation.
param(
    [Parameter(Mandatory = $true)][string] $SnapshotRoot,
    [string] $BindingFile,
    [Parameter(Mandatory = $true)][string] $AssetProfile,
    [Parameter(Mandatory = $true)][ValidateSet('PC-28','PC-29','PC-30','PC-31')][string] $Pc,
    [Parameter(Mandatory = $true)][ValidateSet('baseline','red','green')][string] $Phase,
    [Parameter(Mandatory = $true)][string] $EvidenceRoot,
    [switch] $Ordinary
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Ordinary -and $BindingFile) {
    throw "Sourced refusal never downgrades to ordinary."
}
if (-not $Ordinary -and -not $BindingFile) {
    throw "Sourced execution requires BindingFile."
}

$harness = Join-Path $PSScriptRoot '..\tests\Antiphon.Card0490.NativeHarness\Antiphon.Card0490.NativeHarness.csproj'
$args = @('-Pc', $Pc, '-Phase', $Phase, '-SnapshotRoot', $SnapshotRoot, '-AssetProfile', $AssetProfile, '-EvidenceRoot', $EvidenceRoot)
if ($Ordinary) { $args += '-Ordinary' }
if ($BindingFile) { $args += @('-BindingFile', $BindingFile) }

& dotnet run --project $harness -- @args
exit $LASTEXITCODE
