#requires -Version 7.0
param([string]$Root, [string]$Repository, [string]$ToolDll, [string]$Mode = 'stamp')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Join-Path $Root 'large-stamp-fixture'
[void][IO.Directory]::CreateDirectory($Root)
. (Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1')
. (Join-Path $Repository 'scripts/lib/checkpoint-process-runner.ps1')
if (-not [string]::IsNullOrEmpty([Antiphon.Checkpoints.Coverage.ConfinedFileReader].Assembly.Location)) { throw 'embedded reader required' }
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
    $empty = Join-Path $Root 'empty.bin'
    Assert-Equal (Get-CheckpointContentStamp $empty) 'absent|0|' 'absent path'
    [IO.File]::WriteAllBytes($empty, [byte[]]@())
    $emptyStamp = 'file|0|e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
    Assert-Equal (Get-CheckpointContentStamp $empty) $emptyStamp 'empty file'
    Assert-Equal (Get-CheckpointContentStamp $Root) 'directory|0|' 'empty versus directory'
    $digest = Get-CheckpointFileDigest $empty
    if ($digest.Length -isnot [long] -or $digest.Sha256 -isnot [string]) { throw 'digest result must retain scalar Int64/string types' }
    $saved = (Get-Item Function:Get-CheckpointFileDigest).ScriptBlock
    try {
        foreach ($badResult in @($null, [pscustomobject]@{}, [pscustomobject]@{ Length = 0L }, [pscustomobject]@{ Length = 0L; Sha256 = '' })) {
            function Get-CheckpointFileDigest { param([string]$Path) return $badResult }
            if (-not (Get-CheckpointContentStamp $empty).StartsWith('unreadable|content digest ')) { throw 'missing/empty digest was accepted' }
        }
    } finally { Set-Item Function:Get-CheckpointFileDigest $saved }
    Assert-Equal (Get-CheckpointContentStamp $empty) $emptyStamp 'restored actual digest'
    $deletedBaseline = Get-CheckpointTrackedSnapshot -Projects @()
    [IO.File]::Delete($empty)
    $deleted = Complete-CheckpointBaseline $deletedBaseline
    Assert-Equal $deleted.Kind 'tampered' 'deleted file'
    if (-not $deleted.Detail.Contains('TAMPERED empty.bin')) { throw 'missing deleted path' }
} elseif ($Mode -eq 'reader') {
    # Reuse only the manual-clock helper declaration, not another test's execution or oracle.
    # The deadline script's first Add-Type argument is the variable $source. Select the single
    # here-string that declares class PacedClock by that exact name.
    $helperAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $Repository 'scripts/test-checkpoint-reader-deadline.ps1'), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw 'manual clock parse failed' }
    $name = 'PacedClock'
    $declarations = @($helperAst.FindAll({
        param($node)
        $node -is [Management.Automation.Language.StringConstantExpressionAst] -and
        $node.StringConstantType -eq [Management.Automation.Language.StringConstantType]::SingleQuotedHereString -and
        [regex]::IsMatch($node.Value, "(?m)^public sealed class $name : TimeProvider\r?$")
    }, $false))
    if ($declarations.Count -eq 0) { throw 'manual clock declaration missing' }
    if ($declarations.Count -ne 1) { throw "manual clock declaration count=$($declarations.Count)" }
    $helperSource = $declarations[0].Value
    $helperSource += @'

public sealed class LargeZeroStream : Stream
{
    private readonly long _length;
    private readonly string _path;
    private readonly string _mode;
    private readonly PacedClock _clock;
    private readonly CancellationTokenSource _caller;
    private SafeFileHandle? _handle;
    public long Delivered;
    public int MaxRead, Reads, Opens;
    public LargeZeroStream(long length, string path, string mode, PacedClock clock, CancellationTokenSource caller)
    { _length = length; _path = path; _mode = mode; _clock = clock; _caller = caller; }
    public Func<SafeFileHandle, Stream> Factory() => h => { Opens++; _handle = h; return this; };
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Reads++; MaxRead = Math.Max(MaxRead, buffer.Length);
        if (_mode == "cancel" && Reads == 2) _caller.Cancel();
        if (Delivered == _length && (_mode == "eof" || _mode == "grow" || _mode == "shrink" || _mode == "inspect"))
        {
            if (_mode == "inspect") _handle!.Dispose();
            else if (_mode != "eof")
            {
                using var file = File.Open(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                file.SetLength(_length + (_mode == "grow" ? 1 : -1));
            }
            _clock.Advance(TimeSpan.FromSeconds(3));
        }
        else _clock.Advance(TimeSpan.FromSeconds(_mode == "stall" && Reads == 2 ? 3 : 1));
        cancellationToken.ThrowIfCancellationRequested();
        var count = (int)Math.Min(buffer.Length, _length - Delivered);
        buffer.Span.Slice(0, count).Clear(); Delivered += count;
        return new ValueTask<int>(count);
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => Delivered; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
'@
    Add-Type -TypeDefinition $helperSource
    $reader = [Antiphon.Checkpoints.Coverage.ConfinedFileReader]
    $path = Join-Path $Root 'reader.bin'
    $bound = 1073741824L
    foreach ($case in @('bound', 'oversize', 'growth', 'receipt', 'receipt-over', 'progress', 'stall', 'cancel-before', 'cancel', 'eof', 'grow', 'shrink', 'inspect')) {
        $size = if ($case -in @('bound','oversize','growth')) { $bound } elseif ($case -in @('receipt','receipt-over')) { 67108864L } else { 1048576L }
        if ($case -in @('oversize','receipt-over')) { $size++ }
        $file = [IO.File]::Create($path)
        try { $file.SetLength($size) } finally { $file.Dispose() }
        $clock = [Antiphon.Checkpoints.Witness.PacedClock]::new()
        $caller = [Threading.CancellationTokenSource]::new()
        try {
            if ($case -eq 'cancel-before') { $caller.Cancel() }
            $delivered = if ($case -eq 'growth') { $bound + 1 } else { $size }
            $stream = [Antiphon.Checkpoints.Witness.LargeZeroStream]::new($delivered, $path, $case, $clock, $caller)
            $failure = $null; $result = $null
            try {
                if ($case -in @('receipt','receipt-over')) {
                    $result = $reader::ReadBytes($Root, $path, 67108864, $null, $caller.Token, $clock, $stream.Factory())
                } else { $result = $reader::Digest($Root, $path, $bound, $null, $caller.Token, $clock, $stream.Factory()) }
            } catch { $failure = $_.Exception; while ($null -ne $failure.InnerException) { $failure = $failure.InnerException } }
            if ($case -in @('bound','receipt','progress','eof')) {
                if ($null -ne $failure) { throw "$case failed: $failure" }
                if ($case -eq 'receipt') { Assert-Equal $result.LongLength $size 'receipt exact cap' }
                else {
                    Assert-Equal $result.Item1 $size "$case length"
                    $native = [IO.File]::OpenRead($path)
                    try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($native)) } finally { $native.Dispose() }
                    Assert-Equal $result.Item2 $expected "$case full independent hash"
                }
                Assert-Equal $stream.Delivered $size "$case delivered"
                if ($clock.Changes -lt 4) { throw "$case progress did not rearm beyond three seconds" }
            } else {
                if ($null -eq $failure) { throw "$case accepted" }
                if ($case -in @('oversize','growth','receipt-over')) {
                    if (-not $failure.Message.Contains('selected file exceeds size limit')) { throw "$case wrong refusal: $failure" }
                } elseif ($case -in @('cancel-before','cancel')) {
                    if ($failure -isnot [OperationCanceledException]) { throw "$case lost cancellation: $failure" }
                } elseif ($case -ne 'inspect' -and -not $failure.Message.Contains('read-stalled')) { throw "$case wrong refusal: $failure" }
            }
            Assert-Equal $stream.Opens $(if ($case -in @('oversize','receipt-over','cancel-before')) { 0 } else { 1 }) "$case retained stream"
            if ($stream.MaxRead -gt 262144) { throw "$case exceeded chunk bound" }
            if ($case -eq 'growth') { Assert-Equal $stream.Delivered ($bound + 1) 'one extra byte' }
        } finally { $caller.Dispose() }
    }
    # Native admission still refuses unsafe handles without attempting FIFO reads.
    [IO.File]::WriteAllText($path, 'native bytes')
    $link = Join-Path $Root 'hardlink'
    [void](New-Item -ItemType HardLink -Path $link -Target $path)
    Assert-Equal (Get-CheckpointContentStamp $path) 'unreadable|selected file has multiple links' 'hardlink'
    [IO.File]::Delete($link)
    $outside = $null
    try { [void]$reader::Digest((Join-Path $Root 'other'), $path, $bound, [Threading.CancellationToken]::None) } catch { $outside = $_.Exception }
    if ($null -eq $outside -or -not $outside.ToString().Contains('outside root')) { throw 'containment refusal missing' }
    if ($IsLinux) {
        $fifo = Join-Path $Root 'fifo'
        & mkfifo $fifo
        if ($LASTEXITCODE) { throw 'mkfifo failed' }
        try { Assert-Equal (Get-CheckpointContentStamp $fifo) 'unreadable|selected file is not regular' 'FIFO' } finally { [IO.File]::Delete($fifo) }
    }
} elseif ($Mode -eq 'parser') {
    & pwsh -NoProfile -NonInteractive -File (Join-Path $Repository 'scripts/test-checkpoint-stamp-ps51-parse.ps1') -AllowPwsh
    if ($LASTEXITCODE) { throw 'PS7 parser witness failed' }
} else { throw "unknown mode: $Mode" }
"C1532 $Mode PASS"
