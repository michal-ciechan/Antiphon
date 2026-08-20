namespace Antiphon.Messaging.Slack;

/// <summary>
/// Typed settings for the Slack adapter (bound via <c>IOptions</c> at the composition root),
/// mirroring <c>TelegramSettings</c> member-for-member wherever the concept transfers.
///
/// Slack needs TWO credentials, not one: an app-level token (<c>xapp-…</c>, scope
/// <c>connections:write</c>) that opens the Socket Mode WebSocket, and a bot token
/// (<c>xoxb-…</c>) that authorizes every Web API call. See docs/slack-bot-ops.md.
/// </summary>
public sealed class SlackSettings
{
    public const string SectionName = "Slack";

    /// <summary>Bot user OAuth token (<c>xoxb-…</c>) for the Web API. Never commit — supply via env/user-secrets.</summary>
    public string BotToken { get; set; } = "";

    /// <summary>App-level token (<c>xapp-…</c>, scope <c>connections:write</c>) used ONLY by
    /// <c>apps.connections.open</c> to obtain the Socket Mode WebSocket URL.</summary>
    public string AppToken { get; set; } = "";

    public string ApiBaseUrl { get; set; } = "https://slack.com/api";

    /// <summary>When non-empty, only events from these conversation ids (<c>C…</c>/<c>G…</c>/<c>D…</c>)
    /// are emitted (fail-closed allowlist, the twin of <c>Telegram__AllowedChatIds</c>).</summary>
    public string[] AllowedConversationIds { get; set; } = [];

    /// <summary>
    /// Our own bot USER id (<c>U…</c>), used for the <c>IsSelf</c> echo guard and <c>IsMe</c> on
    /// mentions. Left empty it is resolved once at startup via <c>auth.test</c>; set it explicitly
    /// to make the guard work even when <c>auth.test</c> is unreachable.
    /// </summary>
    public string? BotUserId { get; set; }

    /// <summary>Backoff before retrying after a transient failure that carries no <c>Retry-After</c>
    /// (socket death, <c>apps.connections.open</c> failure, 5xx). Prevents tight-looping.</summary>
    public int ErrorBackoffSeconds { get; set; } = 3;

    /// <summary>Upper bound on how long we'll honor Slack's <c>Retry-After</c>, so a hostile/huge
    /// value can't stall the loop indefinitely.</summary>
    public int MaxRetryAfterSeconds { get; set; } = 60;

    /// <summary>Extra attempts for an outbound send when the failure looks transient
    /// (429/5xx/network/<c>ratelimited</c>). The outbound consumer auto-commits, so without this a
    /// transient blip silently drops the reply. 0 disables.</summary>
    public int SendRetryAttempts { get; set; } = 2;

    /// <summary>
    /// WebSocket keepalive ping interval. .NET pairs this with <see cref="KeepAliveTimeoutSeconds"/>
    /// to fail a half-open connection instead of blocking in <c>ReceiveAsync</c> forever — the
    /// stalled-connection hazard the receive loop cannot otherwise see.
    /// </summary>
    public int KeepAliveSeconds { get; set; } = 30;

    /// <summary>How long to wait for a pong before aborting the socket (0 disables the timeout).</summary>
    public int KeepAliveTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Outbound text formatting. <c>"Markdown"</c> (default) renders the reply's Markdown to Slack
    /// mrkdwn (<see cref="SlackMrkdwnRenderer"/>); <c>"Plain"</c> sends the raw text untouched.
    /// A reply whose <c>RawOverrides</c> carry <c>text</c> is never converted.
    /// </summary>
    public string Formatting { get; set; } = "Markdown";

    /// <summary>
    /// Largest inbound attachment the adapter downloads (via <c>url_private_download</c>) and inlines
    /// into <see cref="Antiphon.Messaging.Attachment.Content"/>. Bounded by the 20 MB bus message cap
    /// after base64 (hence 14 MB raw). Bigger files keep metadata only. 0 disables inbound downloads.
    /// </summary>
    public long MaxInlineAttachmentBytes { get; set; } = 14 * 1024 * 1024;
}
