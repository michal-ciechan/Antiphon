#requires -Version 7.0
# Load the compiled ConfinedFileReader, then the real checkpoint-file-reader.ps1 caller.
# Healthy stamps and the fresh-output seal must use that facade. The script calls only
# members the compiled facade already exposes (Digest, ReadBytes, ContentLimit).
# ASCII-only. Owner: docs/testing-and-build.md (checkpoint runner tool).
param([Parameter(Mandatory = $true)][string]$ToolDll, [string]$Root)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ToolDll = [IO.Path]::GetFullPath($ToolDll)
if (-not [IO.File]::Exists($ToolDll)) { throw "compiled facade dll missing: $ToolDll" }
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Join-Path ([IO.Path]::GetTempPath()) ('facade-' + [guid]::NewGuid().ToString('N')) }
$Root = [IO.Path]::GetFullPath($Root)
[void][IO.Directory]::CreateDirectory($Root)
$directory = [IO.Path]::GetDirectoryName($ToolDll)
foreach ($peer in [IO.Directory]::EnumerateFiles($directory, 'Antiphon.*.dll')) {
    if ([string]::Equals($peer, $ToolDll, [StringComparison]::Ordinal)) { continue }
    [void][Reflection.Assembly]::LoadFrom($peer)
}
[void][Reflection.Assembly]::LoadFrom($ToolDll)
. (Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1')
. (Join-Path $Repository 'scripts/lib/checkpoint-owned-output.ps1')
$reader = [Antiphon.Checkpoints.Coverage.ConfinedFileReader]
if ([string]::IsNullOrEmpty($reader.Assembly.Location)) { throw 'embedded reader was compiled; the compiled facade was not loaded' }
if (-not $reader.Assembly.Location.EndsWith([IO.Path]::DirectorySeparatorChar + 'Antiphon.Checkpoints.dll', [StringComparison]::Ordinal) -and -not $reader.Assembly.Location.EndsWith('/Antiphon.Checkpoints.dll', [StringComparison]::Ordinal) -and -not $reader.Assembly.Location.EndsWith('\Antiphon.Checkpoints.dll', [StringComparison]::Ordinal)) {
    throw "unexpected facade assembly: $($reader.Assembly.Location)"
}
$caller = [IO.File]::ReadAllText((Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1')) + [IO.File]::ReadAllText((Join-Path $Repository 'scripts/lib/checkpoint-owned-output.ps1'))
$names = [regex]::Matches($caller, 'ConfinedFileReader\]::([A-Za-z]+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
if ($names.Count -lt 1) { throw 'caller does not use the facade' }
$flags = [Reflection.BindingFlags]'Public,Static'
foreach ($name in $names) {
    $members = @($reader.GetMember([string]$name, $flags))
    if ($members.Count -lt 1) { throw "compiled facade missing $name" }
}
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $Repository 'scripts/run-checkpoint.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'driver function parse failed' }
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Assert-Equal($Actual, $Expected, [string]$Label) {
    if ($Actual -cne $Expected) { throw "$Label expected=$Expected actual=$Actual" }
}
function New-SparseFile([string]$Path, [long]$Size) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    $stream = [IO.File]::Create($Path)
    try { $stream.SetLength($Size); if ($Size -gt 0) { $stream.WriteByte(83); $stream.Position = $Size - 1; $stream.WriteByte(167) } } finally { $stream.Dispose() }
}
function Get-Expected([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try { return 'file|' + $stream.Length + '|' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
}
$small = Join-Path $Root 'small.bin'
[IO.File]::WriteAllBytes($small, [byte[]]@(1, 2, 3, 4))
$smallStamp = Get-CheckpointContentStamp $small
if ($smallStamp.StartsWith('unreadable|')) { throw "healthy small stamp failed through the compiled facade: $smallStamp" }
Assert-Equal $smallStamp (Get-Expected $small) 'compiled small stamp'
$digest = Get-CheckpointFileDigest $small
if ($digest.Length -isnot [long] -or $digest.Sha256 -isnot [string]) { throw 'compiled digest result must retain scalar Int64/string types' }
$largeDir = Join-Path $Root 'large'
$large = Join-Path $largeDir 'Antiphon.Tests.dll'
New-SparseFile $large 73400320L
$largeStamp = Get-CheckpointContentStamp $large
if ($largeStamp.StartsWith('unreadable|')) { throw "healthy large stamp failed through the compiled facade: $largeStamp" }
Assert-Equal $largeStamp (Get-Expected $large) 'compiled large stamp'
$seal = Seal-CheckpointFreshOutput $largeDir
if ($null -ne $seal) { throw "compiled facade seal failed: $seal" }
$ledger = Get-Content -LiteralPath (Join-Path $largeDir '.checkpoint-output-ledger.json') -Raw | ConvertFrom-Json -AsHashtable
$stream = [IO.File]::OpenRead($large)
try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
if ($ledger['Antiphon.Tests.dll'] -cne $expected) { throw "compiled ledger digest expected=$expected actual=$($ledger['Antiphon.Tests.dll'])" }
if ([string]::IsNullOrEmpty($reader.Assembly.Location)) { throw 'facade was replaced by the embedded reader' }
Write-Host 'C1573 compiled-facade PASS'
