#requires -Version 7.0
# Bounded regular-file reads. Embed the native boundary to avoid a repository-derived source loader.
if (-not ('Antiphon.Checkpoints.Coverage.ConfinedFileReader' -as [type])) {
    Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Checkpoints.Coverage;

/// <summary>Read-only native boundary. Verify metadata and final path on the exact handle read.</summary>
public static class ConfinedFileReader
{
    public const int DocumentLimit = 4 * 1024 * 1024;
    public const int SourceLimit = 1024 * 1024;
    public const long ContentLimit = 1L << 30;
    // A three-second silence window measures a read that delivered nothing. Bytes that arrive
    // after the wait are progress and re-arm the window; the window is not widened. A zero-byte
    // stall is retried once inside the overall budget. Dedicated I/O and monitor waits remove
    // the pool-starvation reason for CARD-1599's six-second stopgap. Windows overlapped reads
    // wait on their own event on this thread, so completion does not depend on a pool worker.
    // The overall budget assumes at least 1 MiB/s after a 30s allowance: tiny receipts get
    // 31s, and an inclusive 1 GiB stamp gets 1054s. Short reads cannot restart that budget.
    public static readonly TimeSpan StallDeadline = TimeSpan.FromSeconds(3);
    public static TimeSpan OverallDeadline(long size) => TimeSpan.FromSeconds(30 + Math.Ceiling(size / (1024d * 1024)));
    public const int ReadChunk = 256 * 1024;

    public static string? Refusal(bool isRegularFile, ulong linkCount, string resolvedPath, string root)
    {
        if (!isRegularFile) return "selected file is not regular";
        if (linkCount != 1) return "selected file has multiple links";
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetFullPath(resolvedPath).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            return "opened file is outside root";
        return null;
    }

    // The optional hook is a deterministic test seam at the verification/read boundary.
    public static string Read(string root, string path, int limit, Action? afterVerification = null) =>
        Read(root, path, limit, out _, afterVerification);

    public static string Read(string root, string path, int limit, out int byteCount, Action? afterVerification = null)
    {
        var bytes = ReadBytes(root, path, limit, afterVerification);
        byteCount = bytes.Length;
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static byte[] ReadBytes(string root, string path, int limit, Action? afterVerification = null, CancellationToken cancellationToken = default) =>
        ReadBytes(root, path, limit, afterVerification, cancellationToken, null, null);

    // CARD-1363: the clock and the handle stream are deterministic test seams at the read boundary; production passes neither.
    public static byte[] ReadBytes(string root, string path, int limit, Action? afterVerification, CancellationToken cancellationToken,
        TimeProvider? time, Func<SafeFileHandle, Stream>? openStream)
    {
        return ReadBudget.Run(time ?? TimeProvider.System, limit, cancellationToken, budget =>
        {
            using var content = new MemoryStream();
            Consume(root, path, limit, afterVerification, cancellationToken, openStream, budget, span => content.Write(span));
            return content.ToArray();
        });
    }

    public static (long Length, string Sha256) Digest(string root, string path, long limit, CancellationToken cancellationToken = default) =>
        Digest(root, path, limit, null, cancellationToken, null, null);

    public static (long Length, string Sha256) Digest(string root, string path, long limit, Action? afterVerification,
        CancellationToken cancellationToken, TimeProvider? time, Func<SafeFileHandle, Stream>? openStream)
    {
        return ReadBudget.Run(time ?? TimeProvider.System, limit, cancellationToken, budget =>
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            Consume(root, path, limit, afterVerification, cancellationToken, openStream, budget, span =>
            {
                hash.AppendData(span);
                total += span.Length;
            });
            return (total, Convert.ToHexString(hash.GetHashAndReset()));
        });
    }

    private delegate void ContentSink(ReadOnlySpan<byte> bytes);

    private static void Consume(string root, string path, long limit, Action? afterVerification, CancellationToken cancellationToken,
        Func<SafeFileHandle, Stream>? openStream, ReadBudget budget, ContentSink sink)
    {
        budget.Check();
        using var handle = OperatingSystem.IsLinux() ? OpenLinux(path) : OperatingSystem.IsWindows() ? OpenWindows(path)
            : throw new InvalidDataException("opened-handle verification unsupported on this platform");
        var metadata = OperatingSystem.IsLinux() ? InspectLinux(handle) : InspectWindows(handle);
        var refusal = Refusal(metadata.Regular, metadata.Links, metadata.Path, root);
        if (refusal is not null) throw new InvalidDataException(refusal);
        if (metadata.Size > (ulong)limit) throw new InvalidDataException("selected file exceeds size limit");
        budget.Inspect(metadata.Size);
        afterVerification?.Invoke();
        using var stream = openStream is not null ? openStream(handle) : OperatingSystem.IsLinux() ? new LinuxReadStream(handle) : new WindowsReadStream(handle);
        long total = 0;
        var buffer = new byte[ReadChunk];
        var zeroStallRetried = false;
        try
        {
            // One byte past the limit is requested so a file that grows during the read is still refused.
            while (true)
            {
                budget.Check();
                int count;
                try
                {
                    count = stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - total + 1)), budget.Token)
                        .AsTask().WaitAsync(budget.Token).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (RetryableStall(ex, budget, cancellationToken) && total == 0 && !zeroStallRetried)
                {
                    zeroStallRetried = true;
                    budget.RearmStall();
                    continue;
                }
                if (count > 0)
                {
                    if (total + count > limit) throw new InvalidDataException("selected file exceeds size limit");
                    sink(buffer.AsSpan(0, count));
                    total += count;
                    budget.Progress(count);
                    continue;
                }
                try { budget.Check(); }
                catch (Exception ex) when (RetryableStall(ex, budget, cancellationToken) && total == 0 && !zeroStallRetried)
                {
                    zeroStallRetried = true;
                    budget.RearmStall();
                    continue;
                }
                break;
            }
        }
        catch (Exception ex) when (ex is ReadStalledException ||
            (ex is OperationCanceledException && budget.Stalled && !cancellationToken.IsCancellationRequested))
        {
            // Only the retained handle can prove that an EOF stall still represents a complete read.
            budget.CheckOverall();
            if (total == (long)metadata.Size &&
                (OperatingSystem.IsLinux() ? InspectLinux(handle) : InspectWindows(handle)).Size == metadata.Size)
            {
                budget.CheckOverall();
                return;
            }
            throw new IOException($"read-stalled: {total} of {metadata.Size} bytes; no progress within {StallDeadline.TotalSeconds:0}s");
        }
    }

    private static bool RetryableStall(Exception ex, ReadBudget budget, CancellationToken cancellationToken) =>
        ex is ReadStalledException ||
        (ex is OperationCanceledException && budget.Stalled && !cancellationToken.IsCancellationRequested);

    // One dedicated I/O thread owns the handle, buffers and sink through disposal. The caller
    // waits on a monitor, not a pool timer. If a syscall cannot be cancelled, that thread keeps
    // ownership until it returns; it cannot publish bytes or touch a disposed caller-owned sink.
    private sealed class ReadBudget
    {
        private readonly object _gate = new();
        private readonly TimeProvider _time;
        private readonly CancellationToken _caller;
        private CancellationTokenSource _stop;
        private readonly long _started;
        private long _progress;
        private long _total;
        private ulong _size;
        private TimeSpan _overall;
        private Exception? _refusal;
        private bool _completed;

        private ReadBudget(TimeProvider time, long limit, CancellationToken caller)
        {
            _time = time;
            _caller = caller;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _started = _progress = time.GetTimestamp();
            _overall = OverallDeadline(limit);
        }

        public CancellationToken Token => _stop.Token;
        public bool Stalled { get { lock (_gate) return _refusal is ReadStalledException; } }

        public static T Run<T>(TimeProvider time, long limit, CancellationToken caller, Func<ReadBudget, T> action)
        {
            caller.ThrowIfCancellationRequested();
            var budget = new ReadBudget(time, limit, caller);
            T result = default!;
            Exception? error = null;
            var worker = new Thread(() =>
            {
                try { result = action(budget); }
                catch (Exception ex) { error = ex; }
                finally
                {
                    lock (budget._gate)
                    {
                        budget._stop.Dispose();
                        budget._completed = true;
                        Monitor.PulseAll(budget._gate);
                    }
                }
            }) { IsBackground = true, Name = "checkpoint-file-read" };
            try { worker.Start(); }
            catch { budget._stop.Dispose(); throw; }
            lock (budget._gate)
            {
                while (!budget._completed)
                {
                    var refusal = budget.Refusal();
                    if (refusal is not null)
                    {
                        budget._refusal = refusal;
                        budget._stop.Cancel();
                        // Let a cancellable read unwind and perform the retained-handle EOF check.
                        // This is a bounded cancellation handoff, never a join of a hung syscall.
                        // An overall expiry has no handoff allowance.
                        var handoff = TimeSpan.FromMilliseconds(100);
                        var left = budget._overall - time.GetElapsedTime(budget._started);
                        if (left < handoff) handoff = left;
                        if (!budget._completed && handoff > TimeSpan.Zero)
                            Monitor.Wait(budget._gate, handoff);
                        if (budget._completed) break;
                        if (refusal is ReadStalledException && budget._refusal is null) continue;
                        throw refusal is ReadStalledException ? new IOException(refusal.Message) : refusal;
                    }
                    // Polling also observes manual TimeProviders and caller cancellation without
                    // installing any timer or queueing any thread-pool work.
                    var remaining = budget.Remaining();
                    if (remaining <= TimeSpan.Zero) continue;
                    Monitor.Wait(budget._gate, remaining < TimeSpan.FromMilliseconds(10)
                        ? remaining : TimeSpan.FromMilliseconds(10));
                }
            }
            if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            caller.ThrowIfCancellationRequested();
            return result;
        }

        public void Inspect(ulong size)
        {
            lock (_gate)
            {
                _size = size;
                _overall = OverallDeadline((long)size);
            }
            Check();
        }

        public void Progress(int count)
        {
            lock (_gate)
            {
                _total += count;
                _progress = _time.GetTimestamp();
                if (_refusal is ReadStalledException) _refusal = null;
                ReplaceCancelledStop();
                Monitor.PulseAll(_gate);
            }
        }

        public void RearmStall()
        {
            lock (_gate)
            {
                if (_refusal is ReadStalledException) _refusal = null;
                _progress = _time.GetTimestamp();
                ReplaceCancelledStop();
                Monitor.PulseAll(_gate);
            }
        }

        private void ReplaceCancelledStop()
        {
            if (!_stop.IsCancellationRequested || _caller.IsCancellationRequested) return;
            _stop.Dispose();
            _stop = CancellationTokenSource.CreateLinkedTokenSource(_caller);
        }

        public void Check()
        {
            lock (_gate)
            {
                var refusal = _refusal ?? Refusal();
                if (refusal is null) return;
                _refusal = refusal;
                throw refusal;
            }
        }

        public void CheckOverall()
        {
            _caller.ThrowIfCancellationRequested();
            lock (_gate)
                if (_time.GetElapsedTime(_started) >= _overall)
                    throw OverallRefusal();
        }

        // All state accesses below are under _gate. A short read never extends _started.
        private TimeSpan Remaining()
        {
            var stall = StallDeadline - _time.GetElapsedTime(_progress);
            var overall = _overall - _time.GetElapsedTime(_started);
            return stall < overall ? stall : overall;
        }

        private Exception? Refusal()
        {
            if (_caller.IsCancellationRequested) return new OperationCanceledException(_caller);
            if (_time.GetElapsedTime(_started) >= _overall) return OverallRefusal();
            if (_time.GetElapsedTime(_progress) >= StallDeadline)
                return new ReadStalledException($"read-stalled: {_total} of {_size} bytes; no progress within {StallDeadline.TotalSeconds:0}s");
            return null;
        }

        private IOException OverallRefusal() => new($"read-deadline-exceeded: {_total} of {_size} bytes; overall limit {_overall.TotalSeconds:0}s");
    }

    private sealed class ReadStalledException(string message) : IOException(message);

    // Linux FileStream.ReadAsync on a non-overlapped fd queues blocking I/O on the pool.
    // The reader already owns a dedicated thread, so execute that syscall inline instead.
    private sealed class LinuxReadStream(SafeFileHandle handle) : FileStream(handle, FileAccess.Read)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }
    }

    // Wait on the overlapped event on this thread. Overlapped I/O does not advance the file pointer.
    private sealed class WindowsReadStream(SafeFileHandle handle) : FileStream(handle, FileAccess.Read)
    {
        private long _offset;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            var pin = GCHandle.Alloc(rented, GCHandleType.Pinned);
            try
            {
                var read = ReadPinned(rented, buffer.Length, CancellationToken.None);
                if (read > 0) rented.AsSpan(0, read).CopyTo(buffer);
                return read;
            }
            finally { pin.Free(); ArrayPool<byte>.Shared.Return(rented); }
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            var pin = GCHandle.Alloc(rented, GCHandleType.Pinned);
            try
            {
                var read = ReadPinned(rented, buffer.Length, cancellationToken);
                if (read > 0) rented.AsSpan(0, read).CopyTo(buffer.Span);
                return ValueTask.FromResult(read);
            }
            finally { pin.Free(); ArrayPool<byte>.Shared.Return(rented); }
        }

        private int ReadPinned(byte[] buffer, int count, CancellationToken cancellationToken)
        {
            var ev = CreateEvent(IntPtr.Zero, true, false, null);
            if (ev == IntPtr.Zero) throw new IOException("read event unavailable: win32=" + Marshal.GetLastWin32Error());
            try
            {
                var overlapped = new NativeOverlapped
                {
                    OffsetLow = unchecked((int)_offset),
                    OffsetHigh = unchecked((int)(_offset >> 32)),
                    EventHandle = ev
                };
                if (!ReadFile(handle, buffer, count, out var read, ref overlapped))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 38) return 0;
                    if (error != 997) throw new IOException("read failed: win32=" + error);
                    while (true)
                    {
                        var wait = WaitForSingleObject(ev, 50);
                        if (wait == 0) break;
                        if (wait == 258 && !cancellationToken.IsCancellationRequested) continue;
                        CancelIoEx(handle, ref overlapped);
                        WaitForSingleObject(ev, 100);
                        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                        throw new IOException("read wait failed: win32=" + wait);
                    }
                    if (!GetOverlappedResult(handle, ref overlapped, out read, false))
                    {
                        var pending = Marshal.GetLastWin32Error();
                        if (pending == 38) return 0;
                        if (pending == 995 || cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                        throw new IOException("read failed: win32=" + pending);
                    }
                }
                if (read > 0) _offset += read;
                return read;
            }
            finally { CloseHandle(ev); }
        }
    }

    private static SafeFileHandle OpenLinux(string path)
    {
        // statx is ABI-stable on Linux architectures. Precheck avoids even attempting special files;
        // O_NONBLOCK also prevents a FIFO swapped in after that precheck from blocking open.
        if (Statx(-100, path, 0x100, 0x205, out var before) != 0) throw NativeFailure();
        if ((before.Mode & 0xF000) != 0x8000) throw new InvalidDataException("selected file is not regular");
        var fd = Open(path, 0x80000 | 0x20000 | 0x800); // O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK, O_RDONLY=0
        if (fd < 0) throw NativeFailure();
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }

    private static (bool Regular, ulong Links, ulong Size, string Path) InspectLinux(SafeFileHandle handle)
    {
        var fd = handle.DangerousGetHandle().ToInt32();
        if (Statx(fd, "", 0x1000, 0x205, out var stat) != 0 || (stat.Mask & 0x205) != 0x205) throw NativeFailure();
        var buffer = new byte[32768];
        var count = ReadLink("/proc/self/fd/" + fd, buffer, (nuint)buffer.Length);
        if (count <= 0 || count >= buffer.Length) throw NativeFailure();
        return ((stat.Mode & 0xF000) == 0x8000, stat.Links, stat.Size, Encoding.UTF8.GetString(buffer, 0, (int)count));
    }

    private static SafeFileHandle OpenWindows(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new InvalidDataException("selected file is not regular");
        // FileStream infers async mode from the retained handle; do not queue synchronous
        // Windows I/O behind the callers blocking on ReadAsync's three-second deadline.
        var handle = CreateFile(path, 0x80000000, 7, IntPtr.Zero, 3, 0x00200000 | 0x08000000 | 0x40000000, IntPtr.Zero); // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN | OVERLAPPED
        if (handle.IsInvalid) { handle.Dispose(); throw NativeFailure(); }
        return handle;
    }

    private static (bool Regular, ulong Links, ulong Size, string Path) InspectWindows(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw NativeFailure();
        var path = new StringBuilder(32768);
        var count = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
        if (count == 0 || count >= path.Capacity) throw NativeFailure();
        var final = path.ToString();
        if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) final = @"\\" + final[8..];
        else if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
        return (GetFileType(handle) == 1 && (info.Attributes & (0x10u | 0x400u)) == 0, info.Links,
            ((ulong)info.SizeHigh << 32) | info.SizeLow, final);
    }

    private static IOException NativeFailure() => new("opened-handle verification failed: errno=" + Marshal.GetLastPInvokeError());

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(40)] public ulong Size;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern nint ReadLink(string path, byte[] buffer, nuint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle handle, byte[] buffer, int count, out int read, ref NativeOverlapped overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(SafeFileHandle handle, ref NativeOverlapped overlapped, out int read, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(SafeFileHandle handle, ref NativeOverlapped overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}

'@
}
function Read-CheckpointBytes {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    return ,([Antiphon.Checkpoints.Coverage.ConfinedFileReader]::ReadBytes([IO.Path]::GetDirectoryName($full), $full, 67108864, $null, [Threading.CancellationToken]::None))
}
function Get-CheckpointFileDigest {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    $digest = [Antiphon.Checkpoints.Coverage.ConfinedFileReader]::Digest([IO.Path]::GetDirectoryName($full), $full, [Antiphon.Checkpoints.Coverage.ConfinedFileReader]::ContentLimit, [Threading.CancellationToken]::None)
    return [pscustomobject]@{ Length = [long]$digest.Item1; Sha256 = [string]$digest.Item2 }
}
function Read-CheckpointText {
    param([string]$Path)
    $bytes = Read-CheckpointBytes $Path
    $reader = [IO.StreamReader]::new([IO.MemoryStream]::new([byte[]]$bytes))
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
