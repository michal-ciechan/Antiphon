using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json;
using System.Security;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>Bounded append worker. The sampler only copies into TryWrite; it never waits for disk or HTTP.</summary>
public sealed class HostLoadCaptureService : BackgroundService, IHostLoadHistorySource
{
    private readonly Channel<(long Sequence, HostSample Sample)> _pending;
    private readonly ConcurrentQueue<long> _lost = new();
    private readonly HostStatsSettings _settings;
    private readonly TimeProvider _time;
    private readonly IHostAdmissionSnapshotReader _authority;
    private readonly ILogger<HostLoadCaptureService> _logger;
    private readonly Guid _boot;
    private readonly Func<HostLoadSpool>? _open;
    private HostLoadSpool? _spool;
    private long _sequence;
    private long _dropped;
    private readonly object _diagnosticGate = new();
    private readonly Dictionary<string, long> _dropReasons = new();
    private readonly Queue<HostLoadCollectorDiagnostic> _recent = new();
    private long _accepted, _appended, _authorityUnknown, _failures;
    private long _spoolBytes;
    private DateTimeOffset? _lastAppendedAt;
    private HostLoadCollectorDiagnostic? _lastError;
    private DateTimeOffset? _nextWarning;
    private string _pathClass = "unresolved";
    public long DroppedSamples => Interlocked.Read(ref _dropped);
    public long SpoolBytes => Interlocked.Read(ref _spoolBytes);
    public DateTimeOffset? LastAppendedAt { get { lock (_diagnosticGate) return _lastAppendedAt; } }
    public HostLoadCollectorDiagnostics Diagnostics
    {
        get
        {
            lock (_diagnosticGate)
                return new(_accepted, DroppedSamples, new Dictionary<string, long>(_dropReasons),
                    _appended, Volatile.Read(ref _spool)?.CursorAdvances ?? 0, _authorityUnknown, _failures,
                    _lastAppendedAt, _lastError?.ErrorClass, _lastError?.At, _recent.ToArray());
        }
    }

    public HostLoadCaptureService(IOptions<HostStatsSettings> settings, TimeProvider time,
        IHostAdmissionSnapshotReader authority, ILogger<HostLoadCaptureService> logger,
        PhoneHomeProcessIdentity? identity = null, Func<HostLoadSpool>? open = null)
    {
        _settings = settings.Value;
        _time = time;
        _authority = authority;
        _logger = logger;
        _boot = identity?.BootId ?? Guid.NewGuid();
        _open = open;
        _pending = Channel.CreateBounded<(long, HostSample)>(new BoundedChannelOptions(_settings.AppendQueueCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public bool TryCapture(HostSample sample)
    {
        if (!_settings.Enabled || !_settings.HistoryEnabled) return false;
        var sequence = Interlocked.Increment(ref _sequence);
        if (_pending.Writer.TryWrite((sequence, sample)))
        { lock (_diagnosticGate) _accepted++; return true; }
        NoteLoss(sequence, "queue-full");
        return false;
    }

    private void NoteLoss(long sequence, string reason)
    {
        lock (_diagnosticGate)
        {
            Interlocked.Increment(ref _dropped);
            _dropReasons[reason] = _dropReasons.GetValueOrDefault(reason) + 1;
            if (_lost.Count < 256) _lost.Enqueue(sequence);
        }
    }

    public HostLoadHistoryPage ReadHistory(string? after, int limit) =>
        (Volatile.Read(ref _spool)?.ReadHistory(after, limit) ?? throw new IOException("host_load_history_unavailable"))
        with { Collector = Diagnostics };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.HistoryEnabled) return;
        // Hosted startup must yield before any filesystem access.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            var stage = "resolve";
            try
            {
                var spool = _spool;
                if (spool is null)
                {
                    // DoNotVerify preserves the absolute profile location even before it exists.
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                        Environment.SpecialFolderOption.DoNotVerify);
                    var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                        Environment.SpecialFolderOption.DoNotVerify);
                    _pathClass = _settings.HistoryPath is not null
                        ? Path.IsPathFullyQualified(_settings.HistoryPath) ? "configured-absolute" : "configured-relative"
                        : Path.IsPathFullyQualified(local) ? "local-data" : "user-data";
                    var path = HostLoadStoragePath.Resolve(_settings.HistoryPath, local, profile);
                    stage = "open";
                    spool = _open?.Invoke() ?? new HostLoadSpool(path, _settings, _time, _boot);
                    Volatile.Write(ref _spool, spool);
                }
                stage = "loss-ledger";
                while (_lost.TryDequeue(out var lost)) spool.RecordLoss(new(_settings.RunnerId, spool.RunnerStoreId, _boot, lost));
                var item = await _pending.Reader.ReadAsync(stoppingToken);
                try
                {
                    var source = new HostLoadIdentity(_settings.RunnerId, spool.RunnerStoreId, _boot).At(item.Sequence);
                    stage = "authority";
                    var authority = await _authority.ReadAsync(stoppingToken);
                    if (authority.State != "known") { lock (_diagnosticGate) _authorityUnknown++; }
                    stage = "append";
                    spool.Append(HostLoadProjection.Create(item.Sample, source, _settings, authority));
                    lock (_diagnosticGate) { _appended++; _lastAppendedAt = item.Sample.At; }
                }
                catch (Exception ex) when (Expected(ex))
                { NoteLoss(item.Sequence, stage == "authority" ? "authority-error" : "append-" + ErrorClass(ex, stage)); throw; }
                stage = "storage-stats";
                Interlocked.Exchange(ref _spoolBytes, spool.Bytes);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (Expected(ex))
            {
                // Register before publishing the failure; clock-driven observers can advance safely.
                var retry = Task.Delay(TimeSpan.FromSeconds(5), _time, stoppingToken);
                ReportFailure(stage, ex);
                await retry;
            }
        }
    }

    private static bool Expected(Exception ex) => ex is IOException or UnauthorizedAccessException
        or ArgumentException or JsonException or NotSupportedException or FormatException or SecurityException;

    private static string ErrorClass(Exception ex, string stage) => ex switch
    {
        UnauthorizedAccessException or SecurityException => "permission",
        JsonException or FormatException => stage == "append" ? "serialization" : "ledger-recovery",
        PathTooLongException or NotSupportedException => "path",
        ArgumentException => stage == "append" ? "validation-serialization" : "path",
        _ => "io"
    };

    private void ReportFailure(string stage, Exception ex)
    {
        var error = ErrorClass(ex, stage);
        var action = stage == "resolve" ? "configure-absolute-history-directory"
            : error == "ledger-recovery" ? "inspect-private-store-integrity"
            : stage == "authority" ? "check-admission-authority"
            : stage == "append" && error is "validation-serialization" or "serialization" ? "check-sample-schema"
            : "check-history-directory-access";
        // An unsuccessful open is not writable by this collector. Other I/O failures do not
        // prove writability; never infer permission from a previous successful open.
        bool? writable = stage is "resolve" or "open" || error == "permission" ? false : null;
        var record = new HostLoadCollectorDiagnostic(_time.GetUtcNow(), stage, _pathClass, error, writable, action);
        bool warn;
        lock (_diagnosticGate)
        {
            _failures++; _lastError = record;
            if (_recent.Count == 8) _recent.Dequeue();
            _recent.Enqueue(record);
            warn = _nextWarning is null || record.At >= _nextWarning;
            if (warn) _nextWarning = record.At + TimeSpan.FromMinutes(1);
        }
        if (warn)
            _logger.LogWarning("Host load collector unavailable stage={Stage} pathClass={PathClass} errorClass={ErrorClass} writable={Writable} action={Action} dropped={Dropped}; live sampling continues",
                stage, _pathClass, error, writable, action, DroppedSamples);
    }
}
