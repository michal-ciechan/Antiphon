using System.Text.RegularExpressions;

namespace Antiphon.Messaging.Slack;

/// <summary>
/// Turns Slack's wire text into the plain, human-readable <see cref="ChannelMessage.Text"/> the
/// prompt envelope shows an agent, and extracts <see cref="Mention"/>s while doing it.
///
/// Slack does not send plain text: it sends entity-escaped text (<c>&amp;amp;</c>/<c>&amp;lt;</c>/<c>&amp;gt;</c>)
/// with angle-bracket tokens spliced in — <c>&lt;@U012ABC&gt;</c> for a user, <c>&lt;#C012AB|general&gt;</c>
/// for a channel, <c>&lt;!here&gt;</c> for a broadcast, <c>&lt;https://x|label&gt;</c> for a link. Raw
/// <c>&lt;@U0123ABCD&gt;</c> in an agent's prompt is noise, so tokens are rewritten to their readable form.
///
/// Order matters and is the whole reason this is one pass: tokens are delimited by RAW angle
/// brackets while their contents are entity-escaped, so the tokens must be consumed BEFORE the
/// entities are unescaped. Unescaping first would let a message containing the literal text
/// "&amp;lt;@U123&amp;gt;" forge a mention.
/// </summary>
public static partial class SlackTextNormalizer
{
    /// <param name="raw">The event's <c>text</c> field, exactly as Slack sent it.</param>
    /// <param name="resolveUserName">Best-effort display-name lookup for a <c>U…</c> id; null keeps the id.</param>
    /// <param name="botUserId">Our own bot user id, so a mention of us gets <see cref="Mention.IsMe"/>.</param>
    public static (string? Text, IReadOnlyList<Mention> Mentions) Normalize(
        string? raw, Func<string, string?>? resolveUserName = null, string? botUserId = null)
    {
        if (string.IsNullOrEmpty(raw))
            return (raw, []);

        var mentions = new List<Mention>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var rewritten = TokenRegex().Replace(raw, match =>
        {
            var body = match.Groups["body"].Value;
            if (body.Length == 0)
                return match.Value;

            var pipe = body.IndexOf('|');
            var head = pipe >= 0 ? body[..pipe] : body;
            var label = pipe >= 0 ? body[(pipe + 1)..] : null;

            // Token bodies are entity-escaped like the rest of the text, so nothing is unescaped
            // here — the single pass at the end does it exactly once for replacements and prose alike.
            switch (head[0])
            {
                case '@':
                {
                    var id = head[1..];
                    if (id.Length == 0)
                        return match.Value;
                    var name = label ?? resolveUserName?.Invoke(id);
                    if (seen.Add(id))
                        mentions.Add(new Mention
                        {
                            Id = id,
                            DisplayName = name,
                            IsMe = botUserId is { Length: > 0 } me && string.Equals(id, me, StringComparison.Ordinal),
                        });
                    return "@" + (string.IsNullOrEmpty(name) ? id : name);
                }

                case '#':
                    return "#" + (label ?? head[1..]);

                // <!here>, <!channel>, <!everyone>, <!subteam^S123|@team>, <!date^…|fallback>
                case '!':
                    return label is { Length: > 0 }
                        ? label
                        : "@" + head[1..];

                default:
                    // A link: <url> or <url|label>. The label is the readable half when present.
                    return label ?? head;
            }
        });

        return (Unescape(rewritten), mentions);
    }

    /// <summary>
    /// The <c>U…</c> ids mentioned in <paramref name="raw"/>, in order, deduplicated. The adapter
    /// pre-resolves these (one cached <c>users.info</c> each) so <see cref="Normalize"/> can stay
    /// synchronous and still render <c>@name</c> instead of <c>&lt;@U0123ABCD&gt;</c>.
    /// </summary>
    public static IReadOnlyList<string> MentionedUserIds(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return [];

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in TokenRegex().Matches(raw))
        {
            var body = match.Groups["body"].Value;
            if (body.Length < 2 || body[0] != '@')
                continue;
            var pipe = body.IndexOf('|');
            var id = pipe >= 0 ? body[1..pipe] : body[1..];
            if (id.Length > 0 && seen.Add(id))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>Reverses the only three entities Slack escapes in message text.</summary>
    public static string Unescape(string s) => s
        .Replace("&lt;", "<", StringComparison.Ordinal)
        .Replace("&gt;", ">", StringComparison.Ordinal)
        .Replace("&amp;", "&", StringComparison.Ordinal);

    // Angle-bracket tokens never nest and never contain a raw '<' or '>' (those arrive escaped).
    [GeneratedRegex(@"<(?<body>[^<>]*)>")]
    private static partial Regex TokenRegex();
}
