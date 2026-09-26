using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class HostStatsPollService : BackgroundService
{
    private readonly ISessionRunnerDirectory _directory;
    private readonly HostStatsCache _cache;
    private readonly IHostStatsAntiphonCounters _counters;
    private readonly IEventBus _bus;
    private readonly HostStatsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<HostStatsPollService> _logger;
    private readonly int _desktopCapacity;

    public HostStatsPollService(ISessionRunnerDirectory directory, HostStatsCache cache,
        IHostStatsAntiphonCounters counters, IEventBus bus, IOptions<HostStatsSettings> settings,
        TimeProvider time, ILogger<HostStatsPollService> logger,
        IOptions<DelegationSettings>? delegation = null)
    {
        _directory = directory;
        _cache = cache;
        _counters = counters;
        _bus = bus;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
        _desktopCapacity = delegation?.Value.MaxConcurrentTasks ?? new DelegationSettings().MaxConcurrentTasks;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;
        await TickOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.PollIntervalMs), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await TickOnceAsync(stoppingToken);
    }

    public async Task TickOnceAsync(CancellationToken ct = default)
    {
        if (!_settings.Enabled)
            return;
        _cache.BeginTick();
        var ids = _directory.KnownRunnerIds
            .Select(id => RunnerRequestIntent.IsDesktopAlias(id) ? RunnerPlatformWire.DesktopId : id)
            .Distinct(StringComparer.Ordinal).ToArray();
        var reads = ids.Select(id => PollHostAsync(id, ct)).ToArray();
        await Task.WhenAll(reads);
        IReadOnlyDictionary<string, HostStatsAntiphonDto> counts;
        try { counts = await _counters.ReadAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Host stats task counter read failed");
            counts = new Dictionary<string, HostStatsAntiphonDto>();
        }
        var rows = await Task.WhenAll(ids.Select(id => DescribeAsync(id, ct)));
        var annotated = counts.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var id in ids)
        {
            annotated.TryGetValue(id, out var count);
            count ??= new HostStatsAntiphonDto(0, new Dictionary<string, int>(),
                new Dictionary<string, int>(), 0, 0, 0, null, null, null);
            int? sessions = id == RunnerPlatformWire.DesktopId ? count.TasksInFlight
                : (_directory as PhoneHomeRunnerDirectory)?.SnapshotLive(id)?.KnownLiveSessions().Count;
            annotated[id] = count with { SessionsLive = sessions };
        }
        _cache.SetProjection(rows, annotated);
        var projected = _cache.Project();
        if (!_cache.Changed)
            return;
        try
        {
            await _bus.PublishToGroupAsync("hosts", "HostStatsUpdated", projected, ct);
            _cache.Published();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Host stats SignalR publish failed");
        }
    }

    private async Task PollHostAsync(string id, CancellationToken ct)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(_settings.RequestTimeoutMs), _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            var answer = await _directory.Resolve(id).GetHostStatsAsync(linked.Token);
            if (answer is null)
                _cache.RecordFailure(id, HostStatsFailureKind.Transient);
            else
                _cache.Record(id, id == RunnerPlatformWire.DesktopId ? AddServerProcess(answer) : answer);
        }
        catch (HostStatsUnsupportedException) { _cache.RecordFailure(id, HostStatsFailureKind.Unsupported); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var kind = ex is Antiphon.Server.Application.Exceptions.ServiceUnavailableException
                ? HostStatsFailureKind.Offline : HostStatsFailureKind.Transient;
            _cache.RecordFailure(id, kind);
            _logger.LogDebug(ex, "Host stats poll failed for {HostId}", id);
        }
    }

    private static RunnerHostStatsDto AddServerProcess(RunnerHostStatsDto answer)
    {
        if (answer.Current is null)
            return answer;
        using var process = Process.GetCurrentProcess();
        var server = new RunnerHostProcessDto("antiphon.server", null, null, process.WorkingSet64);
        return answer with { Current = answer.Current with { Processes = [.. answer.Current.Processes, server] } };
    }

    private async Task<SessionRunnerCatalogueEntryDto> DescribeAsync(string id, CancellationToken ct)
    {
        RunnerDescriptor? descriptor = null;
        try { descriptor = await _directory.DescribeAsync(id, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Host stats descriptor failed for {HostId}", id);
        }
        var desktop = id == RunnerPlatformWire.DesktopId;
        return new SessionRunnerCatalogueEntryDto(id, descriptor?.DisplayName ?? id,
            descriptor?.Platform, descriptor?.PlatformObservedAt,
            desktop || descriptor?.Available == true, desktop || descriptor?.DispatchEligible == true,
            descriptor?.Stale == true ? "stale" : "unavailable",
            desktop ? _desktopCapacity : descriptor?.Capacity, null,
            desktop ? "delegatedTasks" : "sessions", null, descriptor?.Stale == true,
            descriptor?.Capabilities?.Features ?? []);
    }
}
