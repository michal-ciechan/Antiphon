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
    // A three-second silence window covers open/inspection and each read. Dedicated I/O and
    // monitor waits remove the pool-starvation reason for CARD-1599's six-second stopgap.
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
            // The retained stream reads synchronously on our dedicated I/O thread.
            // Overlapped completions would depend on the pool occupied by blocking callers.
            request = new(0, 0x00200000 | 0x08000000); // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN
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
        try
        {
            // One byte past the limit is requested so a file that grows during the read is still refused.
            while (true)
            {
                budget.Check();
                var count = stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - total + 1)), budget.Token)
                    .AsTask().WaitAsync(budget.Token).GetAwaiter().GetResult();
                budget.Check();
                if (count == 0) break;
                if (total + count > limit) throw new InvalidDataException("selected file exceeds size limit");
                sink(buffer.AsSpan(0, count));
                total += count;
                budget.Progress(count);
            }
        }
        catch (Exception ex) when (ex is ReadStalledException ||
            (ex is OperationCanceledException && budget.Stalled && !cancellationToken.IsCancellationRequested))
        {
            // A stall at the old EOF is complete only if the retained handle still has the inspected size.
            budget.CheckOverall();
            if (total == (long)metadata.Size && handle.Inspect().Size == metadata.Size)
            {
                budget.CheckOverall();
                return;
            }
            throw new IOException($"read-stalled: {total} of {metadata.Size} bytes; no progress within {StallDeadline.TotalSeconds:0}s");
        }
    }
    // One dedicated I/O thread owns the handle, buffers and sink through disposal. The caller
    // waits on a monitor, not a pool timer. If a syscall cannot be cancelled, that thread keeps
    // ownership until it returns; it cannot publish bytes or touch a disposed caller-owned sink.
    private sealed class ReadBudget
    {
        private readonly object _gate = new();
        private readonly TimeProvider _time;
        private readonly CancellationToken _caller;
        private readonly CancellationTokenSource _stop;
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
                        if (!budget._completed) throw refusal is ReadStalledException ? new IOException(refusal.Message) : refusal;
                        break;
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
            }
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
}
