using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.SessionRunner;

/// <summary>Instance-owned, best-effort milestones. Never decides session fate or readiness.</summary>
public sealed class RunnerStartupDiagnostics
{
    private readonly ILogger _logger;
    private readonly string _attemptId;
    private readonly int? _supervisorPid;
    private readonly string? _supervisorStart;
    private readonly int _pid = Environment.ProcessId;
    private readonly string _processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O");

    public RunnerStartupDiagnostics(ILogger logger, string? attemptId = null, int? supervisorPid = null, string? supervisorStart = null)
    {
        _logger = logger;
        _attemptId = Guid.TryParse(attemptId, out var id) ? id.ToString("N") : Guid.NewGuid().ToString("N");
        _supervisorPid = supervisorPid;
        _supervisorStart = DateTimeOffset.TryParse(supervisorStart, out var time) ? time.UtcDateTime.ToString("O") : null;
    }

    public void Record(string name, double? elapsedMs = null, string? outcome = null, int? count = null, DateTime? observedUtc = null, Guid? sessionId = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                version = 1, @event = name, utc = (observedUtc ?? DateTime.UtcNow).ToString("O"), producer = "runner",
                producerPid = _pid, producerStartTimeUtc = _processStart, attemptId = _attemptId,
                supervisorPid = _supervisorPid, supervisorStartTimeUtc = _supervisorStart,
                elapsedMs, outcome, count, sessionId
            });
            _logger.LogInformation("ANTIPHON_STARTUP {Milestone}", json);
        }
        catch { /* A failing instrumentation sink cannot affect the measured operation. */ }
    }

    public Phase Begin(string name, Guid? sessionId = null) => new(this, name, sessionId);

    public sealed class Phase : IDisposable
    {
        private readonly RunnerStartupDiagnostics _owner;
        private readonly string _name;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private string _outcome = "failed-or-canceled";
        private int? _count;
        private readonly Guid? _sessionId;
        internal Phase(RunnerStartupDiagnostics owner, string name, Guid? sessionId)
        { _owner = owner; _name = name; _sessionId = sessionId; owner.Record(name + "-start", sessionId: sessionId); }
        public void Complete(int? count = null, string outcome = "completed") { _outcome = outcome; _count = count; }
        public void Dispose() => _owner.Record(_name + "-end", _clock.Elapsed.TotalMilliseconds, _outcome, _count, sessionId: _sessionId);
    }
}
