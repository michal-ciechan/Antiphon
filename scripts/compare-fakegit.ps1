param(
    [ValidateSet('Pilot')][string]$Phase = 'Pilot',
    [Parameter(Mandatory = $true)][string]$SourcePointer
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$pointer = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $SourcePointer))
if (-not (Test-Path -LiteralPath $pointer -PathType Leaf)) {
    throw "FakeGit source pointer missing: $pointer"
}

# The comparison accepts only the explicitly supplied pointer. It never discovers a newest run.
& dotnet run --project (Join-Path $repoRoot 'tools/Antiphon.Checkpoints') -- fakegit-pilot $pointer
exit $LASTEXITCODE
