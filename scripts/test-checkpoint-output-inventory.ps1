#requires -Version 7.0
# Fresh-output inventory witnesses. Seal-CheckpointFreshOutput must stream a regular
# file through the 1 GiB content digest, keep the uppercase SHA-256 ledger value, and
# refuse an over-bound file, a symlink, and a file that grows past the bound.
# ASCII-only. Owner: docs/testing-and-build.md (checkpoint runner tool).
param([string]$Root)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Join-Path ([IO.Path]::GetTempPath()) ('inventory-' + [guid]::NewGuid().ToString('N')) }
$Root = [IO.Path]::GetFullPath($Root)
[void][IO.Directory]::CreateDirectory($Root)
. (Join-Path $Repository 'scripts/lib/checkpoint-owned-output.ps1')
if (-not [string]::IsNullOrEmpty([Antiphon.Checkpoints.Coverage.ConfinedFileReader].Assembly.Location)) { throw 'embedded reader required' }
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $Repository 'scripts/run-checkpoint.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'driver function parse failed' }
foreach ($name in @('Find-CheckpointGitDir', 'Invoke-CheckpointGit', 'Test-CheckpointComponentLink', 'Test-CheckpointPreflight', 'Test-CheckpointWriteRefusal', 'Write-CheckpointSafeText')) {
    $found = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true))
    if ($found.Count -ne 1) { throw "missing unique driver function $name" }
    . ([scriptblock]::Create($found[0].Extent.Text))
}
$script:Failures = [System.Collections.Generic.List[string]]::new()
function Assert-Inventory {
    param([bool]$Condition, [string]$Label, [string]$Detail = '')
    if ($Condition) { Write-Host "PASS $Label" }
    else { $script:Failures.Add("$Label $Detail"); Write-Host "FAIL $Label $Detail" }
}
function New-InventoryRoot {
    param([string]$Name)
    $dir = Join-Path $Root $Name
    [void][IO.Directory]::CreateDirectory($dir)
    return [IO.Path]::GetFullPath($dir)
}
function New-SparseFile {
    param([string]$Path, [long]$Size)
    $stream = [IO.File]::Create($Path)
    try { $stream.SetLength($Size); if ($Size -gt 0) { $stream.WriteByte(83); $stream.Position = $Size - 1; $stream.WriteByte(167) } } finally { $stream.Dispose() }
}
function Get-StreamDigest {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
}
function Read-Ledger {
    param([string]$Dir)
    $path = Join-Path $Dir '.checkpoint-output-ledger.json'
    if (-not [IO.File]::Exists($path)) { throw "ledger missing under $Dir" }
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable)
}
$limit = [int64][Antiphon.Checkpoints.Coverage.ConfinedFileReader]::ContentLimit
Assert-Inventory ($limit -eq 1073741824L) 'content-bound' "actual=$limit"

$smallDir = New-InventoryRoot 'small'
$small = Join-Path $smallDir 'small.bin'
[IO.File]::WriteAllBytes($small, [byte[]]@(9, 8, 7, 6, 5))
$oldDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]](Read-CheckpointBytes $small)))
$smallSeal = Seal-CheckpointFreshOutput $smallDir
Assert-Inventory ($null -eq $smallSeal) 'small-sealed' ([string]$smallSeal)
if ($null -eq $smallSeal) {
    $smallLedger = Read-Ledger $smallDir
    Assert-Inventory ($smallLedger['small.bin'] -ceq $oldDigest) 'small-digest-matches-buffered-reader' "old=$oldDigest ledger=$($smallLedger['small.bin'])"
}

$largeDir = New-InventoryRoot 'large'
$nested = Join-Path $largeDir 'nested'
[void][IO.Directory]::CreateDirectory($nested)
$dll = Join-Path $nested 'Antiphon.Tests.dll'
New-SparseFile $dll 73400320L
$expectedLarge = Get-StreamDigest $dll
$savedRead = ${function:Read-CheckpointBytes}
$script:BufferedReads = 0
function Read-CheckpointBytes { param([string]$Path) $script:BufferedReads++; & $savedRead $Path }
try { $largeSeal = Seal-CheckpointFreshOutput $largeDir }
finally { Set-Item -Path Function:Read-CheckpointBytes -Value $savedRead }
Assert-Inventory ($null -eq $largeSeal) 'large-sealed' ([string]$largeSeal)
Assert-Inventory ($script:BufferedReads -eq 0) 'large-does-not-buffer' "reads=$($script:BufferedReads)"
if ($null -eq $largeSeal) {
    $largeLedger = Read-Ledger $largeDir
    Assert-Inventory ($largeLedger['nested'] -ceq 'directory') 'large-directory-entry' ([string]$largeLedger['nested'])
    Assert-Inventory ($largeLedger['nested/Antiphon.Tests.dll'] -ceq $expectedLarge) 'large-digest' "expected=$expectedLarge ledger=$($largeLedger['nested/Antiphon.Tests.dll'])"
}

$overDir = New-InventoryRoot 'over'
$over = Join-Path $overDir 'over.bin'
$overSize = $limit + 1L
New-SparseFile $over $overSize
$overSeal = Seal-CheckpointFreshOutput $overDir
Assert-Inventory ($overSeal -like "output-inventory-failed: $over size=$overSize limit=${limit}:*selected file exceeds size limit*") 'over-bound-refused' ([string]$overSeal)
Assert-Inventory (-not [IO.File]::Exists((Join-Path $overDir '.checkpoint-output-ledger.json'))) 'over-bound-writes-no-ledger'

$linkDir = New-InventoryRoot 'link'
$linkTarget = Join-Path $linkDir 'target.bin'
[IO.File]::WriteAllBytes($linkTarget, [byte[]]@(1, 2, 3))
$link = Join-Path $linkDir 'link.bin'
[void][IO.File]::CreateSymbolicLink($link, $linkTarget)
$linkSeal = Seal-CheckpointFreshOutput $linkDir
Assert-Inventory ($linkSeal -like 'output-inventory-failed: *link.bin size=* limit=*:*linked output*') 'symlink-refused' ([string]$linkSeal)

if ($IsLinux) {
    $fifoDir = New-InventoryRoot 'fifo'
    $fifo = Join-Path $fifoDir 'device.fifo'
    & mkfifo $fifo
    if ($LASTEXITCODE) { throw 'mkfifo failed' }
    $fifoSeal = Seal-CheckpointFreshOutput $fifoDir
    Assert-Inventory ($fifoSeal -like "output-inventory-failed: $fifo size=* limit=${limit}:*not regular*") 'fifo-refused' ([string]$fifoSeal)
}

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
public sealed class InventoryGrowthStream : Stream
{
    private readonly long _limit;
    private long _delivered;
    public int MaxRead;
    public InventoryGrowthStream(long limit) { _limit = limit; }
    public long Delivered => _delivered;
    public Func<SafeFileHandle, Stream> Factory() => _ => this;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        MaxRead = Math.Max(MaxRead, buffer.Length);
        var count = (int)Math.Min(buffer.Length, _limit - _delivered + 1);
        if (count > 0) buffer.Span.Slice(0, count).Clear();
        _delivered += count;
        return new ValueTask<int>(count);
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _delivered;
    public override long Position { get => _delivered; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
'@
$growthDir = New-InventoryRoot 'growth'
$growth = Join-Path $growthDir 'growing.bin'
[IO.File]::WriteAllBytes($growth, [byte[]]@(4, 5, 6, 7))
$growthLimit = 262144L
$growthStream = [InventoryGrowthStream]::new($growthLimit)
$growthFailure = $null
try { [void][Antiphon.Checkpoints.Coverage.ConfinedFileReader]::Digest($growthDir, $growth, $growthLimit, $null, [Threading.CancellationToken]::None, $null, $growthStream.Factory()) }
catch { $growthFailure = $_.Exception; while ($null -ne $growthFailure.InnerException) { $growthFailure = $growthFailure.InnerException } }
Assert-Inventory ($null -ne $growthFailure -and $growthFailure.Message.Contains('selected file exceeds size limit')) 'growth-refused' ([string]$growthFailure)
Assert-Inventory ($growthStream.Delivered -eq ($growthLimit + 1L) -and $growthStream.MaxRead -le 262144) 'growth-stops-at-one-extra-byte' "delivered=$($growthStream.Delivered) max=$($growthStream.MaxRead)"
$sealText = ${function:Seal-CheckpointFreshOutput}.ToString()
Assert-Inventory ($sealText.Contains('Get-CheckpointFileDigest') -and -not $sealText.Contains('Read-CheckpointBytes')) 'seal-uses-streaming-digest'

if ($script:Failures.Count -gt 0) { throw ($script:Failures -join [Environment]::NewLine) }
Write-Host 'C378 inventory PASS'
