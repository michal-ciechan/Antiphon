using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.PtyHost.Protocol;

/// <summary>
/// Owner memory proves only an attempted stop, never the kernel's sender. Files are untrusted
/// diagnostics. No writer or logger runs on the signal/publication thread or under its gate.
/// </summary>
public sealed class ProcessStopIntentStore : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ProcessStopIntentDto> _attempts = new();
    private readonly Queue<Guid> _attemptOrder = new();
    private readonly object _writerGate = new();
    private readonly Channel<WriteOffer> _offers = Channel.CreateBounded<WriteOffer>(new BoundedChannelOptions(1) {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Func<ProcessStopIntentDto, CancellationToken, Task<bool>> _writer;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _warning;
    private readonly string _root;
    private readonly Task _worker;
    private WriteOffer? _active;
    private bool _disposed;
    private int _warned;
    private Task<bool> _lastRecord = Task.FromResult(false);
    public Guid BootId { get; } = Guid.NewGuid();
    public Task<bool> LastRecord => Volatile.Read(ref _lastRecord);
    public Task WorkerCompletion => _worker;

    public ProcessStopIntentStore(string root,
        Func<ProcessStopIntentDto, CancellationToken, Task<bool>>? writer = null,
        TimeProvider? clock = null, Action<string>? warning = null)
    {
        _root = root;
        _clock = clock ?? TimeProvider.System;
        _writer = writer ?? WriteDiagnosticAsync;
        _warning = warning;
        _worker = Task.Run(WriterLoopAsync);
    }

    public ProcessStopIntentDto? Snapshot(Guid sessionId, DateTime generation, int? signal = null)
    {
        lock (_gate)
            return _attemptOrder.Reverse().Select(id => _attempts[id]).FirstOrDefault(value => value.SessionId == sessionId
                && SessionGeneration.Equal(value.AcceptedStartedAt, generation)
                && (signal is null || value.AttemptedSignal == signal));
    }

    public ProcessStopIntentDto Attempt(ProcessStopIntentDto request, Func<bool?> send)
    {
        var attempt = request with { IssuerBootId = BootId, Provenance = ProcessStopProvenance.LiveIssuer,
            Outcome = ProcessStopOutcome.Requested, Durable = false };
        var helper = request.Target is ProcessStopTarget.Custody or ProcessStopTarget.Helper;
        lock (_gate)
        {
            if (!_attempts.ContainsKey(attempt.RequestId)) _attemptOrder.Enqueue(attempt.RequestId);
            _attempts[attempt.RequestId] = attempt;
            // Bound diagnostics memory; never reconstruct evicted authority from disk.
            while (_attemptOrder.Count > 256) _attempts.Remove(_attemptOrder.Dequeue());
            // Direct signal publication stays atomic with respect to the exit observer.
            // A helper can take 45 seconds and must never hold this shared gate.
            if (!helper) attempt = CompleteAttempt(attempt, send);
        }
        if (helper)
        {
            var completed = Send(attempt, send);
            lock (_gate)
            {
                // An old helper finishing cannot resurrect evicted issuer authority.
                if (_attempts.ContainsKey(attempt.RequestId)) _attempts[attempt.RequestId] = completed;
            }
            attempt = completed;
        }
        // Signal and issued-memory publication have finished. This offer never invokes IO inline.
        Volatile.Write(ref _lastRecord, RecordAndEnrichAsync(attempt));
        return attempt;
    }

    private ProcessStopIntentDto CompleteAttempt(ProcessStopIntentDto attempt, Func<bool?> send)
    {
        var completed = Send(attempt, send);
        _attempts[attempt.RequestId] = completed;
        return completed;
    }

    private static ProcessStopIntentDto Send(ProcessStopIntentDto attempt, Func<bool?> send)
    {
        bool? sent;
        try { sent = send(); }
        catch (Exception) { sent = false; }
        return attempt with { Outcome = sent switch { true => ProcessStopOutcome.Issued,
            false => ProcessStopOutcome.Failed, _ => ProcessStopOutcome.Requested } };
    }

    private async Task<bool> RecordAndEnrichAsync(ProcessStopIntentDto attempt)
    {
        var durable = await TryRecordAsync(attempt).ConfigureAwait(false);
        if (durable)
            lock (_gate)
                if (_attempts.TryGetValue(attempt.RequestId, out var current) && current == attempt)
                    _attempts[attempt.RequestId] = current with { Durable = true };
        return durable;
    }

    /// <summary>100 ms from offer including queueing. A deadline does not cancel a kernel fsync.</summary>
    public Task<bool> TryRecordAsync(ProcessStopIntentDto intent, CancellationToken ct = default)
    {
        var offer = new WriteOffer(intent, _clock, ct);
        lock (_writerGate)
        {
            // One stuck worker remains owned. Do not accumulate background IO behind it.
            if (_disposed || _active is { Expired: true } || !_offers.Writer.TryWrite(offer))
                { offer.Completion.TrySetResult(false); offer.Enqueued = false; }
            else offer.Enqueued = true;
        }
        return ObserveOfferAsync(offer);
    }

    private async Task<bool> ObserveOfferAsync(WriteOffer offer)
    {
        await Task.Yield(); // including saturation/failure diagnostics: never run a logger inline
        bool durable = false;
        try
        {
            durable = await offer.Completion.Task.WaitAsync(offer.Token)
                .ConfigureAwait(false);
            // Timer starts at construction, not when this continuation was scheduled.
            durable &= !offer.Expired;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
        finally
        {
            offer.Expire();
            offer.Completion.TrySetResult(false);
            if (!offer.Enqueued) offer.Dispose();
        }
        if (!durable && Interlocked.Exchange(ref _warned, 1) == 0)
        {
            ThreadPool.QueueUserWorkItem(_ => {
                try { _warning?.Invoke("Stop diagnostic record unavailable; issuer memory retained."); } catch { }
            });
        }
        return durable;
    }

    private async Task WriterLoopAsync()
    {
        await foreach (var offer in _offers.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_writerGate) _active = offer;
            try
            {
                if (!offer.Expired)
                {
                    // An injected writer may block even before returning its Task. Only this
                    // bounded worker invokes it; neither stop nor observer joins that IO.
                    var result = await _writer(offer.Intent, offer.Token).ConfigureAwait(false);
                    offer.Completion.TrySetResult(result && !offer.Expired);
                }
            }
            catch (Exception) { offer.Completion.TrySetResult(false); }
            finally
            {
                offer.Completion.TrySetResult(false);
                offer.Dispose();
                lock (_writerGate) _active = null;
            }
        }
    }

    private Task<bool> WriteDiagnosticAsync(ProcessStopIntentDto intent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_root);
        var final = Path.Combine(_root, FileNameFor(intent));
        var temporary = final + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(file, intent with { Durable = true }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                file.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, final); // never overwrite another attempt
            if (OperatingSystem.IsLinux())
            {
                var fd = Open(_root, 0x10000 /* O_DIRECTORY */);
                if (fd < 0) throw new IOException("Stop diagnostic directory open failed.");
                try { if (Fsync(fd) != 0) throw new IOException("Stop diagnostic directory fsync failed."); }
                finally { Close(fd); }
            }
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    public ProcessStopIntentDto StopProcess(Process process, Guid sessionId, DateTime generation,
        ProcessStopTarget target, ProcessStopPath path, Guid hostInstanceId = default) => Attempt(new() {
            RequestId = Guid.NewGuid(), SessionId = sessionId, AcceptedStartedAt = generation,
            HostInstanceId = hostInstanceId, RequestedAtUtc = _clock.GetUtcNow().UtcDateTime,
            TargetPid = process.Id, TargetStartedAtUtc = process.StartTime.ToUniversalTime(),
            Target = target, AttemptedSignal = 9, Path = path
        }, () => {
            if (process.HasExited) return false;
            process.Kill(entireProcessTree: true);
            return true;
        });

    /// <summary>Recovery-only IO. A file can add diagnostics, never issuer authority.</summary>
    public static ProcessStopIntentDto? ReadRecovered(string root, Guid sessionId, DateTime generation)
    {
        try
        {
            if (!Directory.Exists(root)) return null;
            ProcessStopIntentDto? latest = null;
            // Bounded census; absence or malformed/oversized records is diagnostic loss only.
            foreach (var path in Directory.EnumerateFiles(root, $"{sessionId:N}-{generation.Ticks}-*.json").Take(64))
            {
                var file = new FileInfo(path);
                if (file.LinkTarget is not null || file.Length > 4096) continue;
                var value = JsonSerializer.Deserialize<ProcessStopIntentDto>(File.ReadAllText(path),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (value?.SessionId != sessionId || !SessionGeneration.Equal(value.AcceptedStartedAt, generation)) continue;
                if (latest is null || value.RequestedAtUtc > latest.RequestedAtUtc)
                    latest = value with { Provenance = ProcessStopProvenance.RecoveredUnverified };
            }
            return latest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static string FileNameFor(ProcessStopIntentDto intent) =>
        $"{intent.SessionId:N}-{intent.AcceptedStartedAt.Ticks}-{intent.RequestId:N}.json";

    public void Dispose()
    {
        lock (_writerGate) { _disposed = true; _offers.Writer.TryComplete(); }
        // Deliberately no join: kernel IO may never return. The bounded worker observes its faults.
    }

    private sealed class WriteOffer : IDisposable
    {
        private readonly TimeProvider _clock;
        private readonly long _offeredAt;
        public ProcessStopIntentDto Intent { get; }
        public CancellationToken Caller { get; }
        private readonly CancellationTokenSource _budget;
        private readonly CancellationTokenSource _linked;
        public CancellationToken Token { get; }
        public bool Enqueued { get; set; }
        public bool Expired => Token.IsCancellationRequested
            || _clock.GetElapsedTime(_offeredAt) >= TimeSpan.FromMilliseconds(100);
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WriteOffer(ProcessStopIntentDto intent, TimeProvider clock, CancellationToken caller)
        {
            _clock = clock; _offeredAt = clock.GetTimestamp();
            Intent = intent; Caller = caller;
            _budget = new(TimeSpan.FromMilliseconds(100), clock);
            _linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _budget.Token);
            Token = _linked.Token;
        }
        public void Expire() { try { _budget.Cancel(); } catch (ObjectDisposedException) { } }
        public void Dispose() { _linked.Dispose(); _budget.Dispose(); }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)] private static extern int Fsync(int fd);
    [DllImport("libc", EntryPoint = "close")] private static extern int Close(int fd);
}
