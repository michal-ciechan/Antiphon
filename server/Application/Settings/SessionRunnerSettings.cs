namespace Antiphon.Server.Application.Settings;

public sealed class SessionRunnerSettings
{
    public string BaseUrl { get; set; } = "http://localhost:17283";
    public bool Enabled { get; set; } = true;
    public int EventReconnectDelayMs { get; set; } = 1000;

    /// <summary>Timeout for ordinary request/response calls to the runner (HttpClient.Timeout).</summary>
    public int RequestTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// /events idle watchdog: reconnect if NOTHING (event or keepalive comment) arrives for this
    /// long. The stream itself runs with an infinite HttpClient timeout — the default 100 s
    /// Timeout used to tear the SSE stream down every 100 s and lose events in the reconnect gaps
    /// (2026-07-23). Must comfortably exceed the runner's Events:KeepAliveSeconds (default 15 s).
    /// </summary>
    public int EventStreamIdleTimeoutSeconds { get; set; } = 90;
}
