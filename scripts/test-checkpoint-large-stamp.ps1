#requires -Version 7.0
param([string]$Root, [string]$Repository, [string]$ToolDll, [string]$Mode = 'stamp')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Join-Path $Root 'large-stamp-fixture'
[void][IO.Directory]::CreateDirectory($Root)
[void][Reflection.Assembly]::LoadFrom($ToolDll)
. (Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1')
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $Repository 'scripts/run-checkpoint.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'production parse failed' }
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Assert-Equal($Actual, $Expected, [string]$Label) {
    if ($Actual -cne $Expected) { throw "$Label expected=$Expected actual=$Actual" }
}
function New-LargeFile([string]$Path, [long]$Size) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    $stream = [IO.File]::Create($Path)
    try { $stream.SetLength($Size); $stream.WriteByte(83); $stream.Position = $Size - 1; $stream.WriteByte(167) } finally { $stream.Dispose() }
}
function Get-Expected([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try { return 'file|' + $stream.Length + '|' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
}
if ($Mode -eq 'stamp') {
    $path = Join-Path $Root 'large.dll'
    foreach ($size in @(67108864L, 67108865L, 73400320L)) {
        New-LargeFile $path $size
        Assert-Equal (Get-CheckpointContentStamp $path) (Get-Expected $path) "full regular stamp $size"
    }
    [IO.File]::Delete($path)
    [IO.File]::WriteAllText((Join-Path $Root '.gitignore'), "bin-*/`nobj/`n")
    & git -C $Root init -q -b fixture
    & git -C $Root add .gitignore
    & git -C $Root -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false commit -qm fixture
    if ($LASTEXITCODE) { throw 'git fixture failed' }
    foreach ($relative in @('App/bin-prior/large.dll', 'App/obj/large.dll')) { New-LargeFile (Join-Path $Root $relative) 73400320L }
    $baseline = Get-CheckpointTrackedSnapshot -Projects @()
    if ($null -ne $baseline.Error) { throw $baseline.Error }
    foreach ($relative in @('App/bin-prior/large.dll', 'App/obj/large.dll')) {
        Assert-Equal $baseline.Untracked[$relative] (Get-Expected (Join-Path $Root $relative)) "covered $relative"
    }
    $settled = Complete-CheckpointBaseline $baseline
    Assert-Equal $settled.Kind $null 'unchanged acceptance'
    foreach ($relative in @('App/bin-prior/large.dll', 'App/obj/large.dll')) {
        $path = Join-Path $Root $relative
        $modified = [IO.File]::GetLastWriteTimeUtc($path)
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
        try { $stream.Position = 72351744L; $old = $stream.ReadByte(); $stream.Position--; $stream.WriteByte([byte]($old -bxor 1)) } finally { $stream.Dispose() }
        [IO.File]::SetLastWriteTimeUtc($path, $modified)
        Assert-Equal ([IO.FileInfo]::new($path).Length) 73400320L 'same length'
        Assert-Equal ([IO.File]::GetLastWriteTimeUtc($path)) $modified 'same mtime'
        if ((Get-CheckpointContentStamp $path) -ceq $baseline.Untracked[$relative]) { throw "tail flip missed: $relative" }
    }
    $settled = Complete-CheckpointBaseline $baseline
    Assert-Equal $settled.Kind 'tampered' 'tail flip verdict'
    foreach ($relative in @('App/bin-prior/large.dll', 'App/obj/large.dll')) {
        if (-not $settled.Detail.Contains("TAMPERED $relative")) { throw "missing path: $relative" }
        $path = Join-Path $Root $relative
        $after = Get-Expected $path
        Assert-Equal (Get-CheckpointContentStamp $path) $after 'changed bytes remain'
    }
} else { throw "unknown mode: $Mode" }
"C1532 $Mode PASS"
