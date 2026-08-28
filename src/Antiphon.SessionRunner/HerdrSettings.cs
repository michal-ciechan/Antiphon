namespace Antiphon.SessionRunner;

/// <summary>
/// Configuration for the optional, operator-run Herdr backend.  The switch stays off until a
/// later launch-path slice deliberately opts a session into Herdr.
/// </summary>
public sealed class HerdrSettings
{
    /// <summary>Enables callers to contact an operator-run Herdr instance. Defaults off.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Explicit named session. This has the same precedence as Herdr's <c>--session</c> option,
    /// before <c>HERDR_SOCKET_PATH</c> and <c>HERDR_SESSION</c>.
    /// </summary>
    public string? Session { get; set; }

    /// <summary>Bound on opening a named-pipe connection to Herdr.</summary>
    public int ConnectTimeoutMs { get; set; } = 5_000;

    /// <summary>The wire protocol this client was compiled and tested against.</summary>
    public int ExpectedProtocol { get; set; } = 20;

    /// <summary>
    /// CARD-0162: lower bound (seconds) for the event-pump reconnect backoff after a dropped
    /// stream. Doubles up to <see cref="EventsReconnectMaxSeconds"/>.
    /// </summary>
    public int EventsReconnectMinSeconds { get; set; } = 1;

    /// <summary>CARD-0162: upper bound (seconds) for the event-pump reconnect backoff.</summary>
    public int EventsReconnectMaxSeconds { get; set; } = 30;

    /// <summary>
    /// CARD-0187: bound on polling <c>pane.get.agent</c> after typing the launch script, until
    /// herdr's passive detection reports the expected kind. Default 60 s (K1 4.4 s, K5 under 10 s;
    /// CARD-0195's Codex MCP boot is the outlier and is measured by S3/K9).
    /// </summary>
    public int LaunchDetectTimeoutMs { get; set; } = 60_000;

    /// <summary>
    /// CARD-0224: last-pane records older than this many days are pruned during herdr adoption.
    /// Default 7. A last-pane whose pane is unknown at the next launch is deleted then, regardless.
    /// </summary>
    public int LastPaneRetentionDays { get; set; } = 7;

    /// <summary>CARD-0163: display-only transcript-state labels pushed to live herdr panes.</summary>
    public HerdrStatusPushSettings StatusPush { get; set; } = new();
}

public sealed class HerdrStatusPushSettings
{
    public bool Enabled { get; set; } = true;
    public int DebounceMs { get; set; } = 500;
    public int HeartbeatSeconds { get; set; } = 300;
    public int TtlSeconds { get; set; } = 900;
    public int ExitClearTimeoutMs { get; set; } = 2_000;
}
