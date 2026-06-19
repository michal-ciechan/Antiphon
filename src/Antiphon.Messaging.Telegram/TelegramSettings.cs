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
}
