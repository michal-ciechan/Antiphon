namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Config for the channel bridge (external chat ↔ agent routing). The Kafka connection itself is the
/// <c>AntiphonMessaging</c> section (see <c>Antiphon.Messaging.Client.AntiphonMessagingOptions</c>);
/// this gates whether the bridge consumes at all and tunes its behaviour.
/// </summary>
public sealed class ChannelBridgeSettings
{
    public const string SectionName = "ChannelBridge";

    /// <summary>Master switch. Off by default so dev boxes without broker connectivity stay quiet.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long to wait for a bound agent's session to reach Running before enqueuing.</summary>
    public int AgentStartTimeoutSeconds { get; set; } = 90;

    /// <summary>Extra settle time after a session we just started reports Running (TUI boot, MCP connect).</summary>
    public int AgentReadyDelaySeconds { get; set; } = 12;

    /// <summary>Outbound reply truncation (Telegram caps messages at 4096 chars).</summary>
    public int MaxReplyChars { get; set; } = 4000;

    /// <summary>Drop a pending reply correlation if no matching turn completes within this window.</summary>
    public int PendingReplyTtlMinutes { get; set; } = 30;
}
