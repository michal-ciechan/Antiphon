using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>CARD-0718. A live session pid the sampler may charge. <see cref="HostPid"/> is the pty host.</summary>
public readonly record struct HostStatsProcessTarget(string? SessionId, int Pid, int? HostPid, DateTime StartedAt);

/// <summary>
/// CARD-0718. One sample immediately, then every <c>IntervalMs</c> on the injected clock.
/// A faulting probe is logged at most once a minute and the tick is skipped. Process CPU is the
/// delta over the measured wall between samples, never the configured interval and never zero
/// when the probe cannot say.
/// </summary>
public sealed class HostStatsSamplerService : BackgroundService
{
    private readonly IHostStatsProbe _probe;
    private readonly HostStatsStore _store;
    private readonly IProcessCpuProbe _cpu;
    private readonly Func<IReadOnlyList<HostStatsProcessTarget>> _targets;
    private readonly Func<int, long?> _workingSet;
    private readonly TimeProvider _time;
    private readonly HostStatsSettings _settings;
    private readonly ILogger<HostStatsSamplerService> _logger;
    private readonly Dictionary<int, (TimeSpan Cpu, DateTimeOffset At)> _last = new();
    private DateTimeOffset _lastFault = DateTimeOffset.MinValue;

    public HostStatsSamplerService(
        IHostStatsProbe probe,
        HostStatsStore store,
        IProcessCpuProbe cpu,
        Func<IReadOnlyList<HostStatsProcessTarget>> targets,
        Func<int, long?> workingSet,
        TimeProvider time,
        IOptions<HostStatsSettings> settings,
        ILogger<HostStatsSamplerService> logger)
    {
        _probe = probe;
        _store = store;
        _cpu = cpu;
        _targets = targets;
        _workingSet = workingSet;
        _time = time;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task SampleOnceAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        HostSample? read;
        try
        {
            read = _probe.Read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFault(ex);
            return Task.CompletedTask;
        }

        if (read is null)
            return Task.CompletedTask;

        var now = _time.GetUtcNow();
        var processes = _settings.ProcessSampling ? ReadProcesses(now) : [];
        _store.Add(read.Value with { At = now, Processes = processes });
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        await SampleOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.IntervalMs), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SampleOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private List<HostProcessSample> ReadProcesses(DateTimeOffset now)
    {
        IReadOnlyList<HostStatsProcessTarget> targets;
        try
        {
            targets = _targets();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFault(ex);
            return [];
        }

        var list = new List<HostProcessSample>(targets.Count);
        foreach (var target in targets)
        {
            if (target.Pid > 0)
                list.Add(ReadOne(target.SessionId is null ? "runner" : "session", target.SessionId, target.Pid, target.StartedAt, now));
            if (target.HostPid is int host && host > 0 && host != target.Pid)
                list.Add(ReadOne("pty-host", target.SessionId, host, target.StartedAt, now));
        }

        return list;
    }

    private HostProcessSample ReadOne(string name, string? sessionId, int pid, DateTime startedAt, DateTimeOffset now)
    {
        TimeSpan? cpu = null;
        try
        {
            cpu = _cpu.TryGetTotalCpuTime(pid, startedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFault(ex);
        }

        long workingSet = 0;
        try
        {
            if (_workingSet(pid) is long bytes)
                workingSet = bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFault(ex);
        }

        double? percent = null;
        if (cpu is { } current)
        {
            if (_last.TryGetValue(pid, out var previous))
            {
                var wall = (now - previous.At).TotalSeconds;
                if (wall > 0)
                    percent = (current - previous.Cpu).TotalSeconds / wall * 100.0;
            }

            _last[pid] = (current, now);
        }

        return new HostProcessSample(name, sessionId, percent, workingSet);
    }

    private void LogFault(Exception ex)
    {
        var now = _time.GetUtcNow();
        if (now - _lastFault < TimeSpan.FromMinutes(1))
            return;
        _lastFault = now;
        _logger.LogWarning(ex, "Host stats probe failed; skipping this tick");
    }

    internal static IReadOnlyList<HostStatsProcessTarget> ReadTargets(IServiceProvider services)
    {
        var list = new List<HostStatsProcessTarget>();
        try
        {
            using var self = Process.GetCurrentProcess();
            list.Add(new HostStatsProcessTarget(null, self.Id, null, self.StartTime.ToUniversalTime()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        if (services.GetService<SessionRunnerRuntime>() is not { } runtime)
            return list;
        foreach (var session in runtime.List())
        {
            if (session.Status == "Exited" || session.Pid is not int pid || pid <= 0)
                continue;
            list.Add(new HostStatsProcessTarget(session.SessionId.ToString(), pid, session.HostPid, session.StartedAt));
        }

        return list;
    }

    internal static long? WorkingSet(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
                return null;
            return process.WorkingSet64;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
