using System.Collections.Concurrent;
using System.Threading.Channels;
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
    private readonly Func<HostLoadSpool> _open;
    private HostLoadSpool? _spool;
    private long _sequence;
    private long _dropped;
    public long DroppedSamples => Interlocked.Read(ref _dropped);
    public long SpoolBytes { get; private set; }
    public DateTimeOffset? LastAppendedAt { get; private set; }

    public HostLoadCaptureService(IOptions<HostStatsSettings> settings, TimeProvider time,
        IHostAdmissionSnapshotReader authority, ILogger<HostLoadCaptureService> logger,
        PhoneHomeProcessIdentity? identity = null, Func<HostLoadSpool>? open = null)
    {
        _settings = settings.Value;
        _time = time;
        _authority = authority;
        _logger = logger;
        _boot = identity?.BootId ?? Guid.NewGuid();
        _open = open ?? (() => new HostLoadSpool(_settings.HistoryPath!, _settings, time, _boot));
        _pending = Channel.CreateBounded<(long, HostSample)>(new BoundedChannelOptions(_settings.AppendQueueCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public bool TryCapture(HostSample sample)
    {
        if (!_settings.Enabled || !_settings.HistoryEnabled || _settings.HistoryPath is null) return false;
        var sequence = Interlocked.Increment(ref _sequence);
        if (_pending.Writer.TryWrite((sequence, sample))) return true;
        NoteLoss(sequence);
        return false;
    }

    private void NoteLoss(long sequence)
    {
        Interlocked.Increment(ref _dropped);
        if (_lost.Count < 256) _lost.Enqueue(sequence);
    }

    public HostLoadHistoryPage ReadHistory(string? after, int limit) =>
        Volatile.Read(ref _spool)?.ReadHistory(after, limit) ?? throw new IOException("host_load_history_unavailable");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.HistoryEnabled || _settings.HistoryPath is null) return;
        // Hosted startup must yield before any filesystem access.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var spool = _spool ?? _open();
                Volatile.Write(ref _spool, spool);
                while (_lost.TryDequeue(out var lost)) spool.RecordLoss(new(_settings.RunnerId, spool.RunnerStoreId, _boot, lost));
                var item = await _pending.Reader.ReadAsync(stoppingToken);
                try
                {
                    var source = new HostLoadIdentity(_settings.RunnerId, spool.RunnerStoreId, _boot).At(item.Sequence);
                    var authority = await _authority.ReadAsync(stoppingToken);
                    spool.Append(HostLoadProjection.Create(item.Sample, source, _settings, authority));
                    SpoolBytes = spool.Bytes;
                    LastAppendedAt = item.Sample.At;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { NoteLoss(item.Sequence); throw; }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogWarning("Host load append unavailable; live sampling continues; dropped={Dropped}", DroppedSamples);
                await Task.Delay(TimeSpan.FromSeconds(5), _time, stoppingToken);
            }
        }
    }
}
