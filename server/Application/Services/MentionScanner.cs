using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

public sealed partial class MentionScanner
{
    private const int MaxMessageLength = 1000;

    public IReadOnlyList<AgentMention> Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var stripped = StripAnsi(text);
        var mentions = new List<AgentMention>();
        foreach (Match match in MentionRegex().Matches(stripped))
        {
            var target = match.Groups["target"].Value.Trim();
            if (string.IsNullOrWhiteSpace(target))
                continue;

            var message = match.Groups["message"].Success
                ? match.Groups["message"].Value.Trim()
                : string.Empty;
            if (message.Length > MaxMessageLength)
                message = message[..MaxMessageLength];

            mentions.Add(new AgentMention(target, message));
        }

        return mentions;
    }

    public static string StripAnsi(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var withoutOsc = OscRegex().Replace(text, string.Empty);
        return CsiRegex().Replace(withoutOsc, string.Empty);
    }

    [GeneratedRegex(@"\x1B\][^\a]*(?:\a|\x1B\\)", RegexOptions.Compiled)]
    private static partial Regex OscRegex();

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex CsiRegex();

    [GeneratedRegex(@"(?<![\w.-])@(?<target>[A-Za-z0-9][A-Za-z0-9_.-]{1,63})(?:[ \t:,-]+(?<message>[^\r\n]+))?", RegexOptions.Compiled)]
    private static partial Regex MentionRegex();
}

public sealed record AgentMention(string Target, string Message);
