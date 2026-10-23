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
    public Phase BeginStartup(StartupOperation operation) => new();
    public Phase BeginExternal(ExternalOperation operation) => new();
    public void RecordExternal(ExternalOperation operation, TimeSpan duration, bool succeeded) { }
    public StartupSnapshot ReadStartup() => new("", []);
    public void Dispose() { }
    public sealed class Phase : IDisposable
    {
        public void Complete() { }
        public void Dispose() { }
    }
}
