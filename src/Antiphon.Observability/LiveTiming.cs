using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Antiphon.Observability;

public enum StartupOperation { ConfigLoad, DatabaseConnect, DatabaseCreate, Migrate, Seed, Setup, HostBuild, HostStart, ListenersReady, RunnerRegistration, Adoption }
public enum ExternalOperation { Database, Git, Http, Filesystem }
public sealed record StartupMeasurement(string Phase, string Operation, double ElapsedMs, string Outcome);
public sealed record StartupSnapshot(string RunId, IReadOnlyList<StartupMeasurement> Phases);

/// <summary>Instance-owned timing. Labels are closed enums; payloads cannot enter telemetry.</summary>
public sealed class LiveTiming(ILogger<LiveTiming> logger) : IDisposable
{
    public const string SourceName = "Antiphon.Live";
    private readonly ActivitySource _source = new(SourceName);
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly object _gate = new();
    private readonly Queue<StartupMeasurement> _startup = new();
    private ILogger<LiveTiming> _logger = logger;

    // The bootstrap sink is replaced after the host builds; no process-global logger is owned here.
    public void AttachLogger(ILogger<LiveTiming> logger) => Volatile.Write(ref _logger, logger);

    public Phase BeginStartup(StartupOperation operation)
    {
        var (phase, name) = operation switch
        {
            StartupOperation.ConfigLoad => ("fixture-setup", "config-load"),
            StartupOperation.DatabaseConnect => ("fixture-setup", "database-connect"),
            StartupOperation.DatabaseCreate => ("db-create", "create"),
            StartupOperation.Migrate => ("db-migrate", "migrate"),
            StartupOperation.Seed => ("fixture-setup", "seed"),
            StartupOperation.HostBuild => ("host-startup", "host-build"),
            StartupOperation.HostStart => ("host-startup", "host-start"),
            StartupOperation.ListenersReady => ("host-startup", "listeners-ready"),
            StartupOperation.RunnerRegistration => ("network", "runner-registration"),
            StartupOperation.Adoption => ("fixture-setup", "adoption"),
            _ => ("fixture-setup", "setup")
        };
        return new Phase(this, phase, name, true);
    }

    public Phase BeginExternal(ExternalOperation operation)
    {
        var (phase, name) = ExternalLabels(operation);
        return new Phase(this, phase, name, false);
    }

    public void RecordExternal(ExternalOperation operation, TimeSpan duration, bool succeeded)
    {
        var (phase, name) = ExternalLabels(operation);
        Finish(phase, name, duration.TotalMilliseconds, succeeded, false, null);
    }

    private static (string Phase, string Name) ExternalLabels(ExternalOperation operation) => operation switch
    {
        ExternalOperation.Database => ("db-query", "database"),
        ExternalOperation.Git => ("process-wait", "git"),
        ExternalOperation.Http => ("network", "http"),
        _ => ("filesystem", "filesystem")
    };

    public StartupSnapshot ReadStartup()
    {
        lock (_gate) return new(_runId, Array.AsReadOnly(_startup.ToArray()));
    }

    private Activity? Start(string phase, string operation)
    {
        try
        {
            return _source.StartActivity(operation, ActivityKind.Internal, Activity.Current?.Context ?? default,
                tags: new ActivityTagsCollection { ["phase"] = phase, ["operation"] = operation, ["run.id"] = _runId });
        }
        catch { return null; } // A listener cannot replace the application result.
    }

    private void Finish(string phase, string operation, double elapsedMs, bool succeeded, bool startup, Activity? activity)
    {
        var outcome = succeeded ? "completed" : "failed-or-canceled";
        if (startup)
            lock (_gate)
            {
                if (_startup.Count == 64) _startup.Dequeue();
                _startup.Enqueue(new(phase, operation, elapsedMs, outcome));
            }
        try
        {
            activity?.SetTag("outcome", outcome);
            activity?.SetTag("elapsed.ms", elapsedMs);
            activity?.SetStatus(succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            activity?.Dispose();
        }
        catch { /* Best effort, including user-supplied Activity listeners. */ }
        try
        {
            var sink = Volatile.Read(ref _logger);
            if (startup)
                sink.LogInformation("LIVE_TIMING run={RunId} phase={Phase} operation={Operation} elapsedMs={ElapsedMs} outcome={Outcome}",
                    _runId, phase, operation, elapsedMs, outcome);
            else if (elapsedMs > 1000)
                sink.LogWarning("LIVE_SLOW_CALL run={RunId} phase={Phase} operation={Operation} elapsedMs={ElapsedMs} outcome={Outcome}",
                    _runId, phase, operation, elapsedMs, outcome);
        }
        catch { /* Telemetry must not change the measured operation's outcome. */ }
    }

    public void Dispose() => _source.Dispose();

    public sealed class Phase : IDisposable
    {
        private readonly LiveTiming _owner;
        private readonly string _phase;
        private readonly string _operation;
        private readonly bool _startup;
        private readonly long _start = Stopwatch.GetTimestamp();
        private readonly Activity? _activity;
        private int _state; // 0 incomplete, 1 complete, 2 disposed incomplete, 3 disposed complete
        internal Phase(LiveTiming owner, string phase, string operation, bool startup)
        {
            _owner = owner; _phase = phase; _operation = operation; _startup = startup;
            _activity = owner.Start(phase, operation);
        }
        public void Complete() => Interlocked.CompareExchange(ref _state, 1, 0);
        public void Dispose()
        {
            var state = Volatile.Read(ref _state);
            while (state < 2)
            {
                var previous = Interlocked.CompareExchange(ref _state, state + 2, state);
                if (previous == state)
                {
                    _owner.Finish(_phase, _operation, Stopwatch.GetElapsedTime(_start).TotalMilliseconds, state == 1, _startup, _activity);
                    return;
                }
                state = previous;
            }
        }
    }
}
