using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
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
    private readonly HostLoadCaptureService? _history;
    private readonly Dictionary<(int Pid, DateTime StartedAt), (TimeSpan Cpu, long At)> _last = new();
    private DateTimeOffset _lastFault = DateTimeOffset.MinValue;
    private static int _selfPid;
    private static DateTime _selfStartedUtc;
    private static bool _selfCached;

    public HostStatsSamplerService(
        IHostStatsProbe probe,
        HostStatsStore store,
        IProcessCpuProbe cpu,
        Func<IReadOnlyList<HostStatsProcessTarget>> targets,
        Func<int, long?> workingSet,
        TimeProvider time,
        IOptions<HostStatsSettings> settings,
        ILogger<HostStatsSamplerService> logger, HostLoadCaptureService? history = null)
    {
        _probe = probe;
        _store = store;
        _cpu = cpu;
        _targets = targets;
        _workingSet = workingSet;
        _time = time;
        _settings = settings.Value;
        _logger = logger;
        _history = history;
    }

    public Task SampleOnceAsync(CancellationToken ct = default, bool logTick = true)
    {
        ct.ThrowIfCancellationRequested();
        var tickStarted = Stopwatch.GetTimestamp();
        var cpuStarted = ThreadCpuMilliseconds();
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
        var processes = _settings.ProcessSampling ? ReadProcesses(_time.GetTimestamp()) : [];
        var percent = read.Value.CpuPercent;
        var reason = read.Value.CpuUnknownReason;
        if (percent is { } value && (!double.IsFinite(value) || value < 0 || value > 100))
        { percent = null; reason = "invalid"; }
        var sample = read.Value with { At = now, CpuPercent = percent, Processes = processes, CpuUnknownReason = reason };
        _store.Add(sample);
        _history?.TryCapture(sample);
        if (logTick)
        {
            // Wall time charges time this thread was not running. The tick budget is its own CPU.
            var elapsedMs = cpuStarted is double started && ThreadCpuMilliseconds() is double cpuNow
                ? cpuNow - started
                : Stopwatch.GetElapsedTime(tickStarted).TotalMilliseconds;
            _logger.LogDebug("HOST_LOAD_TICK elapsedMs={ElapsedMs}", elapsedMs);
        }
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        // The cold call pays JIT. Logged ticks are the steady sample the overhead budget measures.
        await SampleOnceAsync(stoppingToken, logTick: false);
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

    private List<HostProcessSample> ReadProcesses(long now)
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

        var active = new HashSet<(int Pid, DateTime StartedAt)>();
        foreach (var target in targets)
        {
            if (target.Pid > 0)
                active.Add((target.Pid, target.StartedAt));
            if (target.HostPid is int host && host > 0 && host != target.Pid)
                active.Add((host, target.StartedAt));
        }
        foreach (var key in _last.Keys.Where(key => !active.Contains(key)).ToArray())
            _last.Remove(key);

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

    private HostProcessSample ReadOne(string name, string? sessionId, int pid, DateTime startedAt, long now)
    {
        // SamplerProcessQuery keeps the working set from this CPU read for the call below.
        // A later sample always reads again. Maximum extra staleness is zero intervals.
        TimeSpan? cpu = null;
        try
        {
            cpu = ReadProcessCpu(pid, startedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFault(ex);
        }

        long workingSet = 0;
        var workingSetKnown = false;
        try
        {
            if (_workingSet(pid) is long bytes)
            {
                workingSet = bytes;
                workingSetKnown = bytes >= 0;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFault(ex);
        }

        double? percent = null;
        if (cpu is { } current)
        {
            var key = (pid, startedAt);
            if (_last.TryGetValue(key, out var previous))
            {
                var elapsed = _time.GetElapsedTime(previous.At, now).TotalSeconds;
                var delta = (current - previous.Cpu).TotalSeconds;
                if (elapsed > 0 && delta >= 0)
                {
                    var calculated = delta / elapsed * 100.0;
                    if (double.IsFinite(calculated)) percent = calculated;
                }
            }

            _last[key] = (current, now);
        }

        return new HostProcessSample(name, sessionId, percent, workingSet) { WorkingSetKnown = workingSetKnown };
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
            // The runner pid and start do not change. Opening the process every five seconds
            // was the dominant sample cost on Linux.
            if (!_selfCached)
            {
                using var self = Process.GetCurrentProcess();
                _selfPid = self.Id;
                _selfStartedUtc = self.StartTime.ToUniversalTime();
                _selfCached = true;
            }
            list.Add(new HostStatsProcessTarget(null, _selfPid, null, _selfStartedUtc));
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

    internal IProcessCpuProbe CpuProbe => _cpu;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ThreadCpuTime
    {
        public readonly long Seconds;
        public readonly long Nanoseconds;
    }

    /// <summary>Linux <c>CLOCK_THREAD_CPUTIME_ID</c>. Windows uses thread cycles below.</summary>
    private const int ClockThreadCpuTimeId = 3;

    [DllImport("libc", EntryPoint = "clock_gettime")]
    private static extern int ClockGetTime(int clockId, out ThreadCpuTime time);

    [DllImport("kernel32", EntryPoint = "QueryThreadCycleTime")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

    [DllImport("kernel32", EntryPoint = "GetCurrentThread")]
    private static extern IntPtr GetCurrentThread();

    private static double _windowsCyclesPerMs;

    /// <summary>
    /// The calling thread's own CPU in milliseconds: Linux <c>CLOCK_THREAD_CPUTIME_ID</c>, Windows
    /// <c>QueryThreadCycleTime</c> (GetThreadTimes moves only at the 15.6 ms clock tick). Null when neither is available.
    /// </summary>
    internal static double? ThreadCpuMilliseconds()
    {
        if (OperatingSystem.IsLinux())
            return ClockGetTime(ClockThreadCpuTimeId, out var time) == 0 ? time.Seconds * 1000d + time.Nanoseconds / 1_000_000d : null;
        if (!OperatingSystem.IsWindows() || !QueryThreadCycleTime(GetCurrentThread(), out var cycles))
            return null;
        var rate = Volatile.Read(ref _windowsCyclesPerMs);
        if (rate <= 0)
            Volatile.Write(ref _windowsCyclesPerMs, rate = CalibrateWindowsCyclesPerMs());
        return rate > 0 ? cycles / rate : null;
    }

    /// <summary>
    /// Thread cycles accrue only while the thread runs, so a descheduled spin lowers cycles per wall
    /// millisecond. The highest of three short spins is the running rate; any error over-charges a tick.
    /// </summary>
    private static double CalibrateWindowsCyclesPerMs()
    {
        var best = 0d;
        for (var i = 0; i < 3; i++)
        {
            if (!QueryThreadCycleTime(GetCurrentThread(), out var before)) return 0;
            var started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2d) { }
            var wall = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!QueryThreadCycleTime(GetCurrentThread(), out var after)) return 0;
            best = Math.Max(best, (after - before) / wall);
        }
        return best;
    }

    private TimeSpan? ReadProcessCpu(int pid, DateTime startedAt)
    {
        // /proc replaces only the production process probe. A fixture probe owns its pid, including
        // one that currently belongs to a live process whose start is newer than the fixture clock.
        if (_cpu is SystemProcessCpuProbe && OperatingSystem.IsLinux() && LinuxProcessFacts.Trusted
            && LinuxProcessFacts.TryCpu(pid, startedAt, out var read))
            return read;
        return _cpu.TryGetTotalCpuTime(pid, startedAt);
    }
}

/// <summary>Linux sampler facts from /proc. A recycled pid stays null and never falls open onto another process.</summary>
internal static class LinuxProcessFacts
{
    private static readonly TimeSpan PidReuseTolerance = TimeSpan.FromMinutes(2);
    private static long _bootSeconds = long.MinValue;
    private static long _ticksPerSecond;
    private static int _trusted; // 0 unknown, 1 yes, -1 no

    internal static bool Trusted
    {
        get
        {
            if (_trusted != 0) return _trusted > 0;
            try
            {
                using var self = Process.GetCurrentProcess();
                _trusted = TryCpu(self.Id, self.StartTime.ToUniversalTime(), out var cpu)
                    && cpu is { } value && (value - self.TotalProcessorTime).Duration() < TimeSpan.FromSeconds(2) ? 1 : -1;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                _trusted = -1;
            }
            return _trusted > 0;
        }
    }

    internal static bool TryWorkingSet(int pid, out long? bytes)
    {
        bytes = null;
        try
        {
            var text = File.ReadAllText("/proc/" + pid.ToString() + "/statm");
            var span = text.AsSpan().Trim();
            var first = span.IndexOf(' ');
            if (first <= 0) return false;
            var rest = span[(first + 1)..];
            var second = rest.IndexOf(' ');
            var token = second < 0 ? rest : rest[..second];
            if (!long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var pages) || pages < 0)
                return false;
            bytes = pages * Environment.SystemPageSize;
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>False means the caller should use the process API. True with a null span is a recycled pid.</summary>
    internal static bool TryCpu(int pid, DateTime startedAt, out TimeSpan? cpu)
    {
        cpu = null;
        if (pid <= 0 || !TryFields(pid, out var utime, out var stime, out var startTicks) || !TryBoot(out var boot, out var hz))
            return false;
        var started = DateTime.UnixEpoch.AddSeconds(boot + startTicks / (double)hz);
        if (started > startedAt.ToUniversalTime() + PidReuseTolerance)
            return true;
        cpu = TimeSpan.FromSeconds((utime + stime) / (double)hz);
        return true;
    }

    private static bool TryFields(int pid, out long utime, out long stime, out long startTicks)
    {
        utime = stime = startTicks = 0;
        try
        {
            var stat = File.ReadAllText("/proc/" + pid.ToString() + "/stat");
            var close = stat.LastIndexOf(')');
            if (close < 0 || close + 2 >= stat.Length) return false;
            var fields = stat.AsSpan(close + 2);
            return Field(fields, 11, out utime) && Field(fields, 12, out stime) && Field(fields, 19, out startTicks);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Field(ReadOnlySpan<char> fields, int index, out long value)
    {
        value = 0;
        var seen = -1;
        var start = 0;
        for (var i = 0; i <= fields.Length; i++)
        {
            if (i != fields.Length && fields[i] != ' ') continue;
            if (i > start)
            {
                seen++;
                if (seen == index)
                    return long.TryParse(fields[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out value);
            }
            start = i + 1;
        }
        return false;
    }

    private static bool TryBoot(out long bootSeconds, out long ticksPerSecond)
    {
        if (_ticksPerSecond > 0 && _bootSeconds != long.MinValue)
        {
            bootSeconds = _bootSeconds;
            ticksPerSecond = _ticksPerSecond;
            return true;
        }
        bootSeconds = 0;
        ticksPerSecond = Sysconf(2);
        if (ticksPerSecond <= 0) return false;
        foreach (var line in File.ReadLines("/proc/stat"))
        {
            if (!line.StartsWith("btime ", StringComparison.Ordinal)) continue;
            if (!long.TryParse(line.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out bootSeconds)) return false;
            _bootSeconds = bootSeconds;
            _ticksPerSecond = ticksPerSecond;
            return true;
        }
        return false;
    }

    [DllImport("libc", EntryPoint = "sysconf")]
    private static extern long Sysconf(int name);
}
