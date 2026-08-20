using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The <c>{{key:NAME}}</c> placeholder: how it is spelled, where it is legal, and the tripwire that
/// refuses one anywhere it survived to a real process launch (CARD-0106 S2).
///
/// <para><b>Legal in environment VALUES only.</b> Not arguments, not <c>--append-system-prompt</c>
/// text, not a brief. Arguments are visible to any process lister, are quoted into failure reasons
/// and argv-integrity tests, and system-prompt text additionally lands in transcripts — a secret in
/// either is a secret published. That rule is ENFORCED here, not documented: a placeholder found in
/// an argument is refused rather than silently stripped, because silently stripping it would launch
/// an agent whose instructions lost a line nobody was told about.</para>
///
/// <para><b>Detection is deliberately LOOSER than resolution.</b> The resolver replaces well-formed
/// tokens; the tripwire refuses anything still carrying the <c>{{key:</c> marker. A malformed name —
/// <c>{{key:has space}}</c> — matches the marker but not the token, so a strict-only tripwire would
/// export it to a child process as literal text. Loud beats literal. The cost is the documented v1
/// limitation: a value that genuinely contains those six characters fails its launch by name rather
/// than passing through, and there is no escape syntax (plan section 3).</para>
/// </summary>
public static class ApiKeyPlaceholder
{
    /// <summary>The six characters that make a value this feature's business at all.</summary>
    public const string Marker = "{{key:";

    /// <summary>
    /// A well-formed token. The name charset is <see cref="ApiKeyNaming.CharacterClass"/>, so
    /// anything storable is spellable and vice versa.
    /// </summary>
    public static readonly Regex Token = new(
        $@"\{{\{{key:({ApiKeyNaming.CharacterClass}+)\}}\}}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when the text carries the marker at all — well-formed or not.</summary>
    public static bool ContainsMarker(string? text) =>
        text is not null && text.Contains(Marker, StringComparison.Ordinal);

    /// <summary>The distinct key names a value references, in first-appearance order.</summary>
    public static IReadOnlyList<string> Names(string? text)
    {
        if (!ContainsMarker(text))
            return [];

        var names = new List<string>();
        foreach (Match match in Token.Matches(text!))
        {
            var name = match.Groups[1].Value;
            if (!names.Contains(name, StringComparer.Ordinal))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Every marker occurrence in the text that is NOT the start of a well-formed token. A non-empty
    /// result is a typo an operator can fix, and it is checked against the ORIGINAL value rather
    /// than the substituted one so a resolved secret that happens to contain the marker cannot be
    /// mistaken for one.
    /// </summary>
    public static bool HasMalformedToken(string? text)
    {
        if (!ContainsMarker(text))
            return false;

        var starts = Token.Matches(text!).Select(m => m.Index).ToHashSet();
        for (var i = text!.IndexOf(Marker, StringComparison.Ordinal);
             i >= 0;
             i = text.IndexOf(Marker, i + 1, StringComparison.Ordinal))
        {
            if (!starts.Contains(i))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Refuses a placeholder in text that is not an environment value — an argument, an appended
    /// system prompt. Throws naming the subject and the token; the text itself is never quoted,
    /// because the thing it might be carrying is the thing this rule exists to keep out of messages.
    /// </summary>
    public static void EnsureAbsent(string? text, string subject)
    {
        if (!ContainsMarker(text))
            return;

        throw new InvalidOperationException(
            $"{subject} contains an API key placeholder ({DescribeTokens(text!)}). "
            + "Placeholders are supported in environment VALUES only — arguments and "
            + "system-prompt text are visible to any process lister and are quoted into logs, "
            + "failure reasons and transcripts, so a key resolved into one would be a key "
            + "published. Put the placeholder in the agent's launch environment instead.");
    }

    /// <summary>
    /// THE TRIPWIRE (plan section 4). Called from <c>AgentSessionService.BuildRuntimeLaunchSpec</c> —
    /// the single method all three <c>adapter.StartAsync</c> sites pass through — so a launch path
    /// that forgets to resolve fails its FIRST launch naming the surviving token, instead of
    /// exporting the literal <c>{{key:...}}</c> string into a real process where the only symptom is
    /// an agent that authenticates as nobody.
    ///
    /// <para>It refuses; it does not resolve. There is no database here on purpose: a tripwire that
    /// could fix the problem would stop being evidence that a path is missing its resolution, and
    /// the next forgotten path would go unnoticed for exactly as long as this one would have.</para>
    /// </summary>
    public static void EnsureResolved(AgentLaunchSpec spec, Guid? sessionId)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var subject = sessionId is { } id
            ? $"the launch spec for session {id:D}"
            : "the launch spec";

        foreach (var (name, value) in spec.Env)
        {
            if (!ContainsMarker(value))
                continue;

            // The variable NAME and the tokens; never the value, which by this point may be a
            // partially-resolved string carrying a real secret alongside an unresolved token.
            throw new InvalidOperationException(
                $"{subject} still carries an unresolved API key placeholder in environment "
                + $"variable '{name}' ({DescribeTokens(value)}). A launch path that builds an Env "
                + "must run it through ApiKeyEnvResolver; nothing downstream of here can, and "
                + "exporting the literal token to the child process would leave the agent "
                + "authenticating as nobody with no error anywhere.");
        }

        for (var i = 0; i < spec.Args.Count; i++)
        {
            if (ContainsMarker(spec.Args[i]))
                EnsureAbsent(spec.Args[i], $"{subject}, argument {i}");
        }
    }

    private static string DescribeTokens(string text)
    {
        var names = Names(text);
        if (names.Count > 0)
            return string.Join(", ", names.Select(n => $"{Marker}{n}}}}}"));
        // Marker present, no well-formed token: a malformed name. Naming the marker is all we can
        // honestly say without quoting text that may carry a value.
        return $"a malformed '{Marker}...}}}}' token";
    }
}
