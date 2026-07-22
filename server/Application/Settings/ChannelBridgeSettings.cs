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

    /// <summary>
    /// Inbound debounce: sliding quiet window per (conversation, sender) before routing buffered
    /// messages, merged newline-joined into one prompt. 0 = passthrough (route each message inline,
    /// exactly the pre-debounce behaviour).
    /// </summary>
    public int DebounceWindowMs { get; set; } = 500;

    /// <summary>Hard cap from the FIRST buffered message — continuous typing can't defer forever.</summary>
    public int DebounceMaxMs { get; set; } = 2000;

    /// <summary>
    /// Coalesce a contiguous run of same-conversation Channel messages into ONE batched delivery
    /// per turn (context + current markers). Off = one message per turn (pre-epic behaviour).
    /// </summary>
    public bool BatchingEnabled { get; set; } = true;
}
