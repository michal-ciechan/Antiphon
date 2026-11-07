using System.Threading.Channels;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.HostLoad;

/// <summary>Two pending 1MiB pages per runner. Refusal retains the runner spool's unacknowledged page.</summary>
public sealed class HostLoadIngestPump(IServiceScopeFactory scopes, ISessionRunnerDirectory directory,
    IOptions<HostLoadHistorySettings> settings, TimeProvider time, ILogger<HostLoadIngestPump> logger) : BackgroundService
{
    private readonly Channel<HostLoadIngestBatch> _queue = Channel.CreateBounded<HostLoadIngestBatch>(256);
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _consumer = new(1, 1);
    public long RefusedBatches { get; private set; }
    public long CommittedBatches { get; private set; }
    public string? LastFailure { get; private set; }
    public bool TryEnqueue(HostLoadIngestBatch batch)
    {
        HostLoadIngestService.Validate(batch);
        lock (_gate)
        {
            var runner = HostLoadCounterCache.Canonical(batch.RunnerId);
            var count = _pending.GetValueOrDefault(runner);
            if (count >= settings.Value.IngestQueueCapacityPerRunner || (_pending.Count >= 128 && !_pending.ContainsKey(runner)))
            { RefusedBatches++; return false; }
            _pending[runner] = count + 1;
            if (_queue.Writer.TryWrite(batch)) return true;
            _pending[runner]--; RefusedBatches++; return false;
        }
    }
    public async Task<bool> PollOnceAsync(string runnerId, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var canonical = HostLoadCounterCache.Canonical(runnerId);
        var after = await db.HostLoadIngestCursors.AsNoTracking().Where(c => c.RunnerId == canonical).Select(c => c.Cursor).SingleOrDefaultAsync(linked.Token);
        var page = await directory.Resolve(runnerId).GetHostLoadHistoryAsync(after, 120, linked.Token);
        if (page is null) return false;
        return TryEnqueue(new(canonical, after, page));
    }
    public async Task<HostLoadIngestReceipt> ProcessOneAsync(CancellationToken ct)
    {
        await _consumer.WaitAsync(ct);
        try
        {
            var batch = await _queue.Reader.ReadAsync(ct);
            lock (_gate) _pending[HostLoadCounterCache.Canonical(batch.RunnerId)]--;
            using var scope = scopes.CreateScope();
            var receipt = await scope.ServiceProvider.GetRequiredService<HostLoadIngestService>().IngestAsync(batch, ct);
            CommittedBatches++; LastFailure = null; return receipt;
        }
        finally { _consumer.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled) return;
        await Task.Yield(); // No history DB/HTTP work delays hosted startup or the live sampler.
        await Task.WhenAll(ProduceAsync(stoppingToken), ConsumeAsync(stoppingToken));
    }
    private async Task ProduceAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(settings.Value.PollIntervalMs), time);
        do
        {
            foreach (var runner in directory.KnownRunnerIds.Distinct(StringComparer.Ordinal).Take(128))
                try { await PollOnceAsync(runner, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception) { LastFailure = "history_read_unavailable"; }
        } while (await timer.WaitForNextTickAsync(ct));
    }
    private async Task ConsumeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await ProcessOneAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception)
            {
                LastFailure = "history_commit_unavailable";
                logger.LogWarning("Host load ingest failed; the committed cursor is unchanged and spool replay remains pending");
            }
        }
    }
}
