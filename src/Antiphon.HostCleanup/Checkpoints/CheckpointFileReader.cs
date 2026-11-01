using System.Security.Cryptography;
using System.Text;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

/// <summary>Verify metadata and final path on the exact handle read.</summary>
public static class CheckpointFileReader
{
    public const int DocumentLimit = 4 * 1024 * 1024;
    public const int SourceLimit = 1024 * 1024;
    public const long ContentLimit = 1L << 30; // Content stamps stream; receipt reads keep their 64 MiB bound.
    // A three-second silence window measures the read. Bytes that return after it has elapsed
    // are not progress, and the window is not widened. A zero-byte stall before any byte is
    // retried once inside the overall budget. A clean end of file is accepted only when the
    // bytes read equal the inspected size and the retained handle still has that size.
    // The terminal stall names a holder when one is
    // known. Dedicated I/O and monitor waits remove the pool-starvation reason for CARD-1599's
    // six-second stopgap. Windows overlapped reads wait on their own event on this thread, so
    // completion does not depend on a pool worker.
    // The overall budget assumes at least 1 MiB/s after a 30s allowance: tiny receipts get
    // 31s, and an inclusive 1 GiB stamp gets 1054s. Short reads cannot restart that budget.
    public static readonly TimeSpan StallDeadline = TimeSpan.FromSeconds(3);
    public static TimeSpan OverallDeadline(long size) => TimeSpan.FromSeconds(30 + Math.Ceiling(size / (1024d * 1024)));
    public const int ReadChunk = 256 * 1024;

    public static string? Refusal(bool isRegularFile, ulong linkCount, string resolvedPath, string root) =>
        Refusal(CheckpointCleanupDefaults.Context, isRegularFile, linkCount, resolvedPath, root);

    public static string? Refusal(CheckpointCleanupContext context, bool isRegularFile, ulong linkCount, string resolvedPath, string root)
    {
        if (!isRegularFile) return "selected file is not regular";
        if (linkCount != 1) return "selected file has multiple links";
        if (!context.IsStrictlyInside(root, resolvedPath)) return "opened file is outside root";
        return null;
    }

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
        ReadBytes(CheckpointCleanupDefaults.Context, root, path, limit, afterVerification, cancellationToken);

    public static byte[] ReadBytes(CheckpointCleanupContext context, string root, string path, int limit,
        Action? afterVerification = null, CancellationToken cancellationToken = default)
    {
        return ReadBudget.Run(context.Time, limit, cancellationToken, budget =>
        {
            using var content = new MemoryStream();
            Consume(context, root, path, limit, afterVerification, cancellationToken, budget, span => content.Write(span));
            return content.ToArray();
        });
    }

    public static (long Length, string Sha256) Digest(CheckpointCleanupContext context, string root, string path,
        long limit, CancellationToken cancellationToken = default)
    {
        return ReadBudget.Run(context.Time, limit, cancellationToken, budget =>
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            Consume(context, root, path, limit, null, cancellationToken, budget, span =>
            {
                hash.AppendData(span);
                total += span.Length;
            });
            return (total, Convert.ToHexString(hash.GetHashAndReset()));
        });
    }

    private delegate void ContentSink(ReadOnlySpan<byte> bytes);

    private static void Consume(CheckpointCleanupContext context, string root, string path, long limit,
        Action? afterVerification, CancellationToken cancellationToken, ReadBudget budget, ContentSink sink)
    {
        budget.Check();
        CheckpointReadOpen request;
        if (context.Io.Platform == CheckpointPlatform.Linux)
        {
            var before = context.Io.Stat(path);
            if (before.Kind == "unknown" || before.Kind == "absent") throw new IOException("opened-handle verification failed");
            if ((before.Mode & 0xF000) != 0x8000) throw new InvalidDataException("selected file is not regular");
            request = new(0x80000 | 0x20000 | 0x800, 0); // CLOEXEC | NOFOLLOW | NONBLOCK
        }
        else if (context.Io.Platform == CheckpointPlatform.Windows)
        {
            if ((context.Io.Attributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidDataException("selected file is not regular");
            // The handle is opened overlapped. WindowsReadStream issues ReadFile with its own
            // OVERLAPPED and must not bind that handle to the thread-pool completion port.
            request = new(0, 0x00200000 | 0x08000000 | 0x40000000); // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN | OVERLAPPED
        }
        else throw new InvalidDataException("opened-handle verification unsupported on this platform");
        using var handle = context.Io.OpenRead(path, request);
        var metadata = handle.Inspect();
        var refusal = Refusal(context, metadata.Regular, metadata.Links, metadata.Path, root);
        if (refusal is not null) throw new InvalidDataException(refusal);
        if (metadata.Size > (ulong)limit) throw new InvalidDataException("selected file exceeds size limit");
        budget.Inspect(metadata.Size);
        afterVerification?.Invoke();
        using var stream = handle.OpenStream();
        long total = 0;
        var buffer = new byte[ReadChunk];
        var zeroStallRetried = false;
        budget.BindHolder(() => context.Io is ICheckpointReadHolders holders ? holders.Holder(path) : null);
        try
        {
            // One byte past the limit is requested so a file that grows during the read is still refused.
            while (true)
            {
                budget.Check();
                // A cancelled in-flight read stays a stall. Only a completed zero-byte read is retried.
                var count = stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - total + 1)), budget.Token)
                    .AsTask().WaitAsync(budget.Token).GetAwaiter().GetResult();
                // The silence window is the read. Bytes that return after it has elapsed are not progress.
                // Only a zero-byte stall before any byte is retried, once, inside the overall budget.
                try { budget.Check(); }
                catch (Exception ex) when (RetryableStall(ex, budget, cancellationToken) && count == 0 && total == 0 && !zeroStallRetried)
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
                var observed = handle.Inspect().Size;
                if (total == (long)metadata.Size && observed == metadata.Size) break;
                throw new InvalidDataException($"read-truncated: {total} of {metadata.Size} bytes; retained size {observed}");
            }
        }
        catch (Exception ex) when (ex is ReadStalledException ||
            (ex is OperationCanceledException && budget.Stalled && !cancellationToken.IsCancellationRequested))
        {
            // A stall at the old EOF is complete only if the retained handle still has the inspected size.
            // A zero-byte stall was already retried once above; an EOF stall is not retried.
            budget.CheckOverall();
            if (total == (long)metadata.Size && handle.Inspect().Size == metadata.Size)
            {
                budget.CheckOverall();
                return;
            }
            throw new IOException(budget.StallText());
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
        private Func<string?>? _holder;
        private bool _holderResolved;
        private string? _holderText;

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
                        // A delivered chunk, or the one zero-byte retry, cleared this stall during the handoff.
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

        public void BindHolder(Func<string?> holder) => _holder = holder;

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

        // The one zero-byte retry keeps the original overall start and opens a fresh silence window.
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

        public string StallText()
        {
            lock (_gate) return FormatStall();
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
                return new ReadStalledException(FormatStall());
            return null;
        }

        private string FormatStall()
        {
            var text = $"read-stalled: {_total} of {_size} bytes; no progress within {StallDeadline.TotalSeconds:0}s";
            if (!_holderResolved)
            {
                _holderResolved = true;
                _holderText = SanitizeHolder(_holder?.Invoke());
            }
            return _holderText is null ? text : text + "; holder=" + _holderText;
        }

        private static string? SanitizeHolder(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var chars = new char[Math.Min(value.Length, 80)];
            var count = 0;
            foreach (var ch in value)
            {
                if (count == chars.Length) break;
                if (ch is >= (char)32 and < (char)127 && ch != ';') chars[count++] = ch;
            }
            var text = new string(chars, 0, count).Trim();
            return text.Length == 0 ? null : text;
        }

        private IOException OverallRefusal() => new($"read-deadline-exceeded: {_total} of {_size} bytes; overall limit {_overall.TotalSeconds:0}s");
    }

    private sealed class ReadStalledException(string message) : IOException(message);
}
