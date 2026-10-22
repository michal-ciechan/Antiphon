using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

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

    /// <summary>Age of an unowned inbound before a known supervision refusal raises a channel incident.</summary>
    public int AgedHoldIncidentMinutes { get; set; } = 10;

    /// <summary>Extra settle time after a session we just started reports Running (TUI boot, MCP connect).</summary>
    public int AgentReadyDelaySeconds { get; set; } = 12;

    /// <summary>Outbound reply truncation (Telegram caps messages at 4096 chars).</summary>
    public int MaxReplyChars { get; set; } = 4000;

    /// <summary>Drop a pending reply correlation if no matching turn completes within this window.</summary>
    public int PendingReplyTtlMinutes { get; set; } = 30;

    /// <summary>
    /// CARD-1434: a channel debt row (a reply whose publication keeps failing) is abandoned once it
    /// has failed this many attempts; its open correlations are settled with a Critical
    /// <c>ChannelReplyLost</c> (<c>PublishFailed</c>) and later replies to that chat flow. 0 disables
    /// the attempt bound; <see cref="PendingReplyTtlMinutes"/> remains the backstop.
    /// </summary>
    public int ReplyDebtGiveUpAttempts { get; set; } = 20;

    /// <summary>
    /// CARD-1434: a channel debt row that has failed at least once is abandoned once this many
    /// minutes have passed since it was created (the time it first waited or failed). 0 disables
    /// the time bound; must not exceed <see cref="PendingReplyTtlMinutes"/>.
    /// </summary>
    public int ReplyDebtGiveUpMinutes { get; set; } = 10;

    /// <summary>
    /// Max raw bytes for a single reply attachment. Attachments ride the Kafka message base64-encoded
    /// under the bus-wide 20 MB message cap, so the raw ceiling is 3/4 of that minus envelope slack.
    /// Larger files are skipped with a note in the reply text.
    /// </summary>
    public long MaxAttachmentBytes { get; set; } = 14 * 1024 * 1024;

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

    /// <summary>
    /// CARD-0338: machine-triggered turn origins whose plain text is delivered as a follow-up
    /// <c>ChannelReply</c> (opt out with exact <c>NO_REPLY</c>). Empty = attachments only (CARD-0250).
    /// <see cref="QueuedMessageOrigin.Channel"/>, <see cref="QueuedMessageOrigin.Ui"/> and
    /// <see cref="QueuedMessageOrigin.Supervision"/> are rejected — the main path owns Channel,
    /// and operator/supervision turns must stay silent. <see cref="QueuedMessageOrigin.System"/>
    /// is accepted if listed; the default excludes it so READY/housekeeping never reach a chat.
    /// </summary>
    public List<QueuedMessageOrigin> MachineTurnTextOrigins { get; set; } =
    [
        QueuedMessageOrigin.Delegation,
        QueuedMessageOrigin.Check,
        QueuedMessageOrigin.Scheduled,
    ];
}

public sealed class ChannelBridgeSettingsValidator : IValidateOptions<ChannelBridgeSettings>
{
    private static readonly QueuedMessageOrigin[] Forbidden =
    [
        QueuedMessageOrigin.Channel,
        QueuedMessageOrigin.Ui,
        QueuedMessageOrigin.Supervision,
    ];

    public ValidateOptionsResult Validate(string? name, ChannelBridgeSettings settings)
    {
        var failures = new List<string>();
        // CARD-1434: the give-up bounds; 0 disables a bound, and the time bound cannot outlive
        // the TTL backstop that would otherwise report the same reply first.
        if (settings.ReplyDebtGiveUpAttempts is < 0 or > 1000)
            failures.Add("ChannelBridge:ReplyDebtGiveUpAttempts must be 0 (disabled) or 1..1000 "
                + $"(got {settings.ReplyDebtGiveUpAttempts}).");
        if (settings.ReplyDebtGiveUpMinutes < 0 || settings.ReplyDebtGiveUpMinutes > settings.PendingReplyTtlMinutes)
            failures.Add("ChannelBridge:ReplyDebtGiveUpMinutes must be 0 (disabled) or at most "
                + $"PendingReplyTtlMinutes ({settings.PendingReplyTtlMinutes}); got {settings.ReplyDebtGiveUpMinutes}. "
                + "The TTL remains the backstop for a reply that is never published.");
        var rejected = (settings.MachineTurnTextOrigins ?? []).Where(o => Forbidden.Contains(o)).Distinct().ToList();
        if (rejected.Count > 0)
            failures.Add("ChannelBridge:MachineTurnTextOrigins must not include Channel, Ui, or Supervision "
                + $"(got {string.Join(", ", rejected)}). The main path owns Channel; operator and "
                + "supervision turns stay silent. Leave the list empty for attachments-only.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
