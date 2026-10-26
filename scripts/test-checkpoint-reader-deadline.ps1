#requires -Version 7.0
# CARD-1363 witness for scripts/lib/checkpoint-file-reader.ps1 and the run-checkpoint.ps1 source stamp.
# The reader modes (progress, stall) pace a manual TimeProvider inside the stream they inject, so the
# three-second window is driven by delivered chunks and never by the wall clock. The ledger modes
# (unreadable, tampered-beside-unreadable) capture a real baseline, then make one untracked entry
# unprovable without changing its bytes through a second hard link outside the repository.
# Owner: docs/testing-and-build.md (CARD-1199 source/content stamping). ASCII-only.
param([string]$Repository, [string]$Scratch, [string]$Mode)
$ErrorActionPreference = 'Stop'
if ($Mode -eq 'windows-open') {
    # Execute the embedded reader's actual Windows open method on every OS. Only
    # its kernel call is substituted; the production flags and FileStream's
    # handle-mode detection remain real. This does not qualify native Windows IO.
    $scriptSource = [IO.File]::ReadAllText((Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1'))
    $match = [regex]::Match($scriptSource, "(?s)Add-Type -TypeDefinition @'\r?\n(.*?)\r?\n'@")
    if (-not $match.Success) { throw 'embedded reader source not found' }
    $source = $match.Groups[1].Value.Replace('var handle = CreateFile(', 'var handle = PortableWindowsOpen.CreateFile(')
    if ($source -ceq $match.Groups[1].Value) { throw 'Windows factory call not found' }
    $source += @'

public static class PortableWindowsOpen
{
    public static uint Flags;
    public static SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template)
    {
        if (access != 0x80000000 || share != 7 || creation != 3) throw new InvalidOperationException("unexpected read access/share/disposition");
        Flags = flags;
        return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            (FileOptions)(flags & 0x40000000));
    }
}
'@
    Add-Type -TypeDefinition $source
    [void][IO.Directory]::CreateDirectory($Scratch)
    $path = Join-Path $Scratch 'windows-owner.txt'
    [IO.File]::WriteAllText($path, '0123456789abcdef0123456789abcdef')
    $method = [Antiphon.Checkpoints.Coverage.ConfinedFileReader].GetMethod('OpenWindows', [Reflection.BindingFlags]'Static,NonPublic')
    $handle = $method.Invoke($null, [object[]]@([string]$path))
    try {
        $stream = [IO.FileStream]::new($handle, [IO.FileAccess]::Read)
        try {
            if (-not $stream.IsAsync) { throw 'Windows verified reader queued synchronous IO: retained FileStream.IsAsync=false' }
            if (([Antiphon.Checkpoints.Coverage.PortableWindowsOpen]::Flags -band 0x08200000) -ne 0x08200000) { throw 'Windows no-follow/sequential flags lost' }
            $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
            $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($path)))
            if ($digest -cne $expected) { throw 'retained stream returned different content' }
            "PASS $Mode async=$($stream.IsAsync) bytes=$($stream.Length)"
        } finally { $stream.Dispose() }
    } finally { $handle.Dispose() }
    exit 0
}
. (Join-Path $Repository 'scripts/lib/checkpoint-file-reader.ps1')
$readerType = [Antiphon.Checkpoints.Coverage.ConfinedFileReader]
if (-not [string]::IsNullOrEmpty($readerType.Assembly.Location)) { throw 'witness must exercise the embedded script reader, not a loaded tool assembly' }

Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Checkpoints.Witness;

/// <summary>A manual clock whose timers fire inside Advance, on the advancing thread.</summary>
public sealed class PacedClock : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<PacedTimer> _timers = new();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    public int Creates;
    public int Changes;
    public int Fires;

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public override long GetTimestamp() => GetUtcNow().UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new PacedTimer(this, callback, state);
        lock (_gate) { _timers.Add(timer); Creates++; timer.Arm(_now, dueTime); }
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        var due = new List<PacedTimer>();
        lock (_gate)
        {
            _now += delta;
            foreach (var timer in _timers)
                if (timer.Due.HasValue && timer.Due.Value <= _now) { timer.Due = null; Fires++; due.Add(timer); }
        }
        foreach (var timer in due) timer.Fire();
    }

    private sealed class PacedTimer : ITimer
    {
        private readonly PacedClock _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        public DateTimeOffset? Due;
        public PacedTimer(PacedClock owner, TimerCallback callback, object? state) { _owner = owner; _callback = callback; _state = state; }
        public void Arm(DateTimeOffset now, TimeSpan dueTime) => Due = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
        public void Fire() => _callback(_state);
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate) { _owner.Changes++; Arm(_owner._now, dueTime); }
            return true;
        }
        public void Dispose() { lock (_owner._gate) Due = null; }
        public ValueTask DisposeAsync() { Dispose(); return default; }
    }
}

/// <summary>Serves at most 64 KiB per read and moves the clock before answering, as a slow disk under load would.</summary>
public sealed class PacedStream : MemoryStream
{
    public const int Served = 64 * 1024;
    private readonly PacedClock _clock;
    private readonly TimeSpan _ordinary;
    private readonly int _specialRead;
    private readonly TimeSpan _special;
    private readonly CancellationTokenSource? _cancelAtSpecial;
    private int _reads;

    public PacedStream(byte[] bytes, PacedClock clock, TimeSpan ordinary, int specialRead, TimeSpan special, CancellationTokenSource? cancelAtSpecial)
        : base(bytes, false)
    {
        _clock = clock; _ordinary = ordinary; _specialRead = specialRead; _special = special; _cancelAtSpecial = cancelAtSpecial;
    }

    public int Reads => _reads;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = Interlocked.Increment(ref _reads);
        if (read == _specialRead) { _cancelAtSpecial?.Cancel(); _clock.Advance(_special); }
        else _clock.Advance(_ordinary);
        return base.ReadAsync(buffer.Slice(0, Math.Min(buffer.Length, Served)), cancellationToken);
    }

    public Func<SafeFileHandle, Stream> Factory() => _ => this;
}
'@

function Get-InnermostException {
    param($Record)
    $inner = $Record.Exception
    while ($null -ne $inner.InnerException) { $inner = $inner.InnerException }
    return $inner
}

function New-PatternedBytes {
    param([int]$Size)
    $bytes = [byte[]]::new($Size)
    for ($i = 0; $i -lt $Size; $i++) { $bytes[$i] = [byte](($i * 31 + ($i -shr 10)) -band 0xFF) }
    return ,$bytes
}

[void][IO.Directory]::CreateDirectory($Scratch)
$limit = 67108864
$none = [System.Threading.CancellationToken]::None
$payloadSize = 7864320 # 7.5 MiB, the archive size behind CARD-1358
$second = [TimeSpan]::FromSeconds(1)

switch ($Mode) {
    'progress' {
        # red: one three-second deadline over the whole read refuses an early chunk although every read answered within a second.
        [byte[]]$payload = New-PatternedBytes $payloadSize
        $path = Join-Path $Scratch 'payload.bin'
        [IO.File]::WriteAllBytes($path, $payload)
        $clock = [Antiphon.Checkpoints.Witness.PacedClock]::new()
        $stream = [Antiphon.Checkpoints.Witness.PacedStream]::new($payload, $clock, $second, 0, [TimeSpan]::Zero, $null)
        [byte[]]$read = $readerType::ReadBytes($Scratch, $path, $limit, $null, $none, $clock, $stream.Factory())
        if ($read.Length -ne $payload.Length) { throw "read $($read.Length) of $($payload.Length) bytes" }
        $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload))
        $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($read))
        if ($actual -cne $expected) { throw 'the bytes delivered over 120 virtual seconds are not the file bytes' }
        if ($stream.Reads -lt 120) { throw "only $($stream.Reads) reads served the payload" }
        if ($clock.Creates -ne 1) { throw "expected one stall timer, saw $($clock.Creates)" }
        if ($clock.Changes -lt 119) { throw "each delivered chunk must re-arm the stall window; saw $($clock.Changes) re-arms" }
        "PASS $Mode reads=$($stream.Reads) changes=$($clock.Changes) fires=$($clock.Fires)"
    }
    'stall' {
        # red: the refusal of a read whose bytes stopped arriving does not say read-stalled.
        [byte[]]$payload = New-PatternedBytes $payloadSize
        $path = Join-Path $Scratch 'payload.bin'
        [IO.File]::WriteAllBytes($path, $payload)
        $clock = [Antiphon.Checkpoints.Witness.PacedClock]::new()
        $stream = [Antiphon.Checkpoints.Witness.PacedStream]::new($payload, $clock, $second, 3, [TimeSpan]::FromSeconds(3), $null)
        $refused = $null
        try { [void]$readerType::ReadBytes($Scratch, $path, $limit, $null, $none, $clock, $stream.Factory()); throw 'stalled read was not refused' }
        catch { $refused = Get-InnermostException $_ }
        $expectedText = 'read-stalled: ' + (2 * [Antiphon.Checkpoints.Witness.PacedStream]::Served) + ' of ' + $payloadSize + ' bytes'
        if ($refused -isnot [IO.IOException] -or -not $refused.Message.StartsWith($expectedText, [StringComparison]::Ordinal)) {
            throw "stalled read refusal must be an IOException starting '$expectedText'; got $($refused.GetType().Name): $($refused.Message)"
        }
        $caller = [System.Threading.CancellationTokenSource]::new()
        $cancelClock = [Antiphon.Checkpoints.Witness.PacedClock]::new()
        $cancelStream = [Antiphon.Checkpoints.Witness.PacedStream]::new($payload, $cancelClock, $second, 2, $second, $caller)
        $aborted = $null
        try { [void]$readerType::ReadBytes($Scratch, $path, $limit, $null, $caller.Token, $cancelClock, $cancelStream.Factory()); throw 'cancelled read was not refused' }
        catch { $aborted = Get-InnermostException $_ }
        if ($aborted -isnot [OperationCanceledException] -or $aborted.Message.Contains('read-stalled')) {
            throw "a caller cancel must stay a cancellation; got $($aborted.GetType().Name): $($aborted.Message)"
        }
        "PASS $Mode stalled='$($refused.Message)' cancelled=$($aborted.GetType().Name)"
    }
    { $_ -in @('unreadable', 'tampered-beside-unreadable') } {
        # red: an entry the reader refuses after capture, with its bytes unchanged, is reported TAMPERED with a restore command.
        . (Join-Path $Repository 'scripts/lib/checkpoint-process-runner.ps1')
        $tokens = $null; $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $Repository 'scripts/run-checkpoint.ps1'), [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors -and @($parseErrors).Count -gt 0) { throw ($parseErrors | Out-String) }
        $buffer = [System.Text.StringBuilder]::new()
        foreach ($definition in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
            [void]$buffer.AppendLine($definition.Extent.Text)
        }
        Invoke-Expression $buffer.ToString()
        $repo = Join-Path $Scratch 'repo'
        [void][IO.Directory]::CreateDirectory((Join-Path $repo 'data'))
        [IO.File]::WriteAllText((Join-Path $repo 'tracked.txt'), 'tracked bytes')
        & git -C $repo init -q -b fixture
        & git -C $repo add tracked.txt
        & git -C $repo -c user.name=Fixture -c user.email=fixture@example.invalid commit -q -m fixture
        if ($LASTEXITCODE) { throw 'fixture git failed' }
        $keep = Join-Path $repo 'data/keep.bin'
        $pinned = Join-Path $repo 'data/pinned.bin'
        [IO.File]::WriteAllText($keep, 'KEEP-ORIGINAL')
        [byte[]]$pinnedBytes = New-PatternedBytes 4096
        [IO.File]::WriteAllBytes($pinned, $pinnedBytes)
        $index = & git -C $repo ls-files -s
        $root = $repo
        $baseline = Get-CheckpointTrackedSnapshot -Projects @()
        if ($null -ne $baseline.Error) { throw "baseline: $($baseline.Error)" }
        if (-not $baseline.Untracked['data/pinned.bin'].StartsWith('file|4096|', [StringComparison]::Ordinal)) { throw "pinned must be readable at capture: $($baseline.Untracked['data/pinned.bin'])" }
        $control = Complete-CheckpointBaseline $baseline
        if ($null -ne $control.Kind) { throw "healthy control: $($control.Detail)" }
        # A second hard link outside the repository makes the entry unprovable (link count 2) without touching its bytes.
        [void](New-Item -ItemType HardLink -Path (Join-Path $Scratch 'pinned-link.bin') -Target $pinned)
        if ($Mode -eq 'tampered-beside-unreadable') { [IO.File]::WriteAllText($keep, 'KEEP-CHANGED') }
        $result = Complete-CheckpointBaseline $baseline
        $detail = [string]$result.Detail
        $lines = @($detail -split "`n")
        $unreadableLine = 'UNREADABLE data/pinned.bin: selected file has multiple links'
        if ($Mode -eq 'unreadable') {
            if ($result.Kind -cne 'unreadable') { throw "kind must be unreadable, got '$($result.Kind)': $detail" }
            if ($lines[0] -cne 'refused: unreadable') { throw "first line: $detail" }
            if ($lines -cnotcontains $unreadableLine) { throw "missing '$unreadableLine': $detail" }
            if ($detail.Contains('TAMPERED') -or $detail.Contains('restore --') -or $detail.Contains('manual command')) { throw "an unprovable entry names no change and no restore: $detail" }
            if ($lines[-1] -cne 'no content difference was observed; nothing in the worktree or index was changed; no restore is needed; repeat the run.') { throw "closing line: $detail" }
        } else {
            if ($result.Kind -cne 'tampered') { throw "kind must be tampered, got '$($result.Kind)': $detail" }
            if ($lines[0] -cne 'refused: tampered') { throw "first line: $detail" }
            $tamperedAt = [array]::IndexOf($lines, 'TAMPERED data/keep.bin')
            if ($tamperedAt -lt 0) { throw "missing TAMPERED data/keep.bin: $detail" }
            if ($lines[$tamperedAt + 1] -cne "manual command: git status --short -- 'data/keep.bin'; inspect and recover user bytes from your backup") { throw "keep.bin command: $detail" }
            $unreadableAt = [array]::IndexOf($lines, $unreadableLine)
            if ($unreadableAt -lt 0) { throw "missing '$unreadableLine': $detail" }
            if ($detail.Contains('TAMPERED data/pinned.bin') -or $detail.Contains("'data/pinned.bin'")) { throw "pinned.bin is unprovable, not changed, and takes no command: $detail" }
            if ($unreadableAt -lt $tamperedAt -or $lines[-1] -cne 'manual restore required; Build-result bytes and index entries were left in place; no source recovery was attempted.') { throw "UNREADABLE lines sit between the TAMPERED entries and the restore line: $detail" }
        }
        if ((& git -C $repo ls-files -s) -cne $index) { throw 'index changed' }
        $after = [IO.File]::ReadAllBytes($pinned)
        if ([Convert]::ToHexString($after) -cne [Convert]::ToHexString($pinnedBytes)) { throw 'settlement changed the unprovable entry' }
        "PASS $Mode kind=$($result.Kind)"
    }
    default { throw "unknown mode '$Mode'" }
}
