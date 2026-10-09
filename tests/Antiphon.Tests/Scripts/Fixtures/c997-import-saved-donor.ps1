# Test-only importer wrapper. Stdout belongs to the importer diagnosis.
# Trace lines go to -Trace. The final exit is the importer's exit code.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Importer,
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Stage,
    [Parameter(Mandatory)][long]$AvailableBytes,
    [string]$Trace = '',
    [switch]$FailProbe
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if (-not [string]::IsNullOrEmpty($Trace)) {
    [System.IO.File]::AppendAllText($Trace, "entry source=$Source stage=$Stage`n")
}

$tracePath = $Trace
$expectedStage = $Stage
$available = $AvailableBytes
$fail = [bool]$FailProbe
$probe = {
    param($stagePath)
    if (-not [string]::IsNullOrEmpty($tracePath)) {
        [System.IO.File]::AppendAllText($tracePath, "probe argument=$stagePath value=$available`n")
    }
    if ($fail) { throw 'C997ProbeFault' }
    if ([string]$stagePath -ne $expectedStage) { throw 'C997StageMismatch' }
    return [long]$available
}.GetNewClosure()

$global:LASTEXITCODE = 0
& $Importer -Source $Source -Stage $Stage -GetAvailableFreeBytes $probe
$code = 0
if ($null -ne $LASTEXITCODE) { $code = $LASTEXITCODE }
exit $code
