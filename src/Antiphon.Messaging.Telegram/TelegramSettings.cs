namespace Antiphon.Messaging.Telegram;

/// <summary>Typed settings for the Telegram adapter (bound via <c>IOptions</c> at the composition root).</summary>
public sealed class TelegramSettings
{
    public const string SectionName = "Telegram";

    /// <summary>Bot API token from @BotFather. Never commit — supply via env/user-secrets.</summary>
    public string BotToken { get; set; } = "";

    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>When non-empty, only updates from these chat ids are emitted (allowlist).</summary>
    public long[] AllowedChatIds { get; set; } = [];

    /// <summary>Optional bot @username (without the @) so self-mentions can be flagged.</summary>
    public string? BotUsername { get; set; }

    public int LongPollTimeoutSeconds { get; set; } = 30;

    /// <summary>Backoff before retrying after a transient failure that carries no <c>retry_after</c>
    /// (network error, 5xx, or an <c>ok:false</c> like 409/401). Prevents tight-looping on getUpdates.</summary>
    public int ErrorBackoffSeconds { get; set; } = 3;

    /// <summary>Upper bound on how long we'll honor Telegram's <c>retry_after</c>, so a hostile/huge value
    /// can't stall the loop indefinitely.</summary>
    public int MaxRetryAfterSeconds { get; set; } = 60;

    /// <summary>Extra attempts for an outbound sendMessage when the failure looks transient (429/5xx/network).
    /// The consumer auto-commits, so without this a transient blip silently drops the reply. 0 disables.</summary>
    public int SendRetryAttempts { get; set; } = 2;
}
