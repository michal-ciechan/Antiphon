namespace Antiphon.Server.Application.Settings;

public sealed class SessionRunnerSettings
{
    public string BaseUrl { get; set; } = "http://localhost:17204";
    public bool Enabled { get; set; } = true;
    public int EventReconnectDelayMs { get; set; } = 1000;

    /// <summary>Timeout for ordinary request/response calls to the runner (HttpClient.Timeout).</summary>
    public int RequestTimeoutSeconds { get; set; } = 100;

    /// <summary>Tighter deadline for the agent-list's read-only runner state lookup.</summary>
    public int ListTimeoutSeconds { get; set; } = 3;

    /// <summary>
    /// /events idle watchdog: reconnect after genuine silence of this many seconds. The deadline
    /// resets on every received line, including unrecognized comments and unsupported frames, so
    /// that traffic keeps the connection alive. It is not a readiness deadline: a stream that
    /// never emits a qualifying frame is not reconnected merely because readiness is absent. The
    /// stream itself runs with an infinite HttpClient timeout — the default 100 s Timeout used to
    /// tear the SSE stream down every 100 s and lose events in the reconnect gaps (2026-07-23).
    /// Must comfortably exceed the runner's Events:KeepAliveSeconds (default 15 s).
    /// </summary>
    public int EventStreamIdleTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// CARD-1153 D-3: direct-HTTP absence evidence. <c>SessionRunner:AbsenceEvidence:KeyPath</c>
    /// names an owner-readable key file under configuration custody; missing means this transport
    /// cannot certify (launch and failure semantics are unchanged).
    /// </summary>
    public SessionRunnerAbsenceEvidenceSettings AbsenceEvidence { get; set; } = new();
}

public sealed class SessionRunnerAbsenceEvidenceSettings
{
    public string? KeyPath { get; set; }
}
